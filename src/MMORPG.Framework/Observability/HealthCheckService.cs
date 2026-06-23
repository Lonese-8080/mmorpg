// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using MMORPG.Framework.Logging;
using MMORPG.Framework.Network;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 健康检查服务（单例）
/// 
/// 提供对服务状态的定期检查，支持：
/// - TCP 监听状态
/// - 当前会话数
/// - 内存使用量
/// - 消息队列积压 等
/// 
/// 使用 "聚合最坏状态" 策略：
/// Healthy(0) 小于 Degraded(1) 小于 Unhealthy(2)，
/// 整体状态取所有检查项中的最大枚举值。
/// 
/// 默认不自动注册任何检查项，由业务层根据需要注册。
/// 可通过 <see cref="HealthCheckServiceExtensions"/> 提供的扩展方法
/// 快速注册常见检查项（内存、会话数）。
/// </summary>
public class HealthCheckService
{
    /// <summary>
    /// 单例实例
    /// </summary>
    public static HealthCheckService Instance { get; } = new();

    /// <summary>
    /// 已注册的检查项列表：名称 + 异步检查委托
    /// 
    /// 使用锁保护读写操作，保证线程安全。
    /// </summary>
    private readonly List<(string Name, Func<CancellationToken, Task<(HealthCheckResult Result, string Description)>> Checker)> _checkers = new();

    /// <summary>
    /// 检查项读写锁
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// ⚠️ 性能优化（#19 修复）：缓存最近一次健康检查结果
    ///
    /// 避免每次 HTTP 请求都重新执行所有检查项
    /// 缓存有效期：_cacheLifetime（默认 10 秒）
    /// </summary>
    private HealthCheckStatus? _cachedStatus;
    private DateTime _cacheTime;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromSeconds(10);
    private readonly object _cacheLock = new();

    /// <summary>
    /// 私有构造函数（保证单例）
    /// </summary>
    private HealthCheckService()
    {
    }

    /// <summary>
    /// 当前缓存的检查结果（仅用于测试/诊断）
    /// </summary>
    public HealthCheckStatus? CachedStatus
    {
        get
        {
            lock (_cacheLock)
            {
                return _cachedStatus;
            }
        }
    }

    /// <summary>
    /// 注册一个异步健康检查项
    /// 
    /// 委托返回 (Result, Description)，
    /// 若委托内部抛异常，会被 CheckHealthAsync 捕获并标记为 Unhealthy。
    /// </summary>
    /// <param name="name">检查项名称</param>
    /// <param name="checker">异步检查委托</param>
    public void Register(string name, Func<CancellationToken, Task<(HealthCheckResult Result, string Description)>> checker)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Logger.Warning("HealthCheck", "Register 名称为空，已忽略");
            return;
        }
        if (checker == null)
        {
            Logger.Warning("HealthCheck", "Register 检查委托为 null，已忽略，Name={0}", name);
            return;
        }

        lock (_lock)
        {
            _checkers.Add((name, checker));
        }
    }

    /// <summary>
    /// 注册一个同步健康检查项（内部转换为异步）
    /// </summary>
    /// <param name="name">检查项名称</param>
    /// <param name="syncChecker">同步检查委托，返回健康检查结果</param>
    /// <param name="description">描述信息（可选，若为 null 则使用空字符串）</param>
    public void Register(string name, Func<HealthCheckResult> syncChecker, string? description = null)
    {
        if (syncChecker == null)
        {
            Logger.Warning("HealthCheck", "Register 同步委托为 null，已忽略，Name={0}", name);
            return;
        }

        Register(name, ct =>
        {
            try
            {
                var result = syncChecker();
                return Task.FromResult((result, description ?? string.Empty));
            }
            catch (Exception ex)
            {
                Logger.Error("HealthCheck", ex, "同步健康检查项异常，Name={0}", name);
                return Task.FromResult((HealthCheckResult.Unhealthy, ex.Message));
            }
        });
    }

    /// <summary>
    /// 并行执行所有已注册的健康检查项，并返回聚合报告
    /// 
    /// 聚合策略：OverallStatus = 所有条目中最大的枚举值
    /// （即最坏状态）。
    /// 
    /// ⚠️ 性能优化（#19 修复）：若距上次检查时间在 _cacheLifetime 之内，
    /// 直接返回缓存结果，避免短时间内重复执行昂贵的检查。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="bypassCache">是否绕过缓存（强制重新检查），默认 false</param>
    public async Task<HealthCheckStatus> CheckHealthAsync(CancellationToken cancellationToken = default, bool bypassCache = false)
    {
        if (!bypassCache)
        {
            lock (_cacheLock)
            {
                if (_cachedStatus != null && DateTime.UtcNow - _cacheTime < _cacheLifetime)
                {
                    return _cachedStatus;
                }
            }
        }

        var status = await RunChecksCoreAsync(cancellationToken);

        lock (_cacheLock)
        {
            _cachedStatus = status;
            _cacheTime = DateTime.UtcNow;
        }

        return status;
    }

    /// <summary>
    /// 实际执行所有检查项（不读/写缓存）
    /// </summary>
    private async Task<HealthCheckStatus> RunChecksCoreAsync(CancellationToken cancellationToken)
    {
        List<(string Name, Func<CancellationToken, Task<(HealthCheckResult Result, string Description)>> Checker)> snapshot;
        lock (_lock)
        {
            snapshot = _checkers.ToList();
        }

        var entries = new HealthCheckEntry[snapshot.Count];
        var tasks = new Task<(HealthCheckResult Result, string Description, int Index)>[snapshot.Count];

        for (int i = 0; i < snapshot.Count; i++)
        {
            int index = i;
            var (name, checker) = snapshot[i];
            tasks[i] = RunCheckerAsync(name, checker, index, cancellationToken);
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception)
        {
            // 单个异常已在 RunCheckerAsync 内部捕获，这里仅作兜底
        }

        var overall = HealthCheckResult.Healthy;
        for (int i = 0; i < tasks.Length; i++)
        {
            var (result, description, index) = tasks[i].IsCompletedSuccessfully
                ? tasks[i].Result
                : (HealthCheckResult.Unhealthy, tasks[i].Exception?.Message ?? "未知错误", i);

            var (name, _) = snapshot[index];
            entries[index] = new HealthCheckEntry(name, result, description);

            if (result > overall)
                overall = result;
        }

        return new HealthCheckStatus(overall, entries);
    }

    /// <summary>
    /// 强制使缓存失效（下一次 CheckHealthAsync 将重新执行所有检查项）
    /// </summary>
    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedStatus = null;
        }
    }

    /// <summary>
    /// 便捷方法：执行检查并渲染为文本
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task<string> RenderTextAsync(CancellationToken ct = default)
    {
        var status = await CheckHealthAsync(ct);
        return status.RenderText();
    }

    /// <summary>
    /// 便捷方法：执行检查并渲染为 JSON
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task<string> RenderJsonAsync(CancellationToken ct = default)
    {
        var status = await CheckHealthAsync(ct);
        return status.RenderJson();
    }

    /// <summary>
    /// 清空所有已注册的检查项（主要用于测试场景）
    /// ⚠️ #19 修复：同时使缓存失效，确保下一次 CheckHealthAsync 重新执行
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _checkers.Clear();
        }
        InvalidateCache();
    }

    /// <summary>
    /// 执行单个检查项（包含异常捕获）
    /// </summary>
    private static async Task<(HealthCheckResult Result, string Description, int Index)> RunCheckerAsync(
        string name,
        Func<CancellationToken, Task<(HealthCheckResult Result, string Description)>> checker,
        int index,
        CancellationToken cancellationToken)
    {
        try
        {
            var (result, description) = await checker(cancellationToken);
            return (result, description, index);
        }
        catch (Exception ex)
        {
            Logger.Error("HealthCheck", ex, "健康检查项异常，Name={0}", name);
            return (HealthCheckResult.Unhealthy, ex.Message, index);
        }
    }
}

