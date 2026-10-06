# -*- coding: utf-8 -*-
"""RoadInn308 offline dry run (D308-16b 답 1 / D308-16c: 가도 주막, ops XI1-XI4 of relayout308_ext.json).
Pure Python: scene YAML + the content assets + the seal profile + the stage-1b height field + roadinn308.json.
Nothing in the Unity project is touched and the editor queue is never used.

  python Tools/Art/roadinn308_dry.py dry [--scene main|folk298|arch296|all] [--data <roadinn308.json>] [--out <dir>]
      per scene: what RoadInn308 content-apply / scene-apply / seal-apply would write and the design's post-conditions as
      numbers ("num <op> <name> <value> [ok|FAIL|info]"). A failed condition is listed under FAILED and the exit code is 2.
      A scene without the keeper point or without a scene_requires key is "skipped (not in this scene)", never a failure.
      -> <out>/roadinn308_dry_<alias>.txt (+ .json)   (default out: Tools/Unity/Stage308_relayout_ext/Dry)
  python Tools/Art/roadinn308_dry.py baseline [--write]
      the bytes the inn must leave alone, per content: sha of the Commissions entry of keeper.commission and of the keeper
      point without its Services. --write stores them (<out>/roadinn308_baseline.json); without it the current values are
      compared with the stored ones (run before the apply with --write, after it without).

Data: the live Art/World/Compact/Rebuild/CliffBoundary308/roadinn308.json when it exists, else the stage copy.
Limits [O]: terrain = the 4 m height grid (bilinear, ~0.1 m off the editor's colliders); colliders = BoxCollider of the scene
YAML (upright boxes; mesh / prefab-internal colliders are only listed); NavMesh, roof underside and sight lines are editor
probes (RoadInn308 probe:<scene>). The editor's plan decides; this is the prediction."""
import argparse, hashlib, json, math, os, re, sys
os.environ.setdefault('OMP_NUM_THREADS', '4'); os.environ.setdefault('OPENBLAS_NUM_THREADS', '4'); os.environ.setdefault('MKL_NUM_THREADS', '4')
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import buildingaudit308_offline as B
import contentseat308_dry as S

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
LIVE = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308/roadinn308.json'
STAGE = ROOT / 'Tools/Unity/Stage308_relayout_ext/Data/roadinn308.json'
OUT = ROOT / 'Tools/Unity/Stage308_relayout_ext/Dry'
ORDER = ['arch296', 'folk298', 'main']
LIGHT_TYPES = {0: 'Spot', 1: 'Directional', 2: 'Point'}


def sha_text(t): return hashlib.sha256(t.encode('utf-8')).hexdigest()
def d2(a, b): return math.hypot(a[0] - b[0], a[1] - b[1])
def f2(v): return '%.2f' % v


class Rep:
    def __init__(self, title): self.lines = [title]; self.nums = []; self.failed = []

    def say(self, s): self.lines.append('  ' + s)

    def num(self, op, name, value, verdict='info', why=''):
        self.nums.append({'op': op, 'name': name, 'value': value, 'verdict': verdict})
        self.lines.append('  num %s %s %s [%s]%s' % (op, name, value, verdict, (' ' + why) if why else ''))
        if verdict == 'FAIL': self.failed.append('%s %s = %s %s' % (op, name, value, why))

    def chk(self, op, name, value, ok, why=''): self.num(op, name, value, 'ok' if ok else 'FAIL', why)


# ---------------------------------------------------------------------------------------------- content asset (text blocks)

def blocks(text, section):
    """{Id: yaml block} of one top-level list of the content asset (entries start with '  - Id: ')."""
    m = re.search(r'^  %s:\s*\n' % section, text, flags=re.M)
    if not m: return {}
    rest = text[m.end():]
    end = re.search(r'^  [A-Za-z_]+:', rest, flags=re.M)
    body = rest[:end.start()] if end else rest
    out = {}
    for part in re.split(r'(?m)^(?=  - Id: )', body):
        mm = re.match(r'  - Id: (.*)\n', part)
        if mm: out[mm.group(1).strip()] = part
    return out


def without_services(block):
    """the point block up to its Services line (Services is the last field of a Point)."""
    i = block.find('\n    Services:')
    return block if i < 0 else block[:i]


