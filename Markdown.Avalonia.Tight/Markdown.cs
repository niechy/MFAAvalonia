using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ColorDocument.Avalonia;
using ColorDocument.Avalonia.DocumentElements;
using ColorTextBlock.Avalonia;
using ColorTextBlock.Avalonia.Utils;
using Markdown.Avalonia.Controls;
using Markdown.Avalonia.Parsers;
using Markdown.Avalonia.Parsers.Builtin;
using Markdown.Avalonia.Plugins;
using Markdown.Avalonia.Tables;
using Markdown.Avalonia.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace Markdown.Avalonia
{
    public class Markdown : AvaloniaObject, IMarkdownEngine, IMarkdownEngine2
    {
        #region const

        /// <summary>
        /// maximum nested depth of [] and () supported by the transform; implementation detail
        /// </summary>
        private const int _nestDepth = 6;

        /// <summary>
        /// Tabs are automatically converted to spaces as part of the transform  
        /// this constant determines how "wide" those tabs become in spaces  
        /// </summary>
        private const int _tabWidth = 4;

        public const string Heading1Class = ClassNames.Heading1Class;
        public const string Heading2Class = ClassNames.Heading2Class;
        public const string Heading3Class = ClassNames.Heading3Class;
        public const string Heading4Class = ClassNames.Heading4Class;
        public const string Heading5Class = ClassNames.Heading5Class;
        public const string Heading6Class = ClassNames.Heading6Class;

        public const string CodeBlockClass = ClassNames.CodeBlockClass;
        public const string ContainerBlockClass = ClassNames.ContainerBlockClass;
        public const string NoContainerClass = ClassNames.NoContainerClass;
        public const string BlockquoteClass = ClassNames.BlockquoteClass;
        public const string NoteClass = ClassNames.NoteClass;

        public const string ParagraphClass = ClassNames.ParagraphClass;

        public const string TableClass = ClassNames.TableClass;
        public const string TableHeaderClass = ClassNames.TableHeaderClass;
        public const string TableFirstRowClass = ClassNames.TableFirstRowClass;
        public const string TableRowOddClass = ClassNames.TableRowOddClass;
        public const string TableRowEvenClass = ClassNames.TableRowEvenClass;
        public const string TableLastRowClass = ClassNames.TableLastRowClass;
        public const string TableFooterClass = ClassNames.TableFooterClass;

        public const string ListClass = ClassNames.ListClass;
        public const string ListMarkerClass = ClassNames.ListMarkerClass;

        #endregion

        /// <summary>
        /// when true, bold and italic require non-word characters on either side  
        /// WARNING: this is a significant deviation from the markdown spec
        /// </summary>
        public bool StrictBoldItalic { get; set; }

        private string _assetPathRoot;

        /// <inheritdoc/>
        public string AssetPathRoot
        {
            get => _assetPathRoot;
            set
            {
                _assetPathRoot = value;
#pragma warning disable CS0618
                if (BitmapLoader is not null)
                    BitmapLoader.AssetPathRoot = value;
#pragma warning restore CS0618
                if (_setupInfo is not null)
                    _setupInfo.PathResolver.AssetPathRoot = value;
            }
        }

        private string[] _assetAssemblyNames;
        public IEnumerable<string> AssetAssemblyNames => _assetAssemblyNames;

        private ICommand? _hyperlinkCommand;

        /// <inheritdoc/>
        public ICommand? HyperlinkCommand
        {
            get => _hyperlinkCommand ?? _setupInfo?.HyperlinkCommand;
            set
            {
                _hyperlinkCommand = value;
            }
        }

        public MdAvPlugins Plugins { get; set; }

        [Obsolete] private IBitmapLoader? _loader;

        /// <inheritdoc/>
        [Obsolete("Please use Plugins propety. see https://github.com/whistyun/Markdown.Avalonia/wiki/How-to-migrages-to-ver11")]
        public IBitmapLoader? BitmapLoader
        {
            get => _loader;
            set
            {
                _loader = value;
                if (_loader is not null)
                {
                    _loader.AssetPathRoot = _assetPathRoot;
                }
            }
        }

        private IContainerBlockHandler? _containerBlockHandler;

        public IContainerBlockHandler? ContainerBlockHandler
        {
            get => _containerBlockHandler ?? _setupInfo?.ContainerBlock;
            set
            {
                _containerBlockHandler = value;
            }
        }

        public CascadeDictionary CascadeResources { get; } = new CascadeDictionary();

        public IResourceDictionary Resources
        {
            get => CascadeResources.Owner;
            set => CascadeResources.Owner = value;
        }

        public bool UseResource { get; set; }

        #region dependencyobject property

        public static readonly DirectProperty<Markdown, ICommand?> HyperlinkCommandProperty =
            AvaloniaProperty.RegisterDirect<Markdown, ICommand?>(nameof(HyperlinkCommand),
                mdEng => mdEng.HyperlinkCommand,
                (mdEng, command) => mdEng.HyperlinkCommand = command);

        [Obsolete("Please use Plugins propety. see https://github.com/whistyun/Markdown.Avalonia/wiki/How-to-migrages-to-ver11")]
        public static readonly DirectProperty<Markdown, IBitmapLoader?> BitmapLoaderProperty =
            AvaloniaProperty.RegisterDirect<Markdown, IBitmapLoader?>(nameof(BitmapLoader),
                mdEng => mdEng.BitmapLoader,
                (mdEng, loader) => mdEng.BitmapLoader = loader);

        #endregion

        #region ParseInfo

        private SetupInfo _setupInfo;
        private BlockParser2[] _topBlockParsers;
        private BlockParser2[] _blockParsers;
        private InlineParser[] _inlines;
        private bool _supportTextAlignment;
        private bool _supportStrikethrough;
        private bool _supportTextileInline;

        #endregion

        public Markdown()
        {
            _assetPathRoot = Environment.CurrentDirectory;

            var stack = new StackTrace();
            _assetAssemblyNames = stack.GetFrames()
                .Select(frm => frm?.GetMethod()?.DeclaringType?.Assembly?.GetName()?.Name)
                .OfType<string>()
                .Where(name => !name.Equals("Markdown.Avalonia"))
                .Distinct()
                .ToArray();

            Plugins = new MdAvPlugins();

            _setupInfo = null!;
            _topBlockParsers = null!;
            _blockParsers = null!;
            _inlines = null!;
            SetupParser();
        }

        private void SetupParser()
        {
            var info = Plugins.Info;
            if (ReferenceEquals(info, _setupInfo))
                return;

            var topBlocks = new List<BlockParser2>();
            var subBlocks = new List<BlockParser2>();
            var inlines = new List<InlineParser>();


            // top-level block parser
            topBlocks.Add(
                info.EnableListMarkerExt ? new ExtListParser() : new CommonListParser());

            topBlocks.Add(new FencedCodeBlockParser(info.EnablePreRenderingCodeBlock));

            if (info.EnableContainerBlockExt)
            {
                topBlocks.Add(new ContainerBlockParser());
            }


            // sub-level block parser
            subBlocks.Add(new BlockquotesParser(info.EnableTextAlignment));
            subBlocks.Add(new SetextHeaderParser());
            subBlocks.Add(new AtxHeaderParser());

            subBlocks.Add(
                info.EnableRuleExt ? new ExtHorizontalParser() : new CommonHorizontalParser());

            if (info.EnableTableBlock)
            {
                subBlocks.Add(new TableParser());
            }

            if (info.EnableNoteBlock)
            {
                subBlocks.Add(new NoteParser());
            }

            subBlocks.Add(new IndentCodeBlockParser());


            // inline parser
            inlines.Add(InlineParser.New(_codeSpan, nameof(CodeSpanEvaluator), CodeSpanEvaluator));
            inlines.Add(InlineParser.New(_imageOrHrefInline, nameof(ImageOrHrefInlineEvaluator), ImageOrHrefInlineEvaluator));

            if (StrictBoldItalic)
            {
                inlines.Add(InlineParser.New(_strictBold, nameof(BoldEvaluator), BoldEvaluator));
                inlines.Add(InlineParser.New(_strictItalic, nameof(ItalicEvaluator), ItalicEvaluator));

                if (info.EnableStrikethrough)
                    inlines.Add(InlineParser.New(_strikethrough, nameof(StrikethroughEvaluator), StrikethroughEvaluator));
            }

            // parser registered by plugin

            topBlocks.AddRange(info.TopBlock.Select(bp => bp.Upgrade()));
            subBlocks.AddRange(info.Block.Select(bp => bp.Upgrade()));
            inlines.AddRange(info.Inline);


            // inform path info to resolver
            info.PathResolver.AssetPathRoot = AssetPathRoot;
            info.PathResolver.CallerAssemblyNames = AssetAssemblyNames;

            info.Overwrite(_hyperlinkCommand);
            info.Overwrite(_containerBlockHandler);
            info.Overwrite(_loader);


            _topBlockParsers = topBlocks.Select(p => info.Override(p).Upgrade()).ToArray();
            _blockParsers = subBlocks.Select(p => info.Override(p).Upgrade()).ToArray();
            _inlines = inlines.ToArray();
            _supportTextAlignment = info.EnableTextAlignment;
            _supportStrikethrough = info.EnableStrikethrough;
            _supportTextileInline = info.EnableTextileInline;
            _setupInfo = info;
        }

        private string? PreprocessText(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            // 正则匹配HTML注释，捕获注释内容（不含前后标签）
            var commentPattern = new Regex(@"<!--([\s\S]*?)-->", RegexOptions.Compiled);

            // 对每个匹配项进行处理
            return commentPattern.Replace(input, match =>
            {
                // 提取注释中间的内容（捕获组1）
                string commentContent = match.Groups[1].Value;

                // 检查内容中是否包含中文字符（仅汉字，范围：\u4e00-\u9fa5）
                bool hasChineseChars = Regex.IsMatch(commentContent, @"[\u4e00-\u9fa5]");

                // 若包含汉字，则保留中间内容；否则完全移除
                return hasChineseChars ? commentContent : string.Empty;
            });
        }

        public Control Transform(string? text)
        {
            return TransformElement(text).Control;
        }

        public DocumentElement TransformElement(string? text)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            text = PreprocessText(text);
            SetupParser();

            text = TextUtil.Normalize(text, _tabWidth);

            var status = new ParseStatus(true & _supportTextAlignment);
            var elements = ParseGamutElement(text, status);
            return new DocumentRootElement(elements);
        }

        public IEnumerable<DocumentElement> ParseGamutElement(string? text, ParseStatus status)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            SetupParser();
            return PrivateRunBlockGamut(text, status);
        }

        public IEnumerable<CInline> ParseGamutInline(string? text)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            SetupParser();
            return PrivateRunSpanGamut(text);
        }

        public IEnumerable<Control> RunBlockGamut(string? text, ParseStatus status)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            SetupParser();

            text = TextUtil.Normalize(text, _tabWidth);

            var elements = PrivateRunBlockGamut(text, status);
            return elements.Select(e => e.Control);
        }

        public IEnumerable<CInline> RunSpanGamut(string? text)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            SetupParser();

            text = TextUtil.Normalize(text, _tabWidth);

            return PrivateRunSpanGamut(text);
        }

        private IEnumerable<DocumentElement> PrivateRunBlockGamut(string text, ParseStatus status)
        {
            var index = 0;
            var length = text.Length;
            var rtn = new List<DocumentElement>();

            var candidates = new List<Candidate<BlockParser2>>();

            for (;;)
            {
                candidates.Clear();

                foreach (var parser in _topBlockParsers)
                {
                    var match = parser.Pattern.Match(text, index, length);
                    if (match.Success) candidates.Add(new Candidate<BlockParser2>(match, parser));
                }

                if (candidates.Count == 0) break;

                candidates.Sort();

                int bestBegin = 0;
                int bestEnd = 0;
                IEnumerable<DocumentElement>? result = null;

                foreach (var c in candidates)
                {
                    result = c.Parser.Convert2(text, c.Match, status, this, out bestBegin, out bestEnd);
                    if (result is not null) break;
                }

                if (result is null) break;

                if (bestBegin > index)
                {
                    RunBlockRest(text, index, bestBegin - index, status, 0, rtn);
                }

                rtn.AddRange(result);

                length -= bestEnd - index;
                index = bestEnd;
            }

            if (index < text.Length)
            {
                RunBlockRest(text, index, text.Length - index, status, 0, rtn);
            }

            return rtn;


            void RunBlockRest(
                string text,
                int index,
                int length,
                ParseStatus status,
                int parserStart,
                List<DocumentElement> outto)
            {
                for (; parserStart < _blockParsers.Length; ++parserStart)
                {
                    var parser = _blockParsers[parserStart];

                    for (;;)
                    {
                        var match = parser.Pattern.Match(text, index, length);
                        if (!match.Success) break;

                        var rslt = parser.Convert2(text, match, status, this, out int parseBegin, out int parserEnd);
                        if (rslt is null) break;

                        if (parseBegin > index)
                        {
                            RunBlockRest(text, index, parseBegin - index, status, parserStart + 1, outto);
                        }
                        outto.AddRange(rslt);

                        length -= parserEnd - index;
                        index = parserEnd;
                    }

                    if (length == 0) break;
                }

                if (length != 0)
                {
                    outto.AddRange(FormParagraphs(text.Substring(index, length), status));
                }
            }
        }


        private IEnumerable<CInline> PrivateRunSpanGamut(string text)
        {
            // Debug：初始解析入口
            var rtn = new List<CInline>();

            // 步骤1：先递归处理所有成对符号（分阶段核心：成对匹配优先）
            var pairParsedResult = ParseAllNestedPairs(text, 1);
            rtn.AddRange(pairParsedResult);

            return rtn;
        }

        #region 核心：通用成对符号解析（可扩展、支持嵌套）

        /// <summary>
        /// 可扩展的成对符号配置（支持任意成对符号，按优先级排序：长符号优先，避免短符号截断长符号）
        /// 格式：(开始符号, 结束符号, 生成对应Inline元素的工厂方法)
        /// </summary>
