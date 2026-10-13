"""SPEC-WORLD-HANYANG-308 phase 1c: the phase-1b plan (hanyang308_city.py: same wall line, same collider boxes) with
pack gate bases + towers (Art/World/PackLite308 fortress = Hwaseong Janganmun), wall guards, low-look stone bridges v2, street surfaces.
python Tools/Art/hanyang308_city_p1c.py [--proof]  ->  Tools/Unity/Stage308_hanyang/city308.json (+ Out/city_report.json)"""
import json, math, sys
sys.path.insert(0, 'Tools/Art')
src = open('Tools/Art/hanyang308_city.py', encoding='utf-8').read()
exec(src[:src.index("if __name__ == '__main__':")])
exec((ST / 'city308_sites.py').read_text(encoding='utf-8'))

# ---- gate kits (Fort_GateBase: 49.9 x 9.48 x 13.96 m, walk 7.5 m, body top +-16.3 m, stair wedges to +-24.9 m, door plane z -4.5, arch clear 5.2 x 6.1 m) ----
GB = dict(half=16.3, wing=24.9, walk=7.5, hd=6.5, door_z=-4.5, aw=5.2, ah=6.1)
def kit(s, sy, munru):
    return dict(s=(s, sy, s), body=GB['walk'] * sy, aw=GB['aw'] * s, ah=GB['ah'] * sy, w=2 * GB['half'] * s, d=2 * GB['hd'] * s, shift=GB['hd'] * s - 2.05, ms=munru)
KIT = dict(large=kit(1.0, 1.0, (0.85, 0.95, 0.85)), small=kit(0.75, 0.8667, (0.65, 0.8, 0.68)))
NEWGATES = dict(gate_donui=dict(kit='large', seg=('W2', 'W3'), open=True), gate_sukjeong=dict(kit='small', seg=('N3', 'N4'), open=False), gate_hyehwa=dict(kit='small', seg=('S', 'N2'), open=False))
GUARD_H = 3.0    # invisible guard above the parapet: walk + 1.2 .. walk + 4.2 m (D305 blocking height 4.0 m)


def box(L, n, lx, y, lz, sx, sy, sz, yaw, pitch=0.0):
    x, z = L(lx, lz); return item('col', x, y, z, yaw, (sx, sy, sz), n=n, pitch=pitch)


