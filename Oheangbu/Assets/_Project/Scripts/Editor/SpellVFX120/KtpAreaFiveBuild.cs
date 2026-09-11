using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpAreaFiveBuild
 {
  static string Folder=>Vfx120Editor.AssetRoot+"/AreaFive";
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||!new[]{"고","노","소","모","오"}.Contains(glyph))throw new Exception("Stopped editor and one area glyph required");
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"AreaFive");
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;
   string baseline=Folder+"/Baseline_"+glyph+".asset";
   if(AssetDatabase.LoadAssetAtPath<Vfx120Profile>(baseline)==null&&!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),baseline))throw new Exception("Baseline backup failed");
   int family=glyph=="고"?0:glyph=="노"?1:glyph=="모"?2:glyph=="소"?3:4;
   p.AreaRemake=glyph=="고"?Vfx120AreaRemake.BambooField:glyph=="노"?Vfx120AreaRemake.FlameCone:glyph=="소"?Vfx120AreaRemake.NeedleVolley:glyph=="모"?Vfx120AreaRemake.SandFront:Vfx120AreaRemake.WaterWave;
   p.KtpEmphasis=true;p.KtpBrightness=2.2f;p.UseOriginalKtp=true;p.AreaCastSeconds=.32f;
   p.NativeCastPrefab=KtpQuickCastBuild.Pattern(Vfx120Editor.AssetRoot+"/KtpEmphasis",family,false);
   p.CameraOffsetLaunch=glyph=="노"||glyph=="소";p.LaunchViewport=new Vector2(.6f,.4f);p.LaunchDepth=1.6f;
   p.KtpContactMultiplier=1.75f;p.KtpContactSurfaceOffset=.15f;p.AreaContactPrefab=Contact(family);
   p.NativeBodyPrefab=Body(p,glyph);p.NativeBodyMotion=Vfx120NativeBodyMotion.None;
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
   var cp=Resources.Load<KtpContactProfile>(KtpContactProfile.ResourcePath);
   cp.SpellProfiles=cp.SpellProfiles.Where(x=>x!=null&&x.Glyph!=glyph).Append(p).ToArray();EditorUtility.SetDirty(cp);AssetDatabase.SaveAssetIfDirty(cp);
   string output=Path.Combine(Vfx120Editor.Output,"AreaFive",glyph);Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"settings.json"),EditorJsonUtility.ToJson(p,true));
   return "AREA_FIVE_BUILT "+glyph;
  }
  static GameObject Contact(int family)
  {
   string path=Folder+"/AreaContact_"+family+".prefab";var old=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(old!=null)return old;
   var host=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/KtpEmphasis/SmallPatternContact_"+family+".prefab"));
   try
   {
    foreach(var ps in host.GetComponentsInChildren<ParticleSystem>())
    {
     ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.startLifetime=ps.name.Contains("Pattern")?.25f:.45f;main.duration=.25f;
     var em=ps.emission;for(int i=0;i<em.burstCount;i++){var burst=em.GetBurst(i);burst.count=new ParticleSystem.MinMaxCurve(Mathf.Max(1,Mathf.CeilToInt(burst.count.constantMax*.5f)));em.SetBurst(i,burst);}
    }
    return PrefabUtility.SaveAsPrefabAsset(host,path);
   }finally{Object.DestroyImmediate(host);}
  }
  static GameObject Body(Vfx120Profile p,string glyph)
  {
   string path=Folder+"/Body_"+glyph+".prefab";var old=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(old!=null)return old;
   var host=new GameObject("AreaBody_"+glyph);
   try
   {
    if(glyph=="고")
    {
     var material=new Material(p.BodyMaterial);material.SetFloat("_DissolveHeight",2.2f);AssetDatabase.CreateAsset(material,Folder+"/Bamboo.mat");
     MeshObject(host,Spike(),material,"Bamboo");
    }
    else if(glyph=="소")
    {
     var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.65f,.7f,.72f));material.SetFloat("_Metallic",.85f);material.SetFloat("_Smoothness",.8f);AssetDatabase.CreateAsset(material,Folder+"/Needle.mat");
     MeshObject(host,Needle(),material,"Needle");
     var line=host.AddComponent<LineRenderer>();line.useWorldSpace=false;line.positionCount=2;line.SetPositions(new[]{new Vector3(0,0,-.65f),new Vector3(0,0,-1.45f)});line.startWidth=.018f;line.endWidth=0;line.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/KtpEmphasis/DebrisUnlit.mat");line.startColor=new Color(.65f,.8f,1,.6f);line.endColor=Color.clear;
    }
    else if(glyph=="노")
    {
     var fire=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/FireBolt/M_Flame.mat");var smoke=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/FireBolt/M_Smoke.mat");
     Particles(host,"FlameCore",fire,140,450,.24f,.48f,new Color(1,.65f,.25f),true);
     Particles(host,"ThinSmoke",smoke,20,35,.3f,.65f,new Color(.22f,.2f,.18f,.22f),false);
    }
    else if(glyph=="모")
    {
     var mat=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/FireBolt/M_Smoke.mat");
     Particles(host,"SandRoll",mat,90,110,.45f,.8f,new Color(.5f,.36f,.19f,.7f),false);
     KtpOffsetGuardBuild.Debris(host,Folder,"Grains",26,new Color(.46f,.33f,.18f),0,.5f,new Vector3(.04f,.04f,.04f),.35f);
     var grains=host.transform.Find("Grains").GetComponent<ParticleSystem>();var main=grains.main;main.loop=true;main.playOnAwake=false;var em=grains.emission;em.SetBursts(Array.Empty<ParticleSystem.Burst>());em.rateOverTime=45;
    }
    else
    {
     var shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/AreaWater.shadergraph");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Copied PolyOne water shader missing or failed");
     var material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/PolyOne/Water URP/Materials/M_Shader Water.mat"));material.shader=shader;
     material.SetFloat("_Amplitude_Wave",0);material.SetFloat("_Speed_Wave",.7f);material.SetFloat("_Frequency_Wave",6);material.SetFloat("_Scale_Noise",9);material.SetFloat("_Speed_Noise",.45f);material.SetFloat("_Max_Depth",2);material.SetFloat("_Refration_Intensity",.015f);
     material.SetColor("_Deep_Water_Color",new Color(.055f,.19f,.25f,1));material.SetColor("_Shallow_Water_Color",new Color(.23f,.5f,.56f,1));
     AssetDatabase.CreateAsset(material,Folder+"/PolyOneWave.mat");MeshObject(host,WaterGrid(),material,"Water");
     var foam=Particles(host,"Foam",AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/FireBolt/M_Smoke.mat"),64,80,.35f,.12f,new Color(.74f,.85f,.84f,.8f),false);
     var shape=foam.shape;shape.scale=new Vector3(.9f,.04f,.15f);
    }
    return PrefabUtility.SaveAsPrefabAsset(host,path);
   }finally{Object.DestroyImmediate(host);}
  }
  static ParticleSystem Particles(GameObject parent,string name,Material mat,int cap,float rate,float life,float size,Color tint,bool fire)
  {
   var go=new GameObject(name);go.transform.SetParent(parent.transform,false);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
   var main=ps.main;main.playOnAwake=false;main.loop=true;main.duration=5;main.maxParticles=cap;main.startLifetime=new ParticleSystem.MinMaxCurve(life*.75f,life);main.startSpeed=.15f;main.startSize=new ParticleSystem.MinMaxCurve(size*.65f,size);main.startColor=tint;main.scalingMode=ParticleSystemScalingMode.Hierarchy;main.simulationSpace=ParticleSystemSimulationSpace.World;
   var emission=ps.emission;emission.rateOverTime=rate;var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(.95f,.4f,.45f);
   var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.85f,.15f),new GradientAlphaKey(0,1)});var color=ps.colorOverLifetime;color.enabled=true;color.color=gradient;
   if(fire){var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=8;sheet.numTilesY=1;sheet.frameOverTime=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,0,1,1));}
   var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=mat;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.sortMode=ParticleSystemSortMode.Distance;
   return ps;
  }
  static void MeshObject(GameObject host,Mesh mesh,Material mat,string name)
  {
   string path=Folder+"/"+name+".asset";AssetDatabase.CreateAsset(mesh,path);host.AddComponent<MeshFilter>().sharedMesh=mesh;host.AddComponent<MeshRenderer>().sharedMaterial=mat;
  }
  static Mesh Spike()
  {
   const int segments=16;float[] levels={0,.36f,.4f,.44f,.88f,.92f,.96f,1.45f};var v=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
   for(int ring=0;ring<levels.Length+2;ring++)for(int j=0;j<=segments;j++)
   {
    float a=j*Mathf.PI*2/segments;float radius=ring>=8?.065f: ring==1||ring==2||ring==4||ring==5?.115f:.095f;
    float y=ring<8?levels[ring]:ring==8?1.45f:1.25f;if(ring==7||ring==8)y+=Mathf.Cos(a)*.25f;
    v.Add(new Vector3(Mathf.Cos(a)*radius,y,Mathf.Sin(a)*radius));uv.Add(new Vector2(j/(float)segments,y));
   }
   for(int r=0;r<9;r++)for(int j=0;j<segments;j++){int a=r*(segments+1)+j,b=a+segments+1;triangles.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
   var mesh=MeshOf(v,uv,triangles);var colors=new Color[v.Count];for(int i=0;i<colors.Length;i++)colors[i]=i/(segments+1)>=7?Color.red:Color.black;mesh.colors=colors;return mesh;
  }
  static Mesh Needle()
  {
   var v=new List<Vector3>{new Vector3(0,0,.75f),new Vector3(.035f,0,-.3f),new Vector3(0,.035f,-.3f),new Vector3(-.035f,0,-.3f),new Vector3(0,-.035f,-.3f),new Vector3(0,0,-.45f)};
   return MeshOf(v,v.Select(x=>new Vector2(x.x,x.z)).ToList(),new List<int>{0,1,2,0,2,3,0,3,4,0,4,1,5,2,1,5,3,2,5,4,3,5,1,4});
  }
  static Mesh WaterGrid()
  {
   var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();for(int z=0;z<=16;z++)for(int x=0;x<=32;x++){v.Add(new Vector3(x/16f-1,0,-z/16f*2.6f));uv.Add(new Vector2(x/32f,z/16f));}
   for(int z=0;z<16;z++)for(int x=0;x<32;x++){int a=z*33+x;t.AddRange(new[]{a,a+1,a+33,a+1,a+34,a+33});}return MeshOf(v,uv,t);
  }
  public static string RepairBambooSurface(){var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Bamboo.asset");var fresh=Spike();EditorUtility.CopySerialized(fresh,mesh);Object.DestroyImmediate(fresh);EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);return "Bamboo tangents and cut surface colors repaired";}
  static Mesh MeshOf(List<Vector3> v,List<Vector2> uv,List<int> t){var mesh=new Mesh();mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;}
 }
}
