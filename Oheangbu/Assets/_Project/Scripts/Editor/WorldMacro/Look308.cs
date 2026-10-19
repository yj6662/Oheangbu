using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 look "PBZ-Ink" (Tools/Unity/Stage308_look/PLAN_look308.md, D308-78, TEST). Queue:
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Look308 Run "<command>"
    //   status                         read only: scene, quality level, driver, profile, shader patch, sun / ambient, globals
    //   apply:<profile>                adds the scene object "Look308" (LookDriver308) with the profile asset
    //                                  (Assets/_Project/Art/World/Look308/Data/<profile>.asset or a full asset path) and the
    //                                  WorldLookDriver's regional sky profile + geography; saves the scene. Nothing else is written.
    //   off                            removes that object and saves the scene (= the picture before this stage; assets stay)
    //   stills:<label>[:off][:views=a,b][:w=1280][:debug=<n>]
    //                                  same cameras every time (Tools/Unity/Stage308_look/views308.json), ground height by a
    //                                  downward ray. The driver previews sun / ambient / post in memory for the shots and is put
    //                                  back afterwards; `off` renders with the driver disabled (the "before" of a pair);
    //                                  debug=<n> sets the InkWash297 debug view (1 lines, 9 pool/band/dry). Nothing is saved.
    //                                  -> Oheangbu/Screenshots/Look308/<label>/<view>.png  (width <= 1280)
    //   check:<label>[:<beforeLabel>]  numbers from the stills (PLAN AC): deep share, bright share, mean saturation, realm
    //                                  separation dE, isolated dark specks (against the before set when given)
    // Queue-safe: never opens a dialog; refusals and failures are returned as text.
    public static class Look308
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string MainScene = "W_Demo_Main", DriverObject = "Look308", AssetDir = "Assets/_Project/Art/World/Look308/Data";
        const string InkShaderPath = "Assets/_Project/Art/World/Finish297/Shaders/InkWash297.shader", FogShaderPath = "Assets/_Project/Art/World/Finish297/Shaders/RealmFog297.shader";
        static readonly int DebugId = Shader.PropertyToID("_OhInkWashDebug308");
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Stage => Path.Combine(Repo, "Tools", "Unity", "Stage308_look");
        static string Shots => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "Look308"));

        [Serializable] sealed class View { public string name = "", note = ""; public float[] eye, target; public float up = 8f, tup = 2f, fov = 60f, eyeY = -99999f, targetY = -99999f; }
        [Serializable] sealed class ViewFile { public string note = ""; public View[] views = Array.Empty<View>(); }

        static string F(float v) => v.ToString("0.###", Inv);

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return Status();
                if (c.StartsWith("check:", StringComparison.Ordinal)) return Check(c.Substring(6));
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED the editor is compiling or importing";
                if (c.StartsWith("apply:", StringComparison.Ordinal)) return Apply(c.Substring(6).Trim());
                if (c == "off") return Off();
                if (c.StartsWith("realm:", StringComparison.Ordinal)) return Realm(c.Substring(6).Trim());
                if (c.StartsWith("stills:", StringComparison.Ordinal)) return Stills(c.Substring(7));
                return "REFUSED unknown command '" + c + "' (status | apply:<profile> | off | realm:<Id>|none | stills:<label>[:off][:views=a,b][:w=1280][:debug=n] | check:<label>[:<before>])";
            }
            catch (Exception e) { return "FAILED " + e.GetType().Name + ": " + e.Message; }
        }

        // ---------------------------------------------------------------- shared

        static string MainRefusal(bool needClean)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != MainScene) return "REFUSED the active scene is '" + scene.name + "', need " + MainScene;
            if (needClean && scene.isDirty) return "REFUSED " + MainScene + " has unsaved changes (another tool is working on it?) — nothing was written";
            return null;
        }
        static LookDriver308 Driver(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LookDriver308>(true)).FirstOrDefault();
        static bool Patched(string path) => File.Exists(Path.Combine(Repo, "Oheangbu", path)) && File.ReadAllText(Path.Combine(Repo, "Oheangbu", path)).Contains("_OhLook308");
        static LookProfile308 LoadProfile(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string path = name.StartsWith("Assets/", StringComparison.Ordinal) ? name : AssetDir + "/" + name + (name.EndsWith(".asset", StringComparison.Ordinal) ? "" : ".asset");
            return AssetDatabase.LoadAssetAtPath<LookProfile308>(path);
        }

        // ---------------------------------------------------------------- status (read only)

        static string Status()
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            sb.AppendLine("Look308 status: active scene " + scene.path + (scene.isDirty ? " (dirty)" : "") + ", open scenes " + SceneManager.sceneCount
                + ", quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + ", play " + EditorApplication.isPlaying);
            sb.AppendLine("shader patch: InkWash297 " + (Patched(InkShaderPath) ? "v5 (look)" : "NOT patched") + ", RealmFog297 " + (Patched(FogShaderPath) ? "look" : "NOT patched"));
            sb.AppendLine("global _OhLook308 " + F(Shader.GetGlobalFloat("_OhLook308")) + ", _OhWorldPost " + F(Shader.GetGlobalFloat("_OhWorldPost")));
            var profiles = AssetDatabase.FindAssets("t:LookProfile308").Select(AssetDatabase.GUIDToAssetPath).ToArray();
            sb.AppendLine("profiles: " + (profiles.Length == 0 ? "none" : string.Join(", ", profiles)));
            foreach (string path in profiles)
            {
                var p = AssetDatabase.LoadAssetAtPath<LookProfile308>(path);
                if (p == null) continue;
                sb.AppendLine("  " + p.name + ": strength " + F(p.Strength) + ", usable " + p.Usable + ", realms " + (p.Realms?.Length ?? 0) + " [" + string.Join(",", (p.Realms ?? Array.Empty<LookProfile308.RealmEntry>()).Select(r => r.Id.ToString())) + "]"
                    + ", zones " + (p.Zones?.Length ?? 0) + ", post override " + p.Post.Override);
            }
            var driver = Driver(scene);
            if (driver == null) sb.AppendLine("driver: none in the active scene (look off)");
            else
            {
                var so = new SerializedObject(driver);
                sb.AppendLine("driver: " + driver.name + " enabled " + driver.isActiveAndEnabled + ", profile " + AssetDatabase.GetAssetPath(so.FindProperty("_profile").objectReferenceValue)
                    + ", regional " + AssetDatabase.GetAssetPath(so.FindProperty("_regional").objectReferenceValue) + ", geography " + AssetDatabase.GetAssetPath(so.FindProperty("_geography").objectReferenceValue)
                    + ", live " + driver.Live + ", scene state applied " + driver.SceneStateApplied);
                sb.AppendLine("  realm weights " + string.Join(" ", Enum.GetValues(typeof(RealmId)).Cast<RealmId>().Select(r => r + "=" + F(driver.RealmWeight(r)))));
                var l = driver.Current;
                sb.AppendLine("  current: exposure " + F(l.Exposure) + " contrast " + F(l.Contrast) + " saturation " + F(l.Saturation) + " ambient x" + F(l.AmbientValue) + " sun x" + F(l.SunIntensity)
                    + " ink tone x" + F(l.InkTone) + " pool " + F(l.PoolAmount) + " band " + F(l.BandAmount) + " fog x" + F(l.FogDensity) + " motes " + F(l.MoteDensity) + " wet " + F(l.Wetness));
            }
            var sun = RenderSettings.sun;
            sb.AppendLine("sun: " + (sun == null ? "none" : sun.name + " euler " + sun.transform.eulerAngles.ToString("0.#") + " intensity " + F(sun.intensity) + " shadow " + F(sun.shadowStrength) + " colour " + sun.color));
            sb.AppendLine("ambient: mode " + RenderSettings.ambientMode + " sky " + RenderSettings.ambientSkyColor + " equator " + RenderSettings.ambientEquatorColor + " ground " + RenderSettings.ambientGroundColor);
            sb.AppendLine("views: " + (File.Exists(Path.Combine(Stage, "views308.json")) ? ReadViews().Length + " in views308.json" : "views308.json missing"));
            return sb.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- apply / off (scene object only)

        static string Apply(string name)
        {
            string refusal = MainRefusal(true);
            if (refusal != null) return refusal;
            var profile = LoadProfile(name);
            if (profile == null) return "REFUSED no LookProfile308 '" + name + "' (looked in " + AssetDir + "; run deploy.py apply and refresh first)";
            if (!profile.Usable) return "REFUSED profile " + profile.name + " is not usable (strength " + F(profile.Strength) + ", realms " + (profile.Realms?.Length ?? 0) + ")";
            if (!Patched(InkShaderPath) || !Patched(FogShaderPath)) return "REFUSED the screen passes are not patched (InkWash297 / RealmFog297 carry no _OhLook308): run deploy.py apply first";
            var scene = SceneManager.GetActiveScene();
            var looks = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldLookDriver>(true)).ToArray();
            if (looks.Length != 1) return "REFUSED expected one WorldLookDriver in " + scene.name + ", found " + looks.Length;
            var ws = new SerializedObject(looks[0]);
            var regional = ws.FindProperty("_regionalSkyProfile").objectReferenceValue as RegionalInkSkyProfile;
            var geography = ws.FindProperty("_skyGeography").objectReferenceValue as WorldMacroSheetSO;
            if (regional == null || geography == null || geography.Regions == null || geography.Regions.Length == 0)
                return "REFUSED the WorldLookDriver has no regional sky profile / geography (realm weights would be missing)";
            var driver = Driver(scene);
            bool created = driver == null;
            if (created)
            {
                var go = new GameObject(DriverObject);
                SceneManager.MoveGameObjectToScene(go, scene);
                driver = go.AddComponent<LookDriver308>();
            }
            var so = new SerializedObject(driver);
            so.FindProperty("_profile").objectReferenceValue = profile;
            so.FindProperty("_regional").objectReferenceValue = regional;
            so.FindProperty("_geography").objectReferenceValue = geography;
            so.FindProperty("_sceneStateInEditMode").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(driver);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) return "FAILED the scene could not be saved (driver " + (created ? "created" : "updated") + " in memory)";
            return "applied: " + (created ? "created" : "updated") + " '" + driver.name + "' with " + AssetDatabase.GetAssetPath(profile) + " (regional " + regional.name + ", geography " + geography.name
                + "). Edit mode shows shader globals only; sun / ambient / post follow in Play and in `stills`. Revert: Look308 off";
        }

        static string Off()
        {
            string refusal = MainRefusal(true);
            if (refusal != null) return refusal;
            var scene = SceneManager.GetActiveScene();
            var driver = Driver(scene);
            if (driver == null) return "off: no LookDriver308 in " + scene.name + " (already off)";
            var go = driver.gameObject;
            bool own = go.name == DriverObject && go.transform.parent == null && go.GetComponents<Component>().Length == 2 && go.transform.childCount == 0;
            if (own) Object.DestroyImmediate(go); else Object.DestroyImmediate(driver);
            Shader.SetGlobalFloat("_OhLook308", 0f);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) return "FAILED the scene could not be saved (driver removed in memory)";
            return "off: removed the look driver" + (own ? " object" : " component") + "; the profile asset and the shader patch stay (inert without a driver)";
        }

        // ---------------------------------------------------------------- stills

        static View[] ReadViews()
        {
            var file = JsonUtility.FromJson<ViewFile>(File.ReadAllText(Path.Combine(Stage, "views308.json")));
            if (file?.views == null || file.views.Length == 0) throw new Exception("views308.json has no views");
            foreach (var v in file.views) if (v.eye == null || v.eye.Length != 2 || v.target == null || v.target.Length != 2) throw new Exception("view " + v.name + ": eye / target need [x, z]");
            return file.views;
        }

        static bool Ground(float x, float z, out float y)
        {
            y = 0f;
            Physics.SyncTransforms();
            if (!Physics.Raycast(new Vector3(x, 2000f, z), Vector3.down, out var hit, 4000f, ~0, QueryTriggerInteraction.Ignore)) return false;
            y = hit.point.y; return true;
        }

        static string Stills(string arg)
        {
            string refusal = MainRefusal(false);
            if (refusal != null) return refusal;
            var parts = arg.Split(':');
            string label = parts[0].Trim();
            if (label.Length == 0 || label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "REFUSED stills needs a label usable as a folder name";
            bool off = parts.Skip(1).Any(p => p == "off");
            int width = 1280; float debug = 0f; HashSet<string> only = null;
            foreach (var p in parts.Skip(1))
            {
                if (p.StartsWith("w=", StringComparison.Ordinal)) width = Mathf.Clamp(int.Parse(p.Substring(2), Inv), 320, 1280);
                else if (p.StartsWith("debug=", StringComparison.Ordinal)) debug = float.Parse(p.Substring(6), Inv);
                else if (p.StartsWith("views=", StringComparison.Ordinal)) only = new HashSet<string>(p.Substring(6).Split(',').Select(s => s.Trim()));
            }
            int height = Mathf.RoundToInt(width * 9f / 16f);
            var scene = SceneManager.GetActiveScene();
            var driver = Driver(scene);
            if (driver == null && !off) return "REFUSED no LookDriver308 in " + scene.name + " (Look308 apply:<profile> first, or stills:<label>:off for the before set)";
            var views = ReadViews().Where(v => only == null || only.Contains(v.name)).ToArray();
            if (views.Length == 0) return "REFUSED no view matches";
            string folder = Path.Combine(Shots, label);
            Directory.CreateDirectory(folder);
            bool wasEnabled = driver != null && driver.enabled;
            // the regional sky / ambient follow the player camera, not the still camera: preview them at every eye (what
            // RegionSky308Capture does), otherwise every realm is shot under the sky of wherever the player stands
            var world = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldLookDriver>(false)).FirstOrDefault(d => d.isActiveAndEnabled);
            var sb = new StringBuilder("Look308 stills " + label + (off ? " (driver off)" : "") + " " + width + "x" + height + " quality " + QualitySettings.names[QualitySettings.GetQualityLevel()]
                + " open scenes " + SceneManager.sceneCount + (debug > 0 ? " debug " + F(debug) : "") + "\n");
            int done = 0;
            try
            {
                if (driver != null)
                {
                    if (off) driver.enabled = false; else { driver.enabled = true; driver.SetEditPreview(true); }
                }
                if (debug > 0) Shader.SetGlobalFloat(DebugId, debug);
                foreach (var v in views)
                {
                    // eyeY / targetY (absolute world heights, the city_views.json convention): interiors, where a ray from above lands on the roof
                    bool absolute = v.eyeY > -9000f && v.targetY > -9000f;
                    float ey = 0f, ty = 0f;
                    if (absolute) { ey = v.eyeY - v.up; ty = v.targetY - v.tup; }
                    else if (!Ground(v.eye[0], v.eye[1], out ey) || !Ground(v.target[0], v.target[1], out ty))
                    {
                        sb.AppendLine("  " + v.name + ": SKIPPED no ground under the eye or target (realm scene not loaded?)");
                        continue;
                    }
                    if (world != null) world.PreviewRegionalSky(new Vector3(v.eye[0], ey + v.up, v.eye[1]));
                    string shot = "look308_" + label + "_" + v.name;
                    string result = Presentation297.Run("shot:" + shot + ":" + F(v.eye[0]) + "," + F(ey + v.up) + "," + F(v.eye[1]) + ":" + F(v.target[0]) + "," + F(ty + v.tup) + "," + F(v.target[1])
                        + ":fov=" + F(v.fov <= 0 ? 60f : v.fov) + ":w=" + width + ":h=" + height + ":hideplayer");
                    if (result != null && File.Exists(result))
                    {
                        File.Copy(result, Path.Combine(folder, v.name + ".png"), true);
                        done++; sb.AppendLine("  " + v.name + ": " + (driver != null && !off ? RealmLine(driver) : "ok"));
                    }
                    else sb.AppendLine("  " + v.name + ": FAILED " + result);
                }
            }
            finally
            {
                if (debug > 0) Shader.SetGlobalFloat(DebugId, 0f);
                if (driver != null)
                {
                    driver.SetEditPreview(false);
                    driver.enabled = wasEnabled;
                }
                if (world != null) world.Apply();   // back to the driver's own edit-mode sky
            }
            sb.Append("stills: " + done + "/" + views.Length + " -> " + folder + " (nothing saved; shoot twice after an editor restart and judge the second set)");
            return sb.ToString();
        }

        // realm:<Id>|none — one realm scene next to the play scene (VRAM: stills with all five open crashed the editor on 2026-10-12).
        // Closes the other realm scenes (never a dirty one), opens the asked one additively, keeps the play scene active and shows the
        // far band for the realms that are not open (what RealmStream308 open-all / close-all do). Nothing is saved.
        static string Realm(string id)
        {
            string refusal = MainRefusal(false);
            if (refusal != null) return refusal;
            const string folder = "Assets/_Project/Scenes/World/Stream";
            var main = SceneManager.GetActiveScene();
            string want = id == "none" || id.Length == 0 ? null : folder + "/Realm_" + id + ".unity";
            if (want != null && !File.Exists(Path.Combine(Repo, "Oheangbu", want))) return "REFUSED no scene " + want;
            int closed = 0; var kept = new List<string>();
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s == main || s.path == want || !s.path.StartsWith(folder + "/Realm_", StringComparison.Ordinal)) continue;
                if (s.isDirty) { kept.Add(s.path); continue; }
                EditorSceneManager.CloseScene(s, true); closed++;
            }
            bool opened = false;
            if (want != null && !SceneManager.GetSceneByPath(want).isLoaded) { EditorSceneManager.OpenScene(want, OpenSceneMode.Additive); opened = true; }
            SceneManager.SetActiveScene(main);
            var far = main.GetRootGameObjects().FirstOrDefault(g => g.name == "RealmFar308"); int on = 0;
            if (far != null)
                foreach (Transform t in far.transform)
                {
                    bool open = SceneManager.GetSceneByPath(folder + "/Realm_" + t.name + ".unity").isLoaded;
                    t.gameObject.SetActive(!open); if (!open) on++;   // SetActive alone does not mark the scene dirty
                }
            if (closed > 0) { EditorUtility.UnloadUnusedAssetsImmediate(true); GC.Collect(); }
            return "realm " + (want ?? "none") + ": opened " + (opened ? 1 : 0) + ", closed " + closed + (kept.Count > 0 ? ", KEPT (dirty) " + string.Join(", ", kept) : "")
                + "; open scenes " + SceneManager.sceneCount + (far != null ? "; far band on for " + on + " of " + far.transform.childCount : "; no RealmFar308 root") + (main.isDirty ? "; play scene DIRTY" : "");
        }

        static string RealmLine(LookDriver308 d) => string.Join(" ", Enum.GetValues(typeof(RealmId)).Cast<RealmId>().Where(r => d.RealmWeight(r) > .01f).Select(r => r + "=" + F(d.RealmWeight(r))));

        // ---------------------------------------------------------------- check (numbers from the stills)

        struct Stat { public float Deep, Bright, Saturation, SpecksPerMp; public Vector3 Lab; }

        static Stat Measure(string file)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(file))) throw new Exception("cannot read " + file);
                var px = tex.GetPixels32(); int w = tex.width, h = tex.height, n = px.Length;
                var luma = new float[n];
                double deep = 0, bright = 0, sat = 0, lr = 0, lg = 0, lb = 0;
                for (int i = 0; i < n; i++)
                {
                    float r = px[i].r / 255f, g = px[i].g / 255f, b = px[i].b / 255f;
                    float y = .2126f * r + .7152f * g + .0722f * b; luma[i] = y;
                    if (y < .15f) deep++;
                    if (y > .5f) bright++;
                    float mx = Mathf.Max(r, Mathf.Max(g, b)), mn = Mathf.Min(r, Mathf.Min(g, b));
                    sat += mx > 1e-4f ? (mx - mn) / mx : 0f;
                    lr += Mathf.GammaToLinearSpace(r); lg += Mathf.GammaToLinearSpace(g); lb += Mathf.GammaToLinearSpace(b);
                }
                // isolated dark specks: a pixel much darker than every one of its 8 neighbours' mean (the #307 black-speck signature)
                int specks = 0;
                for (int y = 1; y < h - 1; y++)
                    for (int x = 1; x < w - 1; x++)
                    {
                        int i = y * w + x;
                        if (luma[i] > .12f) continue;
                        float around = (luma[i - 1] + luma[i + 1] + luma[i - w] + luma[i + w] + luma[i - w - 1] + luma[i - w + 1] + luma[i + w - 1] + luma[i + w + 1]) / 8f;
                        if (around > luma[i] + .22f) specks++;
                    }
                return new Stat { Deep = (float)(deep / n), Bright = (float)(bright / n), Saturation = (float)(sat / n), SpecksPerMp = specks / (n / 1e6f),
                    Lab = Lab(new Vector3((float)(lr / n), (float)(lg / n), (float)(lb / n))) };
            }
            finally { Object.DestroyImmediate(tex); }
        }

        // linear sRGB (D65) -> CIE L*a*b*
        static Vector3 Lab(Vector3 c)
        {
            float X = (.4124f * c.x + .3576f * c.y + .1805f * c.z) / .95047f, Y = .2126f * c.x + .7152f * c.y + .0722f * c.z, Z = (.0193f * c.x + .1192f * c.y + .9505f * c.z) / 1.08883f;
            float Fn(float t) => t > .008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
            float fx = Fn(X), fy = Fn(Y), fz = Fn(Z);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        static string Check(string arg)
        {
            var parts = arg.Split(':');
            string label = parts[0].Trim(), before = parts.Length > 1 ? parts[1].Trim() : "";
            string folder = Path.Combine(Shots, label);
            if (!Directory.Exists(folder)) return "REFUSED no stills folder " + folder;
            var files = Directory.GetFiles(folder, "*.png").OrderBy(f => f).ToArray();
            if (files.Length == 0) return "REFUSED no png in " + folder;
            var sb = new StringBuilder("Look308 check " + label + (before.Length > 0 ? " against " + before : "") + " (" + files.Length + " stills; sRGB luma: deep < .15, bright > .5)\n");
            var stats = new Dictionary<string, Stat>();
            foreach (string f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                var s = Measure(f); stats[name] = s;
                string line = "  " + name + ": deep " + F(s.Deep * 100) + "% bright " + F(s.Bright * 100) + "% sat " + F(s.Saturation) + " Lab " + F(s.Lab.x) + "," + F(s.Lab.y) + "," + F(s.Lab.z) + " specks/MP " + F(s.SpecksPerMp);
                string bf = before.Length > 0 ? Path.Combine(Shots, before, name + ".png") : null;
                if (bf != null && File.Exists(bf))
                {
                    var b = Measure(bf);
                    line += "  | before: deep " + F(b.Deep * 100) + "% sat " + F(b.Saturation) + " specks/MP " + F(b.SpecksPerMp);
                    // AC-L6: no black-speck regression
                    line += s.SpecksPerMp <= b.SpecksPerMp * 1.5f + 60f ? "  speck PASS" : "  speck FAIL";
                }
                sb.AppendLine(line);
            }
            // AC-L1 / AC-L2 on the open views, AC-L3 realm separation between them
            var open = stats.Where(k => k.Key.EndsWith("_open", StringComparison.Ordinal)).OrderBy(k => k.Key).ToArray();
            if (open.Length > 0)
            {
                float deep = open.Average(k => k.Value.Deep), sat = open.Average(k => k.Value.Saturation);
                sb.AppendLine("AC-L1 deep share (open views mean) " + F(deep * 100) + "% " + (deep >= .30f && deep <= .65f ? "PASS" : "FAIL") + " [target 30-65 %]");
                sb.AppendLine("AC-L2 mean saturation (open views) " + F(sat) + " " + (sat >= .10f && sat <= .30f ? "PASS" : "FAIL") + " [target .10-.30]");
                float min = float.MaxValue; string pair = "";
                for (int i = 0; i < open.Length; i++)
                    for (int j = i + 1; j < open.Length; j++)
                    {
                        // chroma-plane distance (a*, b*): the realms must differ in hue, not only in value
                        var a = open[i].Value.Lab; var b = open[j].Value.Lab;
                        float de = Mathf.Sqrt((a.y - b.y) * (a.y - b.y) + (a.z - b.z) * (a.z - b.z));
                        if (de < min) { min = de; pair = open[i].Key + " / " + open[j].Key; }
                    }
                if (open.Length > 1) sb.AppendLine("AC-L3 realm separation: smallest a*b* distance " + F(min) + " (" + pair + ") " + (min >= 4f ? "PASS" : "FAIL") + " [target >= 4]");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
