using Oheangbu.App.Demo;
using UnityEngine;
namespace Oheangbu.App.Prologue {
 [RequireComponent(typeof(CheongryongGrowthController),typeof(PrologueEncounter))]
 public sealed class JourneyGrowthLesson:MonoBehaviour {
  public const string CompletionId="metal_growth_lesson";
  public PrologueSession Session;
  public bool HasProof {get;private set;}
  CheongryongGrowthController growth;float retryAt;bool committed;
  void OnEnable(){growth=GetComponent<CheongryongGrowthController>();growth.GrowthInterrupted+=Interrupted;}
  void OnDisable(){if(growth!=null)growth.GrowthInterrupted-=Interrupted;}
  void Interrupted(){HasProof=true;Commit();}
  void Update(){if(HasProof&&!committed&&Time.timeScale>0&&Time.unscaledTime>=retryAt){retryAt=Time.unscaledTime+1;Commit();}}
  void Commit(){if(Session!=null&&HasProof)committed=Session.RecordGrowthLesson(this);}
 }
 public sealed partial class PrologueSession {
  public bool RecordGrowthLesson(JourneyGrowthLesson source){
   if(!ready||respawning||source==null||source.Session!=this||!source.HasProof||source.gameObject.scene!=gameObject.scene||System.Array.IndexOf(Encounters,source.GetComponent<PrologueEncounter>())<0)return false;
   if(Progress.completed.Contains(JourneyGrowthLesson.CompletionId))return true;
   var candidate=CopyProgress();candidate.completed.Add(JourneyGrowthLesson.CompletionId);return TryCommit(candidate);
  }
 }
}
