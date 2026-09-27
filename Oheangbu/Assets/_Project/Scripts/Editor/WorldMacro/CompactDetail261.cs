using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using Oheangbu.App.World;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string DetailOutput261=Output+"/Detail261", DetailRoot261="CheongrimDetail261";
  static Vector3[][] DetailPaths261()
  {
   var paths=new List<Vector3[]>();
   foreach(string dir in new[]{"Branches259","Progression251","Escort252","Village245"})
   {
    string file=Output+"/"+dir+"/route-plan.json";if(!File.Exists(file))continue;
    var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(file));
    if(plan.routes!=null)paths.AddRange(plan.routes.Select(r=>r.points.Select(p=>new Vector3(p.x,0,p.y)).ToArray()));
   }
   paths.Add(VillageSession().Content.MainPath);return paths.Where(p=>p!=null&&p.Length>1).ToArray();
  }
  public static string Detail261(string command)
  {
   var scene=FrontageScene249();var roots=scene.GetRootGameObjects();var s=VillageSession();
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();
   var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single();
   var ground=FinalSurface(scene);Directory.CreateDirectory(DetailOutput261);
   if(command.StartsWith("capture:"))return CaptureDetail261(command.Substring(8));
   if(command=="litter")
   {
    var detail=roots.Single(g=>g.name==DetailRoot261);
    foreach(var t in detail.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("GroundLitter_")).ToArray())Object.DestroyImmediate(t.gameObject);
    var branchPaths=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(BranchOutput259+"/route-plan.json")).routes.Select(r=>r.points.Select(p=>new Vector3(p.x,0,p.y)).ToArray()).ToArray();
    BuildDetailLitter261(detail,Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Detail261",branchPaths,ground);
    AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return File.ReadAllText(DetailOutput261+"/litter.txt");
   }
   if(command=="audit")
   {
    var lines=new List<string>();void C(bool ok,string label){lines.Add((ok?"PASS ":"FAIL ")+label);}
    var added=art.Sheet.FixedPlacements.Where(p=>p.Id.StartsWith("detail261_")).ToArray();var protos=art.Sheet.Prototypes.ToDictionary(p=>p.Id);
    C(added.Length>1000,"populated authored mountain/forest/ground layers: "+added.Length);
    C(art.Sheet.FixedPlacements.Select(p=>p.Id).Distinct().Count()==art.Sheet.FixedPlacements.Length,"unique placement IDs");
    C(roots.Count(g=>g.name==DetailRoot261)==1,"single owned root");
    C(manifest.Art==art.Sheet,"manifest and live renderer share placement ledger");
    C(!art.Sheet.FixedPlacements.Any(p=>p.Id.StartsWith("herb259_")),"radial herb ring removed");
    C(added.All(p=>protos.ContainsKey(p.PrototypeId)),"every placement resolves prototype");
    C(art.Sheet.Prototypes.Where(p=>p.Id.StartsWith("Detail261_")).SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).All(p=>p.Mesh!=null&&p.Material!=null&&p.Material.enableInstancing&&p.Material.shader.isSupported&&!ShaderUtil.ShaderHasError(p.Material.shader)),"private instanced materials and meshes supported without shader errors");
    float gap=0;foreach(var p in added){var proto=protos[p.PrototypeId];float embed=proto.Category==Sheet.Kind.Rock?proto.Size.y*p.Scale*.25f:0;gap=Mathf.Max(gap,Mathf.Abs(p.Position.y+embed-ground(p.Position.x,p.Position.z).point.y));}
    C(gap<.025f,"ledger grounding within 2.5cm, deliberate rock burial; max="+gap);
    var solids=roots.Single(g=>g.name==DetailRoot261).GetComponentsInChildren<Collider>();
    C(solids.All(c=>added.Any(p=>p.Id==c.name&&Vector3.Distance(p.Position,(c is CapsuleCollider?c.transform:c.transform.parent).position)<.001f)),"all new solids match ledger coordinates: "+solids.Length);
    var auditPaths=DetailPaths261();C(added.Where(p=>protos[p.PrototypeId].Category==Sheet.Kind.Tree||protos[p.PrototypeId].Category==Sheet.Kind.Rock).All(p=>auditPaths.All(path=>FlatPathDistance(p.Position,path)>(protos[p.PrototypeId].Category==Sheet.Kind.Tree?.65f*p.Scale:protos[p.PrototypeId].Radius*p.Scale)+3)),"tree trunks and rock volumes clear all authored routes");
    C(s.Content.SaveSlot=="world-demo-compact-cave-v4","save slot unchanged");
    C(scene.GetRootGameObjects().Single(g=>g.name=="Compact_Rebuild_Terrain").GetComponentsInChildren<MeshFilter>().All(f=>f.sharedMesh==f.GetComponent<MeshCollider>().sharedMesh),"terrain visible and collision meshes still identical");
    var litter=roots.Single(g=>g.name==DetailRoot261).GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("GroundLitter_")).ToArray();
    C(litter.Length>0&&litter.All(f=>f.sharedMesh.normals.All(n=>n.y>0)),"ground microrelief faces point upward");
    C(litter.All(f=>f.GetComponent<Collider>()==null&&f.GetComponent<Renderer>().sharedMaterials.All(m=>m!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader))),"render-only litter has valid materials and does not replace collision floor");
    File.WriteAllText(DetailOutput261+"/audit.txt",string.Join("\n",lines));return string.Join("\n",lines);
   }
   if(command!="build")throw new ArgumentException(command);
   if(scene.isDirty)throw new Exception("Clean candidate required before detail build");
   string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Detail261";Directory.CreateDirectory(folder);
   string backup=DetailOutput261+"/Before/"+DateTime.UtcNow.ToString("yyyyMMddHHmmss");Directory.CreateDirectory(backup);
   var files=new[]{scene.path,AssetDatabase.GetAssetPath(art.Sheet),Path.GetDirectoryName(scene.path)+"/Navigation.asset"};
   for(int i=0;i<files.Length;i++)foreach(var suffix in new[]{"",".meta"})if(File.Exists(files[i]+suffix))File.Copy(files[i]+suffix,backup+"/"+i+"_"+Path.GetFileName(files[i])+suffix,true);
   File.WriteAllLines(backup+"/paths.txt",files);
   var sheet=Object.Instantiate(art.Sheet);sheet.name="Detail261_Placements";
   var prototypes=sheet.Prototypes.Where(p=>!p.Id.StartsWith("Detail261_")).ToList();var originals=prototypes.ToArray();
   // Derivatives only: the old forest and canonical materials retain their exact look.
   foreach(var src in originals.Where(p=>p.Id.StartsWith("Cheongrim_")||p.Id=="Meshy_Pinus"))
   {
    var copy=JsonUtility.FromJson<Sheet.Prototype>(JsonUtility.ToJson(src));copy.Id="Detail261_"+src.Id;
    copy.Lods=src.Lods.Select((level,l)=>new Sheet.Level{Parts=level.Parts.Select((part,n)=>
    {
     string mp=folder+"/"+copy.Id+"_"+l+"_"+n+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(mp);
     if(mat==null){mat=new Material(part.Material);AssetDatabase.CreateAsset(mat,mp);}else mat.CopyPropertiesFromMaterial(part.Material);
     mat.enableInstancing=true;
     void F(string key,float value){if(mat.HasProperty(key))mat.SetFloat(key,value);}
     if(src.Category==Sheet.Kind.Tree){if(mat.HasProperty("_PaintedCanopyTones"))mat.SetVector("_PaintedCanopyTones",new Vector4(.038f,.08f,.14f,.23f));F("_AmbientFloor",.53f);}
     if(src.Category==Sheet.Kind.Shrub||src.Category==Sheet.Kind.Grass){if(mat.HasProperty("_PaintedCanopyTones"))mat.SetVector("_PaintedCanopyTones",new Vector4(.075f,.13f,.21f,.31f));F("_AmbientFloor",.57f);}
     F("_WindAmplitude",.018f);F("_LeafFlutter",0);EditorUtility.SetDirty(mat);
     return new Sheet.Part{Mesh=part.Mesh,Material=mat,Local=part.Local,Submesh=part.Submesh};
    }).ToArray()}).ToArray();prototypes.Add(copy);
   }
   sheet.Prototypes=prototypes.ToArray();var protoById=prototypes.ToDictionary(p=>p.Id);
   var placements=sheet.FixedPlacements.Where(p=>!p.Id.StartsWith("detail261_")&&!p.Id.StartsWith("herb259_")).ToList();
   var old=roots.SingleOrDefault(g=>g.name==DetailRoot261);if(old!=null)Object.DestroyImmediate(old);
   var root=new GameObject(DetailRoot261);var paths=DetailPaths261();
   var branch=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(BranchOutput259+"/route-plan.json")).routes.Select(r=>r.points.Select(p=>new Vector3(p.x,0,p.y)).ToArray()).ToArray();
   float Distance(Vector3 p)=>branch.Min(path=>FlatPathDistance(p,path));
   var random=new System.Random(261);float R(float min,float max)=>(float)(min+random.NextDouble()*(max-min));
   int serial=0;var trunks=new List<Vector3>();
   bool Put(string id,float x,float z,float scale,float yaw,string cluster,float tilt=0)
   {
    var proto=protoById["Detail261_"+id];var hit=ground(x,z);float slope=Vector3.Angle(hit.normal,Vector3.up);
    if(slope>(proto.Category==Sheet.Kind.Rock?67:proto.Category==Sheet.Kind.Tree?43:38))return false;
    float radius=proto.Category==Sheet.Kind.Rock?proto.Radius*scale:proto.Category==Sheet.Kind.Tree?.65f*scale:.12f;
    float clearance=proto.Category==Sheet.Kind.Tree||proto.Category==Sheet.Kind.Rock?3:proto.Category==Sheet.Kind.Prop?2.5f:.9f;
    if(paths.Any(path=>FlatPathDistance(hit.point,path)<radius+clearance))return false;
    if(s.Content.Points.Any(p=>Vector2.Distance(new Vector2(x,z),new Vector2(p.Position.x,p.Position.z))<p.Radius+radius+(proto.Category==Sheet.Kind.Tree?2:1)))return false;
    if(s.Content.Encounters.Any(e=>Vector2.Distance(new Vector2(x,z),new Vector2(e.Feet.x,e.Feet.z))<e.Leash+radius+2))return false;
    if(proto.Category==Sheet.Kind.Tree&&trunks.Any(p=>Vector2.Distance(new Vector2(p.x,p.z),new Vector2(x,z))<4.8f))return false;
    var pos=hit.point;if(proto.Category==Sheet.Kind.Rock)pos.y-=proto.Size.y*scale*.25f;
    placements.Add(new Sheet.FixedPlacement{Id="detail261_"+serial++,PrototypeId=proto.Id,ClusterId=cluster,Position=pos,Euler=new Vector3(tilt,yaw,0),Scale=scale,Preserve=true});
    if(proto.Category==Sheet.Kind.Tree)trunks.Add(pos);return true;
   }
   // Connected yet unequal wooded shoulders. Clear windows are retained at the fork and bends.
   for(float z=2490;z<3330;z+=10)for(float x=2820;x<3490;x+=10)
   {
    float px=x+R(-4,4),pz=z+R(-4,4);var point=new Vector3(px,0,pz);float d=Distance(point);
    float group=Mathf.PerlinNoise(px*.012f+18,pz*.009f+39);
    if(d>225||group<.38f||R(0,1)>(d<85?.91f:.75f))continue;
    bool pine=R(0,1)<.78f;Put(pine?"Cheongrim_SM_PinusDensiflora_Spring_2":"Cheongrim_SM_UlmusDavidiana_Summer_2",px,pz,pine?R(1.45f,2.65f):R(.67f,1.02f),R(0,360),"wooded_shoulder");
   }
   // Low, partly buried outcrops break the smooth slopes. Smaller talus collects below each.
   var seams=new[]{new Vector2(2985,2780),new Vector2(3070,2850),new Vector2(3180,2670),new Vector2(3230,2980),new Vector2(3320,3110),new Vector2(2875,2995),new Vector2(3050,3180)};
   foreach(var seam in seams)for(int i=0;i<13;i++)
   {
    float x=seam.x+i*2.8f+R(-5,5),z=seam.y+i*4.3f+R(-4,4),scale=R(1.35f,4.3f);
    if(!Put("Cheongrim_SM_Rock_K",x,z,scale,R(20,70),"exposed_bedrock",R(-12,12)))continue;
    for(int j=0;j<8;j++)Put(j%3==0?"Cheongrim_SM_Rock_K":"Cheongrim_SM_Rock_L",x+R(-10,13),z+R(-13,10),j%3==0?R(.25f,.65f):R(.4f,1.5f),R(0,360),"talus",R(-16,16));
   }
   // Asymmetric moist pockets rather than a uniform carpet or rings around evidence.
   for(int i=0;i<26000;i++)
   {
    float x=R(2960,3380),z=R(2520,3230);var p=new Vector3(x,0,z);float d=Distance(p);
    if(d>45||Mathf.PerlinNoise(x*.095f+2,z*.071f)<.44f||R(0,1)>.76f)continue;
    string id=i%9==0?"Cheongrim_SM_Deparia_3":i%5==0?"Cheongrim_SM_Deparia_1":i%3==0?"Cheongrim_SM_Grass":"Cheongrim_LowGroundFill";
    Put(id,x,z,R(.7f,1.65f),R(0,360),"forest_floor");
    if(i%17==0)Put("Cheongrim_SM_Rock_L",x+1.4f,z-.8f,R(.18f,.6f),R(0,360),"shoulder_stones");
   }
   // Evidence rests beside a worked woodland edge; no extra quest or invented reward.
   var bag=s.Content.Points.Single(p=>p.Id==HerbTrace259).Position;
   for(int i=0;i<5;i++)Put("Cheongrim_SM_Rock_K",bag.x+7+i*1.35f,bag.z+4+i*.7f,R(.9f,1.7f),R(10,65),"herbalist_shelter",R(-9,9));
   for(int i=0;i<7;i++)Put("Cheongrim_SM_M_WoodLog",bag.x+5.3f+i*.38f,bag.z-3.8f+i*.15f,R(.8f,1.35f),R(35,55),"old_cutting");
   Put("Cheongrim_SM_M_WoodenBox",bag.x+5.1f,bag.z-5, .8f,21,"old_cutting");
   foreach(var p in new[]{new Vector2(3040,2790),new Vector2(3019,2804),new Vector2(3110,2690),new Vector2(3240,3085)})Put("Meshy_Pinus",p.x,p.y,1.04f,R(0,360),"near_pine");
   sheet.FixedPlacements=placements.ToArray();art.Sheet=SavePrivate(sheet,folder+"/Placements.asset");manifest.Art=art.Sheet;art.Invalidate();
   int collisions=0;
   foreach(var placed in placements.Where(p=>p.Id.StartsWith("detail261_")))
   {
    var p=protoById[placed.PrototypeId];if(p.Category!=Sheet.Kind.Tree&&p.Category!=Sheet.Kind.Rock&&p.Category!=Sheet.Kind.Prop)continue;
    var go=new GameObject(placed.Id);go.transform.SetParent(root.transform,false);go.transform.SetPositionAndRotation(placed.Position,Quaternion.Euler(placed.Euler));go.transform.localScale=Vector3.one*placed.Scale;
    if(p.Category==Sheet.Kind.Tree){var c=go.AddComponent<CapsuleCollider>();c.radius=.25f;c.height=Mathf.Min(p.Size.y,5);c.center=Vector3.up*c.height*.5f;}
    else {foreach(var part in p.Lods[0].Parts){var child=new GameObject("solid");child.transform.SetParent(go.transform,false);child.transform.localPosition=part.Local.GetColumn(3);child.transform.localRotation=part.Local.rotation;child.transform.localScale=part.Local.lossyScale;child.AddComponent<MeshCollider>().sharedMesh=part.Mesh;child.name=placed.Id;}}
    collisions++;
   }
   BuildDetailLitter261(root,folder,branch,ground);
   foreach(var obj in new Object[]{art,manifest,art.Sheet})EditorUtility.SetDirty(obj);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Physics.SyncTransforms();
   File.WriteAllText(Output+"/art_placements.json",JsonUtility.ToJson(art.Sheet,true));File.WriteAllText(Output+"/Cartography/placements.json",JsonUtility.ToJson(art.Sheet,true));
   var report=string.Join("\n",placements.Where(p=>p.Id.StartsWith("detail261_")).GroupBy(p=>protoById[p.PrototypeId].Category).Select(g=>g.Key+"="+g.Count()))+"\nCollision roots="+collisions+"\nExisting terrain heights and route coordinates unchanged. NavMesh rebake/map forest update required.";
   File.WriteAllText(DetailOutput261+"/build.txt",report);return report;
  }
  static string CaptureDetail261(string label)
  {
   if(label!="before"&&label!="after")throw new ArgumentException(label);
   var scene=FrontageScene249();var s=VillageSession();var ground=FinalSurface(scene);
   var art=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var observer=art.Observer;
   var go=new GameObject("OffscreenDetail261"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.CopyFrom(s.Walker.ViewCamera);camera.enabled=false;
   EditorUtility.CopySerialized(s.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var rt=new RenderTexture(1920,1080,24);var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);var active=RenderTexture.active;
   var eyes=new[]{new Vector2(3118,2628),new Vector2(3026,2793),new Vector2(3184,3020),new Vector2(3134,2744)};
   var targets=new[]{new Vector2(3120,2690),new Vector2(3038,2808),new Vector2(3240,3085),new Vector2(3000,2850)};
   var lines=new List<string>();
   try
   {
    art.Observer=camera;camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=62;
    for(int i=0;i<eyes.Length;i++)
    {
     var eye=ground(eyes[i].x,eyes[i].y).point+Vector3.up*(i==3?16:1.8f);var target=ground(targets[i].x,targets[i].y).point+Vector3.up*(i==1?.8f:3);
     camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();File.WriteAllBytes(DetailOutput261+"/"+label+"-"+i+".png",texture.EncodeToPNG());
     lines.Add(i+": visible="+art.VisibleInstances+" instanced draws="+art.DrawCalls+" submitted triangles="+art.SubmittedTriangles);
    }
    File.WriteAllLines(DetailOutput261+"/"+label+"-render.txt",lines);return "Four matched offscreen views. Submission counts only; not CPU/GPU timings.\n"+string.Join("\n",lines);
   }
   finally{art.Observer=observer;camera.targetTexture=null;RenderTexture.active=active;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);Object.DestroyImmediate(go);}
  }
 }
}
