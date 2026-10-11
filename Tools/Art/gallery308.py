"""#308 cliff way on the terrain - `gallery308`. Offline, deterministic. SPEC-WORLD-CLIFFWAY-308 "n1 단계".
The old way stood on a mesh wall up to 43 m in front of the slope. Here the same bearings and heights are kept and the line
is moved back onto the terrain face; stone stretches become a terrain ledge, wood stretches a plank gallery pinned to the face.

  python Tools/Art/gallery308.py route <cfg.json>     # face height -> plan/gallery308_route.json (centreline, sections, ledge lines)
  python Tools/Art/gallery308.py build <cfg.json>     # final height -> Art/World/Compact/Rebuild/Gallery308/{Meshes/*.json, layout.json}

Order: rugged308 faces -> gallery308 route -> rugged308 (ledges) -> gallery308 build.
"""
import json, math, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402

OUT = B.RB / 'Gallery308'
ROUTE = B.PL / 'gallery308_route.json'


def smooth(a, n):
    if n <= 1: return a
    k = np.ones(n) / n; p = n // 2; return np.convolve(np.pad(a, (p, n - 1 - p), mode='edge'), k, mode='valid')


def route(cfg):
    S = json.loads((B.ROOT / cfg['inputs']['stations']).read_text(encoding='utf-8')); f = B.load_height(B.ROOT / cfg['inputs']['face_height'])
    rc = cfg['route']; cx, cz = S['cx'], S['cz']; th = np.asarray(S['th']); rp = np.asarray(S['rp']); y = np.asarray(S['y']); s = np.asarray(S['s'])
    r = np.empty(len(th)); y = y.copy(); g = math.tan(math.radians(rc['max_grade_deg']))
    for _ in range(4):            # the moved line is longer in places and shorter in others: re-grade, then find the face again
        for k in range(len(th)):  # first radius, going away from the cove centre, where the tile surface reaches the way height
            rs = np.arange(rp[k] - rc['search_in_m'], rp[k] + rc['search_out_m'], 0.25)
            hh = B.sample_tri(f, cx + np.cos(th[k]) * rs, cz + np.sin(th[k]) * rs); hit = np.flatnonzero(hh >= y[k])
            if not len(hit): raise SystemExit(f'REFUSED: station {k} finds no ground at {y[k]:.1f} m within {rc["search_out_m"]} m')
            r[k] = rs[hit[0]]
        r = smooth(r, int(rc['smooth_m'] / rc['step_m']) | 1)
        x = cx + np.cos(th) * r; z = cz + np.sin(th) * r
        ds = np.hypot(np.diff(x), np.diff(z)); sn = np.concatenate([[0], np.cumsum(ds)])      # arclength of the moved line
        lo = y.copy()
        for k in range(1, len(lo)): lo[k] = min(lo[k], lo[k - 1] + g * ds[k - 1])
        for k in range(len(lo) - 2, -1, -1): lo[k] = min(lo[k], lo[k + 1] + g * ds[k])
        y = smooth(lo, 9)
    secs = []
    for sc in cfg['sections']:
        m = (sn >= sc['from_m']) & (sn <= sc['to_m'])
        if m.sum() >= 2: secs.append(dict(kind=sc['kind'], i0=int(np.flatnonzero(m)[0]), i1=int(np.flatnonzero(m)[-1])))
    wet = B.wet(); pro = B.protected(); j = np.rint(x / 4).astype(int); i = np.rint(z / 4).astype(int)
    out = dict(version=cfg['version'], cx=cx, cz=cz, x=x.round(3).tolist(), z=z.round(3).tolist(), y=y.round(3).tolist(), s=sn.round(3).tolist(), th=th.tolist(),
               moved_back_m=dict(min=round(float((r - rp).min()), 1), median=round(float(np.median(r - rp)), 1), max=round(float((r - rp).max()), 1)),
               length_m=round(float(sn[-1]), 1), old_length_m=round(float(s[-1]), 1), wet_cells=int(wet[i, j].sum()), protected_cells=int(pro[i, j].sum()), sections=secs,
               ledges=[dict(id=f'cove_stone_{n}', half_width_m=cfg['ledge']['half_width_m'], points=[[round(float(x[k]), 3), round(float(z[k]), 3), round(float(y[k]), 3)] for k in range(q['i0'], q['i1'] + 1, 2)])
                       for n, q in enumerate(secs) if q['kind'] == 'stone'])
    if out['wet_cells'] or out['protected_cells']: raise SystemExit(f"REFUSED: the moved line crosses {out['wet_cells']} wet / {out['protected_cells']} protected cells")
    B.write_json(ROUTE, out, indent=None)
    print(f"gallery308 route: {out['length_m']} m (was {out['old_length_m']}), moved back {out['moved_back_m']}, sections {[(q['kind'], round(sn[q['i0']]), round(sn[q['i1']])) for q in secs]} -> {B.rel(ROUTE)}")


