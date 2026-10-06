using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 HUD liquid vessels + action marks (SPEC-HUD-LIQUID-308, D308-11 / D308-11b). Every number of the cluster lives here
    /// [TEST values throughout]; field initializers ARE the design values. The asset sits at Resources/UI308/HudLiquid308.asset
    /// (made by the editor command Oheangbu.EditorTools.WorldMacro.HudLiquid308 Run("hud308-setup")). HudController reads it once
    /// per HUD build: asset present and Enabled = vessels, otherwise the #304 meters (the one-cell rollback).
    /// Values NOT copied here are read from their owners at build time: colours / RimAlpha / LagAlpha / lag timings / ReducedMs
    /// (UiStyle304SO), ReceivedSeconds (HudTokens304), DangerBeginsAtHp (HUD skin), SpellInkCost (CombatConfigSO, via the presenter).
    /// Not a singleton: one immutable data asset. No static state.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/UI/UI308 HUD Liquid Profile", fileName = "HudLiquid308")]
    public sealed class HudLiquid308ProfileSO : ScriptableObject
    {
        public const string ResourcePath = "UI308/HudLiquid308";

        [Tooltip("Off = HudController builds the #304 meters instead (rollback without code)")]
        public bool Enabled = true;

        public LayoutSpec308 Layout = new LayoutSpec308();
        public GlassSpec308 Glass = new GlassSpec308();
        public LiquidLookSpec308 Liquid = new LiquidLookSpec308();
        [Tooltip("ink-in-glass touches taken over from the concept sheet (Art/UI308/HUD/hud308_mock_sheet.png); 0 = the bare Spec look")]
        public LookSpec308 Look = new LookSpec308();
        [Tooltip("HP = 주묵: light and lively. D308-11b: it moves only in answer to what the player does")] public LiquidSpec308 Hp = LiquidSpec308.HpDefaults();
        [Tooltip("먹: slow and viscous; the wet film lives as long as world ink residue (D308-10b, 3-5 s)")] public LiquidSpec308 Ink = LiquidSpec308.InkDefaults();
        public LowSpec308 Low = new LowSpec308();
        public BubbleSpec308 Bubbles = new BubbleSpec308();
        public MotionInputSpec308 Motion = new MotionInputSpec308();
        public MarkSpec308 Marks = new MarkSpec308();
        public ImpactSpec308 Impact = new ImpactSpec308();
        [Tooltip("D308-15 (SPEC-UI-THEME-308): lacquer neck band on the vessels, najeon lids for the marks. Both off = the look before the theme")]
        public ThemeSpec308 Theme = new ThemeSpec308();
        public AtlasSpec308 Atlas = new AtlasSpec308();

        [Header("references (hud308-setup)")]
        [Tooltip("M_InkVessel308 (UI/InkVessel308). Empty = plain fallback quads: the values still read")] public Material Material;
        [Tooltip("hud308_atlas.png (linear, mips, Clamp)")] public Texture2D AtlasTexture;

        void OnValidate()
        {
            // D308-10b: ink left behind lives 3-5 s, in the world and on the glass alike
            float life = Ink.WetHold + Ink.WetDry;
            if (!Ink.WetFromStyle && life > 0f && (life < 3f || life > 5f))
            {
                float k = Mathf.Clamp(life, 3f, 5f) / life;
                Ink.WetHold *= k; Ink.WetDry *= k;
            }
            Layout.ClusterScale = Mathf.Clamp(Layout.ClusterScale, .8f, 1.3f);
            Impact.LightGain = Mathf.Clamp01(Impact.LightGain);
            Impact.MinInterval = Mathf.Max(Impact.MinInterval, 1f / 3f);   // D308-10: at most 3 value flips per second
        }
    }

    [Serializable]
    public sealed class LayoutSpec308
    {
        public UiAnchor304 Anchor = UiAnchor304.BottomLeft;
        [Tooltip("scale pivot = the safe corner (mockup px)")] public Vector2 Pivot = new Vector2(64, 1016);
        [Range(.8f, 1.3f)] public float ClusterScale = 1f;
        [Tooltip("multiply by the user's UI scale setting (only this cluster follows it)")] public bool FollowUiScale = true;
        public Vector2 UiScaleClamp = new Vector2(.8f, 1.3f);
        [Tooltip("mockup px, top-left origin")] public Rect Hp = new Rect(64, 818, 114, 150);
        public Rect Ink = new Rect(158, 880, 100, 132);
        public Vector2 ArcCentre = new Vector2(167, 925);
        public float ArcRadius = 132;
        [Tooltip("degrees above the horizontal, counter-clockwise from +x")] public float DodgeDeg = 64, JumpDeg = 38, VehicleDeg = 12;
        public float MarkSize = 44;
    }

    [Serializable]
    public sealed class GlassSpec308
    {
        public float OutlinePx = 2.6f, PaperRimPx = 2.2f;
        [Range(0f, 1f)] public float OutlineAlpha = .95f, EmptyWash = .14f, HighlightAlpha = .55f;
        [Tooltip("glass wall thickness: the cavity is this far inside the silhouette (the atlas level lines assume 4)")] public float WallPx = 4f;
    }

    [Serializable]
    public sealed class LiquidLookSpec308
    {
        public float PoolEdgePx = 5f;
        [Range(0f, 1f)] public float PoolEdgeDark = .22f, CoreLighten = .06f;
        public float MeniscusPx = 1.1f;
        [Range(0f, 1f)] public float MeniscusAlpha = .78f;
        public float MeniscusClimbPx = 2f, MeniscusClimbRangePx = 6f;
        [Range(0f, 1f)] public float LiquidAlpha = .96f;
        [Tooltip("the freshly received share is this much lighter (담묵) for ReceivedSeconds")] [Range(0f, 1f)] public float FreshLighten = .18f;
        public float PourThreadPx = 2f;
        [Tooltip("seconds for the pour thread to appear / go")] public float PourFadeSeconds = .08f;
        [Tooltip("cost line: dash px, gap px, line px, alpha")] public Vector4 CostLine = new Vector4(6f, 4f, 1.2f, .9f);
    }

    /// <summary>What makes the flat Spec vessel read as ink in glass (concept sheet, review 2026-10-04). All are VALUES of ink and
    /// paper: no gloss gradient, no emission. Every one can be set to 0 to get the bare Spec drawing back.</summary>
    [Serializable]
    public sealed class LookSpec308
    {
        [Tooltip("paper value in the gap between the ink line and the liquid = the thickness of the glass (keeps ink off the ink line on dark ground)")]
        [Range(0f, 1f)] public float WallAlpha = .34f;
        [Tooltip("the body is this much darker at the floor than at the surface (thick ink settles); a value step, not a gloss")]
        [Range(0f, .4f)] public float DepthDarken = .12f;
        [Tooltip("brush pressure: the ink line swells towards the lower left and thins towards the upper right by this share")]
        [Range(0f, .5f)] public float OutlinePressure = .28f;
        [Tooltip("the far half of the surface seen through the glass: a thin paler lens above the value line (design px, HP / ink)")]
        public float LensHpPx = 4.2f, LensInkPx = 3.6f;
        [Tooltip("how much paper the lens mixes into the liquid colour")] [Range(0f, 1f)] public float LensPaperMix = .29f;
        [Tooltip("paper line along the far edge of the lens")] [Range(0f, 1f)] public float LensBackAlpha = .34f;
        [Tooltip("the lens reaches its full height this far from the glass (design px); it thins out near the wall and the floor")]
        public float LensRangePx = 24f;
    }

    [Serializable]
    public sealed class LiquidSpec308
    {
        [Header("slosh (damped spring about the level surface, SPEC §2.3; shoved by player actions only, D308-11b)")]
        public float SpringHz;
        public float Damping;
        [Tooltip("peak surface slope (height / width) a SUDDEN sideways speed change of 1 m/s makes (a sideways dodge is 12 m/s). " +
                 "A change that builds up slowly tilts less: the spring swings back meanwhile")] public float TiltPerMps;
        [Tooltip("reference cap of the slope; the hard cap is this x Motion.TiltOvershootLimit")] public float MaxTilt;
        public float WaveMaxPx, WaveTau, WaveSpeed;
        [Tooltip("ripple px per second gained per unit of tilt speed (slosh lifts ripples)")] public float WaveExcite;
        [Tooltip("ripple px per m/s of forward speed change (starting, stopping, a forward dodge)")] public float WavePerMps;
        [Tooltip("ripple px per m/s of take-off speed (jump) and of fall speed at the landing")] public float HeavePerMps;

        [Header("level (SPEC §2.4)")]
        public float DrainTau;
        public float DrainMinSpeed = .6f, FillTau;
        [Tooltip("HP: read the wet film from UiStyle304 (Meter.LagAlpha, Motion.MeterLagHoldMs / MeterLagDrainMs)")] public bool WetFromStyle;
        [Range(0f, 1f)] public float WetAlpha;
        public float WetHold, WetDry;
        [Tooltip("a single rise larger than this pours: thread + ripple + fresh share (natural regeneration stays quiet)")] public float PourMinDelta = .01f;

        [Header("kicks")]
        [Tooltip("tilt speed per impact frame (x max(light, KickFloor)), away from the impact point")] public float KickTilt;
        [Tooltip("ripple px of a full-strength jolt: an impact frame, KickInk(1), a value change of Motion.LevelFullDelta")] public float KickWavePx;
        [Tooltip("tilt speed when the value DROPS by Motion.LevelFullDelta at once (a hit taken; alternating sides). 0 for ink: spending it is the player's own doing")]
        public float DropKickTilt;

        public static LiquidSpec308 HpDefaults() => new LiquidSpec308
        {
            SpringHz = 1.8f, Damping = .28f, TiltPerMps = .014f, MaxTilt = .22f,
            WaveMaxPx = 3f, WaveTau = .55f, WaveSpeed = 7f, WaveExcite = 4f, WavePerMps = .30f, HeavePerMps = .40f,
            DrainTau = .07f, FillTau = .22f, WetFromStyle = true, WetAlpha = .34f, WetHold = .45f, WetDry = .35f,
            KickTilt = 2.4f, KickWavePx = 2f, DropKickTilt = 1.6f,
        };

        public static LiquidSpec308 InkDefaults() => new LiquidSpec308
        {
            SpringHz = 1.1f, Damping = .55f, TiltPerMps = .010f, MaxTilt = .16f,
            WaveMaxPx = 1.8f, WaveTau = .40f, WaveSpeed = 4f, WaveExcite = 2f, WavePerMps = .18f, HeavePerMps = .24f,
            DrainTau = .11f, FillTau = .30f, WetFromStyle = false, WetAlpha = .30f, WetHold = .6f, WetDry = 3.4f,
            KickTilt = 1.5f, KickWavePx = 1.2f, DropKickTilt = 0f,
        };

        /// <summary>The runtime parameters of this liquid; the style supplies the HP wet film and the reduced-motion length.</summary>
        public LiquidParams308 Resolve(HudLiquid308ProfileSO profile, UiStyle304SO style, float freshSeconds)
        {
            var m = profile.Motion;
            bool fromStyle = WetFromStyle && style != null;
            return new LiquidParams308
            {
                SpringHz = SpringHz, Damping = Damping, MaxTilt = MaxTilt,
                TiltLimit = MaxTilt * Mathf.Max(1f, m.TiltOvershootLimit),
                // TiltPerMps is the first PEAK a sudden 1 m/s makes; the kick that reaches it is peak x w / (first-peak share)
                TiltKickPerMps = TiltPerMps * 6.28318530718f * SpringHz / LiquidSim308.FirstPeakShare(Damping),
                WaveMaxPx = WaveMaxPx, WaveTau = WaveTau, WaveSpeed = WaveSpeed, WaveExcite = WaveExcite,
                WavePerMps = WavePerMps, HeavePerMps = HeavePerMps,
                DrainTau = DrainTau, DrainMinSpeed = DrainMinSpeed, FillTau = FillTau,
                WetAlpha = fromStyle ? style.Meter.LagAlpha : WetAlpha,
                WetHold = fromStyle ? style.Motion.MeterLagHoldMs / 1000f : WetHold,
                WetDry = fromStyle ? style.Motion.MeterLagDrainMs / 1000f : WetDry,
                FreshSeconds = freshSeconds, PourMinDelta = PourMinDelta, PourFadeSeconds = profile.Liquid.PourFadeSeconds,
                KickTilt = KickTilt, KickWavePx = KickWavePx, DropKickTilt = DropKickTilt, LevelFullDelta = m.LevelFullDelta,
                SimStep = m.SimStep, RestTilt = m.RestTilt, RestTiltSpeed = m.RestTiltSpeed, RestWavePx = m.RestWavePx, RestHold = m.RestHold,
                LevelSnap = m.LevelSnap,
                ReducedSeconds = (style != null ? style.Motion.ReducedMs : 120) / 1000f,
            };
        }
    }

    [Serializable]
    public sealed class LowSpec308
    {
        [Tooltip("HP at 0 mixes this much ink into the liquid (0 at the danger threshold)")] [Range(0f, 1f)] public float HpDarken = .22f;
        [Tooltip("dried rings above a low HP level (0-3); fixed levels: threshold x 1, .72, .44")] [Range(0, 3)] public int TideMarks = 3;
        [Range(0f, 1f)] public float TideAlpha = .22f;
        // D308-11b: no tremble. A low state is told by VALUE and MARK (the low level itself, the darker colour, the tide rings,
        // the dry-brush ink body, the screen-edge ink), never by a motion that runs while the player stands still.
        [Tooltip("streak threshold: ink below one spell cost keeps 1 - this of its body (dry brush)")] [Range(0f, 1f)] public float InkDryStreak = .45f;
        [Tooltip("ink band below the threshold over which the body splits (no pop)")] public float InkDryBand = .02f;
        [Tooltip("used only when no threshold was handed over (CombatConfigSO.SpellInkCost)")] [Range(0f, 1f)] public float LowInkFallback = .15f;
        [Tooltip("used only when the HUD has no skin (WorldMacroHudSkinProfileSO.DangerBeginsAtHp)")] [Range(0f, 1f)] public float LowHpFallback = .35f;
    }

    /// <summary>Optional bubbles while pouring. Data only: NOT drawn in this build (default off, SPEC §2.1-7).</summary>
    [Serializable]
    public sealed class BubbleSpec308
    {
        public bool Enabled = false;
        public int Max = 3;
        public float RiseSeconds = .5f;
    }

    /// <summary>D308-11b: the liquid is shoved by what the PLAYER really did, measured (ActionMeter308): the body's speed
    /// changes, fast view turning, take-off and landing. The motion of the camera is no input and nothing runs on its own.</summary>
    [Serializable]
    public sealed class MotionInputSpec308
    {
        [Tooltip("m/s: a body slower than this stands still (its speed reads 0, so contact jitter is no action)")] public float SpeedDeadzone = .12f;
        [Tooltip("m/s: the largest speed change taken from one frame (a dodge is 12)")] public float StepClamp = 14f;
        [Tooltip("m/s: a speed change is handed to the liquid once it has grown this far since the last one handed on (or the body has stopped). " +
                 "The speed noise of a steady walk stays under it (at most .06 m/s at 240 fps on the far side of the map), so walking at one speed is calm")]
        public float StepBand = .2f;
        [Tooltip("deg/s: only view turning FASTER than this counts (looking around and the lock-on pull stay under it)")] public float TurnDeadzone = 150f;
        [Tooltip("deg/s")] public float TurnClamp = 540f;
        [Tooltip("smoothing of the measured turn rate (mouse deltas arrive unevenly)")] public float TurnLowPassHz = 10f;
        [Tooltip("m/s of sideways speed that 180 deg/s of turning ABOVE the dead zone counts as (the vessel swings round with the body)")]
        public float TurnMpsPer180 = 3f;
        [Tooltip("a frame that moves the body further than this is dropped (respawn, placement)")] public float TeleportMetres = 5f;
        [Tooltip("a single value change of this size (share of the vessel) is a full-strength stir: KickWavePx, DropKickTilt")]
        [Range(.05f, 1f)] public float LevelFullDelta = .25f;
        public float SimStep = 1f / 240f, MaxDt = .05f;
        [Tooltip("under these the surface is put to rest: exactly the flat line (a slope of .003 is .15 px at the HP glass, a ripple of .2 px is not seen)")]
        public float RestTilt = .003f, RestTiltSpeed = .01f, RestWavePx = .2f;
        [Tooltip("seconds after the last shove before the surface may be put to rest: a speed that builds up over many frames (a straight walk, " +
                 "a car pulling away) arrives as shoves smaller than the thresholds above, and they must add up")]
        public float RestHold = .25f;
        [Tooltip("hard cap of the slope as a multiple of MaxTilt (MaxTilt caps the TARGET; the spring may overshoot it up to here)")]
        [Range(1f, 2f)] public float TiltOvershootLimit = 1.5f;
        [Tooltip("a rising level closer than this to the value lands on it (then the liquid can rest)")] public float LevelSnap = .0005f;
        [Tooltip("-1: speeding up / turning to the right piles the liquid on the left (tuned by eye in Play)")] public float TiltSign = -1f;
        [Tooltip("the liquid stands still while Time.timeScale is 0 (menus, tutorial pause); it runs on unscaled time otherwise")]
        public bool FreezeWhenPaused = true;
    }

    [Serializable]
    public sealed class MarkSpec308
    {
        [Range(0f, 1f)] public float DryAlpha = .30f, RimAlpha = .92f;
        [Tooltip("soft edge of the re-wetting front (share of the stroke order)")] public float RewetEdge = .04f;
        public int RewetMs = 120, DryMs = 90;
        [Tooltip("the vehicle state and the key bindings are read this often (boss-field polygons are not tested every frame)")] public float VehiclePollSeconds = .2f;

        [Header("key glyph (D308-11b): the key that triggers the mark, as ONE symbol cell on a lacquer keycap")]
        [Tooltip("each mark carries the key of its action (part of the mark, drawn by the mark's own graphic; never a sentence)")] public bool ShowKeys = true;
        [Tooltip("keycap square, design px")] public float KeySize = 18f;
        [Tooltip("keycap centre from the lid centre, design px, y down: the lid's lower-left edge (30 px out, 30 deg left of straight down: " +
                 "the keycap bites the lid's rim line and stays off its pictogram). The keycap is then put on whole px (at most half a px from this)")]
        public Vector2 KeyOffset = new Vector2(-15f, 26f);
        [Tooltip("paper hairline inside the keycap edge: px, alpha")] public float KeyRimPx = 1f;
        [Range(0f, 1f)] public float KeyRimAlpha = .8f;
        [Tooltip("the keycap's lip along its lower edge: px, alpha (DESIGN 5.8 hollow keycap)")] public float KeyLipPx = 2f;
        [Range(0f, 1f)] public float KeyLipAlpha = .28f;
        [Tooltip("the symbol sits this many px above the keycap centre (above the lip)")] public float KeyGlyphLiftPx = 1f;
        [Tooltip("the symbol of a mark that cannot be used now (the hairline and the lacquer stay)")] [Range(0f, 1f)] public float KeyDryAlpha = .42f;
        [Tooltip("the vehicle call is not an Input System action yet (WorldMacroPalanquinSummon polls a key). If the Gameplay map gets an action of this name, its binding wins")]
        public string VehicleAction = "Vehicle";
        [Tooltip("control path of the key the vehicle summon polls, used while that action does not exist")]
        public string VehicleKeyPath = "<Keyboard>/g";
        [Tooltip("control path -> cell of the key block of the atlas. A wide key is ONE symbol (no word); a keyboard key named by one letter or digit needs no row")]
        public KeyGlyphRow308[] KeyGlyphs = HudKeyGlyph308.DefaultTable();
    }

    [Serializable]
    public sealed class ImpactSpec308
    {
        [Tooltip("light reaction (lit side paper, far side ink) for the impact cells")] public bool Enabled = true;
        [Tooltip("the liquid is kicked once when an impact frame begins")] public bool KickOnImpact = true;
        [Range(0f, 1f)] public float LightGain = .85f;
        public float ShadowPx = 9f;
        [Range(0f, 1f)] public float ShadowAlpha = .5f, LiquidShift = .10f, FarRimInk = .6f;
        [Tooltip("a global that stays non-zero longer than this is treated as stuck: no reaction until it reads 0 again. " +
                 "SPEC-SPELL-DEPLOY-308 §8: one cell = 42 ms, at most 3 cells (126 ms)")] public float StuckSeconds = .25f;
        [Tooltip("minimum seconds between two light reactions (>= 1/3: D308-10 allows 3 value flips per second)")] public float MinInterval = .33f;
        [Range(0f, 1f)] public float KickFloor = .5f;
        [Tooltip("SPEC-SPELL-DEPLOY-308: _OhImpactHud308.x (0 = HUD reaction switched off) scales the light. Off = use the impact light alone")]
        public bool FollowHudGlobal = true;
    }

    /// <summary>D308-15 theme on the cluster (SPEC-UI-THEME-308 §3 / §5.3, default answers 1, 2, 5, 6). The vessels wear a lacquer
    /// neck band with one cut-shell line (the lip stays glass: no stopper; the line runs along the band's lower edge, between
    /// the lacquer and the ink); the marks are najeon lids (lacquer disc, cut-shell rim line, the pictogram in shell pieces).
    /// The SHAPES are baked in the atlas (G of the vessel cell, the four lid cells); these are the values the shader colours
    /// them with. Nothing here moves or glows: the shell is a still LDR colour (shimmer off), never above the paper value.
    /// TEMPORARY HOME (Spec, Temporary Exceptions): the kit has no UiTheme308SO yet, so this block carries a copy of the kit's
    /// token values (theme308_tokens.json: Lacquer, nacre.shader_fit, families, shares, arc). When that asset exists,
    /// hud308-setup reads it instead and only the two switches stay here.</summary>
    [Serializable]
    public sealed class ThemeSpec308
    {
        [Tooltip("the vessels' lacquer neck band + cut-shell line (off = the bare glass neck)")] public bool Collar = true;
        [Tooltip("the marks as najeon lids (off = ink pictogram + paper rim, the look before the theme: default answer 2's alternative)")]
        public bool Lids = true;
        [Tooltip("theme308_tokens colours.Lacquer")] public Color Lacquer = new Color32(0x11, 0x0F, 0x0D, 0xFF);
        [Tooltip("nacre colour = N0 + A cos h + B sin h in sRGB code values (theme308_tokens nacre.shader_fit)")]
        public Vector3 NacreN0 = new Vector3(.7254f, .7273f, .7280f);
        public Vector3 NacreA = new Vector3(.0845f, -.0314f, -.0034f), NacreB = new Vector3(.0322f, .0003f, -.0900f);
        [Tooltip("hue of the green / blue / pink piece families (degrees)")] public Vector3 NacreFamilyDeg = new Vector3(170f, 245f, 320f);
        [Tooltip("share of green and of blue cut pieces; the rest is pink, the rare flash")] public Vector2 NacreFamilyShare = new Vector2(.42f, .43f);
        [Tooltip("hue swing inside one piece (+- degrees) and its period along the piece (px)")] public float NacreSwingDeg = 28f, NacreHuePeriodPx = 30f;
        [Tooltip("the hue never leaves this arc (degrees): green - blue - pink, no yellow")] public Vector2 NacreArcDeg = new Vector2(150f, 340f);
        [Tooltip("a lid whose action cannot be used now: the pictogram's shell at this alpha (its seat shows). The rim line stays")]
        [Range(0f, 1f)] public float LidDryAlpha = .30f;
    }

    /// <summary>Geometry of hud308_atlas.png (Tools/Art/hud308_assets.py -> hud308_atlas.json). hud308-setup copies the json here
    /// and into the material; the defaults equal the generator's output.</summary>
    [Serializable]
    public sealed class AtlasSpec308
    {
        [Tooltip("uv x, y, w, h")] public Vector4 Vessel = new Vector4(0.015625f, 0.197266f, 0.297852f, 0.771484f);
        public Vector4 Dodge = new Vector4(0.335938f, 0.5625f, 0.203125f, 0.40625f);
        public Vector4 Jump = new Vector4(0.546875f, 0.5625f, 0.203125f, 0.40625f);
        public Vector4 Vehicle = new Vector4(0.757812f, 0.5625f, 0.203125f, 0.40625f);
        public Vector4 VehicleOut = new Vector4(0.335938f, 0.125f, 0.203125f, 0.40625f);
        [Tooltip("vessel quad in design px: width, height (the 114 x 150 rect + margin on every side)")] public Vector2 QuadRef = new Vector2(122, 158);
        public float MarginPx = 4, SdfRangePx = 16;
        [Tooltip("quad uv y of the inner floor (value 0) and of the full line (value 1)")] public float FloorUv = .06013f, FullUv = .82943f;
        public float NeckTopUv = .96835f, WavelengthPx = 102.6f;
        [Tooltip("the design rect the atlas was drawn for")] public Vector2 RefRect = new Vector2(114, 150);
        [Tooltip("a mark cell = the mark rect (MarkRefPx) + this margin on every side: the mark quad is drawn that much larger so the paper rim is not cut")]
        public float MarkMarginPx = 4, MarkRefPx = 44;
        [Header("theme (D308-15)")]
        [Tooltip("najeon lid cells: uv x, y, w, h (rgb = shell colour as sRGB code values, a = shell coverage)")]
        public Vector4 DodgeLid = new Vector4(0.548828f, 0.269531f, 0.128906f, 0.257812f);
        public Vector4 JumpLid = new Vector4(0.681641f, 0.269531f, 0.128906f, 0.257812f);
        public Vector4 VehicleLid = new Vector4(0.548828f, 0.003906f, 0.128906f, 0.257812f);
        public Vector4 VehicleOutLid = new Vector4(0.681641f, 0.003906f, 0.128906f, 0.257812f);
        [Tooltip("lid: radius px, the px of the mark quad one lid cell covers, inner radius of the rim line (beyond it nothing dims)")]
        public float LidRadiusPx = 22f, LidCellPx = 44f, LidRimInnerPx = 18.5f;
        [Tooltip("collar line baked in the vessel cell's G: half length px and length of one cut piece px (the shader's hue per piece)")]
        public float CollarHalfPx = 16.88f, CollarPiecePx = 6.752f;
        [Header("key glyphs (D308-11b)")]
        [Tooltip("key block: uv x, y of cell 0 (the top-left cell), pitch u, pitch v (rows run down)")]
        public Vector4 KeyGrid = new Vector4(0.819336f, 0.466797f, 0.029297f, 0.058594f);
        [Tooltip("one key cell: uv w, h")] public Vector2 KeyCell = new Vector2(0.027344f, 0.054688f);
        [Tooltip("columns of the key block; design px one cell covers (the symbol box)")] public float KeyColumns = 6f, KeyBoxPx = 14f;
    }
}
