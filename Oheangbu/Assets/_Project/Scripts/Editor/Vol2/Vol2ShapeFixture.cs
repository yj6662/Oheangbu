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
    /// <summary>S5-b slice 3 edit-mode fixture of the four frames (D308-149). No scene, no Play. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2ShapeFixture Run "all"
    ///   G1 the spec reads its numbers from NumbersV3 and states WHERE it measures them (fan width = far chord, ring radius = band centre line)
    ///   G2 path: width / length edges, no tunnelling in one long step, behind the caster
    ///   G3 fan: apex, depth edge, wedge edge in / out by the body radius, behind
    ///   G4 ring: hollow middle, inner / outer edge, a walk across, a leap across, a pass through the middle
    ///   G5 swing capsule: reach and thickness
    ///   C1 path through the coordinator: the first body only; the budget refuses a second anyway (defence in depth)
    ///   C2 path wall: stops before a body behind it; a body in front is touched
    ///   C3 fan: exactly the bodies inside, in distance order, all paid
    ///   C4 ring: the band, the walker across it, not the middle, not outside
    ///   C5 attachment (ㅓ 한 번 = parry): the window [t, t + 0.5 s), half-open; 3 colliders of one swing = ONE contact; a second swing = a second; outside the window nothing; no effect
    ///   C6 command ledger: the damage and body-effect keys of a contact are taken once
    ///   C7 ink refused: no shape, no effect.  C8 a verb with no executor: Unsupported, no error log.  C9 burn ticks.  C10 wet factor
    /// Expected values are written here from the frame geometry and the NumbersV3 rows, never read back from a run.</summary>
    public static class Vol2ShapeFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-shape-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-shape-fixture: editor busy (play / compile / import)";
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

        static bool Near(float a, float b, float eps = 1e-4f) => Mathf.Abs(a - b) <= eps;

        static void Cases(Report r)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var n = book.Numbers;

            // ---- G1 ----
            var tuning = new Vol2ShapeTuning();
            var path = Vol2ShapeSpec.Create(Frame.Path, n, Vector3.zero, Vector3.forward, tuning);
            var fan = Vol2ShapeSpec.Create(Frame.Fan, n, Vector3.zero, Vector3.forward, tuning);
            var ring = Vol2ShapeSpec.Create(Frame.Ring, n, Vector3.zero, Vector3.forward, tuning);
            Check(r, "G1a", Near(path.PathWidth, 0.3f) && Near(path.PathLength, 12f) && Near(fan.FanWidth, 4f) && Near(fan.FanDepth, 5f) && Near(ring.RingRadius, 2f) && Near(ring.RingThickness, 0.6f),
                "path 0.3 x 12, fan 4 x 5, ring r 2 / 0.6 read from NumbersV3 (space.*)");
            float half = Mathf.Asin(0.4f);
            Check(r, "G1b", Near(fan.FanHalfAngleRad, half, 1e-5f) && fan.FanWidthMeasuredAt == "far chord" && Near(fan.FanHalfAngleRad * Mathf.Rad2Deg, 23.578f, 0.01f),
                "fan half-angle asin((4 / 2) / 5) = 23.578 deg: the 4 m is the straight chord between the two far arc ends");
            Check(r, "G1c", Near(ring.RingInner, 1.7f) && Near(ring.RingOuter, 2.3f) && ring.RingRadiusMeasuredAt == "band centre line", "ring band 1.7 .. 2.3 m: radius 2 is the band centre line");
            Check(r, "G1d", Vol2ShapeSpec.Create(Frame.Attachment, n, Vector3.zero, Vector3.forward, tuning).Kind == Vol2ShapeKind.Attachment && fan.Kind == Vol2ShapeKind.Fan && path.Kind == Vol2ShapeKind.Path && ring.Kind == Vol2ShapeKind.Ring, "frame -> shape kind");

            // ---- G2 path ----
            float hw = path.PathWidth * 0.5f, br = 0.4f;
            bool Step(float from, float to, float ahead, float side, out float entry) { return Vol2ShapeGeometry.PathStep(Vector3.zero, Vector3.forward, from, to, hw, At(ahead, side), br, out entry, out float _); }
            bool a = Step(0, 12, 11.9f, 0, out float e1), b = Step(0, 12, 12.35f, 0, out float e2), c = !Step(0, 12, 12.6f, 0, out float _);
            Check(r, "G2a", a && b && c && Near(e2, 11.8f, 1e-3f), "length edge: a body at 11.9 m and 12.35 m (touched at 11.8 m) is hit, 12.6 m is not");
            Check(r, "G2b", Step(0, 12, 6, 0.54f, out float _) && !Step(0, 12, 6, 0.56f, out float _), "width edge: half-width 0.15 + body radius 0.4 = 0.55: 0.54 m off the axis hit, 0.56 m not");
            Check(r, "G2c", Step(0, 12, 6, 0, out float e3) && Near(e3, 5.45f, 1e-3f) && Step(0.5f, 1.0f, 4, 0, out float _) == false, "one 12 m step does not tunnel through a body at 6 m (entry 5.45 m); a short step that stops short does not reach it");
            Check(r, "G2d", !Step(0, 12, -0.6f, 0, out float _) && Step(0, 12, -0.5f, 0, out float _), "behind the caster: -0.6 m not hit, a body overlapping the start (-0.5 m) is");

            // ---- G3 fan ----
            bool F(float ahead, float side) => Vol2ShapeGeometry.FanHit(Vector3.zero, Vector3.forward, fan.FanHalfAngleRad, fan.FanDepth, At(ahead, side), br);
            // the wedge edge at depth 3 m is at 3 * tan(23.578 deg) = 1.309 m; its outward normal is (-0.4, 0.9165) in (ahead, side) terms
            float edgeSide = 3f * Mathf.Tan(fan.FanHalfAngleRad);
            Vector2 normal = new Vector2(-Mathf.Sin(fan.FanHalfAngleRad), Mathf.Cos(fan.FanHalfAngleRad));
            Check(r, "G3a", F(3, 0) && F(0.1f, 0) && F(5.3f, 0) && !F(5.5f, 0) && F(4.9f, 1.9f), "centre line in; at the apex in; depth edge 5 + 0.4: 5.3 in, 5.5 out; (4.9, 1.9) in");
            Check(r, "G3b", F(3, edgeSide) && F(3 + normal.x * 0.39f, edgeSide + normal.y * 0.39f) && !F(3 + normal.x * 0.41f, edgeSide + normal.y * 0.41f) && F(3 - normal.x * 0.41f, edgeSide - normal.y * 0.41f),
                "wedge edge: a body centred on the edge is in; 0.39 m outside the edge still touches it (radius 0.4), 0.41 m outside does not; 0.41 m inside is in");
            Check(r, "G3c", !F(-1, 0) && F(-0.3f, 0) && !F(3, 2.6f) && F(3, -edgeSide) && !F(3, -2.6f), "behind the caster out, except a body touching the apex; 2.6 m to the side at 3 m out; the mirror edge is symmetric");

            // ---- G4 ring ----
            bool Rn(float d) => Vol2ShapeGeometry.RingHit(Vector3.zero, ring.RingInner, ring.RingOuter, At(d, 0), br);
            Check(r, "G4a", !Rn(0) && !Rn(1.2f) && Rn(1.35f) && Rn(2.0f) && Rn(2.65f) && !Rn(2.75f), "hollow: the middle (0 m) and 1.2 m are not touched; 1.35 m touches the inner edge (1.7 - 0.4 = 1.3); 2.0 in; 2.65 touches the outer edge; 2.75 out");
            bool X(Vector3 from, Vector3 to) => Vol2ShapeGeometry.RingCrossed(Vector3.zero, ring.RingInner, ring.RingOuter, from, to, br);
            Check(r, "G4b", X(At(1, 0), At(3, 0)) && !X(At(0.5f, 0), At(1.0f, 0)) && !X(At(3, 0), At(3.5f, 0)), "a walk from inside to outside across the band crosses it; staying inside, or beyond, does not");
            Check(r, "G4c", X(At(-1.5f, 0), At(3.5f, 0)) && X(At(0, 0.1f), At(4, 0)) && !X(At(-1, 0), At(1, 0)), "a leap in one step from one side to the other crosses (no foot contact is needed); a pass through the middle does not");

            // ---- G5 swing capsule ----
            Vector3 sleeve = new Vector3(0, 1.1f, 0), tip = new Vector3(0, 1.1f, 1.6f);
            bool S(float ahead, float side) => Vol2ShapeGeometry.SwingHit(sleeve, tip, 0.45f, At(ahead, side), br);
            Check(r, "G5", S(0.8f, 0.8f) && !S(0.8f, 0.9f) && S(2.4f, 0) && !S(2.5f, 0) && S(-0.8f, 0) && !S(-0.9f, 0), "capsule sleeve -> tip 1.6 m, radius 0.45 + body 0.4: 0.8 m beside it is in, 0.9 out; 0.8 past the tip in, 0.9 out");

            // ---- G6 real enemy ids: positive and distinct (a runtime object's Unity instance id is negative and the rules refuse TargetId <= 0) ----
            {
                var made = new List<GameObject>();
                try
                {
                    var reg = new Vol2TargetRegistry();
                    var vitals = new List<Oheangbu.Combat.EnemyVitals>();
                    for (int i = 0; i < 3; i++)
                    {
                        var go = new GameObject("vol2fx_enemy_" + i) { hideFlags = HideFlags.HideAndDontSave }; made.Add(go);
                        var v = go.AddComponent<Oheangbu.Combat.EnemyVitals>(); vitals.Add(v); reg.AddExtra(v);
                    }
                    reg.Refresh();
                    var ids = vitals.Select(v => reg.IdOf(v)).ToList();
                    Check(r, "G6", ids.All(x => x > 0) && ids.Distinct().Count() == 3 && reg.IdOf(null) == 0 && ids.SequenceEqual(vitals.Select(v => reg.IdOf(v))),
                        "three real EnemyVitals get TargetIds " + string.Join(",", ids) + " (positive, distinct, stable); their Unity instance ids are " + string.Join(",", vitals.Select(v => v.GetInstanceID())));
                }
                finally { foreach (var go in made) if (go != null) UnityEngine.Object.DestroyImmediate(go); }
            }
            // ---- C1 path first only ----
            {
                var rig = Rig.Create(book);
                rig.Add("A4", 4, 0); rig.Add("B7", 7, 0); rig.Add("C10", 10, 0); rig.Add("D6side", 6, 0.9f);
                var log = rig.Cast('가', out var dec);
                bool started = log != null && log.Shape == Vol2ShapeKind.Path && log.Contacts.Count == 0 && !log.Finished;
                rig.Advance(0.2);
                var names = Rig.Names(log);
                Check(r, "C1a", started && names.SequenceEqual(new[] { "A4" }) && log.Finished && log.Status == "Done" && ((Vol2PathEmitter)log.Emitter).EndReason == "first",
                    "한 번 path 가: cast accepted, nothing at the instant of casting, then only the body at 4 m (7 m, 10 m and the 0.9 m side one untouched), path ended at the first body");
                var rec = log.Contacts.Count == 1 ? log.Contacts[0] : null;
                Check(r, "C1b", rec != null && rec.BudgetAccepted && rec.BudgetReason == "FirstContact" && rec.DamageApplied && rec.Units == 0.6m && rec.BodyEffect.StartsWith("bind"),
                    "budget FirstContact; damage 0.6 action units (body.wood.instant 0.6 x min(1 x 1, 1.5)); body effect bind");
                Check(r, "C1c", rig.Damage.Requests.Count == 1 && rig.Damage.Requests[0].BaseH == 0.6m && rig.Damage.Requests[0].Structure == 1m && rig.Damage.Requests[0].Force == 1m && !rig.Damage.Requests[0].Lasting
                    && rig.Status.Requests.Count == 1 && rig.Status.Requests[0].Kind == Vol2BodyActionKind.Bind && Near(rig.Status.Requests[0].Magnitude, 0.5f) && rig.Status.Requests[0].DurationUs == 2000000,
                    "one damage request (H 0.6, R 1, S 1) and one status request: bind x0.5 for 2,000,000 us");
                // defence in depth: the same three bodies through the rules' budget, a path that did NOT stop at the first body
                var rig2 = Rig.Create(book);
                var plan = GlyphRulesV3.Build(book.Spells.First(s => s.Letter == '가').Glyph, n);
                var state = new EffectBudgetV3.State(1, plan, 0);
                var s1 = EffectBudgetV3.Apply(state, new Contact(1, 1, 101, 100, 0, ContactKind.Touch, EffectKind.Instant, 0, TriggerKind.None, 0, 0), plan);
                var s2 = EffectBudgetV3.Apply(s1.State, new Contact(2, 1, 102, 100, 0, ContactKind.Touch, EffectKind.Instant, 0, TriggerKind.None, 0, 0), plan);
                Check(r, "C1d", plan.FirstOnly && s1.Accepted && !s2.Accepted && s2.Reason == "FirstTargetOnly", "if a second body were ever offered, the rules' budget refuses it (FirstTargetOnly)");
                var open = new Vol2PathEmitter(path, 0, false, null, 1.1f); var outc = new List<Vol2ShapeContact>();
                var three = new List<IVol2Target> { new FakeTarget(1, "A", 4, 0), new FakeTarget(2, "B", 7, 0), new FakeTarget(3, "C", 10, 0) };
                open.Tick(1000000, three, outc);
                Check(r, "C1e", outc.Count == 3 && outc[0].TargetId == 1 && outc[1].TargetId == 2 && outc[2].TargetId == 3 && Near(outc[0].Along, 3.45f, 1e-3f) && open.EndReason == "length",
                    "the emitter alone, without stop-at-first, reports all three bodies in path order (entry 3.45 m for the first)");
            }

            // ---- C2 wall ----
            {
                var rig = Rig.Create(book);
                rig.Wall.At = 5f; rig.Add("Behind8", 8, 0);
                var log = rig.Cast('가', out _); rig.Advance(0.5, 20);
                Check(r, "C2a", log.Contacts.Count == 0 && log.Finished && ((Vol2PathEmitter)log.Emitter).EndReason == "wall" && rig.Damage.Requests.Count == 0, "a wall at 5 m: the body at 8 m behind it is not touched, the path ended at the wall, no damage asked");
                var rig2 = Rig.Create(book); rig2.Wall.At = 5f; rig2.Add("Front3", 3, 0); rig2.Add("Behind8", 8, 0);
                var log2 = rig2.Cast('가', out _); rig2.Advance(0.5, 20);
                Check(r, "C2b", Rig.Names(log2).SequenceEqual(new[] { "Front3" }), "a body in front of the wall is touched, the one behind is not");
            }

            // ---- C3 fan ----
            {
                var rig = Rig.Create(book);
                rig.Add("In3", 3, 0); rig.Add("In4.8", 4.8f, -0.3f); rig.Add("In4s1", 4, 1); rig.Add("Out5.8", 5.8f, 0); rig.Add("Out3s2.6", 3, 2.6f); rig.Add("Behind", -2, 0);
                var log = rig.Cast('고', out _);
                bool ordered = Rig.Names(log).SequenceEqual(new[] { "In3", "In4s1", "In4.8" });   // by distance from the apex: 3, 4.12, 4.8
                Check(r, "C3a", log.Shape == Vol2ShapeKind.Fan && ordered && log.Finished && log.PaidContacts == 3 && log.Contacts.All(x => x.DamageApplied && x.Units == 0.6m && x.BodyEffect.StartsWith("bind")),
                    "한 번 fan 고: the three bodies inside, in distance order, all paid and bound at the instant of casting; the far, the wide and the one behind are not");
                Check(r, "C3b", rig.Damage.Requests.Count == 3 && rig.Status.Requests.Count == 3 && log.InkPaid == 14m && rig.Ink.Total == 14m, "three damage requests, three binds; ink asked = 14 (ink.bare.o)");
            }

            // ---- C4 ring ----
            {
                var rig = Rig.Create(book);
                rig.Add("Band2.0", 2, 0); rig.Add("BandSide-2.1", 0, -2.1f); rig.Add("Hole0.5", 0.5f, 0.3f); rig.Add("Hole1.0", 1, 0); rig.Add("Out3.4", 3.4f, 0);
                var walker = rig.Add("Walker", 3.2f, 0); walker.Previous = At(1.0f, 0);       // came from inside the hollow, ended outside: crossed the band this tick
                var sitter = rig.Add("MiddleSitter", 0, 0);
                var log = rig.Cast('구', out _);
                var names = Rig.Names(log).OrderBy(x => x, StringComparer.Ordinal).ToList();
                Check(r, "C4", log.Shape == Vol2ShapeKind.Ring && names.SequenceEqual(new[] { "Band2.0", "BandSide-2.1", "Walker" }) && log.Finished && log.PaidContacts == 3,
                    "한 번 ring 구: the two on the band and the one that walked across it; the middle, the hollow and the outside are untouched");
            }

            // ---- C5 attachment / parry ----
            {
                var rig = Rig.Create(book);
                rig.Add("Near1", 1, 0); rig.Add("Far3", 3, 0);
                long t0 = rig.Services.Clock.NowUs;
                var log = rig.Cast('거', out var dec);
                long window = RuleTypesV3.Microseconds(n.Get("combat.parry.window"));
                bool open = log.ParryWindowOpened && log.WindowStartUs == t0 && log.WindowEndUs == t0 + window && window == 500000;
                bool halfOpen = rig.Coord.TryGetParryWindow(t0 + window - 1, out var w1) && w1.Element == Oheangbu.Core.Domain.Element.Wood && !rig.Coord.TryGetParryWindow(t0 + window, out _);
                Check(r, "C5a", open && halfOpen && rig.Parry.Windows.Count == 1 && rig.Parry.Windows[0].Cast == dec.CastId && log.InkRequested == 0m && rig.Ink.Casts == 0,
                    "ㅓ 한 번 거: the window is [t, t + 500,000 us), half-open, carries the body's element for the judge, the port is told once, the ink is 0 (post-payment is slice 2)");
                Vector3 sl = new Vector3(0, 1.1f, 0), tp = sl + Vector3.forward * tuning.BrushReach;
                var swings = rig.Runtime.Swings;
                long id1 = rig.Services.Ids.NextSwing().Value, id2 = rig.Services.Ids.NextSwing().Value;
                for (int k = 0; k < 3; k++) swings.Raise(new Vol2SwingEvent(id1, sl, tp, rig.Services.Clock.NowUs, k));
                Check(r, "C5b", log.Contacts.Count == 1 && log.Contacts[0].TargetName == "Near1" && log.Contacts[0].SwingId == id1 && log.SwingReports == 3 && log.SwingDuplicates == 2,
                    "three colliders of ONE swing report the same SwingId: one contact on the body in reach, the far body untouched, 2 duplicates dropped");
                swings.Raise(new Vol2SwingEvent(id2, sl, tp, rig.Services.Clock.NowUs));
                Check(r, "C5c", log.Contacts.Count == 2 && log.Contacts[1].SwingId == id2, "a second swing (new SwingId) is a second contact");
                Check(r, "C5d", rig.Damage.Requests.Count == 0 && rig.Status.Requests.Count == 0 && log.Contacts.All(x => !x.BudgetAccepted && x.BudgetReason == "ParryNoEffect"), "a parry glyph has no attack term: no damage request, no body effect");
                rig.Advance(0.5001);
                long id3 = rig.Services.Ids.NextSwing().Value;
                swings.Raise(new Vol2SwingEvent(id3, sl, tp, rig.Services.Clock.NowUs));
                Check(r, "C5e", log.Contacts.Count == 2 && log.Finished && log.Status == "Done" && rig.Coord.ActiveCount == 0, "after the window a swing reports nothing; the cast is Done and gone from the active list");
            }

            // ---- C6 command ledger ----
            {
                var rig = Rig.Create(book);
                rig.Add("A4", 4, 0);
                bool mapped = false;
                rig.Coord.ContactResolved += (l, rec) => { mapped = rig.Services.Attacks.TryGet(rec.Attack, out var meta) && meta.CastId == l.Cast && meta.Letter == '가'; };
                var log = rig.Cast('가', out _); rig.Advance(0.2);
                var attack = log.Contacts[0].Attack; int scope = rig.Services.Ids.ScopeId;
                bool damageTaken = rig.Services.Commands.Contains(new CommandKey(scope, CommandRuleKind.Damage, attack.Value, 0));
                bool statusTaken = rig.Services.Commands.Contains(new CommandKey(scope, CommandRuleKind.Status, attack.Value, 0));
                Check(r, "C6", damageTaken && statusTaken && !rig.Services.Commands.TryBegin(new CommandKey(scope, CommandRuleKind.Damage, attack.Value, 0)) && mapped && rig.Services.Attacks.Count == 0,
                    "the contact's damage and status keys are in the ledger (a replay is refused); while the cast lives its AttackId maps back to the CastId, and the cast's attacks are released when it ends");
            }

            // ---- C7 ink refused ----
            {
                var rig = Rig.Create(book); rig.Ink.Refuse = true; rig.Add("A4", 4, 0);
                var log = rig.Cast('가', out _); rig.Advance(0.2);
                Check(r, "C7", log.Status == "InkRefused" && log.Finished && log.Contacts.Count == 0 && rig.Damage.Requests.Count == 0 && rig.Coord.ActiveCount == 0, "a refused ink spend: no shape, no contact, no damage");
            }

            // ---- C8 a verb nobody registered (slice 4: 남긴다 and 번진다 have executors now; the registry is asked, so later slices need no edit here) ----
            {
                var rig = Rig.Create(book); rig.Add("A4", 4, 0);
                char unregistered = book.Spells.Select(s => s.Glyph).Where(g => g.Letter != '걱' && !rig.Coord.Verbs.Handles(g.Verb)).Select(g => g.Letter).FirstOrDefault();
                if (unregistered == '\0') Check(r, "C8", true, "every verb has an executor (nothing is Unsupported any more)");
                else
                {
                    int unsupported = 0; rig.Coord.CastUnsupported += _ => unsupported++;
                    var log = rig.Cast(unregistered, out _); rig.Advance(0.2);
                    Check(r, "C8", log.Status == "Unsupported" && log.Finished && unsupported == 1 && rig.Coord.UnsupportedCasts == 1 && rig.Damage.Requests.Count == 0, "a verb without an executor (" + unregistered + "): the cast is accepted by the route, Unsupported on the log, nothing happens, no error");
                }
            }

            // ---- C9 burn ----
            {
                var rig = Rig.Create(book); var a4 = rig.Add("A4", 4, 0);
                var log = rig.Cast('나', out _); rig.Advance(0.2);
                bool hit = log.Contacts.Count == 1 && log.Contacts[0].Units == 1.0m && log.Contacts[0].BodyEffect.StartsWith("burn") && !log.Finished;
                rig.Advance(2.2, 22);
                decimal burned = log.Ticks.Where(t => t.Applied).Sum(t => t.Units);
                Check(r, "C9", hit && log.Ticks.Count == 4 && burned == 0.3m && log.Finished && log.Status == "Done" && rig.Damage.Requests.Count(q => q.Lasting) == 4 && rig.Damage.Requests.Count == 5,
                    "나 (fire path): instant 1.0, then 4 ticks every 0.5 s totalling body.fire.utility 0.3 over body.fire.duration 2 s; the cast ends with its last tick");
            }

            // ---- C10 wet ----
            {
                var rig = Rig.Create(book); rig.Add("A4", 4, 0);
                rig.Cast('아', out _); rig.Advance(0.2);
                var metal = rig.Cast('사', out _); rig.Advance(0.2);
                var rigDry = Rig.Create(book); rigDry.Add("A4", 4, 0);
                var dry = rigDry.Cast('사', out _); rigDry.Advance(0.2);
                Check(r, "C10", metal.Contacts.Count == 1 && metal.Contacts[0].Units == 1.0m && metal.Contacts[0].StatusFactor == 1.25m && dry.Contacts[0].Units == 0.8m && dry.Contacts[0].StatusFactor == 1m,
                    "금 on a wet body: 0.8 x 1.25 = 1.0; on a dry body 0.8 (the wet mark comes from 아, body.water.duration 4 s)");
            }
        }
    }
}
