"""#308 cliff skin - `cliffskin308` (SPEC-WORLD-CLIFFS-308 section 12, D308-75). Offline, deterministic, CPU only.
The stage field (n7) stays the body: collision, seal, walk proofs. This writes a CLADDING mesh per listed face: one generated
sheet that follows the terrain face continuously (not rocks placed on it), 0.3 - 2.7 m in front of it, with bedding steps,
leaning joints, master cracks, protruding beds, a blocky cap over the rim, a skirt under the foot and tapered ends.

  python Tools/Art/cliffskin308.py guide <cfg.json> [face ...]   # Out/skin_guide_<face>.png (slope, guide, foot / rim lines)
  python Tools/Art/cliffskin308.py build <cfg.json> [face ...]   # Art/World/Compact/Rebuild/CliffSkin308/{<face>_<tag>_<chunk>_LOD{0,1}.bin, manifest.json}

Sheet = stations along a guide line (iso line of the steep band, foot side) x rows of the bed coordinate (height + tilt + fold).
A station's row sits where the outer envelope of its terrain cross-section reaches that height, so the rows ARE the beds.
Every grid cell belongs to one block (bed x joint interval | master crack | cap block); a node has one vertex per block around
it, all on the node's push line, so blocks step against each other with wall quads and nothing can open between them.
Bin: 'CSK1', int32 nv, int32 ni, float32 origin[3], float32 pos[nv*3] (minus origin), float32 normal[nv*3], float32 uv[nv*2],
uint8 flag[nv] (1 = meant to be seen, 0 = buried on purpose), int32 index[ni].
"""
import hashlib, json, math, struct, sys
from pathlib import Path
import numpy as np
from scipy import ndimage as ndi

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
from cliff308_base import CELL                                             # noqa: E402

OUT = B.RB / 'CliffSkin308'
STAGE_OUT = B.ROOT / 'Tools/Unity/Stage308_cliffs/Out'


def bil(a, x, z):
    u = np.clip(np.asarray(x, float) / CELL, 0, a.shape[1] - 1.001); w = np.clip(np.asarray(z, float) / CELL, 0, a.shape[0] - 1.001); j = np.floor(u).astype(int); i = np.floor(w).astype(int); fu = u - j; fw = w - i
    return a[i, j] * (1 - fu) * (1 - fw) + a[i, j + 1] * fu * (1 - fw) + a[i + 1, j] * (1 - fu) * fw + a[i + 1, j + 1] * fu * fw


def smooth(a, n):
    if n <= 1: return a
    k = np.ones(n) / n; p = n // 2; return np.convolve(np.pad(a, (p, n - 1 - p), mode='edge'), k, mode='valid')


def hsh(a, salt):
    """integer hash -> [0, 1): the same on every machine."""
    x = (np.asarray(a).astype(np.uint64) * np.uint64(2654435761) + np.uint64(salt) * np.uint64(40503) + np.uint64(0x9E3779B9)) & np.uint64(0xFFFFFFFF)
    x ^= x >> np.uint64(15); x = (x * np.uint64(2246822519)) & np.uint64(0xFFFFFFFF); x ^= x >> np.uint64(13); x = (x * np.uint64(3266489917)) & np.uint64(0xFFFFFFFF); x ^= x >> np.uint64(16)
    return x.astype(np.float64) / 4294967296.0


def trace(sd, gx, gz, start, direction, length, iso, step=1.0):
    p = np.array(start, float)

    def grad(q):
        g = np.array([float(bil(gx, q[0], q[1])), float(bil(gz, q[0], q[1]))]); n = np.linalg.norm(g); return g / n if n > 1e-9 else np.array([1.0, 0.0])

    def snap(q):
        for _ in range(12): q = q + grad(q) * (iso - float(bil(sd, q[0], q[1])))
        return q
    p = snap(p); pts = [p.copy()]
    for _ in range(int(length / step)):
        g = grad(p); t = np.array([g[1], -g[0]]) * direction; p = snap(p + t * step); pts.append(p.copy())
    return np.asarray(pts)


class Field:
    def __init__(self, cfg):
        p = B.ROOT / cfg['height']; sha = B.sha256(p)
        if sha != cfg['height_sha256']: raise SystemExit(f'REFUSED: {B.rel(p)} sha {sha} != cfg.height_sha256')
        self.h = B.load_height(p); self.slope = B.slope_deg(self.h); g = cfg['guide']
        S = self.slope >= g['steep_deg']; S = ndi.binary_closing(S, iterations=1); lab, n = ndi.label(S); size = ndi.sum(S, lab, np.arange(1, n + 1)); S = np.isin(lab, 1 + np.flatnonzero(size >= g['min_cells']))
        self.S = S.astype(float); di = ndi.distance_transform_edt(S, sampling=CELL); do = ndi.distance_transform_edt(~S, sampling=CELL)
        self.sd = ndi.gaussian_filter(np.where(S, di - 2.0, -(do - 2.0)), g['sd_sigma_cells']); self.gz, self.gx = np.gradient(self.sd, CELL)
        self.cos = np.cos(np.radians(ndi.gaussian_filter(self.slope, 0.6)))

    def tri(self, x, z): return B.sample_tri(self.h, x, z)


