"""SPEC-WORLD-HANYANG-308 phase 1b: city wall + gates + bridges as placements of shared modules.
python Tools/Art/hanyang308_city.py [--proof]  ->  Tools/Unity/Stage308_hanyang/city308.json (+ Out/city_report.json)
Level modules of one merlon (ta, 3.33 m) step with the ground like the real wall: the body courses stay level, the parapet steps.
Frame of every module: +X along the wall (clockwise round of the city seen with the city on the left), -Z outward, Y up."""
import json, math, sys
sys.path.insert(0, 'Tools/Art')
import numpy as np, cliff308_base as B

ST = B.ROOT / 'Tools/Unity/Stage308_hanyang'
J = json.loads((ST / 'hanyang308.json').read_text(encoding='utf-8'))
H = B.load_height(B.PL / 'Stage/height_pn8.bytes'); WET = B.wet()
PROBE = {j['id']: j for j in json.loads((ST / 'Out/city_probe.json').read_text())['joins']} if (ST / 'Out/city_probe.json').exists() else {}
TA = 10 / 3; WH = 6.5; WD = 6.0; PAR = 1.2; IN_MIN = 4.5; Q = 0.25
V = {v['id']: (float(v['x']), float(v['z'])) for v in J['wall']['vertices']}
GATE = {g['id']: g for g in J['gates']}
g = lambda x, z: float(B.sample(H, x, z))
rep = dict(warn=[], gates={}, wall={}, bridges={})

# as-built overrides after the probe (see SPEC §10): the wall starts at the north end of the kept NE gate slab
V['Q'] = (2227.6, 3501.6)
CHAINS = [dict(id='E', v=['Q', 'R', 'S', 'N2', 'N3', 'N4', 'N5', 'N6', 'F', 'J'], joins=('Q', 'J')), dict(id='W', v=['G', 'W2', 'W3', 'W4', 'W5'], joins=('G', 'W5'))]
JOIN_CHUNK = dict(Q=('CapitalWall_0_24', 2236.0, 3489.5), J=('CapitalWall_1_13', *V['J']), G=('CapitalWall_3_01', *V['G']), W5=('CapitalWall_3_12', *V['W5']))
CHI = ['N3', 'N4', 'N6', 'W2', 'W3', 'W4']
# gate kits: base prefab, base scale, body height (walk level above the apron), arch w/h, base w/d, munru prefab + scale
KIT = dict(
    large=dict(base='Gen_GateBase_Large', bs=(1, 1, 1), body=6.6, aw=5.2, ah=6.0, w=32.0, d=12.0, munru='Gen_Munru_1F_3x2', ms=1.7),
    small=dict(base='Gen_GateBase_Small', bs=(1.15, 1.354, 1.15), body=4.8 * 1.354, aw=3.6 * 1.15, ah=4.2 * 1.354, w=16.0 * 1.15, d=7.5 * 1.15, munru='Gen_Munru_1F_3x2', ms=1.0))
NEWGATES = dict(gate_donui=dict(kit='large', seg=('W2', 'W3'), open=True), gate_sukjeong=dict(kit='small', seg=('N3', 'N4'), open=False), gate_hyehwa=dict(kit='small', seg=('S', 'N2'), open=False))


def frame(a, b):
    dx, dz = b[0] - a[0], b[1] - a[1]; L = math.hypot(dx, dz); d = (dx / L, dz / L); nin = (-d[1], d[0]); psi = math.degrees(math.atan2(dx, dz)) % 360
    return L, d, nin, (psi - 90) % 360


def item(a, x, y, z, yaw, s=(1, 1, 1), col='', n='', pitch=0.0):
    return dict(n=n, a=a, col=col, p=[round(x, 3), round(y, 3), round(z, 3)], yaw=round(yaw % 360, 3), s=[round(float(v), 4) for v in s], pitch=pitch)


