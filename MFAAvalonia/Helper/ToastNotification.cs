using Avalonia;
using Avalonia.Platform;
using MFAAvalonia.Views.Windows;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;


namespace MFAAvalonia.Helper;

public class ToastNotification
{
    // 单例实例
    public static ToastNotification Instance { get; } = new();

    // 存储当前显示的Toast（按显示顺序排列，第一个在最下方）
    private readonly List<NotificationView> _toastQueue = [];

    // 配置参数（可根据需求调整）
    public const int MarginBottom = 2; // 最底部Toast距离屏幕底部的间距
    public const int ToastSpacing = 16; // 两个Toast之间的间距
    public const int MarginRight = 2; // 最底部Toast距离屏幕底部的间距
    private ToastNotification() { }


    public static void Show(string title, string content = "", int duration = 4000, bool sound = true)
    {
        DispatcherHelper.PostOnMainThread(() =>
        {
            Instance.AddToast(new NotificationView
            {
                TitleText = title,
                MessageText = content,
                Duration = duration
            });
        });
        PlayNotificationSound(sound);
    }

    /// <summary>
    /// 添加新Toast到队列并显示
    /// </summary>
    public void AddToast(NotificationView toast)
    {
        // 注册Toast关闭事件（关闭时从队列移除并重新排列）
        toast.Closed += (s, e) => RemoveToast(toast);

        // 添加到队列尾部（新Toast在最下方）
        _toastQueue.Add(toast);
        UpdateAllToastPositions(toast);
        // 显示Toast
        toast.Show();
    }

    /// <summary>
    /// 从队列移除Toast并重新排列
    /// </summary>
    public void RemoveToast(NotificationView toast)
    {
        if (_toastQueue.Remove(toast))
        {
            // 重新计算所有Toast的位置（带动画）
            UpdateAllToastPositions();
        }
    }

    /// <summary>
    /// 重新计算并更新所有Toast的位置（核心逻辑）
    /// </summary>
    private void UpdateAllToastPositions(NotificationView? newToast = null)
    {
        DispatcherHelper.PostOnMainThread(() =>
        {
            var screen = Instances.RootView.Screens.Primary;
            if (screen == null) return;

            // 从屏幕工作区底部开始计算（排除任务栏等区域）
            double currentY = screen.WorkingArea.Bottom - MarginBottom;

            // 倒序遍历：最新的Toast在最下方，旧的依次往上排
            for (int i = _toastQueue.Count - 1; i >= 0; i--)
            {
                var toast = _toastQueue[i];
                if (toast.IsClosed) continue;

                // 关键：使用缓存的实际高度，避免Bounds未更新导致的0值
                double toastHeight = toast.ActualToastHeight;

                // 先减去当前Toast的高度（定位到顶部）
                currentY -= toastHeight;

                // 计算目标位置（靠右对齐，垂直位置为currentY）
                var targetPosition = new PixelPoint(
                    (int)(screen.WorkingArea.Right - toast.Bounds.Width - MarginBottom), // 靠右无重叠
                    (int)currentY
                );

                // 移动到目标位置（新Toast可能需要跳过首次计算，避免抖动）
                if (toast != newToast)
                {
                    toast.MoveTo(targetPosition, TimeSpan.FromMilliseconds(300));
                }
                else
                {
                    // 新Toast直接定位，避免动画冲突
                    toast.Position = targetPosition;
                }

                // 预留Toast之间的间距
                currentY -= ToastSpacing;
            }
        });
    }

    public static void PlayNotificationSound(bool enable = true)
    {
        if (!enable) return;
        TaskManager.RunTask(async () =>
        {
            var uriString = "avares://MFAAvalonia/Assets/Sound/SystemNotification.wav";
            var uri = new Uri(uriString);

            if (!AssetLoader.Exists(uri))
            {
                LoggerHelper.Error($"未找到嵌入资源：{uriString}");
            }
            var stream = AssetLoader.Open(uri);

            stream.Seek(0, SeekOrigin.Begin);

            // 使用NAudio播放Stream中的WAV
            await using var reader = new WaveFileReader(stream); // 读取WAV流
            using var output = new WaveOutEvent(); // 跨平台输出设备

            output.Init(reader); // 初始化输出
            output.Play(); // 开始播放

            // 等待播放完成（避免线程结束导致播放中断）
            while (output.PlaybackState == PlaybackState.Playing)
            {
                await Task.Delay(100);
            }
        }, "播放音频");
    }
}
