// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Google.Protobuf;
using MMORPG.Framework.Logging;
using MMORPG.Framework.Observability;
using MMORPG.Framework.Security;

namespace MMORPG.Framework.Network;

/// <summary>
/// 会话 - 管理单个玩家连接
/// 
/// 生命周期：
/// 1. 创建（构造函数）- 新客户端连接时
/// 2. 开始接收（StartReceive）- 启动异步接收
/// 3. 循环收发（Receive/Send 交替）
/// 4. 断开（Disconnect）- 正常关闭或异常
/// 5. 销毁（GC）
/// 
/// 线程安全说明：
/// - 接收操作：由 IOCP IO 线程处理（单线程）
/// - 发送操作：通过发送队列串行化
/// - 状态修改：使用锁保护
/// 
/// 粘包处理：
/// TCP 是流式协议，需要自己处理消息边界。
/// 我们使用的方法是：
///   - 每条消息有固定长度的消息头（8字节：4长度 + 4类型）
///   - 接收数据后，循环解析所有完整消息
///   - 不完整的数据留到下一次接收
/// </summary>
public class Session
{
    #region 属性

    /// <summary>
    /// 连接的全局唯一 ID
    /// 由服务器在连接建立时分配
    /// 用于日志追踪和会话查找
    /// </summary>
    public long ConnectionId { get; }

    /// <summary>
    /// 远程端点（玩家的 IP 和端口）
    /// 用于日志记录和 IP 黑名单判断
    /// </summary>
    public IPEndPoint? RemoteEndPoint { get; private set; }

    /// <summary>
    /// 连接是否正常
    /// 
    /// 注意：此属性用于快速判断
    /// 真正的连接状态需要通过 Socket 操作判断
    /// </summary>
    public bool IsConnected => _socket != null && _socket.Connected;

    /// <summary>
    /// 最后心跳时间（UTC）
    ///
    /// 每次收到任何数据都会更新此时间
    /// 心跳管理器会定期检查，超时则断开
    ///
    /// ⚠️ 线程安全：DateTime 不能使用 volatile，使用锁保护读写
    /// - IOCP 线程写入（每次收到数据）
    /// - HeartbeatManager Timer 线程读取（超时检查）
    /// </summary>
    private DateTime _lastHeartbeat;
    private readonly object _heartbeatLock = new();

    /// <summary>
    /// 获取/设置最后心跳时间（线程安全）
    /// </summary>
    public DateTime LastHeartbeat
    {
        get
        {
            lock (_heartbeatLock)
            {
                return _lastHeartbeat;
            }
        }
        set
        {
            lock (_heartbeatLock)
            {
                _lastHeartbeat = value;
            }
        }
    }

    /// <summary>
    /// 关联的玩家 ID
    /// 登录成功后由业务层设置
    /// 未登录时为 0
    ///
    /// ⚠️ 线程安全：使用 Interlocked 保证原子读写
    /// - 消息处理线程写入（登录成功）
    /// - 其他线程可能读取（广播、查找等）
    /// </summary>
    private long _playerId;

    /// <summary>
    /// 获取/设置玩家 ID（线程安全）
    /// </summary>
    public long PlayerId
    {
        get => Interlocked.Read(ref _playerId);
        set => Interlocked.Exchange(ref _playerId, value);
    }

    #endregion

    #region 私有字段

    /// <summary>
    /// 客户端套接字
    /// 
    /// 注意：不要直接操作此 Socket
    /// 所有 IO 操作通过 SocketAsyncEventArgs
    /// 此字段可由连接池重置（非 readonly 以支持池化复用）
    /// </summary>
    private Socket? _socket;

    /// <summary>
    /// 服务器配置
    /// 可由连接池重置以支持配置切换
    /// </summary>
    private TcpServerOptions _options;

    /// <summary>
    /// 消息接收回调
    /// 当收到完整消息时调用
    /// 此字段可由连接池重置以支持消息路由的动态切换
    /// </summary>
    private Action<Session, IMessage> _onMessageReceived;

    /// <summary>
    /// 断开回调
    /// 当会话断开时调用
    /// 此字段可由连接池重置以支持连接复用
    /// </summary>
    private Action<Session, string> _onDisconnected;

    /// <summary>
    /// 接收缓冲区
    /// 
    /// 使用 ArrayPool 租用，避免频繁 GC
    /// 大小：_options.ReceiveBufferSize
    /// 此字段非 readonly 以支持连接池重置时重新分配
    /// </summary>
    private byte[] _receiveBuffer;

    /// <summary>
    /// 接收用的 SocketAsyncEventArgs
    /// 
    /// 复用同一个对象，避免频繁创建销毁
    /// 这是高性能网络编程的标准做法
    /// </summary>
    private readonly SocketAsyncEventArgs _receiveArgs;

    /// <summary>
    /// 发送用的 SocketAsyncEventArgs
    /// </summary>
    private readonly SocketAsyncEventArgs _sendArgs;

