#!/usr/bin/env python3
"""
修复后的心跳测试 - 处理TCP粘包
"""

import socket
import struct
import time

MSG_C2S_HEARTBEAT = 0x00000003
MSG_S2C_HEARTBEAT = 0x00000004
HEADER_SIZE = 8  # 4字节length + 4字节msg_id

def encode_heartbeat(client_timestamp: int) -> bytes:
    body = struct.pack('<q', client_timestamp)
    header = struct.pack('<iI', len(body), MSG_C2S_HEARTBEAT)
    return header + body

def decode_messages(data: bytes) -> list:
    """解析所有粘包消息"""
    messages = []
    offset = 0
    
    while offset + HEADER_SIZE <= len(data):
        # 读取消息头
        length, msg_id = struct.unpack('<iI', data[offset:offset + HEADER_SIZE])
        
        # 检查数据完整性
        if offset + HEADER_SIZE + length > len(data):
            break  # 数据不完整，等待更多数据
        
        # 读取消息体
        body_start = offset + HEADER_SIZE
        body_end = body_start + length
        body = data[body_start:body_end]
        
        messages.append({
            'msg_id': msg_id,
            'length': length,
            'body': body
        })
        
        offset = body_end
    
    return messages

def main():
    host = 'localhost'
    port = 7001
    
    print(f"连接 {host}:{port}...")
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(5.0)
    sock.connect((host, port))
    print("连接成功")
    
    # 接收缓冲区
    recv_buffer = b''
    
    # 测试10次心跳
    for i in range(1, 11):
        client_ts = int(time.time() * 1000)
        msg = encode_heartbeat(client_ts)
        
        print(f"\n[{i}] 发送心跳: client_ts={client_ts}")
        
        try:
            sock.sendall(msg)
            
            # 接收数据（可能包含多个粘包消息）
            recv_start = time.time()
            chunk = sock.recv(4096)
            recv_time = (time.time() - recv_start) * 1000
            
            recv_buffer += chunk
            print(f"    收到 {len(chunk)} 字节, recv_buffer={len(recv_buffer)}字节")
            
            # 解析所有消息
            messages = decode_messages(recv_buffer)
            
            if not messages:
                print(f"    ❌ 没有完整消息")
                continue
            
            # 查找匹配的心跳响应
            found = False
            for msg in messages:
                if msg['msg_id'] == MSG_S2C_HEARTBEAT and len(msg['body']) >= 16:
                    server_ts, recv_client_ts = struct.unpack('<qq', msg['body'][:16])
                    
                    # 清除已处理的消息
                    recv_buffer = recv_buffer[HEADER_SIZE + msg['length']:]
                    
                    if recv_client_ts == client_ts:
                        latency = server_ts - client_ts
                        print(f"    ✅ 成功! server_ts={server_ts}, 延迟={latency}ms, "
                              f"剩余缓冲={len(recv_buffer)}字节")
                        found = True
                        break
            
            if not found:
                print(f"    ⚠️ 没有匹配的消息, recv_buffer={len(recv_buffer)}字节")
                for j, m in enumerate(messages):
                    if len(m['body']) >= 16:
                        server_ts, recv_client_ts = struct.unpack('<qq', m['body'][:16])
                        print(f"       消息{j}: msg_id=0x{m['msg_id']:08X}, "
                              f"server_ts={server_ts}, recv_client_ts={recv_client_ts}")
                
        except socket.timeout:
            print(f"    ❌ 超时")
        except Exception as e:
            print(f"    ❌ 错误: {e}")
        
        time.sleep(0.5)  # 500ms间隔
    
    sock.close()
    print("\n测试结束")

if __name__ == '__main__':
    main()