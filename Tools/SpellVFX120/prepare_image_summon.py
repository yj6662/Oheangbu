import argparse,hashlib,json,shutil
from pathlib import Path
R=Path(__file__).resolve().parents[2];p=argparse.ArgumentParser();p.add_argument('name');p.add_argument('image');a=p.parse_args();O=R/'Art/SpellVFX120'/a.name;O.mkdir(exist_ok=True)
shutil.copy2(a.image,O/'Concept.png')
A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120';files=list((A/'Profiles').glob('*.asset'))
for folder in ['WoodDeer','FireHaetae','MetalTiger','StoneDokkaebi','StoneJangseung','MeshySummons']+(['DokkaebiClub'] if a.name=='WaterTurtle' else []):files+=list((A/folder).rglob('*'))
if not (O/'scope_before.json').exists():(O/'scope_before.json').write_text(json.dumps({p.relative_to(R).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files if p.is_file()},indent=2))
print(a.name,'PREPARED')