def ta_tops(a, d, nin, L):
    """level top (walk level) of each merlon-long piece of a straight run"""
    n = max(1, round(L / TA)); lt = L / n; tops = []; hout = []; hin = []
    for k in range(n):
        us = [a[0] + d[0] * (k + t) * lt for t in (0, .25, .5, .75, 1)]; ws = [a[1] + d[1] * (k + t) * lt for t in (0, .25, .5, .75, 1)]
        go = max(g(u - nin[0] * o, w - nin[1] * o) for u, w in zip(us, ws) for o in (2.8, 3.6, 4.6)); gi = max(g(u + nin[0] * o, w + nin[1] * o) for u, w in zip(us, ws) for o in (2.4, 3.2, 4.2))
        tops.append(math.ceil(max(go + WH, gi + IN_MIN) / Q) * Q); hout.append(go); hin.append(gi)
    return n, lt, tops, hout, hin


def build():
    units = []; rays = []; wall_groups = []; gate_groups = []; stats = dict(ta=0, modules=dict(W1=0, W2=0, W3=0), length=0.0, out_min=99, in_min=99, step_max=0, drums=0, chi=0, piers=0); barrier = []
    vert_top = {}
    for ch in CHAINS:
        ids = ch['v']; runs = []
        for i in range(len(ids) - 1):
            a, b = V[ids[i]], V[ids[i + 1]]; L, d, nin, yaw = frame(a, b); cuts = []
            for gid, ng in NEWGATES.items():
                if ng['seg'] != (ids[i], ids[i + 1]): continue
                G = GATE[gid]; k = KIT[ng['kit']]; sg = (G['x'] - a[0]) * d[0] + (G['z'] - a[1]) * d[1]; off = (G['x'] - a[0]) * nin[0] + (G['z'] - a[1]) * nin[1]
                cx, cz = a[0] + d[0] * sg, a[1] + d[1] * sg; hw = k['w'] / 2; top = G['ground'] + k['body']; cuts.append((sg - hw + 0.3, sg + hw - 0.3, top, gid))
                gate_groups.append(gate_group(gid, ng, k, cx, cz, G['ground'], yaw, d, nin)); rep['gates'][gid]['centre_offset_from_plan_m'] = round(off, 2)
                barrier.append(('gate', gid, cx, cz, yaw, k['w'], k['d'], k['aw'] if ng['open'] else 0))
            spans = []; s0 = 0.0
            for c in sorted(cuts): spans.append((s0, c[0], None if not spans and s0 == 0 else None, c[2])); s0 = c[1]
            spans.append((s0, L, None, None))
            # each span: (from, to, gate top at its start, gate top at its end)
            sp2 = []; prev_top = None
            for k_, c in enumerate(sorted(cuts) + [None]):
                f = sp2[-1][1] if False else (0.0 if k_ == 0 else sorted(cuts)[k_ - 1][1]); t = c[0] if c else L
                sp2.append((f, t, prev_top, c[2] if c else None)); prev_top = c[2] if c else None
            for f, t, gtop_a, gtop_b in sp2:
                p0 = (a[0] + d[0] * f, a[1] + d[1] * f); n, lt, tops, ho, hi = ta_tops(p0, d, nin, t - f)
                if gtop_a is not None:
                    for q in range(min(2, n)): tops[q] = max(tops[q], gtop_a) if q else gtop_a if tops[q] <= gtop_a + 1.5 else tops[q]
                if gtop_b is not None:
                    for q in range(min(2, n)): tops[-1 - q] = max(tops[-1 - q], gtop_b) if q else gtop_b if tops[-1 - q] <= gtop_b + 1.5 else tops[-1 - q]
                runs.append(dict(seg=ids[i] + ids[i + 1], p0=p0, d=d, nin=nin, yaw=yaw, n=n, lt=lt, tops=tops, ho=ho, hi=hi, f=f, t=t, a=a, L=L))
        # ramps to the kept chunks at the two ends of the chain
        for end, jid in ((0, ch['joins'][0]), (1, ch['joins'][1])):
            pr = PROBE.get(jid);
            if not pr or pr['n'] == 0: rep['warn'].append('no probe for join ' + jid); continue
            walk = pr['top'] - 0.9; seq = [];
            for r in (runs if end == 0 else runs[::-1]):
                for q in (range(r['n']) if end == 0 else range(r['n'] - 1, -1, -1)): seq.append((r, q))
            for i_, (r, q) in enumerate(seq):
                want = walk - 0.75 * (i_ + 1)
                if want <= r['tops'][q]: break
                r['tops'][q] = math.ceil(want / Q) * Q
            cx, cz = V[jid]; barrier.append(('pier', jid, cx, cz)); stats['piers'] += 1
            wall_groups.append(dict(name='Join_' + jid, cull=0.01, items=[item('mesh:Pier', cx, walk, cz, (runs[0] if end == 0 else runs[-1])['yaw'], col='box', n='Pier_' + jid)])); rep['wall']['join_' + jid] = dict(kept_top=pr['top'], pier_walk=round(walk, 2), x=cx, z=cz)
        # no single-piece dips
        allp = [(r, q) for r in runs for q in range(r['n'])]
        for i_ in range(1, len(allp) - 1):
            r, q = allp[i_]; l = allp[i_ - 1][0]['tops'][allp[i_ - 1][1]]; rr = allp[i_ + 1][0]['tops'][allp[i_ + 1][1]]
            if r['tops'][q] < min(l, rr): r['tops'][q] = min(l, rr)
        # modules
        for r in runs:
            items = []; q = 0; sx = r['lt'] / TA
            while q < r['n']:
                m = 1
                while m < 3 and q + m < r['n'] and r['tops'][q + m] == r['tops'][q]: m += 1
                x = r['p0'][0] + r['d'][0] * q * r['lt']; z = r['p0'][1] + r['d'][1] * q * r['lt']; top = r['tops'][q]
                items.append(item('mesh:W%d' % m, x, top - WH, z, r['yaw'], (sx, 1, 1), 'box')); stats['modules']['W%d' % m] += 1
                lo = min(g(x + r['d'][0] * u + r['nin'][0] * o, z + r['d'][1] * u + r['nin'][1] * o) for u in (0, m * r['lt'] / 2, m * r['lt']) for o in (-3.0, 0, 2.5))
                if lo < top - WH - WD + 0.3: rep['warn'].append('skirt short at %.0f,%.0f: ground %.1f < bottom %.1f' % (x, z, lo, top - WH - WD))
                barrier.append(('box', r['seg'], x + r['d'][0] * m * r['lt'] / 2, z + r['d'][1] * m * r['lt'] / 2, r['yaw'], m * r['lt'], 5.5))
                q += m
            for q in range(r['n']):
                stats['out_min'] = min(stats['out_min'], r['tops'][q] - r['ho'][q]); stats['in_min'] = min(stats['in_min'], r['tops'][q] - r['hi'][q])
                if q: stats['step_max'] = max(stats['step_max'], abs(r['tops'][q] - r['tops'][q - 1]))
            stats['ta'] += r['n']; stats['length'] += r['t'] - r['f']
            wall_groups.append(dict(name='Wall_%s_%s_%d' % (ch['id'], r['seg'], round(r['f'])), cull=0.012, items=items))
            # seal ray stations every 2 m
            s = 1.0
            while s < r['t'] - r['f']:
                x = r['p0'][0] + r['d'][0] * s; z = r['p0'][1] + r['d'][1] * s
                rays.append(dict(x=round(x, 2), z=round(z, 2), nx=round(r['nin'][0], 4), nz=round(r['nin'][1], 4), y=round(max(g(x - r['nin'][0] * 4.5, z - r['nin'][1] * 4.5), g(x + r['nin'][0] * 4.5, z + r['nin'][1] * 4.5)), 2), want='block', tag='%s@%.0f' % (r['seg'], r['f'] + s))); s += 2.0
            vert_top.setdefault(r['seg'][:len(r['seg'])], None)
        # corner drums + chi at the inner vertices
        for i in range(1, len(ids) - 1):
            vid = ids[i]; rl = [r for r in runs if r['seg'] == ids[i - 1] + vid][-1]; rn = [r for r in runs if r['seg'] == vid + ids[i + 1]][0]; top = max(rl['tops'][-1], rn['tops'][0]); x, z = V[vid]
            items = [item('mesh:Drum', x, top - WH, z, 0, col='box', n='Drum_' + vid)]; stats['drums'] += 1; barrier.append(('drum', vid, x, z))
            if vid in CHI:
                ox = -(rl['nin'][0] + rn['nin'][0]); oz = -(rl['nin'][1] + rn['nin'][1]); l = math.hypot(ox, oz); ox /= l; oz /= l; yaw = math.degrees(math.atan2(-ox, -oz)) % 360; tx, tz = oz, -ox
                gs = [g(x + ox * o + tx * u, z + oz * o + tz * u) for o in (4.0, 6.5, 8.5) for u in (-4, 0, 4)]
                if max(gs) > top - 4.0 or min(gs) < top - WH - WD + 0.3: rep['warn'].append('chi %s skipped: ground %.1f..%.1f against top %.1f' % (vid, min(gs), max(gs), top))
                else: items.append(item('mesh:Chi', x, top - WH, z, yaw, col='box', n='Chi_' + vid)); stats['chi'] += 1; barrier.append(('box', 'chi' + vid, x + ox * 5, z + oz * 5, yaw, 8, 7))
            wall_groups.append(dict(name='Corner_' + vid, cull=0.01, items=items))
    # gate apertures: open stations for the rays
    for gid, ng in NEWGATES.items():
        rg = rep['gates'][gid]
        rays.append(dict(x=rg['x'], z=rg['z'], nx=rg['nin'][0], nz=rg['nin'][1], y=rg['apron'], want='open' if ng['open'] else 'block', tag=gid))
    stats['length'] = round(stats['length'], 1); stats['out_min'] = round(stats['out_min'], 2); stats['in_min'] = round(stats['in_min'], 2); rep['wall'].update(stats)
    units.append(dict(name='Hanyang308_Wall', groups=wall_groups + extra_gates(barrier))); units.append(dict(name='Hanyang308_Gates', groups=gate_groups))
    br = bridges()
    if br: units.append(dict(name='Hanyang308_Bridges', groups=br))
    prefabs = sorted({it['a'][7:] for u in units for gr in u['groups'] for it in gr['items'] if it['a'].startswith('prefab:')})
    joins = [dict(id=k, chunk=v[0], x=v[1], z=v[2]) for k, v in JOIN_CHUNK.items()]
    return dict(version='308.hanyang.p1b', prefabs=prefabs, units=units, rays=rays, joins=joins), barrier


