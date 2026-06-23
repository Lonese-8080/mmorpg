// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

// ====================================================================
// 消息队列 - MessageChannel
//
// 这是线程模型的核心组件之一。
// 生产者（IO 线程）把消息放入队列，消费者（主线程）在每帧取出消息处理。
//
// 基于 .NET Channel<T> 实现，这是 .NET Core 2.0+ 推荐的高性能队列。
//
// 为什么用 Channel？
// ┌─────────────────────────────────────────────────────────────┐
// │                                                               │
// │ BlockingCollection vs Channel                                │
// │                                                               │
// │ BlockingCollection：                                         │
// │   ✅ API 简单                                                │
// │   ❌ 内部有锁（并发性能一般）                                 │
// │   ❌ .NET Framework 遗留 API                                 │
// │                                                               │
// │ Channel<T>：                                                 │
// │   ✅ 无锁实现（基于 CAS 操作）                                │
// │   ✅ 支持异步读写（async/await）                              │
// │   ✅ 单消费者/单生产者模式可以非常高效                         │
// │   ✅ .NET 官方推荐的现代队列                                  │
// │   ❌ API 稍复杂一点                                          │
// │                                                               │
// │ 对于游戏服务器：                                              │
// │   我们的场景是：多线程写入（IO 线程），单线程读取（主线程）     │
// │   Channel<T> 完美匹配此场景                                   │
// │                                                               │
// └─────────────────────────────────────────────────────────────┘
// ====================================================================

using System.Threading.Channels;

namespace MMORPG.Framework.Threading;

/// <summary>
/// 消息队列 - 线程安全的消息通道
///
/// 用于：
/// - 网络接收队列（IO 线程 -> 主线程）
/// - 数据库响应队列（DB 线程 -> 主线程）
/// - 发送队列（主线程 -> IO 线程）
///
/// 设计原则：
/// 1️⃣ 写入永远不阻塞（队列有容量限制，满了会丢弃旧消息）
/// 2️⃣ 读取永远不阻塞（空队列返回 false）
/// 3️⃣ 单线程读取，多线程写入（我们的主要场景）
///
/// 使用示例：
/// <code>
/// var channel = new MessageChannel&lt;string&gt;();
///
/// // 生产者（IO 线程）
/// channel.Write("玩家登录");
///
/// // 消费者（主线程，每帧调用）
/// foreach (var msg in channel.DrainAll())
/// {
///     Console.WriteLine(msg);
/// }
/// </code>
/// </summary>
/// <typeparam name="T">消息类型</typeparam>
public class MessageChannel<T>
{
    #region 字段

    /// <summary>
    /// 内部的 Channel 对象
    /// MessageChannel 是 .NET 的高性能并发队列实现
    /// </summary>
    private readonly Channel<T> _channel;

    /// <summary>
    /// 队列容量（0 = 无界）
    /// </summary>
    private readonly int _capacity;

    /// <summary>
    /// 内部计数器（使用 Interlocked 实现线程安全）
    ///
    /// ⚠️ 注意：对于有界队列（DropOldest 模式），此计数可能与实际队列大小不一致，
    /// 因为 DropOldest 会丢弃旧消息但不通知我们。
    /// 建议使用 Count 属性获取准确计数（它会根据队列类型选择正确的计数方式）。
    /// </summary>
    private long _count;

    /// <summary>
    /// 是否为有界队列
    /// </summary>
    private readonly bool _isBounded;

    #endregion

    #region 属性

