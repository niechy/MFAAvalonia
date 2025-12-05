using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ColorDocument.Avalonia;
using ColorDocument.Avalonia.DocumentElements;
using Markdown.Avalonia.Plugins;
using Markdown.Avalonia.StyleCollections;
using Markdown.Avalonia.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MdStyle = Markdown.Avalonia.MarkdownStyle;

namespace Markdown.Avalonia
{
    public class MarkdownScrollViewer : Control
    {
        public static readonly DirectProperty<MarkdownScrollViewer, Uri?> SourceDirectProperty =
            AvaloniaProperty.RegisterDirect<MarkdownScrollViewer, Uri?>(
                nameof(Source),
                o => o.Source,
                (o, v) => o.Source = v);

        public static readonly AvaloniaProperty<Uri?> SourceProperty = SourceDirectProperty;

        private static readonly DirectProperty<MarkdownScrollViewer, string?> MarkdownDirectProperty =
            AvaloniaProperty.RegisterDirect<MarkdownScrollViewer, string?>(
                nameof(Markdown),
                o => o.Markdown,
                (o, v) => o.Markdown = v);

        public static readonly AvaloniaProperty<string?> MarkdownProperty = MarkdownDirectProperty;

        private static readonly AvaloniaProperty<IStyle> MarkdownStyleProperty =
            AvaloniaProperty.RegisterDirect<MarkdownScrollViewer, IStyle>(
                nameof(MarkdownStyle),
                o => o.MarkdownStyle,
                (o, v) => o.MarkdownStyle = v);

        public static readonly AvaloniaProperty<string?> MarkdownStyleNameProperty =
            AvaloniaProperty.RegisterDirect<MarkdownScrollViewer, string?>(
                nameof(MarkdownStyleName),
                o => o.MarkdownStyleName,
                (o, v) => o.MarkdownStyleName = v);

        public static readonly AvaloniaProperty<string?> AssetPathRootProperty =
            AvaloniaProperty.RegisterDirect<MarkdownScrollViewer, string?>(
                nameof(AssetPathRoot),
                o => o.AssetPathRoot,
                (o, v) => o.AssetPathRoot = v);

        public static readonly StyledProperty<bool> SaveScrollValueWhenContentUpdatedProperty =
            AvaloniaProperty.Register<MarkdownScrollViewer, bool>(
                nameof(SaveScrollValueWhenContentUpdated),
                defaultValue: false);

        public static readonly StyledProperty<bool> EnableProgressiveRenderingProperty =
            AvaloniaProperty.Register<MarkdownScrollViewer, bool>(
                nameof(EnableProgressiveRendering),
                defaultValue: true);

        public static readonly StyledProperty<int> InitialRenderLinesProperty =
            AvaloniaProperty.Register<MarkdownScrollViewer, int>(
                nameof(InitialRenderLines),
                defaultValue: 300); // 初始渲染300行，确保用户能看到足够内容

        public static readonly StyledProperty<int> ProgressiveRenderBatchSizeProperty =
            AvaloniaProperty.Register<MarkdownScrollViewer, int>(
                nameof(ProgressiveRenderBatchSize),
                defaultValue: 200); // 每批次渲染200行，加快渲染速度

        public static readonly StyledProperty<int> ProgressiveRenderDelayMsProperty =
            AvaloniaProperty.Register<MarkdownScrollViewer, int>(
                nameof(ProgressiveRenderDelayMs),
                defaultValue: 10); // 减少延迟，加快渲染

        public static readonly AvaloniaProperty<Vector> ScrollValueProperty =
            AvaloniaProperty.RegisterDirect<MarkdownScrollViewer, Vector>(
                nameof(ScrollValue),
                owner => owner.ScrollValue,
                (owner, v) => owner.ScrollValue = v);

        public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
            SelectableTextBlock.SelectionBrushProperty.AddOwner<MarkdownScrollViewer>();

        public static readonly AvaloniaProperty<bool> SelectionEnabledProperty =
            AvaloniaProperty.RegisterDirect<MarkdownScrollViewer, bool>(
                nameof(SelectionEnabled),
                owner => owner.SelectionEnabled,
                (owner, v) => owner.SelectionEnabled = v);

        private static readonly HttpClient s_httpclient = new();

        /// <summary>
        /// 预编译的换行符分割正则
        /// </summary>
        private static readonly Regex s_newlineSplitter = new(@"\r\n|\r|\n", RegexOptions.Compiled);

        #region 文档缓存

        /// <summary>
        /// 获取文档缓存管理器实例
        /// </summary>
        public static MarkdownDocumentCache DocumentCache => MarkdownDocumentCache.Instance;

        /// <summary>
        /// 计算 Markdown 内容的哈希值
        /// </summary>
        private static string ComputeContentHash(string content)
        {
            return MarkdownDocumentCache.ComputeContentHash(content);
        }

