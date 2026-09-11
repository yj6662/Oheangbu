from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[2];ED=ROOT/'Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120';APP=ROOT/'Oheangbu/Assets/_Project/Scripts/App/SpellVFX120';OUT=ROOT/'Art/SpellVFX120/AreaRift';OUT.mkdir(exist_ok=True)
old=json.loads((OUT.parent/'AreaFlow/before_hashes.json').read_text(encoding='utf-8'))
if not (OUT/'before_hashes.json').exists():(OUT/'before_hashes.json').write_text(json.dumps({p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in old},indent=2),encoding='utf-8')
def edit(path,old,new):
 s=path.read_text(encoding='utf-8');assert old in s,(path,old);path.write_text(s.replace(old,new),encoding='utf-8')
edit(APP/'Vfx120Profile.cs','public bool AreaFlowRevision;','public bool AreaFlowRevision;\n        public bool AreaReadabilityRevision, EarthRift;')
p=APP/'Vfx120Effect.AreaRemake.cs'
edit(p,'  GameObject _areaHost;','  LineRenderer[] _riftLines;\n  GameObject _areaHost;')
edit(p,'   BuildAreaSeal();','   if(Profile.EarthRift)_riftLines=_areaHost.GetComponentsInChildren<LineRenderer>();\n   BuildAreaSeal();')
edit(p,'   if(_areaParticles!=null&&_areaParticles.Length>0)','   if(Profile.EarthRift)SampleEarthRift(t,fade);\n   if(_areaParticles!=null&&_areaParticles.Length>0)')
edit(p,'p.Length,p.Angle,seed,life);','p.Length,Profile.AreaReadabilityRevision?p.Angle*(seed<.85f?.22f:.65f):p.Angle,seed,life);')
edit(p,'particle.startSize=(ps.name=="FlameCore"?.12f:.07f)+Mathf.SmoothStep(0,1,life)*(ps.name=="FlameCore"?.8f:.5f);','particle.startSize=Profile.AreaReadabilityRevision?(ps.name=="FlameCore"?.09f:.05f)+Mathf.SmoothStep(0,1,life)*(ps.name=="FlameCore"?.62f:.25f):(ps.name=="FlameCore"?.12f:.07f)+Mathf.SmoothStep(0,1,life)*(ps.name=="FlameCore"?.8f:.5f);')
edit(p,'       else if(kind==Vfx120AreaRemake.SandFront)','''       else if(Profile.EarthRift)
       {
        float birth=t-life*particle.startLifetime;
        float station=Mathf.Clamp(Mathf.Floor(Mathf.Max(0,birth-p.Delay)*p.Speed/1.2f)*1.2f,0,p.Length);
        int lane=(int)(particle.randomSeed%7);float x=RiftAcross(lane,station,p.Length)*p.Radius;
        float spread=(seed-.5f)*life*.65f;
        var point=_areaStart+_areaForward*(station+spread)+_areaRight*(x+spread);
        float lift=4*life*(1-life)*(ps.name=="Grains"?1.25f:.8f);
        point.y=AreaGround(station,x/p.Radius)+.025f+lift;particle.position=point;
        particle.startSize=ps.name=="Grains"?.07f+seed*.05f:.22f+life*.55f;
       }
       else if(kind==Vfx120AreaRemake.SandFront)''')
edit(p,'   _areaHost=null;_areaWater=null;','   _riftLines=null;_areaHost=null;_areaWater=null;')
for name in ['Capture','Audit','Regression']:
 s=(ED/f'KtpAreaFlow{name}.cs').read_text(encoding='utf-8').replace('AreaFlow','AreaRift')
 if name=='Capture':s=s.replace('"고","노","소","오"','"고","노","소","모"').replace('saved AreaWide','saved AreaFlow')
 if name=='Audit':
  s=s.replace('points.Max(x=>x.y)>2.5f','points.Max(x=>x.y)<2f&&points.Max(x=>x.y)>.2f').replace('sand vortex rises above two and a half meters','rift debris remains low and grounded')
  s=s.replace('fx.Sample(3.5f);Need(r,fx.Life>=4.5f&&fx.AreaRaised==17,"bamboo persists through three and a half seconds");','fx.Sample(2.5f);Need(r,Mathf.Abs(fx.Life-3)<.001f&&fx.AreaRaised==17,"bamboo persists until near three second cutoff");fx.Sample(3.01f);Need(r,fx.GetComponentsInChildren<MeshRenderer>().All(x=>!x.enabled),"bamboo hidden after three seconds");')
  s=s.replace('viewport.y<.4f','viewport.y<.2f')
  key='   Gameplay(r);'
  s=s.replace(key,'''   var needle=catalog.Entries.Single(x=>x.Glyph=="소").Profile.NativeBodyPrefab;
   Need(r,needle.transform.localScale.x>=2.5f&&needle.GetComponent<LineRenderer>().startWidth>=.04f,"needle silhouette and trail expanded");
   Need(r,needle.GetComponent<Renderer>().sharedMaterial.IsKeywordEnabled("_EMISSION"),"needle emissive silver material enabled");
   var earth=catalog.Entries.Single(x=>x.Glyph=="모").Profile;
   Need(r,earth.EarthRift&&earth.NativeBodyPrefab.GetComponentsInChildren<LineRenderer>().Length==7,"seven earth fissures replace vortex");
   for(int lane=0;lane<7;lane++)Need(r,Mathf.Abs(Vfx120Effect.RiftAcross(lane,12,12))<=1,"rift lanes remain in damage corridor");
   Gameplay(r);''')
 (ED/f'KtpAreaRift{name}.cs').write_text(s,encoding='utf-8')
edit(ED/'Vfx120Queue.cs','                    case "AreaFlowBuild":','''                    case "AreaRiftBuild": response.result = KtpAreaRiftBuild.Build(command.request); break;
                    case "AreaRiftCapture": response.result = KtpAreaRiftCapture.Start(command.request); break;
                    case "AreaRiftAudit": response.result = KtpAreaRiftAudit.Start(); break;
                    case "AreaRiftRegression": response.result = KtpAreaRiftRegression.Start(); break;
                    case "AreaFlowBuild":''')
for name in ['run','audit']:
 s=(ROOT/f'Tools/SpellVFX120/{name}_area_flow.py').read_text(encoding='utf-8').replace('AreaFlow','AreaRift').replace("'고노소오'","'고노소모'")
 (ROOT/f'Tools/SpellVFX120/{name}_area_rift.py').write_text(s,encoding='utf-8')
print('Rift revision prepared')
