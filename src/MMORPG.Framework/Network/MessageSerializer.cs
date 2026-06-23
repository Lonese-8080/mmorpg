// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Buffers;
using Google.Protobuf;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Network;

/// <summary>
/// 消息序列化器（Protobuf 实现）
///
/// TCP 消息包格式：
/// ┌───────────────────────────────────────────────────────────────┐
/// │  消息体长度(4字节 int) │  消息ID(4字节 uint) │  Protobuf消息体(N字节) │
/// └───────────────────────────────────────────────────────────────┘
///
/// 消息体由 Google.Protobuf IMessage.WriteTo() 序列化
///
/// 使用方式：
/// 1. 在应用启动时调用 Initialize() 注册所有消息类型
/// 2. 或在构建后由 Grpc.Tools 自动生成消息类
/// </summary>
public static class MessageSerializer
{
    /// <summary>
    /// 消息头大小（长度4 + 类型4 = 8字节）
    /// </summary>
    public const int HeaderSize = 8;

    /// <summary>
    /// 最大消息体大小（1MB）
    /// </summary>
    public const int MaxBodySize = 1024 * 1024;

    /// <summary>
    /// 消息工厂：根据消息 ID 创建消息实例
    ///
    /// 使用 Protobuf 生成的消息类（由 protoc 自动生成）
    /// </summary>
    private static readonly Dictionary<uint, Func<IMessage>> _messageFactory = new();

    /// <summary>
    /// 消息 ID 到消息类型的映射（用于序列化时获取 MessageId）
    /// </summary>
    private static readonly Dictionary<Type, uint> _typeToId = new();

    /// <summary>
    /// 是否已初始化
    /// </summary>
    private static bool _initialized = false;

    /// <summary>
    /// 初始化消息序列化器
    ///
    /// 注册所有 Protobuf 生成的消息类型。
    /// 在应用启动时调用。
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        // 框架层消息注册（由 protoc 自动生成）
        // 登录消息
        Register<C2S_Login>(MessageIds.C2S_Login);
        Register<S2C_LoginResult>(MessageIds.S2C_LoginResult);

        // 心跳消息
        Register<C2S_Heartbeat>(MessageIds.C2S_Heartbeat);
        Register<S2C_Heartbeat>(MessageIds.S2C_Heartbeat);

        // 进入世界消息
        Register<C2S_EnterWorld>(MessageIds.C2S_EnterWorld);
        Register<S2C_EnterWorld>(MessageIds.S2C_EnterWorld);

        // 服务器通知消息
        Register<S2C_ServerNotice>(MessageIds.S2C_ServerNotice);
        Register<S2C_Error>(MessageIds.S2C_Error);

        // 移动消息
        Register<C2S_PlayerMove>(MessageIds.C2S_PlayerMove);
        Register<S2C_PlayerPosition>(MessageIds.S2C_PlayerPosition);

