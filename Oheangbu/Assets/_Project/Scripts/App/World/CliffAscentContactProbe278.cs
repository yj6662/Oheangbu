using UnityEngine;
namespace Oheangbu.App.World {
 public sealed class CliffAscentContactProbe278:MonoBehaviour {
  public string LastHit="none", Result; public System.Collections.Generic.HashSet<string> HitNames=new();
  public Vector3[] Points;
  public int Target=4, Moves;
  float stalled;
  void OnControllerColliderHit(ControllerColliderHit hit){HitNames.Add(hit.collider.name+" normal="+hit.normal);LastHit=hit.collider.name+" point="+hit.point+" normal="+hit.normal+" direction="+hit.moveDirection;}
  void FixedUpdate(){
   if(Points==null||Result!=null)return;
   if(Target>=327){Result="PASS";return;}
   var before=transform.position;var d=Points[Target]-before;d.y=0;
   if(d.magnitude<.15f){Target++;return;}
   GetComponent<CharacterController>().Move(Vector3.ClampMagnitude(d,4.5f*Time.fixedDeltaTime)+Vector3.down*3*Time.fixedDeltaTime);Moves++;
   var travelled=transform.position-before;travelled.y=0;
   stalled=travelled.magnitude<.001f?stalled+Time.fixedDeltaTime:0;
   if(stalled>3)Result="FAIL stuck";
  }
 }
}
