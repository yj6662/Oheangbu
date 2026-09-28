using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string Out275="../Art/World/Compact/Rebuild/Rock275";
  const string Asset275="Assets/_Project/Art/World/Rock275";
  public static string Rock275(string command)
  {
   Directory.CreateDirectory(Out275);var scene=FrontageScene249();var roots=scene.GetRootGameObjects();
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();
   if(command=="inspect")
   {
    var list=new List<string>();list.Add("sheet="+AssetDatabase.GetAssetPath(art.Sheet));
    foreach(var p in art.Sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Rock))
    {list.Add("ROCK "+p.Id+" size="+p.Size+" count="+art.Sheet.FixedPlacements.Count(v=>v.PrototypeId==p.Id));foreach(var l in p.Lods)foreach(var part in l.Parts)list.Add("mesh="+AssetDatabase.GetAssetPath(part.Mesh)+" bounds="+part.Mesh.bounds+" local="+part.Local+" triangles="+part.Mesh.GetIndexCount(part.Submesh)/3+" material="+AssetDatabase.GetAssetPath(part.Material)+" shader="+part.Material.shader.name);}
    foreach(var r in roots.SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Where(r=>r.name.Contains("Rock")||r.name.Contains("Cliff")).Take(30))list.Add("SCENE "+r.name+" "+r.transform.position+" "+r.bounds.size);
    File.WriteAllLines(Out275+"/inventory.txt",list);return string.Join("\n",list);
   }
   if(command.StartsWith("capture:"))return CaptureRock275(command.Substring(8));
   return BuildRock275(command);
  }
  [Serializable] sealed class Mesh275 {public Vector3[] vertices,normals;public Vector2[] uv;public int[] triangles;}
  static Mesh ImportRock275(int variant,int level,Vector3 size,string name)
  {
   var data=JsonUtility.FromJson<Mesh275>(File.ReadAllText(Out275+"/granite-"+variant+(level==0?"-near":"-far")+".json"));
   var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.vertices=data.vertices.Select(v=>Vector3.Scale(v,size)).ToArray();
   mesh.normals=data.normals.Select(n=>new Vector3(n.x/size.x,n.y/size.y,n.z/size.z).normalized).ToArray();mesh.uv=data.uv;mesh.triangles=data.triangles;mesh.RecalculateBounds();mesh.RecalculateTangents();
   return ArtMesh(mesh,Asset275+"/"+name+".asset");
  }
  static string BuildRock275(string command)
  {
   var scene=FrontageScene249();var roots=scene.GetRootGameObjects();var session=VillageSession();
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();
   var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single();
   var arrival=roots.SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>()).Single();
   if(command=="check")return CheckRock275();
   if(command!="build"||EditorApplication.isPlayingOrWillChangePlaymode||scene.isDirty)throw new Exception("Clean Edit candidate required");
   Directory.CreateDirectory(Out275+"/Recovery");
   foreach(var p in new[]{scene.path,AssetDatabase.GetAssetPath(art.Sheet),AssetDatabase.GetAssetPath(arrival.Catalog)})
   {var target=Out275+"/Recovery/"+Path.GetFileName(p);if(!File.Exists(target)){File.Copy(p,target);if(File.Exists(p+".meta"))File.Copy(p+".meta",target+".meta");}}
   DevSceneKit.EnsureFolder(Asset275);
   if(AssetDatabase.LoadAssetAtPath<Sheet>(Asset275+"/SourceSheet.asset")==null)AssetDatabase.CreateAsset(Object.Instantiate(art.Sheet),Asset275+"/SourceSheet.asset");
   var sheet=Object.Instantiate(AssetDatabase.LoadAssetAtPath<Sheet>(Asset275+"/SourceSheet.asset"));
   var shader=Shader.Find("Oheangbu/Compact/JointedRock275");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Rock shader not compiled");
   var sourceMaterial=sheet.Prototypes.First(p=>p.Category==Sheet.Kind.Rock).Lods[0].Parts[0].Material;
   var material=new Material(sourceMaterial){shader=shader,name="JointedGranite275",enableInstancing=true};
   material.SetFloat("_WindAmplitude",0);material.SetFloat("_RockScale",.36f);material.SetFloat("_BumpScale",1.25f);material.SetFloat("_Saturation",.20f);
   material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/World/WorldCompact/NaturalSurface/Textures/T_RockGround_1_BC.png"));
   string baseFolder=Path.GetDirectoryName(scene.path).Replace('\\','/');
   material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(baseFolder+"/Surface239/Textures/Fracture_Normal.png"));
   material=SurveyMaterial(material,Asset275+"/Granite.mat");
   foreach(var proto in sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Rock))
   {
    int variant=proto.Id.EndsWith("_L")?1:0;
    proto.Lods=Enumerable.Range(0,2).Select(l=>new Sheet.Level{Parts=new[]{new Sheet.Part{Mesh=ImportRock275(variant,l,proto.Size,proto.Id+"_LOD"+l),Material=material}}}).ToArray();
    proto.SourcePath=Out275+"/JointedGranite275.blend";proto.PolishVersion=275;
   }
   art.Sheet=SavePrivate(sheet,Asset275+"/Placements.asset");manifest.Art=art.Sheet;art.Invalidate();
   arrival.Catalog.Entries=arrival.Catalog.Entries.Where(e=>e.Id!="worker_satchel_site"&&e.Id!="mine_tool_marks_site").ToArray();
   var mine=arrival.Catalog.Entries.Single(e=>e.Id=="mine_interior");mine.Name="폐광";mine.RealmId="realm_cheongrim";
   EditorUtility.SetDirty(arrival.Catalog);EditorUtility.SetDirty(art);EditorUtility.SetDirty(manifest);
   var old=roots.FirstOrDefault(g=>g.name=="JointedMountain275");if(old!=null)Object.DestroyImmediate(old);
   var mountain=new GameObject("JointedMountain275");SceneManager.MoveGameObjectToScene(mountain,scene);
   var near=ImportRock275(2,0,Vector3.one,"MassifNear");var far=ImportRock275(2,1,Vector3.one,"MassifFar");
   var ground=FinalSurface(scene);var layout=manifest.Layout;var random=new System.Random(275);int count=0;
   var routes=layout.Routes.Select(r=>new{r.Width,Points=new[]{layout.Places.Single(p=>p.Id==r.From).XZ}.Concat(r.Bends).Concat(new[]{layout.Places.Single(p=>p.Id==r.To).XZ}).ToArray()}).ToArray();
   var cells=new Dictionary<Vector2Int,List<Matrix4x4>>();
   for(float z=1680;z<3970;z+=95)for(float x=2690;x<3990;x+=95)
   {
    float px=x+(float)random.NextDouble()*40-20,pz=z+(float)random.NextDouble()*40-20;
    var hit=ground(px,pz);float slope=Vector3.Angle(hit.normal,Vector3.up);if(slope<18||slope>74||hit.point.y<95)continue;
    if(Mathf.PerlinNoise(px*.0041f,pz*.0041f)<.38f)continue;
    var q=new Vector2(px,pz);float width=32+(float)random.NextDouble()*30,height=width*.68f,depth=width*.7f,radius=width*.8f;
    if(layout.Places.Any(p=>Vector2.Distance(q,p.XZ)<p.GroundRadius+radius+40))continue;
    if(session.Content.Points.Any(p=>Vector2.Distance(q,new Vector2(p.Position.x,p.Position.z))<radius+38))continue;
    bool blocked=false;foreach(var r in routes){for(int i=1;i<r.Points.Length;i++){var a=r.Points[i-1];var d=r.Points[i]-a;float t=d.sqrMagnitude==0?0:Mathf.Clamp01(Vector2.Dot(q-a,d)/d.sqrMagnitude);if(Vector2.Distance(q,a+d*t)<radius+r.Width*.5f+30){blocked=true;break;}}if(blocked)break;}
    if(blocked)continue;
    // Existing remote hillside navigation is excluded by the stationary obstacle below.
    // Roads and POIs have already been reserved with a full formation radius plus margin.
    var pos=hit.point-Vector3.up*height*.62f;
    var yaw=Mathf.Atan2(hit.normal.x,hit.normal.z)*Mathf.Rad2Deg+random.Next(-18,19);
    var matrix=Matrix4x4.TRS(pos,Quaternion.Euler(0,yaw,0),new Vector3(width,height,depth));
    var obstacleGo=new GameObject("RockObstruction_"+count);obstacleGo.transform.SetParent(mountain.transform,false);obstacleGo.transform.SetPositionAndRotation(pos,Quaternion.Euler(0,yaw,0));
    var obstacle=obstacleGo.AddComponent<UnityEngine.AI.NavMeshObstacle>();obstacle.shape=UnityEngine.AI.NavMeshObstacleShape.Box;obstacle.center=Vector3.up*height*.5f;obstacle.size=new Vector3(width,height,depth);obstacle.carving=true;obstacle.carveOnlyStationary=true;
    var key=new Vector2Int(Mathf.FloorToInt(px/256),Mathf.FloorToInt(pz/256));if(!cells.TryGetValue(key,out var list)){list=new List<Matrix4x4>();cells.Add(key,list);}list.Add(matrix);count++;
   }
   foreach(var pair in cells)
   {
    var cell=new GameObject("Cliff_"+pair.Key.x+"_"+pair.Key.y);cell.transform.SetParent(mountain.transform,false);var renderers=new Renderer[2];
    for(int l=0;l<2;l++)
    {
     var go=new GameObject("LOD"+l);go.transform.SetParent(cell.transform,false);var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(pair.Value.Select(m=>new CombineInstance{mesh=l==0?near:far,transform=m}).ToArray(),true,true);mesh=ArtMesh(mesh,Asset275+"/Cliff_"+pair.Key.x+"_"+pair.Key.y+"_"+l+".asset");
     go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=l==0?ShadowCastingMode.On:ShadowCastingMode.Off;renderers[l]=r;
     if(l==1)cell.AddComponent<MeshCollider>().sharedMesh=mesh;
    }
    var lod=cell.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.22f,new[]{renderers[0]}),new LOD(.006f,new[]{renderers[1]})});lod.RecalculateBounds();
   }
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(Out275+"/catalog.json",JsonUtility.ToJson(arrival.Catalog,true));
   File.WriteAllText(Out275+"/placements.json",JsonUtility.ToJson(art.Sheet,true));
   File.WriteAllText(Output+"/art_placements.json",JsonUtility.ToJson(art.Sheet,true));
   string report="Blender rock variants replace "+sheet.FixedPlacements.Count(p=>sheet.Prototypes.Any(t=>t.Id==p.PrototypeId&&t.Category==Sheet.Kind.Rock))+" existing rocks; original placement transforms retained. Mountain formations="+count+", 256m merged sectors/colliders="+cells.Count+". No original terrain mesh, walk surface, NavMesh asset or save slot changed. Mine arrival is one volume.";
   File.WriteAllText(Out275+"/build.txt",report);return report;
  }
  static string CheckRock275()
  {
   var scene=FrontageScene249();var roots=scene.GetRootGameObjects();var session=VillageSession();var lines=new List<string>();
   void C(bool pass,string label)=>lines.Add((pass?"PASS ":"FAIL ")+label);
   var arrival=roots.SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>()).Single();var cat=arrival.Catalog;
   var cave=cat.Entries.Single(e=>e.Id=="mine_interior");cat.ArrivalNames(cave,out var realm,out var name);
   C(realm=="청림"&&name=="폐광","mine arrival has separate realm and local lines");
   cat.ArrivalNames(cat.Entries.Single(e=>e.Id=="realm_cheongrim"),out realm,out name);C(realm=="청림"&&name=="","realm alone does not duplicate its name");
   C(!cat.Entries.Any(e=>e.Id=="worker_satchel_site"||e.Id=="mine_tool_marks_site"),"mine clue sub-areas removed only from arrival catalog");
   C(session.Content.Points.Any(p=>p.Id=="worker_satchel")&&session.Content.Points.Any(p=>p.Id=="mine_tool_marks"),"both evidence interactions remain");
   var tracker=new WorldLocationTracker(cat);tracker.Step(session.Content.StartFeet,0);tracker.Step(session.Content.StartFeet,1);C(tracker.TakeAnnouncement(1,true)==cave,"mine entrance announces once");
   float now=40;foreach(string id in new[]{"worker_satchel","mine_tool_marks"}){var p=session.Content.Points.Single(v=>v.Id==id).Position;tracker.Step(p,now);tracker.Step(p,now+1);C(tracker.Current==cave&&tracker.TakeAnnouncement(now+1,true)==null,"moving to "+id+" stays in same mine announcement even after cooldown");now+=40;}
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var source=AssetDatabase.LoadAssetAtPath<Sheet>(Asset275+"/SourceSheet.asset");
   C(JsonUtility.ToJson(new PlacementProof275{items=source.FixedPlacements})==JsonUtility.ToJson(new PlacementProof275{items=art.Sheet.FixedPlacements}),"all old placement IDs/transforms/scales preserved");
   var prototypes=art.Sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Rock).ToArray();
   C(prototypes.All(p=>p.PolishVersion==275&&p.Lods.Length==2&&p.Lods[1].Parts[0].Mesh.triangles.Length<p.Lods[0].Parts[0].Mesh.triangles.Length),"all rock prototypes have new near and reduced far geometry");
   C(prototypes.All(p=>p.Lods.All(l=>l.Parts[0].Mesh.bounds.size.x<=p.Size.x*1.001f&&l.Parts[0].Mesh.bounds.size.y<=p.Size.y*1.001f&&l.Parts[0].Mesh.bounds.size.z<=p.Size.z*1.001f)),"replacement geometry remains inside original prototype envelope");
   C(prototypes.All(p=>p.Lods.All(l=>l.Parts[0].Mesh.normals.All(n=>float.IsFinite(n.x)&&Mathf.Abs(n.magnitude-1)<.01f))),"finite normalized rock surface normals");
   var material=AssetDatabase.LoadAssetAtPath<Material>(Asset275+"/Granite.mat");C(material.GetTexture("_BaseMap")!=null&&material.GetTexture("_BumpMap")!=null,"physical scale rock detail textures bound");
   C(!ShaderUtil.ShaderHasError(material.shader)&&material.shader.isSupported,"rock shader supported without compilation errors");
   C(material.GetFloat("_WindAmplitude")==0&&material.GetFloat("_WashStrength")==source.Prototypes.First(p=>p.Category==Sheet.Kind.Rock).Lods[0].Parts[0].Material.GetFloat("_WashStrength"),"solid rocks do not sway and distance wash remains single-path");
   var mountain=roots.Single(g=>g.name=="JointedMountain275");var lods=mountain.GetComponentsInChildren<LODGroup>();
   C(lods.Length>0&&lods.All(g=>g.GetLODs().Length==2&&g.GetComponent<MeshCollider>()!=null),"mountain sectors have near/far meshes and merged static collision");
   C(mountain.GetComponentsInChildren<Rigidbody>().Length==0,"no rigidbody on stationary mountain rock");
   var obstacles=mountain.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>();C(obstacles.Length>0&&obstacles.All(o=>o.carving&&o.carveOnlyStationary),"new solid outcrops configured for stationary navigation carving");
   var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single();
   C(obstacles.All(o=>manifest.Layout.Places.All(p=>Vector2.Distance(new Vector2(o.transform.position.x,o.transform.position.z),p.XZ)>o.size.x*.8f+p.GroundRadius+39)),"outcrop bounding radii keep authored POIs clear");
   bool roadsClear=true;foreach(var o in obstacles)foreach(var route in manifest.Layout.Routes)
   {
    var q=new Vector2(o.transform.position.x,o.transform.position.z);var points=new[]{manifest.Layout.Places.Single(p=>p.Id==route.From).XZ}.Concat(route.Bends).Concat(new[]{manifest.Layout.Places.Single(p=>p.Id==route.To).XZ}).ToArray();
    for(int i=1;i<points.Length;i++){var d=points[i]-points[i-1];float t=d.sqrMagnitude==0?0:Mathf.Clamp01(Vector2.Dot(q-points[i-1],d)/d.sqrMagnitude);if(Vector2.Distance(q,points[i-1]+d*t)<o.size.x*.8f+route.Width*.5f+29)roadsClear=false;}
   }
   C(roadsClear,"all authored route segments keep formation radius plus 29m clearance");
   C(session.Content.SaveSlot=="world-demo-compact-cave-v4","save slot unchanged");
   var preview=EditorSceneManager.NewPreviewScene();
   try
   {
    foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)})
    {
     var go=new GameObject("Arrival275",typeof(RectTransform),typeof(Canvas));SceneManager.MoveGameObjectToScene(go,preview);var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.scaleFactor=size.y/1080f;
     var cameraGo=new GameObject("Arrival275 preview");SceneManager.MoveGameObjectToScene(cameraGo,preview);var camera=cameraGo.AddComponent<Camera>();camera.scene=preview;camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.18f,.17f);canvas.worldCamera=camera;canvas.planeDistance=1;
     var upper=WorldLocationArrival.CreateName(go.transform,arrival.Theme.Font);var lower=WorldLocationArrival.CreateDetail(upper,arrival.Theme.Font);WorldLocationArrival.SetNames(cat,cave,upper,lower);
     var rt=new RenderTexture(size.x,size.y,24);var tex=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);var previous=RenderTexture.active;
     try{camera.targetTexture=rt;camera.aspect=size.x/(float)size.y;Canvas.ForceUpdateCanvases();C(upper.text=="청림"&&lower.text=="폐광"&&upper.fontSize>lower.fontSize&&!upper.raycastTarget&&!lower.raycastTarget,size+" two lines have distinct sizes and do not intercept input");
      var a=new Vector3[4];var b=new Vector3[4];upper.rectTransform.GetWorldCorners(a);lower.rectTransform.GetWorldCorners(b);C(camera.WorldToViewportPoint(a[0]).y>camera.WorldToViewportPoint(b[1]).y-.015f,size+" local line below realm without overlapping glyphs");
      camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,size.x,size.y),0,0);tex.Apply();File.WriteAllBytes(Out275+"/arrival-"+size.x+".png",tex.EncodeToPNG());
     }finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);Object.DestroyImmediate(cameraGo);}
    }
   }finally{EditorSceneManager.ClosePreviewScene(preview);}
   foreach(var p in prototypes)lines.Add("METRIC "+p.Id+" triangles="+string.Join("/",p.Lods.Select(l=>l.Parts[0].Mesh.triangles.Length/3)));
   lines.Add("METRIC formations="+obstacles.Length+" collision sectors="+lods.Length+". Runtime carving, CPU/GPU and user art judgment remain unmeasured.");
   File.WriteAllLines(Out275+"/checks.txt",lines);return string.Join("\n",lines);
  }
  [Serializable] sealed class PlacementProof275{public Sheet.FixedPlacement[] items;}
  static string CaptureRock275(string tag)
  {
   var scene=FrontageScene249();var roots=scene.GetRootGameObjects();var session=VillageSession();var ground=FinalSurface(scene);
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var observer=art.Observer;
   var go=new GameObject("Rock275 review");var camera=go.AddComponent<Camera>();camera.CopyFrom(session.Walker.ViewCamera);camera.enabled=false;camera.useOcclusionCulling=false;
   EditorUtility.CopySerialized(session.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());art.Observer=camera;
   var placement=art.Sheet.FixedPlacements.Where(p=>art.Sheet.Prototypes.Any(t=>t.Id==p.PrototypeId&&t.Category==Sheet.Kind.Rock)).OrderBy(p=>Vector2.Distance(new Vector2(p.Position.x,p.Position.z),new Vector2(3110,2250))).First();
   var b=placement.Position;var eyes=new[]{b+new Vector3(4,2,-5),new Vector3(3410,140,1880),new Vector3(3250,205,2260),new Vector3(3120,63,2220)};
   var targets=new[]{b+Vector3.up*.5f,new Vector3(3020,85,2280),new Vector3(2820,130,2850),new Vector3(3780,180,2090)};
   if(tag=="formations")
   {
    var outcrops=roots.Single(g=>g.name=="JointedMountain275").GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>().OrderBy(o=>o.transform.position.z).Take(4).ToArray();
    eyes=outcrops.Select(o=>o.transform.position+new Vector3(-55,o.size.y*.9f,-65)).ToArray();targets=outcrops.Select(o=>o.transform.position+Vector3.up*o.size.y*.5f).ToArray();
   }
   var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var prior=RenderTexture.active;
   try{camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=62;for(int i=0;i<eyes.Length;i++){camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i]));camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(Out275+"/"+tag+"-"+i+".png",tex.EncodeToPNG());}}
   finally{art.Observer=observer;camera.targetTexture=null;RenderTexture.active=prior;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
   return "Four matched offscreen rock/landscape captures: "+tag;
  }
 }
}
