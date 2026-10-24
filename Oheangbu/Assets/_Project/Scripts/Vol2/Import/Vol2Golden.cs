using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Vol2.Rules;
using R = Oheangbu.Vol2.Rules.ReactionRulesV3;
namespace Oheangbu.Vol2.Import
{
    public sealed class GoldenReport
    {
        public int Pass { get; private set; }
        public int Fail { get; private set; }
        public List<string> Failures { get; } = new List<string>();
        public string FirstFailure => Failures.Count > 0 ? Failures[0] : null;
        public void Check(string id, bool ok, string detail = "")
        {
            if (ok) { Pass++; return; }
            Fail++; if (Failures.Count < 40) Failures.Add(id + (detail.Length > 0 ? " " + detail : ""));
        }
        public void Equal<T>(string id, T expected, T actual) => Check(id, EqualityComparer<T>.Default.Equals(expected, actual), "expected=" + expected + " actual=" + actual);
        public void Near(string id, decimal expected, decimal actual) => Check(id, Math.Abs(expected - actual) <= 0.000000001m, "expected=" + expected + " actual=" + actual);
    }

    /// <summary>Replays the frozen golden fixtures (Tools/SpellVFX120/Pure308/Vol2V3/Golden) against numbers/spells that came back
    /// through the loader. Table-independent on purpose: the fixtures are derived from the E0 numbers and the rule contract, not from table
    /// text. 걱 is the one documented divergence (D308-145 heal exception): the golden still holds the old attack plan, so it is checked
    /// for being heal-only and left out of the rankings. Public API only; no engine types.</summary>
    public static class Vol2Golden
    {
        public static Vol2ImportManifestDto ReadTrustedManifest(string json)
        {
            var j = JsonNode.Parse(json); var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string k in j["sourceHashes"].Keys) hashes.Add(k, j["sourceHashes"][k].Str);
            return new Vol2ImportManifestDto(j["schema"].Str, j["importer"].Str, hashes, j["dtoHash"].Str);
        }

        public static GoldenReport Run(NumbersV3Dto n, IReadOnlyList<Vol2SpellRowDto> spells, string sourceJson, string eventsJson, string balanceJson)
        {
            var r = new GoldenReport();
            if (n == null || !n.IsValidated) { r.Check("numbers-validated", false, "validated numbers required"); return r; }
            var src = JsonNode.Parse(sourceJson); var events = JsonNode.Parse(eventsJson); var balance = JsonNode.Parse(balanceJson);
            r.Equal("golden-rows", 120, src["rows"].Count);
            r.Equal("spell-rows", 120, spells.Count);
            foreach (var row in src["rows"].Items) r.Check("golden-letter-present/" + row["letter"].Str, spells.Any(s => s.Letter == row["letter"].Str[0]));
            Rows(r, n, src); Rankings(r, n, src); Efficiency(r, n, balance); Events(r, n, events); Reactions(r, n, src);
            return r;
        }

        static GlyphRulesV3.Plan Plan(char letter, NumbersV3Dto n)
        { if (!GlyphRulesV3.TryDecode(letter, out var g)) throw new FormatException("illegal glyph " + letter); return GlyphRulesV3.Build(g, n); }

        static void Rows(GoldenReport r, NumbersV3Dto n, JsonNode src)
        {
            foreach (var row in src["rows"].Items)
            {
                char letter = row["letter"].Str[0]; string id = "source/" + letter; var p = Plan(letter, n);
                if (letter == '걱')
                {
                    r.Check(id + "/recovery", p.IsRecovery && p.Total == 0 && p.EffectTerms.Count == 0 && p.Cost == new DefenceNumbersV3Dto(n).HealingCost);
                    continue;
                }
                r.Near(id + "/cost", row["cost"].Dec, p.Cost); r.Near(id + "/per-target", row["perTarget"].Dec, p.PerTarget); r.Near(id + "/shared", row["shared"].Dec, p.Shared);
                for (int targets = 1; targets <= 4; targets++)
                {
                    int effective = p.FirstOnly ? 1 : p.Limited ? Math.Min(targets, 3) : targets;
                    r.Near(id + "/N" + targets, row["perTarget"].Dec * effective + row["shared"].Dec, p.TotalFor(targets));
                }
                if (p.Glyph.Kind != SpellKind.Peak) continue;
                var terms = row["terms"].Items;
                r.Equal(id + "/term-count", terms.Count, p.EffectTerms.Count);
                for (int i = 0; i < terms.Count && i < p.EffectTerms.Count; i++)
                {
                    var t = terms[i]; var a = p.EffectTerms[i];
                    r.Check(id + "/term" + i, t["name"].Str == a.Name && t["amount"].Dec == a.Amount && t["count"].Int == a.Count && t["shared"].Bool == a.Shared);
                }
                var peak = PeakRulesV3.Evaluate(p, new ContactProfile(), 1, false);
                r.Near(id + "/cap", row["cap"].Dec, peak.StructureForceCap); r.Check(id + "/structural-cap", peak.WithinStructuralCap);
                for (int targets = 1; targets <= 4; targets++) foreach (int contacts in new[] { 1, 2 })
                {
                    var profile = new ContactProfile(targets, contacts);
                    var plain = PeakRulesV3.Evaluate(p, profile, 1.15m, false); var counter = PeakRulesV3.Evaluate(p, profile, 1.15m, true);
                    r.Check(id + "/force-counter/N" + targets + "/C" + contacts, plain.FinalMultiplier <= 1.5m && counter.FinalMultiplier <= 1.875m && counter.FinalTotal == plain.FinalTotal * 1.25m);
                }
            }
            r.Equal("seot-total", 2.1m, Plan('섯', n).Total);
            r.Equal("seot-critical-max", 1.875m, PeakRulesV3.Evaluate(Plan('섯', n), new ContactProfile(), 1.15m, true).FinalMultiplier);
            r.Equal("normal-critical", 1.4375m, PeakRulesV3.Evaluate(Plan('넛', n), new ContactProfile(), 1.15m, true).FinalMultiplier);
            r.Equal("no-contact-no-shared", 0m, PeakRulesV3.Evaluate(Plan('섯', n), new ContactProfile(0, 0, false, false, false), 1, true).FinalTotal);
        }

