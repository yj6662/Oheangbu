# -*- coding: utf-8 -*-
"""#308 D308-18 답 5 - offline dry run of the wood profile for three beasts (mine_beast/1, forest_beast308/0, /1). Read only.

  python Tools/Art/wood308_dry.py [--data <wood308.json>] [--out <dir>] [--no-scenes]

Reads the wood rows (default Tools/Unity/Stage308_world18/wood/Data/wood308.json, else the deployed copy), the three world
contents (YAML text), the three candidate scenes (YAML text: actor, profile reference, body, crystal), the source attack profile,
the crystal placement / fit records and the rest places: every checkpoint feet / Rest point of each content with the relayout
rows that the running editor pass writes laid over them (fix stage content308_relayout.json, else the deployed copy) plus the
road inn (relayout308_ext.json XI1).
Every number is OFFLINE ([O] = read from a file here): the editor decides (Content308 Wood check / scene-check, OrganSurface308
check), Play decides the parry (AC-W7..W9).
Then the same checks run on mutated copies of the data: each mutation must turn at least one line red.
Exit code 1 when a FAIL row exists or a mutation stays green. Writes wood308_dry.json / wood308_dry.txt into --out.
"""
import argparse, copy, hashlib, json, math, re, sys
from pathlib import Path

try: sys.stdout.reconfigure(encoding='utf-8')
except Exception: pass
ROOT = Path(__file__).resolve().parents[2]
CB = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308'
STAGE = ROOT / 'Tools/Unity/Stage308_world18/wood'
FIX = ROOT / 'Tools/Unity/Stage308_relayout_fix'
EXT = ROOT / 'Tools/Unity/Stage308_relayout_ext'
ASSETS = ROOT / 'Oheangbu'
CONTENT = {'arch296': 'Assets/_Project/Art/World/Architecture296/Data/a3cb428dc79f23e45a4caf7729b52193_7a0b9698e5728bd4ea84bd5116570b9f_2b30a7723e5289c47a4fd3cde0d788cf_Content.asset',
           'folk298': 'Assets/_Project/Art/Characters/Folklore298/Data/WorldContent298.asset',
           'main': 'Assets/_Project/Scenes/World/Main/WorldContent_Main.asset'}
SCENE = {'arch296': 'Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity',
         'folk298': 'Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity',
         'main': 'Assets/_Project/Scenes/World/W_Demo_Main.unity'}
SHARD_DIR = 'Assets/_Project/Art/Characters/Organs308/Shard308'
SPECIES_PREFABS = 'Assets/_Project/Art/Characters/Folklore298/Prefabs'
CRYSTAL = 'Organ308_maseok_shard'
COMBAT_CONFIG = 'Assets/_Project/Data/Configs/CombatConfig_Default.asset'
PROTECTED = ('watershed295', 'reworld292', 'mountaintrail285', 'w_demo_compact.unity', '03_content.asset')
NUM = r'([-\d.e]+)'


def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def guid_of(asset):
    m = re.search(r'guid: (\w+)', (ASSETS / (asset + '.meta')).read_text(encoding='utf-8')); return m.group(1) if m else None


# ---------- contents ----------

def read_content(alias):
    c = (ASSETS / CONTENT[alias]).read_text(encoding='utf-8')

    def block(name, nxt):
        a = c.index('\n  %s:' % name); b = c.index('\n  %s:' % nxt, a); return c[a:b]
    # one match per list entry: the body runs up to the next entry (lookahead - a consuming pattern skips every second entry)
    entries = lambda text: re.finditer(r'^  - Id: (\S+)\n((?:    .*\n)*)', text + '\n', re.M)
    enc = {}
    for m in entries(block('Encounters', 'MainPath')):
        b = m.group(2); f = re.search(r'Feet: \{x: %s, y: %s, z: %s\}' % (NUM, NUM, NUM), b)
        g = lambda k, d=None: (re.search(r'\n?\s+%s: (\S+)' % k, b) or [None, d])[1]
        enc[m.group(1)] = dict(x=float(f.group(1)), z=float(f.group(3)), content_id=g('ContentId', ''), ranged=g('Ranged') == '1', respawn=g('RespawnOnRest') == '1',
                               detection=float(g('Detection', 'nan')), leash=float(g('Leash', 'nan')), speed=float(g('Speed', 'nan')), activation=float(g('Activation', 'nan')))
    cp = {}
    for m in re.finditer(r'- Id: (\S+)\n(?:.*\n)*?\s+Feet: \{x: %s, y: %s, z: %s\}' % (NUM, NUM, NUM), block('Checkpoints', 'Encounters')):
        cp[m.group(1)] = (float(m.group(2)), float(m.group(4)))
    rest = {}
    for m in entries(block('Points', 'Checkpoints')):
        b = m.group(2); q = re.search(r'Position: \{x: %s, y: %s, z: %s\}' % (NUM, NUM, NUM), b); k = re.search(r'Kind: (\d+)', b)
        if q and k and k.group(1) == '2': rest[m.group(1)] = (float(q.group(1)), float(q.group(3)))   # PrologueInteractionKind.Rest
    n_enc = len(re.findall(r'\n  - Id: ', block('Encounters', 'MainPath'))); n_pts = len(re.findall(r'\n  - Id: ', block('Points', 'Checkpoints')))
    assert len(enc) == n_enc, (alias, len(enc), n_enc)
    return dict(enc=enc, cp=cp, rest=rest, counts=dict(encounters=n_enc, points=n_pts, rest_points=len(rest), checkpoints=len(cp)))


