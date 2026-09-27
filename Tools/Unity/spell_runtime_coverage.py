"""Read-only vocabulary/runtime-entry inventory. Does not infer combat correctness from VFX files."""
from pathlib import Path
import csv
import hashlib
import json
import re
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Continuation258'
CSV = ROOT / 'Docs/오행부_작도어휘_v0_1.csv'
BOOK = ROOT / 'Oheangbu/Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset'
RESOLVER = ROOT / 'Oheangbu/Assets/_Project/Scripts/Spellcraft/SpellResolver.cs'
WIRING = ROOT / 'Oheangbu/Assets/_Project/Scripts/App/CombatLoopWiring.cs'
WARDS = ROOT / 'Oheangbu/Assets/_Project/Scripts/App/EAWardRuntime.cs'
BUFFS = ROOT / 'Oheangbu/Assets/_Project/Scripts/App/EABuffRuntime.cs'
GIYEOK = ROOT / 'Oheangbu/Assets/_Project/Scripts/App/EAGiyeokRuntime.cs'
TRACES = ROOT / 'Oheangbu/Assets/_Project/Scripts/App/EAGiyeokRuntime.Traces.cs'


def run():
    # The prior inventory supplies the explicit 120-cell semantic crosswalk. Its
    # source CSV hash must match before its rows may be reused as design data.
    prior = json.loads((ROOT / 'Art/World/Compact/Rebuild/SpellCoverage254/inventory.json').read_text(encoding='utf-8-sig'))
    expected = next(v for k, v in prior['source_hashes'].items() if k.endswith('.csv'))
    if hashlib.sha256(CSV.read_bytes()).hexdigest() != expected:
        raise RuntimeError('Vocabulary CSV changed: rebuild the semantic crosswalk before auditing')
    import yaml
    body = re.sub(r'^%.*\n|^--- !u!.*\n', '', BOOK.read_text(encoding='utf-8-sig'), flags=re.M)
    book = yaml.safe_load(body)['MonoBehaviour']
    entries = next(v for v in book.values() if isinstance(v, list) and v and isinstance(v[0], dict) and 'Letter' in v[0])
    def glyph(value):
        return chr(value) if isinstance(value, int) else str(value)
    by_letter = {glyph(e['Letter']): e for e in entries}
    if len(by_letter) != len(entries):
        raise RuntimeError('Duplicate runtime book entry')
    resolver_source = RESOLVER.read_text(encoding='utf-8-sig')
    owner_source = GIYEOK.read_text(encoding='utf-8-sig') if GIYEOK.exists() else ''
    owner_match = re.search(r'public static bool Owns\(char letter\)=>([^;]+);', owner_source)
    resolver_match = re.search(r'char lookup = allowGiyeok && \((.*?)\) \?', resolver_source)
    giyeok_letters = (set(re.findall(r"'([^']+)'", owner_match[1])) & set(re.findall(r"'([^']+)'", resolver_match[1]))) if owner_match and resolver_match else set()
    rows = []
    for old in prior['rows']:
        letter = old['letter']
        row = {k: old[k] for k in ['letter', 'element', 'coda', 'category', 'contract', 'intentional_blank']}
        row['book_kind'] = by_letter.get(letter, {}).get('Kind')
        row['resolver_path'] = ('intentional_blank' if row['intentional_blank'] else
                                'book_entry' if letter in by_letter else
                                'opt_in_giyeok' if letter in giyeok_letters else
                                'opt_in_ea_ward' if letter in '구누무수우' and 'allowWards' in RESOLVER.read_text() and WARDS.exists() else
                                'opt_in_ea_buff' if letter in '걱넉먹석억' and 'allowEABuffs' in RESOLVER.read_text() and BUFFS.exists() else
                                'opt_in_guk' if letter == '국' and "letter.Letter == '국'" in RESOLVER.read_text() else 'missing')
        row['runtime_verification'] = 'NOT_VERIFIED_BY_THIS_STATIC_AUDIT'
        row['next_work'] = ('none' if row['intentional_blank'] else 'live semantic test' if row['resolver_path'] != 'missing' else 'implement rule and isolated catalog, then live semantic test')
        rows.append(row)
    report = {
        'utc': datetime.now(timezone.utc).isoformat(),
        'scope': 'Design CSV and explicit current book/resolver entry inventory. No recognition, semantic effect, unlock, live scene binding, VFX or performance pass is inferred.',
        'book': str(BOOK.relative_to(ROOT)),
        'source_hashes': {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in [CSV, BOOK, RESOLVER, WIRING, BUFFS, WARDS, GIYEOK, TRACES] if p.exists()},
        'assigned': sum(not r['intentional_blank'] for r in rows),
        'intentional_blanks': sum(r['intentional_blank'] for r in rows),
        'book_entries': len(entries),
        'missing_resolver_entries': sum(r['resolver_path'] == 'missing' for r in rows),
        'rows': rows,
    }
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'spell-runtime-inventory.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    with (OUT / 'spell-runtime-inventory.csv').open('w', encoding='utf-8-sig', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    print(json.dumps({k: report[k] for k in ['assigned', 'intentional_blanks', 'book_entries', 'missing_resolver_entries']}, ensure_ascii=False))


if __name__ == '__main__':
    run()
