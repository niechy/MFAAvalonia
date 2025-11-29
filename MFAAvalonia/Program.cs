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


    /// <summary>
    /// 设置原生库解析器，从 libs 文件夹加载原生库（.so/.dylib/.dll 原生库）
    /// </summary>
    private static void SetupNativeLibraryResolver()
    {
        try
        {
            string baseDirectory = AppContext.BaseDirectory;
            string libsPath = Path.Combine(baseDirectory, "libs");

            if (!Directory.Exists(libsPath))
            {
                try
                {
                    LoggerHelper.Info($"libs folder does not exist, skipping native library resolver setup: {libsPath}");
                }
                catch
                {
                    // LoggerHelper 可能还未初始化
                }
                return;
            }

            // // 方法2: 使用环境变量设置库搜索路径（备选方案，必须在程序启动早期设置）
            // if (OperatingSystem.IsWindows())
            // {
            //     string? currentPath = Environment.GetEnvironmentVariable("PATH");
            //     if (!string.IsNullOrEmpty(currentPath) && !currentPath.Contains(libsPath, StringComparison.OrdinalIgnoreCase))
            //     {
            //         Environment.SetEnvironmentVariable("PATH", $"{libsPath};{currentPath}");
            //         LoggerHelper.Info($"Added libs folder to PATH environment variable: {libsPath}");
            //     }
            //     else if (string.IsNullOrEmpty(currentPath))
            //     {
            //         Environment.SetEnvironmentVariable("PATH", libsPath);
            //         LoggerHelper.Info($"Set PATH environment variable to libs folder: {libsPath}");
            //     }
            // }
            // else if (OperatingSystem.IsLinux())
            // {
            //     string? currentLdPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            //     if (!string.IsNullOrEmpty(currentLdPath) && !currentLdPath.Contains(libsPath))
            //     {
            //         Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", $"{libsPath};{currentLdPath}");
            //         try
            //         {
            //             LoggerHelper.Info($"Added libs folder to LD_LIBRARY_PATH environment variable: {libsPath}");
            //         }
            //         catch { }
            //     }
            //     else if (string.IsNullOrEmpty(currentLdPath))
            //     {
            //         Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libsPath);
            //         try
            //         {
            //             LoggerHelper.Info($"Set LD_LIBRARY_PATH environment variable to libs folder: {libsPath}");
            //         }
            //         catch { }
            //     }
            // }
            // else if (OperatingSystem.IsMacOS())
            // {
            //     string? currentDyldPath = Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH");
            //     if (!string.IsNullOrEmpty(currentDyldPath) && !currentDyldPath.Contains(libsPath))
            //     {
            //         Environment.SetEnvironmentVariable("DYLD_LIBRARY_PATH", $"{libsPath}:{currentDyldPath}");
            //         try
            //         {
            //             LoggerHelper.Info($"Added libs folder to DYLD_LIBRARY_PATH environment variable: {libsPath}");
            //         }
            //         catch { }
            //     }
            //     else if (string.IsNullOrEmpty(currentDyldPath))
            //     {
            //         Environment.SetEnvironmentVariable("DYLD_LIBRARY_PATH", libsPath);
            //         try
            //         {
            //             LoggerHelper.Info($"Set DYLD_LIBRARY_PATH environment variable to libs folder: {libsPath}");
            //         }
            //         catch { }
            //     }
            // }

            // 方法1: 使用 DllImportResolver 来实际加载库（主要方案，跨平台）
            // 在 Linux/macOS 上，仅设置环境变量可能不够，需要 DllImportResolver 来实际加载
            
            // 缓存已加载的库句柄，避免重复加载
            var loadedLibraries = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

            // 使用 DllImportResolver 为所有程序集设置解析器
            DllImportResolver resolver = (libraryName, assembly, searchPath) =>
            {
                try
                {
                    // 检查缓存，如果已经加载过，直接返回缓存的句柄
                    if (loadedLibraries.TryGetValue(libraryName, out IntPtr cachedHandle))
                    {
                        return cachedHandle;
                    }

                    // 直接在 libs 文件夹中查找库文件
                    string? libraryPath = FindLibraryInLibs(libsPath, libraryName);
                    if (libraryPath != null)
                    {
                        IntPtr handle = NativeLibrary.Load(
                            libraryPath,
                            assembly,
                            DllImportSearchPath.UseDllDirectoryForDependencies
                        );

                        // 缓存加载的句柄
                        loadedLibraries[libraryName] = handle;
                        return handle;
                    }

                    // 在 libs 中找不到，返回 IntPtr.Zero 让系统使用默认的解析逻辑
                    return IntPtr.Zero;
                }
                catch
                {
                    // 静默失败，返回 IntPtr.Zero 让系统使用默认的解析逻辑
                    return IntPtr.Zero;
                }
            };


            // 监听程序集加载事件，为新加载的程序集自动设置解析器
            AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
            {
                try
                {
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

            // 首先尝试直接匹配（libraryName 可能已经包含扩展名）
            string directPath = Path.Combine(libsPath, libraryName);
            if (File.Exists(directPath))
            {
                return directPath;
            }

            // 尝试添加平台特定的扩展名
            foreach (string ext in extensions)
            {
                string pathWithExt = Path.Combine(libsPath, libraryName + ext);
                if (File.Exists(pathWithExt))
                {
                    return pathWithExt;
                }
            }

            // 如果 libraryName 包含扩展名，尝试去掉扩展名后再匹配
            string nameWithoutExt = Path.GetFileNameWithoutExtension(libraryName);
            if (nameWithoutExt != libraryName)
            {
                foreach (string ext in extensions)
                {
                    string path = Path.Combine(libsPath, nameWithoutExt + ext);
                    if (File.Exists(path))
                    {
                        return path;
                    }
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
    /// 清理同目录中与 libs 文件夹重复的动态库文件，防止新旧版本冲突
    /// </summary>
    private static void CleanupDuplicateLibraries(string baseDirectory, string? lib)
    {
        try
        {
            lib ??= "libs";
            var libsPath = Path.Combine(baseDirectory, lib);
            if (!Directory.Exists(libsPath))
                return;

            // 获取 libs 文件夹中的所有动态库文件
            var libsFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var libsDirectoryInfo = new DirectoryInfo(libsPath);
            var libsFileInfos = libsDirectoryInfo.GetFiles()
                .Where(f => f.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                           f.Extension.Equals(".so", StringComparison.OrdinalIgnoreCase) ||
                           f.Extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase));

            foreach (var fileInfo in libsFileInfos)
            {
                // 使用文件名（不包含路径）作为键，支持跨平台比较
                libsFiles.Add(fileInfo.Name);
            }

            if (libsFiles.Count == 0)
                return;

            // 检查同目录中的文件
            var baseDirectoryInfo = new DirectoryInfo(baseDirectory);
            var baseFileInfos = baseDirectoryInfo.GetFiles()
                .Where(f => f.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                           f.Extension.Equals(".so", StringComparison.OrdinalIgnoreCase) ||
                           f.Extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase));

            foreach (var fileInfo in baseFileInfos)
            {
                // 如果同目录中的文件在 libs 中也存在，删除同目录中的文件
                if (libsFiles.Contains(fileInfo.Name))
                {
                    try
                    {
                        fileInfo.Delete();
                        try
                        {
                            LoggerHelper.Info($"Deleted duplicate library file: {fileInfo.Name} (found in libs folder)");
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
