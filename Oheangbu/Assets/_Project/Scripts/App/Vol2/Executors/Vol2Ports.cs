using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using UnityEngine.AI;

namespace Oheangbu.App
{
    // The ports of the vol.2 cast coordinator. The coordinator decides WHAT happens (which body is touched, how much action, which status);
    // the ports do it to the game. Slice 2 provides the real damage entry (TakeResolvedVol2Damage), the ink and the defence; until the
    // main agent switches the binding, the TEMPORARY adapters at the end of this file stand in. Nothing here is a singleton.

    /// <summary>What the coordinator asks the damage entry to apply. All amounts are in ACTION UNITS (the unit of body.*.instant), never HP:
    /// the data has no HP per action unit (design section 8: do not read 1.0 as HP 1). The entry converts, and applies the
    /// window state (B) and the counter-element factor (K) at the moment of the hit (D308-147), not the coordinator.</summary>
    /// <summary>S5-b slice 8: where a damage request comes from (the "request kind" slice 7 asked for). Spell = a hit of a cast; Lasting = a tick of a lasting damage;
    /// Borrowed = a reflected shot's lent power (slice 7); Reaction = a product of the reaction rules - it gives NO groggy, NO posture and cannot trigger a seed (the
    /// reaction engine never announces it as a contact of a cast, so the seed hub does not hear it).</summary>
    public enum Vol2DamageOrigin { Spell, Lasting, Borrowed, Reaction }

    public readonly struct Vol2DamageRequest
    {
        public readonly Vol2DamageOrigin Origin;
        public bool IsReaction => Origin == Vol2DamageOrigin.Reaction;
        /// <summary>The cast that owns the damage (for a reaction: the ACTING material's cast); the reaction depth and the original owner are in the attack's Vol2AttackMetadata.</summary>
        public readonly CastId Cast;
        public readonly AttackId Attack;
        public readonly long SwingId;
        public readonly IVol2Target Target;
        public readonly long TargetLifeRevision;
        public readonly Glyph Glyph;
        public readonly Element Element;
        /// <summary>H of D = H x C(R,S) x K x B ... : body.X.instant.</summary>
        public readonly decimal BaseH;
        /// <summary>R: the structure (the effect term's amount, 1 for 한 번).</summary>
        public readonly decimal Structure;
        /// <summary>S: the force. combat.force.min (1) until the stroke speed is measured (open item).</summary>
        public readonly decimal Force;
        /// <summary>BaseH x min(R x S, cap), before the window (B), the counter (K), equipment and ink power, and times <see cref="StatusFactor"/>.</summary>
        public readonly decimal Units;
        /// <summary>The factor a status of the target put on the hit (a wet target: fire x0.5, metal x1.25). Already inside Units.</summary>
        public readonly decimal StatusFactor;
        /// <summary>true = one tick of a lasting damage (the burn), not a hit.</summary>
        public readonly bool Lasting;
        public readonly long NowUs;
        public readonly Vector3 Point;
        /// <summary>The attacker object (the caster); may be null.</summary>
        public readonly UnityEngine.Object Instigator;
        public Vol2DamageRequest(CastId cast, AttackId attack, long swing, IVol2Target target, Glyph glyph, Element element, decimal h, decimal r, decimal s, decimal units, decimal statusFactor, bool lasting, long nowUs, Vector3 point, UnityEngine.Object instigator,
            Vol2DamageOrigin? origin = null)
        {
            Origin = origin ?? (lasting ? Vol2DamageOrigin.Lasting : Vol2DamageOrigin.Spell);
            Cast = cast; Attack = attack; SwingId = swing; Target = target; TargetLifeRevision = target != null ? target.LifeRevision : 0; Glyph = glyph; Element = element;
            BaseH = h; Structure = r; Force = s; Units = units; StatusFactor = statusFactor; Lasting = lasting; NowUs = nowUs; Point = point; Instigator = instigator;
        }
    }

    public readonly struct Vol2DamageResult
    {
        public readonly bool Applied;
        public readonly float Hp;
        public readonly bool Killed;
        public readonly string Reason;
        public Vol2DamageResult(bool applied, float hp, bool killed, string reason) { Applied = applied; Hp = hp; Killed = killed; Reason = reason ?? ""; }
        public static Vol2DamageResult Refused(string reason) => new Vol2DamageResult(false, 0f, false, reason);
    }

    public interface IVol2DamageSink { Vol2DamageResult Apply(in Vol2DamageRequest request); }