// 显式指定 ImmutableList 的泛型参数为完整元组类型，解决类型推断失败问题
        private readonly ImmutableList<(
            string Start,
            string End,
            Func<IEnumerable<CInline>, CInline> ElementFactory
            )> _pairSymbols = ImmutableList.Create<(
            string Start,
            string End,
            Func<IEnumerable<CInline>, CInline> ElementFactory
            )>(
            // 优先级：长符号 > 短符号（避免 "**" 被 "*" 截断）
            ("**", "**", inlines => new CBold(inlines)), // 加粗
            ("__", "__", inlines => new CUnderline(inlines)), // 下划线
            ("~~", "~~", inlines => new CStrikethrough(inlines)), // 删除线
            ("*", "*", inlines => new CItalic(inlines)), // 斜体
            ("_", "_", inlines => new CItalic(inlines)) // 斜体（下划线版）
            // 可扩展添加其他成对符号，例如：
            // ("===", "===", inlines => new CHighlight(inlines)), // 高亮
            // ("::", "::", inlines => new CCustom(inlines))       // 自定义符号
        );
        private bool IsRangeOverlapped(int targetStart, int targetEnd, List<(int Start, int End)> usedRanges)
        {
            foreach (var (usedStart, usedEnd) in usedRanges)
            {
                // 目标范围与已占用范围有重叠 → 返回true
                if (targetStart < usedEnd && targetEnd > usedStart)
                    return true;
            }
            return false;
        }
        /// <summary>
        /// 递归解析所有嵌套的成对符号（分阶段第一步：先处理完所有成对符号）
        /// </summary>
        /// <param name="text">当前要解析的文本</param>
        /// <param name="level">解析层级（用于Debug缩进）</param>
        /// <returns>成对符号解析后的Inline元素列表</returns>
        private List<CInline> ParseAllNestedPairs(string text, int level)
        {
            var result = new List<CInline>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }
            // 步骤1：找到当前文本中最内层的成对符号（避免外层符号截断内层）
            var (found, startSym, endSym, elementFactory, startIdx, endIdx) = FindInnermostPair(text, level);

            if (!found)
            {
                var parserResult = ProcessWithOriginalParsers(text, level);
                result.AddRange(parserResult);
                return result;
            }

            // 步骤3：有成对符号 → 拆分前/中/后三段处理
            string preText = text.Substring(0, startIdx); // 成对符号之前的文本
            string middleText = text.Substring(
                startIdx + startSym.Length,
                endIdx - (startIdx + startSym.Length) // 成对符号之间的文本（要递归解析）
            );
            string postText = text.Substring(endIdx + endSym.Length); // 成对符号之后的文本

            // ① 处理“成对符号之前”的文本（递归解析，确保前面的成对符号也被处理）
            if (!string.IsNullOrEmpty(preText))
            {
                var preResult = ParseAllNestedPairs(preText, level + 1);
                result.AddRange(preResult);
            }
            var middleResult = ParseAllNestedPairs(middleText, level + 1);

            // ③ 生成当前成对符号对应的Inline元素（如CBold、CItalic）
            var pairElement = elementFactory(middleResult);
            result.Add(pairElement);

            // ④ 处理“成对符号之后”的文本（递归解析，确保后面的成对符号也被处理）
            if (!string.IsNullOrEmpty(postText))
            {
                var postResult = ParseAllNestedPairs(postText, level + 1);
                result.AddRange(postResult);
            }
            return result;
        }

        /// <summary>
        /// 查找文本中最内层的成对符号（核心：避免外层符号截断内层）
        /// </summary>
        /// <returns>是否找到、开始符号、结束符号、元素工厂、开始索引、结束索引</returns>
        private (bool Found, string StartSym, string EndSym, Func<IEnumerable<CInline>, CInline> ElementFactory, int StartIdx, int EndIdx) FindInnermostPair(string text, int level)
        {
            // 拆分：长符号组（长度≥2）、短符号组（长度=1），长符号优先处理
            var longPairs = _pairSymbols.Where(p => p.Start.Length >= 2).ToList();
            var shortPairs = _pairSymbols.Where(p => p.Start.Length == 1).ToList();

            var allValidPairs = new List<(string Start, string End, Func<IEnumerable<CInline>, CInline> Factory, int StartIdx, int EndIdx, int NestLevel)>();

            // 步骤1：先收集长符号的“完整有效匹配”（优先处理，不被短符号干扰）
            foreach (var (start, end, factory) in longPairs)
            {
                int currentStartIdx = 0;
                while (true)
                {
                    // 查找长符号起始标记（非转义）
                    currentStartIdx = FindNonEscaped(text, start, currentStartIdx);
                    if (currentStartIdx == -1) break;

                    // 关键：只找长符号对应的结束标记（不找短符号）
                    int currentEndIdx = FindNonEscaped(text, end, currentStartIdx + start.Length);
                    if (currentEndIdx == -1)
                    {
                        // 找不到长符号结束标记 → 跳过当前起始标记，继续找下一个
                        currentStartIdx += start.Length;
                        continue;
                    }

                    // 计算嵌套层级
                    int nestLevel = CalculateNestLevel(text, currentStartIdx + start.Length, currentEndIdx, level);
                    // 添加长符号有效匹配
                    allValidPairs.Add((start, end, factory, currentStartIdx, currentEndIdx, nestLevel));

                    // 跳过当前长符号的范围，避免重复匹配
                    currentStartIdx = currentEndIdx + end.Length;
                }
            }

            // 步骤2：收集短符号的有效匹配（跳过长符号已经占用的位置）
            var longUsedRanges = allValidPairs.Select(p => (Start: p.StartIdx, End: p.EndIdx + p.End.Length)).ToList();
            foreach (var (start, end, factory) in shortPairs)
            {
                int currentStartIdx = 0;
                while (true)
                {
                    // 查找短符号起始标记（非转义）
                    currentStartIdx = FindNonEscaped(text, start, currentStartIdx);
                    if (currentStartIdx == -1) break;

                    // 检查当前位置是否被长符号占用 → 占用则跳过
                    if (IsRangeOverlapped(currentStartIdx, currentStartIdx + start.Length, longUsedRanges))
                    {
                        currentStartIdx += start.Length;
                        continue;
                    }

                    // 查找短符号结束标记（非转义，且不被长符号占用）
                    int currentEndIdx = FindNonEscaped(text, end, currentStartIdx + start.Length);
                    if (currentEndIdx == -1) break;

                    // 检查结束位置是否被长符号占用 → 占用则跳过
                    if (IsRangeOverlapped(currentEndIdx, currentEndIdx + end.Length, longUsedRanges))
                    {
                        currentStartIdx = currentEndIdx + end.Length;
                        continue;
                    }

                    // 计算嵌套层级
                    int nestLevel = CalculateNestLevel(text, currentStartIdx + start.Length, currentEndIdx, level);
                    // 添加短符号有效匹配
                    allValidPairs.Add((start, end, factory, currentStartIdx, currentEndIdx, nestLevel));

                    // 跳过当前短符号的范围
                    currentStartIdx = currentEndIdx + end.Length;
                }
            }

            if (allValidPairs.Count == 0)
                return (false, "", "", null, -1, -1);

            // 保持原排序规则：最内层→结束位置靠前→符号长度长
            var bestPair = allValidPairs
                .OrderByDescending(p => p.NestLevel) // 最内层优先
                .ThenBy(p => p.EndIdx) // 结束位置靠前优先
                .ThenByDescending(p => p.Start.Length) // 长符号优先（兜底，避免短符号截断）
                .First();

            return (true, bestPair.Start, bestPair.End, bestPair.Factory, bestPair.StartIdx, bestPair.EndIdx);
        }

        /// <summary>
        /// 查找非转义的目标字符串（跳过 \ 开头的符号）
        /// </summary>
        private int FindNonEscaped(string text, string target, int startFrom)
        {
            int index = text.IndexOf(target, startFrom);
            while (index != -1)
            {
                // 检查是否被转义（前面不是 \，或前面是 \\ 双重转义）
                bool isEscaped = index > 0 && text[index - 1] == '\\';
                if (isEscaped)
                {
                    index = text.IndexOf(target, index + target.Length);
                    continue;
                }

                // 优化：如果目标是长符号（≥2字符），检查是否被短符号“截断”（比如 ** 中间夹了 *）
                if (target.Length >= 2)
                {
                    bool isTruncated = false;
                    // 检查长符号内部是否包含对应的短符号（比如 ** 内部有 * → 不视为完整长符号）
                    string innerText = text.Substring(index, target.Length);
                    var shortSymbol = target.Substring(0, 1); // 长符号对应的短符号（如 **→*）
                    if (innerText.Count(c => c.ToString() == shortSymbol) != target.Length)
                    {
                        isTruncated = true;
                    }

                    if (isTruncated)
                    {
                        index = text.IndexOf(target, index + target.Length);
                        continue;
                    }
                }

                return index;
            }
            return -1;
        }

        /// <summary>
        /// 计算文本片段的嵌套层级（用于判断最内层成对符号）
        /// </summary>
        private int CalculateNestLevel(string text, int startIdx, int endIdx, int level)
        {
            int nestLevel = 0;
            int currentIdx = startIdx;

            while (currentIdx < endIdx)
            {
                // 遍历所有成对符号，统计嵌套次数
                foreach (var (start, end, _) in _pairSymbols)
                {
                    if (currentIdx + start.Length <= endIdx && text.Substring(currentIdx, start.Length) == start)
                    {
                        nestLevel++;
                        currentIdx += start.Length;
                        break;
                    }
                    if (currentIdx + end.Length <= endIdx && text.Substring(currentIdx, end.Length) == end)
                    {
                        nestLevel--;
                        currentIdx += end.Length;
                        break;
                    }
                }
                currentIdx++;
            }

            return nestLevel;
        }

        #endregion

        #region 原Parser处理逻辑（分阶段第二步：处理非成对符号文本）

        /// <summary>
        /// 对非成对符号的文本，执行原有的InlineParser Pattern判断（保持原逻辑不变）
        /// </summary>
        private List<CInline> ProcessWithOriginalParsers(string text, int level)
        {
            var rtn = new List<CInline>();

            void OriginalRunSpanRest(
                string txt,
                int index,
                int length,
                int parserStart)
            {
                for (; parserStart < _inlines.Length; ++parserStart)
                {
                    var parser = _inlines[parserStart];

                    for (;;)
                    {
                        var match = parser.Pattern.Match(txt, index, length);
                        if (!match.Success)
                        {
                            break;
                        }

                        var rslt = parser.Convert(txt, match, this, out int parseBegin, out int parserEnd);
                        if (rslt is null)
                        {
                            break;
                        }

                        if (parseBegin > index)
                        {
                            OriginalRunSpanRest(txt, index, parseBegin - index, parserStart + 1);
                        }

                        rtn.AddRange(rslt);

                        length -= parserEnd - index;
                        index = parserEnd;

                        if (length == 0)
                        {
                            break;
                        }
                    }

                    if (length == 0) break;
                }

                if (length != 0)
                {
                    var subtext = text.Substring(index, length);
                    rtn.AddRange(StrictBoldItalic ? DoText(subtext) : DoTextDecorations(subtext, s => DoText(s)));
                }
            }

            OriginalRunSpanRest(text, 0, text.Length, 0);
            return rtn;
        }

        #endregion

        #region grammer - paragraph

        private static readonly Regex _align = new(@"^p([<=>])\.", RegexOptions.Compiled);
        private static readonly Regex _newlinesLeadingTrailing = new(@"^\n+|\n+\z", RegexOptions.Compiled);
        private static readonly Regex _newlinesMultiple = new(@"\n{2,}", RegexOptions.Compiled);

        /// <summary>
        /// splits on two or more newlines, to form "paragraphs";    
        /// </summary>
        private IEnumerable<DocumentElement> FormParagraphs(string text, ParseStatus status)
        {
            var trimemdText = _newlinesLeadingTrailing.Replace(text, "");

            string[] grafs = trimemdText == "" ? new string[0] : _newlinesMultiple.Split(trimemdText);

            foreach (var g in grafs)
            {
                var chip = g;

                TextAlignment? indiAlignment = null;

                if (status.SupportTextAlignment)
                {
                    var alignMatch = _align.Match(chip);
                    if (alignMatch.Success)
                    {
                        chip = chip.Substring(alignMatch.Length);
                        switch (alignMatch.Groups[1].Value)
                        {
                            case "<":
                                indiAlignment = TextAlignment.Left;
                                break;
                            case ">":
                                indiAlignment = TextAlignment.Right;
                                break;
                            case "=":
                                indiAlignment = TextAlignment.Center;
                                break;
                        }
                    }
                }

                var inlines = PrivateRunSpanGamut(chip);
                var ctbox = indiAlignment.HasValue ? new CTextBlockElement(inlines, ParagraphClass, indiAlignment.Value) : new CTextBlockElement(inlines, ParagraphClass);

                yield return ctbox;
            }
        }

        #endregion

        #region grammer - image or href

        private static readonly Regex _imageOrHrefInline = new(string.Format(@"
                (                           # wrap whole match in $1
                    (!)?                    # image maker = $2
                    \[
                        ({0})               # link text = $3
                    \]
                    \(                      # literal paren
                        [ ]*
                        ({1})               # href = $4
                        [ ]*
                        (                   # $5
                        (['""])             # quote char = $6
                        (.*?)               # title = $7
                        \6                  # matching quote
                        [ ]*                # ignore any spaces between closing quote and )
                        )?                  # title is optional
                    \)
                )", GetNestedBracketsPattern(), GetNestedParensPattern()),
            RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);


        private CInline ImageOrHrefInlineEvaluator(Match match)
        {
            if (String.IsNullOrEmpty(match.Groups[2].Value))
            {
                return TreatsAsHref(match);
            }
            else
            {
                return TreatsAsImage(match);
            }
        }


        private CInline TreatsAsHref(Match match)
        {
            string linkText = match.Groups[3].Value;
            string url = match.Groups[4].Value;
            string title = match.Groups[7].Value;

            var link = new CHyperlink(PrivateRunSpanGamut(linkText))
            {
                Command = (urlTxt) =>
                {
                    if (HyperlinkCommand != null && HyperlinkCommand.CanExecute(urlTxt))
                    {
                        HyperlinkCommand.Execute(urlTxt);
                    }
                },

                CommandParameter = url
            };

            if (!String.IsNullOrEmpty(title)
                && !title.Any(ch => !Char.IsLetterOrDigit(ch)))
            {
                link.Classes.Add(title);
            }

            return link;
        }

        private CInline TreatsAsImage(Match match)
        {
            string altText = match.Groups[3].Value;
            string urlTxt = match.Groups[4].Value;
            string title = match.Groups[7].Value;

            return LoadImage(urlTxt, title);
        }

        private CInline LoadImage(string urlTxt, string title)
        {
            if (UseResource && CascadeResources.TryGet(urlTxt, out var resourceVal))
            {
                if (resourceVal is Control control)
                {
                    return new CInlineUIContainer(control);
                }

                CImage? cimg = null;
                if (resourceVal is Bitmap renderedImage)
                {
                    cimg = new CImage(renderedImage);
                }
                if (resourceVal is IEnumerable<Byte> byteEnum)
                {
                    try
                    {
                        using (var memstream = new MemoryStream(byteEnum.ToArray()))
                        {
                            var bitmap = new Bitmap(memstream);
                            cimg = new CImage(bitmap);
                        }
                    }
                    catch { }
                }

                if (cimg is not null)
                {
                    cimg.ClickCommand = new ImageOpenCommand();
                    // 命令参数为图片的原始路径/URL（urlTxt）
                    cimg.ClickCommandParameter = urlTxt;

                    if (!String.IsNullOrEmpty(title)
                        && title.All(Char.IsLetterOrDigit))
                    {
                        cimg.Classes.Add(title);
                    }
                    return cimg;
                }
            }

            CImage image = _setupInfo.LoadImage(urlTxt);
            image.ClickCommand = new ImageOpenCommand();
            image.ClickCommandParameter = urlTxt; // 传递图片路径/URL

            if (!String.IsNullOrEmpty(title)
                && title.All(char.IsLetterOrDigit))
            {
                image.Classes.Add(title);
            }

            return image;
        }

        #endregion

        #region grammer - code

        //    * You can use multiple backticks as the delimiters if you want to
        //        include literal backticks in the code span. So, this input:
        //
        //        Just type ``foo `bar` baz`` at the prompt.
        //
        //        Will translate to:
        //
        //          <p>Just type <code>foo `bar` baz</code> at the prompt.</p>
        //
        //        There's no arbitrary limit to the number of backticks you
        //        can use as delimters. If you need three consecutive backticks
        //        in your code, use four for delimiters, etc.
        //
        //    * You can use spaces to get literal backticks at the edges:
        //
        //          ... type `` `bar` `` ...
        //
        //        Turns to:
        //
        //          ... type <code>`bar`</code> ...         
        //
        private static readonly Regex _codeSpan = new(@"
                    (?<!\\)   # Character before opening ` can't be a backslash
                    (`+)      # $1 = Opening run of `
                    (.+?)     # $2 = The code block
                    (?<!`)
                    \1
                    (?!`)", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);

        private CCode CodeSpanEvaluator(Match match)
        {
            string span = match.Groups[2].Value;
            span = Regex.Replace(span, @"^[ ]*", ""); // leading whitespace
            span = Regex.Replace(span, @"[ ]*$", ""); // trailing whitespace

            var result = new CCode(new[]
            {
                new CRun()
                {
                    Text = span
                }
            });

            return result;
        }

        #endregion

        #region grammer - textdecorations

        private static readonly Regex _strictBold = new(@"([\W_]|^) (\*\*|__) (?=\S) ([^\r]*?\S[\*_]*) \2 ([\W_]|$)",
            RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _strictItalic = new(@"([\W_]|^) (\*|_) (?=\S) ([^\r\*_]*?\S) \2 ([\W_]|$)",
            RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _strikethrough = new(@"(~~) (?=\S) (.+?) (?<=\S) \1",
            RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _underline = new(@"(__) (?=\S) (.+?) (?<=\S) \1",
            RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Turn Markdown *italics* and **bold** into HTML strong and em tags
        /// </summary>
        private IEnumerable<CInline> DoTextDecorations(string text, Func<string, IEnumerable<CInline>> defaultHandler)
        {
            Console.WriteLine(text);
            var rtn = new List<CInline>();

            var buff = new StringBuilder();

            void HandleBefore()
            {
                if (buff.Length > 0)
                {
                    rtn.AddRange(defaultHandler(buff.ToString()));
                    buff.Clear();
                }
            }

            for (var i = 0; i < text.Length; ++i)
            {
                var ch = text[i];
                switch (ch)
                {
                    default:
                        buff.Append(ch);
                        break;

                    case '\\': // escape
                        if (++i < text.Length)
                        {
                            switch (text[i])
                            {
                                default:
                                    buff.Append('\\').Append(text[i]);
                                    break;

                                case '\\': // escape
                                case ':': // bold? or italic
                                case '*': // bold? or italic
                                case '~': // strikethrough?
                                case '_': // underline?
                                case '%': // color?
                                    buff.Append(text[i]);
                                    break;
                            }
                        }
                        else
                            buff.Append('\\');

                        break;

                    case ':': // emoji?
                    {
                        var nxtI = text.IndexOf(':', i + 1);
                        if (nxtI != -1 && EmojiTable.TryGet(text.Substring(i + 1, nxtI - i - 1), out var emoji))
                        {
                            buff.Append(emoji);
                            i = nxtI;
                        }
                        else buff.Append(':');
                        break;
                    }

                    case '*': // bold? or italic
                    {
                        var oldI = i;
                        var inline = ParseAsBoldOrItalic(text, ref i);
                        if (inline == null)
                        {
                            buff.Append(text, oldI, i - oldI + 1);
                        }
                        else
                        {
                            HandleBefore();
                            rtn.Add(inline);
                        }
                        break;
                    }

                    case '~': // strikethrough?
                    {
                        var oldI = i;
                        var inline = ParseAsStrikethrough(text, ref i);
                        if (inline == null)
                        {
                            buff.Append(text, oldI, i - oldI + 1);
                        }
                        else
                        {
                            HandleBefore();
                            rtn.Add(inline);
                        }
                        break;
                    }

                    case '_': // underline?
                    {
                        var oldI = i;
                        var inline = ParseAsUnderline(text, ref i);
                        if (inline == null)
                        {
                            buff.Append(text, oldI, i - oldI + 1);
                        }
                        else
                        {
                            HandleBefore();
                            rtn.Add(inline);
                        }
                        break;
                    }

                    case '%': // color?
                    {
                        var oldI = i;
                        var inline = ParseAsColor(text, ref i);
                        if (inline == null)
                        {
                            buff.Append(text, oldI, i - oldI + 1);
                        }
                        else
                        {
                            HandleBefore();
                            rtn.Add(inline);
                        }
                        break;
                    }
                }
            }

            if (buff.Length > 0)
            {
                rtn.AddRange(defaultHandler(buff.ToString()));
            }

            return rtn;
        }

        private CUnderline? ParseAsUnderline(string text, ref int start)
        {
            var bgnCnt = CountRepeat(text, start, '_');

            int last = EscapedIndexOf(text, start + bgnCnt, '_');

            int endCnt = last >= 0 ? CountRepeat(text, last, '_') : -1;

            if (endCnt >= 2 && bgnCnt >= 2)
            {
                int cnt = 2;
                int bgn = start + cnt;
                int end = last;

                // 核心修改：递归解析 __ 内部的内容
                var innerText = text.Substring(bgn, end - bgn);
                var innerInlines = PrivateRunSpanGamut(innerText);

                start = end + cnt - 1;
                return new CUnderline(innerInlines); // 用递归结果创建下划线
            }
            else
            {
                start += bgnCnt - 1;
                return null;
            }
        }

        private CStrikethrough? ParseAsStrikethrough(string text, ref int start)
        {
            var bgnCnt = CountRepeat(text, start, '~');

            int last = EscapedIndexOf(text, start + bgnCnt, '~');

            int endCnt = last >= 0 ? CountRepeat(text, last, '~') : -1;

            if (endCnt >= 2 && bgnCnt >= 2)
            {
                int cnt = 2;
                int bgn = start + cnt;
                int end = last;

                // 核心修改：递归解析 ~~ 内部的内容
                var innerText = text.Substring(bgn, end - bgn);
                var innerInlines = PrivateRunSpanGamut(innerText);

                start = end + cnt - 1;
                return new CStrikethrough(innerInlines); // 用递归结果创建删除线
            }
            else
            {
                start += bgnCnt - 1;
                return null;
            }
        }

        private CInline? ParseAsBoldOrItalic(string text, ref int start)
        {
            // count asterisk (bgn)
            var bgnCnt = CountRepeat(text, start, '*');

            int last = EscapedIndexOf(text, start + bgnCnt, '*');

            int endCnt = last >= 0 ? CountRepeat(text, last, '*') : -1;

            if (endCnt >= 1)
            {
                int cnt = Math.Min(bgnCnt, endCnt);
                int bgn = start + cnt;
                int end = last;

                // 核心修改：递归解析 **/* 内部的内容（比如链接）
                var innerText = text.Substring(bgn, end - bgn);
                var innerInlines = PrivateRunSpanGamut(innerText); // 递归！解析内部的链接/其他格式

                switch (cnt)
                {
                    case 1: //  italic
                        start = end + cnt - 1;
                        return new CItalic(innerInlines); // 用递归解析后的内容创建斜体
                    case 2: // bold
                        start = end + cnt - 1;
                        return new CBold(innerInlines); // 用递归解析后的内容创建加粗
                    default: // >3; bold-italic
                        bgn = start + 3;
                        start = end + 3 - 1;
                        var inline = new CItalic(innerInlines);
                        return new CBold(new[]
                        {
                            inline
                        });
                }
            }
            else
            {
                start += bgnCnt - 1;
                return null;
            }
        }

        private CInline? ParseAsColor(string text, ref int start)
        {
            if (start + 1 >= text.Length)
                return null;

            if (text[start + 1] != '{')
                return null;

            int end = text.IndexOf('}', start + 1);

            if (end == -1)
                return null;

            var styleTxts = text.Substring(start + 2, end - (start + 2));

            int bgnIdx = end + 1;
            int endIdx = EscapedIndexOf(text, bgnIdx, '%');

            CSpan span;
            if (endIdx == -1)
            {
                endIdx = text.Length - 1;
                span = new CSpan(PrivateRunSpanGamut(text.Substring(bgnIdx)));
            }
            else
            {
                span = new CSpan(PrivateRunSpanGamut(text.Substring(bgnIdx, endIdx - bgnIdx)));
            }

            foreach (var styleTxt in styleTxts.Split(';'))
            {
                var nameAndVal = styleTxt.Split(':');

                if (nameAndVal.Length != 2)
                    return null;

                var name = nameAndVal[0].Trim();
                var colorLbl = nameAndVal[1].Trim();

                switch (name)
                {
                    case "color":
                        try
                        {
                            var color = colorLbl.StartsWith("#") ? (IBrush?)new BrushConverter().ConvertFrom(colorLbl) : (IBrush?)new BrushConverter().ConvertFromString(colorLbl);

                            span.Foreground = color;
                        }
                        catch { }
                        break;

                    case "background":
                        try
                        {
                            var color = colorLbl.StartsWith("#") ? (IBrush?)new BrushConverter().ConvertFrom(colorLbl) : (IBrush?)new BrushConverter().ConvertFromString(colorLbl);

                            span.Background = color;
                        }
                        catch { }
                        break;

                    default:
                        return null;
                }
            }

            start = endIdx;
            return span;
        }


        private int EscapedIndexOf(string text, int start, char target)
        {
            for (var i = start; i < text.Length; ++i)
            {
                var ch = text[i];
                if (ch == '\\') ++i;
                else if (ch == target) return i;
            }
            return -1;
        }
        private int CountRepeat(string text, int start, char target)
        {
            var count = 0;

            for (var i = start; i < text.Length; ++i)
            {
                if (text[i] == target) ++count;
                else break;
            }

            return count;
        }

        private CItalic ItalicEvaluator(Match match)
        {
            var content = match.Groups[3].Value;

            return new CItalic(PrivateRunSpanGamut(content));
        }

        private CBold BoldEvaluator(Match match)
        {
            var content = match.Groups[3].Value;

            return new CBold(PrivateRunSpanGamut(content));
        }

        private CStrikethrough StrikethroughEvaluator(Match match)
        {
            var content = match.Groups[2].Value;

            return new CStrikethrough(PrivateRunSpanGamut(content));
        }

        private CUnderline UnderlineEvaluator(Match match)
        {
            var content = match.Groups[2].Value;

            return new CUnderline(PrivateRunSpanGamut(content));
        }

        #endregion

        #region grammer - text

        private static readonly Regex _eoln = new("\\s+");
        private static readonly Regex _lbrk = new(@"\ {2,}\n");

        private IEnumerable<CRun> DoText(string text)
        {
            var lines = _lbrk.Split(text);
            bool first = true;
            foreach (var line in lines)
            {
                if (first)
                    first = false;
                else
                    yield return new CLineBreak();
                var t = _eoln.Replace(line, " ");
                yield return new CRun()
                {
                    Text = t
                };
            }
        }

        #endregion

        #region helper - make regex

        /// <summary>
        /// Reusable pattern to match balanced [brackets]. See Friedl's 
        /// "Mastering Regular Expressions", 2nd Ed., pp. 328-331.
        /// </summary>
        private static string GetNestedBracketsPattern()
        {
            // in other words [this] and [this[also]] and [this[also[too]]]
            // up to _nestDepth
            return RepeatString(@"
                   (?>              # Atomic matching
                      [^\[\]]+      # Anything other than brackets
                    |
                      \[
                          ", _nestDepth)
                + RepeatString(
                    @" \]
                   )*"
                    , _nestDepth);
        }

        /// <summary>
        /// Reusable pattern to match balanced (parens). See Friedl's 
        /// "Mastering Regular Expressions", 2nd Ed., pp. 328-331.
        /// </summary>
        private static string GetNestedParensPattern()
        {
            // in other words (this) and (this(also)) and (this(also(too)))
            // up to _nestDepth
            return RepeatString(@"
                   (?>              # Atomic matching
                      [^()\n\t]+? # Anything other than parens or whitespace
                    |
                      \(
                          ", _nestDepth)
                + RepeatString(
                    @" \)
                   )*?"
                    , _nestDepth);
        }

        /// <summary>
        /// this is to emulate what's evailable in PHP
        /// </summary>
        private static string RepeatString(string text, int count)
        {
            var sb = new StringBuilder(text.Length * count);
            for (int i = 0; i < count; i++)
                sb.Append(text);
            return sb.ToString();
        }

        #endregion


        #region helper - parse

        private TResult Create<TResult, TContent>(IEnumerable<TContent> content)
            where TResult : Panel, new()
            where TContent : Control
        {
            var result = new TResult();
            foreach (var c in content)
            {
                result.Children.Add(c);
            }

            return result;
        }


        //private IEnumerable<T> Evaluates<T>(
        //        string text, ParseStatus status,
        //        BlockParser<T>[] primary,
        //        BlockParser<T>[] secondly,
        //        Func<string, ParseStatus, IEnumerable<T>> rest
        //    )
        //{
        //    var index = 0;
        //    var length = text.Length;
        //    var rtn = new List<T>();
        //
        //    while (true)
        //    {
        //        int bestIndex = Int32.MaxValue;
        //        Match? bestMatch = null;
        //        BlockParser<T>? bestParser = null;
        //
        //        foreach (var parser in primary)
        //        {
        //            var match = parser.Pattern.Match(text, index, length);
        //            if (match.Success && match.Index < bestIndex)
        //            {
        //                bestIndex = match.Index;
        //                bestMatch = match;
        //                bestParser = parser;
        //            }
        //        }
        //
        //        if (bestParser is null || bestMatch is null) break;
        //
        //        var result = bestParser.Convert(text, bestMatch, status, this, out bestIndex, out int newIndex);
        //
        //        if (bestIndex > index)
        //        {
        //            EvaluateRest(rtn, text, index, bestIndex - index, status, secondly, 0, rest);
        //        }
        //
        //        rtn.AddRange(result);
        //
        //        length -= newIndex - index;
        //        index = newIndex;
        //    }
        //
        //    if (index < text.Length)
        //    {
        //        EvaluateRest(rtn, text, index, text.Length - index, status, secondly, 0, rest);
        //    }
        //
        //    return rtn;
        //
        //}
        //
        //private void EvaluateRest<T>(
        //    List<T> resultIn,
        //    string text, int index, int length,
        //    ParseStatus status,
        //    BlockParser<T>[] parsers, int parserStart,
        //    Func<string, ParseStatus, IEnumerable<T>> rest)
        //{
        //    for (; parserStart < parsers.Length; ++parserStart)
        //    {
        //        var parser = parsers[parserStart];
        //
        //        for (; ; )
        //        {
        //            var match = parser.Pattern.Match(text, index, length);
        //            if (!match.Success) break;
        //
        //            var result = parser.Convert(text, match, status, this, out var matchStartIndex, out int newIndex);
        //
        //            if (matchStartIndex > index)
        //            {
        //                EvaluateRest(resultIn, text, index, match.Index - index, status, parsers, parserStart + 1, rest);
        //            }
        //
        //            resultIn.AddRange(result);
        //
        //            length -= newIndex - index;
        //            index = newIndex;
        //        }
        //
        //        if (length == 0) break;
        //    }
        //
        //    if (length != 0)
        //    {
        //        var suffix = text.Substring(index, length);
        //        resultIn.AddRange(rest(suffix, status));
        //    }
        //}

        #endregion
    }

    internal struct Candidate<T> : IComparable<Candidate<T>>
    {
        public Match Match { get; }
        public T Parser { get; }

        public Candidate(Match result, T parser)
        {
            Match = result;
            Parser = parser;
        }

        public int CompareTo(Candidate<T> other)
            => Match.Index.CompareTo(other.Match.Index);
    }

    internal class UnclosableStream : Stream
    {
        private Stream _stream;

        public UnclosableStream(Stream stream)
        {
            _stream = stream;
        }

        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => _stream.CanSeek;
        public override bool CanWrite => _stream.CanWrite;
        public override long Length => _stream.Length;

        public override long Position
        {
            get => _stream.Position;
            set => _stream.Position = value;
        }

        public override void Flush() { }
        public override void Close() { }

        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);

        public override void SetLength(long value) => _stream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
