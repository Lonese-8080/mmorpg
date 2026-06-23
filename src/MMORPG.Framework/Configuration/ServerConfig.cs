// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace MMORPG.Framework.Configuration;

/// <summary>
/// 服务器配置选项（生产级）
/// 
/// 包含服务器运行所需的所有配置项
/// 支持从 JSON 文件或环境变量加载
/// 实现 <see cref="IValidatableObject"/>，可在启动时进行自校验
/// </summary>
/// <example>
/// 使用示例：
/// var config = ConfigurationLoader.Load&lt;ServerConfig&gt;("config/appsettings.production.json");
/// var validation = config.Validate(new ValidationContext(config));
/// Logger.Info("Config", "服务器配置加载完成: SchemaVersion={0}", config.SchemaVersion);
/// </example>
public class ServerConfig : IValidatableObject
{
    #region 版本

    /// <summary>
    /// 配置模式版本号（SemVer）
    /// 
    /// 用于配置热更新时向后兼容检查
    /// 默认：1.0.0
    /// </summary>
    public string SchemaVersion { get; set; } = "1.0.0";

    #endregion

    #region 服务器配置

    /// <summary>
    /// 服务器配置
    /// </summary>
    public ServerSettings Server { get; set; } = new();

    /// <summary>
    /// 性能配置
    /// </summary>
    public PerformanceSettings Performance { get; set; } = new();

    /// <summary>
    /// 日志配置
    /// </summary>
    public LoggingSettings Logging { get; set; } = new();

    /// <summary>
    /// 网络配置
    /// </summary>
    public NetworkSettings Network { get; set; } = new();

    /// <summary>
    /// 数据库配置
    /// </summary>
    public DatabaseSettings Database { get; set; } = new();

    /// <summary>
    /// 可观测性配置
    /// </summary>
    public ObservabilitySettings Observability { get; set; } = new();

    #endregion

    #region 配置版本与校验

    /// <summary>
    /// 生产级配置校验（实现自 IValidatableObject）
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // SchemaVersion 格式检查（SemVer: X.Y.Z）
        if (string.IsNullOrWhiteSpace(SchemaVersion))
        {
            yield return new ValidationResult("SchemaVersion 不能为空", new[] { nameof(SchemaVersion) });
            yield break;
        }