def overlay(content, notes):
    """The relayout rows the running editor pass writes, laid over a content (both states give the final places)."""
    f = FIX / 'Data/content308_relayout.json'
    if not f.exists(): f = CB / 'content308_relayout.json'
    if f.exists():
        d = json.loads(f.read_text(encoding='utf-8')); off = set(d.get('groups_off', []))
        on = lambda r: r.get('enabled', True) and r.get('group', '') not in off
        for key in ('rests', 'rests_escort'):
            for r in d.get(key, []):
                if not on(r): continue
                content['rest'][r['id']] = (r['x'], r['z'])
                if 'feet' in r: content['cp'][r['id']] = (r['feet']['x'], r['feet']['z'])
        for r in d.get('encounter_moves', []):
            if on(r) and r['id'] in content['enc']: content['enc'][r['id']]['x'] = r['x']; content['enc'][r['id']]['z'] = r['z']; content['enc'][r['id']]['moved_by'] = str(f.relative_to(ROOT)).replace('\\', '/')
        if notes is not None: notes.append('relayout rows laid over the contents: ' + str(f.relative_to(ROOT)).replace('\\', '/') + ' (version ' + str(d.get('version')) + ')')
    xf = ROOT / 'Art/Playtest308/Relayout/ext/relayout308_ext.json'
    if xf.exists():
        for o in json.loads(xf.read_text(encoding='utf-8'))['ops']:
            if o['id'] == 'XI1':
                content['rest']['road_inn308'] = (o['to']['x'], o['to']['z']); content['cp']['road_inn308'] = (o['to']['feet']['x'], o['to']['feet']['z'])


# ---------- scenes ----------

class Yaml:
    def __init__(self, path):
        self.t = Path(path).read_text(encoding='utf-8', errors='replace'); self.docs = {}
        heads = [(m.group(2), int(m.group(1)), m.end()) for m in re.finditer(r'^--- !u!(\d+) &(-?\d+)(?: stripped)?\n', self.t, re.M)]
        for i, (fid, typ, start) in enumerate(heads):
            self.docs[fid] = (typ, start, heads[i + 1][2] if i + 1 < len(heads) else len(self.t))

    def body(self, fid): t, a, b = self.docs[fid]; return self.t[a:b]

    def name_of_transform(self, tid):
        g = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', self.body(tid))
        if not g or g.group(1) not in self.docs: return None
        return re.search(r'm_Name: (.*)', self.body(g.group(1))).group(1).strip()

    def crystals(self, tid, depth=0):
        """(parent name, mesh guid, scale) of every crystal part below a transform; prefab instances below it as ('prefab', guid)."""
        if tid not in self.docs: return []
        tb = self.body(tid); g = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', tb)
        if g is None or g.group(1) not in self.docs:
            pi = re.search(r'm_PrefabInstance: \{fileID: (-?\d+)\}', tb); pb = self.body(pi.group(1)) if pi and pi.group(1) in self.docs else ''
            src = re.search(r'm_SourcePrefab: \{fileID: \d+, guid: (\w+)', pb)
            return [('prefab', src.group(1) if src else None, CRYSTAL in pb or 'm_Mesh' in pb)]
        out = []; gb = self.body(g.group(1)); name = re.search(r'm_Name: (.*)', gb).group(1).strip()
        if name == CRYSTAL:
            mesh = None
            for c in re.findall(r'- component: \{fileID: (-?\d+)\}', gb):
                if self.docs[c][0] == 33: mesh = re.search(r'm_Mesh: \{fileID: -?\d+(?:, guid: (\w+))?', self.body(c)).group(1)
            fa = re.search(r'm_Father: \{fileID: (-?\d+)\}', tb).group(1); s = re.search(r'm_LocalScale: \{x: %s, y: %s, z: %s\}' % (NUM, NUM, NUM), tb)
            out.append(('part', self.name_of_transform(fa), mesh, tuple(float(s.group(i)) for i in (1, 2, 3))))
        kids = re.search(r'm_Children:\n((?:  - \{fileID: -?\d+\}\n)*)', tb)
        for k in re.findall(r'fileID: (-?\d+)', kids.group(1) if kids else ''): out += self.crystals(k, depth + 1)
        return out


