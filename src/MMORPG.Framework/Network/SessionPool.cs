// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Net.Sockets;
using Google.Protobuf;
using MMORPG.Framework.Logging;
using MMORPG.Framework.Observability;

namespace MMORPG.Framework.Network;

/// <summary>
/// Session 连接池
/// 
/// 提供 Session 对象的池化管理，避免频繁创建/销毁 Session
/// 减少 GC 压力，提升连接接入速度
/// 
/// 使用流程：
/// 1. 初始化时预热 Session 池
/// 2. 新连接到达时从池中获取 Session
/// 3. 连接断开后将 Session 归还池中
/// 4. 连接池满时丢弃最老的空闲 Session
/// 
/// 使用示例：
/// <code>
/// var pool = new SessionPool(new SessionPoolOptions
/// {
///     MinPoolSize = 50,
///     MaxPoolSize = 1000
/// });
/// pool.Warmup();  // 预热 50 个 Session
/// 
/// var session = pool.Rent(socket);  // 获取 Session
/// // ... 使用 session ...
/// pool.Return(session);  // 归还 Session
/// </code>
/// </summary>
public class SessionPool
{
    #region 字段

    /// <summary>
    /// 空闲 Session 队列
    /// </summary>
    private readonly ConcurrentQueue<Session> _idleSessions;

    /// <summary>
    /// 正在使用的 Session 计数
    /// </summary>
    private int _activeCount;

    /// <summary>
    /// 池总大小
    /// </summary>
    private int _totalCount;

    /// <summary>
    /// 配置
    /// </summary>
    private readonly SessionPoolOptions _options;

    /// <summary>
    /// Session 创建委托（允许外部注入自定义创建逻辑；默认使用带 Socket 重置的 Session）
    /// </summary>
    private readonly Func<Socket, Session> _sessionFactory;

    /// <summary>
    /// 消息接收回调（用于 Rent 出来的 Session 统一配置）
    /// </summary>
    private readonly Action<Session, IMessage> _onMessageReceived;

    /// <summary>
    /// 断开回调（用于 Rent 出来的 Session 统一配置）
    /// </summary>
    private readonly Action<Session, string> _onDisconnected;

    /// <summary>
    /// 服务器配置（在 Warmup/ResetWith 时使用）
    /// </summary>
    private readonly TcpServerOptions _serverOptions;

    /// <summary>
    /// 是否已经预热完成
    /// </summary>
    private bool _initialized;

    #endregion

    #region 属性

    /// <summary>
    /// 当前空闲 Session 数量
    /// </summary>
    public int IdleCount => _idleSessions.Count;

    /// <summary>
    /// 当前活动 Session 数量
    /// </summary>
    public int ActiveCount => _activeCount;

    /// <summary>
    /// 池总大小
    /// </summary>
    public int TotalCount => _totalCount;

    /// <summary>
    /// 是否已满
    /// </summary>
    public bool IsFull => _totalCount >= _options.MaxPoolSize;

    #endregion

    #region 构造函数

