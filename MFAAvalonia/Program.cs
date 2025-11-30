using Avalonia;
using Avalonia.Controls;
using MaaFramework.Binding;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using System.Text;

namespace MFAAvalonia;

sealed class Program
{
    public static Dictionary<string, string> ParseArguments(string[] args)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            // 识别以 "-" 或 "--" 开头的键
            if (args[i].StartsWith("-"))
            {
                string key = args[i].TrimStart('-').ToLower();
                // 检查下一个元素是否为值（非键）
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    parameters[key] = args[i + 1];
                    i++; // 跳过已处理的值
                }
                else
                {
                    parameters[key] = ""; // 标记无值的键
                }
            }
        }
        return parameters;
    }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    public static Dictionary<string, string> Args { get; private set; } = new();
    private static Mutex? _mutex;
    public static bool IsNewInstance = true;
    public static void ReleaseMutex()
    {
        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Close();
            _mutex = null;
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
        }
    }


    // 缓存已加载的库句柄，避免重复加载（静态字段，供预加载和解析器共用）
    private static readonly Dictionary<string, IntPtr> _loadedLibraries = new(StringComparer.OrdinalIgnoreCase);
    private static string? _libsPath;

    /// <summary>
    /// 设置原生库解析器，从 libs 文件夹加载原生库（.so/.dylib/.dll 原生库）
    /// </summary>
    private static void SetupNativeLibraryResolver()
    {
        try
        {
            string baseDirectory = AppContext.BaseDirectory;
            _libsPath = Path.Combine(baseDirectory, "libs");

            if (!Directory.Exists(_libsPath))
            {
                try
                {
                    LoggerHelper.Info($"libs folder does not exist, skipping native library resolver setup: {_libsPath}");
                }
                catch
                {
                    // LoggerHelper 可能还未初始化
                }
                return;
            }

            // 使用 DllImportResolver 为所有程序集设置解析器
            DllImportResolver resolver = (libraryName, assembly, searchPath) =>
            {
                try
                {
                    // 检查缓存，如果已经加载过，直接返回缓存的句柄
                    if (_loadedLibraries.TryGetValue(libraryName, out IntPtr cachedHandle))
                    {
                        return cachedHandle;
                    }

                    // 直接在 libs 文件夹中查找库文件
                    string? libraryPath = FindLibraryInLibs(_libsPath, libraryName);
                    if (libraryPath != null)
                    {
                        IntPtr handle = NativeLibrary.Load(libraryPath);

                        // 缓存加载的句柄
                        _loadedLibraries[libraryName] = handle;
                        
                        try
                        {
                            LoggerHelper.Info($"Loaded native library from libs: {libraryPath}");
                        }
                        catch { }
                        
                        return handle;
                    }

                    // 在 libs 中找不到，返回 IntPtr.Zero 让系统使用默认的解析逻辑
                    return IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    // 记录错误但不中断，返回 IntPtr.Zero 让系统使用默认的解析逻辑
                    try
                    {
                        LoggerHelper.Warning($"Failed to load native library '{libraryName}': {ex.Message}");
                    }
                    catch { }
                    return IntPtr.Zero;
                }
            };

            // 为当前已加载的所有程序集设置解析器
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    // 跳过会自己设置 DllImportResolver 的程序集（如 SoundFlow）
                    if (ShouldSkipAssembly(assembly))
                        continue;
                    
                    NativeLibrary.SetDllImportResolver(assembly, resolver);
                }
                catch
                {
                    // 某些程序集可能无法设置解析器，静默忽略
                }
            }

            // 监听程序集加载事件，为新加载的程序集自动设置解析器
            AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
            {
                try
                {
                    // 跳过会自己设置 DllImportResolver 的程序集（如 SoundFlow）
                    if (ShouldSkipAssembly(args.LoadedAssembly))
                        return;
                    
                    NativeLibrary.SetDllImportResolver(args.LoadedAssembly, resolver);
                }
                catch
                {
                    // 某些程序集可能无法设置解析器，静默忽略
                }
            };

        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// 判断是否应该跳过为该程序集设置 DllImportResolver
    /// 某些库（如 SoundFlow）会在自己的静态构造函数中设置解析器，
    /// 如果我们先设置了，它们再设置时会抛出 InvalidOperationException
    /// </summary>
    private static bool ShouldSkipAssembly(Assembly assembly)
    {
        try
        {
            var assemblyName = assembly.GetName().Name;
            if (string.IsNullOrEmpty(assemblyName))
                return false;

            // 跳过 SoundFlow 相关程序集，它们会自己设置 DllImportResolver
            if (assemblyName.StartsWith("SoundFlow", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 在 libs 文件夹中查找库文件
    /// </summary>
    private static string? FindLibraryInLibs(string libsPath, string libraryName)
    {
        try
        {
            if (!Directory.Exists(libsPath) || string.IsNullOrEmpty(libraryName))
                return null;

            // 定义平台特定的扩展名
            string[] extensions = OperatingSystem.IsWindows()
                ? new[] { ".dll" }
                : OperatingSystem.IsLinux()
                    ? new[] { ".so" }
                    : OperatingSystem.IsMacOS()
                        ? new[] { ".dylib", ".so" }
                        : new[] { ".dll", ".so", ".dylib" };

            // 定义平台特定的前缀（Linux/macOS 上的库通常以 "lib" 开头）
            string[] prefixes = OperatingSystem.IsWindows()
                ? new[] { "" }
                : new[] { "", "lib" };

            // 首先尝试直接匹配（libraryName 可能已经包含扩展名）
            string directPath = Path.Combine(libsPath, libraryName);
            if (File.Exists(directPath))
            {
                return directPath;
            }

            // 获取不带扩展名的库名
            string nameWithoutExt = Path.GetFileNameWithoutExtension(libraryName);
            
            // 如果库名以 "lib" 开头，也准备一个不带 "lib" 前缀的版本
            string nameWithoutLibPrefix = nameWithoutExt.StartsWith("lib", StringComparison.OrdinalIgnoreCase)
                ? nameWithoutExt.Substring(3)
                : nameWithoutExt;

            // 尝试所有前缀和扩展名的组合
            foreach (string prefix in prefixes)
            {
                foreach (string ext in extensions)
                {
                    // 尝试原始名称
                    string pathWithExt = Path.Combine(libsPath, prefix + libraryName + ext);
                    if (File.Exists(pathWithExt))
                    {
                        return pathWithExt;
                    }

                    // 尝试不带扩展名的名称
                    if (nameWithoutExt != libraryName)
                    {
                        string path = Path.Combine(libsPath, prefix + nameWithoutExt + ext);
                        if (File.Exists(path))
                        {
                            return path;
                        }
                    }

                    // 尝试不带 "lib" 前缀的名称（如果原名以 lib 开头）
                    if (nameWithoutLibPrefix != nameWithoutExt)
                    {
                        string path = Path.Combine(libsPath, prefix + nameWithoutLibPrefix + ext);
                        if (File.Exists(path))
                        {
                            return path;
                        }
                    }
                }
            }

            // 最后尝试模糊匹配：在 libs 目录中查找包含库名的文件
            // 这对于版本化的库文件很有用，如 libfoo.so.1.2.3
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    var files = Directory.GetFiles(libsPath);
                    foreach (var file in files)
                    {
                        string fileName = Path.GetFileName(file);
                        // 检查文件名是否包含库名（不区分大小写）
                        if (fileName.Contains(nameWithoutExt, StringComparison.OrdinalIgnoreCase) ||
                            fileName.Contains(nameWithoutLibPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            // 确保是共享库文件
                            if (fileName.Contains(".so") || fileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
                            {
                                return file;
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略目录枚举错误
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 清理同目录中与 libs 文件夹和 runtimes 文件夹重复的动态库文件，防止新旧版本冲突
    /// </summary>
    private static void CleanupDuplicateLibraries(string baseDirectory, string? lib)
    {
        try
        {
            // 收集所有需要排除的库文件名（来自 libs 和 runtimes）
            var duplicateFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. 收集 libs 文件夹中的所有动态库文件
            lib ??= "libs";
            var libsPath = Path.Combine(baseDirectory, lib);
            if (Directory.Exists(libsPath))
            {
                var libsDirectoryInfo = new DirectoryInfo(libsPath);
                var libsFileInfos = libsDirectoryInfo.GetFiles()
                    .Where(f => IsNativeLibrary(f.Extension));

                foreach (var fileInfo in libsFileInfos)
                {
                    duplicateFiles.Add(fileInfo.Name);
                }
            }

            // 2. 收集 runtimes 文件夹及其子目录中的所有动态库文件
            var runtimesPath = Path.Combine(baseDirectory, "runtimes");
            if (Directory.Exists(runtimesPath))
            {
                try
                {
                    // 递归搜索 runtimes 目录下的所有动态库文件
                    var runtimeFiles = Directory.EnumerateFiles(runtimesPath, "*", SearchOption.AllDirectories)
                        .Where(f => IsNativeLibrary(Path.GetExtension(f)));

                    foreach (var filePath in runtimeFiles)
                    {
                        var fileName = Path.GetFileName(filePath);
                        duplicateFiles.Add(fileName);
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        LoggerHelper.Warning($"Failed to enumerate runtimes folder: {ex.Message}");
                    }
                    catch { }
                }
            }

            if (duplicateFiles.Count == 0)
                return;

            // 3. 检查并删除同目录中的重复文件
            var baseDirectoryInfo = new DirectoryInfo(baseDirectory);
            var baseFileInfos = baseDirectoryInfo.GetFiles()
                .Where(f => IsNativeLibrary(f.Extension));

            foreach (var fileInfo in baseFileInfos)
            {
                // 如果同目录中的文件在 libs 或 runtimes 中也存在，删除同目录中的文件
                if (duplicateFiles.Contains(fileInfo.Name))
                {
                    try
                    {
                        fileInfo.Delete();
                        try
                        {
                            LoggerHelper.Info($"Deleted duplicate library file: {fileInfo.Name} (found in libs or runtimes folder)");
                        }
                        catch
                        {
                            // LoggerHelper 可能还未初始化
                        }
                    }
                    catch (Exception ex)
                    {
                        // 删除失败，记录错误但不中断程序
                        try
                        {
                            LoggerHelper.Warning($"Failed to delete duplicate library file {fileInfo.Name}: {ex.Message}");
                        }
                        catch
                        {
                            // LoggerHelper 可能还未初始化
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 清理失败，记录错误但不中断程序
            try
            {
                LoggerHelper.Warning($"Failed to cleanup duplicate libraries: {ex.Message}");
            }
            catch
            {
                // LoggerHelper 可能还未初始化
            }
        }
    }

    /// <summary>
    /// 判断文件扩展名是否为原生库文件
    /// </summary>
    private static bool IsNativeLibrary(string extension)
    {
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".so", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase);
    }


    [STAThread]
    public static void Main(string[] args)
    {

        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            CleanupDuplicateLibraries(AppContext.BaseDirectory, AppContext.GetData("SubdirectoriesToProbe") as string);

            SetupNativeLibraryResolver();

            List<string> resultDirectories = new List<string>();

            // 获取应用程序基目录
            string baseDirectory = AppContext.BaseDirectory;

            // 构建runtimes文件夹路径
            string runtimesPath = Path.Combine(baseDirectory, "runtimes");

            // 检查runtimes文件夹是否存在
            if (!Directory.Exists(runtimesPath))
            {
                try
                {
                    LoggerHelper.Warning("runtimes文件夹不存在");
                }
                catch { }
            }
            else
            {
                // 搜索runtimes文件夹及其子目录中所有名为"MaaFramework"的文件（不限扩展名）
                var maaFiles = Directory.EnumerateFiles(
                    runtimesPath,
                    "*MaaFramework*",
                    SearchOption.AllDirectories
                );

                foreach (var filePath in maaFiles)
                {
                    var fileDirectory = Path.GetDirectoryName(filePath);
                    if (!resultDirectories.Contains(fileDirectory) && fileDirectory?.Contains(VersionChecker.GetNormalizedArchitecture()) == true)
                    {
                        resultDirectories.Add(fileDirectory);
                    }
                }
                try
                {
                    LoggerHelper.Info("MaaFramework runtimes: " + JsonConvert.SerializeObject(resultDirectories, Formatting.Indented));
                }
                catch { }
                NativeBindingContext.AppendNativeLibrarySearchPaths(resultDirectories);
            }

            var parsedArgs = ParseArguments(args);
            var mutexName = "MFA_"
                + Directory.GetCurrentDirectory()
                    .Replace("\\", "_")
                    .Replace("/", "_")
                    .Replace(":", string.Empty);
            _mutex = new Mutex(true, mutexName, out IsNewInstance);

            try
            {
                LoggerHelper.Info("Args: " + JsonConvert.SerializeObject(parsedArgs, Formatting.Indented));
                LoggerHelper.Info("MFA version: " + RootViewModel.Version);
                LoggerHelper.Info(".NET version: " + RuntimeInformation.FrameworkDescription);
            }
            catch { }
            Args = parsedArgs;

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        }
        catch (Exception e)
        {

            try
            {
                LoggerHelper.Error($"总异常捕获：{e}");
            }
            catch { }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
