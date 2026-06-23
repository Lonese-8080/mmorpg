// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using MMORPG.Framework.Logging;
using MMORPG.Framework.Observability;

namespace MMORPG.Framework.Network;

/// <summary>
/// 消息重试队列
/// 
/// 实现发送失败消息的自动重试功能
/// 使用指数退避策略，避免瞬时错误导致消息丢失
/// 
/// 工作流程：
/// 1. 消息发送失败 → 放入重试队列
/// 2. 等待指数退避时间
/// 3. 重新尝试发送
/// 4. 达到最大重试次数 → 放入死信队列（DLQ）
/// 
/// 使用示例：
/// <code>
/// var retryQueue = new MessageRetryQueue(new MessageRetryOptions
/// {
///     MaxRetryCount = 3,
///     BaseIntervalMs = 100,
///     MaxIntervalMs = 5000
/// });
/// retryQueue.Start();
/// 
/// // 发送失败时
/// retryQueue.EnqueueForRetry(session, messageData, failureReason);
/// </code>
/// </summary>
public class MessageRetryQueue
{
    #region 数据结构

    /// <summary>
    /// 待重试的消息项
    /// </summary>
    private class RetryItem
    {
        /// <summary>
        /// 关联的 Session
        /// </summary>
        public Session Session { get; }

        /// <summary>
        /// 要重试发送的消息数据（已序列化的完整数据包）
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// 首次失败时间
        /// </summary>
        public DateTime FirstFailedAt { get; }

        /// <summary>
        /// 下次可重试时间
        /// </summary>
        public DateTime NextRetryAt { get; set; }

        /// <summary>
        /// 失败原因
        /// </summary>
        public string FailureReason { get; }

        public RetryItem(Session session, byte[] data, string failureReason)
        {
            Session = session;
            Data = data;
            RetryCount = 0;
            FirstFailedAt = DateTime.UtcNow;
            NextRetryAt = DateTime.UtcNow;
            FailureReason = failureReason;
        }
    }

    #endregion

    #region 字段

    /// <summary>
    /// 待重试的消息队列
    /// </summary>
    private readonly ConcurrentQueue<RetryItem> _retryQueue;

    /// <summary>
    /// 死信队列（达到最大重试次数后存放）
    /// </summary>
    private readonly ConcurrentQueue<DeadLetterItem> _deadLetterQueue;

    /// <summary>
    /// 后台处理任务
    /// </summary>
    private Task? _processingTask;

    /// <summary>
    /// 取消令牌
    /// </summary>
    private readonly CancellationTokenSource _cts;

    /// <summary>
    /// 配置
    /// </summary>
    private readonly MessageRetryOptions _options;

    /// <summary>
    /// 是否正在运行
    /// </summary>
    private bool _isRunning;

    /// <summary>
    /// 重试计数
    /// </summary>
    private long _totalRetryAttempts;

    /// <summary>
    /// 成功重试计数
    /// </summary>
    private long _successfulRetries;

    /// <summary>
    /// 死信计数
    /// </summary>
    private long _deadLetterCount;

    #endregion

    #region 属性

    /// <summary>
    /// 当前待重试的消息数量
    /// </summary>
    public int PendingCount => _retryQueue.Count;

    /// <summary>
    /// 死信队列数量
    /// </summary>
    public int DeadLetterCount => _deadLetterQueue.Count;

    /// <summary>
    /// 总尝试次数
    /// </summary>
    public long TotalRetryAttempts => _totalRetryAttempts;

    /// <summary>
    /// 成功的重试次数
    /// </summary>
    public long SuccessfulRetries => _successfulRetries;

    #endregion

    #region 构造函数

    /// <summary>
    /// 构造消息重试队列
    /// </summary>
    public MessageRetryQueue(MessageRetryOptions? options = null)
    {
        _options = options ?? new MessageRetryOptions();
        _retryQueue = new ConcurrentQueue<RetryItem>();
        _deadLetterQueue = new ConcurrentQueue<DeadLetterItem>();
        _cts = new CancellationTokenSource();
        _isRunning = false;
        _totalRetryAttempts = 0;
        _successfulRetries = 0;
        _deadLetterCount = 0;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 启动后台处理
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _processingTask = Task.Run(ProcessingLoop);

        // 注册指标
        try
        {
            MetricsCollector.Instance.RegisterGauge("retry.pending_count", "待重试消息数量");
            MetricsCollector.Instance.RegisterGauge("retry.dead_letter_count", "死信消息数量");
            MetricsCollector.Instance.RegisterCounter("retry.total_attempts", "总重试尝试次数");
            MetricsCollector.Instance.RegisterCounter("retry.successful_count", "成功的重试次数");
        }
        catch { }

        Logger.Info("Network", "消息重试队列已启动: MaxRetryCount={0}, BaseIntervalMs={1}",
            _options.MaxRetryCount, _options.BaseIntervalMs);
    }

