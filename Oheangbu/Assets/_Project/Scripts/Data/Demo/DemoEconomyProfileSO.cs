using UnityEngine;

namespace Oheangbu.Data.Demo
{
    [CreateAssetMenu(menuName = "Oheangbu/Demo/Economy Profile")]
    public sealed class DemoEconomyProfileSO : ScriptableObject
    {
        [Tooltip("Each purchase price; three tiers per element.")]
        public int[] StoneCosts = { 60, 120, 240 };
        [Tooltip("Total damage bonus at each tier; .1/.2/.3 means 110/120/130%, not compounded.")]
        public float[] StoneTotalBonuses = { .1f, .2f, .3f };
        [Tooltip("Each purchase price; two tiers independently for HP and ink capacity.")]
        public int[] CapacityCosts = { 100, 200 };
        public float[] CapacityTotalBonuses = { .1f, .2f };

        public DemoEconomyRules CreateRules() => new DemoEconomyRules(StoneCosts, StoneTotalBonuses, CapacityCosts, CapacityTotalBonuses);
    }
}
