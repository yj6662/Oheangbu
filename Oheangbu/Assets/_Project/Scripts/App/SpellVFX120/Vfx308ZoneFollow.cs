using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>
    /// D308-126: a domain that goes with its caster (궁). Copies the target's position with a short smoothing and keeps the
    /// floor level - never parented to the player (the yaw would swing the local strands and the root scale would multiply
    /// the radius). The logic circle moves every tick, so the picture lags it by about Damp. Stops once the body dries.
    /// </summary>
    public sealed class Vfx308ZoneFollow : MonoBehaviour
    {
        public Transform Target;
        public Vfx308ZoneBody Body;
        [Min(0f)] public float Damp = .05f;

        void LateUpdate()
        {
            if (Target == null || (Body != null && (Body.Drying || Body.Gone))) return;
            Vector3 to = Target.position;
            float k = Damp <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / Damp);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, to, k), Quaternion.identity);
        }
    }
}
