using Avalonia;
using Avalonia.Controls;
using MaaFramework.Binding;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
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

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            List<string> resultDirectories = new List<string>();

            // 获取应用程序基目录
            string baseDirectory = AppContext.BaseDirectory;
            // 构建runtimes文件夹路径
            string runtimesPath = Path.Combine(baseDirectory, "runtimes");

            // 检查runtimes文件夹是否存在
            if (!Directory.Exists(runtimesPath))
            {
                LoggerHelper.Warning("runtimes文件夹不存在");
            }
            else
            {
                // 搜索runtimes文件夹及其子目录中所有名为"MaaFramework"的文件（不限扩展名）
                // 搜索模式说明："MaaFramework.*" 匹配所有以MaaFramework为文件名的文件
                var maaFiles = Directory.EnumerateFiles(
                    runtimesPath,
                    "MaaFramework.*", // 文件名固定为MaaFramework，扩展名任意
                    SearchOption.AllDirectories // 包括所有子目录
                );

                foreach (var filePath in maaFiles)
                {
                    // 获取文件所在的目录
                    var fileDirectory = Path.GetDirectoryName(filePath);

                    // 避免重复添加相同目录（可选，根据需求决定是否保留）
                    if (!resultDirectories.Contains(fileDirectory))
                    {
                        resultDirectories.Add(fileDirectory);
                    }
                }
                LoggerHelper.Info("MaaFramework runtimes: " + JsonConvert.SerializeObject(resultDirectories, Formatting.Indented));
                NativeBindingContext.AppendNativeLibrarySearchPaths(resultDirectories);
            }


            var parsedArgs = ParseArguments(args);
            // Fix: Replace both Windows (\) and Unix (/) path separators for cross-platform compatibility
            var mutexName = "MFA_"
                + Directory.GetCurrentDirectory()
                    .Replace("\\", "_")
                    .Replace("/", "_")
                    .Replace(":", string.Empty);
            _mutex = new Mutex(true, mutexName, out IsNewInstance);
            LoggerHelper.Info("Args: " + JsonConvert.SerializeObject(parsedArgs, Formatting.Indented));
            LoggerHelper.Info("MFA version: " + RootViewModel.Version);
            LoggerHelper.Info(".NET version: " + RuntimeInformation.FrameworkDescription);
            Args = parsedArgs;

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"总异常捕获：{e}");
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
