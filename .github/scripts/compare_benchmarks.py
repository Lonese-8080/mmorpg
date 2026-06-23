#!/usr/bin/env python3
"""
Benchmark Results Comparison Script

Compares current benchmark results with previous results and generates a report.
Detects performance regressions based on configurable thresholds.

Usage:
    python3 compare_benchmarks.py \
        --previous previous-results/*.json \
        --current current-results/*.json \
        --threshold-warning 10 \
        --threshold-failure 20 \
        --output comparison-report.md
"""

import argparse
import json
import glob
import sys
from pathlib import Path
from typing import Dict, List, Tuple, Optional

def load_benchmark_results(file_pattern: str) -> Dict[str, Dict]:
    """Load benchmark results from JSON files."""
    results = {}
    for file_path in glob.glob(file_pattern):
        with open(file_path, 'r') as f:
            data = json.load(f)
            for benchmark in data.get('Benchmarks', []):
                name = benchmark.get('FullName', benchmark.get('Name', 'Unknown'))
                results[name] = {
                    'mean_ns': benchmark.get('Statistics', {}).get('Mean', 0),
                    'stddev_ns': benchmark.get('Statistics', {}).get('StandardDeviation', 0),
                    'median_ns': benchmark.get('Statistics', {}).get('Median', 0),
                    'allocated_bytes': benchmark.get('Memory', {}).get('BytesAllocatedPerOperation', 0),
                    'operations_per_second': 1e9 / benchmark.get('Statistics', {}).get('Mean', 1e9) if benchmark.get('Statistics', {}).get('Mean', 0) > 0 else 0
                }
    return results

def compare_results(
    previous: Dict[str, Dict],
    current: Dict[str, Dict],
    threshold_warning: float,
    threshold_failure: float
) -> Tuple[List[Dict], bool, bool]:
    """Compare benchmark results and detect regressions."""
    comparisons = []
    has_warning = False
    has_failure = False
    
    for name, current_data in current.items():
        if name not in previous:
            comparisons.append({
                'name': name,
                'status': 'new',
                'mean_change': None,
                'ops_change': None,
                'memory_change': None
            })
            continue
        
        prev_data = previous[name]
        
        # Calculate percentage changes
        mean_change = ((current_data['mean_ns'] - prev_data['mean_ns']) / prev_data['mean_ns']) * 100 if prev_data['mean_ns'] > 0 else 0
        ops_change = ((current_data['operations_per_second'] - prev_data['operations_per_second']) / prev_data['operations_per_second']) * 100 if prev_data['operations_per_second'] > 0 else 0
        memory_change = ((current_data['allocated_bytes'] - prev_data['allocated_bytes']) / prev_data['allocated_bytes']) * 100 if prev_data['allocated_bytes'] > 0 else 0
        
        # Determine status
        status = 'ok'
        if mean_change > threshold_failure:
            status = 'failure'
            has_failure = True
        elif mean_change > threshold_warning:
            status = 'warning'
            has_warning = True
        
        comparisons.append({
            'name': name,
            'status': status,
            'mean_change': mean_change,
            'ops_change': ops_change,
            'memory_change': memory_change,
            'previous_mean_ns': prev_data['mean_ns'],
            'current_mean_ns': current_data['mean_ns'],
            'previous_ops': prev_data['operations_per_second'],
            'current_ops': current_data['operations_per_second'],
            'previous_memory': prev_data['allocated_bytes'],
            'current_memory': current_data['allocated_bytes']
        })
    
    return comparisons, has_warning, has_failure

def generate_report(comparisons: List[Dict], has_warning: bool, has_failure: bool) -> str:
    """Generate markdown comparison report."""
    lines = []
    
    # Header
    lines.append("# Performance Benchmark Comparison Report\n")
    lines.append(f"**Generated**: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n\n")
    
    # Summary
    if has_failure:
        lines.append("## ❌ PERFORMANCE_REGRESSION_FAILURE\n")
        lines.append("Performance regression exceeds failure threshold. CI failed.\n\n")
    elif has_warning:
        lines.append("## ⚠️ PERFORMANCE_REGRESSION_WARNING\n")
        lines.append("Performance regression detected but within warning threshold.\n\n")
    else:
        lines.append("## ✅ No Performance Regression\n")
        lines.append("All benchmarks are within acceptable thresholds.\n\n")
    
    # Table header
    lines.append("| Benchmark | Status | Mean Change | Ops/sec Change | Memory Change |\n")
    lines.append("|-----------|--------|-------------|----------------|---------------|\n")
    
    # Table rows
    for comp in comparisons:
        status_icon = {
            'ok': '✅',
            'warning': '⚠️',
            'failure': '❌',
            'new': '🆕'
        }.get(comp['status'], '❓')
        
        mean_change = f"{comp['mean_change']:+.2f}%" if comp['mean_change'] is not None else 'N/A'
        ops_change = f"{comp['ops_change']:+.2f}%" if comp['ops_change'] is not None else 'N/A'
        memory_change = f"{comp['memory_change']:+.2f}%" if comp['memory_change'] is not None else 'N/A'
        
        lines.append(f"| {comp['name']} | {status_icon} | {mean_change} | {ops_change} | {memory_change} |\n")
    
    # Details section
    lines.append("\n## Detailed Results\n\n")
    
    for comp in comparisons:
        if comp['status'] in ['warning', 'failure']:
            lines.append(f"### {comp['name']}\n\n")
            lines.append(f"- **Previous Mean**: {comp['previous_mean_ns']:.2f} ns\n")
            lines.append(f"- **Current Mean**: {comp['current_mean_ns']:.2f} ns\n")
            lines.append(f"- **Change**: {comp['mean_change']:+.2f}%\n")
            lines.append(f"- **Previous Ops/sec**: {comp['previous_ops']:.2f}\n")
            lines.append(f"- **Current Ops/sec**: {comp['current_ops']:.2f}\n\n")
    
    return ''.join(lines)

def main():
    parser = argparse.ArgumentParser(description='Compare benchmark results')
    parser.add_argument('--previous', required=True, help='Previous benchmark results file pattern')
    parser.add_argument('--current', required=True, help='Current benchmark results file pattern')
    parser.add_argument('--threshold-warning', type=float, default=10, help='Warning threshold percentage')
    parser.add_argument('--threshold-failure', type=float, default=20, help='Failure threshold percentage')
    parser.add_argument('--output', required=True, help='Output report file path')
    
    args = parser.parse_args()
    
    # Load results
    previous_results = load_benchmark_results(args.previous)
    current_results = load_benchmark_results(args.current)
    
    if not current_results:
        print("Error: No current benchmark results found")
        sys.exit(1)
    
    # Compare
    comparisons, has_warning, has_failure = compare_results(
        previous_results,
        current_results,
        args.threshold_warning,
        args.threshold_failure
    )
    
    # Generate report
    report = generate_report(comparisons, has_warning, has_failure)
    
    # Write report
    Path(args.output).write_text(report)
    
    # Print summary
    print(f"Comparison completed: {len(comparisons)} benchmarks analyzed")
    if has_failure:
        print("PERFORMANCE_REGRESSION_FAILURE: Performance regression exceeds threshold")
        sys.exit(1)
    elif has_warning:
        print("PERFORMANCE_REGRESSION_WARNING: Performance regression detected")
    else:
        print("No performance regression detected")

if __name__ == '__main__':
    from datetime import datetime
    main()