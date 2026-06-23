// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

// ====================================================================
// 雪花算法 ID 生成器
//
// 雪花算法（Snowflake）是分布式系统中生成全局唯一 ID 的标准算法。
// 由 Twitter 于 2010 年发明，广泛用于分布式系统。
//
// ID 结构（共 64 位）：
// ┌───────────────────────────────────────────────────────────────────┐
// │ 0 │ 时间戳(41位)              │ 机器ID(10位)    │ 序列号(12位)   │
// └───────────────────────────────────────────────────────────────────┘
//   ▲
//   └─ 符号位，恒为 0（保证 ID 为正数）
//
// 详细说明：
// 1️⃣ 时间戳：41 位，精度为毫秒
//    - 从 2024-01-01 00:00:00 UTC 开始计时（可配置）
//    - 可以使用 41 年（2^41 ≈ 2.2 万亿毫秒 ≈ 69.7 年）
//    - 所以这个系统在 2093 年之前都是安全的
//
// 2️⃣ 机器 ID：10 位，最多支持 1024 个节点
//    - 由数据中心 ID（5 位）+ 工作机器 ID（5 位）组成
//    - 或者直接用一个 10 位数字
//    - 不同节点必须分配不同的机器 ID，否则会产生重复
//
// 3️⃣ 序列号：12 位，每毫秒最多 4096 个 ID
//    - 同一毫秒内，从 0 开始自增
//    - 超过 4096 时，等待下一毫秒
//
// 性能特点：
// - 每秒最多生成 4096 × 1000 ≈ 400 万 ID
// - 实际场景中，单节点每秒 10 万+ ID 绰绰有余
// - 生成一个 ID 只需要一次原子操作，非常快
//
// 线程安全：
// - 使用 Interlocked 实现无锁原子操作
// - 多线程环境下保证不重复
// ====================================================================

using System.Diagnostics;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Threading;

/// <summary>
/// 雪花算法 ID 生成器配置
/// </summary>
public class SnowflakeIdOptions
{
    /// <summary>
    /// 起始时间（UTC）
    /// 默认：2024-01-01 00:00:00 UTC
    /// 这个时间是雪花算法的时间起点，不能改
    /// </summary>
    public DateTime StartTime { get; set; } = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// 机器 ID（0-1023）
    /// 分布式部署时每个节点需要不同的 ID
    /// </summary>
    public int WorkerId { get; set; } = 0;

    /// <summary>
    /// 机器 ID 位数（默认 10 位，最多 1024 节点）
    /// </summary>
    public int WorkerIdBits { get; set; } = 10;

    /// <summary>
    /// 序列号位数（默认 12 位，每毫秒 4096 个）
    /// </summary>
    public int SequenceBits { get; set; } = 12;
}

/// <summary>
/// 雪花算法 ID 生成器
///
/// 功能：
/// - 生成全局唯一的 64 位整数 ID
/// - ID 按时间递增（分布式数据库友好，可以保证索引顺序）
/// - 线程安全，多线程环境下无重复
///
/// 使用示例：
/// <code>
/// var generator = new SnowflakeIdGenerator(1);  // 节点 1
/// long id1 = generator.NewId();  // 生成第一个 ID
/// long id2 = generator.NewId();  // 生成第二个 ID
/// </code>
/// </summary>
public class SnowflakeIdGenerator
{
    #region 常量定义

    /// <summary>
    /// 时间戳占用位数
    /// </summary>
    private const int TimestampBits = 41;

    #endregion

    #region 私有字段

    /// <summary>
    /// 起始时间的时间戳（毫秒）
    /// 用于减去，避免时间戳浪费位数
    /// </summary>
    private readonly long _startTimeMillis;

    /// <summary>
    /// 机器 ID（已左移到正确位置）
    /// 最终 ID 中这一段保持不变
    /// </summary>
    private readonly long _workerId;

    /// <summary>
    /// 机器 ID 位数
    /// </summary>
    private readonly int _workerIdBits;

