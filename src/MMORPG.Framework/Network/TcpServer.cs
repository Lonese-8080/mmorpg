// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using MMORPG.Framework.Logging;
using MMORPG.Framework.Observability;

namespace MMORPG.Framework.Network;

/// <summary>
/// TCP 服务器 - 网络层的核心入口
/// 
/// 工作流程：
/// 1. Start() -> 创建监听套接字 -> 开始 Accept
/// 2. Accept 完成 -> 创建 Session -> 继续 Accept
/// 3. Stop() -> 关闭所有 Session -> 关闭监听套接字
/// 
/// 这个类采用 IOCP（I/O Completion Ports）模型，
/// 所有 IO 操作都是异步的，由系统内核的 IOCP 线程池处理。
/// 
/// 职责：
/// - 监听端口，接受新连接
/// - 管理所有在线 Session
/// - 提供广播消息能力
/// - 触发连接相关事件
/// 
/// 使用示例：
/// ```csharp
/// var server = new TcpServer(new TcpServerOptions { Port = 9000 });
/// 
/// server.OnClientConnected += session =>
///     Logger.Info("Network", "玩家连接: {0}", session.ConnectionId);
///
/// server.OnClientDisconnected += (session, reason) =>
///     Logger.Info("Network", "玩家断开: {0}, 原因: {1}",
///         session.ConnectionId, reason);
/// 
/// server.OnMessageReceived += (session, message) => 
///     MessageRouter.Instance.Route(session, message);
/// 
/// server.Start();
/// ```
/// </summary>
public class TcpServer : IAsyncDisposable, IDisposable
{
    #region 字段

    /// <summary>
    /// 取消令牌源（启动时创建，停机时取消并释放）
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// _cts 的锁（创建/取消需要同步）
    /// </summary>
    private readonly object _ctsLock = new();

    /// <summary>
    /// 监听套接字
    /// 所有的客户端连接都是从这个门口进来的
    /// </summary>
    private Socket? _listener;

    /// <summary>
    /// Accept 用的 SocketAsyncEventArgs
    /// </summary>
    private SocketAsyncEventArgs? _acceptArgs;

    /// <summary>
    /// 所有在线玩家的会话
    /// 
    /// Key: ConnectionId（唯一标识每个连接）
    /// Value: Session
    /// 
    /// 使用 ConcurrentDictionary 保证线程安全
    /// 多个 IO 线程可能同时访问这个字典
    /// </summary>
    private readonly ConcurrentDictionary<long, Session> _sessions = new();

    /// <summary>
    /// 下一个连接的 ID
    /// 使用 Interlocked 自增，保证线程安全
    /// </summary>
    private long _nextConnectionId;

    /// <summary>
    /// 服务器配置
    /// </summary>
    private readonly TcpServerOptions _options;

    /// <summary>
    /// TLS 服务器证书
    /// 
    /// 当 EnableTls = true 时加载
    /// </summary>
    private X509Certificate2? _serverCertificate;

    /// <summary>
    /// 心跳管理器
    /// </summary>
    private readonly HeartbeatManager _heartbeatManager;

    /// <summary>
    /// Session 连接池（可空；当配置 EnablePool = true 时启用）
    /// </summary>
    private SessionPool? _sessionPool;

    /// <summary>
    /// 消息重试队列（可空；当配置 MessageRetryCount > 0 时启用）
    /// </summary>
    private MessageRetryQueue? _messageRetryQueue;

    /// <summary>
    /// 是否已启动
    /// 
    /// ⚠️ P2 修复：volatile — 多个线程（广播、停止、状态检查）
    /// 在 _startLock 之外读取此值，确保内存可见性。
    /// </summary>
    private volatile bool _isRunning;

    /// <summary>
    /// 是否正在关闭（用于让 OnClientConnected/OnClientDisconnected 等区分路径）
    /// 
    /// ⚠️ P2 修复：volatile — 停机流程中多个线程读取此状态标志。
    /// </summary>
    private volatile bool _isShuttingDown;

    /// <summary>
    /// 关闭起始时间
    /// </summary>
    private DateTimeOffset _shutdownStartedAt;