def content_facts(path, data):
    text = Path(path).read_text(encoding='utf-8', errors='replace')
    pts = blocks(text, 'Points'); cps = blocks(text, 'Checkpoints'); com = blocks(text, 'Commissions')
    k = data['keeper']; r = data['rest']
    kp = pts.get(k['point'])
    return {
        'text': text, 'points': pts, 'checkpoints': cps,
        'keeper_block': kp, 'commission_block': com.get(k['commission']),
        'has_rest_point': r['id'] in pts, 'has_rest_checkpoint': r['id'] in cps,
        'commission_sha': sha_text(com[k['commission']]) if k['commission'] in com else '',
        'keeper_sha': sha_text(without_services(kp)) if kp else '',
        'keeper_services_empty': bool(kp) and bool(re.search(r'\n    Services: \[\]\s*$', kp.rstrip('\n') + '\n', flags=re.M)),
    }


# ---------------------------------------------------------------------------------------------- scene lights (values)

def scene_lights(path):
    """GameObject fileID -> [light dicts] (type, range, intensity, shadows, enabled)."""
    out = {}; cur = None; cls = None; info = None; in_shadows = False
    for line in B.read_scene_lines(path):
        m = B.HDR.match(line)
        if m:
            cls = int(m.group(1)); cur = m.group(2); info = {'on': True, 'shadows': 0} if cls == 108 else None; in_shadows = False; continue
        if cls != 108 or info is None: continue
        s = line.rstrip('\n')
        if s.startswith('  m_GameObject: '): out.setdefault(B.FILEID.search(s).group(1), []).append(info)
        elif s.startswith('  m_Enabled: '): info['on'] = s.endswith('1')
        elif s.startswith('  m_Type: ') and not in_shadows: info['type'] = int(s.split(':')[1])
        elif s.startswith('  m_Range: '): info['range'] = float(s.split(':')[1])
        elif s.startswith('  m_Intensity: '): info['intensity'] = float(s.split(':')[1])
        elif s.startswith('  m_Shadows:'): in_shadows = True
        elif in_shadows and s.startswith('    m_Type: '): info['shadows'] = int(s.split(':')[1]); in_shadows = False
        elif in_shadows and not s.startswith('    '): in_shadows = False
    return out


def qconj(q): return (-q[0], -q[1], -q[2], q[3])


def box_gaps(sk, at, y, radius, height, reach, skip_prefix):
    """(gap m, key) of every upright BoxCollider whose footprint is within `reach` of `at` and overlaps the capsule's height."""
    out = []
    for tid, n in sk.sc.nodes.items():
        comps = sk.comps.get(n.go, [])
        if not any(c == 65 for (c, _, _) in comps): continue
        p, q, s = sk.sc.world(tid)
        if d2((p[0], p[2]), at) > reach + 40: continue
        key = sk.key_of(tid)
        if any(key.startswith(x) for x in skip_prefix): continue
        for (c, _, info) in comps:
            if c != 65 or not info.get('size') or info.get('on') is False: continue
            ce = info.get('center') or (0.0, 0.0, 0.0); sz = info['size']
            l = B.qrot(qconj(q), (at[0] - p[0], y - p[1], at[1] - p[2]))
            cx, cy, cz = (ce[i] * s[i] for i in range(3)); hx, hy, hz = (abs(sz[i] * s[i]) * 0.5 for i in range(3))
            if l[1] + height < cy - hy or l[1] > cy + hy: continue   # no vertical overlap with the capsule
            gap = math.hypot(max(abs(l[0] - cx) - hx, 0.0), max(abs(l[2] - cz) - hz, 0.0)) - radius
            if gap <= reach: out.append((gap, key))
    return sorted(out)


def near_other_colliders(sk, at, reach):
    out = []
    for tid, n in sk.sc.nodes.items():
        kinds = [S.CLS[c] for (c, _, _) in sk.comps.get(n.go, []) if c in (64, 135, 136)]
        if not kinds: continue
        p, _, _ = sk.sc.world(tid)
        d = d2((p[0], p[2]), at)
        if d <= reach: out.append((d, sk.key_of(tid), '+'.join(sorted(set(kinds)))))
    return sorted(out)


def path_dist(paths, p, gap):
    best = float('inf')
    for pl in paths.values():
        for a, b in zip(pl, pl[1:]):
            if d2((a[0], a[2]), (b[0], b[2])) > gap: continue
            best = min(best, S.seg_dist(p, (a[0], a[2]), (b[0], b[2])))
    return best


# ---------------------------------------------------------------------------------------------- text rules

