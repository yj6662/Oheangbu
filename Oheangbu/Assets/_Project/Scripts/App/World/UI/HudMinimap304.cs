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
    /// Inside the ink enso (paper enso_rim behind) the map disc is a RawImage on a second PaperMapSurface material
    /// (IMapMiniSource304.CreateMiniMaterial) sharing the sheet's _MapTex / _FogTex / _CaveDiscovery with its own _InkTex.
    /// #307 style A "먹 씻김 위 한지" (SPEC-MINIMAP-307, ApplyLook): the material runs the shader's _MINI_HUD variant, which draws
    /// and fades the disc itself (no stencil Mask, so no stepped edge at any resolution): unwalked land a flat ink wash, walked
    /// land hanji with a faint relief shade and ink roads, caves the walked passages only (floor hanji, wall ink).
    /// Walked land only (CONST-RULES §3-4): no terrain under the wash, no objective, no rivers / borders / routes. Caves close
    /// the window to MinimapSpec304.CaveMetres and the walked cave plan grows (the sheet's _CaveDiscovery mask).
    /// The window moves in the shader (_MapWindow, set only when it changes; RawImage.uvRect stays 0..1, so the HUD canvas mesh
    /// is not rebuilt every frame); the ink is printed again only after ReprintMetres of travel, a range change or a new
    /// IMapMiniSource304.MiniRevision (shortcuts, zone; fog reveals need no print, the shader reads the shared _FogTex).
    /// North up with the cinnabar arrow turning (the one accent); Settings.MinimapFollowView turns the map instead (the ink
    /// north tick on its hanji rim then shows north). Marks: rest / place / coin / pin (IMapMarkerSource304), never the
    /// objective. No text.
    /// #308 (SPEC-MAP-OVERHAUL-308, HudMinimap304.Map308.cs): when the bound map carries a notation bundle the paper runs
    /// _MAP308, roads / ridges / cliffs are brush strips in a nested Canvas (no ink print outdoors), marks come from the icon
    /// atlas and a lacquer frame with a nacre north piece surrounds the window. Without a bundle everything below is #307.</summary>
    public sealed partial class HudMinimap304 : MonoBehaviour
    {
        sealed class Mark { public RectTransform Root; public Image Rim, Core; public MapGlyph308Graphic Glyph; }

        UiStyle304SO s;
        HudTokens304 k;
        MinimapSpec304 m;
        CompactUiProfileSO icons;
        RectTransform root, mapRect, markRoot, arrow, northRoot;
        float northAt;   // tick centre distance from the middle when north is straight up
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
        Rect inkWindow, view, shaderWindow;
        Vector3 printedAt;
        float printedRange = -1f, nextRefresh, turn = float.NaN, arrowTurn = float.NaN;
        int printedRevision = int.MinValue;
        bool showSetting = true, follow, printedFollow;

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
        float Aspect => Mathf.Clamp(m.Aspect, .5f, 3f);
        float WindowH => WindowPx / Aspect;   // rectangle (user 2026-09-30): width WindowPx, height WindowH

        /// <summary>Builds "PersistentMinimap" (m.Diameter square, centred on m.Centre, anchored m.Anchor) under the HUD canvas.
        /// icons = old CompactUiProfileSO, only a pictogram fallback when the HUD sprites are not set up.</summary>
        public static HudMinimap304 Create(UiStyle304SO s, HudTokens304 k, Transform hudCanvas, MinimapSpec304 spec, CompactUiProfileSO icons)
        {
            s = s != null ? s : UiStyle304SO.Fallback; k = k != null ? k : HudTokens304.Load(); spec = spec ?? new MinimapSpec304();
            float d = spec.Diameter, dh = d / Mathf.Clamp(spec.Aspect, .5f, 3f);
            var root = V.RectAnchored("PersistentMinimap", hudCanvas, spec.Centre.x - d * .5f, spec.Centre.y - dh * .5f, d, dh, spec.Anchor);
            var mini = root.gameObject.AddComponent<HudMinimap304>();
            mini.s = s; mini.k = k; mini.m = spec; mini.icons = icons; mini.root = root;
            mini.group = root.gameObject.AddComponent<CanvasGroup>();
            mini.group.blocksRaycasts = false; mini.group.interactable = false; mini.group.alpha = 0f;
            mini.Build();
            return mini;
        }

        void Build()
        {
            float w = WindowPx, h = WindowH;
            // #307: MiniMask is a plain parent (harness name): no Mask / RectMask2D, the shader fades the disc (MINIMAP_DESIGN §1)
            var mask = Centred("MiniMask", root, w, h);
            mapRect = Centred("MiniRoot", mask, w, h);
            map = V.Raw(mapRect, null, Color.white);   // white vertices: the look is all in the _MINI_HUD material (ApplyLook)
            // north: stroke_short is horizontal, so it is laid length x thickness and turned 90° (vertical), centred in the ring's
            // gap at the middle of the disc fade (rho ~.81-.96); a hanji rim NorthTickRim px wider first, then the ink (as the pins)
            float len = m.NorthTick.y, th = m.NorthTick.x, rimPx = Mathf.Max(0f, m.NorthTickRim);
            float cy = h * .5f * (1f - (m.Fade.x + m.Fade.y) * .5f) + 3f;
            // the rectangle frame never turns: in follow mode the shader turns the ink and the tick slides along the edge
            northRoot = Centred("North", mask, w, h); northAt = h * .5f - cy;
            for (int pass = 0; pass < 2; pass++)
            {
                float l = pass == 0 ? len + rimPx : len, t = pass == 0 ? th + rimPx : th;
                V.Brush(s, northRoot, pass == 0 ? "NorthTickRim" : "NorthTick", StrokeClass304.Short, pass == 0 ? s.Paper : s.Ink,
                    (w - l) * .5f, cy - t * .5f, l, t, pass == 0 ? .9f : 1f, 90f);
            }
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
            // user 2026-09-30: no brush enso ring around the minimap; the hanji rectangle fades out at its own torn edge
            arrow = Centred("PlayerArrow", root, m.ArrowSize, m.ArrowSize);
            if (s.Sprites.Arrow != null)
            {
                var rim = V.Image(V.Stretch("Rim", arrow), s.Paper, s.Sprites.Arrow); rim.preserveAspect = true;
                rim.rectTransform.localScale = new Vector3(1.12f, 1.12f, 1f);   // DESIGN §5 arrow "1.1배 Paper 사본", §5.13 주사 촉(한지 테)
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
            ApplyLook(material);
            source.SetMiniRoadWidth(m.RoadPx);   // #307 road ink width; the minimap prints no distance ticks
            map.material = material;
            source.AttachMiniRoot(root);
            Bind308(next);   // #308: strips, frame, atlas marks when the map has a notation bundle
            printedRange = -1f; printedRevision = int.MinValue; nextRefresh = 0f; shaderWindow = default; turn = float.NaN;
        }

        static readonly int MapWindowId = Shader.PropertyToID("_MapWindow"), MiniTurnId = Shader.PropertyToID("_MiniTurn");

        /// <summary>#307 style A on the runtime material (MINIMAP_DESIGN §1-2): the _MINI_HUD variant, the ink / hanji tokens (no new
        /// colour) and the MinimapSpec304 look values. The hanji alpha goes through HudTokens304.PaperAlpha; the wash takes the
        /// linear-space ink gamma of the HUD's UI/InkReveal material (as every other HUD ink layer) and wash_tile's grain at
        /// GrainTilePx canvas px per repeat.</summary>
        void ApplyLook(Material mat)
        {
            if (mat == null) return;
            mat.EnableKeyword("_MINI_HUD");
            mat.SetColor("_MiniInk", s.Ink); mat.SetColor("_MiniPaper", s.Sheet);
            // user 2026-09-30: the whole disc is hanji — unwalked land Mist hanji at the paper alpha, walked land Sheet
            var veil = s.Mist; veil.a = k.PaperAlpha(null, m.PaperAlpha); mat.SetColor("_MiniVeil", veil);
            Texture grain = s.Sprites.WashTile;
            mat.SetTexture("_WashTex", grain != null ? grain : Texture2D.grayTexture);   // grey = neutral grain (alpha .5)
            var reveal = s.Materials != null ? s.Materials.InkReveal : null;
            float gamma = reveal != null ? HudTokens304.ShaderInkGamma(reveal) : 1.8f;
            mat.SetVector("_MiniWash", new Vector4(Mathf.Clamp01(m.WashAlpha), gamma, WindowPx / Mathf.Max(1f, m.GrainTilePx), Mathf.Clamp01(m.WashGrain)));
            mat.SetVector("_MiniLand", new Vector4(k.PaperAlpha(null, m.PaperAlpha), Mathf.Clamp01(m.EdgeInk), Mathf.Max(0f, m.ReliefGain), Mathf.Clamp01(m.ReliefInk)));
            float fadeIn = Mathf.Clamp01(m.Fade.x);
            mat.SetVector("_MiniDisc", new Vector4(fadeIn, Mathf.Max(fadeIn + .001f, m.Fade.y), Mathf.Max(0f, m.EdgeWobble), Aspect));
            var bands = m.CaveBands;
            bands.y = Mathf.Max(bands.x + .001f, bands.y); bands.z = Mathf.Max(bands.y + .001f, bands.z); bands.w = Mathf.Clamp01(bands.w);
            mat.SetVector("_MiniCave", bands);
            mat.SetVector(MapWindowId, new Vector4(0f, 0f, 1f, 1f));
        }

        void Unbind()
        {
            Unbind308();
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
            Frame(refresh);
        }

        /// <summary>One frame of the bound, visible minimap: window, strips, turn, marks (also HudMinimap304.Map308.cs PreviewFrame308).</summary>
        void Frame(bool refresh)
        {
            source.MiniPose(out Vector3 here, out float heading);
            if (!float.IsNaN(previewHeading308)) heading = previewHeading308;   // #308 Edit-mode preview only
            Interior = source.MiniInterior;
            float range = Mathf.Max(1f, Interior ? m.CaveMetres : m.OutsideMetres);
            RangeMetres = range;
            int revision = source.MiniRevision;
            Band308(range);                                     // #308: the paper scale values for this range (no-op without a bundle)
            if (Outdoor308) Window308(here, range, revision);   // #308: no ink print outdoors, the window is one material vector
            else
            {
                LeaveWindow308();
                Vector3 moved = here - printedAt; moved.y = 0f;
                if (!Mathf.Approximately(range, printedRange) || revision != printedRevision || follow != printedFollow || moved.sqrMagnitude >= m.ReprintMetres * m.ReprintMetres)
                    Print(here, range, revision);
                view = Widen(source.WindowUv(here, range));
                var uv = new Rect((view.x - inkWindow.x) / inkWindow.width, (view.y - inkWindow.y) / inkWindow.height, view.width / inkWindow.width, view.height / inkWindow.height);
                // #307: the window moves in the shader (print uv = disc uv x size + min, what uvRect did), set only when it changes;
                // uvRect stays (0,0,1,1) so the disc fade stays round and the HUD canvas mesh is not dirtied every frame
                if (uv != shaderWindow && uv.width > 1e-6f && uv.height > 1e-6f)
                {
                    shaderWindow = uv;
                    material.SetVector(MapWindowId, new Vector4(-uv.x / uv.width, -uv.y / uv.height, 1f / uv.width, 1f / uv.height));
                }
            }
            SyncStrips308(here, range);
            float mapTurn = follow ? heading : 0f;
            if (!Mathf.Approximately(turn, mapTurn)) { turn = mapTurn; Turn(mapTurn); }
            float a = follow ? 0f : -heading;
            if (!Mathf.Approximately(arrowTurn, a)) { arrowTurn = a; arrow.localEulerAngles = new Vector3(0f, 0f, a); }
            if (refresh)
            {
                collected.Clear();
                if (markers != null) { try { markers.CollectMarkers(collected); } catch (System.Exception e) { Debug.LogException(e); collected.Clear(); } }
            }
            PlaceMarks(mapTurn);
        }

        void Turn(float deg)
        {
            float rad = deg * Mathf.Deg2Rad, c = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            material.SetVector(MiniTurnId, new Vector4(c, sn, 0f, 0f));
            // north direction on screen, then the tick at the same inset from the frame edge as when it is at the top
            var d = new Vector2(-sn, c);
            float ex = WindowPx * .5f - (WindowH * .5f - northAt), ey = northAt;
            float e = Mathf.Min(Mathf.Abs(d.x) > 1e-4f ? ex / Mathf.Abs(d.x) : float.MaxValue, Mathf.Abs(d.y) > 1e-4f ? ey / Mathf.Abs(d.y) : float.MaxValue);
            northRoot.localEulerAngles = new Vector3(0f, 0f, deg);
            northRoot.anchoredPosition = d * (e - northAt);
            Turn308(deg);
        }

        // the square WindowUv widened about its centre to the rectangle (uv is linear in world metres on the sheet)
        Rect Widen(Rect r) { float cx = r.center.x, w = r.width * Aspect; return new Rect(cx - w * .5f, r.y, w, r.height); }

        void Print(Vector3 here, float range, int revision)
        {
            // the print covers the window plus a pad larger than the reprint distance, so the moving window never leaves it
            float pad = Mathf.Max(m.InkPadMetres, m.ReprintMetres + 1f);
            // square print covering the wide window (half extent range x Aspect across)
            float across = range * Aspect * (follow ? Mathf.Sqrt(1f + 1f / (Aspect * Aspect)) : 1f);   // turning: cover the corners
            inkWindow = source.WindowUv(here, across + pad);
            source.PrintMini(inkWindow, WindowPx * (across + pad) / (range * Aspect));   // same px per metre as the window
            source.ApplyMini(material, inkWindow);   // #307: no Mask, so no stencil copy of the material to keep in step
            printedAt = here; printedRange = range; printedFollow = follow; printedRevision = source.MiniRevision; Prints++;
        }

        void PlaceMarks(float mapTurn)
        {
            float limitX = WindowPx * .5f - MarkInset308, limitY = WindowH * .5f - MarkInset308;   // #308: the notation's clear margin
            float rad = mapTurn * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            // turned, the frame corners see past the unturned window: look markers up in a window grown to the diagonal
            float grow = Mathf.Approximately(mapTurn, 0f) ? 1f : Mathf.Sqrt(1f + Aspect * Aspect);
            var markView = grow == 1f ? view : new Rect(view.center.x - view.width * grow * .5f, view.center.y - view.height * grow * .5f, view.width * grow, view.height * grow);
            int used = 0, pinUsed = 0, objectives = 0;
            foreach (var marker in collected)
            {
                if (marker.Kind == MapMarkerKind304.Objective) { objectives++; continue; }   // CONST-RULES §3-4: never the objective
                if (!source.TryWorldToWindow(marker.World, markView, out Vector2 w01)) continue;
                Vector2 p = new Vector2((w01.x - .5f) * WindowPx * grow, (w01.y - .5f) * WindowH * grow);
                p = new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
                if (Mathf.Abs(p.x) > limitX || Mathf.Abs(p.y) > limitY) continue;
                if (marker.Kind == MapMarkerKind304.Pin && pinCell308 < 0)   // #308: with a pin glyph the pin is a mark like the others
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
            if (Dress308(view304, marker)) return true;   // #308: the atlas glyph by marker id (also resets the mark for the old path)
            if (marker.Kind == MapMarkerKind304.Pin) return false;
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
