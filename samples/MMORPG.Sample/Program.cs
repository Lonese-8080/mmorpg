using MMORPG.Framework.Logging;
using MMORPG.Framework.Configuration;

namespace MMORPG.Sample;

/// <summary>
/// 示例程序入口
/// 
/// 演示如何使用 MMORPG.Framework
/// 包括：
/// - 日志系统初始化和使用
/// - 配置系统
/// - 追踪上下文
/// </summary>
class Program
{
    /// <summary>
    /// 程序入口点
    /// </summary>
    /// <param name="args">命令行参数</param>
    static async Task Main(string[] args)
    {
        // ========================================================
        // 步骤1：初始化日志系统
        // ========================================================
        
        Console.WriteLine("========================================");
        Console.WriteLine("  MMORPG 服务端框架 - 示例程序");
        Console.WriteLine("========================================");
        Console.WriteLine();
        
        // 初始化日志
        Logger.Initialize(new LogOptions
        {
            MinLevel = LogLevel.Debug,  // 开发模式使用 Debug 级别
            EnableConsole = true,       // 启用控制台输出
            EnableFile = true,          // 启用文件输出
            LogDirectory = "logs",      // 日志目录
            MaxFileSize = 10 * 1024 * 1024,  // 10MB 文件大小
            RetentionDays = 7           // 保留 7 天
        });
        
        Logger.Info("Program", "========================================");
        Logger.Info("Program", "  MMORPG 服务端框架示例程序启动");
        Logger.Info("Program", "========================================");
        
        // ========================================================
        // 步骤2：加载配置
        // ========================================================
        
        Logger.Info("Program", "加载服务器配置...");
        
        var config = new ServerConfig
        {
            Server = new ServerSettings
            {
                ServerId = 1,
                ServerName = "测试服务器",
                Type = ServerType.Game,
                DebugMode = true
            },
            Performance = new PerformanceSettings
            {
                TargetFps = 60,
                MaxFps = 240,
                MinFps = 20
            },
            Network = new NetworkSettings
            {
                Port = 9000,
                MaxConnections = 10000,
                HeartbeatInterval = 5,
                HeartbeatTimeout = 20
            }
        };
        
        Logger.Info("Program", "服务器配置加载完成:");
        Logger.Info("Program", "  服务器ID: {0}", config.Server.ServerId);
        Logger.Info("Program", "  服务器名称: {0}", config.Server.ServerName);
        Logger.Info("Program", "  服务器类型: {0}", config.Server.Type);
        Logger.Info("Program", "  目标帧率: {0} Hz", config.Performance.TargetFps);
        Logger.Info("Program", "  监听端口: {0}", config.Network.Port);
        
        // ========================================================
        // 步骤3：演示日志功能
        // ========================================================
        
        Logger.Info("Program", "");
        Logger.Info("Program", "========== 日志功能演示 ==========");
        
        // 普通日志
        Logger.Info("Demo", "这是一条普通信息日志");
        Logger.Warning("Demo", "这是一条警告日志: {0}", "中等");
        Logger.Error("Demo", "这是一条错误日志");
        
        // 带参数的日志
        Logger.Info("Demo", "玩家 {0} 登录成功，IP地址: {1}", 12345, "192.168.1.100");
        
        // 带异常的日志
        try
        {
            throw new InvalidOperationException("这是一个演示异常");
        }
        catch (Exception ex)
        {
            Logger.Error("Demo", ex, "捕获到异常: {0}", ex?.Message);
        }
        
        // 演示追踪上下文
        Logger.Info("Program", "");
        Logger.Info("Program", "========== 追踪上下文演示 ==========");
        
        using (TraceContext.Create())
        {
            Logger.Info("Trace", "开始处理请求");
            Logger.Info("Trace", "追踪ID: {0}", TraceContext.Current?.TraceId ?? "N/A");
            
            // 在追踪上下文中记录玩家信息
            using (PlayerContext.Create(10001, 50001))
            {
                Logger.Info("Trace", "玩家 {0} 的会话 {1} 开始处理",
                    PlayerContext.Current?.PlayerId,
                    PlayerContext.Current?.SessionId);
                
                Logger.Info("Trace", "模拟处理玩家数据...");
                
                Logger.Info("Trace", "玩家 {0} 处理完成", 
                    PlayerContext.Current?.PlayerId);
            }
            
            Logger.Info("Trace", "请求处理完成");
        }
        
        // ========================================================
        // 步骤4：演示不同日志级别
        // ========================================================
        
        Logger.Info("Program", "");
        Logger.Info("Program", "========== 日志级别演示 ==========");
        Logger.Info("Program", "当前最小级别: {0}", LogLevel.Debug.ToString());
        
        Logger.Trace("Demo", "Trace 级别 - 最详细");
        Logger.Debug("Demo", "Debug 级别 - 调试信息");
        Logger.Info("Demo", "Info 级别 - 一般信息");
        Logger.Warning("Demo", "Warning 级别 - 警告");
        Logger.Error("Demo", "Error 级别 - 错误");
        Logger.Fatal("Demo", "Fatal 级别 - 致命错误");
        
        // ========================================================
        // 步骤5：优雅关闭
        // ========================================================
        
        Logger.Info("Program", "");
        Logger.Info("Program", "========== 关闭日志系统 ==========");
        
        await Logger.ShutdownAsync();
        
        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("  程序正常退出");
        Console.WriteLine("========================================");
    }
}
