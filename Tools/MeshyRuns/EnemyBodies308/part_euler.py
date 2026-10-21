"""D308-120 part placement helper: body-space wishes -> a Creatures308 parts[] pos / euler in bone space.

Creatures308 parts:probe:<key> prints each part bone's local axes in body space (x right, y up, z forward), e.g.
  Spine4 idle: x(0.00,-1.00,0.00) y(0.00,0.00,1.00) z(-1.00,0.00,0.00) ...
A part's own +Y (its "up" as drawn on the Meshy sheet) should point along `up` (body space) and its +Z along `fwd`;
`off` is the part's offset from the bone in body space (metres). Prints pos and euler (Unity Quaternion.Euler order Z, X, Y).

  python part_euler.py "x(..) y(..) z(..)" up=0,1,0 fwd=0,0,1 off=0,0.1,0
"""
import math
import re
import sys


def vec(text):
    return [float(v) for v in text.split(',')]


def norm(v):
    n = math.sqrt(sum(c * c for c in v)) or 1.0
    return [c / n for c in v]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def euler_from_matrix(m):
    # m columns = part x, y, z in bone space; Unity R = Ry * Rx * Rz
    # m[1][2] = -sin(x)
    sx = -m[1][2]
    sx = max(-1.0, min(1.0, sx))
    x = math.asin(sx)
    if abs(math.cos(x)) > 1e-6:
        y = math.atan2(m[0][2], m[2][2])
        z = math.atan2(m[1][0], m[1][1])
    else:
        y = math.atan2(-m[2][0], m[0][0])
        z = 0.0
    return [round(math.degrees(a) % 360, 1) for a in (x, y, z)]


def main():
    axes = dict(re.findall(r'([xyz])\(([-0-9., ]+)\)', sys.argv[1]))
    bx, by, bz = (norm(vec(axes[k])) for k in 'xyz')
    opts = dict(a.split('=', 1) for a in sys.argv[2:])
    up = norm(vec(opts.get('up', '0,1,0')))
    fwd = vec(opts.get('fwd', '0,0,1'))
    if opts.get('keep') == 'fwd':   # a ring around a limb: its axis (+Z) is exact, +Y only leans toward `up`
        fwd = norm(fwd)
        up = norm([u - dot(up, fwd) * f for u, f in zip(up, fwd)])
    else:
        fwd = norm([f - dot(fwd, up) * u for f, u in zip(fwd, up)])
    right = cross(up, fwd)
    to_bone = lambda d: [dot(d, bx), dot(d, by), dot(d, bz)]
    cx, cy, cz = to_bone(right), to_bone(up), to_bone(fwd)
    m = [[cx[i], cy[i], cz[i]] for i in range(3)]
    off = vec(opts.get('off', '0,0,0'))
    pos = [round(v, 3) for v in to_bone(off)]
    print('pos', pos, 'euler', euler_from_matrix(m))


if __name__ == '__main__':
    main()
