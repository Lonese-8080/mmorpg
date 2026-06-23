using Xunit;
using MMORPG.Framework.Network;
using Google.Protobuf;

namespace MMORPG.Framework.Tests.Network;

/// <summary>
/// TCP 服务器配置测试
/// </summary>
public class TcpServerOptionsTests
{
    [Fact]
    public void 配置_默认值_应该正确()
    {
        // Act
        var options = new TcpServerOptions();

        // Assert
        Assert.Equal(9000, options.Port);
        Assert.Equal(200, options.Backlog);
        Assert.Equal(20, options.HeartbeatTimeoutSeconds);
        Assert.Equal(1, options.HeartbeatCheckIntervalSeconds);
        Assert.Equal(10000, options.MaxConnections);
        Assert.True(options.ReceiveBufferSize > 0);
        Assert.True(options.SendBufferSize > 0);
    }

    [Fact]
    public void 配置_自定义值_应该正确()
    {
        // Act
        var options = new TcpServerOptions
        {
            Port = 8080,
            Backlog = 500,
            HeartbeatTimeoutSeconds = 30,
            MaxConnections = 5000
        };

        // Assert
        Assert.Equal(8080, options.Port);
        Assert.Equal(500, options.Backlog);
        Assert.Equal(30, options.HeartbeatTimeoutSeconds);
        Assert.Equal(5000, options.MaxConnections);
    }
}

/// <summary>
/// 消息 ID 工具方法测试
/// </summary>
[Collection("MessageSerializer")]
public class MessageIdTests
{
    [Fact]
    public void MessageId_IsFrameworkMessage_应该正确()
    {
        // Assert - 框架消息在正确范围内
        Assert.True(MessageIds.IsFrameworkMessage(0x00000001));
        Assert.True(MessageIds.IsFrameworkMessage(0x00000FFF));

        // 非框架消息
        Assert.False(MessageIds.IsFrameworkMessage(0x00001000));
        Assert.False(MessageIds.IsFrameworkMessage(0x0000FFFF));
    }

    [Fact]
    public void MessageId_IsGameMessage_应该正确()
    {
        // Assert - 游戏消息在正确范围内
        Assert.True(MessageIds.IsGameMessage(0x00001000));
        Assert.True(MessageIds.IsGameMessage(0x00001FFF));

        // 框架层消息（不是游戏消息）
        // 注意：C2S_Login = 0x00000001 是框架层消息
        Assert.False(MessageIds.IsGameMessage(MessageIds.C2S_Login));
        Assert.False(MessageIds.IsGameMessage(MessageIds.C2S_Heartbeat));
    }

    [Fact]
    public void MessageId_GetDescription_应该正确()
    {
        // Assert - GetDescription 返回消息类名（用于日志和调试）
        Assert.Equal("C2S_Login", MessageIds.GetDescription(MessageIds.C2S_Login));
        Assert.Equal("S2C_LoginResult", MessageIds.GetDescription(MessageIds.S2C_LoginResult));
        Assert.Equal("C2S_Heartbeat", MessageIds.GetDescription(MessageIds.C2S_Heartbeat));
        Assert.Equal("S2C_Heartbeat", MessageIds.GetDescription(MessageIds.S2C_Heartbeat));
        Assert.Equal("S2C_ServerNotice", MessageIds.GetDescription(MessageIds.S2C_ServerNotice));
        Assert.Equal("S2C_Error", MessageIds.GetDescription(MessageIds.S2C_Error));

        // 未知消息 - 返回 "Unknown(0xXXXXXXXX)" 格式
        Assert.Contains("Unknown", MessageIds.GetDescription(0x99999999));
    }

    [Fact]
    public void MessageId_值分配_不应该重复()
    {
        // Arrange - 收集所有消息 ID
        var ids = new List<uint>
        {
            MessageIds.C2S_Login,
            MessageIds.S2C_LoginResult,
            MessageIds.C2S_Heartbeat,
            MessageIds.S2C_Heartbeat,
            MessageIds.C2S_EnterWorld,
            MessageIds.S2C_EnterWorld,
            MessageIds.S2C_ServerNotice,
            MessageIds.S2C_Error,
            MessageIds.C2S_PlayerMove,
            MessageIds.S2C_PlayerPosition,
        };

        // Assert - 所有 ID 应该唯一
        var uniqueIds = new HashSet<uint>(ids);
        Assert.Equal(ids.Count, uniqueIds.Count);
    }
}

/// <summary>
/// 消息基类测试
/// </summary>
[Collection("MessageSerializer")]
public class MessageBaseTests
{
    [Fact]
    public void 消息_MessageId_应该正确()
    {
        // Arrange
        var loginMsg = new C2S_Login();
        var heartbeatMsg = new C2S_Heartbeat();
        var noticeMsg = new S2C_ServerNotice();

        // Act & Assert
        Assert.Equal(MessageIds.C2S_Login, MessageSerializer.GetMessageId(loginMsg));
        Assert.Equal(MessageIds.C2S_Heartbeat, MessageSerializer.GetMessageId(heartbeatMsg));
        Assert.Equal(MessageIds.S2C_ServerNotice, MessageSerializer.GetMessageId(noticeMsg));
    }

    [Fact]
    public void 消息_默认值_应该正确()
    {
        // Arrange & Act
        var loginMsg = new C2S_Login();
        var resultMsg = new S2C_LoginResult();

        // Assert
        Assert.NotNull(loginMsg.Account);
        Assert.NotNull(loginMsg.Password);
        Assert.NotNull(resultMsg.Token);
        Assert.NotNull(resultMsg.ErrorMessage);
    }
}
