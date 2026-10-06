"""#308 D308-3b Seal308 관문 성벽 - stone pass wall + 홍예 관문 replacing the D308-3 palisade (offline, numpy only, no paid service).

  python Tools/Art/seal308_wall.py          # -> Art/World/Compact/Rebuild/Enclosure305/Out/Seal308/
                                            #    Meshes/P308_{W0,W1,gatehouse,leaf_L,leaf_R}_LOD{0,1,2}.json + *_Collision.json,
                                            #    Meshes/P308_gate_Collision.json (closed blocker), unity.json (editor manifest),
                                            #    footprint308.json (seal308_closure.py), plan308.png, wall308.txt (ledger + checks)

Contract: D308-3b + SPEC-WORLD-ENCLOSURE-305 §1b′ (Seal308). Inputs (read-only): Enclosure305/seal308.json (anchors A/B = the seal run
ends E305_084 / E305_056, road crossing, route, fact), Out/Seal308/wall308.json (hand ledger, every number TEST), segments305.json
(seam shell ends), Architecture296/Generated/routes.json (road centreline), Finish297/Surface/height.bytes (offline ground; the
editor seal-check re-measures the physics ground - Spec Temporary Exception). The old palisade outputs live in Out/Seal308/_palisade_v1/.

Same grammar as the 도성 성곽: the wall is hanok297_wall.fortress_wall with the capital297_wall SPEC (walk 3.2 m, 여장 1.45 m,
crenels 3.2 m, batter 1:0.12, stone_dressed / stone_rough / stone_plain), the gate is the #297 철옹 산성 kit (육축 piers + 홍예 vault
+ 문루 munru 3x2 military 팔작) and the leaves follow hanok297_wall.gate_door (outline = arch + overlap, mounted in front of the inner
face, swing inward). Materials = the #297 slots (URP Lit, emission 0). No 빗장, no light, no interaction.

Layout (t = unit A->B, n = left normal = north = 상경 가도 / EA side, s along t from A, u along n):
  * wall centreline u = centre_offset (3.2 m): the battered outer foot and the gate block's outer face sit on the line A->B;
  * gate centre = where the road centreline crosses the wall centreline; block = piers + passage (passage_w) x depth;
  * end 치 at s = 0 (A) and s = L (B), u from towers.u_min_m (ledger -1.8: covers the seam shell end 1 m south of the line, 0.6 m thick)
    to the gate block's inner face line;
  * wall runs W0 (치 A -> gate) and W1 (gate -> 치 B) overlap the 치 / block by run_overlap_m (hidden joints);
  * walk = hanok297_wall.walk_profile over (highest ground of the wall band + col_above), grade <= max_grade;
  * leaves: hinge-local meshes (pivot = hinge on the floor of the inner mouth); bottom edge = lowest ground over the whole swing
    minus bury, so it is always under the surface (road falls ~0.2 northwards); open colliders (leaf outline) for the open state;
  * blocker P308_gate_col = the closed leaves' outline prism (inside the leaves' mass), obstacle = carve box over it.
Object frames: pivot on the offline ground, local x = t, local z = n, Unity yaw = atan2(-t.z, t.x) (Quaternion.Euler(0, yaw, 0)).
"""
import json, math, sys, time, hashlib
from pathlib import Path
import numpy as np

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
from hanok297 import MB, nz                                                     # noqa: E402
import hanok297_wall as HW                                                      # noqa: E402
from hanok297_wall import FortressWallSpec, fortress_wall, walk_profile         # noqa: E402
from hanok297_building import BuildingSpec, build_building                      # noqa: E402

ROOT = TOOLS.parents[1]
ENC = ROOT / 'Art/World/Compact/Rebuild/Enclosure305'
OUTD = ENC / 'Out/Seal308'
LEDGER = OUTD / 'wall308.json'
SEAL = ENC / 'seal308.json'
SEGS = ENC / 'segments305.json'
A_ = ROOT / 'Oheangbu/Assets/_Project'
HEIGHT = A_ / 'Art/World/Finish297/Surface/height.bytes'
ROUTES = ROOT / 'Art/World/Compact/Rebuild/Architecture296/Generated/routes.json'
UP = np.array([0.0, 1.0, 0.0])

HW._H = np.fromfile(HEIGHT, '<f4').reshape(1501, 1001).astype(np.float64)    # fortress_wall samples HW.H: use the #297 surface
H = HW.H


# ------------------------------------------------------------------ small helpers
def samples(a, b, step, extra=()):
    """capital297_wall.samples: a..b every <= step, with the given break points inserted (crenel block ends)."""
    base = np.linspace(a, b, max(2, int(math.ceil((b - a) / step)) + 1))
    q = np.unique(np.round(np.concatenate([base, [e for e in extra if a - 1e-6 <= e <= b + 1e-6]]), 4))
    keep = [q[0]]
    for v in q[1:]:
        if v - keep[-1] > .06: keep.append(v)
    keep[-1] = b
    return np.array(keep)


def blocks_of(length, crenel, gap):
    nb = max(1, int(round((length + gap) / crenel))); seg = (length + gap) / nb
    return seg, [(k * seg, k * seg + seg - gap) for k in range(nb)]


def to_local(mb, O, t, n, name=None):
    """World-space MB -> object frame (origin O = (x, y, z), local x = t, local z = n). Proper rotation about +Y."""
    out = MB(name or mb.name)
    if not mb.V: return out
    V = np.asarray(mb.V, float) - np.asarray(O, float); N = np.asarray(mb.N, float)
    rot = lambda A: np.stack([A[:, 0] * t[0] + A[:, 2] * t[1], A[:, 1], A[:, 0] * n[0] + A[:, 2] * n[1]], -1)
    out.V = rot(V).tolist(); out.N = rot(N).tolist(); out.UV = list(mb.UV)
    for k, v in mb.T.items(): out.T[k] = list(v)
    return out


def to_world_pts(V, O, t, n):
    V = np.asarray(V, float)
    return np.stack([O[0] + V[..., 0] * t[0] + V[..., 2] * n[0], O[1] + V[..., 1], O[2] + V[..., 0] * t[1] + V[..., 2] * n[1]], -1)


def tri_array(mb):
    V = np.asarray(mb.V, float); T = np.array([i for tt in mb.T.values() for i in tt], int).reshape(-1, 3); return V[T]


def ray_hits(tris, o, d):
    """capital297_wall.ray_hits: does the segment o -> o + d hit any triangle (M, 3, 3)?"""
    if len(tris) == 0: return False
    v0, v1, v2 = tris[:, 0], tris[:, 1], tris[:, 2]; e1, e2 = v1 - v0, v2 - v0
    p = np.cross(d, e2); det = np.sum(e1 * p, 1); ok = np.abs(det) > 1e-9; inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
    tv = o - v0; u = np.sum(tv * p, 1) * inv; qv = np.cross(tv, e1); v = np.sum(d * qv, 1) * inv; tt = np.sum(e2 * qv, 1) * inv
    return bool(np.any(ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (tt >= 0) & (tt <= 1)))


def strip(mb, Aa, Bb, mat, out, uvh=2.0):
    """Ruled surface between polylines Aa (lower) and Bb (upper), (k, 3) each; out = vector or (k, 3) per-row normals."""
    Pq = np.stack([Aa, Bb], 1)
    if np.ndim(out) == 2: out = np.repeat(np.asarray(out, float)[:, None, :], 2, 1)
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(Aa[:, [0, 2]], axis=0), axis=1))])
    uv = np.stack([np.repeat((s / 2.0)[:, None], 2, 1), Pq[..., 1] / uvh], -1)
    mb.grid(Pq, mat, uv, out)


def mat_emission(path):
    """(emission colour max, _EMISSION keyword on) from a Unity .mat text file."""
    txt = path.read_text(encoding='utf-8', errors='replace'); col = 0.0; kw = False
    for line in txt.splitlines():
        if '_EmissionColor' in line and '{' in line:
            vals = [float(x.split(':')[1]) for x in line.split('{')[1].split('}')[0].split(',') if x.strip()[0] in 'rgb']
            col = max(col, max(vals) if vals else 0.0)
        if ('m_ValidKeywords' in line or 'm_ShaderKeywords' in line or line.strip().startswith('- ')) and '_EMISSION' in line: kw = True
    return col, kw


