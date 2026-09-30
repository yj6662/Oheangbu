using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 조용한 쪽지 채널 renderer (DESIGN §5.10, IMPLEMENTATION §7.2, REFERENCE_BOARD D09). Lives on the menu canvas
    /// under PlaytestUiRoot's "Notice" root and subscribes to the style's UiNoticeChannelSO (drains notices raised before it
    /// subscribed). Two tiers (D09): a 습득 기록 (Pickup without glyphs) is one wet_s line h72 (Serif600 24 + ×N + 출처 meta);
    /// every other notice is the 사건 카드 h110 (조각 글자 50 max 3 + "+N" | divider | 제목 24 + 출처 20; 오류 = cinnabar vertical
    /// dry stroke + 주사 밝음 title, 8 s). At most ToastSpec.MaxVisible cards, stacked downwards ToastSpec.Pitch - Height apart,
    /// the rest queue. While a menu is open or the player is drawing (Q) ordinary notices wait; cards already showing when a
    /// menu opens go back to the head of the queue and return when it closes. Errors, and 안내 / 정비 notices raised while a
    /// menu is open (they answer a menu action), show at once. Top-right corner anchored (V.RectAnchored), text ends inside
    /// x1760 and only the stroke tail leaves the screen. UniTask tweens (UiTween304), no coroutines, no statics.</summary>
    [DisallowMultipleComponent]
    public sealed class MenuToastStack304 : MonoBehaviour
    {
        public const float LightHeight = 72f;
        const float StackGap = 14f;            // ToastSpec.Pitch 124 - Height 110
        const float TitleMaxWidth = 520f, SourceMaxWidth = 560f;
        const float RequeueMinSeconds = 1.5f;

        sealed class Entry { public UiNotice304 Notice; public bool Urgent; public float Remaining; }

        sealed class Card
        {
            public Entry Entry;
            public RectTransform Frame;
            public CanvasGroup Text;
            public Image Underlay;
            public InkRevealEffect Fx;
            public float Height, Alpha, Offset, T, Hold;
            public int State;                  // 0 in, 1 hold, 2 out
            public readonly UiTweenSlot304 MoveSlot = new UiTweenSlot304();
        }

        UiStyle304SO style;
        UiNoticeChannelSO channel;
        Func<bool> menuOpen, drawing;
        readonly List<Entry> waiting = new List<Entry>();
        readonly List<Card> cards = new List<Card>();
        int serial;

        UiStyle304SO S => style != null ? style : UiStyle304SO.Fallback;
        public bool HasChannel => channel != null;
        public int WaitingCount => waiting.Count;
        public int VisibleCount => cards.Count;
        /// <summary>Title of the last card that started showing (harness / capture checks).</summary>
        public string LastShownTitle { get; private set; } = "";

        /// <summary>(Re)binds the style (its Notices channel) and the two wait conditions. Safe to call again on scene binds.</summary>
        public void Bind(UiStyle304SO s, Func<bool> isMenuOpen, Func<bool> isDrawing)
        {
            style = s; menuOpen = isMenuOpen; drawing = isDrawing;
            var next = s != null ? s.Notices : null;
            if (next == channel) return;
            Unsubscribe(); channel = next;
            if (isActiveAndEnabled) Subscribe();
        }

        void OnEnable() { Subscribe(); }
        void OnDisable() { Unsubscribe(); }
        void OnDestroy() { Unsubscribe(); }

        void Subscribe()
        {
            if (channel == null) return;
            channel.Raised -= Enqueue; channel.Raised += Enqueue;
            channel.DrainPending(Enqueue);
        }

        void Unsubscribe() { if (channel != null) channel.Raised -= Enqueue; }

        /// <summary>Queues one notice (the channel calls this; PlaytestUiRoot calls it directly when no channel asset is wired).</summary>
        public void Enqueue(UiNotice304 n)
        {
            n.Title ??= ""; n.Source ??= ""; n.Glyphs ??= "";
            if (n.Title.Length == 0 && n.Glyphs.Length == 0) return;
            if (n.Seconds <= 0f) n.Seconds = S.ToastSeconds(n.Kind);
            if (IsDuplicate(n)) return;
            bool menu = menuOpen != null && menuOpen();
            bool urgent = n.Kind == UiNoticeKind304.Error || (menu && (n.Kind == UiNoticeKind304.Info || n.Kind == UiNoticeKind304.Service));
            waiting.Add(new Entry { Notice = n, Urgent = urgent, Remaining = n.Seconds });
        }

        /// <summary>Drops every card and queued notice (scene change).</summary>
        public void Clear()
        {
            waiting.Clear();
            foreach (var c in cards) DestroyCard(c);
            cards.Clear();
        }

        bool IsDuplicate(UiNotice304 n)
        {
            foreach (var e in waiting) if (Same(e.Notice, n)) return true;
            foreach (var c in cards) if (c.State != 2 && Same(c.Entry.Notice, n)) return true;
            return false;
        }

        // errors collapse by title (a failed save raised by the save-state watcher and by ReturnTitle is one card)
        // (text compared without trailing periods: two senders may split "A. B." differently)
        static bool Same(UiNotice304 a, UiNotice304 b) => a.Kind == b.Kind && Norm(a.Title) == Norm(b.Title)
            && (a.Kind == UiNoticeKind304.Error || (Norm(a.Source) == Norm(b.Source) && a.Glyphs == b.Glyphs));

        static string Norm(string s) => string.IsNullOrEmpty(s) ? "" : s.Trim().TrimEnd('.', ' ');

        void Update()
        {
            var s = S;
            float dt = Time.unscaledDeltaTime;
            bool menu = menuOpen != null && menuOpen();
            bool draw = drawing != null && drawing();
            bool relayout = false;

            // a menu opened over ordinary cards: they go back to the head of the queue and return when it closes
            if (menu)
            {
                int insert = 0;
                for (int i = 0; i < cards.Count; i++)
                {
                    var c = cards[i];
                    if (c.Entry.Urgent || c.State == 2) continue;
                    c.Entry.Remaining = Mathf.Max(RequeueMinSeconds, c.State == 1 ? c.Hold : c.Entry.Remaining);
                    waiting.Insert(insert++, c.Entry);
                    DestroyCard(c); cards.RemoveAt(i); i--; relayout = true;
                }
            }

            bool reduced = UiTween304.ReducedMotion;
            float inSec = s.Motion.Sec(s.Motion.ToastInMs, reduced), outSec = s.Motion.Sec(s.Motion.ToastOutMs, reduced);
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c.Frame == null) { cards.RemoveAt(i); i--; relayout = true; continue; }
                c.T += dt;
                if (c.State == 0 && c.T >= inSec) { c.State = 1; c.Hold = c.Entry.Remaining; }
                else if (c.State == 1) { c.Hold -= dt; if (c.Hold <= 0f) BeginOut(c, s); }
                else if (c.State == 2 && c.T >= outSec + .02f) { DestroyCard(c); cards.RemoveAt(i); i--; relayout = true; }
            }

            int max = Mathf.Max(1, s.Toast.MaxVisible);
            while (cards.Count < max && waiting.Count > 0)
            {
                int k = waiting.FindIndex(e => e.Urgent || (!menu && !draw));
                if (k < 0) break;
                var e = waiting[k]; waiting.RemoveAt(k);
                Show(e, s); relayout = true;
            }
            if (relayout) Relayout(s);
        }

        float NextOffset()
        {
            float offset = 0f;
            foreach (var c in cards) offset += c.Height + StackGap;
            return offset;
        }

        void Relayout(UiStyle304SO s)
        {
            float offset = 0f;
            foreach (var c in cards)
            {
                if (!Mathf.Approximately(c.Offset, offset) && c.Frame != null)
                {
                    c.Offset = offset;
                    var goal = new Vector2(c.Frame.anchoredPosition.x, -offset);
                    if (UiTween304.ReducedMotion) { c.MoveSlot.Cancel(); c.Frame.anchoredPosition = goal; }
                    else UiTween304.Move(c.Frame, goal, s.Motion.Sec(s.Motion.SwellMs), s.Motion.Stroke, c.MoveSlot.Restart(c.Frame)).Forget();
                }
                offset += c.Height + StackGap;
            }
        }

        // ------------------------------------------------------------------ one card
        void Show(Entry e, UiStyle304SO s)
        {
            var n = e.Notice; var t = s.Toast;
            n.Title = Norm(n.Title); n.Source = Norm(n.Source);   // toast lines carry no final period (mockups)
            bool error = n.Kind == UiNoticeKind304.Error;
            bool light = n.Kind == UiNoticeKind304.Pickup && string.IsNullOrEmpty(n.Glyphs);
            float h = light ? LightHeight : t.Height;
            float offset = NextOffset();
            var frame = V.RectAnchored("Toast304_" + (++serial), transform, 0f, offset, UiPageFit304.Width, UiPageFit304.Height, t.Anchor);
            var textRoot = V.Rect("Text", frame, 0, 0, UiPageFit304.Width, UiPageFit304.Height);
            var group = textRoot.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

            float right = light ? BuildLight(s, textRoot, n) : BuildEvent(s, textRoot, n, error);
            float alpha = error ? t.ErrorUnderlayAlpha : t.UnderlayAlpha;
            var under = V.Stroke(s, frame, "ToastUnderlay", StrokeClass304.WetS, s.Ink, t.Origin.x, t.Origin.y, h, right + 16f, alpha);
            under.transform.SetAsFirstSibling();
            float shift = t.Overflow(right);
            if (shift > 0f) foreach (RectTransform child in frame) child.anchoredPosition -= new Vector2(shift, 0f);

            var card = new Card { Entry = e, Frame = frame, Text = group, Underlay = under, Fx = under.GetComponent<InkRevealEffect>(), Height = h, Alpha = alpha, Offset = offset };
            cards.Add(card);
            LastShownTitle = n.Title;
            var ct = UiTween304.Token(frame);
            bool reduced = UiTween304.ReducedMotion;
            if (card.Fx != null) UiTween304.RevealIn(card.Fx, s, s.Motion.ToastInMs, ct, alpha).Forget();
            UiTween304.Alpha(group, 1f, s.Motion.Sec(s.Motion.ToastInMs, reduced), s.Motion.Stroke, ct).Forget();
        }

        // 습득 기록 (D09): title [×N] source on one line, no glyphs
        float BuildLight(UiStyle304SO s, RectTransform root, UiNotice304 n)
        {
            var t = s.Toast;
            string title = n.Title, count = "";
            int x = title.IndexOf('×');
            if (x > 0) { count = title.Substring(x).Trim(); title = title.Substring(0, x).TrimEnd(); }
            float cx = t.NoGlyphTitleX - 20f, mid = t.Origin.y + LightHeight * .5f;
            var tl = Line(s, root, "Title", title, UiType304.Label24, s.Paper, cx, mid, TitleMaxWidth);
            cx += tl.rectTransform.sizeDelta.x;
            if (count.Length > 0) { var cl = Line(s, root, "Count", count, UiType304.MetaBold20, s.Paper, cx + 10f, mid, 160f); cx += 10f + cl.rectTransform.sizeDelta.x; }
            if (!string.IsNullOrEmpty(n.Source)) { var sl = Line(s, root, "Source", n.Source, UiType304.Meta20, s.Mist, cx + 16f, mid, SourceMaxWidth); cx += 16f + sl.rectTransform.sizeDelta.x; }
            return cx;
        }

        // 사건 카드: [조각 글자] | [제목 + 출처], error = cinnabar vertical dry stroke + CinnabarLift title
        float BuildEvent(UiStyle304SO s, RectTransform root, UiNotice304 n, bool error)
        {
            var t = s.Toast;
            float glyphRight = 0f;
            bool hasGlyphs = !string.IsNullOrEmpty(UiNotice304.SplitGlyphs(n.Glyphs, Mathf.Max(0, t.MaxGlyphs), out _));
            if (hasGlyphs) V.GlyphRow(s, root, "Glyphs", n.Glyphs, t.MaxGlyphs, UiType304.ToastGlyph50, s.Paper, t.GlyphPos.x, t.GlyphPos.y, t.GlyphGap, out glyphRight);
            t.Layout(hasGlyphs, glyphRight, error, out float dividerX, out float titleX, out float sourceX);
            if (hasGlyphs)
            {
                // InkReveal material (fully revealed): the α.3 paper hairline gets the shader's linear-space alpha remap like the
                // underlay under it; on UI/Default it composited ~1.5x too bright on the ink (linear blending)
                var divider = V.Image(V.Rect("Divider", root, dividerX, t.DividerY, 1f, t.DividerH), UiStyle304SO.A(s.Paper, t.DividerAlpha));
                InkRevealEffect.On(divider, s, InkRevealMode.Bleed, 1f);
            }
            if (error)
            {
                var edge = V.Brush(s, root, "ErrorEdge", StrokeClass304.Dry, s.Cinnabar, t.ErrorEdge.x, t.ErrorEdge.y, t.ErrorEdge.width, t.ErrorEdge.height, t.ErrorEdgeAlpha, t.ErrorEdgeRot);
                MenuEdgeStroke304.Stretch(edge);
            }
            bool hasSource = !string.IsNullOrEmpty(n.Source);
            float right = hasGlyphs ? glyphRight : titleX;
            var title = V.Label(s, root, "Title", n.Title, UiType304.Label24, error ? s.CinnabarLift : s.Paper, titleX, t.TitleY);
            Cap(title, TitleMaxWidth);
            if (!hasSource) V.Place(title.rectTransform, titleX, t.Origin.y + (t.Height - title.rectTransform.sizeDelta.y) * .5f);
            right = Mathf.Max(right, titleX + title.rectTransform.sizeDelta.x);
            if (hasSource)
            {
                var src = V.Label(s, root, "Source", n.Source, UiType304.Meta20, s.Mist, sourceX, t.SourceY);
                Cap(src, SourceMaxWidth);
                right = Mathf.Max(right, sourceX + src.rectTransform.sizeDelta.x);
            }
            return right;
        }

        static TMP_Text Line(UiStyle304SO s, RectTransform root, string name, string text, UiType304 role, Color color, float x, float midY, float maxWidth)
        {
            var l = V.Label(s, root, name, text, role, color, x, 0f);
            Cap(l, maxWidth);
            V.Place(l.rectTransform, x, midY - l.rectTransform.sizeDelta.y * .5f);
            return l;
        }

        static void Cap(TMP_Text label, float maxWidth)
        {
            var r = label.rectTransform;
            if (r.sizeDelta.x <= maxWidth) return;
            r.sizeDelta = new Vector2(maxWidth, r.sizeDelta.y);
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        void BeginOut(Card c, UiStyle304SO s)
        {
            c.State = 2; c.T = 0f;
            var ct = UiTween304.Token(c.Frame);
            bool reduced = UiTween304.ReducedMotion;
            if (c.Fx != null) { c.Fx.Mode = InkRevealMode.Bleed; UiTween304.RevealOut(c.Fx, s, s.Motion.ToastOutMs, ct, c.Alpha).Forget(); }
            UiTween304.Alpha(c.Text, 0f, s.Motion.Sec(s.Motion.ToastOutMs, reduced) * .6f, s.Motion.Lift, ct).Forget();
        }

        static void DestroyCard(Card c)
        {
            c.MoveSlot.Cancel();
            if (c.Frame != null) { c.Frame.gameObject.SetActive(false); UnityEngine.Object.Destroy(c.Frame.gameObject); }
        }
    }

    /// <summary>#304 short vertical cinnabar dry stroke (오류 알림 가장자리, 설정 15초 경고, 일시정지 저장 실패). The mockups draw
    /// stroke_dry STRETCHED to its rect (CSS .stroke background-size 100%: hud_dark.html 70x12, options.html 110x12), so the
    /// wet body shows for most of the length. V.Brush slices Dry (borders L80 R700 of 1800, scaled by h/90): at 10~12 px thick
    /// and 48~110 long the two borders already fill the rect, leaving only the head blob and the hair tail (toast_error.png).
    /// Stretch = Image.Type.Simple on the same sprite / InkReveal material. No state.</summary>
    public static class MenuEdgeStroke304
    {
        public static Image Stretch(Image img)
        {
            if (img != null) img.type = Image.Type.Simple;
            return img;
        }

        /// <summary>Vertical edge centred on (cx, cy), `length` tall, `thickness` wide, head at the top (Dry rotated 90).</summary>
        public static Image Draw(UiStyle304SO s, Transform parent, string name, float cx, float cy, float length, float thickness, float alpha)
            => Stretch(V.Brush(s, parent, name, StrokeClass304.Dry, (s != null ? s : UiStyle304SO.Fallback).Cinnabar,
                cx - length * .5f, cy - thickness * .5f, length, thickness, alpha, 90f));
    }
}
