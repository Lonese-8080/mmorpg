#!/usr/bin/env python3
"""
MMORPG 框架压力测试客户端

测试内容：
1. TCP 连接建立速度
2. 心跳消息往返延迟
3. 并发连接稳定性
"""

import socket
import struct
import time
import threading
import argparse
from concurrent.futures import ThreadPoolExecutor

# 消息 ID 定义（与框架一致）
MSG_C2S_HEARTBEAT = 0x00000003
MSG_S2C_HEARTBEAT = 0x00000004

def encode_heartbeat(client_timestamp: int) -> bytes:
    """编码心跳请求消息（小端序）"""
    # 消息格式：Length(4) + MessageId(4) + Body(8)
    # Length = 消息体长度（不含消息头）
    msg_id = MSG_C2S_HEARTBEAT
    # 消息体：ClientTimestamp(8)
    body = struct.pack('<q', client_timestamp)  # 小端序，8字节 long
    length = len(body)
    # 消息头：Length(4) + MessageId(4)，小端序
    header = struct.pack('<iI', length, msg_id)
    return header + body

def decode_heartbeat(data: bytes) -> tuple:
    """解码心跳响应消息（小端序）"""
    # 消息格式：Length(4) + MessageId(4) + ServerTimestamp(8) + ClientTimestamp(8)
    if len(data) < 24:  # 8字节头 + 16字节体
        return None, None
    # 解析消息头
    length, msg_id = struct.unpack('<iI', data[:8])
    if msg_id != MSG_S2C_HEARTBEAT:
        return None, None
    # 解析消息体：ServerTimestamp(8) + ClientTimestamp(8)
    server_ts, client_ts = struct.unpack('<qq', data[8:24])
    return server_ts, client_ts

def test_single_connection(host: str, port: int, timeout: float = 10.0) -> dict:
    """测试单个连接"""
    result = {
        'success': False,
        'connect_time': 0,
        'latency': 0,
        'error': None
    }
    
    try:
        start = time.time()
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(timeout)
        sock.connect((host, port))
        result['connect_time'] = time.time() - start
        
        # 发送心跳
        client_ts = int(time.time() * 1000)
        msg = encode_heartbeat(client_ts)
        sock.sendall(msg)
        
        # 接收响应
        response = sock.recv(1024)
        server_ts, recv_client_ts = decode_heartbeat(response)
        
        if server_ts and recv_client_ts == client_ts:
            result['latency'] = server_ts - client_ts
            result['success'] = True
        
        sock.close()
        
    except Exception as e:
        result['error'] = str(e)
        print(f"  连接失败: {e}")
    
    return result

def run_stress_test(host: str, port: int, connections: int, rounds: int) -> dict:
    """运行压力测试"""
    print(f"开始压力测试: {connections} 并发连接, {rounds} 轮")
    
    all_results = []
    
    for round_num in range(1, rounds + 1):
        print(f"\n第 {round_num}/{rounds} 轮...")
        round_start = time.time()
        
        with ThreadPoolExecutor(max_workers=connections) as executor:
            futures = [
                executor.submit(test_single_connection, host, port)
                for _ in range(connections)
            ]
            results = [f.result() for f in futures]
        
        round_time = time.time() - round_start
        successes = sum(1 for r in results if r['success'])
        failures = connections - successes
        
        avg_connect_time = sum(r['connect_time'] for r in results if r['success']) / successes if successes > 0 else 0
        avg_latency = sum(r['latency'] for r in results if r['success']) / successes if successes > 0 else 0
        
        print(f"  成功: {successes}, 失败: {failures}")
        print(f"  平均连接时间: {avg_connect_time*1000:.2f}ms")
        print(f"  平均延迟: {avg_latency:.2f}ms")
        print(f"  轮耗时: {round_time:.2f}s")
        
        all_results.extend(results)
    
    # 总结
    total_success = sum(1 for r in all_results if r['success'])
    total_failure = len(all_results) - total_success
    success_rate = total_success / len(all_results) * 100
    
    print(f"\n{'='*50}")
    print(f"压力测试总结:")
    print(f"  总连接数: {len(all_results)}")
    print(f"  成功: {total_success} ({success_rate:.1f}%)")
    print(f"  失败: {total_failure}")
    
    if total_success > 0:
        avg_connect = sum(r['connect_time'] for r in all_results if r['success']) / total_success
        avg_latency = sum(r['latency'] for r in all_results if r['success']) / total_success
        print(f"  平均连接时间: {avg_connect*1000:.2f}ms")
        print(f"  平均心跳延迟: {avg_latency:.2f}ms")
    
    return {
        'total': len(all_results),
        'success': total_success,
        'failure': total_failure,
        'success_rate': success_rate
    }

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='MMORPG 框架压力测试')
    parser.add_argument('--host', default='localhost', help='服务器地址')
    parser.add_argument('--port', type=int, default=7001, help='服务器端口')
    parser.add_argument('--connections', type=int, default=100, help='并发连接数')
    parser.add_argument('--rounds', type=int, default=3, help='测试轮数')
    
    args = parser.parse_args()
    
    run_stress_test(args.host, args.port, args.connections, args.rounds)