def gate_group(gid, ng, k, cx, cz, apron, yaw, d, nin):
    its = []; bs = k['bs']; top = apron + k['body']; L = lambda lx, lz: (cx + d[0] * lx + nin[0] * lz, cz + d[1] * lx + nin[1] * lz)
    its.append(item('prefab:' + k['base'], cx, apron, cz, yaw, bs, 'mesh', n='Base'))
    its.append(item('prefab:' + k['munru'], cx, top, cz, yaw, (k['ms'],) * 3, 'box', n='Munru'))
    # ground under the base
    gs = [g(*L(u * k['w'] / 2, w * k['d'] / 2)) for u in (-1, -.5, 0, .5, 1) for w in (-1, 0, 1)]; drop = apron - min(gs)
    if drop > 0.25: its.append(item('mesh:Block', cx, apron - drop - 0.3, cz, yaw, (k['w'] + 0.3, drop + 0.3, k['d'] + 0.3), '', n='Foot'))
    zd = -k['d'] / 2 + 0.38 * k['d']; aw, ah = k['aw'], k['ah']
    if ng['open']:
        for sgn in (-1, 1): x, z = L(sgn * (aw / 2 - 0.12), zd); its.append(item('mesh:Leaf', x, apron, z, yaw - 90, (aw / 2, ah - 0.9, 1), 'box', n='Leaf_open'))
    else:
        x, z = L(-aw / 2 - 0.3, zd); its.append(item('mesh:Leaf', x, apron, z, yaw, (aw / 2 + 0.3, ah, 1), '', n='Leaf_L'))
        x, z = L(aw / 2 + 0.3, zd); its.append(item('mesh:Leaf', x, apron, z, yaw + 180, (aw / 2 + 0.3, ah, 1), '', n='Leaf_R'))
        for hy in (0.18, 0.5, 0.82):
            x, z = L(-aw / 2 - 0.3, zd); its.append(item('mesh:Band', x, apron + ah * hy, z, yaw, (aw + 0.6, 0.14, 1), '', n='Band'))
        x, z = L(0, zd); its.append(item('col', x, apron + ah / 2, z, yaw, (aw + 1.0, ah + 0.6, 0.9), n='Blocker'))
    x, z = L(0, -2.78 * k['ms']); its.append(item('mesh:Plaque', x, top + 2.72 * k['ms'], z, yaw, (2.0 * k['ms'], 0.8 * k['ms'], 1), '', n='Plaque'))
    rep['gates'][gid] = dict(x=round(cx, 2), z=round(cz, 2), apron=apron, yaw_base=round(yaw, 2), nin=[round(nin[0], 4), round(nin[1], 4)], kit=k['base'], base_scale=bs, base_w=k['w'], base_d=k['d'], walk_level=round(top, 2), arch_clear=[round(aw, 2), round(ah, 2)],
                           munru=k['munru'], munru_scale=k['ms'], open=ng['open'], ground_under_base=[round(min(gs), 2), round(max(gs), 2)], foot_block_m=round(max(drop, 0), 2))
    if max(gs) - apron > 0.4: rep['warn'].append('%s: ground %.2f above the apron under the base' % (gid, max(gs) - apron))
    return dict(name='Gate_' + gid[5:], cull=0.01, items=its)


