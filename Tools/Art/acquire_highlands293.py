"""Acquire the user-approved (2026-09-27) Poly Haven CC0 assets for the Highlands293 sections.

python Tools/Art/acquire_highlands293.py
Models (1k FBX + bundled textures) → Art/World/Compact/Rebuild/Highlands293/Sources/<slug>/ (Blender inputs, outside Assets)
Textures (2k diffuse / normal GL / ARM) → Oheangbu/Assets/_Project/Art/World/Reworld292/Highlands293/Textures/
Every file is md5-checked against the Poly Haven API and recorded with sha256/bytes/author/licence in Highlands293/sources.json.
"""
from pathlib import Path
import hashlib, json, urllib.request
from concurrent.futures import ThreadPoolExecutor

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Highlands293'
SRC = OUT / 'Sources'
TEX = ROOT / 'Oheangbu/Assets/_Project/Art/World/Reworld292/Highlands293/Textures'
MODELS = ['rock_07', 'rock_09', 'stone_01', 'rock_moss_set_01', 'rock_moss_set_02', 'mountainside', 'rock_face_01', 'rock_face_02',
          # Step 2 (user-approved 2026-09-27, ~46MB): weathered granite blocks/tors, scree, dead timber
          'namaqualand_boulder_02', 'namaqualand_boulder_04', 'namaqualand_boulder_05', 'namaqualand_cliff_02', 'namaqualand_stones_01',
          'dead_tree_trunk']
TEXTURES = ['forrest_ground_01', 'forest_ground_04', 'rocky_trail', 'mossy_rock',
            # Step 2 (user-approved 2026-09-27, ~116MB): Jeokro / Cheolong / Hyeongang / Hwanggyeong rock, trail, near floor
            'burned_ground_01', 'red_laterite_soil_stones', 'rock_boulder_cracked',
            'dark_rock_02', 'rocks_ground_06', 'stone_wall_04',
            'rock_3', 'brown_mud_rocks_01', 'brown_mud_03',
            'tiger_rock', 'stone_pathway', 'dry_ground_rocks']
HEADERS = {'User-Agent': 'Oheangbu-Highlands293/1.0'}


def fetch(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=180).read()


def jobs():
    out = []
    for slug in MODELS:
        files = json.loads(fetch('https://api.polyhaven.com/files/' + slug)); info = json.loads(fetch('https://api.polyhaven.com/info/' + slug))
        item = files['fbx']['1k']['fbx']; base = SRC / slug
        out.append((slug, 'source_model_1k', item, base / Path(item['url']).name, info))
        for rel, inc in item.get('include', {}).items():
            out.append((slug, 'bundled_' + Path(rel).stem, inc, base / rel, info))
    for slug in TEXTURES:
        files = json.loads(fetch('https://api.polyhaven.com/files/' + slug)); info = json.loads(fetch('https://api.polyhaven.com/info/' + slug))
        for channel in ('Diffuse', 'nor_gl', 'arm'):
            item = files[channel]['2k']['jpg']
            out.append((slug, channel, item, TEX / Path(item['url']).name, info))
    return out


def download(job):
    slug, channel, item, dst, info = job
    dst.parent.mkdir(parents=True, exist_ok=True)
    if not dst.exists() or hashlib.md5(dst.read_bytes()).hexdigest() != item['md5']:
        data = fetch(item['url'])
        if hashlib.md5(data).hexdigest() != item['md5']:
            raise RuntimeError('md5 mismatch: ' + item['url'])
        dst.write_bytes(data)
    return dict(asset=slug, channel=channel, path=str(dst.relative_to(ROOT)).replace('\\', '/'), url=item['url'], license='CC0-1.0',
                authors=list(info.get('authors', {}).keys()), source='https://polyhaven.com/a/' + slug,
                sha256=hashlib.sha256(dst.read_bytes()).hexdigest(), bytes=dst.stat().st_size)


if __name__ == '__main__':
    todo = jobs()
    with ThreadPoolExecutor(max_workers=4) as pool:
        ledger = list(pool.map(download, todo))
    (OUT / 'sources.json').write_text(json.dumps(ledger, indent=1, ensure_ascii=False), encoding='utf8')
    print(len(ledger), 'files', round(sum(r['bytes'] for r in ledger) / 1e6, 1), 'MB')
