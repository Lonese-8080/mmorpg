using System.Net.Sockets;
using System.Reflection;
using Google.Protobuf;
using Xunit;
using MMORPG.Framework.Network;
using MMORPG.Framework.Observability;

namespace MMORPG.Framework.Tests.Observability;

/// <summary>
/// 网络层指标集成测试
///
/// 覆盖：
/// - TcpServer 构造时注册 session.active / gc.count / gc.time_ms 三个 Gauge
/// - MessageRouter.RouteAsync 时累计 tps.total 和 message.processed_total
/// - MessageRouter.RouteAsync 时将处理耗时记录到 latency.p50 histogram
/// - Session.SendAsync / ProcessDataBuffer 累计 tps.bytes_tx 和 tps.bytes_rx
///
/// 注意：MetricsCollector.Instance 是单例；为了不影响其它测试对
/// IsEnabled 默认 false 的假设，所有测试结束后会调用 Disable()。
/// 
/// 由于 MetricsCollector / HealthCheckService 是单例，归入 Observability 集合，
/// 与其他可观测性测试串行执行以避免竞态条件。
/// </summary>
[Collection("Observability")]
public class MetricsIntegrationTests : IDisposable
{
    private readonly bool _wasEnabled;

    /// <summary>
    /// 构造：记录 MetricsCollector 的原始状态，测试结束时（Dispose）恢复。
    /// </summary>
    public MetricsIntegrationTests()
    {
        _wasEnabled = MetricsCollector.Instance.IsEnabled;
    }

    /// <summary>
    /// 析构：恢复 MetricsCollector 的 IsEnabled 状态，不影响其它测试。
    /// </summary>
    public void Dispose()
    {
        if (_wasEnabled)
            MetricsCollector.Instance.Enable();
        else
            MetricsCollector.Instance.Disable();
    }

    /// <summary>
    /// 用反射调用 Session 的私有方法 ProcessDataBuffer
    /// </summary>
    private static int InvokeProcessDataBuffer(Session session, byte[] data, int length)
    {
        var method = typeof(Session).GetMethod("ProcessDataBuffer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (int)method!.Invoke(session, new object[] { data, length })!;
    }

    /// <summary>
    /// 用反射构造 Session（因为其构造函数需要 socket 等对象）
    /// </summary>
    private static Session CreateTestSession(
        Action<Session, IMessage>? onMessageReceived = null,
        Action<Session, string>? onDisconnected = null)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var options = new TcpServerOptions();
        return new Session(
            connectionId: 42,
            socket: socket,
            options: options,
            onMessageReceived: onMessageReceived ?? ((_, _) => { }),
            onDisconnected: onDisconnected ?? ((_, _) => { }));
    }

    [Fact]
    [Trait("Category", "Metrics")]
    public void TcpServer_RegistersGauges()
    {
        // Act - 构造 TcpServer 应该注册 3 个 Gauge
        var server = new TcpServer();

        // Assert - Snapshot() 中应包含这 3 个 key
        var snapshot = MetricsCollector.Instance.Snapshot();
        Assert.Contains("session.active", snapshot.Keys);
        Assert.Contains("gc.count", snapshot.Keys);
        Assert.Contains("gc.time_ms", snapshot.Keys);

        // 额外检查：session.active 的当前值应为 0
        Assert.Equal(0.0, snapshot["session.active"]);
    }

    [Fact]
    [Trait("Category", "Metrics")]
    public async Task MessageRouter_IncrementsTpsTotal()
    {
        // Arrange - 启用指标，注册一个 handler，并准备 session
        MetricsCollector.Instance.Enable();

        var router = new MessageRouter();
        IMessage? received = null;
        router.RegisterHandler(MessageIds.C2S_Heartbeat, (session, message) =>
        {
            received = message;
            return Task.CompletedTask;
        });

        var session = CreateTestSession();
        var message = new C2S_Heartbeat();

        // 先记录原来的值，再调用 RouteAsync
        var beforeTpsTotal = MetricsCollector.Instance.GetCounter("tps.total")?.Count ?? 0;

        // Act
        await router.RouteAsync(session, message);

        // Assert
        Assert.NotNull(received);
        var afterTpsTotal = MetricsCollector.Instance.GetCounter("tps.total");
        Assert.NotNull(afterTpsTotal);
        Assert.True(afterTpsTotal!.Count >= beforeTpsTotal + 1,
            $"tps.total 应 >= {beforeTpsTotal + 1}，实际 {afterTpsTotal.Count}");
    }