    /// <summary>
    /// 启动锁
    /// </summary>
    private readonly object _startLock = new();

    #endregion

    #region 事件

    /// <summary>
    /// 当新玩家连接时触发
    /// 
    /// 使用场景：
    /// - 记录日志 "玩家 XXX 连接了"
    /// - 初始化玩家数据
    /// - 发送欢迎消息
    /// </summary>
    public event Action<Session>? OnClientConnected;

    /// <summary>
    /// 当玩家断开连接时触发
    /// 
    /// 使用场景：
    /// - 记录日志 "玩家 XXX 断开了"
    /// - 保存玩家数据到数据库
    /// - 清理玩家相关资源
    /// </summary>
    public event Action<Session, string>? OnClientDisconnected;

    /// <summary>
    /// 当收到消息时触发
    /// 
    /// 参数：Session（发送消息的玩家），IMessage（消息对象）
    /// 通常由 MessageRouter 处理此事件
    /// </summary>
    public event Action<Session, IMessage>? OnMessageReceived;

    #endregion

    #region 属性

    /// <summary>
    /// 当前在线连接数
    /// </summary>
    public int ConnectionCount => _sessions.Count;

    /// <summary>
    /// 服务器是否在运行
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 是否正在关闭服务器
    /// </summary>
    public bool IsShuttingDown => _isShuttingDown;

    /// <summary>
    /// 获取服务器配置（只读访问）
    /// </summary>
    public TcpServerOptions Options => _options;

    #endregion

    #region 构造函数

