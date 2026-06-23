# MMORPG 框架商业化打磨 - 任务清单（Decomposed & Prioritized Task List）

说明：以下任务按依赖关系、优先级排序。每个任务需同时完成实现代码 + 单元测试 + 中文注释 +文档更新。

## [ ] Task 1: 指标基础框架 — Counter / Gauge / Histogram 抽象与默认实现
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - 在 `src/MMORPG.Framework/Observability/` 命名空间下创建 `ICounter`、`IGauge`、`IHistogram` 接口，以及 `MetricsCollector` 单例（`Instance` 属性 + `Enable()/Disable()`）。
  - `MetricsCollector` 支持 `RegisterCounter(name, desc)`、`RegisterGauge(name, desc, valueProvider)`、`RegisterHistogram(name, desc, bucketCount)`，以及 `Snapshot()` 返回 `IDictionary<string, double>`，`GetRegisteredMetrics()` 返回指标名列表。
  - Histogram 默认使用环形缓冲（固定大小，例如 1024 条记录），支持 P50/P95/P99 延迟查询（通过排序）。
  - `IMetric` 接口包含 `string Name { get; }`、`string Description { get; }`、`double Value { get; }`、`void Reset()`。
- **Acceptance Criteria Addressed**: AC-1、AC-2
- **Test Requirements**:
  - `programmatic` TR-1.1: Counter 能正确 `Increment()` / `IncrementBy(n)` / `Reset()`，值正确递增与重置
  - `programmatic` TR-1.2: Gauge 能通过 valueProvider 返回当前值（如 `DateTime.Now` 或活跃会话数）
  - `programmatic` TR-1.3: Histogram 记录 1000 个随机延迟值后，P50/P95/P99 能返回合理数值（验证排序与索引）
  - `programmatic` TR-1.4: `MetricsCollector.Instance` 是单例，多次 `new MetricsCollector()` 或引用均返回同一实例
  - `programmatic` TR-1.5: `Snapshot()` 返回的字典非空，且能查询到 tps.total 等标准指标名
- **Notes**: 线程安全优先，使用 `Interlocked` 和 `ConcurrentDictionary`；性能基准测试：Histogram 记录 100 万条延迟不超过 500ms

---

## [ ] Task 2: 指标框架集成到 TcpServer / Session / MessageRouter
- **Priority**: P0
- **Depends On**: Task 1
- **Description**:
  - 在 `Session.ReceiveAsync/SendAsync` 路径中接入 `tps.bytes_rx`、`tps.bytes_tx` 指标；在 `MessageRouter.RouteAsync` 接入 `tps.total` 和 `message.processed_total`。
  - 使用 `Stopwatch` 对消息路由计时，记录到 `latency.p50/p95/p99` Histogram 中。
  - 注册 `session.active` Gauge，在 Session 增加/减少时更新；注册 `gc.count` 和 `gc.time_ms` Gauge，使用 `GC.CollectionCount(0)` 和 `GC.GetTotalAllocatedBytes()` 来统计（简化实现）。
- **Acceptance Criteria Addressed**: AC-1、AC-2
- **Test Requirements**:
  - `programmatic` TR-2.1: 构造模拟 1000 条消息后，`tps.total` 的值 > 0，`message.processed_total` 的值 >= 1000
  - `programmatic` TR-2.2: 连接 10 个 Session 后，`session.active` 的值为 10
  - `programmatic` TR-2.3: 未启用 Metrics 的 TPS 基准 vs 启用后，下降 < 3%（用 BenchmarkDotNet 风格的简单 benchmark 类）
  - `programmatic` TR-2.4: Histogram 有数据时，`latency.p50` 返回非零值，`p95 >= p50`，`p99 >= p95`
  - `human-judgment` TR-2.5: 代码 review —— 集成点位于消息处理路径但不破坏原逻辑，若 Metrics 未启用则几乎无额外开销（通过方法判断 `if (_metricsEnabled)` short-circuit）
- **Notes**: 关键判断 `_metricsEnabled` 为 `volatile bool`，保持最小开销

---

