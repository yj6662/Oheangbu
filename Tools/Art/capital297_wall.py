"""#297 도성 성곽 — continuous terrain-following capital wall replacing the #296 stepped rampart modules (TEST).

Loop = Architecture296/gates.json Loops[capital296]: the same 8-corner polygon the #296 BuildGateLoop296 walled with 4.7 m
modules (each rising to its own local maximum -> a stepped top). Here one circuit profile (hanok297_wall.walk_profile,
periodic) gives a continuous walk + 여장 parapet; outer face >= 10 m above the outer ground (the #296 MinimumWallHeight).
Corners get circular fillets (R 14 m) so every edge stays straight where the #296 gate objects stand. The four apertures
(canonical CapitalSouthGate253/VictoryGate253 + capital_exit_2/3/5 VictoryGate296, all opened by hwanggyeong_south_gate)
are gaps of aperture width + 2 x 0.2 m, each wall end capped (visual + collision): the loop is sealed everywhere else.
The gate objects, their foundations and route approaches stay in the scene; only the Wall_<edge>_<run> holders are hidden.
Y comes from H: #295 field + the #297 surface ops already applied to the scene (surface-sources.json); no new terrain op.

Outputs Finish297/Capital/: layout.json, unity.json, Meshes/*_LOD{0,1,2}.json + *_Collision.json.
"""
import json, math, re, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
import hanok297_wall as HW
from hanok297_wall import H, FortressWallSpec, fortress_wall, walk_profile
from hanok297 import MB, nz
import surface297

ROOT = Path(__file__).resolve().parents[2]
FIN = ROOT / 'Art/World/Compact/Rebuild/Finish297'
OUT = FIN / 'Capital'
A296 = ROOT / 'Art/World/Compact/Rebuild/Architecture296'
SCENE = ROOT / 'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Main.unity'
PARENT = 'Architecture296_Gates/CapitalPerimeter'

STEP = 1.5                 # profile / collision sampling (m)
FILLET = 14.0              # corner fillet radius (m)
MARGIN = .20               # extra gap per side beyond the #296 aperture width (the gate architecture covers it)
CHUNK = 48.0               # chunk length (per-chunk LOD / culling)
SPEC = dict(walk_w=3.2, min_outer=10.0, inner_rise=1.2, max_grade=.62, parapet_h=1.45, parapet_t=.55, crenel=3.2, crenel_gap=.5,
            batter=.12, stone='stone_dressed', parapet_mat='stone_rough', walk_mat='stone_plain')
MESHES, STATS = [], {}


# --- loop geometry ---------------------------------------------------------------------------------------------------
def fillet_loop(pts, start_edge, start_dist, radius, ds=.25):
    """Closed polygon with circular corner fillets, densely sampled from a point on edge `start_edge` (start_dist metres
    from its first corner) once around back to it. Returns (dense XZ (M,2) with last == first, arc length, fillet
    arc-length intervals)."""
    pts = np.asarray(pts, float); N = len(pts); corners = []
    for i in range(N):
        a, c, b = pts[i - 1], pts[i], pts[(i + 1) % N]
        d1, d2 = nz(c - a), nz(b - c); th = math.acos(float(np.clip(d1 @ d2, -1, 1)))
        t = min(radius * math.tan(th / 2), .4 * np.linalg.norm(c - a), .4 * np.linalg.norm(b - c))
        r = t / math.tan(th / 2) if th > 1e-6 else 0.0
        turn = float(np.sign(d1[0] * d2[1] - d1[1] * d2[0])); l1 = np.array([-d1[1], d1[0]])
        T1, T2 = c - d1 * t, c + d2 * t; O = T1 + l1 * r * turn
        corners.append((T1, T2, O, r, turn, th))
    S = pts[start_edge] + nz(pts[(start_edge + 1) % N] - pts[start_edge]) * start_dist
    out = [S]; arcs = []
    def line(p, q):
        n = max(1, int(math.ceil(np.linalg.norm(q - p) / ds)))
        out.extend(p + (q - p) * (k / n) for k in range(1, n + 1))
    def length():
        return float(np.sum(np.linalg.norm(np.diff(np.array(out), axis=0), axis=1)))
    for j in range(1, N + 1):
        i = (start_edge + j) % N; T1, T2, O, r, turn, th = corners[i]
        line(out[-1], T1)
        if r > 0:
            s0 = length(); a0 = math.atan2(T1[1] - O[1], T1[0] - O[0]); n = max(2, int(math.ceil(r * th / ds)))
            out.extend(O + r * np.array([math.cos(a0 + turn * th * k / n), math.sin(a0 + turn * th * k / n)]) for k in range(1, n + 1))
            out[-1] = T2; arcs.append((s0, length()))
    line(out[-1], S)
    P = np.array(out); s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))])
    return P, s, arcs