    /// <summary>
    /// 发送队列
    /// 
    /// 使用 Channel 实现无锁队列
    /// 工作流程：
    /// 1. SendAsync() 把数据放入队列
    /// 2. IO 线程从队列取出数据
    /// 3. 通过 SocketAsyncEventArgs 发送
    /// 
    /// 好处：
    /// - 解耦发送逻辑和 IO 操作
    /// - 保证消息顺序
    /// - 避免多个线程同时操作 socket
    /// 
    /// 注意：此字段非 readonly，连接池 ResetWith 时会重建队列
    /// </summary>
    private Channel<byte[]> _sendQueue;

    /// <summary>
    /// 连接池使用的 ConnectionId 自增计数器
    /// 
    /// 为池化 Session 分配唯一 ID
    /// </summary>
    private static long _connectionIdCounter;

    /// <summary>
    /// 发送锁
    /// 
    /// 保证同一时刻只有一个发送操作在进行
    /// </summary>
    private readonly object _sendLock = new();

    /// <summary>
    /// 是否有发送操作正在进行
    /// 
    /// ⚠️ P2 修复：volatile — 多个 IO 线程可能读写此字段，
    /// 读取方可能在 _sendLock 之外检查，volatile 确保值不会被缓存到寄存器。
    /// </summary>
    private volatile bool _isSending;

    /// <summary>
    /// 粘包处理：剩余未处理的数据
    ///
    /// TCP 流可能只收到半条消息
    /// 这部分数据会存在这里，等下次收到数据后合并处理
    ///
    /// 使用 ArrayPool 管理：_pendingBuffer 是从池里租来的
    /// 真正有效的数据长度是 _pendingLength（可能小于缓冲区大小）
    /// </summary>
    private byte[] _pendingBuffer = Array.Empty<byte>();

    /// <summary>
    /// 待处理数据的长度
    /// </summary>
    private int _pendingLength = 0;

    /// <summary>
    /// 工作缓冲区（单线程接收，可安全重用）
    /// 避免每次接收都 new byte[]，显著降低 GC 压力
    /// </summary>
    private byte[] _workBuffer = new byte[4096];

    /// <summary>
    /// 是否已断开
    /// 防止重复断开
    /// 
    /// ⚠️ P2 修复：volatile — IOCP 线程（ProcessReceive/ProcessSend）
    /// 在 _disconnectLock 之外读取此值，volatile 保证内存可见性。
    /// </summary>
    private volatile bool _disconnected;

    /// <summary>
    /// ⚠️ #25 修复：是否已释放（防止 Dispose 重入）
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 断开锁
    /// </summary>
    private readonly object _disconnectLock = new();

    /// <summary>
    /// 消息重试队列（由外部设置；null = 不启用重试）
    /// 
    /// 当发送异常时，若此值不为 null，则将消息放入重试队列而不是直接断开连接
    /// </summary>
    public MessageRetryQueue? RetryQueue { get; set; }

    /// <summary>会话级限流器；null = 不启用</summary>
    private RateLimiter? _sessionRateLimiter;

    /// <summary>
    /// TLS 流（启用 TLS 时使用）
    /// 
    /// 在 TLS 握手成功后创建
    /// 所有加密通信通过此流进行
    /// </summary>
    private SslStream? _sslStream;

    /// <summary>
    /// 服务器证书（启用 TLS 时使用）
    /// </summary>
    private X509Certificate2? _serverCertificate;

    /// <summary>
    /// 是否已完成 TLS 握手
    /// 
    /// ⚠️ P2 修复：volatile — 接收线程（StartReceive/OnReceiveCompleted）
    /// 读取此值，TLS 握手回调写入，需保证内存可见性。
    /// </summary>
    private volatile bool _tlsHandshakeCompleted;

    /// <summary>
    /// TLS 异步接收任务的引用（用于取消和等待）
    /// </summary>
    private Task? _sslReceiveTask;

    #endregion

    #region 构造函数