def read_scene(alias, ids):
    y = Yaml(ASSETS / SCENE[alias]); out = {}
    for eid in ids:
        hits = [k for k, (t, a, b) in y.docs.items() if t == 114 and re.search(r'^  Id: %s$' % re.escape(eid), y.t[a:b], re.M) and 'DetectionRange:' in y.t[a:b]]
        rec = dict(count=len(hits))
        if len(hits) == 1:
            b = y.body(hits[0]); go = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', b).group(1); gb = y.body(go)
            rec['name'] = re.search(r'm_Name: (.*)', gb).group(1).strip()
            for f, key in (('DetectionRange', 'detection'), ('Leash', 'leash'), ('Speed', 'speed'), ('PreferredDistance', 'preferred'), ('Ranged', 'ranged')):
                m = re.search(r'^  %s: (\S+)$' % f, b, re.M); rec[key] = float(m.group(1)) if m else None
            for c in re.findall(r'- component: \{fileID: (-?\d+)\}', gb):
                t = y.docs[c][0]; cb = y.body(c)
                if t == 114:
                    m = re.search(r'^  _attackProfile: \{fileID: (-?\d+)(?:, guid: (\w+))?', cb, re.M)
                    if m: rec['attack_profile'] = m.group(2) or ''; rec['attack_mode'] = (re.search(r'^  _attackMode: (\d+)', cb, re.M) or [None, None])[1]
                    m = re.search(r'^  _profile: \{fileID: (-?\d+)(?:, guid: (\w+))?', cb, re.M)
                    if m and 'attack_profile' not in rec or (m and '_maxHp' in cb): rec['vitals'] = m.group(2) or ''
                if t == 4:
                    rec['crystals'] = y.crystals(c)
                    fa = re.search(r'm_Father: \{fileID: (-?\d+)\}', cb).group(1); rec['parent'] = y.name_of_transform(fa) if fa in y.docs else None
        out[eid] = rec
    return out


def prefab_crystal(guid):
    """The crystal inside a species prefab: (prefab name, parent bone, mesh guid, scale) or None."""
    for f in (ASSETS / SPECIES_PREFABS).glob('PF_*.prefab'):
        if guid_of(str(f.relative_to(ASSETS)).replace('\\', '/')) != guid: continue
        y = Yaml(f)
        for fid, (t, a, b) in y.docs.items():
            if t != 1 or not re.search(r'm_Name: %s\s' % CRYSTAL, y.t[a:b]): continue
            gb = y.t[a:b]; mesh = None; tid = None
            for c in re.findall(r'- component: \{fileID: (-?\d+)\}', gb):
                if y.docs[c][0] == 33: mesh = re.search(r'm_Mesh: \{fileID: -?\d+(?:, guid: (\w+))?', y.body(c)).group(1)
                if y.docs[c][0] == 4: tid = c
            tb = y.body(tid); fa = re.search(r'm_Father: \{fileID: (-?\d+)\}', tb).group(1); s = re.search(r'm_LocalScale: \{x: %s, y: %s, z: %s\}' % (NUM, NUM, NUM), tb)
            return (f.stem, y.name_of_transform(fa), mesh, tuple(float(s.group(i)) for i in (1, 2, 3)), 'EnemyOrganSet' in y.t, 'EnemyElementTelegraph' in y.t)
        return (f.stem, None, None, None, False, False)
    return None


def profile_yaml(asset):
    p = ASSETS / asset
    if not p.exists(): return None
    t = p.read_text(encoding='utf-8'); g = lambda k: re.search(r'^  %s: (.*)$' % k, t, re.M).group(1)
    cd = re.search(r'CooldownRange: \{x: %s, y: %s\}' % (NUM, NUM), t)
    return dict(elemental=g('Elemental') == '1', element=int(g('Element')), mode=int(g('Mode')), delivery=int(g('Delivery')), telegraph=float(g('Telegraph')), recovery=float(g('Recovery')),
                damage=float(g('Damage')), range=float(g('Range')), arc=float(g('ArcDegrees')), preferred=float(g('PreferredDistanceHint')), cooldown=(float(cd.group(1)), float(cd.group(2))), sha=sha(p))


ELEMENTS = ['Wood', 'Fire', 'Earth', 'Metal', 'Water']           # Oheangbu.Core.Domain.Element order
MODES = ['LegacyDistance', 'MeleeOnly', 'RangedOnly']            # EnemyController.AttackMode order (asset YAML: NeutralMelee Mode 1, FireRanged Mode 2)
DELIVERIES = ['MeleeArc', 'HomingProjectile', 'AimedProjectile', 'GroundEruption']


