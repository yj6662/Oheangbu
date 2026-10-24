using System;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;

namespace Oheangbu.App
{
    // S5-b slice 2: the real implementations of the ports slice 3's cast coordinator calls (Vol2Ports.cs - slice 3 owns that file and its TEMPORARY
    // adapters). They live here, next to the kit that owns the rules, and are bound by Vol2DefenceInstaller:
    //     coordinator.Damage = kit.DamageSink;     coordinator.Ink = kit.PaidInk;
    // Until then the coordinator's temporary adapters stand in, and the kit's gate must not also pay (Vol2DefenceKit.PaysOrdinaryCasts).

    /// <summary>The vol.2 damage entry for the coordinator's requests. The request carries ACTION UNITS (Units = H x min(R x S, cap) x the
    /// target's status factor, before the window B and the counter K). This sink adds what only the hit frame knows - B from the target's weak-point
    /// window NOW, K (the body overcomes the target's attack body, inside the window, for a spell) - converts units to HP with the one explicit
    /// HpPerUnit, and hands the result to EnemyVitals.TakeResolvedVol2Damage, which applies no old multiplier and the target's own armour once.
    /// Every term of the last hit is kept (LastTerms) for the log.</summary>
    public sealed class Vol2DefenceDamageSink : IVol2DamageSink
    {
        readonly Vol2DefenceKit _kit;
        public Vol2DamageTerms LastTerms { get; private set; }
        public Vol2HitRecord LastHit { get; private set; }
        public int Hits { get; private set; }
        public Vol2DefenceDamageSink(Vol2DefenceKit kit) { _kit = kit ?? throw new ArgumentNullException(nameof(kit)); }

        public Vol2DamageResult Apply(in Vol2DamageRequest r)
        {
            var enemy = r.Target as Vol2EnemyTarget;
            if (enemy == null || enemy.Vitals == null) return Vol2DamageResult.Refused("NotAnEnemy");
            var v = enemy.Vitals;
            if (!v.isActiveAndEnabled || !v.IsAlive) return Vol2DamageResult.Refused("Dead");
            if (v.LifeRevision != r.TargetLifeRevision) return Vol2DamageResult.Refused("LifeRevisionChanged");
            if (!(r.Units > 0m)) return Vol2DamageResult.Refused("NoDamage");
            bool window = v.WeakPointActive;                               // B: the target's state at THIS frame
            bool spell = !r.Lasting;
            bool counter = spell && CounterOf(v, r.Element);
            Vol2DamageTerms terms = spell
                ? Vol2DamageResolver.Resolve(_kit.Numbers, r.BaseH * r.StatusFactor, r.Structure, r.Force, counter, true, window, _kit.HpPerUnit)
                : Vol2DamageResolver.Resolve(_kit.Numbers, r.Units, 1m, 1m, false, false, window, _kit.HpPerUnit);   // a lasting tick: B only
            var source = spell ? DamageSource.PlayerDirect : DamageSource.PersistentSpell;
            bool melee = !r.IsReaction && r.Glyph.Frame == Frame.Attachment;       // S5-b slice 8: a reaction is nobody's swing, whatever the acting cast was
            var attack = Vol2Attack.Create(r.Instigator, source, r.Element, melee);
            var result = v.TakeResolvedVol2Damage(terms.RawHp, attack, terms, false);
            LastTerms = terms; LastHit = v.LastVol2Hit; Hits++;
            return new Vol2DamageResult(result.AppliedDamage > 0f, result.AppliedDamage, result.Killed, result.AppliedDamage > 0f ? "" : "Absorbed");
        }

        static bool CounterOf(EnemyVitals v, Oheangbu.Core.Domain.Element element)
        {
            if (!v.TryAttackElement(out var enemyElement)) return false;
            return ReactionRulesV3.Beats(Vol2Mapping.ToBody(element), Vol2Mapping.ToBody(enemyElement));
        }
    }

    /// <summary>The coordinator's ink port when the kit's payment gate already took the ink of the cast (before CastAccepted): it answers "paid" with
    /// the amount the gate recorded for that very CastId. A cast the gate did not pay (a path that bypassed it) is paid here from the pool - never
    /// free, never twice.</summary>
    public sealed class Vol2PaidInkSpender : IVol2InkSpender
    {
        readonly Vol2DefenceKit _kit;
        public int AlreadyPaid { get; private set; }
        public int PaidHere { get; private set; }
        public Vol2PaidInkSpender(Vol2DefenceKit kit) { _kit = kit ?? throw new ArgumentNullException(nameof(kit)); }
        public bool TrySpend(CastId cast, decimal cost, out decimal paid)
        {
            if (_kit.TryGetPaid(cast, out paid)) { AlreadyPaid++; return true; }
            paid = 0m;
            if (cost <= 0m) return true;
            if (!_kit.Ink.TrySpend(cost)) return false;
            paid = cost; PaidHere++;
            return true;
        }
    }
}
