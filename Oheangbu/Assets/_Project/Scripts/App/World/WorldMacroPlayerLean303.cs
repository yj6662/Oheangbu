using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #303 procedural body lean (SPEC-PLAYER-FEEL-300 H), after the animator: bank into turns, pitch with forward
    // acceleration (and back when braking), lean into a dodge, and a short torso twist + forward lean on a successful cast.
    // Off while drawing, sitting, lying or getting up. Presentation only.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(900)]
    public sealed class WorldMacroPlayerLean303 : MonoBehaviour
    {
        public PlayerReaction303Profile Profile;
        public Animator Animator;
        public PlayerMotor Motor;
        public DrawingInputController Drawing;
        public WorldMacroPlayerReaction303 Reaction;
        public WorldMacroPlaytestSession Session;   // D308-67: who is chasing the player (combat idle). Optional: without it the stance never comes

        Transform _spine, _chest; float _roll, _pitch, _prevSpeed, _castAt = -10f; bool _bound;
        WorldMacroPlayerGestureRig _gesture; bool _castPending; float _castScale = 1f;   // #308 juice B-1 / B-2
        Vector2 _recoil, _recoilVel;   // (pitch, roll) spring knocked by hits
        Transform _neck, _shoulderL, _shoulderR; float _breathWeight, _swayWeight;   // idle (M-1)
        Transform _hips, _legL, _legR, _footL, _footR, _armL, _armR; float _stance, _combatCheckAt; bool _combat;   // combat idle

        // a blow from `from` (body-local direction, y ignored): the upper body is knocked away from it, then springs back
        public void Recoil(Vector3 from, float scale)
        {
            if (Profile == null) return;
            var d = new Vector2(from.x, from.z); if (d.sqrMagnitude < 1e-4f) d = Vector2.up; d.Normalize();
            // front blow → lean back (−pitch); blow from the right → lean left (+roll)
            _recoilVel += new Vector2(-d.y, d.x) * Profile.RecoilDegrees * scale * Profile.RecoilFrequency * 1.6f;
        }
        public float Roll => _roll;
        public float Pitch => _pitch;

        void OnEnable() { _bound = false; }
        void OnDisable() { if (Drawing != null) Drawing.Committed -= OnCommitted; _bound = false; }

        void Bind()
        {
            if (Animator == null) Animator = GetComponent<Animator>();
            if (Animator == null || !Animator.isHuman) return;
            _spine = Animator.GetBoneTransform(HumanBodyBones.Spine); _chest = Animator.GetBoneTransform(HumanBodyBones.Chest);
            _hips = Animator.GetBoneTransform(HumanBodyBones.Hips); _legL = Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg); _legR = Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _footL = Animator.GetBoneTransform(HumanBodyBones.LeftFoot); _footR = Animator.GetBoneTransform(HumanBodyBones.RightFoot); _armL = Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); _armR = Animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _neck = Animator.GetBoneTransform(HumanBodyBones.Neck); _shoulderL = Animator.GetBoneTransform(HumanBodyBones.LeftShoulder); _shoulderR = Animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            if (Drawing != null) { Drawing.Committed -= OnCommitted; Drawing.Committed += OnCommitted; }
            _bound = true;
        }

        // #308 juice (SPEC-ANIM-JUICE-308 B-1 / B-2): the twist is decided in LateUpdate of the same frame (Time.time is the same),
        // after every Committed listener ran. The gesture rig on this object then knows whether the cast was refused (no twist),
        // how long its throw keeps the close-up (the twist starts after it) and the throw's tier (twist scale). Without the rig
        // or its juice profile the values are the old ones: CastDelay and scale 1.
        void OnCommitted(bool success) { _castPending = success; }
        // #308 great brush (SPEC-SPELL-SWING-308 5): both hands are on the brush, so the combat stance does not spread the arms. False without the profile.
        bool GreatBrush308() { if (_gesture == null) _gesture = GetComponent<WorldMacroPlayerGestureRig>(); return _gesture != null && _gesture.GreatBrushActive308; }
        void ResolveCast308()
        {
            _castPending = false;
            if (_gesture == null) _gesture = GetComponent<WorldMacroPlayerGestureRig>();
            if (_gesture != null && _gesture.CommitRejected308) return;
            if (_gesture != null && _gesture.SwingOwnsCast308) return;   // #308 great brush: the swing is the cast's motion (false without its profile)
            float delay = Profile != null ? Profile.CastDelay : 0f;
            _castScale = _gesture != null ? _gesture.CastTwistScale308 : 1f;
            _castAt = Time.time + (_gesture != null ? _gesture.CastTwistDelay308(delay) : delay);
        }

        // a limb is swung outward (side = -1 left, +1 right) about the body's forward axis; the foot keeps its own world rotation (flat on the ground)
        static void Spread(Transform limb, Transform end, Transform body, float degrees, float side, bool keepEnd)
        {
            if (limb == null || degrees <= 0f) return; var tip = limb.childCount > 0 ? limb.GetChild(0).position : limb.position - body.up; var v = tip - limb.position;
            var q = Quaternion.AngleAxis(degrees, body.forward); if (Vector3.Dot(q * v - v, body.right) * side < 0f) q = Quaternion.AngleAxis(-degrees, body.forward);
            var keep = end != null ? end.rotation : Quaternion.identity; limb.rotation = q * limb.rotation; if (keepEnd && end != null) end.rotation = keep;
        }

        void LateUpdate()
        {
            if (!_bound) Bind();
            if (_castPending) ResolveCast308();
            if (!_bound || Motor == null || Profile == null) return;
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            bool off = Motor.IsDrawing || Motor.IsSitting || (Reaction != null && (Reaction.Lying || Reaction.GettingUp));
            Vector3 local = Motor.ActualLocalVelocity; float speed = new Vector2(local.x, local.z).magnitude;
            float roll = 0f, pitch = 0f;
            if (!off)
            {
                roll = Mathf.Clamp(-Motor.ActualYawSpeed * speed * Profile.TurnLean, -Profile.MaxTurnLean, Profile.MaxTurnLean);
                pitch = Mathf.Clamp((speed - _prevSpeed) / dt * Profile.AccelLean, -Profile.MaxAccelLean, Profile.MaxAccelLean);
                if (Motor.IsDodging) { var d = Motor.DodgeLocalDirection; roll += -d.x * Profile.DodgeLean; pitch += d.z * Profile.DodgeLean * .6f; }
            }
            _prevSpeed = speed;
            float k = 1f - Mathf.Exp(-Profile.LeanResponse * dt);
            _roll = Mathf.Lerp(_roll, roll, k); _pitch = Mathf.Lerp(_pitch, pitch, k);
            // damped spring for hit recoil (semi-implicit Euler)
            float w = Profile.RecoilFrequency * 2f * Mathf.PI;
            _recoilVel += (-w * w * _recoil - 2f * Profile.RecoilDamping * w * _recoilVel) * dt; _recoil += _recoilVel * dt;
            if (off && !(Reaction != null && Reaction.Lying)) { _recoil = Vector2.MoveTowards(_recoil, Vector2.zero, 60f * dt); }
            float rp = _recoil.x, rr = _recoil.y;
            // the cast: twist the chest toward the brush side and lean into the throw, easing out
            float u = (Time.time - _castAt) / Mathf.Max(.05f, Profile.CastSeconds);
            float cast = u >= 0f && u < 1f ? Mathf.Sin(Mathf.PI * Mathf.Sqrt(u)) : 0f;   // also while the near view holds (body hidden then)
            var body = Animator.transform;
            float pitchAll = _pitch + rp, rollAll = _roll + rr;
            // idle (M-1): breath on the chest, shoulders and neck; a slow shift of weight on the spine while standing. Nothing while drawing,
            // sitting, lying, getting up or during the cast twist (the brush hand must not drift then).
            // combat idle: is anyone after the player? (looked up four times a second)
            if (Time.time >= _combatCheckAt)
            {
                _combatCheckAt = Time.time + .25f; _combat = false;
                if (Session != null && Session.Actors != null && Profile.CombatStanceEnabled)
                    foreach (var a in Session.Actors) if (a != null && a.isActiveAndEnabled && a.Current == Oheangbu.App.Prologue.PrologueEncounter.Behaviour.Chase && (a.transform.position - body.position).sqrMagnitude < Profile.CombatRangeMeters * Profile.CombatRangeMeters) { _combat = true; break; }
            }
            _stance = Mathf.MoveTowards(_stance, _combat && !off && cast <= 0f && speed < .35f && !Motor.IsDodging ? 1f : 0f, dt / Mathf.Max(.05f, Profile.CombatBlendSeconds));
            if (_stance > .001f && _legL != null && _legR != null && _hips != null)
            {
                float s = Mathf.SmoothStep(0f, 1f, _stance), leg = Profile.CombatLegSpreadDegrees * s, arm = Profile.CombatArmSpreadDegrees * s; float length = _footL != null ? Vector3.Distance(_legL.position, _footL.position) : .85f;
                _hips.position -= body.up * (length * (1f - Mathf.Cos(leg * Mathf.Deg2Rad)));
                Spread(_legL, _footL, body, leg, -1f, true); Spread(_legR, _footR, body, leg, 1f, true); if (!GreatBrush308()) { Spread(_armL, null, body, arm, -1f, false); Spread(_armR, null, body, arm, 1f, false); }
                pitchAll += Profile.CombatLeanDegrees * s;
            }
            bool idleOff = off || cast > 0f || !Profile.BreathEnabled;
            _breathWeight = Mathf.MoveTowards(_breathWeight, idleOff ? 0f : Mathf.Lerp(1f, Profile.BreathMovingScale, Mathf.Clamp01(speed * .5f)), dt * 2f);
            _swayWeight = Mathf.MoveTowards(_swayWeight, !idleOff && speed < .1f ? 1f : 0f, dt * 1.2f);
            float inhale = .5f - .5f * Mathf.Cos(Time.time * 2f * Mathf.PI / Mathf.Max(.5f, Profile.BreathPeriodSeconds));
            float breath = -Profile.BreathChestPitchDegrees * (inhale - .5f) * 2f * _breathWeight * Mathf.Lerp(1f, Profile.CombatBreathScale, _stance);
            float sway = Profile.IdleSwayDegrees * Mathf.Sin(Time.time * 2f * Mathf.PI / Mathf.Max(1f, Profile.IdleSwayPeriodSeconds)) * _swayWeight;
            if (_spine != null && (Mathf.Abs(rollAll) > .01f || Mathf.Abs(pitchAll) > .01f || Mathf.Abs(sway) > .001f))
                _spine.rotation = Quaternion.AngleAxis(pitchAll * .6f, body.right) * Quaternion.AngleAxis(rollAll * .6f + sway, body.forward) * _spine.rotation;
            if (_chest != null && (Mathf.Abs(rollAll) > .01f || Mathf.Abs(pitchAll) > .01f || cast > 0f || Mathf.Abs(breath) > .001f))
                _chest.rotation = Quaternion.AngleAxis(-Profile.CastTwist * cast * _castScale, body.up) * Quaternion.AngleAxis(pitchAll * .4f + Profile.CastLean * cast + breath, body.right) *
                                  Quaternion.AngleAxis(rollAll * .4f - sway * .5f, body.forward) * _chest.rotation;
            if (_breathWeight > .001f)
            {
                inhale *= Mathf.Lerp(1f, Profile.CombatBreathScale, _stance);
                if (_neck != null) _neck.rotation = Quaternion.AngleAxis(-breath * Profile.BreathNeckShare, body.right) * _neck.rotation;
                var rise = body.up * (Profile.BreathShoulderRiseMeters * inhale * _breathWeight);
                if (_shoulderL != null) _shoulderL.position += rise; if (_shoulderR != null) _shoulderR.position += rise;
            }
        }
    }
}
