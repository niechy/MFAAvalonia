using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using System;

namespace MFAAvalonia.ViewModels.Other;

public partial class LocalizationViewModel<T> : ViewModelBase
{
    [ObservableProperty] private string _resourceKey = string.Empty;

    partial void OnResourceKeyChanged(string value)
    {
        UpdateName();
    }

    public LocalizationViewModel() { }

    private readonly string[]? _formatArgsKeys;

    public LocalizationViewModel(string resourceKey)
    {
        ResourceKey = resourceKey;
        LanguageHelper.LanguageChanged += OnLanguageChanged;
    }

    public LocalizationViewModel(string resourceKey, params string[] keys)
    {
        ResourceKey = resourceKey;
        _formatArgsKeys = keys;
        LanguageHelper.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object sender, EventArgs e)
    {
        UpdateName();
    }

    private string _name = string.Empty;
    [ObservableProperty] private T? _other;

    [JsonIgnore]
    public string Name
    {
        get => _name;
        [global::System.Diagnostics.CodeAnalysis.MemberNotNull("_name")]
        set
        {
            if (!global::System.Collections.Generic.EqualityComparer<string>.Default.Equals(_name, value))
            {
                OnPropertyChanging(Name);
                _name = value;
                OnPropertyChanged(Name);
            }
        }
    }

    private void UpdateName()
    {
        if (string.IsNullOrWhiteSpace(ResourceKey))
            return;
        if (_formatArgsKeys != null && _formatArgsKeys.Length != 0)
            Name = ResourceKey.ToLocalizationFormatted(true, _formatArgsKeys);
        else
            Name = ResourceKey.ToLocalization();
    }


    public override string ToString()
        => ResourceKey;
}
public partial class LocalizationViewModel : ViewModelBase
{
    [ObservableProperty] private string _resourceKey = string.Empty;

    partial void OnResourceKeyChanged(string value)
    {
        UpdateName();
    }

    public LocalizationViewModel() { }

    private readonly string[]? _formatArgsKeys;

    public LocalizationViewModel(string resourceKey)
    {
        ResourceKey = resourceKey;
        LanguageHelper.LanguageChanged += OnLanguageChanged;
    }

    public LocalizationViewModel(string resourceKey, params string[] keys)
    {
        ResourceKey = resourceKey;
        _formatArgsKeys = keys;
        LanguageHelper.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// 创建带 DisplayName 和 FallbackName 的 LocalizationViewModel（用于 LanguageHelper 本地化）
    /// </summary>
    /// <param name="displayName">显示名称（可能是 $xxx 形式的本地化 key）</param>
    /// <param name="fallbackName">回退名称（当本地化失败时使用）</param>
    public LocalizationViewModel(string? displayName, string? fallbackName)
    {
        _displayName = displayName;
        _fallbackName = fallbackName;
        _useLanguageHelper = true;
        UpdateName();
        LanguageHelper.LanguageChanged += OnLanguageChanged;
    }

    private readonly string? _displayName;
    private readonly string? _fallbackName;
    private readonly bool _useLanguageHelper;

    private void OnLanguageChanged(object sender, EventArgs e)
    {
        UpdateName();
    }

    private string _name = string.Empty;
    [ObservableProperty] private object? _other;

    [JsonIgnore]
    public string Name
    {
        get => _name;
        [global::System.Diagnostics.CodeAnalysis.MemberNotNull("_name")]
        set
        {
            if (!global::System.Collections.Generic.EqualityComparer<string>.Default.Equals(_name, value))
            {
                OnPropertyChanging(Name);
                _name = value;
                OnPropertyChanged(Name);
            }
        }
    }

    private void UpdateName()
    {
        if (_useLanguageHelper)
        {
            // 使用 LanguageHelper 进行本地化（带 fallback）
            Name = LanguageHelper.GetLocalizedDisplayName(_displayName, _fallbackName);
            return;
        }

        if (string.IsNullOrWhiteSpace(ResourceKey))
            return;
        if (_formatArgsKeys != null && _formatArgsKeys.Length != 0)
            Name = ResourceKey.ToLocalizationFormatted(true, _formatArgsKeys);
        else
            Name = ResourceKey.ToLocalization();
    }


    public override string ToString()
        => ResourceKey;
}