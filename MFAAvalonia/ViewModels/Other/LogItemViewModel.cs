using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using SukiUI;
using System;
using System.Text.RegularExpressions;

namespace MFAAvalonia.ViewModels.Other;

public partial class LogItemViewModel : ViewModelBase
{
    private readonly string[] _formatArgsKeys;
    private bool _transformKey = true;
    private bool _changeColor = true;
    private readonly IBrush _baseBrush;
    public LogItemViewModel(string resourceKey,
        IBrush color,
        string weight = "Regular",
        bool useKey = false,
        string dateFormat = "MM'-'dd'  'HH':'mm':'ss",  bool changeColor = true,
        bool showTime = true,
        params string[] formatArgsKeys)
    {
        _resourceKey = resourceKey;

        Time = DateTime.Now.ToString(dateFormat);
        _changeColor = changeColor;
        Weight = weight;
        ShowTime = showTime;
        _baseBrush = color;
        if (_changeColor)
            SukiTheme.GetInstance().OnBaseThemeChanged += OnThemeChanged;
        OnThemeChanged(Instances.GuiSettingsUserControlModel.BaseTheme);
        if (useKey)
        {
            _formatArgsKeys = formatArgsKeys;
            UpdateContent();
            LanguageHelper.LanguageChanged += OnLanguageChanged;
        }
        else
            Content = resourceKey;
    }

    public LogItemViewModel(string resourceKey,
        IBrush color,
        string weight = "Regular",
        bool useKey = false,
        string dateFormat = "MM'-'dd'  'HH':'mm':'ss",
        bool changeColor = true,
        bool showTime = true,
        bool transformKey = true,
        params string[] formatArgsKeys)
    {
        _resourceKey = resourceKey;
        _transformKey = transformKey;
        Time = DateTime.Now.ToString(dateFormat);
        _changeColor = changeColor;
        Color = color;
        Weight = weight;
        ShowTime = showTime;
        _baseBrush = color;
        if (_changeColor)
            SukiTheme.GetInstance().OnBaseThemeChanged += OnThemeChanged;
        OnThemeChanged(Instances.GuiSettingsUserControlModel.BaseTheme);
        if (useKey)
        {
            _formatArgsKeys = formatArgsKeys;
            UpdateContent();
            LanguageHelper.LanguageChanged += OnLanguageChanged;
        }
        else
            Content = resourceKey;
    }

    public LogItemViewModel(string content,
        IBrush? color,
        string weight = "Regular",
        string dateFormat = "MM'-'dd'  'HH':'mm':'ss",
        bool showTime = true,
        bool changeColor = true)
    {
        Time = DateTime.Now.ToString(dateFormat);
        _baseBrush = color;
        _changeColor = changeColor;
        if (_changeColor)
            SukiTheme.GetInstance().OnBaseThemeChanged += OnThemeChanged;
        OnThemeChanged(Instances.GuiSettingsUserControlModel.BaseTheme);
        Weight = weight;
        ShowTime = showTime;
        Content = content;
        if (_changeColor)
            SukiTheme.GetInstance().OnBaseThemeChanged += OnThemeChanged;
    }

    private string _time;

    public string Time
    {
        get => _time;
        set => SetProperty(ref _time, value);
    }

    private bool _showTime = true;

    public bool ShowTime
    {
        get => _showTime;
        set => SetProperty(ref _showTime, value);
    }

    private string _content;

    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    private IBrush _color;

    public IBrush Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    private string _weight = "Regular";

    public string Weight
    {
        get => _weight;
        set => SetProperty(ref _weight, value);
    }

    private string _resourceKey;

    public string ResourceKey
    {
        get => _resourceKey;
        set
        {
            if (SetProperty(ref _resourceKey, value))
            {
                UpdateContent();
            }
        }
    }

    private void UpdateContent()
    {
        if (_formatArgsKeys == null || _formatArgsKeys.Length == 0)
            Content = ResourceKey.ToLocalization();
        else
        {
            try
            {
                Content = Regex.Unescape(
                    _resourceKey.ToLocalizationFormatted(_transformKey, _formatArgsKeys));
            }
            catch
            {
                Content = _resourceKey.ToLocalizationFormatted(_transformKey, _formatArgsKeys);
            }
        }
    }
    private bool _isDownloading;

    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetProperty(ref _isDownloading, value);
    }

    private void OnLanguageChanged(object sender, EventArgs e)
    {
        UpdateContent();
    }

    private void OnThemeChanged(ThemeVariant variant)
    {
        if (!_changeColor || variant == ThemeVariant.Light)
        {
            Color = _baseBrush;
        }
        else
        {
            if (_baseBrush is ISolidColorBrush solidColorBrush)
            {
                // 反转颜色
                var invertedColor = new Color(
                    solidColorBrush.Color.A,
                    (byte)(255 - solidColorBrush.Color.R),
                    (byte)(255 - solidColorBrush.Color.G),
                    (byte)(255 - solidColorBrush.Color.B));
                Color = new SolidColorBrush(invertedColor);
            }
        }
    }
}
