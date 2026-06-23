using MMORPG.Framework.Network;
using System.Net;
using System.Net.Sockets;
using Xunit;
using Xunit.Abstractions;

namespace MMORPG.Framework.IntegrationTests;

/// <summary>
/// TCP 监听集成测试 - 验证 TcpServer 真正接受客户端连接
/// 
/// 不验证具体消息路由（消息路由在单元测试中已通过 Session 内部逻辑覆盖），
/// 仅验证"监听 → accept → 客户端可连接 → 后续可断开"的端到端能力。
/// </summary>
public class TcpListenerIntegrationTests
{
    private readonly ITestOutputHelper _output;
    public TcpListenerIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact(Timeout = 15000)]
    public async Task Client_CanConnect_ToRunningServer()
    {
        var port = GetFreePort();
        var options = new TcpServerOptions { Port = port, Backlog = 32, MaxConnections = 10 };
        using var server = new TcpServer(options);
        await server.StartAsync();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            Assert.True(client.Connected);
            _output.WriteLine($"已连接到 {IPAddress.Loopback}:{port}");
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task MultipleClients_CanConnect_Concurrently()
    {
        var port = GetFreePort();
        var options = new TcpServerOptions { Port = port, Backlog = 64, MaxConnections = 50 };
        using var server = new TcpServer(options);
        await server.StartAsync();
        try
        {
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    using var c = new TcpClient();
                    await c.ConnectAsync(IPAddress.Loopback, port);
                    Assert.True(c.Connected);
                }));
            }
            await Task.WhenAll(tasks);
            _output.WriteLine("10 个客户端并发连接成功");
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Server_AfterStop_RejectsNewConnections()
    {
        var port = GetFreePort();
        var options = new TcpServerOptions { Port = port, Backlog = 16, MaxConnections = 10 };
        using var server = new TcpServer(options);
        await server.StartAsync();

        // 第一次连接应成功
        using (var ok = new TcpClient())
        {
            await ok.ConnectAsync(IPAddress.Loopback, port);
            Assert.True(ok.Connected);
        }

        await server.StopAsync();

        // 停机后连接应被拒绝
        using var fail = new TcpClient();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await fail.ConnectAsync(IPAddress.Loopback, port);
        });
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
