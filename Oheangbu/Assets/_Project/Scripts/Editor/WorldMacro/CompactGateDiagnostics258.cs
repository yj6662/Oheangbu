using System.IO;
using System.Linq;
using Oheangbu.App.World.UI;
using UnityEngine;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string GateRestDiagnostics258()
  {
   var s=JourneySession258();var ui=PlaytestUiRoot.Instance;ui.CloseMenu();ui.Gate.ReleaseImmediately();
   bool approached=ApproachVillage("capital_escort_rest");var p=s.Content.Points.Single(x=>x.Id=="capital_escort_rest");
   var from=s.Walker.Body.transform.position;var eye=from+Vector3.up*s.Walker.EyeHeight;var ray=p.Position+Vector3.up*1.25f-eye;
   var obstacles=Physics.RaycastAll(eye,ray.normalized,ray.magnitude,~0,QueryTriggerInteraction.Ignore).Select(h=>h.collider.name+" @ "+h.point);
   string result="approached="+approached+" can="+s.CanInteract(p.Id)+" blocked="+s.GameplayInputBlocked+" seated="+s.Walker.Seated+
    " motor="+s.Walker.Motor.enabled+" drawing="+s.Walker.Drawing.InDrawMode+" radius="+p.Radius+" distance="+Vector3.Distance(from,p.Position)+
    " feet="+from+" point="+p.Position+" feedback="+s.LastFeedback+"\n"+string.Join("\n",obstacles);
   File.WriteAllText(ContinuationOutput258+"/gate-rest-diagnostics.txt",result);return result;
  }
 }
}
