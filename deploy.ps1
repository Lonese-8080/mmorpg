# MMORPG Framework 一键部署脚本 (Windows)
# Copyright (c) 2024-2026 MMORPG Framework Contributors
# SPDX-License-Identifier: MIT

<#
.SYNOPSIS
MMORPG Framework 一键部署工具 - Windows 版本

.DESCRIPTION
这个脚本可以帮助你：
1. 编译项目
2. 运行测试
3. 构建 Docker 镜像
4. 上传文件到服务器
5. 服务器部署

使用方法：
  .\deploy.ps1 -Action build
  .\deploy.ps1 -Action test
  .\deploy.ps1 -Action docker
  .\deploy.ps1 -Action upload -Server user@server -Path /home/user/mmorpg
  .\deploy.ps1 -Action full -Server user@server -Path /home/user/mmorpg
#>

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("build", "test", "docker", "upload", "full", "clean")]
    [string]$Action,
    
    [string]$Server = "",
    [string]$Path = "~/mmorpg",
    [string]$SshKey = ""
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot

Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   MMORPG Framework 部署工具 v1.0            ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

function Invoke-Build {
    Write-Host "📦 正在编译项目..." -ForegroundColor Yellow
    dotnet build MMORPG.sln -c Release
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ 编译成功！" -ForegroundColor Green
    } else {
        Write-Host "❌ 编译失败！" -ForegroundColor Red
        exit 1
    }
}

function Invoke-Test {
    Write-Host "🧪 正在运行测试..." -ForegroundColor Yellow
    dotnet test tests/MMORPG.Framework.Tests/MMORPG.Framework.Tests.csproj -c Release
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ 所有测试通过！" -ForegroundColor Green
    } else {
        Write-Host "❌ 测试失败！" -ForegroundColor Red
        exit 1
    }
}

function Invoke-DockerBuild {
    Write-Host "🐳 正在构建 Docker 镜像..." -ForegroundColor Yellow
    docker compose build --no-cache framework-test
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Docker 镜像构建成功！" -ForegroundColor Green
    } else {
        Write-Host "❌ Docker 镜像构建失败！" -ForegroundColor Red
        exit 1
    }
}

function Invoke-Upload {
    if ([string]::IsNullOrEmpty($Server)) {
        Write-Host "❌ 请指定服务器地址，例如：-Server user@192.168.1.100" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "📤 正在上传文件到 $Server..." -ForegroundColor Yellow
    
    # 需要上传的文件列表
    $files = @(
        "tests/MMORPG.Framework.TestHarness/Program.cs",
        "src/MMORPG.Framework/",
        "tests/MMORPG.Framework.Tests/",
        "benchmarks/",
        "Dockerfile",
        "docker-compose.yml",
        ".dockerignore",
        "deploy/",
        "docs/"
    )
    
    Write-Host "  上传文件列表："
    foreach ($file in $files) {
        Write-Host "    - $file"
    }
    
    Write-Host ""
    Write-Host "⚠️  注意：完整上传可能需要较长时间" -ForegroundColor Yellow
    Write-Host "  按 Ctrl+C 取消，按任意键继续..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    
    # 使用 scp 上传（简化版，实际项目建议用 rsync）
    $uploadCmd = "scp -r $files $Server`:$Path/"
    Write-Host "  执行: $uploadCmd"
    Invoke-Expression $uploadCmd
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ 文件上传成功！" -ForegroundColor Green
    } else {
        Write-Host "❌ 文件上传失败！" -ForegroundColor Red
        exit 1
    }
}

function Invoke-Clean {
    Write-Host "🧹 正在清理..." -ForegroundColor Yellow
    
    # 清理 bin 和 obj 目录
    Get-ChildItem -Path . -Recurse -Directory -Name bin | ForEach-Object {
        Write-Host "  删除: $_"
        Remove-Item -Recurse -Force $_ -ErrorAction SilentlyContinue
    }
    
    Get-ChildItem -Path . -Recurse -Directory -Name obj | ForEach-Object {
        Write-Host "  删除: $_"
        Remove-Item -Recurse -Force $_ -ErrorAction SilentlyContinue
    }
    
    # 清理测试结果
    Remove-Item -Recurse -Force TestResults -ErrorAction SilentlyContinue
    
    Write-Host "✅ 清理完成！" -ForegroundColor Green
}

# 主程序
switch ($Action) {
    "build" { Invoke-Build }
    "test" { Invoke-Test }
    "docker" { Invoke-DockerBuild }
    "upload" { Invoke-Upload }
    "clean" { Invoke-Clean }
    "full" {
        Write-Host "🚀 开始完整部署流程..." -ForegroundColor Cyan
        Write-Host ""
        
        Invoke-Build
        Write-Host ""
        
        Invoke-Test
        Write-Host ""
        
        Invoke-DockerBuild
        Write-Host ""
        
        Invoke-Upload
        Write-Host ""
        
        Write-Host "🎉 完整部署完成！" -ForegroundColor Green
        Write-Host "  请登录服务器执行部署命令。" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "完成！" -ForegroundColor Green
