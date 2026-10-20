#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D308-117 (SPEC-FIELD-FIRST-HOUR-308 B1 / B2): builds Art/World/Compact/Rebuild/FieldFirst308/field117.json, the data the editor
tool FieldFirst308 reads. Every word is copied from Docs/DECISIONS.md D308-117 bullet F2 by position (the quoted strings in order);
nothing in this file is a line of the game.

  python Tools/Art/FieldFirst308/field117_build.py           write the json (reports the sha)
  python Tools/Art/FieldFirst308/field117_build.py --check   exit 2 when the file on disk differs from a fresh build
"""
import hashlib, json, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DECISIONS = ROOT / 'Docs' / 'DECISIONS.md'
OUT = ROOT / 'Art' / 'World' / 'Compact' / 'Rebuild' / 'FieldFirst308' / 'field117.json'
ENTRY = 'D308-117'
MAIN_CONTENT = 'Assets/_Project/Scenes/World/Main/WorldContent_Main.asset'
CAMPAIGN = ('Assets/_Project/Art/World/Architecture296/Data/85ebc15b71d242841913ba8297c7b933_af510ae16a71e3d429dc77075aeb7371_'
            '839cb9257ad263442a19c248dffabd22_Campaign.asset')


def decision_strings():
    t = DECISIONS.read_text(encoding='utf-8')
    i = t.index('### ' + ENTRY); j = t.find('\n### ', i + 5); blk = t[i:j if j > 0 else len(t)]
    out = {}
    for ln in blk.split('\n'):
        m = re.match(r'\s+- \*\*(F\d+)', ln)
        if m: out[m.group(1)] = [a or b for a, b in re.findall(r'"([^"]+)"|`([^`]+)`', ln)]
    return out


TEXTS = ROOT / 'Art' / 'World' / 'Compact' / 'Rebuild' / 'CliffBoundary308' / 'texts308.json'

# C2 (SPEC-FIELD-FIRST-HOUR-308 §C2, D308-117 F3 - F6): bullet -> commission. The quoted strings of a bullet are, in order,
# speaker, first, offer 1, offer 2, waiting, report, completed [, row, row line 1 (before the request), row line 2 (pick-up)].
# giver = the Texts308 XF3 - XF6 point id; evidence = a point id, or target = an encounter id. Reward = TEST inside the Spec's
# 50 - 120 band (fetch inside a gated end 60, the dungeon fetch 80, two bandits 90, the beast pack by the sanctuary road 100).
C2 = [
    {'bullet': 'F3', 'id': 'commission308_mansok', 'giver': 'shingle_mansok308', 'evidence': 'sinmok_bark308', 'reward': 60},
    {'bullet': 'F4', 'id': 'commission308_dolsoe', 'giver': 'woodcutter_dolsoe308', 'target': 'field308/cheongrim_charcoal_bandit_2_0', 'reward': 90},
    {'bullet': 'F5', 'id': 'commission308_bunnyeo', 'giver': 'herb_bunnyeo308', 'target': 'forest_beast308/1', 'reward': 100},
    {'bullet': 'F6', 'id': 'commission308_eopdong', 'giver': 'charcoal_eopdong308', 'evidence': 'dg308_cr_kiln_sack', 'reward': 80},
]


def commissions(D):
    rows, evidence = [], []
    for c in C2:
        s = D[c['bullet']]
        if len(s) not in (7, 10): raise SystemExit('%s: %d quoted strings (7 or 10 expected)' % (c['bullet'], len(s)))
        offer, waiting, report, done = s[1:4], [s[4]], [s[5]], [s[6]]
        rows.append({'id': c['id'], 'giver': c['giver'], 'evidence': c.get('evidence', ''), 'target': c.get('target', ''),
                     'reward': c['reward'], 'speaker': s[0], 'src': [c['bullet'], 0, 6],
                     'offer_lines': offer, 'waiting_lines': waiting, 'report_lines': report, 'completed_lines': done,
                     # the old (non-306) talk path reads the *Text fields only
                     'offer_text': ' '.join(offer), 'waiting_text': ' '.join(waiting), 'report_text': ' '.join(report), 'completed_text': ' '.join(done)})
        if c.get('evidence'):
            if len(s) != 10: raise SystemExit('%s: a fetch commission needs the row and its two lines' % c['bullet'])
            evidence.append({'id': c['evidence'], 'commission': c['id'], 'label': s[7], 'locked': s[8], 'text': s[9], 'src': [c['bullet'], 7, 9]})
    return rows, evidence


def text_rules_fail(rows, evidence):
    tr = json.loads(TEXTS.read_text(encoding='utf-8'))['text_rules']; bad = []
    def chk(name, text, mx):
        if len(text) > mx: bad.append('%s %d>%d' % (name, len(text), mx))
        if any(b in text for b in tr['banned']) or re.search(tr['banned_regex'], text): bad.append(name + ' banned word')
        if any(e in text for e in tr['banned_endings']): bad.append(name + ' banned ending')   # Texts308 TextRule: Contains
    for r in rows:
        chk(r['id'] + ' speaker', r['speaker'], tr['speaker_max_chars'])
        for k in ('offer_lines', 'waiting_lines', 'report_lines', 'completed_lines'):
            for n, t in enumerate(r[k]): chk('%s %s[%d]' % (r['id'], k, n), t, tr['max_chars'])
    for e in evidence:
        chk(e['id'] + ' label', e['label'], tr['label_max_chars'])
        chk(e['id'] + ' locked', e['locked'], tr['max_chars']); chk(e['id'] + ' text', e['text'], tr['max_chars'])
    return bad


def build():
    D = decision_strings(); F2 = D['F2']
    c2, c2_evidence = commissions(D)
    bad = text_rules_fail(c2, c2_evidence)
    if bad: raise SystemExit('REFUSED text rules: %s' % bad)
    data = {
        'id': 'field117', 'version': '117.2', 'status': 'TEST - D308-117 (2026-10-19 / C2 2026-10-20). Built by Tools/Art/FieldFirst308/field117_build.py '
                                                       '(lines copied from Docs/DECISIONS.md D308-117 F2 - F6 by position).',
        'doc': [
            'Read by FieldFirst308 (Editor/WorldMacro/FieldFirst308.cs). Every word the tool writes is here; the code holds none.',
            'documents[]: WorldMacroPlaytestSO.Documents rows. id = the UiProgress item id (prefix "letter."), grant_on_stage = the campaign '
            'stage whose completion gives it (same save), pages = the paper, notes = lines on the back shown once after_stage is done (max 3).',
            'campaign_start: the stages removed from InitialCompletedIds (a new game no longer starts with them). trigger_point = the content '
            'point that must exist (Conversation) and carry the stage TriggerId before the removal is allowed (otherwise the stage could not '
            'be completed at all).',
        ],
        'decision': {'file': 'Docs/DECISIONS.md', 'entry': ENTRY, 'bullet': 'F2'},
        'content': MAIN_CONTENT,
        'campaign': CAMPAIGN,
        'documents': [{
            'id': 'letter.commission', 'title': F2[0], 'grant_on_stage': 'commission', 'src': ['F2', 0, 4],
            'pages': F2[1:5],
            'notes': [{'after_stage': 'mine_evidence', 'text': F2[5]}, {'after_stage': 'cargo_contract', 'text': F2[6]}],
        }],
        'campaign_start': {'remove_initial': ['commission'], 'stage': 'commission', 'trigger_point': 'village_commission'},
        # C2 (TEST): the four sub-NPC commissions. Read by FieldFirst308 field308-c2: commissions[] -> WorldContentSO.Commissions
        # rows (giver point = the Texts308 point, must be a Conversation point with no talk rows), evidence[] -> the Evidence
        # point's LockedText (before the request) and Text (the pick-up). reward_band = the Spec range a reward must sit in.
        'c2': {'decision': {'file': 'Docs/DECISIONS.md', 'entry': ENTRY, 'bullets': ['F3', 'F4', 'F5', 'F6']},
               'reward_band': [50, 120], 'commissions': c2, 'evidence': c2_evidence},
        # D308-118 (2026-10-19, TEST): the 운해 band of RealmFog297 on the live fog material. Heights from field308-heights
        # (walkable ground p50 163 / p90 266, peaks 300 - 400): the band wraps the upper slopes, the summits stand above it.
        'cloud_band': {
            'decision': 'D308-118', 'material': 'Assets/_Project/Art/World/Finish297/Materials/Fog297.mat',
            'band': [265, 30, 0.014, 250],          # centre y, half-width m, density per metre, start m
            'shape': [380, 0.62, 0.14, 2000],        # bank noise scale m, coverage, edge softness, sky far cap m
            'colour': [0.94, 0.93, 0.89, 0.96],      # paper; a = maximum opacity
        },
        # SPEC-FIELD-FIRST-HOUR-308 A4 (TEST): from each beat, facing the next beat, is a guide of the next beat in the front view
        # and is the line of sight open? Read by FieldFirst308 field308-sight (read only). eye = beat place (x, z; eye 1.65 m over
        # the ground), toward = the next beat (heading), guides = what should lead there: x, z and up = metres over the ground
        # there, or y = an absolute height (beacon smoke column, summits). Positions from the Spec §1 beat table and the fh views.
        # SPEC-FIELD-FIRST-HOUR-308 C1 (TEST): side-path ends of the realm and what stands there. Read by FieldFirst308
        # field308-ends (read only). An end needs a reward / info / scene within radius_m; the nearest field enemies are the
        # gatekeeper candidates (enemy_prefixes). scene_words = scene object names that count (kind -> words).
        'ends': {
            'layout': 'Assets/_Project/Scenes/World/Main/WorldLayout_Main.asset', 'realm': 'cheongrim',
            'radius_m': 15, 'run_gap_m': 40, 'enemy_near_m': 60, 'bbox_pad_m': 150, 'mouth_m': 20, 'loop_m': 60,
            'enemy_prefixes': ['field308/', 'field308b/', 'forest_beast308/'],
            'scene_words': {'보상': ['Ore_Vein', 'Fragment', 'Satchel', 'Treasure', 'Jar308'], '정보': ['Stele', 'Notice'],
                            '서사': ['Shrine', 'Grave', 'Wreck']},
        },
        'sightline': {
            'eye_up': 1.65, 'front_half_deg': 51, 'end_gap_m': 3.0, 'soft_min_size_m': 2.0, 'soft_max_size_m': 40.0,
            'eye_steps_m': [0, 15, 30, 45, 60, 90, 120, 150, 200, 250, 300, 350, 400], 'road_snap_m': 30,   # the player walks on: the eye is taken at these metres along the heading, first open wins
            'legs': [
                {'id': 'L0 마당 → 성황당', 'eye': [3261.9, 1885.9], 'toward': [3301, 2226],
                 'guides': [{'name': '성황당', 'x': 3301, 'z': 2226, 'up': 4}, {'name': '금표 비석', 'x': 3339, 'z': 2173, 'up': 3}]},
                {'id': 'L1 성황당 → 금표 주막', 'eye': [3293.8, 2229.5], 'toward': [3069, 2339],
                 'guides': [{'name': '금표 주막 지붕', 'x': 3069, 'z': 2339, 'up': 7}, {'name': '주막 아궁이 연기', 'x': 3067.8, 'z': 2336.1, 'y': 169}]},
                {'id': 'L2 주막 → 역참', 'eye': [3061.2, 2340.8], 'toward': [2844, 2390],
                 'guides': [{'name': '역참 지붕', 'x': 2844, 'z': 2390, 'up': 7}]},
                {'id': 'L3 역참 → 마을', 'eye': [2841.6, 2382.4], 'toward': [2761, 2132],
                 'guides': [{'name': '마을 분소 지붕', 'x': 2761, 'z': 2132, 'up': 7}, {'name': '분소 아궁이 연기', 'x': 2755.1, 'z': 2122.6, 'y': 257.7}]},
                # MainPath runs village -> back down past the inn (arc 1231 -> 1663) -> logging camp (2177): the inn is passed twice,
                # so the leg is read in two halves at that crossroads
                {'id': 'L4a 마을 → 주막 갈림(되짚기)', 'eye': [2766.7, 2137.7], 'toward': [3075, 2339],
                 'guides': [{'name': '금표 주막 지붕', 'x': 3069, 'z': 2339, 'up': 7}, {'name': '주막 아궁이 연기', 'x': 3067.8, 'z': 2336.1, 'y': 169}, {'name': '벌목장', 'x': 3318, 'z': 2688, 'up': 6},
                            {'name': '청림 봉수 연기', 'x': 2854.6, 'z': 3494.9, 'y': 280}]},
                {'id': 'L4b 주막 갈림 → 벌목장', 'eye': [3075, 2339], 'toward': [3318, 2688],
                 'guides': [{'name': '벌목장', 'x': 3318, 'z': 2688, 'up': 6}, {'name': '청림 봉수 연기', 'x': 2854.6, 'z': 3494.9, 'y': 280},
                            {'name': '고개 마루 마른 고목(C1, 6 m 지점)', 'x': 3290, 'z': 2522, 'up': 6}]},
                {'id': 'L5 벌목장 → 깊은 숲 갈림', 'eye': [3317.0, 2695.9], 'toward': [3266, 3082],
                 'guides': [{'name': '갈림 자리', 'x': 3266, 'z': 3082, 'up': 3}, {'name': '신목 수관', 'x': 3710, 'z': 3255, 'y': 254},
                            {'name': '청림 봉수 연기', 'x': 2854.6, 'z': 3494.9, 'y': 280}]},
                {'id': 'L6 갈림 → 사냥꾼 주막', 'eye': [3262.7, 3089.3], 'toward': [3205, 3215],
                 'guides': [{'name': '사냥꾼 주막 지붕', 'x': 3205, 'z': 3215, 'up': 7}, {'name': '사냥꾼 주막 아궁이 연기', 'x': 3203.6, 'z': 3205.1, 'y': 184}, {'name': '청림 봉수 연기', 'x': 2854.6, 'z': 3494.9, 'y': 280}]},
                {'id': 'L7 사냥꾼 주막 → 청룡', 'eye': [3205.1, 3223.0], 'toward': [3210, 3550],
                 'guides': [{'name': '성소', 'x': 3211, 'z': 3430, 'up': 5}, {'name': '청룡 자리', 'x': 3210, 'z': 3550, 'up': 10},
                            {'name': '청림 봉수 연기', 'x': 2854.6, 'z': 3494.9, 'y': 280}]},
                # C1 (D308-117): the 신목 side path - does the walker see the gatekeepers and the end before reaching them?
                {'id': 'C1 신목 갈래 → 끝', 'eye': [3602, 3234], 'toward': [3709, 3254],
                 'guides': [{'name': '이끼 승냥이 문지기', 'x': 3683, 'z': 3227, 'up': 1.0}, {'name': '신목 밑동 껍질', 'x': 3697.6, 'z': 3248.3, 'up': 0.6}]},
            ],
        },
        # §8.13 (TEST): cooking smoke over the main roof of the settlement beats - the sheet forest hides the roofs until the player
        # is nearly there (field308-sight with sheet trees, 2026-10-20), a column of ink smoke rises above the crowns. Read by
        # EventWash308 wash308-hearth (the roof = largest "roof" renderer within r of x, z in the open scenes; the houses are in
        # W_Demo_Main). Column: rise x life ~ 44 m, about 35 m seen (the guide has to clear the crowns: field308-sight at +20/30/40/55 m over the roof, 2026-10-20, opened L4a only from +30 m and L3 from the start at +40 m). Density: rate 1.9 / size 2.2 / alpha .42 read as scattered smudges at 250 m (fhs stills), so 4.5 / 3 / .55, ink like the beacon (M_BeaconSmoke308, no emission).
        # 역참 (2844, 2390) has no building within 30 m - only 정담 standing on the road (wash308-hearth:probe) - so no row: smoke
        # from bare ground would have no source. It needs a building or a hearth prop first (PENDING_QUESTIONS_308).
        'hearth': {
            'column': {'rise': 2.2, 'life': 20.0, 'rate': 4.5, 'wind': 0.5, 'size0': 3.0, 'size1': 11.0, 'ink': [0.16, 0.155, 0.15, 0.55],
                       'cap': 100, 'radius': 0.5},
            'rows': [
                {'id': 'hearth_inn', 'name': '금표 주막', 'x': 3069, 'z': 2339, 'r': 30},
                {'id': 'hearth_village', 'name': '마을 분소', 'x': 2761, 'z': 2132, 'r': 30},
                {'id': 'hearth_hunter', 'name': '사냥꾼 주막', 'x': 3205, 'z': 3215, 'r': 30},
            ],
        },
    }
    return json.dumps(data, ensure_ascii=False, indent=1) + '\n'


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    text = build(); sha = hashlib.sha256(text.encode('utf-8')).hexdigest()
    if '--check' in sys.argv:
        cur = OUT.read_text(encoding='utf-8') if OUT.exists() else ''
        same = cur == text; print('field117.json %s a fresh build (sha %s)' % ('equals' if same else 'DIFFERS from', sha[:12])); return 0 if same else 2
    OUT.parent.mkdir(parents=True, exist_ok=True); OUT.write_text(text, encoding='utf-8', newline='\n')
    print('wrote %s sha %s' % (OUT, sha[:12])); return 0


if __name__ == '__main__':
    sys.exit(main())
