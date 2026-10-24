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
    /// <summary>S5-b slice 4 edit-mode fixture of the material registry and the verb registry (D308-149). No scene, no Play. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2MaterialFixture Run "all"
    ///   M1 draws: a pool is drawn down to zero and then consumed; a draw never takes more than is left
    ///   M2 expiry is half-open [created, created + life): alive one tick before, Expired AT the end, no draw at the end
    ///   M3 split: the parent keeps its id, life and lineage; the parts add up to exactly what it held (also for 0.1 / 3)
    ///   M4 extend: life x1.5 once, the amount untouched; a second extension is refused (no stacking)
    ///   M5 move keeps the id, the amount and the life
    ///   M6 the end reasons: Expired, Consumed, ChildConsumed, Replaced, HitCancelled, Cleansed, Aborted - one each, announced once, never twice
    ///   M7 events and the audit; EndAll
    ///   M8 footprints: disc, fan (growth is ONE linear factor), ring (a path across it), band overlap, fan distance
    ///   V1 the verb registry: one executor per verb (a second throws), the default list holds 한 번 / 남긴다 / 번진다, the key is the executor's own verb
    /// Expected values are written here, never read back from a run.</summary>
    public static class Vol2MaterialFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-material-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-material-fixture: editor busy (play / compile / import)";
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
        static Vol2MaterialRegistry NewReg(out ManualVol2Clock clock) { clock = new ManualVol2Clock(); return new Vol2MaterialRegistry(clock, new Vol2IdIssuer()); }
        static Vol2Material Make(Vol2MaterialRegistry reg, long cast, decimal amount, long life, long now = 0, Vol2Footprint fp = null, bool pool = true, Vol2MaterialKind kind = Vol2MaterialKind.Residue)
            => reg.Create(new CastId(cast), '낙', Body.Fire, kind, amount, life, fp ?? Vol2Footprint.Disc(Vector3.zero, 0.6f), Vol2Faction.Player, Vol2Faction.Player, pool, now);

        static void Cases(Report r)
        {
            // ---- M1 ----
            {
                var reg = NewReg(out _); var m = Make(reg, 1, 0.9m, 10 * S);
                bool a = reg.TryDebit(m.Id, 0.3m, 1 * S, out decimal t1) && t1 == 0.3m && m.Remaining == 0.6m && m.Drawn == 0.3m && m.Alive;
                bool b = reg.TryDebit(m.Id, 0.9m, 2 * S, out decimal t2) && t2 == 0.6m && m.Remaining == 0m && m.End == Vol2MaterialEnd.Consumed && m.EndedUs == 2 * S;
                bool c = !reg.TryDebit(m.Id, 0.1m, 3 * S, out decimal t3) && t3 == 0m;
                bool d = !reg.TryDebit(Make(reg, 2, 0.9m, 10 * S).Id, 0m, 0, out _) && !reg.TryDebit(Make(reg, 3, 0.9m, 10 * S).Id, -0.1m, 0, out _);
                Check(r, "M1", a && b && c && d && m.Drawn == 0.9m && reg.Audit().Count == 0, "0.3 then 0.9 takes only the 0.6 left and consumes it (End Consumed at the draw); a spent material refuses; zero and negative draws refused; Drawn = Initial");
            }
            // ---- M2 ----
            {
                var reg = NewReg(out _); var m = Make(reg, 1, 0.9m, 10 * S, 0);
                int before = reg.Sweep(10 * S - 1);
                bool early = before == 0 && m.Alive;
                bool drawAtEnd = !reg.TryDebit(m.Id, 0.1m, 10 * S, out _);          // the end is not inside the life, even before the sweep
                int at = reg.Sweep(10 * S);
                Check(r, "M2", early && drawAtEnd && at == 1 && m.End == Vol2MaterialEnd.Expired && m.EndedUs == 10 * S && m.Remaining == 0.9m && reg.AliveCount == 0, "alive at 9,999,999 us; no draw at 10,000,000; the sweep at 10,000,000 ends it Expired with its amount untouched");
            }
            // ---- M3 ----
            {
                var reg = NewReg(out _); var m = Make(reg, 7, 0.9m, 10 * S);
                bool ok = reg.TrySplit(m.Id, 3, 1 * S, out var kids);
                var all = new List<Vol2Material> { m }; all.AddRange(kids);
                bool sums = ok && all.Sum(x => x.Remaining) == 0.9m && all.All(x => x.Remaining == 0.3m) && kids.Count == 2;
                bool keeps = m.Alive && m.Cast.Value == 7 && m.ExpiresUs == 10 * S && m.Splits == 1 && kids.All(k => k.Cast.Value == 7 && k.ExpiresUs == 10 * S && k.CreatedUs == 0 && k.Splits == 1 && k.Id != m.Id && k.Alive);
                var tiny = Make(reg, 8, 0.1m, 10 * S); bool tinyOk = reg.TrySplit(tiny.Id, 3, 0, out var tk) && (tiny.Remaining + tk.Sum(x => x.Remaining)) == 0.1m && tk.All(x => x.Remaining > 0m);
                var gone = Make(reg, 9, 0.9m, 10 * S); reg.TryEnd(gone.Id, Vol2MaterialEnd.Consumed, 0);
                bool refuses = !reg.TrySplit(gone.Id, 2, 0, out _) && !reg.TrySplit(m.Id, 1, 0, out _);
                Check(r, "M3", sums && keeps && tinyOk && refuses && reg.Audit().Count == 0, "0.9 into 3: the parent KEEPS its id / life / cast and holds 0.3, two new parts of 0.3, total exactly 0.9; 0.1 / 3 still adds up to exactly 0.1; a spent material and a 1-way split are refused");
            }
            // ---- M4 ----
            {
                var reg = NewReg(out _); var m = Make(reg, 1, 0.9m, 10 * S); reg.TryDebit(m.Id, 0.3m, 0, out _);
                bool first = reg.TryExtend(m.Id, 1.5m, 2 * S, out long extra) && extra == 5 * S && m.ExpiresUs == 15 * S && m.LifeExtended && m.Remaining == 0.6m && m.Drawn == 0.3m && m.Id.Value == 1;
                bool second = !reg.TryExtend(m.Id, 1.5m, 3 * S, out long extra2) && extra2 == 0 && m.ExpiresUs == 15 * S;
                var other = Make(reg, 2, 0.9m, 10 * S); bool noGrow = !reg.TryExtend(other.Id, 1m, 0, out _) && !reg.TryExtend(other.Id, 0.5m, 0, out _) && !other.LifeExtended;
                reg.Sweep(10 * S); bool stillAlive = m.Alive; reg.Sweep(15 * S);
                Check(r, "M4", first && second && noGrow && stillAlive && m.End == Vol2MaterialEnd.Expired && m.EndedUs == 15 * S, "10 s -> 15 s once (+5 s), amount and id untouched; a second extension is refused; a factor of 1 or less is refused; it lives past 10 s and expires at 15 s");
            }
            // ---- M5 ----
            {
                var reg = NewReg(out _); var m = Make(reg, 1, 0.9m, 10 * S, 0, Vol2Footprint.Disc(Vector3.zero, 0.6f)); reg.TryDebit(m.Id, 0.3m, 0, out _);
                long id = m.Id.Value; var to = Vol2Footprint.Disc(new Vector3(0, 0, 2), 0.6f);
                bool moved = reg.TryMove(m.Id, to, 1 * S) && m.Footprint == to && m.Id.Value == id && m.Remaining == 0.6m && m.ExpiresUs == 10 * S && m.Alive;
                reg.TryEnd(m.Id, Vol2MaterialEnd.Consumed, 2 * S);
                Check(r, "M5", moved && !reg.TryMove(m.Id, to, 3 * S) && !reg.TryMove(m.Id, null, 0), "moved to 2 m ahead with the same id, amount and life; a spent material and a null footprint are refused");
            }
            // ---- M6 ----
            {
                var reg = NewReg(out _); var seen = new List<(long id, Vol2MaterialEnd why)>(); reg.Ended += (m, why) => seen.Add((m.Id.Value, why));
                var reasons = new[] { Vol2MaterialEnd.Expired, Vol2MaterialEnd.Consumed, Vol2MaterialEnd.ChildConsumed, Vol2MaterialEnd.Replaced, Vol2MaterialEnd.HitCancelled, Vol2MaterialEnd.Cleansed, Vol2MaterialEnd.Aborted };
                var mats = reasons.Select(w => Make(reg, 1, 0.9m, 10 * S)).ToList();
                bool all = true;
                for (int i = 0; i < reasons.Length; i++) all &= reg.TryEnd(mats[i].Id, reasons[i], 1 * S) && mats[i].End == reasons[i] && mats[i].EndedUs == 1 * S && !mats[i].Alive;
                bool twice = !reg.TryEnd(mats[0].Id, Vol2MaterialEnd.Consumed, 2 * S) && mats[0].End == Vol2MaterialEnd.Expired;
                bool once = seen.Count == 7 && seen.Select(x => x.why).SequenceEqual(reasons);
                bool none = false; try { reg.TryEnd(Make(reg, 2, 0.9m, 10 * S).Id, Vol2MaterialEnd.None, 0); } catch (ArgumentException) { none = true; }
                bool sweepSilent = reg.Sweep(100 * S) == 1 && seen.Count == 8 && seen[7].why == Vol2MaterialEnd.Expired;   // only the one still alive (the None test material)
                Check(r, "M6", all && twice && once && none && sweepSilent, "seven end reasons each stored and announced once in order; a second end is refused and keeps the first reason; an end without a reason throws; the sweep never re-ends an ended material");
            }
            // ---- M7 ----
            {
                var reg = NewReg(out _); int created = 0, changed = 0, ended = 0;
                reg.Created += _ => created++; reg.Changed += (_, w) => changed++; reg.Ended += (_, w) => ended++;
                var a = Make(reg, 1, 0.9m, 10 * S); var b = Make(reg, 1, 0.9m, 10 * S, 0, null, false, Vol2MaterialKind.Mark);
                reg.TryDebit(a.Id, 0.3m, 0, out _); reg.TrySplit(a.Id, 2, 0, out _); reg.TryExtend(b.Id, 1.5m, 0, out _);
                int cut = reg.EndAll(5 * S);
                bool audit = reg.Audit().Count == 0 && reg.AliveCount == 0;
                bool byCast = reg.ByCast(new CastId(1)).Count == 3;
                Check(r, "M7", created == 3 && changed == 3 && ended == 3 && cut == 3 && audit && byCast && a.End == Vol2MaterialEnd.Aborted && reg.TotalCreated == 3, "3 created (2 + 1 split part), 3 changes (drawn, split, extended), EndAll ends all three Aborted, audit clean, ByCast finds them");
            }
            // ---- M8 ----
            {
                var disc = Vol2Footprint.Disc(Vector3.zero, 0.6f); var t = new FakeTarget(1, "t", 0.9f, 0f);       // 0.9 away; 0.6 + body 0.4 = 1.0
                bool discOk = disc.Touches(t, 0) && !disc.Touches(new FakeTarget(2, "u", 1.05f, 0f), 0);
                var numbers = Vol2RulebookLoader.Load(null, null);
                if (!numbers.Ok) { Check(r, "M8", false, "rulebook unavailable: " + numbers.Error); }
                else
                {
                    var spec = Vol2ShapeSpec.Create(Frame.Fan, numbers.Numbers, Vector3.zero, Vector3.forward, new Vol2ShapeTuning());
                    float lin = Mathf.Sqrt(1.5f);
                    var fan = Vol2Footprint.Fan(spec, lin, 0, 10 * S);
                    bool grow = Mathf.Abs(fan.ScaleAt(0) - 1f) < 1e-5f && Mathf.Abs(fan.ScaleAt(5 * S) - (1f + (lin - 1f) * 0.5f)) < 1e-5f && Mathf.Abs(fan.ScaleAt(10 * S) - lin) < 1e-5f && Mathf.Abs(fan.ScaleAt(20 * S) - lin) < 1e-5f
                        && Mathf.Abs(lin * lin - 1.5f) < 1e-5f && lin < 1.25f;
                    var far = new FakeTarget(3, "far", 5.7f, 0f);
                    bool reach = !fan.Touches(far, 0) && fan.Touches(far, 10 * S) && !Vol2Footprint.Fan(spec).Touches(far, 10 * S);
                    var ring = Vol2Footprint.Ring(spec2(numbers, Frame.Ring));
                    var walker = new FakeTarget(4, "w", 3.2f, 0f); walker.Previous = At(1f, 0f);
                    bool ringOk = ring.Touches(walker, 0) && !ring.Touches(new FakeTarget(5, "hole", 0.5f, 0.3f), 0);
                    bool band = disc.OverlapsBand(Vector3.zero, 0.0f, 0.3f) && !Vol2Footprint.Disc(At(5f, 0f), 0.6f).OverlapsBand(Vector3.zero, 1.7f, 2.3f) && Vol2Footprint.Disc(At(2f, 0f), 0.6f).OverlapsBand(Vector3.zero, 1.7f, 2.3f);
                    float edge = 3f * Mathf.Tan(spec.FanHalfAngleRad);
                    bool dist = Mathf.Abs(Vol2Footprint.FanDistance(Vector3.zero, Vector3.forward, spec.FanHalfAngleRad, spec.FanDepth, At(5.8f, 0f)) - 0.8f) < 1e-4f
                        && Vol2Footprint.FanDistance(Vector3.zero, Vector3.forward, spec.FanHalfAngleRad, spec.FanDepth, At(3f, 0f)) == 0f
                        && Mathf.Abs(Vol2Footprint.FanDistance(Vector3.zero, Vector3.forward, spec.FanHalfAngleRad, spec.FanDepth, At(-2f, 0f)) - 2f) < 1e-4f
                        && Mathf.Abs(Vol2Footprint.FanDistance(Vector3.zero, Vector3.forward, spec.FanHalfAngleRad, spec.FanDepth, At(3f, 2.6f)) - 1.1823f) < 1e-3f && edge > 1.3f;
                    Check(r, "M8", discOk && grow && reach && ringOk && band && dist, "disc 0.6 + body 0.4; the fan grows by ONE linear factor sqrt(1.5) = 1.2247 over its life (area x1.5, not 1.5 on both axes) so a body at 5.7 m is out at first and in at the end; the ring catches a walker across the band, not the hollow; band overlap; planar distance to the fan (0.8, 0, 2.0, 1.18)");
                }
            }
            // ---- V1 ----
            {
                var reg = Vol2VerbRegistry.CreateDefault();
                bool dup = false; try { reg.Add(new Vol2OnceExecutor()); } catch (InvalidOperationException) { dup = true; }
                bool keys = reg.Registered().All(v => reg.TryGet(v, out var ex) && ex.Verb == v);
                bool mine = reg.Handles(Verb.Once) && reg.Handles(Verb.Leave) && reg.Handles(Verb.Spread);
                Check(r, "V1", dup && keys && mine, "the default list holds 한 번, 남긴다, 번진다 (registered now: " + string.Join(",", reg.Registered()) + "); a second executor for a verb throws; every key is its executor's own verb");
            }
        }

        static Vol2ShapeSpec spec2(Vol2Rulebook book, Frame frame) => Vol2ShapeSpec.Create(frame, book.Numbers, Vector3.zero, Vector3.forward, new Vol2ShapeTuning());
    }
}
