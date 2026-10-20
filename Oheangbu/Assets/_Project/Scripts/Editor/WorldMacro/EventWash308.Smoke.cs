using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 봉수 연기 (SPEC-FIELD-FIRST-HOUR-308 §2 A1, D308-117, TEST): the far guide comes back as a column of ink smoke, never as
 // light. D308-6 stays: the halo / light / core objects remain off and wash308-beacons:revert is never used for this.
 //   wash308-smoke[:dry][:scene=main|arch296|folk298]   adds Smoke308 (one ParticleSystem) on top of every Finish297_Attraction/
 //        Beacon_* (default scene = main, the play target). Material M_BeaconSmoke308 = URP Particles/Unlit, alpha blend, ink grey,
 //        no emission, no light marker; puff texture T_InkPuff308 generated once. Ledger kind "smoke" (created objects only).
 //   wash308-smoke:revert[:scene=..]   deletes the recorded Smoke308 objects (material and texture assets are kept).
 // There is no day/night cycle in the game today, so the night ember of A1 is not built (Spec §8).
 public static partial class EventWash308
 {
  internal const string SmokeName="Smoke308";
  internal const string SmokeMatPath="Assets/_Project/Art/World/Finish297/Attraction/Materials/M_BeaconSmoke308.mat";
  internal const string PuffTexPath="Assets/_Project/Art/World/Finish297/Attraction/Textures/T_InkPuff308.png";
  // TEST values (Spec A1: column 40 – 60 m, leaning slowly in the wind, ink not light)
  const float SmokeRise=3.4f,SmokeLife=15f,SmokeRate=2.6f,SmokeWind=1.1f,SmokeSize0=5f,SmokeSize1=16f;
  static readonly Color SmokeInk=new Color(.16f,.155f,.15f,.5f);

  static string[] SmokeScenes(Dictionary<string,string> o)=>o!=null&&o.ContainsKey("scene")?PostLedger308.Select(o):new[]{PostLedger308.Main};

  /// <summary>Soft ink puff (64 px): a radial falloff broken by value noise so the edge reads as a brush blot, not a disc.</summary>
  internal static Texture2D InkPuff()
  {
   var t=AssetDatabase.LoadAssetAtPath<Texture2D>(PuffTexPath);if(t!=null)return t;
   const int N=64;var tex=new Texture2D(N,N,TextureFormat.RGBA32,false){name="T_InkPuff308"};var px=new Color32[N*N];
   for(int y=0;y<N;y++)for(int x=0;x<N;x++)
   {
    float u=(x+.5f)/N*2f-1f,v=(y+.5f)/N*2f-1f,r=Mathf.Sqrt(u*u+v*v);
    float n=Mathf.PerlinNoise(x*.11f+3.1f,y*.11f+7.3f)*.6f+Mathf.PerlinNoise(x*.29f+11f,y*.29f+5f)*.4f;
    float a=Mathf.Clamp01((1f-r)*1.6f)*Mathf.Lerp(.55f,1f,n);a=a*a*(3f-2f*a);
    px[y*N+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(a*255f));
   }
   tex.SetPixels32(px);tex.Apply();
   EnsureFolder(Path.GetDirectoryName(PuffTexPath).Replace('\\','/'));
   File.WriteAllBytes(PostLedger308.Abs(PuffTexPath),tex.EncodeToPNG());Object.DestroyImmediate(tex);
   AssetDatabase.ImportAsset(PuffTexPath,ImportAssetOptions.ForceSynchronousImport);
   if(AssetImporter.GetAtPath(PuffTexPath) is TextureImporter imp){imp.alphaIsTransparency=true;imp.wrapMode=TextureWrapMode.Clamp;imp.mipmapEnabled=true;imp.sRGBTexture=true;imp.SaveAndReimport();}
   return AssetDatabase.LoadAssetAtPath<Texture2D>(PuffTexPath);
  }

  internal static Material SmokeMaterial()
  {
   var m=AssetDatabase.LoadAssetAtPath<Material>(SmokeMatPath);if(m!=null)return m;
   var sh=Shader.Find("Universal Render Pipeline/Particles/Unlit")??throw new PostLedger308.Refused("URP Particles/Unlit shader missing");
   m=new Material(sh){name="M_BeaconSmoke308"};
   m.SetTexture("_BaseMap",InkPuff());m.SetColor("_BaseColor",Color.white);
   // transparent, alpha blend (the URP particle material keywords the inspector would set)
   m.SetFloat("_Surface",1f);m.SetFloat("_Blend",0f);m.SetFloat("_ZWrite",0f);m.SetFloat("_Cull",0f);
   m.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
   m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Transparent;
   m.SetOverrideTag("RenderType","Transparent");
   m.SetColor("_EmissionColor",Color.black);m.DisableKeyword("_EMISSION");m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;
   EnsureFolder(Path.GetDirectoryName(SmokeMatPath).Replace('\\','/'));
   AssetDatabase.CreateAsset(m,SmokeMatPath);AssetDatabase.SaveAssetIfDirty(m);
   return m;
  }

  /// <summary>Top of the beacon stone (LOD0 bounds when present, else the beacon position).</summary>
  static Vector3 BeaconTop(Transform beacon)
  {
   var r=beacon.Find("LOD0")?.GetComponent<Renderer>()??beacon.GetComponentInChildren<Renderer>(true);
   return r!=null?new Vector3(r.bounds.center.x,r.bounds.max.y,r.bounds.center.z):beacon.position;
  }

  static void BuildSmoke(GameObject go,Material mat)=>BuildSmoke(go,mat,SmokeRise,SmokeLife,SmokeRate,SmokeWind,SmokeSize0,SmokeSize1,SmokeInk,64,.8f);
  // the column shape shared by the beacon smoke and the hearth smoke (EventWash308.Hearth.cs): rise m/s, life s, rate /s, wind m/s,
  // puff size at birth / at death, ink colour (alpha = density), particle cap, emitter radius
  internal static void BuildSmoke(GameObject go,Material mat,float rise,float life,float rate,float wind,float size0,float size1,Color ink,int cap,float radius)
  {
   var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
   var main=ps.main;main.duration=10f;main.loop=true;main.prewarm=true;main.playOnAwake=true;
   main.startLifetime=new ParticleSystem.MinMaxCurve(life*.85f,life);main.startSpeed=new ParticleSystem.MinMaxCurve(rise*.8f,rise);
   main.startSize=new ParticleSystem.MinMaxCurve(size0*.8f,size0);main.startRotation=new ParticleSystem.MinMaxCurve(0f,Mathf.PI*2f);
   main.startColor=ink;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=cap;main.scalingMode=ParticleSystemScalingMode.Local;
   var em=ps.emission;em.rateOverTime=rate;
   var sh=ps.shape;sh.shapeType=ParticleSystemShapeType.Cone;sh.angle=4f;sh.radius=radius;sh.rotation=new Vector3(-90f,0f,0f);
   // wind: a constant sideways push that grows with age (the column leans more as it rises); no Drag (the size x speed trap)
   var vel=ps.velocityOverLifetime;vel.enabled=true;vel.space=ParticleSystemSimulationSpace.World;
   vel.x=new ParticleSystem.MinMaxCurve(wind,AnimationCurve.Linear(0f,.1f,1f,1f));vel.y=new ParticleSystem.MinMaxCurve(0f);vel.z=new ParticleSystem.MinMaxCurve(wind*.35f,AnimationCurve.Linear(0f,.1f,1f,1f));
   var noise=ps.noise;noise.enabled=true;noise.strength=.6f;noise.frequency=.12f;noise.scrollSpeed=.15f;noise.quality=ParticleSystemNoiseQuality.Medium;
   var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(size1/size0,AnimationCurve.EaseInOut(0f,size0/size1,1f,1f));
   var col=ps.colorOverLifetime;col.enabled=true;
   var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0f),new GradientColorKey(new Color(.85f,.84f,.82f),1f)},
    new[]{new GradientAlphaKey(0f,0f),new GradientAlphaKey(1f,.08f),new GradientAlphaKey(.7f,.45f),new GradientAlphaKey(0f,1f)});
   col.color=g;
   var rot=ps.rotationOverLifetime;rot.enabled=true;rot.z=new ParticleSystem.MinMaxCurve(-.08f,.08f);
   var r=go.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=mat;r.renderMode=ParticleSystemRenderMode.Billboard;r.sortMode=ParticleSystemSortMode.Distance;
   r.minParticleSize=0f;r.maxParticleSize=4f;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;
   r.lightProbeUsage=UnityEngine.Rendering.LightProbeUsage.Off;r.reflectionProbeUsage=UnityEngine.Rendering.ReflectionProbeUsage.Off;
  }

  static string Smoke(Dictionary<string,string> o,bool dry)
  {
   PostLedger308.RequireEditable();
   var ledger=ReadLedger();string utc=PostLedger308.Utc();var sb=new StringBuilder("wash308-smoke"+(dry?" (dry)":"")+"\n");
   Material mat=dry?null:SmokeMaterial();
   if(!dry)sb.AppendLine("  material "+SmokeMatPath+" ("+mat.shader.name+", emission off, colour "+SmokeInk+")");
   foreach(var path in SmokeScenes(o))
   {
    var scene=PostLedger308.Open(path);sb.AppendLine(" "+PostLedger308.Short(path));
    var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==AttractionRoot);
    if(root==null){sb.AppendLine("  no "+AttractionRoot+" root (nothing to do)");continue;}
    var beacons=root.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Beacon_",StringComparison.Ordinal)).ToList();
    var todo=beacons.Where(b=>b.Find(SmokeName)==null).ToList();
    sb.AppendLine("  beacons "+beacons.Count+", without smoke "+todo.Count);
    foreach(var b in todo)sb.AppendLine("   "+b.name+" top "+BeaconTop(b).ToString("0.0"));
    if(dry||todo.Count==0)continue;
    string backup=PostLedger308.Backup(BackupRoot,utc,path);ledger.backups.Add(backup);
    foreach(var b in todo)
    {
     var go=new GameObject(SmokeName);go.transform.SetParent(b,false);go.transform.position=BeaconTop(b)+Vector3.up*.3f;go.transform.rotation=Quaternion.identity;
     go.isStatic=false;BuildSmoke(go,mat);EditorUtility.SetDirty(go);
     ledger.changes.Add(new Change{scene=path,kind="smoke",path=PostLedger308.PathOf(go.transform),before="",after="created",utc=utc});
    }
    PostLedger308.SaveScene(scene);WriteLedger(ledger);
    sb.AppendLine("  added "+todo.Count+"; saved; backup "+backup);
   }
   return sb.Append(" ledger "+LedgerPath).ToString();
  }

  static string SmokeRevert(Dictionary<string,string> o)
  {
   PostLedger308.RequireEditable();
   var ledger=ReadLedger();string utc=PostLedger308.Utc();var sb=new StringBuilder("wash308-smoke:revert\n");
   foreach(var path in SmokeScenes(o))
   {
    var mine=ledger.changes.Where(c=>c.scene==path&&c.kind=="smoke").ToList();
    if(mine.Count==0){sb.AppendLine(" "+PostLedger308.Short(path)+": nothing recorded");continue;}
    var scene=PostLedger308.Open(path);string backup=PostLedger308.Backup(BackupRoot,utc,path);ledger.backups.Add(backup);int done=0;
    foreach(var c in mine)
    {
     var t=PostLedger308.Find(scene,c.path);
     if(t!=null&&t.name==SmokeName&&t.GetComponent<ParticleSystem>()!=null){Object.DestroyImmediate(t.gameObject);done++;}
     else if(t!=null){sb.AppendLine("  not ours, kept: "+c.path);continue;}
     ledger.changes.Remove(c);
    }
    PostLedger308.SaveScene(scene);WriteLedger(ledger);
    sb.AppendLine(" "+PostLedger308.Short(path)+": removed "+done+"; backup "+backup);
   }
   return sb.ToString();
  }
 }
}
