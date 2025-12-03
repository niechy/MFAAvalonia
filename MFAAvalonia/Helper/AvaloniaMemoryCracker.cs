using System;
using System.Collections.Generic;
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
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

    private const int VM_PURGE_ALL = 0x00000001;

    // 内存优化阈值（可配置，示例：256MB）
    private const long MEMORY_THRESHOLD = 256 * 1024 * 1024;

    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private Task? _monitorTask;
    // 缓存当前进程句柄，避免重复创建 Process 对象导致句柄泄漏
    private readonly IntPtr _currentProcessHandle;

    // 内存历史记录（用于诊断泄漏趋势）
    private readonly Queue<(DateTime Time, long Memory)> _memoryHistory = new();
    private const int MaxHistoryCount = 10;

    #endregion
    public AvaloniaMemoryCracker()
    {
        // 在构造函数中获取并缓存进程句柄
        // 注意：这里使用 GetCurrentProcess() 返回的是伪句柄，不需要关闭
        _currentProcessHandle = Process.GetCurrentProcess().Handle;
    }

    #region 核心逻辑

    /// <summary>启动内存优化守护进程</summary>
    /// <param name="intervalSeconds">清理间隔秒数（默认30秒）</param>
    public void Cracker(int intervalSeconds = 30)
    {
        // 修复：使用 Task.Run 而不是 Task.Factory.StartNew + async
        _monitorTask = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var beforeMemory = GetCurrentMemoryUsage();
                    PerformMemoryCleanup();
                    var afterMemory = GetCurrentMemoryUsage();

                    // 记录内存变化用于诊断
                    RecordMemorySnapshot(afterMemory);
                    
                    // 检测内存泄漏趋势
                    CheckMemoryLeakTrend(beforeMemory, afterMemory);

                    // 修复：使用 Task.Delay 而不是 Thread.Sleep
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，退出循环
                    break;
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"内存清理异常: {ex.Message}");
                }
            }
        }, _cts.Token);
    }

    /// <summary>记录内存快照用于趋势分析</summary>
    private void RecordMemorySnapshot(long memory)
    {
        lock (_memoryHistory)
        {
            _memoryHistory.Enqueue((DateTime.Now, memory));
            while (_memoryHistory.Count > MaxHistoryCount)
            {
                _memoryHistory.Dequeue();
            }
        }
    }

    /// <summary>检测内存泄漏趋势</summary>
    private void CheckMemoryLeakTrend(long beforeCleanup, long afterCleanup)
    {
        // 如果清理后内存没有明显下降，可能存在泄漏
        var freedMemory = beforeCleanup - afterCleanup;
        var freedMB = freedMemory / (1024.0 * 1024.0);

        if (freedMemory > 0)
        {
            LoggerHelper.Debug($"内存清理: 释放了 {freedMB:F2} MB");
        }

        // 检查内存是否持续增长（泄漏预警）
        lock (_memoryHistory)
        {
            if (_memoryHistory.Count >= 5)
            {
                var snapshots = _memoryHistory.ToArray();
                var firstMemory = snapshots[0].Memory;
                var lastMemory = snapshots[^1].Memory;
                var growthRate = (lastMemory - firstMemory) / (double)firstMemory;

                // 如果在多次清理后内存仍持续增长超过 50%，发出警告
                if (growthRate > 0.5)
                {
                    LoggerHelper.Debug($"检测到潜在内存泄漏: 内存从 {firstMemory / (1024 * 1024)} MB 增长到 {lastMemory / (1024 * 1024)} MB (增长 {growthRate * 100:F1}%)");
                }
            }
        }
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
        // 使用缓存的进程句柄，避免每次调用都创建新的 Process 对象
        // 这可以防止句柄泄漏，避免长时间运行后可能导致的堆损坏
        try
        {
            SetProcessWorkingSetSize(_currentProcessHandle, -1, -1);
        }
        catch (Exception ex)
        {
            LoggerHelper.Debug($"Windows内存优化失败: {ex.Message}");
        }
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
        // 使用 GC 获取托管内存，避免频繁创建 Process 对象
        // 这比 Process.GetCurrentProcess().WorkingSet64 更安全，不会导致句柄泄漏
        try
        {
            return GC.GetTotalMemory(false);
        }
        catch
        {
            return 0;
        }
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
