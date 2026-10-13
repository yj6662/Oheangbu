using Oheangbu.Combat;
using Oheangbu.Data.Spell;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using UnityEngine;

namespace Oheangbu.App.World
{
    // SPEC-SPELL-SWING-308 section 4a (D308-80b): the great two-handed brush in the gesture rig. Presentation only.
    //
    // INVARIANT - no profile, no change. Every seam below starts from Great308() / GreatNow308:
    //   * Great308() is null unless Resources "Swing308/GreatBrush308Profile" exists and is valid;
    //   * Brush.Great is only assigned in BindGreat308, after that null check, so without the profile it is null on both brushes;
    //   * GreatNow308 also needs a bound great brush (a GreatBrushBristles308 on the world brush root) and a running SpellSwing308.
    //   With any of them missing the rig runs the code it ran before this stage: old brush, old carry, old cast follow-through.
    //
    // With the great brush:
    //   * outside the draw mode the Animator owns the arms (SpellSwing308.OwnsArms): no carry pose, no reach toward a tip, no
    //     finger overrides, no torso term. The rig only places the brush from the right hand x the current grip, solves the left
    //     arm onto GripSocketL by the left-hand weight, and feeds the bristles with the tip's motion.
    //   * in the draw mode the drawing branch runs as before (the same pointer, ray, depth search and tip correction; recognition
    //     reads none of it). What differs is the model, the close-up placement numbers taken from the great brush profile, the
    //     bristle component, and the left hand on the shaft of the hidden world body.
    //   * the brush is not resized or stretched (BrushSize / BrushScale belong to the 0.9 m brush).
    //   * the close-up cast follow-through and the torso twist of WorldMacroPlayerLean303 are off (the swing is the motion now).
    public sealed partial class WorldMacroPlayerGestureRig
    {
        public const string NearGripName308 = "NearGripSocket308";

        private GreatBrush308Profile _great308;
        [System.NonSerialized] private bool _greatLooked308, _greatBound308, _greatTipHas308;   // see GreatBrushBristles308._bound
        private SpellSwing308 _swing308;
        private Transform _greatLeftSocket308;
        private Vector3 _greatTipLocal308;

        /// <summary>The profile exists and the bound world brush is a great brush (says nothing about the Animator side).</summary>
        public bool GreatBrushBound308 => _greatBound308;
        /// <summary>The great brush presentation is what the rig runs now (profile, great brush and a running SpellSwing308).</summary>
        public bool GreatBrushActive308 => GreatNow308;
        /// <summary>WorldMacroPlayerLean303 asks: the swing is the cast's motion, so there is no cast twist.</summary>
        public bool SwingOwnsCast308 => GreatNow308;
        /// <summary>Diagnostics (AC-5): distance from the left hand's hold point to GripSocketL after the solve; -1 = not solved this frame.</summary>
        public float LeftHandErrorMeters308 { get; private set; } = -1f;
        /// <summary>Diagnostics: the Animator owned the arms in the last evaluated frame.</summary>
        public bool ArmsOwnedByAnimator308 { get; private set; }

        internal PlayerMotor Motor308 => _motor;
        internal DrawingInputController Drawing308 => _drawing;
        internal WorldMacroCombatWalker Walker308 => _walker;

        private bool GreatNow308 => _greatBound308 && _swing308 != null && _swing308.Active;

        private GreatBrush308Profile Great308()
        {
            if (!_greatLooked308) { _greatLooked308 = true; _great308 = Resources.Load<GreatBrush308Profile>(GreatBrush308Profile.ResourcesPath); }
            return _great308 != null && _great308.IsValid ? _great308 : null;
        }

        /// <summary>Editor tools: the profile asset was created, changed or deleted - look again and bind again.</summary>
        public bool RefreshGreatBrush308()
        {
            _greatLooked308 = false; _great308 = null;
            return TryBind();
        }

        // ---------------------------------------------------------------- binding (TryBind)

        // Before the brushes are bound: the close-up brush is held nearer the bristles than GripSocket (0.95 m behind the tip the
        // hand would be out of the picture). The node is made by the install tool next to GripSocket; here it only gets its place.
        private void PlaceNearGrip308()
        {
            NearGripError308 = null;
            var profile = Great308();
            if (profile == null || _nearBrushRoot == null || _nearGripSocket == null || _nearGripSocket.name != NearGripName308) return;
            Transform grip = Find(_nearBrushRoot, GreatBrushBristles308.GripName);
            Transform left = Find(_nearBrushRoot, GreatBrushBristles308.LeftGripName), tip = Find(_nearBrushRoot, GreatBrushBristles308.TipName);
            if (grip == null || !_nearGripSocket.IsChildOf(_nearBrushRoot)) { NearGripError308 = "GripSocket not found under the close-up brush, or the grip node is outside it"; return; }
            // tail -> tip along the handle (the two grip sockets); without the left socket, toward the tip. Brush-sized distances at
            // bind time: world positions are exact enough here (the brush root has unit scale, so world units are metres).
            Vector3 along = left != null ? grip.position - left.position : tip != null ? tip.position - grip.position : Vector3.zero;
            if (along.sqrMagnitude < .00000001f) { NearGripError308 = "the shaft direction could not be measured (GripSocketL / TipSocket)"; return; }
            _nearGripSocket.SetPositionAndRotation(grip.position + along.normalized * profile.NearGripAlongShaft, grip.rotation);
        }

