// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace MMORPG.Framework.Logging;

/// <summary>
/// 日志条目
/// 
/// 表示一条完整的日志记录，包含：
/// - 时间戳
/// - 日志级别
/// - 来源模块
/// - 消息内容
/// - 异常信息（如果有）
/// - 自定义属性
/// - 追踪上下文
/// 
/// 这个类会被序列化为字符串后输出到各种目标（控制台、文件、网络等）
/// </summary>
public class LogEntry
{
    #region 必需字段

    /// <summary>
    /// 时间戳（UTC时间）
    /// ISO 8601 格式，便于日志分析
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// 日志级别
    /// </summary>
    public LogLevel Level { get; init; }

    /// <summary>
    /// 消息模板
    /// 可以包含占位符，如："{0} 登录成功"
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// 格式化后的消息
    /// 如果消息包含参数，会先格式化
    /// </summary>
    public string FormattedMessage { get; set; }

    #endregion

    #region 可选字段

    /// <summary>
    /// 异常信息（如果有）
    /// 用于记录错误堆栈
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// 日志来源模块
    /// 便于分类和过滤，如：Network、Game、ECS
    /// </summary>
    public string Source { get; init; }

    /// <summary>
    /// 追踪ID
    /// 用于关联一次完整的请求链路
    /// </summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// 玩家ID（如果适用）
    /// 用于关联特定玩家的操作
    /// </summary>
    public long? PlayerId { get; init; }

    /// <summary>
    /// 会话ID
    /// 用于关联特定的连接会话
    /// </summary>
    public long? SessionId { get; init; }

    /// <summary>
    /// 消息类型（如果适用）
    /// 用于关联特定的网络消息
    /// </summary>
    public int? MessageType { get; init; }

    /// <summary>
    /// 自定义属性字典
    /// 用于存储额外的上下文信息
    /// </summary>
    public Dictionary<string, object?>? Properties { get; init; }

    #endregion

    #region 便捷构造函数

    /// <summary>
    /// 创建日志条目
    /// </summary>
    /// <param name="level">日志级别</param>
    /// <param name="source">来源模块</param>
    /// <param name="message">消息内容</param>
    /// <param name="formattedMessage">格式化后的消息（可选）</param>
    /// <param name="exception">异常信息（可选）</param>
    public LogEntry(
        LogLevel level,
        string source,
        string message,
        string? formattedMessage = null,
        Exception? exception = null)
    {
        Timestamp = DateTimeOffset.UtcNow;
        Level = level;
        Source = source;
        Message = message;
        FormattedMessage = formattedMessage ?? message;
        Exception = exception;
    }

    #endregion

    #region 序列化方法

    /// <summary>
    /// 序列化为 JSON 格式（用于结构化日志）
    /// </summary>
    /// <returns>JSON 字符串</returns>
    public string ToJson()
    {
        // 手动构建 JSON，避免异常序列化问题
        var exceptionStr = Exception != null
            ? $"\"exception\": \"{EscapeJson(Exception.ToString())}\""
            : "";

        var traceIdStr = !string.IsNullOrEmpty(TraceId)
            ? $"\"traceId\": \"{EscapeJson(TraceId)}\""
            : "";

        var playerIdStr = PlayerId.HasValue
            ? $"\"playerId\": {PlayerId.Value}"
            : "";

        var sessionIdStr = SessionId.HasValue
            ? $"\"sessionId\": {SessionId.Value}"
            : "";

        var parts = new List<string>
        {
            $"\"timestamp\": \"{Timestamp:O}\"",
            $"\"level\": \"{Level}\"",
            $"\"source\": \"{EscapeJson(Source)}\"",
            $"\"message\": \"{EscapeJson(FormattedMessage)}\"",
            $"\"messageTemplate\": \"{EscapeJson(Message)}\""
        };

        if (!string.IsNullOrEmpty(exceptionStr))
            parts.Add(exceptionStr);
        if (!string.IsNullOrEmpty(traceIdStr))
            parts.Add(traceIdStr);
        if (!string.IsNullOrEmpty(playerIdStr))
            parts.Add(playerIdStr);
        if (!string.IsNullOrEmpty(sessionIdStr))
            parts.Add(sessionIdStr);

        return "{" + string.Join(", ", parts) + "}";
    }

    /// <summary>
    /// 转义 JSON 字符串
    /// </summary>
    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    /// <summary>
    /// 转换为控制台友好格式
    /// </summary>
    /// <returns>格式化的字符串</returns>
    public string ToConsoleString()
    {
        var parts = new List<string>
        {
            $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}]",
            $"[{Level.ToString().ToUpper().PadRight(5)}]",
            $"[{Source.PadRight(15)}]"
        };

        parts.Add(Message);

        if (Properties?.Count > 0)
        {
            var props = string.Join(", ", Properties.Select(p => $"{p.Key}={p.Value}"));
            parts.Add($"[{props}]");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// 获取堆栈跟踪字符串
    /// </summary>
    /// <returns>异常和堆栈信息</returns>
    public string GetExceptionString()
    {
        if (Exception == null)
            return string.Empty;

        return Exception.ToString();
    }

    #endregion
}