    /// <summary>
    /// 构造 Session 连接池
    /// 
    /// 若未提供 sessionFactory，则使用默认实现：
    ///   - 优先从空闲队列中取出已预热的 Session，并调用 ResetWith(socket)
    ///   - 否则创建新的 Session
    /// </summary>
    /// <param name="options">连接池配置（null 时使用默认值）</param>
    /// <param name="serverOptions">服务器网络配置（用于创建 Session 时匹配参数）</param>
    /// <param name="onMessageReceived">消息接收回调（由 TcpServer 提供）</param>
    /// <param name="onDisconnected">断开回调（由 TcpServer 提供）</param>
    /// <param name="sessionFactory">自定义 Session 创建函数（可选）</param>
    public SessionPool(
        SessionPoolOptions? options,
        TcpServerOptions? serverOptions,
        Action<Session, IMessage>? onMessageReceived,
        Action<Session, string>? onDisconnected,
        Func<Socket, Session>? sessionFactory = null)
    {
        _options = options ?? new SessionPoolOptions();
        _serverOptions = serverOptions ?? new TcpServerOptions();
        _onMessageReceived = onMessageReceived ?? ((_, _) => { });
        _onDisconnected = onDisconnected ?? ((_, _) => { });
        _idleSessions = new ConcurrentQueue<Session>();
        _activeCount = 0;
        _totalCount = 0;

        // 默认工厂：从池中取出空闲 Session 并绑定 Socket；没有空闲 Session 时新建
        _sessionFactory = sessionFactory ?? ((socket) =>
        {
            if (_idleSessions.TryDequeue(out var idleSession))
            {
                idleSession.ResetWith(socket, _serverOptions, _onMessageReceived, _onDisconnected);
                return idleSession;
            }
            var newSession = new Session(socket, _onMessageReceived, _onDisconnected);
            Interlocked.Increment(ref _totalCount);
            return newSession;
        });
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 预热连接池
    /// 
    /// 创建 MinPoolSize 个空闲 Session，放入队列中
    /// 提升首次连接的响应速度
    /// </summary>
    public void Warmup()
    {
        if (_initialized)
            return;

        Logger.Info("Network", "开始预热 Session 连接池: MinPoolSize={0}", _options.MinPoolSize);

        // 创建一批空闲 Session 对象（无 Socket 绑定）
        int created = 0;
        for (int i = 0; i < _options.MinPoolSize; i++)
        {
            try
            {
                var session = new Session(null, _onMessageReceived, _onDisconnected);
                _idleSessions.Enqueue(session);
                Interlocked.Increment(ref _totalCount);
                created++;
            }
            catch (Exception ex)
            {
                Logger.Error("Network", ex, "预热 Session 失败");
                break;
            }
        }

        _initialized = true;

        // 更新指标
        try
        {
            MetricsCollector.Instance.RegisterGauge("session_pool.idle_count", "连接池空闲Session数量");
            MetricsCollector.Instance.RegisterGauge("session_pool.active_count", "连接池活动Session数量");
            MetricsCollector.Instance.RegisterGauge("session_pool.total_count", "连接池总Session数量");
        }
        catch { }

        Logger.Info("Network", "Session 连接池预热完成: 已创建 {0} 个空闲 Session", created);
    }

    /// <summary>
    /// 获取一个已绑定 Socket 的 Session
    /// 
    /// 优先使用工厂函数（通常是从空闲队列 + ResetWith）；
    /// 若池已满，则创建临时 Session 并发出警告
    /// </summary>
    public Session Rent(Socket socket)
    {
        // 允许工厂按自己的策略创建 Session
        var session = _sessionFactory(socket);
        Interlocked.Increment(ref _activeCount);
        UpdateMetrics();
        return session;
    }

    /// <summary>
    /// 归还 Session
    /// 
    /// 空闲队列未满时放入队列；满时直接释放资源
    /// </summary>
    /// <param name="session">使用完毕的 Session</param>
    public void Return(Session session)
    {
        if (session == null)
            return;

        Interlocked.Decrement(ref _activeCount);

        // 检查空闲队列是否还有空间（限制最大值避免内存溢出）
        if (_idleSessions.Count < _options.MaxIdleCapacity)
        {
            // 清理 Session 状态，放入空闲队列
            try
            {
                session.ClearSocket();
                _idleSessions.Enqueue(session);
                UpdateMetrics();
                return;
            }
            catch (Exception ex)
            {
                Logger.Error("Network", ex, "归还 Session 到池失败");
            }
        }

        // 释放 Session 资源
        try
        {
            session.Dispose();
            Interlocked.Decrement(ref _totalCount);
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "释放 Session 资源失败");
        }

        UpdateMetrics();
    }

    /// <summary>
    /// 清理过期的空闲 Session
    /// 
    /// 定期检查并清理长时间未使用的 Session
    /// </summary>
    public void CleanupExpired()
    {
        // 超过 MaxIdleCapacity 的部分，移除多余的空闲 Session
        while (_idleSessions.Count > _options.MaxIdleCapacity)
        {
            if (_idleSessions.TryDequeue(out var session))
            {
                try
                {
                    session.Dispose();
                    Interlocked.Decrement(ref _totalCount);
                }
                catch (Exception ex)
                {
                    Logger.Error("Network", ex, "清理过期 Session 失败");
                }
            }
            else
            {
                break;
            }
        }

        UpdateMetrics();
    }

    /// <summary>
    /// 获取连接池统计信息
    /// </summary>
    public string GetStats()
    {
        return string.Format(
            "SessionPool[Idle={0}, Active={1}, Total={2}, Max={3}]",
            IdleCount, ActiveCount, TotalCount, _options.MaxPoolSize);
    }

    /// <summary>
    /// 释放所有资源
    /// </summary>
    public void Dispose()
    {
        Logger.Info("Network", "释放 Session 连接池: 总大小={0}", _totalCount);

        while (_idleSessions.TryDequeue(out var session))
        {
            try
            {
                session.Dispose();
            }
            catch { }
        }

        _totalCount = 0;
        _activeCount = 0;
        _initialized = false;
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 更新指标
    /// </summary>
    private void UpdateMetrics()
    {
        try
        {
            var idleGauge = MetricsCollector.Instance.GetGauge("session_pool.idle_count");
            var activeGauge = MetricsCollector.Instance.GetGauge("session_pool.active_count");
            var totalGauge = MetricsCollector.Instance.GetGauge("session_pool.total_count");

            idleGauge?.Set(_idleSessions.Count);
            activeGauge?.Set(_activeCount);
            totalGauge?.Set(_totalCount);
        }
        catch { }
    }

    #endregion
}

/// <summary>
/// Session 连接池配置
/// </summary>
public class SessionPoolOptions
{
    /// <summary>
    /// 最小池大小（预热时创建）
    /// </summary>
    public int MinPoolSize { get; set; } = 50;

    /// <summary>
    /// 最大池大小
    /// </summary>
    public int MaxPoolSize { get; set; } = 1000;

    /// <summary>
    /// 最大空闲容量
    /// 
    /// 超过此值时，归还的 Session 会被释放
    /// </summary>
    public int MaxIdleCapacity { get; set; } = 500;
}
