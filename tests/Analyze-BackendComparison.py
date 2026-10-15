"""Compare game source samples with independent PresentMon v1 display events.

offset-seconds maps fixture Stopwatch seconds to PresentMon QPC seconds.
Trim stage boundaries to exclude toggling, warmup and clock calibration error.
"""
import argparse
import csv
import json
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument('--stages')
parser.add_argument('--trace', action='append', required=True)
parser.add_argument('--offset-seconds', type=float, default=0)
parser.add_argument('--trim', type=float, default=2)
args = parser.parse_args()
events = []
for path in args.trace:
    with open(path, newline='') as stream:
        for row in csv.DictReader(stream):
            try:
                row['qpc'] = float(row['QPCTime'])
                row['gap'] = float(row['msBetweenDisplayChange'])
                row['shown'] = row['Dropped'] == '0'
                row['trace'] = path
                events.append(row)
            except (TypeError, ValueError, KeyError):
                pass  # A terminated recorder may leave an incomplete final row.
if args.stages:
    with open(args.stages, newline='') as stream:
        stages = list(csv.DictReader(stream))
else:
    stages = []
    for path in args.trace:
        sample = [r for r in events if r['trace'] == path]
        if not sample:
            continue
        stages.append(dict(trace=path, backend=0, limit=0, enabled=True,
                           start_qpc_ms=min(r['qpc'] for r in sample)*1000,
                           end_qpc_ms=max(r['qpc'] for r in sample)*1000,
                           source_fps=0))
for stage in stages:
    start = float(stage['start_qpc_ms']) / 1000 + args.offset_seconds + args.trim
    end = float(stage['end_qpc_ms']) / 1000 + args.offset_seconds - args.trim
    sample = [r for r in events if start <= r['qpc'] < end and ('trace' not in stage or r['trace'] == stage['trace'])]
    shown = [r for r in sample if r['shown']]
    gaps = [r['gap'] for r in shown if r['gap'] > 0]
    result = dict(backend=int(stage['backend']), limit=int(stage['limit']),
                  trace=stage.get('trace'),
                  enabled=stage['enabled'], source_fps=float(stage['source_fps']),
                  requests_fps=round(len(sample) / (end-start), 3),
                  displayed_fps=round(len(shown) / (end-start), 3),
                  dropped_percent=round(100*(len(sample)-len(shown))/max(1, len(sample)), 2),
                  display_gap_p10_p50_p90_ms=np.percentile(gaps, [10, 50, 90]).round(3).tolist() if gaps else [],
                  modes=sorted(set(r['PresentMode'] for r in sample)))
    print(json.dumps(result))