def gate_items(gid, k, cx, cz, apron, yaw, d, nin, is_open, wall_d=None):
    """the pieces of one gate in its frame (lx along the wall, lz inward); base centre shifted inward by k['shift'] so its outer face stands 0.5 m proud of the wall face"""
    its = []; s, sy, _ = k['s']; sh = k['shift']; top = apron + k['body']; L = lambda lx, lz: (cx + d[0] * lx + nin[0] * lz, cz + d[1] * lx + nin[1] * lz)
    bx, bz = L(0, sh); half = GB['half'] * s; wing = GB['wing'] * s; hd = GB['hd'] * s; aw, ah = k['aw'], k['ah']
    its.append(item('prefab:' + ('Fort_GateBaseOpen' if is_open else 'Fort_GateBase'), bx, apron, bz, yaw, k['s'], '', n='Base'))
    its.append(item('prefab:Fort_Munru1', bx, top, bz, yaw, k['ms'], '', n='Munru'))
    mw, mh, md = 22.0 * k['ms'][0], 3.2 * k['ms'][1], 7.8 * k['ms'][2]; its.append(box(L, 'MunruCol', 0, top + mh / 2, sh, mw, mh, md, yaw))
    # colliders: two masses, lintel, shut blocker, stairs (inside only), guard over the outer parapet
    mwid = half - aw / 2 - 0.15
    for sg in (-1, 1): its.append(box(L, 'BaseCol', sg * (aw / 2 + 0.15 + mwid / 2), apron + k['body'] / 2 - 1.0, sh, mwid, k['body'] + 2.0, 2 * hd, yaw))
    its.append(box(L, 'LintelCol', 0, apron + (ah + 0.25 + k['body']) / 2, sh, aw + 0.3, k['body'] - ah - 0.25, 2 * hd, yaw))
    its.append(box(L, 'GuardCol', 0, top + 1.2 + GUARD_H / 2, sh - hd + 0.4, 2 * half + 1.0, GUARD_H, 0.6, yaw))
    for sg in (-1, 1):
        its.append(box(L, 'ParapetCol', sg * (half - 0.35) * 0 + 0, top + 0.95, sh + hd - 0.35, 2 * half, 1.9, 0.6, yaw)) if sg < 0 else None
        run = wing - half; hyp = math.hypot(run, k['body']); ang = math.degrees(math.atan2(k['body'], run)); z0 = 2.6; z1 = sh + hd; wz = z1 - z0
        if wz > 1.5:
            px, pz = L(sg * (half + run / 2), (z0 + z1) / 2); c = math.cos(math.radians(ang))
            its.append(item('col', px, apron + k['body'] / 2 - 0.25 * c, pz, yaw + 90, (wz, 0.5, hyp), n='StairCol', pitch=round(ang * sg, 2)))
            its.append(box(L, 'StairFill', sg * (half + run * 0.25), apron + k['body'] * 0.25 - 0.5, (z0 + z1) / 2, run * 0.5, k['body'] * 0.5 + 1.0, wz, yaw))
    gs = [g(*L(u * wing, sh + w * hd)) for u in (-1, -.6, -.3, 0, .3, .6, 1) for w in (-1, 0, 1)]; drop = apron - min(gs)
    gs += [g(*L(u * wing, sh + w * (hd + 0.4))) for u in np.linspace(-1.02, 1.02, 25) for w in (-1, -0.5, 0, 0.5, 1)]; drop = max(apron - min(gs), 0.0) + 1.5
    fx, fz = L(0, sh); its.append(item('mesh:Block', fx, apron - drop, fz, yaw, (2 * wing + 0.2, drop + 0.02, 2 * hd + 0.2), '', n='Foot'))
    # flagstone apron through the passage (6 cm proud of the gate level)
    ax, az = L(0, sh); its.append(item('mesh:Block', ax, apron - 3.0, az, yaw, (aw + 4.0, 3.06, 2 * hd + 4.0), '', n='Apron'))   # 2 m beyond each face, 3 m down so it never overhangs falling ground
    zd = sh + GB['door_z'] * s
    if is_open:
        for sg in (-1, 1):
            x, z = L(sg * (aw / 2 - 0.1), zd + 0.2); its.append(item('mesh:Leaf', x, apron + 0.05, z, yaw - 90, (aw / 2 - 0.1, ah - 1.0, 1), 'box', n='Leaf_open'))
            for hy in (0.15, 0.4, 0.65, 0.9): its.append(item('mesh:Band', x, apron + 0.05 + (ah - 1.0) * hy, z, yaw - 90, (aw / 2 - 0.1, 0.12, 1), '', n='Band'))
    else:
        its.append(box(L, 'Blocker', 0, apron + ah / 2, zd, aw + 1.0, ah + 0.6, 1.2, yaw))
    rep['gates'][gid] = dict(x=round(cx, 2), z=round(cz, 2), apron=apron, yaw_base=round(yaw, 2), nin=[round(nin[0], 4), round(nin[1], 4)], base='Fort_GateBase' + ('Open' if is_open else ''), base_scale=list(k['s']), base_shift_in=round(sh, 2),
                           body_w=round(2 * half, 1), full_w=round(2 * wing, 1), base_d=round(2 * hd, 1), walk_level=round(top, 2), walk_above_apron=round(k['body'], 2), arch_clear=[round(aw, 2), round(ah, 2)], munru='Fort_Munru1', munru_scale=list(k['ms']),
                           munru_roof=[round(28.9 * k['ms'][0], 1), round(9.5 * k['ms'][1], 1), round(14.1 * k['ms'][2], 1)], open=is_open, ground_under_base=[round(min(gs), 2), round(max(gs), 2)], foot_block_m=round(max(drop, 0), 2))
    if max(gs) - apron > 0.4: rep['warn'].append('%s: ground %.2f above the apron under the base' % (gid, max(gs) - apron))
    return its


def gate_group(gid, ng, k, cx, cz, apron, yaw, d, nin):
    return dict(name='Gate_' + gid[5:], cull=0.004, items=gate_items(gid, k, cx, cz, apron, yaw, d, nin, ng['open']))