        var parts = SchemaVersion.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var patch)
            || major < 0 || minor < 0 || patch < 0)
        {
            yield return new ValidationResult(
                "SchemaVersion 必须为 SemVer 格式（例如 1.0.0）",
                new[] { nameof(SchemaVersion) });
        }

        // 性能
        if (Performance.TargetFps < 10 || Performance.TargetFps > 240)
            yield return new ValidationResult(
                "Performance.TargetFps 应在 10-240 之间",
                new[] { nameof(Performance), nameof(Performance.TargetFps) });

        // 网络
        if (Network.Port < 1 || Network.Port > 65535)
            yield return new ValidationResult("Network.Port 必须在 1-65535 之间", new[] { nameof(Network) });

        if (Network.MaxConnections < 0)
            yield return new ValidationResult("Network.MaxConnections 不能为负数", new[] { nameof(Network) });

        if (Network.HeartbeatTimeout <= 0)
            yield return new ValidationResult("Network.HeartbeatTimeout 必须大于 0", new[] { nameof(Network) });

        if (Network.MessageRetryCount < 0)
            yield return new ValidationResult("Network.MessageRetryCount 不能为负数", new[] { nameof(Network) });

        if (Network.MessageRetryIntervalMs < 0)
            yield return new ValidationResult("Network.MessageRetryIntervalMs 不能为负数", new[] { nameof(Network) });

        // 连接池
        if (Network.SessionPool.MinPoolSize < 0)
            yield return new ValidationResult("Network.SessionPool.MinPoolSize 不能为负数", new[] { nameof(Network) });

        if (Network.SessionPool.MaxPoolSize < Network.SessionPool.MinPoolSize)
            yield return new ValidationResult(
                "Network.SessionPool.MaxPoolSize 不能小于 MinPoolSize",
                new[] { nameof(Network) });

        if (Network.SessionPool.MaxIdleCapacity < 0
            || Network.SessionPool.MaxIdleCapacity > Network.SessionPool.MaxPoolSize)
            yield return new ValidationResult(
                "Network.SessionPool.MaxIdleCapacity 必须在 0 - MaxPoolSize 之间",
                new[] { nameof(Network) });

        // TLS
        if (Network.EnableTls && string.IsNullOrWhiteSpace(Network.CertificatePath))
            yield return new ValidationResult(
                "启用 TLS 时必须提供 CertificatePath",
                new[] { nameof(Network) });

        // 日志采样率
        if (Logging.SamplingRate < 0 || Logging.SamplingRate > 1.0)
            yield return new ValidationResult(
                "Logging.SamplingRate 必须在 0.0 - 1.0 之间",
                new[] { nameof(Logging) });

        // 保留天数
        if (Logging.RetentionDays < 1)
            yield return new ValidationResult(
                "Logging.RetentionDays 必须 >= 1",
                new[] { nameof(Logging) });

        // 可观测性 - 内存
        if (Observability.MemoryWarningThresholdMB <= 0)
            yield return new ValidationResult(
                "Observability.MemoryWarningThresholdMB 必须大于 0",
                new[] { nameof(Observability) });

        if (Observability.MemoryCriticalThresholdMB <= Observability.MemoryWarningThresholdMB)
            yield return new ValidationResult(
                "Observability.MemoryCriticalThresholdMB 必须大于 WarningThresholdMB",
                new[] { nameof(Observability) });

        if (Observability.MemoryCheckIntervalSeconds <= 0)
            yield return new ValidationResult(
                "Observability.MemoryCheckIntervalSeconds 必须大于 0",
                new[] { nameof(Observability) });

        // Prometheus
        if (Observability.EnablePrometheus
            && (Observability.PrometheusPort < 1 || Observability.PrometheusPort > 65535))
            yield return new ValidationResult(
                "Observability.PrometheusPort 必须在 1-65535 之间",
                new[] { nameof(Observability) });
    }

    /// <summary>
    /// 判断当前配置是否与另一个配置兼容（SchemaVersion 主版本相同则兼容）
    /// </summary>
    public bool IsSchemaCompatibleWith(ServerConfig? other)
    {
        if (other == null) return false;
        var thisMajor = GetMajorVersion(SchemaVersion);
        var otherMajor = GetMajorVersion(other.SchemaVersion);
        return thisMajor == otherMajor && thisMajor >= 0;
    }

    private static int GetMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return -1;
        var parts = version.Split('.');
        if (parts.Length == 0) return -1;
        return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;
    }

    #endregion
}

/// <summary>
/// 服务器基本设置
/// </summary>
public class ServerSettings
{
    /// <summary>
    /// 服务器ID
    /// 用于集群环境中标识不同服务器
    /// </summary>
    public int ServerId { get; set; } = 1;

    /// <summary>
    /// 服务器名称
    /// 显示在客户端服务器列表中
    /// </summary>
    public string ServerName { get; set; } = "MMORPG Server";

    /// <summary>
    /// 服务器类型
    /// </summary>
    public ServerType Type { get; set; } = ServerType.Game;

    /// <summary>
    /// 是否为调试模式
    /// 调试模式下会输出更详细的日志
    /// </summary>
    public bool DebugMode { get; set; } = false;
}

/// <summary>
/// 服务器类型枚举
/// </summary>
public enum ServerType
{
    /// <summary>
    /// 游戏服务器
    /// 处理游戏逻辑
    /// </summary>
    Game,

    /// <summary>
    /// 登录服务器
    /// 处理登录和账号验证
    /// </summary>
    Login,

    /// <summary>
    /// 网关服务器
    /// 负责消息转发和负载均衡
    /// </summary>
    Gateway,

    /// <summary>
    /// 聊天服务器
    /// 处理聊天消息
    /// </summary>
    Chat
}

/// <summary>
/// 性能设置
/// </summary>
public class PerformanceSettings
{
    /// <summary>
    /// 目标帧率
    /// 可配置范围：20-240 Hz
    /// </summary>
    public int TargetFps { get; set; } = 60;

