"""#297 opening mine (폐광) interior dressing, generated from the scene's V4 cave mesh.

Coordinates are the geometry frame = local space of the scene `mine` root (children move with the #297 relocation).
Walls come from the scene export (`finish297 export-mesh:mine/Playtest_NaturalCave/Natural_Cave_Interior:mine` and the
#297 Portal_Gallery), not from the older CaveV4/geometry.json mesh; the main path continues through the new gallery to the
portal (Cave/Portal/unity.json). Ore seams (광맥, the prologue's colour guide, DECISIONS #100/#152) are written as specs and
fitted to the walls in Unity by ray (CompactFinish297.CaveDressing.cs).
Measured galleries: main ~9-10 m wide / 6.6-7.3 m high, branches ~7 m / 5.5-6 m. So:
  * branch galleries: full 갱목 sets (two posts + cap beam) every ~5.5 m, fitted .35 m inside the walls;
  * main galleries: wall-side props (post + angled brace + short cap under the rock) alternating sides every ~7 m;
  * the haul track (궤도): two rails + sleepers along the main path from the start toward the mouth (the old cart sits on it).
Outputs Finish297/Cave/Dressing/Meshes/*.json and dressing.json (unity manifest: parent = mine, local coordinates).
"""
import json, math, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from hanok297 import MB, beam

ROOT = Path(__file__).resolve().parents[2]
GEO = ROOT / 'Art/World/Compact/Rebuild/CaveV4/geometry.json'
EXPORT = ROOT / 'Art/World/Compact/Rebuild/Finish297/Export'
PORTAL = ROOT / 'Art/World/Compact/Rebuild/Finish297/Cave/Portal/unity.json'
MINE_T = (1483.0, 32.473, 5429.0)       # scene `mine` root after the #297 move (yaw 90): world.x = local.z + Tx, world.z = Tz - local.x
OUT = ROOT / 'Art/World/Compact/Rebuild/Finish297/Cave/Dressing'
FLOOR = 135.8


def to_local(w):
    return np.array([MINE_T[2] - w[2], w[1] - MINE_T[1], w[0] - MINE_T[0]])


def load():
    d = json.loads(GEO.read_text(encoding='utf-8'))
    V = np.concatenate([np.array(json.loads((EXPORT / f'{n}.json').read_text(encoding='utf-8'))['v']).reshape(-1, 3)
                        for n in ('Natural_Cave_Interior', 'Portal_Gallery')])
    pts = lambda k: np.array([[p['x'], p['y'], p['z']] for p in d[k]])
    u = json.loads(PORTAL.read_text(encoding='utf-8')); A = np.array(u['axisA']); D = np.array(u['axisD'])
    ext = [to_local([A[0] + D[0] * s, u['floor'], A[1] + D[1] * s]) for s in np.arange(u['clipS'], u['faceS'] + 2.61, 2.0)]
    return V, pts('main'), pts('branch'), pts('recess'), np.array(ext), u


def section(V, p, t):
    t = t / max(np.linalg.norm(t[[0, 2]]), 1e-6); n = np.array([t[2], 0, -t[0]]); rel = V - p
    along = rel @ np.array([t[0], 0, t[2]]); lat = rel @ n; dy = V[:, 1] - p[1]
    band = (np.abs(along) < .7) & (dy > .8) & (dy < 2.6) & (np.abs(lat) < 12)
    L = lat[band & (lat < 0)]; R = lat[band & (lat > 0)]; top = (np.abs(along) < 1.0) & (np.abs(lat) < 1.2) & (dy > 2.0)
    return (float(-L.max()) if len(L) else None, float(R.min()) if len(R) else None, float(dy[top].min()) if top.any() else None, n)


def ceiling_at(V, p, t, lateral, half=.45):
    """Rock height above the floor at a lateral offset (the arch comes down toward the walls)."""
    t = t / max(np.linalg.norm(t[[0, 2]]), 1e-6); n = np.array([t[2], 0, -t[0]]); rel = V - p
    along = rel @ np.array([t[0], 0, t[2]]); lat = rel @ n; dy = V[:, 1] - p[1]
    sel = (np.abs(along) < .8) & (np.abs(lat - lateral) < half) & (dy > 1.8)
    return float(dy[sel].min()) if sel.any() else None


def samples(path, step):
    out = []
    for a, b in zip(path[:-1], path[1:]):
        L = np.linalg.norm(b - a); k = max(1, int(L / step))
        for i in range(k): out.append((a + (b - a) * (i + .5) / k, b - a))
    return out


