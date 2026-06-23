// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Attributes;
using MMORPG.Framework.Configuration;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Benchmarks;

/// <summary>
/// 内存基准测试
/// 
/// 测试场景：
/// - 对象池使用
/// - GC 压力测试
/// - 内存分配追踪
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class MemoryBenchmarks
{
    [Benchmark(Baseline = true)]
    [IterationCount(1000)]
    public void StringAllocation_NewString()
    {
        var s = new string('X', 100);
        _ = s.Length;
    }

    [Benchmark]
    [IterationCount(1000)]
    public void StringAllocation_Stackalloc()
    {
        Span<char> buffer = stackalloc char[100];
        buffer.Fill('X');
        _ = buffer.Length;
    }

    [Benchmark]
    [IterationCount(100)]
    public byte[] ByteArrayAllocation_NewArray()
    {
        var array = new byte[1024];
        array[0] = 1;
        return array;
    }

    [Benchmark]
    [IterationCount(100)]
    public void GC_Collect0()
    {
        for (int i = 0; i < 100; i++)
        {
            var obj = new object();
            _ = obj.GetHashCode();
        }
        GC.Collect(0);
    }

    [Benchmark]
    [IterationCount(100)]
    public void GC_Collect2()
    {
        for (int i = 0; i < 100; i++)
        {
            var obj = new object();
            _ = obj.GetHashCode();
        }
        GC.Collect(2, GCCollectionMode.Forced);
    }

    [Benchmark]
    public void Dictionary_Lookup_1000()
    {
        var dict = new System.Collections.Generic.Dictionary<int, string>();
        for (int i = 0; i < 1000; i++)
        {
            dict[i] = i.ToString();
        }

        string result = "";
        for (int i = 0; i < 1000; i++)
        {
            result = dict[i];
        }
    }

    [Benchmark]
    public void List_Add_10000()
    {
        var list = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 10000; i++)
        {
            list.Add(i);
        }
    }

    [Benchmark]
    public void List_Capacity_10000()
    {
        var list = new System.Collections.Generic.List<int>(10000);
        for (int i = 0; i < 10000; i++)
        {
            list.Add(i);
        }
    }
}
