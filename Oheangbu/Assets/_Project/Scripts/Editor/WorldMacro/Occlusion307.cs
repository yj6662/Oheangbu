using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #307 user request (2026-10-01): "오클루전 컬링 한번 적용해봐". Umbra occlusion culling for the open W_Demo_Main (Edit mode):
    //   status                      bake data, parameters, static-flag counts, occlusion areas
    //   areas[:tile=250][:above=12][:within=x0,z0,x1,z1+...][:ring=0]   view volumes only where the camera can be: one OcclusionArea per tile
    //                               that holds NavMesh (optionally only tiles inside the boxes), NavMesh height -2 .. +above m.
    //                               Under root "Occlusion307_Areas" (scene object). | areas-remove | eyes (sweep stations inside a volume?)
    //   flags:dry | flags-apply | flags-revert   occluder/occludee static flags by rule (terrain LOD0, Finish297 walls, Highlands293 rock
    //                               masses in; thin posts, cutout/transparent, moving cargo out). apply writes a GlobalObjectId manifest.
    //   bake[:hole=1][:occluder=10][:backface=100][:maxgb=<now+6>][:minfree=2]   StaticOcclusionCulling.GenerateInBackground; a watchdog
    //                               cancels when the editor's private memory passes maxgb or free physical memory drops under minfree GB
    //                               (BSOD guard; the watch survives a domain reload through SessionState)
    //   bake-status | cancel | clear (removes the bake data, keeps the areas) | save
    // The scene is saved only by `save` (refused while a bake runs). Flags and areas do nothing at runtime without bake data.
    // 2026-10-01 trial result (SPEC-PERF-120): full bake (5921 64 m tiles, 18.9 MB) -0.6..-2.8 ms GPU / up to -2.7 ms CPU, but with the
    // terrain as occluder Umbra drops instanced vegetation packets whose crowns show over ridges (13 of 96 lookpair headings);
    // finer params, LOD0 everywhere, surface-wide volumes and 10 m bounds padding do not help, whole-sheet bounds do (cost x3
    // shadows). Without the terrain occluder the gain is 0. Reverted (clear, areas-remove, flags-revert, params, save).
    // #308 D308-8e: the runtime half of the trial was removed (OcclusionVolume307 guard, UmbraExemption307, the vegetation renderers'
    // hooks; SPEC-ART-RENDERER-CULLING §9, not adopted). `areas` adds no guard and `bake` refuses: without the guard a camera outside
    // the view volumes is culled from the nearest cell, and Umbra drops vegetation that shows over ridges. To resume the trial
    // restore the files kept in Tools/Unity/Stage308_small/Original first. Inspection and restore commands still work.
    [InitializeOnLoad]
    public static class Occlusion307
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string AreaRoot = "Occlusion307_Areas";
        const string KeyWatch = "Occlusion307.watch", KeyMax = "Occlusion307.maxgb", KeyMin = "Occlusion307.minfree", KeyStart = "Occlusion307.start", KeyLast = "Occlusion307.last", KeyPeak = "Occlusion307.peak";
        static string Backups => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Tools/Unity/Stage307_backup_occlusion"));
        static double nextCheck;

        static Occlusion307() { EditorApplication.update += Watch; }

        static bool Watching { get => SessionState.GetBool(KeyWatch, false); set => SessionState.SetBool(KeyWatch, value); }
        static string LastBake { get => SessionState.GetString(KeyLast, ""); set => SessionState.SetString(KeyLast, value); }
        static float PeakGb { get => SessionState.GetFloat(KeyPeak, 0); set => SessionState.SetFloat(KeyPeak, value); }

        [StructLayout(LayoutKind.Sequential)]
        struct MemoryStatus { public uint length, load; public ulong totalPhys, availPhys, totalPage, availPage, totalVirtual, availVirtual, availExtended; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        static double FreeGb() { var m = new MemoryStatus { length = (uint)Marshal.SizeOf<MemoryStatus>() }; return GlobalMemoryStatusEx(ref m) ? m.availPhys / 1073741824.0 : double.NaN; }
        [StructLayout(LayoutKind.Sequential)]
        struct ProcessCounters { public uint cb, pageFaults; public UIntPtr peakWorkingSet, workingSet, quotaPeakPaged, quotaPaged, quotaPeakNonPaged, quotaNonPaged, pagefile, peakPagefile, privateUsage; }
        [DllImport("psapi.dll", SetLastError = true)] static extern bool GetProcessMemoryInfo(IntPtr process, out ProcessCounters counters, uint size);
        // Mono's Process.PrivateMemorySize64 reads 0 inside the editor; psapi PrivateUsage is the Task Manager commit figure
        static double EditorGb() { return GetProcessMemoryInfo(Process.GetCurrentProcess().Handle, out var c, (uint)Marshal.SizeOf<ProcessCounters>()) ? c.privateUsage.ToUInt64() / 1073741824.0 : double.NaN; }

        static float F(string arg, string key, float d) { var m = System.Text.RegularExpressions.Regex.Match(arg, "(?:^|:)" + key + @"=([0-9.]+)"); return m.Success ? float.Parse(m.Groups[1].Value, Inv) : d; }
        static Scene Main => SceneManager.GetSceneByName("W_Demo_Main");

        public static string Run(string arg)
        {
            arg = (arg ?? "").Trim();
            if (EditorApplication.isPlayingOrWillChangePlaymode && arg != "status" && arg != "bake-status" && arg != "eyes" && arg != "cameras") return "refused: Edit mode only";
            switch (arg.Split(':')[0])
            {
                case "status": return Status();
                case "bake-status": return (StaticOcclusionCulling.isRunning ? "running " + (EditorApplication.timeSinceStartup - SessionState.GetFloat(KeyStart, 0)).ToString("F0") + " s" : "idle") + " | editor " + EditorGb().ToString("F1") + " GB (peak " + PeakGb.ToString("F1") + ") free " + FreeGb().ToString("F1") + " GB | data " + StaticOcclusionCulling.umbraDataSize + " B | " + LastBake;
                case "cancel": StaticOcclusionCulling.Cancel(); Watching = false; LastBake += " | cancelled by command"; return "cancelled";
                case "clear":
                {
                    if (StaticOcclusionCulling.isRunning) return "refused: a bake is running";
                    StaticOcclusionCulling.Clear();
                    return "cleared bake data (" + StaticOcclusionCulling.umbraDataSize + " B)";
                }
                case "areas": return Areas(F(arg, "tile", 250), F(arg, "above", 12), Within(arg), (int)F(arg, "ring", 0), F(arg, "surface", 0) > .5f);
                case "areas-remove":
                {
                    var root = FindRoot(); if (root == null) return "no areas";
                    Object.DestroyImmediate(root); EditorSceneManager.MarkSceneDirty(Main); return "areas removed (scene marked dirty, not saved)";
                }
                case "eyes": return Eyes();
                case "params":   // params[:hole=][:occluder=][:backface=]: the scene's bake settings without baking (restore after a trial)
                    StaticOcclusionCulling.smallestHole = F(arg, "hole", StaticOcclusionCulling.smallestHole); StaticOcclusionCulling.smallestOccluder = F(arg, "occluder", StaticOcclusionCulling.smallestOccluder);
                    StaticOcclusionCulling.backfaceThreshold = F(arg, "backface", StaticOcclusionCulling.backfaceThreshold); EditorSceneManager.MarkSceneDirty(Main);
                    return "params hole " + StaticOcclusionCulling.smallestHole + " occluder " + StaticOcclusionCulling.smallestOccluder + " backface " + StaticOcclusionCulling.backfaceThreshold + " (scene marked dirty, not saved)";
                case "dynocc-clean":   // after flags-revert: drop the prefab overrides m_DynamicOccludee = 1 (the default) that the revert left behind
                {
                    int roots = 0, removed = 0;
                    foreach (var go in Main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).Where(PrefabUtility.IsOutermostPrefabInstanceRoot).ToList())
                    {
                        var mods = PrefabUtility.GetPropertyModifications(go); if (mods == null) continue;
                        var keep = mods.Where(m => !(m.propertyPath == "m_DynamicOccludee" && m.value == "1")).ToArray();
                        if (keep.Length == mods.Length) continue;
                        removed += mods.Length - keep.Length; roots++; PrefabUtility.SetPropertyModifications(go, keep);
                    }
                    EditorSceneManager.MarkSceneDirty(Main);
                    return "dynocc-clean: " + removed + " overrides removed on " + roots + " prefab roots (scene marked dirty, not saved)";
                }
                case "cameras":   // every scene Game camera: enabled, occlusion culling flag, position
                    return string.Join("\n", Resources.FindObjectsOfTypeAll<Camera>().Where(c => c.cameraType == CameraType.Game && !EditorUtility.IsPersistent(c))
                        .Select(c => "  " + PathOf(c.transform) + " enabled " + c.isActiveAndEnabled + " occlusion " + c.useOcclusionCulling + " at " + c.transform.position.ToString("F0")));
                case "names": return Names(arg.Length > 6 ? arg.Substring(6) : "");
                case "trim":
                {
                    double before = EditorGb(), freeBefore = FreeGb();
                    EditorUtility.UnloadUnusedAssetsImmediate(true); GC.Collect(); GC.WaitForPendingFinalizers();
                    return "trim: editor " + before.ToString("F1") + " -> " + EditorGb().ToString("F1") + " GB, free " + freeBefore.ToString("F1") + " -> " + FreeGb().ToString("F1") + " GB";
                }
                case "flags": return Flags(false, arg.Contains("noterrain"), arg.Contains("census"), arg.Contains("props"), arg.Contains("terrainocc"));
                case "flags-apply": return Flags(true, arg.Contains("noterrain"), false, arg.Contains("props"), arg.Contains("terrainocc"));
                case "flags-revert": return FlagsRevert();
                case "bake":   // #308 D308-8e: refused (header). The former body (parameters, memory watchdog start) is in Stage308_small/Original.
                    return "refused: the runtime half of the Umbra trial (OcclusionVolume307 guard, vegetation exemption) was removed by D308-8e; restore Tools/Unity/Stage308_small/Original before baking (SPEC-ART-RENDERER-CULLING section 9)";
                case "save":
                {
                    if (StaticOcclusionCulling.isRunning) return "refused: a bake is running";
                    foreach (var g in Main.GetRootGameObjects()) foreach (var s in g.GetComponentsInChildren<Oheangbu.App.World.WorldMacroPlaytestSession>(true))
                        if (Oheangbu.App.World.WorldMacroPlaytestSession.StaleHarnessSuffix307(s.TestSaveSuffix)) s.TestSaveSuffix = "";
                    return "saved " + EditorSceneManager.SaveScene(Main);
                }
            }
            return "refused: status | areas[:tile=][:above=][:within=x0,z0,x1,z1+..] | areas-remove | eyes | flags | flags-apply | flags-revert | bake[:hole=][:occluder=][:backface=][:maxgb=][:minfree=] | bake-status | cancel | clear | save";
        }

        static void Watch()
        {
            if (!Watching) return;
            double now = EditorApplication.timeSinceStartup; if (now < nextCheck) return; nextCheck = now + .5;
            double editor = EditorGb(), free = FreeGb(); if (editor > PeakGb) PeakGb = (float)editor;
            float maxGb = SessionState.GetFloat(KeyMax, 26), minFree = SessionState.GetFloat(KeyMin, 4);
            if (editor > maxGb || free < minFree)
            {
                StaticOcclusionCulling.Cancel(); Watching = false;
                LastBake += " | CANCELLED: editor " + editor.ToString("F1") + " GB, free " + free.ToString("F1") + " GB";
                Debug.LogWarning("[Occlusion307] bake cancelled: editor " + editor.ToString("F1") + " GB, free " + free.ToString("F1") + " GB"); return;
            }
            if (!StaticOcclusionCulling.isRunning)
            {
                Watching = false;
                LastBake += " | finished in " + (now - SessionState.GetFloat(KeyStart, 0)).ToString("F0") + " s, data " + StaticOcclusionCulling.umbraDataSize + " B, peak editor " + PeakGb.ToString("F1") + " GB";
                Debug.Log("[Occlusion307] " + LastBake);
            }
        }

        static GameObject FindRoot() { if (!Main.IsValid()) return null; return Main.GetRootGameObjects().FirstOrDefault(g => g.name == AreaRoot); }

        static string Status()
        {
            var sb = new StringBuilder();
            sb.Append("bake data ").Append(StaticOcclusionCulling.umbraDataSize).Append(" B, running ").Append(StaticOcclusionCulling.isRunning)
              .Append(" | params hole ").Append(StaticOcclusionCulling.smallestHole).Append(" occluder ").Append(StaticOcclusionCulling.smallestOccluder).Append(" backface ").Append(StaticOcclusionCulling.backfaceThreshold)
              .Append(" | editor ").Append(EditorGb().ToString("F1")).Append(" GB, free ").Append(FreeGb().ToString("F1")).Append(" GB\n");
            int occluder = 0, occludee = 0, renderers = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != Main) continue; renderers++;
                var f = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                if ((f & StaticEditorFlags.OccluderStatic) != 0) occluder++; if ((f & StaticEditorFlags.OccludeeStatic) != 0) occludee++;
            }
            sb.Append("renderers ").Append(renderers).Append(", occluder static ").Append(occluder).Append(", occludee static ").Append(occludee).Append('\n');
            var areas = Object.FindObjectsByType<OcclusionArea>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.Append("occlusion areas ").Append(areas.Length).Append(" (root ").Append(FindRoot() != null).Append(")");
            if (areas.Length > 0) { var b = new Bounds(areas[0].transform.TransformPoint(areas[0].center), Vector3.zero); foreach (var a in areas) b.Encapsulate(new Bounds(a.transform.TransformPoint(a.center), a.size)); sb.Append(" covering ").Append(b.min.ToString("F0")).Append(" .. ").Append(b.max.ToString("F0")); }
            return sb.ToString();
        }

        // within=x0,z0,x1,z1+x0,z0,x1,z1 (world XZ boxes); a tile is kept when its centre is inside one
        static List<Rect> Within(string arg)
        {
            var m = System.Text.RegularExpressions.Regex.Match(arg, @"within=([0-9.,+\-]+)"); var list = new List<Rect>(); if (!m.Success) return list;
            foreach (var box in m.Groups[1].Value.Split('+'))
            {
                var v = box.Split(',').Select(x => float.Parse(x, Inv)).ToArray(); if (v.Length != 4) continue;
                list.Add(Rect.MinMaxRect(Mathf.Min(v[0], v[2]), Mathf.Min(v[1], v[3]), Mathf.Max(v[0], v[2]), Mathf.Max(v[1], v[3])));
            }
            return list;
        }

        static string Areas(float tile, float above, List<Rect> within, int ring, bool surface)
        {
            if (!Main.IsValid() || !Main.isLoaded) return "refused: W_Demo_Main is not open";
            var tri = NavMesh.CalculateTriangulation(); if (tri.vertices.Length == 0) return "refused: no NavMesh loaded";
            var cells = new Dictionary<Vector2Int, Vector2>();   // tile -> (minY, maxY)
            void Add(Vector3 p)
            {
                var k = new Vector2Int(Mathf.FloorToInt(p.x / tile), Mathf.FloorToInt(p.z / tile));
                if (within.Count > 0 && !within.Any(r => r.Contains(new Vector2((k.x + .5f) * tile, (k.y + .5f) * tile)))) return;
                cells[k] = cells.TryGetValue(k, out var v) ? new Vector2(Mathf.Min(v.x, p.y), Mathf.Max(v.y, p.y)) : new Vector2(p.y, p.y);
            }
            for (int i = 0; i < tri.indices.Length; i += 3)
            {
                var a = tri.vertices[tri.indices[i]]; var b = tri.vertices[tri.indices[i + 1]]; var c = tri.vertices[tri.indices[i + 2]];
                // NavMesh polygons span far more than a 64 m tile: sample the whole triangle at tile/4, not only its corners
                float edge = Mathf.Max((a - b).magnitude, Mathf.Max((b - c).magnitude, (c - a).magnitude));
                int n = Mathf.Max(1, Mathf.CeilToInt(edge / (tile * .25f)));
                for (int u = 0; u <= n; u++) for (int v = 0; v <= n - u; v++) Add(a + (b - a) * (u / (float)n) + (c - a) * (v / (float)n));
            }
            if (surface)   // surface=1: also every tile of the within boxes over the ground (8x8 rays from above, topmost collider hit), walkable or not
            {
                foreach (var box in within)
                    for (int kx = Mathf.FloorToInt(box.xMin / tile); kx * tile < box.xMax; kx++)
                        for (int kz = Mathf.FloorToInt(box.yMin / tile); kz * tile < box.yMax; kz++)
                        {
                            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                            for (int i = 0; i < 8; i++) for (int j = 0; j < 8; j++)
                                if (Physics.Raycast(new Vector3((kx + (i + .5f) / 8f) * tile, 3000f, (kz + (j + .5f) / 8f) * tile), Vector3.down, out var hit, 6000f, ~0, QueryTriggerInteraction.Ignore))
                                { lo = Mathf.Min(lo, hit.point.y); hi = Mathf.Max(hi, hit.point.y); }
                            if (lo > hi) continue;
                            var k = new Vector2Int(kx, kz);
                            cells[k] = cells.TryGetValue(k, out var v) ? new Vector2(Mathf.Min(v.x, lo), Mathf.Max(v.y, hi)) : new Vector2(lo, hi);
                        }
            }
            if (ring > 0)   // ring=N: also the N neighbouring tiles, each with the union of its neighbours' height ranges (NavMesh edges, gate passages)
            {
                var grown = new Dictionary<Vector2Int, Vector2>(cells);
                foreach (var kv in cells) for (int dx = -ring; dx <= ring; dx++) for (int dz = -ring; dz <= ring; dz++)
                {
                    var k = kv.Key + new Vector2Int(dx, dz);
                    grown[k] = grown.TryGetValue(k, out var v) ? new Vector2(Mathf.Min(v.x, kv.Value.x), Mathf.Max(v.y, kv.Value.y)) : kv.Value;
                }
                cells = grown;
            }
            var old = FindRoot(); if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(AreaRoot); SceneManager.MoveGameObjectToScene(root, Main);
            double volume = 0;
            foreach (var kv in cells)
            {
                float y0 = kv.Value.x - 2f, y1 = kv.Value.y + above;
                var go = new GameObject("Area_" + kv.Key.x + "_" + kv.Key.y); go.transform.SetParent(root.transform, false);
                var area = go.AddComponent<OcclusionArea>();
                area.center = new Vector3((kv.Key.x + .5f) * tile, (y0 + y1) * .5f, (kv.Key.y + .5f) * tile); area.size = new Vector3(tile, y1 - y0, tile);
                volume += (double)tile * tile * (y1 - y0);
            }
            // #308 D308-8e: no runtime guard is added any more (header); these volumes only describe where a bake would look from
            EditorSceneManager.MarkSceneDirty(Main);
            return "areas: " + cells.Count + " tiles of " + tile + " m holding NavMesh" + (within.Count > 0 ? " inside " + within.Count + " box(es)" : "") + ", total view volume " + (volume / 1e9).ToString("F3") + " km^3 (scene marked dirty, not saved)\n" + Eyes();
        }

        static string Eyes()
        {
            var areas = Object.FindObjectsByType<OcclusionArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var sb = new StringBuilder("sweep station eyes (feet + 1.7 m) inside a view volume:");
            foreach (var st in PerfSweep307.Stations)
            {
                var eye = st.feet + Vector3.up * 1.7f;
                bool inside = areas.Any(a => new Bounds(a.transform.TransformPoint(a.center), a.size).Contains(eye));
                sb.Append(' ').Append(st.id).Append('=').Append(inside ? "yes" : "no");
                if (!inside)
                {
                    var near = areas.Select(a => new Bounds(a.transform.TransformPoint(a.center), a.size)).OrderBy(b => (new Vector2(b.center.x, b.center.z) - new Vector2(eye.x, eye.z)).sqrMagnitude).FirstOrDefault();
                    sb.Append(" (eye ").Append(eye.ToString("F1")).Append(", nearest volume ").Append(near.min.ToString("F0")).Append("..").Append(near.max.ToString("F0"));
                    sb.Append(NavMesh.SamplePosition(eye, out var hit, 30f, NavMesh.AllAreas) ? ", NavMesh " + (hit.position - eye).magnitude.ToString("F1") + " m away at " + hit.position.ToString("F1") : ", no NavMesh within 30 m").Append(')');
                }
            }
            return sb.ToString();
        }

        // names:<root object name>: distinct active MeshRenderer names below it, with count, largest size and flags (rule authoring aid)
        static string Names(string rootName)
        {
            var root = Main.GetRootGameObjects().FirstOrDefault(g => g.name == rootName); if (root == null) return "no root " + rootName;
            var rows = root.GetComponentsInChildren<MeshRenderer>(false).Where(r => r.enabled)
                .GroupBy(r => System.Text.RegularExpressions.Regex.Replace(r.name, @"_[0-9]+(_[0-9]+)*$", "_#") + " <" + (r.transform.parent != null ? System.Text.RegularExpressions.Regex.Replace(r.transform.parent.name, @"_[0-9]+(_[0-9]+)*$", "_#") : "") + ">")
                .Select(g => (g.Key, n: g.Count(), size: g.Max(Size), flags: string.Join("|", g.Select(r => (int)GameObjectUtility.GetStaticEditorFlags(r.gameObject)).Distinct())))
                .OrderByDescending(x => x.size).Take(60);
            return rootName + ":\n" + string.Join("\n", rows.Select(x => "  " + x.Key + " x" + x.n + " max " + x.size.ToString("F0") + " m flags " + x.flags));
        }

        // ---------- static flags ----------
        const StaticEditorFlags Occ = StaticEditorFlags.OccluderStatic, Ocd = StaticEditorFlags.OccludeeStatic;

        static bool Under(Transform t, Func<string, bool> name) { for (; t != null; t = t.parent) if (name(t.name)) return true; return false; }
        static bool Has(string s, string part) => s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        static float Size(Renderer r) { var e = r.bounds.size; return Mathf.Max(e.x, Mathf.Max(e.y, e.z)); }
        static bool SeeThrough(Renderer r) => r.sharedMaterials.Any(m => m != null && m.renderQueue >= 2450);
        static string PathOf(Transform t) { var p = t.name; for (t = t.parent; t != null; t = t.parent) p = t.name + "/" + p; return p; }

        // the target flags for one renderer's GameObject, and the rule that set them ("" = unchanged)
        static StaticEditorFlags Target(Renderer r, StaticEditorFlags f, bool noTerrain, bool props, out string rule, bool terrainOccOnly = false)
        {
            rule = ""; var t = r.transform; var n = t.name;
            if (Under(t, x => x == "SealedCargo252")) { rule = "cargo-out"; return f & ~(Occ | Ocd); }   // reparented onto the palanquin at runtime
            bool terrain = t.parent != null && t.parent.name.StartsWith("Terrain_") && Under(t, x => x == "Reworld292_Terrain");
            if (terrain && n == "LOD0") { if (terrainOccOnly) { rule = "terrain-lod0-occluder-only"; return (f | Occ) & ~Ocd; } rule = noTerrain ? "terrain-lod0-occludee" : "terrain-lod0"; return noTerrain ? (f | Ocd) & ~Occ : f | Occ | Ocd; }   // noterrain: the ground hides nothing (ridge test)
            if (terrain && (n == "LOD1" || n == "LOD2")) { if (terrainOccOnly) return f & ~(Occ | Ocd); rule = "terrain-lod12"; return (f | Ocd) & ~Occ; }   // terrainocc: the ground is never culled (it keeps casting shadows onto ridge tops)   // LODGroup occludes with LOD0 only
            bool lowLod = n.EndsWith("_LOD1") || n.EndsWith("_LOD2") || n.EndsWith("_LOD3");
            if ((f & Occ) != 0)
            {
                if (SeeThrough(r)) { rule = "cutout-out"; return f & ~Occ; }
                if (Under(t, x => Has(x, "NativePostsAndBeams") || Has(x, "CAP_GATE_COLUMN"))) { rule = "thin-out"; return f & ~Occ; }
                if (Under(t, x => Has(x, "FixedArchInfill")) && (Has(n, "lintel") || Has(n, "transom"))) { rule = "thin-out"; return f & ~Occ; }
            }
            if (SeeThrough(r)) return props && (f & Ocd) == 0 && Moving(t) == null && !Under(t, x => DenyRoots.Contains(x)) ? Prop(f, out rule) : f;
            // Finish297 kit pieces are <Piece>/LOD0..2; the piece name is on the parent and only LOD0 occludes
            string piece = n.StartsWith("LOD") && t.parent != null ? t.parent.name : n;
            if (Under(t, x => x.StartsWith("Finish297")) && (n == "LOD0" || !n.StartsWith("LOD")) && (Has(piece, "Wall") || Has(piece, "Revet") || Has(piece, "Terrace") || Has(piece, "Haeng"))
                && !Has(piece, "Rail") && !Has(piece, "Door") && Size(r) >= 8f) { rule = "finish-wall"; return f | Occ | Ocd; }
            if (Under(t, x => x.StartsWith("Highlands293")) && (Has(n, "Band") || Has(n, "Apron") || Has(n, "Fortress_wall_remnant") || Has(n, "Crest_mass")) && Size(r) >= 15f)
            { rule = lowLod ? "highland-mass-lod12" : "highland-mass"; return lowLod ? (f | Ocd) & ~Occ : f | Occ | Ocd; }
            // #307 §9: Umbra's dynamic-object test drops objects showing over ridges (seen on the vegetation packets); a non-moving
            // authored renderer is therefore made a static occludee (baked target). Anything that may move keeps 0 and loses dynamic occlusion.
            if (props && (f & Ocd) == 0 && Moving(t) == null && !Under(t, x => DenyRoots.Contains(x))) { rule = "prop-occludee"; return f | Ocd; }
            return f;
        }

        static StaticEditorFlags Prop(StaticEditorFlags f, out string rule) { rule = "prop-occludee"; return f | Ocd; }
        // roots whose renderers move or are gameplay actors (player rig, NPCs and enemies, vehicle, escort) and the tool's own root
        static readonly HashSet<string> DenyRoots = new HashSet<string> { "WorldMacro_Playtest", "WorldMacro_MagicPalanquin_TEST", "CapitalEscort252", "Macro_CombatPlayerRig", AreaRoot };
        static readonly System.Text.RegularExpressions.Regex MovingName = new System.Text.RegularExpressions.Regex(
            "Door|Gate.*Presentation|Presentation|Vehicle|Palanquin|Escort|Npc|NPC|Enemy|Folklore|Summon|Cheongryong|Sway|Bob|Spin|Rotat|Swing|Flicker|Waver|Drift|Mover|Patrol|Follow|Controller|Agent|Tween|Animat|Physics|Ragdoll|Float");
        // the first component on t or an ancestor that can move the renderer at runtime (null = none)
        static string Moving(Transform t)
        {
            for (; t != null; t = t.parent)
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null || c is Transform || c is MeshFilter || c is Renderer || c is LODGroup || c is Collider) continue;
                    if (c is Animator || c is Animation || c is Rigidbody || c is UnityEngine.AI.NavMeshAgent || c is CharacterController || c is Cloth || c is ParticleSystem || c is Joint) return c.GetType().Name;
                    if (c is MonoBehaviour && MovingName.IsMatch(c.GetType().Name)) return c.GetType().Name;
                }
            return null;
        }

        static string Flags(bool apply, bool noTerrain, bool census = false, bool props = false, bool terrainOcc = false)
        {
            if (!Main.IsValid() || !Main.isLoaded) return "refused: W_Demo_Main is not open";
            var rows = new List<(GameObject go, StaticEditorFlags before, StaticEditorFlags after, string rule)>();
            var seen = new HashSet<GameObject>(); var final = new Dictionary<GameObject, StaticEditorFlags>();
            var movers = new Dictionary<string, int>();
            foreach (var root in Main.GetRootGameObjects()) foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled || !seen.Add(r.gameObject)) continue;
                var f = GameObjectUtility.GetStaticEditorFlags(r.gameObject); var to = Target(r, f, noTerrain, props, out var rule, terrainOcc);
                if (to != f) { rows.Add((r.gameObject, f, to, rule)); final[r.gameObject] = to; }
                if (census && (to & Ocd) == 0) { var m = Moving(r.transform) ?? (Under(r.transform, x => DenyRoots.Contains(x)) ? "(deny root)" : "?"); movers[m] = movers.TryGetValue(m, out var n) ? n + 1 : 1; }
            }
            // D rows: every renderer (any kind, inactive too: it may be enabled at runtime) left without OccludeeStatic stops taking Umbra's dynamic test
            var dyn = new List<Renderer>();
            foreach (var root in Main.GetRootGameObjects()) foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var f = final.TryGetValue(r.gameObject, out var to) ? to : GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                if ((f & Ocd) == 0 && r.allowOcclusionWhenDynamic) dyn.Add(r);
            }
            var sb = new StringBuilder((apply ? "flags-apply: " : "flags (dry): ") + rows.Count + " renderer objects change static flags, " + dyn.Count + " renderers lose dynamic occlusion\n");
            foreach (var g in rows.GroupBy(x => x.rule))
            {
                sb.Append("  ").Append(g.Key).Append(' ').Append(g.Count()).Append(" e.g. ");
                sb.Append(string.Join(" ; ", g.Take(3).Select(x => PathOf(x.go.transform)))).Append('\n');
            }
            foreach (var g in dyn.GroupBy(r => r.GetType().Name + " under " + r.transform.root.name).OrderByDescending(g => g.Count()).Take(12))
                sb.Append("  dynamic-off ").Append(g.Key).Append(' ').Append(g.Count()).Append('\n');
            if (census) foreach (var kv in movers.OrderByDescending(x => x.Value).Take(30)) sb.Append("  kept dynamic by ").Append(kv.Key).Append(' ').Append(kv.Value).Append('\n');
            if (!apply) return sb.ToString();
            string folder = Path.Combine(Backups, DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")); Directory.CreateDirectory(folder);
            var manifest = new StringBuilder();
            foreach (var x in rows)
            {
                manifest.Append("F\t").Append(GlobalObjectId.GetGlobalObjectIdSlow(x.go)).Append('\t').Append((int)x.before).Append('\t').Append((int)x.after).Append('\t').Append(x.rule).Append('\t').Append(PathOf(x.go.transform)).Append('\n');
                GameObjectUtility.SetStaticEditorFlags(x.go, x.after);
            }
            foreach (var r in dyn)
            {
                manifest.Append("D\t").Append(GlobalObjectId.GetGlobalObjectIdSlow(r)).Append("\t1\t0\tdynamic-off\t").Append(PathOf(r.transform)).Append('\n');
                Undo.RecordObject(r, "Occlusion307 dynamic-off"); r.allowOcclusionWhenDynamic = false; EditorUtility.SetDirty(r);
            }
            File.WriteAllText(Path.Combine(folder, "flags.tsv"), manifest.ToString(), new UTF8Encoding(false));
            EditorSceneManager.MarkSceneDirty(Main);
            return sb.Append("manifest ").Append(Path.Combine(folder, "flags.tsv")).Append(" (scene marked dirty, not saved)").ToString();
        }

        static string FlagsRevert()
        {
            if (!Directory.Exists(Backups)) return "no manifests";
            var latest = Directory.GetDirectories(Backups).Where(d => File.Exists(Path.Combine(d, "flags.tsv"))).OrderBy(d => d).LastOrDefault();
            if (latest == null) return "no manifests";
            int done = 0, missing = 0, changed = 0;
            foreach (var line in File.ReadAllLines(Path.Combine(latest, "flags.tsv")))
            {
                var c = line.Split('\t');
                if (c.Length >= 4 && c[0] == "D")
                {
                    if (!GlobalObjectId.TryParse(c[1], out var rid) || !(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(rid) is Renderer rr)) { missing++; continue; }
                    if (rr.allowOcclusionWhenDynamic != (c[3] == "1")) changed++;
                    Undo.RecordObject(rr, "Occlusion307 revert"); rr.allowOcclusionWhenDynamic = c[2] == "1"; EditorUtility.SetDirty(rr); done++; continue;
                }
                if (c.Length >= 4 && c[0] == "F") c = c.Skip(1).ToArray();
                if (c.Length < 3 || !GlobalObjectId.TryParse(c[0], out var id)) continue;
                var go = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject; if (go == null) { missing++; continue; }
                if ((int)GameObjectUtility.GetStaticEditorFlags(go) != int.Parse(c[2], Inv)) changed++;
                GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)int.Parse(c[1], Inv)); done++;
            }
            EditorSceneManager.MarkSceneDirty(Main);
            return "flags-revert from " + latest + ": " + done + " restored, " + missing + " missing, " + changed + " had changed since apply (scene marked dirty, not saved)";
        }
    }
}
