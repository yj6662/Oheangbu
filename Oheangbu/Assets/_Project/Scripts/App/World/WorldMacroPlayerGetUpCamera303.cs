using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    // #303 follow-up (SPEC-PLAYER-FEEL-300 H.1, Art/PlaytestRecovery/Player303/RESPAWN_CAMERA_PLAN.md): after the rest-spot
    // respawn the view camera frames the rising body, then eases back to the player's view over get-up progress
    // ReturnStart..ReturnEnd. Presentation only: damage, death, recovery save, checkpoints, input rules and recognition are
    // untouched.
    //
    // Render-time pose override. The pose is set when a render context that draws the view camera begins and is restored
    // when that context ends. PlayerMotor pitch/yaw, CameraRigController pose/boom and body-visibility test, the drawing
    // plane (a camera child) and recognition never see it. Per-camera renderers (grass, dressing, art) do see it, because
    // they cull in beginCameraRendering, which runs inside the context. So they cull against the frame that is drawn.
    // Separate film cameras (Presentation297) are left alone.
    //
    // Once input is back, looking, moving or drawing lets go at once. Drawing sets the weight to 0 on the same frame,
    // so recognition never sees a moved view.
    //
    // Pitch-pop fix (plan §0.3 / §1.5): PlayerMotor zeroes _pitch (ResetMotion at the death and in Teleport) but does not
    // write the pivot while input is blocked (PlayerMotor.cs Update's early return). The pivot therefore keeps the
    // pre-death pitch and would snap to 0 on the first frame input returns. At the get-up start, while input is still
    // blocked and the death veil is opaque, this component levels the pivot to the value the motor will write. It does
    // this even when GetUpCamera is off. It assumes BeginGetUp is only called under the opaque veil
    // (WorldMacroPlayerDeath303.Update). A future BeginGetUp path without the veil would make the levelling visible.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(950)]   // after WorldMacroPlayerLean303 (900): the bones are final
    public sealed class WorldMacroPlayerGetUpCamera303 : MonoBehaviour
    {
        const float CastRadius = .25f;       // = CameraRigController.BoomRadius (technical)
        const float CastLift = .3f;          // cast origin above the focus, so a lying focus never starts inside the ground (technical)
        const float CastSkin = .03f, CastMin = .05f;
        const float ReleaseDegrees = .05f;   // yaw / pitch change per frame that counts as the player's look (technical)
        const float SnapMetres = 3f;         // a focus jump this large in one frame is a teleport: snap, do not sweep (technical)
        const float FallbackHips = .6f;      // non-humanoid body: hips above the feet (technical)
        const float StartWindow = .15f;      // seconds after BeginGetUp a start is still under the opaque veil; a later
                                             // enable (or a re-enable mid get-up) latches without framing or levelling

        public PlayerReaction303Profile Profile;
        public WorldMacroPlayerReaction303 Reaction;
        public WorldMacroPlaytestSession Session;
        [Tooltip("Review harness A/B (Player303Build react-sequence:nocam): measure the untouched view, apply nothing, do not level the pivot.")]
        [System.NonSerialized] public bool DebugMeasureOnly;

        public bool Active { get; private set; }
        public float Weight { get; private set; }
        public bool FirstPerson { get; private set; }
        public bool ReleasedByInput { get; private set; }
        public bool PivotLeveled { get; private set; }
        public int Starts { get; private set; }
        public float PitchBeforeReturn { get; private set; } = float.NaN;     // pivot pitch on the last blocked frame
        public float PivotPitchAtRelease { get; private set; } = float.NaN;   // pivot pitch on the first frame input is back
        public float ReturnPitchJump { get; private set; } = float.NaN;       // |difference|: the pop the fix removes
        public float InputReturnedAt { get; private set; } = -1f;            // Time.time of that first frame
        public float MaxStepDegrees { get; private set; }   // largest rendered rotation change per frame while active (first frame excluded)
        public float MaxStepMetres { get; private set; }    // largest rendered position change per frame while active (first frame excluded)
        public Vector3 FocusViewport { get; private set; }  // focus in the rendered view (z < 0: behind the camera)
        public float FramingDistance { get; private set; }
        public float EyeHeight { get; private set; }        // smoothed eye above the feet (first-person input, logged in any mode)
        public float LookUp => _lookUp;
        public int LastAppliedFrame { get; private set; } = -1;

        Transform _hips, _head, _appliedTo, _appliedParent; Camera _appliedCamera;
        Vector3 _focus, _eye, _savedLocalPos, _appliedLocalPos, _prevRenderedPos;
        Quaternion _savedLocalRot, _prevRendered;
        float _lookUp, _prevYaw, _prevPitch, _lastBlockedPitch;
        bool _latched, _wasBlocked, _applied, _hasPrev;
        readonly RaycastHit[] _hits = new RaycastHit[32];

        void OnEnable()
        {
            RenderPipelineManager.beginContextRendering += OnContextBegin;
            RenderPipelineManager.endContextRendering += OnContextEnd;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnContextBegin;
            RenderPipelineManager.endContextRendering -= OnContextEnd;
            RestoreOutsideRender();
            Stop(); _latched = false;
        }

        WorldMacroCombatWalker Walker
        {
            get
            {
                var s = Session != null ? Session : (Reaction != null ? Reaction.Session : null);
                return s != null ? s.Walker : null;
            }
        }

        static bool InputBlocked(PlayerMotor m) => m.EnvironmentalInputBlocked || (m.RuntimeState != null && m.RuntimeState.InputBlocked);
        static float PivotPitch(Transform pivot) => pivot != null ? Mathf.DeltaAngle(0f, pivot.localEulerAngles.x) : 0f;

        void Stop() { Active = false; Weight = 0f; }

        void LateUpdate()
        {
            if (_applied) RestoreOutsideRender();   // a context that never ended: no pose outlives its frame
            var w = Walker;
            if (Profile == null || Reaction == null || w == null || w.ViewCamera == null || w.Body == null || w.Motor == null || w.Seated)
            { Stop(); return; }
            var body = w.Body.transform; var pivot = w.ViewCamera.transform.parent;
            bool gettingUp = Reaction.GettingUp;
            if (!gettingUp) _latched = false;
            else if (!_latched)
            {
                _latched = true;   // one start per get-up
                if (Reaction.GetUpProgress * Mathf.Max(.1f, Profile.GetUpSeconds) <= StartWindow) OnGetUpStart(w, body, pivot);   // the BeginGetUp frame: under the opaque veil
            }
            if (!Active) return;

            float dt = Time.deltaTime;
            bool blocked = InputBlocked(w.Motor);
            float yaw = body.eulerAngles.y, pitch = PivotPitch(pivot);
            if (blocked) _lastBlockedPitch = pitch;
            else
            {
                // Watch the effect of input, not the input: only look (or the lock-on pull) turns the body or tilts the pivot.
                // The motor ignores input while blocked, so nothing is judged then. On the return frame the pitch check is
                // skipped: that change is the motor catching up, not the player.
                bool returnFrame = _wasBlocked;
                if (returnFrame)
                {
                    PitchBeforeReturn = _lastBlockedPitch; PivotPitchAtRelease = pitch;
                    InputReturnedAt = Time.time;
                    ReturnPitchJump = Mathf.Abs(Mathf.DeltaAngle(_lastBlockedPitch, pitch));
                }
                var v = w.Motor.ActualLocalVelocity;
                if (Mathf.Abs(Mathf.DeltaAngle(_prevYaw, yaw)) > ReleaseDegrees
                    || (!returnFrame && Mathf.Abs(Mathf.DeltaAngle(_prevPitch, pitch)) > ReleaseDegrees)
                    || new Vector2(v.x, v.z).magnitude > Profile.GetUpCameraMoveRelease) ReleasedByInput = true;
            }
            _prevYaw = yaw; _prevPitch = pitch; _wasBlocked = blocked;

            if (w.Motor.IsDrawing || (w.Drawing != null && w.Drawing.InDrawMode)) { ReleasedByInput = true; Weight = 0f; }   // recognition never sees a moved view
            float target = gettingUp && !ReleasedByInput && Profile.GetUpCamera
                ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Profile.GetUpCameraReturnStart, Profile.GetUpCameraReturnEnd, Reaction.GetUpProgress))
                : 0f;
            Weight = Mathf.MoveTowards(Weight, target, dt / Mathf.Max(.01f, Profile.GetUpCameraFade));
            Track(w, body, pivot, dt);
            if (Weight <= 0f && target <= 0f) Active = false;
        }

        void OnGetUpStart(WorldMacroCombatWalker w, Transform body, Transform pivot)
        {
            // Pitch-pop fix (header). The motor's _pitch is 0 here (ResetMotion at the death, again in Teleport) and it has not
            // written the pivot since input was blocked.
            PivotLeveled = false;
            if (!DebugMeasureOnly && InputBlocked(w.Motor) && pivot != null && pivot.parent == body)
            { pivot.localRotation = Quaternion.identity; PivotLeveled = true; }
            if (!Profile.GetUpCamera) return;

            Active = true; Starts++; ReleasedByInput = false; Weight = 1f;   // at once: the veil is opaque
            MaxStepDegrees = 0f; MaxStepMetres = 0f; _hasPrev = false;
            PitchBeforeReturn = PivotPitchAtRelease = ReturnPitchJump = float.NaN;
            InputReturnedAt = -1f;
            // First person only when the rig is not in its shoulder view (D179: the shoulder view is the default and the
            // near view exists only while drawing, which the gate cancels). A boom shortened by a low ceiling stays third person.
            var rig = w.CameraRig;
            FirstPerson = rig != null && (!rig.IsShoulder || rig.IsDrawingCloseup);
            var an = Reaction.Animator; bool human = an != null && an.isHuman;
            _hips = human ? an.GetBoneTransform(HumanBodyBones.Hips) : null;
            _head = human ? an.GetBoneTransform(HumanBodyBones.Head) : null;
            _prevYaw = body.eulerAngles.y; _prevPitch = _lastBlockedPitch = PivotPitch(pivot); _wasBlocked = true;
            Track(w, body, pivot, -1f);
        }

        void Track(WorldMacroCombatWalker w, Transform body, Transform pivot, float dt)
        {
            Vector3 hips = _hips != null ? _hips.position : body.TransformPoint(0f, FallbackHips, 0f);
            Vector3 head = _head != null ? _head.position : (pivot != null ? pivot.position : body.TransformPoint(0f, w.EyeHeight, 0f));
            Vector3 focus = Vector3.Lerp(hips, head, Profile.GetUpCameraFocus);
            Vector3 torso = head - hips;
            float flat = torso.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Angle(torso, Vector3.up) / 90f) : 0f;
            bool snap = dt < 0f || (focus - _focus).sqrMagnitude > SnapMetres * SnapMetres;   // start, or a late teleport under way
            float k = snap ? 1f : 1f - Mathf.Exp(-Mathf.Max(0f, Profile.GetUpCameraResponse) * dt);
            _focus = Vector3.Lerp(_focus, focus, k);
            _eye = Vector3.Lerp(_eye, head + Vector3.up * Profile.GetUpEyeAboveHead, k);
            _lookUp = Mathf.Lerp(_lookUp, flat * Profile.GetUpEyeLookUp, k);
            EyeHeight = _eye.y - body.position.y;
        }

        // The pose the view camera renders this frame (for a follower such as a film rig). False while nothing is applied.
        public bool TryGetViewPose(out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.identity;
            var w = Walker;
            if (!Active || Weight <= 0f || DebugMeasureOnly || Profile == null || !Profile.GetUpCamera || w == null || w.ViewCamera == null || w.Body == null) return false;
            var t = w.ViewCamera.transform;
            Pose(w, t.position, t.rotation, out position, out rotation);
            return true;
        }

        void Pose(WorldMacroCombatWalker w, Vector3 rigPos, Quaternion rigRot, out Vector3 pos, out Quaternion rot)
        {
            var body = w.Body.transform; var pivot = w.ViewCamera.transform.parent;
            if (!FirstPerson)
            {
                // 3rd person (plan §2.3): orbit the body focus at GetUpCameraPitch behind the body's facing, clamped by a
                // sphere cast (a low ceiling pulls the camera in, and it still aims at the body)
                Vector3 origin = _focus + Vector3.up * CastLift;
                Vector3 ideal = _focus + Quaternion.Euler(Profile.GetUpCameraPitch, body.eulerAngles.y, 0f) * new Vector3(0f, 0f, -Profile.GetUpCameraDistance);
                Vector3 want = Clamp(origin, ideal, body);
                FramingDistance = Vector3.Distance(_focus, want);
                Vector3 look = _focus - want;
                Quaternion aim = look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look, Vector3.up) : rigRot;
                pos = Clamp(Vector3.Lerp(pivot != null ? pivot.position : origin, origin, Weight), Vector3.Lerp(rigPos, want, Weight), body);
                rot = Quaternion.Slerp(rigRot, aim, Weight);
            }
            else
            {
                // 1st person / near view (plan §2.4, dormant in this build): the eye follows the head down and back up, and
                // looks up while the torso lies flat (not the head bone's rotation: motion sickness, per-rig axes)
                Vector3 eyeNow = pivot != null ? pivot.position : rigPos;
                pos = rigPos + (_eye - eyeNow) * (Weight * Profile.GetUpEyeFollow);
                rot = rigRot * Quaternion.Euler(-_lookUp * Weight, 0f, 0f);
            }
        }

        void OnContextBegin(ScriptableRenderContext _, List<Camera> cameras)
        {
            if (!Active || _applied) return;
            var w = Walker;
            if (w == null || w.ViewCamera == null || w.Body == null || cameras == null || !cameras.Contains(w.ViewCamera)) return;
            var camera = w.ViewCamera; var t = camera.transform;
            if (Weight > 0f && !DebugMeasureOnly && Profile != null && Profile.GetUpCamera)
            {
                Pose(w, t.position, t.rotation, out var pos, out var rot);
                _savedLocalPos = t.localPosition; _savedLocalRot = t.localRotation; _appliedTo = t; _appliedParent = t.parent; _appliedCamera = camera;
                t.SetPositionAndRotation(pos, rot);
                _appliedLocalPos = t.localPosition; _applied = true; LastAppliedFrame = Time.frameCount;
            }
            FocusViewport = camera.WorldToViewportPoint(_focus);
            Vector3 p = t.position; Quaternion r = t.rotation;
            if (_hasPrev)
            {
                MaxStepDegrees = Mathf.Max(MaxStepDegrees, Quaternion.Angle(_prevRendered, r));
                MaxStepMetres = Mathf.Max(MaxStepMetres, Vector3.Distance(_prevRenderedPos, p));
            }
            _prevRendered = r; _prevRenderedPos = p; _hasPrev = true;
        }

        void OnContextEnd(ScriptableRenderContext _, List<Camera> cameras)
        {
            // only the context that drew the view camera (a nested one, e.g. a reflection render, leaves the pose in place)
            if (!_applied || _appliedTo == null || _appliedCamera == null || cameras == null || !cameras.Contains(_appliedCamera)) return;
            _applied = false;
            if (_appliedTo.parent == _appliedParent) { _appliedTo.localPosition = _savedLocalPos; _appliedTo.localRotation = _savedLocalRot; }
            _appliedTo = null; _appliedCamera = null;
        }

        // Outside a render (a context that never ended, or disable): the rotation is only ever written here, so it goes back.
        // The position goes back only if nobody wrote it since (the rig rewrites it every Update).
        void RestoreOutsideRender()
        {
            if (!_applied) return;
            _applied = false;
            if (_appliedTo != null && _appliedTo.parent == _appliedParent)
            {
                _appliedTo.localRotation = _savedLocalRot;
                if ((_appliedTo.localPosition - _appliedLocalPos).sqrMagnitude < 1e-10f) _appliedTo.localPosition = _savedLocalPos;
            }
            _appliedTo = null; _appliedCamera = null;
        }

        Vector3 Clamp(Vector3 from, Vector3 to, Transform body)
        {
            Vector3 d = to - from; float len = d.magnitude;
            if (len < .001f) return to;
            int n = Physics.SphereCastNonAlloc(from, CastRadius, d / len, _hits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = len;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.distance <= 0f || h.transform == null || h.transform.IsChildOf(body)) continue;   // start overlaps, own capsule / body
                best = Mathf.Min(best, Mathf.Max(CastMin, h.distance - CastSkin));
            }
            return from + d / len * best;
        }
    }
}
