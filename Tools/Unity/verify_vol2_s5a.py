"""S5-a (D308-149) drift check, no Unity needed: the Unity-side copies must be byte-identical to the places they come from.
  Tools/SpellVFX120/Pure308/Vol2V3/src/*      ==  Tools/Unity/Vol2S5aStage/Vol2/Rules/*  ==  Oheangbu/Assets/_Project/Scripts/Vol2/Rules/*
  Tools/Unity/Vol2S5aStage/<Vol2/Import|Vol2/Data|Editor/Vol2>/*  ==  Oheangbu/Assets/_Project/Scripts/<same>/*
  Assets CSV copies  ==  Docs/Research originals (when present), and their SHA-256 == Golden/import_manifest_v11.json sourceHashes
Usage: python Tools/Unity/verify_vol2_s5a.py        exit 0 = no drift"""
from pathlib import Path
import hashlib, json, sys

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'Tools/SpellVFX120/Pure308/Vol2V3/src'
STAGE = ROOT / 'Tools/Unity/Vol2S5aStage'
ASSETS = ROOT / 'Oheangbu/Assets/_Project/Scripts'
DATA = ROOT / 'Oheangbu/Assets/_Project/Data/Spells/Vol2'
GOLDEN = ROOT / 'Tools/SpellVFX120/Pure308/Vol2V3/Golden'


def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()


def names(d, pattern): return sorted(f.name for f in d.glob(pattern) if f.is_file())


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    bad = 0

    def same(label, a, b):
        nonlocal bad
        ok = a.exists() and b.exists() and sha(a) == sha(b)
        if not ok: bad += 1
        print(('OK    ' if ok else 'DRIFT ') + label)

    rules = sorted(f.name for f in SRC.glob('*') if f.suffix in ('.cs', '.asmdef'))
    for n in rules:
        same('Rules/' + n + ' (src -> stage)', SRC / n, STAGE / 'Vol2/Rules' / n)
        same('Rules/' + n + ' (stage -> Assets)', STAGE / 'Vol2/Rules' / n, ASSETS / 'Vol2/Rules' / n)
    extra = set(names(STAGE / 'Vol2/Rules', '*')) ^ set(rules)
    if extra: bad += 1; print('DRIFT Rules file lists differ: ' + ', '.join(sorted(extra)))
    for folder in ('Vol2/Import', 'Vol2/Data', 'Editor/Vol2'):
        for n in names(STAGE / folder, '*'):
            same(folder + '/' + n + ' (stage -> Assets)', STAGE / folder / n, ASSETS / folder / n)
        extra = set(names(STAGE / folder, '*')) ^ {n for n in names(ASSETS / folder, '*') if not n.endswith('.meta')}
        if extra: bad += 1; print('DRIFT %s file lists differ: %s' % (folder, ', '.join(sorted(extra))))
    frozen = json.loads((GOLDEN / 'import_manifest_v11.json').read_text(encoding='utf-8'))['sourceHashes']
    for key, copy, original in (('numbers', 'vol2_numbers_v3.csv', ROOT / 'Docs/Research/e0/numbers_v3.csv'),
                                ('table', 'vol2_table120_v11.csv', ROOT / 'Docs/Research/vol2/vol2_table120_v11.csv')):
        ok = sha(DATA / copy) == frozen[key]
        bad += 0 if ok else 1; print(('OK    ' if ok else 'DRIFT ') + copy + ' sha256 == frozen manifest')
        if original.exists(): same(copy + ' == Docs original', DATA / copy, original)
    print('NO DRIFT' if bad == 0 else '%d DRIFT' % bad)
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