    /// <summary>
    /// 序列号位数
    /// </summary>
    private readonly int _sequenceBits;

    /// <summary>
    /// 机器 ID 左移位数 = 序列号位数
    /// </summary>
    private readonly int _workerIdShift;

    /// <summary>
    /// 时间戳左移位数 = 机器 ID 位数 + 序列号位数
    /// </summary>
    private readonly int _timestampShift;

    /// <summary>
    /// 序列号掩码（用于按位与，防止溢出）
    /// 例如 12 位序列号，掩码 = 0xFFF = 4095
    /// </summary>
    private readonly long _sequenceMask;

    /// <summary>
    /// 当前序列号（使用 Interlocked 保证线程安全）
    /// </summary>
    private long _sequence;

    /// <summary>
    /// 上次生成 ID 的时间戳
    /// 用于检测时间回拨（系统时间被调后）
    /// </summary>
    private long _lastTimestamp;

    /// <summary>
    /// 自旋锁（用于时间回拨时的等待策略）
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// ⚠️ 现代化（#16 修复）：使用 <see cref="TimeProvider"/> 抽象时间源
    /// 便于单元测试中注入确定性时间，并兼容 .NET 8+ 的 TimeProvider 抽象。
    /// 默认使用 <see cref="TimeProvider.System"/>（即系统 UTC 时钟）。
    /// </summary>
    private readonly TimeProvider _timeProvider;

    #endregion

    #region 公共属性

    /// <summary>
    /// 获取起始时间
    /// </summary>
    public DateTime StartTime { get; }

    /// <summary>
    /// 获取机器 ID
    /// </summary>
    public int WorkerId { get; }

    /// <summary>
    /// 已生成的 ID 总数（用于统计）
    /// </summary>
    public long GeneratedCount { get; private set; }

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建雪花算法 ID 生成器
    /// </summary>
    /// <param name="workerId">机器 ID（0-1023，分布式部署时每个节点需要不同）</param>
    /// <param name="timeProvider">时间提供者（默认 <see cref="TimeProvider.System"/>）</param>
    public SnowflakeIdGenerator(int workerId = 0, TimeProvider? timeProvider = null)
        : this(new SnowflakeIdOptions { WorkerId = workerId }, timeProvider)
    {
    }

    /// <summary>
    /// 创建雪花算法 ID 生成器（带配置）
    /// </summary>
    /// <param name="options">配置选项</param>
    /// <param name="timeProvider">时间提供者（默认 <see cref="TimeProvider.System"/>）</param>
    public SnowflakeIdGenerator(SnowflakeIdOptions options, TimeProvider? timeProvider = null)
    {
        // 参数校验
        if (options.WorkerId < 0)
            throw new ArgumentException("机器 ID 不能为负数", nameof(options));

        // 计算机器 ID 的最大值
        var maxWorkerId = (1L << options.WorkerIdBits) - 1;
        if (options.WorkerId > maxWorkerId)
            throw new ArgumentException(
                $"机器 ID 超过最大值 {maxWorkerId}",
                nameof(options));

        if (options.WorkerIdBits <= 0 || options.WorkerIdBits > 20)
            throw new ArgumentException(
                "机器 ID 位数必须在 1-20 之间",
                nameof(options));

        if (options.SequenceBits <= 0 || options.SequenceBits > 20)
            throw new ArgumentException(
                "序列号位数必须在 1-20 之间",
                nameof(options));

        // 校验总位数：符号位1 + 时间戳41 + 机器ID + 序列号 = 64
        if (TimestampBits + options.WorkerIdBits + options.SequenceBits > 63)
            throw new ArgumentException(
                "总位数超过 63 位，无法放入 long",
                nameof(options));

        // 保存配置
        StartTime = options.StartTime;
        WorkerId = options.WorkerId;
        _workerIdBits = options.WorkerIdBits;
        _sequenceBits = options.SequenceBits;
        _startTimeMillis = (long)(options.StartTime - DateTime.UnixEpoch).TotalMilliseconds;

        // 计算位偏移
        _workerIdShift = options.SequenceBits;
        _timestampShift = options.SequenceBits + options.WorkerIdBits;

        // 计算序列号掩码
        _sequenceMask = (1L << options.SequenceBits) - 1;

        // 机器 ID 左移到正确位置
        _workerId = (long)options.WorkerId << options.SequenceBits;

        // 初始化序列号和时间戳
        _sequence = -1;
        _lastTimestamp = -1;
        _timeProvider = timeProvider ?? TimeProvider.System;

        Logger.Info("Network",
            "雪花算法 ID 生成器初始化: 机器ID={0}, 起始时间={1}",
            options.WorkerId,
            options.StartTime.ToString("yyyy-MM-dd HH:mm:ss UTC"));
    }

