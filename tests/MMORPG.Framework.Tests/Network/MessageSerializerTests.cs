using Xunit;
using Google.Protobuf;
using MMORPG.Framework.Network;

namespace MMORPG.Framework.Tests.Network;

/// <summary>
/// 消息序列化测试
/// 
/// 测试消息序列化和反序列化是否正确
/// </summary>
[Collection("MessageSerializer")]
public class MessageSerializerTests
{
    [Fact]
    public void 消息序列化_登录请求_应该正确()
    {
        // Arrange
        var originalMessage = new C2S_Login
        {
            Account = "test_player",
            Password = "test_password_123"
        };

        // Act
        var serialized = MessageSerializer.Serialize(originalMessage);
        var deserialized = MessageSerializer.Deserialize(serialized) as C2S_Login;

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(MessageIds.C2S_Login, MessageSerializer.GetMessageId(deserialized));
        Assert.Equal("test_player", deserialized.Account);
        Assert.Equal("test_password_123", deserialized.Password);
    }

    [Fact]
    public void 消息序列化_登录响应_应该正确()
    {
        // Arrange
        var originalMessage = new S2C_LoginResult
        {
            Success = true,
            PlayerId = 1234567890L,
            Token = "abcdef123456",
            ErrorMessage = string.Empty
        };

        // Act
        var serialized = MessageSerializer.Serialize(originalMessage);
        var deserialized = MessageSerializer.Deserialize(serialized) as S2C_LoginResult;

        // Assert
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
        Assert.Equal(1234567890L, deserialized.PlayerId);
        Assert.Equal("abcdef123456", deserialized.Token);
    }

    [Fact]
    public void 消息序列化_心跳消息_应该正确()
    {
        // Arrange
        var originalMessage = new C2S_Heartbeat
        {
            ClientTime = 1234567890L
        };

        // Act
        var serialized = MessageSerializer.Serialize(originalMessage);
        var deserialized = MessageSerializer.Deserialize(serialized) as C2S_Heartbeat;

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(1234567890L, deserialized.ClientTime);
    }

    [Fact]
    public void 消息序列化_心跳响应_应该正确()
    {
        // Arrange
        var originalMessage = new S2C_Heartbeat
        {
            ServerTime = 9876543210L,
            ClientTime = 1234567890L
        };

        // Act
        var serialized = MessageSerializer.Serialize(originalMessage);
        var deserialized = MessageSerializer.Deserialize(serialized) as S2C_Heartbeat;

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(9876543210L, deserialized.ServerTime);
        Assert.Equal(1234567890L, deserialized.ClientTime);
    }

    [Fact]
    public void 消息序列化_服务器公告_应该正确()
    {
        // Arrange
        var originalMessage = new S2C_ServerNotice
        {
            Notice = "欢迎来到游戏世界！"
        };

        // Act
        var serialized = MessageSerializer.Serialize(originalMessage);
        var deserialized = MessageSerializer.Deserialize(serialized) as S2C_ServerNotice;

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("欢迎来到游戏世界！", deserialized.Notice);
    }

    [Fact]
    public void 消息序列化_错误消息_应该正确()
    {
        // Arrange
        var originalMessage = new S2C_Error
        {
            ErrorCode = 404,
            Message = "未找到资源"
        };

        // Act
        var serialized = MessageSerializer.Serialize(originalMessage);
        var deserialized = MessageSerializer.Deserialize(serialized) as S2C_Error;

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(404, deserialized.ErrorCode);
        Assert.Equal("未找到资源", deserialized.Message);
    }

    [Fact]
    public void 消息序列化_消息头解析_应该正确()
    {
        // Arrange
        var message = new C2S_Heartbeat { ClientTime = 100L };
        var serialized = MessageSerializer.Serialize(message);

        // Act & Assert
        var result = MessageSerializer.TryParseHeader(serialized, 0, out var bodyLength, out var messageId);

        Assert.True(result);
        Assert.Equal(MessageIds.C2S_Heartbeat, messageId);
        Assert.True(bodyLength > 0);
        Assert.Equal(MessageSerializer.HeaderSize + bodyLength, serialized.Length);
    }

    [Fact]
    public void 消息序列化_数据不足_应该返回False()
    {
        // Arrange - 只有4字节（不够消息头8字节）
        var partialData = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        // Act
        var result = MessageSerializer.TryParseHeader(partialData, 0, out _, out _);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void 消息序列化_消息ID分配_应该正确()
    {
        // Assert - 验证所有框架层消息ID在正确的范围内
        Assert.True(MessageIds.IsFrameworkMessage(MessageIds.C2S_Login));
        Assert.True(MessageIds.IsFrameworkMessage(MessageIds.S2C_LoginResult));
        Assert.True(MessageIds.IsFrameworkMessage(MessageIds.C2S_Heartbeat));
        Assert.True(MessageIds.IsFrameworkMessage(MessageIds.S2C_Heartbeat));
        Assert.True(MessageIds.IsFrameworkMessage(MessageIds.S2C_ServerNotice));
        Assert.True(MessageIds.IsFrameworkMessage(MessageIds.S2C_Error));

        // 验证消息ID不重复
        var ids = new HashSet<uint>
        {
            MessageIds.C2S_Login,
            MessageIds.S2C_LoginResult,
            MessageIds.C2S_Heartbeat,
            MessageIds.S2C_Heartbeat,
            MessageIds.C2S_EnterWorld,
            MessageIds.S2C_EnterWorld,
            MessageIds.S2C_ServerNotice,
            MessageIds.S2C_Error,
        };
        // 8个消息，应该有8个唯一ID
        Assert.Equal(8, ids.Count);
    }
}