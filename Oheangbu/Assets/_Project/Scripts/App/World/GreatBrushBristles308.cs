using System.Collections.Generic;
using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.World
{
    // SPEC-SPELL-SWING-308 sections 4a / 6 (D308-80b): the bristles of the great brush (SM_GreatBrush308 v5).
    // One centre chain (Bristle_01..08) and six ring chains (BristleRing_A..F_01..04, each rooted on Bristle_01) follow a lagging
    // spring opposite to the tip's motion; the ring chains have their own stiffness so the strands separate. Contact (a stroke on
    // the glyph plane) drives the BristlePress blendshape and adds a bend; speed and press drive BristleSplay.
    // Presentation only: this component reads a velocity and a contact flag and writes bone local rotations and two blendshape
    // weights. It never sees the pointer, the samples or the power. The tip is not moved here: TipSocket hangs on Bristle_08, and
    // the gesture rig places the brush from TipOffsetInGripLocal so the deformed tip sits on the pointer (the handle moves instead).
    // Only local rotations are written (no world positions: at kilometre coordinates a float step is a fraction of a millimetre).
    // The old BrushBristleRig (six bones, one chain) is not used on this brush.
    [DisallowMultipleComponent]
    public sealed class GreatBrushBristles308 : MonoBehaviour
    {
        public const string GripName = "GripSocket", LeftGripName = "GripSocketL", TipName = "TipSocket";
        public const string CentrePrefix = "Bristle_", RingPrefix = "BristleRing_", SplayShape = "BristleSplay", PressShape = "BristlePress";
        public const int CentreCount = 8, RingCount = 6, RingLength = 4;

        private struct Joint
        {
            public Transform Bone;
            public Quaternion RestLocal, RestInRoot;   // RestInRoot: the bone's rest rotation in the brush root's frame
            public Vector3 RestPosition, RestScale;
            public float Share;                        // this joint's part of the chain's whole bend (sums to 1 over the chain)
        }

        private sealed class Chain
        {
            public Joint[] Joints;
            public Vector2 Bend, Velocity;             // radians, in the (AxisX, AxisY) plane across the shaft
            public float Frequency, Damping, Scale;
            public Vector3 Outward;                    // root frame, unit: away from the shaft (ring chains only)
        }

        private struct Shape { public SkinnedMeshRenderer Renderer; public int Index; }

        private GreatBrush308Profile.BristleSettings _settings = GreatBrush308Profile.BristleSettings.Default;
        private Chain _centre;
        private Chain[] _rings = System.Array.Empty<Chain>();
        private Transform _grip, _tip;
        private Shape[] _splayShapes = System.Array.Empty<Shape>(), _pressShapes = System.Array.Empty<Shape>();
        private Matrix4x4 _centreParentInRoot, _tipLocal;
        private Vector3 _gripPositionInRoot, _shaft, _axisX, _axisY, _restTipOffset;
        private Quaternion _gripRotationInRoot = Quaternion.identity;
        private Vector2 _lastDirection = Vector2.up;
        private float _press, _splay;
        private Vector3 _tipOffset;
        // not serialized: an editor script reload keeps private bools but drops the unserializable chains they vouch for
        [System.NonSerialized] private bool _bound, _tried;

        public bool IsBound => _bound;
        public string BindingError { get; private set; }
        public int BoundRingChains => _rings.Length;
        public bool HasSplayShape => _splayShapes.Length > 0;
        public bool HasPressShape => _pressShapes.Length > 0;
        /// <summary>Diagnostics: the centre chain's whole bend (degrees), press and splay (0..1) of the last evaluation.</summary>
        public float BendDegrees => _centre != null ? _centre.Bend.magnitude * Mathf.Rad2Deg : 0f;
        public float Press => _press;
        public float Splay => _splay;
        /// <summary>Unit axes across the shaft in the brush root's frame (the model's own left-right and press directions, taken
        /// from where ring chains A and B sit; no import axis convention is assumed).</summary>
        public Vector3 ShaftAxisInRoot => _shaft;
        public Vector3 AxisXInRoot => _axisX;
        public Vector3 AxisYInRoot => _axisY;
        /// <summary>GripSocket's pose in the brush root's frame at rest (tools: the grip transform is computed from it).</summary>
        public Vector3 GripPositionInRoot => _gripPositionInRoot;
        public Quaternion GripRotationInRoot => _gripRotationInRoot;

        public void Configure(in GreatBrush308Profile.BristleSettings settings)
        {
            _settings = settings.IsValid ? settings : GreatBrush308Profile.BristleSettings.Default;
            if (_bound) ApplySettings();
        }

        private void OnEnable() { if (!_bound) TryBind(); }
        private void OnDisable() { if (_bound) ResetDeformation(); }

        public bool TryBind()
        {
            _bound = false; _tried = true; BindingError = null;
            Transform root = transform;
            _grip = Find(root, GripName); _tip = Find(root, TipName);
            if (_grip == null || _tip == null) { BindingError = "GripSocket / TipSocket not found under the brush root."; return false; }
            if ((root.lossyScale - Vector3.one).sqrMagnitude > .000001f) { BindingError = "The brush root must have unit world scale."; return false; }
            var centre = new Transform[CentreCount];
            for (int i = 0; i < CentreCount; i++)
            {
                centre[i] = Find(root, CentrePrefix + (i + 1).ToString("00"));
                if (centre[i] == null) { BindingError = "Centre chain bone missing: " + CentrePrefix + (i + 1).ToString("00"); return false; }
                if (i > 0 && centre[i].parent != centre[i - 1]) { BindingError = "Centre chain is not one parent-child line at " + centre[i].name; return false; }
            }
            if (!InRoot(centre[0].parent, root, out _centreParentInRoot)) { BindingError = "Bristle_01 is not under the brush root."; return false; }
            if (!InRoot(_grip, root, out Matrix4x4 gripInRoot)) { BindingError = "GripSocket is not under the brush root."; return false; }
            _gripPositionInRoot = gripInRoot.GetColumn(3); _gripRotationInRoot = Rotation(gripInRoot);
            if (!InRoot(_tip, centre[CentreCount - 1], out _tipLocal))
            {
                // TipSocket does not hang on the last bristle bone: it would not follow the bend. Refuse, so the fault is visible.
                BindingError = "TipSocket must be a descendant of " + centre[CentreCount - 1].name + "."; return false;
            }

            // the shaft axis: tail -> tip along the handle (the two grip sockets), else grip -> tip at rest
            Transform leftGrip = Find(root, LeftGripName);
            Vector3 tipInRoot = (RootMatrix(_tip, root)).GetColumn(3);
            Vector3 shaft = leftGrip != null ? _gripPositionInRoot - (Vector3)RootMatrix(leftGrip, root).GetColumn(3) : tipInRoot - _gripPositionInRoot;
            if (shaft.sqrMagnitude < .000001f) shaft = tipInRoot - _gripPositionInRoot;
            if (shaft.sqrMagnitude < .000001f) { BindingError = "The shaft axis could not be measured."; return false; }
            _shaft = shaft.normalized;

            var rings = new List<Chain>(RingCount);
            Vector3 radialA = Vector3.zero, radialB = Vector3.zero;
            for (int r = 0; r < RingCount; r++)
            {
                var bones = new Transform[RingLength]; bool whole = true;
                for (int j = 0; j < RingLength; j++)
                {
                    bones[j] = Find(root, RingPrefix + (char)('A' + r) + "_" + (j + 1).ToString("00"));
                    if (bones[j] == null || (j > 0 && bones[j].parent != bones[j - 1])) { whole = false; break; }
                }
                if (!whole) continue;   // a missing ring chain is tolerated: the centre chain still works
                Vector3 at = RootMatrix(bones[0], root).GetColumn(3);
                Vector3 radial = Vector3.ProjectOnPlane(at - _gripPositionInRoot, _shaft);
                if (r == 0) radialA = radial; else if (r == 1) radialB = radial;
                rings.Add(new Chain { Joints = Joints(bones, root), Outward = radial.sqrMagnitude > .00000001f ? radial.normalized : Vector3.zero });
            }
            // the model's own cross axes: X = toward ring A, Y = the side ring B is on (build_v5: ring k sits at 60 degrees * k,
            // and BristlePress flattens along this Y). Without the rings: any pair across the shaft.
            _axisX = radialA.sqrMagnitude > .00000001f ? radialA.normalized : Vector3.ProjectOnPlane(Mathf.Abs(_shaft.x) < .9f ? Vector3.right : Vector3.forward, _shaft).normalized;
            Vector3 y = Vector3.ProjectOnPlane(radialB, _shaft); y -= _axisX * Vector3.Dot(y, _axisX);
            _axisY = y.sqrMagnitude > .00000001f ? y.normalized : Vector3.Cross(_shaft, _axisX).normalized;

            _centre = new Chain { Joints = Joints(centre, root) };
            _rings = rings.ToArray();
            ApplySettings();

            var splay = new List<Shape>(1); var press = new List<Shape>(1);
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh; if (mesh == null) continue;
                int s = mesh.GetBlendShapeIndex(SplayShape), p = mesh.GetBlendShapeIndex(PressShape);
                if (s >= 0) splay.Add(new Shape { Renderer = renderer, Index = s });
                if (p >= 0) press.Add(new Shape { Renderer = renderer, Index = p });
            }
            _splayShapes = splay.ToArray(); _pressShapes = press.ToArray();

            _bound = true;
            _restTipOffset = TipOffset(false);
            ResetDeformation();
            return true;
        }

        /// <summary>
        /// The transform the rig holds the brush by (Brush.Grip): the tip offset is reported in its frame. GripSocket by default;
        /// the close-up brush is held by a node nearer the bristles. Must sit rigidly under the brush root (not on a bristle bone).
        /// </summary>
        public bool UseGrip(Transform grip)
        {
            if (!_bound || grip == null || !InRoot(grip, transform, out Matrix4x4 gripInRoot)) return false;
            _grip = grip;
            _gripPositionInRoot = gripInRoot.GetColumn(3); _gripRotationInRoot = Rotation(gripInRoot);
            _restTipOffset = TipOffset(false); _tipOffset = TipOffset(true);
            return true;
        }

        private static Quaternion Rotation(Matrix4x4 m)
        {
            Vector3 forward = m.GetColumn(2), up = m.GetColumn(1);
            return forward.sqrMagnitude > 0f && up.sqrMagnitude > 0f ? Quaternion.LookRotation(forward.normalized, up.normalized) : Quaternion.identity;
        }

        private void ApplySettings()
        {
            if (_centre == null) return;
            Shares(_centre.Joints, _settings.TipBias);
            _centre.Frequency = _settings.CentreFrequency; _centre.Damping = _settings.CentreDamping; _centre.Scale = 1f;
            for (int r = 0; r < _rings.Length; r++)
            {
                Shares(_rings[r].Joints, _settings.TipBias);
                // a fixed pattern per chain (no random state): neighbouring chains get clearly different stiffness
                float pattern = Mathf.Sin((r + 1) * 2.399963f);
                _rings[r].Frequency = Mathf.Max(.1f, _settings.RingFrequency * (1f + _settings.RingStiffnessSpread * pattern));
                _rings[r].Damping = _settings.RingDamping;
                _rings[r].Scale = _settings.RingBendScale * (1f - .25f * _settings.RingStiffnessSpread * pattern);
            }
        }

        private static void Shares(Joint[] joints, float bias)
        {
            float sum = 0f;
            for (int i = 0; i < joints.Length; i++) { joints[i].Share = Mathf.Pow((i + 1f) / joints.Length, Mathf.Max(.01f, bias) - 1f); sum += joints[i].Share; }
            for (int i = 0; i < joints.Length; i++) joints[i].Share /= Mathf.Max(.0001f, sum);
        }

        private static Joint[] Joints(Transform[] bones, Transform root)
        {
            var joints = new Joint[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                Quaternion inRoot = Quaternion.identity;
                for (Transform t = bones[i]; t != null && t != root; t = t.parent) inRoot = t.localRotation * inRoot;
                joints[i] = new Joint
                {
                    Bone = bones[i], RestLocal = bones[i].localRotation, RestInRoot = inRoot,
                    RestPosition = bones[i].localPosition, RestScale = bones[i].localScale
                };
            }
            return joints;
        }

        /// <summary>
        /// Same contract as BrushBristleRig.EvaluateInFrame: the velocity is in the reference frame (x right, y up; the close-up
        /// passes half screen heights per second), the grip rotation is the one this frame's arm solve will use. Evaluate once per
        /// shown frame; the bones are written by ApplyPose after the rig has placed the brush.
        /// </summary>
        public void EvaluateInFrame(Vector3 velocityInReference, Quaternion referenceWorldRotation, Quaternion desiredGripWorldRotation,
            bool contact, float deltaTime)
        {
            if (!Ready()) return;
            Quaternion brush = desiredGripWorldRotation * Quaternion.Inverse(_gripRotationInRoot);
            Vector3 inRoot = Quaternion.Inverse(brush) * (referenceWorldRotation * velocityInReference);
            Advance(inRoot / _settings.ViewSpeedForFullBend, contact, contact ? 1f : _settings.HoverBendShare, deltaTime);
        }

        /// <summary>Outside the close-up: the tip's velocity in world axes (m/s, relative to the body), no glyph plane.</summary>
        public void EvaluateWorld(Vector3 tipVelocityWorld, float deltaTime)
        {
            if (!Ready()) return;
            Vector3 inRoot = Quaternion.Inverse(transform.rotation) * tipVelocityWorld;
            Advance(inRoot / _settings.WorldSpeedForFullBend, false, _settings.AirBendShare, deltaTime);
        }

        private bool Ready()
        {
            if (!_bound && !_tried) TryBind();
            return _bound;
        }

        // drive: velocity in the root frame, already divided by the speed that gives the whole bend
        private void Advance(Vector3 drive, bool contact, float share, float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) dt = 0f;
            if (dt > _settings.ResetAfterGap) { ResetState(); return; }
            if (float.IsNaN(drive.x) || float.IsNaN(drive.y) || float.IsNaN(drive.z) || float.IsInfinity(drive.x) || float.IsInfinity(drive.y) || float.IsInfinity(drive.z)) drive = Vector3.zero;
            var lateral = new Vector2(Vector3.Dot(drive, _axisX), Vector3.Dot(drive, _axisY));
            float speed = Mathf.Clamp01(lateral.magnitude);
            if (lateral.sqrMagnitude > .0004f) _lastDirection = lateral.normalized;

            // press: rises at the start of a stroke, deeper the slower the pointer moves (a mouse has no pressure)
            float pressTarget = contact ? Mathf.Lerp(_settings.PressDeep, _settings.PressLight, speed) : 0f;
            float pressTime = pressTarget > _press ? _settings.PressRiseSeconds : _settings.PressReleaseSeconds;
            _press = Mathf.MoveTowards(_press, pressTarget, dt / Mathf.Max(.005f, pressTime));
            float splayTarget = Mathf.Clamp01(speed * share * _settings.SplayBySpeed + _press * _settings.SplayByPress);
            _splay = Mathf.Lerp(_splay, splayTarget, 1f - Mathf.Exp(-_settings.SplayResponse * dt));

            // the bristles trail behind the motion; a pressed tip also stays bent the way it was last dragged
            float maximum = Mathf.Clamp(_settings.MaximumBendDegrees, 0f, 60f) * Mathf.Deg2Rad;
            Vector2 target = -(speed > 0f ? lateral / Mathf.Max(lateral.magnitude, .0001f) : Vector2.zero) * (speed * share * maximum)
                - _lastDirection * (_press * _settings.PressBendDegrees * Mathf.Deg2Rad);
            target = Vector2.ClampMagnitude(target, maximum);

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / (1f / 120f)), 1, 8);
            float h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                Spring(_centre, target, maximum, h);
                for (int r = 0; r < _rings.Length; r++) Spring(_rings[r], target * _rings[r].Scale, maximum * 1.25f, h);
            }
            _tipOffset = TipOffset(true);
        }

        private static void Spring(Chain chain, Vector2 target, float maximum, float h)
        {
            float w = chain.Frequency * 2f * Mathf.PI;
            chain.Velocity += (w * w * (target - chain.Bend) - 2f * chain.Damping * w * chain.Velocity) * h;   // semi-implicit Euler
            chain.Bend += chain.Velocity * h;
            if (chain.Bend.sqrMagnitude > maximum * maximum)
            {
                chain.Bend = chain.Bend.normalized * maximum;
                Vector2 outward = chain.Bend / Mathf.Max(maximum, .0001f);
                float along = Vector2.Dot(chain.Velocity, outward);
                if (along > 0f) chain.Velocity -= outward * along;   // no energy stored against the limit
            }
        }

        // The rotation (root frame) that tips the shaft toward `bend` by its length (radians).
        private Quaternion BendRotation(Vector2 bend, float share)
        {
            float angle = bend.magnitude * share;
            if (angle < .00001f) return Quaternion.identity;
            Vector3 toward = _axisX * bend.x + _axisY * bend.y;
            return Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.Cross(_shaft, toward).normalized);
        }

        private Quaternion JointLocal(in Joint joint, Quaternion deltaInRoot)
            => joint.RestLocal * (Quaternion.Inverse(joint.RestInRoot) * deltaInRoot * joint.RestInRoot);

        /// <summary>
        /// Grip-local offset from GripSocket to the tip for the evaluated bend: the rig solves the arm with it so the deformed
        /// tip lands on the pointer. Computed from rest data and the bend state only (the bones are not read).
        /// </summary>
        public Vector3 TipOffsetInGripLocal(Vector3 fallback) => _bound ? _tipOffset : fallback;
        public Vector3 RestTipOffsetInGripLocal => _restTipOffset;

        private Vector3 TipOffset(bool deformed)
        {
            Matrix4x4 m = _centreParentInRoot;
            var joints = _centre.Joints;
            for (int i = 0; i < joints.Length; i++)
            {
                Quaternion local = deformed ? JointLocal(joints[i], BendRotation(_centre.Bend, joints[i].Share)) : joints[i].RestLocal;
                m *= Matrix4x4.TRS(joints[i].RestPosition, local, joints[i].RestScale);
            }
            Vector3 tip = (m * _tipLocal).GetColumn(3);
            return Quaternion.Inverse(_gripRotationInRoot) * (tip - _gripPositionInRoot);
        }

        /// <summary>After the rig placed the brush for the frame: write the evaluated bend to the bones and the two shapes.</summary>
        public bool ApplyPose()
        {
            if (!_bound) return false;
            var joints = _centre.Joints;
            Quaternion first = Quaternion.identity;
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i].Bone == null) return false;
                Quaternion delta = BendRotation(_centre.Bend, joints[i].Share);
                if (i == 0) first = delta;
                joints[i].Bone.localRotation = JointLocal(joints[i], delta);
            }
            Quaternion undoFirst = Quaternion.Inverse(first);   // ring chains hang on Bristle_01: they bend on their own, not twice
            for (int r = 0; r < _rings.Length; r++)
            {
                var chain = _rings[r]; var ring = chain.Joints;
                Vector3 tangent = Vector3.Cross(_shaft, chain.Outward);
                for (int j = 0; j < ring.Length; j++)
                {
                    if (ring[j].Bone == null) return false;
                    Quaternion delta = BendRotation(chain.Bend, ring[j].Share);
                    if (tangent.sqrMagnitude > .000001f && _splay > 0f)
                        delta = Quaternion.AngleAxis(_splay * _settings.RingFlareDegrees * ring[j].Share, tangent) * delta;
                    if (j == 0) delta = undoFirst * delta;
                    ring[j].Bone.localRotation = JointLocal(ring[j], delta);
                }
            }
            for (int i = 0; i < _splayShapes.Length; i++)
                if (_splayShapes[i].Renderer != null) _splayShapes[i].Renderer.SetBlendShapeWeight(_splayShapes[i].Index, _splay * 100f);
            for (int i = 0; i < _pressShapes.Length; i++)
                if (_pressShapes[i].Renderer != null) _pressShapes[i].Renderer.SetBlendShapeWeight(_pressShapes[i].Index, _press * 100f);
            return true;
        }

        private void ResetState()
        {
            if (_centre == null) return;
            _centre.Bend = _centre.Velocity = Vector2.zero;
            for (int r = 0; r < _rings.Length; r++) _rings[r].Bend = _rings[r].Velocity = Vector2.zero;
            _press = _splay = 0f; _lastDirection = Vector2.up;
            _tipOffset = _restTipOffset;
        }

        /// <summary>Back to the authored rest shape (bones and shapes are written at once).</summary>
        public void ResetDeformation()
        {
            if (!_bound) return;
            ResetState();
            ApplyPose();
        }

        private static Matrix4x4 RootMatrix(Transform node, Transform root)
        {
            InRoot(node, root, out Matrix4x4 m);
            return m;
        }

        // node's local-to-`root` matrix through the local TRS of the chain between them (no world positions involved)
        private static bool InRoot(Transform node, Transform root, out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.identity;
            for (Transform t = node; t != root; t = t.parent)
            {
                if (t == null) { matrix = Matrix4x4.identity; return false; }
                matrix = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * matrix;
            }
            return true;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