    [Fact]
    [Trait("Category", "Metrics")]
    public async Task MessageRouter_MessageProcessedTotal()
    {
        // Arrange - 启用指标，注册一个 handler
        MetricsCollector.Instance.Enable();

        var router = new MessageRouter();
        router.RegisterHandler(MessageIds.C2S_Heartbeat, (session, message) => Task.CompletedTask);

        var session = CreateTestSession();
        var message = new C2S_Heartbeat();

        var beforeCount = MetricsCollector.Instance.GetCounter("message.processed_total")?.Count ?? 0;
        const int N = 5;

        // Act - 发送 5 条消息
        for (var i = 0; i < N; i++)
            await router.RouteAsync(session, message);

        // Assert
        var counter = MetricsCollector.Instance.GetCounter("message.processed_total");
        Assert.NotNull(counter);
        Assert.True(counter!.Count >= beforeCount + N,
            $"message.processed_total 应 >= {beforeCount + N}，实际 {counter.Count}");
    }

    [Fact]
    [Trait("Category", "Metrics")]
    public async Task MessageRouter_LatencyHistogram()
    {
        // Arrange - 启用指标
        MetricsCollector.Instance.Enable();

        var router = new MessageRouter();
        router.RegisterHandler(MessageIds.C2S_Heartbeat, (session, message) => Task.CompletedTask);

        var session = CreateTestSession();
        var message = new C2S_Heartbeat();

        // Act
        await router.RouteAsync(session, message);

        // Assert - histogram 至少有一个样本
        var hist = MetricsCollector.Instance.GetHistogram("latency.p50");
        Assert.NotNull(hist);
        Assert.True(hist!.SampleCount >= 1,
            $"latency.p50 样本数应 >= 1，实际 {hist.SampleCount}");
    }

    [Fact]
    [Trait("Category", "Metrics")]
    public async Task Session_BytesRxAndTx()
    {
        // Arrange - 启用指标
        MetricsCollector.Instance.Enable();

        IMessage? received = null;
        var session = CreateTestSession(
            onMessageReceived: (s, m) => { received = m; });

        // 先记录 bytes_tx / bytes_rx 计数起点
        var txBefore = MetricsCollector.Instance.GetCounter("tps.bytes_tx")?.Count ?? 0;
        var rxBefore = MetricsCollector.Instance.GetCounter("tps.bytes_rx")?.Count ?? 0;

        // Act 1: 通过 SendAsync 发一条消息，序列化后长度 = 消息头 + 消息体
        var outgoing = new C2S_Heartbeat { ClientTime = 12345 };
        await session.SendAsync(outgoing);

        // Act 2: 准备一份序列化后的字节（消息头 + 消息体），调用私有 ProcessDataBuffer
        var incoming = new C2S_Heartbeat { ClientTime = 99 };
        var incomingData = MessageSerializer.Serialize(incoming);
        InvokeProcessDataBuffer(session, incomingData, incomingData.Length);

        // Assert
        var txCounter = MetricsCollector.Instance.GetCounter("tps.bytes_tx");
        var rxCounter = MetricsCollector.Instance.GetCounter("tps.bytes_rx");

        Assert.NotNull(txCounter);
        Assert.NotNull(rxCounter);

        Assert.True(txCounter!.Count > txBefore,
            $"tps.bytes_tx 应 > {txBefore}，实际 {txCounter.Count}");
        Assert.True(rxCounter!.Count > rxBefore,
            $"tps.bytes_rx 应 > {rxBefore}，实际 {rxCounter.Count}");

        Assert.NotNull(received);
    }
}
