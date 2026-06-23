// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Reflection;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 指标收集器（单例）（基于 System.Diagnostics.Metrics 现代化实现，#10 修复）
///
/// ⚠️ 现代化说明：
/// - 内部使用 <see cref="Meter"/>（System.Diagnostics.Metrics）创建所有仪器
/// - Meter 名称："MMORPG.Framework"，版本从程序集读取
/// - 外部工具（dotnet-counters、OpenTelemetry、Prometheus 等）
///   可以通过 Meter 名称 "MMORPG.Framework" 直接监听所有指标
/// - 保留原有的 ICounter / IHistogram / IGauge 接口和 API，向后兼容
///
/// 管理所有指标的注册、查询、快照与重置。
/// </summary>
public class MetricsCollector
{
    /// <summary>
    /// 单例实例
    /// </summary>
    public static MetricsCollector Instance { get; } = new();

    /// <summary>
    /// 是否启用指标收集（volatile 以保证跨线程可见）
    /// </summary>
    private volatile bool _enabled;

    /// <summary>
    /// 指标存储：名称 → 指标对象
    /// </summary>
    private readonly ConcurrentDictionary<string, IMetric> _metrics = new();

    /// <summary>
    /// ⚠️ 现代化（#10 修复）：底层 Meter 实例
    ///
    /// 所有通过 MetricsCollector 注册的指标，都会同时在这个 Meter 下创建对应的仪器。
    /// 外部监听者（OpenTelemetry、dotnet-counters 等）可以通过
    /// Meter 名称 "MMORPG.Framework" 订阅所有指标。
    /// </summary>
    internal Meter Meter { get; }

    /// <summary>
    /// 私有构造函数（保证单例）
    /// </summary>
    private MetricsCollector()
    {
        var assembly = typeof(MetricsCollector).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "1.0.0";
        Meter = new Meter("MMORPG.Framework", version);

        Logger.Info("Metrics", "MetricsCollector 初始化: Meter={0}, Version={1}", "MMORPG.Framework", version);
    }

    /// <summary>
    /// 当前是否启用
    /// </summary>
    public bool IsEnabled => _enabled;

