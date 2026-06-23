# MMORPG 框架商业化打磨 - Product Requirement Document

## Overview
- **Summary**: 对 MMORPG.Framework 进行商业化级别的打磨，补齐成熟服务端框架应具备但当前尚缺的关键能力：指标监控、健康检查、消息限流、配置热更新、服务状态管理、崩溃收集。本次优化不引入业务逻辑，所有新功能保持与游戏玩法无关的通用定位。
- **Purpose**: 当前框架已具备核心能力（网络、日志、配置、线程调度、消息序列化），但缺乏商业化框架必备的可观测性和鲁棒性工具集——运维人员无法实时了解服务健康状况、无法按会话防刷屏/防攻击、无法在不停服情况下调整配置、也不能在服务遇到未处理异常时收集崩溃现场。
- **Target Users**: 框架层开发者、运维人员、游戏业务层开发者（通过框架 API 间接使用）

## Goals
- **G-1**: 提供统一的可观测指标（Metrics），支持 TPS / 延迟 / 连接数 / 消息量等关键指标的收集与查询。
- **G-2**: 提供健康检查探针（HealthCheck），支持 liveness/readiness 检查接口，便于云部署和容器化运维。
- **G-3**: 实现消息限流（RateLimiting），支持按会话/全局的消息频率限制，防止刷流量攻击。
- **G-4**: 实现配置热更新（HotReload），在不停服情况下重新加载服务器配置并自动应用（日志级别、端口、最大连接数等）。
- **G-5**: 提供服务状态管理（ServiceState），服务可在 Running / Paused / ShuttingDown / Stopped 等状态间流转，业务层可订阅状态变更事件。
- **G-6**: 提供崩溃异常收集与上报（CrashReporting），对未处理异常自动写入日志 / 文件 / 远程上报，且不会破坏服务稳定性。

## Non-Goals
- **不做业务逻辑**：不实现玩家数据、战斗、社交等业务层能力，所有新增模块保持框架通用。
- **不做数据库访问层**：数据持久化（MySQL / Redis）能力延后，本次仅提供框架级连接池基础接口（供后续实现者使用）。
- **不做图形化控制台**：不开发 Web 管理界面，仅提供可调用的编程 API（供后续控制台项目使用）。
- **不做网络加密 / TLS**：传输层加密（TLS/SSL）不在本次范围，后续独立项目处理。
- **不做完整的 Prometheus / Grafana 集成**：本次只暴露可查询的指标对象，具体导出到 Prometheus 留给业务层按需适配。

## Background & Context
当前框架能力总结：

