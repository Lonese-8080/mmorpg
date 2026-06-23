// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Diagnostics;
using Google.Protobuf;
using MMORPG.Framework.Logging;
using MMORPG.Framework.Observability;
using MMORPG.Framework.Resilience;
using MMORPG.Framework.Security;

namespace MMORPG.Framework.Network;

/// <summary>
/// 消息处理器接口
///
/// 业务层需要实现此接口来处理特定类型的消息
///
/// 使用示例：
/// <code>
/// public class LoginHandler : IMessageHandler&lt;C2S_Login&gt;
/// {
///     public async Task HandleAsync(Session session, C2S_Login message)
///     {
///         // 验证账号密码
///         // 返回登录结果
///     }
/// }
/// </code>
/// </summary>
/// <typeparam name="T">处理的消息类型</typeparam>
public interface IMessageHandler<T>
    where T : IMessage
{
    /// <summary>
    /// 处理消息
    /// </summary>
    /// <param name="session">发送消息的会话</param>
    /// <param name="message">消息对象</param>
    /// <returns>异步任务</returns>
    Task HandleAsync(Session session, T message);
}

/// <summary>
/// 消息路由器 - 负责将收到的消息分发到对应的处理器
/// 
/// 工作原理：
/// 1. 业务层在启动时注册消息处理器（RegisterHandler）
/// 2. 收到消息时，根据 MessageId 查找对应的处理器
/// 3. 调用处理器的 HandleAsync 方法
/// 
/// 安全策略：
/// 1. 所有 Handler 的异常都被捕获
/// 2. 不会因为单个消息的处理错误断开连接
/// 3. 记录详细的错误日志
/// 4. 向客户端发送友好的错误提示
/// 
/// 使用示例：
/// <code>
/// var router = MessageRouter.Instance;
/// 
/// // 注册处理器
/// router.RegisterHandler(new LoginHandler());
/// router.RegisterHandler(new HeartbeatHandler());
/// 
/// // 在 TcpServer 的 OnMessageReceived 中路由消息
/// server.OnMessageReceived += (session, message) =>
/// {
///     router.Route(session, message);
/// };
/// </code>
/// </summary>
public class MessageRouter
{
    #region 单例

    /// <summary>
    /// 单例实例
    /// </summary>
    public static MessageRouter Instance { get; } = new();

    #endregion

    #region 字段

    /// <summary>
    /// 消息处理器字典
    ///
    /// Key: MessageId
    /// Value: 处理函数（委托）
    ///
    /// 设计权衡：
    /// - ConcurrentDictionary 自带线程安全，热路径无需 lock
    /// - Register 通常在启动时调用（写入少），RouteAsync 是热路径（读取多）
    /// - TryGetValue + 委托调用的开销远低于 lock + Dictionary.TryGetValue
    /// </summary>
    private readonly ConcurrentDictionary<uint, Func<Session, IMessage, Task>> _handlers = new();

    /// <summary>
    /// 消息处理器对应的弹性策略字典
    /// 
    /// Key: MessageId
    /// Value: ResiliencePipeline（可为 null，表示不使用弹性策略）
    /// 
    /// ⚠️ 注意：此字典与 _handlers 保持同步，当 handler 注册时可通过可选参数同时指定 pipeline
    /// </summary>
    private readonly ConcurrentDictionary<uint, ResiliencePipeline?> _handlerPipelines = new();

    /// <summary>全局消息限流器；null = 不启用限流</summary>
    private RateLimiter? _rateLimiter;

    #endregion

    #region 公共方法 - 注册

    /// <summary>
    /// 注册消息处理器
    ///
    /// 如果同一种消息 ID 被多次注册，后注册的会覆盖先注册的。
    /// 使用 T 实例一次性获取 MessageId（启动时调用，可接受分配）。
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="handler">处理器实例</param>
    public void RegisterHandler<T>(IMessageHandler<T> handler)
        where T : class, IMessage
    {
        RegisterHandler<T>(handler, pipeline: null);
    }

