using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper.HotkeyIpc;

/// <summary>
/// 热键 IPC 客户端 - 子进程使用 (基于 TCP Socket)
/// </summary>
public class HotkeyIpcClient : IDisposable
{
    private const int DefaultPort = 52718;
    private const int PortRange = 10;
    private const int ConnectTimeoutMs = 2000; // 增加超时时间，确保有足够时间完成握手
    private const int HeartbeatIntervalMs = 5000;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private bool _disposed;
    private bool _handshakeCompleted;  // 新增：标记握手是否完成
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string ClientId { get; }
    /// <summary>
    /// 检查是否已连接并完成握手。只有在 TCP 连接成功且收到 ConnectAck 后才返回 true。
    /// </summary>
    public bool IsConnected => _tcpClient?.Connected == true && _handshakeCompleted;

    public event Action<HotkeyIdentifier>? HotkeyTriggered;
    public event Action? Disconnected;
    public event Action<int>? PrimaryChanged;

    public HotkeyIpcClient()
    {
        ClientId = $"{Environment.ProcessId}_{Guid.NewGuid():N}";
    }

    public async Task<bool> ConnectAsync()
    {
        if (IsConnected) return true;

        try
        {
            _cts = new CancellationTokenSource();

            // 首先尝试从文件读取端口
            int? savedPort = HotkeyIpcServer.LoadPortFromFile();
            LoggerHelper.Info($"HotkeyIpcClient: 端口文件读取结果 = {savedPort?.ToString() ?? "null"}");
            if (savedPort.HasValue)
            {
                LoggerHelper.Info($"HotkeyIpcClient: 尝试连接文件中的端口 {savedPort.Value}");
                var result = await TryConnectToPort(savedPort.Value);
                LoggerHelper.Info($"HotkeyIpcClient: 文件端口 {savedPort.Value} 连接结果 = {result}");
                if (result)
                    return true;
            }

            // 如果文件中的端口不可用，扫描端口范围
            LoggerHelper.Info($"HotkeyIpcClient: 扫描端口范围 {DefaultPort}-{DefaultPort + PortRange - 1}");
            for (int i = 0; i < PortRange; i++)
            {
                int port = DefaultPort + i;
                // 如果已经尝试过文件中的端口，跳过
                if (savedPort.HasValue && port == savedPort.Value)
                    continue;
                if (await TryConnectToPort(port))
                    return true;
            }

            LoggerHelper.Warning("HotkeyIpcClient: 无法连接到任何端口");Cleanup();
            return false;
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"HotkeyIpcClient: 连接失败 - {ex.GetType().Name}: {ex.Message}");
            Cleanup();
            return false;
        }
    }

    private async Task<bool> TryConnectToPort(int port)
    {
        TcpClient? tcpClient = null;
        try
        {
            LoggerHelper.Info($"HotkeyIpcClient: 尝试连接端口 {port}...");
            
            tcpClient = new TcpClient();
            tcpClient.NoDelay = true;

            using var connectCts = new CancellationTokenSource(ConnectTimeoutMs);
            await tcpClient.ConnectAsync("127.0.0.1", port, connectCts.Token);

            if (!tcpClient.Connected)
            {
                LoggerHelper.Info($"HotkeyIpcClient: 端口 {port} 连接后状态为未连接");
                tcpClient.Dispose();
                return false;
            }

            LoggerHelper.Info($"HotkeyIpcClient: TCP 连接成功，端口 {port}");
            _tcpClient = tcpClient;
            tcpClient = null; // 防止 finally 中被释放
            _stream = _tcpClient.GetStream();
            _reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
            _writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            // 发送连接请求
            var connectMsg = HotkeyMessage.CreateConnect(ClientId);
            LoggerHelper.Info($"HotkeyIpcClient: 发送连接请求");
            await WriteLineAsync(connectMsg.SerializeToJson());

            // 等待连接确认（带超时）
            LoggerHelper.Info($"HotkeyIpcClient: 等待连接确认...");
            using var readCts = new CancellationTokenSource(ConnectTimeoutMs);
            var response = await _reader.ReadLineAsync(readCts.Token);
            
            if (string.IsNullOrEmpty(response))
            {
                LoggerHelper.Warning($"HotkeyIpcClient: 连接确认响应为空");
                CleanupConnection();
                return false;
            }

            LoggerHelper.Info($"HotkeyIpcClient: 收到响应 - {response}");
            var ack = HotkeyMessage.Deserialize(response);
            if (ack?.Type != HotkeyMessageType.ConnectAck)
            {
                LoggerHelper.Warning($"HotkeyIpcClient: 连接确认类型错误 - {ack?.Type}");
                CleanupConnection();
                return false;
            }

            // 标记握手完成
            _handshakeCompleted = true;
            
            // 启动接收和心跳任务
            _receiveTask = Task.Run(ReceiveLoopAsync);
            _heartbeatTask = Task.Run(HeartbeatLoopAsync);

            LoggerHelper.Info($"HotkeyIpcClient: 已连接到主进程，端口={port}, ClientId={ClientId}");
            return true;
        }
        catch (OperationCanceledException)
        {
            LoggerHelper.Info($"HotkeyIpcClient: 连接端口 {port} 超时");
            tcpClient?.Dispose();
            return false;
        }
        catch (SocketException ex)
        {
            LoggerHelper.Info($"HotkeyIpcClient: 端口 {port} 连接被拒绝 - {ex.SocketErrorCode}");
            tcpClient?.Dispose();
            return false;
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"HotkeyIpcClient: 连接端口 {port} 失败 - {ex.GetType().Name}: {ex.Message}");
            tcpClient?.Dispose();
            return false;
        }
    }

    private async Task WriteLineAsync(string message)
    {
        if (_writer == null) return;
        await _writeLock.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(message);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_cts!.Token.IsCancellationRequested && _reader != null && IsConnected)
            {
                string? line;
                try
                {
                    line = await _reader.ReadLineAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }

                if (string.IsNullOrEmpty(line))
                {
                    LoggerHelper.Debug("HotkeyIpcClient: 收到空消息，连接断开");
                    break;
                }

                LoggerHelper.Debug($"HotkeyIpcClient: 收到消息 - {line}");
                var msg = HotkeyMessage.Deserialize(line);
                if (msg == null) continue;

                switch (msg.Type)
                {
                    case HotkeyMessageType.HotkeyTriggered when msg.Hotkey != null:
                        LoggerHelper.Info($"HotkeyIpcClient: 收到热键触发 - {msg.Hotkey}");
                        HotkeyTriggered?.Invoke(msg.Hotkey);
                        break;
                    case HotkeyMessageType.PrimaryChanged:
                        PrimaryChanged?.Invoke(msg.NewPrimaryId);
                        break;
                    case HotkeyMessageType.HeartbeatAck:
                        // 心跳响应，连接正常
                        LoggerHelper.Debug("HotkeyIpcClient: 收到心跳响应");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"HotkeyIpcClient: 接收异常 - {ex.Message}");
        }
        finally
        {
            LoggerHelper.Info("HotkeyIpcClient: 接收循环结束，触发断开事件");
            Disconnected?.Invoke();
        }
    }

    private async Task HeartbeatLoopAsync()
    {
        try
        {
            while (!_cts!.Token.IsCancellationRequested && IsConnected)
            {
                await Task.Delay(HeartbeatIntervalMs, _cts.Token);
                if (_writer != null && IsConnected)
                {
                    var heartbeat = HotkeyMessage.CreateHeartbeat();
                    await WriteLineAsync(heartbeat.SerializeToJson());
                    LoggerHelper.Debug("HotkeyIpcClient: 发送心跳");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"HotkeyIpcClient: 心跳异常 - {ex.Message}");
        }
    }

    public async Task RegisterHotkeyAsync(HotkeyIdentifier hotkey)
    {
        if (!IsConnected || _writer == null) return;
        try
        {
            var msg = HotkeyMessage.CreateRegister(hotkey.KeyCode, hotkey.Modifiers);
            await WriteLineAsync(msg.SerializeToJson());
            LoggerHelper.Info($"HotkeyIpcClient: 注册热键 {hotkey}");
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"HotkeyIpcClient: 注册热键失败 - {ex.Message}");
        }
    }

    public async Task UnregisterHotkeyAsync(HotkeyIdentifier hotkey)
    {
        if (!IsConnected || _writer == null) return;
        try
        {
            var msg = HotkeyMessage.CreateUnregister(hotkey.KeyCode, hotkey.Modifiers);
            await WriteLineAsync(msg.SerializeToJson());
            LoggerHelper.Info($"HotkeyIpcClient: 注销热键 {hotkey}");
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"HotkeyIpcClient: 注销热键失败 - {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        if (!IsConnected || _writer == null) return;
        try
        {
            var msg = HotkeyMessage.Create(HotkeyMessageType.Disconnect);
            await WriteLineAsync(msg.SerializeToJson());
        }
        catch { }
        finally
        {
            Cleanup();
        }
    }

    private void CleanupConnection()
    {
        _handshakeCompleted = false;  // 重置握手状态
        _reader?.Dispose();
        _writer?.Dispose();
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _reader = null;
        _writer = null;
        _stream = null;
        _tcpClient = null;
    }

    private void Cleanup()
    {
        _cts?.Cancel();
        CleanupConnection();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cleanup();
        _cts?.Dispose();
        _writeLock.Dispose();
    }
}