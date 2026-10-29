using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using static Oheangbu.EditorTools.Vol2.Vol2ShapeFixtureKit;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 4 edit-mode fixture of 번진다 (D308-149): the split of the spread amount, the marks, the caps, the peaks 난 넌 논 눈. No scene, no Play. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2SpreadFixture Run "all"
    ///   S1 path 간: the nearest four of five neighbours within 3 m share 1.0 (0.25 each); the share paid to a body is the share its mark records, never twice
    ///   S2 the split: 3 neighbours -> thirds that add up to at most 1.0; 1 -> the whole 1.0; 0 -> nothing, the pool stays; a dead neighbour is skipped
    ///   S3 난: 0.5 each, three at most (sum 1.5), never thinned; a fourth is refused by the rules (SpreadTargetCap)
    ///   S4 attachment 건: spread by the first touch; the cast reaches three bodies in all (a fourth neighbour gets nothing); the second contact spreads nothing
    ///   S5 넌: the first two swings spread from ONE shared 1.5 (0.5 each), the third swing never; the carrier on the body
    ///   S6 fan 곤 / 논: spread from the fan's edge; 논 adds the back-spread 0.5 over the same bodies in reverse after a pause, and nothing after that
    ///   S7 ring 운: the ring lives 3 s; the spread goes outward only; a later crosser is touched but the pool is spent
    ///   S8 ring 눈: fire follows wood; enemy patches four at most; the vine door is reported; the body in the burning wood takes the 1.0
    ///   S9 identity: attack ids unique, one material per spread contact, no command twice, no grandchildren
    /// Expected values are written here from NumbersV3 and the table, never read back from a run.</summary>
    public static class Vol2SpreadFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-spread-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-spread-fixture: editor busy (play / compile / import)";
            var r = new Report();
            try { using (var errors = new ErrorCounter()) { Cases(r); Check(r, "E0", errors.Errors == 0, "no error log during the fixture" + (errors.Errors > 0 ? ": " + errors.First : "")); } }
            catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        const long S = 1000000;
        static bool Eq(decimal a, decimal b) => Math.Abs(a - b) <= 0.000000001m;
        static List<Vol2ContactRecord> Of(Vol2CastLog log, string kind) => log.Contacts.Where(c => c.Kind == kind && c.BudgetAccepted).ToList();
        static List<string> Names(Vol2CastLog log, string kind) => Of(log, kind).Select(c => c.TargetName).ToList();
        static decimal Sum(Vol2CastLog log, string kind) => Of(log, kind).Sum(c => c.Amount);
        static List<Vol2Material> Marks(Vol2CastLog log) => log.Materials.Where(m => m.Kind == Vol2MaterialKind.Mark).ToList();

        static void Swing(Rig rig)
        {
            long id = rig.Services.Ids.NextSwing().Value;
            Vector3 sl = new Vector3(0, 1.1f, 0), tp = sl + Vector3.forward * 1.6f;
            for (int k = 0; k < 3; k++) rig.Runtime.Swings.Raise(new Vol2SwingEvent(id, sl, tp, rig.Services.Clock.NowUs, k));
        }

        sealed class FakeField : IVol2FieldPort
        {
            public readonly List<string> Vines = new List<string>();
            public string OnPlatform(CastId cast, Vector3 centre, float radius, long lifeUs) => "n/a";
            public string OnVineBurn(CastId cast, Vol2Material vine) { Vines.Add(vine.Tag); return "gate opened"; }
        }

        static Rig PathRig(Vol2Rulebook book, bool all = true)
        {
            var rig = Rig.Create(book);
            rig.Add("A", 4, 0);
            if (all) { rig.Add("B", 4, 1.0f); rig.Add("C", 4, -1.5f); rig.Add("D", 4, 2.0f); rig.Add("E", 4, -2.5f); rig.Add("F", 4, 2.9f); rig.Add("G", 10, 0); }
            return rig;
        }

        static void Cases(Report r)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var n = book.Numbers;
            long life3 = RuleTypesV3.Microseconds(n.Get("timing.n.life")), life6 = RuleTypesV3.Microseconds(n.Get("timing.attachment.life"));

            // ---- S1 path 간 ----
            {
                var rig = PathRig(book);
                var log = rig.Cast('간', out _); rig.Advance(0.5, 10);
                var ins = Of(log, "Instant"); var sp = Of(log, "Spread"); var marks = Marks(log);
                Check(r, "S1a", ins.Count == 1 && ins[0].TargetName == "A" && ins[0].Amount == 0.7m && Eq(ins[0].Units, 0.42m) && sp.Select(c => c.TargetName).SequenceEqual(new[] { "B", "C", "D", "E" }) && sp.All(c => Eq(c.Amount, 0.25m) && Eq(c.Units, 0.15m)) && !log.Contacts.Any(c => c.TargetName == "F" || c.TargetName == "G"),
                    "간: path ends at A (4 m); the nearest four neighbours within 3 m (B 1.0, C 1.5, D 2.0, E 2.5) share 1.0 -> 0.25 each (0.6 x 0.25 = 0.15 units); F (2.9 m, the fifth) and G (6 m) get nothing");
                Check(r, "S1b", marks.Count == 4 && marks.All(m => Eq(m.Initial, 0.25m) && Eq(m.Remaining, 0.25m) && !m.PaysPool && m.LifeUs == life3 && m.Depth == 0 && m.Alive && m.Footprint.Kind == Vol2FootprintKind.Disc) && Eq(marks.Sum(m => m.Initial), Sum(log, "Spread")) && Eq(log.Budget.PaidShared, 1.0m) && Eq(log.Budget.SharedRemaining, 0m) && Eq(log.Budget.PaidPerTarget, 0.7m) && Eq(log.Budget.Transferred, 0m),
                    "four marks, 3 s each, holding exactly what the bodies were paid (0.25): the same share is never allocated twice; the cast paid 0.7 per target + 1.0 shared, the pool is empty");
                Check(r, "S1c", rig.Status.Requests.Count == 5 && rig.Status.Requests.All(q => q.Kind == Vol2BodyActionKind.Bind && q.DurationUs == 2000000) && log.Contacts.Count(c => c.BodyEffect.Length > 0) == 5, "each of the five bodies is bound ONCE, 2 s (wood)");
                Check(r, "S1d", log.Finished && log.Status == "Done" && marks.All(m => m.Alive), "the cast itself is over at once; its marks go on living");
                rig.Advance(3.2, 32);
                Check(r, "S1e", marks.All(m => m.End == Vol2MaterialEnd.Expired && m.EndedUs >= m.ExpiresUs) && Of(log, "Spread").Count == 4 && log.Materials.Count == 4 && rig.Coord.Materials.Audit().Count == 0, "the marks expire after 3 s (Expired); nothing spread again from them (still four spread contacts, four materials: no grandchildren)");
            }
            // ---- S2 the split ----
            {
                var rig3 = Rig.Create(book); rig3.Add("A", 4, 0); rig3.Add("B", 4, 1.0f); rig3.Add("C", 4, -1.5f); rig3.Add("D", 4, 2.0f);
                var log3 = rig3.Cast('간', out _); rig3.Advance(0.5, 10);
                decimal s3 = Sum(log3, "Spread");
                Check(r, "S2a", Of(log3, "Spread").Count == 3 && s3 <= 1.0m && s3 >= 0.9999999m && Of(log3, "Spread").All(c => Eq(c.Amount, 1m / 3m)), "three neighbours: a third each, adding up to at most 1.0 (" + s3 + ")");
                var rig1 = Rig.Create(book); rig1.Add("A", 4, 0); rig1.Add("B", 4, 1.0f);
                var log1 = rig1.Cast('간', out _); rig1.Advance(0.5, 10);
                Check(r, "S2b", Names(log1, "Spread").SequenceEqual(new[] { "B" }) && Eq(Sum(log1, "Spread"), 1.0m) && Marks(log1).Count == 1, "one neighbour takes the whole 1.0");
                var rig0 = Rig.Create(book); rig0.Add("A", 4, 0); rig0.Add("Far", 4, 3.5f);
                var log0 = rig0.Cast('간', out _); rig0.Advance(0.5, 10);
                Check(r, "S2c", Of(log0, "Spread").Count == 0 && Marks(log0).Count == 0 && Eq(log0.Budget.SharedRemaining, 1.0m) && log0.Finished, "no neighbour within 3 m (3.5 m is too far): nothing spreads, the 1.0 stays unpaid, the cast ends");
                var rigD = Rig.Create(book); rigD.Add("A", 4, 0); var dead = rigD.Add("B", 4, 1.0f); rigD.Add("C", 4, -1.5f); dead.Alive = false;
                var logD = rigD.Cast('간', out _); rigD.Advance(0.5, 10);
                Check(r, "S2d", Names(logD, "Spread").SequenceEqual(new[] { "C" }) && Eq(Sum(logD, "Spread"), 1.0m), "a dead neighbour is skipped; the one living neighbour takes the whole 1.0");
            }
            // ---- S3 난 ----
            {
                var rig = PathRig(book);
                var log = rig.Cast('난', out _); rig.Advance(0.5, 10);
                var sp = Of(log, "Spread");
                var extra = log.Budget.Pay(rig.Services.Ids.NextAttack().Value, ContactKind.Shared, EffectKind.Spread, 0.5m, TriggerKind.None, 5, 0, 0, rig.Services.Clock.NowUs, 9001);
                Check(r, "S3a", sp.Select(c => c.TargetName).SequenceEqual(new[] { "B", "C", "D" }) && sp.All(c => c.Amount == 0.5m) && Eq(log.Budget.PaidShared, 1.5m) && Marks(log).Count == 3 && !log.Contacts.Any(c => c.TargetName == "E" || c.TargetName == "F"),
                    "난: three neighbours, 0.5 each (sum 1.5 = peak.spread.peakTotal), the fourth and fifth get nothing");
                Check(r, "S3b", !extra.Accepted && extra.Reason == "SpreadTargetCap" && Eq(log.Budget.PaidPerTarget + log.Budget.PaidShared, n.Get("peak.난.total")), "the rules refuse a fourth spread body (SpreadTargetCap); instant 0.7 + spread 1.5 = 2.2 (peak.난.total, cap 2.55)");
                var rig1 = Rig.Create(book); rig1.Add("A", 4, 0); rig1.Add("B", 4, 1.0f);
                var log1 = rig1.Cast('난', out _); rig1.Advance(0.5, 10);
                var rig2 = Rig.Create(book); rig2.Add("A", 4, 0); rig2.Add("B", 4, 1.0f); rig2.Add("C", 4, -1.5f);
                var log2 = rig2.Cast('난', out _); rig2.Advance(0.5, 10);
                Check(r, "S3c", Of(log1, "Spread").Single().Amount == 0.5m && Of(log2, "Spread").All(c => c.Amount == 0.5m) && Of(log2, "Spread").Count == 2, "with one neighbour it is still 0.5 (not the whole 1.0), with two 0.5 each: never thinned, never concentrated");
            }
            // ---- S4 attachment 건 ----
            {
                var rig = Rig.Create(book);
                rig.Add("A", 1, 0); rig.Add("B", 1, 2.0f); rig.Add("C", 1, -2.4f); rig.Add("D", 1, 2.9f);
                var log = rig.Cast('건', out _); rig.Advance(0.05); Swing(rig);
                Check(r, "S4a", Names(log, "Instant").SequenceEqual(new[] { "A" }) && Names(log, "Spread").SequenceEqual(new[] { "B", "C" }) && Of(log, "Spread").All(c => Eq(c.Amount, 0.5m)) && !log.Contacts.Any(c => c.TargetName == "D") && log.Budget.State.Targets.Count == 3,
                    "건: swing 1 touches A and spreads to the nearest two (B 2.0, C 2.4) 0.5 each - the cast reaches three bodies in all, so D (2.9 m) gets nothing");
                rig.Advance(0.4); Swing(rig);
                Check(r, "S4b", Names(log, "Second").SequenceEqual(new[] { "A" }) && Of(log, "Spread").Count == 2 && Eq(log.Budget.PaidShared, 1.0m) && Marks(log).Count == 2 && Marks(log).All(m => m.LifeUs == life3),
                    "swing 2 is A's second contact 0.3 and spreads nothing new; the marks last 3 s (timing.n.life), not the 6 s of the body");
                rig.Advance(6.2, 31);
                Check(r, "S4c", log.Finished && log.Status == "Done" && Marks(log).All(m => m.End == Vol2MaterialEnd.Expired), "the cast ends with the body's 6 s; the marks expired");
            }
            // ---- S5 넌 ----
            {
                var rig = Rig.Create(book);
                rig.Add("A", 1, 0); rig.Add("B", 1, 2.0f); var c = rig.Add("C", 1, -5.0f);
                var log = rig.Cast('넌', out _);
                var carrier = log.Materials.FirstOrDefault(m => m.Kind == Vol2MaterialKind.Carrier);
                bool car = carrier != null && carrier.Initial == 0m && carrier.Attached && carrier.LifeUs == life6 && carrier.Alive && carrier.Letter == '넌';
                rig.Advance(0.05); Swing(rig);
                bool first = Names(log, "Spread").SequenceEqual(new[] { "B" }) && Of(log, "Spread")[0].Amount == 0.5m;
                c.MoveTo(At(1f, -2.6f)); rig.Advance(0.4); Swing(rig);
                var sp = Of(log, "Spread");
                Check(r, "S5a", car && first && sp.Select(x => x.TargetName).SequenceEqual(new[] { "B", "C" }) && sp.All(x => x.Amount == 0.5m) && Names(log, "Second").SequenceEqual(new[] { "A" }) && Eq(log.Budget.PaidShared, 1.0m) && Eq(log.Budget.SharedRemaining, 0.5m),
                    "넌: the body carries a 6 s fire carrier holding 0; swing 1 spreads 0.5 to B; C comes within 3 m; swing 2 (A's second contact) spreads 0.5 to C from the SAME 1.5 (0.5 left, no refill)");
                rig.Advance(0.3); Swing(rig);
                Check(r, "S5b", Of(log, "Spread").Count == 2 && Eq(log.Budget.PaidShared, 1.0m) && Eq(log.Budget.PaidPerTarget + log.Budget.PaidShared, 2.0m) && Eq(n.Get("peak.넌.total"), 2.5m), "a third swing spreads nothing; paid 0.7 + 0.3 + 1.0 = 2.0 of the peak's 2.5");
                // the third swing never spreads, even with room: A is hit twice with no neighbour, then E (new) with a neighbour next to it
                var rig2 = Rig.Create(book);
                rig2.Add("A", 1, 0); var e = rig2.Add("E", 1.0f, 5.0f); rig2.Add("C2", 1.0f, 7.4f);
                var log2 = rig2.Cast('넌', out _);
                rig2.Advance(0.05); Swing(rig2); rig2.Advance(0.4); Swing(rig2);
                e.MoveTo(At(1f, 0.3f)); rig2.Advance(0.05);
                var near = rig2.Targets.First(t => t.Name == "C2"); near.MoveTo(At(1f, 2.0f)); rig2.Advance(0.05); Swing(rig2);
                Check(r, "S5c", Names(log2, "Instant").SequenceEqual(new[] { "A", "E" }) && Of(log2, "Spread").Count == 0 && log2.Budget.State.Targets.Count == 2 && Eq(log2.Budget.SharedRemaining, 1.5m),
                    "the third swing touches a NEW body (E) next to a neighbour (C2) with room in the cast and the pool untouched, and still spreads nothing: only the first two swings spread");
            }
            // ---- S6 fan 곤 / 논 ----
            {
                var rig = Rig.Create(book);
                rig.Add("In3", 3, 0); rig.Add("In4s1", 4, 1); rig.Add("Out5.8", 5.8f, 0); rig.Add("Out3s2.6", 3, 2.6f); rig.Add("Behind", -2, 0); rig.Add("Far12", 12, 0);
                var log = rig.Cast('곤', out _); rig.Advance(0.05);
                Check(r, "S6a", Names(log, "Instant").SequenceEqual(new[] { "In3", "In4s1" }) && Names(log, "Spread").SequenceEqual(new[] { "Out5.8", "Out3s2.6", "Behind" }) && Sum(log, "Spread") <= 1.0m && Sum(log, "Spread") >= 0.9999999m && !log.Contacts.Any(c => c.TargetName == "Far12") && Marks(log).Count == 3 && log.Finished,
                    "곤: the fan hits In3 and In4s1; the effect spreads from the fan's edge to the bodies within 3 m of it, nearest the edge first (0.8, 1.18, 2.0 m), a third of 1.0 each; Far12 is 7 m away");
                var rigB = Rig.Create(book);
                rigB.Add("In3", 3, 0); rigB.Add("In4s1", 4, 1); rigB.Add("Out5.8", 5.8f, 0); rigB.Add("Out3s2.6", 3, 2.6f); rigB.Add("Behind", -2, 0);
                var logB = rigB.Cast('논', out _); rigB.Advance(0.05);
                bool forward = Names(logB, "Spread").SequenceEqual(new[] { "Out5.8", "Out3s2.6", "Behind" }) && Of(logB, "ReturnSpread").Count == 0 && !logB.Finished;
                rigB.Advance(0.25, 5); bool notYet = Of(logB, "ReturnSpread").Count == 0;
                rigB.Advance(0.3, 6);
                var back = Of(logB, "ReturnSpread");
                long delay = Of(logB, "ReturnSpread")[0].NowUs - Of(logB, "Spread")[0].NowUs;
                Check(r, "S6b", forward && notYet && back.Select(c => c.TargetName).SequenceEqual(new[] { "Behind", "Out3s2.6", "Out5.8" }) && delay >= 400000 && delay < 500000 && Sum(logB, "ReturnSpread") <= 0.5m && Sum(logB, "ReturnSpread") >= 0.4999999m && back.All(c => Eq(c.Amount, 0.5m / 3m)),
                    "논: forward 1.0 first; after the pause (0.4 s) the SAME three bodies in reverse share 0.5 (a sixth each); not before");
                rigB.Advance(5.0, 50);
                Check(r, "S6c", Of(logB, "Spread").Count == 3 && Of(logB, "ReturnSpread").Count == 3 && Marks(logB).Count == 6 && logB.Finished && logB.Status == "Done" && Marks(logB).All(m => m.Depth == 0 && m.End == Vol2MaterialEnd.Expired) && Eq(logB.Budget.SharedRemaining, 0m),
                    "no third pass and no grandchildren: 3 + 3 spread contacts, 6 marks (two layers), the cast ends after the back-spread; every share of the pool is paid");
                Check(r, "S6d", Eq(logB.Budget.PaidPerTarget, 1.4m) && Eq(logB.Budget.PaidShared, 1.5m) && logB.Contacts.Count(c => c.BodyEffect.Length > 0) == 5, "논 paid 0.7 x 2 instant bodies = 1.4 + 1.0 forward + 0.5 back = 1.5 shared; the body action once per body (5 bodies: two in the fan, three spread to), not again on the back-spread");
            }
            // ---- S7 ring 운 ----
            {
                var rig = Rig.Create(book);
                var w = rig.Add("W", 1, 0); rig.Add("N1", 5, 0.3f); rig.Add("N2", 4.2f, 1.5f); rig.Add("N3", 3, -1); var w2 = rig.Add("W2", 6, 4); var hole = rig.Add("Hole", 0.5f, 0.3f);
                var log = rig.Cast('운', out _); rig.Advance(0.05);
                bool quiet = Of(log, "Instant").Count == 0 && !log.Finished;
                w.MoveTo(At(3.2f, 0f)); rig.Advance(0.05);
                Check(r, "S7a", quiet && Names(log, "Instant").SequenceEqual(new[] { "W" }) && Names(log, "Spread").SequenceEqual(new[] { "N2", "N1" }) && Of(log, "Spread").All(c => Eq(c.Amount, 0.5m)) && !log.Contacts.Any(c => c.TargetName == "N3" || c.TargetName == "Hole"),
                    "운: the ring waits (3 s); W steps across the band and is touched; the effect goes OUTWARD to the bodies within 3 m of W that stand farther out (N2 1.80, N1 1.83), 0.5 each; N3 is not farther out");
                rig.Advance(1.0, 10);
                w2.MoveTo(At(2.0f, 0.5f)); rig.Advance(0.05);
                Check(r, "S7b", Names(log, "Instant").SequenceEqual(new[] { "W", "W2" }) && Of(log, "Spread").Count == 2 && Eq(log.Budget.SharedRemaining, 0m), "a later crosser (W2) is touched, but the pool is spent: no more spread");
                rig.Advance(2.2, 22);
                hole.MoveTo(At(2.0f, -0.5f)); rig.Advance(0.05);
                Check(r, "S7c", log.Finished && log.Status == "Done" && !log.Contacts.Any(c => c.TargetName == "Hole") && Marks(log).All(m => m.End == Vol2MaterialEnd.Expired), "after 3 s the ring is gone: a body stepping on the old band is not touched; the cast is Done, the marks expired");
            }
            // ---- S8 ring 눈 ----
            {
                var rig = Rig.Create(book); var field = new FakeField(); rig.Coord.Field = field;
                var mats = rig.Coord.Materials; long now = rig.Services.Clock.NowUs; long life = RuleTypesV3.Microseconds(n.Get("timing.g.life"));
                Vol2Material Plant(long cast, string tag, Body body, long faction, float ahead, float side, bool door = false)
                    => mats.Create(new CastId(cast), '가', body, Vol2MaterialKind.Residue, 0.9m, life, Vol2Footprint.Disc(At(ahead, side), 0.6f), faction == Vol2Faction.Enemy ? 2 : 1, faction, true, now, 0, 0, tag, door);
                var w1 = Plant(9001, "w1", Body.Wood, Vol2Faction.Player, 2f, 0f);
                var w2 = Plant(9002, "w2", Body.Wood, Vol2Faction.World, 0f, -2f, true);
                var w3 = Plant(9003, "w3", Body.Wood, Vol2Faction.Player, 4.5f, 0.5f);
                var w4 = Plant(9004, "w4", Body.Wood, Vol2Faction.Player, 9f, 0f);
                var e = new[] { Plant(9011, "e1", Body.Water, Vol2Faction.Enemy, -2f, 0f), Plant(9012, "e2", Body.Water, Vol2Faction.Enemy, 0f, 2f), Plant(9013, "e3", Body.Water, Vol2Faction.Enemy, 1.414f, 1.414f), Plant(9014, "e4", Body.Water, Vol2Faction.Enemy, -1.414f, 1.414f), Plant(9015, "e5", Body.Water, Vol2Faction.Enemy, -1.414f, -1.414f) };
                var tb = rig.Add("TB", 2f, 0.1f); rig.Add("T1", 4.5f, 0.5f); rig.Add("T3", 9f, 0f);
                var log = rig.Cast('눈', out _); rig.Advance(0.05);
                Check(r, "S8a", w1.End == Vol2MaterialEnd.Consumed && w2.End == Vol2MaterialEnd.Consumed && w3.End == Vol2MaterialEnd.Consumed && w4.Alive, "눈: the wood the ring reaches (w1, the vine door w2) burns, and the wood next to it (w3, 2.55 m from w1) with it; the far wood w4 does not (it is 4.5 m from w3)");
                Check(r, "S8b", e.Take(4).All(x => x.End == Vol2MaterialEnd.Consumed) && e[4].Alive && log.Burned.Count(x => x.EndsWith(":enemy")) == 4 && n.Get("peak.눈.burnCount") == 4, "four of the five enemy patches on the ring burn (peak.눈.burnCount = 4), the fifth stays");
                Check(r, "S8c", Names(log, "Instant").SequenceEqual(new[] { "TB" }) && Names(log, "Spread").SequenceEqual(new[] { "T1" }) && Eq(Of(log, "Spread")[0].Amount, 1.0m) && !log.Contacts.Any(c => c.TargetName == "T3") && Marks(log).Count == 1 && Eq(log.Budget.PaidShared, 1.0m),
                    "TB on the band is touched (and spread nothing: fire follows wood, not bodies); T1 stands in the burning w3 and takes the whole 1.0 (one mark); T3 stands in the far wood and is left alone");
                Check(r, "S8d", field.Vines.SequenceEqual(new[] { "w2" }) && log.FieldNotes.Count == 1 && log.FieldNotes[0].Contains("gate opened"), "the burning vine door is reported to the field port (answered 'gate opened'), once");
                int burnedBefore = log.Burned.Count; rig.Advance(0.5, 10);
                Check(r, "S8e", log.Burned.Count == burnedBefore && Of(log, "Spread").Count == 1 && field.Vines.Count == 1, "nothing burns twice: later ticks add no burn, no spread, no second gate report");
                rig.Advance(3.0, 30);
                Check(r, "S8f", log.Finished && w4.Alive && rig.Coord.Materials.Audit().Count == 0, "the ring is over after 3 s, the cast is Done; w4 is still its own business (its life is not ours)");
            }
            // ---- S9 identity ----
            {
                var rig = PathRig(book);
                var log = rig.Cast('간', out _); rig.Advance(0.5, 10);
                var attacks = log.Contacts.Select(c => c.Attack.Value).ToList();
                var spreadMats = Of(log, "Spread").Select(c => c.MaterialId).ToList();
                Check(r, "S9", attacks.Distinct().Count() == attacks.Count && spreadMats.Distinct().Count() == spreadMats.Count && spreadMats.All(id => id > 0 && log.Materials.Any(m => m.Id.Value == id)) && log.Contacts.All(c => c.DuplicateCommands == 0)
                    && log.Contacts.Where(c => c.BudgetAccepted).All(c => rig.Services.Commands.Contains(new CommandKey(rig.Services.Ids.ScopeId, CommandRuleKind.Damage, c.Attack.Value, 0))),
                    "every contact has its own AttackId; every spread contact has its own MaterialId that is a material of the cast; no command ran twice; each paid contact's damage key is in the ledger");
            }
        }
    }
}
