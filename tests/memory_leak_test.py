#!/usr/bin/env python3
"""
MMORPG 内存泄漏测试

测试内容：
1. 持续发送心跳
2. 同时监控容器内存使用
3. 分析内存变化趋势
"""

import socket
import struct
import time
import subprocess
import threading
import re
from datetime import datetime

MSG_C2S_HEARTBEAT = 0x00000003
MSG_S2C_HEARTBEAT = 0x00000004

def encode_heartbeat(client_timestamp: int) -> bytes:
    msg_id = MSG_C2S_HEARTBEAT
    body = struct.pack('<q', client_timestamp)
    length = len(body)
    header = struct.pack('<iI', length, msg_id)
    return header + body

def decode_heartbeat(data: bytes) -> tuple:
    if len(data) < 24:
        return None, None
    length, msg_id = struct.unpack('<iI', data[:8])
    if msg_id != MSG_S2C_HEARTBEAT:
        return None, None
    server_ts, client_ts = struct.unpack('<qq', data[8:24])
    return server_ts, client_ts

def parse_memory(mem_str: str) -> float:
    """解析内存字符串，返回 MiB 单位"""
    try:
        mem_str = mem_str.strip()
        if 'GiB' in mem_str:
            return float(mem_str.replace('GiB', '').strip()) * 1024
        elif 'MiB' in mem_str:
            return float(mem_str.replace('MiB', '').strip())
        elif 'KB' in mem_str or 'KiB' in mem_str:
            return float(re.sub(r'[^\d.]', '', mem_str)) / 1024
        elif 'B' in mem_str:
            return float(re.sub(r'[^\d.]', '', mem_str)) / (1024 * 1024)
        else:
            return 0
    except:
        return 0

def get_container_stats():
    """获取容器统计信息"""
    try:
        result = subprocess.run(
            ['docker', 'stats', 'mmorpg-server', '--no-stream', '--format', 
             '{{.CPUPerc}}|{{.MemUsage}}|{{.MemPerc}}|{{.PIDs}}'],
            capture_output=True, text=True, timeout=10
        )
        if result.returncode == 0:
            parts = result.stdout.strip().split('|')
            mem_str = parts[1].strip() if len(parts) > 1 else '0MiB/0MiB'
            mem_limit_str = parts[1].split('/')[1].strip() if '/' in parts[1] else '0MiB'
            
            return {
                'cpu': parts[0].strip() if len(parts) > 0 else '0%',
                'memory_used': parse_memory(parts[1].split('/')[0] if '/' in parts[1] else parts[1]),
                'memory_limit': parse_memory(mem_limit_str),
                'memory_percent': parts[2].strip() if len(parts) > 2 else '0%',
                'pids': parts[3].strip() if len(parts) > 3 else '0',
            }
    except Exception as e:
        pass
    return None

