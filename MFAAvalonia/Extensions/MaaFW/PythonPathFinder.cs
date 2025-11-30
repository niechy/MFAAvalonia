using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// Python 路径查找工具类
/// </summary>
public static class PythonPathFinder
{
    /// <summary>
    /// 查找 Python 可执行文件路径
    /// </summary>
    /// <param name="program">程序名称</param>
    /// <returns>找到的路径或原始程序名</returns>
    public static string FindPythonPath(string? program)
    {
        if (program != "python")
        {
            return program ?? string.Empty;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindPythonPathOnWindows(program);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return FindPythonPathOnMacOS(program);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return FindPythonPathOnLinux(program);
        }
        else
        {
            return FindPythonPathGeneric(program);
        }
    }

    private static string FindPythonPathOnWindows(string program)
    {
        // 先检查 PATH 环境变量
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(dir, $"{program}.exe");
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    /* 忽略错误目录 */
                }
            }
        }

        // 尝试查找 Python 安装目录 (常见位置)
        var pythonDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python")
        };

        foreach (var baseDir in pythonDirs)
        {
            if (Directory.Exists(baseDir))
            {
                try
                {
                    // 优先选择版本号最高的目录
                    var pythonDir = Directory.GetDirectories(baseDir)
                        .OrderByDescending(d => d)
                        .FirstOrDefault();

                    if (pythonDir != null)
                    {
                        var pythonPath = Path.Combine(pythonDir, $"{program}.exe");
                        if (File.Exists(pythonPath))
                        {
                            return pythonPath;
                        }
                    }
                }
                catch
                {
                    /* 忽略错误 */
                }
            }
        }

        // 尝试查找 Anaconda/Miniconda
        var condaDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "anaconda3");
        if (Directory.Exists(condaDir))
        {
            var condaPythonPath = Path.Combine(condaDir, $"{program}.exe");
            if (File.Exists(condaPythonPath))
            {
                return condaPythonPath;
            }
        }

        return program;
    }

    private static string FindPythonPathOnMacOS(string program)
    {
        // 检查 PATH 环境变量
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(dir, program);
                    if (File.Exists(fullPath) && IsExecutable(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    /* 忽略错误目录 */
                }
            }
        }

        // 检查 Homebrew 安装位置
        var homebrewDir = "/usr/local/bin";
        var homebrewPath = Path.Combine(homebrewDir, program);
        if (File.Exists(homebrewPath) && IsExecutable(homebrewPath))
        {
            return homebrewPath;
        }

        // 检查 Python.org 安装位置
        var pythonOrgDir = "/Library/Frameworks/Python.framework/Versions";
        if (Directory.Exists(pythonOrgDir))
        {
            try
            {
                // 选择最新版本
                var versions = Directory.GetDirectories(pythonOrgDir)
                    .Select(Path.GetFileName)
                    .Where(v => v != null && v.StartsWith("3"))
                    .OrderByDescending(v => new Version(v!))
                    .ToList();

                foreach (var version in versions)
                {
                    var pythonPath = Path.Combine(pythonOrgDir, version!, "bin", program);
                    if (File.Exists(pythonPath) && IsExecutable(pythonPath))
                    {
                        return pythonPath;
                    }
                }
            }
            catch
            {
                /* 忽略错误 */
            }
        }

        return program;
    }

    private static string FindPythonPathOnLinux(string program)
    {
        // 检查 PATH 环境变量
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(dir, program);
                    if (File.Exists(fullPath) && IsExecutable(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    /* 忽略错误目录 */
                }
            }
        }

        // 检查常见的 Python 安装位置
        var commonDirs = new[]
        {
            "/usr/bin",
            "/usr/local/bin",
            "/opt/python/bin"
        };

        foreach (var dir in commonDirs)
        {
            var fullPath = Path.Combine(dir, program);
            if (File.Exists(fullPath) && IsExecutable(fullPath))
            {
                return fullPath;
            }
        }

        return program;
    }

    private static string FindPythonPathGeneric(string program)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(dir, program);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    /* 忽略错误目录 */
                }
            }
        }

        return program;
    }

    /// <summary>
    /// 检查文件是否具有可执行权限 (仅适用于 Unix-like 系统)
    /// </summary>
    private static bool IsExecutable(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return true;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "file",
                Arguments = $"--brief --mime-type \"{path}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false
            });

            if (process != null)
            {
                process.WaitForExit();
                var output = process.StandardOutput.ReadToEnd().Trim();
                return output.Contains("executable") || output.Contains("application/x-executable");
            }

            return false;
        }
        catch
        {
            return File.Exists(path);
        }
    }
}