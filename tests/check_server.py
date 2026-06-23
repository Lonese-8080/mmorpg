#!/usr/bin/env python3
"""检查服务器状态"""

import socket
import subprocess

def check_port(host, port):
    """检查端口是否开放"""
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(2)
        result = sock.connect_ex((host, port))
        sock.close()
        return result == 0
    except Exception as e:
        return False

def main():
    print("检查服务器状态...")
    
    # 检查端口
    port_open = check_port('localhost', 7001)
    print(f"TCP 端口 7001: {'开放' if port_open else '关闭'}")
    
    # 检查健康检查端口
    health_open = check_port('localhost', 8080)
    print(f"HTTP 端口 8080: {'开放' if health_open else '关闭'}")
    
    if health_open:
        try:
            import urllib.request
            response = urllib.request.urlopen('http://localhost:8080/health', timeout=2)
            data = response.read().decode()
            print(f"健康检查响应: {data}")
        except Exception as e:
            print(f"健康检查失败: {e}")

if __name__ == '__main__':
    main()