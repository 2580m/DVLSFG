"""Correlate native zoom samples with independent PresentMon display events."""
import argparse
import csv
import json
from pathlib import Path
import numpy as np

p = argparse.ArgumentParser()
p.add_argument('--run', type=Path, required=True)
p.add_argument('--fixture', type=Path, required=True)
args = p.parse_args()
for trace in sorted(args.run.glob('*-present.csv')):
    stage = trace.name.removesuffix('-present.csv')
    frames = args.fixture / f'zoom-{stage}.csv'
    if not frames.exists():
        continue
    with trace.open(newline='') as stream:
        events = list(csv.DictReader(stream))
    times = [float(r['QPCTime']) for r in events]
    start, end = min(times) + 1, max(times) - 1
    events = [r for r in events if start <= float(r['QPCTime']) < end]
    with frames.open(newline='') as stream:
        all_frames = [{k: float(v) for k, v in r.items()} for r in csv.DictReader(stream)]
    samples = [r for r in all_frames if start <= r['qpc'] < end]
    if not samples:
        continue
    ms = np.array([r['frame_ms'] for r in samples])
    fov = np.array([r['fov'] for r in samples])
    moving = np.abs(np.diff(fov, prepend=fov[0])) > .1
    shown = [r for r in events if r['Dropped'] == '0']
    gaps = [float(r['msBetweenDisplayChange']) for r in shown if float(r['msBetweenDisplayChange']) > 0]
    result = dict(stage=stage, source_fps=round(1000 / ms.mean(), 2),
                  source_ms_p50_p95_p99=np.percentile(ms, [50, 95, 99]).round(3).tolist(),
                  source_max_ms=round(float(ms.max()), 3),
                  source_over_20ms=int(sum(ms > 20)),
                  displayed_fps=round(len(shown) / (end-start), 2),
                  requested_fps=round(len(events) / (end-start), 2),
                  display_gap_p50_p95=np.percentile(gaps, [50, 95]).round(3).tolist() if gaps else [],
                  fov_range=[round(float(fov.min()), 3), round(float(fov.max()), 3)],
                  fov_changing_frames=int(sum(moving)),
                  sr_fov_changes=int(samples[-1]['sr_fov_changes']-samples[0]['sr_fov_changes']),
                  fg_fov_changes=int(samples[-1]['fg_fov_changes']-samples[0]['fg_fov_changes']),
                  sr_actual_reset_frames=int(samples[-1]['sr_reset_frames']-samples[0]['sr_reset_frames']),
                  fg_actual_reset_frames=int(samples[-1]['fg_reset_frames']-samples[0]['fg_reset_frames']) if 'fg_reset_frames' in samples[0] else None,
                  moving_mean_ms=round(float(ms[moving].mean()), 3) if moving.any() else None,
                  settled_mean_ms=round(float(ms[~moving].mean()), 3) if (~moving).any() else None)
    print(json.dumps(result))
