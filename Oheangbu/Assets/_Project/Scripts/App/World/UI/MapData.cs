using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    public enum WorldMapMarkerKind
    {
        Place,
        Settlement,
        Gate,
        Rest,
        Checkpoint,
        Drop,
        Pin
    }

    public enum WorldMapLineKind
    {
        River,
        Road,
        Trail,
        DetailFill,
        DetailOutline
    }

    [Serializable]
    public sealed class WorldMapLineSpec
    {
        public string Id;
        public WorldMapLineKind Kind;
        public Vector2[] Points = Array.Empty<Vector2>();
        public float PixelWidth = 2f;
    }

    [Serializable]
    public sealed class WorldMapMarkerSpec
    {
        public string Id;
        public string Label;
        public WorldMapMarkerKind Kind;
        public Vector2 WorldXZ;
        public string CompletionId;
        public string ZoneId;
        public bool InitiallyDiscovered;
    }

    [Serializable]
    public sealed class WorldMapZoneSpec
    {
        public string Id;
        public string Label;
        public Vector2[] Polygon = Array.Empty<Vector2>();
        public Vector2[] DetailPath = Array.Empty<Vector2>();
        public WorldMapLineSpec[] DetailLines = Array.Empty<WorldMapLineSpec>();
        public float MinimumY = float.NegativeInfinity;
        public float MaximumY = float.PositiveInfinity;

        public bool Contains(Vector3 world)
        {
            return world.y >= MinimumY && world.y <= MaximumY &&
                   WorldMapDiscoveryGrid.Contains(Polygon, new Vector2(world.x, world.z));
        }
    }

    public sealed class WorldMapUiDependencies
    {
        public WorldMapBakedDataSO BakedData;
        public Font Font;
        public Texture2D PaperTexture;
        public Color Ink = new Color(.12f, .14f, .13f, 1f);
        public Color Paper = new Color(.969f, .945f, .894f, 1f);
        public Color Muted = new Color(.35f, .39f, .36f, 1f);
        public Color Seal = new Color(.58f, .18f, .14f, 1f);
        public bool ReducedMotion;
    }
}
