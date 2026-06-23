#!/bin/bash
# MMORPG Framework 服务器部署脚本
# Copyright (c) 2024-2026 MMORPG Framework Contributors
# SPDX-License-Identifier: MIT

set -e

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# 项目根目录
PROJECT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$PROJECT_DIR"

# 显示菜单
show_menu() {
    echo -e "${CYAN}╔══════════════════════════════════════════════╗${NC}"
    echo -e "${CYAN}║   MMORPG Framework 服务器部署工具 v1.0       ║${NC}"
    echo -e "${CYAN}╚══════════════════════════════════════════════╝${NC}"
    echo ""
    echo "请选择操作："
    echo ""
    echo "  1) 🐳 启动服务 (docker compose up)"
    echo "  2) ⏹  停止服务 (docker compose down)"
    echo "  3) 🔄 重新构建并启动"
    echo "  4) 📋 查看服务状态"
    echo "  5) 📜 查看日志"
    echo "  6) 🧪 运行测试"
    echo "  7) 📊 运行性能基准测试"
    echo "  8) ☸  Kubernetes 部署 (Helm)"
    echo "  9) 🧹 清理旧镜像"
    echo "  0) ❌ 退出"
    echo ""
    echo -n "请输入选项 [0-9]: "
}

# 启动服务
start_service() {
    echo -e "${YELLOW}🚀 正在启动服务...${NC}"
    docker compose up -d
    echo ""
    echo -e "${GREEN}✅ 服务已启动！${NC}"
    echo ""
    echo "访问地址："
    echo "  - 健康检查: http://localhost:8080/health"
    echo "  - 测试结果: http://localhost:8080/test/results"
    echo "  - Prometheus: http://localhost:9090"
    echo "  - Grafana: http://localhost:3000 (admin/admin)"
}

# 停止服务
stop_service() {
    echo -e "${YELLOW}⏹  正在停止服务...${NC}"
    docker compose down
    echo -e "${GREEN}✅ 服务已停止！${NC}"
}

# 重新构建并启动
rebuild_service() {
    echo -e "${YELLOW}🔄 正在重新构建并启动...${NC}"
    echo ""
    
    echo "  1/4 停止服务..."
    docker compose down
    
    echo "  2/4 清理旧镜像..."
    docker rmi mmorpg/framework-test:latest 2>/dev/null || true
    
    echo "  3/4 构建新镜像（不使用缓存）..."
    docker compose build --no-cache framework-test
    
    echo "  4/4 启动服务..."
    docker compose up -d
    
    echo ""
    echo -e "${GREEN}✅ 重新构建完成！${NC}"
    echo ""
    echo "等待服务启动..."
    sleep 15
    
    echo ""
    echo "测试结果："
    curl -s http://127.0.0.1:8080/test/results 2>/dev/null | python3 -m json.tool 2>/dev/null || echo "  服务还在启动中，请稍后再试"
}

# 查看服务状态
show_status() {
    echo -e "${YELLOW}📋 服务状态：${NC}"
    echo ""
    docker compose ps
    echo ""
    echo "端口占用："
    ss -tlnp | grep -E '7001|8080|9090|9091|3000' 2>/dev/null || netstat -tlnp 2>/dev/null | grep -E '7001|8080|9090|9091|3000' || echo "  无法检测端口"
}

# 查看日志
show_logs() {
    echo -e "${YELLOW}📜 查看日志（按 Ctrl+C 退出）${NC}"
    echo ""
    echo "选择查看哪个服务的日志："
    echo "  1) framework-test (主服务)"
    echo "  2) prometheus"
    echo "  3) grafana"
    echo "  4) 全部服务"
    echo ""
    echo -n "请选择 [1-4]: "
    read -r choice
    
    case $choice in
        1) docker compose logs -f framework-test ;;
        2) docker compose logs -f prometheus ;;
        3) docker compose logs -f grafana ;;
        4) docker compose logs -f ;;
        *) echo -e "${RED}无效选项${NC}" ;;
    esac
}

# 运行测试
run_tests() {
    echo -e "${YELLOW}🧪 正在运行测试...${NC}"
    echo ""
    
    if [ -f "MMORPG.sln" ]; then
        dotnet test tests/MMORPG.Framework.Tests/MMORPG.Framework.Tests.csproj -c Release
    else
        echo "  本地编译环境不可用，通过 HTTP 触发测试..."
        curl -s http://127.0.0.1:8080/test/results | python3 -m json.tool 2>/dev/null || echo "  无法连接到服务"
    fi
    
    echo ""
    echo -e "${GREEN}✅ 测试完成！${NC}"
}

