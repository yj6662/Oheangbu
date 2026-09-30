using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Oheangbu.Drawing;
using PDollarGestureRecognizer;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>Replays one approved recognizer sample without changing recognition data or rules (JamoTemplateLibrarySO is only
    /// READ: gestures are built and copied, nothing is written back). #304 (IMPLEMENTATION §5.3): each stroke is a brush RIBBON
    /// strip along the template polyline (arc length u, width = rect height x .085, 기필 x1.1 over the first 3 %, 수필 x.6 over the
    /// last 30 %) textured with Sprites.Ribbon, instead of flat line quads. A film frame shows strokes [0, currentFrom) in the
    /// previous colour (안개 α.75), [currentFrom, currentTo) in the current colour (한지) drawn along their length on Replay, and the
    /// next stroke as a ghost (α.16). No loop: the current strokes draw once when built and again on Replay (획 다시 보기).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CodexStrokeExample : MaskableGraphic
    {
        readonly List<List<Vector2>> strokes = new List<List<Vector2>>();
        readonly UiTweenSlot304 playSlot = new UiTweenSlot304();
        string letter = "";
        Texture ribbon;
        int currentFrom, currentTo;
        Color previousColor = Color.white, ghostColor = new Color(1, 1, 1, .16f);
        float reveal = 1f;
        const float DrawSeconds = .55f;

        public bool HasTemplate => strokes.Count > 0;
        public string Letter => letter;
        /// <summary>Strokes of the letter in writing order (초성, 중성, 종성; each jamo in its template stroke order).</summary>
        public int StrokeCount => strokes.Count;
        public override Texture mainTexture => ribbon != null ? ribbon : base.mainTexture;

        protected override void Awake() { base.Awake(); raycastTarget = false; }
        protected override void OnDisable() { playSlot.Cancel(); base.OnDisable(); }
        protected override void OnDestroy() { playSlot.Cancel(); base.OnDestroy(); }

        /// <summary>Whole letter in one colour, drawn once (pre-#304 call; kept for other callers).</summary>
        public void Initialize(JamoTemplateLibrarySO library, string selectedLetter, Color inkColor)
        {
            Build(library, selectedLetter);
            color = inkColor; previousColor = inkColor; ghostColor = Color.clear;
            currentFrom = 0; currentTo = strokes.Count;
            Replay(0f);
        }

        /// <summary>One film frame (DESIGN §7.4 획 필름). Returns false when the letter has no approved template (show a fallback glyph).</summary>
        public bool InitializeFilm(JamoTemplateLibrarySO library, string selectedLetter, int from, int to, Color previous, Color current, Color ghost, Texture ribbonTexture)
        {
            Build(library, selectedLetter);
            ribbon = ribbonTexture;
            color = current; previousColor = previous; ghostColor = ghost;
            currentFrom = Mathf.Clamp(from, 0, strokes.Count); currentTo = Mathf.Clamp(to, currentFrom, strokes.Count);
            SetMaterialDirty(); SetVerticesDirty();
            return HasTemplate;
        }

        /// <summary>Draws the current strokes again after `delay` seconds (instant under 움직임 줄이기).</summary>
        public void Replay(float delay)
        {
            var ct = playSlot.Restart(this);
            if (UiTween304.ReducedMotion || strokes.Count == 0) { reveal = 1f; SetVerticesDirty(); return; }
            reveal = 0f; SetVerticesDirty();
            Play(delay, ct).Forget();
        }

        async UniTaskVoid Play(float delay, CancellationToken ct)
        {
            if (delay > 0f && await UiTween304.Delay(delay, ct)) return;
            var style = PlaytestUiRoot.Instance != null ? UiStyle304SO.Resolve(PlaytestUiRoot.Instance.Theme) : UiStyle304SO.Fallback;
            await UiTween304.Run(DrawSeconds, style.Motion.Stroke, v => { reveal = v; SetVerticesDirty(); }, ct);
        }

        /// <summary>Start point of a stroke in this rect's local space (pivot-relative, y up). False when out of range.</summary>
        public bool TryGetStrokeStart(int index, out Vector2 local)
        {
            local = default;
            if (index < 0 || index >= strokes.Count || strokes[index].Count == 0) return false;
            local = Local(strokes[index][0], rectTransform.rect);
            return true;
        }

        void Build(JamoTemplateLibrarySO library, string value)
        {
            letter = value ?? ""; strokes.Clear();
            if (library != null) TryBuild(library, letter);
        }

        bool TryBuild(JamoTemplateLibrarySO library, string value)
        {
            if (value.Length != 1) return false;
            int syllable = value[0] - 0xAC00; if (syllable < 0 || syllable >= 11172) return false;
            int initialIndex = syllable / (21 * 28), medialIndex = (syllable / 28) % 21, finalIndex = syllable % 28;
            string initial = InitialName(initialIndex), medial = MedialName(medialIndex), final = FinalName(finalIndex);
            if (initial == null || medial == null || (finalIndex != 0 && final == null)) return false;
            Gesture first = Find(library.BuildInitialGestures(), initial);
            Gesture middle = Find(library.BuildMedialGestures(), medial);
            Gesture last = finalIndex == 0 ? null : Find(library.BuildFinalGestures(), final);
            if (first == null || middle == null || (finalIndex != 0 && last == null)) return false;

            bool horizontal = medial == "ㅗ" || medial == "ㅜ", hasFinal = last != null;
            if (horizontal)
            {
                Append(first, hasFinal ? new Rect(.14f, .58f, .72f, .37f) : new Rect(.12f, .48f, .76f, .47f));
                Append(middle, hasFinal ? new Rect(.10f, .34f, .80f, .20f) : new Rect(.10f, .10f, .80f, .30f));
            }
            else
            {
                float y = hasFinal ? .34f : .10f, height = hasFinal ? .61f : .80f;
                Append(first, new Rect(.05f, y, .43f, height)); Append(middle, new Rect(.52f, y, .43f, height));
            }
            if (last != null) Append(last, new Rect(.18f, .04f, .64f, .24f));
            return strokes.Count > 0;
        }

        static Gesture Find(Gesture[] source, string name)
        {
            if (source == null) return null;
            foreach (var gesture in source) if (gesture != null && gesture.Name == name && gesture.Points != null && gesture.Points.Length > 1) return gesture;
            return null;
        }

        // one polyline per template stroke (StrokeID), in point order, mapped into `slot` (normalized, y up)
        void Append(Gesture gesture, Rect slot)
        {
            var points = gesture.Points; float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in points) { if (p == null) continue; minX = Mathf.Min(minX, p.X); minY = Mathf.Min(minY, p.Y); maxX = Mathf.Max(maxX, p.X); maxY = Mathf.Max(maxY, p.Y); }
            float width = Mathf.Max(.001f, maxX - minX), height = Mathf.Max(.001f, maxY - minY);
            float scale = Mathf.Min(slot.width / width, slot.height / height) * .88f;
            Vector2 center = slot.center, sourceCenter = new Vector2((minX + maxX) * .5f, (minY + maxY) * .5f);
            List<Vector2> line = null; int id = int.MinValue;
            foreach (var p in points)
            {
                if (p == null) continue;
                if (line == null || p.StrokeID != id) { line = new List<Vector2>(); strokes.Add(line); id = p.StrokeID; }
                var q = center + new Vector2(p.X - sourceCenter.x, sourceCenter.y - p.Y) * scale;
                if (line.Count == 0 || (line[line.Count - 1] - q).sqrMagnitude > .0000004f) line.Add(q);
            }
            strokes.RemoveAll(s => s.Count < 2);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (strokes.Count == 0) return;
            Rect rect = rectTransform.rect;
            float width = Mathf.Max(3f, rect.height * .085f);
            for (int i = 0; i < strokes.Count; i++)
            {
                if (i < currentFrom) Ribbon(vh, strokes[i], rect, width, previousColor, 1f);
                else if (i < currentTo) { if (reveal > 0f) Ribbon(vh, strokes[i], rect, width, color, reveal); }
                else if (i == currentTo && ghostColor.a > 0f) Ribbon(vh, strokes[i], rect, width, ghostColor, 1f);
            }
        }

        // brush ribbon: vertices at every polyline point, mitred normals, taper by arc length; drawn up to `upTo` of its length
        static readonly List<Vector2> pts = new List<Vector2>();
        static readonly List<float> arc = new List<float>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { pts.Clear(); arc.Clear(); }

        static void Ribbon(VertexHelper vh, List<Vector2> line, Rect rect, float width, Color tint, float upTo)
        {
            pts.Clear(); arc.Clear();
            float total = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                var p = Local(line[i], rect);
                if (i > 0) total += Vector2.Distance(pts[pts.Count - 1], p);
                pts.Add(p); arc.Add(total);
            }
            if (total < .5f) return;
            float end = total * Mathf.Clamp01(upTo);
            int count = pts.Count;
            if (upTo < 1f)
            {
                int k = 1; while (k < count && arc[k] < end) k++;
                if (k >= count) k = count - 1;
                float seg = arc[k] - arc[k - 1];
                var tip = seg > 1e-4f ? Vector2.Lerp(pts[k - 1], pts[k], (end - arc[k - 1]) / seg) : pts[k];
                count = k + 1; pts[k] = tip; arc[k] = end;
                if (end < .5f) return;
            }
            int start = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert; v.color = tint;
            for (int i = 0; i < count; i++)
            {
                Vector2 d0 = i > 0 ? (pts[i] - pts[i - 1]).normalized : (pts[1] - pts[0]).normalized;
                Vector2 d1 = i < count - 1 ? (pts[i + 1] - pts[i]).normalized : d0;
                Vector2 n0 = new Vector2(-d0.y, d0.x), n1 = new Vector2(-d1.y, d1.x);
                Vector2 n = (n0 + n1); n = n.sqrMagnitude > 1e-6f ? n.normalized : n0;
                float miter = 1f / Mathf.Max(.55f, Vector2.Dot(n, n0));
                float u = arc[i] / total;                      // along the WHOLE stroke, so a half-drawn stroke keeps its taper
                float taper = u < .03f ? 1.1f : u > .7f ? Mathf.Lerp(1f, .6f, (u - .7f) / .3f) : 1f;
                float half = width * .5f * taper * miter;
                v.position = pts[i] + n * half; v.uv0 = new Vector4(u, 1f, 0f, 0f); vh.AddVert(v);
                v.position = pts[i] - n * half; v.uv0 = new Vector4(u, 0f, 0f, 0f); vh.AddVert(v);
            }
            for (int i = 0; i < count - 1; i++)
            {
                int a = start + i * 2;
                vh.AddTriangle(a, a + 1, a + 3); vh.AddTriangle(a, a + 3, a + 2);
            }
        }

        static Vector2 Local(Vector2 normalized, Rect rect)
        { return new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, normalized.x), Mathf.Lerp(rect.yMin, rect.yMax, normalized.y)); }

        static string InitialName(int index) { switch (index) { case 0: return "ㄱ"; case 2: return "ㄴ"; case 6: return "ㅁ"; case 9: return "ㅅ"; case 11: return "ㅇ"; default: return null; } }
        static string MedialName(int index) { switch (index) { case 0: return "ㅏ"; case 4: return "ㅓ"; case 8: return "ㅗ"; case 13: return "ㅜ"; default: return null; } }
        static string FinalName(int index) { switch (index) { case 0: return ""; case 1: return "ㄱ"; case 4: return "ㄴ"; case 16: return "ㅁ"; case 19: return "ㅅ"; case 21: return "ㅇ"; default: return null; } }
    }
}
