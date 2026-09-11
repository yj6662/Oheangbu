from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parents[2];ED=ROOT/'Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120';OUT=ROOT/'Art/SpellVFX120/AreaBranch';OUT.mkdir(exist_ok=True)
old=json.loads((OUT.parent/'AreaRift/before_hashes.json').read_text(encoding='utf-8'))
if not (OUT/'before_hashes.json').exists():(OUT/'before_hashes.json').write_text(json.dumps({p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in old},indent=2),encoding='utf-8')
s=(ED/'KtpAreaRiftCapture.cs').read_text(encoding='utf-8').replace('AreaRift','AreaBranch').replace('new[]{"고","노","소","모"}','new[]{"모"}').replace('saved AreaFlow','saved AreaRift')
(ED/'KtpAreaBranchCapture.cs').write_text(s,encoding='utf-8')
s=(ROOT/'Tools/SpellVFX120/run_area_rift.py').read_text(encoding='utf-8').replace('AreaRift','AreaBranch').replace("'고노소모'","'모'")
(ROOT/'Tools/SpellVFX120/run_area_branch.py').write_text(s,encoding='utf-8')
p=ED/'Vfx120Queue.cs';s=p.read_text(encoding='utf-8');s=s.replace('                    case "AreaRiftBuild":','''                    case "AreaBranchBuild": response.result=KtpAreaBranchBuild.Build(command.request);break;
                    case "AreaBranchCapture": response.result=KtpAreaBranchCapture.Start(command.request);break;
                    case "AreaBranchAudit": response.result=KtpAreaBranchBuild.Audit();break;
                    case "AreaRiftBuild":''');p.write_text(s,encoding='utf-8')
print('Single-spell capture and preservation prepared')