# ---- 소의문: a shut gate set against the outer face of the kept wall (no change to the kept chunk or its collider) ----
def soui():
    G = GATE['gate_soui']; k = KIT['small']; cx, cz = 1650.0, 2648.0; d = (0.0, -1.0); nin = (1.0, 0.0); yaw = frame((cx, cz), (cx + d[0], cz + d[1]))[3]
    off = 2.6 + 2.05 + 0.2      # kept wall half thickness (EST 2.6) -> the base's back face meets the wall face
    cx2, cz2 = cx - nin[0] * off, cz - nin[1] * off; apron = round(max(g(cx2 - nin[0] * o, cz2 + u) for o in (0, 4, 8) for u in (-10, 0, 10)), 2)
    its = gate_items('gate_soui', k, cx2, cz2, apron, yaw, d, nin, False)
    # no way round onto the stairs from outside: the stair wedges stand outside the wall here, so they get blockers instead of walkable ramps
    its = [i for i in its if i['n'] not in ('StairCol',)]
    half = GB['half'] * k['s'][0]; wing = GB['wing'] * k['s'][0]; L = lambda lx, lz: (cx2 + d[0] * lx + nin[0] * lz, cz2 + d[1] * lx + nin[1] * lz)
    for sg in (-1, 1): its.append(box(L, 'WingCol', sg * (half + wing) / 2, apron + 4.0, k['shift'], wing - half, 12.0, 2 * GB['hd'] * k['s'][0], yaw))
    rep['gates']['gate_soui']['note'] = 'set against the outer face of kept chunk CapitalWall_3_16; kept wall thickness EST'
    return dict(name='Gate_soui', cull=0.004, items=its)


_extra0 = extra_gates
def extra_gates(barrier):
    grs = _extra0(barrier)
    grs.append(soui())
    # guards + join blockers are added by guards() after the wall groups exist
    return grs


def guards(units):
    """invisible guard above every parapet / corner / bastion of the new wall, and a stop across the walk before each join tower"""
    wall = [u for u in units if u['name'] == 'Hanyang308_Wall'][0]; its = []; n = 0
    for gr in wall['groups']:
        for it in gr['items']:
            yaw = math.radians(it['yaw']); dx, dz = math.cos(yaw), -math.sin(yaw); nx, nz = -dz, dx   # +X of the module, inward normal
            if it['a'] in ('mesh:W1', 'mesh:W2', 'mesh:W3'):
                m = int(it['a'][-1]); Lm = m * TA * it['s'][0]; x = it['p'][0] + dx * Lm / 2 - nx * 1.62; z = it['p'][2] + dz * Lm / 2 - nz * 1.62
                its.append(item('col', x, it['p'][1] + WH + PAR + GUARD_H / 2, z, it['yaw'], (Lm + 0.3, GUARD_H, 0.6), n='Guard')); n += 1
            elif it['a'] == 'mesh:Chi':
                x = it['p'][0] - nx * 5.0; z = it['p'][2] - nz * 5.0; its.append(item('col', x, it['p'][1] + WH + 2.15, z, it['yaw'], (8.4, 4.1, 7.4), n='GuardChi'))
            elif it['a'] == 'mesh:Drum':
                its.append(item('col', it['p'][0], it['p'][1] + WH + PAR + GUARD_H / 2, it['p'][2], 0, (6.8, GUARD_H, 6.8), n='GuardDrum'))
            elif it['a'] == 'mesh:Pier':
                its.append(item('col', it['p'][0], it['p'][1] + 1.0, it['p'][2], it['yaw'], (9.6, 9.0, 9.6), n='JoinStop'))
    wall['groups'].append(dict(name='Guards', cull=0.0, items=its)); rep['wall']['guards'] = n; rep['wall']['guard_span'] = 'walk + 1.2 .. walk + 4.2 m'


# ---- bridges v2 ----
BR = [dict(id='songgi', L=98.0, w=6.4, pierH=6.5, step=4.75, camber=0.35, rail=0, cols=4, a=(1752.0, 2842.5), b=(1752.0, 2940.5), deck=48.8, ramp=3.0, water=47.3),
      dict(id='yukjo', L=86.0, w=10.0, pierH=19.5, step=4.8, camber=0.45, rail=1, cols=5, a=(1964.5, 3160.5), b=(2010.0, 3081.7), deck=66.0, ramp=4.0, water=51.6)]
