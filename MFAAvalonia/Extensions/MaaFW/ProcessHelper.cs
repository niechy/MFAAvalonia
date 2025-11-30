using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 进程管理辅助类
/// </summary>
public static class ProcessHelper
{
    #region ADB 进程管理

    /// <summary>
    /// 重启 ADB 服务
    /// </summary>
    public static async Task RestartAdbAsync(string adbPath)
    {
        if (string.IsNullOrEmpty(adbPath))
            return;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = MFAExtensions.GetFallbackCommand(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,};

        var process = new Process { StartInfo = processStartInfo };
        process.Start();
        await process.StandardInput.WriteLineAsync($"{adbPath} kill-server");
        await process.StandardInput.WriteLineAsync($"{adbPath} start-server");
        await process.StandardInput.WriteLineAsync("exit");
        await process.WaitForExitAsync();
    }

    /// <summary>
    /// 通过 ADB 重新连接
    /// </summary>
    public static async Task ReconnectByAdbAsync(string adbPath, string address)
    {
        if (string.IsNullOrEmpty(adbPath) || adbPath == "adb")
            return;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = MFAExtensions.GetFallbackCommand(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        var process = new Process { StartInfo = processStartInfo };
        process.Start();
        await process.StandardInput.WriteLineAsync($"{adbPath} disconnect {address}");
        await process.StandardInput.WriteLineAsync("exit");
        await process.WaitForExitAsync();
    }

    /// <summary>
    /// 硬重启 ADB（终止所有 ADB 进程）
    /// </summary>
    public static void HardRestartAdb(string adbPath)
    {
        if (string.IsNullOrEmpty(adbPath))
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowsKillAdbProcesses(adbPath);
        }
        else
        {
            UnixKillAdbProcesses(adbPath);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WindowsKillAdbProcesses(string adbPath)
    {
        const string WmiQueryString = "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process";
        using var searcher = new ManagementObjectSearcher(WmiQueryString);
        using var results = searcher.Get();

        var query = from p in Process.GetProcesses()
                    join mo in results.Cast<ManagementObject>()
                        on p.Id equals (int)(uint)mo["ProcessId"]
                    where ((string)mo["ExecutablePath"])?.Equals(adbPath, StringComparison.OrdinalIgnoreCase) == true
                    select p;

        KillProcesses(query);
    }

    private static void UnixKillAdbProcesses(string adbPath)
    {
        var processes = Process.GetProcessesByName("adb")
            .Where(p =>{
                try
                {
                    return GetUnixProcessPath(p.Id)?.Equals(adbPath, StringComparison.Ordinal) == true;
                }
                catch
                {
                    return false;
                }
            });

        KillProcesses(processes);
    }

    #endregion

    #region 通用进程管理

    /// <summary>
    /// 跨平台终止指定进程
    /// </summary>
    public static void CloseProcessesByName(string processName, string? commandLineKeyword = null)
    {
        var processes = Process.GetProcesses()
            .Where(p => IsTargetProcess(p, processName, commandLineKeyword))
            .ToList();

        foreach (var process in processes)
        {
            SafeTerminateProcess(process);
        }
    }

    /// <summary>
    /// 终止进程列表
    /// </summary>
    public static void KillProcesses(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            try
            {
                process.Kill();
                process.WaitForExit();
            }
            catch
            {
                // 记录日志或忽略异常
            }
        }
    }

    /// <summary>
    /// 安全终止进程
    /// </summary>
    public static void SafeTerminateProcess(Process process)
    {
        try
        {
            if (process.HasExited) return;

            if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && NeedElevation(process))
            {
                ElevateKill(process.Id);
            }
            else
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Error] 终止进程失败: {process.ProcessName} ({process.Id}) - {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    #endregion

    #region 进程查询辅助方法

    private static bool IsTargetProcess(Process process, string processName, string? keyword)
    {
        try
        {
            if (!IsProcessNameMatch(process, processName))
                return false;

            return string.IsNullOrWhiteSpace(keyword) || 
                   GetCommandLine(process).Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessNameMatch(Process process, string targetName)
    {
        var actualName = Path.GetFileNameWithoutExtension(process.ProcessName);
        return actualName.Equals(targetName, StringComparison.OrdinalIgnoreCase);
    }
    
    private static string GetCommandLine(Process process)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? GetWindowsCommandLine(process)
            : OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                ? GetUnixCommandLine(process.Id)
                : "";
    }

    [SupportedOSPlatform("windows")]
    private static string GetWindowsCommandLine(Process process)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
            return searcher.Get().Cast<ManagementObject>()
                    .FirstOrDefault()?["CommandLine"]?.ToString()
                ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static string GetUnixCommandLine(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var cmdlinePath = $"/proc/{pid}/cmdline";
                return File.Exists(cmdlinePath) 
                    ? File.ReadAllText(cmdlinePath, Encoding.UTF8).Replace('\0', ' ') 
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
        else // macOS
        {
            var output = ExecuteShellCommand($"ps -p {pid} -o command=");
            return output?.Trim() ?? string.Empty;
        }
    }

    /// <summary>
    /// 获取 Unix 进程路径
    /// </summary>
    public static string? GetUnixProcessPath(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var exePath = $"/proc/{pid}/exe";
            return File.Exists(exePath) ? new FileInfo(exePath).LinkTarget : null;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var output = ExecuteShellCommand($"ps -p {pid} -o comm=");
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        return null;
    }

    #endregion

    #region 权限提升
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static bool NeedElevation(Process process)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var uid = GetUnixUserId();
            var processUid = GetProcessUid(process.Id);
            return uid != processUid;
        }
        catch
        {
            return true;
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void ElevateKill(int pid)
    {
        ExecuteShellCommand($"sudo kill -9 {pid}");
    }

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();

    private static uint GetUnixUserId() => GetUid();

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static uint GetProcessUid(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var statusPath = $"/proc/{pid}/status";
            var uidLine = File.ReadLines(statusPath)
                .FirstOrDefault(l => l.StartsWith("Uid:"));
            return uint.Parse(uidLine?.Split('\t')[1] ?? "0");
        }
        else // macOS
        {
            var output = ExecuteShellCommand($"ps -p {pid} -o uid=");
            return uint.TryParse(output?.Trim(), out var uid) ? uid : 0;
        }
    }

    #endregion

    #region 平台特定终止方法

    /// <summary>
    /// Windows 平台强制终止进程
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void ForceKillProcessWindows(int processId, string processName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/F /PID {processId}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(2000);

            LoggerHelper.Info($"已使用 taskkill 强制终止进程 (PID: {processId})");
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"taskkill 失败，尝试备用方法: {ex.Message}");

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Process WHERE ProcessId = {processId}");

                foreach (ManagementObject obj in searcher.Get())
                {
                    obj.InvokeMethod("Terminate", null);
                    LoggerHelper.Info($"已使用 WMI 强制终止进程 (PID: {processId})");
                }
            }
            catch (Exception wmiEx)
            {
                LoggerHelper.Error($"WMI 终止进程失败: {wmiEx.Message}");
            }
        }
    }

    /// <summary>
    /// Unix 平台强制终止进程
    /// </summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static void ForceKillProcessUnix(int processId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"kill -9 {processId}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(2000);

            LoggerHelper.Info($"已使用 kill -9 强制终止进程 (PID: {processId})");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"kill -9 失败: {ex.Message}");
        }
    }

    #endregion

    #region Shell 命令执行

    /// <summary>
    /// 执行 Shell 命令
    /// </summary>
    public static string? ExecuteShellCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            return process?.StandardOutput.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    #endregion
}