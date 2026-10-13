using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.World
{
    // SPEC-SPELL-SWING-308 section 4a (D308-80i, user 2026-10-14 "붓 자루 끝에 있는 이거, 장식이지? 중력에 영향받도록 해"):
    // the ring, cord and tassel at the tail of the great brush (SM_GreatBrush308 v6: Tail_01..NN, TailEndSocket) hang by their
    // own weight. One particle per joint end on a chain, gravity, a little air drag, kept off the shaft. Tail_01 is the rigid
    // ring; from the cord down the links are short and the skin is shared between neighbours, so it bends softly (user
    // 2026-10-14 "끈부터 술까지는 부드럽게"); a small bend stiffness keeps the tassel from folding like a bead chain.
    //
    // Presentation only: it reads the brush's own transforms and writes three bone rotations. It never sees the pointer, the
    // samples, the power or the swing rule. Unbound (a v5 brush without the bones) it does nothing.
    // Runs after the gesture rig (order 1000) has placed the brush for the frame.
    [DefaultExecutionOrder(1100)]
    public sealed class GreatBrushTail308 : MonoBehaviour
    {
        public const string Prefix = "Tail_", EndName = "TailEndSocket";
        public const int MaximumCount = 16;
        public int Count => _links.Length;

        private struct Link
        {
            public Transform Bone;
            public Quaternion RestLocal;
            public Vector3 AxisLocal;      // unit, in the bone's own frame: toward the next joint (no import axis convention assumed)
            public float Length;           // metres in the world
            public Vector3 Point, Previous; // the far end of this link
        }

        private GreatBrush308Profile.TailSettings _settings = GreatBrush308Profile.TailSettings.Default;
        private Link[] _links = System.Array.Empty<Link>();
        private Transform _handle, _grip;
        private Vector3 _lastAnchor;
        // not serialized: an editor script reload drops the links these vouch for
        [System.NonSerialized] private bool _bound, _tried;

        public bool IsBound => _bound;
        public string BindingError { get; private set; }
        /// <summary>Diagnostics: unit direction from the first joint to the tassel end, in the world.</summary>
        public Vector3 HangDirection => _bound ? (_links[Count - 1].Point - _links[0].Bone.position).normalized : Vector3.zero;
        /// <summary>Diagnostics: metres from the shaft's centre line to the nearest chain point alongside the shaft (-1 = none alongside).</summary>
        public float NearestToShaft { get; private set; } = -1f;

        public void Configure(in GreatBrush308Profile.TailSettings settings)
            => _settings = settings.IsValid ? settings : GreatBrush308Profile.TailSettings.Default;

        private void OnEnable()
        {
            if (!_bound && !_tried) TryBind();
            ResetChain();
        }

        public bool TryBind()
        {
            _tried = true; _bound = false; BindingError = null;
            var found = new Transform[MaximumCount]; Transform end = null, grip = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == EndName) end = t;
                else if (t.name == GreatBrushBristles308.GripName) grip = t;
                else if (t.name.StartsWith(Prefix, System.StringComparison.Ordinal) && int.TryParse(t.name.Substring(Prefix.Length), out int n) && n >= 1 && n <= MaximumCount) found[n - 1] = t;
            }
            int count = 0; while (count < MaximumCount && found[count] != null) count++;
            if (count == 0) { BindingError = Prefix + "01 is missing (a brush before v6)"; return false; }
            var links = new Link[count]; for (int i = 0; i < count; i++) links[i].Bone = found[i];
            if (end == null) { BindingError = EndName + " is missing"; return false; }
            if (grip == null) { BindingError = GreatBrushBristles308.GripName + " is missing"; return false; }
            for (int i = 0; i < count; i++)
            {
                Transform bone = links[i].Bone, next = i + 1 < count ? links[i + 1].Bone : end;
                Vector3 to = next.position - bone.position; float length = to.magnitude;
                if (length < 1e-4f) { BindingError = bone.name + " has no length"; return false; }
                links[i].RestLocal = bone.localRotation; links[i].AxisLocal = Quaternion.Inverse(bone.rotation) * (to / length); links[i].Length = length;
            }
            _links = links; _handle = links[0].Bone.parent; _grip = grip;
            if (_handle == null) { BindingError = "Tail_01 has no parent"; return false; }
            _bound = true; return true;
        }

        /// <summary>Back to the modelled shape (straight out of the tail), at rest.</summary>
        public void ResetChain()
        {
            if (!_bound) return;
            for (int i = 0; i < Count; i++) _links[i].Bone.localRotation = _links[i].RestLocal;
            for (int i = 0; i < Count; i++)
            {
                Vector3 point = _links[i].Bone.position + _links[i].Bone.rotation * _links[i].AxisLocal * _links[i].Length;
                _links[i].Point = point; _links[i].Previous = point;
            }
            _lastAnchor = _links[0].Bone.position;
        }

        private void LateUpdate()
        {
            if (!_bound) return;
            Step(Time.deltaTime);
        }

        /// <summary>Tools (edit mode stills, checks): lets the chain fall for a while where the brush is now.</summary>
        public void Settle(float seconds)
        {
            if (!_bound) return;
            for (float t = 0f; t < seconds; t += 1f / 60f) Step(1f / 60f);
        }

        private void Step(float deltaTime)
        {
            if (deltaTime <= 0f) { Apply(); return; }
            Vector3 anchor = _links[0].Bone.position;
            Vector3 jump = anchor - _lastAnchor;
            if (jump.magnitude > _settings.TeleportMeters)   // a warp (respawn, the brush changing hands across the body): carry the chain along, no whip
                for (int i = 0; i < Count; i++) { _links[i].Point += jump; _links[i].Previous += jump; }
            _lastAnchor = anchor;

            float dt = Mathf.Min(deltaTime, _settings.MaximumStep); int steps = Mathf.Max(1, _settings.Substeps); float h = dt / steps;
            Vector3 gravity = Physics.gravity * _settings.GravityScale; float keep = Mathf.Clamp01(1f - _settings.Drag * h);
            float stiff = Mathf.Clamp01(_settings.BendStiffness), stiffTassel = Mathf.Clamp01(_settings.TasselBendStiffness); int tasselFrom = Mathf.Max(2, _settings.TasselFirstLink - 1); int passes = Mathf.Max(1, _settings.Iterations);
            Vector3 shaftFrom = _handle.position, shaft = _grip.position - shaftFrom;
            shaft = shaft.sqrMagnitude > 1e-8f ? shaft.normalized : _handle.up;
            NearestToShaft = -1f;
            for (int s = 0; s < steps; s++)
            {
                for (int i = 0; i < Count; i++)
                {
                    Vector3 point = _links[i].Point, velocity = (point - _links[i].Previous) * keep;
                    _links[i].Previous = point; _links[i].Point = point + velocity + gravity * (h * h);
                }
                for (int pass = 0; pass < passes; pass++)
                {
                    // lengths: both ends of a link give way (the eye on the tail cap does not). Dragging only the lower end froze folds in.
                    for (int i = 0; i < Count; i++)
                    {
                        Vector3 from = i == 0 ? anchor : _links[i - 1].Point, to = _links[i].Point - from; float length = to.magnitude;
                        if (length < 1e-6f) { to = gravity.sqrMagnitude > 0f ? gravity.normalized : Vector3.down; length = 1f; _links[i].Point = from + to * _links[i].Length; continue; }
                        Vector3 fix = to / length * (length - _links[i].Length);
                        if (i == 0) _links[i].Point -= fix; else { _links[i - 1].Point += fix * .5f; _links[i].Point -= fix * .5f; }
                    }
                    // bending: two links in a row resist folding (the distance across them is kept toward their summed length). The same
                    // on both ends, so it adds no motion of its own - a one-sided "lean toward the link above" kept the chain squirming.
                    for (int i = 2; i < Count; i++)
                    {
                        float k = i >= tasselFrom ? stiffTassel : stiff; if (k <= 0f) continue;
                        Vector3 across = _links[i].Point - _links[i - 2].Point; float length = across.magnitude, rest = _links[i - 1].Length + _links[i].Length;
                        if (length < 1e-6f || length >= rest) continue;
                        Vector3 fix = across / length * ((length - rest) * k * .5f);
                        _links[i - 2].Point += fix; _links[i].Point -= fix;
                    }
                    // off the shaft: alongside it the ornament lies against the bamboo, not inside it
                    for (int i = 0; i < Count; i++)
                    {
                        Vector3 point = _links[i].Point; float along = Vector3.Dot(point - shaftFrom, shaft);
                        if (along <= 0f) continue;
                        Vector3 radial = point - shaftFrom - shaft * along; float distance = radial.magnitude, clear = _settings.ShaftClearance;
                        if (distance < clear)
                        {
                            if (distance < 1e-5f) { radial = Vector3.ProjectOnPlane(gravity, shaft); if (radial.sqrMagnitude < 1e-8f) radial = Vector3.ProjectOnPlane(Vector3.right, shaft); distance = radial.magnitude; }
                            _links[i].Point = shaftFrom + shaft * along + radial / distance * clear; distance = clear;
                        }
                        if (pass == passes - 1 && s == steps - 1) NearestToShaft = NearestToShaft < 0f ? distance : Mathf.Min(NearestToShaft, distance);
                    }
                }
            }
            Apply();
        }

        private void Apply()
        {
            Vector3 joint = _links[0].Bone.position;
            for (int i = 0; i < Count; i++)
            {
                Transform bone = _links[i].Bone;
                Quaternion rest = (bone.parent != null ? bone.parent.rotation : Quaternion.identity) * _links[i].RestLocal;
                Vector3 direction = _links[i].Point - joint;
                if (direction.sqrMagnitude > 1e-10f) bone.rotation = Quaternion.FromToRotation(rest * _links[i].AxisLocal, direction.normalized) * rest;
                joint = _links[i].Point;
            }
        }
    }
}
