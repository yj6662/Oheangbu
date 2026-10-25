using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>D308-150 V9 step 5a (TEST, presentation only): ink wound round the caster's sleeve - the sibling of the old buff band's brush wrap (AuraBrushWrap308), laid
    /// along a limb of the humanoid the aura is parented to (default the right forearm: lower arm -> hand). A local-space system (+Z = along the limb, a looping arc round
    /// it): each frame after the rig has posed the body it lays the system on the limb and sets the drift so a drop travels the limb's length over its life - a dark
    /// spiral on the light robe that moves with the arm. The great brush's shaft is dark ink already: a dark wrap on it did not read from the shoulder camera.
    /// No humanoid found (or the limb too short): no emission. Reads bone transforms only; writes its own transform and particle system.</summary>
    [DefaultExecutionOrder(1100)]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class Vol2LimbWrap : MonoBehaviour
    {
        public HumanBodyBones From = HumanBodyBones.RightLowerArm;
        public HumanBodyBones To = HumanBodyBones.RightHand;
        [Tooltip("The part of the limb wound (fractions of from -> to).")]
        public float Start = .05f, End = .95f;

        ParticleSystem _ps;
        Transform _a, _b, _root;
        float _life, _next, _speed = -1f;

        /// <summary>Checks: emitting along a limb this frame.</summary>
        public bool Drawing { get; private set; }
        /// <summary>Checks: the humanoid it is laid on (its name), empty when none.</summary>
        public string Bound { get; private set; } = "";

        void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            _life = Mathf.Max(.05f, _ps.main.startLifetime.constantMax);
        }

        void LateUpdate()
        {
            if (_a == null || _b == null || _root != transform.root)
            {
                if (Time.time >= _next) { _next = Time.time + .5f; Bind(); }
            }
            bool on = _a != null && _b != null;
            float len = 0f;
            if (on)
            {
                Vector3 a = _a.position, axis = _b.position - a;
                len = axis.magnitude;
                on = len > .05f;
                if (on)
                {
                    Vector3 dir = axis / len;
                    transform.SetPositionAndRotation(a + dir * len * Start, Quaternion.LookRotation(dir));
                    var parent = transform.parent;
                    transform.localScale = Vector3.one / Mathf.Max(1e-4f, parent != null ? parent.lossyScale.x : 1f);
                }
            }
            Drawing = on;
            var em = _ps.emission;
            if (em.enabled != on) em.enabled = on;
            if (!on) { if (_ps.particleCount > 0) _ps.Clear(false); return; }
            float speed = len * Mathf.Max(.05f, End - Start) / _life;
            if (Mathf.Abs(speed - _speed) > .005f)
            {
                _speed = speed;
                var v = _ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
                v.x = new ParticleSystem.MinMaxCurve(0f); v.y = new ParticleSystem.MinMaxCurve(0f); v.z = new ParticleSystem.MinMaxCurve(speed);
            }
        }

        void Bind()
        {
            // the walking body the player sees: a humanoid that is drawn (an enabled skinned renderer in the hierarchy), nearest the aura's host (the caster)
            _root = transform.root; _a = _b = null;
            Vector3 near = transform.parent != null ? transform.parent.position : transform.position;
            float best = float.MaxValue;
            foreach (var anim in _root.GetComponentsInChildren<Animator>(false))
            {
                if (anim == null || !anim.isActiveAndEnabled || !anim.isHuman || anim.avatar == null) continue;
                bool drawn = false;
                foreach (var r in anim.GetComponentsInChildren<SkinnedMeshRenderer>(false)) if (r.enabled) { drawn = true; break; }
                if (!drawn) continue;
                var a = anim.GetBoneTransform(From); var b = anim.GetBoneTransform(To);
                if (a == null || b == null) continue;
                float d = (anim.transform.position - near).sqrMagnitude;
                if (d < best) { best = d; _a = a; _b = b; Bound = anim.name + "/" + a.name; }
            }
        }
    }
}
