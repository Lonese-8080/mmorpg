// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using MMORPG.Framework.Logging;
using MMORPG.Framework.Observability;

namespace MMORPG.Framework.Network;

/// <summary>
/// TLS 助手类 - 封装 SSL/TLS 加密相关操作
/// 
/// 使用场景：
/// - TcpServer 启动时加载证书
/// - Session 建立连接后进行 TLS 握手
/// - 证书热加载（无需重启服务即可更新证书）
/// - 证书过期监控和告警
/// </summary>
public static class TlsHelper
{
    /// <summary>
    /// 当前加载的证书
    /// </summary>
    private static X509Certificate2? _currentCertificate;

    /// <summary>
    /// 证书文件路径
    /// </summary>
    private static string? _certificatePath;

    /// <summary>
    /// 证书密码
    /// </summary>
    private static string? _certificatePassword;

    /// <summary>
    /// 证书文件监听器（用于热加载）
    /// </summary>
    private static FileSystemWatcher? _certificateWatcher;

    /// <summary>
    /// 证书更新事件
    /// 
    /// 当证书被热加载更新时触发，TcpServer 可以订阅此事件
    /// 更新内部引用的证书
    /// </summary>
    public static event EventHandler<X509Certificate2>? CertificateUpdated;

    /// <summary>
    /// 加载 TLS 证书
    /// </summary>
    /// <param name="options">TCP 服务器配置</param>
    /// <returns>加载成功的证书，失败返回 null</returns>
    public static X509Certificate2? LoadCertificate(TcpServerOptions options)
    {
        if (!options.EnableTls)
            return null;

        if (string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            Logger.Error("Network", "启用 TLS 但未配置证书路径");
            return null;
        }

        if (!File.Exists(options.CertificatePath))
        {
            Logger.Error("Network", "TLS 证书文件不存在: {0}", options.CertificatePath);
            return null;
        }

        try
        {
#pragma warning disable SYSLIB0057
            var certificate = string.IsNullOrEmpty(options.CertificatePassword)
                ? new X509Certificate2(options.CertificatePath)
                : new X509Certificate2(options.CertificatePath, options.CertificatePassword);
#pragma warning restore SYSLIB0057

            // 保存配置用于热加载
            _currentCertificate = certificate;
            _certificatePath = options.CertificatePath;
            _certificatePassword = options.CertificatePassword;

            // 检查证书有效期
            CheckCertificateExpiration(certificate);

            Logger.Info("Network", "TLS 证书加载成功: {0}, 有效期至: {1:yyyy-MM-dd}",
                certificate.Subject, certificate.NotAfter);

            // 注册指标
            RegisterCertificateMetrics(certificate);

            return certificate;
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "加载 TLS 证书失败: {0}", options.CertificatePath);
            return null;
        }
    }

    /// <summary>
    /// 启用证书热加载
    /// 
    /// 监听证书文件变化，自动重新加载证书
    /// 无需重启服务即可更新证书（如证书续期后）
    /// </summary>
    /// <param name="options">TCP 服务器配置</param>
    public static void EnableCertificateHotReload(TcpServerOptions options)
    {
        if (!options.EnableTls || string.IsNullOrWhiteSpace(options.CertificatePath))
            return;

        var directory = Path.GetDirectoryName(options.CertificatePath);
        var fileName = Path.GetFileName(options.CertificatePath);

        if (string.IsNullOrEmpty(directory))
            directory = Directory.GetCurrentDirectory();

        if (!Directory.Exists(directory))
        {
            Logger.Warning("Network", "证书目录不存在，无法启用热加载: {0}", directory);
            return;
        }

        try
        {
            _certificateWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _certificateWatcher.Changed += OnCertificateFileChanged;

            Logger.Info("Network", "TLS 证书热加载已启用: {0}", options.CertificatePath);
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "启用证书热加载失败");
        }
    }

    /// <summary>
    /// 禁用证书热加载
    /// </summary>
    public static void DisableCertificateHotReload()
    {
        if (_certificateWatcher != null)
        {
            _certificateWatcher.EnableRaisingEvents = false;
            _certificateWatcher.Changed -= OnCertificateFileChanged;
            _certificateWatcher.Dispose();
            _certificateWatcher = null;

            Logger.Info("Network", "TLS 证书热加载已禁用");
        }
    }

    /// <summary>
    /// 证书文件变化时的处理
    /// </summary>
    private static void OnCertificateFileChanged(object sender, FileSystemEventArgs e)
    {
        Logger.Info("Network", "检测到证书文件变化，开始重新加载: {0}", e.FullPath);

        // 延迟一小段时间，避免文件写入过程中读取
        Task.Delay(500).ContinueWith(_ =>
        {
            try
            {
#pragma warning disable SYSLIB0057
                var newCertificate = string.IsNullOrEmpty(_certificatePassword)
                    ? new X509Certificate2(_certificatePath!)
                    : new X509Certificate2(_certificatePath!, _certificatePassword);
#pragma warning restore SYSLIB0057

                // 验证新证书
                if (newCertificate == null || !newCertificate.HasPrivateKey)
                {
                    Logger.Error("Network", "新证书无效或缺少私钥");
                    return;
                }

                // 检查有效期
                if (newCertificate.NotAfter < DateTime.UtcNow)
                {
                    Logger.Error("Network", "新证书已过期");
                    return;
                }

                // 释放旧证书
                var oldCertificate = _currentCertificate;
                _currentCertificate = newCertificate;

                // 触发更新事件
                CertificateUpdated?.Invoke(null, newCertificate);

                // 更新指标
                RegisterCertificateMetrics(newCertificate);

                Logger.Info("Network", "TLS 证书热加载成功: {0}, 有效期至: {1:yyyy-MM-dd}",
                    newCertificate.Subject, newCertificate.NotAfter);

                // 释放旧证书资源
                try { oldCertificate?.Dispose(); }
                catch { }
            }
            catch (Exception ex)
            {
                Logger.Error("Network", ex, "证书热加载失败");
            }
        });
    }

    /// <summary>
    /// 检查证书有效期并发出告警
    /// </summary>
    private static void CheckCertificateExpiration(X509Certificate2 certificate)
    {
        var daysUntilExpiry = (certificate.NotAfter - DateTime.UtcNow).TotalDays;

        if (daysUntilExpiry < 0)
        {
            Logger.Error("Network", "TLS 证书已过期！过期时间: {0:yyyy-MM-dd}", certificate.NotAfter);
        }
        else if (daysUntilExpiry < 7)
        {
            Logger.Warning("Network", "TLS 证书即将过期！剩余 {0:F0} 天，过期时间: {1:yyyy-MM-dd}",
                daysUntilExpiry, certificate.NotAfter);
        }
        else if (daysUntilExpiry < 30)
        {
            Logger.Warning("Network", "TLS 证书将在 {0:F0} 天后过期，建议提前续期",
                daysUntilExpiry);
        }
    }

    /// <summary>
    /// 注册证书相关指标
    /// </summary>
    private static void RegisterCertificateMetrics(X509Certificate2 certificate)
    {
        try
        {
            if (!MetricsCollector.Instance.IsEnabled)
                return;

            // 证书过期时间（天数）
            var daysUntilExpiry = (certificate.NotAfter - DateTime.UtcNow).TotalDays;
            var expiryGauge = MetricsCollector.Instance.RegisterGauge("tls.cert_expiry_days", "证书过期剩余天数");
            expiryGauge?.Set(daysUntilExpiry);

            // 证书加载时间戳
            var timestampGauge = MetricsCollector.Instance.RegisterGauge("tls.cert_loaded_timestamp", "证书加载时间戳");
            timestampGauge?.Set(DateTime.UtcNow.Ticks / 1_000_000.0);
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "注册证书指标失败");
        }
    }

    /// <summary>
    /// 获取当前证书
    /// </summary>
    public static X509Certificate2? GetCurrentCertificate()
    {
        return _currentCertificate;
    }

    /// <summary>
    /// 执行 TLS 握手
    /// </summary>
    /// <param name="socket">客户端套接字</param>
    /// <param name="certificate">服务器证书</param>
    /// <param name="options">TLS 配置选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>SslStream，如果握手失败返回 null</returns>
    public static async Task<SslStream?> PerformHandshakeAsync(
        Socket socket,
        X509Certificate2 certificate,
        TcpServerOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var networkStream = new NetworkStream(socket, true);
            var sslStream = new SslStream(
                networkStream,
                false,
                ValidateCertificate);

            await sslStream.AuthenticateAsServerAsync(
                certificate,
                options.RequireClientCertificate,
                options.TlsProtocol,
                false);

            Logger.Debug("Network", "TLS 握手成功: {0}", sslStream.SslProtocol);
            return sslStream;
        }
        catch (Exception ex)
        {
            Logger.Error("Network", ex, "TLS 握手失败");
            return null;
        }
    }

    /// <summary>
    /// 证书验证回调函数
    /// 
    /// 在双向认证模式下，服务器会调用此方法验证客户端证书
    /// </summary>
    private static bool ValidateCertificate(
        object? sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        Logger.Warning("Network", "证书验证失败: {0}", sslPolicyErrors);

        return false;
    }

    /// <summary>
    /// 验证 TLS 配置是否有效
    /// </summary>
    /// <param name="options">TCP 服务器配置</param>
    /// <returns>验证结果，失败时包含错误消息</returns>
    public static (bool Valid, string? ErrorMessage) ValidateTlsConfiguration(TcpServerOptions options)
    {
        if (!options.EnableTls)
            return (true, null);

        if (string.IsNullOrWhiteSpace(options.CertificatePath))
            return (false, "启用 TLS 但未配置证书路径");

        if (!File.Exists(options.CertificatePath))
            return (false, $"TLS 证书文件不存在: {options.CertificatePath}");

        if (!options.CertificatePath.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase))
            Logger.Warning("Network", "证书文件不是 PFX 格式，可能无法加载");

        if (!Enum.IsDefined(options.TlsProtocol))
            return (false, "无效的 TLS 协议版本");

        return (true, null);
    }
}