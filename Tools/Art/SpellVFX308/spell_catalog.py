#!/usr/bin/env python3
"""술식 도감 틀 (2026-10-30, the user: "술식 카탈로그를 만들어줘. 예전에 에셋 카탈로그 만든 것처럼" -> "술식은 이제 새로 만들어 가는 과정이고,
나중에 채워질 때마다 체크할 수 있도록 틀만 만들어달라는 소리였어").
A progress board for the NEW spell VFX (D308-153 restart) in the shape of the model asset catalogue (Oheangbu/Screenshots/AssetCatalog/index.html):
all 120 vol.2 glyphs as slots; a slot fills only from new-route work. The old free showcase (Vol2Vfx/free_showcase) is deliberately NOT read.
  rows    = Oheangbu/Assets/_Project/Data/Spells/Vol2/vol2_table120_v11.csv (vol.2 table, 120 rows)
  done?   = Oheangbu/Assets/_Project/Art/SpellVFX308/Restart/SpellFxCatalog.asset (letter / variant entries)  -> 구현
            Art/SpellVFX308/Restart/shots/<글자>_<variant>/f_*.jpg                                               -> 촬영 (thumbnail + clip copied in)
            Tools/Art/SpellVFX308/spell_catalog_status.json (hand ledger: chosen variant, judge score, user PASS, note) -> 판정
  output  = Oheangbu/Screenshots/SpellCatalog/{index.html, Thumbnails/, Media/} (local, not committed - like the asset catalogue)
Re-run whenever a spell is added or judged:  python Tools/Art/SpellVFX308/spell_catalog.py"""
import csv, datetime, json, re, subprocess
from pathlib import Path
from PIL import Image

REPO = Path(__file__).resolve().parents[3]
CSV = REPO / 'Oheangbu/Assets/_Project/Data/Spells/Vol2/vol2_table120_v11.csv'
CATALOG = REPO / 'Oheangbu/Assets/_Project/Art/SpellVFX308/Restart/SpellFxCatalog.asset'
SHOTS = REPO / 'Art/SpellVFX308/Restart/shots'
LEDGER = Path(__file__).with_name('spell_catalog_status.json')
TEMPLATE = Path(__file__).with_name('spell_catalog_template.html')
OUT = REPO / 'Oheangbu/Screenshots/SpellCatalog'
FF = r'C:\Users\yj666\AppData\Roaming\Python\Python314\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe'

ONSET = {'ㄱ': 'g', 'ㄴ': 'n', 'ㅁ': 'm', 'ㅅ': 's', 'ㅇ': ''}
VOWEL = {'ㅏ': 'a', 'ㅓ': 'eo', 'ㅗ': 'o', 'ㅜ': 'u'}
CODA = {'없음': '', 'ㄱ': 'k', 'ㄴ': 'n', 'ㅁ': 'm', 'ㅅ': 't', 'ㅇ': 'ng'}
STAGES = ['미착수', '구현', '촬영', '대리 판정', '사용자 PASS']


def stem_of(row):
    return (ONSET[row['초성'][0]] + VOWEL[row['중성'][0]] + CODA[row['종성'].split('=')[0]]) or 'u0'


def catalog_entries():
    """letter -> [variant, ...] from the SpellFx catalogue asset (letters are \\uXXXX-escaped in the YAML)."""
    text = CATALOG.read_text(encoding='utf-8')
    out = {}
    for m in re.finditer(r'- letter: "?(.*?)"?\n\s+variant: (\S+)', text):
        letter = m[1].encode('ascii').decode('unicode_escape') if '\\u' in m[1] else m[1]
        out.setdefault(letter, []).append(m[2])
    return out


def shot_dir(glyph, variant):
    d = SHOTS / f'{glyph}_{variant}'
    return d if d.is_dir() and any(d.glob('f_*.jpg')) else None


def ff(*args):
    subprocess.run([FF, '-hide_banner', '-loglevel', 'error', '-y', *map(str, args)], check=True)


