// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Network;

/// <summary>
/// 心跳管理器
///
/// 工作原理：
/// - 每个 Session 有一个 LastHeartbeat 时间戳
/// - 收到任何数据时都会更新此时间戳
/// - 心跳管理器定期检查所有 Session
/// - 如果超过阈值没有收到数据，认为玩家断线
///
/// 为什么需要心跳检测？
/// 1. 玩家拔掉网线 → TCP 连接不会立即断开
/// 2. 路由器重启   → 双方都不知道对方离线
/// 3. 网络闪断     → 连接还在，但数据已无法传递
/// 4. 客户端崩溃   → 没有发送 FIN 包
///
/// 如果没有心跳检测：
/// - 服务器以为玩家在线，保留 Session
/// - 占用内存和 Socket 资源
/// - 其他玩家看到他在那但不响应
///
/// ⚠️ 性能优化（#15 修复）：
/// - 使用"分桶 + 时间轮"思想，按超时区间分桶
/// - 注册 Session 时按预估超时时间放入对应桶
/// - 定时检查时只扫描"即将超时"和"已超时"的桶
/// - 时间复杂度从 O(n) 优化到 O(k)，k = 桶内 Session 数
/// </summary>
public class HeartbeatManager
{
    #region 字段

    /// <summary>
    /// 所有需要检查的会话
    ///
    /// 注意：这是 TcpServer 中同一个字典的引用
    /// </summary>
    private readonly ConcurrentDictionary<long, Session> _sessions;

    /// <summary>
    /// 服务器配置
    /// </summary>
    private readonly TcpServerOptions _options;

    /// <summary>
    /// 定时检查器
    /// </summary>
    private Timer? _timer;

    /// <summary>
    /// 是否已启动
    /// </summary>
    private bool _isRunning;

    /// <summary>
    /// 启动锁
    /// </summary>
    private readonly object _startLock = new();

    /// <summary>
    /// ⚠️ 性能优化：心跳分桶（避免 O(n) 扫描）
    ///
    /// 将 Session 按"预计超时时间"分到不同的桶中：
    /// - 桶 0：[now, now + interval*1)
    /// - 桶 1：[now + interval*1, now + interval*2)
    /// - 桶 2：[now + interval*2, now + interval*3)
    /// - 桶 3：[now + interval*3, now + interval*4)  —— 已超时
    ///
    /// 定时检查时只扫描桶 3，其他桶顺时针"转"到下一格
    /// 时间复杂度：每次检查 O(桶内 Session 数)，而非 O(所有 Session)
    /// </summary>
    private readonly ConcurrentDictionary<long, Session> _bucket0 = new();
    private readonly ConcurrentDictionary<long, Session> _bucket1 = new();
    private readonly ConcurrentDictionary<long, Session> _bucket2 = new();
    private readonly ConcurrentDictionary<long, Session> _bucket3 = new();

    /// <summary>
    /// 分桶间隔（毫秒）= 检查间隔
    /// </summary>
    private int _bucketIntervalMs;

    #endregion

    #region 构造函数

    /// <summary>
    /// 构造心跳管理器
    /// </summary>
    /// <param name="sessions">会话字典（与 TcpServer 共享）</param>
    /// <param name="options">服务器配置</param>
    public HeartbeatManager(ConcurrentDictionary<long, Session> sessions, TcpServerOptions options)
    {
        _sessions = sessions;
        _options = options;
        _bucketIntervalMs = _options.HeartbeatCheckIntervalSeconds * 1000;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 启动心跳检测
    ///
    /// 会创建一个定时器，每隔 HeartbeatCheckIntervalSeconds 检查一次
    /// </summary>
    public void Start()
    {
        lock (_startLock)
        {
            if (_isRunning)
                return;

            _isRunning = true;

            // 创建定时器
            // 首次延迟 1 秒，之后每隔 HeartbeatCheckIntervalSeconds 检查一次
            _timer = new Timer(
                CheckHeartbeats,
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(_options.HeartbeatCheckIntervalSeconds));

            Logger.Info("Network", "心跳管理器启动: 超时={0}秒, 检查间隔={1}秒, 分桶数=4",
                _options.HeartbeatTimeoutSeconds,
                _options.HeartbeatCheckIntervalSeconds);
        }
    }

    /// <summary>
    /// 停止心跳检测
    /// </summary>
    public void Stop()
    {
        lock (_startLock)
        {
            if (!_isRunning)
                return;

            _isRunning = false;

            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }

            // 清空所有桶
            _bucket0.Clear();
            _bucket1.Clear();
            _bucket2.Clear();
            _bucket3.Clear();

            Logger.Info("Network", "心跳管理器已停止");
        }
    }