    /// <summary>
    /// 队列是否为空
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            // 对于有界队列，使用 Channel 内置判断
            if (_isBounded)
            {
                try { return _channel.Reader.Count == 0; }
                catch { return Interlocked.Read(ref _count) == 0; }
            }
            return Interlocked.Read(ref _count) == 0;
        }
    }

    /// <summary>
    /// 当前队列中的消息数
    ///
    /// ⚠️ 对于有界队列（DropOldest 模式），使用 Channel.Reader.Count 获取准确值
    /// 因为 DropOldest 会丢弃旧消息但我们的计数器无法感知。
    /// </summary>
    public int Count
    {
        get
        {
            // 对于有界队列，使用 Channel 内置计数（更准确）
            if (_isBounded)
            {
                try { return _channel.Reader.Count; }
                catch { return (int)Interlocked.Read(ref _count); }
            }
            return (int)Interlocked.Read(ref _count);
        }
    }

    /// <summary>
    /// 队列容量
    /// </summary>
    public int Capacity => _capacity;

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建有界消息队列
    ///
    /// 当队列满时，新消息会覆盖最旧的消息
    /// 这是游戏服务器的合理策略：
    /// - 防止队列无限增长（内存溢出）
    /// - 丢失旧消息比丢失新消息更合理
    /// </summary>
    /// <param name="capacity">队列容量，0 表示无界</param>
    public MessageChannel(int capacity = 0)
    {
        _capacity = capacity;
        _isBounded = capacity > 0;

        if (capacity <= 0)
        {
            // 无界队列：允许存储任意多消息
            _channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
            {
                SingleReader = true,   // 只有一个消费者（主线程）
                SingleWriter = false,  // 多个生产者（多个 IO 线程）
                AllowSynchronousContinuations = false
            });
        }
        else
        {
            // 有界队列：队列满时，丢弃最旧的消息
            _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false
            });
        }
    }

    #endregion

    #region 公共方法 - 写入

    /// <summary>
    /// 同步写入消息
    ///
    /// 线程安全，多个线程可以同时调用
    ///
    /// 注意：
    /// - 对于无界队列，写入永远成功
    /// - 对于有界队列（DropOldest），满时会自动丢弃最旧的消息，写入仍然成功
    /// - 此方法返回 true 表示写入成功（DropOldest 模式下永远返回 true）
    /// </summary>
    /// <param name="item">要写入的消息</param>
    /// <returns>true 表示写入成功</returns>
    public bool Write(T item)
    {
        // DropOldest 模式下 TryWrite 永远成功（除非 Channel 已关闭）
        // 对于无界队列，TryWrite 也永远成功
        if (_channel.Writer.TryWrite(item))
        {
            // 只对无界队列维护计数（有界队列使用 Channel.Reader.Count）
            if (!_isBounded)
            {
                Interlocked.Increment(ref _count);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// 异步写入消息
    ///
    /// 非阻塞：立即尝试写入，与同步的 Write() 行为一致
    /// 队列满时会自动丢弃最旧的消息（BoundedChannelFullMode.DropOldest）
    /// 返回 true 表示写入成功，false 表示写入失败
    ///
    /// 注意：如果确实需要等待队列空位，请使用带 CancellationToken 的重载
    /// </summary>
    public ValueTask<bool> WriteAsync(T item, CancellationToken cancellationToken = default)
    {
        // 优先使用 TryWrite（无锁操作）
        if (_channel.Writer.TryWrite(item))
        {
            // 只对无界队列维护计数
            if (!_isBounded)
            {
                Interlocked.Increment(ref _count);
            }
            return new ValueTask<bool>(true);
        }

        // TryWrite 失败时，走异步等待路径（通常不会发生，除非 Channel 已关闭）
        return WriteAsyncCore(item, cancellationToken);
    }

    /// <summary>
    /// 异步写入的核心等待逻辑
    /// </summary>
    private async ValueTask<bool> WriteAsyncCore(T item, CancellationToken cancellationToken)
    {
        try
        {
            await _channel.Writer.WriteAsync(item, cancellationToken);
            // 只对无界队列维护计数
            if (!_isBounded)
            {
                Interlocked.Increment(ref _count);
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 批量写入消息
    /// </summary>
    public int WriteRange(IEnumerable<T> items)
    {
        var count = 0;
        foreach (var item in items)
        {
            if (_channel.Writer.TryWrite(item))
            {
                // 只对无界队列维护计数
                if (!_isBounded)
                {
                    Interlocked.Increment(ref _count);
                }
                count++;
            }
        }
        return count;
    }

    #endregion

    #region 公共方法 - 读取

    /// <summary>
    /// 尝试读取一条消息
    ///
    /// 这是最基础的读取方式，用于需要精细控制的场景
    /// </summary>
    /// <param name="item">输出参数：读取到的消息</param>
    /// <returns>true 表示读取成功，false 表示队列为空</returns>
    public bool TryRead(out T? item)
    {
        if (_channel.Reader.TryRead(out item))
        {
            // 只对无界队列维护计数
            if (!_isBounded)
            {
                Interlocked.Decrement(ref _count);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// 取出队列中所有消息（一次性清空队列）
    ///
    /// 主线程每帧调用一次这个方法，取出所有待处理的消息
    /// 这是游戏服务器的标准模式：每帧清空队列，立即处理
    /// </summary>
    /// <returns>所有消息的可枚举对象</returns>
    public IEnumerable<T> DrainAll()
    {
        // 循环读取直到队列为空
        while (_channel.Reader.TryRead(out var item))
        {
            // 只对无界队列维护计数
            if (!_isBounded)
            {
                Interlocked.Decrement(ref _count);
            }
            yield return item;
        }
    }

    /// <summary>
    /// 取出最多 N 条消息
    ///
    /// 用于限制单帧处理数量，防止帧率抖动过大
    /// 例如：每帧最多处理 100 条网络消息
    /// </summary>
    /// <param name="maxCount">最大处理数量</param>
    /// <returns>消息列表，数量不超过 maxCount</returns>
    public List<T> DrainUpTo(int maxCount)
    {
        var results = new List<T>(Math.Min(maxCount, 64));

        for (var i = 0; i < maxCount; i++)
        {
            if (_channel.Reader.TryRead(out var item))
            {
                // 只对无界队列维护计数
                if (!_isBounded)
                {
                    Interlocked.Decrement(ref _count);
                }
                results.Add(item);
            }
            else
                break;
        }

        return results;
    }

    /// <summary>
    /// 异步读取一条消息（等待直到有消息）
    ///
    /// 适用场景：后台线程等待消息
    /// 主线程不应该调用此方法（会阻塞主循环）
    /// </summary>
    public ValueTask<T> ReadAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }

    #endregion

    #region 公共方法 - 生命周期

    /// <summary>
    /// 标记完成（不再接收新消息）
    ///
    /// 当队列不再需要写入时调用此方法
    /// 已经在队列中的消息仍可以被读取
    /// </summary>
    public void Complete()
    {
        _channel.Writer.Complete();
    }

    /// <summary>
    /// 清空队列（丢弃所有消息）
    ///
    /// 用于服务器重启、紧急清空等场景
    /// </summary>
    public void Clear()
    {
        while (_channel.Reader.TryRead(out _))
        {
            Interlocked.Decrement(ref _count);
        }
    }

    #endregion
}

/// <summary>
/// 消息通道工厂
///
/// 提供简化的创建方法
/// </summary>
public static class MessageChannel
{
    /// <summary>
    /// 创建一个无界消息队列（容量无限）
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <returns>消息队列实例</returns>
    public static MessageChannel<T> CreateUnbounded<T>()
    {
        return new MessageChannel<T>(0);
    }

    /// <summary>
    /// 创建一个有界消息队列（最大容量）
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="capacity">最大容量</param>
    /// <returns>消息队列实例</returns>
    public static MessageChannel<T> CreateBounded<T>(int capacity)
    {
        return new MessageChannel<T>(capacity);
    }
}
