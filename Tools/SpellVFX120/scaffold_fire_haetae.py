"""Create isolated Haetae presentation/editor tooling from the established deer path."""
from pathlib import Path
import json,hashlib
R=Path(__file__).resolve().parents[2];S=R/'Oheangbu/Assets/_Project/Scripts';O=R/'Art/SpellVFX120/FireHaetae'
files=list((R/'Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles').glob('*.asset'))
files+=list((R/'Oheangbu/Assets/_Project/Art/SpellVFX120/WoodDeer').rglob('*'))
files+=list((R/'Oheangbu/Assets/_Project/Art/SpellVFX120/MeshySummons').rglob('*'))
before={str(p.relative_to(R)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files if p.is_file()}
if not (O/'scope_before.json').exists():(O/'scope_before.json').write_text(json.dumps(before,indent=2))
runtime=(S/'App/SpellVFX120/WoodDeerVfx.cs').read_text()
runtime=runtime.replace('WoodDeer','FireHaetae').replace('hoof','paw').replace('antlers','mane')
start=runtime.index('   // Short local roots')
end=runtime.index('   renderers=',start)
runtime=runtime[:start]+runtime[end:]
start=runtime.index('  Mesh Roots()');end=runtime.index('  public void Sample(',start)
runtime=runtime[:start]+runtime[end:]
runtime=runtime.replace('foreach(var ps in systems){float local=Age-3.8f;if(local>=0&&!Ended)ps.Simulate(local,false,true,false);else ps.Clear();}', '''foreach(var ps in systems)
   {
    float local=ps.name=="FormationSparks"?Age:ps.name=="ManeEmbers"?Age-.8f:Age-3.8f;
    bool window=ps.name=="FormationSparks"?Age<1.2f:ps.name=="ManeEmbers"?Age<3.8f:!Ended;
    if(local>=0&&window&&!Ended)ps.Simulate(local,false,true,false);else ps.Clear();
   }''')
runtime=runtime.replace('anchors.Count!=4','anchors.Count!=3').replace('Four authored paw anchors','Three planted paw anchors').replace('new Vector3[4]','new Vector3[3]').replace('new float[4]','new float[3]').replace('i<4','i<3').replace('j<4','j<3')
runtime=runtime.replace('settings.Brightness=.65f','settings.Brightness=.95f')
old='seal.Sample(.3f);Bounds sb=new Bounds(Centre,Vector3.zero);foreach(var r in seal.GetComponentsInChildren<Renderer>())sb.Encapsulate(r.bounds);sealGo.transform.localScale=Vector3.one*(2.25f/Mathf.Max(.01f,Mathf.Max(sb.size.x,sb.size.z)));seal.Sample(0);'
new='''seal.Sample(1.2f);float diameter=0;
   foreach(var ps in seal.GetComponentsInChildren<ParticleSystem>())
   {
    var particles=new ParticleSystem.Particle[ps.main.maxParticles];int count=ps.GetParticles(particles);
    for(int i=0;i<count;i++)diameter=Mathf.Max(diameter,particles[i].GetCurrentSize(ps)*Mathf.Abs(ps.transform.lossyScale.x));
   }
   if(diameter<.01f)throw new InvalidOperationException("KTP seal emitted no readable particle");
   sealGo.transform.localScale=Vector3.one*(3.1f/diameter);seal.Sample(0);'''
assert old in runtime
runtime=runtime.replace(old,new)
(S/'App/SpellVFX120/FireHaetaeVfx.cs').write_text(runtime)
review=(S/'Editor/SpellVFX120/WoodDeerReview.cs').read_text().replace('WoodDeer','FireHaetae').replace('WOOD_DEER','FIRE_HAETAE').replace('DeerAudit','HaetaeAudit').replace('deer','haetae').replace('hoof','paw')
# Capture longer than the visual life to verify a clean end.
(S/'Editor/SpellVFX120/FireHaetaeReview.cs').write_text(review)
shader=(R/'Oheangbu/Assets/_Project/Shaders/WoodDeer.shader').read_text().replace('Oheangbu/WoodDeer','Oheangbu/FireHaetae').replace('Bark','Charcoal')
shader=shader.replace('_BaseMap("Charcoal color"','_EmberMask("Ember mask",2D)="black"{}\n  _BaseMap("Charcoal color"')
shader=shader.replace('TEXTURE2D(_BaseMap);','TEXTURE2D(_EmberMask);SAMPLER(sampler_EmberMask);TEXTURE2D(_BaseMap);')
shader=shader.replace('return half4(MixFog(color,i.fog),1);','''float mask=SAMPLE_TEXTURE2D(_EmberMask,sampler_EmberMask,i.uv).r;
    float h=(i.world.y-_Ground)/max(.1,_Height);
    float front=min(saturate((_Age-.2)),1-saturate((_Age-3.8)/.8));
    float edge=(1-smoothstep(0,.045,abs(h-front)))*((_Age<1.2||_Age>3.8)?1:0);
    color+=float3(1,.13,.012)*(mask*(.7+.08*sin(_Age*4))+edge*.7);
    return half4(MixFog(color,i.fog),1);''')
shader=shader.replace('float3(.19,.19,.19)','float3(.32,.32,.32)')
(R/'Oheangbu/Assets/_Project/Shaders/FireHaetae.shader').write_text(shader)
build=(S/'Editor/SpellVFX120/WoodDeerBuild.cs').read_text().replace('WoodDeer','FireHaetae').replace('WOOD_DEER','FIRE_HAETAE').replace('곰','놈').replace('Gom','Nom').replace('12543','12532')
start=build.index('   var body=Material(');end=build.index('   var source=',start)
build=build[:start]+'   var body=Material("M_CharcoalEmber",Color.white,true);\n'+build[end:]
build=build.replace('renderer.name.Contains("Body")?body:renderer.name.Contains("Leaves")?leaves:roots','body')
build=build.replace('var model=Object.Instantiate(source);model.name="PF_FireHaetae_Static";', 'var model=new GameObject("PF_FireHaetae_Static");var imported=Object.Instantiate(source,model.transform);imported.name="FireHaetae_Body";')
build=build.replace('"/FixedWards/Pattern_0.prefab"','"/FixedWards/Pattern_1.prefab"')
build=build.replace('foreach(string kind in new[]{"BaseColor","Normal"})','foreach(string kind in new[]{"BaseColor","Normal","EmberMask"})')
build=build.replace('if(bark){mat.SetTexture(', 'if(bark){mat.SetTexture("_EmberMask",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_FireHaetae_EmberMask.png"));mat.SetTexture(')
start=build.index('    KtpOffsetGuardBuild.Debris(');end=build.index('    p.FireHaetaeDebris=',start)
build=build[:start]+'''    KtpOffsetGuardBuild.Debris(debris,Folder,"FormationSparks",36,new Color(1,.28f,.035f),0,.65f,new Vector3(.02f,.04f,.008f),-.1f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"ManeEmbers",16,new Color(.95f,.22f,.025f),0,.5f,new Vector3(.014f,.025f,.006f),-.12f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"DissolveAsh",48,new Color(.22f,.18f,.16f),0,.65f,new Vector3(.025f,.045f,.008f),.08f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"DissolveSparks",28,new Color(1,.24f,.025f),0,.55f,new Vector3(.015f,.03f,.006f),-.15f);
    foreach(var ps in debris.GetComponentsInChildren<ParticleSystem>())
    {
     var main=ps.main;main.startSpeed=new ParticleSystem.MinMaxCurve(.1f,.45f);main.playOnAwake=false;
     var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(.65f,1.1f,1.45f);shape.position=new Vector3(0,.75f,0);
     if(ps.name=="ManeEmbers"){main.loop=true;main.duration=3;var em=ps.emission;em.SetBursts(new ParticleSystem.Burst[0]);em.rateOverTime=8;shape.scale=new Vector3(.42f,.3f,.55f);shape.position=new Vector3(0,1.1f,.65f);}
     if(ps.name=="FormationSparks"){shape.position=new Vector3(0,.06f,0);shape.scale=new Vector3(1.1f,.08f,1.8f);}
    }
''' + build[end:]
build=build.replace('modelMaterials\\\":3','modelMaterials\\\":1')
build=build.replace('new Vector3[4]','new Vector3[3]').replace('new int[4]','new int[3]').replace('c<4','c<3').replace('i<4','i<3').replace('Enumerable.Range(0,4)','Enumerable.Range(0,3)').replace('four hoof positions','three planted paw positions (the fourth paw is raised in the source pose)')
build=build.replace('go.transform.position=new Vector3(centers[i].x,hoofMin,centers[i].z);','go.transform.position=points.Where(point=>Enumerable.Range(0,3).OrderBy(j=>(point-centers[j]).sqrMagnitude).First()==index).OrderBy(point=>point.y).First();')
(S/'Editor/SpellVFX120/FireHaetaeBuild.cs').write_text(build)
print('SCAFFOLDED')
