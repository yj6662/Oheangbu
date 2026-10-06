using System;
using System.Collections.Generic;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #306 #11 폐광 튜토리얼 감독(SPEC-PLAYTEST-306 #11, D306, TEST). 세션이 소유하는 평범한 객체 — 싱글턴·정적 상태 없음.
    // 보스 신호(예고·종료·명중·그로기·스턴 끝)·락온·피격·먹을 듣고, 박자마다 카드 한 장을 요청하고, 카드 뒤에는 보스만 기다리게 한다.
    //   카드 멈춤 = UI가 PauseCoordinator.Begin/End로만(이 객체는 timeScale을 쓰지 않는다)
    //   기다림 = 보스 자기 시계(MineBossController.Hold) · 파편은 방어막이 설 때까지 3 m 앞에서(최대 6초)
    //   작도 중·앞잡 히트스톱 중·메뉴 중에는 카드를 미룬다(Host.CanShowCard).
    // 순서(PLAN §2-11): 알아봄 L1 → 들이받기 L2 → 긋기 L3 → 파편 L4(되풀이 ≤2) → 그로기 L5 → 결정 고리 L7 → 자유 싸움.
    public sealed class MineTutorialDirector
    {
        public interface IHost
        {
            bool Enrolled { get; }
            bool Seen(string ledgerId);
            void MarkSeen(string ledgerId);
            bool CanShowCard { get; }
            /// <summary>true = the card is now on screen and OnCardClosed will follow; false = nothing shows (treated as seen).</summary>
            bool ShowCard(MineTutorialCard306 card);
            void Line(string text);
        }

        public enum Step { Approach, LockWait, ChargeLesson, DrawWait, ParryLesson, AfterGroggy, RingLesson, Free, Done }

        readonly MineTutorialProfileSO profile;
        readonly IHost host;
        readonly Queue<MineTutorialProfileSO.Beat> pending = new Queue<MineTutorialProfileSO.Beat>();
        MineBossController boss;
        PrologueEncounter encounter;
        LockOn lockOn;
        PlayerMotor motor;
        InkPool ink;
        ParryJudge judge;
        Transform player;
        string showing, awaitingCard;
        float waitDeadline = float.PositiveInfinity, stillClock;
        Vector3 stillFrom;
        bool stillDone, playerHit, successLine, generatesLine;
        int shards;

        public Step Current { get; private set; } = Step.Approach;
        public string ShowingBeat => showing;
        public int PendingCount => pending.Count;
        public int CardsShown { get; private set; }
        public MineBossController Boss => boss;

        public MineTutorialDirector(MineTutorialProfileSO profile, IHost host)
        {
            if (profile == null || host == null) throw new ArgumentNullException(profile == null ? nameof(profile) : nameof(host));
            this.profile = profile; this.host = host;
        }

        public static string LedgerId(MineTutorialProfileSO.Beat beat) => MineTutorialProfileSO.IdPrefix + beat.Id;

        public void Bind(MineBossController boss, LockOn lockOn, PlayerMotor motor, InkPool ink, ParryJudge judge, Transform player)
        {
            Unbind();
            this.boss = boss; this.lockOn = lockOn; this.motor = motor; this.ink = ink; this.judge = judge; this.player = player;
            encounter = boss != null ? boss.GetComponent<PrologueEncounter>() : null;
            stillFrom = player != null ? player.position : Vector3.zero;
            if (boss == null) return;
            boss.MoveTelegraphed += OnTelegraphed; boss.MoveEnded += OnEnded; boss.MoveImpact += OnImpact;
            boss.GroggyFilled += OnGroggy; boss.StunEnded += OnStunEnded; boss.ShardHoldGate = ShardGate;
            if (boss.Vitals != null) { boss.Vitals.DamageResolved += OnBossDamaged; boss.Vitals.Died += OnBossDied; }
            if (encounter != null) encounter.PlayerDetected += OnSighted;
            if (boss.Vitals != null && !boss.Vitals.IsAlive) Current = Step.Done;
        }

        public void Unbind()
        {
            if (boss != null)
            {
                boss.MoveTelegraphed -= OnTelegraphed; boss.MoveEnded -= OnEnded; boss.MoveImpact -= OnImpact;
                boss.GroggyFilled -= OnGroggy; boss.StunEnded -= OnStunEnded; boss.ShardHoldGate = null; boss.Hold = false;
                if (boss.Vitals != null) { boss.Vitals.DamageResolved -= OnBossDamaged; boss.Vitals.Died -= OnBossDied; }
            }
            if (encounter != null) encounter.PlayerDetected -= OnSighted;
            boss = null; encounter = null;
        }

        /// <summary>Death, rest or escort restore: the fight starts over (cards already seen stay seen), and the first one shows the rest card.</summary>
        public void OnCombatReset()
        {
            pending.Clear(); awaitingCard = null; waitDeadline = float.PositiveInfinity; playerHit = false; shards = 0;
            successLine = generatesLine = false;
            if (boss != null) { boss.Hold = false; boss.Force(MineBossMove.None); boss.ResetEncounter(); }
            Current = boss != null && boss.Vitals != null && boss.Vitals.IsAlive ? Step.Approach : Step.Done;
            Trigger(MineTutorialSignal.FirstRespawn);
        }

        /// <summary>The UI closed the card (skipAll = Esc held: every remaining beat counts as seen).</summary>
        public void OnCardClosed(string beatId, bool skipAll)
        {
            if (beatId != showing) return;
            showing = null;
            if (skipAll)
            {
                foreach (var b in profile.Beats) if (b != null) host.MarkSeen(LedgerId(b));
                pending.Clear();
            }
            if (awaitingCard == beatId) { awaitingCard = null; waitDeadline = Time.unscaledTime + WaitSecondsFor(beatId); }
        }

        public void Tick()
        {
            if (!profile.Enabled) return;
            TickStandingStill();
            if (ink != null && boss != null && Current != Step.Done && Current != Step.Approach && ink.Value < profile.InkLowFraction)
                Trigger(MineTutorialSignal.InkLow);
            TickWait();
            if (showing == null && pending.Count > 0 && host.CanShowCard) ShowNext();
        }

        void TickStandingStill()
        {
            if (stillDone || player == null) return;
            if (!host.Enrolled || host.Seen(MineTutorialProfileSO.IdPrefix + "t0_walk")) { stillDone = true; return; }
            Vector3 d = player.position - stillFrom; d.y = 0;
            if (d.magnitude > profile.StandingStillMetres) { stillDone = true; return; }
            if (!host.CanShowCard) return;
            stillClock += Time.unscaledDeltaTime;
            if (stillClock >= profile.StandingStillSeconds) { stillDone = true; Trigger(MineTutorialSignal.StandingStill); }
        }

        void TickWait()
        {
            if (boss == null || awaitingCard != null) return;
            bool expired = Time.unscaledTime >= waitDeadline;
            switch (Current)
            {
                case Step.LockWait:
                    if (expired || (lockOn != null && lockOn.IsLocked)) { Release(); Current = Step.ChargeLesson; boss.Force(MineBossMove.Charge); }
                    break;
                case Step.DrawWait:
                    if (expired || playerHit) { Release(); Current = Step.ParryLesson; shards = 0; boss.Force(MineBossMove.Shard); }
                    break;
            }
        }

        void Release() { waitDeadline = float.PositiveInfinity; if (boss != null) boss.Hold = false; }

        void BeginWait(Step step, MineTutorialSignal signal)
        {
            Current = step; boss.Hold = true;
            var beat = profile.Find(signal);
            if (Trigger(signal)) { awaitingCard = beat.Id; waitDeadline = float.PositiveInfinity; }
            else { awaitingCard = null; waitDeadline = Time.unscaledTime; }
        }

        float WaitSecondsFor(string beatId)
        {
            foreach (var b in profile.Beats) if (b != null && b.Id == beatId) return b.Wait == MineTutorialWait.None ? 0f : b.WaitSeconds;
            return 0f;
        }

        // ---- signals ----

        void OnSighted()
        {
            if (Current != Step.Approach || boss == null || boss.Vitals == null || !boss.Vitals.IsAlive) return;
            BeginWait(Step.LockWait, MineTutorialSignal.BossSighted);
        }

        void OnTelegraphed(MineBossMove move)
        {
            if (move == MineBossMove.Charge || move == MineBossMove.Sweep) Trigger(MineTutorialSignal.NeutralTelegraph);
            else if (move == MineBossMove.Shard) { Trigger(MineTutorialSignal.ElementalShard); if (Current == Step.ParryLesson) shards++; }
            else if (move == MineBossMove.CrystalRing) Trigger(MineTutorialSignal.GroundRing);
            // a fight that skipped ahead (the player attacked before being seen) still follows the order
            if (Current == Step.Approach) Current = Step.ChargeLesson;
        }

        void OnEnded(MineBossMove move, bool cancelled)
        {
            if (boss == null) return;
            if (cancelled)
            {
                // a lesson move cut short (sight lost, stun) comes again
                if (Current == Step.ChargeLesson && (move == MineBossMove.Charge || move == MineBossMove.Sweep)) boss.Force(MineBossMove.Charge);
                else if (Current == Step.ParryLesson && move == MineBossMove.Shard) { shards = Mathf.Max(0, shards - 1); boss.Force(MineBossMove.Shard); }
                else if (Current == Step.RingLesson && move == MineBossMove.CrystalRing) boss.Force(MineBossMove.CrystalRing);
                return;
            }
            switch (Current)
            {
                case Step.ChargeLesson:
                    if (move == MineBossMove.Charge || move == MineBossMove.Sweep) { playerHit = false; BeginWait(Step.DrawWait, MineTutorialSignal.AfterNeutral); }
                    break;
                case Step.ParryLesson:
                    if (move != MineBossMove.Shard || boss.Groggied) break;
                    if (shards <= profile.ParryRepeats) boss.Force(MineBossMove.Shard);
                    else { Current = Step.RingLesson; boss.Force(MineBossMove.CrystalRing); }
                    break;
                case Step.RingLesson:
                    if (move == MineBossMove.CrystalRing) { Current = Step.Free; boss.Force(MineBossMove.None); }
                    break;
            }
        }

        void OnImpact(MineBossMove move, EnemyAttackImpact impact)
        {
            if (move != MineBossMove.Shard || !host.Enrolled) return;
            if (impact.Outcome == ParryOutcome.Success && !successLine) { successLine = true; host.Line(profile.ParrySuccessLine); }
            else if (impact.Outcome == ParryOutcome.Fail && !generatesLine) { generatesLine = true; host.Line(profile.ParryGeneratesLine); }
        }

        void OnGroggy()
        {
            Trigger(MineTutorialSignal.GroggyFull);
            if (Current == Step.ParryLesson || Current == Step.ChargeLesson || Current == Step.DrawWait) { Release(); Current = Step.AfterGroggy; boss.Force(MineBossMove.None); }
        }

        void OnStunEnded()
        {
            if (Current == Step.AfterGroggy) { Current = Step.RingLesson; boss.Force(MineBossMove.CrystalRing); }
        }

        void OnBossDamaged(EnemyDamageResult result)
        {
            if (result.AppliedDamage > 0 && (result.Attack.Source == DamageSource.PlayerDirect || result.Attack.Source == DamageSource.Summon)) playerHit = true;
        }

        void OnBossDied() { Current = Step.Done; pending.Clear(); awaitingCard = null; Release(); }

        bool ShardGate()
        {
            if (Current != Step.ParryLesson || showing != null) return false;
            if (judge != null && judge.GuardInWindow(Time.time)) return false;   // a standing guard meets the shard now
            if (motor != null && motor.IsDodging) return false;
            return true;
        }

        // ---- cards ----

        bool Trigger(MineTutorialSignal signal)
        {
            if (!profile.Enabled || !host.Enrolled) return false;
            var beat = profile.Find(signal);
            if (beat == null || host.Seen(LedgerId(beat)) || beat.Id == showing) return false;
            foreach (var b in pending) if (b == beat) return true;
            pending.Enqueue(beat); return true;
        }

        void ShowNext()
        {
            var beat = pending.Dequeue();
            if (host.Seen(LedgerId(beat))) { if (awaitingCard == beat.Id) { awaitingCard = null; waitDeadline = Time.unscaledTime; } return; }
            host.MarkSeen(LedgerId(beat));
            var card = new MineTutorialCard306
            {
                BeatId = beat.Id, Title = beat.Title ?? "", Body = beat.Body ?? "", Letter = beat.Letter ?? "",
                Keys = beat.Keys ?? Array.Empty<string>(), MinSeconds = profile.CardMinSeconds, SkipHoldSeconds = profile.SkipHoldSeconds
            };
            showing = beat.Id; CardsShown++;
            if (!host.ShowCard(card)) OnCardClosed(beat.Id, false);
        }
    }
}
