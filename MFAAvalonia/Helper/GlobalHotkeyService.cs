using Avalonia.Input;
using Avalonia.Threading;
using MFAAvalonia.Extensions;
using SharpHook;
using SharpHook.Data;
using SharpHook.Native;
using System;
using System.Collections.Concurrent;
using System.Windows.Input;

namespace MFAAvalonia.Helper;

public static class GlobalHotkeyService
{
    // 线程安全的热键存储（Key: 组合键标识，Value: 关联命令）
    private static readonly ConcurrentDictionary<(KeyCode, EventMask), ICommand> _commands = new();
    private static TaskPoolGlobalHook? _hook;
    public static bool IsStopped = false;
    /// <summary>
    /// 初始化全局钩子服务
    /// </summary>
    public static void Initialize()
    {
        if (_hook != null) return;

        try
        {
            _hook = new TaskPoolGlobalHook();
            _hook.KeyPressed += HandleKeyEvent;
            _hook.RunAsync(); // 启动后台监听线程
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
            ToastHelper.Error(LangKeys.GlobalHotkeyServiceError.ToLocalization());
        }
    }

    /// <summary>
    /// 注册全局热键（跨平台）
    /// </summary>
    /// <param name="gesture">热键手势（需转换为SharpHook的按键标识）</param>
    /// <param name="command">关联命令</param>
    public static bool Register(KeyGesture? gesture, ICommand command)
    {
        if (gesture == null || command == null)
            return true;
        var (keyCode, modifiers) = ConvertGesture(gesture);
        LoggerHelper.Info($"register Hotkey,modifiers: {modifiers},keyCode: {keyCode}");
        return _commands.TryAdd((keyCode, modifiers), command);
    }