    #endregion

    #region 公共方法 - ID 生成

    /// <summary>
    /// 生成一个新的唯一 ID
    ///
    /// 这是核心方法，线程安全
    ///
    /// 执行流程：
    /// 1. 获取当前时间戳（毫秒）
    /// 2. 如果时间戳与上次相同，序列号 +1
    /// 3. 如果序列号达到上限（4096），等待到下一毫秒
    /// 4. 如果时间回拨（系统时间被调后），等待时间追上来
    /// 5. 组合各部分（时间戳、机器ID、序列号）
    /// </summary>
    public long NewId()
    {
        lock (_lock)
        {
            var timestamp = GetCurrentTimestamp();

            // 处理时间回拨：系统时间被调后了
            // 这是雪花算法的常见问题，需要有处理策略
            if (timestamp < _lastTimestamp)
            {
                var timeDiff = _lastTimestamp - timestamp;

                // 回拨时间小于 5 毫秒，可以等待时间追上来
                if (timeDiff <= 5)
                {
                    var spinCount = 0;
                    while (timestamp < _lastTimestamp)
                    {
                        spinCount++;
                        if (spinCount > 1000)
                        {
                            // 等待超过 1000 次循环仍未追上来
                            // 记录警告并强制使用新时间戳
                            Logger.Warning("Network",
                                "雪花算法时间回拨: 回拨={0}ms, 等待超时，强制使用新时间戳",
                                timeDiff);
                            break;
                        }

                        Thread.SpinWait(10);  // 自旋等待
                        timestamp = GetCurrentTimestamp();
                    }

                    // 正常追上来了：不打日志，避免频繁日志刷屏
                }
                else
                {
                    // 大回拨：系统时间可能被篡改，需要告警
                    Logger.Error("Network",
                        "雪花算法时间回拨: 回拨={0}ms, 系统时间可能被篡改，使用新时间戳",
                        timeDiff);
                }
            }

            // 正常情况：同一毫秒内，序列号自增
            if (timestamp == _lastTimestamp)
            {
                // 序列号自增，并使用掩码防止溢出
                // 例如：_sequenceMask = 0xFFF（4095）
                // 当 _sequence = 4095 时，自增后 & 掩码 = 0，序列号重置
                _sequence = (_sequence + 1) & _sequenceMask;

                // 如果序列号归零，说明本毫秒的 ID 已经用完
                // 需要等待到下一毫秒
                if (_sequence == 0)
                {
                    timestamp = WaitUntilNextMillis(_lastTimestamp);
                }
            }
            else
            {
                // 新的毫秒，序列号从 0 开始
                // 为了保证 ID 递增，同一毫秒内的 ID 也是递增的
                _sequence = 0;
            }

            // 更新上次时间戳
            _lastTimestamp = timestamp;

            // 统计生成数量
            GeneratedCount++;

            // 组合各部分：时间戳 << 22 | 机器 ID << 12 | 序列号
            // 注意这里要用 & 操作确保每部分在正确的位范围内
            var id = ((timestamp - _startTimeMillis) << _timestampShift)
                     | _workerId
                     | _sequence;

            return id;
        }
    }

