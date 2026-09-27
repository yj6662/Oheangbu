using Oheangbu.App.Demo;
using UnityEngine;
using UnityEngine.AI;
namespace Oheangbu.App.Prologue {
 public sealed class JourneyVictoryGate : MonoBehaviour {
  public PrologueSession Session;
  public SouthGateDoorPresentation Door;
  public NavMeshObstacle NavigationObstacle;
  public string BossId="south_gate_general";
  bool initialized,last;
  void Update(){
   if(Session==null||Session.Progress==null||Door==null)return;
   bool open=Session.Progress.defeated.Contains(BossId);
   if(!initialized||last!=open){Door.SetOpened(open,!initialized);initialized=true;last=open;}
   if(NavigationObstacle!=null)NavigationObstacle.enabled=!Door.OpenAnimationComplete;
  }
 }
}