        Logger.Debug("Network", "MessageSerializer 初始化完成，已注册 {0} 种消息类型", _messageFactory.Count);
        _initialized = true;
    }

    /// <summary>
    /// 注册自定义消息类型
    ///
    /// 使用示例：
    /// <code>
    /// MessageSerializer.Register&lt;C2S_Login&gt;(MessageIds.C2S_Login);
    /// </code>
    /// </summary>
    /// <typeparam name="T">消息类型，必须实现 IMessage 且有默认构造函数</typeparam>
    /// <param name="messageId">消息 ID</param>
    public static void Register<T>(uint messageId) where T : IMessage, new()
    {
        _messageFactory[messageId] = () => new T();
        _typeToId[typeof(T)] = messageId;
        Logger.Debug("Network", "注册消息类型: 0x{0:X8} -> {1}", messageId, typeof(T).Name);
    }

    /// <summary>
    /// 注册消息类型（使用工厂方法）
    /// </summary>
    /// <param name="messageId">消息 ID</param>
    /// <param name="factory">消息工厂方法</param>
    /// <param name="type">消息类型</param>
    public static void Register(uint messageId, Func<IMessage> factory, Type type)
    {
        _messageFactory[messageId] = factory;
        _typeToId[type] = messageId;
    }

    /// <summary>
    /// 序列化消息到池化缓冲区（零分配版本）
    ///
    /// 从 ArrayPool 租用缓冲区，将消息写入后返回。
    /// 调用方使用完毕后必须调用 <see cref="ReturnBuffer"/> 归还。
    /// </summary>
    /// <param name="message">要发送的消息</param>
    /// <param name="messageId">消息 ID</param>
    /// <param name="buffer">输出：序列化后的完整数据包（已从 ArrayPool 租用）</param>
    /// <returns>有效数据长度</returns>
    public static int SerializeToPooledBuffer(IMessage message, uint messageId, out byte[] buffer)
    {
        // 1. 计算 Protobuf 消息体长度
        var bodyLength = message.CalculateSize();

        // 2. 验证消息体长度
        if (bodyLength > MaxBodySize)
        {
            Logger.Warning("Network", "消息体长度超过限制: {0} > {1}", bodyLength, MaxBodySize);
            throw new InvalidOperationException($"消息体长度 {bodyLength} 超过最大限制 {MaxBodySize}");
        }

        // 3. 从 ArrayPool 租用缓冲区（预分配稍大的空间，避免频繁 resize）
        var totalSize = HeaderSize + bodyLength;
        buffer = ArrayPool<byte>.Shared.Rent(totalSize);

        // 4. 写入消息头
        // 消息体长度（4字节，小端序）
        buffer[0] = (byte)bodyLength;
        buffer[1] = (byte)(bodyLength >> 8);
        buffer[2] = (byte)(bodyLength >> 16);
        buffer[3] = (byte)(bodyLength >> 24);
        // 消息类型 ID（4字节，小端序）
        buffer[4] = (byte)messageId;
        buffer[5] = (byte)(messageId >> 8);
        buffer[6] = (byte)(messageId >> 16);
        buffer[7] = (byte)(messageId >> 24);

        // 5. 写入 Protobuf 消息体（指定精确长度，避免 buffer 过大导致写入失败）
        message.WriteTo(buffer.AsSpan(HeaderSize, bodyLength));

        return totalSize;
    }

    /// <summary>
    /// 归还池化缓冲区
    /// </summary>
    /// <param name="buffer">从 SerializeToPooledBuffer 获得的缓冲区</param>
    public static void ReturnBuffer(byte[] buffer)
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }

    /// <summary>
    /// 序列化消息为完整的 TCP 数据包（便利方法，内部使用池化缓冲区）
    ///
    /// 写入顺序：
    /// 1. 消息体长度 (4字节 int)
    /// 2. 消息类型 ID (4字节 uint)
    /// 3. Protobuf 消息体 (N字节)
    /// </summary>
    /// <param name="message">要发送的消息</param>
    /// <param name="messageId">消息 ID</param>
    /// <returns>完整的 TCP 数据包字节数组</returns>
    public static byte[] Serialize(IMessage message, uint messageId)
    {
        // 使用池化缓冲区（零额外分配）
        var pooled = SerializeToPooledBuffer(message, messageId, out var pooledBuffer);
        try
        {
            // 复制到结果数组（结果数组由调用方持有，不依赖池的生命周期）
            var result = new byte[pooled];
            Buffer.BlockCopy(pooledBuffer, 0, result, 0, pooled);
            return result;
        }
        finally
        {
            // 立即归还池化缓冲区
            ArrayPool<byte>.Shared.Return(pooledBuffer);
        }
    }

    /// <summary>
    /// 序列化消息为完整的 TCP 数据包（便利方法，自动获取 messageId）
    ///
    /// 从类型映射表自动获取已注册的 messageId。
    /// 如果类型未注册，抛出 InvalidOperationException。
    /// </summary>
    /// <param name="message">要发送的消息</param>
    /// <returns>完整的 TCP 数据包字节数组</returns>
    public static byte[] Serialize(IMessage message)
    {
        var messageId = GetMessageId(message);
        return Serialize(message, messageId);
    }

    /// <summary>
    /// 从完整字节数组反序列化消息
    ///
    /// 读取顺序：
    /// 1. 读取消息体长度 (4字节)
    /// 2. 读取消息类型 ID (4字节)
    /// 3. 根据 ID 创建消息实例
    /// 4. 使用 Protobuf 反序列化消息体
    /// </summary>
    /// <param name="data">完整的 TCP 数据包（包括消息头）</param>
    /// <returns>反序列化后的消息对象</returns>
    public static IMessage Deserialize(byte[] data)
    {
        return Deserialize(data, 0);
    }

    /// <summary>
    /// 从字节数组的指定位置反序列化消息（零分配版本）
    ///
    /// 直接在原始接收缓冲区上工作，避免为每条消息分配 byte[]
    /// 读取顺序与 Deserialize(byte[] data) 相同
    /// </summary>
    /// <param name="data">包含 TCP 数据包的字节数组</param>
    /// <param name="offset">当前消息的起始偏移</param>
    /// <returns>反序列化后的消息对象</returns>
    public static IMessage Deserialize(byte[] data, int offset)
    {
        // 1. 读取消息体长度
        var bodyLength = BitConverter.ToInt32(data, offset);

        // 2. 验证长度
        if (bodyLength < 0 || bodyLength > MaxBodySize)
        {
            throw new InvalidOperationException($"非法的消息体长度: {bodyLength}");
        }

        if (data.Length - offset < HeaderSize + bodyLength)
        {
            throw new InvalidOperationException(
                $"数据长度不足: 需要 {HeaderSize + bodyLength}，可用 {data.Length - offset}");
        }

        // 3. 读取消息类型
        var messageId = BitConverter.ToUInt32(data, offset + 4);

        // 4. 创建消息实例
        if (!_messageFactory.TryGetValue(messageId, out var factory))
        {
            throw new InvalidOperationException($"未注册的消息类型: 0x{messageId:X8}");
        }

        var message = factory();

        // 5. 使用 Protobuf 解析消息体（使用零分配切片版本）
        if (bodyLength > 0)
        {
            message.MergeFrom(data, offset + HeaderSize, bodyLength);
        }

        return message;
    }

    /// <summary>
    /// 从字节数组解析消息头（仅解析长度和类型）
    ///
    /// 用于粘包处理：先读取消息头，判断是否收到完整消息
    /// </summary>
    /// <param name="data">包含消息头的字节数据</param>
    /// <param name="offset">读取偏移</param>
    /// <param name="bodyLength">输出：消息体长度</param>
    /// <param name="messageId">输出：消息类型 ID</param>
    /// <returns>true 表示解析成功</returns>
    public static bool TryParseHeader(byte[] data, int offset, out int bodyLength, out uint messageId)
    {
        bodyLength = 0;
        messageId = 0;

        if (data == null || data.Length - offset < HeaderSize)
            return false;

        bodyLength = BitConverter.ToInt32(data, offset);
        messageId = BitConverter.ToUInt32(data, offset + 4);

        // 验证合理性
        if (bodyLength < 0 || bodyLength > MaxBodySize)
            return false;

        return true;
    }

    /// <summary>
    /// 获取消息类型的 MessageId
    /// </summary>
    /// <param name="message">消息对象</param>
    /// <returns>消息 ID，如果未注册则抛出异常</returns>
    public static uint GetMessageId(IMessage message)
    {
        if (_typeToId.TryGetValue(message.GetType(), out var messageId))
        {
            return messageId;
        }
        throw new InvalidOperationException($"未注册的消息类型: {message.GetType().Name}");
    }
}