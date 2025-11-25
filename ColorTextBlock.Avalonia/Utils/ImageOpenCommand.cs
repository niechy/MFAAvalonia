using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ColorTextBlock.Avalonia.Utils;

public class ImageOpenCommand : ICommand
{
    public event EventHandler? CanExecuteChanged;

    // 存储生成的临时文件路径，用于后续清理
    private readonly List<string> _tempFiles = new List<string>();

    public bool CanExecute(object? parameter)
    {
        return parameter is string; // 参数为图片路径/URL
    }

    public void Execute(object? parameter)
    {
        if (parameter is string source)
        {
            // 异步执行，避免阻塞UI
            _ = OpenImage(source);
        }
    }

    async private Task OpenImage(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            switch (uri.Scheme)
            {
                case "http":
                case "https":
                    // 网络图片：下载到临时文件后打开
                    var tempFile = await DownloadToTempFile(uri);
                    if (tempFile != null)
                        await OpenLocalFileAndCleanup(tempFile);
                    break;
                case "file":
                    // 本地文件：直接打开（无需清理）
                    OpenLocalFile(uri.LocalPath);
                    break;
                case "avares":
                    // 资源图片：提取到临时文件后打开
                    var resFile = await ExtractResourceToTempFile(uri);
                    if (resFile != null)
                        await OpenLocalFileAndCleanup(resFile);
                    break;
            }
        }
        else
        {
            // 相对路径视为本地文件（无需清理）
            if (File.Exists(source))
                OpenLocalFile(source);
        }
    }

    /// <summary>
    /// 打开本地文件并在进程退出后清理临时文件
    /// </summary>
    /// <param name="path">文件路径（临时文件）</param>
    async private Task OpenLocalFileAndCleanup(string path)
    {
        Process? process = null;
        try
        {
            // 启动系统图片浏览器进程
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                process = Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                process = Process.Start("xdg-open", path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                process = Process.Start("open", path);
            }

            if (process != null)
            {
                // 等待进程退出（图片浏览器关闭）
                await process.WaitForExitAsync();
                // 确保进程完全退出后再删除文件
                await Task.Delay(100);
                DeleteTempFile(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"打开/清理临时文件失败：{ex.Message}");
            // 即使出错，也尝试删除临时文件
            DeleteTempFile(path);
        }
    }

    // 打开本地文件（非临时文件，无需等待清理）
    private void OpenLocalFile(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"打开本地文件失败：{ex.Message}");
        }
    }

    // 下载网络图片到临时文件
    async private Task<string?> DownloadToTempFile(Uri uri)
    {
        try
        {
            using var httpClient = new HttpClient();
            var bytes = await httpClient.GetByteArrayAsync(uri);
            var tempPath = Path.GetTempFileName() + Path.GetExtension(uri.LocalPath);
            await File.WriteAllBytesAsync(tempPath, bytes);
            // 记录临时文件路径（兜底清理）
            lock (_tempFiles)
            {
                _tempFiles.Add(tempPath);
            }
            return tempPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"下载网络图片失败：{ex.Message}");
            return null;
        }
    }

    // 提取资源图片到临时文件
    async private Task<string?> ExtractResourceToTempFile(Uri uri)
    {
        try
        {
            await using var stream = AssetLoader.Open(uri);
            var tempPath = Path.GetTempFileName() + ".png"; // 假设资源为图片格式
            await using var fileStream = File.OpenWrite(tempPath);
            await stream.CopyToAsync(fileStream);
            // 记录临时文件路径（兜底清理）
            lock (_tempFiles)
            {
                _tempFiles.Add(tempPath);
            }
            return tempPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"提取资源图片失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 删除临时文件
    /// </summary>
    /// <param name="path">临时文件路径</param>
    private void DeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.WriteLine($"临时文件已删除：{path}");
            }
            // 从记录中移除
            lock (_tempFiles)
            {
                _tempFiles.Remove(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"删除临时文件失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 析构函数：兜底清理剩余的临时文件
    /// </summary>
    ~ImageOpenCommand()
    {
        lock (_tempFiles)
        {
            foreach (var path in _tempFiles)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        Debug.WriteLine($"析构函数清理临时文件：{path}");
                    }
                }
                catch
                {
                    // 忽略删除失败的情况（如文件已被占用）
                }
            }
            _tempFiles.Clear();
        }
    }
}