def at(Pd, sd, q):
    q = np.asarray(q, float); return np.stack([np.interp(q, sd, Pd[:, 0]), np.interp(q, sd, Pd[:, 1])], -1)


def project(Pd, sd, p):
    """Arc length of the closest point on the dense polyline to p (XZ)."""
    a, b = Pd[:-1], Pd[1:]; d = b - a; L2 = np.maximum(np.sum(d * d, 1), 1e-12)
    t = np.clip(np.sum((p - a) * d, 1) / L2, 0, 1); q = a + d * t[:, None]; i = int(np.argmin(np.linalg.norm(q - p, axis=1)))
    return float(sd[i] + t[i] * (sd[i + 1] - sd[i])), float(np.linalg.norm(q[i] - p))


def loop_profile(Pd, sd, outward):
    """Continuous walk height around the closed loop: circuit_profile's ground rule, periodic walk_profile."""
    L = sd[-1]; ext = 96.0; q = np.arange(-ext, L + ext + 1e-6, STEP); P = at(Pd, sd, np.mod(q, L))
    d = nz(np.gradient(P, axis=0)); n = np.stack([d[:, 1], -d[:, 0]], -1) * outward; hw = SPEC['walk_w'] / 2
    g_c = H(P[:, 0], P[:, 1]); g_o = H(*(P + n * (hw + 2.0)).T); g_i = H(*(P - n * (hw + 1.5)).T)
    ground = np.maximum(np.maximum(g_c, g_i) + SPEC['inner_rise'], g_o + SPEC['min_outer'])
    y = walk_profile(q, ground, SPEC['max_grade'])
    keep = (q >= -1e-6) & (q <= L + 1e-6)
    return q[keep], y[keep]


# --- wall pieces -----------------------------------------------------------------------------------------------------
def spec_for(name, crenel):
    return FortressWallSpec(name, None, outward=1, walk_w=SPEC['walk_w'], min_outer=SPEC['min_outer'], inner_rise=SPEC['inner_rise'],
                            parapet_h=SPEC['parapet_h'], parapet_t=SPEC['parapet_t'], crenel=crenel, crenel_gap=SPEC['crenel_gap'] if crenel < 1e6 else 0.0,
                            batter=SPEC['batter'], stone=SPEC['stone'], parapet_mat=SPEC['parapet_mat'], walk_mat=SPEC['walk_mat'], max_grade=SPEC['max_grade'])


def end_cap(vis, col, P, y, w, first, parapet=False):
    """Close a wall run where it stops at a gate: cross-section polygon (outer foot, outer top, inner top, inner foot)
    with fortress_wall's own end frame (np.gradient one-sided difference), optional parapet end face."""
    i, j = (0, 1) if first else (-1, -2)
    d = nz(P[j] - P[i]) if first else nz(P[i] - P[j])                       # along the wall, pointing into the run's end
    n = np.array([d[1], -d[0]]) * w.outward; hw = w.walk_w / 2; p = P[i]; yy = float(y[i])
    g_o = float(H(*(p + n * (hw + 2.0)))); g_i = float(H(*(p - n * (hw + 1.5))))
    base_o = g_o - 1.2; base_i = min(g_i, yy) - .8
    X = lambda off, h: [p[0] + n[0] * off, h, p[1] + n[1] * off]
    out = [-d[0], 0, -d[1]] if first else [d[0], 0, d[1]]
    quad = [X(hw + w.batter * (yy - base_o), base_o), X(hw, yy), X(-hw, yy), X(-hw - .08 * (yy - base_i), base_i)]
    vis.poly(quad, w.stone, out, uvscale=2.0); col.poly(quad, 'c', out)
    if parapet:
        a, b = X(hw, yy), X(hw - w.parapet_t, yy)
        vis.quad(a, b, [b[0], yy + w.parapet_h, b[2]], [a[0], yy + w.parapet_h, a[2]], w.parapet_mat, out, uvscale=1.0)


