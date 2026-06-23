// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Threading;

namespace MMORPG.Framework.Security;

/// <summary>
/// 消息限流器 - 令牌桶算法
/// 
/// 工作原理：
/// 1. 初始化时指定每秒最大消息数（maxMessagesPerSecond）
/// 2. 每收到一条消息尝试获取一个令牌
/// 3. 令牌数量不足时拒绝消息
/// 4. 令牌每秒自动补充到最大容量
/// 
/// 使用方式：
/// <code>
/// var limiter = new RateLimiter(100); // 每秒 100 条
/// if (limiter.TryAcquire())
/// {
///     // 处理消息
/// }
/// else
/// {
///     // 丢弃消息
/// }
/// </code>
/// </summary>
public class RateLimiter
{
    /// <summary>每秒最大消息数</summary>
    private readonly long _maxTokens;

    /// <summary>当前可用令牌数（原子操作）</summary>
    private long _currentTokens;

    /// <summary>最后一次补充令牌的时间（UTC Tick）</summary>
    private long _lastRefillTicks;

    /// <summary>总共拒绝的消息数（原子操作，用于监控）</summary>
    private long _droppedCount;

    /// <summary>最后一次拒绝消息的时间（UTC）</summary>
    private DateTime _lastDroppedTime;

    /// <summary>
    /// 构造限流器
    /// </summary>
    /// <param name="maxMessagesPerSecond">每秒最大消息数</param>
    public RateLimiter(long maxMessagesPerSecond)
    {
        if (maxMessagesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMessagesPerSecond), "每秒消息数必须为正整数");

        _maxTokens = maxMessagesPerSecond;
        _currentTokens = maxMessagesPerSecond;
        _lastRefillTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// 尝试获取一个令牌（线程安全）
    /// </summary>
    /// <returns>true = 允许处理，false = 超过限流</returns>
    public bool TryAcquire()
    {
        return TryAcquire(1);
    }

    /// <summary>
    /// 尝试获取 count 个令牌（线程安全）
    /// </summary>
    /// <param name="count">需要获取的令牌数</param>
    /// <returns>true = 允许处理，false = 超过限流</returns>
    public bool TryAcquire(int count)
    {
        if (count <= 0) return true;

        // 1. 补充令牌（基于时间流逝的比例）
        RefillTokens();

        // 2. 尝试获取令牌
        long current = Interlocked.Read(ref _currentTokens);
        if (current >= count)
        {
            Interlocked.Add(ref _currentTokens, -count);
            return true;
        }

        // 3. 令牌不足，拒绝
        Interlocked.Increment(ref _droppedCount);
        _lastDroppedTime = DateTime.UtcNow;
        return false;
    }

    /// <summary>
    /// 根据时间流逝补充令牌
    ///
    /// ⚠️ 溢出保护：
    /// - 限制 secondsElapsed 最大值为 3600（1 小时），避免长时间未调用时溢出
    /// - 使用 checked 确保计算溢出时抛出异常而非静默失败
    /// </summary>
    private void RefillTokens()
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        long lastTicks = Interlocked.Read(ref _lastRefillTicks);
        long elapsedMs = (nowTicks - lastTicks) / TimeSpan.TicksPerMillisecond;

        if (elapsedMs <= 0) return;

        // 按比例补充令牌：每秒补充 _maxTokens 个
        // 简化实现：整秒补充
        long secondsElapsed = elapsedMs / 1000;
        if (secondsElapsed > 0)
        {
            // 溢出保护：限制最大补充时间为 1 小时
            // 如果服务器长时间未处理请求（如停机后重启），补充太多令牌没有意义
            secondsElapsed = Math.Min(secondsElapsed, 3600);

            // 使用 checked 确保溢出时抛出异常（而非静默失败）
            try
            {
                long refillAmount = checked(secondsElapsed * _maxTokens);
                long currentTokens = Interlocked.Read(ref _currentTokens);
                long newTokens = Math.Min(_maxTokens, currentTokens + refillAmount);
                Interlocked.Exchange(ref _currentTokens, newTokens);
                Interlocked.Exchange(ref _lastRefillTicks, lastTicks + checked(secondsElapsed * 1000 * TimeSpan.TicksPerMillisecond));
            }
            catch (OverflowException)
            {
                // 溢出时直接重置为最大值（极端情况下的安全兜底）
                Interlocked.Exchange(ref _currentTokens, _maxTokens);
                Interlocked.Exchange(ref _lastRefillTicks, nowTicks);
            }
        }
    }

    /// <summary>
    /// 当前可用令牌数（线程安全）
    /// </summary>
    public long CurrentTokens => Interlocked.Read(ref _currentTokens);

    /// <summary>
    /// 总共拒绝的消息数（线程安全）
    /// </summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>
    /// 最后一次拒绝消息的时间（UTC）
    /// </summary>
    public DateTime LastDroppedTime => _lastDroppedTime;

    /// <summary>
    /// 每秒最大消息数
    /// </summary>
    public long MaxMessagesPerSecond => _maxTokens;

    /// <summary>
    /// 重置限流器状态（主要用于测试场景）
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _currentTokens, _maxTokens);
        Interlocked.Exchange(ref _droppedCount, 0);
        Interlocked.Exchange(ref _lastRefillTicks, DateTime.UtcNow.Ticks);
    }
}
