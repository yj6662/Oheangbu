using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
 [Serializable] class CaveLayoutData {
 public Vector3 origin,start,evidence,satchel;
 public Vector3[] main,branch,recess,lights,mouth;
 public WorldMacroNaturalCave.CaveMesh[] meshes;
 }
 static string CaveLayoutBuild(){
 if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
 var data=JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CaveV4/geometry.json"));
 var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
 string folder=Path.GetDirectoryName(receipt.scene).Replace('\\','/')+"/CaveV4";DevSceneKit.EnsureFolder(folder);
 var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
 try {
 var roots=scene.GetRootGameObjects();var mine=roots.Single(g=>g.name=="mine");
 var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
 var ui=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();var content=session.Content;
 var previous=mine.transform.Find("Cave_V4_Dressing");if(previous!=null)Object.DestroyImmediate(previous.gameObject);
 var baffle=mine.transform.Find("Cave_Intro_Outcrop");if(baffle!=null)Object.DestroyImmediate(baffle.gameObject);
 var dressing=new GameObject("Cave_V4_Dressing").transform;dressing.SetParent(mine.transform,false);
 var lampSurface=mine.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f=>f.name=="AssetSurface_0"&&Vector3.Distance(f.GetComponent<Renderer>().bounds.center,new Vector3(3528.7f,139,1803))<2);
 var lampParts=lampSurface!=null?lampSurface.transform.parent.GetComponentsInChildren<MeshFilter>(true):Array.Empty<MeshFilter>();
 var lampCenter=lampParts.Length>0?lampParts[0].GetComponent<Renderer>().bounds.center:Vector3.zero;
 var sourceLight=mine.GetComponentsInChildren<Light>(true).FirstOrDefault(l=>l.transform.position.x>3450);
 var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single().Art;
 var timber=art.Prototypes.First(p=>p.Id.Contains("WoodLog")).Lods[0].Parts[0].Material;
 foreach(var record in data.meshes){
 var target=mine.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name==record.name);
 var mesh=new Mesh{name=record.name+"_V4",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
 mesh.vertices=record.vertices.Select(v=>target.transform.InverseTransformPoint(data.origin+v)).ToArray();mesh.triangles=record.triangles;mesh.RecalculateNormals();mesh.uv=mesh.vertices.Select(v=>new Vector2(v.x,v.z)*.2f).ToArray();mesh.RecalculateBounds();mesh.RecalculateTangents();
 string meshPath=folder+"/"+record.name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
 if(existing==null)AssetDatabase.CreateAsset(mesh,meshPath);else{EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);}
target.sharedMesh=mesh;target.GetComponent<MeshCollider>().sharedMesh=mesh;
 }
 ClipOldPortalToGallery(mine.transform,data,folder);
 // Retire consolidated old-gallery dressing, not the approach or the clue models.
 foreach(var t in mine.GetComponentsInChildren<Transform>(true).ToArray())
 if(t.name.StartsWith("Mine_Detail_")||t.name.StartsWith("Ore_Seam_"))t.gameObject.SetActive(false);
 foreach(var l in mine.GetComponentsInChildren<Light>(true))if(l.transform.position.x>3435&&l.transform.position.z<1854)l.enabled=false;
 foreach(var f in mine.GetComponentsInChildren<MeshFilter>(true))
 if(f.name.StartsWith("AssetSurface_")&&f.GetComponent<Renderer>().bounds.center.y>138&&f.GetComponent<Renderer>().bounds.center.x>3435&&f.GetComponent<Renderer>().bounds.center.z<1854)f.GetComponent<Renderer>().enabled=false;
 for(int i=0;i<data.lights.Length;i++){
 var anchor=data.lights[i]+new Vector3(1.6f,3.3f,0);var group=new GameObject("Gallery_Lantern_"+i).transform;group.SetParent(dressing,false);group.position=anchor;
 foreach(var source in lampParts){var g=new GameObject(source.name);g.transform.SetParent(group,false);g.transform.position=anchor+(source.transform.position-lampCenter);g.transform.rotation=source.transform.rotation;g.transform.localScale=source.transform.lossyScale;g.AddComponent<MeshFilter>().sharedMesh=source.sharedMesh;g.AddComponent<MeshRenderer>().sharedMaterials=source.GetComponent<MeshRenderer>().sharedMaterials;}
 if(sourceLight!=null){var light=Object.Instantiate(sourceLight,group);light.gameObject.SetActive(true);light.enabled=true;light.transform.position=anchor+Vector3.down*.25f;light.range=9;light.intensity=sourceLight.intensity;}
 if(timber!=null){
 void Beam(string name,Vector3 p,Vector3 scale){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(group,false);g.transform.position=p;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=timber;}
 Beam("Upright",anchor+new Vector3(.75f,-1.5f,0),new Vector3(.22f,3.6f,.24f));Beam("LampBracket",anchor+new Vector3(.3f,.22f,0),new Vector3(1.3f,.18f,.20f));
 }
 AlignGalleryLantern(group,data,i,mine.transform);
 }
 void MovePoint(string id,Vector3 position,string visualName){
 var p=content.Points.Single(v=>v.Id==id);var delta=position-p.Position;
 var visual=mine.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==visualName);
 if(visual!=null)visual.position+=delta;
 foreach(var cp in roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroContentPoint>(true)).Where(cp=>cp.Id==id))if(visual==null||!cp.transform.IsChildOf(visual))cp.transform.position+=delta;
 p.Position=position;
 }
 MovePoint("worker_satchel",data.satchel,"Worker_Straw_Bag");
 MovePoint("mine_inquiry",data.evidence,"Mine_Investigation_Clue");
 // Some original clue meshes have no semantic parent; move the remaining small visual at its former location.
 foreach(var f in mine.GetComponentsInChildren<MeshFilter>(true))if(f.name=="AssetSurface_0"&&Vector3.Distance(f.GetComponent<Renderer>().bounds.center,new Vector3(3520,135.96f,1800))<.9f)f.transform.position+=data.evidence-new Vector3(3520,135.86f,1800);
 content.BranchPath=data.branch;
 const string toolId="mine_tool_marks";
 var tool=new Oheangbu.Data.World.PrologueContentSO.Point{Id=toolId,Kind=Oheangbu.Data.World.PrologueInteractionKind.Evidence,Position=data.recess.Last(),Prompt="부러진 운반 상자",Text="똑같은 금표가 찍힌 상자들. 빈 상자 바닥에 탄 심지가 눌어붙었다.\n\n바퀴 자국은 작업장을 거쳐 바깥 운반 갱도로 이어진다.",Radius=2.5f};
 content.Points=content.Points.Where(p=>p.Id!=toolId).Concat(new[]{tool}).ToArray();
 var crate=GameObject.CreatePrimitive(PrimitiveType.Cube);crate.name="Broken_Work_Crate";crate.transform.SetParent(dressing,false);crate.transform.position=tool.Position+Vector3.up*.38f;crate.transform.localScale=new Vector3(1.4f,.7f,.9f);if(timber!=null)crate.GetComponent<Renderer>().sharedMaterial=timber;crate.AddComponent<WorldMacroContentPoint>().Id=toolId;
 content.StartFeet=data.start;content.StartYaw=285;content.SaveSlot="world-demo-compact-cave-v4";
 var checkpoint=content.Checkpoints.Single(c=>c.Id=="mine_start");checkpoint.Feet=data.start;checkpoint.Yaw=285;
 var outside=content.MainPath.Where(p=>p.x<3430||p.z>1857).ToArray();content.MainPath=data.main.Concat(outside).ToArray();
 int enemyIndex=0;
 foreach(var e in content.Encounters.Where(e=>e.Feet.x>3420&&e.Feet.z<1857)){
 var feet=enemyIndex++%2==0?data.main[4]:data.recess[2];e.Feet=feet;e.Patrol=new[]{feet,feet+Vector3.right*1.5f};
 var actor=session.Actors.FirstOrDefault(a=>a.Id==e.Id);if(actor!=null){actor.transform.position=e.Feet;actor.PatrolPoints=e.Patrol;}
 }
 var body=session.Walker.Body;body.enabled=false;body.transform.SetPositionAndRotation(data.start,Quaternion.Euler(0,285,0));body.enabled=true;
 var sheet=ui.WorldSheet;sheet.Routes[0].Points=content.MainPath;EditorUtility.SetDirty(sheet);
 var map=ui.MapData;map.Revision="compact-cave-v4";map.Lines.First(l=>l.Id=="mine_inn").Points=content.MainPath.Select(p=>new Vector2(p.x,p.z)).ToArray();
 WorldMapLineSpec Line(string id,Vector3[] points)=>new WorldMapLineSpec{Id=id,Kind=WorldMapLineKind.DetailFill,PixelWidth=20,Points=points.Select(p=>new Vector2(p.x,p.z)).ToArray()};
 map.Zones=new[]{new WorldMapZoneSpec{Id="mine_interior",Label="폐광",MinimumY=130,MaximumY=150,Polygon=new[]{new Vector2(3423,1760),new Vector2(3565,1760),new Vector2(3565,1857),new Vector2(3423,1857)},DetailPath=data.main.Select(p=>new Vector2(p.x,p.z)).ToArray(),DetailLines=new[]{Line("haul_gallery",data.main),Line("evidence_loop",data.branch),Line("tool_bay",data.recess)}}};
 var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
 var floor=mine.GetComponentsInChildren<MeshCollider>(true).Single(c=>c.name=="Natural_Cave_Floor");
 var removed=manifest.Art.FixedPlacements.Where(p=>manifest.Art.Prototypes.Single(t=>t.Id==p.PrototypeId).Category!=Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Prop&&BelowGalleryRoof(floor,p.Position)).Select(p=>p.Id).ToHashSet();
 manifest.Art.FixedPlacements=manifest.Art.FixedPlacements.Where(p=>!removed.Contains(p.Id)).ToArray();
 foreach(var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>removed.Contains(t.name)).ToArray())Object.DestroyImmediate(t.gameObject);
 EditorUtility.SetDirty(manifest.Art);EditorUtility.SetDirty(content);EditorUtility.SetDirty(map);Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
 string result="Cave V4: connected main + evidence loop + tool bay; independent meshes; baffle removed; map/content/actors/checkpoint/road updated; slot="+content.SaveSlot+"; lamps="+data.lights.Length+"; displaced vegetation="+removed.Count;
 File.WriteAllText(Output+"/CaveV4/build.txt",result);return result;
 }finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
 }
 }
}
