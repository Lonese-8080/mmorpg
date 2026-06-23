# Protobuf 序列化迁移 Spec

## Why
当前框架已从自定义二进制序列化迁移到 Google Protobuf，但存在遗留问题：7 个测试文件被临时排除、文档需要同步更新、部分代码需要适配 Protobuf API。需要完成所有重构工作，确保框架完全适配 .NET 10 + Google.Protobuf 标准。

## What Changes
- 修复 7 个临时排除的测试文件，适配 Protobuf IMessage 接口
- 更新所有文档以反映 Protobuf 序列化方案
- 确保 MessageSerializer 完整支持 Protobuf API
- 验证所有测试通过

## Impact
- Affected specs: 序列化模块、测试模块
- Affected code: 
  - `tests/MMORPG.Framework.Tests/Network/MessageSerializerBenchmarks.cs`
  - `tests/MMORPG.Framework.Tests/Network/MessageRouterBenchmarks.cs`
  - `tests/MMORPG.Framework.Tests/Network/MessageSerializerTests.cs`
  - `tests/MMORPG.Framework.Tests/Network/TcpServerTests.cs`
  - `tests/MMORPG.Framework.Tests/Observability/MetricsIntegrationTests.cs`
  - `tests/MMORPG.Framework.Tests/Network/MessageRouterTests.cs`
  - `tests/MMORPG.Framework.Tests/Security/RateLimiterTests.cs`
  - `docs/05-序列化设计.md`
  - `docs/06-API参考.md`
  - `docs/09-变更日志.md`

## ADDED Requirements

### Requirement: Protobuf 测试适配
所有被排除的测试文件 SHALL 重新实现为使用 Protobuf 生成的消息类（如 `C2S_Login`、`C2S_Heartbeat` 等），而非旧的 `C2S_LoginMessage`、`C2S_HeartbeatMessage`。

#### Scenario: 测试编译通过
- **WHEN** 运行 `dotnet build MMORPG.sln`
- **THEN** 所有测试项目编译成功，无错误

#### Scenario: 测试执行通过
- **WHEN** 运行 `dotnet test MMORPG.sln`
- **THEN** 所有测试通过，包括之前被排除的 7 个测试文件

### Requirement: 文档同步更新
所有相关文档 SHALL 反映 Protobuf 序列化方案，包括：
- API 参考文档中的消息类名（不带 "Message" 后缀）
- 序列化设计文档中的 Protobuf 协议定义
- 变更日志记录完整的迁移过程

#### Scenario: 文档与源码一致
- **WHEN** 检查 `docs/06-API参考.md` 中的消息类定义
- **THEN** 类名与 Protobuf 生成的类名一致（如 `C2S_Login` 而非 `C2S_LoginMessage`）

### Requirement: MessageSerializer API 完整
MessageSerializer SHALL 提供完整的 Protobuf API，包括：
- `Serialize(IMessage message)` — 自动获取 messageId
- `Serialize(IMessage message, uint messageId)` — 显式指定 messageId
- `Deserialize(byte[] data)` — 自动解析消息类型
- `GetMessageId(IMessage message)` — 从类型映射表获取 messageId

#### Scenario: 序列化反序列化正常
- **WHEN** 创建 `C2S_Login` 消息并序列化
- **THEN** 得到有效的 TCP 数据包，可被反序列化为原始消息

## MODIFIED Requirements

### Requirement: 测试项目配置
测试项目 `MMORPG.Framework.Tests.csproj` SHALL 不再排除任何测试文件，所有测试文件都参与编译和执行。

## REMOVED Requirements

### Requirement: 旧的 MessageBase 类
**Reason**: Protobuf 生成的类直接实现 `Google.Protobuf.IMessage` 接口，不再需要自定义 `MessageBase` 基类。
**Migration**: 所有使用 `MessageBase` 的代码改为使用 Protobuf 生成的消息类。