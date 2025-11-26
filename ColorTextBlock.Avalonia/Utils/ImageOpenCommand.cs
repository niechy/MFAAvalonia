using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private readonly List<string> _tempFiles = [];

    // 支持 CImage 或字符串路径作为参数
    public bool CanExecute(object? parameter)
    {
        return parameter is string or CImage;
    }

    public void Execute(object? parameter)
    {
        if (parameter is CImage cImage)
        {
            // 从 CImage 中获取已加载的 IImage
            _ = OpenImageFromCImage(cImage);
        }
        else if (parameter is string source)
        {
            // 兼容原有字符串路径/URL逻辑
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
    // 从 CImage 提取图片并打开
    async private Task OpenImageFromCImage(CImage cImage)
    {
        if (cImage.Image == null)
        {
            Debug.WriteLine("CImage 中未加载图片");
            return;
        }

        // 将 IImage 保存到临时文件
        var tempFile = await SaveImageToTempFile(cImage.Image);
        if (tempFile != null)
        {
            await OpenLocalFileAndCleanup(tempFile);
        }
    }

    // 保存 IImage 到临时文件
    async private Task<string?> SaveImageToTempFile(IImage image)
    {
        try
        {
            // 生成临时文件路径（根据图片类型自动添加扩展名）
            var tempPath = Path.GetTempFileName();
            string extension = image is Bitmap ? ".png" : ".png"; // 默认为PNG
            tempPath = Path.ChangeExtension(tempPath, extension);

            // 处理 Bitmap 类型（直接保存）
            if (image is Bitmap bitmap)
            {
                await using var stream = File.OpenWrite(tempPath);
                bitmap.Save(stream); // 保存 Bitmap 到流
                await stream.FlushAsync();
            }
            // 处理其他 IImage 类型（如 SvgImage，通过渲染到 Bitmap 保存）
            else
            {
                // 创建与图片尺寸一致的 Bitmap
                var pixelSize = new PixelSize((int)image.Size.Width, (int)image.Size.Height);
                var renderTarget = new RenderTargetBitmap(pixelSize, new Vector(96, 96));

                // 渲染 IImage 到 Bitmap
                using (var context = renderTarget.CreateDrawingContext())
                {
                    context.DrawImage(image, new Rect(image.Size), new Rect(0, 0, pixelSize.Width, pixelSize.Height));
                }

                // 保存渲染结果
                await using var stream = File.OpenWrite(tempPath);
                renderTarget.Save(stream);
                await stream.FlushAsync();
            }

            // 记录临时文件路径（兜底清理）
            lock (_tempFiles)
            {
                _tempFiles.Add(tempPath);
            }
            return tempPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"保存图片到临时文件失败：{ex.Message}");
            return null;
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
