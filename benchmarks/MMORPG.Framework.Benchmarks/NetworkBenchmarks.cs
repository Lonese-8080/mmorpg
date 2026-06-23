// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using BenchmarkDotNet.Attributes;
using MMORPG.Framework.Network;

namespace MMORPG.Framework.Benchmarks;

/// <summary>
/// 消息序列化基准测试
/// 
/// 测试场景：
/// - Protobuf 序列化/反序列化
/// - 不同消息大小
/// - 批量序列化
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class NetworkBenchmarks
{
    private C2S_Login? _loginMessage;
    private S2C_ServerNotice? _noticeMessage;
    private byte[]? _serializedLogin;
    private byte[]? _serializedNotice;

    [GlobalSetup]
    public void Setup()
    {
        _loginMessage = new C2S_Login
        {
            Account = "test_account_12345",
            Password = "test_password_67890"
        };

        _noticeMessage = new S2C_ServerNotice
        {
            Content = new string('X', 1000),
            Title = "Server Notice Title"
        };

        _serializedLogin = MessageSerializer.Serialize(_loginMessage);
        _serializedNotice = MessageSerializer.Serialize(_noticeMessage);
    }

    [Benchmark(Baseline = true)]
    public void Serialize_LoginMessage()
    {
        MessageSerializer.Serialize(_loginMessage!);
    }

    [Benchmark]
    public void Serialize_NoticeMessage()
    {
        MessageSerializer.Serialize(_noticeMessage!);
    }

    [Benchmark]
    public void Deserialize_LoginMessage()
    {
        MessageSerializer.Deserialize(_serializedLogin!);
    }

    [Benchmark]
    public void Deserialize_NoticeMessage()
    {
        MessageSerializer.Deserialize(_serializedNotice!);
    }

    [Benchmark]
    public void RoundTrip_LoginMessage_100x()
    {
        for (int i = 0; i < 100; i++)
        {
            var serialized = MessageSerializer.Serialize(_loginMessage!);
            MessageSerializer.Deserialize(serialized);
        }
    }

    [Benchmark]
    public void RoundTrip_NoticeMessage_100x()
    {
        for (int i = 0; i < 100; i++)
        {
            var serialized = MessageSerializer.Serialize(_noticeMessage!);
            MessageSerializer.Deserialize(serialized);
        }
    }
}