def timber_set(mb, col, p, n, t, hl, hr, ceil, lod, full=True, side=1, V=None):
    """Square set: posts .28 m, cap .32 m under the rock at the post positions. full=False -> one wall prop (post +
    brace + short cap) on `side`, its post reaching the rock near the wall (props taller than 6 m are skipped)."""
    yb = FLOOR + .02
    if full:
        la, lb = -hl + .35, hr - .35
        ca = ceiling_at(V, p, t, la) if V is not None else ceil; cb = ceiling_at(V, p, t, lb) if V is not None else ceil
        rock = min(x for x in (ca, cb, ceil) if x is not None)
        top = FLOOR + min(rock - .55, 5.6)
        a = p + n * la; b = p + n * lb
        for q in (a, b):
            beam(mb, [q[0], yb, q[2]], [q[0], top, q[2]], .28, .28, 'wood_dark')
            col.box([q[0], (yb + top) / 2, q[2]], [.3, top - yb, .3], 'c', faces='xXzZ')
        beam(mb, [a[0], top + .16, a[2]], [b[0], top + .16, b[2]], .34, .32, 'wood_dark')
        if lod == 0:   # lagging boards above the cap (hold loose rock)
            for k in range(-2, 3):
                off = np.array([t[0], 0, t[2]]) / np.linalg.norm(t[[0, 2]]) * k * .22
                beam(mb, [a[0] + off[0], top + .36, a[2] + off[2]], [b[0] + off[0], top + .36, b[2] + off[2]], .18, .06, 'wood_board')
        return True
    w = (hr if side > 0 else hl) - .3; rock = ceiling_at(V, p, t, side * w) if V is not None else ceil
    if rock is None or rock > 6.3: return False
    top = FLOOR + rock - .38; q = p + n * side * w; r = p + n * side * (w - 1.6)
    beam(mb, [q[0], yb, q[2]], [q[0], top, q[2]], .28, .28, 'wood_dark')
    beam(mb, [r[0], yb, r[2]], [q[0], FLOOR + min(2.6, rock - 1.2), q[2]], .2, .2, 'wood_dark')
    inner = p + n * side * (w - 1.7); rock_in = ceiling_at(V, p, t, side * (w - 1.7)) if V is not None else rock
    cap_y = min(top + .14, FLOOR + (rock_in or rock) - .2)
    beam(mb, [q[0], cap_y, q[2]], [inner[0], cap_y, inner[2]], .3, .28, 'wood_dark')
    col.box([q[0], (yb + top) / 2, q[2]], [.3, top - yb, .3], 'c', faces='xXzZ')
    return True


def rubble(mb, V, path, rng, step=8.5):
    """Loose rock at the wall feet (spalled from the walls), clusters every ~8.5 m, alternating sides."""
    from hanok297 import nz as _nz
    tt = (1 + 5 ** .5) / 2
    ico = _nz(np.array([(-1, tt, 0), (1, tt, 0), (-1, -tt, 0), (1, -tt, 0), (0, -1, tt), (0, 1, tt), (0, -1, -tt), (0, 1, -tt), (tt, 0, -1), (tt, 0, 1), (-tt, 0, -1), (-tt, 0, 1)], float))
    F = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
         (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]
    side = 1; count = 0
    for q, tv in samples(path, step):
        hl, hr, ceil, n = section(V, q, tv)
        if hl is None or hr is None: continue
        w = (hr if side > 0 else hl) - .35
        for k in range(int(rng.integers(3, 7))):
            c = q + n * side * (w - abs(rng.normal(0, .35))) + np.array([tv[0], 0, tv[2]]) / max(np.linalg.norm(tv[[0, 2]]), 1e-6) * rng.normal(0, .8)
            size = float(np.clip(rng.lognormal(-1.2, .45), .12, .62))
            Vr = ico * size * np.array([1, .6, 1]) * (1 + .2 * rng.standard_normal((12, 1))) + np.array([c[0], FLOOR - .22 * size, c[2]])
            b = mb._add(Vr, _nz(Vr - Vr.mean(0)), Vr[:, [0, 2]] * .5)
            for f in F:
                i, j, l = b + f[0], b + f[1], b + f[2]
                if np.dot(np.cross(Vr[f[1]] - Vr[f[0]], Vr[f[2]] - Vr[f[0]]), Vr[f[0]] - Vr.mean(0)) < 0: j, l = l, j
                mb.T['rock_loose'] += [i, j, l]
            count += 1
        side = -side
    return count


def track(mb, path, lod, gauge=.9):
    """Two iron rails on timber sleepers along a polyline (floor level)."""
    P = []
    for a, b in zip(path[:-1], path[1:]):
        L = np.linalg.norm(b - a); k = max(1, int(L / .8))
        for i in range(k): P.append(a + (b - a) * i / k)
    P.append(path[-1]); P = np.array(P); P[:, 1] = FLOOR + .05
    d = np.gradient(P, axis=0); d /= np.maximum(np.linalg.norm(d[:, [0, 2]], axis=1, keepdims=True), 1e-6)
    n = np.stack([d[:, 2], np.zeros(len(d)), -d[:, 0]], -1)
    for s in (-1, 1):
        R = P + n * s * gauge / 2 + [0, .14, 0]
        for a, b in zip(R[:-1:2], R[2::2]): beam(mb, a, b, .07, .09, 'iron')
    if lod < 2:
        for i in range(0, len(P), 1 if lod == 0 else 3):
            c = P[i]; yaw = math.degrees(math.atan2(d[i, 0], d[i, 2]))
            mb.box([c[0], c[1] + .04, c[2]], [gauge + .6, .1, .22], 'wood_board', yaw=yaw + 90, faces='xXzZY', uvscale=1.0)