def gather(data, scenes=True):
    notes = []; facts = dict(notes=notes, contents={}, scenes={}, prefabs={})
    for alias in CONTENT:
        c = read_content(alias); overlay(c, notes if alias == 'arch296' else None); facts['contents'][alias] = c
    ids = sorted({r['id'] for r in data['encounters']} | set(data['tutorial']['actors']))
    if scenes:
        for alias in SCENE:
            facts['scenes'][alias] = read_scene(alias, ids)
            for rec in facts['scenes'][alias].values():
                for cr in rec.get('crystals', []):
                    if cr[0] == 'prefab' and cr[1] and cr[1] not in facts['prefabs']: facts['prefabs'][cr[1]] = prefab_crystal(cr[1])
    facts['source'] = profile_yaml(data['profiles']['source'])
    facts['assets'] = {a: profile_yaml(a) for a in [t['asset'] for t in data['reference']['tutorial']] + [data['reference']['growth_lesson']['asset']]}
    pl = ASSETS / SHARD_DIR / 'shard308_placements.json'; facts['placements'] = json.loads(pl.read_text(encoding='utf-8')) if pl.exists() else None
    ff = ROOT / data['organ']['fit']['file']; facts['fit'] = json.loads(ff.read_text(encoding='utf-8')) if ff.exists() else None
    facts['shape_guid'] = {e: (guid_of('%s/SM_Organ308_Shard%s.fbx' % (SHARD_DIR, '' if e == 'Neutral' else '_' + e)) if (ASSETS / ('%s/SM_Organ308_Shard%s.fbx.meta' % (SHARD_DIR, '' if e == 'Neutral' else '_' + e))).exists() else None)
                           for e in ['Neutral'] + ELEMENTS}
    cc = ASSETS / COMBAT_CONFIG; m_ = re.search(r'_parryWindow: %s' % NUM, cc.read_text(encoding='utf-8')) if cc.exists() else None
    facts['parry_window'] = float(m_.group(1)) if m_ else None
    ef = EXT / 'Data/earth308.json'; facts['earth'] = json.loads(ef.read_text(encoding='utf-8')) if ef.exists() else None
    return facts


# ---------- the checks (pure: data + facts -> rows) ----------

