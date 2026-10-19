#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D308-117 (SPEC-FIELD-FIRST-HOUR-308 B1 / B3): adds the 덕대 칠복 (F1) and the 광부 아낙 (F7) to the DEPLOYED texts308.json -
an additive patch, not a rebuild: Stage308_texts19/texts308_build.py is behind the deployed copy (308.texts19.3, relayout fix4),
so a rebuild would undo those places. Every word is copied from Docs/DECISIONS.md D308-117 bullets F1 / F7 by position (the
quoted strings of the bullet in order), the same rule as D308-19. Idempotent: earlier XF* rows are replaced.

  python Tools/Art/FieldFirst308/field117_texts.py           patch (backup in Art/Playtest306/Backups/FieldFirst308 first)
  python Tools/Art/FieldFirst308/field117_texts.py --dry     print what would change
  python Tools/Art/FieldFirst308/field117_texts.py --revert  restore the first backup

Places are offline candidates (the editor's physical ground and Texts308 clear{} decide); bodies are clones the user judges on
the still. After a patch: Texts308 content-apply:main -> scene-apply:main -> check:main (Edit mode, realms open-all first).
"""
import json, re, shutil, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
DECISIONS = ROOT / 'Docs' / 'DECISIONS.md'
DATA = ROOT / 'Art' / 'World' / 'Compact' / 'Rebuild' / 'CliffBoundary308' / 'texts308.json'
BACKUP = ROOT / 'Art' / 'Playtest306' / 'Backups' / 'FieldFirst308' / 'texts308.before_f117.json'
ENTRY = 'D308-117'
VERSION_TAG = '+f117.1'

# id -> (bullet tag, index of the quoted string in that bullet, kind)
SRC = {
    'F1_speaker': ('F1', 0, 'speaker'), 'F1_first': ('F1', 1, 'line'),
    'F1_row1': ('F1', 2, 'row'), 'F1_r1a': ('F1', 3, 'line'), 'F1_r1b': ('F1', 4, 'line'),
    'F1_row2': ('F1', 5, 'row'), 'F1_r2a': ('F1', 6, 'line'), 'F1_r2b': ('F1', 7, 'line'),
    'F1_row3': ('F1', 8, 'row'), 'F1_r3a': ('F1', 9, 'line'), 'F1_r3b': ('F1', 10, 'line'),
    'F7_speaker': ('F7', 0, 'speaker'), 'F7_first': ('F7', 1, 'line'),
    'F7_row': ('F7', 2, 'row'), 'F7_1': ('F7', 3, 'line'), 'F7_2': ('F7', 4, 'line'),
}

NOTE_F1_POINT = ("D308-117 B1: id = the campaign stage commission TriggerId (village_commission), so the general demo path completes the "
                 "stage on the first talk and gives the letter (Content.Documents letter.commission, GrantOnStage commission). NOT a "
                 "Commissions GiverId (TryHandleCommission would take it first). The yard where the new game starts (StartFeet 3261,1878, "
                 "mine_start): about 11 m in front of the player, outside the tutorial boss (51 m from the start) and mine_beast/0 "
                 "(detection 16 + leash 28). The board of D308-19 T2 stays at the shaft mouth (168 m).")
NOTE_F7_POINT = ("D308-117 B3: a miner's wife on the stretch shaft mouth -> 성황당 (the 537 m gap of SPEC-FIELD-FIRST-HOUR-308 §1), "
                 "between the notice board (3363,2012) and the 금표 stele (3339,2173). Testimony only (what she saw / heard), no narration.")


def decision_strings():
    t = DECISIONS.read_text(encoding='utf-8')
    i = t.index('### ' + ENTRY); j = t.find('\n### ', i + 5); blk = t[i:j if j > 0 else len(t)]
    out = {}
    for ln in blk.split('\n'):
        m = re.match(r'\s+- \*\*(F\d+)', ln)
        if m: out[m.group(1)] = [a or b for a, b in re.findall(r'"([^"]+)"|`([^`]+)`', ln)]
    return out


def josa(word):
    c = word[-1]
    return '과' if '가' <= c <= '힣' and (ord(c) - 0xAC00) % 28 else '와'


def rows():
    D = decision_strings()
    lines = {k: {'status': 'TEST', 'kind': kind, 'src': [tag, n], 'text': D[tag][n]} for k, (tag, n, kind) in SRC.items()}
    for who in ('F1', 'F7'):
        sp = lines[who + '_speaker']['text']
        lines[who + '_prompt'] = {'status': 'TEST', 'kind': 'prompt', 'derived': who + '_speaker + "' + josa(sp) + ' 이야기"', 'text': sp + josa(sp) + ' 이야기'}
    owners = {'F1': ['village_commission'], 'F7': ['mine_refugee308']}
    points = [
        {'op': 'XF1', 'id': 'village_commission', 'kind': 'Conversation', 'speaker': 'F1_speaker', 'prompt': 'F1_prompt', 'first': ['F1_first'],
         'radius': 2.5, 'max_slope_deg': 16,
         'rows': [{'label': 'F1_row1', 'lines': ['F1_r1a', 'F1_r1b']}, {'label': 'F1_row2', 'lines': ['F1_r2a', 'F1_r2b']},
                  {'label': 'F1_row3', 'lines': ['F1_r3a', 'F1_r3b']}],
         'places': [{'x': 3256.0, 'z': 1889.0}, {'x': 3266.0, 'z': 1890.5}, {'x': 3251.5, 'z': 1885.0}],
         'clear': {'boss': 'mine_tutorial_boss', 'boss_min_m': 40.0, 'enemies': ['mine_beast/0'], 'enemies_min_m': 44.0, 'points_min_m': 5.0},
         'note': NOTE_F1_POINT},
        {'op': 'XF7', 'id': 'mine_refugee308', 'kind': 'Conversation', 'speaker': 'F7_speaker', 'prompt': 'F7_prompt', 'first': ['F7_first'],
         'radius': 2.5, 'max_slope_deg': 16,
         'rows': [{'label': 'F7_row', 'lines': ['F7_1', 'F7_2']}],
         'places': [{'x': 3345.0, 'z': 2135.0}, {'x': 3350.0, 'z': 2128.0}, {'x': 3338.0, 'z': 2142.0}],
         'clear': {'enemies': ['mine_fire/0', 'mine_beast/1'], 'enemies_min_m': 44.0, 'points_min_m': 5.0},
         'note': NOTE_F7_POINT},
    ]
    people = [
        {'op': 'XF1', 'id': 'village_commission', 'source_key': 'Roadside303/Givers/carter303', 'source_point': 'carter303',
         'job_profile': 'Assets/_Project/Data/World/NpcJobs306/NpcJob306_inspector.asset',
         'face': {'x': 3261.0, 'z': 1878.0}, 'remove_children': [], 'reseat_children': ['RiceSack'], 'props': [],
         'note': "body = a clone of the carter (a labourer, his rice sack kept as the yard's sack); faces the start. Job = inspector "
                 "(looks things over). Same face in two places (review R4 precedent) - the user judges on the still FX1."},
        {'op': 'XF7', 'id': 'mine_refugee308', 'source_key': 'Rebuild_InnPeople/herbalist', 'source_point': 'herbalist',
         'job_profile': 'Assets/_Project/Data/World/NpcJobs306/NpcJob306_resident.asset',
         'face': {'x': 3363.0, 'z': 2012.0}, 'remove_children': [], 'reseat_children': [], 'props': [],
         'note': "body = a clone of the inn herbalist; she faces back toward the shaft mouth. Bundles / cart / seal ribbon of B3 come "
                 "from Places308, not here. The user judges on the still FX7."},
    ]
    stills = [{'id': 'FX1', 'what': '덕대 칠복', 'look_at': 'village_commission', 'distance_m': 6},
              {'id': 'FX7', 'what': '광부 아낙', 'look_at': 'mine_refugee308', 'distance_m': 6}]
    return lines, owners, points, people, stills


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    if '--revert' in sys.argv:
        if not BACKUP.exists(): print('no backup'); return 2
        shutil.copyfile(BACKUP, DATA); print('restored', DATA); return 0
    d = json.loads(DATA.read_text(encoding='utf-8'))
    lines, owners, points, people, stills = rows()
    tr = d['text_rules']; bad = []
    for k, e in lines.items():
        kind = e['kind']; mx = tr['max_chars'] if kind == 'line' else tr['label_max_chars'] if kind == 'row' else tr[kind + '_max_chars']
        if len(e['text']) > mx: bad.append('%s %d>%d' % (k, len(e['text']), mx))
        if any(b in e['text'] for b in tr['banned']) or re.search(tr['banned_regex'], e['text']): bad.append(k + ' banned')
    if bad: print('REFUSED text rules:', bad); return 2
    keep = lambda arr: [x for x in arr if not str(x.get('op', '')).startswith('XF')]
    new = dict(d)
    new['lines'] = {k: v for k, v in d['lines'].items() if not k.startswith('F')}; new['lines'].update(lines)
    new['owners'] = {k: v for k, v in d['owners'].items() if not k.startswith('F')}; new['owners'].update(owners)
    new['points'] = keep(d['points']) + points
    new['people'] = keep(d['people']) + people
    new['stills'] = [s for s in d.get('stills', []) if not s['id'].startswith('FX')] + stills
    base = re.sub(r'\+f117\.\d+$', '', d['version']); new['version'] = base + VERSION_TAG
    if '--dry' in sys.argv:
        print('version', d['version'], '->', new['version'])
        print('lines +', len(lines), 'points', [p['id'] for p in points], 'people', [p['id'] for p in people])
        for k, v in lines.items(): print(' ', k, v['kind'], v['text'])
        return 0
    if not BACKUP.exists(): shutil.copyfile(DATA, BACKUP)
    DATA.write_text(json.dumps(new, ensure_ascii=False, indent=1) + '\n', encoding='utf-8', newline='\n')
    print('patched', DATA, 'version', new['version'], '(backup', BACKUP.name + ')'); return 0


if __name__ == '__main__':
    sys.exit(main())
