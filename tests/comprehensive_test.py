#!/usr/bin/env python3
"""
MMORPG 框架全面验证测试

测试项目：
1. 长时间稳定性测试（30分钟持续心跳）
2. 异常场景测试（错误消息、恶意数据、突然断开）
3. 极限并发测试（500-1000连接）
4. 内存监控
5. 连接池复用稳定性
"""

import socket
import struct
import time
import threading
import random
import string
import sys
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

def send_raw(sock: socket.socket, data: bytes, timeout: float = 5.0) -> bool:
    """发送原始数据"""
    try:
        sock.settimeout(timeout)
        sock.sendall(data)
        return True
    except Exception as e:
        return False

def recv_raw(sock: socket.socket, size: int = 1024, timeout: float = 5.0) -> bytes:
    """接收原始数据"""
    try:
        sock.settimeout(timeout)
        return sock.recv(size)
    except Exception as e:
        return b''

# ============================================================
# 测试1: 长时间稳定性测试
# ============================================================
def test_long_running(host: str, port: int, duration_seconds: int = 1800):
    """长时间稳定性测试 - 持续发送心跳"""
    print(f"\n{'='*60}")
    print(f"测试1: 长时间稳定性测试 ({duration_seconds}秒)")
    print(f"{'='*60}")
    
    success_count = 0
    fail_count = 0
    latencies = []
    start_time = time.time()
    last_report = start_time
    
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10.0)
        sock.connect((host, port))
        print(f"连接建立成功")
        
        while time.time() - start_time < duration_seconds:
            client_ts = int(time.time() * 1000)
            msg = encode_heartbeat(client_ts)
            
            if send_raw(sock, msg):
                response = recv_raw(sock, 1024, 5.0)
                if response:
                    server_ts, recv_client_ts = decode_heartbeat(response)
                    if server_ts and recv_client_ts == client_ts:
                        success_count += 1
                        latencies.append(server_ts - client_ts)
                    else:
                        fail_count += 1
                else:
                    fail_count += 1
            else:
                fail_count += 1
            
            # 每10秒报告一次
            if time.time() - last_report >= 10:
                elapsed = time.time() - start_time
                avg_latency = sum(latencies[-100:]) / len(latencies[-100:]) if latencies else 0
                print(f"  [{elapsed:.0f}s] 成功: {success_count}, 失败: {fail_count}, "
                      f"最近100次平均延迟: {avg_latency:.2f}ms")
                last_report = time.time()
            
            time.sleep(1)  # 每秒发送一次心跳
        
        sock.close()
        
    except Exception as e:
        print(f"测试异常: {e}")
    
    print(f"\n长时间稳定性测试总结:")
    print(f"  总心跳数: {success_count + fail_count}")
    print(f"  成功: {success_count}")
    print(f"  失败: {fail_count}")
    print(f"  成功率: {success_count/(success_count+fail_count)*100:.2f}%")
    if latencies:
        print(f"  平均延迟: {sum(latencies)/len(latencies):.2f}ms")
        print(f"  最大延迟: {max(latencies)}ms")
        print(f"  最小延迟: {min(latencies)}ms")

# ============================================================
# 测试2: 异常场景测试
# ============================================================
def test_malformed_messages(host: str, port: int):
    """异常场景测试 - 发送错误格式的消息"""
    print(f"\n{'='*60}")
    print(f"测试2: 异常场景测试")
    print(f"{'='*60}")
    
    test_cases = [
        ("空消息", b''),
        ("随机数据", b'\x00\x01\x02\x03\x04\x05\x06\x07'),
        ("错误消息ID", struct.pack('<iI', 8, 0x99999999) + b'\x00' * 8),
        ("超长消息头", struct.pack('<iI', 999999, MSG_C2S_HEARTBEAT) + b'\x00' * 8),
        ("负数长度", struct.pack('<iI', -1, MSG_C2S_HEARTBEAT) + b'\x00' * 8),
        ("超大消息体声明", struct.pack('<iI', 1000000, MSG_C2S_HEARTBEAT)),
    ]
    
    for name, data in test_cases:
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(5.0)
            sock.connect((host, port))
            
            print(f"  测试: {name} ({len(data)}字节)...", end=' ')
            sock.sendall(data)
            
            # 等待1秒看服务器是否崩溃
            time.sleep(1)
            
            # 尝试发送正常心跳，看服务器是否还能响应
            client_ts = int(time.time() * 1000)
            msg = encode_heartbeat(client_ts)
            sock.sendall(msg)
            response = recv_raw(sock, 1024, 2.0)
            
            if response:
                print(f"通过 (服务器仍然正常)")
            else:
                print(f"警告 (服务器无响应)")
            
            sock.close()
            
        except Exception as e:
            print(f"失败 ({e})")

