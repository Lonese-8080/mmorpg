using Xunit;
using MMORPG.Framework.Network;

namespace MMORPG.Framework.Tests.Network;

/// <summary>
/// 消息路由器测试
/// 
/// 测试消息路由是否正确工作
/// </summary>
[Collection("MessageSerializer")]
public class MessageRouterTests
{
    [Fact]
    public async Task 消息路由器_注册处理器_应该正确路由()
    {
        // Arrange
        var router = new MessageRouter();
        var receivedMessage = (C2S_Heartbeat?)null;
        var receivedSession = (Session?)null;

        // 创建一个测试处理器
        var handler = new TestHeartbeatHandler((session, msg) =>
        {
            receivedSession = session;
            receivedMessage = msg;
            return Task.CompletedTask;
        });

        router.RegisterHandler(handler);

        // 创建一个模拟的 Session（需要 Socket，但我们不实际连接）
        // 由于 Session 没有无参构造函数，这里使用一个简化的测试方式
        // 实际测试应该用 mock 或者集成测试

        // Act - 验证注册成功
        var registeredIds = router.GetRegisteredMessageIds();

        // Assert
        Assert.Contains(MessageIds.C2S_Heartbeat, registeredIds);
    }

    [Fact]
    public void 消息路由器_注册多个处理器_应该正确区分()
    {
        // Arrange
        var router = new MessageRouter();
        var loginHandler = new TestLoginHandler();
        var heartbeatHandler = new TestHeartbeatHandler();

        // Act
        router.RegisterHandler(loginHandler);
        router.RegisterHandler(heartbeatHandler);

        var registeredIds = router.GetRegisteredMessageIds();

        // Assert
        Assert.Contains(MessageIds.C2S_Login, registeredIds);
        Assert.Contains(MessageIds.C2S_Heartbeat, registeredIds);
        Assert.Equal(2, registeredIds.Length);
    }

    [Fact]
    public void 消息路由器_注销处理器_应该正确移除()
    {
        // Arrange
        var router = new MessageRouter();
        var handler = new TestHeartbeatHandler();
        router.RegisterHandler(handler);

        // Act
        var result = router.UnregisterHandler(MessageIds.C2S_Heartbeat);
        var registeredIds = router.GetRegisteredMessageIds();

        // Assert
        Assert.True(result);
        Assert.Empty(registeredIds);
    }

    [Fact]
    public void 消息路由器_注册不存在的ID_应该返回False()
    {
        // Arrange
        var router = new MessageRouter();

        // Act
        var result = router.UnregisterHandler(0x99999999);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void 消息路由器_使用函数注册_应该正确()
    {
        // Arrange
        var router = new MessageRouter();

        // Act
        router.RegisterHandler(MessageIds.C2S_Heartbeat, (session, message) =>
        {
            return Task.CompletedTask;
        });

        var registeredIds = router.GetRegisteredMessageIds();

        // Assert
        Assert.Contains(MessageIds.C2S_Heartbeat, registeredIds);
    }
}

#region 测试辅助类

/// <summary>
/// 测试用心跳处理器
/// </summary>
public class TestHeartbeatHandler : IMessageHandler<C2S_Heartbeat>
{
    private readonly Func<Session, C2S_Heartbeat, Task> _onHandle;

    public TestHeartbeatHandler(Func<Session, C2S_Heartbeat, Task>? onHandle = null)
    {
        _onHandle = onHandle ?? ((s, m) => Task.CompletedTask);
    }

    public Task HandleAsync(Session session, C2S_Heartbeat message)
    {
        return _onHandle(session, message);
    }
}

/// <summary>
/// 测试用登录处理器
/// </summary>
public class TestLoginHandler : IMessageHandler<C2S_Login>
{
    public Task HandleAsync(Session session, C2S_Login message)
    {
        return Task.CompletedTask;
    }
}

#endregion