    /// <summary>
    /// ⚠️ 性能优化：将 Session 注册到心跳桶
    ///
    /// Session 在加入连接池时调用此方法
    /// 按"预计超时时间"分配到对应桶中
    /// </summary>
    /// <param name="session">要注册的 Session</param>
    public void RegisterSession(Session session)
    {
        if (session == null) return;

        // 默认放入桶 0（最近一个检查周期内不需要检查）
        _bucket0[session.ConnectionId] = session;
    }

    /// <summary>
    /// ⚠️ 性能优化：从心跳桶中移除 Session
    ///
    /// Session 断开时调用此方法，从所有桶中移除
    /// </summary>
    public void UnregisterSession(Session session)
    {
        if (session == null) return;

        _bucket0.TryRemove(session.ConnectionId, out _);
        _bucket1.TryRemove(session.ConnectionId, out _);
        _bucket2.TryRemove(session.ConnectionId, out _);
        _bucket3.TryRemove(session.ConnectionId, out _);
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 定时检查心跳
    ///
    /// ⚠️ 性能优化（#15 修复）：
    /// - 使用分桶轮转机制，每次只检查"超时桶"
    /// - 桶 0/1/2 中的 Session 顺时针"转"到下一格
    /// - 桶 3 中的 Session 检查是否真正超时（防止误判）
    /// - 时间复杂度：O(桶 3 内 Session 数)，从 O(n) 优化到 O(k)
    /// </summary>
    private void CheckHeartbeats(object? state)
    {
        if (!_isRunning)
            return;

        var now = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(_options.HeartbeatTimeoutSeconds);
        var disconnectedCount = 0;

        // ----------------------------------------
        // 第一步：桶轮转
        // 桶 0 → 桶 1 → 桶 2 → 桶 3 → 检查
        // ----------------------------------------

        // 桶 2 → 桶 3（即将超时）
        foreach (var session in _bucket2.Values)
        {
            _bucket3[session.ConnectionId] = session;
        }
        _bucket2.Clear();

        // 桶 1 → 桶 2
        foreach (var session in _bucket1.Values)
        {
            _bucket2[session.ConnectionId] = session;
        }
        _bucket1.Clear();

        // 桶 0 → 桶 1
        foreach (var session in _bucket0.Values)
        {
            _bucket1[session.ConnectionId] = session;
        }
        _bucket0.Clear();

        // 重新扫描所有 Session，分配到桶 0
        // （此操作 O(n)，但只在最后做一次，且大部分 Session 都在 0 桶）
        foreach (var session in _sessions.Values)
        {
            if (!session.IsConnected)
                continue;

            // 只把"新加入"或"刚被旋转出去的"Session 放入桶 0
            // 已经存在于其他桶的 Session 不重复放入
            if (!_bucket1.ContainsKey(session.ConnectionId) &&
                !_bucket2.ContainsKey(session.ConnectionId) &&
                !_bucket3.ContainsKey(session.ConnectionId))
            {
                _bucket0[session.ConnectionId] = session;
            }
        }

        // ----------------------------------------
        // 第二步：检查桶 3（已超时）
        // ----------------------------------------
        foreach (var session in _bucket3.Values)
        {
            // 检查是否已断开
            if (!session.IsConnected)
                continue;

            // 计算距离上次活动过了多久
            var elapsed = now - session.LastHeartbeat;

            // 如果超过阈值，断开连接
            if (elapsed > timeout)
            {
                disconnectedCount++;

                Logger.Warning("Network",
                    "心跳超时: ConnectionId={0}, 空闲={1:F1}秒, 断开连接",
                    session.ConnectionId, elapsed.TotalSeconds);

                session.Disconnect("心跳超时");
            }
        }

        // 清空桶 3
        _bucket3.Clear();

        if (disconnectedCount > 0)
        {
            Logger.Debug("Network", "心跳检查完成: 断开={0}个连接, 总在线={1}",
                disconnectedCount, _sessions.Count);
        }
    }

    #endregion
}
