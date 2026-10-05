"""#308 cliff boundary stage 1a -> Enclosure305 v305.3 (SPEC-WORLD-CLIFF-BOUNDARY-308 재생성 체인 1: "숲 v305.3 + 절벽 마스크").

  python Tools/Art/enclosure305_cliff308.py status
  python Tools/Art/enclosure305_cliff308.py build   [--dry]    305.2 + cliff plan -> segments / candidates / cliff mask / report
  python Tools/Art/enclosure305_cliff308.py closure [--dry]    as-built closure (4 m lattice) + fine joint tests + joint hole tests
  python Tools/Art/enclosure305_cliff308.py all     [--dry]    build + closure
  python Tools/Art/enclosure305_cliff308.py revert  [--to 305.2|305.1] [--dry]   put the editor inputs back (bytes of the frozen copies)

v305.4 (cliff stage 1b, config Enclosure305/cliff305_4.cfg.json, STAGED: build / closure write under Enclosure305/Stage305_4/<variant>/ only):
  python Tools/Art/enclosure305_cliff308.py --cfg cliff305_4.cfg.json --variant a|b all [--dry]
  python Tools/Art/enclosure305_cliff308.py --cfg cliff305_4.cfg.json --variant a|b install [--dry]   staged files -> the live editor inputs (sha preconditions, backups)
  python Tools/Art/enclosure305_cliff308.py --cfg cliff305_4.cfg.json revert --to 305.3 [--dry]       live inputs back to the 305.3 files
  variant a = the seam runs the long wall replaces keep their shells; b = they are removed. Both: no shell on the 금표 단애산 top (E305_005 ends
  inside the north face rock, trees stay as a rim band), the E305_005 kink re-routed, logs tilted to the ground or dropped on slopes, no trunk in a rock.

What it does (every number TEST, all of them in Enclosure305/cliff305_3.cfg.json):
  * freezes the 305.2 inputs once (segments305_v305_2.json, Out/forest305_candidates_v305_2.json = byte copies) and always
    derives 305.3 from those copies, so a second run gives the same bytes (no clock, no random numbers in the data files);
  * removes the forest runs the stage's cliff segments replace (ops `replaces`; the partial ones are cut at the stated x);
  * builds the cliff mask on the stage height (quad = 4 m cell): every quad a non-top-forest cliff segment changed, and the
    steep quads of the top-forest segments (금표 단애산). The editor's Ground305 answers "no ground" there (stage copy of
    CompactEnclosure305.cs), so no shell column, tree or cover shrub is ever seated on a face or on a foreign cliff top;
  * cuts every kept run where its shell centre line enters the mask and carries it `stub_m` further: the shell end lies
    inside rock (verified on the tile triangles: rock above both end-cap corners >= the shell height);
  * drops the candidates of removed runs / removed arcs / masked quads and re-seats the ones on lifted unmasked ground;
  * closure: forest 305.3 + cliffs (stage height) + built walls and closed capital doors, with and without the planned
    stage-1b gate (the scenes hold NO gate: Seal308 was never applied), sensitivity lattices, a hole test per joint
    (does the closure depend on it?) and a 0.25 m flood around each joint on the tile triangles.
Writes only under Art/World/Compact/Rebuild/Enclosure305. Reads the cliff plan (ops, stage height, owner map) read-only.
The live inputs of the editor command (segments305.json, Out/forest305_candidates.json) are replaced by `build` (not --dry);
`revert` restores them. Nothing here touches a scene, an asset or the Seal308 wall data.
"""
import argparse, hashlib, json, math, re, sys
from collections import deque
from pathlib import Path
import numpy as np

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
import cliff308_base as B                                                # noqa: E402

ROOT = B.ROOT; ENC = B.ENC; A = B.A
CFG = ENC / 'cliff305_3.cfg.json'      # main() points it at --cfg (v305.4: cliff305_4.cfg.json)
HH, W, CELL = B.HH, B.W, B.CELL


# ------------------------------------------------------------------ small io
def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def sha_bytes(b):
    return hashlib.sha256(b).hexdigest()


def jbytes(obj, indent=1):
    """LF json bytes (never CRLF: the 305.2 file was written in text mode on Windows)."""
    return (json.dumps(obj, ensure_ascii=False, indent=indent, default=B._js) + '\n').encode('utf-8')


class Writer:
    """collects the files of one run; --dry lists them and writes nothing."""
    def __init__(self, dry):
        self.dry = dry; self.log = []

    def put(self, path, data):
        path = Path(path); old = path.read_bytes() if path.exists() else None
        state = 'same' if old == data else ('new' if old is None else 'changed')
        self.log.append((B.rel(path), len(data), sha_bytes(data)[:12], state))
        if not self.dry and state != 'same':
            path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)

    def show(self):
        for p, n, h, st in self.log:
            print(f"  {'(dry) ' if self.dry else ''}{st:7s} {p} ({n} bytes, sha {h})")


def head_version(path):
    """'version' of a json file read from its first bytes (the candidate files are several MB)."""
    m = re.search(rb'"version"\s*:\s*"([^"]+)"', Path(path).read_bytes()[:4096])
    return m.group(1).decode() if m else None


def load_cfg(variant=None):
    cfg = json.loads(CFG.read_text(encoding='utf-8'))
    if cfg.get('variants'):      # v305.4: one config, two staged variants; the variant block overrides the top-level keys
        if variant not in cfg['variants']: raise SystemExit(f"REFUSED: {CFG.name} needs --variant {' | '.join(cfg['variants'])}")
        cfg.update(cfg['variants'][variant]); cfg['variant'] = variant
    cfg['_o'] = {k: ENC / v.replace('{variant}', variant or '') for k, v in cfg['outputs'].items()}
    return cfg


# ------------------------------------------------------------------ geometry
class Line:
    def __init__(self, pts):
        self.P = np.asarray(pts, float); d = np.hypot(*np.diff(self.P, axis=0).T) if len(self.P) > 1 else np.zeros(0)
        self.cum = np.concatenate([[0.0], np.cumsum(d)]); self.L = float(self.cum[-1])

    def at(self, s):
        s = np.clip(np.asarray(s, float), 0, self.L)
        return np.stack([np.interp(s, self.cum, self.P[:, 0]), np.interp(s, self.cum, self.P[:, 1])], -1)

    def sub(self, s0, s1):
        """points of the arc [s0, s1]: interpolated ends + the original vertices strictly between (2 decimals, as lines305)."""
        mid = [self.P[k] for k in range(len(self.P)) if s0 + 1e-6 < self.cum[k] < s1 - 1e-6]
        a = self.P[0] if s0 <= 1e-9 else self.at(s0); b = self.P[-1] if s1 >= self.L - 1e-9 else self.at(s1)
        return [[round(float(x), 2), round(float(z), 2)] for x, z in [a] + mid + [b]]

    def project(self, xz):
        """arc of the nearest point of the polyline for each row of xz."""
        xz = np.asarray(xz, float); best = np.full(len(xz), np.inf); arc = np.zeros(len(xz))
        for k in range(len(self.P) - 1):
            a = self.P[k]; d = self.P[k + 1] - a; L2 = float(d @ d)
            if L2 < 1e-12: continue
            t = np.clip(((xz - a) @ d) / L2, 0, 1); q = a + d * t[:, None]; dd = np.hypot(xz[:, 0] - q[:, 0], xz[:, 1] - q[:, 1])
            m = dd < best; best[m] = dd[m]; arc[m] = self.cum[k] + t[m] * math.sqrt(L2)
        return arc, best


def stations(pts, block, closed, K, step=None):
    """port of CompactEnclosure305.Stations305 (float64): arrays s, p, t, nb, q."""
    P = np.asarray(pts, float)
    if closed and len(P) > 2 and tuple(P[0]) != tuple(P[-1]): P = np.vstack([P, P[:1]])
    ln = Line(P); L = ln.L; step = step or K['step_m']; n = max(1, int(math.ceil(L / step - 1e-12)))
    s = L * np.arange(n + 1) / n; p = ln.at(s); h = K['tangent_half_m']
    t = ln.at(np.minimum(L, s + h)) - ln.at(np.maximum(0, s - h)); nrm = np.hypot(t[:, 0], t[:, 1])
    for k in range(len(t)):                                              # zero tangent -> the previous one (Vector2.up first)
        if nrm[k] ** 2 > 1e-8: t[k] /= nrm[k]
        else: t[k] = t[k - 1] if k else (0.0, 1.0)
    nb = np.stack([t[:, 1], -t[:, 0]], 1) * block; q = p + nb * K['offset_m']
    return dict(s=s, p=p, t=t, nb=nb, q=q, L=L)


def _signed_angle(a, b):
    na, nbn = math.hypot(*a), math.hypot(*b)
    if na < 1e-15 or nbn < 1e-15: return 0.0
    ang = math.degrees(math.acos(max(-1.0, min(1.0, (a[0] * b[0] + a[1] * b[1]) / (na * nbn)))))
    return ang if (a[0] * b[1] - a[1] * b[0]) >= 0 else -ang


def _delta_angle(a, b):
    d = (b - a) % 360.0
    return d - 360.0 if d > 180.0 else d


def _rot(v, deg):
    r = math.radians(deg); c, s = math.cos(r), math.sin(r); return np.array([v[0] * c - v[1] * s, v[0] * s + v[1] * c])


def _cross(a, b, c, d):
    r = b - a; s = d - c; den = r[0] * s[1] - r[1] * s[0]
    if abs(den) < 1e-8: return False
    w = c - a; t = (w[0] * s[1] - w[1] * s[0]) / den; u = (w[0] * r[1] - w[1] * r[0]) / den
    return 0 <= t <= 1 and 0 <= u <= 1


def joints(segs, st, K):
    """port of CompactEnclosure305.Joints305: [dict(a, b, p, kind, angle, gap, flip)] for ends on one line point."""
    ends = [(g, e) for g in segs if not g.get('closed') for e in (False, True)]; out = []; n12 = K['chain_stations']
    endpt = lambda g, e: np.asarray(g['points'][-1 if e else 0], float)

    def toward(S, at_end):
        idx = list(range(max(0, len(S['s']) - n12), len(S['s']))) if at_end else list(range(min(n12, len(S['s']))))[::-1]
        return idx
    for i in range(len(ends)):
        for j in range(i + 1, len(ends)):
            (ga, ea), (gb, eb) = ends[i], ends[j]; pa, pb = endpt(ga, ea), endpt(gb, eb)
            if math.hypot(*(pa - pb)) > K['joint_eps_m']: continue
            SA, SB = st[ga['id']], st[gb['id']]; ia = toward(SA, ea); ib = toward(SB, eb)[::-1]
            Aq = SA['q'][ia]; Bq = SB['q'][ib]; nA = SA['nb'][ia[-1]]; nB = SB['nb'][ib[0]]
            gap = float(math.hypot(*(Aq[-1] - Bq[0]))); angle = _signed_angle(nA, nB)
            oa = Aq[-1] - Aq[max(0, len(Aq) - 2)]; oa = oa / math.hypot(*oa) if oa @ oa > 1e-8 else SA['t'][ia[-1]]
            db = (Bq[1] - Bq[0]) if len(Bq) > 1 else SB['t'][ib[0]]; db = db / math.hypot(*db) if db @ db > 1e-8 else oa
            flip = abs(_delta_angle(angle, _signed_angle(oa, db))) > 90.0
            if abs(angle) > 175.0 and float(_rot(nA, angle * .5) @ oa) < 0: angle = -angle
            kind = 'arc'
            if gap < K['flush_gap_m']: kind = 'flush'
            else:
                hit = False
                for u in range(len(Aq) - 2, -1, -1):
                    for v in range(len(Bq) - 1):
                        if _cross(Aq[u], Aq[u + 1], Bq[v], Bq[v + 1]): hit = True; break
                    if hit: break
                if hit: kind = 'cross'
            out.append(dict(a=ga['id'], b=gb['id'], p=((pa + pb) * .5).tolist(), kind=kind, angle=round(angle, 1), gap=round(gap, 2), flip=bool(flip)))
    return out


def predict(segs, K):
    """what build-scene / check-scene / audit-scene count from the segment file alone (no ground, no scene)."""
    n6 = [g for g in segs if g.get('kit') == 'N6' and len(g['points']) >= 2]
    st = {g['id']: stations(g['points'], g['block'], g.get('closed'), K) for g in n6}; per = {}
    chunks = 0; probes = 0; cover = 0; nav = 0; nst = 0
    for g in n6:
        S = st[g['id']]; n = len(S['s']); L = float(S['s'][-1]); spacing = L / max(1, n - 1)
        kk = max(1, int(math.ceil(L / K['chunk_m'] - 1e-3)))
        cuts = sorted({(n - 1) if k == kk else int(min(max(round(k * K['chunk_m'] / max(1e-3, spacing)), 0), n - 1)) for k in range(kk + 1)})
        c = sum(1 for a, b in zip(cuts[:-1], cuts[1:]) if b > a)
        at = set(); s = np.float32(0); Lf = np.float32(L); lo = min(np.float32(1.5), Lf * np.float32(.5)); hi = max(Lf - np.float32(1.5), Lf * np.float32(.5))
        while s <= Lf:
            at.add(float(min(max(s, lo), hi))); s = np.float32(s + np.float32(K['probe_every_m']))
        at.add(float(hi))
        lq = np.float32(np.hypot(*np.diff(S['q'], axis=0).T).astype(np.float32).sum()); m = 0; s = np.float32(0)
        while s <= lq + np.float32(1e-3):
            m += 1; s = np.float32(s + np.float32(K['cover_every_m']))
        per[g['id']] = dict(stations=n, chunks=c, probes=len(at), cover_samples=m, length_m=round(L, 1))
        chunks += c; probes += len(at); cover += m; nav += (n + 1) // 2; nst += n
    J = joints(n6, st, K); kinds = {k: sum(1 for j in J if j['kind'] == k) for k in ('arc', 'cross', 'flush')}
    opens = sum(1 for g in n6 for e in g.get('ends', []) if e == 'open')
    return dict(segments=len(n6), other_kits=len(segs) - len(n6), length_m=round(sum(v['length_m'] for v in per.values()), 1), km=round(sum(Line(g['points']).L for g in n6) / 1000, 2),
                stations=nst, shell_chunks=chunks, joints=len(J), joint_kinds=kinds, joint_strips=kinds['arc'], joint_plugs=len(J), side_flips=[j['a'] + '/' + j['b'] for j in J if j['flip']],
                colliders=chunks + kinds['arc'] + len(J), open_ends=opens, probe_points=probes, probe_runs=probes * 3, joint_probe_runs=len(J) * 9, open_end_probe_runs=opens * 3,
                cover_samples=cover, nav_samples_max=nav), per, st, J


