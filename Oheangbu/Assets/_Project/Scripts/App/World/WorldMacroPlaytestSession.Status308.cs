using Oheangbu.Data.Demo;

namespace Oheangbu.App.World
{
    /// <summary>#308 SPEC-UI-EQUIPMENT-308 §4: what the 소지품 status column shows, read once per page build. Values only;
    /// nothing here writes to the session, the vitals, the ink pool or the save.</summary>
    public readonly struct PlayerStatus308
    {
        /// <summary>현재 / 최대 체력 (PlayerVitals: Hp01 x MaxHp, MaxHp = config x 배율).</summary>
        public readonly float Hp, MaxHp;
        /// <summary>먹: 채움 비율 0..1 and the capacity multiplier (base capacity = 1).</summary>
        public readonly float Ink01, InkCapacity;
        /// <summary>자연 회복, base-capacity units per second with the 오행 마석 grade multiplier (0 = none / not readable).</summary>
        public readonly float InkRegenPerSecond;
        /// <summary>True when Stats holds the same RuntimePlayerStats the game applied (ApplyDemoStats).</summary>
        public readonly bool HasStats;
        public readonly RuntimePlayerStats Stats;

        public PlayerStatus308(float hp, float maxHp, float ink01, float inkCapacity, float inkRegenPerSecond, bool hasStats, RuntimePlayerStats stats)
        { Hp = hp; MaxHp = maxHp; Ink01 = ink01; InkCapacity = inkCapacity; InkRegenPerSecond = inkRegenPerSecond; HasStats = hasStats; Stats = stats; }
    }

    public sealed partial class WorldMacroPlaytestSession
    {
        /// <summary>Read-only snapshot for the menu. False until the session is ready (vitals and ink bound).</summary>
        public bool TryGetStatus308(out PlayerStatus308 status)
        {
            status = default;
            if (!ready || vitals == null || ink == null || Progress == null) return false;
            float regen = 0f;
            var wiring = Walker != null ? Walker.Wiring : null;
            var config = wiring != null ? wiring.Config308 : null;
            if (config != null)
            {
                // the grade multiplier is the regenerator's own (x1 until something calls InkRegenerator.SetGrade)
                float grade = wiring.InkRegen != null ? wiring.InkRegen.GradeMultiplier : 1f;
                regen = config.InkRegenPerSecond * grade;
            }
            bool hasStats = false; RuntimePlayerStats stats = default;
            if (DemoEconomy != null && Content != null && Content.Economy != null)
            {
                var rules = Content.Economy.CreateRules();
                if (Progress.economy != null && Progress.economy.IsValid(rules))
                {
                    // same constructor arguments as ApplyDemoStats: what the game applied is what the menu shows
                    stats = new RuntimePlayerStats(Progress.economy, rules, EquipmentEnabled ? Progress.equipment : null, Content.EquipmentCatalog);
                    hasStats = true;
                }
            }
            float max = vitals.MaxHp;
            status = new PlayerStatus308(vitals.Hp01 * max, max, ink.Value, ink.CapacityMultiplier, regen, hasStats, stats);
            return true;
        }
    }
}
