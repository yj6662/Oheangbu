"""#297 cave relocation search: rigid transform of the V4 mine so it sits under the #295/#296 terrain.

Envelope = highest interior vertex per 3m cell (Natural_Cave_Interior). Conditions: cover >= COVER over the whole
envelope, outer mouth floor 1m below terrain, 40m apron outward not climbing and dry, and a clear sight line
from the mouth (eye 1.65m) to the Geumpyo inn porch lantern. Writes Finish297/Cave/relocation.json.
"""
import json, math
from pathlib import Path
import numpy as np
ROOT = Path(__file__).resolve().parents[2]
SURF = ROOT / 'Oheangbu/Assets/_Project/Art/World/Watershed295/Surface'
OUT = ROOT / 'Art/World/Compact/Rebuild/Finish297/Cave'
h = np.fromfile(SURF / 'height.bytes', '<f4').reshape(1501, 1001).astype(np.float64)
w = np.fromfile(SURF / 'waterlevel.bytes', '<f4').reshape(1501, 1001).astype(np.float64)
COVER = 8.0; FLOOR = 135.8                              # geometry.json frame (scene mine root adds -11.2m)
INNER = np.array([3435., 1857.]); OUTER = np.array([3410., 1880.]); PIVOT = np.array([3495., 1810.])
START = np.array([3551., 1778.]); LANTERN = np.array([3070.7, 131.0, 2337.1]); OVERLOOK = np.array([3390., 1990.]); RIDGE = np.array([3300., 2228.])


def S(a, x, z):
    x = np.asarray(x, float); z = np.asarray(z, float)
    gx = np.clip(x / 4, 0, 999.999); gz = np.clip(z / 4, 0, 1499.999); i = np.floor(gx).astype(int); j = np.floor(gz).astype(int)
    fx = gx - i; fz = gz - j
    return a[j, i] * (1 - fx) * (1 - fz) + a[j, i + 1] * fx * (1 - fz) + a[j + 1, i] * (1 - fx) * fz + a[j + 1, i + 1] * fx * fz


def envelope():
    d = json.loads((ROOT / 'Art/World/Compact/Rebuild/CaveV4/geometry.json').read_text(encoding='utf-8'))
    o = np.array([d['origin']['x'], d['origin']['y'], d['origin']['z']])
    m = [x for x in d['meshes'] if x['name'] == 'Natural_Cave_Interior'][0]
    v = np.array([[p['x'], p['y'], p['z']] for p in m['vertices']]) + o
    cells = {}
    for x, y, z in v:
        k = (int(x // 3), int(z // 3)); cells[k] = max(cells.get(k, -1e9), y)
    return np.array([[k[0] * 3 + 1.5, k[1] * 3 + 1.5, y] for k, y in cells.items()])


def rotator(yaw):
    c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    return lambda xz: np.stack([PIVOT[0] + c * (xz[..., 0] - PIVOT[0]) - s * (xz[..., 1] - PIVOT[1]),
                                PIVOT[1] + s * (xz[..., 0] - PIVOT[0]) + c * (xz[..., 1] - PIVOT[1])], -1)


def evaluate(env, yaw, dx, dz):
    R = rotator(yaw); off = np.array([dx, dz]); e = R(env[:, [0, 1]]) + off; mo = R(OUTER) + off; mi = R(INNER) + off
    if e.min() < 60 or e[:, 0].max() > 3940 or e[:, 1].max() > 5940: return None
    tm = float(S(h, *mo)); dy = tm - 1.0 - FLOOR
    cover = S(h, e[:, 0], e[:, 1]) - (env[:, 2] + dy)
    if cover.min() < COVER: return None
    u = (mo - mi) / np.linalg.norm(mo - mi)
    prof = [float(S(h, *(mo + u * t))) - (FLOOR + dy) for t in (8, 16, 24, 32, 40)]
    if max(prof) > 1.5: return None
    if max(float(S(w, *(mo + u * t))) - (FLOOR + dy) for t in (0, 8, 16, 24, 32, 40)) > -0.5: return None
    eye = np.array([mo[0], FLOOR + dy + 1.65, mo[1]]) + np.array([u[0], 0, u[1]]) * 6
    n = int(np.linalg.norm(LANTERN[[0, 2]] - eye[[0, 2]]) / 4)
    t = np.linspace(0.02, 0.98, n); p = eye[None, :] + (LANTERN - eye)[None, :] * t[:, None]
    clear = float(np.min(p[:, 1] - S(h, p[:, 0], p[:, 2])))
    st = R(START) + off
    return dict(yaw=yaw, dx=dx, dz=dz, dy=round(dy, 2), mouth=[round(float(mo[0]), 2), round(FLOOR + dy, 2), round(float(mo[1]), 2)],
                mouthTerrain=round(tm, 2), coverMin=round(float(cover.min()), 2), coverP10=round(float(np.percentile(cover, 10)), 2),
                apron=[round(v, 2) for v in prof], lanternClearance=round(clear, 2), overlook=round(float(np.linalg.norm(mo - OVERLOOK)), 1),
                start=[round(float(st[0]), 2), round(FLOOR + dy + .12, 2), round(float(st[1]), 2)], outward=[round(float(u[0]), 3), round(float(u[1]), 3)])


if __name__ == '__main__':
    env = envelope(); found = []
    for yaw in np.arange(225, 285.1, 1.0):
        for dx in range(-260, -150, 2):
            for dz in range(60, 200, 2):
                r = evaluate(env, float(yaw), dx, dz)
                if r: found.append(r)
    # The inn lantern is hidden behind the ridge from any mouth near the overlook; it first shows on the existing
    # path at the crest (RIDGE). Prefer: mouth near the overlook, facing the path to the crest, thick cover, flat apron.
    for r in found:
        m = np.array([r['mouth'][0], r['mouth'][2]]); v = RIDGE - m; v /= np.linalg.norm(v)
        r['facesRidge'] = round(float(np.dot(v, r['outward'])), 3)
    found = [r for r in found if r['overlook'] < 40 and r['facesRidge'] > 0.5]
    found.sort(key=lambda r: (-min(r['coverMin'], 12), -r['facesRidge'], r['overlook'], max(r['apron'])))
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'relocation-search.json').write_text(json.dumps(dict(count=len(found), top=found[:40]), indent=1), encoding='utf-8')
    print(len(found))
    for r in found[:12]: print(r)
