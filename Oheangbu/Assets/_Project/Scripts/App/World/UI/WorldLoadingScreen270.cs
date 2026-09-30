using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>Region loading screen (#304 loading.png + REFERENCE_BOARD D20). Full-bleed region art, a 420 px veil ramp at the
    /// bottom and ONE ink wet_m band (40,836) h170 anchored to the bottom-left corner that carries: the region name (Serif900 48)
    /// + where the journey resumes ("금표 주막에서 이어서", Meta20), the thin progress line (FlowProgressLine304, 640 px, end ticks;
    /// the rotating spinner is gone), the stage text ("먹을 가는 중") after the line, and one quiet waiting line (a region line or
    /// a codex stroke rule: data, never an instruction). Failure keeps the band: the error (CinnabarLift) replaces the resume
    /// meta and two focus rows [다시 시도] (initial selection) / [로비로] replace the line. Unscaled time throughout.
    /// Progress signal for harnesses: Value / Progress01 only grow; ProgressFill ("LoadingStatus/ProgressLine/Fill") has
    /// fillAmount == Value; WaitingLine changes after Profile.StallSeconds (2 s) without progress (AdvanceWaiting).</summary>
    public sealed class WorldLoadingScreen270 : MonoBehaviour
    {
        public static Vector3 RequestedPosition { get; set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { RequestedPosition = Vector3.zero; }

        const float BandX = 40f, BandY = 836f, BandH = 170f, BandAlpha = .93f;
        const float TextX = 96f, RegionY = 858f, MetaY = 878f, MetaGap = 32f;
        const float LineY = 944f, LineW = 640f, StageGap = 24f, WaitY = 966f, ContentPad = 24f;
        const float ActionY = 922f, ActionH = 58f;

        public WorldLoadingProfile270 Profile;
        [Tooltip("#304 style; empty = Profile.Style304, then the playtest root theme's Style304")] public UiStyle304SO Style304;
        public CanvasGroup Fade { get; private set; }
        public Camera ObservedCamera { get; set; }
        public int RenderedFrames { get; private set; }
        /// <summary>Loading progress 0..1; never decreases (SetProgress keeps the maximum).</summary>
        public float Value { get; private set; }
        /// <summary>Same as Value (the #304 name).</summary>
        public float Progress01 => Value;
        public FlowProgressLine304 ProgressLine => line;
        /// <summary>The "Fill" image of the progress line: fillAmount == Value.</summary>
        public Image ProgressFill => line != null ? line.Fill : null;
        public string Stage => status != null ? status.text : "";
        public string RegionName => region != null ? region.text : "";
        public string Journey => journey != null ? journey.text : "";
        /// <summary>The waiting row as shown ("ㄱ 가로에서 꺾어 한 번에 내린다" or an authored line); "" when there is none.</summary>
        public string WaitingLine => waitText == null ? "" : waitHead != null && waitHead.text.Length > 0 ? waitHead.text + " " + waitText.text : waitText.text;
        /// <summary>How often the waiting line changed since Build (stall rule, see AdvanceWaiting).</summary>
        public int WaitingChanges { get; private set; }
        public bool Failed { get; private set; }
        /// <summary>#304: the rotating spinner was removed (DESIGN §8 bans spinners). Always null; see ProgressFill / AdvanceWaiting.</summary>
        public RectTransform Spinner => null;
        public RawImage Illustration { get; private set; }

        UiStyle304SO style;
        RectTransform canvasRect, band, waitRow, actions;
        Image underlay;
        TMP_Text region, journey, status, waitHead, waitText;
        FlowProgressLine304 line;
        CanvasGroup waitGroup;
        readonly List<WorldLoadingProfile270.WaitingLine> lines = new List<WorldLoadingProfile270.WaitingLine>();
        int lineIndex = -1;
        float sinceProgress, sinceLine, waitRight;
        bool changedThisStall;
        string lineKey;
        readonly UiTweenSlot304 waitSlot = new UiTweenSlot304();

        void Awake() { Build(); }
        void OnEnable() { RenderPipelineManager.endCameraRendering += CameraRendered; }
        void OnDisable() { RenderPipelineManager.endCameraRendering -= CameraRendered; waitSlot.Cancel(); }
        void OnDestroy() { waitSlot.Cancel(); }
        void CameraRendered(ScriptableRenderContext context, Camera camera) { if (camera == ObservedCamera) RenderedFrames++; }
        void Update() { AdvanceWaiting(Time.unscaledDeltaTime); }
        /// <summary>#304: retired with the spinner (no-op, kept for old callers).</summary>
        public void AdvanceSpinner(float unscaledDelta) { }
        /// <summary>#304 harness tick (CompactLoading270 looks for Advance304): the per-frame work of Update (the waiting-line
        /// stall rule) driven with an explicit unscaled delta. Progress01 has no display tween, so it needs no tick.</summary>
        public void Advance304(float unscaledDelta) => AdvanceWaiting(unscaledDelta);

        // local role: D20 waiting line = Prose 24 (NanumMyeongjo, no table role at 24)
        static TypeRole WaitRole => new TypeRole(UiType304.Prose28, UiFont304.Prose, 24f, 1.5f, 1f);

        UiStyle304SO ResolveStyle()
        {
            if (Style304 != null) return Style304;
            if (Profile != null && Profile.Style304 != null) return Profile.Style304;
            var root = PlaytestUiRoot.Instance;
            return root != null ? V.Style(root.Theme) : UiStyle304SO.Fallback;
        }

        public void Build()
        {
            if (Fade != null || Profile == null) return;
            var s = style = ResolveStyle();
            var root = new GameObject("RegionLoadingCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
            V.EnsureCanvasChannels(canvas);
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            canvasRect = (RectTransform)root.transform;
            Fade = root.AddComponent<CanvasGroup>();
            var page = V.Stretch("LoadingPage", root.transform);
            V.Image(V.Stretch("Black", page), s.Veil, null, true);   // 먹장막, never pure #000 (DESIGN §2.5)
            Illustration = V.Raw(V.Stretch("RegionIllustration", page), Profile.Fallback, Color.white);
            var crop = Illustration.gameObject.AddComponent<AspectRatioFitter>(); crop.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

            // 하단 420 px 먹 그라데이션 α.66 (loading.png) - only with the real ramp sprite (a flat band would hide the art)
            if (s.Sprites.RampBottom != null)
            {
                var ramp = V.Rect("RampBottom", page, 0, 0, 0, 420);
                ramp.anchorMin = new Vector2(0f, 0f); ramp.anchorMax = new Vector2(1f, 0f); ramp.pivot = new Vector2(.5f, 0f);
                ramp.anchoredPosition = Vector2.zero; ramp.sizeDelta = new Vector2(0f, 420f);
                var rampImage = V.Image(ramp, UiStyle304SO.A(s.Veil, .66f), s.Sprites.RampBottom);
                // #304 QA2: on the shared InkReveal material (fully drawn) so the ink ramp gets the linear-space alpha remap of
                // the other veils; on UI/Default the α.66 gradient composited in linear light and read far lighter than loading.png
                InkRevealEffect.On(rampImage, s, InkRevealMode.Wipe, 1f);
            }

            // the band keeps its distance to the bottom-left corner on any aspect; inside it the mockup px apply
            band = V.RectAnchored("LoadingStatus", page, 0, 0, 1920, 1080, UiAnchor304.BottomLeft);
            underlay = V.Stroke(s, band, "LoadingUnderlay", StrokeClass304.WetM, s.Ink, BandX, BandY, BandH, TextX + LineW + ContentPad, BandAlpha);
            region = Text(s, band, "RegionName", "", s.Role(UiType304.Region48), s.Paper, TextX, RegionY);
            journey = Text(s, band, "Journey", "", s.Role(UiType304.Meta20), s.Mist, TextX, MetaY);
            line = FlowProgressLine304.Create(s, band, "ProgressLine", TextX, LineY, LineW, s.Paper);
            status = Text(s, band, "CurrentOperation", "지역 불러오는 중", s.Role(UiType304.Meta20), s.Mist, TextX + LineW + StageGap, 0f);
            waitRow = V.Rect("WaitingLine", band, TextX, WaitY, 1, 40);
            waitGroup = waitRow.gameObject.AddComponent<CanvasGroup>(); waitGroup.interactable = false; waitGroup.blocksRaycasts = false;
            waitHead = V.Label(s, waitRow, "Head", "", UiType304.Serif900_28, s.Paper, 0f, 0f);
            waitText = V.Label(s, waitRow, "Text", "", WaitRole, s.Mist, 0f, 0f);
            actions = V.Rect("FailureActions", band, 0, 0, 1920, 1080);
            FocusMark304.Attach(s, canvasRect);   // the failure rows' 방점 (one per canvas)
            Select(RequestedPosition); SetProgress(0, "지역 불러오는 중");
        }

        static TMP_Text Text(UiStyle304SO s, Transform parent, string name, string value, TypeRole role, Color color, float x, float cssTop)
        {
            var t = V.Label(s, parent, name, value, role, color, x, 0f);
            V.Place(t.rectTransform, x, cssTop + role.CssTopOffset(t.font));
            return t;
        }

        public void Select(Vector3 world)
        {
            if (Illustration == null) return;
            Illustration.texture = Profile.Select(world);
            var texture = Illustration.texture;
            Illustration.GetComponent<AspectRatioFitter>().aspectRatio = texture != null ? texture.width / (float)texture.height : 16f / 9;
            string name = Profile.RegionName(world);
            if (region != null && region.text != name) { SetText(region, name); Relayout(); }
            string key = string.Join("|", Profile.LocationIds(world));
            if (key != lineKey) { lineKey = key; RefreshLines(world); }
        }

        /// <summary>Where the journey resumes, e.g. "금표 주막에서 이어서" / "새 여정" (meta after the region name).</summary>
        public void SetJourney(string meta)
        {
            if (journey == null) return;
            if (journey.text == (meta ?? "")) return;
            SetText(journey, meta ?? ""); Relayout();
        }

        public void SetProgress(float value, string operation)
        {
            float v = Mathf.Clamp01(value);
            if (v > Value + 1e-4f) { sinceProgress = 0f; changedThisStall = false; }
            Value = Mathf.Max(Value, v);
            if (line != null) line.SetValue(Value);
            if (status != null && status.text != (operation ?? "")) { SetText(status, operation ?? ""); Relayout(); }
        }

        /// <summary>The waiting line changes once the progress has not grown for Profile.StallSeconds (2 s) and the line has been
        /// up that long; while the stall lasts it changes again every Profile.LineSeconds. Called from Update with unscaled time;
        /// edit-mode checks can call it directly.</summary>
        public void AdvanceWaiting(float unscaledDelta)
        {
            if (Failed || Profile == null || lines.Count < 2 || !(unscaledDelta > 0f)) return;
            sinceProgress += unscaledDelta; sinceLine += unscaledDelta;
            float stall = Mathf.Max(.1f, Profile.StallSeconds);
            float need = changedThisStall ? Mathf.Max(stall, Profile.LineSeconds) : stall;
            if (sinceProgress < stall || sinceLine < need) return;
            ShowLine(lineIndex + 1, true);
            changedThisStall = true; WaitingChanges++;
        }

        void RefreshLines(Vector3 world)
        {
            string current = lineIndex >= 0 && lineIndex < lines.Count ? lines[lineIndex].Text : null;
            Profile.CollectWaitingLines(world, style, lines);
            // widest line decides the band width once, so a new line never makes the band jump
            waitRight = 0f;
            foreach (var l in lines) waitRight = Mathf.Max(waitRight, MeasureWait(l));
            int keep = current != null ? lines.FindIndex(l => l.Text == current) : -1;
            ShowLine(keep >= 0 ? keep : lines.Count > 0 ? UnityEngine.Random.Range(0, lines.Count) : -1, false);
            Relayout();
        }

        float MeasureWait(WorldLoadingProfile270.WaitingLine l)
        {
            if (waitText == null || l == null) return 0f;
            float head = !string.IsNullOrEmpty(l.Head) && waitHead != null ? Mathf.Ceil(UiText304.Preferred(waitHead, l.Head).x) + 12f : 0f;
            return head + Mathf.Ceil(UiText304.Preferred(waitText, l.Text).x);
        }

        void ShowLine(int index, bool animate)
        {
            if (waitText == null) return;
            if (index < 0 || lines.Count == 0) { SetText(waitHead, ""); SetText(waitText, ""); lineIndex = -1; return; }
            lineIndex = index % lines.Count; sinceLine = 0f;
            var l = lines[lineIndex];
            bool head = !string.IsNullOrEmpty(l.Head);
            SetText(waitHead, head ? l.Head : "");
            SetText(waitText, l.Text);
            float headW = head ? waitHead.rectTransform.sizeDelta.x + 12f : 0f;
            var role = WaitRole; float textTop = role.CssTopOffset(waitText.font);
            V.Place(waitText.rectTransform, headW, textTop);
            // the jamo sits on the text line's middle
            float mid = textTop + waitText.rectTransform.sizeDelta.y * .5f;
            V.Place(waitHead.rectTransform, 0f, mid - waitHead.rectTransform.sizeDelta.y * .5f);
            waitRow.sizeDelta = new Vector2(Mathf.Max(1f, headW + waitText.rectTransform.sizeDelta.x), 40f);
            if (animate && Application.isPlaying && style != null && waitGroup != null)
                UiTween304.BleedIn(waitGroup, style, 0, waitSlot.Restart(this)).Forget();
            else if (waitGroup != null) { waitSlot.Cancel(); waitGroup.alpha = 1f; }
        }

        static void SetText(TMP_Text t, string value)
        {
            if (t == null) return;
            t.text = value ?? "";
            Vector2 p = t.GetPreferredValues(t.text);
            t.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, Mathf.Ceil(p.x)), Mathf.Max(1f, Mathf.Ceil(p.y)));
        }

        // re-flows the band: resume meta after the region name, stage after the line (or the error), band width = content + tail
        void Relayout()
        {
            if (band == null || style == null) return;
            float right = TextX + LineW;
            if (region != null && journey != null)
            {
                float mx = TextX + (region.text.Length > 0 ? region.rectTransform.sizeDelta.x + MetaGap : 0f);
                float roleTop = style.Role(UiType304.Meta20).CssTopOffset(journey.font);
                V.Place(journey.rectTransform, mx, MetaY + roleTop);
                if (journey.gameObject.activeSelf) right = Mathf.Max(right, mx + journey.rectTransform.sizeDelta.x);
                if (Failed && status != null)
                {
                    var role = style.Role(UiType304.Label24);
                    V.Place(status.rectTransform, mx, MetaY - 3f + role.CssTopOffset(status.font));
                    right = Mathf.Max(right, mx + status.rectTransform.sizeDelta.x);
                }
            }
            if (!Failed && status != null)
            {
                float sx = TextX + LineW + StageGap;
                V.Place(status.rectTransform, sx, LineY - status.rectTransform.sizeDelta.y * .5f);
                right = Mathf.Max(right, sx + status.rectTransform.sizeDelta.x);
                right = Mathf.Max(right, TextX + waitRight);
            }
            if (Failed && actions != null)
                foreach (RectTransform row in actions) right = Mathf.Max(right, row.anchoredPosition.x + row.sizeDelta.x);
            if (underlay != null)
            {
                float w = Mathf.Max(1f, V.StrokeWidth(style, StrokeClass304.WetM, BandX, BandH, right + ContentPad));
                var r = underlay.rectTransform;
                r.sizeDelta = new Vector2(w, BandH);
                r.anchoredPosition = new Vector2(BandX + w * .5f, r.anchoredPosition.y);   // V.Stroke rects are centre-pivoted
            }
        }

        public void Fail(string message, Action retry, Action lobby)
        {
            Failed = true; waitSlot.Cancel();
            var s = style != null ? style : UiStyle304SO.Fallback;
            if (status != null)
            {
                UiText304.ApplyRole(status, s.Role(UiType304.Label24), s);
                status.color = s.CinnabarLift;
                SetText(status, message);
            }
            if (journey != null) journey.gameObject.SetActive(false);
            if (line != null) line.gameObject.SetActive(false);
            if (waitRow != null) waitRow.gameObject.SetActive(false);
            if (actions != null)
                for (int i = actions.childCount - 1; i >= 0; i--)
                {
                    var child = actions.GetChild(i).gameObject; child.SetActive(false);
                    if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
                }
            var retryRow = AddAction(s, "다시 시도", BandX + 16f, retry);
            float next = Right(retryRow) + 20f - 40f;
            AddAction(s, "로비로", next, lobby);
            Relayout();
            FocusMark304.Select(retryRow.Button.gameObject);
        }

        // right edge (band px) of a row's focus underlay, so the next label never sits on the previous tail
        static float Right(FocusRow304 row)
        {
            float rowX = row.Rect.anchoredPosition.x;
            if (row.Underlay == null) return rowX + row.Rect.sizeDelta.x;
            var u = row.Underlay.rectTransform;
            return rowX + u.anchoredPosition.x + u.sizeDelta.x * .5f;
        }

        FocusRow304 AddAction(UiStyle304SO s, string label, float x, Action callback)
        {
            var theme = PlaytestUiRoot.Instance != null ? PlaytestUiRoot.Instance.Theme : null;
            var row = V.FocusRow(s, actions, label, label, x, ActionY, 300f, ActionH, null, new FocusRowSpec304
            {
                Role = UiType304.Label28, LabelX = 40f, SoundTheme = theme,
            });
            // the hit area ends at the label (+ pad) so the two rows never overlap
            var lr = row.Label.rectTransform;
            row.Rect.sizeDelta = new Vector2(lr.anchoredPosition.x + lr.sizeDelta.x + 24f, row.Rect.sizeDelta.y);
            row.Button.onClick.AddListener(() => { row.Button.interactable = false; callback?.Invoke(); });
            return row;
        }
    }
}
