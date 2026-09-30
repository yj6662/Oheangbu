using System;
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 content area shared vocabulary (소지품 · 술식 도감 · 차패 · 장비 상점/강화 · 정비 · 여정의 끝): chips and cells
    /// with the 칸 초점 grammar (cinnabar 4-stroke frame + dab on focus, the same frame in ink once focus moved on to the
    /// detail), the D21 새 내용 flick, the D22 sealed stroke, the D04 coin, notices through UiNoticeChannelSO and focus restore
    /// after a page rebuild. Every member carries the Content304 prefix (partial class shared with other areas).</summary>
    public sealed partial class PlaytestUiRoot
    {
        const float Content304ChipSize = 104f;
        /// <summary>Name of the content element to select again after the page is rebuilt (OnCollected, GearAction, purchases).</summary>
        string content304Focus;

        UiStyle304SO Content304Style => V.Style(Theme);
        static ContentArt304 Content304Art => ContentArt304.Load();

        // ------------------------------------------------------------------ D21 새 내용 (seen registry)
        ContentSeen304 Content304Seen
        {
            get
            {
                if (!TryGetComponent(out ContentSeen304 seen)) seen = gameObject.AddComponent<ContentSeen304>();
                seen.Observe(Session);
                return seen;
            }
        }

        /// <summary>Hook for BindScene (menu / flow area): snapshots what the player already has, so D21 marks count only
        /// what arrives during this session. Without the call the snapshot is taken the first time a content page opens.</summary>
        void Content304SessionBound() { if (Session != null) { var _ = Content304Seen; } }

        /// <summary>For the rail (menu area, D21 tab mark): true while a content page holds an item not looked at yet.</summary>
        public bool Content304HasUnseen(string page)
        {
            if (Session == null || Session.Progress == null) return false;
            var seen = Content304Seen;
            if (page == "술식 도감")
            {
                foreach (var l in Session.Progress.ui.knownSpellLetters) if (seen.IsNew("spell:" + l)) return true;
                return false;
            }
            if (page == "소지품")
            {
                foreach (var i in Session.Progress.ui.items) if (i != null && i.count > 0 && seen.IsNew("item:" + i.id)) return true;
                if (Session.EquipmentEnabled && Session.Progress.equipment != null)
                    foreach (var o in Session.Progress.equipment.Owned) if (o != null && seen.IsNew("gear:" + o.Id)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ focus
        /// <summary>Selects `go` and remembers it as the element to restore after the next rebuild.</summary>
        void Content304Select(GameObject go)
        {
            if (go == null) return;
            content304Focus = go.name;
            FocusMark304.Select(go);
        }

        /// <summary>After a build: selects the remembered element when it exists under `root` (same page rebuilt), else `fallback`.</summary>
        void Content304Restore(Transform root, GameObject fallback)
        {
            var go = Content304FindSelectable(root, content304Focus);
            Content304Select(go != null ? go : fallback);
        }

        /// <summary>Remembers `row` as the element to restore when it gets selected (FocusRow buttons: GearEquip, Buy_*, ReadCodex ...).</summary>
        void Content304Note(Component row)
        {
            if (row == null) return;
            var note = row.gameObject.AddComponent<ContentFocusNote304>();
            note.Selected = go => content304Focus = go.name;
        }

        static GameObject Content304FindSelectable(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            foreach (var s in root.GetComponentsInChildren<Selectable>())
                if (s.name == name && s.interactable && s.gameObject.activeInHierarchy) return s.gameObject;
            return null;
        }

        // ------------------------------------------------------------------ cells and chips
        /// <summary>A focusable 칸 (장비 칩 / 도감 칸 / 석경 칩): transparent hit Button (Transition None) + FocusVisual304 without an
        /// underlay (hover selects, press drops the label) + ContentCell304 (cinnabar CellFocus while focused, ink CellFocus
        /// while it stays the page's selection with focus elsewhere, D21 flick). All rects are in the cell's own px.</summary>
        ContentCell304 Content304Cell(Transform parent, string name, float x, float y, float w, float h, Rect frame, Rect dab,
            UnityAction clicked, Action<ContentCell304> selected, bool onPaper)
        {
            var s = Content304Style;
            var rect = V.Rect(name, parent, x, y, w, Mathf.Max(h, s.MinHit));
            var hit = V.Image(rect, new Color(0f, 0f, 0f, 0f), null, true);
            hit.canvasRenderer.cullTransparentMesh = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None; button.targetGraphic = hit;
            if (clicked != null) button.onClick.AddListener(clicked);
            if (Theme != null && Theme.SoundPalette != null) rect.gameObject.AddComponent<CompactUiSound255>().Theme = Theme;
            var anchor = V.DabAnchor(rect, dab.x, dab.y, dab.width, dab.height, s);
            var visual = rect.gameObject.AddComponent<FocusVisual304>();
            visual.Bind(s, null, null, s.Paper, s.Paper, anchor);
            var cell = rect.gameObject.AddComponent<ContentCell304>();
            cell.Init(s, visual, frame, s.Ink, selected);   // kept = the same frame in 먹 (states.png "초점이 상세로 간 칸"), veil or paper
            return cell;
        }

        /// <summary>장비 칩 paper (tile_chip, fixed colour). Empty = α.16 (DESIGN §5.6) through UI/InkReveal (Content304Ink): on
        /// plain UI/Default the linear blend lifted the empty chip to #5E on the veil (mockup #33), almost a real chip.</summary>
        Image Content304Chip(Transform parent, string name, float x, float y, float size, bool empty)
        {
            var s = Content304Style;
            var chip = V.SpriteImage(parent, name, s.Sprites.TileChip, new Color(1f, 1f, 1f, empty ? .16f : 1f), x, y, size, size);
            if (s.Sprites.TileChip == null) chip.color = UiStyle304SO.A(s.Chip, empty ? .16f : 1f);
            if (empty) Content304Ink(chip, s);
            return chip;
        }

        // ------------------------------------------------------------------ translucent layers in linear colour space (#304 QA2)
        /// <summary>Routes a translucent PAPER or INK layer through the shared UI/InkReveal material (foundation InkRevealEffect.On,
        /// Wipe, reveal 1 = fully drawn), so its design alpha gets the shader's sRGB-composite remap (_LinearInkGamma: ink layers
        /// 1-(1-a)^g, paper layers a^g). UI/Default (V.SpriteImage / V.Image) blends in linear light and lifted these about 2x:
        /// empty chip #5E vs mockup #33, 묵등 #9E vs #64, detail rule #5E vs #31. Mid-tone tints (Mist, Ash, Off) must not use it:
        /// the shader's luminance split mixes both curves for them and lifts them further (Content304Precomposite instead);
        /// full-colour art (gear silhouettes) keeps UI/Default for the same reason. No material instance is created.</summary>
        static T Content304Ink<T>(T graphic, UiStyle304SO s) where T : Graphic
        {
            if (graphic != null) InkRevealEffect.On(graphic, s, InkRevealMode.Wipe, 1f);
            return graphic;
        }

        /// <summary>Opaque sRGB pre-composite of `over` at `alpha` on the surface colour `under` (the mockup's composite, which a
        /// linear blend cannot lift). For a mid-tone layer on a known surface: the pending 강화 방점 = Mist α.35 over the veil
        /// (#44, mockup #42; UI/Default gave #63, UI/InkReveal #6E). The sprite's own alpha still shapes it.</summary>
        static Color Content304Precomposite(Color under, Color over, float alpha)
        {
            var c = Color.Lerp(under, over, Mathf.Clamp01(alpha)); c.a = 1f;
            return c;
        }

        /// <summary>Serif700 roles resolve to Noto Serif KR 600 (no static 700 instance, foundation gap); the codex 행 분류
        /// (공격 / 패링 / 소환, codex.png Serif 700 22) printed visibly lighter than the mockup. Same answer as the map's place
        /// names (WorldMapPresenter.MapLabelFace304): the 800 face at the role's size, tracking and line spacing. The role face
        /// is kept when the 800 asset is missing. Call before placing the label (the baseline helpers read its font).</summary>
        void Content304Serif800Face(TMP_Text t, UiType304 role)
        {
            var s = Content304Style;
            if (t == null || s == null) return;
            var heavy = s.FontFor(UiFont304.Serif800);
            if (heavy == null || heavy == t.font) return;
            t.font = heavy;
            t.fontSharedMaterial = heavy.material;
            var r = s.Role(role);
            if (r != null) t.lineSpacing = r.TmpLineSpacing(heavy);
            if (!string.IsNullOrEmpty(t.text))
            {
                var pref = t.GetPreferredValues(t.text);
                t.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(pref.x), Mathf.Ceil(pref.y));
            }
        }

        /// <summary>D21 새 내용: a short ink flick at the cell's top-right (Mist on the veil, Ash on paper). Cleared when the cell
        /// is looked at (ContentCell304 hides it on select, the registry forgets the key).</summary>
        GameObject Content304NewMark(ContentCell304 cell, string key, float x, float y, bool onPaper)
        {
            if (cell == null || string.IsNullOrEmpty(key) || !Content304Seen.IsNew(key)) return null;
            var s = Content304Style; var art = Content304Art;
            Color c = onPaper ? s.Ash : s.Mist;
            Image mark = art != null && art.Flick != null
                ? V.SpriteImage(cell.transform, "NewMark", art.Flick, c, x, y, 30, 11, -18f)
                : V.Brush(s, cell.transform, "NewMark", StrokeClass304.Short, c, x, y, 30, 8, 1f, -18f);
            mark.raycastTarget = false;
            cell.NewMark = mark.gameObject; cell.SeenKey = key; cell.Seen = Content304Seen;
            return mark.gameObject;
        }

        /// <summary>D22 봉인·완료: one dry ink stroke (-8°, α.8) laid over a label / icon. (x, y, w) = the covered span.</summary>
        Image Content304Sealed(Transform parent, float x, float y, float w, float h = 22f, Color? tint = null)
        {
            var s = Content304Style;
            return V.Brush(s, parent, "Sealed304", StrokeClass304.Dry, tint ?? s.Ink, x, y, w, h, .8f, -8f);
        }

        /// <summary>D04 조선통보 symbol (coin.png: circle, 6.5 % rim, 24 % square hole, thin hole rim, ink monochrome) or null
        /// when content304-setup has not produced the sprite. Always drawn next to the word 조선통보 (support pictogram only).</summary>
        Image Content304Coin(Transform parent, float x, float y, float size, Color tint)
        {
            var art = Content304Art;
            if (art == null || art.Coin == null) return null;
            var img = V.SpriteImage(parent, "Coin304", art.Coin, tint, x, y, size, size);
            img.preserveAspect = true;
            return img;
        }

        /// <summary>"[coin] 조선통보 1,240" right-aligned at `right` (DESIGN §7.3: meta Mist + Figure36 Paper, gap 14). `y` is the
        /// mockup's CSS top of the flex row (align-items: baseline); the figure (line-height 1) sets the shared baseline.</summary>
        RectTransform Content304Currency(Transform parent, string name, float right, float y, int amount)
        {
            var s = Content304Style;
            var root = V.Rect(name, parent, 0, 0, 1, 1);
            var value = V.Label(s, root, "Value", amount.ToString("N0"), UiType304.Figure36, s.Paper, 0, 0);
            var meta = V.Label(s, root, "Label", "조선통보", UiType304.Meta20, s.Mist, 0, 0);
            float vw = value.rectTransform.sizeDelta.x, mw = meta.rectTransform.sizeDelta.x;
            float baseline = Content304CssBaseline(value, y, 1f);
            float x = right - vw;
            Content304OnBaseline(value, x, baseline);
            float mx = x - 14f - mw;
            Content304OnBaseline(meta, mx, baseline);
            Content304Coin(root, mx - 10f - 26f, baseline - 8f - 13f, 26f, s.Mist);   // centred on the word's x-height
            return root;
        }

        // ------------------------------------------------------------------ mockup CSS → TMP placement
        // TMP hangs the first line's ascender on the rect top; the mockups (CSS) centre the font's content area in a line box
        // of `line-height` x size. For the Noto CJK assets (ascent 1.151, descent .286 em) that puts big type 4~52 px lower
        // than the mockup when a CSS top is used as the rect top (Speaker60 10, Figure40 9, Headline44 at line-height 1 10,
        // Glyph240 52). These helpers reproduce the CSS line box from the label's own font and size (text scale included).

        /// <summary>Rect top that reproduces a mockup element at CSS `cssTop` with `cssLineHeight` (a multiple of the size;
        /// 0 = CSS "normal", whose line box is the content area itself, so no offset).</summary>
        static float Content304CssTop(TMP_Text t, float cssTop, float cssLineHeight)
        {
            if (cssLineHeight <= 0f || t == null || t.font == null || t.font.faceInfo.pointSize <= 0f) return cssTop;
            var f = t.font.faceInfo;
            float content = (f.ascentLine - f.descentLine) / f.pointSize * t.fontSize;
            return cssTop + (cssLineHeight * t.fontSize - content) * .5f;
        }

        /// <summary>Rect top → first baseline.</summary>
        static float Content304Ascent(TMP_Text t)
        {
            if (t == null) return 0f;
            if (t.font == null || t.font.faceInfo.pointSize <= 0f) return t.fontSize * .8f;
            return t.font.faceInfo.ascentLine / t.font.faceInfo.pointSize * t.fontSize;
        }

        /// <summary>First baseline → bottom of the content area (the font's descent).</summary>
        static float Content304Descent(TMP_Text t)
        {
            if (t == null) return 0f;
            if (t.font == null || t.font.faceInfo.pointSize <= 0f) return t.fontSize * .25f;
            return -t.font.faceInfo.descentLine / t.font.faceInfo.pointSize * t.fontSize;
        }

        /// <summary>The CSS baseline of an element at `cssTop` with `cssLineHeight`.</summary>
        static float Content304CssBaseline(TMP_Text t, float cssTop, float cssLineHeight) => Content304CssTop(t, cssTop, cssLineHeight) + Content304Ascent(t);

        /// <summary>Places `t` at the mockup's CSS top (x unchanged semantics: left edge).</summary>
        static void Content304AtCss(TMP_Text t, float x, float cssTop, float cssLineHeight)
        { if (t != null) V.Place(t.rectTransform, x, Content304CssTop(t, cssTop, cssLineHeight)); }

        /// <summary>Places `t` so its first baseline sits on `baseline` (CSS align-items: baseline).</summary>
        static void Content304OnBaseline(TMP_Text t, float x, float baseline)
        { if (t != null) V.Place(t.rectTransform, x, baseline - Content304Ascent(t)); }

        /// <summary>Result line → the quiet notice channel (DESIGN §5.10). "정비를 마쳤다. 조선통보 -60" becomes title + source.
        /// Without a channel (fallback style) it goes to Menu304ShowNotice with the kind stated (#304 QA1 merge: ShowNotice now
        /// derives the kind from the text, so 8 s no longer marks an error).</summary>
        void Content304Notice(UiNoticeKind304 kind, string message, string source = null)
        {
            if (string.IsNullOrEmpty(message)) return;
            string title = message.Trim(), src = source;
            int stop = title.IndexOf(". ", StringComparison.Ordinal);
            if (stop > 0 && src == null) { src = title.Substring(stop + 2).Trim(); title = title.Substring(0, stop); }
            title = title.Replace("\n", " ").TrimEnd('.');
            var channel = Content304Style.Notices;
            if (channel != null) channel.Raise(kind, title, src);
            else Menu304ShowNotice(message, kind, kind == UiNoticeKind304.Error ? 8f : 5f);
        }

        /// <summary>1px hairline box (film frames: Paper α.16), through UI/InkReveal (Content304Ink) so it keeps the mockup's weight.</summary>
        static void Content304Hairline(Transform parent, float x, float y, float w, float h, Color c, UiStyle304SO s)
        {
            Content304Ink(V.Image(V.Rect("LineT", parent, x, y, w, 1), c), s); Content304Ink(V.Image(V.Rect("LineB", parent, x, y + h - 1, w, 1), c), s);
            Content304Ink(V.Image(V.Rect("LineL", parent, x, y + 1, 1, h - 2), c), s); Content304Ink(V.Image(V.Rect("LineR", parent, x + w - 1, y + 1, 1, h - 2), c), s);
        }

        /// <summary>가름선 (detail column rule x1368 / trade x720, Paper α.14, 1 x 740) through UI/InkReveal.</summary>
        Image Content304Rule(Transform parent, string name, float x, float y, float h)
        {
            var s = Content304Style;
            return Content304Ink(V.Image(V.Rect(name, parent, x, y, 1, h), UiStyle304SO.A(s.Paper, .14f)), s);
        }

        // ------------------------------------------------------------------ 오행 language constants (not balance data)
        static string Content304Hanja(Element e) => "木火土金水".Substring(Mathf.Clamp((int)e, 0, 4), 1);
        static string Content304ElementName(Element e) => "목화토금수".Substring(Mathf.Clamp((int)e, 0, 4), 1);
        /// <summary>상생: the element that generates e (ElementRelations ring order: 목→화→토→금→수).</summary>
        static Element Content304GeneratedBy(Element e) => (Element)(((int)e + 4) % 5);
        static Element Content304Generates(Element e) => (Element)(((int)e + 1) % 5);
        static Element Content304OvercomeBy(Element e) => (Element)(((int)e + 3) % 5);
        static Element Content304Overcomes(Element e) => (Element)(((int)e + 2) % 5);
    }

    /// <summary>#304 칸 state next to FocusVisual304 (which does hover-select, label, press): the cinnabar CellFocus while focused,
    /// the same frame in ink while the cell stays the page's selection and focus sits elsewhere (C17 묵권), and the D21 flick
    /// that disappears once the cell is looked at. Frames are built on first need.</summary>
    public sealed class ContentCell304 : MonoBehaviour, ISelectHandler
    {
        public string Id;
        /// <summary>The page's selection (gearSelection, selectedSpell ...): shows the ink frame while not focused.</summary>
        public bool Kept;
        public GameObject NewMark;
        public string SeenKey;
        public ContentSeen304 Seen;
        public FocusVisual304 Visual { get; private set; }

        UiStyle304SO style;
        Rect frame;
        Color keptColor;
        Action<ContentCell304> selected;
        GameObject focusFrame, keptFrame;

        public void Init(UiStyle304SO s, FocusVisual304 visual, Rect frameRect, Color kept, Action<ContentCell304> onSelected)
        { style = s; Visual = visual; frame = frameRect; keptColor = kept; selected = onSelected; }

        /// <summary>Label whose colour / press drop FocusVisual304 drives (call after the label exists under the cell).</summary>
        public void BindLabel(TMP_Text label, Color normal, Color focused)
        { if (Visual != null) Visual.Bind(style, null, label, normal, focused, Visual.DabAnchor); }

        public void OnSelect(BaseEventData eventData)
        {
            if (NewMark != null) NewMark.SetActive(false);
            if (Seen != null && !string.IsNullOrEmpty(SeenKey)) Seen.MarkSeen(SeenKey);
            selected?.Invoke(this);
        }

        void LateUpdate()
        {
            bool focused = Visual != null && Visual.Focused && Visual.Interactable;
            if (focused && focusFrame == null) focusFrame = PlaytestUiView.CellFocus(style, transform, frame, style != null ? style.Cinnabar : Color.red, "CellFocus").gameObject;
            if (focusFrame != null && focusFrame.activeSelf != focused) focusFrame.SetActive(focused);
            bool kept = Kept && !focused;
            if (kept && keptFrame == null) keptFrame = PlaytestUiView.CellFocus(style, transform, frame, keptColor, "CellKept").gameObject;
            if (keptFrame != null && keptFrame.activeSelf != kept) keptFrame.SetActive(kept);
        }
    }

    /// <summary>Records which content row was selected last (PlaytestUiRoot restores it after a rebuild).</summary>
    public sealed class ContentFocusNote304 : MonoBehaviour, ISelectHandler
    {
        public Action<GameObject> Selected;
        public void OnSelect(BaseEventData eventData) => Selected?.Invoke(gameObject);
    }

    /// <summary>D21 registry: what the player has already looked at in this session (keys spell:가, item:fragment.ga, gear:pine_brush).
    /// Lives on the PlaytestUiRoot GameObject (not static, reset when a new session object is observed). The first
    /// observation of a session marks everything it already holds as seen.</summary>
    public sealed class ContentSeen304 : MonoBehaviour
    {
        WorldMacroPlaytestSession session;
        readonly HashSet<string> seen = new HashSet<string>();

        public void Observe(WorldMacroPlaytestSession s)
        {
            if (s == session) return;
            session = s; seen.Clear();
            if (s == null || s.Progress == null || s.Progress.ui == null) return;
            foreach (var l in s.Progress.ui.knownSpellLetters) seen.Add("spell:" + l);
            foreach (var i in s.Progress.ui.items) if (i != null) seen.Add("item:" + i.id);
            if (s.Progress.equipment != null && s.Progress.equipment.Owned != null)
                foreach (var o in s.Progress.equipment.Owned) if (o != null) seen.Add("gear:" + o.Id);
        }

        public bool IsNew(string key) => session != null && !string.IsNullOrEmpty(key) && !seen.Contains(key);
        public void MarkSeen(string key) { if (!string.IsNullOrEmpty(key)) seen.Add(key); }
    }
}
