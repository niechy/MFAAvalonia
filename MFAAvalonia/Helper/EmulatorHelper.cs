using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper;

public static class EmulatorHelper
{
    [DllImport("User32.dll", EntryPoint = "FindWindow")]
    [SupportedOSPlatform("windows")]
    private extern static IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("User32.dll", CharSet = CharSet.Auto)]
    [SupportedOSPlatform("windows")]
    private extern static int GetWindowThreadProcessId(IntPtr hwnd, out int id);

    public static bool KillEmulatorModeSwitcher()
    {
        try
        {
            string emulatorMode = "None";
            var windowName = MaaProcessor.Config.AdbDevice.Name;
            if (windowName.Contains("MuMuPlayer12"))
                emulatorMode = "MuMuEmulator12";
            else if (windowName.Contains("Nox"))
                emulatorMode = "Nox";
            else if (windowName.Contains("LDPlayer"))
                emulatorMode = "LDPlayer";
            else if (windowName.Contains("XYAZ"))
                emulatorMode = "XYAZ";
            else if (windowName.Contains("BlueStacks"))
                emulatorMode = "BlueStacks";

            LoggerHelper.Info("Emulator mode detected: " + emulatorMode);

            return emulatorMode switch
            {
                "Nox" => KillEmulatorNox(),
                "LDPlayer" => KillEmulatorLdPlayer(),
                "XYAZ" => KillEmulatorXyaz(),
                "BlueStacks" => KillEmulatorBlueStacks(),
                "MuMuEmulator12" => KillEmulatorMuMuEmulator12(),
                _ => KillEmulatorByWindow(),
            };
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Failed to close emulator: " + e.Message);
            return false;
        }
    }
    /// <summary>
    /// 一个用于调用 MuMu12 模拟器控制台关闭 MuMu12 的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    /// <summary>
    /// 一个用于调用 MuMu12 模拟器控制台关闭 MuMu12 的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    private static bool KillEmulatorMuMuEmulator12()
    {
        string address = MaaProcessor.Config.AdbDevice.AdbSerial;
        int emuIndex;
        if (address == "127.0.0.1:16384")
        {
            emuIndex = 0;
        }
        else if (address.Contains(':'))
        {
            string portStr = address.Split(':')[1];
            if (int.TryParse(portStr, out var port))
            {
                emuIndex = (port - 16384) / 32;
            }
            else
            {
                LoggerHelper.Error($"Failed to parse port from address {address}");
                return false;
            }
        }
        else if (address.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = address.Split('-');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int port))
            {
                emuIndex = (port - 5554) / 2;
            }
            else
            {
                LoggerHelper.Error($"Failed to parse port from emulator style address {address}");
                return false;
            }
        }
        else
        {
            LoggerHelper.Error($"Unsupported address format: {address}");
            return false;
        }
// 尝试找到正在运行的模拟器进程（优先新版，再兼容旧版）
        Process[] processes = Process.GetProcessesByName("MuMuNxDevice"); // 新版
        if (processes.Length == 0)
        {
            processes = Process.GetProcessesByName("MuMuPlayer"); // 旧版
        }
        if (processes.Length == 0)
        {
            return false;
        }
        ProcessModule? processModule;
        try
        {
            processModule = processes[0].MainModule;
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Failed to get the main module of the emulator process.");
            LoggerHelper.Error(e.Message);
            return false;
        }
        string? emulatorExePath = processModule?.FileName;
        if (emulatorExePath == null)
        {
            return false;
        }
// 从 exe 路径回推安装目录
// 新版路径推导: nx_device\12.0\shell\MuMuNxDevice.exe → 上三级目录 = 安装目录
// 旧版路径推导: shell\MuMuPlayer.exe → 上一级目录 = 安装目录
        var installPath = Path.GetFullPath(Path.GetFileName(emulatorExePath).Equals("MuMuNxDevice.exe", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Path.GetDirectoryName(emulatorExePath)!, @"..\..\..")
            : Path.Combine(Path.GetDirectoryName(emulatorExePath)!, ".."));
