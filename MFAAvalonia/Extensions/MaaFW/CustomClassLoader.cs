using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 自定义类加载器，用于动态编译和加载 C# 代码文件
/// </summary>
public class CustomClassLoader
{
    private static List<MetadataReference>? _metadataReferences;
    private static bool _shouldLoadCustomClasses = true;
    private static FileSystemWatcher? _watcher;
    private static IEnumerable<CustomValue<object>>? _customClasses;

    /// <summary>
    /// 获取当前应用程序域中所有程序集的元数据引用
    /// </summary>
    private static List<MetadataReference> GetMetadataReferences()
    {
        if (_metadataReferences == null)
        {
            var domainAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            _metadataReferences = new List<MetadataReference>();

            foreach (var assembly in domainAssemblies)
            {
                if (!assembly.IsDynamic)
                {
                    try
                    {
                        unsafe
                        {
                            if (assembly.TryGetRawMetadata(out byte* blob, out int length))
                            {
                                var moduleMetadata = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
                                var assemblyMetadata = AssemblyMetadata.Create(moduleMetadata);
                                var metadataReference = assemblyMetadata.GetReference();
                                _metadataReferences.Add(metadataReference);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Warning($"Failed to get metadata for assembly {assembly.FullName}: {ex.Message}");
                    }
                }
            }

            // 添加 System.Linq.Expressions 程序集引用
            try
            {
                unsafe
                {
                    if (typeof(System.Linq.Expressions.Expression).Assembly.TryGetRawMetadata(out byte* blob, out int length))
                    {
                        _metadataReferences.Add(AssemblyMetadata.Create(ModuleMetadata.CreateFromMetadata((IntPtr)blob, length)).GetReference());
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Warning($"Failed to add System.Linq.Expressions reference: {ex.Message}");
            }
        }
        return _metadataReferences;
    }

    /// <summary>
    /// 加载并实例化指定目录中实现了指定接口的自定义类
    /// </summary>
    /// <param name="directory">包含 .cs 文件的目录路径</param>
    /// <param name="interfacesToImplement">要实现的接口名称数组</param>
    /// <returns>自定义类实例的集合</returns>
    private static IEnumerable<CustomValue<object>> LoadAndInstantiateCustomClasses(string directory, string[] interfacesToImplement)
    {
        var customClasses = new List<CustomValue<object>>();

        if (!Directory.Exists(directory))
        {
            LoggerHelper.Info($"Custom directory does not exist: {directory}");
            return customClasses;
        }

        // 设置文件监视器
        if (_watcher == null)
        {
            try
            {
                _watcher = new FileSystemWatcher(directory)
                {
                    Filter = "*.cs",
                    EnableRaisingEvents = true
                };
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Deleted += OnFileChanged;
                _watcher.Renamed += OnFileChanged;
                LoggerHelper.Info($"File watcher started for directory: {directory}");
            }
            catch (Exception ex)
            {
                LoggerHelper.Warning($"Failed to create file watcher: {ex.Message}");
            }
        }

        var csFiles = Directory.GetFiles(directory, "*.cs");
        if (csFiles.Length == 0)
        {
            LoggerHelper.Info($"No .cs files found in directory: {directory}");
            return customClasses;
        }

        var references = GetMetadataReferences();

        foreach (var filePath in csFiles)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(filePath);
                LoggerHelper.Info($"Trying to parse custom class: {name}");

                var code = File.ReadAllText(filePath);
                var codeLines = code.Split(new[]
                {
                    '\n'
                }, StringSplitOptions.RemoveEmptyEntries).ToList();

                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var compilation = CSharpCompilation.Create($"DynamicAssembly_{name}_{Guid.NewGuid():N}")
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true)
                        .WithOptimizationLevel(OptimizationLevel.Release))
                    .AddSyntaxTrees(syntaxTree)
                    .AddReferences(references);

                using var ms = new MemoryStream();
                var result = compilation.Emit(ms);

                if (!result.Success)
                {
                    var failures = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
                    foreach (var diagnostic in failures)
                    {
                        var lineInfo = diagnostic.Location.GetLineSpan().StartLinePosition;
                        var lineNumber = lineInfo.Line + 1;
                        var errorLine = lineNumber <= codeLines.Count
                            ? codeLines[lineNumber - 1].Trim()
                            : "无法获取对应代码行（行号超出范围）";
                        LoggerHelper.Error($"{diagnostic.Id}: {diagnostic.GetMessage()}  [错误行号: {lineNumber}]  [错误代码行: {errorLine}]");
                    }
                    continue;
                }

                ms.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());

                var instances =
                    from type in assembly.GetTypes()
                    from iface in interfacesToImplement
                    where type.GetInterfaces().Any(i => i.Name == iface)
                    let instance = CreateInstance(type)
                    where instance != null
                    select new CustomValue<object>(name, instance);

                customClasses.AddRange(instances);
                LoggerHelper.Info($"Successfully loaded custom class: {name}");
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"Failed to load custom class from {filePath}: {ex.Message}");
            }
        }

        _shouldLoadCustomClasses = false;
        return customClasses;
    }

    /// <summary>
    /// 安全地创建类型实例
    /// </summary>
    private static object? CreateInstance(Type type)
    {
        try
        {
            return Activator.CreateInstance(type);
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"Failed to create instance of type {type.FullName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 文件变化事件处理
    /// </summary>
    private static void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        LoggerHelper.Info($"Custom class file changed: {e.FullPath} ({e.ChangeType})");
        _shouldLoadCustomClasses = true;
        _customClasses = null;
    }

    /// <summary>
    /// 获取自定义类实例（带缓存）
    /// </summary>
    /// <param name="directory">包含 .cs 文件的目录路径</param>
    /// <param name="interfacesToImplement">要实现的接口名称数组</param>
    /// <returns>自定义类实例的集合</returns>
    public static IEnumerable<CustomValue<object>> GetCustomClasses(string directory, string[] interfacesToImplement)
    {
        if (_customClasses == null || _shouldLoadCustomClasses)
        {
            _customClasses = LoadAndInstantiateCustomClasses(directory, interfacesToImplement);
        }
        else
        {
            foreach (var value in _customClasses)
            {
                LoggerHelper.Info($"Using cached custom class: {value.Name}");
            }
        }
        return _customClasses;
    }

    /// <summary>
    /// 强制重新加载自定义类
    /// </summary>
    public static void ForceReload()
    {
        _shouldLoadCustomClasses = true;
        _customClasses = null;
        LoggerHelper.Info("Custom classes will be reloaded on next access");
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    public static void Dispose()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Deleted -= OnFileChanged;
            _watcher.Renamed -= OnFileChanged;
            _watcher.Dispose();
            _watcher = null;
            LoggerHelper.Info("File watcher disposed");
        }
        _customClasses = null;
        _metadataReferences = null;
    }
}
