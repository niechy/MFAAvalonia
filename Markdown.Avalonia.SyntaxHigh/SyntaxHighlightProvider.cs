using Avalonia.Platform;
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.TextMate;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Xml;
using System.Linq;
using TextMateSharp.Grammars;

namespace Markdown.Avalonia.SyntaxHigh
{
    public class SyntaxHighlightProvider
    {
        private ObservableCollection<Alias> _aliases;

        private Dictionary<string, string> _nameSolver;
        private Dictionary<string, IHighlightingDefinition> _definitions;
        // TextMate 相关的静态缓存
        private static readonly Lazy<RegistryOptions> _registryOptions = new Lazy<RegistryOptions>(() => new RegistryOptions(ThemeName.DarkPlus));

        // 缓存已安装 TextMate 的编辑器，避免重复安装
        private static readonly Dictionary<TextEditor, TextMate.Installation> _textMateInstallations =
            new Dictionary<TextEditor, TextMate.Installation>();
        private static readonly object _installLock = new object();

        public SyntaxHighlightProvider(ObservableCollection<Alias> aliases)
        {
            _aliases = aliases;
            _nameSolver = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _definitions = new Dictionary<string, IHighlightingDefinition>(StringComparer.OrdinalIgnoreCase);

            _aliases.CollectionChanged += (s, e) => AliasesCollectionChanged(e);
            AliasesCollectionChanged(null);
        }

