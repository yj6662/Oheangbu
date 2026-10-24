using System;

namespace Oheangbu.App
{
    /// <summary>TEMPORARY EXCEPTIONS of slice 9 (S5-b, D308-150 V2: the vol.2 swing driver - the 갈무리 and the ㅓ contacts of a real brush swing).
    /// Numbers and readings the data does not hold; technical stand-ins, none is spell balance. Each is to be confirmed (and moved into the data) or removed
    /// before the vol.2 runtime leaves the lab. Apart from <see cref="Vol2ShapeTuning"/> (TE-S3-n), <see cref="Vol2VerbTuning"/> (TE-S4-n), <see cref="Vol2SplitTuning"/> (TE-S6-n).
    ///  TE-S9s-1  The swing has no contact event. The animation (GreatBrush308Profile.StateEntry) names only the clip's APEX (SwingSweep: 1.0 s clip, apex 0.4 s). The
    ///            contact window is read from the clip time: it opens <see cref="ActiveBeforeApex"/> before the apex and closes <see cref="ActiveAfterApex"/> after it
    ///            (clamped to the clip). A state with no apex marker uses the fractions <see cref="NoApexStartFraction"/> .. <see cref="NoApexEndFraction"/> of the clip.
    ///            Nothing here is a fixed delay: the window moves with the clip the animation really plays (PostApexSpeed, a position variant, a later clip).
    ///  TE-S9s-2  The capsule is the attachment frame's own (TE-S3-2: Vol2ShapeTuning.SwingRadius, sleeve to brush tip), so a 갈무리 first-hit and a ㅓ contact on the
    ///            same body always agree. <see cref="CapsuleRadius"/> &lt;= 0 means "that one"; a positive value overrides it for the first-hit test only.
    ///  TE-S9s-3  Brush tip = the world brush's TipSocket (it hangs on Bristle_08 and follows the bend). Sleeve = the rear hand's grip (GripSocketL, else GripSocket) pulled
    ///            <see cref="SleeveBackMeters"/> toward the butt along the shaft: the rig has no sleeve bone, the two-handed grip is where the sleeve is. If the brush has a
    ///            GripSocket but no TipSocket the tip is derived: GripSocket + its +Y axis x <see cref="FallbackBrushLength"/> (1.25 m). No grip = no pose = no swing contact.
    ///            With a GreatBrushWeapon308 head on the brush (a sword form) the effective tip is the head, not Bristle_08: not handled (a weapon form is a different attack).
    ///  TE-S9s-4  The pose is read once per RENDERED frame in LateUpdate AFTER the gesture rig has put the brush in the hand (execution order 1100), not in FixedUpdate: the
    ///            rig moves the brush in LateUpdate, so a physics step would read a stale pose. Between two frames the capsule is SWEPT: the pose is interpolated in
    ///            <see cref="MaxSubstepMeters"/> steps (at most <see cref="MaxSubstepsPerFrame"/>), and every step is one Vol2SwingEvent, so a fast tip cannot tunnel through a body.
    ///  TE-S9s-5  The bare swing is the left click: in a Vol2 scope the click no longer starts the legacy lock-on pull (HarvestAction.Intercepted, set on each press and only while
    ///            the player's great-brush rig exists - a scene without it keeps the old click) but this driver, which plays
    ///            <see cref="BareSwingState"/> through SpellSwing308.PlayStrike308 (presentation only, the same entry the weapon heads use). It needs no lock-on target and no
    ///            ink ("무먹 붓 휘두르기", always possible). <see cref="GateByCooldown"/>: a click inside the rules' 1.1 s (combat.noInk.cooldown), measured from the last
    ///            REPORT (hit or miss), is refused here so the animation never plays for a swing the rules would refuse.
    ///  TE-S9s-6  Each swing is reported to the 갈무리 exactly once: ReportVol2SwingFirstHit at the first enemy contact, else ReportVol2SwingMissed when the window closes.
    ///            A swing cut BEFORE its window opened (dodge, new draw) was never a swing: no report, no cooldown.
    ///  TE-S9s-7  Like every vol.2 shape the contact is judged on the plane (x, z); height is ignored (Vol2ShapeGeometry.SwingHit). Enemies are the coordinator's latest target
    ///            snapshot (refreshed by every raised event).</summary>
    public sealed class Vol2SwingTuning
    {
        /// <summary>TE-S9s-5: the Animator state the bare swing plays (a SpellSwingProfile308 state name, resolved through GreatBrush308Profile.Aliases).</summary>
        public string BareSwingState = "SwingSweep";
        /// <summary>TE-S9s-1: seconds of CLIP time the window opens before the apex.</summary>
        public float ActiveBeforeApex = 0.15f;
        /// <summary>TE-S9s-1: seconds of CLIP time the window stays open after the apex.</summary>
        public float ActiveAfterApex = 0.20f;
        /// <summary>TE-S9s-1: a state without an apex marker: the window is this fraction ... </summary>
        public float NoApexStartFraction = 0.25f;
        /// <summary>... to this fraction of its clip.</summary>
        public float NoApexEndFraction = 0.70f;
        /// <summary>TE-S9s-2: &lt;= 0 = the attachment frame's SwingRadius (TE-S3-2).</summary>
        public float CapsuleRadius = -1f;
        /// <summary>TE-S9s-3: how far behind the rear hand's grip the sleeve point is (m).</summary>
        public float SleeveBackMeters = 0.12f;
        /// <summary>TE-S9s-3: the brush length used when the brush has no TipSocket (m).</summary>
        public float FallbackBrushLength = 1.25f;
        /// <summary>TE-S9s-4: the longest stretch the tip or the sleeve moves between two raised events (m).</summary>
        public float MaxSubstepMeters = 0.30f;
        public int MaxSubstepsPerFrame = 6;
        /// <summary>TE-S9s-5: refuse a click inside the rules' cooldown. The lab turns it off (its ㅓ rows swing 0.4 s apart to show the second contact).</summary>
        public bool GateByCooldown = true;
        /// <summary>The production source looks for the player's rig this often until it finds one (seconds; the rig is not there in a menu or a loading scene).</summary>
        public float RigSearchSeconds = 0.5f;

        public float Radius(Vol2ShapeTuning shape) => CapsuleRadius > 0f ? CapsuleRadius : (shape != null ? shape.SwingRadius : 0.45f);

        public bool IsValid =>
            Finite(ActiveBeforeApex) && ActiveBeforeApex >= 0f && Finite(ActiveAfterApex) && ActiveAfterApex > 0f &&
            Finite(NoApexStartFraction) && Finite(NoApexEndFraction) && NoApexStartFraction >= 0f && NoApexEndFraction > NoApexStartFraction && NoApexEndFraction <= 1f &&
            Finite(SleeveBackMeters) && Finite(FallbackBrushLength) && FallbackBrushLength > 0f && Finite(MaxSubstepMeters) && MaxSubstepMeters > 0.01f && MaxSubstepsPerFrame >= 1 &&
            !string.IsNullOrEmpty(BareSwingState);

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
