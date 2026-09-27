using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using Oheangbu.Data.Demo;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  const string VillageOutput=Output+"/Village245";
  public static string VillageSurvey(){
   var scene=SceneManager.GetActiveScene();var all=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).ToArray();
   var s=all.Select(x=>x.GetComponent<WorldMacroPlaytestSession>()).First(x=>x!=null);
   var text="Scene "+scene.path+"\nContent "+AssetDatabase.GetAssetPath(s.Content)+"\nCampaign "+AssetDatabase.GetAssetPath(s.Content.Campaign)+"\n";
   foreach(var t in all.Where(x=>x.name.Contains("Inn")||x.name.Contains("inn")||x.name.Contains("Npc")||x.name.Contains("npc")||x.name.Contains("Actor")||x.name.Contains("Road"))){var rr=t.GetComponentsInChildren<Renderer>(true);var b=new Bounds(t.position,Vector3.zero);foreach(var a in rr)b.Encapsulate(a.bounds);text+=t.name+" pos="+t.position+" bounds="+b+" scale="+t.lossyScale+"\n";}
   foreach(var a in s.Actors)text+="ACTOR "+a.Id+" root="+a.name+" position="+a.transform.position+"\n";
   foreach(var p in s.Content.Points)text+="POINT "+p.Id+" "+p.Position+"\n";
   File.WriteAllText(VillageOutput+"/survey.txt",text);return text;
  }
 }
}
