using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.Combat
{
    public sealed partial class PlayerMotor
    {
        [SerializeField] private PlayerLocomotionProfileSO _locomotion;
        private InputAction _sprintAction, _jumpAction, _sitAction, _crouchAction, _locomotionDrawAction;
        private PlayerVitals _locomotionVitals;
        private readonly RaycastHit[] _groundHits = new RaycastHit[24];
        private readonly Collider[] _standHits = new Collider[24];
        private Vector3 _planarVelocity, _actualLocalVelocity, _groundNormal = Vector3.up;
        private Vector3 _standingCenter, _standingEye;
        private float _standingHeight, _posture, _sitTarget, _crouch, _crouchTarget;
        private float _actualYawSpeed;
        private bool _grounded, _jumping, _needsHarvestRelease, _locomotionInitialized, _shapeModified, _standBlocked, _locomotionSubscribed;
        private int _jumpSerial, _landSerial;
        private bool _rolling;

        public PlayerLocomotionProfileSO LocomotionProfile => _locomotion;
        public bool HasLocomotion => _locomotion != null;
        public bool IsLocomotionGrounded => !HasLocomotion ? _controller != null && _controller.isGrounded : _grounded && !_jumping;
        public bool IsAirborne => HasLocomotion && !IsLocomotionGrounded;
        public bool IsSitting => HasLocomotion && (_posture > .001f || _sitTarget > 0f);
        public float Posture01 => _posture;
        public bool IsCrouching => HasLocomotion && (_crouch > .001f || _crouchTarget > .5f);
        public float Crouch01 => _crouch;
        public float ActualYawSpeed => _actualYawSpeed;
        public bool CanStandForBoarding => !HasLocomotion || (!IsSitting && CanStand());
        public bool PrepareForBoarding() { if (!HasLocomotion) return true; if (!CanStandForBoarding) return false; _crouch = _crouchTarget = 0; ApplyPostureShape(); return true; }
        public bool SitRequested => HasLocomotion && (_sitTarget > .5f || _standBlocked);
        public Vector3 ActualLocalVelocity => _actualLocalVelocity;
        public Vector3 GroundNormal => _groundNormal;
        public float VerticalVelocity => _verticalVelocity;
        public float JumpLaunchSpeed => HasLocomotion && _config != null ? JumpSpeed(_locomotion.JumpHeight, _config.Gravity) : 0f;
        public bool IsSprinting => HasLocomotion && IsLocomotionGrounded && !IsSitting && !IsCrouching && !_drawing && _sprintAction != null
            && (_locomotion.SprintToggle ? _sprintLatched : _sprintAction.IsPressed());
        public bool SprintToggles => HasLocomotion && _locomotion.SprintToggle;
        public bool SprintLatched => HasLocomotion && _locomotion.SprintToggle && _sprintLatched;
        private bool _sprintLatched;
        private float _sprintIdle;
        public bool IsDodging => _dodge != null && _dodge.IsDashing;
        public bool IsRolling => IsDodging && _rolling;
        public bool IsHarvesting => _harvest != null && _harvest.IsExtracting;
        public float DodgeProgress => _dodge != null ? _dodge.Progress : 1f;
        public Vector3 DodgeLocalDirection => _dodge != null ? transform.InverseTransformDirection(_dodge.Direction) : Vector3.forward;
        public int JumpSerial => _jumpSerial;
        public int LandingSerial => _landSerial;
        public bool CanBeginDrawing => !HasLocomotion || (ActionAllowed && IsLocomotionGrounded && !IsSitting && !IsDodging);
        private bool CanHarvestNow => !HasLocomotion || (CanBeginDrawing && !_needsHarvestRelease
            && (_locomotionDrawAction == null || !_locomotionDrawAction.IsPressed()));

        public void ConfigureLocomotion(PlayerLocomotionProfileSO profile)
        {
            bool wasSubscribed = _locomotionSubscribed;
            if (wasSubscribed) UnsubscribeLocomotion();
            RestoreStandingShape(); _locomotion = profile;
            InitializeLocomotion(); ResetLocomotion();
            if (wasSubscribed) EnableLocomotion();
        }

        private void InitializeLocomotion()
        {
            if (_controller == null) _controller = GetComponent<CharacterController>();
            var map = _actions != null ? _actions.FindActionMap(MapName) : null;
            _sprintAction = map?.FindAction("Sprint"); _jumpAction = map?.FindAction("Jump"); _sitAction = map?.FindAction("Sit"); _crouchAction = map?.FindAction("Crouch");
            // Motor updates before the drawing controller. Read the current intent so
            // a same-frame Q+LMB press cannot harvest once before drawing enters.
            _locomotionDrawAction = _actions?.FindActionMap("Drawing")?.FindAction("DrawMode");
            _locomotionVitals = GetComponent<PlayerVitals>();
            if (!_locomotionInitialized && _controller != null)
            {
                _standingHeight = _controller.height; _standingCenter = _controller.center;
                _standingEye = _cameraPivot != null ? _cameraPivot.localPosition : Vector3.up * 1.55f;
                _locomotionInitialized = true;
            }
        }

        private void EnableLocomotion()
        {
            if (_locomotionSubscribed) return;
            InitializeLocomotion(); _locomotionSubscribed = true;
            if (_jumpAction != null) _jumpAction.performed += OnJump;
            if (_sprintAction != null) _sprintAction.performed += OnSprint;
            if (_sitAction != null) _sitAction.performed += OnSit;
            if (_crouchAction != null) _crouchAction.performed += OnCrouch;
            if (_locomotionVitals != null) { _locomotionVitals.Damaged += OnLocomotionDamage; _locomotionVitals.Died += OnLocomotionDeath; }
        }
        private void DisableLocomotion()
        {
            UnsubscribeLocomotion(); ResetLocomotion(); RestoreStandingShape();
        }
        private void UnsubscribeLocomotion()
        {
            if (!_locomotionSubscribed) return;
            _locomotionSubscribed = false;
            if (_jumpAction != null) _jumpAction.performed -= OnJump;
            if (_sprintAction != null) _sprintAction.performed -= OnSprint;
            if (_sitAction != null) _sitAction.performed -= OnSit;
            if (_crouchAction != null) _crouchAction.performed -= OnCrouch;
            if (_locomotionVitals != null) { _locomotionVitals.Damaged -= OnLocomotionDamage; _locomotionVitals.Died -= OnLocomotionDeath; }
        }
        private void ResetLocomotion()
        {
            _dodge?.Cancel();
            _planarVelocity = _actualLocalVelocity = Vector3.zero; _sitTarget = _posture = _crouch = _crouchTarget = _actualYawSpeed = 0f;
            _jumping = false; _rolling = false; _standBlocked = false; _grounded = _controller != null && _controller.enabled && _controller.isGrounded;
            _sprintLatched = false; _sprintIdle = 0f;
            _needsHarvestRelease = true; _groundNormal = Vector3.up;
            RestoreStandingShape();
        }
        private void RestoreStandingShape()
        {
            if (!_locomotionInitialized || !_shapeModified) return;
            _shapeModified = false;
            if (_controller != null) { _controller.height = _standingHeight; _controller.center = _standingCenter; }
            if (_cameraPivot != null) _cameraPivot.localPosition = _standingEye;
        }
        private void SuspendLocomotionInput()
        {
            if (!HasLocomotion) return;
            _planarVelocity = _actualLocalVelocity = Vector3.zero;
            _needsHarvestRelease = true;
        }
        private void OnLocomotionDamage(float _) { if (HasLocomotion) { _sitTarget = 0f; _needsHarvestRelease = true; } }
        private void OnLocomotionDeath() { if (HasLocomotion) ResetLocomotion(); }
        private bool ActionAllowed => HasLocomotion && _config != null && isActiveAndEnabled && _controller != null && _controller.enabled
            && !InputBlocked && Time.timeScale > 0f
            && (_locomotionVitals == null || _locomotionVitals.Hp01 > 0f);
        private void OnJump(InputAction.CallbackContext _)
        {
            if (!ActionAllowed || !IsLocomotionGrounded || IsSitting || IsCrouching || IsDodging || _drawing
                || (_harvest != null && _harvest.IsExtracting)) return; // D306: a held LMB is no longer a harvest; only the pull itself blocks
            _verticalVelocity = JumpSpeed(_locomotion.JumpHeight, _config.Gravity);
            _planarVelocity.y = 0f;
            _grounded = false; _jumping = true; _needsHarvestRelease = true; _jumpSerial++;
        }
        // #300 run toggle: each press flips the latch; it only starts where held running could start (standing, grounded)
        private void OnSprint(InputAction.CallbackContext _)
        {
            if (!HasLocomotion || !_locomotion.SprintToggle) return;
            if (_sprintLatched) { _sprintLatched = false; return; }
            if (!ActionAllowed || !IsLocomotionGrounded || IsSitting || IsCrouching || _drawing) return;
            _sprintLatched = true; _sprintIdle = 0f;
        }
        private void OnCrouch(InputAction.CallbackContext _)
        {
            if (!ActionAllowed || !IsLocomotionGrounded || IsSitting || IsDodging || _drawing
                || (_locomotionDrawAction != null && _locomotionDrawAction.IsPressed())
                || (_harvest != null && _harvest.IsExtracting)) return; // D306: a held LMB is no longer a harvest; only the pull itself blocks
            if (_crouchTarget > .5f) { if (CanStand()) _crouchTarget = 0; }
            else _crouchTarget = 1;
        }
        // Presentation only, called after the checkpoint transaction has committed.
        // Movement and damage retain their ordinary immediate stand-up behavior.
        public bool TryBeginRestPose(){
            if(!HasLocomotion||!ActionAllowed||!IsLocomotionGrounded||IsDodging||_drawing||IsCrouching||IsHarvesting)return false;
            _sitTarget=1f;_planarVelocity=Vector3.zero;_needsHarvestRelease=true;return true;
        }
        private void OnSit(InputAction.CallbackContext _)
        {
            if (!ActionAllowed || IsCrouching) return;
            if (IsSitting) { _sitTarget = 0f; return; }
            if (!IsLocomotionGrounded || IsDodging || _drawing || (_harvest != null && _harvest.IsExtracting)
                || (_move != null && _move.ReadValue<Vector2>().sqrMagnitude > .001f) || _planarVelocity.sqrMagnitude > .04f) return;
            _sitTarget = 1f; _planarVelocity = Vector3.zero; _needsHarvestRelease = true;
        }
        public static float JumpSpeed(float height, float gravity) => Mathf.Sqrt(2f * Mathf.Max(0f, height) * Mathf.Abs(gravity));

        private void UpdateLocomotion(Vector2 input) => StepLocomotion(input, Time.deltaTime);

        // Explicit scaled step keeps controller integration deterministic and independently testable.
        // Gameplay calls this once per Update; there is no second motor or unscaled movement path.
        private void StepLocomotion(Vector2 input, float dt)
        {
            if (!ActionAllowed) { _actualLocalVelocity = Vector3.zero; return; }
            if (dt <= 0f) return;
            if (_harvestAction == null || !_harvestAction.IsPressed()) _needsHarvestRelease = false;
            else if (IsSitting || IsAirborne) _needsHarvestRelease = true;
            bool supported = ProbeGround(out RaycastHit support);
            bool wasGrounded = IsLocomotionGrounded;
            if (!_jumping && supported && _verticalVelocity <= 0f && GroundGap(support) <= .05f) _grounded = true;
            if (IsSitting && input.sqrMagnitude > .001f) _sitTarget = 0f;
            if (!supported) _sitTarget = 0f;
            float targetPosture = _sitTarget;
            _standBlocked = targetPosture < _posture && !CanStand();
            if (_standBlocked) targetPosture = _posture;
            float duration = targetPosture > _posture ? _locomotion.SitDownSeconds : _locomotion.StandUpSeconds;
            _posture = Mathf.MoveTowards(_posture, targetPosture, dt / Mathf.Max(.05f, duration));
            float crouchTarget = _crouchTarget;
            if (crouchTarget < _crouch && !CanStand()) { crouchTarget = _crouch; _crouchTarget = 1; }
            _crouch = Mathf.MoveTowards(_crouch, crouchTarget, dt / Mathf.Max(.05f, _locomotion.CrouchTransitionSeconds));
            ApplyPostureShape();

            if (_locomotion.SprintToggle && _sprintLatched)
            {
                // a latched run ends when the player stops, crouches, sits or starts drawing
                _sprintIdle = input.sqrMagnitude < .01f ? _sprintIdle + dt : 0f;
                if (_sprintIdle >= _locomotion.SprintToggleStopSeconds || IsCrouching || IsSitting || _drawing) { _sprintLatched = false; _sprintIdle = 0f; }
            }
            Vector3 direction = transform.right * input.x + transform.forward * input.y;
            direction = Vector3.ClampMagnitude(direction, 1f);
            float speed = IsCrouching ? _locomotion.CrouchSpeed : IsSprinting ? _locomotion.RunSpeed : _locomotion.WalkSpeed;
            Vector3 desired = IsSitting ? Vector3.zero : direction * speed * Mathf.Clamp01(TerrainMovementScale);
            if (supported && IsLocomotionGrounded)
            {
                _groundNormal = support.normal;
                desired = Vector3.ProjectOnPlane(desired, _groundNormal).normalized * desired.magnitude;
            }
            float rate = IsAirborne ? _locomotion.AirAcceleration
                : desired.sqrMagnitude < _planarVelocity.sqrMagnitude ? _locomotion.Deceleration : _locomotion.Acceleration;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, desired, rate * dt);
            Vector3 planar = _planarVelocity;
            if (IsLocomotionGrounded && !IsSitting && _dodge != null && _dodge.TryGetDashVelocity(out Vector3 dash)) planar = dash * Mathf.Clamp01(TerrainMovementScale);

            float verticalMove;
            if (IsLocomotionGrounded && _verticalVelocity <= 0f)
            {
                _verticalVelocity = -_locomotion.GroundStickSpeed; verticalMove = _verticalVelocity * dt;
                // Snap to the measured support instead of repeatedly driving a full skin-width
                // into it. The latter makes a resized crouch capsule pop back out by 3 cm.
                if (supported && wasGrounded) verticalMove = -GroundGap(support) - .001f;
            }
            else { verticalMove = _verticalVelocity * dt + .5f * _config.Gravity * dt * dt; _verticalVelocity += _config.Gravity * dt; }
            Vector3 before = transform.position;
            CollisionFlags flags = _controller.Move(planar * dt + Vector3.up * verticalMove);
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;
            bool nowGrounded = (flags & CollisionFlags.Below) != 0 && _verticalVelocity <= 0f;
            if (!nowGrounded && _verticalVelocity <= 0f && ProbeGround(out support) && GroundGap(support) <= .045f) nowGrounded = true;
            if (!wasGrounded && nowGrounded) { _landSerial++; _needsHarvestRelease = true; }
            if (!nowGrounded) _needsHarvestRelease = true;
            _grounded = nowGrounded; if (nowGrounded) _jumping = false;
            _actualLocalVelocity = transform.InverseTransformDirection((transform.position - before) / dt);
        }

        private Vector3 Feet => transform.TransformPoint(_controller.center) - transform.up * (_controller.height * .5f);
        private float GroundGap(RaycastHit hit) => Vector3.Dot(Feet - hit.point, transform.up);
        private bool ProbeGround(out RaycastHit best)
        {
            best = default; float nearest = float.PositiveInfinity;
            Vector3 feet = Feet; float radius = Mathf.Max(.05f, _controller.radius * .85f);
            int count = Physics.SphereCastNonAlloc(feet + Vector3.up * (radius + .08f), radius, Vector3.down,
                _groundHits, .08f + _locomotion.GroundSnap, _locomotion.GroundLayers, QueryTriggerInteraction.Ignore);
            if (count == _groundHits.Length) return false;
            float normal = Mathf.Cos(_controller.slopeLimit * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                var h = _groundHits[i]; if (h.collider == null || h.transform == transform || h.transform.IsChildOf(transform)
                    || h.normal.y < normal || h.distance >= nearest) continue;
                best = h; nearest = h.distance;
            }
            return !float.IsPositiveInfinity(nearest);
        }
        private bool CanStand()
        {
            Vector3 center = transform.TransformPoint(_standingCenter);
            float radius = Mathf.Max(.01f, _controller.radius - _controller.skinWidth - .005f);
            // Shrink the whole capsule, including its bottom, by the skin margin. Extending the
            // segment to compensate for the smaller radius would let the ground veto every getup.
            float half = Mathf.Max(0f, _standingHeight * .5f - _controller.radius);
            int count = Physics.OverlapCapsuleNonAlloc(center + transform.up * half, center - transform.up * half,
                radius, _standHits, _locomotion.GroundLayers, QueryTriggerInteraction.Ignore);
            if (count == _standHits.Length) return false;
            for (int i = 0; i < count; i++) if (_standHits[i] != null && _standHits[i].transform != transform
                && !_standHits[i].transform.IsChildOf(transform)) return false;
            return true;
        }
        private void ApplyPostureShape()
        {
            float height = Mathf.Lerp(Mathf.Lerp(_standingHeight, Mathf.Max(_controller.radius * 2f, _locomotion.CrouchingHeight), _crouch), Mathf.Max(_controller.radius * 2f, _locomotion.SittingHeight), _posture);
            Vector3 center = _standingCenter + Vector3.up * ((height - _standingHeight) * .5f);
            // Reassigning collider dimensions during every walking frame can rebuild native contacts.
            // Only posture changes write them; an upright motor leaves the controller shape alone.
            if (Mathf.Abs(_controller.height - height) > .00001f || (_controller.center - center).sqrMagnitude > .000000001f)
            {
                // Change the capsule as one shape. Updating height while the old centre is live
                // momentarily changes the feet and invalidates the controller's ground contacts.
                bool active = _controller.enabled;
                _controller.enabled = false;
                _controller.height = height; _controller.center = center;
                _controller.enabled = active; _shapeModified = true;
            }
            if (_cameraPivot != null)
            {
                Vector3 eye = Vector3.Lerp(Vector3.Lerp(_standingEye, new Vector3(_standingEye.x, _locomotion.CrouchingEyeHeight, _standingEye.z), _crouch), new Vector3(_standingEye.x, _locomotion.SittingEyeHeight, _standingEye.z), _posture);
                if ((_cameraPivot.localPosition - eye).sqrMagnitude > .000000001f) { _cameraPivot.localPosition = eye; _shapeModified = true; }
            }
        }
    }
}