def run(d, facts):
    rows = []
    def check(ok, what): rows.append(('PASS' if ok else 'FAIL', what))
    def info(what): rows.append(('INFO', what))
    prof = d['profiles']; organ = d['organ']; ref = d['reference']; ck = d['checks']; tut = d['tutorial']
    # A. data shape: one element for the crystal and the profiles; rows name profiles; rows are existing actors, never tutorial actors
    eff = prof.get('set_element') or (prof['expect']['element'] if prof['expect'].get('elemental') else 'Neutral')
    check(organ['element'] == eff, 'A1 organ.element %s == the element the profiles carry (%s) - crystal shape = actor element (D308-4e)' % (organ['element'], eff))
    check(eff == 'Wood', 'A2 the profiles carry Wood (%s): the parry answer is 서 (금극목, COMBAT-PARRY 정본표), 어 fails completely' % eff)
    names = {r['name'] for r in prof['rows']}; used = {r['attack'] for r in d['encounters'] if r.get('enabled', True)}
    check(used <= names and len(names) == len(prof['rows']), 'A3 every row attack names one profile row (%s of %s)' % (sorted(used), sorted(names)))
    on = [r for r in d['encounters'] if r.get('enabled', True)]
    check(len(on) == 3 and len({r['id'] for r in on}) == 3, 'A4 three enabled rows, three ids (%s)' % [r['id'] for r in on])
    check(all(r.get('existing') is True for r in on), 'A5 every row is an existing actor (nothing is created or moved)')
    check(not ({r['id'] for r in on} & set(tut['actors'])), 'A6 no row names a tutorial actor %s (SPEC-PLAYTEST-306)' % tut['actors'])
    check(all('preferred_distance' not in r for r in on), 'A7 no row changes the preferred distance (PrologueEncounter is not written)')
    check(d.get('group') == 'G12_wood_beasts' and 'vitals' not in d and 'runtime' not in d, 'A8 group %s; no vitals / runtime block (HP profile and the riding switch stay)' % d.get('group'))
    # review (REVIEW.md W5): every row covers the three candidate scenes; the profile folder / source lie outside the protected trees;
    # the data switch is on (a data file that is off does nothing and must not read as a green run)
    want_scenes = set(SCENE)
    check(all(set(r.get('scenes', [])) == want_scenes for r in on), 'A9 every enabled row targets exactly %s (%s)' % (sorted(want_scenes), [(r['id'], r.get('scenes')) for r in on if set(r.get('scenes', [])) != want_scenes] or 'all three'))
    prot = [p_ for p_ in (prof['dir'], prof['source']) if any(k_ in p_.lower() for k_ in PROTECTED)]
    check(not prot, 'A10 profiles.dir / profiles.source outside the protected trees (%s)' % (prot or 'ok'))
    check(d.get('enabled') is True, 'A11 the data switch is on (enabled %s) - off = the Wood pass writes nothing' % d.get('enabled'))
    # B. profiles: the source is what the data expects, the copies are valid and inside the first-lesson limits
    s = facts['source']
    if s is None: check(False, 'B1 source profile %s missing' % prof['source'])
    else:
        check(s['sha'] == prof['source_sha256'], 'B1 source %s sha %s == the data\'s %s' % (prof['source'], s['sha'][:12], prof['source_sha256'][:12]))
        exp = prof['expect']
        check(s['elemental'] == exp['elemental'] and ELEMENTS[s['element']] == exp['element'] and MODES[s['mode']] == exp['mode'] and DELIVERIES[s['delivery']] == exp['delivery'],
              'B2 source is %s · %s · %s (data expects %s · %s · %s)' % ('elemental ' + ELEMENTS[s['element']] if s['elemental'] else 'Neutral', MODES[s['mode']], DELIVERIES[s['delivery']],
                                                                    'elemental ' + exp['element'] if exp['elemental'] else 'Neutral', exp['mode'], exp['delivery']))
        lg = ref['legacy_melee']
        check(abs(s['range'] - lg['range']) < 1e-6 and abs(s['preferred'] - lg['preferred']) < 1e-6,
              'B3 the copies keep the reach the beasts have today: Range %g / PreferredDistanceHint %g == legacy %g / %g (no other preferred distance needed)' % (s['range'], s['preferred'], lg['range'], lg['preferred']))
        if abs(s['recovery'] - lg['recovery']) > 1e-6: info('B3 Recovery %g vs the legacy fallback %g: the beast stands %.1f s longer after a swing (the source value; not replaced)' % (s['recovery'], lg['recovery'], s['recovery'] - lg['recovery']))
        lim = ref['limits']
        for r in prof['rows']:
            valid = (MODES[s['mode']] == 'MeleeOnly') == (DELIVERIES[s['delivery']] == 'MeleeArc') and r['telegraph'] > 0 and r['damage'] >= 0 and s['preferred'] <= s['range'] and s['cooldown'][1] >= s['cooldown'][0]
            check(valid, 'B4 %s passes EnemyAttackProfileSO.TryValidate rules (mode/delivery, positive, preferred <= range)' % r['name'])
            check(lim['telegraph_min'] <= r['telegraph'] <= lim['telegraph_max'], 'B5 %s telegraph %g s in [%g, %g] (fastest tutorial wood beat .. earth teaching row); legacy today %g' % (r['name'], r['telegraph'], lim['telegraph_min'], lim['telegraph_max'], lg['telegraph']))
            check(r['telegraph'] > lg['telegraph'], 'B6 %s telegraph %g s > what the beast does today (%g): the lesson is slower, not faster' % (r['name'], r['telegraph'], lg['telegraph']))
            pw = facts.get('parry_window')
            check(pw is not None and r['telegraph'] > pw, 'B10 %s telegraph %g s > the parry front window %s s (CombatConfig_Default _parryWindow): a guard drawn at the START of the telegraph is past its front window at the impact (Block x factor, not Parry / not full Fail) - AC-W8 is judged with the guard made within the window before the impact: at or after telegraph start + %s s' % (r['name'], r['telegraph'], pw, ('%.2f' % (r['telegraph'] - pw)) if pw is not None else '-'))
            check(r['damage'] <= lim['damage_max'], 'B7 %s damage %g <= %g (never harder than today)' % (r['name'], r['damage'], lim['damage_max']))
        first = next((r for r in prof['rows'] if r['name'] == on[0]['attack']), None) if on else None
        rest_rows = [r for r in prof['rows'] if first is not None and r['name'] != first['name']]
        if first is not None: check(all(first['telegraph'] >= r['telegraph'] and first['damage'] <= r['damage'] for r in rest_rows), 'B8 the first beast met (%s) is the gentlest: %g s / %g vs %s' % (on[0]['id'], first['telegraph'], first['damage'], [(r['name'], r['telegraph'], r['damage']) for r in rest_rows]))
    for t in ref['tutorial'] + [ref['growth_lesson']]:
        a = facts['assets'].get(t['asset'])
        check(a is not None and abs(a['telegraph'] - t['telegraph']) < 1e-6 and abs(a['damage'] - t['damage']) < 1e-6, 'B9 reference %s: Telegraph %s / Damage %s == the data\'s %g / %g' % (t['asset'].split('/')[-1], a and a['telegraph'], a and a['damage'], t['telegraph'], t['damage']))
    # C. the three contents: the rows exist and are what the design read (behaviour unchanged); the 54 m rule against the FINAL rest places
    for alias, c in facts['contents'].items():
        for r in on:
            if alias not in r['scenes']: info('C %s %s: not a target scene of the row' % (alias, r['id'])); continue
            e = c['enc'].get(r['id'])
            if e is None: check(False, 'C1 %s %s: encounter missing in the content' % (alias, r['id'])); continue
            same = e['content_id'] == r['content_id'] and e['ranged'] == r['ranged'] and e['respawn'] == r['respawn'] and all(abs(e[k] - r[k]) < 1e-6 for k in ('detection', 'leash', 'speed', 'activation'))
            check(same, 'C1 %s %s: Detection %g · Leash %g · Speed %g · Respawn %d · Ranged %d · Activation %g · ContentId %s == the row' % (alias, r['id'], e['detection'], e['leash'], e['speed'], e['respawn'], e['ranged'], e['activation'], e['content_id']))
            off = math.dist((e['x'], e['z']), (r['x'], r['z']))
            check(off <= d['rules']['existing_place_tol_m'], 'C2 %s %s: feet (%.1f, %.1f) %.2f m from the row (<= %g)%s' % (alias, r['id'], e['x'], e['z'], off, d['rules']['existing_place_tol_m'], ' - MOVED by ' + e['moved_by'] if 'moved_by' in e else ''))
            need = max(ck['rule54_m'], r['detection'] + r['leash'] + 10)
            places = sorted([(math.dist((e['x'], e['z']), v), 'feet ' + k) for k, v in c['cp'].items()] + [(math.dist((e['x'], e['z']), v), 'rest ' + k) for k, v in c['rest'].items()])
            if places: check(places[0][0] >= need, 'C3 %s %s <-> nearest rest place (%s) %.1f m >= %g (margin %+.1f)' % (alias, r['id'], places[0][1], places[0][0], need, places[0][0] - need))
            boss = c['enc'].get(tut['boss'])
            if boss is not None:
                bd = math.dist((e['x'], e['z']), (boss['x'], boss['z']))
                if bd < 400: check(bd >= tut['boss_field_radius_m'] + r['detection'], 'C4 %s %s <-> tutorial boss %.1f m >= field %g + detection %g (the tutorial beats are not disturbed)' % (alias, r['id'], bd, tut['boss_field_radius_m'], r['detection']))
    # D. the three scenes: the actor, what it carries today, its body and crystal
    neutral = facts['shape_guid'].get('Neutral'); wood = facts['shape_guid'].get(organ['element'])
    check(wood is not None, 'D0 crystal shape SM_Organ308_Shard_%s.fbx exists (guid %s)' % (organ['element'], wood and wood[:8]))
    row = next((p for p in (facts['placements'] or {}).get('placements', []) if p['owner'] == organ['body']), None)
    check(row is not None, 'D0 placement row for body %s (bone %s, radius %s)' % (organ['body'], row and row['bone'], row and row['radius']))
    for alias, sc in facts['scenes'].items():
        for r in on:
            if alias not in r['scenes']: continue
            a = sc.get(r['id'], {})
            check(a.get('count') == 1, 'D1 %s %s: one actor in the scene (%s)%s' % (alias, r['id'], a.get('count'), ' under ' + str(a.get('parent')) if a.get('count') == 1 else ''))
            if a.get('count') != 1: continue
            check(a.get('parent') not in ('Watershed295', 'Reworld292', 'MountainTrail285'), 'D2 %s %s: not directly under a protected tree (parent %s)' % (alias, r['id'], a.get('parent')))
            ap = a.get('attack_profile')
            if ap == '': info('D3 %s %s: no authored attack profile today (legacy melee, _attackMode %s) - Wood scene-apply writes the first one' % (alias, r['id'], a.get('attack_mode')))
            else: info('D3 %s %s: carries an attack profile already (guid %s) - scene-apply replaces it, the ledger keeps its path' % (alias, r['id'], ap))
            check(a.get('ranged') == 0 and abs((a.get('detection') or 0) - r['detection']) < 1e-6 and abs((a.get('leash') or 0) - r['leash']) < 1e-6,
                  'D4 %s %s: the scene actor is melee, Detection %s · Leash %s · Speed %s · PreferredDistance %s (not written by this pass)' % (alias, r['id'], a.get('detection'), a.get('leash'), a.get('speed'), a.get('preferred')))
            cr = a.get('crystals', []); parts = [c for c in cr if c[0] == 'part']; pref = [c for c in cr if c[0] == 'prefab']
            if len(parts) == 1 and not pref:
                _, bone, mesh, scale = parts[0]
                check(row is not None and bone == row['bone'], 'D5 %s %s: crystal under %s == the %s row bone %s (unpacked body)' % (alias, r['id'], bone, organ['body'], row and row['bone']))
                shape = next((e for e, g in facts['shape_guid'].items() if g == mesh), None)
                check(shape in ('Neutral', organ['element']), 'D6 %s %s: crystal shape today = %s (Neutral before the pass, %s after)' % (alias, r['id'], shape, organ['element']))
                check(max(scale) - min(scale) <= organ['k_tol'] * 1.0 and abs(sum(scale) / 3 - organ['fit']['k']) <= organ['k_tol'], 'D7 %s %s: crystal local scale %.4f == species k %.6f (uniform)' % (alias, r['id'], sum(scale) / 3, organ['fit']['k']))
            elif len(pref) == 1 and not parts:
                pc = facts['prefabs'].get(pref[0][1])
                check(pc is not None and pc[0] == 'PF_' + organ['body'], 'D5 %s %s: body = prefab instance %s (the row body is %s)' % (alias, r['id'], pc and pc[0], organ['body']))
                if pc is not None:
                    check(row is not None and pc[1] == row['bone'], 'D5 %s %s: the prefab\'s crystal sits under %s == row bone %s' % (alias, r['id'], pc[1], row and row['bone']))
                    shape = next((e for e, g in facts['shape_guid'].items() if g == pc[2]), None)
                    check(shape == 'Neutral' and pc[4] and pc[5], 'D6 %s %s: prefab crystal shape %s, EnemyOrganSet %s, EnemyElementTelegraph %s (the scene writes an instance override; OrganSurface308 check then needs the staged RowForActor rule)' % (alias, r['id'], shape, pc[4], pc[5]))
                    if pc[3]: check(abs(sum(pc[3]) / 3 - organ['fit']['k']) <= organ['k_tol'], 'D7 %s %s: prefab crystal scale %.4f == species k %.6f' % (alias, r['id'], sum(pc[3]) / 3, organ['fit']['k']))
            else: check(False, 'D5 %s %s: %d crystal part(s) and %d nested prefab(s) under the actor - which crystal is not decidable offline' % (alias, r['id'], len(parts), len(pref)))
        for tid in tut['actors']:
            a = sc.get(tid, {})
            info('D8 %s tutorial actor %s: %s in the scene, not named by any row' % (alias, tid, a.get('count')))
    # E. the crystal fit of the wood shape on this body, and the organ sphere
    fit = facts['fit']
    if fit is None: check(False, 'E1 fit record %s missing' % organ['fit']['file'])
    else:
        f = fit['fit'].get(organ['element'], {}); k = fit['poseFromV3']['k']
        check(fit['owner'] == organ['body'] and f.get('ok') is True and f.get('rimGapM', 1) <= 0.005 and f.get('tipInside') is False and f.get('visibleHiddenSamples', 1) == 0,
              'E1 fit %s on %s: ok %s, rim gap %s m, tip inside %s, hidden samples %s of %s, visible %s m' % (organ['element'], fit['owner'], f.get('ok'), f.get('rimGapM'), f.get('tipInside'), f.get('visibleHiddenSamples'), f.get('visibleSamples'), f.get('visibleLengthM')))
        check(abs(k - organ['fit']['k']) < 1e-6 and abs(f.get('visibleLengthM', 0) - organ['fit']['visibleLengthM']) < 1e-6, 'E2 data fit k %.6f / visible %.4f == the record %.6f / %.4f' % (organ['fit']['k'], organ['fit']['visibleLengthM'], k, f.get('visibleLengthM', 0)))
        shapes = {s_['element']: s_ for s_ in (facts['placements'] or {}).get('shapes', [])}
        if row is not None and 'Neutral' in shapes and organ['element'] in shapes:
            c0 = shapes['Neutral']['meshBoundsCenter']; w = shapes[organ['element']]; reach = 0.0
            for sx in (-1, 1):
                for sy in (-1, 1):
                    for sz in (-1, 1):
                        p = [w['meshBoundsCenter'][a_] + s_ * w['meshBoundsSize'][a_] / 2 - c0[a_] for a_, s_ in (('x', sx), ('y', sy), ('z', sz))]
                        reach = max(reach, math.sqrt(sum(v * v for v in p)) * k)
            need = reach * 1.05
            info('E3 organ sphere: the %s row radius %.4f m; the %s shape reaches at most %.4f m from the neutral bounds centre (AABB corner x k, upper bound [I]) -> x 1.05 = %.4f m' % (organ['body'], row['radius'], organ['element'], reach, need))
            check(need <= row['radius'] or organ.get('grow_radius') is True, 'E3 the sphere may have to grow (%.4f > %.4f): organ.grow_radius is %s' % (need, row['radius'], organ.get('grow_radius')))
    # F. the earth group clones mine_beast/1: order note
    if facts.get('earth'):
        tm = {r['template'] for r in facts['earth']['encounters'] if r.get('enabled', True)}
        if tm & {r['id'] for r in on}:
            info('F1 the earth rows clone %s: an earth clone made AFTER this pass starts as a wood beast and the earth pass rewrites its profile and crystal in the same save (earth-ref / earth-mesh); run the wood pass after the earth scene-apply so the earth ledger\'s before-values stay the neutral ones' % sorted(tm & {r['id'] for r in on}))
    return rows


