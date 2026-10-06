# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 AC-O7 / #214 - an INDEPENDENT model of the crisp walked edge (review of the finalize pass, 2026-10-04).

    python Tools/resource_guard.py --wait
    python Tools/Art/map308_edgecheck.py [--bundle <dir>] [--cginc <MapFog308.cginc>] [--out <dir>]
                                                               -> Art/UI308/Map/edgecheck308.json, exit 1 when anything leaks

Written from the HLSL of MapFog308.cginc (MapFog308_Edge) and of the _MAP308 branch of PaperMapSurface.shader, not from
map308_twin.py: the screen derivatives are taken the way a GPU takes them inside 2 x 2 pixel quads (fine and coarse), next to
the central differences the twin uses. It counts pixels that lie on an UNWALKED cell (looked up from the walk grid, not from
the edge's own value) and still get a walked value or a rim, over nine walk patterns (random, pin holes, checkerboards,
stripes, a staircase, 140 reveal discs, the twin's mock walk) and nine scales from 0.27 to 11.8 m / px, north up and turned.

MapFog308_Edge exists in two formulas; the tool models the one the cginc carries (--cginc, default = the LIVE file
Oheangbu/Assets/_Project/Art/UI/UI308/Map/Shaders/MapFog308.cginc, read only), told apart by `MAPFOG308_E_FLOOR` in its text:
  threshold  field = cover - (p.x + p.y x two noise octaves). The thresholds come from the bundle's notation
             (values.fogEdge.crispEdge). The edge wobbles 4.5 .. 18 m inside the walked cells.
  F          #308 map fix 2: field = min(depth, KCOVER x cover) - theta (B-spline depth of the same nine cells, two phase-pushed
             sine waves, theta never under FLOOR screen px). Every number is a `#define MAPFOG308_E_*` line of the cginc, read
             from the file, never retyped here; the notation's crispEdge is not used. The edge lies 3.8 .. 19.8 m inside the
             walked cells behind a straight border (by a corner of the unwalked land the clamp alone holds it: cover >=
             FROM / KCOVER, 2 .. 2.6 m). The run also fails when the constants break the #214 rules FROM > 0, FROM + SPAN < 1,
             FLOOR >= .809 x KCOVER, because the pixel count alone does not see them: with FLOOR 1.2, and with FROM + SPAN
             1.05, it stays at 0 px (measured 2026-10-05: while FROM is .12 the floor only acts on views coarser than about
             3 m / px, and those are north up here). With the wavy threshold taken away the floor alone holds the edge, and
             then FLOOR 1.2135 gives 0 px while FLOOR 1.2 leaks at turned lattice corners (Tools/Unity/Stage308_mapfix2:
             TOOLS_PATCH.md 3, README 4).
--out keeps the shared report as it is (use it when --cginc is a stage file or a trial copy).

