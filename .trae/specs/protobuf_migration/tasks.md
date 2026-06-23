# Tasks

## Phase 1: 测试文件适配 Protobuf

- [x] Task 1: 修复 MessageSerializerTests.cs — 适配 Protobuf 消息类
  - [x] SubTask 1.1: 将 `C2S_LoginMessage` 替换为 `C2S_Login`
  - [x] SubTask 1.2: 将 `S2C_LoginResultMessage` 替换为 `S2C_LoginResult`
  - [x] SubTask 1.3: 将 `C2S_HeartbeatMessage` 替换为 `C2S_Heartbeat`
  - [x] SubTask 1.4: 将 `S2C_HeartbeatMessage` 替换为 `S2C_Heartbeat`
  - [x] SubTask 1.5: 将 `S2C_ServerNoticeMessage` 替换为 `S2C_ServerNotice`
  - [x] SubTask 1.6: 将 `S2C_ErrorMessage` 替换为 `S2C_Error`
  - [x] SubTask 1.7: 添加 `using Google.Protobuf;` 引用
  - [x] SubTask 1.8: 修复测试逻辑以适配 Protobuf API

- [x] Task 2: 修复 MessageRouterTests.cs — 适配 Protobuf 消息类
  - [x] SubTask 2.1: 将 `C2S_HeartbeatMessage` 替换为 `C2S_Heartbeat`
  - [x] SubTask 2.2: 将 `C2S_LoginMessage` 替换为 `C2S_Login`
  - [x] SubTask 2.3: 添加 `using Google.Protobuf;` 引用
  - [x] SubTask 2.4: 修复 TestHeartbeatHandler 和 TestLoginHandler 类

- [x] Task 3: 修复 MessageSerializerBenchmarks.cs — 适配 Protobuf 消息类
  - [x] SubTask 3.1: 将所有 `*Message` 类名替换为 Protobuf 生成的类名
  - [x] SubTask 3.2: 添加 `using Google.Protobuf;` 引用
  - [x] SubTask 3.3: 修复基准测试逻辑
  - [x] SubTask 3.4: 修复 TestMessageDictionary 辅助类

- [x] Task 4: 修复 MessageRouterBenchmarks.cs — 适配 Protobuf 消息类
  - [x] SubTask 4.1: 将所有 `*Message` 类名替换为 Protobuf 生成的类名
  - [x] SubTask 4.2: 添加 `using Google.Protobuf;` 引用
  - [x] SubTask 4.3: 移除或修复 TestUnregisteredMessage 类
  - [x] SubTask 4.4: 修复基准测试逻辑

- [x] Task 5: 修复 TcpServerTests.cs — 适配 Protobuf 消息类
  - [x] SubTask 5.1: 将所有 `*Message` 类名替换为 Protobuf 生成的类名
  - [x] SubTask 5.2: 添加 `using Google.Protobuf;` 引用
  - [x] SubTask 5.3: 修复测试逻辑

- [x] Task 6: 修复 MetricsIntegrationTests.cs — 适配 Protobuf 消息类
  - [x] SubTask 6.1: 将 `C2S_HeartbeatMessage` 替换为 `C2S_Heartbeat`
  - [x] SubTask 6.2: 修复测试逻辑

- [x] Task 7: 修复 RateLimiterTests.cs — 适配 Protobuf 消息类
  - [x] SubTask 7.1: 将 `C2S_HeartbeatMessage` 替换为 `C2S_Heartbeat`
  - [x] SubTask 7.2: 修复测试逻辑

- [x] Task 8: 移除测试项目中的排除配置
  - [x] SubTask 8.1: 从 `MMORPG.Framework.Tests.csproj` 中移除 `<Compile Remove>` 配置

## Phase 2: 文档更新

- [x] Task 9: 更新 docs/06-API参考.md — 反映 Protobuf 消息类名
  - [x] SubTask 9.1: 检查并修正所有消息类名（不带 "Message" 后缀）
  - [x] SubTask 9.2: 更新 MessageSerializer API 说明

- [x] Task 10: 更新 docs/09-变更日志.md — 记录完整的迁移过程
  - [x] SubTask 10.1: 补充测试修复记录
  - [x] SubTask 10.2: 更新版本号

## Phase 3: 编译验证

- [x] Task 11: 编译验证
  - [x] SubTask 11.1: 运行 `dotnet build MMORPG.sln` 确保零错误
  - [x] SubTask 11.2: 运行 `dotnet test MMORPG.sln` 确保所有测试通过

# Task Dependencies

- Task 8 依赖于 Task 1-7 完成
- Task 11 依赖于 Task 8-10 完成
- Task 1-7 可并行执行
- Task 9-10 可并行执行