#!/usr/bin/env python3
"""调试 TCP 连接测试"""

import socket
import struct
import time

MSG_C2S_HEARTBEAT = 0x00000003
MSG_S2C_HEARTBEAT = 0x00000004

def encode_heartbeat(client_timestamp: int) -> bytes:
    """编码心跳请求消息（小端序）"""
    msg_id = MSG_C2S_HEARTBEAT
    body = struct.pack('<q', client_timestamp)
    length = len(body)
    header = struct.pack('<iI', length, msg_id)
    return header + body

def decode_heartbeat(data: bytes) -> tuple:
    """解码心跳响应消息（小端序）"""
    if len(data) < 24:
        print(f"  响应数据太短: {len(data)} 字节")
        return None, None
    length, msg_id = struct.unpack('<iI', data[:8])
    print(f"  响应头: length={length}, msg_id=0x{msg_id:08X}")
    if msg_id != MSG_S2C_HEARTBEAT:
        print(f"  消息ID不匹配: 期望 0x{MSG_S2C_HEARTBEAT:08X}, 实际 0x{msg_id:08X}")
        return None, None
    server_ts, client_ts = struct.unpack('<qq', data[8:24])
    return server_ts, client_ts

def main():
    host = 'localhost'
    port = 7001
    
    print(f"连接 {host}:{port}...")
    
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5.0)
        sock.connect((host, port))
        print("连接成功!")
        
        # 发送心跳
        client_ts = int(time.time() * 1000)
        msg = encode_heartbeat(client_ts)
        print(f"发送心跳: client_ts={client_ts}")
        print(f"发送数据: {msg.hex()}")
        sock.sendall(msg)
        
        # 接收响应
        print("等待响应...")
        response = sock.recv(1024)
        print(f"收到响应: {len(response)} 字节")
        print(f"响应数据: {response.hex()}")
        
        server_ts, recv_client_ts = decode_heartbeat(response)
        
        if server_ts:
            print(f"心跳成功!")
            print(f"  ServerTimestamp: {server_ts}")
            print(f"  ClientTimestamp: {recv_client_ts}")
            print(f"  延迟: {server_ts - client_ts}ms")
        else:
            print("心跳响应解析失败")
        
        sock.close()
        
    except Exception as e:
        print(f"错误: {e}")
        import traceback
        traceback.print_exc()

if __name__ == '__main__':
    main()