using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>TEST geography authoring source. Coordinates are metres; X east, Z north.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/World/Macro Sheet", fileName = "WorldMacroSheet")]
    public sealed class WorldMacroSheetSO : ScriptableObject
    {
        [Serializable] public sealed class RidgeSpec
        {
            public string Id;
            [Tooltip("XZ crest path; Y is its height contribution above the regional ground.")]
            public Vector3[] Points = Array.Empty<Vector3>();
            public float Width = 700f;
        }
        [Serializable] public sealed class BasinSpec
        {
            public string Id;
            public Vector2 Center;
            public Vector2 Radius;
            public float Floor;
        }
        [Serializable] public sealed class RiverSpec
        {
            public string Id;
            public string ParentId;
            [Tooltip("Upstream to downstream. Y is water elevation, not riverbed height.")]
            public Vector3[] Points = Array.Empty<Vector3>();
            public float Width = 40f;
        }
        [Serializable] public sealed class RouteSpec
        {
            public string Id;
            public string From;
            public string To;
            public bool Carriage;
            public float Width;
            public Vector3[] Points = Array.Empty<Vector3>();
            public string Progression;
        }
        [Serializable] public sealed class SiteSpec
        {
            public string Id;
            public string Label;
            public string Kind;
            public RealmId Realm;
            public Vector3 Position;
        }
        [Serializable] public sealed class RegionSpec
        {
            public string Id;
            public string Label;
            public RealmId Realm;
            public Vector2[] Polygon = Array.Empty<Vector2>();
            public float Density;
        }

        public int Seed = 20260911;
        public Vector2 BoundsMin = new Vector2(-4000, -6000);
        public Vector2 BoundsMax = new Vector2(4000, 6000);
        [Tooltip("Irregular geographic footprint. Bounds only allocate the authoring lattice.")]
        public Vector2[] Outline = Array.Empty<Vector2>();
        public float GridSpacing = 16f;
        public float ChunkSize = 1024f;
        public float CarriageSpeed = 14f;
        public float WalkSpeed = 4.5f;
        [Header("Geological relief; independent of roads and site positions")]
        [Min(0f)] public float FoothillHeight = 0f;
        [Min(32f)] public float FoothillWavelength = 620f;
        [Min(0f)] public float FoothillDetailHeight = 0f;
        [Min(32f)] public float FoothillDetailWavelength = 260f;
        [Range(.1f,.8f)] public float BasinCoreFraction = .45f;
        [Range(.85f,1.5f)] public float BasinRimFraction = 1.25f;
        [Min(80f)] public float ValleyMinimumWidth = 420f;
        [Min(2f)] public float ValleyWidthPerRiverWidth = 11f;
        [Min(12f)] public float ValleyBankTerrace = 12f;
        [Range(0f,.5f)] public float ValleyFoothillRetention = 0f;
        public RidgeSpec[] Ridges = Array.Empty<RidgeSpec>();
        public BasinSpec[] Basins = Array.Empty<BasinSpec>();
        public RiverSpec[] Rivers = Array.Empty<RiverSpec>();
        public RouteSpec[] Routes = Array.Empty<RouteSpec>();
        public SiteSpec[] Sites = Array.Empty<SiteSpec>();
        public RegionSpec[] Regions = Array.Empty<RegionSpec>();

        public SiteSpec FindSite(string id)
        {
            for (int i = 0; i < Sites.Length; i++) if (Sites[i].Id == id) return Sites[i];
            return null;
        }
    }
}
