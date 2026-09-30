using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 상호작용 프롬프트 (DESIGN §5.8 / §7.1, REFERENCE D08): a filled keycap + verb phrase (Label27 paper) on an ink
    /// wet_s underlay α.93 h100, anchored BottomCentre. The content block (key + verb) is centred on x960 (D08); the underlay
    /// grows both ways with the tail slice on the right (V.StrokeWidth rule). "[F] 석경 조각 살피기" becomes [F] + "석경 조각 살피기";
    /// a line without a leading [key] (short feedback) shows the phrase alone. In: stroke wipe PromptInMs; out: alpha PromptOutMs.
    /// Root "InteractionPrompt" (1920x1080 BottomCentre frame, CanvasGroup) > PromptUnderlay, PromptContent > Key_&lt;k&gt;, PromptText.</summary>
    public sealed class HudPrompt304 : MonoBehaviour
    {
        static readonly Regex KeyPrefix = new Regex(@"^\s*\[([^\[\]\r\n]{1,8})\]\s*(.*)$", RegexOptions.Singleline);

        UiStyle304SO s;
        HudTokens304 k;
        RectTransform frame, content, key;
        CanvasGroup group, contentGroup;
        Image underlay;
        InkRevealEffect fx;
        TMP_Text label;
        string keyText, verbText, source;
        bool shown;
        float builtScale = -1f;
        readonly UiTweenSlot304 inSlot = new UiTweenSlot304(), outSlot = new UiTweenSlot304();

        /// <summary>Logical visibility (true from Show until Hide, independent of the 120 ms fade).</summary>
        public bool Visible => shown;
        public string Text => source;
        public string KeyLabel => keyText;
        public string Verb => verbText;
        public TMP_Text Label => label;
        public Image Underlay => underlay;
        /// <summary>QA1 diagnostics (hud304-prompt): frame alpha x content alpha, and the underlay's InkReveal value.</summary>
        public float DrawnAlpha => group != null && contentGroup != null ? group.alpha * contentGroup.alpha : 0f;
        public float UnderlayReveal => fx != null ? fx.Reveal : 0f;

        public static HudPrompt304 Create(UiStyle304SO s, HudTokens304 k, Transform canvas)
        {
            s = s != null ? s : UiStyle304SO.Fallback; k = k != null ? k : HudTokens304.Load();
            var p = s.Prompt;
            var frame = V.RectAnchored("InteractionPrompt", canvas, 0f, 0f, UiPageFit304.Width, UiPageFit304.Height, p.Anchor);
            var prompt = frame.gameObject.AddComponent<HudPrompt304>();
            prompt.s = s; prompt.k = k; prompt.frame = frame;
            prompt.group = frame.gameObject.AddComponent<CanvasGroup>();
            prompt.group.blocksRaycasts = false; prompt.group.interactable = false;
            // QA1: ink wet_s α.93 at its sRGB (mockup) weight in the Linear project. QA2: V.Stroke is on UI/InkReveal, which remaps
            // itself (after2/prompt.png band 26 on ~152 ground = the sRGB α.93 look); InkAlphaFor never compensates it twice
            prompt.underlay = V.Stroke(s, frame, "PromptUnderlay", StrokeClass304.WetS, s.Ink, p.Origin.x, p.Origin.y, p.Height, p.LabelPos.x + 200f, k.InkAlphaFor(s.Materials.InkReveal, p.UnderlayAlpha));
            prompt.fx = prompt.underlay.GetComponent<InkRevealEffect>();
            prompt.content = V.Rect("PromptContent", frame, 0f, 0f, UiPageFit304.Width, UiPageFit304.Height);
            prompt.contentGroup = prompt.content.gameObject.AddComponent<CanvasGroup>();
            prompt.contentGroup.blocksRaycasts = false; prompt.contentGroup.interactable = false;
            prompt.label = V.Label(s, prompt.content, "PromptText", "", p.LabelRole, s.Paper, p.LabelPos.x, p.LabelPos.y);
            prompt.builtScale = UiText304.TextScale;
            frame.gameObject.SetActive(false);
            return prompt;
        }

        /// <summary>Splits "[F] 동사구" into key + verb (key null when the line has no leading bracket key).</summary>
        public static void Parse(string text, out string keyLabel, out string verb)
        {
            keyLabel = null; verb = text ?? "";
            var m = KeyPrefix.Match(verb);
            if (m.Success) { keyLabel = m.Groups[1].Value.Trim(); verb = m.Groups[2].Value.Trim(); }
            else verb = verb.Trim();
        }

        public void Show(string text)
        {
            if (string.IsNullOrEmpty(text)) { Hide(); return; }
            bool scaleChanged = !Mathf.Approximately(builtScale, UiText304.TextScale);
            if (shown && text == source && !scaleChanged) return;
            source = text;
            Parse(text, out string newKey, out string verb);
            bool entering = !shown;
            if (entering)
            {   // activate before measuring (TMP preferred sizes of an inactive label are not reliable)
                outSlot.Cancel();
                if (!frame.gameObject.activeSelf) { contentGroup.alpha = 0f; fx.Reveal = 0f; frame.gameObject.SetActive(true); }
                group.alpha = 1f;
            }
            Layout(newKey, verb, scaleChanged);
            if (!entering) return;                                // same surface, new words: relayout without a second wipe
            shown = true;
            var ct = inSlot.Restart(this);
            UiTween304.RevealIn(fx, s, s.Motion.PromptInMs, ct, k.InkAlphaFor(underlay != null ? underlay.material : null, s.Prompt.UnderlayAlpha)).Forget();
            contentGroup.alpha = 0f;
            UiTween304.Alpha(contentGroup, 1f, s.Motion.Sec(s.Motion.PromptInMs, UiTween304.ReducedMotion), s.Motion.Stroke, ct).Forget();
        }

        public void Hide(bool instant = false)
        {
            if (!shown && !instant) return;
            shown = false; source = null;
            inSlot.Cancel();
            if (instant || !frame.gameObject.activeSelf) { outSlot.Cancel(); frame.gameObject.SetActive(false); return; }
            FadeOut(outSlot.Restart(this)).Forget();
        }

        async UniTaskVoid FadeOut(System.Threading.CancellationToken ct)
        {
            await UiTween304.Alpha(group, 0f, s.Motion.Sec(s.Motion.PromptOutMs, UiTween304.ReducedMotion), s.Motion.Lift, ct);
            if (ct.IsCancellationRequested || this == null || shown) return;
            frame.gameObject.SetActive(false);
            group.alpha = 1f;
        }

        void Layout(string newKey, string verb, bool rescale)
        {
            var p = s.Prompt;
            if (rescale) { UiText304.ApplyRole(label, s.Role(p.LabelRole), s); builtScale = UiText304.TextScale; }
            // ---- verb phrase: one line, wrapping (어절) only past the max width
            label.text = verb ?? "";
            label.textWrappingMode = TextWrappingModes.NoWrap;
            Vector2 pref = label.GetPreferredValues(label.text);
            if (pref.x > k.PromptMaxLabelWidth)
            {
                label.textWrappingMode = TextWrappingModes.Normal;
                pref = label.GetPreferredValues(label.text, k.PromptMaxLabelWidth, 0f);
                pref.x = k.PromptMaxLabelWidth;
            }
            float lw = Mathf.Ceil(pref.x), lh = Mathf.Ceil(pref.y);
            float oneLine = Mathf.Ceil(label.GetPreferredValues("가").y);
            label.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, lw), Mathf.Max(1f, lh));

            // ---- keycap (rebuilt only when the key changes)
            if (newKey != keyText || (key == null) != string.IsNullOrEmpty(newKey))
            {
                if (key != null) { key.gameObject.SetActive(false); Destroy(key.gameObject); key = null; }
                keyText = newKey;
                if (!string.IsNullOrEmpty(newKey)) key = V.Keycap(s, content, newKey, true, false, false, p.KeyPos.x, p.KeyPos.y);
            }
            verbText = verb;
            float kw = key != null ? key.sizeDelta.x : 0f;

            // ---- D08: content block centred on x960, underlay from key - 64 to verb right + ContentPad + tail slice
            float blockW = (key != null ? kw + k.PromptKeyGap : 0f) + lw;
            float left = k.PromptCentreX - blockW * .5f;
            float h = Mathf.Clamp(Mathf.Max(p.Height, lh + (p.Height - oneLine)), p.Height, k.PromptMaxHeight);
            float centreY = p.Origin.y + p.Height * .5f;
            float uy = centreY - h * .5f;
            float labelX = key != null ? left + kw + k.PromptKeyGap : left;
            float labelY = p.LabelPos.y - (lh - oneLine) * .5f;
            if (key != null) V.Place(key, left, p.KeyPos.y);
            V.Place(label.rectTransform, labelX, labelY);
            float ux = left - k.PromptKeyLead;
            float cr = labelX + lw + p.ContentPad;
            float w = Mathf.Max(1f, V.StrokeWidth(s, StrokeClass304.WetS, ux, h, cr));
            var r = underlay.rectTransform;
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(ux + w * .5f, -(uy + h * .5f));
            var spec = s.Stroke(StrokeClass304.WetS);
            if (underlay.type == Image.Type.Sliced) underlay.pixelsPerUnitMultiplier = Mathf.Max(.01f, spec.NativeH / Mathf.Max(1f, h));
        }

        void OnDisable() { inSlot.Cancel(); }
        void OnDestroy() { inSlot.Cancel(); outSlot.Cancel(); }
    }
}