def stations(F, cfg, fc):
    """guide line (foot side of the steep band), outward normal, per station foot / rim along its cross-section."""
    g = cfg['guide']; st = cfg['sheet']
    a = trace(F.sd, F.gx, F.gz, fc['start'], -fc['dir'], fc['back_m'], -g['out_m'])[::-1]; b = trace(F.sd, F.gx, F.gz, fc['start'], fc['dir'], fc['fwd_m'], -g['out_m'])
    pts = np.concatenate([a[:-1], b]); n_s = int(g['smooth_m']) | 1; pts = np.stack([smooth(pts[:, 0], n_s), smooth(pts[:, 1], n_s)], 1)
    s = np.concatenate([[0], np.cumsum(np.hypot(np.diff(pts[:, 0]), np.diff(pts[:, 1])))]); ss = np.arange(0.0, s[-1], st['col_m']); G = np.stack([np.interp(ss, s, pts[:, 0]), np.interp(ss, s, pts[:, 1])], 1)
    T = np.gradient(G, axis=0); T /= np.linalg.norm(T, axis=1, keepdims=True)
    up = np.stack([bil(F.gx, G[:, 0], G[:, 1]), bil(F.gz, G[:, 0], G[:, 1])], 1)                     # into the band = uphill
    N = np.stack([-T[:, 1], T[:, 0]], 1); sign = np.sign(np.sum(N * up, axis=1)); N *= np.where(np.median(sign) >= 0, 1.0, -1.0)   # N = inward (towards the rock)
    nw = int(g['normal_smooth_m'] / st['col_m']) | 1; N0 = N; depth = fc.get('depth_m', g['depth_m']); us = np.arange(-g['out_extra_m'], depth, 0.25); nJ = len(G); gap = int(g['bridge_gap_m'] / 0.25); mw = int(g['extent_smooth_m'] / st['col_m']) | 1
    for attempt in range(6):                                              # sections must not cross between the skirt and the cap
        N = np.stack([smooth(N0[:, 0], nw), smooth(N0[:, 1], nw)], 1); N /= np.linalg.norm(N, axis=1, keepdims=True)
        X = G[:, 0:1] + N[:, 0:1] * us; Z = G[:, 1:2] + N[:, 1:2] * us; Hs = F.tri(X, Z); Sm = bil(F.S, X, Z) > 0.5; uf = np.zeros(nJ); ur = np.zeros(nJ); crest = np.zeros(nJ)
        for j in range(nJ):
            idx = np.flatnonzero(Sm[j])
            if not len(idx): uf[j] = ur[j] = np.nan; continue
            k0 = idx[0]; k1 = k0
            for k in idx[1:]:
                if k - k1 > gap: break
                k1 = k
            # the rim: on past the steep band while the ground still climbs, up to rim_search_m
            e = min(len(us) - 1, k1 + int(g['rim_search_m'] / 0.25)); seg = Hs[j, k1:e + 1]; am = int(np.argmax(seg >= seg.max() - 0.05)); run = Hs[j, k0:e + 1]; km = k0 + int(np.argmax(run >= run.max() - 0.05))
            if km < k1 - 8: k1 = km; crest[j] = 1.0                                                      # a thin ridge: steep on both sides, the rim is its crest
            elif seg.max() > Hs[j, k1] + 0.3 and am < len(seg) - 3: k1 = k1 + am; crest[j] = 1.0          # a crest: the ground stops climbing
            else: k1 = k1 + 2                                                                            # the slope goes on above the band: no crest here
            uf[j] = us[k0] - g['foot_pad_m']; ur[j] = us[min(k1, len(us) - 1)]
        ok = ~np.isnan(uf)
        if ok.sum() < 0.8 * nJ: raise SystemExit(f"REFUSED: face {fc['id']}: only {int(ok.sum())} of {nJ} stations cross a steep band")
        jj = np.arange(nJ); uf = np.interp(jj, jj[ok], uf[ok]); ur = np.interp(jj, jj[ok], ur[ok])
        uf = smooth(ndi.median_filter(uf, mw, mode='nearest'), mw); ur = ndi.median_filter(ur, mw, mode='nearest'); ur0 = ur.copy(); lip = g['rim_step_per_col_m']
        for j in range(1, nJ): ur[j] = min(ur[j], ur[j - 1] + lip)
        for j in range(nJ - 2, -1, -1): ur[j] = min(ur[j], ur[j + 1] + lip)
        crest = np.where(ur < ur0 - 1.0, 0.0, crest); ur = smooth(ur, mw)
        lo = G + N * (uf - st['skirt_m'] * st['skirt_out'] - 3.0)[:, None]; hi = G + N * (ur + st['cap_back_m'] + 0.5)[:, None]
        folds = int((np.sum(np.diff(lo, axis=0) * T[:-1], axis=1) < 0.12 * st['col_m']).sum() + (np.sum(np.diff(hi, axis=0) * T[:-1], axis=1) < 0.12 * st['col_m']).sum())
        if folds == 0: break
        nw = int(nw * 1.6) | 1
    return dict(G=G, N=N, T=T, s=ss, us=us, Hs=Hs, uf=uf, ur=ur, nw=nw, crest=smooth(crest, mw), folds=folds)


