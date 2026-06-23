#!/usr/bin/env python3
"""调试心跳测试 - 显示详细信息"""

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
        return None, None, None
    length, msg_id = struct.unpack('<iI', data[:8])
    server_ts, client_ts = struct.unpack('<qq', data[8:24])
    return length, msg_id, server_ts, client_ts

def main():
    host = 'localhost'
    port = 7001
    
    print(f"连接 {host}:{port}...")
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(10.0)
    sock.connect((host, port))
    print("连接成功")
    
    # 测试5次心跳
    for i in range(1, 6):
        client_ts = int(time.time() * 1000)
        msg = encode_heartbeat(client_ts)
        
        print(f"\n[{i}] 发送心跳: client_ts={client_ts}, 数据长度={len(msg)}")
        print(f"    发送数据: {msg.hex()}")
        
        try:
            sock.sendall(msg)
            print("    发送成功")
            
            start_recv = time.time()
            response = sock.recv(1024)
            recv_time = (time.time() - start_recv) * 1000
            
            print(f"    收到响应: {len(response)}字节, 耗时={recv_time:.2f}ms")
            print(f"    响应数据: {response.hex()}")
            
            if len(response) >= 24:
                length, msg_id, server_ts, recv_client_ts = decode_heartbeat(response)
                print(f"    解析结果: length={length}, msg_id=0x{msg_id:08X}, "
                      f"server_ts={server_ts}, recv_client_ts={recv_client_ts}")
                
                if msg_id == MSG_S2C_HEARTBEAT and recv_client_ts == client_ts:
                    print(f"    ✅ 成功! 延迟={server_ts - client_ts}ms")
                else:
                    print(f"    ❌ 失败: msg_id不匹配或client_ts不匹配")
            else:
                print(f"    ❌ 失败: 数据太短")
                
        except socket.timeout:
            print(f"    ❌ 失败: 超时")
        except Exception as e:
            print(f"    ❌ 失败: {e}")
        
        time.sleep(1)
    
    sock.close()
    print("\n测试结束")

if __name__ == '__main__':
    main()