# ------------------------------------------------------------------ meshes
class Mesh:
    def __init__(self, name): self.name = name; self.v = []; self.n = []; self.uv = []; self.sub = {}

    def box(self, c, ax, ay, az, size, mat):
        """oriented box: centre c, unit axes, full size (sx, sy, sz)."""
        c = np.asarray(c, float); A = [np.asarray(a, float) * h / 2 for a, h in zip((ax, ay, az), size)]; tri = self.sub.setdefault(mat, [])
        for d, (u, w) in ((0, (1, 2)), (1, (2, 0)), (2, (0, 1))):
            for sgn in (1, -1):
                nrm = A[d] / (np.linalg.norm(A[d]) + 1e-12) * sgn; o = c + A[d] * sgn; b = len(self.v)
                for su, sw in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                    p = o + A[u] * su * sgn + A[w] * sw; self.v.append(p); self.n.append(nrm)
                    self.uv.append(((su + 1) / 2 * size[u], (sw + 1) / 2 * size[w]))
                tri += [b, b + 2, b + 1, b, b + 3, b + 2]

    def beam(self, a, b, up, size, mat):
        """box from a to b with section (w, h); `up` fixes the roll."""
        a = np.asarray(a, float); b = np.asarray(b, float); L = np.linalg.norm(b - a)
        if L < 1e-4: return
        t = (b - a) / L; side = np.cross(up, t); side /= np.linalg.norm(side) + 1e-12; u = np.cross(t, side)
        self.box((a + b) / 2, t, u, side, (L, size[1], size[0]), mat)

    def log(self, a, b, r0, r1, mat, sides=6, rng=None, wob=0.0):
        """rough round timber from a to b: a `sides`-gon prism, radius r0 -> r1, each ring vertex pushed in or out by up to wob."""
        a = np.asarray(a, float); b = np.asarray(b, float); L = np.linalg.norm(b - a)
        if L < 1e-4: return
        t = (b - a) / L; ref = np.array([0.0, 1.0, 0.0]) if abs(t[1]) < 0.9 else np.array([1.0, 0.0, 0.0])
        u = np.cross(ref, t); u /= np.linalg.norm(u); w = np.cross(t, u); tri = self.sub.setdefault(mat, [])
        ang = np.linspace(0, 2 * math.pi, sides, endpoint=False) + (rng.uniform(0, 1) if rng is not None else 0.0)
        ring = lambda c, r: [c + (u * math.cos(q) + w * math.sin(q)) * r * (1 + (rng.uniform(-wob, wob) if rng is not None and wob > 0 else 0.0)) for q in ang]
        A_ = ring(a, r0); B_ = ring(b, r1)
        for k in range(sides):
            k2 = (k + 1) % sides; q = [A_[k], A_[k2], B_[k2], B_[k]]; nrm = np.cross(q[1] - q[0], q[3] - q[0]); nrm /= np.linalg.norm(nrm) + 1e-12
            if np.dot(nrm, (q[0] + q[2]) / 2 - (a + b) / 2) < 0: q = [q[1], q[0], q[3], q[2]]; nrm = -nrm
            base = len(self.v)
            for i_, pt in enumerate(q): self.v.append(pt); self.n.append(nrm); self.uv.append(((i_ in (1, 2)) * r0 * 2, (i_ >= 2) * L))
            tri += [base, base + 2, base + 1, base, base + 3, base + 2]
        for c, R_, nrm in ((a, A_, -t), (b, B_, t)):      # end caps
            base = len(self.v); self.v.append(c); self.n.append(nrm); self.uv.append((0.0, 0.0))
            for pt in R_: self.v.append(pt); self.n.append(nrm); self.uv.append((0.1, 0.1))
            for k in range(sides):
                i1, i2 = base + 1 + k, base + 1 + (k + 1) % sides
                tri += ([base, i1, i2] if np.dot(np.cross(R_[k] - c, R_[(k + 1) % sides] - c), nrm) > 0 else [base, i2, i1])

    def slab(self, c, t, side, rx, rz, th, mat, rng, tilt=0.05, sides=(5, 8)):
        """a rough flagstone: an uneven convex polygon (rx along t, rz along side), th thick, its top tilted a little; top face + skirt."""
        c = np.asarray(c, float); n = int(rng.integers(sides[0], sides[1] + 1)); ang = np.sort(rng.uniform(0, 2 * math.pi, n)); up = np.array([0.0, 1.0, 0.0])
        nt = up + t * rng.uniform(-tilt, tilt) + side * rng.uniform(-tilt, tilt); nt /= np.linalg.norm(nt); tri = self.sub.setdefault(mat, [])
        ring = [c + t * math.cos(q) * rx * rng.uniform(0.72, 1.0) + side * math.sin(q) * rz * rng.uniform(0.72, 1.0) for q in ang]
        ring = [pt - nt * np.dot(pt - c, nt) + up * 0.0 for pt in ring]      # on the tilted top plane
        base = len(self.v); self.v.append(c); self.n.append(nt); self.uv.append((0.5, 0.5))
        for pt in ring: self.v.append(pt); self.n.append(nt); d_ = pt - c; self.uv.append((0.5 + np.dot(d_, t), 0.5 + np.dot(d_, side)))
        for k in range(n):
            i1, i2 = base + 1 + k, base + 1 + (k + 1) % n
            tri += ([base, i1, i2] if np.dot(np.cross(ring[k] - c, ring[(k + 1) % n] - c), nt) > 0 else [base, i2, i1])
        for k in range(n):
            p0, p1 = ring[k], ring[(k + 1) % n]; q0, q1 = p0 - up * th, p1 - up * th; nrm = np.cross(p1 - p0, q0 - p0); nrm /= np.linalg.norm(nrm) + 1e-12
            quad = [p0, p1, q1, q0]
            if np.dot(nrm, (p0 + p1) / 2 - c) < 0: quad = [p1, p0, q0, q1]; nrm = -nrm
            b0 = len(self.v)
            for i_, pt in enumerate(quad): self.v.append(pt); self.n.append(nrm); self.uv.append((float(i_ in (1, 2)), float(i_ >= 2) * th))
            tri += [b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2]

    def dump(self):
        v = np.asarray(self.v, float); n = np.asarray(self.n, float); uv = np.asarray(self.uv, float)
        return dict(name=self.name, vertices=v.round(4).ravel().tolist(), normals=n.round(4).ravel().tolist(), uvs=uv.round(4).ravel().tolist(),
                    submeshes=[dict(material=m, triangles=t) for m, t in self.sub.items()])


