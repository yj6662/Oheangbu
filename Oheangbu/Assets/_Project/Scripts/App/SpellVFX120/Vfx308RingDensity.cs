using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>
    /// D308-126: a layer built around a 1 m rim keeps its rate and cap per metre of rim. The presenter scales a floor by the
    /// circle's radius, and Shape scaling widens the emitter only, so without this a 3 m domain would spread a 1 m count thin.
    /// Apply once on a fresh instance: rate and cap times the radius, halved under HalfBelow, off under OffBelow (strands
    /// on a small circle would stand right beside the player).
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(ParticleSystem))]
    public sealed class Vfx308RingDensity : MonoBehaviour
    {
        [Min(0f)] public float HalfBelow = 2f;
        [Min(0f)] public float OffBelow;

        bool _applied;

        public void Apply(float radius)
        {
            if (_applied) return;
            _applied = true;
            var ps = GetComponent<ParticleSystem>();
            if (radius < OffBelow) { var off = ps.emission; off.enabled = false; return; }
            float k = Mathf.Max(radius, .01f) * (radius < HalfBelow ? .5f : 1f);
            var em = ps.emission; em.rateOverTimeMultiplier *= k;
            var main = ps.main; main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(main.maxParticles * Mathf.Max(radius, 1f)));
        }
    }
}
