using Avalonia.Collections;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.ViewModels.Other;
using MFAAvalonia.Views.UserControls;
using SukiUI;
using SukiUI.Dialogs;
using SukiUI.Enums;
using SukiUI.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class GuiSettingsUserControlModel : ViewModelBase
{
    private static readonly SukiTheme _theme = SukiTheme.GetInstance();
    public IAvaloniaReadOnlyList<SukiBackgroundStyle> AvailableBackgroundStyles { get; set; }
    public IAvaloniaReadOnlyList<string> Test { get; set; }
    [ObservableProperty] private bool _backgroundAnimations =
        ConfigurationManager.Current.GetValue(ConfigurationKeys.BackgroundAnimations, false);

    [ObservableProperty] private bool _backgroundTransitions =
        ConfigurationManager.Current.GetValue(ConfigurationKeys.BackgroundTransitions, false);

    [ObservableProperty] private SukiBackgroundStyle _backgroundStyle =
        ConfigurationManager.Current.GetValue(ConfigurationKeys.BackgroundStyle, SukiBackgroundStyle.GradientSoft, SukiBackgroundStyle.GradientSoft, new UniversalEnumConverter<SukiBackgroundStyle>());

    [ObservableProperty] private ThemeVariant _baseTheme;

    [ObservableProperty] private SukiColorTheme _currentColorTheme;
    [ObservableProperty] private AvaloniaList<ThemeItemViewModel> _themeItems;

    public readonly IList<SukiColorTheme> OtherColorThemes = ConfigurationManager.Current.GetValue(ConfigurationKeys.OtherColorTheme, new List<SukiColorTheme>());

    public IAvaloniaReadOnlyList<SupportedLanguage> SupportedLanguages { get; set; }

    [ObservableProperty] private string _currentLanguage;
    [ObservableProperty] private bool _shouldMinimizeToTray = ConfigurationManager.Current.GetValue(ConfigurationKeys.ShouldMinimizeToTray, false);
    partial void OnShouldMinimizeToTrayChanged(bool value) => HandlePropertyChanged(ConfigurationKeys.ShouldMinimizeToTray, value);

    protected override void Initialize()
    {
        SupportedLanguages = new AvaloniaList<SupportedLanguage>(LanguageHelper.SupportedLanguages);
        AvailableBackgroundStyles = new AvaloniaList<SukiBackgroundStyle>(Enum.GetValues<SukiBackgroundStyle>());
        foreach (var color in OtherColorThemes)
        {
            if (_theme.ColorThemes.All(theme => theme.DisplayName != color.DisplayName))
                _theme.AddColorTheme(color);
        }
        CurrentColorTheme = ConfigurationManager.Current.GetValue(ConfigurationKeys.ColorTheme, _theme.ColorThemes.First(t => t.DisplayName.Equals("blue", StringComparison.OrdinalIgnoreCase)));

        BaseTheme =
            ConfigurationManager.Current.GetValue(ConfigurationKeys.BaseTheme, ThemeVariant.Light, new Dictionary<object, ThemeVariant>
            {
                ["Dark"] = ThemeVariant.Dark,
                ["Light"] = ThemeVariant.Light,
            });

        CurrentLanguage = LanguageHelper.CurrentLanguage;
        ThemeItems = new AvaloniaList<ThemeItemViewModel>(
            _theme.ColorThemes.ToList().Select(t => new ThemeItemViewModel(t, this))
        );

        _theme.OnColorThemeChanged += theme =>
        {
            ThemeItems = new AvaloniaList<ThemeItemViewModel>(
                _theme.ColorThemes.ToList().Select(t => new ThemeItemViewModel(t, this))
            );
            CurrentColorTheme = theme;
        };

        LanguageHelper.LanguageChanged += (sender, args) =>
        {
            CurrentLanguage = args.Value.Key;
        };
    }

    [RelayCommand]
    private void CreateCustomTheme()
    {
        Instances.DialogManager.CreateDialog()
            .WithViewModel(dialog => new CustomThemeDialogViewModel(_theme, dialog)).Dismiss().ByClickingBackground()
            .TryShow();
    }

    partial void OnCurrentColorThemeChanged(SukiColorTheme value) => HandlePropertyChanged(ConfigurationKeys.ColorTheme, value, t => _theme.ChangeColorTheme(t));

    partial void OnBaseThemeChanged(ThemeVariant value) => HandlePropertyChanged(ConfigurationKeys.BaseTheme, value, t => _theme.ChangeBaseTheme(t));

    partial void OnBackgroundAnimationsChanged(bool value) => HandlePropertyChanged(ConfigurationKeys.BackgroundAnimations, value);

    partial void OnBackgroundTransitionsChanged(bool value) => HandlePropertyChanged(ConfigurationKeys.BackgroundTransitions, value);

    partial void OnBackgroundStyleChanged(SukiBackgroundStyle value) => HandlePropertyChanged(ConfigurationKeys.BackgroundStyle, value.ToString());

    partial void OnCurrentLanguageChanged(string value) => HandlePropertyChanged(ConfigurationKeys.CurrentLanguage, value, LanguageHelper.ChangeLanguage);

    public void AddOtherColor(SukiColorTheme color)
    {
        OtherColorThemes.Add(color);
        ConfigurationManager.Current.SetValue(ConfigurationKeys.OtherColorTheme, OtherColorThemes);
    }
    
    public void RemoveOtherColor(SukiColorTheme color)
    {
        OtherColorThemes.Remove(color);
        ConfigurationManager.Current.SetValue(ConfigurationKeys.OtherColorTheme, OtherColorThemes);
        ThemeItems = new AvaloniaList<ThemeItemViewModel>(
            _theme.ColorThemes.ToList().Select(t => new ThemeItemViewModel(t, this))
        );
    }
}