        /// <summary>Diagnostics: why the close-up grip node was not placed at the last bind (null = placed, or no great brush).</summary>
        public string NearGripError308 { get; private set; }
        /// <summary>Diagnostics: the first thing that keeps the great brush from being bound / running (null = it runs).</summary>
        public string GreatBrushWhy308
        {
            get
            {
                if (_great308 == null) return "no profile in Resources (" + GreatBrush308Profile.ResourcesPath + ")";
                if (!_great308.IsValid) return "the profile is not valid (NaN, empty layer name or invalid grip)";
                if (_worldBrush == null) return "the world brush is not bound: " + BindingError;
                if (_worldBrush.Great == null) return "the world brush has no GreatBrushBristles308";
                if (!_worldBrush.Great.IsBound) return "bristles: " + _worldBrush.Great.BindingError;
                if (_swing308 == null) return "no SpellSwing308 on the player";
                if (!_swing308.Active) return "SpellSwing308 is not running (Play only; the Animator needs the brush layer)";
                return null;
            }
        }

        // The 0.9 m brush's size and handle stretch do not apply to a great brush.
        private bool SkipBrushSizing308(Transform root)
            => root != null && Great308() != null && root.GetComponent<GreatBrushBristles308>() != null;

        // After both brushes are bound. Without the profile nothing is assigned and Brush.Great stays null.
        private void BindGreat308()
        {
            _greatBound308 = false; _greatLeftSocket308 = null; _greatTipHas308 = false; LeftHandErrorMeters308 = -1f; ArmsOwnedByAnimator308 = false;
            var profile = Great308();
            if (profile == null) return;
            if (_worldBrush != null) _worldBrush.Great = BindBristles308(_worldBrush, profile);
            if (_nearBrush != null) _nearBrush.Great = BindBristles308(_nearBrush, profile);
            if (_worldBrush == null || _worldBrush.Great == null) return;
            if (!_worldBrush.Great.IsBound) { _worldBrush.Great = null; if (_nearBrush != null) _nearBrush.Great = null; return; }   // a brush that did not bind (missing bone / socket, scale) is not a great brush: the old pass runs (PR #30 review)
            _greatLeftSocket308 = Find(_worldBrush.Root, GreatBrushBristles308.LeftGripName);
            BindNearLeft308();
            _swing308 = GetComponent<SpellSwing308>();
            _greatBound308 = true;
        }

        private static GreatBrushBristles308 BindBristles308(Brush brush, GreatBrush308Profile profile)
        {
            var bristles = brush.Root != null ? brush.Root.GetComponent<GreatBrushBristles308>() : null;
            if (bristles == null) return null;
            bristles.Configure(profile.Bristles);
            if (!bristles.IsBound) bristles.TryBind();   // unbound it stays inert (rest shape, rest tip offset) and says why in BindingError
            bristles.UseGrip(brush.Grip);                // the tip offset is reported from the transform the rig holds the brush by
            return bristles;
        }

        private void ResetGreat308()
        {
            if (_worldBrush != null && _worldBrush.Great != null) _worldBrush.Great.ResetDeformation();
            if (_nearBrush != null && _nearBrush.Great != null) _nearBrush.Great.ResetDeformation();
            _greatTipHas308 = false;
        }

        // ---------------------------------------------------------------- numbers that come from the great brush profile

        // the grip outside the draw mode (blend 0 of the drawing grip): the right hand's current great-brush grip
        private Quaternion CarryGripRotation308(Quaternion legacy)
            => GreatNow308 ? WorldMacroPlayerGestureProfile.SafeRotation(_swing308.CurrentGrip.Rotation) : legacy;
        private Vector3 CarryGripPosition308(Vector3 legacy) => GreatNow308 ? _swing308.CurrentGrip.Position : legacy;
        // the drawing grip (close-up): the gesture profile's own unless the great brush profile overrides it
        private Quaternion DrawGripRotation308(Quaternion legacy)
            => GreatNow308 && _great308.NearGripOverride ? WorldMacroPlayerGestureProfile.SafeRotation(_great308.NearGripRotation) : legacy;
        private Vector3 DrawGripPosition308(Vector3 legacy) => GreatNow308 && _great308.NearGripOverride ? _great308.NearGripPosition : legacy;

