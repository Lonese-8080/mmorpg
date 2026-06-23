// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using MMORPG.Framework.Threading;

namespace MMORPG.Framework.Benchmarks;

/// <summary>
/// 雪花ID生成器基准测试
/// 
/// 测试场景：
/// - 单线程生成
/// - 多线程并发生成
/// - ID唯一性验证
/// - 与 Guid.NewGuid() 对比
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class SnowflakeIdBenchmarks
{
    private SnowflakeIdGenerator? _generator;

    [GlobalSetup]
    public void Setup()
    {
        _generator = new SnowflakeIdGenerator(1);
    }

    [Benchmark(Baseline = true)]
    public long SingleThreaded_1000Ids()
    {
        long sum = 0;
        for (int i = 0; i < 1000; i++)
        {
            sum += _generator!.NewId();
        }
        return sum;
    }

    [Benchmark]
    public async Task MultiThreaded_1000Ids()
    {
        var tasks = new List<Task<long>>();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                long sum = 0;
                for (int j = 0; j < 100; j++)
                {
                    sum += _generator!.NewId();
                }
                return sum;
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public long GuidVsSnowflake_Guid()
    {
        long sum = 0;
        for (int i = 0; i < 1000; i++)
        {
            sum += Guid.NewGuid().GetHashCode();
        }
        return sum;
    }

    [Benchmark]
    public bool Uniqueness_100000Ids()
    {
        var ids = new HashSet<long>();

        for (int i = 0; i < 100000; i++)
        {
            ids.Add(_generator!.NextId());
        }

        return ids.Count == 100000;
    }
}

/// <summary>
/// MessageChannel 基准测试
/// 
/// 测试场景：
/// - 单线程入队出队
/// - 多线程并发入队
/// - 多线程并发出队
/// - 批量操作
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class MessageChannelBenchmarks
{
    private MessageChannel<int>? _channel;

    [GlobalSetup]
    public void Setup()
    {
        _channel = MessageChannel.CreateUnbounded<int>();
    }

    [IterationSetup]
    public void ClearChannel()
    {
        while (_channel!.TryRead(out _)) { }
    }

    [Benchmark(Baseline = true)]
    public void SingleThread_EnqueueDequeue_1000()
    {
        for (int i = 0; i < 1000; i++)
        {
            _channel!.WriteAsync(i).AsTask().Wait();
        }

        for (int i = 0; i < 1000; i++)
        {
            _channel!.TryRead(out _);
        }
    }

    [Benchmark]
    public async Task MultiThread_Enqueue_10Threads()
    {
        var tasks = new List<Task>();

        for (int t = 0; t < 10; t++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    _channel!.WriteAsync(i).AsTask().Wait();
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public void TryRead_Batch_10000()
    {
        // 先填充数据
        for (int i = 0; i < 10000; i++)
        {
            _channel!.WriteAsync(i).AsTask().Wait();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var count = 0;

        while (_channel!.TryRead(out _))
        {
            count++;
        }

        sw.Stop();
    }
}

/// <summary>
/// 原子计数器基准测试
/// 
/// 测试场景：
/// - 基本增量操作
/// - 并发增量
/// - 与 lock 方式对比
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class AtomicCounterBenchmarks
{
    private AtomicCounter? _counter;
    private long _lockCounter;
    private readonly object _lock = new();

    [GlobalSetup]
    public void Setup()
    {
        _counter = new AtomicCounter();
        _lockCounter = 0;
    }

    [Benchmark(Baseline = true)]
    public void AtomicIncrement_1000()
    {
        for (int i = 0; i < 1000; i++)
        {
            _counter!.Increment();
        }
    }

    [Benchmark]
    public void LockIncrement_1000()
    {
        for (int i = 0; i < 1000; i++)
        {
            lock (_lock)
            {
                _lockCounter++;
            }
        }
    }

    [Benchmark]
    public async Task AtomicIncrement_Parallel_10Threads()
    {
        var tasks = new List<Task>();

        for (int t = 0; t < 10; t++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    _counter!.Increment();
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task LockIncrement_Parallel_10Threads()
    {
        var tasks = new List<Task>();

        for (int t = 0; t < 10; t++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    lock (_lock)
                    {
                        _lockCounter++;
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);
    }
}

/// <summary>
/// ConcurrentQueue 对比测试
/// 
/// 测试场景：
/// - 与 MessageChannel 对比
/// - 并发性能
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class ConcurrentQueueBenchmarks
{
    private ConcurrentQueue<int>? _queue;
    private MessageChannel<int>? _channel;

    [GlobalSetup]
    public void Setup()
    {
        _queue = new ConcurrentQueue<int>();
        _channel = MessageChannel.CreateUnbounded<int>();
    }

    [Benchmark(Baseline = true)]
    public void ConcurrentQueue_EnqueueDequeue_1000()
    {
        for (int i = 0; i < 1000; i++)
        {
            _queue!.Enqueue(i);
        }

        for (int i = 0; i < 1000; i++)
        {
            _queue!.TryDequeue(out _);
        }
    }

    [Benchmark]
    public void MessageChannel_WriteRead_1000()
    {
        for (int i = 0; i < 1000; i++)
        {
            _channel!.WriteAsync(i).AsTask().Wait();
        }

        for (int i = 0; i < 1000; i++)
        {
            _channel!.TryRead(out _);
        }
    }
}
