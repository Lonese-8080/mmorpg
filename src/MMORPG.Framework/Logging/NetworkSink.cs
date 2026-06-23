// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

// ====================================================================
// 网络日志输出 - NetworkSink
//
// 负责将日志发送到远程日志服务器（如 ELK / Loki / 自定义日志中心）
// 使用 HTTP POST 发送 JSON 格式的日志条目
//
// 设计要点：
// 1. 异步发送，不阻塞主线程
// 2. 批量发送，减少网络开销
// 3. 失败重试，避免网络抖动导致日志丢失
// 4. 本地缓存，发送失败时保留在内存队列中
//
// 注意：
// - 网络日志主要用于生产环境的集中式日志管理
// - 在开发环境默认不启用（EnableNetwork = false）
// - 发送失败不会抛异常，只会记录到本地日志，避免影响业务逻辑
// ====================================================================

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace MMORPG.Framework.Logging;

/// <summary>
/// 网络日志输出
///
/// 通过 HTTP POST 将日志以 JSON 格式发送到远程服务器。
/// 使用内部队列 + 后台发送线程实现批量发送。
///
/// 使用示例（通过 Logger.Initialize 启用）：
/// <code>
/// Logger.Initialize(new LogOptions
/// {
///     EnableNetwork = true,
///     NetworkEndpoint = "http://logserver:8080/log"
/// });
/// </code>
/// </summary>
public sealed class NetworkSink : ILogSink
{
    #region 常量

    /// <summary>
    /// 单批次最大发送条目数
    /// </summary>
    private const int BatchSize = 50;

    /// <summary>
    /// 批量发送最大等待时间（毫秒）
    /// 无论是否攒够 BatchSize 条，到这个时间就发送一次
    /// </summary>
    private const int BatchTimeoutMs = 1000;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    private const int MaxRetries = 3;

    /// <summary>
    /// 队列最大容量
    /// 超过此容量的日志会被丢弃（避免内存泄漏）
    /// </summary>
    private const int QueueCapacity = 10000;

    #endregion

    #region 字段

    /// <summary>
    /// 远程日志服务器地址（例如 http://logserver:8080/log）
    /// </summary>
    private readonly string _endpoint;

    /// <summary>
    /// HTTP 客户端（单例复用，避免 Socket 耗尽）
    /// </summary>
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    /// <summary>
    /// 待发送的日志队列
    /// </summary>
    private readonly ConcurrentQueue<LogEntry> _queue = new();

    /// <summary>
    /// 发送控制信号
    /// 当有新日志加入时触发，告诉后台线程开始工作
    /// </summary>
    private readonly SemaphoreSlim _signal = new(0);

    /// <summary>
    /// CancellationTokenSource，用于停止后台线程
    /// </summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 后台发送任务
    /// </summary>
    private readonly Task _sendTask;

    /// <summary>
    /// 统计：发送失败次数
    /// 用于偶尔记录一次"网络日志发送失败"的提示
    /// </summary>
    private long _failedCount;

    /// <summary>
    /// 是否已释放
    /// </summary>
    private bool _disposed;

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建网络日志输出
    /// </summary>
    /// <param name="endpoint">远程日志服务器 URL（如 http://logserver:8080/log）</param>
    public NetworkSink(string endpoint)
    {
        _endpoint = endpoint;

        // 启动后台发送线程
        _sendTask = Task.Run(SendLoopAsync);
    }

    #endregion

    #region ILogSink 实现

    /// <summary>
    /// 将日志条目加入发送队列
    ///
    /// 注意：此方法只是入队，不立即发送。
    /// 真正的网络发送由后台线程 SendLoopAsync 执行。
    /// </summary>
    public Task WriteAsync(LogEntry entry)
    {
        if (_disposed)
            return Task.CompletedTask;

        // 队列容量检查：超过上限就丢弃最老的日志
        if (_queue.Count >= QueueCapacity)
        {
            Interlocked.Increment(ref _failedCount);
            return Task.CompletedTask;
        }

        _queue.Enqueue(entry);

        // 唤醒后台发送线程（最多释放 1 个信号，避免多次 Release）
        if (_signal.CurrentCount == 0)
        {
            _signal.Release();
        }

        return Task.CompletedTask;
    }

    #endregion

    #region 后台发送循环

    /// <summary>
    /// 后台发送循环
    ///
    /// 工作流程：
    /// 1. 等待信号（有新日志或到达 BatchTimeoutMs）
    /// 2. 收集最多 BatchSize 条日志
    /// 3. 序列化为 JSON 数组
    /// 4. 通过 HTTP POST 发送到远程服务器
    /// 5. 发送失败时按指数退避重试
    /// </summary>
    private async Task SendLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                // 等待信号：有新日志到达，或定期发送
                await _signal.WaitAsync(BatchTimeoutMs, _cts.Token);

                // 收集一批日志
                var batch = new List<LogEntry>(BatchSize);
                while (batch.Count < BatchSize && _queue.TryDequeue(out var entry))
                {
                    batch.Add(entry);
                }

                if (batch.Count == 0)
                    continue;

                // 发送（最多重试 MaxRetries 次）
                await SendBatchAsync(batch);
            }
            catch (OperationCanceledException)
            {
                // 正常退出
                break;
            }
            catch (Exception ex)
            {
                // 发送循环本身出问题，静默处理（避免日志系统异常影响业务）
                Interlocked.Increment(ref _failedCount);

                // 偶尔输出一条提示（不要每条失败都打日志，会形成日志风暴）
                if (_failedCount % 100 == 1)
                {
                    try
                    {
                        Console.Error.WriteLine(
                            $"[NetworkSink] 网络日志发送异常，累计失败 {_failedCount} 次: {ex.Message}");
                    }
                    catch { /* 忽略，防止极端情况 Console 也出错 */ }
                }
            }
        }
    }

    /// <summary>
    /// 发送一批日志
    /// 带指数退避重试
    /// </summary>
    private async Task SendBatchAsync(List<LogEntry> batch)
    {
        // 序列化为 JSON
        var json = JsonSerializer.Serialize(batch);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // 指数退避重试：100ms → 200ms → 400ms
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var response = await _httpClient.PostAsync(_endpoint, content, _cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    return; // 发送成功
                }

                // HTTP 错误：记录一次，但继续重试
                Interlocked.Increment(ref _failedCount);
            }
            catch
            {
                // 网络异常：继续重试
                Interlocked.Increment(ref _failedCount);
            }

            // 指数退避等待
            if (attempt < MaxRetries - 1)
            {
                int delay = (int)Math.Pow(2, attempt) * 100;
                await Task.Delay(delay, _cts.Token);
            }
        }

        // 所有重试都失败：丢弃这批日志，累计失败计数
        Interlocked.Add(ref _failedCount, batch.Count);
    }

    #endregion

    #region IAsyncDisposable 实现

    /// <summary>
    /// 释放资源
    /// 停止后台发送线程，发送剩余日志
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // 发送取消信号
        _cts.Cancel();

        try
        {
            // 等待后台线程完成（最多等 3 秒，避免关机卡住）
            await _sendTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // 忽略
        }
        finally
        {
            _signal.Dispose();
            _cts.Dispose();
        }
    }

    #endregion
}