    /// <summary>
    /// 停止处理
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _cts.Cancel();

        try
        {
            _processingTask?.Wait(2000);
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "等待消息重试队列停止异常");
        }

        Logger.Info("Network", "消息重试队列已停止: Pending={0}, DeadLetter={1}",
            _retryQueue.Count, _deadLetterQueue.Count);
    }

    /// <summary>
    /// 将发送失败的消息入队进行重试
    /// </summary>
    /// <param name="session">关联的 Session（可以为 null，表示会话已断开）</param>
    /// <param name="data">消息数据</param>
    /// <param name="failureReason">失败原因</param>
    public void EnqueueForRetry(Session? session, byte[] data, string failureReason)
    {
        if (!_isRunning)
            return;

        if (data == null || data.Length == 0)
            return;

        // Session 已断开的消息不重试
        if (session == null || !session.IsConnected)
        {
            Logger.Warning("Network", "消息发送失败但 Session 已断开，放弃重试: Reason={0}",
                failureReason);
            return;
        }

        var item = new RetryItem(session, data, failureReason)
        {
            NextRetryAt = DateTime.UtcNow.AddMilliseconds(_options.BaseIntervalMs)
        };
        _retryQueue.Enqueue(item);

        // 更新指标
        UpdateMetrics();

        Logger.Debug("Network", "消息入队重试: Reason={0}, Pending={1}",
            failureReason, _retryQueue.Count);
    }

    /// <summary>
    /// 获取死信队列中的消息
    /// 
    /// 调用方可以：
    /// 1. 记录日志
    /// 2. 写入数据库
    /// 3. 发送通知给管理员
    /// </summary>
    /// <param name="maxCount">最多取出数量</param>
    /// <returns>死信消息列表</returns>
    public List<DeadLetterItem> DrainDeadLetter(int maxCount = 100)
    {
        var result = new List<DeadLetterItem>(Math.Min(maxCount, _deadLetterQueue.Count));
        int count = 0;

        while (count < maxCount && _deadLetterQueue.TryDequeue(out var item))
        {
            result.Add(item);
            count++;
        }

        if (count > 0)
        {
            _deadLetterCount -= count;
            Logger.Warning("Network", "从死信队列取出 {0} 条消息", count);
        }

        return result;
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public string GetStats()
    {
        return string.Format(
            "MessageRetryQueue[Pending={0}, DeadLetter={1}, TotalAttempts={2}, Successful={3}]",
            PendingCount, DeadLetterCount,
            Interlocked.Read(ref _totalRetryAttempts),
            Interlocked.Read(ref _successfulRetries));
    }

    #endregion

    #region 私有方法 - 处理循环

    /// <summary>
    /// 后台处理循环
    /// 
    /// 定期检查并尝试重试待发送的消息
    /// </summary>
    private async Task ProcessingLoop()
    {
        while (!_cts.IsCancellationRequested && _isRunning)
        {
            try
            {
                // 检查是否有待处理的消息
                if (_retryQueue.Count == 0)
                {
                    // 队列为空，短时间等待
                    await Task.Delay(50, _cts.Token);
                    continue;
                }

                // 取出一条消息处理
                if (_retryQueue.TryDequeue(out var item))
                {
                    await ProcessRetryItem(item);
                }
                else
                {
                    // 并发场景：队列刚被其他任务消费完
                    await Task.Delay(20, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
                break;
            }
            catch (Exception ex)
            {
                Logger.Error("Network", ex, "消息重试循环异常");
                await Task.Delay(1000, _cts.Token);  // 出错后稍等
            }
        }
    }

    /// <summary>
    /// 处理单条待重试消息
    /// </summary>
    private async Task ProcessRetryItem(RetryItem item)
    {
        // 检查 Session 是否已断开
        if (item.Session == null || !item.Session.IsConnected)
        {
            // Session 已断开，放入死信队列
            await AddToDeadLetter(item, "Session 已断开");
            return;
        }

        // 检查是否需要等待（指数退避）
        var waitTime = item.NextRetryAt - DateTime.UtcNow;
        if (waitTime > TimeSpan.Zero)
        {
            // 重新入队（延后处理）
            _retryQueue.Enqueue(item);
            // 稍微等待一下，避免快速循环
            await Task.Delay(Math.Min((int)waitTime.TotalMilliseconds, 100));
            return;
        }

        Interlocked.Increment(ref _totalRetryAttempts);

        // 尝试重发
        try
        {
            await item.Session.SendRawAsync(item.Data);
            Interlocked.Increment(ref _successfulRetries);
            UpdateMetrics();

            Logger.Info("Network", "消息重发成功: RetryCount={0}, 原始失败原因={1}",
                item.RetryCount, item.FailureReason);
        }
        catch (Exception ex)
        {
            // 重发失败
            item.RetryCount++;

            if (item.RetryCount >= _options.MaxRetryCount)
            {
                // 达到最大重试次数，放入死信队列
                await AddToDeadLetter(item, $"重发仍失败: {ex.Message}");
            }
            else
            {
                // 计算下次重试时间（指数退避）
                var nextInterval = Math.Min(
                    _options.BaseIntervalMs * (long)Math.Pow(2, item.RetryCount - 1),
                    _options.MaxIntervalMs);
                item.NextRetryAt = DateTime.UtcNow.AddMilliseconds(nextInterval);

                // 重新入队
                _retryQueue.Enqueue(item);

                Logger.Warning("Network", "消息重发失败 ({0}/{1}), 下次重试: {2:HH:mm:ss}, 原因: {3}",
                    item.RetryCount, _options.MaxRetryCount,
                    item.NextRetryAt, ex.Message);
            }

            UpdateMetrics();
        }
    }

    /// <summary>
    /// 将消息添加到死信队列
    /// </summary>
    private async Task AddToDeadLetter(RetryItem item, string reason)
    {
        var deadItem = new DeadLetterItem
        {
            Data = item.Data,
            RetryCount = item.RetryCount,
            FirstFailedAt = item.FirstFailedAt,
            OriginalFailureReason = item.FailureReason,
            FinalFailureReason = reason,
            DiscardedAt = DateTime.UtcNow
        };

        _deadLetterQueue.Enqueue(deadItem);
        _deadLetterCount = _deadLetterQueue.Count;

        // 限制死信队列大小，防止内存溢出
        while (_deadLetterQueue.Count > _options.MaxDeadLetterCapacity)
        {
            _deadLetterQueue.TryDequeue(out _);
        }

        Logger.Error("Network", "消息进入死信队列: 重试次数={0}, 原始失败原因={1}, 最终原因={2}",
            item.RetryCount, item.FailureReason, reason);

        UpdateMetrics();

        await Task.CompletedTask;
    }

    /// <summary>
    /// 更新指标
    /// </summary>
    private void UpdateMetrics()
    {
        try
        {
            var pendingGauge = MetricsCollector.Instance.GetGauge("retry.pending_count");
            var deadLetterGauge = MetricsCollector.Instance.GetGauge("retry.dead_letter_count");
            var attemptsCounter = MetricsCollector.Instance.GetCounter("retry.total_attempts");
            var successfulCounter = MetricsCollector.Instance.GetCounter("retry.successful_count");

            pendingGauge?.Set(_retryQueue.Count);
            deadLetterGauge?.Set(_deadLetterQueue.Count);

            // 更新计数器（如果之前有值，保持增量）
            // 注意：这里简化处理，直接设置为当前值
        }
        catch { }
    }

    #endregion
}

/// <summary>
/// 死信消息条目
/// </summary>
public class DeadLetterItem
{
    /// <summary>
    /// 原始消息数据
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 首次失败时间
    /// </summary>
    public DateTime FirstFailedAt { get; set; }

    /// <summary>
    /// 原始失败原因
    /// </summary>
    public string OriginalFailureReason { get; set; } = string.Empty;

    /// <summary>
    /// 最终失败原因
    /// </summary>
    public string FinalFailureReason { get; set; } = string.Empty;

    /// <summary>
    /// 丢弃时间
    /// </summary>
    public DateTime DiscardedAt { get; set; }
}

/// <summary>
/// 消息重试队列配置
/// </summary>
public class MessageRetryOptions
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 基础重试间隔（毫秒）
    /// 
    /// 实际间隔 = BaseIntervalMs * 2^(重试次数-1)
    /// 默认：100ms
    /// </summary>
    public int BaseIntervalMs { get; set; } = 100;

    /// <summary>
    /// 最大重试间隔（毫秒）
    /// 
    /// 防止指数退避使间隔过大
    /// 默认：5000ms
    /// </summary>
    public int MaxIntervalMs { get; set; } = 5000;

    /// <summary>
    /// 死信队列最大容量
    /// 
    /// 超过此容量的死信消息会被丢弃
    /// 默认：10000
    /// </summary>
    public int MaxDeadLetterCapacity { get; set; } = 10000;
}
