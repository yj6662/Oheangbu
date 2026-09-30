using Oheangbu.App.Demo;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    public sealed partial class PlaytestUiRoot
    {
        const string DemoEndingPage = "여정의 끝";
        SouthGateDoorPresentation demoEndingDoor;
        float nextEndingDoorSearch;
        public bool ShowingDemoEnding => Page == DemoEndingPage;

        void ResetDemoEnding()
        {
            demoEndingDoor = null;
            nextEndingDoorSearch = 0;
        }

        // The persisted receipt is necessary, but the visible doors must finish opening too.
        // Polling also covers a reload after the one-shot completion event has already fired.
        bool TryPresentDemoEnding()
        {
            if (ShowingDemoEnding) return true;
            if (IsTitle || Session == null || !Session.DemoCampaignCompleted ||
                Busy || closingMap || confirmationRoot != null || Page.Length != 0) return false;
            if (demoEndingDoor == null && Time.unscaledTime >= nextEndingDoorSearch)
            {
                nextEndingDoorSearch = Time.unscaledTime + 1;
                var doors = FindObjectsByType<SouthGateDoorPresentation>(FindObjectsSortMode.None);
                foreach (var door in doors)
                    if (door.gameObject.scene == Session.gameObject.scene && door.IsConfigured)
                    {
                        // An ambiguous scene is not proof that the actual gate is open.
                        if (demoEndingDoor != null) { demoEndingDoor = null; return false; }
                        demoEndingDoor = door;
                    }
            }
            if (demoEndingDoor == null || !demoEndingDoor.IsConfigured || !demoEndingDoor.OpenAnimationComplete) return false;
            Pause.Begin();
            Page = DemoEndingPage;
            SetHud(false);
            if (Map != null) Map.SetVisible(false);
            V.Clear(modalLayer);
            Content304BuildEnding();
            ApplyTextScale();
            PlayUi(Theme.ConfirmSound, .4f);
            return true;
        }

        /// <summary>#304 여정의 끝 (DESIGN §7.9, IMPLEMENTATION §7.15): the confirm-strip grammar at 1100x520 over the uniform veil
        /// α.66 - 부인 + 제목 Serif900 60 먹 + 산문 28 + 저장 meta, [저장하고 로비로] (initial selection, filled Enter, ink under-stroke)
        /// and [게임 종료]. On paper the focus flips to an ink underlay with paper text. Names kept: EndingPaper, ReturnLobby, QuitDemo.</summary>
        void Content304BuildEnding()
        {
            var s = Content304Style;
            V.EnsureCanvasChannels(canvas);
            FocusMark304.Attach(s, canvasRect);
            V.Dim(s, modalLayer, -1f, "EndingShade");
            var page = V.Page304(modalLayer, "EndingPage304");
            var paper = V.SpriteImage(page, "EndingPaper", s.Sprites.SheetStrip, s.Sprites.SheetStrip != null ? Color.white : s.Sheet, 410, 280, 1100, 520, 0f, 1f, true);
            var p = paper.transform;
            V.Seal(s, p, 84, 70, 64);
            V.Label(s, p, "Title", "황경 남문 개방", UiType304.Speaker60, s.Ink, 174, 60);
            var body = V.Label(s, p, "Body", "데모의 여정은 여기까지.\n\n함께해 주셔서 감사합니다.", UiType304.Prose28, s.Ink, 176, 152, 840, 0, TextAlignmentOptions.TopLeft, true);
            body.overflowMode = TextOverflowModes.Overflow;
            V.Label(s, p, "Saved", "남문 개방 · 완료 저장됨", UiType304.Meta20, s.Ash, 176, 322);
            var primary = V.FocusRow(s, p, "ReturnLobby", "저장하고 로비로", 88, 378, 560, 110, () => StartCoroutine(ReturnTitle()), new FocusRowSpec304
            {
                OnPaper = true, Role = UiType304.Title36, LabelX = 88, Underlay = StrokeClass304.WetM, UnderlayH = 122, UnderlayX = 0,
                Key = "Enter", KeySmall = false, UnderStroke = true, UnderStrokeW = 300, SoundTheme = Theme,
            });
            V.FocusRow(s, p, "QuitDemo", "게임 종료", 700, 378, 340, 110, () => Confirm("게임을 종료할까요?", "진행은 저장된다.", QuitApplication), new FocusRowSpec304
            {
                OnPaper = true, Role = UiType304.Label28, LabelX = 60, SoundTheme = Theme,
            });
            if (EventSystem.current != null) FocusMark304.Select(primary.Button.gameObject);
        }
    }
}