    /// <summary>
    /// 注册消息处理器（带弹性策略）
    ///
    /// 如果同一种消息 ID 被多次注册，后注册的会覆盖先注册的。
    ///
    /// 使用示例：
    /// ```csharp
    /// var dbPipeline = new ResiliencePipelineBuilder()
    ///     .WithTimeout("Database", 3000)
    ///     .WithRetry("Database", new RetryOptions { MaxRetryCount = 3 })
    ///     .WithCircuitBreaker("Database", new CircuitBreakerOptions { FailureThreshold = 5 })
    ///     .Build();
    ///
    /// router.RegisterHandler(new PlayerQueryHandler(), dbPipeline);
    /// ```
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="handler">处理器实例</param>
    /// <param name="pipeline">弹性策略（可为 null，表示不使用）</param>
    public void RegisterHandler<T>(IMessageHandler<T> handler, ResiliencePipeline? pipeline)
        where T : class, IMessage
    {
        // 启动时一次性注册：创建临时实例获取 MessageId
        var temp = Activator.CreateInstance<T>();
        var messageId = MessageSerializer.GetMessageId(temp);

        Func<Session, IMessage, Task> handlerFunc = async (session, message) =>
        {
            if (message is T typedMessage)
            {
                await handler.HandleAsync(session, typedMessage).ConfigureAwait(false);
            }
            else
            {
                Logger.Warning("Network", "消息类型不匹配: Expected={0}, Actual={1}",
                    typeof(T).Name, message.GetType().Name);
            }
        };

        _handlers[messageId] = handlerFunc;
        _handlerPipelines[messageId] = pipeline;

        var pipelineInfo = pipeline != null ? $", Pipeline={pipeline.Name}" : "";
        Logger.Debug("Network", "注册消息处理器: MessageId={0}({1}), Handler={2}{3}",
            messageId, MessageIds.GetDescription(messageId), handler.GetType().Name, pipelineInfo);
    }

    /// <summary>
    /// 注册消息处理器（直接指定消息 ID，适合委托类型处理器）
    ///
    /// 注意：此重载不校验消息类型，仅按 ID 路由。
    /// 推荐使用泛型版本 <see cref="RegisterHandler{T}(IMessageHandler{T})"/> 以获得类型安全。
    /// </summary>
    /// <param name="messageId">消息 ID</param>
    /// <param name="handler">处理函数</param>
    public void RegisterHandler(uint messageId, Func<Session, IMessage, Task> handler)
    {
        RegisterHandler(messageId, handler, pipeline: null);
    }

    /// <summary>
    /// 注册消息处理器（直接指定消息 ID，适合委托类型处理器，带弹性策略）
    /// </summary>
    /// <param name="messageId">消息 ID</param>
    /// <param name="handler">处理函数</param>
    /// <param name="pipeline">弹性策略（可为 null）</param>
    public void RegisterHandler(uint messageId, Func<Session, IMessage, Task> handler, ResiliencePipeline? pipeline)
    {
        _handlers[messageId] = handler;
        _handlerPipelines[messageId] = pipeline;

        var pipelineInfo = pipeline != null ? $", Pipeline={pipeline.Name}" : "";
        Logger.Debug("Network", "注册消息处理器: MessageId={0}({1}), Handler=Func{2}",
            messageId, MessageIds.GetDescription(messageId), pipelineInfo);
    }

    /// <summary>
    /// 注册消息处理器（直接指定消息 ID，同步版本）
    /// </summary>
    /// <param name="messageId">消息 ID</param>
    /// <param name="handler">处理函数（同步）</param>
    public void RegisterHandler(uint messageId, Action<Session, IMessage> handler)
    {
        RegisterHandler(messageId, handler, pipeline: null);
    }

    /// <summary>
    /// 注册消息处理器（直接指定消息 ID，同步版本，带弹性策略）
    /// </summary>
    /// <param name="messageId">消息 ID</param>
    /// <param name="handler">处理函数（同步）</param>
    /// <param name="pipeline">弹性策略（可为 null）</param>
    public void RegisterHandler(uint messageId, Action<Session, IMessage> handler, ResiliencePipeline? pipeline)
    {
        _handlers[messageId] = (session, message) =>
        {
            handler(session, message);
            return Task.CompletedTask;
        };
        _handlerPipelines[messageId] = pipeline;

        var pipelineInfo = pipeline != null ? $", Pipeline={pipeline.Name}" : "";
        Logger.Debug("Network", "注册消息处理器: MessageId={0}({1}), Handler=Action{2}",
            messageId, MessageIds.GetDescription(messageId), pipelineInfo);
    }

    /// <summary>
    /// 移除消息处理器
    /// </summary>
    /// <param name="messageId">消息 ID</param>
    /// <returns>true 表示成功移除，false 表示该 ID 不存在</returns>
    public bool UnregisterHandler(uint messageId)
    {
        var handlerRemoved = _handlers.TryRemove(messageId, out _);
        var pipelineRemoved = _handlerPipelines.TryRemove(messageId, out _);
        return handlerRemoved; // pipeline 可能不存在，但 handler 一定不存在时才返回 false
    }

