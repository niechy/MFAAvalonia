using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace MFAAvalonia.Extensions;

public static class ScrollViewerExtensions
{
    // 滚动方向控制 - 支持任意 Control（包括 ListBox 等）
    public static readonly AttachedProperty<PanningMode> PanningModeProperty =
        AvaloniaProperty.RegisterAttached<Control, PanningMode>(
            "PanningMode", typeof(ScrollViewerExtensions), PanningMode.Both);

    // 自动滚动控制 - 支持任意 Control（包括 ListBox 等）
    public static readonly AttachedProperty<bool> AutoScrollProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "AutoScroll", typeof(ScrollViewerExtensions), false);

    static ScrollViewerExtensions()
    {
        PanningModeProperty.Changed.AddClassHandler<Control>(OnPanningModeChanged);
        AutoScrollProperty.Changed.AddClassHandler<Control>(OnAutoScrollChanged);
    }

    #region 属性设置器

    public static void SetPanningMode(Control element, PanningMode value) =>
        element.SetValue(PanningModeProperty, value);

    public static PanningMode GetPanningMode(Control element) =>
        element.GetValue(PanningModeProperty);

    public static void SetAutoScroll(Control element, bool value) =>
        element.SetValue(AutoScrollProperty, value);

    public static bool GetAutoScroll(Control element) =>
        element.GetValue(AutoScrollProperty);

    #endregion

    #region 辅助方法

    /// <summary>
    /// 从控件中获取 ScrollViewer（支持 ScrollViewer 本身或包含 ScrollViewer 的控件如 ListBox）
    /// </summary>
    private static ScrollViewer? GetScrollViewer(Control control)
    {
        if (control is ScrollViewer sv)
            return sv;

        // 对于 ListBox、DataGrid 等控件，查找内部的 ScrollViewer
        return control.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

        /// <summary>
        /// 延迟获取 ScrollViewer（等待控件模板应用并添加到可视树后）
        /// </summary>
        private static void WithScrollViewer(Control control, Action<ScrollViewer> action)
        {
            var scrollViewer = GetScrollViewer(control);
            if (scrollViewer != null)
            {
                action(scrollViewer);
                return;
            }
    
            // 需要同时等待模板应用和添加到可视树
            // 因为 GetVisualDescendants() 只有在控件添加到可视树后才能工作
            
            void TryGetScrollViewer()
            {
                var sv = GetScrollViewer(control);
                if (sv != null)
                {
                    action(sv);
                }
            }
    
            void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
            {
                control.AttachedToVisualTree -= OnAttachedToVisualTree;
                // 使用 Post 确保模板已完全应用
                Dispatcher.UIThread.Post(TryGetScrollViewer,DispatcherPriority.Loaded);
            }
    
            if (control is TemplatedControl templatedControl)
            {
                void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
                {
                    templatedControl.TemplateApplied -= OnTemplateApplied;
                    // 模板应用后，检查是否已在可视树中
                    if (control.IsAttachedToVisualTree())
                    {
                        Dispatcher.UIThread.Post(TryGetScrollViewer, DispatcherPriority.Loaded);
                    }
                    else
                    {
                        // 还没有添加到可视树，等待添加
                        control.AttachedToVisualTree += OnAttachedToVisualTree;
                    }
                }
    
                templatedControl.TemplateApplied += OnTemplateApplied;
            }else
            {
                // 非模板控件，只等待添加到可视树
                control.AttachedToVisualTree += OnAttachedToVisualTree;
            }
        }

    #endregion

    #region 逻辑处理

    private static void OnPanningModeChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        WithScrollViewer(control, scrollViewer =>
        {
            var mode = (PanningMode)(args.NewValue ?? PanningMode.Both);

            scrollViewer.HorizontalScrollBarVisibility = mode switch
            {
                PanningMode.VerticalOnly => ScrollBarVisibility.Disabled,
                PanningMode.HorizontalOnly => ScrollBarVisibility.Auto,
                _ => ScrollBarVisibility.Auto
            };

            scrollViewer.VerticalScrollBarVisibility = mode switch
            {
                PanningMode.HorizontalOnly => ScrollBarVisibility.Disabled,
                PanningMode.VerticalOnly => ScrollBarVisibility.Auto,
                _ => ScrollBarVisibility.Auto
            };
        });
    }

    private static void OnAutoScrollChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        var isAutoScroll = args.NewValue is true;

        WithScrollViewer(control, scrollViewer =>
        {
            // 使用 Tag 存储事件处理器引用，以便后续移除
            var handlerKey = $"AutoScrollHandler_{control.GetHashCode()}";

            if (isAutoScroll)
            {
                EventHandler? layoutHandler = null;
                EventHandler<AvaloniaPropertyChangedEventArgs>? propertyHandler = null;

                layoutHandler = (sender, e) =>
                {
                    // 只有当用户在底部附近时才自动滚动
                    if (scrollViewer.Offset.Y >= scrollViewer.ScrollBarMaximum.Y - 10)
                    {
                        Dispatcher.UIThread.Post(() => scrollViewer.ScrollToEnd(), DispatcherPriority.Background);
                    }
                };

                propertyHandler = (sender, e) =>
                {
                    // 用户手动滚动时，如果不在底部则停止自动滚动
                    if (e.Property == ScrollViewer.OffsetProperty)
                    {
                        var offset = scrollViewer.Offset;
                        var maxOffset = scrollViewer.ScrollBarMaximum;
                        // 如果用户滚动到了非底部位置，禁用自动滚动
                        if (offset.Y < maxOffset.Y - 50)
                        {
                            control.SetValue(AutoScrollProperty, false);
                        }
                    }
                };

                // 存储处理器引用
                scrollViewer.Tag = (layoutHandler, propertyHandler);

                // 监听布局变化
                scrollViewer.LayoutUpdated += layoutHandler;

                // 监听用户手动滚动
                scrollViewer.PropertyChanged += propertyHandler;

                // 初始滚动到底部
                Dispatcher.UIThread.Post(() => scrollViewer.ScrollToEnd(), DispatcherPriority.Background);
            }
            else
            {
                // 移除事件监听
                if (scrollViewer.Tag is (EventHandler layoutHandler, EventHandler<AvaloniaPropertyChangedEventArgs> propertyHandler))
                {
                    scrollViewer.LayoutUpdated -= layoutHandler;
                    scrollViewer.PropertyChanged -= propertyHandler;
                    scrollViewer.Tag = null;
                }
            }
        });
    }

    #endregion
}

public enum PanningMode
{
    VerticalOnly,
    HorizontalOnly,
    Both
}
