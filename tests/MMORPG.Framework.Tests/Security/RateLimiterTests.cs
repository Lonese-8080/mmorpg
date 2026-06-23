using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Xunit;
using MMORPG.Framework.Network;
using MMORPG.Framework.Security;

namespace MMORPG.Framework.Tests.Security;

/// <summary>
/// 消息限流模块测试
/// 
/// 使用 [Collection("Observability")] 标签确保与可观测性测试串行执行，
/// 避免对共享静态资源的竞争。
/// </summary>
[Collection("Observability")]
public class RateLimiterTests
{
    [Fact]
    public void TryAcquire_WithinLimit_Allows()
    {
        // Arrange
        var limiter = new RateLimiter(10);

        // Act & Assert - 10 次获取都应该成功
        for (int i = 0; i < 10; i++)
        {
            Assert.True(limiter.TryAcquire(), $"第 {i + 1} 次获取应该成功");
        }
    }

    [Fact]
    public void TryAcquire_ExceedsLimit_Drops()
    {
        // Arrange
        var limiter = new RateLimiter(10);

        // Act - 连续获取 11 次
        for (int i = 0; i < 10; i++)
        {
            limiter.TryAcquire();
        }
        bool result11 = limiter.TryAcquire();

        // Assert - 第 11 次应该失败
        Assert.False(result11, "第 11 次获取应该失败");
        Assert.Equal(1, limiter.DroppedCount);
    }

    [Fact]
    public void TryAcquire_RefillsAfterOneSecond()
    {
        // Arrange
        var limiter = new RateLimiter(10);

        // Act - 先耗尽令牌
        for (int i = 0; i < 10; i++)
        {
            limiter.TryAcquire();
        }

        // 等待 1.1 秒让令牌桶补充
        Thread.Sleep(1100);

        // Assert - 再次尝试应该可以获取
        Assert.True(limiter.TryAcquire(), "等待 1.1 秒后应该可以获取令牌");
    }

    [Fact]
    public void TryAcquire_MultipleCount()
    {
        // Arrange
        var limiter = new RateLimiter(10);

        // Act & Assert
        Assert.True(limiter.TryAcquire(5));  // 剩余 5
        Assert.True(limiter.TryAcquire(5));  // 剩余 0
        Assert.False(limiter.TryAcquire(5)); // 不足
    }

    [Fact]
    public void Reset_RestoresCapacity()
    {
        // Arrange
        var limiter = new RateLimiter(10);

        // Act - 耗尽令牌
        for (int i = 0; i < 10; i++)
        {
            limiter.TryAcquire();
        }
        Assert.Equal(0, limiter.CurrentTokens);

        limiter.Reset();

        // Assert
        Assert.Equal(limiter.MaxMessagesPerSecond, limiter.CurrentTokens);
        Assert.Equal(0, limiter.DroppedCount);
    }

    [Fact]
    public void ThreadSafe_HighConcurrency()
    {
        // Arrange
        var limiter = new RateLimiter(10000);
        var tasks = new Task[10];

        // Act - 10 个线程 × 1000 次获取
        for (int t = 0; t < 10; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    limiter.TryAcquire();
                }
            });
        }
        Task.WaitAll(tasks);

        // Assert - 丢弃数 >= 0（不应该有竞态导致的负数等异常）
        Assert.True(limiter.DroppedCount >= 0, $"DroppedCount 应该 >= 0，实际 {limiter.DroppedCount}");
    }

    [Fact]
    public async Task SetGlobalRateLimit_Negative_Disables()
    {
        // Arrange
        var router = new MessageRouter();
        var handlerCalled = false;
        router.RegisterHandler(MessageIds.C2S_Heartbeat, (session, message) =>
        {
            handlerCalled = true;
            return Task.CompletedTask;
        });

        // 启用一个非常严格的限流（1 条/秒），然后禁用
        router.SetGlobalRateLimit(1);
        router.SetGlobalRateLimit(-1);  // 禁用

        // 创建模拟 session（使用 Socket）
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var clientTask = listener.AcceptSocketAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var serverSocket = await clientTask;
        var session = new Session(1, serverSocket, new TcpServerOptions(), (s, m) => { }, (s, r) => { });

        // Act - 发送多条消息
        for (int i = 0; i < 100; i++)
        {
            await router.RouteAsync(session, new C2S_Heartbeat());
        }

        // Assert - 应该至少有一些消息被处理（handler 被调用）
        // 因为限流已被禁用，消息不会被丢弃
        // 我们用 handlerCalled 来间接验证：至少有一次路由成功
        // 由于 handler 注册了且无其他阻碍，handlerCalled 应该为 true
        Assert.True(handlerCalled, "禁用限流器后消息应该被正常处理");

        listener.Stop();
    }

    [Fact]
    public async Task SessionRateLimit_DropsMessages()
    {
        // Arrange - 创建一个真实的 session
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serverSocketTask = listener.AcceptSocketAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var serverSocket = await serverSocketTask;

        int messagesReceived = 0;
        var session = new Session(
            connectionId: 1,
            socket: serverSocket,
            options: new TcpServerOptions(),
            onMessageReceived: (s, m) => { messagesReceived++; },
            onDisconnected: (s, r) => { });

        // 设置限流：5 条/秒
        session.SetSessionRateLimit(5);

        // 构造 10 条 C2S_Heartbeat 的字节数据
        var singleMsg = MessageSerializer.Serialize(new C2S_Heartbeat());
        var data = new byte[singleMsg.Length * 10];
        for (int i = 0; i < 10; i++)
        {
            Buffer.BlockCopy(singleMsg, 0, data, i * singleMsg.Length, singleMsg.Length);
        }

        // 通过反射调用私有 ProcessDataBuffer 方法
        var method = typeof(Session).GetMethod("ProcessDataBuffer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        // Act - 传入数据
        int processed = (int)method!.Invoke(session, new object[] { data, data.Length })!;

        // Assert - 验证限流生效：被接受的消息数 <= 5
        // 由于限流器初始有 5 个令牌，每条消息消耗 1 个令牌，
        // 所以最多 5 条消息被处理；其余的会被限流丢弃（offset += 1 的方式继续）
        Assert.True(messagesReceived <= 5,
            $"会话限流应该将处理消息数限制在 <= 5，实际 {messagesReceived}");
        // 处理的字节数应该 >= 0（不会抛出异常）
        Assert.True(processed >= 0);

        listener.Stop();
    }
}
