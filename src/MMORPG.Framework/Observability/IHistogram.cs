// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 直方图接口：记录数值分布（例如延迟），支持分位数查询
/// </summary>
public interface IHistogram : IMetric
{
    /// <summary>
    /// 记录一个样本值
    /// </summary>
    /// <param name="value">样本值</param>
    void Record(double value);

    /// <summary>
    /// 获取第 50 百分位数（中位数）
    /// </summary>
    double P50 { get; }

    /// <summary>
    /// 获取第 95 百分位数
    /// </summary>
    double P95 { get; }

    /// <summary>
    /// 获取第 99 百分位数
    /// </summary>
    double P99 { get; }

    /// <summary>
    /// 当前已写入的样本数（最多为环形缓冲容量）
    /// </summary>
    int SampleCount { get; }
}
