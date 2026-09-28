using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Oheangbu.Combat;
namespace Oheangbu.App.Demo
{
 // Multipart skinned tree. Poses sample the combat plan's scaled clock, not an independent attack timer.
 public sealed class SinmokRigAnimation:MonoBehaviour
 {
  public Animator Animator;public CheongryongCombatController Combat;
  public AnimationClip Idle;public AnimationClip[] Attacks;public Transform TurningTrunk;
  public Transform FacingVisual;public AnimationClip SupportRelease;
  AnimationClipPlayable replant;AnimationLayerMixerPlayable layers;AvatarMask supportMask;
  CheongryongAttackPlan facingPlan;float turnStarted,turnDuration,turnFrom,turnTo;
  float aimYaw,lastTime;bool sampled;
  PlayableGraph graph;AnimationMixerPlayable mixer;AnimationClipPlayable[] clips;
  CheongryongAttackPlan previous;EnemyVitals vitals;
  public float PoseWeight{get;private set;}
  void OnEnable(){vitals=GetComponent<EnemyVitals>();if(Combat!=null)Combat.AttackEnded+=Ended;}
  void Ended(CheongryongAttackPlan plan,bool cancelled){previous=cancelled?null:plan;}
  void Create()
  {
   if(graph.IsValid()||Animator==null||Idle==null||Attacks==null||Attacks.Length!=4)return;
   graph=PlayableGraph.Create("Sinmok skinned branches");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
   mixer=AnimationMixerPlayable.Create(graph,5);clips=new AnimationClipPlayable[5];
   for(int i=0;i<5;i++){clips[i]=AnimationClipPlayable.Create(graph,i==0?Idle:Attacks[i-1]);clips[i].SetSpeed(0);graph.Connect(clips[i],0,mixer,i);}
   var output=AnimationPlayableOutput.Create(graph,"Tree bones",Animator);
   if(FacingVisual!=null&&SupportRelease!=null)
   {
    layers=AnimationLayerMixerPlayable.Create(graph,2);graph.Connect(mixer,0,layers,0);layers.SetInputWeight(0,1);
    replant=AnimationClipPlayable.Create(graph,SupportRelease);replant.SetSpeed(0);graph.Connect(replant,0,layers,1);
    supportMask=new AvatarMask();supportMask.AddTransformPath(Animator.transform,true);
    for(int i=0;i<supportMask.transformCount;i++)supportMask.SetTransformActive(i,supportMask.GetTransformPath(i).Contains("Support"));
    layers.SetLayerMaskFromAvatarMask(1,supportMask);output.SetSourcePlayable(layers);
   }else output.SetSourcePlayable(mixer);
   graph.Play();
  }
  public void Evaluate(float now)
  {
   if(vitals!=null&&!vitals.IsAlive)return;Create();if(!graph.IsValid())return;
   for(int i=0;i<5;i++)mixer.SetInputWeight(i,0);
   clips[0].SetTime(Mathf.Repeat(now,Idle.length));PoseWeight=0;
   var plan=Combat!=null&&Combat.isActiveAndEnabled?Combat.CurrentPlan??previous:null;
   if(plan!=null&&!plan.IsCancelled){var phase=CheongryongRigPhase.Attack(now,plan.StartedAt,plan.ReleaseAt,plan.ActiveEndAt,plan.EndAt);
    int n=(int)plan.Kind+1;PoseWeight=phase.Weight;clips[n].SetTime(phase.NormalizedTime*Attacks[n-1].length);mixer.SetInputWeight(n,PoseWeight);}
   mixer.SetInputWeight(0,1-PoseWeight);
   if(FacingVisual!=null&&plan!=null&&!plan.IsCancelled)
   {
    if(facingPlan!=plan)
    {
     facingPlan=plan;var direction=transform.InverseTransformDirection(plan.Direction);turnFrom=FacingVisual.localEulerAngles.y;turnTo=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
     turnStarted=now;turnDuration=Mathf.Min(Mathf.Max(.2f,Mathf.Abs(Mathf.DeltaAngle(turnFrom,turnTo))/180f+.2f),Mathf.Max(.2f,plan.ReleaseAt-plan.StartedAt)*.9f);
    }
    float u=Mathf.Clamp01((now-turnStarted)/turnDuration);
    // Only the windup reorientation lifts the support. During active strikes it remains planted.
    FacingVisual.localRotation=Quaternion.Euler(0,Mathf.LerpAngle(turnFrom,turnTo,Mathf.SmoothStep(0,1,u)),0);
    if(replant.IsValid()){replant.SetTime(u*SupportRelease.length);layers.SetInputWeight(1,u<1&&Mathf.Abs(Mathf.DeltaAngle(turnFrom,turnTo))>10?1:0);}
   }
   else if(layers.IsValid())layers.SetInputWeight(1,0);
   graph.Evaluate(0);
   // The planted roots stay fixed; the upper trunk turns the striking limb toward the locked telegraph.
   float dt=sampled?Mathf.Clamp(now-lastTime,0,.1f):0;lastTime=now;sampled=true;
   if(plan!=null&&!plan.IsCancelled){var local=transform.InverseTransformDirection(plan.Direction);aimYaw=Mathf.MoveTowardsAngle(aimYaw,Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,210*dt);}
   if(TurningTrunk!=null&&FacingVisual==null)TurningTrunk.localRotation=Quaternion.Euler(0,aimYaw,0)*TurningTrunk.localRotation;
  }
  void LateUpdate(){Evaluate(Time.time);}
  void OnDisable(){if(Combat!=null)Combat.AttackEnded-=Ended;previous=null;facingPlan=null;sampled=false;if(graph.IsValid())graph.Destroy();if(supportMask!=null){if(Application.isPlaying)Destroy(supportMask);else DestroyImmediate(supportMask);}}
 }
}