    /// <summary>The ink of a cast. <c>TrySpend</c> false = the cast is refused (no shape, no effect). Parry glyphs have cost 0 here: their
    /// post-payment (0 on success, 14 on failure) belongs to slice 2.</summary>
    public interface IVol2InkSpender { bool TrySpend(CastId cast, decimal cost, out decimal paid); }

    public readonly struct Vol2BodyEffectRequest
    {
        public readonly Vol2BodyActionKind Kind;
        public readonly CastId Cast;
        public readonly AttackId Attack;
        public readonly IVol2Target Target;
        public readonly long NowUs, DurationUs;
        /// <summary>Bind: the movement multiplier. Others: 1.</summary>
        public readonly float Magnitude;
        /// <summary>Push: the flat unit direction away from the caster and the distance.</summary>
        public readonly Vector3 Direction;
        public readonly float Meters;
        public Vol2BodyEffectRequest(Vol2BodyActionKind kind, CastId cast, AttackId attack, IVol2Target target, long nowUs, long durationUs, float magnitude, Vector3 direction, float meters)
        { Kind = kind; Cast = cast; Attack = attack; Target = target; NowUs = nowUs; DurationUs = durationUs; Magnitude = magnitude; Direction = direction; Meters = meters; }
    }

    /// <summary>What the body does to the target's behaviour (slow, push). Burn, wet and cut are bookkeeping the coordinator owns (a damage tick,
    /// a mark, a factor); the sink is told about them too and answers "recorded".</summary>
    public interface IVol2StatusSink
    {
        string Apply(in Vol2BodyEffectRequest request);
        void Tick(float deltaSeconds);
    }

    /// <summary>The defence judge of slice 2 asks this: is a ㅓ 한 번 attachment window open, whose body, until when.</summary>
    public readonly struct Vol2ParryWindow
    {
        public readonly CastId Cast;
        public readonly Element Element;
        public readonly long StartUs, EndUs;
        public Vol2ParryWindow(CastId cast, Element element, long startUs, long endUs) { Cast = cast; Element = element; StartUs = startUs; EndUs = endUs; }
        public bool Open(long nowUs) => Vol2Time.Contains(StartUs, EndUs, nowUs);
    }

    public interface IVol2ParryPort { void WindowOpened(in Vol2ParryWindow window); }

    // ------------------------------------------------------------------------------------------------------------------------------
    // TEMPORARY adapters. Marked so the main agent can find and replace them: every class below says so in its summary.
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>TEMPORARY (slice 3): records the ink of a cast and refuses nothing. Used when the scope has no InkPool (an edit-mode fixture).</summary>
    public sealed class Vol2RecordingInkSpender : IVol2InkSpender
    {
        public decimal Total { get; private set; }
        public int Casts { get; private set; }
        public bool Refuse { get; set; }
        public bool TrySpend(CastId cast, decimal cost, out decimal paid)
        {
            if (Refuse) { paid = 0m; return false; }
            Casts++; Total += cost; paid = cost; return true;
        }
    }

    /// <summary>TEMPORARY (slice 3): spends from the combat InkPool. The pool counts in fractions of the base capacity, so the cost (in ink,
    /// of ink.capacity) is divided by it. Slice 2 owns the real spending rules (post-payment of the parry, the refusal feedback): bind
    /// THIS adapter only while the wiring's Vol2 route does not spend by itself, or the cast is charged twice.</summary>
    public sealed class Vol2InkPoolSpender : IVol2InkSpender
    {
        readonly InkPool _pool;
        readonly decimal _capacity;
        public Vol2InkPoolSpender(InkPool pool, decimal inkCapacity) { _pool = pool; _capacity = inkCapacity > 0m ? inkCapacity : 100m; }
        public bool TrySpend(CastId cast, decimal cost, out decimal paid)
        {
            paid = 0m;
            if (_pool == null) return false;
            if (cost <= 0m) return true;
            if (!_pool.TrySpend((float)(cost / _capacity))) return false;
            paid = cost; return true;
        }
        /// <summary>The lab refills between rows; the game never calls this.</summary>
        public void Refill() { _pool?.Restore(1f); }
        /// <summary>What the pool holds, in ink (of ink.capacity), measured: for the lab's InkDelta. -1 without a pool.</summary>
        public float InkNow => _pool != null ? _pool.Value * _pool.TotalCapacity * (float)_capacity : -1f;
    }