// 新旧路径分别尝试 MuMuManager.exe
        string newConsolePath = Path.Combine(installPath, @"nx_main\MuMuManager.exe");
        string oldConsolePath = Path.Combine(installPath, @"shell\MuMuManager.exe");
        string? consolePath = null;
        LoggerHelper.Info("New console path: " + newConsolePath);
        LoggerHelper.Info("Old console path: " + oldConsolePath);
        if (File.Exists(newConsolePath))
        {
            consolePath = newConsolePath;
        }
        else if (File.Exists(oldConsolePath))
        {
            consolePath = oldConsolePath;
        }
        if (consolePath != null)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(consolePath)
            {
                Arguments = $"api -v {emuIndex} shutdown_player",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            var process = Process.Start(startInfo);
            if (process != null && process.WaitForExit(5000))
            {
                LoggerHelper.Info($"Emulator at index {emuIndex} closed through console. Console path: {consolePath}");
                return true;
            }
            LoggerHelper.Warning($"Console process at index {emuIndex} did not exit within the specified timeout. Killing emulator by window. Console path: {consolePath}");
            return KillEmulatorByWindow();
        }
        LoggerHelper.Error("MuMuManager.exe not found in expected locations (new or old). Trying to kill emulator by window.");
        return KillEmulatorByWindow();
    }

    /// <summary>
    /// 一个用于调用雷电模拟器控制台关闭雷电模拟器的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    private static bool KillEmulatorLdPlayer()
    {
        var address = MaaProcessor.Config.AdbDevice.AdbSerial;
        int emuIndex;
        if (address.Contains(':'))
        {
            var portStr = address.Split(':')[1];
            var port = int.Parse(portStr);
            emuIndex = (port - 5555) / 2;
        }
        else
        {
            var portStr = address.Split('-')[1];
            var port = int.Parse(portStr);
            emuIndex = (port - 5554) / 2;
        }

        var processes = Process.GetProcessesByName("dnplayer");
        if (processes.Length <= 0)
        {
            return false;
        }

        ProcessModule processModule;
        try
        {
            processModule = processes[0].MainModule;
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Error: Failed to get the main module of the emulator process.");
            LoggerHelper.Error(e.Message);
            return false;
        }

        if (processModule == null)
        {
            return false;
        }


        var emuLocation = Path.GetDirectoryName(processModule.FileName);
        if (emuLocation == null)
        {
            return false;
        }

        var consolePath = Path.Combine(emuLocation, "ldconsole.exe");

        if (File.Exists(consolePath))
        {
            var startInfo = new ProcessStartInfo(consolePath)
            {
                Arguments = $"quit --index {emuIndex}",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            var process = Process.Start(startInfo);
            if (process != null && process.WaitForExit(5000))
            {
                LoggerHelper.Info($"Emulator at index {emuIndex} closed through console. Console path: {consolePath}");
                return true;
            }

            LoggerHelper.Warning($"Console process at index {emuIndex} did not exit within the specified timeout. Killing emulator by window. Console path: {consolePath}");
            return KillEmulatorByWindow();
        }

        LoggerHelper.Info($"Error: `{consolePath}` not found, try to kill emulator by window.");
        return KillEmulatorByWindow();
    }

    /// <summary>
    /// 一个用于调用夜神模拟器控制台关闭夜神模拟器的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    private static bool KillEmulatorNox()
    {
        string address = MaaProcessor.Config.AdbDevice.AdbSerial;
        int emuIndex;
        if (address == "127.0.0.1:62001")
        {
            emuIndex = 0;
        }
        else
        {
            string portStr = address.Split(':')[1];
            int port = int.Parse(portStr);
            emuIndex = port - 62024;
        }

        var processes = Process.GetProcessesByName("Nox");
        if (processes.Length <= 0)
        {
            return false;
        }

        ProcessModule processModule;
        try
        {
            processModule = processes[0].MainModule;
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Error: Failed to get the main module of the emulator process.");
            LoggerHelper.Error(e.Message);
            return false;
        }

        if (processModule == null)
        {
            return false;
        }

        var emuLocation = Path.GetDirectoryName(processModule.FileName);
        if (emuLocation == null)
        {
            return false;
        }

        var consolePath = Path.Combine(emuLocation, "NoxConsole.exe");

        if (File.Exists(consolePath))
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(consolePath)
            {
                Arguments = $"quit -index:{emuIndex}",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            var process = Process.Start(startInfo);
            if (process != null && process.WaitForExit(5000))
            {
                LoggerHelper.Info($"Emulator at index {emuIndex} closed through console. Console path: {consolePath}");
                return true;
            }

            LoggerHelper.Warning($"Console process at index {emuIndex} did not exit within the specified timeout. Killing emulator by window. Console path: {consolePath}");
            return KillEmulatorByWindow();
        }

        LoggerHelper.Info($"Error: `{consolePath}` not found, try to kill emulator by window.");
        return KillEmulatorByWindow();
    }

    /// <summary>
    /// 一个用于调用逍遥模拟器控制台关闭逍遥模拟器的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    private static bool KillEmulatorXyaz()
    {
        string address = MaaProcessor.Config.AdbDevice.AdbSerial;
        string portStr = address.Split(':')[1];
        int port = int.Parse(portStr);
        var emuIndex = (port - 21503) / 10;

        Process[] processes = Process.GetProcessesByName("MEmu");
        if (processes.Length <= 0)
        {
            return false;
        }

        ProcessModule processModule;
        try
        {
            processModule = processes[0].MainModule;
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Error: Failed to get the main module of the emulator process.");
            LoggerHelper.Error(e.Message);
            return false;
        }

        if (processModule == null)
        {
            return false;
        }

        var emuLocation = Path.GetDirectoryName(processModule.FileName);
        if (emuLocation == null)
        {
            return false;
        }

        string consolePath = Path.Combine(emuLocation, "memuc.exe");

        if (File.Exists(consolePath))
        {
            var startInfo = new ProcessStartInfo(consolePath)
            {
                Arguments = $"stop -i {emuIndex}",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            var process = Process.Start(startInfo);
            if (process != null && process.WaitForExit(5000))
            {
                LoggerHelper.Info($"Emulator at index {emuIndex} closed through console. Console path: {consolePath}");
                return true;
            }

            LoggerHelper.Warning($"Console process at index {emuIndex} did not exit within the specified timeout. Killing emulator by window. Console path: {consolePath}");
            return KillEmulatorByWindow();
        }

        LoggerHelper.Info($"Error: `{consolePath}` not found, try to kill emulator by window.");
        return KillEmulatorByWindow();
    }

    /// <summary>
    /// 一个用于关闭蓝叠模拟器的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    private static bool KillEmulatorBlueStacks()
    {
        Process[] processes = Process.GetProcessesByName("HD-Player");
        if (processes.Length <= 0)
        {
            return false;
        }

        ProcessModule processModule;
        try
        {
            processModule = processes[0].MainModule;
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Error: Failed to get the main module of the emulator process.");
            LoggerHelper.Error(e.Message);
            return false;
        }

        if (processModule == null)
        {
            return false;
        }


        var emuLocation = Path.GetDirectoryName(processModule.FileName);
        if (emuLocation == null)
        {
            return false;
        }

        string consolePath = Path.Combine(emuLocation, "bsconsole.exe");

        if (File.Exists(consolePath))
        {
            LoggerHelper.Info($"Info: `{consolePath}` has been found. This may be the BlueStacks China emulator, try to kill the emulator by window.");
            return KillEmulatorByWindow();
        }

        LoggerHelper.Info($"Info: `{consolePath}` not found. This may be the BlueStacks International emulator, try to kill the emulator by the port.");
        if (KillEmulator())
        {
            return true;
        }

        LoggerHelper.Info("Info: Failed to kill emulator by the port, try to kill emulator process with PID.");

        if (processes.Length > 1)
        {
            LoggerHelper.Warning("Warning: The number of elements in processes exceeds one, abort closing the emulator");
            return false;
        }

        try
        {
            processes[0].Kill();
            return processes[0].WaitForExit(20000);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Error: Failed to kill emulator process with PID {processes[0].Id}. Exception: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Kills emulator by Window hwnd.
    /// </summary>
    /// <returns>Whether the operation is successful.</returns>
    public static bool KillEmulatorByWindow()
    {
        var pid = 0;
        var windowName = new[]
        {
            "MuMu安卓设备",
            "明日方舟",
            "明日方舟 - MuMu模拟器",
            "BlueStacks App Player",
            "BlueStacks",
        };
        foreach (string processName in windowName)
        {
            if (OperatingSystem.IsWindows())
            {
                var hwnd = FindWindow(null, processName);
                if (hwnd == IntPtr.Zero)
                {
                    continue;
                }

                GetWindowThreadProcessId(hwnd, out pid);
            }
            else
            {
                Process[] processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                {
                    continue;
                }

                pid = processes[0].Id;
            }
            break;
        }

        if (pid == 0)
        {
            return KillEmulator();
        }

        try
        {
            var emulator = Process.GetProcessById(pid);
            emulator.CloseMainWindow();
            if (!emulator.WaitForExit(5000))
            {
                emulator.Kill();
                if (emulator.WaitForExit(5000))
                {
                    LoggerHelper.Info($"Emulator with process ID {pid} killed successfully.");
                    KillEmulator();
                    return true;
                }

                LoggerHelper.Error($"Failed to kill emulator with process ID {pid}.");
                return false;
            }

            // 尽管已经成功 CloseMainWindow()，再次尝试 killEmulator()
            // Refer to https://github.com/MaaAssistantArknights/MaaAssistantArknights/pull/1878
            KillEmulator();

            // 已经成功 CloseMainWindow()，所以不管 killEmulator() 的结果如何，都返回 true
            return true;
        }
        catch
        {
            LoggerHelper.Error("Kill emulator by window error");
        }

        return KillEmulator();
    }

    /// <summary>
    /// Kills emulator.
    /// </summary>
    /// <returns>Whether the operation is successful.</returns>
    public static bool KillEmulator()
    {
        int pid = 0;
        var address = MaaProcessor.Config.AdbDevice.AdbSerial;
        var port = address.StartsWith("127") ? address.Substring(10) : "5555";
        LoggerHelper.Info($"address: {address}, port: {port}");
        string portCmd = string.Empty;
        if (OperatingSystem.IsWindows())
        {
            // Windows平台实现（保留原逻辑）
            portCmd = "netstat -ano | findstr \"" + port + "\"";
        }
        else
        {
            // Linux/macOS平台实现
            portCmd = $"lsof -i :{port} | grep LISTEN | awk '{{print $2}}' | head -n 1";
        }

        var checkCmd = new Process
        {
            StartInfo =
            {
                FileName = GetPlatformShell(),
                Arguments = GetPlatformArguments(),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        try
        {
            checkCmd.Start();

            if (!OperatingSystem.IsWindows()) { checkCmd.StandardInput.WriteLine(portCmd); }

            checkCmd.StandardInput.WriteLine("exit");


            var output = checkCmd.StandardOutput.ReadToEnd();
            pid = ParsePidFromOutput(output, port);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Process error: {ex.Message}");
            return false;
        }
        finally
        {
            checkCmd.Close();
        }

        if (pid == 0)
        {
            LoggerHelper.Error("Failed to get emulator PID");
            return false;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows进程终止
                Process.GetProcessById(pid).Kill();
            } else {
                // Unix系统信号终止
                Process.Start("kill", $"-9 {pid}")?.WaitForExit();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

// 跨平台辅助方法
    private static string GetPlatformShell()
    {
        if (OperatingSystem.IsWindows())
            return "cmd.exe";
        return "/bin/bash";
    }

    private static string GetPlatformArguments()
    {
        if (OperatingSystem.IsWindows())
            return "/c";
        return "-c";
    }

    private static int ParsePidFromOutput(string output, string port)
    {

        if (OperatingSystem.IsWindows())
        {
            // Windows输出解析逻辑
            var reg = new Regex("\\s+", RegexOptions.Compiled);
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("TCP")) continue;

                var arr = reg.Replace(trimmed, ",").Split(',');
                if (arr.Length > 4 && arr[1].Contains($":{port}"))
                    return int.Parse(arr[4]);
            }
        }
        else
        {
            // Linux/macOS输出解析
            if (int.TryParse(output.Trim(), out var result))
                return result;
        }
        return 0;
    }
}
