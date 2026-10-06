using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>Deterministic generator: the layer never touches UnityEngine.Random.</summary>
    public struct DeployRng308
    {
        private uint _s;
        public DeployRng308(int seed) { _s = (uint)seed * 747796405u + 2891336453u; if (_s == 0u) _s = 1u; Next(); }
        public float Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (_s & 0xFFFFFFu) / 16777216f; }
        public float Range(float a, float b) => a + (b - a) * Next();
        public float Sign() => Next() < .5f ? -1f : 1f;
    }

    /// <summary>A drop the form asks for: a ground mark (residue) or an airborne drop that falls and becomes one.</summary>
    public struct DeployDrop308
    {
        public Vector3 Local;      // effect local; ground marks are placed by the field's ray (ComposeHit writes WORLD space here)
        public Vector3 Velocity;   // air drops only (local; world for ComposeHit)
        public float Size;
        public int Cell, Cel;
        public bool Air, Held;
        public float Life;         // air sprites: seconds (0 = the falling drop's default)
        public bool Still;         // air sprites: no gravity, it stays where it is (contact star, wet blot)
        public bool Free;          // #308 forms2: never held, whatever the category says (a mark of something that was not installed)
    }

    /// <summary>A confirmed hit (D308-10c), in world space.</summary>
    public struct DeployHitInput308
    {
        public SpellDeploy308ProfileSO Profile;
        public DeployTier308 Tier;
        public Element Element;
        public int Grade;          // 0 low, 1 mid, 2 high
        public Vector3 Point;      // where the spell met the enemy
        public Vector3 ToEye;      // from the hit point toward the camera
        public float GroundY;      // the ground under the enemy
        public int Seed;
    }

    public struct DeployFormInput308
    {
        public SpellDeploy308ProfileSO Profile;
        public SpellDeploy308ProfileSO.TierSet Tier;
        public DeployCategory308 Category;
        public DeployFrame308 Frame;
        public Element Element;
        public DeployFinal308 Final;
        public int Grade;                 // 0 low, 1 mid, 2 high
        public AreaShape Shape;
        public float Radius, Length, HalfAngleDeg, Speed;
        public int Shots;
        public Vector3[] ShotPoints;      // Volley, effect local (caller-owned)
        public int[] ShotCels;
        public Vector3 PlanPoint;         // Circle centre / Path start / Cone apex (effect local)
        public Vector3 PlanDirection;     // flat unit (effect local)
        public Vector3 Target;            // single target (effect local)
        public Vector3 Camera;            // camera position (effect local)
        public float GroundY, OriginGroundY;
        public int Seed, ImpactCel, BurstCels, IgniteEndCel;
        public float CelSeconds;
        public float WardRadius, WardHeight;
        public bool Triggered;            // combo install: the triggered burst
        // ---- #308 forms2 (InkDeployForms308.Forms2.cs). Form = Category leaves everything above as it was.
        public DeployForm308 Form;
        public GuardPhase308 GuardPhase;  // guard: which look is composed
        public int PhaseCel;              // guard: the cel this look is born on
        public int GuardEndCel, MeltCels; // guard: the cel the rule's lifetime ends in / cels the melt takes
        public Vector3 Anchor;            // emerge: the summon's own place (effect local)
        public bool HasAnchor;
        // ---- #308 forms3 fix pass
        public bool Cover;                // the presenter's standing cover wall (rising strokes on the ring instead of the ward category's column ring)
    }

    public struct DeployFormStats308
    {
        public int Bold, Fine, Needles, Sprites, ColumnStrips, Ribbons, Drops, Births;
        public int FirstBurstCel, LastBirthCel;
        public float Reach;               // farthest bold tip from the plan point / target (m)
        public float HalfAngleDeg;        // widest bold direction from the plan direction (cone)
        public float ScreenShare;         // estimated share of the view the bold strokes cover
        public bool HasAdvance, HeadOnlyAdvance, HasColumn, ColumnHolds;
        public float AdvanceStartShare;   // HeadOnly strokes: share of the length already drawn at advance 0
        // ---- #308 forms2
        public bool Comet;                // the advance is the judged flight: the runtime drives it every frame
        public float TopHeight;           // highest vertex over the origin's ground (m)
        public float TopElevationDeg;     // highest vertex seen from the eye (deg, negative = below the horizon)
        public int Posts, Rails, GroundMarks, HeldDrops;
        public float CometHead, CometTailHalf;   // comet: the head's size and the tail's half width as composed (m)
    }

    // SPEC-SPELL-DEPLOY-308 sections 4-6: (category, frame, element, final, grade, plan) -> stroke placement.
    // Pure: no Unity object state, the same input always writes the same geometry. Local frame: +Z forward, +Y up.
    public static partial class InkDeployForms308
    {
        private struct Ctx
        {
            public InkBurstBuffer308 B;
            public SpellDeploy308ProfileSO P;
            public SpellDeploy308ProfileSO.ElementSet E;
            public DeployDrop308[] Drops;
            public float[] Births;
            public DeployFormStats308 S;
            public DeployRng308 R;
            public Vector3 ToCamera, ScreenRight, ScreenUp, Camera;
            public float HalfWidth, Length, Tint;
            public int Bold, Fine, Needles, Impact, BurstCels;
        }

        public static DeployFormStats308 Compose(in DeployFormInput308 input, InkBurstBuffer308 buffer, DeployDrop308[] drops, float[] births)
        {
            var p = input.Profile;
            var c = new Ctx { B = buffer, P = p, Drops = drops, Births = births, R = new DeployRng308(input.Seed) };
            if (p == null || buffer == null) return c.S;
            var tier = input.Tier ?? p.Tier(DeployTier308.PC);
            var category = p.CategoryOf(input.Category);
            bool top = category.ForceTopGrade || input.Triggered;
            var grade = p.Grades != null && p.Grades.Length == 3 ? p.Grades[top ? 2 : Mathf.Clamp(input.Grade, 0, 2)] : p.Grade(top ? 1f : input.Grade * .45f);
            c.E = p.ElementOf(input.Element);
            float boldShare = input.Triggered ? 1f : category.Bold, fineShare = input.Triggered ? 1f : category.Fine, needleShare = input.Triggered ? 1f : category.Needles;
            c.Bold = Mathf.Min(Mathf.RoundToInt(grade.Bold * boldShare), tier.BoldMax);
            c.Fine = tier.Fine ? Mathf.RoundToInt(grade.Fine * fineShare) : 0;
            c.Needles = Mathf.Min(Mathf.RoundToInt(grade.Needles * needleShare * c.E.NeedleMul), tier.NeedleMax * 2);
            c.HalfWidth = grade.Width * c.E.WidthMul * .5f;
            c.Length = p.Stroke.BaseLength * grade.LengthMul * c.E.LengthMul;
            c.Tint = c.E.Tint;
            c.Impact = Mathf.Max(0, input.ImpactCel);
            c.BurstCels = Mathf.Max(1, input.BurstCels);
            c.S.FirstBurstCel = c.Impact;

            Vector3 focus = input.Category == DeployCategory308.AttackSingle || input.Triggered ? input.Target : AreaFocus(input, c.Length);
            c.Camera = input.Camera;
            c.ToCamera = input.Camera - focus;
            float viewDistance = Mathf.Max(.5f, c.ToCamera.magnitude);
            c.ToCamera = c.ToCamera.sqrMagnitude > 1e-6f ? c.ToCamera / viewDistance : new Vector3(0f, .2f, -1f).normalized;
            c.ScreenRight = Vector3.Cross(Vector3.up, c.ToCamera);
            if (c.ScreenRight.sqrMagnitude < 1e-4f) c.ScreenRight = Vector3.right;
            c.ScreenRight.Normalize();
            c.ScreenUp = Vector3.Cross(c.ToCamera, c.ScreenRight).normalized;

            // first-person cover: the bold strokes may not take more than the allowed share of the view (width shrinks, never the count)
            float viewArea = 2.37f * viewDistance * viewDistance;   // 60 deg vertical, 16:9
            float share = c.Bold * 2f * c.HalfWidth * c.Length / viewArea;
            if (share > p.Stroke.ScreenShareMax && share > 0f) { c.HalfWidth *= p.Stroke.ScreenShareMax / share; share = p.Stroke.ScreenShareMax; }
            c.S.ScreenShare = share;

            int drops0 = Mathf.RoundToInt(grade.Drops * c.E.DropMul);
            int ground = Mathf.RoundToInt(category.ResidueDrops * Mathf.Max(.5f, c.E.DropMul));

            // #308 forms2: a cast whose catalogue body is retired gets its own form (decided by InkForms2Rules308.FormOf)
            if (input.Form != DeployForm308.Category && !input.Triggered) { Forms2(ref c, input, tier, ground, drops0 - ground); return Finish(ref c); }
            // #308 forms3 fix pass: a cover the presenter stands up (Cover) and the triggered burst have their own forms (Forms2.cs)
            if (input.Cover && !input.Triggered && input.Category == DeployCategory308.Ward && p.Cover.Enabled) { Ignite(ref c, input, Vector3.zero); int coverFirst = c.B.VertexCount; Cover(ref c, input, tier); RingDrops(ref c, input, ground); Measure(ref c, coverFirst, c.Camera, input.OriginGroundY); return Finish(ref c); }
            if (input.Triggered) { Ignite(ref c, input, focus); if (p.Trigger.Own) ComboBurst(ref c, input); else SingleBurst(ref c, input, false); Smear(ref c, focus, p.Residue.ImpactSmear); GroundDrops(ref c, input, focus, .9f, ground, false); AirDrops(ref c, input, focus, drops0 - ground); return Finish(ref c); }
            switch (input.Category)
            {
                case DeployCategory308.AttackSingle:
                    Ignite(ref c, input, Vector3.zero);
                    SingleBurst(ref c, input, true);
                    Smear(ref c, input.Target, p.Residue.ImpactSmear);
                    GroundDrops(ref c, input, input.Target, .9f, ground, false);
                    AirDrops(ref c, input, input.Target, drops0 - ground);
                    break;
                case DeployCategory308.AttackArea:
                    Ignite(ref c, input, Vector3.zero);
                    Area(ref c, input, ground);
                    break;
                case DeployCategory308.ComboInstall:
                    // the mark: a square smear and two needles; it stays wet while the install lives
                    Sprite(ref c, Vector3.zero, p.Stroke.IgniteRadius * 2.2f, InkBurstMeshBuilder308.CellSquare, 0f, false);
                    NeedleStar(ref c, Vector3.zero, Mathf.Max(2, c.Needles), .22f, .45f, 0f, Vector3.zero, 0f);
                    Drop(ref c, new Vector3(input.Target.x, input.GroundY, input.Target.z), .42f, InkBurstMeshBuilder308.CellSquare, c.Impact, false, Vector3.zero, true);
                    break;
                case DeployCategory308.Parry:
                    // a small needle star at ignition only (gone when the burst would begin): the guard body stays as it is and the
                    // contact star comes from the parry re-broadcast
                    NeedleStar(ref c, Vector3.zero, Mathf.Max(4, c.Needles), .25f, .5f, -Mathf.Max(1, input.IgniteEndCel), Vector3.zero, 0f);
                    break;
                case DeployCategory308.Buff:
                    Ignite(ref c, input, Vector3.zero);
                    Wind(ref c, input, grade);
                    Drop(ref c, new Vector3(input.Camera.x, input.OriginGroundY, input.Camera.z), 1.1f, InkBurstMeshBuilder308.CellRing, c.Impact, false, Vector3.zero, false);
                    GroundDrops(ref c, input, new Vector3(input.Camera.x, input.OriginGroundY, input.Camera.z), .8f, ground, false);
                    break;
                case DeployCategory308.Summon:
                    Ignite(ref c, input, Vector3.zero);
                    Rise(ref c, input);
                    Drop(ref c, new Vector3(0f, input.OriginGroundY, p.Stroke.SummonForward), 1.4f, InkBurstMeshBuilder308.CellPuddle, c.Impact + c.BurstCels, false, Vector3.zero, false);
                    GroundDrops(ref c, input, new Vector3(0f, input.OriginGroundY, p.Stroke.SummonForward), 1.1f, ground, false);
                    break;
                case DeployCategory308.Ward:
                    Ignite(ref c, input, Vector3.zero);
                    Column(ref c, input, tier, true);
                    RingDrops(ref c, input, ground);
                    break;
                case DeployCategory308.Field:
                    Ignite(ref c, input, Vector3.zero);
                    GroundDrops(ref c, input, new Vector3(input.PlanPoint.x, input.GroundY, input.PlanPoint.z), Mathf.Max(1.5f, input.Radius), ground, true);
                    break;
                default:
                    // blank: the smear alone spreads and dries
                    Sprite(ref c, Vector3.zero, p.Stroke.IgniteRadius * 1.6f, InkBurstMeshBuilder308.CellIgnite, 0f, false);
                    Drop(ref c, new Vector3(0f, input.OriginGroundY, 0f), .3f, InkBurstMeshBuilder308.CellIgnite, 0, false, Vector3.zero, false);
                    break;
            }
            return Finish(ref c);
        }

        private static DeployFormStats308 Finish(ref Ctx c) => c.S;

        // ---- D308-10c rules (pure: the runtime and the director only apply them, the checks run them without a scene)

        /// <summary>May this cast get impact frames at all? An attack row, or the triggered burst of a combo install with the
        /// profile's ComboTrigger switch on. This is eligibility only - see ImpactWanted.</summary>
        public static bool ImpactEligible(SpellDeploy308ProfileSO profile, in SpellDeploy308MapSO.Row row, bool triggered) =>
            profile != null && profile.Impact.Enabled && (row.ImpactFrame || (triggered && row.ImpactOnTrigger && profile.Impact.ComboTrigger));

        /// <summary>Must the cast ask whether its judged target is groggy? Only an eligible cast under Impact.Trigger = GroggyOnly.</summary>
        public static bool ImpactAsksGroggy(SpellDeploy308ProfileSO profile, in SpellDeploy308MapSO.Row row, bool triggered) =>
            ImpactEligible(profile, row, triggered) && profile.Impact.Trigger != DeployImpactTrigger308.Always;

        /// <summary>Does the cast ask for impact frames? Eligible, and its judged target is groggy at the impact cel (single: the
        /// target; area: any planned hit target; combo trigger: the triggered target, read after the trigger). Under
        /// Impact.Trigger = Always (comparison captures) every eligible cast does. Everything else asks for nothing at all:
        /// no full-screen frame, no local form - and so no HUD reaction and no cut pause.</summary>
        public static bool ImpactWanted(SpellDeploy308ProfileSO profile, in SpellDeploy308MapSO.Row row, bool triggered, bool groggy) =>
            ImpactEligible(profile, row, triggered) && (profile.Impact.Trigger == DeployImpactTrigger308.Always || groggy);

        /// <summary>The first cel on which nothing of a burst glows: the last cel tick at or before the end of the burst beat (of
        /// its rise, for a column). The hold and the melt lie wholly after it.</summary>
        public static int GlowEndCel(float burstEnd, float impactTime, float burstSeconds, float celSeconds, bool column)
        {
            float cel = Mathf.Max(.001f, celSeconds);
            float end = column ? Mathf.Min(burstEnd, impactTime + Mathf.Max(1, Mathf.RoundToInt(burstSeconds / cel)) * cel) : burstEnd;
            return Mathf.FloorToInt(end / cel + .0001f);
        }

        /// <summary>The glow amount a burst's material gets on cel `cel`: the data amount inside the burst beat, 0 from GlowEndCel
        /// on and whenever the burst melts. (Per stroke the shader multiplies by the falloff since its birth cel.)</summary>
        public static float GlowAmount(SpellDeploy308ProfileSO profile, int cel, int glowEndCel, float melt) =>
            profile != null && cel < glowEndCel && melt <= 0f ? profile.GlowAmount : 0f;

        /// <summary>D308-13b: does the deploy layer show this letter's enemy hit itself? True for a switched-on attack row whose old
        /// body is retired - the wiring then leaves out the KTP enemy-hit contact (the hit splash is the hit).</summary>
        public static bool OwnsHitContact(SpellDeploy308ProfileSO profile, char letter)
        {
            if (profile == null || !profile.Hit.Enabled || !profile.Hit.ReplaceLegacyContact || !profile.IsEnabled(letter) || profile.Map == null) return false;
            if (!profile.Map.TryGet(letter, out var row)) return false;
            return (row.Category == DeployCategory308.AttackSingle || row.Category == DeployCategory308.AttackArea) && row.LegacyBody == DeployLegacyBody308.Retire;
        }

        /// <summary>Largest number of drops ComposeHit writes (ground puddle + star + blot + air drops at the top grade).</summary>
        public const int MaxHitDrops = 48;

        // D308-10c "the effect that bursts out when the enemy is hit": what a CONFIRMED hit adds to the plain burst. Ink is thrown
        // OUT of the hit point - an even ring of drops in the screen plane, leaning back at the eye and upward -, a needle star
        // and a wet blot stand at the point for a moment, and a puddle larger than the plain burst's lies under the enemy.
        // World space: Local / Velocity are world values. Pure and deterministic like Compose. Returns the drops written.
        public static int ComposeHit(in DeployHitInput308 input, DeployDrop308[] drops)
        {
            var p = input.Profile;
            if (p == null || drops == null || !p.Hit.Enabled) return 0;
            var hit = p.Hit;
            var e = p.ElementOf(input.Element);
            var rng = new DeployRng308(input.Seed);
            float scale = hit.Scale(input.Grade), wet = Mathf.Max(.7f, e.DropSizeMul);
            Vector3 toEye = input.ToEye.sqrMagnitude > 1e-6f ? input.ToEye.normalized : Vector3.back;
            Vector3 right = Vector3.Cross(Vector3.up, toEye);
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            Vector3 up = Vector3.Cross(toEye, right).normalized;
            int n = 0;
            if (hit.GroundSmear > 0f && n < drops.Length)
                drops[n++] = new DeployDrop308 { Local = new Vector3(input.Point.x, input.GroundY, input.Point.z), Size = hit.GroundSmear * scale * wet, Cell = InkBurstMeshBuilder308.CellPuddle };
            if (hit.StarSize > 0f && n < drops.Length)
                drops[n++] = new DeployDrop308 { Local = input.Point + toEye * .08f, Size = hit.StarSize * scale, Cell = rng.Next() < .5f ? InkBurstMeshBuilder308.CellStar : InkBurstMeshBuilder308.CellStarB,
                    Air = true, Still = true, Life = hit.StarSeconds };
            if (hit.BlotSize > 0f && n < drops.Length)
                drops[n++] = new DeployDrop308 { Local = input.Point + toEye * .05f, Size = hit.BlotSize * scale * wet, Cell = InkBurstMeshBuilder308.CellIgnite, Air = true, Still = true, Life = hit.BlotSeconds };
            int count = Mathf.RoundToInt(hit.AirDrops * scale * (input.Tier == DeployTier308.Mobile ? hit.MobileScale : 1f) * Mathf.Clamp(e.DropMul, .6f, 1.4f));
            count = Mathf.Min(count, drops.Length - n);
            float speedMax = Mathf.Max(hit.SpeedMin, hit.SpeedMax), upMax = Mathf.Max(hit.UpMin, hit.UpMax);
            for (int i = 0; i < count; i++)
            {
                // an even ring with jitter, open at the bottom: the drops leave sideways and up, a few a little downward
                float a = Mathf.Lerp(-hit.BelowDeg, 180f + hit.BelowDeg, (i + rng.Next()) / count) * Mathf.Deg2Rad;
                Vector3 outward = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                Vector3 velocity = (outward + toEye * hit.TowardEye).normalized * rng.Range(hit.SpeedMin, speedMax) + Vector3.up * rng.Range(hit.UpMin, upMax);
                bool spray = rng.Next() < .22f;                       // a cluster cell = a spray of small drops
                float size = p.Residue.DropSmall * e.DropSizeMul * rng.Range(1.2f, 2.4f) * hit.DropSizeMul * (spray ? 1.6f : 1f);
                drops[n++] = new DeployDrop308 { Local = input.Point + outward * (.08f + .1f * rng.Next()), Velocity = velocity, Size = size,
                    Cell = spray ? InkBurstMeshBuilder308.CellCluster : InkBurstMeshBuilder308.CellDrop, Air = true };
            }
            return n;
        }

        private static void Born(ref Ctx c, float cel)
        {
            if (cel < 0f) return;
            if (c.Births != null && c.S.Births < c.Births.Length) c.Births[c.S.Births] = cel;
            c.S.Births++;
            c.S.LastBirthCel = Mathf.Max(c.S.LastBirthCel, Mathf.RoundToInt(cel));
        }

        private static void Drop(ref Ctx c, Vector3 local, float size, int cell, int cel, bool air, Vector3 velocity, bool held)
        {
            if (c.Drops == null || c.S.Drops >= c.Drops.Length) return;
            c.Drops[c.S.Drops++] = new DeployDrop308 { Local = local, Size = size, Cell = cell, Cel = cel, Air = air, Velocity = velocity, Held = held };
        }

        // the wet middle of a burst's residue: one large puddle (the field splits it into pieces that follow the ground)
        private static void Smear(ref Ctx c, Vector3 at, float size)
        {
            if (size <= 0f) return;
            Drop(ref c, at, size * Mathf.Lerp(.85f, 1.15f, c.R.Next()) * Mathf.Max(.7f, c.E.DropSizeMul), InkBurstMeshBuilder308.CellPuddle, c.Impact, false, Vector3.zero, false);
        }

        // where the eye looks for an area cast: a cone / corridor opens at the caster's feet, so its screen frame, its needle
        // star and its impact point are taken at its middle, not at the apex under the camera
        public static Vector3 AreaFocus(in DeployFormInput308 input, float strokeLength)
        {
            if (input.Category != DeployCategory308.AttackArea || (input.Shape != AreaShape.Cone && input.Shape != AreaShape.Path)) return input.PlanPoint;
            Vector3 forward = new Vector3(input.PlanDirection.x, 0f, input.PlanDirection.z);
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            float reach = input.Length > 0f ? input.Length : strokeLength * (input.Shape == AreaShape.Cone ? 2f : 3f);
            return new Vector3(input.PlanPoint.x, input.GroundY + .6f, input.PlanPoint.z) + forward * (reach * .55f);
        }

        private static float DropSize(ref Ctx c, out int cell)
        {
            float r = c.R.Next();
            var res = c.P.Residue;
            float size = (r < .5f ? res.DropSmall : r < .85f ? res.DropMid : res.DropLarge) * c.E.DropSizeMul * 2f;
            cell = r < .5f ? InkBurstMeshBuilder308.CellDrop : r < .85f ? InkBurstMeshBuilder308.CellCluster : InkBurstMeshBuilder308.CellPuddle;
            if (cell == InkBurstMeshBuilder308.CellCluster) size *= 2.2f;
            return size;
        }

        private static void GroundDrops(ref Ctx c, in DeployFormInput308 input, Vector3 centre, float radius, int count, bool held)
        {
            for (int i = 0; i < count; i++)
            {
                float a = c.R.Next() * Mathf.PI * 2f, r = radius * Mathf.Sqrt(c.R.Next());
                float size = DropSize(ref c, out int cell);
                Drop(ref c, new Vector3(centre.x + Mathf.Cos(a) * r, centre.y, centre.z + Mathf.Sin(a) * r), size, cell, c.Impact + (int)(c.R.Next() * 2f), false, Vector3.zero, held);
            }
        }

        private static void RingDrops(ref Ctx c, in DeployFormInput308 input, int count)
        {
            float radius = input.WardRadius > 0f ? input.WardRadius : 3f;
            for (int i = 0; i < count; i++)
            {
                float a = (i + c.R.Next() * .6f) / Mathf.Max(1, count) * Mathf.PI * 2f;
                float size = DropSize(ref c, out int cell);
                Drop(ref c, new Vector3(Mathf.Cos(a) * radius, input.OriginGroundY, Mathf.Sin(a) * radius), size, cell, c.Impact + c.BurstCels, false, Vector3.zero, false);
            }
        }

        private static void AirDrops(ref Ctx c, in DeployFormInput308 input, Vector3 from, int count)
        {
            count = Mathf.Clamp(count, 0, 16);
            for (int i = 0; i < count; i++)
            {
                float a = c.R.Next() * Mathf.PI * 2f;
                Vector3 direction = c.ScreenRight * Mathf.Cos(a) + c.ScreenUp * Mathf.Abs(Mathf.Sin(a));
                float size = c.P.Residue.DropSmall * c.E.DropSizeMul * c.R.Range(1.2f, 2.4f);
                Drop(ref c, from, size, InkBurstMeshBuilder308.CellDrop, c.Impact + (int)(c.R.Next() * 2f), true, direction * c.R.Range(1.6f, 4.2f) + Vector3.up * c.R.Range(.4f, 1.6f), false);
            }
        }

        // ignition: a small smear and a needle star at the brush tip, gone when the burst begins. Lines, not light.
        private static void Ignite(ref Ctx c, in DeployFormInput308 input, Vector3 at)
        {
            if (!c.P.CategoryOf(input.Category).Ignite) return;
            float death = -Mathf.Max(1, Mathf.Min(input.IgniteEndCel, Mathf.Max(1, c.Impact)));
            Sprite(ref c, at, c.P.Stroke.IgniteRadius, InkBurstMeshBuilder308.CellIgnite, death, false);
            NeedleStar(ref c, at, 4 + Mathf.Clamp(input.Grade, 0, 2) * 2, .16f, .38f, death, Vector3.zero, 0f);
        }

        private static void Sprite(ref Ctx c, Vector3 at, float half, int cell, float birth, bool rides)
        {
            if (InkBurstMeshBuilder308.AddSprite(c.B, at, c.ScreenRight, c.ScreenUp, half, half, cell, birth, c.R.Next(), c.Tint * .5f, Vector3.zero, rides ? 1f : 0f))
            { c.S.Sprites++; Born(ref c, birth); }
        }

        private static void NeedleStar(ref Ctx c, Vector3 at, int count, float minLength, float maxLength, float birth, Vector3 advance, float weight)
        {
            float half = c.P.Stroke.NeedleWidth * .5f;
            float phase = c.R.Next() * Mathf.PI * 2f;
            for (int i = 0; i < count; i++)
            {
                float a = phase + (i + c.R.Range(-.3f, .3f)) / Mathf.Max(1, count) * Mathf.PI * 2f;
                Vector3 direction = c.ScreenRight * Mathf.Cos(a) + c.ScreenUp * Mathf.Sin(a);
                if (InkBurstMeshBuilder308.AddNeedle(c.B, at + direction * (minLength * .2f), direction, c.R.Range(minLength, maxLength), half, c.ToCamera, birth, c.R.Next(), advance, weight))
                { c.S.Needles++; if (i == 0) Born(ref c, birth); }
            }
        }

        private static InkStroke308 Stroke(ref Ctx c, Vector3 head, Vector3 axis, Vector3 side, float length, float halfWidth, float birth)
        {
            var e = c.E;
            Vector3 bend = Vector3.up - axis * Vector3.Dot(Vector3.up, axis);
            bend = bend.sqrMagnitude > 1e-4f ? bend.normalized : side;
            float curve = e.Curve * .22f, sCurve = e.SCurve * .3f * c.R.Sign();
            var s = new InkStroke308
            {
                Axis = axis, Side = side, Bend = e.SCurve > 0f ? side : bend, Length = length, HalfWidth = halfWidth, Curve = curve, SCurve = sCurve,
                TailShare = Mathf.Clamp(c.P.Stroke.TailShare * e.TailMul, 0f, .6f), Split = e.Split > 0 ? Mathf.Max(2, e.Split - (c.R.Next() < .5f ? 1 : 0)) : 0,
                Segments = c.P.Stroke.CapsuleSegments, TailCell = (int)(c.R.Next() * 3.99f), BirthCel = birth, Seed01 = c.R.Next(), TintWeight = c.Tint,
                MeltBias = c.R.Next(),
            };
            // the head tip lands exactly on `head`
            s.Tail = head - axis * length - s.Bend * (curve * length);
            return s;
        }

        private static void AddBold(ref Ctx c, in InkStroke308 s, Vector3 focus, Vector3 planDirection, bool groundReach = false)
        {
            if (!InkBurstMeshBuilder308.AddStroke(c.B, s)) return;
            c.S.Bold++; Born(ref c, s.BirthCel);
            Vector3 tip = InkBurstMeshBuilder308.HeadTip(s) - focus;
            Vector3 flat = new Vector3(tip.x, 0f, tip.z);
            // an area plan's radius / length is a distance on the ground: the lifted heads are measured flat
            c.S.Reach = Mathf.Max(c.S.Reach, groundReach ? flat.magnitude : tip.magnitude);
            if (planDirection.sqrMagnitude > 1e-4f && flat.sqrMagnitude > 1e-4f) c.S.HalfAngleDeg = Mathf.Max(c.S.HalfAngleDeg, Vector3.Angle(planDirection, flat));
        }

        private static int BurstCel(ref Ctx c, int index, int count) =>
            c.Impact + (count <= 1 ? 0 : Mathf.Min(c.BurstCels - 1, Mathf.FloorToInt(index * (c.BurstCels - 1) / (float)(count - 1) + .001f)));

        // single attack: the main stroke rides the flight clock in cels, the burst opens at the target in the screen plane
        private static void SingleBurst(ref Ctx c, in DeployFormInput308 input, bool main)
        {
            var st = c.P.Stroke;
            Vector3 target = input.Target;
            float distance = Mathf.Max(.5f, target.magnitude);
            Vector3 axis = target.sqrMagnitude > 1e-6f ? target / distance : Vector3.forward;
            Vector3 atTarget = axis * distance;                        // advance vector of everything authored at the target
            int branches = Mathf.Max(0, c.Bold - (main ? 1 : 0));
            if (main && c.Bold > 0)
            {
                float length = Mathf.Min(c.Length, distance * .9f);
                Vector3 side = Vector3.Cross(c.ToCamera, axis);
                if (side.sqrMagnitude < 1e-4f) side = c.ScreenRight;
                side.Normalize();
                int birth = Mathf.Clamp(Mathf.Min(input.IgniteEndCel, c.Impact - 1), 0, c.Impact);
                var s = Stroke(ref c, target, axis, side, length, c.HalfWidth, birth);
                s.Advance = axis * Mathf.Max(0f, distance - length);    // the whole stroke rides: its head is the judged projectile
                AddBold(ref c, s, target, Vector3.zero);
                c.S.HasAdvance = true;
                Final(ref c, input, target, axis, side, length, atTarget);
            }
            // the screen-plane direction of the cast (straight-ahead casts fall back to screen right)
            Vector3 lead = axis - c.ToCamera * Vector3.Dot(axis, c.ToCamera);
            lead = lead.sqrMagnitude > .04f ? lead.normalized : c.ScreenRight;
            Vector3 leadUp = Vector3.Cross(c.ToCamera, lead).normalized;
            for (int i = 0; i < branches; i++)
            {
                float spread = st.SingleSpreadDeg * (.35f + .65f * (i + 1f) / branches) * (i % 2 == 0 ? 1f : -1f);
                float a = (spread + (i % 3 == 2 ? 180f : 0f)) * Mathf.Deg2Rad;
                Vector3 direction = lead * Mathf.Cos(a) + leadUp * Mathf.Sin(a);
                float length = c.Length * c.R.Range(.55f, 1.05f);
                var s = Stroke(ref c, target + direction * (length * .72f), direction, Vector3.Cross(c.ToCamera, direction).normalized, length, c.HalfWidth * c.R.Range(.8f, 1f), BurstCel(ref c, i + 1, branches + 1));
                s.Advance = atTarget;
                AddBold(ref c, s, target, Vector3.zero);
            }
            for (int i = 0; i < c.Fine; i++)
            {
                float a = c.R.Next() * Mathf.PI * 2f;
                Vector3 direction = lead * Mathf.Cos(a) + leadUp * Mathf.Sin(a);
                int birth = BurstCel(ref c, i, c.Fine);
                if (InkBurstMeshBuilder308.AddDryStroke(c.B, target + direction * c.R.Range(.12f, .5f), direction, Vector3.Cross(c.ToCamera, direction).normalized,
                    c.Length * st.FineLengthMul * c.R.Range(.6f, 1.2f), c.HalfWidth * st.FineWidthMul, (int)(c.R.Next() * 3.99f), birth, c.R.Next(), c.Tint, atTarget, 1f))
                { c.S.Fine++; Born(ref c, birth); }
            }
            NeedleStar(ref c, target, c.Needles, st.NeedleLength * .6f, st.NeedleLength * 1.3f, c.Impact, atTarget, 1f);
        }

        // the end treatment follows the jamo shape (at most 15 % of the main stroke)
        private static void Final(ref Ctx c, in DeployFormInput308 input, Vector3 head, Vector3 axis, Vector3 side, float length, Vector3 advance)
        {
            float tip = Mathf.Min(.15f, c.P.FinalOf(input.Final).TipShare) * length;
            if (tip <= .001f) return;
            float seed = c.R.Next();
            switch (input.Final)
            {
                case DeployFinal308.Giyeok:   // the end turns a corner
                    if (InkBurstMeshBuilder308.AddDryStroke(c.B, head, -c.ScreenUp, side, tip * 1.4f, c.HalfWidth * .55f, 0, c.Impact, seed, c.Tint, advance, 1f)) c.S.Fine++;
                    break;
                case DeployFinal308.Nieun:    // one drop beside the end
                    if (InkBurstMeshBuilder308.AddSprite(c.B, head + side * (tip + c.HalfWidth), c.ScreenRight, c.ScreenUp, tip * .5f, tip * .5f, InkBurstMeshBuilder308.CellDrop, c.Impact, seed, c.Tint * .5f, advance, 1f)) c.S.Sprites++;
                    break;
                case DeployFinal308.Mieum:    // a closed square smear
                    if (InkBurstMeshBuilder308.AddSprite(c.B, head + axis * tip * .4f, c.ScreenRight, c.ScreenUp, tip * .7f, tip * .7f, InkBurstMeshBuilder308.CellSquare, c.Impact, seed, c.Tint * .5f, advance, 1f)) c.S.Sprites++;
                    break;
                case DeployFinal308.Siot:     // two crossed needles
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Vector3 direction = (axis + side * (.7f * k)).normalized;
                        if (InkBurstMeshBuilder308.AddNeedle(c.B, head - direction * tip * .4f, direction, tip * 1.8f, c.P.Stroke.NeedleWidth * .5f, c.ToCamera, c.Impact, seed, advance, 1f)) c.S.Needles++;
                    }
                    break;
                case DeployFinal308.Ieung:    // a wet ring drop
                    if (InkBurstMeshBuilder308.AddSprite(c.B, head + axis * tip * .4f, c.ScreenRight, c.ScreenUp, tip * .75f, tip * .75f, InkBurstMeshBuilder308.CellRing, c.Impact, seed, c.Tint * .5f, advance, 1f)) c.S.Sprites++;
                    break;
            }
        }

        // a ground stroke is turned about its own axis to face the eye: a band lying flat is a sliver from eye height
        private static Vector3 FacingSide(ref Ctx c, Vector3 middle, Vector3 axis)
        {
            Vector3 side = Vector3.Cross(c.Camera - middle, axis);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(Vector3.up, axis);
            if (side.sqrMagnitude < 1e-4f) return c.ScreenRight;
            return side.normalized;
        }

        // long area strokes keep the proportion of a brush stroke (width follows length); `cap` keeps neighbours apart
        private static float AreaHalfWidth(ref Ctx c, float length, float cap)
        {
            float half = Mathf.Max(c.HalfWidth, length * c.P.Stroke.AreaWidthShare * .5f * c.E.WidthMul);
            return cap > 0f ? Mathf.Min(half, Mathf.Max(c.HalfWidth, cap)) : half;
        }

        // an area stroke from `start` on the ground to `head`: faces the eye, its lower edge stays on the ground, and an
        // upward bend swoops up to the head instead of burying the tail
        private static InkStroke308 GroundStroke(ref Ctx c, Vector3 start, Vector3 head, float halfWidth, float birth)
        {
            Vector3 chord = head - start;
            float length = Mathf.Max(.2f, chord.magnitude);
            Vector3 axis = chord / length;
            Vector3 side = FacingSide(ref c, (start + head) * .5f, axis);
            Vector3 lift = Vector3.up * (halfWidth * Mathf.Abs(side.y));
            var s = Stroke(ref c, head + lift, axis, side, length, halfWidth, birth);
            Vector3 run = chord - s.Bend * (s.Curve * length);
            float runLength = run.magnitude;
            if (runLength > .05f) { s.Axis = run / runLength; s.Curve = s.Curve * length / runLength; s.SCurve = s.SCurve * length / runLength; s.Length = runLength; }
            s.Tail = start + lift;
            // seen along its length from eye height the dry start would be all there is to see: the wet body takes most of it
            s.TailShare = Mathf.Min(s.TailShare, c.P.Stroke.AreaTailShareMax);
            return s;
        }

        // a sweep along a ground arc about `pivot`, from `from` to `to` (which may be lifted): the stroke's own bend draws the arc
        private static InkStroke308 ArcStroke(ref Ctx c, Vector3 pivot, Vector3 from, Vector3 to, float halfWidth, float birth)
        {
            Vector3 chord = to - from;
            Vector3 flat = new Vector3(chord.x, 0f, chord.z);
            float flatLength = Mathf.Max(.2f, flat.magnitude);
            flat /= flatLength;
            Vector3 outward = (from + to) * .5f - pivot; outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.Cross(flat, Vector3.up);
            Vector3 spoke = from - pivot; spoke.y = 0f;
            float sin = Mathf.Clamp(flatLength * .5f / Mathf.Max(.01f, spoke.magnitude), 0f, .9f), cos = Mathf.Sqrt(1f - sin * sin);
            Vector3 side = FacingSide(ref c, (from + to) * .5f + outward * (flatLength * .2f * sin), flat);
            Vector3 raise = Vector3.up * (halfWidth * Mathf.Abs(side.y));
            var s = Stroke(ref c, to + raise, flat, side, flatLength, halfWidth, birth);
            // parabola through both ends, leaving `from` along the arc's tangent: chord = tangent * L + bend * (curve * L)
            Vector3 run = (flat * cos + outward * sin) * (flatLength * cos) + Vector3.up * chord.y;
            float length = Mathf.Max(.2f, run.magnitude);
            s.Axis = run / length; s.Length = length;
            s.Bend = flat * sin - outward * cos;
            s.Curve = flatLength * sin / length; s.SCurve = 0f;
            s.Tail = from + raise;
            s.TailShare = Mathf.Min(s.TailShare, c.P.Stroke.AreaTailShareMax);
            return s;
        }

        // summon: bold strokes pulled straight up out of the ground ahead, the middle one first and tallest
        private static void Rise(ref Ctx c, in DeployFormInput308 input)
        {
            var st = c.P.Stroke;
            Vector3 foot = new Vector3(0f, input.OriginGroundY, st.SummonForward);
            Vector3 across = new Vector3(c.ScreenRight.x, 0f, c.ScreenRight.z);
            across = across.sqrMagnitude > 1e-4f ? across.normalized : Vector3.right;
            int count = input.Grade >= 2 ? 5 : 3;
            for (int i = 0; i < count; i++)
            {
                float k = i - (count - 1) * .5f;   // -1 0 1
                float height = st.ColumnHeight * Mathf.Max(.4f, 1f - .26f * Mathf.Abs(k)) * c.R.Range(.94f, 1.04f);
                Vector3 axis = (Vector3.up + across * (.14f * k)).normalized;
                Vector3 start = foot + across * (k * st.ColumnWidth * .32f);
                Vector3 side = FacingSide(ref c, start + axis * (height * .5f), axis);
                float half = Mathf.Max(c.HalfWidth, st.ColumnWidth * .2f) * (k == 0f ? 1f : .7f);
                AddBold(ref c, Stroke(ref c, start + axis * height, axis, side, height, half, c.Impact + Mathf.Min(c.BurstCels - 1, Mathf.RoundToInt(Mathf.Abs(k)) * 2)), foot, Vector3.zero);
            }
            NeedleStar(ref c, foot + Vector3.up * (st.ColumnHeight * .45f), Mathf.Max(4, c.Needles), st.NeedleLength * .35f, st.NeedleLength * .8f, c.Impact, Vector3.zero, 0f);
        }

        private static void Area(ref Ctx c, in DeployFormInput308 input, int ground)
        {
            var st = c.P.Stroke;
            Vector3 forward = new Vector3(input.PlanDirection.x, 0f, input.PlanDirection.z);
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            Vector3 centre = new Vector3(input.PlanPoint.x, input.GroundY + .06f, input.PlanPoint.z);
            int n = c.Bold;
            switch (input.Shape)
            {
                case AreaShape.Cone:
                {
                    float half = input.HalfAngleDeg > 0f ? input.HalfAngleDeg : 30f, reach = input.Length > 0f ? input.Length : c.Length * 2f;
                    float lift = Mathf.Min(st.AreaHeadLiftMax, reach * st.AreaHeadLift);
                    float gap = n > 1 ? reach * .66f / (n - 1) : reach;
                    // First person: a stroke lying along the view shows only its end. The fan is drawn as sweeps across the
                    // cone that roll outward, the nearest first; the last one's ends are the plan's reach at its half angle.
                    for (int i = 0; i < n; i++)
                    {
                        float radius = reach * (n == 1 ? 1f : Mathf.Lerp(.34f, 1f, i / (float)(n - 1)));
                        float from = i % 2 == 0 ? -half : half;   // the brush goes back and forth
                        Vector3 a = centre + Quaternion.AngleAxis(from, Vector3.up) * forward * radius;
                        Vector3 b = centre + Quaternion.AngleAxis(-from, Vector3.up) * forward * radius + Vector3.up * (lift * radius / reach);
                        // one brush for every sweep: the far ones are thinner on screen by distance alone, which is what reads as depth
                        float sweep = Mathf.Min(c.HalfWidth * st.AreaSweepWidthMul * c.R.Range(.8f, 1.1f), Mathf.Max(c.HalfWidth, gap * .42f));
                        AddBold(ref c, ArcStroke(ref c, centre, a, b, sweep, BurstCel(ref c, i, n)), centre, forward, true);
                        // each sweep leaves its own wet marks on the ground it crossed
                        for (int m = 0; m < 3; m++)
                        {
                            Vector3 d = Quaternion.AngleAxis(Mathf.Lerp(-half, half, (m + c.R.Range(.2f, .8f)) / 3f), Vector3.up) * forward;
                            Drop(ref c, centre + d * radius, Mathf.Clamp(sweep * c.R.Range(1.8f, 2.8f), .25f, 1.1f), m % 2 == 0 ? InkBurstMeshBuilder308.CellPuddle : InkBurstMeshBuilder308.CellCluster,
                                BurstCel(ref c, i, n), false, Vector3.zero, false);
                        }
                    }
                    Smear(ref c, centre + forward * (reach * .5f), Mathf.Min(c.P.Residue.ImpactSmear * 1.6f, reach * .25f));
                    Fine(ref c, centre, forward, half, reach * .35f, reach * .8f, true);
                    for (int i = 0; i < ground; i++)
                    {
                        float a = c.R.Range(-half, half) * Mathf.Deg2Rad, r = reach * Mathf.Sqrt(c.R.Next());
                        Vector3 d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * forward;
                        float size = DropSize(ref c, out int cell);
                        Drop(ref c, centre + d * r, size, cell, c.Impact + (int)(c.R.Next() * 3f), false, Vector3.zero, false);
                    }
                    break;
                }
                case AreaShape.Path:
                {
                    float reach = input.Length > 0f ? input.Length : c.Length * 3f, speed = Mathf.Max(.1f, input.Speed), cel = Mathf.Max(.01f, input.CelSeconds);
                    if (n > 0)
                    {
                        // one long stroke that lengthens along the corridor at the plan's speed, in cels
                        float wide = AreaHalfWidth(ref c, reach, Mathf.Max(input.Radius, c.HalfWidth));
                        Vector3 longSide = FacingSide(ref c, centre + forward * (reach * .5f), forward);
                        Vector3 raise = Vector3.up * (wide * Mathf.Abs(longSide.y));
                        var s = Stroke(ref c, centre + forward * reach + raise, forward, longSide, reach, wide, c.Impact);
                        s.Advance = forward * (reach * .85f); s.HeadOnly = true; s.Curve = 0f; s.SCurve *= .3f;
                        s.Tail = centre + raise;
                        s.TailShare = Mathf.Min(s.TailShare, st.AreaTailShareMax);
                        AddBold(ref c, s, centre, forward, true);
                        c.S.HasAdvance = c.S.HeadOnlyAdvance = true; c.S.AdvanceStartShare = .15f;
                    }
                    for (int i = 1; i < n; i++)
                    {
                        float at = reach * i / n;
                        Vector3 direction = Quaternion.AngleAxis(st.PathChevronDeg * (i % 2 == 0 ? 1f : -1f), Vector3.up) * forward;
                        // side strokes stay inside the corridor's length (the plan's reach is the long stroke's head)
                        float length = Mathf.Min(Mathf.Max(c.Length * .45f, input.Radius), Mathf.Max(.3f, (reach - at) * 1.1f));
                        AddBold(ref c, GroundStroke(ref c, centre + forward * at, centre + forward * at + direction * length + Vector3.up * Mathf.Min(st.AreaHeadLiftMax, length * st.AreaHeadLift),
                            AreaHalfWidth(ref c, length, 0f) * .8f, c.Impact + Mathf.CeilToInt(at / speed / cel)), centre, Vector3.zero);
                    }
                    for (int i = 0; i < ground; i++)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, forward);
                        float along = reach * c.R.Next();
                        float size = DropSize(ref c, out int cell);
                        Drop(ref c, centre + forward * along + side * (c.R.Range(-1f, 1f) * Mathf.Max(.3f, input.Radius)), size, cell, c.Impact + Mathf.CeilToInt(along / speed / cel), false, Vector3.zero, false);
                    }
                    break;
                }
                case AreaShape.Volley:
                {
                    // one short stroke per shot, born on that shot's cel
                    int shots = input.ShotPoints != null && input.ShotCels != null ? Mathf.Min(input.Shots, Mathf.Min(input.ShotPoints.Length, input.ShotCels.Length)) : 0;
                    Vector3 direction = (forward * .45f - Vector3.up).normalized;
                    Vector3 side = Vector3.Cross(c.ToCamera, direction);
                    side = side.sqrMagnitude > 1e-4f ? side.normalized : c.ScreenRight;
                    for (int i = 0; i < shots; i++)
                    {
                        var s = Stroke(ref c, input.ShotPoints[i], direction, side, Mathf.Min(1.2f, c.Length * .5f), c.HalfWidth * .7f, input.ShotCels[i]);
                        s.Segments = 5; s.Curve = 0f; s.SCurve = 0f; s.Tail = input.ShotPoints[i] - direction * s.Length;
                        AddBold(ref c, s, input.ShotPoints[i], Vector3.zero);
                        if (i < ground)
                        {
                            float size = DropSize(ref c, out int cell);
                            Drop(ref c, new Vector3(input.ShotPoints[i].x, input.GroundY, input.ShotPoints[i].z), size, cell, input.ShotCels[i], false, Vector3.zero, false);
                        }
                    }
                    break;
                }
                default:
                {
                    // Circle (and an area row without a plan): a star whose stroke tips are the radius
                    float radius = input.Radius > 0f ? input.Radius : 3f;
                    float phase = c.R.Next() * 360f;
                    // a ring burst is a crown: the heads fly up and out, so it reads from eye height at any bearing
                    float lift = Mathf.Min(st.AreaHeadLiftMax, radius * st.AreaCrownLift);
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 direction = Quaternion.AngleAxis(phase + (i + c.R.Range(-.25f, .25f)) * 360f / Mathf.Max(1, n), Vector3.up) * Vector3.forward;
                        float tip = radius * (1f - .04f * c.R.Next());
                        AddBold(ref c, GroundStroke(ref c, centre + direction * (tip * .12f), centre + direction * tip + Vector3.up * lift, AreaHalfWidth(ref c, tip * .88f, 0f) * 1.3f, BurstCel(ref c, i, n)), centre, Vector3.zero, true);
                    }
                    Smear(ref c, centre, Mathf.Min(c.P.Residue.ImpactSmear * 1.6f, radius * .6f));
                    Fine(ref c, centre, Vector3.forward, 180f, radius * .3f, radius * .85f, false);
                    for (int i = 0; i < ground; i++)
                    {
                        float a = c.R.Next() * Mathf.PI * 2f, r = radius * Mathf.Sqrt(c.R.Next());
                        float size = DropSize(ref c, out int cell);
                        Drop(ref c, centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), size, cell, c.Impact + (int)(c.R.Next() * 3f), false, Vector3.zero, false);
                    }
                    break;
                }
            }
            // needle lines from the plan point, standing in the screen plane
            Vector3 star = input.Shape == AreaShape.Cone || input.Shape == AreaShape.Path ? AreaFocus(input, c.Length) : centre + Vector3.up * .5f;
            NeedleStar(ref c, star, c.Needles, st.NeedleLength * .5f, st.NeedleLength * 1.1f, c.Impact, Vector3.zero, 0f);
        }

        private static void Fine(ref Ctx c, Vector3 centre, Vector3 forward, float halfAngle, float from, float to, bool across)
        {
            var st = c.P.Stroke;
            for (int i = 0; i < c.Fine; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(c.R.Range(-halfAngle, halfAngle), Vector3.up) * forward;
                int birth = BurstCel(ref c, i, c.Fine);
                if (across)
                {
                    // short dry sweeps between the arcs, across the view
                    float at = c.R.Range(from, to), sweep = Mathf.Max(.3f, at * c.R.Range(.18f, .34f));
                    Vector3 axis = Vector3.Cross(Vector3.up, direction) * c.R.Sign();
                    Vector3 root = centre + direction * at - axis * (sweep * .5f);
                    float thin = AreaHalfWidth(ref c, sweep, 0f) * st.FineWidthMul;
                    Vector3 face = FacingSide(ref c, root + axis * (sweep * .5f), axis);
                    if (InkBurstMeshBuilder308.AddDryStroke(c.B, root + Vector3.up * (thin * Mathf.Abs(face.y) + .1f), axis, face, sweep, thin,
                        (int)(c.R.Next() * 3.99f), birth, c.R.Next(), c.Tint, Vector3.zero, 0f))
                    { c.S.Fine++; Born(ref c, birth); }
                    continue;
                }
                float start = c.R.Range(from * .3f, from), length = Mathf.Max(.2f, c.R.Range(from, to) - start);
                float fine = AreaHalfWidth(ref c, length, 0f) * st.FineWidthMul;
                Vector3 side = FacingSide(ref c, centre + direction * (start + length * .5f), direction);
                if (InkBurstMeshBuilder308.AddDryStroke(c.B, centre + direction * start + Vector3.up * (fine * Mathf.Abs(side.y)), direction, side, length, fine,
                    (int)(c.R.Next() * 3.99f), birth, c.R.Next(), c.Tint, Vector3.zero, 0f))
                { c.S.Fine++; Born(ref c, birth); }
            }
        }

        // buff: thin strokes wind once round the body; in first person they pass along the lower edge of the view
        private static void Wind(ref Ctx c, in DeployFormInput308 input, SpellDeploy308ProfileSO.GradeSet grade)
        {
            int ribbons = input.Grade >= 1 ? 2 : 1;
            for (int i = 0; i < ribbons; i++)
            {
                float y0 = input.Camera.y - 1.25f + i * .3f, y1 = input.Camera.y - .5f + i * .15f;
                int made = InkBurstMeshBuilder308.AddRibbon(c.B, new Vector3(input.Camera.x, 0f, input.Camera.z), c.P.Stroke.NearFade + .05f + i * .12f, y0, y1,
                    i % 2 == 0 ? 1f : -1f, grade.Width * c.P.Stroke.FineWidthMul * .5f, 14, 0, c.Impact, c.BurstCels / 14f, c.R.Next(), c.Tint);
                if (made <= 0) continue;
                c.S.Ribbons++; c.S.Fine += made; Born(ref c, c.Impact);
            }
        }

        // summon: a flat column that rises and drains; ward: a ring of strips that rises, sinks low while the ward stands, drains at the end
        private static void Column(ref Ctx c, in DeployFormInput308 input, SpellDeploy308ProfileSO.TierSet tier, bool ward)
        {
            var st = c.P.Stroke;
            int strips = Mathf.Min(st.ColumnStrips, tier.ColumnStrips);
            int bundle = Mathf.Clamp(strips / 3, 5, 9);   // summon: a bundle of bold rising strokes, not a comb
            float seed = c.R.Next();
            c.S.HasColumn = true; c.S.ColumnHolds = ward;
            if (!ward)
            {
                Vector3 flat = new Vector3(c.ScreenRight.x, 0f, c.ScreenRight.z);
                flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.right;
                c.S.ColumnStrips += InkBurstMeshBuilder308.AddColumn(c.B, new Vector3(0f, input.OriginGroundY, st.SummonForward), flat, Vector3.up, st.ColumnWidth, st.ColumnHeight, bundle, c.Impact, seed, .8f);
                Born(ref c, c.Impact);
                return;
            }
            float radius = input.WardRadius > 0f ? input.WardRadius : 3f, height = input.WardHeight > 0f ? input.WardHeight : st.ColumnHeight;
            // the ring stands all round the caster: kept below eye height so an enemy tell behind it stays readable while it rises
            if (st.WardColumnMaxHeight > 0f) height = Mathf.Min(height, st.WardColumnMaxHeight);
            float width = 2f * Mathf.PI * radius / strips;
            for (int i = 0; i < strips; i++)
            {
                float a = (i + .5f) / strips * Mathf.PI * 2f;
                Vector3 at = new Vector3(Mathf.Cos(a) * radius, input.OriginGroundY, Mathf.Sin(a) * radius);
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                c.S.ColumnStrips += InkBurstMeshBuilder308.AddColumn(c.B, at, tangent, Vector3.up, width, height, 1, c.Impact, Mathf.Repeat(seed + i * .137f, 1f));
            }
            c.S.Reach = radius;
            Born(ref c, c.Impact);
        }
    }
}
