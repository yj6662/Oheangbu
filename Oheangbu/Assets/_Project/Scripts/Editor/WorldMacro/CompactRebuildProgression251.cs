using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string ProgressionOutput251=Output+"/Progression251";
  public static string ProgressionSurvey251()
  {
   var scene=FrontageScene249();var s=VillageSession();var ground=FinalSurface(scene);
   Directory.CreateDirectory(ProgressionOutput251);
   var lines=new List<string>{"Candidate "+scene.path};
   var layout=AssetDatabase.LoadAssetAtPath<CompactWorldLayoutSO>(Folder+"/WorldLayout.asset");
   foreach(var id in new[]{"village","logging","deep_forest","sanctuary","high_cache"})
   {
    var p=layout.Places.Single(x=>x.Id==id);var h=ground(p.XZ.x,p.XZ.y);float min=h.point.y,max=h.point.y,slope=0;
    for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++){var hit=ground(p.XZ.x+x*5,p.XZ.y+z*5);min=Mathf.Min(min,hit.point.y);max=Mathf.Max(max,hit.point.y);slope=Mathf.Max(slope,Vector3.Angle(hit.normal,Vector3.up));}
    lines.Add(id+" ground="+h.point+" spread30m="+(max-min)+" slope="+slope);
   }
   var original=EditorSceneManager.OpenScene(Scene,OpenSceneMode.Additive);
   try
   {
    foreach(var a in original.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<PrologueEncounter>(true)).Where(a=>a.GetComponent<CheongryongCombatController>()!=null||a.GetComponent<DemoGrowthLessonLink>()!=null))
     lines.Add("SOURCE actor "+a.Id+" path="+a.transform.parent.name+" position="+a.transform.position+" components="+string.Join(",",a.GetComponents<Component>().Select(x=>x.GetType().Name)));
    foreach(var g in original.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<DemoGukRevisitSite>(true)))lines.Add("SOURCE guk "+g.name+" pos="+g.transform.position+" pad="+g.LiftPad.position+" upper="+g.UpperSurface.position+" exit="+g.DescentExit.position);
   }
   finally{EditorSceneManager.CloseScene(original,true);SceneManager.SetActiveScene(scene);}
   File.WriteAllText(ProgressionOutput251+"/survey.txt",string.Join("\n",lines));return string.Join("\n",lines);
  }
 }
}
