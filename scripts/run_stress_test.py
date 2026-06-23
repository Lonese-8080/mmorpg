#!/usr/bin/env python3
"""
自动化性能压测脚本

支持多种压测场景：
- 消息吞吐量测试
- 连接建立速度测试
- 弹性策略触发测试
- 混合场景测试

使用方式：
    python3 run_stress_test.py --scenario throughput --connections 1000 --duration 60
    python3 run_stress_test.py --scenario connections --count 10000 --rate 100
    python3 run_stress_test.py --scenario resilience --failure-rate 0.5 --duration 120
"""

import argparse
import subprocess
import json
import time
import sys
import os
from datetime import datetime
from pathlib import Path

class StressTestRunner:
    def __init__(self, output_dir: str = "stress_test_results"):
        self.output_dir = Path(output_dir)
        self.output_dir.mkdir(parents=True, exist_ok=True)
        self.timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    
    def run_throughput_test(self, connections: int, message_rate: int, duration: int) -> dict:
        """运行消息吞吐量测试"""
        print(f"=== 吞吐量测试 ===")
        print(f"连接数: {connections}, 消息速率: {message_rate}/s, 持续时间: {duration}s")
        
        # 构建命令
        cmd = [
            "dotnet", "run",
            "--project", "tests/MMORPG.Framework.StressTests",
            "--configuration", "Release",
            "--",
            "--scenario", "throughput",
            "--connections", str(connections),
            "--message-rate", str(message_rate),
            "--duration", str(duration)
        ]
        
        # 运行测试
        start_time = time.time()
        result = subprocess.run(cmd, capture_output=True, text=True, cwd=os.getcwd())
        elapsed_time = time.time() - start_time
        
        # 解析结果
        results = {
            "scenario": "throughput",
            "params": {
                "connections": connections,
                "message_rate": message_rate,
                "duration": duration
            },
            "elapsed_time": elapsed_time,
            "stdout": result.stdout,
            "stderr": result.stderr,
            "returncode": result.returncode,
            "timestamp": self.timestamp
        }
        
        # 保存结果
        self._save_results(results, "throughput")
        
        return results
    
    def run_connections_test(self, count: int, rate: int) -> dict:
        """运行连接建立速度测试"""
        print(f"=== 连接建立测试 ===")
        print(f"总连接数: {count}, 连接速率: {rate}/s")
        
        cmd = [
            "dotnet", "run",
            "--project", "tests/MMORPG.Framework.StressTests",
            "--configuration", "Release",
            "--",
            "--scenario", "connections",
            "--count", str(count),
            "--rate", str(rate)
        ]
        
        start_time = time.time()
        result = subprocess.run(cmd, capture_output=True, text=True, cwd=os.getcwd())
        elapsed_time = time.time() - start_time
        
        results = {
            "scenario": "connections",
            "params": {
                "count": count,
                "rate": rate
            },
            "elapsed_time": elapsed_time,
            "stdout": result.stdout,
            "stderr": result.stderr,
            "returncode": result.returncode,
            "timestamp": self.timestamp
        }
        
        self._save_results(results, "connections")
        
        return results
    
    def run_resilience_test(self, failure_rate: float, duration: int) -> dict:
        """运行弹性策略测试"""
        print(f"=== 弹性策略测试 ===")
        print(f"失败率: {failure_rate * 100}%, 持续时间: {duration}s")
        
        cmd = [
            "dotnet", "run",
            "--project", "tests/MMORPG.Framework.StressTests",
            "--configuration", "Release",
            "--",
            "--scenario", "resilience",
            "--failure-rate", str(failure_rate),
            "--duration", str(duration)
        ]
        
        start_time = time.time()
        result = subprocess.run(cmd, capture_output=True, text=True, cwd=os.getcwd())
        elapsed_time = time.time() - start_time
        
        results = {
            "scenario": "resilience",
            "params": {
                "failure_rate": failure_rate,
                "duration": duration
            },
            "elapsed_time": elapsed_time,
            "stdout": result.stdout,
            "stderr": result.stderr,
            "returncode": result.returncode,
            "timestamp": self.timestamp
        }
        
        self._save_results(results, "resilience")
        
        return results
    
    def run_mixed_test(self, connections: int, message_rate: int, failure_rate: float, duration: int) -> dict:
        """运行混合场景测试"""
        print(f"=== 混合场景测试 ===")
        print(f"连接数: {connections}, 消息速率: {message_rate}/s, 失败率: {failure_rate * 100}%, 持续时间: {duration}s")
        
        cmd = [
            "dotnet", "run",
            "--project", "tests/MMORPG.Framework.StressTests",
            "--configuration", "Release",
            "--",
            "--scenario", "mixed",
            "--connections", str(connections),
            "--message-rate", str(message_rate),
            "--failure-rate", str(failure_rate),
            "--duration", str(duration)
        ]
        
        start_time = time.time()
        result = subprocess.run(cmd, capture_output=True, text=True, cwd=os.getcwd())
        elapsed_time = time.time() - start_time
        
        results = {
            "scenario": "mixed",
            "params": {
                "connections": connections,
                "message_rate": message_rate,
                "failure_rate": failure_rate,
                "duration": duration
            },
            "elapsed_time": elapsed_time,
            "stdout": result.stdout,
            "stderr": result.stderr,
            "returncode": result.returncode,
            "timestamp": self.timestamp
        }
        
        self._save_results(results, "mixed")
        
        return results
    
    def _save_results(self, results: dict, scenario: str):
        """保存测试结果"""
        filename = f"{scenario}_{self.timestamp}.json"
        filepath = self.output_dir / filename
        
        with open(filepath, 'w', encoding='utf-8') as f:
            json.dump(results, f, indent=2, ensure_ascii=False)
        
        print(f"结果已保存: {filepath}")
    
    def generate_report(self, results_list: list) -> str:
        """生成测试报告"""
        report_lines = []
        report_lines.append("# 性能压测报告\n\n")
        report_lines.append(f"**测试日期**: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n\n")
        
        for results in results_list:
            scenario = results.get("scenario", "unknown")
            params = results.get("params", {})
            elapsed_time = results.get("elapsed_time", 0)
            stdout = results.get("stdout", "")
            
            report_lines.append(f"## {scenario.upper()} 测试\n\n")
            report_lines.append(f"**参数**: {json.dumps(params)}\n\n")
            report_lines.append(f"**耗时**: {elapsed_time:.2f}s\n\n")
            report_lines.append(f"**输出**:\n```\n{stdout}\n```\n\n")
        
        report = ''.join(report_lines)
        
        # 保存报告
        report_path = self.output_dir / f"report_{self.timestamp}.md"
        with open(report_path, 'w', encoding='utf-8') as f:
            f.write(report)
        
        print(f"报告已生成: {report_path}")
        
        return report

