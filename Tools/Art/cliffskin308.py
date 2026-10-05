"""#308 cliff boundary, phase 1b - rock skins for the EA-visible cliff faces. Offline, deterministic (numpy + PIL only).
SPEC-WORLD-CLIFF-BOUNDARY-308 design 1-2, 10; command `skins:<scene>` (CliffSkins308); AC-B2 / B10a / B15 / B17 / B21.

  python Tools/Art/cliffskin308.py --stage 1a            # the terrain the scenes stand on today  -> skins_1b/1a/ (look gate preview)
  python Tools/Art/cliffskin308.py --stage 1b            # ops308_v6.json + plan/Stage/height_p1b.bytes -> skins_1b/1b/ (the set `apply` uses)
  python Tools/Art/cliffskin308.py --stage 1b --ops <ops.json> --height <height.bytes> --out <dir>   # any other field
  python Tools/Art/cliffskin308.py --stage 1b --check    # generate again in memory and compare every output byte with the files on disk

What it does (every number is data: skins_1b/skins308_rules_1b.json):
  1. guides   runs Tools/Art/cliff308_eval.py --skins with THIS package's rules copy -> skins_1b/<stage>/skins308_<stage>.json
              (stations every 2 m on the as-built contour of the triangulated wall, skin flags, tiers A / B, panels + triangle caps).
              plan/skins308.json and plan/skins308_rules.json are never written.
  2. frame    per wall piece: a smoothed centreline (min radius) with outward normals; the true wall is measured on it by ray
              sampling the TRIANGULATED stage terrain (0.25 m x 0.5 m): wall offset per height, toe, rim, ground in front.
  3. skin     jointed rock: crest sections (broken top line, steps 2-4 m, no straight crest > 24 m) -> columns between vertical
              joints -> cells between bedding breaks. Every cell face is a min-link polyline inside the tube
              [wall + clearance, wall + front]: flat facets hide the 4 m lattice pleats, steep facets fall where the lattice jogs.
              The face leans with the wall, station by station (a twisted face). A cell above another is never set back by more
              than ledge_max (no ledge, AC-B10a); it may overhang, and where it has to step back further it leans back up to
              lean_back_max (steeper than the ledge slope). A facet with terrain inside (a lattice corner between two rays) is
              pushed outward until no sample is inside the cliff mass. The crest is a sharp edge with a 60 degree back slope
              (no flat cap). Foot buried bury_m under the lowest ground in front.
              LOD1 / LOD2 = the same sections with wider columns and taller cells (their joints are a subset of LOD0's).
  4. checks   piercing (terrain inside the skin) 0, front distance (march along the frame normal on the triangulated terrain),
              ledge test, top >= wall top + over, crest runs / steps, repeated-section correlation (AC-B21), caps per panel,
              budget (triangles, renderers, mesh memory), visible triangles / batches from the #307 points (frustum + LOD).
  5. outputs  skins_1b/<stage>/: Meshes/*.json (KitMesh: v, n, sub; local to the panel pivot), unity.json, report.md, report.json,
              previews/*.png, shots308_skins.json. No timestamps: a second run writes the same bytes.

Skins have NO collider, no emission, one material, no shadow caster. Nothing here writes inside the Unity project.
"""
import argparse, hashlib, json, math, subprocess, sys
from pathlib import Path
import numpy as np

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
import cliff308_base as B                                                # noqa: E402
from cliff308_base import sample_tri                                     # noqa: E402

OUT = B.CB / 'skins_1b'
RULES = OUT / 'skins308_rules_1b.json'
UP = np.array([0.0, 1.0, 0.0])
FRONT, BEDDING, END, CAP = 0, 1, 2, 3
KIND_NAMES = ['front', 'bedding', 'end', 'cap']
r2 = lambda v: round(float(v), 2)
r3 = lambda v: round(float(v), 3)


# ------------------------------------------------------------------ small helpers
def rng(seed, *keys):
    """numpy Generator seeded by sha256('<seed>:<key>:<key>...') - no Python hash(), no global RNG, no time."""
    d = hashlib.sha256((str(seed) + ':' + ':'.join(str(k) for k in keys)).encode('utf-8')).digest()
    return np.random.default_rng(int.from_bytes(d[:8], 'little'))


def gsm(a, sig):
    """gaussian smoothing with edge padding (sig in samples)."""
    if sig <= 0: return np.asarray(a, float).copy()
    r = int(math.ceil(sig * 3)); k = np.exp(-0.5 * (np.arange(-r, r + 1) / sig) ** 2); k /= k.sum()
    return np.convolve(np.concatenate([np.full(r, a[0]), a, np.full(r, a[-1])]), k, 'valid')


def unit(v):
    v = np.asarray(v, float); return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-12)


def split_range(a, b, wr, ds, min_w, R):
    """node boundaries a = e0 < e1 < ... = b with random widths wr (m); the last piece is never shorter than min_w."""
    e = [a]; i = a
    while True:
        step = max(1, int(round(math.exp(R.uniform(math.log(wr[0]), math.log(wr[1]))) / ds)))      # log-uniform: a few wide slabs among narrow columns
        if b - (i + step) < int(round(min_w / ds)): break
        i += step; e.append(i)
    e.append(b)
    return e


# ------------------------------------------------------------------ frame + wall measurement of one wall piece
class Piece:
    def __init__(self, sid, g, new, h, RL, G, station, mode='raise'):
        self.sid = sid; self.pi = g['piece']; self.G = G; ds = G['ds_m']; self.ds = ds; self.new = new; self.h = h; self.mode = mode
        Q = np.stack([np.asarray(g['sx'], float), np.asarray(g['sz'], float)], 1); n = len(Q); self.nq = n
        O = np.stack([np.asarray(g['ox'], float), np.asarray(g['oz'], float)], 1)
        F = G['frame']; sig = F['sigma_m'] / station
        Qs = np.stack([gsm(Q[:, 0], sig), gsm(Q[:, 1], sig)], 1)

        def radius(P):
            d1 = np.gradient(P, axis=0); sp = np.linalg.norm(d1, axis=1) + 1e-12; T = d1 / sp[:, None]
            return 1.0 / np.maximum(np.linalg.norm(np.gradient(T, axis=0), axis=1) / sp, 1e-9)
        for _ in range(int(F['iterations'])):
            bad = radius(Qs) < F['min_radius_m']
            if not bad.any(): break
            m = np.clip(gsm(np.clip(np.convolve(bad.astype(float), np.ones(9), 'same'), 0, 1), 2.0) * 1.4, 0, 1)
            Q2 = np.stack([gsm(Qs[:, 0], F['extra_sigma_m'] / station), gsm(Qs[:, 1], F['extra_sigma_m'] / station)], 1)
            Qs = Qs * (1 - m[:, None]) + Q2 * m[:, None]
        self.min_radius = float(radius(Qs).min())
        sc = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(Qs, axis=0).T))]); ns = int(math.floor(sc[-1] / ds)) + 1
        s = np.arange(ns) * ds; self.s = s; self.ns = ns; qi = np.arange(n, dtype=float)
        self.uf = np.interp(s, sc, qi)                                    # fractional guide-station index of every fine station
        self.gs = self.uf * (g['length_m'] / max(1, n - 1))               # arclength along the RAW contour (the eval panels use it)
        C = np.stack([np.interp(s, sc, Qs[:, 0]), np.interp(s, sc, Qs[:, 1])], 1)
        T = unit(np.gradient(C, axis=0)); N = np.stack([T[:, 1], -T[:, 0]], 1)
        Oi = np.stack([np.interp(self.uf, qi, O[:, 0]), np.interp(self.uf, qi, O[:, 1])], 1)
        if float((N * Oi).sum(1).mean()) < 0: N = -N
        self.C = C; self.T = T; self.N = N
        f0 = np.clip(np.floor(self.uf).astype(int), 0, n - 1); f1 = np.clip(f0 + 1, 0, n - 1)
        sk = np.asarray(g['skin'], bool).copy(); skr = sk.copy(); ta = np.asarray(g['tier_a'], bool); self.bridged = 0
        q = 0; gapn = int(round(G['bridge_gap_m'] / station))      # a short gap between two skin runs (a one-vertex lattice indent) is skinned across
        while q < n:
            if sk[q]: q += 1; continue
            e = q
            while e + 1 < n and not sk[e + 1]: e += 1
            if q > 0 and e < n - 1 and (e - q + 1) <= gapn: sk[q:e + 1] = True; self.bridged += e - q + 1
            q = e + 1
        self.skin = skr[f0] & skr[f1]; bridge = (sk[f0] & sk[f1]) & ~self.skin; self.tier_a = ta[np.clip(np.round(self.uf).astype(int), 0, n - 1)]
        gtop = np.asarray(g['y_top'], float); self.guide_top = np.interp(self.uf, qi, gtop)

        # --- ray sampling of the triangulated terrain along the frame normals
        Rr = G['ray']; dt = Rr['dt']; tt = np.arange(Rr['t0'], Rr['t1'] + 1e-9, dt); nt = len(tt); self.tt = tt; self.dt = dt
        X = C[:, 0:1] + N[:, 0:1] * tt[None]; Z = C[:, 1:2] + N[:, 1:2] * tt[None]
        hn = sample_tri(new, X, Z); hb = sample_tri(h, X, Z); raised = (hn - hb) > Rr['raised_eps_m']; self.hn = hn
        Qi = np.stack([np.interp(self.uf, qi, Q[:, 0]), np.interp(self.uf, qi, Q[:, 1])], 1)
        tq = ((Qi - C) * N).sum(1); kq = np.clip(np.round((tq - Rr['t0']) / dt).astype(int), 0, nt - 1); ar = np.arange(nt)
        dist = np.abs(ar[None] - kq[:, None]) + np.where(raised, 0, 10 ** 6); k0 = dist.argmin(1)       # nearest raised sample to the contour
        near = dist[np.arange(ns), k0] <= int(round(Rr['contour_search_m'] / dt))
        gap = (~raised) & (ar[None] > k0[:, None]); first = np.where(gap.any(1), gap.argmax(1), nt)
        kt = first - 1; pad = int(round((RL['relief_m'] + RL['clearance_m'] + 2.0) / dt))
        ok = near & (kt + pad + 1 < nt) & (kt - int(round(Rr['wall_back_m'] / dt)) >= 0)
        kt = np.clip(kt, int(round(Rr['wall_back_m'] / dt)), nt - pad - 2); self.kt = kt
        # a bridged gap stays open where another raised mass stands 1-8 m in front of the toe (a joint of two cliff masses)
        jw = (ar[None] >= (kt + 8)[:, None]) & (ar[None] <= (kt + 64)[:, None]); joint = np.convolve((raised & jw).any(1).astype(float), np.ones(2 * int(round(1.0 / ds)) + 1), 'same') > 0
        self.bridged = int((bridge & ~joint & ok).sum()); self.valid = ok; self.skin = (self.skin | (bridge & ~joint)) & ok
        rows = np.arange(ns); self.t_toe = tt[kt]; self.y_toe = hn[rows, kt + 2]
        win = (ar[None] <= kt[:, None]) & (ar[None] >= (kt - int(round(Rr['wall_back_m'] / dt)))[:, None])
        hw = np.where(win, hn, -np.inf)
        rimw = (ar[None] <= kt[:, None]) & (ar[None] >= (kt - int(round(Rr['rim_back_m'] / dt)))[:, None])
        self.rim = np.where(rimw, hn, -np.inf).max(1)
        frontw = (ar[None] >= (kt - 4)[:, None]) & (ar[None] <= (kt + pad)[:, None])
        yF = np.where(frontw, hn, np.inf).min(1) - RL['bury_m']
        # lower envelope, smoothed: the buried foot line is never above the raw value (bury >= bury_m everywhere)
        wn = int(round(G['foot']['min_window_m'] / ds)); pf = np.concatenate([np.full(wn, yF[0]), yF, np.full(wn, yF[-1])])
        mn = np.min(np.stack([pf[i:i + ns] for i in range(2 * wn + 1)], 0), 0)
        self.yF = gsm(mn, G['foot']['smooth_m'] / ds)
        # wall offset per height level: w(s, y) = outermost t (<= toe) where the terrain reaches y
        dy = G['dy_m']; self.dy = dy; top = float(self.rim[ok].max()) if ok.any() else 0.0
        self.Y0 = math.floor(float(self.yF[ok].min()) if ok.any() else 0.0) - 1.0
        nl = int(math.ceil((top + G['crest']['over_max_m'] + 6.0 - self.Y0) / dy)) + 1; self.nl = nl
        Y = self.Y0 + dy * np.arange(nl); W = np.empty((ns, nl)); M = np.maximum.accumulate(hw[:, ::-1], axis=1)[:, ::-1]
        for a in range(0, ns, 128):
            b = min(ns, a + 128); cnt = (M[a:b, :, None] >= Y[None, None, :]).sum(1); kl = np.clip(cnt - 1, 0, nt - 2)
            rr = np.arange(a, b)[:, None]; hk = hn[rr, kl]; hk1 = hn[rr, kl + 1]
            fr = np.where((kl < kt[a:b, None]) & (hk1 < Y[None]) & (hk > hk1), np.clip((hk - Y[None]) / np.maximum(hk - hk1, 1e-9), 0, 1), 0.0)
            w = tt[kl] + dt * fr; w[cnt <= 0] = np.nan
            # above the wall top: keep the offset of the highest level that still has terrain (the rim edge)
            good = ~np.isnan(w); last = np.where(good.any(1), nl - 1 - good[:, ::-1].argmax(1), 0)
            fill = w[np.arange(b - a), last]; w = np.where(np.isnan(w), fill[:, None], w); W[a:b] = np.nan_to_num(w, nan=0.0)
        # wall edge: going back from the toe, the first place above the toe where the ground stops being a face (rise < edge_rise_m
        # per 0.5 m = flatter than about 45 degrees). From the edge height up the skin is a free-standing crag on that edge: every
        # higher level takes the edge's offset (ground that keeps rising gently BEHIND the edge is not a wall to follow; the crest
        # cap dives into it). A ridge that stays steep (outer peaks) has no edge inside the window: its last sample is used.
        back = int(round(Rr['wall_back_m'] / dt)); hp = np.concatenate([np.repeat(hn[:, :1], 4, 1), hn], 1)[:, :nt]      # hn 0.5 m further back
        flat = ((hp - hn) < Rr['edge_rise_m']) & (hn > self.y_toe[:, None] + Rr['edge_min_face_m']) & (ar[None] <= (kt - 2)[:, None]) & (ar[None] >= (kt - back)[:, None])
        ke = np.where(flat.any(1), nt - 1 - flat[:, ::-1].argmax(1), np.clip(kt - back, 0, nt - 1)); self.y_edge = hn[rows, ke]; edge = tt[ke]
        W = np.where(Y[None, :] > (self.y_edge - 0.25)[:, None], edge[:, None], W)
        self.W = W; self.crest = np.full(ns, np.nan); self.w_rim = edge
        self.rim_eff = np.maximum(self.rim, self.guide_top - RL['over_m'])   # never below the guide's wall top (measured on the raw contour)

    def wq(self, idx, y):
        f = np.clip((np.asarray(y, float) - self.Y0) / self.dy, 0, self.nl - 1.001); l0 = np.floor(f).astype(int); a = f - l0
        return self.W[idx, l0] * (1 - a) + self.W[idx, l0 + 1] * a

    def world(self, idx, t, y):
        return np.stack([self.C[idx, 0] + self.N[idx, 0] * t, np.asarray(y, float) + 0 * np.asarray(t, float), self.C[idx, 1] + self.N[idx, 1] * t], -1)

    def n3(self, idx):
        return np.stack([self.N[idx, 0], 0 * self.N[idx, 0], self.N[idx, 1]], -1)

    def t3(self, idx):
        return np.stack([self.T[idx, 0], 0 * self.T[idx, 0], self.T[idx, 1]], -1)