# ------------------------------------------------------------------ main
def main():
    S = json.loads(SEAL.read_text(encoding='utf-8')); Wl = json.loads(LEDGER.read_text(encoding='utf-8'))
    wl, ht, gt, lv, tw, cl = Wl['wall'], Wl['height'], Wl['gate'], Wl['leaves'], Wl['towers'], Wl['closure']
    A = np.asarray(S['anchors']['A'], float); B = np.asarray(S['anchors']['B'], float)
    L = float(np.hypot(*(B - A))); t = (B - A) / L; n = np.array([-t[1], t[0]])
    if n[1] <= 0: raise SystemExit('left normal of A->B does not point north (상경 가도 side): anchors swapped?')
    yaw = math.degrees(math.atan2(-t[1], t[0]))
    cu = float(Wl['placement']['centre_offset_m']); hw = wl['walk_w'] / 2; ov = wl['run_overlap_m']
    tl = tw['len_m']; u0, u1 = tw['u_min_m'], cu + gt['depth'] / 2
    W = lambda s, u: A + t * s + n * u
    Wv = lambda s, u: A[None, :] + t[None, :] * np.asarray(s, float).reshape(-1)[:, None] + n[None, :] * np.asarray(u, float).reshape(-1)[:, None]
    g_at = lambda P: H(P[..., 0], P[..., 1])

    # ---- road centreline (routes.json) and its crossing of the wall centreline
    R296 = {r['id']: r for r in json.loads(ROUTES.read_text(encoding='utf-8-sig'))['routes']}
    rid = S['route'].split(' ')[0]; road = R296[rid]; road_w = float(road.get('width', S['road']['width_m']))
    RP = np.array([(p['x'], p['z']) for p in road['points']], float)
    dense = np.concatenate([np.linspace(a, b, max(2, int(np.hypot(*(b - a)) / .25) + 1)) for a, b in zip(RP[:-1], RP[1:])])
    rs = (dense - A) @ t; ru = (dense - A) @ n

    def road_s_at(u):
        k = np.nonzero(np.diff(np.sign(ru - u)) != 0)[0]
        if len(k) == 0: raise SystemExit(f'road {rid} never crosses u = {u}')
        best = min(k, key=lambda i: abs(rs[i] - ((np.asarray(S['road']['crossing']) - A) @ t)))
        f = (u - ru[best]) / (ru[best + 1] - ru[best]); return float(rs[best] + f * (rs[best + 1] - rs[best]))
    s_g = road_s_at(cu) + gt['shift_s_m']
    half = gt['passage_w'] / 2; Wg = gt['passage_w'] + 2 * gt['pier_w']; dep = gt['depth']
    gs0, gs1 = s_g - Wg / 2, s_g + Wg / 2
    if not (tl / 2 + 3 < gs0 and gs1 < L - tl / 2 - 3):
        raise SystemExit(f'gate block s {gs0:.1f}..{gs1:.1f} leaves no wall run between the end 치 (len {tl}) on a {L:.1f} m line')

    # ---- walk profile over the whole line (height rule)
    u_band = np.arange(cu - hw - 1.0 - ht['outer_band_m'], cu + hw + 0.8 + ht['player_side_m'] + 1e-6, 1.0)
    u_play = np.arange(cu + hw + 0.8, cu + hw + 0.8 + ht['player_side_m'] + 1e-6, 1.0)

    def band_max(s, us):
        ss = np.array([s - ht['along_m'], s, s + ht['along_m']]); SS, UU = np.meshgrid(ss, us)
        return float(np.max(g_at(Wv(SS.ravel(), UU.ravel()))))
    s_prof = np.arange(-tl / 2 - 3.0, L + tl / 2 + 3.0 + 1e-6, wl['station_m'])
    req = np.array([band_max(s, u_band) for s in s_prof]) + ht['col_above_m']
    y_prof = walk_profile(s_prof, req + .16, wl['max_grade'])
    y_prof = np.maximum(y_prof, req)
    ps_prof = np.array([band_max(s, u_play) for s in s_prof])
    yfun = lambda s: np.interp(s, s_prof, y_prof)

    # ---- material slots
    def mkspec(name, crenel):
        return FortressWallSpec(name, None, outward=1, walk_w=wl['walk_w'], parapet_h=wl['parapet_h'], parapet_t=wl['parapet_t'], crenel=crenel,
                                crenel_gap=wl['crenel_gap'] if crenel < 1e6 else 0.0, batter=wl['batter'], stone=wl['stone'],
                                parapet_mat=wl['parapet_mat'], walk_mat=wl['walk_mat'], max_grade=wl['max_grade'])

    def cross(P, w):
        """fortress_wall's cross-section frame and feet for dense points P (k, 2) on the centreline."""
        d = nz(np.gradient(P, axis=0)); no = np.stack([d[:, 1], -d[:, 0]], -1) * w.outward
        g_o = H(*(P + no * (hw + 2.0)).T); g_i = H(*(P - no * (hw + 1.5)).T)
        return d, no, g_o, g_i

    def end_cap(vis, col, P, y, w, first, parapet=False):
        """capital297_wall.end_cap: close a run end (cross-section polygon), optional parapet end face."""
        i, j = (0, 1) if first else (-1, -2)
        d = nz(P[j] - P[i]) if first else nz(P[i] - P[j])
        no = np.array([d[1], -d[0]]) * w.outward; p = P[i]; yy = float(y[i])
        g_o = float(H(*(p + no * (hw + 2.0)))); g_i = float(H(*(p - no * (hw + 1.5))))
        base_o = g_o - 1.2; base_i = min(g_i, yy) - .8
        X = lambda off, h: [p[0] + no[0] * off, h, p[1] + no[1] * off]
        out = [-d[0], 0, -d[1]] if first else [d[0], 0, d[1]]
        quad = [X(hw + w.batter * (yy - base_o), base_o), X(hw, yy), X(-hw, yy), X(-hw - .08 * (yy - base_i), base_i)]
        if vis is not None:
            vis.poly(quad, w.stone, out, uvscale=2.0)
            if parapet:
                a, b = X(hw, yy), X(hw - w.parapet_t, yy)
                vis.quad(a, b, [b[0], yy + w.parapet_h, b[2]], [a[0], yy + w.parapet_h, a[2]], w.parapet_mat, out, uvscale=1.0)
        if col is not None:
            col.poly(quad, 'c', out)
            if parapet:
                a, b = X(hw, yy), X(hw - w.parapet_t, yy)
                col.quad(a, b, [b[0], yy + w.parapet_h, b[2]], [a[0], yy + w.parapet_h, a[2]], 'c', out)

    def wall_col(name, P, y, w):
        """Collision that matches the visual mass: outer / inner faces, walk, 여장 blocks (no strip above the parapet, no wall across
        the crenel gaps - fortress_wall's own continuous parapet collider is not used)."""
        col = MB(name + '_Collision'); d, no, g_o, g_i = cross(P, w)
        s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))])
        base_o = g_o - 1.2; base_i = np.minimum(g_i, y) - .8
        X = lambda off, yy: np.stack([P[:, 0] + no[:, 0] * off, yy, P[:, 1] + no[:, 1] * off], -1)
        O3 = np.stack([no[:, 0], np.zeros(len(no)), no[:, 1]], -1)
        strip(col, X(hw + w.batter * (y - base_o), base_o), X(hw, y), 'c', O3)
        strip(col, X(-hw - .08 * (y - base_i), base_i), X(-hw, y), 'c', -O3)
        strip(col, X(-hw, y), X(hw, y), 'c', UP)
        ph, pt = w.parapet_h, w.parapet_t; up = np.array([0, ph, 0])
        for a in np.arange(0.0, s[-1], w.crenel):
            b = min(a + w.crenel - w.crenel_gap, s[-1])
            if b - a < .6: continue
            idx = np.where((s >= a) & (s <= b))[0]
            if len(idx) < 2: continue
            Ao, Ai = X(hw, y)[idx], X(hw - pt, y)[idx]
            strip(col, Ao, Ao + up, 'c', O3[idx]); strip(col, Ai, Ai + up, 'c', -O3[idx]); strip(col, Ai + up, Ao + up, 'c', UP)
            for e, sg in ((idx[0], -1), (idx[-1], 1)):
                col.quad(X(hw, y)[e], X(hw - pt, y)[e], X(hw - pt, y)[e] + up, X(hw, y)[e] + up, 'c', np.array([d[e, 0], 0, d[e, 1]]) * sg)
        return col

    stats, feet = {}, []

    def build_run(name, s_a, s_b):
        Lr = s_b - s_a; seg, blk = blocks_of(Lr, wl['crenel'], wl['crenel_gap']); brk = [v for ab in blk for v in ab] + [0.0, Lr]

        def pts(q):
            s = s_a + q; return Wv(s, np.full(len(s), cu)), yfun(s)
        lods = []
        for lod, q, crenel, klod in ((0, samples(0, Lr, 1.5, brk), seg, 0), (1, samples(0, Lr, 3.0, brk), seg, 0), (2, samples(0, Lr, 1.5, [0.0, Lr]), 1e9, 2)):
            P, y = pts(q); w = mkspec(name, crenel); vis, _, info = fortress_wall(w, klod, P, y, 0.0)
            end_cap(vis, None, P, y, w, True, crenel < 1e6); end_cap(vis, None, P, y, w, False, crenel < 1e6)
            lods.append(vis)
        P0, y0 = pts(samples(0, Lr, 1.5, brk)); w0 = mkspec(name, seg); col = wall_col(name, P0, y0, w0)
        end_cap(None, col, P0, y0, w0, True, True); end_cap(None, col, P0, y0, w0, False, True)
        # feet buried (capital check): both feet at least .25 m under the ground where they meet it
        d, no, g_o, g_i = cross(P0, w0); base_o = g_o - 1.2; base_i = np.minimum(g_i, y0) - .8
        fo = P0 + no * (hw + w0.batter * (y0 - base_o))[:, None]; fi = P0 - no * (hw + .08 * (y0 - base_i))[:, None]
        for k in range(len(P0)):
            if base_o[k] > float(H(*fo[k])) - .25 or base_i[k] > float(H(*fi[k])) - .25: feet.append([name, round(float(s_a + (P0[k] - Wv([s_a], [cu])[0]) @ t), 1)])
        stats[name + '_run'] = dict(s=[round(s_a, 2), round(s_b, 2)], length=round(Lr, 2), crenel=round(seg, 3), blocks=len(blk),
                                     walk=[round(float(y0.min()), 2), round(float(y0.max()), 2)], outer=[round(float((y0 - g_o).min()), 2), round(float((y0 - g_o).max()), 2)])
        return lods, col

    def parapet_ring(mb, centre, size_x, size_z, ytop, ph, pt, crenel, gap, mat, lod, rot_yaw, to_w):
        """여장 blocks along the 4 edges of a rectangular top (object-local x/z), placed through to_w (local xz -> world xz)."""
        edges = [((-size_x / 2, size_z / 2 - pt / 2), (size_x / 2, size_z / 2 - pt / 2), 'x'), ((-size_x / 2, -size_z / 2 + pt / 2), (size_x / 2, -size_z / 2 + pt / 2), 'x'),
                 ((-size_x / 2 + pt / 2, -size_z / 2 + pt), (-size_x / 2 + pt / 2, size_z / 2 - pt), 'z'), ((size_x / 2 - pt / 2, -size_z / 2 + pt), (size_x / 2 - pt / 2, size_z / 2 - pt), 'z')]
        for a, b, ax in edges:
            Le = abs(b[0] - a[0]) if ax == 'x' else abs(b[1] - a[1])
            blk = [(0.0, Le)] if lod >= 2 else blocks_of(Le, crenel, gap)[1]
            for p0, p1 in blk:
                m = (p0 + p1) / 2; ln = p1 - p0
                cx, cz = (a[0] + m, a[1]) if ax == 'x' else (a[0], a[1] + m)
                wx, wz = to_w(centre[0] + cx, centre[1] + cz)
                size = [ln, ph, pt] if ax == 'x' else [pt, ph, ln]
                mb.box([wx, ytop + ph / 2, wz], size, mat, yaw=rot_yaw, faces='xXzZY' if mat != 'c' else 'xXzZyY', uvscale=1.0)

    # ---- 끝 치 (end bastions) in world space (MB.box with the frame yaw = local x along t)
    towers = {}

    def tower(key, s_c, y_end, vis_lods, col):
        ss = np.linspace(s_c - tl / 2, s_c + tl / 2, 13); uu = np.linspace(u0, u1, 17); SS, UU = np.meshgrid(ss, uu)
        gf = g_at(Wv(SS.ravel(), UU.ravel()))
        ss2 = np.arange(s_c - tl / 2 - 6, s_c + tl / 2 + 6 + 1e-6, 1.0); uu2 = np.arange(u0 - 3, u1 + 6 + 1e-6, 1.0); S2, U2 = np.meshgrid(ss2, uu2)
        req_t = float(np.max(g_at(Wv(S2.ravel(), U2.ravel())))) + ht['col_above_m']
        base = float(gf.min()) - 1.2; top = max(y_end + tw['rise_over_walk_m'], req_t)
        uc = (u0 + u1) / 2; c = W(s_c, uc); sz = [tl, top - base, u1 - u0]
        to_w = lambda x, z: tuple(W(s_c + x, uc + z))
        for lod, vis in enumerate(vis_lods):
            vis.box([c[0], (base + top) / 2, c[1]], sz, wl['stone'], yaw=yaw, faces='xXzZ', uvscale=2.0)
            vis.box([c[0], top + .06, c[1]], [tl + .2, .12, sz[2] + .2], 'stone_plain', yaw=yaw, faces='xXzZY', uvscale=2.0)
            parapet_ring(vis, (0.0, 0.0), tl, sz[2], top + .12, tw['parapet_h'], tw['parapet_t'], tw['crenel'], tw['crenel_gap'], wl['parapet_mat'], lod, yaw, to_w)
        col.box([c[0], (base + top) / 2 + .03, c[1]], [tl, top - base + .06, sz[2]], 'c', yaw=yaw, faces='xXzZyY')
        parapet_ring(col, (0.0, 0.0), tl, sz[2], top + .12, tw['parapet_h'], tw['parapet_t'], tw['crenel'], tw['crenel_gap'], 'c', 0, yaw, to_w)
        towers[key] = dict(s=[round(s_c - tl / 2, 2), round(s_c + tl / 2, 2)], u=[round(u0, 2), round(u1, 2)], base=round(base, 2), top=round(top, 2),
                           req=round(req_t, 2), ground=[round(float(gf.min()), 2), round(float(gf.max()), 2)], centre=[round(float(c[0]), 3), round(float(c[1]), 3)])
        return top

    # ---- W0 = 치 A + run A, W1 = run B + 치 B (world space, then object-local)
    pieces = {}
    run0, col0 = build_run('P308_W0', tl / 2 - ov, gs0 + ov)
    run1, col1 = build_run('P308_W1', gs1 - ov, L - tl / 2 + ov)
    topA = tower('A', 0.0, float(yfun(tl / 2)), run0, col0)
    topB = tower('B', L, float(yfun(L - tl / 2)), run1, col1)
    piv = {}
    for key, s_p in (('P308_W0', 0.0), ('P308_W1', L)):
        p = W(s_p, cu); piv[key] = np.array([p[0], float(H(*p)), p[1]])
    pieces['P308_W0'] = dict(world_lods=run0, world_col=col0); pieces['P308_W1'] = dict(world_lods=run1, world_col=col1)

    # ---- gatehouse in gate-local coordinates (x = t, z = n, y relative to the pivot ground)
    Og2 = W(s_g, cu); Oy = float(H(*Og2)); Og = np.array([Og2[0], Oy, Og2[1]])
    gl = lambda x, z: float(H(*(Og2 + t * x + n * z)))                     # ground at local (x, z), absolute y
    zz = np.linspace(-dep / 2, dep / 2, 17); xx = np.linspace(-half, half, 9)
    floor_max = max(gl(x, z) for x in xx for z in zz)
    spring = floor_max - Oy + gt['passage_h'] - half; crown = spring + half
    walk_gate = float(np.max(yfun(np.linspace(gs0, gs1, 30)))) - Oy
    S2, U2 = np.meshgrid(np.arange(gs0 - 3, gs1 + 3 + 1e-6, 1.0), np.arange(cu - dep / 2 - 3, cu + dep / 2 + 6 + 1e-6, 1.0))
    req_g = float(np.max(g_at(Wv(S2.ravel(), U2.ravel())))) + ht['col_above_m'] - Oy
    top = max(crown + gt['lintel_min_m'], walk_gate + gt['top_over_walk_min_m'], req_g)
    xs_f = np.linspace(-Wg / 2, Wg / 2, 15); zs_f = np.linspace(-dep / 2, dep / 2, 9)
    base = min(gl(x, z) for x in xs_f for z in zs_f) - Oy - 1.2
    munru = BuildingSpec('munru_3x2', gt['munru_bays_x'], gt['munru_bays_z'], col_h=gt['munru_col_h'], style='military', roof='palzak', platform_h=0.0,
                         platform_margin=0.0, stairs=(), walls=dict(front='rail', back='rail', left='rail', right='rail'), enterable=True)
    if gt['munru'] and (sum(gt['munru_bays_x']) / 2 + .3 > Wg / 2 - gt['top_parapet_t'] or sum(gt['munru_bays_z']) / 2 + .3 > dep / 2 - gt['top_parapet_t']):
        raise SystemExit('munru footprint does not fit inside the gate-top parapet')
    gate_vis = []
    for lod in range(3):
        vis = MB(f'P308_gatehouse_LOD{lod}'); seg = [16, 10, 6][lod]; ang = np.linspace(0, math.pi, seg + 1)
        for sx in (-1, 1):
            vis.box([sx * (half + gt['pier_w'] / 2), (base + top) / 2, 0], [gt['pier_w'], top - base, dep], 'stone_dressed', faces='xXzZY', uvscale=2.0)
        for zf in (-dep / 2, dep / 2):                                   # spandrel fans (front/back) from the arch up to the top
            o = [0, 0, 1 if zf > 0 else -1]
            for a0, a1 in zip(ang[:-1], ang[1:]):
                p0 = [half * math.cos(a0), spring + half * math.sin(a0), zf]; p1 = [half * math.cos(a1), spring + half * math.sin(a1), zf]
                tx = lambda a: (np.sign(math.cos(a)) * half if abs(math.cos(a)) > .7 else half * math.cos(a))
                vis.quad(p0, p1, [tx(a1), top, zf], [tx(a0), top, zf], 'stone_dressed', o, uvscale=2.0)
        for a0, a1 in zip(ang[:-1], ang[1:]):                            # vault underside
            p0 = [half * math.cos(a0), spring + half * math.sin(a0), -dep / 2]; p1 = [half * math.cos(a1), spring + half * math.sin(a1), -dep / 2]
            q0 = [p0[0], p0[1], dep / 2]; q1 = [p1[0], p1[1], dep / 2]
            vis.quad(p0, p1, q1, q0, 'stone_plain', np.array([0, spring, 0]) - (np.array(p0) + np.array(q1)) / 2, uvscale=1.5)
        vis.box([0, top + .06, 0], [Wg + .2, .12, dep + .2], 'stone_plain', faces='xXzZY', uvscale=2.0)
        parapet_ring(vis, (0.0, 0.0), Wg, dep, top + .12, gt['top_parapet_h'], gt['top_parapet_t'], gt['top_crenel'], gt['top_crenel_gap'],
                     wl['parapet_mat'], lod, 0.0, lambda x, z: (x, z))
        if gt['munru']:
            bv, _ = build_building(munru, lod); vis.merge(bv.transformed(0.0, (0.0, top + .12, 0.0)))
        gate_vis.append(vis)
    gate_col = MB('P308_gatehouse_Collision')
    for sx in (-1, 1):
        gate_col.box([sx * (half + gt['pier_w'] / 2), (base + top) / 2, 0], [gt['pier_w'], top - base, dep], 'c', faces='xXzZyY')
    gate_col.box([0, (crown + top) / 2, 0], [gt['passage_w'], top - crown, dep], 'c', faces='xXzZyY')
    # review fix: the 홍예 haunches (between the vault curve and the crown box) are visible stone, so they collide too - the same
    # spandrel fans and vault underside as the visual (gatehouse_arch's crown-up box alone left the haunches walk-through for a 국 jump)
    ang_c = np.linspace(0, math.pi, 17)
    for zf in (-dep / 2, dep / 2):
        o = [0, 0, 1 if zf > 0 else -1]
        for a0, a1 in zip(ang_c[:-1], ang_c[1:]):
            p0 = [half * math.cos(a0), spring + half * math.sin(a0), zf]; p1 = [half * math.cos(a1), spring + half * math.sin(a1), zf]
            tx = lambda a: (np.sign(math.cos(a)) * half if abs(math.cos(a)) > .7 else half * math.cos(a))
            gate_col.quad(p0, p1, [tx(a1), top, zf], [tx(a0), top, zf], 'c', o)
    for a0, a1 in zip(ang_c[:-1], ang_c[1:]):
        p0 = [half * math.cos(a0), spring + half * math.sin(a0), -dep / 2]; p1 = [half * math.cos(a1), spring + half * math.sin(a1), -dep / 2]
        q0 = [p0[0], p0[1], dep / 2]; q1 = [p1[0], p1[1], dep / 2]
        gate_col.quad(p0, p1, q1, q0, 'c', np.array([0, spring, 0]) - (np.array(p0) + np.array(q1)) / 2)
    gate_col.box([0, top + .06, 0], [Wg + .2, .12, dep + .2], 'c', faces='xXzZyY')
    parapet_ring(gate_col, (0.0, 0.0), Wg, dep, top + .12, gt['top_parapet_h'], gt['top_parapet_t'], gt['top_crenel'], gt['top_crenel_gap'], 'c', 0, 0.0, lambda x, z: (x, z))

    # ---- leaves (hinge-local) + blocker / obstacle (gate-local)
    R = half + lv['overlap_m']; th = lv['thick_m']; z_d = dep / 2 + th / 2 + .02; wlf = R - .006
    y_f = gl(0.0, dep / 2) - Oy                                          # floor at the inner mouth (local)
    open_deg = lv['open_degrees']; phis = np.radians(np.arange(0.0, open_deg + 1e-6, lv['sweep_step_deg']))
    top_of = lambda r: spring - y_f + np.sqrt(np.maximum(R * R - (R - r) ** 2, 0.0)) - .02
    leaves, leaf_info = {}, {}
    for key, side in (('L', -1), ('R', 1)):
        hx = side * R; dirx = -side                                      # left hinge at -R extends +x; right at +R extends -x

        def bottom(r):
            pts_ = [(hx + dirx * r * math.cos(p), z_d + r * math.sin(p)) for p in phis] + [(hx + dirx * r, z_d - th / 2), (hx + dirx * r, z_d + th / 2)]
            return min(gl(x, z) for x, z in pts_) - Oy - y_f - lv['bury_m']
        lods, col = [], MB(f'P308_leaf_{key}_Collision')
        for lod in range(3):
            nr = [24, 12, 6][lod]; r_ = np.linspace(0.0, wlf, nr + 1); bot = np.array([bottom(r) for r in r_]); tp = top_of(r_)
            mb = MB(f'P308_leaf_{key}_LOD{lod}')
            targets = [mb] + ([col] if lod == 0 else [])
            for tgt in targets:
                mat = 'wood_board' if tgt is mb else 'c'
                for zf in (-th / 2, th / 2):
                    Pq = np.stack([np.stack([dirx * r_, bot, np.full(len(r_), zf)], -1), np.stack([dirx * r_, tp, np.full(len(r_), zf)], -1)], 1)
                    uv = np.stack([np.repeat((r_ / 1.2)[:, None], 2, 1), Pq[..., 1] / 1.2], -1)
                    tgt.grid(Pq, mat, uv, np.array([0, 0, 1.0 if zf > 0 else -1.0]))
                cen = np.array([dirx * wlf / 2, (bot.min() + tp.max()) / 2, 0.0])
                for edge in (tp, bot):
                    Pq = np.stack([np.stack([dirx * r_, edge, np.full(len(r_), -th / 2)], -1), np.stack([dirx * r_, edge, np.full(len(r_), th / 2)], -1)], 1)
                    tgt.grid(Pq, mat, np.zeros(Pq.shape[:2] + (2,)), lambda Q, c=cen: Q - c)
                for k, sg in ((0, -1), (-1, 1)):
                    x_ = dirx * r_[k]
                    tgt.quad([x_, bot[k], -th / 2], [x_, bot[k], th / 2], [x_, tp[k], th / 2], [x_, tp[k], -th / 2], mat, [dirx * sg, 0, 0])
            spr = spring - y_f; ymin = max(0.45, float(bot.max()) + .3)
            if lod < 2:
                for yb in (ymin, spr * .5, spr * .9):                    # 띠장 on the outer (적로) face, iron bands on the inner face
                    if yb > spr - .1: continue
                    mb.box([dirx * wlf / 2, yb, -th / 2 - .035], [wlf - .06, .16, .07], 'wood_dark', faces='xXyYz', uvscale=1.0)
                    mb.box([dirx * wlf / 2, yb + .2, th / 2 + .012], [wlf - .04, .07, .024], 'iron', faces='xXyYZ', uvscale=1.0)
            if lod == 0:
                for k in range(3):                                        # 돌쩌귀 hinge straps on the inner face
                    yh = ymin + k * (spr - .3 - ymin) / 2
                    mb.box([dirx * .3, yh, th / 2 + .02], [.56, .09, .04], 'iron', faces='xXyYZ', uvscale=1.0)
            lods.append(mb)
        leaves[key] = dict(lods=lods, col=col, hinge=[hx, y_f, z_d], open_sign=side)
        r_ = np.linspace(0.0, wlf, 25); leaf_info[key] = dict(bottom_min=round(float(min(bottom(r) for r in r_)), 3), top_max=round(float(top_of(r_).max()), 3))
    bot_lo = y_f + min(v['bottom_min'] for v in leaf_info.values()) + .02    # the leaves' own buried bottom (inside their mass, under the ground)
    blocker = MB('P308_gate_Collision'); arc = np.linspace(0.0, math.pi, 25)
    outline = [[-R, bot_lo], [R, bot_lo]] + [[R * math.cos(a), spring + R * math.sin(a) - .02] for a in arc]
    for zf in (z_d - th / 2, z_d + th / 2):
        blocker.poly([[x, y, zf] for x, y in outline], 'c', [0, 0, 1 if zf > z_d else -1])
    ring = outline + outline[:1]
    for (x0, y0_), (x1, y1_) in zip(ring[:-1], ring[1:]):
        mid = np.array([(x0 + x1) / 2, (y0_ + y1_) / 2, z_d]); cen = np.array([0.0, (bot_lo + spring) / 2, z_d])
        blocker.quad([x0, y0_, z_d - th / 2], [x1, y1_, z_d - th / 2], [x1, y1_, z_d + th / 2], [x0, y0_, z_d + th / 2], 'c', mid - cen)
    ob_c = [0.0, (bot_lo + spring + R) / 2, z_d]; ob_s = [2 * R, spring + R - bot_lo + 1.0, Wl['obstacle']['thickness_m']]

    # ---- export (object-local)
    MESH = OUTD / 'Meshes'; MESH.mkdir(parents=True, exist_ok=True)
    for f in MESH.glob('*.json'): f.unlink()
    local = {}
    for key in ('P308_W0', 'P308_W1'):
        O = piv[key]; local[key] = dict(lods=[to_local(m, O, t, n, f'{key}_LOD{k}') for k, m in enumerate(pieces[key]['world_lods'])],
                                        col=to_local(pieces[key]['world_col'], O, t, n, f'{key}_Collision'))
    local['P308_gatehouse'] = dict(lods=gate_vis, col=gate_col)
    tris = {}
    for key, v in local.items():
        for k, m in enumerate(v['lods']): m.export(MESH / f'{key}_LOD{k}.json')
        v['col'].export(MESH / f'{key}_Collision.json'); tris[key] = dict(LOD0=v['lods'][0].tris(), LOD1=v['lods'][1].tris(), LOD2=v['lods'][2].tris(), collision=v['col'].tris())
    for key, v in leaves.items():
        for k, m in enumerate(v['lods']): m.export(MESH / f'P308_leaf_{key}_LOD{k}.json')
        v['col'].export(MESH / f'P308_leaf_{key}_Collision.json')
        tris[f'P308_leaf_{key}'] = dict(LOD0=v['lods'][0].tris(), LOD1=v['lods'][1].tris(), LOD2=v['lods'][2].tris(), collision=v['col'].tris())
    blocker.export(MESH / 'P308_gate_Collision.json'); tris['P308_gate_col'] = dict(collision=blocker.tris())

    # ---- checks (world space)
    checks = {}
    gate_w = lambda V: to_world_pts(V, Og, t, n)
    col_world = {'P308_W0': tri_array(pieces['P308_W0']['world_col']), 'P308_W1': tri_array(pieces['P308_W1']['world_col']),
                 'P308_gatehouse': gate_w(tri_array(gate_col))}
    solid = np.concatenate(list(col_world.values()))
    blk_w = gate_w(tri_array(blocker))

    def leaf_world(key, deg):
        v = leaves[key]; Tm = tri_array(v['col']); dd = deg * v['open_sign']      # Unity Euler(0, dd): x' = x cos + z sin, z' = -x sin + z cos
        c, s_ = math.cos(math.radians(dd)), math.sin(math.radians(dd))
        x = Tm[..., 0] * c + Tm[..., 2] * s_; z = -Tm[..., 0] * s_ + Tm[..., 2] * c
        Vg = np.stack([x + v['hinge'][0], Tm[..., 1] + v['hinge'][1], z + v['hinge'][2]], -1); return gate_w(Vg)
    open_leaves = np.concatenate([leaf_world('L', open_deg), leaf_world('R', open_deg)])
    hts = ht['probe_heights_m']
    # (a) seal rays across the wall / 치 / gate piers from the player side (start height = highest player-side ground + h)
    miss, rays = [], 0
    for s in np.arange(-tl / 2 + .3, L + tl / 2 - .3, 1.0):
        if abs(s - s_g) < half + .3: continue
        base_g = band_max(s, u_play)
        for h_ in hts:
            o = np.array([*W(s, u1 + 6.0)]); o3 = np.array([o[0], base_g + h_, o[1]]); e = W(s, u0 - 4.0); d3 = np.array([e[0], base_g + h_, e[1]]) - o3
            rays += 1
            if not ray_hits(solid, o3, d3): miss.append([round(float(s), 1), h_])
    # 치 outer ends (west of A along +t, east of B along -t) on the player side of the seam shells
    for s_c, sgn in ((0.0, 1), (L, -1)):
        for u in (0.5, 2.0, 4.0, u1 - .3):
            ss = np.arange(s_c - sgn * (tl / 2 + 6), s_c - sgn * tl / 2, sgn * 1.0); gmax = float(np.max(g_at(Wv(ss, np.full(len(ss), u)))))
            for h_ in hts:
                p0 = W(s_c - sgn * (tl / 2 + 6), u); p1 = W(s_c, u); rays += 1
                if not ray_hits(solid, np.array([p0[0], gmax + h_, p0[1]]), np.array([p1[0] - p0[0], 0, p1[1] - p0[1]])): miss.append(['end', round(s_c, 1), u, h_])
    checks['seal_rays'] = rays; checks['seal_misses'] = miss
    checks['feet_exposed'] = feet
    # (b) passage closed: rays along -n through the mouth must hit the blocker or the block; (c) open: the road span is clear
    closed_miss, open_hit, prays = [], [], 0
    blk_all = np.concatenate([solid, blk_w])
    open_all = np.concatenate([solid, open_leaves])
    for x in (-half + .4, -1.5, 0.0, 1.5, half - .4):
        for h_ in hts:
            o = gate_w(np.array([[x, y_f + h_, dep / 2 + 6.0]]))[0]; e = gate_w(np.array([[x, y_f + h_, -dep / 2 - 3.0]]))[0]; prays += 1
            if not ray_hits(blk_all, o, e - o): closed_miss.append([round(x, 2), h_])
    road_half = road_w / 2
    xs_road = []                                                         # road corridor edges (gate-local x) along the passage + open-leaf zone
    for z in np.arange(-dep / 2, dep / 2 + R + 1.0 + 1e-6, 0.5):
        uz = cu + z; sc_ = road_s_at(uz) - s_g; xs_road.append((round(float(z), 2), round(sc_ - road_half / 0.98, 3), round(sc_ + road_half / 0.98, 3)))
    for x in (-2.5, 0.0, 2.5):
        for h_ in (0.5, 1.0, 1.6):
            for dx in (-.28, .28):
                o = gate_w(np.array([[x + dx, y_f + h_, dep / 2 + R + 3.0]]))[0]; e = gate_w(np.array([[x + dx, floor_max - Oy + h_, -dep / 2 - 2.0]]))[0]; prays += 1
                if ray_hits(open_all, o, e - o): open_hit.append([x + dx, h_])
    # vault haunches collide (review fix): vertical rays in the passage reach the arch curve (+.08) and stop there, never below it (-.08)
    gc_loc = tri_array(gate_col); vault_bad = []
    for x in (-half + .2, -half * .8, -half * .5, 0.0, half * .5, half * .8, half - .2):
        arch_y = spring + math.sqrt(max(half * half - x * x, 0.0)); y0 = floor_max - Oy + .5
        for z in (-dep / 2 + .3, 0.0, dep / 2 - .3):
            if not ray_hits(gc_loc, np.array([x, y0, z]), np.array([0.0, arch_y + .08 - y0, 0.0])): vault_bad.append([round(x, 2), round(z, 2), 'no hit'])
            if ray_hits(gc_loc, np.array([x, y0, z]), np.array([0.0, arch_y - .08 - y0, 0.0])): vault_bad.append([round(x, 2), round(z, 2), 'below arch'])
    checks['vault_haunch_problems'] = vault_bad
    checks['passage_rays'] = prays; checks['passage_closed_misses'] = closed_miss; checks['passage_open_hits'] = open_hit
    # road vs passage / open leaves (route corridor half width / sin(crossing))
    in_pass = [r for r in xs_road if r[0] <= dep / 2]; in_leaf = [r for r in xs_road if r[0] > dep / 2]
    # open leaves in gate-local: inner x extents (|x| of the leaf nearest to the road)
    def leaf_local_open(key):
        v = leaves[key]; Tm = tri_array(v['col']); dd = open_deg * (1 if v['open_sign'] > 0 else -1)
        c, s_ = math.cos(math.radians(dd)), math.sin(math.radians(dd)); return Tm[..., 0] * c + Tm[..., 2] * s_ + v['hinge'][0]
    lx_L = float(leaf_local_open('L').max()); lx_R = float(leaf_local_open('R').min())
    checks['road_margin_in_passage_m'] = round(min(min(half + r[1], half - r[2]) for r in in_pass), 3)
    checks['road_margin_to_open_leaves_m'] = round(min(min(r[1] - lx_L, lx_R - r[2]) for r in in_leaf), 3)
    checks['open_leaf_inner_x'] = [round(lx_L, 3), round(lx_R, 3)]
    # (d) height rule along the line (walk vs requirement, D305 floor over the player side)
    on_wall = (s_prof >= tl / 2) & (s_prof <= L - tl / 2) & ((s_prof < gs0) | (s_prof > gs1))
    checks['walk_minus_req_min'] = round(float((y_prof - req)[on_wall].min()), 3)
    checks['walk_minus_player_ground_min'] = round(float((y_prof - ps_prof)[on_wall].min()), 3)
    checks['tower_top_minus_req'] = {k: round(v['top'] - v['req'], 3) for k, v in towers.items()}
    checks['gate_top_minus_req'] = round(top - req_g, 3)
    checks['gate_top_minus_walk'] = round(top - walk_gate, 3)
    # (e) joints: the wall cross-section at each run end lies inside the 치 / gate block (u range and height)
    def section_u(s):
        P = Wv([s - .2, s, s + .2], [cu, cu, cu]); y = yfun(np.array([s - .2, s, s + .2])); w = mkspec('x', 3.2)
        d, no, g_o, g_i = cross(P, w); base_o = g_o[1] - 1.2; base_i = min(g_i[1], y[1]) - .8
        return cu - (hw + w.batter * (y[1] - base_o)), cu + hw + .08 * (y[1] - base_i), float(y[1]) + w.parapet_h
    joints = {}
    for name, s_j, lo_u, hi_u, top_j in (('W0@A', tl / 2, u0, u1, towers['A']['top']), ('W0@gate', gs0, cu - dep / 2, cu + dep / 2, top + Oy),
                                         ('W1@gate', gs1, cu - dep / 2, cu + dep / 2, top + Oy), ('W1@B', L - tl / 2, u0, u1, towers['B']['top'])):
        a, b, ytop = section_u(s_j); joints[name] = dict(outer_u=round(a, 2), inner_u=round(b, 2), range_u=[round(lo_u, 2), round(hi_u, 2)], parapet_top=round(ytop, 2),
                                                        host_top=round(top_j, 2), ok=bool(lo_u <= a and b <= hi_u and ytop <= top_j + 1e-6))
    checks['joints'] = joints
    # (f) seam shell ends inside the 치 (segments305 run starts, shell 1 m to the blocked side, 0.6 m thick)
    segs = {s['id']: s for s in json.loads(SEGS.read_text(encoding='utf-8'))['segments']}
    shell = {}
    for key, rid_, s_c in (('A', S['anchors']['A_run'], 0.0), ('B', S['anchors']['B_run'], L)):
        P = np.asarray(segs[rid_]['points'], float); dd = (P[1] - P[0]) / np.hypot(*(P[1] - P[0])); nb = np.array([dd[1], -dd[0]]) * segs[rid_]['block']
        e = P[0] + nb * 1.0; se, ue = float((e - A) @ t), float((e - A) @ n)
        shell[key] = dict(run=rid_, shell_end_su=[round(se, 2), round(ue, 2)], ok=bool(s_c - tl / 2 + .3 <= se <= s_c + tl / 2 - .3 and u0 + .2 <= ue - .3 and ue + .3 <= u1 - .3),
                          run_start_dist_to_anchor=round(float(np.hypot(*(P[0] - (A if key == 'A' else B)))), 3))
    checks['shell_ends_in_towers'] = shell
    # (g) collision inside the visible mass (object-local AABB, 5 cm)
    def aabb(mb):
        V = np.asarray(mb.V, float); return V.min(0), V.max(0)
    inside = {}
    for key, v in local.items():
        (vmn, vmx), (cmn, cmx) = aabb(v['lods'][0]), aabb(v['col']); inside[key] = bool(np.all(cmn >= vmn - .05) and np.all(cmx <= vmx + .05))
    for key, v in leaves.items():
        (vmn, vmx), (cmn, cmx) = aabb(v['lods'][0]), aabb(v['col']); inside[f'P308_leaf_{key}'] = bool(np.all(cmn >= vmn - .05) and np.all(cmx <= vmx + .05))
    lvv = []
    for key, v in leaves.items():
        V = np.asarray(v['lods'][0].V, float) + np.asarray(v['hinge']); lvv.append(V)
    lvv = np.concatenate(lvv); bmn, bmx = aabb(blocker); inside['P308_gate_col'] = bool(np.all(bmn >= lvv.min(0) - .05) and np.all(bmx <= lvv.max(0) + .05))
    checks['collision_inside_visual_aabb'] = inside
    # (h) closure footprint: wing bands clear of the road corridor
    cut = cl['wing_cut_from_gate_m']
    wings = [[W(tl / 2, cu), W(gs0 - cut, cu)], [W(gs1 + cut, cu), W(L - tl / 2, cu)]]

    def seg_d(p, a, b):
        d_ = b - a; tt = np.clip(((p - a) @ d_) / (d_ @ d_), 0, 1); return np.hypot(*(p - (a + d_ * tt[:, None])).T)
    near_d = min(float(np.min(seg_d(dense, np.asarray(a), np.asarray(b)))) for a, b in wings)
    checks['wing_to_road_centreline_min_m'] = round(near_d, 3)
    # (i) materials: every slot exists, no emission
    slots = sorted({m for v in list(local.values()) for mb in v['lods'] for m in mb.T} | {m for v in leaves.values() for mb in v['lods'] for m in mb.T})
    mats, emis = {}, []
    for sl in slots:
        p = A_ / ('Art/World/Finish297/Materials/' + sl + '.mat')
        if not p.exists(): emis.append(f'{sl}: MISSING'); continue
        c, kw = mat_emission(p); mats[sl] = Wl['materials_dir'] + sl + '.mat'
        if c > 0 or kw: emis.append(f'{sl}: emission {c} keyword {kw}')
    checks['materials'] = len(mats); checks['material_problems'] = emis
    # (j) mesh sanity
    bad = []
    for f in sorted(MESH.glob('*.json')):
        d = json.loads(f.read_text(encoding='utf-8'))
        if not np.all(np.isfinite(d['v'])) or not np.all(np.isfinite(d['n'])) or not np.all(np.isfinite(d['uv'])): bad.append(f.name + ' nan')
        if not d['sub'] or sum(len(s['t']) for s in d['sub']) == 0: bad.append(f.name + ' empty')
    checks['mesh_problems'] = bad
    ok = (not miss and not feet and not closed_miss and not open_hit and checks['walk_minus_req_min'] >= -1e-6 and checks['walk_minus_player_ground_min'] >= ht['min_block_m']
          and min(checks['tower_top_minus_req'].values()) >= -1e-6 and checks['gate_top_minus_req'] >= -1e-6 and checks['gate_top_minus_walk'] >= gt['top_over_walk_min_m'] - 1e-6
          and all(j['ok'] for j in joints.values()) and all(s['ok'] for s in shell.values()) and all(inside.values())
          and checks['road_margin_in_passage_m'] >= 0.1 and checks['road_margin_to_open_leaves_m'] >= 0.1 and near_d >= cl['route_clear_min_m'] and not emis and not bad
          and not vault_bad)
    checks['ALL'] = 'PASS' if ok else 'FAIL'

    # ---- manifest (JsonUtility-friendly: flat arrays, camelCase), footprint, plan, ledger
    src = hashlib.sha256()
    for p in (SEAL, LEDGER, SEGS, ROUTES, HEIGHT): src.update(p.read_bytes())
    src = src.hexdigest()[:16]
    V3 = lambda v: [round(float(x), 3) for x in v]
    gpos = V3(Og)
    stations = []
    for s in np.arange(-tl / 2, L + tl / 2 + 1e-6, 2.0):
        c_ = W(s, cu); pi = W(s, cu + hw - .15); kind = 'towerA' if s < tl / 2 else 'towerB' if s > L - tl / 2 else 'gate' if gs0 <= s <= gs1 else 'wall'
        topv = towers['A']['top'] if kind == 'towerA' else towers['B']['top'] if kind == 'towerB' else top + Oy if kind == 'gate' else float(yfun(s))
        stations.append(dict(s=round(float(s), 2), x=round(float(c_[0]), 3), z=round(float(c_[1]), 3), ground=round(float(H(*c_)), 3), top=round(float(topv), 3),
                             probeX=round(float(pi[0]), 3), probeZ=round(float(pi[1]), 3), playerGround=round(band_max(s, u_play), 3), kind=kind))
    manifest = dict(
        version='308.2', kind='wall', compound='Seal308', root='Seal308', generated=time.strftime('%Y-%m-%dT%H:%M:%S'), sourceHash=src, decision='D308-3b',
        spec='SPEC-WORLD-ENCLOSURE-305 §1b′ Seal308 (성벽 + 관문, D308-3b): Seal308 (identity, layer 0) > Wall/P308_W0, P308_W1 (LODGroup + child *_col, static) + '
             'Gate/P308_gatehouse (LODGroup + P308_gatehouse_col, static) + Gate/P308_leaves (WorldSealGate308; P308_leaf_L/R hinges with LODs + open *_col; '
             'P308_gate_col closed blocker; P308_gate_ob carve) - blockers, obstacle and open colliders authored DISABLED (edit-time bake = open pass)',
        frameNote='mesh vertices are object-local: x = t (A->B), y up, z = n (상경 가도 side); place each object at `position` with Quaternion.Euler(0, yaw, 0); '
                  'leaf meshes are hinge-local (hinge = leaves.left/right.hinge in the P308_leaves frame)',
        requiredFact=S['required_fact'], meshDir='Art/World/Compact/Rebuild/Enclosure305/Out/Seal308/Meshes', assetDir='Assets/_Project/Art/World/Enclosure305/Seal308',
        lodScreenHeights=[.16, .045, .007], checksAll=checks['ALL'], minBlock=ht['min_block_m'], colAbove=ht['col_above_m'],
        probeHeights=hts,
        materials=[dict(slot=k, path=v) for k, v in sorted(mats.items())],
        pieces=[dict(name='P308_W0', kind='wall', parent='Wall', position=V3(piv['P308_W0']), yaw=round(yaw, 4), lods=3, collider='P308_W0_col', isStatic=True,
                     note='end 치 A + wall run A -> gate'),
                dict(name='P308_W1', kind='wall', parent='Wall', position=V3(piv['P308_W1']), yaw=round(yaw, 4), lods=3, collider='P308_W1_col', isStatic=True,
                     note='wall run gate -> B + end 치 B'),
                dict(name='P308_gatehouse', kind='gatehouse', parent='Gate', position=gpos, yaw=round(yaw, 4), lods=3, collider='P308_gatehouse_col', isStatic=True,
                     note='육축 piers + 홍예 vault + top 여장' + (' + 문루 (no collider, unreachable)' if gt['munru'] else ''))],
        leaves=dict(name='P308_leaves', parent='Gate', position=gpos, yaw=round(yaw, 4), openDegrees=open_deg, duration=lv['duration_s'],
                    left=dict(name='P308_leaf_L', hinge=V3(leaves['L']['hinge']), lods=3, openSign=-1, collider='P308_leaf_L_col'),
                    right=dict(name='P308_leaf_R', hinge=V3(leaves['R']['hinge']), lods=3, openSign=1, collider='P308_leaf_R_col'),
                    blocker='P308_gate_col', blockerMesh='P308_gate_Collision',
                    obstacle=dict(name='P308_gate_ob', center=V3(ob_c), size=V3(ob_s))),
        frame=dict(a=V3(A), t=V3(t), n=V3(n), length=round(L, 3), centreU=cu, walkHalf=hw, innerProbeU=round(cu + hw - .15, 3), playerFromU=round(cu + hw + .8, 3),
                   playerToU=round(cu + hw + .8 + ht['player_side_m'], 3), along=ht['along_m'], sMin=-tl / 2, sMax=round(L + tl / 2, 3), gateS=round(s_g, 3),
                   gateS0=round(gs0, 3), gateS1=round(gs1, 3), passageHalf=half, gateDepth=dep, floorInner=round(y_f + Oy, 3), towerUMin=u0, towerUMax=round(u1, 3)),
        stations=stations,
        gate=dict(top=round(top + Oy, 3), crown=round(crown + Oy, 3), spring=round(spring + Oy, 3), floorMax=round(floor_max, 3), base=round(base + Oy, 3),
                  passage=[gt['passage_w'], gt['passage_h']], width=Wg, depth=dep, munru=gt['munru']),
        towers=towers, runs={k: v for k, v in stats.items()}, leafInfo=leaf_info, tris=tris, checks=checks)
    # idempotent rerun (review fix): unchanged inputs and results keep the previous `generated` stamp, so unity.json (and the seal-scene
    # ledger's input hash in all three scenes) stays byte-identical instead of changing with the clock
    prev = OUTD / 'unity.json'
    if prev.exists():
        try:
            old = json.loads(prev.read_text(encoding='utf-8'))
            if {k: v for k, v in old.items() if k != 'generated'} == json.loads(json.dumps({k: v for k, v in manifest.items() if k != 'generated'}, ensure_ascii=False)):
                manifest['generated'] = old.get('generated', manifest['generated'])
        except (ValueError, OSError):
            pass
    prev.write_text(json.dumps(manifest, ensure_ascii=False, indent=1), encoding='utf-8')
    tower_rect = lambda s_c: [W(s_c - tl / 2, u0).round(3).tolist(), W(s_c + tl / 2, u0).round(3).tolist(), W(s_c + tl / 2, u1).round(3).tolist(), W(s_c - tl / 2, u1).round(3).tolist()]
    foot = dict(version='308.2', kind='wall', line=[A.round(3).tolist(), B.round(3).tolist()],
                wings=[[np.asarray(a).round(3).tolist(), np.asarray(b).round(3).tolist()] for a, b in wings],
                bastions=[[*W(0.0, (u0 + u1) / 2).round(3).tolist(), round(max(tl, u1 - u0) / 2 * .9, 2)], [*W(L, (u0 + u1) / 2).round(3).tolist(), round(max(tl, u1 - u0) / 2 * .9, 2)]],
                panel_closed=[W(gs0 - cut - .5, cu).round(3).tolist(), W(gs1 + cut + .5, cu).round(3).tolist()], panel_open=[],
                wall_physical=[[W(tl / 2 - ov, cu).round(3).tolist(), W(gs0 + ov, cu).round(3).tolist()], [W(gs1 - ov, cu).round(3).tolist(), W(L - tl / 2 + ov, cu).round(3).tolist()]],
                gate_block=[W(gs0, cu - dep / 2).round(3).tolist(), W(gs1, cu - dep / 2).round(3).tolist(), W(gs1, cu + dep / 2).round(3).tolist(), W(gs0, cu + dep / 2).round(3).tolist()],
                towers=[tower_rect(0.0), tower_rect(L)],
                note='world (x, z). seal308_closure.py reads wings / bastions / panel_closed / panel_open: wings = wall runs on the centreline (u = %.1f) stopped %.1f m short '
                     'of the gate block (lattice band clear of the road corridor), bastions = end 치, panel_closed = the gate span (solid only while closed; the leaves swing '
                     'in place, nothing parks). wall_physical / gate_block / towers = the real outlines (information).' % (cu, cut))
    (OUTD / 'footprint308.json').write_text(json.dumps(foot), encoding='utf-8')
    plan(A, t, n, L, cu, tl, u0, u1, gs0, gs1, s_g, half, dep, R, dense, wings, foot, leaves, Og, open_deg, segs, gate_w, leaf_world)
    lines = [f"#308 Seal308 관문 성벽 (D308-3b) | {manifest['generated']} | hash {src} | TEST, offline height.bytes (seal-scene / seal-check re-measure the physics ground)",
             f"line A {A.round(2).tolist()} -> B {B.round(2).tolist()} length {L:.2f} m yaw {yaw:.3f}; wall centreline u = {cu} m (north); road crosses it at s = {s_g:.2f}",
             f"gate block s {gs0:.2f}..{gs1:.2f} (width {Wg:.1f}, depth {dep}), passage {gt['passage_w']} x {gt['passage_h']} m, floor max {floor_max:.2f}, spring {spring + Oy:.2f}, crown {crown + Oy:.2f}, top {top + Oy:.2f}, base {base + Oy:.2f}" + (', 문루 3x2 on top' if gt['munru'] else ''),
             f"walk {y_prof[on_wall].min():.2f}..{y_prof[on_wall].max():.2f}; 치 A top {towers['A']['top']} (ground {towers['A']['ground']}), 치 B top {towers['B']['top']} (ground {towers['B']['ground']})",
             f"leaves R {R:.2f} m, hinge z {z_d:.2f}, inner-mouth floor {y_f + Oy:.2f}, open {open_deg} deg in {lv['duration_s']} s; bottom edge min {json.dumps(leaf_info)}; obstacle centre {V3(ob_c)} size {V3(ob_s)} (gate-local)",
             f"runs {json.dumps(stats, ensure_ascii=False)}",
             f"towers {json.dumps(towers, ensure_ascii=False)}",
             f"tris {json.dumps(tris)}",
             'checks ' + json.dumps(checks, ensure_ascii=False)]
    (OUTD / 'wall308.txt').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print('\n'.join(lines[:5])); print('ALL', checks['ALL'])
    for k in ('seal_misses', 'feet_exposed', 'passage_closed_misses', 'passage_open_hits', 'walk_minus_req_min', 'walk_minus_player_ground_min', 'tower_top_minus_req',
              'gate_top_minus_req', 'gate_top_minus_walk', 'vault_haunch_problems', 'road_margin_in_passage_m', 'road_margin_to_open_leaves_m', 'wing_to_road_centreline_min_m', 'material_problems', 'mesh_problems'):
        print(' ', k, json.dumps(checks[k], ensure_ascii=False) if not isinstance(checks[k], list) or len(checks[k]) < 12 else f'{len(checks[k])} items, first {checks[k][:6]}')
    print('  joints', json.dumps(joints)); print('  shells', json.dumps(shell, ensure_ascii=False)); print('  inside', json.dumps(inside))
    return ok


