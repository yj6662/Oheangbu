"""#297 봉수대 (산정 마석 봉수) — one kit mesh for every realm summit beacon (SPEC-WORLD-FINISH-297 attraction, TEST).

No usable model exists: LM_Bongsudang (WorldMacro/Landmarks) is the Hwaseong 봉수당 palace hall, not a 봉수대.
Local frame: origin = ground at the platform centre, +z = front (stair + 아궁이, turned toward the approach in Unity), y up.
  연대: battered square stone platform (FOOT m at the buried foot, TOP_W m at the top, PLAT_H m above the origin, a skirt buried
        BURY m so it seats on a sloped summit) under a dressed coping slab, with a short front stair (walkable);
  연조: round stone chimney on the platform with a stone-framed arched fire mouth (아궁이, glowing = slot beacon_core) and an
        open lip holding a shallow fire basin;
  core: the 마석 fire body in the basin (separate mesh BeaconCore_*, slot beacon_core -> InkBeacon297 in Unity: no shadows).
Outputs Finish297/Attraction/Meshes/Beacon_LOD{0,1,2}.json, BeaconCore_LOD{0,1,2}.json, Beacon_Collision.json and
Finish297/Attraction/beacon.json (core anchor for the halo/light). Materials: stone_rough, stone_plain, beacon_core.
"""
import json, math, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from hanok297 import MB

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Finish297/Attraction'

FOOT, TOP_W, BURY, PLAT_H = 4.2, 3.7, 1.2, 1.1      # platform foot width (at -BURY), top width, buried skirt, height above origin
COPING, COPE_OVER = .16, .10                       # coping slab thickness / overhang
DECK = PLAT_H + COPING                             # walkable platform top
RISE, RUN, STAIR_W = DECK / 6, .30, 1.3            # 5 steps + the deck edge = 6 equal risers (.21 m)
CH_R0, CH_R1, CH_H = .82, .68, 1.8                 # chimney radius at the base / lip, height above the deck
LIP = DECK + CH_H
BASIN_R, BASIN_D = .50, .30                        # open top: inner radius, basin depth below the lip
CORE_R, CORE_H = .52, 1.25                         # 마석 fire body
CORE_BASE = LIP - BASIN_D + .02


def half_width(y):
    """Battered platform face: half width at height y (foot at -BURY, top at PLAT_H)."""
    return FOOT / 2 + (TOP_W / 2 - FOOT / 2) * (y + BURY) / (PLAT_H + BURY)


def platform(vis, lod):
    y0, y1 = -BURY, PLAT_H; a, b = half_width(y0), half_width(y1)
    for sx, sz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        out = np.array([sx, 0., sz])
        if sx:
            q = [[sx * a, y0, -a], [sx * a, y0, a], [sx * b, y1, b], [sx * b, y1, -b]]
        else:
            q = [[-a, y0, sz * a], [a, y0, sz * a], [b, y1, sz * b], [-b, y1, sz * b]]
        vis.poly(q, 'stone_rough', out + [0, .08, 0], uvscale=2.0)
    c = TOP_W + 2 * COPE_OVER
    vis.box([0, PLAT_H + COPING / 2, 0], [c, COPING, c], 'stone_plain', faces='xXzZY' if lod < 2 else 'Y', uvscale=1.2)


def stair(vis, col, lod):
    """Front stair: 5 solid step blocks against the platform face (each from below ground to its tread)."""
    front = TOP_W / 2 + COPE_OVER
    for k in range(1, 6):
        top = k * RISE; z1 = front + (6 - k) * RUN; z0 = front - .35
        c = [0, (top - .35) / 2, (z0 + z1) / 2]; s = [STAIR_W, top + .35, z1 - z0]
        if lod < 2: vis.box(c, s, 'stone_plain', faces='xXZY', uvscale=1.0)
        if col is not None: col.box(c, s, 'c', faces='xXzZyY')


def ring_grid(vis, mat, y0, y1, r0, r1, sides, inward=False, uvs=1.2):
    """Open cylinder band; normals radial (outward, or inward for the basin wall)."""
    ang = np.linspace(0, 2 * math.pi, sides + 1)
    P = np.stack([np.stack([r0 * np.sin(ang), np.full_like(ang, y0), r0 * np.cos(ang)], -1),
                  np.stack([r1 * np.sin(ang), np.full_like(ang, y1), r1 * np.cos(ang)], -1)], 1)
    L = abs(y1 - y0); uv = np.stack(np.meshgrid(ang * max(r0, r1) / uvs, [0, L / uvs], indexing='ij'), -1)
    sign = -1 if inward else 1
    vis.grid(P, mat, uv, lambda Q: sign * np.concatenate([Q[..., :1], np.zeros_like(Q[..., :1]), Q[..., 2:3]], -1))


def annulus(vis, mat, y, r_in, r_out, sides, up=True):
    ang = np.linspace(0, 2 * math.pi, sides + 1)
    P = np.stack([np.stack([r_in * np.sin(ang), np.full_like(ang, y), r_in * np.cos(ang)], -1),
                  np.stack([r_out * np.sin(ang), np.full_like(ang, y), r_out * np.cos(ang)], -1)], 1)
    vis.grid(P, mat, P[..., [0, 2]] / 1.0, np.array([0., 1. if up else -1., 0.]))


