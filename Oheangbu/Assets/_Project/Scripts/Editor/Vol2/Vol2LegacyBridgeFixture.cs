using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using static Oheangbu.EditorTools.Vol2.Vol2ShapeFixtureKit;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-150 V9 edit-mode fixture of the vol.2 LEGACY BRIDGE (the 20 no-batchim glyphs play the old main game's visuals). No Play. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2LegacyBridgeFixture Run "all"
    ///   L0 the default registry maps P0 / A0 / F0 / R0 to the four bridge recipes (and P1 - P5 stay the ink recipes)
    ///   L1 the 20 casts on the hand clock with a RECORDING stage: one swing at the commit (the old category, apex = the launch), one body at the launch (kind, letter,
    ///      body, target, impact clock = predicted vol.2 contact), contacts relayed, no ink mesh written, end -> one End (not immediate) once the body is gone
    ///   L2 the lab reset (AbortAll) and the model's disposal end every body at once (immediate), nothing left in the stage
    ///   L3 legacy assets resolved for all 20: the main game's visual set (the one the lab and W_Demo_Main point at) has a Vfx120Effect + profile for every glyph;
    ///      the ward letters also have the lab's ward profile membrane
    ///   L4 no emission added: the bridge code writes no material / keyword / light (source scan); the legacy prefabs are used as they are (their emission state is
    ///      reported, unchanged from what passed before)
    ///   L5 TE-S3-6 launch switch: OFF by default (launch = commit); ON moves only a 한 번 path / fan emitter's start to the apex (no contact before it, the contact
    ///      after it), A0 / R0 and P1 unaffected
    /// Expected values are written here from the code's contract, never read back from a run.</summary>
    public static class Vol2LegacyBridgeFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        /// <summary>The visual set the lab scene (SpellLab308) and W_Demo_Main both reference (BrushStrokeFeedAdapter._visualSet): read only.</summary>
        const string MainVisualSetGuid = "5ca7cd6c9cb82c74883e785ac9cb734c";
        /// <summary>The ward profile the lab configures (SpellLabUnlocks308._wards): read only.</summary>
        const string LabWardProfileGuid = "f44e606387fe7ec4fbc556d10459a38c";
        static readonly string[] Rows = { "가나마사아", "거너머서어", "고노모소오", "구누무수우" };
        static readonly string[] Ids = { "P0", "A0", "F0", "R0" };
        static readonly Body[] Bodies = { Body.Wood, Body.Fire, Body.Earth, Body.Metal, Body.Water };

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-legacy-bridge-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-legacy-bridge-fixture: editor busy (play / compile / import)";
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

        /// <summary>A stage that records what the bridge asks for. Bodies are "alive" until the test says they ran out.</summary>
        sealed class RecStage : IVol2LegacyStage
        {
            public readonly List<Vol2LegacyCue> Begun = new List<Vol2LegacyCue>();
            public readonly List<Vol2LegacySwing> Swings = new List<Vol2LegacySwing>();
            public readonly List<(int h, bool immediate)> Ends = new List<(int, bool)>();
            public readonly List<(int h, long target)> Contacts = new List<(int, long)>();
            public readonly List<long> BegunAt = new List<long>(), SwungAt = new List<long>();
            public readonly HashSet<int> Alive_ = new HashSet<int>();
            public Func<long> Clock;
            public int Begin(in Vol2LegacyCue cue) { Begun.Add(cue); BegunAt.Add(Clock()); int h = Begun.Count; Alive_.Add(h); return h; }
            public void Contact(int handle, long targetId, Vector3 point) { Contacts.Add((handle, targetId)); }
            public bool Alive(int handle) => Alive_.Contains(handle);
            public void End(int handle, bool immediate) { Ends.Add((handle, immediate)); Alive_.Remove(handle); }
            public void Swing(in Vol2LegacySwing swing) { Swings.Add(swing); SwungAt.Add(Clock()); }
        }

        static void Cases(Report r)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var cat = Vol2VfxCatalogue.Load();
            Check(r, "F1", cat != null, cat != null ? "catalogue loaded" : "NO CATALOGUE");
            if (cat == null) return;

            // ---- L0 registry ----
            {
                var reg = Vol2VfxRecipeRegistry.CreateDefault();
                bool ok = reg.TryGet(Vol2Recipe.PathOneHit, out var p0) && p0 is Vol2LegacyPathRecipe && reg.TryGet(Vol2Recipe.AttachOnce, out var a0) && a0 is Vol2LegacyAttachRecipe
                    && reg.TryGet(Vol2Recipe.FanOnce, out var f0) && f0 is Vol2LegacyFanRecipe && reg.TryGet(Vol2Recipe.RingOnce, out var r0) && r0 is Vol2LegacyRingRecipe
                    && reg.TryGet(Vol2Recipe.PathLeave, out var p1) && p1 is Vol2PathLeaveRecipe && reg.Count == 9;
                Check(r, "L0", ok, "default registry: P0 / A0 / F0 / R0 = Vol2LegacyPath / Attach / Fan / Ring (D308-150 V9), P1 - P5 the ink recipes, 9 in all");
            }

            // ---- L1 the 20 casts with a recording stage ----
            const long S = 1000000;
            for (int f = 0; f < 4; f++)
                for (int bi = 0; bi < 5; bi++)
                {
                    char letter = Rows[f][bi]; Body body = Bodies[bi]; string tag = Ids[f] + body;
                    var rig = Rig.Create(book);
                    var model = new Vol2VfxModel(rig.Coord, cat);
                    var stage = new RecStage { Clock = () => rig.Services.Clock.NowUs };
                    model.Legacy = stage;
                    try
                    {
                        FakeTarget a = f == 0 ? rig.Add("A", 4.6f, 0f) : f == 1 ? rig.Add("A", 1.6f, 0.25f) : f == 2 ? rig.Add("A", 3.4f, -0.3f) : rig.Add("A", 1.9f, 0.5f);
                        var log = rig.Cast(letter, out var d);
                        model.TryGet(log != null ? log.Cast : default, out var inst);
                        bool spawned = log != null && inst != null && inst.Template.id == Ids[f] && inst.Recipe is Vol2LegacyRecipeBase;
                        if (!spawned) { Check(r, "L1." + tag, false, letter + ": NOT SPAWNED as a bridge cast (" + (d != null ? d.Reason : "no decision") + ")"); continue; }
                        long t0 = log.StartUs;
                        // the commit frame: one swing, the old category
                        bool swing = stage.Swings.Count == 1 && stage.SwungAt[0] == t0 && stage.Swings[0].Letter == letter && stage.Swings[0].Body == body
                            && stage.Swings[0].Kind == ((Vol2LegacyRecipeBase)inst.Recipe).LegacyKind && stage.Swings[0].ToApex >= Vol2LegacyRecipeBase.MinToApex - 1e-4f;
                        // the body begins at the launch (= the commit: the switch is off in a fixture), in the first model tick
                        model.Tick(rig.Services.Clock.NowUs);
                        bool begun = stage.Begun.Count == 1 && stage.BegunAt[0] == t0 && stage.Begun[0].Letter == letter && stage.Begun[0].Body == body;
                        var cue = begun ? stage.Begun[0] : default;
                        string kindNote = "";
                        bool kindOk = false;
                        switch (f)
                        {
                            case 0:
                                // P0: a target on the path 4.6 m ahead: impact clock = (4.6 - radius 0.4 - half width) / path speed, measured from the launch
                                float speed = rig.Coord.Tuning.PathSpeed, half = log.Spec.PathWidth * 0.5f;
                                float expect = (4.6f - Mathf.Sqrt((half + a.Radius) * (half + a.Radius))) / speed;
                                kindOk = cue.Kind == Vol2LegacyKind.Single && cue.HasTarget && cue.TargetId == a.Id && Mathf.Abs(cue.ImpactClock - expect) < 0.01f;
                                kindNote = "single, target A, impact clock " + cue.ImpactClock.ToString("0.000") + " s (expected " + expect.ToString("0.000") + ")";
                                break;
                            case 1:
                                long w = log.WindowEndUs - log.WindowStartUs;
                                kindOk = cue.Kind == Vol2LegacyKind.Guard && Mathf.Abs(cue.GuardWindow - w / 1000000f) < 1e-4f && cue.GuardWindow > 0f;
                                kindNote = "guard, window " + cue.GuardWindow.ToString("0.00") + " s = the vol.2 parry window";
                                break;
                            case 2:
                                kindOk = cue.Kind == Vol2LegacyKind.Area && cue.HasTarget && cue.TargetId == a.Id;
                                kindNote = "area, aimed at the body the fan touches (A)";
                                break;
                            default:
                                kindOk = cue.Kind == Vol2LegacyKind.Ward && Mathf.Abs(cue.RingRadius - log.Spec.RingRadius) < 1e-4f;
                                kindNote = "ward on the ring's band (radius " + cue.RingRadius.ToString("0.00") + ")";
                                break;
                        }
                        // drive to the end: contacts relayed (P0 / F0 / R0 touch A; A0 records no paid contact without a swing)
                        // (a fire body's burn keeps the cast running for its ticks: drive until the cast is done, at most 8 s)
                        for (int i = 0; i < 160 && (i < 40 || !log.Finished); i++) { rig.Advance(0.05); model.Tick(rig.Services.Clock.NowUs); }
                        int paid = log.Contacts.Count(c => c.BudgetAccepted);
                        bool contacts = stage.Contacts.Count == paid && (f == 1 || paid >= 1);
                        var w2 = new Vol2InkWriter { Record = new List<Vol2StrokeAttr>() };
                        inst.Recipe.Write(inst, w2);
                        bool noMesh = w2.Record.Count == 0 && w2.Vertices == 0;
                        // the cast ended; the body is still "alive" (its own life): the instance waits, then the body runs out and the instance retires with one End
                        bool waiting = !inst.Retired && log.Finished && stage.Ends.Count == 0;
                        stage.Alive_.Clear();
                        rig.Advance(0.05); model.Tick(rig.Services.Clock.NowUs);
                        bool ended = inst.Retired && stage.Ends.Count == 1 && stage.Ends[0].h == 1 && !stage.Ends[0].immediate;
                        Check(r, "L1." + tag, swing && begun && kindOk && contacts && noMesh && waiting && ended,
                            letter + " " + body + ": swing at the commit (" + (stage.Swings.Count > 0 ? stage.Swings[0].Kind + " toApex " + stage.Swings[0].ToApex.ToString("0.00") : "none") + ")"
                            + (swing ? "" : " NOT MET") + ", body at the launch" + (begun ? "" : " NOT MET") + ", " + kindNote + (kindOk ? "" : " NOT MET")
                            + ", contacts relayed " + stage.Contacts.Count + "/" + paid + (contacts ? "" : " NOT MET") + ", no ink mesh" + (noMesh ? "" : " NOT MET")
                            + ", waits for its body" + (waiting ? "" : " NOT MET") + ", one End when it is gone" + (ended ? "" : " NOT MET (" + stage.Ends.Count + ")"));
                    }
                    finally { model.Dispose(); }
                }

            // ---- L2 lab reset and disposal ----
            {
                var rig = Rig.Create(book); var model = new Vol2VfxModel(rig.Coord, cat); var stage = new RecStage { Clock = () => rig.Services.Clock.NowUs }; model.Legacy = stage;
                rig.Add("A", 4.6f, 0f);
                rig.Cast('가', out _); model.Tick(rig.Services.Clock.NowUs);
                rig.Coord.AbortAll(); model.Tick(rig.Services.Clock.NowUs);
                bool abort = stage.Begun.Count == 1 && stage.Ends.Count == 1 && stage.Ends[0].immediate && model.Instances.Count == 0;
                rig.Cast('고', out _); model.Tick(rig.Services.Clock.NowUs);
                model.Dispose();
                bool dispose = stage.Begun.Count == 2 && stage.Ends.Count == 2 && stage.Ends[1].immediate && stage.Alive_.Count == 0;
                Check(r, "L2", abort && dispose, "AbortAll ends the body at once (immediate) and retires the instance" + (abort ? "" : " NOT MET") + "; disposing the model ends every live body at once" + (dispose ? "" : " NOT MET")
                    + " [begun " + stage.Begun.Count + ", ends " + string.Join(",", stage.Ends.Select(e => e.h + (e.immediate ? "!" : ""))) + "]");
            }

            // ---- L3 the legacy assets ----
            {
                var set = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(AssetDatabase.GUIDToAssetPath(MainVisualSetGuid));
                var ward = AssetDatabase.LoadAssetAtPath<EAWardProfileSO>(AssetDatabase.GUIDToAssetPath(LabWardProfileGuid));
                var missing = new List<string>(); var seen = new List<string>();
                foreach (string row in Rows)
                    foreach (char c in row)
                    {
                        GameObject prefab = set != null && set.TryGet(c, out var v) ? v.FxPrefab : null;
                        var fx = prefab != null ? prefab.GetComponent<Vfx120Effect>() : null;
                        if (fx == null || fx.Profile == null) missing.Add(c + " (visual set)"); else seen.Add(c + "=" + prefab.name);
                        int wi = "구누무수우".IndexOf(c);
                        if (wi >= 0)
                        {
                            var wp = ward != null && ward.PresentationPrefabs != null && wi < ward.PresentationPrefabs.Length ? ward.PresentationPrefabs[wi] : null;
                            var wfx = wp != null ? wp.GetComponent<Vfx120Effect>() : null;
                            if (wfx == null || wfx.Profile == null) missing.Add(c + " (ward membrane)"); else seen.Add(c + " ward=" + wp.name);
                        }
                    }
                Check(r, "L3", set != null && ward != null && missing.Count == 0, "legacy assets resolved for all 20 (main visual set " + (set != null ? AssetDatabase.GetAssetPath(set) : "NOT FOUND") + ", lab ward profile "
                    + (ward != null ? ward.name : "NOT FOUND") + ")" + (missing.Count > 0 ? " MISSING: " + string.Join(" ", missing) : "") + " [" + string.Join(" ", seen) + "]");

                // L4: the bridge's code adds nothing that glows; the legacy materials are reported as they are
                string dir = Path.Combine(Application.dataPath, "_Project/Scripts/App/Vol2/Presentation");
                var banned = new[] { "EnableKeyword", "_EMISSION", "_EmissionColor", "SetColor(", "AddComponent<Light", "new Material(", ".material.", "sharedMaterial =", "Additive" };
                var hits = new List<string>();
                foreach (string file in new[] { "Vol2LegacyRecipes.cs", "Vol2LegacyStage.cs" })
                {
                    string p = Path.Combine(dir, file);
                    if (!File.Exists(p)) { hits.Add(file + " NOT FOUND"); continue; }
                    string code = File.ReadAllText(p);
                    foreach (string b in banned) if (code.Contains(b)) hits.Add(file + ": " + b);
                }
                int mats = 0, emissive = 0, lights = 0; var emissiveNames = new List<string>();
                foreach (string row in Rows)
                    foreach (char c in row)
                    {
                        if (set == null || !set.TryGet(c, out var v) || v.FxPrefab == null) continue;
                        lights += v.FxPrefab.GetComponentsInChildren<Light>(true).Length;
                        var fx = v.FxPrefab.GetComponent<Vfx120Effect>();
                        var ms = new List<Material>();
                        foreach (var rr in v.FxPrefab.GetComponentsInChildren<Renderer>(true)) ms.AddRange(rr.sharedMaterials);
                        if (fx != null && fx.Profile != null) ms.AddRange(new[] { fx.Profile.BodyMaterial, fx.Profile.InkMaterial, fx.Profile.PatternMaterial });
                        foreach (var m in ms.Where(m => m != null).Distinct())
                        {
                            mats++;
                            if (m.IsKeywordEnabled("_EMISSION")) { emissive++; if (emissiveNames.Count < 6) emissiveNames.Add(m.name); }
                        }
                    }
                Check(r, "L4", hits.Count == 0 && lights == 0, "no emission added: the bridge code writes no material, keyword, colour or light" + (hits.Count > 0 ? " FOUND: " + string.Join("; ", hits) : "")
                    + "; the legacy bodies carry no Light (" + lights + "); their " + mats + " materials are used as they are (_EMISSION on " + emissive + (emissiveNames.Count > 0 ? ": " + string.Join(", ", emissiveNames) : "") + ", unchanged)");
            }

            // ---- L5 the TE-S3-6 launch switch ----
            {
                var rig = Rig.Create(book);
                var tun = rig.Coord.Tuning;
                bool off = !tun.LaunchAtApex && tun.LaunchDelayUs(Frame.Path, Verb.Once) == 0;
                tun.LaunchAtApex = true; tun.PathApexSeconds = 0.28f; tun.FanApexSeconds = 0.53f;
                var a = rig.Add("A", 4.6f, 0f);
                var log = rig.Cast('가', out _);
                bool p0Start = log != null && log.Emitter.StartUs == log.StartUs + 280000;
                rig.Advance(0.27, 9);
                bool none = log != null && log.Contacts.Count == 0;
                rig.Advance(0.2, 8);
                bool hit = log != null && log.Contacts.Count(c => c.BudgetAccepted) == 1 && log.Contacts[0].NowUs >= log.Emitter.StartUs;
                var rig2 = Rig.Create(book); rig2.Coord.Tuning.LaunchAtApex = true; rig2.Coord.Tuning.FanApexSeconds = 0.53f;
                rig2.Add("A", 3.4f, -0.3f);
                var fan = rig2.Cast('고', out _);
                bool fanNone = fan != null && fan.Contacts.Count == 0 && fan.Emitter.StartUs == fan.StartUs + 530000;
                rig2.Advance(0.6, 12);
                bool fanHit = fan != null && fan.Contacts.Count(c => c.BudgetAccepted) == 1;
                var rig3 = Rig.Create(book); rig3.Coord.Tuning.LaunchAtApex = true;
                rig3.Add("A", 1.9f, 0.5f);
                var ring = rig3.Cast('구', out _);
                var parry = rig3.Cast('거', out _);
                var leave = rig3.Cast('각', out _);
                bool others = ring != null && ring.Emitter.StartUs == ring.StartUs && parry != null && parry.Emitter.StartUs == parry.StartUs && leave != null && leave.Emitter.StartUs == leave.StartUs;
                Check(r, "L5", off && p0Start && none && hit && fanNone && fanHit && others,
                    "TE-S3-6: OFF by default" + (off ? "" : " NOT MET") + "; ON: 가's path starts 0.28 s after the commit" + (p0Start ? "" : " NOT MET") + ", touches nothing before" + (none ? "" : " NOT MET")
                    + " and A after it" + (hit ? "" : " NOT MET") + "; 고's fan at +0.53 s" + (fanNone ? "" : " NOT MET") + " then A" + (fanHit ? "" : " NOT MET") + "; 구 / 거 / 각 launch at the commit" + (others ? "" : " NOT MET"));
            }
        }
    }
}
