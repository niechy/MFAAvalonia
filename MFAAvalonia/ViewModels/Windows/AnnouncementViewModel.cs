using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Markdown.Avalonia;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Views.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Windows;

public class AnnouncementItem
{
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public string Content { get; set; } = string.Empty;
}

public partial class AnnouncementViewModel : ViewModelBase
{
    public static readonly string AnnouncementFolder = "Announcement";

    [ObservableProperty] private AvaloniaList<AnnouncementItem> _announcementItems = new();
    [ObservableProperty] private AnnouncementItem? _selectedAnnouncement;
    [ObservableProperty] private string _announcementContent; // 绑定到 MarkdownScrollViewer.Markdown
    [ObservableProperty] private bool _doNotRemindThisAnnouncementAgain = Convert.ToBoolean(
        GlobalConfiguration.GetValue(ConfigurationKeys.DoNotShowAnnouncementAgain, bool.FalseString));

    #region 懒加载核心字段（适配 MarkdownScrollViewer 内部 ScrollViewer）

    private List<string> _allBatches = new(); // 缓存所有拆分后的 Markdown 批次
    private int _currentBatchIndex = 0; // 当前加载到的批次索引
    private bool _isLoadingNextBatch = false; // 防止重复加载
    private WeakReference<ScrollViewer>? _innerScrollViewerWeakRef; // 弱引用持有内部 ScrollViewer
    private const int _loadThreshold = 200; // 触底加载阈值（像素）
    private const int _initialBatchCount = 2; // 初始加载批次（让用户先看到内容）
    private const int _batchSize = 120; // 每批行数（可调整）
    private CancellationTokenSource? _lazyLoadCts; // 懒加载取消令牌

    #endregion

