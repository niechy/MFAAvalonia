using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;
using SukiUI.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MFAAvalonia.ViewModels.UsersControls;

public partial class MultiInstanceEditorDialogViewModel : ViewModelBase
{
    [ObservableProperty] private double _index = 0;
    [ObservableProperty] private string _emulator = "mumu";
    public ISukiDialog Dialog { get; set; }
    public MultiInstanceEditorDialogViewModel(ISukiDialog dialog)
    {
        Dialog = dialog;
    }
    public ObservableCollection<LocalizationViewModel> EmulatorList =>
    [
        new(LangKeys.MuMuEmulator12)
        {
            Other = "mumu"
        },
        new(LangKeys.LDPlayer)
        {
            Other = "ldplayer"
        },
        new(LangKeys.Nox)
        {
            Other = "nox"
        },
        new(LangKeys.BlueStacks)
        {
            Other = "bluestacks"
        },
        new(LangKeys.XYAZ)
        {
            Other = "xyaz"
        },
    ];

    public static readonly Dictionary<string, string> EmulatorMultiOpenArgumentPrefixes = new()
    {
        {
            "mumu", "-v "
        },
        {
            "ldplayer", "index="
        },
        {
            "xyaz", "--index "
        },
        {
            "nox", "-index "
        },
        {
            "bluestacks", "--index "
        }
    };

    [RelayCommand]
    public void Save()
    {
        if (EmulatorMultiOpenArgumentPrefixes.TryGetValue(Emulator, out var emulatorPrefix ))
        {
            Instances.StartSettingsUserControlModel.EmulatorConfig = $"{emulatorPrefix}{Convert.ToInt32(Index)}";
        }
        Dialog.Dismiss();
    }
   
    /// <summary>
    /// 静态方法：自动匹配所有模拟器前缀规则，从 EmulatorConfig 字符串中反向提取 Index
    /// 格式不匹配、提取失败或无有效数字时返回 -1
    /// </summary>
    /// <param name="emulatorConfig">要解析的 EmulatorConfig 字符串（如 "-v 5"、"index=3"）</param>
    /// <returns>提取到的 Index，失败返回 -1</returns>
    public static int TryExtractIndexFromEmulatorConfig(string emulatorConfig)
    {
        emulatorConfig = emulatorConfig.Trim();
        // 1. 基础校验：配置为空/空白 → 返回 -1
        if (string.IsNullOrWhiteSpace(emulatorConfig))
        {
            return -1;
        }

        // 2. 遍历所有模拟器前缀规则，自动匹配（核心逻辑）
        foreach (var (_, targetPrefix) in EmulatorMultiOpenArgumentPrefixes)
        {
            // 检查配置是否以当前前缀开头（匹配成功则尝试提取）
            if (emulatorConfig.StartsWith(targetPrefix, StringComparison.Ordinal))
            {
                // 截取前缀后的内容（去除首尾空白，避免空格干扰）
                var indexPart = emulatorConfig.Substring(targetPrefix.Length).Trim();

                // 尝试转换为非负整数 → 有效则直接返回（找到唯一匹配）
                if (int.TryParse(indexPart, out int index) && index >= 0)
                {
                    return index;
                }
            }
        }

        // 3. 无任何前缀匹配 或 匹配后不是有效数字 → 返回 -1
        return -1;
    }
}
