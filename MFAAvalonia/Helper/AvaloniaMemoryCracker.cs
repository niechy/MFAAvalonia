using System;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper;

/// <summary>
/// 高性能内存清理器，针对 Avalonia 应用优化
/// 使用非阻塞 GC 策略，避免 UI卡顿
/// </summary>
public class AvaloniaMemoryCracker : IDisposable
{
    #region 平台相关API

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    #endregion

    #region 配置常量

    // 内存优化阈值（可配置）
    private const long MemoryThresholdBytes = 256 * 1024 * 1024; // 256MB
    private const long HighMemoryPressureThreshold = 512 * 1024 * 1024; // 512MB - 高内存压力阈值
    private const long CriticalMemoryPressureThreshold = 1024 * 1024 * 1024; // 1GB - 临界内存压力阈值

    // 内存历史记录配置
    private const int MaxHistoryCount = 10;

    // LOH 压缩间隔（每N次清理执行一次LOH压缩，仅在空闲时）
    private const int LohCompactionInterval = 10;

    #endregion

    #region 字段

    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private Task? _monitorTask;

    // 内存历史记录（用于诊断泄漏趋势）
    private readonly Queue<(DateTime Time, long Memory)> _memoryHistory = new();
    private readonly object _historyLock = new();

    // 清理计数器（用于控制LOH压缩频率）
    private int _cleanupCount;
    // 上次清理时间（用于自适应清理间隔）
    private DateTime _lastCleanupTime = DateTime.MinValue;

    // 上次内存使用量（用于判断是否需要清理）
    private long _lastMemoryUsage;

    #endregion

    #region 核心逻辑

