using UnityEngine;

namespace Oheangbu.App.World
{
    // D308-148 앞잡 = 일필휘지 한 획 (step 5 of the riposte plan): every number is TEST data, judged in the mp4 pass.
    // Referenced by Riposte301Profile.Stroke (RiposteStroke308Build sets it; Resources/Riposte301Profile.asset keeps its GUID).
    // The plain-number halves (Shape, Timing) are the structs the pure code reads - this class only holds them for the inspector.
    [CreateAssetMenu(menuName = "Oheangbu/World Macro/Riposte Stroke 308 Profile")]
    public sealed class RiposteStroke308Profile : ScriptableObject
    {
        [Header("Materials (set by RiposteStroke308Build)")]
        [Tooltip("Oheangbu/SpellVFX308/RiposteInk: matte opaque cutout, double sided, depth tested.")]
        public Material StrokeMaterial;
        [Tooltip("Oheangbu/SpellVFX308/InkFlipbook with the flake / speck atlas (4 x 2). Erodes through its field, never fades.")]
        public Material FlakeMaterial;
        [Tooltip("0 = an ordinary world object that depth-tests against the body. VfxAfterFog (20) is NOT used: it must not paint over the enemy.")]
        public int Layer = 0;

        [Header("Pool")]
        [Range(1, 4)] public int PoolSize = 2;

        [Header("Clock [TEST] seconds since the fire (the brief: reveal .05-.20, pierce ~.18, dry .60, crack .60-.85, fall .85-1.20)")]
        public float PrepSeconds = .05f;
        public float RevealSeconds = .15f;
        [Tooltip("> 1: the sweep starts slow and ends fast.")]
        [Min(.2f)] public float RevealPower = 1.5f;
        public float DryStart = .60f, CrackEnd = .85f, FallEnd = 1.20f;
        [Tooltip("Seconds after the pierce that the weak-point window is closed by key (the enemy stays staggered while the stroke dries).")]
        [Min(0f)] public float WindowCloseDelay = .45f;

        [Header("Colour drain: the riposte's own transient global volume [TEST]")]
        public float LookRise = .04f, LookHold = .08f, LookReturn = .45f;
        [Tooltip("Saturation taken out at the peak (a positive number; the volume writes minus this). Exposure is never touched.")]
        [Range(0f, 100f)] public float Desaturate = 65f;
        [Tooltip("The paper the highlights lean toward (its hue at unit luminance: the value is kept).")]
        public Color PaperColor = new Color(.91f, .86f, .76f, 1f);
        [Range(0f, 1f)] public float PaperAmount = .30f;
        [Tooltip("Above LookDriver308's volume (1000) so its saturation wins while this one has weight.")]
        public float VolumePriority = 1100f;

        [Header("Shape [TEST] (metres; the plane is tilted by PlaneYawDegrees about the vertical, fixed at the fire)")]
        [Range(24, 64)] public int Samples = 48;
        [Range(0f, 80f)] public float PlaneYawDegrees = 35f;
        [Range(10f, 70f)] public float SlashDegrees = 38f;
        public float LeadIn = 3.4f, LeadBulge = .28f;
        [Tooltip("At least; the target's body radius wins when larger.")]
        public float TorsoHalf = .35f;
        public float TailLength = 1.8f, TailMaxReach = 7f, TailBulge = .15f;
        [Tooltip("The tail that runs to the brush tip's projection stops this far below the torso mark (about the floor): it never dives under the ground.")]
        public float TailFloorDrop = 1.0f;
        [Tooltip("Width: wet start swelling to HeadWidth at HeadPeak (0..1 of the lead), squeezed to TorsoWidth across the body (an arm's width), thin dry tail.")]
        public float HeadWidth = 1.5f, StartWidthScale = .45f, TorsoWidth = .28f, TailWidth = .05f;
        [Range(.05f, .6f)] public float HeadPeak = .14f;
        [Range(0f, 1f)] public float TailInk = .35f;

        [Header("Flakes and specks [TEST] (one pooled ParticleSystem, flat matte pieces, nothing rises)")]
        [Range(0, 48)] public int FlakeCount = 24;
        public Vector2 FlakeSize = new Vector2(.10f, .24f);
        public Vector2 FlakeLife = new Vector2(.55f, .90f);
        [Tooltip("ParticleSystem gravity modifier.")]
        public float FlakeGravity = 1.6f;
        [Tooltip("Initial downward speed and sideways spread (m/s).")]
        public float FlakeDrop = .5f, FlakeSpread = .6f;
        public Color FlakeInk = new Color(.16f, .15f, .14f, 1f);
        [Tooltip("Specks thrown at the pierce (dry points and blots only; no round glossy drops).")]
        [Range(0, 32)] public int SplashCount = 14;
        public Vector2 SplashSize = new Vector2(.04f, .12f);
        public Vector2 SplashLife = new Vector2(.30f, .55f);
        public float SplashSpeed = 3.0f;
        [Range(16, 256)] public int MaxParticles = 96;

        [Header("Pierce shake [TEST] (once at the pierce; 0 = off, judged first with it off)")]
        [Min(0f)] public float PierceShake = 0f;

        public bool Ready => StrokeMaterial != null && FlakeMaterial != null;

        public RiposteStroke308Shape Shape() => new RiposteStroke308Shape
        {
            Samples = Samples, PlaneYawDegrees = PlaneYawDegrees, SlashDegrees = SlashDegrees,
            LeadIn = LeadIn, LeadBulge = LeadBulge, TorsoHalf = TorsoHalf,
            TailLength = TailLength, TailMaxReach = TailMaxReach, TailBulge = TailBulge, TailFloorDrop = TailFloorDrop,
            HeadWidth = HeadWidth, HeadPeak = HeadPeak, StartWidthScale = StartWidthScale, TorsoWidth = TorsoWidth, TailWidth = TailWidth, TailInk = TailInk,
        };

        public RiposteStroke308Timing Timing() => new RiposteStroke308Timing
        {
            PrepSeconds = PrepSeconds, RevealSeconds = RevealSeconds, RevealPower = RevealPower,
            LookRise = LookRise, LookHold = LookHold, LookReturn = LookReturn,
            DryStart = DryStart, CrackEnd = CrackEnd, FallEnd = FallEnd,
        };
    }
}