    /// <summary>
    /// 构造函数
    /// 
    /// 在新连接建立时由 TcpServer 调用
    /// </summary>
    /// <param name="connectionId">连接 ID</param>
    /// <param name="socket">客户端套接字</param>
    /// <param name="options">服务器配置</param>
    /// <param name="onMessageReceived">消息接收回调</param>
    /// <param name="onDisconnected">断开回调</param>
    /// <param name="serverCertificate">服务器 TLS 证书（启用 TLS 时提供）</param>
    public Session(
        long connectionId,
        Socket socket,
        TcpServerOptions options,
        Action<Session, IMessage> onMessageReceived,
        Action<Session, string> onDisconnected,
        X509Certificate2? serverCertificate = null)
    {
        ConnectionId = connectionId;
        _socket = socket;
        _options = options;
        _onMessageReceived = onMessageReceived;
        _onDisconnected = onDisconnected;
        _serverCertificate = serverCertificate;
        LastHeartbeat = DateTime.UtcNow;

        // 获取远程端点
        try
        {
            RemoteEndPoint = (IPEndPoint?)socket.RemoteEndPoint;
        }
        catch
        {
            RemoteEndPoint = null;
        }

        // 分配接收缓冲区（从数组池租用，减少 GC 压力）
        _receiveBuffer = ArrayPool<byte>.Shared.Rent(options.ReceiveBufferSize);

        // 初始化接收 SocketAsyncEventArgs
        _receiveArgs = new SocketAsyncEventArgs();
        _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
        _receiveArgs.Completed += OnReceiveCompleted;
        _receiveArgs.UserToken = this;

        // 初始化发送 SocketAsyncEventArgs
        _sendArgs = new SocketAsyncEventArgs();
        _sendArgs.Completed += OnSendCompleted;
        _sendArgs.UserToken = this;

        // 创建发送队列
        // 有界队列：防止消息过多导致内存膨胀
        _sendQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(options.MaxSendQueueSize)
        {
            SingleReader = true,   // 单消费者
            SingleWriter = false,  // 多生产者
            FullMode = BoundedChannelFullMode.Wait  // 队列满时等待
        });

