from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[2];ED=ROOT/'Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120';APP=ROOT/'Oheangbu/Assets/_Project/Scripts/App/SpellVFX120';OUT=ROOT/'Art/SpellVFX120/AreaFlow';OUT.mkdir(exist_ok=True)
old=json.loads((OUT.parent/'AreaWide/before_hashes.json').read_text(encoding='utf-8'))
if not (OUT/'before_hashes.json').exists():
    old['Oheangbu/Assets/_Project/Data/Configs/SpellBook_Proto.asset']=''
    (OUT/'before_hashes.json').write_text(json.dumps({p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in old},indent=2),encoding='utf-8')
def edit(path,old,new):
    s=path.read_text(encoding='utf-8');assert old in s,(path,old);path.write_text(s.replace(old,new),encoding='utf-8')
edit(APP/'Vfx120Profile.cs','public bool WideAreaRevision;','public bool WideAreaRevision;\n        public bool AreaFlowRevision;')
p=APP/'Vfx120Effect.AreaRemake.cs'
edit(p,'if(k==Vfx120AreaRemake.BambooField)Life=', 'if(Profile.AreaFlowRevision&&k==Vfx120AreaRemake.FlameCone)Life=Mathf.Max(Life,plan.Delay+2.5f);\n   if(k==Vfx120AreaRemake.BambooField)Life=')
edit(p,'shape.shapeType=ParticleSystemShapeType.ConeVolume;','shape.shapeType=Profile.AreaFlowRevision?ParticleSystemShapeType.Cone:ParticleSystemShapeType.ConeVolume;')
edit(p,'shape.length=Mathf.Max(1,plan.Length-1.6f);','shape.length=Profile.AreaFlowRevision?0:Mathf.Max(1,plan.Length-1.6f);')
edit(p,'m.startSize=ps.name=="FlameCore"?1.3f:.85f;','m.startSize=Profile.AreaFlowRevision?.12f:ps.name=="FlameCore"?1.3f:.85f;')
edit(p,'Profile.WideAreaRevision&&Profile.Glyph=="노"?.32f:.264f','Profile.AreaFlowRevision&&Profile.Glyph=="노"?.18f:Profile.WideAreaRevision&&Profile.Glyph=="노"?.32f:.264f')
edit(p,'  public static float BellWaveHeight', '''  // V1 rolling crest, widened with a softer shore transition; no bell silhouette.
  public static float ShoreWaveHeight(float across,float behind,float time)
  {
   float edge=Mathf.SmoothStep(0,1,Mathf.Clamp01((1-Mathf.Abs(across))/.35f));
   float envelope=Mathf.Pow(Mathf.Max(0,Mathf.Sin(Mathf.PI*Mathf.Clamp01(-behind/2.6f))),1.4f);
   return edge*envelope*(.95f+.2f*Mathf.Sin(time*Mathf.PI*2+across*2.5f));
  }
  float CurrentWaveHeight(float across,float behind,float time)=>Profile.AreaFlowRevision?ShoreWaveHeight(across,behind,time):Profile.WideAreaRevision?BellWaveHeight(across,behind,time):WaveHeight(across,behind,time);
  public static Vector3 FlameStreamPoint(Vector3 nozzle,Vector3 ground,Vector3 forward,Vector3 right,float length,float angle,float seed,float progress)
  {
   float spread=Mathf.Sin((seed+.17f)*31.7f)*angle*Mathf.Deg2Rad;
   var end=ground+(forward*Mathf.Cos(spread)+right*Mathf.Sin(spread))*length+Vector3.up*(.7f+seed*.7f);
   return Vector3.Lerp(nozzle,end,Mathf.Clamp01(progress));
  }
  public static float BellWaveHeight''')
edit(p,'float h=Profile.WideAreaRevision?BellWaveHeight(across,behind,t):WaveHeight(across,behind,t);','float h=CurrentWaveHeight(across,behind,t);')
edit(p,'_areaSim<p.Delay+(Profile.WideAreaRevision?.65f:.18f)','_areaSim<p.Delay+(Profile.AreaFlowRevision?1.68f:Profile.WideAreaRevision?.65f:.18f)')
edit(p,'if(Profile.WideAreaRevision&&(kind==Vfx120AreaRemake.SandFront||kind==Vfx120AreaRemake.WaterWave))','if((Profile.AreaFlowRevision&&kind==Vfx120AreaRemake.FlameCone)||Profile.WideAreaRevision&&(kind==Vfx120AreaRemake.SandFront||kind==Vfx120AreaRemake.WaterWave))')
edit(p,'       if(kind==Vfx120AreaRemake.SandFront)','''       if(kind==Vfx120AreaRemake.FlameCone)
       {
        particle.position=FlameStreamPoint(ReceivedOrigin,p.Point,_areaForward,_areaRight,p.Length,p.Angle,seed,life);
        particle.startSize=(ps.name=="FlameCore"?.12f:.07f)+Mathf.SmoothStep(0,1,life)*(ps.name=="FlameCore"?.8f:.5f);
       }
       else if(kind==Vfx120AreaRemake.SandFront)''')
