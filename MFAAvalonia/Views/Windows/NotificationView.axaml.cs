using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using MFAAvalonia.Helper;
using SukiUI.Controls;
using SukiUI.Extensions;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Views.Windows;

[SupportedOSPlatform("windows")]
internal static class WindowsPInvoke
{
    // 窗口消息：系统设置变化（任务栏隐藏/显示、工作区变化等）
    public const uint WM_SETTINGCHANGE = 0x001A;

    // 系统参数：工作区大小变化
    public const uint SPI_SETWORKAREA = 0x002F;
    public const uint SPI_GETWORKAREA = 0x0030;

    // 获取最新工作区大小（仅保留这一个PInvoke，用于获取真实工作区）
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public extern static bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    // 矩形结构体（存储工作区坐标）
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        // 转换为 Avalonia Rect（适配设备缩放，修正坐标计算）
        public Rect ToAvaloniaRect()
        {
            return new Rect(
                Left, // 物理坐标转设备无关坐标
                Top,
                (Right - Left), // 宽度 = 右-左
                (Bottom - Top) // 高度 = 底-顶
            );
        }
    }
}

public partial class NotificationView : SukiWindow
{
    public double ActualToastHeight { get; private set; }

    public event Action? OnActionButtonClicked;
    public static readonly StyledProperty<bool> HasActionButtonProperty =
        AvaloniaProperty.Register<NotificationView, bool>(
            nameof(HasActionButton),
            false);

    public bool HasActionButton
    {
        get => GetValue(HasActionButtonProperty);
        set => SetValue(HasActionButtonProperty, value);
    }

    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<NotificationView, string>(
            nameof(TitleText),
            "Title");

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public static readonly StyledProperty<string> MessageTextProperty =
        AvaloniaProperty.Register<NotificationView, string>(
            nameof(MessageText),
            "Content");

    public string MessageText
    {
        get => GetValue(MessageTextProperty);
        set => SetValue(MessageTextProperty, value);
    }

    public static readonly StyledProperty<object?> ActionButtonContentProperty =
        AvaloniaProperty.Register<NotificationView, object?>(
            nameof(ActionButtonContent),
            "");

    public object? ActionButtonContent
    {
        get => GetValue(ActionButtonContentProperty);
        set => SetValue(ActionButtonContentProperty, value);
    }

    // 超时时间（默认3秒）
    private Timer? _autoCloseTimer;
    private readonly TimeSpan _timeout;
    private bool _isClosed = false;

    private volatile bool _isClosing = false;

