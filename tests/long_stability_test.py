#!/usr/bin/env python3
"""
MMORPG 长时间稳定性测试（修复版）

测试内容：单连接持续发送心跳，观察成功率、延迟、服务器状态
"""

import socket
import struct
import time
import sys

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

def main():
    host = 'localhost'
    port = 7001
    duration_seconds = 1800  # 30分钟
    
    print("="*60)
    print("MMORPG 长时间稳定性测试（修复版）")
    print("="*60)
    print(f"目标服务器: {host}:{port}")
    print(f"测试时长: {duration_seconds}秒 (30分钟)")
    print()
    
    # 统计变量
    total_sent = 0      # 总发送数
    total_success = 0   # 总成功数
    total_fail = 0      # 总失败数
    latencies = []       # 所有延迟记录
    
    start_time = time.time()
    last_report_time = start_time
    
    # 报告间隔（每60秒报告一次）
    report_interval = 60
    
    try:
        print("正在连接服务器...")
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10.0)
        sock.connect((host, port))
        print(f"连接建立成功，开始测试")
        print()
        
        # 表头
        print(f"{'时间':>8} | {'已发送':>8} | {'成功':>8} | {'失败':>8} | {'成功率':>10} | {'平均延迟':>10} | {'最大延迟':>10}")
        print("-" * 80)
        
        while True:
            elapsed = time.time() - start_time
            
            # 检查是否超时
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
                        latency = server_ts - client_ts
                        latencies.append(latency)
                    else:
                        total_fail += 1
                else:
                    total_fail += 1
            except socket.timeout:
                total_fail += 1
            except Exception as e:
                total_fail += 1
            
            # 定期报告
            if time.time() - last_report_time >= report_interval:
                success_rate = (total_success / total_sent * 100) if total_sent > 0 else 0
                avg_latency = (sum(latencies) / len(latencies)) if latencies else 0
                max_latency = max(latencies) if latencies else 0
                
                elapsed_min = int(elapsed // 60)
                elapsed_sec = int(elapsed % 60)
                time_str = f"{elapsed_min:02d}:{elapsed_sec:02d}"
                
                print(f"{time_str:>8} | {total_sent:>8} | {total_success:>8} | {total_fail:>8} | "
                      f"{success_rate:>9.2f}% | {avg_latency:>9.2f}ms | {max_latency:>9.2f}ms")
                
                last_report_time = time.time()
            
            # 每秒发送一次
            time.sleep(1)
        
        sock.close()
        
    except KeyboardInterrupt:
        print("\n测试被用户中断")
    except Exception as e:
        print(f"\n测试异常: {e}")
    
    # 最终报告
    print("-" * 80)
    print()
    print("="*60)
    print("测试结果总结")
    print("="*60)
    print(f"总发送数: {total_sent}")
    print(f"总成功数: {total_success}")
    print(f"总失败数: {total_fail}")
    print(f"成功率: {(total_success/total_sent*100):.2f}%" if total_sent > 0 else "N/A")
    if latencies:
        print(f"平均延迟: {sum(latencies)/len(latencies):.2f}ms")
        print(f"最大延迟: {max(latencies)}ms")
        print(f"最小延迟: {min(latencies)}ms")
        print(f"延迟>10ms次数: {sum(1 for l in latencies if l > 10)}")
        print(f"延迟>50ms次数: {sum(1 for l in latencies if l > 50)}")

if __name__ == '__main__':
    main()