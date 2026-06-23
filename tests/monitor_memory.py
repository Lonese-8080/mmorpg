#!/usr/bin/env python3
"""
内存监控脚本 - 持续监控容器资源使用
"""

import subprocess
import time
import json
from datetime import datetime

def get_container_stats():
    """获取容器统计信息"""
    try:
        result = subprocess.run(
            ['docker', 'stats', 'mmorpg-server', '--no-stream', '--format', 
             '{{.CPUPerc}}|{{.MemUsage}}|{{.NetIO}}|{{.BlockIO}}|{{.PIDs}}'],
            capture_output=True, text=True, timeout=10
        )
        if result.returncode == 0:
            parts = result.stdout.strip().split('|')
            return {
                'cpu': parts[0].strip() if len(parts) > 0 else 'N/A',
                'memory': parts[1].strip() if len(parts) > 1 else 'N/A',
                'net_io': parts[2].strip() if len(parts) > 2 else 'N/A',
                'block_io': parts[3].strip() if len(parts) > 3 else 'N/A',
                'pids': parts[4].strip() if len(parts) > 4 else 'N/A',
            }
    except Exception as e:
        pass
    return None

def main():
    print("="*60)
    print("MMORPG 服务器内存监控")
    print("="*60)
    print("按 Ctrl+C 停止监控")
    print()
    
    # 记录初始内存
    initial_memory = None
    max_memory = None
    
    try:
        while True:
            stats = get_container_stats()
            if stats:
                timestamp = datetime.now().strftime('%H:%M:%S')
                print(f"[{timestamp}] CPU: {stats['cpu']:>8} | "
                      f"内存: {stats['memory']:>20} | "
                      f"PID: {stats['pids']:>4}")
                
                # 解析内存使用
                try:
                    mem_str = stats['memory'].split('/')[0].strip()
                    if 'MiB' in mem_str:
                        mem_val = float(mem_str.replace('MiB', '').strip())
                    elif 'GiB' in mem_str:
                        mem_val = float(mem_str.replace('GiB', '').strip()) * 1024
                    else:
                        mem_val = 0
                    
                    if initial_memory is None:
                        initial_memory = mem_val
                    if max_memory is None or mem_val > max_memory:
                        max_memory = mem_val
                except:
                    pass
            else:
                print(f"[{datetime.now().strftime('%H:%M:%S')}] 无法获取容器状态")
            
            time.sleep(5)  # 每5秒检查一次
            
    except KeyboardInterrupt:
        print("\n\n监控停止")
        if initial_memory and max_memory:
            print(f"初始内存: {initial_memory:.1f} MiB")
            print(f"峰值内存: {max_memory:.1f} MiB")
            print(f"内存增长: {max_memory - initial_memory:.1f} MiB")

if __name__ == '__main__':
    main()