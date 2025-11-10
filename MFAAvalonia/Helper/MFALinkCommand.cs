using Markdown.Avalonia.Utils;
using System;
using System.IO;
using System.Windows.Input;

namespace MFAAvalonia.Helper;

public class MFALinkCommand : ICommand
{
#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;

    // 新增：当前Markdown文档的路径（作为解析相对链接的基准）
    public string? CurrentDocumentPath { get; set; } = AppContext.BaseDirectory;

    public bool CanExecute(object? parameter) => true;

    // parameter为Markdown中的链接字符串（可能是相对路径或绝对路径）
    public void Execute(object? parameter)
    {
        var urlTxt = parameter as string;
        if (string.IsNullOrWhiteSpace(urlTxt))
            return;

        try
        {
            // 处理链接（区分相对路径和绝对路径）
            var resolvedUrl = ResolveUrl(urlTxt);
            DefaultHyperlinkCommand.GoTo(resolvedUrl);
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
        }
    }

    // 核心：解析相对路径为绝对路径（或保持绝对路径不变）
    private string ResolveUrl(string url)
    {
        // 1. 处理http链接
        if (IsUrl(url))
        {
            return url;
        }
        // 检查绝对路径对应的文件是否存在
        if (IsAbsolutePath(url))
        {
            if (File.Exists(url))
                return url;

            // 提取文件名尝试在当前文档目录查找
            string fileName = Path.GetFileName(url);
            if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrWhiteSpace(CurrentDocumentPath))
            {
                string cd = Path.GetDirectoryName(CurrentDocumentPath);
                if (cd != null && Directory.Exists(cd))
                {
                    string foundPath = FindFileInDirectoryAndSubfolders(cd, fileName);
                    if (!string.IsNullOrEmpty(foundPath))
                        return foundPath;
                }
            } // 找不到则返回原始绝对路径
        }
        // 2. 处理相对路径
        // 若没有有效当前文档路径，直接返回
        if (string.IsNullOrWhiteSpace(CurrentDocumentPath) || !Directory.Exists(Path.GetDirectoryName(CurrentDocumentPath)))
            return url;

        // 3. 解析相对路径为绝对路径
        var currentDir = Path.GetDirectoryName(CurrentDocumentPath);
        if (currentDir == null)
            return url;

        string absolutePath = Path.Combine(currentDir, url);
        string normalizedPath = Path.GetFullPath(absolutePath);

        // 检查解析后的路径是否存在
        if (File.Exists(normalizedPath))
            return normalizedPath;

        // 提取文件名尝试在当前文档目录查找
        string targetFileName = Path.GetFileName(normalizedPath);
        if (!string.IsNullOrEmpty(targetFileName) && Directory.Exists(currentDir))
        {
            string foundPath = FindFileInDirectoryAndSubfolders(currentDir, targetFileName);
            if (!string.IsNullOrEmpty(foundPath))
                return foundPath;
        }
        // 所有尝试失败，返回原始解析路径
        return normalizedPath;
    }
    private string FindFileInDirectoryAndSubfolders(string rootDir, string fileName)
    {
        try
        {
            // 检查当前目录是否包含目标文件
            string currentDirFile = Path.Combine(rootDir, fileName);
            if (File.Exists(currentDirFile))
                return currentDirFile;

            // 递归查找所有子目录
            foreach (string subDir in Directory.EnumerateDirectories(rootDir))
            {
                string foundFile = FindFileInDirectoryAndSubfolders(subDir, fileName);
                if (!string.IsNullOrEmpty(foundFile))
                    return foundFile;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 忽略无权限访问的目录
        }
        catch (PathTooLongException)
        {
            // 忽略路径过长的目录
        }
        catch (IOException)
        {
            // 忽略I/O错误
        }

        // 未找到文件
        return null;
    }

    // 判断是否为绝对路径或网络链接（无需解析）
    private bool IsAbsolutePath(string url)
    {
        // 绝对文件路径（Windows：带盘符；Linux/macOS：以/开头）
        if (Path.IsPathRooted(url))
            return true;
        return false;
    }
    private bool IsUrl(string url)
    {
        // 网络链接（http/https/ftp等）
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFtp))
            return true;
        return false;
    }
}
