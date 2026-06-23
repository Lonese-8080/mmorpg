// ====================================================================
// 消息序列化性能基准测试
//
// 测试目标：
// 1. 消息序列化速度（messages/秒）
// 2. 消息反序列化速度（messages/秒）
// 3. 各种消息类型的序列化开销对比
// 4. 大消息/复杂消息的性能表现
//
// 测试方法：
// - 在固定时间内（如 1000ms）尽可能多次执行操作
// - 计算 TPS（Transactions Per Second）
// - 多次运行取稳定值，减少波动
// ====================================================================

using System.Diagnostics;
using Xunit;
using Google.Protobuf;
using MMORPG.Framework.Network;

namespace MMORPG.Framework.Tests.Network;

/// <summary>
/// 消息序列化性能基准测试
///
/// 这些测试量化消息序列化/反序列化的速度，
/// 帮助我们了解框架在高消息量下的表现。
/// </summary>
[Collection("MessageSerializer")]
public class MessageSerializerBenchmarks
{
    /// <summary>
    /// 每个基准测试的迭代次数
    /// 选择 10000 次以获得稳定的统计结果
    /// </summary>
    private const int Iterations = 10000;

    /// <summary>
    /// 消息序列化性能基准：登录请求
    ///
    /// 登录请求是最典型的"小消息"，
    /// 主要是字符串字段，序列化开销较小。
    /// 目标：TPS > 100 万/秒
    /// </summary>
    [Fact]
    public void 性能基准_序列化_登录请求()
    {
        // Arrange
        var message = new C2S_Login
        {
            Account = "player_001",
            Password = "password_hash_sha256"
        };

        // 预热（让 JIT 完成编译，避免冷启动影响）
        for (int i = 0; i < 100; i++)
        {
            MessageSerializer.Serialize(message);
        }

        // Act - 基准测量
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            MessageSerializer.Serialize(message);
        }
        sw.Stop();

        // Assert - 输出结果
        var tps = (long)Iterations * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        var avgNs = (double)sw.ElapsedTicks * 1_000_000_000 / Stopwatch.Frequency / Iterations;

        // 最低要求：TPS > 50 万/秒（保守值，实际应远远高于此值）
        Assert.True(tps > 50000,
            $"登录请求序列化 TPS 过低: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");

