using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 edit-mode reference renders (SPEC-REGION-SKY-308 AC-S14, SPEC-EVENT-WASH-308 AC-W1). (B0 note: the first version used only
 // pre-#308 APIs so it could record the "pre" reference before the runtime change; since step 2 it uses the #308 driver API
 // SetSkyMaterialOverride / RegionalSkyInstance / SkyMaterial and deploys with that driver.) Queue:
 //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.RegionSky308Capture Run "render-ref:pre308"
 //   render-ref:<label>[:stations=S07,S11|rep|all][:scene=main|arch296|folk298][:res=1920x1080][:sky=scene|1|2|<material path>]
 //       a temporary camera (copy of the scene's player camera + its URP data) at each station, pose a = toward the capital
 //       centroid, b = opposite, pitch +10 deg, eye = ground + stations.json eye; the regional sky is previewed at the station.
 //       The rendered sky is always a transient copy of the driver's sky material (whatever _skyboxMaterial is, or the sky=
 //       override): the driver's own stepped copy, or — when the driver is not stepping a regional sky — a copy made here; that
 //       copy gets _CloudSpeed 0 (never an asset). sky=2 previews RealmInkSky308.mat before L4 (in-memory override, nothing
 //       serialized); sky=1 the step-1 material (L4 "before"); sky=scene (default) whatever the scene has.
 //       Writes Art/World/RegionSky308/Renders/<label>/<station>_<pose>.png + render.json (sky source/shader/mode and per-shot
 //       sky values). Nothing is saved to the scene; the driver re-applies its own view afterwards.
 //   render-compare:<labelA>:<labelB>  per image: differing pixels, max 8-bit channel delta (AC-S14 / AC-W1 need 0).
 //   stations   lists stations.json.
 // Time-driven shaders other than the sky (grass wind) are not frozen: render each label twice and compare a label to itself
 // first to learn the noise floor (Spec: then compare the sky/far region only).
 public static class RegionSky308Capture
 {
  internal const string StationsRel="Art/World/RegionSky308/stations.json";
  [Serializable] internal sealed class Station{public string id="",name="",realm="",note="";public float x,z;public float min=-1;public bool interior;}
  [Serializable] internal sealed class StationFile{public string version="";public float eye=1.65f,pitch=10f;public Station[] stations=new Station[0];public string[] representative=new string[0];public string[] reference=new string[0];}
  [Serializable] sealed class Shot{public string station,pose,file;public Vector3 position;public Vector3 euler;public string sky="";}
  [Serializable] sealed class ShotFile{public string label,scene,utc,skyMaterial,profile;public int width,height;public string skySource="",skyShader="",skyMode="",skyOption="";public List<Shot> shots=new List<Shot>();}
  // sky values recorded per shot (when the material declares them)
  static readonly string[] SkyFloats={"_CloudSpeed","_CloudDensity","_Coverage","_Softness","_Stretch","_BandYaw","_StormYaw","_StormGain","_WetEdge","_CoreInk","_MistHeight","_ZenithFade","_CapitalAzimuth","_CapitalAtmosphere","_Octaves"};
  static readonly string[] SkyColors={"_Horizon","_Zenith","_Cloud","_CloudInk","_FogTint"};

  public static string Run(string command)
  {
   command=(command??"").Trim();
   try
   {
    if(command=="stations")return ListStations();
    if(command.StartsWith("render-ref:",StringComparison.Ordinal))return RenderRef(command.Substring(11));
    if(command.StartsWith("render-compare:",StringComparison.Ordinal))return Compare(command.Substring(15));
   }
   catch(PostLedger308.Refused r){return "refused: "+r.Message;}
   catch(Exception e){return "FAILED "+e;}   // same as the neighbours (MineBoss306): never an unhandled throw into the queue
   return "refused: unknown command '"+command+"' (render-ref:<label>[:stations=..][:scene=..][:res=WxH] | render-compare:<a>:<b> | stations)";
  }

  internal static StationFile Stations()
  {
   string path=PostLedger308.RepoPath(StationsRel);
   if(!File.Exists(path))throw new PostLedger308.Refused("stations.json missing at "+path);
   var f=JsonUtility.FromJson<StationFile>(File.ReadAllText(path));
   if(f?.stations==null||f.stations.Length==0)throw new PostLedger308.Refused("stations.json has no stations");
   return f;
  }

  static string ListStations()
  {
   var f=Stations();var sb=new StringBuilder("stations "+f.version+" eye "+f.eye+" pitch "+f.pitch+"\n");
   foreach(var s in f.stations)sb.AppendLine("  "+s.id+" "+s.name+" ("+s.x+", "+s.z+") "+s.realm+(s.min>=0?" min "+s.min:"")+(s.interior?" interior":""));
   return sb.ToString();
  }

  internal static Station[] Pick(StationFile f,string token)
  {
   if(string.IsNullOrEmpty(token)||token=="ref")token=f.reference!=null&&f.reference.Length>0?string.Join(",",f.reference):"S07,S11";
   if(token=="all")return f.stations;
   if(token=="rep")token=string.Join(",",f.representative??new string[0]);
   var ids=token.Split(',').Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray();
   var picked=ids.Select(id=>f.stations.FirstOrDefault(s=>s.id==id)).ToArray();
   if(picked.Any(s=>s==null))throw new PostLedger308.Refused("unknown station in '"+token+"'");
   return picked;
  }

  /// <summary>Area centroid of the capital realm in the geography sheet (pre-#308 safe copy of RegionalInkSkyProfile.Centroid).</summary>
  internal static bool CapitalCentroid(WorldMacroSheetSO sheet,out Vector2 c)
  {
   c=Vector2.zero;if(sheet?.Regions==null)return false;double area=0,cx=0,cz=0;
   foreach(var r in sheet.Regions)
   {
    if(r==null||r.Realm!=RealmId.Hwanggyeong||r.Polygon==null||r.Polygon.Length<3)continue;var p=r.Polygon;
    for(int i=0,j=p.Length-1;i<p.Length;j=i++){double cross=(double)p[j].x*p[i].y-(double)p[i].x*p[j].y;area+=cross;cx+=(p[j].x+p[i].x)*cross;cz+=(p[j].y+p[i].y)*cross;}
   }
   if(Math.Abs(area)<1e-6)return false;c=new Vector2((float)(cx/(3*area)),(float)(cz/(3*area)));return true;
  }

  internal static float Ground(float x,float z)
  {
   Physics.SyncTransforms();
   var hits=Physics.RaycastAll(new Vector3(x,3000f,z),Vector3.down,6000f,~0,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.3f&&!(h.collider is CharacterController)).OrderByDescending(h=>h.point.y).ToArray();
   if(hits.Length==0)return 0f;
   // a roof or a crown over the station is not the standing ground: prefer the terrain when the top hit is > 2.5 m above it
   var terrain=hits.FirstOrDefault(x=>x.collider is TerrainCollider);
   return terrain.collider!=null&&hits[0].point.y-terrain.point.y>2.5f?terrain.point.y:hits[0].point.y;
  }

  /// <summary>Top-level serialized copy of one component onto another of the same type (owner and script kept).</summary>
  internal static void CopyComponent(Component from,Component to)
  {
   if(from==null||to==null)return;
   var s=new SerializedObject(from);var d=new SerializedObject(to);var it=s.GetIterator();bool enter=true;
   while(it.NextVisible(enter)){enter=false;if(it.name=="m_Script"||it.name=="m_GameObject")continue;d.CopyFromSerializedProperty(it);}
   d.ApplyModifiedPropertiesWithoutUndo();
  }

  static string RenderRef(string arg)
  {
   PostLedger308.RequireEditable();
   var parts=arg.Split(':');string label=parts[0].Trim();if(label.Length==0||label.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new PostLedger308.Refused("bad label '"+label+"'");
   var o=PostLedger308.Options(parts.Skip(1));
   var file=Stations();var stations=Pick(file,o.TryGetValue("stations",out var st)?st:null);
   string scenePath=o.TryGetValue("scene",out var sc)&&sc.Length>0?PostLedger308.Select(o)[0]:PostLedger308.Main;
   var scene=PostLedger308.Open(scenePath);
   int w=1920,h=1080;if(o.TryGetValue("res",out var res)&&res.Contains("x")){var wh=res.Split('x');w=int.Parse(wh[0],CultureInfo.InvariantCulture);h=int.Parse(wh[1],CultureInfo.InvariantCulture);}
   var driver=PostLedger308.All<WorldLookDriver>(scene).FirstOrDefault(d=>d.isActiveAndEnabled)??throw new PostLedger308.Refused("no active WorldLookDriver in "+scenePath);
   var ds=new SerializedObject(driver);
   var source=ds.FindProperty("_skyCamera").objectReferenceValue as Camera;
   if(source==null)source=PostLedger308.All<Camera>(scene).FirstOrDefault(c=>c.CompareTag("MainCamera"))??PostLedger308.All<Camera>(scene).FirstOrDefault(c=>c.enabled);
   if(source==null)throw new PostLedger308.Refused("no player camera in "+scenePath);
   var sheet=ds.FindProperty("_skyGeography").objectReferenceValue as WorldMacroSheetSO;
   bool hasCentroid=CapitalCentroid(sheet,out var centroid);
   // sky source: the scene's (default) or an in-memory override (never serialized)
   string skyOption=o.TryGetValue("sky",out var so)?so.Trim():"";
   Material overrideSky=null;
   if(skyOption=="2"||skyOption=="step2")
   {
    if(RegionSky308.Sky2Shader(out string why)==null)throw new PostLedger308.Refused(why);
    overrideSky=AssetDatabase.LoadAssetAtPath<Material>(RegionSky308.Sky2MatPath)??throw new PostLedger308.Refused("RealmInkSky308.mat missing: run RegionSky308 data:apply:step=2 first");
   }
   else if(skyOption=="1"||skyOption=="step1")
   {
    var current=ds.FindProperty("_skyboxMaterial").objectReferenceValue as Material;
    overrideSky=RegionSky308.Step1SkyMaterial(scenePath,current)??throw new PostLedger308.Refused("no step-1 sky known for "+scenePath+" (L4 ledger 'before' missing)");
    if(overrideSky==current)overrideSky=null;
   }
   else if(skyOption.Length>0&&skyOption!="scene")
   {
    overrideSky=AssetDatabase.LoadAssetAtPath<Material>(skyOption)??throw new PostLedger308.Refused("sky material not found: "+skyOption+" (sky=scene|1|2|<Assets/... .mat>)");
   }
   string outDir=PostLedger308.RepoPath("Art/World/RegionSky308/Renders/"+label);Directory.CreateDirectory(outDir);
   var shots=new ShotFile{label=label,scene=scenePath,utc=PostLedger308.Utc(),width=w,height=h,profile=PostLedger308.AssetRef(ds.FindProperty("_regionalSkyProfile").objectReferenceValue),skyOption=skyOption.Length>0?skyOption:"scene"};
   var priorOverride=driver.SkyMaterialOverride;var priorSkybox=RenderSettings.skybox;
   Material ownCopy=null,ownSource=null;
   var go=new GameObject("~RegionSky308Ref"){hideFlags=HideFlags.HideAndDontSave};
   var rt=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=1};
   var tex=new Texture2D(w,h,TextureFormat.RGB24,false);
   bool async=ShaderUtil.allowAsyncCompilation;
   var sb=new StringBuilder("render-ref "+label+" ("+scenePath+")\n");
   try
   {
    ShaderUtil.allowAsyncCompilation=false;
    var cam=go.AddComponent<Camera>();cam.CopyFrom(source);cam.enabled=false;
    var srcData=source.GetComponent<UniversalAdditionalCameraData>();var dstData=cam.GetUniversalAdditionalCameraData();
    CopyComponent(srcData,dstData);
    cam.targetTexture=rt;cam.aspect=(float)w/h;
    rt.Create();
    if(overrideSky!=null)driver.SetSkyMaterialOverride(overrideSky);
    shots.skySource=PostLedger308.AssetRef(driver.SkyMaterial);
    shots.skyShader=driver.SkyMaterial!=null&&driver.SkyMaterial.shader!=null?driver.SkyMaterial.shader.name:"";
    foreach(var s in stations)
    {
     float y=Ground(s.x,s.z)+file.eye;var pos=new Vector3(s.x,y,s.z);
     float az=hasCentroid?Mathf.Atan2(centroid.x-s.x,centroid.y-s.z)*Mathf.Rad2Deg:0f;
     foreach(var pose in new[]{"a","b"})
     {
      var euler=new Vector3(-file.pitch,pose=="a"?az:az+180f,0);
      go.transform.SetPositionAndRotation(pos,Quaternion.Euler(euler));
      driver.PreviewRegionalSky(pos);
      var sky=RenderSettings.skybox;
      if(sky==null)throw new PostLedger308.Refused("RenderSettings.skybox is null after the preview (driver _useSkybox off or no sky material)");
      string mode;
      if(EditorUtility.IsPersistent(sky))
      {
       // the driver is not stepping a regional sky: render a transient copy of that material instead (never freeze an asset)
       if(ownCopy==null||ownSource!=sky){if(ownCopy!=null)Object.DestroyImmediate(ownCopy);ownSource=sky;ownCopy=new Material(sky){name="~RegionSky308Ref sky (transient)",hideFlags=HideFlags.HideAndDontSave};}
       RenderSettings.skybox=ownCopy;sky=ownCopy;mode="capture-copy of "+PostLedger308.AssetRef(ownSource);
      }
      else mode=sky==driver.RegionalSkyInstance?"driver-transient":"transient (not the driver's)";
      shots.skyMaterial=sky.name;shots.skyMode=mode;
      // freeze the cloud drift on the transient copy only (the driver's next write restores its own speed)
      if(sky.HasProperty("_CloudSpeed"))sky.SetFloat("_CloudSpeed",0f);
      var request=new RenderPipeline.StandardRequest{destination=rt};
      if(RenderPipeline.SupportsRenderRequest(cam,request))RenderPipeline.SubmitRenderRequest(cam,request);else cam.Render();
      var prev=RenderTexture.active;RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();RenderTexture.active=prev;
      string name=s.id+"_"+pose+".png";File.WriteAllBytes(Path.Combine(outDir,name),tex.EncodeToPNG());
      shots.shots.Add(new Shot{station=s.id,pose=pose,file=name,position=pos,euler=euler,sky=SkyValues(sky)});
      sb.AppendLine("  "+s.id+"_"+pose+" at "+pos.ToString("F1")+" yaw "+euler.y.ToString("F1"));
     }
    }
   }
   finally
   {
    ShaderUtil.allowAsyncCompilation=async;
    if(go!=null)Object.DestroyImmediate(go);
    rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);
    if(driver.SkyMaterialOverride!=priorOverride)driver.SetSkyMaterialOverride(priorOverride);
    driver.Apply();   // back to the driver's own edit-mode view (its next Apply rewrites the cloud speed)
    if(ownCopy!=null){if(RenderSettings.skybox==ownCopy)RenderSettings.skybox=priorSkybox;Object.DestroyImmediate(ownCopy);}
   }
   File.WriteAllText(Path.Combine(outDir,"render.json"),JsonUtility.ToJson(shots,true));
   return sb.Append("  -> "+outDir).ToString();
  }

  static string SkyValues(Material m)
  {
   var parts=new List<string>();
   foreach(var p in SkyFloats)if(m.HasProperty(p))parts.Add(p+"="+m.GetFloat(p).ToString("0.####",CultureInfo.InvariantCulture));
   foreach(var p in SkyColors)if(m.HasProperty(p))parts.Add(p+"=#"+ColorUtility.ToHtmlStringRGB(m.GetColor(p)));
   return string.Join(" ",parts);
  }

  static string Compare(string arg)
  {
   var ab=arg.Split(':');if(ab.Length<2)throw new PostLedger308.Refused("render-compare:<labelA>:<labelB>");
   string a=PostLedger308.RepoPath("Art/World/RegionSky308/Renders/"+ab[0].Trim()),b=PostLedger308.RepoPath("Art/World/RegionSky308/Renders/"+ab[1].Trim());
   if(!Directory.Exists(a)||!Directory.Exists(b))throw new PostLedger308.Refused("missing render folder "+(Directory.Exists(a)?b:a));
   var sb=new StringBuilder("render-compare "+ab[0]+" vs "+ab[1]+"\n");int files=0,identical=0;
   foreach(var fa in Directory.GetFiles(a,"*.png").OrderBy(x=>x))
   {
    string fb=Path.Combine(b,Path.GetFileName(fa));if(!File.Exists(fb)){sb.AppendLine("  "+Path.GetFileName(fa)+": missing in "+ab[1]);continue;}
    var ta=new Texture2D(2,2,TextureFormat.RGB24,false);var tb=new Texture2D(2,2,TextureFormat.RGB24,false);
    try
    {
     ta.LoadImage(File.ReadAllBytes(fa));tb.LoadImage(File.ReadAllBytes(fb));
     if(ta.width!=tb.width||ta.height!=tb.height){sb.AppendLine("  "+Path.GetFileName(fa)+": size differs");continue;}
     var pa=ta.GetPixels32();var pb=tb.GetPixels32();int diff=0,max=0,topDiff=0;int topRows=ta.height/10;
     for(int i=0;i<pa.Length;i++)
     {
      int d=Mathf.Max(Mathf.Abs(pa[i].r-pb[i].r),Mathf.Max(Mathf.Abs(pa[i].g-pb[i].g),Mathf.Abs(pa[i].b-pb[i].b)));
      if(d>0){diff++;if(i/ta.width>=ta.height-topRows)topDiff++;}if(d>max)max=d;
     }
     files++;if(diff==0)identical++;
     sb.AppendLine("  "+Path.GetFileName(fa)+": differing px "+diff+" / "+pa.Length+" (top 10% "+topDiff+"), max delta "+max+(diff==0?" IDENTICAL":""));
    }
    finally{Object.DestroyImmediate(ta);Object.DestroyImmediate(tb);}
   }
   return sb.Append("  "+identical+"/"+files+" identical").ToString();
  }
 }
}