    /// <summary>
    /// 启用指标收集
    /// </summary>
    public void Enable()
    {
        try
        {
            _enabled = true;
            Logger.Info("Metrics", "MetricsCollector 已启用");
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "MetricsCollector.Enable 异常");
        }
    }

    /// <summary>
    /// 禁用指标收集
    /// </summary>
    public void Disable()
    {
        try
        {
            _enabled = false;
            Logger.Info("Metrics", "MetricsCollector 已禁用");
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "MetricsCollector.Disable 异常");
        }
    }

    /// <summary>
    /// 注册或获取计数器
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <returns>计数器实例；异常时返回 null</returns>
    public ICounter? RegisterCounter(string name, string description)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Logger.Warning("Metrics", "RegisterCounter 名称为空");
                return null;
            }

            return (ICounter)_metrics.GetOrAdd(name, _ => new DefaultCounter(Meter, name, description ?? string.Empty));
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "RegisterCounter 异常, Name={0}", name);
            return null;
        }
    }

    /// <summary>
    /// 注册或获取量规
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <param name="valueProvider">动态取值委托；若为 null 则使用 Set 模式</param>
    /// <returns>量规实例；异常时返回 null</returns>
    public IGauge? RegisterGauge(string name, string description, Func<double>? valueProvider = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Logger.Warning("Metrics", "RegisterGauge 名称为空");
                return null;
            }

            return (IGauge)_metrics.GetOrAdd(name, _ =>
                valueProvider != null
                    ? new DefaultGauge(Meter, name, description ?? string.Empty, valueProvider)
                    : new DefaultGauge(Meter, name, description ?? string.Empty));
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "RegisterGauge 异常, Name={0}", name);
            return null;
        }
    }

    /// <summary>
    /// 注册或获取直方图
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <param name="capacity">环形缓冲容量（默认 1024）</param>
    /// <returns>直方图实例；异常时返回 null</returns>
    public IHistogram? RegisterHistogram(string name, string description, int capacity = 1024)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Logger.Warning("Metrics", "RegisterHistogram 名称为空");
                return null;
            }

            return (IHistogram)_metrics.GetOrAdd(name, _ => new DefaultHistogram(Meter, name, description ?? string.Empty, capacity));
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "RegisterHistogram 异常, Name={0}", name);
            return null;
        }
    }

    /// <summary>
    /// 查询指定名称指标的当前值
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <returns>当前值；若不存在或异常则返回 null</returns>
    public double? GetValue(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            if (_metrics.TryGetValue(name, out var metric))
                return metric.Value;

            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "GetValue 异常, Name={0}", name);
            return null;
        }
    }

    /// <summary>
    /// 获取所有指标的当前值快照
    /// </summary>
    /// <returns>名称 → 值 的字典拷贝；异常时返回空字典</returns>
    public IDictionary<string, double> Snapshot()
    {
        try
        {
            var result = new Dictionary<string, double>(_metrics.Count);
            foreach (var kv in _metrics)
            {
                try
                {
                    result[kv.Key] = kv.Value.Value;
                }
                catch (Exception ex)
                {
                    Logger.Error("Metrics", ex, "Snapshot 读取指标异常, Name={0}", kv.Key);
                    result[kv.Key] = 0.0;
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "Snapshot 异常");
            return new Dictionary<string, double>();
        }
    }

    /// <summary>
    /// 获取所有指标的详细快照（包含类型、描述、值）
    ///
    /// 用于 Prometheus 导出等场景
    /// </summary>
    /// <returns>指标详细信息列表</returns>
    public IEnumerable<MetricSnapshotEntry> GetDetailedSnapshot()
    {
        try
        {
            var result = new List<MetricSnapshotEntry>(_metrics.Count);
            foreach (var kv in _metrics)
            {
                try
                {
                    var metric = kv.Value;
                    var entry = new MetricSnapshotEntry
                    {
                        Name = kv.Key,
                        Value = metric.Value,
                        Description = metric.Description,
                        Type = GetMetricType(metric)
                    };
                    result.Add(entry);
                }
                catch (Exception ex)
                {
                    Logger.Error("Metrics", ex, "GetDetailedSnapshot 读取指标异常, Name={0}", kv.Key);
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "GetDetailedSnapshot 异常");
            return Array.Empty<MetricSnapshotEntry>();
        }
    }

    /// <summary>
    /// 获取指标类型名称
    /// </summary>
    private static string GetMetricType(IMetric metric)
    {
        if (metric is ICounter)
            return "counter";
        if (metric is IGauge)
            return "gauge";
        if (metric is IHistogram)
            return "histogram";
        return "unknown";
    }

    /// <summary>
    /// 获取所有已注册的指标名称
    /// </summary>
    /// <returns>名称列表；异常时返回空集合</returns>
    public IEnumerable<string> GetRegisteredMetrics()
    {
        try
        {
            return _metrics.Keys.ToArray();
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "GetRegisteredMetrics 异常");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 重置所有指标（不改变启用状态）
    /// </summary>
    public void Reset()
    {
        try
        {
            foreach (var kv in _metrics)
            {
                try
                {
                    kv.Value.Reset();
                }
                catch (Exception ex)
                {
                    Logger.Error("Metrics", ex, "Reset 单个指标异常, Name={0}", kv.Key);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "Reset 异常");
        }
    }

    /// <summary>
    /// 完全重置：清空所有指标并设置为禁用状态（主要用于测试场景）
    ///
    /// 注意：Meter 仪器一旦创建不能销毁，但 Clear() 后
    /// 新的注册会创建新的同名仪器（Meter 内部会去重）。
    /// </summary>
    public void ResetAll()
    {
        try
        {
            _enabled = false;
            _metrics.Clear();
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "ResetAll 异常");
        }
    }

    /// <summary>
    /// 按名称获取计数器
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <returns>计数器；不存在、类型不匹配或异常时返回 null</returns>
    public ICounter? GetCounter(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            if (_metrics.TryGetValue(name, out var metric))
                return metric as ICounter;

            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "GetCounter 异常, Name={0}", name);
            return null;
        }
    }

    /// <summary>
    /// 按名称获取量规
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <returns>量规；不存在、类型不匹配或异常时返回 null</returns>
    public IGauge? GetGauge(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            if (_metrics.TryGetValue(name, out var metric))
                return metric as IGauge;

            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "GetGauge 异常, Name={0}", name);
            return null;
        }
    }

    /// <summary>
    /// 按名称获取直方图
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <returns>直方图；不存在、类型不匹配或异常时返回 null</returns>
    public IHistogram? GetHistogram(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            if (_metrics.TryGetValue(name, out var metric))
                return metric as IHistogram;

            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "GetHistogram 异常, Name={0}", name);
            return null;
        }
    }
}
