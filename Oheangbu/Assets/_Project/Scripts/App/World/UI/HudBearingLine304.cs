using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 방위 먹선 (DESIGN §5.14 / §7.1; since #306 beside HudMinimap304, not instead of it). A thin hair stroke (632,70) 656x22 anchored TopCentre,
    /// ±90° spread over 640 px around x960, ticks every 15° (8 px) / 45° (14 px), Korean compass words above the line
    /// (북 동 남 서 Serif900 24, 북동 ... Serif700 20), the "you" mark, and markers BELOW the line: D13 flat pictograms (쉼터 /
    /// 장소 / 남긴 통보, 32 px), the D35 pin X and the cinnabar objective dab with its label inside ±5°.
    /// Polarity (§2.4): Normal = ink core + paper rim; Inverse (indoor zone from IMapMarkerSource304.IsIndoor, or the top 12 % of
    /// the frame darker than .22 / back above .28, sampled every .5 s at 1/16) = paper core + ink rim.
    /// Visibility (#306: always on, switched on / off by Settings.ShowBearingLine; ticks, words and discovered marks show without an
    /// objective, the objective dab only when one exists): α1 for 4 s after an arrival card or a respawn and while standing, α.5 while moving. Lives under HUD_Canvas (no own Canvas), so SetHud(false)
    /// hides it with the rest of the HUD. Markers come read-only from PlaytestUiRoot.Instance.Map as IMapMarkerSource304 (null ok).</summary>
    public sealed class HudBearingLine304 : MonoBehaviour
    {
        static readonly string[] Words = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };

        sealed class Label304 { public TMP_Text Text; public Material Normal, Inverse; }
        sealed class Mark { public RectTransform Root; public Image Rim, Core; public Vector3 World; public MapMarkerKind304 Kind; public bool Used; }

        UiStyle304SO s;
        HudTokens304 k;
        CompactUiProfileSO icons;
        RectTransform frame;
        CanvasGroup group;
        Canvas canvas;
        Image hairRim, hair, youRim, you, objectiveDab;
        readonly List<Image> tickRims = new List<Image>(), ticks = new List<Image>();
        readonly Label304[] words = new Label304[8];
        Label304 objectiveLabel;
        readonly List<Mark> marks = new List<Mark>();
        readonly List<RectTransform> pins = new List<RectTransform>();
        readonly List<Image> pinRims = new List<Image>(), pinCores = new List<Image>();
        readonly List<MapMarker304> collected = new List<MapMarker304>();
        readonly List<MapMarker304> objectives = new List<MapMarker304>();
        bool inverse, lumaInverse, polarityBuilt, hasObjective;
        float nextCollect, emphasisUntil = -1000f, lastLuma = 1f;
        int lastDeaths = -1, lastArrivals = -1;
        float nextArrivalLookup, builtScale = -1f;
        WorldLocationArrival arrival;
        WorldMapPresenter mapDetail;   // #304 integration: D13 sub-kind of a place (WorldMapPresenter.TryDetailKind304)
        CancellationTokenSource sampling;

        public bool Inverse => inverse;
        public float LastLuma => lastLuma;
        public bool HasObjective => hasObjective;
        public float TargetAlpha { get; private set; }
        public CanvasGroup Group => group;
        public int VisibleMarkers { get; private set; }

        /// <summary>Builds "BearingLine304" (1920x1080 TopCentre frame) under the HUD canvas. icons = old CompactUiProfileSO, only
        /// used as pictogram fallback when the HUD sprites are not set up.</summary>
        public static HudBearingLine304 Create(UiStyle304SO s, HudTokens304 k, Transform hudCanvas, CompactUiProfileSO icons)
        {
            s = s != null ? s : UiStyle304SO.Fallback; k = k != null ? k : HudTokens304.Load();
            var b = s.Bearing;
            var frame = V.RectAnchored("BearingLine304", hudCanvas, 0f, 0f, UiPageFit304.Width, UiPageFit304.Height, b.Anchor);
            var line = frame.gameObject.AddComponent<HudBearingLine304>();
            line.s = s; line.k = k; line.icons = icons; line.frame = frame;
            line.group = frame.gameObject.AddComponent<CanvasGroup>();
            line.group.blocksRaycasts = false; line.group.interactable = false; line.group.alpha = 0f;
            line.Build();
            line.StartSampling();
            return line;
        }

        void Build()
        {
            var b = s.Bearing;
            hairRim = V.Brush(s, frame, "HairRim", StrokeClass304.HairRim, s.Paper, b.Line.x, b.Line.y, b.Line.width, b.Line.height, b.RimAlpha);
            hair = V.Brush(s, frame, "Hair", StrokeClass304.Hair, s.Ink, b.Line.x, b.Line.y, b.Line.width, b.Line.height);
            if (hair.sprite == null)
            {   // style without sprites (setup not run): a plain 2 px line instead of a solid 22 px bar
                var hr = hair.rectTransform; hr.sizeDelta = new Vector2(b.Line.width, 2f);
                var rr = hairRim.rectTransform; rr.sizeDelta = new Vector2(b.Line.width + 2f, 5f);
            }
            int tickCount = Mathf.FloorToInt(2f * b.HalfRangeDeg / Mathf.Max(1f, b.MinorTickDeg)) + 2;
            var tickRoot = V.Rect("Ticks", frame, 0f, 0f, 0f, 0f);
            for (int i = 0; i < tickCount; i++) { tickRims.Add(V.Image(V.Rect("TickRim" + i, tickRoot, 0, 0, 1, 1), s.Paper)); }
            for (int i = 0; i < tickCount; i++) { ticks.Add(V.Image(V.Rect("Tick" + i, tickRoot, 0, 0, 1, 1), s.Ink)); }
            youRim = V.Image(V.Rect("YouRim", frame, b.You.x - 2f, b.You.y - 2f, b.You.width + 4f, b.You.height + 4f), UiStyle304SO.A(s.Paper, .9f));
            you = V.Image(V.Rect("You", frame, b.You.x, b.You.y, b.You.width, b.You.height), s.Ink);
            var wordRoot = V.Rect("Words", frame, 0f, 0f, 0f, 0f);
            for (int i = 0; i < 8; i++)
            {
                bool cardinal = i % 2 == 0;
                var role = cardinal ? UiType304.BearingMajor24 : UiType304.BearingMinor20;
                float top = cardinal ? b.CardinalTop : b.IntercardinalTop;
                float h = s.Role(role).Size;
                var t = V.Label(s, wordRoot, "BearingWord_" + Words[i], Words[i], role, s.Ink, 0f, top, b.LabelWidth, h, TextAlignmentOptions.Bottom);
                words[i] = MakeLabel(t);
            }
            var markRoot = V.Rect("Markers", frame, 0f, 0f, 0f, 0f);
            for (int i = 0; i < Mathf.Max(1, k.MaxMarkers); i++)
            {
                var root = V.Rect("Marker" + i, markRoot, 0f, b.MarkerTop, b.MarkerSize, b.MarkerSize);
                var m = new Mark { Root = root };
                m.Rim = V.Image(V.Stretch("Rim", root), s.Paper); m.Rim.preserveAspect = true; m.Rim.rectTransform.pivot = new Vector2(.5f, .5f);
                m.Core = V.Image(V.Stretch("Core", root), s.Ink); m.Core.preserveAspect = true;
                root.gameObject.SetActive(false);
                marks.Add(m);
            }
            for (int i = 0; i < 3; i++) pins.Add(BuildPin(markRoot, i));
            objectiveDab = V.SpriteImage(frame, "ObjectiveDab", s.Sprites.Dab, s.Cinnabar, 0f, b.ObjectiveDabTop, b.ObjectiveDabSize.x, b.ObjectiveDabSize.y, b.ObjectiveDabRot);
            objectiveDab.gameObject.SetActive(false);
            var ol = V.Label(s, frame, "BearingObjectiveLabel", "", UiType304.BearingLabel20, s.Ink, 0f, b.ObjectiveLabelTop, b.ObjectiveLabelWidth, 0f, TextAlignmentOptions.Top);
            ol.rectTransform.sizeDelta = new Vector2(b.ObjectiveLabelWidth, Mathf.Ceil(s.Role(UiType304.BearingLabel20).Size * 1.2f));
            objectiveLabel = MakeLabel(ol);
            ol.gameObject.SetActive(false);
            canvas = frame.GetComponentInParent<Canvas>();
            builtScale = UiText304.TextScale;
            ApplyPolarity(false, true);
        }

        Label304 MakeLabel(TMP_Text t)
        {
            var normal = t.fontSharedMaterial;
            var inv = s.TmpMaterial(t.font, TmpPreset304.Paper_UnderInk);
            return new Label304 { Text = t, Normal = normal, Inverse = inv != null ? inv : (t.font != null ? t.font.material : normal) };
        }

        RectTransform BuildPin(Transform parent, int i)
        {
            float size = k.PinSize, th = k.PinStroke;
            var root = V.Rect("Pin" + i, parent, 0f, s.Bearing.MarkerTop + (s.Bearing.MarkerSize - size) * .5f, size, size);
            float len = size * 1.25f;
            for (int pass = 0; pass < 2; pass++)
                for (int d = 0; d < 2; d++)
                {
                    float t = pass == 0 ? th + 3f : th;
                    var img = V.Brush(s, root, (pass == 0 ? "PinRim" : "PinStroke") + d, StrokeClass304.Short, pass == 0 ? s.Paper : s.Ink,
                        (size - len) * .5f, (size - t) * .5f, len, t, pass == 0 ? .9f : 1f, d == 0 ? 45f : -45f, d == 1);
                    (pass == 0 ? pinRims : pinCores).Add(img);
                }
            root.gameObject.SetActive(false);
            return root;
        }

        // AddComponent runs OnEnable before Create has assigned the style: sampling starts from Create, then on every re-enable.
        void OnEnable() { if (s != null) StartSampling(); }

        void StartSampling()
        {
            sampling?.Cancel(); sampling?.Dispose();
            sampling = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            SampleLoop(sampling.Token).Forget();
        }

        void OnDisable() { sampling?.Cancel(); sampling?.Dispose(); sampling = null; }

        // ------------------------------------------------------------------ frame
        void LateUpdate()
        {
            if (canvas != null && !canvas.enabled) return;             // SetHud(false): menus, loading, ending
            var root = PlaytestUiRoot.Instance;
            var session = root != null ? root.Session : null;
            bool enabledBySetting = root != null && root.Settings != null && root.Settings.Current.ShowBearingLine;
            var source = root != null && root.Map != null ? (object)root.Map as IMapMarkerSource304 : null;
            mapDetail = root != null ? root.Map : null;
            float now = Time.unscaledTime;

            if (now >= nextCollect)
            {
                nextCollect = now + Mathf.Max(.05f, k.MarkerRefreshSeconds);
                collected.Clear();
                if (source != null) { try { source.CollectMarkers(collected); } catch (System.Exception e) { Debug.LogException(e); collected.Clear(); } }
                objectives.Clear();
                foreach (var m in collected) if (m.Kind == MapMarkerKind304.Objective) objectives.Add(m);
                hasObjective = objectives.Count > 0;
            }
            TrackEmphasis(session, now);

            bool moving = false;
            var walker = session != null ? session.Walker : null;
            if (walker != null && walker.Body != null)
            {
                var v = walker.Body.velocity; v.y = 0f;
                moving = v.magnitude > k.MovingSpeed;
            }
            // #306 AC-1c: out under the death veil too (with the minimap), not only behind it
            bool dying = session != null && session.DeathPresentation != null && session.DeathPresentation.IsActive;
            float target = !enabledBySetting || dying ? 0f : now < emphasisUntil ? 1f : moving ? k.MovingAlpha : 1f;
            TargetAlpha = target;
            float fade = s.Motion.Sec(k.FadeMs, UiTween304.ReducedMotion);
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / Mathf.Max(.01f, fade));
            if (group.alpha <= 0f && target <= 0f) { VisibleMarkers = 0; return; }
            if (!Mathf.Approximately(builtScale, UiText304.TextScale))
            {   // 본문 크기 changed: words are bottom-aligned in their rects, so bigger text grows up, away from the line
                builtScale = UiText304.TextScale; UiText304.ApplyTextScale(frame, builtScale);
                if (objectiveLabel.Text != null) objectiveLabel.Text.text = "";
            }

            var cam = walker != null && walker.ViewCamera != null ? walker.ViewCamera : Camera.main;
            if (cam == null) return;
            bool indoor = source != null && SafeIndoor(source);
            ApplyPolarity(indoor || lumaInverse, false);
            Vector3 eye = cam.transform.position;
            Vector3 f = cam.transform.forward; if (new Vector2(f.x, f.z).sqrMagnitude < 1e-4f) f = cam.transform.up;
            float heading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            LayoutTicks(heading);
            LayoutWords(heading);
            LayoutMarkers(heading, eye);
        }

        static bool SafeIndoor(IMapMarkerSource304 source) { try { return source.IsIndoor; } catch { return false; } }

        void TrackEmphasis(WorldMacroPlaytestSession session, float now)
        {
            var death = session != null ? session.DeathPresentation : null;
            int deaths = death != null ? death.Completed : 0;
            if (lastDeaths >= 0 && deaths != lastDeaths) emphasisUntil = now + k.EmphasisSeconds;
            lastDeaths = deaths;
            if (arrival == null && now >= nextArrivalLookup) { nextArrivalLookup = now + 2f; arrival = FindFirstObjectByType<WorldLocationArrival>(); }
            int arrivals = arrival != null ? arrival.AnnouncedCount : 0;
            if (lastArrivals >= 0 && arrivals != lastArrivals) emphasisUntil = now + k.EmphasisSeconds;
            lastArrivals = arrivals;
        }

        float X(float delta) => s.Bearing.CenterX + delta * s.Bearing.PxPerDegree;

        void LayoutTicks(float heading)
        {
            var b = s.Bearing;
            float step = Mathf.Max(1f, b.MinorTickDeg);
            float first = Mathf.Ceil((heading - b.HalfRangeDeg) / step) * step;
            int i = 0;
            for (float a = first; a <= heading + b.HalfRangeDeg + .001f && i < ticks.Count; a += step, i++)
            {
                float delta = a - heading;
                bool major = Mathf.Abs(Mathf.Repeat(a, b.MajorTickDeg)) < .01f || Mathf.Abs(Mathf.Repeat(a, b.MajorTickDeg) - b.MajorTickDeg) < .01f;
                float h = major ? b.MajorTickPx : b.MinorTickPx, w = b.TickWidth, x = X(delta);
                Put(ticks[i].rectTransform, x - w * .5f, b.TickCenterY - h * .5f, w, h);
                Put(tickRims[i].rectTransform, x - w * .5f - 1.5f, b.TickCenterY - h * .5f - 1.5f, w + 3f, h + 3f);
                ticks[i].enabled = tickRims[i].enabled = true;
            }
            for (; i < ticks.Count; i++) ticks[i].enabled = tickRims[i].enabled = false;
        }

        void LayoutWords(float heading)
        {
            var b = s.Bearing;
            for (int i = 0; i < 8; i++)
            {
                float delta = Mathf.DeltaAngle(heading, i * 45f);
                var t = words[i].Text;
                bool on = Mathf.Abs(delta) <= b.HalfRangeDeg;
                if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
                if (!on) continue;
                var r = t.rectTransform;
                r.anchoredPosition = new Vector2(X(delta) - b.LabelWidth * .5f, r.anchoredPosition.y);
                var c = t.color; c.a = EdgeFade(delta); t.color = c;
            }
        }

        float EdgeFade(float delta) => Mathf.Clamp01((s.Bearing.HalfRangeDeg - Mathf.Abs(delta)) / Mathf.Max(1f, k.LabelEdgeFadeDeg));

        void LayoutMarkers(float heading, Vector3 eye)
        {
            var b = s.Bearing;
            foreach (var m in marks) m.Used = false;
            int used = 0, pinUsed = 0;
            float range2 = k.PlaceRangeMeters * k.PlaceRangeMeters;
            foreach (var marker in collected)
            {
                if (marker.Kind == MapMarkerKind304.Objective) continue;
                Vector3 d = marker.World - eye; d.y = 0f;
                if ((marker.Kind == MapMarkerKind304.Rest || marker.Kind == MapMarkerKind304.Place) && d.sqrMagnitude > range2) continue;
                if (d.sqrMagnitude < .25f) continue;
                float delta = Mathf.DeltaAngle(heading, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                if (Mathf.Abs(delta) > b.HalfRangeDeg) continue;
                float x = X(delta);
                if (marker.Kind == MapMarkerKind304.Pin)
                {
                    if (pinUsed >= pins.Count) continue;
                    var pin = pins[pinUsed++];
                    pin.anchoredPosition = new Vector2(x - k.PinSize * .5f, pin.anchoredPosition.y);
                    if (!pin.gameObject.activeSelf) pin.gameObject.SetActive(true);
                    continue;
                }
                if (used >= marks.Count) continue;
                var view = marks[used++];
                if (!Dress(view, marker)) { used--; continue; }
                view.Used = true;
                view.Root.anchoredPosition = new Vector2(x - b.MarkerSize * .5f, view.Root.anchoredPosition.y);
                if (!view.Root.gameObject.activeSelf) view.Root.gameObject.SetActive(true);
            }
            for (int i = used; i < marks.Count; i++) if (marks[i].Root.gameObject.activeSelf) marks[i].Root.gameObject.SetActive(false);
            for (int i = pinUsed; i < pins.Count; i++) if (pins[i].gameObject.activeSelf) pins[i].gameObject.SetActive(false);
            VisibleMarkers = used + pinUsed;

            // ---- objective: cinnabar dab under the line (clamped to the ends when behind), label inside ±5°
            bool dabOn = false, labelOn = false;
            float best = float.MaxValue; MapMarker304 pick = default;
            foreach (var o in objectives)
            {
                Vector3 d = o.World - eye; d.y = 0f;
                float delta = Mathf.DeltaAngle(heading, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                if (Mathf.Abs(delta) < best) { best = Mathf.Abs(delta); pick = o; }
            }
            if (objectives.Count > 0)
            {
                Vector3 d = pick.World - eye; d.y = 0f;
                float delta = Mathf.DeltaAngle(heading, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                float clamped = Mathf.Clamp(delta, -b.HalfRangeDeg, b.HalfRangeDeg);
                float x = X(clamped);
                var r = objectiveDab.rectTransform;
                r.anchoredPosition = new Vector2(x, r.anchoredPosition.y);
                var c = objectiveDab.color; c.a = Mathf.Abs(delta) > b.HalfRangeDeg ? .6f : 1f; objectiveDab.color = c;
                dabOn = true;
                if (Mathf.Abs(delta) <= b.ObjectiveLabelDeg && !string.IsNullOrEmpty(pick.Label))
                {
                    var t = objectiveLabel.Text;
                    if (t.text != pick.Label)
                    {
                        t.text = pick.Label;
                        float w = Mathf.Max(b.ObjectiveLabelWidth, Mathf.Ceil(t.GetPreferredValues(pick.Label).x) + 8f);
                        t.rectTransform.sizeDelta = new Vector2(w, t.rectTransform.sizeDelta.y);
                    }
                    var lr = t.rectTransform;
                    lr.anchoredPosition = new Vector2(x - lr.sizeDelta.x * .5f, lr.anchoredPosition.y);
                    labelOn = true;
                }
            }
            if (objectiveDab.gameObject.activeSelf != dabOn) objectiveDab.gameObject.SetActive(dabOn);
            if (objectiveLabel.Text.gameObject.activeSelf != labelOn) objectiveLabel.Text.gameObject.SetActive(labelOn);
        }

        bool Dress(Mark view, MapMarker304 marker)
        {
            // D13: the map's baked kind picks the place pictogram (mountain / gate / rest); only a plain place falls back to the label words
            WorldMapMarkerKind detail = WorldMapMarkerKind.Place;
            bool detailed = marker.Kind == MapMarkerKind304.Place && TryDetail(marker, out detail);
            Sprite core, rim;
            if (detailed) k.Pictogram(detail, marker.Label, out core, out rim);
            else k.Pictogram(marker.Kind, marker.Label, out core, out rim);
            if (core == null) core = FallbackIcon(marker, detailed ? detail : WorldMapMarkerKind.Place);
            if (core == null) return false;
            view.Kind = marker.Kind;
            if (view.Core.sprite != core) view.Core.sprite = core;
            // no dedicated rim: the same silhouette drawn 1.12x behind (DESIGN §5 arrow "1.1배 Paper 사본")
            var rimSprite = rim != null ? rim : core;
            if (view.Rim.sprite != rimSprite) view.Rim.sprite = rimSprite;
            float grow = rim != null ? 1f : 1.12f;
            view.Rim.rectTransform.localScale = new Vector3(grow, grow, 1f);
            return true;
        }

        bool TryDetail(MapMarker304 marker, out WorldMapMarkerKind kind)
        {
            kind = WorldMapMarkerKind.Place;
            if (mapDetail == null) return false;
            try { return mapDetail.TryDetailKind304(marker, out kind); } catch { kind = WorldMapMarkerKind.Place; return false; }
        }

        Sprite FallbackIcon(MapMarker304 marker, WorldMapMarkerKind detail)
        {
            switch (marker.Kind)
            {
                case MapMarkerKind304.Rest: return icons != null ? icons.Inn : null;
                case MapMarkerKind304.Coin: return s.Sprites.Disc;
                case MapMarkerKind304.Place:
                    if (icons == null) return null;
                    if (detail == WorldMapMarkerKind.Rest || detail == WorldMapMarkerKind.Checkpoint) return icons.Inn;
                    if (detail == WorldMapMarkerKind.Mountain) return icons.Mountain;
                    if (detail == WorldMapMarkerKind.Gate) return icons.Cave;
                    return k.PlaceFamily(marker.Label) == 2 ? icons.Mountain : icons.Cave;
                default: return null;
            }
        }

        static void Put(RectTransform r, float x, float y, float w, float h)
        {
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        // ------------------------------------------------------------------ polarity
        void ApplyPolarity(bool inv, bool force)
        {
            if (!force && polarityBuilt && inv == inverse) return;
            polarityBuilt = true; inverse = inv;
            Color core = inv ? s.Paper : s.Ink, rim = inv ? s.Ink : s.Paper;
            // QA2: paper halos drawn with V.Brush sit on UI/InkReveal, whose light remap (a^2.2, exact over ink) thins them over the
            // sky; HudTokens304.PaperAlpha pre-scales them like the meter rims. Ink rims (Inverse) keep the plain alpha.
            float RimA(Graphic g, float a) => inv ? a : k.PaperAlpha(g.material, a);
            hairRim.color = UiStyle304SO.A(rim, RimA(hairRim, s.Bearing.RimAlpha));
            hair.color = core;
            foreach (var t in ticks) t.color = core;
            foreach (var t in tickRims) t.color = UiStyle304SO.A(rim, .85f);
            you.color = core; youRim.color = UiStyle304SO.A(rim, .9f);
            foreach (var m in marks) { m.Core.color = core; m.Rim.color = UiStyle304SO.A(rim, .95f); }
            foreach (var p in pinCores) p.color = core;
            foreach (var p in pinRims) p.color = UiStyle304SO.A(rim, RimA(p, .9f));
            foreach (var w in words) Recolour(w, core);
            Recolour(objectiveLabel, core);
        }

        void Recolour(Label304 l, Color core)
        {
            if (l == null || l.Text == null) return;
            var m = inverse ? l.Inverse : l.Normal;
            if (m != null && l.Text.fontSharedMaterial != m) l.Text.fontSharedMaterial = m;
            float a = l.Text.color.a; core.a = a; l.Text.color = core;
        }

        // ------------------------------------------------------------------ luminance of the top band (GPU readback, 2 Hz)
        async UniTaskVoid SampleLoop(CancellationToken ct)
        {
            if (!SystemInfo.supportsAsyncGPUReadback) return;
            while (!ct.IsCancellationRequested)
            {
                if (await UiTween304.Delay(Mathf.Max(.1f, s.Bearing.SampleInterval), ct)) return;
                if (group == null || (group.alpha <= 0f && TargetAlpha <= 0f)) continue;
                if (canvas == null) canvas = frame.GetComponentInParent<Canvas>();
                if (canvas != null && !canvas.isActiveAndEnabled) continue;
                if (await UniTask.WaitForEndOfFrame(ct).SuppressCancellationThrow()) return;
                float? luma = await SampleTopBand(ct);
                if (ct.IsCancellationRequested) return;
                if (!luma.HasValue) continue;
                lastLuma = luma.Value;
                if (!lumaInverse && lastLuma < s.Bearing.HysteresisLow) lumaInverse = true;
                else if (lumaInverse && lastLuma > s.Bearing.HysteresisHigh) lumaInverse = false;
            }
        }

        async UniTask<float?> SampleTopBand(CancellationToken ct)
        {
            int w = Screen.width, h = Screen.height;
            if (w < 16 || h < 16) return null;
            int down = Mathf.Max(1, s.Bearing.SampleDownscale);
            int sw = Mathf.Max(1, w / down), sh = Mathf.Max(1, h / down);
            RenderTexture full = null, small = null;
            try
            {
                full = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                small = RenderTexture.GetTemporary(sw, sh, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(full, small);
                RenderTexture.ReleaseTemporary(full); full = null;
                var request = AsyncGPUReadback.Request(small, 0, TextureFormat.RGBA32);
                if (await UniTask.WaitUntil(() => request.done, PlayerLoopTiming.Update, ct).SuppressCancellationThrow()) return null;
                if (request.hasError) return null;
                NativeArray<Color32> data = request.GetData<Color32>();
                // The captured back buffer is top-first on APIs whose UV origin is at the top (D3D, Metal, Vulkan): the
                // top band is then the FIRST rows of the readback, otherwise (GL) the last rows.
                int band = Mathf.Max(1, Mathf.CeilToInt(sh * s.Bearing.TopBand));
                int y0 = SystemInfo.graphicsUVStartsAtTop ? 0 : sh - band;
                double sum = 0; int n = 0;
                for (int y = y0; y < y0 + band; y++)
                    for (int x = 0; x < sw; x++)
                    {
                        var c = data[y * sw + x];
                        sum += (.2126 * c.r + .7152 * c.g + .0722 * c.b) / 255.0; n++;
                    }
                return n > 0 ? (float)(sum / n) : (float?)null;
            }
            finally
            {
                if (full != null) RenderTexture.ReleaseTemporary(full);
                if (small != null) RenderTexture.ReleaseTemporary(small);
            }
        }

        void OnDestroy() { sampling?.Cancel(); sampling?.Dispose(); sampling = null; }
    }
}
