using UnityEngine;
using Oheangbu.Spellcraft;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        private Vfx120TraditionalMotif _nativeBody;
        public Vfx120TraditionalMotif NativeBody => _nativeBody;
        public bool NativeBodyConfigured { get; private set; }
        public string NativeBodyDiagnostic { get; private set; } = "UNASSIGNED";

        private static bool WashFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool WashFinite(Vector3 value) => WashFinite(value.x) && WashFinite(value.y) && WashFinite(value.z);

        private void BuildElementWash()
        {
            if (Profile.NativeBodyPrefab == null || Profile.NativeBodyMotion == Vfx120NativeBodyMotion.None) return;
            var plan = ReceivedAreaPlan;
            bool cone = Profile.NativeBodyMotion == Vfx120NativeBodyMotion.FlameCone;
            bool path = Profile.NativeBodyMotion == Vfx120NativeBodyMotion.SandFront;
            // A missing plan is not permission to synthesize a damage cone/path.
            // Existing presentation remains available for callers without spatial data.
            if (plan == null || (cone && plan.Shape != AreaShape.Cone) || (path && plan.Shape != AreaShape.Path)
                || (!cone && !path) || !WashFinite(plan.Point) || !WashFinite(plan.Direction)
                || !WashFinite(plan.Length) || !WashFinite(plan.Delay) || plan.Length <= 0 || plan.Delay < 0
                || (cone && (!WashFinite(plan.Angle) || plan.Angle <= 0 || plan.Angle >= 89))
                || (path && (!WashFinite(plan.Radius) || !WashFinite(plan.Speed) || plan.Radius <= 0 || plan.Speed <= 0)))
            { NativeBodyDiagnostic = "COMPATIBLE_SPATIAL_PLAN_REQUIRED"; return; }

            var host = new GameObject("Native_ElementBody");
            host.transform.SetParent(transform, false);
            var body = host.AddComponent<Vfx120TraditionalMotif>();
            var settings = Vfx120TraditionalMotif.Settings.DefaultFor(Vfx120TraditionalMotif.Role.Cast);
            settings.Lifetime = Life;
            settings.FadeSeconds = Mathf.Min(.65f, Life);
            settings.HoldAt = -1; // Flame and loose soil keep their authored particle motion.
            settings.MaxSystems = 12; settings.MaxParticles = 240;
            settings.PreviewControlled = PreviewControlled;
            if (!body.Configure(Profile.NativeBodyPrefab, Profile.Pigment, Profile.Ink,
                Vfx120TraditionalMotif.Role.Cast, settings))
            {
                NativeBodyDiagnostic = body.Diagnostic;
                DisposeNativeHost(host);
                return;
            }
            _nativeBody = body;
            NativeBodyConfigured = true;
            NativeBodyDiagnostic = "CONFIGURED_SPATIAL_PARTICLE_BODY";
            PlaceElementWash();
        }

        private void PlaceElementWash()
        {
            if (_nativeBody == null || !NativeBodyConfigured) return;
            var plan = ReceivedAreaPlan;
            var carrier = _nativeBody.transform;
            Vector3 direction = AreaDirection();
            carrier.localRotation = Quaternion.LookRotation(direction, Vector3.up);
            if (Profile.NativeBodyMotion == Vfx120NativeBodyMotion.FlameCone)
            {
                // Source unit cone spans z=0..1 and x=+/-z. Expansion is complete
                // at the already-issued impact Delay, never postponed by animation.
                float reach = plan.Delay <= 0 ? 1 : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Mathf.Max(0, plan.Delay - .22f),
                    Mathf.Max(.001f, plan.Delay), Age));
                float length = plan.Length * Mathf.Max(.001f, reach);
                float halfWidth = plan.Length * Mathf.Tan(plan.Angle * Mathf.Deg2Rad) * Mathf.Max(.001f, reach);
                carrier.localPosition = transform.InverseTransformPoint(plan.Point);
                carrier.localScale = new Vector3(halfWidth, .9f, length);
            }
            else
            {
                // Identical issued path clock as Center/ApplyAreaPose; no terrain
                // raycasts, collision proxies or gameplay parameters are added here.
                carrier.localPosition = Center() + Vector3.up * .02f;
                carrier.localScale = new Vector3(plan.Radius * 2, 1, 1);
            }
        }

        private void SampleElementWash()
        {
            if (_nativeBody == null) return;
            PlaceElementWash();
            if (PreviewControlled) _nativeBody.Sample(Age);
            else if (Age >= Life) ClearNative(ref _nativeBody);
        }
    }
}
