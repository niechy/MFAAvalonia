using Markdown.Avalonia.Utils;
using System;
using System.IO;
using System.Windows.Input;

namespace MFAAvalonia.Helper;

public class MFALinkCommand : ICommand
{
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
        // 1. 若不是相对路径（如绝对路径、http链接），直接返回
        if (IsAbsoluteUrl(url))
            return url;

        // 2. 若没有当前文档路径，无法解析相对路径，返回原始值
        if (string.IsNullOrWhiteSpace(CurrentDocumentPath) || !Directory.Exists(CurrentDocumentPath))
            return url;

        // 3. 基于当前文档路径解析相对路径
        var currentDir = Path.GetDirectoryName(CurrentDocumentPath);
        if (currentDir == null)
            return url;

        // 组合相对路径和当前目录，得到绝对路径
        var absolutePath = Path.Combine(currentDir, url);
        // 标准化路径（处理 ../ 等相对符号）
        return Path.GetFullPath(absolutePath);
    }

    // 判断是否为绝对路径或网络链接（无需解析）
    private bool IsAbsoluteUrl(string url)
    {
        // 网络链接（http/https/ftp等）
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && 
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFtp))
            return true;

        // 绝对文件路径（Windows：带盘符；Linux/macOS：以/开头）
        if (Path.IsPathRooted(url))
            return true;

        return false;
    }
}
