using System;
using Oheangbu.App.World.UI;
using Oheangbu.App.Prologue;
using UnityEngine;
namespace Oheangbu.App.World
{
 public sealed partial class WorldMacroPlaytestSession
 {
  public bool LocationDiscoveryAllowed=>ready&&vitals!=null&&vitals.Hp01>0&&!respawning&&!GameplayInputBlocked&&!RestPresentationActive&&!HasPendingDefeats&&pendingEnvironmentRecovery==null&&!escortDeathPending;
  public bool LocationArrivalAllowed
  {
   get
   {
    if(!LocationDiscoveryAllowed||Time.unscaledTime<until||Walker==null||Walker.Motor.IsDrawing||Walker.Drawing.InDrawMode)return false;
    foreach(var actor in Actors)if(actor!=null&&actor.isActiveAndEnabled&&actor.Current==PrologueEncounter.Behaviour.Chase)return false;
    return true;
   }
  }
  public static bool PrepareLocationVisit(WorldMacroProgress source,WorldLocationCatalog.Entry entry,out WorldMacroProgress proposed)
  {
   proposed=null;if(!WorldMacroProgress.Valid(source)||entry==null||string.IsNullOrWhiteSpace(entry.Id))return false;
   string fact="location:"+entry.Id;
   if(source.campaign?.Facts?.Contains(fact)==true&&(string.IsNullOrEmpty(entry.MarkerId)||source.ui?.discoveredMarkers?.Contains(entry.MarkerId)==true))return false;
   var next=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
   if(!next.campaign.Facts.Contains(fact))next.campaign.Facts.Add(fact);
   if(!string.IsNullOrEmpty(entry.MarkerId)&&!next.ui.discoveredMarkers.Contains(entry.MarkerId))next.ui.discoveredMarkers.Add(entry.MarkerId);
   proposed=next;return true;
  }
  public bool TryRecordLocationVisit(WorldLocationCatalog.Entry entry)
  {
   if(Progress==null||!LocationDiscoveryAllowed)return false;
   if(!PrepareLocationVisit(Progress,entry,out var proposed))return true;
   return TryCommitInteraction(proposed,out _);
  }
 }
}
