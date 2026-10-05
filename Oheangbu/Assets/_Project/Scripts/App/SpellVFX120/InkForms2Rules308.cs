using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>Which form a cast is drawn with. Category = the form the category always had (InkDeployForms308.Compose).</summary>
    public enum DeployForm308 { Category, Guard, Fence, Comet, Loose, Emerge }
    /// <summary>The guard stroke's look. Window = wet (the parry window is open), Dry = the plain block that is left,
    /// Broken = a parry consumed it (the halves part and melt).</summary>
    public enum GuardPhase308 { Window, Dry, Broken }
    /// <summary>What met the guard. The layer's own enum: the pure rules do not know the combat assembly.</summary>
    public enum GuardContact308 { None, Success, Half, Block, Fail }

    public struct GuardTimeline308
    {
        public int WetEndCel;     // first cel of the dry stroke = floor(window / cel): never wet after the rule's window has closed
        public int EndCel;        // the cel the rule's lifetime ends in
        public int GlowEndCel;    // first cel on which nothing of the stroke glows
    }

    // #308 forms2 (Tools/Unity/Stage308_forms2/DESIGN.md): the rules of the forms that replace the catalogue bodies.
    // Pure and stateless: no Unity object, no clock, no scene. The runtime, the director and the effect only apply them; the
    // offline checks (pure308_forms2.py forms) run them without an editor. Presentation only - nothing here is read by a rule.
    public static class InkForms2Rules308
    {
        /// <summary>The form of a cast. A new form is drawn only where the row RETIRES the catalogue body (the map decides,
        /// the data switch can still say no) AND the caller asked for the new forms (`newForms`: DeployCast308.NewForms - the
        /// effect seam, the director's own curtain, the previews). A cast built by anybody else - the spell presenter's stage
        /// borrows rows and changes their Category (its cover wall is an attack row drawn as a Ward) - keeps the category form.
        /// `hasTarget`: the cast has a target or an area plan; the loose mark is only ever drawn for an install aimed at nothing.</summary>
        public static DeployForm308 FormOf(SpellDeploy308ProfileSO profile, in SpellDeploy308MapSO.Row row, bool triggered, bool hasGuardClock, bool newForms, bool hasTarget)
        {
            if (profile == null || !newForms) return DeployForm308.Category;
            bool retire = row.LegacyBody == DeployLegacyBody308.Retire;
            switch (row.Category)
            {
                case DeployCategory308.Parry: return retire && hasGuardClock && profile.Guard.Enabled ? DeployForm308.Guard : DeployForm308.Category;
                case DeployCategory308.Ward: return retire && profile.Fence.Enabled ? DeployForm308.Fence : DeployForm308.Category;
                case DeployCategory308.ComboInstall: return retire && !triggered && !hasTarget ? DeployForm308.Loose : DeployForm308.Category;
                case DeployCategory308.AttackSingle: return retire && !triggered && profile.Comet.Enabled ? DeployForm308.Comet : DeployForm308.Category;
                case DeployCategory308.Summon: return profile.Summon.OwnEntrance ? DeployForm308.Emerge : DeployForm308.Category;
                default: return DeployForm308.Category;
            }
        }

        /// <summary>N3: may this row's catalogue body be retired at all? Never while the effect runs on somebody else's clock in
        /// Play (the planted tree lift: its body is a thing of the world the rule reads back).</summary>
        public static bool RetireAllowed(in SpellDeploy308MapSO.Row row, bool externalClockInPlay) =>
            row.LegacyBody == DeployLegacyBody308.Retire && !externalClockInPlay;

        /// <summary>N4: a retired body that is not an attack keeps the lifetime its owner gave it (the guard's lifetime, the
        /// ward's, the buff's): the rule side and the sounds read that object. Attack rows take the layer's own length.</summary>
        public static bool KeepsLegacyLife(DeployCategory308 category) =>
            category != DeployCategory308.AttackSingle && category != DeployCategory308.AttackArea && category != DeployCategory308.Blank;

        /// <summary>Forms that are born on cel 0 (no ignition lead): what they show must stand before the rule's own formation ends.</summary>
        public static bool NoLead(DeployForm308 form) => form == DeployForm308.Guard || form == DeployForm308.Fence || form == DeployForm308.Emerge;

        /// <summary>O3: a body that stands (a guard stroke, a ward's wall or fence, a cover wall) rents a standing slot.</summary>
        public static bool Standing(DeployCategory308 category, float holdSeconds, DeployForm308 form) =>
            form == DeployForm308.Guard || form == DeployForm308.Fence || (holdSeconds > 0f && category == DeployCategory308.Ward);

        // ---- pool (O3): slots [0, general) are the bursts', [general, general + standing) the standing bodies'.

        public static void SlotRange(bool standing, int general, int standingSlots, out int first, out int count)
        {
            if (standing && standingSlots > 0) { first = general; count = standingSlots; }
            else { first = 0; count = general; }
        }

        /// <summary>The slot a request gets inside [first, first + count): the first free one, else the one rented longest ago
        /// (`evicts` = it has an owner that must hand it over). -1 = no slot. Nothing outside the range is ever looked at.</summary>
        public static int PickSlot(bool[] used, int[] order, int first, int count, out bool evicts)
        {
            evicts = false;
            if (used == null || order == null) return -1;
            int oldest = -1, end = Mathf.Min(first + count, used.Length);
            for (int i = Mathf.Max(0, first); i < end; i++)
            {
                if (!used[i]) return i;
                if (oldest < 0 || order[i] < order[oldest]) oldest = i;
            }
            evicts = oldest >= 0;
            return oldest;
        }

        // ---- guard stroke

        /// <summary>The guard stroke's timetable. `window` and `life` are what the wiring handed over (ParryJudge.GuardWindow /
        /// GuardLifetime, hold scale included): the form never works a window out by itself.</summary>
        public static GuardTimeline308 GuardTimeline(SpellDeploy308ProfileSO profile, float celSeconds, float window, float life)
        {
            float cel = Mathf.Max(.001f, celSeconds);
            // floor, never round: the wet stroke says "the window is open" - it may dry up to one cel early, never late
            int wetEnd = Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(0f, window) / cel + .0001f));
            int end = Mathf.Max(wetEnd, Mathf.FloorToInt(Mathf.Max(0f, life) / cel + .0001f));
            // the stroke is drawn on cels 0 and 1; whatever is born later (the dry stroke) is born as ink
            // forms3 D1 (user decision "the birth glow is 2 cels"): the glow ends GlowCels cels after the cast, not one cel later
            // (it was 1 + GlowCels = 3 cels). What is born on cel 1 glows for the one cel that is left.
            int glowEnd = Mathf.Min(profile != null ? profile.GlowCels : 0, wetEnd);
            return new GuardTimeline308 { WetEndCel = wetEnd, EndCel = end, GlowEndCel = glowEnd };
        }

        public static GuardPhase308 GuardPhaseAt(in GuardTimeline308 timeline, int cel, bool broken) =>
            broken ? GuardPhase308.Broken : cel < timeline.WetEndCel ? GuardPhase308.Window : GuardPhase308.Dry;

        /// <summary>Where the guard stroke is held: ahead of the eye along the camera's FLAT heading, at the stroke's height. It
        /// follows the heading, never the pitch - a thing held in the hands, not a mark fixed to the screen.</summary>
        public static void GuardPose(SpellDeploy308ProfileSO profile, Vector3 eye, Vector3 viewForward, out Vector3 position, out Vector3 flatForward)
        {
            flatForward = new Vector3(viewForward.x, 0f, viewForward.z);
            flatForward = flatForward.sqrMagnitude > 1e-6f ? flatForward.normalized : Vector3.forward;
            var g = profile.Guard;
            position = eye + flatForward * g.Forward - Vector3.up * (g.EyeHeight - g.Height);
        }

        /// <summary>Longest needle that may stand on the stroke at `barTop` (m above the stroke's origin) without rising over
        /// the elevation limit. 0 = no room.</summary>
        public static float GuardNeedleRoom(SpellDeploy308ProfileSO profile, float barTop, float flatDistance)
        {
            var g = profile.Guard;
            float limit = (g.EyeHeight - g.Height) + Mathf.Tan(g.MaxElevationDeg * Mathf.Deg2Rad) * Mathf.Max(.1f, flatDistance);   // local y of the limit line
            return Mathf.Max(0f, limit - barTop - .01f);   // a centimetre under the line
        }

        /// <summary>Ink thrown toward the attacker when a parry lands (world space). Returns the drops written.</summary>
        public static int GuardSplash(SpellDeploy308ProfileSO profile, DeployTier308 tier, GuardContact308 contact, Vector3 point, Vector3 away, int seed, DeployDrop308[] drops)
        {
            if (profile == null || drops == null) return 0;
            var g = profile.Guard;
            int count = contact == GuardContact308.Success ? Mathf.RoundToInt(g.SplashDrops * (tier == DeployTier308.Mobile ? g.SplashMobileScale : 1f))
                : contact == GuardContact308.Half ? g.HalfDrops : contact == GuardContact308.Block ? g.BlockDrops : 0;
            count = Mathf.Clamp(count, 0, drops.Length);
            away = new Vector3(away.x, 0f, away.z);
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, away);
            var rng = new DeployRng308(seed);
            float max = Mathf.Max(g.SplashSpeedMin, g.SplashSpeedMax);
            for (int i = 0; i < count; i++)
            {
                float fan = Mathf.Lerp(-1f, 1f, (i + rng.Next()) / count);
                // a parry throws the ink at the attacker; a half parry and a block only let it fall
                Vector3 velocity = contact == GuardContact308.Success
                    ? (away + side * (fan * g.SplashFan)).normalized * rng.Range(g.SplashSpeedMin, max) + Vector3.up * rng.Range(g.SplashUpMin, Mathf.Max(g.SplashUpMin, g.SplashUpMax))
                    : side * (fan * g.FallFan) + Vector3.up * rng.Range(g.FallUpMin, Mathf.Max(g.FallUpMin, g.FallUpMax));
                drops[i] = new DeployDrop308 { Local = point + side * (fan * g.SplashSpread), Velocity = velocity, Size = profile.Residue.DropSmall * rng.Range(g.SplashSizeMin, Mathf.Max(g.SplashSizeMin, g.SplashSizeMax)),
                    Cell = rng.Next() < g.SplashClusterChance ? InkBurstMeshBuilder308.CellCluster : InkBurstMeshBuilder308.CellDropTailed, Air = true, Free = true, Life = g.SplashSeconds };
            }
            return count;
        }

        // ---- comet (the travelling shot). The judged "projectile" is no object: it is the point between the cast origin and
        // the target's chest that the flight clock has reached. The comet shows exactly that point, plus the path numbers the
        // player already knows from the old bolt (arc / curve / ease) - read from the letter's profile, never invented here.

        /// <summary>Share of the flight that has passed, continuous in time (never stepped to cels).</summary>
        public static float CometU(float seconds, float flightSeconds) => flightSeconds <= 0f ? 1f : Mathf.Clamp01(seconds / flightSeconds);

        /// <summary>Share of the straight line covered at `u` (ease 1 = even, above 1 = a gathering throw).</summary>
        public static float CometEase(float u, float ease) => ease > 0f && !Mathf.Approximately(ease, 1f) ? Mathf.Pow(Mathf.Clamp01(u), ease) : Mathf.Clamp01(u);

        /// <summary>The side a curved flight bends to (flat, unit): left or right of the flight line by the cast's seed.</summary>
        public static Vector3 CometSide(Vector3 origin, Vector3 target, int seed)
        {
            Vector3 line = target - origin; line.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, line.sqrMagnitude > .001f ? line.normalized : Vector3.forward);
            return ((seed & 1) == 0 ? 1f : -1f) * side;
        }

        /// <summary>What the path numbers add to the straight line at `u`: zero at both ends. This is the old bolt's own path
        /// formula (Vfx120Effect.Bolt300Point, the .35 falloff of the side bend included) repeated, not a number of this layer:
        /// the offline check F7 holds the two together.</summary>
        public static Vector3 CometOffset(float u, float arc, float curve, Vector3 side)
        {
            u = Mathf.Clamp01(u);
            return Vector3.up * (arc * 4f * u * (1f - u)) + side * (curve * Mathf.Sin(Mathf.PI * u) * (1f - .35f * u));
        }

        /// <summary>The comet head's place at `u`: the line from the origin to where the target is NOW, plus the path offset.</summary>
        public static Vector3 CometPoint(Vector3 origin, Vector3 target, float u, float arc, float curve, float ease, Vector3 side) =>
            Vector3.Lerp(origin, target, CometEase(u, ease)) + CometOffset(u, arc, curve, side);

        /// <summary>How much a comet is scaled up so that its head (size `headSize`, m) is at least Comet.MinHeadDeg on screen
        /// at `eyeDistance` (the eye to the target: where the comet is smallest). 1 = as authored; never over Comet.MaxScale.</summary>
        public static float CometScale(SpellDeploy308ProfileSO profile, float headSize, float eyeDistance)
        {
            if (profile == null || headSize <= 0f) return 1f;
            float want = Mathf.Max(0f, eyeDistance) * Mathf.Tan(profile.Comet.MinHeadDeg * Mathf.Deg2Rad);
            return Mathf.Clamp(want / headSize, 1f, Mathf.Max(1f, profile.Comet.MaxScale));
        }

        /// <summary>Half width of a comet tail: the authored one, but never under Comet.TailHalfMin nor thinner than
        /// Comet.MinTailDeg on screen at `eyeDistance`.</summary>
        public static float CometTailHalf(SpellDeploy308ProfileSO profile, float authoredHalf, float eyeDistance)
        {
            if (profile == null) return authoredHalf;
            float floor = Mathf.Max(0f, eyeDistance) * Mathf.Tan(profile.Comet.MinTailDeg * .5f * Mathf.Deg2Rad);
            return Mathf.Max(authoredHalf, Mathf.Max(profile.Comet.TailHalfMin, floor));
        }

        // ---- residue

        /// <summary>Is a ground mark kept wet (held) while its spell lives? A drop the form marked Free never is.</summary>
        public static bool DropHeld(in DeployDrop308 drop, bool categoryHeld) => !drop.Free && (drop.Held || categoryHeld);

        // ---- buff marks

        /// <summary>Buff marks one footstep may leave: one per running buff, at most the tier's number.</summary>
        public static int FootMarksForStep(SpellDeploy308ProfileSO profile, DeployTier308 tier, int runningBuffs) =>
            profile == null || !profile.Buff.FootMarks ? 0 : Mathf.Clamp(runningBuffs, 0, profile.Tier(tier).FootMarksPerStep);

        /// <summary>Which of the running EA buffs (`running`: bit = the element's index, EABuffRuntime.ActiveMask) the layer
        /// shows itself: only a buff whose own letter (the buff row with the Giyeok final of that element) is switched on AND
        /// whose catalogue body the map retires. Every other running buff is the catalogue's to show - no mark, no aura ring,
        /// no end drops beside it.</summary>
        public static int BuffMaskShown(SpellDeploy308ProfileSO profile, int running)
        {
            if (profile == null || running == 0 || profile.Map == null || profile.Map.Rows == null) return 0;
            int shown = 0;
            foreach (var row in profile.Map.Rows)
            {
                if (row.Category != DeployCategory308.Buff || row.Final != DeployFinal308.Giyeok || row.LegacyBody != DeployLegacyBody308.Retire) continue;
                int bit = 1 << (int)row.Element;
                if ((running & bit) != 0 && profile.IsEnabled(row.Char)) shown |= bit;
            }
            return shown;
        }

        /// <summary>Where the drops of an ended buff are born and how they start: ahead of the eye along the flat heading and
        /// a little below it - inside the view -, tossed up. `k` = -0.5 .. 0.5 across the row.</summary>
        public static void BuffShed(SpellDeploy308ProfileSO profile, Vector3 eye, Vector3 viewForward, float k, out Vector3 point, out Vector3 velocity)
        {
            Vector3 flat = new Vector3(viewForward.x, 0f, viewForward.z);
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            var b = profile.Buff;
            point = eye + flat * b.ShedForward + Vector3.Cross(Vector3.up, flat) * (k * b.ShedWidth) - Vector3.up * b.ShedDown;
            velocity = Vector3.up * b.ShedLift;
        }

        /// <summary>Where the `index`-th buff mark of a step lies: beside the print, further out with each one.</summary>
        public static Vector3 FootMarkPoint(SpellDeploy308ProfileSO profile, Vector3 print, Vector3 side, float footWidth, int index)
        {
            var b = profile.Buff;
            return print + side * (footWidth * (b.FootMarkOffset + index * b.FootMarkStep)) + Vector3.up * b.FootMarkLift;
        }

        /// <summary>The most marks the buffs and a fence can keep alive in the spell residue ring at once.</summary>
        public static float BuffAliveCeiling(SpellDeploy308ProfileSO profile, DeployTier308 tier)
        {
            var t = profile.Tier(tier);
            float foot = profile.Buff.FootMarks ? t.FootMarksPerSecond * profile.BuffMarkLife : 0f;
            float aura = t.AuraStamps / Mathf.Max(.1f, profile.Buff.AuraInterval) * profile.BuffAuraLife;
            return foot + aura + t.FenceRingDrops;
        }
    }
}
