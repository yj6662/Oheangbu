"""#308 Hanyang phase 7 - where the ore-craft kit stands in the capital. Offline, deterministic. SPEC-WORLD-HANYANG-308 §17 (D308-64 / D308-79).

  python Tools/Art/hanyang308_arcane.py      # -> Tools/Unity/Stage308_hanyang/arcane308.json (kit meshes + blocks of instances) + Out/arcane_plan_report.json

ADDITIVE: reads hanyang308.json, Out/town_anchors.json, Out/life_anchors.json, city308.json (치 only), walkers308.json (kept clear, never written).
Numbers live in Tools/Unity/Stage308_arcane/arcane308_cfg.json. The editor (`Hanyang308 arcane:blocks`) seats every instance on the ground it finds,
drops what would stand in something, merges a block into one renderer per LOD and writes the unit plan that `arcane:apply` builds.
Systems, not scatter: lamps stand ON the conduit line; the conduit runs kerb to kerb between junction jars; instruments stand at plazas and yards.
"""
import json, math, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import arcane308_kit as K                                                # noqa: E402

ROOT = Path(__file__).resolve().parents[2]; S = ROOT / 'Tools/Unity/Stage308_hanyang'
CFG = json.loads((ROOT / 'Tools/Unity/Stage308_arcane/arcane308_cfg.json').read_text(encoding='utf-8'))
J = lambda p: json.loads((S / p).read_text(encoding='utf-8'))
H = J('hanyang308.json'); TA = J('Out/town_anchors.json'); LA = J('Out/life_anchors.json'); W = json.loads((ROOT / 'Tools/Unity/Stage308_walkers/walkers308.json').read_text(encoding='utf-8'))
HF = np.frombuffer((ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308/plan/Stage/height_pn8.bytes').read_bytes(), dtype='<f4').reshape(1501, 1001)
COLS = CFG['colliders']


def hat(x, z):
    u = x / 4; w = z / 4; j = int(u); i = int(w); fu = u - j; fw = w - i
    return float(HF[i, j] * (1 - fu) * (1 - fw) + HF[i, j + 1] * fu * (1 - fw) + HF[i + 1, j] * (1 - fu) * fw + HF[i + 1, j + 1] * fu * fw)


ROUTES = [(r['id'], np.array([[p['x'], p['z']] for p in r['points']]), r['half']) for r in W['routes']]
PEOPLE = np.array([[s['x'], s['z']] for s in W['stops']] + [[p['x'], p['z']] for p in W['posts']])


def seg_dist(p, a, b):
    d = b - a; L2 = float(d @ d); t = 0.0 if L2 < 1e-9 else float(np.clip((p - a) @ d / L2, 0, 1)); return float(np.linalg.norm(p - (a + d * t)))


def clear(x, z, r=0.3):
    """metres of room to the walkers: negative = inside a walked band or on a stop."""
    p = np.array([x, z]); best = 99.0
    for _, pts, half in ROUTES:
        d = min(seg_dist(p, a, b) for a, b in zip(pts[:-1], pts[1:])); best = min(best, d - half - r)
    best = min(best, float(np.min(np.linalg.norm(PEOPLE - p, axis=1))) - 0.6 - r)
    return best


def poly_at(pts, s):
    """point + unit tangent at arc length s of a polyline."""
    acc = 0.0
    for a, b in zip(pts[:-1], pts[1:]):
        L = float(np.linalg.norm(b - a))
        if s <= acc + L or (b is pts[-1]): t = (s - acc) / max(L, 1e-9); d = (b - a) / max(L, 1e-9); return a + (b - a) * min(max(t, 0), 1), d
        acc += L
    d = pts[-1] - pts[-2]; return pts[-1], d / np.linalg.norm(d)


def plen(pts): return float(sum(np.linalg.norm(b - a) for a, b in zip(pts[:-1], pts[1:])))
def yawdeg(d): return math.degrees(math.atan2(d[0], d[1])) % 360      # Unity yaw whose +z looks along d (x east, z north)
def fwd(yaw): a = math.radians(yaw); return np.array([math.sin(a), math.cos(a)])
def right(yaw): a = math.radians(yaw); return np.array([math.cos(a), -math.sin(a)])


BLOCKS = {}; REPORT = dict(skipped=[], classes={})


def put(unit, block, k, x, z, yaw=0.0, cls='', y=None, mode='g', fit=0.0, s=1.0, x2=None, z2=None, need_clear=True, col=True, margin=0.1):
    c = COLS.get(k.replace('_off', ''), None) if col else None
    if c:      # the editor may slide a fitted object: never further than its room to the walkers
        room = clear(x, z, max(c[0], c[2]) / 2 + margin)
        if need_clear and room < 0: REPORT['skipped'].append([block, k, round(float(x), 1), round(float(z), 1), 'walkers']); return False
        fit = max(0.0, min(fit, room))
    b = BLOCKS.setdefault(block, dict(id=block, unit='Hanyang308_Arcane_' + unit, inst=[]))
    b['inst'].append(dict(k=k, x=round(float(x), 3), z=round(float(z), 3), y=round(float(hat(x, z) if y is None else y), 3), yaw=round(float(yaw), 2), mode=mode, fit=fit, s=s,
                          x2=round(float(x if x2 is None else x2), 3), z2=round(float(z if z2 is None else z2), 3), seg=x2 is not None, col=c or [], cls=cls or k, lit=K.LIT.get(k, 0)))
    return True


def conduit_run(unit, block_of, pts, gaps=(), cls='conduit', ends=True, lamps=None):
    """a conduit along a polyline (np array of xz): modules of ~segM between stations; `gaps` = (arc0, arc1) left open (a gateway); junction jars at both ends
    and at each side of a gap; `lamps` = {arc: kit} stand ON the line (the pipe meets their plinth collars)."""
    L = plen(pts); seg = CFG['conduit']['segM']; lamps = dict(lamps or {}); cuts = [0.0, L]
    for a, b in gaps: cuts += [max(0, a), min(L, b)]
    cuts = sorted(cuts); spans = [(cuts[i], cuts[i + 1]) for i in range(0, len(cuts), 2)]
    for a, b in spans:
        if b - a < seg: continue
        st = sorted(set([a, b] + [s for s in lamps if a < s < b])); stations = []
        for s0, s1 in zip(st[:-1], st[1:]):
            n = max(1, int(round((s1 - s0) / seg))); stations += [s0 + (s1 - s0) * i / n for i in range(n)]
        stations.append(b)
        for s0, s1 in zip(stations[:-1], stations[1:]):
            g0 = .34 if (s0 in lamps or s0 in (a,)) else 0.0; g1 = .34 if (s1 in lamps or s1 in (b,)) else 0.0      # stop at a plinth / a jar
            p0, d0 = poly_at(pts, s0 + g0); p1, _ = poly_at(pts, s1 - g1)
            put(unit, block_of(s0), 'conduit', p0[0], p0[1], yawdeg(p1 - p0), cls, x2=p1[0], z2=p1[1], col=False)
        if ends:
            for s in (a, b): p, d = poly_at(pts, s); put(unit, block_of(s), 'junction', p[0], p[1], yawdeg(d), 'junction', margin=CFG['conduit']['jarMargin'])
    for s, k in lamps.items():
        if any(a <= s <= b for a, b in gaps): continue
        p, d = poly_at(pts, s); put(unit, block_of(s), k, p[0], p[1], yawdeg(d), 'lamp_street')


def offset_line(pts, lat):
    out = []
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]); d = d / np.linalg.norm(d); out.append(p + np.array([d[1], -d[0]]) * lat)      # + = right of travel
    return np.array(out)


