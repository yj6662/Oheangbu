using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 widget vocabulary (IMPLEMENTATION §4.1), added to the existing V (PlaytestUiView). Coordinates follow the
    /// mockups: (x, y) = top-left in px of the parent (1920x1080 page, y down), exactly like V.Rect. HUD elements use
    /// RectAnchored / PlaceAnchored (same mockup px, anchored to a screen corner). Rotations are in CSS degrees as written in
    /// the mockups (clockwise positive, e.g. the dab -14), converted to Unity z = -rotation, about the element's centre (CSS
    /// default transform-origin). Every call takes the UiStyle304SO explicitly (V.Style(Theme) / V.Style(skin)); a null style
    /// falls back to UiStyle304SO.Fallback (tokens only).</summary>
    public static partial class PlaytestUiView
    {
        static UiStyle304SO S(UiStyle304SO s) => s != null ? s : UiStyle304SO.Fallback;

        // ------------------------------------------------------------------ style / canvas / page
        /// <summary>The #304 style of a theme (theme.Style304) - the only accessor screens should use.</summary>
        public static UiStyle304SO Style(PlaytestUiThemeSO theme) => UiStyle304SO.Resolve(theme);
        /// <summary>The #304 style of a HUD skin (skin.Style304, same asset as the theme's).</summary>
        public static UiStyle304SO Style(WorldMacroHudSkinProfileSO skin) => UiStyle304SO.Resolve(skin);

        /// <summary>Adds TexCoord1 to the canvas (and its root) so InkReveal / InkMeter receive their per-vertex payload.
        /// Idempotent; InkRevealEffect and InkMeterGraphic also call it on enable.</summary>
        public static void EnsureCanvasChannels(Canvas canvas)
        {
            if (canvas == null) return;
            const AdditionalCanvasShaderChannels need = AdditionalCanvasShaderChannels.TexCoord1;
            if ((canvas.additionalShaderChannels & need) != need) canvas.additionalShaderChannels |= need;
            var root = canvas.rootCanvas;
            if (root != null && root != canvas && (root.additionalShaderChannels & need) != need) root.additionalShaderChannels |= need;
        }

        /// <summary>1920x1080 page root, centre-anchored, scaled by UiPageFit304 to min(1, W/1920, H/1080), with a CanvasGroup.
        /// Children use V.Rect top-left coordinates of the mockups. Replaces the 1560x900 Folio (IMPLEMENTATION §4.3).</summary>
        public static RectTransform Page304(Transform parent, string name = "Page304")
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(UiPageFit304.Width, UiPageFit304.Height);
            go.AddComponent<UiPageFit304>();
            go.AddComponent<CanvasGroup>();   // FocusMark304.Suspend(page, true) while a confirm dialog is open
            return r;
        }

        /// <summary>Moves a V.Rect-style rect (pivot top-left) to mockup (x, y).</summary>
        public static void Place(RectTransform r, float x, float y) { if (r != null) r.anchoredPosition = new Vector2(x, -y); }

        /// <summary>Rect whose pivot is its centre, placed so its unrotated top-left is (x, y); rotation in CSS degrees.</summary>
        public static RectTransform CenteredRect(string name, Transform parent, float x, float y, float width, float height, float rotation = 0f)
        {
            var r = Rect(name, parent, x, y, width, height);
            r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = new Vector2(x + width * .5f, -(y + height * .5f));
            if (rotation != 0f) r.localRotation = Quaternion.Euler(0f, 0f, -rotation);
            return r;
        }

        // ------------------------------------------------------------------ anchored (HUD) rects
        /// <summary>Anchor point (0..1, Unity y up) of a UiAnchor304.</summary>
        public static Vector2 AnchorPoint(UiAnchor304 anchor)
        {
            switch (anchor)
            {
                case UiAnchor304.TopCentre: return new Vector2(.5f, 1f);
                case UiAnchor304.TopRight: return new Vector2(1f, 1f);
                case UiAnchor304.MiddleLeft: return new Vector2(0f, .5f);
                case UiAnchor304.Centre: return new Vector2(.5f, .5f);
                case UiAnchor304.MiddleRight: return new Vector2(1f, .5f);
                case UiAnchor304.BottomLeft: return new Vector2(0f, 0f);
                case UiAnchor304.BottomCentre: return new Vector2(.5f, 0f);
                case UiAnchor304.BottomRight: return new Vector2(1f, 0f);
                default: return new Vector2(0f, 1f);
            }
        }

        /// <summary>Anchors `r` to a screen corner / edge and puts its unrotated top-left at mockup (x, y) of the 1920x1080
        /// reference: offsets are measured from that corner (left x, right x - 1920, centre x - 960; top y, bottom 1080 - y ...),
        /// so on 21:9 or 16:10 the element keeps its distance to ITS corner. pivot = rotation origin (default top-left;
        /// meters (0, .5) = CSS transform-origin 0 50%). Keeps sizeDelta.</summary>
        public static void PlaceAnchored(RectTransform r, float x, float y, UiAnchor304 anchor, Vector2? pivot = null)
        {
            if (r == null) return;
            Vector2 a = AnchorPoint(anchor), p = pivot ?? new Vector2(0f, 1f), size = r.sizeDelta;
            r.anchorMin = r.anchorMax = a; r.pivot = p;
            float px = x + p.x * size.x, py = y + (1f - p.y) * size.y;   // the pivot point in mockup px
            r.anchoredPosition = new Vector2(px - a.x * UiPageFit304.Width, -(py - (1f - a.y) * UiPageFit304.Height));
        }

        /// <summary>V.Rect for the HUD: w x h at mockup (x, y) anchored to `anchor` (see PlaceAnchored), rotation in CSS degrees
        /// about `pivot`. Use the spec's Anchor: Meter BottomLeft, Prompt BottomCentre, Bearing TopCentre, Toast TopRight,
        /// Arrival TopLeft. Children of the returned rect use plain V.Rect coordinates relative to it.</summary>
        public static RectTransform RectAnchored(string name, Transform parent, float x, float y, float w, float h, UiAnchor304 anchor,
            Vector2? pivot = null, float rotation = 0f)
        {
            var r = Rect(name, parent, 0f, 0f, w, h);
            PlaceAnchored(r, x, y, anchor, pivot);
            if (rotation != 0f) r.localRotation = Quaternion.Euler(0f, 0f, -rotation);
            return r;
        }

        // ------------------------------------------------------------------ text (TMP)
        /// <summary>TMP label sized to its preferred size at (0, 0) (IMPLEMENTATION signature). Move it with V.Place.</summary>
        public static TMP_Text Label(Transform parent, string name, string text, TypeRole role, Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
            => Label(null, parent, name, text, role, color, 0, 0, 0, 0, align, false);

        public static TMP_Text Label(Transform parent, string name, string text, TypeRole role, Color color,
            float x, float y, float width, float height, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool wrap = false)
            => Label(null, parent, name, text, role, color, x, y, width, height, align, wrap);

        public static TMP_Text Label(UiStyle304SO s, Transform parent, string name, string text, UiType304 role, Color color,
            float x, float y, float width = 0, float height = 0, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool wrap = false)
            => Label(s, parent, name, text, S(s).Role(role), color, x, y, width, height, align, wrap);

        /// <summary>TextMeshProUGUI with the role's font / size / line spacing / tracking / preset material. Raycast off,
        /// overflow Overflow, NoWrap unless `wrap` (Normal = 어절 wrapping via TMP Settings' modern Hangul rule).
        /// width or height 0 = the preferred size (for wrap give a width). Gets UiTextBaseSize so the text-scale rule applies.
        /// For dialogue pages set overflowMode = TextOverflowModes.Page yourself.</summary>
        public static TMP_Text Label(UiStyle304SO s, Transform parent, string name, string text, TypeRole role, Color color,
            float x, float y, float width = 0, float height = 0, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool wrap = false)
        {
            var rect = Rect(name, parent, x, y, Mathf.Max(width, 1f), Mathf.Max(height, 1f));
            var t = rect.gameObject.AddComponent<TextMeshProUGUI>();
            t.raycastTarget = false; t.richText = true;
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = align; t.color = color;
            UiText304.ApplyRole(t, role, s);
            t.text = text ?? "";
            if (width <= 0f || height <= 0f)
            {
                Vector2 pref = wrap && width > 0f ? t.GetPreferredValues(t.text, width, 0f) : t.GetPreferredValues(t.text);
                rect.sizeDelta = new Vector2(width > 0f ? width : Mathf.Ceil(pref.x), height > 0f ? height : Mathf.Ceil(pref.y));
            }
            return t;
        }

        /// <summary>조각 글자 row (toast fragments, DESIGN §5.10): one label per glyph `gap` px apart (ToastSpec.GlyphGap), at most
        /// `max` glyphs, then "+N" in overflowRole (Meta20 Mist) when there are more. (x, y) = top-left; right = right edge of the
        /// row in parent px (feed it to ToastSpec304.Layout). Children "Glyph0..", "More".</summary>
        public static RectTransform GlyphRow(UiStyle304SO s, Transform parent, string name, string glyphs, int max, UiType304 role, Color color,
            float x, float y, float gap, out float right, UiType304 overflowRole = UiType304.Meta20, Color? overflowColor = null)
        {
            s = S(s);
            string shown = UiNotice304.SplitGlyphs(glyphs, Mathf.Max(0, max), out string more);
            var root = Rect(name, parent, x, y, 1f, 1f);
            float cx = 0f, hMax = 0f;
            for (int i = 0; i < shown.Length; i++)
            {
                var t = Label(s, root, "Glyph" + i, shown[i].ToString(), role, color, cx, 0f);
                cx += t.rectTransform.sizeDelta.x + gap; hMax = Mathf.Max(hMax, t.rectTransform.sizeDelta.y);
            }
            if (shown.Length > 0) cx -= gap;
            if (!string.IsNullOrEmpty(more))
            {
                var m = Label(s, root, "More", more, overflowRole, overflowColor ?? s.Mist, 0f, 0f);
                float mx = cx + gap * .5f;
                Place(m.rectTransform, mx, Mathf.Max(0f, hMax - m.rectTransform.sizeDelta.y - 4f));
                cx = mx + m.rectTransform.sizeDelta.x;
            }
            root.sizeDelta = new Vector2(Mathf.Max(1f, cx), Mathf.Max(1f, hMax));
            right = x + cx;
            return root;
        }

        // ------------------------------------------------------------------ strokes
        /// <summary>Sprite of a stroke class from the style bundle.</summary>
        public static Sprite StrokeSprite(UiStyle304SO s, StrokeClass304 cls)
        {
            s = S(s);
            var spec = s.Stroke(cls); if (spec.Sprite != null) return spec.Sprite;
            var b = s.Sprites;
            switch (cls)
            {
                case StrokeClass304.WetS: return b.WetS; case StrokeClass304.WetM: return b.WetM; case StrokeClass304.Band: return b.Band;
                case StrokeClass304.Dry: return b.Dry; case StrokeClass304.Line: return b.Line; case StrokeClass304.Hair: return b.Hair;
                case StrokeClass304.HairRim: return b.HairRim; case StrokeClass304.Swell: return b.Swell; case StrokeClass304.Short: return b.Short;
                case StrokeClass304.Spine: return b.Spine; case StrokeClass304.Sweep: return b.Sweep; default: return b.WetS;
            }
        }

        /// <summary>Width rule (IMPLEMENTATION §2): w = contentRight - x + R x (h / nativeH) + 12, so all content sits on the
        /// solid body and the splitting bristles stay inside the right slice.</summary>
        public static float StrokeWidth(UiStyle304SO s, StrokeClass304 cls, float x, float h, float contentRight)
        {
            s = S(s); if (cls == StrokeClass304.Auto) cls = s.StrokeFor(h);
            var spec = s.Stroke(cls);
            return contentRight - x + spec.BorderR * (h / Mathf.Max(1f, spec.NativeH)) + 12f;
        }

        /// <summary>Content-fitted underlay stroke (받침 획): Sliced Image of class WetS / WetM / Band (Auto = from h; heights
        /// 118~124 are ambiguous, pass the class), pixelsPerUnitMultiplier = nativeH / h, width from StrokeWidth. Shared InkReveal
        /// material + InkRevealEffect (Wipe, reveal 1). Pivot is the centre (press scaleY and rotation are symmetric).
        /// tint.a x alpha = final alpha.</summary>
        public static Image Stroke(UiStyle304SO s, Transform parent, string name, StrokeClass304 cls, Color tint,
            float x, float y, float h, float contentRight, float alpha = 1f)
        {
            s = S(s); if (cls == StrokeClass304.Auto) cls = s.StrokeFor(h);
            float w = Mathf.Max(1f, StrokeWidth(s, cls, x, h, contentRight));
            return Brush(s, parent, name, cls, tint, x, y, w, h, alpha);
        }

        /// <summary>Any stroke class at an explicit rect (dry dividers and under-strokes, rail line, hair, swell, short, spine,
        /// sweep, or a sliced class at a fixed width). Sliced classes still get the height multiplier.</summary>
        public static Image Brush(UiStyle304SO s, Transform parent, string name, StrokeClass304 cls, Color tint,
            float x, float y, float w, float h, float alpha = 1f, float rotation = 0f, bool flipX = false, InkRevealMode reveal = InkRevealMode.Wipe)
        {
            s = S(s); if (cls == StrokeClass304.Auto) cls = s.StrokeFor(h);
            var spec = s.Stroke(cls);
            var rect = CenteredRect(name, parent, x, y, w, h, rotation);
            if (flipX) rect.localScale = new Vector3(-1f, 1f, 1f);
            var img = rect.gameObject.AddComponent<Image>();
            img.sprite = StrokeSprite(s, cls); img.raycastTarget = false; img.preserveAspect = false;
            tint.a *= alpha; img.color = tint;
            if (spec.Sliced && img.sprite != null)
            { img.type = UnityEngine.UI.Image.Type.Sliced; img.fillCenter = true; img.pixelsPerUnitMultiplier = Mathf.Max(.01f, spec.NativeH / Mathf.Max(1f, h)); }
            else img.type = UnityEngine.UI.Image.Type.Simple;
            InkRevealEffect.On(img, s, reveal, 1f);
            return img;
        }

        // ------------------------------------------------------------------ keycaps
        /// <summary>건반 (DESIGN §5.8). filled = keycap.png Sliced (한지 면, 2px 먹 테, 5px 턱): the screen's main action.
        /// hollow = 2px rim (paper on veil / ink when onPaper) + lip α.28 / .2: back-out and shortcuts.
        /// Height 40 (small 34), width max(40, text + 20) (small max(34, text + 16)). Returns the root "Key_&lt;label&gt;".</summary>
        public static RectTransform Keycap(UiStyle304SO s, Transform parent, string label, bool filled, bool onPaper = false, bool small = false, float x = 0f, float y = 0f)
        {
            s = S(s); var k = s.Keycap;
            float h = small ? k.SmallHeight : k.Height;
            var root = Rect("Key_" + label, parent, x, y, h, h);
            var text = Label(s, root, "Label", label, small ? UiType304.KeycapSm16 : UiType304.Keycap18,
                filled || onPaper ? s.Ink : s.Paper, 0, 0, h, h - k.TextLift, TextAlignmentOptions.Center, false);
            text.gameObject.AddComponent<UiTextNoScale304>(); text.fontSize = s.Role(small ? UiType304.KeycapSm16 : UiType304.Keycap18).Size;
            float textW = Mathf.Ceil(UiText304.Preferred(text, label).x);
            float w = Mathf.Max(small ? k.SmallMinWidth : k.MinWidth, textW + 2f * (small ? k.SmallPadX : k.PadX));
            root.sizeDelta = new Vector2(w, h); text.rectTransform.sizeDelta = new Vector2(w, h - k.TextLift);
            if (filled && s.Sprites.Keycap != null)
            {
                var face = Image(Stretch("Face", root), Color.white, s.Sprites.Keycap);
                face.type = UnityEngine.UI.Image.Type.Sliced; face.pixelsPerUnitMultiplier = 1f; face.transform.SetAsFirstSibling();
            }
            else
            {
                Color rim = filled || onPaper ? s.Ink : s.Paper;
                if (filled) Image(Stretch("Face", root), s.Paper).transform.SetAsFirstSibling();
                float lipPx = filled ? 5f : k.HollowLipPx;
                Color lip = filled ? s.KeyLip : UiStyle304SO.A(rim, onPaper ? k.HollowLipAlphaPaper : k.HollowLipAlphaVeil);
                Image(Rect("Lip", root, k.RimPx, h - k.RimPx - lipPx, w - 2f * k.RimPx, lipPx), lip);
                Image(Rect("RimTop", root, 0, 0, w, k.RimPx), rim);
                Image(Rect("RimBottom", root, 0, h - k.RimPx, w, k.RimPx), rim);
                Image(Rect("RimLeft", root, 0, k.RimPx, k.RimPx, h - 2f * k.RimPx), rim);
                Image(Rect("RimRight", root, w - k.RimPx, k.RimPx, k.RimPx, h - 2f * k.RimPx), rim);
            }
            return root;
        }

        /// <summary>Keycap width without building it (layout).</summary>
        public static float KeycapWidth(UiStyle304SO s, TMP_Text measure, string label, bool small)
        {
            s = S(s); var k = s.Keycap;
            float textW = measure != null ? Mathf.Ceil(UiText304.Preferred(measure, label).x) : label.Length * (small ? 9f : 10f);
            return Mathf.Max(small ? k.SmallMinWidth : k.MinWidth, textW + 2f * (small ? k.SmallPadX : k.PadX));
        }

        /// <summary>건반 + 동사구 hint ("[Esc] 닫기", "[Q][좌클릭] 긋기"): keycaps then a Meta20 label, gap 10, vertically centred.
        /// Returns the root sized to its content. Put it right next to the action it explains (DESIGN §0.3).</summary>
        public static RectTransform Hint(UiStyle304SO s, Transform parent, string name, IReadOnlyList<string> keys, string label, Color labelColor,
            float x, float y, bool small = true, bool filled = true, bool onPaper = false, UiType304 labelRole = UiType304.Meta20)
        {
            s = S(s); var k = s.Keycap;
            float h = small ? k.SmallHeight : k.Height;
            var root = Rect(name, parent, x, y, 10, h);
            float cx = 0f;
            if (keys != null)
                foreach (var key in keys)
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    var cap = Keycap(s, root, key, filled, onPaper, small, cx, 0f);
                    cx += cap.sizeDelta.x + k.LabelGap;
                }
            if (!string.IsNullOrEmpty(label))
            {
                var t = Label(s, root, "Label", label, labelRole, labelColor, cx, 0f, 0f, 0f);
                var size = t.rectTransform.sizeDelta; Place(t.rectTransform, cx, (h - size.y) * .5f);
                cx += size.x;
            }
            else cx = Mathf.Max(0f, cx - k.LabelGap);
            root.sizeDelta = new Vector2(Mathf.Max(1f, cx), h);
            return root;
        }

        // ------------------------------------------------------------------ focus marks
        /// <summary>방점 image (dab.png, cinnabar, 34x26, rotated -14 about its centre). (x, y) = top-left like the mockups' dab().
        /// A menu shows exactly one: use FocusMark304 (which owns it) rather than placing dabs by hand.</summary>
        public static Image Dab(UiStyle304SO s, Transform parent, float x = 0f, float y = 0f, float w = 0f, float h = 0f, float rotation = float.NaN, Color? color = null)
        {
            s = S(s);
            if (w <= 0f) w = s.DabSize.x; if (h <= 0f) h = s.DabSize.y;
            var r = CenteredRect("Dab", parent, x, y, w, h, float.IsNaN(rotation) ? s.DabRotation : rotation);
            return Image(r, color ?? s.Cinnabar, s.Sprites.Dab);
        }

        /// <summary>Where FocusMark304 puts the dab for a row: an empty rect with the dab's top-left (x, y) and size (0 = style DabSize).</summary>
        public static RectTransform DabAnchor(Transform parent, float x, float y, float w = 0f, float h = 0f, UiStyle304SO s = null)
        {
            var size = S(s).DabSize; if (w <= 0f) w = size.x; if (h <= 0f) h = size.y;
            return Rect("DabAnchor", parent, x, y, w, h);
        }

        /// <summary>선택 부풂 (stroke_swell, paper α.95): the selected tab / value / region. Never cinnabar.</summary>
        public static Image Swell(UiStyle304SO s, Transform parent, float x, float y, float w, float h, Color? tint = null, float alpha = .95f, string name = "Swell")
            => Brush(s, parent, name, StrokeClass304.Swell, tint ?? S(s).Paper, x, y, w, h, alpha);

        public static Image Swell(UiStyle304SO s, Transform parent, Rect rect, Color? tint = null, float alpha = .95f)
            => Swell(s, parent, rect.x, rect.y, rect.width, rect.height, tint, alpha);

        /// <summary>칸 초점 테: four stroke_short strokes crossing 12 px past the corners of `cell` (parent coordinates).
        /// Cinnabar while focused, Ink once focus moved on to the detail column (V.Tint). Returns the container.</summary>
        public static RectTransform CellFocus(UiStyle304SO s, Transform parent, Rect cell, Color color, string name = "CellFocus")
        {
            s = S(s);
            var root = Rect(name, parent, 0, 0, 0, 0);
            float x = cell.x, y = cell.y, w = cell.width, h = cell.height;
            Brush(s, root, "Top", StrokeClass304.Short, color, x - 12, y - 14, w + 26, 22);
            Brush(s, root, "Bottom", StrokeClass304.Short, color, x - 8, y + h - 6, w + 24, 22, 1f, 0f, true);
            Brush(s, root, "Left", StrokeClass304.Short, color, x - (h + 20) * .5f - 2, y + h * .5f - 10, h + 20, 20, 1f, 90f);
            Brush(s, root, "Right", StrokeClass304.Short, color, x + w - (h + 20) * .5f + 2, y + h * .5f - 10, h + 20, 20, 1f, -90f);
            return root;
        }

        /// <summary>Recolours every Graphic under root (CellFocus cinnabar -> ink, a hint turning paper, ...).</summary>
        public static void Tint(Transform root, Color color)
        {
            if (root == null) return;
            foreach (var g in root.GetComponentsInChildren<Graphic>(true)) g.color = color;
        }

        /// <summary>A complete focusable row: Button (transparent hit, Transition None) + TMP label + FocusUnderlay stroke (hidden
        /// until focus) + DabAnchor + FocusVisual304, optional keycap (FocusKeyMode304, own column via KeyX), extra focused key
        /// (FocusKey, e.g. dialogue [F]), meta on the label line (MetaX) or stacked under it (MetaBelow), an always-on base
        /// underlay shown only while unfocused (BaseUnderlay*, dialogue choices), the primary dry under-stroke and the disabled
        /// state (Disabled + DisabledReason). Row coordinates like V.Button (top-left x, y, w, h); every x / y in the spec is in
        /// ROW coordinates (mockup x - row x). The name is the harness name.</summary>
        public static FocusRow304 FocusRow(UiStyle304SO s, Transform parent, string name, string text, float x, float y, float w, float h,
            UnityAction clicked, FocusRowSpec304 spec = null)
        {
            s = S(s); spec ??= new FocusRowSpec304();
            var rect = Rect(name, parent, x, y, w, Mathf.Max(h, s.MinHit));
            h = rect.sizeDelta.y;
            var hit = Image(rect, new Color(0f, 0f, 0f, 0f), null, true);
            hit.canvasRenderer.cullTransparentMesh = true;   // raycasts, draws nothing (keeps the InkReveal batch intact)
            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None; button.targetGraphic = hit;
            if (clicked != null) button.onClick.AddListener(clicked);
            if (spec.SoundTheme != null && spec.SoundTheme.SoundPalette != null) rect.gameObject.AddComponent<CompactUiSound255>().Theme = spec.SoundTheme;

            // ---- label; measured in the focused look too so the underlay always covers the wider of the two
            var role = s.Role(spec.Role);
            var focusRole = spec.FocusRole.HasValue ? s.Role(spec.FocusRole.Value) : null;
            Color labelNormal = spec.LabelColor ?? (spec.OnPaper ? s.Ink : s.Paper);
            Color labelFocus = spec.LabelFocusColor ?? (spec.OnPaper ? s.Paper : s.Ink);
            var label = Label(s, rect, "Label", text, role, labelNormal, spec.LabelX, 0f, 0f, 0f, TextAlignmentOptions.Left);
            TMP_FontAsset focusFont = null;
            if (focusRole != null) focusFont = s.FontOf(focusRole);
            else if (spec.FocusBold) { var bold = BolderFamily(role.Family); if (bold.HasValue) focusFont = s.FontFor(bold.Value); }
            if (focusFont == label.font) focusFont = null;
            float focusSize = focusRole != null && !Mathf.Approximately(focusRole.Size, role.Size) ? focusRole.Size : 0f;
            Vector2 ls = label.rectTransform.sizeDelta;
            if (focusFont != null || focusSize > 0f)
                ls = Vector2.Max(ls, MeasureFocused(label, text, focusFont, focusSize > 0f ? focusSize * label.fontSize / Mathf.Max(1f, role.Size) : 0f));
            label.rectTransform.sizeDelta = ls;

            // ---- meta (inline on the label line, or stacked under the label); a disabled row shows its reason there
            bool reasonShown = spec.Disabled && !string.IsNullOrEmpty(spec.DisabledReason);
            string metaText = reasonShown ? spec.DisabledReason : spec.Meta;
            bool below = spec.MetaBelow || (reasonShown && float.IsNaN(spec.MetaX));
            Color metaNormal = spec.MetaColor ?? (spec.OnPaper ? s.Ash : s.Mist), metaFocused = spec.MetaFocusColor ?? spec.MetaColor ?? (spec.OnPaper ? s.Mist : s.Ash);
            TMP_Text meta = null; Vector2 ms = Vector2.zero;
            if (!string.IsNullOrEmpty(metaText)) { meta = Label(s, rect, "Meta", metaText, spec.MetaRole, metaNormal, 0f, 0f); ms = meta.rectTransform.sizeDelta; }
            if (meta == null) below = false;

            // ---- vertical layout: the label (+ stacked meta) is centred in the row unless LabelY is given
            float blockH = below ? ls.y + spec.MetaGap + ms.y : ls.y;
            float labelY = float.IsNaN(spec.LabelY) ? (h - blockH) * .5f : spec.LabelY;
            Place(label.rectTransform, spec.LabelX, labelY);
            float labelMid = labelY + ls.y * .5f;
            float blockMid = below ? labelY + blockH * .5f : labelMid;
            float right = spec.LabelX + ls.x;

            // ---- keys
            float kh = spec.KeySmall ? s.Keycap.SmallHeight : s.Keycap.Height;
            float kx = float.IsNaN(spec.KeyX) ? right + spec.KeyGap : spec.KeyX;
            float ky = float.IsNaN(spec.KeyY) ? blockMid - kh * .5f : spec.KeyY;
            bool focusKeyInSlot = !string.IsNullOrEmpty(spec.FocusKey) && float.IsNaN(spec.FocusKeyX);
            RectTransform keyNormal = null, keyFocused = null, focusKey = null;
            if (!string.IsNullOrEmpty(spec.Key))
            {
                switch (spec.KeyMode)
                {
                    case FocusKeyMode304.FilledUntilFocused:
                        keyNormal = Keycap(s, rect, spec.Key, true, spec.OnPaper, spec.KeySmall, kx, ky);
                        if (!focusKeyInSlot) keyFocused = Keycap(s, rect, spec.Key, false, !spec.OnPaper, spec.KeySmall, kx, ky);
                        break;
                    case FocusKeyMode304.Hollow:
                        keyNormal = Keycap(s, rect, spec.Key, false, spec.OnPaper, spec.KeySmall, kx, ky);
                        if (!focusKeyInSlot) keyFocused = Keycap(s, rect, spec.Key, false, !spec.OnPaper, spec.KeySmall, kx, ky);
                        break;
                    case FocusKeyMode304.Filled:
                        keyNormal = Keycap(s, rect, spec.Key, true, spec.OnPaper, spec.KeySmall, kx, ky);
                        break;
                    case FocusKeyMode304.FilledWhenFocused:
                        keyFocused = Keycap(s, rect, spec.Key, true, !spec.OnPaper, spec.KeySmall, kx, ky);
                        break;
                }
            }
            if (!string.IsNullOrEmpty(spec.FocusKey))
                focusKey = Keycap(s, rect, spec.FocusKey, true, !spec.OnPaper, spec.KeySmall, float.IsNaN(spec.FocusKeyX) ? kx : spec.FocusKeyX, ky);
            if (keyNormal != null) { keyNormal.name = "Key"; right = Mathf.Max(right, keyNormal.anchoredPosition.x + keyNormal.sizeDelta.x); }
            if (keyFocused != null) { keyFocused.name = "KeyFocused"; right = Mathf.Max(right, keyFocused.anchoredPosition.x + keyFocused.sizeDelta.x); }
            if (focusKey != null) { focusKey.name = "FocusKey"; right = Mathf.Max(right, focusKey.anchoredPosition.x + focusKey.sizeDelta.x); }

            if (meta != null)
            {
                float mx, my;
                if (below) { mx = float.IsNaN(spec.MetaX) ? spec.LabelX : spec.MetaX; my = float.IsNaN(spec.MetaY) ? labelY + ls.y + spec.MetaGap : spec.MetaY; }
                else { mx = float.IsNaN(spec.MetaX) ? right + 24f : spec.MetaX; my = float.IsNaN(spec.MetaY) ? labelMid - ms.y * .5f : spec.MetaY; }
                Place(meta.rectTransform, mx, my);
                right = Mathf.Max(right, mx + ms.x);
            }

            // ---- underlays: FocusUnderlay (drawn on focus) and the optional BaseUnderlay (shown while unfocused)
            float uh = spec.UnderlayH > 0f ? spec.UnderlayH : Mathf.Max(76f, role.Size * 2.8f, below ? blockH * 1.75f : 0f);
            float ux = float.IsNaN(spec.UnderlayX) ? spec.LabelX - .62f * uh : spec.UnderlayX;
            float uy = float.IsNaN(spec.UnderlayY) ? blockMid - uh * .5f : spec.UnderlayY;
            float cr = float.IsNaN(spec.ContentRight) ? right + 24f : spec.ContentRight;
            var underlay = Stroke(s, rect, "FocusUnderlay", spec.Underlay, spec.UnderlayColor ?? (spec.OnPaper ? s.Ink : s.Paper), ux, uy, uh, cr, spec.UnderlayAlpha);
            underlay.transform.SetAsFirstSibling();
            var fx = underlay.GetComponent<InkRevealEffect>(); if (fx != null) fx.Reveal = 0f;

            Image baseUnder = null;
            if (spec.BaseUnderlayH > 0f)
            {
                float bh = spec.BaseUnderlayH;
                float bx = float.IsNaN(spec.BaseUnderlayX) ? spec.LabelX - .62f * bh : spec.BaseUnderlayX;
                float bcr = float.IsNaN(spec.BaseContentRight) ? cr : spec.BaseContentRight;
                baseUnder = Stroke(s, rect, "BaseUnderlay", spec.BaseUnderlay, spec.BaseUnderlayColor ?? s.Ink, bx, blockMid - bh * .5f, bh, bcr, spec.BaseUnderlayAlpha);
                baseUnder.transform.SetAsFirstSibling();   // below the focus underlay
            }

            Image under = null;
            if (spec.UnderStroke)
                under = Brush(s, rect, "UnderStroke", StrokeClass304.Dry, spec.OnPaper ? s.Ink : s.Paper, spec.LabelX - 10f, labelY + ls.y + 2f, spec.UnderStrokeW, 24f, spec.UnderStrokeAlpha);

            Vector2 ds = spec.DabSize.x > 0f ? spec.DabSize : s.DabSize;
            var anchor = DabAnchor(rect, spec.LabelX - spec.DabGap - ds.x, labelMid - ds.y * .5f + spec.DabDy, ds.x, ds.y, s);

            // ---- visual state
            var visual = rect.gameObject.AddComponent<FocusVisual304>();
            if (meta != null) { visual.Meta = meta; visual.MetaNormal = metaNormal; visual.MetaFocused = metaFocused; }
            visual.LabelDisabled = spec.LabelDisabledColor ?? s.Off;
            visual.FocusSize = focusSize;
            if (keyFocused != null) visual.ShowWhenFocused.Add(keyFocused.gameObject);
            if (focusKey != null) visual.ShowWhenFocused.Add(focusKey.gameObject);
            if (keyNormal != null && (keyFocused != null || focusKeyInSlot)) visual.HideWhenFocused.Add(keyNormal.gameObject);
            if (baseUnder != null) { visual.HideWhenFocused.Add(baseUnder.gameObject); visual.HideWhenDisabled.Add(baseUnder.gameObject); }
            if (under != null) { visual.HideWhenFocused.Add(under.gameObject); visual.DimWhenDisabled.Add(under); }
            foreach (var key in new[] { keyNormal, keyFocused, focusKey }) if (key != null) visual.HideWhenDisabled.Add(key.gameObject);
            if (spec.Disabled) button.interactable = false;
            visual.Bind(s, underlay, label, labelNormal, labelFocus, anchor, focusFont);
            return new FocusRow304
            {
                Button = button, Rect = rect, Label = label, Meta = meta, Underlay = underlay, BaseUnderlay = baseUnder, Reveal = fx, DabAnchor = anchor,
                Visual = visual, Key = keyNormal, KeyFocused = keyFocused, FocusKey = focusKey, UnderStroke = under,
            };
        }

        /// <summary>The weight a focused label switches to (DESIGN §5.0 초점 = 먹 글자 800): Serif600/700 -> Serif800, Sans400 -> Sans700,
        /// null = already heavy enough (Serif800/900, Sans700, Prose keeps its face).</summary>
        public static UiFont304? BolderFamily(UiFont304 family)
        {
            switch (family)
            {
                case UiFont304.Serif600: case UiFont304.Serif700: return UiFont304.Serif800;
                case UiFont304.Sans400: return UiFont304.Sans700;
                default: return null;
            }
        }

        static Vector2 MeasureFocused(TMP_Text t, string text, TMP_FontAsset font, float size)
        {
            var font0 = t.font; float size0 = t.fontSize; var material0 = t.fontSharedMaterial;
            if (font != null) t.font = font;
            if (size > 0f) t.fontSize = size;
            Vector2 p = t.GetPreferredValues(text ?? "");
            if (font != null) { t.font = font0; if (material0 != null) t.fontSharedMaterial = material0; }
            t.fontSize = size0;
            return new Vector2(Mathf.Ceil(p.x), Mathf.Ceil(p.y));
        }

        // ------------------------------------------------------------------ surfaces
        /// <summary>Page px of solid veil body added LEFT of veil_wash (the texture's first column, clamped): covers the canvas
        /// margins a scaled page leaves on 21:9 / 32:9 screens and at UI 배율 1.3.</summary>
        public const float VeilLead = 2000f;

        /// <summary>먹장막 (DESIGN §5.7): RawImage veil_wash tinted Veil whose brush lifts at page x = liftX (texture x2000). The rect
        /// runs from liftX - 2000 - VeilLead to liftX + 1200 (uvRect starts at -VeilLead/3200, Clamp = solid body on the left)
        /// and UiVeilFit304 stretches it vertically when the canvas is taller than the scaled page (16:10, 4:3), so no bare
        /// world shows around a page. InkReveal Wipe for the entrance, raycast ON (blocks the world). Optional 먹 위의 먹:
        /// stroke_sweep 1700x620 at sweep.(x, y) alpha sweep.z rotated -8. Returns "Veil304"; the sweep is the sibling "VeilSweep".
        /// Parent it under V.Page304.</summary>
        public static RawImage Veil(UiStyle304SO s, Transform parent, float liftX, Vector3? sweep = null)
        {
            s = S(s);
            const float texW = 3200f;
            var rect = Rect("Veil304", parent, liftX - 2000f - VeilLead, 0f, texW + VeilLead, UiPageFit304.Height);
            var raw = Raw(rect, s.Sprites.VeilWash, s.Veil, true);
            raw.uvRect = new Rect(-VeilLead / texW, 0f, (texW + VeilLead) / texW, 1f);
            rect.gameObject.AddComponent<UiVeilFit304>();
            InkRevealEffect.On(raw, s, InkRevealMode.Wipe, 1f);
            if (sweep.HasValue && sweep.Value.z > 0f)
                Brush(s, parent, "VeilSweep", StrokeClass304.Sweep, s.Ink, sweep.Value.x, sweep.Value.y, 1700f, 620f, sweep.Value.z, -8f);
            return raw;
        }

        /// <summary>Veil for a PlaytestUiRoot page id using the style's VeilLiftX table (일시정지 1100, 옵션 1700, 소지품 1960 ...).</summary>
        public static RawImage Veil(UiStyle304SO s, Transform parent, string page)
        {
            var v = S(s).VeilFor(page);
            return Veil(s, parent, v.LiftX, v.Sweep ? v.SweepXYA : (Vector3?)null);
        }

        /// <summary>Uniform dim (확인 대화상자 뒤 장막 α.66, or any flat veil), raycast on. Stretched over `parent` (use a canvas-wide
        /// parent such as the Confirmation root under canvasRect, not the scaled page).</summary>
        public static Image Dim(UiStyle304SO s, Transform parent, float alpha = -1f, string name = "Shade")
        {
            s = S(s);
            return Image(Stretch(name, parent), UiStyle304SO.A(s.Veil, alpha >= 0f ? alpha : s.ConfirmDimAlpha), null, true);
        }

        /// <summary>Sprite image at a rect; Sliced automatically when the sprite has borders (sheets, keycap, dash box).
        /// rotation = CSS degrees about the centre; ppuMultiplier scales the borders (1 = native px).</summary>
        public static Image SpriteImage(Transform parent, string name, Sprite sprite, Color color, float x, float y, float w, float h,
            float rotation = 0f, float ppuMultiplier = 1f, bool hit = false)
        {
            var r = rotation != 0f ? CenteredRect(name, parent, x, y, w, h, rotation) : Rect(name, parent, x, y, w, h);
            var img = Image(r, color, sprite, hit);
            if (sprite != null && sprite.border.sqrMagnitude > 0f)
            { img.type = UnityEngine.UI.Image.Type.Sliced; img.pixelsPerUnitMultiplier = Mathf.Max(.01f, ppuMultiplier); }
            return img;
        }

        /// <summary>부인(符印): seal_bu (fixed colour) square of `size` at (x, y), rotation in CSS degrees.</summary>
        /// <summary>부인 seal. Returns null (draws nothing) while UiStyle304SO.ShowSeals is off (user request 2026-09-30).</summary>
        public static Image Seal(UiStyle304SO s, Transform parent, float x, float y, float size, float rotation = 0f)
            => S(s).ShowSeals ? SpriteImage(parent, "SealBu", S(s).Sprites.SealBu, Color.white, x, y, size, size, rotation) : null;

        /// <summary>오행 형상 도장 0..4 = 木 火 土 金 水, tinted (Ink on paper, Paper on veil).</summary>
        public static Image Element(UiStyle304SO s, Transform parent, int element, float x, float y, float size, Color tint)
            => SpriteImage(parent, "Element_" + element, S(s).Sprites.Element(element), tint, x, y, size, size);

        /// <summary>Round mark (disc.png): 획순 번호 원 24 Cinnabar, 남긴 통보 먹 점 18 (put a 22 Sheet disc behind it for the rim),
        /// 목적 고리 채움 (Cinnabar α.07). (x, y) = top-left of the size x size box. One of the few round shapes DESIGN §4 allows.</summary>
        public static Image Disc(UiStyle304SO s, Transform parent, float x, float y, float size, Color color, string name = "Disc")
            => SpriteImage(parent, name, S(s).Sprites.Disc, color, x, y, size, size);

        /// <summary>들은 목적 권역 dashed ring (ring_dashed.png): map 140 Cinnabar (+ a Disc Cinnabar α.07 fill), legend 36, list 30
        /// (CinnabarLift on the veil). (x, y) = top-left of the box.</summary>
        public static Image RingDashed(UiStyle304SO s, Transform parent, float x, float y, float size, Color color, string name = "RingDashed")
            => SpriteImage(parent, name, S(s).Sprites.RingDashed, color, x, y, size, size);

        // ------------------------------------------------------------------ rail / meter
        /// <summary>탭 레일 (DESIGN §5.3): Q key, one Tab_&lt;page&gt; button per reachable page (fixed slot width = selected width),
        /// E key, then the [closeKey] 닫기 row named "Close". Pass only pages that are reachable now (정비 only at AtDemoShop).
        /// label(page) gives the display text (PlaytestUiRoot.MenuLabel). fromPage = the page shown before this rebuild (tab
        /// change): the rail is built on it and then slides the swell + tweens the two tab labels to selectedPage (220 ms).
        /// Pause has no rail.</summary>
        public static MenuRail304 MenuRail(UiStyle304SO s, Transform parent, string selectedPage, IReadOnlyList<string> reachablePages, string closeKey,
            Func<string, string> label, Action<string> open, Action close, string closeLabel = "닫기", string fromPage = null)
            => MenuRail304.Create(s, parent, selectedPage, reachablePages, closeKey, label, open, close, closeLabel, fromPage);

        /// <summary>4-layer InkMeter304 at (x, y) with full length w and height h (HP 500x42 cinnabar + lag, 먹 420x30 ink, 로딩 640x24 paper).</summary>
        public static InkMeter304 Meter(UiStyle304SO s, Transform parent, string name, float x, float y, float w, float h, Color valueColor, bool withLag = false)
            => InkMeter304.Create(s, parent, name, x, y, w, h, valueColor, withLag);
    }

    /// <summary>Options of V.FocusRow. Leave NaN / 0 / null for the automatic value. Every x / y is in ROW coordinates.</summary>
    public sealed class FocusRowSpec304
    {
        [Tooltip("row sits on a paper sheet: ink label -> paper label on an INK underlay (DESIGN §5.0)")] public bool OnPaper;
        public UiType304 Role = UiType304.Label28;
        [Tooltip("focused label role (font + size), e.g. dialogue choice Serif700_28 -> Title30. null = FocusBold rule")] public UiType304? FocusRole;
        [Tooltip("without FocusRole: Serif600/700 labels switch to Serif800 (Sans400 -> Sans700) while focused")] public bool FocusBold = true;
        public Color? LabelColor, LabelFocusColor;
        [Tooltip("disabled label colour (null = Off #6E6B64)")] public Color? LabelDisabledColor;
        public float LabelX = 40f;
        [Tooltip("NaN = label (+ stacked meta) centred in the row")] public float LabelY = float.NaN;

        [Header("focus underlay (drawn on focus)")]
        [Tooltip("Auto = from UnderlayH; heights 118~124 are ambiguous: options rows = WetM, dialogue choices = WetS")] public StrokeClass304 Underlay = StrokeClass304.Auto;
        [Tooltip("null = Paper on the veil, Ink on paper")] public Color? UnderlayColor;
        [Tooltip("0 = max(76, role size x 2.8, stacked block x 1.75); primary buttons: size x 3.4 (44 -> 150)")] public float UnderlayH;
        [Tooltip("NaN = LabelX - .62 x underlay height")] public float UnderlayX = float.NaN;
        [Tooltip("NaN = centred on the label (or label + stacked meta)")] public float UnderlayY = float.NaN;
        [Tooltip("NaN = right edge of label / keys / meta + 24")] public float ContentRight = float.NaN;
        public float UnderlayAlpha = 1f;

        [Header("base underlay (shown while unfocused, e.g. dialogue choice: ink wet_s h112 α.92)")]
        [Tooltip("0 = none")] public float BaseUnderlayH;
        public StrokeClass304 BaseUnderlay = StrokeClass304.Auto;
        [Tooltip("null = Ink")] public Color? BaseUnderlayColor;
        [Tooltip("NaN = LabelX - .62 x base height")] public float BaseUnderlayX = float.NaN;
        [Tooltip("NaN = the focus underlay's content right")] public float BaseContentRight = float.NaN;
        public float BaseUnderlayAlpha = .92f;

        [Header("방점")]
        [Tooltip("gap between the dab's right edge and the label")] public float DabGap = 14f;
        [Tooltip("dab centre below the label centre (the mockups sit it 2~9 px low)")] public float DabDy = 4f;
        [Tooltip("0 = style DabSize 34x26; small rows 28x21")] public Vector2 DabSize;

        [Header("keys")]
        [Tooltip("keycap label (Esc, Enter, I, F ...); null = none")] public string Key;
        public FocusKeyMode304 KeyMode = FocusKeyMode304.FilledUntilFocused;
        public bool KeySmall = true;
        [Tooltip("gap after the label when KeyX is NaN")] public float KeyGap = 16f;
        [Tooltip("fixed key column (pause shortcuts: mockup 742 - row x); NaN = after the label")] public float KeyX = float.NaN;
        [Tooltip("NaN = centred on the label block")] public float KeyY = float.NaN;
        [Tooltip("extra FILLED key shown only while focused (dialogue [F]); with FocusKeyX NaN it takes Key's slot and Key hides while focused")]
        public string FocusKey;
        public float FocusKeyX = float.NaN;

        [Header("meta")]
        public string Meta;
        [Tooltip("inline: NaN = after label / key + 24. below: NaN = LabelX")] public float MetaX = float.NaN;
        [Tooltip("NaN = centred on the label line (inline) or label bottom + MetaGap (below)")] public float MetaY = float.NaN;
        [Tooltip("stack the meta UNDER the label (dialogue result meta, 저장 없음)")] public bool MetaBelow;
        public float MetaGap = 0f;
        public UiType304 MetaRole = UiType304.Meta20;
        [Tooltip("null = Mist on the veil / Ash on paper (unfocused) -> Ash / Mist on the underlay (focused). 적용 전 = Cinnabar both")]
        public Color? MetaColor, MetaFocusColor;

        [Header("primary button")]
        [Tooltip("primary button default state: dry under-stroke, hidden while focused, α x .32 while disabled")] public bool UnderStroke;
        public float UnderStrokeW = 220f, UnderStrokeAlpha = .7f;

        [Header("state")]
        [Tooltip("Selectable.interactable = false: Off label, no underlay, keys hidden (FocusVisual304.SetInteractable changes it later)")] public bool Disabled;
        [Tooltip("disabled reason meta (저장 없음, 수직동기화 사용 중); replaces Meta, stacked under the label unless MetaX is set")] public string DisabledReason;
        [Tooltip("adds CompactUiSound255 (focus / select cues) when the theme has a palette")] public PlaytestUiThemeSO SoundTheme;
    }

    /// <summary>Handles returned by V.FocusRow.</summary>
    public sealed class FocusRow304
    {
        public Button Button;
        public RectTransform Rect;
        public TMP_Text Label, Meta;
        public Image Underlay, BaseUnderlay, UnderStroke;
        public InkRevealEffect Reveal;
        public RectTransform DabAnchor, Key, KeyFocused, FocusKey;
        public FocusVisual304 Visual;
    }
}
