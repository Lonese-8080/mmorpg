using System.Diagnostics.Metrics;
using Xunit;
using MMORPG.Framework.Observability;

namespace MMORPG.Framework.Tests.Observability;

/// <summary>
/// 指标模块单元测试
///
/// 由于 MetricsCollector 是单例，归入 Observability 集合，
/// 与其他可观测性测试串行执行以避免竞态条件。
///
/// ⚠️ #10 现代化：DefaultCounter / DefaultHistogram / DefaultGauge
/// 构造函数需传入 Meter。测试中使用独立 Meter 避免与全局 Meter 冲突。
/// </summary>
[Collection("Observability")]
public class MetricsTests : IDisposable
{
    /// <summary>
    /// 测试专用 Meter（避免与全局 Meter 互相干扰）
    /// </summary>
    private readonly Meter _testMeter = new("MMORPG.Framework.Tests", "1.0.0");

    /// <summary>
    /// 每个测试方法运行前执行：重置单例状态，避免测试间互相影响
    /// </summary>
    public MetricsTests()
    {
        MetricsCollector.Instance.ResetAll();
    }

    /// <summary>
    /// 每个测试方法运行后执行：再次清理确保无副作用
    /// </summary>
    public void Dispose()
    {
        MetricsCollector.Instance.ResetAll();
        _testMeter.Dispose();
    }

    [Fact]
    public void Counter_IncrementsCorrectly()
    {
        var counter = new DefaultCounter(_testMeter, "test.counter", "测试");
        counter.Increment();
        counter.Increment();
        counter.Increment();
        Assert.Equal(3, counter.Count);
        Assert.Equal(3.0, counter.Value);
    }

    [Fact]
    public void Counter_IncrementBy()
    {
        var counter = new DefaultCounter(_testMeter, "test.counter2", "测试");
        counter.IncrementBy(10);
        Assert.Equal(10, counter.Count);
        counter.IncrementBy(-3);
        Assert.Equal(7, counter.Count);
    }

    [Fact]
    public void Counter_Reset()
    {
        var counter = new DefaultCounter(_testMeter, "test.counter3", "测试");
        counter.Increment();
        counter.Increment();
        Assert.Equal(2, counter.Count);
        counter.Reset();
        Assert.Equal(0, counter.Count);
        Assert.Equal(0.0, counter.Value);
    }

    [Fact]
    public void Gauge_SetAndGet()
    {
        var gauge = new DefaultGauge(_testMeter, "test.gauge", "测试");
        gauge.Set(3.14);
        Assert.Equal(3.14, gauge.Value, 1e-9);
        gauge.Set(-2.5);
        Assert.Equal(-2.5, gauge.Value, 1e-9);
    }

    [Fact]
    public void Gauge_ValueProvider()
    {
        var value = 42.0;
        var gauge = new DefaultGauge(_testMeter, "test.gauge2", "测试", () => value);
        Assert.Equal(42.0, gauge.Value);
        value = 99.0;
        Assert.Equal(99.0, gauge.Value);
    }

    [Fact]
    public void Histogram_RecordsAndP50Correct()
    {
        var hist = new DefaultHistogram(_testMeter, "test.hist", "测试", 1024);
        for (var i = 1; i <= 100; i++)
            hist.Record(i);

        Assert.Equal(100, hist.SampleCount);
        var p50 = hist.P50;
        Assert.True(p50 >= 49.0 && p50 <= 52.0, $"P50 实际={p50}");
        Assert.True(p50 == 50.0 || p50 == 51.0, $"P50 实际={p50}");
    }

    [Fact]
    public void Histogram_P95_P99_Order()
    {
        var hist = new DefaultHistogram(_testMeter, "test.hist2", "测试", 1024);
        var rand = new Random(42);
        for (var i = 0; i < 1000; i++)
            hist.Record(rand.NextDouble() * 1000);

        var p50 = hist.P50;
        var p95 = hist.P95;
        var p99 = hist.P99;
        Assert.True(p50 <= p95, $"P50={p50} 应 <= P95={p95}");
        Assert.True(p95 <= p99, $"P95={p95} 应 <= P99={p99}");
    }

    [Fact]
    public void Histogram_SampleCountCorrect()
    {
        var hist = new DefaultHistogram(_testMeter, "test.hist3", "测试", 1024);
        for (var i = 0; i < 500; i++)
            hist.Record(i);
        Assert.Equal(500, hist.SampleCount);

        var hist2 = new DefaultHistogram(_testMeter, "test.hist3b", "测试", 1024);
        for (var i = 0; i < 2000; i++)
            hist2.Record(i);
        Assert.Equal(1024, hist2.SampleCount);
    }

    [Fact]
    public void MetricsCollector_IsSingleton()
    {
        var a = MetricsCollector.Instance;
        var b = MetricsCollector.Instance;
        Assert.Same(a, b);
    }

    [Fact]
    public void MetricsCollector_RegisterAndQuery()
    {
        var collector = MetricsCollector.Instance;
        var counter = collector.RegisterCounter("a.b", "desc");
        Assert.NotNull(counter);
        Assert.Equal(0.0, collector.GetValue("a.b"));
        counter!.Increment();
        Assert.Equal(1.0, collector.GetValue("a.b"));
    }

    [Fact]
    public void MetricsCollector_Snapshot_ReturnsAll()
    {
        var collector = MetricsCollector.Instance;
        var c = collector.RegisterCounter("snap.counter", "c");
        var g = collector.RegisterGauge("snap.gauge", "g");
        var h = collector.RegisterHistogram("snap.hist", "h");
        Assert.NotNull(c);
        Assert.NotNull(g);
        Assert.NotNull(h);
        g!.Set(5.0);
        c!.Increment();
        c.Increment();
        h!.Record(1.0);

        var snapshot = collector.Snapshot();
        Assert.True(snapshot.Count >= 3, $"期望至少 3 项，实际 {snapshot.Count}");
        Assert.True(snapshot.ContainsKey("snap.counter"));
        Assert.True(snapshot.ContainsKey("snap.gauge"));
        Assert.True(snapshot.ContainsKey("snap.hist"));
    }

    [Fact]
    public void MetricsCollector_EnabledFlag()
    {
        var collector = MetricsCollector.Instance;
        Assert.False(collector.IsEnabled);
        collector.Enable();
        Assert.True(collector.IsEnabled);
        collector.Disable();
        Assert.False(collector.IsEnabled);
    }

    [Fact]
    public void Counter_ThreadSafe()
    {
        var counter = new DefaultCounter(_testMeter, "test.threadsafe", "测试");
        var tasks = new Task[10];
        for (var t = 0; t < 10; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < 1000; i++)
                    counter.Increment();
            });
        }
        Task.WaitAll(tasks);
        Assert.Equal(10000, counter.Count);
    }
}
