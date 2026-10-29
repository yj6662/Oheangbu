using UnityEngine;

namespace Oheangbu.Combat
{
    // #308 D308-125 적용 5 (TEST): what a ground eruption leaves where it burst, hit or miss: an ember patch Radius wide for Life
    // seconds that burns a player standing in it Damage every Tick seconds (dodgeable, no parry, no guard, no groggy). One enemy
    // keeps at most MaxPerOwner patches; a new one puts out its oldest. Standing Dwell seconds in a patch scorches the player:
    // ScorchDamage every Tick for ScorchLife seconds, wherever they go; a fresh dwell only renews the time. Leaving every patch
    // for more than Grace seconds starts the dwell over. Patch and scorch burn on one shared clock. Enemies never feel it.
    [CreateAssetMenu(menuName = "Oheangbu/Combat/Enemy Ground Patch", fileName = "EnemyGroundPatch")]
    public sealed class EnemyGroundPatchSO : ScriptableObject
    {
        [Min(.1f)] public float Radius = 1.35f;
        [Min(.1f)] public float Life = 6f;
        [Min(.05f)] public float Tick = .5f;
        [Min(0f)] public float Damage = 2f;
        [Min(1)] public int MaxPerOwner = 2;
        [Min(0f)] public float Dwell = .5f;
        [Min(0f)] public float Grace = .25f;
        [Min(0f)] public float ScorchLife = 4f;
        [Min(0f)] public float ScorchDamage = 1.5f;
        [Header("Look (D308-125 5 (c), presentation only)")] public GameObject Look308;
        public GameObject ScorchLook308;

        public EmberExposure308.Numbers Numbers => new EmberExposure308.Numbers(Tick, Damage, Dwell, Grace, ScorchLife, ScorchDamage);

        public bool TryValidate(out string error)
        {
            float[] positive = { Radius, Life, Tick };
            foreach (float v in positive) if (!float.IsFinite(v) || v <= 0) { error = "Patch radius, life and tick must be finite and positive"; return false; }
            float[] free = { Damage, Dwell, Grace, ScorchLife, ScorchDamage };
            foreach (float v in free) if (!float.IsFinite(v) || v < 0) { error = "Patch damage, dwell, grace and scorch values must be finite and not negative"; return false; }
            if (MaxPerOwner < 1) { error = "A patch owner keeps at least one patch"; return false; }
            error = null; return true;
        }
    }
}
