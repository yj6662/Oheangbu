using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    public enum SouthGateAttackKind { Thrust, Sweep, EarthShockwave, NeutralEarthCombo }
    public enum SouthGateCombatState { Idle, Windup, Active, EarthPreparation, Recovery, CounterOpportunity, Stunned, Dead }
    public enum SouthGatePulseShape { Thrust, Sweep, Wave }
    public readonly struct SouthGateGeneralImpact
    {
        public readonly SouthGateGeneralAttackPlan Plan;
        public readonly SouthGateAttackPulse Pulse;
        public readonly Vector3 Point;
        public readonly ParryOutcome Outcome;
        public readonly float AppliedDamage;
        public readonly bool Contact;
        public SouthGateGeneralImpact(SouthGateGeneralAttackPlan plan, SouthGateAttackPulse pulse, Vector3 point, ParryOutcome outcome, float damage, bool contact)
        { Plan = plan; Pulse = pulse; Point = point; Outcome = outcome; AppliedDamage = damage; Contact = contact; }
    }
    public sealed class SouthGateAttackPulse
    {
        public SouthGatePulseShape Shape { get; }
        public AttackProvenance Attack { get; }
        public float ReleaseAt { get; }
        public float ActiveEndAt { get; }
        public float Damage { get; }
        public float Range { get; }
        public float Width { get; }
        public float HalfAngle { get; }
        public float Speed { get; }
        public bool Consumed { get; private set; }
        public bool Cancelled { get; private set; }
        internal SouthGateAttackPulse(SouthGatePulseShape shape, AttackProvenance attack, float release, float active, float damage, float range, float width, float halfAngle, float speed)
        { Shape = shape; Attack = attack; ReleaseAt = release; ActiveEndAt = release + active; Damage = damage; Range = range; Width = width; HalfAngle = halfAngle; Speed = speed; }
        internal void Consume() { Consumed = true; }
        internal void Cancel() { Cancelled = true; }
    }
    // Immutable windup geometry and per-hit ownership. Mutable sampling/consumption belongs only to the combat owner.
    public sealed class SouthGateGeneralAttackPlan
    {
        public SouthGateAttackKind Kind { get; }
        public Vector3 Origin { get; }
        public Vector3 Direction { get; }
        public float StartedAt { get; }
        public float EndAt { get; }
        public float EmpowerStartAt { get; }
        public float EmpowerEndAt { get; }
        public float VerticalTolerance { get; }
        public float CounterSeconds { get; }
        public float SampleTime { get; private set; }
        public float PreviousSampleTime { get; private set; }
        public bool IsCancelled { get; private set; }
        public bool EmpowerInterrupted { get; private set; }
        public IReadOnlyList<SouthGateAttackPulse> Pulses { get; }
        public bool InEarthPreparation(float now) => !IsCancelled && !EmpowerInterrupted && Kind == SouthGateAttackKind.NeutralEarthCombo && now >= EmpowerStartAt && now < EmpowerEndAt;
        public SouthGateGeneralAttackPlan(SouthGateGeneralProfile p, SouthGateAttackKind kind, EnemyVitals owner, Vector3 origin, Vector3 direction, float now)
        {
            if (p == null || !p.TryValidate(out _)) throw new ArgumentException("Valid SouthGate profile required.");
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (owner == null || !Finite(origin) || !Finite(direction) || !float.IsFinite(direction.sqrMagnitude) || direction.sqrMagnitude < .0001f ||
                !float.IsFinite(now) || now < 0 || kind < SouthGateAttackKind.Thrust || kind > SouthGateAttackKind.NeutralEarthCombo)
                throw new ArgumentException("Finite locked geometry, clock and an actual enemy owner required.");
            Kind = kind; Origin = origin; Direction = direction.normalized; StartedAt = SampleTime = PreviousSampleTime = now;
            VerticalTolerance = p.VerticalTolerance; CounterSeconds = p.CounterWindow;
            var hits = new List<SouthGateAttackPulse>();
            if (kind == SouthGateAttackKind.Thrust || kind == SouthGateAttackKind.NeutralEarthCombo)
                hits.Add(new SouthGateAttackPulse(SouthGatePulseShape.Thrust, AttackProvenance.Create(owner, DamageSource.Enemy, null),
                    now + p.ThrustWindup, p.ThrustActive, p.ThrustDamage, p.ThrustRange, p.ThrustRadius, 0, 0));
            else if (kind == SouthGateAttackKind.Sweep)
                hits.Add(new SouthGateAttackPulse(SouthGatePulseShape.Sweep, AttackProvenance.Create(owner, DamageSource.Enemy, null),
                    now + p.SweepWindup, p.SweepActive, p.SweepDamage, p.SweepRange, 0, p.SweepHalfAngle, 0));
            if (kind == SouthGateAttackKind.EarthShockwave || kind == SouthGateAttackKind.NeutralEarthCombo)
            {
                EmpowerStartAt = kind == SouthGateAttackKind.NeutralEarthCombo ? hits[0].ActiveEndAt : now;
                EmpowerEndAt = EmpowerStartAt + (kind == SouthGateAttackKind.NeutralEarthCombo ? p.ComboEmpowerSeconds : p.WaveWindup);
                hits.Add(new SouthGateAttackPulse(SouthGatePulseShape.Wave, AttackProvenance.Create(owner, DamageSource.Enemy, Element.Earth),
                    EmpowerEndAt, p.WaveRange / p.WaveSpeed, kind == SouthGateAttackKind.NeutralEarthCombo ? p.ComboEarthDamage : p.WaveDamage,
                    p.WaveRange, p.WaveWidth, p.WaveHalfAngle, p.WaveSpeed));
            }
            EndAt = hits[hits.Count - 1].ActiveEndAt + (kind == SouthGateAttackKind.Thrust ? p.ThrustRecovery : kind == SouthGateAttackKind.Sweep ? p.SweepRecovery : p.WaveRecovery);
            if (!float.IsFinite(EndAt) || hits.Exists(h => h.ReleaseAt <= now || h.ActiveEndAt <= h.ReleaseAt)) throw new ArgumentException("Attack timeline overflow.");
            Pulses = hits.AsReadOnly();
        }
        public bool Contains(SouthGateAttackPulse pulse, Vector3 point)
        {
            if (!Finite(point) || pulse == null || !Owns(pulse) || Mathf.Abs(point.y - Origin.y) > VerticalTolerance) return false;
            Vector3 delta = Vector3.ProjectOnPlane(point - Origin, Vector3.up); float along = Vector3.Dot(delta, Direction);
            if (pulse.Shape == SouthGatePulseShape.Thrust)
                return along >= 0 && along <= pulse.Range && (delta - Direction * along).sqrMagnitude <= pulse.Width * pulse.Width;
            return delta.sqrMagnitude <= pulse.Range * pulse.Range && (delta.sqrMagnitude < .0001f || Vector3.Angle(Direction, delta) <= pulse.HalfAngle);
        }
        public bool SweptContains(SouthGateAttackPulse pulse, Vector3 point)
        {
            if (IsCancelled || pulse == null || pulse.Cancelled || pulse.Consumed || !Contains(pulse, point) || SampleTime < pulse.ReleaseAt || SampleTime > pulse.ActiveEndAt) return false;
            Vector3 delta = Vector3.ProjectOnPlane(point - Origin, Vector3.up);
            if (pulse.Shape == SouthGatePulseShape.Thrust) return true;
            float previous = Mathf.Clamp01((PreviousSampleTime - pulse.ReleaseAt) / (pulse.ActiveEndAt - pulse.ReleaseAt));
            float current = Mathf.Clamp01((SampleTime - pulse.ReleaseAt) / (pulse.ActiveEndAt - pulse.ReleaseAt));
            if (pulse.Shape == SouthGatePulseShape.Sweep)
            {
                float angle = Vector3.SignedAngle(Direction, delta, Vector3.up);
                return angle >= Mathf.Lerp(-pulse.HalfAngle, pulse.HalfAngle, previous) - 6 && angle <= Mathf.Lerp(-pulse.HalfAngle, pulse.HalfAngle, current) + 6;
            }
            float distance = delta.magnitude;
            return distance + pulse.Width >= previous * pulse.Range && distance - pulse.Width <= current * pulse.Range;
        }
        internal void AdvanceTo(float now)
        { if (!float.IsFinite(now) || now < SampleTime) throw new ArgumentOutOfRangeException(nameof(now)); PreviousSampleTime = SampleTime; SampleTime = now; }
        internal bool InterruptEmpower(float now)
        {
            if (!InEarthPreparation(now)) return false;
            EmpowerInterrupted = true;
            foreach (var hit in Pulses) if (hit.Attack.Element == Element.Earth && !hit.Consumed) hit.Cancel();
            return true;
        }
        internal void Cancel() { IsCancelled = true; foreach (var hit in Pulses) if (!hit.Consumed) hit.Cancel(); }
        bool Owns(SouthGateAttackPulse p) { foreach (var value in Pulses) if (ReferenceEquals(value, p)) return true; return false; }
        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