        static void Rankings(GoldenReport r, NumbersV3Dto n, JsonNode src)
        {
            var plans = new List<GlyphRulesV3.Plan>();
            foreach (var row in src["rows"].Items) { char ch = row["letter"].Str[0]; if (ch != '걱') plans.Add(Plan(ch, n)); }
            for (int count = 1; count <= 4; count++)
            {
                var ranked = plans.Where(p => !p.Glyph.IsParry).OrderByDescending(p => p.ValuePerInk(count)).ToArray();
                var golden = src["rankings"][count.ToString()].Items.Where(x => x["letter"].Str[0] != '걱').ToArray();
                string id = "rank/N" + count; r.Equal(id + "/count", golden.Length, ranked.Length);
                for (int i = 0; i < golden.Length && i < ranked.Length; i++)
                {
                    char ch = golden[i]["letter"].Str[0]; decimal score = golden[i]["score"].Dec;
                    r.Near(id + "/score/" + ch, score, plans.Single(p => p.Glyph.Letter == ch).ValuePerInk(count));
                    r.Near(id + "/position/" + (i + 1), score, ranked[i].ValuePerInk(count)); // ties may reorder letters; the score at the position is exact
                }
            }
        }

        static void Efficiency(GoldenReport r, NumbersV3Dto n, JsonNode balance)
        {
            foreach (int targetCount in new[] { 1, 2 })
            {
                decimal bareAll = 0, normalAll = 0; int frames = 0;
                foreach (var pair in new[] { (Frame.Path, "ㅏ"), (Frame.Fan, "ㅗ"), (Frame.Ring, "ㅜ") })
                {
                    string f = RuleTypesV3.FrameKey(pair.Item1); int bareTargets = pair.Item1 == Frame.Path ? 1 : targetCount;
                    decimal bare = (decimal)bareTargets / n.Get("ink.bare." + f), normal = 0;
                    foreach (string verb in new[] { "g", "n", "m", "s", "ng" })
                    {
                        int k = pair.Item1 == Frame.Path && verb == "s" ? 1 : targetCount;
                        normal += (n.Get("action.normal." + verb + ".perTarget") * k + n.Get("action.normal." + verb + ".shared")) / n.Get("ink.normal." + f) / 5;
                    }
                    decimal efficiency = bare / normal;
                    r.Near("efficiency/N" + targetCount + "/" + f, balance["efficiency"][targetCount.ToString()][pair.Item2].Dec, efficiency);
                    if (targetCount == 1 || pair.Item1 != Frame.Path) { bareAll += bare; normalAll += normal; frames++; }
                }
                r.Near("efficiency-overall/N" + targetCount, balance["efficiency"][targetCount.ToString()]["전체"].Dec, (bareAll / frames) / (normalAll / frames));
            }
        }

