"""Match fixed-view source and managed CPU timings to actual displayed events."""
import argparse
import csv
import json
from pathlib import Path
import numpy as np

p = argparse.ArgumentParser()
p.add_argument('--run', type=Path, required=True)
p.add_argument('--fixture', type=Path, required=True)
a = p.parse_args()
for trace in sorted(a.run.glob('*-present.csv')):
    stage = trace.name.removesuffix('-present.csv')
    path = a.fixture / f'throughput-{stage}.csv'
    if not path.exists():
        continue
    with trace.open(newline='') as stream:
        events = list(csv.DictReader(stream))
    start = min(float(r['QPCTime']) for r in events) + 1
    end = max(float(r['QPCTime']) for r in events) - 1
    events = [r for r in events if start <= float(r['QPCTime']) < end]
    with path.open(newline='') as stream:
        frames = [{k: float(v) for k, v in r.items()} for r in csv.DictReader(stream)]
    frames = [r for r in frames if start <= r['qpc'] < end]
    if not frames:
        continue
    ms = np.array([r['frame_ms'] for r in frames])
    print(json.dumps(dict(stage=stage, source_fps=round(1000/ms.mean(), 2),
        displayed_fps=round(sum(r['Dropped'] == '0' for r in events)/(end-start), 2),
        submitted_fps=round(len(events)/(end-start), 2),
        frame_ms_p50_p95_p99=np.percentile(ms,[50,95,99]).round(3).tolist(),
        mean_ui_cpu_ms=round(np.mean([r['ui_ms'] for r in frames]),3),
        mean_capture_cpu_ms=round(np.mean([r['capture_ms'] for r in frames]),3),
        ui_redraw_percent=round(100*np.mean([r.get('ui_redrawn',1) for r in frames]),2),
        canvases=sorted(set(r['canvases'] for r in frames)),
        layers=sorted(set(r['layers'] for r in frames)),
        target=sorted(set(r['target'] for r in frames)),
        vsync=sorted(set(r['vsync'] for r in frames)))))