        private float NearTipDepth308 => GreatNow308 ? _great308.NearTipDepth : _profile.NearTipDepth;
        private float NearMinimumDepth308 => GreatNow308 ? _great308.NearMinimumDepth : _profile.NearMinimumDepth;
        private float NearMaximumDepth308 => GreatNow308 ? _great308.NearMaximumDepth : _profile.NearMaximumDepth;
        private Vector3 NearShoulderOffset308 => GreatNow308 ? _great308.NearShoulderOffset + NearBreath308() : _profile.NearShoulderOffset;
        // breathing in the close-up (user request 2026-10-13): the shoulder rises and falls, a slower drift sideways. The tip is
        // solved onto the pointer after this, so the stroke itself does not move - only the shaft end and the hands do.
        private Vector3 NearBreath308()
        {
            float a = _great308.NearBreathAmplitude; if (a <= 0f) return Vector3.zero;
            float t = Time.time * _great308.NearBreathHz * 2f * Mathf.PI;
            return new Vector3(Mathf.Sin(t * .5f + 1.3f) * a * .6f, Mathf.Sin(t) * a, Mathf.Sin(t * .5f) * a * .35f);
        }
        private float NearRaiseSeconds308 => GreatNow308 ? _great308.NearRaiseSeconds : _profile.NearRaiseSeconds;
        private Vector3 NearRaiseOffset308 => GreatNow308 ? _great308.NearRaiseOffset : _profile.NearRaiseOffset;

        // ---------------------------------------------------------------- the frame

        // EvaluateGesturePose, right after the animated pose was saved. True = the Animator owns the arms this frame and the rig
        // has done its whole pass (brush, left hand, bristles); the caller returns.
        private bool OwnedPose308(float dt, bool drawing, bool casting, bool harvesting)
        {
            ArmsOwnedByAnimator308 = false;
            if (!GreatNow308 || drawing || casting) { _greatTipHas308 = false; return false; }
            ArmsOwnedByAnimator308 = true;
            // the brush follows the right hand: GripSocket = hand x grip (the clips were solved with this one transform)
            var grip = _swing308.CurrentGrip;
            Quaternion rotation = _world.Hand.rotation * WorldMacroPlayerGestureProfile.SafeRotation(grip.Rotation);
            _worldBrush.Root.rotation = rotation * Quaternion.Inverse(_worldBrush.RootToGripRotation);
            _worldBrush.Root.position += _world.Hand.TransformPoint(grip.Position) - _worldBrush.Grip.position;
            SlingOnBack308(Mathf.SmoothStep(0f, 1f, _swing308.BackBlend));   // sprinting out of combat: hand -> back
            SolveLeftHand308(_swing308.LeftHandOnShaft);

            // bristles trail behind the tip's motion relative to the body (walking straight does not bend them; a swing does)
            Vector3 tip = _worldBrush.Grip.position + _worldBrush.Grip.rotation * _worldBrush.RestTipOffset;
            Vector3 local = _world.Root.InverseTransformPoint(tip);
            Vector3 velocity = _greatTipHas308 && dt > .00001f ? _world.Root.TransformVector((local - _greatTipLocal308) / dt) : Vector3.zero;
            _greatTipLocal308 = local; _greatTipHas308 = true;
            _worldBrush.Great.EvaluateWorld(velocity, dt);
            _worldBrush.Great.ApplyPose();

            SetNearVisible(false);
            _cameraRig?.SetDrawingPresentationActive(false);
            _nearRaise = 0f;
            _effectTip.SetPositionAndRotation(_worldBrush.Tip.position, _worldBrush.Tip.rotation);
            _diagnostics.EffectTipWorld = _effectTip.position;
            _diagnostics.NearTipErrorPixels = -1f;
            _diagnostics.NearShoulderCorrectionMeters = 0f;
            _diagnostics.TipReachable = true;
            _diagnostics.VerticalBlend = 0f; _diagnostics.VerticalActive = false;
            _diagnostics.GripErrorMeters = Vector3.Distance(_world.Hand.TransformPoint(grip.Position), _worldBrush.Grip.position);
            _diagnostics.BrushWorldScaleError = (_worldBrush.Root.lossyScale - Vector3.one).magnitude;
            _diagnostics.DrawingWeight = _drawWeight; _diagnostics.HarvestWeight = _harvestWeight;
            _diagnostics.State = _recoveryRemaining > 0f ? _recoveryState : harvesting ? GestureState.Harvest : GestureState.Carry;
            return true;
        }

        // ---------------------------------------------------------------- the close-up with the great brush (user 2026-10-13)
        // The user's hold (2026-10-13, corrected): the body is turned to the left - RIGHT shoulder ahead, left shoulder back.
        //   right hand = the front hand, it steers and is the hand in the picture: palm and four fingers wrap the shaft from
        //                below, the thumb lies on top;
        //   left hand  = the rear hand at the tail, the axis: back of the hand up (overhand), it stays where it is.
        // The tail (left hand) is the fixed point, the right hand moves the shaft about it and the tip writes. The tip sits
        // exactly on the pointer's ray; the 0.9 m brush's one-arm pen solve is not used. Presentation only: the pointer, the ray
        // and the stroke are what they were.
        public const string NearLeftPrefix308 = "NearL308_";
        private Renderer[] _nearLeft308 = System.Array.Empty<Renderer>();
        private float _nearLift308;
        private Transform _nearShaftPoint308;   // a point on the shaft's centre line (GripSocket of the close-up brush)
        private bool _nearAxes308;
        private sealed class NearFingers308 { public Transform[] Joints = new Transform[12]; public Quaternion[] Rest = new Quaternion[12]; public Transform[] Thumb = new Transform[3]; public Quaternion[] ThumbRest = new Quaternion[3]; public Transform ThumbTip; }
        private NearFingers308 _nearFingersR308, _nearFingersL308;
        private Vector3 _nearFingerR308, _nearDorsalR308, _nearFingerL308, _nearDorsalL308;
        private float _nearKnuckleR308, _nearKnuckleL308;   // wrist -> finger roots, in the hand's local units

