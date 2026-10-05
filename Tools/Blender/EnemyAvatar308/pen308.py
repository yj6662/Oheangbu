"""#308 enemy rig revision 2: the arm-in-body measure for OPEN, multi-part meshes, validated offline before it goes into the editor tool.

The editor tool's old test (ray parity, 4 of 5 rays odd) needs a closed single shell; these three meshes have 940-1713 open edges and
21-128 loose parts, so its self-check fails. The rule now has TWO votes per arm vertex (review finding F5: the winding number alone
misreads 5-12 % of the points just inside the outermost surface and 2-3 % of the points just outside it):
    W  generalised winding number of the point against the body triangles > windingInside
    N  the nearest body triangle (within maxDepth) is seen from BEHIND
    W and N      -> penetration, depth = distance to that nearest triangle
    W, not N     -> UNDETERMINED (the winding says inside, the nearest face is seen from its front: between two layers)
    N, not W     -> UNDETERMINED, but only when the point is within behindOnlyMaxCm of that face and inside a cone of behindConeDeg
                    around its inward normal. Without the two bounds a point 20 cm away beyond the RIM of an open sheet (arm hole,
                    skirt hem, hair card) counts as "behind" it: measured, hundreds of such votes per monster (n_variants probe)
    neither      -> outside; clearance = distance to the nearest body triangle
An UNDETERMINED vertex is reported with its depth and never counted as "no penetration": a picture decides.
Far triangles are summed per grid cell as one dipole (area-normal at the area-weighted centre), near cells exactly.
This file is the SAME algorithm as EnemyRigVerify308.Measure.cs PenWorld (cell, beta, thresholds from verify308.json 'pen') and checks
  1 fast winding against the exact sum on every arm test vertex of sampled poses (largest difference, decisions that flip)
  2 the self-test points (hips joint, chest joint inside; 1 m beside the hips outside) on every sampled pose
  3 the NEAR-SURFACE self-test of the editor tool: points nearOffsetCm inside / outside the outermost body triangles of the height
    band the hands pass; how many the two votes get right, leave undetermined, or get wrong together
  4 its numbers against the Blender BVH numbers of rig308.py for the same source clips (<id>_measure.json)
  python Tools/Blender/EnemyAvatar308/pen308.py   ->  Art/Characters308/EnemyRigVerify2/pen308.json     (numpy only, CPU)"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..')).replace(os.sep, '/')
CFG = json.load(open(os.path.join(HERE, 'avatar308.json'), encoding='utf-8'))
PEN = json.load(open(REPO + '/' + CFG['stage'] + '/Data/verify308.json', encoding='utf-8'))['pen']
SIDES = ('Left', 'Right')


def exact_winding(P, A, B, C):
    """sum of the signed solid angles of triangles (A,B,C) seen from points P, divided by 4 pi (Van Oosterom - Strackee)"""
    out = np.zeros(len(P))
    for i in range(0, len(P), 128):
        p = P[i:i + 128][:, None, :]; a = A[None] - p; b = B[None] - p; c = C[None] - p
        la = np.linalg.norm(a, axis=2); lb = np.linalg.norm(b, axis=2); lc = np.linalg.norm(c, axis=2)
        det = np.einsum('ijk,ijk->ij', a, np.cross(b, c))
        den = la * lb * lc + np.einsum('ijk,ijk->ij', a, b) * lc + np.einsum('ijk,ijk->ij', b, c) * la + np.einsum('ijk,ijk->ij', c, a) * lb
        out[i:i + 128] = (2 * np.arctan2(det, den)).sum(1) / (4 * math.pi)
    return out


def tri_closest(p, A, B, C):
    """closest point on many triangles to one point (Ericson, closest point on triangle)"""
    ab = B - A; ac = C - A; ap = p - A
    d1 = (ab * ap).sum(1); d2 = (ac * ap).sum(1); bp = p - B; d3 = (ab * bp).sum(1); d4 = (ac * bp).sum(1); cp = p - C; d5 = (ab * cp).sum(1); d6 = (ac * cp).sum(1)
    vc = d1 * d4 - d3 * d2; vb = d5 * d2 - d1 * d6; va = d3 * d6 - d5 * d4
    q = np.empty_like(A); done = np.zeros(len(A), dtype=bool)
    def put(mask, val):
        m = mask & ~done; q[m] = val[m]; done[m] = True
    put((d1 <= 0) & (d2 <= 0), A); put((d3 >= 0) & (d4 <= d3), B); put((d6 >= 0) & (d5 <= d6), C)
    with np.errstate(all='ignore'):
        v = d1 / (d1 - d3); put((vc <= 0) & (d1 >= 0) & (d3 <= 0), A + ab * v[:, None])
        w = d2 / (d2 - d6); put((vb <= 0) & (d2 >= 0) & (d6 <= 0), A + ac * w[:, None])
        w2 = (d4 - d3) / ((d4 - d3) + (d5 - d6)); put((va <= 0) & ((d4 - d3) >= 0) & ((d5 - d6) >= 0), B + (C - B) * w2[:, None])
        den = 1.0 / (va + vb + vc); put(np.ones(len(A), dtype=bool), A + ab * (vb * den)[:, None] + ac * (vc * den)[:, None])
    return q


def tri_dist(p, A, B, C): return np.linalg.norm(p - tri_closest(p, A, B, C), axis=1)


def ray_hits_any(o, d, A, B, C, eps=1e-5):
    """does the ray o + t d (t > eps) meet any triangle (Moller - Trumbore, both faces)"""
    e1 = B - A; e2 = C - A; h = np.cross(d[None], e2); a = (e1 * h).sum(1); ok = np.abs(a) > 1e-14
    f = np.where(ok, 1.0 / np.where(ok, a, 1), 0); s = o[None] - A; u = f * (s * h).sum(1); q = np.cross(s, e1); v = f * (q * d[None]).sum(1); t = f * (e2 * q).sum(1)
    return bool((ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > eps)).any())


class Field:
    """Same data and the same arithmetic order as the C# PenWorld."""
    def __init__(self, V, tris, cell, beta):
        self.A = V[tris[:, 0]]; self.B = V[tris[:, 1]]; self.C = V[tris[:, 2]]; self.beta = beta
        an = .5 * np.cross(self.B - self.A, self.C - self.A); cen = (self.A + self.B + self.C) / 3.0; area = np.linalg.norm(an, axis=1)
        self.an = an; self.cen = cen; self.area = area
        lo = np.minimum(np.minimum(self.A, self.B), self.C).min(0); key = np.floor((cen - lo) / cell).astype(np.int64)
        dims = key.max(0) + 1; flat = (key[:, 0] * dims[1] + key[:, 1]) * dims[2] + key[:, 2]
        order = np.argsort(flat, kind='stable'); uniq, start = np.unique(flat[order], return_index=True); self.cells = []
        for k in range(len(uniq)):
            idx = order[start[k]: start[k + 1] if k + 1 < len(uniq) else len(order)]
            a = area[idx].sum(); c = (cen[idx] * area[idx, None]).sum(0) / a if a > 1e-12 else cen[idx].mean(0)
            r = max(np.linalg.norm(self.A[idx] - c, axis=1).max(), np.linalg.norm(self.B[idx] - c, axis=1).max(), np.linalg.norm(self.C[idx] - c, axis=1).max())
            self.cells.append((idx, an[idx].sum(0), c, r))
        self.centres = np.array([c[2] for c in self.cells]); self.radii = np.array([c[3] for c in self.cells]); self.normals = np.array([c[1] for c in self.cells])

    def winding(self, P):
        """fast winding number of many points at once (the same sums as the C# loop, grouped per cell)"""
        P = np.atleast_2d(P); r = self.centres[None] - P[:, None, :]; d = np.linalg.norm(r, axis=2); far = d > self.beta * self.radii[None]
        with np.errstate(all='ignore'): dip = (self.normals[None] * r).sum(2) / d ** 3
        w = np.where(far, dip, 0.0).sum(1)
        for k in np.where((~far).any(0))[0]:
            pts = np.where(~far[:, k])[0]; idx = self.cells[k][0]; p = P[pts][:, None, :]; a = self.A[idx][None] - p; b = self.B[idx][None] - p; c = self.C[idx][None] - p
            la = np.linalg.norm(a, axis=2); lb = np.linalg.norm(b, axis=2); lc = np.linalg.norm(c, axis=2)
            det = np.einsum('ijk,ijk->ij', a, np.cross(b, c)); den = la * lb * lc + np.einsum('ijk,ijk->ij', a, b) * lc + np.einsum('ijk,ijk->ij', b, c) * la + np.einsum('ijk,ijk->ij', c, a) * lb
            w[pts] += (2 * np.arctan2(det, den)).sum(1)
        return w / (4 * math.pi)

    def inside(self, P):
        """the W vote: fast sum, and the exact sum for a point within exactBand of the threshold (as the editor does)"""
        P = np.atleast_2d(P); w = self.winding(P); band = np.abs(w - PEN['windingInside']) < PEN['exactBand']
        if band.any(): w[band] = exact_winding(P[band], self.A, self.B, self.C)
        return w > PEN['windingInside'], w

    def nearest_signed(self, p, cap):
        """-> (distance to the nearest body triangle within cap, or inf; cosine between the triangle's outward normal and the direction
        from its closest point to p: -1 = straight behind the face, +1 = straight in front, near 0 = in its plane, beyond a rim)"""
        best = cap; found = False; cosine = 0.0; lb = np.linalg.norm(self.centres - p, axis=1) - self.radii
        for k in np.argsort(lb):
            if lb[k] >= best: break
            idx = self.cells[k][0]; q = tri_closest(p, self.A[idx], self.B[idx], self.C[idx]); d = np.linalg.norm(p - q, axis=1); j = int(d.argmin())
            if d[j] < best:
                best = float(d[j]); found = True; n = self.an[idx[j]]; cosine = float((p - q[j]) @ n) / (best * float(np.linalg.norm(n)) + 1e-30)
        return (best if found else float('inf')), cosine

    def nearest(self, p, cap):
        d, _ = self.nearest_signed(p, cap); return min(d, cap)

    def classify(self, P):
        """per point: 0 outside (or touching / nothing within maxDepth), 1 penetration (both votes), 2 undetermined (one vote); and the depth"""
        P = np.atleast_2d(P); W, _ = self.inside(P); kind = np.zeros(len(P), dtype=int); depth = np.full(len(P), np.inf)
        reach = (np.linalg.norm(self.centres[None] - P[:, None, :], axis=2) - self.radii[None]).min(1) < PEN['maxDepth']
        cone = -math.cos(math.radians(PEN['behindConeDeg'])); near = PEN['behindOnlyMaxCm'] / 100.0
        for i in np.where(reach | W)[0]:
            d, c = self.nearest_signed(P[i], PEN['maxDepth']); depth[i] = d
            if not (PEN['minDepth'] <= d < PEN['maxDepth']): continue
            if W[i]: kind[i] = 1 if c < 0 else 2
            elif c <= cone and d <= near: kind[i] = 2
        return kind, depth

    def near_surface(self, up):
        """The editor's near-surface self-test. Outermost body triangles of the height band the hands pass (a ray along the normal meets
        no other body triangle), an evenly strided sample of at most nearSamples; the point nearOffsetCm behind each must not read
        'outside' by both votes, the point nearOffsetCm in front must not read 'penetration' by both."""
        lo = min(self.A[:, up].min(), self.B[:, up].min(), self.C[:, up].min()); hi = max(self.A[:, up].max(), self.B[:, up].max(), self.C[:, up].max())
        rel = (self.cen[:, up] - lo) / max(1e-9, hi - lo); cand = np.where((rel > PEN['nearBandLow']) & (rel < PEN['nearBandHigh']) & (self.area > 1e-6))[0]
        want = int(PEN['nearSamples']); stride = max(1, len(cand) // max(1, 3 * want)); n = self.an / (self.area[:, None] + 1e-30); off = PEN['nearOffsetCm'] / 100.0; outer = []
        for t in cand[::stride]:
            if len(outer) >= want: break
            if not ray_hits_any(self.cen[t] + 1e-4 * n[t], n[t], self.A, self.B, self.C): outer.append(int(t))
        outer = np.array(outer, dtype=int); res = {'samples': int(len(outer))}
        if len(outer) == 0: return res
        ki, _ = self.classify(self.cen[outer] - off * n[outer]); ko, _ = self.classify(self.cen[outer] + off * n[outer])
        wi, _ = self.inside(self.cen[outer] - off * n[outer]); wo, _ = self.inside(self.cen[outer] + off * n[outer])
        pct = lambda m: round(100.0 * float(m.mean()), 1)
        res.update({'insidePointsPenetrationPct': pct(ki == 1), 'insidePointsUndeterminedPct': pct(ki == 2), 'insidePointsMissedPct': pct(ki == 0),
                    'outsidePointsClearPct': pct(ko == 0), 'outsidePointsUndeterminedPct': pct(ko == 2), 'outsidePointsFalsePct': pct(ko == 1),
                    'windingAloneMissesInsidePct': pct(~wi), 'windingAloneFalseOutsidePct': pct(wo)})
        return res


def sets(names, W, V0, tris, rest_heads):
    gi = {n: i for i, n in enumerate(names)}; w = lambda *ns: sum(W[:, gi[n]] for n in ns); dom = W.argmax(1); S = {}
    arm_all = w(*[s + p for s in SIDES for p in ('Arm', 'ForeArm', 'Hand')]); body = tris[(arm_all[tris] < PEN['bodyArmWeightMax']).all(1)]
    for side in SIDES:
        u0 = rest_heads[side + 'Arm']; e0 = rest_heads[side + 'ForeArm']; ua = (e0 - u0) / np.linalg.norm(e0 - u0); chain = w(side + 'Arm', side + 'ForeArm', side + 'Hand')
        fore_hand = w(side + 'ForeArm', side + 'Hand') >= .9
        upper = (dom == gi[side + 'Arm']) & (chain >= .9) & (((V0 - u0) @ ua) > .5 * np.linalg.norm(e0 - u0))
        S[side] = {'test': np.where(fore_hand | upper)[0], 'upper': np.where(upper & ~fore_hand)[0], 'hand': np.where(w(side + 'Hand') >= .5)[0]}
    return S, body


def main():
    out = {'schema': '308.enemyrig2.pen.2', 'settings': PEN, 'status': '[O] offline, source clips as the FBX holds them (no humanoid conversion, no runtime relax) = the prediction for candidate A', 'monsters': {}}
    for mid in CFG['monsters']:
        z = np.load(REPO + '/' + CFG['work'] + '/' + mid + '_dump.npz', allow_pickle=False); names = [str(x) for x in z['bones']]; V0 = z['V0']; tris = z['tris']; W = z['W'].astype(np.float64)
        ws = W.sum(1, keepdims=True); W = W / np.where(ws > 0, ws, 1)
        heads = {n: z['rest_rig'][i][:3, 3] for i, n in enumerate(names)}; S, body = sets(names, W, V0, tris, heads)
        blender = json.load(open(REPO + '/Art/Characters308/DokkaebiVerify/fix/%s_measure.json' % mid, encoding='utf-8'))['clips']
        m = {'bodyTriangles': int(len(body)), 'testVertices': {s: int(len(S[s]['test'])) for s in SIDES}, 'clips': {}, 'fastVsExact': {'points': 0, 'maxAbsDifference': 0.0, 'decisionsFlipped': 0, 'nearThreshold': 0}, 'selfTest': {'poses': 0, 'failed': 0, 'hipsMin': 9.0, 'chestMin': 9.0, 'besideMaxAbs': 0.0}}
        hi = names.index('Hips'); ci = names.index('Spine')
        Vh = np.concatenate([V0, np.ones((len(V0), 1))], 1)
        for clip, src in (('walk', 'walk'), ('walkfix', 'fixed'), ('run', 'run'), ('idle', 'motion'), ('attack', 'motion'), ('hit', 'motion'), ('stun', 'motion'), ('death', 'motion')):
            if 'pose_' + clip not in z.files: continue
            pose = z['pose_' + clip]; rest = z['rest_' + src]; inv = np.linalg.inv(rest); step = max(1, len(pose) // 10); n = 0
            res = {s: {'fore': 0.0, 'upper': 0.0, 'unsureFore': 0.0, 'unsureUpper': 0.0, 'framesInside': 0, 'framesUnsure': 0, 'clear': 9.0} for s in SIDES}
            for f in range(0, len(pose), step):
                M = pose[f] @ inv; V = np.einsum('vb,bij,vj->vi', W, M, Vh)[:, :3]; n += 1
                fld = Field(V, body, PEN['cell'], PEN['beta'])
                hips = pose[f][hi][:3, 3]; chest = pose[f][ci][:3, 3]; wh, wc, wo = [float(x) for x in fld.winding(np.array([hips, chest, hips + np.array([PEN['besideMetres'], 0, 0])]))]
                st = m['selfTest']; st['poses'] += 1; st['hipsMin'] = min(st['hipsMin'], wh); st['chestMin'] = min(st['chestMin'], wc); st['besideMaxAbs'] = max(st['besideMaxAbs'], abs(wo))
                if not (wh > PEN['windingInside'] and wc > PEN['windingInside'] and abs(wo) < PEN['windingOutsideMax']): st['failed'] += 1
                if clip == 'idle' and f == 0: m['nearSurface'] = fld.near_surface(2)   # the pose the editor's self-test runs on (idle, frame 0); Blender: z up
                for s in SIDES:
                    idx = S[s]['test']; pts = V[idx]; fast = fld.winding(pts); exact = exact_winding(pts, fld.A, fld.B, fld.C); raw = fast.copy()
                    band = np.abs(fast - PEN['windingInside']) < PEN['exactBand']; fast[band] = exact[band]        # the editor sums these exactly before judging
                    fe = m['fastVsExact']; fe['points'] += len(pts); fe['maxAbsDifference'] = max(fe['maxAbsDifference'], float(np.abs(raw - exact).max())); fe['summedExactly'] = fe.get('summedExactly', 0) + int(band.sum())
                    fe['decisionsFlipped'] += int(((fast > PEN['windingInside']) != (exact > PEN['windingInside'])).sum()); fe['nearThreshold'] += int((np.abs(exact - PEN['windingInside']) < .05).sum())
                    kind, depth = fld.classify(pts); up = np.isin(idx, S[s]['upper']); r = res[s]
                    pen_f = (kind == 1) & ~up; pen_u = (kind == 1) & up; un_f = (kind == 2) & ~up; un_u = (kind == 2) & up
                    if pen_f.any(): r['fore'] = max(r['fore'], float(depth[pen_f].max())); r['framesInside'] += 1
                    if pen_u.any(): r['upper'] = max(r['upper'], float(depth[pen_u].max()))
                    if un_f.any(): r['unsureFore'] = max(r['unsureFore'], float(depth[un_f].max())); r['framesUnsure'] += 1
                    if un_u.any(): r['unsureUpper'] = max(r['unsureUpper'], float(depth[un_u].max()))
                    if not pen_f.any():
                        for v in S[s]['hand'][::4]: r['clear'] = min(r['clear'], fld.nearest(V[v], PEN['clearCap']))
            b = blender.get(clip, {}).get('pen', {})
            m['clips'][clip] = {'posesSampled': n, **{s: {'foreHandMaxCm': round(res[s]['fore'] * 100, 2), 'sleeveMaxCm': round(res[s]['upper'] * 100, 2), 'posesWithForeHandInside': res[s]['framesInside'],
                                                         'unsureForeHandMaxCm': round(res[s]['unsureFore'] * 100, 2), 'unsureSleeveMaxCm': round(res[s]['unsureUpper'] * 100, 2), 'posesWithForeHandUnsure': res[s]['framesUnsure'],
                                                         'handClearanceCm': round(res[s]['clear'] * 100, 2) if res[s]['clear'] < 9 else None,
                                                         'blenderBvh': {'foreHandMaxCm': b.get(s, {}).get('fore_hand_max_cm'), 'sleeveMaxCm': b.get(s, {}).get('upper_max_cm'), 'framesForeHand': b.get(s, {}).get('fore_hand_frames'), 'frames': b.get(s, {}).get('frames')}} for s in SIDES}}
            print(mid, clip, {s: ('pen', m['clips'][clip][s]['foreHandMaxCm'], m['clips'][clip][s]['sleeveMaxCm'], 'unsure', m['clips'][clip][s]['unsureForeHandMaxCm'], m['clips'][clip][s]['unsureSleeveMaxCm'],
                                  'bvh', m['clips'][clip][s]['blenderBvh']['foreHandMaxCm'], m['clips'][clip][s]['blenderBvh']['sleeveMaxCm']) for s in SIDES}, flush=True)
        for k in ('hipsMin', 'chestMin', 'besideMaxAbs'): m['selfTest'][k] = round(m['selfTest'][k], 3)
        m['fastVsExact']['maxAbsDifference'] = round(m['fastVsExact']['maxAbsDifference'], 4)
        print(mid, 'fast vs exact', m['fastVsExact'], 'self-test', m['selfTest'], 'near surface', m.get('nearSurface'), flush=True)
        out['monsters'][mid] = m
    json.dump(out, open(REPO + '/' + CFG['out'] + '/pen308.json', 'w', encoding='utf-8'), indent=1, ensure_ascii=False)


if __name__ == '__main__':
    main()
