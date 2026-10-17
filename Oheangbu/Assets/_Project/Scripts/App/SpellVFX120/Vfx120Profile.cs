using System;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public enum Vfx120Behavior { Projectile, Bind, Heal, Shield, Zone, Summon, Weapon, Buff, Burst, Wave, Reserve }
    public enum Vfx120Layout { Spear, Spiral, Fan, Rain, Dome, Field, Orbit, Wave, Cage, Petals, Pillar, Helix }
    public enum Vfx120NativeBodyMotion { None, FlameCone, SandFront }
    public enum Vfx120AreaRemake { None, BambooField, FlameCone, NeedleVolley, SandFront, WaterWave }
    public enum Vfx120BotanicalKind { None, RootBind, BambooFront, PlantedTree }
    public enum Vfx120WardKind { None, Wood, Earth, Metal, Water, Fire }

    [CreateAssetMenu(menuName = "Oheangbu/VFX120/Spell profile")]
    public sealed class Vfx120Profile : ScriptableObject
    {
        public string Glyph;
        public string Title;
        public string Intent;
        public string Family;
        public bool Assigned;
        // #308 spell deploy layer (SPEC-SPELL-DEPLOY-308): null = the current presentation, unchanged. Set per row by deploy308-enable.
        public Oheangbu.Data.Spell.SpellDeploy308ProfileSO Deploy308;
        public Vfx120Behavior Behavior;
        public Vfx120Layout Layout;
        public Mesh BodyMesh;
        public Mesh AccentMesh;
        // Optional rigid stone rig in the original normalized full-body space.
        // The source whole-body mesh remains available for provenance/fallback.
        public Mesh[] GuardianMeshes;
        public Vector3[] GuardianPivots;
        public Vector3 GuardianFistContact;
        public Material BodyMaterial;
        public Material InkMaterial;
        public Material PatternMaterial;
        public Material MistMaterial;
        // Optional curated KTP copies. Unassigned profiles keep their existing presentation.
        public bool UseOriginalKtp;
        public GameObject NativeCastPrefab;
        public GameObject NativeImpactPrefab;
        public GameObject NativeFieldPrefab;
        public Vfx120TraditionalMotif.Role NativeFieldRole = Vfx120TraditionalMotif.Role.Summon;
        public float NativeScale = .65f;
        public float NativeImpactScale = 1f;
        // Opt-in review treatment. Legacy profiles retain their exact placement and tint.
        public bool KtpEmphasis;
        public float KtpCastMultiplier = 1f, KtpImpactMultiplier = 1f, KtpFieldMultiplier = 1f;
        public float KtpContactMultiplier = 1f, KtpBrightness = 1f;
        public Vector3 KtpCastOffset, KtpImpactOffset, KtpFieldOffset;
        public bool KtpQuickCast, KtpPatternShield;
        public bool CameraOffsetLaunch;
        public Vector2 LaunchViewport = new Vector2(2f/3f,1f/3f);
        public float LaunchDepth = 1.6f, KtpContactBrightness, KtpContactSurfaceOffset;
        public GameObject GuardContactPrefab;
        public bool KtpRectShield;
        public Vfx120AreaRemake AreaRemake;
        public GameObject AreaContactPrefab;
        // #308 D308-104 (e): one small ink stain on the ground under an enemy hit by a range-ㄱ spell (TEST values; scale 1, outside the KTP scale chain)
        public GameObject AreaStainPrefab;
        public float AreaStainDiameter = 2f, AreaStainAlpha = .12f, AreaStainSeconds = 2.5f, AreaStainMinNormalY = .45f;
        public float AreaCastSeconds = .32f;
        public bool WideAreaRevision;
        public bool AreaFlowRevision;
        public bool AreaReadabilityRevision, EarthRift;
        public bool BranchedEarthRift;
        public bool ProceduralEarthRift;
        public Vfx120WardKind WardKind;
        public float WardRadius=3, WardHeight=2.2f, WardFormation=.45f, WardFade=.5f;
        public float WardGroundProbeHeight=16f, WardGroundProbeDepth=48f;
        public Material WardMaterial, WardWaterMaterial;
        public GameObject WardPatternPrefab, WardContactPrefab, WardDebrisPrefab;
        public bool NativeReplaceBody;
        // Curated material-bearing particles replace repeated solid plates only
        // when a compatible spatial plan is supplied by the caller.
        public GameObject NativeBodyPrefab;
        public Vfx120NativeBodyMotion NativeBodyMotion;
        // Curated, textured botanical meshes; the original generator remains a fallback.
        public GameObject BotanicalPrefab;
        public Vfx120BotanicalKind BotanicalKind;
        // Optional real Meshy summon. Static geometry; no synthesized gait or scaling motion.
        public GameObject SummonPrefab;
        public bool WoodDeerPresentation;
        public GameObject WoodDeerPrefab, WoodDeerSeal, WoodDeerDebris;
        public bool FireHaetaePresentation;
        public GameObject FireHaetaePrefab, FireHaetaeSeal, FireHaetaeDebris;
        public bool MetalTigerPresentation;
        public GameObject MetalTigerPrefab, MetalTigerSeal, MetalTigerDebris;
        public bool DokkaebiClubPresentation;
        public GameObject DokkaebiClubPrefab, DokkaebiClubSeal, DokkaebiClubDebris;
        public bool WaterTurtlePresentation;
        public GameObject WaterTurtlePrefab, WaterTurtleSeal, WaterTurtleDebris;
        public bool StoneDokkaebiPresentation;
        public GameObject StoneDokkaebiPrefab, StoneDokkaebiSeal, StoneDokkaebiDebris;
        public float SummonScale = 1f;
        public float SummonYaw;
        public Color Pigment = new Color(.18f, .3f, .23f);
        public Color Accent = new Color(.56f, .47f, .3f);
        public Color Ink = new Color(.075f, .065f, .058f);
        [Range(1, 32)] public int Count = 10;
        [Range(0, 3)] public int RibbonCount = 2;
        [Range(.2f, 8f)] public float Size = 2f;
        public Vector3 PartScale = new Vector3(.14f, .14f, .85f);
        [Range(.5f, 12f)] public float Duration = 3f;
        [Range(.1f, 3f)] public float Flight = .6f;
        [Range(0f, 8f)] public float Turns = 1f;
        [Range(0f, 3f)] public float Lift = 1f;
        [Range(0f, 1f)] public float Stagger = .15f;
        public bool UseMist = true;
        public bool Grounded = true;
        public string SourcePattern;
        public string RecipeJson;
        [Header("#300 basic attack flight body (presentation; flight clock/target/damage unchanged)")]
        [Tooltip("Prefab with Travel (moves along the path; optional Spin mesh body) and Contact (bursts at the impact). Replaces the procedural body.")]
        public GameObject Bolt300Prefab;
        [Min(0f)] public float Bolt300Scale = 1f;
        [Tooltip("D308-84: seconds over which the solid body goes from ink to its pigment after leaving (0 = off; particles soak in their own material).")]
        [Min(0f)] public float Bolt300SoakSeconds;
        [Tooltip("D308-84: the hit (Contact) shrinks when the camera is nearer than this (m), down to 45 % (0 = off). Play sw_s: a 5 m hit filled a third of the view.")]
        [Min(0f)] public float Bolt300NearContactMeters;
        [Tooltip("D308-84: extra scale of the flying body only (Travel), times Bolt300Scale (0 or 1 = same as the hit).")]
        [Min(0f)] public float Bolt300TravelScale;
        [Tooltip("D308-84 stage 4: the #308 guard membrane, stood for the guard clock in place of the pattern shield (set NativeFieldPrefab to none on the same copy).")]
        public GameObject Guard308Prefab;
        [Tooltip("D308-84 stage 4: the #308 loop body of a buff / ward, played for the effect's life; it owns the effect (other children off). A ward's ring is built at 1 m and scaled by WardRadius.")]
        public GameObject Aura308Prefab;
        [Min(0f)] public float Aura308FadeSeconds = .5f;
        [Tooltip("Scale the loop body by WardRadius (wards only).")] public bool Aura308ByWardRadius;
        [Tooltip("11.41: companion letters - an effect prefab (Vfx120Effect with a Bolt300 profile) flown once per shot (Secondary cue). Empty = old behaviour.")]
        public GameObject CompanionShot308Prefab;
        [Tooltip("11.41: sword-form letters - a one-shot particle prefab put down per swing (Hit / Miss cue with a point). Empty = old behaviour.")]
        public GameObject SwordStrike308Prefab;
        [Tooltip("D308-84: BambooField bodies lean up to this many degrees each their own way (0 = the old small fixed jitter). Play sw_t: 17 equal canes read as a fence.")]
        [Range(0f, 30f)] public float AreaBambooLean;
        [Tooltip("D308-84: BambooField body heights spread +-this (0 = the old fixed set).")]
        [Range(0f, .6f)] public float AreaBambooHeightSpread;
        [Tooltip("11.29: the FlameCone seam sets only the half-angle and the emitting point on this body (a #308 fan keeps its own shape, scale and sizes). Off = the old full override.")]
        public bool AreaKeepShape308;
        [Tooltip("Peak height of the flight arc (m).")]
        public float Bolt300Arc;
        [Tooltip("Sideways bend of the flight (m) — the slow homing curve.")]
        public float Bolt300Curve;
        [Tooltip("Spin of the Spin child (deg/s).")]
        public float Bolt300Spin;
        [Tooltip("Progress exponent along the line (1 = constant speed, >1 = starts slow).")]
        [Min(0f)] public float Bolt300Ease = 1f;
    }

}
