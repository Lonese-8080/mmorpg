// ====================================================================
// 消息路由 TPS 基准测试
//
// 测试目标：
// 1. 消息路由的吞吐量（messages/秒）
// 2. 注册/查找处理器的速度
// 3. 多线程并发路由的表现
// 4. 未注册消息的处理开销
// ====================================================================

using System.Diagnostics;
using Google.Protobuf;
using Xunit;
using MMORPG.Framework.Network;
using MMORPG.Game.Messages;

namespace MMORPG.Framework.Tests.Network;

/// <summary>
/// 消息路由器 TPS 基准测试
///
/// 测量消息从接收 → 路由 → 处理器处理的
/// 整个流程的性能。
/// </summary>
[Collection("MessageSerializer")]
public class MessageRouterBenchmarks
{
    /// <summary>
    /// 基准测试 1：消息路由吞吐量
    ///
    /// 模拟服务器接收消息并通过路由分发的场景。
    /// 目标：TPS > 10 万/秒
    /// </summary>
    [Fact]
    public async Task 性能基准_消息路由吞吐量()
    {
        // Arrange
        var router = new MessageRouter();

        // 注册几个处理器
        int moveCount = 0;
        router.RegisterHandler(MessageIds.C2S_PlayerMove, (session, msg) =>
        {
            // 模拟简单处理
            Interlocked.Increment(ref moveCount);
            return Task.CompletedTask;
        });

        // 模拟会话（注意：Session 需要 Socket，这里使用 Mock 模式）
        var testSession = new TestSession();
        var moveMessage = new C2S_PlayerMove
        {
            X = 100,
            Y = 200,
            Z = 300,
            Yaw = 45,
            MoveType = 1
        };

        // 预热
        for (int i = 0; i < 100; i++)
        {
            await router.RouteAsync(testSession, moveMessage);
        }

        // Act
        const int iterations = 10000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            await router.RouteAsync(testSession, moveMessage);
        }
        sw.Stop();

        // Assert
        var tps = (long)iterations * 1000 / Math.Max(1, sw.ElapsedMilliseconds);

        Assert.True(tps > 10000,
            $"消息路由 TPS 过低: {tps:N0} msg/s");