def bridges():
    grs = []
    for b in BR:
        (x0, z0), (x1, z1) = b['a'], b['b']; L, d, nin, yaw = frame((x0, z0), (x1, z1)); psi = yaw + 90; cx, cz = (x0 + x1) / 2, (z0 + z1) / 2; hw = b['w'] / 2 - 0.2; its = []
        its.append(item('mesh:Br_' + b['id'], cx, b['deck'], cz, psi, (1, 1, L / b['L']), '', n='Deck'))
        its.append(item('col', cx, b['deck'] - 0.4 + b['camber'] * 0.5, cz, psi, (b['w'], 0.8 + b['camber'], L), n='DeckCol'))
        for sgn in (-1, 1): its.append(item('col', cx + nin[0] * sgn * hw, b['deck'] + 0.75, cz + nin[1] * sgn * hw, psi, (0.3, 1.5, L), n='RailCol'))
        ends = {}
        for e, (x, z, sgn) in enumerate(((x0, z0, -1), (x1, z1, 1))):
            gl = g(x + d[0] * sgn * b['ramp'], z + d[1] * sgn * b['ramp']); rise = b['deck'] - gl; pitch = math.degrees(math.atan2(rise, b['ramp'])) * sgn; ends['end%d_ground_at_ramp_foot' % e] = round(gl, 2)
            its.append(item('mesh:Block', x + d[0] * sgn * b['ramp'] / 2, (b['deck'] + gl) / 2 - 0.5 * math.cos(math.radians(pitch)), z + d[1] * sgn * b['ramp'] / 2, psi, (b['w'], 0.5, math.hypot(b['ramp'], rise) + 0.4), 'box', n='Ramp%d' % e, pitch=round(pitch, 2)))
        prof = [g(x0 + d[0] * s_, z0 + d[1] * s_) for s_ in np.arange(0, L + .1, 2.0)]
        rep['bridges'][b['id']] = dict(a=b['a'], b=b['b'], length=round(L, 1), width=b['w'], deck=b['deck'], water=b['water'], deck_above_water=round(b['deck'] - b['water'], 2), rows=round(b['L'] / b['step']) + 1, posts_per_row=b['cols'], rail=b['rail'], yaw=round(psi % 360, 1), bed_min=round(min(prof), 2), **ends)
        grs.append(dict(name='Bridge_' + b['id'], cull=0.004, items=its))
    return grs


# ---- street surfaces: draped strips over the graded ground (soft edges in vertex alpha), kerb stones on the main streets ----
LIFT = 0.09
EXCL = [((2158.0, 3300.0), 38.0)]   # the kept #295 crossing of the Jongno route (Watershed295_Crossings, protected): the road line dips under it
def strip(name, line, w, kind, ribs, items, kerb=False):
    """line = [(x, z)] ; stations every 2 m; split where a bridge or water interrupts"""
    pts = []
    for (xa, za), (xb, zb) in zip(line[:-1], line[1:]):
        Ls = math.hypot(xb - xa, zb - za); n = max(1, round(Ls / 2.0))
        for q in range(n): pts.append((xa + (xb - xa) * q / n, za + (zb - za) * q / n))
    pts.append(line[-1]); runs = [[]]
    def wet(x, z):
        if WET[int(round(z / 4)), int(round(x / 4))]: return True
        for (ex, ez), er in EXCL:
            if math.hypot(x - ex, z - ez) < er: return True
        for b in BR:
            (x0, z0), (x1, z1) = b['a'], b['b']; L, d, nin, _ = frame((x0, z0), (x1, z1)); s = (x - x0) * d[0] + (z - z0) * d[1]; t = (x - x0) * nin[0] + (z - z0) * nin[1]
            if -1.0 < s < L + 1.0 and abs(t) < b['w']: return True
        return False
    for p in pts:
        if wet(*p):
            if runs[-1]: runs.append([])
        else: runs[-1].append(p)
    total = 0.0
    for ri, run in enumerate([r for r in runs if len(r) >= 3]):
        offs = [(-w / 2 - 0.9, 0.0), (-w / 2, 0.8), (-w / 4, 1.0), (0.0, 1.0), (w / 4, 1.0), (w / 2, 0.8), (w / 2 + 0.9, 0.0)]
        if w > 20: offs = [(-w / 2 - 1.2, 0.0), (-w / 2, 0.85)] + [(-w / 2 + w * q / 8, 1.0) for q in range(1, 8)] + [(w / 2, 0.85), (w / 2 + 1.2, 0.0)]
        def rib(nm, cross, alpha, kd):
            v = []; x0, z0 = run[0]; y0 = g(x0, z0)
            for i, (x, z) in enumerate(run):
                a = run[max(i - 1, 0)]; b = run[min(i + 1, len(run) - 1)]; tx, tz = b[0] - a[0], b[1] - a[1]; tl = math.hypot(tx, tz) or 1; nx, nz = -tz / tl, tx / tl
                for (o, dy) in cross: px, pz = x + nx * o, z + nz * o; v += [round(px - x0, 3), round(g(px, pz) + dy - y0, 3), round(pz - z0, 3)]
            ribs.append(dict(name=nm, mat=kd, n=len(run), m=len(cross), v=v, a=alpha)); items.append(item('mesh:Rib_' + nm, x0, y0, z0, 0, col=kd, n=nm))
        rib('%s_%d' % (name, ri), [(o, LIFT) for o, _ in offs], [a for _, a in offs], kind)
        if kerb:
            for sg, tag in ((-1, 'kl'), (1, 'kr')):
                o = sg * (w / 2 + 0.55); rib('%s_%d_%s' % (name, ri, tag), [(o - 0.22, -0.15), (o - 0.22, 0.24), (o + 0.22, 0.24), (o + 0.22, -0.15)], [1, 1, 1, 1], 'kerb')
        total += 2.0 * (len(run) - 1)
    return total