# 运行性能基准测试
run_benchmarks() {
    echo -e "${YELLOW}📊 正在运行性能基准测试...${NC}"
    echo ""
    echo "  注意：性能测试可能需要几分钟到十几分钟"
    echo ""
    
    if [ -f "MMORPG.sln" ]; then
        dotnet run --project benchmarks/MMORPG.Framework.Benchmarks -c Release
    else
        echo "  本地编译环境不可用"
        echo "  请在开发机上运行：dotnet run --project benchmarks/MMORPG.Framework.Benchmarks -c Release"
    fi
}

# Kubernetes 部署
deploy_kubernetes() {
    echo -e "${YELLOW}☸  Kubernetes 部署 (Helm)${NC}"
    echo ""
    
    if ! command -v helm &> /dev/null; then
        echo -e "${RED}错误：未安装 Helm${NC}"
        echo "  安装命令：curl https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash"
        return
    fi
    
    if ! command -v kubectl &> /dev/null; then
        echo -e "${RED}错误：未安装 kubectl${NC}"
        return
    fi
    
    echo "  1. 快速部署（默认配置）"
    echo "  2. 自定义部署"
    echo "  3. 查看部署状态"
    echo "  4. 更新部署"
    echo "  5. 卸载"
    echo "  6. 返回主菜单"
    echo ""
    echo -n "请选择 [1-6]: "
    read -r choice
    
    case $choice in
        1)
            echo ""
            echo "  正在部署..."
            kubectl create namespace mmorpg-framework 2>/dev/null || true
            helm install mmorpg-framework ./deploy/kubernetes \
                --namespace mmorpg-framework \
                --set image.repository=mmorpg/framework-test \
                --set image.tag=latest \
                --set image.pullPolicy=IfNotPresent \
                --set autoscaling.enabled=false \
                --set replicaCount=1
            echo ""
            echo -e "${GREEN}✅ 部署完成！${NC}"
            echo "  查看状态：kubectl get pods -n mmorpg-framework"
            ;;
        2)
            echo ""
            echo "  请在 deploy/kubernetes/values.yaml 中配置参数"
            echo "  然后执行：helm install mmorpg-framework ./deploy/kubernetes -n mmorpg-framework"
            ;;
        3)
            echo ""
            kubectl get pods -n mmorpg-framework 2>/dev/null || echo "  命名空间不存在"
            kubectl get svc -n mmorpg-framework 2>/dev/null
            helm list -n mmorpg-framework
            ;;
        4)
            echo ""
            helm upgrade mmorpg-framework ./deploy/kubernetes -n mmorpg-framework
            echo -e "${GREEN}✅ 更新完成！${NC}"
            ;;
        5)
            echo ""
            echo -e "${RED}⚠️  确认要卸载吗？(yes/no)${NC}"
            read -r confirm
            if [ "$confirm" = "yes" ]; then
                helm uninstall mmorpg-framework -n mmorpg-framework
                echo -e "${GREEN}✅ 已卸载${NC}"
            else
                echo "已取消"
            fi
            ;;
        6) return ;;
        *) echo -e "${RED}无效选项${NC}" ;;
    esac
}

# 清理旧镜像
clean_images() {
    echo -e "${YELLOW}🧹 正在清理旧镜像...${NC}"
    
    echo ""
    echo "  当前镜像："
    docker images | grep mmorpg
    
    echo ""
    echo -e "${RED}⚠️  确认要删除所有 mmorpg 镜像吗？(yes/no)${NC}"
    read -r confirm
    
    if [ "$confirm" = "yes" ]; then
        docker images | grep mmorpg | awk '{print $3}' | xargs -r docker rmi -f
        echo -e "${GREEN}✅ 清理完成！${NC}"
    else
        echo "已取消"
    fi
}

# 主循环
while true; do
    echo ""
    show_menu
    read -r choice
    
    case $choice in
        1) start_service ;;
        2) stop_service ;;
        3) rebuild_service ;;
        4) show_status ;;
        5) show_logs ;;
        6) run_tests ;;
        7) run_benchmarks ;;
        8) deploy_kubernetes ;;
        9) clean_images ;;
        0)
            echo ""
            echo -e "${GREEN}再见！👋${NC}"
            exit 0
            ;;
        *)
            echo -e "${RED}无效选项，请重新选择${NC}"
            sleep 1
            ;;
    esac
    
    echo ""
    echo -n "按回车键继续..."
    read -r
done