def extra_gates(barrier):
    """흥인지문 옹성: a half-round outwork of wall modules outside the kept NE gate, open to the north (SPEC §10.3)."""
    o = ONG
    if not o: return []
    cx, cz, r = o['cx'], o['cz'], o['r']; its = []; n = max(3, round(math.radians(abs(o['a1'] - o['a0'])) * r / TA)); tops = []
    pts = [(cx + r * math.sin(math.radians(o['a0'] + (o['a1'] - o['a0']) * k / n)), cz + r * math.cos(math.radians(o['a0'] + (o['a1'] - o['a0']) * k / n))) for k in range(n + 1)]
    out = []
    for k in range(n):
        a, b = pts[k], pts[k + 1]; L, d, nin, yaw = frame(a, b)
        # the city side of this arc is its centre: flip the frame when the centre is on the right
        if (cx - a[0]) * nin[0] + (cz - a[1]) * nin[1] < 0: a, b = b, a; L, d, nin, yaw = frame(a, b)
        go = max(g(a[0] + d[0] * t * L - nin[0] * w, a[1] + d[1] * t * L - nin[1] * w) for t in (0, .5, 1) for w in (2.8, 4.2)); gi = max(g(a[0] + d[0] * t * L + nin[0] * w, a[1] + d[1] * t * L + nin[1] * w) for t in (0, .5, 1) for w in (2.4, 4.0))
        out.append((a, L, yaw, math.ceil(max(go + o['h'], gi + IN_MIN) / Q) * Q))
    top = max(t for *_, t in out)
    for a, L, yaw, t in out:
        its.append(item('mesh:W1', a[0] - 0.12 * L * math.sin(math.radians(yaw + 90)), top - WH, a[1] - 0.12 * L * math.cos(math.radians(yaw + 90)), yaw, (L / TA * 1.24, 1, 1), 'box')); barrier.append(('box', 'ong', a[0], a[1], yaw, L, 5.5))
    rep['gates']['gate_heungin_ongseong'] = dict(centre=[cx, cz], radius=r, from_deg=o['a0'], to_deg=o['a1'], pieces=n, walk_level=top, note=o['note'])
    return [dict(name='Wall_Ongseong_heungin', cull=0.01, items=its)]