def samples(a, b, step, extra=()):
    base = np.linspace(a, b, max(2, int(math.ceil((b - a) / step)) + 1))
    q = np.unique(np.round(np.concatenate([base, [e for e in extra if a - 1e-6 <= e <= b + 1e-6]]), 4))
    keep = [q[0]]
    for v in q[1:]:
        if v - keep[-1] > .06: keep.append(v)
    keep[-1] = b
    return np.array(keep)


def build_run(k, s_a, s_b, Pd, sd, sp, yp, arcs):
    """One run between two apertures: exact block boundaries so every 여장 block starts/ends on a sample."""
    Lr = s_b - s_a; gap = SPEC['crenel_gap']; nb = max(1, int(round((Lr + gap) / SPEC['crenel']))); seg = (Lr + gap) / nb
    blocks = [v for kk in range(nb) for v in (kk * seg, kk * seg + seg - gap)]
    # chunk split points (run-local), nudged off the fillets (one-sided gradients disagree on curves)
    nch = max(1, int(round(Lr / CHUNK))); cuts = list(np.linspace(0, Lr, nch + 1))
    for c in range(1, nch):
        g = s_a + cuts[c]
        for a0, a1 in arcs:
            if a0 - 2 < g < a1 + 2: cuts[c] = (a1 + 2.5 - s_a) if (g - a0) > (a1 - g) else (a0 - 2.5 - s_a)
    cuts = sorted(set(round(c, 4) for c in cuts))
    out = []
    pts = lambda q: (at(Pd, sd, s_a + q), np.interp(s_a + q, sp, yp))
    for c, (a, b) in enumerate(zip(cuts[:-1], cuts[1:])):
        name = f'CapitalWall_{k}_{c:02d}'; first, last = (c == 0), (c == len(cuts) - 2)
        Pc, yc = pts(samples(a, b, STEP, [a, b]))                       # 1.5 m base: collision + LOD2 source
        # LOD0 / LOD1: block boundaries inserted (lod=0 call = no subsampling, steps kept); LOD2: 6 m, continuous parapet
        variants = ((0, pts(samples(a, b, STEP, blocks + [a, b])), seg, 0), (1, pts(samples(a, b, 3.0, blocks + [a, b])), seg, 0), (2, (Pc, yc), 1e9, 2))
        info = None
        for lod, (P, y), crenel, klod in variants:
            w = spec_for(name, crenel); vis, _, inf = fortress_wall(w, klod, P, y, float(a))
            for fl, ok in ((True, first), (False, last)):
                if ok: end_cap(vis, MB('tmp'), P, y, w, fl)
            vis.export(OUT / 'Meshes' / f'{name}_LOD{lod}.json')
            if lod == 0:
                info = inf; STATS[name] = dict(tris=vis.tris(), length=round(inf['length'], 1), walk=[round(v, 1) for v in inf['top']],
                                               outer=[round(v, 1) for v in inf['outer_height']])
            else: STATS[name][f'tris_lod{lod}'] = vis.tris()
        w = spec_for(name, seg); _, col, _ = fortress_wall(w, 0, Pc, yc, float(a))
        for fl, ok in ((True, first), (False, last)):
            if ok: end_cap(MB('tmp'), col, Pc, yc, w, fl)
        col.export(OUT / 'Meshes' / f'{name}_Collision.json'); STATS[name]['collision_tris'] = col.tris()
        MESHES.append(dict(name=name, kind='wall', world=True))
        out.append(dict(name=name, s=[round(s_a + a, 2), round(s_a + b, 2)], col=col, walk=info['walk']))
    return out, seg, nb


