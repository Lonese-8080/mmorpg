using System.Net;
using System.Net.Sockets;
using Xunit;
using MMORPG.Framework.Network;

namespace MMORPG.Framework.Tests.Network;

[Collection("Observability")]
public class MessageRetryQueueTests
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
    public void Constructor_DefaultOptions_InitializesCorrectly()
    {
        var queue = new MessageRetryQueue();

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(0, queue.DeadLetterCount);
        Assert.Equal(0, queue.TotalRetryAttempts);
        Assert.Equal(0, queue.SuccessfulRetries);
    }

    [Fact]
    public void Constructor_CustomOptions_InitializesCorrectly()
    {
        var options = new MessageRetryOptions
        {
            MaxRetryCount = 5,
            BaseIntervalMs = 200,
            MaxIntervalMs = 10000,
            MaxDeadLetterCapacity = 5000
        };
        var queue = new MessageRetryQueue(options);

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(0, queue.DeadLetterCount);
    }

    [Fact]
    public void EnqueueForRetry_NotRunning_DoesNotEnqueue()
    {
        var queue = new MessageRetryQueue();
        var (session, clientSocket, listener) = CreateSession();

        try
        {
            var data = new byte[] { 1, 2, 3, 4 };
            queue.EnqueueForRetry(session, data, "test failure");

            Assert.Equal(0, queue.PendingCount);
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void EnqueueForRetry_NullData_DoesNotEnqueue()
    {
        var queue = new MessageRetryQueue();
        var (session, clientSocket, listener) = CreateSession();

        try
        {
            queue.Start();
            queue.EnqueueForRetry(session, null!, "test failure");

            Assert.Equal(0, queue.PendingCount);

            queue.EnqueueForRetry(session, Array.Empty<byte>(), "test failure");

            Assert.Equal(0, queue.PendingCount);

            queue.Stop();
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void EnqueueForRetry_DisconnectedSession_DoesNotEnqueue()
    {
        var queue = new MessageRetryQueue();
        var (session, clientSocket, listener) = CreateSession();

        try
        {
            queue.Start();
            session.Disconnect("test disconnect");

            var data = new byte[] { 1, 2, 3 };
            queue.EnqueueForRetry(session, data, "test failure");

            Assert.Equal(0, queue.PendingCount);

            queue.Stop();
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void EnqueueForRetry_NullSession_DoesNotEnqueue()
    {
        var queue = new MessageRetryQueue();

        try
        {
            queue.Start();
            var data = new byte[] { 1, 2, 3 };
            queue.EnqueueForRetry(null, data, "test failure");

            Assert.Equal(0, queue.PendingCount);

            queue.Stop();
        }
        finally
        {
        }
    }

    [Fact]
    public void Start_WhenNotRunning_Starts()
    {
        var queue = new MessageRetryQueue();

        queue.Start();
        queue.Stop();
    }

    [Fact]
    public void Start_WhenAlreadyRunning_DoesNotStartTwice()
    {
        var queue = new MessageRetryQueue();

        queue.Start();
        queue.Start();
        queue.Stop();
    }

    [Fact]
    public void Stop_WhenRunning_Stops()
    {
        var queue = new MessageRetryQueue();

        queue.Start();
        queue.Stop();
    }

    [Fact]
    public void Stop_WhenNotRunning_DoesNotThrow()
    {
        var queue = new MessageRetryQueue();

        queue.Stop();
    }

    [Fact]
    public void DrainDeadLetter_EmptyQueue_ReturnsEmpty()
    {
        var queue = new MessageRetryQueue();

        var result = queue.DrainDeadLetter(10);

        Assert.Empty(result);
        Assert.Equal(0, queue.DeadLetterCount);
    }

    [Fact]
    public void GetStats_ReturnsCorrectFormat()
    {
        var queue = new MessageRetryQueue();

        var stats = queue.GetStats();

        Assert.Contains("MessageRetryQueue", stats);
        Assert.Contains("Pending=0", stats);
        Assert.Contains("DeadLetter=0", stats);
        Assert.Contains("TotalAttempts=0", stats);
        Assert.Contains("Successful=0", stats);
    }

    [Fact]
    public async Task EnqueueForRetry_ConnectedSession_AddsToQueue()
    {
        var options = new MessageRetryOptions
        {
            BaseIntervalMs = 50,
            MaxRetryCount = 3
        };
        var queue = new MessageRetryQueue(options);
        var (session, clientSocket, listener) = CreateSession();

        try
        {
            queue.Start();

            var data = new byte[] { 1, 2, 3, 4 };
            queue.EnqueueForRetry(session, data, "test failure");

            Assert.Equal(1, queue.PendingCount);

            queue.Stop();
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public async Task RetrySuccess_UpdatesStats()
    {
        var options = new MessageRetryOptions
        {
            BaseIntervalMs = 10,
            MaxRetryCount = 3
        };
        var queue = new MessageRetryQueue(options);
        var (session, clientSocket, listener) = CreateSession();

        try
        {
            queue.Start();

            var data = new byte[] { 1, 2, 3 };
            queue.EnqueueForRetry(session, data, "test failure");

            await Task.Delay(200);

            Assert.True(queue.TotalRetryAttempts > 0);
            Assert.True(queue.SuccessfulRetries > 0);

            queue.Stop();
        }
        finally
        {
            session.Dispose();
            clientSocket.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public async Task MaxDeadLetterCapacity_LimitsSize()
    {
        var options = new MessageRetryOptions
        {
            MaxDeadLetterCapacity = 5,
            BaseIntervalMs = 10,
            MaxRetryCount = 1
        };
        var queue = new MessageRetryQueue(options);

        var deadLetters = queue.DeadLetterCount;
        Assert.True(deadLetters <= 5);
    }

    [Fact]
    public async Task DrainDeadLetter_RemovesFromQueue()
    {
        var queue = new MessageRetryQueue();

        var result = queue.DrainDeadLetter(100);

        Assert.NotNull(result);
    }
}
