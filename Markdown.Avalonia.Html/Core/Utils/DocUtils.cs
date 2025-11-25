using Avalonia.Controls;
using Avalonia.Layout;
using AvaloniaEdit;
using ColorTextBlock.Avalonia;
using HtmlAgilityPack;
using Markdown.Avalonia.SyntaxHigh;
using Markdown.Avalonia.SyntaxHigh.Extensions;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace Markdown.Avalonia.Html.Core.Utils
{
    static class DocUtils
    {
        public static HorizontalAlignment? GetHorizontalAlignment(HtmlNode node)
        {
            // 优先解析 align 属性（如 <p align="center">）
            var alignAttr = node.Attributes["align"];
            if (alignAttr != null)
            {
                return alignAttr.Value.ToLower() switch
                {
                    "left" => HorizontalAlignment.Left,
                    "right" => HorizontalAlignment.Right,
                    "center" => HorizontalAlignment.Center,
                    _ => null
                };
            }

            // 解析 style 中的 text-align（如 <div style="text-align: center;">）
            var styleAttr = node.Attributes["style"];
            if (styleAttr != null)
            {
                var match = Regex.Match(styleAttr.Value, @"text-align\s*:\s*(\w+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value.ToLower() switch
                    {
                        "left" => HorizontalAlignment.Left,
                        "right" => HorizontalAlignment.Right,
                        "center" => HorizontalAlignment.Center,
                        _ => null
                    };
                }
            }

            return null;
        }
        
        public static Control CreateCodeBlock(string? lang, string code, ReplaceManager manager, SyntaxHighlightProvider provider)
        {
            var txtEdit = new TextEditor();

            if (!String.IsNullOrEmpty(lang))
            {
                txtEdit.Tag = lang;
                txtEdit.SetValue(SyntaxHighlightWrapperExtension.ProviderProperty, provider);
            }

            txtEdit.Text = code;
            txtEdit.HorizontalAlignment = HorizontalAlignment.Stretch;
            txtEdit.IsReadOnly = true;

            var result = new Border();
            result.Classes.Add(Tags.TagCodeBlock.GetClass());
            result.Child = txtEdit;

            return result;
        }

        public static void TrimStart(CInline? inline)
        {
            if (inline is null) return;

            if (inline is CSpan span)
            {
                TrimStart(span.Content.FirstOrDefault());
            }
            else if (inline is CRun run)
            {
                run.Text = run.Text.TrimStart();
            }
        }

        public static void TrimEnd(CInline? inline)
        {
            if (inline is null) return;

            if (inline is CSpan span)
            {
                TrimEnd(span.Content.LastOrDefault());
            }
            else if (inline is CRun run)
            {
                run.Text = run.Text.TrimEnd();
            }
        }
    }
}