ONG = None
BRIDGES = []


def bridges():
    grs = []
    for b in BRIDGES:
        (x0, z0), (x1, z1) = b['a'], b['b']; L, d, nin, yaw = frame((x0, z0), (x1, z1)); psi = yaw + 90; cx, cz = (x0 + x1) / 2, (z0 + z1) / 2; hw = b['w'] / 2 - 0.2; its = []
        its.append(item('mesh:' + b['mesh'], cx, b['deck'], cz, psi, (1, 1, L / b['len']), 'box', n='Deck'))
        for sgn in (-1, 1): its.append(item('col', cx + nin[0] * sgn * hw, b['deck'] + 0.55, cz + nin[1] * sgn * hw, psi, (0.3, 1.1, L), n='RailCol'))
        ends = {}
        for e, (x, z, sgn) in enumerate(((x0, z0, -1), (x1, z1, 1))):
            gl = g(x + d[0] * sgn * b['ramp'], z + d[1] * sgn * b['ramp']); rise = b['deck'] - gl; pitch = math.degrees(math.atan2(rise, b['ramp'])) * sgn; ends['end%d_ground_at_ramp_foot' % e] = round(gl, 2)
            its.append(item('mesh:Block', x + d[0] * sgn * b['ramp'] / 2, (b['deck'] + gl) / 2 - 0.5 * math.cos(math.radians(pitch)), z + d[1] * sgn * b['ramp'] / 2, psi, (b['w'], 0.5, math.hypot(b['ramp'], rise) + 0.4), 'box', n='Ramp%d' % e, pitch=round(pitch, 2)))
        prof = [g(x0 + d[0] * s_, z0 + d[1] * s_) for s_ in np.arange(0, L + .1, 2.0)]
        rep['bridges'][b['id']] = dict(a=b['a'], b=b['b'], length=round(L, 1), width=b['w'], deck=b['deck'], mesh=b['mesh'], yaw=round(psi % 360, 1), bed_min=round(min(prof), 2), clear_over_bed=round(b['deck'] - min(prof), 2), wet_cells_spanned=int(sum(WET[int(round((z0 + d[1] * s_) / 4)), int(round((x0 + d[0] * s_) / 4))] for s_ in range(0, int(L), 4))), **ends)
        grs.append(dict(name='Bridge_' + b['id'], cull=0.01, items=its))
    return grs


