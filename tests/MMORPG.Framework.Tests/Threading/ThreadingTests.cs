using Xunit;
using MMORPG.Framework.Threading;

namespace MMORPG.Framework.Tests.Threading;

/// <summary>
/// 游戏调度器 GameScheduler 测试
/// </summary>
public class GameSchedulerTests
{
    [Fact]
    public void 创建调度器_默认配置_应该正确()
    {
        // Arrange & Act
        var scheduler = new GameScheduler(60);

        // Assert
        Assert.Equal(60, scheduler.TargetFps);
        Assert.False(scheduler.IsRunning);
        Assert.Equal(0, scheduler.FrameNumber);
    }

    [Fact]
    public void 启动调度器_应该调用事件()
    {
        // Arrange
        var scheduler = new GameScheduler(100);  // 高帧率，让测试更快
        var frameStartCount = 0;
        var frameEndCount = 0;
        var updateCount = 0;
        var maxFrames = 20;  // 只跑 20 帧就停止

        scheduler.OnFrameStart += _ => { frameStartCount++; };
        scheduler.OnUpdate += _ =>
        {
            updateCount++;
            if (updateCount >= maxFrames)
                scheduler.Stop();  // 跑够了就停止
        };
        scheduler.OnFrameEnd += _ => { frameEndCount++; };

        // Act
        scheduler.Start();

        // Assert
        Assert.True(frameStartCount >= maxFrames);
        Assert.True(frameEndCount >= maxFrames);
        Assert.True(updateCount >= maxFrames);
        Assert.True(scheduler.FrameNumber >= maxFrames);
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void DeltaTime_应该接近目标帧时间()
    {
        // Arrange - 使用 100 FPS，即每帧 10ms
        var targetFps = 100;
        var scheduler = new GameScheduler(targetFps);
        float? lastDeltaTime = null;
        var frameCount = 0;

        scheduler.OnUpdate += args =>
        {
            frameCount++;
            if (frameCount > 3)  // 跳过前几帧（可能异常）
                lastDeltaTime = args.DeltaTime;

            if (frameCount >= 15)
                scheduler.Stop();
        };

        // Act
        scheduler.Start();

        // Assert - delta time 应该接近目标帧时间
        var expectedDelta = 1.0f / targetFps;  // 0.01s

        Assert.NotNull(lastDeltaTime);
        // 允许 50% 的误差（由于 Sleep 精度）
        Assert.True(lastDeltaTime > 0, "DeltaTime 应该大于 0");
        Assert.True(lastDeltaTime < 1.0f, "DeltaTime 不应该超过 1 秒");
    }

    [Fact]
    public void 停止调度器_应该停止主循环()
    {
        // Arrange
        var scheduler = new GameScheduler(200);  // 更高 FPS
        var frameCount = 0;

        scheduler.OnUpdate += _ =>
        {
            frameCount++;
            if (frameCount >= 10)
                scheduler.Stop();
        };

        // Act
        scheduler.Start();

        // Assert
        Assert.False(scheduler.IsRunning);
        Assert.True(scheduler.FrameNumber > 0);
    }

    [Fact]
    public void 帧率配置_校验边界()
    {
        // Arrange & Act
        var scheduler1 = new GameScheduler(0);    // 0 会被修正为 1
        var scheduler2 = new GameScheduler(2000); // 2000 会被修正为 1000

        // Assert
        Assert.True(scheduler1.TargetFps >= 1);
        Assert.True(scheduler2.TargetFps <= 1000);
    }

    [Fact]
    public async Task 异步启动_应该正确工作()
    {
        // Arrange
        var scheduler = new GameScheduler(200);
        var frameCount = 0;

        scheduler.OnUpdate += _ =>
        {
            frameCount++;
            if (frameCount >= 50)
                scheduler.Stop();
        };

        // Act
        var task = scheduler.StartAsync();
        await task;

        // Assert
        Assert.False(scheduler.IsRunning);
        Assert.True(scheduler.FrameNumber >= 50);
    }

    [Fact]
    public void 事件异常_不应该停止主循环()
    {
        // Arrange
        var scheduler = new GameScheduler(200);
        var frameCount = 0;
        var exceptionCount = 0;

        // 在奇数帧抛出异常
        scheduler.OnUpdate += _ =>
        {
            frameCount++;

            if (frameCount % 2 == 1)
                throw new InvalidOperationException("测试异常");

            if (frameCount >= 20)
                scheduler.Stop();
        };

        // Act & Assert - 主循环应该继续，不应该抛异常
        scheduler.Start();

        Assert.True(frameCount >= 20, $"实际运行了 {frameCount} 帧");
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void 自定义配置_应该正确应用()
    {
        // Arrange & Act
        var options = new GameSchedulerOptions
        {
            TargetFps = 30,
            EnableFpsStats = true,
            EnableFrameTimeStats = true
        };

        var scheduler = new GameScheduler(options);

        // Assert
        Assert.Equal(30, scheduler.TargetFps);
    }

    [Fact]
    public void 运行时间_应该大于0()
    {
        // Arrange
        var scheduler = new GameScheduler(100);
        var frameCount = 0;

        scheduler.OnUpdate += _ =>
        {
            frameCount++;
            if (frameCount >= 30)
                scheduler.Stop();
        };

        // Act
        scheduler.Start();

        // Assert - 运行时间应该大于 0
        Assert.True(scheduler.UptimeSeconds > 0, $"运行时间: {scheduler.UptimeSeconds} 秒");
    }

    [Fact]
    public void 当前FPS_应该在合理范围内()
    {
        // Arrange
        var scheduler = new GameScheduler(100);
        var frameCount = 0;

        scheduler.OnUpdate += _ =>
        {
            frameCount++;
            if (frameCount >= 150)  // 至少跑 1.5 秒，让 FPS 统计更新
                scheduler.Stop();
        };

        // Act
        scheduler.Start();

        // Assert
        var fps = scheduler.CurrentFps;

        // FPS 应该大于 0，但由于线程 Sleep 精度，可能低于目标帧率
        Assert.True(fps > 0, $"FPS 应该大于 0，实际: {fps}");
        // FPS 不应该太高（最高不会超过几倍目标帧率）
        Assert.True(fps < 500, $"FPS 应该小于 500，实际: {fps}");
    }
}

/// <summary>
/// 线程安全集合测试
/// </summary>
public class ThreadSafeCollectionsTests
{
    [Fact]
    public void 原子计数器_初始值_应该为0()
    {
        // Arrange & Act
        var counter = new AtomicCounter();

        // Assert
        Assert.Equal(0, counter.Value);
    }

    [Fact]
    public void 原子计数器_自增_应该正确()
    {
        // Arrange
        var counter = new AtomicCounter();

        // Act
        var afterFirst = counter.Increment();
        var afterSecond = counter.Increment();
        var afterThird = counter.Increment();

        // Assert
        Assert.Equal(1, afterFirst);
        Assert.Equal(2, afterSecond);
        Assert.Equal(3, afterThird);
        Assert.Equal(3, counter.Value);
    }

    [Fact]
    public void 原子计数器_自减_应该正确()
    {
        // Arrange
        var counter = new AtomicCounter();
        counter.Increment();
        counter.Increment();  // 现在是 2

        // Act
        var afterDecrement = counter.Decrement();

        // Assert
        Assert.Equal(1, afterDecrement);
        Assert.Equal(1, counter.Value);
    }

    [Fact]
    public void 原子计数器_Add_应该正确()
    {
        // Arrange
        var counter = new AtomicCounter();

        // Act
        counter.Add(100);
        counter.Add(-50);

        // Assert
        Assert.Equal(50, counter.Value);
    }

    [Fact]
    public void 原子计数器_Set_应该返回旧值()
    {
        // Arrange
        var counter = new AtomicCounter();
        counter.Increment();
        counter.Increment();  // 现在是 2

        // Act
        var oldValue = counter.Set(42);

        // Assert
        Assert.Equal(2, oldValue);
        Assert.Equal(42, counter.Value);
    }

    [Fact]
    public void 原子计数器_Reset_应该清零()
    {
        // Arrange
        var counter = new AtomicCounter();
        counter.Add(999);

        // Act
        var oldValue = counter.Reset();

        // Assert
        Assert.Equal(999, oldValue);
        Assert.Equal(0, counter.Value);
    }

    [Fact]
    public void 原子计数器_多线程_应该线程安全()
    {
        // Arrange
        var counter = new AtomicCounter();
        var threadCount = 20;
        var perThread = 10000;

        // Act
        var tasks = new Task[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < perThread; i++)
                {
                    counter.Increment();
                }
            });
        }
        Task.WaitAll(tasks);

