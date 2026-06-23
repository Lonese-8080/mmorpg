// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

namespace MMORPG.Framework.Observability;

/// <summary>
/// 指标快照条目
/// 
/// 包含指标的完整信息：名称、值、类型、描述
/// 用于 Prometheus 导出、监控报告等场景
/// </summary>
public class MetricSnapshotEntry
{
    /// <summary>
    /// 指标名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 指标当前值
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// 指标类型（counter/gauge/histogram）
    /// </summary>
    public string Type { get; set; } = "unknown";

    /// <summary>
    /// 指标描述
    /// </summary>
    public string Description { get; set; } = string.Empty;
}