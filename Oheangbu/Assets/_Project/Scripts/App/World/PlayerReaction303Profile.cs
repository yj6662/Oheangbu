using UnityEngine;

namespace Oheangbu.App.World
{
    // #303 player body reactions (SPEC-PLAYER-FEEL-300 H), presentation only: hit flinches by direction, a large stagger,
    // the fall on death and the get-up at the rest spot, lean into turns / acceleration / dodges, and a torso accent on a
    // successful cast. Gameplay (damage, immunity, respawn rules, recognition) is untouched. All values TEST.
    [CreateAssetMenu(menuName = "Oheangbu/Player/Reaction 303")]
    public sealed class PlayerReaction303Profile : ScriptableObject
    {
        [Header("Animator layer (added by Player303Build)")]
        public string Layer = "Reaction303";
        public string HitFront = "HitFront", HitBack = "HitBack", HitLeft = "HitLeft", HitRight = "HitRight", HitLarge = "HitLarge", Death = "Death", GetUp = "GetUp";
        [Tooltip("Clip lengths written by the build (seconds).")] public float DeathSeconds = 2f, GetUpSeconds = 3f;

        [Header("Hits")]
        [Tooltip("Share of max HP from which a single hit plays the large stagger.")] public float LargeHitShare = .22f;
        public float HitFadeIn = .06f, HitFadeOut = .24f;
        [Tooltip("Seconds of the hit clip shown before it fades back.")] public float HitHold = .55f, LargeHitHold = 1.0f;
        [Tooltip("Layer weight while moving: the legs keep the locomotion, the body flinches.")] public float MovingWeight = .55f;
        public float MovingSpeed = 1.2f;
        [Tooltip("The nearest living enemy inside this radius sets the hit direction.")] public float ThreatRadius = 12f;

        [Header("Death → rest spot")]
        public float DeathFadeIn = .12f;
        [Tooltip("Veil: starts, is opaque, the respawn happens under it, then it lifts.")]
        public float VeilStart = 1.45f, VeilFull = 2.15f, RespawnAt = 2.3f, VeilOutStart = 2.6f, VeilOutSeconds = .9f;
        public float GetUpFadeOut = .3f;
        [Tooltip("Share of the get-up clip after which input returns.")] [Range(0, 1)] public float GetUpRelease = .72f;
        public Color VeilColor = new Color(.035f, .029f, .023f, 1f);

        // SPEC-PLAYER-FEEL-300 H.1 (RESPAWN_CAMERA_PLAN §2.6), read by WorldMacroPlayerGetUpCamera303. New fields load their
        // class defaults on the existing PlayerReaction303.asset (no asset edit); Player303Build import rebuilds from them too.
        [Header("Get-up camera (render-time, presentation only)")]
        [Tooltip("After the rest-spot respawn the view frames the rising body, then eases back to the player's view.")]
        public bool GetUpCamera = true;
        [Tooltip("Degrees the framing looks down at the body (3rd person).")] public float GetUpCameraPitch = 26f;
        [Tooltip("Metres from the body focus to the camera (the normal shoulder view sits 2.8 behind the eye).")] public float GetUpCameraDistance = 3.2f;
        [Tooltip("Focus between the hips (0) and the head (1).")] [Range(0, 1)] public float GetUpCameraFocus = .45f;
        [Tooltip("Get-up progress where the ease back to the player's view starts / ends (input returns at GetUpRelease).")]
        [Range(0, 1)] public float GetUpCameraReturnStart = .40f, GetUpCameraReturnEnd = .85f;
        [Tooltip("Seconds to let go when the player looks, moves or draws, or the get-up is interrupted.")] public float GetUpCameraFade = .2f;
        [Tooltip("Smoothing (1/s) of the bone focus against animation noise.")] public float GetUpCameraResponse = 8f;
        [Tooltip("Planar speed (m/s) that counts as the player moving once input is back.")] public float GetUpCameraMoveRelease = .3f;
        [Header("Get-up eye (first person / near view only)")]
        [Tooltip("Share of the head's drop the eye follows (0 = the fixed eye height).")] [Range(0, 1)] public float GetUpEyeFollow = 1f;
        [Tooltip("Metres the eye sits above the head bone.")] public float GetUpEyeAboveHead = .06f;
        [Tooltip("Degrees the eye looks up while the torso lies flat.")] public float GetUpEyeLookUp = 45f;

        [Header("Lean")]
        [Tooltip("Roll degrees per (deg/s of turning x m/s of speed).")] public float TurnLean = .0045f;
        public float MaxTurnLean = 8f;
        [Tooltip("Pitch degrees per m/s² of forward acceleration.")] public float AccelLean = .35f;
        public float MaxAccelLean = 6f;
        public float LeanResponse = 8f;
        public float DodgeLean = 15f;

        [Header("Hit recoil (lean spring)")]
        [Tooltip("Degrees the upper body is knocked away from the blow (small hits; large hits x1.6).")] public float RecoilDegrees = 11f;
        public float RecoilFrequency = 7f, RecoilDamping = .45f;

        [Header("Cast accent")]
        [Tooltip("The body is hidden while drawing and returns after the near-view cast hold; the accent starts then.")]
        public float CastDelay = .3f;
        public float CastTwist = 16f, CastLean = 7f, CastSeconds = .5f;
    }
}
