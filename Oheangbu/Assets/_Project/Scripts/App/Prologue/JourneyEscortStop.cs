using Oheangbu.App.Demo;
using UnityEngine;
namespace Oheangbu.App.Prologue {
 public sealed class JourneyEscortStop : MonoBehaviour {
  public PrologueSession Session;
  public string PointId;
  public Transform Barrier;
  public DemoEscortStage ClearedStage;
  Quaternion closed;
  void Awake(){if(Barrier!=null)closed=Barrier.localRotation;}
  void Update(){if(Barrier!=null&&Session!=null&&Session.Progress!=null)Barrier.localRotation=closed*Quaternion.Euler(0,0,Session.Progress.escort.Stage>=ClearedStage?85:0);}
 }
}
