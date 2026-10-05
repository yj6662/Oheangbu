using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    // #304 지도 page chrome (DESIGN §7.6, map.png, 1920x1080 page px). Everything lives in mapLayer (above the menu layer);
    // the rail (지도 selected, [M] 닫기 "Close", Q / E) is PlaytestUiRoot's MapRail304 in MenuRailLayer304 above this layer.
    //   MapVeil304 (Page304): the page veil, lifted at 1960 (= full), faded with the fold on close
    //   TwiceFoldedHanji (the paper; WorldMapPresenter.cs)
    //   MapPage304 (Page304, CanvasGroup = mapControls, visible once the sheet is flat):
    //     left   지금 있는 곳 / LegendPanel (범례 7 rows, [L] folds it)
    //     right  발견한 장소 list (Place_<id> rows, ScrollRect) + detail card + 손으로 hints
    //     bottom PaperMapControls: Current [R] / Whole [T] / Legend [L] / ClearPin [X]
    //     on the paper: MapTitle (D12 vertical title slip with the D11 frame) and 북
    // The page content is rebuilt on every open (text scale, discoveries), like the other #304 pages.
    public sealed partial class WorldMapPresenter
    {
        sealed class PlaceEntry304
        {
            public string Id, Name, KindName, Description;
            public MapMarkerKind304 Kind;
            public WorldMapMarkerKind SpecKind;
            public WorldMapMarkerSpec Spec;
            public Vector2 World;
            public FocusRow304 Row;
        }

        RectTransform veilPage304, page304, handsRoot304;
        CanvasGroup veilGroup304;
        RawImage veil304;
        InkRevealEffect veilReveal304;
        bool legendFolded304, suppressPlaceFocus304, lastInterior304;
        RectTransform titleSlip304, clickHint304, northLabel304, northLine304;
        Image titlePaper304, titleFrame304;
        TMP_Text titleText304, realmText304, hereLabel304, cardName304, cardMeta304, cardBody304;
        Image cardRule304;
        FocusRow304 currentRow304, wholeRow304, legendRow304, clearPinRow304;
        readonly List<PlaceEntry304> places304 = new List<PlaceEntry304>();
        InputAction keyCurrent304, keyWhole304, keyLegend304, keyClearPin304;

        /// <summary>#304 optional hint from the caller before SetExpanded(true): true = the map replaces another open menu page
        /// (tab change), so the veil is already there and is not wiped in again. Consumed by the next open. Without it the map
        /// looks at the menu layer: a page still being torn down there means a tab change.</summary>
        public bool? OpenedFromPage304 { get; set; }

        // ------------------------------------------------------------------ build (Initialize)
        void BuildVeil304()
        {
            veilPage304 = V.Page304(FullRoot, "MapVeil304");
            veilGroup304 = veilPage304.GetComponent<CanvasGroup>();
            veil304 = V.Veil(style, veilPage304, "지도");
            veilReveal304 = veil304.GetComponent<InkRevealEffect>();
            veilGroup304.alpha = 0f; veilGroup304.interactable = veilGroup304.blocksRaycasts = false;
        }

        void BuildPageRoot304()
        {
            page304 = V.Page304(FullRoot, "MapPage304");
            mapControls = page304.GetComponent<CanvasGroup>();
            mapControls.alpha = 0f; mapControls.interactable = mapControls.blocksRaycasts = false;
        }

        void ApplyVeilFold304()
        {
            if (veilGroup304 == null) return;
            // open: the veil is there at once (wiped in by BuildPage304); close: it lifts in the last 18 % of the fold.
            // Tab change away from 지도 (the root already shows another page with its own veil + rail): the sheet folds away
            // over that page, so this veil steps aside at once instead of hiding the new page for half a second.
            var root = PlaytestUiRoot.Instance;
            bool handedOver = !targetExpanded && root != null && !string.IsNullOrEmpty(root.Page) && root.Page != "지도";
            veilGroup304.alpha = targetExpanded ? 1f : handedOver ? 0f : Mathf.SmoothStep(0, 1, Mathf.Clamp01(fold / .18f));
            veilGroup304.interactable = false; veilGroup304.blocksRaycasts = targetExpanded;
        }

        // ------------------------------------------------------------------ build (every open)
        void BuildPage304()
        {
            var s = style;
            var canvas = FullRoot.GetComponentInParent<Canvas>();
            V.EnsureCanvasChannels(canvas);
            FocusMark304.Attach(s, canvas != null ? (RectTransform)canvas.rootCanvas.transform : FullRoot);
            V.Clear(page304);
            legend = null; places304.Clear();
            currentRow304 = wholeRow304 = legendRow304 = clearPinRow304 = null;
            lastInterior304 = data.ZoneAt(CurrentWorld()) != null;

            bool firstOpen = !(OpenedFromPage304 ?? MenuPageUnderneath304());
            OpenedFromPage304 = null;

            BuildTitleSlip304();
            BuildHere304();
            BuildLegend304();
            BuildPlaces304();
            BuildControls304();

            GameObject first = places304.Count > 0 ? places304[0].Row.Button.gameObject : currentRow304 != null ? currentRow304.Button.gameObject : null;
            suppressPlaceFocus304 = true;
            try { if (first != null) FocusMark304.Select(first); }
            finally { suppressPlaceFocus304 = false; }
            if (places304.Count > 0) ShowCard304(places304[0]);

            EnableInput304(true);
            if (firstOpen && veilReveal304 != null)
                UiTween304.RevealIn(veilReveal304, s, s.Motion.VeilMs, UiTween304.Token(veilReveal304)).Forget();
        }

        void EndPage304()
        {
            EnableInput304(false);
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && FullRoot != null && es.currentSelectedGameObject.transform.IsChildOf(FullRoot))
                es.SetSelectedGameObject(null);
        }

        /// <summary>PlaytestUiRoot clears its MenuLayer (V.Clear: deactivate now, destroy at the end of the frame) right before
        /// it opens 지도, so a child still there means another page was on screen (tab change: no veil wipe).</summary>
        bool MenuPageUnderneath304()
        {
            var canvasRoot = FullRoot != null && FullRoot.parent != null ? FullRoot.parent.parent : null;
            var menuLayer = canvasRoot != null ? canvasRoot.Find("MenuLayer") : null;
            return menuLayer != null && menuLayer.childCount > 0;
        }

        // D12: the title is a vertical slip on the paper's top-left (강토 지도 Serif 800 30 + realm 22), D11 frame; 북 kept.
        // #304 QA2: the words of the title are their own columns' runs with a TitleWordGap304 gap (UiText304.Vertical made
        // the space a whole 36 px line), and the D11 rim is the pause / title slips' stroke_line rim on the shared InkReveal
        // material (the slip_frame sprite at α.62 went through UI/Default: blended in linear light it printed ~α.35 grey).
        const string MapTitle304 = "강토 지도";
        const float TitleWordGap304 = 14f, SlipW304 = 92f, SlipPad304 = 18f;
        const float RimInset304 = 7f, RimOver304 = 2f, RimLineH304 = 6f, RimAlpha304 = .6f;   // = PlaytestUiRoot.Flow304SlipRim
        readonly List<TMP_Text> titleWords304 = new List<TMP_Text>();
        Image[] titleRim304;
        bool titleRimBrushes304;

        void BuildTitleSlip304()
        {
            var s = style;
            titleSlip304 = V.Rect("MapTitle", page304, 602, 156, SlipW304, 230);
            titlePaper304 = V.SpriteImage(titleSlip304, "Slip", s.Sprites.SheetSlip, Color.white, 0, 0, SlipW304, 230);
            if (titlePaper304.sprite == null) titlePaper304.color = s.Sheet;
            BuildSlipRim304();
            titleWords304.Clear();
            string[] words = MapTitle304.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
                titleWords304.Add(V.Label(s, titleSlip304, i == 0 ? "Title" : "Title" + (i + 1), UiText304.Vertical(words[i]),
                    UiType304.Title30, s.Ink, 14, SlipPad304, 36, 0, TextAlignmentOptions.Top));
            titleText304 = titleWords304.Count > 0 ? titleWords304[0] : null;
            realmText304 = V.Label(s, titleSlip304, "Realm", "", UiType304.Serif700_22, s.Ash, 54, SlipPad304, 26, 0, TextAlignmentOptions.Top);
            var north = V.Label(s, page304, "North", "북", UiType304.Serif900_22, s.Ink, 1270, 182, 40, 0, TextAlignmentOptions.Top);
            var under = s.TmpMaterial(north.font, TmpPreset304.Ink_UnderPaper); if (under != null) north.fontSharedMaterial = under;
            northLabel304 = north.rectTransform;
            northLine304 = V.Rect("NorthLine", page304, 1288.5f, 212, 3, 26);
            V.Image(northLine304, s.Ink);
            RefreshTitle304(displayedZoneLabel);
        }

        /// <summary>D11 제첨 테: stroke_line brushes (InkReveal, linear-light alpha compensated) 7 px inside, crossing 2 px at the
        /// corners, α.6, as on the pause and title slips. Without the line sprite: the slip_frame sprite, else four 1.5 px
        /// lines, both on the same InkReveal material.</summary>
        void BuildSlipRim304()
        {
            var s = style;
            titleRim304 = null; titleFrame304 = null; titleRimBrushes304 = false;
            if (V.StrokeSprite(s, StrokeClass304.Line) != null)
            {
                titleRimBrushes304 = true;
                titleRim304 = new[]
                {
                    V.Brush(s, titleSlip304, "FrameTop", StrokeClass304.Line, s.Ink, 0, 0, SlipW304, RimLineH304, RimAlpha304),
                    V.Brush(s, titleSlip304, "FrameBottom", StrokeClass304.Line, s.Ink, 0, 0, SlipW304, RimLineH304, RimAlpha304, 0f, true),
                    V.Brush(s, titleSlip304, "FrameLeft", StrokeClass304.Line, s.Ink, 0, 0, 230, RimLineH304, RimAlpha304, 90f),
                    V.Brush(s, titleSlip304, "FrameRight", StrokeClass304.Line, s.Ink, 0, 0, 230, RimLineH304, RimAlpha304, -90f),
                };
                return;
            }
            if (mapStyle.SlipFrame != null)
            {
                titleFrame304 = V.SpriteImage(titleSlip304, "Frame", mapStyle.SlipFrame, UiStyle304SO.A(s.Ink, .62f), 0, 0, SlipW304, 230);
                InkRevealEffect.On(titleFrame304, s, InkRevealMode.Wipe, 1f);
                return;
            }
            var ink = UiStyle304SO.A(s.Ink, RimAlpha304);
            titleRim304 = new[]
            {
                V.Image(V.Rect("FrameTop", titleSlip304, 0, 0, 1, 1.5f), ink), V.Image(V.Rect("FrameBottom", titleSlip304, 0, 0, 1, 1.5f), ink),
                V.Image(V.Rect("FrameLeft", titleSlip304, 0, 0, 1.5f, 1), ink), V.Image(V.Rect("FrameRight", titleSlip304, 0, 0, 1.5f, 1), ink),
            };
            foreach (var line in titleRim304) InkRevealEffect.On(line, s, InkRevealMode.Wipe, 1f);
        }

        void LayoutSlipRim304(float w, float h)
        {
            if (titleFrame304 != null) { SizeSliced(titleFrame304, w, h); return; }
            if (titleRim304 == null || titleRim304.Length < 4) return;
            float x0 = RimInset304, x1 = w - RimInset304, y0 = RimInset304, y1 = h - RimInset304;
            float across = x1 - x0 + 2f * RimOver304, down = y1 - y0 + 2f * RimOver304;
            if (titleRimBrushes304)
            {
                // V.Brush rects: centre pivot, length along the stroke (the side strokes are turned 90°)
                PlaceStroke304(titleRim304[0], (x0 + x1) * .5f, y0, across);
                PlaceStroke304(titleRim304[1], (x0 + x1) * .5f, y1, across);
                PlaceStroke304(titleRim304[2], x0, (y0 + y1) * .5f, down);
                PlaceStroke304(titleRim304[3], x1, (y0 + y1) * .5f, down);
                return;
            }
            // four plain 1.5 px lines (top-left pivot)
            const float t = 1.5f;
            SetRect304(titleRim304[0], x0 - RimOver304, y0 - t * .5f, across, t);
            SetRect304(titleRim304[1], x0 - RimOver304, y1 - t * .5f, across, t);
            SetRect304(titleRim304[2], x0 - t * .5f, y0 - RimOver304, t, down);
            SetRect304(titleRim304[3], x1 - t * .5f, y0 - RimOver304, t, down);
        }

        static void PlaceStroke304(Image stroke, float cx, float cy, float length)
        {
            var r = stroke.rectTransform;
            r.sizeDelta = new Vector2(length, RimLineH304);
            r.anchoredPosition = new Vector2(cx, -cy);
        }

        static void SetRect304(Image image, float x, float y, float w, float h)
        {
            var r = image.rectTransform; r.sizeDelta = new Vector2(w, h); V.Place(r, x, y);
        }

        void RefreshTitle304(string zoneLabel)
        {
            if (titleSlip304 == null || realmText304 == null) return;
            string realm = string.IsNullOrEmpty(zoneLabel) || zoneLabel == "강토" ? "" : zoneLabel;
            realmText304.text = UiText304.Vertical(realm);
            // the title words stack down the left column with a TitleWordGap304 gap between them
            float y = SlipPad304, titleBottom = SlipPad304;
            for (int i = 0; i < titleWords304.Count; i++)
            {
                var word = titleWords304[i]; if (word == null) continue;
                if (i > 0) y += TitleWordGap304;
                Vector2 wp = word.GetPreferredValues(word.text);
                word.rectTransform.sizeDelta = new Vector2(36, Mathf.Ceil(wp.y));
                V.Place(word.rectTransform, 14, y);
                y += Mathf.Ceil(wp.y); titleBottom = y;
            }
            float titleH = titleBottom - SlipPad304;
            Vector2 rp = realm.Length > 0 ? realmText304.GetPreferredValues(realmText304.text) : Vector2.zero;
            realmText304.rectTransform.sizeDelta = new Vector2(26, Mathf.Ceil(rp.y));
            float body = Mathf.Max(titleH, rp.y);
            float h = Mathf.Ceil(SlipPad304 + body + SlipPad304);
            titleSlip304.sizeDelta = new Vector2(SlipW304, h);
            // the realm column hangs to the bottom of the title column (the pause slip's 권역 -> 지명 order)
            V.Place(realmText304.rectTransform, 54, SlipPad304 + Mathf.Max(0f, titleH - rp.y));
            SizeSliced(titlePaper304, SlipW304, h);
            LayoutSlipRim304(SlipW304, h);
        }

        static void SizeSliced(Image image, float w, float h)
        {
            if (image == null) return;
            var r = image.rectTransform; r.sizeDelta = new Vector2(w, h);
        }

        // ------------------------------------------------------------------ left column
        void BuildHere304()
        {
            var s = style;
            V.Label(s, page304, "HereCaption", "지금 있는 곳", UiType304.Meta20, s.Mist, 64, 162);
            hereLabel304 = V.Label(s, page304, "Here", HereText304(), UiType304.Title30, s.Paper, 64, 190);
            if (hereLabel304.rectTransform.sizeDelta.x > 470f)
            {
                hereLabel304.overflowMode = TextOverflowModes.Ellipsis;
                hereLabel304.rectTransform.sizeDelta = new Vector2(470f, hereLabel304.rectTransform.sizeDelta.y);
            }
            V.Brush(s, page304, "HereRule", StrokeClass304.Dry, s.Paper, 58, 248, 460, 26, .35f);
        }

        string HereText304()
        {
            Vector3 here = CurrentWorld();
            if (data.Locations != null)
            {
                var entry = data.Locations.Resolve(here, null);
                if (entry != null)
                {
                    // #304 QA2 (MERGE C4): "청림 · 청림 벌목마을" -> "청림 · 벌목마을", the pause slip's rule (Menu304LocalPlace)
                    data.Locations.ArrivalNames(entry, out string realm, out string local);
                    local = LocalPlace304(realm, local);
                    if (!string.IsNullOrEmpty(realm) && !string.IsNullOrEmpty(local)) return realm + " · " + local;
                    return data.Locations.DisplayName(entry);
                }
            }
            var zone = data.ZoneAt(here);
            return zone != null && !string.IsNullOrEmpty(zone.Label) ? zone.Label : RegionLabel(new Vector2(here.x, here.z));
        }

        /// <summary>The place without its realm prefix when it carries one ("청림 벌목마을" in 청림 -> "벌목마을"; "청림사" is its
        /// own name). Same rule as the pause slip (PlaytestUiRoot.Menu304LocalPlace), so both screens say the same place.</summary>
        static string LocalPlace304(string realm, string place)
        {
            place = (place ?? "").Trim();
            if (string.IsNullOrEmpty(realm) || place.Length <= realm.Length + 1 || !place.StartsWith(realm, StringComparison.Ordinal)) return place;
            char next = place[realm.Length];
            if (next != ' ' && next != '·') return place;
            string rest = place.Substring(realm.Length).TrimStart(' ', '·');
            return rest.Length > 0 ? rest : place;
        }

        void BuildLegend304()
        {
            var s = style;
            var root = V.Rect("LegendPanel", page304, 0, 0, UiPageFit304.Width, UiPageFit304.Height);
            legend = root.gameObject;
            // #308: marker rows on lacquer plates + the terrain-and-roads cells (WorldMapPresenter.Map308.cs); false without a bundle
            if (BuildLegend308(root)) { legend.SetActive(!legendFolded304); return; }
            V.Label(s, root, "LegendCaption", "범례", UiType304.Meta20, s.Mist, 64, 292);
            var rows = mapStyle.Legend != null && mapStyle.Legend.Count > 0 ? mapStyle.Legend : MapStyle304SO.DefaultLegend();
            for (int i = 0; i < rows.Count && i < 7; i++)
            {
                var row = rows[i]; if (row == null) continue;
                float y = 326 + 90 * i;
                var item = V.Rect("Legend_" + i, root, 0, 0, 1, 1);
                LegendSymbol304(item, row.Symbol, 64, y);
                V.Label(s, item, "Name", row.Name, UiType304.Label24, s.Paper, 126, y + 2);
                V.Label(s, item, "Meaning", row.Meaning, UiType304.Meta20, s.Mist, 126, y + 38, 410, 0, TextAlignmentOptions.TopLeft, true);
            }
            legend.SetActive(!legendFolded304);
        }

        void LegendSymbol304(RectTransform parent, MapLegendSymbol304 symbol, float x, float y)
        {
            var s = style;
            switch (symbol)
            {
                case MapLegendSymbol304.Player:
                    if (s.Sprites.Arrow != null) V.SpriteImage(parent, "Symbol", s.Sprites.Arrow, s.Cinnabar, x + 4, y + 2, 36, 36, -38f).preserveAspect = true;
                    break;
                case MapLegendSymbol304.Rest:
                    V.SpriteImage(parent, "Symbol", PictureFor(WorldMapMarkerKind.Rest), s.Paper, x + 5, y + 3, 34, 34).preserveAspect = true;
                    break;
                case MapLegendSymbol304.Place:
                    V.SpriteImage(parent, "SymbolCave", PictureFor(WorldMapMarkerKind.Place), s.Paper, x - 4, y + 7, 26, 26).preserveAspect = true;
                    V.SpriteImage(parent, "SymbolMountain", PictureFor(WorldMapMarkerKind.Mountain), s.Paper, x + 24, y + 8, 24, 24).preserveAspect = true;
                    break;
                case MapLegendSymbol304.Coin:
                    V.SpriteImage(parent, "Symbol", Pick(mapStyle.Coin, s.Sprites.Disc), s.Paper, x + 10, y + 8, 24, 24).preserveAspect = true;
                    break;
                case MapLegendSymbol304.Pin:
                    V.Brush(s, parent, "SymbolA", StrokeClass304.Short, s.Paper, x + 2, y + 15, 40, 10, 1f, 45f);
                    V.Brush(s, parent, "SymbolB", StrokeClass304.Short, s.Paper, x + 2, y + 15, 40, 10, 1f, -45f);
                    break;
                case MapLegendSymbol304.Objective:
                {
                    bool open = mapStyle.Objective == MapObjectiveMark304.OpenBrush && s.Sprites.Enso != null;
                    V.SpriteImage(parent, "Symbol", open ? s.Sprites.Enso : Pick(s.Sprites.RingDashed, s.Sprites.Disc), s.Cinnabar, x + 4, y + 2, 36, 36);
                    break;
                }
                case MapLegendSymbol304.Unwalked:
                {
                    // a real crop of the illustration with the unwalked part on its right (map.png)
                    var crop = V.Rect("Symbol", parent, x, y + 3, 44, 34);
                    var swatch = data.HasIllustration ? LegendSwatchMaterial304() : null;
                    if (swatch != null)
                    {
                        // #304 QA: drawn by the print's own shader (sheet, illustration, fog), so the row shows what 걷지 않은 땅
                        // really looks like on the paper: walked on the left 18 px, fogged on the right 26 px
                        var raw = V.Raw(crop, paperTexture304 != null ? paperTexture304 : Texture2D.whiteTexture, Color.white);
                        raw.uvRect = LegendPaperUv304; raw.material = swatch;
                    }
                    else
                    {
                        if (data.DisplayMap != null) V.Raw(crop, data.DisplayMap, Color.white).uvRect = LegendCropUv304;
                        else V.Image(crop, s.Ash);
                        V.Image(V.Rect("Veil", crop, 18, 0, 26, 34), UiStyle304SO.A(s.Sheet, mapStyle.UnknownVeil));
                    }
                    break;
                }
            }
        }

        // map.png crops Terrain.png at texel (470, 610) 44x34: kept as fractions of whatever illustration the map carries
        static readonly Rect LegendCropUv304 = new Rect(470f / 1024f, 1f - (610f + 34f) / 1536f, 44f / 1024f, 34f / 1536f);
        // a clean patch of the sheet (away from the fold crease at .5 and the torn edge) under the swatch
        static readonly Rect LegendPaperUv304 = new Rect(.25f, .62f, 44f / PaperW, 34f / PaperH);
        Material legendSwatch304;
        Texture2D legendFog304, legendClear304;

        /// <summary>#304 QA: one material instance of the print shader for the 걷지 않은 땅 legend swatch (cached; the legend is
        /// rebuilt on every open). Its own one-row fog puts the walked / unwalked split 18 px into the 44 px crop.</summary>
        Material LegendSwatchMaterial304()
        {
            if (legendSwatch304 != null) return legendSwatch304;
            if (paperMaterial == null || data.DisplayMap == null) return null;
            var m = new Material(paperMaterial.shader) { name = "MapLegendSwatch304", hideFlags = HideFlags.DontSave };
            m.SetTexture("_MapTex", data.DisplayMap);
            m.SetFloat("_KnownBase", 1f);
            m.SetFloat("_PaintedRelief", data.PaintedRelief ? 1f : 0f);
            m.SetFloat("_UnknownVeil", mapStyle.UnknownVeil);
            ConfigureFog304(m);
            m.SetFloat("_FogSoft", 0f);   // a straight split, as in map.png
            var crop = LegendCropUv304;
            m.SetVector("_WorldUv", new Vector4(crop.x, crop.y, crop.width, crop.height));
            m.SetVector("_MapWindow", new Vector4(LegendPaperUv304.x, LegendPaperUv304.y, LegendPaperUv304.width, LegendPaperUv304.height));
            m.SetVector("_MapTileUv", new Vector4(0f, 0f, 1f, 1f));
            m.SetFloat("_HasDetail", 0f); m.SetFloat("_HasCave", 0f); m.SetFloat("_Interior", 0f);
            // the shader reads the fog in world uv: 1024 columns put the split within a pixel of x18 on the 44 px swatch.
            // #308 mapfix2: the crisp edge (MapFog308_Edge) keeps the walked land 1.5 screen px inside an unwalked cell, so
            // one-pixel cells leave no walked side at all. With a bundle the swatch's cells are 8 px (128 columns; the
            // split (470 + 18) / 1024 is exactly column 61 of 128), the pre-#308 soft edge keeps its 1 px cells.
            int columns = n308 != null ? 128 : 1024;
            legendFog304 = new Texture2D(columns, 1, TextureFormat.RGBA32, false, true)
            { name = "MapLegendFog304", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            int split = Mathf.RoundToInt((crop.x + crop.width * 18f / 44f) * columns);
            Color32 unknown = new Color(style.Sheet.r, style.Sheet.g, style.Sheet.b, 1f);   // RefreshFog's rule: alpha 1 = unknown
            var pixels = new Color32[columns];
            for (int i = 0; i < columns; i++) pixels[i] = i < split ? new Color32(0, 0, 0, 0) : unknown;
            legendFog304.SetPixels32(pixels); legendFog304.Apply(false, true);
            m.SetTexture("_FogTex", legendFog304);
            legendClear304 = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "MapLegendClear304", hideFlags = HideFlags.DontSave };
            legendClear304.SetPixels32(new[] { new Color32(0, 0, 0, 0) }); legendClear304.Apply(false, true);
            m.SetTexture("_InkTex", legendClear304); m.SetTexture("_MacroInkTex", legendClear304);
            // #308: the swatch shows the new walked / unwalked values at its own scale (44 px across the crop)
            ApplyPaper308(m, false);
            PaperBand308(m, crop.width * Mathf.Max(1f, data.BoundsMax.x - data.BoundsMin.x) / 44f, ScreenPerReferencePx308());
            legendSwatch304 = m;
            return m;
        }

        void DisposeLegendSwatch304()
        {
            if (legendSwatch304 != null) Destroy(legendSwatch304);
            if (legendFog304 != null) Destroy(legendFog304);
            if (legendClear304 != null) Destroy(legendClear304);
            legendSwatch304 = null; legendFog304 = legendClear304 = null;
        }

        // ------------------------------------------------------------------ right column
        void CollectPlaces304()
        {
            places304.Clear();
            bool ready = session.Progress != null && session.Progress.ui != null;
            var ledger = ready ? session.Progress.ledger : null;
            Vector2 checkpoint = ledger != null ? WorldMapGeometry.XZ(ledger.checkpointPosition) : default;
            MarkerView restOfCheckpoint = ledger != null ? CheckpointRest(checkpoint) : null;
            if (restOfCheckpoint != null && !MarkerKnown(restOfCheckpoint)) restOfCheckpoint = null;
            var rest = mapStyle.Kind(MapMarkerKind304.Rest); var place = mapStyle.Kind(MapMarkerKind304.Place);
            foreach (var m in markers)
                if (KindOf(m.Spec.Kind) == MapMarkerKind304.Rest && MarkerKnown(m))
                    places304.Add(new PlaceEntry304 { Id = m.Spec.Id, Name = m.Name, Kind = MapMarkerKind304.Rest, SpecKind = m.Spec.Kind, Spec = m.Spec, World = m.Spec.WorldXZ, KindName = rest.Name, Description = rest.Description });
            if (ledger != null && restOfCheckpoint == null && fullCheckpointLabel != null)
                places304.Add(new PlaceEntry304 { Id = "Checkpoint", Name = fullCheckpointLabel.text, Kind = MapMarkerKind304.Rest, SpecKind = WorldMapMarkerKind.Checkpoint, World = checkpoint, KindName = rest.Name, Description = rest.Description });
            foreach (var m in markers)
                if (KindOf(m.Spec.Kind) != MapMarkerKind304.Rest && MarkerKnown(m))
                    places304.Add(new PlaceEntry304 { Id = m.Spec.Id, Name = m.Name, Kind = MapMarkerKind304.Place, SpecKind = m.Spec.Kind, Spec = m.Spec, World = m.Spec.WorldXZ, KindName = place.Name, Description = place.Description });
            var destination = session.KnownDestination;
            if (destination != null)
            {
                var objective = mapStyle.Kind(MapMarkerKind304.Objective);
                places304.Add(new PlaceEntry304 { Id = "Objective", Name = KnownLabel(destination), Kind = MapMarkerKind304.Objective, SpecKind = WorldMapMarkerKind.Place,
                    World = new Vector2(destination.Destination.x, destination.Destination.z), KindName = objective.Name,
                    Description = !string.IsNullOrWhiteSpace(destination.TravelHint) ? destination.TravelHint.Trim() : objective.Description });
            }
            if (ledger != null && ledger.dropCurrency > 0)
            {
                var coin = mapStyle.Kind(MapMarkerKind304.Coin);
                places304.Add(new PlaceEntry304 { Id = "Drop", Name = coin.Name, Kind = MapMarkerKind304.Coin, SpecKind = WorldMapMarkerKind.Drop,
                    World = WorldMapGeometry.XZ(ledger.dropPosition), KindName = coin.Name,
                    Description = "조선통보 " + ledger.dropCurrency.ToString("N0") + ". " + coin.Description });
            }
        }

        void BuildPlaces304()
        {
            var s = style;
            CollectPlaces304();
            V.Label(s, page304, "PlacesCaption", "발견한 장소", UiType304.Meta20, s.Mist, 1404, 162);
            int n = places304.Count;
            int shown = Mathf.Clamp(n, 1, Mathf.Max(1, mapStyle.ListVisibleRows));
            const float pitch = 66f, pad = 8f;
            var viewport = V.Rect("PlacesList", page304, 1382, 184, UiPageFit304.Width - 1382, pitch * shown + 2 * pad);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = V.Rect("Content", viewport, 0, 0, UiPageFit304.Width - 1382, pitch * Mathf.Max(1, n) + 2 * pad);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = n > shown;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24f; scroll.inertia = false;
            if (n == 0) V.Label(s, content, "Empty", "아직 이름을 안 곳이 없다", UiType304.Meta20, s.Mist, 22, pad + 20);
            for (int i = 0; i < n; i++)
            {
                var entry = places304[i]; int index = i;
                var row = V.FocusRow(s, content, "Place_" + entry.Id, entry.Name, 0, pad + i * pitch, 470, pitch, () => PlaceSubmitted304(index), new FocusRowSpec304
                {
                    Role = UiType304.Label26, LabelX = 100, UnderlayH = 80, UnderlayX = 0, ContentRight = 462,
                    DabSize = new Vector2(28, 22), DabGap = 58, DabDy = 0f,
                    Meta = entry.KindName, MetaX = 358, SoundTheme = theme,
                });
                if (row.Meta != null)   // kind meta right-aligned on x1820 (map.png)
                {
                    var mr = row.Meta.rectTransform;
                    V.Place(mr, 438f - mr.sizeDelta.x, -mr.anchoredPosition.y);
                }
                Image icon; Graphic glyph308;
                if (entry.Kind == MapMarkerKind304.Objective)
                {
                    bool open = mapStyle.Objective == MapObjectiveMark304.OpenBrush && s.Sprites.Enso != null;
                    icon = V.SpriteImage(row.Rect, "Icon", open ? s.Sprites.Enso : Pick(s.Sprites.RingDashed, s.Sprites.Disc), s.CinnabarLift, 54, 18, 30, 30);
                    row.Visual.AddTint(icon, s.CinnabarLift, s.Cinnabar);
                }
                else if ((glyph308 = ListGlyph308(row.Rect, entry)) != null)
                {
                    // #308: the same atlas glyph as on the paper (no rim: it stands on the row's underlay)
                    icon = null; row.Visual.AddTint(glyph308, s.Paper, s.Ink);
                }
                else
                {
                    icon = V.SpriteImage(row.Rect, "Icon", PictogramFor(entry.SpecKind), s.Paper, 54, 18, 30, 30);
                    row.Visual.AddTint(icon, s.Paper, s.Ink);
                }
                if (icon != null) icon.preserveAspect = true;
                var hook = row.Rect.gameObject.AddComponent<MapPlaceRow304>(); hook.Owner = this; hook.Index = index;
                entry.Row = row;
            }

            // detail card under the list (map.png: rule 470, name 512, meta 560, body 598 with four rows)
            float sepY = 184 + pitch * shown + 2 * pad + 6f;   // 470 with four rows
            // #308: the list / card divider is a nacre inlay line (kit slot Inlay.Line) when the map has a notation bundle
            cardRule304 = n308 != null ? InlayLine308(page304, "CardRule", 1404, sepY + 12, 446)
                : V.Brush(s, page304, "CardRule", StrokeClass304.Dry, s.Paper, 1398, sepY, 458, 26, .3f);
            cardName304 = V.Label(s, page304, "CardName", "", UiType304.Title34, s.Paper, 1404, sepY + 42);
            cardMeta304 = V.Label(s, page304, "CardMeta", "", UiType304.Meta20, s.Mist, 1406, sepY + 90);
            cardBody304 = V.Label(s, page304, "CardBody", "", UiType304.Body22, s.Paper, 1406, sepY + 128, 440, 0, TextAlignmentOptions.TopLeft, true);
            bool anyPlace = n > 0;
            cardRule304.gameObject.SetActive(anyPlace); cardName304.gameObject.SetActive(anyPlace); cardMeta304.gameObject.SetActive(anyPlace); cardBody304.gameObject.SetActive(anyPlace);

            // 손으로: the pointer verbs next to the list (DESIGN §0.3: hints beside the action)
            handsRoot304 = V.Rect("Hands", page304, 1404, 744, 420, 170);
            V.Label(s, handsRoot304, "Caption", "손으로", UiType304.Meta20, s.Mist, 0, 0);
            V.Hint(s, handsRoot304, "DragHint", new[] { "끌기" }, "옮기기", s.Paper, 0, 30);
            V.Hint(s, handsRoot304, "WheelHint", new[] { "휠" }, "확대 · 축소", s.Paper, 0, 82);
            clickHint304 = V.Hint(s, handsRoot304, "ClickHint", new[] { "좌클릭" }, "표식 찍기", s.Paper, 0, 134);
            clickHint304.gameObject.SetActive(!lastInterior304);   // pins are outdoor only (SetPin)
            PlaceHands304(anyPlace ? sepY + 128 : 184 + pitch + 2 * pad);
        }

        void PlaceHands304(float bodyBottom)
        {
            if (handsRoot304 == null) return;
            V.Place(handsRoot304, 1404, Mathf.Max(744f, bodyBottom + 36f));
        }

        void ShowCard304(PlaceEntry304 entry)
        {
            if (entry == null || cardName304 == null) return;
            SetLabelText(cardName304, entry.Name);
            string meta = RealmMeta304(entry.World);
            SetLabelText(cardMeta304, string.IsNullOrEmpty(meta) ? entry.KindName : meta + " · " + entry.KindName);
            cardBody304.text = entry.Description ?? "";
            Vector2 pref = cardBody304.GetPreferredValues(cardBody304.text, 440f, 0f);
            cardBody304.rectTransform.sizeDelta = new Vector2(440f, Mathf.Ceil(pref.y));
            PlaceHands304(-cardBody304.rectTransform.anchoredPosition.y + pref.y);
        }

        /// <summary>"청림 북동쪽" (D49 방면: the direction of the place inside its realm; "청림" near the realm centre).</summary>
        string RealmMeta304(Vector2 world)
        {
            if (data.Locations == null) return RegionLabel(world);
            var realm = data.Locations.RealmAt(new Vector3(world.x, 0f, world.y));
            if (realm == null) return "";
            Vector2 centre = Vector2.zero, min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            int count = 0;
            if (realm.Polygon != null) foreach (var p in realm.Polygon) { centre += p; min = Vector2.Min(min, p); max = Vector2.Max(max, p); count++; }
            if (count == 0) return realm.Name;
            centre /= count;
            Vector2 d = world - centre; float size = Mathf.Max(1f, Mathf.Max(max.x - min.x, max.y - min.y));
            if (d.magnitude < size * .16f) return realm.Name + " 가운데";
            float angle = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;   // 0 = north (+z), 90 = east (+x)
            if (angle < 0f) angle += 360f;
            string[] names = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };
            return realm.Name + " " + names[Mathf.RoundToInt(angle / 45f) % 8] + "쪽";
        }

        internal void PlaceSelected304(int index, bool byHover)
        {
            if (index < 0 || index >= places304.Count) return;
            var entry = places304[index];
            ShowCard304(entry);
            if (!byHover && !suppressPlaceFocus304) FocusPlace304(entry);
        }

        void PlaceSubmitted304(int index)
        {
            if (index < 0 || index >= places304.Count) return;
            ShowCard304(places304[index]);
            FocusPlace304(places304[index]);
        }

        void FocusPlace304(PlaceEntry304 entry)
        {
            if (entry.Spec != null) FocusOn(entry.Spec);
            else FocusOnWorld(entry.World);
        }

        // ------------------------------------------------------------------ controls row (y972, centred under the paper)
        void BuildControls304()
        {
            var controls = V.Rect("PaperMapControls", page304, 0, 972, UiPageFit304.Width, 48);
            currentRow304 = ControlRow304(controls, "Current", "R", "현재 위치", FocusCurrent);
            wholeRow304 = ControlRow304(controls, "Whole", "T", "전체 보기", ShowWholeWorld);
            legendRow304 = ControlRow304(controls, "Legend", "L", legendFolded304 ? "범례 펴기" : "범례 접기", ToggleLegend);
            clearPinRow304 = ControlRow304(controls, "ClearPin", "X", "표식 지우기", ClearPin);
            LayoutControls304();
        }

        FocusRow304 ControlRow304(RectTransform parent, string name, string key, string label, Action action)
        {
            var s = style;
            float kw = V.KeycapWidth(s, null, key, true);
            var row = V.FocusRow(s, parent, name, label, 0, 0, 10, 48, () => action(), new FocusRowSpec304
            {
                Role = UiType304.Label22, LabelX = kw + 10f, Key = key, KeyMode = FocusKeyMode304.Filled, KeySmall = true, KeyX = 0f,
                UnderlayH = 76, UnderlayX = -52f, DabSize = new Vector2(28, 22), DabGap = kw + 22f, SoundTheme = theme,
            });
            return row;
        }

        void LayoutControls304()
        {
            const float gap = 34f;
            var rows = new[] { currentRow304, wholeRow304, legendRow304, clearPinRow304 };
            float total = 0f; int count = 0;
            foreach (var r in rows)
            {
                if (r == null) continue;
                float w = r.Label.rectTransform.anchoredPosition.x + r.Label.rectTransform.sizeDelta.x;
                r.Rect.sizeDelta = new Vector2(w, r.Rect.sizeDelta.y);
                total += w; count++;
            }
            total += gap * Mathf.Max(0, count - 1);
            float x = UiPageFit304.Width * .5f - total * .5f;
            foreach (var r in rows)
            {
                if (r == null) continue;
                V.Place(r.Rect, x, 0f);
                x += r.Rect.sizeDelta.x + gap;
            }
        }

        void RefreshLegendButton304()
        {
            if (legendRow304 == null || legendRow304.Label == null) return;
            legendRow304.Label.text = legendFolded304 ? "범례 펴기" : "범례 접기";
        }

        // ------------------------------------------------------------------ keys (DESIGN §9-4 proposal: R / T / L / X; Q / E and
        // LB / RB belong to PlaytestUiRoot's rail). Code-built actions, enabled only while the map page is open.
        void BuildInput304()
        {
            keyCurrent304 = new InputAction("Map304Current", InputActionType.Button, "<Keyboard>/r");
            keyWhole304 = new InputAction("Map304Whole", InputActionType.Button, "<Keyboard>/t");
            keyLegend304 = new InputAction("Map304Legend", InputActionType.Button, "<Keyboard>/l");
            keyClearPin304 = new InputAction("Map304ClearPin", InputActionType.Button, "<Keyboard>/x");
        }

        IEnumerable<InputAction> Input304()
        {
            yield return keyCurrent304; yield return keyWhole304; yield return keyLegend304; yield return keyClearPin304;
        }

        void EnableInput304(bool on)
        {
            foreach (var a in Input304())
            {
                if (a == null) continue;
                if (on && !a.enabled) a.Enable(); else if (!on && a.enabled) a.Disable();
            }
        }

        void TickInput304()
        {
            if (keyCurrent304 == null || !keyCurrent304.enabled || !targetExpanded || fold < .999f) return;
            if (keyCurrent304.WasPressedThisFrame()) FocusCurrent();
            if (keyWhole304.WasPressedThisFrame()) ShowWholeWorld();
            if (keyLegend304.WasPressedThisFrame()) ToggleLegend();
            if (keyClearPin304.WasPressedThisFrame() && session.Progress?.ui?.pin != null && session.Progress.ui.pin.active) ClearPin();
        }

        void DisposeInput304()
        {
            foreach (var a in Input304()) a?.Dispose();
            keyCurrent304 = keyWhole304 = keyLegend304 = keyClearPin304 = null;
        }
    }
}
