// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Resilience;

/// <summary>
/// 重试策略配置
/// </summary>
public sealed class RetryOptions
{
    /// <summary>最大重试次数（默认：3）</summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>初始重试间隔（毫秒，默认：200ms）</summary>
    public int BaseDelayMs { get; init; } = 200;

    /// <summary>最大重试间隔（毫秒，默认：10000ms）</summary>
    public int MaxDelayMs { get; init; } = 10_000;

    /// <summary>指数退避基数（默认：2，即倍增）</summary>
    public int ExponentialBase { get; init; } = 2;

    /// <summary>是否启用抖动（Jitter）以防止惊群效应（默认：true）</summary>
    public bool UseJitter { get; init; } = true;

    /// <summary>抖动百分比（0.0 - 1.0），即在 baseDelay 的 ±percentage 范围内随机抖动（默认：0.3）</summary>
    public double JitterPercentage { get; init; } = 0.3;

    /// <summary>
    /// 判断异常是否可重试
    /// 返回 true 表示会重试，false 表示立即失败
    /// 
    /// 示例：只对 TimeoutException 和 SocketException 重试
    /// </summary>
    public Func<Exception, bool>? ShouldRetry { get; init; }

    /// <summary>
    /// 每一次重试前的回调（可记录日志）
    /// 参数：attempt（第几次重试，从1开始）, delay（本次延迟ms）, exception（导致重试的异常）
    /// </summary>
    public Action<int, int, Exception>? OnRetry { get; init; }
}

/// <summary>
/// 重试策略 - 实现指数退避 + 抖动
/// 
/// 设计参考：Polly 的 Retry 策略
/// 
/// 退避公式：
///   delay = min(BaseDelay * (ExponentialBase ^ attempt) + jitter, MaxDelayMs)
///   jitter = BaseDelay * JitterPercentage * random(-1, 1)
/// 
/// 示例（BaseDelay=200, ExponentialBase=2, JitterPercentage=0.3）：
///   attempt=1: 400 ± 60ms
///   attempt=2: 800 ± 60ms
///   attempt=3: 1600 ± 60ms
/// </summary>
public sealed class RetryPolicy
{
    private readonly RetryOptions _options;
    private readonly Random _jitterRandom = new();

    /// <summary>策略名称（用于日志）</summary>
    public string Name { get; }

    public RetryPolicy(string name, RetryOptions? options = null)
    {
        Name = name;
        _options = options ?? new RetryOptions();
    }

    /// <summary>
    /// 执行带重试的操作
    /// 
    /// 流程：
    /// 1. 第一次执行（attempt=0）
    /// 2. 失败 → 检查是否可重试（ShouldRetry）→ 计算延迟 → await Task.Delay → 重试
    /// 3. 达到 MaxRetryCount → 抛出最后一次异常
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt <= _options.MaxRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not BrokenCircuitException)
            {
                lastException = ex;

                // 检查是否已达到最大重试次数
                if (attempt >= _options.MaxRetryCount)
                {
                    Logger.Error("Resilience",
                        "重试策略 [{0}] 已达最大重试次数({1})，放弃",
                        Name, _options.MaxRetryCount);
                    throw;
                }

                // 检查异常是否可重试
                if (_options.ShouldRetry != null && !_options.ShouldRetry(ex))
                {
                    Logger.Warning("Resilience",
                        "重试策略 [{0}] 异常类型 {1} 不可重试，立即失败",
                        Name, ex.GetType().Name);
                    throw;
                }

                // -------- 计算延迟 --------
                var delayMs = CalculateDelayMs(attempt);

                // 触发重试回调
                _options.OnRetry?.Invoke(attempt + 1, delayMs, ex);

                Logger.Warning("Resilience",
                    "重试策略 [{0}] 第 {1}/{2} 次重试，等待 {3}ms，异常: {4}",
                    Name, attempt + 1, _options.MaxRetryCount, delayMs, ex.Message);

                // 等待（注意：CancellationToken 在等待期间可以被取消）
                try
                {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Logger.Warning("Resilience",
                        "重试策略 [{0}] 等待期间被取消，已执行 {1}/{2} 次",
                        Name, attempt, _options.MaxRetryCount);
                    throw;
                }
            }
        }

        // 理论上不会到达这里（达到重试上限会直接 throw）
        throw lastException ?? new InvalidOperationException("重试策略执行失败");
    }

    /// <summary>
    /// 执行带重试的操作（无返回值版本）
    /// </summary>
    public async Task ExecuteAsync(
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 计算第 attempt 次重试的延迟时间
    /// 
    /// 公式：min(BaseDelay * (ExponentialBase ^ attempt), MaxDelayMs) + jitter
    /// </summary>
    private int CalculateDelayMs(int attempt)
    {
        // 指数退避
        var exponentialDelay = _options.BaseDelayMs * (int)Math.Pow(_options.ExponentialBase, attempt);
        var cappedDelay = Math.Min(exponentialDelay, _options.MaxDelayMs);

        // 添加抖动
        if (_options.UseJitter)
        {
            var jitterRange = cappedDelay * _options.JitterPercentage;
            var jitter = (_jitterRandom.NextDouble() * 2 - 1) * jitterRange; // -jitterRange ~ +jitterRange
            return Math.Max(1, (int)(cappedDelay + jitter));
        }

        return cappedDelay;
    }
}
