using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.World;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 player juice (SPEC-ANIM-JUICE-308): checks and the edit-mode preview driver.
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Juice308Build Run "check"
    ///       Edit Mode, changes nothing: the C# curves against the python reference, the solver's rules (draw-mode gate, ceilings,
    ///       exact return), the camera owner in a real render (own preview scene, HideAndDontSave objects: nesting order, exact
    ///       restore, composition with a simulated field-of-view breath and view shake), assembly borders, the open scene's rig.
    ///   ... Run "check-curves"                         curves only
    ///   ... Run "timeline:&lt;cast|call|setdown|stack|draw&gt;[:hz]"   solver alone on a timeline -> numeric trace (json)
    ///   ... Run "cam:strip:&lt;cast|call|setdown&gt;[:narrow]"   the open scene's game camera with the reaction on, frame by frame:
    ///                                                    stills + the values the render saw (the camera is restored bit for bit);
    ///                                                    narrow = the field-of-view kick with the other sign
    ///   ... Run "cam:judge:&lt;cast|call|setdown&gt;[:narrow][:amp=N]"   the same reaction where a person can judge it: a clone of the player in a
    ///                                                    bright preview scene with a one-degree reference grid, the close-up arm and a stand-in
    ///                                                    letter (cast) or a stand-in car (call / setdown); 60 Hz frames, three rows - off, on,
    ///                                                    on x N (exaggerated, default 4) - Juice308Build.Preview.cs; sheet and clip:
    ///                                                    python Tools/Unity/Stage308_juice/judge_sheet308.py
    ///   ... Run "p1[:label]" | "p2[:a1|a2][:label]" | "p7"   grip comparison / strip / nine-point tip table of the close-up arm (a clone
    ///                                                    of the player in a preview scene, A-1 / A-2 on) - Juice308Build.Preview.cs
    ///   ... Run "hand-jump"                            SPEC-LOCKON-DRAW-STABILITY-308 4.7: the cloned rig's brush tip and hand while the camera turns or
    ///                                                    the player walks, at the scene's own coordinates, read in double precision - Juice308Build.Preview.cs
    ///   The captures use a memory copy of the profile asset when it exists, else the class defaults; "check" always uses the
    ///   class defaults (they are what the python reference was built from).
    ///   ... Run "recog-identity[:fixture]"              a recorded stroke through the recogniser with the juice off / on: same bytes in,
    ///                                                    same result out
    ///   ... Run "identity"                             the off contract, measured on the cloned rig: no profile at all = Intensity 0 =
    ///                                                    (draw frames) the main profile, every transform, every frame - Juice308Build.Preview.cs
    ///   ... Run "profile:status" | "profile:plan" | "profile:create" | "profile:apply" | "profile:check" | "profile:revert:&lt;record&gt;" | "profile:remove"
    ///                                                    3rd revision (D308-24): the main profile asset is made FROM DATA -
    ///                                                    Art/Characters308/Juice/Data/PlayerJuice308Main.json lists every field. plan = dry,
    ///                                                    create / apply = that one asset only, check = asset against data - Juice308Build.Juice2.cs
    ///   ... Run "parry-identity"                       D308-24 answer 24 on the cloned rig: the same take as a parry cast and as another cast -
    ///                                                    every transform equal, camera offset 0 against the kick - Juice308Build.Juice2.cs
    ///   ... Run "preview-profile:on" | "preview-profile:off"   Play only, memory only: A-1 / A-2 / D-3 on for the live rig
    ///   ... Run "fixtures:cleanup"
    /// Queue paths never open a dialog: a refusal comes back as "refused: ...". No scene is saved. Output: Art/Characters308/Juice/Preview/.
    /// </summary>
    public static partial class Juice308Build
    {
        const string FixturePrefix = "Juice308_Fixture";
        const string ProfileAssetPath = "Assets/_Project/Resources/Juice308/PlayerJuice308Profile.asset";
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        static string OutRoot => Path.Combine(Repo, "Art/Characters308/Juice/Preview");
        static string StageRoot => Path.Combine(Repo, "Tools/Unity/Stage308_juice");
        static string Stamp() => DateTime.UtcNow.ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture);

        // <owner-rows>  (the text between these marker pairs is also compiled outside the editor: checks/owner_model.py)
        static string F(float v, string f = "F4") => v.ToString(f, CultureInfo.InvariantCulture);

        sealed class Checks
        {
            public readonly List<string> Lines = new List<string>(); public int Fail;
            public bool Add(string name, bool ok, string note = "") { Lines.Add((ok ? "ok   " : "FAIL ") + name + (note.Length > 0 ? "  - " + note : "")); if (!ok) Fail++; return ok; }
            public void Note(string text) { Lines.Add("note " + text); }
        }
        // </owner-rows>

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            string[] parts = command.Split(':'); string verb = parts[0].Trim().ToLowerInvariant();
            string Arg(int i) => parts.Length > i ? parts[i].Trim() : "";
            try
            {
                switch (verb)
                {
                    case "": case "help": return Help();
                    case "check": return Check();
                    case "check-curves": { var c = new Checks(); CheckCurves(c); return Finish(c, "curves"); }
                    case "timeline": return Timeline(Arg(1).ToLowerInvariant(), Arg(2));
                    case "cam": return Arg(1).ToLowerInvariant() == "strip" ? CameraStrip(Arg(2).ToLowerInvariant(), Arg(3).ToLowerInvariant())
                        : Arg(1).ToLowerInvariant() == "judge" ? CameraJudge(Arg(2).ToLowerInvariant(), parts.Skip(3).Select(p => p.Trim().ToLowerInvariant()).ToArray())
                        : Arg(1).ToLowerInvariant() == "nesting" ? CameraNestingOnly() : "refused: expected cam:nesting | cam:strip:<cast|call|setdown> | cam:judge:<cast|call|setdown>";
                    case "p1": return GripComparison(Arg(1));
                    case "p2": return ArmStrip(Arg(1).ToLowerInvariant(), Arg(2));
                    case "p7": return NinePointTable();
                    case "hand-jump": return HandJump();
                    case "recog-identity": return RecogIdentity(Arg(1));
                    case "identity": return Identity();
                    case "profile": return Profile308(Arg(1).ToLowerInvariant(), Arg(2));   // #308 3차: from data (Juice308Build.Juice2.cs)
                    case "parry-identity": return ParryIdentity();
                    case "preview-profile": return PreviewProfile(Arg(1).ToLowerInvariant());
                    case "fixtures": return Arg(1).ToLowerInvariant() == "cleanup" ? CleanupFixtures() : "refused: expected fixtures:cleanup";
                }
                return "refused: unknown command '" + command + "'\n" + Help();
            }
            catch (Exception e) { return "error: " + e.GetType().Name + ": " + e.Message + "\n" + CleanupFixtures(); }
        }

        static string Help() => "Juice308Build: check | check-curves | timeline:<cast|call|setdown|stack|draw>[:hz] | cam:nesting | cam:strip:<cast|call|setdown>[:narrow] | cam:judge:<cast|call|setdown>[:narrow][:amp=N] | "
            + "p1[:label] | p2[:a1|a2][:label] | p7 | hand-jump | identity | parry-identity | recog-identity[:fixture] | "
            + "profile:status | profile:plan | profile:create | profile:apply | profile:check | profile:revert:<record> | profile:remove | preview-profile:on|off (Play) | fixtures:cleanup";

        static string Finish(Checks c, string name)
        {
            c.Lines.Add("juice308 " + name + ": " + c.Lines.Count(l => l.StartsWith("ok", StringComparison.Ordinal) || l.StartsWith("FAIL", StringComparison.Ordinal)) + " checks, FAIL " + c.Fail);
            Directory.CreateDirectory(OutRoot);
            File.WriteAllText(Path.Combine(OutRoot, name + "_" + Stamp() + ".txt"), string.Join("\n", c.Lines), new UTF8Encoding(false));
            return string.Join("\n", c.Lines);
        }

        static PlayerJuice308ProfileSO NewProfile(bool preview)
        {
            var profile = ScriptableObject.CreateInstance<PlayerJuice308ProfileSO>();
            profile.name = FixturePrefix + (preview ? "_PreviewProfile" : "_MainProfile"); profile.hideFlags = HideFlags.HideAndDontSave;
            if (preview) profile.EnablePreviewOnly();
            return profile;
        }

        // The captures show what the game will show: the project's profile asset when it exists (a memory copy - the asset is
        // never written), else the class defaults. preview = A-1 / A-2 / D-3 switched on in that copy.
        static PlayerJuice308ProfileSO CaptureProfile(bool preview, out string source)
        {
            var asset = AssetDatabase.LoadAssetAtPath<PlayerJuice308ProfileSO>(ProfileAssetPath);
            source = asset != null ? "a memory copy of " + ProfileAssetPath : "the class defaults (no profile asset yet)";
            if (asset == null) return NewProfile(preview);
            var copy = Object.Instantiate(asset);
            copy.name = FixturePrefix + (preview ? "_PreviewProfile" : "_MainProfile"); copy.hideFlags = HideFlags.HideAndDontSave;
            if (preview) copy.EnablePreviewOnly();
            return copy;
        }

        // ---------------------------------------------------------------- check

        static string Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: check runs in Edit Mode";
            var c = new Checks();
            var scene = SceneManager.GetActiveScene(); bool dirtyBefore = scene.isDirty; int rootsBefore = scene.rootCount;
            CheckCurves(c);
            CheckSolver(c);
            CheckCameraOwner(c);
            CheckBorders(c);
            CheckScene(c, scene);
            CheckJuice2(c, scene);   // #308 3차 (D308-24): profile data, parry exclusion, marks that follow - Juice308Build.Juice2.cs
            c.Add("Z1.open_scene_untouched", scene.isDirty == dirtyBefore && scene.rootCount == rootsBefore, "open scene dirty " + scene.isDirty + " (before " + dirtyBefore + "), roots " + scene.rootCount + " (before " + rootsBefore + ")");
            c.Add("Z2.no_fixture_left", CountFixtures() == 0, CountFixtures() + " object(s) named " + FixturePrefix + "* alive after the checks");
            return Finish(c, "check");
        }

        [Serializable] sealed class RefRow { public string fn; public float[] a; public float t; public float v; }
        [Serializable] sealed class RefFile { public string note; public RefRow[] rows; }
        [Serializable] sealed class Trace { public string name; public string events; public int hz; public float[] nod, roll, fov; }
        [Serializable] sealed class TraceFile { public Trace[] traces; }

        static float Curve(RefRow r)
        {
            float[] a = r.a ?? new float[5];
            switch (r.fn)
            {
                case "pulse": return PlayerJuice308Curves.Pulse(r.t, a[0], a[1]);
                case "ring": return PlayerJuice308Curves.Ring(r.t, a[0], a[1], a[2]);
                case "settle": return PlayerJuice308Curves.Settle(r.t, a[0], a[1], a[2]);
                case "cast_along": return PlayerJuice308Curves.CastAlong(r.t, a[0], a[1], a[2]);
                case "cast_over": return PlayerJuice308Curves.CastOver(r.t, a[0], a[1], a[2], a[3]);
                case "cast_drop": return PlayerJuice308Curves.CastDrop(r.t, a[0], a[1], a[2], a[3]);
                case "air_over": return PlayerJuice308Curves.AirOver(r.t, a[0], a[1], a[2], a[3]);
                case "air_weight": return PlayerJuice308Curves.AirWeight(r.t, a[0], a[1]);
                case "car_settle": return PlayerJuice308Curves.CarSettle(r.t, a[0], a[1], a[2], a[3], a[4]);
                case "car_lift": return PlayerJuice308Curves.CarLift(r.t, a[0], a[1]);
                case "distance_scale": return PlayerJuice308Curves.DistanceScale(r.t, a[0], a[1]);
                case "ring_peak_time": return PlayerJuice308Curves.RingPeakTime(a[0], a[1]);
            }
            return float.NaN;
        }

        static void CheckCurves(Checks c)
        {
            string path = Path.Combine(StageRoot, "checks/out/curves_ref.json");
            if (!File.Exists(path)) { c.Add("C1.curves_reference", false, "missing " + path + " (run Tools/Unity/Stage308_juice/checks/juice_checks.py)"); return; }
            var file = JsonUtility.FromJson<RefFile>(File.ReadAllText(path));
            int n = file != null && file.rows != null ? file.rows.Length : 0; float worst = 0f; string worstRow = ""; int unknown = 0;
            var perFn = new Dictionary<string, float>();
            for (int i = 0; i < n; i++)
            {
                var r = file.rows[i]; float v = Curve(r);
                if (float.IsNaN(v)) { unknown++; continue; }
                float d = Mathf.Abs(v - r.v);
                if (!perFn.TryGetValue(r.fn, out float w) || d > w) perFn[r.fn] = d;
                if (d > worst) { worst = d; worstRow = r.fn + " t " + F(r.t) + " c# " + F(v, "G7") + " ref " + F(r.v, "G7"); }
            }
            c.Add("C1.curves_match_reference", n > 100 && unknown == 0 && worst <= 1e-4f, n + " rows, " + perFn.Count + " functions, largest difference " + F(worst, "G3") + (worstRow.Length > 0 ? " (" + worstRow + ")" : "") + ", limit 1e-4");
            // closed forms end exactly
            c.Add("C2.exact_ends", PlayerJuice308Curves.Pulse(.22f, .05f, .17f) == 0f && PlayerJuice308Curves.Ring(.32f, 6f, .55f, .32f) == 0f
                && PlayerJuice308Curves.Settle(.14f, 7f, .5f, .14f) == 1f && PlayerJuice308Curves.CarSettle(.55f, .03f, .08f, 3.2f, .45f, .47f) == 0f
                && PlayerJuice308Curves.AirOver(.175f, .035f, 7f, .5f, .14f) == 0f, "pulse / ring / car settle / overshoot are exactly 0 (settle exactly 1) from their length on");
        }

        // ---------------------------------------------------------------- solver

        static void Fire(PlayerJuice308Solver s, string e)
        {
            switch (e)
            {
                case "cast": s.CastKick(1f, 1f); break;
                case "call": s.CallMoment(true); break;
                case "setdown": s.VehicleSetDown(0f); break;
            }
        }

        static void CheckSolver(Checks c)
        {
            var main = NewProfile(false); var preview = NewProfile(true);
            try
            {
                // the python mirror's traces (main profile, full power) against the real solver, frame by frame
                string path = Path.Combine(StageRoot, "checks/out/camera_traces_flat.json");
                if (!File.Exists(path)) c.Add("V1.solver_matches_mirror", false, "missing " + path);
                else
                {
                    var file = JsonUtility.FromJson<TraceFile>(File.ReadAllText(path)); float worst = 0f; int frames = 0, traces = 0;
                    foreach (var t in file.traces ?? new Trace[0])
                    {
                        var s = new PlayerJuice308Solver(); s.Configure(main); float dt = 1f / Mathf.Max(1, t.hz); traces++;
                        for (int i = 0; i < t.nod.Length; i++)
                        {
                            if (i == 0) foreach (string e in (t.events ?? "").Split(',')) Fire(s, e.Trim());
                            s.Advance(dt); s.Evaluate(true, out JuiceFrame308 f); frames++;
                            worst = Mathf.Max(worst, Mathf.Abs(f.Camera.x - t.nod[i]), Mathf.Abs(f.Camera.y - t.roll[i]), Mathf.Abs(f.Camera.z - t.fov[i]));
                        }
                    }
                    c.Add("V1.solver_matches_mirror", traces >= 9 && worst <= 2e-4f, traces + " traces, " + frames + " frames: largest difference from the python mirror " + F(worst, "G3") + " deg (limit 2e-4)");
                }
                // gate: nothing while the caller says "not allowed" (draw mode / live stroke), and the reaction is ended, not paused
                var g = new PlayerJuice308Solver(); g.Configure(main); g.CastKick(1f, 1f);
                float dt60 = 1f / 60f; bool zero = true, ran = false;
                for (int i = 0; i < 4; i++) { g.Advance(dt60); g.Evaluate(true, out JuiceFrame308 f); if (i == 0) zero &= Zero(f.Camera); else ran |= f.Camera.x > 0f; }
                g.Advance(dt60); g.Evaluate(false, out JuiceFrame308 blocked); g.Advance(dt60); g.Evaluate(true, out JuiceFrame308 after);
                c.Add("V2.gate_ends_the_reaction", zero && ran && blocked.Camera.x == 0f && blocked.Camera.y == 0f && blocked.Camera.z == 0f && after.Camera.x == 0f && !g.CameraRunning && g.CameraCancels == 1,
                    "commit frame 0, then rising; a blocked frame is exactly 0 and the reaction does not come back (cancels " + g.CameraCancels + ")");
                // ceilings and exact return
                var k = new PlayerJuice308Solver(); k.Configure(main); k.CastKick(1f, 1f); k.CallMoment(true); k.VehicleSetDown(0f);
                Vector3 peak = Vector3.zero; JuiceFrame308 last = default;
                for (int i = 0; i < 90; i++) { k.Advance(1f / 144f); k.Evaluate(true, out last); peak = Vector3.Max(peak, new Vector3(Mathf.Abs(last.Camera.x), Mathf.Abs(last.Camera.y), Mathf.Abs(last.Camera.z))); }
                c.Add("V3.ceilings", peak.x <= main.Camera.MaxPitchDegrees + 1e-6f && peak.y <= main.Camera.MaxRollDegrees + 1e-6f && peak.z <= main.Camera.MaxFovDegrees + 1e-6f,
                    "all three reactions on one frame: nod " + F(peak.x) + " (ceiling " + F(main.Camera.MaxPitchDegrees, "F1") + "), roll " + F(peak.y) + ", fov " + F(peak.z) + " (ceiling " + F(main.Camera.MaxFovDegrees, "F1") + ")");
                c.Add("V4.exact_return", last.Camera.x == 0f && last.Camera.y == 0f && last.Camera.z == 0f && !k.CameraRunning, "0.62 s later the offset is exactly (0, 0, 0) and no clock runs");
                var r = new PlayerJuice308Solver(); r.Configure(main); r.SetReducedMotion(true); r.CastKick(1f, 1f); r.CallMoment(true); r.VehicleSetDown(0f);
                bool none = true; for (int i = 0; i < 30; i++) { r.Advance(dt60); r.Evaluate(true, out JuiceFrame308 f); none &= f.Camera.x == 0f && f.Camera.y == 0f && f.Camera.z == 0f; }
                c.Add("V5.reduced_motion", none && !r.CameraRunning, "ReducedMotion (camera scale " + F(main.CameraReducedMotionScale, "F2") + "): no reaction starts");
                // main profile: nothing on the shaft; preview profile: press, then flick, never both, within the ceiling
                var m = new PlayerJuice308Solver(); m.Configure(main); m.ModeEntered(); m.StrokeStarted(); bool shaft = false;
                for (int i = 0; i < 20; i++) { m.NoteStrokeVelocity(new Vector2(2f, 0f), dt60); m.Advance(dt60); m.Evaluate(false, out JuiceFrame308 f); shaft |= f.AnyShaft || f.SplayAdd != 0f; }
                m.StrokeEnded(); for (int i = 0; i < 20; i++) { m.Advance(dt60); m.Evaluate(false, out JuiceFrame308 f); shaft |= f.AnyShaft || f.SplayAdd != 0f; }
                c.Add("V6.main_profile_shaft_zero", !shaft, "class defaults (A-1 / A-2 off - the main profile's own values are data, rows D1 - D3): shaft tilt, lean and splay are exactly 0");
                var p = new PlayerJuice308Solver(); p.Configure(preview); p.ModeEntered(); p.StrokeStarted(); float tilt = 0f, lean = 0f, splay = 0f, sum = 0f; bool both = false;
                for (int i = 0; i < 12; i++) { p.NoteStrokeVelocity(new Vector2(3.5f, 0f), dt60); p.Advance(dt60); p.Evaluate(false, out JuiceFrame308 f); tilt = Mathf.Max(tilt, f.ShaftRaiseDegrees); splay = Mathf.Max(splay, f.SplayAdd); both |= f.ShaftRaiseDegrees != 0f && f.ShaftLeanDegrees != Vector2.zero; sum = Mathf.Max(sum, f.ShaftRaiseDegrees + f.ShaftLeanDegrees.magnitude); }
                p.StrokeEnded();
                for (int i = 0; i < 20; i++) { p.Advance(dt60); p.Evaluate(false, out JuiceFrame308 f); lean = Mathf.Max(lean, f.ShaftLeanDegrees.magnitude); both |= f.ShaftRaiseDegrees != 0f && f.ShaftLeanDegrees != Vector2.zero; sum = Mathf.Max(sum, f.ShaftRaiseDegrees + f.ShaftLeanDegrees.magnitude); }
                p.StrokeStarted(); p.Advance(dt60); p.Evaluate(false, out JuiceFrame308 cut);
                c.Add("V7.preview_profile_shaft", tilt > 2f && tilt <= 3f + 1e-4f && lean > 2f && lean <= 4f + 1e-4f && splay <= preview.MaxSplayAdd + 1e-6f && !both && sum <= preview.MaxShaftLeanDegrees + 1e-4f && cut.ShaftLeanDegrees == Vector2.zero,
                    "preview profile at 60 Hz: press tilt " + F(tilt, "F2") + " deg, splay +" + F(splay, "F2") + ", flick lean " + F(lean, "F2") + " deg, never both, sum <= " + F(preview.MaxShaftLeanDegrees, "F1") + ", a new stroke cuts the flick on its frame");
                // B-2: the close-up arm is shown for the three tier lengths; C-1: the contact span keeps today's arm weight
                var b = new PlayerJuice308Solver(); b.Configure(main);
                float lo = b.BeginCast(0f), mid = b.BeginCast(.5f), hi = b.BeginCast(1f);
                c.Add("V8.cast_lengths", Mathf.Abs(lo - .295f) < 1e-5f && Mathf.Abs(mid - .34f) < 1e-5f && Mathf.Abs(hi - .365f) < 1e-5f, "close-up arm shown " + F(lo, "F3") + " / " + F(mid, "F3") + " / " + F(hi, "F3") + " s for power 0 / .5 / 1");
                float same = 0f; float raise = .30f, contact = .62f;
                for (int i = 0; i <= 620; i++)
                {
                    float t = i / 1000f; b.EvaluateCall(t, raise, contact, 1.2f, out float w, out float over, out float droop);
                    float original = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / raise)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(contact, 1f, t)));
                    same = Mathf.Max(same, Mathf.Abs(w - original), Mathf.Abs(over), Mathf.Abs(droop));
                }
                b.EvaluateCall(.66f, raise, contact, 1.2f, out float w66, out float over66, out _); b.EvaluateCall(.80f, raise, contact, 1.2f, out _, out float over80, out _);
                c.Add("V9.call_contact_span_untouched", same == 0f && w66 == 1f && over66 > 0f && over66 <= main.CallEnd.MaxOverShare && over80 == 0f,
                    "progress 0 - .62: arm weight, overshoot and droop differ from today by " + F(same, "G3") + "; at .66 the arm is still extended (weight " + F(w66, "F2") + ", overshoot " + F(over66, "F3") + "), at .80 the overshoot is exactly 0");
            }
            finally { Object.DestroyImmediate(main); Object.DestroyImmediate(preview); }
        }

        // ---------------------------------------------------------------- camera owner (a real render in a preview scene)

        static GameObject FixtureObject(string name, Scene preview, Transform parent = null)
        {
            var go = new GameObject(FixturePrefix + "_" + name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go, preview);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        static string CameraNestingOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: cam:nesting runs in Edit Mode";
            var c = new Checks(); CheckCameraOwner(c); return Finish(c, "cam_nesting");
        }

        static void CheckCameraOwner(Checks c)
        {
            Scene preview = default; RenderTexture rt = null;
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                var pivot = FixtureObject("Pivot", preview);
                pivot.transform.SetPositionAndRotation(new Vector3(3277f, 180f, 2252f), Quaternion.Euler(12f, 135f, 0f));   // the main scene's coordinate range
                var camObject = FixtureObject("Camera", preview, pivot.transform);
                camObject.transform.localPosition = new Vector3(0f, .55f, -2.8f);
                var cam = camObject.AddComponent<Camera>(); cam.enabled = false; cam.fieldOfView = 60f; cam.scene = preview;
                rt = new RenderTexture(320, 180, 16) { name = FixturePrefix + "_RT", hideFlags = HideFlags.HideAndDontSave }; cam.targetTexture = rt;
                var rig = pivot.AddComponent<CameraRigController>();   // edit mode: no Awake / OnEnable, nothing subscribes on its own
                typeof(CameraRigController).GetField("_camera", Inst).SetValue(rig, camObject.transform);
                typeof(CameraRigController).GetField("_cameraPivot", Inst).SetValue(rig, pivot.transform);
                OwnerRows(c, rig, cam, camObject.transform, () => cam.Render());
            }
            finally
            {
                if (preview.IsValid())
                {
                    foreach (var root in preview.GetRootGameObjects())
                    {
                        foreach (var rig in root.GetComponentsInChildren<CameraRigController>(true)) rig.ClearRenderOffset308();
                        Object.DestroyImmediate(root);
                    }
                    EditorSceneManager.ClosePreviewScene(preview);
                }
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            }
        }

        // <owner-rows>
        // The rows of the camera owner. They drive the owner through a render and use nothing of the editor, so the same text is
        // compiled outside the editor against a float32 model of Transform / Camera / the pipeline's event order and run in both
        // float modes of the JIT (Tools/Unity/Stage308_juice/checks/owner_model.py). Rules of this region (check repair 2026-10-05):
        //   · A float is compared by its bits (Bits / SameBits) or against a stated tolerance - never `a == b + c`. The editor's JIT
        //     keeps float arithmetic at double precision, so such a line compares a float32 with a double sum (old R3).
        //   · A position is never compared with Vector3 == (it accepts 1e-5 m and so is neither "bit-identical" nor a tolerance
        //     anyone chose) - bits only.
        //   · The stand-ins copy the existing per-camera writers: the field-of-view breath (ImpactFrameDirector308.Breathe) and the
        //     view shake (Riposte301 / Guardian302Actor: they save and restore the WORLD position). At main-scene coordinates
        //     (a world coordinate near 3277 has a float32 spacing of 2.4e-4 m) that round trip does not give the local position back
        //     bit for bit - with or without the owner. So the owner's position rows compare (a) a render of the owner alone
        //     against the bits before it and (b) a render with the shake against the same render without the owner, bit for bit
        //     (old R5 / R7 / R8 compared against the value before the shake and so measured the stand-in, not the owner).
        //   · Every clause of a row is printed (yes / NO), so a FAIL says which clause failed.
        sealed class Clauses
        {
            readonly StringBuilder text = new StringBuilder(); public bool Ok = true;
            public Clauses Add(string name, bool ok) { Ok &= ok; text.Append(text.Length > 0 ? ", " : "").Append(name).Append(ok ? " yes" : " NO"); return this; }
            public override string ToString() => text.ToString();
        }

        static int Bits(float v) => BitConverter.ToInt32(BitConverter.GetBytes(v), 0);   // the float32 itself: a float parameter is 32 bits in every JIT mode
        static bool SameBits(float a, float b) => Bits(a) == Bits(b);
        static bool SameBits(Vector3 a, Vector3 b) => Bits(a.x) == Bits(b.x) && Bits(a.y) == Bits(b.y) && Bits(a.z) == Bits(b.z);
        static bool Same(Quaternion a, Quaternion b) => Bits(a.x) == Bits(b.x) && Bits(a.y) == Bits(b.y) && Bits(a.z) == Bits(b.z) && Bits(a.w) == Bits(b.w);
        static bool Zero(Vector3 v) => v.x == 0f && v.y == 0f && v.z == 0f;   // exact (Vector3 == is approximate)
        static string Hex(float v) => Bits(v).ToString("X8");

        // angle between two rotations in degrees, without the floor of Quaternion.Angle (that returns 0 below about 0.16 degrees:
        // it treats a dot product above 1 - 1e-6 as "equal")
        static float AngleDegrees(Quaternion a, Quaternion b)
        {
            Quaternion d = Quaternion.Inverse(a) * b;
            return 2f * Mathf.Atan2(Mathf.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z), Mathf.Abs(d.w)) * Mathf.Rad2Deg;
        }

        static void OwnerRows(Checks c, CameraRigController rig, Camera cam, Transform view, Action render)
        {
            var order = new List<string>(8);
            const float breath = 1.2f;
            float fovInner = 0f, fovAtDraw = 0f, savedFov = 0f; bool standIns = true;
            Quaternion rotationInner = Quaternion.identity, rotationAtDraw = Quaternion.identity; Vector3 positionAtDraw = Vector3.zero, savedPosition = Vector3.zero;
            Action<ScriptableRenderContext, List<Camera>> contextBegin = (x, cams) => { if (cams.Contains(cam)) order.Add("context-begin"); };
            Action<ScriptableRenderContext, List<Camera>> contextEnd = (x, cams) => { if (cams.Contains(cam)) order.Add("context-end"); };
            // stand-ins for the existing per-camera writers: the field-of-view breath (ImpactFrameDirector308) and a view shake
            // (Riposte301 / Guardian302Actor) - the same statements as theirs; standIns = false leaves the owner alone on the camera
            Action<ScriptableRenderContext, Camera> cameraBegin = (x, k) =>
            {
                if (k != cam) return; order.Add("camera-begin");
                fovInner = k.fieldOfView; rotationInner = k.transform.localRotation;
                if (standIns)
                {
                    savedFov = k.fieldOfView; k.fieldOfView += breath;
                    savedPosition = k.transform.position; k.transform.position += k.transform.rotation * new Vector3(.045f, -.02f, 0f);
                }
                fovAtDraw = k.fieldOfView; rotationAtDraw = k.transform.localRotation; positionAtDraw = k.transform.localPosition;
            };
            Action<ScriptableRenderContext, Camera> cameraEnd = (x, k) =>
            {
                if (k != cam) return; order.Add("camera-end");
                if (standIns) { k.fieldOfView = savedFov; k.transform.position = savedPosition; }
            };
            Quaternion rotation0 = view.localRotation; Vector3 position0 = view.localPosition; float fov0 = cam.fieldOfView;
            RenderPipelineManager.beginContextRendering += contextBegin; RenderPipelineManager.endContextRendering += contextEnd;
            RenderPipelineManager.beginCameraRendering += cameraBegin; RenderPipelineManager.endCameraRendering += cameraEnd;
            try
            {
                // (1) no offset: no subscription, an ordinary render. It is also the control of the position rows: what the
                //     stand-in shake alone leaves behind.
                render();
                bool rendered = order.Count == 4;
                c.Add("R1.render_events_fire", rendered, "one edit-mode render of the fixture camera raised: " + (order.Count == 0 ? "(nothing - the pipeline did not raise its events here; the rows below are UNCONFIRMED)" : string.Join(" < ", order)));
                if (!rendered) return;
                c.Add("R2.nesting_order", order.SequenceEqual(new[] { "context-begin", "camera-begin", "camera-end", "context-end" }), "context-begin < camera-begin < camera-end < context-end");
                Vector3 positionOff = view.localPosition; float residueOff = (positionOff - position0).magnitude;
                float breathDrawn = fov0 + breath;   // stored as a float32 first, then compared by bits
                var k3 = new Clauses().Add("owner applied nothing", rig.RenderOffsetApplied308 == 0)
                    .Add("the per-camera hook saw the rig's field of view", SameBits(fovInner, fov0)).Add("and the rig's rotation", Same(rotationInner, rotation0))
                    .Add("the breath drew at rig value + breath (float32 bits)", SameBits(fovAtDraw, breathDrawn))
                    .Add("rotation after = before (bits)", Same(view.localRotation, rotation0)).Add("field of view after = before (bits)", SameBits(cam.fieldOfView, fov0));
                c.Add("R3.off_is_untouched", k3.Ok, "no offset set: the owner did nothing (applied " + rig.RenderOffsetApplied308 + "), the stand-in breath saw " + F(fovInner, "F3") + " and drew at " + F(fovAtDraw, "F3")
                    + " (bits " + Hex(fovAtDraw) + ", rig value + breath " + Hex(breathDrawn) + ") [" + k3 + "]");
                c.Note("stand-ins alone (no owner): after the render the local position is " + F(residueOff, "G3") + " m from where it was (bits " + (SameBits(positionOff, position0) ? "equal" : "differ")
                    + ") - the world-space save / restore of the existing shakes at these coordinates, not the owner; the position rows below compare against this render");

                // (2) offset on: the render sees rig value + offset (+ breath), and afterwards every value the owner touched is the
                //     rig's own, bit for bit. Each frame starts from the rig's own local position (CameraRigController.Update writes it).
                view.localPosition = position0; order.Clear(); rig.ResetRenderOffsetDiagnostics308();
                const float nod = .9f, roll = .5f, kick = .8f;
                rig.SetRenderOffset308(nod, roll, kick);
                render();
                Quaternion expected = rotation0 * Quaternion.Euler(nod, 0f, roll); float rotationOff = AngleDegrees(rotationAtDraw, expected);
                c.Add("R4.render_sees_the_sum", rig.RenderOffsetApplied308 == 1 && Mathf.Abs(fovInner - (fov0 + kick)) < 1e-4f && Mathf.Abs(fovAtDraw - (fov0 + kick + breath)) < 1e-4f && rotationOff < 1e-3f,
                    "field of view at the per-camera hook " + F(fovInner, "F3") + " (= " + F(fov0, "F1") + " + " + F(kick, "F1") + "), drawn at " + F(fovAtDraw, "F3") + " (+ breath " + F(breath, "F1") + "); rotation off the expected by " + F(rotationOff, "G3")
                    + " deg (limit 1e-3, measured without the 0.16 deg floor of Quaternion.Angle)");
                Vector3 positionOn = view.localPosition; float movedOn = Vector3.Distance(positionAtDraw, position0);
                var k5 = new Clauses().Add("rotation after = before (bits)", Same(view.localRotation, rotation0)).Add("field of view after = before (bits)", SameBits(cam.fieldOfView, fov0))
                    .Add("the offset is off the camera", !rig.RenderOffsetOnCamera308).Add("local position = the same render without the owner (bits)", SameBits(positionOn, positionOff));
                c.Add("R5.exact_restore", k5.Ok, "after the render: local rotation and field of view are bit-identical to before; the local position is bit-identical to the same render without the owner ("
                    + F((positionOn - position0).magnitude, "G3") + " m from the start with the owner, " + F(residueOff, "G3") + " m without) [" + k5 + "]");
                c.Add("R6.no_conflict", rig.RenderOffsetConflicts308 == 0, "restores that found another writer's value: " + rig.RenderOffsetConflicts308);

                // (2b) the owner alone on the camera (no stand-in writes anything): the position bits must not change at all
                standIns = false; view.localPosition = position0; rig.ResetRenderOffsetDiagnostics308();
                render();
                var k7 = new Clauses().Add("the stand-in shake moved the view for its render", movedOn > .04f)
                    .Add("owner alone: its offset reached the render", rig.RenderOffsetApplied308 == 1 && rig.RenderOffsetConflicts308 == 0 && Mathf.Abs(fovInner - (fov0 + kick)) < 1e-4f)
                    .Add("owner alone: local position bits = before", SameBits(view.localPosition, position0))
                    .Add("owner alone: rotation and field of view bits = before", Same(view.localRotation, rotation0) && SameBits(cam.fieldOfView, fov0))
                    .Add("with the shake: local position = the shake alone (bits)", SameBits(positionOn, positionOff));
                standIns = true;
                c.Add("R7.position_never_written", k7.Ok, "the stand-in shake moved the view " + F(movedOn, "F3") + " m for the render; the owner wrote no position: alone it leaves the position bits untouched, and with the shake the result is the shake's own [" + k7 + "]");

                // (3) 200 renders with changing offsets: nothing accumulates. The control comes first: 200 renders of the stand-ins
                //     alone from the same start (no reset in between - an accumulating error would show).
                const int count = 200; var control = new Vector3[count];
                rig.ClearRenderOffset308(); view.localPosition = position0;
                for (int i = 0; i < count; i++) { render(); control[i] = view.localPosition; }
                bool controlClean = Same(view.localRotation, rotation0) && SameBits(cam.fieldOfView, fov0);
                view.localPosition = position0; rig.ResetRenderOffsetDiagnostics308();
                var random = new System.Random(308); int rotationDiffers = 0, fovDiffers = 0, positionDiffers = 0;
                for (int i = 0; i < count; i++)
                {
                    rig.SetRenderOffset308((float)random.NextDouble() * 2.4f - 1.2f, (float)random.NextDouble() * 1.2f - .6f, (float)random.NextDouble() * 3f - 1.5f);
                    render();
                    if (!Same(view.localRotation, rotation0)) rotationDiffers++;
                    if (!SameBits(cam.fieldOfView, fov0)) fovDiffers++;
                    if (!SameBits(view.localPosition, control[i])) positionDiffers++;
                }
                var k8 = new Clauses().Add("rotation never changed (bits)", rotationDiffers == 0).Add("field of view never changed (bits)", fovDiffers == 0)
                    .Add("local position = the stand-ins alone on every render (bits)", positionDiffers == 0).Add("every render carried an offset", rig.RenderOffsetApplied308 == count)
                    .Add("the control left rotation and field of view alone", controlClean).Add("conflicts 0", rig.RenderOffsetConflicts308 == 0);
                c.Add("R8.no_drift_200_renders", k8.Ok, "200 renders with random offsets: the camera's own values never changed (rotation differs on " + rotationDiffers + " render(s), field of view on " + fovDiffers
                    + "); the local position follows the 200 renders without the owner bit for bit (differs on " + positionDiffers + "; the stand-in shake alone ends " + F((control[count - 1] - position0).magnitude, "G3")
                    + " m from the start, after its first render " + F((control[0] - position0).magnitude, "G3") + " m) (conflicts " + rig.RenderOffsetConflicts308 + ") [" + k8 + "]");
                view.localPosition = position0;

                // (4) the draw-mode lock of the owner itself
                var drawing = typeof(CameraRigController).GetField("_drawing", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                drawing.SetValue(rig, true);
                rig.ResetRenderOffsetDiagnostics308(); rig.SetRenderOffset308(nod, roll, kick); order.Clear(); render();
                c.Add("R9.owner_refuses_while_drawing", rig.RenderOffsetApplied308 == 0 && rig.RenderOffsetRefusedDrawing308 >= 1 && Zero(rig.RenderOffset308) && Mathf.Abs(fovInner - fov0) < 1e-6f,
                    "draw flag on: the offset is refused (" + rig.RenderOffsetRefusedDrawing308 + "), the render saw the rig's own field of view " + F(fovInner, "F3"));
                drawing.SetValue(rig, false);

                // (5) cleared: the subscription is gone
                rig.ClearRenderOffset308(); rig.ResetRenderOffsetDiagnostics308(); render();
                c.Add("R10.cleared_is_unsubscribed", rig.RenderOffsetApplied308 == 0 && Zero(rig.RenderOffset308), "after ClearRenderOffset308 a render applies nothing");
            }
            finally
            {
                RenderPipelineManager.beginContextRendering -= contextBegin; RenderPipelineManager.endContextRendering -= contextEnd;
                RenderPipelineManager.beginCameraRendering -= cameraBegin; RenderPipelineManager.endCameraRendering -= cameraEnd;
                rig.ClearRenderOffset308();
            }
        }
        // </owner-rows>

        // ---------------------------------------------------------------- borders and the open scene (read only)

        static void CheckBorders(Checks c)
        {
            string scripts = Path.Combine(Application.dataPath, "_Project/Scripts");
            string[] forbidden = { "Oheangbu.App", "Oheangbu.Presentation", "Oheangbu.Combat", "Oheangbu.BrushRender" };
            foreach (string name in new[] { "Drawing", "Spellcraft" })
            {
                string dir = Path.Combine(scripts, name);
                string asmdef = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.asmdef").FirstOrDefault() : null;
                string text = asmdef != null ? File.ReadAllText(asmdef) : "";
                int mentions = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Count(f => File.ReadAllText(f).Contains("Juice308")) : -1;
                c.Add("B1." + name.ToLowerInvariant() + "_cannot_see_the_juice", asmdef != null && !forbidden.Any(f => text.Contains("\"" + f + "\"")) && mentions == 0,
                    "Oheangbu." + name + " references none of App / Presentation / Combat / BrushRender; sources that mention Juice308: " + mentions);
            }
            c.Add("B2.core_data_types", typeof(PlayerJuice308ProfileSO).Assembly.GetName().Name == "Oheangbu.Data" && typeof(PlayerJuice308Curves).Assembly.GetName().Name == "Oheangbu.Presentation"
                && typeof(PlayerJuice308Solver).Assembly.GetName().Name == "Oheangbu.App" && typeof(CameraRigController).Assembly.GetName().Name == "Oheangbu.Combat",
                "profile in Oheangbu.Data, curves in Oheangbu.Presentation, solver in Oheangbu.App, camera owner in Oheangbu.Combat");
            var statics = new[] { typeof(PlayerJuice308Solver), typeof(PlayerJuice308Curves), typeof(PlayerJuice308ProfileSO) }
                .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(f => !f.IsLiteral && !f.IsInitOnly).Select(f => t.Name + "." + f.Name)).ToArray();
            c.Add("B3.no_static_state", statics.Length == 0, "static mutable fields in profile / curves / solver: " + (statics.Length == 0 ? "0" : string.Join(", ", statics)));
        }

        static WorldMacroPlayerGestureRig SceneRig(Scene scene, out string issue)
        {
            var rigs = Object.FindObjectsByType<WorldMacroPlayerGestureRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => r.gameObject.scene == scene).ToArray();
            issue = rigs.Length == 1 ? null : rigs.Length + " WorldMacroPlayerGestureRig in " + scene.name + " (need exactly one)";
            return rigs.Length == 1 ? rigs[0] : null;
        }

        static void CheckScene(Checks c, Scene scene)
        {
            var rig = SceneRig(scene, out string issue);
            if (rig == null) { c.Note("scene: " + issue + " - the scene rows are skipped"); return; }
            var so = new SerializedObject(rig);
            Object feed = so.FindProperty("_brushFeed")?.objectReferenceValue, cameraRig = so.FindProperty("_cameraRig")?.objectReferenceValue, drawing = so.FindProperty("_drawing")?.objectReferenceValue;
            c.Add("W1.rig_has_its_sources", feed != null && cameraRig != null && drawing != null, "open scene " + scene.name + ": rig '" + rig.name + "' brush feed " + (feed != null) + ", camera rig " + (cameraRig != null) + ", drawing input " + (drawing != null) + " (the juice needs no other wiring)");
            c.Add("W2.lean_on_the_rig_object", rig.GetComponent<WorldMacroPlayerLean303>() != null, "WorldMacroPlayerLean303 sits on the rig's object (it asks the rig with GetComponent): " + (rig.GetComponent<WorldMacroPlayerLean303>() != null));
            var asset = AssetDatabase.LoadAssetAtPath<PlayerJuice308ProfileSO>(ProfileAssetPath);
            c.Note("profile: slot " + (so.FindProperty("_juiceProfile308")?.objectReferenceValue != null ? "set" : "empty") + ", Resources asset " + (asset != null ? "present (" + ProfileAssetPath + ")" : "absent -> the juice does not run; B-1 still does"));
            var bristles = rig.GetComponentsInChildren<BrushBristleRig>(true);
            int withSplay = 0; foreach (var b in bristles) { var s = new SerializedObject(b); var r = s.FindProperty("_bristleRenderer")?.objectReferenceValue as SkinnedMeshRenderer; string shape = s.FindProperty("_splayBlendShape")?.stringValue; if (r != null && r.sharedMesh != null && !string.IsNullOrEmpty(shape) && r.sharedMesh.GetBlendShapeIndex(shape) >= 0) withSplay++; }
            c.Note("bristle rigs under the rig object: " + bristles.Length + ", with the splay blendshape: " + withSplay + " (A-1's splay needs it on the close-up brush; searched under the rig object only)");
        }

        // ---------------------------------------------------------------- timeline (solver alone)

        static string Timeline(string name, string hzText)
        {
            int hz = int.TryParse(hzText, out int parsed) && parsed >= 10 && parsed <= 480 ? parsed : 60;
            var profile = CaptureProfile(true, out string profileSource);
            try
            {
                var s = new PlayerJuice308Solver(); s.Configure(profile); float dt = 1f / hz;
                // (time, event): the same event names as the python timelines
                var events = new List<(float t, string e)>();
                float seconds;
                switch (name)
                {
                    case "cast": events.Add((0f, "cast")); seconds = .4f; break;
                    case "call": events.Add((0f, "call")); seconds = .4f; break;
                    case "setdown": events.Add((0f, "setdown")); seconds = .45f; break;
                    case "stack": events.Add((0f, "cast")); events.Add((0f, "call")); events.Add((0f, "setdown")); seconds = .5f; break;
                    case "draw":
                        events.AddRange(new[] { (0f, "mode"), (.10f, "start"), (.40f, "end"), (.50f, "start"), (.80f, "end"), (1.00f, "commit"), (1.05f, "mode"), (1.15f, "start"), (1.45f, "end"), (1.60f, "commit") });
                        seconds = 2.1f; break;
                    default: return "refused: timeline:<cast|call|setdown|stack|draw>[:hz]";
                }
                var sb = new StringBuilder(); sb.Append("{\"name\":\"").Append(name).Append("\",\"hz\":").Append(hz).Append(",\"profile\":\"preview (A-1 / A-2 / D-3 on) of ").Append(profileSource).Append("\",\"frames\":[");
                bool drawing = false, stroking = false; int index = 0, frames = Mathf.RoundToInt(seconds * hz); Vector3 peak = Vector3.zero; float shaftPeak = 0f; int nonZeroWhileDrawing = 0;
                for (int f = 0; f <= frames; f++)
                {
                    float t = f * dt;
                    while (index < events.Count && events[index].t <= t + 1e-6f)
                    {
                        switch (events[index].e)
                        {
                            case "mode": drawing = true; s.ModeEntered(); break;
                            case "start": stroking = true; s.StrokeStarted(); break;
                            case "end": stroking = false; s.StrokeEnded(); break;
                            case "commit": drawing = false; stroking = false; s.DrawingEnded(); s.CastKick(1f, 1f); break;
                            default: Fire(s, events[index].e); break;
                        }
                        index++;
                    }
                    if (stroking) s.NoteStrokeVelocity(new Vector2(2.2f, -.6f), dt);
                    s.Advance(dt); s.Evaluate(!drawing, out JuiceFrame308 frame);
                    if (drawing && (frame.Camera.x != 0f || frame.Camera.y != 0f || frame.Camera.z != 0f)) nonZeroWhileDrawing++;
                    peak = Vector3.Max(peak, new Vector3(Mathf.Abs(frame.Camera.x), Mathf.Abs(frame.Camera.y), Mathf.Abs(frame.Camera.z)));
                    shaftPeak = Mathf.Max(shaftPeak, frame.ShaftRaiseDegrees + frame.ShaftLeanDegrees.magnitude);
                    if (f > 0) sb.Append(',');
                    sb.Append("{\"t\":").Append(F(t)).Append(",\"drawing\":").Append(drawing ? "true" : "false").Append(",\"tilt\":").Append(F(frame.ShaftRaiseDegrees))
                        .Append(",\"leanX\":").Append(F(frame.ShaftLeanDegrees.x)).Append(",\"leanY\":").Append(F(frame.ShaftLeanDegrees.y)).Append(",\"splay\":").Append(F(frame.SplayAdd))
                        .Append(",\"nod\":").Append(F(frame.Camera.x)).Append(",\"roll\":").Append(F(frame.Camera.y)).Append(",\"fov\":").Append(F(frame.Camera.z)).Append('}');
                }
                sb.Append("]}");
                Directory.CreateDirectory(OutRoot);
                string file = Path.Combine(OutRoot, "timeline_" + name + "_" + hz + ".json"); File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
                return "timeline " + name + " @" + hz + " Hz (" + profileSource + ", preview): " + (frames + 1) + " frames; camera peak nod " + F(peak.x, "F3") + " roll " + F(peak.y, "F3") + " fov " + F(peak.z, "F3") + " deg; shaft peak " + F(shaftPeak, "F2")
                    + " deg; frames with a camera offset while drawing: " + nonZeroWhileDrawing + " -> " + file;
            }
            finally { Object.DestroyImmediate(profile); }
        }

        // ---------------------------------------------------------------- camera strip (the open scene's game camera)

        static string CameraStrip(string name, string variant)
        {
            if (name != "cast" && name != "call" && name != "setdown") return "refused: cam:strip:<cast|call|setdown>[:narrow]";
            if (variant.Length > 0 && variant != "narrow") return "refused: the only variant is 'narrow' (the field-of-view kick with the other sign)";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: cam:strip runs in Edit Mode (the feel itself is a Play item)";
            var scene = SceneManager.GetActiveScene();
            var rigs = Object.FindObjectsByType<CameraRigController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.gameObject.scene == scene).ToArray();
            if (rigs.Length != 1) return "refused: " + rigs.Length + " CameraRigController in " + scene.name + " (need exactly one)";
            var rig = rigs[0]; var view = typeof(CameraRigController).GetField("_camera", Inst).GetValue(rig) as Transform;
            var cam = view != null ? view.GetComponent<Camera>() : null;
            if (cam == null) return "refused: the camera rig has no camera";
            bool dirtyBefore = scene.isDirty;
            Quaternion rotation0 = view.localRotation; Vector3 position0 = view.localPosition; float fov0 = cam.fieldOfView; var target0 = cam.targetTexture;
            float[] times = { 0f, .03f, .05f, .08f, .12f, .18f, .25f, .33f };
            const int w = 640, h = 360;
            var profile = CaptureProfile(false, out string profileSource); RenderTexture rt = null; Texture2D sheet = null, tile = null;
            if (variant == "narrow")
            {
                // the same reaction with the field of view narrowing instead of widening (decision: sign of the kick); memory copy only
                profile.CastKick.FovDegrees = -Mathf.Abs(profile.CastKick.FovDegrees); profile.SetDownThump.FovDegrees = -Mathf.Abs(profile.SetDownThump.FovDegrees);
                profileSource += ", field-of-view sign flipped (narrow)";
            }
            float seenFov = 0f; Quaternion seenRotation = Quaternion.identity; bool seen = false;
            Action<ScriptableRenderContext, Camera> probe = (x, k) => { if (k == cam) { seenFov = k.fieldOfView; seenRotation = k.transform.localRotation; seen = true; } };
            var rows = new StringBuilder(); bool restored = true;
            try
            {
                rt = new RenderTexture(w, h, 24) { name = FixturePrefix + "_StripRT", hideFlags = HideFlags.HideAndDontSave };
                sheet = new Texture2D(w * times.Length, h * 2, TextureFormat.RGB24, false) { name = FixturePrefix + "_Sheet", hideFlags = HideFlags.HideAndDontSave };
                tile = new Texture2D(w, h, TextureFormat.RGB24, false) { name = FixturePrefix + "_Tile", hideFlags = HideFlags.HideAndDontSave };
                RenderPipelineManager.beginCameraRendering += probe;
                rig.ResetRenderOffsetDiagnostics308();
                for (int row = 0; row < 2; row++)   // row 0 = reaction on, row 1 = off (the same frames, for the eye)
                    for (int i = 0; i < times.Length; i++)
                    {
                        var s = new PlayerJuice308Solver(); s.Configure(profile); Fire(s, name);
                        JuiceFrame308 frame = default; const float dt = .005f; int steps = Mathf.RoundToInt(times[i] / dt);
                        for (int k = 0; k <= steps; k++) { s.Advance(dt); s.Evaluate(true, out frame); }
                        Vector3 offset = row == 0 ? frame.Camera : Vector3.zero;
                        rig.SetRenderOffset308(offset.x, offset.y, offset.z);
                        seen = false; cam.targetTexture = rt; cam.Render(); cam.targetTexture = target0;
                        rig.ClearRenderOffset308();
                        restored &= Same(view.localRotation, rotation0) && SameBits(cam.fieldOfView, fov0) && SameBits(view.localPosition, position0);   // bits (Vector3 == accepts 1e-5 m)
                        var active = RenderTexture.active; RenderTexture.active = rt; tile.ReadPixels(new Rect(0, 0, w, h), 0, 0); tile.Apply(false); RenderTexture.active = active;
                        sheet.SetPixels(i * w, (1 - row) * h, w, h, tile.GetPixels());
                        if (row == 0)
                            rows.Append(rows.Length > 0 ? "," : "").Append("{\"ms\":").Append(F(times[i] * 1000f, "F0")).Append(",\"nod\":").Append(F(offset.x)).Append(",\"roll\":").Append(F(offset.y)).Append(",\"fov\":").Append(F(offset.z))
                                .Append(",\"renderSawFov\":").Append(seen ? F(seenFov) : "null").Append(",\"renderSawNodDegrees\":").Append(seen ? F(AngleDegrees(seenRotation, rotation0)) : "null")
                                .Append(",\"shiftPx1080\":").Append(F(540f * Mathf.Tan(offset.x * Mathf.Deg2Rad) / Mathf.Tan(fov0 * .5f * Mathf.Deg2Rad), "F1")).Append('}');
                    }
                sheet.Apply(false);
                string dir = Path.Combine(OutRoot, "P8"); Directory.CreateDirectory(dir); string stamp = Stamp();
                string tag = name + (variant.Length > 0 ? "_" + variant : "");
                string png = Path.Combine(dir, "cam_" + tag + "_" + stamp + ".png"), json = Path.Combine(dir, "cam_" + tag + "_" + stamp + ".json");
                File.WriteAllBytes(png, sheet.EncodeToPNG());
                File.WriteAllText(json, "{\"event\":\"" + name + "\",\"scene\":\"" + scene.name + "\",\"baseFov\":" + F(fov0) + ",\"profile\":\"" + profileSource + ", full power\",\"topRow\":\"reaction on\",\"bottomRow\":\"off\",\"frames\":[" + rows + "],"
                    + "\"applied\":" + rig.RenderOffsetApplied308 + ",\"conflicts\":" + rig.RenderOffsetConflicts308 + ",\"cameraRestoredBitExact\":" + (restored ? "true" : "false") + "}", new UTF8Encoding(false));
                return "cam:strip " + tag + " (" + profileSource + "): " + times.Length + " frames x on / off -> " + png + " ; values the render saw -> " + json + " ; camera restored bit-exact " + restored + ", conflicts " + rig.RenderOffsetConflicts308
                    + ", scene dirty " + scene.isDirty + " (before " + dirtyBefore + ")" + (seen ? "" : " ; WARNING: the per-camera probe never fired (stills are unconfirmed)");
            }
            finally
            {
                RenderPipelineManager.beginCameraRendering -= probe;
                rig.ClearRenderOffset308(); cam.targetTexture = target0;
                if (!Same(view.localRotation, rotation0)) view.localRotation = rotation0;
                if (cam.fieldOfView != fov0) cam.fieldOfView = fov0;
                Object.DestroyImmediate(profile);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (sheet != null) Object.DestroyImmediate(sheet);
                if (tile != null) Object.DestroyImmediate(tile);
            }
        }

        // ---------------------------------------------------------------- recognition identity (AC-J1 b)

        [Serializable] sealed class FixtureStroke { public float[] points; }   // x, y, time triples (pixels at the fixture's screen size, seconds)
        [Serializable] sealed class Fixture { public string name; public string expected; public int width, height; public FixtureStroke[] strokes; }

        static List<StrokeData> Feed(Fixture fixture, PlayerJuice308Solver juice, CameraRigController owner, out byte[] bytes, out int points, out int cameraFrames)
        {
            // The samples go from the fixture into StrokeData exactly as DrawingInputController.HandleStroke stores them:
            // new StrokePoint(screen, time). When a solver is given it runs beside the feed with the same events and frame times
            // (and pushes its camera part to an owner) - it has no reference to the stroke list, which is the point of the check.
            var list = new List<StrokeData>(); points = 0; cameraFrames = 0;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                juice?.ModeEntered();
                float last = 0f;
                foreach (var stroke in fixture.strokes)
                {
                    var data = new StrokeData(); list.Add(data);
                    juice?.StrokeStarted();
                    Vector2 previous = default; float previousTime = 0f;
                    for (int i = 0; i + 2 < stroke.points.Length; i += 3)
                    {
                        var p = new Vector2(stroke.points[i], stroke.points[i + 1]); float t = stroke.points[i + 2];
                        data.Add(new StrokePoint(p, t)); points++;
                        writer.Write(p.x); writer.Write(p.y); writer.Write(t);
                        if (juice != null)
                        {
                            float dt = i == 0 ? Mathf.Max(0f, t - last) : Mathf.Max(0f, t - previousTime);
                            if (i > 0 && dt > 0f) juice.NoteStrokeVelocity((p - previous) / dt / fixture.height * 2f, dt);
                            juice.Advance(dt); juice.Evaluate(false, out JuiceFrame308 frame);   // draw mode: the camera part is refused here
                            if (frame.Camera.x != 0f || frame.Camera.y != 0f || frame.Camera.z != 0f) cameraFrames++;
                            if (owner != null) owner.SetRenderOffset308(frame.Camera.x, frame.Camera.y, frame.Camera.z);
                        }
                        previous = p; previousTime = t; last = t;
                    }
                    juice?.StrokeEnded();
                }
                writer.Flush(); bytes = stream.ToArray();
            }
            return list;
        }

        static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2"))); }

        static string RecogIdentity(string fixtureName)
        {
            if (string.IsNullOrEmpty(fixtureName)) fixtureName = "stroke_na";
            string path = Path.Combine(StageRoot, "fixtures/" + fixtureName + ".json");
            if (!File.Exists(path)) return "refused: missing fixture " + path;
            var fixture = JsonUtility.FromJson<Fixture>(File.ReadAllText(path));
            if (fixture == null || fixture.strokes == null || fixture.strokes.Length == 0) return "refused: the fixture has no strokes";
            string[] guids = AssetDatabase.FindAssets("t:JamoTemplateLibrarySO");
            if (guids.Length == 0) return "refused: no JamoTemplateLibrarySO asset in the project";
            string libraryPath = guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal).First();
            var library = AssetDatabase.LoadAssetAtPath<JamoTemplateLibrarySO>(libraryPath);
            var c = new Checks(); var profile = NewProfile(true); Scene preview = default;
            try
            {
                var pipelineOff = new RecognitionPipeline(); pipelineOff.Initialize(library);
                var pipelineOn = new RecognitionPipeline(); pipelineOn.Initialize(library);
                if (!pipelineOff.IsReady) return "refused: the template library " + libraryPath + " is empty";
                // off: the fixture alone
                var off = Feed(fixture, null, null, out byte[] bytesOff, out int pointsOff, out _);
                var resultOff = pipelineOff.Recognize(off, fixture.height);
                // on: the same fixture while the juice runs beside it (every effect on, a real camera owner receiving its part)
                preview = EditorSceneManager.NewPreviewScene();
                var pivot = FixtureObject("RecogPivot", preview); var camObject = FixtureObject("RecogCamera", preview, pivot.transform);
                var cam = camObject.AddComponent<Camera>(); cam.enabled = false;
                var owner = pivot.AddComponent<CameraRigController>();
                typeof(CameraRigController).GetField("_camera", Inst).SetValue(owner, camObject.transform);
                typeof(CameraRigController).GetField("_drawing", Inst).SetValue(owner, true);   // as in a real draw mode
                var solver = new PlayerJuice308Solver(); solver.Configure(profile);
                solver.CastKick(1f, 1f);   // a kick still running from the cast before, on purpose
                var on = Feed(fixture, solver, owner, out byte[] bytesOn, out int pointsOn, out int cameraFrames);
                var resultOn = pipelineOn.Recognize(on, fixture.height);
                char letterOff = default, letterOn = default;
                bool composedOff = resultOff.Success && HangulComposer.TryCompose(resultOff.InitialName, resultOff.MedialName, resultOff.FinalName, out letterOff, out _, out _, out _);
                bool composedOn = resultOn.Success && HangulComposer.TryCompose(resultOn.InitialName, resultOn.MedialName, resultOn.FinalName, out letterOn, out _, out _, out _);
                float durationOff = off[off.Count - 1].EndTime - off[0].StartTime, durationOn = on[on.Count - 1].EndTime - on[0].StartTime;
                c.Add("I1.input_bytes_identical", bytesOff.Length == bytesOn.Length && Hash(bytesOff) == Hash(bytesOn), "recogniser input (x, y, time as float32, " + pointsOff + " points, " + bytesOff.Length + " bytes): sha256 off " + Hash(bytesOff).Substring(0, 16) + " on " + Hash(bytesOn).Substring(0, 16));
                c.Add("I2.result_identical", resultOff.Success == resultOn.Success && resultOff.InitialName == resultOn.InitialName && resultOff.MedialName == resultOn.MedialName && resultOff.FinalName == resultOn.FinalName
                    && BitConverter.SingleToInt32Bits(resultOff.WorstDistance) == BitConverter.SingleToInt32Bits(resultOn.WorstDistance) && BitConverter.SingleToInt32Bits(resultOff.AverageDistance) == BitConverter.SingleToInt32Bits(resultOn.AverageDistance)
                    && resultOff.SplitGroupCount == resultOn.SplitGroupCount,
                    "off: success " + resultOff.Success + " '" + resultOff.InitialName + "' + '" + resultOff.MedialName + "' + '" + resultOff.FinalName + "' worst " + F(resultOff.WorstDistance, "F3") + " avg " + F(resultOff.AverageDistance, "F3") + " groups " + resultOff.SplitGroupCount + " | on: the same, bit for bit");
                c.Add("I3.letter_fields_identical", composedOff == composedOn && letterOff == letterOn && off.Count == on.Count && pointsOff == pointsOn && BitConverter.SingleToInt32Bits(durationOff) == BitConverter.SingleToInt32Bits(durationOn),
                    "letter '" + (composedOff ? letterOff.ToString() : "-") + "' strokes " + off.Count + " points " + pointsOff + " stroke time " + F(durationOff, "F3") + " s (off) = (on)");
                c.Add("I4.no_camera_while_drawing", cameraFrames == 0 && owner.RenderOffsetApplied308 == 0 && Zero(owner.RenderOffset308), "while the fixture was fed (a kick from the cast before still pending): frames with a camera offset " + cameraFrames + ", offsets on the owner " + owner.RenderOffsetApplied308);
                c.Note("fixture " + fixture.name + " (" + fixture.width + " x " + fixture.height + "), expected '" + fixture.expected + "', recognised '" + (composedOff ? letterOff.ToString() : "-") + "'" + (composedOff && fixture.expected == letterOff.ToString() ? "" : "  <- the fixture is synthetic; a mismatch here does not affect the identity rows") + "; templates " + libraryPath);
                c.Note("juice beside the feed: press starts " + solver.PressStarts + ", flick starts " + solver.FlickStarts + " (preview profile, every effect on)");
                return Finish(c, "recog_identity");
            }
            finally
            {
                Object.DestroyImmediate(profile);
                if (preview.IsValid())
                {
                    foreach (var root in preview.GetRootGameObjects()) { foreach (var rig in root.GetComponentsInChildren<CameraRigController>(true)) rig.ClearRenderOffset308(); Object.DestroyImmediate(root); }
                    EditorSceneManager.ClosePreviewScene(preview);
                }
            }
        }

        // ---------------------------------------------------------------- profile asset / preview profile / cleanup

        static string ProfileStatus()
        {
            var asset = AssetDatabase.LoadAssetAtPath<PlayerJuice308ProfileSO>(ProfileAssetPath);
            var sb = new StringBuilder("profile asset " + ProfileAssetPath + ": " + (asset != null ? "present" : "absent (the juice does not run; the rejection fix B-1 does)"));
            var p = asset != null ? asset : null;
            if (p != null)
                sb.Append("\n  Intensity ").Append(F(p.Intensity, "F2")).Append(" draw ").Append(F(p.DrawIntensity, "F2")).Append(" cast ").Append(F(p.CastIntensity, "F2")).Append(" call ").Append(F(p.CallIntensity, "F2")).Append(" camera ").Append(F(p.CameraIntensity, "F2"))
                    .Append("\n  A-1 ").Append(p.StrokeStart.Enabled).Append(" | A-2 ").Append(p.StrokeEnd.Enabled).Append(" | B-1 ").Append(p.Reject.SuppressOnReject).Append(" | B-2 ").Append(p.CastBeat.Enabled).Append(" | C-1 ").Append(p.CallEnd.Enabled).Append(" | C-4 ").Append(p.Arrival.Enabled)
                    .Append(" | D ").Append(p.Camera.Enabled).Append(" (D-1 ").Append(p.CastKick.Enabled).Append(", D-2a ").Append(p.CallSettle.Enabled).Append(", D-2b ").Append(p.SetDownThump.Enabled).Append(", D-3 ").Append(p.ThrowRoll.Enabled).Append(")")
                    .Append("\n  D-1 nod ").Append(F(p.CastKick.PitchDegrees, "F2")).Append(" fov ").Append(F(p.CastKick.FovDegrees, "F2")).Append(" | D-2a nod ").Append(F(p.CallSettle.PitchDegrees, "F2")).Append(" | D-2b nod ").Append(F(p.SetDownThump.PitchDegrees, "F2")).Append(" fov ").Append(F(p.SetDownThump.FovDegrees, "F2"))
                    .Append(" | ceilings ").Append(F(p.Camera.MaxPitchDegrees, "F1")).Append(" / ").Append(F(p.Camera.MaxRollDegrees, "F1")).Append(" / ").Append(F(p.Camera.MaxFovDegrees, "F1"));
            var rig = SceneRig(SceneManager.GetActiveScene(), out string issue);
            sb.Append("\nopen scene rig: ").Append(rig != null ? rig.name + (Application.isPlaying ? " -> profile in use: " + (rig.JuiceProfile308 != null ? rig.JuiceProfile308.name : "none") : " (Edit Mode: the rig resolves its profile when it runs)") : issue);
            ProfileStatus2(sb, asset, rig);   // #308 3차: the two new switches, the data file, asset against data, the live rig's parry tell
            return sb.ToString();
        }

        // #308 3차 (D308-24): the old ProfileCreate (an asset made of the class defaults) is gone - the main profile's values are
        // data now. profile:plan / create / apply / check / revert / remove live in Juice308Build.Juice2.cs.

        static string PreviewProfile(string mode)
        {
            if (!EditorApplication.isPlaying) return "refused: preview-profile is for a running Play (memory only; Edit Mode uses p1 / p2)";
            var rig = SceneRig(SceneManager.GetActiveScene(), out string issue);
            if (rig == null) return "refused: " + issue;
            foreach (var old in Resources.FindObjectsOfTypeAll<PlayerJuice308ProfileSO>().Where(p => p != null && p.name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToArray())
            { if (rig.JuiceProfile308 == old) rig.SetJuiceProfile308(null); Object.DestroyImmediate(old); }
            if (mode == "off") return "preview-profile off: the rig is back on its own profile (" + (rig.JuiceProfile308 != null ? rig.JuiceProfile308.name : "none") + ")";
            if (mode != "on") return "refused: preview-profile:on | preview-profile:off";
            var source = rig.JuiceProfile308;
            var copy = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<PlayerJuice308ProfileSO>();
            copy.name = FixturePrefix + "_PreviewProfile"; copy.hideFlags = HideFlags.HideAndDontSave; copy.EnablePreviewOnly();
            rig.SetJuiceProfile308(copy);
            return "preview-profile on: a memory copy of " + (source != null ? source.name : "the class defaults") + " with A-1 / A-2 / D-3 on drives the live rig until preview-profile:off or the end of this Play. No asset or scene was changed.";
        }

        static int CountFixtures()
            => Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g != null && g.name.StartsWith(FixturePrefix, StringComparison.Ordinal))
               + Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t => t != null && t.name.StartsWith(FixturePrefix, StringComparison.Ordinal))
               + Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t != null && t.name.StartsWith(FixturePrefix, StringComparison.Ordinal))
               + Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m != null && m.name.StartsWith(FixturePrefix, StringComparison.Ordinal))
               + Resources.FindObjectsOfTypeAll<Material>().Count(m => m != null && m.name.StartsWith(FixturePrefix, StringComparison.Ordinal))
               + Resources.FindObjectsOfTypeAll<PlayerJuice308ProfileSO>().Count(p => p != null && p.name.StartsWith(FixturePrefix, StringComparison.Ordinal));

        static string CleanupFixtures()
        {
            int n = 0;
            foreach (var rig in Resources.FindObjectsOfTypeAll<CameraRigController>().Where(r => r != null && r.gameObject.name.StartsWith(FixturePrefix, StringComparison.Ordinal))) rig.ClearRenderOffset308();
            // a rig still holding a fixture profile in its slot lets go of it (the slot is read directly: the getter would do a Resources lookup)
            var slot = typeof(WorldMacroPlayerGestureRig).GetField("_juiceProfile308", Inst);
            if (!Application.isPlaying && slot != null)
                foreach (var rig in Resources.FindObjectsOfTypeAll<WorldMacroPlayerGestureRig>())
                {
                    var held = rig != null ? slot.GetValue(rig) as PlayerJuice308ProfileSO : null;
                    if (held != null && held.name.StartsWith(FixturePrefix, StringComparison.Ordinal)) rig.SetJuiceProfile308(null);
                }
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && g.name.StartsWith(FixturePrefix, StringComparison.Ordinal) && g.transform.parent == null).ToArray()) { Object.DestroyImmediate(g); n++; }
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && g.name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToArray()) { Object.DestroyImmediate(g); n++; }
            foreach (var t in Resources.FindObjectsOfTypeAll<RenderTexture>().Where(t => t != null && t.name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToArray()) { t.Release(); Object.DestroyImmediate(t); n++; }
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>().Where(t => t != null && t.name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToArray()) { Object.DestroyImmediate(t); n++; }
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>().Where(m => m != null && m.name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToArray()) { Object.DestroyImmediate(m); n++; }
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>().Where(m => m != null && m.name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToArray()) { Object.DestroyImmediate(m); n++; }
            if (!Application.isPlaying)
                foreach (var p in Resources.FindObjectsOfTypeAll<PlayerJuice308ProfileSO>().Where(p => p != null && p.name.StartsWith(FixturePrefix, StringComparison.Ordinal)).ToArray()) { Object.DestroyImmediate(p); n++; }
            return "fixtures:cleanup removed " + n + " object(s) named " + FixturePrefix + "*";
        }
    }
}