# ------------------------------------------------------------------ mesh accumulator
NA = 4   # per-triangle attributes: frame normal x, z | analytic offset in front of the wall (m) | 1 = the facet lies between toe and rim


def attr(f2, m=None):
    f2 = np.asarray(f2, float)
    if f2.shape[-1] < NA: f2 = np.concatenate([f2, np.zeros(f2.shape[:-1] + (NA - f2.shape[-1],))], -1)
    return f2 if m is None else np.broadcast_to(f2, (m, NA))


class Acc:
    def __init__(self):
        self.V = []; self.N = []; self.T = []; self.K = []; self.F = []; self.nv = 0

    def _add(self, V, Nn, T, kind, f2):
        if len(T) == 0: return
        self.V.append(V); self.N.append(Nn); self.T.append(T + self.nv); self.K.append(np.full(len(T), kind, np.uint8)); self.F.append(f2); self.nv += len(V)

    def quads(self, P, out, kind, f2):
        """flat quads P (m, 4, 3); `out` (m, 3) or (3,) fixes the winding: Unity front = cross(b - a, c - a) toward the viewer. The two
        triangles of a quad are wound one by one (a bedding quad may be slightly bow-tied)."""
        P = np.asarray(P, float).reshape(-1, 4, 3); m = len(P); out = np.broadcast_to(np.asarray(out, float), (m, 3)); f2 = attr(f2, m)
        n0 = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]); n1 = np.cross(P[:, 2] - P[:, 0], P[:, 3] - P[:, 0]); keep = (np.linalg.norm(n0, axis=1) + np.linalg.norm(n1, axis=1)) > 1e-7
        if not keep.any(): return
        P = P[keep]; out = out[keep]; f2 = f2[keep]; n0 = n0[keep]; n1 = n1[keep]; m = len(P); fl0 = (n0 * out).sum(1) < 0; fl1 = (n1 * out).sum(1) < 0
        n = unit(np.where(fl0[:, None], -n0, n0) + np.where(fl1[:, None], -n1, n1))
        b = 4 * np.arange(m)[:, None]; t0 = np.where(fl0[:, None], b + np.array([0, 2, 1]), b + np.array([0, 1, 2])); t1 = np.where(fl1[:, None], b + np.array([0, 3, 2]), b + np.array([0, 2, 3]))
        self._add(P.reshape(-1, 3), np.repeat(n, 4, 0), np.stack([t0, t1], 1).reshape(-1, 3), kind, np.repeat(f2, 2, 0))

    def tris(self, P, out, kind, f2):
        P = np.asarray(P, float).reshape(-1, 3, 3); m = len(P); out = np.broadcast_to(np.asarray(out, float), (m, 3)); f2 = attr(f2, m)
        n = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]); ln = np.linalg.norm(n, axis=1); keep = ln > 1e-7
        if not keep.any(): return
        P = P[keep]; out = out[keep]; f2 = f2[keep]; n = n[keep] / ln[keep, None]; m = len(P); flip = (n * out).sum(1) < 0; n[flip] *= -1
        b = 3 * np.arange(m)[:, None]; t = np.where(flip[:, None], b + np.array([0, 2, 1]), b + np.array([0, 1, 2]))
        self._add(P.reshape(-1, 3), np.repeat(n, 3, 0), t, kind, f2)

    def strip(self, Bp, Tp, out, kind, f2, cos_s):
        """a face strip between a bottom and a top polyline; neighbouring facets share vertices when they meet at less than the smooth angle."""
        n = len(Bp); m = n - 1; f2 = attr(f2)
        if m < 1: return
        fn = np.cross(Bp[1:] - Bp[:-1], Tp[1:] - Bp[:-1]) + np.cross(Tp[1:] - Bp[:-1], Tp[:-1] - Bp[:-1]); om = (out[1:] + out[:-1]) / 2
        flip = (fn * om).sum(1) < 0; fn[flip] *= -1; fn = unit(fn)
        V = []; Nn = []; left = np.zeros(m, int); right = np.zeros(m, int)

        def pair(k, nrm):
            V.append(Bp[k]); V.append(Tp[k]); Nn.append(nrm); Nn.append(nrm); return len(V) - 2
        left[0] = pair(0, fn[0])
        for k in range(1, n - 1):
            if float(fn[k - 1] @ fn[k]) > cos_s:
                p = pair(k, unit(fn[k - 1] + fn[k])); right[k - 1] = p; left[k] = p
            else:
                right[k - 1] = pair(k, fn[k - 1]); left[k] = pair(k, fn[k])
        right[m - 1] = pair(n - 1, fn[m - 1])
        a = left; b = right; t0 = np.where(flip[:, None], np.stack([a, b + 1, b], 1), np.stack([a, b, b + 1], 1)); t1 = np.where(flip[:, None], np.stack([a, a + 1, b + 1], 1), np.stack([a, b + 1, a + 1], 1))
        self._add(np.array(V), np.array(Nn), np.stack([t0, t1], 1).reshape(-1, 3), kind, np.repeat((f2[1:] + f2[:-1]) / 2, 2, 0))

    def merged(self):
        if not self.T: return np.zeros((0, 3)), np.zeros((0, 3)), np.zeros((0, 3), int), np.zeros(0, np.uint8), np.zeros((0, NA))
        return np.concatenate(self.V), np.concatenate(self.N), np.concatenate(self.T), np.concatenate(self.K), np.concatenate(self.F)

    def extend(self, other):
        V, Nn, T, K, F = other.merged()
        if len(T) == 0: return
        self.V.append(V); self.N.append(Nn); self.T.append(T + self.nv); self.K.append(K); self.F.append(F); self.nv += len(V)


# ------------------------------------------------------------------ cells of one column
def tube(pc, ia, ib, y0, y1, yc, b, clr, front):
    l0 = max(0, int(math.floor((float(y0.min()) - pc.Y0) / pc.dy)) - 1); l1 = min(pc.nl - 1, int(math.ceil((float(y1.max()) - pc.Y0) / pc.dy)) + 1)
    Yb = pc.Y0 + pc.dy * np.arange(l0, l1 + 1); Wb = pc.W[ia:ib + 1, l0:l1 + 1]
    m = (Yb[None, :] >= y0[:, None] - pc.dy) & (Yb[None, :] <= y1[:, None] + pc.dy)
    r = Wb - np.reshape(b, (-1, 1)) * (Yb[None, :] - yc[:, None])
    return np.where(m, r, -np.inf).max(1) + clr, np.where(m, r, np.inf).min(1) + front


def polyline(s, L, U, rho, R, maxlen, cpref, ds):
    """min-link polyline g(s) inside [L, U] (greedy funnel); `maxlen` (m range) caps a facet, None = as long as it fits."""
    n = len(s); gi = float(L[0] + rho * (U[0] - L[0])); ni = [0]; ng = [gi]; i = 0
    while i < n - 1:
        m = n if maxlen is None else max(2, int(round(R.uniform(maxlen[0], maxlen[1]) / ds)))
        j1 = min(n - 1, i + m)
        if n - 1 - j1 < 3: j1 = n - 1
        d = s[i + 1:j1 + 1] - s[i]; lo = (L[i + 1:j1 + 1] - gi) / d; hi = (U[i + 1:j1 + 1] - gi) / d
        cmn = np.maximum.accumulate(lo); cmx = np.minimum.accumulate(hi); okk = cmn <= cmx + 1e-12
        cnt = len(okk) if okk.all() else max(1, int(np.argmin(okk)))
        c = float(np.clip(R.uniform(-cpref, cpref), cmn[cnt - 1], max(cmn[cnt - 1], cmx[cnt - 1])))
        i += cnt; gi = gi + c * float(d[cnt - 1]); ni.append(i); ng.append(gi)
    return ni, ng


def refine(pc, ia, ni, ng, b, hh, L, U, tol_in, tol_out, depth):
    """world chords: between two nodes the mesh edge is straight in WORLD space while the tube lives in the curved frame; split a
    facet where its chord leaves the tube (bottom and top row are both checked)."""
    C = pc.C; N = pc.N

    def worst(i, gi, j, gj):
        if j - i < 2: return -1
        k = np.arange(i + 1, j); lam = ((k - i) / (j - i))[:, None]; w = 0.0; kw = -1
        for sg in (-0.5, 0.5):
            Pi = C[ia + i] + N[ia + i] * (gi + sg * b[i] * hh[i]); Pj = C[ia + j] + N[ia + j] * (gj + sg * b[j] * hh[j])
            tch = (((Pi[None] * (1 - lam) + Pj[None] * lam) - C[ia + k]) * N[ia + k]).sum(1)
            v = np.maximum(L[k] + sg * b[k] * hh[k] - tch - tol_in, tch - (U[k] + sg * b[k] * hh[k]) - tol_out)
            a = int(v.argmax())
            if v[a] > w: w = float(v[a]); kw = int(k[a])
        return kw
    oi = [ni[0]]; og = [ng[0]]
    for q in range(len(ni) - 1):
        stack = [(ni[q], ng[q], ni[q + 1], ng[q + 1], 0)]; seg = []
        while stack:
            i, gi, j, gj, dp = stack.pop()
            k = worst(i, gi, j, gj) if dp < depth else -1
            if k < 0: seg.append((j, gj)); continue
            gk = float(np.clip(gi + (gj - gi) * (k - i) / (j - i), L[k], U[k]))
            stack.append((k, gk, j, gj, dp + 1)); stack.append((i, gi, k, gk, dp + 1))
        for j, gj in seg: oi.append(j); og.append(gj)
    return oi, og


def chord_t(pc, ia, n, ni, ng, off):
    """frame offset of the actual mesh edge (straight in world space between nodes) at every station of the column."""
    C = pc.C; N = pc.N; out = np.empty(n)
    for q in range(len(ni) - 1):
        i, j = int(ni[q]), int(ni[q + 1]); k = np.arange(i, j + 1); lam = ((k - i) / (j - i))[:, None]
        Pi = C[ia + i] + N[ia + i] * (ng[q] + off[i]); Pj = C[ia + j] + N[ia + j] * (ng[q + 1] + off[j])
        out[k] = (((Pi[None] * (1 - lam) + Pj[None] * lam) - C[ia + k]) * N[ia + k]).sum(1)
    return out


def repair(pc, ia, ni, ng, b, y0, y1, eps, step, iters):
    """no terrain inside a face: the facet triangles are sampled exactly as check_pierce samples them; a facet with a sample inside
    the added cliff mass has its two nodes pushed outward by `step`, until none is left (corners of the lattice wall that fall
    between two measuring rays)."""
    ng = np.asarray(ng, float).copy(); k = ia + ni; hh = (y1 - y0)[ni]; m = len(ni) - 1; pushed = 0.0; fb = np.zeros(m, bool)
    for _ in range(int(iters) + 1):
        Bp = pc.world(k, ng - b[ni] * hh / 2, y0[ni]); Tp = pc.world(k, ng + b[ni] * hh / 2, y1[ni])
        tri = np.concatenate([np.stack([Bp[:-1], Bp[1:], Tp[1:]], 1), np.stack([Bp[:-1], Tp[1:], Tp[:-1]], 1)])
        pts = np.einsum('kb,mbc->mkc', BARY, tri); hn = sample_tri(pc.new, pts[..., 0], pts[..., 2]); hb = sample_tri(pc.h, pts[..., 0], pts[..., 2])
        bad = ((pts[..., 1] > hb + 0.05) & (hn - pts[..., 1] > eps)).any(1); fb = bad[:m] | bad[m:]
        if not fb.any() or pushed >= step * iters - 1e-9: break
        node = np.zeros(len(ni), bool); node[:-1] |= fb; node[1:] |= fb; ng[node] += step; pushed += step
    return ng, pushed, bool(fb.any())