    public bool IsClosed => _isClosed;
    public bool IsClosing => _isClosing;
    public PixelRect GetLatestWorkArea(Screen screen)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                WindowsPInvoke.RECT winRect = new();
                // 调用 Windows 原生 API 获取最新工作区（不受 Avalonia 缓存影响）
                if (WindowsPInvoke.SystemParametersInfo(WindowsPInvoke.SPI_GETWORKAREA, 0, ref winRect, 0))
                {
                    // 转换为 Avalonia 设备无关坐标
                    return PixelRect.FromRect(winRect.ToAvaloniaRect(), screen.Scaling);
                }
                else
                {
                    LoggerHelper.Info("获取失败");
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Warning($"获取原生工作区失败， fallback 到 Avalonia WorkingArea: {ex.Message}");
            }
        }

        // 非 Windows 平台或获取失败时，使用 Avalonia 原生 WorkingArea
        return screen.WorkingArea;
    }

    public NotificationView(long duration)
    {
        DataContext = this;
        InitializeComponent();
        _timeout = TimeSpan.FromMilliseconds(duration);

        Opened += (_, _) =>
        {
            var screen = this.GetHostScreen();
            if (screen == null) return;
            double scaling = screen.Scaling;

            // 将设备无关尺寸转换为物理像素
            double physicalWidth = this.Bounds.Width * scaling;

            // 更准确的初始位置计算
            var x = (int)(GetLatestWorkArea(screen).Right - physicalWidth - ToastNotification.MarginRight * scaling);
            var y = screen.Bounds.Bottom;

            Position = new PixelPoint(x, y);
        };

        // 添加尺寸变化监听
        LayoutUpdated += (s, e) =>
        {
            // 布局更新时重新记录实际高度
            if (Bounds.Height > 0)
            {
                var screen = this.GetHostScreen();
                if (screen == null) return;
                double scaling = screen.Scaling;
                ActualToastHeight = Bounds.Height * scaling;
            }
        };

        ActionButton.Click += (_, _) =>
        {
            OnActionButtonClicked?.Invoke();
            StopAutoCloseTimer();
            CloseWithAnimation();
        };
    }


    public void SetContent(string title, string message)
    {
        TitleText = title;
        MessageText = message;
    }

    // 设置自定义按钮（可选）
    public void SetActionButton(string text, Action onClick)
    {
        ActionButtonContent = text;
        HasActionButton = true;
        OnActionButtonClicked += onClick;
    }
    // 带动画移动到目标位置（供管理器调用）
    public void MoveTo(PixelPoint targetPosition, TimeSpan duration, Action? endAction = null, Action? action = null, bool force = false)
    {
        var callback = endAction;
        // 关闭/正在关闭直接返回
        if (_isClosed || _isClosing) return;

        TaskManager.RunTask(async () =>
        {
            // 中途关闭则终止
            if (!force && (_isClosed || _isClosing)) return;

            // 前置操作（主线程执行）
            action?.Invoke();

            // 主线程获取起始位置+判断移动轴（保留方向区分，多方向移动平滑）
            var (startPos, isXChanged, isYChanged) = (Position, Position.X != targetPosition.X,
                Position.Y != targetPosition.Y);
            // 无任何轴需要移动，直接回调
            if (!isXChanged && !isYChanged)
            {
                await DispatcherHelper.RunOnMainThreadAsync(() => endAction?.Invoke());
                return;
            }

            // 瞬时移动（时长<=0）
            if (duration.TotalMilliseconds <= 0)
            {
                await DispatcherHelper.RunOnMainThreadAsync(() =>
                {
                    if (!_isClosed)
                    {
                        Position = targetPosition;
                        endAction?.Invoke();
                    }
                });
                return;
            }

            var startTime = DateTime.Now;
            var totalMs = duration.TotalMilliseconds;

            // 循环外先记录移动方向（基于初始位置，避免动态Position干扰）
            var isXIncreasing = targetPosition.X > startPos.X; // X轴：目标 > 起始 → 向右移
            var isYIncreasing = targetPosition.Y > startPos.Y; // Y轴：目标 > 起始 → 向下移

            while (!_isClosed)
            {
                // 计算动画进度（0~1）
                var progress = Math.Min((DateTime.Now - startTime).TotalMilliseconds / totalMs, 1.0);

                // 只更新未到达目标的轴
                int currentX = Position.X;
                int currentY = Position.Y;

                if (isXChanged)
                {
                    currentX = (int)(currentX + (targetPosition.X - currentX) * progress);
                    // 根据移动方向判断：是否到达或超出目标X
                    bool xReached = isXIncreasing ? currentX >= targetPosition.X : currentX <= targetPosition.X;
                    if (xReached)
                    {
                        isXChanged = false; // 后续不再更新X轴
                    }
                }

                if (isYChanged)
                {
                    currentY = (int)(currentY + (targetPosition.Y - currentY) * progress);
                    // 根据移动方向判断：是否到达或超出目标Y
                    bool yReached = isYIncreasing ? currentY >= targetPosition.Y : currentY <= targetPosition.Y;
                    if (yReached)
                    {
                        isYChanged = false; // 后续不再更新Y轴
                    }
                }

                // 主线程更新位置（仅更新变化后的位置）
                await DispatcherHelper.RunOnMainThreadAsync(() =>
                {
                    if (!_isClosed)
                        Position = new PixelPoint(currentX, currentY);
                });

                // 所有轴都到达目标，或动画进度完成 → 退出循环
                if ((!isXChanged && !isYChanged) || progress >= 1.0)
                {
                    callback?.Invoke();

                    break;
                }
                await Task.Delay(5); // 200fps平滑动画
            }
            // 最终
        }, noMessage: true);
    }


