// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics.Metrics;
using System.Threading;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 计数器默认实现（基于 System.Diagnostics.Metrics 现代化实现，#10 修复）
///
/// ⚠️ 现代化说明：
/// - 底层使用 <see cref="Counter{T}"/>（System.Diagnostics.Metrics）发布指标
/// - 本地保留 Interlocked 跟踪计数值，用于 <see cref="Count"/> 属性读取
/// - 同时接入 .NET 生态：dotnet-counters、OpenTelemetry、Prometheus 等工具可直接监听
///
/// 线程安全：使用 Interlocked 保证原子性。
/// </summary>
public class DefaultCounter : ICounter
{
    /// <summary>
    /// 本地计数值（用于 Count 属性读取）
    /// </summary>
    private long _count;

    /// <summary>
    /// 底层 Meter 仪器（对外发布指标，供外部监听者采集）
    /// </summary>
    private readonly Counter<long>? _meterCounter;

    /// <summary>
    /// 指标名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 指标描述
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 当前计数值（线程安全读取）
    /// </summary>
    public long Count => Interlocked.Read(ref _count);

    /// <summary>
    /// 指标当前数值（double 形式）
    /// </summary>
    public double Value => Count;

    /// <summary>
    /// 构造函数（不绑定 Meter，发布指标）
    /// </summary>
    /// <param name="meter">Meter 实例，用于创建底层仪器</param>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    public DefaultCounter(Meter meter, string name, string description)
    {
        Name = name;
        Description = description;
        try
        {
            _meterCounter = meter.CreateCounter<long>(name, description: description);
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultCounter 创建 Meter Counter 失败, Name={0}", name);
            _meterCounter = null;
        }
    }

    /// <summary>
    /// 计数器加 1
    /// </summary>
    public void Increment()
    {
        try
        {
            Interlocked.Increment(ref _count);
            _meterCounter?.Add(1);
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultCounter.Increment 异常, Name={0}", Name);
        }
    }

    /// <summary>
    /// 计数器按指定值增加
    /// </summary>
    /// <param name="value">要增加的值</param>
    public void IncrementBy(long value)
    {
        try
        {
            Interlocked.Add(ref _count, value);
            if (value != 0)
                _meterCounter?.Add(value);
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultCounter.IncrementBy 异常, Name={0}, Value={1}", Name, value);
        }
    }

    /// <summary>
    /// 重置计数器到 0
    ///
    /// 注意：Meter Counter 不支持重置（是单调递增的），
    /// 重置仅影响本地 Count 属性读取值。
    /// 外部监听者（如 Prometheus）看到的是全量累计值。
    /// </summary>
    public void Reset()
    {
        try
        {
            Interlocked.Exchange(ref _count, 0);
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultCounter.Reset 异常, Name={0}", Name);
        }
    }
}
