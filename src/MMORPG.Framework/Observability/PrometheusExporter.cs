// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Observability;

/// <summary>
/// Prometheus 指标导出器
/// 
/// 将 MetricsCollector 中的指标导出为 Prometheus 文本格式
/// 支持 HTTP 端点暴露，便于 Prometheus 服务器抓取
/// 
/// 使用示例：
/// <code>
/// var exporter = new PrometheusExporter(9091);
/// exporter.Start();
/// 
/// // Prometheus 可以从 http://localhost:9091/metrics 抓取指标
/// </code>
/// </summary>
public class PrometheusExporter
{
    private HttpListener? _listener;
    private readonly int _port;
    private readonly string _path;
    private bool _isRunning;
    private readonly string _prefix = "mmorpg_";

    /// <summary>
    /// 构造 Prometheus 导出器
    /// </summary>
    /// <param name="port">HTTP 监听端口，默认 9091</param>
    /// <param name="path">指标路径，默认 /metrics</param>
    /// <param name="prefix">指标名称前缀，默认 mmorpg_</param>
    public PrometheusExporter(int port = 9091, string path = "/metrics", string prefix = "mmorpg_")
    {
        _port = port;
        _path = path;
        _prefix = prefix;
    }

    /// <summary>
    /// 启动 HTTP 服务
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}{_path}/");
            _listener.Start();

            _isRunning = true;

            Logger.Info("Metrics", "Prometheus 导出器已启动: http://localhost:{0}{1}/", _port, _path);

            // 异步处理请求
            Task.Run(ProcessRequests);
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "启动 Prometheus 导出器失败");
        }
    }

    /// <summary>
    /// 停止 HTTP 服务
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        try
        {
            _listener?.Stop();
            _listener?.Close();
            _listener = null;

            _isRunning = false;

            Logger.Info("Metrics", "Prometheus 导出器已停止");
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "停止 Prometheus 导出器异常");
        }
    }

    /// <summary>
    /// 处理 HTTP 请求
    /// </summary>
    private async Task ProcessRequests()
    {
        while (_isRunning && _listener != null)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                var response = context.Response;

                // 生成 Prometheus 格式指标
                var metricsText = GeneratePrometheusMetrics();

                // 设置响应头
                response.StatusCode = 200;
                response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
                response.ContentEncoding = Encoding.UTF8;

                // 写入响应
                var buffer = Encoding.UTF8.GetBytes(metricsText);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (HttpListenerException)
            {
                // 监听器停止时会抛出异常，忽略
                break;
            }
            catch (Exception ex)
            {
                Logger.Error("Metrics", ex, "处理 Prometheus 请求异常");
            }
        }
    }

    /// <summary>
    /// 生成 Prometheus 文本格式的指标
    /// 
    /// 格式规范：
    /// # TYPE metric_name counter
    /// # HELP metric_name 描述
    /// metric_name value
    /// </summary>
    public string GeneratePrometheusMetrics()
    {
        var sb = new StringBuilder();

        try
        {
            var snapshot = MetricsCollector.Instance.GetDetailedSnapshot();

            foreach (var entry in snapshot)
            {
                var metricName = SanitizeMetricName(entry.Name);
                var fullMetricName = $"{_prefix}{metricName}";

                // 写入类型声明
                sb.AppendLine($"# TYPE {fullMetricName} {GetPrometheusType(entry.Type)}");

                // 写入帮助信息
                if (!string.IsNullOrEmpty(entry.Description))
                {
                    sb.AppendLine($"# HELP {fullMetricName} {entry.Description}");
                }

                // 写入指标值
                sb.AppendLine($"{fullMetricName} {entry.Value}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Metrics", ex, "生成 Prometheus 指标失败");
            sb.AppendLine("# ERROR: Failed to generate metrics");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 清理指标名称（Prometheus 只允许字母、数字、下划线和冒号）
    /// </summary>
    private static string SanitizeMetricName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "unknown";

        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == ':')
                sb.Append(c);
            else if (c == '.' || c == '-')
                sb.Append('_');
        }

        return sb.ToString();
    }

    /// <summary>
    /// 获取 Prometheus 类型名称
    /// </summary>
    private static string GetPrometheusType(string type)
    {
        return type.ToLowerInvariant();
    }

    /// <summary>
    /// 获取 Prometheus 格式的指标文本（静态方法，便于外部调用）
    /// </summary>
    public static string GetMetricsText(string prefix = "mmorpg_")
    {
        var exporter = new PrometheusExporter(0, "/metrics", prefix);
        return exporter.GeneratePrometheusMetrics();
    }
}