| 模块 | 状态 | 位置 |
|------|------|------|
| **网络层** | ✅ 已实现 | [Network/](file:///g:/QZWCS/MMORPG/src/MMORPG.Framework/Network/Network) |
| **日志系统** | ✅ 已实现 | [Logging/](file:///g:/QZWCS/MMORPG/src/MMORPG.Framework/Logging/Logging) |
| **线程调度** | ✅ 已实现 | [Threading/](file:///g:/QZWCS/MMORPG/src/MMORPG.Framework/Threading/Threading) |
| **配置系统** | ✅ 已实现 | [Configuration/](file:///g:/QZWCS/MMORPG/src/MMORPG.Framework/Configuration/Configuration) |
| **消息序列化** | ✅ 已实现 | [Network/MessageSerializer.cs](file:///g:/QZWCS/MMORPG/src/MMORPG.Framework/Network/MessageSerializer.cs) |
| **消息路由** | ✅ 已实现 | [Network/MessageRouter.cs](file:///g:/QZWCS/MMORPG/src/MMORPG.Framework/Network/MessageRouter.cs) |
| **心跳管理** | ✅ 已实现 | [Network/HeartbeatManager.cs](file:///g:/QZWCS/MMORPG/src/MMORPG.Framework/Network/HeartbeatManager.cs) |
| **指标监控** | ❌ 缺失 | — |
| **健康检查** | ❌ 缺失 | — |
| **消息限流** | ❌ 缺失 | — |
| **配置热更新** | ❌ 缺失 | ConfigurationLoader 目前无自动 reload |
| **服务状态管理** | ❌ 缺失 | TcpServer 目前只有 Start/Stop，没有中间状态 |
| **崩溃收集** | ❌ 缺失 | Logger 仅支持手动记录 |

已有文档参考：
- [01-整体架构.md](file:///g:/QZWCS/MMORPG/docs/01-整体架构.md)
- [16-日志系统.md](file:///g:/QZWCS/MMORPG/docs/16-日志系统.md)
- [05-序列化设计.md](file:///g:/QZWCS/MMORPG/docs/05-序列化设计.md)
- [14-帧率保障.md](file:///g:/QZWCS/MMORPG/docs/14-帧率保障.md)

项目当前测试状态：95/95 测试通过，构建零警告零错误。

## Functional Requirements
- **FR-1**: 指标（Metrics）— 提供 `IMetric` 抽象 + 默认实现 `MetricsCollector`，至少支持计数器（Counter）、量规（Gauge）、直方图（Histogram/延迟统计）三类指标，支持每秒查询 TPS、P50/P95/P99 延迟、当前在线连接数、已处理消息总数、GC 次数、GC 时间等。
- **FR-2**: 指标可订阅 — 业务层可通过事件订阅指标快照，或通过查询 API 读取当前指标值（`double MetricValue(string name)` / `IDictionary<string, double> Snapshot()`）。
- **FR-3**: 健康检查 — 提供 `HealthCheckResult` 枚举（Healthy/Degraded/Unhealthy）+ `HealthCheckStatus` 聚合报告；内置检查点：TCP 监听状态、当前在线率、消息队列积压、内存使用；支持业务层注册自定义检查点。
- **FR-4**: 健康检查暴露方式 — 通过 `Task<string> IHealthCheckService.RenderJsonAsync()` / `RenderTextAsync()` 输出，供 HTTP/控制台调用。
- **FR-5**: 会话级消息限流 — 每个 Session 拥有独立的 RateLimiter，按消息条数/秒限流，超过阈值时可选：1) 丢弃超额消息并记录日志；2) 延迟处理（排入低优先级队列）；3) 直接断开恶意连接。
- **FR-6**: 全局消息限流 — 框架层提供全局每秒消息上限，超过时自动降级（例如将非关键消息排入低优先级队列处理）。
- **FR-7**: 配置热更新 — `ConfigurationLoader` 增加 `EnableFileWatcher(path, intervalMs)` 方法：当配置文件变化时自动重新加载，通过 `event ConfigChanged` 通知业务层；关键配置（如端口、MaxConnections、LogLevel）变化后由框架自动应用到 TcpServer/Logger。
- **FR-8**: 服务状态机 — 提供 `ServiceState`（Stopped → Starting → Running → Pausing → Paused → Stopping → ShuttingDown → Stopped），状态变更事件可订阅，不同状态下消息路由/消息发送行为不同（Paused 时仅接收心跳与系统消息）。
- **FR-9**: 崩溃收集 — 注册 `AppDomain.CurrentDomain.UnhandledException` 和 `TaskScheduler.UnobservedTaskException`，自动将完整的异常（含堆栈、线程、时间、服务状态）写入 `logs/crashes/yyyy-MM-dd-HHmmss.log`，并重试 Flush。
- **FR-10**: 所有新能力可插拔 — 默认禁用，通过初始化配置显式启用。

## Non-Functional Requirements
- **NFR-1**: 框架总性能损耗 < 3%。新增的指标收集、限流检查不能显著增加消息处理路径的开销；在未启用的情况下，不得对现有代码路径产生 measurable overhead。
- **NFR-2**: 线程安全。所有公共 API 必须在多线程场景下安全（生产者/消费者并发）。
- **NFR-3**: 零外部依赖。保持当前框架不引入任何第三方 NuGet 包，只用 .NET 10 BCL。
- **NFR-4**: 中文注释与命名空间规范。所有新增文件遵循现有代码标准（MMORPG.Framework.* 命名空间、详细中文注释、位置占位符 Logger 写法）。
- **NFR-5**: 零警告。新增代码必须使项目仍然 dotnet build 零警告、零错误。
- **NFR-6**: 测试覆盖率。每个新模块需有单元测试；关键路径需有性能基准测试（如 RateLimiter 的 TPS、Metrics 的查询延迟）。总测试数不低于当前 95 项，且全部通过。
- **NFR-7**: 故障隔离。任一功能模块（限流、指标、热更新）遇到内部异常时，不得影响 TcpServer 主逻辑。
- **NFR-8**: 文档对齐。docs/ 目录下新增或更新文档，描述新模块的使用方法与最佳实践。

## Constraints
- **Technical**: .NET 10 / C# 13，禁止引入第三方 NuGet（不使用 Prometheus-net、Polly、Serilog 等）
- **Business**: 保持框架与业务解耦，所有能力以 API 形式提供
- **Dependencies**: 仅依赖现有 Framework 命名空间的模块（Logging、Network、Configuration、Threading）

## Assumptions
- 假设业务开发者会自行将健康检查的 RenderJsonAsync() 输出接入他们的 HTTP 网关（例如 ASP.NET Core minimal API）或运维系统。
- 假设运维侧不会依赖本框架内置的"远程上报协议"——如需将崩溃日志推送到远程运维系统，可通过 Logger.NetworkSink 实现（已存在）。
- 假设配置文件路径可以是绝对路径或相对路径（相对于工作目录），文件为 JSON 格式。
- 假设框架使用者不会同时实例化多个 `MetricsCollector` 全局单例——我们提供 Instance 属性以强制单例。

## Acceptance Criteria

### AC-1: 指标监控可启用且不影响 TPS
- **Given**: 框架已启动，已启用 `MetricsCollector.Enable()` 同时有 100 个活跃会话
- **When**: 进行 10 秒性能基准测试，统计序列化/反序列化处理总 TPS
- **Then**: 相较未启用指标的基准测试，TPS 下降 < 3%，且所有被注册的指标可通过 `Snapshot()` 查询到合理非零值
- **Verification**: `programmatic`
- **Notes**: 测试需要两份 benchmark — 一份启用 Metric，一份未启用，分别跑 10 秒取 TPS 均值

### AC-2: 指标类别覆盖完整
- **Given**: 框架正常运行
- **When**: 调用 `MetricsCollector` 的 `GetRegisteredMetrics()` 查询已注册的指标名
- **Then**: 必须能查询到：`tps.total`、`tps.bytes_tx`、`tps.bytes_rx`、`latency.p50`、`latency.p95`、`latency.p99`、`session.active`、`message.processed_total`、`gc.count`、`gc.time_ms`
- **Verification**: `programmatic`

### AC-3: 健康检查基本功能
- **Given**: 框架正常运行，已注册至少 3 个健康检查项（TCP 监听状态、消息队列积压、内存使用）
- **When**: 调用 `HealthCheckService.CheckHealthAsync()`
- **Then**: 返回 `HealthCheckStatus.Healthy`，并且 RenderJsonAsync() 返回合法 JSON（可被 `JsonDocument.Parse` 解析）
- **Verification**: `programmatic`

### AC-4: 健康检查在异常情况
- **Given**: 框架启动，但我们通过测试 mock 将 TCP 监听标记为 down / 消息队列积压超过阈值 / 内存超过阈值
- **When**: 调用 CheckHealthAsync()
- **Then**: 返回结果的 OverallStatus 为 Degraded 或 Unhealthy，并在 Report 中包含具体失败项
- **Verification**: `programmatic`

### AC-5: 会话级消息限流
- **Given**: 已创建一个测试 Session，RateLimiter 限制为 10 条/秒
- **When**: 在 1 秒内尝试处理 50 条消息
- **Then**: 前 10 条被正常处理，其余被丢弃或延迟；计数器 `droppedMessageCount` 非零且限流日志可通过 Logger 查询到
- **Verification**: `programmatic`

### AC-6: 限流对正常玩家透明
- **Given**: 一个正常发送消息的 Session（每秒 1 条）
- **When**: 持续 10 秒发送消息
- **Then**: 所有消息被正常处理，没有丢弃，RateLimiter 的 `DroppedCount` 保持 0
- **Verification**: `programmatic`

### AC-7: 配置热更新触发与回调
- **Given**: 已调用 `ConfigurationLoader.EnableFileWatcher(path, intervalMs: 500)`，配置文件 `server.json` 当前 `LogLevel = Info`
- **When**: 修改文件使 `LogLevel = Debug`，等待文件监听触发
- **Then**: `ConfigChanged` 事件被触发，`Logger.CurrentLogLevel` 被自动更新为 `Debug`
- **Verification**: `programmatic`

### AC-8: 服务状态机正常流转
- **Given**: 服务处于 `Running` 状态
- **When**: 调用 `PauseAsync()` → `ResumeAsync()` → `StopAsync()`
- **Then**: 每次状态变更都触发了对应的事件，且最终状态为 `Stopped`
- **Verification**: `programmatic`

### AC-9: 崩溃收集写入文件
- **Given**: 框架已初始化，`CrashReporting.Enable(filePathTemplate: "logs/crashes/{0}.log")` 已调用
- **When**: 抛出一个故意的 `throw new InvalidOperationException("test crash")` 在未处理的异步上下文中
- **Then**: 磁盘上存在至少一个新的崩溃日志文件，文件名包含当日时间戳，内容包含堆栈、时间戳
- **Verification**: `programmatic`

### AC-10: 命名空间、命名规范与日志格式一致性（代码可评审）
- **Given**: 新模块代码
- **When**: 审阅
- **Then**: 全部使用 `MMORPG.Framework.*` 命名空间、中文注释、位置占位符（`{0}` 等）的 Logger 调用，无第三方依赖
- **Verification**: `human-judgment`
- **Notes**: 评审者需确保不会引入任何 NuGet 依赖

### AC-11: 文档对齐
- **Given**: 所有代码已实现
- **When**: 检查 docs/
- **Then**: 存在更新后的 `01-整体架构.md` 模块矩阵（增加 Monitoring/Health/RateLimiting 条目），以及新增至少 1 份专门文档（如 `17-框架可观测性与运维.md` 或等价的综合性文档）描述各新模块的用法
- **Verification**: `human-judgment`

## Open Questions
- [ ] **Q1** 框架是否需要"连接池"接口（后续数据库模块使用的 `IDbConnectionPool`）？目前计划延后，但接口现在预留还是完全不做？
- [ ] **Q2** 指标的外部导出格式：我们是否要预定义一种简洁的 Prometheus 文本格式，还是只提供 Snapshot IDictionary？倾向只提供 Snapshot，由业务层按需转换成 Prometheus/JSON 格式。
- [ ] **Q3** 健康检查是否需要提供一个内置的简单 HTTP 服务（例如在独立线程监听 http://localhost:8080/health）？还是只提供 API，由业务层暴露？**倾向于只提供 API，不内置 HTTP**。
- [ ] **Q4** 限流的惩罚策略是否需要可插拔（比如"丢弃""延迟""断开"之外还能由业务层自定义）？
