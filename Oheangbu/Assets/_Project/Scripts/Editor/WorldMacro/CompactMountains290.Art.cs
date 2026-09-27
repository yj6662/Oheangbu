using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class ArtRoute290 { public Vector3[] points; }
  static string Art290()
  {
   var scene=SceneManager.GetActiveScene();if(scene.path!=Scene290)throw new Exception("Candidate only");
   var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var layout=session.MountainLayout;var m=layout.Mountains.Single(x=>x.Realm=="cheongrim");
   string backup=O290+"/before-art-route.json";
   if(!File.Exists(backup)){File.WriteAllText(backup,JsonUtility.ToJson(new ArtRoute290{points=m.MainPath},true));EditorSceneManager.SaveScene(scene,A290+"/BeforeArt.unity",true);AssetDatabase.CopyAsset(A290+"/"+m.Id+"/Height.asset",A290+"/"+m.Id+"/HeightBeforeArt.asset");}
   var oldPath=JsonUtility.FromJson<ArtRoute290>(File.ReadAllText(backup)).points;
   const int first=1125,last=1245;var profile=Profile285;var a=oldPath[first];var b=oldPath[last];var from=profile.points.First();var to=profile.points.Last();
   Vector3 Flat(Vector3 p)=>new Vector3(p.x,0,p.z);
   var rotation=Quaternion.FromToRotation(Flat(to-from).normalized,Flat(b-a).normalized);
   float scale=Flat(b-a).magnitude/Flat(to-from).magnitude;var scaling=new Vector3(scale,(b.y-a.y)/(to.y-from.y),scale);
   var matrix=Matrix4x4.TRS(a-rotation*Vector3.Scale(from,scaling),rotation,scaling);var inverse=matrix.inverse;
   var old=GameObject.Find("Cheongrim_AssetPass_290");if(old!=null)Object.DestroyImmediate(old);
   var art=new GameObject("Cheongrim_AssetPass_290");art.transform.SetPositionAndRotation(matrix.MultiplyPoint3x4(Vector3.zero),rotation);art.transform.localScale=scaling;
   string[] prefixes={"Granite_face_","Granite_foundation_","Carved_trail_","Trail_shoulder_","Timber_bridge","Crest_pine_","Joint_pine_","Ledge_pine_","Pine_root_rock","Debris_batch_","Outer_bedrock_","Habitat_","Apron_elm_"};
   int copied=0;var prototype=EditorSceneManager.OpenScene(Scene285,OpenSceneMode.Additive);
   try{SceneManager.SetActiveScene(scene);var source=prototype.GetRootGameObjects().Single(g=>g.name=="Granite_Trail_285");
    foreach(Transform child in source.transform)if(prefixes.Any(p=>child.name.StartsWith(p))){var clone=Object.Instantiate(child.gameObject,art.transform,false);clone.name=child.name;copied++;}
   }finally{EditorSceneManager.CloseScene(prototype,true);SceneManager.SetActiveScene(scene);}
   if(copied<10)throw new Exception("Prototype geometry selection unexpectedly empty");
   var joins=oldPath.Skip(first-20).Take(20).Concat(oldPath.Skip(last+1).Take(20)).ToArray();
   foreach(var filter in art.GetComponentsInChildren<MeshFilter>().Where(f=>f.transform.parent.name.StartsWith("Granite_"))){
    var source=filter.sharedMesh;var verts=source.vertices;var tri=source.triangles;var keep=new List<int>();
    for(int i=0;i<tri.Length;i+=3){var aa=filter.transform.TransformPoint(verts[tri[i]]);var bb=filter.transform.TransformPoint(verts[tri[i+1]]);var cc=filter.transform.TransformPoint(verts[tri[i+2]]);var lo=Vector3.Min(aa,Vector3.Min(bb,cc));var hi=Vector3.Max(aa,Vector3.Max(bb,cc));
     bool overlap=joins.Any(p=>p.x+1.55f>=lo.x&&p.x-1.55f<=hi.x&&p.z+1.55f>=lo.z&&p.z-1.55f<=hi.z&&p.y+2.4f>=lo.y&&p.y+.15f<=hi.y);
     if(!overlap)keep.AddRange(new[]{tri[i],tri[i+1],tri[i+2]});}
    var clipped=Asset290("Meshes/ArtJoin_"+filter.transform.parent.name+"_"+filter.name+".asset",()=>new Mesh());EditorUtility.CopySerialized(source,clipped);clipped.triangles=keep.ToArray();clipped.RecalculateBounds();EditorUtility.SetDirty(clipped);filter.sharedMesh=clipped;
    if(filter.name=="LOD2")filter.transform.parent.GetComponent<MeshCollider>().sharedMesh=clipped;
   }
   // Replace the matching corridor, not a detached display scene or a parallel floating path.
   var inserted=Resample290(profile.points.Select(matrix.MultiplyPoint3x4).ToArray(),.55f);
   m.MainPath=oldPath.Take(first).Concat(inserted).Concat(oldPath.Skip(last+1)).ToArray();
   var mountain=GameObject.Find(m.Id).transform;
   foreach(string suffix in new[]{"_main","_inner_rock","_outer_apron"}){
    var go=mountain.Find(m.Id+suffix);if(go==null)continue;
    var filter=go.GetComponent<MeshFilter>();var original=Asset290("ArtBaseline/"+m.Id+suffix+".asset",()=>Object.Instantiate(filter.sharedMesh));
    var vertices=original.vertices;var indices=original.triangles;var kept=new List<int>();
    var segment=oldPath.Skip(first).Take(last-first+1).ToArray();
    for(int i=0;i<indices.Length;i+=3){var centre=(vertices[indices[i]]+vertices[indices[i+1]]+vertices[indices[i+2]])/3;
     var local=inverse.MultiplyPoint3x4(centre);float distance=Distance290(new Vector2(centre.x,centre.z),segment,out float pathY);
     bool replace=local.z>=from.z-.2f&&local.z<=to.z+.2f&&distance<(suffix=="_main"?6:30)&&Mathf.Abs(centre.y-pathY)<60;
     if(!replace){kept.Add(indices[i]);kept.Add(indices[i+1]);kept.Add(indices[i+2]);}}
    var mesh=Asset290("Meshes/Art_"+m.Id+suffix+".asset",()=>new Mesh());EditorUtility.CopySerialized(original,mesh);mesh.triangles=kept.ToArray();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);filter.sharedMesh=mesh;go.GetComponent<MeshCollider>().sharedMesh=mesh;
   }
   var height=AssetDatabase.LoadAssetAtPath<TerrainData>(A290+"/"+m.Id+"/Height.asset");var before=AssetDatabase.LoadAssetAtPath<TerrainData>(A290+"/"+m.Id+"/HeightBeforeArt.asset");
   var sourceHeight=AssetDatabase.LoadAssetAtPath<TerrainData>(A285+"/Mountain.asset");var terrain=mountain.GetComponentsInChildren<MeshCollider>().Single(c=>c.name.StartsWith("Terrain_"));var bounds=terrain.bounds;
   var rect=new Rect(bounds.min.x,bounds.min.z,bounds.size.x,bounds.size.z);int n=height.heightmapResolution;var h=before.GetHeights(0,0,n,n);var retained=oldPath.Take(first).Concat(oldPath.Skip(last+1)).Where((_,i)=>i%8==0).ToArray();var reliefRoute=inserted.Where((_,i)=>i%10==0).Append(inserted.Last()).ToArray();
   for(int z=0;z<n;z++)for(int x=0;x<n;x++){
    var world=new Vector3(rect.xMin+rect.width*x/(n-1),a.y,rect.yMin+rect.height*z/(n-1));var p=inverse.MultiplyPoint3x4(world);
    float edge=Mathf.Min(Mathf.Min(p.x+230,90-p.x),Mathf.Min(p.z+100,240-p.z));if(edge<=0)continue;
    float y=sourceHeight.GetInterpolatedHeight((p.x+650)/1200,(p.z+350)/1000)-140;
    float target=matrix.MultiplyPoint3x4(new Vector3(p.x,y,p.z)).y;
    float blend=Mathf.SmoothStep(0,1,edge/65);
    float retainedDistance=Distance290(new Vector2(world.x,world.z),retained,out _);
    blend*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(5,25,retainedDistance));
    h[z,x]=Mathf.Lerp(h[z,x],(target+200)/1200,blend);
    float clearance=Distance290(new Vector2(world.x,world.z),reliefRoute,out float tread);
    if(clearance<12){float cover=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(5,12,clearance));h[z,x]=Mathf.Lerp(h[z,x],Mathf.Min(h[z,x],(tread-.7f+200)/1200),cover);}
   }
   height.SetHeights(0,0,h);EditorUtility.SetDirty(height);Object.DestroyImmediate(terrain.gameObject);
   BuildTerrainMesh290(new MountainField290{Mountain=m,HeightData=height,Bounds=rect},mountain);
   var route=layout.Routes.Single(r=>r.Id==m.Id+"_upper");int junction=Array.FindIndex(m.MainPath,p=>Vector3.Distance(p,m.TemplePath[0])<.1f);
   route.Bends=m.MainPath.Skip(junction).Where((_,i)=>i%10==0).Append(m.MainPath.Last()).Select(p=>new Vector2(p.x,p.z)).ToArray();
   // Keep all access consumers on the same updated 3D corridor.
   m.VehicleExclusions=m.VehicleExclusions.Concat(inserted.Where((_,i)=>i%12==0).Select(p=>new CompactWorldLayoutSO.VehicleExclusion{Centre=p+Vector3.up*10,Size=new Vector3(22,26,22)})).GroupBy(v=>v.Centre).Select(g=>g.First()).ToArray();
   foreach(string suffix in new[]{"_inner_rock","_outer_apron"})mountain.Find(m.Id+suffix).gameObject.SetActive(false);
   TempleArt290(m);BridgeCollision290();
   EditorUtility.SetDirty(layout);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(O290+"/layout.json",JsonUtility.ToJson(layout,true));
   string report=$"Prototype geometry groups reused: {copied}; detailed segment {Length290(inserted):F1}m / total {Length290(m.MainPath):F1}m. Candidate lighting, camera renderer and post retained. Physics, map and navigation must be revalidated. Other mountain art remains blockout.";
   File.WriteAllText(O290+"/art-pass.txt",report);return report;
  }
  static string BridgeCollision290()
  {
   var scene=SceneManager.GetActiveScene();if(scene.path!=Scene290)throw new Exception("Candidate only");
   var root=GameObject.Find("Cheongrim_AssetPass_290").transform;var old=root.Find("ContinuousBoardwalkSupport");if(old!=null)Object.DestroyImmediate(old.gameObject);
   var profile=Profile285;var points=Resample290(new[]{profile.At(profile.bridgeStart-.18f),profile.At(profile.bridgeEnd+.18f)},.18f);
   var v=new List<Vector3>();var tri=new List<int>();
   for(int i=0;i<points.Length;i++){float s=Mathf.Lerp(profile.bridgeStart-.18f,profile.bridgeEnd+.18f,i/(float)(points.Length-1));var p=profile.At(s)-Vector3.up*.05f;var side=profile.Right(s)*(profile.Width(s)*.5f-.08f);v.Add(p-side);v.Add(p+side);if(i>0){int k=i*2;tri.AddRange(new[]{k-2,k,k+1,k-2,k+1,k-1});}}
   var mesh=Asset290("Meshes/ContinuousBoardwalkSupport.asset",()=>new Mesh());mesh.Clear();mesh.vertices=v.ToArray();mesh.triangles=tri.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
   var go=new GameObject("ContinuousBoardwalkSupport");go.transform.SetParent(root,false);go.AddComponent<MeshCollider>().sharedMesh=mesh;
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return "Continuous support 5 cm below boardwalk, matching route width; individual plank visuals retained";
  }
  static void TempleArt290(CompactWorldLayoutSO.Mountain m)
  {
   var parent=GameObject.Find("Compact_MountainContent_290").transform;var previous=parent.Find("AssetTempleRoof");if(previous!=null)Object.DestroyImmediate(previous.gameObject);
   var old=parent.Find("ArchiveRoof");if(old!=null)old.gameObject.SetActive(false);
   // Reuse an actual Korean tiled roof; the original source building and its materials stay immutable.
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HwaseongHaenggung/Prefabs/SM_Jibsacheong_1.prefab");
   var source=prefab.GetComponentInChildren<MeshFilter>();var mesh=source.sharedMesh;var v=mesh.vertices;float threshold=mesh.bounds.min.y+mesh.bounds.size.y*.55f;
   var roof=Asset290("Meshes/ArchiveTiledRoof.asset",()=>new Mesh());EditorUtility.CopySerialized(mesh,roof);
   for(int sub=0;sub<mesh.subMeshCount;sub++){var tri=mesh.GetTriangles(sub);var keep=new List<int>();for(int i=0;i<tri.Length;i+=3)if(v[tri[i]].y>threshold&&v[tri[i+1]].y>threshold&&v[tri[i+2]].y>threshold)keep.AddRange(new[]{tri[i],tri[i+1],tri[i+2]});roof.SetTriangles(keep,sub);}
   roof.RecalculateBounds();EditorUtility.SetDirty(roof);
   var go=new GameObject("AssetTempleRoof");go.transform.SetParent(parent);go.AddComponent<MeshFilter>().sharedMesh=roof;var renderer=go.AddComponent<MeshRenderer>();
   renderer.sharedMaterials=source.GetComponent<Renderer>().sharedMaterials.Select((mat,i)=>Asset290("Materials/ArchiveRoof_"+i+".mat",()=>{var copy=new Material(mat);if(copy.HasProperty("_BaseColor"))copy.SetColor("_BaseColor",new Color(.67f,.66f,.59f));copy.enableInstancing=true;return copy;})).ToArray();
   float factor=38/Mathf.Max(mesh.bounds.size.x,mesh.bounds.size.z);go.transform.localScale=Vector3.one*factor;go.transform.rotation=Quaternion.Euler(0,mesh.bounds.size.z>mesh.bounds.size.x?90:0,0);
   float lowest=Enumerable.Range(0,roof.subMeshCount).SelectMany(roof.GetTriangles).Select(i=>v[i].y).Min();
   var centre=go.transform.rotation*(mesh.bounds.center*factor);go.transform.position=m.Temple+new Vector3(-centre.x,4.25f-lowest*factor,20-centre.z);
   var previousDress=parent.Find("TempleStoneAndTimber");if(previousDress!=null)Object.DestroyImmediate(previousDress.gameObject);var detail=new GameObject("TempleStoneAndTimber");detail.transform.SetParent(parent,false);
   var rock=Import285("Scanned_boulder_LOD1");var stone=MountainMaterial290("cheongrim","Granite");var soil=MountainMaterial290("cheongrim","Rooted_soil");
   parent.Find("ArchiveFloor").GetComponent<Renderer>().sharedMaterial=soil;
   var rng=new System.Random(290);float Rand(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
   for(int i=0;i<54;i++){float x=i<27?-17:17,z=-9+(i%27)*1.32f;var rubble=MeshObject278("BuriedFoundationStone",rock,stone,detail.transform,false);rubble.transform.position=m.Temple+new Vector3(x+Rand(-.8f,.5f),-.17f,z+Rand(-.5f,.5f));rubble.transform.rotation=Quaternion.Euler(Rand(-12,12),Rand(0,360),Rand(-9,9));rubble.transform.localScale=new Vector3(Rand(.5f,1.1f),Rand(.35f,.75f),Rand(.5f,1.2f));}
   var timber=MountainMaterial290("cheongrim","Pier289_poles");
   foreach(float x in new[]{-15f,-5f,5f,15f}){var p=m.Temple+new Vector3(x,2.05f,13);var pole=MeshObject278("ArchiveTimberPost",Import285("Pier289_poles_0"),timber,detail.transform,false);pole.transform.position=p;pole.transform.localScale=new Vector3(.4f,4.1f,.4f);}
  }
  static string ArtCapture290()
  {
   if(SceneManager.GetActiveScene().path!=Scene290)throw new Exception("Candidate only");
   var root=GameObject.Find("Cheongrim_AssetPass_290").transform;var profile=Profile285;var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var m=session.MountainLayout.Mountains[0];
   var source=Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(c=>c.gameObject.scene==session.gameObject.scene&&c.cameraType==CameraType.Game);
   var go=new GameObject("Art290_ReviewCamera");var camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var art=Object.FindFirstObjectByType<CompactRebuildArtRenderer>();var previous=art.Observer;art.Observer=camera;
   var rt=new RenderTexture(1600,900,24);var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);var prior=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
   try{ShaderUtil.allowAsyncCompilation=false;camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=60;
    for(int i=0;i<4;i++){float s=i==0?7:i==1?40:72;var eye=root.TransformPoint(profile.At(s))+Vector3.up*1.7f;var target=root.TransformPoint(profile.At(s+9))+Vector3.up*1.3f;
     if(i==3){eye=m.Temple+new Vector3(0,6,-23);target=m.Temple+new Vector3(0,4,17);}
     camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));camera.Render();camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes(O290+"/art-"+i+".png",tex.EncodeToPNG());}
   }finally{art.Observer=previous;ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=prior;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
   return "Four actual candidate captures, same renderer and environment; three representative asset segment views and temple.";
  }
 }
}
