using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // #308 D308-132 step 6: the wildfire is born at the radius its material had and grows to x1.5 in .4 s ("the new perimeter").
    // The root scale is lerped; layers that must keep their particle size use ParticleSystemScalingMode.Shape, so only their ring
    // widens. Presentation only; the rule's radius is already x1.5 from the first instant.
    public sealed class Vfx308ReactGrow : MonoBehaviour
    {
        public Vector3 From = Vector3.one, To = Vector3.one;
        [Min(0f)] public float Seconds = .4f;

        float _age;

        public float Age => _age;
        public bool Done => _age >= Seconds;

        void OnEnable() => Apply();

        void Update() => Tick(Time.deltaTime);

        public void Tick(float dt)
        {
            if (Done) return;
            _age += Mathf.Max(0f, dt);
            Apply();
        }

        void Apply()
        {
            float k = Seconds <= 0f ? 1f : Mathf.Clamp01(_age / Seconds);
            k = k * k * (3f - 2f * k);   // smoothstep: it settles, it does not stop dead
            transform.localScale = Vector3.Lerp(From, To, k);
        }
    }
}
