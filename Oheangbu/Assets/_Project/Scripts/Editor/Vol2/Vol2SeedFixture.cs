using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using static Oheangbu.EditorTools.Vol2.Vol2ShapeFixtureKit;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 5 edit-mode fixture of 심는다 (D308-149): the seed, its four triggers, the caps, 信, the four earth peaks. No scene, no Play. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2SeedFixture Run "all"
    ///   Q1 path 감 plants the first three bodies, the fourth is refused TargetCap; nothing is struck; the seed is a Mark holding 0, shaped like the host, alive until cast + 8 s
    ///   Q2 the owner's later direct hit (another cast's first contact) works the seed once: 1.2, the body action once, posture 60 asked once, the seed Consumed; never twice
    ///   Q3 갈무리 works a seed (gather contact: cast, material, stable body); another body's contact does not; a repeated contact does not; the un-triggered seed expires
    ///   Q4 ㅏ+ㅁ only: fan / ring / attachment seeds ask no posture
    ///   Q5 信: the drawing holds the waiting seeds (expiry and budget clock), at most 4 s per cast; a worked seed holds nothing; an activation after 8 s of real time works
    ///   Q6 ㅓ 검: the seed works on the next REAL hit (not 0 HP): enemies within 2 m, nearest first, three at most (N4 TargetCap); a second hit works nothing; the damage is not touched
    ///   Q7 멈: the same + the stagger exemption is decided inside the confirmation (a listener that runs after sees it), taken once, not carried to the next hit; only 멈 grants it
    ///   Q8 ㅗ 곰: an initial occupant is not an entry; an entry works on every body in the fan at that moment, one trigger id; once; a later entry does nothing
    ///   Q9 ㅜ 굼: the same on the band; a dash across the band is an entry; a body that leaves and returns is an entry; the hollow and the outside never; the tuning flag turns initial occupants on
    ///   Q10 몸: the giant strikes 1.8 (the entry clips C to 1.5), collapses to an earth fan of 0 for 10 s; 동시 1: a new giant replaces the standing one
    ///   Q11 맘: pressed - bind x0 for 1.5 s (a boss 0.75 s) + the 1.2 hit, posture 60 with the letter
    ///   Q12 멈 push on the three, 뭄: only the CASTER on the band works it; the ring hardens 8 s, no damage, BlocksDashOrJump inside / not outside / not after; the field is asked
    ///   Q13 host gone: a dead or reborn host ends the seed HitCancelled; the registry / scope reset ends the rest Aborted; the hub forgets the run
    ///   Q14 the real kit: 갈무리 through Vol2GatherService (CarryProvider chained, Gathered heard) and posture 60 through the real bridge; the real PlayerVitals feeds the gate
    /// Expected values are written here from NumbersV3 and the table, never read back from a run.</summary>
    public static class Vol2SeedFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-seed-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-seed-fixture: editor busy (play / compile / import)";
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

        const long S = 1000000;
        static bool Eq(decimal a, decimal b) => Math.Abs(a - b) <= 0.000000001m;
        static List<Vol2ContactRecord> Acts(Vol2CastLog log) => log.Contacts.Where(c => c.Kind == "Activation" && c.BudgetAccepted).ToList();
        static List<Vol2Material> Seeds(Vol2CastLog log) => log.Materials.Where(m => m.Tag == Vol2SeedExecutor.TagSeed || m.Tag == Vol2SeedExecutor.TagGiant).ToList();
        static string Join(IEnumerable<string> s) => string.Join("|", s);
        static IEnumerable<string> Names(IEnumerable<Vol2ContactRecord> c) => c.Select(x => x.TargetName);

        sealed class FakeField : IVol2FieldPort
        {
            public readonly List<string> Platforms = new List<string>();
            public string OnPlatform(CastId cast, Vector3 centre, float radius, long lifeUs) { Platforms.Add(cast + " r" + radius + " " + lifeUs); return "ground ok"; }
            public string OnVineBurn(CastId cast, Vol2Material vine) => "";
        }

        sealed class Posture
        {
            public readonly List<(long id, char letter)> Calls = new List<(long, char)>();
            public Vol2SeedPosture Ask(IVol2Target t, char letter) { Calls.Add((t.Id, letter)); return new Vol2SeedPosture(true, true, "Accumulated", 60m, 60m, 0); }
        }

        static Vol2SeedHub HubOf(Rig rig, Posture posture = null)
        {
            var hub = Vol2SeedExecutor.HubOf(rig.Coord);
            if (posture != null) hub.ActiveSeedPosture = posture.Ask;
            hub.GatherTargetMatches = (t, id) => t.Id == id;
            return hub;
        }

        static void Cases(Report r, List<Object> made)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var n = book.Numbers;
            decimal act = n.Get("action.seed.activation"), cap = n.Get("combat.peak.structureForceCap");
            long life = RuleTypesV3.Microseconds(n.Get("timing.seed.life")), pauseCap = 4 * S;
            decimal H(Body b) => Vol2BodyProfile.For(b, n).Instant;
            decimal Units(Body b, decimal amount) => H(b) * Math.Min(amount, cap);

            Check(r, "Q0", Vol2VerbRegistry.CreateDefault().Handles(Verb.Seed) && life == 8 * S && act == 1.2m, "the default registry has a 심는다 executor; the wait is 8 s, the work 1.2");

            // ---- Q1 planting ----
            {
                var rig = Rig.Create(book); var hub = HubOf(rig, new Posture());
                var a = rig.Add("A", 4, 0); var b = rig.Add("B", 6, 0); var c = rig.Add("C", 8, 0); var d = rig.Add("D", 10, 0);
                var log = rig.Cast('감', out var dec); rig.Advance(0.5, 10);
                var plants = log.Contacts.Where(x => x.Kind == "Plant").ToList();
                var seeds = Seeds(log);
                Check(r, "Q1a", Join(Names(plants.Where(x => x.BudgetAccepted))) == "A|B|C" && plants.Any(x => x.TargetName == "D" && !x.BudgetAccepted && x.BudgetReason == "TargetCap") && seeds.Count == 3,
                    "감: the path passes through and plants A B C (the first three); D, the fourth, is refused TargetCap (no seed)");
                Check(r, "Q1b", seeds.All(m => m.Kind == Vol2MaterialKind.Mark && !m.PaysPool && m.Initial == 0m && m.FactionId == Vol2Faction.Player && m.Footprint.Kind == Vol2FootprintKind.Disc && m.ExpiresUs == log.StartUs + life && m.Alive)
                    && rig.Damage.Requests.Count == 0 && rig.Status.Requests.Count == 0 && Eq(log.Budget.PaidPerTarget, 0m) && log.Budget.State.Targets.Count == 3,
                    "each seed is a Mark holding 0, mine, shaped like its host, alive until cast + 8 s; nothing is struck (no damage, no body action, nothing paid)");
                Check(r, "Q1c", rig.Ink.Casts == 1 && rig.Ink.Total == dec.Plan.Cost && !log.Finished && log.Status == "Running", "the cast paid its ink once and keeps running while a seed waits");
                // the host moves: the seed follows it
                a.MoveTo(At(5f, 1f)); rig.Advance(0.1);
                var sa = seeds.FirstOrDefault(m => m.Footprint.Origin.z > 3f && Mathf.Abs(m.Footprint.Origin.x - 1f) < 0.01f);
                Check(r, "Q1d", sa != null && Mathf.Abs(sa.Footprint.Origin.z - 5f) < 0.01f, "a seed follows its host (the footprint moves with the body)");
                // expiry
                rig.Advance(8.5, 17);
                Check(r, "Q1e", seeds.All(m => m.End == Vol2MaterialEnd.Expired && m.ExpiresUs == log.StartUs + life) && log.Finished && log.Status == "Done" && rig.Damage.Requests.Count == 0 && rig.Coord.Materials.Audit().Count == 0,
                    "no trigger came: every seed ends Expired at cast + 8 s, the cast is Done, nothing was ever struck");
            }
            // ---- Q2 the owner's later direct hit ----
            {
                var rig = Rig.Create(book); var posture = new Posture(); var hub = HubOf(rig, posture);
                var a = rig.Add("A", 4, 0); var b = rig.Add("B", 6, 0); var cc = rig.Add("C", 8, 0);
                var log = rig.Cast('감', out _); rig.Advance(0.5, 10);
                var sA = Seeds(log).First(m => Mathf.Abs(m.Footprint.Origin.z - 4f) < 0.01f);
                int before = rig.Damage.Requests.Count;
                var log2 = rig.Cast('가', out _); rig.Advance(0.3, 6);
                var act1 = Acts(log);
                var mine = rig.Damage.Requests.Where(q => q.Cast == log.Cast).ToList();
                Check(r, "Q2a", Join(Names(act1)) == "A" && Eq(act1[0].Amount, act) && mine.Count == 1 && mine[0].BaseH == H(Body.Wood) && Eq(mine[0].Structure, act) && Eq(mine[0].Units, Units(Body.Wood, act)) && sA.End == Vol2MaterialEnd.Consumed,
                    "the second cast's direct hit on A works A's seed: 1.2 (H wood x 1.2 units), the seed is Consumed; B and C still wait");
                var binds = rig.Status.Requests.Where(q => q.Cast == log.Cast).ToList();
                Check(r, "Q2b", binds.Count == 1 && binds[0].Kind == Vol2BodyActionKind.Bind && binds[0].Target.Id == a.Id && Mathf.Abs(binds[0].Magnitude - 0.5f) < 1e-4f, "the body acts once: bind x0.5 on A");
                Check(r, "Q2c", posture.Calls.Count == 1 && posture.Calls[0].id == a.Id && posture.Calls[0].letter == '감', "ㅏ+ㅁ active seed: posture asked once, for A, with the seed's letter");
                Check(r, "Q2d", Seeds(log).Count(m => m.Alive) == 2 && Eq(log.Budget.PaidPerTarget, act) && log2.Contacts.Any(x => x.Kind == "Instant" && x.TargetName == "A"), "B and C keep waiting; the budget paid 1.2 once");
                rig.Cast('가', out _); rig.Advance(0.3, 6);
                Check(r, "Q2e", Acts(log).Count == 1 && posture.Calls.Count == 1, "a seed works once: another hit on A does nothing");
                // a hit on an unseeded body, and the budget's event ids
                rig.Targets.Remove(a); var log4 = rig.Cast('가', out _); rig.Advance(0.3, 6);
                Check(r, "Q2f", Join(Names(log4.Contacts.Where(x => x.Kind == "Instant"))) == "B" && Join(Names(Acts(log))) == "A|B", "with A gone the next 가 hits B: B's seed works (the trigger is the owner's hit on THAT body)");
            }
            // ---- Q3 갈무리 ----
            {
                var rig = Rig.Create(book); var posture = new Posture(); var hub = HubOf(rig, posture);
                var a = rig.Add("A", 4, 0); var b = rig.Add("B", 6, 0); var cc = rig.Add("C", 8, 0);
                var log = rig.Cast('남', out _); rig.Advance(0.5, 10);
                var seeds = Seeds(log); var sA = seeds.First(m => Mathf.Abs(m.Footprint.Origin.z - 4f) < 0.01f); var sB = seeds.First(m => Mathf.Abs(m.Footprint.Origin.z - 6f) < 0.01f); var sC = seeds.First(m => Mathf.Abs(m.Footprint.Origin.z - 8f) < 0.01f);
                hub.NotifyGatherContact(log.Cast.Value, sC.Id.Value, a.Id, 701);                       // C's seed, but the swing met A: refused
                Check(r, "Q3a", Acts(log).Count == 0 && posture.Calls.Count == 0, "a 갈무리 contact that met another body works nothing (the seed is on C)");
                hub.NotifyGatherContact(log.Cast.Value, sB.Id.Value, b.Id, 702);
                var act1 = Acts(log);
                var req = rig.Damage.Requests.Where(q => q.Cast == log.Cast).ToList();
                Check(r, "Q3b", Join(Names(act1)) == "B" && req.Count == 1 && Eq(req[0].Units, Units(Body.Fire, act)) && posture.Calls.Count == 1 && posture.Calls[0].id == b.Id && sB.End == Vol2MaterialEnd.Consumed && act1[0].SwingId == 702,
                    "the 갈무리 contact on B works B's seed (1.2, fire, the swing's id 702, posture 60 asked)");
                hub.NotifyGatherContact(log.Cast.Value, sB.Id.Value, b.Id, 703);
                hub.NotifyGatherContact(log.Cast.Value + 1, sA.Id.Value, a.Id, 704);                    // another cast's id
                Check(r, "Q3c", Acts(log).Count == 1 && sA.Alive, "the same contact again does nothing; a contact carrying another cast's id does nothing");
                var recs = hub.RecordsOf(log.Cast);
                Check(r, "Q3d", recs.Any(x => x.Kind == "ActivateRefused" && x.Trigger == "gather") && recs.Any(x => x.Kind == "Activate" && x.Trigger == "gather"), "the facts say what happened (refused / worked, by 갈무리)");
                rig.Advance(8.2, 41);
                Check(r, "Q3e", sA.End == Vol2MaterialEnd.Expired && sC.End == Vol2MaterialEnd.Expired && log.Finished && log.Status == "Done", "the seeds nobody worked expire; the cast is Done");
            }
            // ---- Q4 only ㅏ+ㅁ asks for posture ----
            {
                var rig = Rig.Create(book); var posture = new Posture(); var hub = HubOf(rig, posture);
                var gate = new Vol2SeedHitGate(rig.Services.Clock); hub.AttachHits(gate);
                rig.Add("In3", 3, 0); rig.Add("N", 1, 0.5f); var e = rig.Add("E", 7, 0.3f);
                var logF = rig.Cast('곰', out _); rig.Advance(0.1);
                e.MoveTo(At(4f, 0.3f)); rig.Advance(0.1);
                var logA = rig.Cast('검', out _); rig.Advance(0.1); gate.Report(5f);
                Check(r, "Q4", Acts(logF).Count >= 2 && Acts(logA).Count == 1 && posture.Calls.Count == 0, "자동 씨앗 그로기 0: a fan seed and an attachment seed worked, the posture was never asked");
            }
            // ---- Q5 信 ----
            {
                var rig = Rig.Create(book); var hub = HubOf(rig, new Posture());
                var a = rig.Add("A", 4, 0);
                var log = rig.Cast('감', out _); rig.Advance(0.5, 10);
                var sA = Seeds(log)[0];
                hub.DrawingSimulated = true; rig.Advance(2.0, 20); hub.DrawingSimulated = false; rig.Advance(0.1);
                Check(r, "Q5a", hub.PausedUsOf(log.Cast) == 2 * S && sA.ExpiresUs == log.StartUs + life + 2 * S, "2 s of drawing hold the seed 2 s: it now lives until cast + 10 s");
                rig.Advance(7.0, 70);
                bool alive = sA.Alive; rig.Advance(1.0, 10);
                Check(r, "Q5b", alive && sA.End == Vol2MaterialEnd.Expired && sA.EndedUs >= log.StartUs + life + 2 * S, "alive at 9.6 s (past the unheld 8 s), Expired once its held life is over");
                // the cap
                var rig2 = Rig.Create(book); var hub2 = HubOf(rig2, new Posture()); rig2.Add("A", 4, 0);
                var log2 = rig2.Cast('감', out _); rig2.Advance(0.5, 10);
                var s2 = Seeds(log2)[0];
                hub2.DrawingSimulated = true; rig2.Advance(6.0, 60); hub2.DrawingSimulated = false; rig2.Advance(0.1);
                Check(r, "Q5c", hub2.PausedUsOf(log2.Cast) == pauseCap && s2.ExpiresUs == log2.StartUs + life + pauseCap && s2.Alive, "a long letter holds the seeds 4 s at most per cast (the cap), no more");
                rig2.Advance(5.0, 50);
                bool aliveAt11 = s2.Alive; rig2.Advance(1.0, 10);
                Check(r, "Q5d", aliveAt11 && s2.End == Vol2MaterialEnd.Expired, "it lives to cast + 12 s and then expires");
                // a worked seed holds nothing
                var rig3 = Rig.Create(book); var hub3 = HubOf(rig3, new Posture()); rig3.Add("A", 4, 0);
                var log3 = rig3.Cast('감', out _); rig3.Advance(0.5, 10); rig3.Cast('가', out _); rig3.Advance(0.3, 6);
                hub3.DrawingSimulated = true; rig3.Advance(1.0, 10); hub3.DrawingSimulated = false;
                Check(r, "Q5e", Acts(log3).Count == 1 && hub3.PausedUsOf(log3.Cast) == 0, "drawing after the seed has worked holds nothing (no waiting seed)");
                // the budget's clock: a trigger after cast + 8 s of REAL time still works when 信 held the seed
                var rig4 = Rig.Create(book); var hub4 = HubOf(rig4, new Posture()); rig4.Add("A", 4, 0);
                var log4 = rig4.Cast('감', out _); rig4.Advance(0.5, 10);
                hub4.DrawingSimulated = true; rig4.Advance(3.0, 30); hub4.DrawingSimulated = false; rig4.Advance(6.0, 60);        // t = 9.5 s, past the unheld 8 s
                rig4.Cast('가', out _); rig4.Advance(0.2, 4);
                Check(r, "Q5f", Acts(log4).Count == 1 && Seeds(log4)[0].End == Vol2MaterialEnd.Consumed, "at 9.5 s of real time (8 s unheld) the seed still works: 信 held it 3 s and the budget's clock stood still with it");
            }
            // ---- Q6 ㅓ 검 ----
            {
                var rig = Rig.Create(book); var hub = HubOf(rig, new Posture());
                var gate = new Vol2SeedHitGate(rig.Services.Clock); hub.AttachHits(gate);
                var n4 = rig.Add("N4", 1.9f, -0.2f); var n3 = rig.Add("N3", 1.8f, 0.2f); var n2 = rig.Add("N2", 1.4f, -0.6f); var n1 = rig.Add("N1", 1.0f, 0.5f); rig.Add("Far", 5, 0);
                var log = rig.Cast('검', out _); rig.Advance(0.2, 4);
                var seed = Seeds(log);
                Check(r, "Q6a", seed.Count == 1 && seed[0].Attached && seed[0].Initial == 0m && seed[0].ExpiresUs == log.StartUs + life && Acts(log).Count == 0 && rig.Damage.Requests.Count == 0, "one seed on the body (no place in the world), 8 s; swings touch nothing, nothing is struck");
                rig.Runtime.Swings.Raise(new Vol2SwingEvent(1, new Vector3(0, 1.1f, 0), new Vector3(0, 1.1f, 1.6f), rig.Services.Clock.NowUs, 0));
                Check(r, "Q6b", log.Contacts.Count == 0, "a swing across the enemies creates no contact for a seed (it is not thrown)");
                Check(r, "Q6c", gate.Report(0f).Valid == false && gate.Report(-3f).Valid == false && seed[0].Alive && Acts(log).Count == 0, "a hit that took no HP (a parry success, a dodge: amount 0) is no hit: the seed waits");
                var hit = gate.Report(6f);
                var acts = Acts(log); var refused = log.Contacts.Where(x => x.Kind == "Activation" && !x.BudgetAccepted).ToList();
                Check(r, "Q6d", hit.Valid && Join(Names(acts)) == "N1|N2|N3" && acts.All(x => Eq(x.Amount, act) && x.SwingId == acts[0].SwingId) && refused.Count == 1 && refused[0].TargetName == "N4" && refused[0].BudgetReason == "TargetCap" && !log.Contacts.Any(x => x.TargetName == "Far"),
                    "the hit works the seed on the enemies within 2 m, nearest first: N1 N2 N3 take 1.2 each (one trigger id); N4, a fourth, is refused TargetCap; Far was out of reach");
                var reqs = rig.Damage.Requests.Where(q => q.Cast == log.Cast).ToList();
                Check(r, "Q6e", reqs.Count == 3 && reqs.All(q => Eq(q.Units, Units(Body.Wood, act))) && rig.Status.Requests.Count(q => q.Cast == log.Cast) == 3 && seed[0].End == Vol2MaterialEnd.Consumed && Eq(log.Budget.PaidPerTarget, 3 * act), "three strikes (wood H x 1.2), each body bound once, the seed Consumed, paid 3 x 1.2");
                gate.Report(6f);
                Check(r, "Q6f", Acts(log).Count == 3 && !gate.StaggerExemptPending && gate.ExemptionsGranted == 0, "a second hit works nothing; 검 grants no stagger exemption (only 멈)");
                Check(r, "Q6g", rig.Damage.Requests.All(q => q.Cast == log.Cast) && rig.Damage.Requests.Count == 3 && rig.Ink.Casts == 1, "응보 gives nothing back: the three strikes are all there is - no other damage, no ink, no HP");
                // enemy within 2 m counts by its radius; nothing near: the seed still works (once) and says so
                var rigN = Rig.Create(book); var hubN = HubOf(rigN); var gateN = new Vol2SeedHitGate(rigN.Services.Clock); hubN.AttachHits(gateN); rigN.Add("Far", 5, 0);
                var logN = rigN.Cast('넘', out _); rigN.Advance(0.2, 4); gateN.Report(4f);
                Check(r, "Q6h", Seeds(logN)[0].End == Vol2MaterialEnd.Consumed && Acts(logN).Count == 0 && hubN.RecordsOf(logN.Cast).Any(x => x.Kind == "NoTarget"), "no enemy within 2 m: the seed still works once and is gone (it is not kept for later)");
            }
            // ---- Q7 멈 and the stagger exemption ----
            {
                var rig = Rig.Create(book); var hub = HubOf(rig);
                var gate = new Vol2SeedHitGate(rig.Services.Clock); hub.AttachHits(gate);
                bool seenByStagger = false; int staggerPlays = 0, exemptPlays = 0;
                gate.Confirmed += hit => { seenByStagger = gate.StaggerExemptPending; if (gate.TakeStaggerExemption()) exemptPlays++; else staggerPlays++; };       // a stagger listener that subscribed AFTER the hub
                var n1 = rig.Add("N1", 1.0f, 0.5f); var n2 = rig.Add("N2", 1.4f, -0.6f); var n3 = rig.Add("N3", 1.8f, 0.2f);
                var log = rig.Cast('멈', out _); rig.Advance(0.2, 4);
                gate.Report(5f);
                Check(r, "Q7a", seenByStagger && exemptPlays == 1 && staggerPlays == 0 && gate.ExemptionsGranted == 1 && gate.ExemptionsTaken == 1, "멈: the exemption was decided INSIDE the confirmation - a stagger listener that runs after the hub already sees it, takes it once, and does not play");
                gate.Report(5f);
                Check(r, "Q7b", !seenByStagger && staggerPlays == 1 && exemptPlays == 1 && gate.ExemptionsGranted == 1, "the next hit gets no exemption (the seed is gone): the stagger plays");
                var acts = Acts(log);
                var pushes = rig.Status.Requests.Where(q => q.Cast == log.Cast).ToList();
                Check(r, "Q7c", Join(Names(acts)) == "N1|N2|N3" && acts.All(x => Eq(x.Amount, act)) && pushes.Count == 3 && pushes.All(q => q.Kind == Vol2BodyActionKind.Push) && rig.Damage.Requests.Count(q => q.Cast == log.Cast) == 3 && Eq(rig.Damage.Requests[0].Units, Units(Body.Earth, act)),
                    "멈: the retribution is the earth push (the 흙 솟음) on the three within 2 m, 1.2 each (earth H x 1.2 units)");
                // a Report that nobody exempts leaves the flag down; an exemption not taken is dropped by the next hit
                var rig2 = Rig.Create(book); var hub2 = HubOf(rig2); var gate2 = new Vol2SeedHitGate(rig2.Services.Clock); hub2.AttachHits(gate2); rig2.Add("N", 1, 0);
                rig2.Cast('멈', out _); rig2.Advance(0.2, 4); gate2.Report(3f);
                bool pending = gate2.StaggerExemptPending; gate2.Report(3f);
                Check(r, "Q7d", pending && !gate2.StaggerExemptPending && gate2.ExemptionsTaken == 0 && gate2.ExemptionsGranted == 1, "an exemption nobody took does not carry over to the next hit");
            }
            // ---- Q8 ㅗ 곰 ----
            {
                var rig = Rig.Create(book); var hub = HubOf(rig, new Posture());
                var in3 = rig.Add("In3", 3, 0); rig.Add("Out7.5", 7.5f, 0); rig.Add("Side", 3.5f, 3.5f); var e = rig.Add("E", 7, 0.3f); var late = rig.Add("Late", 7, -1);
                var log = rig.Cast('곰', out _); rig.Advance(1.0, 20);
                var seed = Seeds(log)[0];
                Check(r, "Q8a", seed.Footprint.Kind == Vol2FootprintKind.Fan && seed.Alive && Acts(log).Count == 0 && rig.Damage.Requests.Count == 0, "the seed is the fan; In3 stands in it when it is laid: that is not an entry, nothing works");
                e.MoveTo(At(4f, 0.3f)); rig.Advance(0.05);
                var acts = Acts(log);
                Check(r, "Q8b", Join(Names(acts)) == "In3|E" && acts.Select(x => x.SwingId).Distinct().Count() == 1 && acts.All(x => Eq(x.Amount, act)) && seed.End == Vol2MaterialEnd.Consumed && Eq(log.Budget.PaidPerTarget, 2 * act),
                    "E enters: the seed works on every body in the fan then (In3 and E, 1.2 each, one trigger id) and is Consumed");
                late.MoveTo(At(3.5f, -0.5f)); rig.Advance(0.1);
                Check(r, "Q8c", Acts(log).Count == 2 && !log.Contacts.Any(x => x.TargetName == "Late" || x.TargetName == "Out7.5" || x.TargetName == "Side"), "a later entry does nothing; the bodies outside the fan are never touched");
                bool once = rig.Status.Requests.Count(q => q.Cast == log.Cast) == 2;
                rig.Advance(8.5, 17);
                Check(r, "Q8d", once && log.Finished && log.Status == "Done", "each body acts once; the cast ends when its 8 s are over");
            }
            // ---- Q9 ㅜ 굼 ----
            {
                var rig = Rig.Create(book); var hub = HubOf(rig, new Posture());
                var hole = rig.Add("Hole", 0.5f, 0.3f); var band = rig.Add("Band", 2, 0); var outer = rig.Add("Out", 4.5f, 0); var w = rig.Add("W", 4.2f, 1f); var late = rig.Add("Late", 4.5f, -1f);
                var log = rig.Cast('굼', out _); rig.Advance(1.0, 20);
                var seed = Seeds(log)[0];
                Check(r, "Q9a", seed.Footprint.Kind == Vol2FootprintKind.Ring && seed.Alive && Acts(log).Count == 0, "the seed is the band; Band stands on it when it is laid: not an entry");
                w.MoveTo(At(2f, 1f)); rig.Advance(0.05);
                Check(r, "Q9b", Join(Names(Acts(log))) == "Band|W" && seed.End == Vol2MaterialEnd.Consumed && !log.Contacts.Any(x => x.TargetName == "Hole" || x.TargetName == "Out"), "W crosses onto the band: the seed works on the bodies on the band then (Band, W); the hollow and the outside are never touched");
                late.MoveTo(At(2f, -1f)); rig.Advance(0.1);
                Check(r, "Q9c", Acts(log).Count == 2, "a later crossing does nothing");
                // a dash across the band, and leaving and returning
                var rig2 = Rig.Create(book); HubOf(rig2); var dash = rig2.Add("Dash", 4.5f, 0); var log2 = rig2.Cast('눔', out _); rig2.Advance(0.5, 10);
                dash.MoveTo(At(-4.5f, 0f)); rig2.Advance(0.05);
                Check(r, "Q9d", Join(Names(Acts(log2))) == "Dash", "a dash from one side across the band to the other is an entry (the body's path is judged, not its feet)");
                var rig3 = Rig.Create(book); HubOf(rig3); var on = rig3.Add("On", 2, 0); var log3 = rig3.Cast('숨', out _); rig3.Advance(0.5, 10);
                on.MoveTo(At(5f, 0f)); rig3.Advance(0.05); on.MoveTo(At(5f, 0f)); rig3.Advance(0.05); on.MoveTo(At(2f, 0f)); rig3.Advance(0.05);
                Check(r, "Q9e", Join(Names(Acts(log3))) == "On", "a body that was on the band, leaves and comes back, is an entry");
                var rig4 = Rig.Create(book); var hub4 = HubOf(rig4); hub4.Tuning.InitialOccupantsTrigger = true; rig4.Add("On", 2, 0); var log4 = rig4.Cast('움', out _); rig4.Advance(0.1);
                Check(r, "Q9f", Join(Names(Acts(log4))) == "On", "TE-S5-2 off: with InitialOccupantsTrigger a body already there triggers it at once");
            }
            // ---- Q10 몸 ----
            {
                var rig = Rig.Create(book); HubOf(rig);
                var old = rig.Coord.Materials.Create(new CastId(9100001), '몸', Body.Earth, Vol2MaterialKind.Mark, 0m, life, Vol2Footprint.Disc(At(3, 0), 0.6f), Vol2Faction.Player, Vol2Faction.Player, false, 0, 0, 0, Vol2SeedExecutor.TagGiant);
                var in3 = rig.Add("In3", 3, 0); var e = rig.Add("E", 7, 0);
                var log = rig.Cast('몸', out var dec); rig.Advance(0.5, 10);
                var giant = Seeds(log)[0];
                Check(r, "Q10a", old.End == Vol2MaterialEnd.Replaced && giant.Tag == Vol2SeedExecutor.TagGiant && giant.Alive && rig.Coord.Materials.Alive.Count(m => m.Tag == Vol2SeedExecutor.TagGiant) == 1, "동시 1: the giant that stood is replaced, one giant stands");
                e.MoveTo(At(4f, 0f)); rig.Advance(0.05);
                var acts = Acts(log); var req = rig.Damage.Requests.Where(q => q.Cast == log.Cast).ToList();
                decimal bigAct = n.Get("action.seed.activation") * n.Get("peak.몸.activationMultiplier");
                Check(r, "Q10b", Join(Names(acts)) == "In3|E" && acts.All(x => Eq(x.Amount, bigAct)) && Eq(bigAct, 1.8m) && req.Count == 2 && req.All(q => Eq(q.Structure, 1.8m) && Eq(q.Units, Units(Body.Earth, 1.8m))) && Eq(Units(Body.Earth, 1.8m), H(Body.Earth) * 1.5m),
                    "the giant strikes once on entry: R 1.8 on each body in the fan (the damage entry clips C to min(R x S, 1.5): earth H x 1.5 - an open question for the rules)");
                var rubble = log.Materials.FirstOrDefault(m => m.Tag == Vol2SeedExecutor.TagRubble);
                Check(r, "Q10c", rubble != null && rubble.Kind == Vol2MaterialKind.Residue && rubble.Body == Body.Earth && rubble.Initial == 0m && rubble.LifeUs == 10 * S && rubble.Footprint.Kind == Vol2FootprintKind.Fan && giant.End == Vol2MaterialEnd.Consumed && !log.Finished,
                    "it collapses into an earth material fan: amount 0, 10 s; the cast lives while it stands");
                rig.Advance(10.5, 21);
                Check(r, "Q10d", rubble.End == Vol2MaterialEnd.Expired && log.Finished && log.Status == "Done", "the collapsed fan expires after 10 s and the cast is Done");
                // a second giant replaces the first before it strikes
                var rig2 = Rig.Create(book); HubOf(rig2); rig2.Add("In3", 3, 0);
                var l1 = rig2.Cast('몸', out _); rig2.Advance(0.2, 4); var l2 = rig2.Cast('몸', out _); rig2.Advance(0.2, 4);
                Check(r, "Q10e", Seeds(l1)[0].End == Vol2MaterialEnd.Replaced && Seeds(l2)[0].Alive && rig2.Coord.Materials.Alive.Count(m => m.Tag == Vol2SeedExecutor.TagGiant) == 1, "a new 몸 replaces the standing giant (it never struck)");
            }
            // ---- Q11 맘 ----
            {
                var rig = Rig.Create(book); var posture = new Posture(); HubOf(rig, posture);
                var a = rig.Add("A", 4, 0); var b = rig.Add("B", 6, 0); rig.Coord.BossTest = t => t.Id == b.Id;
                var log = rig.Cast('맘', out _); rig.Advance(0.5, 10);
                rig.Cast('가', out _); rig.Advance(0.3, 6);
                var press = rig.Status.Requests.Where(q => q.Cast == log.Cast).ToList();
                Check(r, "Q11a", press.Count == 1 && press[0].Kind == Vol2BodyActionKind.Bind && press[0].Magnitude == 0f && press[0].DurationUs == 1500000 && n.Get("combat.bind.maxDuration") == 1.5m, "맘: A is pressed to the ground - no movement (x0) for 1.5 s");
                var req = rig.Damage.Requests.Where(q => q.Cast == log.Cast).ToList();
                Check(r, "Q11b", req.Count == 1 && Eq(req[0].Structure, 1.2m) && Eq(req[0].Units, Units(Body.Earth, 1.2m)) && posture.Calls.Count == 1 && posture.Calls[0].letter == '맘', "plus the 1.2 hit (earth H x 1.2) and posture 60 with the letter");
                a.Alive = false; rig.Targets.Remove(a); rig.Cast('가', out _); rig.Advance(0.3, 6);
                var press2 = rig.Status.Requests.Where(q => q.Cast == log.Cast).ToList();
                Check(r, "Q11c", press2.Count == 2 && press2[1].Target.Id == b.Id && press2[1].DurationUs == 750000, "a boss is pressed half as long: 0.75 s");
            }
            // ---- Q12 뭄 ----
            {
                var rig = Rig.Create(book); var hub = HubOf(rig); var field = new FakeField(); rig.Coord.Field = field;
                Vector3 casterPos = Vector3.zero; hub.CasterPositionOverride = () => casterPos;
                var enemy = rig.Add("W", 4.2f, 1f); var inside = rig.Add("Band", 2, 0);
                var log = rig.Cast('뭄', out _); rig.Advance(0.5, 10);
                enemy.MoveTo(At(2f, 1f)); rig.Advance(0.1);
                var seed = Seeds(log)[0];
                Check(r, "Q12a", seed.Alive && Acts(log).Count == 0 && rig.Damage.Requests.Count == 0, "뭄: an enemy crossing the band does NOT work it (unlike the ordinary ring seed)");
                casterPos = At(1f, 0f); rig.Advance(0.05);
                Check(r, "Q12b", seed.Alive && Acts(log).Count == 0, "the caster still in the hollow: nothing");
                casterPos = At(2f, 0f); rig.Advance(0.05);
                var acts = Acts(log); var ground = log.Materials.FirstOrDefault(m => m.Tag == Vol2SeedExecutor.TagGround);
                Check(r, "Q12c", acts.Count == 1 && acts[0].TargetId == Vol2VerbRunBase.WorldTargetId && Eq(acts[0].Amount, act) && !acts[0].DamageRequested && rig.Damage.Requests.Count == 0 && rig.Status.Requests.Count == 0 && seed.End == Vol2MaterialEnd.Consumed,
                    "the caster steps onto the band: one activation booked (1.2) with no body and no damage - a non-damage axis");
                Check(r, "Q12d", ground != null && ground.Kind == Vol2MaterialKind.Residue && ground.Initial == 0m && ground.LifeUs == 8 * S && ground.Footprint.Kind == Vol2FootprintKind.Ring && field.Platforms.Count == 1 && log.FieldNotes.Count == 1 && log.FieldNotes[0].Contains("ground ok"),
                    "the ring hardens: an earth ring material, amount 0, 8 s; the field was asked for the ground and answered");
                long now = rig.Services.Clock.NowUs;
                Check(r, "Q12e", Vol2SeedWorld.BlocksDashOrJump(rig.Coord.Materials, At(1f, 0f), now, 0.4f) && Vol2SeedWorld.BlocksDashOrJump(rig.Coord.Materials, At(2f, 0f), now, 0.4f) && !Vol2SeedWorld.BlocksDashOrJump(rig.Coord.Materials, At(4.5f, 0f), now, 0.4f),
                    "inside the hard ring (and on its band) no dash or jump; outside they are free");
                rig.Advance(8.5, 17);
                Check(r, "Q12f", ground.End == Vol2MaterialEnd.Expired && !Vol2SeedWorld.BlocksDashOrJump(rig.Coord.Materials, At(1f, 0f), rig.Services.Clock.NowUs, 0.4f) && log.Finished && log.Status == "Done", "after 8 s the ground is soft again and the cast is Done");
                // no field port: recorded only
                var rigN = Rig.Create(book); var hubN = HubOf(rigN); Vector3 cp = Vector3.zero; hubN.CasterPositionOverride = () => cp;
                var logN = rigN.Cast('뭄', out _); rigN.Advance(0.2, 4); cp = At(2f, 0f); rigN.Advance(0.05);
                Check(r, "Q12g", logN.FieldNotes.Count == 1 && logN.FieldNotes[0].Contains("no field port"), "without a field port the request is only recorded");
            }
            // ---- Q13 host gone, resets ----
            {
                var rig = Rig.Create(book); var hub = HubOf(rig, new Posture());
                var a = rig.Add("A", 4, 0); var b = rig.Add("B", 6, 0); rig.Add("C", 8, 0);
                var log = rig.Cast('삼', out _); rig.Advance(0.5, 10);
                var seeds = Seeds(log); var sA = seeds.First(m => Mathf.Abs(m.Footprint.Origin.z - 4f) < 0.01f); var sB = seeds.First(m => Mathf.Abs(m.Footprint.Origin.z - 6f) < 0.01f);
                a.Alive = false; b.LifeRevision++; rig.Advance(0.1);
                Check(r, "Q13a", sA.End == Vol2MaterialEnd.HitCancelled && sB.End == Vol2MaterialEnd.HitCancelled && hub.RecordsOf(log.Cast).Count(x => x.Kind == "HostGone") == 2, "a dead host and a reborn host (new life revision) end their seeds HitCancelled");
                a.Alive = true; rig.Cast('가', out _); rig.Advance(0.3, 6);
                Check(r, "Q13b", Acts(log).Count == 0, "a hit on the reborn body works nothing");
                rig.Coord.AbortAll();
                Check(r, "Q13c", log.Status == "Aborted" && Seeds(log).All(m => !m.Alive) && Seeds(log).Last().End == Vol2MaterialEnd.Aborted && hub.RunsListening == 0 && rig.Coord.Materials.Audit().Count == 0, "the scope reset ends the rest Aborted and the hub forgets the run");
                // the hub forgets a run that is over
                var rig2 = Rig.Create(book); var hub2 = HubOf(rig2); rig2.Add("N", 1, 0);
                var l2 = rig2.Cast('엄', out _); rig2.Advance(0.1);
                int during = hub2.RunsListening; rig2.Advance(8.5, 17);
                Check(r, "Q13d", during == 1 && l2.Finished && hub2.RunsListening == 0, "a run is listened to while it has a seed and forgotten when the cast is Done");
            }
            // ---- Q14 the real kit ----
            {
                var rig = Rig.Create(book);
                var def = new Vol2DefenceRig(made, book, 1f);
                var e1 = Vol2DefenceFixtureSupport.NewEnemy(made, "Q14_e1"); var e2 = Vol2DefenceFixtureSupport.NewEnemy(made, "Q14_e2");
                e1.transform.position = At(4f, 0f); e2.transform.position = At(6f, 0f);
                var t1 = new Vol2EnemyTarget(e1, 1); var t2 = new Vol2EnemyTarget(e2, 2);
                rig.Coord.Registry.Synthetic = () => new IVol2Target[] { t1, t2 };
                void Tick(double seconds) { for (int i = 0; i < 10; i++) { t1.Refresh(); t2.Refresh(); rig.Now += seconds / 10; rig.Coord.Tick((float)(seconds / 10)); } }
                var player = def.Player;
                t1.Refresh(); t2.Refresh();
                var hub = Vol2SeedWiring.Bind(rig.Coord, def.Kit, player);
                Check(r, "Q14a", hub != null && hub.Gather == def.Kit.Gather && hub.Hits != null && hub.Hits.Attached && hub.ActiveSeedPosture != null, "Vol2SeedWiring.Bind hooks the kit's 갈무리, the posture bridge and the player's hits in one call");
                Vol2SeedWiring.Bind(rig.Coord, def.Kit, player);
                var log = rig.Cast('감', out _); Tick(0.5);
                var seeds = Seeds(log);
                var carried = def.Kit.Gather.CarryProvider != null ? def.Kit.Gather.CarryProvider().ToList() : new List<NoInkSwingV3.Carry>();
                Check(r, "Q14b", seeds.Count == 2 && carried.Count == 2 && carried.All(c => c.Seed && c.CastId == log.Cast.Value && c.Letter == '감') && Vol2SeedExecutor.HubOf(rig.Coord) == hub, "two seeds planted on the real bodies; the gather service's carry lists exactly those two (binding twice did not chain twice)");
                var rec = def.Kit.Gather.OnSwingFirstHit(e1, new SwingId(1), e1);
                Tick(0.05);
                var acts = Acts(log);
                Check(r, "Q14c", rec.Accepted && rec.Reason == "Gathered" && Join(Names(acts)) == "Q14_e1" && def.Kit.Posture.PostureOf(e1) == 60m && def.Kit.Posture.PostureOf(e2) == 0m && seeds.Count(m => m.End == Vol2MaterialEnd.Consumed) == 1,
                    "the real 갈무리 swing on e1 works e1's seed (Gathered contact -> hub), and the real posture bridge shows 60 on e1, 0 on e2");
                var rec2 = def.Kit.Gather.CarryProvider().ToList();
                Check(r, "Q14d", rec2.Count == 1 && rec2[0].MaterialId == seeds.First(m => m.Alive).Id.Value, "afterwards the carry lists only the seed that still waits");
                // the real player feeds the gate
                var logA = rig.Cast('멈', out _); Tick(0.2);
                float hp = player.Hp01 * player.MaxHp;
                bool took = player.TakeAttackDamage(7f, IncomingDamageKind.Melee, e1);
                Check(r, "Q14e", took && hub.Hits.Reported == 1 && Seeds(logA)[0].End == Vol2MaterialEnd.Consumed && hub.Hits.ExemptionsGranted == 1 && hub.Hits.StaggerExemptPending && player.Hp01 * player.MaxHp < hp - 6.9f,
                    "the real PlayerVitals hit works the 멈 seed (Damaged -> gate -> hub) and the exemption is pending; the player still lost the HP");
                Check(r, "Q14f", !player.TakeAttackDamage(0f, IncomingDamageKind.Melee, e1) && hub.Hits.Reported == 1, "a hit that takes nothing raises nothing");
            }
        }
    }
}
