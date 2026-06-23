// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Timers;
using MMORPG.Framework.Logging;
using MMORPG.Framework.Observability;
using Timer = System.Timers.Timer;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 内存监控器
/// 
/// 定期监控内存使用情况，并在内存使用过高时发出告警
/// 支持：
/// - 内存使用阈值告警
/// - GC 统计监控
/// - 内存泄漏检测（长时间内存持续增长）
/// - 自动触发 GC（可选）
/// 
/// 使用示例：
/// <code>
/// var monitor = new MemoryMonitor(new MemoryMonitorOptions
/// {
///     WarningThresholdMB = 1024,    // 1GB 警告阈值
///     CriticalThresholdMB = 2048,   // 2GB 严重阈值
///     CheckIntervalSeconds = 30     // 每 30 秒检查一次
/// });
/// monitor.Start();
/// 
/// // 订阅告警事件
/// monitor.MemoryWarning += (sender, e) => 
///     Logger.Warning("Memory", "内存使用过高: {0}MB", e.CurrentMemoryMB);
/// </code>
/// </summary>
public class MemoryMonitor
{
    private readonly MemoryMonitorOptions _options;
    private readonly Timer _checkTimer;
    private long _lastMemoryBytes;
    private int _continuousGrowthCount;
    private bool _isRunning;

    /// <summary>
    /// 内存警告事件
    /// 
    /// 当内存使用超过警告阈值时触发
    /// </summary>
    public event EventHandler<MemoryEventArgs>? MemoryWarning;

    /// <summary>
    /// 内存严重告警事件
    /// 
    /// 当内存使用超过严重阈值时触发
    /// </summary>
    public event EventHandler<MemoryEventArgs>? MemoryCritical;

    /// <summary>
    /// 内存泄漏检测事件
    /// 
    /// 当检测到内存持续增长时触发
    /// </summary>
    public event EventHandler<MemoryLeakEventArgs>? MemoryLeakDetected;

    /// <summary>
    /// 构造内存监控器
    /// </summary>
    /// <param name="options">监控配置</param>
    public MemoryMonitor(MemoryMonitorOptions? options = null)
    {
        _options = options ?? new MemoryMonitorOptions();
        _checkTimer = new Timer(_options.CheckIntervalSeconds * 1000);
        _checkTimer.Elapsed += OnCheckTimerElapsed;
        _isRunning = false;
    }

    /// <summary>
    /// 启动内存监控
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _checkTimer.Start();

        // 记录初始内存
        _lastMemoryBytes = GetCurrentMemoryBytes();

