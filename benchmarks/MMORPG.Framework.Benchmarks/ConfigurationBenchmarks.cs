using BenchmarkDotNet.Attributes;
using MMORPG.Framework.Configuration;
using System.ComponentModel.DataAnnotations;

namespace MMORPG.Framework.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
public class ConfigurationBenchmarks
{
    private ServerConfig _config = null!;

    [GlobalSetup]
    public void Setup()
    {
        _config = new ServerConfig
        {
            Server = new ServerSettings { ServerId = 1, ServerName = "Bench" },
            Network = new NetworkSettings { Port = 9000, MaxConnections = 10000 }
        };
    }

    [Benchmark(Baseline = true)]
    public int ValidateOk()
    {
        var ctx = new ValidationContext(_config);
        var results = _config.Validate(ctx).ToList();
        return results.Count;
    }

    [Benchmark]
    public int GetMajorVersion()
    {
        return typeof(ServerConfig)
            .GetMethod("GetMajorVersion", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { "2.5.0" }) is int v ? v : -1;
    }
}
