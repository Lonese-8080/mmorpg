// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Resilience;

/// <summary>
/// 弹性策略管道 - 将熔断器、重试、超时组合成统一策略
/// 
/// 执行顺序（从外到内）：
/// Timeout → Retry → CircuitBreaker → Action
/// 
/// 设计说明：
/// - Timeout（最外层）：为整个操作设置硬性时间上限
/// - Retry（中间层）：处理瞬时故障，自动重试
/// - CircuitBreaker（最内层）：防止向已确定故障的下游发送请求
/// 
/// 为什么这个顺序：
/// - CircuitBreaker 最先拦截（Open 时直接抛 BrokenCircuitException）
/// - Retry 在 CircuitBreaker 之后（避免重试消耗已经宕机的下游）
/// - Timeout 在最外层（为整个管道设置总时间预算）
/// 
/// 使用示例：
/// ```csharp
/// var pipeline = new ResiliencePipelineBuilder()
///     .WithTimeout("DBTimeout", 3000)
///     .WithRetry("DBRetry", new RetryOptions { MaxRetryCount = 3, BaseDelayMs = 200 })
///     .WithCircuitBreaker("DBCircuit", new CircuitBreakerOptions { FailureThreshold = 5 })
///     .Build();
///
/// var result = await pipeline.ExecuteAsync(async (ct) => {
///     return await Database.QueryAsync(sql, ct);
/// });
/// ```
/// </summary>
public sealed class ResiliencePipeline
{
    private readonly TimeoutPolicy? _timeout;
    private readonly RetryPolicy? _retry;
    private readonly CircuitBreaker? _circuitBreaker;

    /// <summary>管道名称（用于日志和监控）</summary>
    public string Name { get; }

    /// <summary>是否有熔断器</summary>
    public bool HasCircuitBreaker => _circuitBreaker != null;

    /// <summary>获取熔断器快照（用于监控）</summary>
    public CircuitBreakerSnapshot? GetCircuitBreakerSnapshot()
        => _circuitBreaker?.GetSnapshot();

    public ResiliencePipeline(string name, TimeoutPolicy? timeout, RetryPolicy? retry, CircuitBreaker? circuitBreaker)
    {
        Name = name;
        _timeout = timeout;
        _retry = retry;
        _circuitBreaker = circuitBreaker;
    }

    /// <summary>
    /// 执行弹性策略保护的异步操作
    ///
    /// 典型流程（从外到内）：
    /// Timeout → Retry → CircuitBreaker → Action
    ///
    /// ⚠️ #18 修复：执行顺序与文档保持一致
    /// 1. Timeout（最外层）：为整个管道设置总时间预算
    /// 2. Retry（中间层）：处理瞬时故障，自动重试
    /// 3. CircuitBreaker（最内层）：Open 时快速失败
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        // ---- 定义核心执行函数（Action）----
        Func<CancellationToken, Task<TResult>> coreAction = async ct =>
            await action(ct).ConfigureAwait(false);

        // ---- 1. CircuitBreaker 包装（最内层）----
        Func<CancellationToken, Task<TResult>> withCircuitBreaker;
        if (_circuitBreaker != null)
        {
            withCircuitBreaker = async ct =>
                await _circuitBreaker.ExecuteAsync(
                    async () => await coreAction(ct).ConfigureAwait(false),
                    ct).ConfigureAwait(false);
        }
        else
        {
            withCircuitBreaker = coreAction;
        }

        // ---- 2. Retry 包装（中间层）----
        Func<CancellationToken, Task<TResult>> withRetry;
        if (_retry != null)
        {
            withRetry = async ct =>
                await _retry.ExecuteAsync(
                    async () => await withCircuitBreaker(ct).ConfigureAwait(false),
                    ct).ConfigureAwait(false);
        }
        else
        {
            withRetry = withCircuitBreaker;
        }

        // ---- 3. Timeout 包装（最外层）----
        if (_timeout != null)
        {
            return await _timeout.ExecuteAsync(withRetry, cancellationToken).ConfigureAwait(false);
        }

