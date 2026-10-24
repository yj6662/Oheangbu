using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using static Oheangbu.EditorTools.Vol2.Vol2ShapeFixtureKit;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 7 edit-mode fixture of 돌아온다 (D308-149): the four frames out and back, the return term, the orbit, the enemy shots, and the peaks 앙 엉 옹 웅.
    /// No scene, no Play, a hand-driven clock. Queue:
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2ReturnFixture Run "all"
    ///   P1  P5 강: out and back - A, B hit 0.7 on the way out, 0.35 on the way back (B first), once; C beside the path never; the whole trip inside the round trip
    ///   P2  a wall at 6 m: the path turns back there (B behind the wall never touched)
    ///   P3  앙: return 0.525; ONE of my materials (the first the returning head meets) is moved 2 m ahead - same id, remaining, expiry; a fan footprint is rebuilt as a fan; nothing made
    ///   F1  F5 공: the fan itself travels: a body at 7 m is hit only when the fan has got there; the return only on the way back (from 0.5 s); the far body and the side body never
    ///   R1  R5 궁: the ring grows to 4 m and shrinks back: a body at 4.0 m is hit only after the band reached it; the hole and the far body never
    ///   A1  A5 겅: the circle: three bodies hit once, again a turn later (separate swing, 0.3 s or more), the fourth refused (TargetCap), no return damage, the cast ends with its 6 s
    ///   A2  A5 with real brush swings: the first swing is the instant, a later one the second contact, a third pays nothing
    ///   S1  shots: taken on the way out and on the way back by every frame; a shot outside stays; a shot is taken once; 겅 only stops (no borrowed power)
    ///   E1  엉: three reflections (0.3 each from 6 HP), the fourth only stopped; the borrowed ledger apart from the cast's own total; delivered to the shooter later, lasting damage
    ///   E2  엉 caps: power 12 HP lends 0.6, then 0.4 (1.0 in all), the third finds it spent; HpPerUnit changes the unit; a shot with no power is only stopped; no shooter is booked and noted
    ///   O1  옹: bodies pulled toward the caster on the way back (at most 1.5 s), the airborne one pulled down, the boss not at all; everyone wet
    ///   W1  웅: four enemy patches cleansed in the order reached (no product), the fifth stays, the world's and mine stay; my residue noted wet; the pollution gate asked once
    ///   G1  all 20 glyphs: no contact refused as Expired, the return only where the table has one, every cast Done
    ///   I1  identity: attack ids unique, every paid return has its damage command, no command twice
    /// Expected values are written here from NumbersV3 and the table, never read back from a run.</summary>
    public static class Vol2ReturnFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-return-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-return-fixture: editor busy (play / compile / import)";
            var r = new Report();
            var made = new List<Object>();
            try { using (var errors = new ErrorCounter()) { Cases(r, made); Check(r, "E0", errors.Errors == 0, "no error log during the fixture" + (errors.Errors > 0 ? ": " + errors.First : "")); } }
            catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            finally { foreach (var o in made) if (o != null) Object.DestroyImmediate(o); }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        static bool Eq(decimal a, decimal b) => Math.Abs(a - b) <= 0.000000001m;
        static List<Vol2ContactRecord> Of(Vol2CastLog log, string kind) => log.Contacts.Where(c => c.Kind == kind && c.BudgetAccepted).ToList();
        static List<string> Names(Vol2CastLog log, string kind) => Of(log, kind).Select(c => c.TargetName).ToList();
        static bool SameSet(IEnumerable<string> a, params string[] b) => a.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(b.OrderBy(x => x, StringComparer.Ordinal));
        static long Since(Vol2ContactRecord c, Vol2CastLog log) => c.NowUs - log.StartUs;

        sealed class FakeShot : IVol2Projectile
        {
            public Vector3 Pos, Vel; public bool Flying = true; public long TakenBy; public float Power; public EnemyVitals ShooterV; public string Name;
            public object Identity => this;
            public bool InFlight => Flying;
            public Vector3 Position => Pos;
            public Vector3 Velocity => Vel;
            public bool Intercept(long owner) { if (!Flying) return false; Flying = false; TakenBy = owner; return true; }
            public float PowerHp => Power;
            public EnemyVitals Shooter => ShooterV;
        }

        static Vol2ReturnExecutor Exec(Rig rig)
        {
            rig.Coord.Verbs.TryGet(Verb.Return, out var e);
            var ex = e as Vol2ReturnExecutor;
            if (ex != null) { ex.Hub.ScanScene = false; ex.Hub.ScanPhysics = false; }
            return ex;
        }

        static FakeShot Shot(Vol2ReturnExecutor ex, string name, float ahead, float side, float power = 12f, EnemyVitals shooter = null)
        {
            var s = new FakeShot { Name = name, Pos = At(ahead, side) + Vector3.up * 1.1f, Power = power, ShooterV = shooter };
            ex.Hub.Register(s);
            return s;
        }

        static void Swing(Rig rig)
        {
            long id = rig.Services.Ids.NextSwing().Value;
            Vector3 sl = new Vector3(0, 1.1f, 0), tp = sl + Vector3.forward * 1.6f;
            for (int k = 0; k < 3; k++) rig.Runtime.Swings.Raise(new Vol2SwingEvent(id, sl, tp, rig.Services.Clock.NowUs, k));
        }

        sealed class FakeField : IVol2FieldPort
        {
            public readonly List<string> Asked = new List<string>();
            public string OnPlatform(CastId cast, Vector3 centre, float radius, long lifeUs) => "n/a";
            public string OnVineBurn(CastId cast, Vol2Material vine) => "n/a";
        }

        static Vol2Material Plant(Rig rig, long cast, string tag, Body body, long faction, float ahead, float side, float radius = 0.6f)
            => rig.Coord.Materials.Create(new CastId(cast), '가', body, Vol2MaterialKind.Residue, 0.9m, RuleTypesV3.Microseconds(rig.Numbers.Get("timing.g.life")), Vol2Footprint.Disc(At(ahead, side), radius),
                faction == Vol2Faction.Enemy ? 2 : 1, faction, true, rig.Services.Clock.NowUs, 0, 0, tag);

        static void Cases(Report r, List<Object> made)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var n = book.Numbers;
            long roundTrip = RuleTypesV3.Microseconds(n.Get("timing.return.roundTrip")), secondDelay = RuleTypesV3.Microseconds(n.Get("timing.eo.secondDelay"));
            decimal instant = n.Get("action.instant"), ret = instant * n.Get("action.return.multiplier"), retPeak = instant * n.Get("action.return.peakMultiplier"), second = n.Get("action.eo.second");
            decimal Hof(Body b) => n.Get("body." + RuleTypesV3.BodyKey(b) + ".instant");
            Check(r, "F1", Eq(ret, 0.35m) && Eq(retPeak, 0.525m) && roundTrip == 1200000 && Eq(second, 0.3m), "table numbers: return 0.35, 앙 0.525, round trip 1.2 s, second 0.3");

            // ---- P1 path 강 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig);
                Check(r, "P0", ex != null, "the Return executor is registered in the default verb registry");
                if (ex == null) return;
                rig.Add("A", 4, 0); rig.Add("B", 8, 0); rig.Add("C", 4, 3.0f);
                var log = rig.Cast('강', out var dec); rig.Advance(1.0, 50);
                var ins = Of(log, "Instant"); var rets = Of(log, "Return"); var rep = ex.ReportOf(log.Cast);
                Check(r, "P1a", SameSet(Names(log, "Instant"), "A", "B") && ins.All(c => c.Amount == 0.7m) && ins.All(c => Since(c, log) < 400000) && !log.Contacts.Any(c => c.TargetName == "C"), "강: A (4 m) and B (8 m) are hit on the way out, 0.7 each, inside the first 0.4 s; C (3 m beside the path) is never touched");
                Check(r, "P1b", rets.Select(c => c.TargetName).SequenceEqual(new[] { "B", "A" }) && rets.All(c => Eq(c.Amount, 0.35m) && Eq(c.Units, Hof(Body.Wood) * 0.35m)) && rets.All(c => Since(c, log) > 330000 && Since(c, log) < 700000), "the way back: the returning head meets B first, then A: 0.35 each (0.7 x 0.5), between 0.33 s and 0.7 s (12 m at 36 m/s out and back)");
                Check(r, "P1c", rets.All(c => c.BudgetReason == "Returned") && ins.All(c => c.BudgetReason == "FirstContact") && log.Contacts.Count == 4 && Eq(log.Budget.PaidPerTarget, 2.1m) && Eq(log.Budget.PaidShared, 0m), "the rules paid it: FirstContact x2, Returned x2, per-target total 2 x (0.7 + 0.35) = 2.1, nothing shared");
                Check(r, "P1d", rig.Status.Requests.Count == 2 && rig.Status.Requests.All(q => q.Kind == Vol2BodyActionKind.Bind) && log.Contacts.Count(c => c.BodyEffect.Length > 0) == 2, "the body's action (bind) happens once per body, at the first touch; the return does not bind again");
                Check(r, "P1e", log.Finished && log.Status == "Done" && rep != null && rep.TripEndUs - log.StartUs <= roundTrip && Math.Abs(rep.TripEndUs - log.StartUs - 666667) < 2 && rig.Damage.Requests.Count == 4 && rig.Damage.Requests.Count(q => q.Structure == 0.35m) == 2, "the cast is Done; the trip ends 0.667 s after the cast (inside timing.return.roundTrip 1.2 s); 4 damage requests, two of them R = 0.35");
                var first = rets.Count > 0 ? rets[0] : null;
                Check(r, "P1f", first != null && first.NowUs > ins.Max(c => c.NowUs) && rets.All(c => ins.Any(i => i.TargetId == c.TargetId && i.NowUs < c.NowUs)), "a return always comes after the same body's first touch");
            }
            // ---- P2 wall ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig);
                rig.Wall.At = 6f;
                rig.Add("A", 4, 0); rig.Add("B", 8, 0);
                var log = rig.Cast('낭', out _); rig.Advance(1.0, 50);
                var rep = ex.ReportOf(log.Cast);
                Check(r, "P2", Names(log, "Instant").SequenceEqual(new[] { "A" }) && Names(log, "Return").SequenceEqual(new[] { "A" }) && !log.Contacts.Any(c => c.TargetName == "B") && rep.TripEndUs - log.StartUs < 340000,
                    "a wall at 6 m: the path ends there and comes back from there (the trip is 12 m long, 0.33 s); B behind the wall is never touched");
            }
            // ---- P3 앙 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig);
                rig.Add("A", 4, 0); rig.Add("B", 8, 0);
                var m1 = Plant(rig, 9001, "m1", Body.Wood, Vol2Faction.Player, 6f, 0f);
                var m2 = Plant(rig, 9002, "m2", Body.Wood, Vol2Faction.Player, 3f, 0f);
                var m3 = Plant(rig, 9003, "m3", Body.Wood, Vol2Faction.Player, 6f, 3f);
                var m4 = Plant(rig, 9004, "m4", Body.Wood, Vol2Faction.Enemy, 9f, 0f);
                var m5 = Plant(rig, 9005, "m5", Body.Wood, Vol2Faction.Player, 14f, 0f);
                long created = rig.Coord.Materials.TotalCreated;
                long id1 = m1.Id.Value, expiry1 = m1.ExpiresUs;
                var log = rig.Cast('앙', out _); rig.Advance(1.0, 50);
                var rets = Of(log, "Return"); var rep = ex.ReportOf(log.Cast);
                Vector3 dest = new Vector3(0f, 0f, (float)n.Get("peak.앙.destinationDistance"));
                Check(r, "P3a", rets.Select(c => c.TargetName).SequenceEqual(new[] { "B", "A" }) && rets.All(c => Eq(c.Amount, 0.525m) && c.BudgetReason == "Returned") && Eq(Hof(Body.Water) * 0.525m, rets[0].Units), "앙: the return is 0.525 (0.7 x 0.75), not 0.35");
                Check(r, "P3b", rep.CarriedId == id1 && m1.Id.Value == id1 && Vector3.Distance(m1.Footprint.Centre, dest) < 0.001f && m1.Footprint.Kind == Vol2FootprintKind.Disc && m1.Footprint.Radius == 0.6f, "앙: of my materials on the path (m1 at 6 m, m2 at 3 m) the returning head meets the farthest first: m1 is carried to 2 m ahead of the caster (a disc stays a disc)");
                Check(r, "P3c", Eq(m1.Remaining, 0.9m) && m1.Initial == 0.9m && m1.Drawn == 0m && m1.ExpiresUs == expiry1 && m1.Alive && !m1.LifeExtended && m1.Splits == 0, "it keeps its amount (0.9, nothing drawn), its expiry and its lineage: moved, not remade");
                Check(r, "P3d", Vector3.Distance(m2.Footprint.Centre, At(3f, 0f)) < 0.001f && Vector3.Distance(m3.Footprint.Centre, At(6f, 3f)) < 0.001f && Vector3.Distance(m4.Footprint.Centre, At(9f, 0f)) < 0.001f && Vector3.Distance(m5.Footprint.Centre, At(14f, 0f)) < 0.001f,
                    "only ONE is carried (peak.앙.carryCount): m2, the one beside the path (m3), the enemy's (m4) and the one past the path's end (m5) stay where they were");
                Check(r, "P3e", rig.Coord.Materials.TotalCreated == created && log.Materials.Count == 0 && rig.Coord.Materials.Audit().Count == 0 && log.Notes.Any(x => x.StartsWith("carried", StringComparison.Ordinal)), "nothing was made: the registry created no material, the audit is clean");
                // a fan footprint on the path is rebuilt as a fan at the new place
                var rigF = Rig.Create(book); var exF = Exec(rigF);
                rigF.Add("A", 4, 0);
                var spec = Vol2ShapeSpec.Create(Frame.Fan, n, new Vector3(0, 0, 8f), Vector3.forward, new Vol2ShapeTuning());
                var fanMat = rigF.Coord.Materials.Create(new CastId(9006), '가', Body.Wood, Vol2MaterialKind.Residue, 0.9m, 10000000, Vol2Footprint.Fan(spec, 1.2f, 0, 5000000), 1, Vol2Faction.Player, true, rigF.Services.Clock.NowUs, 0, 0, "fanmat");
                var near = Plant(rigF, 9007, "near", Body.Wood, Vol2Faction.Player, 3f, 0f);
                long expiryFan = fanMat.ExpiresUs;
                var logF = rigF.Cast('앙', out _); rigF.Advance(1.0, 50);
                var repF = exF.ReportOf(logF.Cast);
                Check(r, "P3f", repF.CarriedId == fanMat.Id.Value && fanMat.Footprint.Kind == Vol2FootprintKind.Fan && Vector3.Distance(fanMat.Footprint.Origin, dest) < 0.001f && fanMat.Footprint.GrowLinear == 1.2f && fanMat.Footprint.GrowEndUs == 5000000 && Vector3.Distance(fanMat.Footprint.Forward, Vector3.forward) < 0.001f && fanMat.ExpiresUs == expiryFan && Eq(fanMat.Remaining, 0.9m)
                    && Vector3.Distance(near.Footprint.Centre, At(3f, 0f)) < 0.001f, "a FAN residue across the path (apex at 8 m, along the axis) is met first (its far end reaches 12 m): it is rebuilt at the new place as a fan - same direction, same growth, same expiry, same amount; the disc at 3 m stays");
                // no material on the path: nothing is carried
                var rig2 = Rig.Create(book); var ex2 = Exec(rig2);
                rig2.Add("A", 4, 0);
                Plant(rig2, 9101, "off", Body.Wood, Vol2Faction.Player, 6f, 3f);
                var log2 = rig2.Cast('앙', out _); rig2.Advance(1.0, 50);
                Check(r, "P3g", ex2.ReportOf(log2.Cast).CarriedId == 0 && Of(log2, "Return").Count == 1, "no material of mine on the path: nothing is carried, the return still lands");
            }
            // ---- F1 fan 공 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig);
                rig.Add("In3", 3, 0); rig.Add("In4.8", 4.8f, -0.3f); rig.Add("Mid7", 7, 0); rig.Add("Out10.8", 10.8f, 0); rig.Add("Side", 5, 4.5f);
                var log = rig.Cast('공', out _); rig.Advance(1.3, 65);
                var ins = Of(log, "Instant"); var rets = Of(log, "Return"); var rep = ex.ReportOf(log.Cast);
                var mid = ins.FirstOrDefault(c => c.TargetName == "Mid7");
                Check(r, "F1a", SameSet(Names(log, "Instant"), "In3", "In4.8", "Mid7") && ins.All(c => c.Amount == 0.7m) && !log.Contacts.Any(c => c.TargetName == "Out10.8" || c.TargetName == "Side"), "공: In3, In4.8 and Mid7 are hit 0.7; the body past the fan's farthest reach (10.8 m; 5 m travel + 5 m depth + body) and the one at the side are never touched");
                Check(r, "F1b", mid != null && Since(mid, log) >= 100000 && Since(mid, log) <= 300000 && ins.Where(c => c.TargetName != "Mid7").All(c => Since(c, log) <= 40000), "the fan ITSELF travels: In3 and In4.8 are hit at once, Mid7 (7 m) only when the fan has got there (about 0.16 s)");
                Check(r, "F1c", SameSet(Names(log, "Return"), "In3", "In4.8", "Mid7") && rets.All(c => Eq(c.Amount, 0.35m)) && rets.All(c => Since(c, log) >= 500000 && Since(c, log) < roundTrip) && Eq(log.Budget.PaidPerTarget, 3.15m), "the return only on the way back (from 0.5 s), 0.35 each, inside the round trip; 3 x 1.05 = 3.15");
                Check(r, "F1d", log.Finished && log.Status == "Done" && rep.TripEndUs - log.StartUs == 1000000 && rep.LegUs == 500000 && rig.Status.Requests.Count == 3, "the cast is Done after 1.0 s (0.5 s each way, inside timing.return.roundTrip); bind once per body");
                var again = Rig.Create(book); var exAgain = Exec(again);
                again.Add("Late", 6f, 0);
                var logAgain = again.Cast('공', out _); again.Advance(0.05, 5);
                var late = logAgain.Contacts.Where(c => c.TargetName == "Late").ToList();
                Check(r, "F1e", late.Count == 0, "a body at 6 m is not touched at the first instant (the fan reaches 5 m before it travels)");
                again.Advance(1.3, 65);
                Check(r, "F1f", Of(logAgain, "Instant").Count == 1 && Of(logAgain, "Return").Count == 1, "...but it is hit when the fan gets there and again on the way back");
                // a body first met on the way back is a normal first touch, with no return of its own
                var rig3 = Rig.Create(book); var ex3 = Exec(rig3);
                var runner = rig3.Add("Runner", 8f, 4.5f);
                var log3 = rig3.Cast('공', out _); rig3.Advance(0.6, 30);
                runner.MoveTo(At(3.5f, 0f)); rig3.Advance(0.2, 10);
                Check(r, "F1g", Names(log3, "Instant").SequenceEqual(new[] { "Runner" }) && Of(log3, "Return").Count == 0 && Since(Of(log3, "Instant")[0], log3) > 500000, "a body that steps in only on the way back gets a normal first touch (0.7) and no return (the return is the SECOND pass)");
            }
            // ---- R1 ring 궁 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig);
                rig.Add("R2", 2, 0); rig.Add("R3.4", 3.4f, 0.5f); rig.Add("R4.0", 4.0f, 0f); rig.Add("Hole", 0.4f, 0.2f); rig.Add("Far4.9", 4.9f, 0f); rig.Add("Far6", 6f, 0f);
                var log = rig.Cast('궁', out _); rig.Advance(1.3, 65);
                var ins = Of(log, "Instant"); var rets = Of(log, "Return"); var rep = ex.ReportOf(log.Cast);
                var r40 = ins.FirstOrDefault(c => c.TargetName == "R4.0"); var r2 = ins.FirstOrDefault(c => c.TargetName == "R2");
                Check(r, "R1a", SameSet(Names(log, "Instant"), "R2", "R3.4", "R4.0") && !log.Contacts.Any(c => c.TargetName == "Hole" || c.TargetName == "Far4.9" || c.TargetName == "Far6"), "궁: the band is hit by R2, R3.4 and R4.0; the hole, the body just past the band's farthest reach (4.9 m) and the far one never");
                Check(r, "R1b", r2 != null && Since(r2, log) <= 40000 && r40 != null && Since(r40, log) >= 300000 && Since(r40, log) <= 500000, "the ring GROWS: R2 is on the band at once, R4.0 (4 m) only once the band has spread to it (about 0.33 s)");
                Check(r, "R1c", SameSet(Names(log, "Return"), "R2", "R3.4", "R4.0") && rets.All(c => Eq(c.Amount, 0.35m)) && rets.All(c => Since(c, log) >= 500000 && Since(c, log) < roundTrip), "all three again on the way back (from 0.5 s), 0.35 each, inside the round trip");
                Check(r, "R1d", log.Finished && log.Status == "Done" && rep.TripEndUs - log.StartUs == 1000000 && rig.Status.Requests.Count == 3, "the cast is Done after 1.0 s (the ring is back at 2 m); bind once per body");
            }
            // ---- A1 orbit 겅 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig);
                rig.Add("A", 1.2f, 0); rig.Add("B", 0, 1.2f); rig.Add("C", -1.2f, 0); rig.Add("D", 0, -1.2f); rig.Add("E", 4, 0);
                var log = rig.Cast('겅', out _); rig.Advance(0.1, 2);
                bool early = Names(log, "Instant").SequenceEqual(new[] { "A" });
                rig.Advance(6.8, 136);
                var ins = Of(log, "Instant"); var sec = Of(log, "Second"); var rep = ex.ReportOf(log.Cast);
                Check(r, "A1a", early && SameSet(Names(log, "Instant"), "A", "B", "C") && SameSet(Names(log, "Second"), "A", "B", "C") && ins.All(c => c.Amount == 0.7m) && sec.All(c => Eq(c.Amount, 0.3m)), "겅: the circle turns clockwise from straight ahead: A is hit at once, B (right) and C (behind) as it comes round (0.7), each again a turn later (0.3)");
                var d = log.Contacts.Where(c => c.TargetName == "D").ToList();
                Check(r, "A1b", d.Count == 1 && !d[0].BudgetAccepted && d[0].BudgetReason == "TargetCap" && !log.Contacts.Any(c => c.TargetName == "E") && log.Budget.State.Targets.Count == 3, "three bodies in all: D (left, the fourth) is refused with TargetCap, once; E (4 m) is out of reach");
                Check(r, "A1c", sec.All(c => ins.Any(i => i.TargetId == c.TargetId && c.NowUs - i.NowUs >= secondDelay && c.SwingId != i.SwingId && c.SwingId > 0)) && ins.Select(c => c.SwingId).Distinct().Count() == 1 && sec.Select(c => c.SwingId).Distinct().Count() == 1, "the second contact is at least 0.3 s after the first and on another swing (another turn: one SwingId per revolution)");
                Check(r, "A1d", !log.Contacts.Any(c => c.Kind == "Return") && Eq(log.Budget.PaidPerTarget, 3.0m) && rep.OrbitRevolutions >= 2, "no return damage (action.eo.return = 0): the cast paid 3 x (0.7 + 0.3) = 3.0 and nothing else");
                Check(r, "A1e", log.Finished && log.Status == "Done" && log.EndUs - log.StartUs >= 6000000 && log.EndUs - log.StartUs < 6200000, "the circle lives its 6 s, then the cast is Done (" + (log.EndUs - log.StartUs) + " us)");
                Check(r, "A1f", ins.Concat(sec).All(c => c.NowUs - log.StartUs < 6000000) && rig.Status.Requests.Count == 3, "every contact is inside the 6 s life; bind once per body");
            }
            // ---- A2 swings ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig);
                rig.Add("W", 0.5f, 0.3f);
                var log = rig.Cast('겅', out _); rig.Advance(0.05); Swing(rig);
                bool one = Names(log, "Instant").SequenceEqual(new[] { "W" });
                rig.Advance(0.4, 4); Swing(rig);
                bool two = Names(log, "Second").SequenceEqual(new[] { "W" });
                rig.Advance(0.3, 3); Swing(rig);
                Check(r, "A2", one && two && Of(log, "Second").Count == 1 && log.Contacts.Count(c => c.TargetName == "W" && c.BudgetAccepted) == 2 && log.SwingReports == 9, "a real brush swing is a contact of the same budget: the first is the instant, the swing 0.4 s later the second contact, the third pays nothing (a body the circle itself does not reach)");
            }
            // ---- S1 shots ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig);
                var s1 = Shot(ex, "S1", 10f, 0f); var d1 = Shot(ex, "D1", 6f, 3.5f);
                rig.Add("A", 4, 0);
                var log = rig.Cast('강', out _); rig.Advance(0.5, 25);
                var rep = ex.ReportOf(log.Cast);
                Check(r, "S1a", !s1.Flying && s1.TakenBy == log.Cast.Value && d1.Flying && rep.Intercepts.Count == 1 && rep.Intercepts[0].Leg == "out" && rep.Intercepts[0].Key > 0 && !rep.Intercepts[0].Reflected, "강: the shot on the axis (10 m) is taken by the cast on the way out; the one 3.5 m beside it is not");
                // a shot that appears after the head has passed is taken on the way back
                var rigB = Rig.Create(book); var exB = Exec(rigB); rigB.Add("A", 4, 0);
                var logB = rigB.Cast('강', out _); rigB.Advance(0.45, 9);
                var late = Shot(exB, "Late", 3f, 0f); rigB.Advance(0.6, 30);
                var repB = exB.ReportOf(logB.Cast);
                Check(r, "S1c", !late.Flying && repB.Intercepts.Count == 1 && repB.Intercepts[0].Leg == "back", "a shot that is in the air only after the head has passed (3 m, 0.45 s) is taken by the head coming back");
                // fan and ring, out and back
                var rigF = Rig.Create(book); var exF = Exec(rigF);
                var f1 = Shot(exF, "F1", 6f, 0f); var fOut = Shot(exF, "FOut", 6f, 5f);
                var logF = rigF.Cast('공', out _); rigF.Advance(0.52, 26);
                var f2 = Shot(exF, "F2", 4f, 0f); rigF.Advance(1.0, 50);
                var repF = exF.ReportOf(logF.Cast);
                Check(r, "S1d", !f1.Flying && !f2.Flying && fOut.Flying && repF.Intercepts.Select(i => i.Leg).SequenceEqual(new[] { "out", "back" }), "공: the shot on the axis is taken on the way out, the one that comes later (4 m) on the way back; the one 5 m to the side stays");
                var rigR = Rig.Create(book); var exR = Exec(rigR);
                var r1 = Shot(exR, "R1", 3f, -0.6f); var rFar = Shot(exR, "RFar", 6f, 2f);
                var logR = rigR.Cast('궁', out _); rigR.Advance(0.2, 10);
                var r2 = Shot(exR, "R2", 3.1f, 0.5f); rigR.Advance(1.2, 60);
                var repR = exR.ReportOf(logR.Cast);
                Check(r, "S1e", !r1.Flying && !r2.Flying && rFar.Flying && repR.Intercepts.Count == 2 && repR.Intercepts[0].Leg == "out", "궁: shots on the band are taken as it passes (the second appears after 0.2 s); the one at 6 m is never reached");
                // orbit, 겅: only stopped
                var rigO = Rig.Create(book); var exO = Exec(rigO);
                float c45 = 1.6f * 0.70710678f;
                var o1 = Shot(exO, "O1", -c45, c45); var o2 = Shot(exO, "O2", -c45, -c45); var oFar = Shot(exO, "OFar", 4f, 3f); var noPower = Shot(exO, "O0", 1.6f, 0f, 0f);
                var logO = rigO.Cast('겅', out _); rigO.Advance(1.2, 60);
                var repO = exO.ReportOf(logO.Cast);
                Check(r, "S1f", !o1.Flying && !o2.Flying && !noPower.Flying && oFar.Flying && repO.Intercepts.Count == 3 && repO.Intercepts.All(i => i.Leg == "orbit" && !i.Reflected) && repO.Reflections == 0 && repO.BorrowedPaid == 0m && !logO.Contacts.Any(c => c.Kind == "Reflect"), "겅: the circle stops the shots it meets and only stops them: no borrowed power, no reflect record");
                Check(r, "S1g", exO.ReportOf(logO.Cast).Intercepts.Select(i => i.Key).Distinct().Count() == 3, "each shot has its own positive id");
            }
            // ---- E1 / E2 엉 ----
            {
                float c45 = 1.6f * 0.70710678f;
                var rig = Rig.Create(book); var ex = Exec(rig);
                var sh = Vol2DefenceFixtureSupport.NewEnemy(made, "Sh", 60f); sh.transform.position = At(9f, 4f);
                rig.Coord.Registry.AddExtra(sh);
                var a = new FakeTarget(100, "A", 1.2f, 0); var b = new FakeTarget(101, "B", 0, 1.2f); var c = new FakeTarget(102, "C", -1.2f, 0); var dd = new FakeTarget(103, "D", 0, -1.2f);
                rig.Targets.AddRange(new[] { a, b, c, dd });
                var s1 = Shot(ex, "S1", c45, c45, 6f, sh); var s2 = Shot(ex, "S2", -c45, c45, 6f, sh); var s3 = Shot(ex, "S3", -c45, -c45, 6f, sh); var s4 = Shot(ex, "S4", c45, -c45, 6f, sh);
                var log = rig.Cast('엉', out _); rig.Advance(0.3, 15);
                int early = rig.Damage.Requests.Count(q => q.Lasting);
                rig.Advance(6.7, 134);
                var rep = ex.ReportOf(log.Cast); long shId = rig.Coord.Registry.IdOf(sh);
                var refl = Of(log, "Reflect");
                Check(r, "E1a", rep.Intercepts.Count == 4 && rep.Intercepts.Count(i => i.Reflected) == 3 && rep.Intercepts.Count(i => !i.Reflected && i.Reason == "ReflectionCount") == 1 && !s1.Flying && !s2.Flying && !s3.Flying && !s4.Flying, "엉: four shots taken; the first three are turned back, the fourth is only stopped (ReflectionCount: the cap is 3)");
                Check(r, "E1b", refl.Count == 3 && refl.All(x => Eq(x.Amount, 0.3m) && x.BudgetReason == "Reflected" && x.TargetId == shId) && Eq(rep.BorrowedPaid, 0.9m) && rep.Reflections == 3 && Eq(log.Budget.State.BorrowedPaid, 0.9m),
                    "each lends 0.5 x (6 HP / 10 HP per unit) = 0.3 of the shot's own power (before any multiplier); 0.9 in all, at most the 1.0 total; the target is the shooter");
                var lasting = rig.Damage.Requests.Where(q => q.Lasting).ToList();
                Check(r, "E1c", lasting.Count == 3 && lasting.All(q => q.Units == 0.3m && q.Target != null && q.Target.Id == shId && q.StatusFactor == 1m) && early == 0 && refl.All(x => x.DamageRequested && x.DamageApplied && x.DamageNote.Contains("HP per unit")),
                    "the borrowed damage goes to the shooter as a lasting-damage request of 0.3 units (no C, no K), delivered when the shot gets there (not in the first 0.3 s); the conversion is in the record");
                Check(r, "E1d", Names(log, "Instant").Count == 3 && Eq(log.Budget.PaidPerTarget, 3.0m) && Eq(log.Budget.PaidPerTarget + log.Budget.PaidShared, 3.0m) && log.Budget.State.Targets.Count == 3, "the borrowed power is a separate ledger: the cast's own total is still 3 x (0.7 + 0.3) = 3.0; three bodies, D refused");
                Check(r, "E1e", rig.Status.Requests.Count == 3 && rig.Status.Requests.All(q => q.Kind == Vol2BodyActionKind.Wet), "the bodies the circle touches get wet (water's action), once each");
                // E2 caps
                var rig2 = Rig.Create(book); var ex2 = Exec(rig2);
                var sh2 = Vol2DefenceFixtureSupport.NewEnemy(made, "Sh2", 60f); sh2.transform.position = At(9f, 4f); rig2.Coord.Registry.AddExtra(sh2);
                var t1 = Shot(ex2, "T1", c45, c45, 12f, sh2); var t2 = Shot(ex2, "T2", -c45, c45, 12f, sh2); var t3 = Shot(ex2, "T3", -c45, -c45, 12f, sh2);
                var log2 = rig2.Cast('엉', out _); rig2.Advance(3.0, 60);
                var rep2 = ex2.ReportOf(log2.Cast); var ok2 = Of(log2, "Reflect");
                Check(r, "E2a", ok2.Count == 2 && Eq(ok2[0].Amount, 0.6m) && Eq(ok2[1].Amount, 0.4m) && Eq(rep2.BorrowedPaid, 1.0m) && rep2.Intercepts.Count == 3 && rep2.Intercepts[2].Reason == "BorrowedExhausted" && !t3.Flying,
                    "power 12 HP lends 0.5 x 1.2 = 0.6, then only the 0.4 that is left (1.0 in all), the third finds the total spent (BorrowedExhausted) and is only stopped");
                var rig3 = Rig.Create(book); var ex3 = Exec(rig3); ex3.Tuning.HpPerUnit = 20f;
                var sh3 = Vol2DefenceFixtureSupport.NewEnemy(made, "Sh3", 60f); sh3.transform.position = At(9f, 4f); rig3.Coord.Registry.AddExtra(sh3);
                Shot(ex3, "U1", c45, c45, 12f, sh3);
                var log3 = rig3.Cast('엉', out _); rig3.Advance(2.0, 40);
                Check(r, "E2b", Of(log3, "Reflect").Count == 1 && Eq(Of(log3, "Reflect")[0].Amount, 0.3m) && Of(log3, "Reflect")[0].DamageNote.Contains("20 HP per unit"), "HpPerUnit is the one explicit conversion: at 20 HP per unit a 12 HP shot is 0.6 units, lends 0.3 (and the record says 20)");
                var rig4 = Rig.Create(book); var ex4 = Exec(rig4);
                var plain = Shot(ex4, "Plain", c45, c45, 0f, null); var nobody = Shot(ex4, "Nobody", -c45, c45, 6f, null);
                var log4 = rig4.Cast('엉', out _); rig4.Advance(2.0, 40);
                var rep4 = ex4.ReportOf(log4.Cast); var rec4 = log4.Contacts.Where(x => x.Kind == "Reflect").ToList();
                Check(r, "E2c", !plain.Flying && !nobody.Flying && rep4.Intercepts.Count == 2 && rep4.Intercepts.Count(i => i.Reflected) == 1 && rec4.Count == 1 && rec4[0].DamageNote.StartsWith("NoShooter", StringComparison.Ordinal) && !rig4.Damage.Requests.Any(q => q.Lasting), "a shot with no known power is only stopped (nothing booked); a shot with power but no shooter is booked and noted NoShooter, nothing to hit");
            }
            // ---- O1 옹 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); ex.Tuning.GroundHeight = t => 0f;
                rig.Coord.BossTest = t => t.Name == "Boss";
                rig.Add("In3", 3, 0); rig.Add("In4.8", 4.8f, -0.3f); rig.Add("Mid7", 7, 0);
                var air = rig.Add("Air", 3, 0.6f); air.Position = air.Previous = new Vector3(0.6f, 2.5f, 3f);
                var boss = rig.Add("Boss", 4, -1.2f);
                var log = rig.Cast('옹', out _); rig.Advance(1.3, 65);
                var rep = ex.ReportOf(log.Cast); long max = RuleTypesV3.Microseconds(n.Get("combat.bind.maxDuration"));
                var pushes = rig.Status.Requests.Where(q => q.Kind == Vol2BodyActionKind.Push).ToList();
                var wets = rig.Status.Requests.Where(q => q.Kind == Vol2BodyActionKind.Wet).ToList();
                Check(r, "O1a", SameSet(Names(log, "Instant"), "In3", "In4.8", "Mid7", "Air", "Boss") && SameSet(Names(log, "Return"), "In3", "In4.8", "Mid7", "Air", "Boss") && Of(log, "Return").All(c => Eq(c.Amount, 0.35m)) && wets.Count == 5, "옹: all five (the airborne one and the boss too) are hit and hit again on the way back (0.35 each, 1.05 in all), and everyone is wet");
                bool toward = pushes.All(q => Vector3.Dot(q.Direction, (q.Target.Position - Vector3.zero).normalized) < 0f || q.Target.Name == "Air");
                Check(r, "O1b", pushes.Count == 4 && pushes.All(q => q.DurationUs > 0 && q.DurationUs <= max && q.DurationUs <= 500000 && q.NowUs - log.StartUs >= 500000) && !pushes.Any(q => q.Target.Name == "Boss") && toward,
                    "four bodies are pulled, at most 1.5 s (here the 0.5 s that is left of the trip), starting when the fan turns back; the direction points at the caster; the boss is not pulled at all");
                var pa = pushes.FirstOrDefault(q => q.Target.Name == "Air");
                Vector3 expectedAir = new Vector3(0.6f, 2.5f, 3f);
                Vector3 flatAir = new Vector3(0.6f, 0f, 3f).normalized * 1.5f;
                Vector3 destAir = new Vector3(flatAir.x, 0f, flatAir.z);
                Check(r, "O1c", pa.Target != null && pa.Direction.y < -0.1f && Mathf.Abs(pa.Meters - Vector3.Distance(expectedAir, destAir)) < 0.01f && rep.Drags.Any(d => d.Name == "Air" && d.Airborne), "the airborne body is pulled DOWN as well as toward the caster (direction.y < 0, to the ground at 1.5 m)");
                var pin = pushes.FirstOrDefault(q => q.Target.Name == "In3");
                Check(r, "O1d", pin.Target != null && Mathf.Abs(pin.Meters - 1.5f) < 0.01f && Mathf.Abs(pushes.First(q => q.Target.Name == "Mid7").Meters - 5.5f) < 0.01f && rep.Drags.Any(d => d.Name == "Boss" && d.Boss && d.Sink == "boss exempt"), "In3 (3 m) is pulled the 1.5 m to 1.5 m from the caster, Mid7 (7 m) 5.5 m; the boss exemption is recorded");
            }
            // ---- W1 웅 ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig); var field = new FakeField(); rig.Coord.Field = field;
                rig.Add("R2", 2, 0); rig.Add("R3.4", 3.4f, 0.5f);
                Vector3 P(float rr, float deg) { float aa = deg * Mathf.Deg2Rad; return new Vector3(rr * Mathf.Cos(aa), 0f, rr * Mathf.Sin(aa)); }
                var radii = new[] { 2.0f, 3.0f, 3.6f, 4.0f, 4.6f }; var angles = new[] { 0f, 72f, 144f, 216f, 288f };
                var e = new Vol2Material[5];
                for (int i = 0; i < 5; i++) e[i] = Plant(rig, 9200 + i, "e" + (i + 1), Body.Water, Vol2Faction.Enemy, P(radii[i], angles[i]).x, P(radii[i], angles[i]).z);
                var far = Plant(rig, 9210, "far", Body.Water, Vol2Faction.Enemy, 7f, 0f);
                var vine = Plant(rig, 9211, "vine", Body.Wood, Vol2Faction.World, P(3.0f, 36f).x, P(3.0f, 36f).z);
                var mine = Plant(rig, 9212, "mine", Body.Fire, Vol2Faction.Player, P(2.5f, 108f).x, P(2.5f, 108f).z);
                long created = rig.Coord.Materials.TotalCreated;
                var log = rig.Cast('웅', out _); rig.Advance(1.3, 65);
                var rep = ex.ReportOf(log.Cast);
                Check(r, "W1a", e.Take(4).All(m => m.End == Vol2MaterialEnd.Cleansed) && e[4].Alive && far.Alive && vine.Alive && mine.Alive && rep.Cleansed.Count == 4, "웅: four enemy patches end Cleansed (peak.웅.cleanseCount); the fifth (reached last), the far one, the world's vine and my residue stay");
                Check(r, "W1b", rep.Cleansed.SequenceEqual(e.Take(4).Select(m => m.Id.Value)), "in the order the band reached them (2.0, 3.0, 3.6, 4.0 m)");
                Check(r, "W1c", rig.Coord.Materials.TotalCreated == created && log.Materials.Count == 0 && rig.Coord.Materials.Audit().Count == 0 && e.Take(4).All(m => m.End == Vol2MaterialEnd.Cleansed && m.Drawn == 0m && m.Remaining == m.Initial), "removing created nothing: no product, no child (the registry made no material)");
                Check(r, "W1d", rep.WetRequests.SequenceEqual(new[] { mine.Id.Value }) && log.Notes.Any(x => x.Contains("wet request")), "my residue in the band is noted as wet (no wet flag on a material yet); the world's vine is not mine");
                Check(r, "W1e", log.FieldNotes.Count == 1 && log.FieldNotes[0].Contains("pollution") && log.FieldNotes[0].Contains("recorded only"), "the non-combat pollution gate is asked for, once (no cleanse API on the field port yet: recorded) - " + (log.FieldNotes.Count > 0 ? log.FieldNotes[0] : ""));
                Check(r, "W1f", SameSet(Names(log, "Instant"), "R2", "R3.4") && SameSet(Names(log, "Return"), "R2", "R3.4") && Of(log, "Return").All(c => Eq(c.Amount, 0.35m)), "the bodies in the band are hit out and back as for any ring (0.7 then 0.35)");
            }
            // ---- G1 all twenty ----
            {
                int total = 0, bad = 0; var problems = new List<string>();
                var letters = "강겅공궁낭넝농눙망멍몽뭉상성송숭앙엉옹웅";
                foreach (char letter in letters)
                {
                    total++;
                    var rig = Rig.Create(book); var ex = Exec(rig);
                    if (ex == null) { bad++; problems.Add(letter + " no executor"); continue; }
                    GlyphRulesV3.TryDecode(letter, out var g);
                    float ahead = g.Frame == Frame.Path ? 4f : g.Frame == Frame.Fan ? 3f : g.Frame == Frame.Ring ? 2f : 1.2f;
                    rig.Add("A", ahead, 0f);
                    var log = rig.Cast(letter, out var dec);
                    rig.Advance(g.Frame == Frame.Attachment ? 7.0 : 3.2, g.Frame == Frame.Attachment ? 140 : 160);
                    var rep = ex.ReportOf(log.Cast);
                    bool ok = log.Finished && log.Status == "Done" && !log.Contacts.Any(c => c.BudgetReason == "Expired" || c.BudgetReason == "TimeOrder" || c.BudgetReason == "InvalidInput")
                              && Of(log, "Instant").Count == 1 && (g.Frame == Frame.Attachment ? Of(log, "Second").Count == 1 && Of(log, "Return").Count == 0 : Of(log, "Return").Count == 1)
                              && rep != null && rep.Shape == (g.Frame == Frame.Path ? Vol2ShapeKind.Path : g.Frame == Frame.Fan ? Vol2ShapeKind.Fan : g.Frame == Frame.Ring ? Vol2ShapeKind.Ring : Vol2ShapeKind.Attachment)
                              && (g.Frame == Frame.Attachment || (rep.TripEndUs - log.StartUs <= roundTrip && rep.TripEndUs > 0));
                    decimal wantReturn = letter == '앙' ? 0.525m : 0.35m;
                    if (ok && g.Frame != Frame.Attachment) ok = Eq(Of(log, "Return")[0].Amount, wantReturn);
                    if (!ok) { bad++; problems.Add(letter + " status=" + log.Status + " instant=" + Of(log, "Instant").Count + " second=" + Of(log, "Second").Count + " return=" + Of(log, "Return").Count + " reasons=" + string.Join("/", log.Contacts.Select(c => c.BudgetReason).Distinct())); }
                }
                Check(r, "G1", total == 20 && bad == 0, "all 20 glyphs of 돌아온다 run to Done with no contact refused as Expired; one instant, a return where the table has one (0.35, 앙 0.525), a second contact on the circle, no return on the circle" + (bad > 0 ? " - " + string.Join("; ", problems) : ""));
            }
            // ---- I1 identity ----
            {
                var rig = Rig.Create(book); var ex = Exec(rig);
                rig.Add("A", 4, 0); rig.Add("B", 8, 0);
                var log = rig.Cast('강', out _); rig.Advance(1.0, 50);
                var attacks = log.Contacts.Select(c => c.Attack.Value).ToList();
                Check(r, "I1", attacks.Distinct().Count() == attacks.Count && log.Contacts.All(c => c.DuplicateCommands == 0)
                    && log.Contacts.Where(c => c.BudgetAccepted).All(c => rig.Services.Commands.Contains(new CommandKey(rig.Services.Ids.ScopeId, CommandRuleKind.Damage, c.Attack.Value, 0))), "every contact has its own AttackId; every paid contact (the returns too) has its damage command in the ledger; no command ran twice");
                Check(r, "I2", rig.Coord.Materials.Audit().Count == 0 && ex.Tuning.LegUs == 500000 && ex.Tuning.HpPerUnit == Vol2Units.DefaultHpPerUnit, "the registry audit is clean; the tuning carries the stated defaults (0.5 s legs, 10 HP per unit)");
            }
        }
    }
}
