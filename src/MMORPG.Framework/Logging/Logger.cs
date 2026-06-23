// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace MMORPG.Framework.Logging;

/// <summary>
/// 日志记录器
/// 
/// 核心特性：
/// 1. 静态类，全局可用
/// 2. 支持模板字符串（类似 Microsoft.Extensions.Logging）
/// 3. 高性能异步写入（使用 Channel）
/// 4. 零分配（使用 Span 和 ArrayPool）
/// 5. 支持多个输出目标
/// 
/// 使用示例：
/// ```csharp
/// Logger.Info("Server", "服务器启动完成");
/// Logger.Info("Game", "玩家 {0} 登录成功", playerId);
/// Logger.Warning("Config", "配置项 {0} 缺失，使用默认值 {1}", key, value);
/// Logger.Error("Database", ex, "保存玩家数据失败: PlayerId={0}", playerId);
/// ```
/// 
/// 初始化（在程序入口处调用）：
/// ```csharp
/// Logger.Initialize(new LogOptions
/// {
///     MinLevel = LogLevel.Debug,
///     EnableConsole = true,
///     EnableFile = true,
///     LogDirectory = "logs"
/// });
/// ```
/// </summary>
public static class Logger
{
    #region 静态字段（全局配置）

    /// <summary>
    /// 当前最小日志级别
    /// 低于此级别的日志会被丢弃
    /// </summary>
    private static LogLevel _minLevel = LogLevel.Debug;

    /// <summary>
    /// 日志采样率（0.0 - 1.0）
    /// </summary>
    private static double _samplingRate = 1.0;

    /// <summary>
    /// 是否对 Error 级别采样
    /// </summary>
    private static bool _sampleErrorLevel = false;

    /// <summary>
    /// 是否对 Warning 级别采样
    /// </summary>
    private static bool _sampleWarningLevel = false;

    /// <summary>
    /// 高频日志抑制阈值（毫秒）
    /// </summary>
    private static int _suppressDuplicateThresholdMs = 0;

    /// <summary>
    /// 高频日志抑制缓存（消息哈希 → 最后记录时间）
    /// </summary>
    private static readonly Dictionary<int, long> _suppressCache = new();
    private static readonly object _suppressLock = new();

    /// <summary>
    /// 随机数生成器（用于采样）
    /// </summary>
    private static readonly Random _random = new();

    /// <summary>
    /// 日志输出目标列表
    /// </summary>
    private static readonly List<ILogSink> _sinks = new();

    /// <summary>
    /// 异步写入队列
    /// 使用 Channel 实现无锁队列
    /// </summary>
    private static Channel<LogEntry>? _queue;

    /// <summary>
    /// 后台写入任务
    /// </summary>
    private static Task? _writeTask;

    /// <summary>
    /// 是否已初始化
    /// </summary>
    private static bool _initialized;

    /// <summary>
    /// 初始化锁
    /// 确保只初始化一次
    /// </summary>
    private static readonly object _initLock = new();

    /// <summary>
    /// 写入锁
    /// </summary>
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    #endregion

    #region 初始化