def mask_side_effects(segs, st, F, K):
    """what the cliff mask does to check-scene and audit-scene, from the stations alone:
      probe points whose start (3 m on the player side) AND retry start (1.3 m before the face) are masked -> 'no ground at the
      start' twice = INCONCLUSIVE in all three variants; cover samples (every 1.6 m) whose player-face point is masked ->
      no shrub can stand there = an uncovered sample inside rock."""
    both = []; first_only = []; cover_in = {}
    for g in segs:
        S = st[g['id']]; n = len(S['s']); L = float(S['s'][-1]); at = set(); s = np.float32(0); Lf = np.float32(L)
        lo = min(np.float32(1.5), Lf * np.float32(.5)); hi = max(Lf - np.float32(1.5), Lf * np.float32(.5))
        while s <= Lf:
            at.add(float(min(max(s, lo), hi))); s = np.float32(s + np.float32(K['probe_every_m']))
        at.add(float(hi))
        for a in sorted(at):
            k = int(np.argmin(np.abs(S['s'] - a))); frm = S['p'][k] - S['nb'][k] * K['probe_start_m']; rt = S['q'][k] - S['nb'][k] * (K['thick_m'] * .5 + K['probe_retry_m'])
            m1 = bool(F.masked(frm[0], frm[1])); m2 = bool(F.masked(rt[0], rt[1]))
            if m1 and m2: both.append([g['id'], round(float(S['s'][k]), 1)])
            elif m1: first_only.append([g['id'], round(float(S['s'][k]), 1)])
        q = S['q']; cum = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(q, axis=0).T))]); a = np.float32(0); c = 0
        while a <= np.float32(cum[-1]) + np.float32(1e-3):
            j = min(max(int(np.searchsorted(cum, float(a))), 1), n - 1); t = min(max((float(a) - cum[j - 1]) / max(1e-5, cum[j] - cum[j - 1]), 0), 1)
            qq = q[j - 1] + (q[j] - q[j - 1]) * t; nn = -(S['nb'][j - 1] + (S['nb'][j] - S['nb'][j - 1]) * t); nn = nn / (math.hypot(*nn) or 1.0); qf = qq + nn * (K['thick_m'] * .5)
            if F.masked(qf[0], qf[1]): c += 1
            a = np.float32(a + np.float32(K['cover_every_m']))
        if c: cover_in[g['id']] = c
    return dict(probe_points_no_ground_twice=both, probe_runs_inconclusive_expected=len(both) * 3, probe_points_first_start_masked=first_only,
                cover_samples_inside_rock=cover_in, cover_samples_inside_rock_total=sum(cover_in.values()),
                doc='inconclusive = the probe starts inside the cliff mask (the shell end is in rock: nothing to walk to); cover samples inside rock cannot be covered and are not visible')


def emulate_columns(segs, st, F, K):
    """CompactEnclosure305.Columns305 on the stage TERRAIN only (tile triangles, no rocks / walls / decks): ground = not masked
    and triangle slope < 60 deg (normal.y > .5) inside the terrain rectangle. Gives the shell column heights the editor should
    come close to: samples without ground, stations that borrow their heights, the tallest column, the lowest blocking height."""
    out = {}; tot = dict(missing_samples=0, borrowed_stations=0, low_block_stations=0); half = K['thick_m'] * .5

    def ground(p_):
        x, z = float(p_[0]), float(p_[1])
        if not (0 <= x <= 4000 and 0 <= z <= 6000) or F.masked_column(x, z) or float(F.slope(x, z)) >= K['ground_slope_deg']: return None
        return float(F.y(x, z))
    for g in segs:
        S = st[g['id']]; n = len(S['s']); lo = np.full(n, np.inf); hi = np.full(n, -np.inf); pl = np.full(n, -np.inf); miss = 0
        for k in range(n):
            q = S['q'][k]; nb = S['nb'][k]
            for a, pt in ((-4, q - nb * K['player_far_m'][0]), (-3, q - nb * K['player_far_m'][1]), (-2, q - nb * (half + K['face_pad_m'])), (-1, q - nb * K['across_m']), (0, q), (1, q + nb * K['across_m'])):
                y = ground(pt)
                if y is None: miss += 1; continue
                hi[k] = max(hi[k], y)
                if a <= -1: pl[k] = max(pl[k], y)
                if a >= -1: lo[k] = min(lo[k], y)
        borrowed = int(np.isinf(lo).sum()); ok = np.nonzero(~np.isinf(lo))[0]
        if not len(ok): out[g['id']] = dict(stations=n, missing_samples=miss, borrowed_stations=n, note='NO terrain ground on any station'); tot['missing_samples'] += miss; tot['borrowed_stations'] += n; continue
        for k in np.nonzero(np.isinf(lo))[0]:                               # nearest measured station, lower index first (the C# search order)
            best = min(ok, key=lambda i: (abs(int(i) - int(k)), 0 if i < k else 1)); lo[k] = lo[best]; hi[k] = max(hi[k], hi[best]); pl[k] = max(pl[k], pl[best])
        top = hi + K['above_m']; span = top - (lo - K['below_m']); block = np.where(np.isinf(pl), np.inf, top - pl); low = int((block < K['min_block_m']).sum())
        tot['missing_samples'] += miss; tot['borrowed_stations'] += borrowed; tot['low_block_stations'] += low
        if miss or borrowed or span.max() > K['span_flag_m'] or low:
            k = int(span.argmax()); out[g['id']] = dict(stations=n, missing_samples=miss, borrowed_stations=borrowed, span_max_m=round(float(span.max()), 1), span_max_at=[round(float(S['q'][k][0]), 1), round(float(S['q'][k][1]), 1)],
                                                        min_block_m=round(float(block.min()), 2), low_block_stations=low)
    return dict(totals=tot, runs=out, doc='terrain-only emulation; the editor adds rocks, walls, decks and the terrain edge. span = shell top - shell bottom of one column (305.1 build: 6.5 - 13 m)')


