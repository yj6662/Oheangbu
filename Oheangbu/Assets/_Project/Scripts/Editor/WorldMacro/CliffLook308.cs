using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 P0 look gate (SPEC-WORLD-CLIFF-BOUNDARY-308, stage row "P0"): an in-memory preview of two phase-1 cliff stretches
    // inside the open W_Demo_Main world. Nothing is saved: every object carries HideFlags.DontSave, no asset is created, the
    // scene is never saved, a protected tree gets at most renderer.enabled = false (restored by preview:off) and no
    // MeshFilter / MeshCollider reference is ever changed. The patches come from look/tools/look2_patch.py (stage-1a
    // prototype height field on the 4 m lattice); the preview keeps no state in statics, only in its own scene objects.
    // Queue: Oheangbu.EditorTools.WorldMacro.CliffLook308 Run "<command>"
    //   preview:on:<bare|skin>[:stretch=S1,M1][:allowdirty]  build the preview and leave it up (PC quality level)
    //   preview:off                                         remove everything, restore renderers and the quality level
    //   status                                              objects up, hidden renderers, quality, scene dirty, face heights measured on the built mesh
    //   ground:<x>,<z>[;<x>,<z>...]                          physics ground (terrain tile colliders) under points
    //   open-main                                           open W_Demo_Main when the editor is clean (refuses otherwise)
    public static class CliffLook308
    {
        const string RootName = "CliffLook308_Preview";
        const string PatchDir = "Art/World/Compact/Rebuild/CliffBoundary308/look/patch";
        const string TerrainRoot = "Reworld292_Terrain";
        const HideFlags Flags = HideFlags.DontSave;

        [Serializable] sealed class Module { public string mesh; public float z0, w, y0, dLo, dHi; public int gn, gm; public float[] bot, top, poly, grid; }
        [Serializable] sealed class Skin { public int m; public float s0, s1; public bool mirror; }
        [Serializable] sealed class Patch
        {
            public string id, segment, nameKo, bin, binSha256, prototypeSha256, tile, skinMaterialFrom, skinMaterial;
            public float cell, skirt, stationStep, lineLength, standoff, relief;
            public int i0, j0, rows, cols, maskCells;
            public bool closed, flipWinding;
            public float[] sx, sz, nx, nz, oFoot, oRim, yFoot, yTop, roads;
            public Module[] modules;
            public Skin[] skins;
            [NonSerialized] public float[] New, Base;
            [NonSerialized] public byte[] Mask;
        }

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            try
            {
                switch (a[0])
                {
                    case "preview":
                        if (a.Length > 1 && a[1] == "on") return On(a);
                        if (a.Length > 1 && a[1] == "off") return Off();
                        throw new PostLedger308.Refused("use preview:on:<bare|skin>[:stretch=S1,M1] or preview:off");
                    case "status": return Status();
                    case "ground": return Ground(a.Length > 1 ? a[1] : "");
                    case "open-main":
                        if (FindRoots().Count > 0) throw new PostLedger308.Refused("a preview is up; run preview:off first");
                        PostLedger308.RequireEditable(); PostLedger308.Open(PostLedger308.Main);
                        return "open: " + SceneManager.GetActiveScene().path;
                    default: throw new PostLedger308.Refused("unknown command '" + command + "'");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
        }

        // ---------------------------------------------------------------- preview on / off

        static string On(string[] a)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            var opt = PostLedger308.Options(a.Skip(2));
            bool skin = opt.ContainsKey("skin");
            if (!skin && !opt.ContainsKey("bare")) throw new PostLedger308.Refused("variant must be bare or skin");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != PostLedger308.Main) throw new PostLedger308.Refused("active scene is '" + scene.path + "'; the preview is built in " + PostLedger308.Main + " (open-main)");
            if (scene.isDirty && !opt.ContainsKey("allowdirty")) throw new PostLedger308.Refused("the scene has unsaved changes that are not from this tool (add :allowdirty to preview anyway)");
            string[] ids = (opt.TryGetValue("stretch", out var st) && st.Length > 0 ? st : "S1,M1").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            var patches = ids.Select(Load).ToArray();   // every file read happens before the first change
            var tiles = scene.GetRootGameObjects().FirstOrDefault(g => g.name == TerrainRoot);
            if (tiles == null) throw new PostLedger308.Refused("no " + TerrainRoot + " root in the scene");

            int prior = QualitySettings.GetQualityLevel();
            bool dirtyAtOn = scene.isDirty;
            var log = new StringBuilder();
            if (FindRoots().Count > 0) { log.AppendLine("replaced an earlier preview: " + Teardown(false, out int carried)); if (carried >= 0) prior = carried; }

            GameObject root = null;
            var hidden = new List<Renderer>();
            try
            {
                root = Make(RootName, null);
                foreach (var p in patches)
                {
                    var tile = tiles.transform.Find(p.tile);
                    var reference = tile != null && tile.Find("LOD0") != null ? tile.Find("LOD0").GetComponent<MeshRenderer>() : null;
                    if (reference == null || reference.sharedMaterial == null) throw new PostLedger308.Refused("real tile " + p.tile + "/LOD0 has no renderer or material");
                    var mesh = BuildTerrain(p, out int quads, out int skirtEdges);
                    var go = Make(p.id + "_Terrain", root.transform);
                    go.layer = reference.gameObject.layer;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    Dress(go.AddComponent<MeshRenderer>(), reference, reference.sharedMaterial);
                    log.AppendLine(p.id + " terrain: " + mesh.vertexCount + " verts, " + quads + " lattice quads, " + skirtEdges + " skirt edges, material " + reference.sharedMaterial.name
                        + " (" + reference.sharedMaterial.shader.name + ") copied from " + p.tile + "/LOD0, layer " + go.layer);

                    // hide a real tile / forest-shell renderer only where the patch covers it completely
                    foreach (var owner in scene.GetRootGameObjects().Where(g => g.name == TerrainRoot || g.name == "Enclosure305"))
                        foreach (var r in owner.GetComponentsInChildren<Renderer>(false))
                        {
                            if (!r.enabled || !Covers(p, r.bounds)) continue;
                            Make("Hidden|" + PathOf(r.transform), root.transform);
                            r.enabled = false; hidden.Add(r);
                        }

                    if (skin)
                    {
                        var material = SkinMaterial(scene, p, out MeshRenderer cliff, out string source);
                        var skinMesh = BuildSkin(p, out float facing, out bool reflipped);
                        var sg = Make(p.id + "_Skin", root.transform);
                        sg.layer = cliff != null ? cliff.gameObject.layer : go.layer;
                        sg.AddComponent<MeshFilter>().sharedMesh = skinMesh;
                        Dress(sg.AddComponent<MeshRenderer>(), cliff != null ? cliff : reference, material);
                        log.AppendLine(p.id + " skin: " + p.skins.Length + " kit panels, " + skinMesh.vertexCount + " verts, " + skinMesh.triangles.Length / 3 + " tris, material " + material.name
                            + " (" + material.shader.name + ") from " + source + ", outward facing " + facing.ToString("F2", CultureInfo.InvariantCulture) + (reflipped ? " (winding re-flipped)" : "") + ", no collider");
                    }
                }
                int pc = Array.IndexOf(QualitySettings.names, "PC");
                if (pc < 0) throw new PostLedger308.Refused("no quality level named PC");
                if (pc != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(pc, true);
                Make("State|q=" + prior + "|variant=" + (skin ? "skin" : "bare") + "|stretch=" + string.Join(",", ids) + "|dirtyAtOn=" + dirtyAtOn + "|utc=" + PostLedger308.Utc(), root.transform);
            }
            catch
            {
                foreach (var r in hidden) if (r != null) r.enabled = true;
                if (QualitySettings.GetQualityLevel() != prior) QualitySettings.SetQualityLevel(prior, true);
                Destroy(root);
                throw;
            }
            log.AppendLine("hidden real renderers: " + hidden.Count + (hidden.Count == 0 ? " (no real tile or shell renderer lies completely inside and under a patch)" : ""));
            log.AppendLine("quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " (prior level " + prior + " " + QualitySettings.names[prior] + ", restored by preview:off)");
            log.Append("preview up: " + (skin ? "skin" : "bare") + " [" + string.Join(",", ids) + "] scene dirty=" + scene.isDirty);
            return log.ToString();
        }

        static string Off()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (FindRoots().Count == 0) return "preview: none to remove | quality " + QualitySettings.GetQualityLevel() + " (" + QualitySettings.names[QualitySettings.GetQualityLevel()] + ") | scene dirty=" + SceneManager.GetActiveScene().isDirty;
            string text = Teardown(true, out _);
            return "preview off: " + text + " | quality " + QualitySettings.GetQualityLevel() + " (" + QualitySettings.names[QualitySettings.GetQualityLevel()] + ") | scene dirty=" + SceneManager.GetActiveScene().isDirty;
        }

        /// <summary>Removes every preview object, re-enables the renderers it disabled and (optionally) restores the recorded quality level.</summary>
        static string Teardown(bool restoreQuality, out int priorQuality)
        {
            priorQuality = -1;
            int restored = 0, unresolved = 0, objects = 0, meshes = 0;
            foreach (var root in FindRoots())
            {
                foreach (Transform c in root.transform)
                {
                    if (c.name.StartsWith("State|", StringComparison.Ordinal)) { int q = StateInt(c.name, "q"); if (q >= 0) priorQuality = q; }
                    else if (c.name.StartsWith("Hidden|", StringComparison.Ordinal))
                    {
                        var r = Resolve(c.name.Substring(7));
                        if (r != null) { r.enabled = true; restored++; } else unresolved++;
                    }
                }
                objects += root.GetComponentsInChildren<Transform>(true).Length;
                foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh != null && !EditorUtility.IsPersistent(f.sharedMesh)) { Object.DestroyImmediate(f.sharedMesh); meshes++; }
                Object.DestroyImmediate(root);
            }
            if (restoreQuality && priorQuality >= 0 && priorQuality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != priorQuality) QualitySettings.SetQualityLevel(priorQuality, true);
            return objects + " objects and " + meshes + " in-memory meshes destroyed, " + restored + " renderers re-enabled" + (unresolved > 0 ? ", " + unresolved + " HIDDEN RENDERERS NOT FOUND" : "")
                + (priorQuality >= 0 ? ", recorded prior quality " + priorQuality : ", no recorded quality");
        }

        static void Destroy(GameObject root)
        {
            if (root == null) return;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null && !EditorUtility.IsPersistent(f.sharedMesh)) Object.DestroyImmediate(f.sharedMesh);
            Object.DestroyImmediate(root);
        }

        // ---------------------------------------------------------------- status

        static string Status()
        {
            var scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            int level = QualitySettings.GetQualityLevel();
            sb.AppendLine("scene " + scene.path + " dirty=" + scene.isDirty + " playing=" + EditorApplication.isPlaying);
            sb.AppendLine("quality level " + level + " (" + QualitySettings.names[level] + ") of [" + string.Join(",", QualitySettings.names) + "]");
            var roots = FindRoots();
            if (roots.Count == 0) { sb.Append("preview: none - 0 objects up, 0 hidden renderers (restored)"); return sb.ToString(); }
            Physics.SyncTransforms();
            foreach (var root in roots)
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                var state = all.FirstOrDefault(t => t.name.StartsWith("State|", StringComparison.Ordinal));
                var hidden = all.Where(t => t.name.StartsWith("Hidden|", StringComparison.Ordinal)).ToArray();
                sb.AppendLine("preview up: " + all.Length + " objects (all " + (all.All(t => (t.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0) ? "DontSave" : "NOT ALL DontSave") + "), colliders "
                    + root.GetComponentsInChildren<Collider>(true).Length + ", " + (state != null ? state.name : "no state record"));
                sb.AppendLine("hidden real renderers: " + hidden.Length);
                foreach (var h in hidden) { var r = Resolve(h.name.Substring(7)); sb.AppendLine("  " + h.name.Substring(7) + " -> " + (r == null ? "NOT FOUND" : "enabled=" + r.enabled)); }
                foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = f.GetComponent<MeshRenderer>(); var m = mr.sharedMaterial; var b = mr.bounds;
                    bool glow = m != null && (m.IsKeywordEnabled("_EMISSION") || (m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0f));
                    sb.AppendLine("  " + f.name + ": " + f.sharedMesh.vertexCount + " verts, " + f.sharedMesh.triangles.Length / 3 + " tris, bounds min " + b.min.ToString("F1") + " max " + b.max.ToString("F1")
                        + ", material " + (m != null ? m.name + " / " + m.shader.name : "null") + ", emission " + (glow ? "ON" : "none") + ", shadows " + mr.shadowCastingMode + ", layer " + f.gameObject.layer);
                }
                foreach (var f in root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name.EndsWith("_Terrain", StringComparison.Ordinal)))
                    sb.Append(Measure(Load(f.name.Substring(0, f.name.Length - 8)), f.sharedMesh));
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Face heights and lip heights taken from the vertices of the BUILT preview mesh; the ground in front of it is the real terrain collider.</summary>
        static string Measure(Patch p, Mesh built)
        {
            var inv = CultureInfo.InvariantCulture;
            var map = new Dictionary<long, float>(built.vertexCount);
            foreach (var q in built.vertices)
            {
                long key = Key(Mathf.RoundToInt(q.x / p.cell), Mathf.RoundToInt(q.z / p.cell));
                if (!map.TryGetValue(key, out float y) || q.y > y) map[key] = q.y;   // skirt vertices sit below their lattice vertex
            }
            int n = p.sx.Length, onMesh = 0, onGround = 0, missing = 0;
            var strict = new List<float>(n); var plan = new List<float>(n); var rim = new float[n];
            var h = new float[21];   // offsets -12 .. +8 m along the normal toward the upper side, 1 m apart
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 21; k++)
                {
                    float o = k - 12, x = p.sx[i] + p.nx[i] * o, z = p.sz[i] + p.nz[i] * o;
                    int src = Surface(map, p.cell, x, z, out h[k]);
                    if (src == 0) onMesh++; else if (src == 1) onGround++; else missing++;
                }
                // AC definition (Spec section 2): the largest rise inside any 8 m window across the line, samples within 8 m of it
                float best = float.NaN;
                for (int lo = 4; lo < 21; lo++)
                    for (int hi = lo + 1; hi < 21 && hi <= lo + 8; hi++)
                    {
                        if (float.IsNaN(h[lo]) || float.IsNaN(h[hi])) continue;
                        float rise = h[hi] - h[lo];
                        if (float.IsNaN(best) || rise > best) best = rise;
                    }
                if (!float.IsNaN(best)) strict.Add(best);
                // plan window (plan/tools/step2.py): max of the upper side 0..8 m minus min of the lower side 4..12 m
                float up = float.NegativeInfinity, low = float.PositiveInfinity;
                for (int k = 12; k <= 20; k += 2) if (!float.IsNaN(h[k])) up = Mathf.Max(up, h[k]);
                for (int k = 0; k <= 8; k += 2) if (!float.IsNaN(h[k])) low = Mathf.Min(low, h[k]);
                if (!float.IsInfinity(up) && !float.IsInfinity(low)) plan.Add(up - low);
                rim[i] = float.NegativeInfinity;
                for (int k = 12; k <= 20; k++) if (!float.IsNaN(h[k])) rim[i] = Mathf.Max(rim[i], h[k]);
            }
            var sb = new StringBuilder();
            sb.AppendLine("measured on built mesh " + p.id + " (" + p.segment + " " + p.nameKo + "): " + n + " stations every " + p.stationStep.ToString("F2", inv) + " m, line " + p.lineLength.ToString("F0", inv)
                + " m; samples on preview mesh " + onMesh + ", on real terrain collider " + onGround + ", no surface " + missing);
            sb.AppendLine("  face height, AC definition (max rise in an 8 m window): " + Five(strict));
            sb.AppendLine("  face height, plan window (upper 0..8 m max - lower 4..12 m min): " + Five(plan));

            // lip above the road: rim of each station against the physics ground at its nearest EA road point
            int roads = p.roads.Length / 2; var roadY = new float[roads]; var have = new bool[roads];
            var lips = new List<float>(); float nearD = float.MaxValue, nearLip = float.NaN; int nearRoad = -1, nearStation = -1;
            for (int i = 0; i < n; i++)
            {
                if (float.IsInfinity(rim[i])) continue;
                int at = -1; float d2 = float.MaxValue;
                for (int r = 0; r < roads; r++) { float dx = p.roads[r * 2] - p.sx[i], dz = p.roads[r * 2 + 1] - p.sz[i], d = dx * dx + dz * dz; if (d < d2) { d2 = d; at = r; } }
                if (at < 0) continue;
                if (!have[at]) { roadY[at] = TerrainY(p.roads[at * 2] + .02f, p.roads[at * 2 + 1] + .02f, out float gy) ? gy : float.NaN; have[at] = true; }
                if (float.IsNaN(roadY[at])) continue;
                float dist = Mathf.Sqrt(d2), lip = rim[i] - roadY[at];
                if (dist <= 400f) lips.Add(lip);
                if (dist < nearD) { nearD = dist; nearLip = lip; nearRoad = at; nearStation = i; }
            }
            if (nearRoad >= 0)
                sb.AppendLine("  lip above the nearest EA road point: +" + nearLip.ToString("F1", inv) + " m (road (" + p.roads[nearRoad * 2].ToString("F0", inv) + "," + p.roads[nearRoad * 2 + 1].ToString("F0", inv) + ") ground y "
                    + roadY[nearRoad].ToString("F1", inv) + ", rim y " + rim[nearStation].ToString("F1", inv) + " at station " + nearStation + " (" + p.sx[nearStation].ToString("F0", inv) + "," + p.sz[nearStation].ToString("F0", inv)
                    + "), " + nearD.ToString("F0", inv) + " m apart)");
            sb.AppendLine("  lip above each station's nearest road point (stations with a road within 400 m): " + Five(lips));

            // seam: unchanged lattice vertices of the built mesh against the real terrain collider
            var seam = new List<float>(); int step = Mathf.Max(1, built.vertexCount / 4000), index = 0;
            for (int r = 1; r < p.rows - 1; r++)
                for (int c = 1; c < p.cols - 1; c++)
                {
                    int k = r * p.cols + c;
                    if (p.Mask[k] != 0 || !map.TryGetValue(Key(p.j0 + c, p.i0 + r), out float y)) continue;
                    if (index++ % step != 0) continue;
                    if (TerrainY((p.j0 + c) * p.cell + .02f, (p.i0 + r) * p.cell + .02f, out float gy)) seam.Add(Mathf.Abs(y - gy));
                }
            seam.Sort();
            sb.AppendLine("  seam with the real ground (|preview - terrain collider| at " + seam.Count + " unraised lattice vertices of the mesh): " + (seam.Count == 0 ? "no samples"
                : "median " + seam[seam.Count / 2].ToString("F3", inv) + " p95 " + seam[Mathf.Min(seam.Count - 1, (int)(seam.Count * .95f))].ToString("F3", inv) + " max " + seam[seam.Count - 1].ToString("F3", inv) + " m"));
            return sb.ToString();
        }

        static string Five(List<float> values)
        {
            if (values.Count == 0) return "no samples";
            var v = values.OrderBy(x => x).ToArray(); var inv = CultureInfo.InvariantCulture;
            float At(float f) => v[Mathf.Clamp(Mathf.RoundToInt(f * (v.Length - 1)), 0, v.Length - 1)];
            return "min " + v[0].ToString("F1", inv) + " / p10 " + At(.1f).ToString("F1", inv) + " / median " + At(.5f).ToString("F1", inv) + " / p90 " + At(.9f).ToString("F1", inv) + " / max " + v[v.Length - 1].ToString("F1", inv) + " m (n=" + v.Length + ")";
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>Height of the visible surface: the preview mesh where its lattice quad exists (0), else the real terrain collider (1), else none (2).</summary>
        static int Surface(Dictionary<long, float> map, float cell, float x, float z, out float y)
        {
            int ix = Mathf.FloorToInt(x / cell), iz = Mathf.FloorToInt(z / cell);
            float fx = x / cell - ix, fz = z / cell - iz;
            if (map.TryGetValue(Key(ix, iz), out float h00) && map.TryGetValue(Key(ix + 1, iz), out float h10) && map.TryGetValue(Key(ix, iz + 1), out float h01) && map.TryGetValue(Key(ix + 1, iz + 1), out float h11))
            {
                // same split as the tile meshes: triangles (00,01,10) and (10,01,11)
                y = fx + fz <= 1f ? h00 + (h10 - h00) * fx + (h01 - h00) * fz : h11 + (h01 - h11) * (1f - fx) + (h10 - h11) * (1f - fz);
                return 0;
            }
            if (TerrainY(x, z, out y)) return 1;
            y = float.NaN; return 2;
        }

        static bool TerrainY(float x, float z, out float y)
        {
            y = float.NegativeInfinity; bool any = false;
            foreach (var hit in Physics.RaycastAll(new Vector3(x, 3000f, z), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore))
            {
                var t = hit.collider.transform;
                if (t.parent == null || t.parent.name != TerrainRoot || hit.point.y <= y) continue;
                y = hit.point.y; any = true;
            }
            return any;
        }

        static string Ground(string argument)
        {
            Physics.SyncTransforms();
            var inv = CultureInfo.InvariantCulture; var sb = new StringBuilder();
            foreach (string pair in argument.Split(';').Where(s => s.Trim().Length > 0))
            {
                var v = pair.Split(',').Select(t => float.Parse(t, inv)).ToArray();
                bool ok = TerrainY(v[0], v[1], out float y);
                string top = "-"; float topY = float.NegativeInfinity;
                foreach (var hit in Physics.RaycastAll(new Vector3(v[0], 3000f, v[1]), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.point.y > topY) { topY = hit.point.y; top = PathOf(hit.collider.transform); }
                sb.AppendLine(v[0].ToString("F1", inv) + "," + v[1].ToString("F1", inv) + " terrain=" + (ok ? y.ToString("F2", inv) : "none") + " top=" + (float.IsInfinity(topY) ? "none" : topY.ToString("F2", inv)) + " " + top);
            }
            return sb.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- build

        static Patch Load(string id)
        {
            string dir = PostLedger308.RepoPath(PatchDir), json = Path.Combine(dir, id + ".json");
            if (!File.Exists(json)) throw new PostLedger308.Refused("no patch " + json + " (run look/tools/look2_patch.py)");
            var p = JsonUtility.FromJson<Patch>(File.ReadAllText(json, Encoding.UTF8));
            var bytes = File.ReadAllBytes(Path.Combine(dir, p.bin));
            int n = p.rows * p.cols;
            if (n <= 0 || bytes.Length != n * 9) throw new PostLedger308.Refused("patch " + p.bin + " size " + bytes.Length + " != " + n * 9);
            using (var sha = System.Security.Cryptography.SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != p.binSha256) throw new PostLedger308.Refused("patch " + p.bin + " does not match its recorded sha256");
            p.New = new float[n]; p.Base = new float[n]; p.Mask = new byte[n];
            Buffer.BlockCopy(bytes, 0, p.New, 0, n * 4); Buffer.BlockCopy(bytes, n * 4, p.Base, 0, n * 4); Buffer.BlockCopy(bytes, n * 8, p.Mask, 0, n);
            if (p.sx == null || p.sx.Length < 2 || p.nx.Length != p.sx.Length) throw new PostLedger308.Refused("patch " + id + " has no op-line stations");
            return p;
        }

        /// <summary>Lattice quads that touch a raised vertex, on the tile lattice and with the tile triangle order, plus a skirt along the open border.</summary>
        static Mesh BuildTerrain(Patch p, out int quads, out int skirtEdges)
        {
            int rows = p.rows, cols = p.cols;
            var include = new bool[(rows - 1) * (cols - 1)];
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < cols - 1; c++)
                    include[r * (cols - 1) + c] = (p.Mask[r * cols + c] | p.Mask[r * cols + c + 1] | p.Mask[(r + 1) * cols + c] | p.Mask[(r + 1) * cols + c + 1]) != 0;
            var index = new int[rows * cols];
            for (int i = 0; i < index.Length; i++) index[i] = -1;
            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            int Vertex(int r, int c)
            {
                int k = r * cols + c;
                if (index[k] >= 0) return index[k];
                float wx = (p.j0 + c) * p.cell, wz = (p.i0 + r) * p.cell;
                // CompactWorldSurface.Normal on the edited field
                float xl = p.New[r * cols + Mathf.Max(c - 1, 0)], xr = p.New[r * cols + Mathf.Min(c + 1, cols - 1)], zd = p.New[Mathf.Max(r - 1, 0) * cols + c], zu = p.New[Mathf.Min(r + 1, rows - 1) * cols + c];
                index[k] = verts.Count; verts.Add(new Vector3(wx, p.New[k], wz)); normals.Add(new Vector3(xl - xr, 2f * p.cell, zd - zu).normalized); uv.Add(new Vector2(wx / 4f, wz / 4f));
                return index[k];
            }
            bool Included(int r, int c) => r >= 0 && c >= 0 && r < rows - 1 && c < cols - 1 && include[r * (cols - 1) + c];
            quads = 0;
            var border = new List<(int a, int b)>();
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < cols - 1; c++)
                {
                    if (!include[r * (cols - 1) + c]) continue;
                    int v00 = Vertex(r, c), v01 = Vertex(r + 1, c), v10 = Vertex(r, c + 1), v11 = Vertex(r + 1, c + 1);
                    tris.Add(v00); tris.Add(v01); tris.Add(v10); tris.Add(v10); tris.Add(v01); tris.Add(v11);
                    quads++;
                    if (!Included(r - 1, c)) border.Add((v00, v10));
                    if (!Included(r + 1, c)) border.Add((v01, v11));
                    if (!Included(r, c - 1)) border.Add((v00, v01));
                    if (!Included(r, c + 1)) border.Add((v10, v11));
                }
            skirtEdges = border.Count;
            foreach (var (a, b) in border)
            {
                int la = verts.Count; verts.Add(verts[a] + Vector3.down * p.skirt); normals.Add(normals[a]); uv.Add(uv[a]);
                int lb = verts.Count; verts.Add(verts[b] + Vector3.down * p.skirt); normals.Add(normals[b]); uv.Add(uv[b]);
                tris.Add(a); tris.Add(b); tris.Add(la); tris.Add(b); tris.Add(lb); tris.Add(la);   // both windings: the skirt is seen from either side
                tris.Add(a); tris.Add(la); tris.Add(b); tris.Add(b); tris.Add(la); tris.Add(lb);
            }
            var mesh = new Mesh { name = RootName + "_" + p.id + "_Terrain", hideFlags = Flags, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Existing granite kit panels (MountainIntegration290 ArtJoin faces) laid along the op line: each source vertex keeps its
        /// place in the panel (along, up, depth) and is re-seated on the wall between the recorded foot and rim guides. Read-only on the kit.</summary>
        static Mesh BuildSkin(Patch p, out float facing, out bool reflipped)
        {
            var verts = new List<Vector3>(); var outward = new List<Vector3>(); var tris = new List<int>();
            var sources = new Dictionary<int, (Vector3[] v, int[] t)>();
            int n = p.sx.Length;
            float step = p.lineLength / (n - 1);
            foreach (var s in p.skins)
            {
                var mod = p.modules[s.m];
                if (!sources.TryGetValue(s.m, out var src))
                {
                    var kit = AssetDatabase.LoadAssetAtPath<Mesh>(mod.mesh);
                    if (kit == null || !kit.isReadable) throw new PostLedger308.Refused("kit mesh missing or unreadable: " + mod.mesh);
                    src = (kit.vertices, kit.triangles); sources[s.m] = src;
                }
                int offset = verts.Count, bins = mod.bot.Length; var c = mod.poly;
                bool coarse = mod.grid != null && mod.gn > 1 && mod.gm > 1 && mod.grid.Length == mod.gn * mod.gm;
                foreach (var q in src.v)
                {
                    float u = Mathf.Clamp01((q.z - mod.z0) / mod.w);
                    float fb = u * (bins - 1); int b0 = Mathf.Min((int)fb, bins - 2); float bt = fb - b0;
                    float bot = Mathf.Lerp(mod.bot[b0], mod.bot[b0 + 1], bt), top = Mathf.Lerp(mod.top[b0], mod.top[b0 + 1], bt);
                    float v = Mathf.Clamp01((q.y - mod.y0 - bot) / Mathf.Max(top - bot, .001f));
                    float fit = c[0] + c[1] * u + c[2] * v + c[3] * u * u + c[4] * u * v + c[5] * v * v + c[6] * u * u * u + c[7] * u * u * v + c[8] * u * v * v + c[9] * v * v * v;
                    if (coarse)
                    {
                        // the slab's own bulges (coarse bilinear grid over the panel) go with the fit; only block and joint relief stays
                        float gu = u * (mod.gn - 1), gv = v * (mod.gm - 1); int ga = Mathf.Min((int)gu, mod.gn - 2), gb = Mathf.Min((int)gv, mod.gm - 2); float tu = gu - ga, tv = gv - gb;
                        fit += Mathf.Lerp(Mathf.Lerp(mod.grid[gb * mod.gn + ga], mod.grid[gb * mod.gn + ga + 1], tu), Mathf.Lerp(mod.grid[(gb + 1) * mod.gn + ga], mod.grid[(gb + 1) * mod.gn + ga + 1], tu), tv);
                    }
                    float depth = Mathf.Clamp01((q.x - fit - mod.dLo) / Mathf.Max(mod.dHi - mod.dLo, .001f));   // 0 = proudest block, 1 = deepest joint (kit +x points into the rock)
                    float arc = Mathf.Lerp(s.s0, s.s1, s.mirror ? 1f - u : u);
                    if (p.closed) arc = Mathf.Repeat(arc, p.lineLength);   // a ring: the first and last station are the same point
                    float fi = Mathf.Clamp(arc / step, 0f, n - 1.001f); int i0 = (int)fi, i1 = i0 + 1; float t = fi - i0;
                    var normal = new Vector2(Mathf.Lerp(p.nx[i0], p.nx[i1], t), Mathf.Lerp(p.nz[i0], p.nz[i1], t)).normalized;   // toward the upper side
                    float o = Mathf.Lerp(Mathf.Lerp(p.oFoot[i0], p.oFoot[i1], t), Mathf.Lerp(p.oRim[i0], p.oRim[i1], t), v) + p.standoff - depth * p.relief;
                    verts.Add(new Vector3(Mathf.Lerp(p.sx[i0], p.sx[i1], t) - normal.x * o, Mathf.Lerp(Mathf.Lerp(p.yFoot[i0], p.yFoot[i1], t), Mathf.Lerp(p.yTop[i0], p.yTop[i1], t), v), Mathf.Lerp(p.sz[i0], p.sz[i1], t) - normal.y * o));
                    outward.Add(new Vector3(-normal.x, 0f, -normal.y));
                }
                bool flip = p.flipWinding ^ s.mirror;
                for (int k = 0; k < src.t.Length; k += 3) { tris.Add(offset + src.t[k]); tris.Add(offset + src.t[k + (flip ? 2 : 1)]); tris.Add(offset + src.t[k + (flip ? 1 : 2)]); }
            }
            var mesh = new Mesh { name = RootName + "_" + p.id + "_Skin", hideFlags = Flags, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals();
            facing = Facing(mesh, outward); reflipped = false;
            if (facing < 0f)
            {
                for (int k = 0; k < tris.Count; k += 3) { int swap = tris[k + 1]; tris[k + 1] = tris[k + 2]; tris[k + 2] = swap; }
                mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); facing = Facing(mesh, outward); reflipped = true;
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        static float Facing(Mesh mesh, List<Vector3> outward)
        {
            var normals = mesh.normals; double sum = 0;
            for (int i = 0; i < normals.Length; i++) sum += Vector3.Dot(normals[i], outward[i]);
            return normals.Length > 0 ? (float)(sum / normals.Length) : 0f;
        }

        /// <summary>The granite material already on a cliff piece in the scene (Finish297_CliffPath); the asset path of the patch is the fallback.</summary>
        static Material SkinMaterial(Scene scene, Patch p, out MeshRenderer cliff, out string source)
        {
            cliff = null;
            var owner = scene.GetRootGameObjects().FirstOrDefault(g => g.name == p.skinMaterialFrom);
            if (owner != null)
                cliff = owner.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.shader != null && (r.sharedMaterial.name.IndexOf("Granite", StringComparison.OrdinalIgnoreCase) >= 0 || r.sharedMaterial.shader.name.IndexOf("Granite", StringComparison.OrdinalIgnoreCase) >= 0));
            if (cliff != null) { source = PathOf(cliff.transform); return cliff.sharedMaterial; }
            var material = AssetDatabase.LoadAssetAtPath<Material>(p.skinMaterial);
            if (material == null) throw new PostLedger308.Refused("no granite material on " + p.skinMaterialFrom + " and none at " + p.skinMaterial);
            source = p.skinMaterial; return material;
        }

        static void Dress(MeshRenderer target, MeshRenderer reference, Material material)
        {
            target.sharedMaterial = material;
            target.shadowCastingMode = reference.shadowCastingMode; target.receiveShadows = reference.receiveShadows;
            target.renderingLayerMask = reference.renderingLayerMask; target.lightProbeUsage = reference.lightProbeUsage; target.reflectionProbeUsage = reference.reflectionProbeUsage;
            target.allowOcclusionWhenDynamic = false;
        }

        /// <summary>True when every lattice vertex under the bounds is raised and the object ends below the lowest raised height there.</summary>
        static bool Covers(Patch p, Bounds b)
        {
            int c0 = Mathf.FloorToInt(b.min.x / p.cell) - p.j0, c1 = Mathf.CeilToInt(b.max.x / p.cell) - p.j0, r0 = Mathf.FloorToInt(b.min.z / p.cell) - p.i0, r1 = Mathf.CeilToInt(b.max.z / p.cell) - p.i0;
            if (c0 < 0 || r0 < 0 || c1 >= p.cols || r1 >= p.rows) return false;
            float low = float.PositiveInfinity;
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    if (p.Mask[r * p.cols + c] == 0) return false;
                    low = Mathf.Min(low, p.New[r * p.cols + c]);
                }
            return b.max.y <= low;
        }

        // ---------------------------------------------------------------- scene helpers

        static GameObject Make(string name, Transform parent)
        {
            var go = new GameObject(name) { hideFlags = Flags };
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        static List<GameObject> FindRoots() =>
            Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && g.name == RootName && !EditorUtility.IsPersistent(g) && g.transform.parent == null).ToList();

        static int StateInt(string state, string key)
        {
            foreach (string part in state.Split('|'))
                if (part.StartsWith(key + "=", StringComparison.Ordinal) && int.TryParse(part.Substring(key.Length + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
            return -1;
        }

        static string PathOf(Transform t)
        {
            var names = new List<string>();
            for (var p = t; p != null; p = p.parent) names.Add(p.name);
            names.Reverse(); return string.Join("/", names);
        }

        static Renderer Resolve(string path)
        {
            int cut = path.IndexOf('/');
            string rootName = cut < 0 ? path : path.Substring(0, cut);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name == rootName))
            {
                var t = cut < 0 ? root.transform : root.transform.Find(path.Substring(cut + 1));
                var r = t != null ? t.GetComponent<Renderer>() : null;
                if (r != null) return r;
            }
            return null;
        }
    }
}
