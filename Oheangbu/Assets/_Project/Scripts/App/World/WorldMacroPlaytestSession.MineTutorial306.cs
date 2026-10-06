using System;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #306 #11 폐광 튜토리얼 연결(SPEC-PLAYTEST-306 #11, D306, TEST). 감독(MineTutorialDirector)은 이 세션이 소유한다(싱글턴 없음).
    // 카드는 TutorialCardRequested로 UI에 넘기고(트랙 S가 올리고 트랙 U가 보인다), 닫힘은 MineTutorialCardClosed로 돌아온다.
    // 원장 표식 = ledger.completed의 tutorial.mine.* — 새 저장(LoadStatus "new")만 EnrolledId를 받고 카드를 본다.
    // 튜토리얼 보스(mine_tutorial_boss)가 씬에 있으면 그 자리의 옛 첫 몹 mine_beast/0은 나오지 않는다(배치 해제 없이 가용성만).
    public sealed partial class WorldMacroPlaytestSession : MineTutorialDirector.IHost
    {
        public const string MineTutorialReplacedId = "mine_beast/0";
        [Tooltip("#306 #11 폐광 튜토리얼 박자 [TEST]. 비우면 튜토리얼 없음(보스는 자체 순서로 싸운다)")]
        public MineTutorialProfileSO MineTutorialProfile;
        /// <summary>UI → session: false = 설정 "익힘 멈춤" 꺼짐(카드 없음, 본 것으로 친다).</summary>
        [NonSerialized] public bool TutorialCardsAllowed = true;
        /// <summary>Card request (track U answers true when the card is now on screen).</summary>
        public event Func<MineTutorialCard306, bool> TutorialCardRequested;
        MineTutorialDirector mineTutorial;
        Riposte301 mineTutorialRiposte;
        public MineTutorialDirector MineTutorial => mineTutorial;
        public MineBossController MineTutorialBoss { get; private set; }

        bool MineTutorialBossPresent => MineTutorialBoss != null;

        void BindMineTutorial306()
        {
            mineTutorial?.Unbind(); mineTutorial = null; MineTutorialBoss = null;
            foreach (var actor in Actors ?? Array.Empty<Oheangbu.App.Prologue.PrologueEncounter>())
            {
                if (actor == null || actor.Id != MineTutorialProfileSO.BossId || actor.gameObject.scene != gameObject.scene) continue;
                MineTutorialBoss = actor.GetComponent<MineBossController>(); break;
            }
            if (MineTutorialProfile == null || MineTutorialBoss == null) return;
            if (!MineTutorialProfile.TryValidate(out var error)) { Debug.LogError("[MineTutorial306] " + error, this); return; }
            mineTutorial = new MineTutorialDirector(MineTutorialProfile, this);
            var motor = Walker != null ? Walker.Motor : null;
            mineTutorial.Bind(MineTutorialBoss, motor != null ? motor.GetComponent<LockOn>() : null, motor, ink, parry,
                Walker != null && Walker.Body != null ? Walker.Body.transform : null);
        }

        void TickMineTutorial306() { mineTutorial?.Tick(); }
        void ResetMineTutorial306() { mineTutorial?.OnCombatReset(); }
        void UnbindMineTutorial306() { mineTutorial?.Unbind(); mineTutorial = null; }

        /// <summary>The UI closed the card it showed (skipAll = Esc held).</summary>
        public void MineTutorialCardClosed(string beatId, bool skipAll)
        {
            mineTutorial?.OnCardClosed(beatId, skipAll);
            if (skipAll && ready && !SaveBlocked) SaveNow(out _);
        }

        void EnrollMineTutorial306(WorldMacroProgress progress)
        {
            if (LoadStatus == "new" && MineTutorialProfile != null && MineTutorialProfile.Enabled && progress?.ledger?.completed != null &&
                !progress.ledger.completed.Contains(MineTutorialProfileSO.EnrolledId))
                progress.ledger.completed.Add(MineTutorialProfileSO.EnrolledId);
        }

        // ---- MineTutorialDirector.IHost ----
        bool MineTutorialDirector.IHost.Enrolled => Progress?.ledger?.completed != null && Progress.ledger.completed.Contains(MineTutorialProfileSO.EnrolledId);
        bool MineTutorialDirector.IHost.Seen(string ledgerId) => Progress?.ledger?.completed != null && Progress.ledger.completed.Contains(ledgerId);
        void MineTutorialDirector.IHost.MarkSeen(string ledgerId)
        {
            // saved by the card's own page open (OpenPage -> SaveNow) or the next autosave; a card never shows twice in one run either way
            if (Progress?.ledger?.completed != null && !Progress.ledger.completed.Contains(ledgerId)) Progress.ledger.completed.Add(ledgerId);
        }
        bool MineTutorialDirector.IHost.CanShowCard
        {
            get
            {
                if (!ready || respawning || GameplayInputBlocked || Walker == null || Walker.Seated || vitals == null || vitals.Hp01 <= 0) return false;
                if (Walker.Drawing != null && Walker.Drawing.InDrawMode || Walker.Motor != null && Walker.Motor.IsDrawing) return false;
                if (mineTutorialRiposte == null) mineTutorialRiposte = Walker.GetComponent<Riposte301>();
                if (mineTutorialRiposte != null && mineTutorialRiposte.Active) return false;   // hitstop owns timeScale meanwhile
                if (DeathPresentation != null && DeathPresentation.IsActive) return false;
                return true;
            }
        }
        bool MineTutorialDirector.IHost.ShowCard(MineTutorialCard306 card)
        {
            if (!TutorialCardsAllowed) return false;
            var handlers = TutorialCardRequested; if (handlers == null) return false;
            foreach (Func<MineTutorialCard306, bool> h in handlers.GetInvocationList())
            {
                try { if (h(card)) return true; }
                catch (Exception e) { Debug.LogException(e, this); }
            }
            return false;
        }
        void MineTutorialDirector.IHost.Line(string text) { if (!string.IsNullOrEmpty(text)) Show(text); }
    }
}
