using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 cave interior culling bake (SPEC-PERF-120). Runs inside Map307Capture's held Play:
    //   bake:<zoneId>[:cell=3][:radius=180][:w=480][:h=270]  |  status  |  abort
    // Per 3 m cell of the zone (lowest NavMesh floor inside WorldMapZoneSpec.Contains): eyes at floor + 1.0 / 2.2 / 3.4 m, 8 yaws x 3 pitches.
    // Each view renders the probe camera twice: reference, then "sealed" (vegetation/grass skip the camera, every layer culled beyond
    // radius). A cell is sealed only if every view is byte-identical. Unsealed cells dilate by one cell; cells without floor are sealed
    // only when every tested neighbour is. Writes Assets/_Project/Resources/Perf307/InteriorSight_<zone>.asset (read by App
    // InteriorSight307) and Art/Performance/Perf307/InteriorSight/<zone>.txt. Queue-safe; nothing else is saved.
    public static class InteriorSight307BakeTool
    {
        const string ResourceFolder = "Assets/_Project/Resources/Perf307";
        static string Out => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Performance/Perf307/InteriorSight"));
        static readonly float[] Yaws = { 0, 45, 90, 135, 180, 225, 270, 315 }, Pitches = { -30, 0, 30 }, Heights = { 1.0f, 2.2f, 3.4f };

        sealed class CellJob { public int X, Z, Level; public Vector3 Floor; public bool Tested, Sealed; public string FirstFail; }
        static string status = "idle", note = "";
        static List<CellJob> jobs; static int jobIndex, view; static bool hooked;
        static WorldMapZoneSpec zone; static string zoneId; static float cell, radius; static int width, height, gw, gh; static Vector2 origin;
        static Camera probe; static GameObject probeObject; static RenderTexture rt; static Texture2D tex;
        static List<CompactRebuildArtRenderer> arts; static List<CompactGrassRenderer266> grasses;
        static Camera[] priorObservers; static float[] priorClocks, priorWinds; static InteriorSight307 runtime; static bool runtimeWasEnabled;
        static readonly List<string> log = new List<string>(); static double began; static int renders, fails, saved;

        public static string Run(string arg)
        {
            arg = (arg ?? "").Trim();
            if (arg == "status") return status + " | " + note + (jobs != null ? " | cell " + jobIndex + "/" + jobs.Count + " renders " + renders + " failing views " + fails : "");
            if (arg == "abort") { Finish("aborted", false); return "aborted"; }
            if (!arg.StartsWith("bake:", StringComparison.Ordinal)) return "refused: bake:<zoneId>[:cell=3][:radius=180][:w=480][:h=270] | status | abort";
            if (status == "running") return "refused: a bake is running";
            var s = Map307Capture.HeldSession; if (s == null) return "refused: no held Play (Map307Capture play-hold first)";
            var parts = arg.Substring(5).Split(':'); zoneId = parts[0];
            float Opt(string k, float d) { foreach (var p in parts.Skip(1)) { var kv = p.Split('='); if (kv.Length == 2 && kv[0] == k && float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v; } return d; }
            cell = Mathf.Max(1f, Opt("cell", 3f)); radius = Mathf.Max(20f, Opt("radius", 180f)); width = (int)Opt("w", 480); height = (int)Opt("h", 270);
            var ui = Object.FindFirstObjectByType<PlaytestUiRoot>(); var data = ui != null ? ui.MapData : null;
            zone = data != null && data.Zones != null ? data.Zones.FirstOrDefault(z => z != null && z.Id == zoneId) : null;
            if (zone == null) return "refused: zone " + zoneId + " not in the loaded map data (" + (data != null ? string.Join(",", data.Zones.Where(z => z != null).Select(z => z.Id)) : "no map") + ")";
            if (zone.Polygon == null || zone.Polygon.Length < 3) return "refused: zone polygon missing";
            float minX = zone.Polygon.Min(p => p.x) - cell, maxX = zone.Polygon.Max(p => p.x) + cell, minZ = zone.Polygon.Min(p => p.y) - cell, maxZ = zone.Polygon.Max(p => p.y) + cell;
            origin = new Vector2(minX, minZ); gw = Mathf.CeilToInt((maxX - minX) / cell); gh = Mathf.CeilToInt((maxZ - minZ) / cell);
            float midY = float.IsInfinity(zone.MinimumY) || float.IsInfinity(zone.MaximumY) ? s.Walker.Body.transform.position.y : (zone.MinimumY + zone.MaximumY) * .5f;
            float band = float.IsInfinity(zone.MinimumY) || float.IsInfinity(zone.MaximumY) ? 30f : (zone.MaximumY - zone.MinimumY) * .5f + 2f;
            jobs = new List<CellJob>();
            for (int z = 0; z < gh; z++) for (int x = 0; x < gw; x++)
            {
                var c = new Vector3(origin.x + (x + .5f) * cell, midY, origin.y + (z + .5f) * cell);
                // every NavMesh level inside the zone (cave floor, ledges, the terrain surface above), lowest first, up to Levels
                var floors = new List<Vector3>();
                for (float y = midY - band; y <= midY + band; y += 1.5f)
                {
                    var at = new Vector3(c.x, y, c.z);
                    if (!NavMesh.SamplePosition(at, out var hit, 2f, NavMesh.AllAreas)) continue;
                    if (Mathf.Abs(hit.position.x - c.x) > cell * .5f || Mathf.Abs(hit.position.z - c.z) > cell * .5f || !zone.Contains(hit.position)) continue;
                    if (floors.All(f => Mathf.Abs(f.y - hit.position.y) > 1.5f)) floors.Add(hit.position);
                }
                floors.Sort((p, q) => p.y.CompareTo(q.y));
                for (int l = 0; l < Mathf.Min(floors.Count, InteriorSight307SO.Levels); l++) jobs.Add(new CellJob { X = x, Z = z, Level = l, Floor = floors[l], Tested = true });
            }
            int tested = jobs.Count(j => j.Tested);
            if (tested == 0) return "refused: no NavMesh floor inside zone " + zoneId;
            // probe camera = a copy of the player's view camera (same lens, renderer, post), fixed clocks, dithering off
            var source = s.Walker.ViewCamera;
            probeObject = new GameObject("InteriorSight307_probe") { hideFlags = HideFlags.HideAndDontSave }; probe = probeObject.AddComponent<Camera>();
            probe.CopyFrom(source); EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(), probe.GetUniversalAdditionalCameraData());
            probe.layerCullDistances = new float[32];   // never inherit the runtime's sealed-cell culling
            probe.GetUniversalAdditionalCameraData().dithering = false; probe.enabled = false;
            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            probe.targetTexture = rt; probe.aspect = width / (float)height;
            arts = Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None).Where(a => a.isActiveAndEnabled && a.Sheet != null).ToList();
            grasses = Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsSortMode.None).ToList();
            priorObservers = arts.Select(a => a.Observer).ToArray(); priorClocks = arts.Select(a => a.ContactPreviewClock265).ToArray(); priorWinds = grasses.Select(g => g.WindPreviewClock267).ToArray();
            foreach (var a in arts) { a.Observer = probe; a.ContactPreviewClock265 = 3.25f; a.SuppressFor307 = null; }
            foreach (var g in grasses) g.WindPreviewClock267 = 3.25f;
            runtime = s.InteriorSight; runtimeWasEnabled = runtime != null && runtime.enabled; if (runtime != null) runtime.enabled = false;
            log.Clear(); log.Add("#307 interior sight bake " + zoneId + " " + DateTime.Now.ToString("s") + " | cell " + cell + " m radius " + radius + " m | " + width + "x" + height + " | grid " + gw + "x" + gh + " tested " + tested + " | quality " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            jobIndex = 0; view = -1; renders = fails = saved = 0; began = EditorApplication.timeSinceStartup; status = "running"; note = "start";
            Directory.CreateDirectory(Out);
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            return "bake " + zoneId + " started: " + tested + " floor cells of " + jobs.Count + ", " + (Yaws.Length * Pitches.Length * Heights.Length) + " views each";
        }

        static void Tick()
        {
            if (status != "running") return;
            if (Map307Capture.HeldSession == null || !EditorApplication.isPlaying) { Finish("FAIL held Play ended", false); return; }
            double until = EditorApplication.timeSinceStartup + .25;   // bounded work per editor frame
            while (EditorApplication.timeSinceStartup < until)
            {
                if (jobIndex >= jobs.Count) { Finish("done", true); return; }
                var job = jobs[jobIndex];
                if (view < 0)
                {
                    // noise check once: the reference must repeat exactly
                    if (renders == 0)
                    {
                        Pose(job, 0); var r1 = Render(false); var r2 = Render(false);
                        if (!Same(r1, r2)) { Finish("INCONCLUSIVE reference renders differ (time-driven content)", false); return; }
                    }
                    job.Sealed = true; view = 0;
                }
                if (view < Yaws.Length * Pitches.Length * Heights.Length)
                {
                    Pose(job, view);
                    var a = Render(false); var b = Render(true);
                    if (!Same(a, b))
                    {
                        fails++; job.Sealed = false;
                        job.FirstFail = "view " + view + " yaw " + Yaws[view % Yaws.Length] + " pitch " + Pitches[view / Yaws.Length % Pitches.Length] + " h " + Heights[view / (Yaws.Length * Pitches.Length)];
                        if (saved < 8) { saved++; string stem = Path.Combine(Out, zoneId + "-fail-" + saved); File.WriteAllBytes(stem + "-A.png", Encode(a)); File.WriteAllBytes(stem + "-B.png", Encode(b)); }
                        view = int.MaxValue;   // one failing view is enough
                    }
                    else view++;
                }
                if (view >= Yaws.Length * Pitches.Length * Heights.Length)
                {
                    log.Add((job.Sealed ? "SEALED " : "OPEN   ") + job.X + "," + job.Z + " L" + job.Level + " floor " + job.Floor.ToString("F1") + (job.FirstFail != null ? " | " + job.FirstFail : ""));
                    jobIndex++; view = -1; note = "cell " + jobIndex + "/" + jobs.Count;
                }
            }
        }

        static void Pose(CellJob job, int v)
        {
            float yaw = Yaws[v % Yaws.Length], pitch = Pitches[v / Yaws.Length % Pitches.Length], h = Heights[v / (Yaws.Length * Pitches.Length)];
            probe.transform.SetPositionAndRotation(job.Floor + Vector3.up * h, Quaternion.Euler(pitch, yaw, 0f));
        }

        static Color32[] Render(bool sealedMode)
        {
            foreach (var a in arts) a.SuppressFor307 = sealedMode ? probe : null;
            var d = new float[32]; if (sealedMode) for (int i = 0; i < 32; i++) d[i] = radius;
            probe.layerCullDistances = d;   // URP ignores layerCullSpherical (the runtime no longer sets it)
            UnityEngine.Random.InitState(307); probe.Render(); renders++;
            var active = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); RenderTexture.active = active;
            foreach (var a in arts) a.SuppressFor307 = null;
            return tex.GetPixels32();
        }

        static bool Same(Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b) return false;
            return true;
        }

        static byte[] Encode(Color32[] px) { var t = new Texture2D(width, height, TextureFormat.RGBA32, false); t.SetPixels32(px); t.Apply(); var png = t.EncodeToPNG(); Object.DestroyImmediate(t); return png; }

        static void Finish(string how, bool write)
        {
            EditorApplication.update -= Tick; hooked = false;
            try
            {
                if (arts != null) for (int i = 0; i < arts.Count; i++) if (arts[i] != null) { arts[i].Observer = priorObservers[i]; arts[i].ContactPreviewClock265 = priorClocks[i]; arts[i].SuppressFor307 = null; }
                if (grasses != null) for (int i = 0; i < grasses.Count; i++) if (grasses[i] != null) grasses[i].WindPreviewClock267 = priorWinds[i];
                if (runtime != null) runtime.enabled = runtimeWasEnabled;
                if (probe != null) probe.targetTexture = null;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (tex != null) Object.DestroyImmediate(tex);
                if (probeObject != null) Object.DestroyImmediate(probeObject);
                if (write && jobs != null) WriteAsset();
            }
            catch (Exception e) { how += " | finish error " + e.Message; }
            status = how; log.Add("RESULT " + how + " | renders " + renders + " failing views " + fails + " | " + (EditorApplication.timeSinceStartup - began).ToString("0") + " s");
            if (jobs != null) { Directory.CreateDirectory(Out); File.WriteAllLines(Path.Combine(Out, zoneId + ".txt"), log, new UTF8Encoding(false)); }
        }

        static void WriteAsset()
        {
            int L = InteriorSight307SO.Levels, n = gw * gh * L;
            var state = new byte[n]; var floor = new float[n];   // state 1 sealed, 0 open, 2 no level
            for (int i = 0; i < n; i++) { state[i] = 2; floor[i] = float.NaN; }
            foreach (var j in jobs) { int i = (j.X + j.Z * gw) * L + j.Level; state[i] = (byte)(j.Sealed ? 1 : 0); floor[i] = j.Floor.y; }
            var result = new byte[n];
            for (int z = 0; z < gh; z++) for (int x = 0; x < gw; x++) for (int l = 0; l < L; l++)
            {
                int i = (x + z * gw) * L + l; if (state[i] != 1) continue;
                bool open = false;
                // dilation: an open level on the same height (within 3 m) in a neighbouring cell opens this one
                for (int dz = -1; dz <= 1 && !open; dz++) for (int dx = -1; dx <= 1 && !open; dx++)
                {
                    int nx = x + dx, nz = z + dz; if (nx < 0 || nz < 0 || nx >= gw || nz >= gh) continue;
                    for (int m = 0; m < L; m++) { int k = (nx + nz * gw) * L + m; if (state[k] == 0 && Mathf.Abs(floor[k] - floor[i]) < 3f) { open = true; break; } }
                }
                result[i] = (byte)(open ? 0 : 1);
            }
            if (!AssetDatabase.IsValidFolder(ResourceFolder)) { if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources")) AssetDatabase.CreateFolder("Assets/_Project", "Resources"); AssetDatabase.CreateFolder("Assets/_Project/Resources", "Perf307"); }
            string path = ResourceFolder + "/InteriorSight_" + zoneId + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<InteriorSight307SO>(path);
            bool created = asset == null; if (created) asset = ScriptableObject.CreateInstance<InteriorSight307SO>();
            asset.ZoneId = zoneId; asset.Origin = origin; asset.Cell = cell; asset.Width = gw; asset.Height = gh; asset.SightRadius = radius; asset.Sealed = result; asset.FloorY = floor; asset.EyeLow = Heights.Min() - .4f; asset.EyeHigh = Heights.Max() + .5f;
            float minY = jobs.Min(j => j.Floor.y), maxY = jobs.Max(j => j.Floor.y);
            asset.MinY = minY + asset.EyeLow; asset.MaxY = maxY + asset.EyeHigh;
            asset.BakeInfo = log[0] + "\nsealed levels " + result.Count(v => v == 1) + " (tested sealed " + jobs.Count(j => j.Sealed) + ", open " + jobs.Count(j => !j.Sealed) + ", lost to dilation " + (jobs.Count(j => j.Sealed) - result.Count(v => v == 1)) + ")";
            if (created) AssetDatabase.CreateAsset(asset, path); else EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            log.Add("asset " + path + " | " + asset.BakeInfo.Split('\n').Last() + " | camera y band " + asset.MinY.ToString("F1") + ".." + asset.MaxY.ToString("F1"));
        }
    }
}
