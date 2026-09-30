using UnityEngine;

namespace Oheangbu.App.World
{
    // D301 앞잡(글자 난사) — every number is TEST data (COMBAT-GROGGY amendment).
    [CreateAssetMenu(menuName = "Oheangbu/World Macro/Riposte 301 Profile")]
    public sealed class Riposte301Profile : ScriptableObject
    {
        [Min(1f)] public float Range = 14f;
        [Tooltip("Damage of the whole volley before the open weak point's multiplier.")]
        [Min(0f)] public float TotalDamage = 45f;
        [Range(4, 64)] public int GlyphCount = 32;
        [Tooltip("Seconds over which the glyphs leave the caster.")]
        [Min(0f)] public float LaunchSpread = .5f;
        [Min(.1f)] public float Flight = .75f;
        [Min(.05f)] public float GlyphSize = .55f;
        [Tooltip("Radius and height of the fan the glyphs rise from, behind and above the caster.")]
        public float SpawnRadius = 1.8f, SpawnHeight = 1.9f, ArcHeight = 2.4f;
        [Tooltip("Glyph atlas (white glyphs on alpha) and its grid.")]
        public Material GlyphMaterial;
        public int AtlasColumns = 5, AtlasRows = 5;
        public Material TrailMaterial;
        [Tooltip("Element (0 목 1 화 2 토 3 금 4 수) of each atlas cell's initial consonant, row-major.")]
        public int[] CellElements = { 0, 1, 2, 3, 4, 1, 3, 4, 2, 0, 1, 2, 3, 4, 0, 0, 0, 0, 2, 2, 3, 4, 4, 1, 3 };
        [Tooltip("Low-saturation ink per element, ART-COLOR order 목 청록 · 화 주홍 · 토 황토 · 금 은백 · 수 현색.")]
        public Color[] ElementInks =
        {
            new Color(.16f, .27f, .23f, 1f), new Color(.40f, .17f, .12f, 1f), new Color(.36f, .29f, .17f, 1f),
            new Color(.42f, .43f, .44f, 1f), new Color(.08f, .10f, .14f, 1f),
        };
        public Color Ink = new Color(.05f, .045f, .045f, 1f);
        [Tooltip("Momentary contact tint (ART-INK: only at the hit, never sustained).")]
        public Color HitFlash = new Color(1.4f, .35f, .18f, 1f);
        [Tooltip("The volley consumes the open window: groggy and weak point reset after the last glyph.")]
        public bool ConsumesWindow = true;
        [Tooltip("Layer the glyphs draw on (VfxAfterFog, drawn after the realm fog).")]
        public int Layer = 20;

        [Header("#302 magic circles (SPEC-PLAYER-FEEL-300 F)")]
        [Tooltip("Circles drawn in an arc in front of and above the caster; each pours out a share of the glyphs.")]
        public bool MagicCircles = true;
        public Material RingMaterial, SplashMaterial;
        [Tooltip("When set, the circles pour out 도깨비불 orbs of their element instead of glyphs (the circle keeps its initial).")]
        public Material OrbMaterial;
        public float OrbSize = .75f;
        [Range(0, 1)] public float OrbLighten = .35f;
        [Range(1, 24)] public int CircleCount = 12;
        [Tooltip("Circles burst into a volume in front of and above the caster: horizontal spread, distance, height and size ranges.")]
        public float CircleArcDegrees = 150f, CircleDistance = 2.3f, CircleHeight = 2.2f, CircleLift = .55f, CircleSize = 1.25f;
        public Vector2 CircleDistanceRange = new Vector2(1.0f, 3.4f), CircleHeightRange = new Vector2(2.6f, 5.0f), CircleSizeRange = new Vector2(.55f, 2.0f);
        [Tooltip("Half width (m) of the burst, left and right of the caster.")]
        public float CircleSpread = 4.2f;
        [Tooltip("Pop: how far a circle overshoots its size as it bursts open (0 = none).")]
        public float CirclePop = .35f;
        [Tooltip("Seconds each circle takes to draw in, and between one circle and the next.")]
        public float CircleDraw = .3f, CircleStagger = .045f, CircleSpin = 40f;
        [Tooltip("Seconds between glyphs leaving the circles (round robin).")]
        public float FireInterval = .05f;
        [Header("#302 도깨비불 flight")]
        public float WispFlightMin = .55f, WispFlightMax = .8f, WispAmplitude = .95f, WispLoopsMin = 1.2f, WispLoopsMax = 2.4f;
        [Tooltip("Afterimages: one faint copy every Interval seconds, fading over Life.")]
        public float GhostInterval = .035f, GhostLife = .24f, GhostAlpha = .28f, TrailAlpha = .3f;
        [Header("#302 impact")]
        [Range(0, 12)] public int SplashCount = 5;
        public float SplashSpeed = 3.2f, SplashLife = .38f, SplashSize = .28f, ShakeAmplitude = .045f, ShakeDecay = 9f;
        [Tooltip("Hit stop on the last glyph: time scale and real seconds.")]
        public float HitStopScale = .06f, HitStopSeconds = .08f;
        [Header("#302 casting pose (third-person body)")]
        public bool CastPose = true;
        public float PoseRaise = .2f, PoseLower = .3f;
    }
}