    /// <summary>TEMPORARY (slice 3): applies a vol.2 hit through the OLD EnemyVitals.TakeDamage until slice 2's TakeResolvedVol2Damage exists.
    /// What that means (the first open item of the slice): the HP per action unit is UNDEFINED in the data (<see cref="HpPerActionUnit"/> is
    /// a stand-in: 45 is the D308-147 앞잡 hit and is NOT confirmed as this unit). TakeDamage then multiplies by the enemy's own
    /// DamageMultiplier (the window, B x1.5) and Modifiers.Intake (defence, brands); with Source Unknown and no element it does NOT
    /// apply the counter-element K, the guard of a PlayerDirect spell, the five-element completion or any groggy.</summary>
    public sealed class Vol2TempEnemyDamageSink : IVol2DamageSink
    {
        public const float UnconfirmedDefaultHpPerActionUnit = 45f;
        public float HpPerActionUnit { get; }
        public Vol2TempEnemyDamageSink(float hpPerActionUnit = UnconfirmedDefaultHpPerActionUnit) { HpPerActionUnit = hpPerActionUnit; }
        public Vol2DamageResult Apply(in Vol2DamageRequest r)
        {
            var enemy = r.Target as Vol2EnemyTarget;
            if (enemy == null || enemy.Vitals == null) return Vol2DamageResult.Refused("NotAnEnemy");
            var v = enemy.Vitals;
            if (!v.isActiveAndEnabled || !v.IsAlive) return Vol2DamageResult.Refused("Dead");
            if (v.LifeRevision != r.TargetLifeRevision) return Vol2DamageResult.Refused("LifeRevisionChanged");
            float amount = (float)(r.Units * (decimal)HpPerActionUnit);
            if (!(amount > 0f)) return Vol2DamageResult.Refused("NoDamage");
            var attack = AttackProvenance.Create(r.Instigator, DamageSource.Unknown, null);
            var result = v.TakeDamage(amount, attack);
            return new Vol2DamageResult(result.AppliedDamage > 0f, result.AppliedDamage, result.Killed, result.AppliedDamage > 0f ? "" : "Absorbed");
        }
    }

    /// <summary>TEMPORARY (slice 3): slows through EnemyVitals.Control (the existing timed control) and pushes by moving the body a little each
    /// tick (CharacterController, else NavMeshAgent, else the transform). The push distance is a temporary exception (TE-S3-4).</summary>
    public sealed class Vol2TempStatusSink : IVol2StatusSink
    {
        const long OwnerBase = 0x5600000000L;     // a vol.2 owner key, apart from the old effects' keys
        sealed class Push { public Vol2EnemyTarget Target; public Vector3 Direction; public float Remaining, Speed; }
        readonly List<Push> _pushes = new List<Push>();
        public int PushesStarted { get; private set; }

        public string Apply(in Vol2BodyEffectRequest r)
        {
            var enemy = r.Target as Vol2EnemyTarget;
            if (enemy == null || enemy.Vitals == null) return "NotAnEnemy";
            switch (r.Kind)
            {
                case Vol2BodyActionKind.Bind:
                    enemy.Vitals.Control.Apply(OwnerBase + r.Cast.Value, Time.time + Vol2Time.ToSecondsF(r.DurationUs), Mathf.Clamp01(r.Magnitude), false);
                    return "bind x" + r.Magnitude.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " " + Vol2Time.ToSecondsF(r.DurationUs).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "s";
                case Vol2BodyActionKind.Push:
                    float seconds = Mathf.Max(0.05f, Vol2Time.ToSecondsF(r.DurationUs));
                    _pushes.Add(new Push { Target = enemy, Direction = r.Direction, Remaining = r.Meters, Speed = r.Meters / seconds });
                    PushesStarted++;
                    return "push " + r.Meters.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "m";
                default:
                    return "recorded " + r.Kind;
            }
        }

        public void Tick(float dt)
        {
            for (int i = _pushes.Count - 1; i >= 0; i--)
            {
                var p = _pushes[i];
                var v = p.Target.Vitals;
                if (v == null || !v.IsAlive || p.Remaining <= 0f) { _pushes.RemoveAt(i); continue; }
                float step = Mathf.Min(p.Remaining, p.Speed * dt);
                p.Remaining -= step;
                Vector3 delta = p.Direction * step;
                var cc = v.GetComponentInChildren<CharacterController>();
                if (cc != null && cc.enabled) cc.Move(delta);
                else
                {
                    var agent = v.GetComponent<NavMeshAgent>();
                    if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(delta);
                    else v.transform.position += delta;
                }
                if (p.Remaining <= 1e-4f) _pushes.RemoveAt(i);
            }
        }
    }
}