def build_column(pc, ia, ib, crest, vb, lod, tier, R, RL, G):
    """cells of the column of fine stations ia..ib (bottom to top). crest = top line on those stations, vb = cell boundaries (fractions)."""
    ds = pc.ds; idx = np.arange(ia, ib + 1); s = pc.s[idx]; ybot = pc.yF[idx]; H = crest - ybot
    Tb = G['tube']['lod%d' % lod]; St = G['standoff']; Ch = G['chord']['lod%d' % lod]; fac = G['facet']; Rp = G['repair']
    maxlen = fac[tier]['lod0_m'] if lod == 0 else None
    cells = []; state = dict(prev_top=None, bprev=-0.08, rho=float(R.uniform(St['start'][0], St['start'][1]))); Hm = float(H.mean()); Ti = G['tiers']

    def cell(v0, v1, depth):
        y0 = ybot + v0 * H; y1 = ybot + v1 * H; hh = y1 - y0; yc = (y0 + y1) / 2
        # lean of the face, per station: the measuring ray crosses the lattice wall at a changing angle, so the wall's lean along the
        # ray changes along the column (a twisted face follows it; one shared lean would leave the tube on a tall cell)
        ya = y0 + 0.15 * hh; yb_ = np.minimum(y0 + 0.85 * hh, pc.y_edge[idx] - 0.5); okl = (yb_ - ya) > 1.0
        bs = np.where(okl, (pc.wq(idx, np.maximum(yb_, ya + 1e-3)) - pc.wq(idx, ya)) / np.maximum(yb_ - ya, 1e-3), np.nan)
        bs = np.where(np.isnan(bs), float(np.nanmedian(bs)) if okl.any() else state['bprev'], bs); b = np.clip(gsm(bs, St['lean_smooth_m'] / ds), -St['lean_back_max'], 0.05)
        bw = float(np.median(b)); prev_top = state['prev_top']
        if prev_top is not None:
            # the cell below stands too far out (it could not step back by more than ledge_max): lean this face back, up to
            # lean_back_max (a slab steeper than the ledge slope), so that its top is inside the tube again
            need = (pc.wq(idx, np.minimum(y1, pc.y_edge[idx])) + Tb['front_m'] - 0.3 - prev_top + St['ledge_max_m']) / np.maximum(hh, 0.5)
            b = np.clip(np.minimum(b, need), -St['lean_back_max'], 0.05)
        L, U = tube(pc, ia, ib, y0, y1, yc, b, Tb['clearance_m'], Tb['front_m'])
        # a cell whose wall leaves every plane by more than the tube is wide (a lattice jog crossing its height) is halved in height
        if float((L - U).max()) > Ti['split_over_m'] and depth < int(Ti['split_depth']['lod%d' % lod]) and (v1 - v0) * Hm >= 2 * Ti['split_min_m']:
            vm = (v0 + v1) / 2; cell(v0, vm, depth + 1); cell(vm, v1, depth + 1); return
        state['bprev'] = bw; raw = float((L - U).max())
        if prev_top is not None:
            L = np.maximum(L, prev_top - St['ledge_max_m'] + b * hh / 2); U = np.minimum(U, prev_top + St['overhang_max_m'] + b * hh / 2)
        over = np.maximum(L - U, 0.0); U = np.maximum(U, L)
        ni, ng = polyline(s, L, U, state['rho'], R, maxlen, fac['slope_pref'], ds)
        ni, ng = refine(pc, ia, ni, ng, b, hh, L, U, Ch['tol_in_m'], Ch['tol_out_m'], int(Ch['depth']))
        ni = np.asarray(ni, int); ng, pushed, still = repair(pc, ia, ni, ng, b, y0, y1, Rp['eps_m'], Rp['step_m'], Rp['iterations'])
        gfull = np.interp(s, s[ni], ng); state['prev_top'] = chord_t(pc, ia, len(s), ni, ng, b * hh / 2)
        cells.append(dict(ni=ni, ng=ng, b=b, y0=y0, y1=y1, g=gfull, over=float(over.max()), raw=raw, bw=bw, top=False, bottom=False, pushed=pushed, still=still))
        state['rho'] = float(np.clip(state['rho'] + R.uniform(-St['walk'], St['walk']), 0.03, 0.9))
    for j in range(len(vb) - 1): cell(float(vb[j]), float(vb[j + 1]), 0)
    cells[0]['bottom'] = True; cells[-1]['top'] = True
    return dict(ia=ia, ib=ib, cells=cells, crest=crest, lod=lod, tier=tier)


def cell_rows(pc, col, cell):
    """world vertices of the cell face: bottom row and top row at its polyline nodes, + station index and frame t of both rows."""
    k = col['ia'] + cell['ni']; hh = (cell['y1'] - cell['y0'])[cell['ni']]; bn = cell['b'][cell['ni']]; tb = cell['ng'] - bn * hh / 2; tt_ = cell['ng'] + bn * hh / 2
    return pc.world(k, tb, cell['y0'][cell['ni']]), pc.world(k, tt_, cell['y1'][cell['ni']]), k, tb, tt_


def edge_at(P, ni, kk):
    """world points on the polyline P (given at local station indices ni) at local station indices kk (linear along the mesh edge)."""
    f = np.interp(kk, ni, np.arange(len(ni))); a = np.clip(np.floor(f).astype(int), 0, len(ni) - 2); lam = (f - a)[:, None]
    return P[a] * (1 - lam) + P[a + 1] * lam


def cap_back(pc, k, tc, yc, Cr):
    """crest cap: from the crest edge a slope of cap_slope_deg goes back and down until it is cap_bury under the terrain."""
    tau = np.arange(0.0, Cr['cap_reach_m'] + 1e-9, 0.1); tan = math.tan(math.radians(Cr['cap_slope_deg']))
    t = tc[:, None] - tau[None]; line = yc[:, None] - tan * tau[None]
    f = np.clip((t - pc.tt[0]) / pc.dt, 0, len(pc.tt) - 1.001); a = np.floor(f).astype(int); lam = f - a
    ter = pc.hn[k[:, None], a] * (1 - lam) + pc.hn[k[:, None], a + 1] * lam
    hit = line <= ter - Cr['cap_bury_m']; first = np.where(hit.any(1), hit.argmax(1), len(tau) - 1); r = np.arange(len(k))
    return t[r, first], line[r, first]


def front_t(col, side, y):
    """front offset of a column at one of its edges (side 0 = first station, -1 = last) at heights y; nan outside its height range."""
    out = np.full(len(y), np.nan)
    for c in col['cells']:
        hh = c['y1'][side] - c['y0'][side]; g = c['g'][side]; m = (y >= c['y0'][side] - 1e-6) & (y <= c['y1'][side] + 1e-6)
        out[m] = g + c['b'][side] * (y[m] - (c['y0'][side] + c['y1'][side]) / 2)
    return out


def emit_column(pc, col, left, right, acc, G, probes):
    """triangles of one column: cell faces, bedding strips, end caps (culled where the neighbour column hides them), crest cap."""
    cos_s = math.cos(math.radians(G['facet']['smooth_angle_deg'])); inset = G['end_cap_inset_m']; Cr = G['crest']; ia = col['ia']
    rows = [cell_rows(pc, col, c) for c in col['cells']]
    for c, (Bp, Tp, k, tb, tt_) in zip(col['cells'], rows):
        ya = c['y0'][c['ni']]; yb_ = c['y1'][c['ni']]; so = np.maximum(tb - pc.wq(k, ya), tt_ - pc.wq(k, yb_)); band = ((ya > pc.y_toe[k] + 0.5) & (yb_ < pc.y_edge[k] - 0.5)).astype(float)
        acc.strip(Bp, Tp, pc.n3(k), FRONT, np.concatenate([pc.N[k], so[:, None], band[:, None]], 1), cos_s)
        if probes is not None and not c['top'] and not c['bottom'] and len(k) >= 2:
            for m in range(len(k) - 1):
                km = (k[m] + k[m + 1]) // 2; ym = float((c['y0'][km - ia] + c['y1'][km - ia]) / 2); tm = float(c['g'][km - ia])
                P = pc.world(np.array([km]), np.array([tm]), np.array([ym]))[0]
                probes.append((P[0], P[1], P[2], pc.N[km, 0], pc.N[km, 1], tm - float(pc.wq(np.array([km]), np.array([ym]))[0])))
    # bedding strips between a cell and the next one above it
    for j in range(len(col['cells']) - 1):
        lo = col['cells'][j]; up = col['cells'][j + 1]; kk = np.union1d(lo['ni'], up['ni'])
        A = edge_at(rows[j][1], lo['ni'], kk); Bq = edge_at(rows[j + 1][0], up['ni'], kk); n2 = pc.N[ia + kk]
        d = ((A - Bq)[:, [0, 2]] * n2).sum(1)                              # > 0: the lower cell is prouder (its top shows, facing up)
        for m in range(len(kk) - 1):
            da, db = float(d[m]), float(d[m + 1])
            if abs(da) < 0.02 and abs(db) < 0.02: continue
            quad = np.array([Bq[m], Bq[m + 1], A[m + 1], A[m]]); f2 = (n2[m] + n2[m + 1]) / 2
            if da >= -0.01 and db >= -0.01: acc.quads(quad, UP, BEDDING, f2); col['ledge'] = max(col.get('ledge', 0.0), da, db)
            elif da <= 0.01 and db <= 0.01: acc.quads(quad, -UP, BEDDING, f2)
            else:
                lam = da / (da - db); X = ((A[m] * (1 - lam) + A[m + 1] * lam) + (Bq[m] * (1 - lam) + Bq[m + 1] * lam)) / 2
                acc.tris(np.array([Bq[m], X, A[m]]), UP if da > 0 else -UP, BEDDING, f2); acc.tris(np.array([X, Bq[m + 1], A[m + 1]]), UP if db > 0 else -UP, BEDDING, f2); col['ledge'] = max(col.get('ledge', 0.0), da, db)
    # crest cap of the top cell
    top = col['cells'][-1]; Bp, Tp, k, tb, tt_ = rows[-1]
    tbk, ybk = cap_back(pc, k, tt_, top['y1'][top['ni']], Cr); Bk = pc.world(k, tbk, ybk)
    capn = unit(UP * math.cos(math.radians(Cr['cap_slope_deg'])) - pc.n3(k) * math.sin(math.radians(Cr['cap_slope_deg'])))
    if col['lod'] == 2 and len(k) > 2: e2 = np.array([0, len(k) - 1]); acc.strip(Tp[e2], Bk[e2], capn[e2], CAP, pc.N[k[e2]], cos_s)      # far LOD: one cap quad per column
    else: acc.strip(Tp, Bk, capn, CAP, pc.N[k], cos_s)
    # end caps
    for side, nb, sgn in ((0, left, -1.0), (-1, right, 1.0)):
        ke = np.array([col['ia'] if side == 0 else col['ib']]); tdir = pc.t3(ke)[0] * sgn; f2 = pc.N[ke[0]]
        for c, (Bp, Tp, k, tb, tt_) in zip(col['cells'], rows):
            y0 = float(c['y0'][side]); y1 = float(c['y1'][side]); F0 = Bp[side]; F1 = Tp[side]
            if c['top']:
                I = pc.world(ke, pc.wq(ke, np.array([y0])) - inset, np.array([y0]))[0]
                acc.tris(np.array([[F0, F1, I], [F1, Bk[side], I]]), tdir, END, f2); continue
            if nb is not None:
                ys = np.linspace(y0 + 0.05, y1 - 0.05, 7); other = front_t(nb, -1 if side == 0 else 0, ys); own = front_t(col, side, ys)
                if not np.isnan(other).any() and bool((other >= own - 0.005).all()): continue
            I0 = pc.world(ke, pc.wq(ke, np.array([y0])) - inset, np.array([y0]))[0]; I1 = pc.world(ke, pc.wq(ke, np.array([y1])) - inset, np.array([y1]))[0]
            acc.quads(np.array([F0, F1, I1, I0]), tdir, END, f2)


# ------------------------------------------------------------------ layout of one piece: runs -> crest sections -> columns
def keepout_mask(pc, ops, G, stage_rank, apply_stage):
    """stations inside a lift bay window, a drop ledge or the landing (pieces package) and inside the extra keep-outs of the rules."""
    K = G['bay_keepout']; m = np.zeros(pc.ns, bool); C = pc.C; hits = []
    for bay in ops.get('bays', []):
        if bay.get('segment') != pc.sid or stage_rank(bay.get('stage', '1b')) > stage_rank(apply_stage): continue
        c = np.asarray(bay.get('face_station_xz') or bay['at'], float); r = float(bay.get('window_half_m', 14.0)) + K['window_pad_m']
        hit = np.hypot(C[:, 0] - c[0], C[:, 1] - c[1]) <= r; m |= hit; hits.append(dict(kind='bay', id=bay['id'], centre=[r2(c[0]), r2(c[1])], radius_m=r, stations=int(hit.sum())))
        dc = ops.get('drop_chain') or {}
        if dc and stage_rank(dc.get('stage', '1b')) <= stage_rank(apply_stage):
            for lg in dc.get('ledges', []):
                c = np.asarray(lg['centre_xz'], float); r = math.hypot(lg.get('along_m', 3.5) / 2, lg.get('out_m', 3.0)) + K['ledge_pad_m']
                hit = np.hypot(C[:, 0] - c[0], C[:, 1] - c[1]) <= r; m |= hit; hits.append(dict(kind='ledge', id=lg['id'], centre=[r2(c[0]), r2(c[1])], radius_m=r2(r), stations=int(hit.sum())))
            if dc.get('landing'):
                c = np.asarray(dc['landing']['xz'], float); hit = np.hypot(C[:, 0] - c[0], C[:, 1] - c[1]) <= K['landing_r_m']; m |= hit
                hits.append(dict(kind='landing', id=dc['id'], centre=[r2(c[0]), r2(c[1])], radius_m=K['landing_r_m'], stations=int(hit.sum())))
    for e in G.get('extra_keepouts', []):
        c = np.asarray(e['xz'], float); hit = np.hypot(C[:, 0] - c[0], C[:, 1] - c[1]) <= e['r']; m |= hit; hits.append(dict(kind='extra', id=e.get('id', ''), centre=[r2(c[0]), r2(c[1])], radius_m=e['r'], stations=int(hit.sum())))
    return m, hits


