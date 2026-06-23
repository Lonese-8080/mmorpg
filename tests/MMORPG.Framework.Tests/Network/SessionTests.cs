using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Google.Protobuf;
using Xunit;
using MMORPG.Framework.Network;
using MMORPG.Framework.Security;

namespace MMORPG.Framework.Tests.Network;

[Collection("Observability")]
public class SessionTests
{
    private static (Session session, Socket clientSocket, TcpListener listener) CreateSession()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serverSocketTask = listener.AcceptSocketAsync();
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var serverSocket = serverSocketTask.Result;

        var session = new Session(
            connectionId: 1,
            socket: serverSocket,
            options: new TcpServerOptions(),
            onMessageReceived: (s, m) => { },
            onDisconnected: (s, r) => { });

        return (session, client.Client, listener);
    }

    [Fact]
    public void ConnectionId_ReturnsCorrectValue()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            Assert.Equal(1, session.ConnectionId);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void IsConnected_WhenSocketConnected_ReturnsTrue()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            Assert.True(session.IsConnected);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void RemoteEndPoint_IsNotNull()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            Assert.NotNull(session.RemoteEndPoint);
            Assert.IsType<IPEndPoint>(session.RemoteEndPoint);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void PlayerId_Default_IsZero()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            Assert.Equal(0, session.PlayerId);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void PlayerId_SetAndGet_Works()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            session.PlayerId = 12345;
            Assert.Equal(12345, session.PlayerId);

            session.PlayerId = 999;
            Assert.Equal(999, session.PlayerId);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void PlayerId_ThreadSafe_ConcurrentWrites()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            var tasks = new Task[10];
            for (int t = 0; t < 10; t++)
            {
                int tid = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        session.PlayerId = tid * 1000 + i;
                        var _ = session.PlayerId;
                    }
                });
            }

            Task.WaitAll(tasks);

            Assert.True(session.PlayerId >= 0);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void LastHeartbeat_InitialValue_IsRecent()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            var heartbeat = session.LastHeartbeat;
            var diff = (DateTime.UtcNow - heartbeat).TotalSeconds;
            Assert.True(diff < 5, $"初始心跳时间应该在5秒内，实际: {diff}秒");
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void LastHeartbeat_SetAndGet_Works()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            var testTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            session.LastHeartbeat = testTime;

            Assert.Equal(testTime, session.LastHeartbeat);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void LastHeartbeat_ThreadSafe_ConcurrentWrites()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            var tasks = new Task[10];
            for (int t = 0; t < 10; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        session.LastHeartbeat = DateTime.UtcNow.AddSeconds(i);
                        var _ = session.LastHeartbeat;
                    }
                });
            }

            Task.WaitAll(tasks);

            Assert.True(session.LastHeartbeat > DateTime.MinValue);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void SetSessionRateLimit_SetsLimiter()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            session.SetSessionRateLimit(100);

            var field = typeof(Session).GetField("_sessionRateLimiter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var limiter = field!.GetValue(session);
            Assert.NotNull(limiter);
            Assert.IsType<RateLimiter>(limiter);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void SetSessionRateLimit_Negative_Disables()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            session.SetSessionRateLimit(100);
            session.SetSessionRateLimit(-1);

            var field = typeof(Session).GetField("_sessionRateLimiter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var limiter = field!.GetValue(session);
            Assert.Null(limiter);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void RetryQueue_Default_IsNull()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            Assert.Null(session.RetryQueue);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void RetryQueue_SetAndGet_Works()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            var retryQueue = new MessageRetryQueue(new MessageRetryOptions { MaxRetryCount = 10 });
            session.RetryQueue = retryQueue;

            Assert.Same(retryQueue, session.RetryQueue);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void Dispose_SetsDisconnected()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            session.Dispose();

            Assert.False(session.IsConnected);
        }
        finally
        {
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var (session, clientSocket, listener) = CreateSession();
        try
        {
            session.Dispose();
            session.Dispose();
            session.Dispose();
        }
        finally
        {
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public async Task Disconnect_TriggersCallback()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serverSocketTask = listener.AcceptSocketAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var serverSocket = await serverSocketTask;

        string? disconnectReason = null;
        var disconnectCount = 0;
        var session = new Session(
            connectionId: 1,
            socket: serverSocket,
            options: new TcpServerOptions(),
            onMessageReceived: (s, m) => { },
            onDisconnected: (s, r) =>
            {
                disconnectCount++;
                disconnectReason = r;
            });

        try
        {
            session.Disconnect("test reason");

            Assert.Equal(1, disconnectCount);
            Assert.Equal("test reason", disconnectReason);
        }
        finally
        {
            session.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public async Task Disconnect_MultipleCalls_OnlyTriggersOnce()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serverSocketTask = listener.AcceptSocketAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var serverSocket = await serverSocketTask;

        var disconnectCount = 0;
        var session = new Session(
            connectionId: 1,
            socket: serverSocket,
            options: new TcpServerOptions(),
            onMessageReceived: (s, m) => { },
            onDisconnected: (s, r) => disconnectCount++);

        try
        {
            session.Disconnect("reason1");
            session.Disconnect("reason2");
            session.Disconnect("reason3");

            Assert.Equal(1, disconnectCount);
        }
        finally
        {
            session.Dispose();
            listener.Stop();
        }
    }
}
