using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Oheangbu.App.World.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#304 rules shared by the review / validation harnesses (PlaytestMenuReview, CompactRebuildUiChecks,
    /// CompactLobby274, CompactLoading270, WorldMacroPlaytestHudAuthoring, UiOverhaul304 tours).
    /// Text surfaces are TMP_Text AND legacy Text. Labels are read like UiText304.Get. Rules come from DESIGN §3.3 / §8
    /// (no instruction sentences, no icon-only buttons, 16 px floor) and IMPLEMENTATION §8-9. Members of other engineers'
    /// new APIs are reached by name (reflection) so the harness compiles before they land; missing = SKIPPED, not FAIL.
    /// Stateless: no static fields that survive a Play session (domain reload is disabled).
    /// #304 QA round 2: drawn-graphic alpha (CanvasGroup chain x colour x CanvasRenderer), the InkReveal linear-alpha remap mirror,
    /// and a motion sample (CanvasGroup / reveal / dab) so harness waits settle on unscaled time instead of a frame count.</summary>
    public static class HarnessUiRules304
    {
        public const BindingFlags AnyMember = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        public const float MinimumTextPx = 16f;   // DESIGN §8: nothing below 16 (keycap sm / order no. are the only 16 px roles)
        public const float MetaFloorPx = 20f;     // DESIGN §3.2: meta 20 is the smallest reading size
        public const float TitleSafeLeft = 64f, TitleSafeRight = 1856f, TitleSafeTop = 40f, TitleSafeBottom = 1016f;

        // ------------------------------------------------------------------ text surfaces
        /// <summary>TMP_Text and legacy Text under `root` (own GameObject included).</summary>
        public static IEnumerable<Graphic> Texts(Component root, bool includeInactive = false)
        {
            if (root == null) yield break;
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(includeInactive)) if (t != null) yield return t;
            foreach (var t in root.GetComponentsInChildren<Text>(includeInactive)) if (t != null) yield return t;
        }

        public static string TextOf(Graphic g)
        {
            if (g is TMP_Text tmp) return tmp.text ?? string.Empty;
            if (g is Text legacy) return legacy.text ?? string.Empty;
            return string.Empty;
        }

        public static float FontSizeOf(Graphic g)
        {
            if (g is TMP_Text tmp) return tmp.fontSize;
            if (g is Text legacy) return legacy.fontSize;
            return 0f;
        }

        /// <summary>Built and enabled with non-blank text (alpha is ignored: pages bleed in from alpha 0).</summary>
        public static bool IsLive(Graphic g) => g != null && g.isActiveAndEnabled && !string.IsNullOrWhiteSpace(TextOf(g));

        /// <summary>IsLive + its canvas draws + colour alpha x CanvasGroup chain alpha above .01.</summary>
        public static bool IsVisible(Graphic g)
        {
            if (!IsLive(g)) return false;
            var canvas = g.canvas;
            if (canvas == null || !canvas.isActiveAndEnabled || (canvas.rootCanvas != null && !canvas.rootCanvas.enabled)) return false;
            return g.color.a * GroupAlpha(g) > .01f;
        }

        /// <summary>Product of the enabled CanvasGroup alphas above `c` (stops at ignoreParentGroups).</summary>
        public static float GroupAlpha(Component c)
        {
            float a = 1f;
            for (var t = c != null ? c.transform : null; t != null; t = t.parent)
                foreach (var g in t.GetComponents<CanvasGroup>())
                {
                    if (g == null || !g.enabled) continue;
                    a *= g.alpha;
                    if (g.ignoreParentGroups) return a;
                }
            return a;
        }

        public static List<Graphic> LiveTexts(Component root) => Texts(root).Where(IsLive).ToList();
        public static List<Graphic> VisibleTexts(Component root) => Texts(root).Where(IsVisible).ToList();

        public static string Path(Transform leaf, Transform root)
        {
            var names = new Stack<string>();
            for (var t = leaf; t != null && t != root; t = t.parent) names.Push(t.name);
            return string.Join("/", names);
        }

        public static string Describe(Graphic g, Transform root)
        {
            string text = TextOf(g).Replace("\n", "\\n");
            if (text.Length > 40) text = text.Substring(0, 40) + "…";
            return Path(g.transform, root) + " \"" + text + "\"";
        }

        // ------------------------------------------------------------------ keycaps / labels
        /// <summary>A keycap glyph (V.Keycap "Key_&lt;label&gt;", FocusRow "Key" / "KeyFocused" / "FocusKey", rail KeyQ / KeyE):
        /// it names a key, it is not the row's label (DESIGN §5.8: [건반] + 동사구).</summary>
        public static bool IsKeycapText(Component t, Transform stop = null)
        {
            for (var tr = t != null ? t.transform.parent : null; tr != null && tr != stop; tr = tr.parent)
            {
                string n = tr.name;
                if (n.StartsWith("Key_", StringComparison.Ordinal) || n == "Key" || n == "KeyFocused" || n == "FocusKey" || n == "KeyQ" || n == "KeyE") return true;
                if (tr.GetComponent<Selectable>() != null) break;
            }
            return false;
        }

        /// <summary>Visible labels of a Selectable (active TMP / Text children, then UiText304.Get). Sliders also read their row
        /// (the parent), because V.Slider puts the name beside the slider.</summary>
        public static List<string> Labels(Selectable s, bool includeKeycaps = false)
        {
            var result = new List<string>();
            if (s == null) return result;
            Transform scope = s is Slider && s.transform.parent != null ? s.transform.parent : s.transform;
            foreach (var g in Texts(scope))
            {
                if (!IsLive(g)) continue;
                if (!includeKeycaps && IsKeycapText(g, scope)) continue;
                result.Add(TextOf(g).Trim());
            }
            // a grid cell (Gear_*, Codex_*) may carry its name as a sibling right under / beside it: accept a live sibling text
            // whose centre lies within 60 px of the Selectable (a neighbouring Selectable's own labels are never borrowed)
            if (result.Count == 0) foreach (var g in NearbySiblingTexts(s, 60f)) result.Add(TextOf(g).Trim());
            if (result.Count == 0 && includeKeycaps)
            {
                string primary = UiText304.Get(s.gameObject);
                if (!string.IsNullOrWhiteSpace(primary)) result.Add(primary.Trim());
            }
            return result;
        }

        static IEnumerable<Graphic> NearbySiblingTexts(Selectable s, float margin)
        {
            var parent = s != null ? s.transform.parent as RectTransform : null;
            var own = s != null ? s.transform as RectTransform : null;
            if (parent == null || own == null) yield break;
            Rect area = RectIn(parent, own); area.xMin -= margin; area.yMin -= margin; area.xMax += margin; area.yMax += margin;
            foreach (Transform child in parent)
            {
                if (child == s.transform || child.GetComponent<Selectable>() != null) continue;
                foreach (var g in Texts(child))
                {
                    if (!IsLive(g) || IsKeycapText(g, parent) || g.GetComponentInParent<Selectable>() != null) continue;
                    if (area.Contains(RectIn(parent, g.rectTransform).center)) yield return g;
                }
            }
        }

        /// <summary>Axis-aligned bounds of `r` in `space` local coordinates.</summary>
        public static Rect RectIn(RectTransform space, RectTransform r)
        {
            var corners = new Vector3[4]; r.GetWorldCorners(corners);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            foreach (var c in corners)
            {
                Vector3 p = space.InverseTransformPoint(c);
                xMin = Mathf.Min(xMin, p.x); yMin = Mathf.Min(yMin, p.y); xMax = Mathf.Max(xMax, p.x); yMax = Mathf.Max(yMax, p.y);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        static string Collapse(string value) => Regex.Replace(value ?? string.Empty, @"\s+", "");

        /// <summary>True when one of the Selectable's labels (TMP or legacy, keycaps included) equals `requested`
        /// (trimmed, case-insensitive; a second pass ignores whitespace so vertical 세로 labels match too).</summary>
        public static bool MatchesLabel(Selectable s, string requested)
        {
            if (s == null || string.IsNullOrWhiteSpace(requested)) return false;
            string want = requested.Trim();
            var labels = Labels(s, true);
            string primary = UiText304.Get(s.gameObject);
            if (!string.IsNullOrWhiteSpace(primary)) labels.Add(primary.Trim());
            if (labels.Any(l => string.Equals(l, want, StringComparison.OrdinalIgnoreCase))) return true;
            string compact = Collapse(want);
            return compact.Length > 0 && labels.Any(l => string.Equals(Collapse(l), compact, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>DESIGN §8 "아이콘만 있는 버튼이나 행" / IMPLEMENTATION §9.6: an active Selectable with no live non-keycap label.
        /// Scrollbars are exempt (they are part of a scroll view, not an action). D308-27 answer 1 (SPEC-PLAYTEST-TEXT-DIET
        /// AC-TD27.4): so is a 술식 칸 not found yet - it shows its 묵등 block and no word in the grid; the word stands once in the
        /// detail of the selected 칸 (MenuText308 checks that it does).</summary>
        public static bool IsIconOnly(Selectable s)
        {
            if (s == null || !s.isActiveAndEnabled || s is Scrollbar) return false;
            if (Labels(s).Count > 0) return false;
            if (IsUnfoundCodexCell(s)) return false;
            // sub-controls of a labelled row (the options ‹ › steppers PrevHit / NextHit) take the row's label
            for (var p = s.transform.parent; p != null; p = p.parent)
            {
                var row = p.GetComponent<Selectable>();
                if (row != null && row != s) return Labels(row).Count == 0;
            }
            return true;
        }

        /// <summary>A 술식 도감 matrix cell (Codex_&lt;letter&gt; with ContentCell304) that draws its 묵등 block (or the Blot fallback) and
        /// no live text: the 칸 of a letter not found yet (D308-27 answer 1).</summary>
        public static bool IsUnfoundCodexCell(Selectable s)
        {
            if (s == null || !s.name.StartsWith("Codex_", StringComparison.Ordinal) || s.GetComponent<ContentCell304>() == null) return false;
            if (LiveTexts(s).Count > 0) return false;
            return s.GetComponentsInChildren<Image>(false).Any(i => i != null && (i.name == "Mukdeung" || i.name == "Blot"));
        }

        public static List<string> IconOnlySelectables(Component root)
        {
            var list = new List<string>();
            if (root == null) return list;
            foreach (var s in root.GetComponentsInChildren<Selectable>(false))
                if (IsIconOnly(s)) list.Add(Path(s.transform, root.transform) + " (" + s.GetType().Name + ")");
            return list;
        }

        // ------------------------------------------------------------------ copy rules
        static readonly Regex BracketKey = new Regex(@"\[[^\[\]\r\n]{1,12}\]");
        static readonly Regex Imperative = new Regex(@"(하세요|하십시오|누르세요|클릭하세요|(을|를)\s*(눌러|누르)|\bPress\b|\bClick\b)", RegexOptions.IgnoreCase);
        static readonly Regex NumericOnly = new Regex(@"^[\s\d.,:%/+\-×]+$");

        /// <summary>DESIGN §3.3 지시문 금지: a key legend ("[M] 지도  [I] 소지품  [Esc] 메뉴" = two or more bracketed keys in one text)
        /// or an imperative sentence ("F를 눌러 ~하세요"). A single "[F] 동사구" prompt source is NOT instruction style
        /// (the HUD turns it into keycap + verb phrase).</summary>
        public static bool IsInstructionStyle(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (BracketKey.Matches(text).Count >= 2) return true;
            return Imperative.IsMatch(text);
        }

        /// <summary>C01: HUD numbers are banned (a label that is only digits / separators).</summary>
        public static bool IsNumericOnly(string text) => !string.IsNullOrWhiteSpace(text) && text.Any(char.IsDigit) && NumericOnly.IsMatch(text.Trim());

        /// <summary>Spoken lines (the dialogue band's Prose body: TMP Page overflow, or under a *StoryBand* / *Dialogue* object).
        /// A character may say "조심하세요"; that is speech, not a UI instruction, so only the key-legend rule applies to it.</summary>
        public static bool IsDiegeticProse(Component t)
        {
            if (t is TMP_Text tmp && tmp.overflowMode == TextOverflowModes.Page) return true;
            for (var tr = t != null ? t.transform : null; tr != null; tr = tr.parent)
                if (tr.name.IndexOf("StoryBand", StringComparison.OrdinalIgnoreCase) >= 0 || tr.name.IndexOf("Dialogue", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static List<string> InstructionTexts(Component root, bool visibleOnly = true)
        {
            var list = new List<string>();
            if (root == null) return list;
            foreach (var g in Texts(root))
            {
                if (!(visibleOnly ? IsVisible(g) : IsLive(g))) continue;
                string text = TextOf(g);
                bool instruction = IsDiegeticProse(g) ? BracketKey.Matches(text).Count >= 2 : IsInstructionStyle(text);
                if (instruction) list.Add(Describe(g, root.transform));
            }
            return list;
        }

        // ------------------------------------------------------------------ HUD surfaces
        static readonly string[] HudTextNames = { "Prompt", "Interaction", "Toast", "Bearing", "Notice", "Arrival", "Location", "BossBar" };

        /// <summary>#304 HUD text allow-list: the interaction prompt (label + keycap, world F), toasts, the bearing line labels and
        /// the arrival card; #306 (D306) adds the boss bar's name (HudBossBar304). Everything else on the HUD (meters, reticle,
        /// the lock-on HP stroke, the minimap, danger edges) carries no text.</summary>
        public static bool IsAllowedHudText(Component t, Transform hudRoot)
        {
            for (var tr = t != null ? t.transform : null; tr != null; tr = tr.parent)
            {
                string n = tr.name;
                if (n.StartsWith("Key", StringComparison.OrdinalIgnoreCase)) return true;
                foreach (string allowed in HudTextNames) if (n.IndexOf(allowed, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                foreach (var mb in tr.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue;
                    string type = mb.GetType().Name;
                    foreach (string allowed in HudTextNames) if (type.IndexOf(allowed, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                if (tr == hudRoot) break;
            }
            return false;
        }

        public static bool IsToastOrNotice(Component t)
        {
            for (var tr = t != null ? t.transform : null; tr != null; tr = tr.parent)
            {
                if (tr.name.StartsWith("Toast", StringComparison.OrdinalIgnoreCase) || tr.name == "Notice") return true;
                foreach (var mb in tr.GetComponents<MonoBehaviour>())
                    if (mb != null && mb.GetType().Name.IndexOf("Toast", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>Visible HUD texts outside the allow-list, instruction-style HUD texts and HUD numbers (outside toasts).</summary>
        public static void AuditHudTexts(Component hud, out List<string> outside, out List<string> instruction, out List<string> numbers)
        {
            outside = new List<string>(); instruction = new List<string>(); numbers = new List<string>();
            if (hud == null) return;
            foreach (var g in Texts(hud))
            {
                if (!IsVisible(g)) continue;
                string text = TextOf(g), where = Describe(g, hud.transform);
                if (!IsAllowedHudText(g, hud.transform)) outside.Add(where);
                if (IsInstructionStyle(text)) instruction.Add(where);
                if (!IsToastOrNotice(g) && IsNumericOnly(text)) numbers.Add(where);
            }
        }

        /// <summary>Canvases under the HUD other than the world-space interaction prompt (IMPLEMENTATION §8: exactly one = HUD_Canvas).</summary>
        public static List<Canvas> HudScreenCanvases(Component hud)
        {
            var list = new List<Canvas>();
            if (hud == null) return list;
            foreach (var c in hud.GetComponentsInChildren<Canvas>(true))
                if (c != null && !(c.renderMode == RenderMode.WorldSpace && c.name.IndexOf("InteractionPrompt", StringComparison.OrdinalIgnoreCase) >= 0)) list.Add(c);
            return list;
        }

        // ------------------------------------------------------------------ layout
        /// <summary>TMP overflow (null = fits). Truncate / Ellipsis report isTextOverflowing; Overflow / Masking never do, so the laid
        /// out preferred size is compared with the rect: NoWrap = width, wrapping = height. Page (dialogue) and ScrollRect modes
        /// overflow by design. Rects of 1 px or less are anchors, auto-sized labels fit themselves.</summary>
        public static string Overflow(TMP_Text t, float tolerance = 2f)
        {
            if (t == null) return null;
            Rect rect = t.rectTransform.rect;
            if (rect.width <= 1f || rect.height <= 1f || t.enableAutoSizing) return null;
            var mode = t.overflowMode;
            if (mode == TextOverflowModes.Page || mode == TextOverflowModes.ScrollRect || mode == TextOverflowModes.Linked) return null;
            t.ForceMeshUpdate();
            if (t.isTextOverflowing) return "truncated(" + mode + ")";
            bool wraps = t.textWrappingMode == TextWrappingModes.Normal || t.textWrappingMode == TextWrappingModes.PreserveWhitespace;
            Vector2 preferred = t.GetPreferredValues(t.text, wraps ? rect.width : 0f, 0f);
            bool horizontal = !wraps && preferred.x > rect.width + tolerance;
            bool vertical = preferred.y > rect.height + tolerance;
            if (!horizontal && !vertical) return null;
            return (horizontal ? "width" : "height") + " preferred=" + preferred.x.ToString("0.0") + "x" + preferred.y.ToString("0.0")
                + " rect=" + rect.width.ToString("0.0") + "x" + rect.height.ToString("0.0") + " mode=" + mode + " wrap=" + t.textWrappingMode;
        }

        /// <summary>Legacy uGUI Text overflow (the pre-#304 rule, kept for pages not rebuilt yet).</summary>
        public static string Overflow(Text label)
        {
            if (label == null) return null;
            Rect rect = label.rectTransform.rect;
            if (rect.width <= 1f || rect.height <= 1f || label.resizeTextForBestFit) return null;
            bool vertical = label.verticalOverflow == VerticalWrapMode.Truncate && label.preferredHeight > rect.height + .1f;
            bool horizontal = label.horizontalOverflow == HorizontalWrapMode.Overflow && label.preferredWidth > rect.width + .1f;
            if (!vertical && !horizontal) return null;
            return "preferred=" + label.preferredWidth.ToString("0.0") + "x" + label.preferredHeight.ToString("0.0")
                + " rect=" + rect.width.ToString("0.0") + "x" + rect.height.ToString("0.0");
        }

        /// <summary>World corners of the drawn text (TMP textBounds; empty text = none).</summary>
        public static bool TextWorldCorners(TMP_Text t, Vector3[] corners)
        {
            if (t == null || corners == null || corners.Length < 4) return false;
            Bounds b = t.textBounds;
            if (b.size.x <= 0f && b.size.y <= 0f) return false;
            var r = t.rectTransform;
            corners[0] = r.TransformPoint(new Vector3(b.min.x, b.min.y, 0f));
            corners[1] = r.TransformPoint(new Vector3(b.min.x, b.max.y, 0f));
            corners[2] = r.TransformPoint(new Vector3(b.max.x, b.max.y, 0f));
            corners[3] = r.TransformPoint(new Vector3(b.max.x, b.min.y, 0f));
            return true;
        }

        /// <summary>Clipped by a RectMask2D / Mask (scroll views): outside-the-screen tests do not apply.</summary>
        public static bool IsClipped(Component c) => c != null && (c.GetComponentInParent<RectMask2D>() != null || c.GetComponentInParent<Mask>() != null);

        /// <summary>Runs the LateUpdate layout pass a frame would run (PlaytestUiRoot title card / legacy folio, UiPageFit304,
        /// UiVeilFit304) so synchronous editor checks see final scales, then forces the canvases.</summary>
        public static void SettleLayout(Component root)
        {
            if (root == null) return;
            // CanvasScaler applies a changed reference resolution (UI scale preview) in its own Update: apply it now
            foreach (var scaler in root.GetComponentsInChildren<CanvasScaler>(false)) InvokeUnityMessage(scaler, "Handle");
            Canvas.ForceUpdateCanvases();
            InvokeUnityMessage(root, "LateUpdate");
            foreach (var fit in root.GetComponentsInChildren<UiPageFit304>(false)) InvokeUnityMessage(fit, "LateUpdate");
            foreach (var fit in root.GetComponentsInChildren<UiVeilFit304>(false)) InvokeUnityMessage(fit, "LateUpdate");
            Canvas.ForceUpdateCanvases();
        }

        static void InvokeUnityMessage(Component target, string message)
        {
            if (target == null) return;
            var m = target.GetType().GetMethod(message, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            m?.Invoke(target, null);
        }

        /// <summary>UiText304.TextScale is cached per frame. A settings preview applied inside one editor callback must not build
        /// pages with the previous frame's scale, so the harness invalidates the cache (foundation gap: no public invalidate).</summary>
        public static void InvalidateTextScaleCache()
        {
            typeof(UiText304).GetField("scaleFrame", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, -1);
        }

        // ------------------------------------------------------------------ reflection (APIs other engineers add)
        /// <summary>Value of a property or field by name (instance, or static when `target` is a Type). null when missing.</summary>
        public static object Member(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name)) return null;
            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            var p = type.GetProperty(name, AnyMember);
            if (p != null && p.GetIndexParameters().Length == 0 && p.CanRead) return p.GetValue(instance);
            var f = type.GetField(name, AnyMember);
            return f != null ? f.GetValue(instance) : null;
        }

        /// <summary>Writes a property or field by name (instance, or static when `target` is a Type). False when missing / read-only /
        /// of another type. Harness-only: used to put private UI state back after a diagnostic (layout-check tabs, map ticks).</summary>
        public static bool SetMember(object target, string name, object value)
        {
            if (target == null || string.IsNullOrEmpty(name)) return false;
            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            try
            {
                var p = type.GetProperty(name, AnyMember);
                if (p != null && p.CanWrite && p.GetIndexParameters().Length == 0 && (value == null ? !p.PropertyType.IsValueType : p.PropertyType.IsInstanceOfType(value)))
                { p.SetValue(instance, value); return true; }
                var f = type.GetField(name, AnyMember);
                if (f != null && !f.IsInitOnly && !f.IsLiteral && (value == null ? !f.FieldType.IsValueType : f.FieldType.IsInstanceOfType(value)))
                { f.SetValue(instance, value); return true; }
            }
            catch (TargetInvocationException) { }
            catch (ArgumentException) { }
            return false;
        }

        public static bool HasMember(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name)) return false;
            Type type = target as Type ?? target.GetType();
            return type.GetProperty(name, AnyMember) != null || type.GetField(name, AnyMember) != null;
        }

        public static bool TryFloat(object target, string name, out float value)
        {
            value = 0f;
            object raw = Member(target, name);
            if (raw is float f) { value = f; return true; }
            if (raw is double d) { value = (float)d; return true; }
            return false;
        }

        /// <summary>First public instance method among `names` whose parameters match one of `signatures` (in order).</summary>
        public static MethodInfo FindMethod(Type type, IEnumerable<string> names, params Type[][] signatures)
        {
            if (type == null) return null;
            foreach (string name in names)
                foreach (var signature in signatures)
                {
                    var m = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, signature, null);
                    if (m != null) return m;
                }
            return null;
        }

        public static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            if (type == null) return null;
            return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameterCount);
        }

        // ------------------------------------------------------------------ alpha / motion (#304 QA round 2)
        /// <summary>Drawn by uGUI this frame: active + enabled, its canvas and root canvas enabled, not culled (RectMask2D), not a
        /// hidden Mask graphic, not a TMP sub-mesh (it repeats its parent text), not blank text. Alpha is NOT considered.</summary>
        public static bool IsDrawn(Graphic g)
        {
            if (g == null || !g.isActiveAndEnabled || g is TMP_SubMeshUI) return false;
            var canvas = g.canvas;
            if (canvas == null || !canvas.isActiveAndEnabled || (canvas.rootCanvas != null && !canvas.rootCanvas.enabled)) return false;
            if (g.canvasRenderer == null || g.canvasRenderer.cull) return false;
            var mask = g.GetComponent<Mask>();
            if (mask != null && mask.isActiveAndEnabled && !mask.showMaskGraphic) return false;
            if ((g is TMP_Text || g is Text) && string.IsNullOrWhiteSpace(TextOf(g))) return false;
            return true;
        }

        /// <summary>What the graphic is multiplied by on screen: colour alpha x the enabled CanvasGroup chain x the CanvasRenderer
        /// alpha (CrossFadeAlpha). Before any shader remap.</summary>
        public static float EffectiveAlpha(Graphic g)
            => g == null ? 0f : g.color.a * GroupAlpha(g) * (g.canvasRenderer != null ? g.canvasRenderer.GetAlpha() : 1f);

        /// <summary>The enabled CanvasGroup above `c` with the lowest alpha (below `below`), null when the chain is at 1.</summary>
        public static CanvasGroup LowestGroup(Component c, float below = .98f)
        {
            CanvasGroup lowest = null;
            for (var t = c != null ? c.transform : null; t != null; t = t.parent)
            {
                bool stop = false;
                foreach (var g in t.GetComponents<CanvasGroup>())
                {
                    if (g == null || !g.enabled) continue;
                    if (g.alpha < below && (lowest == null || g.alpha < lowest.alpha)) lowest = g;
                    if (g.ignoreParentGroups) stop = true;
                }
                if (stop) break;
            }
            return lowest;
        }

        /// <summary>The foundation shaders that remap alpha for linear-space blending (UI/InkReveal, UI/InkMeter with
        /// _LinearInkGamma &gt; 1 in a Linear project). Everything else (UI/Default, TMP, map shaders) composites the raw alpha.</summary>
        public static bool IsGammaCompensated(Material m, out float gamma)
        {
            gamma = 1f;
            if (m == null || m.shader == null) return false;
            string n = m.shader.name;
            if (n != "UI/InkReveal" && n != "UI/InkMeter") return false;
            gamma = m.HasProperty("_LinearInkGamma") ? m.GetFloat("_LinearInkGamma") : 1f;
            return gamma > 1.001f && QualitySettings.activeColorSpace == ColorSpace.Linear;
        }

        /// <summary>Linear luminance of the tint (the shader's ink / paper split: below .2 = ink, above .6 = paper).</summary>
        public static float TintLuminance(Color tint)
        {
            Color c = QualitySettings.activeColorSpace == ColorSpace.Linear ? tint.linear : tint;
            return .2126f * c.r + .7152f * c.g + .0722f * c.b;
        }

        public static string LayerOf(Color tint)
        {
            float lum = TintLuminance(tint);
            return lum < .2f ? "ink" : lum > .6f ? "paper" : "mid";
        }

        /// <summary>The UI/InkReveal remap (mirrors the shader): ink layers 1-(1-a)^g, paper layers a^g, blended by the tint luminance.
        /// For an uncompensated graphic this is the linear alpha it would need to look like the mockup's sRGB alpha `a`.</summary>
        public static float LinearMatchAlpha(Color tint, float a, float gamma = 2.2f)
        {
            a = Mathf.Clamp01(a);
            if (gamma <= 1.001f || QualitySettings.activeColorSpace != ColorSpace.Linear) return a;
            float dark = 1f - Mathf.Pow(1f - a, gamma), light = Mathf.Pow(a, gamma);
            return Mathf.Lerp(dark, light, Mathf.Clamp01((TintLuminance(tint) - .2f) * 2.5f));
        }

        /// <summary>What the #304 tweens move under `root`: every enabled CanvasGroup alpha, InkRevealEffect.Reveal and each
        /// FocusMark304 dab (colour alpha, scale, screen position, private dabAlpha). Keys are per object + channel so two samples
        /// of consecutive frames can be compared (a frame count says nothing about unscaled-time tweens). Stateless.</summary>
        public static void SampleMotion(Component root, Dictionary<long, float> values, Dictionary<long, string> names = null)
        {
            values.Clear(); names?.Clear();
            if (root == null) return;
            Transform space = root.transform;
            foreach (var g in root.GetComponentsInChildren<CanvasGroup>(false))
                if (g != null && g.enabled) PutMotion(values, names, g, 0, g.alpha, space, "alpha");
            foreach (var fx in root.GetComponentsInChildren<InkRevealEffect>(false))
                if (fx != null && fx.isActiveAndEnabled) PutMotion(values, names, fx, 1, fx.Reveal, space, "reveal");
            foreach (var mark in root.GetComponentsInChildren<FocusMark304>(false))
            {
                if (mark == null || mark.Dab == null) continue;
                var dab = mark.Dab; var t = dab.rectTransform;
                PutMotion(values, names, dab, 2, dab.enabled ? dab.color.a : 0f, space, "dab.a");
                PutMotion(values, names, dab, 3, t.localScale.x, space, "dab.scale");
                PutMotion(values, names, dab, 4, t.position.x, space, "dab.x");
                PutMotion(values, names, dab, 5, t.position.y, space, "dab.y");
                if (TryFloat(mark, "dabAlpha", out float da)) PutMotion(values, names, mark, 6, da, space, "dabAlpha");
            }
        }

        static void PutMotion(Dictionary<long, float> values, Dictionary<long, string> names, UnityEngine.Object o, int channel, float v, Transform space, string what)
        {
            long key = (long)o.GetInstanceID() * 8L + channel;
            values[key] = v;
            if (names != null && o is Component c) names[key] = Path(c.transform, space) + "." + what;
        }

        /// <summary>Keys whose value moved between two SampleMotion results (appeared / vanished keys count). Positions (channels
        /// 4, 5) use .5 px, everything else .002.</summary>
        public static int MotionChanged(Dictionary<long, float> before, Dictionary<long, float> after, List<long> changed = null)
        {
            changed?.Clear();
            int n = 0;
            foreach (var kv in after)
            {
                long channel = ((kv.Key % 8L) + 8L) % 8L;
                float eps = channel == 4 || channel == 5 ? .5f : .002f;
                if (!before.TryGetValue(kv.Key, out float was) || Mathf.Abs(was - kv.Value) > eps) { n++; changed?.Add(kv.Key); }
            }
            foreach (var kv in before) if (!after.ContainsKey(kv.Key)) { n++; changed?.Add(kv.Key); }
            return n;
        }

        /// <summary>Unscaled seconds a #304 page needs before its tweens can be at rest: the veil wipe or the content bleed
        /// (order 2: 2 x RevealGapMs + RevealMs), whichever is longer (reduced motion honoured). .36 s without a style.</summary>
        public static double MotionBudgetSeconds(PlaytestUiRoot root)
        {
            var s = root != null && root.Theme != null ? PlaytestUiView.Style(root.Theme) : null;
            if (s == null || s.Motion == null) return .36d;
            bool reduced = UiTween304.ReducedMotion;
            float veil = s.Motion.Sec(s.Motion.VeilMs, reduced);
            float bleed = (reduced ? 0f : 2f * s.Motion.RevealGapMs / 1000f) + s.Motion.Sec(s.Motion.RevealMs, reduced);
            return Math.Max(veil, bleed);
        }

        public static bool Near(Color a, Color b, float tolerance = .03f)
            => Mathf.Abs(a.r - b.r) <= tolerance && Mathf.Abs(a.g - b.g) <= tolerance && Mathf.Abs(a.b - b.b) <= tolerance;

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c) + "@" + c.a.ToString("0.00");
    }
}
