// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;
using System.Text.Json;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Observability;

/// <summary>
/// HTTP 健康检查端点
/// 
/// 基于 <see cref="HttpListener"/>（零依赖），暴露：
/// - GET /health   详细健康报告（JSON）
/// - GET /ready    就绪探针（200 / 503）
/// - GET /alive    存活探针（始终 200）
/// - GET /         简单说明
/// 
/// 用法：
///   using var endpoint = new HealthEndpoint(port: 8080, "/health");
///   endpoint.Start();
///   ...
///   endpoint.Stop();
/// 
/// 不依赖 Prometheus / OWIN / Kestrel，启动开销极低，适合 Sidecar / K8s 探针。
/// </summary>
public sealed class HealthEndpoint : IDisposable
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly string _prefix;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private volatile bool _started;
    private volatile bool _disposed;

    /// <summary>
    /// 构造健康检查端点
    /// </summary>
    /// <param name="port">监听端口（建议独立于游戏端口，例如 8080）</param>
    /// <param name="healthPath">健康检查路径（默认 /health）</param>
    /// <param name="readyPath">就绪探针路径（默认 /ready）</param>
    /// <param name="alivePath">存活探针路径（默认 /alive）</param>
    public HealthEndpoint(
        int port = 8080,
        string healthPath = "/health",
        string readyPath = "/ready",
        string alivePath = "/alive")
    {
        if (string.IsNullOrWhiteSpace(healthPath)) throw new ArgumentException("healthPath 不能为空", nameof(healthPath));
        if (string.IsNullOrWhiteSpace(readyPath)) throw new ArgumentException("readyPath 不能为空", nameof(readyPath));
        if (string.IsNullOrWhiteSpace(alivePath)) throw new ArgumentException("alivePath 不能为空", nameof(alivePath));
        if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "port 必须在 1-65535");

        _port = port;
        _prefix = $"http://+:{port}/";
        HealthPath = healthPath;
        ReadyPath = readyPath;
        AlivePath = alivePath;

        _listener = new HttpListener();
        _listener.Prefixes.Add(_prefix);
    }

    /// <summary>健康检查路径</summary>
    public string HealthPath { get; }

    /// <summary>就绪探针路径</summary>
    public string ReadyPath { get; }

    /// <summary>存活探针路径</summary>
    public string AlivePath { get; }

    /// <summary>是否在运行</summary>
    public bool IsRunning => _started && !_cts.IsCancellationRequested;

    /// <summary>
    /// 启动 HTTP 监听（非阻塞）
    /// </summary>
    public void Start()
    {
        if (_started || _disposed)
            return;

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Logger.Error("Observability", ex,
                "HealthEndpoint 启动失败: Port={0}（在 Windows 上可能需要以管理员身份运行或使用 netsh http add urlacl）", _port);
            throw;
        }

        _started = true;
        _listenTask = Task.Run(ListenLoopAsync, _cts.Token);
        Logger.Info("Observability", "HealthEndpoint 已启动: {0}{1}", _prefix, HealthPath);
    }

    /// <summary>
    /// 停止监听
    /// </summary>
    public void Stop()
    {
        if (!_started)
            return;
        try
        {
            _cts.Cancel();
            _listener.Stop();
        }
        catch (Exception ex)
        {
            Logger.Warning("Observability", "HealthEndpoint 停止时异常: {0}", ex.Message);
        }
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warning("Observability", "HealthEndpoint 接收请求异常: {0}", ex.Message);
                continue;
            }

            // 异步处理，避免阻塞 Accept
            _ = Task.Run(() => HandleRequestSafe(ctx), _cts.Token);
        }
    }

    private void HandleRequestSafe(HttpListenerContext ctx)
    {
        try
        {
            HandleRequestAsync(ctx).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.Warning("Observability", "HealthEndpoint 处理请求异常: {0}", ex.Message);
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var path = (ctx.Request.Url?.AbsolutePath ?? "/").TrimEnd('/');
        if (string.IsNullOrEmpty(path)) path = "/";

        switch (path)
        {
            case var p when string.Equals(p, HealthPath, StringComparison.OrdinalIgnoreCase):
                await HandleHealth(ctx);
                break;
            case var p when string.Equals(p, ReadyPath, StringComparison.OrdinalIgnoreCase):
                await HandleReady(ctx);
                break;
            case var p when string.Equals(p, AlivePath, StringComparison.OrdinalIgnoreCase):
                HandleAlive(ctx);
                break;
            case "/":
                HandleIndex(ctx);
                break;
            default:
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                break;
        }
    }

    private static void HandleIndex(HttpListenerContext ctx)
    {
        var body = "MMORPG.Framework HealthEndpoint\n" +
                   "  GET /health  - 详细健康报告 (JSON)\n" +
                   "  GET /ready   - 就绪探针 (200 / 503)\n" +
                   "  GET /alive   - 存活探针 (always 200)\n";
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    private static void HandleAlive(HttpListenerContext ctx)
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes("OK");
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    private static async Task HandleReady(HttpListenerContext ctx)
    {
        var status = await HealthCheckService.Instance.CheckHealthAsync();
        var ready = status.OverallStatus != HealthCheckResult.Unhealthy;
        var bytes = Encoding.UTF8.GetBytes(ready ? "READY" : "NOT_READY");
        ctx.Response.StatusCode = ready ? 200 : 503;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    private static async Task HandleHealth(HttpListenerContext ctx)
    {
        var status = await HealthCheckService.Instance.CheckHealthAsync();
        var json = status.RenderJson();
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.StatusCode = status.OverallStatus switch
        {
            HealthCheckResult.Healthy => 200,
            HealthCheckResult.Degraded => 200,
            HealthCheckResult.Unhealthy => 503,
            _ => 200
        };
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ #25 修复：实现标准 Dispose 模式（Dispose(disposing) + SuppressFinalize）。
    /// 该类 sealed 且无终结器（Finalizer），所以 SuppressFinalize 调用是多余的；
    /// 但保留以遵循 .NET 编码规范，便于后续子类化时自动正确。
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { Stop(); } catch (Exception ex) { Logger.Warning("Observability", "HealthEndpoint.Dispose 停止监听时异常: {0}", ex.Message); }
        try { _cts.Dispose(); } catch (Exception ex) { Logger.Warning("Observability", "HealthEndpoint.Dispose 释放 CTS 时异常: {0}", ex.Message); }
        try { _listener.Close(); } catch (Exception ex) { Logger.Warning("Observability", "HealthEndpoint.Dispose 关闭 HttpListener 时异常: {0}", ex.Message); }

        GC.SuppressFinalize(this);
    }
}
