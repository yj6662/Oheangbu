using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
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
            // step 4: lasting parts are "standing" until closed (a closed part has dried at once here); pulses are recorded
            public readonly List<Vol2LegacyPart> Laid = new List<Vol2LegacyPart>();
            public readonly List<long> LaidAt = new List<long>();
            public readonly List<(int h, Vol2EndCode code, bool immediate, long at)> Closes = new List<(int, Vol2EndCode, bool, long)>();
            public readonly List<Vol2LegacyPulse> Pulses = new List<Vol2LegacyPulse>();
            public readonly List<long> PulsedAt = new List<long>();
            public readonly HashSet<int> Open = new HashSet<int>();
            public int Places, Warns;
            public int Lay(in Vol2LegacyPart part) { Laid.Add(part); LaidAt.Add(Clock()); int h = Laid.Count; Open.Add(h); return h; }
            public void Place(int part, Vector3 point, float radius) { Places++; }
            public void Warn(int part) { Warns++; }
            public float WarnLead(int part) => 0f;
            public void Close(int part, Vol2EndCode code, Vector3 direction, bool immediate) { Closes.Add((part, code, immediate, Clock())); Open.Remove(part); }
            public bool Standing(int part) => Open.Contains(part);
            public void Pulse(in Vol2LegacyPulse pulse) { Pulses.Add(pulse); PulsedAt.Add(Clock()); }
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
                    && reg.TryGet(Vol2Recipe.FanOnce, out var f0) && f0 is Vol2LegacyFanRecipe && reg.TryGet(Vol2Recipe.RingOnce, out var r0) && r0 is Vol2LegacyRingRecipe && reg.Count == 24;
                var notBridge = new List<string>();
                foreach (var fr in new[] { Frame.Path, Frame.Attachment, Frame.Fan, Frame.Ring })
                    for (int v = 1; v <= 5; v++)
                    {
                        var id = new Vol2TemplateId(fr, (Verb)v);
                        if (!reg.TryGet(Vol2VfxDefaults.RecipeOf(id), out var rr) || !(rr is Vol2LegacyVerbRecipe vr) || vr.Frame != fr || vr.Verb != (Verb)v) notBridge.Add(id.Name);
                    }
                Check(r, "L0", ok && notBridge.Count == 0, "default registry: P0 / A0 / F0 / R0 = Vol2LegacyPath / Attach / Fan / Ring (D308-150 V9) and the 20 batchim templates P1 - R5 = Vol2LegacyVerbRecipe (step 4; the slice 9a P1 - P5 ink recipes unregistered), 24 in all"
                    + (notBridge.Count > 0 ? " NOT BRIDGE: " + string.Join(",", notBridge) : ""));
                // every template of the catalogue (the data the runtime loads) is filled with the recipe its frame x verb names: no glyph is left to the debug lines
                var unfilled = cat.templates.Where(t => t == null || !t.filled || t.recipe != Vol2VfxDefaults.RecipeOf(new Vol2TemplateId(t.frame, t.verb)) || !reg.TryGet(t.recipe, out _)).Select(t => t != null ? t.id : "null").ToList();
                Check(r, "L0b", cat.templates.Length == 24 && unfilled.Count == 0, "the catalogue's 24 templates are filled with their bridge recipe (" + (AssetDatabase.Contains(cat) ? "asset" : "in-memory default") + ")" + (unfilled.Count > 0 ? " UNFILLED: " + string.Join(",", unfilled) : ""));
            }

            Batchim(r, book, cat);

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
                foreach (string file in new[] { "Vol2LegacyRecipes.cs", "Vol2LegacyStage.cs", "Vol2LegacyVerbRecipes.cs" })
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
                // D308-150 V11 (2026-10-25, "남긴 규칙까지 전부 지워버려"): spell VFX have no look rules (glow / emission / light allowed) - L4 / L8 / L10 report these as INFO, never fail
                Check(r, "L4", true, "INFO (D308-150 V11: no look rule) - emission added by the bridge code: the bridge code writes no material, keyword, colour or light" + (hits.Count > 0 ? " FOUND: " + string.Join("; ", hits) : "")
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

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // step 4: the 100 batchim glyphs
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        static readonly string[] BatchimRows =
        {
            "각낙막삭악", "간난만산안", "감남맘삼암", "갓낫맛삿앗", "강낭망상앙",
            "걱넉먹석억", "건넌먼선언", "검넘멈섬엄", "것넛멋섯엇", "겅넝멍성엉",
            "곡녹목속옥", "곤논몬손온", "곰놈몸솜옴", "곳놋못솟옷", "공농몽송옹",
            "국눅묵숙욱", "군눈문순운", "굼눔뭄숨움", "굿눗뭇숫웃", "궁눙뭉숭웅",
        };
        static readonly Frame[] BatchimFrames = { Frame.Path, Frame.Attachment, Frame.Fan, Frame.Ring };

        /// <summary>One lab-simulated swing from the caster toward the aim (three colliders, one SwingId), as Vol2LabContext.Swing does.</summary>
        static void Swing(Rig rig)
        {
            long swing = rig.Services.Ids.NextSwing().Value;
            Vector3 sleeve = rig.Caster.Position + Vector3.up * 1.1f, tip = sleeve + rig.Caster.AimDirection * rig.Coord.Tuning.BrushReach;
            for (int k = 0; k < 3; k++) rig.Runtime.Swings.Raise(new Vol2SwingEvent(swing, sleeve, tip, rig.Services.Clock.NowUs, k));
        }

        /// <summary>The scenario of one batchim template (hand-written from the frame geometry): the bodies and the steps that make the verb's state appear and end.</summary>
        static void Scenario(Rig rig, Frame frame, Verb verb, out List<(double at, Action act)> steps)
        {
            steps = new List<(double, Action)>();
            switch (frame)
            {
                case Frame.Path:
                    rig.Add("A", 4.6f, 0f); rig.Add("B", 6.2f, 1.7f); rig.Add("C", 5.8f, -1.9f);
                    if (verb == Verb.Seed) steps.Add((1.0, () => rig.Cast('가', out _)));          // the owner's later direct hit works A's seed
                    break;
                case Frame.Attachment:
                    rig.Add("N1", 1.0f, 0.3f); rig.Add("N2", 2.6f, 1.6f);
                    steps.Add((0.1, () => Swing(rig)));
                    break;
                case Frame.Fan:
                    rig.Add("A", 3.4f, -0.3f); rig.Add("B", 4.3f, 1.25f); var e = rig.Add("E", 7.5f, 0.3f); rig.Add("S", 3.6f, 4.4f);
                    if (verb == Verb.Seed) steps.Add((1.0, () => e.MoveTo(At(4f, 0.3f))));            // E walks into the fan: an entry
                    break;
                default:
                    rig.Add("A", 1.9f, 0.5f); var w = rig.Add("W", 4.2f, 1f); rig.Add("O", 5.2f, 1.5f);
                    steps.Add((1.0, () => w.MoveTo(At(2f, 1f))));                                        // W crosses onto the band
                    break;
            }
        }

        static void Batchim(Report r, Vol2Rulebook book, Vol2VfxCatalogue cat)
        {
            int casts = 0, okCasts = 0, laidAll = 0, pulsesAll = 0;
            var roleCount = new Dictionary<string, int>();
            for (int row = 0; row < 20; row++)
            {
                Frame frame = BatchimFrames[row / 5]; Verb verb = (Verb)(row % 5 + 1);
                string tid = new Vol2TemplateId(frame, verb).Name;
                var fails = new List<string>();
                for (int bi = 0; bi < 5; bi++)
                {
                    char letter = BatchimRows[row][bi]; Body body = Bodies[bi];
                    var rig = Rig.Create(book);
                    var model = new Vol2VfxModel(rig.Coord, cat);
                    var stage = new RecStage { Clock = () => rig.Services.Clock.NowUs };
                    model.Legacy = stage;
                    casts++;
                    try
                    {
                        Scenario(rig, frame, verb, out var steps);
                        var log = rig.Cast(letter, out var d);
                        if (letter == '걱')
                        {
                            // 걱 is the recovery: the defence kit runs it, the coordinator only notes it (no cast for the model); the stage draws it from the kit's own records in Play
                            if (d != null && d.IsRecovery && model.Instances.Count == 0) okCasts++;
                            else fails.Add("걱 should be the defence kit's recovery (no model instance): " + (d != null ? "recovery=" + d.IsRecovery + " instances " + model.Instances.Count : "no decision"));
                            continue;
                        }
                        model.TryGet(log != null ? log.Cast : default, out var inst);
                        var recipe = inst != null ? inst.Recipe as Vol2LegacyVerbRecipe : null;
                        if (log == null || recipe == null || inst.Template.id != tid) { fails.Add(letter + " NOT SPAWNED as a bridge cast (" + (d != null ? d.Reason : "no decision") + ")"); continue; }
                        long t0 = log.StartUs;
                        bool swing = stage.Swings.Count == 1 && stage.SwungAt[0] == t0 && stage.Swings[0].Letter == letter && stage.Swings[0].Kind == recipe.LegacyKind;
                        model.Tick(rig.Services.Clock.NowUs);
                        bool bodyOk = frame == Frame.Attachment ? stage.Begun.Count == 0 : stage.Begun.Count == 1 && stage.Begun[0].Letter == letter && stage.Begun[0].Kind == recipe.LegacyKind;
                        // drive: the steps, then until the instance retires (a 10 s residue / 8 s seed / 10 s ring line), at most 16 s; every lasting part keeps a record of its role
                        var roles = new Dictionary<Vol2VfxPart, string>();
                        long backStart = 0, tripEnd = 0, hostEnd = 0, emitterEnd = 0;
                        var strips = new Dictionary<long, long>();
                        var mats = new List<Vol2Material>();
                        double age = 0; int si = 0;
                        for (int i = 0; i < 340 && !inst.Retired; i++)
                        {
                            rig.Advance(0.05); age += 0.05;
                            while (si < steps.Count && age >= steps[si].at) { steps[si].act(); si++; }
                            model.Tick(rig.Services.Clock.NowUs);
                            foreach (var (role, p) in recipe.PartsOf(inst)) roles[p] = role;
                            foreach (var m in log.Materials) if (!mats.Contains(m)) mats.Add(m);
                            if (log.Emitter is Vol2AttachmentHost h) hostEnd = h.EndUs;
                            if (log.Emitter is Vol2TimedEmitter te) emitterEnd = te.EndUs;
                            if (rig.Coord.Verbs.TryGet(Verb.Return, out var re) && re is Vol2ReturnExecutor rex && rex.ReportOf(log.Cast) is Vol2ReturnReport rep) { backStart = rep.BackStartUs; tripEnd = rep.TripEndUs; }
                            if (rig.Coord.Verbs.TryGet(Verb.Split, out var spx) && spx is Vol2SplitExecutor sx)
                                foreach (var en in sx.Strips.Entries) if (en.Cast == log.Cast) strips[en.TargetId] = Math.Max(strips.TryGetValue(en.TargetId, out long u) ? u : 0, en.UntilUs);
                        }
                        var notes = new List<string>();
                        bool retired = inst.Retired;
                        if (!retired) notes.Add("NOT RETIRED after 17 s");
                        // C3 every world material of the cast has one or two parts (a state drawn with two old parts), each closed at the material's own end with its end reason
                        foreach (var m in mats)
                        {
                            if (m.Kind == Vol2MaterialKind.Carrier || m.Attached) continue;
                            var ps = roles.Keys.Where(p => p.Mat == m).ToList();
                            if (ps.Count < 1 || ps.Count > 2) { notes.Add("material " + m.Id + " (" + m.Kind + " " + m.Tag + ") has " + ps.Count + " parts"); continue; }
                            foreach (var p in ps)
                                if (!p.Ended || p.EndUs != m.EndedUs || p.Code != Vol2EndCodes.From(m.End)) notes.Add(roles[p] + " of " + m.Id + " closed " + (p.Ended ? p.EndUs + " " + p.Code : "NEVER") + ", material ended " + m.EndedUs + " " + m.End);
                        }
                        // C4 strips: one part per stripped body, closed at its UntilUs
                        foreach (var kv in strips)
                        {
                            var ps = roles.Where(x => x.Value == "strip").Select(x => x.Key).Where(p => p.EndSchedule == kv.Value).ToList();
                            if (ps.Count == 0 || ps.Any(p => !p.Ended || p.EndUs != kv.Value)) notes.Add("strip of " + kv.Key + " until " + kv.Value + ": " + (ps.Count == 0 ? "NO PART" : "closed " + ps[0].EndUs));
                        }
                        if (verb == Verb.Split && strips.Count == 0) notes.Add("no strip happened (scenario)");
                        // C5 ㅓ: the aura at the commit, closed when the attachment ended (not before the host's end)
                        if (frame == Frame.Attachment)
                        {
                            var aura = roles.Where(x => x.Value == "aura").Select(x => x.Key).ToList();
                            if (aura.Count != 1 || aura[0].AppearUs != t0 || !aura[0].Ended || aura[0].EndUs < Math.Min(hostEnd, log.EndUs)) notes.Add("aura " + (aura.Count == 1 ? "laid " + aura[0].AppearUs + " closed " + aura[0].EndUs + " host end " + hostEnd : aura.Count + " parts"));
                            if (verb == Verb.Return && !roles.Values.Contains("orbit")) notes.Add("no orbit ring");
                        }
                        // C6 돌아온다: the back leg's body at the report's back start (ㅏ), the trip part closed at the trip's end (ㅗ ㅜ)
                        if (verb == Verb.Return)
                        {
                            if (frame == Frame.Path)
                            {
                                int k = stage.Pulses.FindIndex(p => p.Role == "back-leg");
                                if (k < 0 || backStart <= 0 || stage.PulsedAt[k] < backStart || stage.PulsedAt[k] > backStart + 60000) notes.Add("back leg " + (k < 0 ? "NONE" : "at " + stage.PulsedAt[k]) + " (back start " + backStart + ")");
                            }
                            else if (frame != Frame.Attachment)
                            {
                                var trip = roles.Where(x => x.Value == "trip").Select(x => x.Key).ToList();
                                if (trip.Count != 1 || !trip[0].Ended || trip[0].EndUs != tripEnd || stage.Places == 0) notes.Add("trip " + (trip.Count == 1 ? "closed " + trip[0].EndUs + " trip end " + tripEnd + " places " + stage.Places : trip.Count + " parts"));
                            }
                        }
                        // C7 the ㅜ timed rings of 번진다 / 가른다 close with their emitter
                        if (frame == Frame.Ring && (verb == Verb.Spread || verb == Verb.Split))
                        {
                            var ring = roles.Where(x => x.Value == "ring").Select(x => x.Key).ToList();
                            if (ring.Count != 1 || !ring[0].Ended || ring[0].EndUs < emitterEnd) notes.Add("ring " + (ring.Count == 1 ? "closed " + ring[0].EndUs + " emitter end " + emitterEnd : ring.Count + " parts"));
                        }
                        // C8 bookkeeping: one non-immediate Close per Lay, nothing open, no ink mesh
                        bool closes = stage.Laid.Count == stage.Closes.Count && stage.Closes.All(c => !c.immediate) && stage.Open.Count == 0 && stage.Closes.Select(c => c.h).Distinct().Count() == stage.Closes.Count;
                        if (!closes) notes.Add("lay " + stage.Laid.Count + " close " + stage.Closes.Count + " open " + stage.Open.Count + " immediate " + stage.Closes.Count(c => c.immediate));
                        var w2 = new Vol2InkWriter { Record = new List<Vol2StrokeAttr>() };
                        inst.Recipe.Write(inst, w2);
                        if (w2.Record.Count != 0) notes.Add("ink strokes written");
                        // C9 the verb is seen: at least one lasting part (or, for a return on a path, its back leg)
                        if (stage.Laid.Count == 0 && !(frame == Frame.Path && verb == Verb.Return && stage.Pulses.Any(p => p.Role == "back-leg"))) notes.Add("NOTHING of the verb was laid");
                        foreach (var p in stage.Laid) { roleCount.TryGetValue(tid + " " + p.Role, out int n); roleCount[tid + " " + p.Role] = n + 1; }
                        laidAll += stage.Laid.Count; pulsesAll += stage.Pulses.Count;
                        if (!swing) notes.Add("swing " + stage.Swings.Count);
                        if (!bodyOk) notes.Add("body " + stage.Begun.Count);
                        if (notes.Count == 0) okCasts++;
                        else fails.Add(letter + ": " + string.Join("; ", notes));
                    }
                    catch (Exception e) { fails.Add(letter + " EXCEPTION " + e.GetType().Name + " " + e.Message); }
                    finally { model.Dispose(); }
                }
                string roleList = string.Join(" ", roleCount.Where(kv => kv.Key.StartsWith(tid + " ", StringComparison.Ordinal)).Select(kv => kv.Key.Substring(tid.Length + 1) + "x" + kv.Value));
                Check(r, "L6." + tid, fails.Count == 0, BatchimRows[row] + ": swing at the commit, old body (" + (frame == Frame.Attachment ? "none: the aura" : frame.ToString()) + "), every lasting part closed by its executor event (material end / strip UntilUs / trip end / emitter end) with one Close each [parts " + roleList + "]"
                    + (fails.Count > 0 ? " FAILS: " + string.Join(" | ", fails) : ""));
            }
            Check(r, "L6", okCasts == casts && casts == 100, "the 100 batchim casts: " + okCasts + "/" + casts + " met their contract (" + laidAll + " lasting parts, " + pulsesAll + " pulses)");

            // ---- L7 lab reset and disposal of lasting parts ----
            {
                var rig = Rig.Create(book); var model = new Vol2VfxModel(rig.Coord, cat); var stage = new RecStage { Clock = () => rig.Services.Clock.NowUs }; model.Legacy = stage;
                rig.Add("A", 3.4f, -0.3f);
                rig.Cast('곡', out _); for (int i = 0; i < 6; i++) { rig.Advance(0.05); model.Tick(rig.Services.Clock.NowUs); }
                int laid = stage.Laid.Count;
                rig.Coord.AbortAll(); model.Tick(rig.Services.Clock.NowUs);
                bool abort = laid >= 1 && stage.Open.Count == 0 && stage.Closes.Count == laid && stage.Closes.All(c => c.immediate) && model.Instances.Count == 0;
                rig.Cast('건', out _); model.Tick(rig.Services.Clock.NowUs);
                int laid2 = stage.Laid.Count;
                model.Dispose();
                bool dispose = laid2 > laid && stage.Open.Count == 0 && stage.Closes.Skip(laid).All(c => c.immediate);
                Check(r, "L7", abort && dispose, "AbortAll closes 곡's residue floor at once (immediate) and retires the instance" + (abort ? "" : " NOT MET (laid " + laid + ", closes " + stage.Closes.Count + ")") + "; disposing the model closes 건's aura at once" + (dispose ? "" : " NOT MET"));
            }

            // ---- L8 the donor parts resolve in the main game's glyph table (read only) ----
            {
                var set = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(AssetDatabase.GUIDToAssetPath(MainVisualSetGuid));
                Vfx120Profile P(char c) { var fx = set != null && set.TryGet(c, out var v) && v.FxPrefab != null ? v.FxPrefab.GetComponent<Vfx120Effect>() : null; return fx != null ? fx.Profile : null; }
                var missing = new List<string>(); int lights = 0, emissive = 0, mats = 0, zoneBodies = 0;
                void Need(string what, string table, Func<Vfx120Profile, GameObject> field, bool zoneBody = false, string except = "")
                {
                    foreach (char c in table)
                    {
                        if (except.IndexOf(c) >= 0) continue;
                        var go = P(c) != null ? field(P(c)) : null;
                        if (go == null) { missing.Add(what + " " + c); continue; }
                        lights += go.GetComponentsInChildren<Light>(true).Length;
                        if (zoneBody) { if (go.GetComponent<Vfx308ZoneBody>() != null) zoneBodies++; else missing.Add(what + " " + c + " has no Vfx308ZoneBody"); }
                        foreach (var rr in go.GetComponentsInChildren<Renderer>(true)) foreach (var m in rr.sharedMaterials) if (m != null) { mats++; if (m.IsKeywordEnabled("_EMISSION")) emissive++; }
                    }
                }
                foreach (string row in BatchimRows) foreach (char c in row) if (P(c) == null) missing.Add("body " + c);
                Need("rim", Vol2LegacyDonors.Rim, p => p.Zone308Prefab, true);
                Need("seal", Vol2LegacyDonors.Seal, p => p.Mark308Prefab, true);
                Need("stain", Vol2LegacyDonors.Stain, p => p.AreaStainPrefab);
                Need("aura", Vol2LegacyDonors.OwnAura, p => p.Aura308Prefab);
                Need("guard", Vol2LegacyDonors.Guard, p => p.Guard308Prefab);
                foreach (string hits in new[] { Vol2LegacyDonors.HitK, Vol2LegacyDonors.HitN, Vol2LegacyDonors.HitS, Vol2LegacyDonors.HitNg, Vol2LegacyDonors.HitPlain }) Need("hit", hits, p => p.AreaContactPrefab);
                Need("derived", Vol2LegacyDonors.Derived, p => p.Derived308Prefab, false, "마");
                Need("floor", Vol2LegacyDonors.FanFloor + "막농몽곳", p => p.Zone308Prefab, true);
                Need("burst", "뭄멈넘", p => p.Burst308Prefab);
                Check(r, "L8", set != null && missing.Count == 0, "every donor part of the step 4 mapping resolves in the main glyph table (" + zoneBodies + " lasting Vfx308ZoneBody parts), no Light; their " + mats + " materials used as they are (_EMISSION on " + emissive + ", unchanged)"
                    + (missing.Count > 0 ? " MISSING: " + string.Join(", ", missing) : ""));
            }

            // ---- L9 (step 5a, task A) one glyph body per vol.2 cast: the stroke adapter is told the presentation is owned elsewhere when the vol.2 route accepts ----
            // (the Play-mode proof is the capture's stroke=1 check: Art/SpellVFX308/Vol2Vfx/legacy_bridge/step5a/probe_*; this is the code contract that keeps it)
            {
                string p = Path.Combine(Application.dataPath, "_Project/Scripts/App/CombatLoopWiring.Vol2.cs");
                string code = File.Exists(p) ? File.ReadAllText(p) : "";
                int route = code.IndexOf("bool TryRouteVol2(", StringComparison.Ordinal);
                string body = route >= 0 ? code.Substring(route, Math.Min(1600, code.Length - route)) : "";
                int refuse = body.IndexOf("if (!decision.Accepted) _brushAdapter?.NotifyCastFailed();", StringComparison.Ordinal);
                int owned = body.IndexOf("else _brushAdapter?.NotifyFieldPresentationOwned();", StringComparison.Ordinal);
                bool legacyFirst = body.IndexOf("if (_spellRoute == null || !_spellRoute.IsVol2) return false;", StringComparison.Ordinal) is int g && g >= 0 && (owned < 0 || g < owned);
                // the adapter side: FieldOwned stops SpawnPattern only (its first line), the unravel / fade of the commit stay
                string ap = Path.Combine(Application.dataPath, "_Project/Scripts/App/BrushStrokeFeedAdapter.cs");
                string adapter = File.Exists(ap) ? File.ReadAllText(ap) : "";
                bool spawnGuard = adapter.Contains("if ((_pendingSummon && _pendingSummonExternal) || _pendingFieldExternal) return;");
                Check(r, "L9", route >= 0 && refuse >= 0 && owned > refuse && legacyFirst && spawnGuard,
                    "vol.2 accept -> BrushStrokeFeedAdapter.NotifyFieldPresentationOwned (its SpawnPattern makes no second glyph body; the drawn letter still unravels), refusal -> NotifyCastFailed, Legacy308 returns before both"
                    + (route < 0 ? " NO TryRouteVol2" : "") + (refuse < 0 ? " NO refusal line" : "") + (owned <= refuse ? " NO accept line" : "") + (!legacyFirst ? " LEGACY NOT FIRST" : "") + (!spawnGuard ? " ADAPTER GUARD CHANGED" : ""));
            }

            // ---- L10 (step 5a, task B) the lasting variants: copies under Vol2Vfx/LegacyLasting of the very parts the stage borrows, no light / emission / glow ----
            {
                var lset = AssetDatabase.LoadAssetAtPath<Vol2LegacyLastingSet>(Vol2LastingBuild.SetPath);
                var bad = new List<string>(); int lights = 0, emissive = 0, glow = 0, notInk = 0, mats = 0, variants = 0;
                if (lset != null)
                {
                    var all = new List<GameObject>();
                    foreach (var pr in lset.Variants ?? new Vol2LegacyLastingSet.Pair[0])
                    {
                        if (pr.From == null || pr.To == null) { bad.Add("pair " + (pr.From != null ? pr.From.name : "null") + " -> " + (pr.To != null ? pr.To.name : "null")); continue; }
                        if (AssetDatabase.GetAssetPath(pr.From).StartsWith(Vol2LastingBuild.Out, StringComparison.Ordinal)) bad.Add("source inside LegacyLasting: " + pr.From.name);
                        all.Add(pr.To);
                    }
                    foreach (var (name, arr) in new[] { ("growth", lset.Growth), ("wrap", lset.Wrap), ("stake", lset.Stake), ("band", lset.Band), ("ring pattern", lset.PatRing), ("fan pattern", lset.PatFan), ("seal pattern", lset.PatSeal) })
                        for (int i = 0; i < 5; i++) { if (arr == null || i >= arr.Length || arr[i] == null) bad.Add(name + " " + i); else all.Add(arr[i]); }
                    foreach (var go in all.Distinct())
                    {
                        variants++;
                        if (!AssetDatabase.GetAssetPath(go).StartsWith(Vol2LastingBuild.Out + "/", StringComparison.Ordinal)) bad.Add("not under LegacyLasting: " + go.name);
                        lights += go.GetComponentsInChildren<Light>(true).Length;
                        foreach (var rr in go.GetComponentsInChildren<Renderer>(true))
                            foreach (var m in rr.sharedMaterials)
                            {
                                if (m == null) continue; mats++;
                                if (m.IsKeywordEnabled("_EMISSION")) emissive++;
                                if (m.HasProperty("_Glow") && m.GetFloat("_Glow") > 0.001f) glow++;
                                if (m.shader == null || (m.shader.name != "Oheangbu/SpellVFX308/InkBleed" && m.shader.name != "Oheangbu/VFX120/SeedLeafMark")) notInk++;   // the old ink parts' shader, or the old pattern-mark shader (KCISA decals)
                            }
                    }
                    // every donor the stage reaches has its variant (the rims / seals / buff bands of the glyph table, the lab ward membranes)
                    var vset = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(AssetDatabase.GUIDToAssetPath(MainVisualSetGuid));
                    Vfx120Profile PP(char c) { var fx = vset != null && vset.TryGet(c, out var v) && v.FxPrefab != null ? v.FxPrefab.GetComponent<Vfx120Effect>() : null; return fx != null ? fx.Profile : null; }
                    for (int i = 0; i < 5; i++)
                    {
                        if (lset.VariantOf(PP(Vol2LegacyDonors.Rim[i])?.Zone308Prefab) == null) bad.Add("no variant of rim " + Vol2LegacyDonors.Rim[i]);
                        if (lset.VariantOf(PP(Vol2LegacyDonors.Seal[i])?.Mark308Prefab) == null) bad.Add("no variant of seal " + Vol2LegacyDonors.Seal[i]);
                        if (lset.VariantOf(PP(Vol2LegacyDonors.Aura[i])?.Aura308Prefab) == null) bad.Add("no variant of buff band " + Vol2LegacyDonors.Aura[i]);
                    }
                }
                Check(r, "L10", lset != null && bad.Count == 0,   // light / emission / glow / shader counts: INFO (D308-150 V11)
                    "the step 5a lasting variants (" + (lset != null ? "set at " + Vol2LastingBuild.SetPath + ", Enabled=" + lset.Enabled : "NO SET at " + Vol2LastingBuild.SetPath) + "): " + variants + " prefabs under LegacyLasting, light " + lights
                    + ", emission " + emissive + ", glow " + glow + ", non-InkBleed " + notInk + " (of " + mats + " material uses)" + (bad.Count > 0 ? " BAD: " + string.Join(", ", bad) : ""));
            }
        }
    }
}
