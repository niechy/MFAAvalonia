using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using Newtonsoft.Json;
using System;
using System.Linq;

namespace MFAAvalonia.Helper.ValueType;

public partial class DragItemViewModel : ObservableObject
{
    public DragItemViewModel(MaaInterface.MaaInterfaceTask? interfaceItem)
    {
        InterfaceItem = interfaceItem;
        Name = LanguageHelper.GetLocalizedDisplayName(InterfaceItem.DisplayName, InterfaceItem.Name ?? LangKeys.Unnamed);
        InterfaceItem?.InitializeIcon();
        UpdateIconFromInterfaceItem();
        LanguageHelper.LanguageChanged += OnLanguageChanged;
    }

    [ObservableProperty] private string _name = string.Empty;

    /// <summary>解析后的图标路径（用于 UI 绑定）</summary>
    [ObservableProperty] private string? _resolvedIcon;

    /// <summary>是否有图标</summary>
    [ObservableProperty] private bool _hasIcon;


    private bool? _isCheckedWithNull = false;
    private bool _isInitialized;

    /// <summary>
    /// Gets or sets a value indicating whether gets or sets whether the key is checked with null.
    /// </summary>
    [JsonIgnore]
    public bool? IsCheckedWithNull
    {
        get => _isCheckedWithNull;
        set
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                SetProperty(ref _isCheckedWithNull, value);
                if (InterfaceItem != null) InterfaceItem.Check = IsChecked;
            }
            else
            {
                SetProperty(ref _isCheckedWithNull, value);
                if (InterfaceItem != null)
                    InterfaceItem.Check = _isCheckedWithNull;
                ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems,
                    Instances.TaskQueueViewModel.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether gets or sets whether the key is checked.
    /// </summary>
    public bool IsChecked
    {
        get => IsCheckedWithNull != false;
        set => IsCheckedWithNull = value;
    }


    private bool _enableSetting;

    /// <summary>
    /// Gets or sets a value indicating whether gets or sets whether the setting enabled.
    /// </summary>
    [JsonIgnore]
    public bool EnableSetting
    {
        get => _enableSetting;
        set
        {
            SetProperty(ref _enableSetting, value);
            Instances.TaskQueueView.SetOption(this, value);
        }
    }

    private MaaInterface.MaaInterfaceTask? _interfaceItem;

    public MaaInterface.MaaInterfaceTask? InterfaceItem
    {
        get => _interfaceItem;
        set
        {
            if (value != null)
            {
                if (!string.IsNullOrEmpty(value.DisplayName))
                    Name = value.DisplayName;
                IsVisible = value is { Advanced.Count: > 0 } || value is { Option.Count: > 0 } || value.Repeatable == true || !string.IsNullOrWhiteSpace(value.Description) || value.Document is { Count: > 0 };
                IsCheckedWithNull = value.Check;
            }

            SetProperty(ref _interfaceItem, value);
        }
    }

    [ObservableProperty] private bool _isVisible = true;

    /// <summary>
    /// 指示任务是否支持当前选中的资源包。
    /// 当资源包变化时，此属性会被更新。
    /// </summary>
    [ObservableProperty] [JsonIgnore] private bool _isResourceSupported = true;

    /// <summary>
    /// 检查任务是否支持指定的资源包
    /// </summary>
    /// <param name="resourceName">资源包名称</param>
    /// <returns>如果任务支持该资源包或未指定资源限制，则返回 true</returns>
    public bool SupportsResource(string? resourceName)
    {
        // 如果任务没有指定 resource，则支持所有资源包
        if (InterfaceItem?.Resource == null || InterfaceItem.Resource.Count == 0)
            return true;

        // 如果资源名称为空，则显示所有任务
        if (string.IsNullOrWhiteSpace(resourceName))
            return true;

        // 检查任务是否支持当前资源包
        return InterfaceItem.Resource.Any(r =>
            r.Equals(resourceName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 更新任务对指定资源包的支持状态
    /// </summary>
    /// <param name="resourceName">资源包名称</param>
    public void UpdateResourceSupport(string? resourceName)
    {
        IsResourceSupported = SupportsResource(resourceName);
    }

    private void UpdateContent()
    {
        if (!string.IsNullOrEmpty(InterfaceItem?.DisplayName ?? LangKeys.Unnamed))
        {
            Name = LanguageHelper.GetLocalizedDisplayName(InterfaceItem.DisplayName, InterfaceItem.Name ?? LangKeys.Unnamed);
        }
        UpdateIconFromInterfaceItem();
    }

    private void UpdateIconFromInterfaceItem()
    {
        if (InterfaceItem != null)
        {
            ResolvedIcon = InterfaceItem.ResolvedIcon;
            HasIcon = InterfaceItem.HasIcon;
        }
        else
        {
            ResolvedIcon = null;
            HasIcon = false;
        }
    }

    private void OnLanguageChanged(object sender, EventArgs e)
    {
        UpdateContent();
    }

    /// <summary>
    /// Creates a deep copy of the current <see cref="DragItemViewModel"/> instance.
    /// </summary>
    /// <returns>A new <see cref="DragItemViewModel"/> instance that is a deep copy of the current instance.</returns>
    public DragItemViewModel Clone()
    {
        // Clone the InterfaceItem if it's not null
        MaaInterface.MaaInterfaceTask? clonedInterfaceItem = InterfaceItem?.Clone();

        // Create a new DragItemViewModel instance with the cloned InterfaceItem
        DragItemViewModel clone = new(clonedInterfaceItem);

        // Copy all other properties to the new instance
        clone.Name = this.Name;
        clone.IsCheckedWithNull = this.IsCheckedWithNull;
        clone.EnableSetting = this.EnableSetting;
        clone.IsVisible = this.IsVisible;
        clone.IsResourceSupported = this.IsResourceSupported;
        clone.ResolvedIcon = this.ResolvedIcon;
        clone.HasIcon = this.HasIcon;

        return clone;
    }
}