        /// <summary>
        /// 尝试从缓存获取已解析的文档
        /// </summary>
        private static DocumentElement? TryGetFromCache(string contentHash)
        {
            DocumentCache.TryGet(contentHash, out var document);
            return document;
        }

        /// <summary>
        /// 将解析后的文档添加到缓存
        /// </summary>
        private static void AddToCache(string contentHash, DocumentElement document, string markdownContent)
        {
            DocumentCache.Add(contentHash, document, markdownContent);
        }

        /// <summary>
        /// 清除文档缓存
        /// </summary>
        public static void ClearDocumentCache()
        {
            DocumentCache.Clear();
        }

        /// <summary>
        /// 获取当前缓存大小
        /// </summary>
        public static int CacheSize => DocumentCache.Count;

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public static CacheStatistics GetCacheStatistics()
        {
            return DocumentCache.GetStatistics();
        }

        #endregion

        #region 渐进式渲染

        /// <summary>
        /// 大文件阈值（行数），超过此值启用渐进式渲染
        /// </summary>
        private const int LargeFileThreshold = 200;

        /// <summary>
        /// 当前渐进式渲染的取消令牌源
        /// </summary>
        private CancellationTokenSource? _progressiveRenderCts;

        /// <summary>
        /// 是否正在进行渐进式渲染
        /// </summary>
        private bool _isProgressiveRendering;

        /// <summary>
        /// 渐进式渲染完成事件
        /// </summary>
        public event EventHandler? ProgressiveRenderingCompleted;

        /// <summary>
        /// 渐进式渲染进度变化事件
        /// </summary>
        public event EventHandler<ProgressiveRenderingProgressEventArgs>? ProgressiveRenderingProgress;

        #endregion

        private readonly ScrollViewer _viewer;
        private SetupInfo _setup;
        private DocumentElement? _document;
        private IBrush? _selectionBrush;
        private Wrapper _wrapper;

        public MarkdownScrollViewer()
        {
            _plugins = new MdAvPlugins();
            _setup = Plugins.Info;

            var md = new Markdown();
            md.CascadeResources.SetParent(this);
            md.UseResource = _useResource;
            md.Plugins = _plugins;

            _engine = md;

            if (nvl(ThemeDetector.IsFluentAvaloniaUsed))
            {
                _markdownStyleName = nameof(MdStyle.FluentAvalonia);
                _markdownStyle = MdStyle.FluentAvalonia;
            }
            else if (nvl(ThemeDetector.IsFluentUsed))
            {
                _markdownStyleName = nameof(MdStyle.FluentTheme);
                _markdownStyle = MdStyle.FluentTheme;
            }
            else if (nvl(ThemeDetector.IsSimpleUsed))
            {
                _markdownStyleName = nameof(MdStyle.SimpleTheme);
                _markdownStyle = MdStyle.SimpleTheme;
            }
            else
            {
                _markdownStyleName = nameof(MdStyle.Standard);
                _markdownStyle = MdStyle.Standard;
            }
            Styles.Insert(0, _markdownStyle);
            TrySetupSelectionBrush(_markdownStyle);

            _viewer = new ScrollViewer()
            {
                // TODO: ScrollViewer does not seem to take Padding into account in 11.0.0-preview1
                Padding = new Thickness(0),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };

            ((ISetLogicalParent)_viewer).SetParent(this);
            VisualChildren.Add(_viewer);
            LogicalChildren.Add(_viewer);

            EditStyle(_markdownStyle);

            static bool nvl(bool? vl) => vl.HasValue && vl.Value;

            // 使用命名方法而非匿名 lambda，便于取消订阅
            _viewer.ScrollChanged += Viewer_ScrollChanged;
            _viewer.PointerPressed += _viewer_PointerPressed;
            _viewer.PointerMoved += _viewer_PointerMoved;
            _viewer.PointerReleased += _viewer_PointerReleased;

            _wrapper = new Wrapper(this);
            _viewer.Content = _wrapper;
        }

        #region text selection

        private bool _isLeftButtonPressed;
        private Point _startPoint;

        private void Viewer_ScrollChanged(object? sender, ScrollChangedEventArgs e) => OnScrollChanged();

        private void _viewer_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_document is null) return;
            if (!SelectionEnabled) return;