def text_checks(rep, data):
    tr = data['text_rules']; lines = data['lines']; rx = re.compile(tr['banned_regex'])
    used = []
    for sv in data['keeper']['services']:
        label = sv.get('label') or ''
        if label:
            bad = [w for w in tr['banned'] if w in label] + (['regex'] if rx.search(label) else [])
            rep.chk('XI2', 'row name "%s" chars' % label, len(label), len(label) <= tr['label_max_chars'] and not bad, '(<= %d, banned %s)' % (tr['label_max_chars'], bad or 0))
        ids = list(sv.get('lines') or []) + (list(sv.get('greeting') or []) if data['keeper'].get('rest_greeting') else [])
        for i in ids:
            e = lines.get(i)
            if not isinstance(e, dict): rep.chk('XI2', 'line %s' % i, 'missing', False); continue
            t = e.get('text', ''); bad = [w for w in tr['banned'] if w in t] + (['regex'] if rx.search(t) else [])
            rep.chk('XI2', 'line %s chars' % i, len(t), e.get('status') == 'TEST' and 0 < len(t) <= tr['max_chars'] and not bad,
                    '(<= %d, status %s, banned %s) %s' % (tr['max_chars'], e.get('status'), bad or 0, t))
            used.append(i)
    built = sorted(k for k, v in lines.items() if isinstance(v, dict))
    rep.chk('XI2', 'lines built', ','.join(sorted(used)), sorted(used) == built and 'H7' not in used and 'H8' not in used, '(every TEST line is on a row; H7 / H8 not built)')


# ---------------------------------------------------------------------------------------------- one scene

