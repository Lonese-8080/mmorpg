// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

namespace MMORPG.Framework.Logging;

/// <summary>
/// 日志配置选项
/// 
/// 用于初始化日志系统
/// </summary>
public class LogOptions
{
    /// <summary>
    /// 最小日志级别
    /// 低于此级别的日志会被丢弃
    /// 建议：
    /// - 开发环境：Debug
    /// - 生产环境：Info
    /// </summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// 是否启用控制台输出
    /// </summary>
    public bool EnableConsole { get; set; } = true;

    /// <summary>
    /// 是否启用文件输出
    /// </summary>
    public bool EnableFile { get; set; } = true;

    /// <summary>
    /// 日志文件目录
    /// </summary>
    public string LogDirectory { get; set; } = "logs";

    /// <summary>
    /// 单个日志文件最大大小（字节）
    /// 超过此大小会创建新文件
    /// 默认：100MB
    /// </summary>
    public long MaxFileSize { get; set; } = 100 * 1024 * 1024;

    /// <summary>
    /// 日志文件保留天数
    /// 超过此天数的文件会被自动删除
    /// 默认：30天
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// 是否启用网络输出
    /// </summary>
    public bool EnableNetwork { get; set; } = false;

    /// <summary>
    /// 网络端点（用于远程日志服务器）
    /// 格式：http://ip:port/log
    /// </summary>
    public string? NetworkEndpoint { get; set; }

    /// <summary>
    /// 日志采样率（0.0 - 1.0）
    /// 
    /// 用于高并发场景下控制日志量：
    /// - 1.0 = 100% 记录所有日志
    /// - 0.1 = 10% 只记录 10% 的日志
    /// - 0.0 = 0% 不记录任何日志（不建议）
    /// 
    /// 建议：
    /// - 开发环境：1.0（全量记录）
    /// - 生产环境：0.1 - 0.5（采样记录，减少 I/O 压力）
    /// 
    /// 注意：
    /// - Error 和 Warning 级别不受采样率影响，始终记录
    /// - 仅对 Info 和 Debug 级别生效
    /// </summary>
    public double SamplingRate { get; set; } = 1.0;

    /// <summary>
    /// 是否对 Error 级别日志也进行采样
    /// 
    /// 默认：false（Error 级别始终记录，不采样）
    /// 
    /// 如果设置为 true，Error 级别也会按 SamplingRate 进行采样
    /// </summary>
    public bool SampleErrorLevel { get; set; } = false;

    /// <summary>
    /// 是否对 Warning 级别日志也进行采样
    /// 
    /// 默认：false（Warning 级别始终记录，不采样）
    /// 
    /// 如果设置为 true，Warning 级别也会按 SamplingRate 进行采样
    /// </summary>
    public bool SampleWarningLevel { get; set; } = false;

    /// <summary>
    /// 高频日志抑制阈值（同一消息在阈值时间内只记录一次）
    /// 
    /// 单位：毫秒
    /// 默认：0（不抑制）
    /// 
    /// 设置为 1000 表示同一消息在 1 秒内只记录第一次
    /// 用于抑制高频重复日志（如心跳日志）
    /// </summary>
    public int SuppressDuplicateThresholdMs { get; set; } = 0;
}
