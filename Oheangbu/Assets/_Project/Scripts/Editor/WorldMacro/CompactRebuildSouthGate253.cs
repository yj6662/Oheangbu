using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string GateOutput253=Output+"/SouthGate253",GateRoot253="CapitalSouthGate253";
  static void MeshCollision253(Transform root)
  {
   foreach(var c in root.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
   var lod=root.GetComponent<LODGroup>();
   var renderers=lod!=null?lod.GetLODs()[0].renderers:root.GetComponentsInChildren<Renderer>(true);
   foreach(var r in renderers)
   {
    var f=r.GetComponent<MeshFilter>();if(f==null||f.sharedMesh==null)continue;
    var c=r.gameObject.AddComponent<MeshCollider>();c.sharedMesh=f.sharedMesh;
   }
  }
  static void FixDeliveryFrontage253(WorldMacroPlaytestSession s,Scene scene)
  {
   var ground=FinalSurface(scene);var route=scene.GetRootGameObjects().Single(g=>g.name==EscortRoot252).GetComponent<DemoEscortSceneRoute>();
   var stop=route.Stops.Single(p=>p.Id=="cargo_delivery");var center=stop.Parking.position;
   var incomingRight=Vector3.Cross(Vector3.up,stop.Parking.forward);var house=stop.Interaction.parent.Find("Guesthouse252");
   if(Vector3.Dot(house.position-center,incomingRight)>0)
   {
    foreach(Transform child in stop.Interaction.parent)
    {
     var p=child.position;p-=incomingRight*(2*Vector3.Dot(p-center,incomingRight));
     p.y=ground(p.x,p.z).point.y+(child.position.y-ground(child.position.x,child.position.z).point.y);
     child.position=p;
    }
   }
   var clerk=stop.Interaction.parent.Find("CargoClerk252");clerk.rotation=Quaternion.LookRotation(incomingRight);
   MeshCollision253(house);
   foreach(var p in s.Content.Points){if(p.Id==stop.Id)p.Position=stop.Interaction.position;if(p.Id==stop.CheckpointId)p.Position=stop.Checkpoint.position;}
   s.Content.Checkpoints.Single(c=>c.Id==stop.CheckpointId).Feet=stop.Checkpoint.position;
  }
  public static string SouthGateGroundStops253()
  {
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Clean candidate required");
   var route=Object.FindFirstObjectByType<DemoEscortSceneRoute>();var stop=route.Stops.Single(p=>p.Id=="cargo_delivery");var ground=FinalSurface(scene);
   var lines=new List<string>();var used=new List<Vector3>();
   foreach(var node in new[]{stop.CompanionWait,stop.CargoWait})
   {
    var candidates=new List<Vector3>();var original=node.position;
    for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)
    {
     var p=ground(original.x+x*.75f,original.z+z*.75f).point;
     if(Vector3.Distance(p,stop.Parking.position)>7||used.Any(v=>Vector3.Distance(v,p)<.9f))continue;
     if(!NavMesh.SamplePosition(p,out var nav,.35f,NavMesh.AllAreas)||Vector3.Distance(p,nav.position)>.3f)continue;
     if(Physics.OverlapCapsule(p+Vector3.up*.4f,p+Vector3.up*1.45f,.27f,~0,QueryTriggerInteraction.Ignore).Any(c=>c.gameObject.scene==scene&&!c.name.StartsWith("Terrain_")))continue;
     candidates.Add(p);
    }
    if(candidates.Count==0)throw new Exception("No physically grounded stop near "+node.name);
    node.position=candidates.OrderBy(p=>Vector3.Distance(original,p)).First();used.Add(node.position);lines.Add(node.name+" "+original.ToString("F3")+" -> "+node.position.ToString("F3"));
   }
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText(GateOutput253+"/stop-grounding.txt",string.Join("\n",lines));return string.Join("\n",lines);
  }
  public static string SouthGateBuild253()
  {
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Save unrelated scene changes before gate authoring");
   var s=VillageSession();var ground=FinalSurface(scene);var roots=scene.GetRootGameObjects();
   Directory.CreateDirectory(GateOutput253);var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
   var place=manifest.Layout.Places.Single(p=>p.Id=="south_gate");var feet=ground(place.XZ.x,place.XZ.y).point;
   var destination=ground(place.XZ.x,place.XZ.y+25).point;
   FixDeliveryFrontage253(s,scene);
   var old=roots.SingleOrDefault(g=>g.name==GateRoot253);if(old!=null)Object.DestroyImmediate(old);
   var root=new GameObject(GateRoot253).transform;PrologueEncounter actor=null;SouthGateDoorPresentation door=null;
   var source=EditorSceneManager.OpenScene(Scene,OpenSceneMode.Additive);
   try
   {
    var originals=source.GetRootGameObjects();var original=originals.SelectMany(g=>g.GetComponentsInChildren<SouthGateGeneralController>(true)).Single();
    var originalDoor=originals.SelectMany(g=>g.GetComponentsInChildren<SouthGateDoorPresentation>(true)).Single();
    var arch=originals.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="CAP_GATE_ARCH").parent;
    // Opposite directions have an ambiguous FromToRotation axis; preserve world-up.
    var pivot=originalDoor.transform.position;var rotation=Quaternion.AngleAxis(Vector3.SignedAngle(originalDoor.transform.forward,Vector3.forward,Vector3.up),Vector3.up);
    GameObject Copy(GameObject from)
    {
     var copy=Object.Instantiate(from);SceneManager.MoveGameObjectToScene(copy,scene);copy.transform.SetParent(root);
     copy.transform.SetPositionAndRotation(destination+rotation*(from.transform.position-pivot),rotation*from.transform.rotation);copy.transform.localScale=from.transform.lossyScale;return copy;
    }
    var architecture=Copy(arch.gameObject);architecture.name="OriginalCompactGateArchitecture";MeshCollision253(architecture.transform);
    door=Copy(originalDoor.gameObject).GetComponent<SouthGateDoorPresentation>();door.name="VictoryGate253";door.ConfigureSession(s);
    actor=Copy(original.gameObject).GetComponent<PrologueEncounter>();actor.name=WorldMacroPlaytestSession.SouthGateGeneralId;
   }
   finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(scene);}
   var agent=actor.GetComponent<NavMeshAgent>();agent.enabled=false;actor.Session=null;actor.Player=s.Walker.Body.transform;
   actor.transform.SetPositionAndRotation(feet+Vector3.up*agent.baseOffset,Quaternion.Euler(0,180,0));actor.PatrolPoints=new[]{actor.transform.position};actor.Leash=24;actor.DetectionRange=20;actor.gameObject.SetActive(true);
   s.Actors=s.Actors.Where(a=>a!=null&&a.Id!=actor.Id).Concat(new[]{actor}).ToArray();
   s.Content.Encounters=s.Content.Encounters.Where(e=>e.Id!=actor.Id).Concat(new[]{new WorldMacroPlaytestSO.Encounter{Id=actor.Id,ContentId=actor.Id,Feet=feet,Patrol=new[]{feet},Speed=actor.Speed,Ranged=actor.Ranged,Detection=20,Leash=24,Activation=120,RespawnOnRest=false}}).ToArray();
   if(!s.ConfigureDemoSouthGate(actor.GetComponent<SouthGateGeneralController>()))throw new Exception("Gate actor not registered in candidate");
   var wiring=new SerializedObject(s.Walker.Wiring);var enemies=wiring.FindProperty("_enemies");enemies.arraySize=s.Actors.Length;
   for(int i=0;i<s.Actors.Length;i++)enemies.GetArrayElementAtIndex(i).objectReferenceValue=s.Actors[i].GetComponent<EnemyVitals>();wiring.ApplyModifiedPropertiesWithoutUndo();
   var box=door.GetComponent<BoxCollider>();if(box==null)throw new Exception("Canonical closed gate collider missing");
   var modifier=door.GetComponent<NavMeshModifier>();if(modifier==null)modifier=door.gameObject.AddComponent<NavMeshModifier>();modifier.ignoreFromBuild=true;
   var obstacle=door.GetComponent<NavMeshObstacle>();if(obstacle==null)obstacle=door.gameObject.AddComponent<NavMeshObstacle>();
   obstacle.shape=NavMeshObstacleShape.Box;obstacle.center=box.center;obstacle.size=box.size;obstacle.carving=true;obstacle.carveOnlyStationary=false;door.ConfigureNavigation(new[]{obstacle});
   // Terrain-following threshold is also represented by the route ledger/map.
   var approach=new List<Vector3>();for(int i=0;i<=37;i++)approach.Add(ground(feet.x,feet.z+i).point);
   string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/SouthGate253";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
   var meshPath=folder+"/GateApproach.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,meshPath);}
   var vv=new List<Vector3>();var tt=new List<int>();var uv=new List<Vector2>();
   for(int i=0;i<approach.Count;i++)
   {
    for(int j=0;j<9;j++){vv.Add(ground(feet.x+j-4,feet.z+i).point+Vector3.up*.03f);uv.Add(new Vector2(j,i));}
    if(i>0)for(int j=0;j<8;j++){int k=i*9+j;tt.AddRange(new[]{k-9,k,k+1,k-9,k+1,k-8});}
   }
   mesh.Clear();mesh.SetVertices(vv);mesh.SetUVs(0,uv);mesh.SetTriangles(tt,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
   var road=new GameObject("south_gate_threshold253",typeof(MeshFilter),typeof(MeshRenderer));road.transform.SetParent(root);road.GetComponent<MeshFilter>().sharedMesh=mesh;road.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Path.GetDirectoryName(folder).Replace('\\','/')+"/Escort252/RoadSoil.asset");
   manifest.Layout.Places=manifest.Layout.Places.Where(p=>p.Id!="south_gate_inside253").Concat(new[]{new CompactWorldLayoutSO.Place{Id="south_gate_inside253",Realm="Hwanggyeong",Label="황경 남문 안",Purpose="개문 후 통행",XZ=new Vector2(feet.x,feet.z+37),HasSourceBinding=true,SceneRoots=new[]{GateRoot253+"/south_gate_threshold253"}}}).ToArray();
   manifest.Layout.Routes=manifest.Layout.Routes.Where(r=>r.Id!=road.name).Concat(new[]{new CompactWorldLayoutSO.Route{Id=road.name,From="south_gate",To="south_gate_inside253",Role=CompactRouteRole.Main,Width=8}}).ToArray();
   place.HasSourceBinding=true;place.SceneRoots=new[]{GateRoot253};place.EncounterIds=new[]{actor.Id};
   var map=roots.Where(g=>g!=null).SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single().MapData;
   map.Lines=map.Lines.Where(l=>l.Id!=road.name).Concat(new[]{new WorldMapLineSpec{Id=road.name,Kind=WorldMapLineKind.Trail,Points=approach.Select(p=>new Vector2(p.x,p.z)).ToArray()}}).ToArray();
   var geography=s.DemoEscortSummon.WorldSheet;
   geography.Routes=geography.Routes.Where(r=>r.Id!=road.name).Concat(new[]{new WorldMacroSheetSO.RouteSpec{Id=road.name,From="south_gate",To="south_gate_inside253",Points=approach.ToArray(),Width=8,Carriage=false}}).ToArray();
   string originalPath=folder+"/OriginalMainPath253.json";if(!File.Exists(originalPath))File.WriteAllText(originalPath,JsonUtility.ToJson(new VillageLedger245{main=s.Content.MainPath}));
   s.Content.MainPath=JsonUtility.FromJson<VillageLedger245>(File.ReadAllText(originalPath)).main.Concat(approach).ToArray();
   foreach(var stage in s.Content.Campaign.Stages)if(stage.Id=="south_gate"||stage.Id=="ending"){stage.Implemented=true;stage.Destination=stage.Id=="south_gate"?feet:destination;}
   foreach(var o in new Object[]{s,s.Content,s.Content.Campaign,manifest.Layout,map,geography})EditorUtility.SetDirty(o);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(Output+"/layout.json",JsonUtility.ToJson(manifest.Layout,true));File.WriteAllText(Output+"/Cartography/map.json",JsonUtility.ToJson(map,true));
   File.WriteAllText(GateOutput253+"/build.txt","Canonical gate/general cloned into candidate; delivery frontage relocated with mesh collision; two stages wired. Fresh NavMesh and runtime verification pending. Boss="+feet+" gate="+destination);
   return File.ReadAllText(GateOutput253+"/build.txt");
  }
 }
}