    /// <summary>
    /// 最大帧率
    /// 用于限制服务器最大负载
    /// </summary>
    public int MaxFps { get; set; } = 240;

    /// <summary>
    /// 最小帧率
    /// 如果帧率持续低于此值，会触发告警
    /// </summary>
    public int MinFps { get; set; } = 20;

    /// <summary>
    /// 消息队列最大长度
    /// 超过此值会触发拥塞控制
    /// </summary>
    public int MaxQueueLength { get; set; } = 10000;

    /// <summary>
    /// 每帧消息处理预算（毫秒）
    /// </summary>
    public double FrameBudgetMs => 1000.0 / TargetFps;
}

/// <summary>
/// 日志设置
/// </summary>
public class LoggingSettings
{
    /// <summary>
    /// 最小日志级别
    /// </summary>
    public string MinLevel { get; set; } = "Info";

    /// <summary>
    /// 是否启用控制台输出
    /// </summary>
    public bool EnableConsole { get; set; } = true;

    /// <summary>
    /// 是否启用文件输出
    /// </summary>
    public bool EnableFile { get; set; } = true;

    /// <summary>
    /// 日志文件目录
    /// </summary>
    public string LogDirectory { get; set; } = "logs";

    /// <summary>
    /// 日志文件保留天数
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// 日志采样率 (0.0 - 1.0)
    /// 
    /// 生产环境建议设置为 0.1 - 0.3 以减少磁盘 I/O
    /// Debug 和 Warning 级别受此配置影响
    /// Error 和 Fatal 始终记录
    /// </summary>
    public double SamplingRate { get; set; } = 1.0;

    /// <summary>
    /// 高频日志抑制阈值（毫秒）
    /// 
    /// 同一消息在该时间窗口内只记录一次
    /// 0 表示不启用抑制
    /// </summary>
    public int SuppressDuplicateThresholdMs { get; set; } = 0;

    /// <summary>
    /// 是否启用网络日志输出（发送到日志收集服务）
    /// </summary>
    public bool EnableNetwork { get; set; } = false;

    /// <summary>
    /// 网络日志服务端点
    /// </summary>
    public string? NetworkEndpoint { get; set; }
}

/// <summary>
/// 网络设置
/// </summary>
public class NetworkSettings
{
    /// <summary>
    /// 监听端口
    /// </summary>
    public int Port { get; set; } = 9000;

    /// <summary>
    /// 连接队列大小
    /// </summary>
    public int Backlog { get; set; } = 200;

    /// <summary>
    /// 最大连接数
    /// </summary>
    public int MaxConnections { get; set; } = 10000;

    /// <summary>
    /// 心跳间隔（秒）
    /// </summary>
    public int HeartbeatInterval { get; set; } = 5;

    /// <summary>
    /// 心跳超时（秒）
    /// 超过此时间没有收到心跳，会断开连接
    /// </summary>
    public int HeartbeatTimeout { get; set; } = 20;

    /// <summary>
    /// 接收缓冲区大小
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 8192;

    /// <summary>
    /// 发送缓冲区大小
    /// </summary>
    public int SendBufferSize { get; set; } = 8192;

    /// <summary>
    /// 是否启用 TLS/SSL 加密
    /// 
    /// 生产环境强烈建议启用
    /// </summary>
    public bool EnableTls { get; set; } = false;

    /// <summary>
    /// TLS 证书文件路径（PFX 格式）
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// TLS 证书密码
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// 是否启用证书自动热加载
    /// 
    /// 启用后，证书文件变化时自动重新加载，无需重启服务
    /// </summary>
    public bool EnableCertificateHotReload { get; set; } = true;

    /// <summary>
    /// 会话级消息限流（每秒消息数）
    /// 
    /// 0 表示不限制
    /// 建议值：100 - 500 msg/s
    /// </summary>
    public int SessionMessageRateLimit { get; set; } = 100;

    /// <summary>
    /// 全局消息限流（每秒消息数）
    /// 
    /// 0 表示不限制
    /// 建议值：10000 - 50000 msg/s
    /// </summary>
    public int GlobalMessageRateLimit { get; set; } = 10000;

