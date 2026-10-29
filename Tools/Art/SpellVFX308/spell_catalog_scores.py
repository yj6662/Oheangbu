#!/usr/bin/env python3
"""Fills Tools/Art/SpellVFX308/spell_catalog_status.json from the judge's verdicts (2026-10-30, the user: "ㄱ 끝나면 나머지 술식도 완성해서 술식 도감에 반영해놔").
For every Art/SpellVFX308/Restart/judge/g_<글자>_r<n>/verdict.md (가 is g_ga_r<n>) the latest round's total ("**합계** | **100** | **NN**") becomes the glyph's
score; the trail of rounds goes into "judge". Hand-written fields (note, decision, userPass, variant) are kept. Glyphs with prefabs but no verdict get a note.
  python Tools/Art/SpellVFX308/spell_catalog_scores.py && python Tools/Art/SpellVFX308/spell_catalog.py"""
import json, re
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
JUDGE = REPO / 'Art/SpellVFX308/Restart/judge'
LEDGER = Path(__file__).with_name('spell_catalog_status.json')
TOTAL = re.compile(r'\*\*합계\*\*\s*\|\s*\*\*100\*\*\s*\|\s*\*\*([\d.]+)\*\*')


def main():
    led = json.loads(LEDGER.read_text(encoding='utf-8')) if LEDGER.exists() else {}
    rounds = {}
    for d in JUDGE.glob('g_*_r*'):
        m = re.fullmatch(r'g_(.+)_r(\d+)', d.name)
        v = d / 'verdict.md'
        if not m or not v.exists():
            continue
        g = '가' if m[1] == 'ga' else m[1]
        text = v.read_text(encoding='utf-8', errors='replace')
        t = TOTAL.search(text)
        if t:
            # a verdict may carry earlier rounds as extra columns ("| **합계** | **100** | **59** | **65** | **71.5** |"): the last bold number is this round's
            row = text[t.start():text.find('\n', t.start())]
            nums = re.findall(r'\*\*([\d.]+)\*\*', row)[1:]
            rounds.setdefault(g, []).append((int(m[2]), float(nums[-1] if nums else t[1])))
    for g, rs in rounds.items():
        rs.sort()
        e = led.setdefault(g, {})
        e.setdefault('variant', 'A')
        last = rs[-1][1]
        e['score'] = int(round(last))
        trail = ' → '.join(str(int(round(s))) for _, s in rs)
        status = '대리 통과' if last >= 90 else ('최대 회차 소진' if rs[-1][0] >= 6 else '회차 진행 중')
        e['judge'] = f'대리 판정(Fable) · {len(rs)}회차 {trail} · {status}'
        e.setdefault('decision', 'D308-153')
    LEDGER.write_text(json.dumps(led, ensure_ascii=False, indent=1), encoding='utf-8')
    print('scored', len(rounds), 'glyphs:', ', '.join(f'{g} {led[g]["score"]}' for g in sorted(rounds)))


if __name__ == '__main__':
    main()
