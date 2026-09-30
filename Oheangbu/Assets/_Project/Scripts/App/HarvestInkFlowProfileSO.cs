using UnityEngine;
namespace Oheangbu.App
{
    [CreateAssetMenu(menuName="Oheangbu/Playtest/Harvest Ink Flow")]
    public sealed class HarvestInkFlowProfileSO : ScriptableObject
    {
        public Material Material;
        public Color InkColor = new Color(.105f,.095f,.08f,.92f);
        [Tooltip("[LEGACY #132 held flow] not read by the chunk pull")] [Range(0,2)] public int FineThreads = 2;
        [Range(12,48)] public int Points = 32;
        [Tooltip("[LEGACY #132 held flow] RootWidth; the chunk pull reads TendrilRootWidth")] [Min(.001f)] public float RootWidth = .036f;
        [Tooltip("Tendril width at the brush tip and the final size of the flying chunk")] [Min(.001f)] public float TipWidth = .006f;
        [Tooltip("[LEGACY #132 held flow] Spread; the chunk pull reads TendrilSpread")] [Min(0f)] public float Spread = .055f;
        [Min(0f)] public float Sag = .12f;
        [Tooltip("[LEGACY #132 held flow] not read by the chunk pull")] [Min(.03f)] public float RevealSeconds = .22f, ReleaseSeconds = .18f;
        [Tooltip("[LEGACY #132 held flow] not read by the chunk pull")] [Min(.1f)] public float FlowMetersPerSecond = 2.6f;
        [Tooltip("Droplet pool size (spatter, splat and flight trail share it)")] [Range(0,20)] public int Droplets = 12;
        [Min(.001f)] public float DropletSize = .018f;

        // D306 #10 [TEST] chunk pull: latch -> tear -> snap -> fly -> absorb. Matte ink only: no emission, no bloom (ART-INK).
        [Header("Chunk pull timing [TEST D306]")]
        [Tooltip("Latch: three tendrils reach from the brush tip to the chest")] [Min(.02f)] public float LatchSeconds = .12f;
        [Tooltip("End of the tear = expected snap (keep equal to CombatConfigSO.HarvestPullSeconds); scales blob growth only, the snap itself is the ChunkExtracted event")] [Min(.05f)] public float TearSeconds = .45f;
        [Tooltip("Flight of the torn chunk along the bezier to the brush tip")] [Min(.05f)] public float FlySeconds = .22f;
        [Tooltip("Brush tip swell after the chunk arrives")] [Min(.02f)] public float AbsorbSeconds = .14f;
        [Tooltip("Tendrils rewinding to the brush through the dry-brush mask (snap and cancel)")] [Min(.02f)] public float RecoilSeconds = .16f;
        [Header("Chunk pull shape [TEST D306]")]
        [Tooltip("Tendril width at the body root (3x the #132 thread)")] [Min(.001f)] public float TendrilRootWidth = .10f;
        [Tooltip("Tendril width at the brush tip")] [Min(.001f)] public float TendrilTipWidth = .02f;
        [Tooltip("Spread of the three tendrils around the spine (m)")] [Min(0f)] public float TendrilSpread = .09f;
        [Tooltip("Taut tremble during the tear (m, Hz)")] [Min(0f)] public float TremorAmplitude = .012f, TremorHz = 23f;
        [Tooltip("Blob diameter at the root when the tear starts (m)")] [Min(.01f)] public float BlobStartSize = .08f;
        [Tooltip("Blob diameter at the root when it snaps (m)")] [Min(.01f)] public float BlobSize = .28f;
        [Tooltip("Stretch of the flying chunk along its velocity (particle velocityScale)")] [Min(0f)] public float FlyStretch = .08f;
        [Tooltip("Dry-brush ink stain on the body while latched (m)")] [Min(.01f)] public float StainSize = .32f;
        [Tooltip("Ink splat on the body at the snap (m)")] [Min(.01f)] public float SplatSize = .45f;
        [Tooltip("Stain/splat fade after the snap or cancel (s)")] [Min(.05f)] public float StainSeconds = .9f;
        [Tooltip("Stain/splat pushed from the chest toward the camera so they sit on the body surface (m)")] [Min(0f)] public float SurfaceOffset = .28f;
        [Tooltip("Droplets spattered from the root during the tear")] [Range(0,8)] public int SpatterDroplets = 5;
        [Tooltip("Droplets left along the flight")] [Range(0,8)] public int FlyDroplets = 7;
        [Tooltip("Brush tip swell diameter at the absorb peak (m)")] [Min(.005f)] public float TipSwell = .07f;
        [Tooltip("HUD ink stroke slosh on absorb (HudController.KickInk). 0=flash only")] [Range(0,1)] public float AbsorbInkKick = .35f;
        [Header("Screen clamps [TEST D306] - fraction of the view height at that depth")]
        [Tooltip("Widest a tendril may appear, so the pull never covers the near view in first person")] [Range(.005f,.2f)] public float MaxScreenWidth01 = .035f;
        [Tooltip("Largest the chunk may appear near the camera")] [Range(.01f,.3f)] public float MaxScreenBlob01 = .08f;
        [Tooltip("Largest the body stain/splat may appear (a close enemy in first person must not blacken the view)")] [Range(.02f,.4f)] public float MaxScreenStain01 = .16f;
        [Tooltip("Thinnest a tendril may appear, so it still reads at 12 m")] [Range(0f,.02f)] public float MinScreenWidth01 = .004f;
        [Header("Optional materials")]
        [Tooltip("Dry-brush stain/splat material (Oheangbu/VFX120/InkPigment, _Soft 1). Empty = Material in bead mode")] public Material StainMaterial;
        [Tooltip("Optional particle-only KtpContactEffect source for the snap splat, retinted to InkColor. Must use an alpha-blended material (additive vanishes as dark ink)")] public GameObject SplatContact;
        [Min(.001f)] public float SplatContactScale = .22f;
    }
}
