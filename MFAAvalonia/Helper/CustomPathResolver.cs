using Avalonia.Platform;
using Markdown.Avalonia.Utils;
using MFAAvalonia.ViewModels.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper;

/// <summary>
/// 自定义路径解析器：修复DefaultPathResolver的HTTP请求异常问题，增强容错性（无第三方依赖）
/// </summary>
public class CustomPathResolver : IPathResolver
{
    // 自定义HttpClient：设置超时，替代原静态HttpClient（.NET原生API）
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10) // 关键：10秒超时，解决响应提前结束问题
    };

    // 保留原属性：兼容原有配置逻辑
    public string? AssetPathRoot { set; private get; }
    public IEnumerable<string>? CallerAssemblyNames { set; private get; }

    /// <summary>
    /// 重写图片资源解析逻辑：保留原协议支持，增强异常处理
    /// </summary>
    public async Task<Stream?> ResolveImageResource(string relativeOrAbsolutePath)
    {
        // 空值校验：避免无效请求
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
            return null;

        try
        {
            // 1. 处理绝对URL（http/https/file/avares）
            if (Uri.TryCreate(relativeOrAbsolutePath, UriKind.Absolute, out var absoluteUri))
            {
                var stream = await OpenStream(absoluteUri);
                if (stream != null)
                    return stream;
            }

            // 2. 处理CallerAssemblyNames（avares协议，原逻辑保留）
            if (CallerAssemblyNames != null)
            {
                foreach (string callerAssemblyName in CallerAssemblyNames)
                {
                    var avaresUri = new Uri($"avares://{callerAssemblyName}/{relativeOrAbsolutePath}");
                    var stream = await OpenStream(avaresUri);
                    if (stream != null)
                        return stream;
                }
            }

            // 3. 处理AssetPathRoot（原逻辑保留，优化URI拼接）
            if (AssetPathRoot != null)
            {
                Uri assetUri;
                if (!Path.IsPathRooted(AssetPathRoot))
                {
                    assetUri = new Uri(new Uri(AssetPathRoot), relativeOrAbsolutePath);
                }
                else
                {
                    var combinedPath = Path.Combine(AssetPathRoot, relativeOrAbsolutePath);
                    assetUri = new Uri(combinedPath);
                }

                var stream = await OpenStream(assetUri);
                if (stream != null)
                    return stream;
            }

            // 替换原NotImplementedException：返回null而非抛异常，避免程序崩溃
            return null;
        }
        catch (Exception ex)
        {
            // 捕获所有未预期异常，输出日志并返回null
            LoggerHelper.Error($"[CustomPathResolver] 解析图片失败：{relativeOrAbsolutePath}，错误：{ex}");
            return null;
        }
    }

    /// <summary>
    /// 重写OpenStream：增强HTTP请求容错性，保留原协议处理逻辑
    /// </summary>
    private async Task<Stream?> OpenStream(Uri? url)
    {
        if (url == null)
            return null;

        try
        {
            switch (url.Scheme)
            {
                case "http":
                case "https":
                    return await OpenHttpStream(url); // 单独处理HTTP请求，增强容错

                case "file":
                    // 优化file协议：判断文件存在后再打开
                    if (File.Exists(url.LocalPath))
                        return File.OpenRead(url.LocalPath);
                    return null;

                case "avares":
                    // 优化avares协议：判断资源存在后再打开
                    if (AssetLoader.Exists(url))
                        return AssetLoader.Open(url);
                    return null;

                // 替换原InvalidDataException：返回null而非抛异常
                default:
                    LoggerHelper.Error($"[CustomPathResolver] 不支持的协议：{url.Scheme}，URL：{url}");
                    return null;
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"[CustomPathResolver] 打开流失败（URL：{url}）：{ex}");
            return null;
        }
    }

    /// <summary>
    /// 处理HTTP/HTTPS流：原生实现（无Polly），添加状态码校验、异常处理
    /// </summary>
    private async Task<Stream?> OpenHttpStream(Uri url)
    {
        HttpResponseMessage? response = null;
        try
        {
            // 原生HTTP请求：无重试，直接发起（避免依赖Polly）
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            // 添加User-Agent：避免部分服务器拒绝无标识请求
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("MFA", RootViewModel.Version));
            // 响应头优先读取：避免下载完整内容后才发现状态码错误
            response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

            // 校验HTTP响应状态码：非200直接返回null
            if (!response.IsSuccessStatusCode)
            {
                LoggerHelper.Error($"[CustomPathResolver] HTTP请求失败（URL：{url}）：状态码 {response.StatusCode}");
                return null;
            }

            // 读取流：使用异步方法避免阻塞
            return await response.Content.ReadAsStreamAsync();
        }
        catch (HttpRequestException ex)
        {
            LoggerHelper.Error($"[CustomPathResolver] HTTP请求异常（URL：{url}）：{ex.Message}");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            // 单独捕获超时异常（HttpClient.Timeout触发）
            LoggerHelper.Error($"[CustomPathResolver] HTTP请求超时（URL：{url}，超时时间：10秒）：{ex.Message}");
            return null;
        }
        finally
        {
            // 确保非成功响应的Dispose（避免资源泄漏）
            if (response != null && !response.IsSuccessStatusCode)
                response.Dispose();
        }
    }
}
