#!/usr/bin/env python3
"""NB20 round 0: analytic pixel table of the plan's settled shoulder camera (pivot 1.7 m, offset (0.7, 0.55, -2.8) turned by pitch, FOV 60 vertical, 1920x1080).
Sizes in screen pixels of the ㅏ head (0.5 m along the lane), the tail (1.2 m along), the lane width (0.3 m across), an upright contact stroke (0.6 m),
at chest height 1.1 m (the path's judged height) and on the ground, for a target 3 / 6 / 12 m ahead and pitch 0 / 15 / 30 deg.
python Tools/Art/SpellVFX308/nb20/pixel_table.py   (prints a markdown table)"""
import math
import numpy as np

W, H = 1920, 1080
F = (H / 2) / math.tan(math.radians(30))


def cam(pitch):
    p = math.radians(pitch)
    # Unity: x right, y up, z forward; Euler(pitch, 0, 0) rotates about x (positive = look down)
    # Unity left-handed: a positive x rotation tips forward (0,0,1) down to (0, -sin, cos)
    R = np.array([[1, 0, 0], [0, math.cos(p), math.sin(p)], [0, -math.sin(p), math.cos(p)]]).T
    pivot = np.array([0, 1.7, 0])
    off = np.array([0.7, 0.55, -2.8])
    pos = pivot + R @ off
    fwd = R @ np.array([0, 0, 1.0]); up = R @ np.array([0, 1.0, 0]); right = np.array([1.0, 0, 0])
    return pos, fwd, up, right


def proj(pt, c):
    pos, fwd, up, right = c
    d = np.asarray(pt) - pos
    z = d @ fwd
    return np.array([W / 2 + F * (d @ right) / z, H / 2 - F * (d @ up) / z]), z


def size(a, b, c):
    pa, _ = proj(a, c); pb, _ = proj(b, c)
    return float(np.linalg.norm(pa - pb))


def main():
    print('| pitch | dist | y (chest) | head 0.5 m @1.1 m | head on ground | tail 1.2 m @1.1 | lane 0.3 m across @1.1 | contact 0.6 m upright | fan arc @5 m chord 4 m | ring band 0.6 m (front) |')
    print('|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
    for pitch in (0, 15, 30):
        c = cam(pitch)
        for d in (3, 6, 12):
            ch = 1.1
            head = size((0, ch, d - 0.5), (0, ch, d), c)
            headg = size((0, 0.02, d - 0.5), (0, 0.02, d), c)
            tail = size((0, ch, d - 1.7), (0, ch, d - 0.5), c)
            lane = size((-0.15, ch, d), (0.15, ch, d), c)
            cont = size((0, 0.8, d), (0, 1.4, d), c)
            y = proj((0, ch, d), c)[0][1] / H
            fan = size((-2, 0.02, math.sqrt(21)), (2, 0.02, math.sqrt(21)), c) if d == 6 else float('nan')
            ring = size((0, 0.02, 1.7), (0, 0.02, 2.3), c) if d == 3 else float('nan')
            print('| %d | %d | %.3f | %.0f | %.0f | %.0f | %.0f | %.0f | %s | %s |' % (pitch, d, y, head, headg, tail, lane, cont,
                  ('%.0f' % fan) if fan == fan else '', ('%.0f' % ring) if ring == ring else ''))


if __name__ == '__main__':
    main()
