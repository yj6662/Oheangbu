# -*- coding: utf-8 -*-
"""Texts308 offline dry run (D308-19: 새 글 T0-T4, group G14_texts).
Pure Python: Docs/DECISIONS.md + texts308.json + the campaign and the three content assets + the three scene YAMLs + the
height field 1b + routes.json. Nothing in the Unity project is touched and the editor queue is never used.

  python Tools/Art/texts308_dry.py dry [--data <texts308.json>] [--out <dir>]
      D lines: every line of the data equals the D308-19 text byte for byte (re-read by position), text rules per kind,
               no not-chosen draft line anywhere (data, campaign, contents), every line is used by a row
      C lines: campaign - the stage, the two fields, the testimony conditions (stage / fact ids exist), the label is unique
      S lines: per scene - keeper rows, new points, scene sources, places (slope on height_1b, clearances), props, job profiles
      P lines: which surface each text takes (Present -> detail band / dialogue pages)
      O lines: order and ownership WITHOUT the builder (review R3): every row / page list holds lines of ONE decision bullet
               in the decision's order under the owner the data's owners{} table allows; the written stage is the lesson's
               stage; a testimony gate is the stage of the boss beside its trigger; a testimony fact is granted by the stage
               of the point's boss; re-seated / removed children exist under the source body
      exit 2 when a line is FAIL.  -> <out>/texts308_dry.txt (+ .json)   (default out: Tools/Unity/Stage308_texts19/Dry)
  python Tools/Art/texts308_dry.py mutate
      runs the same checks on mutated copies of the data; every mutation must turn at least one line FAIL (exit 2 otherwise).

Marks: [O] read from a file / computed offline, [I] inferred (the editor or Play decides).
Limits [O]: terrain = the 4 m height grid (bilinear); a rock shell or mesh over the terrain is not seen (the editor tool pins
the place on the physical ground, refuses a slope over the limit and warns of a walkable collider above it)."""
import argparse, copy, hashlib, importlib.util, json, math, os, re, sys
os.environ.setdefault('OMP_NUM_THREADS', '4')
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import contentseat308_dry as S

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
STAGE = ROOT / 'Tools/Unity/Stage308_texts19'
LIVE = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308/texts308.json'
DRAFTS = ROOT / 'Art/Playtest308/Relayout/texts/TEXT_DRAFTS_308.md'
ORDER = ['arch296', 'folk298', 'main']
PROMPT_LINE_MAX = 42      # WorldMacroPlaytestSession.PromptLineMaxChars [O]
PROMPT_NAME_MAX = 8       # Dialogue306 MaxPromptNameChars306 [O]
PARTICLES = ['에게 ', '과 ', '와 ', '의 ']


def load_builder():
    spec = importlib.util.spec_from_file_location('texts308_build', STAGE / 'texts308_build.py'); m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m); return m


def unity_yaml(path):
    import yaml
    s = Path(path).read_text(encoding='utf-8')
    s = re.sub(r'^%.*\n', '', s, flags=re.M); s = re.sub(r'^--- .*\n', '', s, flags=re.M)
    return yaml.safe_load(s)['MonoBehaviour']


def d2(a, b): return math.hypot(a[0] - b[0], a[1] - b[1])


class Rep:
    def __init__(self): self.lines = []; self.fail = 0; self.ok = 0

    def say(self, s): self.lines.append(s)

    def chk(self, tag, ok, text, mark='O'):
        self.lines.append('  %s %s %s [%s]' % ('PASS' if ok else 'FAIL', tag, text, mark))
        if ok: self.ok += 1
        else: self.fail += 1


def draft_lines():
    """every [DRAFT] text of the reading document (table rows and the bold prompt / speaker lines)."""
    out = set()
    for ln in DRAFTS.read_text(encoding='utf-8').split('\n'):
        for m in re.finditer(r'\[DRAFT\]\s*(?:\*\*)?([^|*]+?)(?:\*\*)?\s*(?:\(|\||$)', ln):
            t = m.group(1).strip()
            if t: out.add(t)
    return out


