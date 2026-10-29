using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>A's weight curve. Spring (D308-150 V13, the default): one critically damped weight per director, held at 1 while the latest cast's framing
    /// lasts - a cast inside the hold only extends it, a cast during the return turns it back up smoothly (velocity continuous, no new swing).
    /// AgeEnvelope = the V12 shape (in / hold / out as a pure function of the latest cast's age: every cast starts a new swing) - kept for comparison only.</summary>
    public enum Vol2FrameShape { Spring = 0, AgeEnvelope = 1 }

    /// <summary>D308-150 V12 cast camera treatments (lab prototype, TEST) and V13 (2026-10-26: A, softened, is the default of every vol.2 scope):
    /// every number of the four treatments. Loaded from Resources "Vol2CastCamera/Vol2CastCameraProfile" (the editor command castcam:make-profile writes it);
    /// missing = these field defaults. The director works on a runtime copy, so a castcam:set never changes the asset. Times are seconds after the commit
    /// (the cast's StartUs on the scope clock); the swing apex launch is 0.28 s (path) / 0.53 s (fan) in the lab.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/Vol2/Cast Camera Profile", fileName = "Vol2CastCameraProfile")]
    public sealed class Vol2CastCameraProfile : ScriptableObject
    {
        public const string ResourcePath = "Vol2CastCamera/Vol2CastCameraProfile";

        [Header("A Frame: ease the eye up / back / right and pitch down, so caster and impact both fit (V13: softened, the vol.2 default)")]
        public Vol2FrameShape FrameShape = Vol2FrameShape.Spring;
        [Tooltip("Scales the offset, the pitch and the yaw of A together (1 = the V13 soft numbers below; 0 = no framing)")]
        [Range(0f, 2f)] public float FrameStrength = 1f;
        [Tooltip("Spring: smooth time (s) of the weight going up (about 90 % at 1.9 x this)")]
        public float FrameRiseSmooth = .15f;
        [Tooltip("Spring: smooth time (s) of the weight going back down")]
        public float FrameReturnSmooth = .25f;
        [Tooltip("Spring: the weight never changes faster than this per second (caps the eye's speed at |offset| x this)")]
        public float FrameMaxRate = 4f;
        [Tooltip("Both shapes: the framing is held until this long after the LATEST cast's commit")]
        public float FrameHoldUntil = 1.1f;
        [Tooltip("AgeEnvelope only (V12): smoothstep in / out seconds")]
        public float FrameIn = .28f;
        public float FrameOut = .4f;
        [Tooltip("Added to the camera position in its own yaw frame (right, up, forward), metres, times FrameStrength")]
        public Vector3 FrameOffset = new Vector3(.45f, .45f, -.3f);
        public float FramePitchAdd = 10f;
        [Tooltip("The framed pitch never goes past this (a camera already looking further down keeps its own pitch)")]
        public float FramePitchMax = 18f;
        public float FrameYawAdd = -5f;
        public float FrameFovAdd = 0f;

        [Header("A boom: the framed eye never goes into a wall (the rig's own SphereCast rule from the pivot)")]
        public bool FrameBoom = true;
        [Tooltip("Sphere radius of the cast (the rig's BoomRadius)")]
        public float BoomRadius = .25f;
        [Tooltip("Pivot height above the caster when the camera has no pivot parent (the capture camera's shoulder rig: 1.7 m)")]
        public float BoomPivotHeight = 1.7f;
        [Tooltip("A blocked offset is pulled in at once and given back at this rate (1 - exp(-rate dt), the rig's CameraBlendSpeed)")]
        public float BoomReleaseRate = 8f;

        [Header("B Fade: dither the caster while it covers the line from the brush to the target")]
        public float FadeIn = .12f;
        public float FadeHoldUntil = 1.2f;
        public float FadeOut = .3f;
        [Range(0f, 1f)] public float FadeMinVisibility = .3f;
        [Tooltip("Fraction of the line samples inside the caster's screen box that counts as fully covered")]
        public float FadeFullCover = .25f;
        [Tooltip("The great brush fades with the body (off: the brush stays whole - the spell leaves from it)")]
        public bool FadeBrush = false;
        [Tooltip("Screen box of the caster grown by this much (viewport units) for the cover test")]
        public float FadeRectMargin = .02f;

        [Header("C Widen: wider field of view and a nod down, nothing moves")]
        public float WidenIn = .2f;
        public float WidenHoldUntil = 1.0f;
        public float WidenOut = .35f;
        public float WidenFovAdd = 15f;
        public float WidenPitchAdd = 10f;
        [Tooltip("Optional pull back (m) along the camera's flat back; 0 keeps C a pure render-time offset (nod + FOV)")]
        public float WidenBack = 0f;

        [Header("D Cine: a short three-quarter side view (big spells only in a real game)")]
        public float CineIn = .3f;
        public float CineHoldUntil = 1.2f;
        public float CineOut = .35f;
        [Tooltip("Eye beside the line: metres to the camera's side (right), up from the caster's feet, along the aim from the caster")]
        public float CineSide = 3.4f;
        public float CineHeight = 2.4f;
        public float CineAlong = -1f;
        [Tooltip("Looks at this point between caster (0) and target (1), this high")]
        public float CineLookAlong = .5f;
        public float CineLookHeight = .9f;
        public float CineFov = 55f;
        [Tooltip("Target distance used when the cast has no target (m)")]
        public float CineNoTargetDistance = 5f;

        [Header("Focus (the target the treatments frame)")]
        public float FocusHeight = .9f;
        public float FocusMaxAngle = 60f;
        public float FocusMaxDistance = 25f;
        public float FocusNoTargetDistance = 5f;
        [Tooltip("The cover test's line starts this far ahead of the caster, this high (the brush tip region)")]
        public float LineStartAhead = .8f;
        public float LineStartHeight = 1.2f;

        /// <summary>The age-driven treatments' end (s after the commit). A on the spring has no fixed end: it lasts while its weight does.</summary>
        public float EndOf(Vol2CastCamTreatment t)
        {
            float e = 0f;
            if ((t & Vol2CastCamTreatment.Frame) != 0) e = Mathf.Max(e, FrameHoldUntil + FrameOut);
            if ((t & Vol2CastCamTreatment.Fade) != 0) e = Mathf.Max(e, FadeHoldUntil + FadeOut);
            if ((t & Vol2CastCamTreatment.Widen) != 0) e = Mathf.Max(e, WidenHoldUntil + WidenOut);
            if ((t & Vol2CastCamTreatment.Cine) != 0) e = Mathf.Max(e, CineHoldUntil + CineOut);
            return e;
        }

        public static Vol2CastCameraProfile LoadCopy()
        {
            var asset = Resources.Load<Vol2CastCameraProfile>(ResourcePath);
            var copy = asset != null ? Instantiate(asset) : CreateInstance<Vol2CastCameraProfile>();
            copy.name = asset != null ? asset.name + " (runtime copy)" : "Vol2CastCameraProfile (defaults)";
            copy.hideFlags = HideFlags.DontSave;
            return copy;
        }
    }
}