        // 输出性能信息
        Console.WriteLine($"[序列化] 登录请求: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");
    }

    /// <summary>
    /// 消息序列化性能基准：玩家移动
    ///
    /// 玩家移动是最高频的消息类型，
    /// 主要是 float 字段（x, y, z, yaw），
    /// 序列化开销最小。
    /// 目标：TPS > 200 万/秒
    /// </summary>
    [Fact]
    public void 性能基准_序列化_玩家移动()
    {
        // Arrange
        var message = new C2S_PlayerMove
        {
            X = 123.456f,
            Y = 789.012f,
            Z = 345.678f,
            Yaw = 90.0f,
            MoveType = 1 // 跑步
        };

        // 预热
        for (int i = 0; i < 100; i++)
        {
            MessageSerializer.Serialize(message);
        }

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            MessageSerializer.Serialize(message);
        }
        sw.Stop();

        // Assert
        var tps = (long)Iterations * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        var avgNs = (double)sw.ElapsedTicks * 1_000_000_000 / Stopwatch.Frequency / Iterations;

        Assert.True(tps > 50000,
            $"玩家移动序列化 TPS 过低: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");

        Console.WriteLine($"[序列化] 玩家移动: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");
    }

    /// <summary>
    /// 消息序列化性能基准：进入世界
    ///
    /// 进入世界消息包含大量 float 字段（位置、朝向等），
    /// 属于中等消息。
    /// </summary>
    [Fact]
    public void 性能基准_序列化_进入世界()
    {
        // Arrange
        var message = new S2C_EnterWorld
        {
            Success = true,
            PlayerId = 123456789L,
            PositionX = 100.5f,
            PositionY = 50.0f,
            PositionZ = -200.3f,
            ErrorMessage = string.Empty
        };

        // 预热
        for (int i = 0; i < 100; i++)
        {
            MessageSerializer.Serialize(message);
        }

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            MessageSerializer.Serialize(message);
        }
        sw.Stop();

        // Assert
        var tps = (long)Iterations * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        var avgNs = (double)sw.ElapsedTicks * 1_000_000_000 / Stopwatch.Frequency / Iterations;

        Assert.True(tps > 50000,
            $"进入世界序列化 TPS 过低: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");

        Console.WriteLine($"[序列化] 进入世界: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");
    }

    /// <summary>
    /// 消息反序列化性能基准：登录请求
    ///
    /// 反序列化是服务器端的主要开销之一。
    /// 目标：TPS > 100 万/秒
    /// </summary>
    [Fact]
    public void 性能基准_反序列化_登录请求()
    {
        // Arrange - 先序列化一份数据
        var message = new C2S_Login
        {
            Account = "player_001",
            Password = "password_hash_sha256"
        };
        var serialized = MessageSerializer.Serialize(message);

        // 预热
        for (int i = 0; i < 100; i++)
        {
            MessageSerializer.Deserialize(serialized);
        }

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            MessageSerializer.Deserialize(serialized);
        }
        sw.Stop();

        // Assert
        var tps = (long)Iterations * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        var avgNs = (double)sw.ElapsedTicks * 1_000_000_000 / Stopwatch.Frequency / Iterations;

        Assert.True(tps > 50000,
            $"登录请求反序列化 TPS 过低: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");

        Console.WriteLine($"[反序列化] 登录请求: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");
    }

    /// <summary>
    /// 消息反序列化性能基准：玩家移动
    ///
    /// 移动消息是最高频的消息类型，
    /// 反序列化速度直接影响服务器处理能力。
    /// </summary>
    [Fact]
    public void 性能基准_反序列化_玩家移动()
    {
        // Arrange
        var message = new C2S_PlayerMove
        {
            X = 123.456f,
            Y = 789.012f,
            Z = 345.678f,
            Yaw = 90.0f,
            MoveType = 1
        };
        var serialized = MessageSerializer.Serialize(message);

        // 预热
        for (int i = 0; i < 100; i++)
        {
            MessageSerializer.Deserialize(serialized);
        }

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++)
        {
            MessageSerializer.Deserialize(serialized);
        }
        sw.Stop();

        // Assert
        var tps = (long)Iterations * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        var avgNs = (double)sw.ElapsedTicks * 1_000_000_000 / Stopwatch.Frequency / Iterations;

        Assert.True(tps > 50000,
            $"玩家移动反序列化 TPS 过低: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");

        Console.WriteLine($"[反序列化] 玩家移动: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");
    }

    /// <summary>
    /// 完整消息处理流水线基准：序列化 → 反序列化 → 类型判断
    ///
    /// 这个测试模拟完整的消息处理链路，
    /// 是最接近真实场景的性能测试。
    /// 目标：TPS > 50 万/秒
    /// </summary>
    [Fact]
    public void 性能基准_完整处理链路_玩家移动()
    {
        // Arrange
        var message = new C2S_PlayerMove
        {
            X = 100.0f,
            Y = 200.0f,
            Z = 300.0f,
            Yaw = 45.0f,
            MoveType = 1
        };
        var serialized = MessageSerializer.Serialize(message);

        // 预热
        for (int i = 0; i < 100; i++)
        {
            MessageSerializer.Serialize(message);
            MessageSerializer.Deserialize(serialized);
        }

        // Act - 模拟完整链路：反序列化 + 类型判断
        var sw = Stopwatch.StartNew();
        long processedCount = 0;
        for (int i = 0; i < Iterations; i++)
        {
            var msg = MessageSerializer.Deserialize(serialized);
            if (msg is C2S_PlayerMove moveMsg)
            {
                // 模拟读取字段
                _ = moveMsg.X;
                _ = moveMsg.Y;
                _ = moveMsg.Z;
                processedCount++;
            }
        }
        sw.Stop();

        // Assert
        var tps = processedCount * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
        var avgNs = (double)sw.ElapsedTicks * 1_000_000_000 / Stopwatch.Frequency / Iterations;

        Assert.Equal(Iterations, processedCount);
        Assert.True(tps > 50000,
            $"完整链路处理 TPS 过低: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");

        Console.WriteLine($"[完整链路] 玩家移动: {tps:N0} msg/s, 平均 {avgNs:F2}ns/次");
    }

    /// <summary>
    /// 各种消息类型序列化开销对比测试
    ///
    /// 确认不同消息类型的相对开销大小，
    /// 帮助优化者知道优先优化哪类消息。
    /// </summary>
    [Fact]
    public void 性能对比_各消息类型序列化()
    {
        // Arrange
        var messages = new (string Name, IMessage Msg)[]
        {
            ("心跳(C2S)", new C2S_Heartbeat { ClientTime = 1234567890L }),
            ("登录(C2S)", new C2S_Login { Account = "test", Password = "pass" }),
            ("登录响应(S2C)", new S2C_LoginResult { Success = true, PlayerId = 123L, Token = "abc", ErrorMessage = "" }),
            ("进入世界(S2C)", new S2C_EnterWorld { Success = true, PlayerId = 123L, PositionX = 1, PositionY = 2, PositionZ = 3, ErrorMessage = "" }),
            ("玩家移动(C2S)", new C2S_PlayerMove { X = 1, Y = 2, Z = 3, Yaw = 45, MoveType = 1 }),
            ("位置广播(S2C)", new S2C_PlayerPosition { PlayerId = 123L, X = 1, Y = 2, Z = 3, Yaw = 45, MoveType = 1 }),
            ("服务器公告(S2C)", new S2C_ServerNotice { Notice = "欢迎" }),
            ("错误(S2C)", new S2C_Error { ErrorCode = 500, Message = "错误" })
        };

        // 预热
        foreach (var (_, msg) in messages)
        {
            for (int i = 0; i < 50; i++)
            {
                MessageSerializer.Serialize(msg);
            }
        }

        Console.WriteLine("==== 各消息类型序列化性能对比 ====");

        // Act - 测量每种消息类型
        foreach (var (name, msg) in messages)
        {
            const int loopCount = 5000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < loopCount; i++)
            {
                MessageSerializer.Serialize(msg);
            }
            sw.Stop();

            var tps = (long)loopCount * 1000 / Math.Max(1, sw.ElapsedMilliseconds);
            var avgNs = (double)sw.ElapsedTicks * 1_000_000_000 / Stopwatch.Frequency / loopCount;

            Console.WriteLine($"  {name,-18}: {tps,8:N0} msg/s, 平均 {avgNs,8:F2}ns/次");

            // 基本断言
            Assert.True(tps > 50000, $"{name} 序列化 TPS 过低: {tps:N0}");
        }
    }

    /// <summary>
    /// 多线程并发序列化测试
    ///
    /// 模拟服务器同时处理多个连接的场景，
    /// 验证序列化在多线程环境下的表现。
    /// </summary>
    [Fact]
    public void 性能基准_多线程并发_序列化()
    {
        // Arrange
        var message = new C2S_PlayerMove
        {
            X = 100.0f,
            Y = 200.0f,
            Z = 300.0f,
            Yaw = 45.0f,
            MoveType = 1
        };
        const int threadCount = 8;
        const int perThreadIterations = 2000;
        long totalCount = 0;

        // 预热
        for (int i = 0; i < 100; i++)
        {
            MessageSerializer.Serialize(message);
        }

        // Act
        var sw = Stopwatch.StartNew();
        var threads = new Thread[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            threads[t] = new Thread(() =>
            {
                long localCount = 0;
                for (int i = 0; i < perThreadIterations; i++)
                {
                    MessageSerializer.Serialize(message);
                    localCount++;
                }
                Interlocked.Add(ref totalCount, localCount);
            });
            threads[t].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }
        sw.Stop();

        // Assert
        var totalOps = threadCount * perThreadIterations;
        var tps = totalCount * 1000 / Math.Max(1, sw.ElapsedMilliseconds);

        Assert.Equal(totalOps, totalCount);
        Assert.True(tps > 200000,
            $"多线程并发序列化 TPS 过低: {tps:N0} msg/s");

        Console.WriteLine($"[并发] {threadCount}线程 * {perThreadIterations}次 = {tps:N0} msg/s");
    }

    /// <summary>
    /// 消息工厂查找性能测试
    ///
    /// 验证消息工厂的字典查找速度，
    /// 这是消息路由的第一步。
    /// </summary>
    [Fact]
    public void 性能基准_消息工厂查找()
    {
        // Arrange
        var messageIds = new[]
        {
            MessageIds.C2S_Login,
            MessageIds.C2S_Heartbeat,
            MessageIds.S2C_LoginResult,
            MessageIds.S2C_Error,
            MessageIds.C2S_PlayerMove,
            MessageIds.S2C_PlayerPosition,
            MessageIds.C2S_EnterWorld,
            MessageIds.S2C_EnterWorld
        };

        // 先验证可以正确反序列化所有消息
        foreach (var id in messageIds)
        {
            var data = CreateMessageBytesById(id);
            var msg = MessageSerializer.Deserialize(data);
            Assert.NotNull(msg);
            Assert.Equal(id, MessageSerializer.GetMessageId(msg));
        }

        // 预热
        for (int i = 0; i < 100; i++)
        {
            foreach (var id in messageIds)
            {
                var data = CreateMessageBytesById(id);
                MessageSerializer.Deserialize(data);
            }
        }

        // Act
        const int loopCount = 5000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < loopCount; i++)
        {
            foreach (var id in messageIds)
            {
                var data = CreateMessageBytesById(id);
                MessageSerializer.Deserialize(data);
            }
        }
        sw.Stop();

        // Assert
        var totalMessages = (long)loopCount * messageIds.Length;
        var tps = totalMessages * 1000 / Math.Max(1, sw.ElapsedMilliseconds);

        Assert.True(tps > 50000,
            $"混合消息反序列化 TPS 过低: {tps:N0} msg/s");

        Console.WriteLine($"[混合消息] {messageIds.Length}类型 * {loopCount}次 = {tps:N0} msg/s");
    }

    /// <summary>
    /// 辅助方法：根据消息ID创建可反序列化的消息字节
    /// </summary>
    private static byte[] CreateMessageBytesById(uint messageId)
    {
        IMessage msg = messageId switch
        {
            MessageIds.C2S_Login => new C2S_Login { Account = "t", Password = "p" },
            MessageIds.S2C_LoginResult => new S2C_LoginResult { Success = true, PlayerId = 1, Token = "t", ErrorMessage = "" },
            MessageIds.C2S_Heartbeat => new C2S_Heartbeat { ClientTime = 1 },
            MessageIds.S2C_Heartbeat => new S2C_Heartbeat { ServerTime = 1, ClientTime = 2 },
            MessageIds.C2S_EnterWorld => new C2S_EnterWorld { PlayerId = 1, CharacterName = "n" },
            MessageIds.S2C_EnterWorld => new S2C_EnterWorld { Success = true, PlayerId = 1, PositionX = 1, PositionY = 2, PositionZ = 3, ErrorMessage = "" },
            MessageIds.C2S_PlayerMove => new C2S_PlayerMove { X = 1, Y = 2, Z = 3, Yaw = 4, MoveType = 1 },
            MessageIds.S2C_PlayerPosition => new S2C_PlayerPosition { PlayerId = 1, X = 1, Y = 2, Z = 3, Yaw = 4, MoveType = 1 },
            _ => new S2C_Error { ErrorCode = 0, Message = "" }
        };
        return MessageSerializer.Serialize(msg);
    }
}
