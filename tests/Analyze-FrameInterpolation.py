"""Measure real-scene interpolation position and pre-capture present cadence."""
import argparse
import csv
from pathlib import Path
import numpy as np
from PIL import Image


def displacement(a, b):
    window = np.outer(np.hanning(a.shape[0]), np.hanning(a.shape[1]))
    fa = np.fft.fft2((a - a.mean()) * window)
    fb = np.fft.fft2((b - b.mean()) * window)
    cross = fb * fa.conj()
    correlation = np.fft.ifft2(cross / np.maximum(abs(cross), 1e-8)).real
    y, x = np.unravel_index(correlation.argmax(), correlation.shape)
    def peak(center, before, after):
        denominator = before - 2 * center + after
        return .5 * (before - after) / denominator if abs(denominator) > 1e-10 else 0
    dx = x + peak(correlation[y, x], correlation[y, (x - 1) % a.shape[1]], correlation[y, (x + 1) % a.shape[1]])
    dy = y + peak(correlation[y, x], correlation[(y - 1) % a.shape[0], x], correlation[(y + 1) % a.shape[0], x])
    if dx > a.shape[1] / 2: dx -= a.shape[1]
    if dy > a.shape[0] / 2: dy -= a.shape[0]
    return np.array([dx, dy]), float(correlation[y, x])


def analyze(input_path, check=False):
    def read(suffix):
        return np.asarray(Image.open(str(input_path).replace('-input.bmp', suffix)).convert('L'), dtype=float)
    previous, current, generated = read('-previous-scene.bmp'), read('-current-scene.bmp'), read('-generated.bmp')
    h, w = current.shape
    fractions = []
    for cy in (.3, .5, .7):
        for cx in (.25, .5, .75):
            x, y = int(w * cx), int(h * cy)
            crop = np.s_[y-128:y+128, x-128:x+128]
            delta, confidence = displacement(previous[crop], current[crop])
            intermediate, generated_confidence = displacement(previous[crop], generated[crop])
            if np.linalg.norm(delta) < 1 or confidence < .05 or generated_confidence < .05:
                continue
            fraction = float(intermediate @ delta / (delta @ delta))
            fractions.append(fraction)
            print(f'Patch {cx},{cy}: real shift={delta.round(3)}, generated shift={intermediate.round(3)}, fraction={fraction:.3f}')
    if fractions:
        print(f'Median interpolation position={np.median(fractions):.3f} (previous=0, midpoint=0.5, current=1)')
    with open(str(input_path).replace('-input.bmp', '-outputs.csv'), newline='') as stream:
        rows = list(csv.DictReader(stream))
    times = np.array([float(row['milliseconds']) for row in rows])
    kinds = np.array([int(row['generated']) for row in rows])
    gaps = np.diff(times)
    medians = []
    for kind, label in ((0, 'generated -> real'), (1, 'real -> generated')):
        values = gaps[kinds[1:] == kind]
        medians.append(float(np.median(values)))
        print(f'{label}: median={np.median(values):.3f} ms, p10={np.percentile(values,10):.3f}, p90={np.percentile(values,90):.3f}')
    if check:
        if len(fractions) < 3 or not .3 < np.median(fractions) < .7:
            raise AssertionError('Insufficient motion or generated frame is not intermediate')
        if min(medians) <= 0 or max(medians) / min(medians) > 1.25:
            raise AssertionError('Alternating present request intervals remain uneven')
        print('PASS: scene motion is intermediate and median present request intervals agree within 25%')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('input', type=Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    analyze(args.input, args.check)
