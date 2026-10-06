using UnityEngine;
using UnityEngine.EventSystems;

namespace Oheangbu.App.World.UI
{
    /// <summary>DEAD since D308-25 (map 5): the 지도 page has no places list, nothing adds this component and the presenter
    /// method it called (PlaceSelected304) is gone. The file stays only because a stage copies files and never deletes one
    /// (SPEC-MAP-OVERHAUL-308 Temporary Exceptions): remove it with its .meta once the page layout is confirmed.
    /// Was: #304 places list row - told the map which place got the selection.</summary>
    [DisallowMultipleComponent]
    public sealed class MapPlaceRow304 : MonoBehaviour, ISelectHandler
    {
        public WorldMapPresenter Owner;
        public int Index;

        public void OnSelect(BaseEventData eventData) { }
    }
}