class World:
    """everything read once from the project (shared by the dry run and every mutation)."""
    def __init__(self, data):
        self.H = S.Height(S.HEIGHT_1B)
        self.routes = {r['id']: [(p['x'], p['z']) for p in r['points']] for r in json.loads(S.ROUTES.read_text(encoding='utf-8-sig'))['routes']}
        self.camp_path = PROJECT / data['campaign']; self.camp = unity_yaml(self.camp_path)
        self.content = {}; self.scene = {}; self.blob = {}
        keys = [b['object_key'] for b in data['boards']] + [n['source_key'] for n in data['people']] + [data['root']]
        kids = sorted(set(n['source_key'] + '/' + c for n in data['people'] for c in ['Jige', 'PropStick', 'WalkingStick'] + list(n.get('reseat_children', [])) + list(n.get('remove_children', []))))
        keys += kids
        for t in data['targets']:
            a = t['alias']; self.content[a] = unity_yaml(PROJECT / t['content'])
            self.blob[a] = json.dumps(self.content[a], ensure_ascii=False)
            sk = S.SceneK(PROJECT / t['scene'])
            self.scene[a] = {'sha': sk.sha, 'keys': {k: (sk.world(k)[0] if sk.get(k) is not None else None) for k in keys}}
        self.camp_blob = json.dumps(self.camp, ensure_ascii=False)
        self.drafts = draft_lines()
        self.builder = load_builder(); self.decision = self.builder.decision_strings()

    def slope(self, p):
        return max(math.degrees(math.acos(max(-1.0, min(1.0, self.H.normal(p[0], p[1], d)[1])))) for d in (1.0, 2.0, 4.0))


def path_dist(poly, p, gap=40.0):
    best = float('inf')
    for a, b in zip(poly, poly[1:]):
        if d2(a, b) > gap: continue
        best = min(best, S.seg_dist(p, a, b))
    return best


