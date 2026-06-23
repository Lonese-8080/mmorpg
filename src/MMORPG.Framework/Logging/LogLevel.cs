// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

namespace MMORPG.Framework.Logging;

/// <summary>
/// 日志级别枚举
/// 
/// 用于区分日志的重要程度，从高到低：
/// - Fatal：致命错误，服务器必须停止
/// - Error：错误，功能不可用但服务仍可运行
/// - Warning：警告，可能有问题但功能正常
/// - Info：一般信息，正常运行时的关键事件
/// - Debug：调试信息，开发期间使用
/// - Trace：最详细信息，通常只在排查问题时使用
/// 
/// 使用建议：
/// - 生产环境：INFO 及以上
/// - 开发环境：DEBUG 及以上
/// - 排查问题：TRACE
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// 最详细 - 逐帧数据、原始网络包等
    /// 通常在排查问题时才开启
    /// </summary>
    Trace = 0,

    /// <summary>
    /// 调试信息 - 方法入口/出口、变量值等
    /// 生产环境通常关闭
    /// </summary>
    Debug = 1,

    /// <summary>
    /// 一般信息 - 服务器启动、玩家登录等
    /// 生产环境始终开启
    /// </summary>
    Info = 2,

    /// <summary>
    /// 警告 - 配置缺失、资源接近上限等
    /// 需要关注但不影响功能
    /// </summary>
    Warning = 3,

    /// <summary>
    /// 错误 - 数据库超时、消息处理失败等
    /// 功能不可用但服务仍可运行
    /// </summary>
    Error = 4,

    /// <summary>
    /// 致命错误 - 内存耗尽、数据库完全不可用等
    /// 服务器必须停止
    /// </summary>
    Fatal = 5
}
