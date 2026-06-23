// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

namespace MMORPG.Framework.Logging;

/// <summary>
/// 日志输出接口
/// 
/// 定义日志如何输出到各种目标
/// 可以实现此接口来创建自定义输出目标
/// 
/// 内置实现：
/// - ConsoleSink：输出到控制台（带颜色）
/// - FileSink：输出到文件（支持滚动和压缩）
/// - NetworkSink：输出到远程日志服务器
/// 
/// 示例：创建一个自定义输出目标
/// ```csharp
/// public class MySink : ILogSink
/// {
///     public async ValueTask WriteAsync(LogEntry entry)
///     {
///         // 自定义输出逻辑
///         await MyLoggingService.SendAsync(entry);
///     }
///     
///     public ValueTask DisposeAsync() => default;
/// }
/// ```
/// </summary>
public interface ILogSink : IAsyncDisposable
{
    /// <summary>
    /// 写入日志条目
    /// </summary>
    /// <param name="entry">日志条目</param>
    Task WriteAsync(LogEntry entry);
}
