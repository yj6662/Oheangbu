using System;
using System.Linq;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static double motionAuditUntil;
  static EnemyController motionAuditEnemy;
  static Transform motionAuditVisual;
  static bool sawWindup,sawRecovery,sawPose;
  static string MotionAudit(){
   var session=Object.FindFirstObjectByType<PrologueSession>();
   if(!EditorApplication.isPlaying||session==null||!session.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");
   motionAuditEnemy=session.Encounters[0].GetComponent<EnemyController>();motionAuditVisual=motionAuditEnemy.transform.Find("Journey enemy visual");
   var cc=session.Player.GetComponent<CharacterController>();cc.enabled=false;session.Player.position=motionAuditEnemy.transform.position-Vector3.forward*2;cc.enabled=true;
   sawWindup=sawRecovery=sawPose=false;motionAuditUntil=EditorApplication.timeSinceStartup+10;
   EditorApplication.update-=ObserveMotion;EditorApplication.update+=ObserveMotion;
   return "Live AI motion observation running for ten seconds; setup teleport only, no injected attacks";
  }
  static void ObserveMotion(){
   if(!EditorApplication.isPlaying||motionAuditEnemy==null){EditorApplication.update-=ObserveMotion;return;}
   sawWindup|=motionAuditEnemy.IsTelegraphing;sawRecovery|=motionAuditEnemy.IsRecovering;
   sawPose|=Quaternion.Angle(motionAuditVisual.localRotation,Quaternion.identity)>8;
   if(EditorApplication.timeSinceStartup<motionAuditUntil)return;
   EditorApplication.update-=ObserveMotion;
   string result="liveWindup="+sawWindup+" liveRecovery="+sawRecovery+" visiblePose="+sawPose;
   System.IO.File.WriteAllText("../Art/World/PineRest/enemy_motion_check.txt",result);
  }
  static string EnemyMotion(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey edit required");
   int count=0;
   foreach(var actor in Object.FindFirstObjectByType<PrologueSession>().Encounters){
    var visual=actor.transform.Find("Journey enemy visual");if(visual==null)throw new Exception("Visual missing");
    var motion=actor.GetComponent<StoneEnemyMotion>()??actor.gameObject.AddComponent<StoneEnemyMotion>();
    motion.Enemy=actor.GetComponent<EnemyController>();motion.Visual=visual;EditorUtility.SetDirty(motion);count++;
   }
   EditorSceneManager.SaveScene(scene);return count+" rigid stone motion drivers connected; combat remains unchanged";
  }
  static string HudSkin(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey edit required");
   const string source="Assets/_Project/Art/World/WorldMacro/Playtest/HUD/WorldMacroHudSkinProfile.asset";
   string target=Folder+"/HudSkin.asset";
   if(AssetDatabase.LoadMainAssetAtPath(target)==null&&!AssetDatabase.CopyAsset(source,target))throw new Exception("HUD skin copy failed");
   var skin=AssetDatabase.LoadAssetAtPath<Oheangbu.App.World.WorldMacroHudSkinProfileSO>(target);
   if(skin==null||skin.LockRing==null)throw new Exception("HUD ring asset missing");
   var hud=Object.FindFirstObjectByType<Oheangbu.App.HudController>();
   var serialized=new SerializedObject(hud);serialized.FindProperty("Skin").objectReferenceValue=skin;serialized.ApplyModifiedPropertiesWithoutUndo();
   PrefabUtility.RecordPrefabInstancePropertyModifications(hud);EditorUtility.SetDirty(hud);
   EditorSceneManager.SaveScene(scene);return "Journey owns copied HUD skin; existing brush ring and resource art connected.";
  }
  static string ArtProps(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey edit required");
   var session=Object.FindFirstObjectByType<PrologueSession>();
   foreach(var actor in session.Encounters){
    if(actor.transform.Find("Journey enemy visual")!=null)continue;
    string path="Assets/_Project/Art/SpellVFX120/StoneDokkaebi/PF_StoneDokkaebi_Static.prefab";
    var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(source==null)throw new Exception("Existing model missing");
    var visual=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);visual.name="Journey enemy visual";visual.transform.SetParent(actor.transform,false);
    var meshes=visual.GetComponentsInChildren<Renderer>();if(meshes.Length==0)throw new Exception("Model has no renderers");
    var bounds=meshes[0].bounds;foreach(var r in meshes.Skip(1))bounds.Encapsulate(r.bounds);float scale=1.9f/Mathf.Max(.01f,bounds.size.y);visual.transform.localScale*=scale;
    bounds=meshes[0].bounds;foreach(var r in meshes.Skip(1))bounds.Encapsulate(r.bounds);visual.transform.position+=new Vector3(actor.transform.position.x-bounds.center.x,actor.transform.position.y-1-bounds.min.y,actor.transform.position.z-bounds.center.z);
    foreach(var collider in visual.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
    var controller=new SerializedObject(actor.GetComponent<EnemyController>());controller.FindProperty("_renderer").objectReferenceValue=meshes[0];controller.ApplyModifiedPropertiesWithoutUndo();
    var primitive=actor.GetComponent<MeshRenderer>();if(primitive!=null)Object.DestroyImmediate(primitive);var filter=actor.GetComponent<MeshFilter>();if(filter!=null)Object.DestroyImmediate(filter);
   }
   // Existing prop source, fitted to the interaction anchor; no new shared art is generated.
   var satchel=GameObject.Find("Worker satchel");if(satchel!=null){var anchor=satchel.transform.position-Vector3.up*.22f;var parent=satchel.transform.parent;Object.DestroyImmediate(satchel);Place("Worker belongings", "Assets/KoreanTraditionalFestival/Prefabs/SM_JolongtaegiBag.prefab",parent,anchor,.55f);}
   var ledger=GameObject.Find("Logging ledger");if(ledger!=null){var anchor=ledger.transform.position-Vector3.up*.2f;var parent=ledger.transform.parent;Object.DestroyImmediate(ledger);Place("Logging evidence box","Assets/HwaseongHaenggung/Prefabs/SM_M_WoodenBox.prefab",parent,anchor,.65f);}
   EditorSceneManager.SaveScene(scene);return "Four enemy visuals replaced with existing stone creature model; satchel and ledger props replaced. Original hit colliders retained; final motion/visual review pending.";
  }
  static void Place(string name,string path,Transform parent,Vector3 feet,float height){
   if(parent.Find(name)!=null)return;var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab==null)throw new Exception("Prop missing "+path);var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.name=name;go.transform.localRotation=Quaternion.identity;
   var renderers=go.GetComponentsInChildren<Renderer>();var b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);go.transform.localScale*=height/Mathf.Max(.01f,b.size.y);b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);go.transform.position+=feet-new Vector3(b.center.x,b.min.y,b.center.z);
   foreach(var c in go.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
  }
 }
}
