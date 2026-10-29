using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>An enemy attack element for a lab dummy (the 날붓 needs to know what the target's attacks carry). Lab only.</summary>
    public sealed class Vol2LabAttackElement : MonoBehaviour, IEnemyAttackElement308
    {
        public Element Value;
        public bool TryAttackElement(out Element element) { element = Value; return true; }
    }

    /// <summary>The 20 glyphs of 가른다 (ㅅ) of the spell lab (S5-b slice 6). One scenario per frame; the EXPECTED sets are written here by hand from the
    /// frame geometry and the table sentences (never read back from a run):
    ///  path     A at 4 m, B behind it, materials M1 (on the path before A) M2 (beyond A) M3 (off the path); 삿: four in a row (3, 6, 9, 11 m), strip three,
    ///           the path runs on and splits the material between B and C;
    ///  fan      two bodies inside, one beyond, one behind; materials: mine and an enemy's inside the fan, one outside; 솟: a body walks into the metal;
    ///  ring     one on the band, one in the hollow, one that crosses twice, one that crosses late; a material on the band at the start and one that
    ///           lands on it a second later; 숫 also asks the field for the gate;
    ///  attach   A B C D E as in the 남긴다 lab (D a fourth: TargetCap), two swings 0.4 s apart; materials under the caster, beyond reach, behind the
    ///           caster and beside the swing (those two only 섯 cuts); A carries an attack element the glyph's body overcomes (날붓).</summary>
    public sealed class Vol2SplitLab : IVol2LabProvider
    {
        public string Name => "Split";
        public bool Handles(Vol2SpellRowDto row) => row.Verb == Verb.Split;

        sealed class Extra
        {
            public string[] Strip = new string[0];
            public int RearHits;
            public string[] SplitTags = new string[0], UnsplitTags = new string[0];
            public decimal PerTarget;
            public int StripEntries, Restrips;
            public bool Blade;
            public string BladeCounter = "";
            public int BladeHits;
            public int Swings;
        }
        readonly ConditionalWeakTable<Vol2LabScenario, Extra> Extras = new ConditionalWeakTable<Vol2LabScenario, Extra>();

        // ------------------------------------------------------------------ scenarios ------------------------------------------------------------------

        public Vol2LabScenario Build(Vol2SpellRowDto row, Vol2LabContext ctx)
        {
            var caster = ctx.Shapes.Caster;
            var sc = new Vol2LabScenario { Origin = caster.Position, Forward = Vol2ShapeGeometry.FlatUnit(caster.AimDirection) };
            ctx.Scenario = sc;
            var g = row.Glyph; var e = sc.Expect; var x = new Extra();
            Extras.Add(sc, x);
            char letter = g.Letter;
            bool peak = row.Glyph.Kind == Oheangbu.Vol2.Rules.SpellKind.Peak;
            void Step(double at, string name, Action<Vol2LabContext> act) { sc.Steps.Add(new Vol2LabStep { AtSeconds = at, Name = name, Do = act }); }
            decimal instant = ctx.Services.Rulebook.Numbers.Get("action.instant");

            switch (g.Frame)
            {
                case Frame.Path:
                    if (!peak)
                    {
                        ctx.Dummy("A", 4f, 0f); ctx.Dummy("B", 8f, 0f);
                        ctx.Plant("M1_before", Body.Wood, 2f, 0f, Vol2Faction.Player);
                        ctx.Plant("M2_beyond", Body.Wood, 6.5f, 0.1f, Vol2Faction.Enemy);
                        ctx.Plant("M3_aside", Body.Wood, 2f, 3.5f, Vol2Faction.Player);
                        e.Instant = new[] { "A" }; e.Absent = new[] { "B" };
                        x.Strip = new[] { "A" }; x.SplitTags = new[] { "M1_before" }; x.UnsplitTags = new[] { "M2_beyond", "M3_aside" };
                        x.PerTarget = instant; x.StripEntries = 1;
                        sc.Describe = "path ends at A (4 m): the material on the path before A (M1) is cut in two, the one beyond A (M2) and the one off the path (M3) are not; A's guard and armour are stripped 3 s (0.4 booked once); B behind A is never touched";
                    }
                    else
                    {
                        ctx.Dummy("A", 3f, 0f); ctx.Dummy("B", 6f, 0f); ctx.Dummy("C", 9f, 0f); ctx.Dummy("D", 11f, 0f);
                        ctx.Plant("M1_before", Body.Wood, 1.5f, 0f, Vol2Faction.Player);
                        ctx.Plant("M2_between", Body.Wood, 7.5f, 0.1f, Vol2Faction.Enemy);
                        ctx.Plant("M3_aside", Body.Wood, 2f, 3.5f, Vol2Faction.Player);
                        e.Instant = new[] { "A" }; e.Absent = new[] { "D" };
                        e.Rejected["B"] = "FirstDamageOnly"; e.Rejected["C"] = "FirstDamageOnly";
                        x.Strip = new[] { "A", "B", "C" }; x.SplitTags = new[] { "M1_before", "M2_between" }; x.UnsplitTags = new[] { "M3_aside" };
                        x.PerTarget = instant * ctx.Services.Rulebook.Numbers.Get("peak.삿.firstHitMultiplier"); x.StripEntries = 3;
                        sc.Describe = "삿: four bodies in a row (3, 6, 9, 11 m); the path runs on: it cuts the cut-in-two material between B and C as well; A takes the cut x1.5 (1.05), B and C are only stripped (the rules refuse a second paid cut), the fourth body D is not touched; three strips";
                    }
                    break;
                case Frame.Fan:
                    ctx.Dummy("In3", 3f, 0f); ctx.Dummy("In4.8", 4.8f, -0.3f); ctx.Dummy("Out5.8", 5.8f, 0f); ctx.Dummy("Behind", -2f, 0f);
                    ctx.Plant("P1_mine_inside", Body.Wood, 3.5f, 1.0f, Vol2Faction.Player);
                    ctx.Plant("P2_enemy_inside", Body.Wood, 2f, -0.8f, Vol2Faction.Enemy);
                    ctx.Plant("P3_outside", Body.Wood, 7f, 0f, Vol2Faction.Player);
                    ctx.Plant("P4_behind", Body.Wood, -1.5f, 0f, Vol2Faction.Player);
                    e.Instant = new[] { "In3", "In4.8" }; e.Absent = new[] { "Out5.8", "Behind" };
                    x.Strip = new[] { "In3", "In4.8" }; x.SplitTags = new[] { "P1_mine_inside", "P2_enemy_inside" }; x.UnsplitTags = new[] { "P3_outside", "P4_behind" };
                    x.PerTarget = instant * 2; x.StripEntries = 2;
                    if (letter == '솟')
                    {
                        ctx.Dummy("L", 7f, 0f);
                        Step(0.8, "L walks into the metal", c => c.Move("L", 3.5f, -0.5f));
                        e.Residue = new[] { "In3", "In4.8", "L" };
                        sc.Describe = "솟: the fan cuts the materials inside it (mine and the enemy's), strips In3 and In4.8, then leaves a metal residue (0.35, 4 s) on the split; L walks in a moment later and is cut once; nothing outside the fan is touched";
                    }
                    else sc.Describe = "fan: every material inside it (mine P1, the enemy's P2) is cut in two, the ones outside (P3, behind P4) are not; every body inside (In3, In4.8) is stripped 3 s; the 0.4 is booked once; Out5.8 and Behind are not touched";
                    break;
                case Frame.Ring:
                    {
                        ctx.Dummy("Band2.0", 2f, 0f); ctx.Dummy("Hole0.5", 0.5f, 0.3f); ctx.Dummy("C", 3.4f, -1f); ctx.Dummy("D", 6f, 0f); ctx.Dummy("E", 6f, 2f);
                        ctx.Plant("R1_band", Body.Wood, 0f, 2.0f, Vol2Faction.Player);
                        ctx.Plant("R2_hollow", Body.Wood, 0.3f, 0.3f, Vol2Faction.Player);
                        ctx.Plant("R3_outside", Body.Wood, 6f, -3f, Vol2Faction.Player);
                        Step(0.8, "C crosses the band inward", c => c.Move("C", 0.1f, 0f));
                        Step(1.2, "a material lands on the band", c => c.Plant("R4_later", Body.Wood, -2f, 0.3f, Vol2Faction.Enemy));
                        Step(1.6, "C crosses back", c => c.Move("C", 3.4f, -1f));
                        Step(2.4, "D crosses the band", c => c.Move("D", 0.1f, -0.1f));
                        e.Instant = new[] { "Band2.0", "C", "D" }; e.Absent = new[] { "Hole0.5", "E" };
                        x.Strip = new[] { "Band2.0", "C", "D" }; x.SplitTags = new[] { "R1_band", "R4_later" }; x.UnsplitTags = new[] { "R2_hollow", "R3_outside" };
                        x.PerTarget = instant * 3; x.StripEntries = 4; x.Restrips = 1;
                        sc.SettleSeconds = 0.8;
                        sc.Describe = "ring (a 10 s line, not a material; C and D cross to the middle so the earth push cannot walk them back onto the band): R1 on the band is cut at once, R4 that lands on it later is cut too, the one in the hollow and the one outside are not; Band2.0 is stripped, C crosses twice (stripped twice, paid once), D crosses late; Hole0.5 and E never touch the band" + (letter == '숫' ? "; 숫: 4.5 s strips and the field is asked to cut a rock / iron gate" : "");
                        break;
                    }
                default:
                    {
                        sc.UsesSwings = true;
                        foreach (var d in new[] { ("A", 0.8f, 0f), ("B", 1.0f, 0.3f), ("C", 1.2f, -0.3f), ("D", 1.4f, 0f), ("E", 3.5f, 0f) }) ctx.Dummy(d.Item1, d.Item2, d.Item3);
                        var a = sc.Find("A");
                        var beaten = Enum.GetValues(typeof(Body)).Cast<Body>().First(b2 => ReactionRulesV3.Beats(g.Body, b2));
                        var element = a.Go.AddComponent<Vol2LabAttackElement>(); element.Value = Vol2Mapping.ToElement(beaten);
                        ctx.Plant("M1_under_caster", Body.Wood, 0f, 0f, Vol2Faction.Player);
                        ctx.Plant("M2_far", Body.Wood, 5f, 3f, Vol2Faction.Player);
                        ctx.Plant("M3_behind", Body.Wood, -1.2f, 0f, Vol2Faction.Player);
                        ctx.Plant("M5_beside_swing", Body.Wood, 1.2f, 0.9f, Vol2Faction.Enemy);
                        Step(0.05, "swing 1", c => c.Swing()); Step(0.45, "swing 2", c => c.Swing()); Step(0.7, "swing 3", c => c.Swing());
                        e.Instant = new[] { "A", "B", "C" }; e.Second = new[] { "A", "B", "C" }; e.Absent = new[] { "E" };
                        e.Rejected["D"] = "TargetCap";
                        x.Strip = new[] { "A", "B", "C" }; x.StripEntries = 3; x.Blade = true; x.Swings = 3; x.BladeCounter = "A";
                        x.PerTarget = (instant + ctx.Services.Rulebook.Numbers.Get("action.eo.second")) * 3;
                        if (letter == '섯')
                        {
                            x.RearHits = 6; x.PerTarget += instant * ctx.Services.Rulebook.Numbers.Get("peak.섯.rearMultiplier") * 2 * 3;
                            x.SplitTags = new[] { "M1_under_caster", "M3_behind", "M5_beside_swing" }; x.UnsplitTags = new[] { "M2_far" };
                            sc.Describe = "섯: two swings each cut front AND back: A B C (the first three) take the cut, the second contact and the rear cut (x0.5) with each, D is refused (TargetCap); the material under the caster, the one behind it (rear capsule) and the one beside the swing (front capsule) are cut, the far one is not; A's attack element is overcome by the glyph's body: 날붓";
                        }
                        else
                        {
                            x.SplitTags = new[] { "M1_under_caster" }; x.UnsplitTags = new[] { "M2_far", "M3_behind", "M5_beside_swing" };
                            sc.Describe = "ㅓ: swing 1 touches A B C (D, a fourth, is refused TargetCap) and strips them 3 s; swing 2 is the second contact; the material the caster stands in is cut, the others (far, behind, beside the swing) are not; A's attack element is overcome by the glyph's body: 날붓 on each direct hit";
                        }
                        break;
                    }
            }
            return sc;
        }

        // ------------------------------------------------------------------ verdict ------------------------------------------------------------------

        static string Strip(string name) => name.Replace("Vol2LabDummy_", "");
        static string Fmt(decimal d) => d.ToString("0.#####", CultureInfo.InvariantCulture);
        static bool Near(decimal a, decimal b) => Math.Abs(a - b) <= 0.000001m;
        static string Word(Body b) => b == Body.Wood ? "bind" : b == Body.Fire ? "burn" : b == Body.Earth ? "push" : b == Body.Metal ? "cut" : "wet";

        public Vol2OnceVerdict Evaluate(Vol2SpellRowDto row, Vol2LabScenario sc, Vol2CastLog log, Vol2CastDecision decision, Vol2LabContext ctx)
        {
            var v = new Vol2OnceVerdict();
            var problems = new List<string>(); var checks = new List<string>();
            var e = sc.Expect; var g = row.Glyph; var co = ctx.Coordinator; var n = ctx.Services.Rulebook.Numbers;
            Extras.TryGetValue(sc, out var x); x = x ?? new Extra();
            void Need(bool ok, string what) { if (!ok) problems.Add(what); else checks.Add(what); }
            char letter = g.Letter; bool gate = letter == '숫', metal = letter == '솟', twice = letter == '섯', pierce = letter == '삿';

            Vol2SplitExecutor ex = null;
            if (co.Verbs.TryGet(Verb.Split, out var exec)) ex = exec as Vol2SplitExecutor;
            Need(ex != null, "the 가른다 executor is registered");
            var data = ex != null ? ex.DataOf(log.Cast) : null;
            Need(data != null, "the cast has split data");
            if (ex == null || data == null) { v.Pass = false; v.Note = string.Join("; ", problems); return v; }

            Need(log.Status == "Done", "cast status " + log.Status);
            List<string> Names(string kind) => log.Contacts.Where(c => c.Kind == kind && c.BudgetAccepted).Select(c => Strip(c.TargetName)).ToList();
            void SameSet(string kind, string[] want)
            {
                var got = Names(kind).OrderBy(s => s, StringComparer.Ordinal).ToList(); var exp = want.OrderBy(s => s, StringComparer.Ordinal).ToList();
                Need(got.SequenceEqual(exp), kind + " [" + string.Join("|", got) + "] expected [" + string.Join("|", exp) + "]");
            }
            SameSet("Instant", e.Instant); SameSet("Second", e.Second); SameSet("Residue", e.Residue);
            foreach (var a in e.Absent) Need(!log.Contacts.Any(c => Strip(c.TargetName) == a), a + " is not touched");
            foreach (var kv in e.Rejected)
            {
                var recs = log.Contacts.Where(c => Strip(c.TargetName) == kv.Key && c.Kind == "Touch").ToList();
                Need(recs.Count > 0 && recs.All(c => !c.BudgetAccepted && c.BudgetReason == kv.Value), kv.Key + " is refused with " + kv.Value + " (" + string.Join("|", recs.Select(c => c.BudgetReason)) + ")");
            }
            foreach (var c in log.Contacts.Where(c => c.BudgetAccepted && (c.Kind == "Instant" || c.Kind == "Second" || c.Kind == "Rear" || c.Kind == "Residue")))
                if (!c.DamageApplied) problems.Add(Strip(c.TargetName) + " " + c.Kind + " damage not applied (" + c.DamageNote + ")");

            // one body action per body, the body's own, and only on bodies the cast really hit
            string word = Word(g.Body);
            foreach (var grp in log.Contacts.Where(c => c.BodyEffect.Length > 0).GroupBy(c => c.TargetId))
                if (grp.Count() != 1) problems.Add(Strip(grp.First().TargetName) + " took " + grp.Count() + " body actions");
            foreach (var name in e.Instant)
            {
                var first = log.Contacts.FirstOrDefault(c => c.BodyEffect.Length > 0 && Strip(c.TargetName) == name);
                if (first == null || !first.BodyEffect.StartsWith(word, StringComparison.Ordinal)) problems.Add(name + " body action '" + (first != null ? first.BodyEffect : "none") + "', expected " + word);
            }

            // the budget: the touches per target, the 0.4 once, the metal as one transfer
            var budget = log.Budget;
            Need(Near(budget.PaidPerTarget, x.PerTarget), "paid per target " + Fmt(budget.PaidPerTarget) + ", expected " + Fmt(x.PerTarget));
            Need(Near(budget.PaidShared, n.Get("action.strip.utility")), "paid shared " + Fmt(budget.PaidShared) + ", expected " + Fmt(n.Get("action.strip.utility")) + " (the strip's value, booked once)");
            Need(Near(budget.Transferred, metal ? n.Get("peak.솟.residueAmount") : 0m), "transferred " + Fmt(budget.Transferred) + (metal ? ", expected 0.35 (the metal)" : ", expected 0 (a split makes no material)"));
            var stripRecs = log.Contacts.Where(c => c.Kind == "Strip").ToList();
            Need(Near(stripRecs.Where(c => c.BudgetAccepted).Sum(c => c.Amount), n.Get("action.strip.utility")) && stripRecs.Count(c => c.Amount > 0m) == 1, "the 0.4 is on exactly one strip record");

            // the strip: who, how long, state only
            long wantUs = RuleTypesV3.Microseconds(n.Get(gate ? "combat.strip.peakDuration" : "combat.strip.duration"));
            var entries = data.Strips;
            Need(entries.Count == x.StripEntries, "strip entries " + entries.Count + ", expected " + x.StripEntries);
            var stripped = entries.Select(s => Strip(s.TargetName)).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
            Need(stripped.SequenceEqual(x.Strip.OrderBy(s => s, StringComparer.Ordinal)), "stripped [" + string.Join("|", stripped) + "] expected [" + string.Join("|", x.Strip) + "]");
            Need(entries.All(s => s.DurationUs == wantUs && s.UntilUs - s.AtUs >= wantUs), "every strip lasts " + Fmt(wantUs / 1000000m) + " s (" + string.Join("|", entries.Select(s => s.DurationUs)) + ")");
            Need(entries.Count(s => s.Restrip) == x.Restrips, "recrossings " + entries.Count(s => s.Restrip) + ", expected " + x.Restrips);
            Need(entries.Count(s => s.Value > 0m) == 1 && Near(entries.Sum(s => s.Value), n.Get("action.strip.utility")), "one strip carries the 0.4 (" + Fmt(entries.Sum(s => s.Value)) + ")");
            Need(entries.All(s => s.PortNote.StartsWith("strip ok", StringComparison.Ordinal)), "the enemy's guard and armour were really stripped (" + string.Join(" / ", entries.Select(s => s.PortNote).Distinct()) + ")");
            Need(log.Contacts.Where(c => c.Kind == "Strip").All(c => c.BodyEffect.Length == 0 && !c.DamageRequested), "a strip is not damage");

            // the materials: cut in two or left alone; ids, lives, balances kept; nothing new made except the 솟 metal
            Vol2Material M(string key) { sc.Planted.TryGetValue(key, out var m); return m; }
            var splitTags = data.Splits.Select(s => s.Parent.Tag).OrderBy(s => s, StringComparer.Ordinal).ToList();
            Need(splitTags.SequenceEqual(x.SplitTags.OrderBy(s => s, StringComparer.Ordinal)), "split [" + string.Join("|", splitTags) + "] expected [" + string.Join("|", x.SplitTags) + "]");
            int parts = (int)n.Get("reaction.split.parts"); decimal each = n.Get("reaction.split.amountPerPart");
            foreach (var s in data.Splits)
            {
                var p = s.Parent;
                bool ok = s.Children.Count == parts - 1 && p.Splits == 1 && Near(p.Remaining, s.Before * each) && s.Children.All(k => Near(k.Remaining, s.Before * each) && k.Cast == p.Cast && k.CreatedUs == p.CreatedUs && k.ExpiresUs == p.ExpiresUs
                    && k.OwnerId == p.OwnerId && k.FactionId == p.FactionId && k.Depth == p.Depth && k.Body == p.Body && k.Letter == p.Letter && k.Kind == p.Kind);
                ok = ok && Near(p.Remaining + s.Children.Sum(k => k.Remaining), s.Before) && p.Drawn == 0m && p.Initial == s.Before;
                Need(ok, "split " + p.Tag + ": " + Fmt(s.Before) + " -> " + Fmt(p.Remaining) + " + " + string.Join(" + ", s.Children.Select(k => Fmt(k.Remaining))) + ", id / life / owner / expiry kept");
            }
            foreach (var tag in x.UnsplitTags)
            {
                var m = M(tag);
                Need(m != null && m.Splits == 0 && Near(m.Remaining, m.Initial) && !data.Splits.Any(s => s.Parent == m || s.Children.Contains(m)), tag + " is left whole");
            }
            Need(co.Materials.ByCast(log.Cast).Count == (metal ? 1 : 0), "materials made by the cast: " + co.Materials.ByCast(log.Cast).Count + " (a split makes none" + (metal ? ", the metal is the one exception" : "") + ")");
            Need(co.Materials.Audit().Count == 0, "material audit: " + string.Join("; ", co.Materials.Audit()));

            // peaks
            if (metal)
            {
                var res = log.Materials.FirstOrDefault();
                Need(res != null && res.Body == Body.Metal && res.Kind == Vol2MaterialKind.Residue && res.Initial == n.Get("peak.솟.residueAmount") && res.LifeUs == RuleTypesV3.Microseconds(n.Get("peak.솟.residueLife")) && res.Footprint.Kind == Vol2FootprintKind.Fan && res.FactionId == Vol2Faction.Player && res.Depth == 0,
                    "솟: one metal residue 0.35 / 4 s on the fan");
                if (res != null)
                {
                    Need(Near(res.Drawn + res.Remaining, res.Initial) && Near(res.Drawn, log.AmountOf("Residue")), "솟: the metal held " + Fmt(res.Initial) + ", drawn " + Fmt(res.Drawn) + " + left " + Fmt(res.Remaining));
                    Need(res.End == Vol2MaterialEnd.Consumed || res.End == Vol2MaterialEnd.Expired, "솟: the metal ended " + res.End);
                    v.MaterialIds = res.Id.Value.ToString(CultureInfo.InvariantCulture);
                }
            }
            if (twice)
            {
                var rears = log.Contacts.Where(c => c.Kind == "Rear" && c.BudgetAccepted).ToList();
                Need(rears.Count == x.RearHits && rears.All(c => Near(c.Amount, n.Get("peak.섯.rearAmount"))) && data.RearHits == x.RearHits, "섯: " + rears.Count + " rear cuts of 0.35 (two per body, x0.5 of 0.7)");
                Need(rears.Count > 0 && Strip(rears[0].TargetName) == "A", "섯: the rear cut rides with the first body's first contact");
                Need(ctx.SwingsRaised == x.Swings, "섯: " + ctx.SwingsRaised + " swings reported");
            }
            if (pierce)
            {
                var first = log.Contacts.FirstOrDefault(c => c.Kind == "Instant");
                Need(first != null && Strip(first.TargetName) == "A" && Near(first.Amount, n.Get("peak.삿.instant")), "삿: the cut x1.5 (1.05) lands on the first body only");
                Need(log.Contacts.Count(c => c.Kind == "Instant") == 1, "삿: no second paid cut");
            }
            if (gate) Need(log.FieldNotes.Count == 1 && log.FieldNotes[0].Contains("cut rock/iron gate"), "숫: the gate cut is asked of the field (" + string.Join(" / ", log.FieldNotes) + ")");
            else Need(log.FieldNotes.Count == 0, "no field request (" + string.Join(" / ", log.FieldNotes) + ")");

            // 날붓: ㅓ+ㅅ only
            if (g.Frame == Frame.Attachment)
            {
                var blades = data.Blades;
                decimal blade = new DefenceNumbersV3Dto(n).BladePosture;
                Need(blades.Count == 6 && blades.All(b => b.Result.Called), "날붓: asked on each of the 6 direct hits (" + blades.Count + ", called " + blades.Count(b => b.Result.Called) + (blades.Count > 0 ? ", " + blades[0].Result : "") + ")");
                var a = blades.Where(b => Strip(b.TargetName) == "A").ToList();
                Need(a.Count == 2 && a.All(b => b.Result.Counter && b.Result.Accepted && Near(b.Result.Added, blade)) && a.Count >= 2 && Near(a[1].Result.PostureAfter, a[0].Result.PostureAfter + blade),
                    "날붓: A (its attack element is overcome) gains " + Fmt(blade) + " twice" + (a.Count > 0 ? " (" + string.Join(" -> ", a.Select(b => Fmt(b.Result.PostureAfter))) + ")" : ""));
                Need(blades.Where(b => Strip(b.TargetName) != "A").All(b => !b.Result.Counter && b.Result.Added == 0m), "날붓: B and C (no overcome element) gain nothing");
                Need(blades.Select(b => b.Attack.Value).Distinct().Count() == blades.Count && data.BladeDuplicates == 0, "날붓: one signal per hit (no duplicate)");
            }

            v.Created = string.Join("|", data.Splits.Select(s => s.Parent.Id + "->" + string.Join("+", s.Children.Select(k => k.Id.ToString())))) + (log.Materials.Count > 0 ? "|" + string.Join("|", log.Materials.Select(m => m.Id + ":" + m.Kind + ":" + Fmt(m.Initial))) : "");
            v.Consumed = string.Join("|", data.Splits.SelectMany(s => new[] { s.Parent }.Concat(s.Children)).Select(m => m.Id + ":" + m.End));
            v.Depth = data.Splits.Count > 0 ? data.Splits.SelectMany(s => new[] { s.Parent }.Concat(s.Children)).Max(m => m.Depth).ToString(CultureInfo.InvariantCulture) : "";
            v.Owner = data.Splits.Count > 0 ? string.Join("|", data.Splits.Select(s => s.Parent.Tag + ":" + (s.Parent.FactionId == Vol2Faction.Enemy ? "enemy" : "caster"))) : "";
            v.Balance = Fmt(budget.SharedRemaining);
            v.Ledger = string.Join("|", log.Contacts.Where(c => c.BudgetAccepted && c.Amount > 0m).Select(c => c.Kind + ":" + Strip(c.TargetName) + ":" + Fmt(c.Amount)));
            v.Hit = string.Join("|", log.Contacts.Select(c => Strip(c.TargetName)).Distinct().OrderBy(s => s, StringComparer.Ordinal));
            v.Expected = string.Join("|", e.Instant.Concat(e.Residue).Concat(x.Strip).Distinct().OrderBy(s => s, StringComparer.Ordinal));
            v.Unexpected = "";
            v.Units = log.TotalUnits; v.Hp = log.TotalHp;
            v.BodyObserved = string.Join("; ", log.Contacts.Where(c => c.BodyEffect.Length > 0).Select(c => Strip(c.TargetName) + " " + c.BodyEffect))
                + (data.Strips.Count > 0 ? " || strips: " + string.Join("; ", data.Strips.Select(s => Strip(s.TargetName) + (s.Restrip ? "(again)" : "") + " " + Fmt(s.DurationUs / 1000000m) + "s")) : "")
                + (data.Blades.Count > 0 ? " || blade: " + string.Join("; ", data.Blades.Select(b => Strip(b.TargetName) + " " + b.Kind + " " + b.Result)) : "");
            v.Checks = string.Join(" | ", checks);
            v.Pass = problems.Count == 0; v.Note = string.Join("; ", problems);
            return v;
        }
    }
}
