// SPEC-SPELL-120-308 L2 cases of the 2026-10-04 fix pass (decisions of the main agent inside the user's answers D308-13 / D308-13b):
//  a. a TEST-only row (Gate test): a normal save cannot resolve it, the TEST unlock can; the main game opens exactly the
//     five intended giyeok rows;
//  c. the order of a handler cast (SpellDispatchRule308): a presentation or listener failure never changes what was judged,
//     and ink never comes back while hits of the cast are scheduled or its state is on;
//  d. the interim presenter's cue life (SpellCueLife308): a catalogue body is never stretched to the life of a lasting effect.
// (b, the first stage of the single shots behind a final, is in Cases_WP11; the enemy -> player doorway and the modifier
// take-back are in Cases_WP07; the wave's strip is in Cases_WP13.)
// Not checked here (needs Unity objects, L3 / Play): the wiring's own steps (CombatLoopWiring.Spell308.cs), the presenter
// guard and the interim presenter's object handling, the effect registry (its failures list).
using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Spellcraft;

namespace Pure308
{
    public static partial class Program
    {
        // The gate of the wiring (CombatLoopWiring.Spell308.cs, ISpellGate.Judge) for the handler route, on the session's own
        // policy call: campaign / ledger / evidence / TEST unlock / isolated store are the session's facts.
        sealed class SaveGate : ISpellGate
        {
            public bool Campaign, Ledger, Evidence, TestUnlock, Isolated;
            public HashSet<string> Registered = new HashSet<string>();
            public SpellUnlockRule[] Unlocks = new SpellUnlockRule[0];
            public bool Legacy = true;
            public SpellResolveStatus Judge(SpellRow row)
            {
                if (row.Feature != SpellLegacyFeature.None) return new LegacySpellGate(Legacy, Legacy, Legacy, Legacy, Legacy).Judge(row);
                bool registered = Registered.Contains(row.Handler), unlocked = false;
                foreach (var rule in Unlocks)
                {
                    if (rule.Final != row.Final) continue;
                    var judged = SpellUnlockPolicy308.RuleFor(row, rule);
                    unlocked = registered && SpellUnlockPolicy308.Unlocked(Campaign, judged.GrantedInMain, Ledger, Evidence, TestUnlock, Isolated);
                }
                return SpellUnlockPolicy308.JudgeNew(row, registered, unlocked);
            }
        }

        // One handler cast on a model: what the wiring's steps do to ink, scheduled hits, handler state and presentation.
        sealed class DispatchModel : ISpellDispatchSteps308
        {
            public const float Cost = .25f;
            public int PrepareMode, SpendMode, CommitMode, AcceptMode, PresentMode;   // 0 = fine / true, 1 = answers false, 2 = throws
            public float Ink = 1f;
            public int Hits = 3;                 // hits other casts scheduled earlier: never touched
            public int Held = 1;                 // a shot an earlier cast's first stage still holds in flight: never touched
            public bool StateOn;                 // the handler's lasting state (a sword form, a zone)
            public int Waiting, Shown, Accepted, Failed, Reports, Withdrawals, HandlerResets;
            public readonly List<string> Order = new List<string>();
            float _inkBefore; int _hitsBefore, _heldBefore; bool _committing;

            bool Step(string name, int mode)
            {
                Order.Add(name);
                if (mode == 2) throw new InvalidOperationException(name);
                return mode == 0;
            }

            public bool Prepare() => Step("prepare", PrepareMode);
            public bool Spend()
            {
                _inkBefore = Ink;
                if (SpendMode == 1) { Order.Add("spend"); return false; }
                Ink -= Cost;                                     // the ink has left before anything can go wrong
                return Step("spend", SpendMode);
            }
            public bool Commit()
            {
                _hitsBefore = Hits; _heldBefore = Held; _committing = true;
                Hits += 2; Held++;                               // the handler schedules its hits and launches its held first stage ...
                Waiting++;                                       // ... asks for a presentation (held back) ...
                if (CommitMode != 1) StateOn = true;             // ... and switches its state on; a refusal is answered before that
                return Step("commit", CommitMode);
            }
            public void Withdraw(bool faulted)
            {
                Order.Add(faulted ? "withdraw-faulted" : "withdraw"); Withdrawals++;
                if (_committing)
                {
                    _committing = false; Waiting = 0; Hits = _hitsBefore; Held = _heldBefore;
                    if (faulted) { StateOn = false; HandlerResets++; }
                }
                Ink = _inkBefore;
            }
            public void Accept() { Accepted++; Step("accept", AcceptMode); }
            public void Present()
            {
                Order.Add("present");
                if (PresentMode == 2) throw new InvalidOperationException("present");   // the presenter breaks before it shows anything
                Shown += Waiting; Waiting = 0;
            }
            public void Fail() { Order.Add("fail"); Failed++; }
            public void Report(Exception exception) { Reports++; }
        }