def proof(barrier, open_gates):
    """1 m raster: walk from inside the city; the barrier = the collider footprints of this plan + the kept wall line."""
    import scipy.ndimage as ndi
    x0, x1, z0, z1 = 1380, 2420, 2500, 3960; X, Z = np.meshgrid(np.arange(x0, x1) + .5, np.arange(z0, z1) + .5); hh = B.sample(H, X, Z); gz, gx = np.gradient(hh); walk = np.hypot(gx, gz) < math.tan(math.radians(45))
    solid = np.zeros(X.shape, bool)
    def rect(cx, cz, yaw, L, Wd, hole=0.0):
        t = math.radians(yaw); ux, uz = math.cos(t), -math.sin(t); vx, vz = math.sin(t), math.cos(t); a = (X - cx) * ux + (Z - cz) * uz; b = (X - cx) * vx + (Z - cz) * vz
        m = (abs(a) <= L / 2 + 0.3) & (abs(b) <= Wd / 2);
        if hole: m &= abs(a) > hole / 2
        solid[m] = True
    for it in barrier:
        if it[0] == 'box': rect(it[2], it[3], it[4], it[5], it[6])
        elif it[0] == 'gate': rect(it[2], it[3], it[4], it[5], it[6], it[7] if it[1] in open_gates else 0.0)
        elif it[0] in ('drum', 'pier'): solid[np.hypot(X - it[2], Z - it[3]) <= 3.0] = True
    vs = J['wall']['vertices']; ring = [(float(v['x']), float(v['z'])) for v in vs]; ring[4] = (2231.0, 3497.0)
    for i, v in enumerate(vs):
        if v['edge_to_next'] != 'kept': continue
        a = ring[i]; b = ring[(i + 1) % len(ring)]; L = math.hypot(b[0] - a[0], b[1] - a[1])
        for s in np.arange(0, L + 1, 1.0): solid[np.hypot(X - (a[0] + (b[0] - a[0]) * s / L), Z - (a[1] + (b[1] - a[1]) * s / L)) <= 3.0] = True
    # the kept NE gate slab between the kept chunk and the new wall start (shut for this proof)
    for s in np.arange(0, 1.01, 0.05): solid[np.hypot(X - (2231.0 + (V['Q'][0] - 2231.0) * s), Z - (3497.0 + (V['Q'][1] - 3497.0) * s)) <= 3.0] = True
    ringn = [V.get(v['id'], (float(v['x']), float(v['z']))) for v in vs]; ins = np.zeros(X.shape, bool)
    for i in range(len(ringn)):
        (xa, za), (xb, zb) = ringn[i], ringn[(i + 1) % len(ringn)]
        if za == zb: continue
        ins ^= ((za > Z) != (zb > Z)) & (X < (xb - xa) * (Z - za) / (zb - za) + xa)
    far = ~ndi.binary_dilation(ins, iterations=10)
    lab, _ = ndi.label(walk & ~solid); seed = lab[3250 - z0, 1959 - x0]; reach = lab == seed
    return dict(seed_ok=bool(seed > 0), reached_cells=int(reach.sum()), reached_outside_10m=int((reach & far).sum()), barrier_cells=int(solid.sum()))


