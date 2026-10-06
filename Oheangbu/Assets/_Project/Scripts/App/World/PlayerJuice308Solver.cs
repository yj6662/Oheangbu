using Oheangbu.Data.World;
using Oheangbu.Presentation;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>What the juice adds this frame (SPEC-ANIM-JUICE-308). All zero = today's pose.</summary>
    public struct JuiceFrame308
    {
        /// <summary>A-1: the close-up brush shaft stands up toward the drawing-plane normal by this many degrees (tip fixed).</summary>
        public float ShaftRaiseDegrees;
        /// <summary>A-2: shaft lean in the view plane (x right, y up; degrees). The butt leads along the stroke direction.</summary>
        public Vector2 ShaftLeanDegrees;
        /// <summary>A-1: bristle splay added to this frame's pose.</summary>
        public float SplayAdd;
        /// <summary>D: camera reaction for this frame's render only: x nod (+ = dips), y roll, z field of view (degrees).</summary>
        public Vector3 Camera;
        public bool AnyShaft => ShaftRaiseDegrees != 0f || ShaftLeanDegrees.x != 0f || ShaftLeanDegrees.y != 0f;
    }

    /// <summary>
    /// #308 player juice: events + frame time -> JuiceFrame308. A plain class with no scene access, so the preview driver can run
    /// it on a timeline alone (Juice308Build timeline). Every value is a closed form of the time since its event
    /// (PlayerJuice308Curves); the only running state is one clock per event and the smoothed displayed stroke velocity
    /// that the stroke-end flick reads once. Presentation only: it never sees the recogniser's samples, and nothing it
    /// computes is read by a gameplay rule. No static field. Time comes from the caller (the rig's unscaled / capture dt).
    /// An event raised in Update is at t = 0 on that frame's LateUpdate and advances from the next frame.
    /// </summary>
    public sealed class PlayerJuice308Solver
    {
        private struct Clock
        {
            public bool On, Fresh;
            public float T;
            public void Start() { On = true; Fresh = true; T = 0f; }
            public void Stop() { On = false; Fresh = false; T = 0f; }
            public void Advance(float dt, float length)
            {
                if (!On) return;
                if (Fresh) Fresh = false; else T += dt;
                if (T >= length) Stop();
            }
        }

        private PlayerJuice308ProfileSO _profile;
        private bool _reducedMotion;
        private float _frameSeconds = 1f / 60f;

        private Clock _press, _flick, _kick, _callSettle, _thump;
        private float _pressScale, _flickDegrees, _sinceStrokeEnd = float.PositiveInfinity;
        private Vector2 _flickDirection, _velocity;
        private int _strokesInLetter;
        private float _kickScale, _rollSign, _thumpScale;
        private float _flickHz, _thumpHz;   // ring frequencies, fixed on the frame the ring starts (Advance)

        // B-2 values of the running cast (the rig keeps the cast clock)
        private bool _castBeat;
        private float _castHold, _castOvershoot, _castTwistScale = 1f;

        // C-1 / C-3 values of the running call stroke
        private bool _callAccepted = true;

        public PlayerJuice308ProfileSO Profile => _profile;
        public bool Active => _profile != null && _profile.Intensity > 0f;
        public bool ReducedMotion => _reducedMotion;
        /// <summary>Diagnostics for the checks and the preview traces.</summary>
        public int PressStarts { get; private set; }
        public int FlickStarts { get; private set; }
        public int CameraEvents { get; private set; }
        public int CameraCancels { get; private set; }
        /// <summary>Diagnostics: cast kicks that did not start because the cast was a parry (CastKick.OnParryCasts off).</summary>
        public int ParryKicksSkipped { get; private set; }
        public bool CameraRunning => _kick.On || _callSettle.On || _thump.On;

        public void Configure(PlayerJuice308ProfileSO profile) { _profile = profile; Reset(); }
        public void SetReducedMotion(bool reduced) { _reducedMotion = reduced; }

        public void Reset()
        {
            _press.Stop(); _flick.Stop(); _kick.Stop(); _callSettle.Stop(); _thump.Stop();
            _velocity = Vector2.zero; _strokesInLetter = 0; _sinceStrokeEnd = float.PositiveInfinity;
            _castBeat = false; _castHold = 0f; _castOvershoot = 0f; _castTwistScale = 1f; _callAccepted = true;
        }

        private float BodyScale(float group) => _profile.Intensity * group * (_reducedMotion ? _profile.ReducedMotionScale : 1f);
        private float CameraScale => _profile.Intensity * _profile.CameraIntensity * (_reducedMotion ? _profile.CameraReducedMotionScale : 1f);
        /// <summary>Scale the call-side presenters outside the rig use (C-4: the car's visual settle).</summary>
        public float CallScale => Active ? BodyScale(_profile.CallIntensity) : 0f;

        // ---------------------------------------------------------------- A drawing

        public void ModeEntered()
        {
            _strokesInLetter = 0; _sinceStrokeEnd = float.PositiveInfinity; _velocity = Vector2.zero;
            _press.Stop(); _flick.Stop();
            CancelCamera();   // D rule 1: a new draw mode ends every camera reaction on that frame
        }

        public void StrokeStarted()
        {
            _flick.Stop();    // the flick of the stroke before never leans the shaft a new stroke anchors on
            _velocity = Vector2.zero;
            if (!Active || !_profile.StrokeStart.Enabled) { _strokesInLetter++; return; }
            var s = _profile.StrokeStart;
            _pressScale = (_strokesInLetter == 0 ? 1f : s.LaterStrokeScale) * (_sinceStrokeEnd < s.RapidWindow ? s.RapidScale : 1f);
            _strokesInLetter++;
            _press.Start(); PressStarts++;
        }

        /// <summary>The displayed stroke velocity of this frame in the rig's centred viewport units per second (stroking frames only).</summary>
        public void NoteStrokeVelocity(Vector2 velocity, float dt)
        {
            if (_profile == null) return;
            float k = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / Mathf.Max(.001f, _profile.StrokeEnd.SpeedWindow));
            _velocity = Vector2.Lerp(_velocity, velocity, k);
        }

        public void StrokeEnded()
        {
            _press.Stop();    // budget: the press and the flick are never both on the shaft
            _sinceStrokeEnd = 0f;
            if (!Active || !_profile.StrokeEnd.Enabled) return;
            var e = _profile.StrokeEnd;
            float speed = _velocity.magnitude;
            if (speed <= 1e-4f) return;
            _flickDirection = _velocity / speed;
            _flickDegrees = Mathf.Lerp(e.FlickLeanMin, e.FlickLeanMax, Mathf.InverseLerp(e.SpeedLow, e.SpeedHigh, speed));
            _flick.Start(); FlickStarts++;
        }

        /// <summary>The letter ended (commit, misfire, cancel, interrupt): nothing of the drawing stays on the shaft.</summary>
        public void DrawingEnded() { _press.Stop(); _flick.Stop(); _velocity = Vector2.zero; }

        // ---------------------------------------------------------------- B after drawing

        /// <summary>B-2: prepares the throw of a successful commit. Returns the seconds the close-up arm is shown
        /// (0 = the re-timing is off: the rig keeps its own flick).</summary>
        public float BeginCast(float power01)
        {
            _castBeat = false; _castTwistScale = 1f; _castHold = 0f; _castOvershoot = 0f;
            if (!Active || !_profile.CastBeat.Enabled) return 0f;
            var b = _profile.CastBeat;
            int tier = _profile.Tier(Mathf.Clamp01(power01));
            float k = Mathf.Clamp01(BodyScale(_profile.CastIntensity));   // 0..1: blends the tier values toward the plainest throw
            _castHold = (tier == 2 ? b.HoldSecondsHigh : tier == 1 ? b.HoldSecondsMid : b.HoldSecondsLow) * k;
            _castOvershoot = Mathf.Lerp(b.OvershootLow, tier == 2 ? b.OvershootHigh : tier == 1 ? b.OvershootMid : b.OvershootLow, k);
            _castTwistScale = Mathf.Lerp(1f, tier == 2 ? b.TwistScaleHigh : tier == 1 ? b.TwistScaleMid : b.TwistScaleLow, k);
            _castBeat = true;
            return b.CockSeconds + b.FlickSeconds + _castHold + b.DropSeconds;
        }

        public bool CastBeat => _castBeat;
        public float CastTwistScale => _castBeat ? _castTwistScale : 1f;
        public float CastOvershoot => _castOvershoot;
        public void EndCast() { _castBeat = false; }

        /// <summary>B-2: tip position on the path and the lowering, `castSeconds` after the commit.
        /// along: 0 = last pointer, 1 = launch point; over: viewport units past the launch point; drop: 0..1.</summary>
        public void EvaluateCast(float castSeconds, out float along, out float over, out float drop)
        {
            var b = _profile.CastBeat;
            float k = Mathf.Clamp01(BodyScale(_profile.CastIntensity));
            along = PlayerJuice308Curves.CastAlong(castSeconds, b.CockSeconds, b.FlickSeconds, b.CockBack * k);
            over = _castOvershoot * PlayerJuice308Curves.CastOver(castSeconds, b.CockSeconds, b.FlickSeconds, _castHold, b.Rebound);
            drop = PlayerJuice308Curves.CastDrop(castSeconds, b.CockSeconds, b.FlickSeconds, _castHold, b.DropSeconds);
        }

        /// <summary>D-1 (+ D-3): the camera kick of a successful cast. throwX: sign of the throw across the screen (roll).</summary>
        public void CastKick(float power01, float throwX) { CastKick(power01, throwX, false); }

        /// <summary>The same, told what was cast (#308 3rd revision, D308-24 answer 24). parryCast: the accepted cast is a parry
        /// glyph - no camera reaction starts for it unless the profile says so (CastKick.OnParryCasts). Only the camera part is
        /// decided here: the throw (BeginCast / EvaluateCast) never sees the kind of the cast.</summary>
        public void CastKick(float power01, float throwX, bool parryCast)
        {
            if (!Active || !_profile.Camera.Enabled || !_profile.CastKick.Enabled || CameraScale <= 0f) return;
            if (parryCast && !_profile.CastKick.OnParryCasts) { ParryKicksSkipped++; return; }
            _kickScale = Mathf.Lerp(_profile.CastKick.PowerFloor, 1f, Mathf.Clamp01(power01));
            _rollSign = throwX > 0f ? -1f : throwX < 0f ? 1f : 0f;   // throwing right tips the view clockwise (negative z)
            _kick.Start(); CameraEvents++;
        }

        // ---------------------------------------------------------------- C vehicle call

        public void CallBegan() { _callAccepted = true; }

        /// <summary>C-3 / D-2a: the call moment and its answer (the car comes or is taken back / nothing happens).</summary>
        public void CallMoment(bool accepted)
        {
            _callAccepted = accepted;
            if (!accepted || !Active || !_profile.Camera.Enabled || !_profile.CallSettle.Enabled || CameraScale <= 0f) return;
            _callSettle.Start(); CameraEvents++;
        }

        /// <summary>D-2b: the summoned car is fully revealed. distance: flat metres player - car.</summary>
        public void VehicleSetDown(float distance)
        {
            if (!Active || !_profile.Camera.Enabled || !_profile.SetDownThump.Enabled || CameraScale <= 0f) return;
            var t = _profile.SetDownThump;
            _thumpScale = PlayerJuice308Curves.DistanceScale(distance, t.FullDistance, t.ZeroDistance);
            if (_thumpScale <= 0f) return;
            _thump.Start(); CameraEvents++;
        }

        /// <summary>C-1 / C-3: the call stroke as a function of the caller's progress alone.
        /// weight: arm weight; over: path shares past the stroke's end; droop: metres the tip sinks (refused call).</summary>
        public void EvaluateCall(float progress, float raiseEnd, float contactEnd, float strokeSeconds,
            out float weight, out float over, out float droop)
        {
            var c = _profile.CallEnd; var r = _profile.CallResponse;
            float k = Mathf.Clamp01(BodyScale(_profile.CallIntensity));
            bool on = c.Enabled && k > 0f && strokeSeconds > 0f;
            float holdTo = on ? Mathf.Lerp(contactEnd, Mathf.Clamp(c.HoldTo, contactEnd, .98f), k) : contactEnd;
            weight = PlayerJuice308Curves.AirWeight(progress, raiseEnd, holdTo);
            over = droop = 0f;
            if (!on || progress <= contactEnd) return;
            float seconds = (progress - contactEnd) * strokeSeconds;
            float share = Mathf.Min(c.OverShare, c.MaxOverShare) * k * (_callAccepted ? 1f : r.RefusedOverScale);
            over = share * PlayerJuice308Curves.AirOver(seconds, c.OverRise, c.OverHz, c.OverDamping, c.OverSeconds);
            if (!_callAccepted) droop = r.RefusedDroopMeters * k * PlayerJuice308Curves.Smooth(seconds / Mathf.Max(1e-5f, r.DroopSeconds));
        }

        // ---------------------------------------------------------------- frame

        /// <summary>Advances the event clocks by the rig's frame time. Call once per evaluated frame, before Evaluate.</summary>
        public void Advance(float dt)
        {
            if (_profile == null) return;
            dt = Mathf.Max(0f, dt);
            if (dt > 0f) _frameSeconds = dt;
            _sinceStrokeEnd += dt;
            var s = _profile.StrokeStart; var e = _profile.StrokeEnd; var k = _profile.CastKick; var c = _profile.CallSettle; var t = _profile.SetDownThump;
            // A ring keeps the frequency of the frame it starts on (the low-frame-rate rule is read once): a hitch frame in the
            // middle must not re-tune it - the value stays a function of the time since the event alone.
            if (_flick.On && _flick.Fresh) _flickHz = PlayerJuice308Curves.RingHz(e.FlickHz, _frameSeconds, _profile.MinRingFrameRate);
            if (_thump.On && _thump.Fresh) _thumpHz = PlayerJuice308Curves.RingHz(t.Hz, _frameSeconds, _profile.MinRingFrameRate);
            _press.Advance(dt, Mathf.Max(s.PressAttack + s.PressRelease, s.SplayAttack + s.SplayRelease));
            _flick.Advance(dt, e.FlickSeconds);
            float attack = Mathf.Max(_profile.Camera.MinAttackSeconds, 0f);
            float kickLength = k.DelaySeconds + Mathf.Max(Mathf.Max(Mathf.Max(attack, k.PitchAttack) + k.PitchRelease, Mathf.Max(attack, k.FovAttack) + k.FovRelease),
                _profile.ThrowRoll.Enabled ? Mathf.Max(attack, _profile.ThrowRoll.Attack) + _profile.ThrowRoll.Release : 0f);
            _kick.Advance(dt, kickLength);
            _callSettle.Advance(dt, Mathf.Max(attack, c.Attack) + c.Release);
            _thump.Advance(dt, Mathf.Max(t.Seconds, Mathf.Max(attack, t.FovAttack) + t.FovRelease));
        }

        /// <summary>D rule 1 / 7: ends every camera reaction now (draw mode, live strokes, seat, get-up camera, pause).</summary>
        public void CancelCamera()
        {
            if (_kick.On || _callSettle.On || _thump.On) CameraCancels++;
            _kick.Stop(); _callSettle.Stop(); _thump.Stop();
        }

        /// <summary>This frame's additions. cameraAllowed false = the camera part is 0 and its reactions are ended.</summary>
        public void Evaluate(bool cameraAllowed, out JuiceFrame308 frame)
        {
            frame = default;
            if (!Active) { if (CameraRunning) CancelCamera(); return; }

            if (_press.On)
            {
                var s = _profile.StrokeStart; float k = BodyScale(_profile.DrawIntensity) * _pressScale;
                frame.ShaftRaiseDegrees = s.PressTiltDegrees * k * PlayerJuice308Curves.Pulse(_press.T, s.PressAttack, s.PressRelease);
                frame.SplayAdd = Mathf.Min(_profile.MaxSplayAdd, s.PressSplay * k * PlayerJuice308Curves.Pulse(_press.T, s.SplayAttack, s.SplayRelease));
            }
            if (_flick.On)
            {
                var e = _profile.StrokeEnd;
                float hz = _flickHz;
                float degrees = _flickDegrees * BodyScale(_profile.DrawIntensity) * PlayerJuice308Curves.Ring(_flick.T, hz, e.FlickDamping, e.FlickSeconds);
                frame.ShaftLeanDegrees = _flickDirection * degrees;
            }
            // ceiling after the sum (the press and the flick never overlap, the clamp still holds for any profile)
            float limit = _profile.MaxShaftLeanDegrees;
            frame.ShaftRaiseDegrees = Mathf.Clamp(frame.ShaftRaiseDegrees, 0f, limit);
            frame.ShaftLeanDegrees = Vector2.ClampMagnitude(frame.ShaftLeanDegrees, Mathf.Max(0f, limit - frame.ShaftRaiseDegrees));

            if (!cameraAllowed) { CancelCamera(); return; }
            if (!CameraRunning) return;
            frame.Camera = EvaluateCamera();
        }

        private Vector3 EvaluateCamera()
        {
            var cam = _profile.Camera; float minAttack = Mathf.Max(0f, cam.MinAttackSeconds), scale = CameraScale;
            float pitch = 0f, roll = 0f, fov = 0f;
            if (_kick.On)
            {
                var k = _profile.CastKick; float t = _kick.T - k.DelaySeconds;
                pitch += k.PitchDegrees * _kickScale * PlayerJuice308Curves.Pulse(t, Mathf.Max(minAttack, k.PitchAttack), k.PitchRelease);
                fov += k.FovDegrees * _kickScale * PlayerJuice308Curves.Pulse(t, Mathf.Max(minAttack, k.FovAttack), k.FovRelease);
                var r = _profile.ThrowRoll;
                if (r.Enabled) roll += r.RollDegrees * _rollSign * _kickScale * PlayerJuice308Curves.Pulse(t, Mathf.Max(minAttack, r.Attack), r.Release);
            }
            if (_callSettle.On)
            {
                var c = _profile.CallSettle;
                pitch += c.PitchDegrees * PlayerJuice308Curves.Pulse(_callSettle.T, Mathf.Max(minAttack, c.Attack), c.Release);
            }
            if (_thump.On)
            {
                var t = _profile.SetDownThump;
                float hz = _thumpHz;
                float peak = PlayerJuice308Curves.RingPeakTime(hz, t.Damping);
                if (peak > 0f && peak < minAttack) hz *= peak / minAttack;   // the first peak is never earlier than the comfort floor
                pitch += t.PitchDegrees * _thumpScale * PlayerJuice308Curves.Ring(_thump.T, hz, t.Damping, t.Seconds);
                fov += t.FovDegrees * _thumpScale * PlayerJuice308Curves.Pulse(_thump.T, Mathf.Max(minAttack, t.FovAttack), t.FovRelease);
            }
            return PlayerJuice308Curves.ClampCamera(new Vector3(pitch, roll, fov) * scale, cam.MaxPitchDegrees, cam.MaxRollDegrees, cam.MaxFovDegrees);
        }
    }
}
