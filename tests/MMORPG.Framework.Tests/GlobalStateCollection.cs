// ====================================================================
// 全局共享状态测试隔离
//
// xUnit 默认按类内并行 + 类间串行策略调度测试。
// 但本项目存在两类使用全局静态状态的测试：
//   1. ConfigEncryptor.SetMasterKey / MasterKey   —— 静态字段
//   2. ConfigurationLoader._cache                —— ConcurrentDictionary
//   3. Logger                                     —— 全局单例
//
// 必须使用 [Collection] 将其归入同一序列（xUnit 集合内串行，集合间可并行），
// 并由 IDisposable 在每个测试前后清理状态。
// ====================================================================

using MMORPG.Framework.Configuration;
using Xunit;

namespace MMORPG.Framework.Tests;

/// <summary>
/// 标记"会修改全局静态状态"的测试集合。
/// xUnit 保证同一集合内的测试串行执行（不同测试类之间），且不与其他集合并行。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class GlobalStateCollection : ICollectionFixture<GlobalStateFixture>
{
    public const string Name = "GlobalStateShared";
}

/// <summary>
/// 全局状态 fixture：仅在集合开始/结束时清理一次。
/// 每个测试方法仍需通过 IDisposable 自己保存/恢复单测级状态。
/// </summary>
public class GlobalStateFixture : IDisposable
{
    public GlobalStateFixture()
    {
        // 集合开始：清空所有可能受测试污染的全局状态
        ConfigurationLoader.ClearCache();
        ConfigurationLoader.DisableAllFileWatchers();
        ConfigEncryptor.SetMasterKey(null);
    }

    public void Dispose()
    {
        // 集合结束：再次清理，避免污染后续其他集合
        ConfigurationLoader.ClearCache();
        ConfigurationLoader.DisableAllFileWatchers();
        ConfigEncryptor.SetMasterKey(null);
    }
}