/// <summary>
/// 健康检查服务扩展方法
/// 
/// 提供常见检查项的便捷注册方式，如内存使用量、会话数。
/// </summary>
public static class HealthCheckServiceExtensions
{
    /// <summary>
    /// 注册内存使用量检查
    /// 
    /// 基于 <see cref="GC.GetTotalAllocatedBytes(bool)"/> 进行估算：
    /// - 低于阈值 75%：Healthy
    /// - 低于阈值但高于 75%：Degraded
    /// - 达到或超过阈值：Unhealthy
    /// </summary>
    /// <param name="service">健康检查服务</param>
    /// <param name="memoryThresholdBytes">内存阈值（字节），默认 2GB</param>
    public static void AddMemoryCheck(this HealthCheckService service, long memoryThresholdBytes = 2L * 1024 * 1024 * 1024)
    {
        service.Register("memory_usage", ct =>
        {
            var current = GC.GetTotalAllocatedBytes(false);
            HealthCheckResult result;
            string desc;
            if (current < memoryThresholdBytes * 0.75)
            {
                result = HealthCheckResult.Healthy;
                desc = $"当前 {current / 1048576L} MB / 阈值 {memoryThresholdBytes / 1048576L} MB";
            }
            else if (current < memoryThresholdBytes)
            {
                result = HealthCheckResult.Degraded;
                desc = $"当前 {current / 1048576L} MB，接近阈值 {memoryThresholdBytes / 1048576L} MB";
            }
            else
            {
                result = HealthCheckResult.Unhealthy;
                desc = $"当前 {current / 1048576L} MB 超过阈值 {memoryThresholdBytes / 1048576L} MB";
            }
            return Task.FromResult((result, desc));
        });
    }

    /// <summary>
    /// 注册活动会话数检查
    /// 
    /// 根据 <see cref="TcpServer.ConnectionCount"/> 判断：
    /// - 低于 degradedThreshold：Healthy
    /// - 低于 unhealthyThreshold 但 >= degradedThreshold：Degraded
    /// - 达到或超过 unhealthyThreshold：Unhealthy
    /// </summary>
    /// <param name="service">健康检查服务</param>
    /// <param name="server">TcpServer 实例</param>
    /// <param name="degradedThreshold">降级阈值（默认 500）</param>
    /// <param name="unhealthyThreshold">异常阈值（默认 900）</param>
    public static void AddSessionCountCheck(this HealthCheckService service, TcpServer server, int degradedThreshold = 500, int unhealthyThreshold = 900)
    {
        if (server == null)
        {
            Logger.Warning("HealthCheck", "AddSessionCountCheck 参数 server 为 null，已忽略");
            return;
        }

        service.Register("session_count", ct =>
        {
            int count = server.ConnectionCount;
            HealthCheckResult result;
            string desc;
            if (count < degradedThreshold)
            {
                result = HealthCheckResult.Healthy;
                desc = $"当前连接 {count}";
            }
            else if (count < unhealthyThreshold)
            {
                result = HealthCheckResult.Degraded;
                desc = $"当前连接 {count}，接近上限";
            }
            else
            {
                result = HealthCheckResult.Unhealthy;
                desc = $"当前连接 {count} 超过阈值 {unhealthyThreshold}";
            }
            return Task.FromResult((result, desc));
        });
    }
}
