"""Preserve the four selected profiles and an exact asset-scope manifest."""
from pathlib import Path
import hashlib, json
root=Path(__file__).resolve().parents[2]
out=root/'Art/SpellVFX120/Emphasis4'
out.mkdir(exist_ok=True)
assets=root/'Oheangbu/Assets'
paths=list((assets/'_Project/Art/SpellVFX120/Profiles').glob('*.asset'))
paths+=list((assets/'KoreanTraditionalPattern_Effect').rglob('*'))
paths=[p for p in paths if p.is_file()]
manifest=out/'before_hashes.json'
if not manifest.exists():
    def digest(p):
        with p.open('rb') as stream: return hashlib.file_digest(stream,'sha256').hexdigest()
    manifest.write_text(json.dumps({str(p.relative_to(root)):digest(p) for p in paths},indent=2),encoding='utf-8')
for name in ['001_AC00','025_B098','007_AC70','031_B108']:
    dst=out/(name+'.asset.txt')
    if not dst.exists(): dst.write_bytes((assets/f'_Project/Art/SpellVFX120/Profiles/{name}.asset').read_bytes())
print('Preserved profiles and manifest',out)
