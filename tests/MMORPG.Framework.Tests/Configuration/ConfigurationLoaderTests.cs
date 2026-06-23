using System.Text.Json;
using Xunit;
using MMORPG.Framework.Configuration;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Tests.Configuration;

/// <summary>
/// ConfigurationLoader 单元测试
///
/// 该类使用全局静态状态（ConfigurationLoader._cache / _fileWatchers / ConfigChanged 事件），
/// 必须加入 <see cref="GlobalStateCollection"/> 以保证串行执行。
/// </summary>
[Collection(GlobalStateCollection.Name)]
public class ConfigurationLoaderTests
{
    public ConfigurationLoaderTests()
    {
        // 初始化 Logger（用于测试日志输出）
        Logger.Initialize(new LogOptions
        {
            MinLevel = LogLevel.Debug,
            EnableConsole = false,
            EnableFile = false
        });
        ConfigurationLoader.ClearCache();
        ConfigurationLoader.DisableAllFileWatchers();
    }

    [Fact]
    public void Load_FromJsonFile_ReturnsConfig()
    {
        // 准备：创建临时 JSON 文件
        var tempFile = Path.Combine(Path.GetTempPath(), $"mmo_config_{Guid.NewGuid()}.json");
        var configJson = JsonSerializer.Serialize(new
        {
            Port = 8080,
            MaxConnections = 1000,
            Name = "TestServer"
        });
        File.WriteAllText(tempFile, configJson);

        try
        {
            // Act
            var config = ConfigurationLoader.Load<SimpleTestConfig>(tempFile);

            // Assert
            Assert.NotNull(config);
            Assert.Equal(8080, config.Port);
            Assert.Equal(1000, config.MaxConnections);
            Assert.Equal("TestServer", config.Name);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Reload_InvalidatesCache_ReturnsNewValue()
    {
        // 准备：创建临时 JSON 文件
        var tempFile = Path.Combine(Path.GetTempPath(), $"mmo_config_{Guid.NewGuid()}.json");
        var configJson = JsonSerializer.Serialize(new { Port = 8080 });
        File.WriteAllText(tempFile, configJson);

        try
        {
            // 第一次加载
            var config1 = ConfigurationLoader.Load<SimpleTestConfig>(tempFile);
            Assert.Equal(8080, config1.Port);

            // 修改文件
            var newJson = JsonSerializer.Serialize(new { Port = 9090 });
            File.WriteAllText(tempFile, newJson);

            // 等待文件系统更新（确保 LastWriteTime 不同）
            Thread.Sleep(100);

            // 重新加载
            var config2 = ConfigurationLoader.Reload<SimpleTestConfig>(tempFile);

            // Assert
            Assert.Equal(9090, config2.Port);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EnableFileWatcher_FileChanges_TriggersEvent()
    {
        // 准备
        var tempFile = Path.Combine(Path.GetTempPath(), $"mmo_config_{Guid.NewGuid()}.json");
        var configJson = JsonSerializer.Serialize(new { Port = 8080 });
        File.WriteAllText(tempFile, configJson);
        await Task.Delay(250); // 确保写入完成并刷新时间戳

        var eventCount = 0;
        object? newConfig = null;
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? s, ConfigChangedEventArgs e)
        {
            Interlocked.Increment(ref eventCount);
            newConfig = e.NewConfig;
            tcs.TrySetResult(eventCount);
        }

        try
        {
            ConfigurationLoader.EnableFileWatcher<SimpleTestConfig>(tempFile, intervalMs: 100);
            ConfigurationLoader.ConfigChanged += Handler;

            // 等待以确保监听器初始化
            await Task.Delay(150);

            // 修改文件内容（直接覆盖，确保时间戳更新）
            var newJson = JsonSerializer.Serialize(new { Port = 9090 });
            File.WriteAllText(tempFile, newJson);

            // 等待事件触发（最多 5 秒）
            var timeout = Task.Delay(5000);
            var completed = await Task.WhenAny(tcs.Task, timeout);

            Assert.True(completed == tcs.Task, "ConfigChanged 事件未在 5 秒内触发");
            Assert.True(eventCount >= 1, $"期望触发至少 1 次事件，实际 {eventCount} 次");
            Assert.NotNull(newConfig);
            Assert.Equal(9090, ((SimpleTestConfig)newConfig).Port);
        }
        finally
        {
            ConfigurationLoader.ConfigChanged -= Handler;
            ConfigurationLoader.DisableAllFileWatchers();
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void EnableFileWatcher_NonExistentFile_ReturnsSilently()
    {
        // 不应抛异常
        ConfigurationLoader.EnableFileWatcher<SimpleTestConfig>("nonexistent.json");
        // 没有崩溃就算通过
    }

    [Fact]
    public void DisableFileWatcher_AfterEnable_CleansUp()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mmo_config_{Guid.NewGuid()}.json");
        File.WriteAllText(tempFile, JsonSerializer.Serialize(new { Port = 8080 }));

        try
        {
            ConfigurationLoader.EnableFileWatcher<SimpleTestConfig>(tempFile, intervalMs: 100);
            ConfigurationLoader.DisableFileWatcher(tempFile);
            // 不抛异常就算通过
        }
        finally
        {
            ConfigurationLoader.DisableAllFileWatchers();
            File.Delete(tempFile);
        }
    }
}

/// <summary>
/// 简单测试配置类
/// </summary>
public class SimpleTestConfig
{
    public int Port { get; set; }
    public int MaxConnections { get; set; } = 100;
    public string Name { get; set; } = string.Empty;
}
