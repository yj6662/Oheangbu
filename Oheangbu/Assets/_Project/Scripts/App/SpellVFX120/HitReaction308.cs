using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>The timetable of one hit reaction (seconds from the hit). Zero lengths mean the piece is not played.</summary>
    public struct HitReactionPlan308
    {
        public int Flicks;          // flat-value flicks, the pop included (0 = no reaction at all)
        public float Period;        // from one flick's start to the next
        public float On;            // a flick's on-time
        public float Pop;           // the first flick's on-time
        public float Stain;         // the stain is drawn from Pop to Pop + Stain
        public float StainRadius;   // m
        public float Knock;         // the pushed silhouette is drawn from 0 to Knock
        public float KnockMeters;
        public float End;           // nothing of the reaction is drawn at or after this
        public bool Big;            // each whole-body flick spends a full-screen flip token
    }

    // SPEC-SPELL-DEPLOY-308 section 7 (D308-10c): the hit reaction's rules, pure - HitFlicker308 draws what this says, and the
    // checks run it without a scene. Presentation only: nothing here is read by a rule.
    //   pop     the whole body in the flat value for Flicker.PopSeconds: it reads on small and far enemies
    //   flicks  the remaining flat-value flicks
    //   stain   after the pop, a blot round the hit point whose cover falls from 1 to 0 (it never grows)
    //   knock   the silhouette pushed aside once, from the hit for Flicker.KnockSeconds
    // Photosensitivity: a big target (a quarter of the screen or more) gets fewer flicks, spends a flip token on each, and its
    // period is never shorter than the limiter's minimum interval (a shorter one would only ever have its second flick refused).
    // Reduced flash: one short flick and the stain, no knock. Flash off: nothing.
    public static class HitReaction308
    {
        /// <summary>How many flat-value flicks (the pop is the first) a hit gets under the user's flash setting.</summary>
        public static int CountFor(SpellDeploy308ProfileSO.FlickerSet f, int tierMax, DeployFlash308 flash, bool big)
        {
            if (f == null || !f.Enabled || flash == DeployFlash308.Off) return 0;
            int count = flash == DeployFlash308.Reduced ? f.ReducedCount : Mathf.Min(f.Count, tierMax);
            return Mathf.Max(0, big ? Mathf.Min(count, f.BigCount) : count);
        }

        public static HitReactionPlan308 Plan(SpellDeploy308ProfileSO.FlickerSet f, float limiterMinInterval, int tierMax, DeployFlash308 flash, bool big, float bodyHeight)
        {
            var plan = new HitReactionPlan308 { Big = big, Flicks = CountFor(f, tierMax, flash, big) };
            if (plan.Flicks <= 0) { plan.Flicks = 0; return plan; }
            float hz = big ? f.BigHz : f.Hz;
            plan.Period = 1f / Mathf.Max(.5f, hz);
            if (big) plan.Period = Mathf.Max(plan.Period, limiterMinInterval);
            plan.On = Mathf.Min(f.OnSeconds, .5f * plan.Period);
            bool full = flash == DeployFlash308.Full;
            plan.Pop = full ? Mathf.Min(Mathf.Max(f.PopSeconds, plan.On), .9f * plan.Period) : plan.On;
            float height = Mathf.Max(.2f, bodyHeight);
            plan.Stain = f.Stain ? Mathf.Max(0f, f.StainSeconds) : 0f;
            plan.StainRadius = plan.Stain > 0f ? Mathf.Max(.05f, Mathf.Min(height * f.StainRadiusShare, f.StainMaxRadius)) : 0f;
            plan.KnockMeters = f.Knock && full ? Mathf.Clamp(height * f.KnockShare, f.KnockMin, Mathf.Max(f.KnockMin, f.KnockMax)) : 0f;
            plan.Knock = plan.KnockMeters > 0f ? Mathf.Max(0f, f.KnockSeconds) : 0f;
            if (plan.Knock <= 0f) plan.KnockMeters = 0f;
            plan.End = Mathf.Max(plan.Flicks * plan.Period, Mathf.Max(plan.Pop + plan.Stain, plan.Knock));
            return plan;
        }

        /// <summary>Is a whole-body flick on at `t`? `flick` = its index (0 = the pop).</summary>
        public static bool FlickOn(in HitReactionPlan308 plan, double t, out int flick)
        {
            flick = -1;
            if (plan.Flicks <= 0 || t < 0.0 || t >= plan.End) return false;
            flick = (int)(t / plan.Period);
            return flick < plan.Flicks && t - flick * plan.Period <= (flick == 0 ? plan.Pop : plan.On);
        }

        /// <summary>Is the stain drawn at `t`, and how much of its radius does it still cover (1 falling to 0)?</summary>
        public static bool StainOn(in HitReactionPlan308 plan, double t, out float cover)
        {
            cover = 0f;
            if (plan.Flicks <= 0 || plan.Stain <= 0f || t < plan.Pop || t >= plan.Pop + plan.Stain) return false;
            cover = Mathf.Clamp01(1f - (float)((t - plan.Pop) / plan.Stain));
            return true;
        }

        public static bool KnockOn(in HitReactionPlan308 plan, double t) => plan.Flicks > 0 && plan.Knock > 0f && t >= 0.0 && t < plan.Knock;
    }
}
