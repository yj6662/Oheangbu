using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // D308-84 stage 4 (SPEC-SPELL-VFX-OVERHAUL-308 11.22): the #308 loop body of a lasting spell - a buff (ink wound up the
    // body) or a ward (ink membranes standing on its ring). When the profile carries Aura308Prefab it plays for the effect's
    // life (the EA runtime sets Duration; a ward's ring is scaled by WardRadius) and owns the effect: every other child the
    // old builders made (Regrowth, Native_Cast, FixedWard ...) is kept switched off, and the procedural body steps aside.
    // Presentation only: buff and ward rules are the EA runtimes'.
    public sealed partial class Vfx120Effect
    {
        GameObject _aura308;
        ParticleSystem[] _a3Systems = new ParticleSystem[0];
        ParticleSystem[] _a3End = new ParticleSystem[0];
        Transform _a3Cam;
        bool _a3Stopping;
        bool _a3Late;

        public bool Aura308Configured => _aura308 != null;

        void BuildAura308()
        {
            if (Profile == null || Profile.Aura308Prefab == null) return;
            _aura308 = Instantiate(Profile.Aura308Prefab, transform, false);
            _aura308.name = Profile.Aura308Prefab.name;
            // world size, whatever the effect is scaled by (sw_z: a buff host scaled up threw its strokes over the whole sky);
            // the ward ring is built at 1 m and scaled by WardRadius
            float world = Profile.Aura308ByWardRadius && Profile.WardRadius > 0f ? Profile.WardRadius : 1f;   // sw_z3: a buff copy carried WardRadius 3 from its source
            _aura308.transform.localScale = Vector3.one * world / Mathf.Max(1e-4f, transform.lossyScale.x);
            // 11.50: systems under an "End" child wait for the fade (the buff's dry rim); a "Cam" child turns with the camera
            var end = _aura308.transform.Find("End");
            _a3Cam = _aura308.transform.Find("Cam");
            _a3End = end != null ? end.GetComponentsInChildren<ParticleSystem>(true) : new ParticleSystem[0];
            _a3Systems = System.Array.FindAll(_aura308.GetComponentsInChildren<ParticleSystem>(true), ps => end == null || !ps.transform.IsChildOf(end));
            _a3Stopping = false; _a3Late = false;
            foreach (var ps in _a3End) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var ps in _a3Systems) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(false); }
            FaceCamera308();
        }

        void FaceCamera308()
        {
            if (_a3Cam == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 f = cam.transform.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-6f) _a3Cam.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        void SampleAura308()
        {
            if (_aura308 == null) return;
            // the buff host is parented to the player after Begin: keep the world size every frame
            float world = Profile.Aura308ByWardRadius && Profile.WardRadius > 0f ? Profile.WardRadius : 1f;   // sw_z3: a buff copy carried WardRadius 3 from its source
            _aura308.transform.localScale = Vector3.one * world / Mathf.Max(1e-4f, transform.lossyScale.x);
            // the old builders' children stay off (some are made after this one, or lazily)
            foreach (Transform child in transform)
                if (child.gameObject != _aura308 && child.gameObject.activeSelf) child.gameObject.SetActive(false);
            FaceCamera308();
            // 11.54 (D308-110): dark first, faint after - from LateAfter the loops emit at LateAlpha; drawn particles keep their colour and die out
            if (!_a3Late && !_a3Stopping && Profile.Aura308LateAfter > 0f && Age >= Profile.Aura308LateAfter)
            {
                _a3Late = true;
                foreach (var ps in _a3Systems) FadeStart308(ps, Profile.Aura308LateAlpha);
            }
            bool ending = Life > 0f && Age >= Life - Profile.Aura308FadeSeconds;
            if (ending && !_a3Stopping)
            {
                _a3Stopping = true;
                foreach (var ps in _a3Systems) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                foreach (var ps in _a3End) ps.Play(false);
            }
        }

        // 11.62: a lasting body let go before its life (a consumed WP buff, an expired tree) dries over its fade instead of
        // vanishing; the effect then destroys itself. False = not a lasting #308 body, the caller removes it as before.
        public bool DryAura308()
        {
            if (_aura308 == null || Profile == null || !Profile.Aura308Lasting || !Begun || PreviewControlled) return false;
            Life = Mathf.Min(Life, Age + Mathf.Max(.1f, Profile.Aura308FadeSeconds));
            return true;
        }

        static void FadeStart308(ParticleSystem ps, float alpha)
        {
            var main = ps.main;
            var c = main.startColor;
            if (c.mode == ParticleSystemGradientMode.Color) { var k = c.color; k.a *= alpha; main.startColor = new ParticleSystem.MinMaxGradient(k); }
            else if (c.mode == ParticleSystemGradientMode.TwoColors) { var a = c.colorMin; var b = c.colorMax; a.a *= alpha; b.a *= alpha; main.startColor = new ParticleSystem.MinMaxGradient(a, b); }
        }

        void ClearAura308()
        {
            if (_aura308 != null) { _aura308.SetActive(false); if (Application.isPlaying) Destroy(_aura308); else DestroyImmediate(_aura308); }
            _aura308 = null; _a3Systems = new ParticleSystem[0]; _a3End = new ParticleSystem[0]; _a3Cam = null; _a3Stopping = false; _a3Late = false;
        }
    }
}
