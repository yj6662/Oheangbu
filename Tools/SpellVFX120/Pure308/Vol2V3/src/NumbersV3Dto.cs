using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
namespace Oheangbu.Vol2.Rules
{
    public sealed class NumbersV3Dto
    {
        readonly Dictionary<string, NumbersV3RowDto> byKey;
        public ReadOnlyCollection<NumbersV3RowDto> Rows { get; }
        public bool IsValidated { get; }
        public string Profile { get; }
        public NumbersV3Dto(IEnumerable<NumbersV3RowDto> rows) : this(rows, false, "current") { }
        internal NumbersV3Dto(IEnumerable<NumbersV3RowDto> rows, bool validated, string profile = "current")
        {
            Rows = RuleTypesV3.Freeze(rows); IsValidated = validated; Profile = profile;
            byKey = new Dictionary<string, NumbersV3RowDto>(StringComparer.Ordinal);
            foreach (var row in Rows) if (!byKey.ContainsKey(row.Key)) byKey.Add(row.Key, row);
        }
        public decimal Get(string key) => byKey.TryGetValue(key, out var r) && r.Value.HasValue ? r.Value.Value : throw new InvalidOperationException("Missing value: " + key);
        public NumbersV3RowDto Row(string key) => byKey[key];
        internal bool Has(string key) => byKey.ContainsKey(key);
        internal NumbersV3Dto Validated() => new NumbersV3Dto(Rows, true, Profile);
        public NumbersV3Dto CreateEoCandidateProfile()
        {
            if (!IsValidated) throw new InvalidOperationException("Unvalidated numbers");
            var rows = new List<NumbersV3RowDto>();
            foreach (var r in Rows)
                rows.Add(r.CandidateTrigger.StartsWith("E7 ⑨:", StringComparison.Ordinal)
                    ? r.WithValue(decimal.Parse(r.Candidate, CultureInfo.InvariantCulture)) : r);
            return new NumbersV3Dto(rows, true, "eo-candidate-09");
        }
    }
}

namespace Oheangbu.Vol2.Rules
{
    // CSV 원문은 보존한다. 결정의 새 수치만 별도 TEST 묶음으로 전달한다.
    public sealed class ReactionNumbersV3Dto
    {
        public NumbersV3Dto Source { get; }
        public decimal SameMultiplier => Source.Get("reaction.sameBody.maxMultiplier");
        public decimal ChildMultiplier => Source.Get("reaction.child.parentMultiplier");
        public decimal BlockedRemaining => Source.Get("reaction.blocked.remaining");
        public int Depth => (int)Source.Get("reaction.depth");
        public long ChildLifeUs => RuleTypesV3.Microseconds(Source.Get("timing.child.life"));
        public long FadeUs => RuleTypesV3.Microseconds(Source.Get("reaction.child.fade"));
        public ReactionNumbersV3Dto(NumbersV3Dto source)
        { if (source == null || !source.IsValidated) throw new System.ArgumentException("Validated numbers required"); Source = source; }
    }
    public sealed class DefenceNumbersV3Dto
    {
        public string Status => "TEST";
        public string DecisionSource => "D308-145; D308-147; debate30/v04_final.md; debate31/v04_final.md";
        public NumbersV3Dto Source { get; }
        public decimal ParryPosture { get; } = 40m;
        public decimal ActiveSeedPosture { get; } = 60m;
        public decimal BladePosture { get; } = 10m;
        public decimal PostureThreshold { get; } = 100m;
        public decimal FirstWindowSeconds { get; } = 2m;
        public decimal WindowCapSeconds { get; } = 5m;
        public decimal WindowDecay => Source.Get("combat.groggy.decay");
        public long ResetUs => RuleTypesV3.Microseconds(Source.Get("combat.groggy.reset"));
        public long ParryWindowUs => RuleTypesV3.Microseconds(Source.Get("combat.parry.window"));
        public long ParryCooldownUs => RuleTypesV3.Microseconds(Source.Get("combat.parry.cooldown"));
        public decimal FailureCost => Source.Get("combat.parry.failureCost");
        public long SwingCooldownUs => RuleTypesV3.Microseconds(Source.Get("combat.noInk.cooldown"));
        public decimal SwingDamage => Source.Get("combat.noInk.damage");
        public decimal HpFloor { get; } = 1m;
        public decimal GatherPerHit { get; } = 4m;
        public decimal GatherNormal { get; } = 12m;
        public decimal GatherBoss { get; } = 24m;
        public decimal HealingHp { get; } = 30m;
        public decimal HealingCost { get; } = 26m;
        public long HealingLifeUs { get; } = 3000000;
        public long HealingCooldownUs { get; } = 20000000;
        public decimal OutsideHealingBudget { get; } = 30m;
        public decimal WindowDamageMultiplier { get; } = 1.5m;
        public decimal EquipmentCap { get; } = 10m;
        public decimal PercentDivisor { get; } = 100m;
        public long MinimumTelegraphUs { get; } = 800000;
        public DefenceNumbersV3Dto(NumbersV3Dto source)
        { if (source == null || !source.IsValidated) throw new System.ArgumentException("Validated numbers required"); Source = source; }
        // D308-147: K는 창 안 술식에만, B는 모든 피해에 한 번 적용한다. 후보 복귀는 자동 적용하지 않는다.
        public decimal Damage(decimal hp, decimal structure, decimal force, bool counter, bool spell, bool window,
            decimal inkPower = 0, decimal equipment = 0)
        {
            if (hp < 0 || structure < 0 || force < 0 || inkPower < 0 || equipment < 0) throw new System.ArgumentOutOfRangeException();
            decimal c = spell ? System.Math.Min(structure * force, Source.Get("combat.peak.structureForceCap")) : 1m;
            if (spell && window && counter) c *= Source.Get("combat.counter.multiplier");
            return hp * c * (window ? WindowDamageMultiplier : 1m) * (1m + inkPower) *
                (1m + System.Math.Min(equipment, EquipmentCap) / PercentDivisor);
        }
    }
}
