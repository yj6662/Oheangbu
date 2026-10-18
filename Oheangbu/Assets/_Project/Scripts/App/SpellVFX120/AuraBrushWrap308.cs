using Oheangbu.App.World;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // D308-111 (SPEC-SPELL-VFX-OVERHAUL-308 11.55): a buff's ink wound up the great brush. Sits on the "Brush" child of a buff's
    // Aura308 prefab (a local-space system: +Z = up the shaft, a looping arc round it). After the gesture rig has placed the
    // brushes for the frame it lays the system along the great brush the player sees (GripSocket -> TipSocket), and the arc loop
    // plus the drift up the shaft draw a rising spiral that moves with the brush.
    //   * no great brush (old brush, no profile, bristles unbound) or the brush not drawn: no emission, nothing left on screen;
    //   * the part of the shaft nearer the camera than ClipWithin is not used, and a particle that comes within KillWithin (a
    //     swing toward the face) dies at once (D308-109: nothing within 0.45 m of the camera).
    // Presentation only: it reads the brush transforms and renderers and writes its own transform and particle system.
    [DefaultExecutionOrder(1100)]   // after WorldMacroPlayerGestureRig (1000): the brush is where it will be drawn
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class AuraBrushWrap308 : MonoBehaviour
    {
        public float ClipWithin = .5f;
        public float KillWithin = .45f;
        public float MinLength = .1f;

        ParticleSystem _ps;
        ParticleSystem.Particle[] _buf;
        Transform _searchRoot;
        GreatBrushBristles308[] _brushes = new GreatBrushBristles308[0];
        Transform[] _grips = new Transform[0], _tips = new Transform[0];
        Renderer[][] _renderers = new Renderer[0][];
        WorldMacroPlayerGestureRig _rig;
        Camera _cam;
        float _nextLook, _life, _start = -1f, _speed = -1f;

        /// <summary>Checks: emitting along a great brush this frame, and the shaft length used (m).</summary>
        public bool Drawing { get; private set; }
        public float UsedLength { get; private set; }

        void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            _life = Mathf.Max(.05f, _ps.main.startLifetime.constantMax);
            _buf = new ParticleSystem.Particle[Mathf.Max(1, _ps.main.maxParticles)];
        }

        void LateUpdate()
        {
            int pick = Pick();
            float start = 0f, length = 0f;
            bool on = pick >= 0;
            if (_cam == null) _cam = Camera.main;
            if (on)
            {
                Vector3 g = _grips[pick].position, axis = _tips[pick].position - g;
                length = axis.magnitude;
                on = length > .05f;
                if (on)
                {
                    Vector3 dir = axis / length;
                    if (_cam != null) start = ClipStart(g, dir, length, _cam.transform.position, ClipWithin);
                    on = length - start >= MinLength;
                    transform.SetPositionAndRotation(g, Quaternion.LookRotation(dir));
                    var parent = transform.parent;
                    transform.localScale = Vector3.one / Mathf.Max(1e-4f, parent != null ? parent.lossyScale.x : 1f);
                }
            }
            Drawing = on; UsedLength = on ? length - start : 0f;
            var em = _ps.emission;
            if (em.enabled != on) em.enabled = on;
            if (!on) { if (_ps.particleCount > 0) _ps.Clear(false); return; }
            if (Mathf.Abs(start - _start) > .005f) { _start = start; var sh = _ps.shape; sh.position = new Vector3(0f, 0f, start); }
            float speed = (length - start) / _life;
            if (Mathf.Abs(speed - _speed) > .005f)
            {
                _speed = speed;
                var v = _ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
                v.x = new ParticleSystem.MinMaxCurve(0f); v.y = new ParticleSystem.MinMaxCurve(0f); v.z = new ParticleSystem.MinMaxCurve(speed);
            }
            if (_cam != null) KillNear(_cam.transform.position);
        }

        // the shaft distance from the grip past which the shaft stays out of the sphere round the camera (0 = the whole shaft)
        static float ClipStart(Vector3 grip, Vector3 dir, float length, Vector3 cam, float within)
        {
            Vector3 d = grip - cam;
            float b = Vector3.Dot(dir, d), disc = b * b - (d.sqrMagnitude - within * within);
            if (disc <= 0f) return 0f;
            float root = Mathf.Sqrt(disc), s1 = -b - root, s2 = -b + root;
            return s1 < length && s2 > 0f ? Mathf.Clamp(s2, 0f, length) : 0f;
        }

        void KillNear(Vector3 cam)
        {
            int n = _ps.particleCount;
            if (n == 0) return;
            if (_buf.Length < n) _buf = new ParticleSystem.Particle[n];
            n = _ps.GetParticles(_buf, n);
            bool changed = false;
            float k = KillWithin * KillWithin;
            for (int i = 0; i < n; i++)
                if ((transform.TransformPoint(_buf[i].position) - cam).sqrMagnitude < k) { _buf[i].remainingLifetime = 0f; changed = true; }
            if (changed) _ps.SetParticles(_buf, n);
        }

        // the bound great brush that is drawn now (world brush, or the close-up one in the draw mode); the one whose tip is
        // nearest the rig's effect tip when both are drawn. -1 = none.
        int Pick()
        {
            if (_brushes.Length == 0 || _searchRoot != transform.root)
            {
                if (Time.time < _nextLook) return -1;
                _nextLook = Time.time + .25f;
                Bind();
                if (_brushes.Length == 0) return -1;
            }
            if (_rig != null && !_rig.isActiveAndEnabled) return -1;
            int best = -1; float bestD = float.MaxValue;
            Vector3 aim = _rig != null && _rig.EffectTip != null ? _rig.EffectTip.position : Vector3.zero;
            for (int i = 0; i < _brushes.Length; i++)
            {
                if (_brushes[i] == null || !_brushes[i].IsBound || _grips[i] == null || _tips[i] == null || !Shown(_renderers[i])) continue;
                float d = _rig != null && _rig.EffectTip != null ? (_tips[i].position - aim).sqrMagnitude : i;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        void Bind()
        {
            _searchRoot = transform.root;
            // EABuffRuntime parents the buff to the walker body; the rig (camera side) may sit under another root
            _rig = _searchRoot.GetComponentInChildren<WorldMacroPlayerGestureRig>(true);
            if (_rig == null) _rig = FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            _brushes = _rig != null ? _rig.transform.root.GetComponentsInChildren<GreatBrushBristles308>(true) : new GreatBrushBristles308[0];
            _grips = new Transform[_brushes.Length]; _tips = new Transform[_brushes.Length]; _renderers = new Renderer[_brushes.Length][];
            for (int i = 0; i < _brushes.Length; i++)
            {
                _grips[i] = Find(_brushes[i].transform, GreatBrushBristles308.GripName);
                _tips[i] = Find(_brushes[i].transform, GreatBrushBristles308.TipName);
                _renderers[i] = _brushes[i].GetComponentsInChildren<Renderer>(true);
            }
        }

        static bool Shown(Renderer[] renderers)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].enabled && renderers[i].gameObject.activeInHierarchy) return true;
            return false;
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++) { var t = Find(root.GetChild(i), name); if (t != null) return t; }
            return null;
        }
    }
}