def lay_out(pc, RL, G, seed):
    """crest sections of every skin run + the column / cell boundaries of the three LODs. Returns a list of sections."""
    ds = pc.ds; Cr = G['crest']; Co = G['columns']; Ti = G['tiers']; out = []; sk = pc.skin; n = pc.ns; i = 0; run_id = 0; omin = Cr['over_min_m'] + Cr['jitter_m']
    while i < n:
        if not sk[i]: i += 1; continue
        e = i
        while e + 1 < n and sk[e + 1]: e += 1
        if (e - i) * ds >= G['min_run_m']:
            R = rng(seed, pc.sid, pc.pi, 'run', run_id); edges = split_range(i, e, Cr['section_m'], ds, Cr['section_m'][0] * 0.6, R); prev_end = None

            def fit(a, b):
                idx = np.arange(a, b + 1); s = pc.s[idx]; rim = pc.rim_eff[idx]; m0 = float(np.clip(np.polyfit(s, rim, 1)[0], -Cr['slope_max'], Cr['slope_max'])) if len(s) > 2 else 0.0
                return m0, float((rim - m0 * (s - s.mean())).max() - (rim - m0 * (s - s.mean())).min())
            q = 0
            while q < len(edges) - 1:      # a section whose rim leaves its straight line by more than sag_max_m is halved (rim jumps, cap ends)
                a, b = edges[q], edges[q + 1]
                if fit(a, b)[1] > (Cr['sag_max_outer_m'] if pc.mode == 'outer' else Cr['sag_max_m']) and (b - a) * ds >= 2 * Cr['section_min_m']: edges.insert(q + 1, (a + b) // 2)
                else: q += 1
            for q in range(len(edges) - 1):
                a, b = edges[q], edges[q + 1]; idx = np.arange(a, b + 1); s = pc.s[idx]; sm = float(s.mean()); rim = pc.rim_eff[idx]
                m = float(fit(a, b)[0] + R.uniform(-Cr['tilt'], Cr['tilt']))
                line0 = float((rim - m * (s - sm)).max()) + m * (s - sm); note = ''
                if prev_end is None: over = float(R.uniform(omin, Cr['over_max_m']))
                else:
                    over = None
                    for _ in range(12):
                        st = float(R.uniform(Cr['step_m'][0], Cr['step_m'][1])); cand = [prev_end + sg * st - float(line0[0]) for sg in (R.permutation([-1.0, 1.0]))]
                        cand = [c for c in cand if omin <= c <= Cr['over_max_m']]
                        if cand: over = float(cand[0]); break
                    if over is None:
                        lo_, hi_ = prev_end - Cr['step_m'][0] - float(line0[0]), prev_end + Cr['step_m'][0] - float(line0[0])
                        over = float(np.clip(lo_ if abs(lo_ - 3) < abs(hi_ - 3) else hi_, omin, max(omin, Cr['over_max_m'] + 2.0))); note = 'step outside the range (rim jump)'
                crest = line0 + over; prev_end = float(crest[-1]); tier = 'A' if float(pc.tier_a[idx].mean()) >= 0.5 else 'B'
                H = crest - pc.yF[idx]; Hm = float(H.mean())
                vtop = float(((np.minimum(pc.rim[idx], pc.y_edge[idx]) - Cr['body_below_rim_m'] - pc.yF[idx]) / H).min()); vbot = float(((pc.y_toe[idx] + 1.5 - pc.yF[idx]) / H).max())
                vbot = min(vbot, 0.45); vtop = max(vtop, vbot + 0.1)

                def bounds(k, Rr):
                    v = np.linspace(0, 1, k + 1)
                    if k > 1: v[1:-1] += Rr.uniform(-Ti['jitter'], Ti['jitter'], k - 1) / k
                    v[1:-1] = np.clip(v[1:-1], vbot, vtop); v = np.unique(np.round(v, 4))
                    keep = [0.0]
                    for x in v[1:-1]:
                        if (x - keep[-1]) * Hm >= Ti['min_m'] and (1.0 - x) * Hm >= Ti['min_m']: keep.append(float(x))
                    return np.array(keep + [1.0])
                sec = dict(a=a, b=b, crest=crest, tier=tier, note=note, run=run_id, over=over, lod={})
                R2 = rng(seed, pc.sid, pc.pi, 'sec', a)
                v2 = bounds(int(Ti['lod2_count']) if (b - a) * ds >= Ti['lod2_single_below_m'] and pc.mode != 'outer' else 1, R2); sec['lod'][2] = [dict(ia=a, ib=b, vb=v2, drop=0.0)]
                c1 = split_range(a, b, Co[tier]['lod1_m'], ds, Co['min_m'], R2); l1 = []; l0 = []
                for u in range(len(c1) - 1):
                    k1 = int(np.clip(round(Hm / R2.uniform(Ti['lod1_m'][0], Ti['lod1_m'][1])), 2, 7)); v1 = bounds(k1, R2); l1.append(dict(ia=c1[u], ib=c1[u + 1], vb=v1, drop=float(R2.uniform(0, Cr['jitter_m'])) if 0 < u < len(c1) - 2 else 0.0))
                    c0 = split_range(c1[u], c1[u + 1], Co[tier]['lod0_m'], ds, Co['min_m'], R2)
                    for w in range(len(c0) - 1):
                        v0 = [0.0]
                        for x0, x1 in zip(v1[:-1], v1[1:]):
                            k0 = int(np.clip(round((x1 - x0) * Hm / R2.uniform(Ti['lod0_m'][0], Ti['lod0_m'][1])), 1, 4)); sub = np.linspace(x0, x1, k0 + 1)[1:]
                            if k0 > 1: sub[:-1] += R2.uniform(-Ti['jitter'], Ti['jitter'], k0 - 1) * (x1 - x0) / k0
                            for x in sub[:-1]:
                                if vbot <= x <= vtop and (x - v0[-1]) * Hm >= Ti['min_m'] and (x1 - x) * Hm >= Ti['min_m']: v0.append(float(x))
                            v0.append(float(x1))
                        l0.append(dict(ia=c0[w], ib=c0[w + 1], vb=np.array(v0), drop=float(R2.uniform(0, Cr['jitter_m'])), edge=(u == 0 and w == 0) or (u == len(c1) - 2 and w == len(c0) - 2)))
                for c_ in l0:
                    if c_['edge']: c_['drop'] = 0.0      # the first and last column of a section keep the section line: the 2-4 m step stays exact
                sec['lod'][1] = l1; sec['lod'][0] = l0; out.append(sec)
            run_id += 1
        i = e + 1
    return out


# ------------------------------------------------------------------ build: pieces -> columns -> panels
def stage_ranker(ops):
    st = ops.get('stages', ['1a', '1b', '2a', '2b', '3'])
    return lambda s: st.index(s) if s in st else len(st)


def build(S, ops, new, h, rules, log=print):
    RL = rules['rules']; G = rules['generator']; seed = G['seed']; rank = stage_ranker(ops); apply_stage = rules['stages']['apply_stage']
    pieces = []; raw = {}; keepouts = []; modes = {d['id']: d.get('mode', 'raise') for d in ops.get('segments', [])}
    for si, st in enumerate(S['stretches']):
        for g in st['guide']:
            pc = Piece(st['id'], g, new, h, RL, G, RL['station_m'], modes.get(st['id'], 'raise'))
            ko, hits = keepout_mask(pc, ops, G, rank, apply_stage); pc.keepout = ko; pc.skin = pc.skin & ~ko; keepouts += [dict(stretch=st['id'], piece=g['piece'], **x) for x in hits if x['stations']]
            secs = lay_out(pc, RL, G, seed); pc.secs = secs; pc.cols = {0: [], 1: [], 2: []}
            pans = [(pi, p) for pi, p in enumerate(st['panels']) if p['piece'] == g['piece']]
            for sec in secs:
                mid = float(pc.gs[(sec['a'] + sec['b']) // 2]); best = None
                for pi, p in pans:
                    d = 0.0 if p['s0'] <= mid <= p['s1'] else min(abs(mid - p['s0']), abs(mid - p['s1'])); c = abs(mid - (p['s0'] + p['s1']) / 2)
                    if best is None or (d, c) < best[0]: best = ((d, c), pi)
                sec['panel'] = None if best is None or best[0][0] > 8.0 else best[1]
            for lod in (0, 1, 2):
                for sec in secs:
                    if sec['panel'] is None: continue
                    for c in sec['lod'][lod]:
                        col = build_column(pc, c['ia'], c['ib'], sec['crest'][c['ia'] - sec['a']:c['ib'] - sec['a'] + 1] - c['drop'], c['vb'], lod, sec['tier'], rng(seed, st['id'], g['piece'], 'col', lod, c['ia']), RL, G)
                        col['sec'] = sec; pc.cols[lod].append(col)
                        if lod == 0: pc.crest[c['ia']:c['ib'] + 1] = np.fmin(pc.crest[c['ia']:c['ib'] + 1], col['crest'])
                cols = pc.cols[lod]
                for q, col in enumerate(cols):
                    left = cols[q - 1] if q > 0 and cols[q - 1]['ib'] == col['ia'] and cols[q - 1]['sec']['run'] == col['sec']['run'] else None
                    right = cols[q + 1] if q + 1 < len(cols) and cols[q + 1]['ia'] == col['ib'] and cols[q + 1]['sec']['run'] == col['sec']['run'] else None
                    key = (si, col['sec']['panel'])
                    if key not in raw: raw[key] = dict(si=si, pi=col['sec']['panel'], acc=[Acc(), Acc(), Acc()], probes=[], tops=[], secs=[], piece=pc)
                    emit_column(pc, col, left, right, raw[key]['acc'][lod], G, raw[key]['probes'] if lod == 0 else None)
            for sec in secs:
                if sec['panel'] is None: continue
                P = raw[(si, sec['panel'])]; P['secs'].append(sec); km = (sec['a'] + sec['b']) // 2; tb = float(pc.w_rim[km]) - 1.0
                x = float(pc.C[km, 0] + pc.N[km, 0] * tb); z = float(pc.C[km, 1] + pc.N[km, 1] * tb)
                ground = float(sample_tri(new, x, z)); top = float(pc.crest[km])      # lowest LOD0 crest at that station
                if top - ground >= RL['over_m']: P['tops'].append((x, z, top, float(pc.rim[km]), ground))      # probe 1 m behind the wall edge (verify: real terrain there)
            pieces.append(pc)
            log(f"  {st['id']} piece {g['piece']}: {pc.ns} stations, skin {int(pc.skin.sum()) * pc.ds:.0f} m, sections {len(secs)}, columns LOD0 {len(pc.cols[0])} / LOD1 {len(pc.cols[1])} / LOD2 {len(pc.cols[2])}, min frame radius {pc.min_radius:.1f} m")
    # groups: an eval panel shorter than panel.min_m joins its neighbour of the same stretch when the gap is small
    Pm = G['panel']; groups = []
    for key in sorted(raw):
        P = raw[key]; pc = P['piece']; ln = sum((s['b'] - s['a']) * pc.ds for s in P['secs']); a0 = min(s['a'] for s in P['secs']); b0 = max(s['b'] for s in P['secs'])
        P['len'] = ln; P['from'] = pc.C[a0]; P['to'] = pc.C[b0]
        last = groups[-1] if groups else None
        if last is not None and last['si'] == P['si'] and (ln < Pm['min_m'] or last['len'] < Pm['min_m']) and float(np.hypot(*(last['to'] - P['from']))) <= Pm['merge_gap_m']:
            for lod in (0, 1, 2): last['acc'][lod].extend(P['acc'][lod])
            last['probes'] += P['probes']; last['tops'] += P['tops']; last['evals'].append(P['pi']); last['len'] += ln; last['to'] = P['to']; last['secs'] += P['secs']
        else:
            groups.append(dict(si=P['si'], evals=[P['pi']], acc=P['acc'], probes=P['probes'], tops=P['tops'], len=ln, to=P['to'], secs=P['secs'], piece_first=pc.pi))
    return pieces, groups, keepouts


# ------------------------------------------------------------------ checks (world space, on the triangles that were written)
def _bary():
    pts = [(0.96, 0.02, 0.02), (0.02, 0.96, 0.02), (0.02, 0.02, 0.96), (0.5, 0.5, 0.0), (0.0, 0.5, 0.5), (0.5, 0.0, 0.5), (1 / 3, 1 / 3, 1 / 3),
           (0.6, 0.2, 0.2), (0.2, 0.6, 0.2), (0.2, 0.2, 0.6), (0.75, 0.25, 0.0), (0.25, 0.75, 0.0), (0.0, 0.75, 0.25), (0.0, 0.25, 0.75), (0.75, 0.0, 0.25), (0.25, 0.0, 0.75)]
    return np.array(pts)


BARY = _bary()


def check_pierce(V, T, K, new, h, eps):
    """terrain inside the skin: a sample point of a face / bedding triangle that lies inside the ADDED cliff mass (above the base
    height field, under the stage surface). A foot buried under the old ground in front of the toe is the design, not a pierce."""
    sel = (K == FRONT) | (K == BEDDING); P = V[T[sel]]
    if len(P) == 0: return dict(tris=0, pierced=0, max_m=0.0, at=[])
    pts = np.einsum('kb,mbc->mkc', BARY, P); hn = sample_tri(new, pts[..., 0], pts[..., 2]); hb = sample_tri(h, pts[..., 0], pts[..., 2])
    dep = np.where(pts[..., 1] > hb + 0.05, hn - pts[..., 1], -1.0); bad = dep > eps; worst = dep.max(1); tb = bad.any(1)
    at = [[r2(pts[i, int(dep[i].argmax()), 0]), r2(pts[i, int(dep[i].argmax()), 1]), r2(pts[i, int(dep[i].argmax()), 2]), r3(worst[i])] for i in np.argsort(-worst)[:5] if worst[i] > eps]
    return dict(tris=int(len(P)), pierced=int(tb.sum()), max_m=r3(max(0.0, float(worst.max()))), at=at)


def check_front(V, T, K, F, new, step, reach):
    """front distance of the face triangles between toe and rim: march from the centroid against the frame normal until the
    triangulated terrain reaches the centroid's height (an independent check of the frame arithmetic, in world space)."""
    sel = (K == FRONT) & (F[:, 3] > 0.999); P = V[T[sel]]; f2 = F[sel, :2]
    if len(P) == 0: return np.zeros(0), np.zeros(0), np.zeros((0, 3)), 0, 0
    c = P.mean(1); area = np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1) / 2; tau = np.arange(0.0, reach + 1e-9, step); d = np.full(len(c), np.nan)
    for a in range(0, len(c), 20000):
        b = min(len(c), a + 20000); x = c[a:b, 0:1] - f2[a:b, 0:1] * tau[None]; z = c[a:b, 2:3] - f2[a:b, 1:2] * tau[None]
        hit = sample_tri(new, x, z) >= c[a:b, 1:2]; first = np.where(hit.any(1), hit.argmax(1), -1); d[a:b] = np.where(first >= 0, tau[np.clip(first, 0, None)], np.nan)
    buried = d == 0.0; nowall = np.isnan(d); ok = ~buried & ~nowall
    return d[ok], area[ok], c[ok], int(buried.sum()), int(nowall.sum())


def check_ledge(V, T, F, slope_deg, depth_m):
    """AC-B10a, applied to the WHOLE skin (not only within 3.45 m of walkable ground): an up-facing triangle (flatter than slope_deg)
    whose narrowest width in plan (its smallest altitude) exceeds depth_m is a surface one could read as a ledge."""
    P = V[T]; n = unit(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0])); up = n[:, 1] > math.cos(math.radians(slope_deg)); H2 = P[:, :, [0, 2]]
    e = np.stack([np.linalg.norm(H2[:, 1] - H2[:, 0], axis=1), np.linalg.norm(H2[:, 2] - H2[:, 1], axis=1), np.linalg.norm(H2[:, 0] - H2[:, 2], axis=1)], 1)
    a2 = np.abs((H2[:, 1, 0] - H2[:, 0, 0]) * (H2[:, 2, 1] - H2[:, 0, 1]) - (H2[:, 1, 1] - H2[:, 0, 1]) * (H2[:, 2, 0] - H2[:, 0, 0])); dep = a2 / np.maximum(e.max(1), 1e-9); bad = up & (dep > depth_m)
    return dict(up_facing=int(up.sum()), max_depth_m=r3(float(dep[up].max()) if up.any() else 0.0), ledges=int(bad.sum()),
                at=[[r2(v) for v in P[i].mean(0)] + [r3(dep[i])] for i in np.where(bad)[0][:5]])


