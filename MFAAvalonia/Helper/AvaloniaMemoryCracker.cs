using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper;

public class AvaloniaMemoryCracker : IDisposable
{
    #region 平台相关API

    [DllImport("kernel32.dll")]
    private extern static bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

    private const int VM_PURGE_ALL = 0x00000001;

    // 内存优化阈值（可配置，示例：256MB）
    private const long MEMORY_THRESHOLD = 256 * 1024 * 1024;

    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    #endregion

    #region 核心逻辑

    /// <summary>启动内存优化守护进程</summary>
    /// <param name="intervalSeconds">清理间隔秒数（默认30秒）</param>
    public void Cracker(int intervalSeconds = 30)
    {
        Task.Factory.StartNew(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    PerformMemoryCleanup();
                    Thread.Sleep(TimeSpan.FromSeconds(intervalSeconds));
                }
                catch
                {
                    // 异常处理可扩展日志记录
                }
            }
        }, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);

    }


    /// <summary>执行内存清理三步策略</summary>
    private void PerformMemoryCleanup()
    {
        // 第一步：触发托管堆GC回收[1](@ref)
        if (GetCurrentMemoryUsage() > MEMORY_THRESHOLD) // 256MB
        {
            // 优先使用Optimized模式，减少强制回收的性能影响
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
            GC.WaitForPendingFinalizers();
        }

        // 第二步：针对Windows平台优化工作集[1](@ref)
        if (OperatingSystem.IsWindows())
        {
            WindowsMemoryOptimization();
        }
        else if (OperatingSystem.IsLinux())
        {
            LinuxMemoryOptimization();
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacOSMemoryOptimization();
        }

        // 第三步：可选扩展点（如内存池管理）
        // 可在此处集成引用计数或内存碎片整理逻辑[1](@ref)
    }

    #endregion
    #region 平台特定实现

    /// <summary>Windows平台优化（工作集调整）</summary>
    private void WindowsMemoryOptimization()
    {
        SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
    }

    /// <summary>Linux平台优化（释放不常用内存页）</summary>
    private void LinuxMemoryOptimization()
    {
        // try
        // {
        //     // 读取进程内存映射（简化版：对整个堆内存建议释放）
        //     // 更精确的实现可解析/proc/self/maps获取堆地址范围
        //     var process = Process.GetCurrentProcess();
        //     foreach (var module in process.Modules)
        //     {
        //         if (module is ProcessModule pm)
        //         {
        //             // 对每个模块的内存页建议释放（谨慎使用，避免频繁调用）
        //             madvise(pm.BaseAddress, (UIntPtr)pm.ModuleMemorySize, MADV_DONTNEED);
        //         }
        //     }
        //
        //     // 可选：降低OOM Killer优先级（值越小越不容易被杀死）
        //     File.WriteAllText("/proc/self/oom_score_adj", "-500");
        // }
        // catch (Exception ex)
        // {
        //     LoggerHelper.Warn($"Linux内存优化失败: {ex.Message}");
        // }
    }

    /// <summary>macOS平台优化（释放物理内存）</summary>
    private void MacOSMemoryOptimization()
    {
        // try
        // {
        //     // 释放进程中未使用的物理内存（需确保地址范围有效）
        //     var process = Process.GetCurrentProcess();
        //     foreach (var module in process.Modules)
        //     {
        //         if (module is ProcessModule pm)
        //         {
        //             vm_purge(pm.BaseAddress, (nuint)pm.ModuleMemorySize, VM_PURGE_ALL);
        //         }
        //     }
        //
        //     // 可选：启用内存压缩（仅当系统支持时）
        //     int[] name =
        //     {
        //         1 /* CTL_VM */,
        //         48 /* VM_COMPRESSION */
        //     }; // sysctl参数：vm.compressor_mode
        //     int enableCompression = 1; // 1=启用，0=禁用
        //     sysctl(name, (uint)name.Length, IntPtr.Zero, IntPtr.Zero, Marshal.AllocHGlobal(Marshal.SizeOf(enableCompression)), (uint)Marshal.SizeOf(enableCompression));
        // }
        // catch (Exception ex)
        // {
        //     LoggerHelper.Warn($"macOS内存优化失败: {ex.Message}");
        // }
    }

    #endregion

    /// <summary>获取当前进程内存占用（字节）</summary>
    private long GetCurrentMemoryUsage()
    {
        return Process.GetCurrentProcess().WorkingSet64;
    }

    #region 资源释放

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _cts.Cancel();
            _cts.Dispose();
        }
        _disposed = true;
    }

    ~AvaloniaMemoryCracker() => Dispose(false);

    #endregion
}
