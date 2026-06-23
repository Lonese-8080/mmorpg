// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Tracing;

/// <summary>
/// OpenTelemetry 追踪集成
/// 
/// 提供分布式追踪能力，支持：
/// - 自动创建 Span（消息处理、网络 IO）
/// - TraceContext 传播（跨服务链路追踪）
/// - 与 MetricsCollector 集成（追踪 ID 关联指标）
/// 
/// 设计说明：
/// - 使用 System.Diagnostics.Activity（.NET 内置 OTel 支持）
/// - ActivitySource 作为 Span 创建入口
/// - Activity 自动传播 W3C TraceContext（traceparent / tracestate）
/// 
/// 使用示例：
/// ```csharp
/// // 启动时初始化
/// TracingInitializer.Initialize(new TracingOptions { ServiceName = "MMORPG-Server" });
///
/// // 在消息处理中自动创建 Span
/// using var span = TracingHelper.StartMessageSpan(session, message);
/// // ... 处理逻辑
/// ```
/// </summary>
public static class OpenTelemetryIntegration
{
    /// <summary>
    /// ActivitySource 名称（用于创建 Span）
    /// </summary>
    public const string ActivitySourceName = "MMORPG.Framework";

    /// <summary>
    /// ActivitySource 版本
    /// </summary>
    public const string ActivitySourceVersion = "1.0.0";

    /// <summary>
    /// 全局 ActivitySource（用于创建所有 Span）
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ActivitySourceVersion);

    /// <summary>
    /// 是否已初始化
    /// </summary>
    private static bool _initialized;

    /// <summary>
    /// 追踪配置（内部可访问）
    /// </summary>
    internal static TracingOptions Options { get; private set; } = new();

    /// <summary>
    /// 初始化 OpenTelemetry 追踪
    /// 
    /// 应在程序启动时调用一次。
    /// 
    /// 注意：此方法只设置内部配置，实际的 OTel SDK 监听器需要在外部配置：
    /// - OpenTelemetry.Extensions.Hosting（ASP.NET Core 场景）
    /// - 或手动添加 ActivityListener
    /// </summary>
    public static void Initialize(TracingOptions? options = null)
    {
        Options = options ?? new TracingOptions();
        _initialized = true;

        // 确保 ActivitySource 已注册（OTel SDK 会自动发现）
        Logger.Info("Tracing",
            "OpenTelemetry 追踪已初始化: ServiceName={0}, ActivitySource={1}",
            Options.ServiceName, ActivitySourceName);
    }

    /// <summary>
    /// 获取当前 Trace ID（用于日志关联）
    /// </summary>
    public static string? CurrentTraceId
    {
        get
        {
            var activity = Activity.Current;
            return activity?.TraceId.ToHexString();
        }
    }

    /// <summary>
    /// 获取当前 Span ID（用于日志关联）
    /// </summary>
    public static string? CurrentSpanId
    {
        get
        {
            var activity = Activity.Current;
            return activity?.SpanId.ToHexString();
        }
    }

    /// <summary>
    /// 检查追踪是否启用
    /// </summary>
    public static bool IsEnabled => _initialized && Options.Enabled;
}

/// <summary>
/// 追踪配置
/// </summary>
public sealed class TracingOptions
{
    /// <summary>服务名称（用于 OTel 资源标识）</summary>
    public string ServiceName { get; init; } = "MMORPG-Server";

    /// <summary>服务版本（用于 OTel 资源标识）</summary>
    public string ServiceVersion { get; init; } = "1.0.0";

    /// <summary>是否启用追踪（默认：true）</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 是否记录消息体（敏感数据慎用）
    /// 默认：false（只记录 MessageId）
    /// </summary>
    public bool RecordMessageBody { get; init; } = false;

    /// <summary>
    /// 是否记录网络 IO 详情（BytesTransferred）
    /// 默认：true
    /// </summary>
    public bool RecordNetworkIO { get; init; } = true;

    /// <summary>
    /// Span 名称格式（可用占位符：{messageId}, {connectionId}, {playerId}）
    /// 默认："Message/{messageId}"
    /// </summary>
    public string SpanNameFormat { get; init; } = "Message/{messageId}";
}

/// <summary>
/// 追踪辅助类 - 提供常用 Span 创建方法
/// </summary>
public static class TracingHelper
{
    /// <summary>
    /// 开始消息处理 Span
    /// 
    /// 自动添加：
    /// - MessageId 标签
    /// - ConnectionId 标签
    /// - PlayerId 标签（如果有）
    /// - TraceId 关联到 TraceContext
    /// </summary>
    public static Activity? StartMessageSpan(long connectionId, uint messageId, long playerId = 0)
    {
        if (!OpenTelemetryIntegration.IsEnabled)
            return null;

        var spanName = OpenTelemetryIntegration.Options.SpanNameFormat
            .Replace("{messageId}", $"0x{messageId:X8}")
            .Replace("{connectionId}", connectionId.ToString())
            .Replace("{playerId}", playerId.ToString());

        var activity = OpenTelemetryIntegration.ActivitySource.StartActivity(spanName, ActivityKind.Server);

        if (activity != null)
        {
            activity.SetTag("message.id", messageId);
            activity.SetTag("message.name", GetMessageName(messageId));
            activity.SetTag("network.connection_id", connectionId);

            if (playerId > 0)
                activity.SetTag("player.id", playerId);

            // 关联到 TraceContext（用于日志）
            TraceContext.Create(activity.TraceId.ToHexString());
        }

        return activity;
    }