def dry_scene(alias, data, H, out_dir):
    tg = next(t for t in data['targets'] if t['alias'] == alias)
    rep = Rep('roadinn308 dry %s (%s)' % (alias, tg['scene'])); R = data['rules']; r = data['rest']; k = data['keeper']; L = data['lantern']
    cpath = PROJECT / tg['content']; spath = PROJECT / tg['scene']
    cf = content_facts(cpath, data); content = S.read_content(cpath)
    rep.say('content %s sha %s' % (tg['content'], S.sha(cpath)[:12]))
    if cf['keeper_block'] is None:
        rep.say('skipped (not in this scene): the content has no point %s' % k['point']); return rep, {'skipped': 'no keeper point'}
    sk = S.SceneK(spath)
    rep.say('scene sha %s' % sk.sha[:12])
    missing = [key for key in data['scene_requires'] if sk.get(key) is None]
    if missing:
        rep.say('skipped (not in this scene): scene_requires %s absent - RoadInn308 writes nothing here (content and scene)' % missing)
        return rep, {'skipped': 'scene_requires ' + ','.join(missing)}
    elder = content['points'][k['point']]
    P = (r['x'], r['z']); F = (r['feet']['x'], r['feet']['z'])
    yP = H(*P); yF = H(*F)
    slope = lambda p: math.degrees(math.acos(max(-1.0, min(1.0, H.normal(p[0], p[1], 2.0)[1]))))
    # XI1
    rep.say('-- XI1 rest point + checkpoint')
    rep.num('XI1', 'already in content (point / checkpoint)', '%s / %s' % (cf['has_rest_point'], cf['has_rest_checkpoint']))
    rep.chk('XI1', 'P ground y', f2(yP), abs(yP - r['y_offline']) <= R['y_tol_m'], '(row y_offline %.2f)' % r['y_offline'])
    rep.chk('XI1', 'P slope deg (4 m window)', f2(slope(P)), slope(P) <= r['max_slope_deg'], '(<= %s, pinned)' % r['max_slope_deg'])
    rep.chk('XI1', 'F ground y', f2(yF), abs(yF - r['feet']['y_offline']) <= R['y_tol_m'], '(row y_offline %.2f)' % r['feet']['y_offline'])
    rep.chk('XI1', 'F slope deg', f2(slope(F)), slope(F) <= R['feet_max_slope_deg'])
    e2 = (elder[0], elder[2])
    rep.chk('AC-I3', 'feet to keeper point m', f2(d2(F, e2)), d2(F, e2) >= R['feet_keeper_min_m'], '(>= %s)' % R['feet_keeper_min_m'])
    road = path_dist({'MainPath': content['paths'].get('MainPath', [])}, F, R['road_run_gap_m'])
    rep.chk('AC-I3', 'feet to MainPath centre m', f2(road), road >= R['feet_road_min_m'], '(>= %s; 8 m road)' % R['feet_road_min_m'])
    rep.num('AC-I3', 'rest point to MainPath centre m', f2(path_dist({'MainPath': content['paths'].get('MainPath', [])}, P, R['road_run_gap_m'])))
    far = d2(P, e2) + k['talk_radius_m']
    rep.chk('AC-I3', 'keeper talk circle farthest from P m', f2(far), far <= r['radius'], '(<= rest radius %s: 정비 opens anywhere in the talk circle)' % r['radius'])
    gaps = box_gaps(sk, F, yF, R['feet_probe_radius_m'], R['feet_probe_height_m'], 6.0, (L['root'] + '/',))
    rep.chk('AC-I4', 'feet capsule to nearest BoxCollider m', f2(gaps[0][0]) if gaps else 'none within 6 m', not gaps or gaps[0][0] > 0, '(%s)' % (gaps[0][1] if gaps else '-'))
    for d, key, kind in near_other_colliders(sk, F, 3.0):
        rep.num('AC-I4', 'unmeasured collider node within 3 m of the feet', '%s %s' % (f2(d), kind), 'info', key)
    ring = []
    for i in range(int(R['resume_ring_n'])):
        a = math.radians(r['feet']['yaw'] + i * 360.0 / R['resume_ring_n'])
        q = (F[0] + math.sin(a) * R['resume_ring_m'], F[1] + math.cos(a) * R['resume_ring_m'])
        g = box_gaps(sk, q, H(*q), R['feet_probe_radius_m'], R['feet_probe_height_m'], 1.0, (L['root'] + '/',))
        ring.append({'i': i, 'x': round(q[0], 2), 'z': round(q[1], 2), 'keeper_m': round(d2(q, e2), 2), 'box_gap': round(g[0][0], 2) if g else None, 'slope': round(slope(q), 1)})
    free = [x for x in ring if x['keeper_m'] >= 0.6 and (x['box_gap'] is None or x['box_gap'] > 0)]
    rep.chk('QI5', 'resume ring spots clear of boxes and the keeper (offline; NavMesh is an editor probe)', '%d of %d' % (len(free), len(ring)), len(free) >= 1)
    # XI2
    rep.say('-- XI2 keeper Services')
    rep.num('XI2', 'keeper Services now empty', cf['keeper_services_empty'])
    rep.num('AC-I5', 'commission %s block sha' % k['commission'], cf['commission_sha'][:16] or 'MISSING', 'info' if cf['commission_sha'] else 'FAIL')
    rep.num('XI2', 'keeper point (without Services) sha', cf['keeper_sha'][:16])
    text_checks(rep, data)
    # XI3
    rep.say('-- XI3 lantern')
    src = sk.get(L['source_key'])
    rep.chk('XI3', 'source_key resolves', L['source_key'], src is not None)
    lights = scene_lights(spath)
    if src is not None:
        sub = sk.sc.subtree(src); found = [(sk.key_of(t), l) for t in sub for l in lights.get(sk.sc.nodes[t].go, [])]
        # the tool keeps the first light.max Light components of the clone (enabled, data values, shadows off) and switches the rest off
        rep.chk('AC-I4', 'Light components in the source subtree (the clone keeps %d on = the one new sustained light)' % L['light']['max'], len(found), len(found) >= L['light']['max'],
                '(a source without a Light is refused: the tool never makes a light of its own)')
        for key, l in found:
            ok = LIGHT_TYPES.get(l.get('type')) == L['light']['type']
            rep.chk('AC-I4', 'source light', '%s range %s intensity %s shadows %s enabled %s' % (LIGHT_TYPES.get(l.get('type'), l.get('type')), l.get('range'), l.get('intensity'), l.get('shadows'), l.get('on', True)), ok,
                    '(the clone gets the data values: %s, range %s, intensity %s, shadows off)' % (L['light']['type'], L['light']['range_m'], L['light']['intensity']))
        cols = [sk.key_of(t) for t in sub if any(c in (64, 65, 135, 136) for (c, _, _) in sk.comps.get(sk.sc.nodes[t].go, []))]
        rep.num('XI3', 'colliders in the source subtree (stripped from the clone)', len(cols))
        p, yaw, _, s = sk.world(L['source_key'])
        rep.num('XI3', 'source world', '(%.2f, %.2f, %.2f) yaw %.1f scale %.2f' % (p[0], p[1], p[2], yaw, s[0]))
    off = L['offset_from_point']; tgt = (P[0] + off[0], yP + off[1], P[1] + off[2]); want = L['world_target_offline']
    dev = math.dist(tgt, want)
    rep.chk('XI3', 'lantern world target', '(%.2f, %.2f, %.2f)' % tgt, dev <= 0.05, '(design %s, off %.3f m)' % (want, dev))
    for name, q, y in (('keeper', e2, elder[1]), ('feet', F, yF), ('rest point', P, yP)):
        d = math.dist(tgt, (q[0], y, q[1]))
        rep.chk('XI3', 'lantern to %s m (3D)' % name, f2(d), d <= L['light']['range_m'], '(inside the light range %s)' % L['light']['range_m'])
    under = box_gaps(sk, (tgt[0], tgt[2]), tgt[1] - 0.3, 0.15, 0.6, 0.5, (L['root'] + '/',))
    rep.num('XI3', 'BoxCollider around the lantern body (0.15 m)', f2(under[0][0]) + ' ' + under[0][1] if under else 'none within 0.5 m')
    exists = sk.get(L['root']) is not None
    rep.num('XI3', 'scene already has root %s' % L['root'], exists)
    total_on = sum(1 for ls in lights.values() for l in ls if l.get('on', True) and l.get('type') == 2)
    rep.num('AC-I8', 'enabled point lights in the scene file now (the apply adds exactly %d)' % L['light']['max'], total_on)
    # XI4
    rep.say('-- XI4 seal profile')
    seal = Path(PROJECT / data['seal']['profile']).read_text(encoding='utf-8', errors='replace')
    m = re.search(r'^  EaRestIds:\s*\n((?:  - .*\n)*)', seal, flags=re.M)
    ids = [x[4:].strip() for x in m.group(1).splitlines()] if m else []
    rep.num('XI4', 'EaRestIds now', '%d ids, ordered %s, has %s: %s' % (len(ids), ids == sorted(ids), r['id'], r['id'] in ids))
    doc = {'alias': alias, 'scene_sha': sk.sha, 'content_sha': S.sha(cpath), 'P': [P[0], yP, P[1]], 'F': [F[0], yF, F[1]], 'lantern': list(tgt), 'ring': ring,
           'commission_sha': cf['commission_sha'], 'keeper_sha': cf['keeper_sha'], 'nums': rep.nums, 'failed': rep.failed}
    return rep, doc


