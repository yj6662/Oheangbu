using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace Oheangbu.App.World.UI
{
 public sealed class CompactUiSound255:MonoBehaviour,IPointerEnterHandler,ISelectHandler,IPointerDownHandler,ISubmitHandler
 {
  public PlaytestUiThemeSO Theme;
  bool CanPlay=>Theme!=null&&Theme.SoundPalette!=null&&GetComponent<Selectable>()!=null&&GetComponent<Selectable>().IsInteractable();
  void Play(string id,float gain){if(CanPlay)PlaytestUiRoot.Instance?.PlayNamedSound(id,gain);}
  // #304: a FocusVisual304 row selects itself on hover, so OnSelect already plays the focus cue (one cue per hover)
  public void OnPointerEnter(PointerEventData data){var fv=GetComponent<FocusVisual304>();if(fv!=null&&fv.SelectOnHover)return;Play("ui_focus",.22f);}
  public void OnSelect(BaseEventData data)=>Play("ui_focus",.22f);
  public void OnSubmit(BaseEventData data)=>Play("ui_select",.3f);
  public void OnPointerDown(PointerEventData data)=>Play("ui_select",.3f);
 }
 public sealed partial class PlaytestUiRoot
 {
  bool suppressNextPageSound255;
  public bool PlayNamedSound(string id,float gain=.5f)
  {var cue=Theme!=null&&Theme.SoundPalette!=null?Theme.SoundPalette.Find(id):null;if(cue?.Clip==null)return false;PlayUi(cue.Clip,gain);return true;}
 }
}
