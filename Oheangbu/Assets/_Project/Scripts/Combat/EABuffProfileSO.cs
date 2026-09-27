using UnityEngine;

namespace Oheangbu.Combat
{
    [CreateAssetMenu(menuName = "Oheangbu/Combat/EA Buffs TEST")]
    public sealed class EABuffProfileSO : ScriptableObject
    {
        [Tooltip("걱, 넉, 먹, 석, 억 순서. 후보에서만 참조하는 기존 술식 표현 프리팹")]
        public GameObject[] PresentationPrefabs = new GameObject[5];
        [Min(.1f)] public float Duration = 30f;
        [Min(.1f)] public float AuraDuration = 12f;
        [Range(0f, .1f)] public float RegenMaxHpPerSecond = .003f;
        [Min(0f)] public float AuraDamagePerSecond = 2f;
        [Min(.1f)] public float AuraRadius = 2f;
        [Range(0f, .8f)] public float RangedReduction = .15f;
        [Range(0f, .8f)] public float CostReduction = .2f;
        [Min(0f)] public float HoldGrace = 6f;
        [Min(0f)] public float ExtraHoldGrace = 3f;
        [Range(0f, 1f)] public float HoldLossPerSecond = .06f;
        [Range(.1f, 1f)] public float MinimumHoldPower = .4f;

        public bool Valid => Positive(Duration) && Positive(AuraDuration) && Positive(AuraRadius)
            && Bounded(RegenMaxHpPerSecond, 0, .1f) && Bounded(AuraDamagePerSecond, 0, 100)
            && Bounded(RangedReduction, 0, .8f) && Bounded(CostReduction, 0, .8f)
            && Bounded(HoldGrace, 0, 120) && Bounded(ExtraHoldGrace, 0, 120)
            && Bounded(HoldLossPerSecond, 0, 1) && Bounded(MinimumHoldPower, .1f, 1);
        static bool Positive(float x) => float.IsFinite(x) && x > 0;
        static bool Bounded(float x, float lo, float hi) => float.IsFinite(x) && x >= lo && x <= hi;
    }
}
