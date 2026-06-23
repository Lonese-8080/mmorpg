// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 指标基础接口
/// 
/// 所有指标类型的基接口，定义通用属性与行为。
/// </summary>
public interface IMetric
{
    /// <summary>
    /// 指标名称，用于在 MetricsCollector 中唯一标识一个指标
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 指标描述，说明该指标的用途
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 指标当前数值（double 形式）
    /// </summary>
    double Value { get; }

    /// <summary>
    /// 重置指标到初始状态
    /// </summary>
    void Reset();
}
