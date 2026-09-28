using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    public sealed partial class DemoSummonCombatManager
    {
        sealed class ClubTarget
        {
            public EnemyVitals Target;
            public uint Revision;
            public float ContactAt, Angle;
            public bool Resolved;
        }
        void HandleClubTarget(Actor actor, float dt)
        {
            if (HorizontalDistance(actor.Root.transform.position, actor.Target.transform.position) <= actor.Profile.ClubRange && Sight(actor, actor.Target))
            { BeginClubAttack(actor, dt); return; }
            actor.Clock.SetActivityPhase(SummonPhase.Approaching);
            Navigate(actor, actor.Target.transform.position, dt, actor.Profile.ClubRange * .8f);
        }
        void BeginClubAttack(Actor actor, float dt)
        {
            var p = actor.Profile;
            float duration = p.ClubWindupSeconds + p.ClubSweepSeconds + p.ClubRecoverySeconds;
            if (!actor.Clock.CanAttack || actor.Clock.ActivityRemaining <= duration ||
                !TryFace(actor, actor.Target.transform.position, dt * 240)) return;
            Vector3 origin = actor.Root.transform.position + Vector3.up * p.ClubOriginHeight;
            if (!FlameOriginClear(origin)) return;
            var plan = new SummonClubAttackPlan(origin, actor.Root.transform.forward, p.ClubRange, p.ClubHalfAngleDegrees,
                p.ClubHeightTolerance, actor.Clock.Elapsed, p.ClubWindupSeconds, p.ClubSweepSeconds, p.ClubRecoverySeconds);
            if (!plan.Contains(ClubAimPoint(actor.Target))) return;
            var captured = new List<ClubTarget>();
            var candidates = Wiring.SummonTargets;
            for (int i = 0; i < candidates.Count; i++)
            {
                var target = candidates[i];
                if (!Eligible(actor, target) || !plan.Contains(ClubAimPoint(target))) continue;
                var point = ClubAimPoint(target);
                captured.Add(new ClubTarget { Target = target, Revision = target.LifeRevision,
                    ContactAt = plan.ContactAt(point), Angle = plan.SignedAngle(point) });
            }
            captured.Sort((a, b) => a.ContactAt.CompareTo(b.ContactAt));
            var strike = AttackProvenance.Create(actor.Root, DamageSource.Summon, actor.Cast.Element);
            if (!actor.Clock.TryBeginAttack(strike.AttackId, actor.Target.GetInstanceID(), actor.Target.LifeRevision,
                p.ClubWindupSeconds, Mathf.Max(p.CooldownSeconds, p.ClubSweepSeconds + p.ClubRecoverySeconds))) return;
            actor.Strike = strike; actor.StrikeRevision = actor.Target.LifeRevision;
            actor.ClubReleaseTargetId = actor.Target.GetInstanceID(); actor.ClubPlan = plan;
            actor.ClubTargets = captured.ToArray(); actor.ClubReleased = false; actor.AttackIsRoot = false;
            actor.AttackStarted = plan.StartedAt; actor.AttackDuration = duration; actor.RecoveryUntil = plan.EndAt;
            actor.HasPath = false; AttackStarts++; ClubAttackStarts++;
        }
        void TickClubAttack(Actor actor)
        {
            var plan = actor.ClubPlan; plan.AdvanceTo(actor.Clock.Elapsed);
            if (plan.IsCancelled) return;
            var p = actor.Profile;
            if (HorizontalDistance(actor.Root.transform.position, Wiring.SummonPlayer.position) > p.LeashRange ||
                !FootSupport(p, actor.Root.transform.position, actor.Root.transform.rotation) ||
                !VolumeClear(p, actor.Root.transform.position, actor.Root.transform.rotation, true) || !FlameOriginClear(plan.Origin))
            { CancelClubAttack(actor); return; }
            if (actor.Clock.Elapsed < plan.SweepAt) return;
            if (!actor.ClubReleased)
            {
                if (!actor.Clock.TryConsumeHit(actor.Strike.AttackId, actor.ClubReleaseTargetId, actor.StrikeRevision, true, true, true))
                {
                    if (actor.Clock.PendingAttackId == actor.Strike.AttackId) return;
                    CancelClubAttack(actor); return;
                }
                actor.ClubReleased = true; ConsumedStrikes++;
            }
            foreach (var snapshot in actor.ClubTargets)
            {
                if (snapshot.Resolved || actor.Clock.Elapsed < snapshot.ContactAt) continue;
                snapshot.Resolved = true;
                var target = snapshot.Target;
                if (target == null || !target.isActiveAndEnabled || !target.IsAlive || target.LifeRevision != snapshot.Revision ||
                    !Registered(target) || !Eligible(actor, target)) continue;
                var point = ClubAimPoint(target);
                if (!plan.Contains(point) || Mathf.Abs(Mathf.DeltaAngle(plan.SignedAngle(point), snapshot.Angle)) > p.ClubAngularToleranceDegrees ||
                    !Sight(actor, target)) continue;
                ClubContacts++;
                var result = Wiring.ApplySummonHit(target, snapshot.Revision, actor.ClubPower, plan.Origin, actor.Strike, actor.Cast.Letter);
                if (result.AppliedDamage > 0) { ConfirmedHits++; ConfirmedDamage += result.AppliedDamage; }
                if (active != actor || actor.Root == null) return;
            }
        }
        void CancelClubAttack(Actor actor)
        {
            if (actor.ClubPlan == null || actor.ClubPlan.IsCancelled) return;
            actor.ClubPlan.Cancel(); actor.Clock.CancelAttack(); actor.HasPath = false;
            actor.RecoveryUntil = Mathf.Max(actor.Clock.Elapsed + actor.Profile.ClubRecoverySeconds, actor.RecoveryUntil);
            ClubAttackCancellations++;
        }
        static Vector3 ClubAimPoint(EnemyVitals target)
        {
            // Encounter roots already sit at body centre, not at the feet.
            // A second height offset would put short melee swings above their targets.
            if (target.TryGetComponent<Collider>(out var body) && body.enabled && !body.isTrigger)
                return body.bounds.center;
            return target.transform.position;
        }
    }
}
