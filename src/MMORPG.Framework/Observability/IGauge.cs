// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 量规接口：返回动态瞬时值（例如当前在线会话数、当前内存占用）
/// </summary>
public interface IGauge : IMetric
{
    /// <summary>
    /// 设置量规值
    /// </summary>
    /// <param name="value">新值</param>
    void Set(double value);
}
