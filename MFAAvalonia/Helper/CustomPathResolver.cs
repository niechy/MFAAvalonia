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
    // 自定义HttpClient：设置超时，解决响应提前结束问题
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // 保留原属性
    public string? AssetPathRoot { set; private get; } = AppContext.BaseDirectory;
    public IEnumerable<string>? CallerAssemblyNames { set; private get; }


    /// <summary>
    /// 重写图片资源解析逻辑：整合新的相对路径解析规则
    /// </summary>
    public async Task<Stream?>? ResolveImageResource(string relativeOrAbsolutePath)
    {
        // 空值校验：避免无效请求
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
            return null;

        try
        {
            // 第一步：使用新的ResolveUrl解析路径（核心修改点）
            string resolvedPath = ResolveUrl(relativeOrAbsolutePath);
            if (string.IsNullOrWhiteSpace(resolvedPath))
                return null;

            // 第二步：处理解析后的路径，转换为Uri并打开流
            Uri? targetUri = null;

            // 处理网络URL（http/https）
            if (IsUrl(resolvedPath))
            {
                targetUri = new Uri(resolvedPath);
            }
            // 处理本地文件路径（转换为file协议Uri）
            else if (IsAbsolutePath(resolvedPath) || File.Exists(resolvedPath))
            {
                targetUri = new Uri($"file://{resolvedPath}");
            }
            // 保留avares协议的原有处理（若解析后的路径是avares格式）
            else if (resolvedPath.StartsWith("avares://") && Uri.TryCreate(resolvedPath, UriKind.Absolute, out var avaresUri))
            {
                targetUri = avaresUri;
            }

            // 第三步：打开流（复用原OpenStream方法）
            if (targetUri != null)
            {
                var stream = await OpenStream(targetUri);
                if (stream != null)
                    return stream;
            }

            // 保留原avares协议的兜底处理（兼容原有逻辑）
            if (CallerAssemblyNames != null && !IsUrl(relativeOrAbsolutePath) && !IsAbsolutePath(relativeOrAbsolutePath))
            {
                foreach (string callerAssemblyName in CallerAssemblyNames)
                {
                    var avaresUri = new Uri($"avares://{callerAssemblyName}/{resolvedPath}");
                    var stream = await OpenStream(avaresUri);
                    if (stream != null)
                        return stream;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            // 捕获所有未预期异常，输出日志并返回null
            LoggerHelper.Error($"[CustomPathResolver] 解析图片失败：{relativeOrAbsolutePath}，错误：{ex}");
            return null;
        }
    }

    #region 新添加的路径解析逻辑（ResolveUrl + 辅助方法）

    /// <summary>
    /// 新的路径解析核心方法：处理相对路径/绝对路径/网络URL
    /// </summary>
    private string ResolveUrl(string url)
    {
        // 1. 处理http/https网络链接
        if (IsUrl(url))
        {
            return url;
        }

        // 2. 处理本地绝对路径
        if (IsAbsolutePath(url))
        {
            if (File.Exists(url))
                return url;

            // 提取文件名尝试在当前文档目录查找
            string fileName = Path.GetFileName(url);
            if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrWhiteSpace(AssetPathRoot))
            {
                string? cd = Path.GetDirectoryName(AssetPathRoot);
                if (cd != null && Directory.Exists(cd))
                {
                    string? foundPath = FindFileInDirectoryAndSubfolders(cd, fileName);
                    if (!string.IsNullOrEmpty(foundPath))
                        return foundPath;
                }
            }
            // 找不到则返回原始绝对路径
            return url;
        }

        // 3. 处理相对路径（依赖CurrentDocumentPath）
        if (string.IsNullOrWhiteSpace(AssetPathRoot) || !Directory.Exists(Path.GetDirectoryName(AssetPathRoot)))
            return url;

        // 解析相对路径为绝对路径
        var currentDir = Path.GetDirectoryName(AssetPathRoot);
        if (currentDir == null)
            return url;

        string absolutePath = Path.Combine(currentDir, url);
        string normalizedPath = Path.GetFullPath(absolutePath);

        // 检查解析后的路径是否存在
        if (File.Exists(normalizedPath))
            return normalizedPath;

        // 提取文件名尝试在当前文档目录递归查找
        string targetFileName = Path.GetFileName(normalizedPath);
        if (!string.IsNullOrEmpty(targetFileName) && Directory.Exists(currentDir))
        {
            string? foundPath = FindFileInDirectoryAndSubfolders(currentDir, targetFileName);
            if (!string.IsNullOrEmpty(foundPath))
                return foundPath;
        }

        // 所有尝试失败，返回规范化后的路径
        return normalizedPath;
    }

    /// <summary>
    /// 递归查找文件（当前目录+子目录）
    /// </summary>
    private string? FindFileInDirectoryAndSubfolders(string rootDir, string fileName)
    {
        try
        {
            // 检查当前目录
            string currentDirFile = Path.Combine(rootDir, fileName);
            if (File.Exists(currentDirFile))
                return currentDirFile;

            // 递归查找子目录
            foreach (string subDir in Directory.EnumerateDirectories(rootDir))
            {
                string? foundFile = FindFileInDirectoryAndSubfolders(subDir, fileName);
                if (!string.IsNullOrEmpty(foundFile))
                    return foundFile;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 忽略无权限目录
            LoggerHelper.Info($"[CustomPathResolver] 无权限访问目录：{rootDir}");
        }
        catch (PathTooLongException)
        {
            // 忽略路径过长
            LoggerHelper.Info($"[CustomPathResolver] 路径过长：{rootDir}");
        }
        catch (IOException)
        {
            // 忽略I/O错误
            LoggerHelper.Info($"[CustomPathResolver] I/O错误：{rootDir}");
        }

        // 未找到文件
        return null;
    }

    /// <summary>
    /// 判断是否为网络URL（http/https）
    /// </summary>
    private bool IsUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断是否为本地绝对路径
    /// </summary>
    private bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        return Path.IsPathRooted(path);
    }

    #endregion

    #region 原有流处理逻辑（保留并优化）

    /// <summary>
    /// 打开流：处理http/https/file/avares协议
    /// </summary>
    async private Task<Stream?> OpenStream(Uri? url)
    {
        if (url == null)
            return null;

        try
        {
            switch (url.Scheme)
            {
                case "http":
                case "https":
                    return await OpenHttpStream(url);

                case "file":
                    if (File.Exists(url.LocalPath))
                        return File.OpenRead(url.LocalPath);
                    return null;

                case "avares":
                    if (AssetLoader.Exists(url))
                        return AssetLoader.Open(url);
                    return null;

                default:
                    LoggerHelper.Info($"[CustomPathResolver] 不支持的协议：{url.Scheme}，URL：{url}");
                    return null;
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"[CustomPathResolver] 打开流失败（URL：{url}）：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 处理HTTP/HTTPS流：添加超时和状态码校验
    /// </summary>
    async private Task<Stream?> OpenHttpStream(Uri url)
    {
        HttpResponseMessage? response = null;
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("MFA", RootViewModel.Version));
            response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                LoggerHelper.Info($"[CustomPathResolver] HTTP请求失败（URL：{url}）：状态码 {response.StatusCode}");
                return null;
            }

            return await response.Content.ReadAsStreamAsync();
        }
        catch (HttpRequestException ex)
        {
            LoggerHelper.Info($"[CustomPathResolver] HTTP请求异常（URL：{url}）：{ex.Message}");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            LoggerHelper.Info($"[CustomPathResolver] HTTP请求超时（URL：{url}）：{ex.Message}");
            return null;
        }
        finally
        {
            if (response != null && !response.IsSuccessStatusCode)
                response.Dispose();
        }
    }

    #endregion
}
