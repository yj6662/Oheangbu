using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 resolved parameters of one liquid (HudLiquid308ProfileSO.Hp / Ink + style values). Plain data.</summary>
    public struct LiquidParams308
    {
        public float SpringHz, Damping, MaxTilt;
        public float TiltLimit;             // hard cap of the slope itself (>= MaxTilt: a strong action may overshoot up to here)
        public float TiltKickPerMps;        // tilt speed gained per m/s of sudden sideways speed change (= TiltPerMps x w / first-peak share)
        public float WaveMaxPx, WaveTau, WaveSpeed, WaveExcite;
        public float WavePerMps, HeavePerMps;   // ripple px per m/s of forward speed change / of take-off and landing speed
        public float DrainTau, DrainMinSpeed, FillTau;
        public float WetAlpha, WetHold, WetDry, FreshSeconds, PourMinDelta, PourFadeSeconds;
        public float KickTilt, KickWavePx, DropKickTilt, LevelFullDelta;
        public float SimStep, RestTilt, RestTiltSpeed, RestWavePx, LevelSnap, ReducedSeconds;
        public float RestHold;              // seconds after the last shove before the surface may be put to rest
    }

    /// <summary>#308 state of one liquid. Value is the game value (immediate); everything else is what the eye sees.</summary>
    public struct LiquidState308
    {
        public float Value;                 // game value 0..1, set at once
        public float Level;                 // visible level 0..1, follows Value
        public float Tilt, TiltSpeed;       // surface slope (height / width) and its rate
        public float WavePx, Phase;         // ripple amplitude (design px) and phase
        public float WetTop, WetAge;        // wet film on the glass: its top level and age (age < 0 = none)
        public float FreshBottom, FreshAge; // freshly received share: its bottom level and age (age < 0 = none)
        public float Pour;                  // pour thread 0..1
        public bool Pouring;
        public float ReducedSpan;           // |value - level| when the value last changed (reduced motion: linear over ReducedSeconds)
        public float Accumulator;           // fixed-step remainder
        public bool DropSide;               // which way the next damage jolt throws the liquid (alternates)
        public float ShoveHold;             // seconds left in which the surface is not put to rest: a shove may still be building up (0 = none)
        public bool Still;                  // nothing moves and nothing is left to show: the mesh is left alone
    }

    /// <summary>#308 what the player did in one frame, as measured (D308-11b). Everything here is a CHANGE or an EVENT:
    /// a player who stands still produces exactly zero in every field.</summary>
    public struct ActionSample308
    {
        public float LateralStep;           // m/s: change of the sideways speed this frame, fast view turning folded in
        public float ForwardStep;           // m/s: change of the forward speed this frame
        public float JumpSpeed;             // m/s: take-off speed on the frame a jump began, else 0
        public float LandSpeed;             // m/s: downward speed just before the feet landed on this frame, else 0

        public bool Any => LateralStep != 0f || ForwardStep != 0f || JumpSpeed != 0f || LandSpeed != 0f;
        public void Add(in ActionSample308 other)
        {
            LateralStep += other.LateralStep; ForwardStep += other.ForwardStep;
            JumpSpeed = Mathf.Max(JumpSpeed, other.JumpSpeed); LandSpeed = Mathf.Max(LandSpeed, other.LandSpeed);
        }
    }

    /// <summary>#308 liquid model (SPEC-HUD-LIQUID-308 §2.3 / §2.4, D308-11b): a damped spring whose ONLY equilibrium is the level
    /// surface, shoved by what the player really did (Drive: speed changes, take-off, landing; SetValue: damage, ink spent,
    /// ink poured; Kick: an impact frame, a jolt). There is no target tilt, no time-driven term and no idle motion: with no
    /// action the surface is a flat line and the state is Still (a shove is never thrown away for being small: the surface is
    /// put to rest only RestHold after the last one). An exponential level follower with a wet film where the level
    /// was and a pour thread while it rises. Pure functions over structs: no Unity objects, no allocation, no static state.
    /// The numpy twin is Tools/Art/hud308_twin.py (sim_step, drive, ActionMeter).</summary>
    public static class LiquidSim308
    {
        const float TwoPi = 6.28318530718f;

        /// <summary>Share of an impulse's ideal swing (speed / w) that the first peak of a damped spring reaches:
        /// exp(-z phi / sqrt(1 - z^2)), phi = atan(sqrt(1 - z^2) / z). 1 undamped, .69 at z .28, .52 at z .55.</summary>
        public static float FirstPeakShare(float damping)
        {
            float z = Mathf.Clamp(damping, 0f, .999f);
            if (z <= 1e-4f) return 1f;
            float r = Mathf.Sqrt(1f - z * z);
            return Mathf.Exp(-z * Mathf.Atan2(r, z) / r);
        }

        public static void Reset(ref LiquidState308 s, float value)
        {
            value = Mathf.Clamp01(value);
            bool side = s.DropSide;
            s = default;
            s.Value = s.Level = value;
            s.WetAge = -1f; s.FreshAge = -1f;
            s.DropSide = side;
            s.Still = true;
        }

        /// <summary>The game value changed. A drop leaves a wet film from the visible level down to the new one (a second drop
        /// while it dries keeps the top and restarts the clock); a single rise above PourMinDelta pours. A single change above
        /// PourMinDelta also stirs the surface in proportion to its size (D308-11b): ripples, and for a drop the damage jolt
        /// (DropKickTilt; 0 for ink - spending ink is the player's own doing). Natural regeneration stays below it and is quiet.</summary>
        public static void SetValue(ref LiquidState308 s, in LiquidParams308 p, float value)
        {
            value = Mathf.Clamp01(value);
            float delta = value - s.Value;
            if (delta == 0f) return;
            float strength = Mathf.Clamp01(Mathf.Abs(delta) / Mathf.Max(p.LevelFullDelta, 1e-4f));
            if (delta < 0f)
            {
                if (s.Level > value)
                {
                    bool live = s.WetAge >= 0f && s.WetAge < p.WetHold + p.WetDry;
                    s.WetTop = live ? Mathf.Max(s.WetTop, s.Level) : s.Level;
                    s.WetAge = 0f;
                }
                s.Pouring = false;
                if (-delta > p.PourMinDelta)
                {
                    s.DropSide = !s.DropSide;
                    Kick(ref s, in p, (s.DropSide ? 1f : -1f) * p.DropKickTilt * strength, p.KickWavePx * strength);
                }
            }
            else if (delta > p.PourMinDelta)
            {
                MarkFresh(ref s, in p, s.Level);
                s.Pouring = true;
                s.WavePx = Mathf.Min(p.WaveMaxPx, s.WavePx + p.KickWavePx * strength);
            }
            s.Value = value;
            s.ReducedSpan = Mathf.Abs(value - s.Level);
            s.Still = false;
        }

        /// <summary>The share above `from` was just received: it reads lighter for FreshSeconds, then blends in.</summary>
        public static void MarkFresh(ref LiquidState308 s, in LiquidParams308 p, float from)
        {
            bool live = s.FreshAge >= 0f && s.FreshAge < p.FreshSeconds;
            from = Mathf.Clamp01(from);
            s.FreshBottom = live ? Mathf.Min(s.FreshBottom, from) : from;
            s.FreshAge = 0f;
            s.Still = false;
        }

        /// <summary>An impulse on the surface: tilt speed (signed) and extra ripple. The level is never touched.</summary>
        public static void Kick(ref LiquidState308 s, in LiquidParams308 p, float tiltSpeed, float wavePx)
        {
            if (float.IsNaN(tiltSpeed) || float.IsNaN(wavePx)) return;
            if (tiltSpeed == 0f && wavePx <= 0f) return;
            s.TiltSpeed += tiltSpeed;       // bounded in Step (speed cap): repeated kicks cannot pile up
            s.WavePx = Mathf.Min(p.WaveMaxPx, s.WavePx + Mathf.Max(0f, wavePx));
            s.ShoveHold = Mathf.Max(0f, p.RestHold);   // a shove smaller than the rest thresholds is kept: more of it may follow
            s.Still = false;
        }

        /// <summary>A jolt of strength 0..1 that the game reports (a hit taken, a harvested chunk landing in the bottle): ripples on
        /// the ink in proportion, half of them on the HP liquid. No tilt, the levels are not touched.</summary>
        public static void Jolt(ref LiquidState308 hp, ref LiquidState308 ink, in LiquidParams308 hpParams, in LiquidParams308 inkParams, float strength)
        {
            strength = Mathf.Clamp01(strength);
            Kick(ref ink, in inkParams, 0f, inkParams.KickWavePx * strength);
            Kick(ref hp, in hpParams, 0f, hpParams.KickWavePx * strength * .5f);
        }

        /// <summary>An impact frame began (D308-10b; D308-10c: only spells that land on a groggy enemy fire one): the liquid is
        /// thrown away from the impact point. away = -1 / +1 (the side the liquid piles up on), strength 0..1.</summary>
        public static void ImpactKick(ref LiquidState308 s, in LiquidParams308 p, float away, float strength)
        {
            Kick(ref s, in p, away * p.KickTilt * Mathf.Clamp01(strength), p.KickWavePx);
        }

        /// <summary>What the player did this frame shoves the surface, in proportion (D308-11b): a sideways speed change throws
        /// the liquid the other way (sign = MotionInputSpec308.TiltSign), a forward speed change, a take-off and a landing raise
        /// ripples. A zero sample does nothing at all. The level is never touched.</summary>
        public static void Drive(ref LiquidState308 s, in LiquidParams308 p, in ActionSample308 a, float sign)
        {
            float tilt = sign * p.TiltKickPerMps * a.LateralStep;
            float wave = p.WavePerMps * Mathf.Abs(a.ForwardStep) + p.HeavePerMps * (Mathf.Max(0f, a.JumpSpeed) + Mathf.Max(0f, a.LandSpeed));
            Kick(ref s, in p, tilt, wave);
        }

        /// <summary>Film alpha now: WetAlpha while it holds, then down to 0 over WetDry.</summary>
        public static float WetAlphaNow(in LiquidState308 s, in LiquidParams308 p)
        {
            if (s.WetAge < 0f) return 0f;
            if (s.WetAge <= p.WetHold) return p.WetAlpha;
            return p.WetAlpha * Mathf.Clamp01(1f - (s.WetAge - p.WetHold) / Mathf.Max(p.WetDry, 1e-4f));
        }

        /// <summary>Fresh share 1 -> 0 (the last 65 % of its life eases out, like the #304 received flash).</summary>
        public static float FreshNow(in LiquidState308 s, in LiquidParams308 p)
        {
            if (s.FreshAge < 0f || p.FreshSeconds <= 0f) return 0f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.FreshSeconds * .35f, p.FreshSeconds, s.FreshAge));
        }

        /// <summary>Advances one frame. No input: the spring only swings out what SetValue / Drive / Kick put into it and comes
        /// to rest on the level surface. reduced = "움직임 줄이기" (no slosh at all, the level moves in a straight line).</summary>
        public static void Step(ref LiquidState308 s, in LiquidParams308 p, bool reduced, float dt)
        {
            if (dt <= 0f) return;

            // ---- level: the value is never faked, the visible surface reaches it (drain .26-.38 s for a full swing)
            float gap = s.Value - s.Level;
            if (gap != 0f)
            {
                if (reduced)
                    s.Level = Mathf.MoveTowards(s.Level, s.Value, dt * Mathf.Max(s.ReducedSpan, 1e-4f) / Mathf.Max(p.ReducedSeconds, .01f));
                else if (gap < 0f)
                {
                    float step = Mathf.Max(-gap * (1f - Mathf.Exp(-dt / Mathf.Max(p.DrainTau, 1e-4f))), p.DrainMinSpeed * dt);
                    s.Level = Mathf.Max(s.Value, s.Level - step);
                }
                else
                {
                    float step = gap * (1f - Mathf.Exp(-dt / Mathf.Max(p.FillTau, 1e-4f)));
                    s.Level = gap - step <= p.LevelSnap ? s.Value : s.Level + step;
                }
            }

            // ---- wet film and fresh share clocks
            if (s.WetAge >= 0f)
            {
                s.WetAge += dt;
                if (s.WetAge >= p.WetHold + p.WetDry || s.Value >= s.WetTop) s.WetAge = -1f;   // dried, or the liquid is back over it
            }
            if (s.FreshAge >= 0f)
            {
                s.FreshAge += dt;
                if (s.FreshAge >= p.FreshSeconds) s.FreshAge = -1f;
            }

            // ---- pour thread: only for a poured rise, while the surface is still catching up
            if (s.Pouring && (reduced || s.Value - s.Level <= p.PourMinDelta)) s.Pouring = false;
            s.Pour = Mathf.MoveTowards(s.Pour, s.Pouring ? 1f : 0f, dt / Mathf.Max(p.PourFadeSeconds, 1e-3f));

            // ---- slosh: a free damped swing about the level surface (no target, no forcing term)
            bool moving = false;
            if (reduced)
            {
                s.Tilt = s.TiltSpeed = s.WavePx = s.Phase = 0f; s.Accumulator = 0f; s.ShoveHold = 0f;
            }
            else
            {
                // a NaN (a broken pose upstream) must not poison the spring
                if (float.IsNaN(s.Tilt) || float.IsNaN(s.TiltSpeed) || float.IsNaN(s.WavePx)) { s.Tilt = s.TiltSpeed = s.WavePx = 0f; s.Accumulator = 0f; }
                float w = TwoPi * p.SpringHz, h = Mathf.Max(p.SimStep, 1e-4f);
                // the fixed step must stay well inside the integrator's stability bound (w h < 2), whatever the data says
                if (w * h > .5f) h = .5f / Mathf.Max(w, 1e-4f);
                // hard caps: the slope (data: TiltLimit, never below MaxTilt) and its rate (twice what a free swing of that
                // amplitude reaches), so neither a run of kicks nor a hitch can make the surface run away. The rate is capped
                // BEFORE the swing as well: a kick lands between two steps
                float limit = Mathf.Max(p.TiltLimit, p.MaxTilt);
                float speedLimit = 2f * w * limit;
                s.TiltSpeed = Mathf.Clamp(s.TiltSpeed, -speedLimit, speedLimit);
                s.Accumulator += dt;
                int guard = 0;
                while (s.Accumulator >= h && guard++ < 64)
                {
                    // semi-implicit Euler at a fixed step: the same motion at 30, 60 and 144 fps
                    s.TiltSpeed += (-w * w * s.Tilt - 2f * p.Damping * w * s.TiltSpeed) * h;
                    s.Tilt += s.TiltSpeed * h;
                    s.Accumulator -= h;
                }
                if (guard >= 64) s.Accumulator = 0f;
                s.TiltSpeed = Mathf.Clamp(s.TiltSpeed, -speedLimit, speedLimit);
                if (s.Tilt > limit) { s.Tilt = limit; s.TiltSpeed = Mathf.Min(0f, s.TiltSpeed); }
                else if (s.Tilt < -limit) { s.Tilt = -limit; s.TiltSpeed = Mathf.Max(0f, s.TiltSpeed); }

                // a swinging surface lifts ripples; they die away on their own
                float excite = p.WaveExcite * Mathf.Abs(s.TiltSpeed);
                s.WavePx = Mathf.Clamp(s.WavePx + (excite - s.WavePx / Mathf.Max(p.WaveTau, 1e-3f)) * dt, 0f, p.WaveMaxPx);

                // A speed that builds up over many frames (a walk, a car pulling away) arrives as many small shoves, each under the
                // rest thresholds. Putting the surface to rest between them would throw the action away - at 60 fps a straight
                // walk gave no answer at all. So the surface rests only RestHold after the last shove; with no shove it rests at once.
                if (s.ShoveHold > 0f) s.ShoveHold = Mathf.Max(0f, s.ShoveHold - dt);
                moving = Mathf.Abs(s.Tilt) >= p.RestTilt || Mathf.Abs(s.TiltSpeed) >= p.RestTiltSpeed || s.WavePx >= p.RestWavePx || s.ShoveHold > 0f;
                if (!moving) { s.Tilt = s.TiltSpeed = s.WavePx = s.Phase = 0f; s.Accumulator = 0f; }   // rest = exactly the flat line
                else
                {
                    s.Phase += p.WaveSpeed * dt;
                    if (s.Phase > TwoPi) s.Phase -= TwoPi;
                }
            }

            s.Still = !moving && s.Level == s.Value && s.WetAge < 0f && s.FreshAge < 0f && s.Pour <= 0f;
        }

        /// <summary>One frame of the two vessels: what the player did (if anything) shoves both liquids, then both swing on.
        /// HudVessels308.Tick and the offline timeline (Offline/SimCheck308) call exactly this.</summary>
        public static void Frame(ref LiquidState308 hp, ref LiquidState308 ink, in LiquidParams308 hpParams, in LiquidParams308 inkParams,
            in ActionSample308 action, float sign, bool reduced, float dt)
        {
            if (!reduced && dt > 0f && action.Any)
            {
                Drive(ref hp, in hpParams, in action, sign);
                Drive(ref ink, in inkParams, in action, sign);
            }
            Step(ref hp, in hpParams, reduced, dt);
            Step(ref ink, in inkParams, reduced, dt);
        }

        /// <summary>What the eye sees move, in design px of the vessel: the surface's rise at the glass wall (slope x half the
        /// cavity width) plus the ripple amplitude. 0 = a flat line.</summary>
        public static float AmplitudePx(in LiquidState308 s, float halfWidthPx) => Mathf.Abs(s.Tilt) * halfWidthPx + s.WavePx;
    }

    /// <summary>#308 the measured state of the PLAYER -> what the liquid is shoved with (SPEC §2.3, D308-11b). Reads the body
    /// (its displacement over the motor's own time step, in the heading it is seen in), the turn rate of that heading and the
    /// motor's jump / landing serials - never the motion of the camera (a camera moves without the player doing anything: rig
    /// sway, lock-on pull, collision, a draw-mode offset; on foot the heading is the body's own, only while riding it is the
    /// seat view's because the body's is frozen) and never a held key (a body pushed against a wall does not move).
    /// Output = changes and events only, so a body that stands still yields an all-zero sample, exactly. A speed change is
    /// handed on once it has grown past StepBand since the last one handed on (LastLateral / LastForward = the speed the
    /// liquid was last told), or when the body has stopped: the speed noise of a steady body (uneven frame times over the
    /// float grid of the position) stays under the band and sums to nothing, and a speed that builds up over many frames is
    /// handed on in pieces whose sum is the whole change, whatever the frame rate. A paused stretch is not an action: the
    /// first speed measured after it is a new baseline.
    /// A frame that jumps further than TeleportMetres (respawn, placement) is dropped. Plain struct owned by the presenter;
    /// no static state.</summary>
    public struct ActionMeter308
    {
        public Vector3 LastPosition;
        public float LastYaw, LastLateral, LastForward, LastVertical, TurnRate, LastTurnLateral;
        public int LastJumpSerial, LastLandSerial;
        public int Stage;                   // 0 nothing known, 1 position known, 2 velocity known

        public void Clear() { this = default; }

        /// <summary>position = the player body this frame; right / yawDegrees = the heading it is seen in (the body on foot, the
        /// seat view while riding; flattened here); dt = the time step the motor moved it with (scaled; 0 while paused);
        /// jumpSerial / landSerial / launchSpeed = PlayerMotor.JumpSerial / LandingSerial / JumpLaunchSpeed.
        /// Returns the frame's sample (all zero when nothing was done).</summary>
        public ActionSample308 Sample(Vector3 position, Vector3 right, float yawDegrees, float dt,
            int jumpSerial, int landSerial, float launchSpeed, MotionInputSpec308 spec)
        {
            var a = default(ActionSample308);
            if (spec == null) return a;
            if (dt <= 1e-5f)
            {
                // paused (a menu, a tutorial stop): nothing is measured, and what happens to the body across the pause is not
                // the player's action - the first speed measured after it is a new baseline (a menu that stopped a running
                // body must not slosh the liquid when it closes)
                if (Stage == 2) Stage = 1;
                return a;
            }
            right.y = 0f;
            right = right.sqrMagnitude > 1e-8f ? right.normalized : Vector3.right;
            Vector3 forward = Vector3.Cross(right, Vector3.up);
            Vector3 delta = position - LastPosition;
            bool bad = float.IsNaN(delta.x) || float.IsNaN(delta.y) || float.IsNaN(delta.z) || float.IsNaN(yawDegrees);
            if (Stage == 0 || bad || delta.sqrMagnitude > spec.TeleportMetres * spec.TeleportMetres)
            {
                // a start, a respawn or a broken pose: a new baseline at rest, never an impulse
                Stage = bad ? 0 : 1;
                LastPosition = bad ? Vector3.zero : position; LastYaw = bad ? 0f : yawDegrees;
                LastLateral = LastForward = LastVertical = TurnRate = LastTurnLateral = 0f;
                LastJumpSerial = jumpSerial; LastLandSerial = landSerial;
                return a;
            }
            Vector3 velocity = delta / dt;
            float lateral = Vector3.Dot(velocity, right), forward1 = Vector3.Dot(velocity, forward), vertical = velocity.y;
            // standing = exactly zero: contact jitter under the dead zone is not an action
            if (lateral * lateral + forward1 * forward1 < spec.SpeedDeadzone * spec.SpeedDeadzone) lateral = forward1 = 0f;

            // fast view turning only: the share of the turn rate above the dead zone, as an equivalent sideways speed
            float rawTurn = Mathf.Clamp(Mathf.DeltaAngle(LastYaw, yawDegrees) / dt, -spec.TurnClamp, spec.TurnClamp);
            float k = 1f - Mathf.Exp(-6.28318530718f * Mathf.Max(spec.TurnLowPassHz, .1f) * dt);
            TurnRate = Mathf.Lerp(TurnRate, rawTurn, k);
            if (Mathf.Abs(TurnRate) < 1e-3f) TurnRate = 0f;                      // the filter's tail must end
            float over = Mathf.Max(0f, Mathf.Abs(TurnRate) - spec.TurnDeadzone) * Mathf.Sign(TurnRate);
            float turnLateral = over / 180f * spec.TurnMpsPer180;

            if (Stage == 1)
            {
                // the first velocity after a start / teleport / pause is a baseline, not a change
                Stage = 2;
                LastLateral = lateral; LastForward = forward1;
            }
            else
            {
                float clamp = spec.StepClamp, band = Mathf.Max(0f, spec.StepBand);
                // per axis: handed on when the change since the last one handed on has reached the band, or the body stands
                // (then whatever is left goes, so a start and its stop always sum to zero)
                bool stopped = lateral == 0f && forward1 == 0f;
                float lateralChange = lateral - LastLateral, forwardChange = forward1 - LastForward;
                if (stopped || Mathf.Abs(lateralChange) >= band) { a.LateralStep = Mathf.Clamp(lateralChange, -clamp, clamp); LastLateral = lateral; }
                if (stopped || Mathf.Abs(forwardChange) >= band) { a.ForwardStep = Mathf.Clamp(forwardChange, -clamp, clamp); LastForward = forward1; }
                a.LateralStep += Mathf.Clamp(turnLateral - LastTurnLateral, -clamp, clamp);
                if (jumpSerial != LastJumpSerial) a.JumpSpeed = Mathf.Clamp(launchSpeed, 0f, clamp);
                // the fall speed is the one measured BEFORE this frame: on the landing frame the body has already stopped
                if (landSerial != LastLandSerial) a.LandSpeed = Mathf.Clamp(-LastVertical, 0f, clamp);
            }
            LastPosition = position; LastYaw = yawDegrees; LastVertical = vertical;
            LastTurnLateral = turnLateral; LastJumpSerial = jumpSerial; LastLandSerial = landSerial;
            return a;
        }
    }
}
