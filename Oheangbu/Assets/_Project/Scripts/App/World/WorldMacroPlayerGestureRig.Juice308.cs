using Oheangbu.Data.World;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 player juice (SPEC-ANIM-JUICE-308) [TEST]: the rig's share. Presentation only.
    //   · The numbers live in PlayerJuice308ProfileSO, the curves in PlayerJuice308Curves, the timing in PlayerJuice308Solver.
    //     This file only hands the rig's own events to the solver and puts the solver's frame where the rig already has a
    //     free choice: the close-up shaft axis (tip fixed on the pointer), the bristle splay, the throw path, the call stroke's
    //     path past its end and its arm weight, and the camera reaction (through CameraRigController, render-time only).
    //   · No profile = none of this runs and the pose is today's. One exception: a recognised letter whose cast was refused
    //     no longer plays the success throw (B-1) - that works without a profile.
    //   · Nothing here reads or writes the recogniser's samples, the draw mode, the pointer or the ink. The camera reaction is
    //     0 while the draw mode is on or a live stroke is attached to the camera (D308-21).
    //   · Stroke anchors: the rig anchors a stroke on last frame's real transforms. A shaft lean left over from the stroke
    //     before is taken out of that anchor (JuiceCleanAnchors308), so a lean never becomes the pivot of the next stroke.
    //   · The preview seam (GestureInput308) lets the edit-mode driver (Juice308Build) feed draw state and pointer instead of the
    //     live input owners. It is off unless the driver switches it on.
    //   · The user's ReducedMotion preference is read from the GameplayRuntimeStateSO the motor carries (UserSettingsService
    //     pushes it there; PauseCoordinator may swap the object, so it is read where it is used and never cached). No global
    //     accessor is used.
    //   · The drawing input raises ModeEntered / StrokeStarted / StrokeEnded / ... inside its own Update, StrokeStarted before
    //     that frame's first sample is stored. A fault in the juice must never cut that Update short (recognition is
    //     untouchable), so every handler that runs inside those events is fenced: a fault is logged once and switches the
    //     juice off for this rig (JuiceFault308).
    //   · 3rd revision (D308-24): (answer 24) a parry cast gets no camera kick. The kind comes from the wiring's read-only
    //     re-broadcast CastAccepted (SpellCast.Kind), reached through the walker the rig already holds; it fires inside the
    //     letter broadcast, before Committed - the order the refusal uses. The handler stores one bool. The throw, the twist
    //     and the brush do not read it. (answer 23) the profile's Camera.MarksFollow is told to the camera owner with every
    //     offset, so the HUD's lock-on marks can follow the reaction (CameraRigController.MarkFollow308.cs).
    // No static field. No coroutine. No Time.timeScale.
    public sealed partial class WorldMacroPlayerGestureRig
    {
        /// <summary>Preview seam: what EvaluateGesturePose reads from the input owners, supplied by the preview driver.</summary>
        public struct GestureInput308
        {
            public bool Drawing, Stroking, HasPointer;
            public Camera Camera;
            public Vector2 Screen;
        }

        [SerializeField, Tooltip("#308 juice profile. Empty = Resources \"Juice308/PlayerJuice308Profile\"; none = no juice.")]
        private PlayerJuice308ProfileSO _juiceProfile308;

        private PlayerJuice308Solver _juice308;
        private PlayerJuice308ProfileSO _juiceResolved308;
        private bool _juiceLooked308, _juiceReduced308;
        private JuiceFrame308 _juiceFrame308;
        private DrawingInputController _juiceDrawing308;
        private BrushStrokeFeedAdapter _juiceFeed308;
        private WorldMacroPlayerGetUpCamera303 _juiceGetUp308;
        private bool _juiceForcedOff308, _juiceFaulted308;   // preview driver: no profile at all / a handler faulted: the juice is off for this rig
        private int _juiceReducedForced308 = -1;             // preview driver / tests: -1 = read the runtime state, 0 / 1 = forced
        private bool _juiceRejected308;                      // B-1: the commit that is being dispatched was refused by the wiring
        private bool _juiceLeanApplied308;                   // a shaft lean was on the close-up arm in the last evaluated frame
        private Vector3 _juiceWristDelta308, _juiceGripDelta308;   // that lean's displacement of wrist / grasp, view-local
        private Vector3 _juiceCameraPushed308;
        private bool _juiceCastBeat308;
        private float _juiceCastSeconds308;
        private Vector2 _juiceCastVia308, _juiceCastDirection308;
        private bool _juiceInputOn308;
        private GestureInput308 _juiceInput308;
        private CombatLoopWiring _juiceWiring308;            // #308 3차: the wiring whose CastAccepted tells a parry cast apart (walker.Wiring)
        private bool _juiceParry308;                         // the cast accepted for the letter being committed is a parry

        /// <summary>The profile in use (serialized slot, else Resources, else none).</summary>
        public PlayerJuice308ProfileSO JuiceProfile308
        {
            get
            {
                if (_juiceForcedOff308 || _juiceFaulted308) return null;
                if (_juiceProfile308 != null) return _juiceProfile308;
                if (!_juiceLooked308) { _juiceLooked308 = true; _juiceResolved308 = Resources.Load<PlayerJuice308ProfileSO>(PlayerJuice308ProfileSO.ResourcesPath); }
                return _juiceResolved308;
            }
        }
        /// <summary>This frame's additions (all zero without a profile).</summary>
        public JuiceFrame308 JuiceFrame308 => _juiceFrame308;
        /// <summary>B-1: the letter of the commit being dispatched was recognised but its cast was refused (cleared when the next draw mode begins).</summary>
        public bool CommitRejected308 => _juiceRejected308 && JuiceSuppressOnReject308;
        /// <summary>B-2: scale of the torso twist that follows the running throw (1 without juice).</summary>
        public float CastTwistScale308 => _juiceCastBeat308 && _juice308 != null ? _juice308.CastTwistScale : 1f;
        /// <summary>C-4: scale the car's visual settle uses (0 without juice).</summary>
        public float JuiceCallScale308 => JuiceSolver308() != null ? _juice308.CallScale : 0f;
        /// <summary>Diagnostics (Juice308Build): shaft lean applied to the close-up arm this frame (degrees) and its headroom factor.</summary>
        public float JuiceShaftLeanDegrees308 { get; private set; }
        public float JuiceHeadroom308 { get; private set; }
        public PlayerJuice308Solver JuiceSolverForChecks308 => JuiceSolver308();
        /// <summary>Diagnostics (3rd revision): the parry tell is wired (walker -> wiring), and commits that were seen as a parry cast.</summary>
        public bool JuiceParryTapWired308 => _juiceWiring308 != null;
        public int JuiceParryCommits308 { get; private set; }

        private bool JuiceSuppressOnReject308 { get { var p = JuiceProfile308; return p == null || p.Reject.SuppressOnReject; } }

        private PlayerJuice308Solver JuiceSolver308()
        {
            var profile = JuiceProfile308;
            if (profile == null) { _juice308 = null; return null; }
            if (_juice308 == null || _juice308.Profile != profile)
            {
                _juice308 = new PlayerJuice308Solver();
                _juice308.Configure(profile);
                _juice308.SetReducedMotion(_juiceReduced308);
            }
            // every event and every evaluated frame comes through here: the preference is current when a reaction starts
            bool reduced = JuiceReducedMotionNow308();
            if (reduced != _juiceReduced308) { _juiceReduced308 = reduced; _juice308.SetReducedMotion(reduced); }
            return _juice308;
        }

        // ReducedMotion as the game knows it right now: the runtime state object on the motor (a plain field read, no allocation).
        // No motor / no state object = off, as before the settings existed.
        private bool JuiceReducedMotionNow308()
        {
            if (_juiceReducedForced308 >= 0) return _juiceReducedForced308 > 0;
            return _motor != null && _motor.RuntimeState != null && _motor.RuntimeState.ReducedMotion;
        }

        // A fault inside one of the juice's event handlers: logged once, and the juice is off for this rig from then on.
        private void JuiceFault308(System.Exception e)
        {
            if (!_juiceFaulted308) Debug.LogException(e, this);
            _juiceFaulted308 = true; _juice308 = null; _juiceFrame308 = default; _juiceCastBeat308 = false; _juiceLeanApplied308 = false;
        }
        /// <summary>Diagnostics: a juice handler faulted and the juice switched itself off (must stay false).</summary>
        public bool JuiceFaulted308 => _juiceFaulted308;

        /// <summary>Preview driver / tests: use this profile (null = back to the serialized slot / Resources).</summary>
        public void SetJuiceProfile308(PlayerJuice308ProfileSO profile)
        {
            _juiceProfile308 = profile; _juiceLooked308 = false; _juiceResolved308 = null; _juice308 = null;
            JuiceSuspend308();
        }

        /// <summary>Preview driver / tests: force the ReducedMotion reading (null = the motor's runtime state object again).</summary>
        public void SetJuiceReducedMotion308(bool? reducedMotion)
        {
            _juiceReducedForced308 = reducedMotion.HasValue ? (reducedMotion.Value ? 1 : 0) : -1;
            JuiceSolver308();
        }

        /// <summary>Preview driver only: behave as if there were no profile at all (slot empty and no Resources asset). false = normal.</summary>
        public void SetJuiceForcedOff308(bool off)
        {
            _juiceForcedOff308 = off; _juice308 = null;
            JuiceSuspend308();
        }

        // ---------------------------------------------------------------- events

        private void SubscribeJuice308()
        {
            UnsubscribeJuice308();
            _juiceDrawing308 = _subscribedDrawing;
            if (_juiceDrawing308 != null)
            {
                _juiceDrawing308.ModeEntered += OnJuiceModeEntered308;
                _juiceDrawing308.StrokeStarted += OnJuiceStrokeStarted308;
                _juiceDrawing308.StrokeEnded += OnJuiceStrokeEnded308;
                _juiceDrawing308.LetterInterrupted += OnJuiceDrawingEnded308;
                _juiceDrawing308.ModeExited += OnJuiceDrawingEnded308;
            }
            _juiceFeed308 = _brushFeed;
            if (_juiceFeed308 != null) _juiceFeed308.CastRejected += OnJuiceCastRejected308;
            JuiceEnsureCastTap308();
        }

        private void UnsubscribeJuice308()
        {
            if (_juiceDrawing308 != null)
            {
                _juiceDrawing308.ModeEntered -= OnJuiceModeEntered308;
                _juiceDrawing308.StrokeStarted -= OnJuiceStrokeStarted308;
                _juiceDrawing308.StrokeEnded -= OnJuiceStrokeEnded308;
                _juiceDrawing308.LetterInterrupted -= OnJuiceDrawingEnded308;
                _juiceDrawing308.ModeExited -= OnJuiceDrawingEnded308;
                _juiceDrawing308 = null;
            }
            if (_juiceFeed308 != null) { _juiceFeed308.CastRejected -= OnJuiceCastRejected308; _juiceFeed308 = null; }
            if (!ReferenceEquals(_juiceWiring308, null)) { _juiceWiring308.CastAccepted -= OnJuiceCastAccepted308; _juiceWiring308 = null; }
        }

        // #308 3차 (D308-24 answer 24): the wiring is reached through the walker. The walker may be wired after the rig subscribed to
        // the drawing input, so this is looked at again whenever a draw mode begins (before any letter of that mode is cast).
        private void JuiceEnsureCastTap308()
        {
            CombatLoopWiring wiring = _walker != null ? _walker.Wiring : null;
            if (ReferenceEquals(wiring, _juiceWiring308)) return;
            if (!ReferenceEquals(_juiceWiring308, null)) _juiceWiring308.CastAccepted -= OnJuiceCastAccepted308;
            _juiceWiring308 = wiring;
            if (wiring != null) wiring.CastAccepted += OnJuiceCastAccepted308;
        }
        // Runs inside the wiring's dispatch of the letter (a gameplay event): one comparison and one store - it cannot fail there.
        private void OnJuiceCastAccepted308(SpellCast cast, Vector3 origin, Vector3 forward) { _juiceParry308 = cast.Kind == SpellKind.Parry; }

        // The four handlers below run inside DrawingInputController's own events (its Update). Each is fenced: see the header.
        private void OnJuiceModeEntered308()
        {
            _juiceRejected308 = false;   // a refusal never reaches past its own commit
            _juiceParry308 = false;      // nor does the kind of the cast before
            try
            {
                JuiceEnsureCastTap308();
                JuiceSolver308()?.ModeEntered();
                JuicePushCamera308(Vector3.zero);   // D rule 1: the reaction ends on the frame the draw mode begins
            }
            catch (System.Exception e) { JuiceFault308(e); }
        }
        private void OnJuiceStrokeStarted308() { try { JuiceSolver308()?.StrokeStarted(); } catch (System.Exception e) { JuiceFault308(e); } }
        private void OnJuiceStrokeEnded308() { try { JuiceSolver308()?.StrokeEnded(); } catch (System.Exception e) { JuiceFault308(e); } }
        private void OnJuiceDrawingEnded308() { try { _juice308?.DrawingEnded(); } catch (System.Exception e) { JuiceFault308(e); } }
        // the wiring refuses inside the letter broadcast, which runs before Committed (DrawingInputController.Commit)
        private void OnJuiceCastRejected308() { _juiceRejected308 = true; }

        // OnCommitted: true when this recognised letter must not play the success throw
        private bool JuiceCommitRejected308() => CommitRejected308;

        // OnCommitted, after the rig decided about its own throw. cast = recognised and not refused.
        private void JuiceCommitted308(bool cast)
        {
            bool parry = _juiceParry308; _juiceParry308 = false;   // read once: the kind belongs to this commit only
            try
            {
                var solver = JuiceSolver308();
                if (solver == null) return;
                solver.DrawingEnded();
                if (!cast) return;
                if (parry) JuiceParryCommits308++;
                float power = _brushFeed != null ? _brushFeed.LastLetterPower308 : 0f;   // the flash's own power proxy, read only
                // #308 3차 (D308-24 answer 24): a parry cast gets no camera kick unless the profile says so (the solver decides, from data)
                solver.CastKick(power, _profile != null ? _profile.CastFlickViewport.x - _lastNearViewport.x : 0f, parry);
            }
            catch (System.Exception e) { JuiceFault308(e); }   // runs inside the input owner's Committed event: never cut it short
        }

        // BeginCast: seconds the camera rig keeps the close-up. Off = the rig's own value.
        private float JuiceBeginCast308(Vector2 via, Vector2 direction, float fallbackSeconds)
        {
            _juiceCastBeat308 = false; _juiceCastSeconds308 = 0f;
            try
            {
                var solver = JuiceSolver308();
                if (solver == null) return fallbackSeconds;
                float seconds = solver.BeginCast(_brushFeed != null ? _brushFeed.LastLetterPower308 : 0f);
                if (!solver.CastBeat || seconds <= 0f) return fallbackSeconds;
                _juiceCastBeat308 = true; _juiceCastSeconds308 = seconds;
                _juiceCastVia308 = via; _juiceCastDirection308 = direction;
                return seconds + solver.Profile.CastBeat.CloseupMargin;
            }
            catch (System.Exception e) { JuiceFault308(e); return fallbackSeconds; }   // inside the Committed event too: the rig's own throw stays
        }

        private void JuiceEndCast308() { _juiceCastBeat308 = false; _juice308?.EndCast(); }

        // seconds the close-up arm is shown for the running throw
        private float JuiceCastSeconds308(float fallbackSeconds) => _juiceCastBeat308 ? _juiceCastSeconds308 : fallbackSeconds;

        /// <summary>B-2: delay of the torso twist after the commit (WorldMacroPlayerLean303 asks in its LateUpdate).</summary>
        public float CastTwistDelay308(float fallbackSeconds)
            => _juiceCastBeat308 && _juice308 != null && _juice308.Profile.CastBeat.CastDelayFollowsHold ? _juiceCastSeconds308 : fallbackSeconds;

        // the cast block of EvaluateGesturePose: pull back - throw - hold - lower. Off = the three values stay as computed.
        private void JuiceCastPath308(ref Vector2 point, ref float castDrop, ref float reach)
        {
            if (!_juiceCastBeat308 || _juice308 == null) return;
            _juice308.EvaluateCast(_castTime, out float along, out float over, out float drop);
            var beat = _juice308.Profile.CastBeat;
            Vector2 p = _castFrom + (_juiceCastVia308 - _castFrom) * along + _juiceCastDirection308 * over;
            point = new Vector2(Mathf.Clamp(p.x, beat.ViewportMin, beat.ViewportMax), Mathf.Clamp(p.y, beat.ViewportMin, beat.ViewportMax));
            castDrop = drop;
            reach = Mathf.Clamp01(along);
        }

        // ---------------------------------------------------------------- frame

        // EvaluateGesturePose, every evaluated frame: advance the clocks, take this frame's additions, hand the camera its part.
        private void JuiceStep308(float dt, bool drawing, bool stroking, Vector2 visualVelocity)
        {
            _juiceLeanApplied308 = false; JuiceShaftLeanDegrees308 = 0f; JuiceHeadroom308 = 1f;
            var solver = JuiceSolver308();
            if (solver == null) { _juiceFrame308 = default; JuicePushCamera308(Vector3.zero); return; }
            if (stroking) solver.NoteStrokeVelocity(visualVelocity, dt);
            solver.Advance(dt);
            // D rule 1 / 7: no camera reaction while the draw mode is on, a live stroke is attached, or the get-up camera frames the body
            bool cameraAllowed = !drawing && (_brushFeed == null || _brushFeed.LiveStrokeCount308 == 0)
                && (_cameraRig == null || !_cameraRig.IsDrawingCloseup);
            if (cameraAllowed && solver.CameraRunning)
            {
                if (_juiceGetUp308 == null) _juiceGetUp308 = GetComponent<WorldMacroPlayerGetUpCamera303>();
                if (_juiceGetUp308 != null && _juiceGetUp308.Active) cameraAllowed = false;
            }
            solver.Evaluate(cameraAllowed, out _juiceFrame308);
            JuicePushCamera308(_juiceFrame308.Camera);
        }

        // seat, stopped rig, pause, disable: nothing of the juice stays on the camera
        private void JuiceSuspend308()
        {
            _juiceFrame308 = default; _juiceLeanApplied308 = false;
            _juice308?.CancelCamera();
            JuicePushCamera308(Vector3.zero);
        }

        private void JuicePushCamera308(Vector3 nodRollFov)
        {
            if (nodRollFov.x == _juiceCameraPushed308.x && nodRollFov.y == _juiceCameraPushed308.y && nodRollFov.z == _juiceCameraPushed308.z) return;
            _juiceCameraPushed308 = nodRollFov;
            if (_cameraRig == null) return;
            // #308 3차 (D308-24 answer 23): the lock-on marks follow the reaction when the profile says so. Told to the owner BEFORE
            // the offset: the owner announces the change inside SetRenderOffset308 and the HUD asks it for the shift right there.
            _cameraRig.RenderOffsetMarksFollow308 = _juice308 != null && _juice308.Profile != null && _juice308.Profile.Camera.MarksFollow;
            _cameraRig.SetRenderOffset308(nodRollFov.x, nodRollFov.y, nodRollFov.z);
        }

        // ---------------------------------------------------------------- close-up arm (A-1 / A-2)

        // StartedStroke frame: the anchors were read from last frame's real transforms. Take last frame's lean out of them.
        private void JuiceCleanAnchors308(ref Vector3 wristLocal, ref Vector3 gripLocal)
        {
            if (!_juiceLeanApplied308) return;
            wristLocal -= _juiceWristDelta308; gripLocal -= _juiceGripDelta308;
        }

        // ApplyNearArmVertical, right after the bristles were evaluated: this frame's splay addition (no state in the bristle rig).
        private void JuiceSplayNear308()
        {
            if (_juice308 == null || _juiceFrame308.SplayAdd <= 0f || _nearBrush == null || _nearBrush.Bristles == null) return;
            _nearBrush.Bristles.AddPresentationSplay(_juiceFrame308.SplayAdd);
        }

        // ApplyNearArmVertical, after the clean solve of axis / hand / grip / wrist: lean the shaft, keep the tip.
        // The elbow pole, the follow state and the depth were all computed from the clean values and are not touched.
        private void JuiceLeanNear308(Transform view, Vector3 planeNormal, Vector3 tip, Vector3 shoulder, Vector3 elbow, float blend,
            Vector3 gripPosition, Vector3 axis, ref Quaternion hand, ref Quaternion grip, ref Vector3 wrist)
        {
            if (_juice308 == null || !_juiceFrame308.AnyShaft) return;
            var profile = _juice308.Profile;
            // arm headroom: near full reach the lean fades out, so the reach clamp never has to move the tip
            float reach = (_near.UpperLength + _near.ForearmLength) * _profile.MaximumArmExtension;
            float headroom = Mathf.Clamp01((reach - Vector3.Distance(wrist, shoulder)) / Mathf.Max(.001f, profile.HeadroomMeters));
            JuiceHeadroom308 = headroom;
            if (headroom <= 0f) return;
            Vector3 leaned = axis;
            float raise = _juiceFrame308.ShaftRaiseDegrees * headroom;
            if (raise > 0f && planeNormal.sqrMagnitude > .000001f)
                leaned = Vector3.RotateTowards(leaned, planeNormal.normalized, raise * Mathf.Deg2Rad, 0f);
            Vector2 lean = _juiceFrame308.ShaftLeanDegrees * headroom;
            if (lean.x != 0f || lean.y != 0f)
            {
                // the butt leads along the stroke direction (the same sign as the rig's own sweep lean in VerticalAxis)
                Vector3 along = Vector3.ProjectOnPlane(view.right * lean.x + view.up * lean.y, leaned);
                if (along.sqrMagnitude > .0000000001f)
                    leaned = (leaned - along.normalized * Mathf.Tan(lean.magnitude * Mathf.Deg2Rad)).normalized;
            }
            leaned = ClampCone(leaned, axis, profile.MaxShaftLeanDegrees);
            float degrees = Vector3.Angle(axis, leaned);
            if (degrees <= 0f) return;
            Vector3 cleanWrist = wrist; Quaternion cleanGrip = grip;
            hand = BlendedHand(_nearBrush, leaned, view.up, wrist - elbow, blend, out grip);
            wrist = tip - grip * _nearBrush.TipOffset - hand * gripPosition;
            _juiceWristDelta308 = view.InverseTransformVector(wrist - cleanWrist);
            _juiceGripDelta308 = view.InverseTransformVector(cleanGrip * _nearBrush.TipOffset - grip * _nearBrush.TipOffset);
            _juiceLeanApplied308 = true;
            JuiceShaftLeanDegrees308 = degrees;
        }

        // ---------------------------------------------------------------- call stroke (C-1 / C-3) and the car (D-2)

        private void JuiceCallBegan308() { JuiceSolver308()?.CallBegan(); }

        // ApplyAirStrokePose308: arm weight, path past the end and the refused droop. The contact span keeps its own values.
        private void JuiceAirStroke308(float progress, float raiseEnd, float contactEnd, ref float weight, ref float path, ref float droop)
        {
            var solver = JuiceSolver308();
            if (solver == null || !solver.Active) return;
            solver.EvaluateCall(progress, raiseEnd, contactEnd, _air308.Seconds, out float w, out float over, out float sink);
            weight = w;
            if (progress > contactEnd) { path += _air308.Reverse ? -over : over; droop = sink; }
        }

        /// <summary>C-3 / D-2a: the call stroke left the air; accepted = the car comes or is taken back (WorldMacroPalanquinSummon).</summary>
        public void NotifyCallMoment308(bool accepted) { JuiceSolver308()?.CallMoment(accepted); }

        /// <summary>D-2b: the summoned car is fully revealed; distance = flat metres between player and car.</summary>
        public void NotifyVehicleSetDown308(float distance) { JuiceSolver308()?.VehicleSetDown(distance); }

        // ---------------------------------------------------------------- preview seam (Juice308Build)

        private bool JuiceInput308Drawing(bool live) => _juiceInputOn308 ? _juiceInput308.Drawing : live;
        private bool JuiceInput308Stroking() => _juiceInputOn308 ? _juiceInput308.Stroking : _drawing.IsStroking;
        private bool JuiceInput308Pointer(out Camera camera, out Vector2 screen, out Vector3 ink)
        {
            ink = default;
            if (_juiceInputOn308) { camera = _juiceInput308.Camera; screen = _juiceInput308.Screen; return _juiceInput308.HasPointer && camera != null; }
            camera = null; screen = default;
            return _brushFeed != null && _brushFeed.TryGetVisualPointer(out camera, out screen, out ink);
        }

        /// <summary>Preview driver only: feed draw state and pointer instead of the live input owners (null = live input again).</summary>
        public void SetPreviewInput308(GestureInput308? input)
        {
            _juiceInputOn308 = input.HasValue;
            _juiceInput308 = input.GetValueOrDefault();
        }

        /// <summary>Preview driver only: one evaluated frame with the preview input (restores the last frame's edits first, as Update does).</summary>
        public bool PreviewEvaluate308(float dt)
        {
            if (!_bound || !_juiceInputOn308) return false;
            RestoreAnimatedPose();
            EvaluateGesturePose(Mathf.Min(.1f, Mathf.Max(0f, dt)));
            return true;
        }

        /// <summary>Preview driver only: the events the drawing input would raise.</summary>
        public void PreviewModeEntered308() { OnJuiceModeEntered308(); }
        public void PreviewStrokeStarted308() { OnStrokeStarted(); OnJuiceStrokeStarted308(); }
        public void PreviewStrokeEnded308() { OnJuiceStrokeEnded308(); }
        public void PreviewCommitted308(bool success, bool rejected) { PreviewCommitted308(success, rejected, false); }
        /// <summary>Preview driver only: the same, with the kind the wiring would have accepted (parry = a parry glyph).</summary>
        public void PreviewCommitted308(bool success, bool rejected, bool parry)
        {
            if (rejected) OnJuiceCastRejected308();
            _juiceParry308 = parry && !rejected;
            OnCommitted(success);
            OnModeExited(); OnJuiceDrawingEnded308();
        }
        /// <summary>Preview driver only: put the animated pose back and hide the close-up arm (leaves nothing behind).</summary>
        public void PreviewEnd308()
        {
            SetPreviewInput308(null);
            RestoreAnimatedPose(); EndCast(); SetNearVisible(false); ResetTransitions();
            JuiceSuspend308();
        }
    }
}