def run(data, W, rep, built_text=None):
    L = data['lines']; TR = data['text_rules']; rx = re.compile(TR['banned_regex'])
    T = lambda i: L[i]['text'] if i in L else None
    # ------------------------------------------------------------------ D: data
    rep.say('-- D: data (lines, rules)')
    if built_text is not None:
        fresh = W.builder.build(); rep.chk('D1', built_text == fresh, 'texts308.json equals a fresh build from Docs/DECISIONS.md (sha %s)' % hashlib.sha256(built_text.encode('utf-8')).hexdigest()[:12])
    chosen = set(x for v in W.decision.values() for x in v)
    used = set()
    for k, e in L.items():
        text = e.get('text', ''); kind = e.get('kind', 'line')
        if 'src' in e:
            tag, n = e['src']; want = W.decision.get(tag, [None] * 99)[n] if n < len(W.decision.get(tag, [])) else None
            rep.chk('D2', want is not None and want.encode('utf-8') == text.encode('utf-8'), 'line %s = D308-19 %s[%d] byte for byte: "%s"' % (k, tag, n, text))
        else:
            sp = e.get('derived', ''); base = T(sp.split(' ')[0]) or ''
            rep.chk('D2', text.startswith(base) and base in chosen and text[len(base):] in ('과 이야기', '와 이야기'), 'line %s (derived prompt) = speaker "%s" + the existing prompt ending: "%s"' % (k, base, text), 'I')
        mx = TR['max_chars'] if kind == 'line' else TR['label_max_chars'] if kind == 'row' else TR[kind + '_max_chars']
        bad = [w for w in TR['banned'] if w in text] + [w for w in TR.get('banned_endings', []) if w in text] + (['regex'] if rx.search(text) else [])
        rep.chk('D3', e.get('status') == 'TEST' and 0 < len(text) <= mx and not bad and text == text.strip() and '\n' not in text,
                '%s %s: %d chars (<= %d), status %s, banned %s' % (kind, k, len(text), mx, e.get('status'), bad or 0))
    notchosen = set(t for t in W.drafts if t not in chosen and len(t) >= 6)
    hit = sorted(k for k, e in L.items() if e.get('text') in notchosen)
    rep.chk('D4', not hit, 'no not-chosen draft line in the data (%d draft lines of the reading document are not in D308-19)%s' % (len(notchosen), (': ' + ', '.join(hit)) if hit else ''))
    for name, blob in [('campaign', W.camp_blob)] + [('content ' + a, W.blob[a]) for a in ORDER]:
        hit = sorted(t for t in notchosen if t in blob)
        rep.chk('D4', not hit, 'no not-chosen draft line in the %s asset on disk%s' % (name, (': ' + ' / '.join(hit)) if hit else ''))
    # usage
    cr = data['campaign_rows']
    for v in cr['variants'].values(): used.update(v.values())
    for t in data['testimonies']: used.update(t['pages'])
    for k in data['keepers']:
        for r in k['rows']: used.add(r['label']); used.update(r['lines'])
    for p in data['points']:
        used.add(p['prompt']); used.update(p.get('pages', [])); used.update(p.get('first', []))
        if p.get('speaker'): used.add(p['speaker'])
        for r in p.get('rows', []): used.add(r['label']); used.update(r['lines'])
    unknown = sorted(x for x in used if x not in L); unused = sorted(k for k in L if k not in used and k != 'T1_prompt')
    rep.chk('D5', not unknown and not unused, 'every line id used by a row exists (%d used) and every line is used (T1_prompt = the existing point\'s own prompt, compared below)%s' %
            (len(used), (' unknown %s unused %s' % (unknown, unused)) if unknown or unused else ''))
    for p in data['points'] + data['keepers']:
        labels = [T(r['label']) for r in p.get('rows', [])]
        rep.chk('D5', len(labels) == len(set(labels)), 'row names of %s are distinct: %s' % (p.get('id') or p.get('point'), labels))
    # ------------------------------------------------------------------ O: order and ownership (no builder involved)
    rep.say('-- O: order and ownership (decision bullet -> owner; the decision\'s order inside a row)')
    owners = data.get('owners') or {}
    rep.chk('O0', bool(owners) and all(isinstance(v, list) and v for v in owners.values()), 'owners{} names an owner list for %d decision bullets' % len(owners))
    def group(owner, what, ids):
        srcs = [tuple(L[i]['src']) if i in L and 'src' in L[i] else None for i in ids]
        tags = set(x[0] for x in srcs if x)
        rep.chk('O1', None not in srcs and len(tags) == 1 and owner in owners.get(next(iter(tags)), []), '%s of %s: lines %s come from one decision bullet %s that this owner may carry (%s)' % (what, owner, list(ids), sorted(tags), owners.get(next(iter(tags)), []) if tags else '-'))
        rep.chk('O2', None not in srcs and all(a[1] < b[1] for a, b in zip(srcs, srcs[1:])), '%s of %s: in the decision\'s order %s' % (what, owner, [x and x[1] for x in srcs]))
    cr0 = data['campaign_rows']
    for vn, v in cr0['variants'].items():
        if all(k in v for k in ('Objective', 'DestinationLabel')): group(cr0['stage'], 'variant %s (Objective, DestinationLabel)' % vn, [v['Objective'], v['DestinationLabel']])
    for t in data['testimonies']: group(t['trigger'], 'testimony %s pages' % t['id'], t['pages'])
    for k in data['keepers']:
        for r in k['rows']: group(k['point'], 'keeper row', [r['label']] + list(r['lines']))
    for p in data['points']:
        if p['kind'] == 'Evidence': group(p['id'], 'prompt + pages', [p['prompt']] + list(p['pages']))
        else: group(p['id'], 'speaker + first words', [p['speaker']] + list(p['first']))
        for r in p.get('rows', []): group(p['id'], 'row', [r['label']] + list(r['lines']))
        labs = [r['label'] for r in p.get('rows', [])]
        if len(labs) > 1: group(p['id'], 'row names in menu order', labs)
        if p.get('first') and p.get('rows'): group(p['id'], 'first words before the first row', list(p['first']) + [p['rows'][0]['label']])
    # ------------------------------------------------------------------ C: campaign
    rep.say('-- C: campaign %s' % data['campaign'].split('/')[-1][-40:])
    stages = {s['Id']: s for s in W.camp['Stages']}; st = stages.get(cr['stage'])
    rep.chk('C1', st is not None, 'stage %s exists (Objective now "%s", DestinationLabel now "%s")' % (cr['stage'], st and st['Objective'], st and st['DestinationLabel']))
    rep.chk('C1', sorted(cr['allowed_fields']) == ['DestinationLabel', 'Objective'], 'allowed_fields = exactly Objective + DestinationLabel (%s)' % cr['allowed_fields'])
    var = cr['variants'].get(cr['variant'])
    rep.chk('C1', var is not None and sorted(var.keys()) == ['DestinationLabel', 'Objective'], 'variant %s names exactly the two fields' % cr['variant'])
    if st and var and all(T(x) for x in var.values()):
        obj, lab = T(var['Objective']), T(var['DestinationLabel'])
        rep.say('  plan [XT0] %s.Objective: "%s" -> "%s"%s' % (cr['stage'], st['Objective'], obj, '' if st['Objective'] != obj else ' (already)'))
        rep.say('  plan [XT0] %s.DestinationLabel: "%s" -> "%s" (map: "%s 일대")%s' % (cr['stage'], st['DestinationLabel'], lab, lab, '' if st['DestinationLabel'] != lab else ' (already)'))
        twice = [s['Id'] for s in W.camp['Stages'] if s['Id'] != cr['stage'] and s.get('DestinationLabel') == lab]
        rep.chk('C1', not twice, 'DestinationLabel "%s" is used by no other stage (%s)' % (lab, twice or 'none'))
        other = [v for k, v in cr['variants'].items() if k != cr['variant']]
        rep.chk('C1', all(T(o['Objective']) != obj for o in other), 'the other variant\'s text is not the one planned')
        dest = st['Destination']; pt = next((p for p in W.content['main']['Points'] if p['Id'] == st['TriggerId']), None)
        rep.chk('C1', pt is not None and d2((dest['x'], dest['z']), (pt['Position']['x'], pt['Position']['z'])) <= 5, 'stage Destination (%.0f, %.0f) sits on its trigger point %s (the label names that place)' % (dest['x'], dest['z'], st['TriggerId']))
    rep.chk('O3', st is not None and st.get('TriggerId') == cr.get('stage_trigger') and bool(cr.get('stage_trigger')), 'the written stage %s is the stage of the lesson: TriggerId %s = stage_trigger %s' % (cr['stage'], st and st.get('TriggerId'), cr.get('stage_trigger')))
    granted = set(f for s in W.camp['Stages'] for f in (s.get('GrantedFacts') or []))
    all_points = {a: {p['Id']: p for p in W.content[a]['Points']} for a in ORDER}
    new_ids = set(p['id'] for p in data['points'])
    for t in data['testimonies']:
        until = t.get('until_stage_open') or ''; facts = t.get('required_facts') or []
        if until:
            g = stages.get(until)
            rep.chk('C2', g is not None and bool(g.get('PrerequisiteIds')) and all(x in stages for x in g['PrerequisiteIds']) and g.get('Implemented') == 1,
                    'testimony %s: until_stage_open %s is a stage with prerequisites %s (all stage ids)' % (t['id'], until, g and g.get('PrerequisiteIds')))
        for f in facts: rep.chk('C2', f in granted, 'testimony %s: fact %s is granted by a stage (%s)' % (t['id'], f, [s['Id'] for s in W.camp['Stages'] if f in (s.get('GrantedFacts') or [])]))
        rep.chk('C2', bool(until) or bool(facts), 'testimony %s has a condition' % t['id'])
        spec = next((p for p in data['points'] if p['id'] == t['trigger']), None); old = W.content['main'] and next((p for p in W.content['main']['Points'] if p['Id'] == t['trigger']), None)
        at = (old['Position']['x'], old['Position']['z']) if old else (spec['places'][0]['x'], spec['places'][0]['z']) if spec else None
        if until:
            g = stages.get(until); near = data['rules'].get('gate_near_m', 0); d = d2(at, (g['Destination']['x'], g['Destination']['z'])) if g and at else -1
            rep.chk('O4', 0 <= d <= near, 'testimony %s: the gate %s stands beside its trigger point (Destination %.1f m <= gate_near_m %s) = the boss whose absence the text describes' % (t['id'], until, d, near))
        boss = (spec or {}).get('clear', {}).get('boss')
        if facts and spec is not None:
            gs = stages.get(boss) if boss else None
            rep.chk('O5', gs is not None and all(f in (gs.get('GrantedFacts') or []) for f in facts), 'testimony %s: its facts %s are granted by the stage of the point\'s boss %s (%s)' % (t['id'], facts, boss, gs and gs.get('GrantedFacts')))
        where = [a for a in ORDER if t['trigger'] in all_points[a]]
        rep.chk('C2', len(where) == 3 or t['trigger'] in new_ids, 'testimony %s: trigger %s is a point of %s' % (t['id'], t['trigger'], where or 'this data (new point)'))
        have = [x for x in (W.camp.get('Testimonies') or []) if x['TriggerId'] == t['trigger']]
        rep.say('  plan [%s] testimony %s: %s, %d page(s); rows of this trigger now: %d' % (t['op'], t['id'], ('while %s is closed' % until) if until else 'facts %s' % facts, len(t['pages']), len(have)))
    tp = all_points['main'].get('sanctuary_trace308')
    rep.chk('C2', tp is not None and tp['Prompt'] == T('T1_prompt') and tp['Kind'] == 0, 'sanctuary_trace308 is an Evidence point with the prompt "%s"; its own Text stays: "%s"' % (T('T1_prompt'), tp and tp['Text']))
    rep.say('  note [O] Content308 CampaignSig308 (order, prerequisites, Optional, rewards, facts, triggers, act) holds neither Objective nor DestinationLabel nor Testimonies: the gate signature is unchanged by XT0 / XT1 / XT3.')
    # ------------------------------------------------------------------ S: per scene
    join = data['evidence_page_join']
    for a in ORDER:
        rep.say('-- S: %s (scene sha %s)' % (a, W.scene[a]['sha'][:12])); C = W.content[a]; pts = all_points[a]; keys = W.scene[a]['keys']
        enc = {e['Id']: e for e in C['Encounters']}
        for k in data['keepers']:
            p = pts.get(k['point'])
            rep.chk('S1', p is not None and p['Kind'] == 3, 'keeper %s is a Conversation point of this content' % k['point'])
            if p is None: continue
            now = p.get('Services') or []; names = [s.get('Label') for s in now if s['Kind'] == 0]
            rep.chk('S1', any(s['Kind'] == 3 for s in now), 'keeper %s keeps its rows %s; Talk rows now %s -> + %s' % (k['point'], [(s['Kind'], s.get('Label')) for s in now], names, [T(r['label']) for r in k['rows']]))
        rep.chk('S6', keys.get(data['root']) is None, 'the scene has no root %s yet' % data['root'], 'O')
        for spec in data['points']:
            pid = spec['id']; board = next((b for b in data['boards'] if b['id'] == pid), None); person = next((n for n in data['people'] if n['id'] == pid), None)
            src = board['object_key'] if board else person['source_key'] if person else None
            rep.chk('S3', src is not None and keys.get(src) is not None, 'point %s: its scene source %s is in this scene%s' % (pid, src, (' at (%.2f, %.2f, %.2f)' % tuple(keys[src])) if src and keys.get(src) else ''))
            rep.chk('S2', pid not in pts, 'point %s is new in this content (would be added)' % pid)
            place = spec['places'][0]; P = (place['x'], place['z']); y = W.H(*P); sl = W.slope(P); cl = spec.get('clear', {})
            rep.chk('S4', sl <= spec['max_slope_deg'], 'point %s place (%.1f, %.1f): slope %.1f deg <= %s (height_1b, 1 / 2 / 4 m windows), y %.2f (row y_offline %.2f)' % (pid, P[0], P[1], sl, spec['max_slope_deg'], y, place.get('y_offline', y)))
            rep.chk('S4', abs(y - place.get('y_offline', y)) <= data['rules']['y_tol_m'], 'point %s: y_offline agrees with the height field (|d| %.2f)' % (pid, abs(y - place.get('y_offline', y))))
            if board and keys.get(src): rep.chk('S4', d2(P, (keys[src][0], keys[src][2])) <= 0.05, 'point %s sits on the board (%.2f m from it)' % (pid, d2(P, (keys[src][0], keys[src][2]))))
            if cl.get('boss'):
                e = enc.get(cl['boss']); d = d2(P, (e['Feet']['x'], e['Feet']['z'])) if e else -1
                rep.chk('S4', e is not None and d >= cl['boss_min_m'], 'point %s: boss %s %.1f m >= %s (field radius; margin %+.1f)' % (pid, cl['boss'], d, cl['boss_min_m'], d - cl['boss_min_m']))
            for eid in cl.get('enemies', []):
                e = enc.get(eid)
                if e is None: rep.chk('S4', False, 'point %s: enemy %s is in this content' % (pid, eid)); continue
                d = d2(P, (e['Feet']['x'], e['Feet']['z']))
                if 'enemies_min_m' in cl:
                    rep.chk('S4', d >= cl['enemies_min_m'] and d > e['Detection'], 'point %s: enemy %s %.1f m >= %s and outside detection %s (leash %s; 54 m rule margin %+.1f = not a rest / mandatory stop)' % (pid, eid, d, cl['enemies_min_m'], e['Detection'], e['Leash'], d - 54))
                if 'enemy_stand_min_m' in cl:
                    stand = min([d] + [d2(P, (q['x'], q['z'])) for q in (e.get('Patrol') or [])])
                    rep.chk('S4', stand >= cl['enemy_stand_min_m'], 'point %s: enemy %s stand / patrol %.1f m >= %s (the point is %s its detection %s)' % (pid, eid, stand, cl['enemy_stand_min_m'], 'INSIDE' if d <= e['Detection'] else 'outside', e['Detection']))
            if 'rests_min_m' in cl:
                rests = [('feet ' + c['Id'], (c['Feet']['x'], c['Feet']['z'])) for c in C['Checkpoints']] + [('rest ' + p['Id'], (p['Position']['x'], p['Position']['z'])) for p in C['Points'] if p['Kind'] == 2]
                who, at = min(rests, key=lambda r: d2(P, r[1])); d = d2(P, at)
                rep.chk('S4', d >= cl['rests_min_m'], 'point %s: nearest rest (%s) %.1f m >= %s' % (pid, who, d, cl['rests_min_m']))
            if 'points_min_m' in cl:
                others = [(p['Id'], (p['Position']['x'], p['Position']['z'])) for p in C['Points'] if p['Id'] != pid] + [(q['id'], (q['places'][0]['x'], q['places'][0]['z'])) for q in data['points'] if q['id'] != pid]
                who, at = min(others, key=lambda r: d2(P, r[1])); d = d2(P, at)
                rep.chk('S4', d >= cl['points_min_m'], 'point %s: nearest other point (%s) %.1f m >= %s' % (pid, who, d, cl['points_min_m']))
            if 'road_min_m' in cl:
                main = [(p['x'], p['z']) for p in (C.get('MainPath') or [])] ; br = [(p['x'], p['z']) for p in (C.get('BranchPath') or [])]
                dc = min(path_dist(main, P), path_dist(br, P)); dr = path_dist(W.routes.get(cl.get('road', ''), []), P)
                rep.chk('S4', cl['road_min_m'] <= dc <= cl.get('road_max_m', 1e9), 'point %s: road centre (content MainPath / BranchPath) %.1f m in [%s, %s]; route %s %.1f m (wheel half width 4 m)' % (pid, dc, cl['road_min_m'], cl.get('road_max_m'), cl.get('road'), dr))
            for i, alt in enumerate(spec['places'][1:], 1):
                Q = (alt['x'], alt['z']); rep.say('  info point %s candidate %d (%.1f, %.1f): slope %.1f deg, y %.2f' % (pid, i, Q[0], Q[1], W.slope(Q), W.H(*Q)))
    # ------------------------------------------------------------------ S5 / P: assets and surfaces (once)
    rep.say('-- S5 / P: assets and text surfaces')
    for n in data['people']:
        rep.chk('S5', (PROJECT / n['job_profile']).is_file(), '%s: job profile %s exists (no new profile / clip)' % (n['id'], n['job_profile'].split('/')[-1]))
        for pr in n['props']: rep.chk('S5', (PROJECT / pr['prefab']).is_file(), '%s: prop %s = existing prefab %s x%s' % (n['id'], pr['name'], pr['prefab'].split('/')[-1], pr['scale']))
        spec = next((p for p in data['points'] if p['id'] == n['id']), None)
        rep.chk('S5', spec is not None and spec['kind'] == 'Conversation', '%s has a Conversation point row' % n['id'])
        seat = n.get('reseat_children', []); gone = n.get('remove_children', []); kk = W.scene['main']['keys']
        for c in seat + gone: rep.chk('O6', kk.get(n['source_key'] + '/' + c) is not None, '%s: child %s (%s) exists under the source body %s' % (n['id'], c, 're-seated' if c in seat else 'removed', n['source_key']))
        rep.chk('O6', not set(seat) & set(gone), '%s: no child is both re-seated and removed' % n['id'])
        for pr in n['props']:
            if pr.get('on'): rep.chk('O6', pr['on'] in seat, '%s: prop %s rides %s, a re-seated child' % (n['id'], pr['name'], pr['on']))
        loose = [c for c in ('Jige', 'PropStick', 'WalkingStick') if kk.get(n['source_key'] + '/' + c) is not None and c not in seat and c not in gone]
        rep.chk('O6', not loose, '%s: every ground child the source body carries is re-seated or removed (left as copied: %s)' % (n['id'], loose or 'none'))
    maxpage = W.content['main'].get('DialoguePageMaxChars', 72)
    for spec in data['points'] + [dict(t, id=t['id'], kind='Evidence' if t.get('join') == 'evidence' else 'Conversation', first=t['pages']) for t in data['testimonies']]:
        if spec['kind'] == 'Evidence':
            pages = [T(x) or '' for x in spec['pages']]; body = join.join(pages)
            rep.chk('P1', ('\n' in body or len(body) > PROMPT_LINE_MAX) and (len(pages) == 1 or '\n' in join), '%s: %d page(s) -> Present() routes to the detail band (line break in the body: %s, %d chars)' % (spec['id'], len(pages), '\n' in body, len(body)))
            rep.chk('P1', body.split(join) == pages if len(pages) > 1 else True, '%s: the joined text splits back into its %d page(s)' % (spec['id'], len(pages)))
        else:
            first = '\n\n'.join(T(x) or '' for x in spec['first'])
            rep.chk('P2', all(len(T(x) or '') <= maxpage for x in spec['first']), '%s: first words %d chars <= DialoguePageMaxChars %d -> one dialogue page each' % (spec['id'], len(first), maxpage))
            if spec.get('prompt'):
                pr = T(spec['prompt']) or ''; cut = min([pr.find(x) for x in PARTICLES if pr.find(x) > 0] or [-1])
                rep.chk('P3', 0 < cut <= PROMPT_NAME_MAX and pr[:cut] == T(spec['speaker']), '%s: prompt "%s" names the speaker "%s" (%d chars <= %d)' % (spec['id'], pr, pr[:cut], cut, PROMPT_NAME_MAX))
    rep.say('  note [I] "<page>" is honoured by TextMeshPro only in TextOverflowModes.Page (the detail band sets it [O MenuStory304.cs]); Play decides (DEPLOY_PLAN_texts.md P2). Fallback: evidence_page_join "\\n\\n\\n" (two blank lines = one line a page on the 3-line band).')
    return rep


