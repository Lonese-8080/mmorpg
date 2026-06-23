using Xunit;

namespace MMORPG.Framework.Tests.Logging;

/// <summary>
/// 日志系统测试
/// 
/// 测试日志记录器的各项功能
/// </summary>
public class LoggerTests
{
    [Fact]
    public void Logger_初始化_Should_Succeed()
    {
        // Arrange
        var options = new MMORPG.Framework.Logging.LogOptions
        {
            MinLevel = MMORPG.Framework.Logging.LogLevel.Debug,
            EnableConsole = true
        };

        // Act
        MMORPG.Framework.Logging.Logger.Initialize(options);

        // Assert
        // 如果没有抛出异常，说明初始化成功
    }

    [Fact]
    public void Logger_Info_Should_Not_Throw()
    {
        // Arrange
        var options = new MMORPG.Framework.Logging.LogOptions
        {
            MinLevel = MMORPG.Framework.Logging.LogLevel.Info,
            EnableConsole = true
        };

        MMORPG.Framework.Logging.Logger.Initialize(options);

        // Act & Assert - 不应该抛出异常
        var exception = Record.Exception(() =>
        {
            MMORPG.Framework.Logging.Logger.Info("Test", "测试日志");
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Logger_Error_With_Exception_Should_Not_Throw()
    {
        // Arrange
        var options = new MMORPG.Framework.Logging.LogOptions
        {
            MinLevel = MMORPG.Framework.Logging.LogLevel.Info,
            EnableConsole = true
        };

        MMORPG.Framework.Logging.Logger.Initialize(options);

        var testException = new InvalidOperationException("测试异常");

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            MMORPG.Framework.Logging.Logger.Error("Test", testException,
                "发生错误");
        });

        Assert.Null(exception);
    }
}