# --- #296 hierarchy (hide list) ---------------------------------------------------------------------------------------
def scene_children(scene, parent):
    """Direct children names of `parent` (hierarchy path) in a text-serialised Unity scene (GameObject m_Name +
    Transform m_GameObject/m_Father). Returns None if the scene is missing."""
    if not scene.exists(): return None
    name, tr_go, tr_father, go_tr = {}, {}, {}, {}; cur_t = cur = None
    rx = re.compile(r'--- !u!(\d+) &(-?\d+)'); fid = re.compile(r'fileID: (-?\d+)')
    with open(scene, encoding='utf-8', errors='replace') as f:
        for line in f:
            if line.startswith('--- !u!'): m = rx.match(line); cur_t, cur = int(m.group(1)), m.group(2); continue
            if cur_t == 1 and line.startswith('  m_Name: '): name[cur] = line[10:].rstrip('\n')
            elif cur_t in (4, 224):
                if line.startswith('  m_GameObject: '): g = fid.search(line).group(1); tr_go[cur] = g; go_tr[g] = cur
                elif line.startswith('  m_Father: '): tr_father[cur] = fid.search(line).group(1)
    def path(t):
        parts = []
        while t and t != '0': parts.append(name.get(tr_go.get(t), '?')); t = tr_father.get(t)
        return '/'.join(reversed(parts))
    return sorted(path(t) for t in tr_go if path(tr_father.get(t, '0')) == parent)


def derived_wall_holders(loop):
    """BuildGateLoop296 naming (CompactArchitecture296.Gates.cs): per edge, runs split at apertures (Distance +- Width/2),
    spans > 64 m split into ceil(span/64) chunks, each chunk = one holder Wall_<edge>_<run++>."""
    pts = np.array([[p['x'], p['z']] for p in loop['Points']]); names = []
    for e in range(len(pts)):
        L = float(np.linalg.norm(pts[(e + 1) % len(pts)] - pts[e])); cursor = 0.0; run = 0; spans = []
        for g in sorted([g for g in loop['Gates'] if g['Edge'] == e], key=lambda g: g['Distance']):
            spans.append((cursor, max(cursor, g['Distance'] - g['Width'] * .5))); cursor = max(cursor, g['Distance'] + g['Width'] * .5)
        spans.append((cursor, L))
        for a, b in spans:
            span = b - a
            if span < .05: continue
            for _ in range(int(math.ceil(span / 64.0)) if span > 64 else 1): names.append(f'{PARENT}/Wall_{e}_{run}'); run += 1
    return names


# --- checks ----------------------------------------------------------------------------------------------------------
def ray_hits(tris, o, d):
    """Möller-Trumbore: does the segment o -> o+d hit any triangle (M,3,3)?"""
    v0, v1, v2 = tris[:, 0], tris[:, 1], tris[:, 2]; e1, e2 = v1 - v0, v2 - v0
    p = np.cross(d, e2); det = np.sum(e1 * p, 1); ok = np.abs(det) > 1e-9; inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
    tv = o - v0; u = np.sum(tv * p, 1) * inv; qv = np.cross(tv, e1); v = np.sum(d * qv, 1) * inv; t = np.sum(e2 * qv, 1) * inv
    return bool(np.any(ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t >= 0) & (t <= 1)))


def tri_array(mb):
    V = np.asarray(mb.V, float); T = np.array([i for t in mb.T.values() for i in t], int).reshape(-1, 3); return V[T]


