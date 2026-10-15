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
        bool _a3Stopping;

        public bool Aura308Configured => _aura308 != null;

        void BuildAura308()
        {
            if (Profile == null || Profile.Aura308Prefab == null) return;
            _aura308 = Instantiate(Profile.Aura308Prefab, transform, false);
            _aura308.name = Profile.Aura308Prefab.name;
            // world size, whatever the effect is scaled by (sw_z: a buff host scaled up threw its strokes over the whole sky);
            // the ward ring is built at 1 m and scaled by WardRadius
            float world = Profile.WardRadius > 0f ? Profile.WardRadius : 1f;
            _aura308.transform.localScale = Vector3.one * world / Mathf.Max(1e-4f, transform.lossyScale.x);
            _a3Systems = _aura308.GetComponentsInChildren<ParticleSystem>(true);
            _a3Stopping = false;
            foreach (var ps in _a3Systems) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(false); }
        }

        void SampleAura308()
        {
            if (_aura308 == null) return;
            // the buff host is parented to the player after Begin: keep the world size every frame
            float world = Profile.WardRadius > 0f ? Profile.WardRadius : 1f;
            _aura308.transform.localScale = Vector3.one * world / Mathf.Max(1e-4f, transform.lossyScale.x);
            // the old builders' children stay off (some are made after this one, or lazily)
            foreach (Transform child in transform)
                if (child.gameObject != _aura308 && child.gameObject.activeSelf) child.gameObject.SetActive(false);
            bool ending = Life > 0f && Age >= Life - Profile.Aura308FadeSeconds;
            if (ending && !_a3Stopping) { _a3Stopping = true; foreach (var ps in _a3Systems) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); }
        }

        void ClearAura308()
        {
            if (_aura308 != null) { _aura308.SetActive(false); if (Application.isPlaying) Destroy(_aura308); else DestroyImmediate(_aura308); }
            _aura308 = null; _a3Systems = new ParticleSystem[0]; _a3Stopping = false;
        }
    }
}
