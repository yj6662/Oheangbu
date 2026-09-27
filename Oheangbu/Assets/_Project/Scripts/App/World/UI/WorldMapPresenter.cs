using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Data.World;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    [DisallowMultipleComponent]
    public sealed partial class WorldMapPresenter : MonoBehaviour
    {
        public const float OpenDuration = 1.1f;
        public const float CloseDuration = .65f;
        const float WalkHalfExtent = 128f;
        const float VehicleHalfExtent = 256f;

        sealed class MarkerView
        {
            public WorldMapMarkerSpec Spec;
            public RectTransform Mini;
            public RectTransform Full;
            public Text FullLabel;
        }

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
        WorldMapPaperGraphic paperGraphic;
        WorldMapPaperInk paperInk;
        Material paperMaterial;
        Material miniDiscoveryMaterial;
        Material miniPaperMaterial;
        WorldMapPaperInk macroInk;
        WorldMapLineSpec[] majorLines, exploredLines,baseExploredLines;
        WorldActShortcut[] displayedShortcuts;int shortcutCount=-1;
        WorldMapPolylineGraphic miniMajorLines;
        readonly List<Text> regionLabels = new List<Text>();
        WorldMapRegionTile selectedTile;
        RawImage caveIllustration;
        Vehicle.WorldMacroPalanquinSeat vehicleSeat;
        RectTransform paperSheet, fullMarkers;
        CanvasGroup mapControls, markerVisibility;
        Image backdrop;
        Rect lastInkUv;
        WorldMapZoneSpec lastInkZone;
        bool inkReady;
        readonly List<MarkerView> markers = new List<MarkerView>();
        RectTransform miniViewport, miniMarkers, foldMap, fullLabels;
        RawImage miniMap, miniFog;
        WorldMapPolylineGraphic miniLines, caveFootprint, caveDetail;
        Text miniTitle, fullTitle;
        GameObject legend;
        RectTransform miniPlayer, fullPlayer, miniCheckpoint, fullCheckpoint, miniDrop, fullDrop, miniPin, fullPin;
        Texture2D fogTexture;
        Font ownedFont;
        RectTransform legendRect;
        Text fullPlayerLabel, fullCheckpointLabel, fullDropLabel, fullPinLabel;
        Rect miniUv = new Rect(0, 0, 1, 1), fullUv = new Rect(0, 0, 1, 1);
        bool initialized, targetExpanded, progressLoaded, followCurrent, ownsRuntimeData, wholeWorldLayout;
        float fold;
        float transitionFromFold, transitionDuration;
        double transitionStartedAt;
        float nextDiscoveryProbe;
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
            baseExploredLines=exploredLines;
            displayedShortcuts=UnityEngine.Object.FindObjectsByType<WorldActShortcut>(FindObjectsSortMode.None).Where(x=>x.gameObject.scene==session.gameObject.scene).ToArray();
            discovery = new WorldMapDiscoveryGrid(data.BoundsMin, data.BoundsMax, data.Outline);
            EnsureFont(); BuildFog(); BuildMini(parent); BuildFull(parent); BuildMarkers(); ApplyIconStyle(); BuildMapHover();
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
            if (!expanded) followCurrent = false;
            if (expanded) {FullRoot.gameObject.SetActive(Visible);if(data.PaintedRelief)FocusCurrent();}
            if (ReducedMotion) { fold = expanded ? 1f : 0f; transitionFromFold = fold; transitionDuration = 0f; ApplyFold(); }
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            if (!visible) HideMapHover();
            if (!initialized) return;
            MiniRoot.gameObject.SetActive(visible && !targetExpanded && fold <= 0f);
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
                if(!ReducedMotion && ((previousFold < .54f && fold >= .54f) || (previousFold > .54f && fold <= .54f)))
                    FoldRustle?.Invoke(targetExpanded ? .25f : .18f);
                RefreshFullLayout();
                ApplyFold();
                Vector3 current = CurrentWorld();
                LoadProgressIfReady();
                RevealInterior(current);
                if (progressLoaded && Time.unscaledTime >= nextDiscoveryProbe && WorldMapDiscoveryGrid.Contains(data.Outline, new Vector2(current.x, current.z)))
                {
                    nextDiscoveryProbe = Time.unscaledTime + .2f;
                    if ((data.ZoneAt(current) == null || !data.ZoneAt(current).ExploreWalkedPassages) && discovery.Reveal(new Vector2(current.x, current.z)))
                    {
                        session.Progress.ui.discoveredCells = Convert.ToBase64String(discovery.Export());
                        RefreshFog();
                    }
                    foreach (WorldMapMarkerSpec marker in data.Markers)
                        if (marker != null && !marker.RequiresArrival && discovery.IsDiscovered(marker.WorldXZ) && !session.Progress.ui.discoveredMarkers.Contains(marker.Id))
                            session.Progress.ui.discoveredMarkers.Add(marker.Id);
                }
                if (followCurrent) CenterFullOn(new Vector2(current.x, current.z));
                ApplyUvAndMarkers(current);
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
            float worldWidth = data.BoundsMax.x - data.BoundsMin.x;
            float worldHeight = data.BoundsMax.y - data.BoundsMin.y;
            WorldMapZoneSpec zone = data.ZoneAt(CurrentWorld());
            float width = (zone != null ? 150f : data.PaintedRelief ? 1100f : 640f) / worldWidth;
            float viewAspect = foldMap.rect.width / Mathf.Max(1f, foldMap.rect.height);
            fullUv.width = width;
            fullUv.height = Mathf.Clamp(width * worldWidth / (viewAspect * worldHeight), zone != null ? .005f : .03f, 1f);
            followCurrent = true; CenterFullOn(new Vector2(CurrentWorld().x, CurrentWorld().z));
            ApplyUvAndMarkers(CurrentWorld());
        }

        public void SetPin(Vector2 worldXZ, string label)
        {
            if (!initialized || data.ZoneAt(CurrentWorld()) != null || !WorldMapDiscoveryGrid.Contains(data.Outline, worldXZ) || !discovery.IsDiscovered(worldXZ) || session.Progress == null) return;
            session.Progress.ui.pin.active = true; session.Progress.ui.pin.worldXZ = worldXZ;
            session.Progress.ui.pin.label = string.IsNullOrWhiteSpace(label) ? "표식" : label.Trim();
            bool saved=session.SaveNow(out _);PlaytestUiRoot.Instance?.PlayNamedSound(saved?"ui_select":"ui_error",.3f); ApplyUvAndMarkers(CurrentWorld());
        }

        public void ClearPin()
        {
            if (!initialized || session.Progress == null) return;
            session.Progress.ui.pin.active = false; session.Progress.ui.pin.label = "";
            bool saved=session.SaveNow(out _);PlaytestUiRoot.Instance?.PlayNamedSound(saved?"ui_select":"ui_error",.3f); ApplyUvAndMarkers(CurrentWorld());
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

        void BuildMini(RectTransform parent)
        {
            MiniRoot = PlaytestUiView.Rect("PersistentMinimap", parent, 0, 0, 268, 306);
            MiniRoot.anchorMin = MiniRoot.anchorMax = MiniRoot.pivot = Vector2.one;
            MiniRoot.anchoredPosition = new Vector2(-24, -24);
            PlaytestUiView.Image(MiniRoot, new Color(dependencies.Paper.r, dependencies.Paper.g, dependencies.Paper.b, .94f));
            PlaytestUiView.Image(PlaytestUiView.Rect("InkBorder", MiniRoot, 8, 42, 252, 252), new Color(dependencies.Ink.r, dependencies.Ink.g, dependencies.Ink.b, .85f));
            miniViewport = PlaytestUiView.Rect("MapViewport", MiniRoot, 10, 44, 248, 248);
            miniViewport.gameObject.AddComponent<RectMask2D>();
            if (dependencies.PaperTexture != null) PlaytestUiView.Raw(PlaytestUiView.Stretch("Paper", miniViewport), dependencies.PaperTexture, dependencies.Paper);
            else PlaytestUiView.Image(PlaytestUiView.Stretch("Paper", miniViewport), dependencies.Paper);
            miniMap = PlaytestUiView.Raw(PlaytestUiView.Stretch("MapInk", miniViewport), data.DisplayMap, Color.white);
            var miniShader=Resources.Load<Shader>("WorldMap/MiniPaper");
            if(miniShader!=null)
            {
                miniPaperMaterial=new Material(miniShader){name="MiniPaper_Runtime",hideFlags=HideFlags.DontSave};
                miniPaperMaterial.SetTexture("_FogTex",fogTexture);miniPaperMaterial.SetTexture("_ArtTex",data.DisplayMap);
                miniPaperMaterial.SetColor("_PaperColor",dependencies.Paper);miniMap.material=miniPaperMaterial;
            }
            miniMajorLines = PlaytestUiView.Stretch("KnownRivers", miniViewport).gameObject.AddComponent<WorldMapPolylineGraphic>();
            miniMajorLines.raycastTarget = false;
            miniLines = PlaytestUiView.Stretch("SurfaceLines", miniViewport).gameObject.AddComponent<WorldMapPolylineGraphic>();
            miniLines.raycastTarget = false;
            if (data.HasIllustration)
            {
                var lineShader = Resources.Load<Shader>("WorldMap/DiscoveryLines");
                if (lineShader == null) throw new InvalidOperationException("Illustrated map discovery shader is missing.");
                miniDiscoveryMaterial = new Material(lineShader) { name = "MiniMapDiscovery_Runtime", hideFlags = HideFlags.DontSave };
                miniDiscoveryMaterial.SetTexture("_FogTex", fogTexture);
                miniLines.material = miniDiscoveryMaterial;
            }
            miniFog = PlaytestUiView.Raw(PlaytestUiView.Stretch("DiscoveryFog", miniViewport), fogTexture, Color.white);
            caveIllustration = PlaytestUiView.Raw(PlaytestUiView.Stretch("CaveIllustration", miniViewport), null, Color.white);
            caveFootprint = PlaytestUiView.Stretch("CaveFootprint", miniViewport).gameObject.AddComponent<WorldMapPolylineGraphic>();
            caveFootprint.raycastTarget = false;
            RectTransform detailRect = PlaytestUiView.Stretch("CaveDetail", miniViewport);
            caveDetail = detailRect.gameObject.AddComponent<WorldMapPolylineGraphic>(); caveDetail.color = dependencies.Seal; caveDetail.raycastTarget = false;
            miniMarkers = PlaytestUiView.Stretch("Markers", miniViewport);
            miniTitle = PlaytestUiView.Text(MiniRoot, "Location", "강토", Font, 18, dependencies.Ink, 14, 8, 240, 34, TextAnchor.MiddleLeft);
            PlaytestUiView.Text(MiniRoot, "North", "북 N", Font, 14, dependencies.Muted, 205, 8, 48, 30, TextAnchor.MiddleRight);
            miniPlayer = CreateMarker(miniMarkers, "Player", dependencies.Seal, 17, false, "");
            miniCheckpoint = CreateMarker(miniMarkers, "Checkpoint", dependencies.Ink, 9, false, "");
            miniDrop = CreateMarker(miniMarkers, "Drop", dependencies.Seal, 8, false, "");
            miniPin = CreateMarker(miniMarkers, "Pin", dependencies.Seal, 9, false, "");
        }

        void BuildFull(RectTransform parent)
        {
            FullRoot = PlaytestUiView.Stretch("ExpandedWorldMap", parent);
            backdrop = PlaytestUiView.Image(FullRoot, new Color(.025f, .025f, .022f, .66f));
            paperSheet = PlaytestUiView.Rect("TwiceFoldedHanji", FullRoot, 0, 0, 1240, 899);
            paperSheet.anchorMin = paperSheet.anchorMax = paperSheet.pivot = new Vector2(.5f, .5f);
            paperSheet.anchoredPosition = new Vector2(0, -8);
            paperGraphic = paperSheet.gameObject.AddComponent<WorldMapPaperGraphic>();
            Texture paper = Resources.Load<Texture2D>("WorldMap/Paper/HanjiWorn");
            paperGraphic.Configure(paper != null ? paper : dependencies.PaperTexture, Color.white);
            Shader shader = Resources.Load<Shader>("WorldMap/PaperMapSurface");
            if(shader == null) throw new InvalidOperationException("Paper map surface shader is missing.");
            paperMaterial = new Material(shader) { name = "TwiceFoldedMap_Runtime", hideFlags = HideFlags.DontSave };
            paperMaterial.SetTexture("_MapTex", data.DisplayMap);
            paperMaterial.SetFloat("_KnownBase", data.HasIllustration ? 1 : 0);
            paperMaterial.SetTexture("_FogTex", fogTexture);
            paperGraphic.material = paperMaterial;
            paperGraphic.raycastTarget = false;
            paperInk = new WorldMapPaperInk();
            paperMaterial.SetTexture("_InkTex", paperInk.Texture);
            macroInk = new WorldMapPaperInk();
            paperMaterial.SetTexture("_MacroInkTex", macroInk.Texture);
            foldMap = PlaytestUiView.Rect("PrintedMapWindow", paperSheet, 0, 0, 1042, 755);
            foldMap.anchorMin = foldMap.anchorMax = foldMap.pivot = new Vector2(.5f, .5f);
            foldMap.anchoredPosition = Vector2.zero;
            fullMarkers = PlaytestUiView.Stretch("MapMarkers", foldMap);
            markerVisibility = fullMarkers.gameObject.AddComponent<CanvasGroup>();
            RectTransform inputRect = PlaytestUiView.Stretch("MapInput", foldMap);
            PlaytestUiView.Image(inputRect, new Color(0, 0, 0, .001f), null, true);
            var input = inputRect.gameObject.AddComponent<WorldMapInputSurface>(); input.Owner = this;
            fullLabels = PlaytestUiView.Stretch("MapLabels", foldMap);
            var controls = PlaytestUiView.Stretch("PaperMapControls", FullRoot);
            mapControls = controls.gameObject.AddComponent<CanvasGroup>();
            fullTitle = PlaytestUiView.Text(controls, "MapTitle", "강토 지도", Font, 25, dependencies.Paper, 0, 0, 440, 42, TextAnchor.MiddleCenter);
            RectTransform titleRect = fullTitle.rectTransform; titleRect.anchorMin = titleRect.anchorMax = new Vector2(.5f, 1); titleRect.pivot = new Vector2(.5f, 1); titleRect.anchoredPosition = new Vector2(0, -12);
            Button(controls, "Whole", "전체", new Vector2(-132, 28), ShowWholeWorld);
            Button(controls, "Current", "현재", new Vector2(-44, 28), FocusCurrent);
            Button(controls, "Legend", "범례", new Vector2(44, 28), ToggleLegend);
            Button(controls, "ClearPin", "표식 지우기", new Vector2(150, 28), ClearPin, 116);
            Button(controls, "Close", "지도 닫기", new Vector2(-250, 28), () => CloseRequested?.Invoke(), 116);
            legend = PlaytestUiView.Rect("LegendPanel", controls, 0, 0, 270, 312).gameObject;
            legendRect = (RectTransform)legend.transform; legendRect.anchorMin = legendRect.anchorMax = new Vector2(.5f, .5f); legendRect.pivot = new Vector2(0, .5f); legendRect.anchoredPosition = new Vector2(320, 0);
            PlaytestUiView.Image(legendRect, new Color(dependencies.Paper.r, dependencies.Paper.g, dependencies.Paper.b, .97f));
            PlaytestUiView.Text(legendRect, "Text", "범례\n◆ 현재 위치\n◇ 발견한 장소\n▣ 쉼터·확인 지점\n● 남긴 통보\n✦ 사용자 표식\n\n끌기 이동 · 휠 확대\n발견한 야외 영역 좌클릭: 표식", Font, 16, dependencies.Ink, 16, 14, 238, 284);
            legend.SetActive(false);
            fullPlayer = CreateMarker(fullMarkers, "Player", dependencies.Seal, 20, false, "");
            fullPlayerLabel = CreateFullLabel("Player", "현재");
            fullCheckpoint = CreateMarker(fullMarkers, "Checkpoint", dependencies.Ink, 11, false, "");
            fullCheckpointLabel = CreateFullLabel("Checkpoint", "쉼터");
            fullDrop = CreateMarker(fullMarkers, "Drop", dependencies.Seal, 10, false, "");
            fullDropLabel = CreateFullLabel("Drop", "남긴 통보");
            fullPin = CreateMarker(fullMarkers, "Pin", dependencies.Seal, 12, false, "");
            fullPinLabel = CreateFullLabel("Pin", "표식");
            FullRoot.gameObject.SetActive(false);
            RefreshFullLayout(true);
        }

        void RefreshFullLayout(bool force = false)
        {
            if (FullRoot == null || foldMap == null) return;
            Vector2 rootSize = FullRoot.rect.size;
            if (!force && (rootSize - lastFullRootSize).sqrMagnitude < .25f) return;
            lastFullRootSize = rootSize;

            float worldWidth = Mathf.Max(1f, data.BoundsMax.x - data.BoundsMin.x);
            float worldHeight = Mathf.Max(1f, data.BoundsMax.y - data.BoundsMin.y);
            const float paperAspect = 1.38f;
            float availableHeight = Mathf.Max(240f, rootSize.y - 126f);
            float availableWidth = Mathf.Max(240f, rootSize.x - 140f);
            float width = Mathf.Min(1380f, availableWidth, availableHeight * paperAspect);
            float height = width / paperAspect;
            paperSheet.sizeDelta = new Vector2(width, height);
            Vector2 window = new Vector2(width * .84f, height * .84f);
            if(wholeWorldLayout)
            {
                float aspect = worldWidth / worldHeight;
                if(window.x / window.y > aspect) window.x = window.y * aspect;
                else window.y = window.x / aspect;
            }
            foldMap.sizeDelta = window;
            paperMaterial.SetVector("_MapWindow", new Vector4((1f-window.x/width)*.5f,(1f-window.y/height)*.5f,window.x/width,window.y/height));
            inkReady = false;

            if (legendRect != null)
            {
                float desiredX = width * .5f + 16f;
                float containedX = rootSize.x * .5f - legendRect.rect.width - 8f;
                legendRect.anchoredPosition = new Vector2(Mathf.Min(desiredX, containedX), 0);
            }

            if (followCurrent && fullUv.width < .999f)
            {
                float viewAspect = window.x / Mathf.Max(1f, window.y);
                bool interior = data.ZoneAt(CurrentWorld()) != null;
                fullUv.height = Mathf.Clamp(fullUv.width * worldWidth / (viewAspect * worldHeight), interior ? .005f : .03f, 1f);
                CenterFullOn(new Vector2(CurrentWorld().x, CurrentWorld().z));
            }
        }

        RectTransform knownArea;Text knownAreaLabel;
        void BuildMarkers()
        {
            knownArea=PlaytestUiView.Rect("KnownDestinationArea",fullMarkers,0,0,40,40);
            knownArea.pivot=new Vector2(.5f,.5f);
            var areaGraphic=knownArea.gameObject.AddComponent<WorldMapKnownAreaGraphic>();areaGraphic.color=new Color(.32f,.16f,.08f,.7f);areaGraphic.raycastTarget=false;
            knownAreaLabel=CreateFullLabel("KnownDestination","들은 목적 권역");
            if(data.HasIllustration && data.Locations!=null)
                foreach(var region in data.Locations.Entries.Where(e=>e.Priority==0))
                {
                    Text title=CreateFullLabel("Region_"+region.Id,region.Name);title.fontSize=24;
                    title.color=new Color(.10f,.12f,.11f,1);title.rectTransform.sizeDelta=new Vector2(160,42);regionLabels.Add(title);
                }
            if (data.HasIllustration && data.Locations==null)
                foreach (var region in sheet.Regions)
                {
                    Text title = CreateFullLabel("Region_" + region.Id, region.Label);
                    title.fontSize = 24; title.fontStyle = FontStyle.Bold;
                    title.color = new Color(.045f, .065f, .05f, 1f);
                    title.rectTransform.sizeDelta = new Vector2(160, 42);
                    regionLabels.Add(title);
                }
            foreach (WorldMapMarkerSpec spec in data.Markers ?? Array.Empty<WorldMapMarkerSpec>())
            {
                if (spec == null) continue;
                Color color = spec.Kind == WorldMapMarkerKind.Rest ? dependencies.Seal : dependencies.Ink;
                markers.Add(new MarkerView { Spec = spec,
                    Mini = CreateMarker(miniMarkers, spec.Id, color, 7, false, ""),
                    Full = CreateMarker(fullMarkers, spec.Id, color, 9, false, ""),
                    FullLabel = CreateFullLabel(spec.Id, spec.Label) });
            }
        }

        void LoadProgressIfReady()
        {
            if (progressLoaded || session.Progress == null || session.Progress.ui == null) return;
            session.Progress.ui.Normalize();
            int byteCount = discovery.ByteCount;
            if (WorldMapDiscoveryGrid.TryDecode(session.Progress.ui.discoveredCells, byteCount, out byte[] bytes))
                discovery = new WorldMapDiscoveryGrid(data.BoundsMin, data.BoundsMax, data.Outline, bytes);
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
            backdrop.color = new Color(.025f,.025f,.022f,.66f * Mathf.SmoothStep(0,1,Mathf.Clamp01(fold/.18f)));
            FullRoot.gameObject.SetActive(Visible && (targetExpanded || fold > 0));
            MiniRoot.gameObject.SetActive(Visible && !targetExpanded && fold <= 0);
        }

        void ApplyUvAndMarkers(Vector3 current)
        {
            if (!initialized && MiniRoot == null) return;
            int discoveredShortcuts=session.Progress?.campaign?.DiscoveredShortcuts?.Count??0;
            if(shortcutCount!=discoveredShortcuts)
            {
                shortcutCount=discoveredShortcuts;
                var extra=(displayedShortcuts??Array.Empty<WorldActShortcut>()).Where(s=>s!=null&&s.Path!=null&&session.Progress?.campaign?.DiscoveredShortcuts?.Contains(s.Id)==true)
                    .Select(s=>new WorldMapLineSpec{Id=s.Id,Kind=WorldMapLineKind.Trail,PixelWidth=2,Points=s.Path.Select(p=>new Vector2(p.x,p.z)).ToArray()});
                exploredLines=(baseExploredLines??Array.Empty<WorldMapLineSpec>()).Concat(extra).ToArray();inkReady=false;
            }
            WorldMapZoneSpec zone = data.ZoneAt(current);
            bool interior = zone != null;
            ApplyInteriorMask(zone);
            float range = interior ? 64f : session.Walker != null && session.Walker.Seated ? VehicleHalfExtent : WalkHalfExtent;
            miniUv = projection.WorldWindow(new Vector2(current.x, current.z), range);
            miniFog.uvRect = miniUv;
            UpdateMiniTile(miniUv);
            caveIllustration.gameObject.SetActive(interior && zone.Illustration != null);
            if (interior && zone.Illustration != null) {
                caveIllustration.texture=zone.Illustration;var c=zone.IllustrationWorldUv;
                caveIllustration.uvRect=new Rect((miniUv.x-c.x)/c.width,(miniUv.y-c.y)/c.height,miniUv.width/c.width,miniUv.height/c.height);
            }
            miniMap.gameObject.SetActive(!interior); miniFog.gameObject.SetActive(!interior && !data.HasIllustration); miniLines.gameObject.SetActive(!interior);
            miniMajorLines.gameObject.SetActive(!interior && data.HasIllustration);
            miniMajorLines.SetPaths(majorLines, projection, miniUv, 0, 1, 1.4f);
            miniLines.SetPaths(data.HasIllustration ? exploredLines : data.Lines, projection, miniUv, 0, 1, 1.4f);
            if(paperMaterial != null && (targetExpanded || fold > 0))
            {
                paperMaterial.SetVector("_WorldUv",new Vector4(fullUv.x,fullUv.y,fullUv.width,fullUv.height));
                WorldMapRegionTile detail=data.PaintedRelief?data.RegionTiles.FirstOrDefault(t=>t!=null&&t.Texture!=null&&t.WorldUv.Contains(fullUv.min)&&t.WorldUv.Contains(fullUv.max)):null;
                var tile=detail!=null?detail.WorldUv:new Rect(0,0,1,1);
                paperMaterial.SetTexture("_MapTex",data.DisplayMap);
                paperMaterial.SetTexture("_DetailTex",detail!=null?detail.Texture:data.ExploredMap);
                paperMaterial.SetFloat("_HasDetail",data.PaintedRelief&&(detail!=null||data.ExploredMap!=null)?1:0);
                paperMaterial.SetTexture("_CaveTex",zone?.Illustration);
                var caveUv=zone?.IllustrationWorldUv??new Rect(0,0,1,1);
                paperMaterial.SetVector("_CaveUv",new Vector4(caveUv.x,caveUv.y,caveUv.width,caveUv.height));
                paperMaterial.SetFloat("_HasCave",interior&&zone.Illustration!=null?1:0);
                paperMaterial.SetVector("_MapTileUv",new Vector4(tile.x,tile.y,tile.width,tile.height));
                paperMaterial.SetFloat("_Interior",interior ? 1f : 0f);
                if(!inkReady || fullUv != lastInkUv || zone != lastInkZone)
                {
                    paperInk.projectionWorldHeight=fullUv.height*(data.BoundsMax.y-data.BoundsMin.y);
                    paperInk.Draw(interior ? (zone.Illustration!=null?null:zone.DetailLines) : data.HasIllustration ? exploredLines : data.Lines, interior && zone.Illustration==null ? zone.DetailPath : null, projection, fullUv, foldMap.rect.size);
                    macroInk.Draw(!interior && data.HasIllustration ? majorLines : null, null, projection, fullUv, foldMap.rect.size);
                    lastInkUv = fullUv; lastInkZone = zone; inkReady = true;
                }
            }
            string zoneLabel = zone != null ? zone.Label : RegionLabel(new Vector2(current.x, current.z));
            if (displayedZoneLabel != zoneLabel)
            {
                displayedZoneLabel = zoneLabel;
                miniTitle.text = zoneLabel; fullTitle.text = string.IsNullOrEmpty(zoneLabel) ? "강토 지도" : "강토 지도 · " + zoneLabel;
            }
            caveFootprint.ViewWorldHeight=miniUv.height*(data.BoundsMax.y-data.BoundsMin.y);
            caveDetail.gameObject.SetActive(!data.PaintedRelief && interior && zone.DetailPath != null && zone.DetailPath.Length > 1);
            if (caveDetail.gameObject.activeSelf) caveDetail.SetPath(zone.DetailPath, projection, miniUv, dependencies.Icons!=null?5f:2.5f);
            caveFootprint.gameObject.SetActive(interior && zone.Illustration==null && zone.DetailLines != null && zone.DetailLines.Length > 0);
            if (caveFootprint.gameObject.activeSelf) caveFootprint.SetPaths(zone.DetailLines, projection, miniUv);

            Vector2 currentXZ = new Vector2(current.x, current.z);
            PlaceMini(miniPlayer, currentXZ, true); PlaceFull(fullPlayer, currentXZ, true);
            PlaceFullLabel(fullPlayerLabel, currentXZ, true, new Vector2(13, 19));
            float yaw = 0f;
            if (session.Walker != null)
            {
                if (session.Walker.Seated && session.Walker.ViewCamera != null) yaw = session.Walker.ViewCamera.transform.eulerAngles.y;
                else if (session.Walker.Body != null) yaw = session.Walker.Body.transform.eulerAngles.y;
            }
            miniPlayer.localEulerAngles = fullPlayer.localEulerAngles = new Vector3(0, 0, -yaw);
            bool ready = session.Progress != null && session.Progress.ui != null;
            Vector2 checkpoint = ready ? WorldMapGeometry.XZ(session.Progress.ledger.checkpointPosition) : default;
            bool checkpointSeparate = ready && !interior && (checkpoint - currentXZ).sqrMagnitude > 144f;
            PlaceMini(miniCheckpoint, checkpoint, checkpointSeparate); PlaceFull(fullCheckpoint, checkpoint, checkpointSeparate);
            PlaceFullLabel(fullCheckpointLabel, checkpoint, checkpointSeparate && fullUv.width < .35f, new Vector2(-13, -18), true);
            bool hasDrop = ready && session.Progress.ledger.dropCurrency > 0;
            Vector2 drop = hasDrop ? WorldMapGeometry.XZ(session.Progress.ledger.dropPosition) : default;
            PlaceMini(miniDrop, drop, hasDrop && !interior); PlaceFull(fullDrop, drop, hasDrop && !interior);
            PlaceFullLabel(fullDropLabel, drop, hasDrop && !interior, new Vector2(12, -18));
            bool hasPin = ready && session.Progress.ui.pin.active;
            Vector2 pin = hasPin ? session.Progress.ui.pin.worldXZ : default;
            PlaceMini(miniPin, pin, hasPin && !interior); PlaceFull(fullPin, pin, hasPin && !interior);
            PlaceFullLabel(fullPinLabel, pin, hasPin && !interior, new Vector2(12, 16));
            var destination=session.KnownDestination;
            bool showDestination=destination!=null&&!interior;
            var destinationXZ=showDestination?new Vector2(destination.Destination.x,destination.Destination.z):Vector2.zero;
            PlaceFull(knownArea,destinationXZ,showDestination);
            if(showDestination)
            {
                knownArea.sizeDelta=new Vector2(Mathf.Max(20,2*destination.DestinationRadius/(data.BoundsMax.x-data.BoundsMin.x)/fullUv.width*fullMarkers.rect.width),
                    Mathf.Max(20,2*destination.DestinationRadius/(data.BoundsMax.y-data.BoundsMin.y)/fullUv.height*fullMarkers.rect.height));
                knownAreaLabel.text=destination.DestinationLabel+" 일대";
            }
            PlaceFullLabel(knownAreaLabel,destinationXZ,showDestination,new Vector2(16,26));
            foreach (MarkerView marker in markers)
            {
                bool known = !interior && ready && (marker.Spec.InitiallyDiscovered || session.Progress.ui.discoveredMarkers.Contains(marker.Spec.Id));
                known &= (marker.Spec.WorldXZ - currentXZ).sqrMagnitude > (dependencies.Icons!=null?0f:400f);
                PlaceMini(marker.Mini, marker.Spec.WorldXZ, known); PlaceFull(marker.Full, marker.Spec.WorldXZ, known);
                PlaceFullLabel(marker.FullLabel, marker.Spec.WorldXZ, known && fullUv.width < .35f, new Vector2(11, 0));
            }
            for (int i = 0; i < regionLabels.Count; i++)
            {
                Vector2 center = Vector2.zero;
                var polygon = data.Locations!=null ? data.Locations.Entries.Where(e=>e.Priority==0).ElementAt(i).Polygon : sheet.Regions[i].Polygon;
                foreach (var point in polygon) center += point;
                center /= Mathf.Max(1, polygon.Length);
                PlaceFullLabel(regionLabels[i], center, !interior, Vector2.zero);
                regionLabels[i].rectTransform.pivot = new Vector2(.5f, .5f);
                regionLabels[i].alignment = TextAnchor.MiddleCenter;
            }
            RefreshMapHover();
        }

        void UpdateMiniTile(Rect worldWindow)
        {
            WorldMapRegionTile next = null;
            if (data.HasIllustration && data.RegionTiles != null)
                foreach (var tile in data.RegionTiles)
                    if (tile != null && tile.Texture != null && tile.WorldUv.Contains(worldWindow.min) && tile.WorldUv.Contains(worldWindow.max))
                    { next = tile; break; }
            if (next != selectedTile)
            {
                selectedTile = next;
                miniMap.texture = next != null ? next.Texture : data.DisplayMap;
            }
            miniMap.uvRect = next == null ? worldWindow : new Rect(
                (worldWindow.x - next.WorldUv.x) / next.WorldUv.width,
                (worldWindow.y - next.WorldUv.y) / next.WorldUv.height,
                worldWindow.width / next.WorldUv.width, worldWindow.height / next.WorldUv.height);
            if(miniPaperMaterial!=null)
            {
                Rect tileUv=next!=null?next.WorldUv:new Rect(0,0,1,1);
                miniPaperMaterial.SetVector("_WorldUv",new Vector4(tileUv.x,tileUv.y,tileUv.width,tileUv.height));
                miniPaperMaterial.SetFloat("_HasDetail",next!=null&&(data.PaintedRelief||next.Texture.name.StartsWith("MiniTerrain_",StringComparison.Ordinal))?1:0);
            }
        }

        void PlaceMini(RectTransform marker, Vector2 world, bool visible)
        {
            Vector2 n = projection.WorldToNormalized(world);
            Vector2 v = new Vector2((n.x - miniUv.x) / miniUv.width, (n.y - miniUv.y) / miniUv.height);
            visible &= v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1;
            marker.gameObject.SetActive(visible); if (!visible) return;
            marker.anchorMin = marker.anchorMax = v; marker.anchoredPosition = Vector2.zero;
        }

        void PlaceFull(RectTransform marker, Vector2 world, bool visible)
        {
            Vector2 n = projection.WorldToNormalized(world);
            Vector2 v = new Vector2((n.x - fullUv.x) / fullUv.width, (n.y - fullUv.y) / fullUv.height);
            visible &= v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1;
            marker.gameObject.SetActive(visible); if (!visible) return;
            marker.anchorMin = marker.anchorMax = v; marker.anchoredPosition = Vector2.zero;
        }

        void PlaceFullLabel(Text label, Vector2 world, bool visible, Vector2 offset, bool forceLeft = false)
        {
            Vector2 n = projection.WorldToNormalized(world);
            Vector2 v = new Vector2((n.x - fullUv.x) / fullUv.width, (n.y - fullUv.y) / fullUv.height);
            visible &= targetExpanded && fold >= .995f && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1;
            label.gameObject.SetActive(visible); if (!visible) return;
            bool left = forceLeft || v.x > .76f;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = v; rect.pivot = new Vector2(left ? 1 : 0, .5f);
            rect.anchoredPosition = new Vector2(left ? -Mathf.Abs(offset.x) : Mathf.Abs(offset.x), offset.y);
            label.alignment = left ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
        }

        void BuildFog()
        {
            fogTexture = new Texture2D(discovery.Width, discovery.Height, TextureFormat.RGBA32, false, true)
            { name = "WorldMapDiscoveryFog", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            RefreshFog();
        }

        void RefreshFog()
        {
            if (fogTexture == null) return;
            var pixels = new Color32[discovery.Width * discovery.Height];
            for (int y = 0; y < discovery.Height; y++) for (int x = 0; x < discovery.Width; x++)
                pixels[y * discovery.Width + x] = discovery.IsPlayableCell(x, y) && !discovery.IsDiscovered(x, y)
                    ? (Color32)new Color(dependencies.Paper.r, dependencies.Paper.g, dependencies.Paper.b, 1f) : new Color32(0, 0, 0, 0);
            fogTexture.SetPixels32(pixels); fogTexture.Apply(false, false);
        }

        void CenterFullOn(Vector2 world)
        {
            Vector2 n = projection.WorldToNormalized(world);
            fullUv.x = n.x - fullUv.width * .5f; fullUv.y = n.y - fullUv.height * .5f;
            fullUv = WorldMapProjection.ClampUvRect(fullUv);
        }

        Vector3 CurrentWorld()
        {
            if (session.Walker != null && session.Walker.Seated)
            {
                if (vehicleSeat == null || !vehicleSeat.Occupied)
                    vehicleSeat = UnityEngine.Object.FindObjectsByType<Vehicle.WorldMacroPalanquinSeat>(FindObjectsSortMode.None)
                        .FirstOrDefault(s => s.Occupied && s.CombatWalker == session.Walker);
                if (vehicleSeat != null && vehicleSeat.Vehicle != null) return vehicleSeat.Vehicle.transform.position;
            }
            return session.Walker != null && session.Walker.Body != null ? session.Walker.Body.transform.position : session.Content.StartFeet;
        }

        string RegionLabel(Vector2 world)
        {
            if(data.Locations!=null)return data.Locations.RealmAt(new Vector3(world.x,0,world.y))?.Name??"강토";
            foreach (var region in sheet.Regions) if (WorldMapDiscoveryGrid.Contains(region.Polygon, world)) return region.Label;
            return "강토";
        }

        void ToggleLegend() => legend.SetActive(!legend.activeSelf);

        RectTransform CreateMarker(Transform parent, string name, Color color, float size, bool withLabel, string label)
        {
            RectTransform root = PlaytestUiView.Rect("Marker_" + name, parent, 0, 0, size, size);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            if(name=="Player"){var arrow=root.gameObject.AddComponent<WorldMapHeadingGraphic>();arrow.color=color;arrow.raycastTarget=false;}
            else PlaytestUiView.Image(root, color);
            root.localEulerAngles = new Vector3(0, 0, 45);
            if (withLabel)
            {
                Text text = PlaytestUiView.Text(root, "Label", label, Font, 14, dependencies.Ink, size + 6, -8, 150, 28, TextAnchor.MiddleLeft);
                text.rectTransform.localEulerAngles = new Vector3(0, 0, -45);
            }
            return root;
        }

        Text CreateFullLabel(string name, string value)
        {
            Text label = PlaytestUiView.Text(fullLabels, "Label_" + name, value, Font, 14, dependencies.Ink,
                0, 0, 156, 28, TextAnchor.MiddleLeft);
            label.color = new Color(.045f,.038f,.03f,1f);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(.5f, .5f);
            var outline = label.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(.97f,.94f,.85f,.88f); outline.effectDistance = new Vector2(1,-1);
            label.gameObject.SetActive(false);
            return label;
        }

        void Button(Transform parent, string name, string label, Vector2 position, Action action, float width = 78)
        {
            RectTransform rect = PlaytestUiView.Rect(name, parent, 0, 0, width, 38);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.pivot = new Vector2(.5f, 0); rect.anchoredPosition = position;
            Image image = PlaytestUiView.Image(rect, new Color(dependencies.Paper.r, dependencies.Paper.g, dependencies.Paper.b, .94f), null, true);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(() => action());
            if(PlaytestUiRoot.Instance!=null&&PlaytestUiRoot.Instance.Theme.SoundPalette!=null){var sound=rect.gameObject.AddComponent<CompactUiSound255>();sound.Theme=PlaytestUiRoot.Instance.Theme;}
            PlaytestUiView.Text(rect, "Label", label, Font, 16, dependencies.Ink, 0, 0, width, 38, TextAnchor.MiddleCenter);
        }

        Font Font => dependencies.Font != null ? dependencies.Font : ownedFont;
        void EnsureFont()
        {
            if (dependencies.Font != null) return;
            ownedFont = UnityEngine.Font.CreateDynamicFontFromOSFont(new[] { "Noto Sans CJK KR", "Malgun Gothic", "Arial" }, 18);
        }

        void OnDestroy()
        {
            CloseRequested = null;
            FoldRustle = null;
            if(paperMaterial != null) Destroy(paperMaterial);
            DisposeInteriorDiscovery();
            if(miniDiscoveryMaterial != null) Destroy(miniDiscoveryMaterial);
            if(miniPaperMaterial != null) Destroy(miniPaperMaterial);
            macroInk?.Dispose();
            paperInk?.Dispose();
            if (MiniRoot != null) Destroy(MiniRoot.gameObject);
            if (FullRoot != null) Destroy(FullRoot.gameObject);
            if (fogTexture != null) Destroy(fogTexture);
            if (ownedFont != null) Destroy(ownedFont);
            if (ownsRuntimeData && data != null)
            {
                if (data.BaseMap != null) Destroy(data.BaseMap);
                Destroy(data);
            }
        }
    }

}
