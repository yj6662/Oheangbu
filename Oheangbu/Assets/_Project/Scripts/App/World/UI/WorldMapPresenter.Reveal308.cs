using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    // #308 map 3c (SPEC-MAP-OVERHAUL-308 "표식이 뜨는 때", D308-18 answer 7: a mark shows only where the player has walked).
    // A baked marker WITHOUT an arrival event (WorldMapMarkerSpec.RequiresArrival false: the long wall gate and the two inns of
    // map 3) joined Progress.ui.discoveredMarkers the moment its own 32 m discovery cell was walked - about 90 m away, while the
    // paper still drew its place as unwalked wash (the crisp walked edge lies INSIDE the walked cells). With a notation bundle
    // the marker's reveal rule (MapNotation308SO.MarkerReveal, baked from map308_notation.json "markerReveal") now decides:
    //   FirstContact  the cell test alone (every marker the table does not list: the behaviour before map 3c)
    //   GroundDrawn   the cell test AND the paper draws the ground round the anchor as walked land (MapGround308, read from the
    //                 same fog texels the shader samples)
    //   OnArrival     never by walking (the place's arrival event adds it, WorldMacroPlaytestSession.Locations)
    // The answer is ONE bit in the save (discoveredMarkers), so the HUD minimap, the unfolded sheet, the bearing line, the places
    // list and the map_reveal sound cannot disagree. Without a bundle (n308 == null: the #307 path) the test is the old one.
    // Cost: the rule is read again only after the fog texels changed (fogRevision308), only for a marker whose own cell is walked
    // and whose ground was not drawn yet; once drawn it stays drawn (a discovery grid only gains cells, land never falls).
    // No static state: everything below dies with the presenter.
    public sealed partial class WorldMapPresenter
    {
        sealed class GroundMark308 { public WorldMapMarkerSpec Spec; public int CheckedAt = -1; public bool Drawn; }

        readonly List<GroundMark308> groundMarks308 = new List<GroundMark308>();
        WorldMapDiscoveryGrid groundGrid308;   // the grid the cached answers belong to (a loaded save / a preview makes a new one)
        int fogRevision308;                    // bumped by RefreshFog / RefreshFogAround: the texels the shader samples changed
        bool ignoreReveal308;                  // Edit-mode preview only (PreviewIgnoreReveal308)

        /// <summary>The walk test of a marker without an arrival event (Tick, 5 times a second). The cell test comes first and
        /// alone decides without a bundle.</summary>
        bool WalkReveals308(WorldMapMarkerSpec marker)
        {
            if (!discovery.IsDiscovered(marker.WorldXZ)) return false;
            if (n308 == null || ignoreReveal308) return true;
            switch (n308.RevealOf(marker.Id, out float radius))
            {
                case MapMarkerReveal308.OnArrival: return false;
                case MapMarkerReveal308.GroundDrawn: return GroundDrawn308(marker, radius);
                default: return true;
            }
        }

        bool GroundDrawn308(WorldMapMarkerSpec marker, float radius)
        {
            if (fogPixels == null) return false;
            if (!ReferenceEquals(groundGrid308, discovery))
            {
                groundGrid308 = discovery;
                for (int i = 0; i < groundMarks308.Count; i++) { groundMarks308[i].CheckedAt = -1; groundMarks308[i].Drawn = false; }
            }
            GroundMark308 mark = null;
            for (int i = 0; i < groundMarks308.Count; i++) if (ReferenceEquals(groundMarks308[i].Spec, marker)) { mark = groundMarks308[i]; break; }
            if (mark == null) groundMarks308.Add(mark = new GroundMark308 { Spec = marker });
            if (mark.Drawn) return true;
            if (mark.CheckedAt == fogRevision308) return false;
            mark.CheckedAt = fogRevision308;
            mark.Drawn = GroundAt308(marker.WorldXZ, out int w, out int h, out float cx, out float cy, out float perX, out float perY)
                && MapGround308.Drawn(fogPixels, w, h, cx, cy, perX, perY, radius, n308.GroundStepMetres, n308.GroundNeedLand, n308.GroundKCover);
            return mark.Drawn;
        }

        /// <summary>A world point in the SHADER's fog coordinate: c = worldUv x (fog width, fog height), and the cells per metre of
        /// each axis (on the compact map a fog row is drawn 6000 / 188 m high, not the grid's 32 m). False without a fog.</summary>
        bool GroundAt308(Vector2 worldXZ, out int w, out int h, out float cx, out float cy, out float perX, out float perY)
        {
            w = h = 0; cx = cy = perX = perY = 0f;
            if (fogPixels == null || discovery == null || data == null) return false;
            Vector2 size = data.BoundsMax - data.BoundsMin;
            if (!(size.x > 0f) || !(size.y > 0f)) return false;
            w = discovery.Width; h = discovery.Height;
            perX = w / size.x; perY = h / size.y;
            cx = (worldXZ.x - data.BoundsMin.x) * perX; cy = (worldXZ.y - data.BoundsMin.y) * perY;
            return fogPixels.Length >= w * h;
        }

        // ------------------------------------------------------------------ Edit-mode preview (MapOverhaul308): no Play, nothing saved
        /// <summary>Edit-mode preview: true = every marker keeps the cell test (the picture before map 3c). No-op in Play.</summary>
        public void PreviewIgnoreReveal308(bool ignore) { if (!Application.isPlaying) ignoreReveal308 = ignore; }

        /// <summary>Edit-mode preview: would the game's walk test add this marker on the fog as it stands? An arrival marker is
        /// answered by its cell alone, as the preview always did (it has no arrival events).</summary>
        public bool PreviewWalkReveals308(WorldMapMarkerSpec marker)
        {
            if (marker == null || discovery == null) return false;
            if (marker.RequiresArrival) return discovery.IsDiscovered(marker.WorldXZ);
            return WalkReveals308(marker);
        }

        /// <summary>Edit-mode preview / checks: the marker's rule and what the ground rule reads for it right now.</summary>
        public MapMarkerReveal308 PreviewGround308(WorldMapMarkerSpec marker, out float radius, out float leastLand)
        {
            radius = 0f; leastLand = -1f;
            if (marker == null || n308 == null) return MapMarkerReveal308.FirstContact;
            var rule = n308.RevealOf(marker.Id, out radius);
            if (GroundAt308(marker.WorldXZ, out int w, out int h, out float cx, out float cy, out float perX, out float perY))
                leastLand = MapGround308.MinLand(fogPixels, w, h, cx, cy, perX, perY, radius, n308.GroundStepMetres, n308.GroundKCover, float.NegativeInfinity);
            return rule;
        }
    }
}
