using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // D308-84 stage 4 (SPEC-SPELL-VFX-OVERHAUL-308 11.22): the #308 guard - one stroke of ink stood up as a membrane for the
    // guard clock. When the profile carries Guard308Prefab the old shield (Native_Shield through the traditional-pattern motif,
    // BambooGuard) is not built for it and the procedural body steps aside. Stepped at a fixed 60 Hz like the bolt, so a
    // preview scrubbed backward restarts it. Presentation only: the guard and parry windows are the wiring's.
    public sealed partial class Vfx120Effect
    {
        GameObject _guard308;
        ParticleSystem[] _g3Systems = new ParticleSystem[0];
        float _g3Simulated;

        public bool Guard308Configured => _guard308 != null;

        void BuildGuard308()
        {
            if (Profile == null || Profile.Guard308Prefab == null) return;
            _guard308 = Instantiate(Profile.Guard308Prefab, transform, false);
            _guard308.name = Profile.Guard308Prefab.name;
            // 11.28: the prefab's root is the membrane's foot - stood on the ground found under the effect's origin at Begin
            _guard308.transform.localPosition = new Vector3(0f, _originGround, .7f);
            _guard308.transform.localScale = Vector3.one / Mathf.Max(1e-4f, transform.lossyScale.x);   // world size (sw_z: hardly seen)
            _g3Systems = _guard308.GetComponentsInChildren<ParticleSystem>(true);
            // 11.28: "Hold*" systems live the guard clock, "Wet*" the bright (parry) window, so their lifetime-normalised
            // colours end with those clocks. A preview without a clock keeps the prefab's values.
            if (ReceivedGuardClock > 0f)
                foreach (var ps in _g3Systems)
                {
                    string n = ps.gameObject.name;
                    float life = n.StartsWith("Hold") ? Life : n.StartsWith("Wet") && ReceivedGuardBrightWindow > 0f ? ReceivedGuardBrightWindow : 0f;
                    if (life <= 0f) continue;
                    var main = ps.main; main.startLifetime = life;
                }
            ResetGuard308();
        }

        void ResetGuard308()
        {
            _g3Simulated = 0f;
            foreach (var ps in _g3Systems) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Simulate(0, false, true, false); }
        }

        void SampleGuard308()
        {
            if (_guard308 == null) return;
            float end = Mathf.Min(Age, Life);
            if (end + .00001f < _g3Simulated) ResetGuard308();
            const float step = 1f / 60f;
            while (_g3Simulated + step <= end + .00001f)
            {
                foreach (var ps in _g3Systems) ps.Simulate(step, false, false, false);
                _g3Simulated += step;
            }
            if (Age >= Life) foreach (var ps in _g3Systems) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void ClearGuard308()
        {
            if (_guard308 != null) { _guard308.SetActive(false); if (Application.isPlaying) Destroy(_guard308); else DestroyImmediate(_guard308); }
            _guard308 = null; _g3Systems = new ParticleSystem[0];
        }
    }
}
