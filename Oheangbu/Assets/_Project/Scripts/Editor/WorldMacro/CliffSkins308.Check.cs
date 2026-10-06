using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // CliffSkins308: status, verify (read-only measurement on the REAL terrain colliders) and the in-memory look preview.
    public static partial class CliffSkins308
    {
        const string WallPreviewRoot = "WallLook308_Preview";   // order rule of the look gate: cliff on -> wall on -> wall off -> cliff off
        const string ToolTag = "tool=CliffSkins308";
        const HideFlags PreviewFlags = HideFlags.DontSave;

        // ---------------------------------------------------------------- status

        static string Status(CliffCore308.Config cfg)
        {
            var sb = new StringBuilder(); var scene = SceneManager.GetActiveScene(); int level = QualitySettings.GetQualityLevel();
            sb.AppendLine("scene " + scene.path + " dirty=" + scene.isDirty + " playing=" + EditorApplication.isPlaying + " | quality " + level + " (" + QualitySettings.names[level] + ")");
            string dir = PostLedger308.RepoPath(SkinsDir);
            var stages = Directory.Exists(dir) ? Directory.GetDirectories(dir).Select(Path.GetFileName).Where(s => File.Exists(ManifestFile(s))).OrderBy(s => s, StringComparer.Ordinal).ToArray() : new string[0];
            string def = "-"; try { def = DefaultImportStage(); } catch (PostLedger308.Refused r) { def = "(" + r.Message + ")"; }
            sb.AppendLine("skin sets under " + SkinsDir + ": " + (stages.Length == 0 ? "none (python Tools/Art/cliffskin308.py --stage <s>)" : string.Join(", ", stages)) + " | import default stage " + def);
            foreach (string stage in stages)
            {
                try
                {
                    var m = Load(cfg, stage); int files = 0, missing = 0;
                    foreach (var p in m.panels) foreach (var l in p.lods) { if (File.Exists(MeshFile(m, l))) files++; else missing++; }
                    sb.AppendLine("  stage " + stage + ": manifest " + Short(m.Sha256) + ", height " + Short(m.heightSha256) + ", " + m.panels.Length + " panels, LOD0 " + m.totals.lod0Triangles + " / LOD1 " + m.totals.lod1Triangles + " / LOD2 " + m.totals.lod2Triangles
                        + " triangles, mesh memory " + F(m.totals.meshMemoryBytes / 1048576f) + " MB (budget " + F(m.budget.meshMemoryBytesMax / 1048576f) + "), mesh files " + files + (missing > 0 ? " (MISSING " + missing + ")" : ""));
                    var imp = ReadImport(cfg, stage); int present = imp.entries.Count(e => File.Exists(PostLedger308.Abs(e.asset)));
                    sb.AppendLine("    import: " + (imp.entries.Length == 0 ? "not imported" : imp.entries.Length + " entries, " + present + " assets on disk, " + (imp.manifestSha256 == m.Sha256 ? "current" : "STALE (manifest " + Short(imp.manifestSha256) + ")") + ", " + imp.utc)
                        + " | material " + m.MaterialPath + (File.Exists(PostLedger308.Abs(m.MaterialPath)) ? " present" : " absent"));
                }
                catch (PostLedger308.Refused r) { sb.AppendLine("  stage " + stage + ": " + r.Message); }
            }
            foreach (var s in cfg.scenes)
            {
                var l = ReadLedger(cfg, s.path); var t = CliffBoundary308.ReadLedger(cfg, s.path);
                sb.AppendLine("ledger " + s.key + ": skins " + l.state + (l.stage != "" ? " stage " + l.stage : "") + (l.utc != "" ? " " + l.utc : "") + (l.state == "applied" ? " (" + l.panels + " panels, " + l.counts.triangles + " LOD0 triangles, colliders " + l.counts.colliders
                    + ", shadow casters " + l.counts.shadowCasters + ")" : "") + " | terrain " + (t == null ? "none" : t.state + " " + t.stage + " height " + Short(t.heightSha256)));
            }
            if (PostLedger308.Scenes.Contains(scene.path) && stages.Length > 0)
            {
                // root / group names are the same in every manifest of this folder; the first one is used to look
                try
                {
                    var m = Load(cfg, stages[0]); var roots = Roots(scene, m.root);
                    var skins = roots.Length == 1 ? roots[0].transform.Find(m.group) : null;
                    sb.AppendLine("active scene: root " + m.root + " x" + roots.Length + (roots.Length == 1 ? " (children: " + string.Join(", ", roots[0].transform.Cast<Transform>().Select(c => c.name)) + ")" : "")
                        + " | " + m.group + ": " + (skins == null ? "absent" : skins.GetComponentsInChildren<LODGroup>(true).Length + " panels, " + skins.GetComponentsInChildren<MeshRenderer>(true).Length + " mesh renderers, colliders "
                        + skins.GetComponentsInChildren<Collider>(true).Length));
                }
                catch (PostLedger308.Refused r) { sb.AppendLine("active scene: " + r.Message); }
            }
            var previews = FindRoots(null);
            sb.Append("preview: " + (previews.Count == 0 ? "none" : string.Join(" | ", previews.Select(p => p.name + " " + (StateOf(p) ?? "(no state record)") + ", objects " + p.GetComponentsInChildren<Transform>(true).Length
                + ", colliders " + p.GetComponentsInChildren<Collider>(true).Length))));
            return sb.ToString();
        }

        // ---------------------------------------------------------------- verify

        [Serializable] sealed class StretchFront { public string stretch = ""; public int probes, noWall, pierced, over; public float p50, p95, max, offlineDiffMax; }
        [Serializable] sealed class VerifyResult
        {
            public string format = "cb308.skins.verify.1", scene = "", key = "", stage = "", utc = "", manifestSha256 = "", ledgerState = "";
            public bool ok; public int panels, lodGroups, meshRenderers, colliders, shadowCasters, emissiveMaterials, materials, lod0Triangles, lod0TrianglesBudget, renderersBudget;
            public long meshMemoryBytes, meshMemoryBudget, meshMemoryManifest;
            public int probes, probesNoWall, probesPierced, probesOverFront; public float frontP50, frontP95, frontMax, frontRule, offlineDiffMax;
            public int tops, topsBelow; public float topMarginMin, topRule, topTerrainDiffMax;
            public StretchFront[] stretches = new StretchFront[0]; public string[] failures = new string[0], listed = new string[0];
        }

        static float Quantile(List<float> sorted, float q) => sorted.Count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];

        /// <summary>Nearest hit of a ray on the terrain tile colliders only (other colliders are ignored); -1 when none.</summary>
        static float TerrainHit(Vector3 origin, Vector3 direction, float length, Transform terrainRoot, out Vector3 point)
        {
            float best = -1f; point = Vector3.zero;
            foreach (var hit in Physics.RaycastAll(origin, direction, length, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(terrainRoot)) continue;
                if (best < 0f || hit.distance < best) { best = hit.distance; point = hit.point; }
            }
            return best;
        }

        static string Verify(CliffCore308.Config cfg, string token, Dictionary<string, string> opt)
        {
            string path = CliffCore308.ScenePath(cfg, token);
            PostLedger308.RequireEditable(); RequireNoPreview();
            string stage = SceneStage(cfg, path, opt, out var terrain);
            var m = Load(cfg, stage);
            var scene = PostLedger308.Open(path);
            var ledger = ReadLedger(cfg, path); string key = CliffCore308.SceneKey(cfg, path);
            var res = new VerifyResult { scene = path, key = key, stage = stage, utc = PostLedger308.Utc(), manifestSha256 = m.Sha256, ledgerState = ledger.state, frontRule = m.maxFront, topRule = m.overM,
                lod0TrianglesBudget = m.budget.lod0TrisMax, renderersBudget = m.budget.renderersMax, meshMemoryBudget = m.budget.meshMemoryBytesMax, meshMemoryManifest = m.totals.meshMemoryBytes };
            var fail = new List<string>(); var listed = new List<string>();
            string mismatch = TerrainMismatch(terrain, m); if (mismatch != null) fail.Add(mismatch);
            var roots = Roots(scene, m.root);
            var skins = roots.Length == 1 ? roots[0].transform.Find(m.group) : null;
            if (skins == null) fail.Add("no " + m.root + "/" + m.group + " in the scene (" + roots.Length + " roots named " + m.root + ")");
            var terrainRoot = CliffCore308.TerrainRoot(scene, cfg);

            if (skins != null)
            {
                // structure, counts, material, memory
                var groups = skins.GetComponentsInChildren<LODGroup>(true); var renderers = skins.GetComponentsInChildren<MeshRenderer>(true);
                res.panels = m.panels.Length; res.lodGroups = groups.Length; res.meshRenderers = renderers.Length;
                res.colliders = skins.GetComponentsInChildren<Collider>(true).Length; res.shadowCasters = renderers.Count(r => r.shadowCastingMode != ShadowCastingMode.Off);
                var mats = renderers.SelectMany(r => r.sharedMaterials).Distinct().ToArray(); res.materials = mats.Length; res.emissiveMaterials = mats.Count(x => x == null || Glows(x));
                var byName = new Dictionary<string, LODGroup>(); foreach (var g in groups) byName[g.name] = g;
                var unique = new HashSet<Mesh>(); int wrong = 0, lights = skins.GetComponentsInChildren<Light>(true).Length;
                foreach (var p in m.panels)
                {
                    if (!byName.TryGetValue(p.id, out var g)) { wrong++; if (listed.Count < 40) listed.Add("panel " + p.id + " missing"); continue; }
                    var lods = g.GetLODs();
                    if (lods.Length != p.lods.Length || (g.transform.position - new Vector3(p.position[0], p.position[1], p.position[2])).sqrMagnitude > 1e-6f) { wrong++; if (listed.Count < 40) listed.Add("panel " + p.id + ": " + lods.Length + " LODs or moved"); continue; }
                    for (int k = 0; k < lods.Length; k++)
                    {
                        var r = lods[k].renderers != null && lods[k].renderers.Length == 1 ? lods[k].renderers[0] as MeshRenderer : null; var f = r != null ? r.GetComponent<MeshFilter>() : null; var mesh = f != null ? f.sharedMesh : null;
                        if (mesh == null || mesh.vertexCount != p.lods[k].vertices || (int)(mesh.GetIndexCount(0) / 3) != p.lods[k].triangles) { wrong++; if (listed.Count < 40) listed.Add("panel " + p.id + " LOD" + k + ": mesh missing or not the manifest's vertex / triangle count"); continue; }
                        unique.Add(mesh); if (k == 0) res.lod0Triangles += p.lods[k].triangles;
                        if (r.gameObject.layer != m.layer) { wrong++; if (listed.Count < 40) listed.Add("panel " + p.id + " LOD" + k + ": layer " + r.gameObject.layer); }
                    }
                }
                foreach (var mesh in unique) res.meshMemoryBytes += Profiler.GetRuntimeMemorySizeLong(mesh);
                if (groups.Length != m.panels.Length || wrong > 0) fail.Add("structure: " + groups.Length + " LODGroups for " + m.panels.Length + " panels, " + wrong + " wrong panels / LODs");
                if (res.colliders > m.budget.collidersMax) fail.Add("colliders " + res.colliders + " (a skin has none)");
                if (res.shadowCasters > m.budget.shadowCastersMax) fail.Add("shadow casters " + res.shadowCasters + " > " + m.budget.shadowCastersMax);
                if (res.emissiveMaterials > 0) fail.Add("materials with emission (or null): " + res.emissiveMaterials);
                if (lights > 0) fail.Add("lights under " + m.group + ": " + lights);
                if (res.materials > m.budget.materialsMax) fail.Add("materials " + res.materials + " > " + m.budget.materialsMax);
                if (groups.Length > m.budget.renderersMax) fail.Add("panels " + groups.Length + " > " + m.budget.renderersMax);
                if (res.lod0Triangles > m.budget.lod0TrisMax) fail.Add("LOD0 triangles " + res.lod0Triangles + " > " + m.budget.lod0TrisMax);
                if (res.meshMemoryBytes > m.budget.meshMemoryBytesMax) fail.Add("mesh memory " + res.meshMemoryBytes + " bytes > " + m.budget.meshMemoryBytesMax + " (editor figure: the editor may keep a CPU copy the player does not)");
            }

            // probes: front distance and piercing on the real terrain colliders (works without the skins in the scene: the probe points are data)
            Physics.SyncTransforms();
            var all = new List<float>(); var per = new Dictionary<string, List<float>>(); var rows = new Dictionary<string, StretchFront>();
            foreach (var p in m.panels)
            {
                if (!rows.TryGetValue(p.stretch, out var row)) { row = new StretchFront { stretch = p.stretch }; rows[p.stretch] = row; per[p.stretch] = new List<float>(); }
                for (int i = 0; i + m.probeStride <= p.probes.Length; i += m.probeStride)
                {
                    var at = new Vector3(p.probes[i], p.probes[i + 1], p.probes[i + 2]); var outward = new Vector3(p.probes[i + 3], 0f, p.probes[i + 4]).normalized; float offline = p.probes[i + 5];
                    res.probes++; row.probes++;
                    // terrain between a point 1 m in front of the skin and the skin point = the terrain face stands in front of the skin
                    if (TerrainHit(at + outward, -outward, .98f, terrainRoot, out _) >= 0f) { res.probesPierced++; row.pierced++; if (listed.Count < 40) listed.Add("pierced " + p.id + " at " + at.ToString("F2")); continue; }
                    float d = TerrainHit(at, -outward, 8f, terrainRoot, out _);
                    if (d < 0f) { res.probesNoWall++; row.noWall++; continue; }
                    all.Add(d); per[p.stretch].Add(d); row.offlineDiffMax = Mathf.Max(row.offlineDiffMax, Mathf.Abs(d - offline)); res.offlineDiffMax = Mathf.Max(res.offlineDiffMax, Mathf.Abs(d - offline));
                    if (d > m.maxFront + .05f) { res.probesOverFront++; row.over++; if (listed.Count < 40) listed.Add("front " + F(d) + " m > " + F(m.maxFront) + " at " + p.id + " " + at.ToString("F2")); }
                }
                for (int i = 0; i + m.topStride <= p.tops.Length; i += m.topStride)
                {
                    float x = p.tops[i], z = p.tops[i + 1], crest = p.tops[i + 2], offline = p.tops[i + 4];
                    if (TerrainHit(new Vector3(x, 3000f, z), Vector3.down, 6000f, terrainRoot, out var ground) < 0f) { if (listed.Count < 40) listed.Add("top probe " + p.id + ": no terrain under (" + F(x) + ", " + F(z) + ")"); continue; }
                    res.tops++; float margin = crest - ground.y; res.topTerrainDiffMax = Mathf.Max(res.topTerrainDiffMax, Mathf.Abs(ground.y - offline));
                    if (res.tops == 1 || margin < res.topMarginMin) res.topMarginMin = margin;
                    if (margin < m.overM - .05f) { res.topsBelow++; if (listed.Count < 40) listed.Add("top " + F(margin) + " m over the wall top at " + p.id + " (" + F(x) + ", " + F(z) + ")"); }
                }
            }
            all.Sort(); res.frontP50 = Quantile(all, .5f); res.frontP95 = Quantile(all, .95f); res.frontMax = all.Count > 0 ? all[all.Count - 1] : 0f;
            foreach (var kv in rows) { var v = per[kv.Key]; v.Sort(); kv.Value.p50 = Quantile(v, .5f); kv.Value.p95 = Quantile(v, .95f); kv.Value.max = v.Count > 0 ? v[v.Count - 1] : 0f; }
            res.stretches = rows.Values.ToArray();
            if (res.probesPierced > 0) fail.Add("terrain in front of the skin at " + res.probesPierced + " probes");
            if (res.probesOverFront > 0) fail.Add("front distance over " + F(m.maxFront) + " m at " + res.probesOverFront + " probes (max " + F(res.frontMax) + ")");
            if (res.topsBelow > 0) fail.Add("skin top lower than wall top + " + F(m.overM) + " m at " + res.topsBelow + " stations");
            if (res.probes == 0) fail.Add("the manifest has no probes");
            res.failures = fail.ToArray(); res.listed = listed.ToArray(); res.ok = fail.Count == 0;

            var sb = new StringBuilder();
            sb.AppendLine("#308 cliff skins verify - " + path + " | stage " + stage + " | manifest " + Short(m.Sha256) + " | ledger " + ledger.state + " | " + res.utc);
            sb.AppendLine("panels " + res.lodGroups + " / " + res.panels + " (budget " + m.budget.renderersMax + "), mesh renderers " + res.meshRenderers + ", LOD0 triangles " + res.lod0Triangles + " (budget " + m.budget.lod0TrisMax + ")");
            sb.AppendLine("colliders " + res.colliders + ", shadow casters " + res.shadowCasters + ", materials " + res.materials + " (emissive " + res.emissiveMaterials + "), mesh memory " + F(res.meshMemoryBytes / 1048576f) + " MB measured in the editor (manifest "
                + F(m.totals.meshMemoryBytes / 1048576f) + ", budget " + F(m.budget.meshMemoryBytesMax / 1048576f) + ")");
            sb.AppendLine("front distance to the real terrain collider, " + all.Count + " probes: p50 " + F(res.frontP50) + " / p95 " + F(res.frontP95) + " / max " + F(res.frontMax) + " m (rule " + F(m.clearance) + " - " + F(m.maxFront) + "); over " + res.probesOverFront
                + ", pierced " + res.probesPierced + ", no wall within 8 m " + res.probesNoWall + "; largest difference to the offline value " + F(res.offlineDiffMax, "0.###") + " m");
            foreach (var r in res.stretches) sb.AppendLine("  " + r.stretch + ": " + r.probes + " probes, p50 " + F(r.p50) + " / p95 " + F(r.p95) + " / max " + F(r.max) + ", over " + r.over + ", pierced " + r.pierced + ", no wall " + r.noWall + ", offline diff max " + F(r.offlineDiffMax, "0.###"));
            sb.AppendLine("top over the wall top (terrain 1 m behind the edge), " + res.tops + " stations: min " + F(res.topMarginMin) + " m (rule >= " + F(m.overM) + "), below " + res.topsBelow + "; terrain vs offline max " + F(res.topTerrainDiffMax, "0.###") + " m");
            foreach (string s in listed) sb.AppendLine("  listed: " + s);
            sb.AppendLine(res.ok ? "RESULT ok" : "RESULT FAIL: " + string.Join(" | ", fail));
            sb.Append("NOT measured here: look, LOD pops, GPU cost (PerfSweep307), batches in the Stats window.");
            CliffCore308.WriteText(CliffCore308.OutFile(cfg, "cb308-skins-verify-" + key + ".txt"), sb.ToString());
            CliffCore308.WriteText(CliffCore308.OutFile(cfg, "cb308-skins-verify-" + key + ".json"), JsonUtility.ToJson(res, true));
            return sb.ToString();
        }

        // ---------------------------------------------------------------- in-memory look preview

        /// <summary>Preview roots: by name (null = every root that carries this tool's state record, plus roots of the manifest preview name).</summary>
        static List<GameObject> FindRoots(string name) =>
            Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && !EditorUtility.IsPersistent(g) && g.transform.parent == null && (g.hideFlags & HideFlags.DontSave) != 0
                && (name != null ? g.name == name : StateOf(g) != null && StateOf(g).Contains(ToolTag))).ToList();

        static string StateOf(GameObject root)
        {
            foreach (Transform c in root.transform) if (c.name.StartsWith("State|", StringComparison.Ordinal)) return c.name;
            return null;
        }

        static int StateInt(string state, string key)
        {
            foreach (string part in (state ?? "").Split('|'))
                if (part.StartsWith(key + "=", StringComparison.Ordinal) && int.TryParse(part.Substring(key.Length + 1), System.Globalization.NumberStyles.Integer, Inv, out int v)) return v;
            return -1;
        }

        static void DestroyPreview(GameObject root, out int objects, out int meshes)
        {
            objects = root.GetComponentsInChildren<Transform>(true).Length; meshes = 0;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null && !EditorUtility.IsPersistent(f.sharedMesh)) { Object.DestroyImmediate(f.sharedMesh); meshes++; }
            Object.DestroyImmediate(root);
        }

        static string PreviewOn(CliffCore308.Config cfg, Dictionary<string, string> opt)
        {
            RequireIdle();
            var scene = SceneManager.GetActiveScene();
            if (!PostLedger308.Scenes.Contains(scene.path) || PostLedger308.IsProtectedPath(scene.path)) throw new PostLedger308.Refused("active scene '" + scene.path + "' is not a #308 target scene");
            if (scene.isDirty && !opt.ContainsKey("allowdirty")) throw new PostLedger308.Refused("the scene has unsaved changes that are not from this tool (add :allowdirty to preview anyway)");
            string stage = SceneStage(cfg, scene.path, opt, out var terrain);
            var m = Load(cfg, stage);
            string mismatch = TerrainMismatch(terrain, m);
            if (mismatch != null && !opt.ContainsKey("anyheight")) throw new PostLedger308.Refused(mismatch + " (add :anyheight to preview on this terrain anyway)");
            if (FindRoots(WallPreviewRoot).Count > 0) throw new PostLedger308.Refused("the wall preview is up; order = cliff on -> wall on -> wall off -> cliff off (run WallLook308 preview:off first)");
            var existing = FindRoots(m.previewRoot);
            if (existing.Any(g => StateOf(g) == null || !StateOf(g).Contains(ToolTag))) throw new PostLedger308.Refused("another preview named " + m.previewRoot + " is up (CliffLook308); run its preview:off first");

            string[] ids = opt.TryGetValue("stretch", out var st) && st.Length > 0 ? st.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray() : null;
            var panels = m.panels.Where(p => ids == null || ids.Contains(p.stretch)).ToArray();
            if (panels.Length == 0) throw new PostLedger308.Refused("no panel of stretch " + (ids == null ? "(any)" : string.Join(",", ids)) + " (stretches: " + string.Join(", ", m.panels.Select(p => p.stretch).Distinct()) + ")");
            float scale = 1f; int force = -1; string lod = opt.TryGetValue("lod", out var lo) ? lo : "";
            if (lod == "mobile") scale = m.refLodBias / Mathf.Max(m.mobileLodBias, .01f);   // the LOD the Mobile lodBias would pick, shown under the PC quality level
            else if (lod.Length > 0 && (!int.TryParse(lod, out force) || force < 0 || force > 2)) throw new PostLedger308.Refused("lod must be mobile, 0, 1 or 2");
            var material = AssetDatabase.LoadAssetAtPath<Material>(m.materialSource);   // drawn with the source material asset itself: nothing is copied, nothing is written
            if (material == null) throw new PostLedger308.Refused("material source missing: " + m.materialSource);
            if (Glows(material)) throw new PostLedger308.Refused("material source " + m.materialSource + " has emission");
            int pc = Array.IndexOf(QualitySettings.names, "PC");
            if (pc < 0) throw new PostLedger308.Refused("no quality level named PC");

            // every file is read and checked before the first change
            var built = new List<(PanelRow p, Mesh[] meshes)>();
            try { foreach (var p in panels) built.Add((p, p.lods.Select(l => BuildMesh(m, l, m.previewRoot + "_" + LodName(l), PreviewFlags)).ToArray())); }
            catch { foreach (var b in built) foreach (var mesh in b.meshes) Object.DestroyImmediate(mesh); throw; }

            int prior = QualitySettings.GetQualityLevel(); bool dirtyAtOn = scene.isDirty; var log = new StringBuilder();
            foreach (var old in existing)
            {
                int q = StateInt(StateOf(old), "q"); if (q >= 0) prior = q;
                DestroyPreview(old, out int objects, out int meshes); log.AppendLine("replaced an earlier preview: " + objects + " objects and " + meshes + " in-memory meshes destroyed");
            }
            var root = new GameObject(m.previewRoot) { hideFlags = PreviewFlags }; int triangles = 0;
            var parents = new Dictionary<string, Transform>();
            foreach (var b in built)
            {
                if (!parents.TryGetValue(b.p.stretch, out var parent))
                {
                    var s = new GameObject(b.p.stretch) { hideFlags = PreviewFlags }; s.transform.SetParent(root.transform, false); parent = s.transform; parents[b.p.stretch] = parent;
                }
                BuildPanel(m, b.p, b.meshes, material, parent, PreviewFlags, scale, force); triangles += b.p.lods[0].triangles;
            }
            if (pc != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(pc, true);
            var state = new GameObject("State|q=" + prior + "|" + ToolTag + "|stage=" + stage + "|lod=" + (lod.Length > 0 ? lod : "pc") + "|stretch=" + (ids == null ? "all" : string.Join(",", ids)) + "|dirtyAtOn=" + dirtyAtOn + "|utc=" + PostLedger308.Utc()) { hideFlags = PreviewFlags };
            state.transform.SetParent(root.transform, false);
            log.AppendLine("preview up: stage " + stage + " (manifest " + Short(m.Sha256) + "), " + built.Count + " panels in " + parents.Count + " stretches, LOD0 " + triangles + " triangles, material " + material.name + " (" + material.shader.name
                + ", source asset, not copied), shadows " + m.shadowCasting + ", colliders " + root.GetComponentsInChildren<Collider>(true).Length + ", LOD " + (lod == "mobile" ? "Mobile switch distances (screen heights x " + F(scale) + ")" : force >= 0 ? "forced " + force : "PC"));
            if (mismatch != null) log.AppendLine("WARNING (anyheight): " + mismatch);
            log.Append("quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " (prior level " + prior + " " + QualitySettings.names[prior] + ", restored by preview:off) | all objects DontSave, no asset written | scene dirty=" + scene.isDirty);
            return log.ToString();
        }

        static string PreviewOff()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (FindRoots(WallPreviewRoot).Count > 0) throw new PostLedger308.Refused("the wall preview is still up; order = cliff on -> wall on -> wall off -> cliff off (run WallLook308 preview:off first)");
            var mine = FindRoots(null); int level = QualitySettings.GetQualityLevel();
            if (mine.Count == 0) return "preview: none of this tool to remove | quality " + level + " (" + QualitySettings.names[level] + ") | scene dirty=" + SceneManager.GetActiveScene().isDirty;
            int prior = -1, objects = 0, meshes = 0;
            foreach (var root in mine)
            {
                int q = StateInt(StateOf(root), "q"); if (q >= 0) prior = q;
                DestroyPreview(root, out int o, out int k); objects += o; meshes += k;
            }
            if (prior >= 0 && prior < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != prior) QualitySettings.SetQualityLevel(prior, true);
            level = QualitySettings.GetQualityLevel();
            return "preview off: " + objects + " objects and " + meshes + " in-memory meshes destroyed" + (prior >= 0 ? ", recorded prior quality " + prior : ", no recorded quality") + " | quality " + level + " (" + QualitySettings.names[level] + ") | scene dirty="
                + SceneManager.GetActiveScene().isDirty;
        }
    }
}
