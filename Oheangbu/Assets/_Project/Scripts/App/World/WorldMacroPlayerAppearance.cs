using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>
    /// Presentation-only adapter for the macro playtest's existing combat player. It reads movement
    /// and drawing state; it never writes gameplay movement, camera input, recognition, or combat.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed partial class WorldMacroPlayerAppearance : MonoBehaviour
    {
        public const string PlanarSpeedParameter = "PlanarSpeed";

        [SerializeField] private WorldMacroPlayerAppearanceProfile _profile;
        [SerializeField] private Animator _animator;
        [SerializeField] private CharacterController _controller;
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private DrawingInputController _drawing;
        [SerializeField] private CameraRigController _cameraRig;
        [SerializeField] private BrushStrokeFeedAdapter _brushFeed;
        [SerializeField] private WorldMacroPlayerGestureRig _gestureRig;
        [SerializeField] private Renderer[] _worldRenderers = Array.Empty<Renderer>();

        private readonly List<BlinkBinding> _blinkBindings = new List<BlinkBinding>(2);
        private float _planarSpeed;
        private float _nextBlink;
        private float _blinkStarted = -1f;
        private bool _drawingActive;
        private int _jumpSerial, _landSerial;
        private bool _wasDodging;
        private bool _wasMoving;
        private float _dodgeTail = -1f;   // #300 seconds into the dodge recovery tail; < 0 = none

        // #300 presentation only: the 0.25 s dash plays the moving span of the dodge clip (not the whole clip squeezed),
        // then the landing/recovery plays out near its authored speed. Moving input hands over early; the controller's
        // Dodge -> Locomotion cross-fade finishes the blend. Dash distance, time and immunity are untouched.
        private void DodgePresentation(Vector3 local, ref float time, ref bool state)
        {
            Vector2 span = _profile.DodgeDashSpan;
            if (_motor.IsRolling) { _dodgeTail = -1f; return; }
            if (_motor.IsDodging)
            {
                _dodgeTail = 0f;
                time = Mathf.Lerp(span.x, span.y, _motor.DodgeProgress);
                state = true;
                return;
            }
            if (_dodgeTail < 0f) return;
            _dodgeTail += Time.deltaTime;
            bool moving = local.x * local.x + local.z * local.z > _profile.DodgeTailCutSpeed * _profile.DodgeTailCutSpeed;
            float limit = moving ? Mathf.Min(_profile.DodgeTailMovingSeconds, _profile.DodgeTailSeconds) : _profile.DodgeTailSeconds;
            if (_dodgeTail >= limit || !_motor.IsLocomotionGrounded || _motor.IsDrawing || _motor.IsSitting || _motor.IsCrouching)
            {
                _dodgeTail = -1f;
                return;
            }
            time = Mathf.Lerp(span.y, 1f, _dodgeTail / _profile.DodgeTailSeconds);
            state = true;
        }

        private sealed class BlinkBinding
        {
            public SkinnedMeshRenderer Renderer;
            public int Index;
        }

        public WorldMacroPlayerAppearanceProfile Profile => _profile;
        public Animator Animator => _animator;
        public Renderer[] WorldRenderers => _worldRenderers;
        public float PlanarSpeed => _planarSpeed;
        public bool DrawingActive => _drawingActive;

        public void ConfigureGestureRig(WorldMacroPlayerGestureRig gestureRig) { _gestureRig = gestureRig; }

        public void Configure(WorldMacroPlayerAppearanceProfile profile, Animator animator,
            CharacterController controller, PlayerMotor motor, DrawingInputController drawing,
            CameraRigController cameraRig, BrushStrokeFeedAdapter brushFeed, Renderer[] worldRenderers)
        {
            _profile = profile;
            _animator = animator;
            _controller = controller;
            _motor = motor;
            _drawing = drawing;
            _cameraRig = cameraRig;
            _brushFeed = brushFeed;
            _worldRenderers = worldRenderers ?? Array.Empty<Renderer>();
            BindAnimator();
            FindBlinkBindings();
        }

        private void Awake()
        {
            if (_gestureRig == null) _gestureRig = GetComponent<WorldMacroPlayerGestureRig>();
            if (_animator == null) _animator = GetComponent<Animator>();
            if (_worldRenderers == null || _worldRenderers.Length == 0)
                _worldRenderers = GetComponentsInChildren<Renderer>(true);
            BindAnimator();
            FindBlinkBindings();
        }

        private void OnEnable()
        {
            BindAnimator();
            FindBlinkBindings();
            _nextBlink = Time.time + InitialBlinkDelay();
        }

        private void BindAnimator()
        {
            if (_animator == null) return;
            if (_profile != null && _profile.Controller != null
                && _animator.runtimeAnimatorController != _profile.Controller)
                _animator.runtimeAnimatorController = _profile.Controller;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void Update()
        {
            RestoreLookBones();
            Vector3 velocity = _controller != null ? _controller.velocity : Vector3.zero;
            velocity.y = 0f;
            float targetSpeed = velocity.magnitude;
            float damping = _profile != null ? _profile.VelocityDamping : 12f;
            float blend = damping <= 0f ? 1f : 1f - Mathf.Exp(-damping * Mathf.Max(0f, Time.deltaTime));
            _planarSpeed = Mathf.Lerp(_planarSpeed, targetSpeed, blend);
            _drawingActive = _motor != null ? _motor.IsDrawing : _drawing != null && _drawing.InDrawMode;

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.applyRootMotion = false;
                _animator.SetFloat(PlanarSpeedParameter, _planarSpeed);
                if (_profile != null && _profile.DirectionalLocomotion && _motor != null && _motor.HasLocomotion)
                {
                    Vector3 local = _motor.ActualLocalVelocity;
                    _animator.SetFloat("MoveX", local.x); _animator.SetFloat("MoveZ", local.z);
                    _animator.SetBool("Grounded", _motor.IsLocomotionGrounded);
                    _animator.SetFloat("VerticalSpeed", _motor.VerticalVelocity);
                    if (_profile.NaturalLocomotion)
                    {
                        float launch = Mathf.Max(.1f, _motor.JumpLaunchSpeed);
                        _animator.SetFloat("JumpRiseProgress", Mathf.Clamp01(1f - _motor.VerticalVelocity / launch));
                        _animator.SetFloat("JumpFallProgress", Mathf.Clamp01(-_motor.VerticalVelocity / launch));
                    }
                    _animator.SetFloat("SitAmount", _motor.Posture01);
                    _animator.SetFloat("StandAmount", 1f - _motor.Posture01);
                    float dodgeTime = _motor.DodgeProgress; bool dodgeState = _motor.IsDodging;
                    if (_profile.DodgeRecoveryTail) DodgePresentation(local, ref dodgeTime, ref dodgeState);
                    _animator.SetFloat("DodgeProgress", dodgeTime);
                    _animator.SetBool("Sitting", _motor.IsSitting);
                    _animator.SetBool("SitRequested", _motor.SitRequested);
                    _animator.SetBool("Dodging", dodgeState);
                    if (_profile.IdleLookTurn) _animator.SetBool("Rolling", _motor.IsRolling);
                    Vector3 dodge = _motor.DodgeLocalDirection;
                    _animator.SetFloat("DodgeX", dodge.x); _animator.SetFloat("DodgeZ", dodge.z);
                    if (_motor.JumpSerial != _jumpSerial) { _jumpSerial = _motor.JumpSerial; _animator.SetTrigger("Jump"); }
                    if (_motor.LandingSerial != _landSerial) { _landSerial = _motor.LandingSerial; _animator.SetTrigger("Land"); }
                    if (_motor.IsDodging && !_wasDodging)
                    {
                        _animator.ResetTrigger("Dodge");
                        if (!_motor.IsRolling) _animator.SetTrigger("Dodge");
                    }
                    _wasDodging = _motor.IsDodging;
                    if (_profile.RecoveryMotions)
                    {
                        _animator.SetBool("Crouching", _motor.IsCrouching); _animator.SetFloat("CrouchAmount", _motor.Crouch01);
                        _animator.SetFloat("TurnSpeed", _motor.ActualYawSpeed);
                        bool moving = local.x*local.x+local.z*local.z > .04f;
                        if(moving&&!_wasMoving&&local.z>.1f&&Mathf.Abs(local.x)<.1f&&!_motor.IsCrouching&&!_motor.IsSprinting&&!_motor.IsDrawing&&!_motor.IsSitting)_animator.SetTrigger("StartWalk");
                        _wasMoving=moving;
                        UpdateLookTurn(moving);
                    }
                }
            }
        }

        // The selected C02 rig has shoulder, arm, forearm, and hand bones but no finger bones.
        // This small Humanoid IK pass gives the world-body shadow/shoulder a drawing direction;
        // it deliberately does not claim a closed brush grip.
        private void OnAnimatorIK(int layerIndex)
        {
            if (_animator == null || !_animator.isHuman) return;
            if (_gestureRig != null && _gestureRig.IsPresentationReady)
            {
                // The rebuilt hand/brush pass evaluates after the final ink projection.
                _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
                return;
            }
            float weight = _drawingActive && _profile != null ? _profile.DrawingArmWeight : 0f;
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
            if (weight <= 0f || _brushFeed == null
                || !_brushFeed.TryGetVisualPointer(out Camera camera, out _, out Vector3 inkPoint)) return;

            Transform shoulder = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (shoulder == null || camera == null) return;
            Vector3 target = inkPoint - camera.transform.forward * .08f - camera.transform.up * .04f;
            Vector3 reach = target - shoulder.position;
            float maximum = Mathf.Max(.1f, _profile.DrawingReach);
            if (reach.sqrMagnitude > maximum * maximum) target = shoulder.position + reach.normalized * maximum;

            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
            _animator.SetIKPosition(AvatarIKGoal.RightHand, target);
            _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, weight * .55f);
            _animator.SetIKHintPosition(AvatarIKHint.RightElbow,
                shoulder.position + camera.transform.right * .24f - camera.transform.up * .18f);
        }

        private void LateUpdate()
        {
            ApplyLookBones();
            if (_profile == null || _blinkBindings.Count == 0) return;
            float now = Time.time;
            if (_blinkStarted < 0f && now >= _nextBlink) _blinkStarted = now;
            float weight = 0f;
            if (_blinkStarted >= 0f)
            {
                float duration = Mathf.Max(.05f, _profile.BlinkDuration);
                float phase = (now - _blinkStarted) / duration;
                if (phase >= 1f)
                {
                    _blinkStarted = -1f;
                    _nextBlink = now + Mathf.Max(.25f, _profile.BlinkInterval);
                }
                else weight = Mathf.Sin(Mathf.Clamp01(phase) * Mathf.PI) * 100f;
            }
            SetBlink(weight);
        }

        private void OnDisable()
        {
            RestoreLookBones(); _lookInitialized = false;
            _drawingActive = false;
            SetBlink(0f);
        }

        private float InitialBlinkDelay()
        {
            float interval = _profile != null ? Mathf.Max(.25f, _profile.BlinkInterval) : 4.2f;
            return interval * (.75f + Mathf.Repeat(Mathf.Abs(GetInstanceID()) * .6180339f, .5f));
        }

        private void FindBlinkBindings()
        {
            _blinkBindings.Clear();
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    if (name == "BlinkLeft" || name == "BlinkRight")
                        _blinkBindings.Add(new BlinkBinding { Renderer = renderer, Index = i });
                }
            }
        }

        private void SetBlink(float weight)
        {
            for (int i = 0; i < _blinkBindings.Count; i++)
            {
                var binding = _blinkBindings[i];
                if (binding.Renderer != null) binding.Renderer.SetBlendShapeWeight(binding.Index, weight);
            }
        }
    }
}