edit(p,'''        float across=seed*2-1;var point=_areaStart+_areaForward*(along-1.3f-life*.5f)+_areaRight*(across*p.Radius);
        point.y=AreaGround(along,across)+BellWaveHeight(across,-1.3f,t)+.06f;particle.position=point;''','''        float across=seed*2-1,behind=-1.3f-life*.5f;
        if(Profile.AreaFlowRevision&&ps.name=="ShoreFoam")
        {
         int lane=(int)(particle.randomSeed%4);
         if(lane<2){across=(lane==0?-1:1)*(.88f+.12f*life);behind=-2.6f*seed;}
         else {behind=lane==2?-.04f-life*.12f:-2.56f+life*.12f;}
         particle.startSize=.18f+.22f*life;
        }
        var point=_areaStart+_areaForward*(along+behind)+_areaRight*(across*p.Radius);
        point.y=AreaGround(along+behind,across)+CurrentWaveHeight(across,behind,t)*fade+.04f;particle.position=point;''')
for name in ['Capture','Audit','Regression']:
    s=(ED/f'KtpAreaWide{name}.cs').read_text(encoding='utf-8').replace('AreaWide','AreaFlow')
    if name=='Capture':s=s.replace('new[]{"고","노","소","모","오"}','new[]{"고","노","소","오"}').replace('saved V1','saved AreaWide')
    if name=='Audit':
        s=s.replace('maxPeak<=2.26f&&minPeak>=1.94f','maxPeak<=1.16f&&minPeak>=.74f')
        s=s.replace('Vfx120Effect.BellWaveHeight','Vfx120Effect.ShoreWaveHeight')
        start=s.index('   Need(r,Vfx120Effect.ShoreWaveHeight(0,-1.3f,0)>')
        end=s.index('   Gameplay(r);',start)
        s=s[:start]+'''   Need(r,Vfx120Effect.ShoreWaveHeight(1,-1.3f,.4f)==0&&Vfx120Effect.ShoreWaveHeight(.95f,-1.3f,.4f)<.1f,"wave shore eases below ten centimeters at outer five percent");
   foreach(char g in "가나마사아고노소모오거너머서어"){book.TryGet(g,out var current);priorBook.TryGet(g,out var prior);Need(r,JsonUtility.ToJson(current)==JsonUtility.ToJson(prior),g+" damage rules unchanged in flow revision");}
   var cameraGo=new GameObject("FlowOriginAudit");var cam=cameraGo.AddComponent<Camera>();cam.aspect=16f/9;
   try{foreach(string g in new[]{"노","소"}){var profile=catalog.Entries.Single(x=>x.Glyph==g).Profile;var point=Vfx120LaunchPoint.Resolve(profile,cam,Vector3.zero);var viewport=cam.WorldToViewportPoint(point);Need(r,g=="노"?viewport.x>.6f&&viewport.y<.4f:viewport.y>1&&point.y>2,"launch framing "+g);}}
   finally{Object.DestroyImmediate(cameraGo);}
'''+s[end:]
        s=s.replace('     if(glyph=="고")Need(r,fx.AreaRaised==17,','     if(glyph=="고"){fx.Sample(3.5f);Need(r,fx.Life>=4.5f&&fx.AreaRaised==17,"bamboo persists through three and a half seconds");}\n     if(glyph=="고")Need(r,fx.AreaRaised==17,')
        s=s.replace('     if(glyph=="모")','''     if(glyph=="노"){fx.Sample(plan.Delay+1.4f);Need(r,fx.AreaParticles>0,"flamethrower continues beyond one second");Need(r,Vfx120Effect.FlameStreamPoint(Vector3.up,p.Point,Vector3.forward,Vector3.right,6,30,.5f,0)==Vector3.up,"stream begins at nozzle");}
     if(glyph=="오"){var foam=fx.GetComponentsInChildren<ParticleSystem>().First(x=>x.name=="ShoreFoam");Need(r,foam.particleCount>0&&foam.transform.localScale==Vector3.one,"unscaled shore foam emits");}
     if(glyph=="모")'''.replace('p.Point','plan.Point'))
    (ED/f'KtpAreaFlow{name}.cs').write_text(s,encoding='utf-8')
edit(ED/'Vfx120Queue.cs','                    case "AreaWideBuild":','''                    case "AreaFlowBuild": response.result = KtpAreaFlowBuild.Build(command.request); break;
                    case "AreaFlowCapture": response.result = KtpAreaFlowCapture.Start(command.request); break;
                    case "AreaFlowAudit": response.result = KtpAreaFlowAudit.Start(); break;
                    case "AreaFlowRegression": response.result = KtpAreaFlowRegression.Start(); break;
                    case "AreaWideBuild":''')
s=(ROOT/'Tools/SpellVFX120/run_area_wide.py').read_text(encoding='utf-8').replace('AreaWide','AreaFlow').replace("'고노소모오'","'고노소오'")
(ROOT/'Tools/SpellVFX120/run_area_flow.py').write_text(s,encoding='utf-8')
s=(ROOT/'Tools/SpellVFX120/audit_area_wide.py').read_text(encoding='utf-8').replace('AreaWide','AreaFlow')
(ROOT/'Tools/SpellVFX120/audit_area_flow.py').write_text(s,encoding='utf-8')
print('Prepared flow revision and preservation snapshot')