def check_tops(pieces, RL):
    over = []; guide = []; runs = []; steps = []; notes = 0
    for pc in pieces:
        m = pc.skin & ~np.isnan(pc.crest)
        if m.any(): over.append(pc.crest[m] - pc.rim[m]); guide.append(pc.crest[m] - (pc.guide_top[m] - RL['over_m']))
        prev = None
        for sec in pc.secs:
            runs.append((float((sec['b'] - sec['a']) * pc.ds), pc.sid, [r2(v) for v in pc.C[sec['a']]])); notes += 1 if sec['note'] else 0
            if prev is not None and prev['run'] == sec['run'] and prev['b'] == sec['a']: steps.append(abs(float(sec['crest'][0] - prev['crest'][-1])))
            prev = sec
    over = np.concatenate(over) if over else np.zeros(1); guide = np.concatenate(guide) if guide else np.zeros(1); steps = np.array(steps) if steps else np.zeros(1); runs.sort(reverse=True)
    return dict(top_over_rim_m_min_p50_max=[r2(over.min()), r2(np.median(over)), r2(over.max())], top_over_guide_wall_top_m_min=r2(guide.min()), rule_over_m=RL['over_m'],
                top_ok=bool(over.min() >= RL['over_m'] - 1e-6), longest_straight_crest_m=r2(runs[0][0]) if runs else 0.0, longest_at=dict(stretch=runs[0][1], xz=runs[0][2]) if runs else None,
                crest_sections=len(runs), crest_step_m_min_p50_max=[r2(steps.min()), r2(np.median(steps)), r2(steps.max())], crest_steps_outside_range=int(notes))


def signature(pc, step=0.5):
    """what one sees along a stretch, as numbers: skin offset in front of the wall at three heights + crest height over the rim."""
    k = int(round(step / pc.ds)); idx = np.arange(0, pc.ns, k); sig = np.full((len(idx), 5), np.nan)
    for col in pc.cols[0]:
        sel = np.where((idx >= col['ia']) & (idx <= col['ib']))[0]
        if len(sel) == 0: continue
        loc = idx[sel] - col['ia']; ybot = pc.yF[idx[sel]]; H = col['crest'][loc] - ybot
        for q, v in enumerate((0.25, 0.5, 0.75)):
            y = ybot + v * H
            for c in col['cells']:
                m = (y >= c['y0'][loc]) & (y <= c['y1'][loc])
                if m.any():
                    t = c['g'][loc[m]] + c['b'][loc[m]] * (y[m] - (c['y0'][loc[m]] + c['y1'][loc[m]]) / 2); sig[sel[m], q] = t - pc.wq(idx[sel[m]], y[m])
                    if q == 1: sig[sel[m], 4] = t
        sig[sel, 3] = col['crest'][loc] - pc.rim[idx[sel]]
    return sig


def check_repeat(pieces, Rp):
    """AC-B21: normalised cross-correlation of every pair of windows (window_m long, step_m apart) that lie within `within_m` of each
    other and do not overlap, on (a) the generator's own signature (offsets at 3 heights + crest) and (b) the plan offset alone."""
    out = dict(stretch=Rp['stretch'], window_m=Rp['window_m'], within_m=Rp['within_m'], ncc_max_rule=Rp['ncc_max'], pieces=[])
    for pc in pieces:
        if pc.sid != Rp['stretch']: continue
        sig = signature(pc); n = int(round(Rp['window_m'] / 0.5)); st = int(round(Rp['step_m'] / 0.5)); starts = [a for a in range(0, len(sig) - n + 1, st) if not np.isnan(sig[a:a + n, :4]).any()]
        if len(starts) < 2: continue

        def norm(W):
            W = W - W.mean(1, keepdims=True); return W / np.maximum(np.linalg.norm(W, axis=1, keepdims=True), 1e-9)
        A = np.stack([np.concatenate([norm(sig[a:a + n, c][None])[0] for c in range(4)]) / 2.0 for a in starts]); Pn = np.stack([norm(sig[a:a + n, 4][None])[0] for a in starts])
        pos = np.array(starts) * 0.5; D = np.abs(pos[:, None] - pos[None]); pair = (D >= Rp['window_m']) & (D <= Rp['within_m'])
        Ca = A @ A.T; Cp = Pn @ Pn.T; Ca[~pair] = -2; Cp[~pair] = -2; ia = np.unravel_index(int(Ca.argmax()), Ca.shape); ip = np.unravel_index(int(Cp.argmax()), Cp.shape)
        out['pieces'].append(dict(piece=pc.pi, windows=len(starts), pairs=int(pair.sum() // 2), skin_signature_ncc_max=r3(Ca.max()), skin_signature_ncc_p99=r3(np.percentile(Ca[pair], 99)),
                                  skin_signature_worst_pair_s_m=[r2(pos[ia[0]]), r2(pos[ia[1]])], plan_offset_ncc_max=r3(Cp.max()), plan_offset_ncc_p99=r3(np.percentile(Cp[pair], 99)), plan_offset_worst_pair_s_m=[r2(pos[ip[0]]), r2(pos[ip[1]])]))
    mx = max([p['skin_signature_ncc_max'] for p in out['pieces']] or [0.0]); out['ok'] = bool(mx <= Rp['ncc_max']); out['skin_signature_ncc_max'] = mx
    return out


def visibility(panels, rules):
    """triangles and batches of the skins from a point: horizontal frustum + far plane + the LOD the LODGroup would pick. No occlusion."""
    Vv = rules['checks']['visibility']; Ld = rules['lod']; Sh = rules['share']; half = math.atan(math.tan(math.radians(Ld['ref_fov_deg']) / 2) * Vv['aspect']); tanv = math.tan(math.radians(Ld['ref_fov_deg']) / 2)
    views = [(p['id'], p['feet'][0], p['feet'][1] + Vv['eye_m'], p['feet'][2], p['yaws']) for p in Vv['perf_stations']] + [(p['id'], p['xz'][0], None, p['xz'][1], []) for p in Vv['boundary_points']]

    def one(x, y, z, yaw, bias):
        d = np.array([math.sin(math.radians(yaw)), math.cos(math.radians(yaw))]); tris = 0; rend = 0; lods = [0, 0, 0]
        for p in panels:
            c = np.array(p['centre']); e = np.array(p['extent']); cor = np.array([[c[0] + sx * e[0], c[2] + sz * e[2]] for sx in (-1, 1) for sz in (-1, 1)] + [[c[0], c[2]]]) - np.array([x, z])
            dist = np.hypot(cor[:, 0], cor[:, 1]); ang = np.arccos(np.clip((cor @ d) / np.maximum(dist, 1e-6), -1, 1))
            if not ((ang <= half) & (dist <= Vv['far_m'])).any(): continue
            dc = math.sqrt((c[0] - x) ** 2 + (c[2] - z) ** 2 + ((c[1] - y) ** 2 if y is not None else 0.0)); rel = p['size'] / max(dc, 1e-3) / (2 * tanv) * bias
            k = 0 if rel >= p['screen'][0] else 1 if rel >= p['screen'][1] else 2
            if p['screen'][2] > 0 and rel < p['screen'][2]: continue
            tris += p['lods'][k]['triangles']; rend += 1; lods[k] += 1
        return tris, rend, lods
    rows = []
    for vid, x, y, z, yaws in views:
        for name, bias in (('PC', Ld['ref_lod_bias']), ('Mobile', Ld['mobile_lod_bias'])):
            sweep = [(one(x, y, z, yw, bias), yw) for yw in np.arange(0.0, 360.0, Vv['yaw_step_deg'])]; worst = max(sweep, key=lambda t_: (t_[0][0], t_[0][1]))
            rows.append(dict(point=vid, quality=name, worst_yaw_deg=r2(worst[1]), worst_triangles=int(worst[0][0]), worst_renderers=int(worst[0][1]), worst_batches=int(worst[0][1] * Sh['passes_per_renderer']), worst_lod_mix=worst[0][2],
                             station_yaws=[dict(yaw=yw, triangles=int(one(x, y, z, yw, bias)[0]), renderers=int(one(x, y, z, yw, bias)[1])) for yw in yaws]))
    rows.sort(key=lambda r_: -r_['worst_triangles'])
    return rows


# ------------------------------------------------------------------ outputs
class Sink:
    """writes files, or (compare mode) checks that what would be written equals what is on disk."""
    def __init__(self, compare):
        self.compare = compare; self.diff = []; self.same = 0; self.paths = []

    def data(self, path, data):
        path = Path(path); self.paths.append(path)
        if self.compare:
            if path.exists() and path.read_bytes() == data: self.same += 1
            else: self.diff.append(B.rel(path))
            return
        B.guard_out(path); path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)

    def text(self, path, text):
        self.data(path, text.replace('\r\n', '\n').encode('utf-8'))

    def json(self, path, obj, indent=1):
        self.text(path, json.dumps(obj, ensure_ascii=False, indent=indent, default=B._js) + '\n')


def kit_json(name, V, Nn, T, slot):
    f = lambda a: [round(float(x), 3) for x in np.asarray(a).reshape(-1)]
    return json.dumps(dict(name=name, v=f(V), n=f(Nn), uv=[], sub=[dict(m=slot, t=[int(i) for i in np.asarray(T).reshape(-1)])]), separators=(',', ':'))


