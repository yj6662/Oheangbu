#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""#308 relayout fix 4 (rev 2) - offline dry run of the three places (적재장 / 금줄 꼬는 노인 / 옹기장수). READ ONLY: no editor, no Play.

  python Tools/Art/relayout_fix4_dry.py                 every rule as a PASS / FAIL line + RESULT (views are rendered: ~1 minute)
  python Tools/Art/relayout_fix4_dry.py --no-views      the same without the software views (the V lines are skipped)
  python Tools/Art/relayout_fix4_dry.py --pictures      also writes Art/Playtest308/Relayout/fix4/<place>_plan.png, <place>_views.png, <place>_user_half.jpg
  python Tools/Art/relayout_fix4_dry.py mutations       breaks the data one rule at a time: each break must turn its rule red
  option: --data <dir>   read places308.json / texts308.json / meshes/ from another folder (default: the stage Data/)

What is measured and how far it can be trusted
  [O] height field (4 m lattice), vegetation sheets, grass field, scene YAML, content / layout assets, generated mesh json, prefab YAML / FBX boxes.
  [I] a tree crown is a solid ellipsoid, a man is three boxes, a jar is an ellipsoid of its measured box: the V lines say what is IN VIEW,
      not how it looks. The editor stills (RUN_ORDER_fix4.md) judge the look.
  The editor grounds on physics; here the ground is the height field. Places308 plan:<scene> prints the difference per row.
