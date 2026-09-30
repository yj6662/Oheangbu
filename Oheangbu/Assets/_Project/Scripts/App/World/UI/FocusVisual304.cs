using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>One extra graphic FocusVisual304 recolours with the focus state (options value text and ‹ ›, map list icon ...).</summary>
    [Serializable]
    public struct FocusTint304
    {
        public Graphic Target;
        public Color Normal, Focused;
        public FocusTint304(Graphic target, Color normal, Color focused) { Target = target; Normal = normal; Focused = focused; }
    }

    /// <summary>Stock IFocusVisual304 (DESIGN §5.0 state grammar). On focus: the underlay stroke is drawn (InkReveal 0 -> 1,
    /// 240 ms), the label turns ink (or paper on a paper surface) and may switch font (FocusFont, e.g. 800) and size
    /// (FocusSize) keeping its TMP preset, meta recolours, ShowWhenFocused / HideWhenFocused swap (hollow vs filled keycap,
    /// dry under-stroke, base underlay). Disabled (the Selectable's OWN interactable flag; a page suspended by a modal
    /// CanvasGroup is NOT disabled): LabelDisabled colour, no underlay, HideWhenDisabled hidden, DimWhenDisabled x DisabledDim.
    /// Mouse-over SELECTS the row (so there is never a second focus mark). Press = underlay tone .86, scaleY .9 (relative to its
    /// own scale, flips survive), label 2 px down for 90 ms. Lives on the Selectable's GameObject. Built by PlaytestUiView.FocusRow
    /// or wired by hand: fill the fields / lists, then call Bind() last.</summary>
    [DisallowMultipleComponent]
    public sealed class FocusVisual304 : MonoBehaviour, IFocusVisual304, IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, ISubmitHandler
    {
        public UiStyle304SO Style;
        [Tooltip("the stroke under the label (Image with InkRevealEffect); hidden (reveal 0) while unfocused")]
        public Graphic Underlay;
        public TMP_Text Label;
        public Color LabelNormal = Color.white, LabelFocused = Color.black;
        [Tooltip("disabled label colour (DESIGN Off #6E6B64)")] public Color LabelDisabled = new Color32(0x6E, 0x6B, 0x64, 0xFF);
        [Tooltip("font while focused (e.g. Serif800 for a Serif600 row); null = keep. The label's TMP preset is kept.")] public TMP_FontAsset FocusFont;
        [Tooltip("font size while focused in reference px (0 = keep); scaled like the normal size (text scale)")] public float FocusSize;
        public TMP_Text Meta;
        public Color MetaNormal = Color.gray, MetaFocused = Color.gray;
        [SerializeField] RectTransform dabAnchor;
        public List<GameObject> ShowWhenFocused = new List<GameObject>();
        public List<GameObject> HideWhenFocused = new List<GameObject>();
        [Tooltip("extra graphics recoloured with the focus state (disabled = Normal)")] public List<FocusTint304> Tints = new List<FocusTint304>();
        [Tooltip("hidden while the row is disabled (keycaps, base underlay)")] public List<GameObject> HideWhenDisabled = new List<GameObject>();
        [Tooltip("alpha x DisabledDim while disabled (primary dry under-stroke .7 -> .22)")] public List<Graphic> DimWhenDisabled = new List<Graphic>();
        [Range(0f, 1f)] public float DisabledDim = .32f;
        [Tooltip("mouse-over selects this row (DESIGN: 마우스 올림 = 초점 이동)")] public bool SelectOnHover = true;

        public RectTransform DabAnchor { get => dabAnchor; set => dabAnchor = value; }
        public bool Focused { get; private set; }
        /// <summary>Frame in which a mouse-over selected this row (FocusMark304 does not auto-scroll hover selections).</summary>
        public int HoverSelectFrame { get; private set; } = -1;
        /// <summary>The row's own interactable flag (not the CanvasGroup chain): false = the disabled look.</summary>
        public bool Interactable => selectable == null || selectable.interactable;

        InkRevealEffect reveal;
        Selectable selectable;
        float underlayAlpha = 1f;
        Color underlayColor = Color.white;
        TMP_FontAsset normalFont;
        Material normalMaterial, focusMaterial;
        float normalSize, sizeFactor = 1f;
        Vector2 labelPosition;
        Vector3 pressScale = Vector3.one;
        bool bound, pressed, lastInteractable = true;
        Graphic capturedUnderlay;
        bool revealShader;
        TMP_Text capturedLabel;
        readonly List<float> dimBase = new List<float>();
        readonly UiTweenSlot304 strokeSlot = new UiTweenSlot304();
        readonly UiTweenSlot304 pressSlot = new UiTweenSlot304();

        /// <summary>Wires an existing row. Call after the label / underlay / lists exist; leaves the row in its unfocused state.</summary>
        public FocusVisual304 Bind(UiStyle304SO style, Graphic underlay, TMP_Text label, Color labelNormal, Color labelFocused,
            RectTransform dab, TMP_FontAsset focusFont = null)
        {
            Style = style; Underlay = underlay; Label = label; LabelNormal = labelNormal; LabelFocused = labelFocused;
            dabAnchor = dab; FocusFont = focusFont; bound = false; dimBase.Clear(); Capture(); Apply(false, true); return this;
        }

        /// <summary>Adds a graphic recoloured with the focus state and paints it for the current state.</summary>
        public void AddTint(Graphic target, Color normal, Color focused)
        {
            if (target == null) return;
            Tints.Add(new FocusTint304(target, normal, focused));
            target.color = Focused && Interactable ? focused : normal;
        }

        /// <summary>Re-reads the label's current font / size / material as the unfocused look. Call while the row is NOT focused,
        /// after changing the label's role or text scale by hand.</summary>
        public void Recapture() { bound = false; Capture(); Apply(Focused, true); }

        /// <summary>Enables / disables the row (Selectable.interactable) and optionally swaps the meta text (disabled reason).</summary>
        public void SetInteractable(bool interactable, string metaText = null)
        {
            Capture();
            if (selectable != null) selectable.interactable = interactable;
            if (metaText != null && Meta != null) Meta.text = metaText;
            Apply(Focused, !isActiveAndEnabled);
        }

        void Capture()
        {
            // re-capture when the row was wired by hand after AddComponent (Awake saw no underlay / label yet)
            if (bound && capturedUnderlay == Underlay && capturedLabel == Label) return;
            bound = true; capturedUnderlay = Underlay; capturedLabel = Label; reveal = null;
            selectable = GetComponent<Selectable>();
            if (Underlay != null)
            {
                underlayColor = Underlay.color; underlayAlpha = underlayColor.a;
                reveal = Underlay.GetComponent<InkRevealEffect>();
                if (reveal == null) reveal = InkRevealEffect.On(Underlay, Style, InkRevealMode.Wipe, 0f);
                // without the UI/InkReveal material (style not set up) the reveal payload is ignored: toggle the graphic instead
                var m = Underlay.material; revealShader = m != null && m.HasProperty("_NoiseTex") && m.HasProperty("_Reveal");
            }
            if (Label != null)
            {
                normalFont = Label.font; normalMaterial = Label.fontSharedMaterial; normalSize = Label.fontSize;
                labelPosition = Label.rectTransform.anchoredPosition;
                var baseSize = Label.GetComponent<UiTextBaseSize>();
                float reference = baseSize != null && baseSize.Size > 0 ? baseSize.Size : normalSize;
                sizeFactor = reference > 0f ? normalSize / reference : 1f;
                focusMaterial = FocusFont != null ? ResolveFocusMaterial() : null;
            }
        }

        // The preset (Ink_UnderPaper, Rubbing ...) of the normal material, applied to the focus font (TMP resets the material
        // to font.material when the font changes).
        Material ResolveFocusMaterial()
        {
            if (Style == null || FocusFont == null) return null;
            var preset = TmpPreset304.Paper;
            foreach (var entry in Style.Materials.Tmp) if (entry != null && entry.Material != null && entry.Material == normalMaterial) { preset = entry.Preset; break; }
            return Style.TmpMaterial(FocusFont, preset);
        }

        void Awake() { Capture(); }
        void OnDisable() { strokeSlot.Cancel(); pressSlot.Cancel(); ReleasePress(); }
        void OnDestroy() { strokeSlot.Cancel(); pressSlot.Cancel(); }

        void LateUpdate()
        {
            // a script flipping button.interactable directly still gets the disabled look
            if (bound && Interactable != lastInteractable) Apply(Focused, false);
        }

        public void SetFocused(bool focused, bool instant)
        {
            Capture();
            if (focused == Focused && !instant) return;
            Apply(focused, instant || !isActiveAndEnabled);
        }

        void Apply(bool focused, bool instant)
        {
            Focused = focused;
            bool disabled = !Interactable; lastInteractable = !disabled;
            bool lit = focused && !disabled;
            if (Label != null)
            {
                Label.color = disabled ? LabelDisabled : focused ? LabelFocused : LabelNormal;
                if (FocusFont != null && normalFont != null)
                {
                    var font = lit ? FocusFont : normalFont;
                    if (Label.font != font) Label.font = font;
                    var material = lit ? (focusMaterial != null ? focusMaterial : FocusFont.material) : normalMaterial;
                    if (material != null && Label.fontSharedMaterial != material) Label.fontSharedMaterial = material;
                }
                if (FocusSize > 0f && normalSize > 0f)
                {
                    float size = lit ? FocusSize * sizeFactor : normalSize;
                    if (!Mathf.Approximately(Label.fontSize, size)) Label.fontSize = size;
                }
            }
            if (Meta != null) Meta.color = lit ? MetaFocused : MetaNormal;
            foreach (var t in Tints) if (t.Target != null) t.Target.color = lit ? t.Focused : t.Normal;
            foreach (var go in ShowWhenFocused) if (go != null) go.SetActive(focused && !(disabled && HideWhenDisabled.Contains(go)));
            foreach (var go in HideWhenFocused) if (go != null) go.SetActive(!focused && !(disabled && HideWhenDisabled.Contains(go)));
            foreach (var go in HideWhenDisabled)
                if (go != null && !ShowWhenFocused.Contains(go) && !HideWhenFocused.Contains(go)) go.SetActive(!disabled);
            ApplyDim(disabled);

            if (Underlay != null && !revealShader) { Underlay.enabled = lit; return; }
            if (reveal == null) return;
            if (instant || Style == null)
            {
                strokeSlot.Cancel(); reveal.Reveal = lit ? 1f : 0f; UiTween304.SetAlpha(Underlay, underlayAlpha); return;
            }
            var ct = strokeSlot.Restart(this);
            if (lit) UiTween304.StrokeIn(reveal, Style, false, ct, underlayAlpha, true).Forget();
            else UiTween304.StrokeOut(reveal, Style, ct, underlayAlpha).Forget();
        }

        void ApplyDim(bool disabled)
        {
            if (DimWhenDisabled.Count == 0) return;
            // design alphas are captured on the first Apply (Bind), before anything was dimmed; re-captured if the list changes
            if (dimBase.Count != DimWhenDisabled.Count) { dimBase.Clear(); foreach (var g in DimWhenDisabled) dimBase.Add(g != null ? g.color.a : 1f); }
            for (int i = 0; i < DimWhenDisabled.Count; i++)
                if (DimWhenDisabled[i] != null) UiTween304.SetAlpha(DimWhenDisabled[i], dimBase[i] * (disabled ? DisabledDim : 1f));
        }

        // ------------------------------------------------------------------ pointer / submit
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!SelectOnHover) return;
            var s = GetComponent<Selectable>();
            if (s != null && !s.IsInteractable()) return;   // disabled row or a page suspended under a modal
            HoverSelectFrame = Time.frameCount;
            FocusMark304.Select(gameObject);
        }

        public void OnPointerDown(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) Press(); }
        public void OnPointerUp(PointerEventData eventData) { ReleasePress(); }
        public void OnPointerExit(PointerEventData eventData) { ReleasePress(); }

        public void OnSubmit(BaseEventData eventData)
        {
            Press();
            float seconds = Style != null ? Style.Motion.Sec(Style.Motion.TickMs) : .09f;
            var ct = pressSlot.Restart(this);
            ReleaseLater(seconds, ct).Forget();
        }

        async UniTaskVoid ReleaseLater(float seconds, System.Threading.CancellationToken ct)
        {
            if (await UniTask.Delay(System.TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct).SuppressCancellationThrow()) return;
            ReleasePress();
        }

        void Press()
        {
            var s = GetComponent<Selectable>();
            if (s != null && !s.IsInteractable()) return;
            Capture();
            if (pressed) return; pressed = true;
            float tone = Style != null ? Style.PressTone : .86f, scaleY = Style != null ? Style.PressScaleY : .9f, drop = Style != null ? Style.PressDrop : 2f;
            if (Underlay != null)
            {
                var c = Underlay.color; Underlay.color = new Color(underlayColor.r * tone, underlayColor.g * tone, underlayColor.b * tone, c.a);
                pressScale = Underlay.rectTransform.localScale;   // keep authored flips / scales (V.Brush flipX = -1)
                Underlay.rectTransform.localScale = Vector3.Scale(pressScale, new Vector3(1f, scaleY, 1f));
            }
            if (Label != null) Label.rectTransform.anchoredPosition = labelPosition + new Vector2(0, -drop);
        }

        void ReleasePress()
        {
            if (!pressed) return; pressed = false;
            if (Underlay != null)
            {
                var c = Underlay.color; Underlay.color = new Color(underlayColor.r, underlayColor.g, underlayColor.b, c.a);
                Underlay.rectTransform.localScale = pressScale;
            }
            if (Label != null) Label.rectTransform.anchoredPosition = labelPosition;
        }
    }
}