def export(groups, S, rules, out_dir, stage, new, h, sink, log=print):
    """per panel: checks in world space, KitMesh files local to the panel pivot, the unity.json row."""
    U = rules['unity']; Ld = rules['lod']; Ck = rules['checks']; Sh = rules['share']; RL = rules['rules']; tanv = math.tan(math.radians(Ld['ref_fov_deg']) / 2)
    panels = []; count = {}; chk = []; fd = []; fa = []; sa = []; saa = []; over_list = []; march_list = []
    for gr in groups:
        st = S['stretches'][gr['si']]; sid = st['id']; k = count.get(sid, 0); count[sid] = k + 1; pid = '%s_%02d' % (sid, k)
        arr = [gr['acc'][lod].merged() for lod in (0, 1, 2)]; allv = np.concatenate([a[0] for a in arr]); mn = allv.min(0); mx = allv.max(0)
        pivot = np.array([math.floor((mn[0] + mx[0]) / 2), math.floor(mn[1]), math.floor((mn[2] + mx[2]) / 2)], float)
        caps = [int(sum(st['panels'][e][key] for e in gr['evals'])) for key in ('lod0_tris_cap', 'lod1_tris_cap', 'lod2_tris_cap')]
        tier = 'A' if sum(1 for s_ in gr['secs'] if s_['tier'] == 'A') * 2 >= len(gr['secs']) else 'B'; lods = []; row = dict(panel=pid, stretch=sid)
        for lod in (0, 1, 2):
            V, Nn, T, K, F = arr[lod]; name = '%s_LOD%d' % (pid, lod); text = kit_json(name, V - pivot, Nn, T, U['slot']); data = text.encode('utf-8')
            sink.data(out_dir / 'Meshes' / (name + '.json'), data)
            lods.append(dict(file=name + '.json', sha256=hashlib.sha256(data).hexdigest(), vertices=int(len(V)), triangles=int(len(T)), front=int((K == FRONT).sum()), bedding=int((K == BEDDING).sum()), end=int((K == END).sum()), cap=int((K == CAP).sum()),
                             indexBytes=2 if len(V) < 65536 else 4))
            pr = check_pierce(V, T, K, new, h, Ck['pierce_eps_m']); lg = check_ledge(V, T, F, Ck['ledge_slope_deg'], Ck['ledge_depth_m']); row['lod%d' % lod] = dict(pierce=pr, ledge=lg, triangles=int(len(T)), cap=caps[lod], in_cap=bool(len(T) <= caps[lod]))
            if lod == 0:
                d, a, c, buried, nowall = check_front(V, T, K, F, new, Ck['front_march_m'], Ck['front_reach_m']); fd.append(d); fa.append(a); over = d > RL['max_front_m'] + 1e-9
                fr = K == FRONT; Pf = V[T[fr]]; so = F[fr, 2]; ar = np.linalg.norm(np.cross(Pf[:, 1] - Pf[:, 0], Pf[:, 2] - Pf[:, 0]), axis=1) / 2; sa.append(so); saa.append(ar); ov = so > RL['max_front_m'] + 1e-9
                row['front'] = dict(frame_triangles=int(len(so)), frame_p50=r2(np.percentile(so, 50)), frame_p95=r2(np.percentile(so, 95)), frame_max=r2(so.max()), frame_min=r2(so.min()), frame_over_rule=int(ov.sum()),
                                    march_triangles=int(len(d)), march_p50=r2(np.percentile(d, 50)) if len(d) else 0.0, march_p95=r2(np.percentile(d, 95)) if len(d) else 0.0, march_max=r2(d.max()) if len(d) else 0.0,
                                    march_buried=buried, march_no_wall=nowall, march_over_rule=int(over.sum()))
                cf = Pf.mean(1)
                for i in np.where(ov)[0]: over_list.append(dict(panel=pid, xyz=[r2(v) for v in cf[i]], front_m=r2(so[i]), area_m2=r2(ar[i])))
                for i in np.where(over)[0]: march_list.append(dict(panel=pid, xyz=[r2(v) for v in c[i]], front_m=r2(d[i]), area_m2=r2(a[i])))
        chk.append(row); size = float((mx - mn).max())
        s1 = float(np.clip(size * Ld['ref_lod_bias'] / (2 * tanv * Ld['d1_m']), 0.03, 0.95)); s2 = float(np.clip(size * Ld['ref_lod_bias'] / (2 * tanv * Ld['d2_m']), 0.01, s1 * 0.8))
        pb = gr['probes']; npb = int(U['probes_per_panel']); sel = [pb[int(round(q))] for q in np.linspace(0, len(pb) - 1, min(npb, len(pb)))] if pb else []
        tp = gr['tops']; tsel = [tp[int(round(q))] for q in np.linspace(0, len(tp) - 1, min(8, len(tp)))] if tp else []
        panels.append(dict(id=pid, stretch=sid, tier=tier, evalPanels=[int(e) for e in gr['evals']], lengthM=r2(gr['len']), position=[float(v) for v in pivot], boundsMin=[r2(v) for v in mn], boundsMax=[r2(v) for v in mx],
                           centre=[r2(v) for v in (mn + mx) / 2], extent=[r2(v) for v in (mx - mn) / 2], size=r2(size), screen=[r3(s1), r3(s2), float(Ld['cull'])],
                           switchPcM=[r2(size * Ld['ref_lod_bias'] / (2 * tanv * s1)), r2(size * Ld['ref_lod_bias'] / (2 * tanv * s2))], lods=lods, caps=caps,
                           probes=[r3(v) for p in sel for v in p], tops=[r3(v) for p in tsel for v in p]))
    cat = lambda L_, z: np.concatenate(L_) if L_ and sum(len(x) for x in L_) else z
    return panels, chk, dict(march=cat(fd, np.zeros(1)), march_area=cat(fa, np.ones(1)), frame=cat(sa, np.zeros(1)), frame_area=cat(saa, np.ones(1)), over_list=over_list, march_list=march_list)


def render(tris, cols, eye, target, fov, w, h):
    """tiny z-buffer renderer (flat shading, back faces culled with Unity's winding, ink lines on depth / normal breaks)."""
    eye = np.asarray(eye, float); f = unit(np.asarray(target, float) - eye); r = unit(np.cross(UP, f)); u = np.cross(f, r)
    P = tris - eye; X = P @ r; Y = P @ u; Z = P @ f; n = unit(np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0]))
    keep = (Z > 0.5).all(1) & (((eye - tris[:, 0]) * n).sum(1) > 0); X = X[keep]; Y = Y[keep]; Z = Z[keep]; n = n[keep]; cols = cols[keep]
    th = math.tan(math.radians(fov) / 2); px = (X / (Z * th * w / h) * 0.5 + 0.5) * w; py = (0.5 - Y / (Z * th) * 0.5) * h
    light = unit(-0.55 * f + 0.65 * UP + 0.5 * r); shade = 0.38 + 0.62 * np.clip(n @ light, 0, 1)      # from behind the eye, up and to the right: shape, not time of day
    zb = np.full((h, w), np.inf); img = np.empty((h, w, 3)); img[:] = np.array([0.80, 0.82, 0.78]); nb = np.zeros((h, w, 3))
    x0 = np.clip(np.floor(px.min(1)).astype(int), 0, w - 1); x1 = np.clip(np.ceil(px.max(1)).astype(int), 0, w - 1); y0 = np.clip(np.floor(py.min(1)).astype(int), 0, h - 1); y1 = np.clip(np.ceil(py.max(1)).astype(int), 0, h - 1)
    vis = (px.max(1) >= 0) & (px.min(1) <= w - 1) & (py.max(1) >= 0) & (py.min(1) <= h - 1)
    for i in np.where(vis)[0]:
        gx, gy = np.meshgrid(np.arange(x0[i], x1[i] + 1) + 0.5, np.arange(y0[i], y1[i] + 1) + 0.5); ax, ay, bx, by, cx, cy = px[i, 0], py[i, 0], px[i, 1], py[i, 1], px[i, 2], py[i, 2]
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-12: continue
        w0 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / den; w1 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / den; w2 = 1 - w0 - w1
        m = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not m.any(): continue
        z = 1.0 / (w0 / Z[i, 0] + w1 / Z[i, 1] + w2 / Z[i, 2]); sub = zb[y0[i]:y1[i] + 1, x0[i]:x1[i] + 1]; m &= z < sub
        if not m.any(): continue
        sub[m] = z[m]; img[y0[i]:y1[i] + 1, x0[i]:x1[i] + 1][m] = cols[i] * shade[i]; nb[y0[i]:y1[i] + 1, x0[i]:x1[i] + 1][m] = n[i]
    # ink lines: depth steps and normal breaks between neighbouring pixels
    zf = np.where(np.isinf(zb), 1e6, zb); edge = np.zeros((h, w), bool)
    for ax_ in (0, 1):
        a = np.roll(zf, 1, ax_); dz = np.abs(zf - a) / np.minimum(zf, a); dn = (nb * np.roll(nb, 1, ax_)).sum(2)
        edge |= (dz > 0.012) | ((dn < 0.9) & (zf < 1e5) & (a < 1e5))
    edge[0, :] = edge[:, 0] = False; img[edge] = img[edge] * 0.35
    return (np.clip(img, 0, 1) * 255).astype(np.uint8)


