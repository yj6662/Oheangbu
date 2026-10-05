using System;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Data.Spell
{
    // SPEC-SPELL-DEPLOY-308 (D308-10 / D308-10b / D308-10c / D308-13b) [every number TEST]: the spell deploy layer's data.
    // Presentation only: nothing here is read by recognition, power, judgement or the cast plan. No field accepts HDR: colours
    // are ink / paper values, the "light" of an impact frame is a paper value (ART-INK), and the strokes' glow is the canon's
    // MOMENTARY one ("the stroke glows at the moment it is drawn and sinks into ink"): a small LDR amount for a few cels after a
    // stroke's birth, clamped in code below the bloom knee. Time.timeScale has no field here on purpose.
    // Runtime lookup: scene field -> Resources "Deploy308/SpellDeploy308Profile" (created by the editor importer).
    public enum DeployCategory308 { AttackSingle, AttackArea, ComboInstall, Parry, Buff, Summon, Ward, Field, Blank }
    public enum DeployFrame308 { YangSingle, YinSingle, YangArea, YinArea }
    public enum DeployFinal308 { None, Giyeok, Nieun, Mieum, Siot, Ieung }
    public enum DeployFlash308 { Full = 0, Reduced = 1, Off = 2 }
    public enum DeployLegacyBody308 { Keep, Wrap, Retire }
    public enum DeployTier308 { PC = 0, Mobile = 1 }
    /// <summary>D308-10c: when a cast asks for impact frames. GroggyOnly = only when its judged target is groggy at the impact cel
    /// (the default); Always = every attack row (the D308-10 behaviour, kept for comparison captures).</summary>
    public enum DeployImpactTrigger308 { GroggyOnly = 0, Always = 1 }

    [CreateAssetMenu(menuName = "Oheangbu/Spell/Deploy 308 profile TEST", fileName = "SpellDeploy308Profile")]
    public sealed class SpellDeploy308ProfileSO : ScriptableObject
    {
        public const string ResourcesPath = "Deploy308/SpellDeploy308Profile";
        public const float MinResidueLife = 3f, MaxResidueLife = 5f;   // D308-10b "약 3~5초"
        public const float MaxFlipsPerSecond = 3f;                      // D308-10 photosensitivity ceiling (code limit)
        public const float InkCeiling = .85f;                           // LDR ceiling shared with the existing ink shaders
        public const float PaperCeiling = .90f;                         // brightest value an impact frame / HUD reaction may write
        public const float MinImpactFrameSeconds = .016f, MaxImpactFrameSeconds = .042f;   // 3 frames never exceed 126 ms (code limit)
        // D308-10c momentary glow (code limits). The amount is added to the ink in LINEAR units; the glow may lift a stroke pixel
        // to GlowOutputCeiling at most (linear .45 = display .70). URP's bloom only adds above half its linear threshold: the
        // world profiles use 1.15 / 1.2 (knee from .68 / .75) and the lowest in the project is 1.0 (VP_InkLook, knee from .5).
        // .45 is under all of them, so bloom can never pick the glow up, at any data value. The unit command re-checks this
        // against the volume profiles that exist.
        public const float MaxGlowAmount = .35f, GlowOutputCeiling = .45f;
        public const int MaxGlowCels = 4;

        [Header("Master")]
        [Tooltip("Off = the whole layer stays asleep (no director, no footprints, no pass). Rows are switched on one by one on their Vfx120Profile.")]
        public bool LayerEnabled;
        public SpellDeploy308MapSO Map;
        [Tooltip("Letters switched on (maintained by the editor command deploy308-enable). Hook B (casts the adapter skips), the misfire smear, the parry contact star and the hit flicker read it.")]
        public string EnabledLetters = "";
        [Tooltip("Oheangbu/InkBurst308 material (cutout, no emission).")]
        public Material BurstMaterial;
        [Tooltip("Oheangbu/InkResidue308 material (instanced, queue 2050, no depth / normals pass).")]
        public Material ResidueMaterial;
        [Tooltip("Hidden/Oheangbu/ImpactFrame308")]
        public Shader ImpactShader;
        [Tooltip("Hidden/Oheangbu/InkFlat308")]
        public Shader FlatShader;
        public Texture2D Atlas, AtlasMobile, FloodMask;
        [Tooltip("Layer the burst meshes draw on (VfxAfterFog = 20: drawn after the realm fog).")]
        [Range(0, 31)] public int RenderLayer = 20;

        [Serializable]
        public sealed class BeatSet
        {
            [Min(0f)] public float Ignite = .12f, Silence = .06f, Burst = .40f, HoldSingle = .25f, HoldArea = .35f, Melt = .30f;
            [Tooltip("Cel rate of the burst (strokes appear, they do not grow).")]
            [Range(4f, 30f)] public float CelHz = 12f;
            [Tooltip("The burst's first cel is always the judgement clock. Shorter clocks shrink the ignition, then the silence.")]
            [Min(0f)] public float MinLead = .18f;
            [Tooltip("Presentation clock pause on the burst's first cel (game time is untouched).")]
            [Range(0f, .2f)] public float CutPause = .05f;
        }

        [Serializable]
        public sealed class GradeSet
        {
            [Range(0, 12)] public int Bold = 5, Fine = 8, Needles = 6;
            [Range(.05f, 1f)] public float Width = .30f;
            [Range(.2f, 2f)] public float LengthMul = 1f;
            [Range(0, 64)] public int Drops = 24;
            [Range(1, 3)] public int ImpactFrames = 2;
        }

        [Serializable]
        public sealed class StrokeSet
        {
            [Min(.2f)] public float BaseLength = 2.4f;
            [Range(4, 24)] public int CapsuleSegments = 16;
            [Range(0f, .8f)] public float TailShare = .35f;
            [Tooltip("forms3 (D3, was a code constant of look pass 2): share of the dry tail's atlas cell a stroke that is combed out of its tail (DryStart) uses - the cell's solid end, a block as wide as the tail, is left out.")]
            [Range(.3f, 1f)] public float DryStartTailU = .7f;
            [Range(0f, .5f)] public float RimShare = .12f;
            [Tooltip("sRGB value of the wet body / the pooled rim (ink #2A2622 sits between).")]
            [Range(0f, .5f)] public float BodyValue = .22f, RimValue = .05f;
            [Min(.001f)] public float NeedleWidth = .012f;
            [Min(.5f)] public float NeedleLength = 2.2f;
            [Range(.5f, 4f)] public float NeedleMinPixels = 1.5f;
            [Range(4, 40)] public int ColumnStrips = 28;
            [Min(.2f)] public float ColumnWidth = 1.2f, ColumnHeight = 2.6f;
            [Tooltip("A ward's ink wall sinks to this drain while it stands, so it never hides an enemy tell (0 = full height).")]
            [Range(0f, .95f)] public float ColumnHoldDrain = .7f;
            [Tooltip("A ward's ink wall never stands taller than this (m, 0 = the ward body's own height). First person: the wall is a ring round the caster, and at eye height it would hide every enemy tell behind it while it rises.")]
            [Min(0f)] public float WardColumnMaxHeight = 1.2f;
            [Tooltip("Where a summon's column stands, ahead of the cast origin (m).")]
            [Min(0f)] public float SummonForward = 1.6f;
            [Tooltip("Share of the element colour mixed in, LDR (hard ceiling .35).")]
            [Range(0f, .35f)] public float TintMax = .18f;
            [Tooltip("Nothing is drawn nearer than this to the camera; dithered out to NearFade.")]
            [Min(0f)] public float NearClip = .45f, NearFade = .9f;
            [Range(.05f, 1f)] public float ScreenShareMax = .35f;
            [Tooltip("Unused since the area strokes face the eye (kept so the asset keeps its field).")]
            [Range(0f, 60f)] public float AreaTiltDeg = 25f;
            [Tooltip("Area strokes keep a brush stroke's proportion: width as a share of the stroke's length.")]
            [Range(.02f, .3f)] public float AreaWidthShare = .11f;
            [Tooltip("Area strokes rise from the ground to their head: head height as a share of the reach, and its ceiling (m).")]
            [Range(0f, .3f)] public float AreaHeadLift = .14f;
            [Min(0f)] public float AreaHeadLiftMax = 1.4f;
            [Tooltip("Ring bursts: head height as a share of the radius (a crown).")]
            [Range(0f, .6f)] public float AreaCrownLift = .35f;
            [Tooltip("Area strokes: ceiling of the dry start's share of the length.")]
            [Range(0f, .6f)] public float AreaTailShareMax = .3f;
            [Tooltip("Corridor casts: the side strokes cross the corridor at this angle from its direction (chevrons).")]
            [Range(10f, 90f)] public float PathChevronDeg = 65f;
            [Tooltip("Cone casts: width of the sweeps relative to the grade's stroke width.")]
            [Range(.5f, 4f)] public float AreaSweepWidthMul = 1.7f;
            [Min(.02f)] public float IgniteRadius = .12f;
            [Tooltip("Fine dry-brush strokes: width / length relative to the bold ones.")]
            [Range(.05f, 1f)] public float FineWidthMul = .32f, FineLengthMul = .7f;
            [Tooltip("Fan half angle of a single cast's side strokes (deg).")]
            [Range(5f, 90f)] public float SingleSpreadDeg = 34f;
            [Tooltip("D308-10c momentary glow: linear amount added in the element hue on the cel a stroke is born (code ceiling .35). 0 = none. .12 = the wet body reads about .44 on screen at birth, still darker than a daylit ground and far below the bloom knee.")]
            [Range(0f, .35f)] public float GlowAmount = .12f;
            [Tooltip("Cels the glow takes to sink into ink after a stroke's birth (stepped falloff; code ceiling 4). Nothing glows in the hold, the melt, a standing ward, residue or footprints.")]
            [Range(0, 4)] public int GlowCels = 2;
            [Tooltip("Share of the glow the pooled dark rim takes, so the rim stays darker than the body while it glows.")]
            [Range(0f, 1f)] public float GlowRim = .4f;
        }

        [Serializable]
        public sealed class ElementSet
        {
            public string Name = "";
            [Range(.3f, 2f)] public float LengthMul = 1f, WidthMul = 1f;
            [Tooltip("Upward bend (share of the length).")]
            [Range(-1f, 1f)] public float Curve;
            [Tooltip("S bend amplitude (share of the length).")]
            [Range(0f, 1f)] public float SCurve;
            [Tooltip("Tip forks (0 = a blunt wet head).")]
            [Range(0, 3)] public int Split;
            [Range(0f, 3f)] public float TailMul = 1f, NeedleMul = 1f, DropMul = 1f, DropSizeMul = 1f;
            [Tooltip("0 = no wash at all (Metal reads by thinness, not colour).")]
            [Range(0f, 1f)] public float Tint = 1f;
        }

        [Serializable]
        public sealed class CategorySet
        {
            public string Name = "";
            public bool Ignite = true;
            [Tooltip("Bold / fine / needle counts relative to the grade row (0 = none).")]
            [Range(0f, 2f)] public float Bold = 1f, Fine = 1f, Needles = 1f;
            [Range(0, 64)] public int ResidueDrops = 8;
            [Tooltip("An ink column rises and drains (summon / ward).")]
            public bool Column;
            [Tooltip("Thin strokes wind once round the body (buff).")]
            public bool Wind;
            [Tooltip("Residue stays wet while the spell lives, then dries in Residue.FieldTail (field / install mark).")]
            public bool HeldResidue;
            [Tooltip("Always the top grade (the combo trigger).")]
            public bool ForceTopGrade;
        }

        [Serializable]
        public sealed class FinalSet
        {
            public string Name = "";
            [Tooltip("Length of the end treatment as a share of the main stroke (<= .15).")]
            [Range(0f, .15f)] public float TipShare = .12f;
        }

        [Serializable]
        public sealed class CameraSet
        {
            [Range(0f, 4f)] public float FovBreathDeg = 1.2f;
            [Range(.02f, .6f)] public float FovBreathSeconds = .18f;
        }

        [Serializable]
        public sealed class ImpactSet
        {
            public bool Enabled = true;
            [Tooltip("One impact frame, unscaled seconds (one 24 fps cel).")]
            [Range(.016f, .042f)] public float FrameSeconds = .042f;
            [Tooltip("Two-value cut in display (sRGB) luminance.")]
            [Range(.05f, .8f)] public float Threshold = .30f;
            [Range(0f, .3f)] public float InkValue = .07f;
            [Range(.5f, .9f)] public float PaperValue = .88f;
            [Range(0, 32)] public int NeedleCount = 14;
            [Range(.0005f, .02f)] public float NeedleWidth = .0025f;
            [Tooltip("Radius (share of the screen height) of the paper value spreading from the impact point = the 'light'.")]
            [Range(.05f, 2f)] public float RadialReach = .55f;
            [Range(0f, 2f)] public float OutlineStrength = .8f;
            [Tooltip("Reduced setting / over-budget requests: radius of the local form (share of the screen height).")]
            [Range(.05f, .4f)] public float LocalRadius = .22f;
            [Range(1, 3)] public int TokenCapacity = 2;
            [Tooltip("Full-screen value flips per second; the code clamps it to 3.")]
            [Range(0f, 3f)] public float TokensPerSecond = 2f;
            [Min(0f)] public float MinInterval = .40f;
            [Tooltip("Judgement gate: a guard inside its parry window, or an enemy within GateRadius whose predicted impact is nearer than GateSeconds, turns a full-screen frame into the local form.")]
            [Min(0f)] public float GateSeconds = .6f;
            [Min(0f)] public float GateRadius = 14f;
            [Tooltip("Q4 [TEST]: the combo trigger also gets impact frames (3) - under the same groggy condition (D308-10c).")]
            public bool ComboTrigger = true;
            [Tooltip("D308-10c: GroggyOnly = impact frames only for a cast whose judged target is groggy at the impact cel (single: the target; area: any planned hit target; combo trigger: the triggered target). Always = every attack row (comparison captures only).")]
            public DeployImpactTrigger308 Trigger = DeployImpactTrigger308.GroggyOnly;
            [Tooltip("A hit that kills the groggy target closes its weak-point window before the burst's first cel can ask (that cel may be half a cel late). The target still counts as groggy for this long after such a kill.")]
            [Range(0f, .5f)] public float GroggyKillGrace = .12f;
            [Tooltip("Off = the colour buffer at the injection point holds linear values (expected). Editor check item.")]
            public bool TargetIsSrgbEncoded;
            [Tooltip("Full-screen total may not exceed this share of the real parry window (asserted by the unit command).")]
            [Range(.01f, .5f)] public float MaxWindowShare = .15f;
        }

        [Serializable]
        public sealed class FlickerSet
        {
            public bool Enabled = true;
            [Range(0, 8)] public int Count = 4;
            [Range(1f, 12f)] public float Hz = 7f;
            [Range(.016f, .1f)] public float OnSeconds = .042f;
            [Tooltip("Flat value drawn over a dark body / over a light body (sRGB). Not emission.")]
            [Range(0f, .9f)] public float LightValue = .72f, DarkValue = .08f;
            [Tooltip("A target covering at least this share of the screen flickers slower and spends flip tokens.")]
            [Range(.05f, 1f)] public float BigShare = .25f;
            [Range(0, 4)] public int BigCount = 2;
            [Range(1f, 3f)] public float BigHz = 3f;
            [Range(0, 2)] public int ReducedCount = 1;
            [Tooltip("Also flicker on summon / persistent spell hits (default: direct player spells only).")]
            public bool IncludeSummon, IncludePersistent;
            [Range(8, 128)] public int MaxRenderers = 24;
            // ---- D308-10c: the stronger hit reaction (all three are redraws in a flat value, never emission)
            [Tooltip("The first flick is a value pop: the whole body in the flat value for this long, so it reads on small and far enemies (one cel).")]
            [Range(.016f, .125f)] public float PopSeconds = .083f;
            [Tooltip("After the pop a flat-value stain stays round the hit point and shrinks from its rim (it never grows).")]
            public bool Stain = true;
            [Range(.05f, .8f)] public float StainSeconds = .35f;
            [Tooltip("Stain radius as a share of the body's height, and its ceiling (m).")]
            [Range(.05f, 1f)] public float StainRadiusShare = .22f;
            [Min(.05f)] public float StainMaxRadius = .9f;
            [Tooltip("The silhouette is drawn once more, pushed sideways and back: a thin band outside the body for this long (the edge jolts).")]
            public bool Knock = true;
            [Range(.016f, .25f)] public float KnockSeconds = .125f;
            [Tooltip("Knock distance as a share of the body's height, clamped to KnockMin..KnockMax (m).")]
            [Range(0f, .2f)] public float KnockShare = .06f;
            [Min(0f)] public float KnockMin = .05f, KnockMax = .4f;
        }

        // D308-10c "the effect that bursts out when the enemy is hit": what a CONFIRMED hit adds to the plain burst. A cast that
        // hits nothing (no EnemyDamageResolved for it) keeps the plain burst. Everything goes through the residue field: no emission.
        [Serializable]
        public sealed class HitSet
        {
            public bool Enabled = true;
            [Tooltip("Air drops thrown out of the hit point at the middle grade (x GradeScale).")]
            [Range(0, 32)] public int AirDrops = 14;
            [Tooltip("Size of those drops relative to the plain burst's air drops.")]
            [Range(1f, 3f)] public float DropSizeMul = 1.6f;
            [Tooltip("Outward speed in the screen plane (m/s).")]
            [Min(0f)] public float SpeedMin = 2.5f, SpeedMax = 5f;
            [Tooltip("Upward speed added (m/s).")]
            [Min(0f)] public float UpMin = .8f, UpMax = 2.2f;
            [Tooltip("Share of the outward speed that points back at the eye (the splash comes out of the enemy, toward the caster).")]
            [Range(0f, .6f)] public float TowardEye = .2f;
            [Tooltip("The drops leave in a ring that is open at the bottom: how far below the horizontal it reaches (deg). They arc down by gravity afterwards.")]
            [Range(0f, 90f)] public float BelowDeg = 20f;
            [Tooltip("Contact star at the hit point: a screen-plane needle star (lines, not light). Size in metres at the middle grade.")]
            [Min(0f)] public float StarSize = 1.3f;
            [Range(.04f, .3f)] public float StarSeconds = .125f;
            [Tooltip("Wet ink blot at the hit point (screen plane), drying from its rim.")]
            [Min(0f)] public float BlotSize = .9f;
            [Range(.08f, .6f)] public float BlotSeconds = .25f;
            [Tooltip("Wet puddle on the ground under the enemy (m, middle grade). The plain burst's is Residue.ImpactSmear.")]
            [Min(0f)] public float GroundSmear = 1.4f;
            [Tooltip("Low / mid / high grade multipliers for the counts and sizes above.")]
            public float[] GradeScale = { .7f, 1f, 1.35f };
            [Tooltip("Splashes one cast may make (area / volley casts hit several times).")]
            [Range(1, 12)] public int MaxPerCast = 4;
            [Tooltip("Mobile tier: drop count multiplier.")]
            [Range(0f, 1f)] public float MobileScale = .5f;
            [Tooltip("D308-13b: on an enabled Retire attack row the wiring does not spawn the KTP enemy-hit contact (the splash is the hit). Parry contacts and the player's own hit contact are untouched.")]
            public bool ReplaceLegacyContact = true;

            public float Scale(int grade) => GradeScale != null && GradeScale.Length == 3 ? Mathf.Max(0f, GradeScale[Mathf.Clamp(grade, 0, 2)]) : grade <= 0 ? .7f : grade == 1 ? 1f : 1.35f;
        }

        [Serializable]
        public sealed class HudSet
        {
            [Tooltip("Profile-side switch of the HUD reaction (the user switch is UserSettingsData.HudImpactReact).")]
            public bool Enabled = true;
            [Range(0f, 1f)] public float Rim = .8f;
            [Tooltip("Tint of the element's inside (hard ceiling .35).")]
            [Range(0f, .35f)] public float Fill = .30f;
            [Range(0f, 8f)] public float ShadowPixels = 3f;
            [Tooltip("Reduced flash setting: rim only, at this share, no value swap.")]
            [Range(0f, 1f)] public float ReducedScale = .5f;
            [Tooltip("Editor check item: turn on if the lit side of the HP / ink strokes comes out upside down (the overlay target's pixel rows run the other way on this platform).")]
            public bool FlipV;
        }

        /// <summary>forms3 (D10): an airborne drop drawn along the way it travels. The residue field draws an air sprite of the
        /// tailed drop cell as a quad whose long side follows the drop's velocity (head first, the cell's tail behind it) and
        /// grows with its speed; every other air sprite stays the round camera-facing quad it was. Presentation only.</summary>
        [Serializable]
        public sealed class AirTailSet
        {
            [Tooltip("The atlas cell that is drawn along the travel direction (5 = the tailed drop: head at the cell's right, tail at its left). -1 = none: every air sprite is a plain camera-facing quad again.")]
            [Range(-1, 15)] public int Cell = 5;
            [Tooltip("Length of the drawn drop as a multiple of its size: Base at rest, + PerSpeed for every m/s, never above Max.")]
            [Range(1f, 4f)] public float Base = 2.2f;
            [Range(0f, .5f)] public float PerSpeed = .4f;
            [Range(1f, 6f)] public float Max = 5f;
            [Tooltip("m/s below which a drop has no direction of its own yet: it is drawn head down (it is about to fall).")]
            [Min(0f)] public float MinSpeed = .25f;
            [Tooltip("Width of the drawn drop as a share of its size (a drawn-out drop is a little narrower than a round one).")]
            [Range(.4f, 1f)] public float Width = .7f;

            public bool Tailed(int cell) => Cell >= 0 && cell == Cell;
            /// <summary>The direction the head points (unit) and the length of the drawn drop (m) for a drop of `size` m moving at `velocity`.</summary>
            /// pass 4b (Q6): `most` > 0 = this drop's own ceiling (never above Max) - the thrown head, which was a long streak at 5 sizes.
            public float Along(Vector3 velocity, float size, out Vector3 direction, float most = 0f)
            {
                float speed = velocity.magnitude;
                direction = speed > Mathf.Max(1e-4f, MinSpeed) ? velocity / speed : Vector3.down;
                float ceiling = most > 0f ? Mathf.Min(Mathf.Max(1f, Max), Mathf.Max(1f, most)) : Mathf.Max(1f, Max);
                return Mathf.Max(.01f, size) * Mathf.Clamp(Base + speed * PerSpeed, 1f, ceiling);
            }
        }

        [Serializable]
        public sealed class ResidueSet
        {
            [Tooltip("Seconds a spell residue stays (clamped to 3-5 in code).")]
            public float SpellLife = 4.5f;
            [Tooltip("Seconds a footprint stays (clamped to 3-5 in code).")]
            public float FootLife = 3.5f;
            [Tooltip("Seconds a held mark (field / install) takes to dry after its spell ends (clamped to 3-5 in code).")]
            public float FieldTail = 3.0f;
            [Range(1, 8)] public int RaysPerFrame = 4;
            [Range(1, 16)] public int StampsPerFrame = 8;
            [Tooltip("Largest single ground piece (m); bigger marks are split so they do not float on slopes and steps.")]
            [Range(.1f, 1f)] public float MaxPiece = .35f;
            [Range(0f, .05f)] public float Lift = .01f;
            [Range(0f, 1f)] public float Opacity = .82f;
            [Min(.01f)] public float DropSmall = .07f, DropMid = .12f, DropLarge = .22f;
            [Tooltip("The wet puddle under a burst (m, 0 = none).")]
            [Min(0f)] public float ImpactSmear = .8f;
            [Min(0f)] public float Gravity = 9.8f;
            [Tooltip("Hook B fields: the field's real lifetime is not relayed yet, so its marks stay wet this long (phase 2 replaces it).")]
            [Min(0f)] public float FieldHoldFallback = 6f;
            [Tooltip("Ground normals flatter than this are walls: no residue there.")]
            [Range(0f, 1f)] public float MinNormalY = .45f;
            public LayerMask GroundLayers = ~0;
        }

        [Serializable]
        public sealed class FootSet
        {
            public bool Enabled = true;
            [Min(.02f)] public float Width = .11f, Length = .28f;
            [Range(0f, 20f)] public float ToeOutDeg = 6f;
            [Tooltip("Neutral ink, no element tint, never pulses or grows.")]
            [Range(0f, 1f)] public float Opacity = .8f;
            [Min(.2f)] public float StrideWalk = .68f, StrideRun = 1.05f;
            [Min(0f)] public float SideOffset = .10f;
            [Range(0f, 60f)] public float MaxSlopeDeg = 45f;
            [Range(0, 8)] public int LandingDrops = 3;
            [Tooltip("A hit on these layers under the foot means no print (water).")]
            public LayerMask BlockLayers = 1 << 4;
        }

        [Serializable]
        public sealed class FloodSet
        {
            [Min(.05f)] public float Cover = .30f, Clear = .60f;
            [Tooltip("Events allowed to request the screen ink flood. Empty in phase 1: nothing is wired.")]
            public string[] AllowedEvents = Array.Empty<string>();
            [Range(0f, .5f)] public float ReducedEdgeShare = .25f;
        }

        [Serializable]
        public sealed class TierSet
        {
            public string Name = "";
            [Range(1, 8)] public int Bursts = 8;
            [Range(256, 2048)] public int VertsPerBurst = 2048;
            [Range(1, 12)] public int BoldMax = 7;
            public bool Fine = true;
            [Range(0, 12)] public int NeedleMax = 8;
            [Range(4f, 30f)] public float CelHz = 12f;
            [Range(4, 40)] public int ColumnStrips = 28;
            [Range(8, 256)] public int SpellResidue = 160;
            [Range(4, 64)] public int Footprints = 24;
            [Range(8, 256)] public int AirDrops = 256;
            [Tooltip("Off = no colour-copy full-screen frame; the local invert quad only.")]
            public bool FullScreen = true;
            [Range(0, 8)] public int FlickerMax = 4;
            public bool HudReactDefault = true, Flood = true, FovBreath = true, UseMobileAtlas;
            // ---- #308 forms2
            [Tooltip("Slots kept for bodies that STAND (a guard stroke, a ward fence, a cover wall): ordinary bursts never take them and never push them out.")]
            [Range(1, 6)] public int StandingBursts = 4;
            [Range(256, 2048)] public int VertsPerStanding = 1024;
            [Range(0, 16)] public int FencePosts = 12;
            [Range(0, 8)] public int FenceRails = 4;
            [Range(0, 24)] public int FenceRingDrops = 12;
            [Range(0, 8)] public int GuardNeedles = 4;
            public bool GuardDryTicks = true;
            [Range(0, 3)] public int CometTails = 2;
            [Range(0, 6)] public int CometDrops = 4;
            [Range(0, 4)] public int FootMarksPerStep = 2;
            [Tooltip("Buff marks laid per second at most, whatever the stride.")]
            [Range(0f, 12f)] public float FootMarksPerSecond = 6f;
            [Range(0, 6)] public int AuraStamps = 3;
            [Tooltip("Off on a tier that was saved before the forms2 numbers existed (its forms2 fields then hold the PC class defaults): Tier() answers with the code's own numbers for that tier until forms308-profile writes them.")]
            public bool Forms2Saved;

            /// <summary>A copy of this tier whose forms2 numbers are `source`'s (everything else is this tier's).</summary>
            public TierSet WithForms2Of(TierSet source)
            {
                var copy = (TierSet)MemberwiseClone();
                if (source == null) return copy;
                copy.StandingBursts = source.StandingBursts; copy.VertsPerStanding = source.VertsPerStanding; copy.FencePosts = source.FencePosts; copy.FenceRails = source.FenceRails;
                copy.FenceRingDrops = source.FenceRingDrops; copy.GuardNeedles = source.GuardNeedles; copy.GuardDryTicks = source.GuardDryTicks; copy.CometTails = source.CometTails;
                copy.CometDrops = source.CometDrops; copy.FootMarksPerStep = source.FootMarksPerStep; copy.FootMarksPerSecond = source.FootMarksPerSecond; copy.AuraStamps = source.AuraStamps;
                copy.Forms2Saved = true;
                return copy;
            }
        }

        // ---- #308 forms2 (Tools/Unity/Stage308_forms2/DESIGN.md section 4) [every number TEST]: the forms that replace the
        // catalogue bodies. Presentation only, like everything in this file: no rule reads any of it.

        /// <summary>forms3 (D12): the PRESSED head of a thick brush stroke. A brush is pressed down at the stroke's head and drawn
        /// out into the dry tail: the head is blunt and lopsided (one edge runs on straight, the other curves in to meet it).
        /// Narrowing both ends made a leaf. Read by InkBurstMeshBuilder308.PressHead; Share 0 = the stroke keeps its old end.</summary>
        [Serializable]
        public sealed class BrushHeadSet
        {
            [Tooltip("Share of the wet body over which the head closes (0 = off: the stroke keeps the end it had).")]
            [Range(0f, 1f)] public float Share = .22f;
            [Tooltip("Which edge closes: 1 = one edge curves in and the other runs straight, 0 = both alike (a round end), -1 = the other edge.")]
            [Range(-1f, 1f)] public float Lean = 1f;
            [Tooltip("Half width left where the head ends, as a share of the stroke's half width: the round nub that closes it (never a point).")]
            [Range(.05f, 1f)] public float End = .34f;
            [Tooltip("Shape of the closing edge: 2 = a quarter ellipse, higher = a fuller shoulder (the width falls only at the very end).")]
            [Range(1f, 8f)] public float Round = 2.4f;
            [Tooltip("Rings of the stroke's body that lie inside the head (0 = a third of its segments). Few rings kink the outline.")]
            [Range(0, 16)] public int Rings = 6;
            [Tooltip("How far the nub stands out, as a share of its own half width: 1 = a half disc, lower = flatter (a blunt, pressed end).")]
            [Range(.05f, 1f)] public float Cap = .45f;
            [Tooltip("forms3 fix pass: the pressed part is wider than the drawn part. Swell = how much wider the stroke is where the head begins (share of its half width; 0 = one width from root to head), reached over the last SwellSpan of the body before the head.")]
            [Range(0f, .5f)] public float Swell = 0f;
            [Range(.05f, 1f)] public float SwellSpan = .4f;
        }

        /// <summary>Parry (거 너 머 서 어): the guard stroke held in front of the body. Its clock is the rule's (the window and the
        /// lifetime the wiring hands over); nothing here is a duration of the guard.</summary>
        [Serializable]
        public sealed class GuardSet
        {
            public bool Enabled = true;
            [Tooltip("Where the stroke is held: metres ahead of the eye (flat), and its height against the eye height it assumes.")]
            [Min(.3f)] public float Forward = 1.4f;
            [Min(.2f)] public float Height = 1.2f, EyeHeight = 1.6f;
            [Min(.2f)] public float Span = 1.2f;
            [Min(.005f)] public float BarHalfWidth = .06f;
            [Tooltip("How far the middle of the stroke rises (an arch), m.")]
            [Min(0f)] public float Sag = .09f;
            [Tooltip("look2: the stroke stands in the world, not on the screen - it crosses the view at an angle. Its tail end stands this much nearer the eye than its head end (m).")]
            [Min(0f)] public float Depth = .55f;
            [Tooltip("round 2: share of Depth by which the tail end stands NEARER the eye than the stroke's middle (the head end stands the rest farther).")]
            [Range(0f, 1f)] public float DepthTailShare = .5f;
            [Tooltip("round 2: share of the element wash the guard stroke's body takes (it is wide and stands for seconds; 1 = an attack stroke's wash).")]
            [Range(0f, 1f)] public float Wash = .8f;
            [Tooltip("round 2: share of the wet body over which it narrows to its one brush point (every element ends the same way).")]
            [Range(0f, 1f)] public float TipTaper = .3f;
            [Tooltip("round 2: share of the wet body over which it reaches its full width (the attack strokes take .6: a long swell and a long point together read as a leaf).")]
            [Range(.05f, 1f)] public float RootRamp = .25f;
            [Tooltip("look2: share of the wet stroke that is dry-brush tail.")]
            [Range(0f, .6f)] public float WetTailShare = .32f;
            [Tooltip("forms3 (D12): the wet stroke's pressed head (the old TipTaper point is used only when Head.Share is 0). Segments = rings of the wet stroke's body.")]
            public BrushHeadSet Head = new BrushHeadSet { Share = .24f, Lean = 1f, End = .42f, Round = 2.4f, Rings = 9, Cap = .5f, Swell = .14f, SwellSpan = .4f };   // fix pass: the pressed part is wider (it was one width: a sausage)
            [Range(4, 24)] public int Segments = 18;
            [Tooltip("forms3 (D3, were code constants): an element drawn as two lines keeps LineGap half widths between them; a stroke whose head rises (Rise) takes RiseArchShare of the arch.")]
            [Min(1f)] public float LineGap = 4f;
            [Range(0f, 1f)] public float RiseArchShare = .5f;
            [Tooltip("forms3 (D3): the dry stroke - rings of the main strand and of a hair, and the share of the element's wave the main strand keeps.")]
            [Range(4, 24)] public int DrySegments = 12, DryHairSegments = 6;
            [Range(0f, 1f)] public float DryWaveShare = .5f;
            [Tooltip("forms3 (D12): a hair of the split brush leaves the main dry strand this share of the way along it (0 = at the root, where two thin starts read as two separate floating strokes).")]
            [Range(0f, .8f)] public float DryHairFrom = .28f;
            [Tooltip("round 2: the dry stroke after the window is ONE dry-brush stroke plus the hairs of a split brush: DryStrands - 1 hairs leave the same root, each parting from the stroke by DrySplitDeg more (downward), DryHairShare of its length (the second half of that), DryHairWidth of its width. DryTaper = share of the wet part that narrows to the point.")]
            [Range(1, 3)] public int DryStrands = 2;
            [Range(0f, 20f)] public float DrySplitDeg = 6f;
            [Range(.2f, 1f)] public float DryHairShare = .62f, DryHairWidth = .7f;
            [Range(0f, 1f)] public float DryTaper = .6f;
            [Tooltip("round 2: share of a stroke's wet body its brush hairs run over (over 1 = streaked up to its point: a dry stroke). The dry stroke after the window / the pieces of a broken stroke.")]
            [Range(0f, 3f)] public float DryShare = 3f, BreakDryShare = .8f;
            [Range(0f, .6f)] public float DryTailShare = .3f;
            [Tooltip("look2: the braces at both ends (short strokes turned down). Off: they read as the legs of a plank.")]
            public bool Braces;
            [Min(0f)] public float TickLength = .16f;
            [Tooltip("Needles standing on the stroke while the parry window is open (their length is cut so that they stay under MaxElevationDeg).")]
            [Min(0f)] public float NeedleMin = 0f, NeedleMax = 0f;
            [Tooltip("Half width of a needle (m): the window's sign must read at arm's length, a hair does not.")]
            [Min(.001f)] public float NeedleHalfWidth = .012f;
            [Tooltip("The dry stroke that stays after the window: width relative to the wet one.")]
            [Range(.2f, 1f)] public float DryWidthMul = .9f;
            [Range(.01f, .3f)] public float ScreenShareMax = .08f;
            [Tooltip("No vertex of the stroke may stand higher than this, seen from the eye (deg, negative = below the horizon).")]
            [Range(-40f, 0f)] public float MaxElevationDeg = -8f;
            [Tooltip("Contact marks (m / s): parry, half parry, plain block, failed parry.")]
            [Min(0f)] public float SuccessStar = 1.1f, HalfStar = .6f, BlockStar = .35f, FailBlot = .5f;
            [Range(.04f, .4f)] public float StarSeconds = .15f, BlockStarSeconds = .1f, FailBlotSeconds = .25f;
            [Range(0, 8)] public int EndDrops = 2, HalfDrops = 3, BlockDrops = 1;
            [Tooltip("Size of the drops that fall when the guard ends, relative to Residue.DropMid.")]
            [Min(.1f)] public float EndDropSizeMul = 1.4f;
            [Tooltip("A parry breaks the stroke: the halves part this far (m) over BreakCels cels while they melt.")]
            [Min(0f)] public float BreakSpread = .1f;   // fix pass: the cut faces stay about .4 m apart (1 m apart they were two separate lumps)
            [Tooltip("round 2: the two pieces of a broken stroke are short dry-brush strokes - share that is frayed (at the cut), share of the rest that narrows to the point, width against the wet stroke.")]
            [Range(0f, .6f)] public float BreakTailShare = .28f;   // fix pass: the wet piece is 1.5 times as long
            [Range(0f, 1f)] public float BreakTaper = .5f, BreakWidthMul = .7f;
            [Tooltip("forms3 (D12): the pieces of a broken stroke end in a pressed head too (BreakTaper is used only when BreakHead.Share is 0). BreakDepthShare = share of the stroke's depth the pieces keep (under 1: both stand nearly as far from the eye, so both are seen). BreakDrop = how far they sink while they part, as a share of BreakSpread; BreakArchShare = share of the arch a piece keeps; BreakSegments = its rings.")]
            public BrushHeadSet BreakHead = new BrushHeadSet { Share = .4f, Lean = 1f, End = .34f, Round = 2.2f, Rings = 4 };
            [Range(0f, 1f)] public float BreakDepthShare = .35f;
            [Range(0f, 1f)] public float BreakDrop = .3f, BreakArchShare = .25f;
            [Range(4, 24)] public int BreakSegments = 8;
            [Range(1, 6)] public int BreakCels = 3;
            [Tooltip("Ink thrown toward the attacker on a parry (air drops, PC; Mobile x SplashMobileScale).")]
            [Range(0, 24)] public int SplashDrops = 11;
            [Range(0f, 1f)] public float SplashMobileScale = .45f;
            [Min(0f)] public float SplashSpeedMin = 2f, SplashSpeedMax = 4.5f;
            [Tooltip("Parry splash: sideways fan (share of the forward speed) and the upward speed range (m / s).")]
            [Range(0f, 2f)] public float SplashFan = .7f;
            [Min(0f)] public float SplashUpMin = .6f, SplashUpMax = 1.8f;
            [Tooltip("Half parry / plain block: the drops only fall - sideways speed and upward speed range (m / s).")]
            [Range(0f, 2f)] public float FallFan = .6f;
            [Min(0f)] public float FallUpMin = .1f, FallUpMax = .5f;
            [Tooltip("Splash drops: where they start beside the contact point (m), size relative to Residue.DropSmall, share that are lumps, seconds in the air.")]
            [Min(0f)] public float SplashSpread = .12f;
            [Min(.1f)] public float SplashSizeMin = 1.4f, SplashSizeMax = 2.4f;
            [Range(0f, 1f)] public float SplashClusterChance = .25f;
            [Range(.2f, 2f)] public float SplashSeconds = 1.6f;
        }

        /// <summary>Per element shape of the guard stroke (indexed by Element).</summary>
        [Serializable]
        public sealed class GuardElementSet
        {
            public string Name = "";
            [Range(.4f, 1.5f)] public float SpanMul = 1f;
            [Range(.2f, 2f)] public float WidthMul = 1f;
            [Range(1, 2)] public int Lines = 1;
            [Range(0, 3)] public int Split;
            [Tooltip("The head end rises this share of the span (a stroke bent upward) instead of the arch.")]
            [Range(0f, .3f)] public float Rise;
            [Range(0f, .3f)] public float Wave;
            [Range(0f, 3f)] public float NeedleMul = 1f;
            [Range(0, 4)] public int HangDrops;
            [Tooltip("The head end flicks out and down as a dry stroke (never upward: the elevation limit leaves no room there).")]
            public bool DryTailOut;
            [Tooltip("forms3 (D4 / D12): this element's pressed head against Guard.Head - HeadShareMul lengthens it (a longer, sharper close), HeadEndMul scales the nub it ends in.")]
            [Range(.3f, 3f)] public float HeadShareMul = 1f;
            [Range(.2f, 2f)] public float HeadEndMul = 1f;
            [Tooltip("forms3 fix pass: this element's dry stroke (after the window) against Guard.DryWidthMul - a thin element's dry stroke may be wider (metal was a faint hair).")]
            [Range(.5f, 2f)] public float DryWidthMul = 1f;
        }

        /// <summary>Ward (구 누 무 수 우): the ink fence on the ward's own ring. It stands as ink (no glow, no sinking).</summary>
        [Serializable]
        public sealed class FenceSet
        {
            public bool Enabled = true;
            [Min(.2f)] public float PostHeight = .9f, RailHeight = .55f, LowRailHeight = .25f;
            [Min(.01f)] public float PostHalfWidth = .06f, RailHalfWidth = .04f;
            [Tooltip("Stone wall (earth): the two courses' heights and the length of one stone (look2: the width of a stone lump's quad; BrickHalfWidth = half its height).")]
            [Min(.05f)] public float CourseLow = .15f, CourseHigh = .36f, BrickLength = .56f;
            [Min(.01f)] public float BrickHalfWidth = .24f;
            [Tooltip("Stone wall: share of the ring each course covers at least, on every tier (the stones are counted from it). A stone's lump fills about .84 of its quad's width: from 1.2 on the stones overlap (look2: a wall without gaps).")]
            [Range(.3f, 1.8f)] public float CourseCover = 1.4f;
            [Tooltip("pass 4b (main decision Q3), flame fence (fire). RING COHESION FIRST: a low, broken, width-varying ink band on the ground closes the ring, and flame tongues grow out of it in pairs and threes of unequal height that lean into each other and cross. A tongue is one brush flick - pressed on the ground, drawn up, belly low, bending, its upper part loose dry hairs. TongueMin / TongueMax = the tongue's length (m; its top never passes PostHeight), TongueHalfWidth = half width of a tall tongue's belly (a low one: x FlameLowWidth), TongueLeanDeg = how far the outer tongues of a group lean into it, TongueCoarseShare = share of the element's Tongues a tier without fine strokes draws.")]
            [Min(.05f)] public float TongueMin = .66f, TongueMax = 1.1f;   // pass 4c: .66 (was .5) - the low tongue of a group is wet to .45 m at the least
            [Min(.01f)] public float TongueHalfWidth = .165f;
            [Range(0f, 40f)] public float TongueLeanDeg = 25f;
            [Range(.1f, 1f)] public float TongueCoarseShare = .66f;   // pass 4c: 4 of 6 a rail = 16 on the coarse tier (was 3 of 5 = 12)
            [Tooltip("pass 4b: the groups. FlameGroups = how many tongues stand together, group after group (2 or 3; read round and round; nobody stands alone). FlameGroupTight = m between two tongues of a group (they stand closer than they are tall: they cross), FlameGroupApart = how the rest of the ring is shared out between the groups (unequal). FlamePairHeights / FlameTrioHeights = each tongue's length as a share of TongueMin..TongueMax, mirrored in every other group; FlameTallWobble = how much shorter one may be (share). FlameMidLean = the middle tongue of three leans this share of TongueLeanDeg; FlameLowWidth / FlameLowCurl = a low tongue's width / curl against a tall one's (the tall one's tip goes over the low one).")]
            public float[] FlameGroups = { 2f, 2f, 2f, 3f };   // pass 4c: 24 tongues = eleven groups on PC (were 20 in eight)
            [Tooltip("pass 4c: the order of the groups on a tier without fine strokes (16 tongues = seven groups).")]
            public float[] FlameGroupsCoarse = { 2f, 2f, 2f, 3f, 2f, 2f, 3f };
            [Min(.1f)] public float FlameGroupTight = .31f;
            public float[] FlameGroupApart = { 1f, .95f, 1.015f, .965f, 1.01f, .955f, 1.015f };   // pass 4c: unequal, but no stretch without a tongue over 1.5 m (PC) / 2.4 m (Mobile)
            public float[] FlamePairHeights = { 1f, .12f };
            public float[] FlameTrioHeights = { .25f, 1f, 0f };
            [Range(0f, .3f)] public float FlameTallWobble = .06f;
            [Range(0f, 1f)] public float FlameMidLean = .85f;
            [Range(.4f, 1f)] public float FlameLowWidth = .86f;
            [Range(0f, 1f)] public float FlameLowCurl = .5f;
            [Tooltip("FlameJitter = how far a tongue may stand off its place in the group (share of FlameGroupTight); FlameWidthMin..Max = wobble of the width; FlameLeanMin = least lean as a share of TongueLeanDeg; FlameCurve = how far the tip curls the way the tongue leans (share of its height; each between FlameCurlMin and all of it); FlameTipWidth = width where the wet body ends, as a share of the base (never under .3: no thorn); FlameBaseMax = the widest half width (m); FlameWave = the flick's S bend; FlameRamp = share of the body over which it reaches its width from the tip down (high = the belly lies low); FlameTailShare / FlameDryShare = the dry hairs at the top; FlameSink = how far the pressed base lies under the ground.")]
            [Range(0f, .5f)] public float FlameJitter = .05f;
            [Range(.3f, 1.5f)] public float FlameWidthMin = .85f, FlameWidthMax = 1.06f;
            [Range(0f, 1f)] public float FlameLeanMin = .8f;
            [Range(0f, .6f)] public float FlameCurve = .4f;
            [Range(.2f, 1f)] public float FlameTipWidth = .32f;
            [Min(.05f)] public float FlameBaseMax = .2f;
            [Range(0f, 1f)] public float FlameCurlMin = .7f;
            [Range(-.3f, .3f)] public float FlameWave = -.22f;
            [Range(.05f, 1f)] public float FlameRamp = .8f;
            [Range(0f, .6f)] public float FlameTailShare = .16f;   // pass 4c: .16 (was .22) - the wet body reaches higher under the same top
            [Range(0f, 3f)] public float FlameDryShare = .35f;
            [Min(0f)] public float FlameSink = .08f;
            [Tooltip("forms3 (D3, was a code constant): m a flame tongue stays under the fence's height limit.")]
            [Min(0f)] public float FlameTopMargin = .02f;
            [Range(2, 8)] public int FlameSegments = 4, FlameSegmentsCoarse = 3;
            [Tooltip("pass 4c: a tongue whose length share is under FlameTallFrom is drawn with FlameSegmentsLow rings (the vertices go to more groups).")]
            [Range(2, 8)] public int FlameSegmentsLow = 3, FlameSegmentsLowCoarse = 2;
            [Range(0f, 1f)] public float FlameTallFrom = .9f;
            public BrushHeadSet FlameBase = new BrushHeadSet { Share = .34f, Lean = 0f, End = .48f, Round = 1.6f, Rings = 1, Cap = .15f };   // pass 4c: the foot narrows to under .55 of the belly (a teardrop rising out of the band - it was a skirt)
            [Tooltip("pass 4b: the split tip. FlameSideLicks 1 = every tongue has a short dry lick that leaves its body FlameSideFrom of the way up on the side it leans away from, fanned FlameFanDeg from it, FlameSideMin..Max of its height long, FlameSideWidth of its width, FlameRootSpread half widths off its middle - its hairs end beside the tongue's own (no single point). 0 = none.")]
            [Range(0, 1)] public int FlameSideLicks = 1;
            [Range(0f, 40f)] public float FlameFanDeg = 14f;
            [Range(0f, .9f)] public float FlameSideFrom = .5f;
            [Range(.2f, 1f)] public float FlameSideMin = .4f, FlameSideMax = .55f;
            [Range(.2f, 1.5f)] public float FlameSideWidth = .3f;
            [Range(0f, 2f)] public float FlameRootSpread = .5f;
            [Tooltip("pass 4b: the base band - what closes the ring. It is laid from group to group: THICK under a group of tongues (the brush pressed there - the bed the tongues grow out of), thin, combed dry and broken between two groups. FlameBaseSplits (coarse tier: FlameBaseSplitsCoarse) = smears per stretch between two groups, read round and round: 1 = one smear, thin where it leaves a group (FlameBaseLeave m after its middle) and pressed under the next; 2 / 3 = the first is drawn BACK to its group, the others on toward the next, with a break between two. The breaks of a stretch take what FlameBaseWet (FlameBaseWetCoarse) leaves of it, shared out by FlameBaseGaps; the smears share the rest by FlameBaseRuns (both unequal). FlameBedReach = how far a pressed end reaches in under its group (m). FlameBaseHighs = how high each smear stands between FlameBaseHalfMin and FlameBaseHalf (half height, m); none stands over FlameBaseTop (m). A smear starts FlameBaseRoot of its height thin, combed dry over FlameBaseDry of its length, reaches its height over FlameBaseRamp and ends pressed (FlameBaseHead; its swell is the thick end) - never one width. Its lower edge lies FlameBaseSink under the ground; each of its ends stands up to FlameBaseOff m off the ring (in or out). A ring of the smear every FlameBaseSegmentDeg degrees. FlameBridgeHalf / FlameBridgeLap = the dry smear over each break (half height, m; how far it reaches into the smears).")]
            public float[] FlameBaseSplits = { 2f, 2f, 1f, 2f, 2f, 2f, 1f };   // (pass 4c: no longer read - the band is dabs; kept so that a saved profile loads)
            [Tooltip("pass 4c: the band is short DABS. FlameDabs (coarse tier: FlameDabsCoarse) = dabs in the stretch between two groups, read round and round: 1 = one dab whose pressed end lies FlameDabClear m (coarse: FlameDabClearCoarse) from its group - negative: in under the group's last foot, the bed the tongues stand in - and which trails away from it, thinning (all the same way round the ring; FlameDabTurnAbout: turn about), 2 = one pressed in under each group, drawn toward the middle. No dab is longer than FlameDabMax m (coarse tier: FlameDabMaxCoarse - laid in two straight pieces); FlameBaseRuns = each dab's length as a share of that. A hole the dabs leave is bridged by a dry smear when it is wider than FlameBridgeLeast m (on the coarse tier only if FlameBridgeCoarse); the smear is FlameBridgeThin .. 1 of FlameBridgeHalf high. The two ends of a dab stand FlameBaseOff m (x FlameBaseOffLeast .. 1) inside and outside the ring, turn about.")]
            public float[] FlameDabs = { 1f };
            public float[] FlameDabsCoarse = { 1f };
            [Min(.2f)] public float FlameDabMax = 1.02f, FlameDabMaxCoarse = 1.95f;   // (the coarse tier has 7 stretches of 2.3 m and vertices for one smear in each: its smears are long, in two straight pieces)
            public float FlameDabClear = 0f, FlameDabClearCoarse = 0f;   // (0: the pressed end lies under the middle of the last foot)
            public bool FlameDabTurnAbout = false;
            [Min(0f)] public float FlameBridgeLeast = .3f;
            public bool FlameBridgeCoarse = true;
            [Range(.1f, 1f)] public float FlameBridgeThin = .75f;
            [Range(0f, 1f)] public float FlameBaseOffLeast = .7f;
            public float[] FlameBaseSplitsCoarse = { 2f };
            [Range(.3f, .95f)] public float FlameBaseWet = .8f, FlameBaseWetCoarse = .84f;
            public float[] FlameBaseRuns = { 1f, .78f, .9f, .75f, .95f, .82f, .76f, .86f };
            public float[] FlameBaseGaps = { .85f, 1.15f, .7f, 1f, .8f, 1.1f };
            [Min(0f)] public float FlameBaseLeave = .22f, FlameBedReach = .13f;
            public float[] FlameBaseHighs = { 1f, .35f, .8f, .1f, .9f, .55f, .25f };
            [Min(.01f)] public float FlameBaseHalfMin = .064f, FlameBaseHalf = .074f;   // pass 4c: .044 (was .03) - the band is 5 - 17 cm high, not 3 - 6
            [Range(.05f, .18f)] public float FlameBaseTop = .175f;
            [Range(.2f, 1f)] public float FlameBaseRoot = .6f;
            [Range(.05f, 1f)] public float FlameBaseRamp = .5f;
            [Range(0f, 3f)] public float FlameBaseDry = .6f;
            [Min(0f)] public float FlameBaseSink = .015f;
            [Range(0f, .05f)] public float FlameBaseOff = .045f;
            [Range(4f, 30f)] public float FlameBaseSegmentDeg = 17f, FlameBaseSegmentDegCoarse = 12.4f;   // pass 4c: a coarse smear of 1.2 - 1.6 m is laid in three rings + its pressed end (no straight piece over .8 m)
            public BrushHeadSet FlameBaseHead = new BrushHeadSet { Share = .22f, Lean = 1f, End = .5f, Round = 2f, Rings = 1, Cap = .5f, Swell = .28f, SwellSpan = .5f };
            [Min(.01f)] public float FlameBridgeHalf = .05f, FlameBridgeLap = .15f;
            [Tooltip("pass 4b: small dry flickers in the band between two groups of tongues (FlameFlickers per stretch, FlameFlickersCoarse on the coarse tier): each a dry-brush lick FlameFlickerMin..Max m tall (a written order of heights, FlameFlickerHighs), FlameFlickerHalf m half width, leaning up to FlameFlickerLeanDeg turn about, standing at its written place in its share of the stretch (FlameFlickerAt). The band reads as a low fire, never as a line.")]
            [Range(0, 4)] public int FlameFlickers = 0, FlameFlickersCoarse = 0;   // pass 4c: none (tufts of parallel hairs read as grass / combs; the vertices went to more groups of tongues)
            [Range(.1f, 1f)] public float FlameFlickerLeanLeast = .5f;
            [Range(.3f, 2f)] public float FlameFlickerThin = .8f, FlameFlickerWide = 1.15f;
            public float[] FlameFlickerAt = { .3f, .7f, .55f, .2f, .8f, .4f, .6f };
            public float[] FlameFlickerHighs = { .9f, .3f, .6f, 1f, .15f, .75f, .45f };
            [Min(.05f)] public float FlameFlickerMin = .28f, FlameFlickerMax = .56f;
            [Min(.01f)] public float FlameFlickerHalf = .085f;
            [Range(0f, 40f)] public float FlameFlickerLeanDeg = 22f;
            [Tooltip("forms3 (D7): no part of a flame tongue stands farther than this along the ring from its base (m): the fence stays on the ward's own ring (.6 m along a 3 m ring is 6 cm off it).")]
            [Range(.1f, .6f)] public float FlameRingSlack = .6f;
            [Tooltip("round 2: a low rail's arcs and the wave strokes are spindles that lie over each other - degrees an arc reaches past its share of the ring, share of a spindle that narrows to its point (used only when Fence.SpindleHead is off).")]
            [Range(0f, 15f)] public float RailOverlapDeg = 7f, WaveOverlapDeg = 4f;
            [Range(0f, 1f)] public float SpindleTaper = .45f;
            [Tooltip("round 2: a spindle starts this wide (share of its half width) and reaches its full width over SpindleRamp of its length.")]
            [Range(.02f, 1f)] public float SpindleRoot = .35f, SpindleRamp = .5f;
            [Tooltip("look2: the drop hanging under a water GUARD stroke - length and half width (m). (The wave fence has its own: WaveDropLength.)")]
            [Min(.02f)] public float HangDropLength = .16f, HangDropHalfWidth = .045f;
            [Tooltip("Wave fence (water): height of the lower row (a second row is drawn only when the element asks for two), and the strokes' width at the crest relative to RailHalfWidth.")]
            [Min(.05f)] public float WaveLow = .28f;
            [Tooltip("forms4 (P9): height of the wave line's troughs over the ground (m). It was the stone wall's CourseHigh (.36): the whole ring floated .4 - .5 m up, only the drops reached down.")]
            [Min(0f)] public float WaveBase = .06f;
            [Range(.5f, 4f)] public float WaveWidthMul = 2.4f;
            [Tooltip("forms3 (D8), wave fence: ONE line whose thickness follows the wave, laid in half waves (a stroke rising from a trough to the crest, the next falling into the next trough; their ends lie in each other where the line runs level). WaveCrest = height of a crest over a trough (m), WaveTrough = the line's width in the trough as a share of the crest's, WavesPerRail (WavesPerRailCoarse on a tier without fine strokes) x the tier's FenceRails waves round the ring, WaveSegments / WaveSegmentsCoarse = rings of a half wave. The drop under a crest: WaveDropLength / WaveDropHalfWidth (m) - twice the guard stroke's.")]
            [Min(0f)] public float WaveCrest = .2f;
            [Range(.05f, 1f)] public float WaveTrough = .3f;
            [Tooltip("forms3 (D8): every half wave starts WaveOverlapDeg back inside the end of the one before it. A falling half starts WaveJoin of its width wide and reaches its width over WaveJoinRamp of its length (its cut start is hidden in the rising half); it ends WaveEndShare of the trough width wide (its round end is hidden in the next rising half).")]
            [Range(.2f, 1f)] public float WaveJoin = .7f;
            [Range(.2f, 1f)] public float WaveEndShare = .6f;
            [Range(.05f, 1f)] public float WaveJoinRamp = .2f;
            [Range(1, 8)] public int WavesPerRail = 4, WavesPerRailCoarse = 3;   // fix pass 4b (director 4): the coarse tier's half waves were 22 deg chords - a band with steps at the joints; 3 waves a rail = 15 deg
            [Range(2, 12)] public int WaveSegments = 6, WaveSegmentsCoarse = 4;   // forms4 (P9): +3 rings on the coarse tier (it has the room; PC has 36 vertices spare = one ring)
            [Min(.02f)] public float WaveDropLength = .32f, WaveDropHalfWidth = .07f;
            [Tooltip("forms3 fix pass (review S4, were code constants): a wave drop's length varies by WaveDropWobble (+-), it hangs WaveDropUnder half widths under the line, and takes WaveDropWash of the element wash.")]
            [Range(0f, .5f)] public float WaveDropWobble = .2f;
            [Range(0f, 2f)] public float WaveDropUnder = .6f;
            [Range(0f, 1f)] public float WaveDropWash = .5f;
            [Tooltip("forms3 (D8), wooden fence: the rails are bending brush strokes tied from post to post - each starts thin under the last one's head (SpindleRoot of its width, swelling over SpindleRamp of its length), bows, and ends in the pressed head SpindleHead (Share 0 = the old point, SpindleTaper). The ring closes. SpindleSag = how far a piece bows (m; up and down in turn), SpindleRailOverlapDeg = how far before its share of the ring it starts, SpindleSegments / SpindleLowSegments = rings of an upper / a lower piece, SpindleLowWidthMul = the lower row's width against the upper one's. LowRailChordsPerRail = dry chords of the old low rail per tier rail (was a code constant).")]
            public BrushHeadSet SpindleHead = new BrushHeadSet { Share = .18f, Lean = 1f, End = .35f, Round = 2.2f, Rings = 3, Cap = .55f };
            [Tooltip("forms3 fix pass: a post ends in a pressed brush tip, turn about lopsided (a wooden post's forked tip was a row of W-shaped teeth). Share 0 = the element's own end.")]
            public BrushHeadSet PostHead = new BrushHeadSet { Share = .3f, Lean = 1f, End = .45f, Round = 2.2f, Rings = 2, Cap = .5f };
            [Min(0f)] public float SpindleSag = .14f;
            [Range(0f, 10f)] public float SpindleRailOverlapDeg = 5f;
            [Range(3, 12)] public int SpindleSegments = 9, SpindleLowSegments = 3;
            [Tooltip("forms3 fix pass: rings of a rail piece on a tier without fine strokes (few rings kinked the bow: a plank).")]
            [Range(3, 12)] public int SpindleSegmentsCoarse = 5;
            [Range(.3f, 1.5f)] public float SpindleLowWidthMul = .8f;
            [Range(1, 8)] public int LowRailChordsPerRail = 4;
            [Tooltip("forms3 (D8), stone wall: a stone is one of these size classes (small, middle, large - no two small ones lie side by side); a stone of the upper course is never larger than StoneUpperMax (the wall's height limit). StoneWobble = a stone's own unevenness (+-), StoneInset = the upper course stands this much nearer the middle (m).")]
            public float[] StoneSizes = { .7f, 1f, 1.32f };
            [Range(.5f, 1.5f)] public float StoneUpperMax = 1f;
            [Range(0f, .3f)] public float StoneWobble = .08f;
            [Min(0f)] public float StoneInset = .015f;
            [Tooltip("forms3 fix pass (review S4, was a code constant): a stone sits this much higher or lower (m).")]
            [Min(0f)] public float StoneLift = .02f;
            [Range(.01f, .5f)] public float ScreenShareMax = .10f;
            [Tooltip("Seen from the middle of the ring the top may not stand higher than this (deg).")]
            [Range(-40f, 0f)] public float MaxTopElevationDeg = -10f;
            [Tooltip("The eye height the two limits above are judged at.")]
            [Min(.5f)] public float EyeHeight = 1.6f;
        }

        [Serializable]
        public sealed class FenceElementSet
        {
            public string Name = "";
            [Tooltip("Posts relative to the tier's FencePosts (0 = none).")]
            [Range(0f, 2f)] public float PostMul = 1f;
            [Tooltip("Shortest post as a share of PostHeight (1 = all equal).")]
            [Range(.3f, 1f)] public float MinHeightShare = 1f;
            [Range(0f, 20f)] public float LeanDeg;
            public bool Rail = true, RailAtTop, LowRail = true;
            [Tooltip("Stone courses (earth) / wave strokes (water) instead of posts.")]
            [Range(0, 2)] public int Courses;
            public bool Wave;
            [Tooltip("Rows of wave strokes (water): one thin line reads as a wire.")]
            [Range(1, 2)] public int WaveRows = 1;
            [Tooltip("The low dry rail is laid on every tier (not only where fine strokes are on): it ties posts of uneven height into one fence.")]
            public bool LowRailAlways;
            [Range(0, 2)] public int HangDrops;
            [Tooltip("look2: the wet rail lies low (at LowRailHeight) instead of at RailHeight.")]
            public bool RailLow;
            [Tooltip("forms3 (D7): flame masses per tier rail (0 = none): Tongues x the tier's FenceRails masses round the ring.")]
            [Range(0, 16)] public int Tongues;
            [Tooltip("look2: the wet rail's width relative to Fence.RailHalfWidth.")]
            [Range(.2f, 3f)] public float RailWidthMul = 1f;
            [Tooltip("forms4 (P9): a spindle rail of this element swells by RailSwell where the brush was pressed (share of its half width, over the last RailSwellSpan of its body) and starts RailRoot wide (share; 0 = the fence's SpindleRoot): a rail of one width was a wire.")]
            [Range(0f, 1f)] public float RailSwell;
            [Range(.1f, 1f)] public float RailSwellSpan = .6f;
            [Range(0f, 1f)] public float RailRoot;
            [Tooltip("forms3 (D8): the rails are this many bending spindle strokes round the ring, lying over each other (0 = the old ring arcs with a gap between them). SpindleRows = 2 adds a lower row, set off by half a piece, where the tier draws fine strokes.")]
            [Range(0, 24)] public int RailSpindles;
            [Range(1, 2)] public int SpindleRows = 1;
            [Tooltip("forms3 fix pass: this element's rail bow against Fence.SpindleSag (metal: nearly straight).")]
            [Range(0f, 2f)] public float SpindleSagMul = 1f;
            [Tooltip("round 2: share of the element wash this fence's bodies take (thin strokes 1, a wide wall less).")]
            [Range(0f, 1f)] public float Wash = 1f;
        }

        /// <summary>Single attacks: the ink comet that carries the judged flight clock (indexed by Element).</summary>
        [Serializable]
        public sealed class CometElementSet
        {
            public string Name = "";
            [Tooltip("Atlas cell of the head (0 = a forked stroke tip instead of a sprite).")]
            [Range(0, 15)] public int HeadCell;
            [Min(.02f)] public float HeadSize = .2f;
            [Range(0, 3)] public int TailCount = 1;
            [Min(0f)] public float TailLength = 1f;
            [Range(0, 6)] public int ShedDrops;
            [Tooltip("look2: authored half width of the tail (m). 0 = a share of the head size (the element's own).")]
            [Min(0f)] public float TailHalf;
        }

        [Serializable]
        public sealed class CometSet
        {
            public bool Enabled = true;
            [Tooltip("On = the comet's place is read every frame from the flight clock and the target; off = cel steps (the first form).")]
            public bool PerFrame = true;
            [Tooltip("Where the tail lies on the screen plane, from the head: toward the brush tip (right, down).")]
            public Vector2 TailScreen = new Vector2(.8f, -.6f);
            [Tooltip("Grade used for effects an EA runtime builds itself (the brush grade is not handed to them).")]
            [Range(0f, 1f)] public float EaGrade01 = .5f;
            [Tooltip("The head is never smaller than this on screen at the target's distance (deg), the tail never thinner than MinTailDeg; the comet is scaled up for a far target, by MaxScale at most.")]
            [Range(0f, 3f)] public float MinHeadDeg = .8f;
            [Range(0f, 1f)] public float MinTailDeg = .17f;
            [Range(1f, 5f)] public float MaxScale = 3f;
            [Tooltip("No tail is thinner than this (half width, m): a hair-thin line flickers in a cutout shader.")]
            [Min(.002f)] public float TailHalfMin = .02f;
        }

        [Serializable]
        public sealed class SummonSet
        {
            [Tooltip("On = the layer draws the summon's entrance (the rising curtain) and exit itself; the combat summon then starts no formation seal and no dissolve debris. The model, its clock and its sounds are never touched.")]
            public bool OwnEntrance = true;
            [Min(.5f)] public float RiseHeight = 1.5f, CurtainWidth = 1.4f;
            [Tooltip("The curtain stands this far from the summon toward the eye (m).")]
            [Min(0f)] public float StandOff = .4f;
            [Tooltip("look2: the curtain is the earlier Rise again - bold strokes pulled straight up out of the ground, the middle one first and tallest. FootSpan = distance between the outermost strokes' feet (m; with the strokes' own width the curtain is at least shoulder wide), RiseStep = each step away from the middle is lower by this share, FanDeg = and leans outward by this much (deg).")]
            [Min(.2f)] public float FootSpan = .52f;
            [Range(0f, .4f)] public float RiseStep = .2f;
            [Tooltip("look2: half width of the middle stroke as a share of CurtainWidth (the outer ones are a little thinner).")]
            [Range(.03f, .3f)] public float StrokeHalfShare = .146f;
            [Range(0f, 20f)] public float FanDeg = 13f;
            [Tooltip("round 2: the curtain is always Strokes strokes (the grade widens them by GradeWiden each step, it does not add strokes). Each is tall and thin: TailShare = its frayed foot, HeadTaper = share of the wet body that narrows to the one point at its head, Curve / SCurve = its sideways bend (shares of its length). Needles = lines of the old burst star (0 = none). Wash = share of the element wash.")]
            [Range(1, 5)] public int Strokes = 3;
            [Range(0f, .4f)] public float GradeWiden = .08f;
            [Range(0f, .6f)] public float TailShare = .2f;
            [Range(0f, 1f)] public float HeadTaper = .28f;
            [Range(.05f, 1f)] public float RootRamp = .12f;
            [Tooltip("round 2: share of a curtain stroke its brush hairs run over, from the foot up (a stroke pulled out of the ground is dry at its foot and wet at its head).")]
            [Range(0f, 3f)] public float DryShare = .34f;
            [Range(0f, .3f)] public float Curve = .1f, SCurve = .09f;
            [Tooltip("forms3 (D6): back to the accepted 10c rising stroke - thick (height to width about 4 : 1), planted in the ground mark, the feet lying over each other, only the tip drawn to a side. Head = the pressed head at a stroke's top (HeadTaper is used only when Head.Share is 0). RootWidth = width at the foot as a share of the stroke's; RootSink = the foot starts this far UNDER the ground (m: the frayed hairs stand in the ground mark, no gap); OuterWidthShare = an outer stroke's width against the middle one's; OuterCurveStep = each step outward bends this much more; DepthStep = each step outward stands this far behind the middle stroke (m: the feet overlap without lying in one plane); TopJitter = share by which a stroke's top may be lower; HeadRoom = room left under RiseHeight for the head, in half widths; FootJitter = sideways unevenness of a foot (m); BirthStep = cels between the middle stroke and each step outward; NeedleHeightShare = where the optional needle star stands (share of RiseHeight); Segments = rings of a stroke.")]
            public BrushHeadSet Head = new BrushHeadSet { Share = .3f, Lean = 1f, End = .25f, Round = 1.5f, Rings = 5, Cap = .35f, Swell = 0f };   // fix pass 4b: drawn out to one side over half the stroke (a round closed end was a leaf); fix pass: the tip is really drawn out (three round fingertips read as a glove)
            [Range(.1f, 1f)] public float RootWidth = 1f;
            [Min(0f)] public float RootSink = .28f;
            [Tooltip("forms4 (P4): not three equal strokes. OuterWidths / OuterHeights = the width and the height of each step outward against the middle stroke's (first entry = the stroke that rises second, second = the third; a missing entry = OuterWidthShare / RiseStep as before). BodySink = the WET body starts this far under the ground (m; over 0 the frayed foot is whatever of RootSink lies under that - at 8 m the hairs are gone and a body that ended at the ground floated). TurnDeg = each step outward is turned about its upright by this much, away from the eye (the curtain is a folding screen of three leaves: from the side and from 45 degrees a leaf still shows its width - one flat card facing the caster was a thread from the side).")]
            public float[] OuterWidths = { .85f, .74f };
            public float[] OuterHeights = { .85f, .7f };
            [Min(0f)] public float BodySink = .1f;
            [Range(0f, 80f)] public float TurnDeg = 40f;
            [Tooltip("fix pass 4b (director 4: three round-ended leaves fanning evenly from one point were a succulent; the middle stroke was a thread from the side). MidTurnDeg = the middle stroke is turned about its upright too (it shows width from the side). FanOtherDeg = the stroke on the far side of the bend fans by this much instead of FanDeg (an uneven fan: one hand, not a plant's symmetry).")]
            [Range(0f, 45f)] public float MidTurnDeg = 26f;
            [Range(0f, 20f)] public float FanOtherDeg = 5f;
            [Range(.3f, 1f)] public float OuterWidthShare = .9f;
            [Range(0f, 1f)] public float OuterCurveStep = .35f;
            [Min(0f)] public float DepthStep = .03f;
            [Range(0f, .3f)] public float TopJitter = .03f;
            [Range(0f, 1f)] public float HeadRoom = .15f;
            [Min(0f)] public float FootJitter = .02f;
            [Range(0, 4)] public int BirthStep = 2;
            [Range(0f, 1f)] public float NeedleHeightShare = .45f;
            [Range(4, 24)] public int Segments = 12;
            [Range(0, 8)] public int Needles;
            [Range(0f, 1f)] public float Wash = 1f;
            [Min(0f)] public float Puddle = 1.4f, ExitPuddle = 1.2f;
            [Range(0, 12)] public int ExitDrops = 6, ExitDropsMobile = 3;
            [Tooltip("Exit drops: ring they start on (m from the summon), height they fall from (m), size relative to Residue.DropMid, seconds in the air.")]
            [Min(0f)] public float ExitRadiusMin = .15f, ExitRadiusMax = .5f;
            [Min(0f)] public float ExitHeightMin = .6f, ExitHeightMax = 1.5f;
            [Min(.1f)] public float ExitDropSizeMul = 1.4f;
            [Range(.2f, 2f)] public float ExitDropSeconds = 1.6f;
        }

        /// <summary>forms3 fix pass (director 3): the standing wall of a cover the spell presenter shows (a cast that says Cover).
        /// A few thick rising strokes of the curtain's family on the cover's ring instead of the ward category's column ring
        /// (a low saw-toothed crown). Ink: no glow once it has risen. Presentation only.</summary>
        [Serializable]
        public sealed class CoverSet
        {
            [Tooltip("Off = the ward category's column ring, as before.")]
            public bool Enabled = true;
            [Tooltip("Strokes round the ring: one every Apart m, never fewer than StrokesMin, never more than StrokesMax (StrokesMaxCoarse on a tier without fine strokes).")]
            [Min(.2f)] public float Apart = .31f;
            [Range(3, 40)] public int StrokesMin = 7, StrokesMax = 28, StrokesMaxCoarse = 21;   // pass 4c: 21 (was 19) with SegmentsCoarse 4 - no stroke wider than .58 m
            [Tooltip("pass 4b (main decision Q1). The wall's OWN cap: no tip stands over TopMax (1.45 m = a hand under the 1.55 m eye; the ward category's column ring keeps Stroke.WardColumnMaxHeight). Height = the tallest tip asked for (cut to TopMax). The wall HIDES up to HideHeight: every stroke is still wide and wet there; above it the strokes END - the lowest tip stands at TipLow, the tallest at the cap, HeightWobble = how far a tip may stand under its written place (m).")]
            [Range(.5f, 1.45f)] public float TopMax = 1.45f;
            [Min(.3f)] public float Height = 1.5f;
            [Range(.5f, 1.2f)] public float HideHeight = 1f;
            [Range(.6f, 1.45f)] public float TipLow = 1.15f;
            [Tooltip("pass 4c: the least a wet body ends over HideHeight (m), the least its solid ink (after the combed start) ends over it (m), the least a dry tip runs on over the wet body of the lowest stroke (m).")]
            [Range(.02f, .2f)] public float WetOver = .08f;
            [Range(0f, .1f)] public float SolidOver = .01f;
            [Range(.02f, .2f)] public float TipLeast = .03f;
            [Range(0f, .1f)] public float HeightWobble = .01f;
            [Tooltip("pass 4b: uneven tips, a WRITTEN order (no random level row). The i-th stroke round the ring ends Tips[i mod n] of the way from TipLow (0) to the cap (1): tall beside low, no two neighbours within 5 cm, TipsApart = the least difference (share) between two neighbours - where the ring's count is no multiple of n the last stroke takes a later share of the order to keep it.")]
            public float[] Tips = { 1f, .067f, .8f, .4f, .967f, 0f, .733f, .467f, .9f, .133f, .7f, .033f, .6f };   // pass 4c: tall beside low, turn about (1.45 1.17 1.39 1.27 1.44 1.15 1.37 1.29 1.42 1.19 1.36 1.16 1.33 m) - the old order rose and fell as one wave: a scalloped rim
            [Tooltip("pass 4c: where each stroke's WET body ends - the share of the way from HideHeight to its tip, stroke after stroke (the same order as Tips; a missing entry = TipWet). Most are lifted just over HideHeight and run on as a long dry tip (.2 - .3 m), a few stay wet nearly to the tip: the wet ink ends at unequal heights and the skyline is dry brush.")]
            public float[] TipWets = { .889f, .53f, .51f, .3f, .455f, .53f, .76f, .3f, .2f, .4f, .47f, .56f, .25f };
            [Range(0f, .5f)] public float TipsApart = .2f;
            [Tooltip("pass 4b: the tip. The wet body starts TipWet of the way from HideHeight up to the tip (above it: the dry tail's hairs). It starts TipWidth of the stroke's width wide on the tallest tip and TipWidthLow on the lowest (a tall tip is drawn out long and thin, a low one ends short and broad), and has its full width again TipRun m under where it starts (at the latest RampUnder m under HideHeight). The hairs are combed over DryShare of the wet body on the tallest tip (x DryLow on the lowest: its combed part must end over HideHeight).")]
            [Range(0f, 1f)] public float TipWet = .65f;
            [Range(.15f, 1f)] public float TipWidth = .27f, TipWidthLow = .7f;   // pass 4c: by where the WET body ends (it was by the tip's height)
            [Min(0f)] public float RampUnder = .2f, TipRun = .6f;
            [Range(0f, 3f)] public float DryShare = .4f;
            [Range(0f, 1f)] public float DryLow = .5f;
            [Tooltip("pass 4b: the sway. The foot stands upright; the tip is drawn along the ring - the tallest tip by SwayDeg, the lowest by SwayLowDeg, each between Least and all of its own - always the same way round (alternating sways open V-shaped gaps).")]
            [Range(0f, 30f)] public float SwayDeg = 28f, SwayLowDeg = 22f;
            [Tooltip("pass 4c: a ring smaller than SwayFullAt m sways less (by its radius: on a small ring the same sway opens the wall at the hide height).")]
            [Min(.1f)] public float SwayFullAt = 1f;
            [Range(0f, 1f)] public float Least = .75f;
            [Min(.03f)] public float HalfWidth = .15f;
            [Range(0f, .4f)] public float WidthWobble = .09f;
            [Tooltip("forms4 (P2 / P3): the wall HIDES and is no row of flat cards facing the caster. A stroke stands ON the ring facing outward (its width along the ring) and is turned about its own upright by TurnDeg, turn about - the strokes fold like a screen. Its half width = its share of the ring x RingCover / 2 (over 1: neighbours overlap), between HalfWidth and HalfMax. Jitter = how far it may stand off its even place (share of the spacing).")]
            [Range(0f, 45f)] public float TurnDeg = 18f;
            [Range(.5f, 1.8f)] public float RingCover = 1.5f;
            [Min(.05f)] public float HalfMax = .267f;   // pass 4c: .267 (was .33) - no stroke wider than .58 m where its pressed foot begins to swell
            [Range(0f, .4f)] public float Jitter = .04f;
            [Tooltip("pass 4b: the largest ring (m) the wall is stood on - PC / the coarse tier. On a larger one the strokes the vertex budget allows cannot close it (F18 shows the wall closed up to these), so NO wall is stood: the cover keeps its held ground marks and its ring stroke.")]
            [Min(0f)] public float ClosesTo = 1.6f, ClosesToCoarse = 1.2f;   // pass 4c: 1.6 / 1.2 (were 2.1 / 1.35) - beyond them a stroke is wider than .58 m: a plank
            [Tooltip("The stroke: how far the pressed foot starts under the ground (m), rings (fine / coarse tier), the pressed foot (its nub lies under the ground; Swell = how much wider the stroke is where the brush was pressed), share of the element wash the body takes.")]
            [Min(0f)] public float RootSink = .2f;
            [Range(4, 16)] public int Segments = 6, SegmentsCoarse = 4;
            public BrushHeadSet Foot = new BrushHeadSet { Share = .12f, Lean = 1f, End = .55f, Round = 2f, Rings = 1, Cap = .45f, Swell = .22f, SwellSpan = .45f };
            [Tooltip("pass 4c: how unlike the pressed feet are (the foot's length x 1 - FootVary .. 1, its swell x 1 +- FootVary).")]
            [Range(0f, .6f)] public float FootVary = .35f;
            [Range(0f, 1f)] public float Wash = .8f;
        }

        /// <summary>The triggered burst of an installed mark (the combo burst). pass 4b (main decision Q2): of the accepted single
        /// burst's family - the ink mass (the pressed heads) at the centre, the strokes thrown outward into dry tails, uneven
        /// angles and lengths; not the top grade's single burst (a bundle of long forked strokes). Presentation only.</summary>
        [Serializable]
        public sealed class TriggerSet
        {
            [Tooltip("Off = the old triggered burst (the top grade's single burst).")]
            public bool Own = true;
            [Tooltip("Bold strokes at most; their length against the grade's stroke length (each between LengthLeast and all of it); their width against the grade's (each between WidthLeast and all of it).")]
            [Range(1, 7)] public int Bold = 6;
            [Range(.2f, 1f)] public float LengthMul = .85f;
            [Range(.3f, 1f)] public float LengthLeast = .8f;
            [Range(.3f, 1.5f)] public float WidthMul = .8f, WidthLeast = .75f;
            [Tooltip("pass 4b (Q2): the MASS is at the centre. A bold stroke's pressed head lies Through m PAST the point (each between ThroughLeast and all of it), so the heads lie over each other and pool; from there the stroke is thrown outward: TailShare of it is the dry tail at its outer end, the wet body is combed out of that tail over DryShare, starts RootWidth of its width wide and is full RootRamp of the way to the head (thin and dry outside, full and wet at the centre).")]
            [Min(0f)] public float Through = .16f;
            [Range(0f, 1f)] public float ThroughLeast = .35f;
            [Range(0f, .6f)] public float TailShare = .34f;
            [Range(0f, 3f)] public float DryShare = .55f;
            [Range(.2f, 1f)] public float RootWidth = .42f;
            [Range(.05f, 1f)] public float RootRamp = .7f;
            [Tooltip("m from the point at which the fine dry strokes and the needle lines start (x FineOpen), and how unevenly they keep it (+-).")]
            [Min(0f)] public float Open = .35f;
            [Range(0f, .5f)] public float OpenWobble = .3f;
            [Tooltip("How far a stroke may stand off its even place round the point (share of the angle between two).")]
            [Range(0f, .5f)] public float Scatter = .12f;
            [Tooltip("Share of the grade's fine dry strokes and needle lines the burst keeps.")]
            [Range(0f, 1f)] public float FineShare = .8f, NeedleShare = .6f;
            [Tooltip("The pressed head AT THE CENTRE: blunt, lopsided, a little swollen (the brush came down there).")]
            public BrushHeadSet Head = new BrushHeadSet { Share = .26f, Lean = 1f, End = .5f, Round = 2.2f, Rings = 3, Cap = .6f, Swell = .14f, SwellSpan = .4f };
            [Tooltip("forms4 (P7): the burst is of the accepted single burst's family - an INK body with the element as a wash. Its glow rule is that burst's own (inside the burst beat, each stroke for GlowCels cels after ITS birth), so strokes born late in the beat were still flat element colour when the burst was looked at: the bold strokes are all born within the first BirthCels cels. FineOpen = the fine dry strokes start at this share of Open; Short = length of every other bold stroke where Lengths names none.")]
            [Range(1, 8)] public int BirthCels = 3;
            [Range(0f, 1f)] public float FineOpen = .3f;
            [Range(.4f, 1f)] public float Short = .6f;
            [Tooltip("fix pass 4b (director 4: six strokes at an even 60 degrees were a propeller). Offsets = how far each bold stroke stands off its even place (share of the angle between two; a written order - two or three strokes gather, the others stand wide; a missing entry = 0), on top of Scatter. Lengths = each stroke's length as a share of the longest (a written order: two long, the others short). ViewHalf = a stroke that runs up the view ends within this share of the eye's distance over the point (it stays inside the picture).")]
            public float[] Offsets = { 0f, .34f, -.3f, .22f, -.36f, .1f };
            public float[] Lengths = { 1f, .56f, .7f, .92f, .52f, .64f };
            [Range(.1f, .6f)] public float ViewHalf = .4f;
        }

        [Serializable]
        public sealed class BuffSet
        {
            [Tooltip("A mark of the running buff is left beside each footprint (no standing display of any kind).")]
            public bool FootMarks = true;
            [Min(.02f)] public float FootMarkSize = .09f;
            [Tooltip("Seconds a buff mark / a fire-aura drop stays (clamped to 3-5).")]
            public float MarkLife = 3.5f, AuraLife = 3f;
            [Tooltip("Fire aura: drops are laid on its reach every this many seconds (the rule's own damage interval).")]
            [Min(.1f)] public float AuraInterval = .5f;
            [Range(0, 8)] public int ShedDrops = 3;
            [Min(0f)] public float EndRing = 1.1f;
            [Tooltip("End drops: where they are born against the eye (m ahead along the flat heading, m below, row width), the upward speed they start with (m / s), size relative to Residue.DropMid, seconds in the air. Born inside the view (about -14 deg) and tossed up, so that they stay on screen for a third of a second.")]
            [Min(.3f)] public float ShedForward = 1.2f;
            [Min(0f)] public float ShedDown = .3f, ShedWidth = .5f, ShedLift = 1.2f;
            [Min(.1f)] public float ShedSizeMul = 1.4f;
            [Tooltip("forms4 (P8): the end drops are not three copies. Each differs in size by up to ShedSizeVary (share, +-), starts up to ShedHeightVary m higher or lower, is tossed with ShedLiftVary (share, +-) more or less lift - so they turn over and fall at different moments - and drifts sideways by up to ShedDrift m/s. The differences are drawn from the end's own seed (no Random): the same end looks the same.")]
            [Range(0f, .6f)] public float ShedSizeVary = .35f;
            [Min(0f)] public float ShedHeightVary = .14f;
            [Range(0f, .8f)] public float ShedLiftVary = .45f;
            [Min(0f)] public float ShedDrift = .55f;   // pass 4c: .55 (was .3) - tossed up to 25 degrees off the upright (three drops falling straight down side by side were three dashes: a HUD tick)
            [Tooltip("pass 4c: ShedLiftWithHeight = the drop that starts higher is also tossed harder (one share for both), so the three never fall level; ShedTailMax = an end drop is drawn at most that many sizes long (0 = the profile's AirTail.Max).")]
            public bool ShedLiftWithHeight = true;
            [Range(0f, 6f)] public float ShedTailMax = 3f;
            [Range(.2f, 2f)] public float ShedSeconds = 1.6f;
            [Tooltip("Foot marks: sideways distance of the first mark in foot widths, the step to each next one, how far above the ground the stamp is asked for (m), most marks one step may be asked for.")]
            [Min(0f)] public float FootMarkOffset = 1.3f, FootMarkStep = 1f, FootMarkLift = .25f;
            [Range(0, 8)] public int FootMarksAsked = 4;
            [Tooltip("Fire aura drops: the ring is turned by this much each time (deg), drop size relative to Residue.DropMid.")]
            [Range(0f, 360f)] public float AuraTurnDeg = 137.5f;
            [Min(.1f)] public float AuraSizeMul = 1.5f;
            [Tooltip("How often the layer asks whether a buff / a field still runs (per second).")]
            [Range(1f, 30f)] public float WatchHz = 6f;
            [Tooltip("Atlas cells of the five buff marks (wood fire earth metal water).")]
            public int[] MarkCells = { 5, 10, 6, 12, 14 };   // forms4 (P9): metal 11 -> 12 (cell 11 is a square frame; 12 is the thin six-pointed star - thin and sharp, metal's own character, no other element's mark)   // forms3 D5: fire 15 -> 10 (the drip cell fills its quad edge to edge and printed a square)
            [Tooltip("PROPOSED, off: an ink band on the brush shaft while a buff runs. Close to a standing display - needs the HUD whitelist question first. Nothing reads it yet.")]
            public bool BrushBand;
        }

        [Serializable]
        public sealed class LooseSet
        {
            [Min(.05f)] public float SealSize = .2f;
            [Tooltip("round 2: the blot's quad against SealSize (the blot itself fills about two thirds of it: the mark is about .3 m wide), needle lines beside it (count, length range in half seal sizes), share of the element wash.")]
            [Range(.5f, 2f)] public float BlotMul = 1.3f;
            [Range(0, 4)] public int Needles = 2;
            [Range(0f, 2f)] public float NeedleMin = .5f, NeedleMax = 1f;
            [Range(0f, 1f)] public float Wash = 1f;
            [Range(1, 8)] public int SlideCel = 3, FallCel = 5;
            [Min(.05f)] public float GroundSize = .3f;
            [Tooltip("look2: the mark is an ink blot with drops running down from it (no square). Runs = drops, RunLength = how long a run is (m).")]
            [Range(1, 3)] public int Runs = 3;
            [Min(.02f)] public float RunLength = .16f;
        }

        [Serializable]
        public sealed class FieldMarkSet
        {
            [Tooltip("Hook B field marks stay wet while the wiring says the platform / bridge exists, and never longer than this (s).")]
            [Min(1f)] public float MaxHold = 60f;
            [Tooltip("A field's marks are not asked about for this long after the cast (the platform / bridge is still being made).")]
            [Min(0f)] public float WatchGrace = 1f;
        }

        public BeatSet Beats = new BeatSet();
        [Tooltip("Low / mid / high (grade01 < .4 <= mid < .75 <= high).")]
        public GradeSet[] Grades =
        {
            new GradeSet { Bold = 3, Fine = 4, Needles = 4, Width = .22f, LengthMul = .8f, Drops = 12, ImpactFrames = 1 },
            new GradeSet { Bold = 5, Fine = 8, Needles = 6, Width = .30f, LengthMul = 1f, Drops = 24, ImpactFrames = 2 },
            new GradeSet { Bold = 7, Fine = 12, Needles = 8, Width = .38f, LengthMul = 1.2f, Drops = 40, ImpactFrames = 3 },
        };
        [Range(0f, 1f)] public float GradeMid = .4f, GradeHigh = .75f;
        public StrokeSet Stroke = new StrokeSet();
        [Tooltip("Indexed by Element (Wood, Fire, Earth, Metal, Water): the element reads by shape first.")]
        public ElementSet[] Elements =
        {
            new ElementSet { Name = "Wood", LengthMul = 1.25f, Split = 3, DropMul = .8f },
            new ElementSet { Name = "Fire", Curve = .35f, TailMul = 2f, DropMul = .8f, DropSizeMul = .7f },
            new ElementSet { Name = "Earth", LengthMul = .7f, WidthMul = 1.3f, TailMul = .5f, NeedleMul = .5f, DropMul = 1.2f, DropSizeMul = 1.6f },
            new ElementSet { Name = "Metal", WidthMul = .6f, TailMul = .6f, NeedleMul = 2f, DropMul = .5f, Tint = 0f },
            new ElementSet { Name = "Water", SCurve = .3f, TailMul = .8f, NeedleMul = .6f, DropMul = 1.8f },
        };
        [Tooltip("Indexed by DeployCategory308.")]
        public CategorySet[] Categories =
        {
            new CategorySet { Name = "AttackSingle", ResidueDrops = 8 },
            new CategorySet { Name = "AttackArea", ResidueDrops = 20 },
            new CategorySet { Name = "ComboInstall", Bold = 0f, Fine = 0f, Needles = .34f, ResidueDrops = 6, HeldResidue = true },
            new CategorySet { Name = "Parry", Bold = 0f, Fine = 0f, Needles = 1f, ResidueDrops = 0 },
            new CategorySet { Name = "Buff", Bold = 0f, Fine = .25f, Needles = 0f, ResidueDrops = 8, Wind = true },
            new CategorySet { Name = "Summon", Bold = 0f, Fine = 0f, Needles = 0f, ResidueDrops = 10, Column = true },
            new CategorySet { Name = "Ward", Bold = 0f, Fine = 0f, Needles = 0f, ResidueDrops = 12, Column = true },
            new CategorySet { Name = "Field", Bold = 0f, Fine = 0f, Needles = 0f, ResidueDrops = 24, HeldResidue = true },
            new CategorySet { Name = "Blank", Bold = 0f, Fine = 0f, Needles = 0f, ResidueDrops = 1 },
        };
        [Tooltip("Indexed by DeployFinal308: the end treatment follows the jamo shape, not the effect meaning.")]
        public FinalSet[] Finals =
        {
            new FinalSet { Name = "None", TipShare = 0f }, new FinalSet { Name = "Giyeok" }, new FinalSet { Name = "Nieun" },
            new FinalSet { Name = "Mieum" }, new FinalSet { Name = "Siot" }, new FinalSet { Name = "Ieung" },
        };
        public CameraSet Camera = new CameraSet();
        public ImpactSet Impact = new ImpactSet();
        public FlickerSet Flicker = new FlickerSet();
        public HitSet Hit = new HitSet();
        public HudSet Hud = new HudSet();
        public ResidueSet Residue = new ResidueSet();
        public FootSet Foot = new FootSet();
        public AirTailSet AirTail = new AirTailSet();
        public FloodSet Flood = new FloodSet();
        [Tooltip("Indexed by DeployTier308 (PC, Mobile).")]
        public TierSet[] Tiers =
        {
            new TierSet { Name = "PC", Forms2Saved = true },
            new TierSet { Name = "Mobile", Bursts = 4, VertsPerBurst = 1024, BoldMax = 4, Fine = false, NeedleMax = 4, CelHz = 10f, ColumnStrips = 12,
                SpellResidue = 64, Footprints = 12, AirDrops = 48, FullScreen = false, FlickerMax = 2, HudReactDefault = false, Flood = false,
                FovBreath = false, UseMobileAtlas = true,
                StandingBursts = 3, VertsPerStanding = 640, FencePosts = 6, FenceRails = 4, FenceRingDrops = 6, GuardNeedles = 2, GuardDryTicks = false,
                CometTails = 1, CometDrops = 2, FootMarksPerStep = 1, FootMarksPerSecond = 2f, AuraStamps = 2, Forms2Saved = true },
        };
        public GuardSet Guard = new GuardSet();
        [Tooltip("Indexed by Element.")]
        public GuardElementSet[] GuardElements =
        {
            new GuardElementSet { Name = "Wood", SpanMul = 1.05f },
            new GuardElementSet { Name = "Fire", Rise = .06f },
            new GuardElementSet { Name = "Earth", SpanMul = .95f, WidthMul = .92f, NeedleMul = .5f },   // forms4 (P9): 12 % narrower (a sweet potato)   // forms3: longer against its width (it read as a lump)
            new GuardElementSet { Name = "Metal", WidthMul = .7f, Lines = 1, NeedleMul = 2f, HeadShareMul = 1.15f, HeadEndMul = .95f, DryWidthMul = 1.3f },   // fix pass: the head is blunt like the others (a long small head narrowed both ends: a rod);   // forms3 D4: one line, thinner and sharper (two parallel lines read as a HUD bar)
            new GuardElementSet { Name = "Water", Wave = .1f, NeedleMul = .5f, HangDrops = 2 },
        };
        public FenceSet Fence = new FenceSet();
        [Tooltip("Indexed by Element.")]
        public FenceElementSet[] FenceElements =
        {
            new FenceElementSet { Name = "Wood", LeanDeg = 8f, LowRail = false, RailSpindles = 12, SpindleRows = 1, RailWidthMul = 1.8f },   // forms3 D8: bending spindle rails, the ring closes
            new FenceElementSet { Name = "Fire", PostMul = 0f, Rail = false, LowRail = false, Tongues = 6, Wash = .85f },   // forms3 D7: flame tongues, no rail (pass 4b: 5 a rail = 20 on PC, in pairs and threes)
            new FenceElementSet { Name = "Earth", PostMul = 0f, Rail = false, LowRail = false, Courses = 2, Wash = .45f },
            new FenceElementSet { Name = "Metal", PostMul = 2f, RailAtTop = true, LowRail = false, RailSpindles = 6, SpindleSagMul = .5f, RailWidthMul = 1.6f, RailSwell = .5f, RailRoot = .18f },   // fix pass: bending pieces, the ring closes (ruled rods with a gap)
            new FenceElementSet { Name = "Water", PostMul = 0f, Rail = false, LowRail = false, Wave = true, WaveRows = 1, HangDrops = 1 },   // forms3 D8: one line
        };
        public CometSet Comet = new CometSet();
        [Tooltip("Indexed by Element: head cell / tail count / tail length / shed drops differ for all five.")]
        public CometElementSet[] Comets =
        {
            new CometElementSet { Name = "Wood", HeadCell = 0, HeadSize = .16f, TailCount = 1, TailLength = 1.4f, ShedDrops = 0 },
            new CometElementSet { Name = "Fire", HeadCell = 5, HeadSize = .26f, TailCount = 2, TailLength = .7f, ShedDrops = 2 },
            new CometElementSet { Name = "Earth", HeadCell = 7, HeadSize = .34f, TailCount = 3, TailLength = .25f, ShedDrops = 3 },
            new CometElementSet { Name = "Metal", HeadCell = 12, HeadSize = .22f, TailCount = 1, TailLength = 2.0f, ShedDrops = 0, TailHalf = .05f },
            new CometElementSet { Name = "Water", HeadCell = 4, HeadSize = .24f, TailCount = 1, TailLength = .9f, ShedDrops = 4 },
        };
        public SummonSet Summon = new SummonSet();
        public CoverSet Cover = new CoverSet();
        public TriggerSet Trigger = new TriggerSet();
        public BuffSet Buff = new BuffSet();
        public LooseSet Loose = new LooseSet();
        public FieldMarkSet FieldMark = new FieldMarkSet();
        // round 2: the wash share of the standing forms is each form's own number now (Guard.Wash, FenceElements[].Wash, Summon.Wash,
        // Loose.Wash): one share for all of them left wood / earth / metal and water indistinguishable.
        [Tooltip("look2: share of the momentary birth glow (Stroke.GlowAmount) the standing forms take. It only lowers the glow; the glow's cels and its end are the same rule.")]
        [Range(0f, 1f)] public float StandingGlow = .45f;
        [Tooltip("Quality level names that use the Mobile tier.")]
        public string[] MobileQualityNames = { "Mobile" };

        // ---- clamped reads: the runtime never trusts the raw fields for the D308-10 / D308-10b limits ----
        public float SpellLife => Mathf.Clamp(Residue.SpellLife, MinResidueLife, MaxResidueLife);
        public float FootLife => Mathf.Clamp(Residue.FootLife, MinResidueLife, MaxResidueLife);
        public float FieldTail => Mathf.Clamp(Residue.FieldTail, MinResidueLife, MaxResidueLife);
        public float FlipsPerSecond => Mathf.Clamp(Impact.TokensPerSecond, 0f, MaxFlipsPerSecond);
        public float TintMax => Mathf.Clamp(Stroke.TintMax, 0f, .35f);
        public float HudFill => Mathf.Clamp(Hud.Fill, 0f, .35f);
        public float CelSeconds(TierSet tier) => 1f / Mathf.Clamp(tier != null ? Mathf.Min(tier.CelHz, Beats.CelHz) : Beats.CelHz, 4f, 30f);
        public float ImpactFrameSeconds => Mathf.Clamp(Impact.FrameSeconds, MinImpactFrameSeconds, MaxImpactFrameSeconds);
        public float ImpactTotalSeconds(int frames) => Mathf.Clamp(frames, 0, 3) * ImpactFrameSeconds;
        // D308-10c momentary glow: the runtime and the shader never see the raw fields
        public float GlowAmount => Mathf.Clamp(Stroke.GlowAmount, 0f, MaxGlowAmount);
        public int GlowCels => Mathf.Clamp(Stroke.GlowCels, 0, MaxGlowCels);
        /// <summary>The glow's falloff `cels` after a stroke's birth: 1 on the birth cel, stepping down to 0 at GlowCels and staying
        /// there. InkBurst308 computes the same expression per vertex (birth cel in TEXCOORD1.y).</summary>
        public float GlowFalloff(float celsSinceBirth)
        {
            int cels = GlowCels;
            return cels <= 0 || celsSinceBirth < 0f ? 0f : Mathf.Clamp01(1f - celsSinceBirth / cels);
        }

        public int GradeIndex(float grade01) => grade01 < GradeMid ? 0 : grade01 < GradeHigh ? 1 : 2;
        public GradeSet Grade(float grade01) => Grades != null && Grades.Length == 3 ? Grades[GradeIndex(grade01)] : Fallback.Grades[GradeIndex(grade01)];
        public ElementSet ElementOf(Element element)
        {
            int i = (int)element;
            return Elements != null && i >= 0 && i < Elements.Length && Elements[i] != null ? Elements[i] : Fallback.Elements[Mathf.Clamp(i, 0, 4)];
        }
        public CategorySet CategoryOf(DeployCategory308 category)
        {
            int i = (int)category;
            return Categories != null && i >= 0 && i < Categories.Length && Categories[i] != null ? Categories[i] : Fallback.Categories[Mathf.Clamp(i, 0, 8)];
        }
        public FinalSet FinalOf(DeployFinal308 final)
        {
            int i = (int)final;
            return Finals != null && i >= 0 && i < Finals.Length && Finals[i] != null ? Finals[i] : Fallback.Finals[Mathf.Clamp(i, 0, 5)];
        }
        public TierSet Tier(DeployTier308 tier)
        {
            int i = (int)tier;
            var t = Tiers != null && i >= 0 && i < Tiers.Length && Tiers[i] != null ? Tiers[i] : Fallback.Tiers[Mathf.Clamp(i, 0, 1)];
            if (t.Forms2Saved) return t;
            // #308 forms2: an asset saved before the stage has no forms2 numbers on its tiers - they deserialise to the class
            // defaults, which are the PC's, on Mobile too. Until forms308-profile writes them the code's own tier answers for them.
            i = Mathf.Clamp(i, 0, 1);
            if (_forms2Tiers == null) _forms2Tiers = new TierSet[2];
            if (_forms2Tiers[i] == null) _forms2Tiers[i] = t.WithForms2Of(Forms2Defaults(i));
            return _forms2Tiers[i];
        }
        public DeployTier308 TierForQuality(string qualityName)
        {
            if (MobileQualityNames != null && !string.IsNullOrEmpty(qualityName))
                foreach (var name in MobileQualityNames)
                    if (!string.IsNullOrEmpty(name) && string.Equals(name, qualityName, StringComparison.OrdinalIgnoreCase)) return DeployTier308.Mobile;
            return DeployTier308.PC;
        }
        // ---- #308 forms2 reads
        public float BuffMarkLife => Mathf.Clamp(Buff.MarkLife, MinResidueLife, MaxResidueLife);
        public float BuffAuraLife => Mathf.Clamp(Buff.AuraLife, MinResidueLife, MaxResidueLife);
        public GuardElementSet GuardOf(Element element)
        {
            int i = (int)element;
            return GuardElements != null && i >= 0 && i < GuardElements.Length && GuardElements[i] != null ? GuardElements[i] : Fallback.GuardElements[Mathf.Clamp(i, 0, 4)];
        }
        public FenceElementSet FenceOf(Element element)
        {
            int i = (int)element;
            return FenceElements != null && i >= 0 && i < FenceElements.Length && FenceElements[i] != null ? FenceElements[i] : Fallback.FenceElements[Mathf.Clamp(i, 0, 4)];
        }
        public CometElementSet CometOf(Element element)
        {
            int i = (int)element;
            return Comets != null && i >= 0 && i < Comets.Length && Comets[i] != null ? Comets[i] : Fallback.Comets[Mathf.Clamp(i, 0, 4)];
        }
        public int BuffMarkCell(int index) => Buff.MarkCells != null && index >= 0 && index < Buff.MarkCells.Length ? Mathf.Clamp(Buff.MarkCells[index], 0, 15) : 4;

        public bool IsEnabled(char letter) => LayerEnabled && letter != default && !string.IsNullOrEmpty(EnabledLetters) && EnabledLetters.IndexOf(letter) >= 0;
        public bool FloodAllowed(string eventName)
        {
            if (string.IsNullOrEmpty(eventName) || Flood.AllowedEvents == null) return false;
            foreach (var allowed in Flood.AllowedEvents) if (allowed == eventName) return true;
            return false;
        }

        [NonSerialized] private TierSet[] _forms2Tiers;   // per instance, rebuilt when the asset is edited (OnValidate)
        /// <summary>Checks only: the code's forms2 numbers of a tier come from here (the Fallback instance in the engine).</summary>
        [NonSerialized] public TierSet[] Forms2DefaultsForChecks;
        private TierSet Forms2Defaults(int index) => Forms2DefaultsForChecks != null ? Forms2DefaultsForChecks[index] : Fallback.Tiers[index];
        /// <summary>The cached forms2 tiers are dropped (an edit of the asset, a check that changed a tier).</summary>
        public void ResetForms2Tiers() { _forms2Tiers = null; }

        // code defaults for a profile whose arrays were emptied by hand; an instance field, never a static cache
        [NonSerialized] private SpellDeploy308ProfileSO _fallback;
        private SpellDeploy308ProfileSO Fallback
        {
            get
            {
                if (_fallback == null) { _fallback = CreateInstance<SpellDeploy308ProfileSO>(); _fallback.hideFlags = HideFlags.HideAndDontSave; }
                return _fallback;
            }
        }

        private void OnDisable()
        {
            if (_fallback == null) return;
            if (Application.isPlaying) Destroy(_fallback); else DestroyImmediate(_fallback);
            _fallback = null;
        }

        private void OnValidate()
        {
            _forms2Tiers = null;
            Residue.SpellLife = Mathf.Clamp(Residue.SpellLife, MinResidueLife, MaxResidueLife);
            Residue.FootLife = Mathf.Clamp(Residue.FootLife, MinResidueLife, MaxResidueLife);
            Residue.FieldTail = Mathf.Clamp(Residue.FieldTail, MinResidueLife, MaxResidueLife);
            Impact.TokensPerSecond = Mathf.Clamp(Impact.TokensPerSecond, 0f, MaxFlipsPerSecond);
            Impact.PaperValue = Mathf.Min(Impact.PaperValue, PaperCeiling);
            Impact.FrameSeconds = Mathf.Clamp(Impact.FrameSeconds, MinImpactFrameSeconds, MaxImpactFrameSeconds);
            Stroke.TintMax = Mathf.Clamp(Stroke.TintMax, 0f, .35f);
            Stroke.GlowAmount = Mathf.Clamp(Stroke.GlowAmount, 0f, MaxGlowAmount);
            Stroke.GlowCels = Mathf.Clamp(Stroke.GlowCels, 0, MaxGlowCels);
            Hud.Fill = Mathf.Clamp(Hud.Fill, 0f, .35f);
            Buff.MarkLife = Mathf.Clamp(Buff.MarkLife, MinResidueLife, MaxResidueLife);
            Buff.AuraLife = Mathf.Clamp(Buff.AuraLife, MinResidueLife, MaxResidueLife);
        }
    }
}