// 滑入动画（从右侧滑入）
    private void StartSlideInAnimation()
    {
        TaskManager.RunTask(async () =>
        {
            // 等待布局完成
            await Task.Delay(50);

            // 在UI线程上获取准确的尺寸
            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                var screen = this.GetHostScreen();
                if (screen == null) return;

                // 确保窗口已经完成布局
                Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Arrange(new Rect(this.DesiredSize));

                // 现在获取的高度是准确的

                double scaling = screen.Scaling;

                // 将设备无关尺寸转换为物理像素
                double physicalWidth = this.Bounds.Width * scaling;
                double physicalHeight = this.Bounds.Height * scaling;
                ActualToastHeight = physicalHeight;
                var targetX = (int)(screen.WorkingArea.Right - physicalWidth - ToastNotification.MarginRight * scaling);
                var targetY = (int)(screen.WorkingArea.Bottom - physicalHeight - ToastNotification.MarginBottom * scaling);
                MoveTo(new PixelPoint(targetX, targetY), TimeSpan.FromMilliseconds(100), StartAutoCloseTimer);
            });
        }, noMessage: true);
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        StopAutoCloseTimer();
        CloseWithAnimation();
    }

// 滑出动画（向右侧滑出）
    private void CloseWithAnimation()
    {
        var screen = this.GetHostScreen();
        if (screen == null) return;

        // 目标位置（屏幕右侧外部）
        var targetX = screen.WorkingArea.Right;
        var targetPosition = new PixelPoint(targetX, Position.Y);
        MoveTo(targetPosition, TimeSpan.FromMilliseconds(150), Close, () => _isClosing = true, true);
    }

// 自动关闭定时器（保持不变）
    private void StartAutoCloseTimer()
    {
        _autoCloseTimer = new Timer(_ =>
        {
            DispatcherHelper.PostOnMainThread(CloseWithAnimation);
        }, null, _timeout, Timeout.InfiniteTimeSpan);
    }

    private void StopAutoCloseTimer()
    {
        _autoCloseTimer?.Dispose();
        _autoCloseTimer = null;
    }
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _isClosed = true;
        base.OnClosing(e);
    }

    // Windows API (已存在)
    [DllImport("user32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EX_STYLE = -20;
    private const int WS_EX_APPWINDOW = 0x00040000, WS_EX_TOOLWINDOW = 0x00000080;


    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // 跨平台窗口隐藏设置
        if (OperatingSystem.IsWindows())
        {
            HookWndProcForWorkAreaChange();
            SetWindowHideFromTaskSwitcher();
        }

        DispatcherHelper.RunOnMainThreadAsync(
            StartSlideInAnimation, DispatcherPriority.Render);
    }

    [SupportedOSPlatform("windows")]
    private void HookWndProcForWorkAreaChange()
    {
        try
        {
            var topLevel = GetTopLevel(this);
            var handle = topLevel?.TryGetPlatformHandle()?.Handle;
            if (handle == null || handle == IntPtr.Zero)
            {
                LoggerHelper.Warning("无法获取窗口句柄，无法监听工作区变化");
                return;
            }
            
            // 注册窗口消息钩子，监听 WM_SETTINGCHANGE
            Win32Properties.AddWndProcHookCallback(topLevel, (hwnd, msg, wParam, lParam, ref handled) =>
            {
                // 只处理 "系统设置变化" + "工作区大小变化" 事件
                if (msg == WindowsPInvoke.WM_SETTINGCHANGE && (uint)wParam == WindowsPInvoke.SPI_SETWORKAREA)
                {
                    // 用 SystemIdle 优先级，确保获取最新工作区（兼容 Win7）
                    DispatcherHelper.RunOnMainThreadAsync(() =>
                    {
                        ToastNotification.Instance.UpdateAllToastPositions();
                    }, DispatcherPriority.SystemIdle);

                    handled = true;
                }
                return IntPtr.Zero;
            });
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"注册窗口钩子失败: {ex.Message}", ex);
        }
    }


    /// <summary>
    /// 设置窗口不在任务切换器（Alt+Tab/Task View）中显示
    /// </summary>
    private void SetWindowHideFromTaskSwitcher()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel?.TryGetPlatformHandle()?.Handle is not IntPtr handle || handle == IntPtr.Zero)
        {
            LoggerHelper.Warning("无法获取窗口平台句柄");
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                SetWindowsWindowStyle(handle);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"设置窗口隐藏属性失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Windows平台：通过设置窗口样式隐藏
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void SetWindowsWindowStyle(IntPtr handle)
    {
        int currentStyle = GetWindowLong(handle, GWL_EX_STYLE);
        int newStyle = (currentStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLong(handle, GWL_EX_STYLE, newStyle);
    }
}
