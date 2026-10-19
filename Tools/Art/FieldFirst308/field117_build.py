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


def build():
    F2 = decision_strings()['F2']
    data = {
        'id': 'field117', 'version': '117.1', 'status': 'TEST - D308-117 (2026-10-19). Built by Tools/Art/FieldFirst308/field117_build.py '
                                                       '(lines copied from Docs/DECISIONS.md D308-117 F2 by position).',
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
        # D308-118 (2026-10-19, TEST): the 운해 band of RealmFog297 on the live fog material. Heights from field308-heights
        # (walkable ground p50 163 / p90 266, peaks 300 - 400): the band wraps the upper slopes, the summits stand above it.
        'cloud_band': {
            'decision': 'D308-118', 'material': 'Assets/_Project/Art/World/Finish297/Materials/Fog297.mat',
            'band': [285, 25, 0.006, 250],          # centre y, half-width m, density per metre, start m
            'shape': [380, 0.45, 0.12, 2500],        # bank noise scale m, coverage, edge softness, sky far cap m
            'colour': [0.93, 0.92, 0.88, 0.9],      # paper; a = maximum opacity
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
