// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

// ====================================================================
// 线程安全集合
//
// 提供线程安全的集合类，用于管理共享状态。
// 这些类封装了 .NET 的 Concurrent 集合，提供更易用的 API。
//
// 主要使用场景：
// - Session 字典（玩家连接管理）
// - 全局配置（线程安全的读写）
// - 计数器（原子增减）
//
// 为什么不用直接用 Concurrent 集合？
// - API 更简洁（TryAddOrUpdate、GetOrAdd）
// - 提供更符合游戏服务器场景的方法
// - 统一的错误处理和日志
// ====================================================================

using System.Collections.Concurrent;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Threading;

/// <summary>
/// 线程安全的集合管理
///
/// 封装了游戏服务器常用的线程安全集合：
/// - 网络消息队列（MessageChannel）
/// - 数据库请求队列
/// - 数据库响应队列
///
/// 使用示例：
/// <code>
/// var collections = new ThreadSafeCollections();
///
/// // IO 线程写入网络消息
/// collections.NetworkQueue.Write((session, message));
///
/// // 主线程在每帧读取并处理
/// foreach (var (session, message) in collections.NetworkQueue.DrainAll())
/// {
///     ProcessMessage(session, message);
/// }
/// </code>
/// </summary>
public class ThreadSafeCollections
{
    #region 字段

    /// <summary>
    /// Session 字典（IO 线程写入，主线程读取）
    /// 存储所有在线玩家的连接信息
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, object> _sessions = new();

    /// <summary>
    /// 网络接收队列（IO 线程 -> 主线程）
    /// 存储客户端发来的网络消息
    /// </summary>
    private readonly object _networkQueuePlaceholder = new();

    #endregion

    #region 属性

    /// <summary>
    /// 当前在线玩家数
    /// </summary>
    public int SessionCount => _sessions.Count;

    #endregion

    #region 公共方法

    /// <summary>
    /// 添加或更新一个 Session
    /// </summary>
    /// <param name="connectionId">连接 ID</param>
    /// <param name="session">会话对象</param>
    public void AddOrUpdateSession(long connectionId, object session)
    {
        _sessions.AddOrUpdate(connectionId, session, (id, old) => session);
    }

    /// <summary>
    /// 移除一个 Session
    /// </summary>
    /// <param name="connectionId">连接 ID</param>
    /// <returns>true 表示成功移除</returns>
    public bool TryRemoveSession(long connectionId)
    {
        return _sessions.TryRemove(connectionId, out _);
    }

    /// <summary>
    /// 尝试获取一个 Session
    /// </summary>
    /// <param name="connectionId">连接 ID</param>
    /// <param name="session">输出参数：会话对象</param>
    /// <returns>true 表示找到</returns>
    public bool TryGetSession(long connectionId, out object? session)
    {
        return _sessions.TryGetValue(connectionId, out session);
    }

    /// <summary>
    /// 获取所有 Session 的快照
    /// 注意：这会创建一个新数组，适合偶尔使用
    /// </summary>
    /// <returns>Session 数组</returns>
    public object[] GetAllSessions()
    {
        return _sessions.Values.ToArray();
    }

    #endregion
}

/// <summary>
/// 原子计数器
///
/// 使用 Interlocked 实现无锁的原子增减
/// 用于：玩家人数、消息计数等统计
/// </summary>
public class AtomicCounter
{
    private long _value;

    /// <summary>
    /// 当前值
    /// </summary>
    public long Value => Interlocked.Read(ref _value);

    /// <summary>
    /// 自增 1
    /// </summary>
    /// <returns>自增后的值</returns>
    public long Increment()
    {
        return Interlocked.Increment(ref _value);
    }

    /// <summary>
    /// 自减 1
    /// </summary>
    /// <returns>自减后的值</returns>
    public long Decrement()
    {
        return Interlocked.Decrement(ref _value);
    }

    /// <summary>
    /// 增加指定值
    /// </summary>
    /// <param name="value">增量</param>
    /// <returns>增加后的值</returns>
    public long Add(long value)
    {
        return Interlocked.Add(ref _value, value);
    }

    /// <summary>
    /// 设置新值（返回旧值）
    /// </summary>
    /// <param name="newValue">新值</param>
    /// <returns>旧值</returns>
    public long Set(long newValue)
    {
        return Interlocked.Exchange(ref _value, newValue);
    }

    /// <summary>
    /// 重置为 0
    /// </summary>
    /// <returns>重置前的值</returns>
    public long Reset()
    {
        return Set(0);
    }
}
