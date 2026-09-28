using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
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
  const string EscortOutput252=Output+"/Escort252";
  public static string EscortSurvey252()
  {
   var scene=FrontageScene249();var s=VillageSession();var ground=FinalSurface(scene);
   Directory.CreateDirectory(EscortOutput252);var lines=new List<string>();
   var layout=AssetDatabase.LoadAssetAtPath<CompactWorldLayoutSO>(Folder+"/WorldLayout.asset");
   foreach(var id in new[]{"merchant","road_pass","inspection_one","road_hamlet","inspection_two","capital_delivery","south_gate"})
   {
    var p=layout.Places.Single(x=>x.Id==id);var h=ground(p.XZ.x,p.XZ.y);float lo=h.point.y,hi=lo,slope=0;
    for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++){var hit=ground(p.XZ.x+x*5,p.XZ.y+z*5);lo=Mathf.Min(lo,hit.point.y);hi=Mathf.Max(hi,hit.point.y);slope=Mathf.Max(slope,Vector3.Angle(hit.normal,Vector3.up));}
    lines.Add(id+" ground="+h.point+" spread="+(hi-lo)+" slope="+slope);
   }
   var call=s.GetComponent<WorldMacroPalanquinSummon>();
   foreach(var xz in new[]{new Vector2(2700,2172),new Vector2(2690,2172),new Vector2(2700,2160),new Vector2(2702,2186)})
    foreach(var forward in new[]{Vector3.left,Vector3.back,Vector3.right,Vector3.forward})
    {var p=ground(xz.x,xz.y).point;if(call.TryFindPlacement(p,forward,out var pose,out var reason))lines.Add("START player="+p+" forward="+forward+" vehicle="+pose.Position);}
   lines.Add("CANDIDATE companion="+s.DemoEscortCompanion+" sockets="+s.DemoEscortPassengerSocket+"/"+s.DemoEscortCargoSocket);
   var source=EditorSceneManager.OpenScene(Scene,OpenSceneMode.Additive);
   try
   {
    var old=source.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
    lines.Add("SOURCE cargo="+old.DemoEscortCargo+" companion="+old.DemoEscortCompanion+" general="+old.DemoSouthGateGeneral);
    foreach(var c in source.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<MonoBehaviour>(true)).Where(c=>c is DemoEscortSceneRoute||c is SouthGateDoorPresentation||c is SouthGateGeneralController))
     lines.Add("SOURCE "+c.GetType().Name+" object="+c.name+" parent="+c.transform.parent+" pos="+c.transform.position+" JSON="+JsonUtility.ToJson(c));
   }
   finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(scene);}
   File.WriteAllText(EscortOutput252+"/survey.txt",string.Join("\n",lines));return string.Join("\n",lines);
  }
 }
}
