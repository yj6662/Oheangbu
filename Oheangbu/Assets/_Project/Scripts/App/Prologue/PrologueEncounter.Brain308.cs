using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.Prologue
{
    // #308 D308-70 (SPEC-ENEMY-BRAIN-308, TEST): the detailed behaviour of an ordinary field enemy, switched on by a filled-in
    // EnemyBrainProfile308 (its own, or the one the session hands out from the content asset). Bosses (dragon, general, tutorial
    // boss, growth lesson) never use it. Current keeps its four values, so everything that asks "is somebody chasing?" reads the
    // same answer; the finer state is Phase.
    //   Patrol : sees in a cone ahead, senses close by all around, wakes on a blow or on a neighbour's call
    //   Chase  : Notice (stops, turns to the player) -> Approach -> Wait (outside its reach until it gets a turn) -> Engage (its turn:
    //            steps in and attacks) ; Search (lost sight: goes to where it last saw the player and looks around) -> Return
    // The body faces the player only close in or standing; while it travels it faces where it goes (no walking sideways).
    // Attack shapes, clocks, damage, parry and dodge stay with EnemyController: this only says whether a new attack may start.
    public sealed partial class PrologueEncounter
    {
        public enum Phase308 { None, Notice, Approach, Wait, Engage, Search }

        [Tooltip("#308 D308-70: this actor's own behaviour profile. None = the one the session hands out (WorldMacroPlaytestSO.EnemyBrain); none there either = the behaviour before.")]
        public EnemyBrainProfile308 Brain;
        public Phase308 Phase { get; private set; }
        public bool BrainActive308 => Brain != null && boss == null && general == null && tutorialBoss == null && growth == null && Brain.Usable;
        public bool HoldsTurn308 => pack308 != null && pack308.Holds(this);
        public Vector3 LastKnown308 => lastKnown;
        // Read-only record for the Play check: seconds this actor travelled faster than 0.5 m/s, and how many of them with the body
        // more than 35 degrees off its direction of travel (walking sideways or backwards under a forward gait).
        public float MovedSeconds308 { get; private set; }
        public float SideSlipSeconds308 { get; private set; }

        EnemyPack308 pack308; Transform packPlayer308;
        Vector3 lastKnown, slipPrevious;
        float noticeUntil, alertAt, turnClaimedAt, retakeAt, searchArrivedAt = -1f, searchYaw;
        bool alertPending, called, attackSeen, slipHas;

        EnemyPack308 Pack308()
        {
            if (pack308 != null && packPlayer308 == Player) return pack308;
            if (pack308 != null) pack308.Leave(this);
            packPlayer308 = Player; pack308 = Player != null ? Player.GetComponent<EnemyPack308>() : null;
            if (pack308 == null && Player != null) pack308 = Player.gameObject.AddComponent<EnemyPack308>();
            if (pack308 != null) pack308.Join(this);
            return pack308;
        }

        void ReleaseTurn308(float now, bool retake)
        {
            if (pack308 != null && pack308.Holds(this)) { pack308.Release(this); if (retake && Brain != null) retakeAt = now + Brain.RetakeSeconds; }
            attackSeen = false;
        }

        void LeaveBrain308()
        {
            if (pack308 != null) pack308.Leave(this);
            pack308 = null; packPlayer308 = null; Phase = Phase308.None; alertPending = called = attackSeen = false; searchArrivedAt = -1f;
        }

        void ResetBrain308()
        {
            ReleaseTurn308(Time.time, false);
            Phase = Phase308.None; alertPending = called = false; retakeAt = 0f; searchArrivedAt = -1f; slipHas = false;
            MovedSeconds308 = SideSlipSeconds308 = 0f;
        }

        void Damaged308(EnemyDamageResult result)
        {
            if (!BrainActive308 || !Brain.AlertOnDamage || Player == null || result.Target != vitals || result.AppliedDamage <= 0f || result.Killed) return;
            if (Current == Behaviour.Patrol) { if (!alertPending || Time.time < alertAt) { alertPending = true; called = false; alertAt = Time.time; } lastKnown = Player.position; }
            // already hunting but blind: the blow tells where the player is, and the search starts over from there
            else if (Current == Behaviour.Chase && Phase == Phase308.Search) { lastKnown = Player.position; lastSeen = Time.time - Brain.LoseSightSeconds; searchArrivedAt = -1f; }
        }

        /// <summary>A neighbour that noticed the player calls this actor (EnemyPack308). Only a patrolling actor listens.</summary>
        public void HearCall308(Vector3 known, float at)
        {
            if (!BrainActive308 || Current != Behaviour.Patrol || alertPending) return;
            alertPending = true; called = true; alertAt = at; lastKnown = known;
        }

        void Slip308()
        {
            Vector3 position = transform.position; float dt = Time.deltaTime;
            if (slipHas && dt > 0f)
            {
                Vector3 travel = Vector3.ProjectOnPlane(position - slipPrevious, Vector3.up); float speed = travel.magnitude / dt;
                if (speed > .5f && speed < 12f)
                {
                    MovedSeconds308 += dt;
                    if (Vector3.Angle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), travel) > 35f) SideSlipSeconds308 += dt;
                }
            }
            slipPrevious = position; slipHas = true;
        }

        void BrainFrame308()
        {
            // standing where the player was last seen: look left and right
            if (Current == Behaviour.Chase && Phase == Phase308.Search && searchArrivedAt >= 0f && Brain.SearchLookPeriodSeconds > 0f)
                transform.rotation = Quaternion.Euler(0f, searchYaw + Mathf.Sin((Time.time - searchArrivedAt) / Brain.SearchLookPeriodSeconds * Mathf.PI * 2f) * Brain.SearchLookDegrees, 0f);
        }

        void TickBrain308(float distance)
        {
            float now = Time.time; var b = Brain; var pack = Pack308();
            attacks.TurnDegreesPerSecond = b.TurnDegreesPerSecond;
            Vector3 to = Vector3.ProjectOnPlane(Player.position - transform.position, Vector3.up);
            float facing = to.sqrMagnitude < .0001f ? 0f : Vector3.Angle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), to);
            bool sight = attacks.HasLineOfSight();
            // unaware: a cone ahead, plus anything close by. Aware: all around, as before.
            bool sensed = Current == Behaviour.Patrol
                ? sight && (distance < b.ProximityRadius || distance < DetectionRange && facing <= b.VisionHalfAngleDegrees)
                : distance < DetectionRange && sight;
            if (sensed) { lastSeen = now; lastKnown = Player.position; }

            if (Current == Behaviour.Patrol && (sensed || alertPending && now >= alertAt))
            {
                bool calls = sensed || !called;        // an actor that only came because it was called does not call on
                if (!sensed) lastSeen = now;           // woken by a blow or a call: the give-up clock starts now, heading for the reported place
                Current = Behaviour.Chase; Phase = Phase308.Notice; noticeUntil = now + b.NoticeSeconds;
                alertPending = called = false; searchArrivedAt = -1f;
                PlayerDetected?.Invoke();
                if (calls && b.CallRadius > 0f && pack != null) pack.Call(this, lastKnown, b.CallRadius, now + b.CallDelaySeconds);
            }
            if (Current == Behaviour.Chase && (Vector3.Distance(Player.position, home) > Leash || now - lastSeen > b.GiveUpSeconds))
            { Current = Behaviour.Return; attacks.StopAttack(); }

            if (Current != Behaviour.Chase)
            {
                // patrol and the way home: nothing to attack, and the body follows its path
                Phase = Phase308.None; ReleaseTurn308(now, false);
                attacks.AttackEnabled = false; attacks.FaceTarget = false; agent.updateRotation = true;
                agent.speed = Speed * vitals.Control.MovementScale(now) * (Current == Behaviour.Patrol ? PatrolSpeedScale : 1f);
                agent.isStopped = attacks.AttackInProgress;
                if (agent.isStopped) return;
                if (Current == Behaviour.Return) StepReturn308(); else StepPatrol();
                return;
            }

            bool attacking = attacks.AttackInProgress;
            if (Phase == Phase308.Notice && now < noticeUntil && !attacking)
            {
                // the beat the player reads: it stops and turns
                attacks.AttackEnabled = false; attacks.FaceTarget = true; agent.updateRotation = false; agent.isStopped = true;
                return;
            }

            bool ranged = Ranged || attacks.EffectiveAttackMode == EnemyController.AttackMode.RangedOnly;
            float range = attacks.AttackRange;
            bool searching = !sensed && now - lastSeen > b.LoseSightSeconds;

            // attack turns: at most so many actors of a kind are in an attack on this player, and not right on one another's heels
            bool holds = pack != null && pack.Holds(this);
            if (attacking) attackSeen = true;
            if (holds && (attackSeen ? !attacking : now - turnClaimedAt > b.TurnClaimSeconds)) { ReleaseTurn308(now, true); holds = false; }
            if (!holds && !attacking && sensed && now >= retakeAt && attacks.AttackReady && distance <= range + b.ClaimMargin && pack != null &&
                pack.TryClaim(this, ranged, ranged ? b.MaxRangedAttackers : b.MaxMeleeAttackers, b.TurnGapSeconds, now))
            { holds = true; turnClaimedAt = now; attackSeen = false; }
            // an attack under way is never cut by the turn rule; a new one needs the turn and the body pointing at the player
            attacks.AttackEnabled = sensed && (attacking || holds && facing <= b.AttackFacingDegrees);

            Phase = searching ? Phase308.Search : holds || attacking ? Phase308.Engage : distance <= range + b.ClaimMargin ? Phase308.Wait : Phase308.Approach;
            bool moving = agent.velocity.sqrMagnitude > .04f;
            bool face = !searching && sensed && (distance <= range + b.FaceMargin || !moving);
            attacks.FaceTarget = face; agent.updateRotation = !face;

            agent.speed = Speed * vitals.Control.MovementScale(now);
            agent.isStopped = attacking;
            if (attacking) return;
            if (searching)
            {
                agent.stoppingDistance = .3f; agent.SetDestination(lastKnown);
                if (!agent.pathPending && agent.remainingDistance <= Mathf.Max(.35f, b.SearchArriveMeters))
                {
                    if (searchArrivedAt < 0f) { searchArrivedAt = now; searchYaw = transform.eulerAngles.y; }
                    agent.isStopped = true; agent.updateRotation = false;   // BrainFrame308 turns it
                }
                return;
            }
            searchArrivedAt = -1f;
            float stop = PreferredDistance > 0 ? PreferredDistance : Ranged ? 9 : 1.7f;      // as before
            if (!ranged && !holds) stop = Mathf.Max(stop, range + b.WaitRingMargin);        // waits outside its reach; never backs away
            agent.stoppingDistance = stop; agent.SetDestination(sensed ? Player.position : lastKnown);
        }
    }
}