def guide_png(F, cfg, fc, S_):
    from PIL import Image, ImageDraw
    G = S_['G']; N = S_['N']; x0 = int(G[:, 0].min() // 4) - 28; x1 = int(G[:, 0].max() // 4) + 28; z0 = int(G[:, 1].min() // 4) - 28; z1 = int(G[:, 1].max() // 4) + 28; k = 8
    sl = F.slope[z0:z1, x0:x1]; hh = F.h[z0:z1, x0:x1]; v = np.clip(sl / 75.0, 0, 1); rgb = np.stack([v, v, v], 2); rgb[sl < 40] = rgb[sl < 40] * [0.45, 0.6, 1.0]; rgb[(np.floor(hh / 10) % 2 == 0)] *= 0.8
    img = Image.fromarray((rgb * 255).astype(np.uint8)).resize(((x1 - x0) * k, (z1 - z0) * k), Image.NEAREST); d = ImageDraw.Draw(img)
    P = lambda p: ((p[0] / 4 - x0) * k, (p[1] / 4 - z0) * k)
    d.line([P(p) for p in G], fill=(255, 220, 0), width=2); d.line([P(G[j] + N[j] * S_['uf'][j]) for j in range(len(G))], fill=(0, 220, 255), width=2); d.line([P(G[j] + N[j] * S_['ur'][j]) for j in range(len(G))], fill=(255, 60, 60), width=2)
    for j in range(0, len(G), 20): d.line([P(G[j]), P(G[j] + N[j] * S_['ur'][j])], fill=(90, 255, 90), width=1)
    d.ellipse([P(fc['start'])[0] - 5, P(fc['start'])[1] - 5, P(fc['start'])[0] + 5, P(fc['start'])[1] + 5], outline=(255, 0, 255), width=2)
    for gx_ in range(x0 + 5, x1, 25): d.text(((gx_ - x0) * k, 2), str(gx_ * 4), fill=(255, 255, 0))
    for gz_ in range(z0 + 5, z1, 25): d.text((2, (gz_ - z0) * k), str(gz_ * 4), fill=(255, 255, 0))
    STAGE_OUT.mkdir(parents=True, exist_ok=True); p = STAGE_OUT / f"skin_guide_{fc['id']}.png"; img.save(p); return p


def build_face(F, cfg, fc, walks, keep):
    st = cfg['sheet']; rk = cfg['rock']; S_ = stations(F, cfg, fc); G = S_['G']; N = S_['N']; us = S_['us']; Hs = S_['Hs']; uf = S_['uf']; ur = S_['ur']; nJ = len(G); dy = st['row_m']; col = st['col_m']
    seed = int.from_bytes(hashlib.sha256(f"{cfg['seed']}:{fc['id']}".encode()).digest()[:4], 'little'); rng = np.random.default_rng(seed)
    hat = lambda u: np.array([np.interp(u[j], us, Hs[j]) for j in range(nJ)])
    yf = hat(uf + cfg['guide']['foot_pad_m']); yr = hat(ur); sarr = S_['s']
    # the bed coordinate: rows are tilted and gently folded along the face
    fold = ndi.gaussian_filter1d(rng.normal(size=nJ + 400), rk['fold_wave_m'] / col)[200:200 + nJ]; fold = fold / max(1e-6, np.abs(fold).max()) * rk['fold_m']
    D = sarr * math.tan(math.radians(rng.uniform(-rk['tilt_deg'], rk['tilt_deg']))) + fold; D -= D.mean()
    y0 = float((yf - D).min() - st['skirt_m'] - dy); y1 = float((yr - D).max() + rk['cap_h'][1] + 2 * dy); nI = int(math.ceil((y1 - y0) / dy)) + 1
    Yt = y0 + np.arange(nI)[:, None] * dy + D[None, :]                                                   # target height of node (row, station)
    # beds: irregular thickness in rows; major beds = groups
    bed_of = np.zeros(nI - 1, int); b = 0; r = 0; bed_start = []
    while r < nI - 1:
        t = int(rng.choice(rk['bed_rows'], p=np.asarray(rk['bed_rows_p']) / sum(rk['bed_rows_p']))); bed_of[r:r + t] = b; bed_start.append(r); b += 1; r += t
    nB = b; bed_start = np.asarray(bed_start); major_of = np.zeros(nB, int); m = 0; q = 0
    while q < nB:
        t = int(rng.integers(rk['major_beds'][0], rk['major_beds'][1] + 1)); major_of[q:q + t] = m; m += 1; q += t
    nM = m
    # cap blocks along the rim
    cap_of = np.zeros(nJ - 1, int); c = 0; q = 0
    while q < nJ - 1:
        t = max(2, int(rng.uniform(rk['cap_len_m'][0], rk['cap_len_m'][1]) / col)); cap_of[q:q + t] = c; c += 1; q += t
    nC = c; u_c = rng.random(nC); cap_h = np.where(u_c < 0.3, rng.uniform(rk['cap_h'][0], 0.9, nC), np.where(u_c < 0.78, rng.uniform(0.9, 1.9, nC), rng.uniform(1.9, rk['cap_h'][1], nC)))
    yr_loc = ndi.maximum_filter1d(yr, int(5.0 / col) | 1, mode='nearest') - D; rim_row = np.floor((yr - D - y0) / dy).astype(int)
    cap_top = np.zeros(nC, int)
    for k in range(nC):
        js = np.flatnonzero(cap_of == k); cap_top[k] = int(math.ceil((yr_loc[js[0]:js[-1] + 2].max() + cap_h[k] * (0.3 + 0.7 * float(S_['crest'][js].mean())) - y0) / dy))
    top_row = np.empty(nJ, int); top_row[:-1] = cap_top[cap_of]; top_row[-1] = top_row[-2]
    top_node = np.maximum(top_row, np.concatenate([[top_row[0]], top_row[:-1]]))                          # a node carries the taller of its two cap blocks
    we = np.clip(np.minimum(sarr, sarr[-1] - sarr) / st['taper_m'], 0, 1); we = we * we * (3 - 2 * we)   # ends: everything sinks into the terrain
    top_node = np.minimum(top_node, rim_row + np.ceil((np.concatenate([cap_h[cap_of], cap_h[cap_of[-1:]]]) + rk['cap_tower_m']) / dy).astype(int))
    top_node = np.where(we <= 0.02, rim_row - 1, np.round(rim_row + (top_node - rim_row) * we).astype(int))
    # node base points
    I = np.arange(nI)[:, None]; P = np.zeros((nI, nJ, 3)); Wt = np.ones((nI, nJ)); zone = np.zeros((nI, nJ), int)   # 0 face, 1 skirt, 2 cap, 3 clamped below, 4 clamped above
    # how far the cap reaches behind the rim: cap_back_m on a plateau, less on a thin ridge whose far side falls away (the cap must not roof the far face)
    back = np.full(nJ, st['cap_back_m']); hb = hat(ur + back)
    for b_ in (1.8, 1.2, 0.7):
        low = hb < yr - 0.6; back = np.where(low, b_, back); hb = np.where(low, hat(ur + back), hb)
    hb = np.minimum(hb, yr) - st['cap_sink_m']
    # a thin ridge is seen from both sides: a cap standing over its crest shows its back as a comb, so there the sheet only laps the crest (one row)
    ridge = (yr - hat(ur + st['cap_back_m'])) > fc.get('ridge_fall_m', st['ridge_fall_m']); top_node = np.where(ridge, np.minimum(top_node, rim_row + 1), top_node)
    for j in range(nJ):
        k0 = int(np.searchsorted(us, uf[j])); k1 = int(np.searchsorted(us, ur[j])); E = np.maximum.accumulate(Hs[j, k0:k1 + 1]); uu = us[k0:k1 + 1]; yt = Yt[:, j]
        kk = np.clip(np.searchsorted(E, yt), 1, len(E) - 1); e0 = E[kk - 1]; e1 = E[kk]; fr = np.clip((yt - e0) / np.maximum(e1 - e0, 1e-6), 0, 1); u = uu[kk - 1] + (uu[kk] - uu[kk - 1]) * fr; y = yt.copy()
        foot_y = E[0]; sk = yt < foot_y; u = np.where(sk, uf[j] - (foot_y - yt) * st['skirt_out'], u); zn = np.where(sk, 1, 0)
        lowc = yt < foot_y - st['skirt_m']; u = np.where(lowc, uf[j] - st['skirt_m'] * st['skirt_out'], u); y = np.where(lowc, foot_y - st['skirt_m'], y); zn = np.where(lowc, 3, zn)
        capz = yt > E[-1]; u = np.where(capz, ur[j], u); zn = np.where(capz, 2, zn)
        hi = np.arange(nI) > top_node[j]; u = np.where(hi, ur[j] + back[j], u); y = np.where(hi, hb[j], y); zn = np.where(hi, 4, zn)
        P[:, j, 0] = G[j, 0] + N[j, 0] * u; P[:, j, 2] = G[j, 1] + N[j, 1] * u; P[:, j, 1] = y; zone[:, j] = zn
    Wt = np.where(zone >= 3, 0.0, 1.0)
    # blocks: cell (row i, station j) -> id
    nci, ncj = nI - 1, nJ - 1; ci = np.arange(nci)[:, None]; cj = np.arange(ncj)[None, :]; bed = bed_of[:, None] + 0 * cj; blk = np.zeros((nci, ncj), np.int64); crack = np.zeros((nci, ncj), bool); bj0 = np.zeros((nci, ncj)); bi0 = np.broadcast_to(bed_start[bed_of][:, None], (nci, ncj)).astype(float)
    lean_b = (hsh(np.arange(nB), 11) - 0.5) * 2 * rk['joint_lean']; lean_m = (hsh(np.arange(nM), 12) - 0.5) * 2 * rk['master_lean']
    def cuts(n_, lo, hi, rg):
        out = []; p_ = rg.uniform(0, hi)
        while p_ < n_: out.append(p_); p_ += rg.uniform(lo, hi)
        return np.asarray(out)
    master = [cuts(ncj, rk['master_m'][0] / col, rk['master_m'][1] / col, rng) for _ in range(nM)]; mwide = [(rng.random(len(mm)) < rk['crack_wide_p']).astype(int) + 1 for mm in master]
    minor = [cuts(ncj, rk['joint_m'][0] / col, rk['joint_m'][1] / col, rng) for _ in range(nB)]
    mrow0 = np.zeros(nM, int)
    for mm in range(nM): mrow0[mm] = bed_start[np.flatnonzero(major_of == mm)[0]]
    for i in range(nci):
        bb = bed_of[i]; mm = major_of[bb]; jm = np.arange(ncj) - lean_m[mm] * (i - mrow0[mm]); jb = np.arange(ncj) - lean_b[bb] * (i - bed_start[bb])
        mi = np.searchsorted(master[mm], jm); bi = np.searchsorted(minor[bb], jb); blk[i] = bb * 1000000 + mi * 1000 + bi
        mb = np.concatenate([[0.0], minor[bb]]); bj0[i] = mb[bi] + lean_b[bb] * (i - bed_start[bb])
        if len(master[mm]):
            near = np.clip(mi - 1, 0, len(master[mm]) - 1); dist = jm - master[mm][near]; cr = (mi > 0) & (dist >= 0) & (dist < mwide[mm][near]); crack[i] = cr; blk[i] = np.where(cr, 500000000 + mm * 1000 + near, blk[i])
    capcell = ci >= (rim_row[None, :-1] - rk['cap_overlap_rows']); blk = np.where(capcell, 900000000 + cap_of[None, :], blk); crack &= ~capcell
    cap_j0 = np.zeros(nC); cap_j0[cap_of[::-1]] = np.arange(ncj)[::-1]; bj0 = np.where(capcell, cap_j0[cap_of][None, :], bj0); bi0 = np.where(capcell, (rim_row[None, :-1] - rk['cap_overlap_rows']) + 0.0 * ci, bi0)
    # displacement per (node, block)
    broad = ndi.gaussian_filter(rng.normal(size=(nI, nJ)), (rk['broad_m'] / dy * 0.5, rk['broad_m'] / col)); broad = (broad - broad.min()) / max(1e-9, broad.max() - broad.min())
    fine = ndi.gaussian_filter(rng.normal(size=(nI, nJ)), (1.2, 1.6)); fine /= max(1e-9, np.abs(fine).max())
    bed_off = hsh(np.arange(nB), 21) ** 1.3 * rk['bed_off_m'] + np.where(hsh(np.arange(nB), 22) < rk['protrude_p'], rk['protrude_m'], 0.0)
    def dcell(i_, j_, blk_, bed_, crack_, cap_, j0_, i0_):
        b_h = hsh(blk_, 31); tj = (hsh(blk_, 34) - 0.5) * 2 * rk['tilt_per_m'] * np.clip((j_ - j0_ - 4) * col, -6, 6); ti = (hsh(blk_, 35) - 0.5) * 2 * rk['tilt_per_m'] * np.clip((i_ - i0_ - 1) * dy, -3, 3)
        d = rk['d_min'] + bed_off[bed_] + rk['block_off_m'] * b_h + rk['major_off_m'] * hsh(blk_ // 1000, 32) + rk['broad_off_m'] * broad[i_, j_] + rk['fine_m'] * fine[i_, j_] + tj + ti
        d = np.where(crack_, rk['d_min'] - 0.08 + 0.1 * b_h, d)
        d = np.where(cap_, rk['d_min'] + 0.15 + rk['cap_out_m'] * hsh(blk_, 36) + rk['broad_off_m'] * 0.5 * broad[i_, j_] + tj, d)
        return np.clip(d, rk['d_min'] - 0.1, rk['d_max'])
    # foot rubble: the lowest rows step out
    foot_row = np.floor((yf - D - y0) / dy).astype(int)
    # ends, gallery envelope
    dcap = np.full((nI, nJ), 99.0); inside = np.zeros((nI, nJ), bool)
    for wl in walks:
        A = wl[:-1]; Bv = wl[1:]
        if A[:, 0].max() < P[..., 0].min() - 8 or A[:, 0].min() > P[..., 0].max() + 8 or A[:, 2].max() < P[..., 2].min() - 8 or A[:, 2].min() > P[..., 2].max() + 8: continue
        Q = P.reshape(-1, 3); best = np.full(len(Q), 99.0); bestp = np.full(len(Q), 99.0)
        for a_, b_ in zip(A, Bv):
            ab = b_ - a_; L2 = ab[0] ** 2 + ab[2] ** 2
            if L2 < 1e-6: continue
            t = np.clip(((Q[:, 0] - a_[0]) * ab[0] + (Q[:, 2] - a_[2]) * ab[2]) / L2, 0, 1); px = a_[0] + ab[0] * t; pz = a_[2] + ab[2] * t; py = a_[1] + ab[1] * t; dh = np.hypot(Q[:, 0] - px, Q[:, 2] - pz); dv = Q[:, 1] - py
            band = (dv > -st['walk_under_m']) & (dv < st['walk_head_m']); best = np.where(band, np.minimum(best, dh), best)
            bandp = (dv > -st['post_zone_under_m']) & (dv < st['walk_head_m'] + 1.5); bestp = np.where(bandp, np.minimum(bestp, dh), bestp)
        cap_ = (best - st['walk_half_m'] - 0.3).reshape(nI, nJ); dcap = np.minimum(dcap, cap_); inside |= cap_ < rk['d_min']
        dcap = np.minimum(dcap, (st['post_zone_d_m'] + np.maximum(0.0, bestp - st['post_zone_half_m']) * 0.5).reshape(nI, nJ))
    steepf = np.clip((bil(ndi.gaussian_filter(F.slope, 0.8), P[..., 0], P[..., 2]) - rk['thin_below_deg'][0]) / (rk['thin_below_deg'][1] - rk['thin_below_deg'][0]), 0.0, 1.0); steepf = np.where(zone == 2, 1.0, steepf)
    # a bench or a gentle apron inside the face is left to the terrain (soil on a ledge): the sheet dives under it instead of lying on it as a slab
    slp = bil(ndi.gaussian_filter(F.slope, 0.8), P[..., 0], P[..., 2]); hidef = np.clip((slp - rk['hide_below_deg'][0]) / (rk['hide_below_deg'][1] - rk['hide_below_deg'][0]), 0.0, 1.0); hidef = hidef * hidef * (3 - 2 * hidef); hidef = np.where(zone == 0, hidef, 1.0)
    dirv = np.zeros((nJ, 3)); dirv[:, 0] = -N[:, 0]; dirv[:, 2] = -N[:, 1]; dirv[:, 1] = st['push_up']; dirv /= np.linalg.norm(dirv, axis=1, keepdims=True)

    def mesh(k, tag):
        """k = 1: full grid; k > 1: every k-th node, a coarse cell takes the block of the fine cell at its middle."""
        ri = np.unique(np.concatenate([np.arange(0, nI, k), [nI - 1]])); rj = np.unique(np.concatenate([np.arange(0, nJ, k), [nJ - 1]])); mi_ = np.minimum(ri[:-1] + k // 2, nci - 1); mj_ = np.minimum(rj[:-1] + k // 2, ncj - 1)
        cb = blk[np.ix_(mi_, mj_)]; cj0 = bj0[np.ix_(mi_, mj_)]; ci0 = bi0[np.ix_(mi_, mj_)]; cbed = bed[np.ix_(mi_, mj_)]; ccr = crack[np.ix_(mi_, mj_)]; ccap = capcell[np.ix_(mi_, mj_)]; a, bq = len(ri) - 1, len(rj) - 1
        if k > 1: ccr = ccr & False
        corner = [(0, 0), (0, 1), (1, 1), (1, 0)]; keys = []; ds = []; nodes = []
        for (di_, dj_) in corner:
            ni_ = np.broadcast_to(ri[di_:di_ + a][:, None], (a, bq)); nj_ = np.broadcast_to(rj[dj_:dj_ + bq][None, :], (a, bq))
            d = dcell(ni_, nj_, cb, cbed, ccr, ccap, cj0, ci0)
            d = d + np.where((ni_ - foot_row[nj_] < rk['foot_rows']) & ~ccap & ~ccr, rk['foot_out_m'] * hsh(cb, 41) * (hsh(cb, 42) < rk['foot_p']), 0.0)
            d = rk['d_min'] + (d - rk['d_min']) * (0.12 + 0.88 * steepf[ni_, nj_]); d = np.minimum(d, dcap[ni_, nj_]); d = np.where(inside[ni_, nj_], -0.5, d); d = -0.5 + (d + 0.5) * we[nj_] * hidef[ni_, nj_]; d = d * Wt[ni_, nj_]
            node = ni_.astype(np.int64) * nJ + nj_; keys.append(node * 4000000000 + cb); ds.append(d); nodes.append(node)
        keys = np.stack(keys, -1); ds = np.stack(ds, -1); nodes = np.stack(nodes, -1); uq, first, inv = np.unique(keys.ravel(), return_index=True, return_inverse=True); inv = inv.reshape(keys.shape)
        vn = nodes.ravel()[first]; vd = ds.ravel()[first].copy(); vi = vn // nJ; vj = vn % nJ; base = P[vi, vj].copy(); base[:, 1] -= 0.9 * (1 - we[vj]) ** 2 + 0.6 * (1 - hidef[vi, vj]); dv = dirv[vj]; zn = zone[vi, vj]      # the ends sink as well as pull in
        seen = (zn <= 2) & (zn != 1) & (we[vj] > 0.75) & ~inside[vi, vj] & (Wt[vi, vj] > 0) & (hidef[vi, vj] > 0.9)
        # never closer than clear_m to the terrain surface where it is meant to be seen
        short = 0
        for it in range(10):
            q_ = base + dv * vd[:, None]; cl = (q_[:, 1] - F.tri(q_[:, 0], q_[:, 2])) * bil(F.cos, q_[:, 0], q_[:, 2]); need = seen & (cl < st['clear_m'])
            if it == 9: short = int(need.sum())
            if not need.any(): break
            vd = np.where(need, vd + np.clip((st['clear_m'] - cl) * 1.6, 0.05, 1.0), vd)
        pos = base + dv * vd[:, None]; cs_ = bil(F.cos, pos[:, 0], pos[:, 2]); gy = F.tri(pos[:, 0], pos[:, 2]); cl = (pos[:, 1] - gy) * cs_
        lift = seen & (cl < st['clear_m']); pos[lift, 1] = gy[lift] + st['clear_m'] / np.maximum(cs_[lift], 0.2); short = int(lift.sum())      # a push that runs into a neighbouring buttress: lifted clear instead
        cl = (pos[:, 1] - F.tri(pos[:, 0], pos[:, 2])) * cs_
        # the sheet's edge in node space (walls only join copies of one node, on one line): every edge node must be under the ground
        ar_q = np.linalg.norm(np.cross(pos[inv[..., 1]] - pos[inv[..., 0]], pos[inv[..., 2]] - pos[inv[..., 0]]), axis=-1) + np.linalg.norm(np.cross(pos[inv[..., 2]] - pos[inv[..., 0]], pos[inv[..., 3]] - pos[inv[..., 0]]), axis=-1); kept = ar_q > 1e-4
        kp = np.pad(kept, 1); eh = kp[1:, 1:-1] != kp[:-1, 1:-1]; ev = kp[1:-1, 1:] != kp[1:-1, :-1]            # eh[r, c]: edge under cell row r (node row r), ev[r, c]: edge left of cell col c
        above = np.full(len(ri) * len(rj), -99.0); li = np.searchsorted(ri, vi); lj = np.searchsorted(rj, vj); np.maximum.at(above, li * len(rj) + lj, pos[:, 1] - F.tri(pos[:, 0], pos[:, 2])); above = above.reshape(len(ri), len(rj))
        en = np.zeros((len(ri), len(rj)), bool); en[:, :-1] |= eh; en[:, 1:] |= eh; en[:-1, :] |= ev; en[1:, :] |= ev
        bnd_ = dict(edge_nodes=int(en.sum()), edge_nodes_above_ground=int((en & (above > 0.02)).sum()), worst_above_m=round(float(above[en].max()), 2) if en.any() else 0.0)
        quad = inv.reshape(-1, 4); cbf = cb.ravel(); qi = np.repeat(np.arange(a), bq); qj = np.tile(np.arange(bq), a)
        tris = np.concatenate([quad[:, [0, 1, 2]], quad[:, [0, 2, 3]]]); tq = np.concatenate([np.arange(len(quad))] * 2)
        # walls between cells of different blocks: own vertices (flat), facing the lower side
        wpos = []; wtri = []; wq = []; nw_ = 0
        dm = vd[quad].mean(axis=1); cen = pos[quad].mean(axis=1); wflag = []; wcap = []; capv = (zn == 2) | (zn == 4)
        def walls(c0, c1, e0, e1, q0):
            nonlocal nw_
            diff = cbf[c0] != cbf[c1]; c0 = c0[diff]; c1 = c1[diff]; r_ = np.arange(len(c0))
            a0 = quad[c0][r_, e0]; a1 = quad[c0][r_, e1]; b0 = quad[c1][r_, q0[0]]; b1 = quad[c1][r_, q0[1]]
            pa0, pa1, pb0, pb1 = pos[a0], pos[a1], pos[b0], pos[b1]; ar = np.linalg.norm(np.cross(pa1 - pa0, pb1 - pa0), axis=1) + np.linalg.norm(np.cross(pb1 - pa0, pb0 - pa0), axis=1); ok = ar > 1e-5
            if not ok.any(): return
            pa0, pa1, pb0, pb1 = pa0[ok], pa1[ok], pb0[ok], pb1[ok]; c0 = c0[ok]; c1 = c1[ok]; n_ = len(pa0)
            want = (cen[c1] - cen[c0]) * np.sign(dm[c0] - dm[c1] + 1e-9)[:, None]; fnq = np.cross(pa1 - pa0, pb1 - pa0) + np.cross(pb1 - pa0, pb0 - pa0); fl = np.sum(fnq * want, axis=1) < 0
            v1 = np.where(fl[:, None], pb0, pa1); v3 = np.where(fl[:, None], pa1, pb0)
            vv = np.stack([pa0, v1, pb1, v3], 1).reshape(-1, 3); idx = nw_ + np.arange(n_)[:, None] * 4; wpos.append(vv); wtri.append(np.concatenate([idx + [0, 1, 2], idx + [0, 2, 3]])); wq.append(np.concatenate([c0, c0])); nw_ += n_ * 4
            sa0, sa1, sb0, sb1 = seen[a0][ok], seen[a1][ok], seen[b0][ok], seen[b1][ok]; wflag.append(np.stack([sa0, np.where(fl, sb0, sa1), sb1, np.where(fl, sa1, sb0)], 1).ravel()); wcap.append(np.repeat((capv[a0] & capv[a1] & capv[b0] & capv[b1])[ok], 4))
        cid = np.arange(a * bq).reshape(a, bq)
        walls(cid[:, :-1].ravel(), cid[:, 1:].ravel(), 1, 2, (0, 3))      # between stations: edge (0,1)-(1,1) of the left cell = (0,0)-(1,0) of the right
        walls(cid[:-1, :].ravel(), cid[1:, :].ravel(), 3, 2, (0, 1))      # between rows: edge (1,0)-(1,1) of the lower cell = (0,0)-(0,1) of the upper
        nv0 = len(pos); allpos = np.concatenate([pos] + wpos) if wpos else pos; alltri = np.concatenate([tris] + [w + nv0 for w in wtri]) if wtri else tris
        flag = np.concatenate([seen] + wflag) if wflag else seen; cols_of_tri = np.concatenate([qj[tq]] + [qj[w] for w in wq]) if wq else qj[tq]
        p0, p1, p2 = allpos[alltri[:, 0]], allpos[alltri[:, 1]], allpos[alltri[:, 2]]; fn = np.cross(p1 - p0, p2 - p0); ar = np.linalg.norm(fn, axis=1); okt = ar > 2e-5; alltri = alltri[okt]; fn = fn[okt]; cols_of_tri = cols_of_tri[okt]
        # outward: grid triangles by the face direction; a wall already faces its lower block
        out_dir = dirv[np.minimum(rj[np.minimum(cols_of_tri, bq - 1)], nJ - 1)]; is_wall = alltri[:, 0] >= nv0
        flip = (np.sum(fn * out_dir, axis=1) < 0) & ~is_wall; alltri[flip] = alltri[flip][:, [0, 2, 1]]; fn[flip] *= -1
        # the cap stands over the crest and is seen from behind on a thin ridge: its triangles are written a second time, facing the other way
        capall = np.concatenate([capv] + wcap) if wcap else capv; ct = capall[alltri].all(axis=1); two = None
        if ct.any():
            src = alltri[ct]; cv = np.unique(src); base_n = len(allpos); remap = np.full(base_n, -1); remap[cv] = base_n + np.arange(len(cv)); two = (base_n, len(cv))
            allpos = np.concatenate([allpos, allpos[cv]]); flag = np.concatenate([flag, flag[cv]]); alltri = np.concatenate([alltri, remap[src][:, [0, 2, 1]]]); fn = np.concatenate([fn, -fn[ct]]); cols_of_tri = np.concatenate([cols_of_tri, cols_of_tri[ct]])
        nrm = np.zeros_like(allpos); np.add.at(nrm, alltri[:, 0], fn); np.add.at(nrm, alltri[:, 1], fn); np.add.at(nrm, alltri[:, 2], fn)
        ln = np.linalg.norm(nrm, axis=1); vcol = np.zeros(len(allpos), int); vcol[alltri[:, 0]] = cols_of_tri; vcol[alltri[:, 1]] = cols_of_tri; vcol[alltri[:, 2]] = cols_of_tri
        fb = dirv[np.minimum(rj[np.minimum(vcol, bq - 1)], nJ - 1)]; nrm = np.where(ln[:, None] > 1e-9, nrm / np.maximum(ln, 1e-9)[:, None], fb)
        # the ground shader calls a surface rock by 1 - normal.y: ledge tops keep a rock normal (no soil / grass on a bench of the skin)
        ny = np.minimum(nrm[:, 1], st['normal_y_max']); hz = np.hypot(nrm[:, 0], nrm[:, 2]); want = np.sqrt(1 - ny ** 2); sc = np.where(hz > 1e-4, want / np.maximum(hz, 1e-4), 0.0)
        nx = np.where(hz > 1e-4, nrm[:, 0] * sc, fb[:, 0] / np.maximum(np.hypot(fb[:, 0], fb[:, 2]), 1e-6) * want); nz = np.where(hz > 1e-4, nrm[:, 2] * sc, fb[:, 2] / np.maximum(np.hypot(fb[:, 0], fb[:, 2]), 1e-6) * want)
        nrm = np.stack([nx, ny, nz], 1); nrm /= np.linalg.norm(nrm, axis=1, keepdims=True)
        used = np.zeros(len(allpos), bool); used[alltri.ravel()] = True
        st_ = dict(boundary=bnd_, short=short, seen_clear_min=float(cl[seen & used[:nv0]].min()) if (seen & used[:nv0]).any() else None, seen=int((seen & used[:nv0]).sum()))
        return allpos, nrm, alltri, flag, cols_of_tri, rj, st_
    res = {}
    for lod, k in (('LOD0', 1), ('LOD1', st['lod1_every'])):
        res[lod] = mesh(k, lod)
    # rim / foot facts per station (for the report and skin:check)
    rim_cover_up = (y0 + top_node * dy + D) - yr; info = dict(stations=nJ, rows=nI, length_m=float(sarr[-1]), height_m=float(np.median(yr - yf)), height_max_m=float((yr - yf).max()), beds=nB, majors=nM, cap_blocks=nC,
                                                                rim_over_min_m=float(rim_cover_up[(we > 0.75) & ~ridge].min()) if ((we > 0.75) & ~ridge).any() else 0.0, rim_back_m=float(back[we > 0.75].min()), ridge_share=round(float(ridge.mean()), 3), skirt_m=st['skirt_m'], normal_window_cols=S_['nw'],
                                                                foot=[round(float(v), 1) for v in (G[nJ // 2] + N[nJ // 2] * uf[nJ // 2])], rim_y=[round(float(yr.min()), 1), round(float(yr.max()), 1)], foot_y=[round(float(yf.min()), 1), round(float(yf.max()), 1)])
    # keep-out polygons (+ margin): no vertex inside
    if keep:
        ap = res['LOD0'][0]; worst = 1e9
        for poly, margin in keep:
            inside_ = B.poly_mask(np.asarray(poly, float).reshape(-1, 2), ap[:, 0:1], ap[:, 2:3])[:, 0]; pp = np.asarray(poly, float).reshape(-1, 2); dmin = np.full(len(ap), 1e9)
            for q0, q1 in zip(pp, np.roll(pp, -1, axis=0)):
                ab = q1 - q0; t = np.clip(((ap[:, 0] - q0[0]) * ab[0] + (ap[:, 2] - q0[1]) * ab[1]) / max(1e-9, ab @ ab), 0, 1); dmin = np.minimum(dmin, np.hypot(ap[:, 0] - q0[0] - ab[0] * t, ap[:, 2] - q0[1] - ab[1] * t))
            worst = min(worst, float(np.where(inside_, -dmin, dmin).min() - margin))
        info['keepout_clear_m'] = round(worst, 1)
        if worst < 0: raise SystemExit(f"REFUSED: face {fc['id']} is {-worst:.1f} m inside a keep-out polygon + margin")
    return res, info


def boundary_report(F, pos, tri, tol=0.002):
    """open edges after welding by position: each must be under the terrain surface (nobody can look behind the sheet)."""
    key = np.round(pos / tol).astype(np.int64); _, wid = np.unique(key, axis=0, return_inverse=True); wid = wid.ravel(); t = wid[tri]; e = np.concatenate([t[:, [0, 1]], t[:, [1, 2]], t[:, [2, 0]]]); e = e[e[:, 0] != e[:, 1]]
    es = np.sort(e, axis=1); uq, cnt = np.unique(es, axis=0, return_counts=True); op = uq[cnt == 1]
    if not len(op): return dict(open_edges=0, open_above_ground=0, worst_above_m=0.0)
    rep = np.zeros(wid.max() + 1, int); rep[wid] = np.arange(len(pos)); pp = pos[rep[op.ravel()]]; above = pp[:, 1] - F.tri(pp[:, 0], pp[:, 2])
    return dict(open_edges=int(len(op)), open_above_ground=int((above.reshape(-1, 2).max(axis=1) > 0.05).sum()), worst_above_m=round(float(above.max()), 2))


def write_bin(path, pos, nrm, uv, flag, tri):
    o = pos.mean(axis=0).astype(np.float32); path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, 'wb') as f:
        f.write(b'CSK1'); f.write(struct.pack('<ii', len(pos), tri.size)); f.write(o.tobytes()); f.write((pos - o).astype('<f4').tobytes()); f.write(nrm.astype('<f4').tobytes()); f.write(uv.astype('<f4').tobytes()); f.write(flag.astype(np.uint8).tobytes()); f.write(tri.astype('<i4').tobytes())
    return o


def main():
    B.utf8(); cmd = sys.argv[1]; cfg_p = Path(sys.argv[2]); cfg = json.loads(cfg_p.read_text(encoding='utf-8')); only = set(sys.argv[3:]); F = Field(cfg)
    faces = [f for f in cfg['faces'] if f.get('on', True) and (not only or f['id'] in only)]
    if cmd == 'guide':
        for fc in faces:
            S_ = stations(F, cfg, fc); hat = lambda u, j: float(np.interp(u, S_['us'], S_['Hs'][j])); n = len(S_['G']); hh = [hat(S_['ur'][j], j) - hat(S_['uf'][j], j) for j in range(n)]
            print(f"{fc['id']}: stations {n}, length {S_['s'][-1]:.0f} m, height median {np.median(hh):.1f} max {max(hh):.1f} m, depth ur median {np.median(S_['ur']):.1f} max {S_['ur'].max():.1f}, normal window {S_['nw']} cols, folds {S_['folds']}, png {B.rel(guide_png(F, cfg, fc, S_))}")
        return
    if cmd != 'build': raise SystemExit(__doc__)
    walks = []
    lay = B.ROOT / cfg['walks']
    if lay.exists():
        for w in json.loads(lay.read_text(encoding='utf-8'))['ways']:
            wl = np.stack([w['walkX'], w['walkY'], w['walkZ']], 1); seg = np.linalg.norm(np.diff(wl, axis=0), axis=1); s = np.concatenate([[0], np.cumsum(seg)]); ss = np.arange(0, s[-1], 0.75); walks.append(np.stack([np.interp(ss, s, wl[:, c]) for c in range(3)], 1))
    keep = []
    for kp in cfg.get('keepout', []):
        src = json.loads((B.ROOT / kp['file']).read_text(encoding='utf-8')); poly = [c for v in src['wall']['vertices'] for c in (v['x'], v['z'])]; keep.append((poly, kp['margin_m']))
    man_p = OUT / 'manifest.json'; man = json.loads(man_p.read_text(encoding='utf-8')) if man_p.exists() else dict(version='cliffskin308.1', faces=[])
    man['height_sha256'] = cfg['height_sha256']; man['tag'] = cfg['tag']; tot0 = 0
    for fc in faces:
        res, info = build_face(F, cfg, fc, walks, keep); chunks = []; col_per = int(cfg['sheet']['chunk_m'] / cfg['sheet']['col_m']); nJ = info['stations']; n_ch = max(1, int(round(nJ / col_per))); edges = np.linspace(0, nJ, n_ch + 1).astype(int)
        for old in OUT.glob(f"{fc['id']}_*.bin"): old.unlink()
        tri_tot = {'LOD0': 0, 'LOD1': 0}; mem = 0; bnd = None; short = 0; cmin = 99.0
        for lod in ('LOD0', 'LOD1'):
            pos, nrm, tri, flag, tcol, rj, st_ = res[lod]; short += st_['short']; cmin = min(cmin, st_['seen_clear_min'] or 99.0)
            if lod == 'LOD0': bnd = st_['boundary']
            bad = int((~np.isfinite(nrm).all(axis=1) | (np.abs(np.linalg.norm(nrm, axis=1) - 1) > 1e-3)).sum())
            if bad: raise SystemExit(f"REFUSED: {fc['id']} {lod}: {bad} normals are not unit length")
            fine_col = rj[np.minimum(tcol, len(rj) - 2)]
            for c in range(n_ch):
                sel = (fine_col >= edges[c]) & (fine_col < edges[c + 1]) if c < n_ch - 1 else (fine_col >= edges[c]); t = tri[sel]
                if not len(t): continue
                uqv, inv = np.unique(t.ravel(), return_inverse=True); p = pos[uqv]; uv = np.stack([p[:, 0] * 0.25 + p[:, 2] * 0.25, p[:, 1] * 0.25], 1); name = f"{fc['id']}_{cfg['tag']}_{c}_{lod}"
                o = write_bin(OUT / (name + '.bin'), p, nrm[uqv], uv, flag[uqv], inv.reshape(-1, 3)); tri_tot[lod] += len(t); mem += len(uqv) * 32 + t.size * (4 if len(uqv) > 65000 else 2)
                ch = next((x for x in chunks if x['index'] == c), None)
                if ch is None: ch = dict(index=c, name=f"{fc['id']}_{c}", min=[round(float(v), 2) for v in p.min(axis=0)], max=[round(float(v), 2) for v in p.max(axis=0)]); chunks.append(ch)
                ch[lod] = dict(file=name + '.bin', vertices=int(len(uqv)), triangles=int(len(t)))
        per100 = tri_tot['LOD0'] / max(1.0, info['length_m']) * 100
        entry = dict(id=fc['id'], realm=fc['realm'], tag=cfg['tag'], **info, triangles=tri_tot, lod0_tris_per_100m=round(per100), mesh_bytes=mem, boundary=bnd, clearance_short=short, seen_clear_min_m=round(cmin, 3), chunks=chunks)
        man['faces'] = [f for f in man['faces'] if f['id'] != fc['id']] + [entry]; tot0 += tri_tot['LOD0']
        print(f"{fc['id']} ({fc['realm']}): {info['length_m']:.0f} m x {info['height_m']:.0f} m (max {info['height_max_m']:.0f}), LOD0 {tri_tot['LOD0']} tris ({per100:.0f} / 100 m), LOD1 {tri_tot['LOD1']}, {mem / 1e6:.1f} MB, chunks {len(chunks)}, beds {info['beds']}, cap blocks {info['cap_blocks']}, rim over min {info['rim_over_min_m']:.2f} m, edge nodes {bnd['edge_nodes']} above ground {bnd['edge_nodes_above_ground']} (worst {bnd['worst_above_m']}), lifted clear {short}, min clearance {cmin:.2f}" + (f", keep-out clear {info['keepout_clear_m']} m" if 'keepout_clear_m' in info else ''))
        if per100 > cfg['budget']['lod0_per_100m']: raise SystemExit(f"REFUSED: {fc['id']} LOD0 {per100:.0f} tris / 100 m > budget {cfg['budget']['lod0_per_100m']}")
    man['faces'].sort(key=lambda f: f['id']); man['lod0_total'] = sum(f['triangles']['LOD0'] for f in man['faces']); man['mesh_bytes_total'] = sum(f['mesh_bytes'] for f in man['faces'])
    if man['lod0_total'] > cfg['budget']['lod0_total']: raise SystemExit(f"REFUSED: LOD0 total {man['lod0_total']} > budget {cfg['budget']['lod0_total']}")
    OUT.mkdir(parents=True, exist_ok=True); man_p.write_text(json.dumps(man, ensure_ascii=False, indent=1), encoding='utf-8'); print(f"manifest {B.rel(man_p)}: faces {len(man['faces'])}, LOD0 total {man['lod0_total']}, mesh memory {man['mesh_bytes_total'] / 1e6:.1f} MB")


if __name__ == '__main__':
    main()