if __name__ == '__main__':
    exec((ST / 'city308_sites.py').read_text(encoding='utf-8')) if (ST / 'city308_sites.py').exists() else None
    plan, barrier = build()
    (ST / 'city308.json').write_text(json.dumps(plan, ensure_ascii=False), encoding='utf-8')
    if '--proof' in sys.argv:
        rep['proof_all_shut'] = proof(barrier, set()); rep['proof_donui_open'] = proof(barrier, {'gate_donui'})
    (ST / 'Out/city_report.json').write_text(json.dumps(rep, ensure_ascii=False, indent=1), encoding='utf-8')
    w = rep['wall']; print('wall', {k: w[k] for k in ('length', 'ta', 'modules', 'out_min', 'in_min', 'step_max', 'drums', 'chi', 'piers') if k in w})
    for k, v in rep['gates'].items(): print(k, {a: b for a, b in v.items() if a in ('x', 'z', 'apron', 'walk_level', 'arch_clear', 'ground_under_base', 'foot_block_m', 'centre_offset_from_plan_m', 'pieces', 'radius')})
    for k, v in rep['bridges'].items(): print(k, v)
    for k in ('proof_all_shut', 'proof_donui_open'):
        if k in rep: print(k, rep[k])
    print('items', sum(len(gr['items']) for u in plan['units'] for gr in u['groups']), 'rays', len(plan['rays']), 'prefabs', plan['prefabs']); [print('WARN', x) for x in rep['warn']]
