using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // #300 basic attack flight bodies (SPEC-PLAYER-FEEL-300 D), built from the free "Free Quick Effects Vol.1" textures.
    // Presentation only: the flight clock (the issued impact clock), target, damage and the KTP cast/impact patterns are
    // unchanged; this replaces the procedural/bespoke body of the five basic attacks when a profile carries a prefab.
    // Prefab contract: "Travel" moves along the element path (particles, trails, an optional "Spin" mesh body);
    // "Contact" bursts at the impact. Particles advance in fixed 60 Hz steps (same history at any frame rate, seekable).
    public sealed partial class Vfx120Effect
    {
        GameObject _bolt300;
        Transform _b3Travel, _b3Contact, _b3Spin;
        ParticleSystem[] _b3TravelSystems, _b3ContactSystems;
        TrailRenderer[] _b3Trails;
        Renderer[] _b3BodyRenderers;
        float _b3Simulated;
        bool _b3Impacted;
        Vector3 _b3Side;

        public static bool IsBolt300(Vfx120Profile p) => p != null && p.Bolt300Prefab != null;
        public bool Bolt300Configured => _bolt300 != null;
        public bool Bolt300Impacted => _b3Impacted;
        public Vector3 Bolt300Position => _b3Travel == null ? ReceivedOrigin : _b3Travel.position;

        void BuildBolt300()
        {
            if (!IsBolt300(Profile)) return;
            _bolt300 = Instantiate(Profile.Bolt300Prefab, transform, false);
            _bolt300.name = Profile.Bolt300Prefab.name;
            _b3Travel = _bolt300.transform.Find("Travel");
            _b3Contact = _bolt300.transform.Find("Contact");
            if (_b3Travel == null || _b3Contact == null) { ClearBolt300(); return; }
            _b3Spin = _b3Travel.Find("Spin");
            _b3TravelSystems = _b3Travel.GetComponentsInChildren<ParticleSystem>(true);
            _b3ContactSystems = _b3Contact.GetComponentsInChildren<ParticleSystem>(true);
            _b3Trails = _b3Travel.GetComponentsInChildren<TrailRenderer>(true);
            _b3BodyRenderers = _b3Spin != null ? _b3Spin.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            // ink-pigment bodies take the element pigment (the shader's own default is a neutral teal)
            var body = new MaterialPropertyBlock();
            foreach (var r in _b3BodyRenderers)
            {
                if (r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_Body")) continue;
                r.GetPropertyBlock(body);
                body.SetColor("_BaseColor", Color.Lerp(Profile.Pigment, Profile.Ink, .35f));
                body.SetFloat("_Alpha", 1f);
                r.SetPropertyBlock(body);
            }
            // the homing curve bends to one side of the flight line (seeded per cast, stable while seeking)
            Vector3 line = TargetPoint() - ReceivedOrigin; line.y = 0;
            Vector3 side = Vector3.Cross(Vector3.up, line.sqrMagnitude > .001f ? line.normalized : transform.forward);
            _b3Side = ((GetInstanceID() & 1) == 0 ? 1f : -1f) * side;
            if (Profile.Bolt300Scale > 0f && Mathf.Abs(Profile.Bolt300Scale - 1f) > .001f)
                _b3Travel.localScale = _b3Contact.localScale = Vector3.one * Profile.Bolt300Scale;
            ResetBolt300();
        }

        void ResetBolt300()
        {
            _b3Simulated = 0f; _b3Impacted = false;
            foreach (var ps in _b3TravelSystems) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); var em = ps.emission; em.enabled = true; ps.Simulate(0, false, true, false); }
            foreach (var ps in _b3ContactSystems) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Simulate(0, false, true, false); }
            _b3Travel.position = ReceivedOrigin;
            _b3Travel.rotation = Quaternion.LookRotation(Bolt300Tangent(0f), Vector3.up);
            foreach (var t in _b3Trails) { t.Clear(); t.emitting = true; }
            foreach (var r in _b3BodyRenderers) r.enabled = true;
        }

        // element paths: straight thrust (wood, metal), straight with a small lift (fire), a heavy arc (earth),
        // a slow bend toward the target (water); Bolt300Ease > 1 starts slow (a gathering throw)
        Vector3 Bolt300Point(float at)
        {
            float s = Mathf.Clamp01(at / Mathf.Max(.001f, _flight));
            float e = Profile.Bolt300Ease > 0f ? Mathf.Pow(s, Profile.Bolt300Ease) : s;
            Vector3 p = Vector3.Lerp(ReceivedOrigin, TargetPoint(), e);
            float bump = 4f * s * (1f - s);
            return p + Vector3.up * (Profile.Bolt300Arc * bump) + _b3Side * (Profile.Bolt300Curve * Mathf.Sin(Mathf.PI * s) * (1f - .35f * s));
        }

        Vector3 Bolt300Tangent(float at)
        {
            Vector3 d = Bolt300Point(Mathf.Min(at + .02f, _flight)) - Bolt300Point(Mathf.Max(0f, Mathf.Min(at, _flight - .02f)));
            if (d.sqrMagnitude < 1e-6f) d = TargetPoint() - ReceivedOrigin;
            return d.sqrMagnitude > 1e-6f ? d.normalized : transform.forward;
        }

        void SampleBolt300()
        {
            if (_bolt300 == null) return;
            float end = Mathf.Min(Age, Life);
            if (end + .00001f < _b3Simulated) ResetBolt300();
            const float step = 1f / 60f;
            float hit = NativeImpactClock();
            float arrive = hit >= 0f ? hit : _flight;
            while (_b3Simulated + step <= end + .00001f)
            {
                float at = _b3Simulated + step;
                bool flying = at < arrive;
                _b3Travel.position = Bolt300Point(Mathf.Min(at, arrive));
                _b3Travel.rotation = Quaternion.LookRotation(Bolt300Tangent(Mathf.Min(at, arrive)), Vector3.up);
                if (_b3Spin != null && Mathf.Abs(Profile.Bolt300Spin) > .01f) _b3Spin.localRotation = Quaternion.Euler(at * Profile.Bolt300Spin, at * Profile.Bolt300Spin * .37f, 0f);
                foreach (var ps in _b3TravelSystems) { var em = ps.emission; em.enabled = flying; ps.Simulate(step, false, false, false); }
                if (hit >= 0f && at >= hit && hit < Life)
                {
                    if (!_b3Impacted)
                    {
                        _b3Impacted = true;
                        _b3Contact.position = TargetPoint();
                        Vector3 axis = Bolt300Tangent(hit);
                        _b3Contact.rotation = Quaternion.LookRotation(-axis, Mathf.Abs(axis.y) > .98f ? Vector3.forward : Vector3.up);
                        foreach (var r in _b3BodyRenderers) r.enabled = false;
                    }
                    foreach (var ps in _b3ContactSystems) ps.Simulate(Mathf.Min(step, at - hit), false, false, false);
                }
                _b3Simulated = at;
            }
            bool stillFlying = end < arrive;
            foreach (var t in _b3Trails) t.emitting = stillFlying;
            if (!stillFlying && hit < 0f) foreach (var r in _b3BodyRenderers) r.enabled = false;   // a miss simply ends at the fallback point
            if (Age >= Life)
            {
                foreach (var ps in _b3TravelSystems) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach (var ps in _b3ContactSystems) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        void ClearBolt300()
        {
            if (_bolt300 != null) { _bolt300.SetActive(false); if (Application.isPlaying) Destroy(_bolt300); else DestroyImmediate(_bolt300); }
            _bolt300 = null; _b3Travel = _b3Contact = _b3Spin = null;
            _b3TravelSystems = _b3ContactSystems = null; _b3Trails = null; _b3BodyRenderers = null;
        }
    }
}
