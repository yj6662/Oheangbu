using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>탭 레일 (DESIGN §5.3, build.py rail()). One stroke_line (64,106) 1792x16 paper α.45; tabs bottom-aligned at
    /// y106 with 14 px padding, gap 44, fixed slot width = the label's width at the SELECTED role (Title34) so nothing jumps;
    /// selected = Title34 paper + swell (h36, slot + 26 left + 30 right, 16 below the line), others Label24 mist.
    /// Q / E small keycaps at both ends (LB / RB), then 40 px later the "[closeKey] 닫기" row named "Close".
    /// Buttons are named Tab_&lt;page&gt; (harness names). SetSelected(page, true) slides the swell (SwellMs, scaleX 1.3 -> 1) and
    /// scale-tweens the two tab labels (24/34 -> 1 and 34/24 -> 1 about their bottom-left) so the size change is not a jump.
    /// Pages are rebuilt after V.Clear on every tab change, so pass the previous page as fromPage to Create / V.MenuRail.</summary>
    public sealed class MenuRail304 : MonoBehaviour
    {
        [Serializable]
        public sealed class Tab
        {
            public string Page;
            public Button Button;
            public TMP_Text Label;
            public RectTransform Slot;
            public float SlotWidth;
            public FocusVisual304 Visual;
            [NonSerialized] public readonly UiTweenSlot304 ScaleSlot = new UiTweenSlot304();
        }

        [SerializeField] UiStyle304SO style;
        [SerializeField] List<Tab> tabs = new List<Tab>();
        public Image Line, Swell;
        public RectTransform KeyQ, KeyE;
        public Button Close;
        public string Selected { get; private set; } = "";
        readonly UiTweenSlot304 swellSlot = new UiTweenSlot304();

        public IReadOnlyList<Tab> Tabs => tabs;

        /// <summary>Builds the rail. fromPage (the page shown before this rebuild, reachable) = build on it, then animate to
        /// selectedPage; null / same page = no animation.</summary>
        public static MenuRail304 Create(UiStyle304SO s, Transform parent, string selectedPage, IReadOnlyList<string> pages, string closeKey,
            Func<string, string> label, Action<string> open, Action close, string closeLabel = "닫기", string fromPage = null)
        {
            s = s != null ? s : UiStyle304SO.Fallback;
            var r = s.Rail;
            var root = V.Rect("MenuRail304", parent, 0, 0, UiPageFit304.Width, r.TabsBottom + r.SwellBelow + 8);
            var rail = root.gameObject.AddComponent<MenuRail304>();
            rail.style = s;
            rail.Line = V.Brush(s, root, "RailLine", StrokeClass304.Line, s.Paper, r.Line.x, r.Line.y, r.Line.width, r.Line.height, r.LineAlpha);
            rail.Swell = V.Swell(s, root, 0, r.TabsBottom + r.SwellBelow - r.SwellH, 10, r.SwellH, s.Paper, r.SwellAlpha);
            rail.Swell.enabled = false;

            float x = r.Left;
            rail.KeyQ = V.Keycap(s, root, "Q", true, false, true, x, 0);
            V.Place(rail.KeyQ, x, r.TabsBottom - r.KeyBottomMargin - rail.KeyQ.sizeDelta.y);
            x += rail.KeyQ.sizeDelta.x + r.TabGap;

            var selRole = s.Role(r.SelectedRole);
            if (pages != null)
                foreach (var page in pages)
                {
                    if (string.IsNullOrEmpty(page)) continue;
                    string text = label != null ? label(page) : page;
                    string p = page;
                    var slot = V.Rect("Tab_" + page, root, x, r.TabsBottom - r.HitHeight, 10, r.HitHeight);
                    var hit = V.Image(slot, new Color(0, 0, 0, 0), null, true); hit.canvasRenderer.cullTransparentMesh = true;
                    var button = slot.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None; button.targetGraphic = hit;
                    if (open != null) button.onClick.AddListener(() => open(p));
                    var t = V.Label(s, slot, "Label", text, selRole, s.Paper, 0, 0, 0, 0);
                    float w = Mathf.Max(t.rectTransform.sizeDelta.x, s.MinHit);
                    slot.sizeDelta = new Vector2(w, r.HitHeight);
                    var lr = t.rectTransform;   // bottom-aligned inside the slot, 14 px above the line
                    lr.anchorMin = lr.anchorMax = lr.pivot = Vector2.zero;
                    lr.anchoredPosition = new Vector2(0, r.TabPadBottom);
                    // height = the selected role's real line box (serif ascenders exceed Size x 1.3); bottom-anchored, so nothing moves
                    lr.sizeDelta = new Vector2(w + 8, Mathf.Max(selRole.Size * 1.3f, t.GetPreferredValues(text, w + 8, 0f).y + 2f));
                    t.alignment = TextAlignmentOptions.BottomLeft;
                    Vector2 dab = s.DabSize * .82f;
                    var anchor = V.DabAnchor(slot, -dab.x - 12, r.HitHeight - r.TabPadBottom - 14 - dab.y * .5f, dab.x, dab.y, s);
                    var visual = slot.gameObject.AddComponent<FocusVisual304>();
                    visual.Bind(s, null, t, s.Mist, s.Paper, anchor);
                    rail.tabs.Add(new Tab { Page = page, Button = button, Label = t, Slot = slot, SlotWidth = w, Visual = visual });
                    x += w + r.TabGap;
                }

            rail.KeyE = V.Keycap(s, root, "E", true, false, true, x, 0);
            V.Place(rail.KeyE, x, r.TabsBottom - r.KeyBottomMargin - rail.KeyE.sizeDelta.y);
            x += rail.KeyE.sizeDelta.x + r.CloseGap;

            if (!string.IsNullOrEmpty(closeKey) || close != null)
            {
                float hh = Mathf.Max(s.MinHit, s.Keycap.SmallHeight);
                var row = V.Rect("Close", root, x, r.TabsBottom - r.KeyBottomMargin - s.Keycap.SmallHeight - (hh - s.Keycap.SmallHeight) * .5f, 10, hh);
                var hit = V.Image(row, new Color(0, 0, 0, 0), null, true); hit.canvasRenderer.cullTransparentMesh = true;
                var button = row.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None; button.targetGraphic = hit;
                if (close != null) button.onClick.AddListener(() => close());
                var hint = V.Hint(s, row, "Hint", new[] { string.IsNullOrEmpty(closeKey) ? "Esc" : closeKey }, closeLabel, s.Mist, 0, (hh - s.Keycap.SmallHeight) * .5f);
                row.sizeDelta = new Vector2(hint.sizeDelta.x, hh);
                var hintLabel = hint.Find("Label") != null ? hint.Find("Label").GetComponent<TMP_Text>() : null;
                Vector2 dab = s.DabSize * .82f;
                var anchor = V.DabAnchor(row, -dab.x - 12, (hh - dab.y) * .5f, dab.x, dab.y, s);
                row.gameObject.AddComponent<FocusVisual304>().Bind(s, null, hintLabel, s.Mist, s.Paper, anchor);
                rail.Close = button;
            }

            if (!string.IsNullOrEmpty(fromPage) && fromPage != selectedPage && rail.Find(fromPage) != null && rail.Find(selectedPage) != null)
            { rail.SetSelected(fromPage, false); rail.SetSelected(selectedPage, true); }
            else rail.SetSelected(selectedPage, false);
            return rail;
        }

        public Tab Find(string page) { foreach (var t in tabs) if (t.Page == page) return t; return null; }
        public Button TabButton(string page) => Find(page)?.Button;

        /// <summary>Page `dir` steps from the selected one (Q = -1, E = +1), wrapping; null when the rail is empty.</summary>
        public string Neighbor(int dir)
        {
            if (tabs.Count == 0) return null;
            int i = tabs.FindIndex(t => t.Page == Selected); if (i < 0) i = 0;
            int n = ((i + dir) % tabs.Count + tabs.Count) % tabs.Count;
            return tabs[n].Page;
        }

        /// <summary>Marks `page` selected: role / colour swap, swell under its slot. animate (and motion not reduced): the swell
        /// slides and the labels whose role changed scale-tween from their old size.</summary>
        public void SetSelected(string page, bool animate)
        {
            var s = style != null ? style : UiStyle304SO.Fallback; var r = s.Rail;
            string previous = Selected;
            Selected = page ?? "";
            bool tween = animate && isActiveAndEnabled && !UiTween304.ReducedMotion;
            float seconds = s.Motion.Sec(s.Motion.SwellMs);
            Tab target = null;
            foreach (var t in tabs)
            {
                bool sel = t.Page == Selected; if (sel) target = t;
                float before = t.Label != null ? t.Label.fontSize : 0f;
                UiText304.ApplyRole(t.Label, s.Role(sel ? r.SelectedRole : r.UnselectedRole), s);
                if (t.Visual != null) { t.Visual.LabelNormal = sel ? s.Paper : s.Mist; t.Visual.LabelFocused = s.Paper; }
                if (t.Label != null) t.Label.color = sel || (t.Visual != null && t.Visual.Focused) ? s.Paper : s.Mist;
                if (t.Label == null) continue;
                var lr = t.Label.rectTransform;
                bool changed = t.Page == Selected || t.Page == previous;
                if (tween && changed && before > 0f && t.Label.fontSize > 0f && !Mathf.Approximately(before, t.Label.fontSize))
                {
                    lr.localScale = Vector3.one * (before / t.Label.fontSize);   // pivot bottom-left: grows / shrinks from the baseline
                    UiTween304.Scale(lr, Vector3.one, seconds, s.Motion.Stroke, t.ScaleSlot.Restart(this)).Forget();
                }
                else if (!tween) { t.ScaleSlot.Cancel(); lr.localScale = Vector3.one; }
            }
            if (Swell == null) return;
            if (target == null) { Swell.enabled = false; return; }
            float w = target.SlotWidth + r.SwellLeft + r.SwellRight;
            float left = target.Slot.anchoredPosition.x - r.SwellLeft;
            var sr = Swell.rectTransform;
            var goal = new Vector2(left + w * .5f, -(r.TabsBottom + r.SwellBelow - r.SwellH * .5f));
            bool wasShown = Swell.enabled;
            Swell.enabled = true;
            sr.sizeDelta = new Vector2(w, r.SwellH);
            if (!animate || !wasShown || UiTween304.ReducedMotion || !isActiveAndEnabled)
            { swellSlot.Cancel(); sr.anchoredPosition = goal; sr.localScale = Vector3.one; return; }
            var ct = swellSlot.Restart(this);
            UiTween304.Move(sr, goal, seconds, s.Motion.Stroke, ct).Forget();
            sr.localScale = new Vector3(s.Motion.SwellSlideScaleX, 1f, 1f);
            UiTween304.Scale(sr, Vector3.one, seconds, s.Motion.Stroke, ct).Forget();
        }

        void OnDisable()
        {
            swellSlot.Cancel();
            foreach (var t in tabs) { t.ScaleSlot.Cancel(); if (t.Label != null) t.Label.rectTransform.localScale = Vector3.one; }
        }
    }
}
