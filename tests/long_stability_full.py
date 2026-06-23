#!/usr/bin/env python3
"""
MMORPG 长时间稳定性测试（完整版 - 支持粘包处理）
"""

import socket
import struct
import time
import sys

MSG_C2S_HEARTBEAT = 0x00000003
MSG_S2C_HEARTBEAT = 0x00000004
HEADER_SIZE = 8

def encode_heartbeat(client_timestamp: int) -> bytes:
    body = struct.pack('<q', client_timestamp)
    header = struct.pack('<iI', len(body), MSG_C2S_HEARTBEAT)
    return header + body

def decode_messages(data: bytes) -> tuple:
    """解析所有粘包消息，返回(消息列表, 剩余缓冲)"""
    messages = []
    offset = 0
    
    while offset + HEADER_SIZE <= len(data):
        length, msg_id = struct.unpack('<iI', data[offset:offset + HEADER_SIZE])
        
        if offset + HEADER_SIZE + length > len(data):
            break
        
        body_start = offset + HEADER_SIZE
        body_end = body_start + length
        body = data[body_start:body_end]
        
        messages.append({'msg_id': msg_id, 'length': length, 'body': body})
        offset = body_end
    
    return messages, data[offset:]

def main():
    host = 'localhost'
    port = 7001
    duration_seconds = 1800  # 30分钟
    
    print("="*60)
    print("MMORPG 长时间稳定性测试（完整版）")
    print("="*60)
    print(f"目标服务器: {host}:{port}")
    print(f"测试时长: {duration_seconds}秒 (30分钟)")
    print()
    
    total_sent = 0
    total_success = 0
    total_fail = 0
    latencies = []
    
    recv_buffer = b''
    start_time = time.time()
    last_report_time = start_time
    report_interval = 60  # 每分钟报告
    
    try:
        print("正在连接服务器...")
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10.0)
        sock.connect((host, port))
        print("连接建立成功，开始测试")
        print()
        
        print(f"{'时间':>8} | {'已发送':>8} | {'成功':>8} | {'失败':>8} | {'成功率':>10} | {'平均延迟':>10} | {'最大延迟':>10}")
        print("-" * 85)
        
        while True:
            elapsed = time.time() - start_time
            
            if elapsed >= duration_seconds:
                break
            
            total_sent += 1
            client_ts = int(time.time() * 1000)
            msg = encode_heartbeat(client_ts)
            
            try:
                sock.sendall(msg)
                
                # 接收数据
                chunk = sock.recv(4096)
                recv_buffer += chunk
                
                # 解析消息
                messages, recv_buffer = decode_messages(recv_buffer)
                
                found = False
                for m in messages:
                    if m['msg_id'] == MSG_S2C_HEARTBEAT and len(m['body']) >= 16:
                        server_ts, recv_client_ts = struct.unpack('<qq', m['body'][:16])
                        if recv_client_ts == client_ts:
                            total_success += 1
                            latencies.append(server_ts - client_ts)
                            found = True
                            break
                
                if not found:
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
            
            time.sleep(1)
        
        sock.close()
        
    except KeyboardInterrupt:
        print("\n测试被用户中断")
    except Exception as e:
        print(f"\n测试异常: {e}")
    
    print("-" * 85)
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

if __name__ == '__main__':
    main()