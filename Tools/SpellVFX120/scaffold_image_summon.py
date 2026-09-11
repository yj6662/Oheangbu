import argparse
from pathlib import Path
R=Path(__file__).resolve().parents[2];S=R/'Oheangbu/Assets/_Project/Scripts';p=argparse.ArgumentParser();p.add_argument('name',choices=['DokkaebiClub','WaterTurtle']);a=p.parse_args();name=a.name;water=name=='WaterTurtle';count=4 if water else 3;glyph='옴' if water else '몸'
def rename(s):return s.replace('StoneDokkaebi',name).replace('STONE_DOKKAEBI',name.upper()).replace('몸',glyph)
runtime=rename((S/'App/SpellVFX120/StoneDokkaebiVfx.cs').read_text())
runtime=runtime.replace('anchors.Count!=2',f'anchors.Count!={count}').replace('new Vector3[2]',f'new Vector3[{count}]').replace('new float[2]',f'new float[{count}]').replace('i<2',f'i<{count}').replace('j<2',f'j<{count}').replace('Two planted foot anchors required',f'{count} ground support anchors required')
if water:runtime=runtime.replace('3.4f/diameter','4.2f/diameter')
(S/f'App/SpellVFX120/{name}Vfx.cs').write_text(runtime)
build=rename((S/'Editor/SpellVFX120/StoneDokkaebiBuild.cs').read_text()).replace('new Vector3[2]',f'new Vector3[{count}]').replace('new int[2]',f'new int[{count}]').replace('c<2',f'c<{count}').replace('i<2',f'i<{count}').replace('Enumerable.Range(0,2)',f'Enumerable.Range(0,{count})').replace('Baseline_Mom.asset','Baseline_Previous.asset').replace('tris>18000','tris>22000')
if water:
 build=build.replace('Pattern(Folder,2,true)','Pattern(Folder,4,true)').replace('new Color(.43f,.35f,.25f)','new Color(.38f,.64f,.69f)').replace('new Color(.46f,.39f,.29f)','new Color(.38f,.63f,.7f)').replace('new Color(.47f,.38f,.26f,.3f)','new Color(.60f,.79f,.81f,.22f)').replace('FormationPebbles','FormationDroplets').replace('DissolveStones','DissolveWater').replace('FormationDust','FormationMist').replace('DissolveDust','DissolveMist').replace('M_WeatheredGranite','M_TurtleJade')
else:build=build.replace('M_WeatheredGranite','M_AgedBronze')
(S/f'Editor/SpellVFX120/{name}Build.cs').write_text(build)
review=rename((S/'Editor/SpellVFX120/StoneDokkaebiReview.cs').read_text())
(S/f'Editor/SpellVFX120/{name}Review.cs').write_text(review)
shader=rename((R/'Oheangbu/Assets/_Project/Shaders/StoneDokkaebi.shader').read_text())
if water:shader=shader.replace('float3(.20,.14,.075)','float3(.09,.18,.22)')
(R/f'Oheangbu/Assets/_Project/Shaders/{name}.shader').write_text(shader)
profile=S/'App/SpellVFX120/Vfx120Profile.cs';s=profile.read_text();marker='        public bool StoneDokkaebiPresentation;'
if f'public bool {name}Presentation;' not in s:s=s.replace(marker,f'        public bool {name}Presentation;\n        public GameObject {name}Prefab, {name}Seal, {name}Debris;\n'+marker);profile.write_text(s)
effect=S/'App/SpellVFX120/Vfx120Effect.cs';s=effect.read_text()
if f'public {name}Vfx {name}' not in s:
 marker='        public StoneDokkaebiVfx StoneDokkaebi'
 idx=s.index(marker);s=s[:idx]+f'        public {name}Vfx {name} {{get;private set;}}\n        private void Clear{name}(){{if({name}==null)return;{name}.Dispose();if(Application.isPlaying)Destroy({name});else DestroyImmediate({name});{name}=null;}}\n'+s[idx:]
 s=s.replace('            ClearStoneDokkaebi();',f'            Clear{name}();\n            ClearStoneDokkaebi();')
 marker='            if(Profile.StoneDokkaebiPresentation)';idx=s.index(marker);s=s[:idx]+f'            if(Profile.{name}Presentation){{Life={name}Vfx.ReviewLife;{name}=gameObject.AddComponent<{name}Vfx>();{name}.Configure(Profile,ReceivedOrigin,transform.forward);return;}}\n'+s[idx:]
 marker='            if(StoneDokkaebi!=null)';idx=s.index(marker);s=s[:idx]+f'            if({name}!=null){{{name}.Sample(Age);return;}}\n'+s[idx:];effect.write_text(s)
queue=S/'Editor/SpellVFX120/Vfx120Queue.cs';s=queue.read_text();marker='case "StoneDokkaebiBuild":';idx=s.index(marker)
if f'case "{name}Build":' not in s:
 code=''.join(f'case "{name}{op}": response.result={name}{cls}.{call};break;\n                    ' for op,cls,call in [('Build','Build','Build()'),('Audit','Review','Audit()'),('Capture','Review','Start()'),('CaptureExternal','Review','Start(true)')]);s=s[:idx]+code+s[idx:];queue.write_text(s)
# Only this profile opts into the new appearance; old flags would otherwise win on reuse.
buildpath=S/f'Editor/SpellVFX120/{name}Build.cs';s=buildpath.read_text();s=s.replace(f'p.{name}Presentation=true;',f'p.StoneDokkaebiPresentation=false;p.{name}Presentation=true;');buildpath.write_text(s)
print(name,'SCAFFOLDED')
