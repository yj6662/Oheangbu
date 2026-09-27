using System;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string SouthGateAuthor(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey required");if(GameObject.Find("Journey South Gate")!=null)return "Existing south gate retained";
   var session=Object.FindFirstObjectByType<PrologueSession>();var ground=GameObject.Find("Capital road ground").GetComponent<MeshCollider>();
   if(!ground.Raycast(new Ray(new Vector3(270,150,-800),Vector3.down),out var anchor,300))throw new Exception("Capital ground missing");float level=anchor.point.y;
   var mesh=ground.sharedMesh;var v=mesh.vertices;for(int i=0;i<v.Length;i++){var p=v[i];float w=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(28,48,Mathf.Abs(p.x-270))))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(-800,-820,p.z));p.y=Mathf.Lerp(p.y,level,w);v[i]=p;}mesh.vertices=v;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);ground.sharedMesh=null;ground.sharedMesh=mesh;Physics.SyncTransforms();
   var root=new GameObject("Journey South Gate").transform;var destination=new Vector3(270,level,-875);PrologueEncounter boss=null;SouthGateDoorPresentation door=null;
   var source=EditorSceneManager.OpenScene("Assets/_Project/Scenes/World/W_Demo_Compact.unity",OpenSceneMode.Additive);
   try{
    var roots=source.GetRootGameObjects();var original=roots.SelectMany(g=>g.GetComponentsInChildren<SouthGateGeneralController>(true)).Single();var oldDoor=roots.SelectMany(g=>g.GetComponentsInChildren<SouthGateDoorPresentation>(true)).Single();var assembly=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="CAP_GATE_ARCH").parent;
    var pivot=oldDoor.transform.position;var rotation=Quaternion.FromToRotation(oldDoor.transform.forward,Vector3.back);
    GameObject Copy(GameObject old){var go=Object.Instantiate(old);SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(root);go.transform.SetPositionAndRotation(destination+rotation*(old.transform.position-pivot),rotation*old.transform.rotation);go.transform.localScale=old.transform.lossyScale;return go;}
    Copy(assembly.gameObject).name="Journey south gate architecture";door=Copy(oldDoor.gameObject).GetComponent<SouthGateDoorPresentation>();door.ConfigureSession(null);boss=Copy(original.gameObject).GetComponent<PrologueEncounter>();
   }finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(scene);}
   boss.gameObject.SetActive(false);boss.name="south_gate_general";boss.Id=boss.name;boss.Player=session.Player;boss.Session=session;boss.transform.SetPositionAndRotation(new Vector3(270,level+.875f,-850),Quaternion.identity);boss.PatrolPoints=new[]{boss.transform.position};boss.GetComponent<NavMeshAgent>().enabled=true;
   var general=boss.GetComponent<SouthGateGeneralController>();var profile=Object.Instantiate(general.Profile);profile.name="Journey south gate general";AssetDatabase.CreateAsset(profile,Folder+"/SouthGateGeneral.asset");
   var vitals=boss.GetComponent<EnemyVitals>();var sourceConfig=(CombatConfigSO)new SerializedObject(vitals).FindProperty("_config").objectReferenceValue;var config=Object.Instantiate(sourceConfig);config.name="Journey south gate combat";AssetDatabase.CreateAsset(config,Folder+"/SouthGateCombat.asset");
   void Set(Object o,string field,Object value){var so=new SerializedObject(o);so.FindProperty(field).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
   Set(vitals,"_config",config);var legacy=boss.GetComponent<EnemyController>();Set(legacy,"_config",config);Set(legacy,"_player",session.Player);Set(legacy,"_playerVitals",session.Player.GetComponent<PlayerVitals>());general.ConfigureProfile(profile,config);general.enabled=true;boss.enabled=true;
   session.Encounters=session.Encounters.Concat(new[]{boss}).ToArray();var wiring=new SerializedObject(session.Wiring);var enemies=wiring.FindProperty("_enemies");enemies.arraySize=session.Encounters.Length;for(int i=0;i<session.Encounters.Length;i++)enemies.GetArrayElementAtIndex(i).objectReferenceValue=session.Encounters[i].GetComponent<EnemyVitals>();wiring.ApplyModifiedPropertiesWithoutUndo();
   session.Content.EncounterRules=session.Content.EncounterRules.Concat(new[]{new PrologueContentSO.EncounterRule{Id=boss.Id,RequiredCompleted=new[]{"cargo_delivery"},PersistentDefeat=true,OverrideDefeatReward=true,DefeatReward=120}}).ToArray();
   var rest=new Vector3(251,level,-817);if(ground.Raycast(new Ray(rest+Vector3.up*100,Vector3.down),out var restHit,200))rest=restHit.point;session.Content.Points=session.Content.Points.Concat(new[]{new PrologueContentSO.Point{Id="SouthGateRest",Kind=PrologueInteractionKind.Rest,Position=rest,Radius=3,RequiredCompleted=new[]{"cargo_delivery"}}}).ToArray();
   var marker=GameObject.CreatePrimitive(PrimitiveType.Cylinder);marker.name="South gate resting stone";marker.transform.SetParent(root);marker.transform.position=rest+Vector3.right*1.3f+Vector3.up*.25f;marker.transform.localScale=new Vector3(1,.25f,1);marker.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/PaleStone.mat");
   var bridge=door.gameObject.AddComponent<JourneyVictoryGate>();bridge.Session=session;bridge.Door=door;var box=door.GetComponent<BoxCollider>();var obstacle=door.gameObject.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.center=box.center;obstacle.size=box.size;obstacle.carving=true;bridge.NavigationObstacle=obstacle;
   Physics.SyncTransforms();var surface=Object.FindFirstObjectByType<NavMeshSurface>();var old=surface.navMeshData;surface.BuildNavMesh();var next=surface.navMeshData;if(next==null)throw new Exception("Navigation bake failed");surface.RemoveData();EditorUtility.CopySerialized(next,old);surface.navMeshData=old;surface.AddData();EditorUtility.SetDirty(old);Object.DestroyImmediate(next);
   if(!NavMesh.SamplePosition(boss.transform.position,out _,3,NavMesh.AllAreas))throw new Exception("General lacks navigation");EditorUtility.SetDirty(session);EditorUtility.SetDirty(session.Content);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Owned gate architecture, persistent general, 120 reward, rest and victory-door binding authored";
  }
  static string SouthGateWalls(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey required");if(GameObject.Find("Journey City Wall")!=null)return "Existing city wall retained";
   var ground=GameObject.Find("Capital road ground").GetComponent<MeshCollider>();var door=Object.FindFirstObjectByType<JourneyVictoryGate>().Door;float half=door.GetComponent<BoxCollider>().size.x*.5f;var root=new GameObject("Journey City Wall").transform;root.SetParent(GameObject.Find("Journey South Gate").transform);
   Bounds BoundsOf(GameObject go){var rs=go.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
   float Height(float x){if(!ground.Raycast(new Ray(new Vector3(x,150,-875),Vector3.down),out var hit,300))throw new Exception("Wall support absent "+x);return hit.point.y;}
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HwaseongForteressGate/Prefabs/SM_FortificationWall.prefab");
   foreach(var range in new[]{new Vector2(80,270-half),new Vector2(270+half,460)}){
    int count=Mathf.CeilToInt((range.y-range.x)/8);float width=(range.y-range.x)/count;
    for(int i=0;i<count;i++){
     float x=range.x+(i+.5f)*width;float left=Height(range.x+i*width+.01f),right=Height(range.x+(i+1)*width-.01f);float bottom=Mathf.Min(left,right)-.4f,height=12+Mathf.Abs(right-left);
     var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);go.name="City curtain wall "+range.x+" "+i;foreach(var c in go.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);var bounds=BoundsOf(go);go.transform.localScale=Vector3.Scale(go.transform.localScale,new Vector3((width+.02f)/bounds.size.x,height/bounds.size.y,4/bounds.size.z));bounds=BoundsOf(go);go.transform.position+=new Vector3(x,bottom,-875)-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);bounds=BoundsOf(go);var box=go.AddComponent<BoxCollider>();box.center=go.transform.InverseTransformPoint(bounds.center);box.size=new Vector3(bounds.size.x/go.transform.lossyScale.x,bounds.size.y/go.transform.lossyScale.y,bounds.size.z/go.transform.lossyScale.z);
    }
   }
   Physics.SyncTransforms();var surface=Object.FindFirstObjectByType<NavMeshSurface>();var previous=surface.navMeshData;surface.BuildNavMesh();var next=surface.navMeshData;surface.RemoveData();EditorUtility.CopySerialized(next,previous);surface.navMeshData=previous;surface.AddData();EditorUtility.SetDirty(previous);Object.DestroyImmediate(next);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
   int samples=0,gaps=0;for(float x=81;x<460;x+=2){if(Mathf.Abs(x-270)<half+.4f)continue;samples++;if(!Physics.SphereCast(new Vector3(x,Height(x)+1.2f,-870),.35f,Vector3.back,out var hit,10,1,QueryTriggerInteraction.Ignore)||!hit.transform.IsChildOf(root))gaps++;}string report="City wall samples="+samples+" gaps="+gaps+"; source prefab reused; navigation rebaked";File.WriteAllText("../Art/World/PineRest/south_gate_wall_checks.txt",report);return report;
  }
  static string SouthGateBindings(){
   if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=Scene||SceneManager.GetActiveScene().isDirty)throw new Exception("Saved Journey required");var s=Object.FindFirstObjectByType<PrologueSession>();var actor=s.Encounters.Single(x=>x.Id=="south_gate_general");string before="actor="+actor.enabled+" general="+actor.GetComponent<SouthGateGeneralController>().enabled;actor.enabled=true;actor.GetComponent<SouthGateGeneralController>().enabled=true;actor.GetComponent<NavMeshAgent>().enabled=true;EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return before+"; Journey owns enabled encounter/controller";
  }
  static string SouthGateView(){var s=RoadRestSession();var door=Object.FindFirstObjectByType<JourneyVictoryGate>().Door;s.Teleport(door.transform.position+Vector3.forward*44+Vector3.up*1.1f,180);return "At gate approach view; audit teleport only";}
  static string SouthGateGrounding(){
   if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=Scene||SceneManager.GetActiveScene().isDirty)throw new Exception("Saved Journey required");
   var s=Object.FindFirstObjectByType<PrologueSession>();var p=s.Content.Points.Single(x=>x.Id=="SouthGateRest");var ground=GameObject.Find("Capital road ground").GetComponent<MeshCollider>();if(!ground.Raycast(new Ray(p.Position+Vector3.up*100,Vector3.down),out var hit,200))throw new Exception("Rest support absent");p.Position=hit.point;GameObject.Find("South gate resting stone").transform.position=p.Position+Vector3.right*1.3f+Vector3.up*.25f;EditorUtility.SetDirty(s.Content);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return "South gate rest grounded at "+p.Position;
  }
  static string SouthGateSurvey(){
   var current=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||current.path!=Scene||current.isDirty)throw new Exception("Saved Journey required");var source=EditorSceneManager.OpenScene("Assets/_Project/Scenes/World/W_Demo_Compact.unity",OpenSceneMode.Additive);
   try{var roots=source.GetRootGameObjects();var boss=roots.SelectMany(g=>g.GetComponentsInChildren<SouthGateGeneralController>(true)).Single();var door=roots.SelectMany(g=>g.GetComponentsInChildren<SouthGateDoorPresentation>(true)).Single();var arch=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="CAP_GATE_ARCH");var r=arch.parent.GetComponentsInChildren<Renderer>(true);var b=r[0].bounds;foreach(var item in r.Skip(1))b.Encapsulate(item.bounds);var report="boss="+boss.transform.position+" scale="+boss.transform.lossyScale+" gate="+door.transform.position+" scale="+door.transform.lossyScale+" forward="+door.transform.forward+" assembly="+arch.parent.name+" bounds="+b+" children="+string.Join(",",arch.parent.Cast<Transform>().Select(t=>t.name));File.WriteAllText("../Art/World/PineRest/south_gate_survey.txt",report);return report;}
   finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(current);}
  }
 }
}
