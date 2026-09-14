using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    [CreateAssetMenu(menuName = "Oheangbu/World/Runtime Map Data", fileName = "WorldMapBakedData")]
    public sealed class WorldMapBakedDataSO : ScriptableObject
    {
        public const int CurrentVersion = 4;

        public int Version = CurrentVersion;
        public string Revision;
        public Vector2 BoundsMin;
        public Vector2 BoundsMax;
        public Vector2[] Outline = Array.Empty<Vector2>();
        public Texture2D BaseMap;
        [Tooltip("Illustrated macro terrain, visible before exploration; paths and places remain discovery gated.")]
        public Texture2D IllustratedMap;
        public WorldMapRegionTile[] RegionTiles = Array.Empty<WorldMapRegionTile>();
        public bool HasIllustration => IllustratedMap != null;
        public Texture2D DisplayMap => IllustratedMap != null ? IllustratedMap : BaseMap;
        public WorldMapLineSpec[] Lines = Array.Empty<WorldMapLineSpec>();
        public WorldMapMarkerSpec[] Markers = Array.Empty<WorldMapMarkerSpec>();
        public WorldMapZoneSpec[] Zones = Array.Empty<WorldMapZoneSpec>();

        public bool IsUsable => Version >= 3 && Version <= CurrentVersion && BaseMap != null && Lines != null &&
                                BoundsMax.x > BoundsMin.x && BoundsMax.y > BoundsMin.y &&
                                Outline != null && Outline.Length >= 3;

        public WorldMapZoneSpec ZoneAt(Vector3 world)
        {
            if (Zones == null) return null;
            for (int i = 0; i < Zones.Length; i++)
                if (Zones[i] != null && Zones[i].Contains(world)) return Zones[i];
            return null;
        }
    }

    [Serializable]
    public sealed class WorldMapRegionTile
    {
        public string Id;
        public Texture2D Texture;
        public Rect WorldUv;
    }
}
