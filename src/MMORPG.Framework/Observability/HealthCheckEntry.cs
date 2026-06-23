// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

namespace MMORPG.Framework.Observability;

/// <summary>
/// 单项健康检查报告条目
/// 
/// 描述一个检查项的名称、结果以及附加描述信息。
/// </summary>
public class HealthCheckEntry
{
    /// <summary>
    /// 检查项名称（如 "tcp_listener"、"memory_usage"）
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 检查结果
    /// </summary>
    public HealthCheckResult Result { get; }

    /// <summary>
    /// 描述信息（如 "内存使用 120MB / 阈值 1024MB"）
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 构造一个健康检查条目
    /// </summary>
    /// <param name="name">检查项名称</param>
    /// <param name="result">检查结果</param>
    /// <param name="description">描述信息</param>
    public HealthCheckEntry(string name, HealthCheckResult result, string description)
    {
        Name = name;
        Result = result;
        Description = description;
    }
}
