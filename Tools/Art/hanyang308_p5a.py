"""SPEC-WORLD-HANYANG-308 phase 5a (§14): hero models + leftovers as a post-process of the phase-1c / phase-3 plans.
python Tools/Art/hanyang308_p5a.py   (reads Backup/20261013_p5a_pre/{city308,town308}.json = the p1c / p3 plans, writes city308.json + town308.json + hide308.json)
The palace part of phase 5a lives in hanyang308_palace.py itself (HERO = True). Never run city:meshes / town:meshes / town:blocks for this: no solid changes here.
Then: Hanyang308 city:apply:Gates | city:apply:Wall | town:apply:<unit> | hero:hide:off"""
import json, math, sys
sys.path.insert(0, 'Tools/Art')
import cliff308_base as B

ST = B.ROOT / 'Tools/Unity/Stage308_hanyang'; PRE = ST / 'Backup/20261013_p5a_pre'
H = B.load_height(B.PL / 'Stage/height_pn8.bytes')
g = lambda x, z: float(B.sample(H, x, z))
SIZE = {e['name']: e['bounds_unity'] for e in json.load(open(B.ROOT / 'Art/World/PackLite308/manifest.json', encoding='utf-8'))['items']}
rep = {}


def item(a, x, y, z, yaw=0.0, s=(1, 1, 1), col='', n='', pitch=0.0):
    return dict(n=n, a=a, col=col, p=[round(x, 3), round(y, 3), round(z, 3)], yaw=round(yaw % 360, 3), s=[round(float(v), 4) for v in s], pitch=round(pitch, 3))


# ===================================================================================== city: gate towers + 흥인지문
city = json.loads((PRE / 'city308.json').read_text(encoding='utf-8'))
MS = dict(small=(1.25, 1.15, 1.1), large=(1.5, 1.35, 1.4))   # Hero_Munru_3x2 (몸 9.4 x 5.4, 지붕 14.0 x 10.1, 높이 7.2) on the small / large 석축
swapped = []
for u in city['units']:
    for gr in u['groups']:
        for k, it in enumerate(gr['items']):
            if it['a'] != 'prefab:Fort_Munru1': continue
            ms = MS['large'] if 'donui' in gr['name'] else MS['small']
            it['a'] = 'prefab:Hero_Munru_3x2'; it['s'] = list(ms); top = it['p'][1]
            for c in gr['items']:
                if c['n'] == 'MunruCol': c['s'] = [round(9.4 * ms[0], 3), round(3.2 * ms[1], 3), round(5.4 * ms[2], 3)]; c['p'][1] = round(top + 1.6 * ms[1], 3)
            swapped.append((gr['name'], ms))
rep['munru_3x2'] = swapped
# 흥인지문: 중층 5칸 문루 on the kept #296 gate (stone top 86.7; old columns / beams / rafters / roof renderers are switched off by hero:hide) + 옹성 continued to a half-round, open to the north
HX, HZ, HTOP, HYAW = 2232.0, 3493.7, 86.7, 238.7
hs = (0.78, 0.85, 0.80)
dress = dict(name='Gate_heungin_dress', cull=0.004, items=[
    item('mesh:Block', HX, HTOP - 1.1, HZ, HYAW, (19.4, 1.12, 7.4), '', n='MunruDeck'),
    item('prefab:Hero_Munru_5x2', HX, HTOP, HZ, HYAW, hs, '', n='Munru')])
for u in city['units']:
    if u['name'] == 'Hanyang308_Gates': u['groups'] = [x for x in u['groups'] if x['name'] != dress['name']] + [dress]
    for gr in u['groups']:
        if gr['name'] != 'Wall_Ongseong_heungin': continue
        cx, cz, r = 2233.299, 3494.750, 18.045; last = gr['items'][-1]; added = []
        # NOT extended (tried 6 more pieces, 26..87 deg: they stood on the kept exit road capital_exit_2, which turns north right outside the gate - still p5_heungin_air_blocked) -> the quarter-round of phase 1b stays
        for k in range(1, 1):
            a = math.radians(15.7 + 11.0 * k); x, z = cx + r * math.cos(a), cz + r * math.sin(a)
            go = max(g(cx + (r + w) * math.cos(a), cz + (r + w) * math.sin(a)) for w in (0, 2.8, 4.2))
            y = max(last['p'][1], math.ceil((go + 4.6 - 6.5) / 0.25) * 0.25)
            gr['items'].append(item('mesh:W1', x, y, z, 247.5 - 11.0 * k, last['s'], 'box')); added.append((round(x, 1), round(z, 1), y, round(go, 1)))
        rep['ongseong_added'] = added
city['version'] = '308.hanyang.p5a'
(ST / 'city308.json').write_text(json.dumps(city, ensure_ascii=False), encoding='utf-8')
(ST / 'hide308.json').write_text(json.dumps(dict(keys=['Realm_Hwanggyeong/KoreanGateArchitecture/' + n for n in ['CAP_GATE_COLUMN_%d' % i for i in range(6)] + ['CAP_GATE_CROSSBEAM', 'CAP_GATE_RAFTER', 'CAP_GATE_ROOF']]), indent=1), encoding='utf-8')

