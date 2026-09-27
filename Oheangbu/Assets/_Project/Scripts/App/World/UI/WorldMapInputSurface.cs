using UnityEngine;
using UnityEngine.EventSystems;

namespace Oheangbu.App.World.UI
{
    internal sealed class WorldMapInputSurface : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler
    {
        public WorldMapPresenter Owner;
        bool dragged;
        bool held;

        public void OnPointerEnter(PointerEventData data) => OnPointerMove(data);
        public void OnPointerMove(PointerEventData data) { if (!held) Owner?.MapPointer(data.position, data.enterEventCamera); }
        public void OnPointerExit(PointerEventData data) => Owner?.HideMapHover();
        public void OnPointerUp(PointerEventData data) { held = false; OnPointerMove(data); }
        public void OnEndDrag(PointerEventData data) { held = false; OnPointerMove(data); }
        void OnDisable() { held = false; Owner?.HideMapHover(); }

        public void OnPointerDown(PointerEventData eventData) { dragged = false; held = true; Owner?.HideMapHover(); }

        public void OnBeginDrag(PointerEventData eventData) { dragged = false; held = true; Owner?.HideMapHover(); }

        public void OnDrag(PointerEventData eventData)
        {
            dragged |= eventData.delta.sqrMagnitude > 1;
            var rect=(RectTransform)transform;
            if(RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,eventData.position,eventData.pressEventCamera,out Vector2 current) &&
               RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,eventData.position-eventData.delta,eventData.pressEventCamera,out Vector2 previous))
                Owner?.Pan(current-previous);
        }

        public void OnScroll(PointerEventData eventData)
            => Owner?.Zoom(eventData.scrollDelta.y, eventData.position, eventData.pressEventCamera);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!dragged && eventData.button == PointerEventData.InputButton.Left)
                Owner?.PlacePinFromScreen(eventData.position, eventData.pressEventCamera);
        }
    }
}
