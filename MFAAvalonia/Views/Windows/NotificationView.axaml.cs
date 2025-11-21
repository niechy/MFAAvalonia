using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    public static readonly StyledProperty<int> DurationProperty =
        AvaloniaProperty.Register<NotificationView, int>(
            nameof(Duration),
            2000);

    public int Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    // 超时时间（默认3秒）
    private Timer? _autoCloseTimer;
    private readonly TimeSpan _timeout;
    private bool _isClosed = false;

    private volatile bool _isClosing = false;

    public bool IsClosed => _isClosed;
    public bool IsClosing => _isClosing;
    public NotificationView()
    {
        DataContext = this;
        InitializeComponent();
        _timeout = TimeSpan.FromMilliseconds(Duration);

        Opened += (_, _) =>
        {
            var screen = this.GetHostScreen();
            if (screen == null) return;
            double scaling = screen.Scaling;

            // 将设备无关尺寸转换为物理像素
            double physicalWidth = this.Bounds.Width * scaling;

            // 更准确的初始位置计算
            var x = screen.WorkingArea.Right - (int)physicalWidth - ToastNotification.MarginRight;
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
                LoggerHelper.Info("开局");
                MoveTo(new PixelPoint(targetX, targetY), TimeSpan.FromMilliseconds(100), endAction: () =>
                {
                    LoggerHelper.Info("测试");
                    StartAutoCloseTimer();
                });
            });
        });
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

    // Linux X11 API
    [DllImport("libX11.so.6")]
    [SupportedOSPlatform("linux")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    [SupportedOSPlatform("linux")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    [SupportedOSPlatform("linux")]
    private static extern int XSetTransientForHint(IntPtr display, IntPtr window, IntPtr parent);

    [DllImport("libX11.so.6")]
    [SupportedOSPlatform("linux")]
    private static extern IntPtr XInternAtom(IntPtr display, string atom_name, [MarshalAs(UnmanagedType.Bool)] bool only_if_exists);

    [DllImport("libX11.so.6")]
    [SupportedOSPlatform("linux")]
    private static extern int XChangeProperty(IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr type,
        int format,
        int mode,
        IntPtr data,
        int nelements);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // 跨平台窗口隐藏设置
        SetWindowHideFromTaskSwitcher();

        DispatcherHelper.RunOnMainThreadAsync(
            StartSlideInAnimation, DispatcherPriority.Render);
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
            else if (OperatingSystem.IsLinux())
            {
                SetLinuxWindowProperties(handle);
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

    /// <summary>
    /// Linux平台：通过X11设置窗口属性
    /// </summary>
    [SupportedOSPlatform("linux")]
    private void SetLinuxWindowProperties(IntPtr handle)
    {
        try
        {
            IntPtr display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                LoggerHelper.Warning("无法打开X11显示连接");
                return;
            }

            try
            {
                // 设置为临时窗口（不会出现在任务栏）
                IntPtr rootWindow = GetRootWindow(display);
                XSetTransientForHint(display, handle, rootWindow);

                // 设置窗口类型为工具提示或通知类型
                SetWindowType(display, handle, "_NET_WM_WINDOW_TYPE_NOTIFICATION");

                LoggerHelper.Info("Linux窗口属性设置成功");
            }
            finally
            {
                XCloseDisplay(display);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"Linux窗口属性设置失败: {ex.Message}");
        }
    }

    // Linux辅助方法
    [SupportedOSPlatform("linux")]
    private IntPtr GetRootWindow(IntPtr display)
    {
        // 获取默认根窗口
        [DllImport("libX11.so.6")]
        extern static IntPtr XDefaultRootWindow(IntPtr display);

        return XDefaultRootWindow(display);
    }

    [SupportedOSPlatform("linux")]
    private void SetWindowType(IntPtr display, IntPtr window, string windowType)
    {
        try
        {
            IntPtr typeAtom = XInternAtom(display, windowType, false);
            IntPtr typeProperty = XInternAtom(display, "_NET_WM_WINDOW_TYPE", false);
            IntPtr atomType = XInternAtom(display, "ATOM", false);

            if (typeAtom != IntPtr.Zero && typeProperty != IntPtr.Zero)
            {
                XChangeProperty(display, window, typeProperty, atomType, 32, 0,
                    typeAtom
                    , 1);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"设置窗口类型失败: {ex.Message}");
        }
    }
}
