using UnityEngine;
namespace Oheangbu.App.Prologue {
 public sealed partial class PrologueSession {
  public bool RenAvailable=>Progress!=null&&Progress.defeated.Contains("cheongryong")&&!Progress.renUsed;
  bool TryJourneyRen(float survivingHpFraction){
   if(!ready||respawning||!RenAvailable||!float.IsFinite(survivingHpFraction)||survivingHpFraction<=0||survivingHpFraction>1)return false;
   var candidate=CopyProgress();Snapshot(candidate);candidate.renUsed=true;candidate.hp=survivingHpFraction;
   // Save the post-hit HP before Vitals publishes survival; never snapshot pre-hit HP over it.
   return TryCommit(candidate,false);
  }
 }
}
