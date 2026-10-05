using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Data.World;
using TMPro;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>
    /// 강토 지도 (#304 map.png). The twice-folded hanji (ExpandedWorldMap/TwiceFoldedHanji/PrintedMapWindow/MapInput,
    /// WorldMapPaperReview) now carries the sheet_map paper at 800x820 (print window 740x760) on a full veil, a legend
    /// column on the left, the discovered-places list + detail card on the right and the [R][T][L][X] row under the paper
    /// (WorldMapPresenter.Page304.cs). The rail ([M] 닫기, Q / E) is PlaytestUiRoot's MapRail304 above the map layer. Marks and labels are TMP / sprite based
    /// (WorldMapPresenter.Marks304.cs). #306: the minimap is back on the HUD (HudMinimap304 under HUD_Canvas, reading
    /// IMapMiniSource304, WorldMapPresenter.Mini306.cs); MiniRoot is its root once attached, an empty inactive stub before. The
    /// HUD bearing line reads IMapMarkerSource304.
    /// #308 (SPEC-MAP-OVERHAUL-308): with a notation bundle (MapStyle304SO.Notation308, WorldMapPresenter.Map308.cs) the sheet
    /// draws the baked terrain picture (_MAP308), brush strips, atlas icons, plated place names, the two-part legend and the
    /// lacquer board; without one every line below runs as before.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class WorldMapPresenter : MonoBehaviour
    {
        public const float OpenDuration = 1.1f;
        public const float CloseDuration = .65f;

        // map.png, 1920x1080 page px (DESIGN §7.6)
        const float PaperX = 560f, PaperY = 140f, PaperW = 800f, PaperH = 820f, PrintInset = 30f;

        /// <summary>#306: the HUD minimap's root ("PersistentMinimap" under HUD_Canvas) once HudMinimap304 attaches; until then
        /// the empty, inactive #304 stub of the same name. Stays an auto-property (editor tools reach its backing field by name).</summary>
        public RectTransform MiniRoot { get; private set; }
        public RectTransform FullRoot { get; private set; }
        public bool Visible { get; private set; } = true;
        public bool Expanded => targetExpanded;
        public bool Folding => Visible && !Mathf.Approximately(fold, targetExpanded ? 1f : 0f);
        public bool LegendVisible => legend != null && legend.activeSelf;
        public bool ReducedMotion { get; set; }
        public float FoldProgress => fold;
        public int PaperVertexCount => paperGraphic != null ? paperGraphic.GeometryVertexCount : 0;
        public int PaperTriangleCount => paperGraphic != null ? paperGraphic.GeometryTriangleCount : 0;
        public event Action<float> FoldRustle;
        public double LastTickMilliseconds { get; private set; }
        public double MeanTickMilliseconds { get; private set; }
        public long TickSamples { get; private set; }
        public event Action CloseRequested;

        WorldMacroPlaytestSession session;
        WorldMacroSheetSO sheet;
        WorldMapBakedDataSO data;
        WorldMapProjection projection;
        WorldMapDiscoveryGrid discovery;
        WorldMapUiDependencies dependencies;
        UiStyle304SO style;
        MapStyle304SO mapStyle;
        PlaytestUiThemeSO theme;
        WorldMapPaperGraphic paperGraphic;
        WorldMapPaperInk paperInk;
        Material paperMaterial;
        Texture paperTexture304;
        WorldMapPaperInk macroInk;
        WorldMapLineSpec[] majorLines, exploredLines, baseExploredLines, straightLines;
        Vector2[][] realmBorders;
        WorldActShortcut[] displayedShortcuts; int shortcutCount = -1;
        readonly List<TMP_Text> regionLabels = new List<TMP_Text>();
        readonly List<Vector2> regionCentres = new List<Vector2>();
        Vehicle.WorldMacroPalanquinSeat vehicleSeat;
        RectTransform paperSheet, fullMarkers;
        CanvasGroup mapControls, markerVisibility;
        Rect lastInkUv;
        WorldMapZoneSpec lastInkZone;
        bool inkReady;
        RectTransform foldMap, fullLabels;
        GameObject legend;
        Texture2D fogTexture;
        // #307 fog cache: the texels and the (outline) playability of every cell, so a reveal rewrites only the cells around it
        Color32[] fogPixels;
        bool[] fogPlayable;
        Rect fullUv = new Rect(0, 0, 1, 1);
        bool initialized, targetExpanded, progressLoaded, followCurrent, ownsRuntimeData, wholeWorldLayout;
        float fold;
        float transitionFromFold, transitionDuration;
        double transitionStartedAt;
        float nextDiscoveryProbe;
        float pageScale = 1f;
        Vector2 lastFullRootSize = new Vector2(-1, -1);
        int lastTickFrame = -1;
        string displayedZoneLabel;
        static readonly ProfilerMarker TickMarker = new ProfilerMarker("Oheangbu.WorldMap.Tick");

        public void Initialize(RectTransform parent, WorldMacroPlaytestSession playtestSession, WorldMacroSheetSO macroSheet,
            WorldMapUiDependencies uiDependencies = null)
        {
            if (initialized) throw new InvalidOperationException("World map presenter is already initialized.");
            if (parent == null || playtestSession == null || macroSheet == null) throw new ArgumentNullException("World map initialization requires parent, session, and sheet.");
            session = playtestSession; sheet = macroSheet; dependencies = uiDependencies ?? new WorldMapUiDependencies();
            ReducedMotion = dependencies.ReducedMotion;
            theme = dependencies.Theme != null ? dependencies.Theme : PlaytestUiRoot.Instance != null ? PlaytestUiRoot.Instance.Theme : null;
            style = dependencies.Style != null ? dependencies.Style : V.Style(theme);
            mapStyle = dependencies.MapStyle != null ? dependencies.MapStyle : MapStyle304SO.Resolve();
            data = dependencies.BakedData;
            if (data == null || !data.IsUsable) data = Resources.Load<WorldMapBakedDataSO>("WorldMap/WorldMapBakedData");
            if (data == null || !data.IsUsable)
            {
                Texture2D fallback = WorldMapRasterizer.Render(sheet, session.Content, 512, 768, false);
                data = WorldMapRuntimeDataFactory.Create(sheet, session.Content, null, null, fallback);
                data.hideFlags = HideFlags.DontSave;
                ownsRuntimeData = true;
            }
            projection = new WorldMapProjection(data.BoundsMin, data.BoundsMax);
            majorLines = data.Lines.Where(l => l != null && l.Kind == WorldMapLineKind.River).ToArray();
            exploredLines = data.Lines.Where(l => l != null && l.Kind != WorldMapLineKind.River).ToArray();
            baseExploredLines = exploredLines;
            straightLines = Straighten(exploredLines);
            realmBorders = data.Locations != null
                ? data.Locations.Entries.Where(e => e != null && e.Priority == 0 && e.Polygon != null && e.Polygon.Length >= 3).Select(e => e.Polygon).ToArray()
                : (sheet.Regions ?? Array.Empty<WorldMacroSheetSO.RegionSpec>()).Where(r => r != null && r.Polygon != null && r.Polygon.Length >= 3).Select(r => r.Polygon).ToArray();
            displayedShortcuts = UnityEngine.Object.FindObjectsByType<WorldActShortcut>(FindObjectsSortMode.None).Where(x => x.gameObject.scene == session.gameObject.scene).ToArray();
            discovery = new WorldMapDiscoveryGrid(data.BoundsMin, data.BoundsMax, data.Outline);
            InitNotation308();   // #308: the bundle for this map, or none (the pre-#308 path)
            BuildFog(); BuildMiniStub(parent); BuildFull(parent); BuildMarks304(); BuildMapHover(); BuildInput304();
            ApplyFold(); ApplyUvAndMarkers(CurrentWorld());
            initialized = true;
        }

        public void SetExpanded(bool expanded)
        {
            if (!initialized || targetExpanded == expanded) return;
            targetExpanded = expanded;
            HideMapHover();
            transitionFromFold = fold;
            transitionDuration = (expanded ? OpenDuration : CloseDuration) * Mathf.Abs((expanded ? 1f : 0f) - fold);
            transitionStartedAt = Time.realtimeSinceStartupAsDouble;
            if (!expanded) { followCurrent = false; EndPage304(); }
            if (expanded)
            {
                FullRoot.gameObject.SetActive(Visible);
                FocusCurrent();                     // DESIGN §7.6: the default view is the current region at 1.5x
                BuildPage304();
            }
            if (ReducedMotion) { fold = expanded ? 1f : 0f; transitionFromFold = fold; transitionDuration = 0f; ApplyFold(); }
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            if (!visible) HideMapHover();
            if (!initialized) return;
            FullRoot.gameObject.SetActive(visible && (targetExpanded || fold > 0f));
        }

        public void Tick()
        {
            if (!initialized || lastTickFrame == Time.frameCount) return;
            lastTickFrame = Time.frameCount;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            TickMarker.Begin();
            try
            {
                float target = targetExpanded ? 1f : 0f;
                float previousFold = fold;
                if (ReducedMotion) { fold = target; transitionFromFold = fold; transitionDuration = 0f; }
                else fold = transitionDuration <= 0f ? target : Mathf.Lerp(transitionFromFold, target,
                    Mathf.Clamp01((float)(Time.realtimeSinceStartupAsDouble - transitionStartedAt) / transitionDuration));
                if (!ReducedMotion && ((previousFold < .54f && fold >= .54f) || (previousFold > .54f && fold <= .54f)))
                    FoldRustle?.Invoke(targetExpanded ? .25f : .18f);
                bool open = targetExpanded || fold > 0f;
                if (open) RefreshFullLayout();
                ApplyFold();
                Vector3 current = CurrentWorld();
                LoadProgressIfReady();
                RevealInterior(current);
                if (progressLoaded && Time.unscaledTime >= nextDiscoveryProbe && WorldMapDiscoveryGrid.Contains(data.Outline, new Vector2(current.x, current.z)))
                {
                    nextDiscoveryProbe = Time.unscaledTime + .2f;
                    if ((data.ZoneAt(current) == null || !data.ZoneAt(current).ExploreWalkedPassages) && discovery.Reveal(new Vector2(current.x, current.z), WorldMapDiscoveryGrid.RevealRadius, RevealGate308(current)))
                    {
                        // stays at the reveal: the session's save snapshots (autosave, rest, interactions, death) copy Progress
                        // without a map hook, so a deferred export could save stale walked land (~2.6 KB string per reveal)
                        session.Progress.ui.discoveredCells = Convert.ToBase64String(discovery.Export());
                        RefreshFogAround(new Vector2(current.x, current.z), WorldMapDiscoveryGrid.RevealRadius);
                    }
                    foreach (WorldMapMarkerSpec marker in data.Markers)
                        // #308 map 3c: WalkReveals308 = the same cell test first; with a notation bundle a marker's reveal rule may ask more
                        if (marker != null && !marker.RequiresArrival && WalkReveals308(marker) && !session.Progress.ui.discoveredMarkers.Contains(marker.Id))
                            session.Progress.ui.discoveredMarkers.Add(marker.Id);
                }
                // Closed map: the sheet draws nothing; discovery above keeps running for the bearing line and the HUD minimap
                // (#306), which prints its own window through IMapMiniSource304.
                if (RefreshShortcutLines306()) miniRevision++;
                RefreshStates308();   // #308: stroke states (the HUD minimap reads their revision; no minimap print)
                if (!open) return;
                if (followCurrent) CenterFullOn(new Vector2(current.x, current.z));
                ApplyUvAndMarkers(current);
                TickInput304();
            }
            finally
            {
                TickMarker.End();
                LastTickMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                TickSamples++;
                MeanTickMilliseconds += (LastTickMilliseconds - MeanTickMilliseconds) / TickSamples;
            }
        }

        void LateUpdate() => Tick();

        public void ShowWholeWorld()
        {
            if (data.ZoneAt(CurrentWorld()) != null) { FocusCurrent(); return; }
            wholeWorldLayout = true; RefreshFullLayout(true);
            fullUv = new Rect(0, 0, 1, 1); followCurrent = false;
            if (initialized) ApplyUvAndMarkers(CurrentWorld());
        }

        public void FocusCurrent()
        {
            if (!initialized) return;
            wholeWorldLayout = false; RefreshFullLayout(true);
            Vector3 here = CurrentWorld();
            SetFocusWindow(data.ZoneAt(here) != null);
            followCurrent = true; CenterFullOn(new Vector2(here.x, here.z));
            ApplyUvAndMarkers(here);
        }

        /// <summary>#304: centres the unfolded map on a place (places list, gamepad), at the 현재 위치 zoom when the map shows
        /// the whole world, otherwise at the current zoom. Interiors keep their local plan (the card still updates).</summary>
        public void FocusOn(WorldMapMarkerSpec marker)
        {
            if (marker == null) return;
            FocusOnWorld(marker.WorldXZ);
        }

        /// <summary>#304: FocusOn for a IMapMarkerSource304 entry (HUD bearing line, D26 later).</summary>
        public void FocusOn(MapMarker304 marker) => FocusOnWorld(new Vector2(marker.World.x, marker.World.z));

        void FocusOnWorld(Vector2 worldXZ)
        {
            if (!initialized) return;
            Vector3 here = CurrentWorld();
            if (data.ZoneAt(here) != null) return;
            if (wholeWorldLayout || fullUv.width >= .999f) { wholeWorldLayout = false; RefreshFullLayout(true); SetFocusWindow(false); }
            followCurrent = false; CenterFullOn(worldXZ);
            ApplyUvAndMarkers(here);
        }

        void SetFocusWindow(bool interior)
        {
            float worldWidth = Mathf.Max(1f, data.BoundsMax.x - data.BoundsMin.x);
            float worldHeight = Mathf.Max(1f, data.BoundsMax.y - data.BoundsMin.y);
            float metres;
            // DESIGN §7.6 기본 보기: the illustration at 1.5 screen px per texel (map.png: 493 of 1024 Terrain px in 740 px).
            // The live compact map (painted relief, 1001 px over 4000 m) opens at ~1970 m instead of the pre-#304 1100 m.
            if (interior) metres = mapStyle.InteriorCurrentMetres;
            else if (data.HasIllustration && data.DisplayMap != null && data.DisplayMap.width > 0)
                metres = worldWidth * (foldMap.rect.width / Mathf.Max(.01f, pageScale) / Mathf.Max(.1f, mapStyle.CurrentViewTexelScale)) / data.DisplayMap.width;
            else if (data.PaintedRelief) metres = mapStyle.PaintedCurrentMetres;
            else metres = mapStyle.PlainCurrentMetres;
            float width = Mathf.Clamp(metres / worldWidth, .002f, 1f);
            float viewAspect = foldMap.rect.width / Mathf.Max(1f, foldMap.rect.height);
            fullUv.width = width;
            fullUv.height = Mathf.Clamp(width * worldWidth / (viewAspect * worldHeight), interior ? .005f : .03f, 1f);
        }

        public void SetPin(Vector2 worldXZ, string label)
        {
            if (!initialized || data.ZoneAt(CurrentWorld()) != null || !WorldMapDiscoveryGrid.Contains(data.Outline, worldXZ) || !discovery.IsDiscovered(worldXZ) || session.Progress == null) return;
            session.Progress.ui.pin.active = true; session.Progress.ui.pin.worldXZ = worldXZ;
            session.Progress.ui.pin.label = string.IsNullOrWhiteSpace(label) ? "표식" : label.Trim();
            bool saved = session.SaveNow(out _); PlaytestUiRoot.Instance?.PlayNamedSound(saved ? "ui_select" : "ui_error", .3f); ApplyUvAndMarkers(CurrentWorld());
        }

        public void ClearPin()
        {
            if (!initialized || session.Progress == null) return;
            session.Progress.ui.pin.active = false; session.Progress.ui.pin.label = "";
            bool saved = session.SaveNow(out _); PlaytestUiRoot.Instance?.PlayNamedSound(saved ? "ui_select" : "ui_error", .3f); ApplyUvAndMarkers(CurrentWorld());
        }

        internal void Pan(Vector2 screenDelta)
        {
            if (!targetExpanded || fold < .999f || foldMap == null) return;
            fullUv.x -= screenDelta.x / Mathf.Max(1, foldMap.rect.width) * fullUv.width;
            fullUv.y -= screenDelta.y / Mathf.Max(1, foldMap.rect.height) * fullUv.height;
            fullUv = WorldMapProjection.ClampUvRect(fullUv); followCurrent = false;
            ApplyUvAndMarkers(CurrentWorld());
        }

        internal void Zoom(float scroll, Vector2 screenPoint, Camera eventCamera)
        {
            if (!targetExpanded || fold < .999f || Mathf.Abs(scroll) < .01f) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(foldMap, screenPoint, eventCamera, out Vector2 local)) return;
            Rect rect = foldMap.rect;
            Vector2 focus = new Vector2(Mathf.InverseLerp(rect.xMin, rect.xMax, local.x), Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));
            float factor = scroll > 0 ? .82f : 1.22f;
            float worldWidth = data.BoundsMax.x - data.BoundsMin.x;
            float worldHeight = data.BoundsMax.y - data.BoundsMin.y;
            float viewAspect = foldMap.rect.width / Mathf.Max(1f, foldMap.rect.height);
            float heightPerWidth = worldWidth / (viewAspect * worldHeight);
            bool interior = data.ZoneAt(CurrentWorld()) != null;
            float minimumWidth = interior ? 80f / worldWidth : .03f;
            float newWidth = Mathf.Clamp(fullUv.width * factor, minimumWidth, Mathf.Min(1f, 1f / heightPerWidth));
            float newHeight = newWidth * heightPerWidth;
            Vector2 anchored = fullUv.min + Vector2.Scale(focus, fullUv.size);
            fullUv = new Rect(anchored.x - focus.x * newWidth, anchored.y - focus.y * newHeight, newWidth, newHeight);
            fullUv = WorldMapProjection.ClampUvRect(fullUv); followCurrent = false;
            ApplyUvAndMarkers(CurrentWorld());
        }

        internal void PlacePinFromScreen(Vector2 screenPoint, Camera eventCamera)
        {
            if (!targetExpanded || fold < .999f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(foldMap, screenPoint, eventCamera, out Vector2 local)) return;
            Rect r = foldMap.rect;
            Vector2 inView = new Vector2(Mathf.InverseLerp(r.xMin, r.xMax, local.x), Mathf.InverseLerp(r.yMin, r.yMax, local.y));
            Vector2 normalized = fullUv.min + Vector2.Scale(inView, fullUv.size);
            SetPin(projection.NormalizedToWorld(normalized), "표식");
        }

        void BuildMiniStub(RectTransform parent)
        {
            // #304 stub, kept until the #306 HUD minimap attaches its root (AttachMiniRoot): empty and inactive, for callers
            // that only check that MiniRoot exists.
            MiniRoot = miniStub = V.Rect("PersistentMinimap", parent, 0, 0, 1, 1);
            miniStub.gameObject.SetActive(false);
        }

        void BuildFull(RectTransform parent)
        {
            FullRoot = V.Stretch("ExpandedWorldMap", parent);
            V.EnsureCanvasChannels(FullRoot.GetComponentInParent<Canvas>());

            // 1. the page veil (brush lifts at 1960 = full page), faded with the fold on close
            BuildVeil304();
            BuildBoard308();   // #308: the lacquer board the sheet lies on (no-op without a bundle)

            // 2. the twice-folded sheet (sheet_map, 800x820; print window 740x760)
            paperSheet = V.Rect("TwiceFoldedHanji", FullRoot, 0, 0, PaperW, PaperH);
            paperSheet.anchorMin = paperSheet.anchorMax = paperSheet.pivot = new Vector2(.5f, .5f);
            paperSheet.anchoredPosition = new Vector2(PaperX + PaperW * .5f - UiPageFit304.Width * .5f, -(PaperY + PaperH * .5f - UiPageFit304.Height * .5f));
            paperGraphic = paperSheet.gameObject.AddComponent<WorldMapPaperGraphic>();
            Texture paper = style.Sprites.SheetMap != null ? style.Sprites.SheetMap.texture : null;
            if (paper == null) paper = Resources.Load<Texture2D>("WorldMap/Paper/HanjiWorn");
            if (paper == null) paper = dependencies.PaperTexture;
            paperGraphic.Configure(paper, Color.white);
            paperTexture304 = paper;
            Shader shader = Resources.Load<Shader>("WorldMap/PaperMapSurface");
            if (shader == null) throw new InvalidOperationException("Paper map surface shader is missing.");
            paperMaterial = new Material(shader) { name = "TwiceFoldedMap_Runtime", hideFlags = HideFlags.DontSave };
            paperMaterial.SetTexture("_MapTex", data.DisplayMap);
            paperMaterial.SetFloat("_KnownBase", data.HasIllustration ? 1 : 0);
            paperMaterial.SetFloat("_PaintedRelief", data.PaintedRelief ? 1 : 0);
            paperMaterial.SetFloat("_UnknownVeil", data.HasIllustration ? mapStyle.UnknownVeil : 0f);
            ConfigureFog304(paperMaterial);
            paperMaterial.SetTexture("_FogTex", fogTexture);
            paperGraphic.material = paperMaterial;
            paperGraphic.raycastTarget = false;
            paperInk = new WorldMapPaperInk(); ConfigureInk(paperInk);
            paperMaterial.SetTexture("_InkTex", paperInk.Texture);
            macroInk = new WorldMapPaperInk(); ConfigureInk(macroInk);
            paperMaterial.SetTexture("_MacroInkTex", macroInk.Texture);
            ApplyPaper308(paperMaterial, false);   // #308: _MAP308 + the baked terrain picture (no-op without a bundle)
            foldMap = V.Rect("PrintedMapWindow", paperSheet, 0, 0, PaperW - 2f * PrintInset, PaperH - 2f * PrintInset);
            foldMap.anchorMin = foldMap.anchorMax = foldMap.pivot = new Vector2(.5f, .5f);
            foldMap.anchoredPosition = Vector2.zero;
            BuildStrokes308(foldMap);   // #308: brush strips under the marks (own nested Canvas)
            fullMarkers = V.Stretch("MapMarkers", foldMap);
            markerVisibility = fullMarkers.gameObject.AddComponent<CanvasGroup>();
            markerVisibility.interactable = markerVisibility.blocksRaycasts = false;
            RectTransform inputRect = V.Stretch("MapInput", foldMap);
            V.Image(inputRect, new Color(0, 0, 0, .001f), null, true);
            var input = inputRect.gameObject.AddComponent<WorldMapInputSurface>(); input.Owner = this;
            fullLabels = V.Stretch("MapLabels", foldMap);

            // 3. the page: legend, places, controls, title slip (rebuilt on every open, WorldMapPresenter.Page304.cs)
            BuildPageRoot304();

            FullRoot.gameObject.SetActive(false);
            RefreshFullLayout(true);
        }

        void ConfigureInk(WorldMapPaperInk ink)
        {
            // #304: brush ink always (was the icon-mode switch); the compact painted relief keeps its own road look
            ink.BrushStyle = true;
            ink.PaintedRelief = data.PaintedRelief;
            ink.Daedong = mapStyle.Daedong && data.HasIllustration;
            ink.InkColour = InkTexel(style.Ink); ink.AshColour = InkTexel(style.Ash); ink.PaperColour = InkTexel(style.Sheet);
            ink.RoadWidthPx = mapStyle.RoadWidthPx; ink.TickMetres = mapStyle.RoadTickMetres; ink.TickMinPx = mapStyle.RoadTickMinPx;
            ink.TickLengthPx = mapStyle.RoadTickLengthPx; ink.TickWidthPx = mapStyle.RoadTickWidthPx;
            ink.RiverDoubleMinPx = mapStyle.RiverDoubleMinPx; ink.RiverDoubleMaxMetresPerPx = mapStyle.RiverDoubleMaxMetresPerPx; ink.RiverRimPx = mapStyle.RiverRimPx; ink.RiverSinglePx = mapStyle.RiverSinglePx;
            ink.DotPx = mapStyle.BorderDotPx; ink.DotGapPx = mapStyle.BorderDotGapPx;
        }

        /// <summary>#304 QA: 걷지 않은 땅 as a fog-covered picture (the relief's shading stays, faint and monochrome) and the
        /// rounded walked-land edge. Illustrated maps only, like the veil; without the MapStyle fields the shader keeps the
        /// flat .84 sheet and the old cell feather (all its defaults are 0).</summary>
        void ConfigureFog304(Material material)
        {
            if (material == null) return;
            bool illustrated = data.HasIllustration;
            material.SetFloat("_UnknownShade", illustrated ? Mathf.Clamp01(mapStyle.UnknownShade) : 0f);
            material.SetFloat("_UnknownRelief", illustrated ? Mathf.Max(0f, mapStyle.UnknownRelief) : 0f);
            material.SetFloat("_UnknownReliefTexels", Mathf.Max(0f, mapStyle.UnknownReliefTexels));
            material.SetFloat("_FogSoft", mapStyle.FogSoftEdge ? 1f : 0f);
            material.SetFloat("_FogNoise", Mathf.Max(0f, mapStyle.FogEdgeNoise));
            // #304 QA2: the painted relief as a monochrome ink wash on the sheet (walked land, and through the fog's share of
            // it the unwalked land too). An older shader without the property ignores it (coloured relief as before).
            material.SetFloat("_PaintedInk", illustrated && data.PaintedRelief ? Mathf.Clamp01(mapStyle.PaintedInkWash) : 0f);
            material.SetVector("_PaintedInkRange", new Vector4(mapStyle.PaintedInkLight, Mathf.Min(mapStyle.PaintedInkDark, mapStyle.PaintedInkLight - .01f),
                Mathf.Clamp01(mapStyle.PaintedInkMax), Mathf.Max(0f, mapStyle.PaintedZoneNeutral)));
        }

        // The ink layers are linear textures read straight into the colour math: store tokens as linear values so ink
        // #141413 shows as #141413 (not the grey its sRGB bytes would give in a Linear project) and the sheet matches the paper.
        static Color32 InkTexel(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? (Color32)c.linear : (Color32)c;

        WorldMapLineSpec[] Straighten(WorldMapLineSpec[] lines)
        {
            // D15 (b): roads drawn straight, 대동여지도 style. Done once per line set (not per frame); fewer segments also
            // means less raster work than the original polylines.
            if (lines == null) return null;
            float tolerance = Mathf.Max(0f, mapStyle != null ? mapStyle.RoadStraightenMetres : 0f);
            if (tolerance <= 0f) return lines;
            var result = new WorldMapLineSpec[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                if (l == null || l.Points == null || (l.Kind != WorldMapLineKind.Road && l.Kind != WorldMapLineKind.Trail)) { result[i] = l; continue; }
                result[i] = new WorldMapLineSpec { Id = l.Id, Kind = l.Kind, PixelWidth = l.PixelWidth, Points = WorldMapGeometry.Simplify(l.Points, tolerance) };
            }
            return result;
        }

        void RefreshFullLayout(bool force = false)
        {
            if (FullRoot == null || foldMap == null) return;
            Vector2 rootSize = FullRoot.rect.size;
            if (!force && (rootSize - lastFullRootSize).sqrMagnitude < .25f) return;
            lastFullRootSize = rootSize;

            // same rule as UiPageFit304 on the page roots: the sheet scales with the columns around it
            float k = Mathf.Min(1f, rootSize.x / UiPageFit304.Width, rootSize.y / UiPageFit304.Height);
            if (k <= 0f || float.IsNaN(k)) k = 1f;
            bool scaleChanged = !Mathf.Approximately(k, pageScale);
            pageScale = k;
            float worldWidth = Mathf.Max(1f, data.BoundsMax.x - data.BoundsMin.x);
            float worldHeight = Mathf.Max(1f, data.BoundsMax.y - data.BoundsMin.y);
            float width = PaperW * k, height = PaperH * k;
            paperSheet.sizeDelta = new Vector2(width, height);
            paperSheet.anchoredPosition = new Vector2((PaperX + PaperW * .5f - UiPageFit304.Width * .5f) * k, -(PaperY + PaperH * .5f - UiPageFit304.Height * .5f) * k);
            Vector2 window = new Vector2((PaperW - 2f * PrintInset) * k, (PaperH - 2f * PrintInset) * k);
            if (wholeWorldLayout)
            {
                float aspect = worldWidth / worldHeight;
                if (window.x / window.y > aspect) window.x = window.y * aspect;
                else window.y = window.x / aspect;
            }
            foldMap.sizeDelta = window;
            paperMaterial.SetVector("_MapWindow", new Vector4((1f - window.x / width) * .5f, (1f - window.y / height) * .5f, window.x / width, window.y / height));
            inkReady = false;
            if (scaleChanged) ScaleMarks304(k);

            if (followCurrent && fullUv.width < .999f)
            {
                float viewAspect = window.x / Mathf.Max(1f, window.y);
                bool interior = data.ZoneAt(CurrentWorld()) != null;
                fullUv.height = Mathf.Clamp(fullUv.width * worldWidth / (viewAspect * worldHeight), interior ? .005f : .03f, 1f);
                CenterFullOn(new Vector2(CurrentWorld().x, CurrentWorld().z));
            }
        }

        void LoadProgressIfReady()
        {
            if (progressLoaded || session.Progress == null || session.Progress.ui == null) return;
            session.Progress.ui.Normalize();
            int byteCount = discovery.ByteCount;
            if (WorldMapDiscoveryGrid.TryDecode(session.Progress.ui.discoveredCells, byteCount, out byte[] bytes))
            { discovery = new WorldMapDiscoveryGrid(data.BoundsMin, data.BoundsMax, data.Outline, bytes); fogPlayable = null; }
            if (session.Progress.ui.discoveredMarkers == null) session.Progress.ui.discoveredMarkers = new List<string>();
            foreach (WorldMapMarkerSpec marker in data.Markers)
                if (marker != null && marker.InitiallyDiscovered && !session.Progress.ui.discoveredMarkers.Contains(marker.Id))
                    session.Progress.ui.discoveredMarkers.Add(marker.Id);
            progressLoaded = true; RefreshFog();
        }

        void ApplyFold()
        {
            if (FullRoot == null) return;
            paperGraphic.SetProgress(fold);
            float readable = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.94f, 1f, fold));
            mapControls.alpha = readable;
            mapControls.interactable = mapControls.blocksRaycasts = targetExpanded && fold >= .999f;
            markerVisibility.alpha = fold >= .995f ? 1f : 0f;
            ApplyFold308();
            ApplyVeilFold304();
            FullRoot.gameObject.SetActive(Visible && (targetExpanded || fold > 0));
        }

        void ApplyUvAndMarkers(Vector3 current)
        {
            if (!initialized && FullRoot == null) return;
            if (RefreshShortcutLines306()) miniRevision++;
            WorldMapZoneSpec zone = data.ZoneAt(current);
            bool interior = zone != null;
            ApplyInteriorMask(zone);
            if (paperMaterial != null && (targetExpanded || fold > 0))
            {
                paperMaterial.SetVector("_WorldUv", new Vector4(fullUv.x, fullUv.y, fullUv.width, fullUv.height));
                WorldMapRegionTile detail = data.PaintedRelief ? data.RegionTiles.FirstOrDefault(t => t != null && t.Texture != null && t.WorldUv.Contains(fullUv.min) && t.WorldUv.Contains(fullUv.max)) : null;
                var tile = detail != null ? detail.WorldUv : new Rect(0, 0, 1, 1);
                paperMaterial.SetTexture("_MapTex", data.DisplayMap);
                paperMaterial.SetTexture("_DetailTex", detail != null ? detail.Texture : data.ExploredMap);
                paperMaterial.SetFloat("_HasDetail", data.PaintedRelief && (detail != null || data.ExploredMap != null) ? 1 : 0);
                paperMaterial.SetTexture("_CaveTex", zone?.Illustration);
                var caveUv = zone?.IllustrationWorldUv ?? new Rect(0, 0, 1, 1);
                paperMaterial.SetVector("_CaveUv", new Vector4(caveUv.x, caveUv.y, caveUv.width, caveUv.height));
                paperMaterial.SetFloat("_HasCave", interior && zone.Illustration != null ? 1 : 0);
                paperMaterial.SetVector("_MapTileUv", new Vector4(tile.x, tile.y, tile.width, tile.height));
                paperMaterial.SetFloat("_Interior", interior ? 1f : 0f);
                // #308: outdoors the brush strips carry every line (no CPU raster, no realm border dots)
                if ((!inkReady || fullUv != lastInkUv || zone != lastInkZone) && SkipRaster308(interior, zone))
                { lastInkUv = fullUv; lastInkZone = zone; inkReady = true; }
                if (!inkReady || fullUv != lastInkUv || zone != lastInkZone)
                {
                    // Display size in PAGE px, so stroke widths stay the same on 16:10 or at UI 배율 1.3 (sheet scaled by k).
                    Vector2 display = foldMap.rect.size / Mathf.Max(.01f, pageScale);
                    float viewHeight = fullUv.height * (data.BoundsMax.y - data.BoundsMin.y);
                    paperInk.projectionWorldHeight = viewHeight; macroInk.projectionWorldHeight = viewHeight;
                    bool daedong = paperInk.Daedong && !interior;
                    paperInk.Draw(interior ? (zone.Illustration != null ? null : zone.DetailLines) : data.HasIllustration ? (daedong ? straightLines : exploredLines) : data.Lines,
                        interior && zone.Illustration == null ? zone.DetailPath : null, projection, fullUv, display);
                    macroInk.BorderPolygons = daedong ? realmBorders : null;
                    macroInk.Draw(!interior && data.HasIllustration ? majorLines : null, null, projection, fullUv, display);
                    lastInkUv = fullUv; lastInkZone = zone; inkReady = true;
                }
                SyncStrokes308(interior);
            }
            string zoneLabel = zone != null ? zone.Label : RegionLabel(new Vector2(current.x, current.z));
            if (displayedZoneLabel != zoneLabel)
            {
                displayedZoneLabel = zoneLabel;
                RefreshTitle304(zoneLabel);
            }
            PlaceMarks304(current, interior);
            RefreshMapHover();
        }

        /// <summary>Discovered shortcuts join the known line set (sheet and minimap ink). True when the set changed.</summary>
        bool RefreshShortcutLines306()
        {
            int discoveredShortcuts = session.Progress?.campaign?.DiscoveredShortcuts?.Count ?? 0;
            if (shortcutCount == discoveredShortcuts) return false;
            shortcutCount = discoveredShortcuts;
            var extra = (displayedShortcuts ?? Array.Empty<WorldActShortcut>()).Where(s => s != null && s.Path != null && session.Progress?.campaign?.DiscoveredShortcuts?.Contains(s.Id) == true)
                .Select(s => new WorldMapLineSpec { Id = s.Id, Kind = WorldMapLineKind.Trail, PixelWidth = 2, Points = s.Path.Select(p => new Vector2(p.x, p.z)).ToArray() });
            exploredLines = (baseExploredLines ?? Array.Empty<WorldMapLineSpec>()).Concat(extra).ToArray();
            straightLines = Straighten(exploredLines); inkReady = false;
            return true;
        }

        void PlaceFull(RectTransform marker, Vector2 world, bool visible)
        {
            Vector2 n = projection.WorldToNormalized(world);
            Vector2 v = new Vector2((n.x - fullUv.x) / fullUv.width, (n.y - fullUv.y) / fullUv.height);
            visible &= v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1;
            if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
            if (!visible) return;
            marker.anchorMin = marker.anchorMax = v; marker.anchoredPosition = Vector2.zero;
        }

        void PlaceFullLabel(TMP_Text label, Vector2 world, bool visible, Vector2 offset, bool forceLeft = false)
        {
            Vector2 n = projection.WorldToNormalized(world);
            Vector2 v = new Vector2((n.x - fullUv.x) / fullUv.width, (n.y - fullUv.y) / fullUv.height);
            visible &= targetExpanded && fold >= .995f && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1;
            if (label.gameObject.activeSelf != visible) label.gameObject.SetActive(visible);
            if (!visible) return;
            MeasureAwake304(label);   // #304: labels built while the map was hidden measured at 1/10 size (TMP wakes late)
            bool left = forceLeft || v.x > .76f;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = v; rect.pivot = new Vector2(left ? 1 : 0, .5f);
            rect.anchoredPosition = new Vector2((left ? -Mathf.Abs(offset.x) : Mathf.Abs(offset.x)) * pageScale, offset.y * pageScale);
            var alignment = left ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            if (label.alignment != alignment) label.alignment = alignment;
        }

        void PlaceCentredLabel(TMP_Text label, Vector2 world, bool visible, float dy)
        {
            Vector2 n = projection.WorldToNormalized(world);
            Vector2 v = new Vector2((n.x - fullUv.x) / fullUv.width, (n.y - fullUv.y) / fullUv.height);
            visible &= targetExpanded && fold >= .995f && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1;
            if (label.gameObject.activeSelf != visible) label.gameObject.SetActive(visible);
            if (!visible) return;
            MeasureAwake304(label);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = v; rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0f, dy);
            if (label.alignment != TextAlignmentOptions.Center) label.alignment = TextAlignmentOptions.Center;
        }

        void BuildFog()
        {
            fogTexture = new Texture2D(discovery.Width, discovery.Height, TextureFormat.RGBA32, false, true)
            { name = "WorldMapDiscoveryFog", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            RefreshFog();
        }

        /// <summary>Every cell (build, saved progress load). #307: into the cached texel array; the outline test per cell is
        /// cached too (fogPlayable, dropped when the discovery grid is rebuilt).</summary>
        void RefreshFog()
        {
            if (fogTexture == null) return;
            int w = discovery.Width, h = discovery.Height;
            if (fogPixels == null || fogPixels.Length != w * h) { fogPixels = new Color32[w * h]; fogPlayable = null; }
            if (fogPlayable == null)
            {
                fogPlayable = new bool[w * h];
                // #308 map fix (M3): with a notation bundle a cell nobody can ever reveal is fog too. Before, a cell outside
                // the outline was written as "walked" (alpha 0); the compact map's top row (cell centres ON the outline's
                // north edge) was such a row, and the crisp edge drew its upper half as walked land with an ink rim.
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) fogPlayable[y * w + x] = n308 != null || discovery.IsPlayableCell(x, y);
            }
            Color32 unknown = FogUnknown307();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                fogPixels[y * w + x] = fogPlayable[y * w + x] && !discovery.IsDiscovered(x, y) ? unknown : new Color32(0, 0, 0, 0);
            fogTexture.SetPixels32(fogPixels); fogTexture.Apply(false, false);
            fogRevision308++;   // #308 map 3c: the texels changed (WorldMapPresenter.Reveal308.cs reads them again)
            miniRevision++;   // load only (once): the reveal path below leaves the minimap print alone
        }

        /// <summary>#307: after WorldMapDiscoveryGrid.Reveal(worldXZ, radius) only cells inside the same bounding box can have
        /// changed, so only they are rewritten (same texels as RefreshFog). No array is allocated and MiniRevision is not bumped:
        /// the HUD minimap samples the same _FogTex and needs no new print.</summary>
        void RefreshFogAround(Vector2 worldXZ, float radius)
        {
            if (fogTexture == null) return;
            int w = discovery.Width, h = discovery.Height;
            if (fogPixels == null || fogPlayable == null || fogPixels.Length != w * h) { RefreshFog(); return; }
            float cell = WorldMapDiscoveryGrid.CellSize;
            int minX = Mathf.Max(0, Mathf.FloorToInt((worldXZ.x - radius - data.BoundsMin.x) / cell));
            int maxX = Mathf.Min(w - 1, Mathf.FloorToInt((worldXZ.x + radius - data.BoundsMin.x) / cell));
            int minY = Mathf.Max(0, Mathf.FloorToInt((worldXZ.y - radius - data.BoundsMin.y) / cell));
            int maxY = Mathf.Min(h - 1, Mathf.FloorToInt((worldXZ.y + radius - data.BoundsMin.y) / cell));
            Color32 unknown = FogUnknown307();
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
                fogPixels[y * w + x] = fogPlayable[y * w + x] && !discovery.IsDiscovered(x, y) ? unknown : new Color32(0, 0, 0, 0);
            fogTexture.SetPixels32(fogPixels); fogTexture.Apply(false, false);
            fogRevision308++;   // #308 map 3c
        }

        // The shader reads alpha only (unknown = 1); RGB is the sheet so a bilinear sample never darkens.
        Color32 FogUnknown307() => new Color(style.Sheet.r, style.Sheet.g, style.Sheet.b, 1f);

        void CenterFullOn(Vector2 world)
        {
            Vector2 n = projection.WorldToNormalized(world);
            fullUv.x = n.x - fullUv.width * .5f; fullUv.y = n.y - fullUv.height * .5f;
            fullUv = WorldMapProjection.ClampUvRect(fullUv);
        }

        float nextSeatSearch;
        Vector3 CurrentWorld()
        {
            if (session.Walker != null && session.Walker.Seated)
            {
                // #307: the scene-wide search runs at most twice a second (it ran several times per frame while seated without a seat)
                if ((vehicleSeat == null || !vehicleSeat.Occupied) && Time.unscaledTime >= nextSeatSearch)
                {
                    nextSeatSearch = Time.unscaledTime + .5f;
                    vehicleSeat = UnityEngine.Object.FindObjectsByType<Vehicle.WorldMacroPalanquinSeat>(FindObjectsSortMode.None)
                        .FirstOrDefault(s => s.Occupied && s.CombatWalker == session.Walker);
                }
                if (vehicleSeat != null && vehicleSeat.Vehicle != null) return vehicleSeat.Vehicle.transform.position;
            }
            return session.Walker != null && session.Walker.Body != null ? session.Walker.Body.transform.position : session.Content.StartFeet;
        }

        string RegionLabel(Vector2 world)
        {
            if (data.Locations != null) return data.Locations.RealmAt(new Vector3(world.x, 0, world.y))?.Name ?? "강토";
            foreach (var region in sheet.Regions) if (WorldMapDiscoveryGrid.Contains(region.Polygon, world)) return region.Label;
            return "강토";
        }

        void ToggleLegend()
        {
            legendFolded304 = !legendFolded304;
            if (legend != null) legend.SetActive(!legendFolded304);
            RefreshLegendButton304();
        }

        void OnDestroy()
        {
            CloseRequested = null;
            FoldRustle = null;
            DisposeInput304();
            if (paperMaterial != null) Destroy(paperMaterial);
            DisposeLegendSwatch304();
            DisposeInteriorDiscovery();
            Dispose308();
            macroInk?.Dispose();
            paperInk?.Dispose();
            miniInk?.Dispose();
            if (miniStub != null) Destroy(miniStub.gameObject);   // #306: an attached HUD minimap root belongs to the HUD
            MiniRoot = null;
            if (FullRoot != null) Destroy(FullRoot.gameObject);
            if (fogTexture != null) Destroy(fogTexture);
            if (ownsRuntimeData && data != null)
            {
                if (data.BaseMap != null) Destroy(data.BaseMap);
                Destroy(data);
            }
        }
    }
}
