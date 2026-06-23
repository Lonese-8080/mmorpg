# 此目录由 protoc 自动生成

运行以下命令生成 C# 代码：

```bash
protoc --csharp_out=. ../common.proto
protoc --csharp_out=. ../game.proto
```

或在项目构建时自动生成（需要配置 Grpc.Tools NuGet 包）。