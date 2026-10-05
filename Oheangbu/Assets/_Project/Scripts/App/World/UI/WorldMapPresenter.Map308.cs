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
    // the paper material's _MAP308 values, the brush strips of the print window, icons from the atlas, hanji plates and
    // placement of the place names, the two-part legend, the lacquer board under the sheet, the region-gated reveal and the
    // stroke states. Everything is off (n308 == null) when the map has no bundle or the bundle was baked for another map:
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
        Material strokeMaterial308, iconMaterial308, legendStrokeMaterial308, forestMaterial308, waterMaterial308;
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

        // ------------------------------------------------------------------ paper material (sheet, minimap, legend swatch)
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
            PaperBand308(material, mini ? 1.2f : 2.7f, 1f);
        }

        /// <summary>The values that follow the scale: pattern period of the zoom band, shore line width in metres, slope wash or
        /// elevation wash, and the walked-edge ink rim (its width in screen px, inside the crisp edge).</summary>
        void PaperBand308(Material material, float metresPerPx, float screenPerReferencePx)
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
            material.SetVector("_M308Rim", new Vector4(Mathf.Clamp01(n308.EdgeRimAlpha), Mathf.Max(.5f, n308.EdgeRimPx * screen), 0f, 0f));
        }

        public void ApplyMiniBand308(Material paper, float metresPerPx, float screenPerReferencePx) => PaperBand308(paper, metresPerPx, screenPerReferencePx);

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
        sealed class LabelSlot308 { public TMP_Text Label; public RectTransform Plate; public float Icon; public int Priority, Slot = -2; public bool Wanted; }
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
            RankLabel308(fullCheckpointLabel, 0, mapStyle.RestSize);
            RankLabel308(fullDropLabel, 20, mapStyle.CoinSize);
            RankLabel308(fullPinLabel, 21, mapStyle.PinSize);
            RankLabel308(knownAreaLabel, -1, 0f);          // centred under its ring: only the plate follows it
            labelSlots308.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>After PlaceMarks304 placed every name the old way (right of its icon): each visible name takes the first of
        /// right / left / above / below that is inside the window, clear of the names already placed, of the paper's title slip
        /// and of the roads; a name with no such place is hidden (its icon stays). The choice is redone only when the view, the
        /// page scale or the set of visible names changes; the placing itself is cheap and runs every call.</summary>
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
            if (decide)
            {
                labelUv308 = fullUv; labelScale308 = pageScale; labelWanted308 = wanted;
                placedLabels308.Clear();
                int obstacles = 0;
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
                    // the objective name keeps its own place (centred under the ring); the plate goes under it
                    Shown308(slot, true);
                    FitPlate308(slot, v, rect.pivot, rect.anchoredPosition, rect.sizeDelta);
                    if (decide) placedLabels308.Add(new Rect(at.x + rect.anchoredPosition.x - box.x * .5f, at.y + rect.anchoredPosition.y - box.y * .5f, box.x, box.y));
                    continue;
                }
                if (decide)
                {
                    slot.Slot = -1;
                    for (int c = 0; c < 4 && slot.Slot < 0; c++)
                    {
                        Rect candidate = Candidate308(c, at, box, reach);
                        if (candidate.xMin < window.xMin + 2f || candidate.xMax > window.xMax - 2f || candidate.yMin < window.yMin + 2f || candidate.yMax > window.yMax - 2f) continue;
                        bool clear = true;
                        for (int p = 0; p < placedLabels308.Count && clear; p++) clear = !candidate.Overlaps(placedLabels308[p]);
                        if (!clear) continue;
                        // window px -> world metres
                        float x0 = data.BoundsMin.x + (fullUv.x + Mathf.InverseLerp(window.xMin, window.xMax, candidate.xMin) * fullUv.width) * world.x;
                        float x1 = data.BoundsMin.x + (fullUv.x + Mathf.InverseLerp(window.xMin, window.xMax, candidate.xMax) * fullUv.width) * world.x;
                        float z0 = data.BoundsMin.y + (fullUv.y + Mathf.InverseLerp(window.yMin, window.yMax, candidate.yMin) * fullUv.height) * world.y;
                        float z1 = data.BoundsMin.y + (fullUv.y + Mathf.InverseLerp(window.yMin, window.yMax, candidate.yMax) * fullUv.height) * world.y;
                        // only roads the sheet shows count (walked land, open states): an unseen road must not push a name aside (#214)
                        if (strokes308.AnySegmentInRect(x0, z0, x1, z1, (int)MapStrokeClass308.Trail, (int)MapStrokeClass308.Highway, walked308 ?? (walked308 = Walked308), stateOpen308)) continue;
                        slot.Slot = c; placedLabels308.Add(candidate);
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

        // ------------------------------------------------------------------ legend: marker rows on plates + terrain and roads (§4)
        Material LegendStrokeMaterial308()
        {
            if (legendStrokeMaterial308 != null || n308 == null) return legendStrokeMaterial308;
            legendStrokeMaterial308 = CreateStrokeMaterial308();
            if (legendStrokeMaterial308 == null) return null;
            legendStrokeMaterial308.name = "MapStroke308_Legend";
            var opt = legendStrokeMaterial308.GetVector("_S308Opt");
            legendStrokeMaterial308.SetVector("_S308Opt", new Vector4(0f, 0f, opt.z, opt.w));   // a sample: no fog, no window
            return legendStrokeMaterial308;
        }

        Material PatternMaterial308(bool water)
        {
            ref Material slot = ref (water ? ref waterMaterial308 : ref forestMaterial308);
            if (slot != null || n308 == null || n308.IconShader == null) return slot;
            slot = new Material(n308.IconShader) { name = water ? "MapPattern308_Water" : "MapPattern308_Forest", hideFlags = HideFlags.DontSave };
            slot.SetVector("_InkChan", water ? new Vector4(0f, 1f, 0f, 0f) : new Vector4(1f, 0f, 0f, 0f));
            slot.SetVector("_RimChan", Vector4.zero); slot.SetVector("_Ink2Chan", Vector4.zero);   // the pattern's B is another picture
            slot.SetFloat("_I308Gamma", Mathf.Max(1f, n308.InkGamma));
            return slot;
        }

        /// <summary>A thin nacre inlay line (kit cell line_najeon_thin, tiled along x, baked colours; a plain nacre rule without the kit).</summary>
        Image InlayLine308(Transform parent, string name, float x, float y, float width)
        {
            bool kit = n308.InlayLine != null;
            float height = kit ? n308.InlayLine.rect.height * 100f / Mathf.Max(1f, n308.InlayLine.pixelsPerUnit) : 2f;
            var image = V.Image(V.Rect(name, parent, x, y, width, height), kit ? Color.white : n308.Nacre, n308.InlayLine);
            if (kit) image.type = Image.Type.Tiled;
            return image;
        }

        /// <summary>The #308 legend: the seven marker rows (same words, atlas glyphs on small lacquer plates, pitch LegendRowPitch)
        /// and under them the terrain-and-roads cells, each a sample drawn from the stroke atlas / pattern itself. False = no
        /// bundle (the caller builds the #304 legend).</summary>
        bool BuildLegend308(RectTransform root)
        {
            if (n308 == null) return false;
            var s = style;
            V.Label(s, root, "LegendCaption", "범례", UiType304.Meta20, s.Mist, 64, 292);
            InlayLine308(root, "LegendInlay", 64, 320, 456);
            var rows = mapStyle.Legend != null && mapStyle.Legend.Count > 0 ? mapStyle.Legend : MapStyle304SO.DefaultLegend();
            float pitch = Mathf.Max(48f, n308.LegendRowPitch);
            int shown = Mathf.Min(rows.Count, 7);
            for (int i = 0; i < shown; i++)
            {
                var row = rows[i]; if (row == null) continue;
                float y = 332 + pitch * i;
                var item = V.Rect("Legend_" + i, root, 0, 0, 1, 1);
                if (!LegendGlyph308(item, row.Symbol, 64, y)) LegendSymbol304(item, row.Symbol, 64, y);
                V.Label(s, item, "Name", row.Name, UiType304.Label24, s.Paper, 126, y + 2);
                V.Label(s, item, "Meaning", row.Meaning, UiType304.Meta20, s.Mist, 126, y + 36, 410, 0, TextAlignmentOptions.TopLeft, true);
            }
            var cells = n308.Legend;
            if (cells == null || cells.Count == 0) return true;
            float top = 332 + pitch * shown + 6f;
            V.Label(s, root, "TerrainCaption", n308.LegendTerrainCaption, UiType304.Meta20, s.Mist, 64, top);
            InlayLine308(root, "TerrainInlay", 64, top + 28, 456);
            for (int i = 0; i < cells.Count && i < 9; i++)
            {
                var cell = cells[i]; if (cell == null) continue;
                float x = 64 + 154 * (i % 3), y = top + 40 + 42 * (i / 3);
                var item = V.Rect("Terrain_" + i, root, x, y, 150, 28);
                var chip = V.Rect("Sample", item, 0, 2, 44, 24);
                V.Image(chip, n308.Paper);
                LegendSample308(chip, cell);
                V.Label(s, item, "Name", cell.Name, UiType304.Meta20, s.Paper, 54, 0);
            }
            return true;
        }

        // the sample on its paper chip: a piece of the class row at the sheet's default scale, or a patch of the pattern
        void LegendSample308(RectTransform chip, MapLegendCell308 cell)
        {
            const float metresPerPx = 2.66f;   // the sheet's default view (band Z2): what the legend is read against
            int band = n308.Band(metresPerPx);
            switch (cell.Kind)
            {
                case MapLegendKind308.Stroke:
                {
                    var material = LegendStrokeMaterial308();
                    if (material == null) return;
                    var go = V.Stretch("Stroke", chip).gameObject;
                    go.AddComponent<MapStrokeSwatch308Graphic>().Configure(n308, cell.StrokeClass, cell.Rank, cell.UnderClass, band, metresPerPx, material);
                    return;
                }
                case MapLegendKind308.Forest:
                case MapLegendKind308.Water:
                {
                    bool water = cell.Kind == MapLegendKind308.Water;
                    var material = PatternMaterial308(water);
                    if (material == null || n308.Pattern == null) return;
                    float periodPx = n308.PatternPeriod(band) / metresPerPx;
                    var glyph = MapGlyph308Graphic.Create("Pattern", chip, n308.Pattern, material, -1,
                        UiStyle304SO.A(n308.Ink, water ? n308.RippleInk : n308.ForestInk), 0f, 44f);
                    var r = glyph.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
                    glyph.SetTile(new Vector2(44f / periodPx, 24f / periodPx));
                    return;
                }
                default:
                    if (n308.TryCell(cell.Glyph, out int atlasCell) && IconMaterial308() != null)
                    {
                        var glyph = MapGlyph308Graphic.Create("Glyph", chip, n308.IconAtlas(24f), IconMaterial308(), atlasCell, n308.Ink, 0f, 24f);
                        glyph.rectTransform.sizeDelta = new Vector2(24f, 24f);
                    }
                    return;
            }
        }

        /// <summary>A marker row's symbol from the atlas on a small lacquer plate (kit slot Plate.Icon). False = no glyph for it.</summary>
        bool LegendGlyph308(RectTransform parent, MapLegendSymbol304 symbol, float x, float y)
        {
            int cell; Color ink = n308.Nacre;
            switch (symbol)
            {
                case MapLegendSymbol304.Player: cell = KeyCell308(MapNotation308SO.KeyPlayer); ink = style.Cinnabar; break;
                case MapLegendSymbol304.Rest: cell = KeyCell308(WorldMapMarkerKind.Rest.ToString()); break;
                case MapLegendSymbol304.Place: cell = KeyCell308(WorldMapMarkerKind.Place.ToString()); break;
                case MapLegendSymbol304.Coin: cell = KeyCell308(MapNotation308SO.KeyCoin); break;
                case MapLegendSymbol304.Pin: cell = KeyCell308(MapNotation308SO.KeyPin); break;
                case MapLegendSymbol304.Objective: cell = KeyCell308(MapNotation308SO.KeyObjective); ink = style.Cinnabar; break;
                default: return false;    // the unwalked swatch keeps its own drawing (the print shader)
            }
            if (cell < 0) return false;
            var plate = V.Rect("Plate", parent, x, y, 44, 44);
            var image = V.Image(plate, n308.PlateIcon != null ? Color.white : n308.Lacquer, n308.PlateIcon != null ? n308.PlateIcon : style.Sprites.Disc);
            if (n308.PlateIcon != null && n308.PlateIcon.border.sqrMagnitude > 0f) image.type = Image.Type.Sliced;
            else image.preserveAspect = true;
            // the rest row shows the wake ring around its symbol (the legend says "고리 두른 곳에서 깬다")
            int ring = symbol == MapLegendSymbol304.Rest ? KeyCell308(MapNotation308SO.KeyWakeRing) : -1;
            if (ring >= 0) MapGlyph308Graphic.Create("Ring", plate, n308.IconAtlas(40f), IconMaterial308(), ring, ink, 0f, 40f);
            var glyph = MapGlyph308Graphic.Create("Symbol", plate, n308.IconAtlas(32f), IconMaterial308(), cell, ink, 0f, ring >= 0 ? 26f : 32f);
            // on the lacquer plate there is no hanji rim under the glyph: the brush's ferrule and handle are drawn in the paper tone
            if (symbol == MapLegendSymbol304.Player) { glyph.rectTransform.localEulerAngles = new Vector3(0f, 0f, -38f); glyph.SetSecondInk(1f, true); }
            return true;
        }

        /// <summary>The places list's 30 px icon from the atlas (the glyph the place has on the paper). Null = the #304 pictogram.</summary>
        Graphic ListGlyph308(RectTransform row, PlaceEntry304 entry)
        {
            if (n308 == null || IconMaterial308() == null) return null;
            int cell = -1;
            if (entry.Spec != null)
            {
                for (int i = 0; i < markers.Count && cell < 0; i++) if (markers[i].Spec == entry.Spec) cell = markers[i].Cell308;
            }
            else if (entry.Kind == MapMarkerKind304.Rest) cell = KeyCell308(MapNotation308SO.KeyCheckpoint, WorldMapMarkerKind.Rest.ToString());
            else if (entry.Kind == MapMarkerKind304.Coin) cell = KeyCell308(MapNotation308SO.KeyCoin);
            if (cell < 0) return null;
            var glyph = MapGlyph308Graphic.Create("Icon", row, n308.IconAtlas(30f), IconMaterial308(), cell, style.Paper, 0f, 30f);
            var r = glyph.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(54f, -18f);
            return glyph;
        }

        // ------------------------------------------------------------------ the lacquer board under the sheet (§5, D308-15)
        /// <summary>Under the veil page (same page px and scale as the legend, fades with the veil): the board (BoardRect, 836 x 910)
        /// with the sheet lying on it: a lacquer body and over it the kit's Frame.MapBoard (tiled 9-slice, 80 + 9 n by 82 + 9 m px,
        /// open centre). Without the kit sprite the body alone is drawn.</summary>
        void BuildBoard308()
        {
            if (n308 == null || veilPage304 == null) return;
            Rect b = n308.BoardRect;
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
            Kill308(strokeMaterial308); Kill308(iconMaterial308); Kill308(legendStrokeMaterial308);
            Kill308(forestMaterial308); Kill308(waterMaterial308); Kill308(clearInk308);
            strokeMaterial308 = iconMaterial308 = legendStrokeMaterial308 = forestMaterial308 = waterMaterial308 = null;
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
            Kill308(legendSwatch304); Kill308(legendFog304); Kill308(legendClear304);
            legendSwatch304 = null; legendFog304 = legendClear304 = null;
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