def main():
    parser = argparse.ArgumentParser(description='MMORPG 框架性能压测脚本')
    
    parser.add_argument('--scenario', choices=['throughput', 'connections', 'resilience', 'mixed', 'all'],
                        default='throughput', help='测试场景')
    
    # 吞吐量测试参数
    parser.add_argument('--connections', type=int, default=1000, help='并发连接数')
    parser.add_argument('--message-rate', type=int, default=10000, help='消息速率（msg/s）')
    parser.add_argument('--duration', type=int, default=60, help='测试持续时间（s）')
    
    # 连接测试参数
    parser.add_argument('--count', type=int, default=10000, help='总连接数')
    parser.add_argument('--rate', type=int, default=100, help='连接速率（conn/s）')
    
    # 弹性测试参数
    parser.add_argument('--failure-rate', type=float, default=0.5, help='失败率（0.0-1.0）')
    
    # 输出目录
    parser.add_argument('--output-dir', default='stress_test_results', help='结果输出目录')
    
    args = parser.parse_args()
    
    runner = StressTestRunner(args.output_dir)
    results_list = []
    
    try:
        if args.scenario == 'throughput':
            results = runner.run_throughput_test(args.connections, args.message_rate, args.duration)
            results_list.append(results)
        
        elif args.scenario == 'connections':
            results = runner.run_connections_test(args.count, args.rate)
            results_list.append(results)
        
        elif args.scenario == 'resilience':
            results = runner.run_resilience_test(args.failure_rate, args.duration)
            results_list.append(results)
        
        elif args.scenario == 'mixed':
            results = runner.run_mixed_test(args.connections, args.message_rate, args.failure_rate, args.duration)
            results_list.append(results)
        
        elif args.scenario == 'all':
            # 运行所有场景
            results_list.append(runner.run_throughput_test(1000, 10000, 60))
            results_list.append(runner.run_connections_test(10000, 100))
            results_list.append(runner.run_resilience_test(0.5, 120))
            results_list.append(runner.run_mixed_test(1000, 10000, 0.3, 120))
        
        # 生成报告
        runner.generate_report(results_list)
        
        print("\n=== 测试完成 ===")
        
    except Exception as e:
        print(f"测试失败: {e}")
        sys.exit(1)

if __name__ == '__main__':
    main()