def cmd_baseline(data, out_dir, write):
    cur = {}
    for tg in data['targets']:
        cf = content_facts(PROJECT / tg['content'], data)
        cur[tg['alias']] = {'commission_sha': cf['commission_sha'], 'keeper_sha': cf['keeper_sha']}
    f = out_dir / 'roadinn308_baseline.json'
    if write:
        out_dir.mkdir(parents=True, exist_ok=True); f.write_text(json.dumps(cur, indent=1), encoding='utf-8'); print('baseline written', f)
        for a, v in cur.items(): print(' ', a, v['commission_sha'][:16], v['keeper_sha'][:16])
        return 0
    if not f.exists(): print('no baseline', f, '- run with --write before the apply'); return 2
    old = json.loads(f.read_text(encoding='utf-8')); bad = 0
    for a, v in cur.items():
        for key in ('commission_sha', 'keeper_sha'):
            same = old.get(a, {}).get(key) == v[key]; bad += 0 if same else 1
            print('  %s %s %s %s' % (a, key, v[key][:16], 'unchanged [ok]' if same else 'CHANGED [FAIL] (was %s)' % old.get(a, {}).get(key, '')[:16]))
    return 2 if bad else 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('cmd', choices=['dry', 'baseline'])
    ap.add_argument('--scene', default='all'); ap.add_argument('--data'); ap.add_argument('--out'); ap.add_argument('--write', action='store_true')
    a = ap.parse_args()
    dpath = Path(a.data) if a.data else (LIVE if LIVE.exists() else STAGE)
    data = json.loads(dpath.read_text(encoding='utf-8')); out_dir = Path(a.out) if a.out else OUT
    if a.cmd == 'baseline': return cmd_baseline(data, out_dir, a.write)
    H = S.Height(S.HEIGHT_P1B); out_dir.mkdir(parents=True, exist_ok=True); bad = 0
    for alias in (ORDER if a.scene == 'all' else [a.scene]):
        rep, doc = dry_scene(alias, data, H, out_dir)
        rep.lines.insert(1, '  data %s sha %s; height %s' % (dpath, S.sha(dpath)[:12], Path(H.path).name))
        if rep.failed: rep.lines.append('  FAILED (%d):' % len(rep.failed)); rep.lines += ['    ' + x for x in rep.failed]
        else: rep.lines.append('  no failed condition')
        (out_dir / ('roadinn308_dry_%s.txt' % alias)).write_text('\n'.join(rep.lines) + '\n', encoding='utf-8')
        (out_dir / ('roadinn308_dry_%s.json' % alias)).write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding='utf-8')
        print('\n'.join(rep.lines)); bad += len(rep.failed)
    return 2 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
