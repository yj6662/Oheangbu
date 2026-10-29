using System;
using System.Globalization;
using System.Threading;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.Combat
{
    // S5-b slice 2, design section 5. The vol.2 damage D308-147:  D = H x min(R x S, 1.5) x K x B x (1 + M) x (1 + min(A, 10) / 100).
    // The rules compute it (DefenceNumbersV3Dto.Damage); this file only splits it into terms for the log and converts it to HP.
    // B is judged at the damage frame (the weak-point window of the target at that moment), never at cast time.

    /// <summary>Every term of one vol.2 damage, as computed. D = H x C x K x B x (1+M) x (1+min(A,cap)/100), C = min(R x S, cap) for a
    /// spell, 1 otherwise. Plain numbers for the CSV / log; nothing here feeds back into a rule.</summary>
    public readonly struct Vol2DamageTerms
    {
        public readonly decimal H, R, S, C, K, B, M, A, D;
        public readonly bool Spell, Counter, Window;
        public readonly float HpPerUnit;
        /// <summary>D in real HP before the target's armour / strip / brand (D x HpPerUnit).</summary>
        public float RawHp => (float)D * HpPerUnit;
        public Vol2DamageTerms(decimal h, decimal r, decimal s, decimal c, decimal k, decimal b, decimal m, decimal a, decimal d,
            bool spell, bool counter, bool window, float hpPerUnit)
        { H = h; R = r; S = s; C = c; K = k; B = b; M = m; A = a; D = d; Spell = spell; Counter = counter; Window = window; HpPerUnit = hpPerUnit; }
        /// <summary>The product of the terms, for a check against D (the rules compute D in one expression).</summary>
        public decimal Product => H * C * K * B * (1m + M) * (1m + A / 100m);
        public override string ToString()
        {
            var inv = CultureInfo.InvariantCulture;
            return "H=" + H.ToString(inv) + " R=" + R.ToString(inv) + " S=" + S.ToString(inv) + " C=" + C.ToString(inv) + " K=" + K.ToString(inv) +
                   " B=" + B.ToString(inv) + " M=" + M.ToString(inv) + " A=" + A.ToString(inv) + " D=" + D.ToString(inv) +
                   " window=" + (Window ? "Y" : "N") + " spell=" + (Spell ? "Y" : "N") + " counter=" + (Counter ? "Y" : "N") + " hpPerUnit=" + HpPerUnit.ToString(inv);
        }
    }

    public static class Vol2DamageResolver
    {
        /// <param name="h">base damage of the glyph (작용량)</param>
        /// <param name="structure">R: the structure share (1 when the glyph has none)</param>
        /// <param name="force">S: the stroke force, 1 .. combat.force.max</param>
        /// <param name="counter">the body overcomes the target's attack body (상극)</param>
        /// <param name="spell">a spell (cast) rather than the bare swing: only a spell has C and K</param>
        /// <param name="window">the target's weak-point window is open AT THIS FRAME</param>
        /// <param name="inkPower">M</param>
        /// <param name="equipment">A, percent; capped at DefenceNumbersV3Dto.EquipmentCap</param>
        public static Vol2DamageTerms Resolve(DefenceNumbersV3Dto numbers, decimal h, decimal structure, decimal force, bool counter, bool spell, bool window,
            float hpPerUnit, decimal inkPower = 0m, decimal equipment = 0m)
        {
            if (numbers == null) throw new ArgumentNullException(nameof(numbers));
            if (hpPerUnit <= 0f || float.IsNaN(hpPerUnit) || float.IsInfinity(hpPerUnit)) throw new ArgumentOutOfRangeException(nameof(hpPerUnit));
            decimal d = numbers.Damage(h, structure, force, counter, spell, window, inkPower, equipment);
            decimal c = spell ? Math.Min(structure * force, numbers.Source.Get("combat.peak.structureForceCap")) : 1m;
            decimal k = spell && window && counter ? numbers.Source.Get("combat.counter.multiplier") : 1m;
            decimal b = window ? numbers.WindowDamageMultiplier : 1m;
            decimal a = Math.Min(equipment, numbers.EquipmentCap);
            return new Vol2DamageTerms(h, structure, force, c, k, b, inkPower, a, d, spell, counter, window, hpPerUnit);
        }
    }

    /// <summary>What EnemyVitals.TakeResolvedVol2Damage did with one hit: the terms it was given, what its armour made of the raw HP,
    /// what was really lost. Kept as EnemyVitals.LastVol2Hit and raised as Vol2DamageApplied.</summary>
    public readonly struct Vol2HitRecord
    {
        public readonly Vol2DamageTerms Terms;
        public readonly bool HasTerms;
        public readonly long AttackId;
        public readonly float RequestedHp;
        public readonly float AfterIntakeHp;
        public readonly float AppliedHp;
        public readonly bool Floored;
        public readonly bool Killed;
        public Vol2HitRecord(Vol2DamageTerms terms, bool hasTerms, long attackId, float requested, float afterIntake, float applied, bool floored, bool killed)
        { Terms = terms; HasTerms = hasTerms; AttackId = attackId; RequestedHp = requested; AfterIntakeHp = afterIntake; AppliedHp = applied; Floored = floored; Killed = killed; }
        public override string ToString()
        {
            var inv = CultureInfo.InvariantCulture;
            return "attack=" + AttackId + " requested=" + RequestedHp.ToString("0.###", inv) + " afterIntake=" + AfterIntakeHp.ToString("0.###", inv) +
                   " applied=" + AppliedHp.ToString("0.###", inv) + (Floored ? " floored" : "") + (Killed ? " killed" : "") + (HasTerms ? " | " + Terms : "");
        }
    }

    /// <summary>The attack identity of a vol.2 hit. Same ids as AttackProvenance.Create hands out (one counter), but NoCredit is set so the
    /// five-element completion can never count a vol.2 hit even if some old path were to read it (a marker only: the proof that no old
    /// multiplier is applied is the dedicated entry, not this flag).</summary>
    public static class Vol2Attack
    {
        public static AttackProvenance Create(UnityEngine.Object instigator, DamageSource source, Oheangbu.Core.Domain.Element? element, bool melee = false)
        {
            long id = AttackProvenance.Create(instigator, source, element).AttackId;
            return new AttackProvenance(id, instigator, source, element, melee, true);
        }
    }
}