def plan(A, t, n, L, cu, tl, u0, u1, gs0, gs1, s_g, half, dep, R, road, wings, foot, leaves, Og, open_deg, segs, gate_w, leaf_world):
    from PIL import Image, ImageDraw, ImageFont
    sc = 11.0; ctr = A + t * (L / 2) + n * 3.0; size = (980, 600)
    img = Image.new('RGB', size, (240, 236, 226)); d = ImageDraw.Draw(img)
    try: font = ImageFont.truetype('malgun.ttf', 12)
    except OSError: font = None
    P = lambda xz: (size[0] / 2 + (xz[0] - ctr[0]) * sc, size[1] / 2 - (xz[1] - ctr[1]) * sc)
    W = lambda s, u: A + t * s + n * u
    rd = road[np.hypot(road[:, 0] - ctr[0], road[:, 1] - ctr[1]) < 60]
    for p in rd[::4]: x, y = P(p); r = 3.0 * sc; d.ellipse([x - r, y - r, x + r, y + r], fill=(222, 214, 196))
    for p in rd[::2]: d.point(P(p), fill=(120, 100, 70))
    for s in segs.values():
        Q = np.asarray(s['points'], float)
        if np.min(np.hypot(Q[:, 0] - ctr[0], Q[:, 1] - ctr[1])) > 80: continue
        d.line([P(q) for q in Q], fill=(170, 30, 30), width=2)
        nb = np.array([Q[1, 1] - Q[0, 1], -(Q[1, 0] - Q[0, 0])]); nb = nb / np.hypot(*nb) * s['block']
        d.line([P(q + nb) for q in Q], fill=(60, 60, 60), width=1)
    poly = lambda pts, **k: d.polygon([P(p) for p in pts], **k)
    for s_c in (0.0, L): poly([W(s_c - tl / 2, u0), W(s_c + tl / 2, u0), W(s_c + tl / 2, u1), W(s_c - tl / 2, u1)], fill=(150, 146, 138), outline=(60, 60, 60))
    for a, b in foot['wall_physical']:
        sa, sb = (np.asarray(a) - A) @ t, (np.asarray(b) - A) @ t
        poly([W(sa, cu - 1.6 - .9), W(sb, cu - 1.6 - .9), W(sb, cu + 1.6 + .7), W(sa, cu + 1.6 + .7)], fill=(170, 166, 158), outline=(80, 80, 80))
    poly([W(gs0, cu - dep / 2), W(gs1, cu - dep / 2), W(gs1, cu + dep / 2), W(gs0, cu + dep / 2)], fill=(130, 126, 120), outline=(40, 40, 40))
    poly([W(s_g - half, cu - dep / 2), W(s_g + half, cu - dep / 2), W(s_g + half, cu + dep / 2), W(s_g - half, cu + dep / 2)], fill=(222, 214, 196))
    for key in ('L', 'R'):
        hx, _, hz = leaves[key]['hinge']; dirx = -leaves[key]['open_sign']; wl_ = R - .006
        for deg, col in ((0.0, (30, 80, 160)), (open_deg, (30, 140, 200))):
            a = math.radians(deg); e = gate_w(np.array([[hx + dirx * wl_ * math.cos(a), 0.0, hz + wl_ * math.sin(a)], [hx, 0.0, hz]]))
            d.line([P(e[1, [0, 2]]), P(e[0, [0, 2]])], fill=col, width=3)
    for a, b in foot['wings']: d.line([P(a), P(b)], fill=(230, 120, 0), width=1)
    d.line([P(foot['panel_closed'][0]), P(foot['panel_closed'][1])], fill=(230, 60, 0), width=1)
    d.text((10, 8), 'Seal308 관문 성벽 plan (11 px/m): grey = wall runs / end 치 / gate block, beige = passage + road, dark blue = closed leaves, light blue = open leaves,',
           fill=(0, 0, 0), font=font)
    d.text((10, 24), 'red = seam run lines, dark grey = their shells (1 m to the blocked side), orange = closure footprint (wings / closed panel)', fill=(0, 0, 0), font=font)
    d.text(P(A + n * 9), 'A (E305_084)', fill=(0, 0, 0), font=font); d.text(P(A + t * L + n * 9), 'B (E305_056)', fill=(0, 0, 0), font=font)
    d.text(P(W(s_g, 18)), 'N: 상경 가도 (EA)', fill=(0, 0, 0), font=font); d.text(P(W(s_g - 6, -10)), 'S: 적로', fill=(0, 0, 0), font=font)
    img.save(OUTD / 'plan308.png')


if __name__ == '__main__':
    sys.exit(0 if main() else 1)