It CAN fail: with the threshold at .02 + .05 x noise the whole-world view leaks (about 12,000 px in the control run), and a
field of cell CENTRES leaks at every scale.
map308_twin.py's own gate (walked_edge) reads six minimap windows at 1080p with central differences only; its fog_214 block
defines "unwalked" by the composite's own walked value, so that block cannot fail. Run this after any change to the edge
function or to its thresholds. About 3 minutes; numpy only; nothing under Oheangbu/ is written (the live cginc is read).
"""
import json, math, re, sys, time
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
FOG_W, FOG_H, CELL = 125, 188, 32.0
P_EDGE = (np.float32(.14), np.float32(.42), np.float32(1.6), np.float32(3.7))      # replaced by the bundle's notation in main()
RIM_PX = 2.0
f32 = np.float32
sys.path.insert(0, str(Path(__file__).resolve().parent))
import map308_edge3 as E3       # #308 map 3: the formula of a cginc that has `#define MAPFOG308_E_GATE0`
LIVE_CGINC = ROOT / 'Oheangbu/Assets/_Project/Art/UI/UI308/Map/Shaders/MapFog308.cginc'
SHEET = None                    # #308 map 4: the bundle's values.fogEdge.sheet (+ basePx = fogEdge.px), set by main(); None = a bundle without it
F_NAMES = ('FROM', 'SPAN', 'KCOVER', 'FLOOR', 'D1', 'D2', 'HALF1', 'HALF2', 'PHASE', 'PK', 'RK', 'RIM0', 'RIM1', 'RIMBREAK')
F_FLOOR_PER_KCOVER = .809       # #214: half of the steepest climb of KCOVER x cover across a lattice corner (1.618 x KCOVER per cell)


def edge_constants(cginc):
    """which MapFog308_Edge the cginc carries: None = the threshold formula (numbers from the bundle's notation), a dict = formula F
    with the values of its `#define MAPFOG308_E_<name>` lines ({name: float | (float, float)})."""
    src = Path(cginc).read_bytes().decode('utf-8-sig')
    if 'MAPFOG308_E_FLOOR' not in src: return None
    q = {}
    for m in re.finditer(r'^[ \t]*#define[ \t]+MAPFOG308_E_(\w+)[ \t]+(float2\(([^)]*)\)|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)', src, re.M):
        q[m.group(1)] = tuple(float(v) for v in m.group(3).split(',')) if m.group(3) else float(m.group(2))
    missing = [k for k in F_NAMES if k not in q] + (E3.missing3(q) if E3.is_map3(q) else [])
    if missing: raise SystemExit('[map308_edgecheck] %s carries formula F but no #define for MAPFOG308_E_%s' % (cginc, ', MAPFOG308_E_'.join(missing)))
    return q


def constant_rules(q):
    """the #214 rules on formula F's constants -> [(text, ok)]"""
    if E3.is_map3(q): return E3.constant_rules3(q)
    need = F_FLOOR_PER_KCOVER * q['KCOVER']
    return [('FROM %g > 0' % q['FROM'], q['FROM'] > 0), ('FROM + SPAN %g < 1' % (q['FROM'] + q['SPAN']), q['FROM'] + q['SPAN'] < 1),
            ('FLOOR %g >= .809 x KCOVER %g = %g' % (q['FLOOR'], q['KCOVER'], need), q['FLOOR'] >= need)]


def hash_(x, y):
    s = np.sin((x * f32(127.1) + y * f32(311.7)).astype(f32)).astype(f32) * f32(43758.5453)
    return (s - np.floor(s)).astype(f32)


def noise(x, y):
    ix = np.floor(x); iy = np.floor(y); fx = (x - ix).astype(f32); fy = (y - iy).astype(f32)
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
    a = hash_(ix, iy); b = hash_(ix + 1, iy); c = hash_(ix, iy + 1); d = hash_(ix + 1, iy + 1)
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def deriv(g, mode):
    """(ddx, ddy) of a per-pixel array the way a GPU gives them inside 2x2 quads. mode: 'fine' (per row / column of the quad),
    'coarse' (one value per quad, from its first row / column), 'central' (np.gradient: what the builder's twin uses)."""
    if mode == 'central':
        gy, gx = np.gradient(g); return gx, gy
    h, w = g.shape; h2, w2 = h - h % 2, w - w % 2
    gx = np.zeros_like(g); gy = np.zeros_like(g)
    q = g[:h2, :w2]
    dx = q[:, 1::2] - q[:, 0::2]                    # per row of each quad
    dy = q[1::2, :] - q[0::2, :]                    # per column of each quad
    if mode == 'coarse':
        dx = np.repeat(dx[0::2], 2, axis=0); dy = np.repeat(dy[:, 0::2], 2, axis=1)
    gx[:h2, :w2] = np.repeat(dx, 2, axis=1); gy[:h2, :w2] = np.repeat(dy, 2, axis=0)
    if h % 2: gx[-1] = gx[-2]; gy[-1] = gy[-2]
    if w % 2: gx[:, -1] = gx[:, -2]; gy[:, -1] = gy[:, -2]
    return gx, gy


def edge(walked, X, Z, mode='fine', p=P_EDGE, q=None):
    """MapFog308_Edge + the caller's edgePx. walked: bool [FOG_H, FOG_W] south row first. -> crisp, edgePx, gate, cellwalked
    q = None: the threshold formula with the notation's numbers p (gate = cover). q = edge_constants(cginc): formula F with the
    cginc's constants (p is not read, as in the shader; gate = the function's .y, the caller's rim gate)."""
    W = walked.astype(f32)
    cu = (X / CELL).astype(f32); cv = (Z / CELL).astype(f32)
    cj = np.floor(cu).astype(int); ci = np.floor(cv).astype(int); fx = cu - cj; fy = cv - ci
    at = lambda ii, jj: W[np.clip(ii, 0, FOG_H - 1), np.clip(jj, 0, FOG_W - 1)]            # clamp + point sampling
    w = {(a, b): at(ci + a, cj + b) for a in (-1, 0, 1) for b in (-1, 0, 1)}             # (dv, du)
    k00 = np.minimum(np.minimum(w[(-1, -1)], w[(-1, 0)]), np.minimum(w[(0, -1)], w[(0, 0)]))
    k10 = np.minimum(np.minimum(w[(-1, 0)], w[(-1, 1)]), np.minimum(w[(0, 0)], w[(0, 1)]))
    k01 = np.minimum(np.minimum(w[(0, -1)], w[(0, 0)]), np.minimum(w[(1, -1)], w[(1, 0)]))
    k11 = np.minimum(np.minimum(w[(0, 0)], w[(0, 1)]), np.minimum(w[(1, 0)], w[(1, 1)]))
    cover = (k00 * (1 - fx) + k10 * fx) * (1 - fy) + (k01 * (1 - fx) + k11 * fx) * fy
    dux, duy = deriv(cu, mode); dvx, dvy = deriv(cv, mode)
    ppc = 1.0 / np.maximum(np.hypot(np.hypot(dux, dvx), np.hypot(duy, dvy)) * .7071, 1e-6)
    if E3.is_map3(q):                                                                       # #308 map 3: bites + the continuous rim gate (map308_edge3.py)
        field, gate = E3.edge3(q, at, ci, cj, fx, fy, cu, cv, ppc, noise)
    elif q is None:
        fade1 = np.clip(ppc / max(float(p[2]), 1e-3) * .5 - .5, 0, 1); fade2 = np.clip(ppc / max(float(p[3]), 1e-3) * .5 - .5, 0, 1)
        n = (.5 + (noise(cu * p[2], cv * p[2]) - .5) * fade1) * .62 + (.5 + (noise(cu * p[3] + f32(17.3), cv * p[3] + f32(17.3)) - .5) * fade2) * .38
        field = cover - (p[0] + p[1] * n)
        gate = cover
    else:                                                                                   # formula F, statement by statement
        a0x = .5 * (1 - fx) * (1 - fx); a2x = .5 * fx * fx; a1x = 1 - a0x - a2x              # float2 a0=.5*(1-f)*(1-f); a2=.5*f*f; a1=1-a0-a2;
        a0y = .5 * (1 - fy) * (1 - fy); a2y = .5 * fy * fy; a1y = 1 - a0y - a2y
        bs = ((w[(-1, -1)] * a0x + w[(-1, 0)] * a1x + w[(-1, 1)] * a2x) * a0y                # bs=(w00*a0.x+w10*a1.x+w20*a2.x)*a0.y
              + (w[(0, -1)] * a0x + w[(0, 0)] * a1x + w[(0, 1)] * a2x) * a1y                 #   +(w01*a0.x+w11*a1.x+w21*a2.x)*a1.y
              + (w[(1, -1)] * a0x + w[(1, 0)] * a1x + w[(1, 1)] * a2x) * a2y)                #   +(w02*a0.x+w12*a1.x+w22*a2.x)*a2.y;
        depth = 1 - np.sqrt(np.maximum(2 * (1 - bs), 0))                                    # depth=1-sqrt(max(2*(1-bs),0));
        land = np.minimum(depth, q['KCOVER'] * cover)                                       # land=min(depth,KCOVER*cover);
        pk, rk = f32(q['PK']), f32(q['RK'])
        n1 = noise(cu * pk + f32(5.2), cv * pk + f32(5.2)); n2 = noise(cu * pk + f32(31.7), cv * pk + f32(31.7))   # MapFog308_Noise(c*PK+5.2), (c*PK+31.7)
        dot1 = (cu * f32(q['D1'][0]) + cv * f32(q['D1'][1]) + f32(q['PHASE']) * n1).astype(f32)   # float, as the shader: sin of ~1300 rad
        dot2 = (cu * f32(q['D2'][0]) + cv * f32(q['D2'][1]) + f32(q['PHASE']) * n2).astype(f32)
        wave1 = np.sin(dot1) * np.clip(ppc * q['HALF1'] * .5 - .5, 0, 1)                    # wave1=sin(dot(c,D1)+PHASE*n1)*saturate(pxPerCell*HALF1*.5-.5);
        wave2 = np.sin(dot2) * np.clip(ppc * q['HALF2'] * .5 - .5, 0, 1)
        theta = q['FROM'] + q['SPAN'] * (.5 + .25 * (wave1 + wave2))                        # theta=FROM+SPAN*(.5+.25*(wave1+wave2));
        theta = np.maximum(theta, q['FLOOR'] / ppc)                                         # theta=max(theta,FLOOR/pxPerCell);
        nr = .5 + (noise(cu * rk + f32(23.1), cv * rk + f32(23.1)) - .5) * np.clip(ppc / q['RK'] * .5 - .5, 0, 1)   # nr=lerp(.5,Noise(c*RK+23.1),saturate(pxPerCell/RK*.5-.5));
        t = np.clip((nr - q['RIM0']) / (q['RIM1'] - q['RIM0']), 0, 1)
        gate = np.maximum(np.clip(land, 0, 1), q['RIMBREAK'] * (1 - .08 * t * t * (3 - 2 * t)))   # gate=max(saturate(land),RIMBREAK*(1-.08*smoothstep(RIM0,RIM1,nr)));
        field = land - theta                                                                # return float2(land-theta,gate);
    gx, gy = deriv(field, mode)
    epx = field / np.maximum(np.hypot(gx, gy), 1e-5)
    crisp = np.clip(epx + .5, 0, 1)
    edge.field = field                                                                      # #308 map 4: the sheet's whole rim reads the field (run() below)
    return crisp, epx, gate, at(ci, cj) > .5


def view(cx, cz, w, h, mpp, turn_deg=0.0):
    xs = (np.arange(w) + .5 - w / 2) * mpp; zs = -(np.arange(h) + .5 - h / 2) * mpp
    XX, ZZ = np.meshgrid(xs, zs); a = math.radians(turn_deg); c, s = math.cos(a), math.sin(a)
    return cx + XX * c - ZZ * s, cz + XX * s + ZZ * c


def walk_patterns(rng):
    out = {}
    w = rng.random((FOG_H, FOG_W)) < .5; out['random 50 %'] = w
    w = rng.random((FOG_H, FOG_W)) < .85; out['random 85 % (pin holes)'] = w
    ii, jj = np.mgrid[0:FOG_H, 0:FOG_W]; out['checkerboard 1 cell'] = (ii + jj) % 2 == 0
    out['checkerboard 2 cells'] = ((ii // 2) + (jj // 2)) % 2 == 0
    out['stripes 3 on 1 off'] = (ii % 4) != 0
    out['diagonal staircase'] = (ii - jj) % 7 < 4
    cz, cx = (ii + .5) * CELL, (jj + .5) * CELL
    discs = np.zeros((FOG_H, FOG_W), bool)
    for _ in range(140):
        x, z = rng.random() * 4000, rng.random() * 6000
        discs |= np.hypot(cx - x, cz - z) <= 96.0
    out['140 reveal discs (96 m)'] = discs
    return out


def run(walked_sets, zooms, n_views, rng, turn=False, p=None, q=None):
    """q = edge_constants(cginc) runs formula F; the rim below is the paper shader's: it is gated by the edge function's .y"""
    res = {}
    for wname, walked in walked_sets.items():
        for zname, (w, h, mpp, rim_px) in zooms.items():
            for mode in ('fine', 'coarse', 'central'):
                leak = 0; leak_vis = 0; rim_leak = 0; px = 0; unw_px = 0; mx = 0.0; band = 0; edge_px = 0
                r2 = np.random.default_rng(1234)
                for k in range(n_views):
                    cx = 200 + r2.random() * 3600; cz = 200 + r2.random() * 5600; t = (r2.random() * 360.0) if turn else 0.0
                    X, Z = view(cx, cz, w, h, mpp, t)
                    crisp, epx, gate, cw = edge(walked, X, Z, mode, p or P_EDGE, q)
                    inside = (X >= 0) & (X < 4000) & (Z >= 0) & (Z < 6000)
                    unw = (~cw) & inside
                    if E3.is_map3(q):
                        rim = crisp * E3.rim3(q, gate, epx, rim_px)                               # the paper's map3 line: crisp x MapFog308_Rim(walk.y, edgePx, _M308Rim.y)
                        if SHEET is not None and zname.startswith('full'):                        # #308 map 4: the sheet's whole rim - BOTH rims are counted (max = any blend of the two)
                            rim = np.maximum(rim, crisp * E3.rim3_whole(q, edge.field, epx, SHEET['px'] * rim_px / SHEET['basePx']))
                    else: rim = crisp * np.clip(rim_px - epx + .5, 0, 1) * (1 - np.clip((gate - .92) / .08, 0, 1) ** 2 * (3 - 2 * np.clip((gate - .92) / .08, 0, 1)))
                    leak += int((crisp[unw] > 0).sum()); leak_vis += int((crisp[unw] > 1 / 255).sum()); rim_leak += int((rim[unw] > 1 / 255).sum())
                    mx = max(mx, float(crisp[unw].max()) if unw.any() else 0.0)
                    px += int(inside.sum()); unw_px += int(unw.sum())
                    band += int(((crisp > .02) & (crisp < .98)).sum()); edge_px += int(((crisp >= .5) & (np.abs(epx) < 1.0)).sum())
                res[f'{wname} | {zname} | {mode}'] = dict(px=px, unwalked_px=unw_px, crisp_gt0_on_unwalked=leak, crisp_visible_on_unwalked=leak_vis,
                                                         rim_visible_on_unwalked=rim_leak, max_crisp_on_unwalked=round(mx, 4),
                                                         aa_band_px_per_edge_px=round(band / max(edge_px, 1), 3))
    return res


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    global P_EDGE
    sys.path.insert(0, str(ROOT / 'Tools/Art'))
    import map308_lib as M, map308_twin as TW
    arg = lambda name: Path(sys.argv[sys.argv.index(name) + 1]) if name in sys.argv else None
    bundle = arg('--bundle') or M.OUT
    cginc = arg('--cginc') or LIVE_CGINC; Q = edge_constants(cginc)       # None = the threshold formula, a dict = formula F
    note = json.loads((bundle / 'map308_notation.json').read_text(encoding='utf-8')); ce = note['values']['fogEdge']['crispEdge']
    P_EDGE = (f32(ce['thresholdFrom']), f32(ce['thresholdSpan']), f32(ce['noiseCellsPerFogCell'][0]), f32(ce['noiseCellsPerFogCell'][1]))      # formula F does not read it
    rim = float(note['values']['fogEdge']['px'])
    global SHEET
    SHEET = dict(note['values']['fogEdge']['sheet'], basePx=rim) if note['values']['fogEdge'].get('sheet') else None    # #308 map 4
    rng = np.random.default_rng(308)
    sets = walk_patterns(rng)
    sets[f"mock walk ({note['stage']})"] = TW.mock_walk(TW.Bundle(bundle))
    zooms = {  # name: (w, h, metres per px, rim px on screen)
        'minimap 1080p 1.14 m/px': (294, 210, 240.0 / 210, rim),
        'minimap 1440p 0.86 m/px': (392, 280, 240.0 / 280, rim * 4 / 3),
        'minimap 2160p 0.57 m/px': (588, 420, 240.0 / 420, rim * 2),
        'minimap 720p 1.71 m/px': (196, 140, 240.0 / 140, rim * 2 / 3),
        'full Z2 2.66 m/px': (740, 760, 2.66, rim),
        'full Z2 1440p 2.0 m/px': (987, 1013, 2.0, rim * 4 / 3),
        'full Z3 7.9 m/px': (507, 760, 6000.0 / 760, rim),
        'full Z3 720p 11.8 m/px': (338, 507, 6000.0 / 507, rim * 2 / 3),
        'full max zoom 0.27 m/px': (740, 760, .27, rim),
    }
    t = time.time()
    out = dict(north_up=run(sets, zooms, 6, rng, p=P_EDGE, q=Q), turned=run(sets, {k: zooms[k] for k in ('minimap 1080p 1.14 m/px', 'minimap 1440p 0.86 m/px')}, 12, rng, turn=True, p=P_EDGE, q=Q))
    tot = {}
    for part, d in out.items():
        for k, v in d.items():
            mode = k.split(' | ')[-1]; a = tot.setdefault(part + ' / ' + mode, dict(px=0, unwalked_px=0, crisp_gt0=0, visible=0, rim=0, max=0.0))
            a['px'] += v['px']; a['unwalked_px'] += v['unwalked_px']; a['crisp_gt0'] += v['crisp_gt0_on_unwalked']; a['visible'] += v['crisp_visible_on_unwalked']
            a['rim'] += v['rim_visible_on_unwalked']; a['max'] = max(a['max'], v['max_crisp_on_unwalked'])
    worst = sorted(((v['crisp_gt0_on_unwalked'], k) for part in out.values() for k, v in part.items()), reverse=True)[:6]
    aa = [v['aa_band_px_per_edge_px'] for k, v in out['north_up'].items() if k.endswith('| fine') and (k.startswith('mock') or k.startswith('140'))]
    leaks = sum(a['crisp_gt0'] + a['rim'] for a in tot.values())
    rules = constant_rules(Q) if Q else []; broken = [text for text, ok in rules if not ok]; ok = leaks == 0 and not broken
    if Q is None:
        rep = dict(id='map308_edgecheck', bundle=M.rel(bundle), stage=note['stage'], thresholds=[float(v) for v in P_EDGE], rim_px=rim, ok=ok, leaks=leaks, totals=tot, worst=worst,
                   edge_transition_px=dict(min=min(aa), max=max(aa), note='pixels with .02 < walked value < .98 per edge pixel (1 = a 1 px anti-aliased edge)'), seconds=round(time.time() - t, 1))
        if arg('--cginc'): rep.update(cginc=str(cginc).replace('\\', '/'), edge_formula='threshold')      # (a default run writes the report it always wrote)
    else:
        rep = dict(id='map308_edgecheck', bundle=M.rel(bundle), stage=note['stage'], cginc=str(cginc).replace('\\', '/'), edge_formula='map3' if E3.is_map3(Q) else 'F', constants=Q,
                   constant_rules=[dict(rule=text, ok=bool(good)) for text, good in rules], rim_px=rim, ok=ok, leaks=leaks, totals=tot, worst=worst,
                   edge_transition_px=dict(min=min(aa), max=max(aa), note='pixels with .02 < walked value < .98 per edge pixel (1 = a 1 px anti-aliased edge)'), seconds=round(time.time() - t, 1))
    for k, a in tot.items(): print(f"  {'ok  ' if a['crisp_gt0'] + a['rim'] == 0 else 'FAIL'} {k}: {a['unwalked_px']:,} px on unwalked cells, walked value > 0 on {a['crisp_gt0']}, rim on {a['rim']}, largest {a['max']}")
    print(f"  edge transition {min(aa)}-{max(aa)} px")
    if Q: print(f"  {'ok  ' if not broken else 'FAIL'} constants (#define MAPFOG308_E_* of the cginc): {', '.join(text for text, _ in rules)}" + (f" - BROKEN: {', '.join(broken)}" if broken else ''))
    M.write_text((arg('--out') or M.CONCEPT) / ('edgecheck308.json' if note['stage'] == 'base' else f"edgecheck308_{note['stage']}.json"), M.dumps(dict(rep, detail=out)))
    if Q is None:
        print(f"[map308_edgecheck] {'ok' if leaks == 0 else 'FAILED'}: {leaks} leaking px, thresholds {[round(float(v), 3) for v in P_EDGE]}, {rep['seconds']} s")
    else:
        print(f"[map308_edgecheck] {'ok' if ok else 'FAILED'}: {leaks} leaking px, {len(broken)} broken constant rule(s), formula F of {str(cginc).replace(chr(92), '/')} "
              f"(FROM {Q['FROM']:g} SPAN {Q['SPAN']:g} KCOVER {Q['KCOVER']:g} FLOOR {Q['FLOOR']:g} RIMBREAK {Q['RIMBREAK']:g}), {rep['seconds']} s")
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