# ------------------------------------------------------------------ (a) streets
def streets():
    c = CFG['streets']; Y = c['yukjo']; ax = H['scale']['axis_x']; sites = {s['id']: s for s in H['sites']}
    gates_e = [sites[i]['z'] for i in ('yukjo_e0', 'yukjo_e1', 'yukjo_e2')]; gates_w = [sites[i]['z'] for i in ('yukjo_w0', 'yukjo_w1', 'yukjo_w2')]
    for side, gz in ((1, gates_e), (-1, gates_w)):
        x = ax + side * Y['lat']; pts = np.array([[x, Y['z0']], [x, Y['z1']]]); lamps = {round(z - Y['z0'], 2): 'lamp_formal' for z in Y['lampZ']}
        conduit_run('Streets', lambda s: 'Yukjo_%d' % int(s // 75), pts, [(z - Y['z0'] - Y['gateGap'], z - Y['z0'] + Y['gateGap']) for z in gz], lamps=lamps)
    for k, (cands, yaw) in (('gauge', (Y['gauge'], 0)), ('pungi', (Y['pungi'], 0))):
        for x, z in cands:
            if put('Streets', 'Yukjo_0', k, x, z, yaw, k, fit=2.0): break
    for rid, R in c['routes'].items():
        _, pts, half = next(r for r in ROUTES if r[0] == rid); L = plen(pts); latA = (half + R['kerb']) * R['sideA']; a0, a1 = R.get('from', 4.0), L - R.get('to', 4.0)
        A = offset_line(pts, latA); LA_ = plen(A); f = LA_ / L; blk = lambda s, R=R, rid=rid: '%s_%d' % (R.get('block', rid), int(s // R.get('blockM', c['blockM'])))
        if R.get('conduit', True):
            lamps = {round(s * f, 2): ('lamp_street' if i % R.get('litEveryA', 1) == 0 else 'lamp_street_off') for i, s in enumerate(np.arange(a0 + R['every'] / 2, a1 - 2, R['every']))}
            sub = [poly_at(A, s)[0] for s in np.arange(a0 * f, a1 * f + .01, 3.0)]; conduit_run('Streets', blk, np.array(sub), lamps={round(s - a0 * f, 2): k for s, k in lamps.items() if s - a0 * f > 1})
        else:
            for i, s in enumerate(np.arange(a0 + R['every'] / 2, a1 - 2, R['every'])):
                p, d = poly_at(A, s * f); put('Streets', blk(s), 'lamp_street' if i % R.get('litEveryA', 1) == 0 else 'lamp_street_off', p[0], p[1], yawdeg(d), 'lamp_street')
        if R.get('hooks', True):      # the other kerb: timber hook lamps, staggered
            Bl = offset_line(pts, -(half + R['kerb'] + .2) * R['sideA']); fb = plen(Bl) / L
            for i, s in enumerate(np.arange(a0 + R['every'], a1 - 2, R['every'])):
                p, d = poly_at(Bl, s * fb); put('Streets', blk(s), 'lamp_hook' if i % R.get('litEveryB', 2) == 0 else 'lamp_hook_off', p[0], p[1], yawdeg(d) + 90 * R['sideA'], 'lamp_hook')
    P = c['jonggak']
    for cand in P['sibo']:
        if put('Streets', 'Jonggak', 'sibo', cand[0], cand[1], cand[2], 'instrument', fit=2.5): break
    for x, z in P['lamps']: put('Streets', 'Jonggak', 'lamp_street', x, z, 0, 'lamp_street', fit=1.5)
    for i, w_ in enumerate(TA['wells']):
        if w_['zone'] not in c['richWells']: continue
        x, _, z = w_['at']; put('Streets', 'Wells_' + w_['zone'], 'pump', x + 1.9, z + .4, 90, 'pump', fit=1.6); put('Streets', 'Wells_' + w_['zone'], 'lamp_gate' if i % 2 == 0 else 'lamp_gate_off', x - 1.7, z - 1.2, 0, 'lamp_well', fit=1.6)


# ------------------------------------------------------------------ (b) palace + offices
def palace():
    c = CFG['palace']; ax = H['scale']['axis_x']; ab = H['p2']['as_built']
    for x, z in c['woldaePlates']: put('Palace', 'Gwanghwamun', 'plate', ax + x, z, 0, 'plate', col=False)
    for x, z in c['woldaeLamps']: put('Palace', 'Gwanghwamun', 'lamp_formal', ax + x, z, 0, 'lamp_palace')
    for x, z, yaw in c['gwangBoxes']: put('Palace', 'Gwanghwamun', 'gatebox', ax + x, z, yaw, 'gatebox', fit=2.0)
    for x, z in c['heungLamps']: put('Palace', 'Heungnyemun', 'lamp_formal', ax + x, z, 0, 'lamp_palace', fit=1.0)
    for x, z in c['heungPlates']: put('Palace', 'Heungnyemun', 'plate_b', ax + x, z, 0, 'plate', col=False)
    for x, z in c['joLamps']: put('Palace', 'Geunjeong', 'lamp_formal', ax + x, z, 0, 'lamp_palace', fit=1.0)
    for x, z in c['joPlates']: put('Palace', 'Geunjeong', 'plate', ax + x, z, 0, 'plate', col=False)
    for x, z, y, yaw in c['deumeuPumps']: put('Palace', 'Geunjeong', 'pump', ax + x, z, yaw, 'fireward', y=y, fit=1.2)
    for name, sets in c['offices'].items():
        g = np.array(ab[name + '_Daemun']['p'])[[0, 2]]; hall = next(v for k, v in ab.items() if k.startswith(name + '_') and not k.endswith('Daemun') and not k.endswith('Haengnang')); hp = np.array(hall['p'])[[0, 2]]
        d = (hp - g) / np.linalg.norm(hp - g); r = np.array([d[1], -d[0]]); yaw = yawdeg(-d); y = ab[name + '_Daemun']['p'][1]
        put('Offices', 'Office_' + c['officeRow'][name], 'plate_b', *(g - d * 2.6), yaw, 'plate', y=y, col=False)
        for k, along, side, dyaw in sets:
            p = g + d * along + r * side; put('Offices', 'Office_' + c['officeRow'][name], k, p[0], p[1], yaw + dyaw, 'office_' + k.replace('_off', ''), y=y, fit=2.5, need_clear=False)


# ------------------------------------------------------------------ (c) market
def market():
    c = CFG['market']
    shops = {s_['id']: s_ for s_ in TA['shop_fronts']}; allp = np.vstack([r[1] for r in ROUTES])
    used = set()
    for k, ids in c['stallShops'].items():      # a trade stands in front of a shop front that has no goods table (the shop's own wares), long side to the street
        for sid in ids:
            if sid in used: continue
            x, y, z = shops[sid]['at']; o = np.array([x, z]); near = min((a_ + (b_ - a_) * float(np.clip((o - a_) @ (b_ - a_) / max(float((b_ - a_) @ (b_ - a_)), 1e-9), 0, 1)) for _, pts_, _ in ROUTES for a_, b_ in zip(pts_[:-1], pts_[1:])), key=lambda q: float(np.linalg.norm(q - o))); n_ = (near - o) / np.linalg.norm(near - o); t_ = np.array([n_[1], -n_[0]]); done = False
            for out in (c['stallOut'],):
                for dt in (0, 2.2, -2.2, 3.6, -3.6, 5.0, -5.0):
                    p = o + n_ * out + t_ * dt
                    if not done and clear(p[0], p[1], 0.2) >= 0 and put('Market', 'Market_Trades', k, p[0], p[1], yawdeg(n_), 'trade_' + k, y=y, fit=0.0, need_clear=False):
                        REPORT.setdefault('trades', {})[k] = [sid, round(p[0], 1), round(p[1], 1), round(float(n_[0]), 2), round(float(n_[1]), 2)]; done = True; used.add(sid)
            if done: break
    for i, s in enumerate(TA['stalls']):
        if i % c['tapEvery']: continue
        x, _, z = s['at']; put('Market', 'Market_Taps', 'tap', x + .9, z + .9, 0, 'tap', fit=1.0, col=False)
    for t in TA['taverns']:
        x, _, z = t['at']; f = fwd(t['yaw']); r = right(t['yaw']); p = np.array([x, z]) + f * 1.4 + r * 2.2; put('Market', 'Market_Trades', 'brazier', p[0], p[1], t['yaw'], 'brazier', fit=2.0)


# ------------------------------------------------------------------ (d) walls + gates (D308-79: restrained; nothing with a collider on the wall top)
def walls():
    c = CFG['walls']
    for g in H['gates']:
        G = c['gates'].get(g['id'])
        if not G: continue
        o = np.array([g['x'], g['z']]); inw = -fwd(g['yaw']); r = right(g['yaw']); blk = 'Gate_' + g['id'][5:]; lit = G.get('lit', False); lamp = 'lamp_formal' if lit else 'lamp_formal_off'
        for sd in (-1, 1): p = o + inw * G.get('in', 10.0) + r * sd * G.get('half', 5.5); put('Walls', blk, lamp, p[0], p[1], g['yaw'], 'lamp_gate', fit=2.0)
        p = o + inw * (G.get('in', 10.0) - 3.5) + r * (G.get('half', 5.5) - .9); put('Walls', blk, 'gatebox', p[0], p[1], g['yaw'] + 180, 'gatebox', fit=2.0)
        if G.get('plate', True): p = o + inw * (G.get('in', 10.0) - 5.0); put('Walls', blk, 'plate', p[0], p[1], g['yaw'], 'plate', col=False)
        if 'beacon' in G: p = o + inw * (G.get('in', 10.0) + 3.0) - r * (G.get('half', 5.5) + 3.0); put('Walls', blk, 'beacon' if G['beacon'] else 'beacon_off', p[0], p[1], 0, 'beacon', fit=3.0)
        if G.get('out'):
            for sd in (-1, 1): p = o - inw * G['out'] + r * sd * 6.0; put('Walls', blk, 'lamp_formal' if lit else 'lamp_formal_off', p[0], p[1], g['yaw'], 'lamp_gate', fit=2.0)
    city = J('city308.json'); chi = [(it['p'], it['yaw']) for u in city['units'] for gr in u['groups'] for it in gr['items'] if it['a'] == 'mesh:Chi']
    for i, (p, yaw) in enumerate(chi):
        k = c['chi'][i % len(c['chi'])]
        if not k: continue
        a = math.radians(yaw); lz = c['chiZ']; x = p[0] + lz * math.sin(a); z = p[2] + lz * math.cos(a)
        put('Walls', 'Chi_%d' % i, k, x, z, yaw + 180, 'wall_' + k, y=p[1] + c['wallTop'], mode='f', s=c['chiScale'], col=False)


# ------------------------------------------------------------------ (e) housing
def housing():
    c = CFG['housing']
    for g in LA['gates']:
        x, _, z = g['at']; f = fwd(g['yaw']); r = right(g['yaw'])
        for sd, k in ((-1, 'lamp_gate'), (1, 'lamp_gate_off')): p = np.array([x, z]) + f * 1.3 + r * sd * 1.9; put('Housing', 'Bukchon', k, p[0], p[1], g['yaw'], 'lamp_house', fit=1.2)
        p = np.array([x, z]) + f * 1.0 + r * 3.4; put('Housing', 'Bukchon', 'chimney', p[0], p[1], g['yaw'], 'chimney', fit=1.5)
    n = {}
    for w_ in TA['wells'] + LA['wells']:
        zone = w_['zone']
        if zone not in c['poorWells']: continue
        i = n.get(zone, 0); n[zone] = i + 1; x, _, z = w_['at']
        if i % c['patchEvery'] == 0: put('Housing', 'Poor_' + zone, 'oredust', x + 1.6, z - .8, 40 * i, 'patched', fit=1.2, col=False)
        if i % c['lampEvery'] == 0: put('Housing', 'Poor_' + zone, 'lamp_gate' if i == 0 else 'lamp_gate_off', x - 1.8, z + 1.0, 0, 'lamp_well', fit=1.5)


def main():
    streets(); palace(); market(); walls(); housing()
    blocks = list(BLOCKS.values()); lit = sum(i['lit'] for b in blocks for i in b['inst']); tri = {k: (K.build(k, 0)[1].tris(), K.build(k, 1)[1].tris()) for k in K.KIT}
    cap = CFG['litCap']      # too many lit lamps: unlight hook lamps first, then every other street lamp (deterministic)
    for kind in ('lamp_hook', 'lamp_street'):
        pool = [i for b in blocks for i in b['inst'] if i['k'] == kind]
        for j, i in enumerate(pool):
            if lit <= cap: break
            if j % 2 == 1: i['k'] = kind + '_off'; i['lit'] = 0; lit -= 1
    for b in blocks:
        xs = [i['x'] for i in b['inst']]; zs = [i['z'] for i in b['inst']]; ext = max(max(xs) - min(xs), max(zs) - min(zs), 6.0)
        b['o'] = [round((max(xs) + min(xs)) / 2, 2), round(min(i['y'] for i in b['inst']), 2), round((max(zs) + min(zs)) / 2, 2)]; b['cut'] = round(float(np.clip(0.4 * ext / 90.0, 0.1, 0.4)), 3); b['cull'] = 0.012
    cls = {}; t0 = t1 = 0
    for b in blocks:
        for i in b['inst']: cls[i['cls']] = cls.get(i['cls'], 0) + 1; t0 += tri[i['k']][0]; t1 += tri[i['k']][1]
    plan = dict(version=CFG['version'], core=CFG['core'], materials=CFG['materials'], kit=K.dump(), blocks=blocks)
    (S / 'arcane308.json').write_text(json.dumps(plan, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    REPORT.update(version=CFG['version'], blocks=len(blocks), instances=sum(len(b['inst']) for b in blocks), lit=lit, classes=dict(sorted(cls.items())), lod0_tris=t0, lod1_tris=t1,
                  units=sorted({b['unit'] for b in blocks}), kit_tris={k: v for k, v in tri.items()})
    (S / 'Out').mkdir(exist_ok=True); (S / 'Out/arcane_plan_report.json').write_text(json.dumps(REPORT, ensure_ascii=False, indent=1), encoding='utf-8')
    print('blocks', len(blocks), 'instances', REPORT['instances'], 'lit', lit, '| LOD0 tris', t0, 'LOD1', t1, '| skipped for walkers', len(REPORT['skipped']))
    print(json.dumps(REPORT['classes'], ensure_ascii=False)); print('skipped', REPORT['skipped'][:12])
    print({b['id']: len(b['inst']) for b in blocks})


if __name__ == '__main__':
    sys.exit(main())
