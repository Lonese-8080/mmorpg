// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics.Metrics;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 直方图默认实现（基于 System.Diagnostics.Metrics 现代化实现，#10 修复）
///
/// ⚠️ 现代化说明：
/// - 底层使用 <see cref="Histogram{T}"/>（System.Diagnostics.Metrics）发布指标
/// - 保留环形缓冲用于 P50 / P95 / P99 分位数计算（Histogram 本身不直接暴露分位数，
///   分位数聚合由外部 MeterListener 负责）
/// - 同时接入 .NET 生态：dotnet-counters、OpenTelemetry、Prometheus 等工具可直接监听
///
/// - 每次查询分位数时，拷贝一份新数组排序后计算，避免破坏环形缓冲
/// - 线程安全：使用 lock 保证操作原子性
/// </summary>
public class DefaultHistogram : IHistogram
{
    /// <summary>
    /// 环形缓冲
    /// </summary>
    private readonly double[] _buffer;

    /// <summary>
    /// 下一个写入位置
    /// </summary>
    private int _index;

    /// <summary>
    /// 已写入样本数（最多为 capacity）
    /// </summary>
    private int _count;

    /// <summary>
    /// 同步锁
    /// </summary>
    private readonly object _lockObj = new();

    /// <summary>
    /// 底层 Meter 仪器（对外发布指标，供外部监听者采集）
    /// </summary>
    private readonly Histogram<double>? _meterHistogram;

    /// <summary>
    /// 指标名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 指标描述
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 环形缓冲容量
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// 当前实际样本数
    /// </summary>
    public int SampleCount
    {
        get
        {
            lock (_lockObj)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// 指标当前数值（返回 P50）
    /// </summary>
    public double Value => P50;

    /// <summary>
    /// 第 50 百分位数（中位数）
    /// </summary>
    public double P50 => GetPercentile(0.5);

    /// <summary>
    /// 第 95 百分位数
    /// </summary>
    public double P95 => GetPercentile(0.95);

    /// <summary>
    /// 第 99 百分位数
    /// </summary>
    public double P99 => GetPercentile(0.99);

    /// <summary>
    /// 构造函数（使用默认容量 1024）
    /// </summary>
    /// <param name="meter">Meter 实例，用于创建底层仪器</param>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    public DefaultHistogram(Meter meter, string name, string description)
        : this(meter, name, description, 1024)
    {
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="meter">Meter 实例，用于创建底层仪器</param>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <param name="capacity">环形缓冲容量，必须大于 0</param>
    public DefaultHistogram(Meter meter, string name, string description, int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须大于 0");

        Name = name;
        Description = description;
        _buffer = new double[capacity];
        _index = 0;
        _count = 0;

        try
        {
            _meterHistogram = meter.CreateHistogram<double>(name, description: description);
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultHistogram 创建 Meter Histogram 失败, Name={0}", name);
            _meterHistogram = null;
        }
    }

    /// <summary>
    /// 记录一个样本值
    /// </summary>
    /// <param name="value">样本值</param>
    public void Record(double value)
    {
        try
        {
            lock (_lockObj)
            {
                _buffer[_index] = value;
                _index = (_index + 1) % _buffer.Length;
                if (_count < _buffer.Length)
                    _count++;
            }

            _meterHistogram?.Record(value);
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultHistogram.Record 异常, Name={0}, Value={1}", Name, value);
        }
    }

    /// <summary>
    /// 重置直方图（清空所有样本）
    ///
    /// 注意：Meter Histogram 不支持重置，重置仅影响本地分位数计算。
    /// 外部监听者（如 Prometheus）看到的是全量累计直方图。
    /// </summary>
    public void Reset()
    {
        try
        {
            lock (_lockObj)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _index = 0;
                _count = 0;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultHistogram.Reset 异常, Name={0}", Name);
        }
    }

    /// <summary>
    /// 计算指定百分位数
    /// </summary>
    /// <param name="percentile">百分位数（0.0~1.0）</param>
    /// <returns>对应的分位数值；若无样本则返回 0.0</returns>
    private double GetPercentile(double percentile)
    {
        try
        {
            double[] snapshot;
            int count;

            lock (_lockObj)
            {
                count = _count;
                if (count == 0)
                    return 0.0;

                snapshot = new double[count];

                if (count < _buffer.Length)
                {
                    for (var i = 0; i < count; i++)
                        snapshot[i] = _buffer[i];
                }
                else
                {
                    var capacity = _buffer.Length;
                    for (var i = 0; i < count; i++)
                        snapshot[i] = _buffer[(_index + i) % capacity];
                }
            }

            Array.Sort(snapshot);

            var idx = (int)((count - 1) * percentile);
            if (idx < 0) idx = 0;
            if (idx >= count) idx = count - 1;

            return snapshot[idx];
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "DefaultHistogram.GetPercentile 异常, Name={0}, P={1}", Name, percentile);
            return 0.0;
        }
    }
}