        private void BindNearLeft308()
        {
            _nearLeft308 = System.Array.Empty<Renderer>(); _nearAxes308 = false;
            if (_nearRoot == null || _near == null || !_near.Valid) return;
            var found = new System.Collections.Generic.List<Renderer>();
            foreach (var renderer in _nearRoot.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.StartsWith(NearLeftPrefix308, System.StringComparison.Ordinal)) found.Add(renderer);
            _nearLeft308 = found.ToArray();
            _nearShaftPoint308 = _nearBrushRoot != null ? Find(_nearBrushRoot, GreatBrushBristles308.GripName) : null;
            // the hands' own axes, measured from their finger roots (no assumption about the bones' local frames):
            // finger = wrist -> middle finger root, dorsal = the back of the hand
            // the back of the hand from the thumb: right hand = cross(toward the thumb, along the fingers), left hand the other way
            // round. (2026-10-13 Play: taking the sign from the gesture profile's HandDorsalLocal put the palm on the wrong side.)
            if (!HandAxes308(_near.Hand, false, out _nearFingerR308, out _nearDorsalR308)) return;
            _nearKnuckleR308 = Knuckle308(_near.Hand); _nearKnuckleL308 = _near.LeftHand != null ? Knuckle308(_near.LeftHand) : 0f;
            if (_near.LeftHand == null || !HandAxes308(_near.LeftHand, true, out _nearFingerL308, out _nearDorsalL308)) _nearDorsalL308 = Vector3.zero;
            SkinnedMeshRenderer skin = null;
            foreach (var renderer in _nearRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (renderer.sharedMesh != null && renderer.bones.Length == renderer.sharedMesh.bindposes.Length) { skin = renderer; break; }
            _nearFingersR308 = FindFingers308(_near.Hand, "Right", skin); _nearFingersL308 = _near.LeftHand != null ? FindFingers308(_near.LeftHand, "Left", skin) : null;
            _nearAxes308 = true;
        }

        // The straight-finger pose is read from the mesh's bind pose, never from the joints as they are now: the joints carry
        // whatever the last frame (or the last bind, or an animation) left in them, and a curl put on top of that compounds -
        // 2026-10-13: that was the tight fist beside the shaft in Play and the flat hand in the edit-mode preview.
        private static Quaternion BindLocal308(SkinnedMeshRenderer skin, Transform joint, Quaternion fallback)
        {
            if (skin == null || joint == null || joint.parent == null) return fallback;
            var bones = skin.bones; int child = System.Array.IndexOf(bones, joint), parent = System.Array.IndexOf(bones, joint.parent);
            if (child < 0 || parent < 0) return fallback;
            var poses = skin.sharedMesh.bindposes;
            Matrix4x4 local = poses[parent] * poses[child].inverse;   // the joint in its parent's space at bind
            Vector3 forward = local.GetColumn(2), up = local.GetColumn(1);
            return forward.sqrMagnitude > 0f && up.sqrMagnitude > 0f ? Quaternion.LookRotation(forward, up) : fallback;
        }

        private static NearFingers308 FindFingers308(Transform hand, string side, SkinnedMeshRenderer skin)
        {
            var found = new NearFingers308(); string[] names = { "Index", "Middle", "Ring", "Pinky" };
            foreach (Transform child in hand.GetComponentsInChildren<Transform>(true))
            {
                for (int f = 0; f < 4; f++)
                    for (int j = 0; j < 3; j++)
                        if (child.name.EndsWith(side + "Hand" + names[f] + (j + 1), System.StringComparison.Ordinal)) { found.Joints[f * 3 + j] = child; found.Rest[f * 3 + j] = BindLocal308(skin, child, child.localRotation); }
                for (int j = 0; j < 3; j++)
                    if (child.name.EndsWith(side + "HandThumb" + (j + 1), System.StringComparison.Ordinal)) { found.Thumb[j] = child; found.ThumbRest[j] = BindLocal308(skin, child, child.localRotation); }
                if (child.name.EndsWith(side + "HandThumb4", System.StringComparison.Ordinal)) found.ThumbTip = child;
            }
            if (found.ThumbTip == null) found.ThumbTip = found.Thumb[2];
            return found;
        }

        // After the hand is placed: the four fingers close round the shaft (toward the palm), the thumb comes over to the far
        // side of the shaft from the palm. From the bind pose every frame, so nothing accumulates.
        private static void WrapFingers308(NearFingers308 fingers, Vector3 fingerWorld, Vector3 dorsalWorld, Vector3 hold, Vector3 axis, Vector3 curl, Vector3 thumbCurl)
        {
            if (fingers == null) return;
            Vector3 toPalm = Vector3.Cross(fingerWorld, -dorsalWorld).normalized;   // turning about this takes the fingers toward the palm side
            for (int f = 0; f < 4; f++)
                for (int j = 0; j < 3; j++)
                {
                    Transform joint = fingers.Joints[f * 3 + j]; if (joint == null) continue;
                    joint.localRotation = fingers.Rest[f * 3 + j];
                    joint.rotation = Quaternion.AngleAxis(j == 0 ? curl.x : j == 1 ? curl.y : curl.z, toPalm) * joint.rotation;
                }
            if (fingers.ThumbTip == null) return;
            // The thumb lies along the shaft, pointing the way the player looks (user 2026-10-13: "엄지가 플레이어가 바라보는 방향을
            // 향하고 있어야"): the first bone climbs from the thumb root onto the shaft, the next two run along it toward the tip.
            for (int j = 0; j < 3; j++) if (fingers.Thumb[j] != null) fingers.Thumb[j].localRotation = fingers.ThumbRest[j];
            // (user's sketch 2026-10-13, seen from above) the thumb comes up the near side of the shaft and lies over its top,
            // pointing forward and a little toward the fingertips that come over from the far side.
            Vector3 up = -dorsalWorld;
            for (int j = 0; j < 3; j++)
            {
                Transform joint = fingers.Thumb[j], next = j < 2 ? fingers.Thumb[j + 1] : fingers.ThumbTip;
                if (joint == null || next == null || next == joint) continue;
                Vector3 now = next.position - joint.position; if (now.sqrMagnitude < .0000001f) continue;
                Vector3 want = j == 0 ? up * thumbCurl.x + axis * .5f + fingerWorld * .2f
                             : j == 1 ? up * thumbCurl.y + axis * .8f + fingerWorld * .35f
                                      : up * thumbCurl.z + axis * .9f + fingerWorld * .4f;
                joint.rotation = Quaternion.FromToRotation(now.normalized, want.normalized) * joint.rotation;
            }
        }
        // distance from the thumb tip to the line along the shaft through the point over it
        private static float ThumbDistance308(Vector3 tip, Vector3 over, Vector3 axis) => Vector3.Cross(tip - over, axis).magnitude;

        // world distance from the wrist to the root of the middle finger (where the fingers start to bend)
        private static float Knuckle308(Transform hand)
        {
            foreach (Transform child in hand.GetComponentsInChildren<Transform>(true))
                if (child.name.EndsWith("HandMiddle1", System.StringComparison.Ordinal)) return Vector3.Distance(hand.position, child.position);
            return .09f;
        }

        private static bool HandAxes308(Transform hand, bool left, out Vector3 finger, out Vector3 cross)
        {
            finger = cross = Vector3.zero;
            Transform middle = null, index = null, pinky = null, thumb = null;
            foreach (Transform child in hand.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.EndsWith("HandMiddle1", System.StringComparison.Ordinal)) middle = child;
                else if (child.name.EndsWith("HandIndex1", System.StringComparison.Ordinal)) index = child;
                else if (child.name.EndsWith("HandPinky1", System.StringComparison.Ordinal)) pinky = child;
                else if (child.name.EndsWith("HandThumb1", System.StringComparison.Ordinal)) thumb = child;
            }
            if (middle == null || index == null || pinky == null) return false;
            finger = hand.InverseTransformPoint(middle.position).normalized;
            // toward the thumb side: the thumb root, or index over pinky when there is no thumb bone
            Vector3 radial = thumb != null ? hand.InverseTransformPoint(thumb.position) : hand.InverseTransformPoint(index.position) - hand.InverseTransformPoint(pinky.position);
            radial = Vector3.ProjectOnPlane(radial, finger);
            // sign settled by looking (edit-mode preview 2026-10-13): with the other sign the back of the hand lay against the shaft
            cross = (left ? Vector3.Cross(radial, finger) : Vector3.Cross(finger, radial)).normalized;
            // the direction itself from the finger roots (the thumb root sits off the palm's plane and tilted the normal); the thumb only gives the sign
            Vector3 flat = Vector3.ProjectOnPlane(Vector3.Cross(hand.InverseTransformPoint(index.position), hand.InverseTransformPoint(pinky.position)), finger);
            // Right hand: index-root x pinky-root IS the back of the hand (T-pose check: arm along +x, palm down, index forward:
            // (8.6, 0, 2) x (6.9, 0, -2.5) has +y); the left hand is the mirror image, so the other sign. The thumb root is not used
            // for this: it sits under the palm and swung the result by most of a right angle (the cause of three wrong tries).
            if (flat.sqrMagnitude > .0000001f) cross = left ? -flat.normalized : flat.normalized;
            return finger.sqrMagnitude > .5f && cross.sqrMagnitude > .5f;
        }

