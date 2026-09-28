using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.Data.World;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string O293="../Art/World/Compact/Rebuild/Highlands293";
  [Serializable] sealed class HighlandPacket293 {public Highland293[] Modules;public string[] Sections;}
  [Serializable] sealed class Highland293
  {
   public string Id,Realm;public Vector3 A,B,Centre;public float Sign,Width,Vertical,SourceLength;public Vector3[] Path;
   public Vector3 Map(Vector3 p)
   {
    var sf=new Vector2(5,94).normalized;var sr=new Vector2(sf.y,-sf.x);var d=new Vector2(p.x-8,p.z+4);
    float u=Vector2.Dot(d,sf),v=Vector2.Dot(d,sr);var target=new Vector2(B.x-A.x,B.z-A.z);var tf=target.normalized;var tr=new Vector2(tf.y,-tf.x);
    var xz=new Vector2(A.x,A.z)+tf*u*target.magnitude/SourceLength+tr*v*Sign*Width;
    float y=A.y+(p.y-18)*Vertical+u/SourceLength*(B.y-A.y-21*Vertical);return new Vector3(xz.x,y,xz.y);
   }
  }
  static Vector3? eye293,target293;static string output293;
  static string Cleanup293()
  {
   int count=0;foreach(string path in Directory.GetDirectories(A292+"/Highlands293"))
   {string name=Path.GetFileName(path);if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"^(Materials|mountain_(cheongrim|jeokro|cheolong|hyeongang|hwanggyeong)) [0-9]+$"))continue;
    if(Directory.EnumerateFileSystemEntries(path).Any())continue;if(AssetDatabase.DeleteAsset(path.Replace('\\','/')))count++;}
   // Numbered twins of the Highlands293 folder itself were left beside it by an earlier deferred import.
   var twins=Directory.GetDirectories(A292).Select(p=>p.Replace('\\','/')).Where(p=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),@"^Highlands293 [0-9]+$")&&!Directory.EnumerateFileSystemEntries(p).Any()).ToArray();
   var failed=new List<string>();if(twins.Length>0)AssetDatabase.DeleteAssets(twins,failed);count+=twins.Length-failed.Count;
   return "Removed "+count+" empty folders from deferred import; populated assets retained"+(failed.Count>0?"; failed="+failed.Count:"");
  }
  static string Capture293(int index)
  {
   int k=index%5;var modules=JsonUtility.FromJson<HighlandPacket293>(File.ReadAllText(O293+"/modules.json")).Modules;
   var module=modules.FirstOrDefault(m=>m.Realm.Equals(RealmOrder293[k],StringComparison.OrdinalIgnoreCase));
   if(module==null)
   {
    // sectioned realm: its B section (or its first), trail view up / whole mountain
    var ids=SectionIds293();int own=Array.FindIndex(ids,x=>SectionMeta293(x).realm.Equals(RealmOrder293[k],StringComparison.OrdinalIgnoreCase)&&SectionMeta293(x).kind=="B");
    if(own<0)own=Array.FindIndex(ids,x=>SectionMeta293(x).realm.Equals(RealmOrder293[k],StringComparison.OrdinalIgnoreCase));
    return CaptureSections293(own+(index<5?":0":":5"));
   }
   eye293=index<5?module.Map(Profile285.At(10))+Vector3.up*1.7f:module.Map(new Vector3(-95,110,-90));
   target293=index<5?module.Map(Profile285.At(32))+Vector3.up*1.7f:module.Map(Profile285.At(55))+Vector3.up*25;
   output293=O293+(index<5?"/trail-":"/overview-")+k+".png";
   try{Capture292(k+6);return output293;}finally{eye293=null;target293=null;output293=null;}
  }
  static string Themes293()
  {
   var session=Session292();var layout=session.MountainLayout;var root=GameObject.Find("Highlands293");if(root==null)throw new Exception("Assemble highlands first");
   var field=new CompactWorldSurface(layout);var modules=JsonUtility.FromJson<HighlandPacket293>(File.ReadAllText(O293+"/modules.json")).Modules;var rows=new List<string>();
   var catalogs=session.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>(true)).Select(a=>a.Catalog)
    .Concat(session.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldRealmAtmosphere>(true)).Select(a=>a.Catalog))
    .Concat(session.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Select(a=>a.MapData.Locations)).Where(c=>c!=null).Distinct();
   foreach(var catalog in catalogs)
   {if(!AssetDatabase.GetAssetPath(catalog).StartsWith(A292+"/"))throw new Exception("Shared location catalog cannot be edited");
    foreach(var area in layout.Realms){var entry=catalog.Entries.FirstOrDefault(e=>e.Priority==0&&e.Name==area.Label);if(entry==null)throw new Exception("Missing realm "+area.Label);entry.Centre=field.Point(area.Centre);entry.Polygon=area.Polygon;}
    foreach(var entry in catalog.Entries.Where(e=>e.Priority>0)){var area=layout.RealmAt(new Vector2(entry.Centre.x,entry.Centre.z));if(area!=null)entry.RealmId=catalog.Entries.First(e=>e.Priority==0&&e.Name==area.Label).Id;}
    EditorUtility.SetDirty(catalog);
   }
   for(int k=0;k<modules.Length;k++)
   {
    var m=modules[k];int r=Array.FindIndex(RealmOrder293,x=>x.Equals(m.Realm,StringComparison.OrdinalIgnoreCase));
    var parent=root.transform.Find(m.Id);var old=parent.Find("RegionalDetails");if(old!=null)Object.DestroyImmediate(old.gameObject);
    var group=new GameObject("RegionalDetails").transform;group.SetParent(parent,false);
    var timber=Asset292("Highlands293/Materials/Theme_"+r+".mat",()=>new Material(AssetDatabase.LoadAssetAtPath<Material>(A285+"/Materials/Pier289_poles.mat")));
    timber.SetColor("_BaseColor",new[]{new Color(.57f,.48f,.35f),new Color(.24f,.20f,.17f),new Color(.43f,.42f,.38f),new Color(.40f,.46f,.43f),new Color(.60f,.55f,.44f)}[r]);EditorUtility.SetDirty(timber);
    var rock=AssetDatabase.LoadAssetAtPath<Material>(A292+"/Materials/KoreanGround.mat");
    var pole=AssetDatabase.LoadAssetAtPath<Mesh>(A285+"/Meshes/Pier289_poles_0.asset");
    if(r==0||r==1)
    {
     // Felled/cut timber and locally charred remains stand off the walking surface.
     for(int i=0;i<5;i++){var p=m.Map(Profile285.At(31+i*.7f)-Profile285.Right(31+i*.7f)*3.8f);p.y=field.Sample(p.x,p.z)+.2f;
      var go=new GameObject(r==0?"Cut_timber":"Charred_timber");go.transform.SetParent(group,false);go.transform.position=p;go.transform.rotation=Quaternion.Euler(82+i*3,r*29+i*17,0);go.transform.localScale=new Vector3(.28f,1.8f+i*.15f,.28f);go.AddComponent<MeshFilter>().sharedMesh=pole;go.AddComponent<MeshRenderer>().sharedMaterial=timber;}
    }
    if(r==2)
    {
     var iron=Asset292("Highlands293/Materials/Iron_repairs.mat",()=>new Material(Shader.Find("Universal Render Pipeline/Lit")));iron.SetColor("_BaseColor",new Color(.15f,.16f,.15f));iron.SetFloat("_Smoothness",.13f);EditorUtility.SetDirty(iron);
     for(int i=0;i<5;i++){var p=m.Map(Profile285.At(54+i)-Profile285.Right(54+i)*1.05f)-Vector3.up*.10f;Box290("Iron_repair_strap",group,p,new Vector3(.12f,.09f,.5f),iron);}
    }
    if(r==4)
    {
     var stone=AssetDatabase.LoadAssetAtPath<Mesh>(A285+"/Meshes/Scanned_boulder_LOD2.asset");
     for(int i=0;i<8;i++){var p=m.Map(Profile285.At(75+i*.75f)-Profile285.Right(75+i*.75f)*3.7f);p.y=field.Sample(p.x,p.z)-.10f;var go=new GameObject("Old_path_stone");go.transform.SetParent(group,false);go.transform.position=p;go.transform.localScale=new Vector3(.40f,.25f,.38f);go.AddComponent<MeshFilter>().sharedMesh=stone;go.AddComponent<MeshRenderer>().sharedMaterial=rock;}
    }
    int clear=0,total=0;
    foreach(var p in m.Path.Where((_,i)=>i%18==0)){total++;var hits=Physics.RaycastAll(p+Vector3.up*3,Vector3.down,5).Where(h=>h.collider.transform.IsChildOf(root.transform)||h.collider.transform.root.name=="Reworld292_Terrain").OrderBy(h=>h.distance).ToArray();if(hits.Length>0&&Mathf.Abs(hits[0].point.y-p.y)<.7f)clear++;}
    rows.Add(m.Id+" unobstructed walking-surface rays="+clear+"/"+total+"; manual traversal and performance pending");
   }
   Physics.SyncTransforms();Save292();File.WriteAllText(O293+"/surface-checks.txt",string.Join("\n",rows));return string.Join("\n",rows);
  }
  static string Highlands293()
  {
   RequireClean292();var session=Session292();var layout=session.MountainLayout;var scene=session.gameObject.scene;
   if(layout.Realms.Length!=5)throw new Exception("Load 293 surface/layout before assembling highlands");
   var packet=JsonUtility.FromJson<HighlandPacket293>(File.ReadAllText(O293+"/modules.json"));
   var old=GameObject.Find("Highlands293");if(old!=null)Object.DestroyImmediate(old);
   var root=new GameObject("Highlands293");root.AddComponent<CompactMountainAccess>().Layout=layout;
   var sourceScene=EditorSceneManager.OpenScene(Scene285,OpenSceneMode.Additive);
   var source=sourceScene.GetRootGameObjects().Single(g=>g.name=="Granite_Trail_285");
   var selected=source.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Granite_face_")||t.name.StartsWith("Granite_foundation_")||t.name.StartsWith("Carved_trail_")||t.name.StartsWith("Trail_shoulder_")||t.name=="Timber_bridge"||t.name.StartsWith("Outer_bedrock_")||t.name.StartsWith("Ledge_pine_")||t.name.StartsWith("Pine_root_rock")).ToArray();
   var rows=new List<string>();var mats=new Dictionary<string,Material>();
   Material MaterialFor(Material sourceMat,Highland293 module)
   {
    if(sourceMat==null)return null;string key=module.Realm+"_"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sourceMat));
    if(mats.TryGetValue(key,out var m))return m;
    m=Asset292("Highlands293/Materials/"+key+".mat",()=>new Material(sourceMat));
    var realm=layout.Realms.First(r=>r.Id.Equals(module.Realm,StringComparison.OrdinalIgnoreCase));
    // Retain source albedo/normal/woodgrain; pigment differences remain restrained.
    if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",Color.Lerp(new Color(.81f,.81f,.77f),realm.Tint,.20f));
    if(m.HasProperty("_Saturation"))m.SetFloat("_Saturation",.18f);
    if(m.HasProperty("_WashStrength"))m.SetFloat("_WashStrength",0);
    if(m.HasProperty("_AmbientFloor"))m.SetFloat("_AmbientFloor",.36f);
    if(m.HasProperty("_PaperStrength"))m.SetFloat("_PaperStrength",.08f);
    // Shared 285 granite stays untouched; highland copies dissolve into the realm ground wash at range.
    if(sourceMat.shader.name=="Oheangbu/Prototype/Granite285")
    {m.shader=AssetDatabase.LoadAssetAtPath<Shader>(A292+"/Shaders/HighlandGranite293.shader");
     m.SetColor("_FarTone",Color.Lerp(new Color(.76f,.74f,.68f),realm.Tint,.18f));m.SetVector("_FarRange",new Vector4(60,420,0,0));m.SetFloat("_FarStrength",.42f);}
    m.enableInstancing=true;EditorUtility.SetDirty(m);mats[key]=m;return m;
   }
   try
   {
    AssetDatabase.StartAssetEditing();
    foreach(var module in packet.Modules)
    {
     var parent=new GameObject(module.Id);SceneManager.MoveGameObjectToScene(parent,scene);parent.transform.SetParent(root.transform,false);int serial=0,colliders=0;
     foreach(var template in selected)
     {
      var clone=Object.Instantiate(template.gameObject);SceneManager.MoveGameObjectToScene(clone,scene);clone.transform.SetParent(parent.transform,true);
      var groups=clone.GetComponentsInChildren<LODGroup>(true);
      var filters=clone.GetComponentsInChildren<MeshFilter>(true);var matrices=filters.Select(f=>f.transform.localToWorldMatrix).ToArray();
      var sourceColliders=clone.GetComponentsInChildren<MeshCollider>(true).Select(c=>(Mesh:c.sharedMesh,Matrix:c.transform.localToWorldMatrix)).ToArray();
      foreach(var c in clone.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
      Mesh Warp(Mesh original,Matrix4x4 matrix,string suffix)
      {
       var mesh=Asset292("Highlands293/"+module.Id+"/"+suffix+".asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;
       mesh.vertices=original.vertices.Select(v=>module.Map(matrix.MultiplyPoint3x4(v))).ToArray();mesh.uv=original.uv;mesh.subMeshCount=original.subMeshCount;
       for(int sm=0;sm<original.subMeshCount;sm++){var indices=original.GetTriangles(sm);if(module.Sign<0)for(int i=0;i<indices.Length;i+=3)(indices[i+1],indices[i+2])=(indices[i+2],indices[i+1]);mesh.SetTriangles(indices,sm);}
       mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
      }
      for(int i=0;i<filters.Length;i++)
      {
       var f=filters[i];if(f.sharedMesh==null)continue;var mesh=Warp(f.sharedMesh,matrices[i],"render_"+serial++);
       f.transform.SetParent(parent.transform,false);f.transform.localPosition=Vector3.zero;f.transform.localRotation=Quaternion.identity;f.transform.localScale=Vector3.one;f.sharedMesh=mesh;
       var renderer=f.GetComponent<MeshRenderer>();renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>MaterialFor(m,module)).ToArray();
       if(f.name=="Timber_tread"){f.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;colliders++;}
      }
      foreach(var c in sourceColliders)
      {
       if(c.Mesh==null)continue;var go=new GameObject("IndependentCollision");go.transform.SetParent(parent.transform,false);go.AddComponent<MeshCollider>().sharedMesh=Warp(c.Mesh,c.Matrix,"collision_"+serial++);colliders++;
      }
      foreach(var group in groups)group.RecalculateBounds();
     }
     rows.Add(module.Id+": copied/deformed source meshes="+serial+" independent colliders="+colliders);
    }
   }
   finally{AssetDatabase.StopAssetEditing();EditorSceneManager.CloseScene(sourceScene,true);SceneManager.SetActiveScene(scene);}
   // One region authority feeds map labels/discovery and the atmosphere's location lookup.
   foreach(var catalog in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>(true)).Select(a=>a.Catalog)
    .Concat(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldRealmAtmosphere>(true)).Select(a=>a.Catalog))
    .Concat(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Select(a=>a.MapData.Locations)).Where(c=>c!=null).Distinct())
   {
    foreach(var area in layout.Realms)
    {var entry=catalog.Entries.FirstOrDefault(e=>e.Priority==0&&string.Equals(e.RealmId,area.Id,StringComparison.OrdinalIgnoreCase))??catalog.Entries.FirstOrDefault(e=>e.Priority==0&&e.Name==area.Label);
     if(entry==null){entry=new WorldLocationCatalog.Entry{Id=area.Id.ToLowerInvariant(),RealmId=area.Id.ToLowerInvariant(),Priority=0};catalog.Entries=catalog.Entries.Concat(new[]{entry}).ToArray();}
     entry.Name=area.Label;entry.Centre=new Vector3(area.Centre.x,0,area.Centre.y);entry.Polygon=area.Polygon;entry.MinimumY=-1000;entry.MaximumY=2000;entry.FloorPath=Array.Empty<Vector3>();}
    foreach(var entry in catalog.Entries.Where(e=>e.Priority>0)){var realm=layout.RealmAt(new Vector2(entry.Centre.x,entry.Centre.z));if(realm!=null)entry.RealmId=catalog.Entries.First(e=>e.Priority==0&&e.Name==realm.Label).Id;}
    EditorUtility.SetDirty(catalog);
   }
   foreach(var ui in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)))
   {var map=ui.MapData;map.Revision="reworld-293";foreach(var area in layout.Realms){var zone=map.Zones.FirstOrDefault(z=>string.Equals(z.Id,area.Id,StringComparison.OrdinalIgnoreCase)||z.Label==area.Label);if(zone!=null)zone.Polygon=area.Polygon;}EditorUtility.SetDirty(map);}
   rows.AddRange(Sections293(root.transform,layout));
   Physics.SyncTransforms();
   foreach(var module in packet.Modules)
   {
    int hit=0,total=0;foreach(var p in module.Path.Where((_,i)=>i%18==0)){total++;if(Physics.RaycastAll(p+Vector3.up*2,Vector3.down,4).Any(h=>h.collider.transform.IsChildOf(root.transform)&&Mathf.Abs(h.point.y-p.y)<.7f))hit++;}
    rows.Add(module.Id+" downward walking-surface rays="+hit+"/"+total+" (fixture only, not traversal)");
   }
   Save292();File.WriteAllText(O293+"/unity-checks.txt",string.Join("\n",rows));return string.Join("\n",rows);
  }
 }
}