    partial void OnDoNotRemindThisAnnouncementAgainChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.DoNotShowAnnouncementAgain, value.ToString());
    }

    // 选中项变更时：清理之前的懒加载状态，重新初始化
    partial void OnSelectedAnnouncementChanged(AnnouncementItem? value)
    {
        if (value is null) return;
        
        // 取消之前的加载任务
        if (_lazyLoadCts != null)
        {
            _lazyLoadCts.Cancel();
            _lazyLoadCts.Dispose();
            _lazyLoadCts = null;
        }
        // 清理滚动监听和资源
        CleanupScrollListener();

        AnnouncementContent = string.Empty;
        _view?.Viewer.ScrollViewer.ScrollToHome();
        
        _ = LoadContentForSelectedItemAsync(value);
        SetMarkdownScrollViewer(_view?.Viewer, false);

    }

    // 核心：加载公告内容（Markdown 格式），拆分批次缓存
    async private Task LoadContentForSelectedItemAsync(AnnouncementItem item)
    {
        try
        {
            if (item.Content.Length > 4000)
            {
                // 初始化懒加载（拆分批次 + 初始加载）
                await InitializeLazyLoadAsync(item.Content);
            }
            else
            {
                AnnouncementContent = item.Content;
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"加载公告内容失败: {item.FilePath}, 错误: {ex.Message}");
            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                AnnouncementContent = $"### 加载失败\n{ex.Message}"; // Markdown 格式提示
            });
        }
    }

    #region 懒加载核心逻辑（适配 MarkdownScrollViewer 内部 ScrollViewer）

    /// <summary>
    /// 由 View 调用：传递 MarkdownScrollViewer 实例，提取内部 ScrollViewer 监听滚动
    /// </summary>
    public void SetMarkdownScrollViewer(MarkdownScrollViewer? markdownScrollViewer, bool clear = true)
    {
        // 清理之前的监听
        if (clear)
            CleanupScrollListener();

        if (markdownScrollViewer == null) return;
        try
        {
            // 从 MarkdownScrollViewer 的 VisualChildren 提取内部 ScrollViewer（源码唯一子元素）

            // 弱引用持有内部 ScrollViewer，避免内存泄漏
            _innerScrollViewerWeakRef = new WeakReference<ScrollViewer>(markdownScrollViewer.ScrollViewer);
            // 监听滚动事件（核心：直接响应内部 ScrollViewer 的滚动变化）
            markdownScrollViewer.ScrollViewer.ScrollChanged += InnerScrollViewer_ScrollChanged;

        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"提取 MarkdownScrollViewer 内部 ScrollViewer 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 内部 ScrollViewer 滚动事件回调：判断是否触底，触发下一批加载
    /// </summary>
    async private void InnerScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (_isLoadingNextBatch) return; // 正在加载中，忽略
        if (_currentBatchIndex >= _allBatches.Count) return; // 所有批次已加载完成

        // 触底判断：当前滚动偏移 + 可视高度 >= 内容总高度 - 阈值（避免空白）
        bool isNearBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height
            >= scrollViewer.Extent.Height - _loadThreshold;

        if (isNearBottom)
        {
            await LoadNextBatchAsync();
        }
    }

    /// <summary>
    /// 初始化懒加载：拆分 Markdown 文本为批次 + 加载初始批次
    /// </summary>
    async private Task InitializeLazyLoadAsync(string fullMarkdownContent)
    {
        _lazyLoadCts = new CancellationTokenSource();
        var cancellationToken = _lazyLoadCts.Token;

        try
        {
            // 1. 拆分 Markdown 文本为批次（按行拆分，不破坏格式）
            var lines = fullMarkdownContent.Split([Environment.NewLine], StringSplitOptions.None);
            _allBatches = SplitIntoMarkdownBatches(lines, _batchSize);
            _currentBatchIndex = 0;

            // 2. UI 线程清空原有内容（低优先级，不阻塞交互）
            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                AnnouncementContent = string.Empty;
            }, DispatcherPriority.Background, cancellationToken);

            // 3. 加载初始批次（1-2批，让用户快速看到内容）
            await LoadInitialBatchesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 切换选中项时取消，忽略异常
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"初始化懒加载失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载初始批次（1-2批）
    /// </summary>
    async private Task LoadInitialBatchesAsync(CancellationToken cancellationToken)
    {
        int loadCount = Math.Min(_initialBatchCount, _allBatches.Count);
        for (int i = 0; i < loadCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LoadBatchAsync(_allBatches[i]);
            _currentBatchIndex++;
        }
    }

    /// <summary>
    /// 加载下一批 Markdown 文本
    /// </summary>
    async private Task LoadNextBatchAsync()
    {
        if (_isLoadingNextBatch || _currentBatchIndex >= _allBatches.Count)
            return;

        _isLoadingNextBatch = true;
        try
        {
            var batchText = _allBatches[_currentBatchIndex];
            await LoadBatchAsync(batchText);
            _currentBatchIndex++;

            // 所有批次加载完成，移除滚动监听
            if (_currentBatchIndex >= _allBatches.Count)
            {
                CleanupScrollListener();
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"加载下一批文本失败: {ex.Message}");
        }
        finally
        {
            _isLoadingNextBatch = false;
        }
    }

    /// <summary>
    /// 加载单个批次（UI 线程，低优先级）
    /// </summary>
    private async Task LoadBatchAsync(string batchText)
    {
        await DispatcherHelper.RunOnMainThreadAsync(() =>
        {
            // 追加批次文本（Markdown 格式兼容）
            AnnouncementContent += batchText;
        }, DispatcherPriority.Background);

        // 让出 CPU 时间片，确保窗口拖动等交互流畅
        await Task.Delay(1);
    }

    /// <summary>
    /// 将 Markdown 行列表拆分为批次（保持格式完整性）
    /// </summary>
    private List<string> SplitIntoMarkdownBatches(string[] lines, int batchSize)
    {
        var batches = new List<string>();
        var sb = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            sb.AppendLine(lines[i]);

            // 每 batchSize 行生成一个批次，或最后一行时强制生成
            if ((i + 1) % batchSize == 0 || i == lines.Length - 1)
            {
                batches.Add(sb.ToString());
                sb.Clear();
            }
        }

        return batches;
    }

    /// <summary>
    /// 清理滚动监听（避免内存泄漏）
    /// </summary>
    private void CleanupScrollListener()
    {
        if (_innerScrollViewerWeakRef?.TryGetTarget(out var innerScrollViewer) == true)
        {
            innerScrollViewer.ScrollChanged -= InnerScrollViewer_ScrollChanged;
        }
        _innerScrollViewerWeakRef = null;
        _allBatches.Clear();
        _currentBatchIndex = 0;
    }

    /// <summary>
    /// 清理懒加载所有资源（切换选中项/窗口关闭时调用）
    /// </summary>
    private void CleanupLazyLoadResources()
    {
        // 取消并释放加载任务
        if (_lazyLoadCts != null)
        {
            _lazyLoadCts.Cancel();
            _lazyLoadCts.Dispose();
            _lazyLoadCts = null;
        }

        // 清理滚动监听（内部已重置 _allBatches 和 _currentBatchIndex）
        CleanupScrollListener();

        // 重置状态
        _isLoadingNextBatch = false;
    }

    #endregion

    /// <summary>
    /// 加载公告元数据（Markdown 文件列表）
    /// </summary>
    private async Task LoadAnnouncementMetadataAsync()
    {
        try
        {
            var resourcePath = Path.Combine(AppContext.BaseDirectory, "resource");
            var announcementDir = Path.Combine(resourcePath, AnnouncementFolder);

            if (!Directory.Exists(announcementDir))
            {
                LoggerHelper.Warning($"公告文件夹不存在: {announcementDir}");
                return;
            }

            // 后台线程获取 Markdown 文件列表
            var mdFiles = await Task.Run(() =>
                Directory.GetFiles(announcementDir, "*.md")
                    .OrderBy(Path.GetFileName)
                    .ToList()
            ).ConfigureAwait(false);

            var tempItems = new List<AnnouncementItem>();
            foreach (var mdFile in mdFiles)
            {
                try
                {
                    // 读取第一行作为标题（Markdown 标题可能以 # 开头，已在 ReadFirstLineAsync 中处理）
                    var fileContent = await File.ReadAllTextAsync(mdFile);
                    SplitFirstLine(fileContent, out string firstLine, out var content);
                    var title = firstLine.TrimStart('#', ' ').Trim();
                    tempItems.Add(new AnnouncementItem
                    {
                        Title = title,
                        FilePath = mdFile,
                        Content = content,
                        LastModified = File.GetLastWriteTime(mdFile)
                    });
                }
                catch (Exception ex)
                {
                    LoggerHelper.Error($"读取公告元数据失败: {mdFile}, 错误: {ex.Message}");
                }
            }

            // UI 线程更新公告列表
            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                AnnouncementItems.Clear();
                AnnouncementItems.AddRange(tempItems);

                // 默认选中第一个公告
                if (AnnouncementItems.Any())
                {
                    SelectedAnnouncement = AnnouncementItems[0];
                }
                LoggerHelper.Info($"公告数量：{AnnouncementItems.Count}");
            });
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"加载公告元数据失败: {ex.Message}");
        }
    }

    // 拆分 Markdown 第一行（标题）和剩余内容
    public static void SplitFirstLine(string content, out string firstLine, out string remainingContent)
    {
        if (string.IsNullOrEmpty(content))
        {
            firstLine = "";
            remainingContent = "";
            return;
        }

        var newLineCandidates = new[]
        {
            "\r\n",
            "\n",
            "\r"
        };
        int firstNewLineIndex = int.MaxValue;
        string matchedNewLine = null;

        foreach (var nl in newLineCandidates)
        {
            int index = content.IndexOf(nl);
            if (index != -1 && index < firstNewLineIndex)
            {
                firstNewLineIndex = index;
                matchedNewLine = nl;
            }
        }

        if (matchedNewLine == null)
        {
            firstLine = content;
            remainingContent = "";
        }
        else
        {
            firstLine = content.Substring(0, firstNewLineIndex);
            // 保留剩余内容的 Markdown 格式（包含换行符）
            remainingContent = content.Substring(firstNewLineIndex + matchedNewLine.Length);
        }
    }

    private AnnouncementView? _view;

    public void SetView(AnnouncementView? view)
    {
        _view = view;
    }

    public static async void CheckAnnouncement(bool forceShow = false)
    {
        try
        {
            var viewModel = new AnnouncementViewModel();
            await viewModel.LoadAnnouncementMetadataAsync();
            
            if (forceShow)
            {
                if (!viewModel.AnnouncementItems.Any())
                {
                    ToastHelper.Warn(LangKeys.Warning.ToLocalization(), LangKeys.AnnouncementEmpty.ToLocalization());
                    return;
                }
            }
            else if (viewModel.DoNotRemindThisAnnouncementAgain || !viewModel.AnnouncementItems.Any())
            {
                return;
            }

            var announcementView = new AnnouncementView
            {
                DataContext = viewModel
            };
            viewModel.SetView(announcementView);
            viewModel.SetMarkdownScrollViewer(announcementView.Viewer);
            announcementView.Show();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"显示公告窗口失败: {ex.Message}");
        }
    }

    // 窗口关闭时清理资源
    public void Cleanup()
    {
        CleanupLazyLoadResources();
    }
}
