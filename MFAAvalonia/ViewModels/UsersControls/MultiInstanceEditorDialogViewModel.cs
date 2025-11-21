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
    
    // public string? Output { get; set; }
}
