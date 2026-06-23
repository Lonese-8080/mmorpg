// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Text;

namespace MMORPG.Framework.Observability;

/// <summary>
/// 完整的健康检查报告
/// 
/// 聚合所有检查项的结果，并提供文本与 JSON 渲染能力，
/// 便于运维系统、监控平台或控制台查看。
/// </summary>
public class HealthCheckStatus
{
    /// <summary>
    /// 聚合状态（取所有条目的最坏状态）
    /// </summary>
    public HealthCheckResult OverallStatus { get; }

    /// <summary>
    /// 所有检查项列表
    /// </summary>
    public IReadOnlyList<HealthCheckEntry> Entries { get; }

    /// <summary>
    /// 检查时间戳（UTC）
    /// </summary>
    public DateTime TimestampUtc { get; }

    /// <summary>
    /// 构造健康检查报告
    /// </summary>
    /// <param name="overall">聚合状态</param>
    /// <param name="entries">所有检查项</param>
    public HealthCheckStatus(HealthCheckResult overall, IEnumerable<HealthCheckEntry> entries)
    {
        OverallStatus = overall;
        Entries = entries.ToList().AsReadOnly();
        TimestampUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// 渲染为纯文本格式（适合控制台查看）
    /// </summary>
    public string RenderText()
    {
        var sb = new StringBuilder();
        sb.Append($"[{TimestampUtc:yyyy-MM-dd HH:mm:ss UTC}] ");
        sb.AppendLine($"Overall: {OverallStatus}");
        foreach (var entry in Entries)
        {
            sb.AppendLine($"  - {entry.Name}: {entry.Result} - {entry.Description}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 渲染为 JSON 格式（适合运维系统/HTTP 接口）
    /// 
    /// 输出形如：
    /// {
    ///   "timestamp":"2025-01-01T12:00:00Z",
    ///   "overall_status":"Healthy",
    ///   "entries":[
    ///     {"name":"memory_usage","status":"Healthy","description":"当前 10MB"},
    ///     {"name":"tcp_listener","status":"Healthy","description":"监听正常"}
    ///   ]
    /// }
    /// </summary>
    public string RenderJson()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"timestamp\":\"{TimestampUtc:yyyy-MM-ddTHH:mm:ssZ}\",");
        sb.Append($"\"overall_status\":\"{OverallStatus}\",");
        sb.Append("\"entries\":[");
        bool first = true;
        foreach (var entry in Entries)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"name\":\"").Append(entry.Name).Append("\",");
            sb.Append("\"status\":\"").Append(entry.Result.ToString()).Append("\",");
            sb.Append("\"description\":\"").Append(entry.Description.Replace("\"", string.Empty)).Append("\"}");
        }
        sb.Append("]}");
        return sb.ToString();
    }
}
