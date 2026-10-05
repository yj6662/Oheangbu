using Oheangbu.Core.Domain;
using Oheangbu.Data.Demo;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 소지품 screen wording (SPEC-UI-EQUIPMENT-308 §3 / §4). Pure functions: every number comes from the
    /// arguments (catalog, state, rules, quote values), none is written here. EquipmentChecks308 "edit" (AC-E9) calls these
    /// with data built in memory and compares the result with its own arithmetic.</summary>
    public static class EquipmentText308
    {
        public static string Pct(float v) => (v * 100f).ToString("0");
        public static string Signed(float v) => (v >= 0f ? "+" : "") + Pct(v);
        public static string Coins(int amount) => "조선통보 " + amount.ToString("N0");

        public static string EffectName(EquipmentEffect effect)
            => effect == EquipmentEffect.MaxHealth ? "최대 체력" : effect == EquipmentEffect.MaxInk ? "최대 먹"
             : effect == EquipmentEffect.WoodDamage ? "목 속성 위력" : "모든 속성 위력";

        public static string GearName(EquipmentDefinition d, EquipmentState state)
        { int level = state.Level(d.Id); return level > 0 ? d.Name + " +" + level : d.Name; }

        /// <summary>"모든 속성 위력 +6%": the item's total bonus (base + upgrades).</summary>
        public static string GearEffect(EquipmentCatalogSO catalog, EquipmentState state, EquipmentDefinition d)
            => EffectName(d.Effect) + " +" + Pct(catalog.Bonus(state, d.Id)) + "%";

        /// <summary>"기본 +4% · 강화 +2%": the two parts of the total.</summary>
        public static string GearParts(EquipmentCatalogSO catalog, EquipmentState state, EquipmentDefinition d)
            => "기본 +" + Pct(d.Bonus) + "% · 강화 +" + Pct(state.Level(d.Id) * catalog.UpgradeStep) + "%";

        public static string GearState(EquipmentState state, EquipmentDefinition d, bool worn)
        {
            int level = state.Level(d.Id);
            string step = level > 0 ? "+" + level + " 강화" : "강화 전";
            return worn ? step + " · 착용 중" : step;
        }

        /// <summary>"교체 +2%p": the candidate's bonus against what the slot wears now (0 when the slot is empty).</summary>
        public static string Compare(EquipmentCatalogSO catalog, EquipmentState state, EquipmentDefinition candidate)
        {
            string old = state.Equipped[(int)candidate.Slot];
            float before = string.IsNullOrEmpty(old) ? 0f : catalog.Bonus(state, old);
            return "교체 " + Signed(catalog.Bonus(state, candidate.Id) - before) + "%p";
        }

        public static string CompareAgainst(EquipmentCatalogSO catalog, EquipmentState state, EquipmentDefinition candidate, string slotName)
        {
            string old = state.Equipped[(int)candidate.Slot];
            var worn = string.IsNullOrEmpty(old) ? null : catalog.Find(old);
            return worn != null ? "착용 중인 " + worn.Name + Josa(worn.Name, "과", "와") + " 비교" : "비어 있는 " + slotName + " 칸과 비교";
        }

        /// <summary>True while the item can still be upgraded; `step` = "+2 강화", `cost` = the catalog's cost of that step.</summary>
        public static bool NextUpgrade(EquipmentCatalogSO catalog, EquipmentState state, EquipmentDefinition d, out string step, out int cost)
        {
            int level = state.Level(d.Id), max = catalog.UpgradeCosts != null ? catalog.UpgradeCosts.Length : 0;
            step = ""; cost = 0;
            if (level >= max) return false;
            step = "+" + (level + 1) + " 강화"; cost = catalog.UpgradeCosts[level];
            return true;
        }

        public static string UpgradeWhere(string artisan, int cost) => artisan + "에게서 " + Coins(cost);

        public static string ElementHanja(Element e) => "木火土金水".Substring(Clamp((int)e), 1);
        public static string ElementName(Element e) => "목화토금수".Substring(Clamp((int)e), 1);
        public static string StoneName(Element e) => ElementName(e) + " 마석";
        public static string StoneLevel(int level) => level + "단";
        /// <summary>"목 속성 위력 +20%" from the rules' cumulative bonus of the current grade.</summary>
        public static string StoneEffect(Element e, float totalBonus) => ElementName(e) + " 속성 위력 +" + Pct(totalBonus) + "%";
        /// <summary>"3단 +30%": the next grade and ITS cumulative bonus (a bare "+30%" read as an increase on top of the current one).</summary>
        public static string StoneNext(int nextLevel, float nextTotalBonus) => StoneLevel(nextLevel) + " +" + Pct(nextTotalBonus) + "%";
        public static string Power(float multiplier) => Signed(multiplier - 1f) + "%";
        public static string Fraction(int current, int maximum) => current + " / " + maximum;
        public static string InkRegen(float perSecond, float scale) => "초당 " + (perSecond * scale).ToString("0.#");

        /// <summary>받침 있는 말 뒤 withFinal (과, 은, 을), 없으면 withoutFinal (와, 는, 를).</summary>
        public static string Josa(string word, string withFinal, string withoutFinal)
        {
            if (string.IsNullOrEmpty(word)) return withoutFinal;
            int code = word[word.Length - 1] - 0xAC00;
            return code >= 0 && code < 11172 && code % 28 != 0 ? withFinal : withoutFinal;
        }

        static int Clamp(int i) => i < 0 ? 0 : i > 4 ? 4 : i;
    }
}