def terrain_tris(new, h, x0, z0, x1, z1):
    j0 = max(0, int(x0 // 4)); j1 = min(B.W - 2, int(x1 // 4)); i0 = max(0, int(z0 // 4)); i1 = min(B.HH - 2, int(z1 // 4))
    jj, ii = np.meshgrid(np.arange(j0, j1 + 1), np.arange(i0, i1 + 1)); jj = jj.ravel(); ii = ii.ravel()
    v = lambda di, dj: np.stack([(jj + dj) * 4.0, new[ii + di, jj + dj], (ii + di) * 4.0], 1)
    v00, v10, v01, v11 = v(0, 0), v(0, 1), v(1, 0), v(1, 1); T = np.concatenate([np.stack([v00, v01, v10], 1), np.stack([v10, v01, v11], 1)])
    rz = lambda di, dj: (new[ii + di, jj + dj] - h[ii + di, jj + dj]) > 0.5
    r00, r10, r01, r11 = rz(0, 0), rz(0, 1), rz(1, 0), rz(1, 1); cnt = np.concatenate([r00.astype(int) + r01 + r10, r10.astype(int) + r01 + r11])
    col = np.where((cnt == 3)[:, None], np.array([0.74, 0.72, 0.66]), np.where((cnt > 0)[:, None], np.array([0.66, 0.65, 0.61]), np.array([0.80, 0.78, 0.70])))
    return T, col


def previews(groups, rules, out_dir, new, h, sink, log=print):
    from PIL import Image
    import io
    Pv = rules.get('previews')
    if not Pv: return []
    out = []; world = {}
    for lod in (0, 1, 2):
        Vs = []
        for gr in groups:
            V, Nn, T, K, F = gr['acc'][lod].merged(); Vs.append(V[T])
        world[lod] = np.concatenate(Vs) if Vs else np.zeros((0, 3, 3))
    for v in Pv['views']:
        ex, ez = v['eye']; tx, tz = v['target']; eye = np.array([ex, float(sample_tri(new, ex, ez)) + v.get('eye_up', 1.6), ez]); tgt = np.array([tx, float(sample_tri(new, tx, tz)) + v.get('target_up', 10.0), tz])
        pad = v.get('pad_m', 110.0); bx0, bx1 = min(ex, tx) - pad, max(ex, tx) + pad; bz0, bz1 = min(ez, tz) - pad, max(ez, tz) + pad
        T, col = terrain_tris(new, h, bx0, bz0, bx1, bz1); tris = [T]; cols = [col]
        if v.get('skin', True):
            W = world[int(v.get('lod', 0))]; c = W.mean(1); m = (c[:, 0] >= bx0) & (c[:, 0] <= bx1) & (c[:, 2] >= bz0) & (c[:, 2] <= bz1); tris.append(W[m]); cols.append(np.tile(np.array([0.47, 0.44, 0.40]), (int(m.sum()), 1)))
        img = render(np.concatenate(tris), np.concatenate(cols), eye, tgt, Pv['fov'], Pv['width'], Pv['height']); buf = io.BytesIO(); Image.fromarray(img).save(buf, format='PNG', optimize=False)
        name = 'previews/%s.png' % v['name']; sink.data(out_dir / name, buf.getvalue()); out.append(dict(file=name, eye=[r2(x) for x in eye], target=[r2(x) for x in tgt], lod=int(v.get('lod', 0)), skin=bool(v.get('skin', True)), note=v.get('note', '')))
        log('  preview ' + name)
    return out


def shots(rules, new, stage):
    Sh = rules['shots']; out = []
    for group in ('sheet', 'reveal'):
        for v in Sh[group]:
            ex, ez = v['eye']; tx, tz = v['target']; ey = float(sample_tri(new, ex, ez)) + Sh['eye_m']; ty = float(sample_tri(new, tx, tz)) + v['target_up']
            for variant in ('bare', 'skin', 'skinmobile'):
                name = 'skins%s_%s_%s' % (stage, v['name'], variant)
                out.append(dict(group=group, name=name, variant=variant, eye=[r2(ex), r2(ey), r2(ez)], target=[r2(tx), r2(ty), r2(tz)], note=v.get('note', ''),
                                command='shot:%s:%.2f,%.2f,%.2f:%.2f,%.2f,%.2f:fov=%d:w=%d:h=%d:hideplayer' % (name, ex, ey, ez, tx, ty, tz, Sh['fov'], Sh['width'], Sh['height'])))
    return dict(id='shots308_skins', stage=stage, type='Oheangbu.EditorTools.WorldMacro.Presentation297', method='Run', eye_above_terrain_m=Sh['eye_m'],
                variants=dict(bare='no preview up (the scene as it is)', skin='after CliffSkins308 preview:on:stage=%s (Presentation297 shoots at PC quality: PC LOD distances)' % stage,
                              skinmobile='after CliffSkins308 preview:on:stage=%s:lod=mobile (the LOD the Mobile quality level would pick; the Mobile pipeline asset itself is NOT reproduced by Presentation297)' % stage),
                order=['Presentation297 shots of variant bare', 'CliffSkins308 preview:on:stage=%s' % stage, 'shots of variant skin', 'CliffSkins308 preview:on:stage=%s:lod=mobile' % stage, 'shots of variant skinmobile', 'CliffSkins308 preview:off'],
                shots=out)


def coarse_cover(groups, new, steps=(10.0, 20.0)):
    """[I] how much of the skin a COARSER terrain LOD would hide: the tile LOD1 / LOD2 meshes have 10 m / 20 m quads, so their cliff
    face is a ramp 10 / 20 m wide instead of 4 m and reaches further out than the collider face the skin stands on. Estimate only:
    coarse vertices = the stage field sampled at multiples of the step, same quad split as the 4 m tiles."""
    out = {}
    for step in steps:
        nx = int(4000 // step) + 1; nz = int(6000 // step) + 1; gx, gz = np.meshgrid(np.arange(nx) * step, np.arange(nz) * step); H = sample_tri(new, np.clip(gx, 0, 3999.99), np.clip(gz, 0, 5999.99))

        def surf(x, z):
            j = np.clip(x / step, 0, nx - 1.001); i = np.clip(z / step, 0, nz - 1.001); j0 = np.floor(j).astype(int); i0 = np.floor(i).astype(int); fx = j - j0; fz = i - i0
            h00 = H[i0, j0]; h10 = H[i0, j0 + 1]; h01 = H[i0 + 1, j0]; h11 = H[i0 + 1, j0 + 1]
            return np.where(fx + fz <= 1, h00 + (h10 - h00) * fx + (h01 - h00) * fz, h11 + (h01 - h11) * (1 - fx) + (h10 - h11) * (1 - fz))
        row = {}
        for lod in (0, 1, 2):
            hid = 0.0; tot = 0.0
            for gr in groups:
                V, Nn, T, K, F = gr['acc'][lod].merged(); P = V[T[K == FRONT]]
                if len(P) == 0: continue
                c = P.mean(1); a = np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1) / 2; above = c[:, 1] > sample_tri(new, c[:, 0], c[:, 2])
                hid += float(a[above & (surf(c[:, 0], c[:, 2]) > c[:, 1] + 0.05)].sum()); tot += float(a[above].sum())
            row['skin_lod%d_face_area_hidden_share' % lod] = round(hid / max(tot, 1e-9), 3)
        out['terrain_quad_%dm' % int(step)] = row
    return out


def report_md(rep, unity, rules):
    L = []; A = L.append; t = rep['totals']; sh = rules['share']; f = rep['front']; tp = rep['tops']; ac = rep['acceptance']; RL = rules['rules']
    ok = lambda b: 'OK' if b else 'FAIL'
    A('# 암면 껍질(rock skin) 생성 보고 - 단계 %s' % rep['stage']); A('')
    A('상태: **IMPLEMENTED**(오프라인 - 4 m 격자 삼각 지형 기준). 엔진 실측 0건. 표기 [O] = 이 도구가 잰 값, [I] = 추정.'); A('')
    A('- 높이장 `%s` sha256 `%s`' % (rep['height'], rep['height_sha256'])); A('- ops `%s` sha256 `%s`' % (rep['ops'], rep['ops_sha256']))
    A('- 규칙 `%s` sha256 `%s`' % (rep['rules'], rep['rules_sha256'])); A('- 안내선 `%s` (cliff308_eval.py --skins, 패널 %d, LOD0 상한 합 %d)' % (rep['guide'], rep['guide_totals']['instances'], rep['guide_totals']['lod0_tris'])); A('')
    A('## 1. 합계와 예산 [O]'); A(''); A('| 항목 | 값 | 한도 | 판정 |'); A('|---|---|---|---|')
    A('| 패널(렌더러 묶음 = LODGroup) | %d | %d | %s |' % (t['panels'], sh['renderers_max'], ok(ac['renderers_ok'])))
    A('| LOD0 삼각형 | %d | %d | %s |' % (t['lod_triangles'][0], sh['lod0_tris_max'], ok(ac['lod0_tris_ok'])))
    A('| LOD1 / LOD2 삼각형 | %d / %d | 패널별 상한 | %s |' % (t['lod_triangles'][1], t['lod_triangles'][2], ok(ac['caps_ok'])))
    A('| 꼭짓점 LOD0 / 1 / 2 | %d / %d / %d | 메시당 65,535 미만(16비트 색인) | %s |' % (t['lod_vertices'][0], t['lod_vertices'][1], t['lod_vertices'][2], ok(ac['index16_ok'])))
    A('| 메시 메모리(전 LOD, 꼭짓점 %d B + 색인 2 B) [I] | %.2f MB | %.1f MB | %s |' % (sh['bytes_per_vertex'], t['mesh_memory_mb'], sh['mesh_memory_mb_max'], ok(ac['memory_ok'])))
    A('| 재질 / 그림자 캐스터 / 충돌체 | %d / %d / %d | %d / %d / %d | OK |' % (t['materials'], t['shadow_casters'], t['colliders'], sh['materials_max'], sh['shadow_casters_max'], sh['colliders_max']))
    A('| 껍질 길이 | %.0f m | - | - |' % t['skin_m']); A('')
    A('LOD0은 안내선 상한(%d)의 %.1f%%만 쓴다. 꼭짓점 형식 = 위치 + 법선(셰이더 HighlandNatural이 POSITION·NORMAL만 읽는다 - UV·탄젠트 없음).' % (rep['guide_totals']['lod0_cap'], 100 * t['lod0_share_of_guide_cap'])); A('')
    A('## 2. 구간별 [O]'); A(''); A('| 구간 | 패널 | 길이 m | LOD0 | LOD1 | LOD2 | LOD0 상한 합 | 메모리 MB | 관통 | 선반 | 앞섬 초과(틀 / 행군) |'); A('|---|---|---|---|---|---|---|---|---|---|---|')
    for k, e in rep['per_stretch'].items():
        A('| %s | %d | %.0f | %d | %d | %d | %d | %.2f | %d | %d | %d / %d |' % (k, e['panels'], e['length_m'], e['lod0'], e['lod1'], e['lod2'], e['cap0'], e['memory_bytes'] / 1048576.0, e['pierced'], e['ledges'], e['front_over'], e.get('march_over', 0)))
    A(''); A('## 3. 검사 [O]'); A(''); A('| 검사 | 결과 | 판정 |'); A('|---|---|---|')
    A('| 관통(지형이 껍질 안으로) - 전 LOD, 면·층리 삼각형마다 16점 | %d | %s |' % (sum(e['pierced'] for e in rep['per_stretch'].values()), ok(ac['pierce_0'])))
    A('| 앞섬(틀 법선, LOD0 면 조각 %d개) 최소 / p50 / p95 / 최대 | %.2f / %.2f / %.2f / %.2f m (규칙 %.1f-%.1f m) | 초과 %d |' % (f['frame']['triangles'], f['frame']['min'], f['frame']['p50'], f['frame']['p95'], f['frame']['max'], RL['clearance_m'], RL['max_front_m'], f['frame']['over_rule']))
    A('| 앞섬(월드 행군, 발치-마루 사이 %d개) p50 / p95 / 최대 | %.2f / %.2f / %.2f m | 초과 %d (면적 %.4f%%) |' % (f['march']['triangles'], f['march']['p50'], f['march']['p95'], f['march']['max'], f['march']['over_rule'], 100 * f['march']['over_rule_area_share']))
    A('| 선반(AC-B10a, 껍질 전체에 적용): 50도보다 누운 윗면의 평면 폭 > 0.6 m | %d (층리 턱 최대 %.3f m) | %s |' % (sum(e['ledges'] for e in rep['per_stretch'].values()), f['bedding_up_depth_frame_max_m'], ok(ac['ledge_0'])))
    A('| 윗선 >= 벽 마루 + %.1f m (최소 / 중앙) | %.2f / %.2f m, 안내선 벽 마루 대비 최소 %.2f m | %s |' % (tp['rule_over_m'], tp['top_over_rim_m_min_p50_max'][0], tp['top_over_rim_m_min_p50_max'][1], tp['top_over_guide_wall_top_m_min'], ok(ac['top_ok'])))
    A('| 가장 긴 곧은 마루 | %.1f m (%s, x %.0f z %.0f) / 한도 %.0f m | %s |' % (tp['longest_straight_crest_m'], tp['longest_at']['stretch'], tp['longest_at']['xz'][0], tp['longest_at']['xz'][1], rules['checks']['straight_crest_max_m'], ok(ac['straight_crest_ok'])))
    A('| 마루 단차(구획 %d개) 최소 / 중앙 / 최대 | %.2f / %.2f / %.2f m, 2-4 m 밖 %d곳(가장자리 높이가 급변하는 곳) | 보고 |' % (tp['crest_sections'], tp['crest_step_m_min_p50_max'][0], tp['crest_step_m_min_p50_max'][1], tp['crest_step_m_min_p50_max'][2], tp['crest_steps_outside_range']))
    rp = rep['repeat']
    for p in rp['pieces']:
        A('| 되풀이(AC-B21) %s: %.0f m 창, %.0f m 안 겹치지 않는 쌍 %d개 | 껍질 서명 NCC 최대 %.3f(p99 %.3f) · 평면 선형 NCC 최대 %.3f / 한도 %.2f | %s |' % (rp['stretch'], rp['window_m'], rp['within_m'], p['pairs'], p['skin_signature_ncc_max'], p['skin_signature_ncc_p99'], p['plan_offset_ncc_max'], rp['ncc_max_rule'], ok(rp['ok'])))
    A('| 패널별 상한(LOD0 / 1 / 2) | %d 패널 전부 검사 | %s |' % (t['panels'], ok(ac['caps_ok']))); A('')
    c = rep['cells']
    A('칸(cell) 수 LOD0 / 1 / 2 = %d / %d / %d. 관 조건(앞섬 · 선반 0.4 m · 오버행 1.4 m)을 함께 못 채워 아래 한계를 따른 칸 %d / %d / %d(최대 벗어남 %.2f / %.2f / %.2f m), 관통 수리(바깥으로 민 칸) %d / %d / %d, 수리 실패 %d.' % (
        c['lod0']['cells'], c['lod1']['cells'], c['lod2']['cells'], c['lod0']['outside_tube'], c['lod1']['outside_tube'], c['lod2']['outside_tube'], c['lod0']['outside_tube_max_m'], c['lod1']['outside_tube_max_m'], c['lod2']['outside_tube_max_m'], c['lod0']['repaired'], c['lod1']['repaired'], c['lod2']['repaired'],
        c['lod0']['repair_failed'] + c['lod1']['repair_failed'] + c['lod2']['repair_failed'])); A('')
    if f['march_over_list']:
        A('### 앞섬 %.1f m 초과 목록(월드 행군, %d개 삼각형)' % (RL['max_front_m'], f['march_over_list_total'])); A(''); A('| 패널 | x | y | z | 앞섬 m | 면적 m2 |'); A('|---|---|---|---|---|---|')
        for o in f['march_over_list']: A('| %s | %.1f | %.1f | %.1f | %.2f | %.2f |' % (o['panel'], o['xyz'][0], o['xyz'][1], o['xyz'][2], o['front_m'], o['area_m2']))
        A('')
    if f['over_list']:
        A('### 앞섬 초과 목록(틀 법선, %d개)' % f['over_list_total']); A(''); A('| 패널 | x | y | z | 앞섬 m |'); A('|---|---|---|---|---|')
        for o in f['over_list'][:40]: A('| %s | %.1f | %.1f | %.1f | %.2f |' % (o['panel'], o['xyz'][0], o['xyz'][1], o['xyz'][2], o['front_m']))
        A('')
    A('## 4. 비운 자리와 이은 자리 [O]'); A('')
    for k in rep['keepouts']: A('- 비움: %s %s `%s` 중심 (%.1f, %.1f) 반경 %.1f m, 측점 %d개(0.25 m)' % (k['stretch'], k['kind'], k['id'], k['centre'][0], k['centre'][1], k['radius_m'], k['stations']))
    A('- 안내선의 짧은 빈칸(격자 한 점 들어간 곳, <= %.0f m)을 이은 측점: %s' % (rules['generator']['bridge_gap_m'], ', '.join('%s %d' % (k, v) for k, v in rep['bridged_gap_stations'].items() if v) or '없음')); A('')
    A('## 5. 보이는 삼각형·배치 추정 [I] (수평 시야 + 거리 LOD, 가림 없음 = 상한)'); A(''); A('| 지점 | 품질 | 최악 방위 | 삼각형 | 렌더러 | 배치(패스 %d) | LOD0 / 1 / 2 패널 | 측정 방위(삼각형 / 렌더러) |' % sh['passes_per_renderer']); A('|---|---|---|---|---|---|---|---|')
    for v in rep['visibility']:
        A('| %s | %s | %.0f | %d | %d | %d | %d / %d / %d | %s |' % (v['point'], v['quality'], v['worst_yaw_deg'], v['worst_triangles'], v['worst_renderers'], v['worst_batches'], v['worst_lod_mix'][0], v['worst_lod_mix'][1], v['worst_lod_mix'][2],
                                                                  ' · '.join('%.0f: %d / %d' % (y['yaw'], y['triangles'], y['renderers']) for y in v['station_yaws']) or '-'))
    A(''); A('## 6. Mobile 지형 LOD 위험 [I]'); A('')
    A('타일 LOD1(10 m 칸) / LOD2(20 m 칸)의 절벽 면은 4 m 한 칸이 아니라 10 / 20 m 폭 비탈이라 충돌 면보다 앞으로 나온다. 그 비탈 밑에 들어가는 껍질 면 넓이 비율(추정):'); A('')
    A('| 지형 칸 | 껍질 LOD0 | 껍질 LOD1 | 껍질 LOD2 |'); A('|---|---|---|---|')
    for k, e in rep['mobile_terrain_lod'].items(): A('| %s | %.1f%% | %.1f%% | %.1f%% |' % (k.replace('terrain_quad_', ''), 100 * e['skin_lod0_face_area_hidden_share'], 100 * e['skin_lod1_face_area_hidden_share'], 100 * e['skin_lod2_face_area_hidden_share']))
    A(''); A('PC는 모든 거리에서 타일 LOD0이라 해당 없다. Mobile에서 타일이 LOD1·2로 바뀌는 거리와 실제 꼭짓점 높이는 편집기에서만 알 수 있다(NOT RUN).'); A('')
    A('## 7. 미리보기(소프트웨어 렌더 - 모양만, 재질·먹 후처리·나무 없음)'); A('')
    for p in rep['previews']: A('- `%s` : %s' % (p['file'], p['note']))
    A(''); A('## 8. 오프라인으로 못 한 것 (NOT RUN)'); A('')
    for s_ in ('수묵 후처리 아래의 실제 룩, PC·Mobile 캡처: `CliffSkins308 preview:on:stage=%s` 뒤 `shots308_skins.json`의 `Presentation297 shot` 명령' % rep['stage'],
               'GPU ms(PerfSweep307 - 사용자가 같은 PC로 게임 중이면 무효), 실제 배치 수·SetPass',
               '실제 충돌체 기준 앞섬·관통·윗선: `CliffSkins308 verify:<scene>`', 'LOD 전환 튐, Mobile 타일 LOD와의 겹침', '메시 임포트: `CliffSkins308 import`'):
        A('- ' + s_)
    A('')
    return chr(10).join(L)


# ------------------------------------------------------------------ main
def run_eval(stage, ops_path, height_path, out_dir, rules_path, rules, force, compare, log=print):
    """guide data: Tools/Art/cliff308_eval.py --skins with this package's rules copy (never the plan/ files)."""
    skins = out_dir / ('skins308_%s.json' % stage); ev = out_dir / '_work' / ('eval_%s.json' % stage)
    if skins.exists() and not force:
        S = json.loads(skins.read_text(encoding='utf-8'))
        if S.get('height_sha256') == B.sha256(height_path) and S.get('ops_sha256') == B.sha256(ops_path) and S.get('rules') == rules['rules']:
            log('guides: %s is up to date (height, ops and rules match)' % B.rel(skins)); return S
    if compare: raise SystemExit('REFUSED: --check needs an up-to-date %s (run the generator first)' % B.rel(skins))
    cmd = [sys.executable, str(TOOLS / 'cliff308_eval.py'), str(ops_path), '--height', str(height_path), '--stage', stage, '--fast', '--out', str(ev), '--skins', str(skins), '--skin-rules', str(rules_path)]
    log('guides: ' + ' '.join(B.rel(c) if Path(c).exists() else c for c in cmd[1:]))
    p = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
    if p.returncode != 0: raise SystemExit('REFUSED: cliff308_eval.py failed\n' + p.stdout[-2000:] + p.stderr[-2000:])
    return json.loads(skins.read_text(encoding='utf-8'))


def main():
    B.utf8()
    ap = argparse.ArgumentParser(description='#308 rock skins of the EA-visible cliff faces (offline)')
    ap.add_argument('--stage', default='1a'); ap.add_argument('--ops'); ap.add_argument('--height'); ap.add_argument('--out'); ap.add_argument('--rules')
    ap.add_argument('--force-eval', action='store_true', help='re-run cliff308_eval.py --skins even when the guide file is up to date')
    ap.add_argument('--check', action='store_true', help='generate in memory and compare every output with the files on disk (writes nothing)')
    ap.add_argument('--no-previews', action='store_true'); ap.add_argument('--only', help='debug: comma list of stretch ids (use with --out; the result is not a deliverable)')
    a = ap.parse_args()
    rules_path = Path(a.rules) if a.rules else RULES; rules = json.loads(rules_path.read_text(encoding='utf-8')); out_dir = Path(a.out) if a.out else OUT / a.stage
    stg = rules['stages'].get(a.stage, {}); ops_path = Path(a.ops) if a.ops else (B.ROOT / stg['ops'] if stg.get('ops') else None); height_path = Path(a.height) if a.height else (B.ROOT / stg['height'] if stg.get('height') else None)
    for what, p in (('ops', ops_path), ('height', height_path)):
        if p is None or not p.exists():
            raise SystemExit('REFUSED: the %s file of stage %s is missing (%s). The terrain package delivers it; then run: python Tools/Art/cliffskin308.py --stage %s' % (what, a.stage, B.rel(p) if p else 'not in the rules', a.stage))
    log = print; log('stage %s | height %s sha256 %s | ops %s sha256 %s' % (a.stage, B.rel(height_path), B.sha256(height_path), B.rel(ops_path), B.sha256(ops_path)))
    S = run_eval(a.stage, ops_path, height_path, out_dir, rules_path, rules, a.force_eval, a.check, log)
    if a.only: S['stretches'] = [s_ for s_ in S['stretches'] if s_['id'] in a.only.split(',')]
    ops = json.loads(ops_path.read_text(encoding='utf-8')); new = B.load_height(height_path); h = B.load_height(); sink = Sink(a.check)
    pieces, groups, keepouts = build(S, ops, new, h, rules, log)
    panels, chk, FR = export(groups, S, rules, out_dir, a.stage, new, h, sink, log)
    RL = rules['rules']; Sh = rules['share']; Ck = rules['checks']; U = rules['unity']
    tot = [sum(p['lods'][k]['triangles'] for p in panels) for k in (0, 1, 2)]; verts = [sum(p['lods'][k]['vertices'] for p in panels) for k in (0, 1, 2)]
    mem = sum(l['vertices'] * Sh['bytes_per_vertex'] + l['triangles'] * 3 * l['indexBytes'] for p in panels for l in p['lods'])
    per = {}
    for p, c in zip(panels, chk):
        e = per.setdefault(p['stretch'], dict(panels=0, length_m=0.0, lod0=0, lod1=0, lod2=0, cap0=0, vertices=0, memory_bytes=0, pierced=0, ledges=0, front_over=0))
        e['panels'] += 1; e['length_m'] = r2(e['length_m'] + p['lengthM']); e['lod0'] += p['lods'][0]['triangles']; e['lod1'] += p['lods'][1]['triangles']; e['lod2'] += p['lods'][2]['triangles']; e['cap0'] += p['caps'][0]
        e['vertices'] += sum(l['vertices'] for l in p['lods']); e['memory_bytes'] += sum(l['vertices'] * Sh['bytes_per_vertex'] + l['triangles'] * 3 * l['indexBytes'] for l in p['lods'])
        e['pierced'] += sum(c['lod%d' % k]['pierce']['pierced'] for k in (0, 1, 2)); e['ledges'] += sum(c['lod%d' % k]['ledge']['ledges'] for k in (0, 1, 2)); e['front_over'] += c['front']['frame_over_rule']; e['march_over'] = e.get('march_over', 0) + c['front']['march_over_rule']
    tops = check_tops(pieces, RL); rep = check_repeat(pieces, Ck['repeat']); vis = visibility(panels, rules)
    def wpct(v, w, q):
        o = np.argsort(v); c = np.cumsum(w[o]) / max(float(w.sum()), 1e-9); return float(v[o][min(len(v) - 1, int(np.searchsorted(c, q)))])
    fs = FR['frame']; fw = FR['frame_area']; fm = FR['march']; mw = FR['march_area']; lim = RL['max_front_m'] + 1e-9
    ledge_frame = max([col.get('ledge', 0.0) for pc in pieces for col in pc.cols[0]] or [0.0])
    front = dict(rule_max_m=RL['max_front_m'], rule_clearance_m=RL['clearance_m'],
                 frame=dict(definition='offset of every LOD0 face facet in front of the wall, along the frame normal, at the facet nodes (wall = outermost point of the triangulated stage terrain at that height; above the rim = the rim edge, below the toe = the toe)',
                            triangles=int(len(fs)), min=r2(fs.min()), p50=r2(np.percentile(fs, 50)), p95=r2(np.percentile(fs, 95)), max=r2(fs.max()), area_weighted_p50=r2(wpct(fs, fw, 0.5)), area_weighted_p95=r2(wpct(fs, fw, 0.95)),
                            over_rule=int((fs > lim).sum()), over_rule_area_share=round(float(fw[fs > lim].sum() / max(float(fw.sum()), 1e-9)), 5)),
                 march=dict(definition='independent world-space check on the written triangles between toe and rim: from the centroid against the frame normal to the triangulated terrain at the centroid height (0.05 m march)',
                            triangles=int(len(fm)), p50=r2(np.percentile(fm, 50)), p95=r2(np.percentile(fm, 95)), max=r2(fm.max()), area_weighted_p50=r2(wpct(fm, mw, 0.5)), area_weighted_p95=r2(wpct(fm, mw, 0.95)),
                            over_rule=int((fm > lim).sum()), over_rule_area_share=round(float(mw[fm > lim].sum() / max(float(mw.sum()), 1e-9)), 5)),
                 over_list_total=len(FR['over_list']), over_list=sorted(FR['over_list'], key=lambda o: -o['front_m'])[:80], march_over_list_total=len(FR['march_list']), march_over_list=sorted(FR['march_list'], key=lambda o: -o['front_m'])[:40],
                 bedding_up_depth_frame_max_m=r3(ledge_frame))
    acceptance = dict(
        pierce_0=bool(sum(e['pierced'] for e in per.values()) == 0), ledge_0=bool(sum(e['ledges'] for e in per.values()) == 0), top_ok=tops['top_ok'],
        straight_crest_ok=bool(tops['longest_straight_crest_m'] <= Ck['straight_crest_max_m']), repeat_ok=rep['ok'],
        caps_ok=bool(all(c['lod%d' % k]['in_cap'] for c in chk for k in (0, 1, 2))), lod0_tris_ok=bool(tot[0] <= Sh['lod0_tris_max']), renderers_ok=bool(len(panels) <= Sh['renderers_max']),
        memory_ok=bool(mem <= Sh['mesh_memory_mb_max'] * 1024 * 1024), index16_ok=bool(all(l['indexBytes'] == 2 for p in panels for l in p['lods'])),
        front_max_ok=bool(front['frame']['over_rule'] == 0 and front['march']['over_rule'] == 0), front_over_listed=bool(front['over_list_total'] == front['frame']['over_rule']))
    unity = dict(format='cb308.skins.unity.1', stage=a.stage, heightSha256=B.sha256(height_path), opsSha256=B.sha256(ops_path), rulesSha256=B.sha256(rules_path), guideSha256=B.sha256(out_dir / ('skins308_%s.json' % a.stage)),
                 generatorSha256=B.sha256(Path(__file__)), meshDir=B.rel(out_dir / 'Meshes'), assetDir=U['asset_dir'] + '/' + a.stage, materialDir=U['material_dir'], materialName=U['material_name'], materialSource=U['material_source'],
                 materialSourceGuid=U['material_source_guid'], slot=U['slot'], root=U['root'], group=U['group'], previewRoot=U['preview_root'], layer=U['layer'], shadowCasting=U['shadow_casting'], receiveShadows=U['receive_shadows'],
                 staticFlags=U['static_flags'], meshReadable=U['mesh_readable'], refLodBias=rules['lod']['ref_lod_bias'], mobileLodBias=rules['lod']['mobile_lod_bias'], refFovDeg=rules['lod']['ref_fov_deg'],
                 clearance=RL['clearance_m'], maxFront=RL['max_front_m'], overM=RL['over_m'], probeStride=6, topStride=5,
                 budget=dict(lod0TrisMax=Sh['lod0_tris_max'], renderersMax=Sh['renderers_max'], meshMemoryBytesMax=int(Sh['mesh_memory_mb_max'] * 1024 * 1024), materialsMax=Sh['materials_max'], shadowCastersMax=Sh['shadow_casters_max'],
                             collidersMax=Sh['colliders_max'], bytesPerVertex=Sh['bytes_per_vertex'], passesPerRenderer=Sh['passes_per_renderer']),
                 totals=dict(panels=len(panels), lod0Triangles=tot[0], lod1Triangles=tot[1], lod2Triangles=tot[2], lod0Vertices=verts[0], lod1Vertices=verts[1], lod2Vertices=verts[2], meshMemoryBytes=int(mem), skinLengthM=r2(sum(p['lengthM'] for p in panels))),
                 panels=panels)
    sink.json(out_dir / 'unity.json', unity)
    pv = [] if a.no_previews else previews(groups, rules, out_dir, new, h, sink, log)
    sink.json(out_dir / 'shots308_skins.json', shots(rules, new, a.stage))
    report = dict(id='skins308_report', stage=a.stage, status='IMPLEMENTED offline (triangulated 4 m lattice); nothing measured in the engine', height=B.rel(height_path), height_sha256=B.sha256(height_path), ops=B.rel(ops_path), ops_sha256=B.sha256(ops_path),
                  rules=B.rel(rules_path), rules_sha256=B.sha256(rules_path), guide=B.rel(out_dir / ('skins308_%s.json' % a.stage)), guide_totals=S['totals'],
                  totals=dict(panels=len(panels), skin_m=r2(sum(p['lengthM'] for p in panels)), lod_triangles=tot, lod_vertices=verts, mesh_memory_mb=round(mem / 1024 / 1024, 2), materials=1, shadow_casters=0, colliders=0,
                              lod0_share_of_guide_cap=round(tot[0] / max(1, S['totals']['lod0_cap']), 3)),
                  per_stretch=per, front=front, tops=tops, repeat=rep, keepouts=keepouts, frame_min_radius_m={'%s/%d' % (pc.sid, pc.pi): r2(pc.min_radius) for pc in pieces},
                  invalid_stations={'%s/%d' % (pc.sid, pc.pi): int((~pc.valid).sum()) for pc in pieces}, bridged_gap_stations={'%s/%d' % (pc.sid, pc.pi): pc.bridged for pc in pieces}, cells={('lod%d' % k): dict(columns=sum(len(pc.cols[k]) for pc in pieces), cells=sum(len(c['cells']) for pc in pieces for c in pc.cols[k]), outside_tube=sum(1 for pc in pieces for c in pc.cols[k] for x in c['cells'] if x['over'] > 0.05),
                                                           outside_tube_max_m=r2(max([x['over'] for pc in pieces for c in pc.cols[k] for x in c['cells']] or [0.0])),
                                                           repaired=sum(1 for pc in pieces for c in pc.cols[k] for x in c['cells'] if x['pushed'] > 0), repair_push_max_m=r2(max([x['pushed'] for pc in pieces for c in pc.cols[k] for x in c['cells']] or [0.0])),
                                                           repair_failed=sum(1 for pc in pieces for c in pc.cols[k] for x in c['cells'] if x['still'])) for k in (0, 1, 2)}, visibility=vis, mobile_terrain_lod=coarse_cover(groups, new), acceptance=acceptance, panels_check=chk, previews=pv)
    sink.json(out_dir / 'report.json', report)
    sink.text(out_dir / 'report.md', report_md(report, unity, rules))
    if not a.check:
        keep = {p.name for p in sink.paths if p.parent.name == 'Meshes'}
        for f in sorted((out_dir / 'Meshes').glob('*.json')):
            if f.name not in keep: f.unlink(); log('  removed stale mesh ' + f.name)
    log('panels %d | LOD0 %d / LOD1 %d / LOD2 %d triangles | memory %.2f MB | pierced %d | ledges %d | front(frame) p50 %.2f p95 %.2f max %.2f (over %d) | crest longest %.1f m | repeat ncc %.3f' % (
        len(panels), tot[0], tot[1], tot[2], mem / 1024 / 1024, sum(e['pierced'] for e in per.values()), sum(e['ledges'] for e in per.values()), front['frame']['p50'], front['frame']['p95'], front['frame']['max'], front['frame']['over_rule'], tops['longest_straight_crest_m'], rep['skin_signature_ncc_max']))
    log('acceptance ' + json.dumps(acceptance))
    if a.check:
        log('check: %d files identical, %d different%s' % (sink.same, len(sink.diff), '' if not sink.diff else ' -> ' + ', '.join(sink.diff[:12])))
        return 0 if not sink.diff else 3
    return 0 if all(v for k_, v in acceptance.items() if k_ != 'front_max_ok') else 2


if __name__ == '__main__':
    sys.exit(main())
