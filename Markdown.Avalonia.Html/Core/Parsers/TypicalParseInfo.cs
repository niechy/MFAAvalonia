﻿using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ColorTextBlock.Avalonia;
using HtmlAgilityPack;
using Markdown.Avalonia.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Markdown.Avalonia.Html.Core.Parsers
{
    public class TypicalParseInfo
    {
        public string HtmlTag { get; }
        public string FlowDocumentTagText { get; }
        public Type? FlowDocumentTag { get; }
        public string? TagNameReference { get; }
        public Tags TagName { get; }
        public string? ExtraModifyName { get; }

        public TypicalParseInfo(string[] line)
        {
            FlowDocumentTagText = line[1];

            if (FlowDocumentTagText.StartsWith("#"))
            {
                FlowDocumentTag = null;
            }
            else
            {
                Type? elementType = AppDomain.CurrentDomain
                                             .GetAssemblies()
                                             .Select(asm => asm.GetType(FlowDocumentTagText))
                                             .OfType<Type>()
                                             .FirstOrDefault();

                if (elementType is null)
                    throw new ArgumentException($"Failed to load type '{line[1]}'");

                FlowDocumentTag = elementType;
            }


            HtmlTag = line[0];
            TagNameReference = GetArrayAt(line, 2);
            ExtraModifyName = GetArrayAt(line, 3);

            if (TagNameReference is not null)
            {
                TagName = (Tags)Enum.Parse(typeof(Tags), TagNameReference);
            }

            if (ExtraModifyName is not null)
            {
                switch ("ExtraModify" + ExtraModifyName)
                {
                    case nameof(ExtraModifyHyperlink):
                    case nameof(ExtraModifyStrikethrough):
                    case nameof(ExtraModifySubscript):
                    case nameof(ExtraModifySuperscript):
                    case nameof(ExtraModifyAcronym):
                    case nameof(ExtraModifyCenter):
                        break;

                    default:
                        throw new InvalidOperationException("unknown method ExtraModify" + ExtraModifyName);
                }
            }

            static string? GetArrayAt(string[] array, int idx)
            {
                if (idx < array.Length && !string.IsNullOrWhiteSpace(array[idx]))
                {
                    return array[idx];
                }
                return null;
            }
        }

        public bool TryReplace(HtmlNode node, ReplaceManager manager, out IEnumerable<StyledElement> generated)
        {
            // 创建控件实例
            if (FlowDocumentTag is null)
            {
                switch (FlowDocumentTagText)
                {
                    case "#border":
                        var pnl = new StackPanel { Orientation = Orientation.Vertical };
                        var parseResult = manager.ParseChildrenAndGroup(node).ToArray();
                        foreach (var ctrl in parseResult)
                            pnl.Children.Add(ctrl);

                        var bdr = new Border { Child = pnl };
                        generated = new[] { bdr };
                        break;

                    case "#blocks":
                        generated = manager.ParseChildrenAndGroup(node).ToArray();
                        break;

                    case "#jagging":
                        generated = manager.ParseChildrenJagging(node).ToArray();
                        break;

                    case "#inlines":
                        if (manager.ParseChildrenJagging(node).TryCast<CInline>(out var inlines))
                        {
                            generated = inlines.ToArray();
                            break;
                        }
                        else
                        {
                            generated = EnumerableExt.Empty<StyledElement>();
                            return false;
                        }

                    default:
                        throw new InvalidOperationException();
                }
            }
            else
            {
                var tag = (StyledElement)Activator.CreateInstance(FlowDocumentTag)!;

                if (tag is StackPanel pnl)
                {
                    pnl.Orientation = Orientation.Vertical;
                    var parseResult = manager.ParseChildrenAndGroup(node).ToArray();
                    foreach (var ctrl in parseResult)
                        pnl.Children.Add(ctrl);
                }
                else if (tag is CTextBlock textbox)
                {
                    var parseResult = manager.ParseChildrenJagging(node).ToArray();

                    if (parseResult.TryCast<CInline>(out var parsed))
                    {
                        textbox.Content.AddRange(parsed);
                    }
                    else if (parseResult.Length == 1 && parseResult[0] is CTextBlock)
                    {
                        tag = parseResult[0];
                    }
                    else
                    {
                        generated = EnumerableExt.Empty<StyledElement>();
                        return false;
                    }
                }
                else if (tag is CSpan span)
                {
                    if (!SetupCSpan(span))
                    {
                        generated = EnumerableExt.Empty<StyledElement>();
                        return false;
                    }
                }
                else if (tag is CCode code)
                {
                    var codecontent = (AvaloniaList<CInline>)code.Content;
                    var codespan = new CSpan();
                    codecontent.Add(codespan);

                    if (!SetupCSpan(codespan))
                    {
                        generated = EnumerableExt.Empty<StyledElement>();
                        return false;
                    }
                }
                else if (tag is not CLineBreak)
                {
                    throw new InvalidOperationException();
                }

                generated = new[] { tag };

                bool SetupCSpan(CSpan span)
                {
                    var content = (AvaloniaList<CInline>)span.Content;
                    var parseResult = manager.ParseChildrenJagging(node).ToArray();

                    if (parseResult.TryCast<CInline>(out var parsed))
                    {
                        content.AddRange(parsed);
                    }
                    else if (tag is CSpan && manager.Grouping(parseResult).TryCast<CTextBlock>(out var paragraphs))
                    {
                        foreach (var para in paragraphs)
                            foreach (var inline in para.Content.ToArray())
                                content.Add(inline);
                    }
                    else return false;

                    return true;
                }
            }

            // 应用对齐样式（核心逻辑：处理align属性和text-align样式）
            var alignment = DocUtils.GetHorizontalAlignment(node);
            if (alignment.HasValue)
            {
                foreach (var element in generated)
                {
                    // 处理容器控件（如StackPanel、Border）的水平对齐
                    if (element is Panel panel)
                    {
                        panel.HorizontalAlignment = alignment.Value;
                    }
                    // 处理文本控件（如CTextBlock）的文本对齐
                    if (element is CTextBlock textBlock)
                    {
                        textBlock.TextAlignment = alignment.Value switch
                        {
                            HorizontalAlignment.Left => TextAlignment.Left,
                            HorizontalAlignment.Right => TextAlignment.Right,
                            HorizontalAlignment.Center => TextAlignment.Center,
                            _ => textBlock.TextAlignment
                        };
                        textBlock.HorizontalAlignment = alignment.Value;
                    }
                    // 处理Border控件的对齐
                    if (element is Border border)
                    {
                        border.HorizontalAlignment = alignment.Value;
                        if (border.Child is Panel borderChild)
                        {
                            borderChild.HorizontalAlignment = alignment.Value;
                        }
                    }
                }
            }

            // 应用标签样式
            if (TagNameReference is not null)
            {
                var clsNm = TagName.GetClass();
                foreach (var tag in generated)
                {
                    tag.Classes.Add(clsNm);
                    if (tag is Border bdr)
                        bdr.Child.Classes.Add(clsNm);
                }
            }

            // 额外样式修改
            if (ExtraModifyName is not null)
            {
                switch ("ExtraModify" + ExtraModifyName)
                {
                    case nameof(ExtraModifyHyperlink):
                        foreach (var tag in generated)
                            ExtraModifyHyperlink((CHyperlink)tag, node, manager);
                        break;
                    case nameof(ExtraModifyStrikethrough):
                        foreach (var tag in generated)
                            ExtraModifyStrikethrough((CSpan)tag, node, manager);
                        break;
                    case nameof(ExtraModifySubscript):
                        foreach (var tag in generated)
                            ExtraModifySubscript((CSpan)tag, node, manager);
                        break;
                    case nameof(ExtraModifySuperscript):
                        foreach (var tag in generated)
                            ExtraModifySuperscript((CSpan)tag, node, manager);
                        break;
                    case nameof(ExtraModifyAcronym):
                        foreach (var tag in generated)
                            ExtraModifyAcronym((CSpan)tag, node, manager);
                        break;
                    case nameof(ExtraModifyCenter):
                        foreach (var tag in generated)
                            ExtraModifyCenter((Border)tag, node, manager);
                        break;
                }
            }

            return true;
        }

        public void ExtraModifyHyperlink(CHyperlink link, HtmlNode node, ReplaceManager manager)
        {
            var href = node.Attributes["href"]?.Value;
            if (href is not null)
            {
                link.CommandParameter = href;
                link.Command = (urlTxt) =>
                {
                    var command = manager.HyperlinkCommand;
                    if (command != null && command.CanExecute(urlTxt))
                    {
                        command.Execute(urlTxt);
                    }
                };
            }
        }

        public void ExtraModifyStrikethrough(CSpan span, HtmlNode node, ReplaceManager manager)
        {
            span.IsStrikethrough = true;
        }

        public void ExtraModifySubscript(CSpan span, HtmlNode node, ReplaceManager manager)
        {
            // TODO: 实现下标逻辑
        }

        public void ExtraModifySuperscript(CSpan span, HtmlNode node, ReplaceManager manager)
        {
            // TODO: 实现上标逻辑
        }

        public void ExtraModifyAcronym(CSpan span, HtmlNode node, ReplaceManager manager)
        {
            // TODO: 实现首字母缩写逻辑
        }

        public void ExtraModifyCenter(Border center, HtmlNode node, ReplaceManager manager)
        {
            center.HorizontalAlignment = HorizontalAlignment.Center;
            foreach (var child in ((StackPanel)center.Child!).Children)
            {
                if (child is CTextBlock cbox)
                {
                    cbox.HorizontalAlignment = HorizontalAlignment.Center;
                }
            }
        }

        internal static IEnumerable<TypicalParseInfo> Load(string resourcePath)
        {
            var asm = typeof(TypicalBlockParser).Assembly;
            using var stream = asm.GetManifestResourceStream(resourcePath);

            if (stream is null)
                throw new ArgumentException($"resource not found: '{resourcePath}'");

            using var reader = new StreamReader(stream!);
            while (reader.ReadLine() is string line)
            {
                if (line.StartsWith("#")) continue;
                var elements = line.Split('|').Select(t => t.Trim()).ToArray();
                yield return new TypicalParseInfo(elements);
            }
        }
    }
}