MUTATIONS = [
    ('M01 one character of a line changed', lambda d: d['lines']['T2_3'].__setitem__('text', d['lines']['T2_3']['text'].replace('한한다고', '한한다'))),
    ('M02 a line of 41 characters', lambda d: d['lines']['T3_tree2'].__setitem__('text', d['lines']['T3_tree2']['text'] + ' 그러하오이다')),
    ('M03 a banned word in a line', lambda d: d['lines']['T4_1'].__setitem__('text', d['lines']['T4_1']['text'].replace('장수', '메뉴'))),
    ('M04 a row name of 9 characters', lambda d: d['lines']['T1_row'].__setitem__('text', d['lines']['T1_row']['text'] + '들')),
    ('M05 a Latin letter in a line', lambda d: d['lines']['T1_a1'].__setitem__('text', d['lines']['T1_a1']['text'] + ' F')),
    ('M06 a not-chosen draft line swapped in', lambda d: d['lines']['T4_1'].__setitem__('text', '문 앞에 장수 하나가 섰소. 군졸들도 그쪽은 안 보오.')),
    ('M07 a testimony gate that is not a stage', lambda d: d['testimonies'][0].__setitem__('until_stage_open', 'cheongryong_gate')),
    ('M08 a testimony fact no stage grants', lambda d: d['testimonies'][1].__setitem__('required_facts', ['defeated:sinmok'])),
    ('M09 the rope keeper inside the boss field', lambda d: d['points'][1]['places'].__setitem__(0, {'x': 3690.0, 'z': 3242.0, 'y_offline': 224.3})),
    ('M10 the potter beside an earth enemy', lambda d: d['points'][2]['places'].__setitem__(0, {'x': 1898.0, 'z': 2400.0, 'y_offline': 98.6})),
    ('M11 a keeper that is in no content', lambda d: d['keepers'][0].__setitem__('point', 'hunter_innkeeper')),
    ('M12 a third stage field allowed', lambda d: d['campaign_rows']['allowed_fields'].append('Dialogue')),
    ('M13 a page join without a line break', lambda d: d.__setitem__('evidence_page_join', '<page>')),
    ('M14 a line whose status is DRAFT', lambda d: d['lines']['T3_after'].__setitem__('status', 'DRAFT')),
    ('M15 a prop prefab that does not exist', lambda d: d['people'][1]['props'][0].__setitem__('prefab', 'Assets/Korea_TreasureProps/Prefabs/SM_Onggi.prefab')),
    ('M16 a variant the data does not hold', lambda d: d['campaign_rows'].__setitem__('variant', 'C')),
    ('M17 the potter on the wheel line', lambda d: d['points'][2]['places'].__setitem__(0, {'x': 1920.0, 'z': 2434.0, 'y_offline': 95.39})),
    ('M18 the board point 4 m off the board', lambda d: d['points'][0]['places'].__setitem__(0, {'x': 3366.66, 'z': 2011.98, 'y_offline': 169.4})),
    ('M19 a line id a row names but lines{} lacks', lambda d: d['keepers'][0]['rows'][1]['lines'].append('T3j_3')),
    ('M20 a polite ending the voice rule bans', lambda d: d['lines']['T4_2'].__setitem__('text', '해가 져도 그 자리입니다.')),
    # review R3: structure mutations that the build-equality line D1 alone used to catch
    ('M21 the lines of a Talk row in reverse order', lambda d: d['points'][2]['rows'][0]['lines'].reverse()),
    ('M22 two pages of the notice swapped', lambda d: d['points'][0]['pages'].__setitem__(slice(0, 2), d['points'][0]['pages'][1::-1])),
    ('M23 a line of the rope keeper in the potter\'s row', lambda d: d['points'][2]['rows'][0]['lines'].append('T3_tree1')),
    ('M24 the innkeeper rows on another NPC that has a rest row', lambda d: d['keepers'][0].__setitem__('point', 'hamlet_elder303')),
    ('M25 the after-kill line on a fact of another boss', lambda d: d['testimonies'][1].__setitem__('required_facts', ['defeated:cheongryong'])),
    ('M26 the before-text gated by another existing stage', lambda d: d['testimonies'][0].__setitem__('until_stage_open', 'escort')),
    ('M27 the two fields written to another existing stage', lambda d: d['campaign_rows'].__setitem__('stage', 'logging')),
    ('M28 the potter 12.7 m from the shrine feet', lambda d: d['points'][2]['places'].__setitem__(0, {'x': 1925.5, 'z': 2436.0, 'y_offline': 95.55})),
    ('M29 a jar riding a child that is not re-seated', lambda d: d['people'][1].__setitem__('reseat_children', ['PropStick'])),
    ('M30 a re-seated child the source body does not have', lambda d: d['people'][0]['reseat_children'].append('Jige')),
    ('M31 a row name placed after its lines', lambda d: d['points'][1]['rows'][0].__setitem__('label', 'T3_row_rope')),
    ('M32 the first words of one speaker under the other', lambda d: d['points'][1].__setitem__('first', ['T4_first'])),
]


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(); ap.add_argument('cmd', choices=['dry', 'mutate']); ap.add_argument('--data'); ap.add_argument('--out')
    a = ap.parse_args()
    dpath = Path(a.data) if a.data else (LIVE if LIVE.exists() else STAGE / 'Data/texts308.json')
    text = dpath.read_text(encoding='utf-8'); data = json.loads(text); out = Path(a.out) if a.out else STAGE / 'Dry'; out.mkdir(parents=True, exist_ok=True)
    W = World(data)
    if a.cmd == 'dry':
        rep = Rep(); rep.say('texts308 dry  data %s sha %s  height %s  DECISIONS sha %s' % (dpath, hashlib.sha256(text.encode('utf-8')).hexdigest()[:12], Path(W.H.path).name, S.sha(ROOT / 'Docs/DECISIONS.md')[:12]))
        rep.say('  campaign sha %s; contents %s' % (S.sha(W.camp_path)[:12], ', '.join('%s %s' % (t['alias'], S.sha(PROJECT / t['content'])[:12]) for t in data['targets'])))
        run(data, W, rep, text)
        rep.say('RESULT PASS %d / FAIL %d' % (rep.ok, rep.fail))
        (out / 'texts308_dry.txt').write_text('\n'.join(rep.lines) + '\n', encoding='utf-8')
        (out / 'texts308_dry.json').write_text(json.dumps({'pass': rep.ok, 'fail': rep.fail, 'data_sha': hashlib.sha256(text.encode('utf-8')).hexdigest(), 'scene_sha': {k: v['sha'] for k, v in W.scene.items()}}, indent=1), encoding='utf-8')
        print('\n'.join(rep.lines)); return 2 if rep.fail else 0
    base = Rep(); run(copy.deepcopy(data), W, base, text); lines = ['texts308 mutations (base: PASS %d / FAIL %d)' % (base.ok, base.fail)]; dead = 0
    for name, fn in MUTATIONS:
        d = copy.deepcopy(data); fn(d); r = Rep()
        try: run(d, W, r)
        except Exception as e: r.fail += 1; r.lines.append('  FAIL X the run stopped: %s %s [O]' % (type(e).__name__, e))
        red = [l.strip() for l in r.lines if l.strip().startswith('FAIL')]
        lines.append('  %s %s -> %d FAIL line(s)%s' % ('RED ' if red else 'GREEN (mutation not caught)', name, len(red), (': ' + red[0][:150]) if red else '')); dead += 0 if red else 1
    lines.append('RESULT %d mutation(s), %d not caught' % (len(MUTATIONS), dead))
    (out / 'texts308_mutations.txt').write_text('\n'.join(lines) + '\n', encoding='utf-8'); print('\n'.join(lines))
    return 2 if dead or base.fail else 0


if __name__ == '__main__':
    sys.exit(main())