        private void SetNearLeftVisible308(bool visible)
        {
            // 2026-10-13: the left hand is the rear hand by the body - out of the picture by design (user: "화면에는 오른손이 나옴").
            // Its arm is still solved (the pivot is its hold) but not drawn unless the profile asks: a stray left hand showed at the bottom edge.
            bool show = visible && (GreatNow308 || _nearPreview308) && _great308 != null && _great308.NearTwoHand && _great308.NearShowLeftArm && _nearAxes308;
            for (int i = 0; i < _nearLeft308.Length; i++)
                if (_nearLeft308[i] != null) { _nearLeft308[i].enabled = show; _nearLeft308[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        }

        // the hand's world rotation from where its fingers point and where its back faces
        private static Quaternion HandRotation308(Vector3 fingerWorld, Vector3 dorsalWorld, Vector3 fingerLocal, Vector3 dorsalLocal)
            => Quaternion.LookRotation(fingerWorld, dorsalWorld) * Quaternion.Inverse(Quaternion.LookRotation(fingerLocal, dorsalLocal));

        /// <summary>Diagnostics / preview: where the right hand holds the shaft this frame (world).</summary>
        public Vector3 NearHoldRight308 { get; private set; }
        // Editor preview only (GreatBrush308Build nearpreview): lets the close-up solve run in edit mode, where SpellSwing308 is not active.
        private bool _nearPreview308;

        // A hand closed on a rod. The rod lies across the palm, so the fingers point across the rod and the back of the hand faces
        // away from it - both at right angles to the rod. What is left is the turn about the rod: it is taken from the forearm, so
        // the wrist stays straight (no twist at any brush angle). Right hand: palm up under the rod (thumb side toward the tip);
        // left hand: palm down over it.
        private static void GripOnShaft308(bool left, Vector3 hold, Vector3 axis, Vector3 shoulder, Transform view, float knuckle, GreatBrush308Profile p,
            out Vector3 wrist, out Vector3 finger, out Vector3 dorsal)
        {
            Vector3 up = Vector3.ProjectOnPlane(view.up, axis); if (up.sqrMagnitude < .0001f) up = Vector3.ProjectOnPlane(view.forward, axis); up.Normalize();
            dorsal = left ? up : -up;                                    // left: back of the hand up; right: back of the hand down
            // the fingers: across the rod, away from where the arm comes from
            Vector3 fromArm = Vector3.ProjectOnPlane(hold - shoulder, axis);
            fromArm = Vector3.ProjectOnPlane(fromArm, dorsal);           // in the palm's plane
            Vector3 side = Vector3.Cross(dorsal, axis).normalized;       // the one direction that is across the rod and in the palm's plane
            finger = Vector3.Dot(fromArm, side) >= 0f ? side : -side;
            if (fromArm.sqrMagnitude < .0004f) finger = left ? Vector3.ProjectOnPlane(view.right, axis).normalized : -Vector3.ProjectOnPlane(view.right, axis).normalized;
            finger = Vector3.ProjectOnPlane(finger, dorsal).normalized;
            // the hand may lie a little diagonally on the rod, toward where the forearm comes from: less bend in the wrist
            Vector3 armInPalm = Vector3.ProjectOnPlane(hold - shoulder, dorsal);
            if (armInPalm.sqrMagnitude > .0004f && p.NearWristFollowDegrees > 0f)
                finger = Vector3.RotateTowards(finger, armInPalm.normalized, p.NearWristFollowDegrees * Mathf.Deg2Rad, 0f).normalized;
            // the rod sits in the palm just inside the finger roots; the wrist is back along the fingers and off the rod on the back-of-hand side
            wrist = hold - finger * Mathf.Max(.02f, knuckle - p.NearPalm.x) + dorsal * p.NearPalm.y;
        }

        private bool NearGreat308(Camera camera, Vector2 screen, Vector3 visualVelocity, bool stroking, float dt)
        {
            if (!(GreatNow308 || (_nearPreview308 && _great308 != null)) || !_great308.NearTwoHand || !_nearAxes308 || _nearBrush == null || _nearBrush.Great == null || _near == null || !_near.Valid) return false;
            var p = _great308; Transform view = camera.transform;
            foreach (var pair in _nearCopies) { pair.Target.localPosition = pair.Source.localPosition; pair.Target.localRotation = pair.Source.localRotation; }
            _nearRoot.rotation = view.rotation * Quaternion.Euler(0f, p.NearBodyYawDegrees, 0f);   // turned left: the right shoulder leads

            Ray ray = BrushStrokeFeedAdapter.ScreenPointToRayPrecise308(camera, screen);
            float depth = Mathf.Clamp(p.NearTipDepth, Mathf.Max(camera.nearClipPlane + .04f, p.NearMinimumDepth), Mathf.Max(p.NearMinimumDepth, p.NearMaximumDepth));
            _nearLift308 = Mathf.MoveTowards(_nearLift308, stroking ? 0f : _profile.PenLiftDistance, dt / .06f * Mathf.Max(.001f, _profile.PenLiftDistance));
            Vector3 tip = ray.GetPoint(depth - _nearLift308);

            // the axis: the left hand at the tail, a fixed point by the body (it only breathes)
            Vector3 pivot = view.TransformPoint(p.NearLeftPivot + NearBreath308());
            Vector3 axis = tip - pivot; if (axis.sqrMagnitude < .0001f) return false; axis.Normalize();

            // the brush: shaft along the axis, ring A side up; bristles evaluated for this frame; then the real (bent) tip on the ray
            Vector3 upAcross = Vector3.ProjectOnPlane(view.up, axis).normalized;
            Quaternion root = Quaternion.LookRotation(Vector3.Cross(-upAcross, axis), axis);
            _nearBrush.Great.EvaluateInFrame(visualVelocity, view.rotation, root * _nearBrush.RootToGripRotation, stroking, dt);
            _nearBrush.Root.rotation = root;
            _nearBrush.Great.ApplyPose();
            _nearBrush.Root.position += tip - _nearBrush.Tip.position;

            // ---- the arms (user 2026-10-13, third pass): "어깨를 포함한 팔 전체가 움직이는 게 아니라, 팔꿈치 관절이 펴지고 접히는 정도를
            // 통해 팔의 움직임을" - the shoulders stay where the body is; only the elbows open and close. The right hand holds the
            // shaft at the point nearest its comfortable place (it slides a little along the shaft instead of dragging the arm).
            Vector3 shoulder = view.TransformPoint(p.NearRightShoulder);
            _nearRoot.position += shoulder - _near.Upper.position;
            MeasureLengths(_near);
            float upperLength = Vector3.Distance(_near.Upper.position, _near.Forearm.position), forearmLength = Vector3.Distance(_near.Forearm.position, _near.Hand.position);
            float reach = (upperLength + forearmLength) * .96f;

            // The hands hold the SHAFT, not the line through the tip: the tip is on the bristles, and pressed bristles bend it
            // several centimetres off the shaft's centre line. Measuring the holds from the tip put the hand round a line beside the
            // shaft (2026-10-13: the fist next to the shaft in every screenshot). Everything below is measured on the centre line.
            Vector3 centre = _nearShaftPoint308 != null ? _nearShaftPoint308.position : tip;
            Vector3 tipOnAxis = centre + axis * Vector3.Dot(tip - centre, axis);
            Vector3 comfort = view.TransformPoint(p.NearRightComfort);
            float along = Mathf.Clamp(Vector3.Dot(tipOnAxis - comfort, axis), p.NearRightFromTipRange.x, p.NearRightFromTipRange.y);
            Vector3 holdRight = tipOnAxis - axis * along;
            GripOnShaft308(false, holdRight, axis, shoulder, view, _nearKnuckleR308, p, out Vector3 wrist, out Vector3 fingerR, out Vector3 dorsalR);
            if ((wrist - shoulder).magnitude > reach)
            {
                // out of reach: slide the hold toward the shoulder along the shaft until the arm can close on it
                float nearest = Mathf.Clamp(Vector3.Dot(tipOnAxis - shoulder, axis), p.NearRightFromTipRange.x, p.NearRightFromTipRange.y + .25f);
                holdRight = tipOnAxis - axis * Mathf.Lerp(along, nearest, .85f);
                GripOnShaft308(false, holdRight, axis, shoulder, view, _nearKnuckleR308, p, out wrist, out fingerR, out dorsalR);
            }
            Quaternion hand = HandRotation308(fingerR, dorsalR, _nearFingerR308, p.NearFlipPalm ? -_nearDorsalR308 : _nearDorsalR308);
            Vector3 pole = (shoulder + wrist) * .5f + view.TransformDirection(p.NearRightElbowHint);
            PlayerVisualIK.Solve(_near.Upper, _near.Forearm, _near.Hand, wrist, pole, hand, 1f, 1f);
            WrapFingers308(_nearFingersR308, fingerR, dorsalR, holdRight, axis, p.NearFingerCurlDegrees, p.NearThumbCurlDegrees);
            NearHoldRight308 = holdRight;

            // left hand, the rear hand at the tail (the axis): over the shaft, back of the hand up
            if (_near.LeftUpper != null && _near.LeftForearm != null && _near.LeftHand != null && _nearDorsalL308.sqrMagnitude > .5f)
            {
                Vector3 holdLeft = tipOnAxis - axis * Vector3.Dot(tipOnAxis - pivot, axis);   // the shaft's centre line beside the pivot
                GripOnShaft308(true, holdLeft, axis, _near.LeftUpper.position, view, _nearKnuckleL308, p, out Vector3 wristL, out Vector3 fingerL, out Vector3 dorsalL);
                Quaternion handL = HandRotation308(fingerL, dorsalL, _nearFingerL308, p.NearFlipPalm ? -_nearDorsalL308 : _nearDorsalL308);
                Vector3 poleL = (_near.LeftUpper.position + wristL) * .5f + view.TransformDirection(new Vector3(-.25f, -.3f, -.1f));
                PlayerVisualIK.Solve(_near.LeftUpper, _near.LeftForearm, _near.LeftHand, wristL, poleL, handL, 1f, 1f);
                WrapFingers308(_nearFingersL308, fingerL, dorsalL, holdLeft, axis, p.NearFingerCurlDegrees, p.NearThumbCurlDegrees);
            }

            Vector3 actual = BrushStrokeFeedAdapter.WorldToScreenPrecise308(camera, _nearBrush.Tip.position);
            _diagnostics.NearTipErrorPixels = actual.z > camera.nearClipPlane ? Vector2.Distance(screen, new Vector2(actual.x, actual.y)) : float.PositiveInfinity;
            _diagnostics.NearTipErrorBeforeCorrectionPixels = _diagnostics.NearTipErrorPixels;
            _diagnostics.TipReachable = _diagnostics.NearTipErrorPixels <= 2f;
            _diagnostics.NearShoulderCorrectionMeters = 0f;
            return true;
        }

        // The brush slung across the back: head over the right shoulder, tail by the left hip. The frame is built from the body's
        // own joints (shoulder line, hips -> shoulders), so it leans and turns with the torso in a run or in a seat.
        private void SlingOnBack308(float weight)
        {
            if (weight <= 0f || _great308 == null || _worldBrush == null || _world.Upper == null || _world.LeftUpper == null) return;
            if (_greatHips308 == null) { var animator = GetComponent<Animator>(); _greatHips308 = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null; }
            Vector3 middle = (_world.Upper.position + _world.LeftUpper.position) * .5f;
            Vector3 right = (_world.Upper.position - _world.LeftUpper.position).normalized;
            Vector3 up = _greatHips308 != null ? (middle - _greatHips308.position).normalized : _world.Root.up;
            Vector3 forward = Vector3.Cross(right, up).normalized; right = Vector3.Cross(up, forward);
            if (forward.sqrMagnitude < .5f) return;
            Vector3 d = _great308.BackShaftDirection.sqrMagnitude > .0001f ? _great308.BackShaftDirection.normalized : Vector3.up;
            Vector3 shaft = (right * d.x + up * d.y + forward * d.z).normalized;
            Vector3 x = Vector3.ProjectOnPlane(forward, shaft).normalized;                    // ring A side (root -X) looks away from the back
            Quaternion rootRotation = Quaternion.LookRotation(Vector3.Cross(x, shaft), shaft);
            Vector3 o = _great308.BackSocketOffset;
            Vector3 socket = middle + right * o.x + up * o.y + forward * o.z;
            _worldBrush.Root.rotation = Quaternion.Slerp(_worldBrush.Root.rotation, rootRotation, weight);
            _worldBrush.Root.position += Vector3.Lerp(_worldBrush.Grip.position, socket, weight) - _worldBrush.Grip.position;
        }
        private Transform _greatHips308;

        // In a seat / a vehicle / on a stopped rig the rig returns early: the great brush still has to be somewhere. On the back.
        private void SuspendedBrush308()
        {
            if (!_greatBound308 || _worldBrush == null || _worldBrush.Great == null) return;
            SlingOnBack308(1f);
            _worldBrush.Great.ApplyPose();
            if (_effectTip != null) _effectTip.SetPositionAndRotation(_worldBrush.Tip.position, _worldBrush.Tip.rotation);
            _greatTipHas308 = false;
        }

        // The drawing branch (and the legacy pass), after the world brush was placed.
        private void AfterWorldBrush308()
        {
            if (_worldBrush.Great == null) return;
            if (GreatNow308) SolveLeftHand308(_great308.DrawLeftHandOnShaft * _drawWeight);
            _worldBrush.Great.ApplyPose();
            _greatTipHas308 = false;
        }

        // Two-bone IK of the left arm onto GripSocketL. The clip's own wrist rotation and elbow side are kept: the clips were
        // solved with the hand on the socket, so this only takes out what retargeting and the upper-body mask left over.
        private void SolveLeftHand308(float weight)
        {
            LeftHandErrorMeters308 = -1f;
            if (weight <= 0f || _greatLeftSocket308 == null || _great308 == null
                || _world.LeftUpper == null || _world.LeftForearm == null || _world.LeftHand == null) return;
            Quaternion wrist = _world.LeftHand.rotation;
            Vector3 hold = _world.LeftHand.TransformVector(_great308.LeftHandHoldLocal);   // wrist -> the palm point that sits on the shaft
            Vector3 target = _greatLeftSocket308.position - hold;
            Vector3 pole = _world.LeftForearm.position + _world.Root.TransformDirection(_great308.LeftElbowBias);
            PlayerVisualIK.Solve(_world.LeftUpper, _world.LeftForearm, _world.LeftHand, target, pole, wrist,
                Mathf.Clamp01(weight), _great308.LeftArmMaximumExtension);
            LeftHandErrorMeters308 = Vector3.Distance(_world.LeftHand.position + _world.LeftHand.TransformVector(_great308.LeftHandHoldLocal),
                _greatLeftSocket308.position);
        }
    }
}
