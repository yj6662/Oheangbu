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
    /// <summary>S5-b slice 4 edit-mode fixture of 남긴다 (D308-149): the residue, its shared pool of 0.9, the peaks 각 곡 국. No scene, no Play. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2LeaveFixture Run "all"
    ///   L1 path 낙: instant on the first body, the residue lands there, booked as ONE transfer of 0.9; three bodies drain it by thirds, the fourth finds it spent
    ///   L2 path 악: the residue expires at created + 10 s (half-open), the cast ends with it
    ///   L3 fan 녹: the footprint is the fan; a later body draws; what is outside / behind never does
    ///   L4 ring 눅: the band; the hollow and the outside never draw; a body walking onto it later does
    ///   L5 attachment 넉: 4 bodies in one swing (a fourth is refused TargetCap); the second contact 0.3 once; one residue draw per body
    ///   L6 걱 is not run here (Recovery(slice2)), no ink, no material
    ///   L7 budget sharing: the cast can book the residue once (a second transfer is refused); a draw can never take more than is left
    ///   L8 각: caged 1.5 s (boss 0.75 s), the residue where the cage lets go (follows the target, a dead target releases at once), life counts from there
    ///   L9 곡: area x1.5 as ONE linear factor, a body beyond the first reach is taken in later, each body is bound once, 1.5 s (boss 0.75)
    ///   L10 국: MY residue inside the ring lives x1.5 once; the outside, the enemy's and a second 국 do not; amount untouched; the platform request is recorded / answered
    ///   L11 the clock jump (lab fast-forward) expires a residue in one tick
    /// Expected values are written here from NumbersV3 and the table, never read back from a run.</summary>
    public static class Vol2LeaveFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-leave-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-leave-fixture: editor busy (play / compile / import)";
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
        static decimal Sum(Vol2CastLog log, string kind) => Of(log, kind).Sum(c => c.Amount);
        static Vol2Material Residue(Vol2CastLog log) => log.Materials.FirstOrDefault(m => m.Kind == Vol2MaterialKind.Residue);

        static void Swing(Rig rig, float reach = 1.6f)
        {
            long id = rig.Services.Ids.NextSwing().Value;
            Vector3 sl = new Vector3(0, 1.1f, 0), tp = sl + Vector3.forward * reach;
            for (int k = 0; k < 3; k++) rig.Runtime.Swings.Raise(new Vol2SwingEvent(id, sl, tp, rig.Services.Clock.NowUs, k));
        }

        sealed class FakeField : IVol2FieldPort
        {
            public readonly List<string> Platforms = new List<string>(), Vines = new List<string>();
            public string OnPlatform(CastId cast, Vector3 centre, float radius, long lifeUs) { Platforms.Add(cast + " r" + radius + " " + lifeUs); return "platform ok"; }
            public string OnVineBurn(CastId cast, Vol2Material vine) { Vines.Add(cast + " " + vine.Id); return "gate opened"; }
        }

        static void Cases(Report r)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var n = book.Numbers;
            long life10 = RuleTypesV3.Microseconds(n.Get("timing.g.life")), life6 = RuleTypesV3.Microseconds(n.Get("timing.attachment.life"));

            // ---- L1 path 낙 ----
            {
                var rig = Rig.Create(book);
                var a = rig.Add("A", 4, 0); rig.Add("B8", 8, 0); var l = rig.Add("L", 6, 3); var m = rig.Add("M", 6, -3); var z = rig.Add("Z", 6, -6);
                var log = rig.Cast('낙', out var dec);
                rig.Advance(0.5, 10);
                var res = Residue(log);
                var ins = Of(log, "Instant"); var dr = Of(log, "Residue");
                bool landed = res != null && res.Alive && res.Footprint.Kind == Vol2FootprintKind.Disc && Vector3.Distance(res.Footprint.Origin, a.Position) < 0.01f && res.Initial == 0.9m && res.Remaining == 0.6m && res.LifeUs == life10 && res.CreatedUs - log.StartUs <= 150000;
                Check(r, "L1a", ins.Count == 1 && ins[0].TargetName == "A" && ins[0].Amount == 0.7m && Eq(ins[0].Units, 0.7m) && dr.Count == 1 && dr[0].TargetName == "A" && Eq(dr[0].Amount, 0.3m) && landed && log.Contacts.All(c => c.TargetName != "B8"),
                    "낙: A (4 m) takes the instant 0.7; the residue lands at A (disc, 0.9, 10 s) and A's own draw is a third of it (0.3, left 0.6); B behind A is never reached");
                l.MoveTo(At(4.3f, 0.2f)); rig.Advance(0.05);
                m.MoveTo(At(3.8f, -0.3f)); rig.Advance(0.05);
                z.MoveTo(At(4.0f, 0.3f)); rig.Advance(0.05);
                dr = Of(log, "Residue");
                Check(r, "L1b", dr.Select(c => c.TargetName).SequenceEqual(new[] { "A", "L", "M" }) && Eq(dr.Sum(c => c.Amount), 0.9m) && res.End == Vol2MaterialEnd.Consumed && res.Drawn == 0.9m && Eq(res.Remaining, 0m) && !log.Contacts.Any(c => c.TargetName == "Z"),
                    "three bodies drain the residue by thirds (0.3 each, 0.9 in all); it is Consumed; Z arrives after it is spent and gets nothing");
                rig.Advance(2.6, 26);
                Check(r, "L1c", log.Finished && log.Status == "Done" && Eq(log.Budget.Transferred, 0.9m) && Eq(log.Budget.PaidPerTarget, 0.7m) && Eq(log.Budget.PaidShared, 0m) && Eq(log.Budget.Transferred + log.Budget.PaidPerTarget, n.Get("peak.각.total")) && rig.Coord.Materials.Audit().Count == 0,
                    "the cast booked ONE transfer of 0.9 and the instant 0.7 = 1.6 (a 남긴다 cast's total); the cast is Done with its residue; the registry audit is clean");
                var hit = log.Contacts.Where(c => c.DamageApplied).ToList();
                var spells = rig.Damage.Requests.Where(q => !q.Lasting).ToList();
                Check(r, "L1d", hit.Count == 4 && spells.Count == 4 && rig.Damage.Requests.Count(q => q.Lasting) == 12 && spells.All(q => q.BaseH == 1.0m) && spells.Select(q => q.Structure).OrderBy(x => x).SequenceEqual(new[] { 0.3m, 0.3m, 0.3m, 0.7m })
                    && log.Contacts.Count(c => c.BodyEffect.StartsWith("burn")) == 3 && log.Contacts.Count(c => c.BodyEffect.Length > 0) == 3,
                    "four hit requests (R 0.7 once, R 0.3 three times, H 1.0 fire) and 3 x 4 burn ticks; each of the three bodies takes the burn ONCE (the draw after the instant does not burn again)");
            }
            // ---- L2 path 악, natural expiry ----
            {
                var rig = Rig.Create(book);
                rig.Add("A", 4, 0);
                var log = rig.Cast('악', out _);
                rig.Advance(0.5, 10);
                var res = Residue(log); long created = res.CreatedUs;
                rig.Advance(9.0, 18); bool aliveAt9 = res.Alive && !log.Finished;
                rig.Advance(0.7, 14);
                Check(r, "L2", aliveAt9 && res.End == Vol2MaterialEnd.Expired && res.EndedUs >= created + life10 && res.EndedUs < created + life10 + 100000 && Eq(res.Remaining, 0.6m) && log.Finished && log.Status == "Done" && Eq(Sum(log, "Residue"), 0.3m),
                    "악: alive after 9 s, Expired at created + 10 s (the first tick at or after it), its unspent 0.6 stays on the record; the cast is Done; the pool drawn is only A's 0.3");
            }
            // ---- L3 fan 녹 ----
            {
                var rig = Rig.Create(book);
                rig.Add("In3", 3, 0); rig.Add("In4.8", 4.8f, -0.3f); rig.Add("Out5.8", 5.8f, 0); rig.Add("Behind", -2, 0);
                var late = rig.Add("L", 7, 0); var after = rig.Add("M", 7, 2);
                var log = rig.Cast('녹', out _); rig.Advance(0.05);
                var res = Residue(log);
                Check(r, "L3a", res != null && res.Footprint.Kind == Vol2FootprintKind.Fan && res.Footprint.ScaleAt(5 * S) == 1f && Of(log, "Instant").Count == 2 && Of(log, "Residue").Select(c => c.TargetName).OrderBy(x => x).SequenceEqual(new[] { "In3", "In4.8" }) && Eq(res.Remaining, 0.3m),
                    "녹: the residue is the fan itself (no growth); the two bodies inside take the instant and a third each of the pool (0.3 left); the one beyond the depth and the one behind do not");
                late.MoveTo(At(3.5f, -0.5f)); rig.Advance(0.05); after.MoveTo(At(2.5f, 0.3f)); rig.Advance(0.05);
                Check(r, "L3b", Of(log, "Residue").Select(c => c.TargetName).SequenceEqual(new[] { "In3", "In4.8", "L" }) && res.End == Vol2MaterialEnd.Consumed && !log.Contacts.Any(c => c.TargetName == "M") && !log.Contacts.Any(c => c.TargetName == "Out5.8" || c.TargetName == "Behind") && Of(log, "Instant").All(c => c.TargetName != "L"),
                    "L walks into the fan later and draws the last third (no instant: it was not there at the cast); M arrives after the pool is spent");
            }
            // ---- L4 ring 눅 ----
            {
                var rig = Rig.Create(book);
                rig.Add("Band2.0", 2, 0); rig.Add("BandSide", 0, -2.1f); rig.Add("Hole0.5", 0.5f, 0.3f); rig.Add("Out3.4", 3.4f, 0);
                var walker = rig.Add("L", 3.4f, -1f);
                var log = rig.Cast('눅', out _); rig.Advance(0.05);
                var res = Residue(log);
                walker.MoveTo(At(2.0f, 1.0f)); rig.Advance(0.05);
                Check(r, "L4", res != null && res.Footprint.Kind == Vol2FootprintKind.Ring && Of(log, "Instant").Select(c => c.TargetName).OrderBy(x => x).SequenceEqual(new[] { "Band2.0", "BandSide" })
                    && Of(log, "Residue").Select(c => c.TargetName).OrderBy(x => x).SequenceEqual(new[] { "Band2.0", "BandSide", "L" }) && !log.Contacts.Any(c => c.TargetName == "Hole0.5" || c.TargetName == "Out3.4") && res.End == Vol2MaterialEnd.Consumed,
                    "눅: the two bodies on the band take the instant and a third each; the hollow and the outside never touch it; L steps onto the band later and draws the last third");
            }
            // ---- L5 attachment 넉 ----
            {
                var rig = Rig.Create(book);
                foreach (var d in new[] { ("A", 0.8f, 0f), ("B", 1.0f, 0.3f), ("C", 1.2f, -0.3f), ("D", 1.4f, 0f) }) rig.Add(d.Item1, d.Item2, d.Item3);
                var log = rig.Cast('넉', out _);
                var res = Residue(log);
                bool onBody = res != null && res.Attached && res.LifeUs == life6 && res.Alive && Eq(res.Initial, 0.9m);
                rig.Advance(0.05); Swing(rig);
                var ins = Of(log, "Instant");
                bool first = ins.Select(c => c.TargetName).SequenceEqual(new[] { "A", "B", "C" }) && Of(log, "Residue").Select(c => c.TargetName).SequenceEqual(new[] { "A", "B", "C" }) && res.End == Vol2MaterialEnd.Consumed
                    && log.Contacts.Where(c => c.TargetName == "D").All(c => !c.BudgetAccepted && c.BudgetReason == "TargetCap");
                rig.Advance(0.4); Swing(rig);
                var sec = Of(log, "Second");
                bool second = sec.Select(c => c.TargetName).SequenceEqual(new[] { "A", "B", "C" }) && sec.All(c => Eq(c.Amount, 0.3m));
                rig.Advance(0.3); Swing(rig);
                bool third = Of(log, "Second").Count == 3 && Of(log, "Instant").Count == 3 && log.Contacts.Count(c => c.BudgetReason == "NoSecondContact") >= 3;
                Check(r, "L5a", onBody && first, "넉: the residue rides the body (6 s, 0.9, no place in the world); swing 1 touches A B C (a fourth, D, is refused TargetCap), each draws a third; the pool is spent (Consumed)");
                Check(r, "L5b", second && third && Eq(log.Budget.PaidPerTarget, 3.0m) && Eq(log.Budget.Transferred, 0.9m), "swing 2, 0.4 s later, is the second contact 0.3 on the same three; swing 3 pays nothing (NoSecondContact); per-target pay 3 x 0.7 + 3 x 0.3 = 3.0");
                rig.Advance(6.2, 31);
                Check(r, "L5c", log.Finished && log.Status == "Done" && log.SwingReports == 9, "the cast ends when the body's 6 s are over (Done); 9 collider reports of 3 swings were counted");
            }
            // ---- L6 걱 ----
            {
                var rig = Rig.Create(book); rig.Add("A", 1, 0);
                var log = rig.Cast('걱', out var dec);
                rig.Advance(0.2);
                Check(r, "L6", dec.Accepted && dec.IsRecovery && log != null && log.Status == "Recovery(slice2)" && log.Finished && log.Contacts.Count == 0 && log.Materials.Count == 0 && rig.Ink.Casts == 0 && rig.Damage.Requests.Count == 0 && rig.Coord.RecoveryRouted == 1 && rig.Coord.UnsupportedCasts == 0 && rig.Coord.ActiveCount == 0,
                    "걱 is the defence kit's: the coordinator notes Recovery(slice2), asks no ink (the gate took it), makes no attack, no material, no shape");
            }
            // ---- L7 budget sharing ----
            {
                var rig = Rig.Create(book); rig.Add("A", 4, 0);
                var log = rig.Cast('막', out _); rig.Advance(0.5, 10);
                var res = Residue(log);
                var again = log.Budget.Pay(rig.Services.Ids.NextAttack().Value, ContactKind.Transfer, EffectKind.Residue, 0.9m, TriggerKind.None, Vol2VerbRunBase.WorldTargetId, 0, 0, rig.Services.Clock.NowUs, 999);
                bool noSecond = !again.Accepted && again.Reason == "MaterialCount";
                bool noMore = rig.Coord.Materials.TryDebit(res.Id, 5m, rig.Services.Clock.NowUs, out decimal taken) && Eq(taken, 0.6m) && res.End == Vol2MaterialEnd.Consumed && !rig.Coord.Materials.TryDebit(res.Id, 0.1m, rig.Services.Clock.NowUs, out _);
                Check(r, "L7", noSecond && noMore && Eq(res.Drawn, 0.9m) && Eq(res.Drawn + Sum(log, "Residue") - Sum(log, "Residue"), 0.9m), "막: the budget refuses a second transfer for the cast (MaterialCount): 0.9 is the most the cast can put in the world; a draw of 5 takes only the 0.6 left; a spent residue refuses");
            }
            // ---- L8 각 ----
            {
                var rig = Rig.Create(book);
                var a = rig.Add("A", 4, 0);
                var log = rig.Cast('각', out _); rig.Advance(0.12, 2);       // the path reaches A (3.45 m at 36 m/s = 0.096 s)
                long hitAt = log.Contacts.FirstOrDefault(c => c.Kind == "Instant") != null ? log.Contacts.First(c => c.Kind == "Instant").NowUs : -1;
                var bind = rig.Status.Requests.FirstOrDefault(q => q.Kind == Vol2BodyActionKind.Bind);
                long cage = RuleTypesV3.Microseconds(n.Get("combat.bind.maxDuration"));
                Check(r, "L8a", hitAt > 0 && Residue(log) == null && bind.DurationUs == cage && cage == 1500000 && Near(bind.Magnitude, 0.5f) && log.Notes.Any(x => x.StartsWith("cage on")), "각: the path hits A, A is caged: bind x0.5 for 1.5 s; no residue yet");
                a.MoveTo(At(5f, 0.5f)); rig.Advance(0.9, 9);
                bool stillCaged = Residue(log) == null;
                rig.Advance(0.6, 12);
                var res = Residue(log);
                bool atRelease = res != null && res.CreatedUs - hitAt >= cage && res.CreatedUs - hitAt < cage + 100000 && Vector3.Distance(new Vector3(res.Footprint.Origin.x, 0, res.Footprint.Origin.z), At(5f, 0.5f)) < 0.01f;
                Check(r, "L8b", stillCaged && atRelease && res.LifeUs == life10 && res.Initial == 0.9m && Eq(log.Budget.Transferred, 0.9m) && Eq(log.Budget.PaidPerTarget + log.Budget.Transferred, n.Get("peak.각.total")),
                    "the cage follows A (it moved to 5 m, 0.5 off): the residue is made 1.5 s after the hit at A's place THEN, 10 s of life from there, 0.9; instant 0.7 + residue 0.9 = 1.6 (peak.각.total)");
                Check(r, "L8c", Eq(Sum(log, "Residue"), 0.3m) && Of(log, "Residue").Count == 1, "A, standing in it, takes a third at once (0.3)");
                // a boss: half the cage
                var rigB = Rig.Create(book); rigB.Add("A", 4, 0); rigB.Coord.BossTest = t => t.Id == 1;
                var logB = rigB.Cast('각', out _); rigB.Advance(0.12, 2);
                var bindB = rigB.Status.Requests.FirstOrDefault(q => q.Kind == Vol2BodyActionKind.Bind);
                Check(r, "L8d", bindB.DurationUs == (long)(cage * n.Get("combat.bind.bossMultiplier")) && bindB.DurationUs == 750000, "a boss is caged for half of it: 0.75 s");
                // the target dies in the cage: the residue is made at once, where it fell
                var rigD = Rig.Create(book); var ad = rigD.Add("A", 4, 0);
                var logD = rigD.Cast('각', out _); rigD.Advance(0.12, 2);
                ad.Alive = false; rigD.Advance(0.05);
                var resD = Residue(logD);
                Check(r, "L8e", resD != null && resD.CreatedUs - logD.Contacts.First(c => c.Kind == "Instant").NowUs < 300000 && logD.Notes.Any(x => x.Contains("target gone")), "A dies in the cage: the residue is made at once at its last place");
            }
            // ---- L9 곡 ----
            {
                var rig = Rig.Create(book);
                rig.Add("In3", 3, 0); rig.Add("In4.8", 4.8f, -0.3f); rig.Add("G", 5.7f, 0f); rig.Add("Out7.5", 7.5f, 0f);
                var log = rig.Cast('곡', out _); rig.Advance(0.05);
                var res = Residue(log);
                float lin = Mathf.Sqrt((float)n.Get("peak.곡.area"));
                bool one = res != null && Mathf.Abs(res.Footprint.GrowLinear - lin) < 1e-5f && Mathf.Abs(res.Footprint.GrowLinear * res.Footprint.GrowLinear - 1.5f) < 1e-4f && res.Footprint.GrowLinear < 1.25f && res.LifeUs == life10;
                Check(r, "L9a", one && Of(log, "Residue").Select(c => c.TargetName).OrderBy(x => x).SequenceEqual(new[] { "In3", "In4.8" }) && !log.Contacts.Any(c => c.TargetName == "G"), "곡: area x1.5 is ONE linear factor sqrt 1.5 = 1.2247 on the radius (not 1.5 on length and width each); G at 5.7 m is beyond the first reach and untouched");
                rig.Advance(1.0, 20); bool notYet = !log.Contacts.Any(c => c.TargetName == "G");
                rig.Advance(2.5, 50);
                var g = Of(log, "Residue").FirstOrDefault(c => c.TargetName == "G");
                Check(r, "L9b", notYet && g != null && g.NowUs - res.CreatedUs >= 2000000 && g.NowUs - res.CreatedUs < 3200000 && res.End == Vol2MaterialEnd.Consumed && !log.Contacts.Any(c => c.TargetName == "Out7.5"),
                    "G (5.7 m) is not taken in at 1 s but is once the fan has grown (needs scale 1.06: about 2.7 s into its life); it draws the last third; the far body never");
                var binds = rig.Status.Requests.Where(q => q.Kind == Vol2BodyActionKind.Bind).ToList();
                Check(r, "L9c", binds.Count == 3 && binds.Select(q => q.Target.Id).Distinct().Count() == 3 && binds.All(q => q.DurationUs == 1500000) && n.Get("peak.곡.bindCount") == 1, "each of the three bodies is bound ONCE, 1.5 s (peak.곡.bindCount = 1); the draw after the instant does not bind again");
                var rigB = Rig.Create(book); rigB.Coord.BossTest = t => true; rigB.Add("In3", 3, 0);
                rigB.Cast('곡', out _); rigB.Advance(0.05);
                Check(r, "L9d", rigB.Status.Requests.Count(q => q.Kind == Vol2BodyActionKind.Bind) == 1 && rigB.Status.Requests.First(q => q.Kind == Vol2BodyActionKind.Bind).DurationUs == 750000, "a boss in the fan is bound 0.75 s");
            }
            // ---- L10 국 ----
            {
                var rig = Rig.Create(book); var field = new FakeField(); rig.Coord.Field = field;
                var mats = rig.Coord.Materials; long now = rig.Services.Clock.NowUs;
                Vol2Material Plant(long cast, long faction, Vector3 at) => mats.Create(new CastId(cast), '낙', Body.Fire, Vol2MaterialKind.Residue, 0.9m, life10, Vol2Footprint.Disc(at, 0.6f), faction == Vol2Faction.Enemy ? 2 : 1, faction, true, now);
                var mine = Plant(9001, Vol2Faction.Player, At(0.5f, 0.5f)); var outside = Plant(9002, Vol2Faction.Player, At(6f, 0f)); var enemy = Plant(9003, Vol2Faction.Enemy, At(1f, 0f));
                var log = rig.Cast('국', out _); rig.Advance(0.05);
                long grown = (long)(life10 * n.Get("peak.국.lifeMultiplier"));
                Check(r, "L10a", mine.LifeExtended && mine.LifeUs == grown && grown == 15 * S && mine.Drawn == 0m && Eq(mine.Remaining, 0.9m) && !outside.LifeExtended && outside.LifeUs == life10 && !enemy.LifeExtended && enemy.LifeUs == life10 && log.Extended.Count == 1,
                    "국: MY residue inside the ring lives 15 s (x1.5), its amount untouched; the one outside the ring and the enemy's keep 10 s");
                var own = Residue(log);
                Check(r, "L10c", own != null && own.LifeUs == life10 && !own.LifeExtended && own.Footprint.Kind == Vol2FootprintKind.Ring && field.Platforms.Count == 1 && log.FieldNotes.Count == 1 && log.FieldNotes[0].Contains("platform ok"),
                    "국's own residue is an ordinary 10 s ring; the field port is asked for the platform (answered 'platform ok' and written to the log)");
                var log2 = rig.Cast('국', out _); rig.Advance(0.05);
                Check(r, "L10b", mine.LifeUs == grown && log2.Extended.Contains(mine.Id.Value + ":refused") && own.LifeExtended && own.LifeUs == grown && Residue(log2).LifeUs == life10 && Residue(log2).LifeExtended == false,
                    "a second 국 does not stretch MY already stretched residue again (refused, still 15 s: no stacking); the first 국's ring, inside the second's, is stretched once; the second's own ring is ordinary");
                var rigN = Rig.Create(book); var logN = rigN.Cast('국', out _); rigN.Advance(0.05);
                Check(r, "L10d", logN.FieldNotes.Count == 1 && logN.FieldNotes[0].Contains("no field port"), "without a field port the platform request is only recorded");
            }
            // ---- L11 the clock jump ----
            {
                var rig = Rig.Create(book); rig.Add("A", 4, 0);
                var log = rig.Cast('삭', out _); rig.Advance(0.5, 10);
                var res = Residue(log);
                rig.Services.Skew.Seconds += 10.2;
                rig.Advance(0.05);
                Check(r, "L11", res.End == Vol2MaterialEnd.Expired && log.Finished && rig.Services.Clock.NowUs > res.ExpiresUs, "the lab's clock jump (+10.2 s) expires the residue in one tick and the cast ends with it");
            }
        }

        static bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-4f;
    }
}