def ore_specs(main_p):
    """At every turn of the main path a seam on the wall straight ahead of the incoming gallery (visible down its length),
    one on a long straight, and a last one before the portal (the hand-over to the daylight and the inn lantern)."""
    out = []
    for i in range(1, len(main_p) - 1):
        a, b, c = main_p[i - 1], main_p[i], main_p[i + 1]
        din = (b - a)[[0, 2]]; din /= np.linalg.norm(din); dout = (c - b)[[0, 2]]; dout /= np.linalg.norm(dout)
        if float(din @ dout) > .95: continue                                     # not a turn
        out.append(dict(origin=[float(b[0]), FLOOR + 1.9, float(b[2])], dir=[float(din[0]), float(din[1])], length=3.8, tilt=10.0 if i % 2 else -8.0))
    return out


def main():
    V, main_p, branch_p, recess_p, ext, portal = load(); (OUT / 'Meshes').mkdir(parents=True, exist_ok=True)
    track_path = np.vstack([main_p, ext[1:]])
    gallery_timbers = ext[:max(1, len(ext) - 5)]                                  # the portal already has its last 3 sets
    manifest = dict(parent='mine', meshes=[], note=__doc__.strip().splitlines()[0])
    for lod in (0, 1, 2):
        tim = MB('CaveTimbers'); col = MB('CaveTimbers_collision'); rails = MB('CaveTrack'); side = 1; sets = 0
        for path, step, full in ((branch_p, 5.5, True), (recess_p, 5.0, True), (main_p, 7.0, False), (gallery_timbers, 6.0, False)):
            for p, t in samples(path, step):
                hl, hr, ceil, n = section(V, p, t)
                if hl is None or hr is None or ceil is None or ceil < 3.6: continue
                use_full = full and hl + hr < 8.6
                if timber_set(tim, col, p, n, t, hl, hr, ceil, lod, full=use_full, side=side, V=V): sets += 1
                side = -side
        track(rails, track_path, lod)
        rub = MB('CaveRubble'); rocks = rubble(rub, V, main_p, np.random.default_rng(2970)) if lod < 2 else 0
        if lod < 2: rub.export(OUT / 'Meshes' / f'CaveRubble_LOD{lod}.json')
        tim.export(OUT / 'Meshes' / f'CaveTimbers_LOD{lod}.json'); rails.export(OUT / 'Meshes' / f'CaveTrack_LOD{lod}.json')
        if lod == 0:
            col.export(OUT / 'Meshes' / 'CaveTimbers_Collision.json')
            manifest['stats'] = dict(timberSets=sets, timberTris=tim.tris(), trackTris=rails.tris(), rubbleRocks=rocks, rubbleTris=rub.tris())
    manifest['meshes'] = [dict(name='CaveTimbers', kind='dressing', world=False, position=[0, 0, 0], yaw=0.0),
                          dict(name='CaveTrack', kind='dressing', world=False, position=[0, 0, 0], yaw=0.0)]
    ore = ore_specs(main_p)
    # long last straight + the new gallery: one seam mid-way on the right, one 11 m before the portal on the left
    g0, g1 = ext[0], ext[-1]; gd = (g1 - g0)[[0, 2]]; gd /= np.linalg.norm(gd); right = np.array([gd[1], -gd[0]])
    for f, side in ((.35, 1), (.62, -1)):
        q = g0 + (g1 - g0) * f; ore.append(dict(origin=[float(q[0]), FLOOR + 1.8, float(q[2])], dir=[float(right[0] * side), float(right[1] * side)], length=3.6, tilt=6.0 * side))
    manifest['ore'] = ore
    # daylight spill just inside the portal (world, from the portal frame): brightens the last metres before the exit
    A = np.array(portal['axisA']); D = np.array(portal['axisD'])
    spill = lambda s, h: to_local([A[0] + D[0] * s, portal['floor'] + h, A[1] + D[1] * s]).round(3).tolist()
    manifest['lights'] = [dict(name='Portal_Daylight_Spill', local=spill(portal['faceS'] - 2.0, 3.2), color=[1.0, .95, .88], intensity=1.8, range=15.0),
                          dict(name='Portal_Daylight_Deep', local=spill(portal['faceS'] - 10.0, 3.0), color=[.95, .93, .88], intensity=.7, range=11.0)]
    (OUT / 'dressing.json').write_text(json.dumps(manifest, indent=1), encoding='utf-8')
    print(json.dumps(manifest['stats']), 'ore', len(manifest['ore']), 'track points', len(track_path))


if __name__ == '__main__':
    main()
