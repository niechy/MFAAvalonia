using Avalonia;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using System;
using System.Collections.Generic;

namespace Markdown.Avalonia.SyntaxHigh
{
    /// <summary>
    /// Provides TextMate-based syntax highlighting with theme support
    /// </summary>
    public class TextMateHighlightProvider : IDisposable
    {
        private static TextMateHighlightProvider? _instance;
        private static readonly object _lock = new object();
        private readonly RegistryOptions _registryOptions;
        private readonly Dictionary<TextEditor, TextMate.Installation> _installations = new();
        private ThemeName _currentTheme;
        private bool _disposed;

        public static TextMateHighlightProvider Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new TextMateHighlightProvider();
                    }
                }
                return _instance;
            }
        }

        private TextMateHighlightProvider()
        {
            // Detect initial theme based on application theme
            _currentTheme = DetectTheme();
            _registryOptions = new RegistryOptions(_currentTheme);

            // Subscribe to theme changes
            if (Application.Current != null)
            {
                Application.Current.ActualThemeVariantChanged += OnThemeChanged;
            }
        }

        private ThemeName DetectTheme()
        {
            if (Application.Current?.ActualThemeVariant == ThemeVariant.Dark)
            {
                return ThemeName.DarkPlus;
            }
            return ThemeName.LightPlus;
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            var newTheme = DetectTheme();
            if (newTheme != _currentTheme)
            {
                _currentTheme = newTheme;
                UpdateAllEditorThemes();
            }
        }

        private void UpdateAllEditorThemes()
        {
            foreach (var kvp in _installations)
            {
                try
                {
                    kvp.Value.SetTheme(_registryOptions.LoadTheme(_currentTheme));
                }
                catch
                {
                    // Ignore errors during theme update
                }
            }
        }

        /// <summary>
        /// Apply TextMate syntax highlighting to a TextEditor
        /// </summary>
        /// <param name="editor">The TextEditor to apply highlighting to</param>
        /// <param name="languageId">The language identifier (e.g., "jsonc", "csharp", "python")</param>
        public void ApplyHighlighting(TextEditor editor, string languageId)
        {
            if (_disposed) return;

            // Remove existing installation if any
            RemoveHighlighting(editor);

            try
            {
                // Install TextMate
                var installation = editor.InstallTextMate(_registryOptions);
                _installations[editor] = installation;

                // Get the scope name for the language
                var language = GetLanguageByIdOrExtension(languageId);
                if (language != null)
                {
                    var scopeName = _registryOptions.GetScopeByLanguageId(language.Id);
                    installation.SetGrammar(scopeName);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply TextMate highlighting for {languageId}: {ex.Message}");
            }
        }

        private Language? GetLanguageByIdOrExtension(string languageId)
        {
            if (string.IsNullOrEmpty(languageId))
                return null;

            // Try to get by extension first (with dot prefix)
            var lang = _registryOptions.GetLanguageByExtension("." + languageId);
            if (lang != null)
                return lang;

            // Try without dot
            lang = _registryOptions.GetLanguageByExtension(languageId);
            if (lang != null)
                return lang;

            // Try to find by language ID directly
            foreach (var availableLang in _registryOptions.GetAvailableLanguages())
            {
                if (string.Equals(availableLang.Id, languageId, StringComparison.OrdinalIgnoreCase))
                    return availableLang;
            }

            return null;
        }

        /// <summary>
        /// Remove TextMate highlighting from a TextEditor
        /// </summary>
        /// <param name="editor">The TextEditor to remove highlighting from</param>
        public void RemoveHighlighting(TextEditor editor)
        {
            if (_installations.TryGetValue(editor, out var installation))
            {
                try
                {
                    installation.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
                _installations.Remove(editor);
            }
        }

        /// <summary>
        /// Set the theme for all editors
        /// </summary>
        /// <param name="themeName">The theme to apply</param>
        public void SetTheme(ThemeName themeName)
        {
            _currentTheme = themeName;
            UpdateAllEditorThemes();
        }

        /// <summary>
        /// Get the current theme
        /// </summary>
        public ThemeName CurrentTheme => _currentTheme;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (Application.Current != null)
            {
                Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
            }

            foreach (var installation in _installations.Values)
            {
                try
                {
                    installation.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }
            _installations.Clear();
        }
    }
}
