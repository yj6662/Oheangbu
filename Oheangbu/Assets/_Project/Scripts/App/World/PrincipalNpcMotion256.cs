using UnityEngine;
namespace Oheangbu.App.World
{
 // Visual observer only. Gameplay movement, cargo, conversation and save ownership stay on the existing root.
 [DefaultExecutionOrder(350),DisallowMultipleComponent]
 public sealed class PrincipalNpcMotion256:MonoBehaviour
 {
  public Animator Animator;
  public Transform MotionRoot;
  public WorldMacroPlaytestSession Session;
  public bool IsWangso;
  public bool AllowRun;
  public string CurrentState {get;private set;}
  public float ObservedSpeed {get;private set;}
  Vector3 previous;
  void OnEnable(){previous=MotionRoot!=null?MotionRoot.position:transform.position;CurrentState=null;ObservedSpeed=0;}
  void LateUpdate()
  {
   if(Animator==null||MotionRoot==null)return;
   var p=MotionRoot.position;float dt=Time.deltaTime;
   float speed=dt>0?Vector3.ProjectOnPlane(p-previous,Vector3.up).magnitude/dt:0;previous=p;
   if(dt<=0)return;
   bool riding=IsWangso&&Session!=null&&Session.DemoEscortPassengerSocket!=null&&MotionRoot.IsChildOf(Session.DemoEscortPassengerSocket);
   ObservedSpeed=riding||speed>12?0:speed;
   string next=ObservedSpeed>.15f?(AllowRun&&ObservedSpeed>3.2f?"Run":"Walk"):"Idle";
   if(next!=CurrentState){Animator.CrossFadeInFixedTime(next,.18f);CurrentState=next;}
   Animator.speed=next=="Walk"?Mathf.Clamp(ObservedSpeed/1.35f,.65f,1.6f):next=="Run"?Mathf.Clamp(ObservedSpeed/3.7f,.75f,1.4f):1;
  }
 }
}