def build(cfg):
    R = json.loads(ROUTE.read_text(encoding='utf-8')); f = B.load_height(B.ROOT / cfg['inputs']['final_height'])
    x = np.asarray(R['x']); z = np.asarray(R['z']); y = np.asarray(R['y']); s = np.asarray(R['s']); cx, cz = R['cx'], R['cz']
    P = np.stack([x, y, z], 1); tan = np.gradient(P, axis=0); tan /= np.linalg.norm(tan, axis=1, keepdims=True)
    inn = np.stack([x - cx, np.zeros(len(x)), z - cz], 1); inn /= np.linalg.norm(inn, axis=1, keepdims=True)      # into the mountain
    up = np.array([0.0, 1.0, 0.0]); M = cfg['materials']; W_ = cfg['wood']; SR = cfg['stone_rail']
    deck = Mesh('Gallery_Deck'); frame = Mesh('Gallery_Frame'); rail = Mesh('Gallery_Rail'); col = Mesh('Gallery_Collision')
    at = lambda sv: (np.array([np.interp(sv, s, P[:, c]) for c in range(3)]), np.array([np.interp(sv, s, tan[:, c]) for c in range(3)]), np.array([np.interp(sv, s, inn[:, c]) for c in range(3)]))
    ground = lambda p: float(B.sample_tri(f, p[0], p[2]))
    stats = dict(wood_m=0.0, stone_m=0.0, bays=0, posts=0, chain_posts=0, deck_gap_max_m=0.0, brace_foot_gap_max_m=0.0, rail_gaps_m=0.0)
    rng = np.random.default_rng(int(cfg.get('seed', 308))); J = cfg['irregular']; U = lambda lo_hi: float(rng.uniform(lo_hi[0], lo_hi[1]))
    for q in R['sections']:
        s0, s1 = s[q['i0']], s[q['i1']]
        if q['kind'] == 'wood':
            a0, a1 = s0 - W_['overlap_m'], s1 + W_['overlap_m']; st = [a0]
            while st[-1] < a1 - J['bay_m'][0]: st.append(min(a1, st[-1] + U(J['bay_m'])))      # bays of uneven length
            if a1 - st[-1] > 0.3: st.append(a1)
            st = np.asarray(st); nb = len(st) - 1; stats['wood_m'] += float(a1 - a0); prev_post = None
            for b in range(nb + 1):
                p, t, n_ = at(st[b]); t = t / np.linalg.norm(t); n_ = n_ / np.linalg.norm(n_); out = -n_
                inner = p.copy()                      # the deck's inner edge sits on the face: walk the bearing until the tile surface is at deck height
                for dd in np.arange(-4.0, 4.01, 0.05):
                    c = p + n_ * dd
                    if ground(c) >= p[1] - 0.02: inner = c; break
                inner[1] = p[1]; inner = inner + n_ * W_['inset_m']; outer = inner + out * W_['deck_w']
                if b:
                    mid_i = (inner + prev_i) / 2; mid_o = (outer + prev_o) / 2; c = (mid_i + mid_o) / 2; tt = (inner + outer) / 2 - (prev_i + prev_o) / 2; L = np.linalg.norm(tt); tt /= L
                    side = np.cross(up, tt); side /= np.linalg.norm(side); u2 = np.cross(tt, side)
                    col.box(c - u2 * W_['deck_t'] / 2, tt, u2, side, (L + 0.02, W_['deck_t'], W_['deck_w']), M['deck'])      # the walked surface stays one even slab
                    # drawn deck: single planks laid across, each its own width, length, skew and height; never above the walked surface
                    x_ = -L / 2
                    while x_ < L / 2 - 0.05:
                        wpl = min(U(J['plank_w']), L / 2 - x_); gap = U(J['plank_gap']); cx_ = x_ + wpl / 2; x_ += wpl + gap
                        over = U(J['plank_over']); skew = math.radians(U(J['plank_skew_deg'])); drop = U(J['plank_drop'])
                        pt = tt * math.cos(skew) + side * math.sin(skew); ps = np.cross(up, pt); ps /= np.linalg.norm(ps); pu = np.cross(pt, ps)
                        pc = c + tt * cx_ + side * (over / 2 * np.sign(np.dot(side, out)))
                        deck.box(pc - pu * (W_['deck_t'] / 2 + drop), pt, pu, ps, (wpl, W_['deck_t'], W_['deck_w'] + over), M['deck']); stats['planks'] = stats.get('planks', 0) + 1
                        if rng.uniform() < J['patch_share']:      # a patch board nailed over a bad one, askew
                            sk2 = skew + math.radians(U(J['patch_skew_deg'])); pt2 = tt * math.cos(sk2) + side * math.sin(sk2); ps2 = np.cross(up, pt2); ps2 /= np.linalg.norm(ps2)
                            deck.box(pc - pu * (drop + 0.004) + ps * U([-0.3, 0.3]), pt2, np.cross(pt2, ps2), ps2, (wpl * 0.8, 0.02, W_['deck_w'] * U([0.45, 0.75])), M['frame'])
                    # two stringers under the planks, round timber
                    for off, r_ in ((0.22, U(J['stringer_r'])), (W_['deck_w'] - 0.2, U(J['stringer_r']))):
                        frame.log(prev_i + (prev_o - prev_i) * off / W_['deck_w'] - up * (W_['deck_t'] + r_ + 0.01) - tt * 0.15, inner + (outer - inner) * off / W_['deck_w'] - up * (W_['deck_t'] + r_ + 0.01) + tt * 0.15, r_, r_ * U([0.82, 1.0]), M['frame'], rng=rng, wob=0.08)
                    stats['deck_gap_max_m'] = max(stats['deck_gap_max_m'], abs(ground(mid_i - n_ * W_['inset_m']) - mid_i[1]))
                # joist into the rock (uneven reach), brace down to the face (not at every joist), post on the outer edge (leaning, some gone)
                jr = U(J['joist_r']); jy = up * (W_['deck_t'] + 2 * U(J['stringer_r']) + jr)
                frame.log(inner + n_ * U(J['joist_embed']) - jy, outer + out * U(J['joist_over']) - jy, jr, jr * U([0.8, 1.0]), M['frame'], rng=rng, wob=0.1)
                if rng.uniform() < J['brace_share']:
                    foot = None; dropm = U(J['brace_drop'])
                    for dd in np.arange(0.0, 7.0, 0.05):
                        c = outer + n_ * dd - up * dropm
                        if ground(c) >= c[1]: foot = c; break
                    if foot is not None:
                        br = U(J['brace_r']); frame.log(outer - jy * 1.1 + t * U([-0.25, 0.25]), foot + n_ * 0.35, br, br * 0.85, M['frame'], rng=rng, wob=0.1); stats['braces'] = stats.get('braces', 0) + 1
                post = None
                if rng.uniform() < J['post_share'] or b in (0, nb):
                    hgt = U(J['post_h']); lean = math.radians(U(J['post_lean_deg'])); la = rng.uniform(0, 2 * math.pi)
                    axis = up * math.cos(lean) + (out * math.cos(la) * 0.6 + t * math.sin(la)) * math.sin(lean); axis /= np.linalg.norm(axis); pr = U(J['post_r'])
                    post = outer + axis * hgt; rail.log(outer - up * 0.3, post + axis * U([0.02, 0.14]), pr, pr * 0.8, M['frame'], rng=rng, wob=0.1); stats['posts'] += 1
                if b and post is not None and prev_post is not None and np.linalg.norm(post - prev_post) < J['bay_m'][1] * 1.7:
                    if rng.uniform() < J['top_rail_share']:
                        rr_ = U(J['rail_r']); rail.log(prev_post - tt * 0.12, post + tt * 0.12, rr_, rr_ * U([0.75, 1.0]), M['frame'], rng=rng, wob=0.1)
                        lo_ = min(prev_post[1], post[1]) - (prev_o[1] + outer[1]) / 2      # collider only where a rail is drawn, no higher than the lower post
                        ro = (prev_o + outer) / 2 + up * lo_ / 2; rt = outer - prev_o; rl = np.linalg.norm(rt); rt /= rl
                        col.box(ro, rt, up, np.cross(rt, up), (rl, lo_, 0.06), M['frame'])
                        if rng.uniform() < J['mid_rail_share']:
                            f1, f2 = U([0.4, 0.6]), U([0.4, 0.6]); rr2 = U(J['rail_r']) * 0.85
                            rail.log(prev_o + (prev_post - prev_o) * f1, outer + (post - outer) * f2, rr2, rr2 * 0.85, M['frame'], rng=rng, wob=0.1)
                    else: stats['rail_gaps_m'] += float(np.linalg.norm(post - prev_post))
                elif b: stats['rail_gaps_m'] += float(st[b] - st[b - 1])
                prev_post = post if post is not None else None; prev_i, prev_o = inner, outer
            stats['bays'] += nb
        else:
            stats['stone_m'] += float(s1 - s0); prev = None; sv = s0
            while sv <= s1 + 1e-6:
                p, t, n_ = at(sv); t = t / np.linalg.norm(t); n_ = n_ / np.linalg.norm(n_); edge = None; step_ = U(J['stone_post_m']); sv += step_
                for dd in np.arange(0.8, SR['edge_search_m'], 0.1):      # where the ledge ends on the lake side
                    c = p - n_ * dd
                    if ground(c) < p[1] - SR['edge_drop_m']: edge = p - n_ * max(0.6, dd - SR['edge_back_m'] - U([0.0, 0.5])); break
                if edge is None: prev = None; stats['rail_gaps_m'] += step_; continue
                edge[1] = ground(edge); hgt = U(J['stone_post_h']); lean = math.radians(U(J['stone_lean_deg'])); la = rng.uniform(0, 2 * math.pi)
                axis = up * math.cos(lean) + (-n_ * math.cos(la) + t * math.sin(la)) * math.sin(lean); axis /= np.linalg.norm(axis); top = edge + axis * hgt
                rail.beam(edge - up * 0.35, top, t, [SR['post'][0] * U([0.8, 1.3]), SR['post'][1] * U([0.8, 1.3])], M['iron']); stats['chain_posts'] += 1
                if prev is not None and np.linalg.norm(top - prev) < J['stone_post_m'][1] * 1.6 and rng.uniform() < J['chain_share']:
                    a = prev - up * U([0.08, 0.2]); b = top - up * U([0.08, 0.2]); sag = U(J['chain_sag']); nseg = 4
                    pts = [a + (b - a) * k / nseg - up * sag * math.sin(math.pi * k / nseg) for k in range(nseg + 1)]
                    for k in range(nseg): rail.beam(pts[k], pts[k + 1], up, (SR['chain_t'], SR['chain_t']), M['iron'])
                    ct = b - a; cl = np.linalg.norm(ct); ct /= cl; lo_ = min(a[1], b[1]) - sag - (prev_edge[1] + edge[1]) / 2
                    if lo_ > 0.3: col.box((prev_edge + edge) / 2 + up * lo_ / 2, ct, up, np.cross(ct, up), (cl, lo_, 0.05), M['iron'])
                elif prev is not None: stats['rail_gaps_m'] += float(np.linalg.norm(top - prev))
                prev = top; prev_edge = edge
    # ---- rough flagstones on the stone ledges of the cove (drawn only: the ledge itself is the walked ground)
    stones = Mesh('Gallery_Stones'); TR = cfg['trail']
    def pave(c, t, side, half, y_top, mat):
        """one cross row of 1-3 flagstones around centre c (top at y_top, never above it by more than lift)."""
        k_ = int(rng.integers(1, 4)); edges = np.sort(rng.uniform(-half, half, k_ - 1)) if k_ > 1 else np.array([]); cuts = np.r_[-half - U(TR['over']), edges, half + U(TR['over'])]
        ln = U(TR['len'])
        for a_, b_ in zip(cuts[:-1], cuts[1:]):
            if rng.uniform() < TR['hole_share'] and k_ > 1: continue
            cc = c + side * (a_ + b_) / 2 + t * U([-0.12, 0.12]); cc[1] = y_top + U(TR['lift'])
            stones.slab(cc, t, side, ln / 2, max(0.18, (b_ - a_) / 2 - U([0.01, 0.05])), U(TR['thick']), mat, rng, tilt=TR['tilt']); stats['flagstones'] = stats.get('flagstones', 0) + 1
        return ln
    for q in R['sections']:
        if q['kind'] != 'stone': continue
        sv = s[q['i0']] + 0.4
        while sv < s[q['i1']] - 0.4:
            p_, t_, n_ = at(sv); t_ = t_ / np.linalg.norm(t_); n_ = n_ / np.linalg.norm(n_); c_ = p_.copy()
            ln = pave(c_, t_, n_, TR['cove_half_m'], ground(c_), M['stone']) if rng.uniform() < TR['cove_cover'] else U(TR['len'])
            sv += ln + U(TR['gap'])
    # ---- the three #297 cut cliff paths: their own tread meshes are switched off (colliders stay), rough stones and planks are drawn in their place
    wood = Mesh('Gallery_TrailWood'); tsites = []
    try:
        import cliffpath297 as CP, surface297
        h0 = CP.HW.terrain().copy(); g0 = CP.Ground(h0); pre = [CP.Site(sp, g0, g0) for sp in CP.SITES]
        hp, _ = surface297.apply(h0.copy(), CP.protected_mask(), [dict(o, source='CliffPath') for st_ in pre for o in CP.notch_ops(st_)]); gp = CP.Ground(hp)
        for sp in CP.SITES:
            S_ = CP.Site(sp, g0, gp); n3 = np.stack([S_.n[:, 0], np.zeros(S_.N), S_.n[:, 1]], 1); t3 = np.stack([S_.t[:, 0], np.zeros(S_.N), S_.t[:, 1]], 1)
            fr = lambda sv: (np.array([np.interp(sv, S_.s, S_.xz[:, 0]), float(S_.Y(sv)), np.interp(sv, S_.s, S_.xz[:, 1])]), np.array([np.interp(sv, S_.s, t3[:, c]) for c in range(3)]), np.array([np.interp(sv, S_.s, n3[:, c]) for c in range(3)]))
            sv = float(S_.s[0]) + 0.3; cnt0 = stats.get('flagstones', 0); planks0 = stats.get('trail_planks', 0)
            while sv < float(S_.s[-1]) - 0.3:
                c_, t_, n_ = fr(sv); t_ /= np.linalg.norm(t_); n_ /= np.linalg.norm(n_)
                if S_.gal and S_.gal[0] <= sv <= S_.gal[1]:      # the hung gallery: single planks across
                    wpl = U(J['plank_w']); skew = math.radians(U(J['plank_skew_deg'])); pt = t_ * math.cos(skew) + n_ * math.sin(skew); ps = np.cross(up, pt); ps /= np.linalg.norm(ps)
                    wood.box(c_ + up * (0.012 - U(J['plank_drop'])) - n_ * U([0.0, 0.25]), pt, np.cross(pt, ps), ps, (wpl, 0.06, 2 * S_.hw + 0.3 + U(J['plank_over'])), M['deck']); stats['trail_planks'] = stats.get('trail_planks', 0) + 1
                    sv += wpl + U(J['plank_gap'])
                else:
                    sv += pave(c_, t_, n_, S_.hw + 0.1, c_[1] - 0.01, M['stone']) + U(TR['gap'])
            tsites.append(dict(id=S_.id, length_m=round(float(S_.s[-1] - S_.s[0]), 1), flagstones=stats.get('flagstones', 0) - cnt0, planks=stats.get('trail_planks', 0) - planks0))
    except Exception as e:      # the #297 inputs may be gone (2026-10-06): the cove is still built
        tsites.append(dict(error=repr(e)[:300]))
    stats['trail_sites'] = tsites
    (OUT / 'Meshes').mkdir(parents=True, exist_ok=True)
    for m in (deck, frame, rail, col, stones, wood):
        if not m.v: continue
        B.write_json(OUT / 'Meshes' / f'{m.name}.json', m.dump(), indent=None)
    grade = np.degrees(np.arctan(np.abs(np.gradient(y, s)))); gy = B.sample_tri(f, x, z)
    lay = dict(id='hyeongang_cove', version=cfg['version'], length=R['length_m'], start=P[0].round(3).tolist(), end=P[-1].round(3).tolist(), heightMin=round(float(y.min()), 2), heightMax=round(float(y.max()), 2),
               gradeMaxDeg=round(float(grade.max()), 1), sections=[dict(kind=q['kind'], from_m=round(float(s[q['i0']]), 1), to_m=round(float(s[q['i1']]), 1)) for q in R['sections']],
               stoneGroundOffMax=round(float(max(np.abs(gy - y)[q['i0']:q['i1'] + 1].max() for q in R['sections'] if q['kind'] == 'stone')), 2),
               vertices={m.name: len(m.v) for m in (deck, frame, rail, col, stones, wood)}, **{k: (round(v, 2) if isinstance(v, float) else v) for k, v in stats.items()})
    B.write_json(OUT / 'layout.json', lay)
    print('gallery308 build:', json.dumps(lay, ensure_ascii=False))


def main():
    B.utf8()
    if len(sys.argv) != 3 or sys.argv[1] not in ('route', 'build'): print(__doc__); return 2
    cfg = json.loads(Path(sys.argv[2]).read_text(encoding='utf-8'))
    route(cfg) if sys.argv[1] == 'route' else build(cfg)
    return 0


if __name__ == '__main__':
    sys.exit(main())
