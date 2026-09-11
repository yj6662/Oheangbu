from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parents[2];ED=ROOT/'Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120';OUT=ROOT/'Art/SpellVFX120/AreaNatural';OUT.mkdir(exist_ok=True)
old=json.loads((OUT.parent/'AreaBranch/before_hashes.json').read_text(encoding='utf-8'))
if not (OUT/'before_hashes.json').exists():
 (OUT/'before_hashes.json').write_text(json.dumps({p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in old},indent=2),encoding='utf-8')
for name in ['KtpAreaBranchCapture.cs']:
 s=(ED/name).read_text(encoding='utf-8').replace('AreaBranch','AreaNatural').replace('saved AreaRift profiles','saved AreaBranch profiles')
 (ED/name.replace('Branch','Natural')).write_text(s,encoding='utf-8')
p=ED/'Vfx120Queue.cs';s=p.read_text(encoding='utf-8')
if 'case "AreaNaturalBuild"' not in s:
 s=s.replace('                    case "AreaBranchBuild":','''                    case "AreaNaturalBuild": response.result=KtpAreaNaturalBuild.Build(command.request);break;
                    case "AreaNaturalCapture": response.result=KtpAreaNaturalCapture.Start(command.request);break;
                    case "AreaNaturalAudit": response.result=KtpAreaNaturalBuild.Audit();break;
                    case "AreaBranchBuild":''');p.write_text(s,encoding='utf-8')
s=(ROOT/'Tools/SpellVFX120/run_area_branch.py').read_text(encoding='utf-8').replace('AreaBranch','AreaNatural').replace('saved AreaRift profiles','saved AreaBranch profiles')
(ROOT/'Tools/SpellVFX120/run_area_natural.py').write_text(s,encoding='utf-8')
print('AreaNatural snapshots and capture prepared')
