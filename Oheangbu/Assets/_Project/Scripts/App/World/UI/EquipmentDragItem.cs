using System;
using UnityEngine;
using UnityEngine.EventSystems;
namespace Oheangbu.App.World.UI {
 public sealed class EquipmentDragItem:MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler {
  public string ItemId;public long Revision;public Action<string,Oheangbu.Data.Demo.EquipmentSlot,long> Dropped;
  GameObject ghost;
  public void OnBeginDrag(PointerEventData e){PlaytestUiRoot.Instance?.PlayNamedSound("ui_drag",.4f);var canvas=GetComponentInParent<Canvas>();ghost=new GameObject("DraggedEquipment",typeof(RectTransform),typeof(EquipmentInkGraphic));ghost.transform.SetParent(canvas.transform,false);var r=(RectTransform)ghost.transform;r.sizeDelta=new Vector2(60,60);var g=ghost.GetComponent<EquipmentInkGraphic>();var source=GetComponentInChildren<EquipmentInkGraphic>();g.Symbol=source.Symbol;g.Artwork=source.Artwork;g.color=source.Artwork!=null?new Color(1,1,1,.9f):new Color(.13f,.12f,.09f,.8f);g.raycastTarget=false;OnDrag(e);}
  public void OnDrag(PointerEventData e){if(ghost!=null)ghost.transform.position=e.position;}
  public void OnEndDrag(PointerEventData e){PlaytestUiRoot.Instance?.PlayNamedSound("ui_drop",.3f);if(ghost!=null)Destroy(ghost);}
  void OnDisable(){if(ghost!=null)Destroy(ghost);}
 }
 public sealed class EquipmentDropSlot:MonoBehaviour,IDropHandler {
  public Oheangbu.Data.Demo.EquipmentSlot Slot;
  public void OnDrop(PointerEventData e){var item=e.pointerDrag!=null?e.pointerDrag.GetComponent<EquipmentDragItem>():null;if(item!=null)item.Dropped?.Invoke(item.ItemId,Slot,item.Revision);}
 }
}
