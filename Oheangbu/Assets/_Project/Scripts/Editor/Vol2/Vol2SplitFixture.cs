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
    /// <summary>S5-b slice 6 edit-mode fixture of 가른다 (D308-149): the material split, the timed strip, the four frames, the peaks 삿 섯 솟 숫, the 날붓. No scene, no Play
    /// (the real armour state of an enemy is the Play lab's, EnemyVitals needs its Awake). Queue:
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2SplitFixture Run "all"
    ///   S1 path 갓: the head splits the material it crosses (not the one beyond the first body, not the one off the path); the first body is stripped 3 s; 0.4 once; a strip is not damage
    ///   S2 삿: the path runs on; cut x1.5 on the first body only; three bodies stripped, the fourth untouched; the cut-in-two reaches the material between B and C
    ///   S3 fan 곳: every material in the fan (mine, the enemy's, a mark), every body in it; owner kept; the 0.4 once
    ///   S4 솟: after the split a metal residue 0.35 / 4 s on the fan, drawn once per body, the cast ends with it; the one material a split makes
    ///   S5 ring 굿 / 숫: a 10 s line (no material): the material on it at the start and the one that lands later; crossings, a recrossing strips again and pays nothing; 숫 4.5 s + the gate request
    ///   S6 attachment 것: three bodies at most (TargetCap), second contact, strip once per body, the 날붓 on each direct hit once; the caster's own path splits what it crosses
    ///   S7 섯: single target 2.1 = 0.7 + 0.3 + 0.35 x 2 + 0.4; three targets; front and back capsule split the material beside / behind the swing
    ///   S8 all 20 glyphs: registered, one target, the plan's total booked, strip length, one body action
    ///   S9 conservation: a second cast splits the halves again; the family always adds up; ids unique
    ///   S10 what is never split: a gate door, an attached carrier, a spent / expired material, the cast's own material
    ///   S11 the strip book: never shortened by a shorter strip; a new life is not stripped
    ///   S12 abort / no port: the swing handler of 섯 is gone after an abort; a missing 날붓 port is recorded, not thrown
    /// Expected values are written here from NumbersV3 and the table, never read back from a run.</summary>
    public static class Vol2SplitFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-split-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-split-fixture: editor busy (play / compile / import)";
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

        static void Swing(Rig rig)
        {
            long id = rig.Services.Ids.NextSwing().Value;
            Vector3 sl = rig.Caster.Position + new Vector3(0, 1.1f, 0), tp = sl + Vector3.forward * 1.6f;
            for (int k = 0; k < 3; k++) rig.Runtime.Swings.Raise(new Vol2SwingEvent(id, sl, tp, rig.Services.Clock.NowUs, k));
        }

        sealed class RecStrip : IVol2StripPort
        {
            public readonly List<(string name, long dur, long at)> Calls = new List<(string, long, long)>();
            public string Apply(IVol2Target t, long durationUs, long nowUs) { Calls.Add((t.Name, durationUs, nowUs)); return "strip ok fake"; }
        }

        sealed class RecBlade : IVol2BladePort
        {
            public readonly HashSet<string> Counter = new HashSet<string>();
            public readonly List<(string name, char letter)> Calls = new List<(string, char)>();
            readonly Dictionary<string, decimal> _posture = new Dictionary<string, decimal>();
            public Vol2BladeResult Hit(IVol2Target target, Glyph glyph, long nowUs)
            {
                Calls.Add((target.Name, glyph.Letter));
                bool c = Counter.Contains(target.Name);
                _posture.TryGetValue(target.Name, out decimal p);
                if (c) p += 10m;
                _posture[target.Name] = p;
                return new Vol2BladeResult(true, c, true, c ? 10m : 0m, p, false, "fake");
            }
        }

        static Vol2SplitExecutor Exec(Rig rig) { rig.Coord.Verbs.TryGet(Verb.Split, out var e); return e as Vol2SplitExecutor; }

        static Vol2Material Plant(Rig rig, long cast, string tag, Body body, long faction, float ahead, float side, bool door = false, Vol2MaterialKind kind = Vol2MaterialKind.Residue, decimal amount = 0.9m, bool pays = true, Vol2Footprint fp = null, long lifeUs = 0)
        {
            if (lifeUs == 0) lifeUs = RuleTypesV3.Microseconds(rig.Numbers.Get("timing.g.life"));
            return rig.Coord.Materials.Create(new CastId(cast), '가', body, kind, amount, lifeUs, fp ?? Vol2Footprint.Disc(At(ahead, side), 0.6f), faction == Vol2Faction.Enemy ? 2 : 1, faction, pays, rig.Services.Clock.NowUs, 0, 0, tag, door);
        }

        static float Dist(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return (a - b).magnitude; }

        static void Cases(Report r)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var n = book.Numbers;
            long strip3 = RuleTypesV3.Microseconds(n.Get("combat.strip.duration")), strip45 = RuleTypesV3.Microseconds(n.Get("combat.strip.peakDuration"));
            decimal inst = n.Get("action.instant"), val = n.Get("action.strip.utility");
            {
                var probe = Rig.Create(book);
                var ex = Exec(probe);
                Check(r, "F1", ex != null && probe.Coord.Verbs.Handles(Verb.Split) && strip3 == 3 * S && strip45 == 4500000 && val == 0.4m && n.Get("reaction.split.parts") == 2 && n.Get("reaction.split.amountPerPart") == 0.5m,
                    "the 가른다 executor is registered; strip 3 s / 4.5 s, value 0.4, a split is 2 parts of 1/2 (the data)");
            }

            // ---- S1 path 갓 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); var sp = new RecStrip(); ex.StripPort = sp;
                rig.Add("A", 4, 0); rig.Add("B", 8, 0);
                var m1 = Plant(rig, 9001, "M1", Body.Wood, Vol2Faction.Player, 2, 0); var m2 = Plant(rig, 9002, "M2", Body.Wood, Vol2Faction.Enemy, 6.5f, 0.1f); var m3 = Plant(rig, 9003, "M3", Body.Wood, Vol2Faction.Player, 2, 3.5f);
                long createdBefore = rig.Coord.Materials.TotalCreated;
                var log = rig.Cast('갓', out var dec); rig.Advance(0.5, 10);
                var data = ex.DataOf(log.Cast); var ins = Of(log, "Instant");
                Check(r, "S1a", ins.Count == 1 && ins[0].TargetName == "A" && ins[0].Amount == 0.7m && Eq(ins[0].Units, 0.42m) && ins[0].BudgetReason == "FirstContact" && !log.Contacts.Any(c => c.TargetName == "B") && log.Finished && log.Status == "Done",
                    "갓: the path ends at A (4 m, units 0.6 x 0.7 = 0.42); B behind it is never touched; the cast is Done");
                var se = data.Strips;
                Check(r, "S1b", se.Count == 1 && se[0].TargetName == "A" && se[0].DurationUs == strip3 && se[0].UntilUs == se[0].AtUs + strip3 && se[0].Value == 0.4m && !se[0].Restrip && sp.Calls.Count == 1 && sp.Calls[0].dur == strip3
                    && ex.Strips.IsStripped(rig.Targets[0], se[0].AtUs + strip3 - 1) && !ex.Strips.IsStripped(rig.Targets[0], se[0].AtUs + strip3),
                    "A is stripped for exactly 3 s on the scope clock ([start, start + 3 s)); the port was asked once for 3 s; the entry carries the 0.4");
                Check(r, "S1c", Eq(log.Budget.PaidPerTarget, 0.7m) && Eq(log.Budget.PaidShared, 0.4m) && Eq(log.Budget.Transferred, 0m) && Eq(log.Budget.SharedRemaining, 0m) && Of(log, "Strip").Count == 1 && Eq(Of(log, "Strip")[0].Amount, 0.4m) && Of(log, "Strip")[0].BudgetReason == "SharedPaid",
                    "the cast paid 0.7 per target + 0.4 shared (the strip's value, once), nothing transferred, the pool is empty");
                Check(r, "S1d", rig.Damage.Requests.Count == 1 && rig.Status.Requests.Count == 1 && rig.Status.Requests[0].Kind == Vol2BodyActionKind.Bind && log.Contacts.Count(c => c.BodyEffect.Length > 0) == 1 && log.Contacts.Where(c => c.Kind == "Strip").All(c => !c.DamageRequested && c.BodyEffect.Length == 0),
                    "a strip is not damage (one damage request in all) and not a body action: A is bound ONCE (wood), by the cut");
                var s = data.Splits;
                var kid = s.Count == 1 && s[0].Children.Count == 1 ? s[0].Children[0] : null;
                Check(r, "S1e", s.Count == 1 && s[0].Parent == m1 && kid != null && Eq(m1.Remaining, 0.45m) && Eq(kid.Remaining, 0.45m) && Eq(m1.Remaining + kid.Remaining, 0.9m) && m1.Splits == 1 && m1.Drawn == 0m && m1.Initial == 0.9m,
                    "M1 (on the path before A) is cut in two: the parent keeps its id and holds 0.45, the child holds 0.45 - the halves add up to the 0.9");
                Check(r, "S1f", kid != null && kid.Cast == m1.Cast && kid.OwnerId == m1.OwnerId && kid.FactionId == m1.FactionId && kid.CreatedUs == m1.CreatedUs && kid.ExpiresUs == m1.ExpiresUs && kid.Depth == m1.Depth && kid.Body == m1.Body && kid.Kind == m1.Kind && kid.Id != m1.Id && kid.Alive && m1.Alive,
                    "the child keeps the parent's cast, owner, faction, birth, expiry, depth, body and kind: ownership and life do not change");
                Check(r, "S1g", m2.Splits == 0 && m2.Remaining == 0.9m && m3.Splits == 0 && m3.Remaining == 0.9m && rig.Coord.Materials.ByCast(log.Cast).Count == 0 && rig.Coord.Materials.TotalCreated == createdBefore + 1 && rig.Coord.Materials.Audit().Count == 0,
                    "M2 (beyond the first body) and M3 (off the path) are whole; the cast made no material - the one new material is the child; the registry audit is clean");
                Check(r, "S1h", kid != null && m1.Footprint.Kind == Vol2FootprintKind.Disc && Mathf.Abs(Dist(m1.Footprint.Origin, kid.Footprint.Origin) - 2f * 0.6f * ex.Tuning.HalfOffsetFraction) < 1e-4f && Mathf.Abs(m1.Footprint.Radius - 0.6f * ex.Tuning.HalfRadiusScale) < 1e-4f && Mathf.Abs(kid.Footprint.Radius - m1.Footprint.Radius) < 1e-6f,
                    "the two halves stand apart either side of the cut (0.6 m), each of radius 0.6 x 0.7071 (TE-S6-1)");
            }
            // ---- S2 삿 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip();
                rig.Add("A", 3, 0); rig.Add("B", 6, 0); rig.Add("C", 9, 0); rig.Add("D", 11, 0);
                var m1 = Plant(rig, 9001, "M1", Body.Wood, Vol2Faction.Player, 1.5f, 0); var m2 = Plant(rig, 9002, "M2", Body.Wood, Vol2Faction.Enemy, 7.5f, 0.1f); var m3 = Plant(rig, 9003, "M3", Body.Wood, Vol2Faction.Player, 2, 3.5f);
                var log = rig.Cast('삿', out var dec); rig.Advance(0.6, 12);
                var data = ex.DataOf(log.Cast); var ins = Of(log, "Instant");
                Check(r, "S2a", ins.Count == 1 && ins[0].TargetName == "A" && ins[0].Amount == 1.05m && Eq(ins[0].Units, 0.84m) && log.Finished && log.Status == "Done",
                    "삿: the cut x1.5 (1.05, units 0.8 x 1.05 = 0.84) lands on the first body only; the path ran its 12 m and the cast is Done");
                var b = log.Contacts.Where(c => (c.TargetName == "B" || c.TargetName == "C") && c.Kind == "Touch").ToList();
                Check(r, "S2b", b.Count == 2 && b.All(c => !c.BudgetAccepted && c.BudgetReason == "FirstDamageOnly" && !c.DamageRequested) && !log.Contacts.Any(c => c.TargetName == "D"),
                    "B and C are refused a second paid cut (FirstDamageOnly: no copy of the x1.5), the fourth body D is not even logged as touched");
                var names = data.Strips.Select(x => x.TargetName).ToList();
                Check(r, "S2c", names.SequenceEqual(new[] { "A", "B", "C" }) && data.Strips.All(x => x.DurationUs == strip3 && !x.Restrip) && data.Strips.Count(x => x.Value > 0m) == 1 && data.Strips[0].Value == 0.4m && n.Get("peak.삿.pierceCount") == 3,
                    "the first three bodies are stripped 3 s (peak.삿.pierceCount = 3), in the order the path met them; the 0.4 rides with the first");
                Check(r, "S2d", Eq(log.Budget.PaidPerTarget, 1.05m) && Eq(log.Budget.PaidShared, 0.4m) && Eq(log.Budget.PaidPerTarget + log.Budget.PaidShared, n.Get("peak.삿.total")) && log.Budget.PaidPerTarget + log.Budget.PaidShared <= n.Get("peak.삿.cap"),
                    "the cast paid 1.05 + 0.4 = 1.45 (peak.삿.total), within the cap 1.65");
                Check(r, "S2e", rig.Status.Requests.Count == 1 && rig.Status.Requests[0].Target.Name == "A" && rig.Damage.Requests.Count == 1, "only A takes the body action and the damage; B and C are stripped, nothing more");
                Check(r, "S2f", data.Splits.Count == 2 && data.Splits.Select(x => x.Parent).SequenceEqual(new[] { m1, m2 }) && m3.Splits == 0 && Eq(m2.Remaining, 0.45m) && m2.FactionId == Vol2Faction.Enemy,
                    "the path runs on past A: it also cuts M2 (the enemy's, between B and C); M1 is cut, M3 (off the path) is not");
            }
            // ---- S3 fan 곳 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); var sp = new RecStrip(); ex.StripPort = sp;
                rig.Add("In3", 3, 0); rig.Add("In4.8", 4.8f, -0.3f); rig.Add("Out5.8", 5.8f, 0); rig.Add("Behind", -2, 0);
                var p1 = Plant(rig, 9001, "P1", Body.Wood, Vol2Faction.Player, 3.5f, 1.0f); var p2 = Plant(rig, 9002, "P2", Body.Wood, Vol2Faction.Enemy, 2, -0.8f);
                var p3 = Plant(rig, 9003, "P3", Body.Wood, Vol2Faction.Player, 7, 0); var p4 = Plant(rig, 9004, "P4", Body.Wood, Vol2Faction.Player, -1.5f, 0);
                var p5 = Plant(rig, 9005, "P5mark", Body.Fire, Vol2Faction.Player, 4.0f, 0.5f, false, Vol2MaterialKind.Mark, 0.25m, false);
                var log = rig.Cast('곳', out _); rig.Advance(0.2, 2);
                var data = ex.DataOf(log.Cast);
                Check(r, "S3a", Names(log, "Instant").SequenceEqual(new[] { "In3", "In4.8" }) && !log.Contacts.Any(c => c.TargetName == "Out5.8" || c.TargetName == "Behind") && log.Finished && log.Status == "Done" && Eq(log.Budget.PaidPerTarget, 1.4m),
                    "곳: every body inside the fan (In3, In4.8) is hit once (0.7 each), Out5.8 (beyond 5 m) and Behind are not; the fan is one instant");
                Check(r, "S3b", data.Strips.Select(x => x.TargetName).SequenceEqual(new[] { "In3", "In4.8" }) && data.Strips.All(x => x.DurationUs == strip3) && data.Strips[0].Value == 0.4m && data.Strips[1].Value == 0m && Eq(log.Budget.PaidShared, 0.4m) && sp.Calls.Count == 2,
                    "both are stripped 3 s, nearest first; the 0.4 is booked once (with In3), the second strip is state only");
                Check(r, "S3c", data.Splits.Select(x => x.Parent.Tag).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "P1", "P2", "P5mark" }) && p3.Splits == 0 && p4.Splits == 0,
                    "every material in the fan is cut (mine P1, the enemy's P2, the mark P5), the ones outside (P3) and behind (P4) are not");
                var k2 = data.Splits.First(x => x.Parent == p2).Children[0]; var k5 = data.Splits.First(x => x.Parent == p5).Children[0];
                Check(r, "S3d", k2.FactionId == Vol2Faction.Enemy && k2.OwnerId == p2.OwnerId && Eq(p2.Remaining, 0.45m) && Eq(k2.Remaining, 0.45m) && Eq(p5.Remaining, 0.125m) && Eq(k5.Remaining, 0.125m) && k5.Kind == Vol2MaterialKind.Mark && !k5.PaysPool && k5.Body == Body.Fire,
                    "an enemy's material is cut and stays the enemy's; a mark (reaction fuel, not a pool) is cut 0.125 + 0.125 and stays a mark");
                Check(r, "S3e", rig.Coord.Materials.ByCast(log.Cast).Count == 0 && rig.Coord.Materials.Audit().Count == 0 && rig.Status.Requests.Count == 2, "the fan made no material; the audit is clean; each of the two bodies took one body action");
            }
            // ---- S4 솟 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip();
                rig.Add("In3", 3, 0); rig.Add("In4.8", 4.8f, -0.3f); rig.Add("Out5.8", 5.8f, 0); var l = rig.Add("L", 7, 0);
                var p1 = Plant(rig, 9001, "P1", Body.Wood, Vol2Faction.Player, 3.5f, 1.0f); var p3 = Plant(rig, 9003, "P3", Body.Wood, Vol2Faction.Player, 7, 2);
                var log = rig.Cast('솟', out _); rig.Advance(0.1, 2);
                var data = ex.DataOf(log.Cast);
                var res = log.Materials.FirstOrDefault();
                Check(r, "S4a", log.Materials.Count == 1 && res != null && res.Body == Body.Metal && res.Kind == Vol2MaterialKind.Residue && res.Initial == 0.35m && res.LifeUs == 4 * S && res.Footprint.Kind == Vol2FootprintKind.Fan && res.FactionId == Vol2Faction.Player && res.Depth == 0 && res.PaysPool
                    && Eq(log.Budget.Transferred, 0.35m) && rig.Coord.Materials.ByCast(log.Cast).Count == 1,
                    "솟: after the split a metal residue stands on the fan - 0.35, 4 s, mine, depth 0 - booked as a transfer; the one material a cut makes");
                Check(r, "S4b", data.Splits.Select(x => x.Parent.Tag).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "P1" }) && p3.Splits == 0 && !data.Splits.Any(x => x.Parent == res || x.Children.Contains(res)) && Eq(log.Budget.PaidPerTarget, 1.4m) && Eq(log.Budget.PaidShared, 0.4m),
                    "the materials in the fan are cut, the metal itself is not (the cast's own); 0.7 x 2 + 0.4 + 0.35 transferred = 1.45 + 0.7 (two bodies)");
                var draws0 = Of(log, "Residue");
                Check(r, "S4c", draws0.Select(c => c.TargetName).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "In3", "In4.8" }) && draws0.All(c => Math.Abs(c.Amount - 0.35m / 3m) < 0.000000001m) && !log.Finished,
                    "the two bodies standing in the fan draw a third of the metal each, once; the cast is not over while the metal stands");
                rig.Advance(0.6, 6); l.MoveTo(At(3.5f, -0.5f)); rig.Advance(0.1);
                var draws = Of(log, "Residue");
                Check(r, "S4d", draws.Count == 3 && draws.Select(c => c.TargetName).SequenceEqual(new[] { "In3", "In4.8", "L" }) && Math.Abs((res.Drawn) - draws.Sum(c => c.Amount)) < 0.000000001m && Math.Abs(res.Drawn + res.Remaining - 0.35m) < 0.000000001m && !draws.Any(c => !c.DamageApplied) && log.Contacts.Count(c => c.BodyEffect.Length > 0) == 3,
                    "L walks in and is cut once; nothing is drawn twice; drawn + left = 0.35; three body actions in all (the walker's the first it takes)");
                rig.Advance(4.5, 45);
                Check(r, "S4e", log.Finished && log.Status == "Done" && !res.Alive && (res.End == Vol2MaterialEnd.Expired || res.End == Vol2MaterialEnd.Consumed) && res.EndedUs <= res.ExpiresUs + 200000 && rig.Coord.Materials.Audit().Count == 0,
                    "the metal ends (" + res.End + ") within its 4 s and the cast is Done with it; the audit is clean");
            }
            // ---- S5 ring 굿 / 숫 ----
            foreach (char ring in new[] { '굿', '숫' })
            {
                bool gate = ring == '숫';
                var rig = Rig.Create(book); var ex = Exec(rig); var sp = new RecStrip(); ex.StripPort = sp;
                rig.Add("Band2.0", 2, 0); rig.Add("Hole0.5", 0.5f, 0.3f); var c1 = rig.Add("C", 3.4f, -1); var d1 = rig.Add("D", 6, 0); rig.Add("E", 6, 2);
                var r1 = Plant(rig, 9001, "R1", Body.Wood, Vol2Faction.Player, 0, 2.0f); var r2 = Plant(rig, 9002, "R2", Body.Wood, Vol2Faction.Player, 0.3f, 0.3f); var r3 = Plant(rig, 9003, "R3", Body.Wood, Vol2Faction.Player, 6, -3);
                var log = rig.Cast(ring, out _); rig.Advance(0.05);
                var data = ex.DataOf(log.Cast);
                bool startOk = Names(log, "Instant").SequenceEqual(new[] { "Band2.0" }) && data.Splits.Count == 1 && data.Splits[0].Parent == r1;
                c1.MoveTo(At(0.5f, -0.5f)); rig.Advance(0.05);
                c1.MoveTo(At(0.5f, -0.5f)); rig.Advance(0.05);
                var r4 = Plant(rig, 9004, "R4", Body.Wood, Vol2Faction.Enemy, -2, 0.3f); rig.Advance(0.05);
                c1.MoveTo(At(3.4f, -1f)); rig.Advance(0.05);
                d1.MoveTo(At(0.6f, 0.2f)); rig.Advance(0.05);
                long want = gate ? strip45 : strip3;
                Check(r, "S5a" + ring, startOk && !log.Finished && log.Materials.Count == 0 && rig.Coord.Materials.ByCast(log.Cast).Count == 0,
                    ring + ": the ring is a 10 s line (no material of its own); at the start the body on the band (Band2.0) is hit and the material on the band (R1) is cut; the cast is running");
                Check(r, "S5b" + ring, Names(log, "Instant").OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "Band2.0", "C", "D" }) && !log.Contacts.Any(c => c.TargetName == "Hole0.5" || c.TargetName == "E") && Eq(log.Budget.PaidPerTarget, 2.1m) && Eq(log.Budget.PaidShared, 0.4m),
                    "bodies that cross the band are hit once each (0.7 x 3 + 0.4); the one in the hollow and the one that never moves are not");
                var cs = data.Strips.Where(x => x.TargetName == "C").ToList();
                Check(r, "S5c" + ring, data.Strips.Count == 4 && cs.Count == 2 && !cs[0].Restrip && cs[1].Restrip && cs[1].Value == 0m && cs[1].UntilUs > cs[0].UntilUs && data.Strips.All(x => x.DurationUs == want) && data.Strips.Count(x => x.Value > 0m) == 1 && data.Strips[0].TargetName == "Band2.0"
                    && Of(log, "Instant").Count(c => c.TargetName == "C") == 1 && Eq(log.Budget.PaidShared, 0.4m),
                    "C crosses twice: stripped twice (the second renews the state, pays nothing: one Instant, one 0.4), all strips " + (gate ? "4.5" : "3") + " s");
                Check(r, "S5d" + ring, data.Splits.Select(x => x.Parent).SequenceEqual(new[] { r1, r4 }) && r2.Splits == 0 && r3.Splits == 0 && Eq(r4.Remaining, 0.45m) && r4.FactionId == Vol2Faction.Enemy,
                    "the material that lands on the band later (R4) is cut when it arrives; the one in the hollow (R2) and the one outside (R3) are never cut");
                Check(r, "S5e" + ring, gate ? log.FieldNotes.Count == 1 && log.FieldNotes[0].Contains("cut rock/iron gate") && log.FieldNotes[0].Contains("recorded only") : log.FieldNotes.Count == 0,
                    gate ? "숫: the field is asked to cut a rock / iron gate (no field port: recorded)" : "굿: no gate request - an ordinary line cuts no gate");
                rig.Advance(10.5, 105);
                Check(r, "S5f" + ring, log.Finished && log.Status == "Done" && log.EndUs - log.StartUs >= 10 * S && log.EndUs - log.StartUs < 10 * S + 700000, ring + ": the line lasts 10 s (timing.s.ringLife) and the cast ends with it");
            }
            // ---- S6 attachment 것 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip(); var blade = new RecBlade(); blade.Counter.Add("A"); ex.Blade = blade;
                rig.Add("A", 0.8f, 0); rig.Add("B", 1.0f, 0.3f); rig.Add("C", 1.2f, -0.3f); rig.Add("D", 1.4f, 0); rig.Add("E", 3.5f, 0);
                var m1 = Plant(rig, 9001, "M1", Body.Wood, Vol2Faction.Player, 0, 0); var m2 = Plant(rig, 9002, "M2", Body.Wood, Vol2Faction.Player, 5, 3);
                var m3 = Plant(rig, 9003, "M3", Body.Wood, Vol2Faction.Player, -1.2f, 0); var m5 = Plant(rig, 9005, "M5", Body.Wood, Vol2Faction.Enemy, 1.2f, 0.9f);
                var log = rig.Cast('것', out _); rig.Advance(0.05); Swing(rig);
                rig.Advance(0.4); Swing(rig);
                rig.Advance(0.25); Swing(rig);
                var data = ex.DataOf(log.Cast);
                Check(r, "S6a", Names(log, "Instant").SequenceEqual(new[] { "A", "B", "C" }) && Names(log, "Second").SequenceEqual(new[] { "A", "B", "C" }) && log.Contacts.Where(c => c.TargetName == "D").All(c => !c.BudgetAccepted && c.BudgetReason == "TargetCap") && !log.Contacts.Any(c => c.TargetName == "E"),
                    "것: swing 1 touches A B C (D, a fourth, is refused TargetCap), swing 2 is their second contact, swing 3 pays nothing; E is out of reach");
                Check(r, "S6b", Eq(log.Budget.PaidPerTarget, 3.0m) && Eq(log.Budget.PaidShared, 0.4m) && data.Strips.Select(x => x.TargetName).SequenceEqual(new[] { "A", "B", "C" }) && data.Strips.All(x => x.DurationUs == strip3 && !x.Restrip) && data.Strips[0].Value == 0.4m && data.Strips.Count(x => x.Value > 0m) == 1,
                    "the cast paid (0.7 + 0.3) x 3 + 0.4; each body is stripped ONCE (3 s) at its first contact - the second contact does not strip again; the 0.4 rides with A");
                Check(r, "S6c", blade.Calls.Count == 6 && blade.Calls.All(c => c.letter == '것') && data.Blades.Count == 6 && data.Blades.Count(b => b.Kind == "Instant") == 3 && data.Blades.Count(b => b.Kind == "Second") == 3 && data.Blades.Select(b => b.Attack.Value).Distinct().Count() == 6 && data.BladeDuplicates == 0
                    && data.Blades.Where(b => b.TargetName == "A").All(b => b.Result.Counter && b.Result.Added == 10m) && data.Blades.Where(b => b.TargetName != "A").All(b => !b.Result.Counter),
                    "날붓: asked on each of the six direct hits (Instant + Second of three bodies), once per AttackId; the third swing (a refused touch) and the repeated colliders add none; only the body A overcomes");
                Check(r, "S6d", data.Splits.Select(x => x.Parent).SequenceEqual(new[] { m1 }) && m2.Splits == 0 && m3.Splits == 0 && m5.Splits == 0 && Eq(m1.Remaining, 0.45m),
                    "the material the caster stands in is cut; the far one, the one behind and the one beside the swing are not (only 섯 cuts front and back)");
                Check(r, "S6e", log.Contacts.Count(c => c.BodyEffect.Length > 0) == 3 && rig.Status.Requests.Count == 3 && rig.Damage.Requests.Count == 6, "three bodies, three body actions (bind, once each); six damage requests (3 x Instant + Second), none for a strip");
                rig.Advance(6.5, 65);
                Check(r, "S6f", log.Finished && log.Status == "Done" && log.EndUs - log.StartUs >= 6 * S, "the cast ends with the body's 6 s");
            }
            {
                // the caster's own path
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip();
                var near = Plant(rig, 9001, "Cross", Body.Wood, Vol2Faction.Player, 4.5f, 0); var off = Plant(rig, 9002, "Off", Body.Wood, Vol2Faction.Player, 3, 3);
                var log = rig.Cast('넛', out _); rig.Advance(0.5, 5);
                bool still = near.Splits == 0;
                rig.Caster.Position = new Vector3(0, 0, 5f); rig.Advance(0.1);
                var data = ex.DataOf(log.Cast);
                Check(r, "S6g", still && data.Splits.Count == 1 && data.Splits[0].Parent == near && off.Splits == 0 && Eq(near.Remaining, 0.45m) && data.Splits[0].Where == "caster",
                    "넛: the caster walks 5 m and splits the material he passes through (a path from this tick's start to its end, not only the feet at the end); the one off to the side stays whole");
            }
            // ---- S7 섯 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip(); var blade = new RecBlade(); ex.Blade = blade;
                rig.Add("A", 0.8f, 0);
                var log = rig.Cast('섯', out _); rig.Advance(0.05); Swing(rig);
                rig.Advance(0.4); Swing(rig); rig.Advance(0.25); Swing(rig);
                var data = ex.DataOf(log.Cast); var rear = Of(log, "Rear");
                Check(r, "S7a", Eq(log.Budget.PaidPerTarget, 1.7m) && Eq(log.Budget.PaidShared, 0.4m) && Eq(log.Budget.PaidPerTarget + log.Budget.PaidShared, n.Get("peak.섯.total")) && n.Get("peak.섯.total") == 2.1m && Eq(n.Get("peak.섯.cap"), 2.1m),
                    "섯 on one target: 0.7 + 0.3 + 0.35 x 2 + 0.4 = 2.1 (peak.섯.total, cap 2.1) - the rules' own sum");
                Check(r, "S7b", rear.Count == 2 && rear.All(c => c.Amount == 0.35m && c.BudgetReason == "RearShare" && c.DamageApplied && Eq(c.Units, 0.28m)) && data.RearHits == 2 && Of(log, "Instant").Count == 1 && Of(log, "Second").Count == 1 && rig.Damage.Requests.Count == 4,
                    "the back cut is half of the front (0.35 = 0.7 x 0.5), one with each of the body's two contacts, a hit of its own (units 0.8 x 0.35 = 0.28): four damage requests in all");
                Check(r, "S7c", blade.Calls.Count == 2 && data.Blades.Select(b => b.Kind).SequenceEqual(new[] { "Instant", "Second" }), "the rear cut is not a direct hit: the 날붓 is asked twice (Instant, Second), not four times");
            }
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip(); ex.Blade = new RecBlade();
                rig.Add("A", 0.8f, 0); rig.Add("B", 1.0f, 0.3f); rig.Add("C", 1.2f, -0.3f); rig.Add("D", 1.4f, 0);
                var m1 = Plant(rig, 9001, "M1", Body.Wood, Vol2Faction.Player, 0, 0); var m2 = Plant(rig, 9002, "M2", Body.Wood, Vol2Faction.Player, 5, 3);
                var m3 = Plant(rig, 9003, "M3", Body.Wood, Vol2Faction.Player, -1.2f, 0); var m5 = Plant(rig, 9005, "M5", Body.Wood, Vol2Faction.Enemy, 1.2f, 0.9f);
                var log = rig.Cast('섯', out _); rig.Advance(0.05); Swing(rig);
                rig.Advance(0.4); Swing(rig); rig.Advance(0.25); Swing(rig);
                var data = ex.DataOf(log.Cast);
                Check(r, "S7d", Names(log, "Instant").SequenceEqual(new[] { "A", "B", "C" }) && Of(log, "Rear").Count == 6 && log.Contacts.Where(c => c.TargetName == "D").All(c => c.BudgetReason == "TargetCap") && Eq(log.Budget.PaidPerTarget, 5.1m) && Eq(log.Budget.PaidShared, 0.4m),
                    "섯 on four bodies: three at most (D refused TargetCap), six rear cuts, (1.7) x 3 + 0.4");
                var tags = data.Splits.ToDictionary(x => x.Parent.Tag, x => x.Where);
                Check(r, "S7e", tags.Count == 3 && tags.ContainsKey("M1") && tags.ContainsKey("M3") && tags.ContainsKey("M5") && m2.Splits == 0 && tags["M3"] == "swing-rear" && tags["M5"] == "swing-front",
                    "the swing's front capsule cuts the material beside it (M5), the mirrored capsule behind the caster cuts the one behind (M3), the caster's own path the one he stands in (M1); the far one is whole");
                Check(r, "S7f", data.Splits.All(x => Eq(x.Parent.Remaining + x.Children.Sum(k => k.Remaining), x.Before) && x.Parent.Splits == 1), "every cut family adds up to what the material held; three colliders per swing cut a material once");
            }
            // ---- S8 all 20 ----
            {
                int ok = 0; var bad = new List<string>();
                foreach (Body body in Enum.GetValues(typeof(Body)))
                    foreach (Frame frame in Enum.GetValues(typeof(Frame)))
                    {
                        char letter = GlyphRulesV3.Encode(body, frame, Verb.Split);
                        var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip();
                        rig.Add("A", frame == Frame.Path ? 4f : frame == Frame.Fan ? 3f : frame == Frame.Ring ? 2f : 0.8f, 0f);
                        var log = rig.Cast(letter, out var dec);
                        if (log == null) { bad.Add(letter + " refused " + dec.Reason); continue; }
                        rig.Advance(0.05);
                        if (frame == Frame.Attachment) { Swing(rig); rig.Advance(0.4); Swing(rig); }
                        rig.Advance(12.5, 125);
                        var data = ex.DataOf(log.Cast); var plan = dec.Plan;
                        bool peak = plan.Glyph.Kind == Oheangbu.Vol2.Rules.SpellKind.Peak;
                        long want = letter == '숫' ? strip45 : strip3;
                        decimal paid = log.Budget.PaidPerTarget + log.Budget.PaidShared + log.Budget.Transferred;
                        var why = new List<string>();
                        if (log.Status != "Done") why.Add("status " + log.Status);
                        if (!Eq(paid, plan.TotalFor(1))) why.Add("paid " + paid + " != plan " + plan.TotalFor(1));
                        if (data == null || data.Strips.Count != 1 || data.Strips[0].DurationUs != want || data.Strips[0].Value != 0.4m) why.Add("strip " + (data == null ? "no data" : data.Strips.Count + " entries"));
                        if (Of(log, "Instant").Count != 1 || (frame == Frame.Attachment ? Of(log, "Second").Count != 1 : Of(log, "Second").Count != 0)) why.Add("contacts");
                        if (rig.Status.Requests.Count != 1) why.Add("body actions " + rig.Status.Requests.Count);
                        if (letter == '섯' && Of(log, "Rear").Count != 2) why.Add("rear " + Of(log, "Rear").Count);
                        if (letter != '섯' && Of(log, "Rear").Count != 0) why.Add("unexpected rear");
                        if (letter == '솟' ? log.Materials.Count != 1 : log.Materials.Count != 0) why.Add("materials " + log.Materials.Count);
                        if (!Eq(plan.Shared, letter == '솟' ? 0.75m : 0.4m)) why.Add("plan shared " + plan.Shared);
                        if (why.Count == 0) ok++; else bad.Add(letter + " " + string.Join(", ", why));
                    }
                Check(r, "S8", ok == 20 && bad.Count == 0, "all 20 가른다 glyphs: accepted, one target, Done, the plan's total booked (per target + shared + transfer), one strip of the right length carrying the 0.4, one body action, a rear cut only for 섯, a material only for 솟" + (bad.Count > 0 ? " | " + string.Join(" | ", bad) : ""));
            }
            // ---- S9 conservation ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip();
                rig.Add("A", 4, 0);
                var m = Plant(rig, 9001, "M", Body.Wood, Vol2Faction.Player, 2, 0);
                var l1 = rig.Cast('갓', out _); rig.Advance(0.5, 10);
                var k1 = ex.DataOf(l1.Cast).Splits[0].Children[0];
                rig.Targets[0].Alive = false; var a2 = rig.Add("A2", 4, 0);
                var l2 = rig.Cast('갓', out _); rig.Advance(0.5, 10);
                var d2 = ex.DataOf(l2.Cast);
                var family = rig.Coord.Materials.ByCast(new CastId(9001));
                Check(r, "S9a", d2.Splits.Count == 2 && d2.Splits.Select(x => x.Parent.Id.Value).OrderBy(x => x).SequenceEqual(new[] { m.Id.Value, k1.Id.Value }) && family.Count == 4 && family.Select(x => x.Id.Value).Distinct().Count() == 4 && Eq(family.Sum(x => x.Remaining), 0.9m)
                    && family.All(x => x.Cast.Value == 9001 && x.ExpiresUs == m.ExpiresUs && x.CreatedUs == m.CreatedUs && x.Depth == 0 && x.FactionId == Vol2Faction.Player) && m.Splits == 2 && k1.Splits == 2 && rig.Coord.Materials.Audit().Count == 0,
                    "a second cast cuts the two halves again (the halves still lie on the path): four pieces, four ids, 0.9 in all, one cast, one expiry, depth 0, owner unchanged; the registry audit is clean");
                Check(r, "S9b", family.Count(x => x.Alive) == 4 && Eq(m.Remaining, 0.225m) && family.All(x => Eq(x.Remaining, 0.225m)), "each piece holds a quarter (0.225)");
                var attacks = l2.Contacts.Select(c => c.Attack.Value).ToList();
                Check(r, "S9c", attacks.Distinct().Count() == attacks.Count && l2.Contacts.All(c => c.DuplicateCommands == 0) && attacks.Count == 2, "every record (the hit, the strip) has its own AttackId; no command ran twice");
            }
            // ---- S10 never split ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip();
                rig.Add("A", 6, 0);
                var door = Plant(rig, 9001, "door", Body.Wood, Vol2Faction.World, 2, 0, true);
                var carrier = Plant(rig, 9002, "carrier", Body.Fire, Vol2Faction.Player, 2, 0, false, Vol2MaterialKind.Carrier, 0m, true, Vol2Footprint.Attached(Vector3.zero));
                var spent = Plant(rig, 9003, "spent", Body.Wood, Vol2Faction.Player, 2, 0.1f); rig.Coord.Materials.TryDebit(spent.Id, 0.9m, rig.Services.Clock.NowUs, out _);
                var empty = Plant(rig, 9004, "zero", Body.Wood, Vol2Faction.Player, 2, 0.2f, false, Vol2MaterialKind.Mark, 0m, false);
                var shortLived = Plant(rig, 9005, "short", Body.Wood, Vol2Faction.Player, 2, 0.3f, false, Vol2MaterialKind.Residue, 0.9m, true, null, 100000);
                var plain = Plant(rig, 9006, "plain", Body.Wood, Vol2Faction.Player, 2, 0f);
                rig.Advance(0.2, 2);
                var log = rig.Cast('갓', out _); rig.Advance(0.5, 10);
                var data = ex.DataOf(log.Cast);
                Check(r, "S10a", data.Splits.Count == 1 && data.Splits[0].Parent == plain && door.Splits == 0 && carrier.Splits == 0 && spent.Splits == 0 && empty.Splits == 0 && shortLived.Splits == 0 && Eq(door.Remaining, 0.9m),
                    "a gate door, a body-attached carrier, a spent material, an empty mark and an expired material are never cut; only the plain residue is");
            }
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip();
                rig.Add("In3", 3, 0);
                var log = rig.Cast('솟', out _); rig.Advance(0.1, 2);
                var res = log.Materials[0];
                var log2 = rig.Cast('곳', out _); rig.Advance(0.1, 2);
                var d2 = ex.DataOf(log2.Cast);
                Check(r, "S10b", d2.Splits.Count == 1 && d2.Splits[0].Parent == res && res.Splits == 1 && Eq(res.Remaining + d2.Splits[0].Children[0].Remaining, res.Initial - res.Drawn),
                    "another cast's metal residue is cut like any material (the cast's own is not): 0.35 less what In3 drew, in two halves");
            }
            // ---- S11 the strip book ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip();
                var a = rig.Add("A", 2, 0);
                var log = rig.Cast('숫', out _); rig.Advance(0.1);
                long t0 = ex.DataOf(log.Cast).Strips[0].AtUs, until = ex.Strips.UntilOf(a);
                rig.Advance(1.0, 10);
                a.MoveTo(At(4, 0)); rig.Advance(0.05);
                var log2 = rig.Cast('갓', out _); rig.Advance(0.3, 6);
                var e2 = ex.DataOf(log2.Cast).Strips;
                Check(r, "S11a", until == t0 + strip45 && e2.Count == 1 && e2[0].DurationUs == strip3 && e2[0].AtUs + strip3 < until && ex.Strips.UntilOf(a) == until && ex.Strips.IsStripped(a, t0 + strip45 - 1) && !ex.Strips.IsStripped(a, t0 + strip45),
                    "a 3 s strip begun inside a 4.5 s one does not shorten it: the body stays stripped until the later end");
                a.LifeRevision++;
                Check(r, "S11b", !ex.Strips.IsStripped(a, t0 + 1), "a new life (revision) is not stripped");
            }
            // ---- S12 abort / no port ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip();
                rig.Add("A", 0.8f, 0);
                var log = rig.Cast('섯', out _); rig.Advance(0.05);
                rig.Coord.AbortAll();
                var late = Plant(rig, 9001, "late", Body.Wood, Vol2Faction.Player, 1.2f, 0.9f);
                Swing(rig); rig.Advance(0.1);
                Check(r, "S12a", log.Status == "Aborted" && late.Splits == 0 && ex.DataOf(log.Cast).Splits.Count == 0, "after an abort the swing handler of 섯 is gone: a swing cuts nothing");
            }
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip();       // no 날붓 port, no wiring in an edit-mode scope
                rig.Add("A", 0.8f, 0);
                var log = rig.Cast('엇', out _); rig.Advance(0.05); Swing(rig); rig.Advance(0.4); Swing(rig);
                var data = ex.DataOf(log.Cast);
                Check(r, "S12b", data.Blades.Count == 2 && data.Blades.All(b => !b.Result.Called && b.Result.Reason == "NoBladePort") && Of(log, "Instant").Count == 1 && Of(log, "Second").Count == 1 && log.Status == "Running", "no 날붓 port: the miss is recorded (NoBladePort), the cast goes on, nothing throws");
            }
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.StripPort = new RecStrip(); var blade = new RecBlade(); ex.Blade = blade;
                rig.Add("A", 4, 0);
                rig.Cast('갓', out _); rig.Advance(0.5, 10);
                Check(r, "S12c", blade.Calls.Count == 0, "the 날붓 is a ㅓ matter: a path cut never asks for it");
            }
        }
    }
}
