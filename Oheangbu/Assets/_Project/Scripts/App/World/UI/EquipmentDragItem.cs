using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace Oheangbu.App.World.UI {
 /// <summary>Drag source on a 보유 격자 chip (Gear_&lt;id&gt;). #304: the ghost is an 88² chip (tile_chip paper + the item art)
 /// following the pointer; it never captures the pointer, so the EquipmentDropSlot under it still receives OnDrop.
 /// Hierarchy: DraggedEquipmentChip (CanvasGroup, blocksRaycasts off) → Chip (Image) → DraggedEquipment (EquipmentInkGraphic,
 /// the harness name, colour white).</summary>
 public sealed class EquipmentDragItem:MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler {
  public string ItemId;public long Revision;public Action<string,Oheangbu.Data.Demo.EquipmentSlot,long> Dropped;
  const float GhostChip=88f,GhostArt=74f;
  GameObject ghost;
  public void OnBeginDrag(PointerEventData e){
   PlaytestUiRoot.Instance?.PlayNamedSound("ui_drag",.4f);
   if(ghost!=null)Destroy(ghost);
   var canvas=GetComponentInParent<Canvas>();if(canvas==null)return;
   var root=canvas.rootCanvas!=null?canvas.rootCanvas.transform:canvas.transform;
   ghost=new GameObject("DraggedEquipmentChip",typeof(RectTransform),typeof(CanvasGroup));ghost.transform.SetParent(root,false);ghost.transform.SetAsLastSibling();
   var group=ghost.GetComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;
   var r=(RectTransform)ghost.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(GhostChip,GhostChip);
   var theme=PlaytestUiRoot.Instance!=null?PlaytestUiRoot.Instance.Theme:null;var s=UiStyle304SO.Resolve(theme);
   var chip=new GameObject("Chip",typeof(RectTransform),typeof(Image));chip.transform.SetParent(ghost.transform,false);
   var cr=(RectTransform)chip.transform;cr.anchorMin=Vector2.zero;cr.anchorMax=Vector2.one;cr.offsetMin=cr.offsetMax=Vector2.zero;
   var paper=chip.GetComponent<Image>();paper.raycastTarget=false;paper.sprite=s.Sprites.TileChip;
   if(paper.sprite!=null){paper.color=Color.white;if(paper.sprite.border.sqrMagnitude>0f)paper.type=Image.Type.Sliced;}else paper.color=s.Chip;
   var art=new GameObject("DraggedEquipment",typeof(RectTransform),typeof(EquipmentInkGraphic));art.transform.SetParent(chip.transform,false);
   var ar=(RectTransform)art.transform;ar.anchorMin=ar.anchorMax=ar.pivot=new Vector2(.5f,.5f);ar.sizeDelta=new Vector2(GhostArt,GhostArt);ar.anchoredPosition=Vector2.zero;
   var g=art.GetComponent<EquipmentInkGraphic>();var source=GetComponentInChildren<EquipmentInkGraphic>();
   if(source!=null){g.Symbol=source.Symbol;g.Artwork=source.Artwork;}
   g.color=g.Artwork!=null?new Color(1,1,1,.95f):UiStyle304SO.A(s.Ink,.8f);g.raycastTarget=false;
   OnDrag(e);
  }
  public void OnDrag(PointerEventData e){if(ghost!=null)ghost.transform.position=e.position;}
  public void OnEndDrag(PointerEventData e){PlaytestUiRoot.Instance?.PlayNamedSound("ui_drop",.3f);if(ghost!=null)Destroy(ghost);ghost=null;}
  void OnDisable(){if(ghost!=null)Destroy(ghost);ghost=null;}
 }
 public sealed class EquipmentDropSlot:MonoBehaviour,IDropHandler {
  public Oheangbu.Data.Demo.EquipmentSlot Slot;
  public void OnDrop(PointerEventData e){var item=e.pointerDrag!=null?e.pointerDrag.GetComponent<EquipmentDragItem>():null;if(item!=null)item.Dropped?.Invoke(item.ItemId,Slot,item.Revision);}
 }
}
