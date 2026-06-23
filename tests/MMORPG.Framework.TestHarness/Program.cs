// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

/*
 * MMORPG.Framework 框架测试工具（纯框架测试，无游戏逻辑）
 *
 * 测试覆盖：
 * - 网络层（TcpServer 连接、消息收发）
 * - 指标系统（Counter / Histogram / Gauge + Prometheus 导出）
 * - 健康检查（HealthEndpoint）
 * - 熔断器（CircuitBreaker 状态转换）
 * - 限流器（RateLimiter 令牌桶）
 * - 雪花ID（SnowflakeIdGenerator 并发生成）
 * - 游戏调度器（GameScheduler 帧率稳定性）
 * - 配置热更新（ConfigurationLoader 重载）
 * - 消息通道（MessageChannel 并发读写）
 * - 消息路由（MessageRouter 序列化/反序列化）
 *
 * HTTP 命令端点：
 *   GET /health           → 健康检查
 *   GET /metrics          → Prometheus 指标
 *   GET /test/run         → 手动触发全部测试（需 API Key）
 *   GET /test/results     → 查看测试结果
 *   GET /test/connect     → 网络连接测试
 *
 * 环境变量配置：
 *   AUTO_RUN_TESTS=1      → 启动时自动运行测试
 *   TEST_API_KEY=xxx      → API Key（可选，设置后需在请求头 X-API-Key 中提供）
 *   TEST_TIMEOUT_SECONDS=30 → 单测试超时时间（默认30秒）
 *   TEST_MAX_RETRIES=2    → 测试重试次数（默认2次）
 *
 * 使用：dotnet run --project tests/MMORPG.Framework.TestHarness
 */

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using MMORPG.Framework.Configuration;
using MMORPG.Framework.Logging;
using MMORPG.Framework.Network;
using MMORPG.Framework.Observability;
using MMORPG.Framework.Resilience;
using MMORPG.Framework.Security;
using MMORPG.Framework.Threading;

namespace MMORPG.Framework.TestHarness;

public class TestHarnessOptions
{
    public int TcpPort { get; set; } = 7001;
    public int HttpPort { get; set; } = 8080;
    public int TestConnections { get; set; } = 50;
    public int MessagesPerConnection { get; set; } = 20;
    public int TestTimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 2;
}

class Program
{
    private static readonly TestHarnessOptions _options = new();
    private static readonly HttpListener _httpListener = new();
    private static readonly List<TestResult> _results = new();
    private static readonly object _resultsLock = new();
    private static TcpServer? _server;
    
    private static ICounter? _testTotalCounter;
    private static ICounter? _testPassedCounter;
    private static ICounter? _testFailedCounter;
    private static IHistogram? _testDurationHistogram;

    static async Task Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════╗");
        Console.WriteLine("║   MMORPG.Framework 框架测试工具 v2.0        ║");
        Console.WriteLine("║   纯框架测试，无游戏逻辑                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════╝");
        Console.WriteLine();

        // 1. 初始化日志系统（最先初始化，确保所有组件都能正常记录日志）
        Logger.Initialize(new LogOptions
        {
            MinLevel = LogLevel.Info,
            EnableConsole = true,
            EnableFile = true
        });

        // 2. 加载配置
        LoadConfiguration();
        Logger.Info("TestHarness", "日志系统初始化完成");

        MessageSerializer.Initialize();
        Logger.Info("TestHarness", "消息序列化器初始化完成");

        StartHttpListener();

        try
        {
            var exporter = new PrometheusExporter(9091, "/metrics", "mmorpg_");
            exporter.Start();
            Logger.Info("TestHarness", "Prometheus 导出器启动: http://localhost:9091/metrics");
        }
        catch (Exception ex)
        {
            Logger.Warning("TestHarness", "Prometheus 导出器启动失败（非致命）: {0}", ex.Message);
        }

        RegisterFrameworkMetrics();
        SetupHealthChecks();

        var autoRun = args.Contains("--auto-run") || Environment.GetEnvironmentVariable("AUTO_RUN_TESTS") == "1";

