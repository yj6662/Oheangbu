from pathlib import Path
R=Path(__file__).resolve().parents[2];S=R/'Oheangbu/Assets/_Project/Scripts'
build=(S/'Editor/SpellVFX120/StoneDokkaebiBuild.cs').read_text().replace('StoneDokkaebi','StoneJangseung').replace('STONE_DOKKAEBI','STONE_JANGSEUNG')
for field in ['Prefab','Seal','Debris','Presentation']:build=build.replace('p.StoneJangseung'+field,'p.StoneDokkaebi'+field)
build=build.replace('two planted foot positions','two base support positions').replace('Baseline_Mom.asset','Baseline_Dokkaebi.asset')
build=build.replace('new Vector3(1.3f,1.3f,.9f)','new Vector3(.9f,1.8f,.8f)').replace('new Vector3(0,.85f,0)','new Vector3(0,1.15f,0)')
(S/'Editor/SpellVFX120/StoneJangseungBuild.cs').write_text(build)
review=(S/'Editor/SpellVFX120/StoneDokkaebiReview.cs').read_text().replace('StoneDokkaebiBuild','StoneJangseungBuild').replace('StoneDokkaebiReview','StoneJangseungReview').replace('STONE_DOKKAEBI','STONE_JANGSEUNG').replace('StoneDokkaebi_Body','StoneJangseung_Body')
(S/'Editor/SpellVFX120/StoneJangseungReview.cs').write_text(review)
shader=(R/'Oheangbu/Assets/_Project/Shaders/StoneDokkaebi.shader').read_text().replace('Oheangbu/StoneDokkaebi','Oheangbu/StoneJangseung')
(R/'Oheangbu/Assets/_Project/Shaders/StoneJangseung.shader').write_text(shader)
queue=S/'Editor/SpellVFX120/Vfx120Queue.cs';s=queue.read_text();marker='case "StoneDokkaebiBuild":'
idx=s.index(marker)
if 'case "StoneJangseungBuild":' not in s:
 s=s[:idx]+'''case "StoneJangseungBuild": response.result=StoneJangseungBuild.Build();break;
                case "StoneJangseungAudit": response.result=StoneJangseungReview.Audit();break;
                case "StoneJangseungCapture": response.result=StoneJangseungReview.Start();break;
                case "StoneJangseungCaptureExternal": response.result=StoneJangseungReview.Start(true);break;
                '''+s[idx:];queue.write_text(s)
print('SCAFFOLDED')
