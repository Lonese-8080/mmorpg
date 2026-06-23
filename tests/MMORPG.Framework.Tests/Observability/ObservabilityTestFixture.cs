using Xunit;
using MMORPG.Framework.Network;

namespace MMORPG.Framework.Tests.Observability;

/// <summary>
/// 可观测性模块测试集合定义
/// 
/// 由于 MetricsCollector 和 HealthCheckService 是单例，
/// 为了避免测试间的状态竞争，将所有可观测性测试
/// 归入同一集合，保证它们在同一个线程上顺序执行。
/// </summary>
[CollectionDefinition("Observability")]
public class ObservabilityCollection : ICollectionFixture<ObservabilityTestFixture>
{
}

/// <summary>
/// 可观测性模块测试全局初始化/清理
/// 
/// 在所有可观测性测试运行之前执行一次，
/// 在全部测试运行完成后再执行一次清理。
/// </summary>
public class ObservabilityTestFixture : IDisposable
{
    public ObservabilityTestFixture()
    {
        // 确保消息类型已注册
        MessageSerializer.Initialize();
        // 运行前清理：确保单例处于干净状态
        MMORPG.Framework.Observability.MetricsCollector.Instance.ResetAll();
        MMORPG.Framework.Observability.HealthCheckService.Instance.Clear();
    }

    public void Dispose()
    {
        // 运行后清理
        MMORPG.Framework.Observability.MetricsCollector.Instance.ResetAll();
        MMORPG.Framework.Observability.HealthCheckService.Instance.Clear();
    }
}
