using Xunit;
using MMORPG.Framework.Network;

namespace MMORPG.Framework.Tests.Network;

/// <summary>
/// 消息序列化器全局初始化 Fixture
///
/// 使用 xUnit 的 AssemblyFixture 模式，确保 MessageSerializer.Initialize()
/// 在整个测试程序集运行前只执行一次，避免并行测试时的并发冲突。
/// </summary>
public class MessageSerializerFixture
{
    public MessageSerializerFixture()
    {
        // 确保消息类型已注册（Initialize 有内部保护，多次调用不会重复注册）
        MessageSerializer.Initialize();
    }
}

/// <summary>
/// Assembly Fixture 定义 - 让所有需要 MessageSerializer 的测试类使用此集合
/// </summary>
[CollectionDefinition("MessageSerializer", DisableParallelization = true)]
public class MessageSerializerCollection : ICollectionFixture<MessageSerializerFixture>
{
}