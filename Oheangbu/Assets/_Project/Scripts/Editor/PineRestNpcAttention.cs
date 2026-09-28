using System;
using System.Linq;
using Oheangbu.App.Prologue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string NpcAttention(){
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey edit required");
   var root=GameObject.Find("Jeongdam appearance");if(root==null)throw new Exception("Jeongdam model missing");
   string path=Folder+"/JeongdamAttention.asset";var profile=AssetDatabase.LoadAssetAtPath<JourneyNpcAttentionProfileSO>(path);
   if(profile==null){profile=ScriptableObject.CreateInstance<JourneyNpcAttentionProfileSO>();AssetDatabase.CreateAsset(profile,path);}
   profile.AfterReportPointId="ForestRest";profile.StopAfterDefeatedId="cheongryong";EditorUtility.SetDirty(profile);
   var view=root.GetComponent<JourneyNpcAttention>()??root.AddComponent<JourneyNpcAttention>();view.Session=UnityEngine.Object.FindFirstObjectByType<PrologueSession>();view.Profile=profile;view.CommissionId="j1";EditorUtility.SetDirty(view);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Jeongdam visual attention connected; collision/quest data unchanged";
  }
  static string NpcAttentionState(bool interact){
   var session=RoadRestSession();var view=UnityEngine.Object.FindFirstObjectByType<JourneyNpcAttention>();if(view==null)throw new Exception("Attention component missing");
   if(interact)session.Interact("jeongdam_j1");
   var evidence=session.Content.Points.Single(p=>p.Id=="LoggingEvidence").Position-view.transform.position;evidence.y=0;
   var player=session.Player.position-view.transform.position;player.y=0;
   return "engaged="+view.Engaged+"; guiding="+view.Guiding+"; playerAngle="+Vector3.Angle(view.transform.forward,player)+"; evidenceAngle="+Vector3.Angle(view.transform.forward,evidence)+"; guideTarget="+view.LastGuideTargetId+"; accepted="+session.Progress.completed.Contains("j1:accepted")+"; npcPosition="+view.transform.position;
  }
 }
}
