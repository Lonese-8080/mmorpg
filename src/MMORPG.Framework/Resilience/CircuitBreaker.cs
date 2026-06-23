// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Resilience;

/// <summary>
/// 熔断器状态
/// 
/// 设计参考：Microsoft.Resilience 和 Steeltoe.CircuitBreaker
/// 状态机：Closed → Open → HalfOpen → Closed
/// 
/// 原理：
/// - Closed：正常请求通过，失败计数累加；超过阈值则熔断（Open）
/// - Open：请求直接失败（快速失败），等待窗口后进入 HalfOpen 试探
/// - HalfOpen：放行少量请求（= 1），成功则恢复正常（Closed），失败则继续熔断（Open）
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>Closed（正常）：所有请求通过，失败计数归零</summary>
    Closed = 0,

    /// <summary>Open（熔断）：请求直接失败，快速返回</summary>
    Open = 1,

    /// <summary>HalfOpen（半开）：试探性放行，验证下游是否恢复</summary>
    HalfOpen = 2
}

/// <summary>
/// 熔断器配置
/// </summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>熔断阈值：失败次数达到此值则触发熔断（默认：5）</summary>
    public int FailureThreshold { get; init; } = 5;

    /// <summary>熔断持续时间（毫秒）：Open 状态保持这么久后才允许转换到 HalfOpen（默认：30000ms）</summary>
    public int DurationOfBreakMs { get; init; } = 30_000;

    /// <summary>半开状态放行请求数：HalfOpen 时允许通过的请求数量（默认：1）</summary>
    public int HalfOpenMaxAttempts { get; init; } = 1;

    /// <summary>成功计数阈值：HalfOpen 状态下，成功次数达到此值则恢复 Closed（默认：1）</summary>
    public int SuccessThreshold { get; init; } = 1;

    /// <summary>最小执行时间（毫秒）：执行时间低于此值的请求不计入失败统计（避免把超时当失败，默认：500ms）</summary>
    public int MinimumExecutionTimeMs { get; init; } = 500;
}

/// <summary>
/// 熔断器 - 防止故障扩散的核心组件
/// 
/// 使用示例：
/// ```csharp
/// var circuitBreaker = new CircuitBreaker(new CircuitBreakerOptions { FailureThreshold = 5, DurationOfBreakMs = 30000 });
/// 
/// try {
///     var result = await circuitBreaker.ExecuteAsync(async () => await DownstreamService.CallAsync());
/// }
/// catch (BrokenCircuitException ex) {
///     // 熔断期间直接抛异常，快速失败
///     Logger.Warning("Resilience", "熔断中，快速失败: {0}", ex.Message);
/// }
/// ```
/// </summary>
public sealed class CircuitBreaker
{
    private readonly CircuitBreakerOptions _options;
    private readonly object _lock = new();

    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount;
    private int _halfOpenAttempts;
    private int _halfOpenSuccesses;
    private long _lastFailureTimestamp; // DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    /// <summary>熔断器名称（用于日志和调试）</summary>
    public string Name { get; }