        static partial void RunCore308(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("A5", false, "the fix-pass cases need the table"); return; }
            var rows = c.Build.Rows;

            // ---- a. TEST-only rows ----
            var ids = new HashSet<string>(rows.Where(x => x.HasHandler).Select(x => x.Handler));
            SaveGate Save(bool campaign, bool ledger, bool evidence, bool test, bool isolated) =>
                new SaveGate { Campaign = campaign, Ledger = ledger, Evidence = evidence, TestUnlock = test, Isolated = isolated, Registered = ids, Unlocks = c.Build.Unlocks };
            SpellResolveStatus Status(SpellRow row, ISpellGate gate) => SpellResolveCore308.Resolve(row.Letter[0], 1f, 1f, row, gate, out _);
            // the most a normal save can prove: the campaign runs, every ledger id is recorded, every evidence encounter is defeated
            var normal = Save(true, true, true, false, false);
            // the editor-only TEST unlock inside the isolated store (no campaign proof at all)
            var testing = Save(false, false, false, true, true);
            // a TEST unlock asked for outside the isolated store is not an unlock
            var leaked = Save(false, false, false, true, false);

            var testOnly = rows.Where(x => x.Gate == SpellGateMode.Test).ToArray();
            r.Fact("testOnlyRows", string.Join(" ", testOnly.Select(x => x.Index + ":" + x.Handler)));
            // grid cell 50 = earth a + giyeok (the rock that stays as cover): the one enemy / world dependent row behind the final
            // the main game grants. No glyph is named: the cell is found by its number, the rule by the sheet's Gate column.
            var rock = c.RowAt(50);
            r.Check("A5", testOnly.Length == 1 && testOnly[0] == rock && rock.Final == SpellFinal.Giyeok && rock.HasHandler && rock.Feature == SpellLegacyFeature.None,
                "exactly one row is TEST-only: the rock-cover row, behind the giyeok final", string.Join(" ", testOnly.Select(x => x.Index)));
            r.Check("A5", c.Build.Unlocks.Any(u => u.Final == rock.Final && u.GrantedInMain) && Status(rock, normal) == SpellResolveStatus.Locked,
                "a normal save cannot resolve it: with the campaign running, its final granted in the main game, the ledger id recorded and every evidence complete it is Locked (the misfire)",
                Status(rock, normal).ToString());
            r.Check("A5", Status(rock, testing) == SpellResolveStatus.Ok && Status(rock, leaked) == SpellResolveStatus.Locked,
                "the TEST unlock in the isolated store resolves it; the same request outside the isolated store does not");
            var opened = rows.Where(x => x.Final != SpellFinal.None && x.HasHandler && x.Feature == SpellLegacyFeature.None && x.Gate != SpellGateMode.Open && Status(x, normal) == SpellResolveStatus.Ok)
                .Select(x => x.Index).ToArray();
            r.Fact("mainGameNewRows", string.Join(" ", opened));
            // 14 wood o, 38 fire o, 62 earth o, 86 metal o, 110 water o, each + giyeok: the five glyphs the main story opens after its first final
            r.Check("A5", opened.SequenceEqual(new[] { 14, 38, 62, 86, 110 }),
                "the main game opens exactly the five intended new rows behind the giyeok final (the o glyphs of the five elements); nothing else", string.Join(" ", opened));
            var newRows = rows.Where(x => x.HasHandler && x.Feature == SpellLegacyFeature.None).ToArray();
            r.Check("A5", newRows.Length == 64 && newRows.All(x => Status(x, testing) == SpellResolveStatus.Ok),
                "with the TEST unlock in the isolated store all 64 new rows resolve, the TEST-only row among them", newRows.Count(x => Status(x, testing) == SpellResolveStatus.Ok).ToString());
            var giyeokRule = c.Build.Unlocks.First(u => u.Final == SpellFinal.Giyeok);
            var ordinary = c.RowAt(14);
            r.Check("A5", SpellUnlockPolicy308.RuleFor(rock, giyeokRule).GrantedInMain == false && SpellUnlockPolicy308.RuleFor(ordinary, giyeokRule).GrantedInMain &&
                SpellUnlockPolicy308.RuleFor(rock, giyeokRule).LedgerId == giyeokRule.LedgerId && SpellUnlockPolicy308.RuleFor(null, giyeokRule).GrantedInMain,
                "the per-row rule: a TEST-only row is judged without the main-game grant of its final; every other row keeps its final's rule as it is");
            // the sheet rule: Gate test only narrows a handler row behind a final
            string head = "글자,Mode,Handler,Pending,Kind,Gate,Feature,Inherit,BasePower,SpeedMul,AreaShape,AreaAngle,AreaRadius,AreaLength,AreaSpeed,AreaDelay,Shots,Interval,Scatter,Params\n";
            SpellBuildResult308 Sheet(string text) => SpellTableBuilder308.Build(c.CanonicalCsv, new List<SpellSheet308> { new SpellSheet308("Rules308_T", head + text) }, c.UnlockCsv);
            string bare = SpellGrammar308.LetterAt(1).ToString(), final = SpellGrammar308.LetterAt(2).ToString(), summon = SpellGrammar308.LetterAt(16).ToString();
            string baseRow = bare + ",,core.single,,AttackSingle,,book,,12,,,,,,,,,,,\n";
            var narrowed = Sheet(baseRow + final + ",,x.test,,,test,,base,,,,,,,,,,,,\n");
            r.Check("A5", narrowed.Ok && narrowed.Rows[1].Gate == SpellGateMode.Test && narrowed.CanonicalText.Contains("|Test|"),
                "Gate test is accepted on a handler row behind a final and is part of the table text (so the table hash sees it)");
            r.Check("A5", !Sheet(bare + ",,x.test,,AttackSingle,test,,,12,,,,,,,,,,,\n").Ok && !Sheet(baseRow + final + ",,ea.giyeok,,,test,giyeok,base,,,,,,,,,,,,\n").Ok &&
                !Sheet(summon + ",,core.summon,,Summon,test,book,,,,,,,,,,,,,\n").Ok && !Sheet(baseRow + final + ",,x.test,,,tset,,base,,,,,,,,,,,,\n").Ok,
                "Gate test is refused on a row without a final, on a legacy row (the 36 keep their own gates) and on a summon row; an unknown Gate word is refused");

