using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 D308-127 debate 15 (TEST): the flail's chain hangs and whips. The head (build_heads308.py) is rigid parts, no skin
    // (PROD-RIG: machines are rigid parts): Flail_01..NN (the links) and Flail_Rod nested parent -> child, each origin on its
    // upper joint, FlailEndSocket at the rod's far end. One particle per link end on a chain: gravity, air drag, a little bend
    // stiffness, a heavy rod (its end weighs RodMass), kept off the shaft. The solver is GreatBrushTail308's, copied (debate 15:
    // the tassel has no numeric regression check, so it is not shared until one exists), with a mass per point.
    // The strike pull (Fable): for a moment after a strike every point is drawn to the brush axis carried straight on past the
    // ferrule (where a swung flail flies out), weight 1 on the strike frame, held PullHold, gone over PullFade; while pulled a
    // point keeps the anchor's velocity, so the chain whips on when it is let go.
    // Presentation only: it writes the links' rotations. The judgement is the rule's cone; the chain never moves it.
    // Runs after the brush is placed (rig 1000, tail 1100) and before the afterimage samples the rod (1150).
    [DefaultExecutionOrder(1120)]
    public sealed class FlailChain308 : MonoBehaviour
    {
        public const string Prefix = "Flail_", RodName = "Flail_Rod", EndName = "FlailEndSocket";
        public const int MaximumCount = 16;

        [SerializeField] private float _gravityScale = 1f;
        [SerializeField, Min(0f)] private float _drag = 1.2f;
        [SerializeField, Range(1, 8)] private int _substeps = 4;
        [SerializeField, Range(1, 12)] private int _iterations = 6;
        [SerializeField, Min(.001f)] private float _maximumStep = 1f / 30f;
        [SerializeField, Range(0f, 1f)] private float _bendStiffness = .12f;
        [Tooltip("The rod end's mass against a link end's (debate 15: x3).")]
        [SerializeField, Min(1f)] private float _rodMass = 3f;
        [SerializeField, Min(.01f)] private float _teleportMeters = 1.5f;
        [Tooltip("m: the chain lies against the shaft at this distance from its centre line, never inside it.")]
        [SerializeField, Min(0f)] private float _shaftClearance = .03f;
        [SerializeField, Min(0f)] private float _shaftLength = 1.1f;
        [SerializeField, Min(0f)] private float _pullHold = .05f;
        [SerializeField, Min(.01f)] private float _pullFade = .25f;

        private struct Link
        {
            public Transform Bone;
            public Quaternion RestLocal;
            public Vector3 AxisLocal;
            public float Length, InverseMass;
            public Vector3 Point, Previous;
        }

        private Link[] _links = System.Array.Empty<Link>();
        private Vector3 _lastAnchor;
        private float _strikeAt = float.NegativeInfinity;
        [System.NonSerialized] private bool _bound, _tried, _simulating;
        private GreatBrushWeapon308 _head;

        public bool IsBound => _bound;
        public int Count => _links.Length;
        public string BindingError { get; private set; }
        public float StrikeAt => _strikeAt;
        /// <summary>Diagnostics: the rod end in the world (zero when unbound).</summary>
        public Vector3 EndPoint => _bound && Count > 0 ? _links[Count - 1].Point : Vector3.zero;
        /// <summary>Diagnostics: unit direction from the first joint to the rod end.</summary>
        public Vector3 HangDirection => _bound && Count > 0 ? (_links[Count - 1].Point - _links[0].Bone.position).normalized : Vector3.zero;
        /// <summary>Diagnostics: the chain's summed length (m).</summary>
        public float TotalLength { get { float sum = 0f; for (int i = 0; i < Count; i++) sum += _links[i].Length; return sum; } }
        /// <summary>Diagnostics: metres from the shaft's centre line to the nearest point alongside it (-1 = none alongside).</summary>
        public float NearestToShaft { get; private set; } = -1f;
        /// <summary>The pull weight at `now` (1 on the strike, 0 when let go).</summary>
        public float PullWeight(float now)
        {
            float t = now - _strikeAt;
            if (t < 0f || float.IsNaN(t)) return 0f;
            if (t <= _pullHold) return 1f;
            return Mathf.Clamp01(1f - (t - _pullHold) / _pullFade);
        }

        private void OnEnable() { _simulating = false; }

        public void Strike(float now) => _strikeAt = now;

        public bool TryBind()
        {
            _tried = true; _bound = false; BindingError = null;
            var found = new Transform[MaximumCount]; Transform rod = null, end = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == EndName) end = t;
                else if (t.name == RodName) rod = t;
                else if (t.name.StartsWith(Prefix, System.StringComparison.Ordinal) && int.TryParse(t.name.Substring(Prefix.Length), out int n) && n >= 1 && n <= MaximumCount) found[n - 1] = t;
            }
            int count = 0; while (count < MaximumCount && found[count] != null) count++;
            if (count == 0) { BindingError = Prefix + "01 is missing (a flail before debate 15)"; return false; }
            if (rod == null) { BindingError = RodName + " is missing"; return false; }
            if (end == null) { BindingError = EndName + " is missing"; return false; }
            var links = new Link[count + 1];
            for (int i = 0; i < count; i++) links[i].Bone = found[i];
            links[count].Bone = rod;
            for (int i = 0; i <= count; i++)
            {
                Transform bone = links[i].Bone, next = i < count ? links[i + 1].Bone : end;
                Vector3 to = next.position - bone.position; float length = to.magnitude;
                if (length < 1e-4f) { BindingError = bone.name + " has no length"; return false; }
                links[i].RestLocal = bone.localRotation; links[i].AxisLocal = Quaternion.Inverse(bone.rotation) * (to / length); links[i].Length = length;
                links[i].InverseMass = i == count ? 1f / _rodMass : 1f;
            }
            _links = links; _head = GetComponentInParent<GreatBrushWeapon308>();
            _bound = true; return true;
        }

        /// <summary>Back to the modelled shape (straight along the shaft), at rest.</summary>
        public void ResetChain()
        {
            if (!_bound) return;
            for (int i = 0; i < Count; i++) _links[i].Bone.localRotation = _links[i].RestLocal;
            // a hair of bend across the chain: a dead straight chain held upright balances against gravity and never falls
            Vector3 across = Vector3.zero;
            for (int i = 0; i < Count; i++)
            {
                Vector3 axis = _links[i].Bone.rotation * _links[i].AxisLocal;
                if (i == 0)
                {
                    // whichever of the bone's axes lies most across the chain (the links' own +Z runs along it)
                    Transform bone = _links[i].Bone;
                    across = Vector3.ProjectOnPlane(bone.right, axis);
                    if (across.sqrMagnitude < .25f) across = Vector3.ProjectOnPlane(bone.up, axis);
                    if (across.sqrMagnitude < .25f) across = Vector3.ProjectOnPlane(bone.forward, axis);
                    across = across.normalized;
                }
                Vector3 point = _links[i].Bone.position + axis * _links[i].Length + across * (.002f * (i + 1));
                _links[i].Point = point; _links[i].Previous = point;
            }
            _lastAnchor = _links[0].Bone.position;
        }

        private void LateUpdate() => Step(Time.time, Time.deltaTime);

        /// <summary>Tools (edit mode stills, checks): lets the chain fall for a while where the brush is now.</summary>
        public void Settle(float seconds, float now = 0f)
        {
            for (float t = 0f; t < seconds; t += 1f / 60f) Step(now + t, 1f / 60f);
        }

        /// <summary>Advance to `now` by `deltaTime` (checks call this with their own clock).</summary>
        public void Step(float now, float deltaTime)
        {
            if (!_bound && !_tried) { _head = GetComponentInParent<GreatBrushWeapon308>(); if (Grown() >= .999f) TryBind(); }
            if (!_bound) return;
            // growing out of the ferrule or sinking back: the chain is the modelled straight shape, scaled with the head
            if (Grown() < .999f)
            {
                if (_simulating) for (int i = 0; i < Count; i++) _links[i].Bone.localRotation = _links[i].RestLocal;
                _simulating = false; return;
            }
            if (!_simulating) { ResetChain(); _simulating = true; }
            if (deltaTime <= 0f) { Apply(); return; }
            Vector3 anchor = _links[0].Bone.position;
            Vector3 jump = anchor - _lastAnchor;
            if (jump.magnitude > _teleportMeters)
                for (int i = 0; i < Count; i++) { _links[i].Point += jump; _links[i].Previous += jump; }
            Vector3 anchorStart = jump.magnitude > _teleportMeters ? anchor : _lastAnchor;
            _lastAnchor = anchor;

            float dt = Mathf.Min(deltaTime, _maximumStep); int steps = Mathf.Max(1, _substeps); float h = dt / steps;
            Vector3 gravity = Physics.gravity * _gravityScale; float keep = Mathf.Clamp01(1f - _drag * h);
            Transform root = _head != null ? _head.transform : transform;
            Vector3 axis = root.up, shaft = -axis;   // the head's +Y runs on from the ferrule; the shaft runs back from it
            float pull = PullWeight(now);
            NearestToShaft = -1f;
            for (int s = 0; s < steps; s++)
            {
                Vector3 anchorNow = Vector3.Lerp(anchorStart, anchor, (s + 1f) / steps), anchorStep = (anchor - anchorStart) / steps;
                for (int i = 0; i < Count; i++)
                {
                    Vector3 point = _links[i].Point, velocity = (point - _links[i].Previous) * keep;
                    _links[i].Previous = point; _links[i].Point = point + velocity + gravity * (h * h);
                }
                if (pull > 0f)
                {
                    float along = 0f;
                    for (int i = 0; i < Count; i++)
                    {
                        along += _links[i].Length;
                        Vector3 target = anchorNow + axis * along, before = _links[i].Point;
                        _links[i].Point = Vector3.Lerp(before, target, pull);
                        // pulled points move with the brush: their velocity becomes the anchor's (the whip comes on release)
                        _links[i].Previous = Vector3.Lerp(_links[i].Previous, _links[i].Point - anchorStep, pull);
                    }
                }
                for (int pass = 0; pass < _iterations; pass++)
                {
                    for (int i = 0; i < Count; i++)
                    {
                        Vector3 from = i == 0 ? anchorNow : _links[i - 1].Point, to = _links[i].Point - from; float length = to.magnitude;
                        if (length < 1e-6f) { _links[i].Point = from + (gravity.sqrMagnitude > 0f ? gravity.normalized : Vector3.down) * _links[i].Length; continue; }
                        Vector3 fix = to / length * (length - _links[i].Length);
                        float wa = i == 0 ? 0f : _links[i - 1].InverseMass, wb = _links[i].InverseMass, sum = wa + wb;
                        if (sum <= 0f) continue;
                        if (i > 0) _links[i - 1].Point += fix * (wa / sum);
                        _links[i].Point -= fix * (wb / sum);
                    }
                    if (_bendStiffness > 0f)
                        for (int i = 2; i < Count; i++)
                        {
                            Vector3 across = _links[i].Point - _links[i - 2].Point; float length = across.magnitude, rest = _links[i - 1].Length + _links[i].Length;
                            if (length < 1e-6f || length >= rest) continue;
                            Vector3 fix = across / length * ((length - rest) * _bendStiffness * .5f);
                            _links[i - 2].Point += fix; _links[i].Point -= fix;
                        }
                    for (int i = 0; i < Count; i++)
                    {
                        Vector3 point = _links[i].Point; float along = Vector3.Dot(point - anchorNow, shaft);
                        if (along <= 0f || along > _shaftLength) continue;
                        Vector3 radial = point - anchorNow - shaft * along; float distance = radial.magnitude;
                        if (distance < _shaftClearance)
                        {
                            if (distance < 1e-5f) { radial = Vector3.ProjectOnPlane(gravity, shaft); if (radial.sqrMagnitude < 1e-8f) radial = Vector3.ProjectOnPlane(Vector3.right, shaft); distance = radial.magnitude; }
                            _links[i].Point = anchorNow + shaft * along + radial / distance * _shaftClearance; distance = _shaftClearance;
                        }
                        if (pass == _iterations - 1 && s == steps - 1) NearestToShaft = NearestToShaft < 0f ? distance : Mathf.Min(NearestToShaft, distance);
                    }
                }
            }
            Apply();
        }

        private float Grown() => _head != null ? _head.Grown : 1f;

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
