using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.Demo
{
    public sealed partial class DemoSummonCombatManager
    {
        sealed class WaterTarget
        {
            public EnemyVitals Target;
            public uint Revision;
            public float ContactAt, Along;
            public bool Resolved;
        }
        RaycastHit[] waterHits = new RaycastHit[32];
        void HandleWaterTarget(Actor actor, float dt)
        {
            var p = actor.Profile; var target = actor.Target.transform.position;
            float distance = HorizontalDistance(actor.Root.transform.position, target);
            if (distance < p.WaterMinimumDistance && !actor.KeepingDistance)
            { actor.KeepingDistance = true; actor.HasPath = false; actor.NextPath = actor.Clock.Elapsed; }
            if (actor.KeepingDistance && distance >= p.WaterPreferredDistance - .1f)
            { actor.KeepingDistance = false; actor.HasPath = false; }
            if (actor.KeepingDistance)
            {
                Vector3 away = Vector3.ProjectOnPlane(actor.Root.transform.position - target, Vector3.up);
                if (away.sqrMagnitude < .0001f) away = -actor.Root.transform.forward;
                Vector3 destination = target + away.normalized * p.WaterPreferredDistance; destination.y = actor.Root.transform.position.y;
                actor.Clock.SetActivityPhase(SummonPhase.Following);
                if (HorizontalDistance(destination, Wiring.SummonPlayer.position) <= p.LeashRange && Navigate(actor, destination, dt, .1f, target, true))
                { actor.MovingBackward = true; return; }
            }
            Vector3 origin = actor.Root.transform.TransformPoint(p.WaterOriginOffset);
            if (Vector3.Distance(origin, ClubAimPoint(actor.Target)) > p.WaterRange || !Sight(actor, actor.Target))
            {
                actor.KeepingDistance = false; actor.Clock.SetActivityPhase(SummonPhase.Approaching);
                Navigate(actor, target, dt, p.WaterPreferredDistance); return;
            }
            BeginWaterAttack(actor, dt);
        }
        void BeginWaterAttack(Actor actor, float dt)
        {
            var p = actor.Profile;
            float duration = p.WaterWindupSeconds + p.WaterStreamSeconds + p.WaterRecoverySeconds;
            if (!actor.Clock.CanAttack || actor.Clock.ActivityRemaining <= duration || !TryFace(actor, actor.Target.transform.position, dt * 240)) return;
            Vector3 origin = actor.Root.transform.TransformPoint(p.WaterOriginOffset), aim = ClubAimPoint(actor.Target);
            if (Vector3.Distance(origin, aim) < .01f) return;
            var plan = new SummonWaterAttackPlan(origin, aim - origin, p.WaterRange, p.WaterRadius, p.WaterTravelSpeed,
                actor.Clock.Elapsed, p.WaterWindupSeconds, p.WaterStreamSeconds, p.WaterRecoverySeconds);
            plan.ClipTo(WaterClearDistance(plan));
            if (!plan.Contains(aim)) return;
            var captured = new List<WaterTarget>(); var candidates = Wiring.SummonTargets;
            for (int i = 0; i < candidates.Count; i++)
            {
                var target = candidates[i];
                if (!Eligible(actor, target)) continue;
                Vector3 point = ClubAimPoint(target); if (!plan.Contains(point)) continue;
                captured.Add(new WaterTarget { Target = target, Revision = target.LifeRevision, ContactAt = plan.ContactAt(point), Along = plan.Longitudinal(point) });
            }
            captured.Sort((a, b) => a.ContactAt.CompareTo(b.ContactAt));
            var strike = AttackProvenance.Create(actor.Root, DamageSource.Summon, actor.Cast.Element);
            if (!actor.Clock.TryBeginAttack(strike.AttackId, actor.Target.GetInstanceID(), actor.Target.LifeRevision,
                p.WaterWindupSeconds, Mathf.Max(p.CooldownSeconds, p.WaterStreamSeconds + p.WaterRecoverySeconds))) return;
            actor.Strike = strike; actor.StrikeRevision = actor.Target.LifeRevision; actor.WaterReleaseTargetId = actor.Target.GetInstanceID();
            actor.WaterPlan = plan; actor.WaterTargets = captured.ToArray(); actor.WaterReleased = false; actor.AttackIsRoot = false;
            actor.AttackStarted = plan.StartedAt; actor.AttackDuration = duration; actor.RecoveryUntil = plan.EndAt;
            actor.HasPath = false; AttackStarts++; WaterAttackStarts++;
        }
        void TickWaterAttack(Actor actor)
        {
            var plan = actor.WaterPlan; plan.AdvanceTo(actor.Clock.Elapsed);
            if (plan.IsCancelled) return;
            var p = actor.Profile;
            if (HorizontalDistance(actor.Root.transform.position, Wiring.SummonPlayer.position) > p.LeashRange ||
                !FootSupport(p, actor.Root.transform.position, actor.Root.transform.rotation) ||
                !VolumeClear(p, actor.Root.transform.position, actor.Root.transform.rotation, true))
            { CancelWaterAttack(actor); return; }
            plan.ClipTo(WaterClearDistance(plan));
            if (plan.ClearDistance <= .001f) { CancelWaterAttack(actor); return; }
            if (!plan.IsReleased) return;
            if (!actor.WaterReleased)
            {
                if (!actor.Clock.TryConsumeHit(actor.Strike.AttackId, actor.WaterReleaseTargetId, actor.StrikeRevision, true, true, true))
                {
                    if (actor.Clock.PendingAttackId == actor.Strike.AttackId) return;
                    CancelWaterAttack(actor); return;
                }
                actor.WaterReleased = true; ConsumedStrikes++;
            }
            foreach (var snapshot in actor.WaterTargets)
            {
                if (snapshot.Resolved || actor.Clock.Elapsed < snapshot.ContactAt) continue;
                snapshot.Resolved = true;
                var target = snapshot.Target;
                if (!plan.IsStreaming || target == null || !target.isActiveAndEnabled || !target.IsAlive || target.LifeRevision != snapshot.Revision ||
                    !Registered(target) || !Eligible(actor, target) || snapshot.Along > plan.FrontDistance + .001f) continue;
                Vector3 point = ClubAimPoint(target);
                // Contacts belong to the committed leading edge. A dodge cannot reschedule its hit further down the stream.
                if (!plan.Contains(point) || Mathf.Abs(plan.Longitudinal(point) - snapshot.Along) > plan.Radius ||
                    plan.Longitudinal(point) > plan.FrontDistance + .001f) continue;
                WaterContacts++;
                var result = Wiring.ApplySummonHit(target, snapshot.Revision, actor.WaterPower, plan.Origin, actor.Strike, actor.Cast.Letter);
                if (result.AppliedDamage > 0) { ConfirmedHits++; ConfirmedDamage += result.AppliedDamage; }
                if (active != actor || actor.Root == null) return;
            }
        }
        float WaterClearDistance(SummonWaterAttackPlan plan)
        {
            var physics = gameObject.scene.GetPhysicsScene(); if (!physics.IsValid()) return 0;
            int overlapsCount = physics.OverlapSphere(plan.Origin, plan.Radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (overlapsCount == overlaps.Length) return 0;
            for (int i = 0; i < overlapsCount; i++) if (!IgnoreCharacter(overlaps[i])) return 0;
            int count;
            while (true)
            {
                count = physics.SphereCast(plan.Origin, plan.Radius, plan.Direction, waterHits, plan.ClearDistance, ~0, QueryTriggerInteraction.Ignore);
                if (count < waterHits.Length) break;
                // Bounded diagnostics: an overloaded physics corridor blocks rather than dropping a possible wall.
                if (waterHits.Length >= 4096) return 0;
                Array.Resize(ref waterHits, waterHits.Length * 2);
            }
            float distance = plan.ClearDistance;
            for (int i = 0; i < count; i++) if (!IgnoreCharacter(waterHits[i].collider)) distance = Mathf.Min(distance, Mathf.Max(0, waterHits[i].distance - .01f));
            return distance;
        }
        void CancelWaterAttack(Actor actor)
        {
            if (actor.WaterPlan == null || actor.WaterPlan.IsCancelled) return;
            actor.WaterPlan.Cancel(); actor.Clock.CancelAttack(); actor.HasPath = false;
            actor.RecoveryUntil = Mathf.Max(actor.Clock.Elapsed + actor.Profile.WaterRecoverySeconds, actor.RecoveryUntil);
            WaterAttackCancellations++;
        }
    }
}
