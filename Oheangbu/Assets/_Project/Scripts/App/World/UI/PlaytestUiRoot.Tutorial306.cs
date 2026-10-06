using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    // #306 #11 TutorialCard304 (SPEC-PLAYTEST-306 #11, D306, TEST): the mine tutorial's one card surface. Session.TutorialCardRequested
    // -> the "익힘" page: OpenPage (Pause.Begin = the only time stop, save, HUD off) -> veil, title, one line, keycaps, the letter's
    // strokes replaying. After MinSeconds (real time) F / Space / left click / pad south closes it (Pause.End -> the gate releases when
    // every key is up, so the closing key never reaches interaction or harvest). Esc: a tap closes, held SkipHoldSeconds = every remaining
    // card counts as seen (the fight stays: this is not a difficulty choice). Settings.TutorialPause off = no card at all.
    public sealed partial class PlaytestUiRoot
    {
        public const string TutorialPage306 = "익힘";
        MineTutorialCard306 tutorialCard306;
        float tutorialShownAt306, tutorialEscSince306 = -1f;
        RectTransform tutorialSkipBar306;
        bool tutorialSkip306;
        /// <summary>Harness view: the card on screen (null = none).</summary>
        public MineTutorialCard306 TutorialCardShowing => tutorialCard306;

        void Tutorial306Hook()
        {
            if (Session == null) return;
            Session.TutorialCardRequested -= OnTutorialCardRequested306; Session.TutorialCardRequested += OnTutorialCardRequested306;
            Session.TutorialCardsAllowed = Settings == null || Settings.Current == null || Settings.Current.TutorialPause;
        }

        void Tutorial306Unhook()
        {
            Tutorial306Ended(false);
            if (Session != null) Session.TutorialCardRequested -= OnTutorialCardRequested306;
        }

        void Tutorial306SettingsChanged()
        {
            if (Session != null && Settings != null && Settings.Current != null) Session.TutorialCardsAllowed = Settings.Current.TutorialPause;
        }

        bool OnTutorialCardRequested306(MineTutorialCard306 card)
        {
            if (card == null || !bound || IsTitle || Busy || closingMap || Page.Length > 0 || confirmationRoot != null || LoadingInProgress ||
                ShowingDemoEnding || (Session != null && Session.RestPresentationActive)) return false;
            tutorialCard306 = card; tutorialSkip306 = false; tutorialEscSince306 = -1f;
            OpenPage(TutorialPage306);
            if (Page != TutorialPage306) { tutorialCard306 = null; return false; }
            return true;
        }

        void BuildTutorialCard306()
        {
            var card = tutorialCard306; var s = V.Style(Theme);
            V.EnsureCanvasChannels(canvas);
            V.Dim(s, modalLayer, .72f, "TutorialVeil306");
            var page = V.Page304(modalLayer, "TutorialCard304"); contentRoot = page;
            bool letter = card != null && !string.IsNullOrEmpty(card.Letter);
            float left = letter ? 520f : 460f, width = letter ? 760f : 1000f;
            var title = V.Label(s, page, "Title", card != null ? card.Title : "", UiType304.Headline44, s.Paper, left, 360, width, 60, TextAlignmentOptions.TopLeft);
            var body = V.Label(s, page, "Body", card != null ? card.Body : "", UiType304.Title30, s.Paper, left, 440, width, 120, TextAlignmentOptions.TopLeft, true);
            float kx = left;
            if (card != null && card.Keys != null)
                foreach (var key in card.Keys)
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    var cap = V.Keycap(s, page, key, true, false, false, kx, 590);
                    kx += cap.sizeDelta.x + 12f;
                }
            if (letter)
            {
                var frame = V.Rect("LetterFilm", page, 1320, 380, 196, 180);
                var film = frame.gameObject.AddComponent<CodexStrokeExample>();
                film.raycastTarget = false; InkRevealEffect.On(film, s, InkRevealMode.Wipe, 1f);   // same as the codex film (Content304Ink)
                // the codex film path (Content304Film): count the strokes, then draw them all in 한지 on the ribbon texture
                var library = Theme != null ? Theme.StrokeTemplates : null;
                film.InitializeFilm(library, card.Letter, 0, 0, Color.clear, Color.clear, Color.clear, s.Sprites.Ribbon);
                int strokes = film.StrokeCount;
                if (strokes > 0) { film.InitializeFilm(library, card.Letter, 0, strokes, Color.clear, s.Paper, UiStyle304SO.A(s.Paper, .16f), s.Sprites.Ribbon); film.Replay(.15f); }
                else { UnityEngine.Object.Destroy(film); V.Label(s, frame, "Glyph", card.Letter, UiType304.CellGlyph64, s.Paper, 0, 0, 196, 176, TextAlignmentOptions.Center); }
            }
            // closing hint (controls only): [F] 닫기 · [Esc] 길게 — 익힘 건너뛰기
            float hx = left, hy = 690;
            var f = V.Keycap(s, page, "F", false, false, true, hx, hy); hx += f.sizeDelta.x + 8f;
            var close = V.Label(s, page, "CloseLabel", "닫기", UiType304.Meta20, s.Mist, hx, hy + 6); hx += close.preferredWidth + 28f;
            var esc = V.Keycap(s, page, "Esc", false, false, true, hx, hy); hx += esc.sizeDelta.x + 8f;
            var skip = V.Label(s, page, "SkipLabel", "길게 — 익힘 건너뛰기", UiType304.Meta20, s.Mist, hx, hy + 6);
            tutorialSkipBar306 = V.Rect("SkipHold", page, esc.anchoredPosition.x, hy + 40, 0, 2);
            V.Image(tutorialSkipBar306, s.Paper);
            tutorialShownAt306 = Time.unscaledTime;
            title.alpha = body.alpha = 1f; skip.alpha = 1f;
        }

        /// <summary>Per frame while the card shows (from Menu304Tick's caller): closing keys after MinSeconds, Esc tap / hold.</summary>
        void Tutorial306Tick()
        {
            if (tutorialCard306 == null) return;
            if (Page != TutorialPage306) { Tutorial306Ended(false); return; }   // cleared by another flow: the director must not wait
            float minSeconds = Mathf.Max(0f, tutorialCard306.MinSeconds), hold = Mathf.Max(.2f, tutorialCard306.SkipHoldSeconds);
            var kb = Keyboard.current; var mouse = Mouse.current; var pad = Gamepad.current;
            bool escDown = kb != null && kb.escapeKey.isPressed || pad != null && pad.startButton.isPressed;
            if (tutorialEscSince306 >= 0f)
            {
                float held = Time.unscaledTime - tutorialEscSince306;
                if (tutorialSkipBar306 != null) tutorialSkipBar306.sizeDelta = new Vector2(180f * Mathf.Clamp01(held / hold), 2f);
                if (held >= hold) { tutorialSkip306 = true; CloseMenu(); return; }
                if (!escDown) { tutorialEscSince306 = -1f; if (Time.unscaledTime - tutorialShownAt306 >= minSeconds) CloseMenu(); else if (tutorialSkipBar306 != null) tutorialSkipBar306.sizeDelta = new Vector2(0, 2); }
                return;
            }
            if (Time.unscaledTime - tutorialShownAt306 < minSeconds) return;
            bool close = kb != null && (kb.fKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) ||
                mouse != null && mouse.leftButton.wasPressedThisFrame || pad != null && pad.buttonSouth.wasPressedThisFrame;
            if (close) CloseMenu();
        }

        /// <summary>Back() (Esc / pad start pressed) while the card shows: start the hold instead of opening the pause menu.</summary>
        bool Tutorial306Back()
        {
            if (Page != TutorialPage306 || tutorialCard306 == null) return false;
            if (tutorialEscSince306 < 0f) tutorialEscSince306 = Time.unscaledTime;
            return true;
        }

        /// <summary>Test-harness hook (#308): skip every remaining tutorial card through the same path as holding Esc.</summary>
        public bool SkipTutorial306ForHarness() { if (Page != TutorialPage306 || tutorialCard306 == null) return false; tutorialSkip306 = true; CloseMenu(); return true; }

        /// <summary>After FinishClose (or any flow that dropped the page): tell the session once.</summary>
        void Tutorial306Ended(bool fromClose)
        {
            var card = tutorialCard306; if (card == null) return;
            bool skip = tutorialSkip306;
            tutorialCard306 = null; tutorialSkip306 = false; tutorialEscSince306 = -1f; tutorialSkipBar306 = null;
            try { Session?.MineTutorialCardClosed(card.BeatId, skip); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
