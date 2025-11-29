using Avalonia;
using Avalonia.Controls;
using MaaFramework.Binding;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

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

            // 清理同目录中与 libs 文件夹重复的动态库，防止新旧版本冲突
            CleanupDuplicateLibraries(baseDirectory, libsPath);

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
            //         Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", $"{libsPath}:{currentLdPath}");
            //         LoggerHelper.Info($"Added libs folder to LD_LIBRARY_PATH environment variable: {libsPath}");
            //     }
            //     else if (string.IsNullOrEmpty(currentLdPath))
            //     {
            //         Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libsPath);
            //         LoggerHelper.Info($"Set LD_LIBRARY_PATH environment variable to libs folder: {libsPath}");
            //     }
            // }
            // else if (OperatingSystem.IsMacOS())
            // {
            //     string? currentDyldPath = Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH");
            //     if (!string.IsNullOrEmpty(currentDyldPath) && !currentDyldPath.Contains(libsPath))
            //     {
            //         Environment.SetEnvironmentVariable("DYLD_LIBRARY_PATH", $"{libsPath}:{currentDyldPath}");
            //         LoggerHelper.Info($"Added libs folder to DYLD_LIBRARY_PATH environment variable: {libsPath}");
            //     }
            //     else if (string.IsNullOrEmpty(currentDyldPath))
            //     {
            //         Environment.SetEnvironmentVariable("DYLD_LIBRARY_PATH", libsPath);
            //         LoggerHelper.Info($"Set DYLD_LIBRARY_PATH environment variable to libs folder: {libsPath}");
            //     }
            // }

            // 缓存已加载的库路径，避免重复加载和重复日志
            var loadedLibraries = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

            // 方法1: 使用 DllImportResolver 为所有程序集设置解析器（主要方案）
            DllImportResolver resolver = (libraryName, assembly, searchPath) =>
            {
                try
                {
                    // 检查缓存，如果已经加载过，直接返回缓存的句柄
                    if (loadedLibraries.TryGetValue(libraryName, out IntPtr cachedHandle))
                    {
                        return cachedHandle;
                    }

                    // 智能检测：先检查系统标准路径是否存在该库
                    // 如果系统能找到，返回 IntPtr.Zero 让系统使用默认加载逻辑
                    // 如果系统找不到，才从 libs 文件夹加载
                    if (IsLibraryAvailableInSystem(libraryName))
                    {
                        // 系统能找到该库，让系统使用默认加载逻辑
                        return IntPtr.Zero;
                    }

                    string extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" :
                                      RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";

                    // 在 Linux/macOS 上，优先尝试 lib 前缀的变体（处理 uiohook -> libuiohook.so 等情况）
                    List<string> libraryNameVariants = new List<string>();

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        // Linux/macOS: 优先尝试 lib 前缀
                        if (!libraryName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
                        {
                            libraryNameVariants.Add($"lib{libraryName}"); // lib + 名称（优先）
                        }
                        libraryNameVariants.Add(libraryName); // 原始名称
                        if (libraryName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
                        {
                            libraryNameVariants.Add(libraryName.Substring(3)); // 移除 lib 前缀
                        }
                    }
                    else
                    {
                        // Windows: 按原始顺序
                        libraryNameVariants.Add(libraryName); // 原始名称
                        if (!libraryName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
                        {
                            libraryNameVariants.Add($"lib{libraryName}"); // lib + 名称
                        }
                        if (libraryName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
                        {
                            libraryNameVariants.Add(libraryName.Substring(3)); // 移除 lib 前缀
                        }
                    }

                    foreach (string variant in libraryNameVariants)
                    {
                        // 尝试直接使用库名称（可能已经包含扩展名）
                        string nativeLibPath = Path.Combine(libsPath, variant);
                        if (File.Exists(nativeLibPath))
                        {
                            IntPtr handle = NativeLibrary.Load(nativeLibPath);
                            if (handle != IntPtr.Zero)
                            {
                                loadedLibraries[libraryName] = handle;
                                return handle;
                            }
                        }

                        // 尝试添加扩展名
                        if (!variant.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                        {
                            nativeLibPath = Path.Combine(libsPath, $"{variant}{extension}");
                            if (File.Exists(nativeLibPath))
                            {
                                IntPtr handle = NativeLibrary.Load(nativeLibPath);
                                if (handle != IntPtr.Zero)
                                {
                                    loadedLibraries[libraryName] = handle;
                                    return handle;
                                }
                            }
                        }
                    }

                    // 精确匹配失败后，尝试查找包含目标名称的文件
                    string? containingFile = FindLibraryContainingName(libsPath, libraryName, extension);
                    if (containingFile != null)
                    {
                        IntPtr handle = NativeLibrary.Load(containingFile);
                        if (handle != IntPtr.Zero)
                        {
                            loadedLibraries[libraryName] = handle;
                            return handle;
                        }
                    }

                    // 如果找不到，返回 IntPtr.Zero 让系统使用默认的解析逻辑
                    return IntPtr.Zero;
                }
                catch
                {
                    // 静默失败，返回 IntPtr.Zero 让系统使用默认的解析逻辑
                    // 注意：这里不能使用 LoggerHelper，因为它可能触发原生库加载导致递归
                    return IntPtr.Zero;
                }
            };

            // 为所有已加载的程序集设置解析器
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
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
                    NativeLibrary.SetDllImportResolver(args.LoadedAssembly, resolver);
                }
                catch
                {
                    // 某些程序集可能无法设置解析器，静默忽略
                }
            };
        }
        catch (Exception ex)
        {
            try
            {
                LoggerHelper.Error($"SetupNativeLibraryResolver failed: {ex}");
            }
            catch
            {
                // LoggerHelper 可能还未初始化
            }
            throw; // 重新抛出异常，让上层处理
        }
    }

    /// <summary>
    /// 清理同目录中与 libs 文件夹重复的动态库文件，防止新旧版本冲突
    /// </summary>
    private static void CleanupDuplicateLibraries(string baseDirectory, string libsPath)
    {
        try
        {
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

    /// <summary>
    /// 检查库是否在系统标准路径中可用
    /// 如果系统能找到该库，返回 true；否则返回 false
    /// </summary>
    private static bool IsLibraryAvailableInSystem(string libraryName)
    {
        try
        {
            string extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" :
                              RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";

            // 生成库名称变体
            List<string> libraryNameVariants = new List<string>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (!libraryName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
                {
                    libraryNameVariants.Add($"lib{libraryName}");
                }
                libraryNameVariants.Add(libraryName);
                if (libraryName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
                {
                    libraryNameVariants.Add(libraryName.Substring(3));
                }
            }
            else
            {
                libraryNameVariants.Add(libraryName);
                if (!libraryName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
                {
                    libraryNameVariants.Add($"lib{libraryName}");
                }
            }

            // 系统标准路径列表
            List<string> systemPaths = new List<string>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux 标准库路径
                systemPaths.AddRange(new[]
                {
                    "/usr/lib",
                    "/usr/lib/x86_64-linux-gnu",
                    "/usr/lib64",
                    "/usr/local/lib",
                    "/lib",
                    "/lib/x86_64-linux-gnu",
                    "/lib64"
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS 标准库路径
                systemPaths.AddRange(new[]
                {
                    "/usr/lib",
                    "/usr/local/lib",
                    "/opt/homebrew/lib",
                    "/opt/local/lib"
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows 系统路径
                string? systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
                if (!string.IsNullOrEmpty(systemRoot))
                {
                    systemPaths.Add(Path.Combine(systemRoot, "System32"));
                    systemPaths.Add(Path.Combine(systemRoot, "SysWOW64"));
                }
                systemPaths.Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
            }

            // 检查每个变体在每个系统路径中是否存在
            foreach (string variant in libraryNameVariants)
            {
                foreach (string systemPath in systemPaths)
                {
                    if (!Directory.Exists(systemPath))
                        continue;

                    // 检查直接文件名
                    string fullPath = Path.Combine(systemPath, variant);
                    if (File.Exists(fullPath))
                        return true;

                    // 检查带扩展名的文件名
                    if (!variant.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        fullPath = Path.Combine(systemPath, $"{variant}{extension}");
                        if (File.Exists(fullPath))
                            return true;
                    }

                    // 在 Linux 上，还要检查带版本号的 .so 文件（如 libX11.so.6）
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        try
                        {
                            string baseName = variant;
                            // 如果 variant 已经包含 .so，提取基础名称
                            if (baseName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                            {
                                baseName = Path.GetFileNameWithoutExtension(baseName);
                            }
                            // 如果 variant 不包含扩展名，直接使用

                            var dirInfo = new DirectoryInfo(systemPath);
                            var matchingFiles = dirInfo.GetFiles($"{baseName}.so*");
                            if (matchingFiles.Length > 0)
                                return true;
                        }
                        catch
                        {
                            // 忽略文件系统错误
                        }
                    }
                }
            }

            return false;
        }
        catch
        {
            // 发生任何错误，保守地返回 false，让程序尝试从 libs 加载
            return false;
        }
    }

    /// <summary>
    /// 在 libs 文件夹中查找包含目标名称的库文件（精确匹配失败后的备选方案）
    /// 使用安全的文件枚举方式，避免在 Linux 上出现问题
    /// </summary>
    private static string? FindLibraryContainingName(string libsPath, string libraryName, string extension)
    {
        if (!Directory.Exists(libsPath))
        {
            return null;
        }

        try
        {
            // 规范化目标名称（转小写，移除扩展名和 lib 前缀用于匹配）
            string normalizedTarget = libraryName.ToLowerInvariant();
            if (normalizedTarget.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                normalizedTarget = normalizedTarget.Substring(0, normalizedTarget.Length - extension.Length);
            }
            string targetWithoutLib = normalizedTarget.StartsWith("lib")
                ? normalizedTarget.Substring(3)
                : normalizedTarget;

            string? bestMatch = null;
            int bestScore = 0;

            // 使用 DirectoryInfo 和简单的文件枚举，避免 Directory.GetFiles 可能的问题
            DirectoryInfo dirInfo = new DirectoryInfo(libsPath);
            FileInfo[] files = dirInfo.GetFiles();

            foreach (FileInfo fileInfo in files)
            {
                string fileName = fileInfo.Name;
                string extensionLower = fileInfo.Extension.ToLowerInvariant();

                // 只检查库文件
                if (extensionLower != ".dll" && extensionLower != ".so" && extensionLower != ".dylib")
                {
                    continue;
                }

                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string normalizedFile = fileNameWithoutExt.ToLowerInvariant();
                string fileWithoutLib = normalizedFile.StartsWith("lib")
                    ? normalizedFile.Substring(3)
                    : normalizedFile;

                int score = 0;

                // 去掉 lib 前缀后完全匹配（最高优先级）
                if (fileWithoutLib.Equals(targetWithoutLib, StringComparison.OrdinalIgnoreCase))
                {
                    score = 900;
                }
                // 文件名包含目标名称（去掉 lib 前缀）
                else if (fileWithoutLib.Contains(targetWithoutLib, StringComparison.OrdinalIgnoreCase))
                {
                    score = 700;
                }
                // 目标名称包含文件名（去掉 lib 前缀）
                else if (targetWithoutLib.Contains(fileWithoutLib, StringComparison.OrdinalIgnoreCase))
                {
                    score = 600;
                }
                // 文件名包含目标名称（完整）
                else if (normalizedFile.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    score = 500;
                }

                // 在 Linux/macOS 上，有 lib 前缀的文件加分
                if ((RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) &&
                    normalizedFile.StartsWith("lib"))
                {
                    score += 50;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = fileInfo.FullName;
                }
            }

            return bestMatch;
        }
        catch
        {
            // 静默失败，返回 null
            return null;
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            // 设置原生库解析器，从 libs 文件夹加载原生库（.so/.dylib/.dll）
            SetupNativeLibraryResolver();

            List<string> resultDirectories = new List<string>();

            // 获取应用程序基目录
            string baseDirectory = AppContext.BaseDirectory;

            // MaaFW 的原生库搜索路径设置（保留 MaaFW 的代码，不做修改）
            string libsPath = Path.Combine(baseDirectory, "libs");
            if (Directory.Exists(libsPath))
            {
                NativeBindingContext.AppendNativeLibrarySearchPaths(new[] { libsPath });
            }

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
            // 确保异常信息输出到控制台
            Console.Error.WriteLine($"[FATAL ERROR] Unhandled exception: {e}");
            Console.Error.WriteLine($"[FATAL ERROR] Stack trace: {e.StackTrace}");
            if (e.InnerException != null)
            {
                Console.Error.WriteLine($"[FATAL ERROR] Inner exception: {e.InnerException}");
            }

            // 尝试使用 LoggerHelper（如果已初始化）
            try
            {
                LoggerHelper.Error($"总异常捕获：{e}");
            }
            catch { }

            Environment.ExitCode = 1;
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