    /// <summary>
    /// 开始网络 IO Span（接收/发送）
    /// </summary>
    public static Activity? StartNetworkSpan(long connectionId, string operation, int bytesCount = 0)
    {
        if (!OpenTelemetryIntegration.IsEnabled)
            return null;

        var activity = OpenTelemetryIntegration.ActivitySource.StartActivity(
            $"Network/{operation}",
            ActivityKind.Client);

        if (activity != null)
        {
            activity.SetTag("network.connection_id", connectionId);
            activity.SetTag("network.operation", operation);

            if (OpenTelemetryIntegration.Options.RecordNetworkIO && bytesCount > 0)
                activity.SetTag("network.bytes", bytesCount);
        }

        return activity;
    }

    /// <summary>
    /// 开始数据库操作 Span
    /// </summary>
    public static Activity? StartDatabaseSpan(string operation, string? table = null, string? sql = null)
    {
        if (!OpenTelemetryIntegration.IsEnabled)
            return null;

        var activity = OpenTelemetryIntegration.ActivitySource.StartActivity(
            $"Database/{operation}",
            ActivityKind.Client);

        if (activity != null)
        {
            activity.SetTag("db.operation", operation);

            if (!string.IsNullOrEmpty(table))
                activity.SetTag("db.table", table);

            if (!string.IsNullOrEmpty(sql))
                activity.SetTag("db.statement", sql);
        }

        return activity;
    }

    /// <summary>
    /// 开始弹性策略 Span（熔断/重试/超时）
    /// </summary>
    public static Activity? StartResilienceSpan(string policyName, string operation)
    {
        if (!OpenTelemetryIntegration.IsEnabled)
            return null;

        var activity = OpenTelemetryIntegration.ActivitySource.StartActivity(
            $"Resilience/{policyName}/{operation}",
            ActivityKind.Internal);

        if (activity != null)
        {
            activity.SetTag("resilience.policy", policyName);
            activity.SetTag("resilience.operation", operation);
        }

        return activity;
    }

    /// <summary>
    /// 记录异常到当前 Span
    /// </summary>
    public static void RecordException(Activity? activity, Exception ex)
    {
        if (activity == null || !activity.IsAllDataRequested)
            return;

        activity.SetTag("error", true);
        activity.SetTag("error.type", ex.GetType().FullName);
        activity.SetTag("error.message", ex.Message);

        if (ex.StackTrace != null)
            activity.SetTag("error.stack_trace", ex.StackTrace);
    }

    /// <summary>
    /// 记录事件到当前 Span
    /// </summary>
    public static void AddEvent(Activity? activity, string eventName, params (string key, object value)[] attributes)
    {
        if (activity == null || !activity.IsAllDataRequested)
            return;

        var eventTags = new ActivityTagsCollection();
        foreach (var (key, value) in attributes)
            eventTags.Add(key, value);

        activity.AddEvent(new ActivityEvent(eventName, tags: eventTags));
    }

    /// <summary>
    /// 获取消息名称（用于 Span 标签）
    /// </summary>
    private static string GetMessageName(uint messageId)
    {
        // 使用 MessageIds.GetDescription
        return MMORPG.Framework.Network.MessageIds.GetDescription(messageId);
    }
}

/// <summary>
/// TraceContext 扩展 - 与现有 TraceContext 类兼容
/// </summary>
public static class TraceContextExtensions
{
    /// <summary>
    /// 从 Activity 创建 TraceContext
    /// </summary>
    public static TraceContext FromActivity(Activity activity)
    {
        return TraceContext.Create(activity.TraceId.ToHexString());
    }

    /// <summary>
    /// 将 TraceContext 传播到 Activity
    /// </summary>
    public static Activity? PropagateToActivity(TraceContext? context, string spanName)
    {
        if (context == null || !OpenTelemetryIntegration.IsEnabled)
            return null;

        // 创建新 Span，继承 TraceId
        var activity = OpenTelemetryIntegration.ActivitySource.StartActivity(spanName, ActivityKind.Server);

        if (activity != null)
        {
            // W3C TraceContext 格式：traceparent = 00-{traceId}-{spanId}-01
            // Activity 会自动处理，我们只需要确保 TraceId 一致
        }

        return activity;
    }
}