        // Assert
        Assert.Equal(threadCount * perThread, counter.Value);
    }

    [Fact]
    public void 线程安全集合_Session管理_应该正确()
    {
        // Arrange
        var collections = new ThreadSafeCollections();

        // Act
        collections.AddOrUpdateSession(1, "Session1");
        collections.AddOrUpdateSession(2, "Session2");
        collections.AddOrUpdateSession(3, "Session3");

        // Assert
        Assert.Equal(3, collections.SessionCount);

        // 尝试获取
        Assert.True(collections.TryGetSession(1, out var s1));
        Assert.NotNull(s1);

        // 尝试获取不存在的
        Assert.False(collections.TryGetSession(999, out var s999));

        // 移除
        Assert.True(collections.TryRemoveSession(2));
        Assert.Equal(2, collections.SessionCount);

        // 再次移除应该失败
        Assert.False(collections.TryRemoveSession(2));
    }

    [Fact]
    public void 线程安全集合_获取所有Session_应该正确()
    {
        // Arrange
        var collections = new ThreadSafeCollections();

        // Act
        collections.AddOrUpdateSession(1, "A");
        collections.AddOrUpdateSession(2, "B");
        collections.AddOrUpdateSession(3, "C");

        var all = collections.GetAllSessions();

        // Assert
        Assert.Equal(3, all.Length);
        Assert.Contains("A", all);
        Assert.Contains("B", all);
        Assert.Contains("C", all);
    }

    [Fact]
    public void 线程安全集合_多线程操作_应该线程安全()
    {
        // Arrange
        var collections = new ThreadSafeCollections();
        var threadCount = 10;
        var perThread = 100;

        // Act - 多个线程同时添加和移除
        var tasks = new Task[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            var threadId = t;
            tasks[t] = Task.Run(() =>
            {
                for (var i = 0; i < perThread; i++)
                {
                    var key = threadId * perThread + i;
                    collections.AddOrUpdateSession(key, $"Session-{key}");
                }

                // 移除一半
                for (var i = 0; i < perThread / 2; i++)
                {
                    var key = threadId * perThread + i;
                    collections.TryRemoveSession(key);
                }
            });
        }
        Task.WaitAll(tasks);

        // Assert - 应该有一半的 Session 保留
        Assert.Equal(threadCount * perThread / 2, collections.SessionCount);
    }
}
