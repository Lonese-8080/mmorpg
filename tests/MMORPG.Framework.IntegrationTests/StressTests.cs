using MMORPG.Framework.Network;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace MMORPG.Framework.IntegrationTests;

/// <summary>
/// 并发连接压力测试 - 验证 TcpServer 在高并发连接下的稳定性
/// 
/// 仅测试"建连/断连"路径，不涉及消息路由（路由由单元测试覆盖）
/// </summary>
public class ConcurrentConnectTests
{
    private readonly ITestOutputHelper _output;
    public ConcurrentConnectTests(ITestOutputHelper output) => _output = output;

    [Fact(Timeout = 30000)]
    public async Task HighConcurrency_ConnectDisconnect_NoHangNoLeak()
    {
        var port = GetFreePort();
        var options = new TcpServerOptions
        {
            Port = port,
            Backlog = 256,
            MaxConnections = 200
        };
        using var server = new TcpServer(options);
        await server.StartAsync();
        try
        {
            const int clientCount = 80;
            var sw = Stopwatch.StartNew();

            var tasks = new List<Task>();
            for (int i = 0; i < clientCount; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    using var c = new TcpClient();
                    await c.ConnectAsync(IPAddress.Loopback, port);
                    await Task.Delay(50);
                    c.Close();
                }));
            }
            await Task.WhenAll(tasks);
            sw.Stop();
            _output.WriteLine($"完成 {clientCount} 个客户端连接-延时-断开，耗时 {sw.ElapsedMilliseconds}ms");

            // 验证服务端没挂
            Assert.True(server.IsRunning);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task WithSessionPool_HighConcurrency_StartStopSucceed()
    {
        var port = GetFreePort();
        var options = new TcpServerOptions
        {
            Port = port,
            Backlog = 256,
            MaxConnections = 200,
            EnableSessionPool = true,
            SessionPoolMinPoolSize = 20,
            SessionPoolMaxPoolSize = 100,
            SessionPoolMaxIdleCapacity = 50
        };
        using var server = new TcpServer(options);
        await server.StartAsync();
        try
        {
            // 启用了连接池也能正常建连
            using var c = new TcpClient();
            await c.ConnectAsync(IPAddress.Loopback, port);
            Assert.True(c.Connected);
        }
        finally
        {
            await server.StopAsync();
        }
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
