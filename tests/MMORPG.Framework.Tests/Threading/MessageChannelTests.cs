using Xunit;
using MMORPG.Framework.Threading;

namespace MMORPG.Framework.Tests.Threading;

/// <summary>
/// 消息队列 MessageChannel 测试
/// </summary>
public class MessageChannelTests
{
    [Fact]
    public void 新队列_应该为空()
    {
        // Arrange & Act
        var channel = new MessageChannel<int>();

        // Assert
        Assert.True(channel.IsEmpty);
        Assert.Equal(0, channel.Count);
    }

    [Fact]
    public void 写入消息_应该能读取()
    {
        // Arrange
        var channel = new MessageChannel<int>();

        // Act
        var writeResult = channel.Write(42);
        var readResult = channel.TryRead(out var value);

        // Assert
        Assert.True(writeResult);
        Assert.True(readResult);
        Assert.Equal(42, value);
        Assert.True(channel.IsEmpty);
    }

    [Fact]
    public void 读取空队列_应该返回False()
    {
        // Arrange
        var channel = new MessageChannel<int>();

        // Act
        var result = channel.TryRead(out var value);

        // Assert
        Assert.False(result);
        Assert.Equal(0, value);
    }

    [Fact]
    public void DrainAll_应该取出所有消息()
    {
        // Arrange
        var channel = new MessageChannel<int>();

        // Act
        for (var i = 0; i < 100; i++)
        {
            channel.Write(i);
        }

        var messages = channel.DrainAll().ToList();

        // Assert
        Assert.Equal(100, messages.Count);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(i, messages[i]);
        }
        Assert.True(channel.IsEmpty);
    }

    [Fact]
    public void DrainUpTo_应该限制数量()
    {
        // Arrange
        var channel = new MessageChannel<int>();

        // Act - 写入 100 条消息，但只取 10 条
        for (var i = 0; i < 100; i++)
        {
            channel.Write(i);
        }

        var messages = channel.DrainUpTo(10);

        // Assert
        Assert.Equal(10, messages.Count);
        Assert.Equal(90, channel.Count);  // 剩下 90 条

        // 再取剩下的
        var rest = channel.DrainAll().ToList();
        Assert.Equal(90, rest.Count);
    }

    [Fact]
    public void 有界队列_满时_应该丢弃旧消息()
    {
        // Arrange - 容量为 10 的队列
        var channel = new MessageChannel<int>(10);

        // Act - 写入 20 条消息
        for (var i = 0; i < 20; i++)
        {
            channel.Write(i);
        }

        var messages = channel.DrainAll().ToList();

        // Assert - 最多保留 10 条，是最新的 10 条
        Assert.True(messages.Count <= 10, $"实际数量: {messages.Count}");
        // 最后一条消息应该是 19
        Assert.Equal(19, messages[messages.Count - 1]);
    }

    [Fact]
    public void 批量写入_应该正确()
    {
        // Arrange
        var channel = new MessageChannel<int>();
        var items = new[] { 1, 2, 3, 4, 5 };

        // Act
        var written = channel.WriteRange(items);

        // Assert
        Assert.Equal(5, written);
        Assert.Equal(5, channel.Count);
    }

    [Fact]
    public void 清空队列_应该清空()
    {
        // Arrange
        var channel = new MessageChannel<int>();
        channel.Write(1);
        channel.Write(2);
        channel.Write(3);

        // Act
        channel.Clear();

        // Assert
        Assert.True(channel.IsEmpty);
        Assert.Equal(0, channel.Count);
    }

    [Fact]
    public void 多线程写入_应该线程安全()
    {
        // Arrange
        var channel = new MessageChannel<int>();
        var threadCount = 10;
        var perThread = 1000;

        // Act - 多个线程同时写入
        var tasks = new Task[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            var threadStart = t * perThread;
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < perThread; i++)
                {
                    channel.Write(threadStart + i);
                }
            });
        }

        Task.WaitAll(tasks);

        // Assert - 所有消息都成功写入
        Assert.Equal(threadCount * perThread, channel.Count);
    }

    [Fact]
    public void 工厂方法_CreateUnbounded_应该正确()
    {
        // Act
        var channel = MessageChannel.CreateUnbounded<string>();

        // Assert
        channel.Write("hello");
        Assert.Equal(1, channel.Count);
        Assert.True(channel.TryRead(out var value));
        Assert.Equal("hello", value);
    }

    [Fact]
    public void 工厂方法_CreateBounded_应该正确()
    {
        // Act
        var channel = MessageChannel.CreateBounded<string>(100);

        // Assert
        Assert.Equal(100, channel.Capacity);
        channel.Write("test");
        Assert.Equal(1, channel.Count);
    }

    [Fact]
    public void 字符串消息_应该正确读写()
    {
        // Arrange
        var channel = new MessageChannel<string>();

        // Act
        channel.Write("Hello");
        channel.Write("World");
        channel.Write("Game");

        var messages = channel.DrainAll().ToList();

        // Assert
        Assert.Equal(3, messages.Count);
        Assert.Equal("Hello", messages[0]);
        Assert.Equal("World", messages[1]);
        Assert.Equal("Game", messages[2]);
    }

    [Fact]
    public void 引用类型消息_应该不丢失()
    {
        // Arrange
        var channel = new MessageChannel<List<int>>();
        var list1 = new List<int> { 1, 2, 3 };
        var list2 = new List<int> { 4, 5, 6 };

        // Act
        channel.Write(list1);
        channel.Write(list2);

        var messages = channel.DrainAll().ToList();

        // Assert
        Assert.Equal(2, messages.Count);
        Assert.Equal(3, messages[0].Count);
        Assert.Equal(3, messages[1].Count);
    }

    [Fact]
    public void DrainAll_多次调用_应该正确()
    {
        // Arrange
        var channel = new MessageChannel<int>();

        // Act - 第一次
        channel.Write(1);
        channel.Write(2);
        var firstBatch = channel.DrainAll().ToList();

        channel.Write(3);
        channel.Write(4);
        channel.Write(5);
        var secondBatch = channel.DrainAll().ToList();

        // Assert
        Assert.Equal(2, firstBatch.Count);
        Assert.Equal(3, secondBatch.Count);
        Assert.True(channel.IsEmpty);
    }
}
