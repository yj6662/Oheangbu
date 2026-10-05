using Oheangbu.App.World.UI;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 D308-11b proof (SPEC-HUD-LIQUID-308 AC-H16): one scripted stretch of play run through the REAL liquid code -
    /// a synthetic player body moved by the motor's own kinematics, measured by ActionMeter308, driven through
    /// LiquidSim308.Frame exactly like HudVessels308.Tick - with what must hold checked frame by frame:
    ///   the surface is a flat line whenever the player stands still (first window, last window, and from the moment each
    ///   action has settled until the next one), every action gets a clear answer, a stronger action a stronger one, the
    ///   READING never moves with the slosh, and reduced motion is flat throughout.
    /// Timeline: stand 3 s, walk 2 s, stop, run 2 s, stop, dodge, jump and land, a fast view turn, a hit, ink spent, ink
    /// refilled, an impact-frame kick, stand 5 s.
    /// Used by the editor command (checks:sim) and, unchanged, by the offline harness (Offline/SimCheck308.cs). Pure: only
    /// Mathf / Vector3; nothing is read from or written to the project. Not a runtime class (it ships in the editor assembly).</summary>
    public static class HudLiquid308Timeline
    {
        /// <summary>How the player moves in this script = the live motor's numbers (PlayerLocomotion.asset, CombatConfigSO):
        /// the offline driver compares them with those files.</summary>
        public sealed class Body
        {
            public float WalkSpeed = 2.2f, RunSpeed = 5.5f, Acceleration = 16f, Deceleration = 22f;
            public float DodgeDistance = 3f, DodgeSeconds = .25f, JumpHeight = .75f, Gravity = -20f;
            public float TurnDegPerSec = 480f, TurnSeconds = .30f, TurnRampSeconds = .05f;
            public float HpBefore = .80f, HpAfter = .62f, HitDamage = 18f, HitFullDamage = 25f;      // the live presenter: KickInk(.25 + .75 x clamp01(damage / 25))
            public float InkStart = .60f, InkSpent = .45f, InkRefilled = .75f;
            public float ImpactLight = 1f, ImpactKickFloor = .5f;
        }

        /// <summary>What the checks ask for (TEST numbers of AC-H16).</summary>
        public sealed class Limits
        {
            public float HpHalfWidthPx = 51f, InkHalfWidthPx = 44.7f;   // half the cavity at its widest: slope -> px at the glass
            public float RestPx = 0f;               // amplitude allowed while the player stands still (0 = the flat line, exactly)
            public float WalkMinPx = .5f, ActionMinPx = 1f;
            public float SettleSeconds = 3f;
        }

        public const int SegStand0 = 0, SegWalk = 1, SegRun = 2, SegDodge = 3, SegJump = 4, SegTurn = 5, SegHit = 6, SegInkSpent = 7, SegInkRefill = 8, SegImpact = 9, SegStandEnd = 10;
        public static readonly string[] Names = { "stand", "walk", "run", "dodge", "jump+land", "turn", "hit", "ink spent", "ink refill", "impact", "stand" };
        // start of each segment and the end of its ACTION (the rest of the segment is standing still), seconds
        static readonly float[] Start = { 0f, 3f, 8f, 13f, 16f, 19.5f, 22.5f, 25.5f, 28.5f, 31.5f, 34.5f };
        const float End = 39.5f;

        public sealed class Trace
        {
            public int Frames; public float Fps;
            public float[] Time, HpTilt, HpWave, InkTilt, InkWave, HpLevel, InkLevel, HpValue, InkValue, HpAmp, InkAmp, Pour;
            public int[] Segment;
            public bool[] Acting;                   // the player is doing something on this frame (moving, airborne, turning, an event)
        }

        public sealed class Report
        {
            public float[] SegmentStart, ActionEnd, HpPeakPx, InkPeakPx, SettleSeconds;
            public float RestMaxPx, StandFirstMaxPx, StandLastMaxPx;
            public int RestFrames, UnsettledFrames;
            public float LevelDriftMax, ValueDriftMax;      // against the same run without any action: must be exactly 0
            public float ReducedMaxTilt, ReducedMaxWavePx, ReducedMaxPour;
            public bool Calm, Responds, Proportional, Settles, ReadingKept, ReducedFlat;
            public bool Ok => Calm && Responds && Proportional && Settles && ReadingKept && ReducedFlat;
        }

        static float ActionSeconds(int segment, Body b)
        {
            switch (segment)
            {
                case SegWalk: return 2f + b.WalkSpeed / b.Deceleration;
                case SegRun: return 2f + b.RunSpeed / b.Deceleration;
                case SegDodge: return b.DodgeSeconds;
                case SegJump: return 2f * LiquidTimelineMath.JumpSpeed(b) / Mathf.Abs(b.Gravity);
                case SegTurn: return b.TurnSeconds + 2f * b.TurnRampSeconds;
                default: return 0f;                 // stand windows and the instant events
            }
        }

        /// <summary>Runs the script. actions = false leaves the player standing and the jolts out (the value changes stay): the
        /// reference for "the reading does not move with the slosh".</summary>
        public static Trace Run(in LiquidParams308 hpParams, in LiquidParams308 inkParams, MotionInputSpec308 motion, Body b, Limits limits,
            float fps, bool reduced, bool actions, float tiltSign)
        {
            float dt = 1f / fps;
            int frames = Mathf.RoundToInt(End * fps);
            var tr = new Trace
            {
                Frames = frames, Fps = fps, Time = new float[frames], HpTilt = new float[frames], HpWave = new float[frames], InkTilt = new float[frames],
                InkWave = new float[frames], HpLevel = new float[frames], InkLevel = new float[frames], HpValue = new float[frames],
                InkValue = new float[frames], HpAmp = new float[frames], InkAmp = new float[frames], Pour = new float[frames],
                Segment = new int[frames], Acting = new bool[frames],
            };
            var hp = default(LiquidState308); var ink = default(LiquidState308);
            LiquidSim308.Reset(ref hp, b.HpBefore); LiquidSim308.Reset(ref ink, b.InkStart);
            var meter = default(ActionMeter308);
            Vector3 position = Vector3.zero, planar = Vector3.zero;
            float yaw = 0f, vertical = 0f;
            int jumpSerial = 0, landSerial = 0; bool airborne = false;
            float launch = LiquidTimelineMath.JumpSpeed(b);
            int fired = -1;                         // the segment whose instant event has been fired

            for (int f = 0; f < frames; f++)
            {
                float t = f * dt;
                int seg = SegStand0;
                for (int k = Start.Length - 1; k >= 0; k--) if (t >= Start[k] - 1e-5f) { seg = k; break; }
                float into = t - Start[seg];
                bool acting = false;

                // ---- the player (the motor's order and kinematics: turn, then MoveTowards with Acceleration / Deceleration, a dash,
                // a ballistic jump)
                if (actions && seg == SegTurn)
                {
                    float ramp = Mathf.Max(b.TurnRampSeconds, 1e-3f);
                    float w = into < ramp ? into / ramp : into < ramp + b.TurnSeconds ? 1f : Mathf.Max(0f, 1f - (into - ramp - b.TurnSeconds) / ramp);
                    yaw += b.TurnDegPerSec * w * dt;
                }
                Vector3 right = new Vector3(Mathf.Cos(yaw * Mathf.Deg2Rad), 0f, -Mathf.Sin(yaw * Mathf.Deg2Rad));
                Vector3 forward = Vector3.Cross(right, Vector3.up);
                Vector3 desired = Vector3.zero;
                if (actions)
                {
                    if (seg == SegWalk && into < 2f) desired = (right * .5f + forward * .866f) * b.WalkSpeed;
                    else if (seg == SegRun && into < 2f) desired = (right * -.5f + forward * .866f) * b.RunSpeed;
                }
                float rate = desired.sqrMagnitude < planar.sqrMagnitude ? b.Deceleration : b.Acceleration;
                planar = Vector3.MoveTowards(planar, desired, rate * dt);
                Vector3 move = planar;
                if (actions && seg == SegDodge && into < b.DodgeSeconds) move = right * (b.DodgeDistance / b.DodgeSeconds);   // a sideways dodge
                if (actions && seg == SegJump && fired != SegJump) { fired = SegJump; jumpSerial++; airborne = true; vertical = launch; }
                if (airborne)
                {
                    float y = position.y + vertical * dt + .5f * b.Gravity * dt * dt;
                    vertical += b.Gravity * dt;
                    if (y <= 0f && vertical < 0f) { y = 0f; airborne = false; landSerial++; vertical = 0f; }
                    position.y = y;
                }
                position += new Vector3(move.x, 0f, move.z) * dt;
                acting = actions && (move.sqrMagnitude > 1e-6f || airborne || (seg == SegTurn && into < ActionSeconds(SegTurn, b)));

                // ---- the game values and the jolts the game reports (HudController.SetHp01 / SetInk01 / KickInk)
                if (seg == SegHit && fired != SegHit)
                {
                    fired = SegHit; acting = actions;
                    LiquidSim308.SetValue(ref hp, in hpParams, b.HpAfter);
                    if (actions && !reduced) LiquidSim308.Jolt(ref hp, ref ink, in hpParams, in inkParams, .25f + Mathf.Clamp01(b.HitDamage / b.HitFullDamage) * .75f);
                }
                if (seg == SegInkSpent && fired != SegInkSpent) { fired = SegInkSpent; acting = actions; LiquidSim308.SetValue(ref ink, in inkParams, b.InkSpent); }
                if (seg == SegInkRefill && fired != SegInkRefill) { fired = SegInkRefill; acting = actions; LiquidSim308.SetValue(ref ink, in inkParams, b.InkRefilled); }
                if (seg == SegImpact && fired != SegImpact)
                {
                    fired = SegImpact; acting = actions;
                    if (actions && !reduced)
                    {
                        // HudVessels308.Tick: the impact point lies to the right of the cluster, the liquid is thrown to the left
                        float strength = Mathf.Max(b.ImpactLight, b.ImpactKickFloor);
                        LiquidSim308.ImpactKick(ref hp, in hpParams, -1f, strength);
                        LiquidSim308.ImpactKick(ref ink, in inkParams, -1f, strength);
                    }
                }

                // ---- measured, then the frame of the two liquids (= HudVessels308.Tick)
                var sample = meter.Sample(position, right, yaw, dt, jumpSerial, landSerial, launch, motion);
                LiquidSim308.Frame(ref hp, ref ink, in hpParams, in inkParams, in sample, tiltSign, reduced, dt);

                tr.Time[f] = t; tr.Segment[f] = seg; tr.Acting[f] = acting;
                tr.HpTilt[f] = hp.Tilt; tr.HpWave[f] = hp.WavePx; tr.InkTilt[f] = ink.Tilt; tr.InkWave[f] = ink.WavePx;
                tr.HpLevel[f] = hp.Level; tr.InkLevel[f] = ink.Level; tr.HpValue[f] = hp.Value; tr.InkValue[f] = ink.Value;
                tr.Pour[f] = Mathf.Max(hp.Pour, ink.Pour);
                tr.HpAmp[f] = LiquidSim308.AmplitudePx(in hp, limits.HpHalfWidthPx);
                tr.InkAmp[f] = LiquidSim308.AmplitudePx(in ink, limits.InkHalfWidthPx);
            }
            return tr;
        }

        /// <summary>The checks of AC-H16 over one full run, its action-free twin and its reduced-motion twin.</summary>
        public static Report Check(Trace full, Trace noActions, Trace reducedRun, Body b, Limits limits)
        {
            int n = Start.Length;
            var r = new Report { SegmentStart = (float[])Start.Clone(), ActionEnd = new float[n], HpPeakPx = new float[n], InkPeakPx = new float[n], SettleSeconds = new float[n] };
            r.Calm = true; r.Settles = true;
            for (int k = 0; k < n; k++)
            {
                float start = Start[k], end = k + 1 < n ? Start[k + 1] : End, actionEnd = start + ActionSeconds(k, b);
                r.ActionEnd[k] = actionEnd;
                int lastMoving = -1, first = -1, last = -1;
                for (int f = 0; f < full.Frames; f++)
                {
                    float t = full.Time[f];
                    if (t < start - 1e-5f || t >= end - 1e-5f) continue;
                    if (first < 0) first = f;
                    last = f;
                    r.HpPeakPx[k] = Mathf.Max(r.HpPeakPx[k], full.HpAmp[f]); r.InkPeakPx[k] = Mathf.Max(r.InkPeakPx[k], full.InkAmp[f]);
                    if (full.HpAmp[f] > limits.RestPx || full.InkAmp[f] > limits.RestPx) lastMoving = f;
                }
                bool standWindow = k == SegStand0 || k == SegStandEnd;
                if (standWindow)
                {
                    float max = Mathf.Max(r.HpPeakPx[k], r.InkPeakPx[k]);
                    if (k == SegStand0) r.StandFirstMaxPx = max; else r.StandLastMaxPx = max;
                    r.RestMaxPx = Mathf.Max(r.RestMaxPx, max);
                    r.RestFrames += last - first + 1;
                    if (max > limits.RestPx) r.Calm = false;
                    r.SettleSeconds[k] = 0f;
                    continue;
                }
                // settled = the first frame after which nothing moves any more; from there to the next action the player stands still
                float settledAt = lastMoving < 0 ? actionEnd : full.Time[lastMoving] + 1f / full.Fps;
                r.SettleSeconds[k] = Mathf.Max(0f, settledAt - actionEnd);
                if (r.SettleSeconds[k] > limits.SettleSeconds || settledAt > end - 2f / full.Fps) { r.Settles = false; r.UnsettledFrames += 1; }
                r.RestFrames += lastMoving < 0 ? last - first + 1 : last - lastMoving;   // frames after it settled: all at amplitude <= RestPx by construction
            }
            // every action is answered: movement and the hit on the HP liquid, ink events on the ink, the impact on both
            r.Responds = r.HpPeakPx[SegWalk] >= limits.WalkMinPx && r.HpPeakPx[SegRun] >= limits.ActionMinPx && r.HpPeakPx[SegDodge] >= limits.ActionMinPx
                && r.HpPeakPx[SegJump] >= limits.ActionMinPx && r.HpPeakPx[SegTurn] >= limits.ActionMinPx && r.HpPeakPx[SegHit] >= limits.ActionMinPx
                && r.InkPeakPx[SegInkSpent] >= limits.WalkMinPx && r.InkPeakPx[SegInkRefill] >= limits.ActionMinPx
                && r.HpPeakPx[SegImpact] >= limits.ActionMinPx && r.InkPeakPx[SegImpact] >= limits.ActionMinPx;
            r.Proportional = r.HpPeakPx[SegWalk] < r.HpPeakPx[SegRun] && r.HpPeakPx[SegRun] < r.HpPeakPx[SegDodge]
                && r.InkPeakPx[SegWalk] < r.InkPeakPx[SegRun] && r.InkPeakPx[SegRun] < r.InkPeakPx[SegDodge];
            for (int f = 0; f < full.Frames; f++)
            {
                r.LevelDriftMax = Mathf.Max(r.LevelDriftMax, Mathf.Max(Mathf.Abs(full.HpLevel[f] - noActions.HpLevel[f]), Mathf.Abs(full.InkLevel[f] - noActions.InkLevel[f])));
                r.ValueDriftMax = Mathf.Max(r.ValueDriftMax, Mathf.Max(Mathf.Abs(full.HpValue[f] - noActions.HpValue[f]), Mathf.Abs(full.InkValue[f] - noActions.InkValue[f])));
                r.ReducedMaxTilt = Mathf.Max(r.ReducedMaxTilt, Mathf.Max(Mathf.Abs(reducedRun.HpTilt[f]), Mathf.Abs(reducedRun.InkTilt[f])));
                r.ReducedMaxWavePx = Mathf.Max(r.ReducedMaxWavePx, Mathf.Max(reducedRun.HpWave[f], reducedRun.InkWave[f]));
                r.ReducedMaxPour = Mathf.Max(r.ReducedMaxPour, reducedRun.Pour[f]);
            }
            r.ReadingKept = r.LevelDriftMax == 0f && r.ValueDriftMax == 0f;
            r.ReducedFlat = r.ReducedMaxTilt == 0f && r.ReducedMaxWavePx == 0f && r.ReducedMaxPour == 0f;
            return r;
        }
    }

    /// <summary>#308 D308-11b review (AC-H16.9): speed that BUILDS UP over many frames with no sideways part - a walk or a run
    /// straight ahead (W alone), a car pulling away and braking. Each frame's share of such a change is smaller than the rest
    /// thresholds of the liquid, so this is the case where a model that rests between shoves gives no answer at all; and it is
    /// the commonest thing a player does. Stand .5 s, speed up, hold the speed 3 s, slow down, stand 4 s - through the same
    /// ActionMeter308 -> LiquidSim308.Frame path as the timeline. Pure.</summary>
    public static class HudLiquid308Straight
    {
        public sealed class Report
        {
            public float HpPeakPx, InkPeakPx;   // the answer (px at the glass)
            public float CruiseMaxPx;           // the last second at one speed: must be the flat line
            public float SettleSeconds;         // from the body standing again to the flat line
            public int Frames;
        }

        public static Report Run(in LiquidParams308 hpParams, in LiquidParams308 inkParams, MotionInputSpec308 motion, HudLiquid308Timeline.Limits limits,
            float fps, float speed, float acceleration, float deceleration, float tiltSign)
        {
            const float startAt = .5f, cruise = 3f, tail = 4f;
            float dt = 1f / fps;
            float stopAt = startAt + speed / acceleration + cruise, stoodAt = stopAt + speed / deceleration;
            int frames = Mathf.RoundToInt((stoodAt + tail) * fps);
            var hp = default(LiquidState308); var ink = default(LiquidState308);
            LiquidSim308.Reset(ref hp, .8f); LiquidSim308.Reset(ref ink, .6f);
            var meter = default(ActionMeter308);
            Vector3 position = Vector3.zero; float v = 0f; int lastMoving = -1;
            var r = new Report { Frames = frames };
            for (int f = 0; f < frames; f++)
            {
                float t = f * dt;
                float want = t >= startAt && t < stopAt ? speed : 0f;
                v = Mathf.MoveTowards(v, want, (want < v ? deceleration : acceleration) * dt);
                position += Vector3.forward * (v * dt);                       // right = +x, so +z is straight ahead
                var sample = meter.Sample(position, Vector3.right, 0f, dt, 0, 0, 0f, motion);
                LiquidSim308.Frame(ref hp, ref ink, in hpParams, in inkParams, in sample, tiltSign, false, dt);
                float a = LiquidSim308.AmplitudePx(in hp, limits.HpHalfWidthPx), b = LiquidSim308.AmplitudePx(in ink, limits.InkHalfWidthPx);
                r.HpPeakPx = Mathf.Max(r.HpPeakPx, a); r.InkPeakPx = Mathf.Max(r.InkPeakPx, b);
                if (t >= stopAt - 1f && t < stopAt) r.CruiseMaxPx = Mathf.Max(r.CruiseMaxPx, Mathf.Max(a, b));
                if (a > limits.RestPx || b > limits.RestPx) lastMoving = f;
            }
            r.SettleSeconds = lastMoving < 0 ? 0f : Mathf.Max(0f, (lastMoving + 1) * dt - stoodAt);
            return r;
        }
    }

    static class LiquidTimelineMath
    {
        // = PlayerMotor.JumpSpeed(height, gravity)
        public static float JumpSpeed(HudLiquid308Timeline.Body b) => Mathf.Sqrt(2f * Mathf.Max(0f, b.JumpHeight) * Mathf.Abs(b.Gravity));
    }
}
