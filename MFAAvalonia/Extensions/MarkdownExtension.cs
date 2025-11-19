using Avalonia.Markup.Xaml;
using Markdown.Avalonia.Utils;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;
using System;
using System.IO;

namespace MFAAvalonia.Extensions;

public class MarkdownExtension : MarkupExtension
{
    public string? Directory { get; set; }
    
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var resourcePath = Path.Combine(AppContext.BaseDirectory, "resource");

        var targetDir = string.IsNullOrEmpty(Directory)
            ? Path.Combine(resourcePath, AnnouncementViewModel.AnnouncementFolder) 
            : Path.Combine(resourcePath, Directory);
        
        return new Markdown.Avalonia.Markdown
        {
            HyperlinkCommand = new MFALinkCommand(), 
            AssetPathRoot = targetDir 
        };
    }
}