    /// <summary>
    /// 消息重试次数
    /// 
    /// 发送失败时的重试次数
    /// 0 表示不重试
    /// 默认：3 次
    /// </summary>
    public int MessageRetryCount { get; set; } = 3;

    /// <summary>
    /// 消息重试间隔（毫秒）
    /// 
    /// 使用指数退避策略：重试间隔 = 初始值 * 2^重试次数
    /// 默认：100ms
    /// </summary>
    public int MessageRetryIntervalMs { get; set; } = 100;

    /// <summary>
    /// Session 连接池设置
    /// 
    /// 启用后可降低连接建立时的对象分配 GC 压力
    /// </summary>
    public SessionPoolSettings SessionPool { get; set; } = new();
}

/// <summary>
/// Session 连接池配置
/// </summary>
public class SessionPoolSettings
{
    /// <summary>
    /// 是否启用连接池
    /// 默认：true
    /// </summary>
    public bool EnablePool { get; set; } = true;

    /// <summary>
    /// 最小池大小（启动预热数量）
    /// 默认：200
    /// </summary>
    public int MinPoolSize { get; set; } = 200;

    /// <summary>
    /// 最大池大小
    /// 默认：2000
    /// </summary>
    public int MaxPoolSize { get; set; } = 2000;

    /// <summary>
    /// 空闲 Session 容量上限（超过后归还即销毁）
    /// 默认：1000
    /// </summary>
    public int MaxIdleCapacity { get; set; } = 1000;
}

/// <summary>
/// 数据库设置
/// </summary>
public class DatabaseSettings
{
    /// <summary>
    /// 数据库类型
    /// </summary>
    public DatabaseType Type { get; set; } = DatabaseType.MySql;

    /// <summary>
    /// 连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 最大连接数
    /// </summary>
    public int MaxPoolSize { get; set; } = 100;

    /// <summary>
    /// 最小连接数
    /// </summary>
    public int MinPoolSize { get; set; } = 10;

    /// <summary>
    /// 连接超时（秒）
    /// </summary>
    public int ConnectionTimeout { get; set; } = 30;

    /// <summary>
    /// 命令超时（秒）
    /// </summary>
    public int CommandTimeout { get; set; } = 30;
}

/// <summary>
/// 数据库类型枚举
/// </summary>
public enum DatabaseType
{
    /// <summary>
    /// MySQL
    /// </summary>
    MySql,

    /// <summary>
    /// PostgreSQL
    /// </summary>
    PostgreSql,

    /// <summary>
    /// SQL Server
    /// </summary>
    SqlServer,

    /// <summary>
    /// Redis
    /// </summary>
    Redis,

    /// <summary>
    /// MongoDB
    /// </summary>
    MongoDB
}

/// <summary>
/// 可观测性设置
/// 
/// 包含指标、健康检查、监控相关配置
/// </summary>
public class ObservabilitySettings
{
    /// <summary>
    /// 是否启用指标收集
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// 是否启用内存监控
    /// </summary>
    public bool EnableMemoryMonitor { get; set; } = true;

    /// <summary>
    /// 是否启用崩溃收集
    /// </summary>
    public bool EnableCrashReporting { get; set; } = true;

    /// <summary>
    /// 内存告警阈值（MB）
    /// 
    /// 超过此值触发警告级别告警
    /// </summary>
    public long MemoryWarningThresholdMB { get; set; } = 1024;

    /// <summary>
    /// 内存严重告警阈值（MB）
    /// 
    /// 超过此值触发严重级别告警
    /// </summary>
    public long MemoryCriticalThresholdMB { get; set; } = 2048;

    /// <summary>
    /// 内存检查间隔（秒）
    /// </summary>
    public int MemoryCheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// 是否启用 Prometheus 指标导出
    /// </summary>
    public bool EnablePrometheus { get; set; } = false;

    /// <summary>
    /// Prometheus HTTP 导出端口
    /// </summary>
    public int PrometheusPort { get; set; } = 9091;

    /// <summary>
    /// Prometheus 导出路径
    /// </summary>
    public string PrometheusPath { get; set; } = "/metrics";
}
