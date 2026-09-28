using UnityEngine;
namespace Oheangbu.App.Prologue {
 [DisallowMultipleComponent]
 public sealed class JourneyCollectibleView : MonoBehaviour {
  public PrologueSession Session;
  public string PointId;
  Renderer[] renderers;
  Collider[] colliders;
  bool[] visible,solid;
  bool? collected;
  void Awake(){
   renderers=GetComponentsInChildren<Renderer>(true);colliders=GetComponentsInChildren<Collider>(true);
   visible=new bool[renderers.Length];solid=new bool[colliders.Length];
   for(int i=0;i<renderers.Length;i++)visible[i]=renderers[i].enabled;
   for(int i=0;i<colliders.Length;i++)solid[i]=colliders[i].enabled;
  }
  void LateUpdate()=>RefreshFromProgress();
  public void RefreshFromProgress(){
   if(Session==null||Session.Progress==null||renderers==null)return;
   bool next=Session.Progress.completed.Contains(PointId);if(collected==next)return;collected=next;
   for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null)renderers[i].enabled=!next&&visible[i];
   for(int i=0;i<colliders.Length;i++)if(colliders[i]!=null)colliders[i].enabled=!next&&solid[i];
  }
 }
}
