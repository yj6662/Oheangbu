using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public enum PresentOpKind308 { Cast, Stamp, Air, Body, EndBody, ReleaseHeld }

    /// <summary>The transform an op is tied to; the stage resolves it (the rules never hold a scene object).</summary>
    public enum PresentRef308 { None, RequestTarget, CueTarget }

    /// <summary>One thing the stage does through the deploy layer's host. Every number is already resolved.</summary>
    public struct PresentOp308
    {
        public PresentOpKind308 Kind;
        public float Delay;                 // seconds after the event (a ring is laid when its zone forms, a fling's drops leave one after another)
        // Cast: one deploy burst (InkDeployRuntime308)
        public DeployCategory308 Category;
        public bool Triggered, UsePlan;
        public float HoldSeconds, WardRadius;   // HoldSeconds > 0 only for a standing wall (Stand)
        public PresentRef308 Target;        // cast: the judged target; body: who carries it
        public Vector3 Origin, Point;       // cast: origin and fallback point; stamp / air: where; body: where it points
        // Stamp: a ground mark
        public Vector3 Forward;
        public float Size, Life, Opacity;
        public int Cell;
        public bool Held;                   // stays wet until the handle releases it, then dries in Life seconds
        public float TailMax;               // air: > 0 = this tailed drop is drawn at most that many sizes long (pass 4b Q6: the thrown head)
        // Air: an airborne sprite
        public Vector3 Velocity;
        public float Gravity, LandY;
        // Body: a composed form (InkPresentForms308)
        public PresentBody308 Body;
        public float BodyHold;              // seconds after its birth beat before it melts; below 0 = carried until Unmark
        public bool Hit;
        public float Reach;                 // Slash: metres to where the strike went; ZoneRing: the zone's radius (Point = its centre)
        // fix pass 4b (review S6): Air - this drop takes the sheet's least on-screen size (thrown ink and a zone's sustained drops
        // only: a hit's splash and every other air sprite keep the size they were accepted with)
        public bool Floor;
    }

    /// <summary>A presenter call in plain values. The stage fills it from the request, the cue and the scene.</summary>
    public struct PresentEvent308
    {
        public SpellFxRole Role;
        public PresentMoment308 Moment;
        public SpellFxCue Cue;
        public DeployCategory308 MapCategory;
        public Element Element;
        public DeployTier308 Tier;
        public int Grade, Seed;
        // the request
        public Vector3 Origin, FallbackPoint, TargetPoint;
        public bool HasTarget, HasPlan;
        public float ImpactClock, Duration, Radius;
        // the cue
        public Vector3 Point, Normal, CueTargetPoint;
        public bool CueHasTarget;
        public int TargetCount;
        public Vector3[] TargetPoints;      // caller-owned
        public float At, Value, Now;
        // the scene as the stage reads it
        public Vector3 Eye, EyeForward, CasterFeet;
        public float GroundY;               // the ground under the cue's point
        public DeployDrop308[] HitScratch;  // caller-owned, InkDeployForms308.MaxHitDrops long
    }

    public struct PresentPlan308
    {
        public int Count;
        public bool Routed;                 // false = no route: nothing is drawn and the call is counted
        public bool Ends;
        public PresentRoute308 Route;
    }

    // #308 present add-on: the PURE half of the spell presenter on the deploy layer. A presenter call (role + Begin / cue /
    // End, in plain values) becomes a list of ops by the sheet's route. No scene object, no clock, no random state of its
    // own: the same event always gives the same ops, so the offline runner executes every route (pure308_present.py).
    // What the ops may ask for is bounded here:
    //   - a ground mark lives SpellLife (3-5 s by the layer); a held one dries in FieldTail (3 s) once released,
    //   - an airborne sprite lives at most SpellPresent308SheetSO.MaxAirSeconds,
    //   - a cast takes the layer's own three beats; only a standing wall (Stand) is given a hold, and it does not glow,
    //   - nothing here asks for glow: a stroke glows on its birth cels by the layer's own rule, marks and drops never do,
    //   - forms3 (D9): a zone's ring stroke (Body ZoneRing) is held by its handle like the held ground marks - it goes with the
    //     same Release - and never longer than the sheet's RingSeconds,
    //   - forms3 (D10): thrown ink and a zone step's drops are the TAILED drop cell; the residue field draws that cell along
    //     the drop's own velocity (the op only names the cell: where it flies is unchanged).
    public static class SpellPresentRules308
    {
        public const int NoSplash = -1, DripOnly = -2;
        const float StampLift = .5f;        // the field looks for the ground below the point it is given

        public static bool IsAttack(DeployCategory308 category) => category == DeployCategory308.AttackSingle || category == DeployCategory308.AttackArea;

        /// <summary>Is this glyph shown by the deploy presenter? The layer's own data switch: the layer is on, the letter is one
        /// of its enabled letters, and the map knows it. Everything else goes to the interim presenter.</summary>
        public static bool Uses(SpellDeploy308ProfileSO profile, char letter) =>
            profile != null && profile.Map != null && profile.IsEnabled(letter) && profile.Map.TryGet(letter, out _);

        /// <summary>Does the layer show this glyph's enemy hit itself (so the KTP contact stays out)? The layer's own rule
        /// (an enabled, retired attack row), plus the enabled buff and install rows whose hits this presenter shows.</summary>
        public static bool OwnsHit(SpellDeploy308ProfileSO profile, SpellPresent308SheetSO sheet, char letter)
        {
            if (InkDeployForms308.OwnsHitContact(profile, letter)) return true;
            if (profile == null || sheet == null || profile.Map == null || !profile.Hit.Enabled || !profile.Hit.ReplaceLegacyContact || !profile.IsEnabled(letter)) return false;
            if (!profile.Map.TryGet(letter, out var row)) return false;
            return (row.Category == DeployCategory308.Buff && sheet.Splash.OwnBuffRows) || (row.Category == DeployCategory308.ComboInstall && sheet.Splash.OwnInstallRows);
        }

        /// <summary>The hit splash of a confirmed hit no live cast took: its grade (0..2), DripOnly, or NoSplash. Only for a glyph
        /// whose hits the layer owns, and never for a hit a live cast already splashed.</summary>
        public static int FreeSplash(SpellPresent308SheetSO sheet, PresentHitSource308 source, bool routed, bool owned)
        {
            if (sheet == null || !sheet.Splash.Free || routed || !owned) return NoSplash;
            switch (source)
            {
                case PresentHitSource308.Direct: return Mathf.Clamp(sheet.Splash.DirectGrade, 0, 2);
                case PresentHitSource308.Companion: return Mathf.Clamp(sheet.Splash.CompanionGrade, 0, 2);
                case PresentHitSource308.Retaliation: return Mathf.Clamp(sheet.Splash.RetaliationGrade, 0, 2);
                case PresentHitSource308.Harmony: return Mathf.Clamp(sheet.Splash.HarmonyGrade, 0, 2);
                case PresentHitSource308.Persistent: return sheet.Splash.PersistentDrip ? DripOnly : NoSplash;
                default: return NoSplash;
            }
        }

        /// <summary>What an enemy's own state reads as. Blocked actions (a freeze, a bind, an interrupt's stun) come before a
        /// slow; taking more damage and a lowered defence are one reading; weaker strikes are another.</summary>
        public static PresentState308 StateOf(SpellPresent308SheetSO sheet, bool blocksActions, float moveScale, float takenScale, float defenceShred, float outgoingScale)
        {
            var state = PresentState308.None;
            if (blocksActions) state |= PresentState308.Bound;
            else if (moveScale < (sheet != null ? sheet.Mark.SlowBelow : 1f)) state |= PresentState308.Slowed;
            if (takenScale > 1f || defenceShred > 0f) state |= PresentState308.Exposed;
            if (outgoingScale < 1f) state |= PresentState308.Weakened;
            return state;
        }

        public static DeployCategory308 CastCategory(PresentRoute308 route, DeployCategory308 mapCategory)
        {
            if (route == null) return mapCategory;
            if (route.CastAs == PresentCastAs308.Always) return route.Category;
            if (route.CastAs == PresentCastAs308.WhenNotAttack && !IsAttack(mapCategory)) return route.Category;
            return mapCategory;
        }

        /// <summary>Did the cast ask to outlive its impact (an ember that sits in its carrier, a shot that returns)?</summary>
        public static bool Lasting(SpellPresent308SheetSO sheet, in PresentEvent308 e) => e.Duration > e.ImpactClock + (sheet != null ? sheet.Pool.LastingSlack : 0f);

        /// <summary>Seconds a body may stay after its birth beat: its whole life never passes the sheet's ceiling.</summary>
        public static float BodyHold(SpellPresent308SheetSO sheet, SpellDeploy308ProfileSO profile, float asked) =>
            Mathf.Clamp(asked, 0f, Mathf.Max(0f, SpellPresent308SheetSO.MaxBodySeconds - sheet.Pool.BodyBeat - profile.Beats.Melt));

        public static PresentPlan308 Plan(SpellPresent308SheetSO sheet, SpellDeploy308ProfileSO profile, in PresentEvent308 e, PresentOp308[] ops)
        {
            var plan = new PresentPlan308();
            if (sheet == null || profile == null || ops == null) return plan;
            var route = sheet.Find(e.Role, e.Moment, e.Cue);
            if (route == null) return plan;
            plan.Routed = true; plan.Route = route; plan.Ends = route.Ends || e.Moment == PresentMoment308.End;
            int n = 0;
            var acts = route.Acts;
            for (int i = 0; acts != null && i < acts.Length; i++)
            {
                int seed = e.Seed + i * 7919;
                switch (acts[i])
                {
                    case PresentAct308.Cast:
                    {
                        var category = CastCategory(route, e.MapCategory);
                        Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Cast, Category = category, UsePlan = true,
                            Target = e.HasTarget ? PresentRef308.RequestTarget : PresentRef308.None, Origin = CastOrigin(sheet, e, category), Point = e.FallbackPoint });
                        break;
                    }
                    case PresentAct308.Stand:
                        Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Cast, Category = CastCategory(route, e.MapCategory), HoldSeconds = sheet.StandSeconds(e.Duration),
                            WardRadius = Mathf.Max(sheet.Ring.MinRadius, e.Radius), Origin = e.Origin, Point = e.FallbackPoint });
                        break;
                    case PresentAct308.Trigger:
                    {
                        // the burst opens at the target in the screen plane: its origin stands a step toward the eye
                        Vector3 toEye = e.Eye - e.Point;
                        toEye = toEye.sqrMagnitude > 1e-4f ? toEye.normalized : -Forward(e);
                        Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Cast, Category = e.MapCategory, Triggered = true,
                            Target = e.CueHasTarget ? PresentRef308.CueTarget : PresentRef308.None, Origin = e.Point + toEye, Point = e.Point });
                        break;
                    }
                    case PresentAct308.HeldRing: Ring(ops, ref n, sheet, profile, e.FallbackPoint, e.Radius, seed, Mathf.Min(Mathf.Max(0f, e.ImpactClock), SpellPresent308SheetSO.MaxAirSeconds), e.Tier); break;
                    case PresentAct308.HeldRingAtCue: Ring(ops, ref n, sheet, profile, e.Point, e.Value > 0f ? e.Value : e.Radius, seed, 0f, e.Tier); break;
                    case PresentAct308.Release: Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.ReleaseHeld }); break;
                    case PresentAct308.FlingToTarget:
                        if (e.HasTarget) Fling(ops, ref n, sheet, profile, Brush(sheet, e), e.TargetPoint, e.ImpactClock);
                        break;
                    case PresentAct308.FlingToCueTargets:
                    {
                        int count = e.TargetPoints != null ? Mathf.Min(e.TargetCount, Mathf.Min(e.TargetPoints.Length, sheet.Fling.TargetsMax)) : 0;
                        for (int k = 0; k < count; k++) Fling(ops, ref n, sheet, profile, e.Point, e.TargetPoints[k], Flight(sheet, e, e.Point, e.TargetPoints[k]));
                        if (count == 0 && e.CueHasTarget) Fling(ops, ref n, sheet, profile, e.Point, e.CueTargetPoint, Flight(sheet, e, e.Point, e.CueTargetPoint));
                        else if (count == 0 && e.Value > 0f && Flat(e.Normal).sqrMagnitude > 1e-4f)
                        {
                            // derived shots that found nobody still leave: mirrored about the shot's direction by the cue's angle
                            Vector3 along = Flat(e.Normal).normalized;
                            for (int side = -1; side <= 1; side += 2)
                            {
                                Vector3 to = e.Point + Turn(along, e.Value * side) * sheet.Fling.AirDistance;
                                Fling(ops, ref n, sheet, profile, e.Point, to, sheet.Fling.AirDistance / Mathf.Max(1f, sheet.Fling.Speed));
                            }
                        }
                        break;
                    }
                    case PresentAct308.FlingToCaster:
                    {
                        // what comes back comes back to the brush
                        Vector3 to = Brush(sheet, e);
                        Fling(ops, ref n, sheet, profile, e.Point, to, Flight(sheet, e, e.Point, to));
                        break;
                    }
                    case PresentAct308.FlingFromCaster:
                    {
                        // a companion shot's cue carries its flight time (seconds from now), not a clock
                        Vector3 from = Brush(sheet, e);
                        Fling(ops, ref n, sheet, profile, from, e.Point, e.At > 0f ? e.At : Vector3.Distance(from, e.Point) / Mathf.Max(1f, sheet.Fling.Speed));
                        break;
                    }
                    case PresentAct308.MarkCarried:
                        if (e.HasTarget && (!route.OnlyIfLasting || Lasting(sheet, e)))
                            Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Body, Body = PresentBody308.MarkCarried, Target = PresentRef308.RequestTarget, BodyHold = -1f });
                        break;
                    case PresentAct308.MarkCueCarrier:
                        if (e.CueHasTarget && (!route.OnlyIfLasting || Lasting(sheet, e)))
                            Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Body, Body = PresentBody308.MarkCarried, Target = PresentRef308.CueTarget, BodyHold = -1f });
                        break;
                    case PresentAct308.MarkInstall:
                        if (e.CueHasTarget) Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Body, Body = PresentBody308.MarkInstall, Target = PresentRef308.CueTarget, BodyHold = -1f });
                        break;
                    case PresentAct308.Unmark: Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.EndBody }); break;
                    case PresentAct308.Splash: Splash(ops, ref n, sheet, profile, e.Point, e.Eye, e.GroundY, e.Element, sheet.Splash.PointGrade, e.Tier, seed, e.HitScratch); break;
                    case PresentAct308.Shed: Shed(ops, ref n, sheet, profile, e, seed, false); break;
                    case PresentAct308.ShedSpent: Shed(ops, ref n, sheet, profile, e, seed, true); break;
                    case PresentAct308.Drip:
                        Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Stamp, Point = e.CasterFeet + Forward(e) * profile.Stroke.NearFade + Vector3.up * StampLift, Forward = Forward(e),
                            Size = Piece(profile, sheet.Shed.FeetMark), Cell = InkBurstMeshBuilder308.CellPuddle, Life = profile.SpellLife, Opacity = profile.Residue.Opacity });
                        break;
                    case PresentAct308.Blade:
                        Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Body, Body = PresentBody308.Blade, BodyHold = BodyHold(sheet, profile, sheet.Sword.BladeHold) });
                        break;
                    case PresentAct308.Slash:
                    case PresentAct308.SlashHit:
                    {
                        float reach = Vector3.Distance(Flat(e.Eye), Flat(e.Point));
                        Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Body, Body = PresentBody308.Slash, Point = e.Point, Hit = acts[i] == PresentAct308.SlashHit,
                            Reach = reach > .5f ? reach : sheet.Sword.SlashReach, BodyHold = BodyHold(sheet, profile, sheet.Sword.SlashHold) });
                        break;
                    }
                    default: break;   // Silent: the route's Why says what shows the moment instead
                }
            }
            plan.Count = n;
            return plan;
        }

        // ---- compositions (also called by the stage for what no cue announces: the free hit splash, the drip of a zone's step)

        /// <summary>The layer's hit splash (InkDeployForms308.ComposeHit) as ops: air drops, the standing star and blot, the puddle.</summary>
        public static void Splash(PresentOp308[] ops, ref int n, SpellPresent308SheetSO sheet, SpellDeploy308ProfileSO profile, Vector3 point, Vector3 eye, float groundY,
            Element element, int grade, DeployTier308 tier, int seed, DeployDrop308[] scratch)
        {
            if (scratch == null || sheet == null || profile == null) return;
            int count = InkDeployForms308.ComposeHit(new DeployHitInput308 { Profile = profile, Tier = tier, Element = element, Grade = Mathf.Clamp(grade, 0, 2), Point = point,
                ToEye = eye - point, GroundY = groundY, Seed = seed }, scratch);
            Vector3 forward = Flat(point - eye);
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            for (int i = 0; i < count; i++)
            {
                var drop = scratch[i];
                if (drop.Air)
                    Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Air, Point = drop.Local, Velocity = drop.Velocity, Size = drop.Size, Cell = drop.Cell,
                        Life = sheet.AirSeconds(drop.Life > 0f ? drop.Life : 1.6f), Gravity = drop.Still ? 0f : 1f, LandY = groundY });
                else
                    Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Stamp, Point = drop.Local + Vector3.up * StampLift, Forward = forward, Size = drop.Size, Cell = drop.Cell,
                        Life = profile.SpellLife, Opacity = profile.Residue.Opacity });
            }
        }

        /// <summary>A zone's step on an enemy: a few drops fall off it (no star, no puddle).</summary>
        public static void DripAt(PresentOp308[] ops, ref int n, SpellPresent308SheetSO sheet, SpellDeploy308ProfileSO profile, Vector3 point, float groundY, Element element, int seed)
        {
            if (sheet == null || profile == null) return;
            var rng = new DeployRng308(seed);
            float wet = profile.ElementOf(element).DropSizeMul;
            var set = sheet.Splash;
            for (int i = 0; i < set.DripDrops; i++)
            {
                float a = rng.Next() * Mathf.PI * 2f;
                var spoke = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Air, Delay = i * Mathf.Max(0f, set.DripStagger), Point = point + spoke * set.DripSpread + Vector3.up * (set.DripRise != null && set.DripRise.Length > 0 ? set.DripRise[i % set.DripRise.Length] : 0f),
                    Velocity = spoke * rng.Range(set.DripOut.x, set.DripOut.y) + Vector3.up * rng.Range(set.DripUp.x, set.DripUp.y), Size = profile.Residue.DropSmall * wet * rng.Range(set.DripSize.x, set.DripSize.y),
                    Cell = i < set.DripTailed ? InkBurstMeshBuilder308.CellDropTailed : InkBurstMeshBuilder308.CellDrop, Life = sheet.AirSeconds(1.6f), Gravity = 1f, LandY = groundY, Floor = true });
            }
        }

        // held ground marks: a ring at the radius (what the player must read is where the zone ends) and one mark at the centre
        static void Ring(PresentOp308[] ops, ref int n, SpellPresent308SheetSO sheet, SpellDeploy308ProfileSO profile, Vector3 centre, float radius, int seed, float delay, DeployTier308 tier)
        {
            float r = Mathf.Max(sheet.Ring.MinRadius, radius);
            int count = sheet.RingCount(r, tier);   // fewer marks on the Mobile tier: its residue field is small
            var rng = new DeployRng308(seed);
            for (int i = 0; i < count; i++)
            {
                float a = (i + rng.Range(-.25f, .25f)) / count * Mathf.PI * 2f;
                var spoke = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                int pick = i % 3;
                Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Stamp, Delay = delay, Point = centre + spoke * r + Vector3.up * StampLift, Forward = new Vector3(-spoke.z, 0f, spoke.x),
                    Size = Piece(profile, sheet.Ring.Drop * rng.Range(.85f, 1.15f)), Cell = pick == 0 ? InkBurstMeshBuilder308.CellCluster : pick == 1 ? InkBurstMeshBuilder308.CellDrop : InkBurstMeshBuilder308.CellPuddle,
                    Life = profile.FieldTail, Opacity = profile.Residue.Opacity, Held = true });
            }
            Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Stamp, Delay = delay, Point = centre + Vector3.up * StampLift, Forward = Vector3.forward, Size = Piece(profile, sheet.Ring.Centre),
                Cell = InkBurstMeshBuilder308.CellRing, Life = profile.FieldTail, Opacity = profile.Residue.Opacity, Held = true });
            // forms3 (D9): the edge itself - one ring stroke lying on the ground, laid at the same moment, held by the handle
            if (sheet.Ring.Body)
                Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Body, Body = PresentBody308.ZoneRing, Delay = delay, Point = centre, Reach = r, BodyHold = -1f });
        }

        /// <summary>forms3 fix pass (review S1): which ring stroke melts to make room for a new one. `ring[i]` = body slot i holds a
        /// ring stroke that is not melting yet, `started[i]` = when it was shown. Returns the oldest of them when sheet.RingBodies
        /// are alive already, else -1 (there is room). Pure.</summary>
        public static int RingToRelease(SpellPresent308SheetSO sheet, bool[] ring, float[] started, int count)
        {
            if (sheet == null || ring == null || started == null) return -1;
            int alive = 0, oldest = -1;
            for (int i = 0; i < count && i < ring.Length && i < started.Length; i++)
            {
                if (!ring[i]) continue;
                alive++;
                if (oldest < 0 || started[i] < started[oldest]) oldest = i;
            }
            return alive >= sheet.RingBodies ? oldest : -1;
        }

        // ink thrown from one point to another: a few drops leaving one after another, arriving `seconds` later
        static void Fling(PresentOp308[] ops, ref int n, SpellPresent308SheetSO sheet, SpellDeploy308ProfileSO profile, Vector3 from, Vector3 to, float seconds)
        {
            float flight = sheet.AirSeconds(Mathf.Max(sheet.Fling.MinSeconds, seconds));
            Vector3 path = to - from;
            float length = path.magnitude;
            if (length < .05f) return;
            float pull = profile.Residue.Gravity * Mathf.Clamp01(sheet.Fling.Gravity);
            // the arc lands on `to`: the rise it starts with is what the pull takes back over the flight
            Vector3 velocity = path / flight + Vector3.up * (.5f * pull * flight);
            // (fix pass 4b: a slow throw's drops still leave as one train - never more than StepMax s apart)
            float step = Mathf.Min(sheet.Fling.Spacing / Mathf.Max(.5f, length / flight), Mathf.Max(.01f, sheet.Fling.StepMax));
            int drops = sheet.FlingDrops;
            for (int i = 0; i < drops; i++)
                // fix pass 4b (director 4: three dashes at one height were a dotted line): the followers leave a little over and under the head's line, turn about
                Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Air, Delay = i * step, Point = from + Vector3.up * (i == 0 ? 0f : sheet.Fling.Scatter * FlingStep(sheet, i)), Velocity = velocity, Size = sheet.Fling.Size * Mathf.Max(.2f, 1f - sheet.Fling.SizeStep * i), Floor = true,
                    TailMax = i < sheet.Fling.Tailed ? Mathf.Max(0f, sheet.Fling.HeadTailMax) : 0f,   // pass 4b (Q6): the thrown head is no long streak
                    // forms3 (D10): the leading drops are tailed drops (drawn along their flight), the last ones spatter; Tailed 0 = the old round first drop
                    Cell = i < sheet.Fling.Tailed ? InkBurstMeshBuilder308.CellDropTailed : i == 0 || sheet.Fling.FollowRound ? InkBurstMeshBuilder308.CellDrop : InkBurstMeshBuilder308.CellCluster, Life = flight,
                    Gravity = Mathf.Max(.0001f, Mathf.Clamp01(sheet.Fling.Gravity)),
                    LandY = Mathf.Min(from.y, to.y) - 100f });
        }

        // pass 4b (Q6): the i-th follower's height off the head's line, as a share of Fling.Scatter - the sheet's written order
        // (a missing order = over and under, turn about, as before)
        static float FlingStep(SpellPresent308SheetSO sheet, int i)
        {
            var steps = sheet.Fling.ScatterSteps;
            return steps != null && steps.Length > 0 ? Mathf.Clamp(steps[(i - 1) % steps.Length], -1f, 1f) : i % 2 == 1 ? 1f : -1f;
        }

        // the end cue of what the caster carried: drops fall off in front of the eye and leave a mark at the feet
        static void Shed(PresentOp308[] ops, ref int n, SpellPresent308SheetSO sheet, SpellDeploy308ProfileSO profile, in PresentEvent308 e, int seed, bool spent)
        {
            var set = sheet.Shed;
            var rng = new DeployRng308(seed);
            Vector3 forward = Forward(e);
            float wet = profile.ElementOf(e.Element).DropSizeMul;
            // nothing is drawn inside the near fade of the first-person view
            float ahead = Mathf.Max(set.Ahead, profile.Stroke.NearFade + .1f);
            for (int i = 0; i < set.Drops; i++)
            {
                Vector3 spoke = Turn(forward, Mathf.Lerp(-set.SpreadDeg * .5f, set.SpreadDeg * .5f, (i + rng.Next()) / Mathf.Max(1, set.Drops)));
                Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Air, Point = e.Eye + spoke * ahead - Vector3.up * set.Below, Velocity = spoke * rng.Range(.2f, .9f) + Vector3.up * rng.Range(-.3f, .4f),
                    Size = set.Size * wet * rng.Range(.8f, 1.5f), Cell = rng.Next() < .3f ? InkBurstMeshBuilder308.CellCluster : InkBurstMeshBuilder308.CellDrop, Life = sheet.AirSeconds(1.6f),
                    Gravity = 1f, LandY = e.CasterFeet.y });
            }
            Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Stamp, Point = e.CasterFeet + forward * ahead + Vector3.up * StampLift, Forward = forward, Size = Piece(profile, set.FeetMark),
                Cell = InkBurstMeshBuilder308.CellRing, Life = profile.SpellLife, Opacity = profile.Residue.Opacity });
            if (spent)
                Add(ops, ref n, new PresentOp308 { Kind = PresentOpKind308.Air, Point = e.Eye + forward * ahead - Vector3.up * (set.Below * .5f), Size = set.SpentStar, Cell = InkBurstMeshBuilder308.CellStar,
                    Life = sheet.AirSeconds(set.SpentStarSeconds), Gravity = 0f, LandY = e.CasterFeet.y });
        }

        /// <summary>Where the brush tip is taken to be: ahead of the eye along the view, a little below it. The handlers give the
        /// caster's feet as a request's origin; in first person a stroke born there would start under the view.</summary>
        public static Vector3 Brush(SpellPresent308SheetSO sheet, in PresentEvent308 e)
        {
            Vector3 view = e.EyeForward.sqrMagnitude > 1e-6f ? e.EyeForward.normalized : Vector3.forward;
            return e.Eye + view * Mathf.Max(.5f, sheet.Pool.BrushReach) - Vector3.up * Mathf.Clamp01(sheet.Pool.BrushDrop);
        }

        /// <summary>Where a burst is rooted. The forms the layer draws from the brush (an attack's ignition and flight, a buff, an
        /// install) start at the brush tip - the area itself is the plan's; the forms it draws from the ground (a rising tree, a
        /// standing wall, a field) stay where the request puts them.</summary>
        public static Vector3 CastOrigin(SpellPresent308SheetSO sheet, in PresentEvent308 e, DeployCategory308 category)
        {
            bool fromGround = category == DeployCategory308.Summon || category == DeployCategory308.Ward || category == DeployCategory308.Field;
            return fromGround ? e.Origin : Brush(sheet, e);
        }

        // seconds a thrown drop flies: until the clock the cue names, else by the sheet's speed
        static float Flight(SpellPresent308SheetSO sheet, in PresentEvent308 e, Vector3 from, Vector3 to) =>
            e.At > e.Now ? e.At - e.Now : Vector3.Distance(from, to) / Mathf.Max(1f, sheet.Fling.Speed);

        // one ground mark = one slot of the residue field (a mark above the field's piece size is cut into several)
        static float Piece(SpellDeploy308ProfileSO profile, float size) => Mathf.Min(Mathf.Max(.05f, size), profile.Residue.MaxPiece);

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static Vector3 Forward(in PresentEvent308 e)
        {
            Vector3 flat = Flat(e.EyeForward);
            return flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        }

        // a flat direction turned about the vertical by degrees (no Quaternion: this file runs outside the engine too)
        static Vector3 Turn(Vector3 flat, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(a), sin = Mathf.Sin(a);
            return new Vector3(flat.x * cos + flat.z * sin, 0f, -flat.x * sin + flat.z * cos);
        }

        static void Add(PresentOp308[] ops, ref int n, in PresentOp308 op)
        {
            if (n < ops.Length) ops[n++] = op;
        }
    }
}
