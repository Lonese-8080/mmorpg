# MMORPG 服务端框架 —— API 参考文档

> **文档编号**: API-001  
> **版本**: v1.1.0  
> **最后更新**: 2026-06-23

---

## ⚠️ 实现状态

**已实现**（v1.1.0 全面对齐）：
- `MMORPG.Framework.Network` — IOCP 网络层（TcpServer、Session、SessionPool、MessageRetryQueue、MessageRouter、MessageSerializer、MessageIds、HeartbeatManager、TlsHelper、Validator、IMessage = Google.Protobuf.IMessage）
- `MMORPG.Framework.Threading` — GameScheduler（主循环调度）、MessageChannel（消息队列）、SnowflakeIdGenerator（雪花 ID）、ThreadSafeCollections、AtomicCounter
- `MMORPG.Framework.Logging` — 结构化日志（Logger、LogLevel、LogEntry、ILogSink、ConsoleSink、FileSink、NetworkSink）
- `MMORPG.Framework.Configuration` — ServerConfig、ConfigurationLoader、ConfigEncryptor、ConfigCenter、FrameworkOptions
- `MMORPG.Framework.Resilience` — ResiliencePipeline、ResiliencePipelineBuilder、RetryPolicy、CircuitBreaker、TimeoutPolicy、ResiliencePolicyRegistry（v2.5.0 新增）
- `MMORPG.Framework.Observability` — MetricsCollector、HealthCheckService、HealthEndpoint、CrashReporting、MemoryMonitor、PrometheusExporter（v2.4.0 新增）
- `MMORPG.Framework.Security` — RateLimiter、ServiceStateManager（v2.4.0 新增）

**规划中（本文档末尾列出）**：
- `MMORPG.Framework.Serialization` — Protobuf 编解码器（**已实现**，由 `Google.Protobuf` 提供，生成代码位于 `Framework.Network.Protobuf` 命名空间）
- `MMORPG.Core.ECS` — World、Entity、Archetype、System 等（未实现）

---

## 目录

