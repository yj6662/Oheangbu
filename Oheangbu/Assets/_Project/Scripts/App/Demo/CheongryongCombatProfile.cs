using UnityEngine;

namespace Oheangbu.App.Demo
{
    // Initial demo TEST values. This asset is tuning data, not a difficulty/balance claim.
    [CreateAssetMenu(menuName = "Oheangbu/Demo/Cheongryong Combat Profile", fileName = "CheongryongCombatProfile")]
    public sealed class CheongryongCombatProfile : ScriptableObject
    {
        [Header("Encounter (demo TEST)")]
        [Min(.1f)] public float EngageRange = 18f, LeashRange = 24f;
        [Min(.01f)] public float InitialDelay = 1.2f, CooldownSeconds = 1.1f, CancelRecoverySeconds = .65f;
        [Range(.25f, 1f)] public float PhaseTwoCooldownFactor = .72f;
        [Min(.1f)] public float VerticalTolerance = 2.5f;
        public LayerMask EnvironmentMask = ~0;
        public Vector3 HeadOriginOffset = new Vector3(0, 1.1f, 1.7f);
        public Vector3 TailOriginOffset = new Vector3(0, .7f, -2f);

        [Header("Head bite: neutral, dodge")]
        [Min(.01f)] public float BiteRange = 3.2f, BiteWindup = .85f, BiteActiveSeconds = .16f, BiteRecovery = .65f;
        [Range(1, 180)] public float BiteHalfAngle = 45f;
        [Min(0)] public float BiteDamage = 13f;

        [Header("Broad tail sweep: neutral, dodge")]
        [Min(.01f)] public float TailRange = 5.6f, TailWindup = 1.1f, TailActiveSeconds = .32f, TailRecovery = .85f;
        [Range(1, 180)] public float TailHalfAngle = 130f;
        [Min(0)] public float TailDamage = 16f;

        [Header("Fixed ground eruption: Wood, Metal parry")]
        [Min(.01f)] public float RootRadius = 1.8f, RootWindup = 1.35f, RootActiveSeconds = .22f, RootRecovery = .75f;
        [Min(0)] public float RootDamage = 14f;
        [Min(.01f)] public float GroundProbeUp = 2f, GroundProbeDown = 5f;

        [Header("Fixed mouth projectile: Wood, Metal parry")]
        [Min(.01f)] public float ProjectileRange = 18f, ProjectileRadius = .6f, ProjectileSpeed = 10f;
        [Min(.01f)] public float ProjectileWindup = 1.15f, ProjectileRecovery = .75f;
        [Min(0)] public float ProjectileDamage = 14f;

        public bool TryValidate(out string error)
        {
            foreach (float value in new[] { EngageRange, LeashRange, InitialDelay, CooldownSeconds, CancelRecoverySeconds,
                VerticalTolerance, BiteRange, BiteWindup, BiteActiveSeconds, BiteRecovery, TailRange, TailWindup,
                TailActiveSeconds, TailRecovery, RootRadius, RootWindup, RootActiveSeconds, RootRecovery,
                GroundProbeUp, GroundProbeDown, ProjectileRange, ProjectileRadius, ProjectileSpeed, ProjectileWindup, ProjectileRecovery })
                if (!float.IsFinite(value) || value <= 0f || value > 100f)
                { error = "Cheongryong TEST distances and times must be finite, positive and bounded (<=100)."; return false; }
            foreach (float value in new[] { BiteDamage, TailDamage, RootDamage, ProjectileDamage })
                if (!float.IsFinite(value) || value < 0f || value > 1000f)
                { error = "Cheongryong TEST damage must be finite and between zero and 1000."; return false; }
            if (!Finite(HeadOriginOffset) || !Finite(TailOriginOffset) || HeadOriginOffset.magnitude > 20f || TailOriginOffset.magnitude > 20f ||
                !float.IsFinite(BiteHalfAngle) || BiteHalfAngle < 1f || BiteHalfAngle > 180f ||
                !float.IsFinite(TailHalfAngle) || TailHalfAngle < 1f || TailHalfAngle > 180f ||
                !float.IsFinite(PhaseTwoCooldownFactor) || PhaseTwoCooldownFactor < .25f || PhaseTwoCooldownFactor > 1f ||
                EngageRange > LeashRange || BiteRange > EngageRange || TailRange > EngageRange || RootRadius >= EngageRange ||
                ProjectileRange > EngageRange || ProjectileRadius >= ProjectileRange || EnvironmentMask.value == 0)
            { error = "Cheongryong requires ordered ranges, finite sockets/angles, environment layers and a bounded phase-two cadence."; return false; }
            error = null; return true;
        }

        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