## [ ] Task 3: 健康检查 — HealthCheckService + 默认检查项
- **Priority**: P0
- **Depends On**: Task 1（可选，若检查项用到 Metrics）
- **Description**:
  - `src/MMORPG.Framework/Observability/HealthCheckService.cs`：提供 `Register(string name, Func<CancellationToken, Task<HealthCheckResult>> check)`、`Task<HealthCheckStatus> CheckHealthAsync(CancellationToken)`。
  - `HealthCheckResult` 枚举：`Healthy = 0`、`Degraded = 1`、`Unhealthy = 2`
  - `HealthCheckStatus` 类：`OverallStatus`（聚合最坏的单项状态）、`IList<HealthCheckEntry> Entries`
  - 默认内置检查项：`tcp_listener`（监听是否存活）、`queue_backlog`（消息队列积压是否 > 阈值，基于 Session/TcpServer 信息）、`memory_usage`（当前内存 / 阈值）
  - 提供 `string RenderJsonAsync()`（输出 JSON）和 `string RenderText()`（输出纯文本）。
- **Acceptance Criteria Addressed**: AC-3、AC-4
- **Test Requirements**:
  - `programmatic` TR-3.1: 注册 3 个"健康"项后调用 CheckHealthAsync()，返回 OverallStatus = Healthy，Entries.Count = 3
  - `programmatic` TR-3.2: 注册 2 个健康项 + 1 个返回 Degraded 的项，返回 OverallStatus = Degraded
  - `programmatic` TR-3.3: 注册 1 个返回 Unhealthy 的项 + 2 个健康项，返回 OverallStatus = Unhealthy，Entries 中能找到失败项
  - `programmatic` TR-3.4: RenderJsonAsync() 输出能被 `JsonDocument.Parse` 解析，含 "overall_status" 和 "entries" 字段
  - `programmatic` TR-3.5: RenderText() 输出非空，包含每项名称与状态文字
- **Notes**: 聚合策略：取所有检查项中最坏状态（Healthy < Degraded < Unhealthy）

---

## [ ] Task 4: 消息限流 — RateLimiter（会话级 + 全局级）
- **Priority**: P0
- **Depends On**: None
- **Description**:
  - `src/MMORPG.Framework/Security/RateLimiter.cs`：基础令牌桶实现，构造时指定 `maxMessagesPerSecond`。
  - `RateLimiter` 提供 `bool TryAcquire(int count = 1)` 方法，返回 true（允许处理）或 false（超过限制）。
  - `RateLimiter` 记录 `DroppedCount`（被拒绝的消息总数）、`DateTime LastDroppedTime`。
  - 在 `Session.OnMessageReceived` 或 `MessageRouter.RouteAsync` 路径中接入 Session 级限流；同时保留全局 RateLimiter（单例）在框架初始化时注册总消息阈值。
  - 惩罚策略配置：可枚举 `RateLimitPolicy.Drop`、`RateLimitPolicy.Delay`（降级到低优先级处理）、`RateLimitPolicy.Disconnect`（断开连接）。默认策略为 `Drop`。
  - 在丢弃消息时，Logger 记录 Warning（"Session {0} Rate limit exceeded, dropped {1} messages"）。
- **Acceptance Criteria Addressed**: AC-5、AC-6
- **Test Requirements**:
  - `programmatic` TR-4.1: RateLimiter 限制 10/秒，1 秒内 TryAcquire 50 次，前 10 次 true，后 40 次 false
  - `programmatic` TR-4.2: 等待 1.1 秒后，新的 TryAcquire 又能成功
  - `programmatic` TR-4.3: RateLimiter 限制 100/秒，每秒 TryAcquire 10 次持续 3 秒，全部成功
  - `programmatic` TR-4.4: 多线程并发 TryAcquire（10 线程 × 1000 次，限制 5000/秒），结果正确，无竞态异常
  - `programmatic` TR-4.5: 惩罚策略能在 Session 集成测试中验证：Drop 策略下，DroppedCount 正确递增，Logger 有对应 Warning
- **Notes**: 令牌桶实现用 `Stopwatch` + `Interlocked`，避免高开销

---

