using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels;
using SukiUI;
using SukiUI.Dialogs;
using SukiUI.Models;
using System.Linq;
using TextMateSharp.Themes;

namespace MFAAvalonia.Views.UserControls;

public partial class CustomThemeDialogViewModel(SukiTheme theme, ISukiDialog dialog) : ViewModelBase
{
    [ObservableProperty] private string _displayName = "Pink";
    [ObservableProperty] private Color _primaryColor = Colors.DeepPink;
    [ObservableProperty] private Color _accentColor = Colors.Pink;

    [RelayCommand]
    private void TryCreateTheme()
    {
        if (string.IsNullOrEmpty(DisplayName)) return;
        if (theme.ColorThemes.Any(t => t.DisplayName == DisplayName))
        {
            ToastHelper.Error(LangKeys.ColorThemeAlreadyExists.ToLocalization());
            dialog.Dismiss();
            return;
        }
        var color = new SukiColorTheme(DisplayName, PrimaryColor, AccentColor);
        Instances.GuiSettingsUserControlModel.AddOtherColor(color);
        theme.AddColorTheme(color);
        theme.ChangeColorTheme(color);
        dialog.Dismiss();
    }
}
