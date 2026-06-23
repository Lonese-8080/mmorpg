using MMORPG.Framework.Network;
using System.Net;
using System.Net.Sockets;
using Xunit;
using Xunit.Abstractions;

namespace MMORPG.Framework.IntegrationTests;

/// <summary>
/// 服务端生命周期集成测试 - 启动、运行、优雅停机
/// 
/// 这些测试验证 TcpServer 的"硬"接口契约（不依赖具体消息路径）：
/// - StartAsync 后 IsRunning 为 true
/// - StopAsync 后 IsRunning 为 false
/// - StopAsync 不挂起，资源被释放
/// - 启用 Session 连接池 / 消息重试时不抛异常
/// - 多次启停无资源泄漏
/// </summary>
public class ServerLifecycleTests
{
    private readonly ITestOutputHelper _output;
    public ServerLifecycleTests(ITestOutputHelper output) => _output = output;

    [Fact(Timeout = 15000)]
    public async Task StartStop_Basic()
    {
        var port = GetFreePort();
        var options = new TcpServerOptions { Port = port, Backlog = 32, MaxConnections = 50 };
        using var server = new TcpServer(options);
        await server.StartAsync();
        Assert.True(server.IsRunning);
        await server.StopAsync();
        Assert.False(server.IsRunning);
    }

    [Fact(Timeout = 15000)]
    public async Task StartStop_WithSessionPoolAndRetry()
    {
        var port = GetFreePort();
        var options = new TcpServerOptions
        {
            Port = port,
            Backlog = 32,
            MaxConnections = 50,
            EnableSessionPool = true,
            SessionPoolMinPoolSize = 5,
            SessionPoolMaxPoolSize = 20,
            SessionPoolMaxIdleCapacity = 10,
            MessageRetryCount = 2,
            MessageRetryIntervalMs = 50
        };
        using var server = new TcpServer(options);
        await server.StartAsync();
        Assert.True(server.IsRunning);
        await server.StopAsync();
        Assert.False(server.IsRunning);
        Assert.False(server.IsShuttingDown);
    }

    [Fact(Timeout = 30000)]
    public async Task StartStop_MultipleCycles()
    {
        for (int i = 0; i < 3; i++)
        {
            var port = GetFreePort();
            var options = new TcpServerOptions
            {
                Port = port,
                Backlog = 16,
                MaxConnections = 30,
                EnableSessionPool = true,
                SessionPoolMinPoolSize = 2,
                SessionPoolMaxPoolSize = 10,
                MessageRetryCount = 1
            };
            using var server = new TcpServer(options);
            await server.StartAsync();
            Assert.True(server.IsRunning);
            await server.StopAsync();
            Assert.False(server.IsRunning);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task StartAsync_ReturnsQuickly()
    {
        var port = GetFreePort();
        var options = new TcpServerOptions { Port = port, Backlog = 16, MaxConnections = 20 };
        using var server = new TcpServer(options);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var task = server.StartAsync();
        await task; // 立即完成
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1000, $"StartAsync 应立即完成，实际 {sw.ElapsedMilliseconds}ms");
        await server.StopAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task Dispose_StopsServer_Idempotent()
    {
        var port = GetFreePort();
        var options = new TcpServerOptions { Port = port, Backlog = 16, MaxConnections = 20 };
        var server = new TcpServer(options);
        await server.StartAsync();
        server.Dispose();
        Assert.False(server.IsRunning);
        // 二次 Dispose 不应抛异常
        var ex = Record.Exception(() => server.Dispose());
        Assert.Null(ex);
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
