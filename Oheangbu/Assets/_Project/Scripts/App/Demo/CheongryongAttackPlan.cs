using System;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    public enum CheongryongAttackKind { HeadBite, TailSweep, RootEruption, WoodProjectile }
    public enum CheongryongCombatState { Idle, Windup, Active, Recovery, Growth, Stunned, Dead }

    public readonly struct CheongryongAttackImpact
    {
        public readonly CheongryongAttackPlan Plan;
        public readonly Vector3 Point;
        public readonly ParryOutcome Outcome;
        public readonly float AppliedDamage;
        public readonly bool Contact;
        public CheongryongAttackImpact(CheongryongAttackPlan plan, Vector3 point, ParryOutcome outcome, float appliedDamage, bool contact)
        { Plan = plan; Point = point; Outcome = outcome; AppliedDamage = appliedDamage; Contact = contact; }
    }

    // The public surface is read-only. The combat owner alone advances/resolves/cancels the plan.
    // Geometry and damage are copied at windup: moving the player, rig or profile cannot retarget an attack.
    public sealed class CheongryongAttackPlan
    {
        public CheongryongAttackKind Kind { get; }
        public AttackProvenance Attack { get; }
        public Vector3 Origin { get; }
        public Vector3 Direction { get; }
        public Vector3 TargetPoint { get; }
        public float Range { get; }
        public float Radius { get; }
        public float HalfAngleDegrees { get; }
        public float VerticalTolerance { get; }
        public float Damage { get; }
        public float Speed { get; }
        public float StartedAt { get; }
        public float ReleaseAt { get; }
        public float ActiveEndAt { get; }
        public float EndAt { get; }
        public float SampleTime { get; private set; }
        public float PreviousSampleTime { get; private set; }
        public float ClearDistance { get; private set; }
        public bool ContactConsumed { get; private set; }
        public bool IsCancelled { get; private set; }
        public bool IsElemental => Attack.Element.HasValue;
        public bool IsActive => !IsCancelled && SampleTime >= ReleaseAt && SampleTime <= ActiveEndAt;
        public float Windup01 => Mathf.Clamp01((SampleTime - StartedAt) / (ReleaseAt - StartedAt));
        public float Active01 => Mathf.Clamp01((SampleTime - ReleaseAt) / (ActiveEndAt - ReleaseAt));
        public float ProjectileDistance => Mathf.Min(ClearDistance, Mathf.Max(0f, SampleTime - ReleaseAt) * Speed);
        public Vector3 ProjectilePosition => Origin + Direction * ProjectileDistance;

        // #306 read-only telegraph clock (SPEC-PLAYTEST-306 #12): when this attack would meet a body at `bodyPoint` (the
        // player centre + .8 m the owner samples). Same scaled clock as the plan; recomputed per call, never fed back.
        // Bolt: release + (along - radius) / speed, bounded by the clipped travel. Every other kind: release.
        public float PredictImpactTime(Vector3 bodyPoint)
        {
            if (Kind != CheongryongAttackKind.WoodProjectile || !Finite(bodyPoint)) return ReleaseAt;
            float along = Mathf.Clamp(Vector3.Dot(bodyPoint - Origin, Direction) - Radius, 0f, ClearDistance);
            return Mathf.Min(ActiveEndAt, ReleaseAt + along / Speed);
        }

        public CheongryongAttackPlan(CheongryongCombatProfile profile, CheongryongAttackKind kind, AttackProvenance attack,
            Vector3 origin, Vector3 direction, Vector3 targetPoint, float startedAt)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!profile.TryValidate(out var error)) throw new ArgumentException(error, nameof(profile));
            if (!Finite(origin) || !Finite(direction) || direction.sqrMagnitude < .0001f || !float.IsFinite(direction.sqrMagnitude) ||
                !Finite(targetPoint) || !float.IsFinite(startedAt) || startedAt < 0f ||
                kind < CheongryongAttackKind.HeadBite || kind > CheongryongAttackKind.WoodProjectile ||
                attack.AttackId <= 0 || attack.Instigator == null || attack.Source != DamageSource.Enemy ||
                attack.Element != (kind >= CheongryongAttackKind.RootEruption ? Element.Wood : (Element?)null))
                throw new ArgumentException("Attack needs finite locked geometry, time and valid enemy-owned provenance.");
            Kind = kind; Attack = attack; Origin = origin; Direction = direction.normalized; TargetPoint = targetPoint;
            StartedAt = SampleTime = PreviousSampleTime = startedAt; VerticalTolerance = profile.VerticalTolerance;
            float windup, active, recovery;
            switch (kind)
            {
                case CheongryongAttackKind.HeadBite:
                    Range = profile.BiteRange; HalfAngleDegrees = profile.BiteHalfAngle; Damage = profile.BiteDamage;
                    windup = profile.BiteWindup; active = profile.BiteActiveSeconds; recovery = profile.BiteRecovery; break;
                case CheongryongAttackKind.TailSweep:
                    Range = profile.TailRange; HalfAngleDegrees = profile.TailHalfAngle; Damage = profile.TailDamage;
                    windup = profile.TailWindup; active = profile.TailActiveSeconds; recovery = profile.TailRecovery; break;
                case CheongryongAttackKind.RootEruption:
                    Range = profile.EngageRange; Radius = profile.RootRadius; Damage = profile.RootDamage;
                    windup = profile.RootWindup; active = profile.RootActiveSeconds; recovery = profile.RootRecovery; break;
                default:
                    Range = profile.ProjectileRange; Radius = profile.ProjectileRadius; Speed = profile.ProjectileSpeed; Damage = profile.ProjectileDamage;
                    windup = profile.ProjectileWindup; active = Range / Speed; recovery = profile.ProjectileRecovery; break;
            }
            ClearDistance = Range; ReleaseAt = StartedAt + windup; ActiveEndAt = ReleaseAt + active; EndAt = ActiveEndAt + recovery;
            if (!float.IsFinite(EndAt) || ReleaseAt <= StartedAt || ActiveEndAt <= ReleaseAt)
                throw new ArgumentException("Attack timeline overflow.");
        }

        // Non-projectile shapes use feet and a vertical limit. Projectile uses a body-center point.
        public bool Contains(Vector3 point)
        {
            if (!Finite(point)) return false;
            Vector3 delta = point - (Kind == CheongryongAttackKind.RootEruption ? TargetPoint : Origin);
            if (Kind == CheongryongAttackKind.WoodProjectile)
            {
                float along = Vector3.Dot(delta, Direction);
                return along >= 0f && along <= ClearDistance && (delta - Direction * along).sqrMagnitude <= Radius * Radius;
            }
            if (Mathf.Abs(delta.y) > VerticalTolerance) return false;
            delta.y = 0f;
            if (Kind == CheongryongAttackKind.RootEruption) return delta.sqrMagnitude <= Radius * Radius;
            return delta.sqrMagnitude <= Range * Range &&
                (delta.sqrMagnitude < .0001f || Vector3.Angle(Vector3.ProjectOnPlane(Direction, Vector3.up), delta) <= HalfAngleDegrees);
        }

        // A projectile checks the swept interval against the player's CURRENT position, never the old aim point.
        public bool SweptProjectileContains(Vector3 point)
        {
            if (Kind != CheongryongAttackKind.WoodProjectile || !Contains(point) || IsCancelled || ContactConsumed || SampleTime > EndAt) return false;
            float along = Vector3.Dot(point - Origin, Direction);
            float previous = Mathf.Min(ClearDistance, Mathf.Max(0f, PreviousSampleTime - ReleaseAt) * Speed);
            return SampleTime >= ReleaseAt && along + Radius >= previous && along - Radius <= ProjectileDistance;
        }

        internal void AdvanceTo(float now)
        {
            if (!float.IsFinite(now) || now < SampleTime) throw new ArgumentOutOfRangeException(nameof(now));
            PreviousSampleTime = SampleTime; SampleTime = now;
        }
        internal void ClipTo(float distance) { ClearDistance = Mathf.Min(ClearDistance, Mathf.Max(0f, distance)); }
        internal void ConsumeContact() { ContactConsumed = true; }
        internal void Cancel() { IsCancelled = true; }
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
