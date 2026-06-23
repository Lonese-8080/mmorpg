#!/bin/bash
set -e

echo "========== MMORPG Framework VM Setup =========="

# 更新系统
echo "[1/8] 更新系统..."
sudo apt update && sudo apt upgrade -y

# 安装基础工具
echo "[2/8] 安装基础工具..."
sudo apt install -y curl wget git unzip vim htop net-tools \
    ca-certificates gnupg lsb-release software-properties-common

# 安装 Docker
echo "[3/8] 安装 Docker..."
if ! command -v docker &> /dev/null; then
    curl -fsSL https://get.docker.com | sh
    sudo usermod -aG docker vagrant
fi

# 安装 Docker Compose
echo "[4/8] 安装 Docker Compose..."
sudo curl -L "https://github.com/docker/compose/releases/download/v2.24.0/docker-compose-$(uname -s)-$(uname -m)" \
    -o /usr/local/bin/docker-compose
sudo chmod +x /usr/local/bin/docker-compose
docker-compose --version

# 安装 .NET 10 SDK
echo "[5/8] 安装 .NET 10 SDK..."
if ! command -v dotnet &> /dev/null; then
    wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
    chmod +x dotnet-install.sh
    sudo ./dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
    sudo ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
fi
dotnet --version

# 安装压力测试工具
echo "[6/8] 安装压测工具..."
sudo apt install -y apache2-utils
if ! command -v bombardier &> /dev/null; then
    wget https://github.com/rakyll/bombardier/releases/download/v1.11.0/bombardier_linux_amd64 -O /tmp/bombardier
    sudo mv /tmp/bombardier /usr/local/bin/bombardier
    sudo chmod +x /usr/local/bin/bombardier
fi

# 安装 Python
echo "[7/8] 安装 Python..."
sudo apt install -y python3 python3-pip python3-venv

# 防火墙配置（开发环境关闭，生产环境请按需配置）
echo "[8/8] 配置防火墙（开发模式）..."
sudo ufw disable || true

echo ""
echo "========== Setup Complete! =========="
echo "请运行 'vagrant reload' 以加载 Docker 组权限"
echo "然后运行 'cd /vagrant && docker-compose up -d' 启动所有服务"
