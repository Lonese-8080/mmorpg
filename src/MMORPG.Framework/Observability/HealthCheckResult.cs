// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

namespace MMORPG.Framework.Observability;

/// <summary>
/// 单项健康检查结果枚举
/// 
/// 用于描述单个检查项的状态。按严重性递增排列：
/// Healthy = 0（最健康），Degraded = 1（降级），Unhealthy = 2（异常）。
/// 
/// 聚合策略：整体状态 = 所有检查项中的最坏状态（即最大枚举值）。
/// </summary>
public enum HealthCheckResult
{
    /// <summary>健康 - 检查项正常运行</summary>
    Healthy = 0,

    /// <summary>降级 - 服务仍可运行，但存在潜在问题（例如资源接近阈值）</summary>
    Degraded = 1,

    /// <summary>异常 - 服务存在严重问题，需要立即处理</summary>
    Unhealthy = 2
}
