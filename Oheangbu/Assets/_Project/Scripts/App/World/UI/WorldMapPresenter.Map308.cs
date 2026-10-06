using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308: what the HUD minimap needs from the map besides IMapMiniSource304 when the map carries a notation bundle
    /// (SPEC-MAP-OVERHAUL-308). The map owns the bundle, the parsed strips and the stroke states; the minimap owns its own
    /// strip graphic and materials. Notation308 == null = this map draws the pre-#308 way (no bundle, or one baked for
    /// another map).</summary>
    public interface IMapNotationSource308
    {
        MapNotation308SO Notation308 { get; }
        MapStrokes308Data Strokes308 { get; }
        /// <summary>stateOpen[state - 1] for strokes with a state; the revision bumps when a state opens.</summary>
        bool[] StrokeStates308(out int revision);
        /// <summary>Discovered shortcut paths the bake does not carry (drawn as trails).</summary>
        IReadOnlyList<Vector2[]> ExtraStrokes308 { get; }
        /// <summary>A UI/MapStroke308 material reading this map's fog texture. The caller owns and destroys it.</summary>
        Material CreateStrokeMaterial308();
        /// <summary>A UI/MapIcon308 material with the hanji rim colour. The caller owns and destroys it. Null without icons.</summary>
        Material CreateIconMaterial308();
        /// <summary>Scale-dependent paper values (pattern period, shore width, wash kind, rim width) for a minimap material.</summary>
        void ApplyMiniBand308(Material paper, float metresPerPx, float screenPerReferencePx);
        /// <summary>Atlas cell of a CollectMarkers entry (rest, place, coin, pin); false = draw it the pre-#308 way.</summary>
        bool TryMarkerCell308(MapMarker304 marker, out int cell);
    }

    // #308 notation on the unfolded sheet (SPEC-MAP-OVERHAUL-308, D308-14 / 14b / 15): the bundle (MapStyle304SO.Notation308),
    // the paper material's _MAP308 values, the brush strips of the print window, icons from the atlas, the hanji plate and
    // placement of the one name tag (D308-25: the pointed mark's), the lacquer board under the sheet, the region-gated reveal
    // and the stroke states. D308-25 took the two-part legend and the places list's glyphs out with their run-time materials.
    // Everything is off (n308 == null) when the map has no bundle or the bundle was baked for another map:
    // then the pre-#308 code runs untouched. No static state; run-time objects are destroyed in Dispose308.
    public sealed partial class WorldMapPresenter : IMapNotationSource308
    {
        MapNotation308SO n308;
        MapStrokes308Data strokes308;
        byte[] regions308;                       // 125 x 188 region numbers, south row first; null = no gate
        Func<Vector2, bool> revealGate308;
        int revealRegion308, regionsWidth308, regionsHeight308;
        bool[] stateOpen308 = Array.Empty<bool>();
        int stateRevision308, stateShortcuts308 = -1, stateFacts308 = -1;
        readonly List<Vector2[]> extraStrokes308 = new List<Vector2[]>();
        MapStrokes308Graphic fullStrokes308;
        RectTransform fullStrokesHost308;
        CanvasGroup fullStrokesGroup308;
        Material strokeMaterial308, iconMaterial308;
        Texture2D clearInk308;
        Rect strokeUv308 = new Rect(-1f, -1f, 0f, 0f);
        Vector2 strokeWindow308 = new Vector2(-1f, -1f);
        float paperMetresPerPx308 = -1f;
        bool inkCleared308;

        public MapNotation308SO Notation308 => n308;
        public MapStrokes308Data Strokes308 => strokes308;
        public IReadOnlyList<Vector2[]> ExtraStrokes308 => extraStrokes308;
        /// <summary>#308 probe: strip mesh rebuilds, vertices and mean build time of the unfolded sheet (AC-P1 / P3).</summary>
        public int FullStripUploads308 => fullStrokes308 != null ? fullStrokes308.Uploads : 0;
        public int FullStripVertices308 => fullStrokes308 != null ? fullStrokes308.VertexCount : 0;

        static Vector4 Srgb308(Color c) => new Vector4(c.r, c.g, c.b, 1f);   // tokens as sRGB values: the shaders convert once

        // ------------------------------------------------------------------ bundle (Initialize, before the fog is built)
        void InitNotation308()
        {
            n308 = null; strokes308 = null; regions308 = null; revealGate308 = null;
            var candidate = mapStyle != null ? mapStyle.Notation308 : null;
            if (candidate == null || ownsRuntimeData || !candidate.IsUsableFor(data)) return;
            var parsed = MapStrokes308Data.Parse(candidate.Strokes.bytes, out string error);
            if (parsed == null) { Debug.LogWarning("[Map308] map308_strokes.bytes rejected (" + error + "): the map draws the pre-#308 way."); return; }
            if ((parsed.Min - data.BoundsMin).sqrMagnitude > .01f || (parsed.Max - data.BoundsMax).sqrMagnitude > .01f)
            { Debug.LogWarning("[Map308] the strips' bounds differ from the map's: the map draws the pre-#308 way."); return; }
            n308 = candidate; strokes308 = parsed;
            regionsWidth308 = Mathf.CeilToInt((data.BoundsMax.x - data.BoundsMin.x) / WorldMapDiscoveryGrid.CellSize);
            regionsHeight308 = Mathf.CeilToInt((data.BoundsMax.y - data.BoundsMin.y) / WorldMapDiscoveryGrid.CellSize);
            if (n308.RegionGatedReveal && n308.RevealRegions != null)
            {
                var bytes = n308.RevealRegions.bytes;
                bool any = false;
                if (bytes != null && bytes.Length == regionsWidth308 * regionsHeight308)
                    for (int i = 0; i < bytes.Length && !any; i++) any = bytes[i] != 0 && bytes[i] != 255;
                // a file with no closed region gates nothing: keep the plain radius reveal (same call as before #308)
                if (any) { regions308 = bytes; revealGate308 = RevealVisible308; }
            }
        }

        // ------------------------------------------------------------------ reveal (§6.3: a cliff-top field is not revealed from its foot)
        int RegionAt308(Vector2 worldXZ)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((worldXZ.x - data.BoundsMin.x) / WorldMapDiscoveryGrid.CellSize), 0, regionsWidth308 - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((worldXZ.y - data.BoundsMin.y) / WorldMapDiscoveryGrid.CellSize), 0, regionsHeight308 - 1);
            return regions308[y * regionsWidth308 + x];
        }

        /// <summary>The `visible` filter of WorldMapDiscoveryGrid.Reveal for this position: null (no filter) without closed regions.
        /// A cell opens when it is common ground (0), a face nobody can stand on (255), or the region the player stands in.</summary>
        Func<Vector2, bool> RevealGate308(Vector3 current)
        {
            if (revealGate308 == null) return null;
            revealRegion308 = RegionAt308(new Vector2(current.x, current.z));
            return revealGate308;
        }

        bool RevealVisible308(Vector2 cellCentre)
        {
            int region = RegionAt308(cellCentre);
            return region == 0 || region == 255 || region == revealRegion308;
        }

        // ------------------------------------------------------------------ paper material (sheet, minimap)
        /// <summary>_MAP308 and the values that do not depend on the scale. No-op without a bundle.</summary>
        void ApplyPaper308(Material material, bool mini)
        {
            if (n308 == null || material == null) return;
            material.EnableKeyword("_MAP308");
            material.SetTexture("_Terrain308", n308.Terrain);
            material.SetTexture("_Pattern308", n308.Pattern);
            material.SetVector("_M308Paper", Srgb308(n308.Paper));
            material.SetVector("_M308Unknown", Srgb308(n308.Unknown));
            material.SetVector("_M308Ink", Srgb308(n308.Ink));
            material.SetVector("_M308Grain", new Vector4(Mathf.Clamp01(n308.Grain.x), Mathf.Clamp01(n308.Grain.y), 0f, 0f));
            material.SetVector("_M308Edge", n308.EdgeShape);   // the crisp walked edge: the strips' material carries the same values
            var bands = n308.CaveBands;
            bands.y = Mathf.Max(bands.x + .001f, bands.y); bands.z = Mathf.Max(bands.y + .001f, bands.z); bands.w = Mathf.Clamp01(bands.w);
            material.SetVector("_M308Cave", bands);
            if (clearInk308 != null) material.SetTexture("_MacroInkTex", clearInk308);
            PaperBand308(material, mini ? 1.2f : 2.7f, 1f, mini);
        }

        /// <summary>The values that follow the scale: pattern period of the zoom band, shore line width in metres, slope wash or
        /// elevation wash, and the walked-edge ink rim (its width in screen px, inside the crisp edge).
        /// #308 map 4: mini = the HUD minimap's material (the broken dry-brush rim, as before); otherwise the unfolded sheet
        /// (the notation's thin whole rim).</summary>
        void PaperBand308(Material material, float metresPerPx, float screenPerReferencePx, bool mini = false)
        {
            if (n308 == null || material == null) return;
            metresPerPx = Mathf.Max(.01f, metresPerPx);
            int band = n308.Band(metresPerPx);
            float period = n308.PatternPeriod(band);
            Vector2 size = data.BoundsMax - data.BoundsMin;
            material.SetVector("_M308Tile", new Vector4(size.x / period, size.y / period, 0f, 0f));
            bool world = band >= 3;   // the whole-world band: elevation wash for the slope wash, no rock strokes, lighter dots and ripples
            float top = Mathf.Max(1f, n308.ElevationMaxMetres), screen = Mathf.Max(.1f, screenPerReferencePx);
            material.SetVector("_M308Wash", new Vector4(Mathf.Clamp01(n308.SlopeWash), Mathf.Clamp01(n308.ElevationWash), world ? 1f : 0f, 0f));
            material.SetVector("_M308Elev", new Vector4(n308.ElevationRangeMetres.x / top, Mathf.Max(n308.ElevationRangeMetres.x + 1f, n308.ElevationRangeMetres.y) / top,
                Mathf.Max(.01f, n308.RippleFeatherMetres), 0f));
            material.SetVector("_M308Forest", new Vector4(Mathf.Clamp01(n308.ForestShowFrom), Mathf.Max(.1f, n308.ForestGain), Mathf.Clamp01(world ? n308.ForestInkWorld : n308.ForestInk), 0f));
            material.SetVector("_M308Rock", new Vector4(Mathf.Clamp(n308.RockFrom - Mathf.Max(0f, n308.RockSoftness), 0f, .999f), world ? 0f : Mathf.Clamp01(n308.RockInk),
                Mathf.Clamp01(n308.ShoreInk), Mathf.Max(0f, n308.RippleFromMetres)));
            // shore line: half width in metres, and one screen px in metres for its edge
            material.SetVector("_M308Water", new Vector4(Mathf.Max(1f, n308.WaterRangeMetres), Mathf.Max(0f, n308.ShorePx) * .5f * metresPerPx,
                Mathf.Max(.01f, metresPerPx / screen), Mathf.Clamp01(world ? n308.RippleInkWorld : n308.RippleInk)));
            // #308 map 4 (D308-24 answer 9): the minimap's vector is the one it always got (z = w = 0: the shader's sheet block is not even
            // compiled into its variant). The sheet gets the notation's thin whole rim: z = 0 / 1, w = its width in screen px, and the
            // rim's ink alpha goes from the minimap's to the sheet's with z.
            if (mini) material.SetVector("_M308Rim", new Vector4(Mathf.Clamp01(n308.EdgeRimAlpha), Mathf.Max(.5f, n308.EdgeRimPx * screen), 0f, 0f));
            else
            {
                float whole = Mathf.Clamp01(n308.SheetRimWhole);
                material.SetVector("_M308Rim", new Vector4(Mathf.Clamp01(Mathf.Lerp(n308.EdgeRimAlpha, n308.SheetRimAlpha, whole)), Mathf.Max(.5f, n308.EdgeRimPx * screen),
                    whole, Mathf.Max(.5f, n308.SheetRimPx * screen)));
            }
        }

        public void ApplyMiniBand308(Material paper, float metresPerPx, float screenPerReferencePx) => PaperBand308(paper, metresPerPx, screenPerReferencePx, true);

        /// <summary>Edit-mode preview (MapOverhaul308, #308 map 4): the rim vector the unfolded sheet's material holds right now -
        /// (ink alpha, broken rim px, whole rim 0 / 1, whole rim px), read back from the material, not from the notation.</summary>
        public Vector4 PreviewSheetRim308 => paperMaterial != null && paperMaterial.HasProperty("_M308Rim") ? paperMaterial.GetVector("_M308Rim") : Vector4.zero;

        /// <summary>A 1 x 1 clear texture for the ink layers the #308 path does not print.</summary>
        Texture2D ClearInk308()
        {
            if (clearInk308 != null) return clearInk308;
            clearInk308 = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "Map308ClearInk", hideFlags = HideFlags.DontSave };
            clearInk308.SetPixels32(new[] { new Color32(0, 0, 0, 0) }); clearInk308.Apply(false, true);
            return clearInk308;
        }

        /// <summary>True when the sheet's CPU ink raster is not needed for this view: outdoors the strips carry every line and
        /// the realm border dots are gone (§1). Inside a zone without a cave plan the detail lines are still printed.</summary>
        bool SkipRaster308(bool interior, WorldMapZoneSpec zone)
        {
            if (n308 == null || paperMaterial == null) return false;
            bool raster = interior && zone != null && zone.Illustration == null;
            if (raster) { if (inkCleared308) { paperMaterial.SetTexture("_InkTex", paperInk.Texture); inkCleared308 = false; } return false; }
            if (!inkCleared308) { paperMaterial.SetTexture("_InkTex", ClearInk308()); inkCleared308 = true; }
            return true;
        }

        // ------------------------------------------------------------------ strips
        public Material CreateStrokeMaterial308()
        {
            if (n308 == null || n308.StrokeShader == null) return null;
            var m = new Material(n308.StrokeShader) { name = "MapStroke308_Runtime", hideFlags = HideFlags.DontSave };
            m.SetTexture("_FogTex", fogTexture);
            m.SetFloat("_FogSoft", mapStyle.FogSoftEdge ? 1f : 0f);
            m.SetFloat("_FogNoise", Mathf.Max(0f, mapStyle.FogEdgeNoise));
            m.SetVector("_S308Edge", n308.EdgeShape);          // = the paper's _M308Edge: a strip ends where the paper's land ends
            m.SetVector("_S308Ink", Srgb308(n308.Ink));
            m.SetVector("_S308Paper", Srgb308(n308.Paper));
            float footprint = .8f * Mathf.Clamp(n308.AtlasRowPad, 1f, 24f) / (Mathf.Max(8f, n308.AtlasRowPx) * Mathf.Max(1, n308.AtlasRows));
            m.SetVector("_S308Opt", new Vector4(1f, 1f, Mathf.Max(1f, n308.InkGamma), footprint));
            m.SetVector("_S308Frame", new Vector4(294f, 210f, 1f, Mathf.Clamp01(n308.PaperBandAlpha)));   // the owner sets its window size
            return m;
        }

        public Material CreateIconMaterial308()
        {
            if (n308 == null || !n308.HasIcons) return null;
            var m = new Material(n308.IconShader) { name = "MapIcon308_Runtime", hideFlags = HideFlags.DontSave };
            m.SetColor("_Rim", UiStyle304SO.A(n308.Paper, 1f));
            m.SetColor("_Ink2", UiStyle304SO.A(n308.Ink, 1f));   // the second ink of the brush mark (ferrule, handle, edge)
            m.SetFloat("_I308Gamma", Mathf.Max(1f, n308.InkGamma));
            return m;
        }

        Material IconMaterial308() => iconMaterial308 != null ? iconMaterial308 : iconMaterial308 = CreateIconMaterial308();

        public bool[] StrokeStates308(out int revision) { revision = stateRevision308; return stateOpen308; }

        /// <summary>Stroke states from the save (discovered shortcuts, facts). Runs when either count changed; true = changed.</summary>
        bool RefreshStates308()
        {
            if (n308 == null) return false;
            var campaign = session.Progress != null ? session.Progress.campaign : null;
            int shortcuts = campaign != null && campaign.DiscoveredShortcuts != null ? campaign.DiscoveredShortcuts.Count : 0;
            int facts = campaign != null && campaign.Facts != null ? campaign.Facts.Count : 0;
            if (shortcuts == stateShortcuts308 && facts == stateFacts308) return false;
            stateShortcuts308 = shortcuts; stateFacts308 = facts;
            int count = n308.States != null ? n308.States.Count : 0;
            if (stateOpen308.Length != count) stateOpen308 = new bool[count];
            for (int i = 0; i < count; i++)
            {
                var state = n308.States[i];
                bool open = false;
                if (state != null && campaign != null && !string.IsNullOrEmpty(state.Key))
                    open = state.Kind == "fact" ? campaign.Facts != null && campaign.Facts.Contains(state.Key)
                                                : campaign.DiscoveredShortcuts != null && campaign.DiscoveredShortcuts.Contains(state.Key);
                stateOpen308[i] = open;
            }
            // a discovered shortcut the bake does not carry (no stroke with its id hash) is drawn from the scene's path
            extraStrokes308.Clear();
            if (displayedShortcuts != null && campaign != null && campaign.DiscoveredShortcuts != null)
                foreach (var shortcut in displayedShortcuts)
                {
                    if (shortcut == null || shortcut.Path == null || shortcut.Path.Length < 2 || !campaign.DiscoveredShortcuts.Contains(shortcut.Id)) continue;
                    if (strokes308.FindStroke(MapStrokes308Data.Fnv1a32(shortcut.Id)) >= 0) continue;
                    var points = new Vector2[shortcut.Path.Length];
                    for (int i = 0; i < points.Length; i++) points[i] = new Vector2(shortcut.Path[i].x, shortcut.Path[i].z);
                    extraStrokes308.Add(points);
                }
            stateRevision308++;
            return true;
        }

        /// <summary>The sheet's strip graphic, under the print window before the marks (BuildFull).</summary>
        void BuildStrokes308(RectTransform window)
        {
            if (n308 == null) return;
            strokeMaterial308 = CreateStrokeMaterial308();
            if (strokeMaterial308 == null) return;
            fullStrokes308 = MapStrokes308Graphic.CreateHost("MapStrokes308", window, out fullStrokesHost308, out _);
            fullStrokes308.Bind(strokes308, n308, strokeMaterial308);
            fullStrokesGroup308 = fullStrokesHost308.gameObject.AddComponent<CanvasGroup>();
            fullStrokesGroup308.interactable = fullStrokesGroup308.blocksRaycasts = false;
            fullStrokesGroup308.alpha = 0f;
        }

        /// <summary>The strips appear with the marks, once the sheet lies flat.</summary>
        void ApplyFold308()
        {
            if (fullStrokesGroup308 != null) fullStrokesGroup308.alpha = fold >= .995f ? 1f : 0f;
        }

        float ScreenPerReferencePx308()
        {
            var canvas = FullRoot != null ? FullRoot.GetComponentInParent<Canvas>() : null;
            float scale = canvas != null && canvas.rootCanvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            return Mathf.Max(.1f, scale * pageScale);
        }

        /// <summary>Every ApplyUvAndMarkers while the sheet is open: the strips of the view at its scale (a rebuild only when the
        /// view leaves the loaded bins or the scale changes; a pan is a RectTransform move) and the paper's band values.</summary>
        void SyncStrokes308(bool interior)
        {
            if (n308 == null || foldMap == null) return;
            RefreshStates308();
            Vector2 size = data.BoundsMax - data.BoundsMin;
            Vector2 windowPx = foldMap.rect.size;
            float k = Mathf.Max(.01f, pageScale);
            var view = new Rect(data.BoundsMin.x + fullUv.x * size.x, data.BoundsMin.y + fullUv.y * size.y, fullUv.width * size.x, fullUv.height * size.y);
            float canvasPerMetre = windowPx.x / Mathf.Max(1f, view.width);
            float pagePerMetre = canvasPerMetre / k;                       // reference px per metre
            float metresPerPx = 1f / Mathf.Max(1e-5f, pagePerMetre);
            if (!Mathf.Approximately(metresPerPx, paperMetresPerPx308))
            { paperMetresPerPx308 = metresPerPx; PaperBand308(paperMaterial, metresPerPx, ScreenPerReferencePx308()); }
            if (fullStrokes308 == null) return;
            bool show = !interior;
            if (fullStrokesHost308.gameObject.activeSelf != show) fullStrokesHost308.gameObject.SetActive(show);
            if (!show) return;
            if (fullUv != strokeUv308 || windowPx != strokeWindow308)
            {
                strokeUv308 = fullUv; strokeWindow308 = windowPx;
                strokeMaterial308.SetVector("_S308View", new Vector4(fullUv.x, fullUv.y, 1f / Mathf.Max(1e-6f, fullUv.width), 1f / Mathf.Max(1e-6f, fullUv.height)));
                strokeMaterial308.SetVector("_S308Turn", new Vector4(1f, 0f, 1f, 0f));
                strokeMaterial308.SetVector("_S308Frame", new Vector4(windowPx.x / k, windowPx.y / k, 1f, Mathf.Clamp01(n308.PaperBandAlpha)));
            }
            fullStrokes308.Show(view, strokes308.BinMetres * .5f, pagePerMetre, stateOpen308, stateRevision308, extraStrokes308);
            var rect = fullStrokes308.rectTransform;
            var scale = new Vector3(k, k, 1f);
            if (rect.localScale != scale) rect.localScale = scale;
            Vector2 at = (fullStrokes308.Origin - view.center) * canvasPerMetre;
            if (rect.anchoredPosition != at) rect.anchoredPosition = at;
        }

        // ------------------------------------------------------------------ icons (sheet)
        /// <summary>Atlas cell of a baked marker, or -1 (no bundle, no icon atlas, or no glyph for it).</summary>
        int MarkerCell308(WorldMapMarkerSpec spec)
        {
            if (n308 == null || spec == null || IconMaterial308() == null) return -1;
            return n308.TryMarkerCell(spec.Id, spec.Kind, spec.Label, out int cell) ? cell : -1;
        }

        int KeyCell308(string key, string fallbackKey = null)
        {
            if (n308 == null || IconMaterial308() == null) return -1;
            if (n308.TryKeyCell(key, out int cell)) return cell;
            return fallbackKey != null && n308.TryKeyCell(fallbackKey, out cell) ? cell : -1;
        }

        /// <summary>A mark drawn from the icon atlas (ink drawing + hanji rim in one quad), built like CreateMark: inactive, centred
        /// pivot, scaled with the page. Null when cell &lt; 0 (the caller builds the pre-#308 mark).</summary>
        RectTransform GlyphMark308(string name, int cell, Color ink, float size)
        {
            if (cell < 0 || n308 == null) return null;
            var root = V.Rect("Marker_" + name, fullMarkers, 0, 0, size, size);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            var glyph = MapGlyph308Graphic.Create("Icon", root, n308.IconAtlas(size), IconMaterial308(), cell, ink, Mathf.Clamp01(n308.IconRimAlpha), size);
            var r = glyph.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            root.gameObject.SetActive(false);
            scaledMarks.Add(root);
            return root;
        }

        /// <summary>The wake ring (the rest you wake at: same symbol + outer ink ring) from the atlas, WakeRingScale x the symbol.
        /// Null = the #304 ring sprite.</summary>
        GameObject WakeRing308(RectTransform root)
        {
            int cell = KeyCell308(MapNotation308SO.KeyWakeRing);
            if (cell < 0) return null;
            float k = Mathf.Max(1f, n308.WakeRingScale), size = root.sizeDelta.x * k;
            var ring = MapGlyph308Graphic.Create("VariantRing", root, n308.IconAtlas(size), IconMaterial308(), cell, style.Ink, Mathf.Clamp01(n308.IconRimAlpha), size);
            var r = ring.rectTransform;
            r.anchorMin = Vector2.one * (.5f - k * .5f); r.anchorMax = Vector2.one * (.5f + k * .5f); r.offsetMin = r.offsetMax = Vector2.zero;
            r.SetAsFirstSibling();
            return ring.gameObject;
        }

        public bool TryMarkerCell308(MapMarker304 marker, out int cell)
        {
            cell = -1;
            if (n308 == null || !n308.HasIcons) return false;
            switch (marker.Kind)
            {
                case MapMarkerKind304.Coin: return n308.TryKeyCell(MapNotation308SO.KeyCoin, out cell);
                case MapMarkerKind304.Pin: return n308.TryKeyCell(MapNotation308SO.KeyPin, out cell);
                case MapMarkerKind304.Rest:
                case MapMarkerKind304.Place:
                {
                    var xz = new Vector2(marker.World.x, marker.World.z);
                    for (int i = 0; i < markers.Count; i++)
                        if ((markers[i].Spec.WorldXZ - xz).sqrMagnitude < .25f) { cell = markers[i].Cell308; return cell >= 0; }
                    // the rest you wake at when it is not a baked rest
                    return marker.Kind == MapMarkerKind304.Rest &&
                           (n308.TryKeyCell(MapNotation308SO.KeyCheckpoint, out cell) || n308.TryKeyCell(WorldMapMarkerKind.Rest.ToString(), out cell));
                }
                default: return false;
            }
        }

        // ------------------------------------------------------------------ place names: hanji plates, priority, clear of roads (§4)
        sealed class LabelSlot308 { public TMP_Text Label; public RectTransform Plate; public float Icon; public int Priority, Slot = -2; public bool Wanted; public Vector2 Free; }
        // name priorities outside the baked markers' own (LabelPriority308); the pick's tie-break reads the same numbers
        const int RankCheckpoint308 = 0, RankDrop308 = 20, RankPin308 = 21;
        const int SlotFree308 = 4;               // not one of the four sides: the tag's centre is the icon + LabelSlot308.Free
        const float TagEdgePx308 = 2f;           // a tag keeps this far inside the print window
        readonly List<LabelSlot308> labelSlots308 = new List<LabelSlot308>();
        readonly Dictionary<TMP_Text, LabelSlot308> labelSlotOf308 = new Dictionary<TMP_Text, LabelSlot308>();
        readonly List<Rect> placedLabels308 = new List<Rect>();
        Rect labelUv308; float labelScale308 = -1f; int labelWanted308 = -1;
        Func<Vector2, bool> walked308;           // cached delegate of Walked308 (no allocation per label)
        bool Walked308(Vector2 worldXZ) => discovery != null && discovery.IsDiscovered(worldXZ);

        /// <summary>CreateFullLabel calls this BEFORE it creates the label, so the plate lies under the text. Null without a bundle.</summary>
        RectTransform CreatePlate308(string name)
        {
            if (n308 == null) return null;
            var plate = V.Rect("Plate_" + name, fullLabels, 0, 0, 10, 10);
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(.5f, .5f);
            V.Image(plate, UiStyle304SO.A(n308.Paper, Mathf.Clamp01(n308.PlateAlpha)));
            if (mapStyle.SlipFrame != null)
            {
                var line = V.Image(V.Stretch("Line", plate), UiStyle304SO.A(n308.Ink, Mathf.Clamp01(n308.PlateLineAlpha)), mapStyle.SlipFrame);
                if (mapStyle.SlipFrame.border.sqrMagnitude > 0f) { line.type = Image.Type.Sliced; line.fillCenter = false; }
            }
            plate.gameObject.SetActive(false);
            return plate;
        }

        void RegisterLabel308(TMP_Text label, RectTransform plate)
        {
            if (label == null || plate == null || labelSlotOf308.ContainsKey(label)) return;
            var slot = new LabelSlot308 { Label = label, Plate = plate, Priority = 9 };
            labelSlots308.Add(slot); labelSlotOf308[label] = slot;
        }

        void RankLabel308(TMP_Text label, int priority, float iconPx)
        {
            if (label != null && labelSlotOf308.TryGetValue(label, out var slot)) { slot.Priority = priority; slot.Icon = iconPx; }
        }

        // the notation's glyph order (inn > village > gaekju > gate > peak > the rest); without a glyph, the marker kind
        int LabelPriority308(MarkerView marker)
        {
            if (marker.Cell308 >= 0) return 1 + n308.LabelRank(marker.Cell308);
            switch (marker.Spec.Kind)
            {
                case WorldMapMarkerKind.Rest: case WorldMapMarkerKind.Checkpoint: return 1;
                case WorldMapMarkerKind.Settlement: return 2;
                case WorldMapMarkerKind.Gate: return 4;
                case WorldMapMarkerKind.Mountain: return 5;
                default: return 6;
            }
        }

        /// <summary>After BuildMarks304: priorities (rest &gt; settlement &gt; gate &gt; peak &gt; the rest) and the icon each name hangs on.</summary>
        void RankLabels308()
        {
            if (n308 == null) return;
            foreach (var marker in markers) RankLabel308(marker.FullLabel, LabelPriority308(marker), marker.Size);
            RankLabel308(fullCheckpointLabel, RankCheckpoint308, mapStyle.RestSize);
            RankLabel308(fullDropLabel, RankDrop308, mapStyle.CoinSize);
            RankLabel308(fullPinLabel, RankPin308, mapStyle.PinSize);
            RankLabel308(knownAreaLabel, -1, 0f);          // centred under its ring: only the plate follows it
            labelSlots308.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>After PlaceMarks304 placed the name the old way (right of its icon): the visible name takes the first of
        /// right / left / above / below that is inside the window, clear of the paper's title slip and north mark and of the
        /// roads. D308-25: there is one name at most (the pointed mark's) and it must show - when every side crosses a road the
        /// roads are let go, and when no side has room at all (a corner of the window, beside the slip) the right-hand place
        /// moves the least distance that fits (FitLabel304), at most MapStyle304SO.HoverTagMaxShiftPx. The heard objective's
        /// name keeps its place under the ring and is moved whole into the window the same way. The choice is redone only when
        /// the view, the page scale or the shown name changes; the placing itself is cheap and runs every call.</summary>
        void ArrangeLabels308()
        {
            if (n308 == null || labelSlots308.Count == 0 || foldMap == null) return;
            int wanted = 0;
            for (int i = 0; i < labelSlots308.Count; i++)
            {
                var slot = labelSlots308[i];
                slot.Wanted = slot.Label != null && slot.Label.gameObject.activeSelf;
                if (slot.Wanted) wanted = wanted * 31 + i + 1;
            }
            bool decide = fullUv != labelUv308 || !Mathf.Approximately(pageScale, labelScale308) || wanted != labelWanted308;
            Rect window = foldMap.rect;
            float k = Mathf.Max(.01f, pageScale);
            Vector2 pad = n308.PlatePad * k;
            int obstacles = 0;
            if (decide)
            {
                labelUv308 = fullUv; labelScale308 = pageScale; labelWanted308 = wanted;
                placedLabels308.Clear();
                AddObstacle304(titleSlip304, ref obstacles);
                AddObstacle304(northLabel304, ref obstacles);
                AddObstacle304(northLine304, ref obstacles);
                for (int i = 0; i < obstacles; i++) placedLabels308.Add(labelObstacles304[i]);
            }
            Vector2 world = data.BoundsMax - data.BoundsMin;
            for (int i = 0; i < labelSlots308.Count; i++)
            {
                var slot = labelSlots308[i];
                if (!slot.Wanted) { if (slot.Plate.gameObject.activeSelf) slot.Plate.gameObject.SetActive(false); continue; }
                var rect = slot.Label.rectTransform;
                Vector2 v = rect.anchorMin;
                Vector2 at = new Vector2(Mathf.Lerp(window.xMin, window.xMax, v.x), Mathf.Lerp(window.yMin, window.yMax, v.y));
                Vector2 text = rect.sizeDelta * k, box = text + 2f * pad;
                float reach = (slot.Icon * .5f + n308.LabelGap) * k;
                if (slot.Priority < 0)
                {
                    // the objective name keeps its own place (centred under the ring); the plate goes under it.
                    // D308-25: moved whole into the window, clear of the slip and the north mark (PlaceCentredLabel resets the
                    // base position on every call, so the stored shift is added once per placement)
                    Shown308(slot, true);
                    if (decide)
                    {
                        Vector2 centre = at + rect.anchoredPosition, fitted = centre;
                        slot.Free = FitLabel304(ref fitted, box * .5f, window, TagEdgePx308, obstacles) ? fitted - centre : Vector2.zero;
                    }
                    if (slot.Free != Vector2.zero) rect.anchoredPosition += slot.Free;
                    FitPlate308(slot, v, rect.pivot, rect.anchoredPosition, rect.sizeDelta);
                    if (decide) placedLabels308.Add(new Rect(at.x + rect.anchoredPosition.x - box.x * .5f, at.y + rect.anchoredPosition.y - box.y * .5f, box.x, box.y));
                    continue;
                }
                if (decide)
                {
                    slot.Slot = -1;
                    // pass 0: clear of the shown roads; pass 1 (D308-25): the roads let go - the pointed mark's name must show
                    for (int pass = 0; pass < 2 && slot.Slot < 0; pass++)
                    for (int c = 0; c < 4 && slot.Slot < 0; c++)
                    {
                        Rect candidate = Candidate308(c, at, box, reach);
                        if (candidate.xMin < window.xMin + TagEdgePx308 || candidate.xMax > window.xMax - TagEdgePx308 || candidate.yMin < window.yMin + TagEdgePx308 || candidate.yMax > window.yMax - TagEdgePx308) continue;
                        bool clear = true;
                        for (int p = 0; p < placedLabels308.Count && clear; p++) clear = !candidate.Overlaps(placedLabels308[p]);
                        if (!clear) continue;
                        // window px -> world metres
                        float x0 = data.BoundsMin.x + (fullUv.x + Mathf.InverseLerp(window.xMin, window.xMax, candidate.xMin) * fullUv.width) * world.x;
                        float x1 = data.BoundsMin.x + (fullUv.x + Mathf.InverseLerp(window.xMin, window.xMax, candidate.xMax) * fullUv.width) * world.x;
                        float z0 = data.BoundsMin.y + (fullUv.y + Mathf.InverseLerp(window.yMin, window.yMax, candidate.yMin) * fullUv.height) * world.y;
                        float z1 = data.BoundsMin.y + (fullUv.y + Mathf.InverseLerp(window.yMin, window.yMax, candidate.yMax) * fullUv.height) * world.y;
                        // only roads the sheet shows count (walked land, open states): an unseen road must not push a name aside (#214)
                        if (pass == 0 && strokes308.AnySegmentInRect(x0, z0, x1, z1, (int)MapStrokeClass308.Trail, (int)MapStrokeClass308.Highway, walked308 ?? (walked308 = Walked308), stateOpen308)) continue;
                        slot.Slot = c; placedLabels308.Add(candidate);
                    }
                    if (slot.Slot < 0)
                    {
                        // no side has room (a window corner, beside the slip or the north mark): the right-hand place, moved the
                        // least distance that puts the whole plate inside the window and clear of both
                        Vector2 wanted0 = Candidate308(0, at, box, reach).center, fitted = wanted0;
                        if (FitLabel304(ref fitted, box * .5f, window, TagEdgePx308, obstacles) && (fitted - wanted0).magnitude <= Mathf.Max(0f, mapStyle.HoverTagMaxShiftPx) * k)
                        { slot.Slot = SlotFree308; slot.Free = fitted - at; placedLabels308.Add(new Rect(fitted.x - box.x * .5f, fitted.y - box.y * .5f, box.x, box.y)); }
                    }
                }
                if (slot.Slot < 0) { Shown308(slot, false); continue; }
                Shown308(slot, true);
                Vector2 pivot, position; TextAlignmentOptions align;
                switch (slot.Slot)
                {
                    case 0: pivot = new Vector2(0f, .5f); position = new Vector2(reach + pad.x, 0f); align = TextAlignmentOptions.Left; break;
                    case 1: pivot = new Vector2(1f, .5f); position = new Vector2(-reach - pad.x, 0f); align = TextAlignmentOptions.Right; break;
                    case 2: pivot = new Vector2(.5f, 0f); position = new Vector2(0f, reach + pad.y); align = TextAlignmentOptions.Center; break;
                    case SlotFree308: pivot = new Vector2(.5f, .5f); position = slot.Free; align = TextAlignmentOptions.Center; break;
                    default: pivot = new Vector2(.5f, 1f); position = new Vector2(0f, -reach - pad.y); align = TextAlignmentOptions.Center; break;
                }
                if (rect.pivot != pivot) rect.pivot = pivot;
                if (rect.anchoredPosition != position) rect.anchoredPosition = position;
                if (slot.Label.alignment != align) slot.Label.alignment = align;
                FitPlate308(slot, v, pivot, position, rect.sizeDelta);
            }
        }

        // A name that found no place is hidden by its text alpha (PlaceFullLabel keeps deciding its GameObject's active state
        // every call; toggling that back and forth would rebuild the TMP mesh every frame).
        static void Shown308(LabelSlot308 slot, bool shown)
        {
            float alpha = shown ? 1f : 0f;
            if (!Mathf.Approximately(slot.Label.alpha, alpha)) slot.Label.alpha = alpha;
            if (!shown && slot.Plate.gameObject.activeSelf) slot.Plate.gameObject.SetActive(false);
        }

        // the plate rect (window px) of candidate c: 0 right, 1 left, 2 above, 3 below the icon
        static Rect Candidate308(int c, Vector2 at, Vector2 box, float reach)
        {
            switch (c)
            {
                case 0: return new Rect(at.x + reach, at.y - box.y * .5f, box.x, box.y);
                case 1: return new Rect(at.x - reach - box.x, at.y - box.y * .5f, box.x, box.y);
                case 2: return new Rect(at.x - box.x * .5f, at.y + reach, box.x, box.y);
                default: return new Rect(at.x - box.x * .5f, at.y - reach - box.y, box.x, box.y);
            }
        }

        // the plate takes the label's anchor and pivot and grows PlatePad around it (both carry the page scale as localScale)
        void FitPlate308(LabelSlot308 slot, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var plate = slot.Plate;
            if (!plate.gameObject.activeSelf) plate.gameObject.SetActive(true);
            Vector2 pad = n308.PlatePad;
            plate.anchorMin = plate.anchorMax = anchor; plate.pivot = pivot;
            plate.sizeDelta = size + 2f * pad;
            float k = Mathf.Max(.01f, pageScale);
            plate.anchoredPosition = position + new Vector2((pivot.x - .5f) * 2f * pad.x * k, (pivot.y - .5f) * 2f * pad.y * k);
        }

        // ------------------------------------------------------------------ the lacquer board under the sheet (§5, D308-15)
        /// <summary>Under the veil page (same page px and scale as the page chrome, fades with the veil): the board with the sheet
        /// lying on it: a lacquer body and over it the kit's Frame.MapBoard (tiled 9-slice, 80 + 9 n by 82 + 9 m px, open centre).
        /// Without the kit sprite the body alone is drawn. D308-25: the board is the sheet's rect grown by MapStyle304SO.BoardGrow
        /// (1592 x 910 at (164, 126) for the 1556 x 820 sheet: n = 168, m = 92); the bundle's BoardRect (836 x 910, the old
        /// sheet) is not read until the bundle is baked again (SPEC-MAP-OVERHAUL-308 Temporary Exceptions).</summary>
        void BuildBoard308()
        {
            if (n308 == null || veilPage304 == null) return;
            Rect sheet = mapStyle.SheetRect; Vector4 grow = mapStyle.BoardGrow;
            Rect b = new Rect(sheet.x - grow.x, sheet.y - grow.y, sheet.width + grow.x + grow.z, sheet.height + grow.y + grow.w);
            var board = V.Image(V.Rect("MapBoard308", veilPage304, b.x, b.y, b.width, b.height), n308.Lacquer);
            board.raycastTarget = false;
            if (n308.FrameBoard != null)
            {
                var frame = V.Image(V.Stretch("Frame", board.rectTransform), n308.FrameTint, n308.FrameBoard);
                if (n308.FrameBoard.border.sqrMagnitude > 0f) { frame.type = Image.Type.Tiled; frame.fillCenter = false; }
            }
            if (n308.LatticeBand == null || n308.LatticeAlpha <= 0f) return;
            // the kit's lattice tile along the four edge bands, faint (pattern contrast under the theme's limit)
            float band = Mathf.Max(4f, n308.BoardBand - 6f), inset = 3f;
            Color tint = UiStyle304SO.A(n308.Nacre, Mathf.Clamp01(n308.LatticeAlpha));
            Lattice308(board.rectTransform, "LatticeTop", inset + band, inset, b.width - 2f * (inset + band), band, tint);
            Lattice308(board.rectTransform, "LatticeBottom", inset + band, b.height - inset - band, b.width - 2f * (inset + band), band, tint);
            Lattice308(board.rectTransform, "LatticeLeft", inset, inset + band, band, b.height - 2f * (inset + band), tint);
            Lattice308(board.rectTransform, "LatticeRight", b.width - inset - band, inset + band, band, b.height - 2f * (inset + band), tint);
        }

        void Lattice308(RectTransform parent, string name, float x, float y, float w, float h, Color tint)
        {
            var image = V.Image(V.Rect(name, parent, x, y, w, h), tint, n308.LatticeBand);
            image.type = Image.Type.Tiled;
        }

        // ------------------------------------------------------------------ teardown
        // Destroy in Play; in Edit mode (the MapOverhaul308 preview) objects are destroyed at once
        static void Kill308(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void Dispose308()
        {
            Kill308(strokeMaterial308); Kill308(iconMaterial308); Kill308(clearInk308);
            strokeMaterial308 = iconMaterial308 = null;
            clearInk308 = null; fullStrokes308 = null; n308 = null; strokes308 = null; regions308 = null; revealGate308 = null;
            labelSlots308.Clear(); labelSlotOf308.Clear(); extraStrokes308.Clear();
        }

        /// <summary>Edit-mode preview teardown (MapOverhaul308): MonoBehaviour.OnDestroy does not run outside Play, so the
        /// run-time materials and textures this presenter made are released here, at once. No-op in Play (OnDestroy does it).</summary>
        public void ReleasePreview308()
        {
            if (Application.isPlaying) return;
            Dispose308();
            Kill308(paperMaterial); paperMaterial = null;
            foreach (var entry in interiors.Values) Kill308(entry.Mask);
            interiors.Clear();
            if (macroInk != null) Kill308(macroInk.Texture);
            if (paperInk != null) Kill308(paperInk.Texture);
            if (miniInk != null) Kill308(miniInk.Texture);
            macroInk = paperInk = miniInk = null;
            Kill308(fogTexture); fogTexture = null;
            if (ownsRuntimeData && data != null) { Kill308(data.BaseMap); Kill308(data); }
            initialized = false;
        }

        /// <summary>Edit-mode preview: every cell walked, none, or left as loaded; then the fog texture is rebuilt.</summary>
        public void PreviewReveal308(bool? all, Vector2 around, float radius)
        {
            if (Application.isPlaying || discovery == null) return;
            if (all.HasValue)
            {
                var bytes = new byte[discovery.ByteCount];
                if (all.Value) for (int i = 0; i < bytes.Length; i++) bytes[i] = 255;
                discovery = new WorldMapDiscoveryGrid(data.BoundsMin, data.BoundsMax, data.Outline, bytes);
                fogPlayable = null;
            }
            if (radius > 0f)
            {
                // the game's own reveal (radius steps of the real 96 m disc, with the region gate) along a circle walk
                int steps = Mathf.Max(1, Mathf.CeilToInt(radius / (WorldMapDiscoveryGrid.RevealRadius * .5f)));
                for (int ring = 0; ring <= steps; ring++)
                {
                    float r = Mathf.Max(0f, radius - WorldMapDiscoveryGrid.RevealRadius) * ring / steps;
                    int spokes = ring == 0 ? 1 : Mathf.Max(6, Mathf.CeilToInt(2f * Mathf.PI * r / (WorldMapDiscoveryGrid.RevealRadius * .5f)));
                    for (int k = 0; k < spokes; k++)
                    {
                        float a = 2f * Mathf.PI * k / spokes;
                        Vector2 p = around + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        discovery.Reveal(p, WorldMapDiscoveryGrid.RevealRadius, RevealGate308(new Vector3(p.x, 0f, p.y)));
                    }
                }
            }
            progressLoaded = true;
            RefreshFog();
        }

        /// <summary>Edit-mode preview: the cell under a world point is walked (after PreviewReveal308).</summary>
        public bool PreviewDiscovered308(Vector2 worldXZ) => discovery != null && discovery.IsDiscovered(worldXZ);

        /// <summary>Edit-mode preview: lay the sheet out and place everything for the current view (what Tick does in Play).</summary>
        public void PreviewRefresh308()
        {
            if (Application.isPlaying || !initialized) return;
            RefreshFullLayout(true);
            ApplyFold();
            ApplyUvAndMarkers(CurrentWorld());
        }
    }
}
