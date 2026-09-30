using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    // #304 contract between the world map (implements) and the HUD bearing line (reads). Read-only view of what the map already knows.
    public enum MapMarkerKind304 { Rest, Place, Objective, Coin, Pin }

    public struct MapMarker304
    {
        public MapMarkerKind304 Kind;
        public Vector3 World;
        public string Label;
    }

    public interface IMapMarkerSource304
    {
        /// <summary>Appends the markers the player has discovered (rests, places, heard objectives, dropped coins, the player pin). Returns the count added.</summary>
        int CollectMarkers(List<MapMarker304> into);
        /// <summary>True inside caves and interiors, where the bearing line switches polarity.</summary>
        bool IsIndoor { get; }
    }
}