        Logger.Debug("Network", "会话创建: ConnectionId={0}, EndPoint={1}",
            ConnectionId, RemoteEndPoint?.ToString() ?? "未知");
    }

    #endregion

    #region 公共方法 - 接收

    /// <summary>
    /// 开始接收数据
    /// 
    /// 在 Session 创建后由 TcpServer 调用一次
    /// 之后的接收由异步回调自动继续
    /// 
    /// 如果启用了 TLS，会先执行 TLS 握手
    /// 
    /// 注意：此方法是线程安全的
    /// </summary>
    public void StartReceive()
    {
        if (_disconnected)
            return;

        if (_options.EnableTls && !_tlsHandshakeCompleted)
        {
            // 启用了 TLS，先执行握手
            _sslReceiveTask = Task.Run(async () =>
            {
                try
                {
                    if (_serverCertificate == null)
                    {
                        Logger.Error("Network", "TLS 已启用但证书为空: ConnectionId={0}", ConnectionId);
                        Disconnect("TLS 证书缺失");
                        return;
                    }

                    _sslStream = await TlsHelper.PerformHandshakeAsync(
                        _socket!,
                        _serverCertificate!,
                        _options).ConfigureAwait(false);

                    if (_sslStream == null)
                    {
                        Disconnect("TLS 握手失败");
                        return;
                    }

                    _tlsHandshakeCompleted = true;
                    Logger.Info("Network", "TLS 握手成功: ConnectionId={0}, Protocol={1}",
                        ConnectionId, _sslStream.SslProtocol);

                    // 握手成功，开始异步接收（通过 SslStream）
                    await StartSslReceive().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Error("Network", ex, "TLS 握手异常: ConnectionId={0}", ConnectionId);
                    Disconnect("TLS 握手异常");
                }
            });
        }
        else
        {
            // 明文模式或 TLS 已完成，直接开始接收
            // 发起异步接收
            // 如果返回 false，表示操作同步完成，立即处理
            // 如果返回 true，表示操作异步进行，完成时会触发 Completed 事件
            if (!_socket!.ReceiveAsync(_receiveArgs))
            {
                ProcessReceive(_receiveArgs);
            }
        }
    }

    /// <summary>设置会话级限流</summary>
    public void SetSessionRateLimit(long maxMessagesPerSecond)
    {
        if (maxMessagesPerSecond <= 0)
            _sessionRateLimiter = null;
        else
            _sessionRateLimiter = new RateLimiter(maxMessagesPerSecond);
    }

    /// <summary>
    /// 通过 SslStream 开始异步接收数据
    ///
    /// 在 TLS 握手成功后调用。
    /// 修复：改为 async Task 而非 async void，异常可被正确传播和观察。
    /// </summary>
    private async Task StartSslReceive()
    {
        if (_disconnected || _sslStream == null)
            return;

        try
        {
            while (!_disconnected)
            {
                var bytesRead = await _sslStream.ReadAsync(_receiveBuffer, 0, _receiveBuffer.Length).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    Disconnect("对方关闭连接");
                    return;
                }

                // 更新心跳时间
                LastHeartbeat = DateTime.UtcNow;

                // 处理接收到的数据（复用现有逻辑）
                ProcessSslReceiveData(bytesRead);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "SSL 接收异常: ConnectionId={0}", ConnectionId);
            Disconnect("SSL 接收异常");
        }
    }

    /// <summary>
    /// 处理通过 SslStream 接收到的数据
    /// 
    /// 复用现有的粘包处理逻辑
    /// </summary>
    private void ProcessSslReceiveData(int bytesRead)
    {
        if (_disconnected)
            return;

        // ---------- 使用可重用工作缓冲区处理粘包 ----------
        var totalLength = _pendingLength + bytesRead;

        // 确保工作缓冲区足够大（不够就扩容）
        if (_workBuffer.Length < totalLength)
        {
            var newSize = ((totalLength / 4096) + 1) * 4096;
            _workBuffer = new byte[newSize];
        }

        // 合并：旧数据 + 新数据到 _workBuffer
        if (_pendingLength > 0)
        {
            Array.Copy(_pendingBuffer, 0, _workBuffer, 0, _pendingLength);
            Array.Copy(_receiveBuffer, 0, _workBuffer, _pendingLength, bytesRead);
        }
        else
        {
            Array.Copy(_receiveBuffer, 0, _workBuffer, 0, bytesRead);
        }

        // 解析消息（传入工作缓冲区和有效长度）
        var processedOffset = ProcessDataBuffer(_workBuffer, totalLength);

        // 保存未处理的数据（如果有）
        if (processedOffset < totalLength)
        {
            var remaining = totalLength - processedOffset;

            if (_pendingBuffer.Length < remaining)
            {
                if (_pendingBuffer.Length > 0)
                {
                    try { ArrayPool<byte>.Shared.Return(_pendingBuffer); }
                    catch { }
                }

                var rentSize = Math.Max(remaining, 1024);
                _pendingBuffer = ArrayPool<byte>.Shared.Rent(rentSize);
            }

            Array.Copy(_workBuffer, processedOffset, _pendingBuffer, 0, remaining);
            _pendingLength = remaining;
        }
        else
        {
            _pendingLength = 0;
        }
    }

    #endregion

    #region 公共方法 - 发送

    /// <summary>
    /// 发送消息（异步）
    /// 
    /// 消息会被序列化并放入发送队列
    /// 由 IO 线程负责实际发送
    /// 
    /// 使用示例：
    /// ```csharp
    /// await session.SendAsync(new LoginResponse { Success = true });
    /// ```
    /// </summary>
    /// <param name="message">要发送的消息</param>
    public async ValueTask SendAsync(IMessage message)
    {
        if (_disconnected)
            return;

        try
        {
            // 获取消息 ID
            var messageId = MessageSerializer.GetMessageId(message);

            // 序列化消息（加上消息头）
            var data = MessageSerializer.Serialize(message, messageId);

            // ========== 指标收集：tps.bytes_tx（发送字节数） ==========
            try
            {
                if (MetricsCollector.Instance.IsEnabled)
                {
                    var bytesTxCounter = MetricsCollector.Instance.RegisterCounter(
                        "tps.bytes_tx", "发送字节数（消息头+消息体）");
                    bytesTxCounter?.IncrementBy(data.Length);
                }
            }
            catch (Exception metricsEx)
            {
                Logger.Error("Network", metricsEx, "指标收集(tps.bytes_tx)异常，忽略");
            }

            // 放入发送队列（如果队列已满，WriteAsync 会等待）
            await _sendQueue.Writer.WriteAsync(data).ConfigureAwait(false);

            // 尝试立即发送
            TrySend();
        }
        catch (Exception ex)
        {
            var msgId = MessageSerializer.GetMessageId(message);
            Logger.Error("Network", ex, "发送消息失败: ConnectionId={0}, MessageId={1}",
                ConnectionId, msgId);
            Disconnect("发送消息异常");
        }
    }

    /// <summary>
    /// 发送原始字节数据（异步）
    /// 
    /// 主要用于内部或测试场景
    /// 调用方需要确保数据格式正确（包括消息头）
    /// </summary>
    /// <param name="data">完整的消息数据包</param>
    public async ValueTask SendRawAsync(byte[] data)
    {
        if (_disconnected)
            return;

        try
        {
            // ========== 指标收集：tps.bytes_tx（发送字节数） ==========
            try
            {
                if (MetricsCollector.Instance.IsEnabled)
                {
                    var bytesTxCounter = MetricsCollector.Instance.RegisterCounter(
                        "tps.bytes_tx", "发送字节数（消息头+消息体）");
                    bytesTxCounter?.IncrementBy(data.Length);
                }
            }
            catch (Exception metricsEx)
            {
                Logger.Error("Network", metricsEx, "指标收集(tps.bytes_tx)异常，忽略");
            }

            await _sendQueue.Writer.WriteAsync(data).ConfigureAwait(false);
            TrySend();
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "发送原始数据失败: ConnectionId={0}", ConnectionId);

            // 若启用消息重试，则尝试进入重试队列，避免一失败就断开
            if (RetryQueue != null)
            {
                try
                {
                    RetryQueue.EnqueueForRetry(this, data, $"SendRawAsync 异常: {ex.Message}");
                    return; // 已入重试队列；不直接断开
                }
                catch (Exception retryEx)
                {
                    Logger.Error("Network", retryEx, "入重试队列失败: ConnectionId={0}", ConnectionId);
                }
            }

            Disconnect("发送数据异常");
        }
    }

    #endregion

    #region 公共方法 - 断开

    /// <summary>
    /// 断开连接（同步版）
    ///
    /// 执行清理，但不等待 TLS 异步接收任务完成。
    /// 若需等待，请使用 <see cref="DisconnectAsync"/>。
    /// </summary>
    /// <param name="reason">断开原因</param>
    public void Disconnect(string reason = "未知原因") => DisconnectInternalAsync(reason, waitForSslTask: false).ConfigureAwait(false);

    /// <summary>
    /// 断开连接（异步版，可等待 TLS 异步接收任务完成）
    ///
    /// 推荐在优雅停机路径使用。
    /// </summary>
    /// <param name="reason">断开原因</param>
    public async Task DisconnectAsync(string reason = "未知原因")
    {
        await DisconnectInternalAsync(reason, waitForSslTask: true).ConfigureAwait(false);
    }

    /// <summary>
    /// 内部断开实现（统一所有清理逻辑）
    /// </summary>
    private async Task DisconnectInternalAsync(string reason, bool waitForSslTask)
    {
        // 使用锁防止重复断开
        lock (_disconnectLock)
        {
            if (_disconnected)
                return;

            _disconnected = true;
        }

        Logger.Debug("Network", "会话断开: ConnectionId={0}, Reason={1}",
            ConnectionId, reason);

        // 关闭 socket（会触发 SSL/Socket 的 ReadAsync 返回 0）
        try { _socket?.Shutdown(SocketShutdown.Both); } catch { }
        try { _socket?.Close(); } catch { }

        // 关闭 SSL 流
        try { _sslStream?.Close(); } catch { }
        try { _sslStream?.Dispose(); } catch { }

        // 释放 SocketAsyncEventArgs（⚠️ P2 修复：SocketAsyncEventArgs 实现 IDisposable）
        try { _receiveArgs.Dispose(); } catch { }
        try { _sendArgs.Dispose(); } catch { }

        // 释放缓冲区
        try { ArrayPool<byte>.Shared.Return(_receiveBuffer); } catch { }

        // 清理发送队列
        _sendQueue.Writer.TryComplete();

        // 等待 TLS 异步接收任务结束（优雅停机时必须）
        if (waitForSslTask && _sslReceiveTask != null)
        {
            try { await _sslReceiveTask.ConfigureAwait(false); } catch { }
        }

        // 触发断开回调
        try { _onDisconnected?.Invoke(this, reason); }
        catch (Exception ex) { Logger.Error("Network", ex, "断开回调异常: ConnectionId={0}", ConnectionId); }
    }

    #endregion

    #region 私有方法 - 接收处理

    /// <summary>
    /// Receive 操作完成时的回调
    /// 
    /// 由 IOCP 线程池调用
    /// </summary>
    private void OnReceiveCompleted(object? sender, SocketAsyncEventArgs e)
    {
        ProcessReceive(e);
    }

    /// <summary>
    /// 处理接收到的数据
    ///
    /// 步骤：
    /// 1. 检查错误
    /// 2. 检查是否收到数据（BytesTransferred=0 表示对方关闭）
    /// 3. 更新心跳时间
    /// 4. 处理粘包：解析所有完整消息（使用可重用工作缓冲区，避免 GC）
    /// 5. 继续接收
    /// </summary>
    private void ProcessReceive(SocketAsyncEventArgs e)
    {
        if (_disconnected)
            return;

        // 检查是否有错误
        if (e.SocketError != SocketError.Success)
        {
            Disconnect($"接收错误: {e.SocketError}");
            return;
        }

        // 检查是否收到数据
        // BytesTransferred = 0 表示对方优雅关闭了连接
        if (e.BytesTransferred == 0)
        {
            Disconnect("对方关闭连接");
            return;
        }

        // 更新心跳时间（收到任何数据都算活动）
        LastHeartbeat = DateTime.UtcNow;

        // ---------- 使用可重用工作缓冲区处理粘包 ----------
        var totalLength = _pendingLength + e.BytesTransferred;

        // 确保工作缓冲区足够大（不够就扩容）
        if (_workBuffer.Length < totalLength)
        {
            // 扩容：取需要大小的下一个 4K 倍数，减少频繁扩容
            var newSize = ((totalLength / 4096) + 1) * 4096;
            _workBuffer = new byte[newSize];
        }

        // 合并：旧数据 + 新数据到 _workBuffer
        if (_pendingLength > 0)
        {
            // 有待处理数据：先拷贝旧数据
            Array.Copy(_pendingBuffer, 0, _workBuffer, 0, _pendingLength);
            // 再拷贝新收到的数据
            Array.Copy(e.Buffer!, e.Offset, _workBuffer, _pendingLength, e.BytesTransferred);
        }
        else
        {
            // 没有待处理数据：直接拷贝新数据
            Array.Copy(e.Buffer!, e.Offset, _workBuffer, 0, e.BytesTransferred);
        }

        // 解析消息（传入工作缓冲区和有效长度）
        var processedOffset = ProcessDataBuffer(_workBuffer, totalLength);

        // 保存未处理的数据（如果有）
        if (processedOffset < totalLength)
        {
            var remaining = totalLength - processedOffset;

            // 确保 _pendingBuffer 足够大
            if (_pendingBuffer.Length < remaining)
            {
                // 如果 _pendingBuffer 原来是池里租来的，先归还
                if (_pendingBuffer.Length > 0)
                {
                    try { ArrayPool<byte>.Shared.Return(_pendingBuffer); }
                    catch { }
                }

                // 重新租用（至少 1024 字节，方便后续接收）
                var rentSize = Math.Max(remaining, 1024);
                _pendingBuffer = ArrayPool<byte>.Shared.Rent(rentSize);
            }

            // 拷贝剩余数据
            Array.Copy(_workBuffer, processedOffset, _pendingBuffer, 0, remaining);
            _pendingLength = remaining;
        }
        else
        {
            _pendingLength = 0;
        }

        // 继续接收下一批数据
        // 注意：接收缓冲区是循环使用的
        if (!_disconnected)
        {
            _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);

            if (!_socket!.ReceiveAsync(_receiveArgs))
            {
                ProcessReceive(_receiveArgs);
            }
        }
    }

    /// <summary>
    /// 处理数据缓冲区：解析所有完整消息
    /// 
    /// 返回已处理的字节数
    /// 剩余未处理的字节数会被保存到 _pendingBuffer
    /// </summary>
    private int ProcessDataBuffer(byte[] data, int length)
    {
        var offset = 0;

        while (offset < length)
        {
            var remaining = length - offset;

            // 必须至少有消息头才能做限流判断
            if (remaining < MessageSerializer.HeaderSize)
            {
                // 数据不够消息头，等下一批
                break;
            }

            // 解析消息头（先解析，才能知道要跳过多少字节）
            if (!MessageSerializer.TryParseHeader(data, offset, out var bodyLength, out var messageId))
            {
                Logger.Warning("Network", "非法消息头: ConnectionId={0}", ConnectionId);
                Disconnect("非法消息头");
                return length; // 标记所有数据已处理（实际上会断开）
            }

            // ⚠️ P0 修复：bodyLength 必须有上限，防止恶意客户端发送超大 bodyLength 导致 OOM
            const int MaxBodyLength = 10 * 1024 * 1024; // 10MB
            if (bodyLength > MaxBodyLength)
            {
                Logger.Warning("Network", "消息体超限: ConnectionId={0}, MessageId=0x{1:X8}, bodyLength={2} > {3}",
                    ConnectionId, messageId, bodyLength, MaxBodyLength);
                Disconnect($"消息体超限({bodyLength}字节)");
                return length;
            }

            var totalMessageSize = MessageSerializer.HeaderSize + bodyLength;

            // 检查是否收到完整消息
            if (remaining < totalMessageSize)
            {
                // 还没收完整，等下一批
                break;
            }

            // 会话级限流检查（⚠️ P0 修复：在解析完头部、确认完整消息后才做限流判断）
            if (_sessionRateLimiter != null && !_sessionRateLimiter.TryAcquire())
            {
                Logger.Warning("Network", "会话消息被限流：ConnectionId={0}, MessageId=0x{1:X8}, 累计丢弃={2}",
                    ConnectionId, messageId, _sessionRateLimiter.DroppedCount);
                // 跳过整条消息（不是 1 字节！），防止错位破坏后续消息解析
                offset += totalMessageSize;
                continue;
            }

            // 提取并解析消息（直接在 data 上工作，零分配）
            try
            {
                var message = MessageSerializer.Deserialize(data, offset);
                Logger.Debug("Network", "收到消息: ConnectionId={0}, MessageId={1}({2})",
                    ConnectionId, messageId, MessageIds.GetDescription(messageId));

                // ========== 指标收集：tps.bytes_rx（接收字节数） ==========
                try
                {
                    if (MetricsCollector.Instance.IsEnabled)
                    {
                        var bytesRxCounter = MetricsCollector.Instance.RegisterCounter(
                            "tps.bytes_rx", "接收字节数（消息头+消息体）");
                        bytesRxCounter?.IncrementBy(totalMessageSize);
                    }
                }
                catch (Exception metricsEx)
                {
                    Logger.Error("Network", metricsEx, "指标收集(tps.bytes_rx)异常，忽略");
                }

                // 触发消息回调
                _onMessageReceived?.Invoke(this, message);
            }
            catch (Exception ex)
            {
                Logger.Error("Network", ex, "消息解析失败: ConnectionId={0}, MessageId={1}",
                    ConnectionId, messageId);
                // 解析失败不断开，只是跳过这条消息继续处理
                // 防止恶意客户端通过发送错误消息使大量玩家断线
            }

            // 移动到下一条消息
            offset += totalMessageSize;
        }

        return offset;
    }

    #endregion

    #region 私有方法 - 发送处理

    /// <summary>
    /// 尝试发送数据
    /// 
    /// 如果当前没有正在进行的发送操作，
    /// 从队列取出一条消息开始发送
    /// </summary>
    private void TrySend()
    {
        if (_disconnected)
            return;

        lock (_sendLock)
        {
            // 检查是否有发送操作正在进行
            if (_isSending)
                return;

            // 从队列取出消息
            if (!_sendQueue.Reader.TryRead(out var data))
            {
                // 队列空了
                return;
            }

            // 开始发送
            _isSending = true;
            _sendArgs.SetBuffer(data, 0, data.Length);

            try
            {
                if (!_socket!.SendAsync(_sendArgs))
                {
                    // 同步完成
                    ProcessSend(_sendArgs);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Network", ex, "发起发送异常: ConnectionId={0}", ConnectionId);
                _isSending = false;
                Disconnect("发送异常");
            }
        }
    }

    /// <summary>
    /// Send 操作完成时的回调
    /// </summary>
    private void OnSendCompleted(object? sender, SocketAsyncEventArgs e)
    {
        ProcessSend(e);
    }

    /// <summary>
    /// 处理发送完成
    /// </summary>
    private void ProcessSend(SocketAsyncEventArgs e)
    {
        if (_disconnected)
            return;

        // 检查错误
        if (e.SocketError != SocketError.Success)
        {
            Logger.Warning("Network", "发送错误: ConnectionId={0}, Error={1}",
                ConnectionId, e.SocketError);
            Disconnect($"发送错误: {e.SocketError}");
            return;
        }

        lock (_sendLock)
        {
            // 标记发送完成
            _isSending = false;
        }

        // 检查队列是否还有消息
        // 如果有，继续发送
        TrySend();
    }

    #endregion

    #region 连接池支持

    /// <summary>
    /// 用于连接池预分配的简单构造函数
    /// 
    /// 此构造函数创建一个空的 Session（不绑定 Socket）
    /// 由 SessionPool 在预热阶段调用，避免首次连接时创建新对象
    /// </summary>
    /// <param name="socket">初始 Socket（可空，由后续 ResetWith 绑定）</param>
    /// <param name="onMessageReceived">消息接收回调</param>
    /// <param name="onDisconnected">断开回调</param>
    public Session(
        Socket? socket,
        Action<Session, IMessage> onMessageReceived,
        Action<Session, string> onDisconnected)
    {
        // 使用静态计数器自动分配 ConnectionId（支持池化复用）
        ConnectionId = Interlocked.Increment(ref _connectionIdCounter);

        _socket = socket;
        _options = new TcpServerOptions();  // 使用默认配置
        _onMessageReceived = onMessageReceived;
        _onDisconnected = onDisconnected;
        LastHeartbeat = DateTime.UtcNow;

        // 尝试获取远程端点
        if (socket != null)
        {
            try
            {
                RemoteEndPoint = (IPEndPoint?)socket.RemoteEndPoint;
            }
            catch
            {
                RemoteEndPoint = null;
            }
        }

        // 分配接收缓冲区
        _receiveBuffer = ArrayPool<byte>.Shared.Rent(_options.ReceiveBufferSize);

        // 初始化接收 SocketAsyncEventArgs
        _receiveArgs = new SocketAsyncEventArgs();
        _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
        _receiveArgs.Completed += OnReceiveCompleted;
        _receiveArgs.UserToken = this;

        // 初始化发送 SocketAsyncEventArgs
        _sendArgs = new SocketAsyncEventArgs();
        _sendArgs.Completed += OnSendCompleted;
        _sendArgs.UserToken = this;

        // 创建发送队列
        _sendQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(_options.MaxSendQueueSize)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        Logger.Debug("Network", "池化 Session 创建: ConnectionId={0}", ConnectionId);
    }

    /// <summary>
    /// 用新的 Socket 重置 Session（连接池复用核心方法）
    /// 
    /// 由 SessionPool.Rent 调用，将一个空闲的 Session 重新绑定到新的客户端连接
    /// 重置：
    ///   1. Socket 引用
    ///   2. 远程端点
    ///   3. 粘包剩余数据
    ///   4. 发送队列
    ///   5. TLS 状态
    ///   6. 心跳时间
    ///   7. 服务器配置（连接池场景下需要更新）
    ///   8. 消息回调（连接池场景下需要更新）
    /// </summary>
    /// <param name="newSocket">新的客户端 Socket</param>
    /// <param name="options">服务器配置（连接池场景）</param>
    /// <param name="onMessageReceived">消息接收回调（可选，连接池场景）</param>
    /// <param name="onDisconnected">断开回调（可选，连接池场景）</param>
    public void ResetWith(Socket newSocket, TcpServerOptions? options = null,
        Action<Session, IMessage>? onMessageReceived = null,
        Action<Session, string>? onDisconnected = null)
    {
        lock (_disconnectLock)
        {
            // 1. 重置连接状态
            _disconnected = false;

            // 2. 更新 Socket 引用
            _socket = newSocket;

            // 3. 更新远程端点
            try
            {
                RemoteEndPoint = (IPEndPoint?)newSocket.RemoteEndPoint;
            }
            catch
            {
                RemoteEndPoint = null;
            }

            // 4. 重置粘包状态
            _pendingLength = 0;

            // 5. 重置 TLS 状态
            _sslStream = null;
            _tlsHandshakeCompleted = false;
            _sslReceiveTask = null; // 清除旧任务引用

            // 6. 重置心跳
            LastHeartbeat = DateTime.UtcNow;

            // 7. 重置发送锁状态
            lock (_sendLock)
            {
                _isSending = false;
            }

            // 8. 更新服务器配置（连接池场景）
            if (options != null)
            {
                _options = options;
            }

            // 9. 更新回调（连接池场景）
            if (onMessageReceived != null)
            {
                _onMessageReceived = onMessageReceived;
            }
            if (onDisconnected != null)
            {
                _onDisconnected = onDisconnected;
            }

            // 10. 重新创建发送队列（丢弃旧队列中的消息）
            _sendQueue.Writer.TryComplete();

            // 重新分配新的 Channel（因为 Writer.TryComplete 之后不能再写）
            var newQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(_options.MaxSendQueueSize)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            // 使用反射或字段直接赋值都可以，这里直接访问字段
            _sendQueue = newQueue;

            Logger.Debug("Network", "Session 重置完成: ConnectionId={0}, EndPoint={1}",
                ConnectionId, RemoteEndPoint?.ToString() ?? "未知");
        }
    }

    /// <summary>
    /// 清理 Session 的 Socket 引用（归还到连接池时调用）
    /// 
    /// 由 SessionPool.Return 调用
    /// 只清理与连接相关的引用，保留可复用的缓冲区等资源
    /// </summary>
    public void ClearSocket()
    {
        lock (_disconnectLock)
        {
            _disconnected = true;

            // 关闭 Socket
            try { _socket?.Close(); } catch { }
            _socket = null;

            // 关闭 SSL 流
            try { _sslStream?.Close(); } catch { }
            try { _sslStream?.Dispose(); } catch { }
            _sslStream = null;
            _tlsHandshakeCompleted = false;
            _sslReceiveTask = null;

            // 清空发送队列
            _sendQueue.Writer.TryComplete();

            // 重新分配发送队列（下次 Rent 时仍可用）
            var newQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(_options.MaxSendQueueSize)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            _sendQueue = newQueue;

            // 清空其他状态
            RemoteEndPoint = null;
            _pendingLength = 0;
            PlayerId = 0;

            Logger.Debug("Network", "Session Socket 已清理: ConnectionId={0}", ConnectionId);
        }
    }

    /// <summary>
    /// 释放所有资源（连接池销毁时调用）
    /// </summary>
    /// <remarks>
    /// ⚠️ #25 修复：实现标准 Dispose 模式。
    /// - 标记 _disconnected 防止重入
    /// - 所有 IO 释放都用 try/catch 包装，单个失败不影响整体清理
    /// - 调用 <see cref="GC.SuppressFinalize"/> 遵循 .NET 编码规范
    /// </remarks>
    public void Dispose()
    {
        lock (_disconnectLock)
        {
            if (_disposed) return;
            _disposed = true;
            _disconnected = true;

            try { _socket?.Close(); } catch (Exception ex) { Logger.Warning("Network", "Session.Dispose 关闭 Socket 异常: {0}", ex.Message); }
            _socket = null;

            try { _sslStream?.Close(); } catch (Exception ex) { Logger.Warning("Network", "Session.Dispose 关闭 SslStream 异常: {0}", ex.Message); }
            try { _sslStream?.Dispose(); } catch (Exception ex) { Logger.Warning("Network", "Session.Dispose 释放 SslStream 异常: {0}", ex.Message); }
            _sslStream = null;
            _sslReceiveTask = null;

            try { _sendQueue.Writer.TryComplete(); } catch (Exception ex) { Logger.Warning("Network", "Session.Dispose 完成 Channel 异常: {0}", ex.Message); }

            try { ArrayPool<byte>.Shared.Return(_receiveBuffer); } catch (Exception ex) { Logger.Warning("Network", "Session.Dispose 归还缓冲区异常: {0}", ex.Message); }

            try { _receiveArgs.Dispose(); } catch (Exception ex) { Logger.Warning("Network", "Session.Dispose 释放 ReceiveArgs 异常: {0}", ex.Message); }
            try { _sendArgs.Dispose(); } catch (Exception ex) { Logger.Warning("Network", "Session.Dispose 释放 SendArgs 异常: {0}", ex.Message); }

            Logger.Debug("Network", "Session 资源已释放: ConnectionId={0}", ConnectionId);
        }

        GC.SuppressFinalize(this);
    }

    #endregion
}

