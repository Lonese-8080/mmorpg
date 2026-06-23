using BenchmarkDotNet.Attributes;
using System.Threading.Channels;

namespace MMORPG.Framework.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
public class ChannelBenchmarks
{
    private Channel<int> _unbounded = null!;
    private Channel<int> _bounded = null!;

    [GlobalSetup]
    public void Setup()
    {
        _unbounded = Channel.CreateUnbounded<int>();
        _bounded = Channel.CreateBounded<int>(new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.Wait });
    }

    [Benchmark(Baseline = true)]
    public async Task<int> UnboundedProduceConsume()
    {
        for (int i = 0; i < 1000; i++) await _unbounded.Writer.WriteAsync(i);
        var sum = 0;
        for (int i = 0; i < 1000; i++) sum += await _unbounded.Reader.ReadAsync();
        return sum;
    }

    [Benchmark]
    public async Task<int> BoundedProduceConsume()
    {
        for (int i = 0; i < 1000; i++) await _bounded.Writer.WriteAsync(i);
        var sum = 0;
        for (int i = 0; i < 1000; i++) sum += await _bounded.Reader.ReadAsync();
        return sum;
    }
}