            var point = e.GetCurrentPoint(_document.Control);
            if (point.Properties.IsLeftButtonPressed && _document is not null)
            {
                _isLeftButtonPressed = true;
                _startPoint = point.Position;
                _document.Select(_startPoint, point.Position);

                this.Focus();
            }
        }

        private void _viewer_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_document is null) return;

            var point = e.GetCurrentPoint(_document.Control);
            if (_isLeftButtonPressed && point.Properties.IsLeftButtonPressed)
            {
                if (_document is not null)
                    _document.Select(_startPoint, point.Position);
            }
        }

        private void _viewer_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_document is null) return;

            var point = e.GetCurrentPoint(_document.Control);
            if (_isLeftButtonPressed && !point.Properties.IsLeftButtonPressed)
            {
                _isLeftButtonPressed = false;

                if (_document is not null)
                    _document.Select(_startPoint, point.Position);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!SelectionEnabled) return;

            // Ctrl+C
            if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
            {
                if (_document is not null
                    && TopLevel.GetTopLevel(this) is TopLevel top
                    && top.Clipboard is IClipboard clipboard)
                {
                    clipboard.SetTextAsync(_document.GetSelectedText());
                }
            }
        }

        #endregion

        public event HeaderScrolled? HeaderScrolled;
        private List<HeaderRect>? _headerRects;
        private HeaderScrolledEventArgs? _eventArgs;

        private void OnViewportSizeChanged(object? obj, EventArgs arg)
        {
            _headerRects = null;
            _wrapper.Restructure();
        }

        private void OnScrollChanged()
        {
            if (HeaderScrolled is null) return;

            double offsetY = _viewer.Offset.Y;
            double viewHeight = _viewer.Viewport.Height;

            if (_headerRects is null)
            {
                if (_document is null) return;

                _headerRects = new List<HeaderRect>();
                foreach (var doc in _document.Children.OfType<HeaderElement>())
                {
                    var t = doc.GetRect(this);
                    var rect = new Rect(t.Left, t.Top + offsetY, t.Width, t.Height);
                    _headerRects.Add(new HeaderRect(rect, doc));
                }
            }

            var tree = new Header?[5];
            var viewing = new List<Header>();

            tree[0] = _headerRects.Where(rct => rct.Header.Level == 1)
                .Select(rct => CreateHeader(rct))
                .FirstOrDefault();

            foreach (var headerRect in _headerRects)
            {
                var boundY = headerRect.BaseBound.Bottom - offsetY;

                if (boundY < 0)
                {
                    var header = CreateHeader(headerRect);
                    tree[header.Level - 1] = header;

                    for (var i = header.Level; i < tree.Length; ++i)
                        tree[i] = null;
                }
                else if (0 <= boundY && boundY < viewHeight)
                {
                    viewing.Add(CreateHeader(headerRect));
                }
                else break;
            }

            var newEvArg = new HeaderScrolledEventArgs(tree.OfType<Header>().ToList(), viewing);
            if (_eventArgs != newEvArg)
            {
                _eventArgs = newEvArg;
                HeaderScrolled(this, _eventArgs);
            }

            static Header CreateHeader(HeaderRect headerRect)
            {
                var header = headerRect.Header;
                return new Header(header.Level, header.Text);
            }
        }

        private void EditStyle(IStyle mdstyle)
        {
            if (mdstyle is INamedStyle { IsEditted: false } nameStyle
                && mdstyle is Styles styles)
            {
                foreach (var edit in _setup.StyleEdits)
                    edit.Edit(nameStyle.Name, styles);

                nameStyle.IsEditted = true;
            }
        }

        private void TrySetupSelectionBrush(IStyle style)
        {
            _selectionBrush = null;

            var key = "MarkdownScrollViewer.SelectionBrush";
            if (style.TryGetResource(key, null, out var brushObj)
                && brushObj is IBrush brush)
            {
                _selectionBrush = brush;
            }
        }

        /// <summary>
        /// 当前内容的哈希值（用于缓存检查）
        /// </summary>
        private string? _currentContentHash;

        /// <summary>
        /// 是否启用渐进式渲染
        /// </summary>
        public bool EnableProgressiveRendering
        {
            get => GetValue(EnableProgressiveRenderingProperty);
            set => SetValue(EnableProgressiveRenderingProperty, value);
        }

        /// <summary>
        /// 初始渲染的行数
        /// </summary>
        public int InitialRenderLines
        {
            get => GetValue(InitialRenderLinesProperty);
            set => SetValue(InitialRenderLinesProperty, value);
        }

        /// <summary>
        /// 渐进式渲染每批次的行数
        /// </summary>
        public int ProgressiveRenderBatchSize
        {
            get => GetValue(ProgressiveRenderBatchSizeProperty);
            set => SetValue(ProgressiveRenderBatchSizeProperty, value);
        }

        /// <summary>
        /// 渐进式渲染批次之间的延迟（毫秒）
        /// </summary>
        public int ProgressiveRenderDelayMs
        {
            get => GetValue(ProgressiveRenderDelayMsProperty);
            set => SetValue(ProgressiveRenderDelayMsProperty, value);
        }

        /// <summary>
        /// 是否正在进行渐进式渲染
        /// </summary>
        public bool IsProgressiveRendering => _isProgressiveRendering;

        /// <summary>
        /// 取消当前的渐进式渲染
        /// </summary>
        public void CancelProgressiveRendering()
        {
            _progressiveRenderCts?.Cancel();
            _progressiveRenderCts = null;
            _isProgressiveRendering = false;
        }

        private void UpdateMarkdown()
        {
            if (_wrapper.Document is null && String.IsNullOrEmpty(Markdown))
                return;

            // 首先取消任何正在进行的渐进式渲染
            CancelProgressiveRendering();

            try
            {
                var markdownContent = Markdown ?? "";
                var contentHash = ComputeContentHash(markdownContent);

                // 如果内容哈希相同，无需更新
                if (_currentContentHash == contentHash && _wrapper.Document != null && !_isProgressiveRendering)
                {
                    System.Diagnostics.Debug.WriteLine("Markdown 内容未变化，跳过更新");
                    return;
                }

                var ofst = _viewer.Offset;
                DocumentElement? newDocument = null;

                // 注意：由于 Avalonia 控件不能被多个父级共享，我们不再使用缓存的文档进行增量更新
                // 每次内容变化都需要重新解析以获得新的控件实例，这样可以避免控件复用导致的显示问题

                // 重新解析内容
                // 检查是否需要渐进式渲染
                var lines = markdownContent.Split(new[]
                {
                    "\r\n",
                    "\r",
                    "\n"
                }, StringSplitOptions.None);
                bool shouldUseProgressiveRendering = EnableProgressiveRendering && lines.Length > LargeFileThreshold;

                if (shouldUseProgressiveRendering)
                {
                    // 使用渐进式渲染
                    StartProgressiveRendering(markdownContent, lines, contentHash, ofst);
                    return;
                }

                newDocument = _engine.TransformElement(markdownContent);
                newDocument.Control.Classes.Add("Markdown_Avalonia_MarkdownViewer");

                // 全量更新（不使用增量更新，避免控件复用导致的显示问题）
                _document = newDocument;
                _currentContentHash = contentHash;

                if (_wrapper.Document?.Control is Control oldContentControl)
                {
                    oldContentControl.SizeChanged -= OnViewportSizeChanged;
                }

                _wrapper.Document = _document;

                if (_wrapper.Document?.Control is Control newContentControl)
                {
                    newContentControl.SizeChanged += OnViewportSizeChanged;
                }

                _headerRects = null;

                if (SaveScrollValueWhenContentUpdated)
                    _viewer.Offset = ofst;

            }
            catch (StackOverflowException ex)
            {
                // 处理堆栈溢出异常（通常是由于 Markdown 内容深度嵌套导致的）
                System.Diagnostics.Debug.WriteLine($"Markdown 解析堆栈溢出: {ex.Message}");
                CreateErrorDocument("Markdown 内容解析失败：内容嵌套过深或格式有问题");
            }
            catch (RegexMatchTimeoutException ex)
            {
                // 处理正则表达式超时异常
                System.Diagnostics.Debug.WriteLine($"Markdown 正则表达式匹配超时: {ex.Message}");
                CreateErrorDocument("Markdown 内容解析失败：内容格式复杂，解析超时");
            }
            catch (OutOfMemoryException ex)
            {
                // 处理内存溢出异常（可能是由于大量递归或复杂正则导致的）
                System.Diagnostics.Debug.WriteLine($"Markdown 解析内存溢出: {ex.Message}");
                CreateErrorDocument("Markdown 内容解析失败：内容过大或格式过于复杂");
            }
            catch (Exception ex)
            {
                // 处理其他解析异常
                System.Diagnostics.Debug.WriteLine($"Markdown 解析失败: {ex.Message}\n{ex.StackTrace}");
                var errorMsg = ex.Message.Length > 100 ? ex.Message.Substring(0, 100) + "..." : ex.Message;
                CreateErrorDocument($"Markdown 内容解析失败: {errorMsg}");
            }
        }

        /// <summary>
        /// 启动渐进式渲染
        /// </summary>
        private void StartProgressiveRendering(string fullContent, string[] lines, string contentHash, Vector savedOffset)
        {
            // 取消之前的渐进式渲染
            CancelProgressiveRendering();

            _progressiveRenderCts = new CancellationTokenSource();
            _isProgressiveRendering = true;

            var ct = _progressiveRenderCts.Token;

            // 计算安全的初始渲染行数（不在代码块中间分割）
            int initialLines = FindSafeBreakPoint(lines, Math.Min(InitialRenderLines, lines.Length));
            string initialContent = string.Join("\n", lines.Take(initialLines));

            try
            {
                // 先渲染初始部分
                var initialDocument = _engine.TransformElement(initialContent);
                initialDocument.Control.Classes.Add("Markdown_Avalonia_MarkdownViewer");

                // 更新显示
                _document = initialDocument;
                _currentContentHash = null; // 暂时不设置哈希，因为还没渲染完

                if (_wrapper.Document?.Control is Control oldContentControl)
                {
                    oldContentControl.SizeChanged -= OnViewportSizeChanged;
                }

                _wrapper.Document = _document;

                if (_wrapper.Document?.Control is Control newContentControl)
                {
                    newContentControl.SizeChanged += OnViewportSizeChanged;
                }

                _headerRects = null;

                if (SaveScrollValueWhenContentUpdated)
                    _viewer.Offset = savedOffset;

                // 触发进度事件
                ProgressiveRenderingProgress?.Invoke(this, new ProgressiveRenderingProgressEventArgs(initialLines, lines.Length));

                // 如果还有剩余内容，异步加载
                if (initialLines < lines.Length)
                {
                    _ = ContinueProgressiveRenderingReplaceAsync(lines, initialLines, contentHash, fullContent, ct);
                }
                else
                {
                    // 渲染完成
                    _currentContentHash = contentHash;
                    AddToCache(contentHash, _document, fullContent);
                    _isProgressiveRendering = false;
                    ProgressiveRenderingCompleted?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"渐进式渲染初始部分失败: {ex.Message}");
                _isProgressiveRendering = false;

                // 回退到普通渲染
                try
                {
                    var fallbackDocument = _engine.TransformElement(fullContent);
                    fallbackDocument.Control.Classes.Add("Markdown_Avalonia_MarkdownViewer");
                    _document = fallbackDocument;
                    _currentContentHash = contentHash;
                    AddToCache(contentHash, _document, fullContent);

                    if (_wrapper.Document?.Control is Control oldCtrl)
                    {
                        oldCtrl.SizeChanged -= OnViewportSizeChanged;
                    }

                    _wrapper.Document = _document;

                    if (_wrapper.Document?.Control is Control newCtrl)
                    {
                        newCtrl.SizeChanged += OnViewportSizeChanged;
                    }

                    _headerRects = null;

                    if (SaveScrollValueWhenContentUpdated)
                        _viewer.Offset = savedOffset;
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"回退渲染也失败: {fallbackEx.Message}");
                    CreateErrorDocument($"Markdown 渲染失败: {fallbackEx.Message}");
                }
            }
        }

        /// <summary>
        /// 代码块开始标记的正则表达式（匹配 ``` 或 ~~~，可能带语言标识）
        /// </summary>
        private static readonly Regex s_codeBlockStartRegex = new Regex(@"^(\s*)(```|~~~)(\w*)\s*$", RegexOptions.Compiled);

        /// <summary>
        /// 代码块结束标记的正则表达式
        /// </summary>
        private static readonly Regex s_codeBlockEndRegex = new Regex(@"^(\s*)(```|~~~)\s*$", RegexOptions.Compiled);

        /// <summary>
        /// 查找安全的分割点（不在代码块中间）
        /// </summary>
        /// <param name="lines">所有行</param>
        /// <param name="targetLine">目标行号</param>
        /// <returns>安全的分割行号</returns>
        private int FindSafeBreakPoint(string[] lines, int targetLine)
        {
            if (targetLine >= lines.Length)
                return lines.Length;

            // 检查从开始到目标行是否在代码块内
            bool inCodeBlock = false;
            string? codeBlockMarker = null; // 记录代码块使用的标记（``` 或 ~~~）
            int lastSafePoint = 0;

            for (int i = 0; i < targetLine && i < lines.Length; i++)
            {
                var line = lines[i];

                if (!inCodeBlock)
                {
                    // 检查是否是代码块开始
                    var startMatch = s_codeBlockStartRegex.Match(line);
                    if (startMatch.Success)
                    {
                        inCodeBlock = true;
                        codeBlockMarker = startMatch.Groups[2].Value;
                    }
                    else
                    {
                        // 不在代码块内，这是一个安全点
                        lastSafePoint = i + 1;
                    }
                }
                else
                {
                    // 在代码块内，检查是否是代码块结束
                    var endMatch = s_codeBlockEndRegex.Match(line);
                    if (endMatch.Success && endMatch.Groups[2].Value == codeBlockMarker)
                    {
                        inCodeBlock = false;
                        codeBlockMarker = null;
                        // 代码块结束后是安全点
                        lastSafePoint = i + 1;
                    }
                }
            }

            // 如果目标行在代码块内，需要找到代码块结束的位置
            if (inCodeBlock)
            {
                // 继续向后查找代码块结束
                for (int i = targetLine; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var endMatch = s_codeBlockEndRegex.Match(line);
                    if (endMatch.Success && endMatch.Groups[2].Value == codeBlockMarker)
                    {
                        // 找到代码块结束，返回结束行的下一行
                        return i + 1;
                    }
                }
                // 如果没找到结束标记，返回所有行（整个文档）
                return lines.Length;
            }

            // 如果目标行不在代码块内，返回目标行
            return targetLine;
        }

        /// <summary>
        /// 异步继续渐进式渲染（替换模式）
        /// 每次重新解析从开始到当前位置的所有内容，确保代码块等结构完整
        /// </summary>
        private async Task ContinueProgressiveRenderingReplaceAsync(string[] lines, int startLine, string contentHash, string fullContent, CancellationToken ct)
        {
            int currentLine = startLine;
            int batchSize = ProgressiveRenderBatchSize;
            int delayMs = ProgressiveRenderDelayMs;

            try
            {
                while (currentLine < lines.Length && !ct.IsCancellationRequested)
                {
                    // 等待一小段时间，让UI有机会响应
                    await Task.Delay(delayMs, ct);

                    if (ct.IsCancellationRequested)
                        break;

                    // 计算这一批次要渲染到的行数（使用安全分割点）
                    int targetEndLine = Math.Min(currentLine + batchSize, lines.Length);
                    int endLine = FindSafeBreakPoint(lines, targetEndLine);

                    // 如果安全分割点没有前进，强制前进到目标位置或文档末尾
                    if (endLine <= currentLine)
                    {
                        endLine = Math.Min(targetEndLine, lines.Length);
                    }

                    // 解析从开始到当前位置的所有内容
                    string contentSoFar = string.Join("\n", lines.Take(endLine));

                    // 在UI线程上更新
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (ct.IsCancellationRequested)
                            return;

                        try
                        {
                            // 重新解析整个内容到当前位置
                            var newDocument = _engine.TransformElement(contentSoFar);
                            newDocument.Control.Classes.Add("Markdown_Avalonia_MarkdownViewer");
                            ReplaceDocument(newDocument);

                            _headerRects = null;

                            // 触发进度事件
                            ProgressiveRenderingProgress?.Invoke(this, new ProgressiveRenderingProgressEventArgs(endLine, lines.Length));
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"渐进式渲染批次失败: {ex.Message}");
                        }
                    }, DispatcherPriority.Background, ct);

                    currentLine = endLine;
                }

                // 渲染完成
                if (!ct.IsCancellationRequested)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _currentContentHash = contentHash;
                        if (_document != null)
                        {
                            AddToCache(contentHash, _document, fullContent);
                        }
                        _isProgressiveRendering = false;
                        ProgressiveRenderingCompleted?.Invoke(this, EventArgs.Empty);
                    }, DispatcherPriority.Background);
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("渐进式渲染被取消");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"渐进式渲染异常: {ex.Message}");
            }
            finally
            {
                _isProgressiveRendering = false;
            }
        }


        /// <summary>
        /// 替换当前文档
        /// </summary>
        private void ReplaceDocument(DocumentElement newDocument)
        {
            if (_wrapper.Document?.Control is Control oldContentControl)
            {
                oldContentControl.SizeChanged -= OnViewportSizeChanged;
            }

            _document = newDocument;
            _wrapper.Document = _document;

            if (_wrapper.Document?.Control is Control newContentControl)
            {
                newContentControl.SizeChanged += OnViewportSizeChanged;
            }
        }

        private void CreateErrorDocument(string errorMessage)
        {
            try
            {
                // 创建一个简单的文本块来显示错误消息
                var errorTextBlock = new TextBlock
                {
                    Text = errorMessage,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10),
                    Foreground = Brushes.Red
                };

                var errorBorder = new Border
                {
                    Child = errorTextBlock,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(10)
                };

                // 使用 UnBlockElement 包装错误控件，然后创建 DocumentRootElement
                var errorElement = new UnBlockElement(errorBorder);
                var errorDocument = new DocumentRootElement(new[]
                {
                    errorElement
                });

                var ofst = _viewer.Offset;

                if (_wrapper.Document?.Control is Control oldContentControl)
                {
                    oldContentControl.SizeChanged -= OnViewportSizeChanged;
                }

                _wrapper.Document = errorDocument;

                if (_wrapper.Document?.Control is Control newContentControl)
                {
                    newContentControl.SizeChanged += OnViewportSizeChanged;
                }

                _headerRects = null;

                if (SaveScrollValueWhenContentUpdated)
                    _viewer.Offset = ofst;
            }
            catch
            {
                // 如果创建错误文档也失败，至少清空内容避免崩溃
                _wrapper.Document = null;
            }
        }

        private IMarkdownEngine2 _engine;

        public IMarkdownEngineBase Engine
        {
            set
            {
                if (value is null)
                    throw new ArgumentNullException(nameof(Engine));

                if (value is IMarkdownEngine engine1)
                    _engine = engine1.Upgrade();
                else if (value is IMarkdownEngine2 engine)
                    _engine = engine;
                else
                    throw new ArgumentException();

                _engine.CascadeResources.SetParent(this);
                _engine.UseResource = _useResource;
                _engine.Plugins = _plugins;

                if (AssetPathRoot is not null)
                    _engine.AssetPathRoot = AssetPathRoot;
            }
            get => _engine;
        }

        private string? _AssetPathRoot;

        public string? AssetPathRoot
        {
            set
            {
                if (value is not null)
                {
                    _engine.AssetPathRoot = _AssetPathRoot = value;
                    UpdateMarkdown();
                }
            }
            get => _AssetPathRoot;
        }

        public bool SaveScrollValueWhenContentUpdated
        {
            set => SetValue(SaveScrollValueWhenContentUpdatedProperty, value);
            get => GetValue(SaveScrollValueWhenContentUpdatedProperty);
        }

        public Vector ScrollValue
        {
            set => _viewer.Offset = value;
            get => _viewer.Offset;
        }

        private bool _selectionEnabled;

        public bool SelectionEnabled
        {
            set
            {
                Focusable = _selectionEnabled = value;
            }
            get => _selectionEnabled;
        }

        [Content]
        public string? HereMarkdown

        {
            get
            {
                return Markdown;
            }
            set
            {
                if (String.IsNullOrEmpty(value))
                {
                    Markdown = value;
                }
                else
                {
                    // like PHP's flexible_heredoc_nowdoc_syntaxes,
                    // The indentation of the closing tag dictates 
                    // the amount of whitespace to strip from each line 
                    var lines = s_newlineSplitter.Split(value);

                    // count last line indent
                    int lastIdtCnt = TextUtil.CountIndent(lines.Last());
                    // count full indent
                    int someIdtCnt = lines
                        .Where(line => !String.IsNullOrWhiteSpace(line))
                        .Select(line => TextUtil.CountIndent(line))
                        .Min();

                    var indentCount = Math.Max(lastIdtCnt, someIdtCnt);

                    Markdown = String.Join(
                        "\n",
                        lines
                            // skip first blank line
                            .Skip(String.IsNullOrWhiteSpace(lines[0]) ? 1 : 0)
                            // strip indent
                            .Select(line =>
                            {
                                var realIdx = 0;
                                var viewIdx = 0;

                                while (viewIdx < indentCount && realIdx < line.Length)
                                {
                                    var c = line[realIdx];
                                    if (c == ' ')
                                    {
                                        realIdx += 1;
                                        viewIdx += 1;
                                    }
                                    else if (c == '\t')
                                    {
                                        realIdx += 1;
                                        viewIdx = ((viewIdx >> 2) + 1) << 2;
                                    }
                                    else break;
                                }

                                return line.Substring(realIdx);
                            })
                    );
                }
            }
        }

        private string? _markdown;

        public string? Markdown
        {
            get
            {
                return _markdown;
            }
            set
            {
                if (SetAndRaise(MarkdownDirectProperty, ref _markdown, value))
                {
                    UpdateMarkdown();
                }
            }
        }

        private Uri? _source;

        public Uri? Source
        {
            get
            {
                return _source;
            }
            set
            {
                if (!SetAndRaise(SourceDirectProperty, ref _source, value))
                    return;

                if (value is null)
                {
                    _source = value;
                    Markdown = null;
                    return;
                }

                if (!value.IsAbsoluteUri)
                    throw new ArgumentException("it is not absolute.");

                _source = value;

                switch (_source.Scheme)
                {
                    case "http":
                    case "https":
                        using (var res = s_httpclient.GetAsync(_source).Result)
                        using (var strm = res.Content.ReadAsStreamAsync().Result)
                        using (var reader = new StreamReader(strm, true))
                            Markdown = reader.ReadToEnd();
                        break;

                    case "file":
                        using (var strm = File.OpenRead(_source.LocalPath))
                        using (var reader = new StreamReader(strm, true))
                            Markdown = reader.ReadToEnd();
                        break;

                    case "avares":
                        using (var strm = AssetLoader.Open(_source))
                        using (var reader = new StreamReader(strm, true))
                            Markdown = reader.ReadToEnd();
                        break;

                    default:
                        throw new ArgumentException($"unsupport schema {_source.Scheme}");
                }

                AssetPathRoot =
                    value.Scheme == "file" ? value.LocalPath : value.AbsoluteUri;
            }
        }

        private IStyle _markdownStyle;

        public IStyle MarkdownStyle
        {
            get
            {
                return _markdownStyle;
            }
            set
            {
                if (value is null)
                    throw new ArgumentNullException(nameof(MarkdownStyle));

                if (_markdownStyle != value)
                {
                    EditStyle(value);

                    if (_markdownStyle is not null)
                        Styles.Remove(_markdownStyle);

                    Styles.Insert(0, value);

                    TrySetupSelectionBrush(value);
                    //ResetContent();
                }

                _markdownStyle = value;
            }
        }

        private string? _markdownStyleName;

        public string? MarkdownStyleName
        {
            get
            {
                return _markdownStyleName;
            }
            set
            {
                _markdownStyleName = value;

                if (_markdownStyleName is null)
                {
                    MarkdownStyle =
                        nvl(ThemeDetector.IsFluentAvaloniaUsed) ? MdStyle.FluentAvalonia :
                        nvl(ThemeDetector.IsFluentUsed) ? MdStyle.FluentTheme :
                        nvl(ThemeDetector.IsSimpleUsed) ? MdStyle.SimpleTheme :
                        MdStyle.Standard;
                }
                else if (_markdownStyleName == "Empty")
                {
                    MarkdownStyle = new Styles();
                }
                else
                {
                    var prop = typeof(MarkdownStyle).GetProperty(_markdownStyleName);
                    if (prop is null) return;

                    var propVal = prop.GetValue(null) as IStyle;
                    if (propVal is null) return;

                    MarkdownStyle = propVal;
                }

                static bool nvl(bool? vl) => vl.HasValue && vl.Value;
            }
        }

        private MdAvPlugins _plugins;

        public MdAvPlugins Plugins
        {
            get => _plugins;
            set
            {
                _plugins = _engine.Plugins = value;
                _setup = Plugins.Info;

                EditStyle(MarkdownStyle);
                UpdateMarkdown();
            }
        }

        private bool _useResource;

        public bool UseResource
        {
            get => _useResource;
            set
            {
                _engine.UseResource = value;
                _useResource = value;
                UpdateMarkdown();
            }
        }

        public IBrush? SelectionBrush
        {
            get => GetValue(SelectionBrushProperty);
            set => SetValue(SelectionBrushProperty, value);
        }

        internal IBrush ComputedSelectionBrush => SelectionBrush ?? _selectionBrush ?? Brushes.Cyan;


        public ScrollViewer ScrollViewer => _viewer;

        class HeaderRect
        {
            public Rect BaseBound { get; }
            public HeaderElement Header { get; }

            public HeaderRect(Rect bound, HeaderElement header)
            {
                BaseBound = bound;
                Header = header;
            }
        }

        class Wrapper : Control, ISelectionRenderHelper
        {
            private MarkdownScrollViewer _viewer;
            private readonly Canvas _canvas;
            private readonly Dictionary<Control, Rectangle> _rects;
            private DocumentElement? _document;

            public DocumentElement? Document
            {
                get => _document;
                set
                {
                    if (_document is not null)
                    {
                        VisualChildren.Remove(_document.Control);
                        LogicalChildren.Remove(_document.Control);
                        _document.Helper = null;
                        Clear();
                    }

                    _document = value;

                    if (_document is not null)
                    {
                        VisualChildren.Insert(0, _document.Control);
                        LogicalChildren.Insert(0, _document.Control);
                        _document.Helper = this;
                        InvalidateMeasure();
                    }
                }
            }

            public Wrapper(MarkdownScrollViewer v)
            {
                _viewer = v;
                _canvas = new Canvas();
                _canvas.PointerPressed += (s, e) => _document?.UnSelect();

                _rects = new Dictionary<Control, Rectangle>();

                VisualChildren.Add(_canvas);
            }

            public void Register(Control control)
            {
                if (!_rects.ContainsKey(control))
                {
                    var brush = _viewer.ComputedSelectionBrush;
                    var bounds = GetRectInDoc(control);
                    var rect = new Rectangle()
                    {
                        Width = bounds.Value.Width,
                        Height = bounds.Value.Height,
                        Fill = brush,
                        Opacity = .5
                    };

                    Canvas.SetLeft(rect, bounds.Value.Left);
                    Canvas.SetTop(rect, bounds.Value.Top);

                    _rects[control] = rect;
                    _canvas.Children.Add(rect);
                }
            }

            public void Unregister(Control control)
            {
                if (_rects.TryGetValue(control, out var rct))
                {
                    _canvas.Children.Remove(rct);
                    _rects.Remove(control);
                }
            }
            public void Clear()
            {
                _canvas.Children.Clear();
                _rects.Clear();
            }

            public void Restructure()
            {
                foreach (var rct in _rects)
                {
                    var boundN = GetRectInDoc(rct.Key);
                    if (boundN.HasValue)
                    {
                        var bound = boundN.Value;
                        rct.Value.Width = bound.Width;
                        rct.Value.Height = bound.Height;
                        Canvas.SetLeft(rct.Value, bound.Left);
                        Canvas.SetTop(rct.Value, bound.Top);
                    }
                }
            }

            public Rect? GetRectInDoc(Control control)
            {
                if (!LayoutInformation.GetPreviousArrangeBounds(control).HasValue)
                    return null;

                double driftX = 0;
                double driftY = 0;

                StyledElement? c;
                for (c = control.Parent;
                     c is not null
                     && c is Layoutable layoutable
                     && !ReferenceEquals(_document.Control, layoutable);
                     c = c.Parent)
                {
                    driftX += layoutable.Bounds.X;
                    driftY += layoutable.Bounds.Y;
                }

                return new Rect(
                    control.Bounds.X + driftX,
                    control.Bounds.Y + driftY,
                    control.Bounds.Width,
                    control.Bounds.Height);
            }
        }
    }
}
