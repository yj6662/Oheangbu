using System;
using Oheangbu.Data.World;
using UnityEngine;
namespace Oheangbu.App.Prologue {
 public sealed class JourneyNpcAttention:MonoBehaviour {
  public PrologueSession Session;
  public JourneyNpcAttentionProfileSO Profile;
  public string CommissionId="j1";
  Quaternion resting;
  bool engaged;
  float guideUntil;
  PrologueContentSO.Commission commission;
  PrologueContentSO.Point giver,evidence,guideTarget;
  public string LastGuideTargetId=>guideTarget?.Id;
  public bool Guiding=>engaged&&Time.time<guideUntil;
  public bool Engaged=>engaged;
  void Awake(){resting=transform.rotation;}
  void OnEnable(){if(Session!=null)Session.InteractionPresented+=Respond;}
  void Start(){if(Session==null||Session.Content.Commissions==null)return;commission=Array.Find(Session.Content.Commissions,q=>q.Id==CommissionId);if(commission==null)return;giver=Array.Find(Session.Content.Points,p=>p.Id==commission.GiverId);evidence=Array.Find(Session.Content.Points,p=>p.Id==commission.EvidenceId);}
  void OnDisable(){if(Session!=null)Session.InteractionPresented-=Respond;engaged=false;guideUntil=0;}
  void Respond(PrologueInteractionKind kind,Vector3 point,PrologueSession.InteractionResult result){
   if(Profile==null||giver==null||evidence==null||kind!=PrologueInteractionKind.Conversation||(point-giver.Position).sqrMagnitude>.01f)return;
   if(result==PrologueSession.InteractionResult.SaveFailed)return;
   bool pending=Session.Progress.completed.Contains(CommissionId+":accepted")&&!Session.Progress.completed.Contains(commission.EvidenceId)&&!Session.Progress.completed.Contains(CommissionId+":reported");
   guideTarget=pending?evidence:null;
   bool reported=Session.Progress.completed.Contains(CommissionId+":reported");
   bool finished=!string.IsNullOrEmpty(Profile.StopAfterDefeatedId)&&Session.Progress.defeated.Contains(Profile.StopAfterDefeatedId);
   if(reported&&!finished&&!string.IsNullOrEmpty(Profile.AfterReportPointId))guideTarget=Array.Find(Session.Content.Points,p=>p.Id==Profile.AfterReportPointId);
   guideUntil=guideTarget!=null?Time.time+Profile.GuideSeconds:0;
  }
  void LateUpdate(){
   if(Profile==null||Session==null||Session.Player==null||Session.Progress==null||Time.deltaTime<=0)return;
   float distance=Vector3.Distance(transform.position,Session.Player.position);
   engaged=distance<=(engaged?Mathf.Max(Profile.EngageDistance,Profile.DisengageDistance):Profile.EngageDistance);
   Quaternion target=resting;
   if(engaged){Vector3 delta=(Guiding&&guideTarget!=null?guideTarget.Position:Session.Player.position)-transform.position;delta.y=0;if(delta.sqrMagnitude>.001f)target=Quaternion.LookRotation(delta);}
   transform.rotation=Quaternion.RotateTowards(transform.rotation,target,Profile.TurnDegreesPerSecond*Time.deltaTime);
  }
 }
}
