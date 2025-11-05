using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AvaloniaExtensions.Axaml.Markup;
using MFAAvalonia.Helper;
using System.Threading.Tasks;


namespace MFAAvalonia.Views.UserControls.Settings;
#pragma warning disable CS4014  // Because this call is not awaited, execution of the current method continues before the call is completed
public partial class VersionUpdateSettingsUserControl : UserControl
{
    public VersionUpdateSettingsUserControl()
    {
        DataContext = Instances.VersionUpdateSettingsUserControlModel;
        InitializeComponent();
    }

    private void CopyVersion(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            var version = textBlock.Text;
            if (!string.IsNullOrEmpty(version))
            {
                TaskManager.RunTaskAsync(async () =>
                {
                    DispatcherHelper.PostOnMainThread(async () => await Instances.RootView.Clipboard.SetTextAsync(version));
                    DispatcherHelper.PostOnMainThread(() =>   textBlock.Bind(ToolTip.TipProperty, new I18nBinding("CopiedToClipboard")));
                    DispatcherHelper.PostOnMainThread(() => ToolTip.SetIsOpen(textBlock, true));
                    await Task.Delay(1000);
                    DispatcherHelper.PostOnMainThread(() => ToolTip.SetIsOpen(textBlock, false));
                    DispatcherHelper.PostOnMainThread(() =>   textBlock.Bind(ToolTip.TipProperty, new I18nBinding("CopyToClipboard")));
                }, name: "复制版本号");
            }
        }
    }
}