    /// <summary>启动内存优化守护进程</summary>
    /// <param name="intervalSeconds">基础清理间隔秒数（默认30秒），实际间隔会根据内存压力自适应调整</param>
    public void Cracker(int intervalSeconds = 30)
    {
        _monitorTask = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var currentMemory = GetCurrentMemoryUsage();
                    var memoryInfo = GetMemoryPressureInfo();

                    // 根据内存压力决定是否需要清理
                    var shouldCleanup = ShouldPerformCleanup(currentMemory, memoryInfo);

                    if (shouldCleanup)
                    {
                        var beforeMemory = currentMemory;
                        await PerformMemoryCleanupAsync(memoryInfo);
                        var afterMemory = GetCurrentMemoryUsage();

                        // 记录内存变化用于诊断
                        RecordMemorySnapshot(afterMemory);

                        // 检测内存泄漏趋势
                        CheckMemoryLeakTrend(beforeMemory, afterMemory);

                        _lastCleanupTime = DateTime.UtcNow;
                        _lastMemoryUsage = afterMemory;
                    }

                    // 自适应清理间隔：内存压力越大，间隔越短
                    var adaptiveInterval = CalculateAdaptiveInterval(intervalSeconds, memoryInfo);
                    await Task.Delay(TimeSpan.FromSeconds(adaptiveInterval), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"[内存管理]内存清理异常: {ex.Message}");
                    // 发生异常时使用默认间隔
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), _cts.Token).ConfigureAwait(false);
                }
            }
        }, _cts.Token);
    }

    /// <summary>判断是否需要执行清理</summary>
    private bool ShouldPerformCleanup(long currentMemory, MemoryPressureInfo memoryInfo)
    {
        // 如果内存超过阈值，需要清理
        if (currentMemory > MemoryThresholdBytes)
            return true;

        // 如果内存压力高，需要清理
        if (memoryInfo.MemoryLoadPercentage > 70)
            return true;

        // 如果距离上次清理时间过长（超过5分钟），执行一次清理
        if ((DateTime.UtcNow - _lastCleanupTime).TotalMinutes > 5)
            return true;

        // 如果内存增长超过上次的50%，需要清理
        if (_lastMemoryUsage > 0 && currentMemory > _lastMemoryUsage * 1.5)
            return true;

        return false;
    }

    /// <summary>计算自适应清理间隔</summary>
    private static int CalculateAdaptiveInterval(int baseInterval, MemoryPressureInfo memoryInfo)
    {
        // 根据内存压力调整间隔
        if (memoryInfo.MemoryLoadPercentage > 90)
            return Math.Max(10, baseInterval / 2); // 高压力：间隔缩短到1/2，最少10秒
        if (memoryInfo.MemoryLoadPercentage > 70)
            return Math.Max(15, baseInterval * 2 / 3); // 中等压力：间隔缩短到2/3
        if (memoryInfo.MemoryLoadPercentage < 30)
            return baseInterval * 2; // 低压力：间隔延长到2倍

        return baseInterval;
    }

    /// <summary>记录内存快照用于趋势分析</summary>
    private void RecordMemorySnapshot(long memory)
    {
        lock (_historyLock)
        {
            _memoryHistory.Enqueue((DateTime.UtcNow, memory));
            while (_memoryHistory.Count > MaxHistoryCount)
            {
                _memoryHistory.Dequeue();
            }
        }
    }

    /// <summary>检测内存泄漏趋势</summary>
    private void CheckMemoryLeakTrend(long beforeCleanup, long afterCleanup)
    {
        var freedMemory = beforeCleanup - afterCleanup;
        var freedMB = freedMemory / (1024.0 * 1024.0);

        if (freedMemory > 1024 * 1024) // 只记录释放超过1MB的情况
        {
            LoggerHelper.Info($"[内存管理]释放了 {freedMB:F2} MB");
        }

        // 检查内存是否持续增长（泄漏预警）
        lock (_historyLock)
        {
            if (_memoryHistory.Count >= 5)
            {
                var snapshots = _memoryHistory.ToArray();
                var firstMemory = snapshots[0].Memory;
                var lastMemory = snapshots[^1].Memory;

                if (firstMemory > 0)
                {
                    var growthRate = (lastMemory - firstMemory) / (double)firstMemory;

                    // 如果在多次清理后内存仍持续增长超过 50%，发出警告
                    if (growthRate > 0.5)
                    {
                        LoggerHelper.Info($"[内存管理]检测到潜在内存泄漏: 内存从 {firstMemory / (1024 * 1024)} MB 增长到 {lastMemory / (1024 * 1024)} MB (增长 {growthRate * 100:F1}%)");
                    }
                }
            }
        }
    }

    /// <summary>执行内存清理策略（异步，避免阻塞UI）</summary>
    private async Task PerformMemoryCleanupAsync(MemoryPressureInfo memoryInfo)
    {
        _cleanupCount++;

        // 第一步：使用非阻塞 GC 策略
        PerformNonBlockingGarbageCollection(memoryInfo);

        // 第二步：等待一小段时间让 GC 在后台完成
        await Task.Delay(50, _cts.Token).ConfigureAwait(false);

        // 第三步：平台特定优化（在后台线程执行）
        await Task.Run(PerformPlatformSpecificOptimization, _cts.Token).ConfigureAwait(false);

        // 第四步：定期执行LOH压缩（每N次清理执行一次，且仅在低压力时）
        if (_cleanupCount % LohCompactionInterval == 0 && memoryInfo.MemoryLoadPercentage < 50)
        {
            // LOH 压缩会导致暂停，仅在内存压力较低时执行
            ScheduleLohCompaction();
        }
    }

    /// <summary>执行非阻塞垃圾回收</summary>
    private static void PerformNonBlockingGarbageCollection(MemoryPressureInfo memoryInfo)
    {
        try
        {
            // 关键改进：使用 blocking: false 避免阻塞 UI 线程
            // compacting: false 避免内存压缩导致的长时间暂停

            if (memoryInfo.TotalMemory > CriticalMemoryPressureThreshold)
            {
                // 临界压力：执行完整 GC，但不阻塞
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, false, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, false, true);
            }
            else if (memoryInfo.TotalMemory > HighMemoryPressureThreshold)
            {
                // 高压力：强制GC
                GC.Collect(GC.MaxGeneration,  GCCollectionMode.Forced, blocking: false, compacting:  true);
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            else if (memoryInfo.TotalMemory > MemoryThresholdBytes)
            {
                // 中等压力：优化模式GC
                GC.Collect(GC.MaxGeneration,  GCCollectionMode.Optimized, blocking: false, true);
            }
            else
            {
                // 低压力：仅回收Gen0和Gen1
                GC.Collect(1,GCCollectionMode.Optimized, blocking: false);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"[内存管理]GC执行异常: {ex.Message}");
        }
    }

    /// <summary>调度LOH压缩（在下次GC 时执行）</summary>
    private static void ScheduleLohCompaction()
    {
        try
        {
            // 仅设置标志，让 GC 在合适的时机自动执行压缩
            // 不立即触发 GC，避免阻塞
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            LoggerHelper.Info("[内存管理]已调度LOH压缩（将在下次GC时执行）");
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"[内存管理]LOH压缩调度异常: {ex.Message}");
        }
    }

    /// <summary>执行平台特定优化</summary>
    private static void PerformPlatformSpecificOptimization()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsMemoryOptimization();
        }
        // Linux 和 macOS 的优化通过 GC 已经处理
    }

    #endregion

    #region 平台特定实现

    /// <summary>Windows平台优化（工作集调整）</summary>
    private static void WindowsMemoryOptimization()
    {
        try
        {
            var processHandle = GetCurrentProcess();

            // 重置工作集大小限制，让系统自动管理
            // 注意：EmptyWorkingSet 可能导致页面错误增加，影响性能
            //仅使用 SetProcessWorkingSetSize 来提示系统可以回收内存
            SetProcessWorkingSetSize(processHandle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"[内存管理]Windows内存优化失败: {ex.Message}");
        }
    }

    #endregion

    #region 内存信息获取

    /// <summary>获取当前进程内存占用（字节）</summary>
    private static long GetCurrentMemoryUsage()
    {
        try
        {
            return GC.GetTotalMemory(false);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>获取内存压力信息</summary>
    private static MemoryPressureInfo GetMemoryPressureInfo()
    {
        try
        {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            var totalMemory = GC.GetTotalMemory(false);

            // 计算内存负载百分比
            var memoryLoadPercentage = gcMemoryInfo.HighMemoryLoadThresholdBytes > 0
                ? (int)((double)gcMemoryInfo.MemoryLoadBytes / gcMemoryInfo.HighMemoryLoadThresholdBytes * 100)
                : 0;

            return new MemoryPressureInfo
            {
                TotalMemory = totalMemory,
                HeapSize = gcMemoryInfo.HeapSizeBytes,
                FragmentedBytes = gcMemoryInfo.FragmentedBytes,
                MemoryLoadBytes = gcMemoryInfo.MemoryLoadBytes,
                HighMemoryLoadThreshold = gcMemoryInfo.HighMemoryLoadThresholdBytes,
                MemoryLoadPercentage = Math.Min(100, Math.Max(0, memoryLoadPercentage)),
                PinnedObjectsCount = gcMemoryInfo.PinnedObjectsCount,
                Generation0Count = GC.CollectionCount(0),
                Generation1Count = GC.CollectionCount(1),
                Generation2Count = GC.CollectionCount(2)
            };
        }
        catch
        {
            return new MemoryPressureInfo
            {
                TotalMemory = GC.GetTotalMemory(false),
                MemoryLoadPercentage = 50 // 默认中等压力
            };
        }
    }

    /// <summary>内存压力信息</summary>
    private readonly struct MemoryPressureInfo
    {
        public long TotalMemory { get; init; }
        public long HeapSize { get; init; }
        public long FragmentedBytes { get; init; }
        public long MemoryLoadBytes { get; init; }
        public long HighMemoryLoadThreshold { get; init; }
        public int MemoryLoadPercentage { get; init; }
        public long PinnedObjectsCount { get; init; }
        public int Generation0Count { get; init; }
        public int Generation1Count { get; init; }
        public int Generation2Count { get; init; }
    }

    #endregion

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

            // 等待监控任务完成
            try
            {
                _monitorTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 忽略等待异常
            }

            _cts.Dispose();
        }

        _disposed = true;
    }

    ~AvaloniaMemoryCracker() => Dispose(false);

    #endregion
}
