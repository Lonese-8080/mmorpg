// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Perfolizer.Horology;

namespace MMORPG.Framework.Benchmarks;

/// <summary>
/// BenchmarkDotNet 配置类
/// </summary>
public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        // 配置基准测试运行环境
        AddJob(Job.Default
            .WithWarmupCount(3)      // 预热次数
            .WithIterationCount(10)  // 迭代次数
            .WithMinIterationTime(TimeInterval.FromMilliseconds(100)) // 最小迭代时间 100ms
        );
    }
}