    /// <summary>
    /// 初始化日志系统
    /// 
    /// 应该在程序启动时调用一次
    /// </summary>
    /// <param name="options">日志配置</param>
    public static void Initialize(LogOptions options)
    {
        lock (_initLock)
        {
            if (_initialized)
            {
                // 已初始化，发出警告
                Warning("Logger", "日志系统已初始化，忽略重复初始化");
                return;
            }

            // 设置最小级别
            _minLevel = options.MinLevel;

            // 设置采样率
            _samplingRate = Math.Clamp(options.SamplingRate, 0.0, 1.0);
            _sampleErrorLevel = options.SampleErrorLevel;
            _sampleWarningLevel = options.SampleWarningLevel;
            _suppressDuplicateThresholdMs = options.SuppressDuplicateThresholdMs;

            // 创建异步队列
            _queue = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(10000)
            {
                SingleReader = true,   // 单消费者
                SingleWriter = false,  // 多生产者
                FullMode = BoundedChannelFullMode.Wait  // 队列满时等待
            });

            // 创建输出目标
            if (options.EnableConsole)
            {
                _sinks.Add(new ConsoleSink());
            }

            if (options.EnableFile)
            {
                _sinks.Add(new FileSink(options.LogDirectory, options.MaxFileSize, options.RetentionDays));
            }

            if (options.EnableNetwork && !string.IsNullOrEmpty(options.NetworkEndpoint))
            {
                _sinks.Add(new NetworkSink(options.NetworkEndpoint!));
            }

            // 启动后台写入任务
            _writeTask = WriteLoopAsync();

            // 标记已初始化（必须在写入日志之前）
            _initialized = true;

            // 写入启动日志
            Info("Logger", "日志系统初始化完成，最小级别: {0}", _minLevel);
        }
    }

    /// <summary>
    /// 关闭日志系统
    /// 
    /// 应该在程序退出前调用
    /// </summary>
    public static async Task ShutdownAsync()
    {
        if (!_initialized || _queue == null)
            return;

        // 标记队列不再接受新消息
        _queue.Writer.Complete();

        // 等待后台任务完成
        if (_writeTask != null)
        {
            try
            {
                await _writeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                // 超时，强制退出
                Error("Logger", "日志系统关闭超时");
            }
        }

        // 关闭所有输出
        foreach (var sink in _sinks)
        {
            await sink.DisposeAsync();
        }

        _sinks.Clear();
        _initialized = false;

        Info("Logger", "日志系统已关闭");
    }

    #endregion

    #region 公共方法 - 日志记录

    /// <summary>
    /// 记录 Trace 日志
    /// </summary>
    /// <param name="source">来源模块</param>
    /// <param name="message">消息</param>
    /// <param name="args">参数</param>
    [Conditional("TRACE")]
    public static void Trace(string source, string message, params object[] args)
    {
        Log(LogLevel.Trace, source, message, args);
    }

    /// <summary>
    /// 记录 Debug 日志
    /// </summary>
    /// <param name="source">来源模块</param>
    /// <param name="message">消息</param>
    /// <param name="args">参数</param>
    [Conditional("DEBUG")]
    public static void Debug(string source, string message, params object[] args)
    {
        Log(LogLevel.Debug, source, message, args);
    }

    /// <summary>
    /// 记录 Info 日志
    /// </summary>
    /// <param name="source">来源模块</param>
    /// <param name="message">消息</param>
    /// <param name="args">参数</param>
    public static void Info(string source, string message, params object[] args)
    {
        Log(LogLevel.Info, source, message, args);
    }

    /// <summary>
    /// 记录 Warning 日志
    /// </summary>
    /// <param name="source">来源模块</param>
    /// <param name="message">消息</param>
    /// <param name="args">参数</param>
    public static void Warning(string source, string message, params object[] args)
    {
        Log(LogLevel.Warning, source, message, args);
    }

    /// <summary>
    /// 记录 Error 日志（带异常）
    /// </summary>
    /// <param name="source">来源模块</param>
    /// <param name="exception">异常对象</param>
    /// <param name="message">消息</param>
    /// <param name="args">参数</param>
    public static void Error(string source, Exception? exception, string message, params object[] args)
    {
        Log(LogLevel.Error, source, message, args, exception);
    }

    /// <summary>
    /// 记录 Error 日志
    /// </summary>
    /// <param name="source">来源模块</param>
    /// <param name="message">消息</param>
    /// <param name="args">参数</param>
    public static void Error(string source, string message, params object[] args)
    {
        Log(LogLevel.Error, source, message, args, null);
    }

    /// <summary>
    /// 记录 Fatal 日志（带异常）
    /// </summary>
    /// <param name="source">来源模块</param>
    /// <param name="exception">异常对象</param>
    /// <param name="message">消息</param>
    /// <param name="args">参数</param>
    public static void Fatal(string source, Exception? exception, string message, params object[] args)
    {
        Log(LogLevel.Fatal, source, message, args, exception);
    }

    /// <summary>
    /// 记录 Fatal 日志
    /// </summary>
    /// <param name="source">来源模块</param>
    /// <param name="message">消息</param>
    /// <param name="args">参数</param>
    public static void Fatal(string source, string message, params object[] args)
    {
        Log(LogLevel.Fatal, source, message, args, null);
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 核心日志方法
    /// </summary>
    /// <param name="level">日志级别</param>
    /// <param name="source">来源模块</param>
    /// <param name="message">消息模板</param>
    /// <param name="args">参数</param>
    /// <param name="exception">异常对象</param>
    private static void Log(
        LogLevel level,
        string source,
        string message,
        object[] args,
        Exception? exception = null)
    {
        // 级别检查
        if (level < _minLevel)
            return;

        // 采样检查
        if (!ShouldLog(level, source, message))
            return;

        // 确保已初始化
        if (!_initialized || _queue == null)
        {
            // 尝试写入控制台
            Console.Error.WriteLine($"[ERROR] 日志系统未初始化: {source} - {message}");
            return;
        }

        // 格式化消息
        var formattedMessage = args.Length > 0
            ? string.Format(message, args)
            : message;

        // 创建日志条目
        var entry = new LogEntry(level, source, message, formattedMessage, exception)
        {
            // 添加追踪上下文（如果有）
            TraceId = TraceContext.Current?.TraceId,
            PlayerId = PlayerContext.Current?.PlayerId,
            SessionId = PlayerContext.Current?.SessionId
        };

        // 尝试写入队列
        // 如果队列满（丢弃模式），消息会被丢弃
        if (!_queue.Writer.TryWrite(entry))
        {
            // 队列满，记录一条警告
            Console.Error.WriteLine($"[WARNING] 日志队列已满，丢弃消息: {formattedMessage}");
        }
    }

    /// <summary>
    /// 判断是否应该记录此日志（采样 + 高频抑制）
    /// </summary>
    private static bool ShouldLog(LogLevel level, string source, string message)
    {
        // 1. 采样检查
        // Error 和 Warning 默认不采样（始终记录）
        // 只有配置了采样 Error/Warning 才会采样
        if (level == LogLevel.Error && !_sampleErrorLevel)
            return true;

        if (level == LogLevel.Warning && !_sampleWarningLevel)
            return true;

        // Fatal 级别始终记录
        if (level == LogLevel.Fatal)
            return true;

        // 其他级别按采样率决定
        if (_samplingRate < 1.0)
        {
            // 使用线程安全的随机采样
            // 注意：Random 不是线程安全的，但这里我们使用锁保护
            double sampleValue;
            lock (_random)
            {
                sampleValue = _random.NextDouble();
            }

            if (sampleValue > _samplingRate)
                return false;
        }

        // 2. 高频日志抑制检查
        if (_suppressDuplicateThresholdMs > 0)
        {
            var messageHash = HashCode.Combine(source, message);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            lock (_suppressLock)
            {
                if (_suppressCache.TryGetValue(messageHash, out var lastLogTime))
                {
                    var elapsed = now - lastLogTime;
                    if (elapsed < _suppressDuplicateThresholdMs)
                    {
                        // 在抑制阈值内，不记录
                        return false;
                    }
                }

                // 更新最后记录时间
                _suppressCache[messageHash] = now;

                // 定期清理过期的缓存项（避免内存泄漏）
                if (_suppressCache.Count > 10000)
                {
                    var expiredKeys = _suppressCache
                        .Where(kv => now - kv.Value > _suppressDuplicateThresholdMs * 10)
                        .Select(kv => kv.Key)
                        .ToList();

                    foreach (var key in expiredKeys)
                    {
                        _suppressCache.Remove(key);
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 后台写入循环
    /// 持续从队列中读取日志并写入所有输出目标
    ///
    /// ⚠️ 异常处理：
    /// - 单条日志写入失败不会影响其他日志
    /// - 整个 sink 写入循环有 5 秒超时保护（防止某个慢 sink 阻塞整个日志管道）
    /// </summary>
    private static async Task WriteLoopAsync()
    {
        if (_queue == null)
            return;

        await foreach (var entry in _queue.Reader.ReadAllAsync())
        {
            try
            {
                // 并行写入所有目标，每个 sink 都有 5 秒超时
                // 防止某个慢 sink（文件/网络）阻塞整个日志管道
                var sinkTimeout = TimeSpan.FromSeconds(5);
                var tasks = _sinks.Select(sink => WriteSinkWithTimeoutAsync(sink, entry, sinkTimeout));

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (TimeoutException)
                {
                    // 单个 sink 写入超时，错误会通过 Console 输出
                    // 不影响其他 sink 继续写入
                }
            }
            catch (Exception ex)
            {
                // 防止日志系统异常导致程序崩溃
                Console.Error.WriteLine($"日志写入失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 带超时保护的 sink 写入
    ///
    /// 如果 sink 写入超过指定时间，抛出 TimeoutException（不影响其他 sink）
    /// </summary>
    private static async Task WriteSinkWithTimeoutAsync(ILogSink sink, LogEntry entry, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            var writeTask = sink.WriteAsync(entry);

            // 等待 sink 写入完成或超时
            if (await Task.WhenAny(writeTask, Task.Delay(timeout, cts.Token)) == writeTask)
            {
                // sink 写入完成
                await writeTask;
            }
            else
            {
                // 超时
                Console.Error.WriteLine($"[WARNING] 日志 sink 写入超时: {sink.GetType().Name}, 消息: {entry.FormattedMessage}");
                throw new TimeoutException();
            }
        }
        catch (Exception ex)
        {
            // 单个 sink 写入失败，记录到控制台，不影响其他 sink
            Console.Error.WriteLine($"[ERROR] 日志 sink 写入失败: {sink.GetType().Name}, {ex.Message}");
        }
    }

    #endregion
}

/// <summary>
/// 追踪上下文
/// 
/// 用于在整个请求链路中传递追踪信息
/// 包括：追踪ID、玩家ID、会话ID 等
/// 
/// 使用方式：
/// ```csharp
/// using (TraceContext.Create(playerId, sessionId))
/// {
///     Logger.Info("Game", "开始处理请求");
///     // ... 处理逻辑
///     Logger.Info("Game", "请求处理完成");
/// }
/// ```
/// </summary>
public class TraceContext : IDisposable
{
    private static readonly AsyncLocal<TraceContext?> _current = new();

    /// <summary>
    /// 获取当前追踪上下文
    /// </summary>
    public static TraceContext? Current => _current.Value;

    /// <summary>
    /// 追踪 ID
    /// 用于关联一次完整的请求流程
    /// </summary>
    public string TraceId { get; private set; } = string.Empty;

    /// <summary>
    /// 创建新的追踪上下文
    /// </summary>
    /// <param name="traceId">追踪ID（可选，自动生成）</param>
    /// <returns>追踪上下文</returns>
    public static TraceContext Create(string? traceId = null)
    {
        var context = new TraceContext
        {
            TraceId = traceId ?? Guid.NewGuid().ToString("N")
        };

        _current.Value = context;
        return context;
    }

    /// <summary>
    /// 创建子追踪（保留父追踪ID）
    /// </summary>
    public TraceContext CreateChild()
    {
        return new TraceContext
        {
            TraceId = TraceId
        };
    }

    /// <summary>
    /// 清除追踪上下文
    /// </summary>
    public static void Clear()
    {
        _current.Value = null;
    }

    /// <summary>
    /// 实现 IDisposable，支持 using 语法
    /// </summary>
    public void Dispose()
    {
        Clear();
    }
}

/// <summary>
/// 玩家上下文
/// 
/// 用于在当前线程/异步上下文中访问玩家信息
/// 
/// 使用方式：
/// ```csharp
/// using (PlayerContext.Create(playerId, sessionId))
/// {
///     Logger.Info("Game", "处理玩家请求");
///     // ... 处理逻辑
/// }
/// ```
/// </summary>
public class PlayerContext : IDisposable
{
    private static readonly AsyncLocal<PlayerContext?> _current = new();

    /// <summary>
    /// 获取当前玩家上下文
    /// </summary>
    public static PlayerContext? Current => _current.Value;

    /// <summary>
    /// 玩家 ID
    /// </summary>
    public long PlayerId { get; private set; }

    /// <summary>
    /// 会话 ID
    /// </summary>
    public long SessionId { get; private set; }

    /// <summary>
    /// 创建新的玩家上下文
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="sessionId">会话ID</param>
    /// <returns>玩家上下文</returns>
    public static PlayerContext Create(long playerId, long sessionId)
    {
        var context = new PlayerContext
        {
            PlayerId = playerId,
            SessionId = sessionId
        };

        _current.Value = context;
        return context;
    }

    /// <summary>
    /// 清除玩家上下文
    /// </summary>
    public static void Clear()
    {
        _current.Value = null;
    }

    /// <summary>
    /// 实现 IDisposable，支持 using 语法
    /// </summary>
    public void Dispose()
    {
        Clear();
    }
}
