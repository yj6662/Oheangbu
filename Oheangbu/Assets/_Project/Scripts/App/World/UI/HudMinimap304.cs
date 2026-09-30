using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#306 먹 원상 미니맵 (SPEC-PLAYTEST-306 #1, D306 revising D304 "minimap -> bearing line"). "PersistentMinimap" (the
    /// retired #304 root's smoke-test name; WorldMapPresenter.MiniRoot points here once attached) sits BottomRight under
    /// HUD_Canvas with no own Canvas, so SetHud(false) hides it with the HUD (menus, map, pause, ending) and the loading / death
    /// canvases draw over it; it also fades out while the death veil runs and while Settings.ShowMinimap is off.
    /// Inside the ink enso (paper enso_rim behind) a disc mask holds the map: a RawImage on a second PaperMapSurface material
    /// (IMapMiniSource304.CreateMiniMaterial) sharing the sheet's _MapTex / _FogTex / _CaveDiscovery with its own _InkTex.
    /// Walked land only (CONST-RULES §3-4): unwalked cells are the bare sheet, no objective, no rivers / borders / routes. Caves
    /// close the window to MinimapSpec304.CaveMetres and the walked cave plan grows (the sheet's _CaveDiscovery mask).
    /// The window moves every frame through RawImage.uvRect (vertices only, so the Mask's stencil copy of the material stays
    /// valid); the ink is printed again only after ReprintMetres of travel, a range change or a new IMapMiniSource304.MiniRevision
    /// (walked land, shortcuts, zone). North up with the cinnabar arrow turning (the one accent); Settings.MinimapFollowView turns
    /// the map instead (the ink north tick then shows north). Marks: rest / place / coin / pin (IMapMarkerSource304), never the
    /// objective. No text.</summary>
    public sealed class HudMinimap304 : MonoBehaviour
    {
        sealed class Mark { public RectTransform Root; public Image Rim, Core; }

        UiStyle304SO s;
        HudTokens304 k;
        MinimapSpec304 m;
        CompactUiProfileSO icons;
        RectTransform root, mapRect, markRoot, arrow;
        CanvasGroup group;
        Canvas canvas;
        RawImage map;
        Material material;
        Object boundMap;
        IMapMiniSource304 source;
        IMapMarkerSource304 markers;
        WorldMapPresenter detail;
        readonly List<Mark> marks = new List<Mark>();
        readonly List<RectTransform> pins = new List<RectTransform>();
        readonly List<MapMarker304> collected = new List<MapMarker304>();
        Rect inkWindow, view;
        Vector3 printedAt;
        float printedRange = -1f, nextRefresh, turn = float.NaN, arrowTurn = float.NaN;
        int printedRevision = int.MinValue;
        bool showSetting = true, follow;

        public RectTransform Root => root;
        public RawImage Map => map;
        public Material Material => material;
        public float TargetAlpha { get; private set; }
        public CanvasGroup Group => group;
        public bool FollowView => follow;
        public bool Interior { get; private set; }
        public float RangeMetres { get; private set; }
        public Rect ViewWindow => view;
        public int Prints { get; private set; }
        public int VisibleMarkers { get; private set; }
        /// <summary>Objective entries the source offered and the minimap left off (AC-1e: none is ever drawn).</summary>
        public int ObjectivesLeftOff { get; private set; }
        float WindowPx => m.Diameter * Mathf.Clamp(m.Window, .2f, 1f);

        /// <summary>Builds "PersistentMinimap" (m.Diameter square, centred on m.Centre, anchored m.Anchor) under the HUD canvas.
        /// icons = old CompactUiProfileSO, only a pictogram fallback when the HUD sprites are not set up.</summary>
        public static HudMinimap304 Create(UiStyle304SO s, HudTokens304 k, Transform hudCanvas, MinimapSpec304 spec, CompactUiProfileSO icons)
        {
            s = s != null ? s : UiStyle304SO.Fallback; k = k != null ? k : HudTokens304.Load(); spec = spec ?? new MinimapSpec304();
            float d = spec.Diameter;
            var root = V.RectAnchored("PersistentMinimap", hudCanvas, spec.Centre.x - d * .5f, spec.Centre.y - d * .5f, d, d, spec.Anchor);
            var mini = root.gameObject.AddComponent<HudMinimap304>();
            mini.s = s; mini.k = k; mini.m = spec; mini.icons = icons; mini.root = root;
            mini.group = root.gameObject.AddComponent<CanvasGroup>();
            mini.group.blocksRaycasts = false; mini.group.interactable = false; mini.group.alpha = 0f;
            mini.Build();
            return mini;
        }

        void Build()
        {
            float w = WindowPx;
            // disc mask (stencil, the disc sprite's alpha); a style without the sprite falls back to a square RectMask2D
            var mask = Centred("MiniMask", root, w, w);
            if (s.Sprites.Disc != null) { V.Image(mask, Color.white, s.Sprites.Disc); mask.gameObject.AddComponent<Mask>().showMaskGraphic = false; }
            else mask.gameObject.AddComponent<RectMask2D>();
            mapRect = Centred("MiniRoot", mask, w, w);
            map = V.Raw(mapRect, null, s.Sheet);   // white _MainTex x the sheet colour: the window scrolls, the paper must not jump
            // north: stroke_short is horizontal, so it is laid length x thickness and turned 90° (vertical, 2 px in from the top)
            float len = m.NorthTick.y, th = m.NorthTick.x;
            V.Brush(s, mapRect, "NorthTick", StrokeClass304.Short, s.Ink, (w - len) * .5f, 2f + (len - th) * .5f, len, th, 1f, 90f);
            markRoot = Centred("MiniMarks", root, 0f, 0f);
            for (int i = 0; i < Mathf.Max(1, k.MaxMarkers); i++)
            {
                var r = Centred("Mark" + i, markRoot, m.MarkerSize, m.MarkerSize);
                var mk = new Mark { Root = r };
                mk.Rim = V.Image(V.Stretch("Rim", r), UiStyle304SO.A(s.Paper, m.MarkRimAlpha)); mk.Rim.preserveAspect = true;
                mk.Core = V.Image(V.Stretch("Core", r), s.Ink); mk.Core.preserveAspect = true;
                r.gameObject.SetActive(false);
                marks.Add(mk);
            }
            for (int i = 0; i < 3; i++) pins.Add(BuildPin(markRoot, i));
            if (s.Sprites.EnsoRim != null) V.Image(V.Stretch("EnsoRim", root), UiStyle304SO.A(s.Paper, k.PaperAlpha(null, m.RimAlpha)), s.Sprites.EnsoRim).preserveAspect = true;
            if (s.Sprites.Enso != null) V.Image(V.Stretch("EnsoRing", root), s.Ink, s.Sprites.Enso).preserveAspect = true;
            arrow = Centred("PlayerArrow", root, m.ArrowSize, m.ArrowSize);
            if (s.Sprites.Arrow != null)
            {
                var rim = V.Image(V.Stretch("Rim", arrow), s.Sheet, s.Sprites.Arrow); rim.preserveAspect = true;
                rim.rectTransform.localScale = new Vector3(1.12f, 1.12f, 1f);   // DESIGN §5 arrow "1.1배 Paper 사본"
                V.Image(V.Stretch("Icon", arrow), s.Cinnabar, s.Sprites.Arrow).preserveAspect = true;
            }
            else { var g = V.Stretch("Icon", arrow).gameObject.AddComponent<WorldMapHeadingGraphic>(); g.color = s.Cinnabar; g.raycastTarget = false; }
            canvas = root.GetComponentInParent<Canvas>();
        }

        static RectTransform Centred(string name, Transform parent, float w, float h)
        {
            var r = V.Rect(name, parent, 0f, 0f, w, h);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.anchoredPosition = Vector2.zero;
            return r;
        }

        RectTransform BuildPin(Transform parent, int i)
        {
            float size = m.PinSize, th = m.PinStroke, len = size * 1.25f;
            var r = Centred("Pin" + i, parent, size, size);
            for (int pass = 0; pass < 2; pass++)
                for (int d = 0; d < 2; d++)
                {
                    float t = pass == 0 ? th + 3f : th;
                    V.Brush(s, r, (pass == 0 ? "PinRim" : "PinStroke") + d, StrokeClass304.Short, pass == 0 ? s.Paper : s.Ink,
                        (size - len) * .5f, (size - t) * .5f, len, t, pass == 0 ? .9f : 1f, d == 0 ? 45f : -45f, d == 1);
                }
            r.gameObject.SetActive(false);
            return r;
        }

        // ------------------------------------------------------------------ binding (PlaytestUiRoot.Instance.Map, read-only)
        void Bind(WorldMapPresenter next)
        {
            if (next != null && ReferenceEquals(next, boundMap) && material != null) return;
            if (next == null && ReferenceEquals(boundMap, null)) return;
            Unbind();
            if (next == null) return;
            var created = ((IMapMiniSource304)next).CreateMiniMaterial();
            if (created == null) return;                                   // map not initialized yet: retry next frame
            boundMap = next; source = next; markers = next; detail = next; material = created;
            map.material = material;
            source.AttachMiniRoot(root);
            printedRange = -1f; printedRevision = int.MinValue; nextRefresh = 0f;
        }

        void Unbind()
        {
            if (!ReferenceEquals(boundMap, null) && boundMap != null && source != null) { try { source.DetachMiniRoot(root); } catch (System.Exception e) { Debug.LogException(e); } }
            boundMap = null; source = null; markers = null; detail = null;
            if (map != null) map.material = null;
            if (material != null) { Destroy(material); material = null; }
        }

        // ------------------------------------------------------------------ frame
        void LateUpdate()
        {
            var ui = PlaytestUiRoot.Instance;
            var presenter = ui != null ? ui.Map : null;
            if (!ReferenceEquals(boundMap, null) && boundMap == null) Unbind();   // destroyed with its scene
            Bind(presenter);                                           // attach (MiniRoot) even while the HUD is hidden
            if (canvas != null && !canvas.enabled) return;             // SetHud(false): menus, map, pause, ending
            float now = Time.unscaledTime;
            bool refresh = now >= nextRefresh;
            if (refresh)
            {
                nextRefresh = now + Mathf.Max(.05f, m.RefreshSeconds);
                var settings = ui != null && ui.Settings != null ? ui.Settings.Current : null;
                showSetting = settings == null || settings.ShowMinimap; follow = settings != null && settings.MinimapFollowView;
            }
            var session = ui != null ? ui.Session : null;
            bool dying = session != null && session.DeathPresentation != null && session.DeathPresentation.IsActive;
            bool ready = source != null && material != null && source.MiniReady;
            float target = showSetting && ready && !dying ? 1f : 0f;
            TargetAlpha = target;
            float fade = s.Motion.Sec(m.FadeMs, UiTween304.ReducedMotion);
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / Mathf.Max(.01f, fade));
            if (!ready || (group.alpha <= 0f && target <= 0f)) { VisibleMarkers = 0; return; }

            source.MiniPose(out Vector3 here, out float heading);
            Interior = source.MiniInterior;
            float range = Mathf.Max(1f, Interior ? m.CaveMetres : m.OutsideMetres);
            RangeMetres = range;
            Vector3 moved = here - printedAt; moved.y = 0f;
            int revision = source.MiniRevision;
            if (!Mathf.Approximately(range, printedRange) || revision != printedRevision || moved.sqrMagnitude >= m.ReprintMetres * m.ReprintMetres)
                Print(here, range, revision);
            view = source.WindowUv(here, range);
            var uv = new Rect((view.x - inkWindow.x) / inkWindow.width, (view.y - inkWindow.y) / inkWindow.height, view.width / inkWindow.width, view.height / inkWindow.height);
            if (map.uvRect != uv) map.uvRect = uv;
            float mapTurn = follow ? heading : 0f;
            if (!Mathf.Approximately(turn, mapTurn)) { turn = mapTurn; mapRect.localEulerAngles = new Vector3(0f, 0f, mapTurn); }
            float a = follow ? 0f : -heading;
            if (!Mathf.Approximately(arrowTurn, a)) { arrowTurn = a; arrow.localEulerAngles = new Vector3(0f, 0f, a); }
            if (refresh)
            {
                collected.Clear();
                if (markers != null) { try { markers.CollectMarkers(collected); } catch (System.Exception e) { Debug.LogException(e); collected.Clear(); } }
            }
            PlaceMarks(mapTurn);
        }

        void Print(Vector3 here, float range, int revision)
        {
            // the print covers the window plus a pad larger than the reprint distance, so the moving window never leaves it
            float pad = Mathf.Max(m.InkPadMetres, m.ReprintMetres + 1f);
            inkWindow = source.WindowUv(here, range + pad);
            source.PrintMini(inkWindow, WindowPx * (range + pad) / range);
            source.ApplyMini(material, inkWindow);
            // under the Mask the RawImage renders a stencil COPY of the material (StencilMaterial): set the copy as well
            var rendering = map.materialForRendering;
            if (rendering != null && rendering != material) source.ApplyMini(rendering, inkWindow);
            printedAt = here; printedRange = range; printedRevision = source.MiniRevision; Prints++;
        }

        void PlaceMarks(float mapTurn)
        {
            float half = WindowPx * .5f, limit = half - m.MarkerRimInset;
            float rad = mapTurn * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            int used = 0, pinUsed = 0, objectives = 0;
            foreach (var marker in collected)
            {
                if (marker.Kind == MapMarkerKind304.Objective) { objectives++; continue; }   // CONST-RULES §3-4: never the objective
                if (!source.TryWorldToWindow(marker.World, view, out Vector2 w01)) continue;
                Vector2 p = (w01 - new Vector2(.5f, .5f)) * WindowPx;
                if (p.sqrMagnitude > limit * limit) continue;
                p = new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
                if (marker.Kind == MapMarkerKind304.Pin)
                {
                    if (pinUsed >= pins.Count) continue;
                    var pin = pins[pinUsed++];
                    pin.anchoredPosition = p;
                    if (!pin.gameObject.activeSelf) pin.gameObject.SetActive(true);
                    continue;
                }
                if (used >= marks.Count) continue;
                var view304 = marks[used];
                if (!Dress(view304, marker)) continue;
                used++;
                view304.Root.anchoredPosition = p;
                if (!view304.Root.gameObject.activeSelf) view304.Root.gameObject.SetActive(true);
            }
            for (int i = used; i < marks.Count; i++) if (marks[i].Root.gameObject.activeSelf) marks[i].Root.gameObject.SetActive(false);
            for (int i = pinUsed; i < pins.Count; i++) if (pins[i].gameObject.activeSelf) pins[i].gameObject.SetActive(false);
            VisibleMarkers = used + pinUsed; ObjectivesLeftOff = objectives;
        }

        bool Dress(Mark view304, MapMarker304 marker)
        {
            // D13 pictograms as on the bearing line: the map's baked kind first, the label words for a plain place
            WorldMapMarkerKind kind = WorldMapMarkerKind.Place;
            bool detailed = false;
            if (marker.Kind == MapMarkerKind304.Place && detail != null) { try { detailed = detail.TryDetailKind304(marker, out kind); } catch { detailed = false; } }
            Sprite core, rim;
            if (detailed) k.Pictogram(kind, marker.Label, out core, out rim);
            else k.Pictogram(marker.Kind, marker.Label, out core, out rim);
            if (core == null) core = Fallback(marker, detailed ? kind : WorldMapMarkerKind.Place);
            if (core == null) return false;
            if (view304.Core.sprite != core) view304.Core.sprite = core;
            var rimSprite = rim != null ? rim : core;
            if (view304.Rim.sprite != rimSprite) view304.Rim.sprite = rimSprite;
            float grow = rim != null ? 1f : 1.12f;
            view304.Rim.rectTransform.localScale = new Vector3(grow, grow, 1f);
            return true;
        }

        Sprite Fallback(MapMarker304 marker, WorldMapMarkerKind kind)
        {
            switch (marker.Kind)
            {
                case MapMarkerKind304.Rest: return icons != null ? icons.Inn : null;
                case MapMarkerKind304.Coin: return s.Sprites.Disc;
                case MapMarkerKind304.Place:
                    if (icons == null) return null;
                    if (kind == WorldMapMarkerKind.Rest || kind == WorldMapMarkerKind.Checkpoint) return icons.Inn;
                    if (kind == WorldMapMarkerKind.Mountain) return icons.Mountain;
                    if (kind == WorldMapMarkerKind.Gate) return icons.Cave;
                    return k.PlaceFamily(marker.Label) == 2 ? icons.Mountain : icons.Cave;
                default: return null;
            }
        }

        void OnDestroy() { Unbind(); }
    }
}