            // ---- c. the order of a handler cast ----
            int scenarios = 0, casts = 0; var broken = new List<string>();
            for (int prepare = 0; prepare < 3; prepare++) for (int spend = 0; spend < 3; spend++) for (int commit = 0; commit < 3; commit++)
            for (int accept = 0; accept < 3; accept += 2) for (int present = 0; present < 3; present += 2)
            {
                var m = new DispatchModel { PrepareMode = prepare, SpendMode = spend, CommitMode = commit, AcceptMode = accept, PresentMode = present };
                var outcome = SpellDispatchRule308.Run(ref m);
                scenarios++;
                string name = prepare + "" + spend + commit + accept + present;
                bool judged = prepare == 0 && spend == 0 && commit == 0;
                var expected = judged ? SpellDispatchOutcome308.Cast : prepare != 0 || spend != 0 ? SpellDispatchOutcome308.Refused :
                    commit == 2 ? SpellDispatchOutcome308.Faulted : SpellDispatchOutcome308.Withdrawn;
                bool fine = outcome == expected;
                if (judged)
                {
                    casts++;
                    // the cast stands whatever the listeners and the presenter do: ink spent, both hits scheduled, state on
                    fine &= Math.Abs(m.Ink - (1f - DispatchModel.Cost)) < 1e-6f && m.Hits == 5 && m.Held == 2 && m.StateOn && m.Withdrawals == 0 && m.Failed == 0 && m.Accepted == 1;
                    fine &= m.Shown == (present == 0 ? 1 : 0) && m.Reports == (accept == 2 ? 1 : 0) + (present == 2 ? 1 : 0);
                    fine &= string.Join(",", m.Order) == "prepare,spend,commit,accept,present";
                }
                else
                {
                    // no cast: all the ink is back, no hit of this cast is scheduled, its state is off, nothing was shown or announced
                    fine &= m.Ink == 1f && m.Hits == 3 && m.Held == 1 && !m.StateOn && m.Shown == 0 && m.Waiting == 0 && m.Accepted == 0 && m.Failed == 1 && m.Order.Last() == "fail";
                    fine &= !m.Order.Contains("present") && !m.Order.Contains("accept");
                    bool reached = prepare == 0 && spend == 0;
                    fine &= m.Withdrawals == (reached || (prepare == 0 && spend == 2) ? 1 : 0) && m.HandlerResets == (reached && commit == 2 ? 1 : 0);
                    if (prepare != 0) fine &= m.Order.Count == 2 && !m.Order.Contains("spend");           // a refused Prepare spends nothing
                }
                if (!fine && broken.Count < 6) broken.Add(name + ":" + outcome + ":" + string.Join(">", m.Order));
            }
            r.Fact("dispatchScenarios", scenarios);
            r.Check("A9", broken.Count == 0 && scenarios == 108 && casts == 4,
                "a handler cast, over every way its steps can answer or throw (" + scenarios + " scenarios): judged = ink spent, hits scheduled, state on, whatever accept and presentation do; " +
                "not judged = all ink back, no hit of the cast left scheduled, state off, nothing shown", string.Join(" | ", broken));
            {
                var m = new DispatchModel { PresentMode = 2 };
                var outcome = SpellDispatchRule308.Run(ref m);
                r.Check("A9", outcome == SpellDispatchOutcome308.Cast && Math.Abs(m.Ink - (1f - DispatchModel.Cost)) < 1e-6f && m.Hits == 5 && m.StateOn && m.Reports == 1 && m.Failed == 0,
                    "a presenter that throws: the cast still resolves as judged and the ink stays spent (no refund with hits already scheduled, no state left on with refunded ink)");
                var faulted = new DispatchModel { CommitMode = 2 };
                var outcome2 = SpellDispatchRule308.Run(ref faulted);
                r.Check("A9", outcome2 == SpellDispatchOutcome308.Faulted && faulted.Ink == 1f && faulted.Hits == 3 && faulted.Held == 1 && !faulted.StateOn && faulted.HandlerResets == 1 && faulted.Shown == 0 &&
                    string.Join(",", faulted.Order) == "prepare,spend,commit,withdraw-faulted,fail",
                    "a handler that throws inside Commit after scheduling: the hits it scheduled are taken back before the ink comes back, the handler is reset, nothing is shown");
                var refused = new DispatchModel { CommitMode = 1 };
                SpellDispatchRule308.Run(ref refused);
                r.Check("A9", refused.Ink == 1f && refused.Hits == 3 && refused.Held == 1 && refused.HandlerResets == 0 && string.Join(",", refused.Order) == "prepare,spend,commit,withdraw,fail",
                    "a Commit that answers false: the one refund path takes the scheduled hits back first; the handler is not reset");
                var listener = new DispatchModel { AcceptMode = 2 };
                SpellDispatchRule308.Run(ref listener);
                r.Check("A9", listener.Shown == 1 && listener.Hits == 5 && listener.Reports == 1 && listener.Order.Last() == "present",
                    "presentation runs last, after the rules and the announcement, and still runs when a listener of the announcement threw");
            }

