// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 计数器接口：只增不减的数值指标（例如累计消息数）
/// </summary>
public interface ICounter : IMetric
{
    /// <summary>
    /// 计数器加 1
    /// </summary>
    void Increment();

    /// <summary>
    /// 计数器按指定值增加
    /// </summary>
    /// <param name="value">要增加的值</param>
    void IncrementBy(long value);

    /// <summary>
    /// 当前计数值
    /// </summary>
    long Count { get; }
}