def media(glyph, stem, d):
    """Thumbnails/<stem>.jpg + Media/<stem>.mp4 from the shot frames; skipped when the copies are newer than the frames."""
    frames = sorted(d.glob('f_*.jpg'))
    thumb, clip = OUT / 'Thumbnails' / f'{stem}.jpg', OUT / 'Media' / f'{stem}.mp4'
    newest = max(f.stat().st_mtime for f in frames)
    if not thumb.exists() or thumb.stat().st_mtime < newest:
        Image.open(frames[int(len(frames) * 0.45)]).convert('RGB').resize((640, 360), Image.LANCZOS).save(thumb, quality=86)
    if not clip.exists() or clip.stat().st_mtime < newest:
        ff('-framerate', 60, '-start_number', int(frames[0].stem[2:]), '-i', d / 'f_%05d.jpg', '-vf', 'scale=640:-2',
           '-c:v', 'libx264', '-crf', 26, '-preset', 'slow', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', '-an', clip)
    return {'thumb': f'Thumbnails/{stem}.jpg', 'clip': f'Media/{stem}.mp4', 'frames': len(frames),
            'shotDir': str(d.relative_to(REPO)).replace('\\', '/'), 'shotTime': datetime.datetime.fromtimestamp(newest).strftime('%Y-%m-%d %H:%M')}


def main():
    for sub in ('Thumbnails', 'Media'):
        (OUT / sub).mkdir(parents=True, exist_ok=True)
    rows = list(csv.DictReader(open(CSV, encoding='utf-8-sig')))
    cat = catalog_entries()
    ledger = json.loads(LEDGER.read_text(encoding='utf-8')) if LEDGER.exists() else {}
    entries = []
    for i, row in enumerate(rows):
        g, stem = row['글자'], stem_of(row)
        led = ledger.get(g, {})
        variants = cat.get(g, [])
        variant = led.get('variant') or (variants[-1] if variants else '')
        d = shot_dir(g, variant) if variant else None
        stage = 0
        if variant and variant in variants: stage = 1
        if d: stage = 2
        if led.get('score') is not None: stage = 3
        if led.get('userPass'): stage = 4
        ink = re.search(r'먹 (\d+)\(TEST\)', row['비고'])
        entries.append({
            'index': i + 1, 'glyph': g, 'stem': stem,
            'onset': row['초성'][0], 'body': row['초성'].split('=')[1], 'vowel': row['중성'][0], 'frame': row['중성'].split('=')[1],
            'coda': row['종성'].split('=')[0], 'verb': row['종성'].split('=')[1], 'kind': row['구분'],
            'def': row['한줄정의'], 'mark': row['자국윤곽'], 'motion': row['몸거동'], 'material': row['반응재료'], 'note': row['비고'],
            'ink': int(ink[1]) if ink else None,
            'stage': stage, 'stageLabel': STAGES[stage], 'variant': variant, 'variants': variants,
            'score': led.get('score'), 'judge': led.get('judge', ''), 'userPass': bool(led.get('userPass')),
            'decision': led.get('decision', ''), 'ledgerNote': led.get('note', ''),
            'media': media(g, stem, d) if d else None,
        })
    counts = [sum(e['stage'] == k for e in entries) for k in range(len(STAGES))]
    data = {'entries': entries, 'stages': STAGES, 'metadata': {
        'generated': datetime.datetime.now().strftime('%Y-%m-%d %H:%M'),
        'table': str(CSV.relative_to(REPO)).replace('\\', '/'), 'catalog': str(CATALOG.relative_to(REPO)).replace('\\', '/'),
        'ledger': str(LEDGER.relative_to(REPO)).replace('\\', '/'), 'counts': counts}}
    page = TEMPLATE.read_text(encoding='utf-8').replace('/*DATA*/{}', json.dumps(data, ensure_ascii=False).replace('</', '<\\/'))
    (OUT / 'index.html').write_text(page, encoding='utf-8')
    print('wrote', OUT / 'index.html', dict(zip(STAGES, counts)))


if __name__ == '__main__':
    main()