        /// <summary>
        /// 解析语言别名为实际语言名称
        /// </summary>
        public string ResolveLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang))
                return lang;
            for (var i = 0; i < 10; ++i)
                if (_nameSolver.TryGetValue(lang.ToLower(), out var realName))
                    lang = realName;
                else
                    break;
            return lang;
        }

        /// <summary>
        /// 获取 AvaloniaEdit 的高亮定义（用于传统方式）
        /// </summary>
        public IHighlightingDefinition? Solve(string lang)
        {
            lang = ResolveLanguage(lang);

            if (_definitions.TryGetValue(lang, out var def))
                return def;

            // Try to get definition by extension first
            var result = HighlightingManager.Instance.GetDefinitionByExtension("." + lang);
            if (result != null)
                return result;

            // If not found by extension, try to get by name
            return HighlightingManager.Instance.GetDefinition(lang);
        }

        /// <summary>
        /// 为 TextEditor 应用 TextMate 高亮（支持更多语言如 jsonc）
        /// </summary>
        public void ApplyTextMateHighlighting(TextEditor editor, string lang)
        {
            if (editor == null || string.IsNullOrEmpty(lang))
                return;

            try
            {
                lang = ResolveLanguage(lang);
                var scopeName = GetTextMateScopeName(lang);

                if (string.IsNullOrEmpty(scopeName))
                    return;

                lock (_installLock)
                {
                    // 检查是否已经安装过 TextMate
                    if (!_textMateInstallations.TryGetValue(editor, out var installation))
                    {
                        installation = editor.InstallTextMate(_registryOptions.Value);
                        _textMateInstallations[editor] = installation;

                        // 当编辑器被卸载时清理缓存
                        editor.DetachedFromVisualTree += (s, e) =>
                        {
                            lock (_installLock)
                            {
                                if (_textMateInstallations.TryGetValue(editor, out var inst))
                                {
                                    inst.Dispose();
                                    _textMateInstallations.Remove(editor);
                                }
                            }
                        };
                    }

                    installation.SetGrammar(scopeName);
                }
            }
            catch (Exception)
            {
                // TextMate 高亮失败时静默处理，编辑器将显示无高亮的纯文本
            }
        }

        /// <summary>
        /// 获取语言对应的 TextMate scope name
        /// </summary>
        private string? GetTextMateScopeName(string lang)
        {
            if (string.IsNullOrEmpty(lang))
                return null;

            lang = lang.ToLowerInvariant();

            // TextMate scope 映射表
            var scopeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // JSON 相关 - 这是 HighlightingManager 不支持的
                {
                    "json", "source.json"
                },
                {
                    "jsonc", "source.json.comments"
                },
                {
                    "json5", "source.json"
                },

                // JavaScript/TypeScript
                {
                    "js", "source.js"
                },
                {
                    "javascript", "source.js"
                },
                {
                    "jsx", "source.js.jsx"
                },
                {
                    "ts", "source.ts"
                },
                {
                    "typescript", "source.ts"
                },
                {
                    "tsx", "source.tsx"
                },

                // C#
                {
                    "cs", "source.cs"
                },
                {
                    "csharp", "source.cs"
                },
                {
                    "c#", "source.cs"
                },

                // Python
                {
                    "py", "source.python"
                },
                {
                    "python", "source.python"
                },

                // C/C++
                {
                    "c", "source.c"
                },
                {
                    "cpp", "source.cpp"
                },
                {
                    "c++", "source.cpp"
                },
                {
                    "h", "source.c"
                },
                {
                    "hpp", "source.cpp"
                },

                // Web
                {
                    "html", "text.html.basic"
                },
                {
                    "htm", "text.html.basic"
                },
                {
                    "css", "source.css"
                },
                {
                    "scss", "source.css.scss"
                },
                {
                    "less", "source.css.less"
                },
                {
                    "xml", "text.xml"
                },

                // Shell
                {
                    "sh", "source.shell"
                },
                {
                    "bash", "source.shell"
                },
                {
                    "zsh", "source.shell"
                },
                {
                    "ps1", "source.powershell"
                },
                {
                    "powershell", "source.powershell"
                },

                // 其他常用语言
                {
                    "java", "source.java"
                },
                {
                    "go", "source.go"
                },
                {
                    "rust", "source.rust"
                },
                {
                    "rs", "source.rust"
                },
                {
                    "ruby", "source.ruby"
                },
                {
                    "rb", "source.ruby"
                },
                {
                    "php", "source.php"
                },
                {
                    "swift", "source.swift"
                },
                {
                    "kotlin", "source.kotlin"
                },
                {
                    "kt", "source.kotlin"
                },
                {
                    "scala", "source.scala"
                },
                {
                    "lua", "source.lua"
                },
                {
                    "perl", "source.perl"
                },
                {
                    "r", "source.r"
                },
                {
                    "sql", "source.sql"
                },
                // 配置文件
                {
                    "yaml", "source.yaml"
                },
                {
                    "yml", "source.yaml"
                },
                {
                    "toml", "source.toml"
                },
                {
                    "ini", "source.ini"
                },
                // 标记语言
                {
                    "md", "text.html.markdown"
                },
                {
                    "markdown", "text.html.markdown"
                },
                {
                    "tex", "text.tex"
                },
                {
                    "latex", "text.tex.latex"
                },

                // 其他
                {
                    "dockerfile", "source.dockerfile"
                },
                {
                    "docker", "source.dockerfile"
                },
                {
                    "makefile", "source.makefile"
                },
                {
                    "make", "source.makefile"
                },
                {
                    "diff", "source.diff"
                },
                {
                    "patch", "source.diff"
                },
            };

            if (scopeMap.TryGetValue(lang, out var scopeName))
                return scopeName;

            // 尝试通过 RegistryOptions 获取
            try
            {
                var language = _registryOptions.Value.GetLanguageByExtension("." + lang);
                if (language != null)
                {
                    return _registryOptions.Value.GetScopeByLanguageId(language.Id);
                }
            }
            catch
            {
                // 忽略错误
            }

            return null;
        }

        private void AliasesCollectionChanged(NotifyCollectionChangedEventArgs? arg)
        {
            IEnumerable<Alias> adding;

            if (arg is null || arg.OldItems != null)
            {
                _nameSolver.Clear();
                _definitions.Clear();
                SetupForBuiltIn();

                adding = _aliases;
            }
            else if (arg?.NewItems != null)
            {
                adding = arg.NewItems.Cast<Alias>();
            }
            else
                adding = Array.Empty<Alias>();


            foreach (var alias in adding)
            {
                if (alias.Name is null) continue;

                if (!String.IsNullOrEmpty(alias.RealName))
                {
                    _nameSolver[alias.Name] = alias.RealName;
                }
                else if (alias.XSHD != null)
                {
                    var definition = Load(alias.XSHD);

                    if (definition is null)
                        throw new ArgumentException($"Failed loading: {alias.XSHD}");

                    _definitions[alias.Name] = definition;
                }
            }
        }

        private void SetupForBuiltIn()
        {
            // https://github.com/AvaloniaUI/AvaloniaEdit/blob/master/src/AvaloniaEdit/Highlighting/Resources/Resources.cs
            // 语言别名映射
            _nameSolver["c#"] = "cs";
            _nameSolver["csharp"] = "cs";
            _nameSolver["javascript"] = "js";
            _nameSolver["coco"] = "atg";
            _nameSolver["c++"] = "cpp";
            _nameSolver["powershell"] = "ps1";
            _nameSolver["python"] = "py";
            _nameSolver["markdown"] = "md";

            // 添加更多别名以支持 TextMate
            _nameSolver["typescript"] = "ts";
            _nameSolver["shell"] = "sh";
            _nameSolver["bash"] = "sh";
        }

        private IHighlightingDefinition? Load(Uri source)
        {
            switch (source.Scheme)
            {
                case "file":
                    return File.Exists(source.LocalPath) ? Open(File.OpenRead(source.LocalPath)) : null;

                case "avares":
                    return AssetLoader.Exists(source) ? Open(AssetLoader.Open(source)) : null;

                default:
                    throw new ArgumentException($"unsupport scheme '{source.Scheme}'");
            }

            IHighlightingDefinition Open(Stream stream)
            {
                try
                {
                    using (var reader = XmlReader.Create(stream))
                        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
                finally
                {
                    stream.Close();
                }
            }
        }
    }
}
