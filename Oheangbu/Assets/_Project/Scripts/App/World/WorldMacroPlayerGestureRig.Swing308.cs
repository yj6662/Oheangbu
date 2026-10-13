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
        private Vector3 NearShoulderOffset308 => GreatNow308 ? _great308.NearShoulderOffset : _profile.NearShoulderOffset;
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
