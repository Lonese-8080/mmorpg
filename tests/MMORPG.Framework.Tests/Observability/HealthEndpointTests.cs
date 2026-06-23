using System.Net;
using MMORPG.Framework.Observability;
using Xunit;

namespace MMORPG.Framework.Tests.Observability;

/// <summary>
/// HealthEndpoint 单元测试
/// </summary>
public class HealthEndpointTests
{
    [Fact]
    public void Ctor_InvalidPort_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HealthEndpoint(port: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HealthEndpoint(port: 70000));
    }

    [Fact]
    public void Ctor_InvalidPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => new HealthEndpoint(port: 8080, healthPath: ""));
        Assert.Throws<ArgumentException>(() => new HealthEndpoint(port: 8080, readyPath: ""));
        Assert.Throws<ArgumentException>(() => new HealthEndpoint(port: 8080, alivePath: ""));
    }

    [Fact]
    public void New_Instance_IsNotRunning()
    {
        using var ep = new HealthEndpoint(port: GetFreePort());
        Assert.False(ep.IsRunning);
    }

    [Fact]
    public void Stop_BeforeStart_DoesNotThrow()
    {
        using var ep = new HealthEndpoint(port: GetFreePort());
        var ex = Record.Exception(() => ep.Stop());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var ep = new HealthEndpoint(port: GetFreePort());
        ep.Dispose();
        var ex = Record.Exception(() => ep.Dispose());
        Assert.Null(ex);
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
