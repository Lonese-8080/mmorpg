// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

namespace MMORPG.Framework.Logging;

/// <summary>
/// 控制台日志输出
/// 
/// 特性：
/// - 带颜色的日志输出
/// - 不同级别使用不同颜色
/// - 高性能，使用锁保护并发写入
/// 
/// 颜色方案：
/// - Trace：暗灰色
/// - Debug：灰色
/// - Info：白色
/// - Warning：黄色
/// - Error：红色
/// - Fatal：暗红色
/// </summary>
public class ConsoleSink : ILogSink
{
    #region 私有字段

    /// <summary>
    /// 写入锁
    /// 控制台输出需要同步
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 日志级别对应的颜色
    /// </summary>
    private static readonly Dictionary<LogLevel, ConsoleColor> Colors = new()
    {
        [LogLevel.Trace] = ConsoleColor.DarkGray,
        [LogLevel.Debug] = ConsoleColor.Gray,
        [LogLevel.Info] = ConsoleColor.White,
        [LogLevel.Warning] = ConsoleColor.Yellow,
        [LogLevel.Error] = ConsoleColor.Red,
        [LogLevel.Fatal] = ConsoleColor.DarkRed
    };

    #endregion

    #region ILogSink 实现

    /// <summary>
    /// 写入日志到控制台
    /// </summary>
    /// <param name="entry">日志条目</param>
    public Task WriteAsync(LogEntry entry)
    {
        lock (_lock)
        {
            // 设置前景色
            Console.ForegroundColor = Colors.GetValueOrDefault(entry.Level, ConsoleColor.White);

            try
            {
                // 构建日志行
                var line = BuildLogLine(entry);

                // 输出到标准输出
                Console.WriteLine(line);

                // 如果有异常，输出堆栈
                if (entry.Exception != null)
                {
                    Console.WriteLine(entry.Exception.ToString());
                }
            }
            finally
            {
                // 恢复默认颜色
                Console.ResetColor();
            }
        }

        return Task.CompletedTask;
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 构建日志行
    /// </summary>
    /// <param name="entry">日志条目</param>
    /// <returns>格式化的日志行</returns>
    private static string BuildLogLine(LogEntry entry)
    {
        // 格式：[时间] [级别] [模块] 消息
        var parts = new List<string>
        {
            $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}]",
            $"[{entry.Level.ToString().ToUpper().PadRight(5)}]",
            $"[{entry.Source.PadRight(15)}]",
            entry.FormattedMessage
        };

        // 添加属性（如果有）
        if (entry.Properties?.Count > 0)
        {
            var props = string.Join(", ", entry.Properties.Select(p => $"{p.Key}={p.Value}"));
            parts.Add($"[{props}]");
        }

        // 添加追踪ID（如果有）
        if (!string.IsNullOrEmpty(entry.TraceId))
        {
            parts.Add($"[TraceId={entry.TraceId}]");
        }

        // 添加玩家ID（如果有）
        if (entry.PlayerId.HasValue)
        {
            parts.Add($"[PlayerId={entry.PlayerId}]");
        }

        // 添加会话ID（如果有）
        if (entry.SessionId.HasValue)
        {
            parts.Add($"[SessionId={entry.SessionId}]");
        }

        return string.Join(" ", parts);
    }

    #endregion

    #region IDisposable 实现

    /// <summary>
    /// 释放资源
    /// </summary>
    public ValueTask DisposeAsync()
    {
        // ConsoleSink 不需要释放资源
        return default;
    }

    #endregion
}