        Logger.Info("Memory", "内存监控已启动: 警告阈值={0}MB, 严重阈值={1}MB, 检查间隔={2}s",
            _options.WarningThresholdMB, _options.CriticalThresholdMB, _options.CheckIntervalSeconds);
    }

    /// <summary>
    /// 停止内存监控
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _checkTimer.Stop();

        Logger.Info("Memory", "内存监控已停止");
    }

    /// <summary>
    /// 定时检查内存使用情况
    /// </summary>
    private void OnCheckTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            CheckMemory();
        }
        catch (Exception ex)
        {
            Logger.Error("Memory", ex, "内存检查异常");
        }
    }

    /// <summary>
    /// 检查内存使用情况
    /// </summary>
    private void CheckMemory()
    {
        var currentMemoryBytes = GetCurrentMemoryBytes();
        var currentMemoryMB = currentMemoryBytes / (1024 * 1024);

        // 更新指标
        UpdateMetrics(currentMemoryBytes);

        // 1. 检查阈值告警
        if (currentMemoryMB >= _options.CriticalThresholdMB)
        {
            var args = new MemoryEventArgs(currentMemoryMB, _options.CriticalThresholdMB, "严重");
            MemoryCritical?.Invoke(this, args);
            Logger.Error("Memory", "内存使用严重: {0}MB, 阈值={1}MB", currentMemoryMB, _options.CriticalThresholdMB);

            // 如果配置了自动 GC，触发 GC
            if (_options.AutoTriggerGCOnCritical)
            {
                TriggerGC();
            }
        }
        else if (currentMemoryMB >= _options.WarningThresholdMB)
        {
            var args = new MemoryEventArgs(currentMemoryMB, _options.WarningThresholdMB, "警告");
            MemoryWarning?.Invoke(this, args);
            Logger.Warning("Memory", "内存使用过高: {0}MB, 阈值={1}MB", currentMemoryMB, _options.WarningThresholdMB);
        }

        // 2. 检查内存泄漏（持续增长）
        if (_options.EnableLeakDetection)
        {
            CheckMemoryLeak(currentMemoryBytes);
        }

        // 3. 记录 GC 统计
        if (_options.EnableGCStats)
        {
            RecordGCStats();
        }

        _lastMemoryBytes = currentMemoryBytes;
    }

    /// <summary>
    /// 检查内存泄漏
    /// 
    /// 如果内存持续增长超过阈值次数，发出告警
    /// </summary>
    private void CheckMemoryLeak(long currentMemoryBytes)
    {
        var growthThresholdBytes = _options.LeakGrowthThresholdMB * 1024 * 1024;

        if (currentMemoryBytes > _lastMemoryBytes + growthThresholdBytes)
        {
            _continuousGrowthCount++;

            if (_continuousGrowthCount >= _options.LeakContinuousCountThreshold)
            {
                var growthMB = (currentMemoryBytes - _lastMemoryBytes) / (1024 * 1024);
                var args = new MemoryLeakEventArgs(
                    currentMemoryBytes / (1024 * 1024),
                    growthMB,
                    _continuousGrowthCount);

                MemoryLeakDetected?.Invoke(this, args);
                Logger.Warning("Memory", "检测到可能的内存泄漏: 当前={0}MB, 增长={1}MB, 连续增长次数={2}",
                    args.CurrentMemoryMB, args.GrowthMB, args.ContinuousGrowthCount);

                // 重置计数
                _continuousGrowthCount = 0;
            }
        }
        else
        {
            // 内存没有增长，重置计数
            _continuousGrowthCount = 0;
        }
    }

    /// <summary>
    /// 获取当前内存使用量（字节）
    /// </summary>
    private static long GetCurrentMemoryBytes()
    {
        return GC.GetTotalMemory(false);
    }

    /// <summary>
    /// 更新内存指标
    /// </summary>
    private void UpdateMetrics(long memoryBytes)
    {
        try
        {
            if (!MetricsCollector.Instance.IsEnabled)
                return;

            var memoryMB = memoryBytes / (1024.0 * 1024.0);
            var gauge = MetricsCollector.Instance.GetGauge("memory.used_mb");
            if (gauge != null)
            {
                gauge.Set(memoryMB);
            }
            else
            {
                MetricsCollector.Instance.RegisterGauge("memory.used_mb", "内存使用量(MB)");
                MetricsCollector.Instance.GetGauge("memory.used_mb")?.Set(memoryMB);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Memory", ex, "更新内存指标失败");
        }
    }

    /// <summary>
    /// 记录 GC 统计信息
    /// </summary>
    private void RecordGCStats()
    {
        try
        {
            if (!MetricsCollector.Instance.IsEnabled)
                return;

            // GC 次数
            for (int gen = 0; gen <= 2; gen++)
            {
                var count = GC.CollectionCount(gen);
                var gauge = MetricsCollector.Instance.GetGauge($"gc.gen{gen}_count");
                if (gauge != null)
                {
                    gauge.Set(count);
                }
                else
                {
                    MetricsCollector.Instance.RegisterGauge($"gc.gen{gen}_count", $"GC Gen{gen}收集次数");
                    MetricsCollector.Instance.GetGauge($"gc.gen{gen}_count")?.Set(count);
                }
            }

            // GC 总暂停时间（近似）
            var totalMemory = GC.GetTotalMemory(false);
            var totalGauge = MetricsCollector.Instance.GetGauge("gc.total_memory_bytes");
            if (totalGauge != null)
            {
                totalGauge.Set(totalMemory);
            }
            else
            {
                MetricsCollector.Instance.RegisterGauge("gc.total_memory_bytes", "GC管理的总内存(字节)");
                MetricsCollector.Instance.GetGauge("gc.total_memory_bytes")?.Set(totalMemory);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Memory", ex, "记录 GC 统计失败");
        }
    }

    /// <summary>
    /// 手动触发 GC
    /// 
    /// 仅在严重内存告警时使用
    /// </summary>
    public void TriggerGC()
    {
        Logger.Warning("Memory", "手动触发 GC（内存严重告警）");

        var beforeMemoryMB = GetCurrentMemoryBytes() / (1024 * 1024);

        // 执行完整 GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var afterMemoryMB = GetCurrentMemoryBytes() / (1024 * 1024);
        var freedMB = beforeMemoryMB - afterMemoryMB;

        Logger.Info("Memory", "GC 完成: 释放 {0}MB, 当前 {1}MB", freedMB, afterMemoryMB);

        // 更新指标
        var freedGauge = MetricsCollector.Instance.GetGauge("gc.manual_freed_mb");
        if (freedGauge != null)
        {
            freedGauge.Set(freedMB);
        }
        else
        {
            MetricsCollector.Instance.RegisterGauge("gc.manual_freed_mb", "手动GC释放内存(MB)");
            MetricsCollector.Instance.GetGauge("gc.manual_freed_mb")?.Set(freedMB);
        }
    }

    /// <summary>
    /// 获取当前内存使用报告
    /// </summary>
    public MemoryReport GetReport()
    {
        var memoryBytes = GetCurrentMemoryBytes();
        var memoryMB = memoryBytes / (1024 * 1024);

        return new MemoryReport
        {
            CurrentMemoryMB = memoryMB,
            WarningThresholdMB = _options.WarningThresholdMB,
            CriticalThresholdMB = _options.CriticalThresholdMB,
            IsWarning = memoryMB >= _options.WarningThresholdMB,
            IsCritical = memoryMB >= _options.CriticalThresholdMB,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            Timestamp = DateTime.UtcNow
        };
    }
}

/// <summary>
/// 内存监控配置
/// </summary>
public class MemoryMonitorOptions
{
    /// <summary>
    /// 警告阈值（MB）
    /// 
    /// 当内存使用超过此阈值时，触发警告事件
    /// 默认：1024MB（1GB）
    /// </summary>
    public long WarningThresholdMB { get; set; } = 1024;

    /// <summary>
    /// 严重阈值（MB）
    /// 
    /// 当内存使用超过此阈值时，触发严重告警事件
    /// 默认：2048MB（2GB）
    /// </summary>
    public long CriticalThresholdMB { get; set; } = 2048;

    /// <summary>
    /// 检查间隔（秒）
    /// 
    /// 默认：30秒
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// 是否启用内存泄漏检测
    /// 
    /// 默认：true
    /// </summary>
    public bool EnableLeakDetection { get; set; } = true;

    /// <summary>
    /// 内存泄漏增长阈值（MB）
    /// 
    /// 每次检查内存增长超过此阈值时，计数器增加
    /// 默认：50MB
    /// </summary>
    public long LeakGrowthThresholdMB { get; set; } = 50;

    /// <summary>
    /// 内存泄漏连续增长次数阈值
    /// 
    /// 连续增长超过此次数时，发出泄漏告警
    /// 默认：5次
    /// </summary>
    public int LeakContinuousCountThreshold { get; set; } = 5;

    /// <summary>
    /// 是否启用 GC 统计记录
    /// 
    /// 默认：true
    /// </summary>
    public bool EnableGCStats { get; set; } = true;

    /// <summary>
    /// 是否在严重告警时自动触发 GC
    /// 
    /// 默认：false（不建议开启，可能导致性能问题）
    /// </summary>
    public bool AutoTriggerGCOnCritical { get; set; } = false;
}

/// <summary>
/// 内存事件参数
/// </summary>
public class MemoryEventArgs : EventArgs
{
    public long CurrentMemoryMB { get; }
    public long ThresholdMB { get; }
    public string Level { get; }

    public MemoryEventArgs(long currentMemoryMB, long thresholdMB, string level)
    {
        CurrentMemoryMB = currentMemoryMB;
        ThresholdMB = thresholdMB;
        Level = level;
    }
}

/// <summary>
/// 内存泄漏事件参数
/// </summary>
public class MemoryLeakEventArgs : EventArgs
{
    public long CurrentMemoryMB { get; }
    public long GrowthMB { get; }
    public int ContinuousGrowthCount { get; }

    public MemoryLeakEventArgs(long currentMemoryMB, long growthMB, int continuousGrowthCount)
    {
        CurrentMemoryMB = currentMemoryMB;
        GrowthMB = growthMB;
        ContinuousGrowthCount = continuousGrowthCount;
    }
}

/// <summary>
/// 内存报告
/// </summary>
public class MemoryReport
{
    public long CurrentMemoryMB { get; set; }
    public long WarningThresholdMB { get; set; }
    public long CriticalThresholdMB { get; set; }
    public bool IsWarning { get; set; }
    public bool IsCritical { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public DateTime Timestamp { get; set; }

    public string GetStatus()
    {
        if (IsCritical) return "严重";
        if (IsWarning) return "警告";
        return "正常";
    }
}