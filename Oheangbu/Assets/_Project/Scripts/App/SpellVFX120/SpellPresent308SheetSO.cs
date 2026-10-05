using System;
using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // #308 present add-on (SPEC-SPELL-120-308 section 9 seam, drawn by the SPEC-SPELL-DEPLOY-308 layer; D308-10c / D308-13b).
    // This sheet is the DATA of the spell presenter on the deploy layer: which presenter call (role + Begin / cue / End)
    // becomes which composition of the layer's ink, and every number those compositions use. No glyph is named here; a
    // glyph is switched to this presenter by the layer's existing data switch (SpellDeploy308ProfileSO.EnabledLetters).
    // Without an asset (Resources/Deploy308/SpellPresent308Sheet) the code defaults below are used as they are.
    // Presentation only: nothing in here is read by a rule.

    public enum PresentMoment308 { Begin, Cue, End }

    /// <summary>What one route does. Each act is a composition of what the deploy layer draws; none of them makes light that
    /// lasts (the glow of a stroke is its birth cels, ground marks and air drops have none).</summary>
    public enum PresentAct308
    {
        Silent,              // deliberately nothing: Why says what shows the moment instead
        Cast,                // one deploy burst of the glyph's map row (the category's own three-beat form)
        Stand,               // Cast as a standing ink wall (the ward form) for the life the rule asked: ink, no glow once it has risen
        Trigger,             // the triggered burst of an installed mark, at the cue's target (impact frames only on a groggy target)
        HeldRing,            // ground marks at the request's place and radius: wet while the handle lives, dry afterwards
        HeldRingAtCue,       // the same at the cue's point with the cue's value as the radius
        Release,             // the handle's ground marks start to dry
        FlingToTarget,       // ink thrown from the request's origin to its target over the impact clock (a mark in flight)
        FlingToCueTargets,   // ink thrown from the cue's point to every enemy the cue names (crescents, droplets, a bounce, an ember)
        FlingToCaster,       // ink thrown from the cue's point back to the caster
        FlingFromCaster,     // ink thrown from the caster to the cue's point (a companion shot)
        MarkCarried,         // an ink mark the request's target carries while the cast lives on
        MarkCueCarrier,      // the same on the enemy the cue names (nothing when it names none, or when the route is for lasting casts and this one is not)
        MarkInstall,         // the installed mark on the cue's target
        Unmark,              // the handle's carried mark melts
        Splash,              // the hit splash at the cue's point, where no enemy hit is confirmed (a projectile shot down, a gate opening)
        Shed,                // the end cue of something the caster carried: ink falls off in front of the caster
        ShedSpent,           // the same with a needle star: it was used up
        Drip,                // a small mark at the caster's feet (something came back)
        Blade,               // sword form entry: the stroke becomes the blade
        Slash,               // one sword strike through the air
        SlashHit,            // one sword strike that landed
    }

    /// <summary>Which category form a Cast act takes.</summary>
    public enum PresentCastAs308 { Map, Always, WhenNotAttack }

    // (ZoneRing: forms3 D9 - appended, so the numbers of the older members stay what they were)
    public enum PresentBody308 { Slash, Blade, MarkState, MarkInstall, MarkCarried, ZoneRing }

    /// <summary>What a spell left on an enemy (read from the enemy's own control / modifier state, never written).</summary>
    [Flags]
    public enum PresentState308 { None = 0, Bound = 1, Slowed = 2, Exposed = 4, Weakened = 8 }

    /// <summary>Where a confirmed hit came from (the presenter's own reading of the damage source).</summary>
    public enum PresentHitSource308 { Direct, Companion, Retaliation, Harmony, Persistent, Other }

    [Serializable]
    public sealed class PresentRoute308
    {
        public SpellFxRole Role;
        public PresentMoment308 Moment;
        [Tooltip("Read only when Moment = Cue.")]
        public SpellFxCue Cue;
        public PresentAct308[] Acts = Array.Empty<PresentAct308>();
        [Tooltip("Which category form a Cast / Stand act takes: the glyph's own map row, always Category, or Category only for a glyph that is not an attack row.")]
        public PresentCastAs308 CastAs;
        public DeployCategory308 Category;
        [Tooltip("The route's mark acts (MarkCarried, MarkCueCarrier) are for a cast that asked to outlive its impact (an ember, a shot that returns).")]
        public bool OnlyIfLasting;
        [Tooltip("Nothing follows this cue: the handle's marks are released and the handle is forgotten.")]
        public bool Ends;
        [Tooltip("Why the route is what it is (a Silent route says what shows the moment instead).")]
        public string Why = "";

        public PresentRoute308() { }
        public PresentRoute308(SpellFxRole role, PresentMoment308 moment, SpellFxCue cue, string why, params PresentAct308[] acts)
        { Role = role; Moment = moment; Cue = cue; Why = why; Acts = acts ?? Array.Empty<PresentAct308>(); }
        public PresentRoute308 As(PresentCastAs308 mode, DeployCategory308 category) { CastAs = mode; Category = category; return this; }
        public PresentRoute308 Lasting() { OnlyIfLasting = true; return this; }
        public PresentRoute308 End() { Ends = true; return this; }
        public bool Has(PresentAct308 act) { if (Acts != null) foreach (var a in Acts) if (a == act) return true; return false; }
    }

    [CreateAssetMenu(menuName = "Oheangbu/Spell/Present 308 sheet TEST", fileName = "SpellPresent308Sheet")]
    public sealed class SpellPresent308SheetSO : ScriptableObject
    {
        public const string ResourcesPath = "Deploy308/SpellPresent308Sheet";
        // Code ceilings (the accessors below never answer more, whatever the data says).
        public const float MaxAirSeconds = 2f;            // an airborne sprite: the layer's own falling drop lives 1.6 s
        public const float MaxBodySeconds = 1.5f;         // a body that is not carried by anything (slash, blade): birth + hold + melt
        public const float MaxMarkSecondsCeiling = 120f;  // a carried mark (ink, no glow) is taken off at the latest after this
        public const float MaxStandSecondsCeiling = 120f; // a standing ink wall (the longest cover life a rule sheet may ask)
        public const int MaxRingStamps = 24, MaxFlingDrops = 6, MaxOps = 96;

        [Serializable]
        public sealed class FlingSet
        {
            [Range(1, 6)] public int Drops = 4;
            [Min(.02f)] public float Size = .26f;
            [Tooltip("m/s when the cue carries no arrival time.")]
            [Min(1f)] public float Speed = 14f;
            [Min(.04f)] public float MinSeconds = .12f;
            [Range(0f, 1f)] public float Gravity = .35f;
            [Tooltip("m between the drops of one fling at its start.")]
            [Min(0f)] public float Spacing = .8f;   // fix pass 4b: .42 m lay inside the head's own drawn tail (head and follower fused into one tadpole); fix pass: the drops follow each other IN LINE, apart (at .14 m they were one dash)
            [Tooltip("fix pass 4b (director 4): the followers start this far (m) over and under the head's line, turn about (three drops at one height were a dotted line across the view); FollowRound = the drops after the tailed ones are round drops (off = spatter, as before).")]
            [Min(0f)] public float Scatter = .14f;   // pass 4b (Q6): .14 (was .08) - the followers fly at clearly different heights
            [Tooltip("fix pass 4b: seconds between two drops of one fling at most (a slow throw: the wider spacing must not stretch the train past the air ceiling).")]
            [Range(.02f, .6f)] public float StepMax = .15f;
            public bool FollowRound = true;
            [Tooltip("pass 4b (main decision Q6): the thrown HEAD is drawn at most this many sizes long (the residue field's tailed drop may be 5 sizes long at speed - a long streak, a dash); 0 = the field's own ceiling. Only the thrown head takes it: every other tailed drop is drawn as it was. ScatterSteps = each follower's height off the head's line as a share of Scatter (a written order: over, under, far over - three clearly different heights).")]
            [Range(0f, 6f)] public float HeadTailMax = 2.5f;
            public float[] ScatterSteps = { 1f, -.75f, .35f, -.3f, .7f };
            [Tooltip("forms3 (D10): how many of a fling's drops, counted from the first, are the tailed drop (the residue field draws that cell along the way it flies: head first, tail behind). The rest are spatter (the cluster cell). 0 = round drops as before. Each later drop is smaller by SizeStep of the first (was a code constant).")]
            [Range(0, 6)] public int Tailed = 1;   // fix pass 4b: only the head is the tailed drop (four tailed drops in a row were four dashes)
            [Range(0f, .6f)] public float SizeStep = .4f;   // forms4 (P8): one large head and small followers (three drops of nearly one size lay over each other as a peanut)
            [Range(1, 8)] public int TargetsMax = 6;
            [Tooltip("m a derived shot that found nobody flies before it is gone.")]
            [Min(.5f)] public float AirDistance = 2.5f;
        }

        [Serializable]
        public sealed class RingSet
        {
            [Tooltip("m between two marks on the ring.")]
            [Min(.2f)] public float Spacing = .9f;
            [Range(3, 24)] public int CountMin = 6, CountMax = 14;
            [Tooltip("m; never larger than the residue field's piece size (one mark = one slot).")]
            [Min(.05f)] public float Drop = .3f, Centre = .34f;
            [Tooltip("m; the smallest radius a ring is laid with.")]
            [Min(.2f)] public float MinRadius = .6f;
            [Tooltip("Held ground marks the presenter keeps wet at once; beyond it the oldest handle's marks start to dry.")]
            [Range(8, 128)] public int HeldBudget = 72;
            [Tooltip("The budget is never more than this share of the residue field's spell ring on the running tier (PC 160 marks, Mobile 64): a held mark is never pushed out, so what the zones hold is taken from every other cast.")]
            [Range(.1f, .8f)] public float HeldShare = .45f;
            [Tooltip("Ring marks on the Mobile tier as a share of the PC count (its spell ring is much smaller).")]
            [Range(.25f, 1f)] public float MobileCount = .5f;
            [Tooltip("forms3 (D9): the zone's edge is one ring stroke lying on the ground (a mesh body of the burst material) made of broken dry-brush arcs, each drawn out of a dry tail and ending in a pressed head; it stays while the zone lives and melts when the handle lets go. Off = the ground marks alone, as before.")]
            public bool Body = true;
            [Tooltip("forms3 fix pass (review S1): ring strokes alive at once at most (each takes one of the Pool.Bodies body slots, shared with slashes, blades and marks; never more than half of them). The oldest ring melts to make room.")]
            [Range(1, 8)] public int BodiesMax = 4;
            [Tooltip("Arcs of the ring on PC / on the Mobile tier, and the share of its sector each arc covers (the rest is the break between two arcs).")]
            [Range(3, 4)] public int Arcs = 4;
            [Range(3, 4)] public int ArcsMobile = 4;
            [Tooltip("forms4 (P5): the arcs follow the ring's length - one every ArcEvery m of it, never fewer than Arcs / ArcsMobile, never more than ArcsMax / ArcsMaxMobile (the body's vertex room). A large ring of four arcs was four long thin lines: from inside it, one line across the view.")]
            [Min(1f)] public float ArcEvery = 3.5f;
            [Range(4, 8)] public int ArcsMax = 8;
            [Range(4, 8)] public int ArcsMaxMobile = 6;
            [Tooltip("forms4 (P5): rings of an arc follow its sweep - one every SegmentDeg degrees, at least SegmentsMin, at most Segments / SegmentsMobile; rings of its pressed head on the Mobile tier (PC: Head.Rings).")]
            [Range(4f, 20f)] public float SegmentDeg = 9f;
            [Range(4f, 20f)] public float SegmentDegMobile = 14f;
            [Range(3, 8)] public int SegmentsMin = 4;
            [Range(2, 6)] public int HeadRingsMobile = 3;
            [Tooltip("forms4 (P5): a ring of more than ManyArcs arcs gives each head HeadRingsMany rings (the vertex room); strands on one ring at most (PC / Mobile).")]
            [Range(3, 8)] public int ManyArcs = 5;
            [Range(2, 6)] public int HeadRingsMany = 2;
            [Range(1f, 2f)] public float ManySegmentMul = 1.5f;
            [Range(3, 8)] public int ManyArcsMobile = 4;
            [Tooltip("pass 4b (main decision Q4): seen from inside, a ring on the ground is a level band - VERTICAL brush breaks the line. Every arc carries 5 .. 8 dry strands STANDING on it: StrandCounts (Mobile: StrandCountsMobile) = how many, arc after arc (read round and round); on a ring of more than ManyArcs (ManyArcsMobile) arcs every arc carries StrandsLeast (the body's vertex room). StrandHighs = each strand's top between StrandMin and StrandMax m over the ground (a written, uneven order), StrandGaps = the spacing along the arc (unequal), StrandHalf = half width (m), StrandLeanDeg = how far they lean the way the arc runs, StrandAlong = the share of the arc they stand along.")]
            public float[] StrandCounts = { 6f, 8f, 5f, 7f };
            public float[] StrandCountsMobile = { 5f, 6f, 5f, 6f };
            [Range(0, 8)] public int StrandsLeast = 5;
            public float[] StrandHighs = { .55f, 1f, .2f, .75f, 0f, .9f, .4f, .65f, .1f, .85f, .3f };
            public float[] StrandGaps = { 1f, .45f, 1.5f, .7f, 1.25f, .55f, 1.6f, .85f };
            [Min(0f)] public float StrandMin = .35f, StrandMax = .7f;
            [Min(.01f)] public float StrandHalf = .17f;
            [Range(0f, 45f)] public float StrandLeanDeg = 45f;   // pass 4c: 45 x StrandLeanLeast .34 .. 1 = 15 .. 45 degrees (one lean of 30 made every bundle a comb of parallel hairs)
            [Tooltip("fix pass 4b (review N1, were code constants): a strand stands StrandPlaceMin .. StrandPlaceMax of the way through its own share of the arc, leans StrandLeanLeast .. 1 of StrandLeanDeg, is turned about its upright by StrandTurnStep degrees more than the strand before it (+- StrandTurnJitter), is StrandWidthMin .. StrandWidthMax of StrandHalf wide and stands up to StrandAcross half widths of the stroke off its middle.")]
            [Range(0f, 1f)] public float StrandPlaceMin = 0f, StrandPlaceMax = .2f;
            [Range(0f, 1f)] public float StrandLeanLeast = .34f;
            [Range(0f, 180f)] public float StrandTurnStep = 60f;
            [Range(0f, 90f)] public float StrandTurnJitter = 20f;
            [Range(.2f, 2f)] public float StrandWidthMin = .7f, StrandWidthMax = 1.2f;
            [Range(0f, 1f)] public float StrandAcross = .5f;
            [Range(.05f, 1f)] public float StrandAlong = .94f;
            [Tooltip("pass 4b (Q4): the arcs are of unequal length - ArcShares = the share of its sector each arc covers, arc after arc (read round and round; the rest is the break after it: the breaks differ); ArcShare = the share of an arc the order does not name. No arc is longer than ArcMaxLength m on a ring of any size (a large zone is short strokes with wide breaks); ArcPlaces = where such a shortened arc stands in its sector (0 = at its start, 1 = at its end). A ring whose sectors are shorter than ArcUnevenFrom m keeps the even ArcShare (a shorter arc there is a comma).")]
            public float[] ArcShares = { .98f, .84f, .93f, .8f, .9f };
            [Range(1f, 4f)] public float ArcMaxLength = 3.95f;
            [Min(0f)] public float ArcUnevenFrom = 1.6f;
            public float[] ArcPlaces = { .1f, .75f, .3f, .85f, .5f };
            [Range(.4f, 1.05f)] public float ArcShare = .97f;
            [Tooltip("pass 4c (rule review F1): an arc's wet body is at least ArcWidths times as long as the arc is wide - the half width (HalfShare of the radius) is cut for an arc too short for it (a shorter, equally wide arc is a comma).")]
            [Range(4f, 8f)] public float ArcWidths = 4.6f;
            [Tooltip("An arc never sweeps more than this (deg): its spine is one parabola, which leaves a circle by about 1 % of the radius at 90 deg and quickly more beyond it (three arcs of 120 deg would be 3 % off).")]
            [Range(40f, 100f)] public float ArcMaxDeg = 88f;
            [Tooltip("Each arc's length varies by this share (+-); the ring's start angle is the seed's.")]
            [Range(0f, .3f)] public float ArcJitter = .03f;
            [Tooltip("Half width of the ring stroke: HalfShare of the radius, kept between HalfMin and HalfMax (m).")]
            [Range(.01f, .2f)] public float HalfShare = .085f;
            [Min(.02f)] public float HalfMin = .07f, HalfMax = .32f;   // pass 4b (Q4): .32 (was .4) - an arc is at most 4 m long and must stay 4 widths long (no comma);   // forms4 (P5): HalfMin stays .07 (the director asked .12): on the least ring (.6 m) a wider arc is under 4 widths long - a comma (F16)
            [Tooltip("An arc: share of its length that is the dry tail, share of its wet body the combed hairs run over, the width it starts with (share of the half width) and the share over which it reaches its full width.")]
            [Range(0f, .6f)] public float TailShare = .15f;
            [Range(0f, 3f)] public float DryShare = .9f;
            [Range(.2f, 1f)] public float RootWidth = .34f;
            [Range(.05f, 1f)] public float RootRamp = .85f;
            [Tooltip("The pressed head each arc ends in (the brush is lifted off there; pass 4b: it swells before it closes - a thick end after a thin start).")]
            public SpellDeploy308ProfileSO.BrushHeadSet Head = new SpellDeploy308ProfileSO.BrushHeadSet { Share = .16f, Lean = 1f, End = .45f, Round = 2.2f, Rings = 4, Cap = .55f, Swell = .12f, SwellSpan = .5f };
            [Tooltip("Rings of an arc's body on PC / Mobile (a quarter circle needs about ten to stay round).")]
            [Range(6, 16)] public int Segments = 12;
            [Range(6, 16)] public int SegmentsMobile = 9;
            [Tooltip("m the stroke lies over the ground it was fitted to; cels between the births of two arcs (all of them are born inside the body's birth beat: 0 or 1).")]
            [Range(0f, .1f)] public float Lift = .03f;
            [Range(0, 1)] public int BirthStep = 1;
            [Tooltip("The arcs lean in and out of the ring by this share of the half width (alternating), so that the ring is brushed, not ruled; each arc takes between WobbleLeast and all of it.")]
            [Range(0f, 1f)] public float Wobble = .5f;
            [Range(0f, 1f)] public float WobbleLeast = .5f;
            [Tooltip("s; the ring is taken off at the latest after this, whatever the rule says (ink, no glow after its birth cels).")]
            [Min(1f)] public float MaxSeconds = 120f;
        }

        [Serializable]
        public sealed class ShedSet
        {
            [Range(0, 16)] public int Drops = 7;
            [Tooltip("m ahead of the eye and below it the drops start; degrees they spread over.")]
            [Min(.5f)] public float Ahead = 1.2f;
            [Range(0f, 1.2f)] public float Below = .55f;
            [Range(10f, 160f)] public float SpreadDeg = 70f;
            [Min(.02f)] public float Size = .09f;
            [Min(.05f)] public float SpentStar = .5f;
            [Range(.04f, .3f)] public float SpentStarSeconds = .125f;
            [Min(.05f)] public float FeetMark = .3f;
        }

        [Serializable]
        public sealed class SplashSet
        {
            [Tooltip("Grade (0 low, 1 mid, 2 high) of the splash thrown at a point where no enemy hit is confirmed.")]
            [Range(0, 2)] public int PointGrade = 0;
            [Tooltip("A confirmed hit of a glyph this presenter shows that NO live cast took (a second stage, a derived shot, a companion shot, a detonation, a sword strike) throws the hit splash too.")]
            public bool Free = true;
            [Range(0, 2)] public int DirectGrade = 1, CompanionGrade = 0, RetaliationGrade = 0, HarmonyGrade = 2;
            [Tooltip("Damage over time (a zone's step) drips instead of splashing.")]
            public bool PersistentDrip = true;
            [Range(0, 8)] public int DripDrops = 3;
            [Tooltip("forms3 (D10, were code constants): a zone step's drops leave DripSpread m round the point, outward at DripOut m/s and upward at DripUp m/s (x = least, y = most), DripSize times the profile's small drop (x - y); the first DripTailed of them are the tailed drop, drawn along the way they fall.")]
            [Min(0f)] public float DripSpread = .24f;   // pass 4c: .24 (was .15) - the two round drops left as one lump
            public Vector2 DripOut = new Vector2(.3f, 1.1f), DripUp = new Vector2(.2f, .9f), DripSize = new Vector2(2.6f, 4.4f);   // pass 4c: 2.6 - 4.4 (was 3 - 7: two balls of .3 m)
            [Tooltip("pass 4c: the drops of one step are born at different heights - m over (under) the point, drop after drop (read round and round; none = 0).")]
            public float[] DripRise = { 0f, .14f, -.12f };
            [Tooltip("forms3 fix pass: s between two drops of one step (they leave one after another, not as one dot).")]
            [Range(0f, .3f)] public float DripStagger = .07f;
            [Range(0, 8)] public int DripTailed = 1;   // forms4 (P8): one tailed drop and round ones (two tailed drops at a right angle were a propeller)
            [Min(.05f)] public float DripCooldown = .5f;
            [Tooltip("Free splashes per second at most (one splash takes about twenty slots of the residue field, which every cast shares).")]
            [Range(1, 16)] public int PerSecond = 4;
            [Tooltip("Which rows' hits the presenter shows itself (the KTP enemy-hit contact stays out for them), beside the layer's own retired attack rows.")]
            public bool OwnBuffRows = true, OwnInstallRows = true;
        }

        [Serializable]
        public sealed class MarkSet
        {
            [Tooltip("An enemy's control / modifier state is shown as an ink mark it carries (no cue comes for these: the state itself is read).")]
            public bool StateMarks = true;
            [Range(1f, 30f)] public float WatchHz = 6f;
            [Min(.05f)] public float Size = .34f;
            [Tooltip("m toward the eye the mark stands off the enemy's root (at least the enemy's own collider radius).")]
            [Min(0f)] public float TowardEye = .45f;
            [Tooltip("Share of the enemy's collider height the chest mark sits at (no collider: the layer's chest height).")]
            [Range(.2f, 1f)] public float HeightShare = .62f;
            [Tooltip("s; a carried mark is taken off at the latest after this, whatever the rule says.")]
            [Min(1f)] public float MaxSeconds = 60f;
            [Tooltip("A slowed enemy below this movement scale counts as slowed.")]
            [Range(.05f, 1f)] public float SlowBelow = .98f;
        }

        [Serializable]
        public sealed class SwordSet
        {
            [Min(.02f)] public float SlashWidth = .11f;
            [Range(20f, 140f)] public float SlashArcDeg = 70f;
            [Tooltip("Share of the strike's reach the sweep is drawn at (never inside the near fade).")]
            [Range(.3f, 1f)] public float SlashReachShare = .8f;
            [Tooltip("m; the reach used when the cue carries none.")]
            [Min(.9f)] public float SlashReach = 2.2f;
            [Range(0f, .6f)] public float SlashTailShare = .45f;
            [Min(0f)] public float SlashHold = .12f;
            [Min(.3f)] public float BladeLength = 1.3f;
            [Min(.02f)] public float BladeWidth = .042f;
            [Min(0f)] public float BladeHold = .45f;
            [Tooltip("look2 round 2: a sword stroke is drawn 2 m ahead and fills much of the view - share of the layer's birth glow it takes (at the full amount it flashed as a flat grey / red plane), share of its wet body that narrows to the one point at its end, share over which the body reaches its full width.")]
            [Range(0f, 1f)] public float GlowShare = .35f;
            [Range(0f, 1f)] public float TipTaper = .35f;
            [Range(.05f, 1f)] public float RootRamp = .3f;
            [Tooltip("forms3 (D12): the sweep and the blade end in the brush's pressed head (blunt, one edge straight, the other curving in; the width stays full and falls only at the very end). Share 0 = the old TipTaper point.")]
            public SpellDeploy308ProfileSO.BrushHeadSet SlashHead = new SpellDeploy308ProfileSO.BrushHeadSet { Share = .3f, Lean = 1f, End = .5f, Round = 2.2f, Rings = 7, Cap = .6f };
            public SpellDeploy308ProfileSO.BrushHeadSet BladeHead = new SpellDeploy308ProfileSO.BrushHeadSet { Share = .26f, Lean = 1f, End = .3f, Round = 1.7f, Rings = 6, Cap = .5f };
            [Tooltip("forms3 (D3, were code constants): rings of a sword stroke's body (0 = the layer's Stroke.CapsuleSegments); the sweep starts SlashFromY under the eye line and ends SlashToY over it (m); SlashHairs dry hairs run with it.")]
            [Range(0, 24)] public int Segments = 16;
            public float SlashFromY = -.42f, SlashToY = .06f;
            [Range(0, 4)] public int SlashHairs = 2;
            [Tooltip("forms3 (D12): needle lines along the back of the blade. 0: a separate straight line beside the stroke read as a pole. BladeTailShare = dry share of the blade stroke, BladeCurve = its bend (share of its length).")]
            [Range(0, 2)] public int BladeNeedles = 0;
            [Range(0f, .6f)] public float BladeTailShare = .22f;
            [Range(-.3f, .3f)] public float BladeCurve = .04f;
            [Tooltip("forms3 fix pass (director 3: the blade was a feather - widest in the middle). The blade starts thin at the hand (BladeRootWidth of its width), widens over BladeRootRamp of its body - it is widest near its head - and closes with one straight edge (BladeHead). BladeFrom = where its tail is against the eye (m: right, up, ahead - ahead never under the near fade), BladeAxis = the way it is drawn. (Both were code constants.)")]
            [Range(.1f, 1f)] public float BladeRootWidth = .38f;
            [Range(.05f, 1f)] public float BladeRootRamp = .85f;
            public Vector3 BladeFrom = new Vector3(.34f, -.5f, .05f);
            public Vector3 BladeAxis = new Vector3(-.26f, .62f, .74f);
            [Tooltip("forms3 fix pass (review S4, were code constants): each grade widens the sweep / the blade by this share; a dry hair of the sweep starts SlashHairFrom (least, most) of the way along it, SlashHairDepth of the reach behind it and up to SlashHairLift m above or below, runs SlashHairRun (least, most) of the reach and is SlashHairWidth of the sweep's width.")]
            [Range(0f, .5f)] public float SlashGradeWiden = .2f, BladeGradeWiden = .15f;
            public Vector2 SlashHairFrom = new Vector2(.15f, .55f), SlashHairRun = new Vector2(.3f, .5f);
            [Range(0f, .5f)] public float SlashHairDepth = .1f, SlashHairLift = .14f, SlashHairWidth = .35f;
        }

        [Serializable]
        public sealed class AirFloorSet
        {
            [Range(0f, 40f)] public float MinPixels = 12f;
            [Min(0f)] public float FloorFrom = 6f;
            [Min(240f)] public float ScreenHeight = 1080f;
            [Range(20f, 120f)] public float FovDeg = 60f;
            [Min(.02f)] public float FloorMax = .3f;
        }

        [Serializable]
        public sealed class PoolSet
        {
            [Range(1, 8)] public int Casts = 8;
            [Range(1, 24)] public int Bodies = 12;
            [Range(128, 1024)] public int BodyVerts = 384;
            [Range(8, 128)] public int Handles = 96;
            [Tooltip("s after the life a request asked for before its handle is ended anyway (a handler that never says End).")]
            [Min(0f)] public float HandleSlack = 5f;
            [Tooltip("s; the shortest birth beat of a body (its strokes glow inside it only).")]
            [Range(.05f, .5f)] public float BodyBeat = .17f;
            [Tooltip("s; a standing ink wall stands at most this long.")]
            [Min(1f)] public float MaxStandSeconds = 30f;
            [Tooltip("s; a cast that asks to live this much longer than its impact clock leaves a mark on its target.")]
            [Min(0f)] public float LastingSlack = .25f;
            [Tooltip("Where the brush tip is taken to be: m ahead of the eye along the view, and m below it (the layer's own preview uses the same). A burst the presenter begins ignites there, thrown ink leaves from there and comes back to it.")]
            [Min(.5f)] public float BrushReach = 1.6f;
            [Range(0f, 1f)] public float BrushDrop = .25f;
        }

        public FlingSet Fling = new FlingSet();
        public RingSet Ring = new RingSet();
        public ShedSet Shed = new ShedSet();
        public SplashSet Splash = new SplashSet();
        public MarkSet Mark = new MarkSet();
        public SwordSet Sword = new SwordSet();
        public PoolSet Pool = new PoolSet();
        [Tooltip("forms4 (P8): an airborne drop keeps a least size ON SCREEN when it is far away (thrown ink was a dash at 6 m and nothing at 9 m). From FloorFrom m on, its size is at least MinPixels pixels of a ScreenHeight-pixel view with a FovDeg vertical field of view, at the farthest point of its flight from the eye; the floor never makes a drop larger than FloorMax m.")]
        public AirFloorSet AirFloor = new AirFloorSet();
        [Tooltip("Element wash used when the glyph's catalogue profile gives no pigment (Wood, Fire, Earth, Metal, Water).")]
        public Color[] ElementTints =
        {
            new Color(.30f, .48f, .32f), new Color(.72f, .28f, .20f), new Color(.62f, .50f, .28f), new Color(.66f, .66f, .64f), new Color(.24f, .36f, .56f),
        };

        [Tooltip("One row per presenter call a handler makes. A call without a row is counted as unrouted and draws nothing.")]
        public PresentRoute308[] Routes = DefaultRoutes();

        // ---- clamped reads (the stage and the rules never see the raw fields of a limit)
        public float AirSeconds(float seconds) => Mathf.Clamp(seconds, .05f, MaxAirSeconds);
        /// <summary>forms4 (P8): the size an airborne drop of `size` m is drawn with when the farthest point of its flight is
        /// `distance` m from the eye - never smaller than AirFloor.MinPixels on screen from AirFloor.FloorFrom m on (and never
        /// enlarged past AirFloor.FloorMax). Nearer than FloorFrom, or with the floor switched off, the size is unchanged.</summary>
        public float AirSize(float size, float distance) => AirSize(size, distance, 0f, 1f);
        /// <summary>fix pass 4b (review S6 / director 4): the same with the view's own vertical field of view (`fovDeg`; 0 = AirFloor.FovDeg,
        /// the sheet's assumption) and for a drop that is drawn `widthShare` of its size wide (the tailed drop: its SHORT side keeps
        /// the least size - the floor was given to a size the drop is drawn narrower than).</summary>
        public float AirSize(float size, float distance, float fovDeg, float widthShare)
        {
            var f = AirFloor;
            if (f == null || f.MinPixels <= 0f || distance < f.FloorFrom) return size;
            float floor = f.MinPixels * 2f * distance * Mathf.Tan(Mathf.Clamp(fovDeg > 0f ? fovDeg : f.FovDeg, 20f, 120f) * .5f * Mathf.Deg2Rad) / Mathf.Max(240f, f.ScreenHeight);
            floor /= Mathf.Clamp(widthShare, .2f, 1f);
            return Mathf.Max(size, Mathf.Min(floor, Mathf.Max(size, f.FloorMax)));
        }
        public float MarkSeconds => Mathf.Clamp(Mark.MaxSeconds, 1f, MaxMarkSecondsCeiling);
        /// <summary>forms3 (D9): the longest a zone's ring stroke stays (the same code ceiling as a carried mark).</summary>
        public float RingSeconds => Mathf.Clamp(Ring.MaxSeconds, 1f, MaxMarkSecondsCeiling);
        /// <summary>forms3 fix pass (review S1): ring strokes alive at once - the data's number, never more than half the body pool.</summary>
        public int RingBodies => Mathf.Clamp(Ring.BodiesMax, 1, Mathf.Max(1, Pool.Bodies / 2));
        /// <summary>The least number of arcs a ring stroke has on a tier (a small ring).</summary>
        public int RingArcs(DeployTier308 tier) => Mathf.Clamp(tier == DeployTier308.Mobile ? Ring.ArcsMobile : Ring.Arcs, 3, 4);
        /// <summary>forms4 (P5): arcs of a ring of this radius - one every Ring.ArcEvery m of its length, between the tier's least
        /// and the tier's most (never over MaxRingArcs: the body's vertex room).</summary>
        public const int MaxRingArcs = 8;
        public int RingArcs(DeployTier308 tier, float radius)
        {
            int least = RingArcs(tier), most = Mathf.Clamp(tier == DeployTier308.Mobile ? Ring.ArcsMaxMobile : Ring.ArcsMax, least, MaxRingArcs);
            return Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * Mathf.Max(Ring.MinRadius, radius) / Mathf.Max(1f, Ring.ArcEvery)), least, most);
        }
        public float RingHalfWidth(float radius) => Mathf.Clamp(Mathf.Max(Ring.MinRadius, radius) * Ring.HalfShare, Mathf.Min(Ring.HalfMin, Ring.HalfMax), Mathf.Max(Ring.HalfMin, Ring.HalfMax));
        public float StandSeconds(float asked) => Mathf.Clamp(asked, 0f, Mathf.Min(Pool.MaxStandSeconds, MaxStandSecondsCeiling));
        public int FlingDrops => Mathf.Clamp(Fling.Drops, 1, MaxFlingDrops);
        public int RingCount(float radius, DeployTier308 tier = DeployTier308.PC)
        {
            float scale = tier == DeployTier308.Mobile ? Mathf.Clamp(Ring.MobileCount, .25f, 1f) : 1f;
            int n = Mathf.RoundToInt(2f * Mathf.PI * Mathf.Max(Ring.MinRadius, radius) / Mathf.Max(.2f, Ring.Spacing) * scale);
            int low = Mathf.Max(3, Mathf.RoundToInt(Mathf.Min(Ring.CountMin, Ring.CountMax) * scale));
            int high = Mathf.Max(low, Mathf.RoundToInt(Mathf.Min(Mathf.Max(Ring.CountMin, Ring.CountMax), MaxRingStamps - 1) * scale));
            return Mathf.Clamp(n, low, high);
        }
        /// <summary>Held ground marks the presenter may keep wet at once on a residue field whose spell ring has `fieldCapacity` slots.</summary>
        public int HeldMarks(int fieldCapacity) =>
            Mathf.Max(4, Mathf.Min(Ring.HeldBudget, Mathf.FloorToInt(Mathf.Max(0, fieldCapacity) * Mathf.Clamp(Ring.HeldShare, .1f, .8f))));
        public Color Tint(int element) => ElementTints != null && element >= 0 && element < ElementTints.Length ? ElementTints[element] : Color.grey;

        public PresentRoute308 Find(SpellFxRole role, PresentMoment308 moment, SpellFxCue cue)
        {
            if (Routes == null) return null;
            foreach (var route in Routes)
                if (route != null && route.Role == role && route.Moment == moment && (moment != PresentMoment308.Cue || route.Cue == cue)) return route;
            return null;
        }

        // The code defaults: one row for every presenter call the 55 effect handlers of SPEC-SPELL-120-308 make (the inventory
        // is Tools/Unity/Stage308_present/inventory308.tsv; pure308_present.py holds the two against each other).
        public static PresentRoute308[] DefaultRoutes()
        {
            const PresentMoment308 B = PresentMoment308.Begin, C = PresentMoment308.Cue, E = PresentMoment308.End;
            const SpellFxCue none = default;
            return new[]
            {
                // a flying shot the handler presents itself (derived-hit glyphs, the intercepting needle)
                new PresentRoute308(SpellFxRole.Projectile, B, none, "the glyph's own burst: a single shot flies as the main stroke, a volley follows its plan", PresentAct308.Cast),
                new PresentRoute308(SpellFxRole.Projectile, C, SpellFxCue.Hit, "the confirmed hit throws the hit splash itself; a cast that lives on (an ember, a shot that returns) leaves its mark on the enemy", PresentAct308.MarkCarried).Lasting(),
                new PresentRoute308(SpellFxRole.Projectile, C, SpellFxCue.Split, "the derived shots leave the point of impact as thrown ink", PresentAct308.FlingToCueTargets),
                new PresentRoute308(SpellFxRole.Projectile, C, SpellFxCue.Secondary, "a derived hit is confirmed damage: the layer throws the hit splash for it; an ember that moved on (a cast that lives on) marks the enemy it reached - only now, once the move has landed", PresentAct308.MarkCueCarrier).Lasting(),
                new PresentRoute308(SpellFxRole.Projectile, C, SpellFxCue.Return, "the shot leaves the enemy and comes back as thrown ink", PresentAct308.Unmark, PresentAct308.FlingToCaster),
                new PresentRoute308(SpellFxRole.Projectile, C, SpellFxCue.Consume, "it arrived: a small mark at the caster's feet (the meters show what came back)", PresentAct308.Drip),
                new PresentRoute308(SpellFxRole.Projectile, C, SpellFxCue.TargetDefeated, "the carrier fell: the ember leaves it as thrown ink (the next enemy is marked when the move lands: Secondary - a wall may stop it)", PresentAct308.Unmark, PresentAct308.FlingToCueTargets),
                new PresentRoute308(SpellFxRole.Projectile, C, SpellFxCue.Expire, "the ember's time is up: it leaves as thrown ink (the next enemy is marked when the move lands: Secondary), or goes out", PresentAct308.Unmark, PresentAct308.FlingToCueTargets),
                new PresentRoute308(SpellFxRole.Projectile, C, SpellFxCue.Intercept, "an enemy projectile was shot down: ink bursts where it was", PresentAct308.Splash).End(),
                new PresentRoute308(SpellFxRole.Projectile, C, SpellFxCue.Miss, "nothing was shot down: the needle's stroke ends on its own", PresentAct308.Silent).End(),
                new PresentRoute308(SpellFxRole.Projectile, E, none, "what the handle still holds is released", PresentAct308.Silent),
                // a companion buff and its shots
                new PresentRoute308(SpellFxRole.Companion, B, none, "cast cue of the buff: strokes wind once round the caster", PresentAct308.Cast),
                new PresentRoute308(SpellFxRole.Companion, C, SpellFxCue.Secondary, "a companion shot leaves the caster as thrown ink", PresentAct308.FlingFromCaster),
                new PresentRoute308(SpellFxRole.Companion, C, SpellFxCue.Hit, "the companion shot's damage is confirmed: the layer throws the hit splash for it", PresentAct308.Silent),
                new PresentRoute308(SpellFxRole.Companion, E, none, "end cue of the buff: the ink falls off", PresentAct308.Shed),
                // the nine self buffs
                new PresentRoute308(SpellFxRole.Aura, B, none, "cast cue of the buff: strokes wind once round the caster", PresentAct308.Cast),
                new PresentRoute308(SpellFxRole.Aura, C, SpellFxCue.Expire, "end cue: the ink falls off", PresentAct308.Shed),
                new PresentRoute308(SpellFxRole.Aura, C, SpellFxCue.Consume, "end cue of a buff that was used up: the ink falls off with a needle star", PresentAct308.ShedSpent),
                new PresentRoute308(SpellFxRole.Aura, E, none, "the end cue was shown by Expire / Consume; a recast replaces the buff without one", PresentAct308.Silent),
                // a zone that stays
                new PresentRoute308(SpellFxRole.Zone, B, none, "the glyph's own burst where the zone forms (a tree rises), then ground marks that stay wet while the zone lives",
                    PresentAct308.Cast, PresentAct308.HeldRing).As(PresentCastAs308.WhenNotAttack, DeployCategory308.Summon),
                new PresentRoute308(SpellFxRole.Zone, C, SpellFxCue.Expire, "the zone is over: its ground marks dry", PresentAct308.Release).End(),
                new PresentRoute308(SpellFxRole.Zone, C, SpellFxCue.Break, "the zone was broken: ink bursts at its centre, the ground marks dry", PresentAct308.Splash, PresentAct308.Release).End(),
                new PresentRoute308(SpellFxRole.Zone, E, none, "replaced or dropped: the ground marks dry", PresentAct308.Release),
                // an attack whose confirmed hits open zones
                new PresentRoute308(SpellFxRole.Cast, B, none, "the glyph's own area burst", PresentAct308.Cast),
                new PresentRoute308(SpellFxRole.Cast, C, SpellFxCue.Secondary, "a zone opens where a hit landed: ground marks that stay wet while it lives", PresentAct308.HeldRingAtCue),
                new PresentRoute308(SpellFxRole.Cast, C, SpellFxCue.Expire, "the last zone of the cast is over: the ground marks dry", PresentAct308.Release).End(),
                new PresentRoute308(SpellFxRole.Cast, C, SpellFxCue.Break, "the last zone of the cast was broken: the ground marks dry", PresentAct308.Release).End(),
                new PresentRoute308(SpellFxRole.Cast, E, none, "replaced or dropped: the ground marks dry", PresentAct308.Release),
                // an installed mark and its trigger
                new PresentRoute308(SpellFxRole.Mark, B, none, "the install form at the brush and the mark thrown at the enemy", PresentAct308.Cast, PresentAct308.FlingToTarget),
                new PresentRoute308(SpellFxRole.Mark, C, SpellFxCue.Anchor, "the mark is on the enemy: it carries it until it fires or runs out", PresentAct308.MarkInstall),
                new PresentRoute308(SpellFxRole.Mark, C, SpellFxCue.Miss, "the thrown mark does not arrive; End follows", PresentAct308.Silent),
                new PresentRoute308(SpellFxRole.Mark, C, SpellFxCue.Expire, "unfired: the mark melts off the enemy", PresentAct308.Unmark),
                new PresentRoute308(SpellFxRole.Mark, C, SpellFxCue.TargetDefeated, "lost with its enemy: the mark melts", PresentAct308.Unmark),
                new PresentRoute308(SpellFxRole.Mark, C, SpellFxCue.Detonate, "the layer's triggered burst (impact frames only if the target is groggy after it); the detonation's damage throws the hit splash", PresentAct308.Unmark, PresentAct308.Trigger),
                new PresentRoute308(SpellFxRole.Mark, E, none, "what the handle still holds is released", PresentAct308.Silent),
                // cover that stands
                new PresentRoute308(SpellFxRole.Ward, B, none, "a standing ink wall for the cover's life (ink, no glow once it has risen) and ground marks under it",
                    PresentAct308.Stand, PresentAct308.HeldRing).As(PresentCastAs308.Always, DeployCategory308.Ward),
                new PresentRoute308(SpellFxRole.Ward, C, SpellFxCue.Expire, "the cover is over: the wall drains on its own clock, the ground marks dry", PresentAct308.Release).End(),
                new PresentRoute308(SpellFxRole.Ward, E, none, "replaced: the wall and the ground marks go", PresentAct308.Release),
                // a field glyph that opens a way
                new PresentRoute308(SpellFxRole.Field, B, none, "the field form: ground marks at the gate that stay wet through the burst", PresentAct308.Cast),
                new PresentRoute308(SpellFxRole.Field, C, SpellFxCue.Hit, "the gate opens: ink bursts at it", PresentAct308.Splash).End(),
                new PresentRoute308(SpellFxRole.Field, E, none, "nothing is held", PresentAct308.Silent),
                // the sword form
                new PresentRoute308(SpellFxRole.Attached, B, none, "cast cue of the form and the stroke that becomes the blade", PresentAct308.Cast, PresentAct308.Blade),
                new PresentRoute308(SpellFxRole.Attached, C, SpellFxCue.Hit, "one sweep of ink toward the enemy; the strike's damage throws the hit splash", PresentAct308.SlashHit),
                new PresentRoute308(SpellFxRole.Attached, C, SpellFxCue.Miss, "one sweep of ink through the air", PresentAct308.Slash),
                new PresentRoute308(SpellFxRole.Attached, C, SpellFxCue.Expire, "end cue of the form: the ink falls off", PresentAct308.Shed),
                new PresentRoute308(SpellFxRole.Attached, E, none, "the end cue was shown by Expire", PresentAct308.Silent),
            };
        }

        private void OnValidate()
        {
            if (Ring.CountMax < Ring.CountMin) Ring.CountMax = Ring.CountMin;
            Mark.MaxSeconds = Mathf.Min(Mark.MaxSeconds, MaxMarkSecondsCeiling);
            Ring.MaxSeconds = Mathf.Min(Ring.MaxSeconds, MaxMarkSecondsCeiling);
            Pool.MaxStandSeconds = Mathf.Min(Pool.MaxStandSeconds, MaxStandSecondsCeiling);
        }
    }
}
