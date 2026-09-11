from pathlib import Path
R=Path(__file__).resolve().parents[2];S=R/'Oheangbu/Assets/_Project/Scripts'
def rename(s):return s.replace('MetalTiger','StoneDokkaebi').replace('METAL_TIGER','STONE_DOKKAEBI').replace('Tiger','Dokkaebi').replace('tiger','dokkaebi').replace('솜','몸').replace('Som','Mom')
runtime=rename((S/'App/SpellVFX120/MetalTigerVfx.cs').read_text())
runtime=runtime.replace('anchors.Count!=4','anchors.Count!=2').replace('Four planted paw','Two planted foot').replace('new Vector3[4]','new Vector3[2]').replace('new float[4]','new float[2]').replace('i<4','i<2').replace('j<4','j<2').replace('3.8f/diameter','3.4f/diameter')
runtime=runtime.replace('bool forming=ps.name=="FormationFilings"','bool forming=ps.name.StartsWith("Formation",StringComparison.Ordinal)')
(S/'App/SpellVFX120/StoneDokkaebiVfx.cs').write_text(runtime)
build=rename((S/'Editor/SpellVFX120/MetalTigerBuild.cs').read_text()).replace('tris<12414','tris<1000')
build=build.replace('new Vector3[4]','new Vector3[2]').replace('new int[4]','new int[2]').replace('c<4','c<2').replace('i<4','i<2').replace('Enumerable.Range(0,4)','Enumerable.Range(0,2)').replace('four planted paw positions','two planted foot positions')
build=build.replace('"M_BrushedSilver"','"M_WeatheredGranite"').replace('Pattern(Folder,3,true)','Pattern(Folder,2,true)')
start=build.index('    KtpOffsetGuardBuild.Debris(');end=build.index('    p.StoneDokkaebiDebris=',start)
build=build[:start]+'''    KtpOffsetGuardBuild.Debris(debris,Folder,"FormationPebbles",28,new Color(.43f,.35f,.25f),0,.75f,new Vector3(.07f,.085f,.06f),-.15f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"DissolveStones",42,new Color(.46f,.39f,.29f),0,.65f,new Vector3(.08f,.1f,.07f),.65f);
    var stone=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/DebrisShard.asset");stone.Clear();stone.vertices=new[]{new Vector3(-.5f,-.4f,-.4f),new Vector3(.4f,-.5f,-.5f),new Vector3(.5f,.4f,-.4f),new Vector3(-.4f,.5f,-.5f),new Vector3(-.4f,-.5f,.5f),new Vector3(.5f,-.4f,.4f),new Vector3(.4f,.5f,.5f),new Vector3(-.5f,.4f,.4f)};stone.triangles=new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};stone.RecalculateNormals();stone.RecalculateBounds();EditorUtility.SetDirty(stone);AssetDatabase.SaveAssetIfDirty(stone);
    foreach(var ps in debris.GetComponentsInChildren<ParticleSystem>())
    {
     var main=ps.main;main.startSpeed=new ParticleSystem.MinMaxCurve(.3f,.65f);main.playOnAwake=false;
     var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(1.3f,1.3f,.9f);shape.position=new Vector3(0,.85f,0);
     if(ps.name=="FormationPebbles"){shape.position=new Vector3(0,.06f,0);shape.scale=new Vector3(1.5f,.08f,1.2f);}
    }
    foreach(string name in new[]{"FormationDust","DissolveDust"})
    {
     var go=new GameObject(name);go.transform.SetParent(debris.transform,false);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
     var main=ps.main;main.playOnAwake=false;main.loop=false;main.duration=.7f;main.startLifetime=.6f;main.startSpeed=new ParticleSystem.MinMaxCurve(.15f,.35f);main.startSize=new ParticleSystem.MinMaxCurve(.22f,.4f);main.startColor=new Color(.47f,.38f,.26f,.3f);main.maxParticles=18;
     var em=ps.emission;em.rateOverTime=0;em.SetBursts(new[]{new ParticleSystem.Burst(0,18)});var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(1.4f,.12f,1f);shape.position=new Vector3(0,.08f,0);
     var colors=ps.colorOverLifetime;colors.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.5f,.15f),new GradientAlphaKey(0,1)});colors.color=g;
     ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/FireBolt/M_Smoke.mat");
    }
''' + build[end:]
(S/'Editor/SpellVFX120/StoneDokkaebiBuild.cs').write_text(build)
review=rename((S/'Editor/SpellVFX120/MetalTigerReview.cs').read_text())
(S/'Editor/SpellVFX120/StoneDokkaebiReview.cs').write_text(review)
shader=rename((R/'Oheangbu/Assets/_Project/Shaders/MetalTiger.shader').read_text()).replace('Silver','Granite')
start=shader.index('    float3 view=');end=shader.index('    return half4(',start)
shader=shader[:start]+'''    color+=float3(.20,.14,.075)*edge*.15;
''' +shader[end:]
(R/'Oheangbu/Assets/_Project/Shaders/StoneDokkaebi.shader').write_text(shader)
print('STONE_DOKKAEBI_SCAFFOLDED')