rev 2 (review): R1 replays the tool's own two WARN tests on EVERY row (rev 1 did it for the yard only and missed the rope gate), D5 the
mesh validity, A9 measures existing objects by their bounds, A13 the 국 gate guard, B9 - B11 / C3 / C12 the props of the two people at
EVERY candidate place (seat on the slope, off the path, hanks on their pegs), P1 the car lane for every row. V-B5 is INFO, not PASS: the
old rope hangs over the ground by the data (B8).
"""
import copy, json, math, shutil, sys, tempfile
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import relayout_fix4_lib as F          # noqa: E402
import relayout_fix3_lib as L          # noqa: E402
import contentseat308_dry as S         # noqa: E402
import meshbounds308 as MBD            # noqa: E402

DATA = F.STAGE / 'Data'
ORIG = F.STAGE / 'Original' / 'texts308.json.orig'
KEEP = ('lines', 'owners', 'campaign_rows', 'testimonies', 'keepers', 'boards', 'text_rules', 'evidence_page_join', 'targets', 'campaign', 'root', 'group', 'decision')
RULES_NEW = ('tilt_probe_m',)          # the only rule numbers this stage may add to texts308.json rules{} (its note grows by one sentence)
POINT_KEEP = ('op', 'id', 'kind', 'speaker', 'prompt', 'first', 'pages', 'radius', 'rows')
PEOPLE_KEEP = ('op', 'id', 'source_key', 'source_point', 'job_profile', 'remove_children', 'reseat_children')
PROP_KEYS = {'name', 'prefab', 'local', 'yaw', 'scale', 'on', 'tilt', 'sink_m', 'hang_m'}


class Rep:
    def __init__(self): self.rows = []
    def c(self, ok, tag, text): self.rows.append((bool(ok), tag, text))
    def info(self, text, tag='INFO'): self.rows.append((None, tag, text))
    def fails(self): return [r for r in self.rows if r[0] is False]


def d2(a, b): return math.hypot(a[0] - b[0], a[1] - b[1])


def prefab_size(path):
    """measured box of a prefab (w, h, d), base y; cached beside the pictures."""
    f = F.OUT / 'cache' / 'prefab_boxes.json'; c = json.loads(f.read_text()) if f.exists() else {}
    if path not in c:
        lo, hi, notes = MBD.prefab_box(path, str(F.OUT / 'cache' / 'guid_cache.json')); t = (F.PROJECT / path).read_text(encoding='utf-8', errors='replace')
        import re
        q = re.findall(r'm_LocalRotation: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+), w: ([-\d.e]+)\}', t)
        c[path] = dict(size=[hi[i] - lo[i] for i in range(3)], lo=list(lo), root_identity=bool(q) and all(abs(float(v)) < 1e-6 for v in q[0][:3]), colliders=len(re.findall(r'^(?:BoxCollider|MeshCollider|SphereCollider|CapsuleCollider):', t, flags=re.M)))
        f.parent.mkdir(parents=True, exist_ok=True); f.write_text(json.dumps(c, indent=1))
    return c[path]


# ------------------------------------------------------------------------------------------------ Texts308, replayed offline

def clear_lines(W, spec, at):
    k = spec.get('clear') or {}; out = []
    def say(name, v, ok, rule): out.append((ok, '%s %.2f m %s' % (name, v, rule)))
    if k.get('boss') and k['boss'] in W.enc: d = d2(at, F.P3(W.enc[k['boss']]['Feet'])[::2]); say('boss ' + k['boss'], d, d >= k['boss_min_m'], '>= %g' % k['boss_min_m'])
    for eid in k.get('enemies', []):
        e = W.enc.get(eid)
        if not e: continue
        d = d2(at, F.P3(e['Feet'])[::2])
        if 'enemies_min_m' in k: say('enemy ' + eid, d, d >= k['enemies_min_m'] and d > e['Detection'], '>= %g and outside its detection %g' % (k['enemies_min_m'], e['Detection']))
    if 'rests_min_m' in k:
        best = (1e9, '')
        for cid, ft in W.feet.items(): best = min(best, (d2(at, ft[::2]), 'feet ' + cid))
        for pid, p in W.points.items():
            if p['Kind'] == 2: best = min(best, (d2(at, F.P3(p['Position'])[::2]), 'rest ' + pid))
        say('nearest rest (%s)' % best[1], best[0], best[0] >= k['rests_min_m'], '>= %g' % k['rests_min_m'])
    if 'points_min_m' in k:
        best = min((d2(at, F.P3(p['Position'])[::2]), pid) for pid, p in W.points.items() if pid != spec['id'])
        say('nearest other point (%s)' % best[1], best[0], best[0] >= k['points_min_m'], '>= %g' % k['points_min_m'])
    if 'road_min_m' in k:
        d = W.path_d(at); say('road centre line (content paths)', d, k['road_min_m'] <= d <= k.get('road_max_m', 1e9), 'in [%g, %g]' % (k['road_min_m'], k.get('road_max_m', 999)))
    return out


def pick(W, spec):
    """the first candidate place that stands (tri slope within the row's limit, every clearance kept) - Texts308.Pick."""
    why = []
    for pl in spec['places']:
        at = (pl['x'], pl['z']); sl = F.tri_slope(*at)
        if sl > spec['max_slope_deg']: why.append('(%.1f, %.1f): slope %.1f > %g' % (at[0], at[1], sl, spec['max_slope_deg'])); continue
        cl = clear_lines(W, spec, at)
        if not all(ok for ok, _ in cl): why.append('(%.1f, %.1f): %s' % (at[0], at[1], '; '.join(t for ok, t in cl if not ok))); continue
        return at, sl, cl, why
    return None, None, [], why


def body_yaw(person, at):
    f = person.get('face')
    return math.degrees(math.atan2(f['x'] - at[0], f['z'] - at[1])) % 360.0 if f else person['yaw'] % 360.0


def to_world(at, yaw, lx, lz):
    c = math.cos(math.radians(yaw)); s = math.sin(math.radians(yaw)); return (at[0] + lx * c + lz * s, at[1] - lx * s + lz * c)


# ------------------------------------------------------------------------------------------------ props of a person (Texts308 props[])

def prop_pose(person, at, pr):
    """world XZ and world yaw of a prop of a person standing at `at` (Texts308: body.TransformPoint(local), body yaw + prop yaw)."""
    yaw = body_yaw(person, at); return to_world(at, yaw, pr['local'][0], pr['local'][2]), (yaw + pr['yaw']) % 360.0


def prop_ground(pr): return abs(pr['local'][1]) < 1e-4 and pr.get('on') is None


def prop_box(pr, q, wyaw):
    """the four XZ corners of the prefab's measured box under the prop."""
    pb = prefab_size(pr['prefab']); sc = pr['scale']; lo = pb['lo']; sz = pb['size']
    return F.local_box_world(dict(center=[(lo[0] + sz[0] / 2) * sc, 0, (lo[2] + sz[2] / 2) * sc], size=[sz[0] * sc, 0, sz[2] * sc]), q[0], q[1], wyaw)


def prop_ring(pr, q, wyaw, n=8):
    """n points on the ellipse inscribed in that box: what a round thing (a jar, a straw stack) stands on."""
    pb = prefab_size(pr['prefab']); sc = pr['scale']; lo = pb['lo']; sz = pb['size']; cx = (lo[0] + sz[0] / 2) * sc; cz = (lo[2] + sz[2] / 2) * sc
    return [to_world(q, wyaw, cx + sz[0] * sc / 2 * math.cos(2 * math.pi * k / n), cz + sz[2] * sc / 2 * math.sin(2 * math.pi * k / n)) for k in range(n)]


def prop_seat(T, pr, q, wyaw):
    """how a GROUND prop sits. tilt true: laid on the plane Texts308.PropTilt takes (four probes, rules.tilt_probe_m) -> the largest
    difference between that plane and the ground under the four box corners, and the plane's tilt. Else upright on the ground under
    its centre + hang_m - sink_m -> how far its lowest rim point is over the ground (float) and under it (bury) round the inscribed ring."""
    pb = prefab_size(pr['prefab']); sc = pr['scale']
    if pr.get('tilt'):
        n, plane = F.ground_plane(q[0], q[1], T['rules']['tilt_probe_m'])
        return dict(kind='tilt', residual=max(abs(F.h(*c) - plane(*c)) for c in prop_box(pr, q, wyaw)), tilt=math.degrees(math.acos(max(-1.0, min(1.0, float(n[1]))))))
    base = F.h(*q) + pr.get('hang_m', 0.0) - pr.get('sink_m', 0.0) + pb['lo'][1] * sc; ring = prop_ring(pr, q, wyaw)
    return dict(kind='upright', float=max(base - F.h(*c) for c in ring), bury=max(F.h(*c) - base for c in ring), base=base)


# ------------------------------------------------------------------------------------------------ rows

def mesh_of(P, name): return next((m for m in P['meshes'] if m['name'] == name), None)


def row_geo(P, r):
    """footprint corners (world XZ), collider corner lists, top height over the pivot."""
    sc = r.get('scale', 1.0)
    if r['kind'] == 'mesh':
        m = mesh_of(P, r['mesh']); lo, hi = m['lo'], m['hi']
        fp = F.local_box_world(dict(center=[(lo[0] + hi[0]) / 2 * sc, 0, (lo[2] + hi[2]) / 2 * sc], size=[(hi[0] - lo[0]) * sc, 0, (hi[2] - lo[2]) * sc]), r['x'], r['z'], r['yaw'])
        cols = [F.local_box_world(dict(center=[v * sc for v in b['center']], size=[v * sc for v in b['size']], yaw=b.get('yaw', 0)), r['x'], r['z'], r['yaw']) for b in m['colliders']] if r.get('solid') else []
        return fp, cols, hi[1] * sc
    pb = prefab_size(r['prefab']); w, hgt, d = [v * sc for v in pb['size']]
    return F.local_box_world(dict(center=[0, 0, 0], size=[w, 0, d]), r['x'], r['z'], r['yaw']), [], hgt


def rows_on(P):
    return [(pl, r) for pl in P['places'] if pl.get('enabled', True) for r in pl['rows']]


def poly_samples(poly, step=0.5):
    out = list(poly)
    for a, b in zip(poly, poly[1:] + poly[:1]):
        n = max(1, int(d2(a, b) / step))
        out += [(a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n) for i in range(1, n)]
    return out


def poly_pt_d(poly, p): return L.poly_pt_d(poly, p)


def mesh_faults(folder, m):
    """(vertices with a zero-length normal, triangles of zero area) of a generated mesh json."""
    d = json.loads((Path(folder) / m['json']).read_text(encoding='utf-8')); V = np.array(d['v'], float).reshape(-1, 3); N = np.array(d['n'], float).reshape(-1, 3)
    zn = int((np.linalg.norm(N, axis=1) < 1e-6).sum()); za = 0
    for s in d['sub']:
        t = np.array(s['t'], int).reshape(-1, 3); za += int((np.linalg.norm(np.cross(V[t[:, 1]] - V[t[:, 0]], V[t[:, 2]] - V[t[:, 0]]), axis=1) < 1e-9).sum())
    return zn, za


# ------------------------------------------------------------------------------------------------ the scene as layers

COL = dict(ground=(206, 200, 184), trunk=(70, 58, 48), crown=(74, 96, 70), rock=(150, 150, 146), old=(120, 110, 100), man=(40, 36, 34), brown=(120, 92, 66), altar=(170, 168, 160), lesson=(52, 80, 52),
           straw=F.SLOT['straw'], rope=F.SLOT['rope'], white=(232, 230, 222))
ID = dict(ground=1, trunk=2, crown=3, existing=4, man=10, manprops=11, new=20, lesson=30, oldrope=31, altar=32, paper=40, stacks=41)


def base_layers(win, trees=True, skip_ids=()):
    layers = [(L.ground_tris(win[0], win[1], win[2], win[3], 2.0), COL['ground'], False, ID['ground'])]
    if trees:
        for row in F.veg_rows(win):
            if row['id'] in skip_ids: continue
            tr, cr = F.tree_tris(row)
            if len(tr): layers.append((tr, COL['trunk'], False, ID['trunk']))
            layers.append((cr, COL['crown'] if row['tree'] or row['cat'] == 1 else COL['rock'], False, ID['crown']))
    return layers


def keys_layers(keys, colour, iid):
    out = []
    for k in keys:
        T = F.scene_mesh(k)
        if T is not None: out.append((T, colour, True, iid))
    return out


def subtree_layers(prefix, colour, iid, boxes=True):
    """the real .asset meshes under a scene key; an object whose mesh is an FBX or a primitive is drawn as its measured box (rev 2)."""
    sk = F.scene(); out = []
    t = sk.get(prefix)
    if t is None: return out
    for x in sk.sc.subtree(t):
        k = sk.key_of(x)
        if '_LOD1' in k or '_LOD2' in k or '_LOD3' in k or k.endswith('/LOD1') or k.endswith('/LOD2') or 'Collision' in k: continue
        T = F.scene_mesh(k)
        if T is not None: out.append((T, colour, True, iid)); continue
        if not boxes: continue
        n = sk.sc.nodes[x]
        if not any(c == 23 for (c, _, i) in sk.comps.get(n.go, [])): continue      # drawn things only (a bare collider is not seen)
        b = F.scene_box(k)
        if b is not None: out.append((F.box_tris(b[0]), colour, True, iid))
    return out


def rows_layers(P, place, hfun=F.h):
    out = []
    for r in place['rows']:
        y = hfun(r['x'], r['z']) - r.get('sink_m', 0.0); sc = r.get('scale', 1.0)
        if r['kind'] == 'mesh':
            for slot, T in F.gen_mesh(r['mesh'], P.get('_mesh_folder')).items():
                iid = ID['paper'] if slot == 'paper' else ID['stacks'] if r['mesh'].startswith('LogStack') or r['mesh'] == 'Tripod' else ID['new']
                out.append((F.placed(T, r['x'], y, r['z'], r['yaw'], sc), F.SLOT[slot], True, iid))
        else:
            pb = prefab_size(r['prefab']); out.append((F.jar_tris(r['x'], y, r['z'], [v * sc for v in pb['size']]), COL['brown'], False, ID['new']))
    return out


def person_layers(T, pid, at, moved_from=None):
    """the man (three boxes) and his Texts308 props at `at`; children the clone carries (Jige ...) are the real meshes of the scene,
    turned and moved rigidly with the body. A tilted prop follows the ground under its corners, a hung / sunk one its hang_m / sink_m."""
    person = next(n for n in T['people'] if n['id'] == pid); yaw = body_yaw(person, at); y = F.h(*at)
    out = [(F.person_tris(at[0], y, at[1], yaw), COL['man'], False, ID['man'])]
    for pr in person['props']:
        (wx, wz), wyaw = prop_pose(person, at, pr); pb = prefab_size(pr['prefab']); sz = [v * pr['scale'] for v in pb['size']]; ly = pr['local'][1]
        wy = (F.h(wx, wz) + pr.get('hang_m', 0.0) - pr.get('sink_m', 0.0)) if prop_ground(pr) else y + ly
        name = pr['prefab'].split('/')[-1]
        if pr.get('tilt'):
            n, plane = F.ground_plane(wx, wz, T['rules']['tilt_probe_m']); c = prop_box(pr, (wx, wz), wyaw); thick = 0.06 if 'Mat' in name else sz[1] * .8
            lo = [np.array([p[0], plane(*p) - .02, p[1]]) for p in c]; hi = [np.array([p[0], plane(*p) + thick, p[1]]) for p in c]; tri = []
            for i in range(4):
                j = (i + 1) % 4; tri += [[lo[i], hi[i], lo[j]], [lo[j], hi[i], hi[j]]]
            tri += [[hi[0], hi[2], hi[1]], [hi[0], hi[3], hi[2]]]; out.append((np.array(tri), COL['straw'], True, ID['manprops']))
        elif 'Mat' in name: out.append((L.box_tris((wx, wz), (sz[0], .06, sz[2]), wyaw, wy), COL['straw'], False, ID['manprops']))
        elif 'Pot' in name or 'Jar' in name: out.append((F.jar_tris(wx, wy, wz, sz), COL['white'] if '046' in name or '038' in name else COL['brown'], False, ID['manprops']))
        elif 'Rope' in name:
            c = abs(math.cos(math.radians(wyaw))); s = abs(math.sin(math.radians(wyaw)))
            out.append((F.ellipsoid((wx, wy + sz[1] / 2, wz), (sz[0] * c + sz[2] * s) / 2, sz[1] / 2, (sz[0] * s + sz[2] * c) / 2, 8, 5), COL['rope'], False, ID['manprops']))
        else: out.append((F.ellipsoid((wx, wy + pb['lo'][1] * pr['scale'] + sz[1] / 2, wz), sz[0] / 2, sz[1] / 2, sz[2] / 2, 10, 6), COL['straw'], False, ID['manprops']))
    if moved_from is not None:
        src = F.scene_pose('Texts308/' + pid); turn = math.radians(yaw - (src[3] if src else yaw)); ct, st = math.cos(turn), math.sin(turn)
        for child in person.get('reseat_children', []):
            for lay in subtree_layers('Texts308/%s/%s' % (pid, child), COL['trunk'], ID['manprops'], boxes=False):
                tri = lay[0].copy(); c0 = tri.reshape(-1, 3).mean(axis=0); rx = tri[..., 0] - moved_from[0]; rz = tri[..., 2] - moved_from[1]
                nx = at[0] + rx * ct + rz * st; nz = at[1] - rx * st + rz * ct; cx, cz = float(nx.mean()), float(nz.mean())
                tri[..., 0] = nx; tri[..., 2] = nz; tri[..., 1] += F.h(cx, cz) - F.h(c0[0], c0[2]); out.append((tri, lay[1], True, lay[3]))
    return out


def eyes_for(W, route, ref, dists):
    """eye points `dists` metres back along the route from its vertex nearest to ref."""
    line = W.routes[route]; i = min(range(len(line)), key=lambda k: d2(line[k], ref))
    return [F.along_back(line, i, d) for d in dists]


# ------------------------------------------------------------------------------------------------ the rules

def run(P, T, views=True):
    rep = Rep(); W = F.World('main'); rules = P['rules']; T0 = json.loads(ORIG.read_text(encoding='utf-8')); out = dict(W=W)
    mesh_folder = Path(P.get('_mesh_folder') or DATA / 'meshes')
    # ---- D: data
    import hashlib
    index = json.loads((mesh_folder / 'index.json').read_text(encoding='utf-8'))['meshes']
    rep.c(json.dumps(index, sort_keys=True) == json.dumps(P['meshes'], sort_keys=True), 'D1', 'places308.json meshes[] = Data/meshes/index.json (%d meshes)' % len(index))
    bad = [m['name'] for m in P['meshes'] if hashlib.sha256((mesh_folder / m['json']).read_bytes()).hexdigest() != m['sha256']]
    rep.c(not bad, 'D1', 'every mesh json hashes to its row' + (' - NOT: ' + ', '.join(bad) if bad else ''))
    for slot, m in P['materials'].items():
        f = F.PROJECT / m['path']; meta = Path(str(f) + '.meta'); txt = f.read_text(encoding='utf-8', errors='replace') if f.exists() else ''
        guid_ok = meta.exists() and ('guid: ' + m['guid']) in meta.read_text(encoding='utf-8', errors='replace')
        rep.c(f.exists() and guid_ok and '_EMISSION' not in txt.split('m_ValidKeywords')[1].split('m_InvalidKeywords')[0] if 'm_ValidKeywords' in txt else f.exists() and guid_ok, 'D2',
              'material %s = existing %s (guid %s, no _EMISSION keyword)' % (slot, m['path'].split('/')[-1], m['guid'][:8]))
    used = {s for m in P['meshes'] for s in m['slots']}
    rep.c(used <= set(P['materials']), 'D2', 'every mesh slot has a material (%s)' % ', '.join(sorted(used)))
    for pl in P['places']:
        for r in pl['rows']:
            if r['kind'] == 'mesh': rep.c(mesh_of(P, r['mesh']) is not None and (not r.get('solid') or len(mesh_of(P, r['mesh'])['colliders']) > 0), 'D3', 'row %s: mesh %s exists%s' % (r['id'], r['mesh'], ' and carries collider boxes' if r.get('solid') else ''))
            else:
                ok = (F.PROJECT / r['prefab']).exists(); pb = prefab_size(r['prefab']) if ok else {}
                rep.c(ok and pb.get('root_identity') and abs(pb['lo'][1]) <= 0.02 and not r.get('solid') and r.get('scale', 1) <= rules['prefab_scale_max'], 'D3', 'row %s: prefab %s exists, root rotation identity, base on its pivot (%.3f), not solid' % (r['id'], r['prefab'].split('/')[-1], pb.get('lo', [0, 9, 0])[1]))
    prot = ('Watershed295', 'Reworld292', 'MountainTrail285', 'W_Demo_Compact.unity', '03_Content.asset')
    paths = [t['scene'] for t in P['targets']] + [t['content'] for t in P['targets']] + [P['asset_dir'], P['mesh_dir']]
    rep.c(not any(x in p for p in paths for x in prot), 'D4', 'no target, asset folder or mesh folder lies in a protected tree')
    for m in P['meshes']:
        zn, za = mesh_faults(mesh_folder, m)
        rep.c(zn == 0 and za == 0, 'D5', 'mesh %s: %d zero-length normal(s), %d zero-area triangle(s) (rev 1: RopeGate 54 / 64, Tripod 50 / 72 - the lashings were flat)' % (m['name'], zn, za))
    need = ('pose_tol_m', 'pose_tol_deg', 'scale_tol', 'y_tol_m', 'corner_gap_max_m', 'corridor_clear_m', 'point_collider_pad_m', 'prefab_scale_max', 'mesh_bounds_tol_m', 'path_run_gap_m')
    rep.c(all(isinstance(rules.get(k), (int, float)) for k in need), 'D6', 'every rule number Places308 reads at load is in rules{} (%d keys; nothing of the kind is left in the code)' % len(need))
    # ---- X: the text data is untouched
    for k in KEEP: rep.c(json.dumps(T.get(k), sort_keys=True, ensure_ascii=False) == json.dumps(T0.get(k), sort_keys=True, ensure_ascii=False), 'X1', 'texts308.json %s = the live D308-19 data' % k)
    r1, r0 = T.get('rules') or {}, T0.get('rules') or {}
    rep.c(all(r1.get(k) == v for k, v in r0.items() if k != 'note') and set(r1) - set(r0) <= set(RULES_NEW) and str(r1.get('note', '')).startswith(str(r0.get('note', ''))), 'X1',
          'texts308.json rules: every live number unchanged; added: %s (the note grows by one sentence)' % (sorted(set(r1) - set(r0)) or 'none'))
    for a, b in zip(T['points'], T0['points']):
        rep.c(all(a.get(k) == b.get(k) for k in POINT_KEEP), 'X1', 'point %s: id, kind, speaker, prompt, lines, rows, radius unchanged' % b['id'])
        changed = sorted(k for k in set(a) | set(b) if a.get(k) != b.get(k))
        rep.c(set(changed) <= {'places', 'max_slope_deg', 'clear', 'note'}, 'X2', 'point %s: changed keys = %s' % (b['id'], changed or 'none'))
        if a.get('clear') != b.get('clear'):
            ck = sorted(k for k in set(a['clear']) | set(b['clear']) if a['clear'].get(k) != b['clear'].get(k)); rep.c(ck == ['rests_min_m'] and a['id'] == 'south_gate_potter308', 'X2', 'point %s clear: only rests_min_m changed (%s -> %s; exception EX-M1)' % (a['id'], b['clear'].get('rests_min_m'), a['clear'].get('rests_min_m')))
    for a, b in zip(T['people'], T0['people']):
        rep.c(all(a.get(k) == b.get(k) for k in PEOPLE_KEEP), 'X1', 'person %s: source body, job profile, kept / removed children unchanged' % b['id'])
        changed = sorted(k for k in set(a) | set(b) if a.get(k) != b.get(k))
        rep.c(set(changed) <= {'face', 'yaw', 'props', 'note'} and (a.get('face') or 'yaw' in a), 'X2', 'person %s: changed keys = %s (he keeps a face point or a yaw)' % (b['id'], changed or 'none'))
        gone = sorted({pr['name'] for pr in b['props']} - {pr['name'] for pr in a['props']})
        rep.c(not gone, 'X2', 'person %s: every prop name of run 2 is still in the data (Texts308 never deletes a prop that left the data: it would stay in the scene)%s' % (b['id'], ' - GONE: ' + ', '.join(gone) if gone else ''))
        rep.c(all(pr.get('on') is None or pr['on'] in a['reseat_children'] for pr in a['props']) and len({pr['name'] for pr in a['props']}) == len(a['props']) and all(set(pr) <= PROP_KEYS for pr in a['props']), 'X2',
              'person %s: %d props, names unique, a prop on a child rides a re-seated child, no key Texts308 does not read' % (a['id'], len(a['props'])))
        for pr in a['props']:
            ok = (F.PROJECT / pr['prefab']).exists(); pb = prefab_size(pr['prefab']) if ok else {}
            rep.c(ok and pb.get('root_identity') and pb['lo'][1] > -0.10, 'C9' if a['id'] == 'south_gate_potter308' else 'B6', '%s prop %s: %s root rotation identity, lowest point %.3f m under its pivot = it stands upright where the tool puts it' % (a['id'].split('_')[-1], pr['name'], pr['prefab'].split('/')[-1], pb.get('lo', [0, -9, 0])[1]))
    rep.c(len(T['points']) == len(T0['points']) and len(T['people']) == len(T0['people']), 'X1', 'no point and no person added or removed')
    stx = (F.STAGE / 'Editor' / 'WorldMacro' / 'Texts308.Scene.cs').read_text(encoding='utf-8', errors='replace'); otx = (F.STAGE / 'Original' / 'Texts308.Scene.cs.orig').read_text(encoding='utf-8', errors='replace')
    import difflib
    dl = list(difflib.unified_diff(otx.splitlines(), stx.splitlines(), lineterm='', n=0)); added = [l for l in dl if l.startswith('+') and not l.startswith('+++')]; gone = [l for l in dl if l.startswith('-') and not l.startswith('---')]
    rep.c('bool swap=child!=null&&was!=prefab' in stx and 'static Quaternion PropTilt(' in stx and 'static float PropLift(' in stx and len(added) <= 34 and len(gone) <= 4, 'K1',
          'staged Texts308.Scene.cs = the live file + the prefab swap of a prop + the prop options tilt / sink_m / hang_m + two check lines (+%d / -%d lines; every other line is the live one)' % (len(added), len(gone)))
    ptx = (F.STAGE / 'Editor' / 'WorldMacro' / 'Places308.cs').read_text(encoding='utf-8', errors='replace')
    rep.c('static float CornerMax(Cfg cfg,JToken r)=>OptNum(r,"corner_gap_max_m",cfg.R("corner_gap_max_m"));' in ptx and ptx.count('CornerMax(cfg,r)') >= 4, 'K1', 'staged Places308.cs reads a row\'s own corner_gap_max_m in plan and in check (the same test this dry run replays as R1)')
    ex = {e['id']: e for e in P['exceptions']}
    # ---- geometry common to the rows
    geo = {r['id']: row_geo(P, r) for pl, r in rows_on(P)}
    # ================================================================================== R / P: every row, the tool's own tests
    wide = [rid for rid, w_ in W.width.items() if w_ >= rules['carriageway_min_width_m'] and rid in W.routes]
    for pl, r in rows_on(P):
        fp, cols, top = geo[r['id']]; g = F.h(r['x'], r['z']); gap = max(abs(F.h(*q) - g) for q in fp); lim = r.get('corner_gap_max_m', rules['corner_gap_max_m']); sl = F.tri_slope(r['x'], r['z'])
        rep.c(gap <= lim and sl <= r['max_slope_deg'] and abs(r.get('y_offline', g) - g) <= 0.006, 'R1',
              '%s: Places308 plan would not WARN / REFUSE - footprint corners within %.2f m of the pivot ground (limit %.2f%s), slope %.1f deg <= %g, y_offline = the field' % (r['id'], gap, lim, ", the row's own" if 'corner_gap_max_m' in r else '', sl, r['max_slope_deg']))
        lane = min((min(W.route_d(q, [rid])[0] for q in poly_samples(fp)), rid) for rid in wide) if wide else (1e9, '-')
        rep.c(lane[0] >= rules['car_lane_half_m'], 'P1', '%s: %.1f m from the centre line of the nearest carriageway (%s) >= the car lane half width %.1f' % (r['id'], lane[0], lane[1], rules['car_lane_half_m']))
    # ================================================================================== A: the log yard
    A = next(p for p in P['places'] if p['id'] == 'A_yard'); enc = W.enc[A['lesson']]; lf = F.P3(enc['Feet'])[::2]; det = float(enc['Detection']); lp = W.points[A['anchor_point']]
    winA = (lf[0] - 60, lf[0] + 60, lf[1] - 70, lf[1] + 50); rowsA = F.veg_rows(winA); seedsA = F.seeds(winA)
    stage = next(s for s in W.stages.values() if s['TriggerId'] == A['anchor_point'])
    rep.c(d2(F.P3(stage['Destination'])[::2], F.P3(lp['Position'])[::2]) < 0.01 and stage['DestinationLabel'] == T['lines']['T0A_label']['text'], 'A0', 'stage %s: Destination = the lesson point, DestinationLabel "%s" (M3: the name stays; this stage writes no campaign field)' % (stage['Id'], stage['DestinationLabel']))
    existing = []
    for k, p, cls in F.objects_near(lf, 45.0):
        if not any(c in (23, 64, 65) for c in cls): continue
        b = F.scene_box(k); existing.append((k, F.box_footprint(b[0]) if b is not None else None, (p[0], p[2])))
    gg = rules['gate_guard']; stone = [F.scene_box(k) for k in gg['keys']]; stone = [b for b in stone if b is not None]
    rep.c(len(stone) == len(gg['keys']), 'A13', 'the stone platform of the 국 revisit is measured (%d of %d objects by their mesh bounds)' % (len(stone), len(gg['keys'])))
    stone_top = max(float(b[0][:, 1].max()) for b in stone) if stone else float('nan'); stone_fp = [F.box_footprint(b[0]) for b in stone]
    if stone: out['stone'] = dict(top=stone_top, fp=stone_fp, height=stone_top - min(F.h(*q) for q in stone_fp[0]))
    rowsAon = A['rows'] if A.get('enabled', True) else []
    for r in rowsAon:
        fp, cols, top = geo[r['id']]; solid = bool(r.get('solid')) and bool(cols); pts = [q for c in cols for q in poly_samples(c)] if solid else poly_samples(fp)
        dl_ = min(W.line_d(q) for q in pts); need_ = rules['corridor_clear_m'] if solid else rules['loose_clear_m']
        rep.c(dl_ >= need_, 'A1' if solid else 'A2', '%s (%s%s): nearest route / path centre line %.2f m >= %.1f' % (r['id'], r['mesh'], ', solid' if solid else '', dl_, need_))
        dlesson = min(d2(q, lf) for q in pts); need_ = det if solid else rules['lesson_loose_clear_m']
        rep.c(dlesson >= need_, 'A3' if solid else 'A4', '%s: lesson feet %.2f m >= %s%s' % (r['id'], dlesson, ('its Detection %.0f' % det) if solid else ('%.1f' % need_), (' (inside its Leash %g)' % enc['Leash']) if solid and dlesson < enc['Leash'] else ''))
        if solid:
            for pid, p in W.points.items():
                q = F.P3(p['Position'])[::2]
                if d2(q, lf) > 60: continue
                dp = min(poly_pt_d(c, q) for c in cols)
                if dp < float(p['Radius']) + rules['point_collider_pad_m'] + 6: rep.c(dp >= float(p['Radius']) + rules['point_collider_pad_m'], 'A10', '%s: collider to point %s %.2f m >= its Radius %.1f + %.1f (no box can cut the eye -> point ray)' % (r['id'], pid, dp, float(p['Radius']), rules['point_collider_pad_m']))
            m = mesh_of(P, r['mesh']); sc = r.get('scale', 1.0); ctop = F.h(r['x'], r['z']) - r.get('sink_m', 0.0) + max(b['center'][1] + b['size'][1] / 2 for b in m['colliders']) * sc
            dstone = min(L.poly_poly_d(c, s_) for c in cols for s_ in stone_fp) if stone_fp else 1e9
            if dstone < gg['reach_m']: rep.c(ctop + gg['jump_m'] + gg['margin_m'] <= stone_top, 'A13', '%s: %.2f m from the stone platform (inside reach %.1f) - its collider top %.2f + jump %.2f + %.2f = %.2f <= the platform top %.2f (not a step onto the 국 gate)' % (r['id'], dstone, gg['reach_m'], ctop, gg['jump_m'], gg['margin_m'], ctop + gg['jump_m'] + gg['margin_m'], stone_top))
            else: rep.c(True, 'A13', '%s: %.2f m from the stone platform (outside reach %.1f)' % (r['id'], dstone, gg['reach_m']))
        sl = max(F.tri_slope(*q) for q in fp + [(r['x'], r['z'])]); gap = max(abs(F.h(*q) - F.h(r['x'], r['z'])) for q in fp)
        rep.c(sl <= r['max_slope_deg'] and gap <= rules['corner_gap_max_m'], 'A7', '%s: slope under the footprint %.1f deg <= %g, ground under the corners within %.2f m of the pivot ground (<= %.2f)' % (r['id'], sl, r['max_slope_deg'], gap, rules['corner_gap_max_m']))
        ns = int(sum(1 for s_ in seedsA if poly_pt_d(fp, (s_[0], s_[2])) <= rules['grass_pad_m'])); tr = [t for t in rowsA if poly_pt_d(fp, (t['pos'][0], t['pos'][2])) - (t['trunk'] if t['tree'] else t['R']) <= rules['trunk_clear_m']]
        rep.c(ns == 0 and not tr, 'A8', '%s: grass seeds in the footprint + %.1f m: %d, vegetation rows within %.1f m: %d (no grass op, no vegetation op)' % (r['id'], rules['grass_pad_m'], ns, rules['trunk_clear_m'], len(tr)) + (' ' + ', '.join(t['id'] for t in tr) if tr else ''))
        near = sorted((L.poly_poly_d(fp, poly) if poly else poly_pt_d(fp, piv), k) for k, poly, piv in existing); near = [n for n in near if n[0] < rules['object_clear_m']]
        rep.c(not near, 'A9', '%s: existing scene objects whose bounds come within %.1f m of the footprint: %d' % (r['id'], rules['object_clear_m'], len(near)) + (' (%s at %.2f m)' % (near[0][1].split('/')[-1], near[0][0]) if near else ''))
    for i, a in enumerate(rowsAon):
        for b in rowsAon[i + 1:]:
            if a.get('solid') and b.get('solid') and geo[a['id']][1] and geo[b['id']][1]:
                dd = min(L.poly_poly_d(ca, cb) for ca in geo[a['id']][1] for cb in geo[b['id']][1]); rep.c(dd >= rules['stack_gap_m'], 'A11', '%s / %s: collider gap %.2f m >= %.1f (a walker passes between two of them or they read as one)' % (a['id'], b['id'], dd, rules['stack_gap_m']))
    n_stack = sum(1 for r in rowsAon if r['mesh'].startswith('LogStack')); logs = {'LogStack7': 7, 'LogStack6': 6, 'LogStack5': 5, 'LogStackFallen': 7}
    rep.c(3 <= n_stack <= 4 and any(r['mesh'] == 'LogStackFallen' for r in rowsAon) and any(r['mesh'] == 'SkidWay' for r in rowsAon) and any(r['mesh'] == 'Tripod' and r.get('solid') for r in rowsAon) and len({r['mesh'] for r in rowsAon if r['mesh'].startswith('Stump')}) >= 2, 'A12',
          'yard = %d log stacks (%s logs), one half collapsed, a skid way, a tripod with a slung log, a chopping block, %d stumps of %d shapes' % (n_stack, ' / '.join(str(logs[r['mesh']]) for r in rowsAon if r['mesh'] in logs), sum(1 for r in rowsAon if r['mesh'].startswith('Stump')), len({r['mesh'] for r in rowsAon if r['mesh'].startswith('Stump')})))
    # ================================================================================== B: the rope elder
    specB = next(p for p in T['points'] if p['id'] == 'sinmok_rope_keeper308'); specB0 = next(p for p in T0['points'] if p['id'] == 'sinmok_rope_keeper308'); pb_ = P['person']['sinmok_rope_keeper308']
    atB, slB, clB, whyB = pick(W, specB); oldB = (specB0['places'][0]['x'], specB0['places'][0]['z'])
    rep.c(atB is not None, 'B1', 'elder: a candidate place stands - %s' % ('(%.1f, %.1f), lattice slope %.1f deg <= %g%s' % (atB[0], atB[1], slB, specB['max_slope_deg'], '; passed over: ' + ' | '.join(whyB) if whyB else '') if atB else ' | '.join(whyB)))
    Brow = next(p for p in P['places'] if p['id'] == 'B_rope'); personB = next(n for n in T['people'] if n['id'] == 'sinmok_rope_keeper308'); Bon = Brow['rows'] if Brow.get('enabled', True) else []
    winB = (specB['places'][0]['x'] - 70, specB['places'][0]['x'] + 50, specB['places'][0]['z'] - 45, specB['places'][0]['z'] + 35); seedsB = F.seeds(winB); rowsB = F.veg_rows(winB)
    half = W.width[pb_['route']] / 2; hung_n = 0
    rep.c(len(specB['places']) == 1 or not any(pr.get('hang_m') is not None for pr in personB['props']), 'B9', 'elder: %d candidate place(s) - a prop that hangs on a fixed peg allows exactly one (no fallback place)' % len(specB['places']))
    for pl in specB['places']:
        q = (pl['x'], pl['z']); cl = clear_lines(W, specB, q); dr = W.route_d(q, [pb_['route']])[0]; tag = '(%.1f, %.1f)' % q; yawB = body_yaw(personB, q)
        rep.c(F.tri_slope(*q) <= specB['max_slope_deg'] and all(ok for ok, _ in cl), 'B1', 'elder candidate %s: slope %.1f deg; %s' % (tag, F.tri_slope(*q), '; '.join(t for _, t in cl)))
        rep.c(pb_['route_m'][0] <= dr <= pb_['route_m'][1], 'B2', 'elder candidate %s: %.2f m from the centre line of %s, in [%g, %g] (beside the path, off its trodden width)' % (tag, dr, pb_['route'], pb_['route_m'][0], pb_['route_m'][1]))
        for pr in personB['props']:
            pq, wyaw = prop_pose(personB, q, pr); name = pr['name']
            if name in pb_['hung'] or pr.get('hang_m') is not None:
                hung_n += 1; hg = pb_['hung'].get(name); gate = next((r for r in Bon if hg and r['id'] == hg['row']), None); anch = (mesh_of(P, gate['mesh']).get('anchors') or {}).get(hg['anchor']) if gate else None
                if not (hg and gate and anch and pr.get('hang_m') is not None): rep.c(False, 'B9', 'elder %s prop %s: hangs on a peg of the rope gate - NOT: no row / anchor / hang_m for it' % (tag, name)); continue
                aw = to_world((gate['x'], gate['z']), gate['yaw'], anch[0], anch[2]); peg = F.h(gate['x'], gate['z']) - gate.get('sink_m', 0.0) + anch[1]; ps = prefab_size(pr['prefab']); H = ps['size'][1] * pr['scale']
                pivot = F.h(*pq) + pr['hang_m']; topat = pivot + pb_['hang_top_share'] * H
                rep.c(d2(pq, aw) <= pb_['hang_xz_tol_m'] and abs(topat - peg) <= pb_['hang_y_tol_m'] and pr['hang_m'] >= pb_['hang_clear_min_m'], 'B9',
                      'elder %s prop %s: hangs on %s of %s - %.3f m from the peg in XZ (<= %.2f), its top loop %.3f m from the peg height (<= %.2f), its lower end %.2f m over the ground under it (>= %.1f)' % (tag, name, hg['anchor'], hg['row'], d2(pq, aw), pb_['hang_xz_tol_m'], abs(topat - peg), pb_['hang_y_tol_m'], pr['hang_m'], pb_['hang_clear_min_m']))
                continue
            if not prop_ground(pr): continue
            seat = prop_seat(T, pr, pq, wyaw); box = prop_box(pr, pq, wyaw)
            if seat['kind'] == 'tilt': rep.c(seat['residual'] <= pb_['tilt_residual_max_m'] and seat['tilt'] <= pb_['tilt_max_deg'], 'B10', 'elder %s prop %s: laid on the slope (tilt %.1f deg <= %g) - the ground under its four corners is within %.3f m of that plane (<= %.2f)' % (tag, name, seat['tilt'], pb_['tilt_max_deg'], seat['residual'], pb_['tilt_residual_max_m']))
            else: rep.c(seat['float'] <= pb_['float_max_m'] and seat['bury'] <= pb_['bury_max_m'], 'B10', 'elder %s prop %s: upright%s - its rim is at most %.3f m over the ground (<= %.2f) and %.3f m under it (<= %.2f)' % (tag, name, (', sunk %.2f m' % pr['sink_m']) if pr.get('sink_m') else '', max(seat['float'], 0.0), pb_['float_max_m'], max(seat['bury'], 0.0), pb_['bury_max_m']))
            edge = min(W.route_d(c, [pb_['route']])[0] for c in poly_samples(box, 0.25))
            rep.c(edge >= half + pb_['path_clear_pad_m'], 'B11', 'elder %s prop %s: its edge is %.2f m from the path centre line >= the trodden half width %.1f + %.1f' % (tag, name, edge, half, pb_['path_clear_pad_m']))
            ns = int(sum(1 for s_ in seedsB if poly_pt_d(box, (s_[0], s_[2])) <= rules['grass_pad_m'])); tr = [t for t in rowsB if poly_pt_d(box, (t['pos'][0], t['pos'][2])) - (t['trunk'] if t['tree'] else t['R']) <= rules['trunk_clear_m']]
            rep.c(d2(pq, q) <= pb_['prop_reach_m'] and ns == 0 and not tr, 'B6', 'elder %s prop %s: %.2f m from him (<= %.1f); grass seeds within %.1f m of it: %d, vegetation rows within %.1f m: %d (no grass op)' % (tag, name, d2(pq, q), pb_['prop_reach_m'], rules['grass_pad_m'], ns, rules['trunk_clear_m'], len(tr)))
    rep.c(hung_n == len(pb_['hung']) * len(specB['places']), 'B9', 'elder: every prop named in person.hung hangs (%d hanging prop(s))' % hung_n)
    rep.c(specB['clear']['boss_min_m'] == specB0['clear']['boss_min_m'] and specB['clear']['rests_min_m'] == specB0['clear']['rests_min_m'], 'B1', 'elder: the fight rules are the D308-19 ones (boss >= %g, rest >= %g); given up: max_slope_deg %g -> %g (EX-M2)' % (specB['clear']['boss_min_m'], specB['clear']['rests_min_m'], specB0['max_slope_deg'], specB['max_slope_deg']))
    rep.c('EX-M2' in ex and ex['EX-M2']['now'] == specB['max_slope_deg'], 'B1', 'exception EX-M2 is recorded in places308.json with the value the text data carries (%g)' % specB['max_slope_deg'])
    if atB:
        e = W.enc[specB['clear']['boss']]; dboss = d2(atB, F.P3(e['Feet'])[::2])
        rep.c(dboss > e['Leash'] and dboss > e['Detection'], 'B7', 'elder: %.1f m from the tree, outside its Leash %g and Detection %g' % (dboss, e['Leash'], e['Detection']))
        gt = pb_['gate']
        for r in Bon:
            fp, cols, top = geo[r['id']]; m = mesh_of(P, r['mesh']); span = (m['hi'][2] - m['lo'][2]) / 2 - 0.1; posts = [to_world((r['x'], r['z']), r['yaw'], 0, s_ * span) for s_ in (-1, 1)]; piv = F.h(r['x'], r['z']) - r.get('sink_m', 0)
            dpost = min(W.route_d(q, [pb_['route']])[0] for q in posts); bury = min(F.h(*q) - (piv + m['lo'][1]) for q in posts); lift = max(piv - F.h(*q) for q in posts)
            rep.c(not r.get('solid') and not m['colliders'], 'B4', '%s: no collider (nothing to bake, nothing to cut a prompt ray)' % r['id'])
            rep.c(dpost >= rules['post_clear_m'], 'B4', '%s: nearer pole %.2f m from the path centre line >= %.1f (off the trodden width)' % (r['id'], dpost, rules['post_clear_m']))
            rep.c(bury >= gt['pole_bury_min_m'] and lift <= gt['lift_max_m'], 'B4', '%s: each pole foot is %.2f m or more under its own ground (>= %.1f: no pole floats); the rope hangs up to %.2f m lower over the ground at one pole than at the pivot (<= %.2f)' % (r['id'], bury, gt['pole_bury_min_m'], lift, gt['lift_max_m']))
            rep.c(gt['from_elder_m'][0] <= d2((r['x'], r['z']), atB) <= gt['from_elder_m'][1], 'B4', '%s: %.2f m from the elder, in [%g, %g] (the rope hangs at his work place, not on him)' % (r['id'], d2((r['x'], r['z']), atB), gt['from_elder_m'][0], gt['from_elder_m'][1]))
            tr = [t for t in rowsB if min(d2(q, (t['pos'][0], t['pos'][2])) for q in posts) - (t['trunk'] if t['tree'] else t['R']) <= rules['trunk_clear_m']]
            rep.c(not tr, 'B4', '%s: vegetation rows within %.1f m of a pole: %d' % (r['id'], rules['trunk_clear_m'], len(tr)))
            eb = W.enc[specB['clear']['boss']]; rep.c(min(d2(q, F.P3(eb['Feet'])[::2]) for q in posts) > eb['Leash'], 'B4', '%s: both poles outside the tree\'s Leash %g (%.1f m)' % (r['id'], eb['Leash'], min(d2(q, F.P3(eb['Feet'])[::2]) for q in posts)))
    # S7 (not this stage's objects): where the old rope and the rest stand against the height field - reported, never a PASS
    lows = []
    for k in pb_['old_rope_keys']:
        Tm = F.scene_mesh(k)
        if Tm is not None: Pm = Tm.reshape(-1, 3); lows.append(float(Pm[:, 1].min()) - F.h(float(Pm[:, 0].mean()), float(Pm[:, 2].mean())))
    rp = W.points.get(pb_['rest_point']); rf = W.feet.get(pb_['rest_point'])
    out['old_rope_over_ground'] = (min(lows), max(lows)) if lows else None
    rep.info('the old rope and its three posts (%s ...) stand %.1f - %.1f m ABOVE the height field by the scene data; the rest %s: point y %.2f vs field %.2f, respawn feet y %.2f vs field %.2f. Not measured in the editor (RUN_ORDER C0 probes it). Not written by this stage; "the old rope reads" is NOT claimed' % (
        pb_['old_rope_keys'][0].split('/')[-1], min(lows) if lows else float('nan'), max(lows) if lows else float('nan'), pb_['rest_point'], F.P3(rp['Position'])[1] if rp else float('nan'), F.h(*F.P3(rp['Position'])[::2]) if rp else float('nan'), rf[1] if rf else float('nan'), F.h(rf[0], rf[2]) if rf else float('nan')), 'B8')
    # ================================================================================== C: the potter
    specC = next(p for p in T['points'] if p['id'] == 'south_gate_potter308'); specC0 = next(p for p in T0['points'] if p['id'] == 'south_gate_potter308'); pc = P['person']['south_gate_potter308']
    atC, slC, clC, whyC = pick(W, specC); oldC = (specC0['places'][0]['x'], specC0['places'][0]['z']); rest = W.points[pc['rest']]; rq = F.P3(rest['Position'])[::2]; personC = next(n for n in T['people'] if n['id'] == 'south_gate_potter308')
    rep.c(atC is not None, 'C2', 'potter: a candidate place stands - %s' % ('(%.1f, %.1f), slope %.1f deg <= %g%s' % (atC[0], atC[1], slC, specC['max_slope_deg'], '; passed over: ' + ' | '.join(whyC) if whyC else '') if atC else ' | '.join(whyC)))
    gap = float(rest['Radius']) + float(specC['radius']); feet = W.feet[pc['rest']][::2]
    same = []; others = {alias: F.World(alias) for alias in ('arch296', 'folk298')}
    for alias, Wa in others.items():
        ra = Wa.points.get(pc['rest']); same.append(ra is not None and d2(F.P3(ra['Position'])[::2], rq) < 0.01 and abs(float(ra['Radius']) - float(rest['Radius'])) < 1e-6 and d2(Wa.feet[pc['rest']][::2], feet) < 0.01)
    rep.c(all(same), 'C1', 'the rest point %s (XZ, Radius %.1f) and its respawn feet are the same in the three content assets: the proof below holds in every scene' % (pc['rest'], float(rest['Radius'])))
    for alias, Wa in others.items():                                     # Texts308 picks per scene, against THAT scene's content
        for spec_, who, tag_ in ((specB, 'elder', 'B1'), (specC, 'potter', 'C2')):
            pk = pick(Wa, spec_); first = (spec_['places'][0]['x'], spec_['places'][0]['z'])
            rep.c(pk[0] == first, tag_, '%s in the content of %s: Texts308 picks the same first place %s there (%s)' % (who, alias, '(%.1f, %.1f)' % first, '; '.join(t for _, t in pk[2]) if pk[0] else ' | '.join(pk[3])))
    src = F.scene_pose('Roadside303/Givers/woodbrother303'); srck = {}
    for ch in personC['reseat_children']:
        cp = F.scene_pose('Roadside303/Givers/woodbrother303/' + ch)
        if src and cp: srck[ch] = L.to_local((src[0], 0, src[2], src[3]), cp[0], cp[2])
    for pl in specC['places']:
        q = (pl['x'], pl['z']); cl = clear_lines(W, specC, q); tag = '(%.1f, %.1f)' % q; yawC = body_yaw(personC, q)
        rep.c(d2(q, rq) > gap + pc['prompt_gap_margin_m'], 'C1', 'potter candidate %s: %.2f m from the rest point > its Radius %.1f + the potter Radius %.1f + %.1f = the two prompts are never both active (M1)' % (tag, d2(q, rq), float(rest['Radius']), float(specC['radius']), pc['prompt_gap_margin_m']))
        rep.c(F.tri_slope(*q) <= specC['max_slope_deg'] and all(ok for ok, _ in cl), 'C2', 'potter candidate %s: slope %.1f deg; %s' % (tag, F.tri_slope(*q), '; '.join(t for _, t in cl)))
        ground = [(pr['name'], prop_pose(personC, q, pr)[0], max(prefab_size(pr['prefab'])['size'][0], prefab_size(pr['prefab'])['size'][2]) * pr['scale'] / 2) for pr in personC['props'] if prop_ground(pr)]
        kids = [(ch, to_world(q, yawC, lx, lz), pc['child_radius_m'].get(ch, 0.45)) for ch, (lx, lz) in srck.items()]
        worst = min((W.route_d(c, [pc['route']])[0] - rad, n) for n, c, rad in ground + kids)
        rep.c(worst[0] >= pc['carriageway_half_m'], 'C3', 'potter %s: every ground jar and the frame stand off the carriageway - nearest edge %.2f m from the centre line of %s (%s) >= its half width %.1f; the car lane is the middle +- %.1f m' % (tag, worst[0], pc['route'], worst[1], pc['carriageway_half_m'], pc['car_lane_half_m']))
        rep.c(W.route_d(q, [pc['route']])[0] >= pc['carriageway_half_m'] + 1.0, 'C3', 'potter %s: he stands %.2f m from the centre line (>= half width %.1f + 1.0)' % (tag, W.route_d(q, [pc['route']])[0], pc['carriageway_half_m']))
        near = min(d2(c, feet) - rad for n, c, rad in ground + kids)
        rep.c(near >= pc['feet_clear_m'], 'C8', 'potter %s: nothing of his load within %.1f m of the respawn feet (nearest %.2f m)' % (tag, pc['feet_clear_m'], near))
        for pr in personC['props']:
            if not prop_ground(pr): continue
            pq, wyaw = prop_pose(personC, q, pr); seat = prop_seat(T, pr, pq, wyaw)
            rep.c(seat['kind'] == 'upright' and seat['float'] <= pc['float_max_m'] and seat['bury'] <= pc['bury_max_m'], 'C12', 'potter %s prop %s: stands upright on the ground - rim at most %.3f m over it (<= %.2f), %.3f m under it (<= %.2f)' % (tag, pr['name'], max(seat.get('float', 9), 0.0), pc['float_max_m'], max(seat.get('bury', 9), 0.0), pc['bury_max_m']))
    rep.c(abs(specC['clear']['rests_min_m'] - (gap + pc['prompt_gap_margin_m'])) < 1e-6 and 'EX-M1' in ex and ex['EX-M1']['now'] == specC['clear']['rests_min_m'], 'C1', 'rests_min_m %g = the two radii + %.1f, recorded as exception EX-M1 (was %g)' % (specC['clear']['rests_min_m'], pc['prompt_gap_margin_m'], specC0['clear']['rests_min_m']))
    rep.c(all(specC['clear'].get(k) == specC0['clear'].get(k) for k in ('road_min_m', 'road_max_m', 'enemies_min_m', 'boss_min_m', 'points_min_m')) and specC['max_slope_deg'] == specC0['max_slope_deg'], 'C3', 'potter: road [%g, %g] m, enemies >= %g, boss >= %g, slope <= %g are the D308-19 values' % (specC['clear']['road_min_m'], specC['clear']['road_max_m'], specC['clear']['enemies_min_m'], specC['clear']['boss_min_m'], specC['max_slope_deg']))
    jars = [pr for pr in personC['props'] if 'Jar' in pr['prefab'] or 'Pot' in pr['prefab']]; onframe = [pr for pr in jars if pr.get('on')]
    rep.c(all(pr['prefab'].split('/')[-1] in pc['plain_jars'] for pr in jars), 'C9', 'potter: every jar is the brown earthenware %s (%s) - no white moon jar SM_046_Pot, no painted SM_038_Pot (Texts308 replaces a prop whose prefab changed)' % (' / '.join(pc['plain_jars']), ', '.join('%s %s' % (pr['name'], pr['prefab'].split('/')[-1][:-7]) for pr in jars)))
    rep.c(jars and len(onframe) >= pc['frame_share_min'] * len(jars), 'C11', 'potter: %d of his %d jars ride the frame (>= %.0f %%) - "옹기는 여러 날째 지게 위요" (D308-19 T4; run 2 had 2 of 4, rev 1 of this stage 2 of 7)' % (len(onframe), len(jars), 100 * pc['frame_share_min']))
    if atC:
        yawC = body_yaw(personC, atC)
        rep.c(d2(atC, rq) <= pc['group_max_m'] and d2(atC, rq) < d2(oldC, rq), 'C7', 'potter: %.2f m from the 성황당 돌무더기 (was %.2f), <= %.1f' % (d2(atC, rq), d2(oldC, rq), pc['group_max_m']))
        ground = [(pr['name'], prop_pose(personC, atC, pr)[0]) for pr in personC['props'] if prop_ground(pr)]
        esc = [rid for rid in W.routes if rid.endswith('__capital_delivery')]; de = min(W.route_d(atC, [rid])[0] for rid in esc) if esc else float('nan'); dstop = d2(atC, F.P3(W.stages['delivery']['Destination'])[::2])
        rep.c(de > 50, 'C10', 'potter: the escort cart ends at the delivery stop (stage delivery, %.0f m from him along another road: %s is %.0f m away) - the cart never drives %s' % (dstop, esc[0] if esc else '?', de, pc['route']))
        nearjar = min(d2(c, rq) for n, c in ground) if ground else 1e9
        rep.c(nearjar < d2(atC, rq), 'C6', 'potter: a ground jar stands toward the shrine (nearest ground jar %.2f m from the 돌무더기, he %.2f m)' % (nearjar, d2(atC, rq)))
    out.update(atB=atB, oldB=oldB, atC=atC, oldC=oldC, geo=geo, lf=lf, existing=existing)
    # ================================================================================== V: what is in view
    if views and atB and atC:
        rd = P['read']; fov, w, hh = rd['fov'], rd['w'], rd['h']; out['views'] = {}
        # ---- A
        tree_key = 'CheongrimProgression251/demo_growth_lesson/Chapter3_Visual/Tree_00'; base = base_layers(winA) + subtree_layers('CheongrimRoadStories264/TaintedTools264', COL['old'], ID['existing']) + subtree_layers('CheongrimProgression251/GukRevisit251', COL['rock'], ID['existing'])
        lesson = keys_layers([tree_key], COL['lesson'], ID['lesson']); newA = rows_layers(P, A) if A.get('enabled', True) else []
        ref = tuple(A['read_ref']); rl = A['read_look']; eyesA = eyes_for(W, A['approach_route'], ref, rd['distances_m']) + eyes_for(W, A['approach_route'], lf, [A['lesson_eye_m']])
        yl = (rl[0], F.h(*rl) + 0.8, rl[1]); TT = lesson[0][0].reshape(-1, 3) if lesson else np.zeros((1, 3)); out['lesson_height'] = float(TT[:, 1].max() - F.h(*lf)); looks = [yl, yl, yl, (lf[0], F.h(*lf) + 1.5, lf[1])]
        rep.info('the lesson target itself (%s): real mesh x3, %.1f m tall, %.1f m wide - a young tree, not a big one' % (tree_key.split('/')[-1], out['lesson_height'], float(np.ptp(TT[:, 0]))))
        if 'stone' in out: rep.info('the stone platform of the 국 revisit is drawn in both states as its measured box: %.1f x %.1f m, top %.2f m over the ground beside it (rev 1 left it out of every view)' % (d2(out['stone']['fp'][0][0], out['stone']['fp'][0][1]), d2(out['stone']['fp'][0][1], out['stone']['fp'][0][2]), out['stone']['height']))
        for dist, e, look in zip(rd['distances_m'] + ['L'], eyesA, looks):
            eye = F.eye_on(e[0], e[1], rd['eye_m']); b = F.shown(base + lesson, [ID['lesson']], eye, look, fov, w, hh); a = F.shown(base + lesson + newA, [ID['lesson']], eye, look, fov, w, hh); st = F.shown(base + lesson + newA, [ID['stacks']], eye, look, fov, w, hh)
            out['views'][('A', dist)] = dict(eye=eye, look=look, before=base + lesson, after=base + lesson + newA, lesson_before=b, lesson_after=a, stacks=st)
            if dist != 'L' and b[0] > 0: rep.c(a[0] >= b[0] * rd['lesson_keep_share'], 'V-A5', 'A %d m before the yard: the lesson tree keeps %.0f %% of the pixels it showed before (%d -> %d) >= %.0f %%' % (dist, 100.0 * a[0] / b[0], b[0], a[0], 100 * rd['lesson_keep_share']))
            if dist == 'L':
                rep.c(a[0] >= b[0] * rd['lesson_keep_share'] and a[2] >= rd['lesson_keep_share'], 'V-A5', 'A, %d m before the lesson tree: it shows %.0f %% of itself (%d px; before the yard %d px) - nothing new stands in front of it' % (A['lesson_eye_m'], 100 * a[2], a[0], b[0])); continue
            if str(dist) in rd['yard_share_min']:
                rep.c(st[2] >= rd['yard_share_min'][str(dist)] and st[3] >= rd['yard_frame_min'][str(dist)], 'V-A6', 'A %d m before the yard: the log stacks and the tripod show %.0f %% of themselves (>= %.0f) and fill %.2f %% of the frame (>= %.2f); before: 0' % (dist, 100 * st[2], 100 * rd['yard_share_min'][str(dist)], 100 * st[3], 100 * rd['yard_frame_min'][str(dist)]))
        # ---- B
        baseB = base_layers(winB); oldrope = keys_layers(pb_['old_rope_keys'], COL['trunk'], ID['oldrope'])
        newB = rows_layers(P, Brow) if Brow.get('enabled', True) else []; manB = person_layers(T, 'sinmok_rope_keeper308', atB); manB0 = person_layers(T0, 'sinmok_rope_keeper308', oldB)
        eyesB = eyes_for(W, pb_['route'], atB, [45, 15, 6]); tgt = (atB[0], F.h(*atB) + 1.2, atB[1])
        for dist, e in zip([45, 15, 6], eyesB):
            eye = F.eye_on(e[0], e[1], rd['eye_m']); la = baseB + oldrope + newB + manB; lb = baseB + oldrope + manB0
            m_a = F.shown(la, [ID['man']], eye, tgt, fov, w, hh); m_b = F.shown(lb, [ID['man']], eye, tgt, fov, w, hh); pp = F.shown(la, [ID['paper']], eye, tgt, fov, w, hh); orp = F.shown(la, [ID['oldrope']], eye, tgt, fov, w, hh); prp = F.shown(la, [ID['manprops']], eye, tgt, fov, w, hh)
            out['views'][('B', dist)] = dict(eye=eye, look=tgt, before=lb, after=la, man_after=m_a, man_before=m_b, paper=pp, oldrope=orp, props=prp)
            if str(dist) in rd['person_share_min']:
                rep.c(m_a[2] >= rd['person_share_min'][str(dist)], 'V-B3', 'B %d m before him on the path: the elder shows %.0f %% of himself (%d px) >= %.0f %%; at his old place from the same eye: %d px in the frame. His straw stack, mat, sheaves and hanks: %d px' % (dist, 100 * m_a[2], m_a[0], 100 * rd['person_share_min'][str(dist)], m_b[0], prp[0]))
                rep.c(pp[0] >= rd['strips_px_min'][str(dist)], 'V-B4', 'B %d m: white paper of the new rope in view %d px (>= %d), %.0f %% of the strips unhidden' % (dist, pp[0], rd['strips_px_min'][str(dist)], 100 * pp[2]))
            if dist == 15: rep.info('B 15 m: the old rope and its posts are drawn where the scene data puts them (%d px in this frame) - that is %.1f m or more over the ground (B8): this number is NOT evidence that the old rope reads' % (orp[0], min(lows) if lows else float('nan')), 'V-B5')
        # ---- C
        winC = (rq[0] - 45, rq[0] + 40, rq[1] - 62, rq[1] + 40); baseC = base_layers(winC); altar = subtree_layers(pc['altar_key'], COL['altar'], ID['altar'])
        manC = person_layers(T, 'south_gate_potter308', atC, moved_from=oldC); manC0 = person_layers(T0, 'south_gate_potter308', oldC, moved_from=oldC)
        eyesC = eyes_for(W, pc['route'], rq, rd['distances_m']); mid = ((atC[0] + rq[0]) / 2, (atC[1] + rq[1]) / 2); tgtC = (mid[0], F.h(*mid) + 1.0, mid[1])
        for dist, e in zip(rd['distances_m'], eyesC):
            eye = F.eye_on(e[0], e[1], rd['eye_m']); la = baseC + altar + manC; lb = baseC + altar + manC0
            g = F.shown(la, [ID['man'], ID['manprops'], ID['altar']], eye, tgtC, fov, w, hh)
            span = abs((math.degrees(math.atan2(atC[0] - e[0], atC[1] - e[1])) - math.degrees(math.atan2(rq[0] - e[0], rq[1] - e[1])) + 180) % 360 - 180)
            span0 = abs((math.degrees(math.atan2(oldC[0] - e[0], oldC[1] - e[1])) - math.degrees(math.atan2(rq[0] - e[0], rq[1] - e[1])) + 180) % 360 - 180)
            out['views'][('C', dist)] = dict(eye=eye, look=tgtC, before=lb, after=la, group=g, span=span, span0=span0, depth=(d2(e, atC), d2(e, rq)))
            if dist >= 15: rep.c(g[2] >= rd['group_share_min'] and span <= rd['group_span_max_deg'], 'V-C6', 'C %d m on the road: man + load + 돌무더기 show %.0f %% of themselves (>= %.0f); man and shrine %.1f deg apart (<= %.0f; before %.1f), he %.1f m and the cairn %.1f m from the eye (distance apart %.1f -> %.1f m)' % (dist, 100 * g[2], 100 * rd['group_share_min'], span, rd['group_span_max_deg'], span0, d2(e, atC), d2(e, rq), d2(oldC, rq), d2(atC, rq)))
        eye = F.eye_on(feet[0], feet[1], rd['eye_m']); tg = (atC[0], F.h(*atC) + 1.0, atC[1]); la = baseC + altar + manC; m_r = F.shown(la, [ID['man']], eye, tg, fov, w, hh)
        out['views'][('C', 'rest')] = dict(eye=eye, look=tg, before=baseC + altar + manC0, after=la, man=m_r)
        rep.c(m_r[2] >= rd['group_share_min'], 'V-C7', 'C, standing on the respawn feet of the shrine and turned to him: the potter shows %.0f %% of himself (%d px) >= %.0f %% - nothing stands between the rest and him' % (100 * m_r[2], m_r[0], 100 * rd['group_share_min']))
    return rep, out


# ------------------------------------------------------------------------------------------------ mutations

def mutations(P, T):
    def setrow(P, rid, **kw):
        for pl in P['places']:
            for r in pl['rows']:
                if r['id'] == rid: r.update(kw)
    def delkey(P, rid, key):
        for pl in P['places']:
            for r in pl['rows']:
                if r['id'] == rid: r.pop(key, None)
    def point(T, pid): return next(p for p in T['points'] if p['id'] == pid)
    def person(T, pid): return next(p for p in T['people'] if p['id'] == pid)
    def prop(T, pid, name): return next(pr for pr in person(T, pid)['props'] if pr['name'] == name)
    tmp = Path(tempfile.mkdtemp(prefix='fix4_mut_'))
    def flat_mesh(P, T):
        shutil.copytree(DATA / 'meshes', tmp / 'meshes', dirs_exist_ok=True); f = tmp / 'meshes' / 'RopeGate.json'; d = json.loads(f.read_text(encoding='utf-8'))
        d['n'][0:3] = [0.0, 0.0, 0.0]; t = d['sub'][0]['t']; t[1] = t[0]; f.write_text(json.dumps(d, separators=(',', ':')), encoding='utf-8'); P['_mesh_folder'] = str(tmp / 'meshes')
    E, PT = 'sinmok_rope_keeper308', 'south_gate_potter308'
    M = [
     ('a stack on the path', 'A1', lambda P, T: setrow(P, 'A_stack_2', x=3342.6), False),
     ('the slung log of the tripod over the path', 'A1', lambda P, T: setrow(P, 'A_tripod', x=3342.4, z=2686.0), False),
     ('a stack inside the lesson\'s detection', 'A3', lambda P, T: setrow(P, 'A_stack_2', z=2692.5), False),
     ('a stump on the lesson point', 'A4', lambda P, T: setrow(P, 'A_stump_1', x=3341.5, z=2701.0), False),
     ('the block on the footpath to the stone platform', 'A2', lambda P, T: setrow(P, 'A_block', x=3329.2, z=2684.0), False),
     ('a stack into the trees east of the path', 'A8', lambda P, T: setrow(P, 'A_stack_3', x=3352.6, z=2687.6), False),
     ('a stack onto the sorting trestle', 'A9', lambda P, T: setrow(P, 'A_stack_3', x=3360.6, z=2690.6, solid=False), False),
     ('stack 1 back against the stone platform (its rev-1 place)', 'A9', lambda P, T: setrow(P, 'A_stack_1', x=3334.6, z=2679.2), False),
     ('the three-layer stack as a step beside the stone platform', 'A13', lambda P, T: setrow(P, 'A_stack_2', x=3336.4, z=2675.0, yaw=0.0, scale=1.25), False),
     ('two stacks into each other', 'A11', lambda P, T: setrow(P, 'A_stack_1', x=3337.6, z=2683.4), False),
     ('a stack on the 22 deg bank', 'A7', lambda P, T: setrow(P, 'A_stack_4', x=3312.0, z=2664.0), False),
     ('a solid row whose mesh has no collider box', 'D3', lambda P, T: setrow(P, 'A_block', solid=True), False),
     ('a mesh json changed after the index', 'D1', lambda P, T: P['meshes'][0].__setitem__('sha256', '0' * 64), False),
     ('a mesh with a flat triangle and a zero normal', 'D5', flat_mesh, False),
     ('a rule number taken out of the data', 'D6', lambda P, T: P['rules'].pop('path_run_gap_m'), False),
     ('a glowing material', 'D2', lambda P, T: P['materials']['paper'].update(path='Assets/_Project/Art/World/Finish297/Materials/candle.mat', guid='?'), False),
     ('the yard stacks hide the lesson tree', 'V-A5', lambda P, T: (setrow(P, 'A_stack_2', x=3342.6, z=2692.0, yaw=90.0), setrow(P, 'A_stack_1', x=3341.6, z=2690.0, yaw=90.0)), True),
     ('the rope gate without its own corner limit (rev 1: the run order stopped here)', 'R1', lambda P, T: delkey(P, 'B_rope_gate', 'corner_gap_max_m'), False),
     ('a row on the south-gate road (the offering jar of rev 1)', 'P1', lambda P, T: setrow(P, 'A_block', x=1923.18, z=2448.92), False),
     ('the elder back on his old shelf', 'B2', lambda P, T: point(T, E).update(places=[{"x": 3687.0, "z": 3217.0, "y_offline": 221.55}]), False),
     ('the elder inside the fight circle', 'B1', lambda P, T: point(T, E).update(places=[{"x": 3676.0, "z": 3239.0, "y_offline": 222.0}]), False),
     ('the elder beside the rest', 'B1', lambda P, T: point(T, E).update(places=[{"x": 3662.5, "z": 3238.0, "y_offline": 216.0}]), False),
     ('the boss rule loosened in the text data', 'X2', lambda P, T: point(T, E)['clear'].update(boss_min_m=30.0), False),
     ('a Texts308 rule number changed', 'X1', lambda P, T: T['rules'].update(y_tol_m=0.5), False),
     ('a prop of run 2 dropped from the data (it would stay in the scene)', 'X2', lambda P, T: person(T, E)['props'].pop(1), False),
     ('the elder hidden from the path (his old place, seen from the new eyes)', 'V-B3', lambda P, T: (point(T, E).update(places=[{"x": 3687.0, "z": 3217.0, "y_offline": 221.55}], max_slope_deg=16), P['person'][E].update(route_m=[0, 99])), True),
     ('a rope pole on the path', 'B4', lambda P, T: setrow(P, 'B_rope_gate', z=3245.2), False),
     ('the rope gate solid', 'B4', lambda P, T: setrow(P, 'B_rope_gate', solid=True), False),
     ('the mat laid flat on the 12 deg slope (rev 1)', 'B10', lambda P, T: prop(T, E, 'WorkMat').pop('tilt'), False),
     ('the straw stack not sunk (its downhill edge in the air)', 'B10', lambda P, T: prop(T, E, 'StrawHeap').pop('sink_m'), False),
     ('the mat across the path', 'B11', lambda P, T: prop(T, E, 'WorkMat').update(local=[2.3, 0.0, 0.6]), False),
     ('a rope hank beside its peg', 'B9', lambda P, T: prop(T, E, 'RopeCoil0').update(local=[0.6, 0.0, 0.55]), False),
     ('a rope hank back on the ground (rev 1: a standing disc)', 'B9', lambda P, T: prop(T, E, 'RopeCoil1').pop('hang_m'), False),
     ('a second place for the elder while his hanks hang on fixed pegs', 'B9', lambda P, T: point(T, E)['places'].append({"x": 3659.6, "z": 3245.2, "y_offline": 214.9}), False),
     ('a line of the old man changed', 'X1', lambda P, T: T['lines']['T3_first'].update(text=T['lines']['T3_first']['text'] + ' '), False),
     ('a new prompt', 'X1', lambda P, T: point(T, PT).update(prompt='T3_prompt'), False),
     ('the potter inside the rest prompt (the place the verifier named)', 'C1', lambda P, T: point(T, PT).update(places=[{"x": 1925.5, "z": 2446.5, "y_offline": 95.83}]), False),
     ('the potter on the carriageway', 'C2', lambda P, T: point(T, PT).update(places=[{"x": 1923.6, "z": 2443.0, "y_offline": 95.6}]), False),
     ('rests_min_m below the two radii', 'C1', lambda P, T: point(T, PT)['clear'].update(rests_min_m=4.0), False),
     ('a jar on the road', 'C3', lambda P, T: prop(T, PT, 'Jar3').update(local=[0.35, 0.0, 2.4]), False),
     ('a jar on the road at the third candidate place only', 'C3', lambda P, T: point(T, PT)['places'].__setitem__(2, {"x": 1918.3, "z": 2449.6, "y_offline": 95.9}), False),
     ('the painted jar back', 'C9', lambda P, T: prop(T, PT, 'Jar3').update(prefab='Assets/Korea_TreasureProps/Prefabs/SM_038_Pot.prefab'), False),
     ('the white moon jar back', 'C9', lambda P, T: prop(T, PT, 'Jar2').update(prefab='Assets/Korea_TreasureProps/Prefabs/SM_046_Pot.prefab'), False),
     ('six jars, four of them on the ground (rev 1)', 'C11', lambda P, T: person(T, PT)['props'].extend([dict(prop(T, PT, 'Jar2'), name='Jar4', local=[2.5, 0.0, -0.3]), dict(prop(T, PT, 'Jar2'), name='Jar5', local=[2.6, 0.0, -0.9])]), False),
     ('the potter left where he is (15.7 m from the shrine)', 'C7', lambda P, T: point(T, PT).update(places=[{"x": 1925.5, "z": 2434.5, "y_offline": 95.53}]), False),
     ('a jar on the respawn feet', 'C8', lambda P, T: prop(T, PT, 'Jar3').update(local=[-0.9, 0.0, 7.3]), False),
    ]
    caught = 0; lines = []
    for name, tag, fn, needs_views in M:
        p = copy.deepcopy(P); t = copy.deepcopy(T); fn(p, t)
        try: rep, _ = run(p, t, views=needs_views); hit = [r for r in rep.fails() if r[1] == tag]; other = [r for r in rep.fails() if r[1] != tag]
        except Exception as e: hit = []; other = [(False, 'EXC', repr(e))]
        ok = bool(hit); caught += ok
        lines.append('%s  %-70s -> %s%s' % ('RED ' if ok else 'MISS', name, ('FAIL ' + tag + ': ' + hit[0][2][:110]) if ok else 'rule %s stayed green' % tag, '' if ok or not other else ' (other: %s)' % other[0][2][:80]))
    shutil.rmtree(tmp, ignore_errors=True)
    return caught, len(M), lines


# ------------------------------------------------------------------------------------------------ main

def load(folder):
    P = json.loads((folder / 'places308.json').read_text(encoding='utf-8')); T = json.loads((folder / 'texts308.json').read_text(encoding='utf-8'))
    if folder != DATA: P['_mesh_folder'] = str(folder / 'meshes')
    return P, T


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace'); a = sys.argv[1:]
    folder = Path(a[a.index('--data') + 1]).resolve() if '--data' in a else DATA; P, T = load(folder)
    if 'mutations' in a:
        caught, n, lines = mutations(P, T)
        for l in lines: print(l)
        print('RESULT %d mutation(s), %d caught, %d not caught' % (n, caught, n - caught)); return 0 if caught == n else 1
    rep, out = run(P, T, views='--no-views' not in a)
    print('relayout_fix4 dry: places308.json %s (%s), texts308.json %s (%s), scene Main %s, height %s' % (F.sha12(folder / 'places308.json'), P['version'], F.sha12(folder / 'texts308.json'), T['version'], F.scene().sha[:12], F.sha12(S.HEIGHT_P1B)))
    for ok, tag, text in rep.rows: print('%s %-5s %s' % ('PASS' if ok else 'INFO' if ok is None else 'FAIL', tag, text))
    np_, nf, ni = sum(1 for r in rep.rows if r[0] is True), len(rep.fails()), sum(1 for r in rep.rows if r[0] is None)
    solid = [r['id'] for pl, r in rows_on(P) if r.get('solid')]
    print('needs the NavMesh bake: collider rows %s + the two people moved by Texts308 (their bodies carry a CapsuleCollider)' % (', '.join(solid) or 'none'))
    print('no collider: %s' % ', '.join(r['id'] for pl, r in rows_on(P) if not r.get('solid')))
    print('RESULT PASS %d / FAIL %d / INFO %d%s' % (np_, nf, ni, '' if '--no-views' not in a else ' (views skipped)'))
    if '--pictures' in a and 'views' in out:
        import relayout_fix4_draw as DR
        DR.draw_all(P, T, out)
    return 0 if nf == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