            // ---- d. the interim presenter never stretches a catalogue body ----
            float cap = SpellCueLife308.LastingCueSeconds;
            float[] authoredLives = { .5f, 1.5f, 3f, 6f, 12f };
            bool never = true, shortened = true, own = true;
            foreach (float authored in authoredLives)
            {
                own &= SpellCueLife308.Seconds(0f, authored) == authored && !SpellCueLife308.CueOnly(0f, authored);
                foreach (float requested in new[] { .2f, .6f, 1f, 2.9f, 3f, 4f, 5f, 6f, 8f, 12f, 15f, 20f, 25f, 30f, 600f })
                {
                    float life = SpellCueLife308.Seconds(requested, authored);
                    never &= life <= authored && life <= requested && life <= cap && life > 0f;
                    shortened &= requested > Math.Min(authored, cap) ? SpellCueLife308.CueOnly(requested, authored) : life == requested && !SpellCueLife308.CueOnly(requested, authored);
                }
            }
            r.Check("A9", never && cap > 0f && cap <= 3f, "a catalogue body a handler asks a life for never lives longer than it was authored for, nor longer than it was asked to, nor longer than the " + cap + " s cue");
            r.Check("A9", shortened && own, "a shorter wish is honoured exactly, no wish means the authored life, and a lasting effect is marked cue-only (its persistence is not drawn by the catalogue body)");
            // every lasting number of the rule sheets (buff durations, sword / tree / mark / rock / zone lives) against the longest authorable body (12 s)
            var lasting = new List<string>(); bool cut = true;
            foreach (var row in rows)
                foreach (var p in row.Params ?? new SpellParam[0])
                {
                    bool life = p.Key == "duration" || p.Key.EndsWith(".life", StringComparison.Ordinal) || p.Key.EndsWith(".duration", StringComparison.Ordinal);
                    if (!life || !(p.Value > cap)) continue;
                    lasting.Add(row.Index + ":" + p.Key + "=" + p.Value);
                    foreach (float authored in authoredLives) cut &= SpellCueLife308.Seconds(p.Value, authored) <= cap && SpellCueLife308.CueOnly(p.Value, authored);
                }
            r.Fact("lastingRowNumbers", lasting.Count);
            r.Check("A9", cut && lasting.Count >= 30,
                "every lasting number of the rule sheets (buff 30 s, tree 20 s, installed mark 15 s, rock 12 s, zones 5 to 8 s ...) is shown as a cue of at most " + cap + " s, whatever body it gets (" + lasting.Count + " row numbers)",
                string.Join(" ", lasting.Take(6)));
            r.Check("A9", SpellCueLife308.Seconds(25f, 12f) == cap && SpellCueLife308.Seconds(float.NaN, 3f) == 3f && SpellCueLife308.Seconds(30f, 0f) <= cap && SpellCueLife308.Seconds(30f, float.NaN) <= cap,
                "a sword form's longest life (ink / drain, up to 25 s) is a cue too; a broken number never stretches anything");
        }
    }
}
