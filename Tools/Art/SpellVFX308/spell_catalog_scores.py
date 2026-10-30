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
    rounds, membrane = {}, {}
    # D308-157: judge/f_<글자>_r<n> are the membrane-grammar rounds (variant F, the game default once a frame passes its gate) - kept apart from the vine (g_) rounds
    for d in list(JUDGE.glob('g_*_r*')) + list(JUDGE.glob('f_*_r*')):
        m = re.fullmatch(r'([gf])_(.+)_r(\d+)', d.name)
        v = d / 'verdict.md'
        if not m or not v.exists():
            continue
        g = '가' if m[2] == 'ga' else m[2]
        if m[1] == 'f':
            t = TOTAL.search(v.read_text(encoding='utf-8', errors='replace'))
            if t:
                row = v.read_text(encoding='utf-8', errors='replace')[t.start():]
                nums = re.findall(r'\*\*([\d.]+)\*\*', row[:row.find('\n')])[1:]
                membrane.setdefault(g, []).append((int(m[3]), float(nums[-1] if nums else t[1])))
            continue
        m = (None, m[2], m[3])
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
    # D308-161 (2026-10-31): the hexagon membrane is the defence look only - the five wards (their own H rounds) and the five bare-ㅓ parries.
    # Every other glyph's f_ rounds were a trial that is kept as variant F but is not the glyph's look: noted, not scored.
    DEFENCE = set('거너머서어')
    for g, rs in list(membrane.items()):
        if g in DEFENCE:
            continue
        rs.sort()
        e = led.setdefault(g, {})
        e['variant'] = 'A'
        e['note'] = f'육각 막 시험(F) {len(rs)}회차 마지막 {int(round(rs[-1][1]))} — D308-161로 방어 전용이 되어 보관, 새 룩 = 먹 붓획 + 몸 물성(구현 전)'
        if e.get('decision') == 'D308-157':
            e['decision'] = 'D308-161'
        del membrane[g]
    for g, rs in membrane.items():
        rs.sort()
        e = led.setdefault(g, {})
        vine = rounds.get(g)
        last = rs[-1][1]
        e['variant'] = 'F'
        e['score'] = int(round(last))
        trail = ' → '.join(str(int(round(s))) for _, s in rs)
        status = '대리 통과' if last >= 90 else ('최대 회차 소진' if rs[-1][0] >= 6 else '회차 진행 중')
        e['judge'] = f'대리 판정(Fable) · 막 문법 F {len(rs)}회차 {trail} · {status}' + (f' (덩굴 A 마지막 {int(round(vine[-1][1]))})' if vine else '')
        e['decision'] = 'D308-157'
        e['note'] = '막 문법(F, ㅜ 막과 같은 육각 셀)이 게임 기본 — 덩굴 프리팹 G_는 되돌리기용으로 보관'
    LEDGER.write_text(json.dumps(led, ensure_ascii=False, indent=1), encoding='utf-8')
    print('scored', len(rounds), 'glyphs:', ', '.join(f'{g} {led[g]["score"]}' for g in sorted(rounds)))


if __name__ == '__main__':
    main()
