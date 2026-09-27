using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static GameObject Box290(string name,Transform parent,Vector3 centre,Vector3 size,Material material,bool collision=true)
  {
   var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,true);
   go.transform.position=centre;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
   if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
  }
  static string BuildContent290()
  {
   var scene=SceneManager.GetActiveScene();if(scene.path!=Scene290)throw new Exception("Mountain candidate only");
   var roots=scene.GetRootGameObjects();var s=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
   var layout=s.MountainLayout;if(layout==null||layout.Mountains.Length!=5)throw new Exception("Build geometry first");
   var previous=GameObject.Find("Compact_MountainContent_290");if(previous!=null)Object.DestroyImmediate(previous);
   var root=new GameObject("Compact_MountainContent_290");
   var points=s.Content.Points.Where(p=>!p.Id.StartsWith("mountain_")).ToList();
   var visuals=s.InteractionVisuals.Where(v=>!v.Id.StartsWith("mountain_")).ToList();
   var checkpoints=s.Content.Checkpoints.Where(c=>!c.Id.StartsWith("mountain_")).ToList();
   // Build the optional Cheongrim experience first. Other temples stay explicitly unimplemented.
   var m=layout.Mountains.Single(v=>v.Realm=="cheongrim");var t=m.Temple;
   var stone=MountainMaterial290(m.Realm,"Granite");var timber=MountainMaterial290(m.Realm,"Pier289_planks");
   var actions=new List<CompactWorldLayoutSO.MountainAction>();
   GameObject Point(string suffix,Vector3 feet,string title,string text,int reward=0,string[] requires=null,string record=null)
   {
    string id=m.Id+"_"+suffix;var go=new GameObject(id);go.transform.SetParent(root.transform);go.transform.position=feet;
    var prop=Box290("Weathered_"+suffix,go.transform,feet+Vector3.up*.45f,new Vector3(.72f,.9f,.5f),suffix.Contains("winch")?timber:stone);
    var cp=go.AddComponent<WorldMacroContentPoint>();cp.Id=id;cp.Visual=prop.transform;
    var action=new CompactWorldLayoutSO.MountainAction{Id=id,Title=title,Text=text,Reward=reward,RecordId=record,RequiredCompleted=requires??Array.Empty<string>()};actions.Add(action);
    points.Add(new PrologueContentSO.Point{Id=id,Position=feet,Radius=2.4f,Kind=PrologueInteractionKind.Evidence,Prompt="",Text=text,RequiredCompleted=action.RequiredCompleted});
    visuals.Add(new WorldMacroPlaytestSession.InteractionVisual{Id=id,Renderers=go.GetComponentsInChildren<Renderer>()});return go;
   }
   // A traversable open courtyard, two damaged service galleries, and an inner archive.
   Box290("ArchiveFloor",root.transform,t+new Vector3(0,-.17f,9),new Vector3(36,.24f,38),stone);
   Box290("WestWall",root.transform,t+new Vector3(-18,2,9),new Vector3(.9f,4.3f,39),stone);
   Box290("EastWallNorth",root.transform,t+new Vector3(18,2,15),new Vector3(.9f,4.3f,26),stone);
   Box290("EastWallSouth",root.transform,t+new Vector3(18,2,-7),new Vector3(.9f,4.3f,6),stone);
   Box290("RearWall",root.transform,t+new Vector3(0,2,28),new Vector3(36,4.3f,.9f),stone);
   foreach(float x in new[]{-10f,10f})Box290("ArchivePartition",root.transform,t+new Vector3(x,2,13),new Vector3(16,4.3f,.9f),stone);
   foreach(float x in new[]{-13f,13f})for(int i=0;i<4;i++)
   {
    var p=t+new Vector3(x,0,-7+i*5);Box290("GalleryPost",root.transform,p+Vector3.up*1.8f,new Vector3(.28f,3.6f,.3f),timber);
    var beam=Box290("BrokenGalleryBeam",root.transform,p+new Vector3(x<0?1.2f:-1.2f,3.5f,.1f),new Vector3(3.8f,.25f,.35f),timber,false);beam.transform.Rotate(0,i*3-4,i==2?8:0);
   }
   // Reuse the Korean temple roof as a roof only: the interior remains walkable.
   var roofMesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Project/Art/CodexWorld/Meshes/Inn_RoofSurface.asset");
   var roofMat=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/RoofTiles.mat");
   if(roofMesh!=null&&roofMat!=null)
   {
    var roof=MeshObject278("ArchiveRoof",roofMesh,roofMat,root.transform,false);var b=roofMesh.bounds;
    roof.transform.localScale=new Vector3(39/b.size.x,5/Mathf.Max(.1f,b.size.y),19/b.size.z);
    roof.transform.position=t+new Vector3(0,4.1f,21)-Vector3.Scale(b.center,roof.transform.localScale)+Vector3.up*2.5f;
   }
   string brace=m.Id+"_brace",winch=m.Id+"_winch",archive=m.Id+"_archive_open";
   Point("brace",t+new Vector3(-12,0,-5),"무너진 버팀목","낡은 장부의 짜임대로 버팀목을 끼웠다. 느슨했던 줄이 다시 팽팽해졌다.");
   Point("winch",t+new Vector3(12,0,2),"권양기","흙에 묻힌 축을 돌리자 안쪽의 빗장이 들린다.",0,new[]{brace});
   Point("archive_open",t+new Vector3(0,0,11),"기록당 빗장","목재 틈에 걸린 빗장을 풀었다.",0,new[]{winch});
   Point("temple",t+new Vector3(0,0,23),"봉산의 장부","반출 장부에는 나무의 수가 남았으나 심은 이들의 이름은 지워져 있다. 마지막 장에는 산 위 감시소의 인장이 찍혀 있다.",160,new[]{archive},"record.mountain_cheongrim_temple");
   Point("summit",m.Summit+new Vector3(2,0,0),"봉산 감시 기록","계곡의 마차 수를 적던 기록이다. 마지막 줄은 돌아오지 않은 빈 수레 세 대에서 멎어 있다.",120,null,"record.mountain_cheongrim_summit");
   void Gate(string name,Vector3 feet,string required,Vector3 size)
   {
    var panel=Box290(name,root.transform,feet+Vector3.up*1.8f,size,timber);var gate=panel.AddComponent<CompactMountainGate>();
    gate.Session=s;gate.Panel=panel.transform;gate.RequiredCompleted=required;gate.OpenOffset=new Vector3(0,-4,0);gate.Blockers=panel.GetComponents<Collider>();
    var obstacle=panel.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.size=Vector3.one;obstacle.carving=true;gate.Obstacle=obstacle;
   }
   Gate("ArchiveDoor",t+new Vector3(0,0,13),archive,new Vector3(3.8f,3.6f,.55f));
   var returnPoint=m.ReturnPath[24];Gate("ReturnGate",returnPoint,m.TempleRewardId,new Vector3(4,3.6f,.6f));
   // A visual cue is readable from the fork; a second cue is an intermittent spatial bell.
   var fork=m.TemplePath[0];var lantern=Box290("BrokenStoneLantern",root.transform,fork+new Vector3(-1.6f,.65f,0),new Vector3(.6f,1.3f,.6f),stone);lantern.transform.Rotate(0,22,12);
   var bell=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Mountain290/TempleBell.wav");
   if(bell!=null){var cue=new GameObject("DistantTempleBell").AddComponent<CompactMountainBell>();cue.transform.SetParent(root.transform);cue.transform.position=t+Vector3.up*4;cue.Clip=bell;}
   m.Actions=actions.ToArray();
   // Reuse existing sanctuary retry semantics, not a new respawn system.
   var rest=m.Summit+new Vector3(-8,0,-7);string restId=m.Id+"_rest";
   var restGo=Box290("SummitSanctuary",root.transform,rest+Vector3.up*.45f,new Vector3(.9f,.9f,.7f),stone);restGo.AddComponent<WorldMacroContentPoint>().Id=restId;
   points.Add(new PrologueContentSO.Point{Id=restId,Kind=PrologueInteractionKind.Rest,Position=rest,Radius=2.6f});
   visuals.Add(new WorldMacroPlaytestSession.InteractionVisual{Id=restId,Renderers=restGo.GetComponents<Renderer>()});
   checkpoints.Add(new WorldMacroPlaytestSO.CheckpointSpec{Id=restId,Label=m.Label,Feet=rest+Vector3.back*2,Yaw=0});
   s.Content.Points=points.ToArray();s.InteractionVisuals=visuals.ToArray();s.Content.Checkpoints=checkpoints.ToArray();
   UpdateMountainMap290(scene.GetRootGameObjects(),layout);
   foreach(var o in new Object[]{s,s.Content,layout})EditorUtility.SetDirty(o);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(O290+"/layout.json",JsonUtility.ToJson(layout,true));
   return "Cheongrim devices, archive gate, independent 160/120 rewards, return gate and sanctuary authored. Other four temple gameplay/bosses remain pending. Temple duration and visual quality unverified.";
  }
  static void UpdateMountainMap290(GameObject[] roots,CompactWorldLayoutSO layout)
  {
   var map=roots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single().MapData;
   var cat=roots.SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>(true)).Single().Catalog;
   var entries=cat.Entries.Where(e=>!e.Id.StartsWith("mountain_")).ToList();var markers=map.Markers.Where(e=>!e.Id.StartsWith("mountain_")).ToList();
   foreach(var m in layout.Mountains.Where(v=>v.Actions.Length>0))
   {
    foreach(var pair in new[]{(m.Id+"_summit",m.Summit,m.Label),(m.Id+"_temple",m.Temple,"숨은 절")})
    {
     entries.Add(new WorldLocationCatalog.Entry{Id=pair.Item1,Name=pair.Item3,RealmId="realm_"+m.Realm,MarkerId=pair.Item1,Centre=pair.Item2,Radius=32,MinimumY=pair.Item2.y-3,MaximumY=pair.Item2.y+10,Priority=35});
     markers.Add(new WorldMapMarkerSpec{Id=pair.Item1,Label=pair.Item3,Kind=pair.Item1.EndsWith("summit")?WorldMapMarkerKind.Mountain:WorldMapMarkerKind.Place,WorldXZ=new Vector2(pair.Item2.x,pair.Item2.z),RequiresArrival=true,CompletionId=pair.Item1});
    }
   }
   cat.Entries=entries.ToArray();map.Markers=markers.ToArray();map.Revision="mountains-290-v1";EditorUtility.SetDirty(cat);EditorUtility.SetDirty(map);
  }
 }
}
