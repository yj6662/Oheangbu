using System;
using System.Linq;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string GukInputPrepare(){
   var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("-audit-")||!s.HasGuk)throw new Exception("Isolated unlocked save required");
   var rest=s.Content.Points.Single(p=>p.Id=="InnRest");s.Teleport(rest.Position+Vector3.up,0);s.Interact(rest.Id);
   var cc=s.Player.GetComponent<CharacterController>();s.Teleport(s.RevisitSite.LiftPad.position+Vector3.up*(cc.height*.5f-cc.center.y+.03f),0);return "At casting pad; ordinary rest restored ink. No letter or field cast injected.";
  }
  static string GukSite(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey required");
   var session=Object.FindFirstObjectByType<PrologueSession>();
   if(session.RevisitSite!=null)return "Existing revisit retained";
   var ground=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");
   if(!ground.Raycast(new Ray(new Vector3(-13,200,78),Vector3.down),out var hit,400))throw new Exception("No ledge ground");
   var root=new GameObject("Journey Guk Revisit").transform;var site=root.gameObject.AddComponent<DemoGukRevisitSite>();
   var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/PaleStone.mat");
   GameObject Block(string name,Vector3 center,Vector3 size){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root);go.transform.position=center;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;return go;}
   Transform Anchor(string name,Vector3 point){var go=new GameObject(name);go.transform.SetParent(root);go.transform.position=point;return go.transform;}
   var basePoint=hit.point+Vector3.up*.2f;
   Block("Flat casting stone",basePoint-Vector3.up*.15f,new Vector3(2.4f,.3f,2.4f));
   var upper=basePoint+new Vector3(0,2.35f,2.3f);
   Block("High stone shelf",new Vector3(upper.x,basePoint.y+1.175f,upper.z),new Vector3(3,2.35f,2.8f));
   site.LiftPad=Anchor("LiftPad",basePoint);site.UpperSurface=Anchor("UpperSurface",upper);site.DescentExit=Anchor("DescentExit",basePoint+Vector3.back*2);
   Place("High trail belongings","Assets/KoreanTraditionalFestival/Prefabs/SM_JolongtaegiBag.prefab",root,upper,.55f);
   session.RevisitSite=site;session.WoodLiftProfile=AssetDatabase.LoadAssetAtPath<Vfx120Profile>("Assets/_Project/Art/SpellVFX120/Profiles/020_AD6D.asset");if(session.WoodLiftProfile==null)throw new Exception("WoodLift profile missing");
   session.Content.Points=session.Content.Points.Concat(new[]{new PrologueContentSO.Point{Id=DemoGukRevisitSite.Id,Kind=PrologueInteractionKind.Currency,Position=upper,Radius=1.5f,Currency=40,RequiredDefeated=new[]{"cheongryong"},Prompt="보따리 살피기",Text="숲길 높은 바위에 남겨진 보따리다."}}).ToArray();
   EditorUtility.SetDirty(session);EditorUtility.SetDirty(session.Content);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Guk service profile and revisit ledge connected";
  }
 }
}
