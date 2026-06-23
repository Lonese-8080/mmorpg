using System.IO;
using MMORPG.Framework.Logging;
using MMORPG.Framework.Network;
using MMORPG.Framework.Observability;
using MMORPG.Framework.Security;

namespace MMORPG.Server;

/// <summary>
/// 游戏服务器入口点 - 最小化示例，用于验证框架能力
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // 1. 初始化日志系统（必须最先初始化）
        Logger.Initialize(new LogOptions
        {
            MinLevel = LogLevel.Info,
            EnableConsole = true,
            EnableFile = true,
            LogDirectory = "./logs"
        });

        Logger.Info("Server", "游戏服务器启动中...");

        // 2. 初始化消息序列化器（注册所有 Protobuf 消息类型）
        MessageSerializer.Initialize();

        // 2. 启动服务状态管理器
        await ServiceStateManager.Instance.StartAsync();

        // 3. 使用 MessageRouter 单例注册处理器
        var router = MessageRouter.Instance;

        // 注册心跳消息处理器
        router.RegisterHandler(MessageIds.C2S_Heartbeat, async (session, message) =>
        {
            var heartbeat = (C2S_Heartbeat)message;
            var response = new S2C_Heartbeat
            {
                ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ClientTime = heartbeat.ClientTime
            };
            await session.SendAsync(response);
            Logger.Debug("Network", "心跳: ConnectionId={0}", session.ConnectionId);
        });

        // 注册测试消息处理器（使用 S2C_Error 作为响应）
        router.RegisterHandler(0x00000100, async (session, message) =>
        {
            Logger.Info("Network", "测试消息: ConnectionId={0}", session.ConnectionId);
            await Task.Delay(10);
            var response = new S2C_Error { ErrorCode = 200, Message = "OK" };
            await session.SendAsync(response);
        });

        // 4. 创建 TCP 服务器
        // 注意：消息路由已在 MessageRouter.Instance 中配置（第32行）
        // TcpServer 会自动调用 MessageRouter.Instance.RouteAsync 处理消息
        var server = new TcpServer(new TcpServerOptions
        {
            Port = 7001,
            Backlog = 512,
            MaxConnections = 1000,
            ReceiveBufferSize = 8192,
            SendBufferSize = 8192,
            HeartbeatTimeoutSeconds = 30
        });

        // 5. 启动 HTTP 健康检查端点
        var healthEndpoint = new HealthEndpoint(8080);
        healthEndpoint.Start();

        // 6. 启动 TCP 服务器
        server.Start();

        Logger.Info("Server", "游戏服务器已启动: TCP端口=7001, HTTP端口=8080");
        Logger.Info("Server", "等待客户端连接...");

        // 7. 等待停机信号（容器化环境兼容）
        var tcs = new TaskCompletionSource<bool>();

        // 处理 Ctrl+C / SIGTERM
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult(true);
        };

        // 处理进程退出信号
        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            tcs.TrySetResult(true);
        };

        await tcs.Task;

        // 8. 优雅停机
        Logger.Info("Server", "服务器正在停止...");
        await ServiceStateManager.Instance.StopAsync();
        await server.StopAsync(5000);
        healthEndpoint.Stop();

        Logger.Info("Server", "游戏服务器已停止");
    }
}
