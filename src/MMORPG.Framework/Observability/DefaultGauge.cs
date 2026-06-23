// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics.Metrics;
using System.Threading;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 量规默认实现（基于 System.Diagnostics.Metrics 现代化实现，#10 修复）
///
/// ⚠️ 现代化说明：
/// - 底层使用 <see cref="ObservableGauge{T}"/>（System.Diagnostics.Metrics）发布指标
/// - ObservableGauge 是"拉模式"：外部监听者采集时回调获取值
/// - 同时接入 .NET 生态：dotnet-counters、OpenTelemetry、Prometheus 等工具可直接监听
///
/// 支持两种模式：
/// - Set 模式：调用 Set(double) 设置值，内部使用 volatile long 配合 BitConverter 转换 double
/// - valueProvider 模式：构造时传入 Func&lt;double&gt;，每次访问 Value 都调用该委托
///
/// 线程安全。
/// </summary>
public class DefaultGauge : IGauge
{
    /// <summary>
    /// Set 模式下的内部存储（用 long 保存 double 的二进制表示，配合 Interlocked 保证原子性）
    /// </summary>
    private long _rawValue;

    /// <summary>
    /// valueProvider 模式的取值委托（可能为 null）
    /// </summary>
    private readonly Func<double>? _valueProvider;

    /// <summary>
    /// 指标名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 指标描述
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 指标当前数值（double 形式）。
    /// 若构造时提供了 valueProvider，则调用委托；否则返回内部存储的值。
    /// </summary>
    public double Value
    {
        get
        {
            try
            {
                if (_valueProvider != null)
                    return _valueProvider();

                return BitConverter.Int64BitsToDouble(Interlocked.Read(ref _rawValue));
            }
            catch (Exception ex)
            {
                Logger.Error("Metrics", ex, "DefaultGauge.Value 获取异常, Name={0}", Name);
                return 0.0;
            }
        }
    }

    /// <summary>
    /// 构造函数（Set 模式）
    /// </summary>
    /// <param name="meter">Meter 实例，用于创建底层仪器</param>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    public DefaultGauge(Meter meter, string name, string description)
    {
        Name = name;
        Description = description;
        _rawValue = BitConverter.DoubleToInt64Bits(0.0);

        try
        {
            meter.CreateObservableGauge<double>(name, () => Value, description: description);
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultGauge 创建 ObservableGauge 失败, Name={0}", name);
        }
    }

    /// <summary>
    /// 构造函数（valueProvider 模式）
    /// </summary>
    /// <param name="meter">Meter 实例，用于创建底层仪器</param>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <param name="valueProvider">动态取值委托</param>
    public DefaultGauge(Meter meter, string name, string description, Func<double> valueProvider)
    {
        Name = name;
        Description = description;
        _valueProvider = valueProvider;
        _rawValue = BitConverter.DoubleToInt64Bits(0.0);

        try
        {
            meter.CreateObservableGauge<double>(name, () => Value, description: description);
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultGauge 创建 ObservableGauge 失败, Name={0}", name);
        }
    }

    /// <summary>
    /// 设置量规值（仅 Set 模式有内部状态影响）
    /// </summary>
    /// <param name="value">新值</param>
    public void Set(double value)
    {
        try
        {
            Interlocked.Exchange(ref _rawValue, BitConverter.DoubleToInt64Bits(value));
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultGauge.Set 异常, Name={0}, Value={1}", Name, value);
        }
    }

    /// <summary>
    /// 重置量规（仅 Set 模式，将内部值置 0）
    ///
    /// 注意：ObservableGauge 是拉模式，重置会体现在下一次采集时。
    /// </summary>
    public void Reset()
    {
        try
        {
            Interlocked.Exchange(ref _rawValue, BitConverter.DoubleToInt64Bits(0.0));
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultGauge.Reset 异常, Name={0}", Name);
        }
    }
}
