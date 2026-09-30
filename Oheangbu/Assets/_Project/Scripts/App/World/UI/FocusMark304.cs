using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>What a focusable row shows when the EventSystem selects it (IMPLEMENTATION §4.2). Put the implementation
    /// on the same GameObject as the Selectable. FocusVisual304 is the stock implementation.</summary>
    public interface IFocusVisual304
    {
        /// <summary>Centre of the 방점 for this row (usually a child named "DabAnchor"); null = no dab for this row.</summary>
        RectTransform DabAnchor { get; }
        /// <summary>Draw / lift the underlay, swap the label colour. `instant` skips tweens (page rebuilds, reduced motion).</summary>
        void SetFocused(bool focused, bool instant);
    }

    /// <summary>THE single 방점 of one menu canvas (not a singleton: one per canvas, created by Attach). Every frame it looks
    /// at EventSystem.current.currentSelectedGameObject; when that changes it tells the old IFocusVisual304 to unfocus and the
    /// new one to focus, and slides the dab to the new DabAnchor in DabSlideMs (pops .6 -> 1.08 -> 1 when it was hidden).
    /// The dab follows its anchor like a child would: size x the anchor's world scale (pages scaled by UiPageFit304), alpha x the
    /// anchor's CanvasGroup chain (it appears with the page's BleedIn, last), hidden while the anchor centre is outside its
    /// RectMask2D (scrolled away). Keyboard / pad selections inside a ScrollRect are scrolled into view (AutoScroll).
    /// Only selections under `Scope` count. Validation §9.2: VisibleCount must be 1 per open menu.</summary>
    [DisallowMultipleComponent]
    public sealed class FocusMark304 : MonoBehaviour
    {
        static readonly List<FocusMark304> active = new List<FocusMark304>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }

        /// <summary>Enabled FocusMark304 components (all canvases).</summary>
        public static int ActiveCount => active.Count;
        /// <summary>Marks currently drawing their dab (the runtime assertion "방점 == 1").</summary>
        public static int VisibleCount { get { int n = 0; foreach (var m in active) if (m != null && m.Visible) n++; return n; } }

        [SerializeField] RectTransform scope;
        [SerializeField] Image dab;
        [SerializeField] UiStyle304SO style;
        [Tooltip("scroll a keyboard / pad selection inside a ScrollRect into view (mouse-over selections are left alone)")]
        [SerializeField] bool autoScroll = true;
        [SerializeField] float scrollPadding = 12f;

        GameObject current;
        IFocusVisual304 currentVisual;
        RectTransform target;
        RectMask2D clip;
        Vector2 baseSize;
        Color dabColor = Color.white;
        float dabAlpha = 1f;
        Vector3 lastPosition;
        float hiddenAt = -10f, slideT = -1f, popT = -1f;
        Vector3 slideFrom;

        public Image Dab => dab;
        public RectTransform Scope { get => scope; set => scope = value; }
        public GameObject Current => current;
        public bool AutoScroll { get => autoScroll; set => autoScroll = value; }
        /// <summary>Drawing a dab that can be seen (enabled, has a target, not faded out / clipped).</summary>
        public bool Visible => dab != null && dab.enabled && target != null && dabAlpha > .01f;

        /// <summary>Returns the mark of `layer`'s canvas, creating it (as the last child of `layer`) when missing.
        /// scope = the subtree whose selections the mark follows (usually the canvas root). Call once per canvas build.</summary>
        public static FocusMark304 Attach(UiStyle304SO s, RectTransform layer, RectTransform scope = null)
        {
            if (layer == null) return null;
            s = s != null ? s : UiStyle304SO.Fallback;
            var canvas = layer.GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas.transform : layer;
            var existing = root.GetComponentInChildren<FocusMark304>(true);
            if (existing != null)
            {
                existing.style = s; existing.dabColor = s.Cinnabar; if (scope != null) existing.scope = scope;
                existing.gameObject.SetActive(true); return existing;
            }
            var rect = PlaytestUiView.Stretch("FocusMark304", layer);
            var mark = rect.gameObject.AddComponent<FocusMark304>();
            mark.style = s; mark.scope = scope != null ? scope : root as RectTransform;
            mark.dab = PlaytestUiView.Dab(s, rect, 0, 0);
            mark.dab.gameObject.name = "Dab"; mark.dab.enabled = false; mark.dab.raycastTarget = false;
            mark.dabColor = s.Cinnabar;
            return mark;
        }

        /// <summary>Selects `go` (the page's explicit initial selection: Resume, Continue / NewGame, Cancel, first row ...).</summary>
        public static void Select(GameObject go)
        {
            var es = EventSystem.current; if (es == null) return;
            if (es.currentSelectedGameObject == go) return;
            es.SetSelectedGameObject(go);
        }

        /// <summary>Modal rule: the page behind a confirm dialog stops being interactable and loses its selection.</summary>
        public static void Suspend(CanvasGroup page, bool suspended)
        {
            if (page == null) return;
            page.interactable = !suspended;
            var es = EventSystem.current;
            if (suspended && es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.transform.IsChildOf(page.transform))
                es.SetSelectedGameObject(null);
        }

        /// <summary>Scrolls `scroll`'s content by the least amount that brings `item` (plus padding) inside the viewport.
        /// Returns true when it moved. The ScrollRect's own clamping keeps the content in range.</summary>
        public static bool ScrollIntoView(ScrollRect scroll, RectTransform item, float padding = 12f)
        {
            if (scroll == null || item == null || scroll.content == null || !item.IsChildOf(scroll.content)) return false;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, item);
            Rect v = viewport.rect;
            Vector2 d = Vector2.zero;
            if (scroll.vertical)
            {
                if (b.max.y + padding > v.yMax) d.y = b.max.y + padding - v.yMax;
                else if (b.min.y - padding < v.yMin) d.y = b.min.y - padding - v.yMin;
            }
            if (scroll.horizontal)
            {
                if (b.min.x - padding < v.xMin) d.x = b.min.x - padding - v.xMin;
                else if (b.max.x + padding > v.xMax) d.x = b.max.x + padding - v.xMax;
            }
            if (d.sqrMagnitude < 1e-4f) return false;
            var contentParent = scroll.content.parent as RectTransform;
            Vector3 world = viewport.TransformVector(d);
            Vector2 local = contentParent != null ? (Vector2)contentParent.InverseTransformVector(world) : d;
            scroll.StopMovement();
            scroll.content.anchoredPosition -= local;
            return true;
        }

        /// <summary>Forgets the current row (e.g. after V.Clear) so the next selection re-runs SetFocused.</summary>
        public void Forget() { current = null; currentVisual = null; target = null; clip = null; }

        void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        void OnDisable() { active.Remove(this); Hide(); current = null; currentVisual = null; }

        void LateUpdate()
        {
            var es = EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            if (sel != null && (!sel.activeInHierarchy || (scope != null && !sel.transform.IsChildOf(scope)))) sel = null;
            bool visualDestroyed = !ReferenceEquals(currentVisual, null) && (currentVisual as Object) == null;
            if (sel != current || visualDestroyed) Switch(sel);
            if (dab == null) return;
            if (target == null || !target.gameObject.activeInHierarchy) { Hide(); return; }

            var parent = transform.parent;
            if (parent != null && transform.GetSiblingIndex() != parent.childCount - 1) transform.SetAsLastSibling();

            // follow the anchor like a child: its world scale (scaled page) and its CanvasGroup / RectMask2D visibility
            var dr = dab.rectTransform;
            var dabParent = dr.parent;
            float k = Mathf.Abs(target.lossyScale.x) / Mathf.Max(1e-5f, dabParent != null ? Mathf.Abs(dabParent.lossyScale.x) : 1f);
            Vector2 size = baseSize * k;
            if ((dr.sizeDelta - size).sqrMagnitude > 1e-4f) dr.sizeDelta = size;
            Vector3 goal = Centre(target);
            dabAlpha = GroupAlpha(target);
            if (clip != null && dabAlpha > 0f && !Inside(clip, goal)) dabAlpha = 0f;
            var c = dabColor; c.a *= dabAlpha;
            if (dab.color != c) dab.color = c;

            float dt = Time.unscaledDeltaTime;
            var motion = style != null ? style.Motion : null;
            if (slideT >= 0f && motion != null)
            {
                slideT += dt; float d = Mathf.Max(.001f, motion.Sec(motion.DabSlideMs));
                float e = UiEase304.Eval(motion.Stroke, slideT / d);
                dr.position = Vector3.LerpUnclamped(slideFrom, goal, e);
                if (slideT >= d) slideT = -1f;
            }
            else dr.position = goal;
            if (popT >= 0f && motion != null)
            {
                popT += dt; float d = Mathf.Max(.001f, motion.Sec(motion.DabMs)); float t = Mathf.Clamp01(popT / d);
                float s = t < .6f ? Mathf.Lerp(motion.DabPopFrom, motion.DabPopPeak, UiEase304.Eval(motion.Stroke, t / .6f))
                                  : Mathf.Lerp(motion.DabPopPeak, 1f, UiEase304.Eval(motion.Lift, (t - .6f) / .4f));
                dr.localScale = Vector3.one * s;
                if (t >= 1f) { popT = -1f; dr.localScale = Vector3.one; }
            }
            lastPosition = dr.position;
        }

        void Switch(GameObject sel)
        {
            if ((currentVisual as Object) != null) currentVisual.SetFocused(false, false);
            current = sel;
            currentVisual = sel != null ? sel.GetComponent<IFocusVisual304>() : null;
            if ((currentVisual as Object) == null) currentVisual = null;
            if (currentVisual != null) currentVisual.SetFocused(true, false);
            if (sel != null && autoScroll && !SelectedByHover(sel))
            {
                var scroll = sel.GetComponentInParent<ScrollRect>();
                if (scroll != null) ScrollIntoView(scroll, sel.transform as RectTransform, scrollPadding);
            }
            var next = currentVisual != null ? currentVisual.DabAnchor : null;
            if (next == null || dab == null) { target = null; clip = null; Hide(); return; }

            bool reduced = UiTween304.ReducedMotion;
            bool wasVisible = dab.enabled && target != null;
            bool recentlyHidden = !wasVisible && Time.unscaledTime - hiddenAt < .12f;   // page rebuild: keep sliding, no second pop
            target = next;
            clip = next.GetComponentInParent<RectMask2D>();
            baseSize = next.rect.size; if (baseSize.x < 1f || baseSize.y < 1f) baseSize = style != null ? style.DabSize : new Vector2(34, 26);
            dab.rectTransform.sizeDelta = baseSize;
            dab.enabled = true;
            var dr = dab.rectTransform;
            if (!reduced && (wasVisible || recentlyHidden)) { slideFrom = wasVisible ? dr.position : lastPosition; slideT = 0f; popT = -1f; dr.localScale = Vector3.one; }
            else { slideT = -1f; dr.position = Centre(next); popT = reduced ? -1f : 0f; dr.localScale = reduced ? Vector3.one : Vector3.one * (style != null ? style.Motion.DabPopFrom : .6f); }
        }

        void Hide()
        {
            if (dab != null && dab.enabled) { dab.enabled = false; hiddenAt = Time.unscaledTime; }
            target = null; clip = null; slideT = -1f; popT = -1f; dabAlpha = 0f;
        }

        static bool SelectedByHover(GameObject sel)
        {
            var fv = sel.GetComponent<FocusVisual304>();
            return fv != null && fv.HoverSelectFrame == Time.frameCount;
        }

        // product of the CanvasGroup alphas above t (stops at a group with ignoreParentGroups)
        static float GroupAlpha(Transform t)
        {
            float a = 1f;
            for (var p = t; p != null; p = p.parent)
            {
                if (!p.TryGetComponent(out CanvasGroup g) || !g.enabled) continue;
                a *= g.alpha;
                if (g.ignoreParentGroups || a <= 0f) break;
            }
            return a;
        }

        static bool Inside(RectMask2D mask, Vector3 world)
        {
            var mr = mask.rectTransform;
            return mr.rect.Contains((Vector2)mr.InverseTransformPoint(world));
        }

        static Vector3 Centre(RectTransform r) => r.TransformPoint(r.rect.center);
    }
}