    /// <summary>启用或更新全局消息限流</summary>
    /// <param name="maxMessagesPerSecond">每秒最大消息数；0 或负数则禁用</param>
    public void SetGlobalRateLimit(long maxMessagesPerSecond)
    {
        if (maxMessagesPerSecond <= 0)
            _rateLimiter = null;
        else
            _rateLimiter = new RateLimiter(maxMessagesPerSecond);
        Logger.Info("Network", "全局消息限流已设置：{0} 条/秒", maxMessagesPerSecond);
    }

    #endregion

    #region 公共方法 - 路由

    /// <summary>
    /// 路由消息到对应的处理器
    ///
    /// 流程：
    /// 1. 根据 MessageId 查找处理器
    /// 2. 如果找到，调用处理器（异步）
    /// 3. 如果没找到，记录警告日志
    ///
    /// 注意：此方法不会抛出异常，所有异常都会被内部捕获并记录。
    /// </summary>
    /// <param name="session">发送消息的会话</param>
    /// <param name="message">消息对象</param>
    public async Task RouteAsync(Session session, IMessage message)
    {
        // ========== 指标收集：TPS / 消息数 / 延迟（零分配计时） ==========
        // ⚠️ P2 修复：使用 Stopwatch.GetTimestamp() 而非 new Stopwatch.StartNew()（零堆分配）
        var startTimestamp = Stopwatch.GetTimestamp();

        // 获取消息 ID（通过类型映射表）
        var messageId = MessageSerializer.GetMessageId(message);

        try
        {
            if (MetricsCollector.Instance.IsEnabled)
            {
                try
                {
                    var tpsCounter = MetricsCollector.Instance.RegisterCounter("tps.total", "总消息处理次数");
                    tpsCounter?.Increment();
                    var processedCounter = MetricsCollector.Instance.RegisterCounter("message.processed_total", "已处理消息总数");
                    processedCounter?.Increment();
                }
                catch (Exception ex)
                {
                    Logger.Error("Network", ex, "指标收集异常，忽略");
                }
            }
        }
        catch
        {
            // MetricsCollector 未启用，静默忽略
        }

        // 限流检查（若启用）
        if (_rateLimiter != null && !_rateLimiter.TryAcquire())
        {
            Logger.Warning("Network", "消息被限流：MessageId=0x{0:X8}, ConnectionId={1}, 累计丢弃={2}",
                messageId, session.ConnectionId, _rateLimiter.DroppedCount);
            return; // 丢弃消息
        }

        // 服务状态检查：Paused 状态下只处理系统消息/心跳
        if (!ServiceStateManager.Instance.AcceptsNormalMessages)
        {
            bool isSystemMessage = messageId == MessageIds.C2S_Heartbeat;
            if (!isSystemMessage)
            {
                Logger.Warning("Network", "服务处于 {0} 状态，丢弃非系统消息：MessageId={1}",
                    ServiceStateManager.Instance.CurrentState, messageId);
                return;
            }
        }

        try
        {
            // 1. 查找对应的处理器（ConcurrentDictionary 自带线程安全，无需 lock）
            if (!_handlers.TryGetValue(messageId, out var handler) || handler == null)
            {
                Logger.Warning("Network", "未注册的消息类型: MessageId=0x{0:X8}({1}), ConnectionId={2}",
                    messageId, MessageIds.GetDescription(messageId), session.ConnectionId);
                return;
            }

            // 3. 数据校验（如果消息类有校验属性）
            if (!ValidateMessage(message, out var validationError))
            {
                Logger.Warning("Network", "消息校验失败: MessageId=0x{0:X8}, ConnectionId={1}, Error={2}",
                    messageId, session.ConnectionId, validationError ?? "未知错误");

                // 发送错误响应（使用原始二进制格式）
                var errorResponse = CreateErrorResponse(400, validationError ?? "数据校验失败");
                await session.SendRawAsync(errorResponse).ConfigureAwait(false);
                return;
            }

            // 4. 执行处理器（带弹性策略，如果已注册）
            _handlerPipelines.TryGetValue(messageId, out var pipeline);
            if (pipeline != null)
            {
                // 弹性策略包装：Timeout → Retry → CircuitBreaker → Handler
                await pipeline.ExecuteAsync(
                    async ct =>
                    {
                        await handler(session, message).ConfigureAwait(false);
                        return true;
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await handler(session, message).ConfigureAwait(false);
            }
        }
        catch (BrokenCircuitException ex)
        {
            // ⚠️ 熔断期间：快速失败，不重试，不耗资源
            Logger.Warning("Network",
                "消息处理熔断中，跳过: MessageId=0x{0:X8}, ConnectionId={1}, Reason={2}",
                messageId, session.ConnectionId, ex.Message);

            try
            {
                var errorResponse = CreateErrorResponse(503, "服务暂时不可用，请稍后重试");
                await session.SendRawAsync(errorResponse).ConfigureAwait(false);
            }
            catch { /* 忽略发送失败 */ }
        }
        catch (TimeoutRejectedException ex)
        {
            // ⏱️ 超时：记录警告，返回 504
            Logger.Warning("Network",
                "消息处理超时: MessageId=0x{0:X8}, ConnectionId={1}, Reason={2}",
                messageId, session.ConnectionId, ex.Message);

            try
            {
                var errorResponse = CreateErrorResponse(504, "处理超时，请稍后重试");
                await session.SendRawAsync(errorResponse).ConfigureAwait(false);
            }
            catch { /* 忽略发送失败 */ }
        }
        catch (Exception ex)
        {
            // 4. 捕获所有异常，防止消息处理异常导致连接断开
            Logger.Error("Network", ex, "处理消息时发生异常: MessageId=0x{0:X8}({1}), ConnectionId={2}",
                messageId, MessageIds.GetDescription(messageId), session.ConnectionId);

            // 向客户端发送错误提示
            try
            {
                var errorResponse = CreateErrorResponse(500, "服务器处理消息时发生错误");
                await session.SendRawAsync(errorResponse).ConfigureAwait(false);
            }
            catch (Exception sendEx)
            {
                Logger.Error("Network", sendEx, "发送错误响应失败: ConnectionId={0}",
                    session.ConnectionId);
            }
        }
        finally
        {
            // ========== 指标收集：记录延迟（零分配 Stopwatch） ==========
            try
            {
                if (MetricsCollector.Instance.IsEnabled)
                {
                    var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
                    var latencyHist = MetricsCollector.Instance.RegisterHistogram("latency.p50", "消息处理耗时(毫秒)");
                    latencyHist?.Record(elapsedMs);
                }
            }
            catch
            {
                // 静默忽略
            }
        }
    }

    /// <summary>
    /// 创建错误响应消息（原始二进制格式）
    /// </summary>
    private static byte[] CreateErrorResponse(int errorCode, string errorMessage)
    {
        // 格式：消息体长度(4B) + 消息ID(4B) + errorCode(4B) + errorMessage(string)
        var errorBytes = System.Text.Encoding.UTF8.GetBytes(errorMessage);
        var bodyLength = 4 + 4 + errorBytes.Length; // errorCode + errorMessage length + content

        var buffer = new byte[MessageSerializer.HeaderSize + bodyLength];

        // 写入消息头
        buffer[0] = (byte)bodyLength;
        buffer[1] = (byte)(bodyLength >> 8);
        buffer[2] = (byte)(bodyLength >> 16);
        buffer[3] = (byte)(bodyLength >> 24);

        var msgId = MessageIds.S2C_Error;
        buffer[4] = (byte)msgId;
        buffer[5] = (byte)(msgId >> 8);
        buffer[6] = (byte)(msgId >> 16);
        buffer[7] = (byte)(msgId >> 24);

        // 写入消息体
        var offset = MessageSerializer.HeaderSize;
        buffer[offset] = (byte)errorCode;
        buffer[offset + 1] = (byte)(errorCode >> 8);
        buffer[offset + 2] = (byte)(errorCode >> 16);
        buffer[offset + 3] = (byte)(errorCode >> 24);

        var len = errorBytes.Length;
        buffer[offset + 4] = (byte)len;
        buffer[offset + 5] = (byte)(len >> 8);
        buffer[offset + 6] = (byte)(len >> 16);
        buffer[offset + 7] = (byte)(len >> 24);

        System.Buffer.BlockCopy(errorBytes, 0, buffer, offset + 8, errorBytes.Length);

        return buffer;
    }

    /// <summary>
    /// 获取已注册的消息类型列表
    /// 
    /// 主要用于调试和监控
    /// </summary>
    /// <returns>已注册的消息 ID 数组</returns>
    public uint[] GetRegisteredMessageIds()
    {
        return _handlers.Keys.ToArray();
    }

    /// <summary>
    /// 校验消息数据
    /// 
    /// 使用反射扫描消息类的校验属性，执行所有校验规则
    /// </summary>
    /// <param name="message">要校验的消息</param>
    /// <param name="errorMessage">校验失败时的错误消息</param>
    /// <returns>是否校验通过</returns>
    private bool ValidateMessage(IMessage message, out string? errorMessage)
    {
        try
        {
            return Validator.TryValidate(message, out errorMessage);
        }
        catch (Exception ex)
        {
            var msgId = MessageSerializer.GetMessageId(message);
            Logger.Error("Network", ex, "消息校验异常: MessageId=0x{0:X8}", msgId);
            errorMessage = "校验异常";
            return false;
        }
    }

    #endregion
}
