using Xunit;
using MMORPG.Framework.Threading;

namespace MMORPG.Framework.Tests.Threading;

/// <summary>
/// 雪花算法 ID 生成器测试
/// </summary>
public class SnowflakeIdGeneratorTests
{
    [Fact]
    public void 生成ID_单个_应该为正数()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(1);

        // Act
        var id = generator.NewId();

        // Assert
        Assert.True(id > 0);  // ID 应该是正数（最高位始终为 0）
    }

    [Fact]
    public void 生成ID_多个_应该唯一且递增()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(2);
        var count = 10000;
        var ids = new long[count];

        // Act
        for (var i = 0; i < count; i++)
        {
            ids[i] = generator.NewId();
        }

        // Assert - 检查唯一性
        var uniqueIds = new HashSet<long>(ids);
        Assert.Equal(count, uniqueIds.Count);  // 所有 ID 都不重复

        // Assert - 检查递增性
        for (var i = 1; i < count; i++)
        {
            Assert.True(ids[i] > ids[i - 1],
                $"第 {i} 个 ID ({ids[i]}) 应该大于第 {i - 1} 个 ({ids[i - 1]})");
        }
    }

    [Fact]
    public void 生成ID_批量生成_应该正确()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(3);

        // Act
        var ids = generator.NewIds(100);

        // Assert
        Assert.Equal(100, ids.Length);
        var unique = new HashSet<long>(ids);
        Assert.Equal(100, unique.Count);  // 所有 ID 都不重复
    }

    [Fact]
    public void 生成ID_不同机器_应该不冲突()
    {
        // Arrange
        var generator1 = new SnowflakeIdGenerator(1);
        var generator2 = new SnowflakeIdGenerator(2);
        var allIds = new HashSet<long>();

        // Act
        for (var i = 0; i < 1000; i++)
        {
            allIds.Add(generator1.NewId());
            allIds.Add(generator2.NewId());
        }

        // Assert - 两个机器的 ID 不冲突
        Assert.Equal(2000, allIds.Count);
    }

    [Fact]
    public void 解析ID_时间戳_应该正确()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(5);

        // Act - 生成一个 ID 并立即解析
        var id = generator.NewId();
        var timestamp = generator.GetTimestampFromId(id);

        // Assert - 时间应该在合理范围内
        var now = DateTime.UtcNow;
        Assert.True(timestamp <= now, "生成时间不应晚于当前时间");
        Assert.True(timestamp > now.AddMinutes(-1), "生成时间不应早于当前时间 1 分钟");
    }

    [Fact]
    public void 解析ID_机器ID_应该正确()
    {
        // Arrange
        var workerId = 7;
        var generator = new SnowflakeIdGenerator(workerId);

        // Act
        var id = generator.NewId();
        var parsedWorkerId = generator.GetWorkerIdFromId(id);

        // Assert
        Assert.Equal(workerId, parsedWorkerId);
    }

    [Fact]
    public void 解析ID_序列号_应该递增()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(9);

        // Act - 连续生成几个 ID
        var id1 = generator.NewId();
        var id2 = generator.NewId();
        var id3 = generator.NewId();

        var seq1 = generator.GetSequenceFromId(id1);
        var seq2 = generator.GetSequenceFromId(id2);
        var seq3 = generator.GetSequenceFromId(id3);

        // Assert - 序列号应该递增（或在同一毫秒内递增）
        Assert.True(seq2 >= seq1);
        Assert.True(seq3 >= seq2);
    }

    [Fact]
    public void 配置_自定义位长度_应该正确()
    {
        // Arrange
        var options = new SnowflakeIdOptions
        {
            WorkerId = 3,
            WorkerIdBits = 4,  // 4 位 = 最多 16 个机器
            SequenceBits = 8   // 8 位 = 每毫秒最多 256 个 ID
        };

        var generator = new SnowflakeIdGenerator(options);

        // Act
        var id = generator.NewId();
        var workerId = generator.GetWorkerIdFromId(id);

        // Assert
        Assert.Equal(3, workerId);
        Assert.True(id > 0);
    }

    [Fact]
    public void 配置_机器ID超出范围_应该抛异常()
    {
        // Arrange & Act & Assert
        var options = new SnowflakeIdOptions
        {
            WorkerId = 100,   // 机器 ID = 100
            WorkerIdBits = 5   // 5 位 = 最多 32 个机器 (0-31)
        };

        Assert.Throws<ArgumentException>(() => new SnowflakeIdGenerator(options));
    }

    [Fact]
    public void 默认生成器_应该能正常工作()
    {
        // Arrange & Act
        var id1 = IdGenerator.NewId();
        var id2 = IdGenerator.NewId();
        var ids = IdGenerator.NewIds(10);

        // Assert
        Assert.True(id1 > 0);
        Assert.True(id2 > id1);  // 递增
        Assert.Equal(10, ids.Length);
        Assert.True(ids[9] > ids[0]);  // 批量 ID 也是递增的
    }

    [Fact]
    public void 多线程并发_生成ID_应该线程安全()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(4);
        var threadCount = 8;
        var idsPerThread = 5000;
        var allIds = new List<long>(threadCount * idsPerThread);
        var lockObj = new object();

        // Act - 同时启动多个线程生成 ID
        var tasks = new Task[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                var localIds = new long[idsPerThread];
                for (var i = 0; i < idsPerThread; i++)
                {
                    localIds[i] = generator.NewId();
                }

                lock (lockObj)
                {
                    allIds.AddRange(localIds);
                }
            });
        }

        Task.WaitAll(tasks);

        // Assert - 所有 ID 都不重复
        var uniqueIds = new HashSet<long>(allIds);
        Assert.Equal(threadCount * idsPerThread, uniqueIds.Count);
    }

    [Fact]
    public void 生成数量统计_应该正确()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(6);

        // Act
        var before = generator.GeneratedCount;
        for (var i = 0; i < 42; i++)
        {
            generator.NewId();
        }
        var after = generator.GeneratedCount;

        // Assert
        Assert.Equal(42, after - before);
    }

    [Fact]
    public void ParseId_返回完整信息()
    {
        // Arrange
        var generator = new SnowflakeIdGenerator(15);

        // Act
        var id = generator.NewId();
        var (timestamp, workerId, sequence) = generator.ParseId(id);

        // Assert
        Assert.True(timestamp <= DateTime.UtcNow);
        Assert.Equal(15, workerId);
        Assert.True(sequence >= 0);
    }
}