    /// <summary>
    /// 批量生成多个 ID
    /// </summary>
    /// <param name="count">要生成的 ID 数量</param>
    /// <returns>ID 数组</returns>
    public long[] NewIds(int count)
    {
        if (count <= 0)
            throw new ArgumentException("数量必须大于 0", nameof(count));

        var result = new long[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = NewId();
        }
        return result;
    }

    #endregion

    #region 公共方法 - 解析 ID

    /// <summary>
    /// 从 ID 中解析出时间戳
    /// </summary>
    /// <param name="id">雪花算法生成的 ID</param>
    /// <returns>生成时间</returns>
    public DateTime GetTimestampFromId(long id)
    {
        var timestampPart = (id >> _timestampShift) + _startTimeMillis;
        return DateTime.UnixEpoch.AddMilliseconds(timestampPart);
    }

    /// <summary>
    /// 从 ID 中解析出机器 ID
    /// </summary>
    /// <param name="id">雪花算法生成的 ID</param>
    /// <returns>机器 ID</returns>
    public int GetWorkerIdFromId(long id)
    {
        var workerMask = (1L << _workerIdBits) - 1;
        return (int)((id >> _workerIdShift) & workerMask);
    }

    /// <summary>
    /// 从 ID 中解析出序列号
    /// </summary>
    /// <param name="id">雪花算法生成的 ID</param>
    /// <returns>序列号</returns>
    public long GetSequenceFromId(long id)
    {
        return id & _sequenceMask;
    }

    /// <summary>
    /// 解析 ID 的所有信息（用于调试）
    /// </summary>
    /// <param name="id">雪花算法生成的 ID</param>
    /// <returns>解析结果</returns>
    public (DateTime Timestamp, int WorkerId, long Sequence) ParseId(long id)
    {
        return (
            GetTimestampFromId(id),
            GetWorkerIdFromId(id),
            GetSequenceFromId(id)
        );
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 获取当前时间戳（毫秒，从 Unix 纪元开始）
    /// 使用 <see cref="TimeProvider"/> 抽象（#16 现代化），便于测试注入。
    /// </summary>
    private long GetCurrentTimestamp()
    {
        return _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 等待直到下一毫秒
    /// 当本毫秒的 ID 用完时调用
    /// </summary>
    private long WaitUntilNextMillis(long lastTimestamp)
    {
        var timestamp = GetCurrentTimestamp();
        var spinCount = 0;

        // 自旋等待
        while (timestamp <= lastTimestamp)
        {
            spinCount++;
            if (spinCount > 10000)
            {
                // 超过 10000 次自旋仍未进入下一毫秒
                // 使用 Thread.Sleep(1) 让出 CPU
                Thread.Sleep(1);
                spinCount = 0;
            }
            else
            {
                // 短期自旋，避免上下文切换
                Thread.SpinWait(100);
            }

            timestamp = GetCurrentTimestamp();
        }

        return timestamp;
    }

    #endregion

    #region 静态实例

    /// <summary>
    /// 默认实例（方便快速使用）
    ///
    /// <code>
    /// long id = SnowflakeIdGenerator.Default.NewId();
    /// </code>
    /// </summary>
    public static SnowflakeIdGenerator Default { get; } = new(0);

    #endregion
}

/// <summary>
/// ID 生成器工厂
/// 简化 ID 生成器的创建和管理
/// </summary>
public static class IdGenerator
{
    /// <summary>
    /// 生成一个 ID（使用默认生成器）
    /// </summary>
    /// <returns>唯一的 64 位 ID</returns>
    public static long NewId()
    {
        return SnowflakeIdGenerator.Default.NewId();
    }

    /// <summary>
    /// 批量生成 ID
    /// </summary>
    /// <param name="count">数量</param>
    /// <returns>ID 数组</returns>
    public static long[] NewIds(int count)
    {
        return SnowflakeIdGenerator.Default.NewIds(count);
    }
}
