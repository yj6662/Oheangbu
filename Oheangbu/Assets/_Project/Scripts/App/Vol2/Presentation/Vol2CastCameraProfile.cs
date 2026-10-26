using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>D308-150 V12 cast camera treatments (LAB-ONLY prototype, TEST): every number of the four treatments the user compares.
    /// Loaded from Resources "Vol2CastCamera/Vol2CastCameraProfile" (the editor command castcam:make-profile writes it); missing = these field
    /// defaults. The director works on a runtime copy, so a castcam:set never changes the asset. Times are seconds after the commit (the cast's
    /// StartUs on the scope clock); the swing apex launch is 0.28 s (path) / 0.53 s (fan) in the lab.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/Vol2/Cast Camera Profile", fileName = "Vol2CastCameraProfile")]
    public sealed class Vol2CastCameraProfile : ScriptableObject
    {
        public const string ResourcePath = "Vol2CastCamera/Vol2CastCameraProfile";

        [Header("A Frame: ease the eye up / back / right and pitch down, so caster and impact both fit")]
        public float FrameIn = .28f;
        public float FrameHoldUntil = 1.1f;
        public float FrameOut = .4f;
        [Tooltip("Added to the camera position in its own yaw frame (right, up, forward), metres")]
        public Vector3 FrameOffset = new Vector3(.9f, .9f, -.6f);
        public float FramePitchAdd = 20f;
        public float FramePitchMax = 28f;
        public float FrameYawAdd = -10f;
        public float FrameFovAdd = 0f;

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
