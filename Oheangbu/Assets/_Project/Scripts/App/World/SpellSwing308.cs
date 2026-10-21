using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Data.Spell;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App.World
{
    // SPEC-SPELL-SWING-308 sections 5 / 5a (D308-80b): the body's side of the great brush.
    //  - Two idle stances on the upper-body Animator layer: shoulder carry <-> combat stance. Stance comes at once with a draw
    //    mode, a swing (every category, so a parry too), a hit taken, a dodge, a lock-on or a chasing enemy nearby; carry comes
    //    back after RelaxAfterSeconds with none of them.
    //  - The swing motion of a cast: started by CombatLoopWiring.SwingBegan308, timed so the clip's apex falls on the rule's apex
    //    (the rule's clock is the truth; this only shows it), cut by a fizzle, a dodge or a new draw mode.
    //  - Tells WorldMacroPlayerGestureRig who owns the arms (the Animator outside the draw mode), how much the left hand is on the
    //    shaft and which grip the right hand holds.
    // Presentation only: it listens, plays Animator states and sets layer weights. The swing rule (apex, lock, fizzle) lives in
    // CombatLoopWiring.Swing308 and never reads this component. No profile in Resources = this component does nothing.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(130)]   // Update after the appearance (100) and the hit reactions (120); the gesture rig reads it in LateUpdate (1000)
    public sealed class SpellSwing308 : MonoBehaviour
    {
        public enum StanceState { Carry, ToStance, Stance, ToCarry }

        [Tooltip("비우면 Resources \"Swing308/GreatBrush308Profile\". 그것도 없으면 아무것도 하지 않는다.")]
        public GreatBrush308Profile Profile;
        public Animator Animator;
        [Tooltip("쫓는 적을 찾는 곳. 비우면 한 번 찾아본다(없으면 쫓는 적 조건만 빠진다).")]
        public WorldMacroPlaytestSession Session;

        private WorldMacroPlayerGestureRig _rig;
        private WorldMacroPlayerReaction303 _reaction;
        private PlayerMotor _motor;
        private DrawingInputController _drawing, _subscribedDrawing;
        private CombatLoopWiring _subscribedWiring;
        private PlayerVitals _subscribedVitals;
        private LockOn _lockOn;
        private bool _looked, _sessionLooked;

        private Animator _boundAnimator;
        private RuntimeAnimatorController _boundController;
        private int _layer = -1, _bodyLayer = -1, _torsoLayer = -1, _leftLayer = -1, _timeHash, _shiftHash;
        private float _torso;
        private bool _hasTime, _hasShift;

        private StanceState _stance = StanceState.Carry;
        private float _shiftStarted, _lastCombatAt = float.NegativeInfinity, _threatCheckAt;
        private bool _chased, _wasDodging, _started;

        // the one motion that is playing (swing, guard, buff), if any
        private bool _swinging, _holding;
        private int _entry = -1;                 // index into Profile.States of the state shown on the layer (idle states included)
        private float _stateStarted, _swingReleasedAt, _swingApexAt, _swingLockUntil, _holdUntil, _clipTime;
        private int _swingFrame = -1;
        // D308-84: the clip starts this far in when the category does not wait (the ward's short slam); the aim of this swing
        private float _clipStart;
        private bool _aimOn;
        private Vector3 _aimPoint;
        private float _aimWeight;

        private float _upper, _body, _left;

        /// <summary>The great brush presentation is running (profile, layer and a bound great brush).</summary>
        public bool Active { get; private set; }
        /// <summary>The Animator's pose is the arms' pose: true whenever the great brush is active and the draw mode is off.</summary>
        public bool OwnsArms => Active && !(_drawing != null && _drawing.InDrawMode);
        /// <summary>0..1: how much the left hand holds the shaft now (the state's curve, eased, times the layer's weight).</summary>
        public float LeftHandOnShaft => Active ? _left * _upper * (1f - Mathf.SmoothStep(0f, 1f, BackBlend)) : 0f;
        /// <summary>0 = shoulder carry, 1 = combat stance (eased over the transition).</summary>
        public float StanceBlend { get; private set; }
        /// <summary>0 = in the hand, 1 = slung on the back (sprinting out of combat; the rig also slings it in a seat). Arms are free at 1.</summary>
        public float BackBlend { get; private set; }
        private float _sprintSince = -1f, _sprintEndedAt = -100f; private bool _backWanted;
        public StanceState Stance => _stance;
        public bool Swinging => _swinging || _holding;
        /// <summary>A swing began in this frame (the rig asks inside the same commit: no close-up follow-through then).</summary>
        public bool SwingBeganThisFrame => _swingFrame == Time.frameCount;
        public string State => Profile != null && _entry >= 0 && _entry < Profile.States.Length ? Profile.States[_entry].State : null;
        public float UpperWeight => _upper;
        public float BodyWeight => _body;
        public float ClipSeconds => _clipTime;
        public int Swings { get; private set; }
        public int Cuts { get; private set; }

        /// <summary>The right hand's grip now (GripSocket in the hand bone's local space): carry and two-hand blended by the stance.</summary>
        public GreatBrush308Profile.Grip CurrentGrip
        {
            get
            {
                var p = Profile;
                if (p == null) return new GreatBrush308Profile.Grip { Rotation = Quaternion.identity };
                float t = Mathf.SmoothStep(0f, 1f, StanceBlend);
                return new GreatBrush308Profile.Grip
                {
                    Position = Vector3.Lerp(p.CarryGrip.Position, p.TwoHandGrip.Position, t),
                    Rotation = Quaternion.Slerp(p.CarryGrip.Rotation, p.TwoHandGrip.Rotation, t)
                };
            }
        }

        private void OnEnable() { _started = false; _boundAnimator = null; }

        private void OnDisable()
        {
            Unsubscribe();
            ReleaseLayers();
            Active = false; _swinging = _holding = false; _upper = _body = _left = 0f; _started = false;
        }

        private GreatBrush308Profile ResolveProfile()
        {
            if (Profile == null && !_looked) { _looked = true; Profile = Resources.Load<GreatBrush308Profile>(GreatBrush308Profile.ResourcesPath); }
            return Profile != null && Profile.IsValid ? Profile : null;
        }

        // Everything is found on the player the way the neighbouring components find it; nothing is global.
        private bool Bind(GreatBrush308Profile profile)
        {
            if (_rig == null) _rig = GetComponent<WorldMacroPlayerGestureRig>();
            if (_rig == null || !_rig.GreatBrushBound308) return false;
            if (Animator == null) Animator = GetComponent<Animator>();
            if (Animator == null || Animator.runtimeAnimatorController == null) return false;
            if (_reaction == null) _reaction = GetComponent<WorldMacroPlayerReaction303>();
            _motor = _rig.Motor308; _drawing = _rig.Drawing308;
            if (_motor == null) return false;
            if (_lockOn == null) _lockOn = _motor.GetComponent<LockOn>();
            if (Session == null && !_sessionLooked)
            {
                _sessionLooked = true;
                var lean = GetComponent<WorldMacroPlayerLean303>();
                Session = lean != null && lean.Session != null ? lean.Session : FindFirstObjectByType<WorldMacroPlaytestSession>();
            }
            if (_boundAnimator != Animator || _boundController != Animator.runtimeAnimatorController)
            {
                _boundAnimator = Animator; _boundController = Animator.runtimeAnimatorController;
                _layer = Animator.GetLayerIndex(profile.Layer);
                _bodyLayer = string.IsNullOrEmpty(profile.BodyLayer) ? -1 : Animator.GetLayerIndex(profile.BodyLayer);
                _torsoLayer = string.IsNullOrEmpty(profile.TorsoLayer) ? -1 : Animator.GetLayerIndex(profile.TorsoLayer);
                _leftLayer = string.IsNullOrEmpty(profile.LeftArmLayer) ? -1 : Animator.GetLayerIndex(profile.LeftArmLayer);
                _timeHash = Animator.StringToHash(profile.TimeParameter ?? ""); _shiftHash = Animator.StringToHash(profile.ShiftParameter ?? "");
                _hasTime = _hasShift = false;
                foreach (var parameter in Animator.parameters)
                {
                    if (parameter.type != AnimatorControllerParameterType.Float) continue;
                    if (parameter.nameHash == _timeHash) _hasTime = true;
                    if (parameter.nameHash == _shiftHash) _hasShift = true;
                }
                _started = false;
            }
            if (_layer < 0) return false;
            Subscribe();
            return true;
        }

        private void Subscribe()
        {
            if (_subscribedDrawing != _drawing)
            {
                if (_subscribedDrawing != null) { _subscribedDrawing.ModeEntered -= OnModeEntered; _subscribedDrawing.ModeExited -= OnModeExited; }
                _subscribedDrawing = _drawing;
                if (_drawing != null) { _drawing.ModeEntered += OnModeEntered; _drawing.ModeExited += OnModeExited; }
            }
            var walker = _rig.Walker308;
            CombatLoopWiring wiring = walker != null ? walker.Wiring : null;   // may be wired after this component woke: looked at every frame
            if (!ReferenceEquals(wiring, _subscribedWiring))
            {
                if (!ReferenceEquals(_subscribedWiring, null))
                {
                    _subscribedWiring.SwingBegan308 -= OnSwingBegan; _subscribedWiring.SwingFizzled308 -= OnSwingFizzled;
                }
                _subscribedWiring = wiring;
                if (wiring != null) { wiring.SwingBegan308 += OnSwingBegan; wiring.SwingFizzled308 += OnSwingFizzled; }
            }
            PlayerVitals vitals = _motor != null ? _motor.GetComponent<PlayerVitals>() : null;
            if (!ReferenceEquals(vitals, _subscribedVitals))
            {
                if (!ReferenceEquals(_subscribedVitals, null)) _subscribedVitals.Damaged -= OnDamaged;
                _subscribedVitals = vitals;
                if (vitals != null) vitals.Damaged += OnDamaged;
            }
        }

        private void Unsubscribe()
        {
            if (_subscribedDrawing != null) { _subscribedDrawing.ModeEntered -= OnModeEntered; _subscribedDrawing.ModeExited -= OnModeExited; }
            _subscribedDrawing = null;
            if (!ReferenceEquals(_subscribedWiring, null))
            {
                _subscribedWiring.SwingBegan308 -= OnSwingBegan; _subscribedWiring.SwingFizzled308 -= OnSwingFizzled;
            }
            _subscribedWiring = null;
            if (!ReferenceEquals(_subscribedVitals, null)) _subscribedVitals.Damaged -= OnDamaged;
            _subscribedVitals = null;
        }

        private void ReleaseLayers()
        {
            if (_boundAnimator == null) return;
            if (_layer >= 0 && _layer < _boundAnimator.layerCount) _boundAnimator.SetLayerWeight(_layer, 0f);
            if (_bodyLayer >= 0 && _bodyLayer < _boundAnimator.layerCount) _boundAnimator.SetLayerWeight(_bodyLayer, 0f);
            if (_torsoLayer >= 0 && _torsoLayer < _boundAnimator.layerCount) _boundAnimator.SetLayerWeight(_torsoLayer, 0f);
            if (_leftLayer >= 0 && _leftLayer < _boundAnimator.layerCount) _boundAnimator.SetLayerWeight(_leftLayer, 0f);
        }

        // ---------------------------------------------------------------- events (all of them only note a time or start a state)

        private void Touch() { _lastCombatAt = Time.time; }
        private void OnModeEntered() { Touch(); if (Active) Cut(); }
        private void OnDamaged(float amount) { Touch(); }
        // The draw mode ends and the world body is shown again on this very frame: the brush layer is there at once, not faded in
        // over the arm of the locomotion layer (the change happens while the body was hidden behind the close-up).
        private void OnModeExited() { Touch(); ShowUpperNow(); }
        private void ShowUpperNow()
        {
            if (!Active || _layer < 0 || Animator == null || _motor == null) return;
            bool lying = _reaction != null && (_reaction.Lying || _reaction.GettingUp);
            if (lying || _motor.IsDodging || _motor.IsSitting || !_motor.enabled) return;
            _upper = 1f; Animator.SetLayerWeight(_layer, 1f);
        }
        private void OnSwingFizzled(SpellCast cast) { Touch(); if (Active) Cut(); }

        // Raised inside the letter's dispatch (a gameplay event): fenced, so a fault here can never cut the cast short.
        private void OnSwingBegan(SpellCast cast, string state, float apexTime, float lockUntil)
        {
            try
            {
                Touch();
                _swingFrame = Time.frameCount;
                if (!Active || Profile == null) return;
                int index = Playable(state);
                // D308-84: a position variant the controller does not have yet falls back to the category's own motion
                if (index < 0 && _subscribedWiring != null) index = Playable(_subscribedWiring.SwingBaseState308);
                if (index < 0) return;
                EnterStance();
                ShowUpperNow();   // released from the close-up in this frame: the motion starts at full weight
                _swinging = true; _holding = false; Swings++;
                _swingReleasedAt = Time.time; _swingApexAt = apexTime; _swingLockUntil = lockUntil;
                var shown = Profile.States[index];
                float lead = _subscribedWiring != null ? _subscribedWiring.SwingClipLead308 : 0f;
                _clipStart = apexTime <= Time.time && lead > 0f && shown.ClipApexSeconds > 0f ? Mathf.Max(0f, shown.ClipApexSeconds - lead) : 0f;
                Show(index, Profile.SwingFadeIn, true);
                SetTime(_timeHash, _hasTime, shown.ClipSeconds > 0f ? Mathf.Clamp01(_clipStart / shown.ClipSeconds) : 0f);
                // the aim: only toward a target (a free-aim point has the player's own height), only for a cast that waits for its apex
                _aimOn = _subscribedWiring != null && _subscribedWiring.SwingAimHasTarget308 && apexTime > Time.time;
                _aimPoint = _aimOn ? _subscribedWiring.SwingAim308 : default;
            }
            catch (System.Exception e) { Debug.LogException(e, this); }
        }

        // D308-127 (TEST, debate 13): a weapon-form strike shows a swing. Presentation only - called by the weapon head presenter
        // on the strike's Hit / Miss cue; the rule judged the strike on the click and never reads this. The clip starts toApex
        // seconds before its apex and runs at its own speed (the wind-up is not shown: the afterimage is the head's path after
        // the contact, and pressing the wind-up into two frames would draw it as one long streak).
        public bool PlayStrike308(string state, float toApex, float lockSeconds)
        {
            try
            {
                Touch();
                _swingFrame = Time.frameCount;
                if (!Active || Profile == null) return false;
                int index = Playable(state);
                if (index < 0) return false;
                EnterStance();
                ShowUpperNow();
                _swinging = true; _holding = false; Swings++;
                float now = Time.time; var shown = Profile.States[index];
                // apex time = now: StepSwing then runs the clip from _clipStart at its own speed
                _swingReleasedAt = now; _swingApexAt = now; _swingLockUntil = now + Mathf.Max(0f, lockSeconds);
                _clipStart = Mathf.Max(0f, shown.ClipApexSeconds - Mathf.Max(0f, toApex)); _aimOn = false;
                Show(index, Profile.SwingFadeIn, true);
                SetTime(_timeHash, _hasTime, shown.ClipSeconds > 0f ? Mathf.Clamp01(_clipStart / shown.ClipSeconds) : 0f);
                return true;
            }
            catch (System.Exception e) { Debug.LogException(e, this); return false; }
        }

        private int Playable(string state)
        {
            if (string.IsNullOrEmpty(state)) return -1;
            int index = Profile.Find(Profile.Resolve(state));
            return index >= 0 && Profile.States[index].ClipSeconds > 0f && Has(_layer, Profile.States[index].State) ? index : -1;
        }

        // D308-84 (SPEC section 5c): the upper body turns a little more toward a high, low or off-centre target around the apex.
        // After the Animator, before the gesture rig (1000) puts the brush in the hand. Bones only - the root, PlayerForward() and the
        // rule never move. Weight rises over AimInSeconds before the apex and falls to zero at the lock's end.
        private void LateUpdate()
        {
            _aimWeight = 0f;
            if (!_aimOn || !_swinging || !Active || Animator == null || !Animator.isHuman || _motor == null || _subscribedWiring == null) return;
            if (_drawing != null && _drawing.InDrawMode) return;
            var swing = _subscribedWiring.SwingProfile308;
            if (swing == null || !swing.Position.Enabled) return;
            var rule = swing.Position;
            float now = Time.time;
            float rise = Mathf.Clamp01(1f - (_swingApexAt - now) / rule.AimInSeconds);
            float fall = now <= _swingApexAt ? 1f : Mathf.Clamp01((_swingLockUntil - now) / Mathf.Max(.01f, _swingLockUntil - _swingApexAt));
            float w = Mathf.SmoothStep(0f, 1f, Mathf.Min(rise, fall)) * _upper;
            if (w <= .001f) return;
            Transform spine = Animator.GetBoneTransform(HumanBodyBones.Spine), chest = Animator.GetBoneTransform(HumanBodyBones.Chest);
            Transform upper = Animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (spine == null || chest == null) return;
            Transform top = upper != null ? upper : chest;
            Transform body = _motor.transform;
            Vector3 to = _aimPoint - top.position;
            Vector3 flat = Vector3.ProjectOnPlane(to, Vector3.up);
            if (flat.sqrMagnitude < .01f) return;
            float pitch = Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg;
            if (Mathf.Abs(pitch) < rule.AimDeadbandDegrees) pitch = 0f;
            pitch = Mathf.Clamp(pitch, -rule.AimPitchDown, rule.AimPitchUp);
            float yaw = Mathf.Clamp(Vector3.SignedAngle(Vector3.ProjectOnPlane(body.forward, Vector3.up), flat, Vector3.up), -rule.AimYaw, rule.AimYaw);
            if (Mathf.Abs(pitch) < .01f && Mathf.Abs(yaw) < .01f) return;
            _aimWeight = w;
            // up = lean back: a positive angle about the body's right axis tips the chest forward and down
            Quaternion turn = Quaternion.AngleAxis(yaw * w, Vector3.up) * Quaternion.AngleAxis(-pitch * w, body.right);
            spine.rotation = Quaternion.Slerp(Quaternion.identity, turn, .4f) * spine.rotation;
            chest.rotation = Quaternion.Slerp(Quaternion.identity, turn, upper != null ? .35f : .6f) * chest.rotation;
            if (upper != null) upper.rotation = Quaternion.Slerp(Quaternion.identity, turn, .25f) * upper.rotation;
        }

        /// <summary>Read-only: the upper-body aim's weight in the last frame (0 = none).</summary>
        public float AimWeight => _aimWeight;

        // ---------------------------------------------------------------- frame

        private void Update()
        {
            var profile = ResolveProfile();
            bool ready = profile != null && Bind(profile);
            if (!ready)
            {
                if (Active) { ReleaseLayers(); Active = false; _swinging = _holding = false; _upper = _body = _left = 0f; _started = false; }
                return;
            }
            Active = true;
            float now = Time.time, dt = Time.deltaTime;
            bool drawing = _drawing != null && _drawing.InDrawMode;
            bool dodging = _motor.IsDodging;
            bool dodgeStarted = dodging && !_wasDodging; _wasDodging = dodging;
            bool locked = _lockOn != null && _lockOn.IsLocked;
            if (now >= _threatCheckAt) { _threatCheckAt = now + profile.ThreatPollSeconds; _chased = Chased(profile); }
            if (drawing || dodging || locked || _chased || Swinging) Touch();
            if (dodgeStarted && Swinging) Cut();   // SPEC 3: the dodge ends the motion (the rule itself decides about the cast)

            if (!_started)
            {
                _started = true; _stance = StanceState.Carry; StanceBlend = 0f; _swinging = _holding = false;
                Show(profile.Find(profile.CarryIdle), 0f, false);
            }

            StepSwing(profile, now);
            StepStance(profile, now);

            // layer weights: the hit reaction, the ride, the seat and the dodge keep their own bodies
            bool lying = _reaction != null && (_reaction.Lying || _reaction.GettingUp);
            bool suppressed = lying || dodging || _motor.IsSitting || !_motor.enabled;
            float upperTarget = suppressed ? 0f : drawing ? profile.DrawLayerWeight : 1f;
            _upper = Mathf.MoveTowards(_upper, upperTarget, dt / Mathf.Max(.01f, profile.LayerFade));
            Vector3 local = _motor.ActualLocalVelocity;
            bool standing = local.x * local.x + local.z * local.z < profile.StandingSpeed * profile.StandingSpeed && _motor.IsLocomotionGrounded;
            float bodyTarget = Swinging && standing && !suppressed && !drawing ? 1f : 0f;
            _body = Mathf.MoveTowards(_body, bodyTarget, dt / Mathf.Max(.01f, profile.BodyFade));
            // on the back while sprinting out of combat: both arms go back to the run (user request 2026-10-13)
            bool sprinting = _motor.IsSprinting && !suppressed;
            if (sprinting) { if (_sprintSince < 0f) _sprintSince = now; } else { if (_sprintSince >= 0f) _sprintEndedAt = now; _sprintSince = -1f; }
            bool calm = _stance == StanceState.Carry && !Swinging && !drawing;
            if (!calm) _backWanted = false;
            else if (sprinting && now - _sprintSince >= profile.BackAfterSeconds) _backWanted = true;
            else if (!sprinting && now - _sprintEndedAt >= profile.BackReleaseSeconds) _backWanted = false;
            BackBlend = Mathf.MoveTowards(BackBlend, _backWanted ? 1f : 0f, dt / Mathf.Max(.05f, profile.BackSeconds));
            float inHand = 1f - Mathf.SmoothStep(0f, 1f, BackBlend);
            Animator.SetLayerWeight(_layer, _upper * inHand);
            // the left arm: the brush motions' in the stance and in a swing; in the shoulder carry it is free and swings with the walk
            if (_leftLayer >= 0) Animator.SetLayerWeight(_leftLayer, _upper * inHand * (Swinging ? 1f : Mathf.SmoothStep(0f, 1f, StanceBlend)));
            // the torso: the clip's while standing or swinging, the walk's own sway while moving (user 2026-10-13: the upper body
            // was rigid when walking with the brush on the shoulder or in the stance)
            if (_torsoLayer >= 0)
            {
                float torsoTarget = Swinging ? 1f : standing ? profile.TorsoWeightStanding : profile.TorsoWeightMoving;
                _torso = Mathf.MoveTowards(_torso, torsoTarget, dt / Mathf.Max(.01f, profile.TorsoFade));
                Animator.SetLayerWeight(_torsoLayer, _upper * inHand * _torso);
            }
            if (_bodyLayer >= 0) Animator.SetLayerWeight(_bodyLayer, _body);

            // the left hand: the shown state's curve at its clip time, eased
            float leftTarget = 0f;
            if (_entry >= 0 && _entry < profile.States.Length)
            {
                var entry = profile.States[_entry];
                bool shifting = !Swinging && (_stance == StanceState.ToStance && entry.State == profile.ToStance
                    || _stance == StanceState.ToCarry && entry.State == profile.ToCarry);
                float t = _swinging ? _clipTime
                    : shifting ? (_stance == StanceState.ToStance ? StanceBlend : 1f - StanceBlend) * entry.ClipSeconds   // the transition clip is stretched over its seconds
                    : now - _stateStarted;
                if (entry.Loop && entry.ClipSeconds > 0f) t = Mathf.Repeat(t, entry.ClipSeconds);
                leftTarget = GreatBrush308Profile.LeftHandAt(entry, t);
            }
            _left = Mathf.MoveTowards(_left, leftTarget, dt / Mathf.Max(.01f, profile.LeftHandBlendSeconds));
        }

        private void StepSwing(GreatBrush308Profile profile, float now)
        {
            if (_swinging)
            {
                var entry = profile.States[_entry];
                float elapsed = now - _swingReleasedAt, toApex = _swingApexAt - _swingReleasedAt;
                // the clip's apex is put on the rule's apex; after it the clip runs at its own speed
                _clipTime = toApex > .0001f && entry.ClipApexSeconds > 0f
                    ? elapsed < toApex ? entry.ClipApexSeconds * (elapsed / toApex) : entry.ClipApexSeconds + (elapsed - toApex) * profile.PostApexSpeed
                    : _clipStart + elapsed;   // a category that does not wait runs at the clip's own speed (from _clipStart: D308-84 ward)
                SetTime(_timeHash, _hasTime, Mathf.Clamp01(_clipTime / entry.ClipSeconds));
                int next = profile.Find(entry.Next);
                bool chained = next >= 0 && Has(_layer, profile.States[next].State);
                float fadeOut = chained ? Mathf.Min(profile.IdleFade, entry.ClipSeconds * .5f) : profile.SwingFadeOut;
                if (_clipTime < entry.ClipSeconds - fadeOut) return;
                _swinging = false;
                if (chained)
                {
                    _holding = true; _holdUntil = Mathf.Max(_swingLockUntil, now + profile.States[next].HoldSeconds);
                    Show(next, fadeOut, true);
                }
                else ShowIdle(profile, profile.SwingFadeOut);
            }
            else if (_holding && now >= _holdUntil)
            {
                _holding = false;
                ShowIdle(profile, profile.SwingFadeOut);
            }
        }

        private void StepStance(GreatBrush308Profile profile, float now)
        {
            bool combat = now - _lastCombatAt < profile.RelaxAfterSeconds;   // Touch() is called every frame a holding condition is true
            switch (_stance)
            {
                case StanceState.Carry:
                    StanceBlend = 0f;
                    if (combat) BeginShift(profile, StanceState.ToStance, now);
                    break;
                case StanceState.ToStance:
                {
                    float u = Mathf.Clamp01((now - _shiftStarted) / profile.ToStanceSeconds);
                    StanceBlend = u; SetTime(_shiftHash, _hasShift, u);
                    if (u >= 1f) { _stance = StanceState.Stance; if (!Swinging) ShowIdle(profile, profile.IdleFade); }
                    break;
                }
                case StanceState.Stance:
                    StanceBlend = 1f;
                    if (!combat && !Swinging) BeginShift(profile, StanceState.ToCarry, now);
                    break;
                case StanceState.ToCarry:
                {
                    if (combat)
                    {
                        // back to the stance from where the brush is now, not from the carry (PR #30 review): keep the blend it has
                        float keep = StanceBlend; BeginShift(profile, StanceState.ToStance, now); _shiftStarted = now - keep * profile.ToStanceSeconds; break;
                    }
                    float u = Mathf.Clamp01((now - _shiftStarted) / profile.ToCarrySeconds);
                    StanceBlend = 1f - u; SetTime(_shiftHash, _hasShift, u);
                    if (u >= 1f) { _stance = StanceState.Carry; ShowIdle(profile, profile.IdleFade); }
                    break;
                }
            }
        }

        private void BeginShift(GreatBrush308Profile profile, StanceState to, float now)
        {
            _stance = to; _shiftStarted = now;
            if (Swinging) return;   // the motion is on the layer; the stance only keeps its own clock
            int index = profile.Find(to == StanceState.ToStance ? profile.ToStance : profile.ToCarry);
            SetTime(_shiftHash, _hasShift, 0f);
            if (index >= 0 && profile.States[index].ClipSeconds > 0f && Has(_layer, profile.States[index].State)) Show(index, profile.CutFade, false);
            else   // no transition clip: a plain cross-fade between the two idles over the transition's length
                Show(profile.Find(to == StanceState.ToStance ? profile.StanceIdle : profile.CarryIdle),
                    to == StanceState.ToStance ? profile.ToStanceSeconds : profile.ToCarrySeconds, false);
        }

        // a swing needs the stance at once: the stance clock jumps, the motion itself covers the change
        private void EnterStance()
        {
            _stance = StanceState.Stance; StanceBlend = 1f;
        }

        private void Cut()
        {
            if (!Swinging || Profile == null) return;
            _swinging = _holding = false; Cuts++;
            EnterStance();
            ShowIdle(Profile, Profile.CutFade);
        }

        private void ShowIdle(GreatBrush308Profile profile, float fade)
        {
            bool carry = _stance == StanceState.Carry;
            int index = profile.Find(carry ? profile.CarryIdle : profile.StanceIdle);
            if (index >= 0 && index == _entry) return;   // already there (a transition without a clip cross-faded straight to it)
            Show(index, fade, false);
        }

        // cross-fade the upper layer (and the body layer for a motion) to a state of the profile
        private void Show(int index, float fade, bool withBody)
        {
            var profile = Profile;
            if (profile == null || index < 0 || index >= profile.States.Length) { _entry = -1; return; }
            string state = profile.States[index].State;
            _entry = index; _stateStarted = Time.time; _clipTime = 0f;
            if (Has(_layer, state))
            {
                if (fade <= 0f) Animator.Play(state, _layer, 0f); else Animator.CrossFadeInFixedTime(state, fade, _layer, 0f);
            }
            if (_leftLayer >= 0 && Has(_leftLayer, state))
            {
                if (fade <= 0f) Animator.Play(state, _leftLayer, 0f); else Animator.CrossFadeInFixedTime(state, fade, _leftLayer, 0f);
            }
            if (_torsoLayer >= 0 && Has(_torsoLayer, state))
            {
                if (fade <= 0f) Animator.Play(state, _torsoLayer, 0f); else Animator.CrossFadeInFixedTime(state, fade, _torsoLayer, 0f);
            }
            if (withBody && _bodyLayer >= 0 && Has(_bodyLayer, state))
            {
                if (fade <= 0f) Animator.Play(state, _bodyLayer, 0f); else Animator.CrossFadeInFixedTime(state, fade, _bodyLayer, 0f);
            }
        }

        private bool Has(int layer, string state)
            => layer >= 0 && !string.IsNullOrEmpty(state) && Animator != null && Animator.HasState(layer, Animator.StringToHash(state));

        private void SetTime(int hash, bool has, float normalized) { if (has) Animator.SetFloat(hash, normalized); }

        private bool Chased(GreatBrush308Profile profile)
        {
            if (Session == null || Session.Actors == null || profile.ThreatRadius <= 0f) return false;
            Vector3 at = _motor.transform.position; float radius = profile.ThreatRadius * profile.ThreatRadius;
            foreach (var actor in Session.Actors)
                if (actor != null && actor.isActiveAndEnabled && actor.Current == PrologueEncounter.Behaviour.Chase
                    && (actor.transform.position - at).sqrMagnitude < radius) return true;
            return false;
        }
    }
}
