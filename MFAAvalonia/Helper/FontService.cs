using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Helper;

/// <summary>
/// 字体服务类，用于管理全局字体缩放和字体选择
/// </summary>
public partial class FontService : ObservableObject
{
    /// <summary>
    /// 单例实例
    /// </summary>
    public static FontService Instance { get; } = new();

    /// <summary>
    /// 默认缩放比例
    /// </summary>
    public const double DefaultScale = 1.0;

    /// <summary>
    /// 最小缩放比例
    /// </summary>
    public const double MinScale = 0.8;

    /// <summary>
    /// 最大缩放比例
    /// </summary>
    public const double MaxScale = 1.5;

    /// <summary>
    /// 当前字体缩放比例
    /// </summary>
    [ObservableProperty]
    private double _currentScale = DefaultScale;

    /// <summary>
    /// 用于UI缩放的 ScaleTransform
    /// </summary>
    [ObservableProperty]
    private ScaleTransform _scaleTransform = new(1, 1);

    /// <summary>
    /// 基础字体大小定义（与SukiUI 默认值对应）
    /// </summary>
    private static readonly Dictionary<string, double> BaseFontSizes = new()
    {
        ["FontSizeSmall"] = 12,
        ["FontSizeMedium"] = 14,
        ["FontSizeLarge"] = 16,
        ["FontSizeH1"] = 32,
        ["FontSizeH2"] = 28,
        ["FontSizeH3"] = 24,
        ["FontSizeH4"] = 20,
        ["FontSizeH5"] = 18,
        ["FontSizeH6"] = 16,
    };

    private FontService() { }

    /// <summary>
    /// 初始化字体服务，从配置中加载字体缩放设置
    /// </summary>
    public static void Initialize()
    {
        var scale = ConfigurationManager.Current.GetValue(ConfigurationKeys.FontScale, DefaultScale);
        Instance.ApplyFontScale(scale, false);
    }

    /// <summary>
    /// 应用字体缩放
    /// </summary>
    /// <param name="scale">缩放比例 (0.8 - 1.5)</param>
    /// <param name="saveToConfig">是否保存到配置</param>
    public void ApplyFontScale(double scale, bool saveToConfig = true)
    {
        // 限制缩放范围
        scale = Math.Clamp(scale, MinScale, MaxScale);
        CurrentScale = scale;

        // 更新 ScaleTransform
        ScaleTransform = new ScaleTransform(scale, scale);

        var app = Application.Current;
        if (app?.Resources == null) return;

        // 更新各级字体大小资源
        foreach (var (key, baseSize) in BaseFontSizes)
        {
            var scaledSize = Math.Round(baseSize * scale, 1);
            app.Resources[key] = scaledSize;
        }

        // 保存到配置
        if (saveToConfig)
        {
            ConfigurationManager.Current.SetValue(ConfigurationKeys.FontScale, scale);
        }
    }

    /// <summary>
    /// 重置字体缩放为默认值
    /// </summary>
    public void ResetFontScale()
    {
        ApplyFontScale(DefaultScale);
    }

    /// <summary>
    /// 获取系统已安装的字体列表（预留功能）
    /// </summary>
    /// <returns>字体名称列表</returns>
    public IEnumerable<string> GetSystemFonts()
    {
        try
        {
            return FontManager.Current.SystemFonts
                .Select(f => f.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n);
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"获取系统字体列表失败: {ex.Message}");
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    /// 应用字体（预留功能）
    /// </summary>
    /// <param name="fontName">字体名称</param>
    public void ApplyFontFamily(string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName)) return;

        var app = Application.Current;
        if (app?.Resources == null) return;

        try
        {
            app.Resources["DefaultFontFamily"] = new FontFamily(fontName);
            ConfigurationManager.Current.SetValue(ConfigurationKeys.FontFamily, fontName);
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"应用字体失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取缩放后的字体大小
    /// </summary>
    /// <param name="baseSize">基础字体大小</param>
    /// <returns>缩放后的字体大小</returns>
    public double GetScaledFontSize(double baseSize)
    {
        return Math.Round(baseSize * CurrentScale, 1);
    }
}