def streets():
    ribs = []; groups = []; lens = {}
    for r in J['roads']:
        its = []; main = r['id'] in ('rd_jongno', 'rd_saemunan', 'rd_yukjo_link', 'rd_namdaemun_w')
        if r['id'] == 'rd_namdaemun_e': continue      # the same line as rd_namdaemun_w
        lens[r['id']] = strip(r['id'][3:], [(p[0], p[1]) for p in r['line']], r['width'], 'road', ribs, its, kerb=main)
        if its: groups.append(dict(name='Street_' + r['id'][3:], cull=0.02, items=its))
    its = []; lens['yukjo_street'] = strip('yukjo', [(1959.0, 3176.0), (1959.0, 3322.0)], 30.0, 'road', ribs, its, kerb=True)
    groups.append(dict(name='Street_yukjo', cull=0.01, items=its))
    rep['streets'] = dict(lengths_m=lens, total_m=round(sum(lens.values()), 1), ribbons=len(ribs), lift_m=LIFT, method='draped strips, GroundFit299 town_soft299 material (queue 2050 blend, depth / shadow passes off), kerb stones on the main streets')
    return dict(name='Hanyang308_Streets', groups=groups), ribs


if __name__ == '__main__':
    plan, barrier = build()
    guards(plan['units'])
    su, ribs = streets(); plan['units'].append(su); plan['ribbons'] = ribs; plan['version'] = '308.hanyang.p1c'
    plan['bridges'] = [dict(id=b['id'], L=b['L'], w=b['w'], pierH=b['pierH'], step=b['step'], camber=b['camber'], rail=b['rail'], cols=b['cols']) for b in BR]
    plan['prefabs'] = sorted({it['a'][7:] for u in plan['units'] for gr in u['groups'] for it in gr['items'] if it['a'].startswith('prefab:')})
    (ST / 'city308.json').write_text(json.dumps(plan, ensure_ascii=False), encoding='utf-8')
    if '--proof' in sys.argv:
        rep['proof_all_shut'] = proof(barrier, set()); rep['proof_donui_open'] = proof(barrier, {'gate_donui'})
    (ST / 'Out/city_report.json').write_text(json.dumps(rep, ensure_ascii=False, indent=1), encoding='utf-8')
    w = rep['wall']; print('wall', {k: w[k] for k in ('length', 'ta', 'modules', 'out_min', 'in_min', 'step_max', 'drums', 'chi', 'piers', 'guards') if k in w})
    for k, v in rep['gates'].items(): print(k, {a: b for a, b in v.items() if a in ('x', 'z', 'apron', 'walk_level', 'arch_clear', 'ground_under_base', 'foot_block_m', 'centre_offset_from_plan_m', 'munru_roof', 'body_w')})
    for k, v in rep['bridges'].items(): print(k, v)
    print('streets', rep['streets']['lengths_m'], rep['streets']['total_m'], rep['streets']['ribbons'])
    for k in ('proof_all_shut', 'proof_donui_open'):
        if k in rep: print(k, rep[k])
    print('items', sum(len(gr['items']) for u in plan['units'] for gr in u['groups']), 'rays', len(plan['rays']), 'prefabs', plan['prefabs']); [print('WARN', x) for x in rep['warn']]