class MemoryMonitor:
    def __init__(self, interval: float = 5.0):
        self.interval = interval
        self.running = False
        self.memory_samples = []
        self.thread = None
        
    def start(self):
        self.running = True
        self.thread = threading.Thread(target=self._monitor_loop)
        self.thread.daemon = True
        self.thread.start()
        print("内存监控已启动")
        
    def stop(self):
        self.running = False
        if self.thread:
            self.thread.join(timeout=2)
        print("内存监控已停止")
        
    def _monitor_loop(self):
        while self.running:
            stats = get_container_stats()
            if stats:
                sample = {
                    'timestamp': time.time(),
                    'memory_mib': stats['memory_used'],
                    'cpu': stats['cpu'],
                    'pids': stats['pids']
                }
                self.memory_samples.append(sample)
            time.sleep(self.interval)
    
    def get_report(self):
        """生成内存分析报告"""
        if not self.memory_samples:
            return "没有收集到内存数据"
        
        initial = self.memory_samples[0]['memory_mib']
        final = self.memory_samples[-1]['memory_mib']
        max_mem = max(s['memory_mib'] for s in self.memory_samples)
        min_mem = min(s['memory_mib'] for s in self.memory_samples)
        
        # 计算增长趋势
        if len(self.memory_samples) >= 10:
            first_half = sum(s['memory_mib'] for s in self.memory_samples[:len(self.memory_samples)//2]) / (len(self.memory_samples)//2)
            second_half = sum(s['memory_mib'] for s in self.memory_samples[len(self.memory_samples)//2:]) / (len(self.memory_samples) - len(self.memory_samples)//2)
            trend = second_half - first_half
            trend_str = f"{trend:+.2f} MiB"
        else:
            trend_str = "数据不足"
        
        report = f"""
内存分析报告
{'='*50}
样本数量: {len(self.memory_samples)}
监控时长: {self.memory_samples[-1]['timestamp'] - self.memory_samples[0]['timestamp']:.0f} 秒

内存使用 (MiB):
  初始: {initial:.2f}
  最终: {final:.2f}
  最小: {min_mem:.2f}
  最大: {max_mem:.2f}
  变化: {final - initial:+.2f} MiB ({((final-initial)/initial*100):+.2f}%)
  
增长趋势: {trend_str}

判断结果:
"""
        if final > initial * 1.5:
            report += "  ⚠️ 可能存在内存泄漏 (增长 > 50%)"
        elif final > initial * 1.2:
            report += "  ⚠️ 轻微内存增长 (增长 > 20%)，建议观察"
        else:
            report += "  ✅ 内存使用稳定"
            
        return report

def main():
    host = 'localhost'
    port = 7001
    duration_seconds = 600  # 10分钟测试
    
    print("="*60)
    print("MMORPG 内存泄漏测试")
    print("="*60)
    print(f"目标服务器: {host}:{port}")
    print(f"测试时长: {duration_seconds}秒 (10分钟)")
    print()
    
    # 启动内存监控
    monitor = MemoryMonitor(interval=5.0)
    monitor.start()
    
    # 统计变量
    total_sent = 0
    total_success = 0
    total_fail = 0
    
    start_time = time.time()
    
    try:
        print("正在连接服务器...")
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10.0)
        sock.connect((host, port))
        print(f"连接建立成功，开始测试")
        print()
        
        # 表头
        print(f"{'时间':>8} | {'已发送':>8} | {'成功':>8} | {'失败':>8} | {'成功率':>10} | {'内存(MiB)':>12}")
        print("-" * 75)
        
        last_report_time = start_time
        report_interval = 30  # 每30秒报告一次
        
        while True:
            elapsed = time.time() - start_time
            
            if elapsed >= duration_seconds:
                break
            
            # 发送心跳
            total_sent += 1
            client_ts = int(time.time() * 1000)
            msg = encode_heartbeat(client_ts)
            
            try:
                sock.sendall(msg)
                response = sock.recv(1024)
                
                if response:
                    server_ts, recv_client_ts = decode_heartbeat(response)
                    if server_ts and recv_client_ts == client_ts:
                        total_success += 1
                    else:
                        total_fail += 1
                else:
                    total_fail += 1
            except:
                total_fail += 1
            
            # 定期报告
            if time.time() - last_report_time >= report_interval:
                success_rate = (total_success / total_sent * 100) if total_sent > 0 else 0
                current_mem = monitor.memory_samples[-1]['memory_mib'] if monitor.memory_samples else 0
                
                elapsed_min = int(elapsed // 60)
                elapsed_sec = int(elapsed % 60)
                time_str = f"{elapsed_min:02d}:{elapsed_sec:02d}"
                
                print(f"{time_str:>8} | {total_sent:>8} | {total_success:>8} | {total_fail:>8} | "
                      f"{success_rate:>9.2f}% | {current_mem:>11.2f}")
                
                last_report_time = time.time()
            
            time.sleep(1)
        
        sock.close()
        
    except KeyboardInterrupt:
        print("\n测试被用户中断")
    except Exception as e:
        print(f"\n测试异常: {e}")
    finally:
        monitor.stop()
    
    # 最终报告
    print("-" * 75)
    print()
    print("="*60)
    print("测试结果总结")
    print("="*60)
    print(f"总发送数: {total_sent}")
    print(f"总成功数: {total_success}")
    print(f"总失败数: {total_fail}")
    print(f"成功率: {(total_success/total_sent*100):.2f}%" if total_sent > 0 else "N/A")
    
    print()
    print(monitor.get_report())

if __name__ == '__main__':
    main()