using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 quick A/B sweep inside the held Play of Map307Capture (play-hold): per station pose, variant A then every variant then A
    // again, each settled 1.5 s and sampled 3 s (FrameTimingManager GPU / CPU main medians + frame interval). Variants are applied in
    // memory and restored after each sample (never saved). Reference only: Game view resolution, Editor Play.
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.PerfSweep307 Run "sweep:<name>:<variant>|<variant>..."
    //   variant = key=value[,key=value]: far=<m> cascades=<1-4> shadow=<m> feature=<RendererFeature>/<0|1> lanternshadows=0
    //   lodbias=<f> artpath=<0|1> ; status | abort. Output Art/Performance/Perf307/Sweep/<name>.csv + .txt
    public static class PerfSweep307
    {
        const string Renderer297 = "Assets/_Project/Art/World/Finish297/Renderer297.asset";
        internal static readonly (string id, Vector3 feet, float[] yaws)[] Stations = {
            ("mine_start", new Vector3(3261.00f, 168.28f, 1878.00f), new[] { 0f, 180f }),
            ("mine_yard", new Vector3(3368.60f, 165.81f, 2033.90f), new[] { 315f, 210f }),
            ("village_rest", new Vector3(2758.44f, 216.88f, 2133.89f), new[] { 0f, 180f }),
            ("forest_band", new Vector3(3613.04f, 173.38f, 2050.25f), new[] { 86.2f, 266.2f }),
            ("cheolong_fortress", new Vector3(723.36f, 253.72f, 3587.95f), new[] { 211.7f, 31.7f }),
            ("capital_gate", new Vector3(2000.00f, 98.02f, 2495.00f), new[] { 0f, 180f }) };
        static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Performance/Perf307/Sweep"));

        sealed class Sample { public string Station, Variant; public float Yaw, Gpu, Cpu, Interval, Collect, Classify, Tests, Replay, Tris, Batches, Casters; public int N; }
        static string status = "idle", name = "", note = "";
        static List<string> variants = new List<string>();
        static readonly List<Sample> samples = new List<Sample>();
        static readonly List<string> census = new List<string>();   // first A sample per pose: CompactRebuildArtRenderer.Census307 rows
        static int station, pose, variant, stage; static double at; static bool hooked;
        static readonly List<float> gpu = new List<float>(), cpu = new List<float>(), interval = new List<float>(), collect = new List<float>(), classify = new List<float>(), tests = new List<float>(), replay = new List<float>(), tris = new List<float>(), batches = new List<float>(), casters = new List<float>();
        static ProfilerRecorder classifyRecorder, testsRecorder, replayRecorder, trisRecorder, batchesRecorder, castersRecorder;   // render counters: proof that a culling variant culled
        static float spinRate; static ProfilerRecorder collectRecorder;   // spin=<deg/s>: the view turns during the sample (forces re-collects)
        static Action restore; static Action held;
        static readonly FrameTiming[] timing = new FrameTiming[1];
        static double Now => EditorApplication.timeSinceStartup;

        public static string Run(string arg)
        {
            arg = (arg ?? "").Trim();
            if (arg == "status") return status + " | " + note + " | samples " + samples.Count;
            if (arg == "abort") { Stop("aborted"); return "aborted"; }
            // apply:<variant> keeps the variant on (for look captures) until revert; one at a time, never saved
            if (arg.StartsWith("apply:", StringComparison.Ordinal))
            {
                if (status == "running") return "refused: a sweep is running";
                var hs = Map307Capture.HeldSession; if (hs == null) return "refused: no held Play";
                string v = arg.Substring(6); string why = Check(v); if (why != null) return "refused: " + why;
                try { held?.Invoke(); } catch { }
                held = Apply(v, hs); return "applied " + v + " (revert to undo)";
            }
            // band-state: every loaded dressing sheet's in-memory 2c band (the code default is 0; a domain reload keeps the old value)
            if (arg == "band-state" || arg == "band-reset")
            {
                var sheets = Resources.FindObjectsOfTypeAll<WorldMacroDressingSheetSO>();
                if (arg == "band-reset") foreach (var sh in sheets) sh.CollectBandMetres307 = 0f;   // not saved, not dirtied
                return string.Join("; ", sheets.Select(sh => sh.name + " band " + sh.CollectBandMetres307 + " dirty " + EditorUtility.IsDirty(sh)));
            }
            // lookpair:<name>:<variant>[:pitch=-4] — deterministic look captures for approval: a probe copy of the view camera at every
            // station eye (feet + 1.7 m) and yaw renders A, the variant is applied, B is rendered, the variant is reverted.
            // Fixed contact/wind clocks, dithering off (as artpixel). PNGs + sheet: Art/Performance/Perf307/Look/<name>/.
            // pick:<station>:<yaw>:<pitch>:x,y;x,y... — #307 floating-object hunt: the lookpair probe camera (station feet + 1.7 m, 1600x900)
            // casts a ray through each pixel; every Renderer whose bounds the ray enters is listed nearest first (Edit or Play)
            // pickart:<station>:<yaw>:<pitch>:x,y;x,y...[:<variant>] — #307: one probe render (lookpair pose, held Play) with every art renderer
            // logging its chunks; per pixel ray, the chunks whose box the ray enters, nearest first, with mode
            if (arg.StartsWith("pickart:", StringComparison.Ordinal))
            {
                var hs = Map307Capture.HeldSession; if (hs == null) return "refused: no held Play";
                var pp = arg.Split(':'); if (pp.Length < 5) return "refused: pickart:<station>:<yaw>:<pitch>:x,y;x,y[:variant]";
                var st = Stations.FirstOrDefault(x => x.id == pp[1]); if (st.id == null) return "refused: unknown station " + pp[1];
                float yaw = float.Parse(pp[2], CultureInfo.InvariantCulture), pitch = float.Parse(pp[3], CultureInfo.InvariantCulture);
                var arts = Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).Where(a => a.isActiveAndEnabled && a.Sheet != null).ToList();
                var camera = ArtCull307.CloneObserver("PickArt307_camera", arts, out var go);
                const int w = 1600, h = 900; var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                var observers = arts.Select(a => a.Observer).ToArray(); var undo = pp.Length > 5 && pp[5].Length > 0 ? Apply(pp[5], hs) : null;
                float pickMin = pp.Length > 6 ? float.Parse(pp[6], CultureInfo.InvariantCulture) : 5f;   // pickart:...:<variant>:<min m>
                var sbp = new StringBuilder();
                try
                {
                    camera.targetTexture = rt; camera.aspect = w / (float)h; camera.transform.SetPositionAndRotation(st.feet + Vector3.up * 1.7f, Quaternion.Euler(pitch, yaw, 0f));
                    foreach (var a in arts) { a.Observer = camera; a.EmitLog307 = new List<CompactRebuildArtRenderer.EmitRecord307>(); }
                    camera.Render();
                    sbp.Append("occlusion ").Append(camera.useOcclusionCulling).Append(" | logged ").Append(string.Join(", ", arts.Select(a => a.name + " " + a.EmitLog307.Count))).Append('\n');
                    foreach (var px in pp[4].Split(';'))
                    {
                        var xy = px.Split(','); var ray = camera.ViewportPointToRay(new Vector3(float.Parse(xy[0], CultureInfo.InvariantCulture) / w, 1f - float.Parse(xy[1], CultureInfo.InvariantCulture) / h, 0f));
                        var hits = new List<(float d, string line)>();
                        foreach (var a in arts) foreach (var e in a.EmitLog307) if (e.Tight.IntersectRay(ray, out float d))
                            hits.Add((d, a.name + " " + e.Mode + " n" + e.Count + " " + e.Part + " tight " + e.Tight.center.ToString("F0") + " size " + e.Tight.size.ToString("F0")));
                        sbp.Append("pixel ").Append(px).Append(": ").Append(hits.Count).Append(" chunks\n");
                        foreach (var hit in hits.Where(x => x.d > pickMin).OrderBy(x => x.d).Take(30)) sbp.Append("   ").Append(hit.d.ToString("F0")).Append(" m  ").Append(hit.line).Append('\n');
                    }
                }
                finally
                {
                    try { undo?.Invoke(); } catch { }
                    for (int i = 0; i < arts.Count; i++) if (arts[i] != null) { arts[i].Observer = observers[i]; arts[i].EmitLog307 = null; arts[i].ForgetCollect307(); }
                    camera.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
                }
                return sbp.ToString();
            }
            if (arg.StartsWith("pick:", StringComparison.Ordinal))
            {
                var pp = arg.Split(':'); if (pp.Length < 5) return "refused: pick:<station>:<yaw>:<pitch>:x,y;x,y";
                var st = Stations.FirstOrDefault(x => x.id == pp[1]); if (st.id == null) return "refused: unknown station " + pp[1];
                float yaw = float.Parse(pp[2], CultureInfo.InvariantCulture), pitch = float.Parse(pp[3], CultureInfo.InvariantCulture);
                var arts = Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).ToList();
                var camera = ArtCull307.CloneObserver("Pick307_camera", arts, out var go);
                var sbp = new StringBuilder();
                try
                {
                    camera.aspect = 1600f / 900f; camera.transform.SetPositionAndRotation(st.feet + Vector3.up * 1.7f, Quaternion.Euler(pitch, yaw, 0f));
                    var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                    foreach (var px in pp[4].Split(';'))
                    {
                        var xy = px.Split(','); float x = float.Parse(xy[0], CultureInfo.InvariantCulture), y = float.Parse(xy[1], CultureInfo.InvariantCulture);
                        var ray = camera.ViewportPointToRay(new Vector3(x / 1600f, 1f - y / 900f, 0f));
                        var hits = new List<(float d, Renderer r)>();
                        bool all = pp.Length > 5 && pp[5] == "all";   // pick:...:all keeps terrain-sized boxes too
                        foreach (var r in renderers) if ((all || r.bounds.size.magnitude < 150f) && r.bounds.IntersectRay(ray, out float d)) hits.Add((d, r));   // terrain-sized boxes skipped
                        sbp.Append("pixel ").Append(px).Append(": ").Append(hits.Count).Append(" bounds on the ray\n");
                        foreach (var h in hits.OrderBy(h => h.d).Take(12))
                        {
                            var t = h.r.transform; string path = t.name; for (var q = t.parent; q != null; q = q.parent) path = q.name + "/" + path;
                            var m = h.r.sharedMaterial;
                            sbp.Append("   ").Append(h.d.ToString("F0")).Append(" m  ").Append(path).Append(" [").Append(h.r.GetType().Name).Append("] mat ").Append(m != null ? m.name + " / " + (m.shader != null ? m.shader.name : "?") + " q" + m.renderQueue : "-")
                               .Append(" centre ").Append(h.r.bounds.center.ToString("F0")).Append(" size ").Append(h.r.bounds.size.ToString("F1")).Append(" scene ").Append(h.r.gameObject.scene.name).Append(" static ").Append((int)GameObjectUtility.GetStaticEditorFlags(h.r.gameObject)).Append("\n");
                        }
                    }
                }
                finally { Object.DestroyImmediate(go); }
                return sbp.ToString();
            }
            if (arg.StartsWith("lookpair:", StringComparison.Ordinal))
            {
                var hs = Map307Capture.HeldSession; if (hs == null) return "refused: no held Play";
                var parts = arg.Split(':'); if (parts.Length < 3) return "refused: lookpair:<name>:<variant>";
                string why = Check(parts[2]); if (why != null) return "refused: " + why;
                float pitch = -4f; int yawCount = 0; string baseVariant = "";   // yaws=N: N evenly spaced headings per station instead of the two station yaws; base=<variant>: applied to A and B
                foreach (var p in parts.Skip(3)) { var kv = p.Split('='); if (kv.Length == 2 && kv[0] == "pitch") float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out pitch); if (kv.Length == 2 && kv[0] == "yaws") int.TryParse(kv[1], out yawCount); if (p.StartsWith("base=", StringComparison.Ordinal)) baseVariant = p.Substring(5); }
                if (baseVariant.Length > 0) { string whyBase = Check(baseVariant); if (whyBase != null) return "refused: base " + whyBase; }
                return LookPair(hs, parts[1], parts[2], pitch, yawCount, baseVariant);
            }
            // sheets-apply[:dry] — user approval 2026-10-01 ("1, 2, 3 다 적용해"): every dressing sheet the loaded scenes' art renderers use gets
            // TreeMiddle 180 (LOD2 cards from 180 m), Meshy pine LOD0 -> LOD1 at 55 m (LodOverrides307) and the pine shadow proxy
            // (ShadowProxies307, LOD1). The 2c band is forced to 0 before saving (a domain reload can keep an old in-memory value).
            // Edit mode; each sheet .asset is backed up to Tools/Unity/Stage307_backup_sheets/<utc>/ and saved alone (SaveAssetIfDirty).
            if (arg == "sheets-apply" || arg == "sheets-apply:dry")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Edit mode only";
                bool dry = arg.EndsWith(":dry");
                var sheets = Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(r => r.Sheet).Where(x => x != null).Distinct().ToList();
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
                string backup = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Tools/Unity/Stage307_backup_sheets/" + stamp));
                var log = new List<string> { (dry ? "DRY " : "APPLY ") + "sheets " + sheets.Count };
                foreach (var sh in sheets)
                {
                    string path = AssetDatabase.GetAssetPath(sh);
                    if (path.Contains("/Watershed295/") || path.Contains("/Reworld292/") || path.Contains("/MountainTrail285/")) { log.Add("SKIP protected " + path); continue; }
                    bool pines = sh.Prototypes != null && sh.Prototypes.Any(p => p != null && p.Id != null && p.Id.Contains("Pinus"));
                    log.Add(path + " | TreeNear " + sh.TreeNear + " TreeMiddle " + sh.TreeMiddle + " -> " + Mathf.Min(sh.TreeMiddle, 180f) + " | pines " + pines + " | band " + sh.CollectBandMetres307 + " -> 0");
                    if (dry) continue;
                    Directory.CreateDirectory(backup);
                    string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
                    File.Copy(full, Path.Combine(backup, Path.GetFileName(path)), true);
                    sh.TreeMiddle = Mathf.Min(sh.TreeMiddle, 180f); sh.CollectBandMetres307 = 0f;   // never farther than the sheet had
                    sh.LodOverrides307 = pines ? new[] { new WorldMacroDressingSheetSO.LodOverride307 { IdContains = "Pinus", TreeNear = 55f } } : new WorldMacroDressingSheetSO.LodOverride307[0];
                    sh.ShadowProxies307 = pines ? new[] { new WorldMacroDressingSheetSO.ShadowProxy307 { IdContains = "Pinus", ProxyLod = 1 } } : new WorldMacroDressingSheetSO.ShadowProxy307[0];
                    EditorUtility.SetDirty(sh); AssetDatabase.SaveAssetIfDirty(sh);
                }
                if (!dry) log.Add("backups " + backup);
                return string.Join("\n", log);
            }
            if (arg == "revert") { if (held == null) return "nothing applied"; try { held(); } finally { held = null; } return "reverted"; }
            if (!arg.StartsWith("sweep:", StringComparison.Ordinal)) return "refused: sweep:<name>:<v1>|<v2>... | status | abort";
            if (status == "running") return "refused: a sweep is running";
            if (Map307Capture.HeldSession == null) return "refused: no held Play (Map307Capture play-hold first)";
            var rest = arg.Substring(6); int c = rest.IndexOf(':'); if (c <= 0) return "refused: sweep:<name>:<variants>";
            name = rest.Substring(0, c); variants = new List<string> { "A" };
            variants.AddRange(rest.Substring(c + 1).Split('|').Select(v => v.Trim()).Where(v => v.Length > 0)); variants.Add("A");
            foreach (var v in variants.Where(v => v != "A")) { string why = Check(v); if (why != null) return "refused: " + why; }
            samples.Clear(); census.Clear(); station = pose = variant = stage = 0; at = Now; status = "running"; note = "start";
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            return "sweep " + name + " started: " + Stations.Length + " stations x 2 poses x " + variants.Count + " variants (~" + (Stations.Length * (4 + 2 * variants.Count * 4.6)).ToString("0") + " s)";
        }

        static string Check(string v)
        {
            foreach (var kv in v.Split(','))
            {
                var p = kv.Split('='); if (p.Length != 2) return "variant part '" + kv + "' is not key=value";
                if (!new[] { "far", "cascades", "shadow", "feature", "lanternshadows", "lodbias", "artpath", "fine", "treenear", "treemid", "pinenear", "pinemid", "sight", "band", "spin", "serial", "pineproxy", "off", "scale", "inkdepth", "inknormal", "inkline", "inkground", "vegdepth", "vegcluster", "prime", "softq", "bladepx", "bladetone", "bladestart", "bladeend", "bladegrey", "sort", "groundperf", "triskip", "canopy", "inkmode", "inkfloor", "occlusion", "wholebounds", "artoff", "artskip", "artreverse" }.Contains(p[0].Trim())) return "unknown key " + p[0];
            }
            return null;
        }

        static void Tick()
        {
            if (status != "running") return;
            var s = Map307Capture.HeldSession;
            if (s == null || !EditorApplication.isPlaying) { Stop("FAIL held Play ended"); return; }
            FrameTimingManager.CaptureFrameTimings();
            var st = Stations[station];
            switch (stage)
            {
                case 0:   // teleport to the station, settle 4 s
                    s.Teleport(st.feet, st.yaws[pose]); at = Now; stage = 1; note = st.id + " settle"; return;
                case 1:
                    if (Now - at < (pose == 0 && variant == 0 ? 4.0 : 0.5)) return;
                    s.Teleport(st.feet, st.yaws[pose]); restore = Apply(variants[variant], s); at = Now; stage = 2; note = st.id + "@" + st.yaws[pose] + " " + variants[variant]; return;
                case 2:   // settle 1.5 s after the variant
                    if (Now - at < 1.5) return;
                    gpu.Clear(); cpu.Clear(); interval.Clear(); collect.Clear(); classify.Clear(); tests.Clear(); replay.Clear(); tris.Clear(); batches.Clear(); casters.Clear(); at = Now; stage = 3;
                    if (!trisRecorder.Valid) trisRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
                    if (!batchesRecorder.Valid) batchesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
                    if (!castersRecorder.Valid) castersRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count", 1);
                    if (!classifyRecorder.Valid) classifyRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Oh.Art.Classify", 1);
                    if (!testsRecorder.Valid) testsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Oh.Art.Tests", 1);
                    if (!replayRecorder.Valid) replayRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Oh.Art.Replay", 1);
                    if (!collectRecorder.Valid) collectRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Oh.Art.Collect", 1);
                    return;
                case 3:
                    if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
                    {
                        if (timing[0].gpuFrameTime > 0) gpu.Add((float)timing[0].gpuFrameTime);
                        if (timing[0].cpuMainThreadFrameTime > 0) cpu.Add((float)timing[0].cpuMainThreadFrameTime);
                    }
                    interval.Add(Time.unscaledDeltaTime * 1000f);
                    if (collectRecorder.Valid) collect.Add(collectRecorder.LastValue / 1e6f);
                    if (classifyRecorder.Valid) classify.Add(classifyRecorder.LastValue / 1e6f); if (testsRecorder.Valid) tests.Add(testsRecorder.LastValue / 1e6f); if (replayRecorder.Valid) replay.Add(replayRecorder.LastValue / 1e6f);
                    if (trisRecorder.Valid) tris.Add(trisRecorder.LastValue); if (batchesRecorder.Valid) batches.Add(batchesRecorder.LastValue); if (castersRecorder.Valid) casters.Add(castersRecorder.LastValue);
                    if (spinRate > 0) s.Teleport(st.feet, st.yaws[pose] + (float)(Now - at) * spinRate);
                    if (Now - at < 3.0) return;
                    if (variant == 0) foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None))
                        foreach (var row in r.Census307().Split('\n')) if (row.Length > 0) census.Add(st.id + "@" + st.yaws[pose].ToString("0", CultureInfo.InvariantCulture) + "\t" + r.name + "\t" + row);
                    samples.Add(new Sample { Station = st.id, Yaw = st.yaws[pose], Variant = variants[variant], Gpu = Median(gpu), Cpu = Median(cpu), Interval = Median(interval), Collect = Median(collect), Classify = Median(classify), Tests = Median(tests), Replay = Median(replay), Tris = Median(tris), Batches = Median(batches), Casters = Median(casters), N = interval.Count });
                    try { restore?.Invoke(); } catch (Exception e) { note = "restore failed " + e.Message; } restore = null;
                    variant++;
                    if (variant >= variants.Count) { variant = 0; pose++; if (pose >= st.yaws.Length) { pose = 0; station++; if (station >= Stations.Length) { Stop("done"); return; } } stage = 0; return; }
                    stage = 1; at = Now; return;
            }
        }

        static string LookPair(WorldMacroPlaytestSession s, string name, string variant, float pitch, int yawCount = 0, string baseVariant = "")
        {
            var arts = Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).Where(a => a.isActiveAndEnabled && a.Sheet != null).ToList();
            var grasses = Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsSortMode.None).ToList();
            var camera = ArtCull307.CloneObserver("LookPair307_camera", arts, out var go);
            const int w = 1600, h = 900; var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var observers = arts.Select(a => a.Observer).ToArray(); var clocks = arts.Select(a => a.ContactPreviewClock265).ToArray(); var winds = grasses.Select(g => g.WindPreviewClock267).ToArray();
            string folder = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Performance/Perf307/Look")), name); Directory.CreateDirectory(folder);
            var lines = new List<string> { "#307 lookpair " + name + " " + variant + " " + DateTime.Now.ToString("s") + " | pitch " + pitch }; Action undoBase = null;
            try
            {
                camera.targetTexture = rt; camera.aspect = w / (float)h;
                if (baseVariant.Length > 0) { undoBase = Apply(baseVariant, s); lines[0] += " | base " + baseVariant; }
                foreach (var a in arts) { a.Observer = camera; a.ContactPreviewClock265 = 3.25f; }
                foreach (var g in grasses) g.WindPreviewClock267 = 3.25f;
                foreach (var st in Stations) foreach (var yaw in yawCount > 0 ? Enumerable.Range(0, yawCount).Select(i => i * 360f / yawCount).ToArray() : st.yaws)
                {
                    camera.transform.SetPositionAndRotation(st.feet + Vector3.up * 1.7f, Quaternion.Euler(pitch, yaw, 0f));
                    var a0 = Shot(camera, rt, tex, w, h);
                    var undo = Apply(variant, s);
                    var b0 = Shot(camera, rt, tex, w, h);
                    undo?.Invoke();
                    string stem = Path.Combine(folder, st.id + "_" + yaw.ToString("0", CultureInfo.InvariantCulture));
                    File.WriteAllBytes(stem + "_A.png", a0); File.WriteAllBytes(stem + "_B.png", b0);
                    lines.Add(st.id + " yaw " + yaw);
                }
            }
            finally
            {
                try { undoBase?.Invoke(); } catch (Exception e) { lines.Add("base undo failed " + e.Message); }
                for (int i = 0; i < arts.Count; i++) if (arts[i] != null) { arts[i].Observer = observers[i]; arts[i].ContactPreviewClock265 = clocks[i]; arts[i].ForgetCollect307(); }
                for (int i = 0; i < grasses.Count; i++) if (grasses[i] != null) grasses[i].WindPreviewClock267 = winds[i];
                camera.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
            }
            File.WriteAllLines(Path.Combine(folder, "lookpair.txt"), lines);
            return "lookpair " + name + ": " + (lines.Count - 1) + " pairs in " + folder;
        }

        static byte[] Shot(Camera camera, RenderTexture rt, Texture2D tex, int w, int h)
        {
            UnityEngine.Random.InitState(307); camera.Render();
            var active = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false); tex.Apply(false); RenderTexture.active = active;
            return tex.EncodeToPNG();
        }

        static float Median(List<float> v) { if (v.Count == 0) return float.NaN; var a = v.OrderBy(x => x).ToList(); return a[a.Count / 2]; }

        static Action Apply(string v, WorldMacroPlaytestSession s)
        {
            if (v == "A") return null;
            var undo = new List<Action>();
            foreach (var kv in v.Split(','))
            {
                var p = kv.Split('='); string k = p[0].Trim(), val = p[1].Trim();
                float f = float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float ff) ? ff : 0f;
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                switch (k)
                {
                    case "far":
                        foreach (var cam in Camera.allCameras.Where(x => x.cameraType == CameraType.Game)) { var c0 = cam; float prior = c0.farClipPlane; c0.farClipPlane = Mathf.Min(prior, f); undo.Add(() => { if (c0 != null) c0.farClipPlane = prior; }); }
                        break;
                    case "cascades":
                        if (asset != null) { int prior = asset.shadowCascadeCount; asset.shadowCascadeCount = Mathf.Clamp((int)f, 1, 4); undo.Add(() => asset.shadowCascadeCount = prior); }
                        break;
                    case "shadow":
                        if (asset != null) { float prior = asset.shadowDistance; asset.shadowDistance = f; undo.Add(() => asset.shadowDistance = prior); }
                        break;
                    case "feature":
                    {
                        var parts = val.Split('/'); var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Renderer297);
                        var feat = data != null ? data.rendererFeatures.FirstOrDefault(x => x != null && x.name == parts[0]) : null;
                        if (feat != null) { bool prior = feat.isActive; feat.SetActive(parts.Length > 1 && parts[1] == "1"); undo.Add(() => feat.SetActive(prior)); }
                        break;
                    }
                    case "lanternshadows":
                        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x => x.type != LightType.Directional && x.shadows != LightShadows.None))
                        { var l0 = l; var prior = l0.shadows; l0.shadows = LightShadows.None; undo.Add(() => { if (l0 != null) l0.shadows = prior; }); }
                        break;
                    case "lodbias":
                    { float prior = QualitySettings.lodBias; QualitySettings.lodBias = f; undo.Add(() => QualitySettings.lodBias = prior); break; }
                    case "fine":   // fine=0: the sheets' fine shadow cells off (in memory, restored)
                        foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None))
                        {
                            var sh = r.Sheet; if (sh == null) continue; float pr = sh.FineShadowRadius307; bool pl = sh.FineShadowLanterns307;
                            if (f < .5f) { sh.FineShadowRadius307 = 0; sh.FineShadowLanterns307 = false; } else if (f > 1.5f) sh.FineShadowRadius307 = f;
                            undo.Add(() => { sh.FineShadowRadius307 = pr; sh.FineShadowLanterns307 = pl; });
                        }
                        break;
                    case "treenear": case "treemid": case "pinenear": case "pinemid":   // LOD distances (sheet-wide or Meshy pines): in memory, re-prepared
                        foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None))
                        {
                            var sh = r.Sheet; if (sh == null) continue; var r0 = r;
                            float pn = sh.TreeNear, pm = sh.TreeMiddle; var po = sh.LodOverrides307;
                            if (k == "treenear") sh.TreeNear = f; else if (k == "treemid") sh.TreeMiddle = f;
                            else
                            {
                                var list = (po ?? new WorldMacroDressingSheetSO.LodOverride307[0]).ToList();
                                var o = list.FirstOrDefault(x => x.IdContains == "Pinus"); if (o == null) { o = new WorldMacroDressingSheetSO.LodOverride307 { IdContains = "Pinus" }; list.Insert(0, o); }
                                else { list[list.IndexOf(o)] = o = new WorldMacroDressingSheetSO.LodOverride307 { IdContains = "Pinus", TreeNear = o.TreeNear, TreeMiddle = o.TreeMiddle }; }
                                if (k == "pinenear") o.TreeNear = f; else o.TreeMiddle = f;
                                sh.LodOverrides307 = list.ToArray();
                            }
                            r0.Invalidate();
                            undo.Add(() => { sh.TreeNear = pn; sh.TreeMiddle = pm; sh.LodOverrides307 = po; if (r0 != null) r0.Invalidate(); });
                        }
                        break;
                    case "pineproxy":   // pineproxy=1: Meshy pine LOD0 shadows from LOD1 (shadow proxy), in memory, re-prepared
                        foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None))
                        {
                            var sh = r.Sheet; if (sh == null) continue; var r0 = r; var prior = sh.ShadowProxies307;
                            sh.ShadowProxies307 = f > .5f ? new[] { new WorldMacroDressingSheetSO.ShadowProxy307 { IdContains = "Pinus", ProxyLod = 1 } } : new WorldMacroDressingSheetSO.ShadowProxy307[0];
                            r0.Invalidate(); undo.Add(() => { sh.ShadowProxies307 = prior; if (r0 != null) r0.Invalidate(); });
                        }
                        break;
                    case "serial":   // serial=1: the single-thread S8 collect (parallel collect off)
                        foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None)) { var r0 = r; bool pr = r0.SerialCollect307; r0.SerialCollect307 = f > .5f; undo.Add(() => { if (r0 != null) r0.SerialCollect307 = pr; }); }
                        break;
                    case "spin": spinRate = f; undo.Add(() => spinRate = 0); break;
                    case "band":   // band=<m>: the 2c collect band (0 = collect every frame), in memory
                        foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None))
                        { var sh = r.Sheet; if (sh == null) continue; float pb = sh.CollectBandMetres307; sh.CollectBandMetres307 = f; undo.Add(() => sh.CollectBandMetres307 = pb); }
                        break;
                    case "sight":   // sight=0: InteriorSight307 (sealed cave cells) off
                        if (s.InteriorSight != null && f < .5f) { var sight = s.InteriorSight; bool was = sight.enabled; sight.enabled = false; undo.Add(() => { if (sight != null) sight.enabled = was; }); }
                        break;
                    case "scale":   // scale=<0.1-2>: URP render scale (pixel-bound vs geometry-bound test only)
                        if (asset != null) { float prior = asset.renderScale; asset.renderScale = f; undo.Add(() => asset.renderScale = prior); }
                        break;
                    // #307 black-speck root cause (in memory, restored): InkWash297 material strengths and the vegetation sheets' depth passes
                    case "inkdepth": case "inknormal": case "inkline": case "inkground": case "inkmode": case "inkfloor":
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/Finish297/Materials/M_InkWash297.mat");
                        string prop = k == "inkdepth" ? "_DepthEdgeStrength" : k == "inknormal" ? "_NormalEdgeStrength" : k == "inkline" ? "_LineInk" : k == "inkmode" ? "_DepthEdgeMode307" : k == "inkfloor" ? "_DepthEdgeFloor307" : "_GroundNormalEdge";
                        if (mat != null && mat.HasProperty(prop)) { float prior = mat.GetFloat(prop); mat.SetFloat(prop, f); undo.Add(() => mat.SetFloat(prop, prior)); }
                        break;
                    }
                    case "vegdepth":   // vegdepth=0: every sheet material drawn by the art renderers leaves DepthOnly/DepthNormals (not in the depth/normals textures)
                    case "vegcluster": // vegcluster=1: those materials' _ClusterFade307 (per-instance LOD cross-fade)
                    {
                        var mats = new HashSet<Material>();
                        foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None))
                            if (r.Sheet != null && r.Sheet.Prototypes != null) foreach (var proto in r.Sheet.Prototypes) if (proto != null && proto.Lods != null) foreach (var l in proto.Lods) if (l != null && l.Parts != null) foreach (var part in l.Parts) if (part != null && part.Material != null) mats.Add(part.Material);
                        if (k == "vegcluster") { foreach (var m in mats) if (m.HasProperty("_ClusterFade307")) { var m0 = m; float prior = m0.GetFloat("_ClusterFade307"); m0.SetFloat("_ClusterFade307", f); undo.Add(() => m0.SetFloat("_ClusterFade307", prior)); } break; }
                        foreach (var m in mats)
                        {
                            var m0 = m; bool d0 = m0.GetShaderPassEnabled("DepthOnly"), n0 = m0.GetShaderPassEnabled("DepthNormals");
                            m0.SetShaderPassEnabled("DepthOnly", f > .5f); m0.SetShaderPassEnabled("DepthNormals", f > .5f);
                            undo.Add(() => { m0.SetShaderPassEnabled("DepthOnly", d0); m0.SetShaderPassEnabled("DepthNormals", n0); });
                        }
                        break;
                    }
                    // #307 blade speck fix tuning (grass material, in memory): bladepx=<px floor>, bladetone=<amount>, bladestart/bladeend=<m>,
                    // bladegrey=<scale of the default ground tone .55/.51/.43>
                    case "bladepx": case "bladetone": case "bladestart": case "bladeend": case "bladegrey":
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/Architecture296/Materials/d8403c640456fbc49a590d961a0db795_Material.asset");
                        if (mat == null || !mat.HasProperty("_BladePixelFloor307")) break;
                        float px = mat.GetFloat("_BladePixelFloor307"); var tone = mat.GetVector("_BladeFarTone307"); var colour = mat.GetColor("_BladeFarColour307");
                        if (k == "bladepx") mat.SetFloat("_BladePixelFloor307", f);
                        else if (k == "bladegrey") mat.SetColor("_BladeFarColour307", new Color(.55f * f, .51f * f, .43f * f, 1));
                        else mat.SetVector("_BladeFarTone307", new Vector4(k == "bladestart" ? f : tone.x, k == "bladeend" ? f : tone.y, k == "bladetone" ? f : tone.z, 0));
                        undo.Add(() => { mat.SetFloat("_BladePixelFloor307", px); mat.SetVector("_BladeFarTone307", tone); mat.SetColor("_BladeFarColour307", colour); });
                        break;
                    }
                    case "sort":   // sort=1|-1: camera-drawn chunks front to back / back to front (CompactRebuildArtRenderer.SortChunks307)
                        foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None)) { var r0 = r; int prior = r0.SortChunks307; r0.SortChunks307 = (int)f; undo.Add(() => { if (r0 != null) r0.SortChunks307 = prior; }); }
                        break;
                    case "groundperf":   // groundperf=<flags>: KoreanInkGround296 _Perf307Ground cost split (in memory)
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/Architecture296/Materials/554e75388d01b1b47ac1543a7e0617b0_KoreanGround.mat");
                        if (mat != null && mat.HasProperty("_Perf307Ground")) { float prior = mat.GetFloat("_Perf307Ground"); mat.SetFloat("_Perf307Ground", f); undo.Add(() => mat.SetFloat("_Perf307Ground", prior)); }
                        break;
                    }
                    case "occlusion":   // occlusion=0|1: Camera.useOcclusionCulling on the game cameras (Umbra bake A/B)
                        // every scene Game camera, including the disabled hidden probe cameras of lookpair/ArtPixel (Camera.allCameras lists enabled ones only)
                        foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>().Where(x => x.cameraType == CameraType.Game && !EditorUtility.IsPersistent(x))) { var c0 = cam; bool prior = c0.useOcclusionCulling; c0.useOcclusionCulling = f > .5f; undo.Add(() => { if (c0 != null) c0.useOcclusionCulling = prior; }); }
                        break;
                    case "canopy": case "groundfloat":   // canopy=<v>: ground _CanopyStrength293 (in memory); the ground material's canopy darkening
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/Architecture296/Materials/554e75388d01b1b47ac1543a7e0617b0_KoreanGround.mat");
                        if (mat != null && mat.HasProperty("_CanopyStrength293")) { float prior = mat.GetFloat("_CanopyStrength293"); mat.SetFloat("_CanopyStrength293", f); undo.Add(() => mat.SetFloat("_CanopyStrength293", prior)); }
                        break;
                    }
                    case "triskip":   // triskip=<weight>: KoreanInkGround296 _TriplanarSkip307 (in memory)
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/Architecture296/Materials/554e75388d01b1b47ac1543a7e0617b0_KoreanGround.mat");
                        if (mat != null && mat.HasProperty("_TriplanarSkip307")) { float prior = mat.GetFloat("_TriplanarSkip307"); mat.SetFloat("_TriplanarSkip307", f); undo.Add(() => mat.SetFloat("_TriplanarSkip307", prior)); }
                        break;
                    }
                    case "prime":   // prime=0|1|2: Renderer297 depth priming (Disabled / Auto / Forced), in memory
                    {
                        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Renderer297);
                        if (data != null) { var prior = data.depthPrimingMode; data.depthPrimingMode = (DepthPrimingMode)(int)f; undo.Add(() => data.depthPrimingMode = prior); }
                        break;
                    }
                    case "softq":   // softq=0|1|2|3: soft shadow quality (UsePipelineSettings / Low / Medium / High)
                        if (asset != null)
                        {
                            var so = new SerializedObject(asset); var prop = so.FindProperty("m_SoftShadowQuality");
                            if (prop != null) { int prior = prop.intValue; prop.intValue = (int)f; so.ApplyModifiedPropertiesWithoutUndo(); undo.Add(() => { var so2 = new SerializedObject(asset); so2.FindProperty("m_SoftShadowQuality").intValue = prior; so2.ApplyModifiedPropertiesWithoutUndo(); }); }
                        }
                        break;
                    case "off":   // off=art|grass|terrain|addlights: that whole layer not drawn (GPU cost breakdown only)
                        if (val == "art") foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None)) { var r0 = r; bool was = r0.enabled; r0.enabled = false; undo.Add(() => { if (r0 != null) r0.enabled = was; }); }
                        if (val == "grass") foreach (var r in Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsSortMode.None)) { var r0 = r; bool was = r0.enabled; r0.enabled = false; undo.Add(() => { if (r0 != null) r0.enabled = was; }); }
                        if (val == "terrain") foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)) { var t0 = t; bool was = t0.drawHeightmap; t0.drawHeightmap = false; undo.Add(() => { if (t0 != null) t0.drawHeightmap = was; }); }
                        if (val == "ground") foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(x => x.enabled && x.sharedMaterial != null && x.sharedMaterial.shader != null && x.sharedMaterial.shader.name.Contains("KoreanInkGround")))
                        { var m0 = mr; m0.enabled = false; undo.Add(() => { if (m0 != null) m0.enabled = true; }); }
                        if (val == "post" || val == "smaa") foreach (var cam in Camera.allCameras.Where(x => x.cameraType == CameraType.Game))
                        {
                            var data = cam.GetComponent<UniversalAdditionalCameraData>(); if (data == null) continue; var d0 = data;
                            if (val == "post") { bool prior = d0.renderPostProcessing; d0.renderPostProcessing = false; undo.Add(() => { if (d0 != null) d0.renderPostProcessing = prior; }); }
                            else { var prior = d0.antialiasing; d0.antialiasing = AntialiasingMode.None; undo.Add(() => { if (d0 != null) d0.antialiasing = prior; }); }
                        }
                        if (val == "addlights") foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x => x.type != LightType.Directional && x.enabled)) { var l0 = l; l0.enabled = false; undo.Add(() => { if (l0 != null) l0.enabled = true; }); }
                        break;
                    case "artpath":
                        foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None)) { var r0 = r; int prior = r0.CullPath307; r0.CullPath307 = (int)f; undo.Add(() => { if (r0 != null) r0.CullPath307 = prior; }); }
                        break;
                    case "artoff":   // artoff=<i>: only the i-th art renderer (by name) disabled, to find which one a difference comes from
                    {
                        var list = Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).OrderBy(r => r.name).ToList(); int i = (int)f;
                        if (i >= 0 && i < list.Count) { var r0 = list[i]; r0.enabled = false; undo.Add(() => { if (r0 != null) r0.enabled = true; }); }
                        break;
                    }
                    case "artskip":   // artskip=<mask>: art chunks of these modes not drawn (1 ShadowsOnly, 2 On, 4 Off) — bisecting a difference
                        foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None)) { var r0 = r; int prior = r0.SkipModes307; r0.SkipModes307 = (int)f; undo.Add(() => { if (r0 != null) r0.SkipModes307 = prior; }); }
                        break;
                    case "artreverse":   // artreverse=1: art batches submitted in reverse order (depth-tie sensitivity check)
                        foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None)) { var r0 = r; bool prior = r0.ReverseSubmit307; r0.ReverseSubmit307 = f > .5f; undo.Add(() => { if (r0 != null) r0.ReverseSubmit307 = prior; }); }
                        break;
                    case "wholebounds":   // wholebounds=1: every art packet's worldBounds = the whole sheet box (tests Umbra against too-tight packet bounds)
                        foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None)) { var r0 = r; bool prior = r0.WholeSheetBounds307; r0.WholeSheetBounds307 = f > .5f; undo.Add(() => { if (r0 != null) r0.WholeSheetBounds307 = prior; }); }
                        break;
                }
            }
            foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None)) r.ForgetCollect307();   // lists follow the variant
            return () => { for (int i = undo.Count - 1; i >= 0; i--) undo[i](); foreach (var r in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None)) if (r != null) r.ForgetCollect307(); };
        }

        static void Stop(string how)
        {
            try { restore?.Invoke(); } catch { } restore = null;
            status = how; if (samples.Count == 0) return;
            Directory.CreateDirectory(Folder);
            var csv = new StringBuilder("station,yaw,variant,gpu_ms,cpuMain_ms,interval_ms,frames,collect_ms,classify_ms,tests_ms,replay_ms,tris,batches,shadow_casters\n");
            foreach (var x in samples) csv.AppendLine(string.Join(",", x.Station, x.Yaw.ToString("0.#", CultureInfo.InvariantCulture), "\"" + x.Variant + "\"", F(x.Gpu), F(x.Cpu), F(x.Interval), x.N, x.Collect.ToString("0.00", CultureInfo.InvariantCulture), x.Classify.ToString("0.00", CultureInfo.InvariantCulture), x.Tests.ToString("0.00", CultureInfo.InvariantCulture), x.Replay.ToString("0.00", CultureInfo.InvariantCulture), x.Tris.ToString("0", CultureInfo.InvariantCulture), x.Batches.ToString("0", CultureInfo.InvariantCulture), x.Casters.ToString("0", CultureInfo.InvariantCulture)));
            File.WriteAllText(Path.Combine(Folder, name + ".csv"), csv.ToString(), new UTF8Encoding(false));
            // table: per station+yaw, first A and each variant's delta vs the mean of both A samples
            var t = new StringBuilder("#307 sweep " + name + " " + DateTime.Now.ToString("s") + " | quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " | " + Screen.width + "x" + Screen.height + " | " + how + "\n");
            t.AppendLine("pose\tA gpu/cpu/int\t" + string.Join("\t", variants.Skip(1).Take(variants.Count - 2).Select(v => v + " (gpu/cpu/int)")) + "\tA-again gpu");
            foreach (var g in samples.GroupBy(x => x.Station + "@" + x.Yaw.ToString("0", CultureInfo.InvariantCulture)))
            {
                var list = g.ToList(); if (list.Count < 1) continue;
                var a = list[0];
                var line = g.Key + "\t" + F(a.Gpu) + "/" + F(a.Cpu) + "/" + F(a.Interval);
                for (int i = 1; i < list.Count - 1; i++) line += "\t" + F(list[i].Gpu) + "/" + F(list[i].Cpu) + "/" + F(list[i].Interval);
                if (list.Count > 1) line += "\t" + F(list[list.Count - 1].Gpu);
                t.AppendLine(line);
            }
            File.WriteAllText(Path.Combine(Folder, name + ".txt"), t.ToString(), new UTF8Encoding(false));
            if (census.Count > 0) File.WriteAllText(Path.Combine(Folder, name + "_census.tsv"), "pose\trenderer\tprototype\tlod\tmesh\ttris\ton\tshadowOnly\toff\tproxy\n" + string.Join("\n", census) + "\n", new UTF8Encoding(false));
            note = "wrote " + Path.Combine(Folder, name + ".txt");
        }

        static string F(float v) => float.IsNaN(v) ? "nan" : v.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
