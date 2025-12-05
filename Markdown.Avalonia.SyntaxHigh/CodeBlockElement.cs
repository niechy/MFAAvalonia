using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using ColorDocument.Avalonia;
using System.Text;

namespace Markdown.Avalonia.SyntaxHigh
{
    internal class CodeBlockElement : DocumentElement
    {
        private string _lang;
        private string _code;
        private Lazy<Border> _control;
        private TextEditor? _textEditor;

        public override Control Control => _control.Value;

        public override IEnumerable<DocumentElement> Children => Array.Empty<DocumentElement>();

        public CodeBlockElement(SyntaxHighlightProvider provider, string lang, string code)
        {
            _lang = lang;
            _code = code;
            _control = new Lazy<Border>(() => Create(lang, code));
        }

        public override void ConstructSelectedText(StringBuilder stringBuilder)
        {
            stringBuilder.Append(_code);
        }

        public override void Select(Point from, Point to)
        {
            Helper?.Register(Control);}

        public override void UnSelect()
        {
            Helper?.Unregister(Control);
        }

        private Border Create(string lang, string code)
        {
            // 创建展开/折叠三角形图标
            var expandIcon = new PathIcon()
            {
                Data = Geometry.Parse("M 0,0 L 6,5 L 0,10 Z"),
                Width = 10,
                Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new RotateTransform(90), // 默认展开状态，箭头向下
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
            };
            expandIcon.Classes.Add("CodeBlockExpandIcon");

            // 语言标签
            var langLabel = new TextBlock()
            {
                Text = string.IsNullOrEmpty(lang) ? "code" : lang,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                FontWeight = FontWeight.DemiBold,
                FontSize = 12
            };
            langLabel.Classes.Add("CodeBlockLangLabel");

            // 复制按钮 - GitHub 风格的双方块图标
            var copyIcon = new PathIcon()
            {
                Data = Geometry.Parse("M 3,0 L 11,0 L 11,8 L 9,8 L 9,2 L 3,2 Z M 0,3 L 8,3 L 8,11 L 0,11 Z M 1,4 L 1,10 L 7,10 L 7,4 Z"),
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            copyIcon.Classes.Add("CodeBlockCopyIcon");

            var copyButton = new Button()
            {
                Content = copyIcon,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),Padding = new Thickness(6),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center
            };
            copyButton.Classes.Add("CodeBlockCopyButton");

            // Header 左侧部分（展开图标 + 语言名称）
            var headerLeft = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            headerLeft.Children.Add(expandIcon);
            headerLeft.Children.Add(langLabel);

            // Header 右侧部分（复制按钮）
            var headerRight = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 4, 0)
            };
            headerRight.Children.Add(copyButton);

            // Header 容器
            var header = new DockPanel()
            {
                Height = 32,
                LastChildFill = false
            };
            DockPanel.SetDock(headerLeft, Dock.Left);
            DockPanel.SetDock(headerRight, Dock.Right);
            header.Children.Add(headerLeft);
            header.Children.Add(headerRight);
            header.Classes.Add("CodeBlockHeader");

            // 代码编辑器
            _textEditor = new TextEditor();
            _textEditor.Text = code;
            _textEditor.HorizontalAlignment = HorizontalAlignment.Stretch;
            _textEditor.IsReadOnly = true;
            _textEditor.Margin = new Thickness(0, 0, 0, 8);
            _textEditor.Classes.Add("CodeBlockEditor");
            _textEditor.ShowLineNumbers = false;
            _textEditor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _textEditor.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

            // Apply TextMate syntax highlighting
            if (!string.IsNullOrEmpty(lang))
            {
                TextMateHighlightProvider.Instance.ApplyHighlighting(_textEditor, lang);
            }

            // 代码内容容器（可折叠）
            var codeContent = new Border()
            {
                Child = _textEditor,
                Padding = new Thickness(8, 4, 8, 4),
                IsVisible = true // 默认展开
            };
            codeContent.Classes.Add("CodeBlockContent");

            // 复制按钮点击事件
            copyButton.Click += (s, e) =>
            {
                var clipboard = TopLevel.GetTopLevel(_textEditor)?.Clipboard;
                clipboard?.SetTextAsync(_textEditor.Text);
            };

            // Header 点击事件（展开/折叠）
            header.PointerPressed += (s, e) =>
            {
                codeContent.IsVisible = !codeContent.IsVisible;
                // 旋转箭头图标
                if (codeContent.IsVisible)
                {
                    expandIcon.RenderTransform = new RotateTransform(90); // 向下箭头（展开）
                }
                else
                {
                    expandIcon.RenderTransform = new RotateTransform(0); // 向右箭头（折叠）
                }
            };

            // 主容器
            var mainContainer = new StackPanel()
            {
                Orientation = Orientation.Vertical
            };
            mainContainer.Children.Add(header);
            mainContainer.Children.Add(codeContent);

            var result = new Border();
            result.Classes.Add(Markdown.CodeBlockClass);
            result.Child = mainContainer;

            return result;
        }
    }
}
