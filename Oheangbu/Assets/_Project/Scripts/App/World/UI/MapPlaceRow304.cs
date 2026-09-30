using UnityEngine;
using UnityEngine.EventSystems;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 places list row (지도, right column): tells the map which place got the selection. Keyboard / pad
    /// selection centres the map on the place (the list alone is enough to use the map with a pad); a mouse-over selection
    /// (FocusVisual304.HoverSelectFrame) only updates the detail card so the sheet does not jump under the pointer.</summary>
    [DisallowMultipleComponent]
    public sealed class MapPlaceRow304 : MonoBehaviour, ISelectHandler
    {
        public WorldMapPresenter Owner;
        public int Index;
        FocusVisual304 visual;

        public void OnSelect(BaseEventData eventData)
        {
            if (Owner == null) return;
            if (visual == null) visual = GetComponent<FocusVisual304>();
            bool byHover = visual != null && visual.HoverSelectFrame == Time.frameCount;
            Owner.PlaceSelected304(Index, byHover);
        }
    }
}
