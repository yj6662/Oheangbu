using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // S5-b slice 4: the verb half of the spell lab. The runner (Vol2LabRunner) walks the 120 table rows; for a row whose verb has a lab
    // PROVIDER it builds a scenario (runtime dummies, planted materials, timed steps), injects the letter, runs the steps on the scope clock,
    // fast-forwards the clock to the end of the cast's life (Vol2Services.Skew - the lab never waits 10 s for a residue), and asks the provider
    // for the verdict. HOW TO ADD A VERB'S LAB (slices 5 to 7): one new file with a class implementing IVol2LabProvider and ONE line in
    // Vol2VerbLab.Providers. Expectations are written by hand from the frame geometry and the table sentences, never read back from a run.

    public sealed class Vol2LabStep { public double AtSeconds; public string Name = ""; public Action<Vol2LabContext> Do; }

    /// <summary>What a row must show. Names are the dummies'. Kinds are the contact kinds of Vol2ContactRecord.</summary>
    public sealed class Vol2LabExpect
    {
        public string[] Instant = new string[0], Second = new string[0], Residue = new string[0], Spread = new string[0], Return = new string[0], Absent = new string[0];
        /// <summary>name -> the budget reason every touch of that body must have been refused with.</summary>
        public readonly Dictionary<string, string> Rejected = new Dictionary<string, string>();
        /// <summary>The residue must end this way (None = the row expects no residue).</summary>
        public Vol2MaterialEnd ResidueEnd = Vol2MaterialEnd.None;
        public decimal ResiduePaid, SpreadPaid, ReturnPaid;
        public int Marks;
    }

    public sealed class Vol2LabScenario
    {
        public string Variant = "main", Describe = "";
        public Vector3 Origin, Forward;
        public readonly List<Vol2LabDummy> Dummies = new List<Vol2LabDummy>();
        public readonly List<Vol2LabStep> Steps = new List<Vol2LabStep>();
        public readonly Dictionary<string, Vol2Material> Planted = new Dictionary<string, Vol2Material>();
        public Vol2LabExpect Expect = new Vol2LabExpect();
        public bool UsesSwings;
        /// <summary>Seconds of the scope clock to let the cast run after its last step, before the fast-forward.</summary>
        public double SettleSeconds = 0.4;
        /// <summary>Seconds the clock must have run before the fast-forward at the very least (a growing fan needs time to grow).</summary>
        public double MinSeconds;
        /// <summary>S5-b slice 8: the reaction rows run on the real clock (a burn that ticks for 2 s is watched, not skipped): no fast-forward.</summary>
        public bool SkipFastForward;
        public Vol2LabDummy Find(string name) { foreach (var d in Dummies) if (d.Name == name) return d; return null; }
    }

    /// <summary>What a step can do to the scenario.</summary>
    public sealed class Vol2LabContext
    {
        long _plantedCasts;       // per row (one context per row); planted ids start at 9,000,000, far from the issuer's real CastIds (1, 2, ...)
        public Vol2ShapeRuntime Shapes { get; }
        public Vol2Services Services { get; }
        public Vol2CastCoordinator Coordinator => Shapes.Coordinator;
        public Vol2LabScenario Scenario { get; set; }
        public int SwingsRaised { get; private set; }
        /// <summary>S5-b slice 8: casts another letter through the recognition channel (set by the runner; a step of a reaction row casts a second glyph with it).</summary>
        public Action<char> Cast { get; set; }
        /// <summary>S5-b slice 8: the scope's defence kit (null without one) - the seams row reads what the installer bound.</summary>
        public Vol2DefenceKit Kit { get; set; }
        public Vol2LabContext(Vol2ShapeRuntime shapes, Vol2Services services) { Shapes = shapes; Services = services; }

        Vector3 Right => Vector3.Cross(Vector3.up, Scenario.Forward);
        public Vector3 At(float ahead, float side) => Scenario.Origin + Scenario.Forward * ahead + Right * side;

        public Vol2LabDummy Dummy(string name, float ahead, float side)
        {
            var go = new GameObject("Vol2LabDummy_" + name);
            go.transform.position = At(ahead, side);
            var cap = go.AddComponent<CapsuleCollider>(); cap.radius = 0.4f; cap.height = 1.8f; cap.center = new Vector3(0f, 0.9f, 0f);
            var vitals = go.AddComponent<EnemyVitals>();
            Shapes.Registry.AddExtra(vitals);
            var d = new Vol2LabDummy { Go = go, Vitals = vitals, Name = name, Start = go.transform.position, HpBefore = vitals.Hp };
            Scenario.Dummies.Add(d);
            return d;
        }

        public void Move(string name, float ahead, float side)
        {
            var d = Scenario.Find(name);
            if (d != null && d.Go != null) { d.Go.transform.position = At(ahead, side); Physics.SyncTransforms(); }
        }

        /// <summary>One lab-simulated swing reported by three colliders (the same SwingId), from the caster toward the aim.</summary>
        public void Swing()
        {
            long swing = Services.Ids.NextSwing().Value;
            Vector3 sleeve = Scenario.Origin + Vector3.up * 1.1f, tip = sleeve + Scenario.Forward * Coordinator.Tuning.BrushReach;
            for (int k = 0; k < 3; k++) Shapes.Swings.Raise(new Vol2SwingEvent(swing, sleeve, tip, Services.Clock.NowUs, k));
            SwingsRaised++;
        }

        /// <summary>A material that belongs to no cast of this row (an old residue of mine, an enemy patch, a world vine), standing at (ahead, side).</summary>
        public Vol2Material Plant(string name, Body body, float ahead, float side, long factionId, float radius = 0.6f, bool gateDoor = false)
        {
            var cast = new CastId(9000000 + (++_plantedCasts));
            long life = RuleTypesV3.Microseconds(Services.Rulebook.Numbers.Get("timing.g.life"));
            var m = Coordinator.Materials.Create(cast, '가', body, Vol2MaterialKind.Residue, 0.9m, life, Vol2Footprint.Disc(At(ahead, side), radius),
                factionId == Vol2Faction.Enemy ? 2 : 1, factionId, true, Services.Clock.NowUs, 0, 0, name, gateDoor);
            Scenario.Planted[name] = m;
            return m;
        }
    }

    public interface IVol2LabProvider
    {
        string Name { get; }
        bool Handles(Vol2SpellRowDto row);
        Vol2LabScenario Build(Vol2SpellRowDto row, Vol2LabContext ctx);
        Vol2OnceVerdict Evaluate(Vol2SpellRowDto row, Vol2LabScenario sc, Vol2CastLog log, Vol2CastDecision decision, Vol2LabContext ctx);
    }

    public static class Vol2VerbLab
    {
        public const string Scenario = "S4-verb";

        /// <summary>THE SINGLE LIST of verb lab providers. A later slice adds one line.</summary>
        public static readonly IVol2LabProvider[] Providers =
        {
            new Vol2LeaveSpreadLab(),      // 남긴다 / 번진다 (slice 4)
            new Vol2SeedLab(),             // 심는다 (slice 5)
            new Vol2SplitLab(),            // 가른다 (slice 6)
            new Vol2ReturnLab(),           // 돌아온다 (slice 7)
        };

        public static IVol2LabProvider Find(Vol2SpellRowDto row)
        {
            foreach (var p in Providers) if (p.Handles(row)) return p;
            return null;
        }

        /// <summary>The clock jumps to just after the end of everything this cast and its materials have: no row waits for a 10 s life.</summary>
        public static void FastForward(Vol2LabContext ctx, Vol2CastLog log, Vol2CastDecision d)
        {
            long now = ctx.Services.Clock.NowUs, end = now;
            foreach (var m in ctx.Coordinator.Materials.Alive) end = Math.Max(end, m.ExpiresUs);
            if (d != null && d.Plan != null) end = Math.Max(end, log.StartUs + d.Plan.LifeUs);
            ctx.Services.Skew.Seconds += (end - now + 100000) / 1000000.0;
        }

        public static void Cleanup(Vol2LabScenario sc, Vol2TargetRegistry registry)
        {
            if (sc == null) return;
            foreach (var d in sc.Dummies) { registry.RemoveExtra(d.Vitals); if (d.Go != null) UnityEngine.Object.Destroy(d.Go); }
            sc.Dummies.Clear();
        }
    }

    /// <summary>The 40 glyphs of 남긴다 (ㄱ) and 번진다 (ㄴ), minus 걱 (the defence kit's recovery, run by the lab as a slice 2 row).</summary>
    public sealed class Vol2LeaveSpreadLab : IVol2LabProvider
    {
        public string Name => "Leave+Spread";
        public bool Handles(Vol2SpellRowDto row) => (row.Verb == Verb.Leave || row.Verb == Verb.Spread) && row.Letter != '걱';

        static bool DrawsThird(Body b) => b == Body.Fire || b == Body.Earth || b == Body.Metal;

        // ------------------------------------------------------------------ scenarios ------------------------------------------------------------------

        public Vol2LabScenario Build(Vol2SpellRowDto row, Vol2LabContext ctx)
        {
            var caster = ctx.Shapes.Caster;
            var sc = new Vol2LabScenario { Origin = caster.Position, Forward = Vol2ShapeGeometry.FlatUnit(caster.AimDirection) };
            ctx.Scenario = sc;
            var g = row.Glyph; var n = ctx.Services.Rulebook.Numbers; var e = sc.Expect;
            char letter = g.Letter; bool third = DrawsThird(g.Body);
            void Step(double at, string name, Action<Vol2LabContext> act) { sc.Steps.Add(new Vol2LabStep { AtSeconds = at, Name = name, Do = act }); }
            decimal residue = n.Get("action.normal.g.shared");

            if (g.Verb == Verb.Leave)
            {
                switch (g.Frame)
                {
                    case Frame.Path:
                        ctx.Dummy("A", 4f, 0f); ctx.Dummy("B", 8f, 0f); ctx.Dummy("L", 6f, 3.5f);
                        Step(2.4, "L enters the residue", c => c.Move("L", 4.3f, 0.2f));
                        e.Instant = new[] { "A" }; e.Residue = third ? new[] { "A", "L", "M" } : new[] { "A", "L" };
                        e.Absent = third ? new[] { "B", "Z" } : new[] { "B" };
                        if (third)
                        {
                            ctx.Dummy("M", 6f, -3.5f); ctx.Dummy("Z", 6f, -6f);
                            Step(2.8, "M enters the residue", c => c.Move("M", 3.8f, -0.3f));
                            Step(3.2, "Z finds it spent", c => c.Move("Z", 4.0f, 0.3f));
                        }
                        sc.Describe = "path leaves a residue at the first body (A 4 m; B behind it never touched); L, " + (third ? "M" : "") + " walk in later and draw their share" + (third ? "; Z arrives after the pool is spent" : "") + (letter == '각' ? "; 각: A is caged 1.5 s first, the residue appears where the cage lets go" : "");
                        sc.SettleSeconds = 0.5;
                        break;
                    case Frame.Fan:
                        ctx.Dummy("In3", 3f, 0f); ctx.Dummy("In4.8", 4.8f, -0.3f);
                        e.Instant = new[] { "In3", "In4.8" };
                        if (letter == '곡')
                        {
                            ctx.Dummy("G", 5.7f, 0f); ctx.Dummy("Out7.5", 7.5f, 0f);
                            e.Residue = new[] { "In3", "In4.8", "G" }; e.Absent = new[] { "Out7.5" };
                            sc.MinSeconds = 3.4;
                            sc.Describe = "곡: the fan grows to area x1.5; G stands just beyond its first reach (5.7 m) and is taken in when it has grown; each body is bound once, 1.5 s";
                        }
                        else
                        {
                            ctx.Dummy("Out5.8", 5.8f, 0f);
                            e.Residue = third ? new[] { "In3", "In4.8", "L" } : new[] { "In3", "In4.8" };
                            e.Absent = third ? new[] { "Out5.8", "M" } : new[] { "Out5.8" };
                            if (third)
                            {
                                ctx.Dummy("L", 7f, 0f); ctx.Dummy("M", 7f, 2f);
                                Step(1.0, "L walks into the fan", c => c.Move("L", 3.5f, -0.5f));
                                Step(1.4, "M finds the pool spent", c => c.Move("M", 2.5f, 0.3f));
                            }
                            sc.Describe = "fan leaves a residue shaped like itself: In3, In4.8 inside draw at once" + (third ? "; L walks in later and draws; M arrives after the pool is spent" : "") + "; Out5.8 beyond the depth never touches it";
                        }
                        break;
                    case Frame.Ring:
                        ctx.Dummy("Band2.0", 2f, 0f); ctx.Dummy("BandSide", 0f, -2.1f); ctx.Dummy("Hole0.5", 0.5f, 0.3f); ctx.Dummy("Out3.4", 3.4f, 0f);
                        e.Instant = new[] { "Band2.0", "BandSide" };
                        e.Residue = third ? new[] { "Band2.0", "BandSide", "L" } : new[] { "Band2.0", "BandSide" };
                        e.Absent = third ? new[] { "Hole0.5", "Out3.4", "M" } : new[] { "Hole0.5", "Out3.4" };
                        if (third)
                        {
                            ctx.Dummy("L", 3.4f, -1f); ctx.Dummy("M", 6f, 3f);
                            Step(1.0, "L steps onto the band", c => c.Move("L", 2.0f, 1.0f));
                            Step(1.4, "M finds the pool spent", c => c.Move("M", -2.0f, 0.2f));
                        }
                        if (letter == '국')
                        {
                            ctx.Plant("P1_mine_inside", Body.Wood, 0.5f, 0.5f, Vol2Faction.Player);
                            ctx.Plant("P2_mine_outside", Body.Wood, 6f, 0f, Vol2Faction.Player);
                            ctx.Plant("P3_enemy_inside", Body.Wood, 1f, 0f, Vol2Faction.Enemy);
                        }
                        sc.Describe = "the ring leaves a residue band: the two bodies on the band draw at once; the hollow and the outside never touch it" + (third ? "; L steps on later and draws" : "") + (letter == '국' ? "; 국: MY residue inside the ring grows x1.5 once, the one outside and the enemy's do not" : "");
                        break;
                    default:    // attachment
                        foreach (var d in new[] { ("A", 0.8f, 0f), ("B", 1.0f, 0.3f), ("C", 1.2f, -0.3f), ("D", 1.4f, 0f), ("E", 3.5f, 0f) }) ctx.Dummy(d.Item1, d.Item2, d.Item3);
                        sc.UsesSwings = true;
                        Step(0.05, "swing 1", c => c.Swing()); Step(0.45, "swing 2", c => c.Swing()); Step(0.7, "swing 3", c => c.Swing());
                        e.Instant = new[] { "A", "B", "C" }; e.Second = new[] { "A", "B", "C" }; e.Residue = new[] { "A", "B", "C" };
                        e.Rejected["D"] = "TargetCap"; e.Absent = new[] { "E" };
                        sc.Describe = "ㅓ on the body: swing 1 touches A B C (the first three; D, a fourth, is refused: TargetCap) and each draws a share; swing 2 (0.4 s later) is the second contact 0.3 on the same three; swing 3 pays nothing; E is out of reach";
                        break;
                }
                e.ResidueEnd = third || letter == '곡' || g.Frame == Frame.Attachment ? Vol2MaterialEnd.Consumed : Vol2MaterialEnd.Expired;
                if (g.Frame == Frame.Attachment) e.ResidueEnd = Vol2MaterialEnd.Consumed;
                int draws = e.Residue.Length;
                e.ResiduePaid = Math.Min(residue, draws * residue / 3m);
            }
            else
            {
                bool peak = row.Glyph.Kind == Oheangbu.Vol2.Rules.SpellKind.Peak;
                switch (g.Frame)
                {
                    case Frame.Path:
                        ctx.Dummy("A", 4f, 0f); ctx.Dummy("B", 4f, 1.0f); ctx.Dummy("C", 4f, -1.5f); ctx.Dummy("D", 4f, 2.0f); ctx.Dummy("E", 4f, -2.5f); ctx.Dummy("F", 4f, 2.9f); ctx.Dummy("G", 10f, 0f);
                        e.Instant = new[] { "A" };
                        if (peak) { e.Spread = new[] { "B", "C", "D" }; e.Absent = new[] { "E", "F", "G" }; e.SpreadPaid = 1.5m; e.Marks = 3; }
                        else { e.Spread = new[] { "B", "C", "D", "E" }; e.Absent = new[] { "F", "G" }; e.SpreadPaid = 1.0m; e.Marks = 4; }
                        sc.Describe = "path ends at A (4 m); the effect spreads to the bodies within 3 m of A, nearest first: B 1.0, C 1.5, D 2.0, E 2.5, F 2.9; " + (peak ? "난: 0.5 each, three at most" : "1.0 split among the four nearest") + "; G (6 m from A) is out of reach";
                        break;
                    case Frame.Attachment:
                        sc.UsesSwings = true;
                        if (letter == '넌')
                        {
                            ctx.Dummy("A", 1f, 0f); ctx.Dummy("B", 1f, 2.0f); ctx.Dummy("C", 1f, -5.0f);
                            Step(0.05, "swing 1", c => c.Swing());
                            Step(0.25, "C comes within 3 m of A", c => c.Move("C", 1f, -2.6f));
                            Step(0.45, "swing 2", c => c.Swing()); Step(0.7, "swing 3", c => c.Swing());
                            e.Instant = new[] { "A" }; e.Second = new[] { "A" }; e.Spread = new[] { "B", "C" }; e.SpreadPaid = 1.0m; e.Marks = 2;
                            sc.Describe = "넌: swing 1 touches A and spreads 0.5 to B; C walks near; swing 2 (second contact on A) spreads 0.5 to C from the SAME 1.5; swing 3 spreads nothing";
                        }
                        else
                        {
                            ctx.Dummy("A", 1f, 0f); ctx.Dummy("B", 1f, 2.0f); ctx.Dummy("C", 1f, -2.4f); ctx.Dummy("D", 1f, 2.9f);
                            Step(0.05, "swing 1", c => c.Swing()); Step(0.45, "swing 2", c => c.Swing());
                            e.Instant = new[] { "A" }; e.Second = new[] { "A" }; e.Spread = new[] { "B", "C" }; e.Absent = new[] { "D" }; e.SpreadPaid = 1.0m; e.Marks = 2;
                            sc.Describe = "ㅓ: swing 1 touches A and spreads to the nearest two (B 2.0, C 2.4; the cast reaches three bodies in all, so D 2.9 gets nothing) 0.5 each; swing 2 is A's second contact";
                        }
                        break;
                    case Frame.Fan:
                        ctx.Dummy("In3", 3f, 0f); ctx.Dummy("In4s1", 4f, 1f); ctx.Dummy("Out5.8", 5.8f, 0f); ctx.Dummy("Out3s2.6", 3f, 2.6f); ctx.Dummy("Behind", -2f, 0f); ctx.Dummy("Far12", 12f, 0f);
                        e.Instant = new[] { "In3", "In4s1" }; e.Spread = new[] { "Out5.8", "Out3s2.6", "Behind" }; e.Absent = new[] { "Far12" }; e.SpreadPaid = 1.0m; e.Marks = 3;
                        if (letter == '논') { e.Return = new[] { "Behind", "Out3s2.6", "Out5.8" }; e.ReturnPaid = 0.5m; e.Marks = 6; sc.SettleSeconds = 1.0; }      // the back-spread comes 0.4 s after the forward one, inside the cast's 3 s: the clock may only jump after it
                        sc.Describe = "fan hits In3, In4s1; the effect spreads from its edge to the bodies within 3 m of it (nearest first: Out5.8 0.8, Out3s2.6 1.18, Behind 2.0), 1.0 split three ways" + (letter == '논' ? "; then, after a pause, the SAME bodies in reverse share 0.5 (back-spread)" : "");
                        break;
                    default:    // ring
                        if (letter == '눈')
                        {
                            ctx.Dummy("TB", 2f, 0.1f); ctx.Dummy("T1", 4.5f, 0.5f); ctx.Dummy("T3", 9f, 0f);
                            ctx.Plant("W1_mine_band", Body.Wood, 2f, 0f, Vol2Faction.Player);
                            ctx.Plant("W2_vinedoor_band", Body.Wood, 0f, -2f, Vol2Faction.World, 0.6f, true);
                            ctx.Plant("W3_mine_chain", Body.Wood, 4.5f, 0.5f, Vol2Faction.Player);
                            ctx.Plant("W4_mine_far", Body.Wood, 9f, 0f, Vol2Faction.Player);
                            foreach (var p in new[] { ("E1", -2f, 0f), ("E2", 0f, 2f), ("E3", 1.414f, 1.414f), ("E4", -1.414f, 1.414f), ("E5", -1.414f, -1.414f) }) ctx.Plant("E" + p.Item1.Substring(1) + "_enemy_band", Body.Water, p.Item2, p.Item3, Vol2Faction.Enemy);
                            e.Instant = new[] { "TB" }; e.Spread = new[] { "T1" }; e.Absent = new[] { "T3" }; e.SpreadPaid = 1.0m; e.Marks = 1;
                            sc.MinSeconds = 0.5;
                            sc.Describe = "눈: fire follows WOOD: W1 and the vine door W2 touch the ring, W3 is next to W1 (2.55 m) and burns with them, W4 (far) does not; T1 stands in W3 and takes the whole 1.0; the ring burns four of the five enemy patches (E1 to E4)";
                        }
                        else
                        {
                            ctx.Dummy("W", 1f, 0f); ctx.Dummy("N1", 5f, 0.3f); ctx.Dummy("N2", 4.2f, 1.5f); ctx.Dummy("N3", 3f, -1f);
                            Step(0.3, "W crosses the band", c => c.Move("W", 3.2f, 0f));
                            e.Instant = new[] { "W" }; e.Spread = new[] { "N2", "N1" }; e.Absent = new[] { "N3" }; e.SpreadPaid = 1.0m; e.Marks = 2;
                            sc.Describe = "ring lives 3 s; W steps across the band (1.0 -> 3.2 m) and is touched; the effect spreads outward to the bodies within 3 m of W that stand farther out: N2 1.80, N1 1.83; N3 (3.0, -1.0) is not farther out; 1.0 split two ways";
                        }
                        break;
                }
            }
            return sc;
        }

        // ------------------------------------------------------------------ verdict ------------------------------------------------------------------

        static string Strip(string name) => name.Replace("Vol2LabDummy_", "");
        static string Word(Body b) => b == Body.Wood ? "bind" : b == Body.Fire ? "burn" : b == Body.Earth ? "push" : b == Body.Metal ? "cut" : "wet";
        static string Fmt(decimal d) => d.ToString("0.#####", CultureInfo.InvariantCulture);
        static bool Near(decimal a, decimal b) => Math.Abs(a - b) <= 0.000001m;

        static List<string> NamesOf(Vol2CastLog log, string kind) => log.Contacts.Where(c => c.Kind == kind && c.BudgetAccepted).Select(c => Strip(c.TargetName)).ToList();

        public Vol2OnceVerdict Evaluate(Vol2SpellRowDto row, Vol2LabScenario sc, Vol2CastLog log, Vol2CastDecision decision, Vol2LabContext ctx)
        {
            var v = new Vol2OnceVerdict();
            var problems = new List<string>();
            var e = sc.Expect; var g = row.Glyph; var n = ctx.Services.Rulebook.Numbers; var co = ctx.Coordinator;
            void Need(bool ok, string what) { if (!ok) problems.Add(what); }

            Need(log.Status == "Done", "cast status " + log.Status);
            void SameSet(string kind, string[] want)
            {
                var got = NamesOf(log, kind).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var exp = want.OrderBy(x => x, StringComparer.Ordinal).ToList();
                Need(got.SequenceEqual(exp), kind + " [" + string.Join("|", got) + "] expected [" + string.Join("|", exp) + "]");
            }
            SameSet("Instant", e.Instant); SameSet("Second", e.Second); SameSet("Residue", e.Residue);
            var spread = NamesOf(log, "Spread"); Need(spread.SequenceEqual(e.Spread), "Spread [" + string.Join("|", spread) + "] expected [" + string.Join("|", e.Spread) + "] (nearest first)");
            var back = NamesOf(log, "ReturnSpread"); Need(back.SequenceEqual(e.Return), "ReturnSpread [" + string.Join("|", back) + "] expected [" + string.Join("|", e.Return) + "] (reverse)");
            foreach (var a in e.Absent) Need(!log.Contacts.Any(c => Strip(c.TargetName) == a), a + " was touched");
            foreach (var kv in e.Rejected)
            {
                var recs = log.Contacts.Where(c => Strip(c.TargetName) == kv.Key).ToList();
                Need(recs.Count > 0 && recs.All(c => !c.BudgetAccepted && c.BudgetReason == kv.Value), kv.Key + " must be refused with " + kv.Value + " (" + string.Join("|", recs.Select(c => c.BudgetReason)) + ")");
            }
            foreach (var c in log.Contacts.Where(c => c.BudgetAccepted))
                if (!c.DamageApplied) problems.Add(Strip(c.TargetName) + " " + c.Kind + " damage not applied (" + c.DamageNote + ")");
            // one body action per body, the body's own (a spread body takes it too)
            string word = Word(g.Body);
            foreach (var grp in log.Contacts.Where(c => c.BodyEffect.Length > 0).GroupBy(c => c.TargetId))
                if (grp.Count() != 1) problems.Add(Strip(grp.First().TargetName) + " took " + grp.Count() + " body actions");
            foreach (var d in sc.Dummies)
            {
                var affected = log.Contacts.Where(c => c.BudgetAccepted && Strip(c.TargetName) == d.Name && c.Kind != "ReturnSpread").ToList();
                if (affected.Count == 0) continue;
                var first = affected.FirstOrDefault(c => c.BodyEffect.Length > 0);
                if (first == null || !first.BodyEffect.StartsWith(word, StringComparison.Ordinal)) problems.Add(d.Name + " body action '" + (first != null ? first.BodyEffect : "none") + "', expected " + word);
            }
            Need(co.Materials.Audit().Count == 0, "material audit: " + string.Join("; ", co.Materials.Audit()));

            var budget = log.Budget;
            var plan = decision.Plan;
            var residues = log.Materials.Where(m => m.Kind == Vol2MaterialKind.Residue).ToList();
            var marks = log.Materials.Where(m => m.Kind == Vol2MaterialKind.Mark).ToList();
            decimal drawn = log.AmountOf("Residue"), spreadPaid = log.AmountOf("Spread"), returnPaid = log.AmountOf("ReturnSpread");
            if (g.Verb == Verb.Leave)
            {
                Need(residues.Count == 1, "residues made " + residues.Count);
                if (residues.Count == 1)
                {
                    var r = residues[0];
                    long life = RuleTypesV3.Microseconds(n.Get(g.Frame == Frame.Attachment ? "timing.attachment.life" : "timing.g.life"));
                    Need(r.LifeUs == life, "residue life " + r.LifeUs + " us, expected " + life);
                    Need(r.End == e.ResidueEnd, "residue ended " + r.End + ", expected " + e.ResidueEnd);
                    Need(Near(r.Initial, n.Get("action.normal.g.shared")) && Near(r.Initial, budget.Transferred), "residue holds " + Fmt(r.Initial) + ", transferred " + Fmt(budget.Transferred) + ", expected " + Fmt(n.Get("action.normal.g.shared")));
                    Need(Near(r.Drawn, drawn) && Near(r.Drawn + r.Remaining, r.Initial), "drawn " + Fmt(r.Drawn) + " + remaining " + Fmt(r.Remaining) + " != initial " + Fmt(r.Initial));
                    Need(Near(drawn, e.ResiduePaid), "residue draws total " + Fmt(drawn) + ", expected " + Fmt(e.ResiduePaid));
                    Need(drawn <= r.Initial + 0.000001m, "the cast drew more than 0.9");
                    Need(r.FactionId == Vol2Faction.Player && r.Depth == 0 && r.Cast == log.Cast, "residue ownership");
                    if (g.Frame == Frame.Attachment) Need(r.Attached, "the residue of a ㅓ stays on the body (no footprint in the world)");
                    v.MaterialIds = r.Id.Value.ToString(CultureInfo.InvariantCulture);
                }
                Need(Near(budget.PaidShared, 0m), "budget PaidShared " + Fmt(budget.PaidShared) + " (the residue is booked as a transfer, not as shared pay)");
                LeavePeaks(row, sc, log, residues.Count == 1 ? residues[0] : null, n, co, problems, v);
            }
            else
            {
                Need(Near(spreadPaid, e.SpreadPaid), "spread paid " + Fmt(spreadPaid) + ", expected " + Fmt(e.SpreadPaid));
                Need(Near(returnPaid, e.ReturnPaid), "return-spread paid " + Fmt(returnPaid) + ", expected " + Fmt(e.ReturnPaid));
                Need(Near(budget.PaidShared, spreadPaid + returnPaid), "budget PaidShared " + Fmt(budget.PaidShared) + " != spread + return " + Fmt(spreadPaid + returnPaid));
                Need(marks.Count == e.Marks, "marks " + marks.Count + ", expected " + e.Marks);
                long markLife = RuleTypesV3.Microseconds(n.Get("timing.n.life"));
                Need(marks.All(m => m.LifeUs == markLife && m.End == Vol2MaterialEnd.Expired && !m.PaysPool && m.Depth == 0), "marks: life 3 s, ended Expired, not a pool, depth 0");
                // the same share is not allocated twice: the marks hold exactly what the bodies were paid
                Need(Near(marks.Sum(m => m.Initial), spreadPaid + returnPaid), "marks hold " + Fmt(marks.Sum(m => m.Initial)) + ", the bodies were paid " + Fmt(spreadPaid + returnPaid));
                Need(Near(budget.SharedRemaining, plan.Shared - spreadPaid - returnPaid), "budget balance " + Fmt(budget.SharedRemaining));
                v.MaterialIds = string.Join("|", log.Materials.Select(m => m.Id.Value.ToString(CultureInfo.InvariantCulture)));
                SpreadPeaks(row, sc, log, plan, n, co, problems, v);
            }
            v.Created = string.Join("|", log.Materials.Select(m => m.Id + ":" + m.Kind + ":" + Fmt(m.Initial)));
            v.Consumed = string.Join("|", log.Materials.Select(m => m.Id + ":" + m.End));
            v.Depth = log.Materials.Count > 0 ? log.Materials.Max(m => m.Depth).ToString(CultureInfo.InvariantCulture) : "";
            v.Owner = log.Materials.Count > 0 ? "caster" : "";
            v.Balance = Fmt(budget.SharedRemaining);
            v.Ledger = string.Join("|", log.Contacts.Where(c => c.BudgetAccepted && c.Amount > 0m).Select(c => c.Kind + ":" + Strip(c.TargetName) + ":" + Fmt(c.Amount)));
            v.Hit = string.Join("|", log.Contacts.Select(c => Strip(c.TargetName)).Distinct().OrderBy(x => x, StringComparer.Ordinal));
            v.Expected = string.Join("|", e.Instant.Concat(e.Residue).Concat(e.Spread).Concat(e.Return).Distinct().OrderBy(x => x, StringComparer.Ordinal));
            v.Unexpected = "";
            v.Units = log.TotalUnits; v.Hp = log.TotalHp;
            v.BodyObserved = string.Join("; ", log.Contacts.Where(c => c.BodyEffect.Length > 0).Select(c => Strip(c.TargetName) + " " + c.BodyEffect));
            v.Pass = problems.Count == 0; v.Note = string.Join("; ", problems);
            return v;
        }

        void LeavePeaks(Vol2SpellRowDto row, Vol2LabScenario sc, Vol2CastLog log, Vol2Material res, NumbersV3Dto n, Vol2CastCoordinator co, List<string> problems, Vol2OnceVerdict v)
        {
            char letter = row.Letter; var checks = new List<string>();
            void Need(bool ok, string what) { if (!ok) problems.Add(what); else checks.Add(what); }
            long Us(string key) => RuleTypesV3.Microseconds(n.Get(key));
            if (res != null && row.Frame == Frame.Path && letter != '각')
                Need(res.CreatedUs - log.StartUs < 600000, "path residue is made when the path lands (<0.6 s)");
            switch (letter)
            {
                case '각':
                    {
                        long cage = Us("combat.bind.maxDuration");
                        Need(log.Notes.Any(x => x.StartsWith("cage on")) && log.Notes.Any(x => x.StartsWith("cage released")), "각: the cage began and let go");
                        if (res != null)
                        {
                            long since = res.CreatedUs - log.StartUs;
                            Need(since >= cage && since <= cage + 300000, "각: residue made " + since + " us after the cast, cage is " + cage + " us");
                            Need(res.LifeUs == Us("timing.g.life"), "각: residue life counts from the release");
                        }
                        var a = log.Contacts.FirstOrDefault(c => c.Kind == "Instant" && Strip(c.TargetName) == "A");
                        Need(a != null && a.BodyEffect.Contains("1.5s"), "각: A is bound 1.5 s (" + (a != null ? a.BodyEffect : "no contact") + ")");
                        break;
                    }
                case '곡':
                    {
                        Need(log.Contacts.Where(c => c.BodyEffect.Length > 0).All(c => c.BodyEffect.Contains("1.5s")) && log.Contacts.Count(c => c.BodyEffect.Length > 0) == 3, "곡: three bodies, each bound once for 1.5 s");
                        if (res != null)
                        {
                            float lin = res.Footprint.GrowLinear;
                            Need(Mathf.Abs(lin * lin - (float)n.Get("peak.곡.area")) < 1e-3f, "곡: area x1.5 = linear " + lin.ToString("0.####") + " squared");
                            var gc = log.Contacts.FirstOrDefault(c => c.Kind == "Residue" && Strip(c.TargetName) == "G");
                            Need(gc != null && gc.NowUs - res.CreatedUs >= 2000000, "곡: G (beyond the first reach) is taken in only after the fan has grown (at " + (gc != null ? (gc.NowUs - res.CreatedUs) : -1) + " us)");
                        }
                        break;
                    }
                case '국':
                    {
                        sc.Planted.TryGetValue("P1_mine_inside", out var p1); sc.Planted.TryGetValue("P2_mine_outside", out var p2); sc.Planted.TryGetValue("P3_enemy_inside", out var p3);
                        long life = Us("timing.g.life");
                        Need(p1 != null && p1.LifeExtended && p1.LifeUs == (long)(life * n.Get("peak.국.lifeMultiplier")), "국: my residue inside the ring lives x1.5 (" + (p1 != null ? p1.LifeUs : -1) + " us)");
                        Need(p2 != null && !p2.LifeExtended && p2.LifeUs == life && p3 != null && !p3.LifeExtended && p3.LifeUs == life, "국: the residue outside the ring and the enemy's are not stretched");
                        Need(p1 != null && p1.End == Vol2MaterialEnd.Expired && p1.Drawn == 0m, "국: only the life changes, never the amount (drawn " + (p1 != null ? Fmt(p1.Drawn) : "?") + ")");
                        Need(log.Extended.Count == 1 && log.FieldNotes.Count >= 1, "국: one extension logged, the platform request recorded (" + string.Join(" / ", log.FieldNotes) + ")");
                        break;
                    }
            }
            v.Checks = string.Join(" | ", checks);
        }

        void SpreadPeaks(Vol2SpellRowDto row, Vol2LabScenario sc, Vol2CastLog log, GlyphRulesV3.Plan plan, NumbersV3Dto n, Vol2CastCoordinator co, List<string> problems, Vol2OnceVerdict v)
        {
            char letter = row.Letter; var checks = new List<string>();
            void Need(bool ok, string what) { if (!ok) problems.Add(what); else checks.Add(what); }
            var forward = log.Contacts.Where(c => c.Kind == "Spread" && c.BudgetAccepted).ToList();
            switch (letter)
            {
                case '난':
                    Need(forward.Count == 3 && forward.All(c => Near(c.Amount, n.Get("peak.spread.amountPerTarget"))), "난: three bodies, 0.5 each, no thinning");
                    break;
                case '넌':
                    {
                        var carrier = log.Materials.FirstOrDefault(m => m.Kind == Vol2MaterialKind.Carrier);
                        Need(carrier != null && carrier.Initial == 0m && carrier.Attached && carrier.LifeUs == RuleTypesV3.Microseconds(n.Get("timing.attachment.life")), "넌: the body carries a 6 s fire carrier holding 0");
                        Need(forward.Count == 2 && forward.All(c => Near(c.Amount, n.Get("peak.spread.amountPerTarget"))), "넌: two bodies, 0.5 each, from the one shared 1.5");
                        Need(forward.Count == 2 && forward[0].NowUs < forward[1].NowUs && forward[1].NowUs - forward[0].NowUs >= 150000, "넌: the second body is spread to by the second swing");
                        Need(ctxSwings(sc) == 3 && log.SwingReports == 9, "넌: three swings reported, three colliders each");
                        break;
                    }
                case '논':
                    {
                        var back = log.Contacts.Where(c => c.Kind == "ReturnSpread" && c.BudgetAccepted).ToList();
                        Need(back.Count == 3 && forward.Count == 3, "논: three forward, three back");
                        Need(back.Count > 0 && forward.Count > 0 && back[0].NowUs - forward[forward.Count - 1].NowUs >= co.VerbTuning.BackSpreadDelayUs, "논: the back-spread follows after the pause");
                        Need(log.Contacts.Count(c => c.Kind == "Spread" || c.Kind == "ReturnSpread") == 6, "논: no third pass, no grandchildren (6 spread contacts in all)");
                        Need(back.All(c => forward.Any(f => f.TargetId == c.TargetId)), "논: the back-spread reaches only the bodies the forward one did");
                        break;
                    }
                case '눈':
                    {
                        Vol2Material M(string key) { sc.Planted.TryGetValue(key, out var m); return m; }
                        Need(M("W1_mine_band") != null && M("W1_mine_band").End == Vol2MaterialEnd.Consumed && M("W2_vinedoor_band").End == Vol2MaterialEnd.Consumed && M("W3_mine_chain").End == Vol2MaterialEnd.Consumed, "눈: the wood the ring reaches and the wood next to it burns (Consumed)");
                        Need(M("W4_mine_far").End == Vol2MaterialEnd.Expired, "눈: the far wood is not burnt");
                        int burntEnemy = sc.Planted.Where(kv => kv.Key.StartsWith("E", StringComparison.Ordinal)).Count(kv => kv.Value.End == Vol2MaterialEnd.Consumed);
                        Need(burntEnemy == (int)n.Get("peak.눈.burnCount") && M("E5_enemy_band").End == Vol2MaterialEnd.Expired, "눈: four enemy patches burn, the fifth stays (" + burntEnemy + ")");
                        Need(log.FieldNotes.Count == 1 && log.FieldNotes[0].Contains("vine door"), "눈: the vine door burning is reported to the field (" + string.Join(" / ", log.FieldNotes) + ")");
                        var t1 = log.Contacts.FirstOrDefault(c => c.Kind == "Spread" && Strip(c.TargetName) == "T1");
                        Need(t1 != null && Near(t1.Amount, plan.Term(EffectKind.Spread)), "눈: the body standing in burning wood takes the whole 1.0");
                        break;
                    }
            }
            v.Checks = string.Join(" | ", checks);
        }

        static int ctxSwings(Vol2LabScenario sc) => sc.Steps.Count(s => s.Name.StartsWith("swing", StringComparison.Ordinal));
    }
}
