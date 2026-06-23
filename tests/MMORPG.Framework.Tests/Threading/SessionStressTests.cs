// ====================================================================
// Session/连接管理压力测试
//
// 测试目标：
// 1. 线程安全集合的并发操作性能
// 2. 大量 Session 同时连接/断开的稳定性
// 3. 原子计数器的正确性
// ====================================================================

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using MMORPG.Framework.Threading;

namespace MMORPG.Framework.Tests.Threading;

/// <summary>
/// Session/连接管理压力测试
///
/// 测试多线程环境下 Session 管理的正确性和性能。
/// </summary>
public class SessionStressTests
{
    /// <summary>
    /// 测试 1：线程安全集合的并发添加
    ///
    /// 模拟 1000 个玩家同时连接的场景。
    /// </summary>
    [Fact]
    public void 压力测试_并发添加Session()
    {
        // Arrange
        var collections = new ThreadSafeCollections();
        const int sessionCount = 1000;
        const int threadCount = 10;
        const int perThreadSessions = sessionCount / threadCount;

        // Act
        var sw = Stopwatch.StartNew();
        var threads = new Thread[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int startId = t * perThreadSessions;
            threads[t] = new Thread(() =>
            {
                for (int i = 0; i < perThreadSessions; i++)
                {
                    long connectionId = startId + i;
                    collections.AddOrUpdateSession(connectionId, new object());
                }
            });
            threads[t].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }
        sw.Stop();

        // Assert
        Assert.Equal(sessionCount, collections.SessionCount);

        var opsPerSec = (long)sessionCount * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        Console.WriteLine($"[Session] 并发添加 {sessionCount:N0} 个会话: {opsPerSec:N0} ops/s, 总耗时 {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 测试 2：线程安全集合的并发添加与移除
    ///
    /// 模拟玩家不断连接和断开的场景。
    /// </summary>
    [Fact]
    public void 压力测试_并发添加与移除()
    {
        // Arrange
        var collections = new ThreadSafeCollections();
        const int operations = 2000;
        const int threadCount = 8;
        const int perThreadOps = operations / threadCount;

        // 先添加一些 Session
        for (int i = 0; i < operations; i++)
        {
            collections.AddOrUpdateSession(i, new object());
        }
        Assert.Equal(operations, collections.SessionCount);

        // Act - 并发移除和添加
        var sw = Stopwatch.StartNew();
        var threads = new Thread[threadCount];
        int removedCount = 0;
        int addedCount = 0;

        for (int t = 0; t < threadCount; t++)
        {
            int threadId = t;
            threads[t] = new Thread(() =>
            {
                // 一半线程移除，一半线程添加
                if (threadId % 2 == 0)
                {
                    for (int i = threadId / 2 * perThreadOps;
                        i < threadId / 2 * perThreadOps + perThreadOps; i++)
                    {
                        if (collections.TryRemoveSession(i))
                        {
                            Interlocked.Increment(ref removedCount);
                        }
                    }
                }
                else
                {
                    for (int i = threadId / 2 * perThreadOps + operations;
                        i < threadId / 2 * perThreadOps + operations + perThreadOps; i++)
                    {
                        collections.AddOrUpdateSession(i, new object());
                        Interlocked.Increment(ref addedCount);
                    }
                }
            });
            threads[t].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }
        sw.Stop();

        // Assert
        int expectedCount = operations - removedCount + addedCount;
        Assert.Equal(expectedCount, collections.SessionCount);

        var totalOps = removedCount + addedCount;
        var opsPerSec = (long)totalOps * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        Console.WriteLine($"[Session] 并发添加/移除 {totalOps:N0} 次: {opsPerSec:N0} ops/s");
    }

    /// <summary>
    /// 测试 3：原子计数器的正确性
    ///
    /// 在多线程环境下验证计数器不会丢失。
    /// </summary>
    [Fact]
    public void 压力测试_原子计数器并发()
    {
        // Arrange
        var counter = new AtomicCounter();
        const int threadCount = 16;
        const int perThreadIncrements = 10000;

        // Act
        var sw = Stopwatch.StartNew();
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < perThreadIncrements; i++)
                {
                    counter.Increment();
                }
            });
        }
        Task.WaitAll(tasks);
        sw.Stop();

        // Assert - 验证原子性
        long expectedCount = (long)threadCount * perThreadIncrements;
        Assert.Equal(expectedCount, counter.Value);

        var opsPerSec = expectedCount * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        Console.WriteLine($"[Counter] 原子计数器 {threadCount}线程 * {perThreadIncrements}次: {opsPerSec:N0} ops/s");
    }

    /// <summary>
    /// 测试 4：原子计数器的增减混合操作
    ///
    /// 模拟在线玩家数增减的场景。
    /// </summary>
    [Fact]
    public void 压力测试_原子计数器混合()
    {
        // Arrange
        var counter = new AtomicCounter();
        const int threadCount = 8;
        const int perThreadOps = 5000;

        // Act
        var sw = Stopwatch.StartNew();
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadId = t;
            tasks[t] = Task.Run(() =>
            {
                // 一半线程增加，一半线程减少
                if (threadId % 2 == 0)
                {
                    for (int i = 0; i < perThreadOps; i++)
                    {
                        counter.Increment();
                    }
                }
                else
                {
                    for (int i = 0; i < perThreadOps; i++)
                    {
                        counter.Decrement();
                    }
                }
            });
        }
        Task.WaitAll(tasks);
        sw.Stop();

        // Assert - 平衡操作应该结果为0
        long expected = 0;
        Assert.Equal(expected, counter.Value);

        var totalOps = (long)threadCount * perThreadOps;
        var opsPerSec = totalOps * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        Console.WriteLine($"[Counter] 混合增减 {totalOps:N0} 次: {opsPerSec:N0} ops/s, 最终值 {counter.Value}");
    }

    /// <summary>
    /// 测试 5：大量并发 Session 的管理
    ///
    /// 模拟数千玩家同时存在的场景，验证内存和性能。
    /// </summary>
    [Fact]
    public void 压力测试_大规模Session管理()
    {
        // Arrange
        var collections = new ThreadSafeCollections();
        const int totalSessions = 5000;

        // Act
        var sw = Stopwatch.StartNew();

        // 批量添加
        for (int i = 0; i < totalSessions; i++)
        {
            collections.AddOrUpdateSession(i, new object());
        }

        // 验证数量
        Assert.Equal(totalSessions, collections.SessionCount);

        // 批量查询
        int foundCount = 0;
        for (int i = 0; i < totalSessions; i++)
        {
            if (collections.TryGetSession(i, out _))
            {
                foundCount++;
            }
        }

        // 批量移除
        int removedCount = 0;
        for (int i = 0; i < totalSessions; i++)
        {
            if (collections.TryRemoveSession(i))
            {
                removedCount++;
            }
        }
        sw.Stop();

        // Assert
        Assert.Equal(totalSessions, foundCount);
        Assert.Equal(totalSessions, removedCount);
        Assert.Equal(0, collections.SessionCount);

        var totalOps = totalSessions * 3; // add + query + remove
        var opsPerSec = (long)totalOps * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        Console.WriteLine($"[Session] 大规模管理 {totalOps:N0} 次操作: {opsPerSec:N0} ops/s, 总耗时 {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 测试 6：GetAllSessions 性能
    ///
    /// 验证获取所有 Session 快照的性能，
    /// 此操作可能在广播消息时触发。
    /// </summary>
    [Fact]
    public void 性能测试_获取全部Session()
    {
        // Arrange
        var collections = new ThreadSafeCollections();
        const int sessionCount = 2000;
        for (int i = 0; i < sessionCount; i++)
        {
            collections.AddOrUpdateSession(i, new object());
        }

        // Act
        const int iterations = 100;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var sessions = collections.GetAllSessions();
            Assert.Equal(sessionCount, sessions.Length);
        }
        sw.Stop();

        // Assert
        var opsPerSec = (long)iterations * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        Console.WriteLine($"[Session] 获取全部 {sessionCount} 个会话: {opsPerSec:N0} ops/s");
    }
}