# ------------------------------------------------------------------ stage field + cliff mask
class Field:
    def __init__(self, cfg):
        self.cfg = cfg; hp = ROOT / cfg['stage_height']; self.new = B.load_height(hp); self.base = B.load_height(); self.height_sha = sha(hp); self.base_sha = sha(B.BASE_HEIGHT)
        self.ops_path = ROOT / cfg['ops']; self.ops = json.loads(self.ops_path.read_text(encoding='utf-8')); self.ops_sha = sha(self.ops_path)
        rep = json.loads((ROOT / cfg['stage_report']).read_text(encoding='utf-8'))
        if rep.get('out_sha256') != self.height_sha: raise SystemExit(f"REFUSED: {cfg['stage_height']} sha {self.height_sha[:12]} is not the one its report names ({str(rep.get('out_sha256'))[:12]})")
        if rep.get('ops_sha256') != self.ops_sha: raise SystemExit(f"REFUSED: {cfg['ops']} sha {self.ops_sha[:12]} is not the ops the stage height was made from ({str(rep.get('ops_sha256'))[:12]}); re-run scarp308 or point the config at the right ops")
        if rep.get('base_sha256') != self.base_sha: raise SystemExit('REFUSED: the base height changed since the stage height was made')
        if rep.get('stage') != cfg['stage']: raise SystemExit(f"REFUSED: the stage height is stage {rep.get('stage')}, the config says {cfg['stage']}")
        self.owner = np.load(ROOT / cfg['owner'])
        if self.owner.shape != self.new.shape: raise SystemExit('REFUSED: owner map shape')
        self.segid = [d['id'] for d in self.ops['segments']]; self.diff = self.new - self.base
        ch = np.abs(self.diff) > cfg['changed_eps_m']
        if int(ch.sum()) != int(rep.get('changed_cells', -1)): raise SystemExit(f"REFUSED: changed cells {int(ch.sum())} != report {rep.get('changed_cells')}")
        top = [self.segid.index(t) for t in cfg['mask']['top_forest_segments']]
        vtop = ch & np.isin(self.owner, top); vnon = ch & ~vtop
        if cfg['mask'].get('bench') == 'ground': vnon &= self.owner != -2     # v305.4a: the wall benches (owner -2) stay Enclosure305 ground - the seam shells stand on them
        qa = lambda v: v[:-1, :-1] | v[:-1, 1:] | v[1:, :-1] | v[1:, 1:]
        h00 = self.new[:-1, :-1]; h10 = self.new[:-1, 1:]; h01 = self.new[1:, :-1]; h11 = self.new[1:, 1:]
        self.triA = np.degrees(np.arctan(np.hypot(h10 - h00, h01 - h00) / CELL)); self.triB = np.degrees(np.arctan(np.hypot(h11 - h01, h11 - h10) / CELL))
        steep = np.maximum(self.triA, self.triB) >= cfg['mask']['slope_deg']
        self.mask_non = qa(vnon); self.mask_top = qa(vtop) & steep & ~self.mask_non; self.mask = self.mask_non | self.mask_top
        self.changed_quads = qa(ch); self.lifted_open = self.changed_quads & ~self.mask          # top-forest ground: seat here
        # v305.4: no shell on a top-forest segment's top - for the SHELL line the whole changed area of those segments counts as rock (the forest keeps the top as ground)
        self.top_quads = qa(vtop); self.shells_off_top = (cfg.get('top_forest') or {}).get('shells') == 'remove'
        self.mask_shell = (self.mask_non | self.top_quads) if self.shells_off_top else self.mask_non
        # review 2026-10-04: a masked quad whose tile triangle A (00, 10, 01: fx + fz <= 1) or B (11, 01, 10) has no changed vertex still
        # holds base ground a player stands on (the foot of a face). The shell columns must count it, or the shell top is cut short there
        self.free_a = self.mask & ~(ch[:-1, :-1] | ch[:-1, 1:] | ch[1:, :-1]); self.free_b = self.mask & ~(ch[1:, 1:] | ch[1:, :-1] | ch[:-1, 1:])
        # owner of a quad = owner of its highest-fill corner
        d4 = np.stack([self.diff[:-1, :-1], self.diff[:-1, 1:], self.diff[1:, :-1], self.diff[1:, 1:]]); o4 = np.stack([self.owner[:-1, :-1], self.owner[:-1, 1:], self.owner[1:, :-1], self.owner[1:, 1:]])
        self.qowner = np.take_along_axis(o4, d4.argmax(0)[None], 0)[0]

    def quad(self, x, z):
        return np.clip((np.asarray(z, float) // CELL).astype(int), 0, HH - 2), np.clip((np.asarray(x, float) // CELL).astype(int), 0, W - 2)

    def masked(self, x, z, which='mask'):
        i, j = self.quad(x, z); return getattr(self, which)[i, j]

    def masked_column(self, x, z):
        """the shell-column ground test (C# Ground305, 8-argument overload): masked quad, except its unchanged tile triangle."""
        x = np.asarray(x, float); z = np.asarray(z, float); i, j = self.quad(x, z); low = (x / CELL - j) + (z / CELL - i) <= 1
        return self.mask[i, j] & ~np.where(low, self.free_a[i, j], self.free_b[i, j])

    def y(self, x, z):
        return B.sample_tri(self.new, x, z)

    def slope(self, x, z):
        """slope (deg) of the tile triangle under (x, z)."""
        x = np.clip(np.asarray(x, float), 0, 3999.99); z = np.clip(np.asarray(z, float), 0, 5999.99); i, j = self.quad(x, z)
        low = (x / CELL - j) + (z / CELL - i) <= 1
        return np.where(low, self.triA[i, j], self.triB[i, j])

    def seg_at(self, x, z):
        i, j = self.quad(x, z); o = int(self.qowner[i, j]); return self.segid[o] if o >= 0 else '-'

    def mask_bytes(self):
        # 0 ground, 1 no ground, 2 / 3 = no ground for forest / cover fill / plugs / probe starts, but triangle A / B is ground for the shell columns
        return np.ascontiguousarray(np.where(self.free_a, 2, np.where(self.free_b, 3, self.mask.astype(np.uint8))), dtype=np.uint8).tobytes()


# ------------------------------------------------------------------ inputs (frozen 305.2)
def frozen_inputs(cfg, wr):
    """(segments doc, candidates doc, their source paths). The 305.2 files are copied once, byte for byte."""
    o = cfg['_o']; out = []
    for live, froz, what in ((o['live_segments'], o['segments_frozen'], 'segments305.json'), (o['live_candidates'], o['candidates_frozen'], 'forest305_candidates.json')):
        if froz.exists():
            src = froz
            # review 2026-10-04: a live file that is itself the source version but not the frozen bytes = 305.2 was regenerated after the freeze
            if head_version(live) == cfg['from_version'] and live.read_bytes() != froz.read_bytes():
                raise SystemExit(f"REFUSED: {B.rel(live)} is a {cfg['from_version']} file that differs from the frozen copy {B.rel(froz)} (regenerated after the freeze?). "
                                 f"Rename the stale frozen copy (keep it) and run build again to freeze the new file; nothing changed")
        else:
            v = json.loads(live.read_text(encoding='utf-8')).get('version')
            if v != cfg['from_version']: raise SystemExit(f"REFUSED: {B.rel(live)} is version {v} and there is no frozen {cfg['from_version']} copy ({B.rel(froz)}); cannot derive {cfg['to_version']}")
            wr.put(froz, live.read_bytes()); src = live
        doc = json.loads(Path(src).read_text(encoding='utf-8'))
        if doc.get('version') != cfg['from_version']: raise SystemExit(f"REFUSED: {B.rel(src)} is version {doc.get('version')}, expected {cfg['from_version']}")
        out += [doc, froz]                                                # the frozen copy is the named source (same bytes on the first run)
    return out[0], out[2], out[1], out[3]


def replaced_lists(F, stage):
    """(full ids -> cliff segment, partial id -> (spec, cliff segment)) from the ops' `replaces` of the built stages."""
    import scarp308 as SC
    st = F.ops.get('stages', SC.STAGES_DEFAULT); full = {}; part = {}
    for d in F.ops['segments']:
        if d.get('skip') or d.get('reference_only') or SC.stage_rank(st, SC.seg_stage(d)) > SC.stage_rank(st, stage): continue
        d = SC.seg_for_stage(d, st, stage)                                # ops v6: stage_overrides (replaces of a later stage)
        for r in d.get('replaces', []):
            rid = r.split(' ')[0]
            if '(' in r: part[rid] = (r, d['id'])
            else: full[rid] = d['id']
    return full, part


def stated_cut(line, spec, step):
    """arc interval [a, b] the `replaces` entry leaves ('E305_054 (x>=2160)' -> the part with x < 2160)."""
    m = re.search(r'\(([xz])([<>])=([-0-9.]+)\)', spec)
    if not m: raise SystemExit(f'REFUSED: cannot read the clip of {spec}')
    col = 0 if m.group(1) == 'x' else 1; sign = 1 if m.group(2) == '>' else -1; v = float(m.group(3))
    s = np.linspace(0, line.L, int(line.L / step) + 1); c = (line.at(s)[:, col] - v) * sign >= 0     # True = replaced
    if not c.any() or c.all(): raise SystemExit(f'REFUSED: clip {spec} leaves nothing or everything')
    if c[-1] and not c[0]: return 0.0, float(s[np.argmax(c)])            # replaced tail: keep up to the first replaced point
    if c[0] and not c[-1]: return float(s[len(c) - 1 - np.argmax(c[::-1])]), line.L      # replaced head: keep after the last replaced point
    raise SystemExit(f'REFUSED: clip {spec} cuts the run in the middle (both ends on one side)')


# ------------------------------------------------------------------ build 305.3
def build(cfg, wr, quiet=False):
    say = (lambda *a: None) if quiet else print
    F = Field(cfg); K = cfg['shell_copy']; CE = cfg['cliff_end']; o = cfg['_o']
    S2, C2, seg_src, cand_src = frozen_inputs(cfg, wr)
    jraw = dict(seg=(seg_src if seg_src.exists() else o['live_segments']).read_bytes(), cand=(cand_src if cand_src.exists() else o['live_candidates']).read_bytes())
    E = json.loads((ROOT / cfg['eval']).read_text(encoding='utf-8'))
    full, part = replaced_lists(F, cfg['stage'])
    plan_list = sorted(r for r in E['forest_runs_replaced']['list'] if '(within ' not in r); mine = sorted(list(full) + [v[0] for v in part.values()])      # (within ...) = a clearing inside a run, not a replaced run
    if plan_list != mine: raise SystemExit(f'REFUSED: eval forest_runs_replaced {plan_list} != ops replaces {mine}')
    segs2 = apply_reroutes(cfg, S2['segments']); by2 = {s['id']: s for s in segs2}
    for rid in list(full) + list(part):
        if rid not in by2: raise SystemExit(f'REFUSED: replaced run {rid} is not in the {cfg["from_version"]} segments')
    wall_runs = list(F.ops.get('wall', {}).get('replaces', [])) if cfg.get('seam_runs') == 'remove' else []
    for rid in wall_runs:                                                 # v305.4b: the long wall replaces the seam runs
        if rid not in by2: raise SystemExit(f'REFUSED: wall run {rid} is not in the {cfg["from_version"]} segments')
        full[rid] = 'wall'
    top_ids = set(cfg['mask']['top_forest_segments']); shells_off = F.shells_off_top
    ep = lambda s, e: np.asarray(s['points'][-1 if e else 0], float)

    def mates(s, e, ids):
        p = ep(s, e); return [t['id'] for t in segs2 if t['id'] in ids and t['id'] != s['id'] for k in (0, 1) if math.hypot(*(ep(t, k) - p)) <= K['joint_eps_m']]

    # per run: samples of the shell centre line on the ORIGINAL polyline, mask flags, stated cut
    info = {}
    for s in segs2:
        if s['id'] in full: continue
        ln = Line(s['points']); a, b = (0.0, ln.L)
        if s['id'] in part and not (shells_off and part[s['id']][1] in top_ids): a, b = stated_cut(ln, part[s['id']][0], CE['clip_sample_m'])
        # (v305.4: a clip stated by a top-forest segment is not cut at the stated value - the shell mask of that top cuts the run where it meets the rock)
        S = stations(s['points'], s['block'], False, K, step=CE['sample_m']); inr = (S['s'] >= a - 1e-9) & (S['s'] <= b + 1e-9)
        info[s['id']] = dict(ln=ln, a=a, b=b, S=S, non=F.masked(S['q'][:, 0], S['q'][:, 1], 'mask_shell') & inr,
                             top=(F.masked(S['q'][:, 0], S['q'][:, 1], 'mask_top') & inr) if not shells_off else np.zeros(len(inr), bool), inr=inr)
    removed = dict(full); gone_note = {}
    for rid, I in info.items():
        if not (I['inr'] & ~I['non'] & ~I['top']).any(): removed[rid] = 'inside ' + F.seg_at(*I['S']['q'][np.argmax(I['inr'])]); gone_note[rid] = 'every shell station lies in the cliff mask'
    # pieces: iterate because a removed run takes its neighbour's anchor away
    plan = {}
    for _ in range(8):
        plan = {}; kept_ids = {s['id'] for s in segs2 if s['id'] not in removed}; changed = False
        for s in segs2:
            rid = s['id']
            if rid in removed: continue
            I = info[rid]; sv = I['S']['s']; non = I['non']; top = I['top']; inr = I['inr']
            idx = np.nonzero(inr & ~non)[0]
            runs_ = np.split(idx, np.nonzero(np.diff(idx) > 1)[0] + 1) if len(idx) else []
            pieces = []
            for r_ in runs_:
                clear = r_[~top[r_]]
                if not len(clear): continue
                u, v = int(clear[0]), int(clear[-1])                     # first / last unmasked sample (top-mask samples trimmed at the ends)
                lo_anch = u == 0 and I['a'] == 0.0 and (not mates(s, False, set(by2)) or bool(mates(s, False, kept_ids)))
                hi_anch = v == len(sv) - 1 and I['b'] >= I['ln'].L - 1e-9 and (not mates(s, True, set(by2)) or bool(mates(s, True, kept_ids)))
                lo_rock = int(r_[0]) > 0 and bool(non[int(r_[0]) - 1]) and inr[int(r_[0]) - 1]
                hi_rock = int(r_[-1]) < len(sv) - 1 and bool(non[int(r_[-1]) + 1]) and inr[int(r_[-1]) + 1]
                pieces.append(dict(u=u, v=v, r0=int(r_[0]), r1=int(r_[-1]), lo_anch=lo_anch, hi_anch=hi_anch, lo_rock=lo_rock, hi_rock=hi_rock))
            keep = [p for p in pieces if p['lo_anch'] or p['hi_anch']]
            if not keep:
                removed[rid] = 'orphan'; gone_note[rid] = 'no arc outside the cliff mask still joins a kept run'; changed = True; continue
            if len(keep) > 1: raise SystemExit(f'REFUSED: {rid} splits into {len(keep)} anchored pieces around a cliff mass (not supported: split the run by hand)')
            plan[rid] = (keep[0], [p for p in pieces if p is not keep[0]])
        if not changed: break

    # new segments
    segs3 = []; cliff_ends = []; arcs = {}; notes_by = {}
    gate = cfg['gate_ends_open']['ends']
    for s in segs2:
        rid = s['id']
        if rid in removed: continue
        I = info[rid]; ln = I['ln']; sv = I['S']['s']; pc, dropped = plan[rid]; t = json.loads(json.dumps(s)); notes = []
        s0 = 0.0 if pc['lo_anch'] and pc['u'] == 0 else float(sv[pc['u']]); s1 = ln.L if pc['hi_anch'] and pc['v'] == len(sv) - 1 else float(sv[pc['v']])
        ends_new = list(t.get('ends', ['tie', 'tie'])); ec = [None, None]
        for side, anch, rock, r_edge in ((0, pc['lo_anch'] and pc['u'] == 0, pc['lo_rock'], pc['r0']), (1, pc['hi_anch'] and pc['v'] == len(sv) - 1, pc['hi_rock'], pc['r1'])):
            if anch: continue
            if rock:                                                     # into rock: first masked sample + stub, never past the masked interval or the stated cut
                k = r_edge + (1 if side else -1); entry = float(sv[k]); step = 1 if side else -1; last = k
                while 0 <= last + step < len(sv) and I['non'][last + step] and I['inr'][last + step]: last += step
                end = entry + CE['stub_m'] * step; end = min(end, float(sv[last])) if side else max(end, float(sv[last]))
                if side: s1 = end
                else: s0 = end
                qe = I['S']['q'][k]; ec[side] = dict(kind='rock', cliff=F.seg_at(qe[0], qe[1]), entry_arc=round(entry, 2), entry_q=[round(float(qe[0]), 2), round(float(qe[1]), 2)], stub_m=round(abs(end - entry), 2))
            else:                                                        # on a top-forest cliff top (rim) or at the stated cut on open ground
                edge = float(sv[pc['v' if side else 'u']]); qe = I['S']['q'][pc['v' if side else 'u']]
                on_top = bool(F.masked(qe[0], qe[1], 'lifted_open')); near_top = bool(I['top'][min(len(sv) - 1, pc['v'] + 1)] if side else I['top'][max(0, pc['u'] - 1)])
                ec[side] = dict(kind='top' if (on_top or near_top) else 'cut', cliff=F.seg_at(qe[0], qe[1]) if on_top else (F.seg_at(*I['S']['q'][min(len(sv) - 1, pc['v'] + 1) if side else max(0, pc['u'] - 1)]) if near_top else '-'),
                                entry_arc=round(edge, 2), entry_q=[round(float(qe[0]), 2), round(float(qe[1]), 2)], stub_m=0.0)
                if ec[side]['kind'] == 'cut': raise SystemExit(f'REFUSED: {rid} end {side} would end on open ground at {ec[side]["entry_q"]} (stated cut outside any cliff mass): no barrier there')
        if (s0, s1) != (0.0, ln.L):
            t['points'] = ln.sub(s0, s1); t['length_m'] = round(Line(t['points']).L, 1)
        arcs[rid] = (s0, s1)
        for side in (0, 1):
            if ec[side] is None: continue
            old_mates = mates(s, bool(side), set(by2)); ends_new[side] = 'tie'
            if ec[side]['kind'] == 'rock':
                notes.append(f"{cfg['to_version']} end {side}: cut where the shell enters {ec[side]['cliff']} rock at ({ec[side]['entry_q'][0]:.1f}, {ec[side]['entry_q'][1]:.1f}) and carried {ec[side]['stub_m']:.1f} m into it"
                             + (f" (was: joint with {', '.join(old_mates)})" if old_mates else '') + f"; {round((s0 if side == 0 else ln.L - s1), 1)} m of the {cfg['from_version']} line dropped")
            else:
                notes.append(f"{cfg['to_version']} end {side}: ends on the {ec[side]['cliff']} cliff top at ({ec[side]['entry_q'][0]:.1f}, {ec[side]['entry_q'][1]:.1f}), first shell station clear of the face"
                             + (f" ({part[rid][0]} replaced)" if rid in part else '') + '; the face below closes, this end has no closure role')
        if rid in part: notes.append(f"{cfg['to_version']}: {part[rid][0]} replaced by cliff {part[rid][1]} (ops {cfg['ops'].split('/')[-1]})")
        if rid in gate:
            ends_new[gate[rid]] = 'open'; notes.append(f"{cfg['to_version']} end {gate[rid]}: 'open' - " + cfg['gate_ends_open'].get('note', f"the {cfg['from_version']} tie to the Seal308 anchor is void (Seal308 never applied; the stage-1b gate + long wall close the 53 m gap A-B)"))
        brid = [(float(sv[r[0]]), float(sv[r[-1]])) for r in (np.split(np.nonzero(I['top'] & (sv > s0) & (sv < s1))[0], np.nonzero(np.diff(np.nonzero(I['top'] & (sv > s0) & (sv < s1))[0]) > 1)[0] + 1)) if len(r)]
        for a_, b_ in brid: notes.append(f"{cfg['to_version']}: the shell crosses a {cfg['mask']['top_forest_segments'][0]} face at arc {a_ - s0:.1f}-{b_ - s0:.1f} m (kept: forest and shell continue on the cliff top)")
        if notes or ends_new != t.get('ends'):
            t['ends'] = ends_new; t['notes'] = list(t.get('notes', [])) + notes
            t['cliff308'] = dict(arc_of_305_2=[round(s0, 2), round(s1, 2)], ends=ec, dropped_pieces=[[round(float(sv[p['u']]), 1), round(float(sv[p['v']]), 1)] for p in dropped])
        notes_by[rid] = notes; segs3.append(t)
        for side in (0, 1):
            if ec[side] is not None: cliff_ends.append((rid, side, ec[side], mates(s, bool(side), set(by2))))

    # as-built check of every cliff end on the stations the editor will make
    exp3, per3, st3, J3 = predict(segs3, K); joints_tab = []
    by3 = {s['id']: s for s in segs3}
    for rid, side, e, old in cliff_ends:
        S = st3[rid]; n = len(S['s']); k = n - 1 if side else 0; q = S['q'][k]; nb = S['nb'][k]; half = K['thick_m'] * .5
        inmask = F.masked(S['q'][:, 0], S['q'][:, 1]); order = range(n - 1, -1, -1) if side else range(n)
        clear = next((i for i in order if not inmask[i]), None); row = dict(run=rid, end=side, kind=e['kind'], cliff=e['cliff'], was_joint_with=old)
        row.update(line_end=by3[rid]['points'][-1 if side else 0], shell_end=[round(float(q[0]), 2), round(float(q[1]), 2)], entry=e['entry_q'], stub_m=e['stub_m'])
        if e['kind'] == 'rock':
            step = 1 if side else -1; sup = []
            for i in range(clear, k + step, step):                       # player-side supports the editor samples at the last open station and along the stub
                for d in (half + K['face_pad_m'], K['across_m'], K['player_far_m'][1], K['player_far_m'][0], 0.0):
                    p_ = S['q'][i] - S['nb'][i] * d
                    if not F.masked_column(p_[0], p_[1]) and F.slope(p_[0], p_[1]) < K['ground_slope_deg']: sup.append(float(F.y(p_[0], p_[1])))
            foot = max(sup) if sup else float(F.y(*S['q'][clear])); corners = [q - nb * half, q + nb * half, q]
            rock = min(float(F.y(c[0], c[1])) for c in corners); cover = rock - foot
            row.update(foot_y=round(foot, 2), shell_top_y=round(foot + K['above_m'], 2), rock_y_over_end_cap=round(rock, 2), cover_m=round(cover, 2), buried=bool(cover >= CE['min_cover_m']),
                       stations_in_mask=int(inmask.sum()), end_in_mask=bool(inmask[k]), last_clear_station=[round(float(S['q'][clear][0]), 2), round(float(S['q'][clear][1]), 2)], kc=int(clear), step=step)
            if not row['buried'] or not row['end_in_mask']: raise SystemExit(f"REFUSED: {rid} end {side} is not buried (cover {cover:.1f} m, in mask {row['end_in_mask']}) - raise cliff_end.stub_m or fix the line by hand")
        else:
            row.update(stations_in_mask=int(inmask.sum()), end_in_mask=bool(inmask[k]), note='on the cliff top; no closure role (the face below closes)')
        joints_tab.append(row)
    # a kept run that goes over a top-forest face: the shell bridges the face (no end). Foot side = the side not on lifted ground
    for g in segs3:
        S = st3[g['id']]; mt = F.masked(S['q'][:, 0], S['q'][:, 1], 'mask_top'); n = len(mt); idx = np.nonzero(mt)[0]
        for r_ in (np.split(idx, np.nonzero(np.diff(idx) > 1)[0] + 1) if len(idx) else []):
            i0, i1 = int(r_[0]), int(r_[-1])
            if i0 == 0 or i1 == n - 1: continue
            up0 = bool(F.masked(S['q'][i0 - 1][0], S['q'][i0 - 1][1], 'lifted_open')); up1 = bool(F.masked(S['q'][i1 + 1][0], S['q'][i1 + 1][1], 'lifted_open'))
            if up0 == up1: raise SystemExit(f"REFUSED: {g['id']} crosses a top-forest face at arc {S['s'][i0]:.1f} with {'both' if up0 else 'no'} side on the cliff top")
            kc, step = (i1 + 1, -1) if up0 else (i0 - 1, 1); mid = S['q'][(i0 + i1) // 2]
            joints_tab.append(dict(run=g['id'], end=None, kind='bridge', cliff=F.seg_at(mid[0], mid[1]), was_joint_with=[], at=[round(float(mid[0]), 2), round(float(mid[1]), 2)], shell_end=[round(float(mid[0]), 2), round(float(mid[1]), 2)],
                                   arc_m=[round(float(S['s'][i0]), 1), round(float(S['s'][i1]), 1)], stations_in_mask=int(i1 - i0 + 1), foot_station=[round(float(S['q'][kc][0]), 2), round(float(S['q'][kc][1]), 2)],
                                   foot_y=round(float(F.y(*S['q'][kc])), 2), top_y=round(float(F.y(*S['q'][i0 - 1 if up0 else i1 + 1])), 2), kc=int(kc), step=int(step),
                                   note='the run continues over the face onto the cliff top: no end, the shell columns step from the foot to the top across the face'))
    # every shell station must stand on unmasked ground or belong to a rock stub / a top-face crossing
    stray = []
    for g in segs3:
        S = st3[g['id']]; m = F.masked(S['q'][:, 0], S['q'][:, 1], 'mask_non'); n = len(m)
        idx = np.nonzero(m)[0]
        for i in idx:
            d_end = min(float(S['s'][i]), float(S['s'][-1] - S['s'][i]))
            if d_end > CE['stub_m'] + K['step_m'] + CE['stray_tol_m']: stray.append((g['id'], round(float(S['s'][i]), 1)))
    if stray: raise SystemExit(f'REFUSED: shell stations inside a foreign cliff mass away from a stub: {stray[:8]}')

    # ---- candidates
    cands = C2['candidates']; by_cluster = {}
    for i, c in enumerate(cands): by_cluster.setdefault(c['ClusterId'], []).append(i)
    keep = np.ones(len(cands), bool); why = np.zeros(len(cands), np.int8)          # 1 removed run, 2 removed arc, 3 cliff mask
    xz = np.array([[c['Position'][0], c['Position'][2]] for c in cands], float)
    for rid, ii in by_cluster.items():
        ii = np.asarray(ii)
        if rid in removed: keep[ii] = False; why[ii] = 1; continue
        if rid not in arcs: raise SystemExit(f'REFUSED: candidates of unknown run {rid}')
        s0, s1 = arcs[rid]; ln = info[rid]['ln']
        if (s0, s1) != (0.0, ln.L):
            arc, _ = ln.project(xz[ii]); out = (arc < s0 - 1e-6) | (arc > s1 + 1e-6); keep[ii[out]] = False; why[ii[out]] = 2
    kind = np.array([c['kind'] for c in cands]); rules = {}; euler = {}; named = {}
    TF = cfg.get('top_forest') or {}; CR = cfg.get('candidates') or {}
    if TF:      # v305.4: the top of a top-forest segment carries trees only as a rim band (no shell there), clear of the bay and of the declared points
        on_top = np.asarray(F.masked(xz[:, 0], xz[:, 1], 'top_quads')) & ~np.asarray(F.masked(xz[:, 0], xz[:, 1]))
        rim = np.full(len(cands), np.inf); import scarp308 as SC_
        for d in F.ops['segments']:
            if d['id'] in TF['segments']:      # the op line as scarp308 raises it (the face lies on it)
                sg = SC_.Seg(d); rim = np.minimum(rim, dist_to_lines(xz, [sg.P.tolist() + ([sg.P[0].tolist()] if sg.closed else [])]))
        band = on_top & (rim <= TF['rim_band_m']) & np.isin(kind, TF['rim_band_kinds'])
        bay = np.hypot(xz[:, 0] - TF['bay_clear']['at'][0], xz[:, 1] - TF['bay_clear']['at'][1]) <= TF['bay_clear']['r_m']
        kp = np.zeros(len(cands), bool)
        for q in TF.get('keepouts', []): kp |= np.hypot(xz[:, 0] - q['at'][0], xz[:, 1] - q['at'][1]) <= q.get('r_m', TF.get('keepout_r_m', 6.0))
        back = on_top & band & ~bay & ~kp & (why > 0) & (why < 3); keep[back] = True; why[back] = 0           # trees of a removed top run stay inside the rim band
        off = on_top & keep & ~(band & ~bay & ~kp); keep[off] = False; why[off] = 4
        rules['top_rim_band'] = dict(on_top=int(on_top.sum()), kept_in_rim_band=int((on_top & keep).sum()), kept_from_removed_top_runs=int(back.sum()), dropped_outside_band_or_kind=int((on_top & ~band).sum()),
                                     dropped_bay_clear=int((on_top & band & bay).sum()), dropped_keepouts=int((on_top & band & ~bay & kp).sum()))
    mk = F.masked(xz[:, 0], xz[:, 1]) & keep; keep[mk] = False; why[mk] = 3
    if cfg.get('wall'):      # v305.4: nothing inside the long wall / gate / bench footprint
        WL = [ln['line'] for ln in F.ops.get('wall', {}).get('lines', [])] + ([F.ops['wall']['gate']] if F.ops.get('wall', {}).get('gate') else [])
        wk_ = keep & (dist_to_lines(xz, WL) <= cfg['wall']['keepout_m']); keep[wk_] = False; why[wk_] = 5
        rules['wall_footprint'] = dict(dropped=int(wk_.sum()), keepout_m=cfg['wall']['keepout_m'], lines=len(WL))
    if CR.get('rock_trunk'):      # v305.4: no tree trunk inside a rock of the named sheets or inside a lip
        RT = CR['rock_trunk']; rocks = sheet_rocks(RT['sheets'], RT['rock_category']); hit = near_any(xz, rocks, RT['pad_m']); tr = keep & (kind == 'tree') & (hit >= 0)
        keep[tr] = False; why[tr] = 6; lipn = 0; in_rock = (kind == 'tree') & (hit >= 0)
        lp = ROOT / CR['lips'] if CR.get('lips') else None
        if lp is not None and lp.exists():
            LL = [l['line'] for l in json.loads(lp.read_text(encoding='utf-8')).get('lips', []) if len(l['line']) > 1 and (l.get('required') or not CR.get('lips_required_only'))]
            lt = keep & (kind == 'tree') & (dist_to_lines(xz, LL) <= CR['lip_clear_m']) if LL else np.zeros(len(cands), bool); keep[lt] = False; why[lt] = 6; lipn = int(lt.sum())
        rules['rock_trunk'] = dict(rocks_indexed=len(rocks), sheets=RT['sheets'], pad_m=RT['pad_m'], dropped_in_sheet_rocks=int(tr.sum()), dropped_in_lips=lipn)
    lift = F.masked(xz[:, 0], xz[:, 1], 'lifted_open') & keep
    if CR.get('log'):      # v305.4: a log lies on its ground (pitch / roll from the tile triangle) or is dropped on a slope
        LG = CR['log']; sl_ = np.asarray(F.slope(xz[:, 0], xz[:, 1]), float); lg = keep & (kind == 'log'); dropl = lg & (sl_ > LG['drop_above_deg']); keep[dropl] = False; why[dropl] = 7; tilt = lg & ~dropl & (sl_ >= LG['tilt_from_deg'])
        for i in np.nonzero(tilt)[0]: euler[i] = _unity_euler(tri_normal(F, float(xz[i, 0]), float(xz[i, 1])), float(cands[i]['Euler'][1]))
        rules['log'] = dict(logs=int(lg.sum()), dropped_above_deg=int(dropl.sum()), tilted=int(tilt.sum()), level=int((lg & ~dropl & ~tilt).sum()), drop_above_deg=LG['drop_above_deg'], tilt_from_deg=LG['tilt_from_deg'])
        lift = lift & keep
    if CR:      # the look defects the config names: every instance near the named place, whatever rule decided its fate
        WHY = {0: 'kept', 1: 'dropped: its run is removed', 2: 'dropped: on a removed arc of its run', 3: 'dropped: cliff mask', 4: 'dropped: cliff top outside the rim band / inside the bay clearing',
               5: 'dropped: wall footprint', 6: 'dropped: trunk inside a rock / lip', 7: 'dropped: log on a slope over the limit'}
        v3 = ENC / (cfg.get('install') or {}).get('revert_to_305_3', {}).get('candidates', '_none_'); ids3 = None
        if v3.exists(): ids3 = set(re.findall(r'"Id": "([^"]+)"', v3.read_text(encoding='utf-8')))
        slope_all = np.asarray(F.slope(xz[:, 0], xz[:, 1]), float)
        for rule_, sel in (('log', kind == 'log'), ('rock_trunk', in_rock if CR.get('rock_trunk') else np.zeros(len(cands), bool))):
            for q in (CR.get(rule_) or {}).get('name_near', []):
                for i in np.nonzero(sel & (np.hypot(xz[:, 0] - q['at'][0], xz[:, 1] - q['at'][1]) <= q['r_m']))[0]:
                    e_ = dict(rule=rule_, run=cands[i]['ClusterId'], xz=[round(float(xz[i, 0]), 1), round(float(xz[i, 1]), 1)], slope_deg=round(float(slope_all[i]), 1), in_the_scene_now_305_3=(cands[i]['Id'] in ids3) if ids3 is not None else None,
                              fate=WHY[int(why[i])] + ((', tilted to the ground: Euler ' + str(euler[i])) if i in euler else '') + (', re-seated on the stage height' if keep[i] and lift[i] else ''))
                    if rule_ == 'rock_trunk': e_.update(rock=rocks[hit[i]][3], rock_xz=[round(rocks[hit[i]][0], 1), round(rocks[hit[i]][1], 1)], rock_r_m=round(rocks[hit[i]][2], 2), trunk_from_rock_centre_m=round(float(math.hypot(xz[i, 0] - rocks[hit[i]][0], xz[i, 1] - rocks[hit[i]][1])), 2))
                    named[cands[i]['Id']] = e_
    newy = np.round(F.y(xz[:, 0], xz[:, 1]), 2); out_c = []; reseat = 0; reseat_dy = []
    for i, c in enumerate(cands):
        if not keep[i]: continue
        if lift[i] or i in euler:
            d = dict(c)
            if lift[i]: d['Position'] = [c['Position'][0], float(newy[i]), c['Position'][2]]; reseat_dy.append(float(newy[i]) - c['Position'][1]); reseat += 1
            if i in euler: d['Euler'] = euler[i]
            out_c.append(d)
        else: out_c.append(c)
    kinds = lambda L: {k: sum(1 for c in L if c['kind'] == k) for k in ('tree', 'shrub', 'log')}
    cstats = dict(kinds(out_c), **{'from_' + cfg['from_version']: len(cands), 'dropped_removed_run': int((why == 1).sum()), 'dropped_removed_arc': int((why == 2).sum()), 'dropped_cliff_mask': int((why == 3).sum()),
                                   'reseated_on_lifted_ground': reseat, 'reseat_dy_m_min_max': [round(min(reseat_dy), 2), round(max(reseat_dy), 2)] if reseat_dy else None})
    if rules: cstats.update(dropped_top_outside_rim_band=int((why == 4).sum()), dropped_wall_footprint=int((why == 5).sum()), dropped_trunk_in_rock=int((why == 6).sum()), dropped_log_on_slope=int((why == 7).sum()), rules=rules, named_instances=named)
    C3 = dict(version=cfg['to_version'], band_m=C2.get('band_m', 24.0), stats=cstats, from_stats=C2.get('stats'), prototypes=sorted({c['PrototypeId'] for c in out_c}),
              cliff308=dict(height_sha256=F.height_sha, ops_sha256=F.ops_sha, stage=cfg['stage'], tool='Tools/Art/enclosure305_cliff308.py'), candidates=out_c)
    cand_bytes = (json.dumps(C3, ensure_ascii=False) + '\n').encode('utf-8')

    # ---- segment document
    exp2, per2, st2, J2 = predict(segs2, K); mask_b = F.mask_bytes()
    S3 = dict(version=cfg['to_version'], source=f"{B.rel(seg_src)} ({cfg['from_version']}) + {cfg['ops']} stage {cfg['stage']}", phase=S2.get('phase', 1),
              rule=S2.get('rule', '') + f". {cfg['to_version']} (cliff stage {cfg['stage']}, D308-9/9c/9d): the runs the cliff segments replace are gone, every kept run that meets a cliff mass ends {CE['stub_m']} m inside its rock, "
                   + cfg.get('rule_tail', "the gate ends of E305_084 / E305_056 are open until the stage-1b gate + long wall (Seal308 was never applied).") + " Tool: Tools/Art/enclosure305_cliff308.py",
              requires_height_sha256=F.height_sha,
              cliff_mask=dict(file=B.rel(o.get('live_mask', o['mask'])), sha256=sha_bytes(mask_b), rows=HH - 1, cols=W - 1, cell_m=CELL, masked_quads=int(F.mask.sum()),
                              layout='uint8, row-major: index = floor(z / cell) * cols + floor(x / cell); 0 = ground; 1 = no Enclosure305 ground (shell column, tree, cover shrub, plug, probe start); '
                                     '2 / 3 = the same, except that the tile triangle with no changed vertex (2: fx + fz <= 1 = corners 00, 10, 01; 3: corners 11, 01, 10) is ground for the shell columns',
                              quads_with_a_free_triangle=int(F.free_a.sum() + F.free_b.sum()),
                              top_forest_segments=cfg['mask']['top_forest_segments'], slope_deg=cfg['mask']['slope_deg']),
              cliff308=dict(stage=cfg['stage'], ops=cfg['ops'], ops_sha256=F.ops_sha, height=cfg['stage_height'], height_sha256=F.height_sha, base_sha256=F.base_sha, from_version=cfg['from_version'],
                            from_sha256=sha_bytes(jraw['seg']), removed={k: removed[k] for k in sorted(removed)}, partial={k: v[0] for k, v in part.items()},
                            wall_runs_kept_until_1b=list(F.ops.get('wall', {}).get('replaces', [])), stub_m=CE['stub_m'],
                            **(dict(variant=cfg['variant'], seam_runs=cfg.get('seam_runs'), wall_runs_removed=wall_runs) if cfg.get('variant') else {})),
              components=S2.get('components'), summary_305_1=S2.get('summary'), summary_seal308_305_2=S2.get('summary_seal308'),
              summary_all=dict(segments=len(segs3), km=round(sum(Line(s['points']).L for s in segs3) / 1000, 2), ea_km=round(sum(Line(s['points']).L for s in segs3 if s.get('ea')) / 1000, 2),
                               open_ends=sum(e == 'open' for s in segs3 for e in s['ends']),
                               by_zone={z: round(sum(Line(s['points']).L for s in segs3 if s['zone'] == z) / 1000, 2) for z in sorted({s['zone'] for s in segs3})}),
              segments=segs3)
    seg_bytes = jbytes(S3)

    # ---- forest estimate against the sheet that is in the project now (built from 305.1 on 2026-09-30)
    est = forest_estimate(cfg, out_c, {s['id'] for s in segs3}, arcs, info, {c['Id']: int(w) for c, w in zip(cands, why)})
    side = mask_side_effects(segs3, st3, F, K); cols = emulate_columns(segs3, st3, F, K)
    extra = {}
    if cfg.get('vault'):      # v305.4: the vault margin of every station near a rock end + of the named weak spots, and the joints-scene forecast
        V = cfg['vault']; vm = {}; low = []
        for g in segs3:
            S = st3[g['id']]; n = len(S['s']); want = set()
            for r in joints_tab:
                if r['run'] == g['id'] and r.get('kc') is not None: want |= set(range(max(0, r['kc'] - V['joint_stations']), min(n, r['kc'] + V['joint_stations'] + 1)))
            for q in V.get('watch', []):
                if q['run'] == g['id']: want |= set(np.nonzero(np.hypot(S['q'][:, 0] - q['at'][0], S['q'][:, 1] - q['at'][1]) <= q['r_m'])[0].tolist())
            want = sorted(k for k in want if not F.masked(S['q'][k][0], S['q'][k][1]))
            if not want: continue
            res = vault_margins(S, F, K, V, want); ok = [(v['margin_m'], k) for k, v in res.items() if v]
            if not ok: continue
            m, k = min(ok); vm[g['id']] = dict(stations=len(ok), margin_min_m=m, at_station=int(k), **{kk: res[k][kk] for kk in ('margin_with_step_m', 'stance_xz', 'stance_y', 'crossing_station', 'shell_top_y', 'station_xz')})
            if m < V['min_margin_m']: low.append((g['id'], m))
        watch = []
        for q in V.get('watch', []):
            if q['run'] not in st3: watch.append(dict(q, result='run removed')); continue
            S = st3[q['run']]; near = np.nonzero((np.hypot(S['q'][:, 0] - q['at'][0], S['q'][:, 1] - q['at'][1]) <= q['r_m']) & ~np.asarray(F.masked(S['q'][:, 0], S['q'][:, 1])))[0]
            res = vault_margins(S, F, K, V, near.tolist()) if len(near) else {}; ok = [(v['margin_m'], k) for k, v in res.items() if v]
            watch.append(dict(q, stations=len(ok), margin_min_m=min(ok)[0] if ok else None, at=res[min(ok)[1]] if ok else None, ok=bool(ok and min(ok)[0] >= V['min_margin_m'])))
        extra['vault_margin'] = dict(model={k: v for k, v in V.items() if k not in ('watch',)}, doc='the review model of V305_3 section 14 as a table (국 lift + jump from a walkable stance on the player side, fall during the flight, one point of the shell centre line): '
                                     'margin = shell top - best feet height where the path crosses the shell. Terrain only (tile triangles); the editor joints-scene / check-scene decide',
                                     per_run_near_cliff_joints=vm, below_min=low, watch=watch)
        if any(not w.get('ok', True) for w in watch if 'result' not in w): raise SystemExit(f"REFUSED: vault margin under {V['min_margin_m']} m at a watched place: {[(w['run'], w['margin_min_m']) for w in watch if 'result' not in w and not w['ok']]}")
        extra['expected_joints_scene'] = expected_joints_scene(segs3, st3, F, K)
    rep = dict(id='enclosure305.v305_3', date=cfg['date'], status='IMPLEMENTED offline (data + lattice + tile-triangle tests). Nothing built or measured in the editor',
               inputs=dict(segments=B.rel(seg_src), segments_sha256=sha_bytes(jraw['seg']), candidates=B.rel(cand_src), candidates_sha256=sha_bytes(jraw['cand']), ops=cfg['ops'], ops_sha256=F.ops_sha,
                           height=cfg['stage_height'], height_sha256=F.height_sha, base_sha256=F.base_sha, config=B.rel(CFG), config_sha256=sha(CFG)),
               outputs=dict(segments_sha256=sha_bytes(seg_bytes), candidates_sha256=sha_bytes(cand_bytes), mask_sha256=sha_bytes(mask_b)),
               removed_runs=dict(count=len(removed), by_plan=sorted(full), partial=[v[0] for v in part.values()], extra={k: (removed[k], gone_note.get(k, '')) for k in sorted(removed) if k not in full}),
               trimmed_runs={rid: dict(arc_of_305_2=[round(a, 2), round(b, 2)], length_305_2=round(info[rid]['ln'].L, 1), length_305_3=per3[rid]['length_m']) for rid, (a, b) in sorted(arcs.items()) if (a, b) != (0.0, info[rid]['ln'].L)},
               gate_ends_open=cfg['gate_ends_open'], cliff_joints=joints_tab,
               mask=dict(masked_quads=int(F.mask.sum()), foreign_cliff_quads=int(F.mask_non.sum()), top_forest_face_quads=int(F.mask_top.sum()), top_forest_open_quads=int(F.lifted_open.sum()),
                         quads_with_a_free_triangle=int(F.free_a.sum() + F.free_b.sum())),
               diff={cfg['from_version']: dict(exp2, candidates=len(cands), candidate_kinds=kinds(cands)), cfg['to_version']: dict(exp3, candidates=len(out_c), candidate_kinds=kinds(out_c)),
                     'scene_now_305.1': scene_now(cfg, K)},
               candidates=cstats, forest_estimate=est, mask_side_effects=side, shell_columns_emulated=cols, expected_editor=expected_lines(cfg, exp3, len(out_c), est, F, side), **extra,
               per_segment={rid: per3[rid] for rid in per3}, joints=[dict(j, p=[round(j['p'][0], 2), round(j['p'][1], 2)]) for j in J3])
    wr.put(o['segments'], seg_bytes); wr.put(o['candidates'], cand_bytes); wr.put(o['mask'], mask_b); wr.put(o['report'], jbytes(rep))
    if extra.get('expected_joints_scene') and 'joints' in o:
        wr.put(o['joints'], jbytes(dict(id='enclosure305.joints', version=cfg['to_version'], height_sha256=F.height_sha, segments_sha256=sha_bytes(seg_bytes), cliff_joints=joints_tab,
                                        expected_joints_scene=extra['expected_joints_scene'], vault_margin=extra['vault_margin'])))
    if not cfg.get('staged'):      # v305.4 is staged: `install` copies the files to the live editor inputs
        wr.put(o['live_segments'], seg_bytes); wr.put(o['live_candidates'], cand_bytes)
    say(f"{cfg['to_version']}: runs {exp2['segments']} -> {exp3['segments']} ({exp2['km']} -> {exp3['km']} km), removed {len(removed)} ({len(full)} by plan + {len(removed) - len(full)}: {', '.join(k for k in sorted(removed) if k not in full)}), "
        f"partial {len(part)}, trimmed into rock {sum(1 for r in joints_tab if r['kind'] == 'rock')}, ends on a cliff top {sum(1 for r in joints_tab if r['kind'] == 'top')}, face crossings {sum(1 for r in joints_tab if r['kind'] == 'bridge')}")
    say(f"  colliders (chunks + joint strips + plugs): {exp2['colliders']} -> {exp3['colliders']} = {exp3['shell_chunks']} + {exp3['joint_strips']} + {exp3['joint_plugs']} (joints arc/cross/flush {exp3['joint_kinds']}), open ends {exp3['open_ends']}")
    say(f"  candidates {len(cands)} -> {len(out_c)}: removed run {cstats['dropped_removed_run']}, removed arc {cstats['dropped_removed_arc']}, cliff mask {cstats['dropped_cliff_mask']}, re-seated {reseat}")
    if rules: say('  v305.4 candidate rules: ' + json.dumps(rules, ensure_ascii=False) + ' | named: ' + json.dumps(named, ensure_ascii=False))
    if extra.get('vault_margin'): say('  vault margin (watch): ' + json.dumps([(w['run'], w.get('margin_min_m'), w.get('ok', w.get('result'))) for w in extra['vault_margin']['watch']]) + f" | runs under {cfg['vault']['min_margin_m']} m near a cliff joint: {extra['vault_margin']['below_min']}"
                                  + f" | joints-scene forecast: open sides {extra['expected_joints_scene']['open_sides']}, runs {extra['expected_joints_scene']['runs']}")
    say(f"  mask quads {int(F.mask.sum())} (foreign cliff {int(F.mask_non.sum())}, top-forest faces {int(F.mask_top.sum())}); forest estimate kept ~{est['expected_kept']} (sheet now {est['sheet_now_placements']}: {est['fate_of_the_placements_in_the_sheet_now']})")
    say(f"  check-scene: {exp3['probe_points']} probe points, {len(side['probe_points_no_ground_twice'])} start inside the mask twice -> {side['probe_runs_inconclusive_expected']} INCONCLUSIVE runs expected; audit (b): {side['cover_samples_inside_rock_total']} cover samples inside rock {side['cover_samples_inside_rock']}")
    say(f"  shell columns (terrain-only emulation): {cols['totals']}; runs with missing ground or a column > {K['span_flag_m']:g} m: " + ', '.join(f"{k} span {v.get('span_max_m')} m at {v.get('span_max_at')} missing {v['missing_samples']} borrowed {v['borrowed_stations']}" for k, v in cols['runs'].items()))
    for r in joints_tab:
        if r['kind'] == 'bridge': say(f"  joint {r['run']} bridges a {r['cliff']} face at {r['at']} (arc {r['arc_m']}), foot {r['foot_y']} -> top {r['top_y']}")
        else: say(f"  joint {r['run']} end {r['end']} -> {r['cliff']} [{r['kind']}] line end {r['line_end']} shell end {r['shell_end']}" + (f" stub {r['stub_m']} m, rock over end cap {r['cover_m']} m above foot (shell {K['above_m']} m)" if r['kind'] == 'rock' else ' (cliff top)'))
    gone = [by2[r]['points'] for r in removed]
    for rid, (a_, b_) in arcs.items():
        ln = info[rid]['ln']
        if a_ > 1e-6: gone.append(ln.sub(0.0, a_))
        if b_ < ln.L - 1e-6: gone.append(ln.sub(b_, ln.L))
    return dict(F=F, segs3=segs3, segs2=segs2, st3=st3, J3=J3, joints_tab=joints_tab, removed=removed, part=part, arcs=arcs, rep=rep, K=K, gone=gone)


def scene_now(cfg, K):
    """the build the three scenes carry today: Out/build-<scene>.txt (2026-09-30, segments 305.1) + the frozen 305.1 file."""
    p = ENC / 'Seal308/backup/segments305.v305_1.json'
    if not p.exists(): return None
    d = json.loads(p.read_text(encoding='utf-8')); e, _, _, _ = predict(d['segments'], K)
    led = ENC / 'Out/Before/ledgers_305_1/build-W_Demo_Main.txt'                    # copy of the 2026-09-30 ledger (build-scene overwrites Out/build-*.txt)
    if not led.exists(): led = ENC / 'Out/build-W_Demo_Main.txt'
    m = re.search(r'shell chunks=(\d+) \+ joint strips=(\d+) \+ joint plugs=(\d+)', led.read_text(encoding='utf-8')) if led.exists() else None
    k = re.search(r'candidates (\d+) kept (\d+)', led.read_text(encoding='utf-8')) if led.exists() else None
    return dict(e, ledger_colliders=[int(x) for x in m.groups()] if m else None, ledger_candidates_kept=[int(x) for x in k.groups()] if k else None,
                note='predicted from Seal308/backup/segments305.v305_1.json with the same port; ledger_* = what the 2026-09-30 build wrote (the port must reproduce it)')


def sheet_trees():
    """XZ of every Tree placement of the sheets build-scene indexes (all but Forest305): the 3.5 m top-up rule."""
    pts = []
    for name, rel in B.SHEETS.items():
        if name == 'Forest305': continue
        cat = {}; sec = None; pid = None; cur = None
        for line in open(A / rel, encoding='utf-8'):
            if line.startswith('  ') and not line.startswith('   ') and line.rstrip().endswith(':') and not line.startswith('  -'): sec = line.strip()[:-1]; continue
            if sec == 'Prototypes':
                if line.startswith('  - Id: '): pid = line[8:].strip()
                elif line.startswith('    Category: ') and pid is not None: cat[pid] = int(line.split(':')[1])
            elif sec == 'FixedPlacements':
                if line.startswith('    PrototypeId: '): cur = line[17:].strip()
                elif line.startswith('    Position: ') and cur is not None:
                    m = re.search(r'x: ([-0-9.eE+]+), y: [-0-9.eE+]+, z: ([-0-9.eE+]+)', line)
                    if m and cat.get(cur) == 0: pts.append((float(m.group(1)), float(m.group(2))))
    return np.array(pts, float).reshape(-1, 2)


def forest_estimate(cfg, cands3, kept_ids, arcs, info, fate):
    """how many placements build-scene should keep. Exact part: candidates the 2026-09-30 build kept (ids in the project's
    Forest305.asset). Estimated part: the seal-run candidates never built, through the 3.5 m existing-tree rule only."""
    p = ENC / cfg['forest_reference_sheet']                            # byte copy of the sheet built from 305.1 (the editor rewrites the project's sheet)
    if not p.exists(): p = A / B.SHEETS['Forest305']
    if not p.exists(): return dict(expected_kept=None, note='Forest305.asset not found')
    ids = set(); fills = {}
    for line in open(p, encoding='utf-8'):
        if line.startswith('  - Id: '):
            i = line[8:].strip(); ids.add(i)
            if i.startswith('enclosure305_fill_'): k = i[len('enclosure305_fill_'):].rsplit('_', 1)[0]; fills[k] = fills.get(k, 0) + 1
    built = {c['ClusterId'] for c in cands3 if c['Id'] in ids}
    prev_runs = {('E305_' + i.split('_')[1]) for i in ids if not i.startswith('enclosure305_fill_')}
    old = [c for c in cands3 if c['ClusterId'] in prev_runs]; new = [c for c in cands3 if c['ClusterId'] not in prev_runs]
    kept_old = sum(1 for c in old if c['Id'] in ids)
    T = sheet_trees(); grid = {}
    for x, z in T: grid.setdefault((int(x // 8), int(z // 8)), []).append((x, z))

    FE = cfg['forest_estimate']

    def near_tree(x, z, r=cfg['shell_copy']['tree_clear_m']):
        for gx in range(int((x - r) // 8), int((x + r) // 8) + 1):
            for gz in range(int((z - r) // 8), int((z + r) // 8) + 1):
                for tx, tz in grid.get((gx, gz), ()):
                    if (tx - x) ** 2 + (tz - z) ** 2 <= r * r: return True
        return False
    sim_old = sum(1 for c in old if not near_tree(c['Position'][0], c['Position'][2])); kept_new = sum(1 for c in new if not near_tree(c['Position'][0], c['Position'][2]))
    fill_kept = sum(n for k, n in fills.items() if 'E305_' + k in kept_ids and arcs.get('E305_' + k, (0, 0)) == (0.0, info['E305_' + k]['ln'].L))
    fill_all = sum(fills.values()); now_ids = [i for i in ids if i.startswith('enclosure305_') and not i.startswith('enclosure305_fill_')]
    f = [fate.get(i, -1) for i in now_ids]; today = dict(placements=len(now_ids) + fill_all, of_removed_runs=f.count(1), of_removed_arcs=f.count(2), in_the_cliff_mask=f.count(3), stay=f.count(0), not_in_the_candidate_file=f.count(-1), cover_fill=fill_all,
                                                        cover_fill_of_removed_or_trimmed_runs=fill_all - fill_kept)
    return dict(reference_sheet=B.rel(p), reference_sheet_sha256=sha(p), sheet_now_placements=len(now_ids) + fill_all, fate_of_the_placements_in_the_sheet_now=today, sheet_now_cover_fill=fill_all, candidates_of_runs_built_before=len(old), of_them_kept_on_2026_09_30=kept_old,
                tree_rule_simulation_on_them=sim_old, candidates_of_runs_never_built=len(new), never_built_runs=sorted({c['ClusterId'] for c in new}), tree_rule_simulation_on_never_built=kept_new,
                cover_fill_of_untouched_runs_on_2026_09_30=fill_kept, existing_trees_indexed=int(len(T)),
                expected_kept=kept_old + kept_new + fill_kept, expected_kept_range=[kept_old + int(kept_new * FE['low_new']) + int(fill_kept * FE['low_fill']), kept_old + kept_new + fill_all + FE['high_extra']],
                note='expected_kept = exact (runs built before, same filters) + simulated 3.5 m tree rule (runs never built; content / preserved-area / no-ground drops not simulated) + cover fill of the untouched runs. '
                     'Cover fill on the never-built seal runs and at the new ends is not predicted: the range allows for it')


def expected_lines(cfg, e, ncand, est, F, side):
    k = e['joint_kinds']
    return dict(status_line=f"segments305.json {cfg['to_version']}: N6 {e['segments']} segments, {e['km']:.2f} km (other kits skipped: 0), open ends {e['open_ends']}, ... | forest305_candidates.json {cfg['to_version']}: {ncand} candidates | cliff mask {int(F.mask.sum())} quads, requires height sha {F.height_sha[:12]}",
                build_scene=f"built Enclosure305 in <scene>: {e['segments']} N6 segments, {e['shell_chunks']} shell chunks + {e['joint_strips']} joint strips ({e['joints']} joints), 0 carve boxes, forest ~{est.get('expected_kept')}/{ncand} placements; block height PASS",
                build_ledger=f"shell chunks={e['shell_chunks']} + joint strips={e['joint_strips']} + joint plugs={e['joint_plugs']}; joints={e['joints']}: arc {k['arc']}, cross {k['cross']}, flush {k['flush']}; open ends={e['open_ends']}",
                check_scene=f"segment probe points={e['probe_points']} runs={e['probe_runs']} (INCONCLUSIVE expected {side['probe_runs_inconclusive_expected']}: starts inside the cliff mask at the rock ends); joints={e['joints']} runs={e['joint_probe_runs']}; "
                            f"open ends={e['open_ends']} runs={e['open_end_probe_runs']} ({cfg.get('open_end_note', 'the two gate ends LEAK by design until stage 1b')})",
                audit_scene=f"(a) colliders under Enclosure305={e['colliders']}, renderer-less *_col shells={e['colliders']}, other 0; (b) cover samples {e['cover_samples']}, at least {side['cover_samples_inside_rock_total']} uncovered inside rock "
                            f"({len(side['cover_samples_inside_rock'])} runs); (d) NavMesh station samples <= {e['nav_samples_max']}")


# ------------------------------------------------------------------ closure
def fine_joint(F, cfg, st3, J3, run, kc, step, K, hole=False):
    """0.25 m 4-neighbour flood on the tile triangles around one forest-cliff contact. kc = the last shell station of `run`
    on open ground before the rock, step = +1 / -1 = station direction toward the rock. Seeds on the player side, 5 m back;
    sealed = no walkable cell on the blocked side of the last 20 m of the shell is reached and no lifted ground is reached.
    hole = the same flood with the 12 m of shell before the rock taken out (the test must then leak, or it proves nothing)."""
    T = cfg['joint_test']; c = T['cell_m']; Wn = T['window_m']; S = st3[run]; n = len(S['s']); back = int(round(T['seed_back_m'] / K['step_m'])); ks = min(n - 1, max(0, kc - step * back))
    ctr = S['q'][kc]; m = int(2 * Wn / c); xs = ctr[0] - Wn + (np.arange(m) + .5) * c; zs = ctr[1] - Wn + (np.arange(m) + .5) * c
    X, Z = np.meshgrid(xs, zs); ok = F.slope(X, Z) < T['walk_deg']

    def block(P, r):
        for a, b in zip(P[:-1], P[1:]):
            if max(a[0], b[0]) < xs[0] - r or min(a[0], b[0]) > xs[-1] + r or max(a[1], b[1]) < zs[0] - r or min(a[1], b[1]) > zs[-1] + r: continue
            d = b - a; L2 = float(d @ d) or 1e-12
            j0 = max(0, int((min(a[0], b[0]) - r - xs[0]) / c)); j1 = min(m, int((max(a[0], b[0]) + r - xs[0]) / c) + 2); i0 = max(0, int((min(a[1], b[1]) - r - zs[0]) / c)); i1 = min(m, int((max(a[1], b[1]) + r - zs[0]) / c) + 2)
            if j1 <= j0 or i1 <= i0: continue
            xx = X[i0:i1, j0:j1]; zz = Z[i0:i1, j0:j1]; t = np.clip(((xx - a[0]) * d[0] + (zz - a[1]) * d[1]) / L2, 0, 1)
            ok[i0:i1, j0:j1] &= np.hypot(xx - (a[0] + d[0] * t), zz - (a[1] + d[1] * t)) > r
    gap = int(round(T['hole_m'] / K['step_m']))
    for rid, S2_ in st3.items():
        q = S2_['q']
        if rid == run and hole:                                          # stations kc - step * gap .. kc + step * 2 removed
            lo, hi = sorted((kc - step * gap, kc + step * 2)); parts = [q[:max(0, lo)], q[min(n, hi + 1):]]
        else: parts = [q]
        for part_ in parts:
            if len(part_) > 1: block(part_, T['shell_block_r_m'])
    for j in J3:
        r = (K['plug_cross_r_m'] if j['kind'] == 'cross' else K['plug_r_m']) + T['plug_block_extra_m']; ok &= np.hypot(X - j['p'][0], Z - j['p'][1]) > r
    cell = lambda p: (int(np.clip((p[1] - zs[0]) / c, 0, m - 1)), int(np.clip((p[0] - xs[0]) / c, 0, m - 1)))
    seeds = [cell(S['q'][ks] - S['nb'][ks] * d) for d in (T['seed_side_m'], T['seed_side_m'] + 1.0, T['seed_side_m'] + 2.0)]; seeds = [p for p in seeds if ok[p]]
    R = np.zeros_like(ok); dq = deque(seeds)
    for p in seeds: R[p] = True
    while dq:
        i, j = dq.popleft()
        for a, b in ((i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1)):
            if 0 <= a < m and 0 <= b < m and ok[a, b] and not R[a, b]: R[a, b] = True; dq.append((a, b))
    far = int(round(T['far_m'] / K['step_m'])); probes = []
    for i in range(kc, kc - step * (far + 1), -step):
        if not 0 <= i < n: break
        for d in T['target_offsets_m']: probes.append(cell(S['q'][i] + S['nb'][i] * d))
    probes = sorted(set(p for p in probes if ok[p])); hit = sum(1 for p in probes if R[p])
    lifted = (B.sample_tri(F.new, X, Z) - B.sample_tri(F.base, X, Z)) > T['lifted_m']
    return dict(seed=[round(float(S['q'][ks][0] - S['nb'][ks][0] * T['seed_side_m']), 1), round(float(S['q'][ks][1] - S['nb'][ks][1] * T['seed_side_m']), 1)], seeds_walkable=len(seeds),
                blocked_side_cells=len(probes), blocked_side_cells_reached=hit, lifted_cells_reached=int((R & lifted).sum()), flood_m2=round(float(R.sum()) * c * c, 1))


def closure(cfg, wr, built=None):
    import cliff308_closure as CC
    if built is None: built = build(cfg, Writer(True), quiet=True)
    F, segs3, segs2, st3, J3, tab, K = built['F'], built['segs3'], built['segs2'], built['st3'], built['J3'], built['joints_tab'], built['K']
    CL = cfg['closure']; o = cfg['_o']
    motor = json.loads((ROOT / cfg['motor_model']).read_text(encoding='utf-8')).get('probe', {}).get('jumpNeedsWalkable')
    ctx = CC.Ctx(log=lambda *a: None); new = F.new; sl = B.slope_deg(new); X, Z = ctx.X, ctx.Z
    raw = lambda runs: np.logical_or.reduce([B.stamp(r['points'], CL['band_m'], X, Z) for r in runs])
    walls = np.zeros(new.shape, bool)
    for _, P in B.wall_lines(): walls |= B.stamp(P, B.SEAL_BAND, X, Z)
    struct = ctx.structure; gate_only = struct & ~walls; keep3 = raw(segs3)
    # the Seal308 closed solid (305.2 runs + walls + closed doors + gate footprint) minus the bands of what 305.3 drops; cells of a
    # barrier that stays (kept runs, walls, gate footprint) are never removed - the method of cliff308_closure.replaced_mask
    drop = np.zeros(new.shape, bool)
    for P in built['gone']:
        if len(P) >= 2 and B.polyline_len(P) > 0.01: drop |= ctx.band(P, CL['drop_band_m'])
    drop &= ~(keep3 | struct)
    fp = json.loads((B.ENC / 'Out/Seal308/footprint308.json').read_text(encoding='utf-8')); gate_wide = np.zeros(new.shape, bool)
    for P in list(fp.get('wings', [])) + ([fp['panel_closed']] if fp.get('panel_closed') else []): gate_wide |= ctx.band(P, CL['drop_band_m'])
    for bx, bz, br in fp.get('bastions', []): gate_wide |= np.hypot(X - bx, Z - bz) <= br + (CL['drop_band_m'] - CL['band_m'])
    gate_wide &= ~(keep3 | walls)
    solids = {'as_built_planned_gate': ctx.scl & ~drop, 'as_built_scene_no_gate': ctx.scl & ~drop & ~gate_wide}
    wall_src = None
    if CL.get('wall'):      # v305.4: the long wall and the gate of stage 1b stand (planned-gate config) - the scene config is the build WITHOUT them (before Jangseong308 apply)
        wband, gband, wall_src = CC.wall_solid(ctx, F.ops, None); solids['as_built_planned_gate'] = (ctx.scl & ~drop) | wband | gband
        solids['as_built_wall_gate_open'] = (ctx.sop & ~drop) | wband
    out = dict(id='closure305_3', date=cfg['date'], stage=cfg['stage'], segments_version=cfg['to_version'], height_sha256=F.height_sha, ops_sha256=F.ops_sha,
               method='4 m lattice, 4-neighbour flood from the Seal308 new-game node (3364, 2020), walkable < %g deg on the stage height. Solid = the Seal308 closed lattice (305.2 runs as %g m half bands, never over a route corridor; built walls; the four closed capital doors; the gate footprint) minus a %g m band around every run / arc 305.3 drops, never a cell of a barrier that stays. NOT a physics proof' % (CL['walk_deg'], CL['band_m'], CL['drop_band_m']),
               jump_rule=dict(jumpNeedsWalkable=motor, doc='D308-9d: a jump starts only from walkable ground, so a >= 45 deg face is not climbable and the slope lattice is the right model. With the old motor (false) the faces are climbed by jumping (probe 305 of 326) and this proof does not hold'),
               gate_assumption=dict(as_built_planned_gate='the stage-1b gate at A (1868.02, 1750.61) - B (1920, 1740) is counted as a closed barrier (Seal308 footprint cells: wings, end bastions, closed panel) - the cliff plan (closure_p1a.json S0_as_built) assumes the same. It does NOT exist in the scenes',
                                    as_built_scene_no_gate='what the three scenes hold after stage 1a: no gate, no long wall, the 53 m gap A-B open'),
               lattice=dict(dropped_cells=int((ctx.scl & drop).sum()), gate_cells_removed_in_the_scene_config=int((ctx.scl & ~drop & gate_wide).sum()), seal308_solid='Enclosure305/Out/Seal308/closure/_work/solid-closed.npy',
                            seal308_solid_sha256=sha(B.ENC / 'Out/Seal308/closure/_work/solid-closed.npy')))
    reach = {}
    if wall_src: out['wall_band_source'] = wall_src
    for name, solid in solids.items():
        opened = name.endswith('gate_open')
        R = B.flood(ctx.walkable(new, solid, sl), B.SEED_NODE); reach[name] = R; rep = CC.state_report(ctx, R, ctx.rop if opened else ctx.rcl, new, solid, not opened)
        if opened:
            rep['late_items_reached_count'] = None; out[name] = rep
            print(f"  {name:24s} reach {rep['reach_ha']} ha (base {rep['reach_ha_base']}), items lost {len(rep['items_lost'])}, cliff top {rep['cliff_top_reach_ha']} ha")
            continue
        rep['late_items_reached_count'] = len(rep['late_items_reached']); out[name] = rep
        print(f"  {name:24s} reach {rep['reach_ha']} ha (base {rep['reach_ha_base']}), new {rep['new_ha']} lost {rep['lost_ha']}, EA items missed {len(rep['ea_items_missed'])}/{rep['ea_items']}, late reached {len(rep['late_items_reached'])} (newly {len(rep['late_items_newly_reached'])}), world edge {rep['world_edge_reach_m']} m, cliff top {rep['cliff_top_reach_ha']} ha")
    pr = ROOT / cfg['plan_reach']
    if pr.exists():
        P = np.load(pr); R = reach['as_built_planned_gate']
        out['vs_cliff_plan_S0_as_built'] = dict(plan_reach_ha=round(P.sum() * 16 / 1e4, 1), reach_ha=round(R.sum() * 16 / 1e4, 1), only_here_cells=int((R & ~P).sum()), only_plan_cells=int((P & ~R).sum()),
                                                only_here_clusters=B.clusters(R & ~P, 60, 12), only_plan_clusters=B.clusters(P & ~R, 60, 12),
                                                doc='the plan keeps E305_000 / 020 / 133c020 / 055 / 084 / 036 / 026 / 027 / 017 / 085c052 as full 305.2 bands; here they are cut at the rock')
    out['sensitivity'] = CC.sensitivity(ctx, new, sl, {'S0_as_built': solids['as_built_planned_gate']}, log=lambda *a: None)
    for mn in ('node_slope_8n', 'edge_slope_4n', 'edge_slope_8n'):
        e = out['sensitivity'][mn]['S0_as_built']; print(f"  sensitivity {mn:14s} reach {e['reach_ha']} ha (base {out['sensitivity'][mn]['base_reach_ha']}), late newly {len(e['late_items_newly_reached'])}, EA missed {len(e['ea_items_missed'])}, edge {e['world_edge_reach_m']} m, cliff top {e['cliff_top_reach_ha']} ha, ok {e['ok']}")
    # joints: fine flood (tile triangles) + hole test (4 m lattice)
    base_solid = solids['as_built_planned_gate']; R0 = reach['as_built_planned_gate']; rows = []
    near = lambda R, it: CC.reached(R, it['x'], it['z'], min(it.get('r', 12), 30))
    for r in tab:
        row = dict(run=r['run'], end=r['end'], cliff=r['cliff'], kind=r['kind'], shell_end=r['shell_end'], at=r.get('at', r['shell_end']))
        if r['kind'] in ('rock', 'bridge'):
            a = fine_joint(F, cfg, st3, J3, r['run'], r['kc'], r['step'], K); c = fine_joint(F, cfg, st3, J3, r['run'], r['kc'], r['step'], K, hole=True)
            row['fine'] = dict(sealed=bool(a['seeds_walkable'] > 0 and a['blocked_side_cells'] > 0 and a['blocked_side_cells_reached'] == 0 and a['lifted_cells_reached'] == 0), detail=a,
                               with_a_12m_hole_blocked_side_cells_reached=c['blocked_side_cells_reached'], test_is_sensitive=bool(c['blocked_side_cells_reached'] > 0))
        others = raw([s_ for s_ in segs3 if s_['id'] != r['run']]) | struct
        hole = ctx.band(next(s_ for s_ in segs3 if s_['id'] == r['run'])['points'], CL['drop_band_m']) & (np.hypot(X - row['at'][0], Z - row['at'][1]) <= CL['hole_r_m']) & ~others
        Rh = B.flood(ctx.walkable(new, base_solid & ~hole, sl), B.SEED_NODE); gain = round(float((Rh & ~R0).sum()) * 16 / 1e4, 2)
        late = [[it['kind'], it['id']] for it in ctx.items if not it['ea'] and near(Rh, it) and not near(R0, it)]
        row['hole_test'] = dict(reach_gain_ha=gain, late_items_newly_reached=len(late), world_edge_reach_m=CC.edge_m(Rh), cliff_top_reach_ha=round(float(((F.diff > CL['cliff_top_m']) & Rh).sum() * 16 / 1e4), 2),
                                closure_depends_on_this_joint=bool(gain >= CL['depends_min_ha'] or late or CC.edge_m(Rh) > 0))
        rows.append(row)
        print(f"  joint {r['run']} end {r['end']} @ {r['cliff']}: " + (f"fine {'SEALED' if row['fine']['sealed'] else 'LEAK'} ({row['fine']['detail']['blocked_side_cells_reached']}/{row['fine']['detail']['blocked_side_cells']} blocked-side cells; 12 m hole -> {row['fine']['with_a_12m_hole_blocked_side_cells_reached']}), " if 'fine' in row else '') +
              f"hole +{gain} ha, late {len(late)}, edge {row['hole_test']['world_edge_reach_m']} m -> depends {row['hole_test']['closure_depends_on_this_joint']}")
    out['forest_cliff_joints'] = rows
    # other joints the closure rests on (no forest): cliff-wall and cliff-cliff, listed from the ops / eval
    E = json.loads((ROOT / cfg['eval']).read_text(encoding='utf-8'))
    out['other_joints'] = dict(cliff_wall=E.get('structures_under_raise', {}).get('walls_buried', []),
                               cliff_cliff={k: v['face_ac_8m_window'].get('joint_runs', []) for k, v in E.get('stretches', {}).items() if v['face_ac_8m_window'].get('joint_runs')},
                               doc='cliff_wall: the capital east wall runs into P1_R1w (D308-9d: left as is). cliff_cliff: one cliff mass stepping onto another (P1_M1 / P1_R1e / P1_R1w)')
    ok = all(('fine' not in r or r['fine']['sealed']) for r in rows); a = out['as_built_planned_gate']
    if 'as_built_wall_gate_open' in out: out['gate_open'] = dict(cliff_top_reach_ha=out['as_built_wall_gate_open']['cliff_top_reach_ha'], items_lost=out['as_built_wall_gate_open']['items_lost'])
    out['verdict'] = dict(as_built_planned_gate=dict(ea_missed=len(a['ea_items_missed']), late_newly_reached=len(a['late_items_newly_reached']), world_edge_reach_m=a['world_edge_reach_m'], cliff_top_reach_ha=a['cliff_top_reach_ha'],
                                                     ea_reach_delta_ha=round(a['reach_ha'] - a['reach_ha_base'], 2), ok=bool(not a['ea_items_missed'] and not a['late_items_newly_reached'] and a['world_edge_reach_m'] == 0 and a['cliff_top_reach_ha'] == 0)),
                          sensitivity_ok=out['sensitivity']['ok'], fine_joints_sealed=ok,
                          scene_no_gate=dict(late_items_reached=len(out['as_built_scene_no_gate']['late_items_reached']), reach_ha=out['as_built_scene_no_gate']['reach_ha'], world_edge_reach_m=out['as_built_scene_no_gate']['world_edge_reach_m'],
                                             doc='open by design until stage 1b: the gap at the gate'))
    wr.put(o['closure'], jbytes(out))
    try:
        from PIL import Image
        import io
        img = np.zeros((HH, W, 3), np.uint8); img[:] = (236, 232, 220); img[F.diff > CL['cliff_top_m']] = (150, 140, 128); img[ctx.rcl & ~R0] = (40, 90, 220); img[R0] = (205, 222, 196); img[R0 & ~ctx.rcl] = (230, 60, 50)
        img[reach['as_built_scene_no_gate'] & ~R0] = (240, 200, 120); img[keep3 & base_solid] = (20, 60, 20); img[ctx.scl & struct] = (0, 0, 0)
        buf = io.BytesIO(); Image.fromarray(img[::-1][int(HH - 1 - 4200 / 4):int(HH - 1 - 1300 / 4), int(1000 / 4):]).save(buf, 'PNG'); wr.put(o['closure_png'], buf.getvalue())
    except Exception as ex:                                              # the picture is optional
        print('  (no picture:', ex, ')')
    print('  verdict', json.dumps(out['verdict'], ensure_ascii=False))
    return out


# ------------------------------------------------------------------ status / revert
def status(cfg):
    o = cfg['_o']
    for k in ('live_segments', 'segments_frozen', 'segments', 'live_candidates', 'candidates_frozen', 'candidates', 'mask', 'live_mask', 'report', 'closure', 'joints'):
        if k not in o: continue
        p = o[k]
        if not p.exists(): print(f'{k:18s} {B.rel(p)}: missing'); continue
        v = ''
        if p.suffix == '.json' and p.stat().st_size < 4e7:
            try: v = ' version ' + str(json.loads(p.read_text(encoding='utf-8')).get('version', '-'))
            except Exception: v = ' (unreadable)'
        print(f'{k:18s} {B.rel(p)}:{v} sha {sha(p)[:12]} ({p.stat().st_size} bytes)')
    hp = ROOT / cfg['stage_height']; print(f"stage height       {cfg['stage_height']}: sha {sha(hp)[:12]}; ops {cfg['ops']}: sha {sha(ROOT / cfg['ops'])[:12]}")
    live = json.loads(o['live_segments'].read_text(encoding='utf-8'))
    print(f"editor input now   segments305.json {live.get('version')}" + (f", needs height {live['requires_height_sha256'][:12]}, mask {live['cliff_mask']['file']}" if live.get('requires_height_sha256') else ' (no cliff mask: base height build)'))


def revert(cfg, wr, to):
    o = cfg['_o']
    if to == '305.3':
        if not cfg.get('install'): raise SystemExit('REFUSED: --to 305.3 needs the v305.4 config (--cfg cliff305_4.cfg.json)')
        return revert_to_305_3(cfg, wr)
    src = {'305.2': (o['segments_frozen'], o['candidates_frozen']), '305.1': (ENC / 'Seal308/backup/segments305.v305_1.json', ENC / 'Seal308/backup/forest305_candidates.v305_1.json')}.get(to)
    if src is None: raise SystemExit('REFUSED: --to 305.2 or 305.1')
    for p in src:
        if not p.exists(): raise SystemExit(f'REFUSED: {B.rel(p)} is missing; nothing changed')
    for p, v in zip(src, (to, to)):
        if json.loads(p.read_text(encoding='utf-8')).get('version') != v: raise SystemExit(f'REFUSED: {B.rel(p)} is not version {v}; nothing changed')
    wr.put(o['live_segments'], src[0].read_bytes()); wr.put(o['live_candidates'], src[1].read_bytes())
    print(f"editor inputs -> {to} (bytes of {B.rel(src[0])} and {B.rel(src[1])}). The versioned {cfg['to_version']} files and the mask stay on disk. "
          f"The scenes change only when Enclosure305 build-scene runs again (after CliffBoundary308 revert:<scene> put the base tiles back: the staged build-scene refuses a {to} build on the stage height)")


# ------------------------------------------------------------------ v305.4 (cliff stage 1b): candidate rules, vault margin, staged install
def _unity_euler(n, yaw_deg):
    """Euler (x, y, z) of Quaternion.FromToRotation(up, n) * Quaternion.Euler(0, yaw, 0) in Unity's order (R = Ry Rx Rz)."""
    n = np.asarray(n, float); n /= np.linalg.norm(n) + 1e-12; up = np.array([0.0, 1.0, 0.0]); ax = np.cross(up, n); s = float(np.linalg.norm(ax)); c = float(up @ n)
    if s < 1e-9: A_ = np.eye(3)
    else:
        ax /= s; K_ = np.array([[0, -ax[2], ax[1]], [ax[2], 0, -ax[0]], [-ax[1], ax[0], 0]]); A_ = np.eye(3) + K_ * s + K_ @ K_ * (1 - c)
    y = math.radians(yaw_deg); Ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]]); R = A_ @ Ry
    ex = math.asin(max(-1.0, min(1.0, -R[1, 2]))); ey = math.atan2(R[0, 2], R[2, 2]); ez = math.atan2(R[1, 0], R[1, 1])
    return [round(math.degrees(ex) % 360, 2), round(math.degrees(ey) % 360, 2), round(math.degrees(ez) % 360, 2)]


def tri_normal(F, x, z):
    """unit normal (x, y, z) of the tile triangle under (x, z) on the stage height."""
    i, j = F.quad(x, z); i = int(i); j = int(j); low = (x / CELL - j) + (z / CELL - i) <= 1; h = F.new
    if low: gx = (h[i, j + 1] - h[i, j]) / CELL; gz = (h[i + 1, j] - h[i, j]) / CELL
    else: gx = (h[i + 1, j + 1] - h[i + 1, j]) / CELL; gz = (h[i + 1, j + 1] - h[i, j + 1]) / CELL
    n = np.array([-gx, 1.0, -gz]); return n / np.linalg.norm(n)


def sheet_rocks(names, category):
    """[(x, z, radius, id, sheet)] of the rock placements of the named sheets (prototype Radius x placement Scale)."""
    out = []
    for name in names:
        rad = {}; sec = None; pid = None; cur = None; cid = None; pos = None
        for line in open(A / B.SHEETS[name], encoding='utf-8'):
            if line.startswith('  ') and not line.startswith('   ') and line.rstrip().endswith(':') and not line.startswith('  -'): sec = line.strip()[:-1]; continue
            if sec == 'Prototypes':
                if line.startswith('  - Id: '): pid = line[8:].strip(); rad[pid] = [None, 0.0]
                elif line.startswith('    Category: ') and pid is not None: rad[pid][0] = int(line.split(':')[1])
                elif line.startswith('    Radius: ') and pid is not None: rad[pid][1] = float(line.split(':')[1])
            elif sec == 'FixedPlacements':
                if line.startswith('  - Id: '): cid = line[8:].strip(); cur = None; pos = None
                elif line.startswith('    PrototypeId: '): cur = line[17:].strip()
                elif line.startswith('    Position: '):
                    m = re.search(r'x: ([-0-9.eE+]+), y: [-0-9.eE+]+, z: ([-0-9.eE+]+)', line); pos = (float(m.group(1)), float(m.group(2))) if m else None
                elif line.startswith('    Scale: ') and cur is not None and pos is not None and rad.get(cur, [None])[0] == category:
                    out.append((pos[0], pos[1], rad[cur][1] * float(line.split(':')[1]), cid, name))
    return out


def near_any(xz, pts, pad):
    """for each row of xz: index of a (x, z, r, ...) disc of `pts` it lies in (r + pad), or -1."""
    hit = -np.ones(len(xz), int)
    if not pts: return hit
    P = np.array([(p[0], p[1], p[2]) for p in pts], float); grid = {}
    for k, (x, z, r) in enumerate(P): grid.setdefault((int(x // 16), int(z // 16)), []).append(k)
    for q, (x, z) in enumerate(xz):
        for gx in (int(x // 16) - 1, int(x // 16), int(x // 16) + 1):
            for gz in (int(z // 16) - 1, int(z // 16), int(z // 16) + 1):
                for k in grid.get((gx, gz), ()):
                    if (P[k, 0] - x) ** 2 + (P[k, 1] - z) ** 2 <= (P[k, 2] + pad) ** 2: hit[q] = k
    return hit


def dist_to_lines(xz, lines):
    d = np.full(len(xz), np.inf)
    for L in lines:
        ln = Line(L)
        if ln.L <= 0: continue
        d = np.minimum(d, ln.project(xz)[1])
    return d


def shell_columns(S, F, K):
    """per station of one run: (top, player ground) the editor's Columns305 gives on the stage TERRAIN (emulate_columns per station)."""
    n = len(S['s']); half = K['thick_m'] * .5; lo = np.full(n, np.inf); hi = np.full(n, -np.inf); pl = np.full(n, -np.inf)
    for k in range(n):
        q = S['q'][k]; nb = S['nb'][k]
        for a, pt in ((-4, q - nb * K['player_far_m'][0]), (-3, q - nb * K['player_far_m'][1]), (-2, q - nb * (half + K['face_pad_m'])), (-1, q - nb * K['across_m']), (0, q), (1, q + nb * K['across_m'])):
            x, z = float(pt[0]), float(pt[1])
            if not (0 <= x <= 4000 and 0 <= z <= 6000) or F.masked_column(x, z) or float(F.slope(x, z)) >= K['ground_slope_deg']: continue
            y = float(F.y(x, z)); hi[k] = max(hi[k], y)
            if a <= -1: pl[k] = max(pl[k], y)
            if a >= -1: lo[k] = min(lo[k], y)
    ok = np.nonzero(~np.isinf(lo))[0]
    if len(ok):
        for k in np.nonzero(np.isinf(lo))[0]:
            best = min(ok, key=lambda i: (abs(int(i) - int(k)), 0 if i < k else 1)); lo[k] = lo[best]; hi[k] = max(hi[k], hi[best]); pl[k] = max(pl[k], pl[best])
    return hi + K['above_m'], pl


def vault_margins(S, F, K, V, stations=None):
    """the 'vault' model of the 305.3 review as a table: from every walkable stance on the player side within V.stance_r_m of a shell
    station, a 국 lift (guk_m) then a jump (jump_m) at run speed toward the shell; the feet height where the path crosses the shell
    centre line (flight time = horizontal distance / speed, the fall during the flight included) against the shell top there.
    margin = shell top - best feet height (positive = the shell is higher). -> per station (margin, stance xz, crossing station).
    Stances on the top of a top-forest segment without shells are left out: stepping off that rim is closed by the lips (lips308)."""
    top, _ = shell_columns(S, F, K); n = len(S['s']); g = float(V['gravity']); v0 = math.sqrt(2 * g * V['jump_m']); out = {}
    r = float(V['stance_r_m']); c = float(V['cell_m']); off = np.arange(-r, r + 1e-6, c); ox, oz = np.meshgrid(off, off); keep = np.hypot(ox, oz) <= r; ox = ox[keep]; oz = oz[keep]
    clear = K['thick_m'] * .5 + V['capsule_r_m']
    for k in (stations if stations is not None else range(n)):
        q = S['q'][k]; nb = S['nb'][k]; x = q[0] + ox; z = q[1] + oz
        side = (x - q[0]) * nb[0] + (z - q[1]) * nb[1]                   # < 0 = player side of the local shell line
        ok = (side <= -clear) & (F.slope(x, z) < V['walk_deg']) & ~F.masked(x, z)
        if getattr(F, 'shells_off_top', False): ok &= ~F.masked(x, z, 'top_quads')      # a stance on a shell-less cliff top is the rim's matter (lips308, fall rule), not the forest shell's
        if not ok.any(): out[k] = None; continue
        x = x[ok]; z = z[ok]; ys = F.y(x, z); best = (-np.inf, None, None)
        for kc in range(max(0, k - V['cross_stations']), min(n, k + V['cross_stations'] + 1)):
            d = np.hypot(S['q'][kc][0] - x, S['q'][kc][1] - z); t = d / V['speed']; feet = ys + V['guk_m'] + v0 * t - .5 * g * t * t - top[kc]
            i = int(feet.argmax())
            if feet[i] > best[0]: best = (float(feet[i]), (float(x[i]), float(z[i]), float(ys[i])), kc)
        out[k] = dict(margin_m=round(-best[0], 2), margin_with_step_m=round(-best[0] - V['step_m'], 2), stance_xz=[round(best[1][0], 1), round(best[1][1], 1)], stance_y=round(best[1][2], 2), crossing_station=int(best[2]),
                      shell_top_y=round(float(top[best[2]]), 2), station_xz=[round(float(q[0]), 1), round(float(q[1]), 1)])
    return out


def expected_joints_scene(segs3, st3, F, K, back=4):
    """what joints-scene (CliffJoints305) will run: every stretch of shell stations in the cliff mask, each open side of it:
    up to `back` straight runs + one aimed run, three variants each."""
    sides = []; runs = 0
    for g in segs3:
        S = st3[g['id']]; n = len(S['s']); m = np.asarray(F.masked(S['q'][:, 0], S['q'][:, 1])); a = 0
        while a < n:
            if not m[a]: a += 1; continue
            b = a
            while b + 1 < n and m[b + 1]: b += 1
            kind = 'end 1 in rock' if b == n - 1 else ('end 0 in rock' if a == 0 else 'face crossing')
            for kc, step in ((a - 1, 1), (b + 1, -1)):
                if kc < 0 or kc >= n: continue
                straight = 0
                for bk in range(back):
                    k = kc - step * bk
                    if k < 0 or k >= n or m[k]: break
                    straight += 1
                sides.append(dict(run=g['id'], stations=[int(a), int(b)], kind=kind, from_station=int(kc), at=[round(float(S['q'][kc][0]), 1), round(float(S['q'][kc][1]), 1)], straight_runs=straight, runs=(straight + 1) * 3)); runs += (straight + 1) * 3
            a = b + 1
    return dict(open_sides=len(sides), runs=runs, per_variant=runs // 3, sides=sides, expect='FAIL 0. joints <scene>: PASS or INCONCLUSIVE; open sides %d, runs %d; walk p/0/i, jump+step p/0/i, guk+jump+step p/0/i' % (len(sides), runs))


def apply_reroutes(cfg, segs2):
    """cfg.reroute: {run: {from_xz, to_xz, points}} - the 305.2 polyline between the vertices nearest from_xz and to_xz is replaced by `points`."""
    out = []
    for s in segs2:
        rr = (cfg.get('reroute') or {}).get(s['id'])
        if not rr: out.append(s); continue
        P = [list(p) for p in s['points']]; near = lambda q: int(np.argmin([math.hypot(p[0] - q[0], p[1] - q[1]) for p in P])); a, b = near(rr['from_xz']), near(rr['to_xz'])
        if not a < b: raise SystemExit(f"REFUSED: reroute of {s['id']}: from / to vertices {a}, {b} are not in line order")
        if math.hypot(P[a][0] - rr['from_xz'][0], P[a][1] - rr['from_xz'][1]) > 0.05 or math.hypot(P[b][0] - rr['to_xz'][0], P[b][1] - rr['to_xz'][1]) > 0.05:
            raise SystemExit(f"REFUSED: reroute of {s['id']}: from_xz / to_xz are not vertices of the {cfg['from_version']} line")
        t = json.loads(json.dumps(s)); t['points'] = P[:a + 1] + [list(p) for p in rr['points']] + P[b:]; t['length_m'] = round(Line(t['points']).L, 1)
        t['notes'] = list(t.get('notes', [])) + [f"{cfg['to_version']}: vertices {a + 1}..{b - 1} of the {cfg['from_version']} line replaced by {len(rr['points'])} point(s) ({rr['why']})"]
        out.append(t)
    return out


def install(cfg, wr, variant):
    """staged 305.4 files -> the editor's live inputs. Sha preconditions on what is live now; byte copies of the live files first."""
    o = cfg['_o']; I = cfg['install']; exp = I['expect_live']
    for k in ('segments', 'candidates', 'mask'):
        if not o[k].exists(): raise SystemExit(f"REFUSED: staged {B.rel(o[k])} is missing (python Tools/Art/enclosure305_cliff308.py --cfg {CFG.name} --variant {variant} all); nothing changed")
    seg = json.loads(o['segments'].read_text(encoding='utf-8'))
    if seg.get('version') != cfg['to_version'] or head_version(o['candidates']) != cfg['to_version']: raise SystemExit('REFUSED: the staged pair is not version ' + cfg['to_version'] + '; nothing changed')
    if seg['cliff_mask']['sha256'] != sha(o['mask']) or seg['cliff_mask']['file'] != B.rel(o['live_mask']): raise SystemExit('REFUSED: the staged mask is not the one the staged segment file names; nothing changed')
    hp = ROOT / cfg['stage_height']
    if seg['requires_height_sha256'] != sha(hp): raise SystemExit(f"REFUSED: the staged files are cut for height {seg['requires_height_sha256'][:12]} but {cfg['stage_height']} is {sha(hp)[:12]}; rebuild the stage first")
    live = dict(segments=sha(o['live_segments']), candidates=sha(o['live_candidates']))
    same = live['segments'] == sha(o['segments']) and live['candidates'] == sha(o['candidates']) and o['live_mask'].exists() and sha(o['live_mask']) == sha(o['mask'])
    if same: print(f"already installed: the live inputs are the staged {cfg['to_version']} files - no change"); return
    known = {exp['segments_sha256']: exp['segments_version']}
    for v in ('a', 'b'):
        p = ENC / cfg['outputs']['segments'].replace('{variant}', v)
        if p.exists(): known[sha(p)] = '305.4' + v
    if live['segments'] not in known: raise SystemExit(f"REFUSED: live segments305.json sha {live['segments'][:12]} is neither {exp['segments_version']} ({exp['segments_sha256'][:12]}) nor a staged 305.4 variant; nothing changed")
    if known[live['segments']] == exp['segments_version'] and live['candidates'] != exp['candidates_sha256']: raise SystemExit(f"REFUSED: live Out/forest305_candidates.json sha {live['candidates'][:12]} is not the {exp['segments_version']} file; nothing changed")
    stamp = 'from-' + known[live['segments']] + '-' + live['segments'][:12]; bdir = ENC / I['backup_dir'] / stamp
    for src in (o['live_segments'], o['live_candidates']) + ((o['live_mask'],) if o['live_mask'].exists() else ()):
        wr.put(bdir / src.name, src.read_bytes())
    wr.put(o['live_mask'], o['mask'].read_bytes()); wr.put(o['live_candidates'], o['candidates'].read_bytes()); wr.put(o['live_segments'], o['segments'].read_bytes())
    print(f"editor inputs: {known[live['segments']]} -> {cfg['to_version']} (segments305.json, Out/forest305_candidates.json, {B.rel(o['live_mask'])}); backups in {B.rel(bdir)}. "
          f"The scenes change only when Enclosure305 build-scene runs (after CliffBoundary308 tiles:<scene>:stage={cfg['stage']}: the segment file needs height {seg['requires_height_sha256'][:12]})")


def revert_to_305_3(cfg, wr):
    o = cfg['_o']; I = cfg['install']; exp = I['expect_live']; src = (ENC / I['revert_to_305_3']['segments'], ENC / I['revert_to_305_3']['candidates'])
    for p, want in zip(src, (exp['segments_sha256'], exp['candidates_sha256'])):
        if not p.exists() or sha(p) != want: raise SystemExit(f"REFUSED: {B.rel(p)} is missing or is not the recorded 305.3 file ({want[:12]}); nothing changed")
    m3 = ENC / I['revert_to_305_3']['mask']
    if not m3.exists() or sha(m3) != exp['mask_305_3_sha256']: raise SystemExit(f"REFUSED: {B.rel(m3)} is missing or changed; nothing changed")
    wr.put(o['live_segments'], src[0].read_bytes()); wr.put(o['live_candidates'], src[1].read_bytes())
    print(f"editor inputs -> 305.3 (bytes of {B.rel(src[0])} and {B.rel(src[1])}; mask {B.rel(m3)} was never removed). {B.rel(o['live_mask'])} stays on disk (no file names it). "
          f"305.3 needs the stage-1a height: CliffBoundary308 tiles:<scene>:stage=1a first, then Enclosure305 build-scene")


def main():
    global CFG
    B.utf8()
    ap = argparse.ArgumentParser(description='Enclosure305 v305.3 (cliff stage 1a) / v305.4 (stage 1b, --cfg cliff305_4.cfg.json --variant a|b)')
    ap.add_argument('cmd', choices=['status', 'build', 'closure', 'all', 'revert', 'install']); ap.add_argument('--dry', action='store_true'); ap.add_argument('--to', default='305.2')
    ap.add_argument('--cfg', help='config file name under Enclosure305/ or a path (default cliff305_3.cfg.json)'); ap.add_argument('--variant', help='v305.4: a | b')
    a = ap.parse_args()
    if a.cfg: CFG = Path(a.cfg) if Path(a.cfg).is_absolute() or Path(a.cfg).exists() else ENC / a.cfg
    raw = json.loads(CFG.read_text(encoding='utf-8'))
    cfg = load_cfg(a.variant if raw.get('variants') and not (a.cmd == 'revert' and a.variant is None) else (next(iter(raw['variants'])) if raw.get('variants') else None)); wr = Writer(a.dry)
    if a.cmd == 'status': status(cfg); return 0
    if a.cmd == 'revert': revert(cfg, wr, a.to); wr.show(); return 0
    if a.cmd == 'install':
        if not cfg.get('install'): raise SystemExit('REFUSED: install is a v305.4 command (--cfg cliff305_4.cfg.json --variant a|b)')
        install(cfg, wr, cfg['variant']); wr.show(); return 0
    built = build(cfg, wr) if a.cmd in ('build', 'all') else None
    if a.cmd in ('closure', 'all'): closure(cfg, wr, built)
    wr.show()
    return 0


if __name__ == '__main__':
    sys.exit(main())
