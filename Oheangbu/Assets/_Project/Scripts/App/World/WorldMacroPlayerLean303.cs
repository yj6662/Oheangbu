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

        Transform _spine, _chest; float _roll, _pitch, _prevSpeed, _castAt = -10f; bool _bound;
        Vector2 _recoil, _recoilVel;   // (pitch, roll) spring knocked by hits

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
            if (Drawing != null) { Drawing.Committed -= OnCommitted; Drawing.Committed += OnCommitted; }
            _bound = true;
        }

        void OnCommitted(bool success) { if (success) _castAt = Time.time + (Profile != null ? Profile.CastDelay : 0f); }

        void LateUpdate()
        {
            if (!_bound) Bind();
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
            if (_spine != null && (Mathf.Abs(rollAll) > .01f || Mathf.Abs(pitchAll) > .01f))
                _spine.rotation = Quaternion.AngleAxis(pitchAll * .6f, body.right) * Quaternion.AngleAxis(rollAll * .6f, body.forward) * _spine.rotation;
            if (_chest != null && (Mathf.Abs(rollAll) > .01f || Mathf.Abs(pitchAll) > .01f || cast > 0f))
                _chest.rotation = Quaternion.AngleAxis(-Profile.CastTwist * cast, body.up) * Quaternion.AngleAxis(pitchAll * .4f + Profile.CastLean * cast, body.right) *
                                  Quaternion.AngleAxis(rollAll * .4f, body.forward) * _chest.rotation;
        }
    }
}