# ============================================================
# 测试3: 极限并发测试
# ============================================================
def test_concurrent_connections(host: str, port: int, max_connections: int = 1000):
    """极限并发测试"""
    print(f"\n{'='*60}")
    print(f"测试3: 极限并发测试 (最高 {max_connections} 连接)")
    print(f"{'='*60}")
    
    test_cases = [100, 200, 300, 500, 800, 1000]
    
    for conn_count in test_cases:
        if conn_count > max_connections:
            break
            
        print(f"\n  测试 {conn_count} 并发连接...")
        start = time.time()
        
        def test_single():
            try:
                sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                sock.settimeout(10.0)
                sock.connect((host, port))
                
                client_ts = int(time.time() * 1000)
                msg = encode_heartbeat(client_ts)
                sock.sendall(msg)
                
                response = recv_raw(sock, 1024, 5.0)
                server_ts, recv_client_ts = decode_heartbeat(response)
                
                sock.close()
                return server_ts is not None and recv_client_ts == client_ts
            except:
                return False
        
        with ThreadPoolExecutor(max_workers=conn_count) as executor:
            futures = [executor.submit(test_single) for _ in range(conn_count)]
            results = [f.result() for f in futures]
        
        elapsed = time.time() - start
        successes = sum(results)
        failures = conn_count - successes
        
        print(f"    成功: {successes}/{conn_count} ({successes/conn_count*100:.1f}%)")
        print(f"    失败: {failures}")
        print(f"    耗时: {elapsed:.2f}s")
        
        if successes < conn_count * 0.95:  # 成功率低于95%
            print(f"    ⚠️ 成功率低于95%，停止增加并发")
            break
        
        time.sleep(2)  # 间隔2秒

# ============================================================
# 测试4: 突然断开测试
# ============================================================
def test_abrupt_disconnect(host: str, port: int):
    """突然断开测试"""
    print(f"\n{'='*60}")
    print(f"测试4: 突然断开测试")
    print(f"{'='*60}")
    
    # 创建100个连接，然后突然全部断开
    print(f"  创建100个连接...")
    sockets = []
    for i in range(100):
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(5.0)
            sock.connect((host, port))
            sockets.append(sock)
        except:
            pass
    
    print(f"  成功创建 {len(sockets)} 个连接")
    
    # 突然全部断开（不发送关闭消息）
    print(f"  突然全部断开...")
    for sock in sockets:
        try:
            sock.close()
        except:
            pass
    
    time.sleep(2)
    
    # 检查服务器是否还能接受新连接
    print(f"  检查服务器状态...")
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5.0)
        sock.connect((host, port))
        
        client_ts = int(time.time() * 1000)
        msg = encode_heartbeat(client_ts)
        sock.sendall(msg)
        response = recv_raw(sock, 1024, 2.0)
        
        if response:
            print(f"  ✅ 服务器仍然正常")
        else:
            print(f"  ❌ 服务器无响应")
        
        sock.close()
    except Exception as e:
        print(f"  ❌ 连接失败: {e}")

# ============================================================
# 测试5: 连接池复用测试
# ============================================================
def test_connection_pool_reuse(host: str, port: int):
    """连接池复用稳定性测试"""
    print(f"\n{'='*60}")
    print(f"测试5: 连接池复用稳定性测试 (10轮 x 100连接)")
    print(f"{'='*60}")
    
    for round_num in range(1, 11):
        print(f"\n  第 {round_num}/10 轮...")
        start = time.time()
        
        def test_single():
            try:
                sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                sock.settimeout(5.0)
                sock.connect((host, port))
                
                client_ts = int(time.time() * 1000)
                msg = encode_heartbeat(client_ts)
                sock.sendall(msg)
                
                response = recv_raw(sock, 1024, 2.0)
                server_ts, recv_client_ts = decode_heartbeat(response)
                
                sock.close()
                return server_ts is not None and recv_client_ts == client_ts
            except:
                return False
        
        with ThreadPoolExecutor(max_workers=100) as executor:
            futures = [executor.submit(test_single) for _ in range(100)]
            results = [f.result() for f in futures]
        
        elapsed = time.time() - start
        successes = sum(results)
        print(f"    成功: {successes}/100, 耗时: {elapsed:.2f}s")
        
        if successes < 95:
            print(f"    ❌ 成功率过低，测试失败")
            return
        
        time.sleep(1)
    
    print(f"\n  ✅ 连接池复用测试通过")

# ============================================================
# 主函数
# ============================================================
def main():
    host = 'localhost'
    port = 7001
    
    print("="*60)
    print("MMORPG 框架全面验证测试")
    print("="*60)
    print(f"目标服务器: {host}:{port}")
    print(f"开始时间: {time.strftime('%Y-%m-%d %H:%M:%S')}")
    
    # 测试2: 异常场景
    test_malformed_messages(host, port)
    
    # 测试4: 突然断开
    test_abrupt_disconnect(host, port)
    
    # 测试5: 连接池复用
    test_connection_pool_reuse(host, port)
    
    # 测试3: 极限并发
    test_concurrent_connections(host, port, 1000)
    
    # 测试1: 长时间稳定性 (最后运行，因为时间最长)
    test_long_running(host, port, 1800)  # 30分钟
    
    print(f"\n{'='*60}")
    print("全面验证测试完成")
    print(f"结束时间: {time.strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"{'='*60}")

if __name__ == '__main__':
    main()