        static void Events(GoldenReport r, NumbersV3Dto n, JsonNode fixture)
        {
            foreach (var test in fixture["cases"].Items)
            {
                string id = test["caseId"].Str; var p = Plan(test["letter"].Str[0], n);
                var s = new EffectBudgetV3.State(test["castId"].Long, p, test["startedUs"].Long);
                var commandIds = new HashSet<string>(); int index = 0;
                foreach (var item in test["events"].Items)
                {
                    var input = item["input"]; var x = item["expected"];
                    long L(string key, long fallback = 0) => input.Has(key) ? input[key].Long : fallback;
                    string S(string key, string fallback) => input.Has(key) ? input[key].Str : fallback;
                    decimal amount = input.Has("amount") ? input["amount"].Dec : 0;
                    var e = new Contact(L("eventId"), L("castId", s.CastId), L("targetId"), L("nowUs"), L("swingId"),
                        (ContactKind)Enum.Parse(typeof(ContactKind), S("kind", "Touch")), (EffectKind)Enum.Parse(typeof(EffectKind), S("effect", "Residue")), amount,
                        (TriggerKind)Enum.Parse(typeof(TriggerKind), S("trigger", "None")), L("lifeRevision"), L("materialId"));
                    decimal oldPaid = s.TotalPaid, oldShared = s.SharedRemaining; int oldSlots = s.Targets.Count;
                    var step = EffectBudgetV3.Apply(s, e, p); string prefix = id + "/" + (++index);
                    r.Check(prefix + "/immutable", s.TotalPaid == oldPaid && s.SharedRemaining == oldShared && s.Targets.Count == oldSlots);
                    r.Equal(prefix + "/accepted", x["accepted"].Bool, step.Accepted); r.Equal(prefix + "/reason", x["reason"].Str, step.Reason);
                    s = step.State;
                    r.Equal(prefix + "/per", x["per"].Dec, s.PaidPerTarget); r.Equal(prefix + "/shared", x["shared"].Dec, s.PaidShared);
                    r.Equal(prefix + "/remaining", x["remaining"].Dec, s.SharedRemaining); r.Equal(prefix + "/slots", x["slots"].Int, s.Targets.Count);
                    r.Equal(prefix + "/commands", x["commandAmount"].Dec, step.Commands.Sum(v => v.ActionAmount));
                    if (x.Has("transferred")) r.Equal(prefix + "/transferred", x["transferred"].Dec, s.Transferred);
                    if (x.Has("borrowed")) r.Equal(prefix + "/borrowed", x["borrowed"].Dec, s.BorrowedPaid);
                    if (x.Has("childBudget")) r.Equal(prefix + "/child", x["childBudget"].Dec, step.Commands.Sum(v => v.ChildBudget));
                    if (x.Has("hp")) r.Equal(prefix + "/hp", x["hp"].Dec, step.Commands.Sum(v => v.HpDamage));
                    if (x.Has("utility")) r.Equal(prefix + "/utility", x["utility"].Dec, step.Commands.Sum(v => v.UtilityValue));
                    r.Check(prefix + "/conservation", s.PaidShared + s.Transferred + s.SharedRemaining == p.Shared && s.TotalPaid == s.PaidPerTarget + s.PaidShared && s.Targets.All(t => t.Paid <= p.PerTarget));
                    r.Check(prefix + "/command-identity", step.Commands.All(cmd => commandIds.Add(cmd.Id) && cmd.CastId == e.CastId && cmd.TargetId == e.TargetId && cmd.LifeRevision == e.LifeRevision && cmd.MaterialId == e.MaterialId));
                    if (!step.Accepted) r.Equal(prefix + "/rejected-no-commands", 0, step.Commands.Count);
                }
            }
        }

        static R.Material M(long id, Body body) => new R.Material(id, id, body, 1, 0, 20000000, 7, 1, id * 10, 0, true);
        static void Reactions(GoldenReport r, NumbersV3Dto n, JsonNode src)
        {
            var rn = new ReactionNumbersV3Dto(n); var cells = src["reactionCells"].Items;
            string[] names = { "비화", "먹임", "옮겨붙음", "꺾음", "막힘" };
            r.Equal("reaction-cells", 25, cells.Count);
            for (int a = 0; a < 5; a++) for (int b = 0; b < 5; b++)
            {
                string id = "cell/" + a + "/" + b; var old = new R.State(new[] { M(1, (Body)a), M(2, (Body)b) });
                var e = new R.ContactPair(1, 1, 2, 100, 3, firstMoving: true, directAreaBudget: 1);
                var step = R.Resolve(old, e, rn); r.Check(id + "/accepted", step.Accepted, step.Reason); if (!step.Accepted) continue;
                var cmd = step.Commands[0];
                r.Check(id + "/golden", cells[a * 5 + b]["text"].Str.StartsWith(names[(int)cmd.Reaction], StringComparison.Ordinal));
                r.Check(id + "/immutable", old.Materials[1].Remaining == 1 && !old.Materials[1].Consumed && old.Materials.Count == 2);
                r.Check(id + "/no-side-effects", cmd.Groggy == 0 && !cmd.CanDetonate && !cmd.IsMaterial);
                r.Check(id + "/replay", !R.Resolve(step.State, e, rn).Accepted);
            }
        }
    }
}