    /// <summary>
    /// 构造 TCP 服务器
    /// </summary>
    /// <param name="options">服务器配置</param>
    public TcpServer(TcpServerOptions? options = null)
    {
        _options = options ?? new TcpServerOptions();
        _heartbeatManager = new HeartbeatManager(_sessions, _options);

        // ========== 指标收集：注册 Gauge（valueProvider 模式，查询时才计算值） ==========
        try
        {
            // session.active: 当前在线会话数（由 _sessions.Count 动态获取）
            MetricsCollector.Instance.RegisterGauge("session.active", "当前在线会话数",
                () => _sessions.Count);

            // gc.count: GC 第 0 代回收次数（用于监控内存压力）
            MetricsCollector.Instance.RegisterGauge("gc.count", "GC 第 0 代回收次数",
                () => GC.CollectionCount(0));

            // gc.time_ms: 近似的内存指标（当前分配字节数转换为 MB），用于观察内存分配趋势
            MetricsCollector.Instance.RegisterGauge("gc.time_ms", "已分配字节数(近似 MB)",
                () => GC.GetTotalAllocatedBytes(false) / 1_000_000.0);
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "TcpServer 注册 Gauge 异常，忽略");
        }
    }

    #endregion

    #region 公共方法 - 启动/停止

    /// <summary>
    /// 启动服务器（异步入口）
    ///
    /// ⚠️ 注意：此方法使用 Task.Run 包装同步的 Start() 操作。
    /// Socket 的 Bind/Listen 本身是同步操作，无法真正异步化。
    /// 此方法适用于需要在非阻塞上下文中启动服务器的场景。
    ///
    /// 执行步骤：
    /// 1. 创建监听套接字
    /// 2. 绑定端口
    /// 3. 开始监听
    /// 4. 启用 Session 连接池（若有配置）
    /// 5. 启用消息重试队列（若有配置）
    /// 6. 开始接受连接
    /// 7. 启动心跳检测
    /// </summary>
    public Task StartAsync()
    {
        return Task.Run(() => Start());
    }

    /// <summary>
    /// 启动服务器
    /// 
    /// 执行步骤：
    /// 1. 创建监听套接字
    /// 2. 绑定端口
    /// 3. 开始监听
    /// 4. 开始接受连接
    /// 5. 启动心跳检测
    /// </summary>
    public void Start()
    {
        lock (_startLock)
        {
            if (_isRunning)
            {
                Logger.Warning("Network", "服务器已在运行，忽略重复启动");
                return;
            }

            // 校验配置参数
            var (valid, error) = _options.Validate();
            if (!valid)
            {
                Logger.Error("Network", "配置参数无效: {0}", error ?? "未知错误");
                return;
            }

            // 创建取消令牌源（用于优雅停机时取消所有异步操作）
            lock (_ctsLock)
            {
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
            }

            // ----------------------------------------
            // 步骤1: 验证 TLS 配置（如启用）
            // ----------------------------------------
            if (_options.EnableTls)
            {
                var (tlsValid, tlsError) = TlsHelper.ValidateTlsConfiguration(_options);
                if (!tlsValid)
                {
                    Logger.Error("Network", "TLS 配置验证失败: {0}", tlsError ?? "未知错误");
                    return;
                }

                _serverCertificate = TlsHelper.LoadCertificate(_options);
                if (_serverCertificate == null)
                {
                    Logger.Error("Network", "无法加载 TLS 证书，服务器启动失败");
                    return;
                }

                // 启用证书热加载
                TlsHelper.EnableCertificateHotReload(_options);
                
                // 订阅证书更新事件
                TlsHelper.CertificateUpdated += OnCertificateUpdated;

                Logger.Info("Network", "TLS 已启用，协议版本: {0}", _options.TlsProtocol);
            }

            // ----------------------------------------
            // 步骤2: 创建监听套接字
            // ----------------------------------------
            _listener = new Socket(
                AddressFamily.InterNetwork,  // IPv4
                SocketType.Stream,            // TCP（流式套接字）
                ProtocolType.Tcp              // TCP 协议
            );

            // 设置 SO_REUSEADDR，允许端口快速重用（调试时很有用）
            _listener.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            // ----------------------------------------
            // 步骤2: 绑定端口
            // ----------------------------------------
            // IPAddress.Any = 0.0.0.0，监听所有网卡
            var endPoint = new IPEndPoint(IPAddress.Any, _options.Port);
            _listener.Bind(endPoint);

            // ----------------------------------------
            // 步骤3: 开始监听
            // ----------------------------------------
            // Backlog = 操作系统最多缓存的等待连接数
            _listener.Listen(_options.Backlog);

            // ----------------------------------------
            // 步骤4: 启用 Session 连接池（若有配置）
            // ----------------------------------------
            try
            {
                if (_options.EnableSessionPool && _options.SessionPoolMinPoolSize >= 0)
                {
                    var poolOptions = new SessionPoolOptions
                    {
                        MinPoolSize = Math.Max(0, _options.SessionPoolMinPoolSize),
                        MaxPoolSize = Math.Max(_options.SessionPoolMinPoolSize, _options.SessionPoolMaxPoolSize),
                        MaxIdleCapacity = Math.Max(0, _options.SessionPoolMaxIdleCapacity)
                    };

                    _sessionPool = new SessionPool(
                        poolOptions,
                        _options,
                        OnSessionMessageReceived,
                        OnSessionDisconnected);

                    _sessionPool.Warmup();
                    Logger.Info("Network", "Session 连接池已启用: MinPoolSize={0}, MaxPoolSize={1}",
                        poolOptions.MinPoolSize, poolOptions.MaxPoolSize);
                }
                else
                {
                    Logger.Info("Network", "未启用 Session 连接池，将按需创建 Session");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Network", ex, "启用 Session 连接池失败，退化为按需创建 Session");
                _sessionPool = null;
            }

            // ----------------------------------------
            // 步骤5: 启用消息重试队列（若有配置）
            // ----------------------------------------
            try
            {
                if (_options.MessageRetryCount > 0)
                {
                    var retryOptions = new MessageRetryOptions
                    {
                        MaxRetryCount = _options.MessageRetryCount,
                        BaseIntervalMs = Math.Max(1, _options.MessageRetryIntervalMs),
                        MaxIntervalMs = Math.Max(_options.MessageRetryIntervalMs, 5000)
                    };

                    _messageRetryQueue = new MessageRetryQueue(retryOptions);
                    _messageRetryQueue.Start();
                    Logger.Info("Network", "消息重试队列已启用: MaxRetryCount={0}, BaseIntervalMs={1}",
                        retryOptions.MaxRetryCount, retryOptions.BaseIntervalMs);
                }
                else
                {
                    Logger.Info("Network", "未启用消息重试队列（MessageRetryCount = 0）");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Network", ex, "启用消息重试队列失败，继续运行但不重试");
                _messageRetryQueue = null;
            }

            // ----------------------------------------
            // 步骤6: 开始接受连接
            // ----------------------------------------
            _acceptArgs = new SocketAsyncEventArgs();
            _acceptArgs.Completed += OnAcceptCompleted;

            // 标记服务器为运行状态（必须在 StartAccept 之前）
            _isRunning = true;

            StartAccept();

            // ----------------------------------------
            // 步骤7: 启动心跳管理器
            // ----------------------------------------
            _heartbeatManager.Start();

            Logger.Info("Network", "服务器启动完成，监听端口: {0}", _options.Port);
        }
    }

    /// <summary>
    /// 停止服务器
    /// 
    /// 执行步骤：
    /// 1. 关闭监听套接字（停止接受新连接）
    /// 2. 断开所有现有连接
    /// 3. 停止心跳检测
    /// </summary>
    public void Stop()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步停止服务器（优雅关闭）
    ///
    /// 三阶段关闭：
    /// 1. 关闭监听套接字（停止接受新连接）+ 触发取消令牌
    /// 2. 等待正在处理的消息完成 / 现有连接主动断开（最多 waitForPendingMilliseconds）
    /// 3. 强制断开仍未断开的连接
    ///
    /// 额外步骤：
    /// - 停止心跳检测
    /// - 释放 Session 连接池
    /// - 停止消息重试队列
    /// - 释放证书
    /// </summary>
    /// <param name="waitForPendingMilliseconds">等待待处理消息完成的时间（毫秒）</param>
    /// <param name="cancellationToken">外部取消令牌（可提前触发关闭）</param>
    /// <returns>异步任务</returns>
    public async Task StopAsync(int waitForPendingMilliseconds = 5000, CancellationToken cancellationToken = default)
    {
        CancellationToken linkedToken;
        CancellationTokenSource? ctsToCancel;

        // 在锁内获取需要的信息，但不调用 Cancel（避免死锁）
        lock (_ctsLock)
        {
            if (_cts == null) return; // 未启动
            ctsToCancel = _cts;
            // 与外部令牌链接
            linkedToken = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken).Token;
        }

        // 在锁外触发取消（通知所有热路径）—— Cancel() 可能触发回调，回调不应在锁内执行
        ctsToCancel.Cancel();

        Socket? listenerToClose;
        SocketAsyncEventArgs? acceptArgsToDispose;

        lock (_startLock)
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _isShuttingDown = true;
            _shutdownStartedAt = DateTimeOffset.UtcNow;

            // 在锁内获取需要释放的资源引用，但不执行阻塞操作
            listenerToClose = _listener;
            _listener = null;

            acceptArgsToDispose = _acceptArgs;
            _acceptArgs = null;

            _heartbeatManager.Stop();

            Logger.Info("Network", "开始优雅关闭，等待 {0}ms 处理待完成消息，当前在线={1}",
                waitForPendingMilliseconds, _sessions.Count);
        }

        // 在锁外执行阻塞的 Socket.Close 操作（避免阻塞其他线程）
        try { listenerToClose?.Close(); } catch (Exception ex) { Logger.Error("Network", ex, "关闭监听套接字异常"); }
        try { acceptArgsToDispose?.Dispose(); } catch { }

        // 阶段2：等待剩余连接自然结束 或 超时（⚠️ P0 修复：使用取消令牌，不再轮询）
        if (waitForPendingMilliseconds > 0)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(waitForPendingMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken, timeoutCts.Token);
                try { await WaitForSessionsDrainAsync(linkedCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    if (_sessions.Count > 0)
                        Logger.Warning("Network", "优雅关闭超时，仍有 {0} 个连接未断开", _sessions.Count);
                }
            }
            catch { /* 忽略取消相关异常 */ }
        }

        // 阶段3：强制断开剩余连接（异步断开，不等待）
        var sessions = _sessions.Values.ToArray();
        foreach (var session in sessions)
        {
            // 显式 Discard，告知编译器这是有意为之的 fire-and-forget
            _ = session.DisconnectAsync("服务器关闭");
        }

        _sessions.Clear();

        // 释放 Session 连接池
        try { _sessionPool?.Dispose(); _sessionPool = null; }
        catch (Exception ex) { Logger.Error("Network", ex, "释放 Session 连接池异常"); }

        // 停止消息重试队列
        try { _messageRetryQueue?.Stop(); _messageRetryQueue = null; }
        catch (Exception ex) { Logger.Error("Network", ex, "停止消息重试队列异常"); }

        // 释放证书
        try { _serverCertificate?.Dispose(); } catch { }

        // 释放取消令牌源
        lock (_ctsLock) { _cts?.Dispose(); _cts = null; }

        _isShuttingDown = false;
        Logger.Info("Network", "服务器已停止，断开 {0} 个连接，总耗时 {1}ms",
            sessions.Length,
            (long)(DateTimeOffset.UtcNow - _shutdownStartedAt).TotalMilliseconds);
    }

    /// <summary>
    /// 等待所有会话自然断开（轮询退出条件）
    /// </summary>
    private async Task WaitForSessionsDrainAsync(CancellationToken cancellationToken)
    {
        while (_sessions.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 实现 IAsyncDisposable：推荐在 async using 场景使用。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync(0).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 实现 IDisposable：向后兼容，内部调用异步停机（Fire-and-Forget 模式）。
    /// 推荐使用 <see cref="DisposeAsync"/> 或 <c>await using var server = ...</c>。
    /// </summary>
    public void Dispose()
    {
        // ⚠️ P0 修复：不再同步阻塞异步操作
        // Fire-and-forget 停机（调用方不会等待，但这是安全的——Socket.Close 本身是同步的）
        _ = StopAsync(0);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region 公共方法 - 消息发送

    /// <summary>
    /// 广播消息给所有在线玩家（异步）
    ///
    /// 使用场景：
    /// - 服务器公告
    /// - 全服活动通知
    /// - 系统广播
    ///
    /// 注意：这是一个比较重的操作
    /// 对于 10000 玩家，会同时发送 10000 条消息
    ///
    /// 修复：改为 async Task 而非 async void，异常可被正确观察。
    /// </summary>
    /// <param name="message">要广播的消息</param>
    /// <returns>所有发送任务的聚合任务（失败时返回 faulted task）</returns>
    public async Task BroadcastAsync(IMessage message)
    {
        if (!_isRunning)
            return;

        var sessions = _sessions.Values.ToArray();
        var messageId = MessageSerializer.GetMessageId(message);
        Logger.Debug("Network", "广播消息: MessageId={0}, 玩家数={1}",
            messageId, sessions.Length);

        if (sessions.Length == 0)
            return;

        // 并行发送给所有玩家
        var tasks = new Task[sessions.Length];
        for (var i = 0; i < sessions.Length; i++)
        {
            var session = sessions[i];
            tasks[i] = session.IsConnected
                ? session.SendAsync(message).AsTask()
                : Task.CompletedTask;
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 部分失败是正常的（用户可能已在发送过程中断开）
            Logger.Warning("Network", "广播异常（部分发送失败）: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 获取指定连接的会话
    /// </summary>
    /// <param name="connectionId">连接 ID</param>
    /// <returns>会话对象，如果找不到返回 null</returns>
    public Session? GetSession(long connectionId)
    {
        _sessions.TryGetValue(connectionId, out var session);
        return session;
    }

    /// <summary>
    /// 获取所有会话的快照
    /// </summary>
    /// <returns>会话数组</returns>
    public Session[] GetAllSessions()
    {
        return _sessions.Values.ToArray();
    }

    #endregion

    #region 私有方法 - Accept 处理

    /// <summary>
    /// 开始接受客户端连接
    /// 
    /// 使用 SocketAsyncEventArgs 实现 Accept 的异步操作
    /// 这样可以在连接很多时不阻塞主线程
    /// </summary>
    private void StartAccept()
    {
        if (!_isRunning || _listener == null || _acceptArgs == null)
            return;

        // 重置 AcceptSocket，准备接受下一个连接
        _acceptArgs.AcceptSocket = null;

        // 尝试异步 Accept
        // 如果返回 false，表示操作同步完成（很少见，但需要处理）
        // 如果返回 true，表示操作异步进行，完成时会触发 Completed 事件
        try
        {
            if (!_listener.AcceptAsync(_acceptArgs))
            {
                // 同步完成：直接处理，然后继续接受下一个
                ProcessAccept(_acceptArgs);
                StartAccept();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "发起 Accept 异常");
            // 尝试继续（可能是短暂的错误）
            if (_isRunning)
            {
                Thread.Sleep(100);
                StartAccept();
            }
        }
    }

    /// <summary>
    /// Accept 操作完成时的回调
    /// 
    /// 由 IOCP 线程池调用
    /// </summary>
    private void OnAcceptCompleted(object? sender, SocketAsyncEventArgs e)
    {
        // 处理 Accept 结果
        ProcessAccept(e);

        // 继续接受下一个连接
        // 注意：这是一个循环，服务器会一直运行直到 Stop()
        StartAccept();
    }

    /// <summary>
    /// 处理 Accept 结果
    /// 
    /// 成功时：
    /// 1. 创建新的 Session 对象
    /// 2. 加入会话管理
    /// 3. 开始接收数据
    /// 4. 触发 OnClientConnected 事件
    /// 
    /// 失败时：
    /// 1. 记录错误日志
    /// 2. 什么也不做，等待下一个 Accept
    /// </summary>
    private void ProcessAccept(SocketAsyncEventArgs e)
    {
        if (!_isRunning)
            return;

        // 检查是否有错误
        if (e.SocketError != SocketError.Success)
        {
            Logger.Warning("Network", "Accept 失败: {0}", e.SocketError);
            return;
        }

        // 获取新连接的套接字
        var clientSocket = e.AcceptSocket;
        if (clientSocket == null)
        {
            Logger.Warning("Network", "Accept 返回空 Socket");
            return;
        }

        // 检查是否超过最大连接数
        if (_sessions.Count >= _options.MaxConnections)
        {
            Logger.Warning("Network", "连接数已达上限: {0}, 拒绝新连接",
                _options.MaxConnections);

            try
            {
                clientSocket.Close();
            }
            catch { }
            return;
        }

        // 生成唯一的 ConnectionId
        var connectionId = Interlocked.Increment(ref _nextConnectionId);

        // 创建 Session 对象（优先使用连接池；否则新建）
        Session session;
        if (_sessionPool != null)
        {
            session = _sessionPool.Rent(clientSocket);
        }
        else
        {
            session = new Session(
                connectionId,
                clientSocket,
                _options,
                OnSessionMessageReceived,
                OnSessionDisconnected,
                _serverCertificate);
        }

        // 加入在线会话字典
        if (!_sessions.TryAdd(session.ConnectionId, session))
        {
            // 理论上不会发生（自增 ID 是唯一的）
            Logger.Error("Network", "会话 ID 冲突: {0}", session.ConnectionId);
            try { clientSocket.Close(); } catch { }
            return;
        }

        Logger.Info("Network", "新连接: ConnectionId={0}, 来自={1}, 当前在线={2}",
            session.ConnectionId,
            session.RemoteEndPoint?.ToString() ?? "未知",
            _sessions.Count);

        // 绑定重试队列（若启用）
        if (_messageRetryQueue != null)
        {
            session.RetryQueue = _messageRetryQueue;
        }

        // 触发连接事件（异步执行，避免阻塞 IOCP 线程池）
        // ⚠️ 注意：事件处理在独立线程池线程上执行，不阻塞后续的 StartReceive
        // 如果事件处理器很慢，不会影响其他连接的 Accept
        _ = Task.Run(() =>
        {
            try
            {
                OnClientConnected?.Invoke(session);
            }
            catch (Exception ex) when (!IsFatalException(ex))
            {
                Logger.Error("Network", ex, "OnClientConnected 事件处理异常");
            }
        });

        // 开始接收数据（立即启动，不等待事件处理完成）
        session.StartReceive();
    }

    #endregion

    #region 私有方法 - 会话回调

    /// <summary>
    /// Session 收到消息时的回调
    /// 
    /// 这里把消息转发给 OnMessageReceived 事件
    /// 通常由 MessageRouter 处理消息路由
    /// </summary>
    private void OnSessionMessageReceived(Session session, IMessage message)
    {
        try
        {
            // 触发 OnMessageReceived 事件，外部可订阅此事件处理消息
            OnMessageReceived?.Invoke(session, message);

            // 默认消息路由（兜底处理），避免未订阅时消息丢失
            _ = MessageRouter.Instance.RouteAsync(session, message);
        }
        catch (Exception ex) when (!IsFatalException(ex))
        {
            // 捕获非致命异常，防止消息处理异常导致连接断开
            // 致命异常（OOM/StackOverflow 等）会向上传播，让进程终止
            var msgId = MessageSerializer.GetMessageId(message);
            Logger.Error("Network", ex, "消息处理异常: ConnectionId={0}, MessageId={1}",
                session.ConnectionId, msgId);
        }
    }

    /// <summary>
    /// Session 断开时的回调
    /// 
    /// 1. 从会话管理中移除
    /// 2. 若启用连接池，则归还 Session
    /// 3. 触发 OnClientDisconnected 事件
    /// </summary>
    private void OnSessionDisconnected(Session session, string reason)
    {
        // 从会话管理中移除
        _sessions.TryRemove(session.ConnectionId, out _);

        Logger.Info("Network", "连接断开: ConnectionId={0}, 原因={1}, 当前在线={2}",
            session.ConnectionId, reason, _sessions.Count);

        // 触发断开事件（在归还 Session 之前触发，便于业务层做日志/数据保存）
        try
        {
            OnClientDisconnected?.Invoke(session, reason);
        }
        catch (Exception ex) when (!IsFatalException(ex))
        {
            Logger.Error("Network", ex, "OnClientDisconnected 事件处理异常");
        }

        // 若启用连接池，则归还；否则 Session 自身 Disconnect 已释放资源
        try
        {
            if (_sessionPool != null)
            {
                _sessionPool.Return(session);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "归还 Session 到连接池失败");
        }
    }

    /// <summary>
    /// 证书更新事件处理
    /// 
    /// 当证书热加载成功后，更新服务器内部引用的证书
    /// 新连接将使用新证书进行 TLS 握手
    /// </summary>
    private void OnCertificateUpdated(object? sender, X509Certificate2 newCertificate)
    {
        Logger.Info("Network", "TLS 证书已更新，新证书主题: {0}", newCertificate.Subject);
        
        // 更新服务器证书引用
        _serverCertificate = newCertificate;
        
        // 更新证书热加载次数指标
        try
        {
            var reloadCountGauge = MetricsCollector.Instance.GetGauge("tls.cert_reload_count");
            var currentValue = MetricsCollector.Instance.GetValue("tls.cert_reload_count") ?? 0;
            
            if (reloadCountGauge != null)
            {
                reloadCountGauge.Set(currentValue + 1);
            }
            else
            {
                MetricsCollector.Instance.RegisterGauge("tls.cert_reload_count", "证书热加载次数");
                var gauge = MetricsCollector.Instance.GetGauge("tls.cert_reload_count");
                gauge?.Set(1);
            }
        }
        catch { }
    }

    #endregion

    #region 私有方法 - 异常处理辅助

    /// <summary>
    /// 判断异常是否为致命异常（不应被捕获）
    ///
    /// 致命异常包括：
    /// - OutOfMemoryException：内存耗尽，无法继续运行
    /// - StackOverflowException：栈溢出，无法恢复
    /// - ThreadAbortException：线程被终止（.NET Framework 遗留）
    /// - AccessViolationException：内存访问违规
    ///
    /// 注意：ExecutionEngineException 在 .NET 5+ 中已过时，不再使用。
    /// </summary>
    private static bool IsFatalException(Exception ex)
    {
        return ex is OutOfMemoryException ||
               ex is StackOverflowException ||
               ex is ThreadAbortException ||
               ex is AccessViolationException;
    }

    #endregion
}
