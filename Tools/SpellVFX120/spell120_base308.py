"""SPEC-SPELL-120-308 WP-01A: (re)generate Rules308_Base.csv from the spell book the main scene reads today.

The Base sheet is the 36 glyphs that already resolve (numbers copied from the book copy under Architecture296/Data, the
legacy feature of each, the three legacy traits) plus the 24 reserved rows (Pending = their work package, no handler).
The 40 glyphs built in this phase are NOT in Base: each work package adds its own Rules308_WPnn.csv.

Read-only against Oheangbu/Assets. Writes one file into the stage (or --out). Re-running it must reproduce the file
byte for byte; spell120_contract308.py compares the sheet with every book of the copy chain.

usage: python Tools/SpellVFX120/spell120_base308.py [--check] [--out <file>]
"""
import argparse, csv, hashlib, io, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BOOK = ROOT / ('Oheangbu/Assets/_Project/Art/World/Architecture296/Data/'
               'dde80d292e16ddb48a9102e5bcae089d_17a09b6fa2e231f41a18e15962f1a2c4_607113b45769a524983ba10b2d5e1a71_SpellBook_WorldMacroSummon_TEST.asset')
MAP = ROOT / 'Art/SpellVFX120/Spell120_308/handler_map308.csv'
OUT = ROOT / 'Tools/Unity/Stage308_spell/_ProjectAssets/Data/Spells/Rules308_Base.csv'
COLUMNS = ['글자', 'Mode', 'Handler', 'Pending', 'Kind', 'Gate', 'Feature', 'Inherit', 'BasePower', 'SpeedMul', 'AreaShape',
           'AreaAngle', 'AreaRadius', 'AreaLength', 'AreaSpeed', 'AreaDelay', 'Shots', 'Interval', 'Scatter', 'Params']
KINDS = ['AttackSingle', 'AttackArea', 'Parry', 'Summon', 'Field', 'Buff', 'Ward', 'Install']
SHAPES = ['None', 'Cone', 'Circle', 'Path', 'Volley']
FIELDS = ['Kind', 'Element', 'BasePower', 'AreaShape', 'ProjectileSpeedMul', 'AreaAngle', 'AreaRadius', 'AreaLength', 'AreaSpeed',
          'AreaImpactDelay', 'VolleyShots', 'VolleyInterval', 'ScatterVolley']


def unity_string(text):
    text = text.strip()
    if text.startswith('"'):
        return text.strip('"').encode('utf-8').decode('unicode_escape')
    return text


def book_entries(path):
    """[(letter, {field: text})] of a SpellBookSO asset's _entries, in file order."""
    text = Path(path).read_text(encoding='utf-8')
    block = text.split('  _entries:', 1)[1]
    entries = []
    for line in block.splitlines()[1:]:
        if line.startswith('  - Letter:'):
            entries.append((unity_string(line.split(':', 1)[1]), {}))
        elif line.startswith('    ') and entries:
            key, value = line.strip().split(':', 1)
            entries[-1][1][key] = value.strip()
        elif line.strip() and not line.startswith('   '):
            break
    return entries


def number(text):
    """Unity float text -> the sheet's plain decimal ('' for zero: the consumer default)."""
    value = float(text)
    if value == 0:
        return ''
    out = repr(value)
    return out[:-2] if out.endswith('.0') else out


def core_handler(kind, shape):
    if kind == 'Parry':
        return 'core.parry'
    if kind == 'Summon':
        return 'core.summon'
    return {'None': 'core.single', 'Cone': 'core.cone', 'Circle': 'core.circle', 'Path': 'core.path', 'Volley': 'core.volley'}[shape]


def build():
    plan = list(csv.DictReader(io.StringIO(MAP.read_text(encoding='utf-8-sig'))))
    entries = dict(book_entries(BOOK))
    rows = []
    for item in plan:
        letter, package, handler, feature = item['글자'], item['묶음'], item['처리기'], item['구형 기능 키']
        row = dict.fromkeys(COLUMNS, '')
        row['글자'] = letter
        if package == 'legacy':
            row['Handler'], row['Feature'] = handler, feature
            if feature == 'book':
                e = entries[letter]
                kind, shape = KINDS[int(e['Kind'])], SHAPES[int(e['AreaShape'])]
                assert core_handler(kind, shape) == handler, (letter, kind, shape, handler)
                row['Kind'] = kind
                row['Gate'] = 'open' if item['해금'] == 'open' else ''
                row['BasePower'], row['SpeedMul'] = number(e['BasePower']), number(e['ProjectileSpeedMul'])
                row['AreaShape'] = '' if shape == 'None' else shape
                row['AreaAngle'], row['AreaRadius'], row['AreaLength'] = number(e['AreaAngle']), number(e['AreaRadius']), number(e['AreaLength'])
                row['AreaSpeed'], row['AreaDelay'] = number(e['AreaSpeed']), number(e['AreaImpactDelay'])
                row['Shots'], row['Interval'] = number(e['VolleyShots']), number(e['VolleyInterval'])
                row['Scatter'] = '1' if e['ScatterVolley'] == '1' else ''
                # the two glyphs the old wiring special-cased by name (now data)
                if handler == 'core.circle':
                    row['Params'] = 'area.spikes=1'
                if handler == 'core.path' and item['초성'] == 'ㅁ':
                    row['Params'] = 'area.seed=1'
            elif feature == 'giyeok':
                row['Inherit'] = 'base'
                if item['초성'] == 'ㅅ':
                    row['Params'] = 'trace.pierce=1'
        elif item['상태'].startswith('예약'):
            row['Pending'] = package
        else:
            continue  # blank glyphs and the glyphs of this phase's work packages have no Base row
        rows.append(row)
    out = io.StringIO()
    out.write('# SPEC-SPELL-120-308 Rules308_Base: the 36 glyphs that resolve today (values = the book the main scene reads) + 24 reserved rows.' + '\n')
    out.write('# Generated by Tools/SpellVFX120/spell120_base308.py. TEST numbers. Work packages add Rules308_WPnn.csv; they never edit this file.' + '\n')
    writer = csv.writer(out, lineterminator='\n')
    writer.writerow(COLUMNS)
    for row in rows:
        writer.writerow([row[c] for c in COLUMNS])
    return out.getvalue(), rows


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(); ap.add_argument('--check', action='store_true'); ap.add_argument('--out', default=str(OUT))
    a = ap.parse_args()
    text, rows = build()
    data = (chr(0xFEFF) + text).encode('utf-8')
    legacy = sum(1 for r in rows if r['Handler'])
    print('Rules308_Base: %d rows (%d legacy, %d reserved), sha256 %s' % (len(rows), legacy, len(rows) - legacy, hashlib.sha256(data).hexdigest()))
    target = Path(a.out)
    if a.check:
        same = target.exists() and target.read_bytes() == data
        print('CHECK ' + ('same as ' if same else 'DIFFERS from ') + str(target))
        sys.exit(0 if same else 1)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    print('wrote', target)


if __name__ == '__main__':
    main()
