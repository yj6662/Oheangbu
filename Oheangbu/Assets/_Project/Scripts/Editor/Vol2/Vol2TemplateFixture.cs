using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using VolSpellKind = Oheangbu.Vol2.Rules.SpellKind;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 1 (D308-149, design s5b_design_v1 section 12) edit-mode fixture. Everything runs in memory: no asset is written,
    /// no scene is touched, no Play is needed. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2TemplateFixture Run "all"
    ///   L  the vol.2 rulebook loads through the loader against the frozen manifest (120 rows)
    ///   T  120 rows -> 24 templates (P0..R5, five bodies each), 5 bodies (24 rows each), 20 peaks (the section 8 letters), unregistered 0
    ///      (the expected sets below are written out by hand from the design, never taken from the run)
    ///   K  kinds: bare 20 / normal 80 / peak 20, one parry (the attachment bare row per body), 걱 the only recovery
    ///   A  the glyph adapter over all 120 letters: 120 accepted, CastIds 1..120, template / peak / recovery / cost as the rows say;
    ///      outside letters refused NotInVocabulary; an unavailable rulebook refuses all 120 and issues no CastId; a closed glyph is Locked
    ///   I  identity / clock: four independent id counters, CommandKey keeps same-event commands apart and drops a replay, 0.3 s = 300,000 us,
    ///      [start, end) edges, a clock never goes backwards, the attack ledger keeps the original CastId and releases by cast
    ///   R  the lab request file: one-shot, only the spell lab scene may consume it, a stale one is ignored
    /// Not covered (later slices): contacts, damage, effects, scenes, Play.</summary>
    public static class Vol2TemplateFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        // section 7: TemplateId = frame x verb, hand-written
        static readonly string[] ExpectedTemplates =
        {
            "P0","P1","P2","P3","P4","P5", "A0","A1","A2","A3","A4","A5", "F0","F1","F2","F3","F4","F5", "R0","R1","R2","R3","R4","R5"
        };
        // section 8: the 20 peaks (wood: leave / fire: spread / earth: seed / metal: split / water: return, one per frame)
        const string ExpectedPeaks = "각걱곡국난넌논눈맘멈몸뭄삿섯솟숫앙엉옹웅";

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-template-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-template-fixture: editor busy (play / compile / import)";
            var r = new Report();
            try { Body(r); } catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        static void Body(Report r)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "L1", book.Ok, book.Ok ? "rulebook loaded: " + book.Spells.Count + " spells, dto " + book.DtoHash.Substring(0, 12) : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var spells = book.Spells;
            Check(r, "L2", spells.Count == 120 && spells.Select(s => s.Letter).Distinct().Count() == 120, "120 distinct letters");

            // ---- T: templates / bodies / peaks
            var byTemplate = spells.GroupBy(s => Vol2TemplateId.Of(s.Glyph).Name).ToDictionary(g => g.Key, g => g.ToList());
            Check(r, "T1", byTemplate.Count == 24 && ExpectedTemplates.All(byTemplate.ContainsKey), "24 templates, exactly the hand-written P0..R5 set (" + byTemplate.Count + " found)");
            Check(r, "T2", ExpectedTemplates.All(t => byTemplate.TryGetValue(t, out var rows) && rows.Count == 5 && rows.Select(x => x.Body).Distinct().Count() == 5), "every template holds five letters, one per body");
            var all = Vol2TemplateId.All();
            Check(r, "T3", all.Count == 24 && all.Select(t => t.Name).SequenceEqual(ExpectedTemplates) && ExpectedTemplates.All(n => Vol2TemplateId.TryParse(n, out var id) && id.Name == n), "Vol2TemplateId.All() == the 24 hand-written names; parse round-trips");
            var bodies = spells.GroupBy(s => s.Body).ToDictionary(g => g.Key, g => g.Count());
            Check(r, "T4", bodies.Count == 5 && bodies.Values.All(c => c == 24), "5 bodies, 24 letters each");
            var peaks = spells.Where(s => s.Kind == VolSpellKind.Peak).Select(s => s.Letter).OrderBy(c => c).ToArray();
            var expectedPeaks = ExpectedPeaks.OrderBy(c => c).ToArray();
            Check(r, "T5", peaks.Length == 20 && peaks.SequenceEqual(expectedPeaks), "20 peaks == the section 8 letters (" + new string(peaks) + ")");
            int unregistered = spells.Count(s => !Vol2TemplateId.TryParse(Vol2TemplateId.Of(s.Glyph).Name, out _) || !Enum.IsDefined(typeof(Body), s.Body));
            Check(r, "T6", unregistered == 0, "unregistered rows " + unregistered);

            // ---- K: kinds
            int bare = spells.Count(s => s.Kind == VolSpellKind.Bare), normal = spells.Count(s => s.Kind == VolSpellKind.Normal);
            Check(r, "K1", bare == 20 && normal == 80 && peaks.Length == 20, "bare " + bare + " / normal " + normal + " / peak " + peaks.Length);
            var parry = spells.Where(s => s.Glyph.IsParry).Select(s => s.Letter).ToArray();
            Check(r, "K2", parry.Length == 5 && spells.Where(s => s.Glyph.IsParry).All(s => s.Frame == Frame.Attachment && s.Verb == Verb.Once), "five parry rows (attachment x once, one per body)");

            // ---- A: the adapter over the 120 letters
            var clock = new ManualVol2Clock(0); var ids = new Vol2IdIssuer(7);
            var adapter = new Vol2GlyphAdapter(book.Numbers, null, clock, ids);
            int events = 0; var seen = new List<Vol2CastDecision>();
            adapter.Decided += d => { events++; seen.Add(d); };
            var decisions = new List<Vol2CastDecision>();
            foreach (var row in spells) { clock.Advance(1000); decisions.Add(adapter.Receive(new Oheangbu.Core.Domain.DrawnLetter(row.Letter, default, default, null, .6f, .5f, 1.2f, 1.4f, 3))); }
            Check(r, "A1", decisions.All(d => d.Accepted) && adapter.AcceptedCount == 120 && adapter.RejectedCount == 0 && events == 120 && seen.Count == 120, "120 injected letters: 120 accepted, 0 refused, 120 Decided events");
            Check(r, "A2", decisions.Select(d => d.CastId.Value).SequenceEqual(Enumerable.Range(1, 120).Select(i => (long)i)) && ids.CastsIssued == 120, "CastIds 1..120 in order, none issued twice");
            bool match = true; string firstBad = "";
            for (int i = 0; i < 120 && match; i++)
            {
                var d = decisions[i]; var row = spells[i];
                string why = d.Letter != row.Letter ? "letter" : !d.Decoded || d.Glyph.Body != row.Body || d.Glyph.Frame != row.Frame || d.Glyph.Verb != row.Verb ? "glyph"
                    : d.Template.Name != Vol2TemplateId.Of(row.Glyph).Name ? "template" : d.IsPeak != (row.Kind == VolSpellKind.Peak) ? "peak"
                    : d.IsRecovery != (row.Letter == '걱') ? "recovery" : d.Plan == null || d.Plan.Cost != row.Cost ? "cost" : d.DecidedUs != (i + 1) * 1000L ? "time" : "";
                if (why.Length > 0) { match = false; firstBad = row.Letter + ":" + why; }
            }
            Check(r, "A3", match, match ? "glyph / template / peak / recovery / cost / decision time agree with the rows" : "first mismatch " + firstBad);
            Check(r, "A4", decisions.Count(d => d.IsRecovery) == 1 && decisions.Single(d => d.IsRecovery).Letter == '걱', "걱 is the only recovery decision (D308-145)");
            Check(r, "A5", decisions[0].WorstJamoDistance == .6f && decisions[0].StrokeCount == 3 && decisions[0].HoldDuration == 1.4f, "recognition facts of the DrawnLetter are carried on the decision");

            var outside = new[] { 'A', ' ', '밥', '갔', '힣', '\0' };
            var refused = outside.Select(c => adapter.Receive(c)).ToArray();
            Check(r, "A6", refused.All(d => !d.Accepted && d.Reason == Vol2CastRejectReason.NotInVocabulary && !d.CastId.IsValid) && ids.CastsIssued == 120, "letters outside the 120 are refused NotInVocabulary and take no CastId");

            var unavailable = new Vol2GlyphAdapter(null, "test: no data", clock, new Vol2IdIssuer(8));
            var none = spells.Select(s => unavailable.Receive(s.Letter)).ToArray();
            Check(r, "A7", none.All(d => !d.Accepted && d.Reason == Vol2CastRejectReason.RulebookUnavailable && d.Detail == "test: no data" && !d.CastId.IsValid) && unavailable.AcceptedCount == 0, "an unavailable rulebook refuses all 120 (explicit failure, no cast)");

            var closed = new Vol2GlyphAdapter(book.Numbers, null, clock, new Vol2IdIssuer(9), g => g.Letter != '걱');
            var locked = spells.Select(s => closed.Receive(s.Letter)).ToArray();
            Check(r, "A8", locked.Count(d => d.Accepted) == 119 && locked.Single(d => !d.Accepted).Reason == Vol2CastRejectReason.Locked && locked.Single(d => !d.Accepted).Letter == '걱', "a closed glyph is refused Locked, the other 119 are accepted");

            // ---- I: identity / clock / ledgers
            var issuer = new Vol2IdIssuer(3);
            var c1 = issuer.NextCast(); var a1 = issuer.NextAttack(); var a2 = issuer.NextAttack(); var s1 = issuer.NextSwing(); var m1 = issuer.NextMaterial();
            Check(r, "I1", c1.Value == 1 && a1.Value == 1 && a2.Value == 2 && s1.Value == 1 && m1.Value == 1 && !default(CastId).IsValid && new Vol2IdIssuer().NextCast().Value == 1, "cast / attack / swing / material counters are independent and per instance");
            var ledger = new CommandLedger();
            var k0 = new CommandKey(3, CommandRuleKind.Damage, 55, 0); var k1 = new CommandKey(3, CommandRuleKind.Damage, 55, 1); var k2 = new CommandKey(3, CommandRuleKind.Status, 55, 0);
            Check(r, "I2", ledger.TryBegin(k0) && ledger.TryBegin(k1) && ledger.TryBegin(k2) && !ledger.TryBegin(k0) && ledger.Count == 3 && k0 != k1 && k0 == new CommandKey(3, CommandRuleKind.Damage, 55, 0), "same-event commands differ by ordinal / rule; a replayed key is refused");
            Check(r, "I3", Vol2Time.FromSeconds(0.3) == 300000L && Vol2Time.FromSeconds(1.5) == 1500000L && Vol2Time.ToSecondsF(300000L) == 0.3f && RuleTypesV3.Microseconds(0.3m) == 300000L, "0.3 s = 300,000 us on both conversions");
            Check(r, "I4", Vol2Time.Contains(100, 400, 100) && Vol2Time.Contains(100, 400, 399) && !Vol2Time.Contains(100, 400, 400) && !Vol2Time.Contains(100, 400, 99), "[start, end): the start is inside, the end is not");
            double now = 10; var live = new Vol2Clock(() => now);
            long t0 = live.NowUs; now = 10.25; long t1 = live.NowUs; now = 9.0; long t2 = live.NowUs; now = double.NaN; long t3 = live.NowUs; now = 10.5; long t4 = live.NowUs;
            Check(r, "I5", t0 == 0 && t1 == 250000 && t2 == 250000 && t3 == 250000 && t4 == 500000, "the clock starts at 0, never goes back, ignores a non-finite source (" + t0 + "," + t1 + "," + t2 + "," + t3 + "," + t4 + ")");
            var attacks = new Vol2AttackLedger();
            var glyph = spells.First(s => s.Letter == '각').Glyph;
            var castA = issuer.NextCast(); var castB = issuer.NextCast();
            attacks.Register(new Vol2AttackMetadata(a1, castA, s1, glyph, 100));
            attacks.Register(new Vol2AttackMetadata(a2, castA, s1, glyph, 200, reactionDepth: 1, originalOwnerId: 9, igniterId: 4, triggerAttackId: a1));
            var other = issuer.NextAttack(); attacks.Register(new Vol2AttackMetadata(other, castB, issuer.NextSwing(), glyph, 300));
            bool dup = false; try { attacks.Register(new Vol2AttackMetadata(a1, castA, s1, glyph, 1)); } catch (InvalidOperationException) { dup = true; }
            bool got = attacks.TryGet(a2, out var meta);
            Check(r, "I6", dup && got && meta.CastId == castA && meta.TriggerAttackId == a1 && meta.IgniterId == 4 && meta.OriginalOwnerId == 9 && meta.ReactionDepth == 1 && meta.Letter == '각' && meta.Verb == Verb.Leave, "attack metadata keeps the original CastId and the trigger; a duplicate AttackId is refused");
            int released = attacks.ReleaseCast(castA);
            Check(r, "I7", released == 2 && attacks.Count == 1 && attacks.TryGet(other, out _) && !attacks.TryGet(a1, out _), "releasing a finished cast drops only its attacks");

            // ---- R: the lab request file (one-shot, lab scene only)
            RequestChecks(r);
        }

        static void RequestChecks(Report r)
        {
            string folder = Vol2LabRequestFile.Folder, path = Vol2LabRequestFile.RequestPath;
            System.IO.Directory.CreateDirectory(folder);
            if (System.IO.File.Exists(path)) { Check(r, "R0", false, "a real request.json is pending (" + path + "); fixture R skipped so it is not consumed"); return; }
            string stamp = "fixture_" + DateTime.Now.ToString("HHmmssfff");
            string Make(long ageMs) => JsonUtility.ToJson(new Vol2LabRequest { mode = "Vol2", run = true, runId = stamp, commit = "test", issuedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ageMs, scene = Vol2LabRequestFile.LabScenePath });
            string consumed = System.IO.Path.Combine(folder, "consumed_" + stamp + ".json"), expired = System.IO.Path.Combine(folder, "expired_" + stamp + ".json");
            try
            {
                System.IO.File.WriteAllText(path, Make(1000));
                bool main = Vol2LabRequestFile.TryConsume("Assets/_Project/Scenes/World/W_Demo_Main.unity", out var none);
                Check(r, "R1", !main && none == null && System.IO.File.Exists(path), "a scene that is not the spell lab leaves the request untouched and stays Legacy");
                bool lab = Vol2LabRequestFile.TryConsume(Vol2LabRequestFile.LabScenePath, out var req);
                Check(r, "R2", lab && req != null && req.Mode == SpellRuntimeMode.Vol2 && req.run && req.runId == stamp && !System.IO.File.Exists(path) && System.IO.File.Exists(consumed), "the spell lab consumes it once (renamed consumed_*), mode Vol2");
                Check(r, "R3", !Vol2LabRequestFile.TryConsume(Vol2LabRequestFile.LabScenePath, out _), "a second read finds nothing (one-shot)");
                System.IO.File.WriteAllText(path, Make(Vol2LabRequestFile.MaxAgeMs + 60000));
                bool stale = Vol2LabRequestFile.TryConsume(Vol2LabRequestFile.LabScenePath, out var old);
                Check(r, "R4", !stale && old == null && System.IO.File.Exists(expired) && !System.IO.File.Exists(path), "a request older than five minutes is renamed expired_* and ignored");
            }
            finally
            {
                foreach (string f in new[] { path, consumed, expired }) { try { if (System.IO.File.Exists(f)) System.IO.File.Delete(f); } catch (Exception) { } }
            }
        }
    }
}
