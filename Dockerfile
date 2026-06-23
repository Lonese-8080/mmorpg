# Dockerfile - MMORPG Framework 框架测试工具
# 多阶段构建：构建阶段 + 运行时阶段

# 用途：纯框架测试，无游戏逻辑
# 测试覆盖：网络层、指标、熔断器、限流器、雪花ID、调度器、消息通道、序列化、配置热更新
#
# 使用（Ubuntu 上构建并运行）：
#   docker build -t mmorpg/test-harness:latest .
#   docker run --rm -p 8080:8080 -p 9091:9091 -e AUTO_RUN_TESTS=1 mmorpg/test-harness:latest
#
# 或使用 docker-compose:
#   docker compose up -d
#   docker compose logs -f

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 复制所有项目文件
COPY src/MMORPG.Framework/MMORPG.Framework.csproj ./MMORPG.Framework/
COPY tests/MMORPG.Framework.TestHarness/MMORPG.Framework.TestHarness.csproj ./MMORPG.Framework.TestHarness/

# 还原依赖（利用 Docker 缓存）
RUN dotnet restore MMORPG.Framework.TestHarness/MMORPG.Framework.TestHarness.csproj

# 复制源代码
COPY src/MMORPG.Framework/ ./MMORPG.Framework/
COPY tests/MMORPG.Framework.TestHarness/ ./MMORPG.Framework.TestHarness/

# 构建（Release）
RUN dotnet build MMORPG.Framework.TestHarness/MMORPG.Framework.TestHarness.csproj -c Release -o /app/build

# ============ 运行时阶段 ============
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# 安装 curl（用于健康检查）
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# 创建非 root 用户（安全最佳实践）
RUN groupadd -r appgroup -g 10000 && \
    useradd -r -g appgroup -u 10000 appuser && \
    mkdir -p /app/logs && \
    chown -R appuser:appgroup /app

# 复制构建产物
COPY --from=build /app/build ./

# 设置权限
RUN chown -R appuser:appgroup /app

# 切换到非 root 用户
USER appuser

# 暴露端口
EXPOSE 7001 8080 9091

# 健康检查（框架自身）
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# 启动命令
# - 不传参数：等待 HTTP 命令触发测试
# - AUTO_RUN_TESTS=1：启动时自动跑测试
ENTRYPOINT ["dotnet", "MMORPG.Framework.TestHarness.dll"]
