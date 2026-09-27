using UnityEngine;

namespace Oheangbu.App.Demo
{
    [CreateAssetMenu(menuName = "Oheangbu/Demo/South Gate General Profile")]
    public sealed class SouthGateGeneralProfile : ScriptableObject
    {
        [Header("Demo TEST tuning, not final balance")]
        public float EngageRange = 16, LeashRange = 24, InitialDelay = 1.1f, Cooldown = .9f, CancelRecovery = .65f;
        public float VerticalTolerance = 2.1f, CounterWindow = 1.8f;
        public LayerMask EnvironmentMask = ~0;
        public Vector3 WeaponOffset = new Vector3(0, 1.05f, .65f);
        public float ThrustRange = 3.8f, ThrustRadius = .55f, ThrustWindup = .85f, ThrustActive = .2f, ThrustRecovery = .7f, ThrustDamage = 13;
        public float SweepRange = 3.4f, SweepHalfAngle = 85, SweepWindup = 1.05f, SweepActive = .36f, SweepRecovery = .85f, SweepDamage = 16;
        public float WaveRange = 14, WaveHalfAngle = 38, WaveWidth = .6f, WaveSpeed = 8, WaveWindup = 1.2f, WaveRecovery = .9f, WaveDamage = 15;
        public float ComboEmpowerSeconds = 1.2f, ComboEarthDamage = 17;

        public bool TryValidate(out string error)
        {
            foreach (float v in new[] { EngageRange, LeashRange, InitialDelay, Cooldown, CancelRecovery, VerticalTolerance, CounterWindow,
                ThrustRange, ThrustRadius, ThrustWindup, ThrustActive, ThrustRecovery, SweepRange, SweepWindup, SweepActive, SweepRecovery,
                WaveRange, WaveWidth, WaveSpeed, WaveWindup, WaveRecovery, ComboEmpowerSeconds })
                if (!float.IsFinite(v) || v <= 0 || v > 100) { error = "Distances/times must be finite and positive, at most 100."; return false; }
            foreach (float v in new[] { ThrustDamage, SweepDamage, WaveDamage, ComboEarthDamage })
                if (!float.IsFinite(v) || v < 0 || v > 1000) { error = "Damage must be finite in 0..1000."; return false; }
            if (!Finite(WeaponOffset) || WeaponOffset.magnitude > 10 || EnvironmentMask.value == 0 ||
                EngageRange > LeashRange || ThrustRange > EngageRange || SweepRange > EngageRange || WaveRange > EngageRange ||
                ThrustRadius >= ThrustRange || WaveWidth >= WaveRange || !float.IsFinite(SweepHalfAngle) || SweepHalfAngle < 1 || SweepHalfAngle > 180 ||
                !float.IsFinite(WaveHalfAngle) || WaveHalfAngle < 1 || WaveHalfAngle > 89)
            { error = "Invalid sockets, environment mask or ordered geometry."; return false; }
            error = null; return true;
        }
        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
