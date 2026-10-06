using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Oheangbu.Combat;
namespace Oheangbu.App.World
{
 // Candidate-only adapter. Combat owns state, damage, navigation and resurrection.
 [DisallowMultipleComponent]
 public sealed class EnemyRigMotion273:MonoBehaviour
 {
  public Animator Animator; public EnemyController Enemy; public EnemyVitals Vitals;
  public AnimationClip Idle,Walk,Attack,Hit,Stun,Death;
  public float WalkMetresPerSecond=1.5f;
  PlayableGraph graph;AnimationMixerPlayable mixer;AnimationClipPlayable[] nodes;
  Vector3 previousPosition;float distance,hitAt=-100,deathAt=-100;uint revision;
  public string CurrentPose{get;private set;} public float PoseTime{get;private set;}
  // #307 phase 1 item 4: graph evaluations skipped while the enemy was inactive and none of its renderers was drawn (the pose
  // inputs are still set every frame and the graph is stateless, so the first drawn frame evaluates the same pose)
  public int SkippedEvaluations{get;private set;}
  Renderer[] poseRenderers;bool liveEvaluate;   // only LateUpdate may skip; an explicit Evaluate call (checks) always samples
  bool CanSkipPose(){if(!liveEvaluate||poseRenderers==null||Enemy.isActiveAndEnabled)return false;foreach(var r in poseRenderers)if(r!=null&&r.enabled&&r.gameObject.activeInHierarchy)return false;return true;}
  void OnEnable(){previousPosition=transform.position;if(Vitals!=null){revision=Vitals.LifeRevision;Vitals.DamageResolved+=Damaged;Vitals.Died+=Died;}}
  // #306: hit pose only for damage the presentation may react to (EnemyVitals.ReactsTo: ReactToHarvest), as EnemyRigMotion298
  void Damaged(EnemyDamageResult r){if(r.Target==Vitals&&r.AppliedDamage>0&&Vitals.ReactsTo(r.Attack))hitAt=Time.time;}
  void Died(){deathAt=Time.time;}
  void Create()
  {
   if(graph.IsValid()||Animator==null||Idle==null||Walk==null||Attack==null||Hit==null||Stun==null||Death==null)return;
   Animator.runtimeAnimatorController=null;Animator.applyRootMotion=false;Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
   graph=PlayableGraph.Create("Candidate enemy authored pose clock");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
   mixer=AnimationMixerPlayable.Create(graph,6);nodes=new AnimationClipPlayable[6];var clips=new[]{Idle,Walk,Attack,Hit,Stun,Death};
   for(int i=0;i<6;i++){nodes[i]=AnimationClipPlayable.Create(graph,clips[i]);nodes[i].SetSpeed(0);graph.Connect(nodes[i],0,mixer,i);}
   AnimationPlayableOutput.Create(graph,"Enemy bones",Animator).SetSourcePlayable(mixer);graph.Play();
   poseRenderers=Animator.GetComponentsInChildren<Renderer>(true);
  }
  public static float AttackPhase(bool telegraph,float anticipation,bool recovering,float recovery)=>telegraph?.5f*Mathf.Clamp01(anticipation):recovering?.5f+.5f*Mathf.Clamp01(recovery):.55f;
  public void Evaluate(float now,float dt)
  {
   Create();if(!graph.IsValid()||Vitals==null||Enemy==null)return;
   if(revision!=Vitals.LifeRevision){revision=Vitals.LifeRevision;hitAt=deathAt=-100;distance=0;previousPosition=transform.position;}
   float travel=Vector3.ProjectOnPlane(transform.position-previousPosition,Vector3.up).magnitude;previousPosition=transform.position;
   float speed=dt>0?travel/dt:0;if(speed>12)speed=travel=0;distance+=travel;
   for(int i=0;i<6;i++)mixer.SetInputWeight(i,0);
   int index=0;float t=Mathf.Repeat(now,Idle.length);CurrentPose="Idle";
   if(!Vitals.IsAlive){if(deathAt<0)deathAt=now;index=5;t=Mathf.Min(Mathf.Max(0,now-deathAt),Death.length);CurrentPose="Death";}
   else if(Enemy.IsStunned){index=4;t=Mathf.Repeat(now,Stun.length);CurrentPose="Stun";}
   else if(Enemy.AttackInProgress){index=2;t=AttackPhase(Enemy.IsTelegraphing,Enemy.TelegraphProgress,Enemy.IsRecovering,Enemy.RecoveryProgress)*Attack.length;CurrentPose="Attack";}
   else if(now-hitAt>=0&&now-hitAt<Hit.length){index=3;t=now-hitAt;CurrentPose="Hit";}
   else if(speed>.08f){index=1;t=Mathf.Repeat(distance/Mathf.Max(.1f,WalkMetresPerSecond),Walk.length);CurrentPose="Walk";}
   nodes[index].SetTime(t);mixer.SetInputWeight(index,1);PoseTime=t;
   if(CanSkipPose()){SkippedEvaluations++;return;}
   graph.Evaluate(0);
  }
  void LateUpdate(){if(Time.deltaTime>0){liveEvaluate=true;try{Evaluate(Time.time,Time.deltaTime);}finally{liveEvaluate=false;}}}
  void OnDisable(){if(Vitals!=null){Vitals.DamageResolved-=Damaged;Vitals.Died-=Died;}if(graph.IsValid())graph.Destroy();}
 }
}
