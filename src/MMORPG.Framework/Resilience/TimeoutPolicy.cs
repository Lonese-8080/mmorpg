// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Resilience;

/// <summary>
/// 超时策略配置
/// </summary>
public sealed class TimeoutOptions
{
    /// <summary>超时时间（毫秒，默认：5000ms）</summary>
    public int TimeoutMs { get; init; } = 5_000;

    /// <summary>
    /// 超时时执行的回调
    /// 可用于记录详细日志或指标
    /// </summary>
    public Action<string>? OnTimeout { get; init; }
}

/// <summary>
/// 超时策略 - 给操作设置硬性时间上限
/// 
/// 使用 TimeoutExtensions 实现超时控制，内部基于 Task.Delay + CancellationToken。
/// 
/// 设计原则：
/// - 区分"超时取消"和"业务异常"——超时抛 TimeoutRejectedException
/// - 协作式取消：使用 CancellationToken，允许下游感知取消并清理资源
/// </summary>
public sealed class TimeoutPolicy
{
    private readonly TimeoutOptions _options;

    /// <summary>策略名称（用于日志）</summary>
    public string Name { get; }

    public TimeoutPolicy(string name, TimeoutOptions? options = null)
    {
        Name = name;
        _options = options ?? new TimeoutOptions();
    }

    /// <summary>
    /// 执行带超时保护的操作
    /// 
    /// 超时后：
    /// 1. 触发 OnTimeout 回调（如果有）
    /// 2. 取消 CancellationToken（下游可感知）
    /// 3. 抛出 TimeoutRejectedException
    /// 
    /// 注意：超时取消是"协作式"的，下游代码需要检查 CancellationToken 才能响应取消。
    /// 对于不支持取消的操作（如外部 HTTP 调用），超时后操作仍可能在后台运行，
    /// 此时只能通过观察其副作用（部分写入、状态不一致）来发现超时。
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        // 使用 Chain 组合两个 CancellationToken：外部令牌 + 内部超时令牌
        using var timeoutCts = new CancellationTokenSource(_options.TimeoutMs);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            return await action(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // 是超时导致的取消（不是外部取消）
            _options.OnTimeout?.Invoke($"操作 [{Name}] 执行超时({_options.TimeoutMs}ms)");

            Logger.Warning("Resilience",
                "超时策略 [{0}] 执行超时({1}ms)，触发取消",
                Name, _options.TimeoutMs);

            throw new TimeoutRejectedException(
                $"操作 [{Name}] 执行超时({_options.TimeoutMs}ms)",
                ex);
        }
        finally
        {
            linkedCts.Dispose();
        }
    }

    /// <summary>
    /// 执行带超时保护的操作（无额外参数版本）
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
    /// ExecuteAsync 的简化版本（action 不接收 CancellationToken）
    /// 内部使用超时 CancellationToken，但不传递给下游
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(_ => action(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 无返回值版本
    /// </summary>
    public async Task ExecuteAsync(
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(_ => action(), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// 超时被拒绝异常
/// 
/// 当 TimeoutPolicy 检测到操作超时时会抛出此异常。
/// 这是"快速失败"的体现：不再等待，直接报告超时。
/// </summary>
public sealed class TimeoutRejectedException : Exception
{
    public TimeoutRejectedException(string message) : base(message) { }
    public TimeoutRejectedException(string message, Exception? inner) : base(message, inner) { }
}
