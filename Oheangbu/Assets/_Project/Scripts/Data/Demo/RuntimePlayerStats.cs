using System;
using Oheangbu.Core.Domain;

namespace Oheangbu.Data.Demo
{
    /// <summary>Derived values only: shared CombatConfig and ScriptableObjects are never changed.</summary>
    public readonly struct RuntimePlayerStats
    {
        readonly float wood, fire, earth, metal, water;
        public float MaxHpMultiplier { get; }
        public float MaxInkMultiplier { get; }

        public RuntimePlayerStats(DemoEconomyState state, DemoEconomyRules rules, EquipmentState gear=null, EquipmentCatalogSO catalog=null)
        {
            if (state == null || !state.IsValid(rules)) throw new ArgumentException("Invalid demo economy state.", nameof(state));
            float all=0,woodBonus=0,hp=0,ink=0;
            if(gear!=null&&gear.Matches(catalog))foreach(var id in gear.Equipped){if(string.IsNullOrEmpty(id))continue;var d=catalog.Find(id);float b=catalog.Bonus(gear,id);switch(d.Effect){case EquipmentEffect.AllDamage:all+=b;break;case EquipmentEffect.WoodDamage:woodBonus+=b;break;case EquipmentEffect.MaxHealth:hp+=b;break;case EquipmentEffect.MaxInk:ink+=b;break;}}
            wood = 1f + all + woodBonus + rules.TotalBonus(DemoUpgradeTrack.Wood, state.Level(DemoUpgradeTrack.Wood));
            fire = 1f + all + rules.TotalBonus(DemoUpgradeTrack.Fire, state.Level(DemoUpgradeTrack.Fire));
            earth = 1f + all + rules.TotalBonus(DemoUpgradeTrack.Earth, state.Level(DemoUpgradeTrack.Earth));
            metal = 1f + all + rules.TotalBonus(DemoUpgradeTrack.Metal, state.Level(DemoUpgradeTrack.Metal));
            water = 1f + all + rules.TotalBonus(DemoUpgradeTrack.Water, state.Level(DemoUpgradeTrack.Water));
            MaxHpMultiplier = 1f + hp + rules.TotalBonus(DemoUpgradeTrack.Health, state.Level(DemoUpgradeTrack.Health));
            MaxInkMultiplier = 1f + ink + rules.TotalBonus(DemoUpgradeTrack.Ink, state.Level(DemoUpgradeTrack.Ink));
        }

        public float DamageMultiplier(Element element)
        {
            switch (element)
            {
                case Element.Wood: return wood;
                case Element.Fire: return fire;
                case Element.Earth: return earth;
                case Element.Metal: return metal;
                case Element.Water: return water;
                default: throw new ArgumentOutOfRangeException(nameof(element));
            }
        }
    }
}
