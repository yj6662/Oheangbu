using System;

namespace Oheangbu.Data.Demo
{
    /// <summary>Immutable balance snapshot. Bonuses are totals, never multiplicatively compounded levels.</summary>
    public sealed class DemoEconomyRules
    {
        readonly int[] stoneCosts, capacityCosts;
        readonly float[] stoneBonuses, capacityBonuses;

        public static DemoEconomyRules ApprovedDefaults => new DemoEconomyRules(
            new[] { 60, 120, 240 }, new[] { .1f, .2f, .3f },
            new[] { 100, 200 }, new[] { .1f, .2f });

        public DemoEconomyRules(int[] stoneCosts, float[] stoneTotalBonuses, int[] capacityCosts, float[] capacityTotalBonuses)
        {
            Validate(stoneCosts, stoneTotalBonuses, 3, "stone");
            Validate(capacityCosts, capacityTotalBonuses, 2, "capacity");
            this.stoneCosts = (int[])stoneCosts.Clone();
            stoneBonuses = (float[])stoneTotalBonuses.Clone();
            this.capacityCosts = (int[])capacityCosts.Clone();
            capacityBonuses = (float[])capacityTotalBonuses.Clone();
        }

        public static bool IsTrack(DemoUpgradeTrack track) => (int)track >= 0 && (int)track < 7;
        public int MaximumLevel(DemoUpgradeTrack track) { CheckTrack(track); return (int)track < 5 ? 3 : 2; }

        public int CostForNextLevel(DemoUpgradeTrack track, int currentLevel)
        {
            if (currentLevel < 0 || currentLevel >= MaximumLevel(track)) throw new ArgumentOutOfRangeException(nameof(currentLevel));
            return ((int)track < 5 ? stoneCosts : capacityCosts)[currentLevel];
        }

        public float TotalBonus(DemoUpgradeTrack track, int level)
        {
            if (level < 0 || level > MaximumLevel(track)) throw new ArgumentOutOfRangeException(nameof(level));
            return level == 0 ? 0f : ((int)track < 5 ? stoneBonuses : capacityBonuses)[level - 1];
        }

        static void CheckTrack(DemoUpgradeTrack track)
        { if (!IsTrack(track)) throw new ArgumentOutOfRangeException(nameof(track)); }

        static void Validate(int[] costs, float[] bonuses, int expectedLevels, string name)
        {
            if (costs == null || bonuses == null || costs.Length != expectedLevels || bonuses.Length != expectedLevels)
                throw new ArgumentException(name + " needs exactly " + expectedLevels + " tiers.");
            float previous = 0;
            for (int i = 0; i < costs.Length; i++)
            {
                if (costs[i] <= 0 || float.IsNaN(bonuses[i]) || float.IsInfinity(bonuses[i]) || bonuses[i] <= previous)
                    throw new ArgumentException(name + " prices must be positive and total bonuses finite and increasing.");
                previous = bonuses[i];
            }
        }
    }
}
