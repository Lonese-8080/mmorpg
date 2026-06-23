// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using MMORPG.Framework.Logging;
using MMORPG.Framework.Observability;
using MMORPG.Framework.Configuration;
using MMORPG.Framework.Network;

namespace MMORPG.Framework;

/// <summary>
/// 框架选项 - 可插拔配置入口
/// 
/// 统一管理所有模块的启用/禁用状态，方便业务层一次性
/// 初始化整个框架的行为。
/// 
/// 默认所有模块都是禁用的，业务层需要显式启用才能生效。
/// 
/// 使用示例：
/// <code>
/// var options = new FrameworkOptions
/// {
///     EnableMetrics = true,
///     EnableHealthChecks = true,
///     EnableCrashReporting = true,
///     CrashReportDirectory = "/var/log/mmorpg/crashes"
/// };
/// FrameworkBootstrap.Configure(options);
/// </code>
/// </summary>
public class FrameworkOptions
{
    /// <summary>启用指标收集（MetricsCollector）</summary>
    public bool EnableMetrics { get; set; }

    /// <summary>启用健康检查（HealthCheckService）</summary>
    public bool EnableHealthChecks { get; set; }

    /// <summary>启用崩溃收集（CrashReporting）</summary>
    public bool EnableCrashReporting { get; set; }

    /// <summary>崩溃报告目录（默认 logs/crashes）</summary>
    public string CrashReportDirectory { get; set; } = "logs/crashes";

    /// <summary>是否启用全局消息限流（默认为 false，即不限流）</summary>
    public bool EnableGlobalRateLimiting { get; set; }

    /// <summary>全局每秒最大消息数（启用消息限流时生效）</summary>
    public long GlobalRateLimitMessagesPerSecond { get; set; } = 10000;

    /// <summary>是否启用会话级消息限流</summary>
    public bool EnableSessionRateLimiting { get; set; }

    /// <summary>单会话每秒最大消息数</summary>
    public long SessionRateLimitMessagesPerSecond { get; set; } = 100;

    /// <summary>是否启用配置热更新</summary>
    public bool EnableConfigHotReload { get; set; }

    /// <summary>配置热更新检查间隔（毫秒）</summary>
    public int ConfigHotReloadIntervalMs { get; set; } = 1000;

    /// <summary>要监听热更新的配置文件路径列表</summary>
    public List<string> HotReloadConfigFiles { get; } = new();

    /// <summary>构造默认配置（所有模块禁用）</summary>
    public FrameworkOptions()
    {
    }

    /// <summary>创建推荐的生产配置（所有核心模块启用）</summary>
    public static FrameworkOptions CreateProduction()
    {
        return new FrameworkOptions
        {
            EnableMetrics = true,
            EnableHealthChecks = true,
            EnableCrashReporting = true,
            EnableGlobalRateLimiting = true,
            EnableSessionRateLimiting = true,
            GlobalRateLimitMessagesPerSecond = 50000,
            SessionRateLimitMessagesPerSecond = 200,
            EnableConfigHotReload = true,
            ConfigHotReloadIntervalMs = 1000
        };
    }

    /// <summary>创建调试配置（更宽松的限制，更详细的日志）</summary>
    public static FrameworkOptions CreateDebug()
    {
        return new FrameworkOptions
        {
            EnableMetrics = true,
            EnableHealthChecks = true,
            EnableCrashReporting = true,
            EnableGlobalRateLimiting = false,
            EnableSessionRateLimiting = false,
            EnableConfigHotReload = true,
            ConfigHotReloadIntervalMs = 500
        };
    }
}

/// <summary>
/// 框架启动引导器
/// 
/// 根据 FrameworkOptions 配置统一初始化各个模块。
/// 业务层只需调用一次 FrameworkBootstrap.Configure(options) 即可。
/// </summary>
public static class FrameworkBootstrap
{
    /// <summary>是否已配置（防止重复配置）</summary>
    private static bool _configured;

    /// <summary>配置锁</summary>
    private static readonly object _configLock = new();

    /// <summary>
    /// 根据配置选项初始化框架
    /// </summary>
    /// <param name="options">框架选项</param>
    public static void Configure(FrameworkOptions options)
    {
        if (options == null)
        {
            Logger.Warning("Framework", "Configure 传入 null，忽略配置");
            return;
        }

        lock (_configLock)
        {
            if (_configured)
            {
                Logger.Warning("Framework", "框架已配置，忽略重复调用");
                return;
            }

            Logger.Info("Framework", "配置框架开始");

            // 1. 指标收集
            if (options.EnableMetrics)
            {
                MetricsCollector.Instance.Enable();
                Logger.Info("Framework", "  → 指标收集：已启用");
            }
            else
            {
                Logger.Info("Framework", "  → 指标收集：已禁用");
            }

            // 2. 健康检查（注册默认的内存检查）
            if (options.EnableHealthChecks)
            {
                HealthCheckService.Instance.AddMemoryCheck(1024L * 1024 * 1024);
                Logger.Info("Framework", "  → 健康检查：已启用（内存检查阈值 1GB）");
            }
            else
            {
                Logger.Info("Framework", "  → 健康检查：已禁用");
            }

            // 3. 崩溃收集
            if (options.EnableCrashReporting)
            {
                CrashReporting.Enable(options.CrashReportDirectory);
                Logger.Info("Framework", "  → 崩溃收集：已启用（目录 {0}）", options.CrashReportDirectory);
            }
            else
            {
                Logger.Info("Framework", "  → 崩溃收集：已禁用");
            }

            // 4. 消息限流 - 全局
            if (options.EnableGlobalRateLimiting)
            {
                MessageRouter.Instance.SetGlobalRateLimit(options.GlobalRateLimitMessagesPerSecond);
                Logger.Info("Framework", "  → 全局消息限流：已启用（{0}/秒）", options.GlobalRateLimitMessagesPerSecond);
            }
            else
            {
                Logger.Info("Framework", "  → 全局消息限流：已禁用");
            }

            // 5. 配置热更新
            if (options.EnableConfigHotReload)
            {
                foreach (var configFile in options.HotReloadConfigFiles)
                {
                    try
                    {
                        ConfigurationLoader.EnableFileWatcher<ServerConfig>(
                            configFile, options.ConfigHotReloadIntervalMs);
                        Logger.Info("Framework", "  → 配置热更新：监听 {0}", configFile);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Framework", ex, "  → 配置热更新：监听 {0} 失败", configFile);
                    }
                }

                if (options.HotReloadConfigFiles.Count == 0)
                {
                    Logger.Info("Framework", "  → 配置热更新：已启用但未指定文件（请通过 HotReloadConfigFiles 添加）");
                }
            }
            else
            {
                Logger.Info("Framework", "  → 配置热更新：已禁用");
            }

            _configured = true;
            Logger.Info("Framework", "配置框架完成");
        }
    }

    /// <summary>
    /// 判断框架是否已配置
    /// </summary>
    public static bool IsConfigured
    {
        get { lock (_configLock) return _configured; }
    }

    /// <summary>
    /// 重置框架配置状态（主要用于测试场景）
    /// </summary>
    public static void ResetConfiguration()
    {
        lock (_configLock)
        {
            _configured = false;
            MetricsCollector.Instance.ResetAll();
            HealthCheckService.Instance.Clear();
            CrashReporting.Disable();
            ConfigurationLoader.DisableAllFileWatchers();
            ConfigurationLoader.ClearCache();
        }
    }
}
