using System.Linq;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    // #306 IMapMiniSource304 (SPEC-PLAYTEST-306 #1): what the HUD minimap (HudMinimap304) needs from the map. The minimap owns
    // its material (a second PaperMapSurface) and asks for prints of a window; the walked-land state (fog cells, walked cave
    // cells, shortcut lines) stays here and is shared by texture, so the minimap reveals exactly what the unfolded sheet does.
    // Walked land only (CONST-RULES §3-4, AC-1e): the unwalked illustration prints as the bare sheet (no fog-covered picture),
    // no known rivers or realm borders (_MacroInkTex stays empty), no objective area. PrintMini is timed separately from Tick.
    public sealed partial class WorldMapPresenter : IMapMiniSource304
    {
        WorldMapPaperInk miniInk;
        WorldMapZoneSpec miniZone;
        RectTransform miniStub;
        int miniRevision, miniWorldFrame = -1;
        Vector3 miniWorld;
        public double LastMiniPrintMilliseconds { get; private set; }
        public double MeanMiniPrintMilliseconds { get; private set; }
        public long MiniPrints { get; private set; }

        public bool MiniReady => initialized && progressLoaded && data != null && fogTexture != null;
        public Texture MapTex => data != null ? data.DisplayMap : null;
        public Texture FogTex => fogTexture;
        public Texture InkTex => initialized ? MiniInk306().Texture : null;
        public int MiniRevision => miniRevision;
        public bool MiniInterior => initialized && MiniZone306() != null;

        WorldMapPaperInk MiniInk306()
        {
            if (miniInk == null) { miniInk = new WorldMapPaperInk(); ConfigureInk(miniInk); }
            return miniInk;
        }

        /// <summary>CurrentWorld once per frame for the HUD reads (pose, zone, print): seated, it may search for the vehicle.</summary>
        Vector3 MiniWorld306()
        {
            if (miniWorldFrame != Time.frameCount) { miniWorldFrame = Time.frameCount; miniWorld = CurrentWorld(); }
            return miniWorld;
        }

        WorldMapZoneSpec MiniZone306()
        {
            var zone = data.ZoneAt(MiniWorld306());
            if (zone != miniZone) { miniZone = zone; miniRevision++; }
            return zone;
        }

        public Material CreateMiniMaterial()
        {
            if (!initialized) return null;
            Shader shader = Resources.Load<Shader>("WorldMap/PaperMapSurface");
            if (shader == null) return null;
            var m = new Material(shader) { name = "PersistentMinimap_Runtime", hideFlags = HideFlags.DontSave };
            m.SetTexture("_MapTex", data.DisplayMap);
            m.SetFloat("_KnownBase", data.HasIllustration ? 1 : 0);
            m.SetFloat("_PaintedRelief", data.PaintedRelief ? 1 : 0);
            ConfigureFog304(m);
            // walked land only: the full sheet's fog-covered picture (_UnknownShade / _UnknownRelief) would show unwalked terrain
            m.SetFloat("_UnknownVeil", 1f); m.SetFloat("_UnknownShade", 0f); m.SetFloat("_UnknownRelief", 0f);
            m.SetTexture("_FogTex", fogTexture);
            m.SetTexture("_InkTex", MiniInk306().Texture);
            m.SetTexture("_MacroInkTex", Texture2D.blackTexture);
            m.SetVector("_MapWindow", new Vector4(0, 0, 1, 1));   // print = the whole RawImage; the window moves by uvRect
            return m;
        }

        public Rect WindowUv(Vector3 centre, float rangeMetres)
        {
            Vector2 n = projection.WorldToNormalized(new Vector2(centre.x, centre.z));
            float w = rangeMetres * 2f / Mathf.Max(1f, data.BoundsMax.x - data.BoundsMin.x);
            float h = rangeMetres * 2f / Mathf.Max(1f, data.BoundsMax.y - data.BoundsMin.y);
            // unclamped (unlike WorldMapProjection.WorldWindow): off the map edge the shader prints the bare sheet
            return new Rect(n.x - w * .5f, n.y - h * .5f, w, h);
        }

        public bool TryWorldToWindow(Vector3 world, Rect window, out Vector2 window01)
        {
            Vector2 n = projection.WorldToNormalized(new Vector2(world.x, world.z));
            // WorldToNormalized clamps; points off the map fall back to the unclamped formula
            Vector2 size = data.BoundsMax - data.BoundsMin;
            if (world.x < data.BoundsMin.x || world.x > data.BoundsMax.x || world.z < data.BoundsMin.y || world.z > data.BoundsMax.y)
                n = new Vector2((world.x - data.BoundsMin.x) / Mathf.Max(1f, size.x), (world.z - data.BoundsMin.y) / Mathf.Max(1f, size.y));
            window01 = new Vector2((n.x - window.x) / Mathf.Max(1e-6f, window.width), (n.y - window.y) / Mathf.Max(1e-6f, window.height));
            return window01.x >= 0f && window01.x <= 1f && window01.y >= 0f && window01.y <= 1f;
        }

        public void PrintMini(Rect inkWindow, float inkDisplayPx)
        {
            if (!initialized) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            RefreshShortcutLines306();
            WorldMapZoneSpec zone = MiniZone306();
            bool interior = zone != null;
            var ink = MiniInk306();
            ink.projectionWorldHeight = inkWindow.height * (data.BoundsMax.y - data.BoundsMin.y);
            bool daedong = ink.Daedong && !interior;
            // same line sets as the sheet (ApplyUvAndMarkers); the fog mask in the shader keeps unwalked roads off
            ink.Draw(interior ? (zone.Illustration != null ? null : zone.DetailLines) : data.HasIllustration ? (daedong ? straightLines : exploredLines) : data.Lines,
                interior && zone.Illustration == null ? zone.DetailPath : null, projection, inkWindow, new Vector2(inkDisplayPx, inkDisplayPx));
            LastMiniPrintMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            MiniPrints++;
            MeanMiniPrintMilliseconds += (LastMiniPrintMilliseconds - MeanMiniPrintMilliseconds) / MiniPrints;
        }

        public void ApplyMini(Material mini, Rect inkWindow)
        {
            if (mini == null || !initialized) return;
            WorldMapZoneSpec zone = miniZone;
            bool interior = zone != null;
            mini.SetVector("_WorldUv", new Vector4(inkWindow.x, inkWindow.y, inkWindow.width, inkWindow.height));
            WorldMapRegionTile detail = data.PaintedRelief ? data.RegionTiles.FirstOrDefault(t => t != null && t.Texture != null && t.WorldUv.Contains(inkWindow.min) && t.WorldUv.Contains(inkWindow.max)) : null;
            var tile = detail != null ? detail.WorldUv : new Rect(0, 0, 1, 1);
            mini.SetTexture("_MapTex", data.DisplayMap);
            mini.SetTexture("_DetailTex", detail != null ? detail.Texture : data.ExploredMap);
            mini.SetFloat("_HasDetail", data.PaintedRelief && (detail != null || data.ExploredMap != null) ? 1 : 0);
            mini.SetVector("_MapTileUv", new Vector4(tile.x, tile.y, tile.width, tile.height));
            mini.SetTexture("_CaveTex", zone?.Illustration);
            var caveUv = zone?.IllustrationWorldUv ?? new Rect(0, 0, 1, 1);
            mini.SetVector("_CaveUv", new Vector4(caveUv.x, caveUv.y, caveUv.width, caveUv.height));
            mini.SetFloat("_HasCave", interior && zone.Illustration != null ? 1 : 0);
            mini.SetFloat("_Interior", interior ? 1f : 0f);
            // the walked cave plan: the same per-zone mask the sheet reads (updated in place by RevealInterior)
            var entry = GetInteriorDiscovery(zone);
            mini.SetFloat("_ExploreCave", zone != null && zone.ExploreWalkedPassages ? 1 : 0);
            mini.SetTexture("_CaveDiscovery", entry != null ? entry.Mask : Texture2D.blackTexture);
            mini.SetTexture("_FogTex", fogTexture);
            mini.SetTexture("_InkTex", MiniInk306().Texture);
        }

        public void MiniPose(out Vector3 world, out float headingDeg)
        {
            world = MiniWorld306();
            // the unfolded sheet's player arrow rule (PlaceMarks304): the view while seated, else the body
            headingDeg = 0f;
            if (session != null && session.Walker != null)
            {
                if (session.Walker.Seated && session.Walker.ViewCamera != null) headingDeg = session.Walker.ViewCamera.transform.eulerAngles.y;
                else if (session.Walker.Body != null) headingDeg = session.Walker.Body.transform.eulerAngles.y;
            }
        }

        /// <summary>The inactive stub stays behind (OnDestroy removes it): a detached HUD (destroyed before the map) hands MiniRoot
        /// back to it, so callers that only test MiniRoot for null (runtime smoke mapMiniRoot) keep passing.</summary>
        public void AttachMiniRoot(RectTransform root) { if (root != null) MiniRoot = root; }

        public void DetachMiniRoot(RectTransform root) { if (root != null && MiniRoot == root) MiniRoot = miniStub; }
    }
}