# ===================================================================================== town: 종묘 정전 + yard-wall colliders
town = json.loads((PRE / 'town308.json').read_text(encoding='utf-8'))
FX = 0.835   # 19 + 3 + 3 칸을 지킨다: 칸 4.04 -> 3.37 m, 몸 84.3 m, 처마 끝 86.8 m (월대 88.0 m)
for u in town['units']:
    if u['name'] != 'Hanyang308_Jongmyo': continue
    for gr in u['groups']:
        if gr['name'] != 'Jeongjeon': continue
        old = [it for it in gr['items'] if it['a'].startswith('prefab:')][0]; cx, WT = old['p'][0], old['p'][1]; wz1 = 3280.4; hz = wz1 - 0.6 - 8.2
        its = [it for it in gr['items'] if it['a'].startswith('mesh:')]
        its.append(item('prefab:Hero_Jongmyo_Jeongjeon', cx, WT, hz, 0, (FX, 1, 1), n='Jongmyo_Jeongjeon'))
        xe = (50.5 + 1.2) * FX; its.append(item('col', cx, WT + 3.25, hz - 0.3, 0, (2 * xe, 6.5, 15.2), n='Body'))
        for sgn in (-1, 1): its.append(item('col', cx + sgn * 48.6 * FX, WT + 2.25, hz - 17.05, 0, (6.0 * FX + 0.4, 4.5, 18.3), n='Wolrang'))
        gr['items'] = its; rep['jeongjeon'] = dict(at=[cx, WT, hz], scale_x=FX, bay=round(4.04 * FX, 2), body=round(101 * FX, 1), eaves=round(104 * FX, 1))
# yard walls: one box per straight run of 4 m modules (same block, same yaw, consecutive along the run, tops within 0.7 m)
blocks = {b['id']: b for b in town['blocks']}; ncol = 0; nmod = 0; where = {}
for u in town['units']:
    for gr in u['groups']:
        gr['items'] = [it for it in gr['items'] if it['n'] != 'YardWall']
        for it in list(gr['items']):
            if not it['a'].startswith('prefab:TB_'): continue
            bl = blocks.get(it['a'][10:]);
            if bl is None: continue
            mods = [h for h in bl['houses'] if h['a'].startswith('Gen_Wall_')]; nmod += len(mods); runs = []
            for h in mods:
                sz = SIZE[h['a']]; L = sz['size'][0] * h['s'][0]; ht = sz['size'][1] * h['s'][1]; yw = math.radians(h['yaw']); dx, dz = math.cos(yw), -math.sin(yw)   # local +X in world
                done = False
                for r in runs:
                    if r['a'] != h['a'] or abs(r['yaw'] - h['yaw']) > 0.5: continue
                    ex, ez = r['x1'], r['z1']; sx, sz_ = h['p'][0] - dx * L / 2, h['p'][2] - dz * L / 2
                    if math.hypot(ex - sx, ez - sz_) < 0.45 and abs(r['y0'] - h['p'][1]) < 0.7 and abs(r['y1'] - (h['p'][1] + ht)) < 0.9:
                        r['x1'], r['z1'] = h['p'][0] + dx * L / 2, h['p'][2] + dz * L / 2; r['y0'] = min(r['y0'], h['p'][1]); r['y1'] = max(r['y1'], h['p'][1] + ht); done = True; break
                    ex, ez = r['x0'], r['z0']; sx, sz_ = h['p'][0] + dx * L / 2, h['p'][2] + dz * L / 2
                    if math.hypot(ex - sx, ez - sz_) < 0.45 and abs(r['y0'] - h['p'][1]) < 0.7 and abs(r['y1'] - (h['p'][1] + ht)) < 0.9:
                        r['x0'], r['z0'] = h['p'][0] - dx * L / 2, h['p'][2] - dz * L / 2; r['y0'] = min(r['y0'], h['p'][1]); r['y1'] = max(r['y1'], h['p'][1] + ht); done = True; break
                if not done: runs.append(dict(a=h['a'], yaw=h['yaw'], x0=h['p'][0] - dx * L / 2, z0=h['p'][2] - dz * L / 2, x1=h['p'][0] + dx * L / 2, z1=h['p'][2] + dz * L / 2, y0=h['p'][1], y1=h['p'][1] + ht, d=SIZE[h['a']]['size'][2] * h['s'][2]))
            for r in runs:
                L = math.hypot(r['x1'] - r['x0'], r['z1'] - r['z0']); y0 = r['y0'] - 0.5
                gr['items'].append(item('col', (r['x0'] + r['x1']) / 2, (y0 + r['y1']) / 2, (r['z0'] + r['z1']) / 2, r['yaw'], (L, r['y1'] - y0, max(0.45, r['d'])), n='YardWall')); ncol += 1
            where[u['name']] = where.get(u['name'], 0) + len(runs)
rep['yard_walls'] = dict(modules=nmod, boxes=ncol, by_unit=where)
town['version'] = '308.town.p5a'
(ST / 'town308.json').write_text(json.dumps(town, ensure_ascii=False), encoding='utf-8')
(ST / 'Out/p5a_report.json').write_text(json.dumps(rep, ensure_ascii=False, indent=1), encoding='utf-8')
print(json.dumps(rep, ensure_ascii=False)[:2500])
