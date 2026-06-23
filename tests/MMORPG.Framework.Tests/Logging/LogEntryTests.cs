using Xunit;

namespace MMORPG.Framework.Tests.Logging;

/// <summary>
/// 日志条目测试
/// 
/// 测试日志条目的序列化和格式化功能
/// </summary>
public class LogEntryTests
{
    [Fact]
    public void LogEntry_创建_Should_Set_All_Properties()
    {
        // Arrange & Act
        var entry = new MMORPG.Framework.Logging.LogEntry(
            MMORPG.Framework.Logging.LogLevel.Info,
            "Test",
            "Test message",
            "Formatted message");

        // Assert
        Assert.Equal(MMORPG.Framework.Logging.LogLevel.Info, entry.Level);
        Assert.Equal("Test", entry.Source);
        Assert.Equal("Test message", entry.Message);
        Assert.Equal("Formatted message", entry.FormattedMessage);
        Assert.True(entry.Timestamp > DateTimeOffset.MinValue);
    }

    [Fact]
    public void LogEntry_ToJson_Should_Return_Valid_Json()
    {
        // Arrange
        var entry = new MMORPG.Framework.Logging.LogEntry(
            MMORPG.Framework.Logging.LogLevel.Info,
            "Test",
            "Test message",
            "Test message");

        // Act
        var json = entry.ToJson();

        // Assert
        Assert.NotNull(json);
        Assert.Contains("Test message", json);
        Assert.Contains("Test", json);
    }

    [Fact]
    public void LogEntry_ToConsoleString_Should_Include_Timestamp()
    {
        // Arrange
        var entry = new MMORPG.Framework.Logging.LogEntry(
            MMORPG.Framework.Logging.LogLevel.Info,
            "Test",
            "Test message",
            "Test message");

        // Act
        var consoleString = entry.ToConsoleString();

        // Assert
        Assert.Contains("Test message", consoleString);
        Assert.Contains("[INFO", consoleString);
    }
}
