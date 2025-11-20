using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MFAAvalonia.Helper;
using SukiUI.Controls;
using SukiUI.Extensions;
using System;
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

            // 更准确的初始位置计算
            var x = screen.WorkingArea.Right - (int)this.Width - ToastNotification.MarginRight;
            var y = screen.Bounds.Bottom;

            Position = new PixelPoint(x, y);
        };

        // 使用Render优先级确保布局完成
        Loaded += (_, _) => DispatcherHelper.RunOnMainThreadAsync(
            StartSlideInAnimation, DispatcherPriority.Render);

        // 添加尺寸变化监听
        LayoutUpdated += (s, e) =>
        {
            // 布局更新时重新记录实际高度
            if (Bounds.Height > 0)
            {
                ActualToastHeight = Bounds.Height;
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
        // 关闭/正在关闭直接返回
        if (_isClosed || _isClosing) return;

        TaskManager.RunTask(async () =>
        {
            // 中途关闭则终止
            if (!force && (_isClosed || _isClosing || targetPosition == Position)) return;

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
                    break;

                await Task.Delay(5); // 200fps平滑动画
            }

            // 最终

            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                // Position = targetPosition;
                endAction?.Invoke();
            });


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
                ActualToastHeight = Bounds.Height;

                var targetX = (int)(screen.WorkingArea.Right - Bounds.Width - ToastNotification.MarginRight);
                var targetY = (int)(screen.WorkingArea.Bottom - Bounds.Height - ToastNotification.MarginBottom);

                MoveTo(new PixelPoint(targetX, targetY), TimeSpan.FromMilliseconds(100), StartAutoCloseTimer);
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
}
