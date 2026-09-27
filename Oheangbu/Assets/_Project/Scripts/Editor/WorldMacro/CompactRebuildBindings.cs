using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] public sealed class BindingObject
  {public string path;public Vector3 position;public string[] components;public int children;}
  [Serializable] public sealed class BindingSurvey
  {public string scene,content;public Vector3 start;public BindingObject[] objects;public PrologueContentSO.Point[] points;public WorldMacroPlaytestSO.Encounter[] encounters;public WorldMacroPlaytestSO.CheckpointSpec[] checkpoints;}
  static string HierarchyPath(Transform t)=>t.parent==null?t.name:HierarchyPath(t.parent)+"/"+t.name;
  static string Bindings()
  {
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=Scene)throw new Exception("Compact edit required");
   var transforms=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
   var session=transforms.Select(t=>t.GetComponent<WorldMacroPlaytestSession>()).Single(s=>s!=null);var content=session.Content;
   var objects=transforms.Where(t=>t.parent==null||t.parent.parent==null||t.parent.parent.parent==null||t.GetComponents<MonoBehaviour>().Any(c=>c!=null&&(c.GetType().Namespace??"").StartsWith("Oheangbu"))).Select(t=>new BindingObject{path=HierarchyPath(t),position=t.position,children=t.childCount,components=t.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType().FullName).ToArray()}).ToArray();
   var survey=new BindingSurvey{scene=scene.path,content=AssetDatabase.GetAssetPath(content),start=content.StartFeet,objects=objects,points=content.Points,encounters=content.Encounters,checkpoints=content.Checkpoints};
   File.WriteAllText(Output+"/bindings.json",JsonUtility.ToJson(survey,true));return "Read-only binding survey: "+objects.Length+" objects, "+content.Points.Length+" interactions, "+content.Encounters.Length+" encounters";
  }
 }
}
