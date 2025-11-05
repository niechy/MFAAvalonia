
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using SukiUI.Models;
using TextMateSharp.Themes;

namespace MFAAvalonia.ViewModels.Other;

public partial class ThemeItemViewModel(SukiColorTheme theme, GuiSettingsUserControlModel settingsModel) : ViewModelBase
{
    [ObservableProperty] private bool _isSelected = settingsModel.CurrentColorTheme.DisplayName == theme.DisplayName;

    [ObservableProperty] private SukiColorTheme _theme = theme;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            Instances.GuiSettingsUserControlModel.CurrentColorTheme = Theme;
    }
}
