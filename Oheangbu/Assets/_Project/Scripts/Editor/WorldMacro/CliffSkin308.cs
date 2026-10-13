using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 cliff skin (SPEC-WORLD-CLIFFS-308 section 12, D308-75): generated cladding sheets over the terrain faces. The terrain stays the body:
    // the skin has no collider, no NavMesh part, no static flag. Meshes come from python Tools/Art/cliffskin308.py build (bins + manifest.json).
    //   status
    //   skin:import[:<face>]   bins -> mesh assets under Assets/_Project/Art/World/CliffSkin308/<Realm>/ (asset name carries the build tag)
    //   skin:place             root CliffSkin308/<Realm>/<face chunk> in the play scene (LODGroup: LOD0, LOD1, culled), the terrain tile's own material
    //   skin:revert            root removed from the play scene (assets stay; skin:purge deletes them)
    //   skin:purge             mesh assets of this tool deleted (only when no root is in the scene)
    //   skin:check             objects, materials, budget, vertices against the stage field, sheet edges, gallery clearance, no colliders
    // Queue-safe: no dialog, refusals come back as "REFUSED ...". The scene is backed up before it is saved; AssetDatabase.SaveAssets is never called.
    public static class CliffSkin308
    {
        const string Usage = "status | skin:import[:<face>] | skin:place | skin:revert | skin:purge | skin:check";
        const string PlaySceneName = "W_Demo_Main", RootName = "CliffSkin308", AssetDir = "Assets/_Project/Art/World/CliffSkin308", TerrainRoot = "Reworld292_Terrain";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string StageDir => Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_cliffs");
        static string DataDir => Path.Combine(RepoRoot, "Art", "World", "Compact", "Rebuild", "CliffSkin308");

        [Serializable] sealed class Budget { public int lod0_per_100m = 150000, lod0_total = 2500000; }
        [Serializable] sealed class Lod { public float lod0_until_m = 240f, cull_m = 650f; }
        [Serializable] sealed class Config { public string tag = "", height = "", walks = ""; public Budget budget = new Budget(); public Lod lod = new Lod(); }
        [Serializable] sealed class Part { public string file = ""; public int vertices, triangles; }
        [Serializable] sealed class Chunk { public int index; public string name = ""; public float[] min = Array.Empty<float>(), max = Array.Empty<float>(); public Part LOD0 = new Part(), LOD1 = new Part(); }
        [Serializable] sealed class Tris { public int LOD0, LOD1; }
        [Serializable] sealed class Edge { public int edge_nodes, edge_nodes_above_ground; public float worst_above_m; }
        [Serializable] sealed class Face
        {
            public string id = "", realm = "", tag = ""; public float length_m, height_m, height_max_m, rim_over_min_m, rim_back_m, seen_clear_min_m, ridge_share; public int lod0_tris_per_100m, mesh_bytes;
            public Tris triangles = new Tris(); public Edge boundary = new Edge(); public Chunk[] chunks = Array.Empty<Chunk>();
        }
        [Serializable] sealed class Manifest { public string tag = "", height_sha256 = ""; public Face[] faces = Array.Empty<Face>(); public int lod0_total, mesh_bytes_total; }
        [Serializable] sealed class WalkWay { public string id = ""; public float[] walkX = Array.Empty<float>(), walkY = Array.Empty<float>(), walkZ = Array.Empty<float>(); }
        [Serializable] sealed class LayoutFile { public WalkWay[] ways = Array.Empty<WalkWay>(); }
        sealed class Bin { public Vector3 origin; public Vector3[] pos, nrm; public Vector2[] uv; public byte[] flag; public int[] tri; }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return Status();
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "REFUSED scripts are compiling or failed to compile";
                if (c == "skin:import" || c.StartsWith("skin:import:", StringComparison.Ordinal)) return Import(c.Length > 12 ? c.Substring(12) : "");
                switch (c)
                {
                    case "skin:place": return Place();
                    case "skin:revert": return Revert();
                    case "skin:purge": return Purge();
                    case "skin:check": return Check();
                }
                return "REFUSED unknown command '" + c + "' (" + Usage + ")";
            }
            catch (Exception e) { return "REFUSED " + e.Message; }
        }

        static string F(float v) => v.ToString("F2", Inv);
        static Config ReadConfig() => JsonUtility.FromJson<Config>(File.ReadAllText(Path.Combine(StageDir, "cliffskin308.json")));
        static Manifest ReadManifest()
        {
            string p = Path.Combine(DataDir, "manifest.json"); if (!File.Exists(p)) throw new Exception("no " + p + " (run python Tools/Art/cliffskin308.py build ...)");
            return JsonUtility.FromJson<Manifest>(File.ReadAllText(p));
        }

        static Scene PlayScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != PlaySceneName) throw new Exception("open " + PlaySceneName + " first (active scene is " + scene.name + ")");
            if (scene.isDirty) throw new Exception("the play scene has unsaved changes");
            return scene;
        }

        static string Backup(Scene scene, string label)
        {
            string dir = Path.Combine(StageDir, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_" + label); Directory.CreateDirectory(dir);
            File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true); return dir;
        }

        static void Save(Scene scene) { EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved"); }
        static GameObject Root(Scene scene) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string MeshPath(Face f, Part p) => AssetDir + "/" + f.realm + "/" + Path.GetFileNameWithoutExtension(p.file) + ".asset";

        static Bin ReadBin(string file)
        {
            byte[] b = File.ReadAllBytes(Path.Combine(DataDir, file)); if (b.Length < 24 || b[0] != 'C' || b[1] != 'S' || b[2] != 'K' || b[3] != '1') throw new Exception(file + " is not a CSK1 file");
            int nv = BitConverter.ToInt32(b, 4), ni = BitConverter.ToInt32(b, 8); var o = new float[3]; Buffer.BlockCopy(b, 12, o, 0, 12); int off = 24;
            if (b.Length != 24 + nv * 33 + ni * 4) throw new Exception(file + " has the wrong length");
            var fp = new float[nv * 3]; Buffer.BlockCopy(b, off, fp, 0, nv * 12); off += nv * 12; var fn = new float[nv * 3]; Buffer.BlockCopy(b, off, fn, 0, nv * 12); off += nv * 12;
            var fu = new float[nv * 2]; Buffer.BlockCopy(b, off, fu, 0, nv * 8); off += nv * 8; var flag = new byte[nv]; Buffer.BlockCopy(b, off, flag, 0, nv); off += nv; var tri = new int[ni]; Buffer.BlockCopy(b, off, tri, 0, ni * 4);
            var bin = new Bin { origin = new Vector3(o[0], o[1], o[2]), pos = new Vector3[nv], nrm = new Vector3[nv], uv = new Vector2[nv], flag = flag, tri = tri };
            for (int i = 0; i < nv; i++) { bin.pos[i] = new Vector3(fp[3 * i], fp[3 * i + 1], fp[3 * i + 2]); bin.nrm[i] = new Vector3(fn[3 * i], fn[3 * i + 1], fn[3 * i + 2]); bin.uv[i] = new Vector2(fu[2 * i], fu[2 * i + 1]); }
            return bin;
        }

        static string Status()
        {
            var scene = SceneManager.GetActiveScene(); var root = Root(scene); var sb = new StringBuilder("CliffSkin308 status: active scene " + scene.name + (scene.isDirty ? "*" : ""));
            sb.Append(", root ").Append(root == null ? "absent" : "present (renderers " + root.GetComponentsInChildren<Renderer>(true).Length + ", LOD groups " + root.GetComponentsInChildren<LODGroup>(true).Length + ", colliders " + root.GetComponentsInChildren<Collider>(true).Length + ")");
            int assets = AssetDatabase.IsValidFolder(AssetDir) ? AssetDatabase.FindAssets("t:Mesh", new[] { AssetDir }).Length : 0; sb.Append(", mesh assets ").Append(assets);
            string mp = Path.Combine(DataDir, "manifest.json"); if (File.Exists(mp)) { var m = ReadManifest(); sb.Append(", manifest tag ").Append(m.tag).Append(" faces ").Append(m.faces.Length).Append(" LOD0 ").Append(m.lod0_total); } else sb.Append(", no manifest");
            return sb.ToString();
        }

        // normals are normalised here as well: a zero-length normal lights as NaN (white bloom over the whole view)
        static string Import(string only)
        {
            PlayScene(); var man = ReadManifest(); int made = 0, kept = 0; long verts = 0, tris = 0; var sb = new StringBuilder();
            foreach (var face in man.faces)
            {
                if (only.Length > 0 && face.id != only) continue; Folder(AssetDir + "/" + face.realm);
                foreach (var ch in face.chunks)
                    foreach (var part in new[] { ch.LOD0, ch.LOD1 })
                    {
                        if (string.IsNullOrEmpty(part.file)) continue; string path = MeshPath(face, part);
                        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) { kept++; continue; }      // a tag is written once: same name = same data
                        var bin = ReadBin(part.file); int nv = bin.pos.Length, zero = 0;
                        for (int i = 0; i < nv; i++) { var n = bin.nrm[i]; if (float.IsNaN(n.x) || float.IsNaN(n.y) || float.IsNaN(n.z) || n.sqrMagnitude < 1e-8f) { bin.nrm[i] = Vector3.up; zero++; } else bin.nrm[i] = n.normalized; }
                        if (zero > 0) throw new Exception(part.file + ": " + zero + " zero / NaN normals");
                        var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(part.file), indexFormat = nv > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                        mesh.SetVertices(bin.pos); mesh.SetNormals(bin.nrm); mesh.SetUVs(0, bin.uv); mesh.SetTriangles(bin.tri, 0, false); mesh.RecalculateBounds();
                        MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Low); AssetDatabase.CreateAsset(mesh, path);
                        var so = new SerializedObject(mesh); var rw = so.FindProperty("m_IsReadable"); if (rw != null) { rw.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
                        AssetDatabase.SaveAssetIfDirty(mesh); made++; verts += nv; tris += bin.tri.Length / 3;
                    }
                sb.Append("\n  ").Append(face.id).Append(" (").Append(face.realm).Append("): chunks ").Append(face.chunks.Length);
            }
            return "CliffSkin308 skin:import: tag " + man.tag + ", mesh assets made " + made + " (already there " + kept + "), vertices " + verts + ", triangles " + tris + sb;
        }

        static Renderer TileAt(GameObject terrain, float x, float z)
        {
            Renderer best = null; int bestCount = -1;
            foreach (Transform tile in terrain.transform)
                foreach (var f in tile.GetComponentsInChildren<MeshFilter>(true))
                {
                    var r = f.GetComponent<Renderer>(); if (r == null || f.sharedMesh == null) continue; var b = r.bounds; if (x < b.min.x || x > b.max.x || z < b.min.z || z > b.max.z) continue;
                    if (f.sharedMesh.vertexCount > bestCount) { best = r; bestCount = f.sharedMesh.vertexCount; }
                }
            return best;
        }

        static string Place()
        {
            var scene = PlayScene(); var cfg = ReadConfig(); var man = ReadManifest(); var terrain = scene.GetRootGameObjects().FirstOrDefault(g => g.name == TerrainRoot); if (terrain == null) throw new Exception("no " + TerrainRoot + " root");
            foreach (var face in man.faces) foreach (var ch in face.chunks) foreach (var part in new[] { ch.LOD0, ch.LOD1 }) if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(face, part)) == null) throw new Exception("mesh asset missing: " + MeshPath(face, part) + " (skin:import first)");
            string backup = Backup(scene, "skin_place"); var old = Root(scene); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene); int groups = 0; var sb = new StringBuilder(); var used = new HashSet<string>(); float k = 2f * Mathf.Tan(30f * Mathf.Deg2Rad);
            foreach (var face in man.faces)
            {
                var realm = root.transform.Find(face.realm); if (realm == null) { var rg = new GameObject(face.realm); rg.transform.SetParent(root.transform, false); realm = rg.transform; }
                foreach (var ch in face.chunks)
                {
                    float cx = (ch.min[0] + ch.max[0]) / 2f, cz = (ch.min[2] + ch.max[2]) / 2f; var tile = TileAt(terrain, cx, cz); if (tile == null || tile.sharedMaterial == null) throw new Exception("no terrain tile under " + ch.name + " (" + F(cx) + "," + F(cz) + ")");
                    var holder = new GameObject(ch.name); holder.transform.SetParent(realm, false); holder.layer = tile.gameObject.layer; var lods = new List<LOD>(); int li = 0;
                    foreach (var part in new[] { ch.LOD0, ch.LOD1 })
                    {
                        string path = MeshPath(face, part); var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); used.Add(path); var bin0 = ReadBinOrigin(part.file);
                        var g = new GameObject(li == 0 ? "LOD0" : "LOD1"); g.transform.SetParent(holder.transform, false); g.transform.position = bin0; g.layer = holder.layer;
                        g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = tile.sharedMaterial; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
                        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off; r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                        lods.Add(new LOD(li == 0 ? 0.5f : 0.1f, new Renderer[] { r })); li++;
                    }
                    var mn = new Vector3(ch.min[0], ch.min[1], ch.min[2]); var mx = new Vector3(ch.max[0], ch.max[1], ch.max[2]); float size = Mathf.Max(mx.x - mn.x, mx.y - mn.y, mx.z - mn.z, 1f); var set = lods.ToArray();
                    set[0].screenRelativeTransitionHeight = Mathf.Clamp(size / (cfg.lod.lod0_until_m * k), 0.02f, 0.9f); set[1].screenRelativeTransitionHeight = Mathf.Clamp(size / (cfg.lod.cull_m * k), 0.005f, set[0].screenRelativeTransitionHeight * 0.9f);
                    var lg = holder.AddComponent<LODGroup>(); lg.SetLODs(set); lg.localReferencePoint = holder.transform.InverseTransformPoint((mn + mx) / 2f); lg.size = size; lg.fadeMode = LODFadeMode.None; groups++;
                    sb.Append("\n  ").Append(face.realm).Append('/').Append(ch.name).Append(": size ").Append(F(size)).Append(" m, LOD0 >= ").Append(set[0].screenRelativeTransitionHeight.ToString("0.###", Inv)).Append(", LOD1 >= ").Append(set[1].screenRelativeTransitionHeight.ToString("0.###", Inv)).Append(", material ").Append(tile.sharedMaterial.name);
                }
            }
            // older tags of this tool: nothing in the scene uses them any more
            int deleted = 0; foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { AssetDir })) { string p = AssetDatabase.GUIDToAssetPath(guid); if (!used.Contains(p)) { AssetDatabase.DeleteAsset(p); deleted++; } }
            Save(scene);
            return "CliffSkin308 skin:place: " + groups + " LOD groups under " + RootName + ", no colliders, shadows off, static flags none, older mesh assets deleted " + deleted + ", backup " + backup + sb;
        }

        static Vector3 ReadBinOrigin(string file)
        {
            var b = new byte[24]; using (var fs = File.OpenRead(Path.Combine(DataDir, file))) { if (fs.Read(b, 0, 24) != 24) throw new Exception(file + " is too short"); }
            var o = new float[3]; Buffer.BlockCopy(b, 12, o, 0, 12); return new Vector3(o[0], o[1], o[2]);
        }

        static string Revert()
        {
            var scene = PlayScene(); var old = Root(scene); if (old == null) return "CliffSkin308 skin:revert: root absent";
            string backup = Backup(scene, "skin_revert"); UnityEngine.Object.DestroyImmediate(old); Save(scene); return "CliffSkin308 skin:revert: root removed, backup " + backup + " (mesh assets stay; skin:purge deletes them)";
        }

        static string Purge()
        {
            var scene = PlayScene(); if (Root(scene) != null) return "REFUSED the root is in the scene (skin:revert first)";
            int deleted = 0; if (AssetDatabase.IsValidFolder(AssetDir)) foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { AssetDir })) { AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid)); deleted++; }
            return "CliffSkin308 skin:purge: mesh assets deleted " + deleted;
        }

        static float[] Field(Config cfg)
        {
            byte[] raw = File.ReadAllBytes(Path.Combine(RepoRoot, cfg.height)); if (raw.Length != 1501 * 1001 * 4) throw new Exception("stage field has the wrong size: " + cfg.height);
            var f = new float[1501 * 1001]; Buffer.BlockCopy(raw, 0, f, 0, raw.Length); return f;
        }

        // height of the tile MESH surface: each 4 m quad = triangles (00, 01, 10) and (10, 01, 11), as the tiles are built
        static float Tri(float[] h, float x, float z)
        {
            float u = Mathf.Clamp(x, 0f, 3999.99f) / 4f, w = Mathf.Clamp(z, 0f, 5999.99f) / 4f; int j = (int)u, i = (int)w; float fx = u - j, fz = w - i;
            float h00 = h[i * 1001 + j], h10 = h[i * 1001 + j + 1], h01 = h[(i + 1) * 1001 + j], h11 = h[(i + 1) * 1001 + j + 1];
            return fx + fz <= 1f ? h00 + (h10 - h00) * fx + (h01 - h00) * fz : h11 + (h01 - h11) * (1f - fx) + (h10 - h11) * (1f - fz);
        }

        static string Check()
        {
            var scene = SceneManager.GetActiveScene(); if (scene.name != PlaySceneName) throw new Exception("open " + PlaySceneName + " first");
            var cfg = ReadConfig(); var man = ReadManifest(); var field = Field(cfg); var root = Root(scene); var terrain = scene.GetRootGameObjects().FirstOrDefault(g => g.name == TerrainRoot);
            var sb = new StringBuilder("CliffSkin308 skin:check (tag " + man.tag + ")"); int checks = 0, fails = 0;
            void Line(bool ok, string text) { checks++; if (!ok) fails++; sb.Append("\n  ").Append(ok ? "PASS " : "FAIL ").Append(text); }
            if (root == null) { Line(false, "root " + RootName + " absent"); return sb.Append("\n  checks " + checks + ", FAIL " + fails).ToString(); }
            // walks of the cliff ways, one station every 0.5 m
            var walk = new List<Vector3>(); string lay = Path.Combine(RepoRoot, cfg.walks);
            if (File.Exists(lay)) foreach (var w in JsonUtility.FromJson<LayoutFile>(File.ReadAllText(lay)).ways) for (int q = 0; q + 1 < w.walkX.Length; q++)
            {
                var a = new Vector3(w.walkX[q], w.walkY[q], w.walkZ[q]); var b = new Vector3(w.walkX[q + 1], w.walkY[q + 1], w.walkZ[q + 1]); int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.5f)); for (int s = 0; s < steps; s++) walk.Add(Vector3.Lerp(a, b, s / (float)steps));
            }
            int objMissing = 0, meshWrong = 0, boundsOff = 0, matBad = 0, staticFlags = 0, shadowOn = 0; long seen = 0, behind = 0, thin = 0, badNormal = 0, inWalk = 0, verts = 0; float minClear = 1e9f; string firstBehind = "", firstWalk = "", firstMat = "";
            foreach (var face in man.faces)
            {
                foreach (var ch in face.chunks)
                {
                    var holder = root.transform.Find(face.realm + "/" + ch.name); if (holder == null) { objMissing++; continue; }
                    var lg = holder.GetComponent<LODGroup>(); if (lg == null || lg.GetLODs().Length != 2 || lg.size < 20f) objMissing++;
                    var parts = new[] { ch.LOD0, ch.LOD1 };
                    for (int li = 0; li < 2; li++)
                    {
                        var t = holder.Find(li == 0 ? "LOD0" : "LOD1"); var mf = t != null ? t.GetComponent<MeshFilter>() : null; var r = t != null ? t.GetComponent<MeshRenderer>() : null; if (mf == null || r == null || mf.sharedMesh == null) { objMissing++; continue; }
                        if (mf.sharedMesh.name != Path.GetFileNameWithoutExtension(parts[li].file) || mf.sharedMesh.vertexCount != parts[li].vertices || (int)(mf.sharedMesh.GetIndexCount(0) / 3) != parts[li].triangles) meshWrong++;
                        var b = r.bounds; if (b.min.x < ch.min[0] - 6f || b.min.y < ch.min[1] - 6f || b.min.z < ch.min[2] - 6f || b.max.x > ch.max[0] + 6f || b.max.y > ch.max[1] + 6f || b.max.z > ch.max[2] + 6f || b.size.x < 1f) boundsOff++;
                        var tile = terrain != null ? TileAt(terrain, b.center.x, b.center.z) : null; var m = r.sharedMaterial;
                        if (m == null || m.shader == null || !m.shader.isSupported || m.shader.name.Contains("InternalError") || tile == null || tile.sharedMaterial != m || m.IsKeywordEnabled("_EMISSION")) { matBad++; if (firstMat.Length == 0) firstMat = holder.name + " " + (m == null ? "no material" : m.name + " / " + (m.shader != null ? m.shader.name : "no shader")); }
                        if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0) staticFlags++; if (r.shadowCastingMode != ShadowCastingMode.Off) shadowOn++;
                        if (li != 0) continue;
                        var bin = ReadBin(parts[li].file); verts += bin.pos.Length;
                        for (int i = 0; i < bin.pos.Length; i++)
                        {
                            var p = bin.pos[i] + bin.origin; var n = bin.nrm[i]; if (float.IsNaN(n.x) || Mathf.Abs(n.magnitude - 1f) > 0.01f) badNormal++;
                            if (bin.flag[i] != 0)
                            {
                                seen++; float over = p.y - Tri(field, p.x, p.z); minClear = Mathf.Min(minClear, over); if (over < 0.25f) thin++;
                                if (over < 0f) { behind++; if (firstBehind.Length == 0) firstBehind = holder.name + " (" + F(p.x) + "," + F(p.y) + "," + F(p.z) + ") " + F(over) + " m"; }
                            }
                        }
                        // no skin vertex inside the walked space of a way (0.9 m to either side, 0.1 - 1.9 m over the deck)
                        if (walk.Count > 0)
                        {
                            var wb = new Bounds(walk[0], Vector3.zero); foreach (var w in walk) wb.Encapsulate(w); wb.Expand(6f);
                            if (wb.Intersects(b))
                            {
                                var near = walk.Where(w => b.SqrDistance(w) < 9f).ToArray();
                                for (int i = 0; i < bin.pos.Length && near.Length > 0; i++)
                                {
                                    var p = bin.pos[i] + bin.origin;
                                    foreach (var w in near) { float dy = p.y - w.y; if (dy > 0.1f && dy < 1.9f && (p.x - w.x) * (p.x - w.x) + (p.z - w.z) * (p.z - w.z) < 0.81f) { inWalk++; if (firstWalk.Length == 0) firstWalk = holder.name + " (" + F(p.x) + "," + F(p.y) + "," + F(p.z) + ")"; break; } }
                                }
                            }
                        }
                    }
                }
            }
            int groups = man.faces.Sum(f => f.chunks.Length);
            Line(objMissing == 0 && meshWrong == 0, "objects: " + groups + " chunks x (LODGroup + LOD0 + LOD1), missing " + objMissing + ", mesh not the manifest's " + meshWrong);
            Line(boundsOff == 0, "renderer bounds inside the manifest box (+6 m: LOD1 is cut at coarser columns): off " + boundsOff);
            Line(matBad == 0, "material = the terrain tile's own material under each chunk, shader supported, no emission keyword: bad " + matBad + (firstMat.Length > 0 ? " [" + firstMat + "]" : ""));
            Line(behind == 0, "LOD0 vertices meant to be seen " + seen + " of " + verts + ": under the terrain surface " + behind + (firstBehind.Length > 0 ? " [" + firstBehind + "]" : "") + ", lowest height over the terrain " + F(minClear) + " m (under 0.25 m: " + thin + ")");
            Line(badNormal == 0, "normals: not unit length / NaN " + badNormal);
            foreach (var face in man.faces)
            {
                Line(face.rim_over_min_m >= 0.5f && face.rim_back_m >= 0.5f, face.id + " rim: cap stands at least " + F(face.rim_over_min_m) + " m over the terrain rim and reaches " + F(face.rim_back_m) + " m behind it (>= 0.5 m both); thin-ridge stations (cap left off, one row over the crest) " + (face.ridge_share * 100f).ToString("F0", Inv) + " %");
                Line(face.boundary.edge_nodes_above_ground == 0, face.id + " sheet edge: " + face.boundary.edge_nodes + " edge nodes, above the ground " + face.boundary.edge_nodes_above_ground + " (highest " + F(face.boundary.worst_above_m) + " m) - an edge under the ground cannot be looked behind");
                Line(face.lod0_tris_per_100m <= cfg.budget.lod0_per_100m, face.id + " budget: " + F(face.length_m) + " m x " + F(face.height_m) + " m, LOD0 " + face.triangles.LOD0 + " (" + face.lod0_tris_per_100m + " / 100 m, limit " + cfg.budget.lod0_per_100m + "), LOD1 " + face.triangles.LOD1 + ", " + (face.mesh_bytes / 1048576f).ToString("F1", Inv) + " MB");
            }
            Line(man.lod0_total <= cfg.budget.lod0_total, "LOD0 total " + man.lod0_total + " (limit " + cfg.budget.lod0_total + "), mesh memory " + (man.mesh_bytes_total / 1048576f).ToString("F1", Inv) + " MB");
            Line(inWalk == 0, "cliff ways: skin vertices inside the walked space (" + walk.Count + " stations) " + inWalk + (firstWalk.Length > 0 ? " [" + firstWalk + "]" : ""));
            int colliders = root.GetComponentsInChildren<Collider>(true).Length; int extra = root.GetComponentsInChildren<Component>(true).Count(c => c != null && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer) && !(c is LODGroup));
            Line(colliders == 0 && extra == 0 && staticFlags == 0 && shadowOn == 0, "the skin changes nothing a walker or an agent can touch: colliders " + colliders + ", other components " + extra + ", static flags " + staticFlags + ", shadow casters " + shadowOn + " (reach = the terrain's, unchanged)");
            return sb.Append("\n  checks " + checks + ", FAIL " + fails).ToString();
        }
    }
}
