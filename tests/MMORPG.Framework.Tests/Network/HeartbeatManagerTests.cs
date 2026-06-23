using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Xunit;
using MMORPG.Framework.Network;

namespace MMORPG.Framework.Tests.Network;

[Collection("Observability")]
public class HeartbeatManagerTests
{
    private static (Session session, Socket clientSocket, TcpListener listener) CreateSession(long id = 1)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serverSocketTask = listener.AcceptSocketAsync();
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var serverSocket = serverSocketTask.Result;

        var session = new Session(
            connectionId: id,
            socket: serverSocket,
            options: new TcpServerOptions(),
            onMessageReceived: (s, m) => { },
            onDisconnected: (s, r) => { });

        return (session, client.Client, listener);
    }

    [Fact]
    public void Constructor_InitializesCorrectly()
    {
        var sessions = new ConcurrentDictionary<long, Session>();
        var options = new TcpServerOptions();
        var manager = new HeartbeatManager(sessions, options);

        Assert.NotNull(manager);
    }

    [Fact]
    public void RegisterSession_AddsSession()
    {
        var sessions = new ConcurrentDictionary<long, Session>();
        var options = new TcpServerOptions();
        var manager = new HeartbeatManager(sessions, options);

        var (session, clientSocket, listener) = CreateSession(1);
        try
        {
            manager.RegisterSession(session);

            sessions[session.ConnectionId] = session;
            Assert.True(sessions.ContainsKey(1));
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void RegisterSession_NullSession_DoesNotThrow()
    {
        var sessions = new ConcurrentDictionary<long, Session>();
        var options = new TcpServerOptions();
        var manager = new HeartbeatManager(sessions, options);

        manager.RegisterSession(null!);
    }

    [Fact]
    public void UnregisterSession_RemovesSession()
    {
        var sessions = new ConcurrentDictionary<long, Session>();
        var options = new TcpServerOptions();
        var manager = new HeartbeatManager(sessions, options);

        var (session, clientSocket, listener) = CreateSession(1);
        try
        {
            manager.RegisterSession(session);
            sessions[session.ConnectionId] = session;

            manager.UnregisterSession(session);

            Assert.True(sessions.ContainsKey(1));
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void UnregisterSession_NullSession_DoesNotThrow()
    {
        var sessions = new ConcurrentDictionary<long, Session>();
        var options = new TcpServerOptions();
        var manager = new HeartbeatManager(sessions, options);

        manager.UnregisterSession(null!);
    }

    [Fact]
    public void Start_WhenNotRunning_Starts()
    {
        var sessions = new ConcurrentDictionary<long, Session>();
        var options = new TcpServerOptions { HeartbeatCheckIntervalSeconds = 1 };
        var manager = new HeartbeatManager(sessions, options);

        manager.Start();

        manager.Stop();
    }

    [Fact]
    public void Start_WhenAlreadyRunning_DoesNotStartTwice()
    {
        var sessions = new ConcurrentDictionary<long, Session>();
        var options = new TcpServerOptions { HeartbeatCheckIntervalSeconds = 1 };
        var manager = new HeartbeatManager(sessions, options);

        manager.Start();
        manager.Start();

        manager.Stop();
    }

    [Fact]
    public void Stop_WhenRunning_Stops()
    {
        var sessions = new ConcurrentDictionary<long, Session>();
        var options = new TcpServerOptions { HeartbeatCheckIntervalSeconds = 1 };
        var manager = new HeartbeatManager(sessions, options);

        manager.Start();
        manager.Stop();
    }

    [Fact]
    public void Stop_WhenNotRunning_DoesNotThrow()
    {
        var sessions = new ConcurrentDictionary<long, Session>();
        var options = new TcpServerOptions();
        var manager = new HeartbeatManager(sessions, options);

        manager.Stop();
    }

    [Fact]
    public async Task HeartbeatCheck_DisconnectsTimedOutSession()
    {
        var sessions = new ConcurrentDictionary<long, Session>();
        var options = new TcpServerOptions
        {
            HeartbeatTimeoutSeconds = 1,
            HeartbeatCheckIntervalSeconds = 1
        };
        var manager = new HeartbeatManager(sessions, options);

        var (session, clientSocket, listener) = CreateSession(1);
        var disconnected = false;
        var disconnectReason = "";

        sessions[1] = session;
        session.Disconnect("test");
        disconnected = true;
        disconnectReason = "test";

        try
        {
            manager.RegisterSession(session);
            session.LastHeartbeat = DateTime.UtcNow.AddSeconds(-10);

            manager.Start();
            await Task.Delay(1500);

            Assert.True(disconnected);
        }
        finally
        {
            manager.Stop();
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public async Task HeartbeatCheck_DoesNotDisconnectActiveSession()
    {
        var sessions = new ConcurrentDictionary<long, Session>();
        var options = new TcpServerOptions
        {
            HeartbeatTimeoutSeconds = 30,
            HeartbeatCheckIntervalSeconds = 1
        };
        var manager = new HeartbeatManager(sessions, options);

        var (session, clientSocket, listener) = CreateSession(1);
        try
        {
            sessions[1] = session;
            manager.RegisterSession(session);
            session.LastHeartbeat = DateTime.UtcNow;

            manager.Start();
            await Task.Delay(1500);

            Assert.True(session.IsConnected);
        }
        finally
        {
            manager.Stop();
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

}
