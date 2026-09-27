using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string VehicleOutput250=Output+"/Vehicle250";
  static int callsBefore250;
  static Vector3 feet250,forward250;
  public static string BibleBindings250()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit scene audit required");
   var scene=SceneManager.GetActiveScene();var s=VillageSession();
   if(!scene.path.Contains("slice-5e82ecd76d2a/"))throw new Exception("Latest candidate required");
   var components=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>(true)).Where(c=>c!=null).ToArray();
   var names=new[]{"CheongryongCombatController","DemoGukRevisitSite","SouthGateGeneralController","SouthGateDoorPresentation","DemoSummonCombatManager","WorldMacroPalanquinSummon","WorldMacroTraversalService","WorldMacroInnRestPresentation"};
   var rows=new List<string>{"Scene: "+scene.path,"Content: "+AssetDatabase.GetAssetPath(s.Content),"Campaign: "+AssetDatabase.GetAssetPath(s.Content.Campaign),"SaveSlot: "+s.Content.SaveSlot};
   foreach(var name in names){var found=components.Where(x=>x.GetType().Name==name).ToArray();rows.Add(name+": total="+found.Length+" active="+found.Count(x=>x.enabled&&x.gameObject.activeInHierarchy));}
   foreach(var field in s.GetType().GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public).Where(f=>f.Name.Contains("Escort")||f.Name.Contains("Guk")||f.Name.Contains("SouthGate")))
   {var value=field.GetValue(s);rows.Add(field.Name+"="+(value is Object obj?(obj==null?"null":obj.name):value?.ToString()??"null"));}
   Directory.CreateDirectory(VehicleOutput250);File.WriteAllText(VehicleOutput250+"/bible-bindings.txt",string.Join("\n",rows));return string.Join("\n",rows);
  }
  public static string VehicleCheck250(string command)
  {
   var s=VillageSession();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("_compact_slice_"))throw new Exception("Private diagnostic Play required");
   var call=s.GetComponent<WorldMacroPalanquinSummon>();if(call==null)throw new Exception("Missing vehicle summon");
   Directory.CreateDirectory(VehicleOutput250);var lines=new List<string>();
   void Check(bool value,string text){string line=(value?"PASS ":"FAIL ")+text;lines.Add(line);File.AppendAllText(VehicleOutput250+"/runtime.txt",line+"\n");if(!value)throw new Exception(text);}
   if(command=="probes")
   {
    File.WriteAllText(VehicleOutput250+"/runtime.txt","");
    var oldSheet=call.WorldSheet;var oldSession=call.Session;var sheet=Object.Instantiate(oldSheet);
    sheet.Routes=Array.Empty<WorldMacroSheetSO.RouteSpec>();sheet.Rivers=Array.Empty<WorldMacroSheetSO.RiverSpec>();
    var pad=GameObject.CreatePrimitive(PrimitiveType.Cube);pad.name="Vehicle250TemporaryPhysicsPad";pad.transform.position=new Vector3(-1000,99.5f,-1000);pad.transform.localScale=new Vector3(60,1,60);pad.GetComponent<Renderer>().enabled=false;
    GameObject block=null;var before=call.Vehicle.Body.position;int count=call.SuccessfulCalls;
    try
    {
     call.WorldSheet=sheet;call.Session=null;Physics.SyncTransforms();var feet=new Vector3(-1000,100,-1000);
     foreach(var direction in new[]{Vector3.forward,Vector3.right,Vector3.back,Vector3.left})
     {
      Check(call.TryFindPlacement(feet,direction,out var pose,out _),"front placement on flat ground without any road: "+direction);
      var d=pose.Position-feet;d.y=0;
      Check(Vector3.Dot(d.normalized,direction)>.999f&&Mathf.Abs(d.magnitude-call.MinimumPlayerDistance)<.02f&&pose.Route=="player_front","near distance and requested facing: "+direction);
     }
     // Regression: valid near-distance poses used to fail with e.g. 5.999987 m.
     // Vary both fractional world origin and diagonal bearing on actual colliders.
     var priorPad=pad.transform.position;pad.transform.position=new Vector3(1900,99.5f,2200);Physics.SyncTransforms();
     var missed=new List<string>();
     for(int phase=0;phase<6;phase++)for(int angle=0;angle<8;angle++)
     {
      var origin=new Vector3(1900+phase*.137f,100,2200+phase*.219f);var direction=Quaternion.Euler(0,13+angle*45,0)*Vector3.forward;
      if(!call.TryFindPlacement(origin,direction,out _,out _))missed.Add(phase+"/"+angle+":"+call.LastPlacementDiagnostic);
     }
     pad.transform.position=priorPad;Physics.SyncTransforms();
     Check(missed.Count==0,"48 diagonal front placements at kilometre coordinates: "+string.Join(";",missed));
     Check(call.TryFindPlacement(feet,Vector3.up,out _,out _),"vertical preferred direction has a horizontal fallback");
     block=GameObject.CreatePrimitive(PrimitiveType.Cube);block.name="Vehicle250TemporaryBlocker";block.transform.position=feet+Vector3.forward*call.MinimumPlayerDistance+Vector3.up*2;block.transform.localScale=new Vector3(8,4,8);block.GetComponent<Renderer>().enabled=false;Physics.SyncTransforms();
     Check(!call.TryFindPlacement(feet,Vector3.forward,out _,out _),"blocked front fails instead of selecting another road");
     Object.DestroyImmediate(block);block=null;pad.SetActive(false);Physics.SyncTransforms();
     Check(!call.TryFindPlacement(feet,Vector3.forward,out _,out _),"unsupported front cannot place a floating vehicle");
     pad.SetActive(true);sheet.Rivers=new[]{new WorldMacroSheetSO.RiverSpec{Width=40,Points=new[]{feet+Vector3.up,feet+Vector3.up+Vector3.forward*30}}};Physics.SyncTransforms();
     Check(!call.TryFindPlacement(feet,Vector3.forward,out _,out _),"water-covered support is rejected");
     Check(call.Vehicle.Body.position==before&&call.SuccessfulCalls==count,"queries including failures never move or duplicate the vehicle");
    }
    finally{call.WorldSheet=oldSheet;call.Session=oldSession;if(block!=null)Object.DestroyImmediate(block);Object.DestroyImmediate(pad);Object.DestroyImmediate(sheet);Physics.SyncTransforms();}
   }
   else if(command=="prepare")
   {
    var ui=PlaytestUiRoot.Instance;ui.CloseMenu();ui.Gate.ReleaseImmediately();
    var ground=FinalSurface(SceneManager.GetActiveScene());bool found=false;
    foreach(var xz in new[]{new Vector2(2700,2180),new Vector2(2702,2186),new Vector2(2705,2190)})
    {
     var p=ground(xz.x,xz.y).point;
     foreach(var f in new[]{Vector3.left,Vector3.forward,Vector3.right,Vector3.back})
      if(call.TryFindPlacement(p,f,out _,out _)){feet250=p;forward250=f;found=true;break;}
     if(found)break;
    }
    Check(found,"authored village has a valid nearby front placement");
    Check(call.TryFindPlacement(feet250,forward250,out var frontPose,out _)&&frontPose.Route=="player_front","front placement remains independent of authored carriage roads");
    call.RecallAfterRecovery();s.Teleport(feet250,Quaternion.LookRotation(forward250).eulerAngles.y);callsBefore250=call.SuccessfulCalls;
    return string.Join("\n",lines)+"\nPlayer="+feet250+" forward="+forward250;
   }
   else if(command=="begin")Check(call.TryBeginShortcut(out var reason),"actual G entry accepted: "+reason);
   else if(command=="verify")
   {
    Check(!call.Calling&&call.SuccessfulCalls==callsBefore250+1,"real timed gesture commits exactly once");
    var delta=call.Vehicle.Body.position-s.Walker.Body.transform.position;delta.y=0;
    Check(Vector3.Dot(delta.normalized,forward250)>.995f&&Mathf.Abs(delta.magnitude-call.MinimumPlayerDistance)<.35f,"actual car is directly ahead: distance="+delta.magnitude+" position="+call.Vehicle.Body.position);
    Check(call.LastRoute=="player_front"&&!call.IsRecalled&&call.Vehicle.gameObject.activeInHierarchy,"same recalled vehicle is visible at front placement");
    Check(Object.FindObjectsByType<WorldMacroPalanquinController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==1,"there is still exactly one vehicle");
    Check(!PlaytestUiRoot.Instance.Gate.InputBlocked&&!call.TemporaryPendant.gameObject.activeSelf,"gesture ends and returns gameplay control");
    callsBefore250=call.SuccessfulCalls;
   }
   else throw new Exception("Unknown vehicle diagnostic");
   return string.Join("\n",lines);
  }
 }
}
