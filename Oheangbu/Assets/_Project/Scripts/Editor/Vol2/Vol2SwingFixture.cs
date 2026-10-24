using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using VContainer;
using static Oheangbu.EditorTools.Vol2.Vol2ShapeFixtureKit;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 9 edit-mode fixture of the vol.2 swing driver (D308-150 V2): the contact window read from the clip, the swept capsule, the per-swing dedupe, the
    /// exactly-once 갈무리 report, the click's gate, and the ㅓ attachments taking REAL contacts from it (A5 first / second contact, 것, 섯 with the TE-S6-11 cap, the parry
    /// window). No scene, no Play: the swing's animation is a scripted source (the production source needs the player's rig, the lab and the game have that). Queue:
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2SwingFixture Run "all"
    ///   W1-W4  window: [apex - 0.15, apex + 0.20] of CLIP time, clamped to the clip; no apex marker -> 25 % .. 70 % of the clip; one sweep per frame inside, none outside
    ///   C1-C4  a swing cut before its window is never a swing; a window that passed in one long frame still sweeps once; a clip that ended on the frame it passed the window sweeps once; source lost
    ///   G1-G4  sweep: a tip that crosses a body between two frames hits it (and does not when the sweep is off); capsule radius = the attachment frame's (TE-S3-2) to the centimetre; plane only; the first enemy = nearest the axis, ties by id
    ///   D1-D3  one SwingId per swing on every raised event; one first-hit per swing; ids unique; a miss is reported once at the window's end, never both
    ///   E1     substeps: 1.0 m of tip travel = 4 events, 4.0 m is capped at 6, the last event is the frame's pose
    ///   K1-K2  the click's gate: a swing in progress and the rules' 1.1 s cooldown (from the last REPORT) refuse; the gate off lets the swing go; an external swing opens one too
    ///   A1-A5  the attachments: 겅 (A5) first contact, second contact on another swing, nothing on the third; 것 three bodies + TargetCap; 거 parry window closes the contacts; 섯 TE-S6-11 cap
    ///   H1-H2  the host: the click reaches the driver through HarvestAction.Pressed and the legacy pull is switched off only while a rig exists
    ///   L1     Legacy308: no shape runtime, no driver, nothing registered, HarvestAction untouched
    /// Expected values are written here from NumbersV3, the table and the tuning's stated defaults - never read back from a run.</summary>
    public static class Vol2SwingFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-swing-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-swing-fixture: editor busy (play / compile / import)";
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

        /// <summary>The scripted animation. Clip numbers are what the test sets frame by frame.</summary>
        sealed class FakeSource : IVol2SwingSource
        {
            public int Serial, Begins; public bool Swinging, Available = true, CanBegin = true;
            public float Clip, Apex = 0.4f, Length = 1.0f; public Vector3 Sleeve, Tip;
            public bool TrySample(out Vol2SwingSample s) { s = new Vol2SwingSample { Serial = Serial, Swinging = Swinging, ClipSeconds = Clip, ApexSeconds = Apex, LengthSeconds = Length }; return Available; }
            public bool TryPose(out Vector3 sleeve, out Vector3 tip) { sleeve = Sleeve; tip = Tip; return Available; }
            public bool Begin() { if (!CanBegin) return false; Begins++; Serial++; Swinging = true; Clip = 0f; return true; }
        }

        sealed class Kit
        {
            public Rig Rig; public Vol2SwingDriver Driver; public FakeSource Src = new FakeSource(); public Vol2RecordingGatherPort Rec = new Vol2RecordingGatherPort();
            public readonly List<Vol2SwingEvent> Events = new List<Vol2SwingEvent>();
            public static Kit Create(Vol2Rulebook book, bool gate = false, Vol2SwingTuning tuning = null)
            {
                var k = new Kit { Rig = Rig.Create(book) };
                tuning = tuning ?? new Vol2SwingTuning(); tuning.GateByCooldown = gate;
                k.Driver = new Vol2SwingDriver(k.Rig.Coord, tuning) { Source = k.Src, Gather = k.Rec };
                k.Rig.Runtime.Swing = k.Driver;
                k.Rig.Runtime.Swings.Contact += e => k.Events.Add(e);
                return k;
            }
            /// <summary>One rendered frame: the scope clock moves, the animation says where it is, the driver looks.</summary>
            public void Frame(float clip, Vector3 sleeve, Vector3 tip, double dt = 0.02)
            {
                Rig.Advance(dt); Src.Clip = clip; Src.Sleeve = sleeve; Src.Tip = tip; Driver.Update();
            }
            /// <summary>A whole swing: the click, one wind-up frame, three frames inside the window, one past it.</summary>
            public bool Swing(Vector3 sleeve, Vector3 tip)
            {
                Src.Sleeve = sleeve; Src.Tip = tip;
                bool ok = Driver.TryBeginSwing();
                Frame(0.02f, sleeve, tip); Frame(0.26f, sleeve, tip); Frame(0.30f, sleeve, tip); Frame(0.34f, sleeve, tip); Frame(0.70f, sleeve, tip);
                return ok;
            }
        }

        static readonly Vector3 Chest = new Vector3(0f, 1.1f, 0f), Reach = new Vector3(0f, 1.1f, 1.6f);

        static Vol2Material Plant(Rig rig, long cast, string tag, Body body, long faction, float ahead, float side)
        {
            long life = RuleTypesV3.Microseconds(rig.Numbers.Get("timing.g.life"));
            return rig.Coord.Materials.Create(new CastId(cast), '가', body, Vol2MaterialKind.Residue, 0.9m, life, Vol2Footprint.Disc(At(ahead, side), 0.6f), faction == Vol2Faction.Enemy ? 2 : 1, faction, true, rig.Services.Clock.NowUs, 0, 0, tag, false);
        }

        static void Cases(Report r)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var n = book.Numbers;
            long cooldown = RuleTypesV3.Microseconds(n.Get("combat.noInk.cooldown"));
            var defaults = new Vol2SwingTuning();
            Check(r, "F1", cooldown == 1100000 && defaults.IsValid && defaults.ActiveBeforeApex == 0.15f && defaults.ActiveAfterApex == 0.20f && defaults.NoApexStartFraction == 0.25f && defaults.NoApexEndFraction == 0.70f && defaults.BareSwingState == "SwingSweep",
                "the rules' cooldown is 1.1 s (combat.noInk.cooldown); the tuning stands at its stated defaults (window apex -0.15 / +0.20 s, 25 % .. 70 % without an apex, SwingSweep)");

            // ---- W1 window around the apex ----
            {
                var k = Kit.Create(book); k.Src.Apex = 0.4f; k.Src.Length = 1.0f;
                k.Driver.TryBeginSwing();
                float[] clips = new float[25]; for (int i = 0; i < clips.Length; i++) clips[i] = 0.02f + 0.04f * i;      // 0.02 .. 0.98
                var perFrame = new List<(float clip, int events)>();
                foreach (float c in clips) { int before = k.Events.Count; k.Frame(c, Chest, Reach); perFrame.Add((c, k.Events.Count - before)); }
                var inside = perFrame.Where(p => p.events > 0).Select(p => p.clip).ToList();
                bool edges = inside.Count > 0 && inside.Min() >= 0.25f && inside.Max() <= 0.60f && inside.Count == 9 && perFrame.Where(p => p.clip < 0.25f || p.clip > 0.60f).All(p => p.events == 0);
                Check(r, "W1a", edges && k.Driver.FramesSwept == 9 && k.Driver.RaisedEvents == 9 && Math.Abs(k.Driver.WindowStartClip - 0.25f) < 1e-4f && Math.Abs(k.Driver.WindowEndClip - 0.60f) < 1e-4f,
                    "apex 0.4 s of a 1.0 s clip: events only on the 9 frames with clip in [0.25, 0.60] (" + inside.Count + " seen " + (inside.Count > 0 ? inside.Min().ToString("0.00") + ".." + inside.Max().ToString("0.00") : "-") + ")");
                Check(r, "W1b", k.Driver.Swings == 1 && k.Driver.Misses == 1 && k.Driver.FirstHits == 0 && k.Rec.Misses.Count == 1 && k.Rec.Hits.Count == 0 && k.Driver.Last.Why == "Window" && !k.Driver.InSwing && k.Driver.Cancelled == 0,
                    "no body: the swing closes once at the window's end (Why=Window) and is booked as ONE miss, no first hit");
            }
            // ---- W2 no apex marker ----
            {
                var k = Kit.Create(book); k.Src.Apex = 0f; k.Src.Length = 1.0f;
                k.Driver.TryBeginSwing();
                int inWindow = 0; for (int i = 0; i < 25; i++) { int before = k.Events.Count; k.Frame(0.03f + 0.04f * i, Chest, Reach); if (k.Events.Count > before) inWindow++; }
                Check(r, "W2", inWindow == 11 && Math.Abs(k.Driver.WindowStartClip - 0.25f) < 1e-4f && Math.Abs(k.Driver.WindowEndClip - 0.70f) < 1e-4f && k.Driver.Misses == 1,
                    "a state with no apex marker: the window is 25 % .. 70 % of its clip (0.25 .. 0.70 s of 1.0 s): " + inWindow + " frames");
            }
            // ---- W3 the window is clamped to the clip ----
            {
                var k = Kit.Create(book); k.Src.Apex = 0.9f; k.Src.Length = 1.0f;
                k.Driver.TryBeginSwing();
                int inWindow = 0; for (int i = 0; i < 25; i++) { int before = k.Events.Count; k.Frame(0.02f + 0.04f * i, Chest, Reach); if (k.Events.Count > before) inWindow++; }
                Check(r, "W3", inWindow == 6 && Math.Abs(k.Driver.WindowStartClip - 0.75f) < 1e-4f && Math.Abs(k.Driver.WindowEndClip - 1.0f) < 1e-4f,
                    "apex 0.9 s: the window is [0.75, 1.0] - it never runs past the clip (+0.20 would be 1.10)");
            }
            // ---- W4 the window follows the clip: a different clip, a different window ----
            {
                var k = Kit.Create(book); k.Src.Apex = 0.65f; k.Src.Length = 1.4f;       // SwingArc: 1.4 s clip, apex 0.65
                k.Driver.TryBeginSwing(); k.Frame(0.02f, Chest, Reach);
                Check(r, "W4", Math.Abs(k.Driver.WindowStartClip - 0.50f) < 1e-4f && Math.Abs(k.Driver.WindowEndClip - 0.85f) < 1e-4f, "SwingArc (1.4 s clip, apex 0.65 s): the window is [0.50, 0.85] - read from the clip, not a fixed delay");
            }

            // ---- C cuts and long frames ----
            {
                var k = Kit.Create(book); k.Driver.TryBeginSwing();
                k.Frame(0.02f, Chest, Reach); k.Frame(0.06f, Chest, Reach);
                k.Src.Swinging = false; k.Frame(0.10f, Chest, Reach);          // a dodge cuts the motion before its window
                Check(r, "C1", k.Driver.Cancelled == 1 && k.Driver.Swings == 1 && k.Driver.Reports == 0 && k.Rec.Hits.Count == 0 && k.Rec.Misses.Count == 0 && k.Events.Count == 0 && k.Driver.LastReportUs < 0 && k.Driver.Last.Why.Contains("before the window"),
                    "a swing cut before its window opened was never a swing: no event, no report, no cooldown started");
            }
            {
                var k = Kit.Create(book); k.Rig.Add("T", 1.0f, 0f);
                k.Driver.TryBeginSwing(); k.Frame(0.02f, Chest, Reach);
                k.Frame(0.90f, Chest, Reach);                                   // one long frame: the whole window passed between two frames
                Check(r, "C2", k.Driver.FramesSwept == 1 && k.Driver.Swings == 1 && k.Rec.Hits.Count == 1 && !k.Driver.InSwing, "a window that passed inside ONE long frame is still swept once (the brush crossed it): the body is hit, the swing is closed");
            }
            {
                var k = Kit.Create(book); k.Rig.Add("T", 1.0f, 0f);
                k.Driver.TryBeginSwing(); k.Frame(0.02f, Chest, Reach); k.Frame(0.06f, Chest, Reach);
                k.Src.Swinging = false; k.Frame(0.70f, Chest, Reach);          // the clip ended on the very frame it passed the window
                Check(r, "C3", k.Driver.FramesSwept == 1 && k.Rec.Hits.Count == 1 && k.Driver.Cancelled == 0, "a clip that ended on the frame it passed the window: one sweep from the last pose, then closed - not mistaken for a cut");
            }
            {
                var k = Kit.Create(book); k.Driver.TryBeginSwing(); k.Frame(0.02f, Chest, Reach); k.Frame(0.30f, Chest, Reach);
                k.Src.Available = false; k.Driver.Update();
                Check(r, "C4", !k.Driver.InSwing && k.Driver.Swings == 1 && k.Rec.Misses.Count + k.Rec.Hits.Count <= 1, "the rig going away mid-swing closes the swing (no stuck state)");
                k.Src.Available = true;
            }

            // ---- G sweep geometry ----
            {
                // the tip passes from the left of a body to its right between two frames: neither end pose touches it, the sweep does
                var k = Kit.Create(book); var t = k.Rig.Add("T", 1.5f, 0f);
                Vector3 left = new Vector3(-2.0f, 1.1f, 1.5f), right = new Vector3(2.0f, 1.1f, 1.5f);
                bool endsMiss = !Vol2ShapeGeometry.SwingHit(Chest, left, 0.45f, t.Position, t.Radius) && !Vol2ShapeGeometry.SwingHit(Chest, right, 0.45f, t.Position, t.Radius);
                k.Driver.TryBeginSwing(); k.Frame(0.02f, Chest, left); k.Frame(0.26f, Chest, right); k.Frame(0.30f, Chest, right); k.Frame(0.70f, Chest, right);
                Check(r, "G1a", endsMiss && k.Rec.Hits.Count == 1 && k.Rec.Hits[0].target == "T" && k.Rec.Misses.Count == 0 && k.Events.Count == 7, "a tip crossing a body between two frames (4 m in one step, 6 substeps + 1 + ...): neither pose touches it, the sweep hits it, once (events " + k.Events.Count + ")");
                var off = new Vol2SwingTuning { MaxSubstepsPerFrame = 1 };
                var k2 = Kit.Create(book, false, off); k2.Rig.Add("T", 1.5f, 0f);
                k2.Driver.TryBeginSwing(); k2.Frame(0.02f, Chest, left); k2.Frame(0.26f, Chest, right); k2.Frame(0.30f, Chest, right); k2.Frame(0.70f, Chest, right);
                Check(r, "G1b", k2.Rec.Hits.Count == 0 && k2.Rec.Misses.Count == 1, "the same swing with the sweep off (one step per frame) tunnels through - so the sweep is what catches it");
            }
            {
                // radius: the attachment frame's own SwingRadius (TE-S3-2) + the body's, to the centimetre
                float rad = defaults.Radius(null), body = 0.4f;
                var kr = Kit.Create(book);
                Check(r, "G2a", Math.Abs(defaults.Radius(kr.Rig.Coord.Tuning) - kr.Rig.Coord.Tuning.SwingRadius) < 1e-6f && Math.Abs(kr.Rig.Coord.Tuning.SwingRadius - 0.45f) < 1e-6f, "the capsule radius is the attachment frame's SwingRadius (TE-S3-2, 0.45) - one number for the first hit and the ㅓ contact");
                float lateralIn = rad + body - 0.01f, lateralOut = rad + body + 0.01f;
                var k = Kit.Create(book); var t = k.Rig.Add("T", 0.8f, lateralIn);
                k.Swing(Chest, Reach); bool hitIn = k.Rec.Hits.Count == 1;
                t.MoveTo(At(0.8f, lateralOut)); k.Swing(Chest, Reach);
                Check(r, "G2b", hitIn && k.Rec.Hits.Count == 1 && k.Rec.Misses.Count == 1, "a body 1 cm inside radius + body radius is hit, 1 cm outside is not (0.85 m from the axis)");
                var small = new Vol2SwingTuning { CapsuleRadius = 0.20f };
                var ks = Kit.Create(book, false, small); var ts = ks.Rig.Add("T", 0.8f, 0.65f);       // inside 0.45 + 0.4, outside 0.20 + 0.4
                ks.Swing(Chest, Reach);
                Check(r, "G2c", ks.Rec.Hits.Count == 0 && ks.Rec.Misses.Count == 1 && Math.Abs(small.Radius(ks.Rig.Coord.Tuning) - 0.20f) < 1e-6f, "a positive CapsuleRadius overrides the frame's for the first-hit test");
            }
            {
                var k = Kit.Create(book); var t = k.Rig.Add("T", 0.8f, 0.2f); t.Position = new Vector3(0.2f, 6f, 0.8f); t.Previous = t.Position;
                k.Swing(Chest, Reach);
                Check(r, "G3", k.Rec.Hits.Count == 1, "the contact is judged on the plane (x, z) like every vol.2 shape: a body 6 m above the capsule's height is still hit");
            }
            {
                var k = Kit.Create(book); var t1 = k.Rig.Add("T1", 0.8f, 0.3f); var t2 = k.Rig.Add("T2", 0.8f, -0.3f);
                k.Swing(Chest, Reach);
                bool tie = k.Rec.Hits.Count == 1 && k.Rec.Hits[0].target == "T1";
                var t3 = k.Rig.Add("T3", 1.0f, 0.1f);
                k.Swing(Chest, Reach);
                Check(r, "G4", tie && k.Rec.Hits.Count == 2 && k.Rec.Hits[1].target == "T3", "two bodies at the same distance: the lower id (T1); a body nearer the axis (T3, 0.1 m) wins the next swing - the first enemy is deterministic");
            }

            // ---- D ids and exactly-once ----
            {
                var k = Kit.Create(book); k.Rig.Add("T", 0.8f, 0f);
                k.Swing(Chest, Reach);
                var ids = k.Events.Select(e => e.SwingId).Distinct().ToList();
                bool indices = k.Events.Select((e, i) => e.ColliderIndex == i).All(b => b);
                bool poses = k.Events.All(e => (e.Sleeve - Chest).sqrMagnitude < 1e-8f && (e.Tip - Reach).sqrMagnitude < 1e-8f);
                bool timed = k.Events.All(e => e.NowUs > 0) && k.Events.Zip(k.Events.Skip(1), (a, b) => b.NowUs >= a.NowUs).All(x => x);
                Check(r, "D1a", ids.Count == 1 && ids[0] == k.Rec.Hits[0].swing && ids[0] == k.Driver.Last.SwingId && k.Events.Count == 3 && indices && poses && timed,
                    "every raised event of a swing carries the swing's one SwingId (also the one the 갈무리 was given), the pose the animation gave, the scope clock, a running index (3 window frames = 3 events)");
                Check(r, "D1b", k.Rec.Hits.Count == 1 && k.Rec.Misses.Count == 0 && k.Driver.FirstHits == 1 && k.Driver.Reports == 1, "three frames over the body: ONE first-hit, no miss");
            }
            {
                var k = Kit.Create(book); k.Rig.Add("T", 0.8f, 0f);
                for (int i = 0; i < 3; i++) { k.Swing(Chest, Reach); k.Rig.Advance(0.3); }
                var hitIds = k.Rec.Hits.Select(h => h.swing).ToList();
                bool increasing = hitIds.Zip(hitIds.Skip(1), (a, b) => b > a).All(x => x);
                Check(r, "D2", k.Driver.Swings == 3 && hitIds.Count == 3 && hitIds.Distinct().Count() == 3 && increasing && k.Events.Select(e => e.SwingId).Distinct().Count() == 3 && k.Driver.Reports == 3,
                    "three swings: three SwingIds (issued by the scope's issuer, increasing), three first-hits - each swing reports on its own");
            }
            {
                var k = Kit.Create(book);
                k.Swing(Chest, Reach); k.Rig.Add("T", 0.8f, 0f); k.Swing(Chest, Reach);
                Check(r, "D3", k.Driver.Reports == 2 && k.Rec.Misses.Count == 1 && k.Rec.Hits.Count == 1 && k.Rec.Misses[0] != k.Rec.Hits[0].swing, "an air swing and a body swing: one miss and one hit, never both for one swing, never neither");
            }

            // ---- E substeps ----
            {
                var k = Kit.Create(book);
                Vector3 tip0 = new Vector3(0f, 1.1f, 0.6f), tip1 = new Vector3(0f, 1.1f, 1.6f);
                k.Driver.TryBeginSwing(); k.Frame(0.02f, Chest, tip0);
                int before = k.Events.Count; k.Frame(0.26f, Chest, tip1);
                int n1 = k.Events.Count - before; var last = k.Events[k.Events.Count - 1]; var first = k.Events[before];
                Check(r, "E1a", n1 == 4 && (last.Tip - tip1).sqrMagnitude < 1e-8f && Math.Abs(first.Tip.z - 0.85f) < 1e-4f, "1.0 m of tip travel in one frame = ceil(1.0 / 0.30) = 4 events; the first is a quarter of the way, the last is the frame's pose (" + n1 + ")");
                var k2 = Kit.Create(book);
                k2.Driver.TryBeginSwing(); k2.Frame(0.02f, Chest, new Vector3(0f, 1.1f, -2f));
                int b2 = k2.Events.Count; k2.Frame(0.26f, Chest, new Vector3(0f, 1.1f, 2f));
                Check(r, "E1b", k2.Events.Count - b2 == 6, "4.0 m of travel is capped at MaxSubstepsPerFrame = 6 events (" + (k2.Events.Count - b2) + ")");
            }

            // ---- K the click ----
            {
                var k = Kit.Create(book, true); k.Rig.Add("T", 0.8f, 0f);
                bool first = k.Driver.TryBeginSwing();
                bool during = k.Driver.TryBeginSwing();
                k.Frame(0.02f, Chest, Reach); k.Frame(0.26f, Chest, Reach); k.Frame(0.70f, Chest, Reach);       // hit at 0.26, closed at 0.70
                long reported = k.Driver.LastReportUs;
                bool soon = k.Driver.TryBeginSwing();
                k.Rig.Now = (reported + cooldown - 10000) / 1000000.0; bool justBefore = k.Driver.TryBeginSwing();
                k.Rig.Now = (reported + cooldown + 1000) / 1000000.0; bool atCooldown = k.Driver.TryBeginSwing();
                Check(r, "K1", first && !during && !soon && !justBefore && atCooldown && k.Driver.Refused == 3 && k.Src.Begins == 2 && reported > 0,
                    "the click: a swing in progress refuses, so does one inside 1.1 s of the last REPORT (10 ms short still refuses), 1 ms past 1.1 s it goes; a refused click never starts the animation (Begins " + k.Src.Begins + ")");
                var k2 = Kit.Create(book, true); k2.Driver.TryBeginSwing(); k2.Frame(0.02f, Chest, Reach); k2.Frame(0.26f, Chest, Reach); k2.Frame(0.70f, Chest, Reach);
                long missAt = k2.Driver.LastReportUs;
                k2.Rig.Now = (missAt + cooldown - 10000) / 1000000.0; bool miss1 = k2.Driver.TryBeginSwing();
                Check(r, "K1b", missAt > 0 && !miss1, "a swing that missed starts the cooldown too (the rules book a miss as a swing): cooldown from the miss report");
                var k3 = Kit.Create(book, true);
                k3.Src.CanBegin = false; bool noBegin = k3.Driver.TryBeginSwing(); k3.Driver.Enabled = false; k3.Src.CanBegin = true; bool off = k3.Driver.TryBeginSwing(); k3.Driver.Enabled = true; k3.Driver.Source = null; bool nosrc = k3.Driver.TryBeginSwing();
                Check(r, "K1c", !noBegin && !off && !nosrc && k3.Driver.Swings == 0 && k3.Driver.Refused == 3, "no animation to play / driver off / no source: the click is refused, nothing opens");
            }
            {
                // a swing that begins without a click (any other source of the motion) opens a swing too; a new motion replaces an unfinished one
                var k = Kit.Create(book); k.Rig.Add("T", 0.8f, 0f);
                k.Src.Serial = 5; k.Src.Swinging = true; k.Frame(0.02f, Chest, Reach); k.Frame(0.26f, Chest, Reach);
                long first = k.Driver.CurrentSwing;
                k.Src.Serial = 6; k.Frame(0.02f, Chest, Reach);                      // a new motion starts while the first is open
                Check(r, "K2", k.Driver.Swings == 2 && first > 0 && k.Driver.CurrentSwing != first && k.Rec.Hits.Count == 1 && k.Rec.Hits[0].swing == first,
                    "a motion that no click started opens a swing as well; a new motion replaces an unfinished one (the first is closed and booked)");
                var k2 = Kit.Create(book); k2.Src.Serial = 3; k2.Src.Swinging = false; k2.Frame(0.1f, Chest, Reach);
                Check(r, "K2b", k2.Driver.Swings == 0, "a state without a strike in it (an idle, a hold) is not a swing");
                // two sources count their motions apart: swapping the source must not make the new one's first motion look like the old one's last
                var k3 = Kit.Create(book); k3.Swing(Chest, Reach);
                var other = new FakeSource(); k3.Driver.Source = other; other.Sleeve = Chest; other.Tip = Reach;
                bool opened = k3.Driver.TryBeginSwing();         // other.Serial becomes 1, the same number the first source ended on
                Check(r, "K2c", opened && k3.Driver.Swings == 2 && k3.Driver.InSwing, "swapping the source forgets the old serial: the new source's first motion (serial 1 again) opens a swing");
            }

            // ---- A the attachments take REAL contacts ----
            {
                // 겅 (A5, slice 7): the first contact is the instant, the swing 0.4 s later is the second contact, the third pays nothing
                var k = Kit.Create(book); k.Rig.Add("W", 0.5f, 0.3f);
                var log = k.Rig.Cast('겅', out _); k.Rig.Advance(0.05);
                k.Swing(Chest, Reach); long swing1 = k.Driver.Last.SwingId; bool one = Names(log, "Instant").SequenceEqual(new[] { "W" });
                k.Rig.Advance(0.4, 4); k.Swing(Chest, Reach); long swing2 = k.Driver.Last.SwingId; bool two = Names(log, "Second").SequenceEqual(new[] { "W" });
                k.Rig.Advance(0.3, 3); k.Swing(Chest, Reach);
                var ins = Of(log, "Instant")[0]; var sec = Of(log, "Second")[0];
                long secondDelay = RuleTypesV3.Microseconds(n.Get("timing.eo.secondDelay"));
                Check(r, "A1", one && two && log.Contacts.Count(c => c.TargetName == "W" && c.BudgetAccepted) == 2 && ins.SwingId == swing1 && sec.SwingId == swing2 && swing1 != swing2 && sec.NowUs - ins.NowUs >= secondDelay,
                    "겅 (A5): the driver's first swing is the instant, the second (another SwingId, >= 0.3 s later) the second contact, the third swing pays nothing - the budget sees real contacts");
                Check(r, "A1b", log.SwingReports >= 9 && log.SwingDuplicates >= 6 && k.Driver.Swings == 3, "the attachment received every swept event of the three swings (" + log.SwingReports + " reports, " + log.SwingDuplicates + " of them repeats of a body already contacted)");
            }
            {
                // 것: three bodies at most (TargetCap), the second contact on swing 2, swing 3 pays nothing
                var k = Kit.Create(book);
                k.Rig.Add("A", 0.8f, 0); k.Rig.Add("B", 1.0f, 0.3f); k.Rig.Add("C", 1.2f, -0.3f); k.Rig.Add("D", 1.4f, 0); k.Rig.Add("E", 3.5f, 0);
                var log = k.Rig.Cast('것', out _); k.Rig.Advance(0.05);
                k.Swing(Chest, Reach); k.Rig.Advance(0.4); k.Swing(Chest, Reach); k.Rig.Advance(0.25); k.Swing(Chest, Reach);
                Check(r, "A2", Names(log, "Instant").SequenceEqual(new[] { "A", "B", "C" }) && Names(log, "Second").SequenceEqual(new[] { "A", "B", "C" }) && log.Contacts.Where(c => c.TargetName == "D").All(c => !c.BudgetAccepted && c.BudgetReason == "TargetCap") && !log.Contacts.Any(c => c.TargetName == "E"),
                    "것: swing 1 touches A B C (D refused TargetCap), swing 2 is their second contact, swing 3 pays nothing; E is out of reach - the same table as with hand-raised swings");
                Check(r, "A2b", k.Rec.Hits.Count == 3 && k.Rec.Hits.Select(h => h.target).Distinct().Count() == 1 && k.Rec.Hits[0].target == "A", "and the 갈무리 saw each swing's first enemy once (A: nearest the axis)");
            }
            {
                // 거 (parry, 0.5 s): the window takes the swing's contact; after it nothing
                var k = Kit.Create(book); k.Rig.Add("N", 0.8f, 0f);
                var log = k.Rig.Cast('거', out _);
                k.Swing(Chest, Reach); int inWindow = log.Contacts.Count;
                k.Rig.Advance(0.6, 6); k.Swing(Chest, Reach);
                Check(r, "A3", log.ParryWindowOpened && inWindow == 1 && log.Contacts.Count == 1 && log.Contacts.All(c => !c.BudgetAccepted && !c.DamageRequested), "거 (parry window 0.5 s): the swing inside the window is contacted once without any effect; the swing after the window adds nothing");
            }
            {
                // 섯, TE-S6-11: the front / rear material cut runs on the first two swings only
                Vol2Material[] RunSeot(int cap, out Vol2SplitCastData data)
                {
                    var k = Kit.Create(book); var ex = k.Rig.Coord.Verbs.TryGet(Verb.Split, out var e0) ? e0 as Vol2SplitExecutor : null;
                    ex.Tuning.SeotSwings = cap;
                    var m1 = Plant(k.Rig, 9001, "M1", Body.Wood, Vol2Faction.Enemy, 1.2f, 0);                     // in the first swing's reach
                    var m2 = Plant(k.Rig, 9002, "M2", Body.Wood, Vol2Faction.Enemy, 1.2f + 8f, 0);                // in the second's (its sleeve is 8 m further on)
                    var m3 = Plant(k.Rig, 9003, "M3", Body.Wood, Vol2Faction.Enemy, 1.2f + 16f, 0);               // in the third's
                    var log = k.Rig.Cast('섯', out _); k.Rig.Advance(0.05);
                    k.Swing(Chest, Reach); k.Rig.Advance(0.4);
                    k.Swing(Chest + Vector3.forward * 8f, Reach + Vector3.forward * 8f); k.Rig.Advance(0.4);
                    k.Swing(Chest + Vector3.forward * 16f, Reach + Vector3.forward * 16f);
                    data = ex.DataOf(log.Cast);
                    return new[] { m1, m2, m3 };
                }
                var capped = RunSeot(2, out var dataCap);
                Check(r, "A4a", capped[0].Splits == 1 && capped[1].Splits == 1 && capped[2].Splits == 0 && dataCap.SwingsCut == 2 && dataCap.SwingsCapped == 1 && dataCap.Splits.Count == 2,
                    "섯 (TE-S6-11): three swings, each over a material of its own - the first two cut theirs, the third cuts nothing (cut with " + dataCap.SwingsCut + ", refused " + dataCap.SwingsCapped + ")");
                var open = RunSeot(0, out var dataOpen);
                Check(r, "A4b", open[2].Splits == 1 && dataOpen.SwingsCapped == 0 && dataOpen.Splits.Count == 3, "and it is the cap that stops it: with SeotSwings = 0 (the old behaviour) the third swing cuts its material too");
                // several events of ONE swing are one swing for the cap
                var k = Kit.Create(book); var ex = k.Rig.Coord.Verbs.TryGet(Verb.Split, out var e1) ? e1 as Vol2SplitExecutor : null;
                var mA = Plant(k.Rig, 9101, "MA", Body.Wood, Vol2Faction.Enemy, 1.2f, 0);
                var log2 = k.Rig.Cast('섯', out _); k.Rig.Advance(0.05);
                k.Driver.TryBeginSwing(); k.Frame(0.02f, Chest, Reach); for (int i = 0; i < 6; i++) k.Frame(0.26f + 0.02f * i, Chest, Reach);
                Check(r, "A4c", ex.DataOf(log2.Cast).SwingsCut == 1 && ex.DataOf(log2.Cast).SwingsCapped == 0 && k.Events.Count >= 6, "six frames of one swing are ONE swing for the cap (" + k.Events.Count + " events, 1 swing counted)");
            }

            // ---- H the host: the click ----
            {
                var k = Kit.Create(book, false);
                var go = EditorUtility.CreateGameObjectWithHideFlags("Vol2SwingFixtureHost", HideFlags.HideAndDontSave, typeof(HarvestAction), typeof(Vol2SwingDriverHost));
                try
                {
                    var harvest = go.GetComponent<HarvestAction>(); var host = go.GetComponent<Vol2SwingDriverHost>();
                    bool defaults0 = !harvest.Intercepted;
                    int pressed = 0; harvest.Pressed += () => pressed++;
                    harvest.TryBeginPull();                                            // Legacy: nobody listens to the driver yet
                    bool plain = k.Driver.Swings == 0 && pressed == 1 && !harvest.Intercepted;
                    host.Bind(k.Driver, harvest);
                    k.Src.Available = true; harvest.TryBeginPull();
                    bool routed = k.Driver.Swings == 1 && harvest.Intercepted && k.Src.Begins == 1 && pressed == 2;
                    k.Frame(0.02f, Chest, Reach); k.Frame(0.70f, Chest, Reach);
                    k.Src.Available = false; k.Src.CanBegin = false; harvest.TryBeginPull();
                    bool noRig = !harvest.Intercepted && k.Driver.Swings == 1 && pressed == 3;       // without a rig the old click keeps working
                    host.Bind(null, null);                                              // unhook
                    bool released = !harvest.Intercepted;
                    Check(r, "H1", defaults0 && plain && routed && noRig && released, "the click (HarvestAction.Pressed) reaches the driver and switches the legacy pull off while the rig exists; without a rig the pull is the old one; unbinding puts it back (Intercepted " + harvest.Intercepted + ")");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }

            // ---- L Legacy308 ----
            {
                var legacy = Vol2Installer.Build(SpellRuntimeMode.Legacy308, null, _ => true, () => 0.0);
                var builder = new ContainerBuilder();
                Vol2DefenceInstaller.Register(builder, legacy);
                bool registered; using (var container = builder.Build()) registered = container.TryResolve(out Vol2SessionLedger _);
                var go = EditorUtility.CreateGameObjectWithHideFlags("Vol2SwingFixtureLegacy", HideFlags.HideAndDontSave, typeof(HarvestAction));
                try
                {
                    var harvest = go.GetComponent<HarvestAction>(); int pressed = 0; harvest.Pressed += () => pressed++;
                    bool pulled = harvest.TryBeginPull();
                    Check(r, "L1", legacy.Shapes == null && Vol2SwingBinder.Bind(null, null) == null && !registered && !harvest.Intercepted && pressed == 1 && !pulled,
                        "Legacy308: no shape runtime, no swing driver, nothing registered by the installer; HarvestAction keeps Intercepted = false and its pull runs the old gates (no driver listens)");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
        }
    }
}
