using Lang.Avalonia;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;

#nullable enable

namespace MFAAvalonia.Localization;

/// <summary>
/// 适配Assets.Localization路径下Resx资源的本地化插件
/// </summary>
public class MFAResxLangPlugin : ILangPlugin
{
    // 资源管理器字典（资源基础名称 -> 资源管理器）
    private Dictionary<string, ResourceManager>? _resourceManagers;
    // 默认文化
    private CultureInfo? _defaultCulture;
    // 配置：资源所在的命名空间前缀（适配Assets.Localization路径）
    public string ResourceNamespacePrefix { get; set; } = "MFAAvalonia.Assets.Localization";

    // 存储加载的资源（文化名称 -> 语言资源）
    public Dictionary<string, LocalizationLanguage> Resources { get; } = new();

    // 标记（原插件兼容字段，此处无用但保留接口实现）
    public string Mark { get; set; } = "i18n";

    // 当前文化
    public CultureInfo Culture
    {
        get => _culture ?? CultureInfo.InvariantCulture;
        set
        {
            _culture = value;
            Sync(value); // 切换文化时同步资源
        }
    }

    private CultureInfo? _culture;

    /// <summary>
    /// 加载资源（指定默认文化）
    /// </summary>
    public void Load(CultureInfo cultureInfo)
    {
        _defaultCulture = cultureInfo;
        _culture = cultureInfo;

        // 加载所有程序集中符合命名空间前缀的资源
        LoadResourceManagers();
        Sync(_culture);
    }

    /// <summary>
    /// 追加加载资源程序集
    /// </summary>
    public void AddResource(params Assembly[]? assemblies)
    {
        if (assemblies == null || assemblies.Length == 0) return;

        // 从指定程序集中加载资源
        var newManagers = GetResourceManagersFromAssemblies(assemblies);
        if (newManagers == null || newManagers.Count == 0) return;

        if (_resourceManagers == null)
            _resourceManagers = new Dictionary<string, ResourceManager>();

        // 合并新资源（去重）
        foreach (var (baseName, manager) in newManagers)
        {
            if (!_resourceManagers.ContainsKey(baseName))
                _resourceManagers[baseName] = manager;
        }

        Sync(_culture);
    }

    /// <summary>
    /// 获取支持的语言（简化实现，从资源中提取）
    /// </summary>
    public List<LocalizationLanguage>? GetLanguages()
    {
        return Resources.Values.Distinct().ToList();
    }

    /// <summary>
    /// 获取指定键的资源
    /// </summary>
    public string? GetResource(string key, string? cultureName = null)
    {
        var targetCulture = string.IsNullOrWhiteSpace(cultureName)
            ? Culture
            : new CultureInfo(cultureName);

        // 1. 尝试从目标文化获取
        if (TryGetResource(key, targetCulture.Name, out var value))
            return value;

        // 2. 尝试从默认文化获取
        if (TryGetResource(key, _defaultCulture?.Name ?? string.Empty, out value))
            return value;

        // 3. 尝试从不变文化（默认资源）获取
        if (TryGetResource(key, CultureInfo.InvariantCulture.Name, out value))
            return value;

        // 4. 未找到返回键本身
        return key;
    }