## [ ] Task 5: 配置热更新 — ConfigurationLoader 文件监听 + ConfigChanged 事件
- **Priority**: P1
- **Depends On**: None（仅修改现有 ConfigurationLoader.cs）
- **Description**:
  - 给 `ConfigurationLoader` 增加：
    - `void EnableFileWatcher(string path, int intervalMs = 1000)`：按给定间隔检查文件的 LastWriteTimeUtc，若变化则重新加载
    - `void DisableFileWatcher()`：停止监听
    - `event EventHandler<ConfigChangedEventArgs>? ConfigChanged`：配置变化事件
    - `ConfigChangedEventArgs`：包含 `DateTime ChangedAt`、`IReadOnlyDictionary<string, object?> OldSnapshot`、`IReadOnlyDictionary<string, object?> NewSnapshot`
  - 在 Logger 与 TcpServer 中分别提供配置变更应用：例如 `LogLevel` 变化时调用 `Logger.SetLogLevel(...)`；`MaxConnections` 变化时更新 TcpServer 的软限制。
- **Acceptance Criteria Addressed**: AC-7
- **Test Requirements**:
  - `programmatic` TR-5.1: 单元测试模拟文件时间戳变化后，ConfigChanged 事件被触发至少 1 次（用 TempFile）
  - `programmatic` TR-5.2: 启用监听后，间隔时间内多次修改，去抖动机制（只触发一次或延迟到修改停止）能正确工作
  - `programmatic` TR-5.3: 禁用监听后，文件变化不再触发事件
  - `programmatic` TR-5.4: OldSnapshot 和 NewSnapshot 中的 `LogLevel` 正确反映变化前后值
  - `human-judgment` TR-5.5: 热更新去抖动设计合理（不因为 IDE 保存文件反复触发多次 reload）
- **Notes**: 去抖动建议使用"最后一次修改 + intervalMs"策略；intervalMs 默认 1000 或更多

---

## [ ] Task 6: 服务状态管理 — ServiceState 状态机
- **Priority**: P1
- **Depends On**: Task 4（可选，状态机可独立实现）
- **Description**:
  - `src/MMORPG.Framework/Security/ServiceStateManager.cs`：
    - 状态枚举：`Stopped` → `Starting` → `Running` → `Pausing` → `Paused` → `Stopping` → `ShuttingDown` → `Stopped`
    - `async Task StartAsync()`、`async Task PauseAsync()`、`async Task ResumeAsync()`、`async Task StopAsync()`、`async Task ShutdownAsync()`
    - `event EventHandler<ServiceStateChangedEventArgs>? StateChanged`：每次状态变更事件（含 FromState / ToState / ChangedAt）
    - `bool IsRunning { get; }`、`bool AcceptsNormalMessages { get; }`（Paused 时返回 false）
    - `MessageRouter.RouteAsync` 集成：当 `AcceptsNormalMessages == false` 时，仅允许系统消息（如心跳、管理命令）通过，其它被丢弃。
- **Acceptance Criteria Addressed**: AC-8
- **Test Requirements**:
  - `programmatic` TR-6.1: 状态从 Stopped 经 Start → Running，StateChanged 事件触发两次（Stopped→Starting，Starting→Running）
  - `programmatic` TR-6.2: Running → PauseAsync → ResumeAsync → StopAsync，最终状态 Stopped，事件顺序正确
  - `programmatic` TR-6.3: Paused 时 AcceptsNormalMessages == false，非系统消息被丢弃
  - `programmatic` TR-6.4: 并发调用 Pause/Resume/Stop，内部有 `SemaphoreSlim` 锁，无竞态异常
  - `human-judgment` TR-6.5: 代码 review —— 状态流转设计符合运维直觉，每个终态有明确的定义
- **Notes**: 用 `SemaphoreSlim(1, 1)` 作为同步锁，保证每次状态变更一个操作

---

## [ ] Task 7: 崩溃收集 — CrashReporting
- **Priority**: P1
- **Depends On**: Task 1（可选，崩溃文件写入可用 Logger）
- **Description**:
  - `src/MMORPG.Framework/Observability/CrashReporting.cs`：
    - `static void Enable(string filePathTemplate = "logs/crashes/{0}.log")`：注册 AppDomain.UnhandledException 和 TaskScheduler.UnobservedTaskException 事件
    - `static void Disable()`：取消注册
    - `static void ReportException(Exception ex, string? extra = null)`：手动报告异常（写文件 + Logger.Error）
  - 每次报告生成独立文件，内容包含：时间戳（UTC）、线程 ID、服务状态（若有 ServiceStateManager）、异常类型、Message、StackTrace、InnerException、Extra info
  - 写文件使用 `StreamWriter`，带 try/catch，若失败回退到 Logger.Error
