using MMORPG.Framework.Network;
using Xunit;

namespace MMORPG.Framework.Tests.Network;

/// <summary>
/// TcpServer 优雅停机与 IDisposable 测试
/// </summary>
public class TcpServerLifecycleTests
{
    [Fact(Timeout = 10000)]
    public async Task StartAsync_ThenStopAsync_TogglesIsRunning()
    {
        var port = GetFreePort();
        using var server = new TcpServer(new TcpServerOptions { Port = port, Backlog = 8, MaxConnections = 10 });
        Assert.False(server.IsRunning);
        await server.StartAsync();
        Assert.True(server.IsRunning);
        Assert.False(server.IsShuttingDown);
        await server.StopAsync();
        Assert.False(server.IsRunning);
        Assert.False(server.IsShuttingDown);
    }

    [Fact(Timeout = 10000)]
    public async Task StartAsync_WithoutAwait_DoesNotBlock()
    {
        var port = GetFreePort();
        using var server = new TcpServer(new TcpServerOptions { Port = port, Backlog = 8, MaxConnections = 10 });
        var task = server.StartAsync();
        Assert.NotNull(task);
        await task;
        Assert.True(server.IsRunning);
        await server.StopAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task Dispose_Stops_Server()
    {
        var port = GetFreePort();
        var server = new TcpServer(new TcpServerOptions { Port = port, Backlog = 8, MaxConnections = 10 });
        await server.StartAsync();
        Assert.True(server.IsRunning);
        server.Dispose();
        Assert.False(server.IsRunning);
    }

    [Fact(Timeout = 10000)]
    public async Task Dispose_Without_Start_DoesNotThrow()
    {
        var server = new TcpServer(new TcpServerOptions { Port = GetFreePort(), Backlog = 8, MaxConnections = 10 });
        var ex = Record.Exception(() => server.Dispose());
        Assert.Null(ex);
    }

    [Fact(Timeout = 10000)]
    public async Task StopAsync_BeforeStart_IsNoOp()
    {
        using var server = new TcpServer(new TcpServerOptions { Port = GetFreePort(), Backlog = 8, MaxConnections = 10 });
        await server.StopAsync(); // 应立即返回
        Assert.False(server.IsRunning);
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
