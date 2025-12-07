using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;
using SukiUI.Controls;

namespace MFAAvalonia.Views.Windows;

public partial class ChangelogView : SukiWindow
{
    public ChangelogView()
    {
        InitializeComponent();
    }
    
    private void Close(object sender, RoutedEventArgs e) => Close();
    
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        
        // 清理 ViewModel
        if (DataContext is AnnouncementViewModel viewModel)
        {
            viewModel.Cleanup();
            viewModel.SetView(null);
        }
        
        // 显式清理 MarkdownScrollViewer
        if (Viewer != null)
        {
            Viewer.Cleanup();
            Viewer.Markdown = null;
        }
        
        // 清空 DataContext 以断开绑定
        DataContext = null;
    }
}