    /// <summary>
    /// 注销全局热键
    /// </summary>
    public static void Unregister(KeyGesture? gesture)
    {
        if (gesture == null) return;

        var (keyCode, modifiers) = ConvertGesture(gesture);
        _commands.TryRemove((keyCode, modifiers), out _);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public static void Shutdown()
    {
        _hook?.Dispose();
        _commands.Clear();
        IsStopped = true;
    }
    private static KeyCode ErrorHandle(KeyGesture gesture)
    {
        if (Enum.TryParse(typeof(KeyCode), $"Vc{gesture.Key.ToString()}", out var key))
        {
            return
                (KeyCode)key;
        }

        LoggerHelper.Warning($"热键映射失败：未找到 KeyCode.Vc{gesture.Key.ToString()}");
        return KeyCode.VcEscape;
    }

    // 转换Avalonia手势到SharpHook标识
// 替换 GlobalHotkeyService 中的 ConvertGesture 方法
    private static (KeyCode, EventMask) ConvertGesture(KeyGesture gesture)
    {
        // 1. 修复数字键、特殊字符键的 KeyCode 映射
        var keyCode = gesture.Key switch
        {
            // 数字键映射（Avalonia Key.D0-D9 → SharpHook Vc0-Vc9）
            Key.D0 => KeyCode.Vc0,
            Key.D1 => KeyCode.Vc1,
            Key.D2 => KeyCode.Vc2,
            Key.D3 => KeyCode.Vc3,
            Key.D4 => KeyCode.Vc4,
            Key.D5 => KeyCode.Vc5,
            Key.D6 => KeyCode.Vc6,
            Key.D7 => KeyCode.Vc7,
            Key.D8 => KeyCode.Vc8,
            Key.D9 => KeyCode.Vc9,

            // 特殊字符键映射（Avalonia OemXXX → SharpHook 对应键）
            Key.OemPlus => KeyCode.VcEquals, // 加号（+）
            Key.OemMinus => KeyCode.VcMinus, // 减号（-）
            Key.OemComma => KeyCode.VcComma, // 逗号（,）
            Key.OemPeriod => KeyCode.VcPeriod, // 句号（.）
            Key.OemQuestion => KeyCode.VcSlash, // 问号（?）
            Key.OemSemicolon => KeyCode.VcSemicolon, // 分号（;）
            Key.OemQuotes => KeyCode.VcQuote, // 单引号（'）
            Key.OemOpenBrackets => KeyCode.VcOpenBracket, // 左括号（[）
            Key.OemCloseBrackets => KeyCode.VcCloseBracket, // 右括号（]）
            Key.OemPipe => KeyCode.VcBackslash, // 竖线（|）
            Key.OemTilde => KeyCode.VcBackQuote, // 波浪号（~）
            Key.NumPad0 => KeyCode.VcO,
            Key.NumPad1 => KeyCode.Vc1,
            Key.NumPad2 => KeyCode.Vc2,
            Key.NumPad3 => KeyCode.Vc3,
            Key.NumPad4 => KeyCode.Vc4,
            Key.NumPad5 => KeyCode.Vc5,
            Key.NumPad6 => KeyCode.Vc6,
            Key.NumPad7 => KeyCode.Vc7,
            Key.NumPad8 => KeyCode.Vc8,
            Key.NumPad9 => KeyCode.Vc9,
            // 功能键（可选补充，避免遗漏）
            Key.Enter => KeyCode.VcEnter,
            Key.Space => KeyCode.VcSpace,
            Key.Tab => KeyCode.VcTab,
            Key.Back => KeyCode.VcBackspace,

            // 其他键（字母、功能键等）保持原有逻辑
            _ => ErrorHandle(gesture)
        };

        // 2. 修复组合修饰键（支持 Ctrl+Shift、Alt+Ctrl 等）
        var modifiers = EventMask.None;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
            modifiers |= EventMask.LeftCtrl | EventMask.RightCtrl; // 同时支持左右Ctrl
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
            modifiers |= EventMask.LeftAlt | EventMask.RightAlt; // 同时支持左右Alt
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
            modifiers |= EventMask.LeftShift | EventMask.RightShift; // 同时支持左右Shift
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
            modifiers |= EventMask.LeftMeta | EventMask.RightMeta; // 同时支持左右Win键

        return (keyCode, modifiers);
    }

    // 处理全局按键事件
// 修改 HandleKeyEvent 方法
    private static void HandleKeyEvent(object? sender, KeyboardHookEventArgs e)
    {
        // 1. 定义：归一化后的修饰键掩码（合并左/右键）
        const EventMask ControlMask = EventMask.LeftCtrl | EventMask.RightCtrl; // Ctrl 统一掩码
        const EventMask ShiftMask = EventMask.LeftShift | EventMask.RightShift; // Shift 统一掩码
        const EventMask AltMask = EventMask.LeftAlt | EventMask.RightAlt;       // Alt 统一掩码（可选，保持完整）
        const EventMask MetaMask = EventMask.LeftMeta | EventMask.RightMeta;     // Win 键统一掩码（可选）

        // 2. 获取原始修饰键和键码
        var rawModifiers = e.RawEvent.Mask;
        var keyCode = e.Data.KeyCode;

        // 3. 修饰键归一化：左/右键 → 统一修饰键
        var normalizedModifiers = EventMask.None;
        if ((rawModifiers & ControlMask) != 0) // 包含左Ctrl或右Ctrl → 视为 Control
            normalizedModifiers |= ControlMask;
        if ((rawModifiers & ShiftMask) != 0)   // 包含左Shift或右Shift → 视为 Shift
            normalizedModifiers |= ShiftMask;
        if ((rawModifiers & AltMask) != 0)     // 包含左Alt或右Alt → 视为 Alt
            normalizedModifiers |= AltMask;
        if ((rawModifiers & MetaMask) != 0)    // 包含左Win或右Win → 视为 Meta
            normalizedModifiers |= MetaMask;
        
        // 5. 用归一化后的修饰键匹配（注册时需用相同的统一掩码）
        if (_commands.TryGetValue((keyCode, normalizedModifiers), out var command) && command.CanExecute(null))
        {
            Dispatcher.UIThread.Post(() => command.Execute(null));
        }
    }
}