    /// <summary>当前状态（线程安全读取）</summary>
    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                // 检查是否应该从 Open 转换到 HalfOpen
                if (_state == CircuitBreakerState.Open &&
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastFailureTimestamp >= _options.DurationOfBreakMs)
                {
                    TransitionTo(CircuitBreakerState.HalfOpen);
                }
                return _state;
            }
        }
    }

    /// <summary>失败计数（用于监控）</summary>
    public int FailureCount => _failureCount;

    public CircuitBreaker(string name, CircuitBreakerOptions? options = null)
    {
        Name = name;
        _options = options ?? new CircuitBreakerOptions();
    }

    /// <summary>
    /// 执行受保护的操作
    /// 
    /// 熔断器根据当前状态决定：
    /// - Closed：正常执行，异常时增加失败计数
    /// - Open：直接抛出 BrokenCircuitException（快速失败）
    /// - HalfOpen：允许执行，失败则重新熔断
    /// </summary>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    /// <param name="action">要执行的操作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    /// <exception cref="BrokenCircuitException">熔断期间抛出</exception>
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        // -------- 1. 状态检查（快速路径，无锁） --------
        var currentState = State;
        if (currentState == CircuitBreakerState.Open)
        {
            throw new BrokenCircuitException(
                $"熔断器 [{Name}] 处于 Open 状态，请求被拒绝",
                innerException: null);
        }

        // -------- 2. 执行操作 --------
        var stopwatch = Stopwatch.StartNew();
        bool executionSucceeded = false;
        TResult? result = default;

        try
        {
            result = await action().ConfigureAwait(false);
            executionSucceeded = true;
            return result!;
        }
        catch (Exception ex) when (ex is not BrokenCircuitException)
        {
            // -------- 3. 失败处理 --------
            lock (_lock)
            {
                var executionTimeMs = stopwatch.ElapsedMilliseconds;

                // 执行时间过短的请求不计入失败（避免把超时当失败）
                if (executionTimeMs < _options.MinimumExecutionTimeMs)
                {
                    Logger.Debug("Resilience",
                        "熔断器 [{0}] 执行时间过短({1}ms < {2}ms)，不计入失败",
                        Name, executionTimeMs, _options.MinimumExecutionTimeMs);
                    throw;
                }

                _lastFailureTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                switch (_state)
                {
                    case CircuitBreakerState.Closed:
                        _failureCount++;
                        Logger.Warning("Resilience",
                            "熔断器 [{0}] 失败 +1，当前失败计数: {1}/{2}",
                            Name, _failureCount, _options.FailureThreshold);

                        if (_failureCount >= _options.FailureThreshold)
                        {
                            Logger.Error("Resilience",
                                "熔断器 [{0}] 失败次数超限，触发熔断！持续 {1}ms",
                                Name, _options.DurationOfBreakMs);
                            TransitionTo(CircuitBreakerState.Open);
                        }
                        break;

                    case CircuitBreakerState.HalfOpen:
                        _halfOpenAttempts++;
                        Logger.Warning("Resilience",
                            "熔断器 [{0}] HalfOpen 失败，重试次数: {1}/{2}",
                            Name, _halfOpenAttempts, _options.HalfOpenMaxAttempts);

                        // HalfOpen 期间任何失败都重新熔断
                        if (_halfOpenAttempts >= _options.HalfOpenMaxAttempts)
                        {
                            Logger.Error("Resilience",
                                "熔断器 [{0}] HalfOpen 期间失败，重新熔断",
                                Name);
                            TransitionTo(CircuitBreakerState.Open);
                        }
                        break;
                }
            }
            throw; // 重新抛出原始异常
        }
        finally
        {
            // -------- 4. HalfOpen 成功处理（恢复） --------
            if (executionSucceeded)
            {
                lock (_lock)
                {
                    if (_state == CircuitBreakerState.HalfOpen)
                    {
                        _halfOpenSuccesses++;
                        Logger.Debug("Resilience",
                            "熔断器 [{0}] HalfOpen 成功 +1，{1}/{2}",
                            Name, _halfOpenSuccesses, _options.SuccessThreshold);

                        if (_halfOpenSuccesses >= _options.SuccessThreshold)
                        {
                            Logger.Info("Resilience",
                                "熔断器 [{0}] 下游已恢复，关闭熔断器",
                                Name);
                            TransitionTo(CircuitBreakerState.Closed);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 执行受保护的操作（无返回值版本）
    /// </summary>
    public async Task ExecuteAsync(
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 重置熔断器（强制回到 Closed 状态）
    /// 主要用于测试或人工干预
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Closed;
            _failureCount = 0;
            _halfOpenAttempts = 0;
            _halfOpenSuccesses = 0;
            _lastFailureTimestamp = 0;
            Logger.Info("Resilience", "熔断器 [{0}] 已重置", Name);
        }
    }

    /// <summary>
    /// 获取当前状态快照（用于监控和日志）
    /// </summary>
    public CircuitBreakerSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new CircuitBreakerSnapshot(Name, _state, _failureCount);
        }
    }

    private void TransitionTo(CircuitBreakerState newState)
    {
        if (_state == newState) return;

        Logger.Debug("Resilience", "熔断器 [{0}] 状态变更: {1} → {2}",
            Name, _state, newState);

        _state = newState;

        switch (newState)
        {
            case CircuitBreakerState.Closed:
                _failureCount = 0;
                _halfOpenAttempts = 0;
                _halfOpenSuccesses = 0;
                break;
            case CircuitBreakerState.HalfOpen:
                _halfOpenAttempts = 0;
                _halfOpenSuccesses = 0;
                break;
            case CircuitBreakerState.Open:
                _lastFailureTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                break;
        }
    }
}

/// <summary>
/// 熔断器快照（用于监控）
/// </summary>
public readonly record struct CircuitBreakerSnapshot(
    string Name,
    CircuitBreakerState State,
    int FailureCount);

/// <summary>
/// 熔断异常
/// 
/// 当熔断器处于 Open 状态时，所有请求都会抛出此异常。
/// 这实现了"快速失败"策略：不再等待下游超时，直接返回错误。
/// </summary>
public sealed class BrokenCircuitException : Exception
{
    public BrokenCircuitException(string message) : base(message) { }
    public BrokenCircuitException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