MUTATIONS = [
    ('M1 organ.element -> Earth (crystal shape != profile element)', lambda d: d['organ'].__setitem__('element', 'Earth')),
    ('M2 profiles.set_element removed (neutral copies under a wood crystal)', lambda d: d['profiles'].pop('set_element')),
    ('M3 a row attack names no profile row', lambda d: d['encounters'][0].__setitem__('attack', 'WoodBeast308_Missing')),
    ('M4 first profile telegraph 0.6 s (faster than today)', lambda d: d['profiles']['rows'][0].__setitem__('telegraph', 0.6)),
    ('M5 den profile damage 24 (harder than today)', lambda d: d['profiles']['rows'][1].__setitem__('damage', 24)),
    ('M6 a row names the tutorial actor mine_beast/0', lambda d: d['encounters'][0].__setitem__('id', 'mine_beast/0')),
    ('M7 a row expects Detection 20 (behaviour changed)', lambda d: d['encounters'][1].__setitem__('detection', 20)),
    ('M8 a row place 30 m off', lambda d: d['encounters'][2].__setitem__('x', d['encounters'][2]['x'] + 30)),
    ('M9 organ.body -> dokkaebi (another species row: bone Spine01)', lambda d: d['organ'].__setitem__('body', 'dokkaebi')),
    ('M10 source sha noted wrong (the source changed since the design read it)', lambda d: d['profiles'].__setitem__('source_sha256', '0' * 64)),
    ('M11 organ.grow_radius false (the wood shape would be cut by the sphere)', lambda d: d['organ'].__setitem__('grow_radius', False)),
    ('M12 a row is not existing (the pass would clone and place it)', lambda d: d['encounters'][0].__setitem__('existing', False)),
    ('M13 the playable scene dropped from a row (main)', lambda d: d['encounters'][0].__setitem__('scenes', ['arch296', 'folk298'])),
    ('M14 profiles.dir inside a protected tree', lambda d: d['profiles'].__setitem__('dir', 'Assets/_Project/Art/Reworld292/Data')),
    ('M15 the data switch off', lambda d: d.__setitem__('enabled', False)),
    ('M16 telegraph and its limit lowered together to 0.85 s (inside the parry front window)', lambda d: (d['profiles']['rows'][1].__setitem__('telegraph', 0.85), d['reference']['limits'].__setitem__('telegraph_min', 0.8))),
]


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--data'); ap.add_argument('--out', default=str(STAGE / 'Dry')); ap.add_argument('--no-scenes', action='store_true')
    a = ap.parse_args()
    data_file = Path(a.data) if a.data else (STAGE / 'Data/wood308.json' if (STAGE / 'Data/wood308.json').exists() else CB / 'wood308.json')
    d = json.loads(data_file.read_text(encoding='utf-8'))
    facts = gather(d, not a.no_scenes); notes = ['wood data ' + str(data_file).replace('\\', '/') + ' sha ' + sha(data_file)[:12]] + facts['notes']
    if a.no_scenes: notes.append('--no-scenes: the D rows (scene YAML) were not read')
    rows = run(d, facts); base = {w for s, w in rows if s == 'FAIL'}
    for alias, c in facts['contents'].items(): notes.append('%s content: %s' % (alias, c['counts']))
    fails = sum(1 for s, _ in rows if s == 'FAIL'); passes = sum(1 for s, _ in rows if s == 'PASS')
    mut = []
    for name, f in MUTATIONS:
        m = copy.deepcopy(d)
        try: f(m); red = [w for s, w in run(m, facts) if s == 'FAIL' and w not in base]
        except Exception as e: red = ['exception %s: %s' % (type(e).__name__, e)]
        mut.append(dict(name=name, red=len(red), first=red[0] if red else None))
    green = [m for m in mut if m['red'] == 0]
    text = 'wood308 dry run (offline) - PASS %d / FAIL %d; mutations red %d / %d\n' % (passes, fails, len(mut) - len(green), len(mut)) + ''.join('  note %s\n' % n for n in notes) + ''.join('%s %s\n' % r for r in rows)
    text += 'mutations (each must turn at least one line red)\n' + ''.join('%s %s -> %d FAIL%s\n' % ('PASS' if m['red'] else 'FAIL', m['name'], m['red'], (' | first: ' + m['first'][:150]) if m['first'] else '') for m in mut)
    out = dict(data=str(data_file).replace('\\', '/'), notes=notes, rows=[dict(status=s, what=w) for s, w in rows], mutations=mut, **{'pass': passes, 'fail': fails},
               scenes={al: {i: {k: v for k, v in rec.items()} for i, rec in sc.items()} for al, sc in facts['scenes'].items()})
    od = Path(a.out); od.mkdir(parents=True, exist_ok=True)
    (od / 'wood308_dry.json').write_text(json.dumps(out, ensure_ascii=False, indent=1, default=str), encoding='utf-8'); (od / 'wood308_dry.txt').write_text(text, encoding='utf-8')
    print(text); print('written', str(od / 'wood308_dry.json').replace('\\', '/'))
    return 1 if fails or green else 0


if __name__ == '__main__':
    sys.exit(main())