        return await withRetry(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 无返回值的执行
    /// </summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async ct =>
        {
            await action(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取管道状态的友好描述（用于日志和监控）
    /// </summary>
    public string GetStatusDescription()
    {
        var parts = new List<string> { Name };

        if (_circuitBreaker != null)
            parts.Add($"CB={_circuitBreaker.State}");

        if (_retry != null)
            parts.Add("Retry=ON");

        if (_timeout != null)
            parts.Add("Timeout=ON");

        return string.Join(" | ", parts);
    }
}

/// <summary>
/// 弹性策略构建器 - Fluent API 构建 ResiliencePipeline
/// 
/// 使用示例：
/// ```csharp
/// var pipeline = new ResiliencePipelineBuilder()
///     .WithTimeout("HttpCall", 5000)
///     .WithRetry("HttpCall", new RetryOptions { MaxRetryCount = 3 })
///     .WithCircuitBreaker("HttpCall", new CircuitBreakerOptions { FailureThreshold = 5 })
///     .Build();
/// ```
/// </summary>
public sealed class ResiliencePipelineBuilder
{
    private string _name = "Default";
    private TimeoutPolicy? _timeout;
    private RetryPolicy? _retry;
    private CircuitBreaker? _circuitBreaker;

    /// <summary>
    /// 设置管道名称
    /// </summary>
    public ResiliencePipelineBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>
    /// 添加超时策略
    /// </summary>
    public ResiliencePipelineBuilder WithTimeout(int timeoutMs)
    {
        _timeout = new TimeoutPolicy(_name, new TimeoutOptions { TimeoutMs = timeoutMs });
        return this;
    }

    /// <summary>
    /// 添加超时策略（完整配置）
    /// </summary>
    public ResiliencePipelineBuilder WithTimeout(string name, TimeoutOptions options)
    {
        _timeout = new TimeoutPolicy(name, options);
        return this;
    }

    /// <summary>
    /// 添加重试策略
    /// </summary>
    public ResiliencePipelineBuilder WithRetry(RetryOptions? options = null)
    {
        _retry = new RetryPolicy(_name, options);
        return this;
    }

    /// <summary>
    /// 添加重试策略（完整配置）
    /// </summary>
    public ResiliencePipelineBuilder WithRetry(string name, RetryOptions options)
    {
        _retry = new RetryPolicy(name, options);
        return this;
    }

    /// <summary>
    /// 添加熔断器策略
    /// </summary>
    public ResiliencePipelineBuilder WithCircuitBreaker(CircuitBreakerOptions? options = null)
    {
        _circuitBreaker = new CircuitBreaker(_name, options);
        return this;
    }

    /// <summary>
    /// 添加熔断器策略（完整配置）
    /// </summary>
    public ResiliencePipelineBuilder WithCircuitBreaker(string name, CircuitBreakerOptions options)
    {
        _circuitBreaker = new CircuitBreaker(name, options);
        return this;
    }

    /// <summary>
    /// 构建不可变的 ResiliencePipeline
    /// </summary>
    public ResiliencePipeline Build()
    {
        if (_timeout == null && _retry == null && _circuitBreaker == null)
        {
            Logger.Warning("Resilience", "弹性策略 [{0}] 未配置任何策略（Timeout/Retry/CircuitBreaker），管道将是空操作", _name);
        }

        return new ResiliencePipeline(_name, _timeout, _retry, _circuitBreaker);
    }
}

/// <summary>
/// 全局弹性策略注册表
/// 
/// 业务可以在启动时注册多个命名策略，供 MessageRouter 运行时引用。
/// 
/// 使用示例：
/// ```csharp
/// ResiliencePolicyRegistry.Register("Database", new ResiliencePipelineBuilder()
///     .WithTimeout("Database", 3000)
///     .WithRetry("Database", new RetryOptions { MaxRetryCount = 3 })
///     .WithCircuitBreaker("Database", new CircuitBreakerOptions { FailureThreshold = 5 })
///     .Build());
/// ```
/// </summary>
public static class ResiliencePolicyRegistry
{
    private static readonly Dictionary<string, ResiliencePipeline> _pipelines = new();
    private static readonly object _lock = new();

    /// <summary>
    /// 注册一个命名的弹性策略
    /// </summary>
    public static void Register(string name, ResiliencePipeline pipeline)
    {
        lock (_lock)
        {
            _pipelines[name] = pipeline;
            Logger.Info("Resilience", "弹性策略注册: {0} -> {1}", name, pipeline.GetStatusDescription());
        }
    }

    /// <summary>
    /// 获取已注册策略（如果不存在则返回 null）
    /// </summary>
    public static ResiliencePipeline? Get(string name)
    {
        lock (_lock)
        {
            return _pipelines.TryGetValue(name, out var pipeline) ? pipeline : null;
        }
    }

    /// <summary>
    /// 获取所有已注册的策略名称
    /// </summary>
    public static string[] GetRegisteredNames()
    {
        lock (_lock)
        {
            return _pipelines.Keys.ToArray();
        }
    }

    /// <summary>
    /// 获取熔断器快照（用于健康检查）
    /// </summary>
    public static Dictionary<string, CircuitBreakerSnapshot?> GetAllCircuitBreakerSnapshots()
    {
        lock (_lock)
        {
            return _pipelines.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.GetCircuitBreakerSnapshot());
        }
    }

    /// <summary>
    /// 清空注册表（主要用于测试）
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _pipelines.Clear();
        }
    }
}
