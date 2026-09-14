using UnityEngine;
using UnityEngine.EventSystems;

namespace Oheangbu.App.World.UI
{
    internal sealed class WorldMapInputSurface : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IScrollHandler, IPointerClickHandler
    {
        public WorldMapPresenter Owner;
        bool dragged;

        public void OnPointerDown(PointerEventData eventData) { dragged = false; }

        public void OnBeginDrag(PointerEventData eventData) { dragged = false; }

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
