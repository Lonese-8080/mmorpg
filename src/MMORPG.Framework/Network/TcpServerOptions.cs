// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

namespace MMORPG.Framework.Network;

/// <summary>
/// TCP 服务器配置选项
/// 
/// 使用示例：
/// ```csharp
/// var options = new TcpServerOptions
/// {
///     Port = 9000,
///     Backlog = 200,
///     ReceiveBufferSize = 8 * 1024,
///     HeartbeatTimeoutSeconds = 20,
///     MaxConnections = 10000
/// };
/// ```
/// </summary>
public class TcpServerOptions
{
    /// <summary>
    /// 监听端口
    /// 默认：9000
    /// </summary>
    public int Port { get; set; } = 9000;

    /// <summary>
    /// 监听队列长度
    /// 操作系统最多缓存的等待连接数量
    /// 默认：200
    /// </summary>
    public int Backlog { get; set; } = 200;

    /// <summary>
    /// 接收缓冲区大小（字节）
    /// 默认：8KB
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 8 * 1024;

    /// <summary>
    /// 发送缓冲区大小（字节）
    /// 默认：8KB
    /// </summary>
    public int SendBufferSize { get; set; } = 8 * 1024;

    /// <summary>
    /// 心跳超时时间（秒）
    /// 超过此时间没有收到心跳，断开连接
    /// 默认：20秒
    /// </summary>
    public int HeartbeatTimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// 心跳检查间隔（秒）
    /// 每隔多久检查一次所有连接的心跳
    /// 默认：1秒
    /// </summary>
    public int HeartbeatCheckIntervalSeconds { get; set; } = 1;

    /// <summary>
    /// 最大连接数
    /// 超过此数量的新连接会被拒绝
    /// 默认：10000
    /// </summary>
    public int MaxConnections { get; set; } = 10000;

    /// <summary>
    /// 发送队列最大消息数
    /// 超过此数量会断开连接（防止内存泄漏）
    /// 默认：1000
    /// </summary>
    public int MaxSendQueueSize { get; set; } = 1000;

    /// <summary>
    /// 是否启用 TLS/SSL 加密
    /// 默认：false（明文传输）
    /// 
    /// 生产环境建议设置为 true，配合 CertificatePath 和 CertificatePassword 使用
    /// </summary>
    public bool EnableTls { get; set; } = false;

    /// <summary>
    /// TLS 证书文件路径（PFX 格式）
    /// 
    /// 当 EnableTls = true 时必须提供
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// TLS 证书密码
    /// 
    /// 如果证书有密码保护，需要提供此值
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    /// TLS 协议版本
    /// 默认：Tls12（兼容大多数客户端，安全性较高）
    /// 
    /// 可选值：Tls12, Tls13
    /// </summary>
    public System.Security.Authentication.SslProtocols TlsProtocol { get; set; } =
        System.Security.Authentication.SslProtocols.Tls12;

    /// <summary>
    /// 是否检查客户端证书（双向认证）
    /// 默认：false（单向认证，仅服务器认证）
    /// 
    /// 设置为 true 时，服务器会验证客户端证书
    /// </summary>
    public bool RequireClientCertificate { get; set; } = false;

    /// <summary>
    /// 是否启用 Session 连接池
    /// 默认：false（不启用；与旧版本行为兼容）
    /// </summary>
    public bool EnableSessionPool { get; set; } = false;

    /// <summary>
    /// Session 连接池最小预分配数
    /// 默认：50
    /// </summary>
    public int SessionPoolMinPoolSize { get; set; } = 50;

    /// <summary>
    /// Session 连接池最大容量
    /// 默认：1000
    /// </summary>
    public int SessionPoolMaxPoolSize { get; set; } = 1000;

    /// <summary>
    /// Session 连接池最大空闲容量
    /// 默认：500
    /// </summary>
    public int SessionPoolMaxIdleCapacity { get; set; } = 500;

    /// <summary>
    /// 消息发送失败后的最大重试次数
    /// 默认：3
    /// 0 = 不启用重试（与旧版本行为兼容）
    /// </summary>
    public int MessageRetryCount { get; set; } = 3;

    /// <summary>
    /// 消息重试的基础间隔（毫秒）
    ///
    /// 实际采用指数退避：BaseIntervalMs * 2^(retryCount-1)
    /// 默认：100ms
    /// </summary>
    public int MessageRetryIntervalMs { get; set; } = 100;

    /// <summary>
    /// 校验配置参数的有效性
    ///
    /// ⚠️ 在 TcpServer.Start() 中会自动调用此方法
    /// 如果参数无效，服务器启动会失败
    /// </summary>
    /// <returns>(isValid, errorMessage)</returns>
    public (bool Valid, string? Error) Validate()
    {
        if (Port < 0 || Port > 65535)
            return (false, $"端口无效: {Port}（必须在 0-65535 范围内）");

        if (Backlog < 0)
            return (false, $"Backlog 无效: {Backlog}（必须 >= 0）");

        if (ReceiveBufferSize < 256)
            return (false, $"接收缓冲区太小: {ReceiveBufferSize}（最小 256 字节）");

        if (SendBufferSize < 256)
            return (false, $"发送缓冲区太小: {SendBufferSize}（最小 256 字节）");

        if (HeartbeatTimeoutSeconds < 5)
            return (false, $"心跳超时太短: {HeartbeatTimeoutSeconds}（最小 5 秒）");

        if (HeartbeatCheckIntervalSeconds < 1)
            return (false, $"心跳检查间隔太短: {HeartbeatCheckIntervalSeconds}（最小 1 秒）");

        if (MaxConnections < 1)
            return (false, $"最大连接数无效: {MaxConnections}（必须 >= 1）");

        if (MaxSendQueueSize < 10)
            return (false, $"发送队列太小: {MaxSendQueueSize}（最小 10）");

        if (EnableTls && string.IsNullOrEmpty(CertificatePath))
            return (false, "启用 TLS 但未指定证书路径");

        if (SessionPoolMinPoolSize < 0)
            return (false, $"SessionPoolMinPoolSize 无效: {SessionPoolMinPoolSize}（必须 >= 0）");

        if (SessionPoolMaxPoolSize < SessionPoolMinPoolSize)
            return (false, $"SessionPoolMaxPoolSize < SessionPoolMinPoolSize");

        if (MessageRetryCount < 0)
            return (false, $"MessageRetryCount 无效: {MessageRetryCount}（必须 >= 0）");

        if (MessageRetryIntervalMs < 1)
            return (false, $"MessageRetryIntervalMs 无效: {MessageRetryIntervalMs}（必须 >= 1）");

        return (true, null);
    }
}
