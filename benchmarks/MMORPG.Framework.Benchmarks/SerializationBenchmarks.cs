#if FALSE // TODO: 重新实现为 Protobuf IMessage
using BenchmarkDotNet.Attributes;
using MMORPG.Framework.Network;

namespace MMORPG.Framework.Benchmarks;

/// <summary>
/// 序列化性能基准
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class SerializationBenchmarks
{
    private TestMessage _message = null!;
    private byte[] _serialized = null!;

    [GlobalSetup]
    public void Setup()
    {
        MessageSerializer.Register<TestMessage>();
        _message = new TestMessage { Id = 42, Name = "Benchmark", Value = 12345.6789 };
        _serialized = MessageSerializer.Serialize(_message);
    }

    [Benchmark(Baseline = true)]
    public byte[] Serialize()
    {
        return MessageSerializer.Serialize(_message);
    }

    [Benchmark]
    public TestMessage Deserialize()
    {
        var msg = new TestMessage();
        msg.Deserialize(_serialized, 0, _serialized.Length);
        return msg;
    }

    [Benchmark]
    public bool HeaderParse()
    {
        return MessageSerializer.TryParseHeader(_serialized, 0, out _, out _);
    }
}

public class TestMessage : MessageBase
{
    public override uint MessageId => 1;
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }

    protected override void SerializeCore(BinaryWriter writer)
    {
        writer.Write(Id);
        writer.Write(Name);
        writer.Write(Value);
    }

    protected override void DeserializeCore(BinaryReader reader)
    {
        Id = reader.ReadInt32();
        Name = reader.ReadString();
        Value = reader.ReadDouble();
    }
}
#endif