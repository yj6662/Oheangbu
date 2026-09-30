using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 flow helpers (title card parts, confirm modal rules, journey loading overlay). Area prefix Flow304 on every
    /// member so partials of other areas cannot collide. No statics besides pure helpers / constants.</summary>
    public sealed partial class PlaytestUiRoot
    {
        /// <summary>Title card origin at 1080: LateUpdate centres the 904 px card vertically ((1080 - 904) / 2 = 88), x 102.</summary>
        const float Flow304CardX = 102f, Flow304CardY = 88f;

        string Flow304TitleReturn;              // title row that opened the page we come back from (설정 / 조작)
        int Flow304TitleScene = -1;             // scene handle of the last title build (bleed-in once per title scene)
        GameObject Flow304ConfirmReturn;        // selection to give back when the confirm closes
        Button Flow304ConfirmAccept;            // [확인] of the open confirm (Enter shortcut, display countdown)
        int Flow304CountdownShown = -1;         // last whole second written into the display countdown
        FlowProgressLine304 Flow304LoadingLine; // progress of the journey overlay (ShowLoading)
        readonly List<CanvasGroup> Flow304SuspendedGroups = new List<CanvasGroup>();
        readonly List<bool> Flow304SuspendedWas = new List<bool>();

        // 五行符 slip text: Serif900 40, line-height 1.18 (title.html); no table role has that size
        static TypeRole Flow304SlipRole => new TypeRole(UiType304.Region72, UiFont304.Serif900, 40f, 1.18f);

        // ------------------------------------------------------------------ title parts
        /// <summary>REFERENCE_BOARD D11 제첨 테: a 1.5 px ink single line 7 px inside the slip edge (stroke_line brush strokes, corners
        /// crossing 2 px), α.6. Ink, not cinnabar (the seal is the only red on the slip).</summary>
        static void Flow304SlipRim(UiStyle304SO s, RectTransform slip, float w, float h, float inset = 7f, float alpha = .6f)
        {
            const float lineH = 6f, over = 2f;   // stroke_line's core is ~25 % of its height -> ~1.5 px
            float x0 = inset, x1 = w - inset, y0 = inset, y1 = h - inset;
            V.Brush(s, slip, "RimTop", StrokeClass304.Line, s.Ink, x0 - over, y0 - lineH * .5f, x1 - x0 + 2f * over, lineH, alpha);
            V.Brush(s, slip, "RimBottom", StrokeClass304.Line, s.Ink, x0 - over, y1 - lineH * .5f, x1 - x0 + 2f * over, lineH, alpha, 0f, true);
            float len = y1 - y0 + 2f * over, cy = (y0 + y1) * .5f;
            V.Brush(s, slip, "RimLeft", StrokeClass304.Line, s.Ink, x0 - len * .5f, cy - lineH * .5f, len, lineH, alpha, 90f);
            V.Brush(s, slip, "RimRight", StrokeClass304.Line, s.Ink, x1 - len * .5f, cy - lineH * .5f, len, lineH, alpha, -90f);
        }

        /// <summary>Save line (152,924) / (152,950): state meta + "&lt;checkpoint&gt;에서 재시작" without a seal (removed at the user's request
        /// 2026-09-30). A damaged save shows only the cinnabar-lift state line.</summary>
        void Flow304TitleSaveLine(UiStyle304SO s, RectTransform card, WorldMacroSaveSlotInfo info, bool canContinue)
        {
            float x = 152f - Flow304CardX;
            if (info.Status == WorldMacroSaveSlotStatus.Invalid)
            {
                Flow304LabelAt(s, card, "SaveStatus", "저장 손상 · 원본은 보관됨", UiType304.MetaBold20, s.CinnabarLift, x, 924f - Flow304CardY);
                return;
            }
            if (!canContinue) return;   // no save: the disabled 이어하기 already says 저장 없음
            string state = info.Status == WorldMacroSaveSlotStatus.Temporary ? "중단된 저장 · 이어하기로 복구"
                : info.Status == WorldMacroSaveSlotStatus.Backup ? "백업 저장 · 이어하기로 복구" : "저장된 게임";
            Flow304LabelAt(s, card, "SaveStatus", state, UiType304.Meta20, s.Mist, x, 924f - Flow304CardY);
            var progress = info.Progress;
            string place = WorldMacroCheckpointRules.Label(Content, progress, progress != null && progress.ledger != null ? progress.ledger.checkpoint : null);
            Flow304LabelAt(s, card, "SavePlace", place + "에서 재시작", UiType304.Body22, s.Paper, x, 950f - Flow304CardY);
        }

        static TMP_Text Flow304LabelAt(UiStyle304SO s, Transform parent, string name, string text, UiType304 roleId, Color color, float x, float cssTop)
        {
            var role = s.Role(roleId);
            var t = V.Label(s, parent, name, text, role, color, x, 0f);
            V.Place(t.rectTransform, x, cssTop + role.CssTopOffset(t.font));
            return t;
        }

        // ------------------------------------------------------------------ confirm modal rules
        /// <summary>Suspends every page root (CanvasGroup) under the title / menu layers while the confirm is open: not interactable,
        /// selection released, default look kept (confirm.png). Remembers each group's flag for Flow304Resume.</summary>
        void Flow304Suspend()
        {
            Flow304Resume(false);
            foreach (var layer in new[] { baseLayer, modalLayer })
            {
                if (layer == null) continue;
                for (int i = 0; i < layer.childCount; i++)
                {
                    var g = layer.GetChild(i).GetComponent<CanvasGroup>();
                    if (g == null || !g.gameObject.activeInHierarchy) continue;
                    Flow304SuspendedGroups.Add(g); Flow304SuspendedWas.Add(g.interactable);
                    FocusMark304.Suspend(g, true);
                }
            }
        }

        void Flow304Resume(bool restoreSelection)
        {
            for (int i = 0; i < Flow304SuspendedGroups.Count; i++)
                if (Flow304SuspendedGroups[i] != null) Flow304SuspendedGroups[i].interactable = Flow304SuspendedWas[i];
            Flow304SuspendedGroups.Clear(); Flow304SuspendedWas.Clear();
            var back = Flow304ConfirmReturn; Flow304ConfirmReturn = null;
            if (!restoreSelection || back == null || !back.activeInHierarchy) return;
            var sel = back.GetComponent<Selectable>();
            if (sel == null || sel.interactable) FocusMark304.Select(back);
        }

        /// <summary>[돌아가기]. The filled [Enter] on [확인] is the confirm's main-action key, so an Enter submit that lands on the
        /// default-focused 돌아가기 accepts; pad A / Space / click keep the safe side.</summary>
        void Flow304CancelPressed()
        {
            var kb = Keyboard.current;
            bool enter = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            if (enter && Flow304ConfirmAccept != null && Flow304ConfirmAccept.IsInteractable()) { Flow304ConfirmAccept.onClick.Invoke(); return; }
            DismissConfirmation();
        }

        /// <summary>Display-settings countdown in the confirm body: "N초 뒤 이전 화면 설정으로 돌아갑니다", tabular digits,
        /// rewritten only when the whole second changes.</summary>
        void Flow304ShowCountdown()
        {
            if (confirmationBody == null || Settings == null) return;
            int seconds = Mathf.Max(0, Mathf.CeilToInt(Settings.PreviewSecondsRemaining));
            if (seconds == Flow304CountdownShown) return;
            Flow304CountdownShown = seconds;
            confirmationBody.text = "<mspace=0.6em>" + seconds + "</mspace>초 뒤 이전 화면 설정으로 돌아갑니다";
        }

        // ------------------------------------------------------------------ journey overlay (ShowLoading)
        /// <summary>Opaque 먹장막 over the menu canvas (ink edges bleed inward, VeilMs), centred: 부인 64, the label Title36 paper
        /// ("여정 기록됨" / "여정 준비" / "원본 보존 · 로비로" / any harness label) and the thin progress line (640). #304 QA: the seal
        /// used to show only for the exact "여정 기록됨" string, so every other label was bare text + line on black
        /// (after/loading.png); the seal is now the overlay's fixed centre mark and the group keeps one layout. Seals are off by
        /// user request (UiStyle304SO.ShowSeals, 2026-09-30): V.Seal then returns null and the label/line keep the no-seal top 498.</summary>
        void Flow304ShowLoading(string label)
        {
            V.Clear(modalLayer); Page = "";
            var es = EventSystem.current; if (es != null) es.SetSelectedGameObject(null);
            var s = V.Style(Theme);
            V.EnsureCanvasChannels(canvasRect != null ? canvasRect.GetComponentInParent<Canvas>() : null);
            var veil = V.Dim(s, modalLayer, 1f, "LoadingVeil");
            var page = V.Page304(modalLayer, "LoadingPage");
            var role = s.Role(UiType304.Title36);
            // seal 64 (456..520) + 20 + label 36 x 1.2 + 36 + line: the group's middle sits on the screen's middle (~545)
            const float sealSize = 64f, sealGap = 20f, lineGap = 36f;
            float top = 456f;
            if (V.Seal(s, page, 960f - sealSize * .5f, top, sealSize) != null) top += sealSize + sealGap;
            else top = 498f;   // no seal (ShowSeals off): label + line centred on their own, as before the seal row
            loadingText = V.Label(s, page, "LoadingLabel", label ?? "", role, s.Paper, 460f, 0f, 1000f, 0f, TextAlignmentOptions.Top);
            V.Place(loadingText.rectTransform, 460f, top + role.CssTopOffset(loadingText.font));
            float lineY = top + role.Size * role.LineHeight + lineGap;
            Flow304LoadingLine = FlowProgressLine304.Create(s, page, "ProgressLine", 640f, lineY, 640f, s.Paper);
            if (!Application.isPlaying) return;
            var fx = InkRevealEffect.On(veil, s, InkRevealMode.Edges, 0f);
            UiTween304.RevealIn(fx, s, s.Motion.VeilMs, UiTween304.Token(fx)).Forget();
            UiTween304.BleedIn(page.GetComponent<CanvasGroup>(), s, 1, UiTween304.Token(page)).Forget();
        }

        void Flow304LoadingProgress(float value)
        {
            if (Flow304LoadingLine != null) Flow304LoadingLine.SetValue(value);
        }
    }
}