def main():
    OUT.mkdir(parents=True, exist_ok=True); (OUT / 'Meshes').mkdir(exist_ok=True)
    for f in (OUT / 'Meshes').glob('*.json'): f.unlink()
    loop = [l for l in json.loads((A296 / 'gates.json').read_text(encoding='utf-8-sig'))['Loops'] if l['Id'] == 'capital296'][0]
    corners = np.array([[p['x'], p['z']] for p in loop['Points']], float)
    gates = loop['Gates']; south = [g for g in gates if g['Id'] == 'south_gate'][0]
    # ground = #295 field + the #297 ops already applied in the scene (surface-sources.json); none reaches this loop, and
    # the staged palace pads are checked below (they must not change a wall sample either)
    prot_path = HW.SURF / 'protected.bytes'
    prot = (np.fromfile(prot_path, np.uint8).reshape(1501, 1001) > 0) if prot_path.exists() else None
    base = HW.terrain().copy(); HW._H, _ = surface297.apply(base.copy(), prot, surface297.load_ops())
    Pd, sd, arcs = fillet_loop(corners, south['Edge'], south['Distance'], FILLET)
    L = float(sd[-1]); sp, yp = loop_profile(Pd, sd, 1)
    # apertures (arc length on the loop; the south gate is the seam s = 0 = L)
    ap = []
    for g in gates:
        c = np.array([g['Centre']['x'], g['Centre']['z']]); sg, off = project(Pd, sd, c)
        if g['Id'] == 'south_gate': sg = 0.0
        ap.append(dict(id=g['Id'], path=g['Path'], s=sg, offset=round(off, 3), half=g['Width'] / 2 + MARGIN, width=g['Width'], centre=g['Centre'],
                       inward=g['Inward'], routes=g['Routes'], fact=g['OpenFact']))
    ap.sort(key=lambda a: a['s'])
    runs = []; walks = []
    for k in range(len(ap)):
        a = ap[k]; b = ap[(k + 1) % len(ap)]
        s_a = a['s'] + a['half']; s_b = (b['s'] if k + 1 < len(ap) else L) - b['half']
        pieces, seg, nb = build_run(k, s_a, s_b, Pd, sd, sp, yp, arcs)
        runs.append(dict(run=k, between=[a['id'], b['id']], s=[round(s_a, 2), round(s_b, 2)], length=round(s_b - s_a, 1), crenel=round(seg, 3), blocks=nb,
                         chunks=[p['name'] for p in pieces])); walks += pieces
    # --- checks: seal (horizontal rays across the wall at 3 heights every ~1 m), apertures clear, NaN / empty ---------
    # a real gap needs open air above the ground: rays run at (highest ground across the wall band) + dh, from each side;
    # the feet must also be buried where they meet the ground (no crawl-under)
    checks = dict(seal_rays=0, seal_misses=[], feet_checked=0, feet_exposed=[], aperture_rays=0, aperture_hits=[], nan=[], empty=[])
    hw = SPEC['walk_w'] / 2
    for w in walks:
        tris = tri_array(w['col']); a, b = w['s']
        for q in np.arange(a + .3, b - .3, 1.0):
            p = at(Pd, sd, q); d = nz(at(Pd, sd, q + .05) - at(Pd, sd, q - .05)); n = np.array([d[1], -d[0]])
            band = max(float(H(*(p + n * o))) for o in np.linspace(-6, 6, 13))
            for side, sgn in (('outer', 1), ('inner', -1)):
                for dh in (.3, 1.0, 1.6):
                    o = np.array([p[0] + n[0] * sgn * 9, band + dh, p[1] + n[1] * sgn * 9]); dvec = np.array([-n[0] * sgn * 18, 0, -n[1] * sgn * 18])
                    checks['seal_rays'] += 1
                    if not ray_hits(tris, o, dvec): checks['seal_misses'].append([round(float(q), 1), side, dh])
            yq = float(np.interp(q, sp, yp)); g_o = float(H(*(p + n * (hw + 2.0)))); g_i = float(H(*(p - n * (hw + 1.5))))
            base_o = g_o - 1.2; base_i = min(g_i, yq) - .8
            fo = p + n * (hw + SPEC['batter'] * (yq - base_o)); fi = p - n * (hw + .08 * (yq - base_i)); checks['feet_checked'] += 1
            if base_o > float(H(*fo)) - .25 or base_i > float(H(*fi)) - .25:
                checks['feet_exposed'].append([round(float(q), 1), round(float(H(*fo)) - base_o, 2), round(float(H(*fi)) - base_i, 2)])
    all_col = np.concatenate([tri_array(w['col']) for w in walks])
    joints = [w['s'][1] for w in walks if not any(abs(w['s'][1] - (a['s'] - a['half']) % L) < .5 for a in ap)]
    checks['joint_rays'] = 0; checks['joint_misses'] = []
    for q in joints:
        for dq_ in (-.05, 0.0, .05):
            p = at(Pd, sd, q + dq_); d = nz(at(Pd, sd, q + dq_ + .05) - at(Pd, sd, q + dq_ - .05)); n = np.array([d[1], -d[0]])
            band = max(float(H(*(p + n * o))) for o in np.linspace(-6, 6, 13))
            for sgn in (1, -1):
                for dh in (.3, 1.0, 1.6):
                    checks['joint_rays'] += 1
                    if not ray_hits(all_col, np.array([p[0] + n[0] * sgn * 9, band + dh, p[1] + n[1] * sgn * 9]), np.array([-n[0] * sgn * 18, 0, -n[1] * sgn * 18])):
                        checks['joint_misses'].append([round(float(q + dq_), 2), sgn, dh])
    for a in ap:
        c = np.array([a['centre']['x'], a['centre']['y'], a['centre']['z']]); iw = np.array([a['inward']['x'], 0, a['inward']['z']])
        tan = np.array([iw[2], 0, -iw[0]])
        for lat in np.linspace(-(a['width'] / 2 - .45), a['width'] / 2 - .45, 5):
            for dh in (.3, 1.0, 1.6):
                o = c + tan * lat + np.array([0, dh, 0]) - iw * 9; checks['aperture_rays'] += 1
                if ray_hits(all_col, o, iw * 18): checks['aperture_hits'].append([a['id'], round(float(lat), 2), dh])
    for f in sorted((OUT / 'Meshes').glob('*.json')):
        d = json.loads(f.read_text(encoding='utf-8'))
        if not np.all(np.isfinite(d['v'])) or not np.all(np.isfinite(d['n'])) or not np.all(np.isfinite(d['uv'])): checks['nan'].append(f.name)
        if not d['sub'] or sum(len(s['t']) for s in d['sub']) == 0: checks['empty'].append(f.name)
    # staged compounds not yet in the scene surface (e.g. the palace pads) must leave the wall ground untouched
    applied = {o['source'] for o in surface297.load_ops()}; staged = []
    for u in sorted(FIN.glob('*/unity.json')):
        dd = json.loads(u.read_text(encoding='utf-8')); src = dd.get('compound', u.parent.name)
        if src == 'Capital' or src in applied: continue
        staged += [dict(o, source=src) for o in dd.get('terrainOps', [])]
    if staged:
        Hs, _ = surface297.apply(HW._H.copy(), prot, staged); q = np.arange(0, L, 2.0); Pq = at(Pd, sd, q)
        dq = nz(np.gradient(Pq, axis=0)); nq = np.stack([dq[:, 1], -dq[:, 0]], -1); diff = 0.0
        for o in (-4.5, -2.0, 0.0, 2.0, 4.5):
            X_ = Pq + nq * o; saved = HW._H; a1 = H(X_[:, 0], X_[:, 1]); HW._H = Hs; a2 = H(X_[:, 0], X_[:, 1]); HW._H = saved
            diff = max(diff, float(np.max(np.abs(a2 - a1))))
        checks['staged_ops'] = dict(sources=sorted({o['source'] for o in staged}), ops=len(staged), max_change_at_wall=round(diff, 4))
    # --- hide list: every Wall_<edge>_<run> holder under CapitalPerimeter (not the gate holders / approaches) -----------
    derived = derived_wall_holders(loop)
    kids = scene_children(SCENE, PARENT)
    from_scene = [p for p in (kids or []) if re.fullmatch(re.escape(PARENT) + r'/Wall_\d+_\d+', p)]
    hide = from_scene if kids is not None else derived
    others = [p for p in (kids or []) if p not in from_scene]
    hide_check = dict(scene=str(SCENE.relative_to(ROOT)).replace('\\', '/'), scene_children=len(kids or []), wall_holders=len(from_scene),
                      derived=len(derived), identical=sorted(from_scene) == sorted(derived), kept=others)
    # --- routes: through each gate (needs the gate open), and closed-state halves stopping short of the leaves --------
    R296 = {r['id']: r for r in json.loads((A296 / 'Generated/routes.json').read_text(encoding='utf-8-sig'))['routes']}
    routes, crossing = [], []
    polyc = Pd
    for rid, r in R296.items():
        Q = np.array([[p['x'], p['y'], p['z']] for p in r['points']])
        for j in range(len(Q) - 1):
            p0, p1 = Q[j, [0, 2]], Q[j + 1, [0, 2]]
            a_, b_ = polyc[:-1], polyc[1:]; rr = b_ - a_; ss = p1 - p0; den = rr[:, 0] * ss[1] - rr[:, 1] * ss[0]
            okd = np.abs(den) > 1e-9; den_ = np.where(okd, den, 1)
            t = ((p0 - a_)[:, 0] * ss[1] - (p0 - a_)[:, 1] * ss[0]) / den_; u = ((p0 - a_)[:, 0] * rr[:, 1] - (p0 - a_)[:, 1] * rr[:, 0]) / den_
            hit = np.where(okd & (t >= 0) & (t <= 1) & (u >= 0) & (u <= 1))[0]
            for i in hit:
                X_ = a_[i] + rr[i] * t[i]; sx = float(sd[i] + t[i] * (sd[i + 1] - sd[i]))
                best = min(ap, key=lambda a: min(abs(sx - a['s']), abs(sx - a['s'] - L), abs(sx - a['s'] + L)))
                dist = min(abs(sx - best['s']), abs(sx - best['s'] - L), abs(sx - best['s'] + L))
                crossing.append(dict(route=rid, gate=best['id'], point=[round(float(X_[0]), 2), round(float(Q[j, 1] + (Q[j + 1, 1] - Q[j, 1]) * u[i]), 2), round(float(X_[1]), 2)],
                                     alongFromGateCentre=round(dist, 2), clearHalfWidth=round(best['half'] - MARGIN - (r.get('width', 2.0) / 2), 2),
                                     inAperture=bool(dist <= best['half'] - .3)))
    seen = set(); crossing = [c for c in crossing if not ((c['route'], c['gate'], tuple(c['point'])) in seen or seen.add((c['route'], c['gate'], tuple(c['point']))))]
    checks['crossings_outside_apertures'] = [c for c in crossing if not c['inAperture']]
    for a in ap:
        rid = next((r for r in a['routes'] if r in R296 and any(c['route'] == r and c['gate'] == a['id'] for c in crossing)), None)
        if rid is None: continue
        Q = np.array([[p['x'], p['y'], p['z']] for p in R296[rid]['points']])
        c = np.array([a['centre']['x'], a['centre']['z']]); iw = np.array([a['inward']['x'], a['inward']['z']])
        side = (Q[:, [0, 2]] - c) @ iw; near = np.linalg.norm(Q[:, [0, 2]] - c, axis=1)
        k0 = int(np.argmin(near)); lo = k0; hi = k0
        while lo > 0 and near[lo] < 32: lo -= 1
        while hi < len(Q) - 1 and near[hi] < 32: hi += 1
        seg_ = Q[lo:hi + 1]; seg_ = seg_[np.r_[np.arange(0, len(seg_) - 1, max(1, len(seg_) // 24)), len(seg_) - 1]]
        sd_ = (seg_[:, [0, 2]] - c) @ iw
        if sd_[0] > 0: seg_ = seg_[::-1]; sd_ = sd_[::-1]                  # outside -> inside
        routes.append(dict(id=f'{a["id"]}_through', points=seg_.tolist(), requires=f'{a["fact"]} (gate leaves open)', source=rid))
        leaf = 1.5                                                          # stop 1.5 m short of the leaf plane (blocker depth unknown offline)
        outside = seg_[sd_ < -leaf]; inside = seg_[sd_ > leaf]
        if len(outside) >= 2: routes.append(dict(id=f'{a["id"]}_outside_to_leaf', points=outside.tolist(), requires='', source=rid))
        if len(inside) >= 2: routes.append(dict(id=f'{a["id"]}_inside_to_leaf', points=inside[::-1].tolist(), requires='', source=rid))
    for r in routes:
        pts_ = np.array(r['points']); g = H(pts_[:, 0], pts_[:, 2]); r['surface_dy'] = [round(float((pts_[:, 1] - g).min()), 2), round(float((pts_[:, 1] - g).max()), 2)]
    # --- outputs ---------------------------------------------------------------------------------------------------------
    total = dict(LOD0=sum(v['tris'] for v in STATS.values()), LOD1=sum(v.get('tris_lod1', 0) for v in STATS.values()),
                 LOD2=sum(v.get('tris_lod2', 0) for v in STATS.values()), collision=sum(v.get('collision_tris', 0) for v in STATS.values()))
    walk_all = np.concatenate([np.array(w['walk']) for w in walks])
    layout = dict(note=__doc__.strip().splitlines()[0], loop=dict(length=round(L, 1), corners=corners.tolist(), fillet=FILLET, fillets=[[round(a, 1), round(b, 1)] for a, b in arcs],
                                                               walk=[round(float(yp.min()), 1), round(float(yp.max()), 1)]),
                  spec=SPEC, apertures=[{k: v for k, v in a.items()} for a in ap], runs=runs, hide=hide, hideCheck=hide_check, crossingRoutes=crossing,
                  routes=routes, checks=checks, stats=STATS, totals=total,
                  # Tools/Blender/preview_fortress297.py keys: mesh names under walls; the kept #296 gate architecture
                  # (CapitalSouthGate253 source bounds 23.13 x 24.69 x 13.39 m) as placeholders in the apertures
                  walls=[dict(name=m['name']) for m in MESHES], gates=[], paths=[], buildings=[], terrainOps=[],
                  preview=dict(terrain=[1560, 2420, 2500, 3660],
                               placeholders=[dict(name='Gate296_' + a['id'], centre=[a['centre']['x'], a['centre']['y'] - .4, a['centre']['z']], size=[23.13, 12.4, 13.39],
                                                  yaw=math.degrees(math.atan2(a['inward']['x'], a['inward']['z']))) for a in ap],
                               cameras=[dict(name='south', eye=[2000, 125, 2440], target=[2000, 96, 2560]),
                                                                         dict(name='southeast', eye=[2330, 170, 2480], target=[2250, 130, 2560]),
                                                                         dict(name='north', eye=[1900, 110, 3700], target=[1900, 70, 3600]),
                                                                         dict(name='west_gates', eye=[1560, 80, 3480], target=[1645, 58, 3480]),
                                                                         dict(name='overview', eye=[1400, 700, 2300], target=[2000, 80, 3080], lens=28),
                                                                         dict(name='exit2', eye=[2275, 95, 3450], target=[2232, 78, 3494]),
                                                                         dict(name='wallwalk', eye=[2120, 118, 2562], target=[2000, 100, 2556])]))
    (OUT / 'layout.json').write_text(json.dumps(layout, indent=1, ensure_ascii=False), encoding='utf-8')
    flat = lambda pts: [round(float(v), 3) for p in pts for v in p]
    unity = dict(compound='Capital', root='Finish297_Capital', meshes=MESHES, hide=hide, clip=[], markers=[],
                 gates=[dict(name=a['id'], position=[a['centre']['x'], a['centre']['y'], a['centre']['z']],
                             yaw=round(math.degrees(math.atan2(a['inward']['x'], a['inward']['z'])), 3), width=a['width'], height=9.9, barred='', shortcut=False) for a in ap],
                 routes=[dict(id=r['id'], points=flat(r['points']), requires=r['requires']) for r in routes], doors=[], terrainOps=[],
                 info=dict(testOnly=True, gateObjectsKept=[a['path'] for a in ap], crossingRoutes=crossing,
                           note='*_through routes need the capital gates opened (hwanggyeong_south_gate); in the saved closed state the '
                                'SouthGateDoorPresentation blockers stop them at the leaves. *_outside_to_leaf / *_inside_to_leaf are the closed-state checks.'))
    (OUT / 'unity.json').write_text(json.dumps(unity, indent=1, ensure_ascii=False), encoding='utf-8')
    print(json.dumps(dict(loop_length=round(L, 1), walk=layout['loop']['walk'], chunks=len(MESHES), totals=total,
                          outer=[min(v['outer'][0] for v in STATS.values()), max(v['outer'][1] for v in STATS.values())],
                          apertures=[(a['id'], round(a['s'], 1), a['offset'], round(2 * a['half'], 2)) for a in ap],
                          hide=len(hide), hide_check={k: v for k, v in hide_check.items() if k != 'kept'}, kept=hide_check['kept'],
                          seal=dict(rays=checks['seal_rays'], misses=len(checks['seal_misses']), feet=checks['feet_checked'], feet_exposed=len(checks['feet_exposed']), joint_rays=checks['joint_rays'], joint_misses=len(checks['joint_misses'])), apertures_clear=dict(rays=checks['aperture_rays'], hits=len(checks['aperture_hits'])),
                          nan=checks['nan'], empty=checks['empty'], staged=checks.get('staged_ops'), crossings_outside=checks['crossings_outside_apertures'],
                          routes=[(r['id'], len(r['points']), r['surface_dy']) for r in routes]), indent=1, ensure_ascii=False))


if __name__ == '__main__':
    main()
