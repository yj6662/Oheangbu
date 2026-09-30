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
        Pin,
        Mountain
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
        public bool RequiresArrival;
    }

    [Serializable]
    public sealed class WorldMapZoneSpec
    {
        public bool ExploreWalkedPassages;
        public string DiscoveryRevision = "";
        public Texture2D Illustration;
        public Rect IllustrationWorldUv;
        public string Id;
        public string Label;
        public Vector2[] Polygon = Array.Empty<Vector2>();
        public Vector2[] DetailPath = Array.Empty<Vector2>();
        public WorldMapLineSpec[] DetailLines = Array.Empty<WorldMapLineSpec>();
        public float MinimumY = float.NegativeInfinity;
        public float MaximumY = float.PositiveInfinity;
        public Vector3[] FloorPath = Array.Empty<Vector3>();
        public float FloorClearance = 3f;

        public bool Contains(Vector3 world)
        {
            return world.y >= MinimumY && world.y <= MaximumY &&
                   WorldLocationCatalog.FloorContains(FloorPath,world,FloorClearance) &&
                   WorldMapDiscoveryGrid.Contains(Polygon, new Vector2(world.x, world.z));
        }
    }

    public sealed class WorldMapUiDependencies
    {
        /// <summary>#304: the page style. null = UiStyle304SO.Resolve(Theme), then PlaytestUiRoot.Instance's theme.</summary>
        public UiStyle304SO Style;
        /// <summary>#304: the playtest theme (sounds, Style304). null = PlaytestUiRoot.Instance.Theme.</summary>
        public PlaytestUiThemeSO Theme;
        /// <summary>#304: map screen data (sprites + tunables). null = MapStyle304SO.Resolve() (Resources/UI304/map).</summary>
        public MapStyle304SO MapStyle;
        /// <summary>Support pictures only (#304 brings the text labels back): inn / cave / mountain drawings for 34 px+ marks.</summary>
        public CompactUiProfileSO Icons;
        public WorldMapBakedDataSO BakedData;
        /// <summary>Not read since #304 (map text is TextMeshPro through UiStyle304SO); kept for callers that still set it.</summary>
        public Font Font;
        /// <summary>Fallback paper only; the #304 sheet is Style304.Sprites.SheetMap.</summary>
        public Texture2D PaperTexture;
        public Color Ink = new Color(.12f, .14f, .13f, 1f);
        public Color Paper = new Color(.969f, .945f, .894f, 1f);
        public Color Muted = new Color(.35f, .39f, .36f, 1f);
        public Color Seal = new Color(.58f, .18f, .14f, 1f);
        public bool ReducedMotion;
    }
}