        if (autoRun)
        {
            await Task.Delay(500);
            await RunAllTestsAsync();
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("  框架已启动，可通过以下方式触发测试：");
            Console.WriteLine("    curl -H 'X-API-Key: <key>' http://localhost:{0}/test/run    → 运行全部测试", _options.HttpPort);
            Console.WriteLine("    curl http://localhost:{0}/test/results → 查看测试结果", _options.HttpPort);
            Console.WriteLine();
            Console.WriteLine("  或设置环境变量 AUTO_RUN_TESTS=1 启动时自动运行");
            Console.WriteLine("  设置 TEST_API_KEY=xxx 启用 API 认证");
            Console.WriteLine();
            Console.WriteLine("  按 Ctrl+C 停止");
        }

        var tcs = new TaskCompletionSource<bool>();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(true); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => tcs.TrySetResult(true);

        await tcs.Task;

        Logger.Info("TestHarness", "测试工具停止中...");
        if (_server != null) await _server.StopAsync(3000);
        _httpListener.Stop();
        _httpListener.Close();
        Logger.Info("TestHarness", "测试工具已停止");
    }

    private static void LoadConfiguration()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("TCP_PORT"), out var tcpPort))
            _options.TcpPort = tcpPort;
        
        if (int.TryParse(Environment.GetEnvironmentVariable("HTTP_PORT"), out var httpPort))
            _options.HttpPort = httpPort;
        
        if (int.TryParse(Environment.GetEnvironmentVariable("TEST_CONNECTIONS"), out var connections))
            _options.TestConnections = connections;
        
        if (int.TryParse(Environment.GetEnvironmentVariable("MESSAGES_PER_CONNECTION"), out var messages))
            _options.MessagesPerConnection = messages;
        
        if (int.TryParse(Environment.GetEnvironmentVariable("TEST_TIMEOUT_SECONDS"), out var timeout))
            _options.TestTimeoutSeconds = timeout;
        
        if (int.TryParse(Environment.GetEnvironmentVariable("TEST_MAX_RETRIES"), out var retries))
            _options.MaxRetries = retries;

        Logger.Info("TestHarness", "配置加载完成: TCP={0}, HTTP={1}, Timeout={2}s, Retries={3}", 
            _options.TcpPort, _options.HttpPort, _options.TestTimeoutSeconds, _options.MaxRetries);
    }

    private static void StartHttpListener()
    {
        try
        {
            _httpListener.Prefixes.Add($"http://+:{_options.HttpPort}/");
            _httpListener.Start();
            Logger.Info("TestHarness", "HTTP 命令端点启动: http://*:{0}/", _options.HttpPort);

            _ = Task.Run(async () =>
            {
                while (_httpListener.IsListening)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        _ = HandleHttpRequestAsync(context);
                    }
                    catch { break; }
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error("TestHarness", "HTTP 监听器启动失败: {0}", ex.Message);
        }
    }

    private static bool ValidateApiKey(HttpListenerContext context)
    {
        var expectedKey = Environment.GetEnvironmentVariable("TEST_API_KEY");
        if (string.IsNullOrEmpty(expectedKey))
            return true;

        var apiKey = context.Request.Headers["X-API-Key"];
        return apiKey == expectedKey;
    }

    private static async Task HandleHttpRequestAsync(HttpListenerContext context)
    {
        var response = context.Response;
        var path = context.Request.Url?.AbsolutePath ?? "/";

        string content = "";
        int statusCode = 200;

        try
        {
            if (path == "/test/run" && !ValidateApiKey(context))
            {
                statusCode = 401;
                content = JsonSerializer.Serialize(new { error = "Unauthorized", message = "请在请求头中提供 X-API-Key" });
                response.ContentType = "application/json";
            }
            else
            {
                switch (path)
                {
                    case "/health":
                        content = await HandleHealthAsync();
                        response.ContentType = "application/json";
                        break;

                    case "/metrics":
                        content = HandleMetrics();
                        response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
                        break;

                    case "/test/run":
                        _ = Task.Run(async () => await RunAllTestsAsync());
                        content = JsonSerializer.Serialize(new { status = "running" });
                        response.ContentType = "application/json";
                        break;

                    case "/test/results":
                        content = HandleTestResults();
                        response.ContentType = "application/json";
                        break;

                    case "/test/connect":
                        content = await HandleConnectTestAsync();
                        response.ContentType = "application/json";
                        break;

                    case "/test/reset":
                        lock (_resultsLock) _results.Clear();
                        content = JsonSerializer.Serialize(new { status = "ok" });
                        response.ContentType = "application/json";
                        break;

                    default:
                        statusCode = 404;
                        content = "Not Found. 可用端点: /health, /metrics, /test/run, /test/results, /test/connect";
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            statusCode = 500;
            content = JsonSerializer.Serialize(new { error = "Internal Error", message = ex.Message });
            Logger.Error("TestHarness", "HTTP 请求处理异常: {0}", ex.ToString());
        }

        var buffer = Encoding.UTF8.GetBytes(content);
        response.StatusCode = statusCode;
        response.ContentType ??= "text/plain; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    private static async Task<string> HandleHealthAsync()
    {
        var status = await HealthCheckService.Instance.CheckHealthAsync();
        var result = new
        {
            status = status.OverallStatus.ToString(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            checks = status.Entries.Select(e => new { name = e.Name, status = e.Result.ToString(), description = e.Description })
        };
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string HandleMetrics()
    {
        var exporter = new PrometheusExporter(0, "/metrics", "mmorpg_");
        return exporter.GeneratePrometheusMetrics();
    }

    private static string HandleTestResults()
    {
        List<TestResult> snapshot;
        lock (_resultsLock)
        {
            snapshot = _results.ToList();
        }
        
        var summary = new
        {
            total = snapshot.Count,
            passed = snapshot.Count(r => r.Passed),
            failed = snapshot.Count(r => !r.Passed),
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
            tests = snapshot.Select(r => new
            {
                name = r.Name,
                passed = r.Passed,
                durationMs = r.DurationMs,
                message = r.Message,
                timestamp = r.Timestamp.ToString("HH:mm:ss.fff")
            })
        };
        return JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
    }

    private static async Task<string> HandleConnectTestAsync()
    {
        var sw = Stopwatch.StartNew();
        int success = 0;

        var tasks = new List<Task<bool>>(10);
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    using var client = new System.Net.Sockets.TcpClient();
                    await client.ConnectAsync("127.0.0.1", _options.TcpPort);
                    Interlocked.Increment(ref success);
                    return true;
                }
                catch { return false; }
            }));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        return JsonSerializer.Serialize(new
        {
            connected = success,
            failed = 10 - success,
            durationMs = sw.ElapsedMilliseconds
        });
    }

    private static async Task RunAllTestsAsync()
    {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  开始运行框架测试");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine();

        lock (_resultsLock)
        {
            _results.Clear();
        }

        await StartTcpServerAsync();

        var testTasks = new List<Task>
        {
            RunWithTimeoutAndRetry(() => Task.Run(RunMetricsTest), "Metrics"),
            RunWithTimeoutAndRetry(() => Task.Run(RunRateLimiterTest), "RateLimiter"),
            RunWithTimeoutAndRetry(() => Task.Run(RunSnowflakeTest), "SnowflakeId"),
            RunWithTimeoutAndRetry(RunNetworkTestAsync, "Network"),
            RunWithTimeoutAndRetry(RunCircuitBreakerTestAsync, "CircuitBreaker"),
            RunWithTimeoutAndRetry(() => Task.Run(RunGameSchedulerTest), "GameScheduler"),
            RunWithTimeoutAndRetry(() => Task.Run(RunMessageChannelTest), "MessageChannel"),
            RunWithTimeoutAndRetry(() => Task.Run(RunSerializerTest), "Serializer"),
            RunWithTimeoutAndRetry(() => Task.Run(RunConfigHotReloadTest), "ConfigHotReload"),
            RunWithTimeoutAndRetry(() => Task.Run(RunHealthCheckTest), "HealthCheck")
        };

        await Task.WhenAll(testTasks);

        if (_server != null) await _server.StopAsync(2000);

        await SaveResultsToFile();

        List<TestResult> snapshot;
        lock (_resultsLock) { snapshot = _results.ToList(); }

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════");
        var passed = snapshot.Count(r => r.Passed);
        var failed = snapshot.Count(r => !r.Passed);
        Console.WriteLine($"  测试结果: {passed} 通过 / {failed} 失败 / {snapshot.Count} 总计");

        if (failed > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  失败项：");
            foreach (var r in snapshot.Where(r => !r.Passed))
                Console.WriteLine($"    ✗ {r.Name}: {r.Message}");
        }
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine();
    }

    private static async Task RunWithTimeoutAndRetry(Func<Task> testFunc, string testName)
    {
        var timeout = TimeSpan.FromSeconds(_options.TestTimeoutSeconds);
        
        for (int retry = 0; retry <= _options.MaxRetries; retry++)
        {
            try
            {
                var cts = new CancellationTokenSource(timeout);
                var task = testFunc();
                var completedTask = await Task.WhenAny(task, Task.Delay(timeout));
                
                if (completedTask == task)
                {
                    await task;
                    return;
                }
                
                throw new TimeoutException($"测试 {testName} 超时 ({timeout.TotalSeconds}s)");
            }
            catch (Exception ex)
            {
                Logger.Warning("TestHarness", "[{0}] 尝试 {1}/{2} 失败: {3}", testName, retry + 1, _options.MaxRetries + 1, ex.Message);
                
                if (retry >= _options.MaxRetries)
                {
                    RecordResult(testName, false, (long)timeout.TotalMilliseconds, $"最终失败: {ex.Message}");
                    return;
                }
                
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (retry + 1)));
            }
        }
    }

    private static async Task SaveResultsToFile()
    {
        try
        {
            List<TestResult> snapshot;
            lock (_resultsLock) { snapshot = _results.ToList(); }

            var results = new
            {
                total = snapshot.Count,
                passed = snapshot.Count(r => r.Passed),
                failed = snapshot.Count(r => !r.Passed),
                timestamp = DateTimeOffset.UtcNow.ToString("o"),
                tests = snapshot.Select(r => new
                {
                    name = r.Name,
                    passed = r.Passed,
                    durationMs = r.DurationMs,
                    message = r.Message,
                    timestamp = r.Timestamp.ToString("o")
                })
            };

            var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
            if (!Directory.Exists(logsDir))
                Directory.CreateDirectory(logsDir);

            var filePath = Path.Combine(logsDir, $"test_results_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.json");
            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            
            Logger.Info("TestHarness", "测试结果已保存: {0}", filePath);
        }
        catch (Exception ex)
        {
            Logger.Error("TestHarness", "保存测试结果失败: {0}", ex.Message);
        }
    }

    private static async Task StartTcpServerAsync()
    {
        try
        {
            var options = new TcpServerOptions
            {
                Port = _options.TcpPort,
                Backlog = 256,
                MaxConnections = 2000,
                ReceiveBufferSize = 8192,
                SendBufferSize = 8192,
                HeartbeatTimeoutSeconds = 30
            };

            var (valid, error) = options.Validate();
            if (!valid) { Logger.Error("TestHarness", "TcpServer 参数校验失败: {0}", error); return; }

            _server = new TcpServer(options);

            MessageRouter.Instance.RegisterHandler(MessageIds.C2S_Heartbeat, async (session, message) =>
            {
                var req = (C2S_Heartbeat)message;
                await session.SendAsync(new S2C_Heartbeat
                {
                    ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ClientTime = req.ClientTime
                });
            });

            await _server.StartAsync();
            Logger.Info("TestHarness", "TcpServer 启动成功: port={0}", _options.TcpPort);
        }
        catch (Exception ex)
        {
            Logger.Error("TestHarness", "TcpServer 启动失败: {0}", ex.Message);
        }
    }

    private static async Task RunNetworkTestAsync()
    {
        var sw = Stopwatch.StartNew();
        bool passed = false;
        string message = "";

        try
        {
            if (_server == null)
            {
                message = "TcpServer 未启动";
                sw.Stop();
                RecordResult("Network", false, 0, message);
                return;
            }

            Logger.Info("TestHarness", "[网络测试] 开始: {0} 连接 × {1} 消息", _options.TestConnections, _options.MessagesPerConnection);

            var tasks = new List<Task<(bool ok, int successCount)>>(_options.TestConnections);
            for (int i = 0; i < _options.TestConnections; i++)
            {
                int connId = i;
                tasks.Add(Task.Run(async () =>
                {
                    int localSuccess = 0;
                    try
                    {
                        using var client = new System.Net.Sockets.TcpClient();
                        await client.ConnectAsync("127.0.0.1", _options.TcpPort);
                        var stream = client.GetStream();
                        var buffer = new byte[8192];

                        for (int j = 0; j < _options.MessagesPerConnection; j++)
                        {
                            var heartbeat = new C2S_Heartbeat
                            {
                                ClientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            };

                            var body = heartbeat.ToByteArray();
                            var msgId = MessageIds.C2S_Heartbeat;

                            var packet = new byte[8 + body.Length];
                            BitConverter.GetBytes(body.Length).CopyTo(packet, 0);
                            BitConverter.GetBytes(msgId).CopyTo(packet, 4);
                            body.CopyTo(packet, 8);

                            await stream.WriteAsync(packet);
                            stream.ReadTimeout = 5000;

                            var header = new byte[8];
                            int read = await ReadExactAsync(stream, header);
                            if (read != 8) { await Task.Delay(10); continue; }

                            var bodyLen = BitConverter.ToInt32(header, 0);
                            if (bodyLen > 0 && bodyLen < 4096)
                            {
                                var respBody = new byte[bodyLen];
                                read = await ReadExactAsync(stream, respBody);
                                if (read == bodyLen) localSuccess++;
                            }
                        }
                    }
                    catch { }

                    return (localSuccess > 0, localSuccess);
                }));
            }

            var results = await Task.WhenAll(tasks);
            int totalSuccess = results.Sum(r => r.Item2);
            int totalExpected = _options.TestConnections * _options.MessagesPerConnection;
            sw.Stop();

            passed = totalSuccess >= totalExpected * 0.8;
            message = $"{_options.TestConnections}连接×{_options.MessagesPerConnection}消息，成功{totalSuccess}/{totalExpected}，耗时{sw.ElapsedMilliseconds}ms";

            Logger.Info("TestHarness", "[网络测试] 完成: {0}", message);
        }
        catch (Exception ex)
        {
            sw.Stop();
            passed = false;
            message = $"异常: {ex.Message}";
            Logger.Error("TestHarness", "[网络测试] 失败: {0}", ex.Message);
        }

        RecordResult("Network", passed, sw.ElapsedMilliseconds, message);
    }

    private static async Task<int> ReadExactAsync(System.Net.Sockets.NetworkStream stream, byte[] buffer)
    {
        int total = 0, read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer, total, buffer.Length - total)) > 0)
            total += read;
        return total;
    }

    private static void RunMetricsTest()
    {
        var sw = Stopwatch.StartNew();
        bool passed = true;
        string message = "";

        try
        {
            var counter = MetricsCollector.Instance.RegisterCounter("test.counter", "测试计数器");
            if (counter == null) { passed = false; message += "Counter 注册失败; "; }
            else
            {
                for (int i = 0; i < 1000; i++) counter.Increment();
                if (counter.Count != 1000) { passed = false; message += $"Counter 计数错误: {counter.Count}; "; }
            }

            var hist = MetricsCollector.Instance.RegisterHistogram("test.histogram", "测试直方图", 1024);
            if (hist == null) { passed = false; message += "Histogram 注册失败; "; }
            else
            {
                var rand = new Random(42);
                for (int i = 0; i < 100; i++) hist.Record(rand.NextDouble() * 100);
                if (hist.SampleCount != 100) { passed = false; message += $"Histogram 样本数错误: {hist.SampleCount}; "; }
                if (hist.P50 <= 0 || hist.P99 > 100) { passed = false; message += $"Histogram 分位数异常: P50={hist.P50}, P99={hist.P99}; "; }
            }

            var gauge = MetricsCollector.Instance.RegisterGauge("test.gauge", "测试量规");
            if (gauge == null) { passed = false; message += "Gauge 注册失败; "; }
            else
            {
                gauge.Set(99.5);
                if (Math.Abs(gauge.Value - 99.5) > 0.001) { passed = false; message += $"Gauge 值错误: {gauge.Value}; "; }
            }

            var exporter = new PrometheusExporter(0, "/metrics", "mmorpg_");
            var metrics = exporter.GeneratePrometheusMetrics();
            if (!metrics.Contains("mmorpg_test_counter") && !metrics.Contains("mmorpg_test_histogram"))
            {
                passed = false;
                message += "Prometheus 导出缺少测试指标; ";
            }

            message = passed && string.IsNullOrEmpty(message) ? "Counter/Histogram/Gauge/Prometheus 全部正常" : (message.Length > 0 ? message.TrimEnd(' ', ';') : "正常");
        }
        catch (Exception ex)
        {
            passed = false;
            message = $"异常: {ex.Message}";
        }

        sw.Stop();
        Logger.Info("TestHarness", "[指标测试] 结果: {0}", message);
        RecordResult("Metrics", passed, sw.ElapsedMilliseconds, message);
    }

    private static async Task RunCircuitBreakerTestAsync()
    {
        var sw = Stopwatch.StartNew();
        bool passed = false;
        string message = "";

        try
        {
            var cb = new CircuitBreaker("test.cb", new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                DurationOfBreakMs = 500,
                HalfOpenMaxAttempts = 1,
                MinimumExecutionTimeMs = 0
            });

            int openedCount = 0;
            for (int i = 0; i < 5; i++)
            {
                try { await cb.ExecuteAsync<int>(async () => { await Task.Yield(); throw new Exception("fail"); }); }
                catch (BrokenCircuitException) { Interlocked.Increment(ref openedCount); }
                catch { }
            }

            await Task.Delay(600);

            if (cb.State == CircuitBreakerState.Open || cb.State == CircuitBreakerState.HalfOpen)
            {
                passed = true;
                message = $"熔断器正常: 触发打开，状态={cb.State}";
            }
            else
            {
                message = $"熔断器未能打开（State={cb.State}）";
            }
        }
        catch (Exception ex)
        {
            passed = false;
            message = $"异常: {ex.Message}";
        }

        sw.Stop();
        Logger.Info("TestHarness", "[熔断测试] 结果: {0}", message);
        RecordResult("CircuitBreaker", passed, sw.ElapsedMilliseconds, message);
    }

    private static void RunRateLimiterTest()
    {
        var sw = Stopwatch.StartNew();
        bool passed = false;
        string message = "";

        try
        {
            var limiter = new RateLimiter(100);

            int allowed = 0, denied = 0;
            for (int i = 0; i < 120; i++)
            {
                if (limiter.TryAcquire()) allowed++;
                else denied++;
            }

            if (allowed >= 95 && denied >= 15)
            {
                passed = true;
                message = $"令牌桶正常: 允许={allowed}, 拒绝={denied}";
            }
            else
            {
                message = $"令牌桶异常: 允许={allowed}, 拒绝={denied}";
            }
        }
        catch (Exception ex)
        {
            passed = false;
            message = $"异常: {ex.Message}";
        }

        sw.Stop();
        Logger.Info("TestHarness", "[限流测试] 结果: {0}", message);
        RecordResult("RateLimiter", passed, sw.ElapsedMilliseconds, message);
    }

    private static void RunSnowflakeTest()
    {
        var sw = Stopwatch.StartNew();
        bool passed = false;
        string message = "";

        try
        {
            var generator = new SnowflakeIdGenerator(1);

            var ids = new HashSet<long>();
            var lock_obj = new object();
            Parallel.For(0, 10000, _ =>
            {
                var id = generator.NewId();
                lock (lock_obj) { ids.Add(id); }
            });

            sw.Stop();

            if (ids.Count == 10000)
            {
                passed = true;
                message = $"雪花ID正常: 10000个ID全部唯一，耗时{sw.ElapsedMilliseconds}ms";
            }
            else
            {
                message = $"ID重复: 生成10000个，唯一{ids.Count}个";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            passed = false;
            message = $"异常: {ex.Message}";
        }

        Logger.Info("TestHarness", "[雪花ID测试] 结果: {0}", message);
        RecordResult("SnowflakeId", passed, sw.ElapsedMilliseconds, message);
    }

    private static void RunGameSchedulerTest()
    {
        var sw = Stopwatch.StartNew();
        bool passed = false;
        string message = "";

        try
        {
            var scheduler = new GameScheduler(new GameSchedulerOptions
            {
                TargetFps = 20,
                FrameTimeWindowSize = 50
            });

            int tickCount = 0;
            scheduler.OnUpdate += _ => Interlocked.Increment(ref tickCount);

            // 在新线程中启动调度器（Start() 会阻塞）
            var task = Task.Run(() => scheduler.Start());

            // 等待 1 秒后主动停止
            Thread.Sleep(1000);
            scheduler.Stop();

            // 等待调度器完全停止
            task.Wait(TimeSpan.FromSeconds(5));

            sw.Stop();

            if (tickCount >= 17 && tickCount <= 23)
            {
                passed = true;
                message = $"调度器正常: 期望≈20帧，实际{tickCount}帧";
            }
            else
            {
                message = $"帧数偏差: 期望≈20，实际{tickCount}";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            passed = false;
            message = $"异常: {ex.Message}";
        }

        Logger.Info("TestHarness", "[调度器测试] 结果: {0}", message);
        RecordResult("GameScheduler", passed, sw.ElapsedMilliseconds, message);
    }

    private static void RunMessageChannelTest()
    {
        var sw = Stopwatch.StartNew();
        bool passed = false;
        string message = "";

        try
        {
            var channel = MessageChannel.CreateUnbounded<string>();

            var writeTasks = new List<Task>(100);
            for (int i = 0; i < 100; i++)
            {
                int id = i;
                writeTasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 10; j++)
                    channel.WriteAsync($"msg-{id}-{j}").AsTask().Wait();
            }));
            }

            Task.WaitAll(writeTasks.ToArray());

            int readCount = 0;
            while (channel.TryRead(out var _))
            {
                readCount++;
            }

            sw.Stop();

            if (readCount == 1000)
            {
                passed = true;
                message = $"消息通道正常: 写入1000条，读取{readCount}条";
            }
            else
            {
                message = $"消息数量不匹配: 期望1000，实际{readCount}";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            passed = false;
            message = $"异常: {ex.Message}";
        }

        Logger.Info("TestHarness", "[消息通道测试] 结果: {0}", message);
        RecordResult("MessageChannel", passed, sw.ElapsedMilliseconds, message);
    }

    private static void RunSerializerTest()
    {
        var sw = Stopwatch.StartNew();
        bool passed = false;
        string message = "";

        try
        {
            var login = new C2S_Login { Account = "test_account", Password = "test_password" };
            var serialized = MessageSerializer.Serialize(login);
            var deserialized = MessageSerializer.Deserialize(serialized) as C2S_Login;

            sw.Stop();

            if (deserialized != null && deserialized.Account == "test_account" && deserialized.Password == "test_password")
            {
                passed = true;
                message = $"序列化正常: C2S_Login 序列化/反序列化成功，{serialized.Length}字节";
            }
            else
            {
                message = "反序列化结果与原数据不匹配";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            passed = false;
            message = $"异常: {ex.Message}";
        }

        Logger.Info("TestHarness", "[序列化测试] 结果: {0}", message);
        RecordResult("Serializer", passed, sw.ElapsedMilliseconds, message);
    }

    private static void RunConfigHotReloadTest()
    {
        var sw = Stopwatch.StartNew();
        bool passed = false;
        string message = "";
        string? tempFile = null;

        try
        {
            tempFile = Path.Combine(Path.GetTempPath(), $"mmorpg_test_config_{Guid.NewGuid():N}.json");
            File.WriteAllText(tempFile, "{\"Test\": {\"Value\": \"initial\"}}");

            var config1 = ConfigurationLoader.LoadSection<TestConfig>(tempFile, "Test");
            if (config1 == null || config1.Value != "initial")
            {
                message = $"初始配置读取失败: {config1?.Value}";
                sw.Stop();
                RecordResult("ConfigHotReload", false, sw.ElapsedMilliseconds, message);
                return;
            }

            File.WriteAllText(tempFile, "{\"Test\": {\"Value\": \"updated\"}}");
            ConfigurationLoader.ClearCache();
            Thread.Sleep(200);

            var config2 = ConfigurationLoader.LoadSection<TestConfig>(tempFile, "Test");
            sw.Stop();

            if (config2 != null && config2.Value == "updated")
            {
                passed = true;
                message = "配置热更新正常: initial → updated";
            }
            else
            {
                message = $"热更新后配置未变化: {config2?.Value}";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            passed = false;
            message = $"异常: {ex.Message}";
        }
        finally
        {
            if (tempFile != null && File.Exists(tempFile))
                try { File.Delete(tempFile); } catch { }
        }

        Logger.Info("TestHarness", "[配置热更新测试] 结果: {0}", message);
        RecordResult("ConfigHotReload", passed, sw.ElapsedMilliseconds, message);
    }

    private class TestConfig
    {
        public string Value { get; set; } = string.Empty;
    }

    private static void RunHealthCheckTest()
    {
        var sw = Stopwatch.StartNew();
        bool passed = false;
        string message = "";

        try
        {
            var status = HealthCheckService.Instance.CheckHealthAsync().GetAwaiter().GetResult();

            sw.Stop();

            if (status.OverallStatus == HealthCheckResult.Healthy)
            {
                passed = true;
                message = $"健康检查正常: {status.OverallStatus}，{status.Entries.Count} 项";
            }
            else
            {
                message = $"健康检查异常: {status.OverallStatus}";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            passed = false;
            message = $"异常: {ex.Message}";
        }

        Logger.Info("TestHarness", "[健康检查测试] 结果: {0}", message);
        RecordResult("HealthCheck", passed, sw.ElapsedMilliseconds, message);
    }

    private static void SetupHealthChecks()
    {
        HealthCheckService.Instance.Clear();

        HealthCheckService.Instance.Register("TcpServer", () =>
            _server != null ? HealthCheckResult.Healthy : HealthCheckResult.Unhealthy);

        HealthCheckService.Instance.Register("HttpListener", () =>
            _httpListener.IsListening ? HealthCheckResult.Healthy : HealthCheckResult.Unhealthy);

        HealthCheckService.Instance.Register("MessageSerializer", () => HealthCheckResult.Healthy);
    }

    private static void RegisterFrameworkMetrics()
    {
        MetricsCollector.Instance.Enable();
        
        _testTotalCounter = MetricsCollector.Instance.RegisterCounter("framework.test.total", "测试运行总次数");
        _testPassedCounter = MetricsCollector.Instance.RegisterCounter("framework.test.passed", "测试通过次数");
        _testFailedCounter = MetricsCollector.Instance.RegisterCounter("framework.test.failed", "测试失败次数");
        _testDurationHistogram = MetricsCollector.Instance.RegisterHistogram("framework.test.duration_ms", "测试耗时分布");
        
        MetricsCollector.Instance.RegisterGauge("framework.connections", "当前活跃连接数", () => _server?.ConnectionCount ?? 0);
    }

    private static void RecordResult(string name, bool passed, long durationMs, string message)
    {
        var result = new TestResult(name, passed, durationMs, message, DateTime.Now);
        
        lock (_resultsLock)
        {
            _results.Add(result);
        }

        _testTotalCounter?.Increment();
        if (passed) _testPassedCounter?.Increment();
        else _testFailedCounter?.Increment();
        _testDurationHistogram?.Record(durationMs);

        Logger.Info("TestHarness", "[测试结果] {0}: {1} ({2}ms) - {3}", name, passed ? "通过" : "失败", durationMs, message);
    }

    private record TestResult(string Name, bool Passed, long DurationMs, string Message, DateTime Timestamp);
}