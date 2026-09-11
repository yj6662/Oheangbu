from pathlib import Path
import json,hashlib
root=Path(__file__).resolve().parents[2]
p=root/'Oheangbu/Assets/_Project/Scripts/App/SpellVFX120/Vfx120Effect.cs'
s=p.read_text(encoding='utf-8-sig')
if 'BuildFireBolt();' not in s:
    s=s.replace('IsWoodLift(Profile);','IsWoodLift(Profile) || IsFireBolt(Profile);')
    for verb in ('Clear','Build','Sample'):
        s=s.replace(f'{verb}WoodLift();',f'{verb}WoodLift();\n            {verb}FireBolt();')
    p.write_text(s,encoding='utf-8')
p=root/'Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120/Vfx120Queue.cs'
s=p.read_text(encoding='utf-8-sig')
if 'case "FireBoltBuild"' not in s:
    s=s.replace('case "BambooBoltBuild":','case "FireBoltBuild": response.result=Vfx120FireBoltBuilder.Build(); break;\n                    case "BambooBoltBuild":')
    p.write_text(s,encoding='utf-8')
p=root/'Art/SpellVFX120/na45_before_profiles.json'
if not p.exists():
    folder=root/'Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles'
    p.write_text(json.dumps({x.name:hashlib.sha256(x.read_bytes()).hexdigest() for x in folder.glob('*.asset')},indent=2),encoding='utf-8')
