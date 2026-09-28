using System;
using System.IO;
using System.Linq;
using Oheangbu.App.Prologue;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools
{
 public static partial class PineRestGameBuilder
 {
  const string CityWalkReport="../Art/World/PineRest/city_entry_walk.txt";
  static double cityWalkDeadline,cityArrival;
  static float cityWalkDistance;
  static Vector3 cityWalkPrevious;
  static bool cityRestPending;
  static string CityEntryPrepare()
  {
   var s=RoadRestSession();var p=s.Content.Points.Single(x=>x.Id=="CityEntryRest");
   if(s.Progress.defeated.Contains("south_gate_general"))throw new Exception("Fresh audit required");
   string before=s.Progress.checkpoint;s.Teleport(p.Position+Vector3.up,0);s.Interact(p.Id);
   bool locked=s.Progress.checkpoint==before;
   if(!locked)throw new Exception("City rest accepted before gate victory");
   s.Progress.defeated.Add("south_gate_general");s.Save();
   File.WriteAllText(CityWalkReport,"PASS city rest requires gate victory\nGate victory save is setup fixture, not combat evidence.\n");
   return "Verified locked rest; gate victory fixture committed. Wait for gate opening before CityEntryWalk";
  }
  static string CityEntryWalk()
  {
   var s=RoadRestSession();var gate=Object.FindFirstObjectByType<JourneyVictoryGate>();
   if(!gate.Door.OpenAnimationComplete||roadKeyboard!=null)throw new Exception("Open gate and idle input audit required");
   var destination=s.Content.Points.Single(x=>x.Id=="CityEntryRest").Position;
   s.Teleport(gate.Door.transform.position+Vector3.forward*9+Vector3.up*1.1f,180);
   BeginRoadWalk(s,destination);
   priorRoadKeyboard=Keyboard.current;roadKeyboard=InputSystem.AddDevice<Keyboard>("JourneyCityWalkAudit");roadKeyboard.MakeCurrent();roadKeys=Array.Empty<Key>();
   cityWalkDistance=0;cityWalkPrevious=s.Player.position;cityWalkDeadline=EditorApplication.timeSinceStartup+100;cityRestPending=false;
   InputSystem.onBeforeUpdate+=RoadDriveInput;EditorApplication.update+=CityWalkTick;
   return "City traversal running: gate -> market street -> stream bridge -> rest; actual WASD then F";
  }
  static void CityWalkTick()
  {
   try
   {
    if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup>cityWalkDeadline)throw new Exception("City traversal stopped or timed out");
    var s=RoadRestSession();cityWalkDistance+=Vector3.Distance(s.Player.position,cityWalkPrevious);cityWalkPrevious=s.Player.position;
    if(cityRestPending)
    {
     if(EditorApplication.timeSinceStartup<cityArrival+.5)return;
     if(s.Progress.checkpoint!="CityEntryRest")throw new Exception("Arrival F did not commit city rest");
     var saved=new PrologueProgressStore(Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json")).Load();
     if(saved.checkpoint!="CityEntryRest")throw new Exception("City rest missing on disk");
     FinishCityWalk(null);return;
    }
    if(RoadWalking(s)){roadKeys=new[]{Key.F};cityRestPending=true;cityArrival=EditorApplication.timeSinceStartup;}
   }
   catch(Exception e){FinishCityWalk(e.ToString());}
  }
  static void FinishCityWalk(string error)
  {
   EditorApplication.update-=CityWalkTick;InputSystem.onBeforeUpdate-=RoadDriveInput;
   if(roadKeyboard!=null){InputSystem.RemoveDevice(roadKeyboard);roadKeyboard=null;}
   if(priorRoadKeyboard!=null&&priorRoadKeyboard.added)priorRoadKeyboard.MakeCurrent();roadKeys=Array.Empty<Key>();
   File.AppendAllText(CityWalkReport,(error==null?"PASS player walked from gate across stream and used F to persist city rest":"FAIL "+error)+"; distance="+cityWalkDistance+"; final="+cityWalkPrevious+"\nInitial pose/victory fixture only, then actual motor input. Not manual campaign, final city art, or future region completion.\n");
  }
 }
}
