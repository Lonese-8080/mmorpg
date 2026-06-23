# MMORPG 框架商业化打磨 — 验证检查清单

实现完成后逐项核对。

## 🧪 代码测试验证
- [ ] **C-1**: `MetricsCollector` 单例正确 — Instance 始终指向同一对象
- [ ] **C-2**: Counter 正确 — Increment/IncrementBy/Reset 均返回预期值
- [ ] **C-3**: Gauge 正确 — 通过 valueProvider 能返回动态值
- [ ] **C-4**: Histogram 正确 — P50 ≤ P95 ≤ P99，且非零
- [ ] **C-5**: Snapshot() 返回完整标准指标：`tps.total`、`tps.bytes_tx`、`tps.bytes_rx`、`latency.p50`、`latency.p95`、`latency.p99`、`session.active`、`message.processed_total`、`gc.count`、`gc.time_ms`
- [ ] **C-6**: TcpServer/Session/MessageRouter 接入后，指标能随消息处理递增
- [ ] **C-7**: 启用 Metrics vs 未启用，TPS 下降 < 3%
- [ ] **C-8**: HealthCheckService 注册 3 项健康检查 → Healthy
- [ ] **C-9**: 注册含 Degraded/Unhealthy 项 → OverallStatus 取最坏状态
- [ ] **C-10**: RenderJsonAsync() 输出合法 JSON
- [ ] **C-11**: RenderText() 输出非空可读文本
- [ ] **C-12**: RateLimiter 令牌桶 — 10/秒限制下，50 次中前 10 通过，后续被拒绝
- [ ] **C-13**: RateLimiter 令牌桶 — 等待 >1 秒后重新可用
- [ ] **C-14**: RateLimiter 正常玩家（1/秒 × 10 秒）全部通过，DroppedCount=0
- [ ] **C-15**: RateLimiter 多线程并发 TryAcquire 无异常
- [ ] **C-16**: 惩罚策略 Drop 下，丢弃消息时 Logger 有 Warning 记录
- [ ] **C-17**: ConfigurationLoader.EnableFileWatcher() — 修改文件触发 ConfigChanged
- [ ] **C-18**: ConfigurationLoader — 去抖动机制生效（短时间多次修改只触发少量事件）
- [ ] **C-19**: ConfigurationLoader.DisableFileWatcher() — 后续修改不再触发
- [ ] **C-20**: ConfigChanged 事件携带的 OldSnapshot/NewSnapshot 正确反映 LogLevel 等变化
- [ ] **C-21**: ServiceStateManager 正确流转 Stopped→Starting→Running→Pausing→Paused→Stopping→Stopped
- [ ] **C-22**: ServiceStateManager 每次状态变更都触发 StateChanged 事件
- [ ] **C-23**: ServiceStateManager Paused 状态下 AcceptsNormalMessages=false，非系统消息被丢弃
- [ ] **C-24**: ServiceStateManager 并发调用 Pause/Resume/Stop 无竞态异常
- [ ] **C-25**: CrashReporting.Enable() — 手动 ReportException 生成崩溃文件
- [ ] **C-26**: 崩溃文件内容含：时间戳、异常类型、Message、StackTrace
- [ ] **C-27**: CrashReporting.Disable() — 后续异常不生成新文件
- [ ] **C-28**: UnobservedTaskException 触发 — 报告到崩溃文件
- [ ] **C-29**: FrameworkOptions 全 false 时，框架行为与当前版本一致（无副作用）
- [ ] **C-30**: FrameworkOptions 启用模块后，各模块正常工作
- [ ] **C-31**: MetricsBenchmarks — 单个 Histogram 记录 100 万值 < 500ms
- [ ] **C-32**: RateLimiterBenchmarks — 10 线程 × 100 万 TryAcquire < 500ms
- [ ] **C-33**: HealthCheckBenchmarks — 10 检查项 CheckHealthAsync < 50ms
- [ ] **C-34**: 全部 95+ 已有测试继续通过，无回归
- [ ] **C-35**: `dotnet build` 零警告零错误

## 🏗️ 代码质量 & 规范检查
- [ ] **C-36**: 所有新文件使用 `MMORPG.Framework.*` 命名空间（例如 `MMORPG.Framework.Observability`、`MMORPG.Framework.Security`）
- [ ] **C-37**: 所有新文件包含完整中文注释（类、方法、公开属性都有 `<summary>` 注释）
- [ ] **C-38**: Logger 调用全部使用位置占位符（`{0}`、`{1}`…），无命名占位符
- [ ] **C-39**: 零第三方 NuGet 依赖 —— 仅使用 .NET 10 BCL
- [ ] **C-40**: 所有公共 API 在多线程下安全（使用 `Interlocked`、`ConcurrentDictionary`、`SemaphoreSlim`）
- [ ] **C-41**: 禁用状态下的模块调用 `null` 短路或 `volatile bool` 判断，几乎零额外开销
- [ ] **C-42**: 代码遵循现有样式（缩进、换行、命名）

## 📖 文档检查
- [ ] **C-43**: `docs/01-整体架构.md` 模块矩阵已新增 Observability、Security、ServiceState 条目，状态标记为 ✅ 已实现
- [ ] **C-44**: 新增 `docs/17-框架可观测性与运维.md`（或等价综合文档），涵盖 Metrics/HealthCheck/RateLimiting/HotReload/ServiceState/CrashReporting
- [ ] **C-45**: 文档中包含每个模块的作用、API 列表、使用示例代码
- [ ] **C-46**: 示例代码中的命名空间、类名与实际代码一致
- [ ] **C-47**: 文档全中文撰写，风格与现有文档一致

## 🔌 非功能性 & 边界条件
- [ ] **C-48**: 故障隔离 —— 单个模块内部异常（如 Metrics 记录失败）不影响 TcpServer 主逻辑
- [ ] **C-49**: 无资源泄漏 —— SemaphoreSlim 被释放，文件 StreamWriter 被 Disposed
- [ ] **C-50**: 高并发测试 —— 16 线程 × 100 万次操作无异常，数据结构最终一致
