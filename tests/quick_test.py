#!/usr/bin/env python3
"""快速测试单个连接"""

import socket
import struct
import time

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
    
    print(f"测试连接 {host}:{port}...")
    
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5.0)
        sock.connect((host, port))
        print("连接成功!")
        
        client_ts = int(time.time() * 1000)
        msg = encode_heartbeat(client_ts)
        sock.sendall(msg)
        
        response = sock.recv(1024)
        print(f"收到 {len(response)} 字节")
        
        server_ts, recv_client_ts = decode_heartbeat(response)
        if server_ts:
            print(f"成功! 延迟: {server_ts - client_ts}ms")
        else:
            print("解析失败")
        
        sock.close()
        
    except Exception as e:
        print(f"失败: {e}")

if __name__ == '__main__':
    main()