def chimney(vis, lod):
    sides = [16, 10, 6][lod]
    ring_grid(vis, 'stone_rough', DECK - .02, LIP - .12, CH_R0, CH_R1, sides, uvs=1.6)   # 연조 body
    if lod < 2:
        ring_grid(vis, 'stone_plain', DECK - .02, DECK + .16, CH_R0 + .09, CH_R0 + .07, sides)   # 굽돌 plinth band
        annulus(vis, 'stone_plain', DECK + .16, CH_R0 - .02, CH_R0 + .07, sides)
    ring_grid(vis, 'stone_plain', LIP - .14, LIP, CH_R1 + .06, CH_R1 + .06, sides)          # lip band
    annulus(vis, 'stone_plain', LIP - .14, CH_R1 - .02, CH_R1 + .06, sides, up=False)
    annulus(vis, 'stone_plain', LIP, BASIN_R, CH_R1 + .06, sides)                           # lip top
    ring_grid(vis, 'stone_plain', LIP - BASIN_D, LIP, BASIN_R, BASIN_R, sides, inward=True)  # basin wall
    ang = np.linspace(0, 2 * math.pi, sides + 1)[:-1]
    vis.poly([[BASIN_R * math.sin(a), LIP - BASIN_D, BASIN_R * math.cos(a)] for a in ang], 'stone_plain', [0, 1, 0])
    if lod < 2:   # 아궁이: stone surround on the front with the fire glowing through an arched mouth
        z0, z1, w, h = CH_R0 - .25, CH_R0 + .14, .78, .82
        vis.box([0, DECK + h / 2, (z0 + z1) / 2], [w, h, z1 - z0], 'stone_plain', faces='xXZY', uvscale=1.0)
        arch_w, spring, top = .40, DECK + .38, DECK + .60
        n = [8, 5][lod]; pts = [[-arch_w / 2, DECK + .06, z1 + .005], [arch_w / 2, DECK + .06, z1 + .005]]
        for i in range(n + 1):
            t = i / n * math.pi
            pts.append([arch_w / 2 * math.cos(t), spring + (top - spring) * math.sin(t), z1 + .005])
        vis.poly(pts, 'beacon_core', [0, 0, 1])


def core(lod):
    """마석 fire body: a lumpy flame (bulb low, soft tip) standing in the basin."""
    mb = MB('beacon_core'); nu, nv = [(16, 10), (10, 6), (6, 4)][lod]
    th = np.linspace(0, 2 * math.pi, nu + 1); v = np.linspace(0, 1, nv + 1)
    T, Vv = np.meshgrid(th, v, indexing='ij')
    prof = np.sin(math.pi * np.clip(.14 + Vv * .86, 0, 1)) ** .7 * (1 - .3 * Vv)
    lumps = 1 + .12 * np.sin(3 * T + 2.1 * Vv) + .07 * np.sin(5 * T + 1.3 + 4.2 * Vv)
    R = np.maximum(CORE_R * prof * lumps, .03)
    lean = .06 * Vv ** 2                                          # a slight lean, so it is not a turned solid
    P = np.stack([R * np.sin(T) + lean, CORE_BASE + CORE_H * Vv, R * np.cos(T)], -1)
    uv = np.stack([T / (2 * math.pi) * 2, Vv * 2], -1)
    centre = np.array([0., CORE_BASE + CORE_H * .35, 0.])
    mb.grid(P, 'beacon_core', uv, lambda Q: Q - centre)
    return mb


def build(lod):
    vis = MB('Beacon'); col = MB('Beacon_collision') if lod == 0 else None
    platform(vis, lod); stair(vis, col, lod); chimney(vis, lod)
    if col is not None:
        c = TOP_W + 2 * COPE_OVER
        col.box([0, (DECK - BURY) / 2, 0], [c, DECK + BURY, c], 'c', faces='xXzZyY')
        col.cyl([0, DECK - .05, 0], [0, LIP, 0], CH_R0, CH_R1 + .06, 8, 'c', caps=True)
    return vis, col


def main():
    (OUT / 'Meshes').mkdir(parents=True, exist_ok=True)
    for f in (OUT / 'Meshes').glob('Beacon*.json'): f.unlink()
    stats = {}
    for lod in (0, 1, 2):
        vis, col = build(lod); vis.export(OUT / 'Meshes' / f'Beacon_LOD{lod}.json')
        cm = core(lod); cm.export(OUT / 'Meshes' / f'BeaconCore_LOD{lod}.json')
        if col is not None: col.export(OUT / 'Meshes' / 'Beacon_Collision.json'); stats['collision_tris'] = col.tris()
        stats[f'LOD{lod}'] = dict(tris=vis.tris(), core_tris=cm.tris(), slots=sorted(k for k, t in vis.T.items() if t))
    anchor = dict(core=[0.0, round(CORE_BASE + CORE_H * .45, 3), 0.0], top=round(CORE_BASE + CORE_H, 3), platformTop=round(DECK, 3),
                  footprint=FOOT, stairFront=round(TOP_W / 2 + COPE_OVER + 5 * RUN, 3), lip=round(LIP, 3))
    (OUT / 'beacon.json').write_text(json.dumps(anchor, indent=1), encoding='utf-8')
    print(json.dumps(dict(anchor=anchor, **stats), indent=1))


if __name__ == '__main__':
    main()
