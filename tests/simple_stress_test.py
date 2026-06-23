#!/usr/bin/env python3
"""简化版压力测试 - 逐步增加并发数"""

import socket
import struct
import time
import threading
from concurrent.futures import ThreadPoolExecutor

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

def test_single_connection(host: str, port: int, timeout: float = 10.0) -> dict:
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
    
    return result

def run_test(host: str, port: int, connections: int) -> dict:
    print(f"\n测试 {connections} 并发连接...")
    start = time.time()
    
    with ThreadPoolExecutor(max_workers=connections) as executor:
        futures = [
            executor.submit(test_single_connection, host, port)
            for _ in range(connections)
        ]
        results = [f.result() for f in futures]
    
    elapsed = time.time() - start
    successes = sum(1 for r in results if r['success'])
    failures = connections - successes
    
    avg_connect = sum(r['connect_time'] for r in results if r['success']) / successes if successes > 0 else 0
    avg_latency = sum(r['latency'] for r in results if r['success']) / successes if successes > 0 else 0
    
    print(f"  成功: {successes}/{connections} ({successes/connections*100:.1f}%)")
    print(f"  失败: {failures}")
    print(f"  总耗时: {elapsed:.2f}s")
    print(f"  平均连接时间: {avg_connect*1000:.2f}ms")
    print(f"  平均延迟: {avg_latency:.2f}ms")
    
    return {
        'connections': connections,
        'success': successes,
        'failure': failures,
        'elapsed': elapsed
    }

def main():
    host = 'localhost'
    port = 7001
    
    print("="*50)
    print("MMORPG 框架渐进式压力测试")
    print("="*50)
    
    # 逐步增加并发数
    test_cases = [1, 5, 10, 20, 50, 100]
    
    all_results = []
    for conn_count in test_cases:
        result = run_test(host, port, conn_count)
        all_results.append(result)
        time.sleep(1)  # 间隔1秒，让服务器恢复
    
    print("\n" + "="*50)
    print("测试总结:")
    print("="*50)
    for r in all_results:
        print(f"  {r['connections']:3d} 连接: 成功 {r['success']:3d}/{r['connections']:3d} ({r['success']/r['connections']*100:5.1f}%) 耗时 {r['elapsed']:.2f}s")

if __name__ == '__main__':
    main()