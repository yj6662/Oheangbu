using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.57: the floor a lingering zone leaves (곡 곳 녹 농 몽). It keeps its own clock: the
    // body systems start at Delay, go faint at Delay + LateAfter (dark first, faint after - D308-110) and dry at Delay +
    // Life. Optional children "End" (played once when it dries) and "Break" (played once when the zone is broken) are not
    // part of the body. No emission, no droplets; nothing is left once it has dried. Presentation only.
    // D308-132 step 6: a fourth way out, React (a reaction consumed the zone: it pales out, an optional "React" child folds along the wave).
    // D308-125 5 (c), the enemy ember patch: three ways to go - Dry (its life ran out: End), Splash (washed: Break, and End
    // only while EndOnSplash) and Fade (replaced / cleared: no End, no Break, everything pales out).
    public sealed class Vfx308ZoneBody : MonoBehaviour
    {
        public float Delay, Life = 6f;
        [Tooltip("Seconds after Delay at which the body systems' start alpha drops to LateAlpha (0 = off).")]
        [Min(0f)] public float LateAfter = 3f;
        [Tooltip("D308-126: seconds before the end at which the body pales instead (wins over LateAfter when > 0; off when Life is under twice this).")]
        [Min(0f)] public float LateBefore;
        [Range(0f, 1f)] public float LateAlpha = .35f;
        [Tooltip("Seconds from drying (emission stopped) to the object going.")]
        [Min(0f)] public float DryFade = 1.2f;
        [Tooltip("Splash() also plays End (false: a broken floor leaves only its Break - D308-125 5 (c), no ash after a wash).")]
        public bool EndOnSplash = true;

        static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        ParticleSystem[] _body, _end, _break, _react;
        float _age, _dryAge, _fade;
        bool _started, _late, _dry, _gone, _splashed, _reacted;
        ParticleSystemRenderer[] _fadeR; float[] _fadeBase; MaterialPropertyBlock _mpb;

        public bool Started => _started;
        public bool Late => _late;
        public bool Drying => _dry;
        public bool Gone => _gone;
        public bool Reacted => _reacted;
        public IReadOnlyList<ParticleSystem> Body { get { Collect(); return _body; } }

        void Update() => Tick(Time.deltaTime);

        public void Tick(float dt)
        {
            if (_gone) return;
            Collect();
            _age += dt;
            if (_dry)
            {
                _dryAge += dt;
                if (_fadeR != null)
                {
                    float k = 1f - Mathf.Clamp01(_dryAge / _fade);
                    for (int i = 0; i < _fadeR.Length; i++)
                    {
                        if (_fadeR[i] == null) continue;
                        _fadeR[i].GetPropertyBlock(_mpb); _mpb.SetFloat(OpacityId, _fadeBase[i] * k); _fadeR[i].SetPropertyBlock(_mpb);
                    }
                    if (_dryAge >= _fade) Vanish();
                    return;
                }
                if (_dryAge >= DryFade) Vanish();
                return;
            }
            if (!_started && _age >= Delay)
            {
                _started = true;
                foreach (var ps in _body) if (ps != null) ps.Play(false);
            }
            if (!_started) return;
            float lateAt = LateBefore > 0f ? (Life > 2f * LateBefore ? Life - LateBefore : -1f) : (LateAfter > 0f ? LateAfter : -1f);
            if (!_late && lateAt >= 0f && _age >= Delay + lateAt)
            {
                _late = true;
                foreach (var ps in _body) if (ps != null) FadeStart(ps, LateAlpha);
            }
            if (_age >= Delay + Life) Dry();
        }

        // stop emitting, let what is out fade; before the start (a refused zone) it just goes
        public void Dry()
        {
            if (_gone || _dry) return;
            Collect();
            if (!_started) { Vanish(); return; }
            _dry = true; _dryAge = 0f;
            foreach (var ps in _body) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (!_splashed || EndOnSplash) foreach (var ps in _end) if (ps != null) ps.Play(false);
        }

        public void Splash()
        {
            if (_gone) return;
            Collect();
            if (!_started) { Vanish(); return; }
            if (!_dry) { _splashed = true; foreach (var ps in _break) if (ps != null) ps.Play(false); }
            Dry();
        }

        // D308-132 step 6: a reaction consumed the zone (SpellZoneEnd.Reacted), Splash's sibling. An optional "React" child is turned so
        // its +Z runs along `dir` (the way the wave ran) and played once; then the floor pales out like Fade - no End, no Break, no
        // splash: the reaction's product takes its place. A floor without a React child just pales.
        public void React(Vector3 dir, float seconds = .4f)
        {
            if (_gone) return;
            Collect();
            if (!_started) { Vanish(); return; }
            if (_dry) return;       // already going (dried, washed, replaced): nothing to add
            _reacted = true;
            if (_react.Length > 0)
            {
                var child = transform.Find("React");
                dir.y = 0f;
                if (child != null && dir.sqrMagnitude > 1e-6f) child.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                float last = 0f;
                foreach (var ps in _react) if (ps != null) { ps.Play(false); last = Mathf.Max(last, ps.main.duration + ps.main.startLifetime.constantMax); }
                seconds = Mathf.Max(seconds, last);
            }
            Fade(seconds);
        }

        // go quietly: no End, no Break, everything that is out pales to nothing over `seconds` (a replaced or cleared floor)
        public void Fade(float seconds)
        {
            if (_gone) return;
            Collect();
            if (!_started || seconds <= 0f) { Vanish(); return; }
            if (_dry && _fadeR != null) return;
            _dry = true; _dryAge = 0f; _fade = seconds;
            foreach (var ps in _body) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            _fadeR = GetComponentsInChildren<ParticleSystemRenderer>(true);
            _fadeBase = new float[_fadeR.Length];
            for (int i = 0; i < _fadeR.Length; i++)
            {
                var m = _fadeR[i].sharedMaterial;
                _fadeBase[i] = m != null && m.HasProperty(OpacityId) ? m.GetFloat(OpacityId) : 1f;
            }
            _mpb ??= new MaterialPropertyBlock();
        }

        public void Kill() => Vanish();

        void Vanish()
        {
            if (_gone) return;
            _gone = true;
            gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        void Collect()
        {
            if (_body != null) return;
            var body = new List<ParticleSystem>(); var end = new List<ParticleSystem>(); var brk = new List<ParticleSystem>(); var react = new List<ParticleSystem>();
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                string part = Part(ps.transform);
                (part == "End" ? end : part == "Break" ? brk : part == "React" ? react : body).Add(ps);
            }
            _body = body.ToArray(); _end = end.ToArray(); _break = brk.ToArray(); _react = react.ToArray();
        }

        // the named child (End / Break) a system sits under, or null for the body
        string Part(Transform t)
        {
            for (; t != null && t != transform; t = t.parent)
                if (t.parent == transform && (t.name == "End" || t.name == "Break" || t.name == "React")) return t.name;
            return null;
        }

        static void FadeStart(ParticleSystem ps, float alpha)
        {
            var main = ps.main;
            var c = main.startColor;
            if (c.mode == ParticleSystemGradientMode.Color) { var k = c.color; k.a *= alpha; main.startColor = new ParticleSystem.MinMaxGradient(k); }
            else if (c.mode == ParticleSystemGradientMode.TwoColors) { var a = c.colorMin; var b = c.colorMax; a.a *= alpha; b.a *= alpha; main.startColor = new ParticleSystem.MinMaxGradient(a, b); }
        }
    }
}