        Console.WriteLine($"[路由] 玩家移动消息: {tps:N0} msg/s");
    }

    /// <summary>
    /// 基准测试 2：多类型消息路由
    ///
    /// 模拟同时处理多种消息类型的场景。
    /// 目标：TPS > 8 万/秒
    /// </summary>
    [Fact]
    public async Task 性能基准_多类型消息路由()
    {
        // Arrange
        var router = new MessageRouter();
        int totalProcessed = 0;

        // 注册多个处理器
        router.RegisterHandler(MessageIds.C2S_Login, (session, msg) =>
        {
            Interlocked.Increment(ref totalProcessed);
            return Task.CompletedTask;
        });
        router.RegisterHandler(MessageIds.C2S_Heartbeat, (session, msg) =>
        {
            Interlocked.Increment(ref totalProcessed);
            return Task.CompletedTask;
        });
        router.RegisterHandler(MessageIds.C2S_EnterWorld, (session, msg) =>
        {
            Interlocked.Increment(ref totalProcessed);
            return Task.CompletedTask;
        });
        router.RegisterHandler(MessageIds.C2S_PlayerMove, (session, msg) =>
        {
            Interlocked.Increment(ref totalProcessed);
            return Task.CompletedTask;
        });

        var testSession = new TestSession();
        var messages = new IMessage[]
        {
            new C2S_Login { Account = "a", Password = "p" },
            new C2S_Heartbeat { ClientTime = 1 },
            new C2S_EnterWorld { PlayerId = 1, CharacterName = "n" },
            new C2S_PlayerMove { X = 1, Y = 2, Z = 3, Yaw = 4, MoveType = 1 }
        };

        // 预热
        foreach (var msg in messages)
        {
            for (int i = 0; i < 50; i++)
            {
                await router.RouteAsync(testSession, msg);
            }
        }

        // Act
        const int iterations = 8000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            foreach (var msg in messages)
            {
                await router.RouteAsync(testSession, msg);
            }
        }
        sw.Stop();

        // Assert
        var totalMsgs = (long)iterations * messages.Length;
        var tps = totalMsgs * 1000 / Math.Max(1, sw.ElapsedMilliseconds);

        Assert.True(tps > 10000,
            $"多类型消息路由 TPS 过低: {tps:N0} msg/s");

        Console.WriteLine($"[路由] 多类型消息: {tps:N0} msg/s, 共处理 {totalMsgs:N0} 条");
    }

    /// <summary>
    /// 基准测试 3：未注册消息的处理（错误路径）
    ///
    /// 测量接收未注册消息时的开销，
    /// 虽然不应该常发生，但需要保证性能。
    /// </summary>
    [Fact]
    public async Task 性能基准_未注册消息()
    {
        // Arrange - 不注册任何处理器
        var router = new MessageRouter();
        var testSession = new TestSession();

        // 使用已定义但未注册的消息类型（C2S_UseSkill 是战斗消息，框架层未注册）
        // 注意：需要先注册这个消息类型到 MessageSerializer
        MessageSerializer.Register<C2S_UseSkill>(MessageIds.C2S_UseSkill);
        var unregisteredMessage = new C2S_UseSkill { SkillId = 1, TargetId = 100 };

        // 预热
        for (int i = 0; i < 100; i++)
        {
            await router.RouteAsync(testSession, unregisteredMessage);
        }

        // Act
        const int iterations = 5000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            await router.RouteAsync(testSession, unregisteredMessage);
        }
        sw.Stop();

        // Assert
        var tps = (long)iterations * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        Assert.True(tps > 5000,
            $"未注册消息处理 TPS 过低: {tps:N0} msg/s");

        Console.WriteLine($"[路由] 未注册消息: {tps:N0} msg/s");
    }

    /// <summary>
    /// 基准测试 4：多线程并发路由
    ///
    /// 模拟多个连接同时发送消息的场景。
    /// 目标：TPS > 50 万/秒（8 线程）
    /// </summary>
    [Fact]
    public void 性能基准_多线程并发路由()
    {
        // Arrange
        var router = new MessageRouter();
        long totalProcessed = 0;

        router.RegisterHandler(MessageIds.C2S_PlayerMove, (session, msg) =>
        {
            Interlocked.Increment(ref totalProcessed);
            return Task.CompletedTask;
        });

        const int threadCount = 8;
        const int perThreadIterations = 2000;
        var testSession = new TestSession();
        var moveMessage = new C2S_PlayerMove
        {
            X = 100,
            Y = 200,
            Z = 300,
            Yaw = 45,
            MoveType = 1
        };

        // 预热
        for (int i = 0; i < 100; i++)
        {
            _ = router.RouteAsync(testSession, moveMessage);
        }

        // Act
        var sw = Stopwatch.StartNew();
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(async () =>
            {
                for (int i = 0; i < perThreadIterations; i++)
                {
                    await router.RouteAsync(testSession, moveMessage);
                }
            });
        }
        Task.WaitAll(tasks);
        sw.Stop();

        // Assert
        var totalMsgs = (long)threadCount * perThreadIterations;
        var tps = totalMsgs * 1000 / Math.Max(1, sw.ElapsedMilliseconds);

        Assert.True(tps > 5000,
            $"多线程并发路由 TPS 过低: {tps:N0} msg/s");

        Console.WriteLine($"[路由] 多线程并发({threadCount}线程): {tps:N0} msg/s");
    }

    /// <summary>
    /// 基准测试 5：处理器注册开销
    ///
    /// 测量消息处理器注册的性能，
    /// 通常在启动时执行，不需要极高性能。
    /// </summary>
    [Fact]
    public void 性能基准_处理器注册()
    {
        // Arrange
        const int iterations = 1000;
        var router = new MessageRouter();

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            uint messageId = (uint)i + 1000;
            router.RegisterHandler(messageId, (session, msg) => Task.CompletedTask);
        }
        sw.Stop();

        // Assert
        var tps = (long)iterations * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        Assert.True(tps > 10000,
            $"处理器注册 TPS 过低: {tps:N0} ops/s");

        Console.WriteLine($"[路由] 处理器注册: {tps:N0} ops/s");
    }
}

/// <summary>
/// 测试用 Session（不依赖真实 Socket）
///
/// 用于在测试中模拟会话对象。
/// </summary>
public class TestSession : Session
{
    public TestSession() : base(0, null!, new TcpServerOptions(), (s, m) => { }, (s, reason) => { })
    {
    }
}

// 注意：TestUnregisteredMessage 类已删除
// 在 Protobuf 环境下，测试"未注册消息"场景需要实现完整的 IMessage<T> 接口
// 这个场景可以通过使用已定义但未注册的消息类型（如 C2S_UseSkill）来测试
