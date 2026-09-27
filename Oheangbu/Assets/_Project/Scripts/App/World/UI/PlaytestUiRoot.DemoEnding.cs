using Oheangbu.App.Demo;
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
            V.Image(V.Stretch("EndingShade", modalLayer), new Color(.025f, .03f, .025f, .52f), null, true);
            var paper = V.Rect("EndingPaper", modalLayer, 0, 0, 1020, 620);
            paper.anchorMin = paper.anchorMax = paper.pivot = new Vector2(.5f, .5f);
            paper.anchoredPosition = Vector2.zero;
            V.Image(V.Stretch("Paper", paper), Theme.Paper, null, true);
            if (Theme.PaperTexture != null) V.Raw(V.Stretch("Fibers", paper), Theme.PaperTexture, new Color(1, 1, 1, .20f));
            V.Text(paper, "Seal", "五 行 符", Theme.Font, 24, Theme.Seal, 70, 50, 880, 44, TextAnchor.MiddleCenter);
            V.Text(paper, "Title", "황경 남문 개방", Theme.Font, 46, Theme.Ink, 60, 132, 900, 80, TextAnchor.MiddleCenter);
            V.Rule(paper, Theme, 145, 242, 730);
            V.Text(paper, "Body", "데모의 여정은 여기까지.\n\n함께해 주셔서 감사합니다.",
                Theme.Font, 27, Theme.Ink, 90, 285, 840, 155, TextAnchor.UpperCenter);
            var primary = V.Button(paper, "ReturnLobby", "저장하고 로비로", Theme, 115, 480, 380, 62,
                () => StartCoroutine(ReturnTitle()), true);
            V.Button(paper, "QuitDemo", "게임 종료", Theme, 525, 480, 380, 62,
                () => Confirm("게임을 종료할까요?", "진행은 저장된다.", QuitApplication));
            V.Text(paper, "Saved", "남문 개방 · 완료 저장됨", Theme.Font, 19, Theme.Muted, 80, 565, 860, 32, TextAnchor.MiddleCenter);
            ApplyTextScale();
            PlayUi(Theme.ConfirmSound, .4f);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(primary.gameObject);
            return true;
        }
    }
}
