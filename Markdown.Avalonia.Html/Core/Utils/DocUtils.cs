using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
        
        public static Brush? GetForegroundColor(HtmlNode node)
        {
            var styleAttr = node.Attributes["style"];
            if (styleAttr == null) return null;

            // 匹配color样式（支持 color: red; / color:#fff; / color: rgb(255,0,0); 等）
            var colorMatch = Regex.Match(
                styleAttr.Value,
                @"color\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase
            );
            if (!colorMatch.Success) return null;

            string colorValue = colorMatch.Groups[1].Value.Trim();
            try
            {
                // 解析颜色值为Avalonia的Brush
                return ParseColorToBrush(colorValue);
            }
            catch
            {
                // 解析失败时返回null
                return null;
            }
        }

        // 内部方法：将颜色字符串转为SolidColorBrush
        public static SolidColorBrush ParseColorToBrush(string colorValue)
        {
            // 处理颜色名（如 red、blue）
            if (Color.TryParse(colorValue, out var namedColor))
            {
                return new SolidColorBrush(namedColor);
            }

            // 处理十六进制（如 #f00、#ff0000、#ff0000ff）
            if (colorValue.StartsWith("#"))
            {
                string hex = colorValue.TrimStart('#');
                // 补全短十六进制（#f00 → #ff0000）
                hex = hex.Length switch
                {
                    3 => string.Concat(hex.Select(c => $"{c}{c}")),
                    4 => string.Concat(hex.Select(c => $"{c}{c}")), // #rgba → #rrggbbaa
                    _ => hex
                };
                // 转换为Color
                if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var hexValue))
                {
                    Color color = Color.FromUInt32(hexValue);
                    return new SolidColorBrush(color);
                }
            }

            // 处理RGB（rgb(255,0,0)）和RGBA（rgba(255,0,0,0.5)）
            var rgbMatch = Regex.Match(
                colorValue,
                @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([0-9.]+)\s*)?\)",
                RegexOptions.IgnoreCase
            );
            if (rgbMatch.Success)
            {
                byte r = byte.Parse(rgbMatch.Groups[1].Value);
                byte g = byte.Parse(rgbMatch.Groups[2].Value);
                byte b = byte.Parse(rgbMatch.Groups[3].Value);
                float a = rgbMatch.Groups[4].Success ? float.Parse(rgbMatch.Groups[4].Value) : 1f;

                // 限制透明度范围0-1
                a = Math.Clamp(a, 0f, 1f);
                Color color = Color.FromArgb((byte)(a * 255), r, g, b);
                return new SolidColorBrush(color);
            }

            // 解析失败抛出异常
            throw new FormatException($"Unsupported color format: {colorValue}");
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
