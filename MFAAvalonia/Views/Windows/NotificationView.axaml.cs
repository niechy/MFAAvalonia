using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MFAAvalonia.Helper;
using SukiUI.Controls;
using System;
using System.Diagnostics;
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
    public bool IsClosed => _isClosed;
    public NotificationView()
    {
        DataContext = this;
        InitializeComponent();
        _timeout = TimeSpan.FromMilliseconds(Duration);
        Opened += (sender, args) =>
        {
            var screen = Screens.Primary;
            var x = screen.WorkingArea.Width - (int)Bounds.Width - ToastNotification.MarginRight;
            var y = screen.Bounds.Height;
            Position = new PixelPoint(
                x,
                y
            );
        };

        Loaded += (sender, args) => StartSlideInAnimation();


        ActionButton.Click += (s, e) =>
        {
            OnActionButtonClicked?.Invoke();
            StopAutoCloseTimer(); // 手动操作后取消自动关闭
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
    public void MoveTo(PixelPoint targetPosition, TimeSpan duration, Action? action = null)
    {
        if (_isClosed) return;

        // 记录起始位置（必须在UI线程获取，确保准确）
        var startPos = Position;
        if (startPos == targetPosition) return;

        var totalMs = duration.TotalMilliseconds;
        if (totalMs <= 0)
        {
            DispatcherHelper.PostOnMainThread(() => Position = targetPosition); // UI线程更新
            return;
        }

        var startTime = DateTime.Now;

        // 用Task.Run避免阻塞UI，但更新位置必须回UI线程
        TaskManager.RunTask(async () =>
        {
            while (DateTime.Now.Subtract(startTime).TotalMilliseconds < totalMs && !_isClosed)
            {
                var progress = Math.Min(DateTime.Now.Subtract(startTime).TotalMilliseconds / totalMs, 1.0);
                var currentX = (int)(startPos.X + (targetPosition.X - startPos.X) * progress);
                var currentY = (int)(startPos.Y + (targetPosition.Y - startPos.Y) * progress);

                // 关键：在UI线程更新Position，确保实时生效
                DispatcherHelper.PostOnMainThread(() =>
                {
                    if (!_isClosed) Position = new PixelPoint(currentX, currentY);
                });

                await Task.Delay(5); // 5ms间隔（200fps），更平滑且性能可接受
            }

            // 最终位置校准（UI线程）
            if (!_isClosed)
            {
                DispatcherHelper.PostOnMainThread(() =>
                {
                    Position = targetPosition;
                    action?.Invoke();
                });
            }
        }, noMessage: true);
    }

    // 滑入动画（从右侧滑入）
    private void StartSlideInAnimation()
    {
        var screen = Screens.Primary;
        var x = screen.WorkingArea.Width;
        var y = screen.WorkingArea.Height;

        MoveTo(new PixelPoint(
            (int)(x - Bounds.Width - ToastNotification.MarginRight),
            (int)(y - Bounds.Height - ToastNotification.MarginBottom) // 距离顶部50px
        ), TimeSpan.FromMilliseconds(100), StartAutoCloseTimer);
        ActualToastHeight = Bounds.Height;
    }
    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        StopAutoCloseTimer();
        CloseWithAnimation();
    }

    // 滑出动画（向右侧滑出）
    private void CloseWithAnimation()
    {
        var screen = Screens.Primary;
        if (screen == null) return;

        // 目标位置（屏幕右侧外部）
        var targetX = screen.WorkingArea.Right;
        var targetPosition = new PixelPoint(targetX, Position.Y);
        MoveTo(targetPosition, TimeSpan.FromMilliseconds(150), Close);
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