- **Acceptance Criteria Addressed**: AC-9
- **Test Requirements**:
  - `programmatic` TR-7.1: 启用后手动调用 ReportException(InvalidOperationException("test"))，磁盘出现新文件，文件内容含 "InvalidOperationException" 和 "test"
  - `programmatic` TR-7.2: 构造一个 `Task.Run(() => throw ...)`（无 await）触发 UnobservedTaskException，检查是否触发了 ReportException
  - `programmatic` TR-7.3: 禁用后，异常不会写入崩溃文件
  - `programmatic` TR-7.4: 崩溃文件名格式符合模板（yyyy-MM-dd-HHmmss.log）
  - `human-judgment` TR-7.5: 崩溃报告格式人类可读（包含时间、堆栈、上下文），便于事后分析
- **Notes**: 注意对 `Environment.FailFast` 的处理（若框架遇到不可恢复异常，可选 FailFast）

---

## [ ] Task 8: 可插拔配置 + 统一入口扩展
- **Priority**: P2
- **Depends On**: Task 1-7
- **Description**:
  - `src/MMORPG.Framework/FrameworkOptions.cs`：统一的选项类，包含 `bool EnableMetrics`、`bool EnableHealthChecks`、`bool EnableRateLimiting`、`bool EnableCrashReporting`、`string? ConfigHotReloadPath` 等
  - `TcpServer` 构造函数可选接收 `FrameworkOptions`，并在初始化时配置各模块
  - 所有模块默认禁用，需要通过 FrameworkOptions 显式启用
- **Acceptance Criteria Addressed**: FR-10（可插拔）
- **Test Requirements**:
  - `programmatic` TR-8.1: 所有 `EnableXXX = false` 时，框架启动后 `MetricsCollector.Instance` 禁用，`RateLimiter` 不接入，行为与当前版本完全一致
  - `programmatic` TR-8.2: 配置 `EnableMetrics = true`、`EnableRateLimiting = true` 后，两个模块均正常工作
  - `human-judgment` TR-8.3: 代码 review —— FrameworkOptions 的使用方式清晰，默认值合理

---

## [ ] Task 9: 性能基准 & 回归测试
- **Priority**: P1
- **Depends On**: Task 1-8
- **Description**:
  - 在现有测试项目中新增性能基准测试类：
    - `tests/Observability/MetricsBenchmarks.cs`
    - `tests/Observability/RateLimiterBenchmarks.cs`
    - `tests/Observability/HealthCheckBenchmarks.cs`
  - 验证每个新模块的 TPS / 延迟 / 内存分配
- **Acceptance Criteria Addressed**: NFR-1、NFR-6
- **Test Requirements**:
  - `programmatic` TR-9.1: Metrics 接入前后 MessageSerializer TPS 对比：下降 < 3%
  - `programmatic` TR-9.2: RateLimiter.TryAcquire 10 线程并发 × 100 万次，< 500ms 完成
  - `programmatic` TR-9.3: HealthCheckService.CheckHealthAsync() 在 10 个检查项的场景下，< 50ms
  - `programmatic` TR-9.4: 全部 95+ 现有测试继续通过，无回归
  - `programmatic` TR-9.5: dotnet build 零警告零错误
- **Notes**: benchmark 类命名空间 `MMORPG.Tests.Observability`

---

## [ ] Task 10: 文档更新
- **Priority**: P1
- **Depends On**: Task 1-9
- **Description**:
  - 更新 `docs/01-整体架构.md`：模块矩阵新增 Observability、Security、ServiceState 条目
  - 新增 `docs/17-框架可观测性与运维.md`：综合介绍 Metrics / HealthCheck / RateLimit / HotReload / ServiceState / CrashReporting 的用法
  - 可选项：更新 16-日志系统 / 15-数据持久化中引用新模块
- **Acceptance Criteria Addressed**: AC-10、AC-11
- **Test Requirements**:
  - `human-judgment` TR-10.1: 文档中文撰写清晰，包含：模块作用、API 列表、使用示例、最佳实践
  - `human-judgment` TR-10.2: docs/17-* 与代码实现完全对齐（示例代码能编译运行）
  - `human-judgment` TR-10.3: 所有示例中的 Logger 调用使用位置占位符，命名空间一致