    /// <summary>
    /// 同步指定文化的资源到字典
    /// </summary>
    private void Sync(CultureInfo? cultureInfo)
    {
        if (cultureInfo == null || _resourceManagers == null || _resourceManagers.Count == 0)
            return;

        var cultureName = cultureInfo.Name;
        // 初始化当前文化的资源容器
        if (!Resources.ContainsKey(cultureName))
        {
            Resources[cultureName] = new LocalizationLanguage
            {
                Language = cultureInfo.DisplayName,
                Description = cultureInfo.NativeName,
                CultureName = cultureName
            };
        }
        var currentLang = Resources[cultureName];
        currentLang.Languages.Clear(); // 清空旧资源

        // 从所有资源管理器加载当前文化的资源
        foreach (var (baseName, manager) in _resourceManagers)
        {
            try
            {
                // 获取当前文化的资源集（包含该文化特有资源）
                var cultureResources = manager.GetResourceSet(cultureInfo, true, true);
                // 获取默认资源集（不变文化，作为 fallback）
                var invariantResources = manager.GetResourceSet(CultureInfo.InvariantCulture, true, true);

                // 合并资源（当前文化优先，默认资源补充）
                var allResources = new Dictionary<object, object?>();
                if (invariantResources != null)
                {
                    foreach (DictionaryEntry entry in invariantResources)
                        allResources[entry.Key] = entry.Value;
                }
                if (cultureResources != null)
                {
                    foreach (DictionaryEntry entry in cultureResources)
                        allResources[entry.Key] = entry.Value; // 覆盖默认资源
                }

                // 存入当前文化的资源字典
                foreach (var (keyObj, valueObj) in allResources)
                {
                    if (keyObj is string key && valueObj is string value)
                        currentLang.Languages[key] = value;
                }
            }
            catch (Exception ex)
            {
                // 忽略单个资源文件的加载错误
                Console.WriteLine($"加载资源 {baseName} 失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 从所有程序集中加载资源管理器
    /// </summary>
    private void LoadResourceManagers()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic) // 排除动态程序集
            .ToList();

        _resourceManagers = GetResourceManagersFromAssemblies(assemblies);
    }

    /// <summary>
    /// 从指定程序集中提取符合命名空间前缀的资源管理器
    /// </summary>
    private Dictionary<string, ResourceManager>? GetResourceManagersFromAssemblies(IEnumerable<Assembly> assemblies)
    {
        var managers = new Dictionary<string, ResourceManager>();

        foreach (var assembly in assemblies)
        {
            try
            {
                // 获取程序集中所有嵌入的资源名称
                var resourceNames = assembly.GetManifestResourceNames();

                // 筛选出符合命名空间前缀的资源（如 "MFAAvalonia.Assets.Localization.Strings.resx" 编译后名称）
                var targetResourceNames = resourceNames
                    .Where(name => name.StartsWith(ResourceNamespacePrefix, StringComparison.OrdinalIgnoreCase))
                    .Select(GetResourceBaseName) // 提取资源基础名称（不含文化和扩展名）
                    .Distinct() // 去重（同一资源的不同文化版本）
                    .ToList();

                // 为每个基础名称创建资源管理器
                foreach (var baseName in targetResourceNames)
                {
                    if (!managers.ContainsKey(baseName))
                    {
                        var manager = new ResourceManager(baseName, assembly);
                        managers[baseName] = manager;
                    }
                }
            }
            catch (Exception ex)
            {
                // 忽略单个程序集的加载错误
                Console.WriteLine($"加载程序集 {assembly.FullName} 资源失败: {ex.Message}");
            }
        }

        return managers.Count > 0 ? managers : null;
    }

    /// <summary>
    /// 从嵌入资源名称提取资源基础名称（如 "MFAAvalonia.Assets.Localization.Strings.fr.resx" → "MFAAvalonia.Assets.Localization.Strings"）
    /// </summary>
    private string GetResourceBaseName(string manifestResourceName)
    {
        // 移除扩展名（.resources 或 .resx，编译后通常是 .resources）
        var nameWithoutExt = manifestResourceName;
        if (nameWithoutExt.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
            nameWithoutExt = nameWithoutExt[..^".resources".Length];

        // 移除文化后缀（如 .en-US、.zh-Hant）
        var cultureSeparatorIndex = nameWithoutExt.LastIndexOf('.');
        if (cultureSeparatorIndex > 0)
        {
            var potentialCulture = nameWithoutExt[(cultureSeparatorIndex + 1)..];
            // 简单判断是否为文化名称（包含连字符或符合语言代码格式）
            if (potentialCulture.Contains('-')
                || CultureInfo.GetCultures(CultureTypes.AllCultures)
                    .Any(c => c.Name.Equals(potentialCulture, StringComparison.OrdinalIgnoreCase)))
            {
                nameWithoutExt = nameWithoutExt[..cultureSeparatorIndex];
            }
        }

        return nameWithoutExt;
    }

    /// <summary>
    /// 尝试从指定文化获取资源
    /// </summary>
    private bool TryGetResource(string key, string cultureName, out string? value)
    {
        value = null;
        if (!Resources.TryGetValue(cultureName, out var lang))
            return false;

        return lang.Languages.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value);
    }
}
