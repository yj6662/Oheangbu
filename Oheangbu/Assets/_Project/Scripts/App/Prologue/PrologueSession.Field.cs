using Oheangbu.App.Demo;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEngine;
namespace Oheangbu.App.Prologue {
 public sealed partial class PrologueSession {
  public Vfx120Profile WoodLiftProfile;
  public DemoGukRevisitSite RevisitSite;
  public FieldSpellService JourneyField {get;private set;}
  bool liftedAtRevisit;
  public bool HasGuk=>Progress!=null&&Progress.defeated.Contains("cheongryong");
  void BindJourneyField(){
   if(WoodLiftProfile==null)return;
   JourneyField=GetComponent<FieldSpellService>();if(JourneyField==null)JourneyField=gameObject.AddComponent<FieldSpellService>();
   var motor=Player.GetComponent<PlayerMotor>();
   JourneyField.Configure(cc,motor,vitals,WoodLiftProfile,()=>ready&&isActiveAndEnabled&&HasGuk,()=>motor.IsSitting||JourneySeated,()=>Time.timeScale==0);
   JourneyField.StateChanged+=JourneyFieldChanged;vitals.Died+=ClearRevisitProof;Wiring.FieldSpells=JourneyField;
  }
  void ClearRevisitProof(){liftedAtRevisit=false;}
  void JourneyFieldChanged(FieldLiftState state){
   if(state==FieldLiftState.Holding&&HasGuk&&RevisitSite!=null&&RevisitSite.LiftPad!=null&&JourneyField.PassengerSupported&&JourneyField.CurrentHeight>=2.1f&&Vector3.Distance(JourneyField.BasePoint,RevisitSite.LiftPad.position)<=1)liftedAtRevisit=true;
  }
  bool CanCollectJourneyRevisit(PrologueContentSO.Point point){
   if(point.Id!=DemoGukRevisitSite.Id)return true;
   return HasGuk&&liftedAtRevisit&&RevisitSite!=null&&RevisitSite.UpperSurface!=null&&Mathf.Abs(FieldSpellService.Feet(cc).y-RevisitSite.UpperSurface.position.y)<.25f;
  }
  void UnbindJourneyField(){
   if(vitals!=null)vitals.Died-=ClearRevisitProof;
   if(JourneyField==null)return;JourneyField.StateChanged-=JourneyFieldChanged;
   if(Wiring!=null&&Wiring.FieldSpells==JourneyField)Wiring.FieldSpells=null;
  }
 }
}
