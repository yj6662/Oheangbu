using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Spellcraft;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpAreaNaturalBuild
 {
  public const string Folder="Assets/_Project/Art/SpellVFX120/AreaNatural";
  static string Output=>Path.Combine(Vfx120Editor.Output,"AreaNatural");
  static Vfx120Profile Profile=>AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset").Entries.Single(x=>x.Glyph=="모").Profile;
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||glyph!="모")throw new Exception("Stopped editor and 모 required");
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"AreaNatural");
   var p=Profile;
   if(!File.Exists(Folder+"/Baseline_모.asset"))AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),Folder+"/Baseline_모.asset");
   if(!File.Exists(Folder+"/BaselineSpellBook.asset"))AssetDatabase.CopyAsset("Assets/_Project/Data/Configs/SpellBook_Proto.asset",Folder+"/BaselineSpellBook.asset");
   p.ProceduralEarthRift=true;EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
   Directory.CreateDirectory(Path.Combine(Output,glyph));File.WriteAllText(Path.Combine(Output,glyph,"settings.json"),EditorJsonUtility.ToJson(p,true));return "AREA_NATURAL_BUILT 모";
  }
  [Serializable] class Report{public string status;public List<string> checks=new List<string>(),errors=new List<string>();public float maxSurfaceError;public int groundQueries,vertices;public double maxBuildMilliseconds;}
  static void Need(Report r,bool condition,string name){if(!condition)throw new Exception(name);r.checks.Add(name);}
  static readonly Vector3 Origin=new Vector3(5000,0,5000);
  public static string Audit()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stopped editor required");
   var memory=(float)typeof(KtpAreaNaturalCapture).GetMethod("CommitRatio",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);
   if(memory>=.85f)throw new Exception("Commit >=85%; audit not started");
   var r=new Report();GameObject hill=null;Mesh hillMesh=null;Material material=null;
   try
   {
    Directory.CreateDirectory(Output);
    var randomState=UnityEngine.Random.state;
    for(int seed=1;seed<=100;seed++)
    {
     var a=new ProceduralRiftPath(seed,12,6);var b=new ProceduralRiftPath(seed,12,6);var other=new ProceduralRiftPath(seed+1,12,6);
     Need(r,a.Branches[0].End==b.Branches[0].End&&a.Branches[6].Phase==b.Branches[6].Phase,"seed replay "+seed);
     Need(r,a.Branches[0].End!=other.Branches[0].End,"different cast "+seed);
     Need(r,Mathf.Abs(a.Branches[1].End.y-a.Branches[2].End.y)>.00001f,"asymmetric fork "+seed);
     for(int lane=1;lane<7;lane++){int parent=lane<3?0:lane<5?1:2;Need(r,a.Branches[lane].Start==a.Branches[parent].End,"shared junction "+seed+"/"+lane);}
     for(int lane=0;lane<7;lane++)for(int i=0;i<=20;i++)if(Mathf.Abs(a.Branches[lane].At(Mathf.Lerp(a.Branches[lane].Start.y,a.Branches[lane].End.y,i/20f)).x)>6)throw new Exception("Out of corridor");
    }
    Need(r,JsonUtility.ToJson(randomState)==JsonUtility.ToJson(UnityEngine.Random.state),"Unity random state untouched");
    hillMesh=MakeHill();hill=new GameObject("NaturalRiftAuditHill");hill.layer=6;hill.transform.position=Origin;
    hill.AddComponent<MeshFilter>().sharedMesh=hillMesh;hill.AddComponent<MeshCollider>().sharedMesh=hillMesh;
    material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.32f,.37f,.29f));hill.AddComponent<MeshRenderer>().sharedMaterial=material;Physics.SyncTransforms();
    foreach(int fps in new[]{30,60,120})foreach(float speed in new[]{1f,.2f})
    {
     var host=new GameObject("NaturalRiftAudit");
     try
     {
      var fx=host.AddComponent<Vfx120Effect>();fx.Profile=Profile;fx.PreviewControlled=true;
      var plan=new AreaImpactPlan{Shape=AreaShape.Path,Point=Origin,Direction=Vector3.forward,Radius=6,Length=12,Speed=7,Delay=.4f,CreatedAt=Time.time,VisualSeed=fps/30};
      string snapshot=JsonUtility.ToJson(plan);fx.SetAreaPlan(plan);var watch=System.Diagnostics.Stopwatch.StartNew();fx.Begin(Origin+Vector3.up,null,Origin+Vector3.forward*12,Color.white);watch.Stop();
      r.maxBuildMilliseconds=Math.Max(r.maxBuildMilliseconds,watch.Elapsed.TotalMilliseconds);int queries=fx.RiftGroundQueries;
      Need(r,fx.RiftMissingGround==0,"hill all samples grounded "+fps+"/"+speed);
      for(float t=0;t<.7f;t+=speed/fps)fx.Sample(t);
      Need(r,fx.RiftVisibleBranches==1,"single trunk starts "+fps+"/"+speed);
      fx.Sample(2.2f);Need(r,fx.RiftVisibleBranches==7,"seven branch segments visible "+fps+"/"+speed);
      var filters=host.GetComponentsInChildren<MeshFilter>().Where(x=>x.name.StartsWith("ProceduralRift_")).ToArray();
      Need(r,filters.Length==7,"seven ground meshes "+fps+"/"+speed);
      int count=0;float max=0;
      foreach(var f in filters)foreach(var v in f.sharedMesh.vertices)
      {
       var world=f.transform.TransformPoint(v);NeedSurface(hill.GetComponent<MeshCollider>(),world,ref max);count++;
      }
      foreach(var f in filters)
      {
       var vertices=f.sharedMesh.vertices;var triangles=f.sharedMesh.triangles;
       for(int i=0;i<triangles.Length;i+=3)
        NeedSurface(hill.GetComponent<MeshCollider>(),f.transform.TransformPoint((vertices[triangles[i]]+vertices[triangles[i+1]]+vertices[triangles[i+2]])/3),ref max);
      }
      r.vertices=count;r.maxSurfaceError=Mathf.Max(r.maxSurfaceError,max);r.groundQueries=queries;
      Need(r,max<.02f,"hill ribbon edges within 2cm "+fps+"/"+speed+" error="+max);
      fx.Sample(1.8f);fx.Sample(2.2f);Need(r,fx.RiftGroundQueries==queries,"no frame-time ground queries "+fps+"/"+speed);
      Need(r,JsonUtility.ToJson(plan)==snapshot,"damage plan unchanged "+fps+"/"+speed);
      Need(r,fx.AreaParticles>0,"ground debris present "+fps+"/"+speed);
      if(speed==1)Render(host,"hill_seed_"+(fps/30)+".jpg");
      if(fps==30&&speed==1)for(int i=0;i<12;i++){fx.Sample(.45f+i*.17f);Render(host,"hill_motion_"+i.ToString("D2")+".jpg");}
      fx.Sample(fx.Life+.7f);Need(r,fx.RiftVisibleBranches==0&&fx.AreaParticles==0,"all effects end "+fps+"/"+speed);
     }finally{Object.DestroyImmediate(host);}
    }
    // No collider below the path: don't invent a flat bridge through empty space.
    hill.SetActive(false);Physics.SyncTransforms();var empty=new GameObject("MissingGroundAudit");
    try{var fx=empty.AddComponent<Vfx120Effect>();fx.Profile=Profile;fx.PreviewControlled=true;fx.SetAreaPlan(new AreaImpactPlan{Shape=AreaShape.Path,Point=Origin,Direction=Vector3.forward,Radius=6,Length=12,Speed=7,Delay=.4f});fx.Begin(Origin,null,Origin+Vector3.forward*12,Color.white);fx.Sample(2.2f);Need(r,fx.RiftMissingGround>0&&empty.GetComponentsInChildren<MeshFilter>().Where(x=>x.name.StartsWith("ProceduralRift_")).All(x=>x.sharedMesh.triangles.Length==0),"missing ground is not bridged");}finally{Object.DestroyImmediate(empty);}
    r.status="PASS";
   }catch(Exception e){r.status="FAIL";r.errors.Add(e.ToString());}
   finally{if(hill!=null)Object.DestroyImmediate(hill);if(hillMesh!=null)Object.DestroyImmediate(hillMesh);if(material!=null)Object.DestroyImmediate(material);}
   Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"audit.json"),JsonUtility.ToJson(r,true));return r.status+" "+r.checks.Count+" checks "+string.Join(";",r.errors);
  }
  static void NeedSurface(MeshCollider hill,Vector3 point,ref float max)
  {
   if(!hill.Raycast(new Ray(point+Vector3.up*16,Vector3.down),out var hit,48))throw new Exception("No hill at ribbon edge");
   max=Mathf.Max(max,Mathf.Abs(point.y-hit.point.y-Vfx120Effect.RiftSurfaceOffset));
  }
  static Mesh MakeHill()
  {
   const int n=128;var v=new Vector3[(n+1)*(n+1)];var t=new List<int>();
   for(int z=0;z<=n;z++)for(int x=0;x<=n;x++)
   {
    float xx=-9+18f*x/n,zz=-3+19f*z/n;
    float y=2.5f*Mathf.Exp(-Mathf.Pow((zz-6)/3.5f,2))+.4f*Mathf.Sin(xx*.8f)*Mathf.Sin(zz*.5f);
    int i=z*(n+1)+x;v[i]=new Vector3(xx,y,zz);
    if(x<n&&z<n)t.AddRange(new[]{i,i+n+1,i+1,i+1,i+n+1,i+n+2});
   }
   var mesh=new Mesh{name="DiagnosticRollingHill"};mesh.vertices=v;mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
  }
  static void Render(GameObject host,string name)
  {
   var cameraGo=new GameObject("HillReviewCamera");var camera=cameraGo.AddComponent<Camera>();var rt=new RenderTexture(1920,1080,24);Texture2D image=null;var previous=RenderTexture.active;
   try
   {
    camera.transform.position=Origin+new Vector3(10,11,-7);camera.transform.LookAt(Origin+new Vector3(0,1,6));camera.fieldOfView=52;camera.farClipPlane=60;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.09f,.09f);camera.targetTexture=rt;camera.Render();
    RenderTexture.active=rt;image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToJPG(90));
   }finally{RenderTexture.active=previous;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);if(image!=null)Object.DestroyImmediate(image);Object.DestroyImmediate(cameraGo);}
  }
 }
}