1. [Framework.Network](#1-frameworknetwork)
2. [Framework.Threading](#2-frameworkthreading)
3. [Framework.Logging](#3-frameworklogging)
4. [Framework.Configuration](#4-frameworkconfiguration)
5. [Framework.Resilience](#5-frameworkresilience)
6. [Framework.Observability](#6-frameworkobservability)
7. [Framework.Security](#7-frameworksecurity)
8. [未来规划中的 API](#8-未来规划中的-api)

---

## 1. Framework.Network

### 1.1 TcpServer

```csharp
namespace MMORPG.Framework.Network;

/// <summary>
/// TCP 服务器（基于 IOCP，使用 SocketAsyncEventArgs）
///
/// 使用示例：
/// <code>
/// var server = new TcpServer(new TcpServerOptions
/// {
///     Port = 7001,
///     Backlog = 512,
///     MaxConnections = 10000,
///     ReceiveBufferSize = 8 * 1024,
///     HeartbeatTimeoutSeconds = 30
/// });
///
/// server.OnClientConnected += session =>
///     Logger.Info("Network", "玩家连接: ConnectionId={0}", session.ConnectionId);
///
/// server.OnClientDisconnected += (session, reason) =>
///     Logger.Info("Network", "玩家断开: ConnectionId={0}, Reason={1}",
///         session.ConnectionId, reason);
///
/// // 注意：消息路由已在 MessageRouter 中配置，
/// // TcpServer 会自动调用 MessageRouter 处理，无需手动 Route
///
/// server.Start();
/// </code>
/// </summary>
public class TcpServer : IAsyncDisposable, IDisposable
{
    /// <summary>服务器配置（只读访问）</summary>
    public TcpServerOptions Options { get; }

    /// <summary>服务器是否在运行</summary>
    public bool IsRunning { get; }

    /// <summary>服务器是否正在关闭（优雅停机中）</summary>
    public bool IsShuttingDown { get; }

    /// <summary>当前在线连接数</summary>
    public int ConnectionCount { get; }

    // ============================================================
    // 事件
    // ============================================================

    /// <summary>新玩家连接时触发</summary>
    public event Action<Session>? OnClientConnected;

    /// <summary>玩家断开时触发</summary>
    public event Action<Session, string>? OnClientDisconnected;

    /// <summary>收到完整消息时触发（框架内部同时触发 MessageRouter）</summary>
    public event Action<Session, IMessage>? OnMessageReceived;

    // ============================================================
    // 生命周期
    // ============================================================

    /// <summary>构造服务器（配置可选）</summary>
    public TcpServer(TcpServerOptions? options = null);

    /// <summary>同步启动（阻塞至服务器关闭）</summary>
    public void Start();

    /// <summary>异步启动（启动后立即返回，服务器在后台运行）</summary>
    public Task StartAsync();

    /// <summary>停止服务器（三阶段停机：关闭监听 → 等待连接排空 → 强制断开）</summary>
    public Task StopAsync(int waitForPendingMilliseconds = 5000,
        CancellationToken cancellationToken = default);

    /// <summary>同步停止（调用 StopAsync().GetAwaiter().GetResult()）</summary>
    public void Stop();

    // ============================================================
    // 广播与查询
    // ============================================================

    /// <summary>广播消息给所有在线玩家</summary>
    public Task BroadcastAsync(IMessage message);

    /// <summary>根据连接 ID 获取会话（找不到返回 null）</summary>
    public Session? GetSession(long connectionId);

    /// <summary>获取所有在线会话的快照</summary>
    public Session[] GetAllSessions();

    // ============================================================
    // 资源释放
    // ============================================================

    /// <summary>异步资源释放（等待所有连接优雅关闭）</summary>
    public ValueTask DisposeAsync();

    /// <summary>同步资源释放（fire-and-forget）</summary>
    public void Dispose();
}
```

### 1.2 TcpServerOptions

```csharp
namespace MMORPG.Framework.Network;

/// <summary>
/// TCP 服务器配置
/// </summary>
public class TcpServerOptions
{
    // -------------------- 网络基础 --------------------
    /// <summary>监听端口，默认 9000</summary>
    public int Port { get; set; } = 9000;

    /// <summary>连接队列大小（Listen backlog），默认 200</summary>
    public int Backlog { get; set; } = 200;

    /// <summary>最大连接数，默认 10000</summary>
    public int MaxConnections { get; set; } = 10000;

    /// <summary>接收缓冲区大小（字节），默认 8 KB</summary>
    public int ReceiveBufferSize { get; set; } = 8 * 1024;

    /// <summary>发送缓冲区大小（字节），默认 8 KB</summary>
    public int SendBufferSize { get; set; } = 8 * 1024;

    // -------------------- 心跳 --------------------
    /// <summary>心跳检查间隔（秒），默认 10</summary>
    public int HeartbeatCheckIntervalSeconds { get; set; } = 10;

    /// <summary>心跳超时（秒），超过此时间无数据则断开，默认 30</summary>
    public int HeartbeatTimeoutSeconds { get; set; } = 30;

    // -------------------- TLS --------------------
    /// <summary>启用 TLS 加密连接</summary>
    public bool EnableTls { get; set; } = false;

    /// <summary>证书文件路径（pfx/pem）</summary>
    public string? CertificatePath { get; set; }

    /// <summary>证书密码</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>TLS 协议版本，默认 TLS 1.2</summary>
    public SslProtocols TlsProtocol { get; set; } = SslProtocols.Tls12;

    /// <summary>要求客户端提供证书（双向认证）</summary>
    public bool RequireClientCertificate { get; set; } = false;

    // -------------------- Session 对象池 --------------------
    /// <summary>启用 Session 对象池（减少 GC）</summary>
    public bool EnableSessionPool { get; set; } = false;

    /// <summary>池最小预热数量，默认 50</summary>
    public int SessionPoolMinPoolSize { get; set; } = 50;

    /// <summary>池最大容量，默认 1000</summary>
    public int SessionPoolMaxPoolSize { get; set; } = 1000;

    /// <summary>池最大空闲容量，默认 500</summary>
    public int SessionPoolMaxIdleCapacity { get; set; } = 500;

    // -------------------- 消息重试 --------------------
    /// <summary>消息发送失败重试次数，默认 3</summary>
    public int MessageRetryCount { get; set; } = 3;

    /// <summary>消息重试基础间隔（毫秒），默认 100</summary>
    public int MessageRetryIntervalMs { get; set; } = 100;

    // -------------------- 参数校验 --------------------
    /// <summary>
    /// 校验配置参数的有效性
    ///
    /// ⚠️ 在 TcpServer.Start() 中会自动调用此方法
    /// 如果参数无效，服务器启动会失败并记录错误日志
    /// </summary>
    /// <returns>(isValid, errorMessage)</returns>
    public (bool Valid, string? Error) Validate();
}
```

### 1.3 Session

```csharp
namespace MMORPG.Framework.Network;

/// <summary>
/// 玩家会话 — 表示一个已建立的客户端连接
///
/// 生命周期：连接建立 → 收发消息 → 断开/超时/优雅关闭
/// 线程安全：发送操作内部使用 Channel 队列化；接收操作由 IOCP 线程处理
/// </summary>
public class Session
{
    /// <summary>连接 ID（服务器自增唯一）</summary>
    public long ConnectionId { get; }

    /// <summary>玩家 ID（业务层设置，未登录时为 0）</summary>
    public long PlayerId { get; set; }

    /// <summary>远程端点（IP 和端口），可能为 null</summary>
    public IPEndPoint? RemoteEndPoint { get; private set; }

    /// <summary>是否仍处于连接状态</summary>
    public bool IsConnected { get; }

    /// <summary>最后心跳时间（UTC），每次收到数据时更新</summary>
    public DateTime LastHeartbeat { get; set; }

    /// <summary>消息重试队列（发送失败时自动入队重试，可为 null）</summary>
    public MessageRetryQueue? RetryQueue { get; set; }

    // ============================================================
    // 生命周期
    // ============================================================

    /// <summary>开始接收数据（内部由 TcpServer 调用）</summary>
    public void StartReceive();

    // ============================================================
    // 发送
    // ============================================================

    /// <summary>
    /// 发送消息（异步，线程安全）
    /// </summary>
    /// <returns>true 表示发送成功（进入发送队列），false 表示连接已断开</returns>
    public async ValueTask<bool> SendAsync(IMessage message);

    /// <summary>发送原始字节（用于心跳等框架内部消息）</summary>
    public async ValueTask SendRawAsync(byte[] data);

    /// <summary>设置会话级消息限流</summary>
    public void SetSessionRateLimit(long maxMessagesPerSecond);

    // ============================================================
    // 断开
    // ============================================================

    /// <summary>同步断开连接</summary>
    public void Disconnect(string reason = "未知原因");

    /// <summary>异步断开连接（等待 TLS 接收任务完成）</summary>
    public Task DisconnectAsync(string reason = "未知原因");

    // ============================================================
    // 连接池复用（仅供 SessionPool 使用）
    // ============================================================

    /// <summary>重置 Session 以复用（连接池归还时调用）</summary>
    public void ResetWith(Socket newSocket,
        TcpServerOptions? options = null,
        Action<Session, IMessage>? onMessageReceived = null,
        Action<Session, string>? onDisconnected = null);

    /// <summary>清理 Socket 引用但保留缓冲区资源（归还池时调用）</summary>
    public void ClearSocket();

    /// <summary>释放所有资源（池销毁时调用）</summary>
    public void Dispose();
}
```

### 1.4 MessageRouter

```csharp
namespace MMORPG.Framework.Network;

/// <summary>
/// 消息路由器（单例）
///
/// 职责：
/// 1. 维护消息 ID → 处理函数的映射（支持带弹性策略）
/// 2. 执行消息路由（含异常处理，不会因单条消息异常断开连接）
/// 3. 支持全局消息限流（令牌桶）
/// 4. 支持消息数据校验（IValidatableObject）
/// 5. 捕获 BrokenCircuitException（熔断）→ 返回 503
/// 6. 捕获 TimeoutRejectedException（超时）→ 返回 504
///
/// 使用示例：
/// <code>
/// // 注册业务消息处理器（泛型接口版）
/// MessageRouter.Instance.RegisterHandler(new LoginHandler());
///
/// // 注册带弹性策略的处理器
/// var dbPipeline = new ResiliencePipelineBuilder()
///     .WithTimeout(5000)
///     .WithRetry(new RetryOptions { MaxRetryCount = 3 })
///     .Build();
///
/// MessageRouter.Instance.RegisterHandler(
///     (uint)MessageIds.C2S_Login,
///     async (session, msg) => { /* 处理登录 */ },
///     pipeline: dbPipeline);
///
/// // 设置全局消息限流（每秒 10000 条）
/// MessageRouter.Instance.SetGlobalRateLimit(10000);
/// </code>
/// </summary>
public class MessageRouter
{
    /// <summary>单例实例</summary>
    public static MessageRouter Instance { get; }

    // ============================================================
    // 注册 — 6 个重载
    // ============================================================

    /// <summary>注册泛型消息处理器（不带弹性策略）</summary>
    public void RegisterHandler<T>(IMessageHandler<T> handler)
        where T : class, IMessage;

    /// <summary>注册泛型消息处理器（带弹性策略）</summary>
    public void RegisterHandler<T>(IMessageHandler<T> handler, ResiliencePipeline? pipeline)
        where T : class, IMessage;

    /// <summary>注册委托处理函数（Func 版，不带策略）</summary>
    public void RegisterHandler(uint messageId,
        Func<Session, IMessage, Task> handler);

    /// <summary>注册委托处理函数（Func 版，带弹性策略）</summary>
    public void RegisterHandler(uint messageId,
        Func<Session, IMessage, Task> handler,
        ResiliencePipeline? pipeline);

    /// <summary>注册委托处理函数（同步 Action 版，不带策略）</summary>
    public void RegisterHandler(uint messageId,
        Action<Session, IMessage> handler);

    /// <summary>注册委托处理函数（同步 Action 版，带弹性策略）</summary>
    public void RegisterHandler(uint messageId,
        Action<Session, IMessage> handler,
        ResiliencePipeline? pipeline);

    /// <summary>移除指定消息 ID 的处理器</summary>
    public bool UnregisterHandler(uint messageId);

    // ============================================================
    // 全局限流
    // ============================================================

    /// <summary>设置全局消息限流（令牌桶）</summary>
    public void SetGlobalRateLimit(long maxMessagesPerSecond);

    // ============================================================
    // 路由
    // ============================================================

    /// <summary>路由消息（异步，找到对应处理器并执行）</summary>
    public async Task RouteAsync(Session session, IMessage message);

    /// <summary>获取已注册的消息 ID 数组</summary>
    public uint[] GetRegisteredMessageIds();
}

/// <summary>
/// 泛型消息处理器接口
/// </summary>
/// <typeparam name="T">处理的消息类型</typeparam>
public interface IMessageHandler<T> where T : IMessage
{
    Task HandleAsync(Session session, T message);
}

/// <summary>
/// 熔断异常（由 CircuitBreaker 在 Open 状态时抛出）
/// </summary>
public class BrokenCircuitException : Exception
{
    public BrokenCircuitException(string message) : base(message) { }
    public BrokenCircuitException(string message, Exception? innerException)
        : base(message, innerException) { }
}

/// <summary>
/// 超时异常（由 TimeoutPolicy 在超时时抛出）
/// </summary>
public class TimeoutRejectedException : Exception
{
    public TimeoutRejectedException(string message) : base(message) { }
    public TimeoutRejectedException(string message, Exception? inner)
        : base(message, inner) { }
}
```

### 1.5 MessageIds

```csharp
namespace MMORPG.Framework.Network;

/// <summary>
/// 消息 ID 常量（uint 类型）
///
/// 分配规则：
/// - 0x00000001 ~ 0x00000FFF：框架层（已实现）
/// - 0x00001000 ~ 0xFFFFFFFF：游戏业务（未实现，业务层自行分配）
/// </summary>
public static class MessageIds
{
    // 框架层 — 已实现
    public const uint C2S_Login          = 0x00000001;
    public const uint S2C_LoginResult    = 0x00000002;
    public const uint C2S_Heartbeat      = 0x00000003;
    public const uint S2C_Heartbeat      = 0x00000004;
    public const uint C2S_EnterWorld     = 0x00000005;
    public const uint S2C_EnterWorld     = 0x00000006;
    public const uint S2C_ServerNotice   = 0x00000100;
    public const uint S2C_Error          = 0x00000101;

    /// <summary>判断是否为框架层消息</summary>
    public static bool IsFrameworkMessage(uint messageId);

    /// <summary>判断是否为游戏业务消息</summary>
    public static bool IsGameMessage(uint messageId);

    /// <summary>获取消息 ID 的描述文本（用于日志）</summary>
    public static string GetDescription(uint messageId);
}
```

### 1.6 HeartbeatManager

```csharp
namespace MMORPG.Framework.Network;

/// <summary>
/// 心跳管理器（由 TcpServer 内部使用）
///
/// - 定期检查所有 Session 的 LastHeartbeat
/// - 超过 HeartbeatTimeoutSeconds 无数据则主动断开
/// - 使用 System.Threading.Timer 实现定时任务
/// </summary>
public class HeartbeatManager
{
    /// <summary>构造心跳管理器（会话字典来自 TcpServer）</summary>
    public HeartbeatManager(
        ConcurrentDictionary<long, Session> sessions,
        TcpServerOptions options);

    /// <summary>启动心跳检查</summary>
    public void Start();

    /// <summary>停止心跳检查</summary>
    public void Stop();
}
```

### 1.6 IMessage（Google Protobuf）

```csharp
/// 框架消息类由 protos/*.proto 定义，由 Grpc.Tools 在编译时
/// 自动生成。每个消息类实现 Google.Protobuf.IMessage<T> 接口，
/// 框架将此接口简称为 IMessage。
///
/// 典型的消息定义（在 .proto 文件中）：
///
/// <code>
/// // protos/common.proto
/// message C2S_Login {
///   string player_name = 1;
///   int32  version     = 2;
/// }
///
/// message S2C_LoginResult {
///   bool   success = 1;
///   string reason  = 2;
/// }
/// </code>
///
/// 由 protoc 生成的 C# 类：
///
/// <code>
/// // MMORPG.Framework/Network/Protobuf/Common.cs（自动生成）
/// using pb = Google.Protobuf;
///
/// public sealed partial class C2S_Login
///     : pb::IMessage<C2S_Login>
/// {
///     public string PlayerName { get; set; }
///     public int Version { get; set; }
///     // ...
/// }
/// </code>
///
/// 用法：
///
/// <code>
/// // 1. 在 MessageId.cs 中分配消息 ID
/// //    public const uint C2S_Login = 0x00000001;
///
/// // 2. 在 MessageSerializer 中注册（通常在框架启动时自动注册）
/// //    MessageSerializer.Register<C2S_Login>(MessageIds.C2S_Login);
///
/// // 3. 构造消息
/// var login = new C2S_Login { PlayerName = "张三", Version = 100 };
///
/// // 4. 发送
/// session.Send(login);
/// </code>

```

### 1.8 MessageSerializer

```csharp
namespace MMORPG.Framework.Network;

/// <summary>
/// 消息序列化器（Google Protobuf 实现）
///
/// TCP 消息包格式：
/// ┌───────────────────────────────────────────────────────┐
/// │  消息体长度(4B, int, 小端) │  消息ID(4B, uint) │  消息体(NB) │
/// └───────────────────────────────────────────────────────┘
/// 总长度 = 8 + 消息体长度
/// </summary>
public static class MessageSerializer
{
    /// <summary>消息头大小（长度 4 + 类型 4 = 8 字节）</summary>
    public const int HeaderSize = 8;

    /// <summary>最大消息体大小（1 MB），用于防攻击</summary>
    public const int MaxBodySize = 1024 * 1024;

    /// <summary>注册自定义消息类型</summary>
    public static void Register<T>(uint messageId)
        where T : IMessage, new();

    /// <summary>序列化消息为完整 TCP 数据包（含消息头）</summary>
    public static byte[] Serialize(IMessage message);

    /// <summary>反序列化完整 TCP 数据包 → 消息对象</summary>
    public static IMessage Deserialize(byte[] data);

    /// <summary>
    /// 仅解析消息头（用于粘包处理）
    /// </summary>
    /// <param name="data">数据</param>
    /// <param name="offset">解析起点</param>
    /// <param name="bodyLength">输出：消息体长度</param>
    /// <param name="messageId">输出：消息 ID</param>
    /// <returns>true 表示解析成功</returns>
    public static bool TryParseHeader(
        byte[] data, int offset,
        out int bodyLength, out uint messageId);
}
```

### 1.9 SessionPool

```csharp
namespace MMORPG.Framework.Network;

/// <summary>
/// Session 对象池
///
/// 功能：
/// 1. 预热阶段创建 MinPoolSize 个 Session 并分配 ConnectionId
/// 2. Rent：从池中借出 Session，复用已有缓冲区
/// 3. Return：归还 Session，ClearSocket 后重置状态
/// 4. CleanupExpired：清理超出 MaxIdleCapacity 的空闲 Session
/// </summary>
public class SessionPool
{
    /// <summary>当前空闲 Session 数量</summary>
    public int IdleCount { get; }

    /// <summary>当前活跃（已借出）Session 数量</summary>
    public int ActiveCount { get; }

    /// <summary>池中总 Session 数量（Idle + Active）</summary>
    public int TotalCount { get; }

    /// <summary>池是否已满（ TotalCount >= MaxPoolSize）</summary>
    public bool IsFull { get; }

    public SessionPool(SessionPoolOptions? options,
        TcpServerOptions? serverOptions,
        Action<Session, IMessage>? onMessageReceived,
        Action<Session, string>? onDisconnected,
        Func<Socket, Session>? sessionFactory = null);

    /// <summary>预热：创建 MinPoolSize 个 Session 并分配 ConnectionId</summary>
    public void Warmup();

    /// <summary>从池中借出一个 Session（用于处理新连接）</summary>
    public Session Rent(Socket clientSocket);

    /// <summary>归还 Session 到池中</summary>
    public void Return(Session session);

    /// <summary>清理超出 MaxIdleCapacity 的空闲 Session</summary>
    public void CleanupExpired();

    /// <summary>获取统计信息（格式：Idle=X, Active=Y, Total=Z）</summary>
    public string GetStats();

    public void Dispose();
}

public class SessionPoolOptions
{
    public int MinPoolSize { get; set; } = 50;
    public int MaxPoolSize { get; set; } = 1000;
    public int MaxIdleCapacity { get; set; } = 500;
}
```

### 1.10 MessageRetryQueue

```csharp
namespace MMORPG.Framework.Network;

/// <summary>
/// 消息自动重试队列
///
/// 功能：
/// 1. 发送失败时将消息（含字节数据）自动入队
/// 2. 后台轮询使用指数退避重试
/// 3. 重试次数超限后移入死信队列（DeadLetterItem）
/// 4. Session 已断开时不重试，直接丢弃
/// </summary>
public class MessageRetryQueue
{
    /// <summary>当前等待重试的消息数</summary>
    public int PendingCount { get; }

    /// <summary>当前死信队列中的消息数</summary>
    public int DeadLetterCount { get; }

    /// <summary>累计重试总次数</summary>
    public long TotalRetryAttempts { get; }

    /// <summary>累计成功重试次数</summary>
    public long SuccessfulRetries { get; }

    public MessageRetryQueue(MessageRetryOptions? options = null);
    public void Start();
    public void Stop();

    /// <summary>将发送失败的消息加入重试队列</summary>
    public void EnqueueForRetry(Session? session, byte[] data, string failureReason);

    /// <summary>批量取出死信队列中的消息（最多 maxCount 条）</summary>
    public List<DeadLetterItem> DrainDeadLetter(int maxCount = 100);

    public string GetStats();
}

public class MessageRetryOptions
{
    public int MaxRetryCount { get; set; } = 3;
    public int BaseIntervalMs { get; set; } = 100;
    public int MaxIntervalMs { get; set; } = 5000;
    public int MaxDeadLetterCapacity { get; set; } = 10000;
}

public class DeadLetterItem
{
    public byte[] Data { get; }
    public string FailureReason { get; }
    public int RetryCount { get; }
    public DateTime FirstAttemptAt { get; }
    public DateTime DeadLetteredAt { get; }
}
```

### 1.11 TlsHelper

```csharp
namespace MMORPG.Framework.Network;

/// <summary>
/// TLS 辅助工具
///
/// 功能：
/// 1. 加载服务器证书（pfx 文件）
/// 2. 验证 TLS 配置有效性
/// 3. 执行 TLS 握手
/// 4. 证书热加载（FileSystemWatcher 监听文件变更）
/// </summary>
public static class TlsHelper
{
    /// <summary>证书热加载触发事件（参数：新证书对象）</summary>
    public static event EventHandler<X509Certificate2>? CertificateUpdated;

    /// <summary>从 TcpServerOptions 加载证书</summary>
    public static X509Certificate2? LoadCertificate(TcpServerOptions options);

    /// <summary>启用证书热加载（FileSystemWatcher 监听证书文件变更）</summary>
    public static void EnableCertificateHotReload(TcpServerOptions options);

    /// <summary>禁用证书热加载</summary>
    public static void DisableCertificateHotReload();

    /// <summary>获取当前活动的证书</summary>
    public static X509Certificate2? GetCurrentCertificate();

    /// <summary>验证 TLS 配置是否有效</summary>
    public static (bool Valid, string? ErrorMessage) ValidateTlsConfiguration(TcpServerOptions options);

    /// <summary>执行 TLS 握手（异步）</summary>
    public static async Task<SslStream?> PerformHandshakeAsync(
        Socket socket,
        X509Certificate2 certificate,
        TcpServerOptions options,
        CancellationToken cancellationToken = default);
}
```

### 1.12 Validator

```csharp
namespace MMORPG.Framework.Network;

/// <summary>
/// 数据校验工具
///
/// 使用反射查找带有校验属性（Required/StringLength/Range）的属性，
/// 在 MessageRouter.RouteAsync 处理消息前对消息数据做预校验。
/// </summary>
public static class Validator
{
    /// <summary>校验对象（返回所有校验结果）</summary>
    public static List<ValidationResult> Validate(object obj);

    /// <summary>校验对象（遇第一个错误即返回）</summary>
    public static bool TryValidate(object obj, out string? errorMessage);
}

public class ValidationResult
{
    public bool IsValid { get; }
    public string? ErrorMessage { get; }
    public string MemberName { get; }
}

public abstract class ValidationAttribute : Attribute
{
    public abstract bool IsValid(object? value);
}

public class RequiredAttribute : ValidationAttribute { }
public class StringLengthAttribute : ValidationAttribute
{
    public int MaxLength { get; set; }
    public override bool IsValid(object? value);
}
public class RangeAttribute : ValidationAttribute
{
    public double Min { get; set; }
    public double Max { get; set; }
    public override bool IsValid(object? value);
}

---

## 2. Framework.Threading

### 2.1 GameScheduler

```csharp
namespace MMORPG.Framework.Threading;

/// <summary>
/// 游戏主循环调度器
///
/// - 以固定帧率（TargetFps）驱动帧更新
/// - 每帧触发 OnFrameStart / OnUpdate / OnFrameEnd 事件
/// - 统计当前 FPS（每秒更新一次）
/// - 帧超时警告（当帧处理时间超过目标帧时间的 2 倍时）
///
/// 使用示例：
/// <code>
/// var scheduler = new GameScheduler(20); // 20 FPS
///
/// scheduler.OnFrameStart += args => ProcessNetworkMessages();
/// scheduler.OnUpdate     += args => ProcessGameLogic();
/// scheduler.OnFrameEnd   += args => FlushSendQueue();
///
/// // 阻塞当前线程运行主循环（放在后台线程也可以）
/// scheduler.Start();
/// </code>
/// </summary>
public class GameScheduler
{
    /// <summary>目标帧率（构造时设置）</summary>
    public int TargetFps { get; }

    /// <summary>当前实际 FPS（每秒更新一次）</summary>
    public double CurrentFps { get; }

    /// <summary>当前帧号（从 0 开始递增）</summary>
    public long FrameNumber { get; }

    /// <summary>是否正在运行</summary>
    public bool IsRunning { get; }

    /// <summary>启动时间（UTC）</summary>
    public DateTime StartTime { get; }

    /// <summary>运行时间（秒，Stopwatch 精确测量）</summary>
    public double UptimeSeconds { get; }

    // ============================================================
    // 事件
    // ============================================================

    /// <summary>帧开始事件（参数：帧号、delta time、帧处理时间 ms）</summary>
    public event Action<FrameEventArgs>? OnFrameStart;

    /// <summary>主逻辑事件（帧开始后、帧结束前）</summary>
    public event Action<FrameEventArgs>? OnUpdate;

    /// <summary>帧结束事件</summary>
    public event Action<FrameEventArgs>? OnFrameEnd;

    // ============================================================
    // 构造与生命周期
    // ============================================================

    /// <summary>创建调度器（指定目标帧率，默认 20 FPS）</summary>
    public GameScheduler(int targetFps = 20);

    /// <summary>创建调度器（自定义配置）</summary>
    public GameScheduler(GameSchedulerOptions options);

    /// <summary>启动主循环（阻塞当前线程）</summary>
    public void Start();

    /// <summary>请求停止（在下一帧后结束，非阻塞）</summary>
    public void Stop();
}

/// <summary>
/// 调度器配置
/// </summary>
public class GameSchedulerOptions
{
    /// <summary>目标帧率（默认 20）</summary>
    public int TargetFps { get; set; } = 20;

    /// <summary>启用 FPS 统计（默认启用）</summary>
    public bool EnableFpsStats { get; set; } = true;

    /// <summary>启用帧时间统计（默认启用）</summary>
    public bool EnableFrameTimeStats { get; set; } = true;

    /// <summary>
    /// v2.9.0 新增：帧时间统计窗口大小（用于计算平均/最大/最小帧时间）
    /// 范围 1-10000，默认 100。60 FPS 下 1000 帧 ≈ 16.6 秒历史。
    /// </summary>
    public int FrameTimeWindowSize { get; set; } = 100;
}

/// <summary>
/// 帧事件参数
/// </summary>
public readonly struct FrameEventArgs
{
    public long FrameNumber { get; }
    public float DeltaTime { get; }     // 秒
    public double FrameTimeMs { get; }   // 毫秒
}

/// <summary>
/// GameScheduler 扩展方法
/// </summary>
public static class GameSchedulerExtensions
{
    /// <summary>在独立线程中启动主循环并返回 Task</summary>
    public static Task StartAsync(this GameScheduler scheduler);

    /// <summary>简写：注册一个每帧执行的回调</summary>
    public static void RegisterUpdate(this GameScheduler scheduler,
        Action<FrameEventArgs> action);
}
```

### 2.2 MessageChannel<T>

```csharp
namespace MMORPG.Framework.Threading;

/// <summary>
/// 消息通道 — 线程安全的无锁消息队列（基于 Channel<T>）
///
/// - 多生产者（多个 IO 线程写入）
/// - 单消费者（主线程每帧 DrainAll）
/// - 有界队列：满时自动丢弃最旧的消息（防拥塞）
/// - 无界队列：任意增长（慎用）
///
/// 使用示例：
/// <code>
/// var channel = new MessageChannel&lt;string&gt;(capacity: 10000);
///
/// // IO 线程写入
/// channel.Write("some-message");
///
/// // 主线程每帧清空并处理
/// foreach (var msg in channel.DrainAll())
/// {
///     HandleMessage(msg);
/// }
/// </code>
/// </summary>
/// <typeparam name="T">消息元素类型</typeparam>
public class MessageChannel<T>
{
    /// <summary>是否为空（使用 Interlocked 精确计数）</summary>
    public bool IsEmpty { get; }

    /// <summary>当前队列中的消息数</summary>
    public int Count { get; }

    /// <summary>队列容量（0 = 无界）</summary>
    public int Capacity { get; }

    // ============================================================
    // 写入
    // ============================================================

    /// <summary>同步写入一条消息（线程安全）</summary>
    /// <returns>true 表示成功写入</returns>
    public bool Write(T item);

    /// <summary>异步写入（队列满时可等待），返回 true 表示成功写入</summary>
    public ValueTask<bool> WriteAsync(T item,
        CancellationToken cancellationToken = default);

    /// <summary>批量写入（返回成功条数）</summary>
    public int WriteRange(IEnumerable<T> items);

    // ============================================================
    // 读取
    // ============================================================

    /// <summary>尝试读取一条消息（非阻塞）</summary>
    public bool TryRead(out T? item);

    /// <summary>取出队列中所有消息（一次性清空，返回枚举）</summary>
    public IEnumerable<T> DrainAll();

    /// <summary>最多读取 maxCount 条消息（返回列表）</summary>
    public List<T> DrainUpTo(int maxCount);

    /// <summary>异步读取一条（等待直到有消息）</summary>
    public ValueTask<T> ReadAsync(
        CancellationToken cancellationToken = default);

    // ============================================================
    // 生命周期
    // ============================================================

    /// <summary>标记不再接收新消息（已在队列中的仍可读）</summary>
    public void Complete();

    /// <summary>清空并丢弃所有消息</summary>
    public void Clear();
}

/// <summary>
/// MessageChannel 工厂
/// </summary>
public static class MessageChannel
{
    /// <summary>创建无界消息队列</summary>
    public static MessageChannel<T> CreateUnbounded<T>();

    /// <summary>创建有界消息队列（满时丢弃最旧）</summary>
    public static MessageChannel<T> CreateBounded<T>(int capacity);
}
```

### 2.3 SnowflakeIdGenerator

```csharp
namespace MMORPG.Framework.Threading;

/// <summary>
/// 雪花算法 ID 生成器
///
/// ID 结构（64 位）：
/// - 1 位符号位（恒为 0）
/// - 41 位时间戳（毫秒精度，起始时间可配置）
/// - 10 位工作节点 ID（workerId，分布式部署时每个节点唯一）
/// - 12 位序列号（同一毫秒内自增，4096 个/毫秒）
///
/// 线程安全：使用 lock 保证同一毫秒内的原子操作
/// 时间源（v2.9.0）：默认 <see cref="TimeProvider.System"/>，构造函数支持注入以方便测试中模拟时间。
/// </summary>
public class SnowflakeIdGenerator
{
    /// <summary>起始时间（UTC）</summary>
    public DateTime StartTime { get; }

    /// <summary>本机工作节点 ID（0 ~ 1023）</summary>
    public int WorkerId { get; }

    /// <summary>已生成的 ID 总数（统计用）</summary>
    public long GeneratedCount { get; private set; }

    /// <summary>构造：指定 workerId（默认 0）</summary>
    public SnowflakeIdGenerator(int workerId = 0, TimeProvider? timeProvider = null);

    /// <summary>构造：自定义配置 + 可选时间提供者</summary>
    public SnowflakeIdGenerator(SnowflakeIdOptions options, TimeProvider? timeProvider = null);

    /// <summary>生成一个唯一 ID</summary>
    public long NewId();

    /// <summary>批量生成 count 个 ID</summary>
    public long[] NewIds(int count);

    /// <summary>从 ID 解析出时间戳</summary>
    public DateTime GetTimestampFromId(long id);

    /// <summary>从 ID 解析出工作节点 ID</summary>
    public int GetWorkerIdFromId(long id);

    /// <summary>从 ID 解析出序列号</summary>
    public long GetSequenceFromId(long id);

    /// <summary>解析 ID 的所有组成部分</summary>
    public (DateTime Timestamp, int WorkerId, long Sequence) ParseId(long id);
}

/// <summary>
/// 雪花算法配置
/// </summary>
public class SnowflakeIdOptions
{
    /// <summary>起始时间（UTC，默认 2024-01-01 00:00:00）</summary>
    public DateTime StartTime { get; set; } =
        new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>工作节点 ID（默认 0）</summary>
    public int WorkerId { get; set; } = 0;

    /// <summary>工作节点 ID 位数（默认 10）</summary>
    public int WorkerIdBits { get; set; } = 10;

    /// <summary>序列号位数（默认 12）</summary>
    public int SequenceBits { get; set; } = 12;
}

/// <summary>
/// 默认生成器快捷访问
/// </summary>
public static class IdGenerator
{
    /// <summary>使用默认 SnowflakeIdGenerator(0) 生成一个 ID</summary>
    public static long NewId();

    /// <summary>批量生成</summary>
    public static long[] NewIds(int count);
}
```

### 2.4 ThreadSafeCollections

```csharp
namespace MMORPG.Framework.Threading;

/// <summary>
/// 线程安全字典（扩展自 ConcurrentDictionary，简化 API）
/// </summary>
public class ConcurrentDictionary<TKey, TValue> :
    System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>
    where TKey : notnull
{
    /// <summary>获取或创建一个项（工厂方法）</summary>
    public new TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory);
}

/// <summary>
/// 线程安全的集合管理（Session 字典 + 扩展能力）
///
/// 通常由框架内部使用，业务层可自行实例化作为自定义集合容器。
/// </summary>
public class ThreadSafeCollections
{
    /// <summary>当前在线 Session 数</summary>
    public int SessionCount { get; }

    /// <summary>添加/更新一个 Session（使用 long 连接 ID 作为键）</summary>
    public void AddOrUpdateSession(long connectionId, object session);

    /// <summary>移除一个 Session</summary>
    public bool TryRemoveSession(long connectionId);

    /// <summary>尝试获取 Session</summary>
    public bool TryGetSession(long connectionId, out object? session);

    /// <summary>获取所有 Session 的快照（创建新数组）</summary>
    public object[] GetAllSessions();
}
```

### 2.5 AtomicCounter

```csharp
namespace MMORPG.Framework.Threading;

/// <summary>
/// 原子计数器（基于 Interlocked，无锁）
///
/// 使用场景：在线人数统计、消息计数、性能监控
/// </summary>
public class AtomicCounter
{
    /// <summary>当前值</summary>
    public long Value => Interlocked.Read(ref _value);

    /// <summary>自增 +1，返回自增后的值</summary>
    public long Increment();

    /// <summary>自减 -1，返回自减后的值</summary>
    public long Decrement();

    /// <summary>增加指定值</summary>
    public long Add(long value);

    /// <summary>设置新值，返回旧值</summary>
    public long Set(long newValue);

    /// <summary>重置为 0，返回旧值</summary>
    public long Reset();
}
```

---

## 3. Framework.Logging

### 3.1 Logger

```csharp
namespace MMORPG.Framework.Logging;

/// <summary>
/// 结构化日志记录器（静态类）
///
/// 特性：
/// - 模板字符串格式（类似 Microsoft.Extensions.Logging，第一个参数为 source 模块名）
/// - 高性能异步写入（内部使用 Channel 队列化，后台线程写入）
/// - 支持 Console 和 File 两种输出目标
/// - 不会因日志异常使程序崩溃（写入异常被内部捕获）
///
/// 使用示例：
/// <code>
/// // 初始化（程序入口处调用一次）
/// Logger.Initialize(new LogOptions
/// {
///     MinLevel = LogLevel.Info,
///     EnableConsole = true,
///     EnableFile = true,
///     LogDirectory = "logs",
///     RetentionDays = 7
/// });
///
/// Logger.Info("Network", "服务器启动完成，监听端口: {0}", 9000);
/// Logger.Warning("Network", "连接数已达上限: {0}", 10000);
/// Logger.Error("Database", ex, "数据库连接失败，将在 {0} 秒后重试", 5);
///
/// // 关闭（程序退出前）
/// await Logger.ShutdownAsync();
/// </code>
/// </summary>
public static class Logger
{
    /// <summary>初始化（调用一次即可，重复调用安全）</summary>
    public static void Initialize(LogOptions options);

    /// <summary>关闭日志系统（异步，等待队列写入完成）</summary>
    public static async Task ShutdownAsync();

    // ============================================================
    // 各等级日志
    // ============================================================

    /// <summary>调试日志（条件编译：DEBUG）</summary>
    [Conditional("DEBUG")]
    public static void Debug(string source, string message, params object[] args);

    /// <summary>信息日志</summary>
    public static void Info(string source, string message, params object[] args);

    /// <summary>警告日志</summary>
    public static void Warning(string source, string message, params object[] args);

    /// <summary>错误日志（带异常）</summary>
    public static void Error(string source, Exception? exception,
        string message, params object[] args);

    /// <summary>错误日志（无异常）</summary>
    public static void Error(string source, string message, params object[] args);

    /// <summary>致命错误日志（带异常）</summary>
    public static void Fatal(string source, Exception? exception,
        string message, params object[] args);

    /// <summary>致命错误日志（无异常）</summary>
    public static void Fatal(string source, string message, params object[] args);
}

/// <summary>
/// 日志等级
/// </summary>
public enum LogLevel
{
    Verbose = 0,
    Debug   = 1,
    Info    = 2,
    Warning = 3,
    Error   = 4,
    Fatal   = 5,
}

/// <summary>
/// 日志配置
/// </summary>
public class LogOptions
{
    /// <summary>最低日志级别（低于此级别的消息被丢弃）</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Debug;

    /// <summary>是否启用控制台输出</summary>
    public bool EnableConsole { get; set; } = true;

    /// <summary>是否启用文件输出</summary>
    public bool EnableFile { get; set; } = true;

    /// <summary>日志目录</summary>
    public string LogDirectory { get; set; } = "logs";

    /// <summary>单文件最大大小（字节，默认 10 MB）</summary>
    public long MaxFileSize { get; set; } = 10 * 1024 * 1024;

    /// <summary>文件保留天数（默认 30 天）</summary>
    public int RetentionDays { get; set; } = 30;
}

/// <summary>
/// 追踪上下文（用于在异步/多线程场景下关联同一次操作的日志）
/// </summary>
public class TraceContext : IDisposable
{
    public static TraceContext? Current { get; }
    public string TraceId { get; private set; } = string.Empty;

    public static TraceContext Create(string? traceId = null);
    public TraceContext CreateChild();
    public static void Clear();
    public void Dispose();
}

/// <summary>
/// 玩家上下文（用于把同一次玩家操作的日志关联到 PlayerId/SessionId）
/// </summary>
public class PlayerContext : IDisposable
{
    public static PlayerContext? Current { get; }
    public long PlayerId { get; private set; }
    public long SessionId { get; private set; }

    public static PlayerContext Create(long playerId, long sessionId);
    public static void Clear();
    public void Dispose();
}
```

---

## 4. Framework.Configuration

### 4.1 ServerConfig

```csharp
namespace MMORPG.Framework.Configuration;

/// <summary>
/// 服务器根配置
///
/// 通常从 JSON 文件加载，或由业务层自定义。
/// </summary>
public class ServerConfig
{
    public ServerSettings      Server      { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public LoggingSettings     Logging     { get; set; } = new();
    public NetworkSettings     Network     { get; set; } = new();
    public DatabaseSettings    Database    { get; set; } = new();
}

/// <summary>
/// 服务器基础设置
/// </summary>
public class ServerSettings
{
    public int ServerId { get; set; } = 1;
    public string ServerName { get; set; } = "MMORPG Server";
    public ServerType Type { get; set; } = ServerType.Game;
    public bool DebugMode { get; set; } = false;
}

public enum ServerType { Game, Login, Gateway, Chat }

/// <summary>
/// 性能设置
/// </summary>
public class PerformanceSettings
{
    public int TargetFps { get; set; } = 20;
    public int MaxFps { get; set; } = 240;
    public int MinFps { get; set; } = 20;
    public int MaxQueueLength { get; set; } = 10000;
    public double FrameBudgetMs => 1000.0 / TargetFps;
}

/// <summary>
/// 日志设置
/// </summary>
public class LoggingSettings
{
    public string MinLevel { get; set; } = "Info";
    public bool EnableConsole { get; set; } = true;
    public bool EnableFile { get; set; } = true;
    public string LogDirectory { get; set; } = "logs";
    public int RetentionDays { get; set; } = 30;
}

/// <summary>
/// 网络设置
/// </summary>
public class NetworkSettings
{
    public int Port { get; set; } = 9000;
    public int Backlog { get; set; } = 200;
    public int MaxConnections { get; set; } = 10000;
    public int HeartbeatInterval { get; set; } = 5;
    public int HeartbeatTimeout { get; set; } = 20;
    public int ReceiveBufferSize { get; set; } = 8192;
    public int SendBufferSize { get; set; } = 8192;
}

/// <summary>
/// 数据库设置（规划中，框架本身不直接使用数据库）
/// </summary>
public class DatabaseSettings
{
    public DatabaseType Type { get; set; } = DatabaseType.MySql;
    public string ConnectionString { get; set; } = string.Empty;
    public int MaxPoolSize { get; set; } = 100;
    public int MinPoolSize { get; set; } = 10;
    public int ConnectionTimeout { get; set; } = 30;
    public int CommandTimeout { get; set; } = 30;
}

public enum DatabaseType { MySql, PostgreSql, SqlServer, Redis, MongoDB }
```

---

## 5. Framework.Resilience

### 5.1 ResiliencePipeline

```csharp
namespace MMORPG.Framework.Resilience;

/// <summary>
/// 弹性管道（组合 Timeout + Retry + CircuitBreaker）
///
/// 执行顺序：最外层 Timeout → 中层 Retry → 内层 CircuitBreaker → 业务 Action
/// </summary>
public sealed class ResiliencePipeline
{
    public string Name { get; }
    public bool HasCircuitBreaker { get; }

    public ResiliencePipeline(string name,
        TimeoutPolicy? timeout,
        RetryPolicy? retry,
        CircuitBreaker? circuitBreaker);

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken ct = default);

    public async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken ct = default);

    public CircuitBreakerSnapshot? GetCircuitBreakerSnapshot();
    public string GetStatusDescription();
}

/// <summary>
/// 熔断器状态快照（只读 record）
/// </summary>
public readonly record struct CircuitBreakerSnapshot(
    string Name,
    CircuitBreakerState State,
    int FailureCount);

public enum CircuitBreakerState
{
    Closed = 0,  // 正常
    Open = 1,     // 熔断（快速失败）
    HalfOpen = 2  // 半开（试探性放行）
}
```

### 5.2 ResiliencePipelineBuilder

```csharp
public sealed class ResiliencePipelineBuilder
{
    public ResiliencePipelineBuilder Name(string name);
    public ResiliencePipelineBuilder WithTimeout(int timeoutMs);
    public ResiliencePipelineBuilder WithTimeout(string name, TimeoutOptions options);
    public ResiliencePipelineBuilder WithRetry(RetryOptions? options = null);
    public ResiliencePipelineBuilder WithRetry(string name, RetryOptions options);
    public ResiliencePipelineBuilder WithCircuitBreaker(CircuitBreakerOptions? options = null);
    public ResiliencePipelineBuilder WithCircuitBreaker(string name, CircuitBreakerOptions options);
    public ResiliencePipeline Build();
}
```

### 5.3 RetryPolicy

```csharp
/// <summary>
/// 重试策略配置
/// </summary>
public sealed class RetryOptions
{
    public int MaxRetryCount { get; init; } = 3;
    public int BaseDelayMs { get; init; } = 200;
    public int MaxDelayMs { get; init; } = 10_000;
    public int ExponentialBase { get; init; } = 2;
    public bool UseJitter { get; init; } = true;
    public double JitterPercentage { get; init; } = 0.3;
    public Func<Exception, bool>? ShouldRetry { get; init; }
    public Action<int, int, Exception>? OnRetry { get; init; }
}

public sealed class RetryPolicy
{
    public string Name { get; }
    public RetryPolicy(string name, RetryOptions? options = null);

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken ct = default);

    public async Task ExecuteAsync(Func<Task> action, CancellationToken ct = default);
}
```

### 5.4 CircuitBreaker

```csharp
/// <summary>
/// 熔断器配置
/// </summary>
public sealed class CircuitBreakerOptions
{
    public int FailureThreshold { get; init; } = 5;
    public int DurationOfBreakMs { get; init; } = 30_000;
    public int HalfOpenMaxAttempts { get; init; } = 1;
    public int SuccessThreshold { get; init; } = 1;
    public int MinimumExecutionTimeMs { get; init; } = 500;
}

public sealed class CircuitBreaker
{
    public string Name { get; }
    public CircuitBreakerState State { get; }
    public int FailureCount { get; }
    public CircuitBreaker(string name, CircuitBreakerOptions? options = null);

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken ct = default);

    public async Task ExecuteAsync(Func<Task> action, CancellationToken ct = default);

    /// <summary>重置熔断器（主要用于测试）</summary>
    public void Reset();
    public CircuitBreakerSnapshot GetSnapshot();
}

/// <summary>
/// 熔断时抛出的异常
/// </summary>
public class BrokenCircuitException : Exception
{
    public BrokenCircuitException(string message) : base(message) { }
    public BrokenCircuitException(string message, Exception? innerException)
        : base(message, innerException) { }
}
```

### 5.5 TimeoutPolicy

```csharp
public sealed class TimeoutOptions
{
    public int TimeoutMs { get; init; } = 5_000;
    public Action<string>? OnTimeout { get; init; }
}

public sealed class TimeoutPolicy
{
    public string Name { get; }
    public TimeoutPolicy(string name, TimeoutOptions? options = null);

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken ct = default);

    public async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken ct = default);
}

/// <summary>
/// 超时时抛出的异常
/// </summary>
public class TimeoutRejectedException : Exception
{
    public TimeoutRejectedException(string message) : base(message) { }
    public TimeoutRejectedException(string message, Exception? inner)
        : base(message, inner) { }
}
```

### 5.6 ResiliencePolicyRegistry

```csharp
public static class ResiliencePolicyRegistry
{
    public static void Register(string name, ResiliencePipeline pipeline);
    public static ResiliencePipeline? Get(string name);
    public static string[] GetRegisteredNames();
    public static Dictionary<string, CircuitBreakerSnapshot?> GetAllCircuitBreakerSnapshots();
    public static void Clear();  // 测试用
}
```

---

## 6. Framework.Observability

### 6.1 MetricsCollector

> ⚠️ **v3.0.0 现代化（#10 修复）**：底层迁移至 `System.Diagnostics.Metrics`（Meter API）。
> - 所有指标同时注册到内部 Meter（名称：`MMORPG.Framework`）
> - 外部工具（dotnet-counters、OpenTelemetry、Prometheus 等）可通过 Meter 名称直接订阅
> - 对外 API（ICounter / IHistogram / IGauge / MetricsCollector）完全不变，向后兼容

```csharp
namespace MMORPG.Framework.Observability;

public class MetricsCollector  // 单例
{
    public static MetricsCollector Instance { get; }

    // v3.0.0 新增：底层 Meter 实例（供高级用户接入 OpenTelemetry 等）
    internal Meter Meter { get; }

    // ---------- 指标注册 ----------
    public ICounter? RegisterCounter(string name, string description);
    public IGauge? RegisterGauge(string name, string description, Func<double>? valueProvider = null);
    public IHistogram? RegisterHistogram(string name, string description, int capacity = 1024);

    // ---------- 指标查询 ----------
    public double? GetValue(string name);
    public IDictionary<string, double> Snapshot();
    public IEnumerable<MetricSnapshotEntry> GetDetailedSnapshot();
    public IEnumerable<string> GetRegisteredMetrics();

    // ---------- 按类型获取 ----------
    public ICounter? GetCounter(string name);
    public IGauge? GetGauge(string name);
    public IHistogram? GetHistogram(string name);

    // ---------- 控制 ----------
    public bool IsEnabled { get; }
    public void Enable();
    public void Disable();
    public void Reset();
    public void ResetAll();
}

public interface ICounter { long Count { get; } void Increment(); void IncrementBy(long value); }
public interface IGauge { double Value { get; } void Set(double value); }
public interface IHistogram
{
    double Value { get; }
    void Record(double value);
    int SampleCount { get; }
    double P50 { get; }
    double P95 { get; }
    double P99 { get; }
}

public class MetricSnapshotEntry
{
    public string Name { get; set; }
    public double Value { get; set; }
    public string Description { get; set; }
    public string Type { get; set; }
}
```

**v3.0.0 Meter 接入说明**：
- 所有通过 `RegisterCounter/RegisterGauge/RegisterHistogram` 注册的指标，
  都会同时创建对应的 `Counter<long>` / `Histogram<double>` / `ObservableGauge<double>` 仪器。
- Meter 名称：`"MMORPG.Framework"`，版本与程序集版本一致
- 外部监听者（如 OpenTelemetry）只需订阅该 Meter 即可获取所有指标
- `Reset()` 仅重置本地读取值，Meter 仪器保持累计（符合 OpenTelemetry 语义）

### 6.2 HealthCheckService

```csharp
namespace MMORPG.Framework.Observability;

public class HealthCheckService  // 单例
{
    public static HealthCheckService Instance { get; }

    public void Register(string name,
        Func<CancellationToken, Task<(HealthCheckResult, string)>> checker);
    public void Register(string name,
        Func<HealthCheckResult> syncChecker,
        string? description = null);

    // v2.9.0 新增 bypassCache 参数；默认 10 秒结果缓存
    public async Task<HealthCheckStatus> CheckHealthAsync(
        CancellationToken ct = default,
        bool bypassCache = false);

    public Task<string> RenderTextAsync(CancellationToken ct = default);
    public Task<string> RenderJsonAsync(CancellationToken ct = default);

    public void Clear();

    // v2.9.0 新增
    public void InvalidateCache();
    public HealthCheckStatus? CachedStatus { get; }
}

public enum HealthCheckResult { Healthy = 0, Degraded = 1, Unhealthy = 2 }

public class HealthCheckStatus
{
    public HealthCheckResult OverallStatus { get; }
    public HealthCheckEntry[] Entries { get; }
}

public static class HealthCheckServiceExtensions
{
    public static void AddMemoryCheck(
        this HealthCheckService service,
        long memoryThresholdBytes = 2L * 1024 * 1024 * 1024);

    public static void AddSessionCountCheck(
        this HealthCheckService service,
        TcpServer server,
        int degradedThreshold = 500,
        int unhealthyThreshold = 900);
}
```

**说明（v2.9.0）**：
- 默认 `CheckHealthAsync()` 会复用最近 10 秒内的检查结果，避免每次 HTTP 请求都重跑所有检查项。
- 调用 `Clear()` 会自动让缓存失效，测试场景下无需手动调用 `InvalidateCache()`。
- 关键检查（如服务状态变更后）可显式传 `bypassCache: true` 强制重跑。

### 6.3 HealthEndpoint

```csharp
/// <summary>
/// HTTP 健康检查端点（基于 HttpListener，零依赖）
/// </summary>
public sealed class HealthEndpoint : IDisposable
{
    public HealthEndpoint(
        int port = 8080,
        string healthPath = "/health",
        string readyPath = "/ready",
        string alivePath = "/alive");

    public string HealthPath { get; }
    public string ReadyPath { get; }
    public string AlivePath { get; }
    public bool IsRunning { get; }

    public void Start();
    public void Stop();
}
```

### 6.4 CrashReporting

```csharp
namespace MMORPG.Framework.Observability;

public static class CrashReporting
{
    public static void Enable(string? crashDirectory = null);
    public static void Disable();
    public static void ReportException(Exception? ex, string? extraMessage = null);
    public static bool IsEnabled { get; }
    public static long CrashCount { get; }
    public static string CrashDirectory { get; }
}
```

### 6.5 MemoryMonitor

```csharp
namespace MMORPG.Framework.Observability;

public class MemoryMonitor
{
    public event EventHandler<MemoryEventArgs>? MemoryWarning;
    public event EventHandler<MemoryEventArgs>? MemoryCritical;
    public event EventHandler<MemoryLeakEventArgs>? MemoryLeakDetected;

    public MemoryMonitor(MemoryMonitorOptions? options = null);
    public void Start();
    public void Stop();
    public void TriggerGC();
    public MemoryReport GetReport();
}

public class MemoryMonitorOptions
{
    public long WarningThresholdMB { get; set; } = 1024;
    public long CriticalThresholdMB { get; set; } = 2048;
    public int CheckIntervalSeconds { get; set; } = 30;
    public bool EnableLeakDetection { get; set; } = true;
    public long LeakGrowthThresholdMB { get; set; } = 50;
    public int LeakContinuousCountThreshold { get; set; } = 5;
    public bool EnableGCStats { get; set; } = true;
    public bool AutoTriggerGCOnCritical { get; set; } = false;
}

public class MemoryReport
{
    public long CurrentMemoryMB { get; set; }
    public long WarningThresholdMB { get; set; }
    public long CriticalThresholdMB { get; set; }
    public bool IsWarning { get; }
    public bool IsCritical { get; }
    public int Gen0Collections { get; }
    public int Gen1Collections { get; }
    public int Gen2Collections { get; }
    public DateTime Timestamp { get; }
}
```

### 6.6 PrometheusExporter

```csharp
namespace MMORPG.Framework.Observability;

/// <summary>
/// Prometheus 指标导出器（基于 HttpListener）
/// </summary>
public class PrometheusExporter
{
    /// <param name="port">HTTP 端口，默认 9091</param>
    /// <param name="path">指标路径，默认 /metrics/（末尾有斜杠）</param>
    /// <param name="prefix">指标名前缀，默认 mmorpg_</param>
    public PrometheusExporter(int port = 9091,
        string path = "/metrics/",
        string prefix = "mmorpg_");

    public void Start();
    public void Stop();
    public string GeneratePrometheusMetrics();
    public static string GetMetricsText(string prefix = "mmorpg_");
}
```

---

## 7. Framework.Security

### 7.1 RateLimiter

```csharp
namespace MMORPG.Framework.Security;

/// <summary>
/// 消息限流器 — 令牌桶算法
/// </summary>
public class RateLimiter
{
    /// <summary>构造限流器</summary>
    /// <param name="maxMessagesPerSecond">每秒最大消息数</param>
    public RateLimiter(long maxMessagesPerSecond);

    /// <summary>尝试获取一个令牌（线程安全）</summary>
    /// <returns>true = 允许处理，false = 超过限流</returns>
    public bool TryAcquire();

    /// <summary>尝试获取 count 个令牌</summary>
    public bool TryAcquire(int count);

    /// <summary>当前可用令牌数（线程安全）</summary>
    public long CurrentTokens { get; }

    /// <summary>总共拒绝的消息数</summary>
    public long DroppedCount { get; }

    /// <summary>最后一次拒绝消息的时间（UTC）</summary>
    public DateTime LastDroppedTime { get; }

    /// <summary>每秒最大消息数</summary>
    public long MaxMessagesPerSecond { get; }

    /// <summary>重置限流器状态（测试用）</summary>
    public void Reset();
}
```

### 7.2 ServiceStateManager

```csharp
namespace MMORPG.Framework.Security;

public class ServiceStateManager  // 单例
{
    public static ServiceStateManager Instance { get; }

    /// <summary>当前服务状态（线程安全读取）</summary>
    public ServiceState CurrentState { get; }

    /// <summary>是否处于 Running 状态</summary>
    public bool IsRunning { get; }

    /// <summary>是否应正常路由消息（= Running）</summary>
    public bool AcceptsNormalMessages { get; }

    public event EventHandler<ServiceStateChangedEventArgs>? StateChanged;

    public Task StartAsync();
    public Task PauseAsync();
    public Task ResumeAsync();
    public Task StopAsync();
    public void Reset();
}

public enum ServiceState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Pausing = 3,
    Paused = 4,
    Stopping = 5
}

public class ServiceStateChangedEventArgs : EventArgs
{
    public ServiceState OldState { get; }
    public ServiceState NewState { get; }
    public DateTimeOffset ChangedAtUtc { get; }
}

---

## 8. 未来规划中的 API

以下 API **当前未实现**，作为未来开发的路标。

### 8.1 Framework.Serialization（已实现）

当前网络层已经使用 `MessageSerializer`（Google Protobuf 序列化）作为消息编解码，由 `Google.Protobuf` 提供支持，生成代码位于 `Framework.Network.Protobuf` 命名空间。本命名空间保留用于未来的扩展。

```csharp
namespace MMORPG.Framework.Serialization; // 已实现

public interface IMessageCodec
{
    void Serialize(IMessage message, Stream stream);
    IMessage Deserialize(Stream stream);
}

/// <summary>Protobuf 编解码器</summary>
public class ProtobufCodec<T> : IMessageCodec where T : IMessage<T>, new()
```

### 5.2 Core.ECS（规划中）

完整的实体-组件-系统架构，用于大规模场景管理（玩家、NPC、怪物、掉落物等）。

```csharp
namespace MMORPG.Core.ECS; // 规划中

public sealed class World
{
    public int EntityCount { get; }
    public IReadOnlyList<ISystem> Systems { get; }

    public Entity CreateEntity();
    public Entity CreateEntity<T1>(T1 component1) where T1 : struct;
    public Entity CreateEntity<T1, T2>(T1 c1, T2 c2) where T1 : struct where T2 : struct;
    public void DestroyEntity(Entity entity);
    public bool Exists(Entity entity);

    public void AddComponent<T>(Entity entity, T component) where T : struct;
    public ref T GetComponent<T>(Entity entity) where T : struct;
    public bool TryGetComponent<T>(Entity entity, out T component) where T : struct;
    public void RemoveComponent<T>(Entity entity) where T : struct;
    public bool HasComponent<T>(Entity entity) where T : struct;

    public QueryEnumerator<T1> Query<T1>() where T1 : struct;
    public QueryEnumerator<T1, T2> Query<T1, T2>() where T1 : struct where T2 : struct;

    public void AddSystem<T>(T system) where T : ISystem;
    public T GetSystem<T>() where T : ISystem;
    public void RemoveSystem<T>() where T : ISystem;
    public void Update(float deltaTime);
}

public readonly struct Entity : IEquatable<Entity>, IComparable<Entity>
{
    public int Version { get; }
    public ushort ArchetypeId { get; }
    public uint Index { get; }
    public bool IsNull { get; }
    public static readonly Entity Null;
}

public interface IComponent
{
    static abstract int ComponentTypeId { get; }
}

public interface ISystem
{
    void OnCreate(World world);
    void Update(World world, float deltaTime);
    void OnDestroy(World world);
}

public abstract class SystemBase : ISystem { ... }
```

### 5.3 其他规划中的 API

- `Session.SetData<T> / GetData<T> / RemoveData<T>`：会话级 KV 存储（当前未实现，业务层可自行维护字典）
- `TcpServer.BroadcastTo(IEnumerable<long> playerIds, IMessage message)`：给部分玩家广播（当前未实现，业务层可通过 GetAllSessions 然后过滤发送）
- `TcpServer.GetSessionByPlayerId(long playerId)`：按玩家 ID 找会话（当前未实现，业务层可自行维护映射）
- `MessageRouter.Clear()`：清空所有已注册处理器（当前未实现）
- `ChannelExtensions.DrainAll<T>() / TryReadAll<T>()`：这些能力已在 `MessageChannel<T>` 中直接提供，不作为扩展方法存在

---

## 相关文档

| 文档 | 说明 |
|------|------|
| [01-整体架构.md](./01-整体架构.md) | 架构概览 |
| [02-网络设计.md](./02-网络设计.md) | 网络层详细设计 |
| [03-线程模型.md](./03-线程模型.md) | 线程模型设计 |
| [04-ECS设计.md](./04-ECS设计.md) | ECS 架构设计（规划） |
| [05-序列化设计.md](./05-序列化设计.md) | 序列化设计与规划 |
| [07-开发者指南.md](./07-开发者指南.md) | 开发者使用指南 |

---

## 修改历史

| 版本 | 日期 | 修改内容 | 作者 |
|------|------|---------|------|
| v1.3.0 | 2026-06-23 | v3.0.0 同步：MetricsCollector 现代化迁移至 System.Diagnostics.Metrics（Meter API），新增 Meter 内部属性、OpenTelemetry 接入说明 | 与伙伴共同编写 |
| v1.2.0 | 2026-06-23 | v2.9.0 同步：新增 `HealthCheckService` 结果缓存（10s TTL、InvalidateCache、bypassCache）、`SnowflakeIdGenerator` TimeProvider 注入、`GameSchedulerOptions.FrameTimeWindowSize`（1-10000） | 与伙伴共同编写 |
| v1.1.0 | 2026-06-23 | 全面对齐：新增 Framework.Resilience（§5）、Framework.Observability（§6）、Framework.Security（§7）三章；修复 TcpServer（StopAsync/BroadcastAsync/IsShuttingDown）、MessageRouter（RouteAsync/6个RegisterHandler重载/SetGlobalRateLimit）、Session（SetSessionRateLimit/DisconnectAsync/ResetWith）等 API 签名；新增 SessionPool、MessageRetryQueue、TlsHelper、Validator 四个类；修复 MessageChannel.WriteAsync 返回值为 ValueTask[bool] | 与伙伴共同编写 |
| v1.0.0 | 2026-06-21 | 全面更新：仅保留已实现的 API，新增 Framework.Configuration 章节，添加未来规划部分 | 与伙伴共同编写 |

---

> **提示**：本节为框架真实的 API 参考。使用前请对照 `src/MMORPG.Framework/**/*.cs` 源码确认签名一致性。
