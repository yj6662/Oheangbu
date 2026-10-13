using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 stage n7 (SPEC-WORLD-CLIFFS-308): what the editor does after CliffBoundary308 has applied the stage field.
    //   status
    //   ways:build | ways:clear       the climbing cliff ways (Tools/Art/cliffs308.py build): root CliffWays308 in the play scene, one child per way
    //                                 (drawn meshes under a LODGroup, one MeshCollider, NavMesh "not walkable" boxes so no enemy follows)
    //   rocks:build | rocks:clear     dressing blocks at the moved feet + the terrace cairns (Out/rocks.json): root CliffRocks308
    //   rocks:reseat | rocks:unseat   the earlier foot boulders (RuggedRocks308) follow the ground by (to - from); the ones a buttress buried are switched off
    //   flora:reseat | flora:restore  the Flora308 sheets follow the ground by (to - from); plants on a new face are dropped
    // Numbers: Tools/Unity/Stage308_cliffs/cliffs308_editor.json. Ledger + backups: Tools/Unity/Stage308_cliffs/{Ledger,Backup}.
    // Queue-safe: no dialog, refusals come back as "REFUSED ...". Scenes are backed up before they are saved; AssetDatabase.SaveAssets is never called.
    public static class Cliffs308
    {
        const string Usage = "plants:plan | plants:apply | plants:revert | sky:check | ways:check | strata:apply | strata:revert | status | ways:build | ways:clear | rocks:build | rocks:clear | rocks:reseat | rocks:unseat | flora:reseat | flora:restore";
        const string PlaySceneName = "W_Demo_Main", WaysRoot = "CliffWays308", RocksRoot = "CliffRocks308";
        const string AssetDir = "Assets/_Project/Art/World/CliffWays308", MaterialDir = "Assets/_Project/Art/World/Finish297/Materials/", SurfaceDir = "Assets/_Project/Art/World/CliffBoundary308/Surface";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string StageDir => Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_cliffs");
        static string WaysData => Path.Combine(RepoRoot, "Art", "World", "Compact", "Rebuild", "CliffWays308");
        static string OldRocksLedger => Path.Combine(StageDir, "Ledger", "old_rocks.json");
        static string FloraBackupDir => Path.Combine(StageDir, "Ledger", "flora_before");

        [Serializable] sealed class Tint { public string realm = ""; public float[] color = Array.Empty<float>(); }
        [Serializable] sealed class Config
        {
            public string stage = "", floraFrom = "", floraTo = "", oldRocksRoot = "", stoneSource = ""; public float floraRemoveSlopeDeg = 40f, oldRocksBuryM = 3.5f, oldRocksFaceDeg = 35f, lodCullScreenHeight = 0.01f, navBoxEveryM = 10f, navBoxWidthM = 11f, navBoxUpM = 5f, navBoxDownM = 3f;
            public string[] floraSheets = Array.Empty<string>(), sharedSheets = Array.Empty<string>(); public float plantClearM = 2f; public Tint[] stoneTint = Array.Empty<Tint>(), rockTint = Array.Empty<Tint>(); public RealmPoly[] realms = Array.Empty<RealmPoly>();
        }
        [Serializable] sealed class RealmPoly { public string id = ""; public float[] poly = Array.Empty<float>(); }
        [Serializable] sealed class Sub { public string material = ""; public int[] triangles = Array.Empty<int>(); }
        [Serializable] sealed class MeshData { public string name = ""; public float[] vertices = Array.Empty<float>(), normals = Array.Empty<float>(), uvs = Array.Empty<float>(); public Sub[] submeshes = Array.Empty<Sub>(); }
        [Serializable] sealed class WayRoute { public string id = "", realm = ""; public float[] x = Array.Empty<float>(), y = Array.Empty<float>(), z = Array.Empty<float>(), s = Array.Empty<float>(); }
        [Serializable] sealed class WalkWay { public string id = "", realm = ""; public float[] walkX = Array.Empty<float>(), walkY = Array.Empty<float>(), walkZ = Array.Empty<float>(); }
        [Serializable] sealed class LayoutFile { public WalkWay[] ways = Array.Empty<WalkWay>(); }
        [Serializable] sealed class RouteFile { public string stage = ""; public WayRoute[] ways = Array.Empty<WayRoute>(); }
        [Serializable] sealed class Rock { public string prefab = "", kind = "", realm = ""; public float x, y, z, yaw, size, sink, tiltX, tiltZ; }
        [Serializable] sealed class RockFile { public string prefabDir = ""; public Rock[] rocks = Array.Empty<Rock>(); }
        [Serializable] sealed class OldRock { public int index; public float y; public bool active; }
        [Serializable] sealed class OldRockLedger { public string root = "", from = "", to = ""; public List<OldRock> rocks = new List<OldRock>(); }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return Status();
                if (c.StartsWith("holes:", StringComparison.Ordinal))
                {
                    // holes:<x0>,<z0>,<x1>,<z1>  a 0.2 m grid of rays straight down: where is there no terrain collider, or one that is off the stage field
                    var v_ = c.Substring(6).Split(',').Select(v => float.Parse(v, Inv)).ToArray(); var to_ = Field(ReadConfig().floraTo); Physics.SyncTransforms(); int n_ = 0, miss_ = 0, off_ = 0; float mx0 = 1e9f, mx1 = -1e9f, mz0 = 1e9f, mz1 = -1e9f; var names = new HashSet<string>();
                    var terr_ = SceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == "Reworld292_Terrain");
                    for (float z = v_[1]; z <= v_[3]; z += 0.2f) for (float x = v_[0]; x <= v_[2]; x += 0.2f)
                    {
                        n_++; float want = Sample(to_, x, z); bool hit = false; float best = float.NegativeInfinity; string who = "";
                        foreach (var h in Physics.RaycastAll(new Vector3(x, want + 80f, z), Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore)) if (h.collider.transform.IsChildOf(terr_.transform) && h.point.y > best) { hit = true; best = h.point.y; who = h.collider.name; }
                        if (!hit) { miss_++; mx0 = Mathf.Min(mx0, x); mx1 = Mathf.Max(mx1, x); mz0 = Mathf.Min(mz0, z); mz1 = Mathf.Max(mz1, z); } else { names.Add(who); if (Mathf.Abs(best - want) > 0.3f) off_++; }
                    }
                    // the drawn meshes in the box: normals that are zero / NaN / pointing down, tangents missing or parallel to the normal (a shader that builds a frame from them outputs NaN there)
                    int vn = 0, badN = 0, downN = 0, par = 0, noTan = 0; float minNy = 1f, maxDot = 0f; string firstBad = "";
                    foreach (Transform tile in terr_.transform)
                    {
                        var f0 = tile.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).OrderByDescending(f => f.sharedMesh.vertexCount).FirstOrDefault(); if (f0 == null) continue;
                        var b_ = f0.GetComponent<Renderer>().bounds; if (b_.max.x < v_[0] || b_.min.x > v_[2] || b_.max.z < v_[1] || b_.min.z > v_[3]) continue;
                        var m_ = f0.transform.localToWorldMatrix; var vs = f0.sharedMesh.vertices; var ns = f0.sharedMesh.normals; var ts = f0.sharedMesh.tangents; if (ts.Length != vs.Length) noTan++;
                        for (int q = 0; q < vs.Length; q++)
                        {
                            var w = m_.MultiplyPoint3x4(vs[q]); if (w.x < v_[0] || w.x > v_[2] || w.z < v_[1] || w.z > v_[3]) continue; vn++; var nq = ns[q];
                            bool bad = float.IsNaN(nq.x) || float.IsNaN(nq.y) || float.IsNaN(nq.z) || nq.sqrMagnitude < 0.25f; if (bad) badN++; if (nq.y <= 0.001f) downN++; minNy = Mathf.Min(minNy, nq.y);
                            if (ts.Length == vs.Length) { float d = Mathf.Abs(Vector3.Dot(nq.normalized, new Vector3(ts[q].x, ts[q].y, ts[q].z).normalized)); maxDot = Mathf.Max(maxDot, d); if (d > 0.995f) par++; }
                            if ((bad || nq.y <= 0.001f) && firstBad.Length == 0) firstBad = tile.name + " (" + F(w.x) + "," + F(w.y) + "," + F(w.z) + ") n " + nq.ToString("F3");
                        }
                    }
                    var rlist = new List<string>();
                    foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    {
                        foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                        {
                            var b_ = r.bounds; if (b_.max.x < v_[0] || b_.min.x > v_[2] || b_.max.z < v_[1] || b_.min.z > v_[3] || b_.max.y < Sample(to_, (v_[0] + v_[2]) / 2f, (v_[1] + v_[3]) / 2f) - 3f || b_.min.y > Sample(to_, (v_[0] + v_[2]) / 2f, (v_[1] + v_[3]) / 2f) + 12f) continue;
                            var mf_ = r.GetComponent<MeshFilter>(); if (rlist.Count < 30) rlist.Add(root.name + "/" + (r.transform.parent != null ? r.transform.parent.name + "/" : "") + r.name + (r.enabled ? "" : " (disabled)") + " mesh " + (mf_ != null && mf_.sharedMesh != null ? mf_.sharedMesh.name + " v" + mf_.sharedMesh.vertexCount : "-") + " [" + (r.sharedMaterial != null ? r.sharedMaterial.name : "no material") + "] size " + F(b_.size.x) + "x" + F(b_.size.y) + "x" + F(b_.size.z) + " at " + F(b_.center.x) + "," + F(b_.center.y) + "," + F(b_.center.z));
                        }
                    }
                    string meshNote = ", mesh vertices in the box " + vn + ": normal zero/NaN " + badN + ", normal.y <= 0 " + downN + " (min n.y " + minNy.ToString("0.###", Inv) + "), tangent parallel to normal " + par + " (max |dot| " + maxDot.ToString("0.###", Inv) + "), tiles without tangents " + noTan + (firstBad.Length > 0 ? ", first " + firstBad : "");
                    return "Cliffs308 holes: rays " + n_ + ", no terrain " + miss_ + (miss_ > 0 ? " in x " + F(mx0) + ".." + F(mx1) + " z " + F(mz0) + ".." + F(mz1) : "") + ", off the field " + off_ + ", tiles " + string.Join(",", names) + meshNote + "\n  renderers: " + string.Join("\n    ", rlist);
                }
                if (c.StartsWith("lodforce:", StringComparison.Ordinal))
                {
                    // lodforce:<level | -1>  every terrain tile's LODGroup is forced to that level (-1 = back to automatic). Not serialized: an experiment for stills.
                    int lv = int.Parse(c.Substring(9), Inv); int n_ = 0; var terr_ = SceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == "Reworld292_Terrain");
                    foreach (var lg in terr_.GetComponentsInChildren<LODGroup>(true)) { lg.ForceLOD(lv); n_++; }
                    return "Cliffs308 lodforce " + lv + " on " + n_ + " terrain LOD groups (scene dirty " + SceneManager.GetActiveScene().isDirty + ")";
                }
                if (c.StartsWith("lod:", StringComparison.Ordinal))
                {
                    // lod:<eye x>,<eye y>,<eye z>:<fov>  which LOD each named terrain tile would draw from that eye (LODGroup rule: size / distance / (2 tan(fov / 2)) x lodBias)
                    var a_ = c.Substring(4).Split(':'); var e_ = a_[0].Split(',').Select(v => float.Parse(v, Inv)).ToArray(); float fov = float.Parse(a_[1], Inv); var eye = new Vector3(e_[0], e_[1], e_[2]);
                    var sb = new StringBuilder("Cliffs308 lod: lodBias " + F(QualitySettings.lodBias) + ", maximumLODLevel " + QualitySettings.maximumLODLevel + ", quality " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
                    var terr = SceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == "Reworld292_Terrain");
                    foreach (Transform tile in terr.transform)
                    {
                        var lg = tile.GetComponent<LODGroup>() ?? tile.GetComponentInChildren<LODGroup>(true); if (lg == null) continue; var wp = lg.transform.TransformPoint(lg.localReferencePoint); float dist = Vector3.Distance(wp, eye); if (dist > 900f) continue;
                        float size = lg.size * Mathf.Max(lg.transform.lossyScale.x, lg.transform.lossyScale.y, lg.transform.lossyScale.z), rel = size / (dist * 2f * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad)) * QualitySettings.lodBias; var lods = lg.GetLODs(); int k = 0; while (k < lods.Length && rel < lods[k].screenRelativeTransitionHeight) k++;
                        sb.Append("\n  ").Append(tile.name).Append(": size ").Append(F(size)).Append(", ref ").Append(F(wp.x)).Append(",").Append(F(wp.y)).Append(",").Append(F(wp.z)).Append(", dist ").Append(F(dist)).Append(", relative height ").Append(rel.ToString("0.###", Inv)).Append(" -> LOD").Append(k).Append(k >= lods.Length ? " (culled)" : "").Append(", fade ").Append(lg.fadeMode).Append(", enabled ").Append(lg.enabled);
                    }
                    return sb.ToString();
                }
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "REFUSED scripts are compiling or failed to compile";
                switch (c)
                {
                    case "strata:apply": return Strata(true);
                    case "strata:revert": return Strata(false);
                    case "ways:build": return Ways(true);
                    case "ways:clear": return Ways(false);
                    case "ways:check": return WaysCheck();
                    case "sky:check": return SkyCheck();
                    case "plants:plan": return Plants("plan");
                    case "plants:apply": return Plants("apply");
                    case "plants:revert": return Plants("revert");
                    case "rocks:build": return Rocks(true);
                    case "rocks:clear": return Rocks(false);
                    case "rocks:reseat": return OldRocks(true);
                    case "rocks:unseat": return OldRocks(false);
                    case "flora:reseat": return Flora(true);
                    case "flora:restore": return Flora(false);
                }
                return "REFUSED unknown command '" + c + "' (" + Usage + ")";
            }
            catch (Exception e) { return "REFUSED " + e.Message; }
        }

        static Config ReadConfig() => JsonUtility.FromJson<Config>(File.ReadAllText(Path.Combine(StageDir, "cliffs308_editor.json")));
        static string F(float v) => v.ToString("F2", Inv);

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
        static GameObject Root(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // the ground material's rock / debris field re-derived for the stage (python Tools/Art/strata308.py Tools/Unity/Stage308_cliffs/cliffs308.json)
        const string GroundMaterial = "Assets/_Project/Art/World/Architecture296/Materials/554e75388d01b1b47ac1543a7e0617b0_KoreanGround.mat", FieldProperty = "_StrataField276";
        static string StrataLedger => Path.Combine(StageDir, "Ledger", "strata_before.txt");
        static string Strata(bool apply)
        {
            var cfg = ReadConfig(); var mat = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterial); if (mat == null) throw new Exception("ground material not found: " + GroundMaterial);
            string now = AssetDatabase.GetAssetPath(mat.GetTexture(FieldProperty));
            if (!apply)
            {
                if (!File.Exists(StrataLedger)) return "Cliffs308 strata:revert: nothing recorded (field is " + now + ")";
                string was = File.ReadAllText(StrataLedger).Trim(); var before = AssetDatabase.LoadAssetAtPath<Texture2D>(was); if (before == null) throw new Exception("recorded field texture is gone: " + was);
                mat.SetTexture(FieldProperty, before); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); File.Delete(StrataLedger);
                return "Cliffs308 strata:revert: " + FieldProperty + " " + now + " -> " + was;
            }
            string file = "surface_" + cfg.stage + ".png", src = Path.Combine(RepoRoot, "Art", "World", "Compact", "Rebuild", "CliffBoundary308", "plan", "Stage", file), dst = SurfaceDir + "/" + file;
            if (!File.Exists(src)) throw new Exception("no " + src + " (run python Tools/Art/strata308.py Tools/Unity/Stage308_cliffs/cliffs308.json)");
            if (string.IsNullOrEmpty(now)) throw new Exception("the ground material has no " + FieldProperty + " texture to copy import settings from");
            bool again = now == dst; if (again) { if (!File.Exists(StrataLedger)) throw new Exception("the field is already " + dst + " and no ledger names what was there before"); now = File.ReadAllText(StrataLedger).Trim(); }
            File.Copy(src, Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), dst), true); AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            var si = AssetImporter.GetAtPath(now) as TextureImporter; var di = AssetImporter.GetAtPath(dst) as TextureImporter; if (si == null || di == null) throw new Exception("texture importer missing (" + now + " -> " + dst + ")");
            var settings = new TextureImporterSettings(); si.ReadTextureSettings(settings); di.SetTextureSettings(settings);
            di.textureCompression = si.textureCompression; di.maxTextureSize = si.maxTextureSize; di.SetPlatformTextureSettings(si.GetDefaultPlatformTextureSettings());
            var standalone = si.GetPlatformTextureSettings("Standalone"); if (standalone.overridden) di.SetPlatformTextureSettings(standalone);
            di.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dst); if (tex == null) throw new Exception("import failed: " + dst);
            Directory.CreateDirectory(Path.GetDirectoryName(StrataLedger)); File.WriteAllText(StrataLedger, now);
            mat.SetTexture(FieldProperty, tex); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
            return "Cliffs308 strata:apply: " + FieldProperty + " " + now + " -> " + dst + " (" + tex.width + "x" + tex.height + " " + tex.format + ", sRGB " + di.sRGBTexture + ")";
        }

        static string Status()
        {
            var sb = new StringBuilder("Cliffs308 status\n"); var cfg = ReadConfig(); var scene = SceneManager.GetActiveScene();
            sb.Append("  stage ").Append(cfg.stage).Append(", active scene ").Append(scene.name).Append(scene.isDirty ? "*" : "").Append('\n');
            foreach (string name in new[] { WaysRoot, RocksRoot, cfg.oldRocksRoot })
            {
                var g = Root(scene, name); sb.Append("  ").Append(name).Append(": ");
                if (g == null) { sb.Append("absent\n"); continue; }
                int off = 0; foreach (Transform t in g.transform) if (!t.gameObject.activeSelf) off++;
                sb.Append("children ").Append(g.transform.childCount).Append(" (off ").Append(off).Append("), renderers ").Append(g.GetComponentsInChildren<Renderer>(true).Length).Append(", colliders ").Append(g.GetComponentsInChildren<Collider>(true).Length).Append('\n');
            }
            sb.Append("  old rocks ledger: ").Append(File.Exists(OldRocksLedger) ? "present" : "-").Append(", flora backup: ").Append(Directory.Exists(FloraBackupDir) ? "present" : "-");
            return sb.ToString();
        }

        // normals are normalised here: a zero-length normal lights as NaN (white bloom over the whole view)
        static Mesh MakeMesh(MeshData d, out string[] slots)
        {
            int n = d.vertices.Length / 3; var v = new Vector3[n]; var nn = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(d.vertices[3 * i], d.vertices[3 * i + 1], d.vertices[3 * i + 2]);
                var nr = new Vector3(d.normals[3 * i], d.normals[3 * i + 1], d.normals[3 * i + 2]); nn[i] = nr.sqrMagnitude > 1e-8f ? nr.normalized : Vector3.up;
                uv[i] = new Vector2(d.uvs[2 * i], d.uvs[2 * i + 1]);
            }
            var mesh = new Mesh { name = d.name, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetNormals(nn); mesh.SetUVs(0, uv); mesh.subMeshCount = d.submeshes.Length;
            for (int s = 0; s < d.submeshes.Length; s++) mesh.SetTriangles(d.submeshes[s].triangles, s, false);
            mesh.RecalculateBounds(); mesh.RecalculateTangents(); slots = d.submeshes.Select(s => s.material).ToArray(); return mesh;
        }

        static Material StoneFor(Config cfg, string realm)
        {
            var tint = cfg.stoneTint.FirstOrDefault(t => t.realm == realm); var src = AssetDatabase.LoadAssetAtPath<Material>(cfg.stoneSource);
            if (src == null) throw new Exception("stone source material missing: " + cfg.stoneSource);
            if (tint == null || tint.color.Length < 3) return src;
            Folder(AssetDir + "/Materials"); string path = AssetDir + "/Materials/stone_" + realm + ".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(src) { name = "stone_" + realm }; AssetDatabase.CreateAsset(mat, path); } else mat.CopyPropertiesFromMaterial(src);
            var col = new Color(tint.color[0], tint.color[1], tint.color[2], 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col); if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
            mat.DisableKeyword("_EMISSION"); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); return mat;
        }

        static string RealmAt(Config cfg, float x, float z)
        {
            foreach (var r in cfg.realms)
            {
                bool inside = false; int n = r.poly.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    float xi = r.poly[2 * i], zi = r.poly[2 * i + 1], xj = r.poly[2 * j], zj = r.poly[2 * j + 1];
                    if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
                }
                if (inside) return r.id;
            }
            return "";
        }

        // dressing rocks take the realm's rock colour: this tool's own copy of the prefab's rock material with _BaseColor set (no emission)
        static int TintRock(Config cfg, GameObject inst, string realm, Dictionary<string, Material> cache)
        {
            var tint = cfg.rockTint.FirstOrDefault(t => t.realm == realm); if (tint == null || tint.color.Length < 3) return 0; int n = 0;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials; bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i]; if (src == null || !src.HasProperty("_BaseColor")) continue; string srcPath = AssetDatabase.GetAssetPath(src); if (srcPath.StartsWith(AssetDir + "/Materials/", StringComparison.Ordinal)) continue;
                    string key = realm + "|" + srcPath;
                    if (!cache.TryGetValue(key, out var mat))
                    {
                        Folder(AssetDir + "/Materials"); string path = AssetDir + "/Materials/rock_" + realm + "_" + Path.GetFileNameWithoutExtension(srcPath) + ".mat"; mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (mat == null) { mat = new Material(src) { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(mat, path); } else mat.CopyPropertiesFromMaterial(src);
                        mat.SetColor("_BaseColor", new Color(tint.color[0], tint.color[1], tint.color[2], 1f)); mat.DisableKeyword("_EMISSION"); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); cache[key] = mat;
                    }
                    mats[i] = mat; any = true;
                }
                if (any) { r.sharedMaterials = mats; n++; }
            }
            return n;
        }

        static string Ways(bool build)
        {
            var scene = PlayScene(); var old = Root(scene, WaysRoot);
            if (!build)
            {
                if (old == null) return "Cliffs308 ways:clear: root absent";
                string b0 = Backup(scene, "ways_clear"); UnityEngine.Object.DestroyImmediate(old); Save(scene); return "Cliffs308 ways:clear: root removed, backup " + b0;
            }
            var cfg = ReadConfig(); string dir = Path.Combine(WaysData, "Meshes");
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "Way_*.json").OrderBy(x => x).ToArray() : Array.Empty<string>();
            if (files.Length == 0) throw new Exception("no mesh json under " + dir + " (run python Tools/Art/cliffs308.py build ...)");
            string layFile = Path.Combine(WaysData, "layout.json"); if (!File.Exists(layFile)) throw new Exception("no " + layFile);
            var lay = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(layFile)); var realmOf = lay.ways.ToDictionary(w => w.id, w => w.realm);
            var route = new RouteFile { ways = lay.ways.Select(Dense).ToArray() }; foreach (var w in route.ways) w.realm = realmOf[w.id]; string backup = Backup(scene, "ways");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            Folder(AssetDir + "/Meshes");
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { AssetDir + "/Meshes" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            var root = new GameObject(WaysRoot); SceneManager.MoveGameObjectToScene(root, scene); int drawn = 0, colliders = 0, boxes = 0; long vertices = 0; var sb = new StringBuilder();
            foreach (var way in route.ways)
            {
                var holder = new GameObject(way.id); holder.transform.SetParent(root.transform, false); var renderers = new List<Renderer>(); Material stone = null;
                foreach (string file in files.Where(f => Path.GetFileName(f).StartsWith("Way_" + way.id + "_", StringComparison.Ordinal)))
                {
                    var d = JsonUtility.FromJson<MeshData>(File.ReadAllText(file)); var mesh = MakeMesh(d, out var slots);
                    AssetDatabase.CreateAsset(mesh, AssetDir + "/Meshes/" + d.name + ".asset"); AssetDatabase.SaveAssetIfDirty(mesh); vertices += mesh.vertexCount;
                    var g = new GameObject(d.name.Substring(("Way_" + way.id + "_").Length)); g.transform.SetParent(holder.transform, false);
                    if (d.name.EndsWith("_Collision", StringComparison.Ordinal)) { var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = false; mc.isTrigger = false; colliders++; continue; }
                    if (slots.Contains("stone_rough") && stone == null) stone = StoneFor(cfg, way.realm);
                    var mats = slots.Select(s => s == "stone_rough" ? stone : AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + s + ".mat")).ToArray();
                    if (mats.Any(m => m == null)) throw new Exception("material missing under " + MaterialDir + ": " + string.Join(",", slots));
                    g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterials = mats; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; renderers.Add(r); drawn++;
                }
                if (renderers.Count > 0)
                {
                    var lod = holder.AddComponent<LODGroup>(); lod.SetLODs(new[] { new LOD(cfg.lodCullScreenHeight, renderers.ToArray()) }); lod.RecalculateBounds();
                }
                // NavMesh: the way is the player's alone. Boxes of area 1 (not walkable) along the centreline; the next bake leaves no NavMesh on the shelves or decks.
                float next = 0f; int n = Mathf.Min(way.x.Length, way.s.Length);
                for (int k = 0; k + 1 < n; k++)
                {
                    if (way.s[k] < next) continue;
                    int e = k; while (e + 1 < n && way.s[e + 1] - way.s[k] < cfg.navBoxEveryM) e++;
                    if (e == k) break;
                    var a = new Vector3(way.x[k], way.y[k], way.z[k]); var b = new Vector3(way.x[e], way.y[e], way.z[e]); var flat = new Vector3(b.x - a.x, 0f, b.z - a.z); if (flat.sqrMagnitude < 0.01f) { next = way.s[e]; continue; }
                    var box = new GameObject("NavNotWalkable_" + boxes); box.transform.SetParent(holder.transform, false); box.transform.position = new Vector3((a.x + b.x) / 2f, Mathf.Min(a.y, b.y), (a.z + b.z) / 2f); box.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
                    var vol = box.AddComponent<NavMeshModifierVolume>(); vol.area = 1; float h = Mathf.Abs(b.y - a.y) + cfg.navBoxUpM + cfg.navBoxDownM;
                    vol.size = new Vector3(cfg.navBoxWidthM, h, flat.magnitude + 2f); vol.center = new Vector3(0f, h / 2f - cfg.navBoxDownM, 0f); boxes++; next = way.s[e];
                }
                sb.Append("\n  ").Append(way.id).Append(" (").Append(way.realm).Append("): renderers ").Append(renderers.Count);
            }
            Save(scene);
            return "Cliffs308 ways:build: " + route.ways.Length + " ways, " + drawn + " drawn meshes, " + colliders + " colliders, " + boxes + " nav boxes, " + vertices + " vertices, root " + WaysRoot + ", backup " + backup + sb;
        }

        // physics along each way (edit mode ray casts on the scene colliders): is there ground under every half metre of the centreline, how far is it
        // from the planned height, how big is the largest step, and is the walked strip wide enough (ground within 0.45 m of the centre 0.8 m to either side)
        static string WaysCheck()
        {
            var cfg = ReadConfig(); var scene = SceneManager.GetActiveScene(); if (scene.name != PlaySceneName) throw new Exception("open " + PlaySceneName + " first");
            var lay = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(Path.Combine(WaysData, "layout.json"))); var route = new RouteFile { ways = lay.ways.Select(Dense).ToArray() };
            Physics.SyncTransforms(); var sb = new StringBuilder("Cliffs308 ways:check (ray casts every 0.5 m of the walked line, Edit mode; PASS = ground everywhere, step <= 0.25 m, normal < 35 deg, headroom 1.9 m, width 1.6 m; the motor walks 45 deg and steps 0.3 m - cb308.json probe.capsuleSlope / capsuleStep)"); int fails = 0;
            foreach (var way in route.ways)
            {
                int n = way.x.Length, miss = 0, narrow = 0, samples = 0, steep = 0, low = 0; float maxOff = 0f, maxStep = 0f, prevY = float.NaN, maxSlope = 0f; string worst = ""; var hits = new Dictionary<string, int>(); var notes = new List<string>(); var narrowNotes = new List<string>();
                for (int k = 0; k < n; k++)
                {
                    var p = new Vector3(way.x[k], way.y[k], way.z[k]); samples++;
                    if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) { miss++; prevY = float.NaN; continue; }
                    if (Physics.Raycast(hit.point + Vector3.up * 0.3f, Vector3.up, 1.6f, ~0, QueryTriggerInteraction.Ignore)) low++;
                    float off = hit.point.y - p.y; if (Mathf.Abs(off) > maxOff) { maxOff = Mathf.Abs(off); worst = "s " + F(way.s[k]) + " (" + F(p.x) + "," + F(p.z) + ") ground " + F(hit.point.y) + " plan " + F(p.y) + " on " + hit.collider.name; }
                    string top = hit.collider.transform.root.name; hits[top] = hits.TryGetValue(top, out int c) ? c + 1 : 1;
                    if (!float.IsNaN(prevY)) { float st = Mathf.Abs(hit.point.y - prevY); maxStep = Mathf.Max(maxStep, st); if (st > 0.25f && notes.Count < 6) notes.Add("step " + F(st) + " at s " + F(way.s[k]) + " (" + F(p.x) + "," + F(p.y) + "," + F(p.z) + ") hit y " + F(hit.point.y) + " n.y " + F(hit.normal.y)); } prevY = hit.point.y;
                    float slope = Vector3.Angle(hit.normal, Vector3.up); maxSlope = Mathf.Max(maxSlope, slope); if (slope >= 35f) steep++;
                    int k2 = Mathf.Min(n - 1, k + 1), k1 = Mathf.Max(0, k - 1); var t = new Vector3(way.x[k2] - way.x[k1], 0f, way.z[k2] - way.z[k1]).normalized; var side = new Vector3(-t.z, 0f, t.x); bool wide = true;
                    string why = "";
                    foreach (float o in new[] { -0.8f, 0.8f })
                    {
                        bool got = Physics.Raycast(hit.point + side * o + Vector3.up * 1.2f, Vector3.down, out var h2, 2.4f, ~0, QueryTriggerInteraction.Ignore);
                        if (!got || Mathf.Abs(h2.point.y - hit.point.y) > 0.45f) { wide = false; why += " [" + F(o) + (got ? " dy " + F(h2.point.y - hit.point.y) + " " + h2.collider.transform.root.name : " nothing") + "]"; }
                    }
                    if (!wide) { narrow++; if (narrowNotes.Count < 5) narrowNotes.Add("s " + F(way.s[k]) + why); }
                }
                bool ok = miss == 0 && maxStep <= 0.25f && steep == 0 && low == 0 && narrow == 0; if (!ok) fails++;
                sb.Append("\n  ").Append(ok ? "PASS " : "FAIL ").Append(way.id).Append(": stations ").Append(samples).Append(", no ground ").Append(miss).Append(", ground vs plan max ").Append(F(maxOff)).Append(" m [").Append(worst).Append("], largest step between stations ").Append(F(maxStep))
                  .Append(" m, steepest normal ").Append(F(maxSlope)).Append(" deg (>= 35: ").Append(steep).Append("), headroom under 1.9 m ").Append(low).Append(", stations narrower than 1.6 m ").Append(narrow).Append(", standing on ").Append(string.Join(" ", hits.Select(h => h.Key + " " + h.Value)));
                if (notes.Count > 0) sb.Append("\n      ").Append(string.Join("; ", notes)); if (narrowNotes.Count > 0) sb.Append("\n      narrow: ").Append(string.Join("; ", narrowNotes));
            }
            return sb.Append("\n  checks " + route.ways.Length + ", FAIL " + fails).ToString();
        }

        // the walked line of the layout, one station every 0.5 m
        static WayRoute Dense(WalkWay w)
        {
            var xs = new List<float>(); var ys = new List<float>(); var zs = new List<float>(); var ss = new List<float>(); float acc = 0f;
            for (int k = 0; k + 1 < w.walkX.Length; k++)
            {
                var a = new Vector3(w.walkX[k], w.walkY[k], w.walkZ[k]); var b = new Vector3(w.walkX[k + 1], w.walkY[k + 1], w.walkZ[k + 1]); float len = Vector3.Distance(a, b); int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.5f));
                for (int i = 0; i < steps; i++) { var p = Vector3.Lerp(a, b, i / (float)steps); xs.Add(p.x); ys.Add(p.y); zs.Add(p.z); ss.Add(acc + len * i / steps); }
                acc += len;
            }
            return new WayRoute { id = w.id, x = xs.ToArray(), y = ys.ToArray(), z = zs.ToArray(), s = ss.ToArray() };
        }

        // sky through terrain: (a) straight down on every changed cell (4 points per cell) the collider must be where the field is;
        // (b) from ground-level eyes 30-90 m away (8 bearings, eye 1.7 m over the field) a ray aimed 1.5 m UNDER the surface of the changed cell must be
        // stopped by a collider before it gets there. Mesh colliders are one-sided like the drawn mesh, so a ray that slips through a crack or
        // past a back face arrives - exactly what the camera would see as sky. (c) the drawn LOD0 mesh of each changed tile must be the collider mesh.
        static string SkyCheck()
        {
            var cfg = ReadConfig(); var scene = SceneManager.GetActiveScene(); if (scene.name != PlaySceneName) throw new Exception("open " + PlaySceneName + " first");
            var from = Field(cfg.floraFrom); var to = Field(cfg.floraTo); Physics.SyncTransforms(); int cells = 0, down = 0, downBad = 0, rays = 0, through = 0, slotRays = 0, slotSky = 0; var bad = new List<string>(); var badDown = new List<string>(); var badSlot = new List<string>();
            var terrain = scene.GetRootGameObjects().First(g => g.name == "Reworld292_Terrain"); int mask = ~0;
            for (int i = 2; i < 1499; i++)
                for (int j = 2; j < 999; j++)
                {
                    if (Mathf.Abs(to[i * 1001 + j] - from[i * 1001 + j]) < 0.05f) continue; cells++;
                    for (int q = 0; q < 4; q++)
                    {
                        float x = j * 4f + (q % 2 == 0 ? -1.3f : 1.3f), z = i * 4f + (q < 2 ? -1.3f : 1.3f), want = Sample(to, x, z); down++;
                        bool hit = false; float best = float.NegativeInfinity;
                        foreach (var h in Physics.RaycastAll(new Vector3(x, want + 60f, z), Vector3.down, 120f, mask, QueryTriggerInteraction.Ignore)) if (h.collider.transform.IsChildOf(terrain.transform)) { hit = true; best = Mathf.Max(best, h.point.y); }
                        if (!hit || Mathf.Abs(best - want) > 0.3f) { downBad++; if (badDown.Count < 10) badDown.Add("(" + F(x) + "," + F(z) + ") field " + F(want) + (hit ? " terrain " + F(best) : " NO TERRAIN")); }
                    }
                    if ((i + j) % 3 != 0) continue;
                    float cx = j * 4f, cz = i * 4f; var target = new Vector3(cx, to[i * 1001 + j] - 1.5f, cz);
                    // (d) a slot: the ray passes 2 m over this cell, at least 6 m under the rim that stands within 24 m, with higher ground within 8 m on BOTH sides (a gap up to 16 m = about four cells) of it, and meets nothing behind
                    float rim = float.NegativeInfinity; for (int di = -6; di <= 6; di++) for (int dj = -6; dj <= 6; dj++) rim = Mathf.Max(rim, to[(i + di) * 1001 + j + dj]);
                    var over = new Vector3(cx, to[i * 1001 + j] + 2f, cz); bool underRim = over.y < rim - 6f;
                    if (underRim)
                    {
                        bool seen = false;
                        for (int q = 0; q < 8 && !seen; q++)
                        {
                            float a2 = q * Mathf.PI / 8f; var ax = new Vector3(Mathf.Cos(a2), 0f, Mathf.Sin(a2)); var side = new Vector3(-ax.z, 0f, ax.x); bool left = false, right = false;
                            for (float o = 3f; o <= 8f; o += 1f) { if (Sample(to, cx + side.x * o, cz + side.z * o) > over.y + 6f) left = true; if (Sample(to, cx - side.x * o, cz - side.z * o) > over.y + 6f) right = true; }
                            if (!left || !right) continue;
                            slotRays++;
                            for (int sgn = -1; sgn <= 1 && !seen; sgn += 2)
                            {
                                // an eye on ground 12 - 150 m down the slot on one side, and nothing at all behind the cell on the other side
                                if (Physics.Raycast(over, ax * sgn, 400f, mask, QueryTriggerInteraction.Ignore)) continue;
                                if (Physics.Raycast(over, (-ax * sgn + Vector3.down * 0.12f).normalized, out var he, 150f, mask, QueryTriggerInteraction.Ignore) && he.distance >= 12f && he.normal.y >= 0.7f)
                                { seen = true; slotSky++; if (badSlot.Count < 14) badSlot.Add("cell (" + F(cx) + "," + F(over.y) + "," + F(cz) + ") rim " + F(rim) + " seen from (" + F(he.point.x) + "," + F(he.point.y) + "," + F(he.point.z) + ")"); }
                            }
                        }
                    }
                    for (int b = 0; b < 8; b++)
                        foreach (float dist in new[] { 30f, 60f, 90f })
                        {
                            float a = b * Mathf.PI / 4f + 0.2f, ex = cx + Mathf.Cos(a) * dist, ez = cz + Mathf.Sin(a) * dist; if (ex < 8f || ez < 8f || ex > 3992f || ez > 5992f) continue;
                            var eye = new Vector3(ex, Sample(to, ex, ez) + 1.7f, ez); if (eye.y > target.y + dist * 1.2f) continue;      // looking steeply down from far above is not a ground view
                            var dir = target - eye; float len = dir.magnitude; rays++;
                            if (Physics.Raycast(eye, dir / len, len - 0.05f, mask, QueryTriggerInteraction.Ignore)) continue;
                            if (Physics.CheckSphere(eye, 0.05f, mask, QueryTriggerInteraction.Ignore)) continue;
                            through++; if (bad.Count < 14) bad.Add("eye (" + F(eye.x) + "," + F(eye.y) + "," + F(eye.z) + ") -> under (" + F(cx) + "," + F(target.y) + "," + F(cz) + ")");
                        }
                }
            int tiles = 0, split = 0; var splitNames = new List<string>();
            foreach (Transform tile in terrain.transform)
            {
                var col = tile.GetComponent<MeshCollider>(); if (col == null || col.sharedMesh == null) continue; tiles++;
                var lod0 = tile.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).OrderByDescending(f => f.sharedMesh.vertexCount).FirstOrDefault();
                if (lod0 != null && lod0.sharedMesh != col.sharedMesh) { split++; if (splitNames.Count < 8) splitNames.Add(tile.name + ": drawn " + lod0.sharedMesh.name + " / collider " + col.sharedMesh.name); }
            }
            // (e) seams: every drawn mesh of every tile, its vertices on a 500 m tile border; two tiles must put the same height on a shared border point.
            // Compared within the same LOD rank (0 = finest) and against the stage field for rank 0.
            int seamPoints = 0, seamOff = 0, edgeOffField = 0; float seamWorst = 0f; var seamNotes = new List<string>(); var edge = new Dictionary<long, (float y, string who)>[3]; for (int q = 0; q < 3; q++) edge[q] = new Dictionary<long, (float, string)>();
            foreach (Transform tile in terrain.transform)
            {
                var fs = tile.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).OrderByDescending(f => f.sharedMesh.vertexCount).Take(3).ToArray();
                for (int rank = 0; rank < fs.Length; rank++)
                {
                    var m = fs[rank].transform.localToWorldMatrix; var vs = fs[rank].sharedMesh.vertices;
                    foreach (var v0 in vs)
                    {
                        var w = m.MultiplyPoint3x4(v0); float fx = Mathf.Repeat(w.x + 0.01f, 500f), fz = Mathf.Repeat(w.z + 0.01f, 500f); if (fx > 0.02f && fz > 0.02f) continue;
                        long key = (long)Mathf.RoundToInt(w.x * 100f) * 10000000L + Mathf.RoundToInt(w.z * 100f);
                        if (rank == 0 && w.x >= 0f && w.x <= 4000f && w.z >= 0f && w.z <= 6000f && Mathf.Abs(Mathf.Repeat(w.x + 0.01f, 4f)) < 0.02f && Mathf.Abs(Mathf.Repeat(w.z + 0.01f, 4f)) < 0.02f)
                        {
                            float want = to[Mathf.RoundToInt(w.z / 4f) * 1001 + Mathf.RoundToInt(w.x / 4f)];
                            if (Mathf.Abs(want - w.y) > 0.05f) { edgeOffField++; if (seamNotes.Count < 10) seamNotes.Add(tile.name + " " + fs[rank].sharedMesh.name + " (" + F(w.x) + "," + F(w.z) + ") mesh " + F(w.y) + " field " + F(want)); }
                        }
                        if (edge[rank].TryGetValue(key, out var other))
                        {
                            if (other.who == tile.name) continue; seamPoints++; float dy = Mathf.Abs(other.y - w.y);
                            if (dy > 0.05f) { seamOff++; if (dy > seamWorst) seamWorst = dy; if (seamNotes.Count < 10) seamNotes.Add("LOD" + rank + " " + other.who + " | " + tile.name + " (" + F(w.x) + "," + F(w.z) + ") " + F(other.y) + " vs " + F(w.y)); }
                        }
                        else edge[rank][key] = (w.y, tile.name);
                    }
                }
            }
            var lg = terrain.GetComponentsInChildren<LODGroup>(true).FirstOrDefault(); string lods = lg == null ? "no LODGroup" : string.Join(" / ", lg.GetLODs().Select(l => l.screenRelativeTransitionHeight.ToString("0.###", Inv)));
            bool ok = downBad == 0 && through == 0 && split == 0 && slotSky == 0 && seamOff == 0 && edgeOffField == 0;
            return "Cliffs308 sky:check " + (ok ? "PASS" : "FAIL") + ": changed cells " + cells + ", down probes " + down + " off the field " + downBad + ", ground-view rays " + rays + " that reach under the surface " + through + ", slot axes (2 m over a cell, 6 m under the rim within 24 m, higher ground within 8 m on both sides, 8 bearings) " + slotRays + ", cells where ground on one side of the slot looks through to the sky on the other " + slotSky + ", shared tile-border points " + seamPoints + " that disagree " + seamOff + " (worst " + F(seamWorst) + " m), finest-mesh border vertices off the stage field " + edgeOffField + ", tiles " + tiles + " with drawn LOD0 != collider " + split + ", tile LOD transitions " + lods
                + (badDown.Count > 0 ? "\n  down: " + string.Join("; ", badDown) : "") + (seamNotes.Count > 0 ? "\n  seams: " + string.Join("\n    ", seamNotes) : "") + (badSlot.Count > 0 ? "\n  slot sky: " + string.Join("\n    ", badSlot) : "") + (bad.Count > 0 ? "\n  through: " + string.Join("\n    ", bad) : "") + (splitNames.Count > 0 ? "\n  " + string.Join("; ", splitNames) : "") + "\n  checks 6, FAIL " + ((downBad > 0 ? 1 : 0) + (through > 0 ? 1 : 0) + (split > 0 ? 1 : 0) + (slotSky > 0 ? 1 : 0) + (seamOff > 0 ? 1 : 0) + (edgeOffField > 0 ? 1 : 0));
        }

        // plants of the shared sheets (DryLandscape, Dressing - also used by the candidate scenes, re-made by the #296 bake) that stand within plantClearM of a
        // walked line: removed through the BuildingFix308.VegRows ledger (tag cliffs308), so "Veg replay" brings the removal back after a re-bake.
        static string Plants(string what)
        {
            var cfg = ReadConfig(); if (what == "revert") return BuildingFix308.VegRowsRevert("cliffs308", .05f);
            var lay = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(Path.Combine(WaysData, "layout.json"))); var lines = lay.ways.Select(Dense).ToArray(); var asks = new List<BuildingFix308.VegRowAsk308>();
            foreach (string path in cfg.sharedSheets)
            {
                var sheet = AssetDatabase.LoadAssetAtPath<Oheangbu.Data.World.WorldMacroDressingSheetSO>(path); if (sheet == null || sheet.FixedPlacements == null) continue;
                foreach (var fp in sheet.FixedPlacements)
                {
                    if (fp == null) continue; bool near = false;
                    foreach (var w in lines) { for (int k = 0; k < w.x.Length && !near; k += 2) { float dx = w.x[k] - fp.Position.x, dz = w.z[k] - fp.Position.z; if (dx * dx + dz * dz <= cfg.plantClearM * cfg.plantClearM) near = true; } if (near) break; }
                    if (near) asks.Add(new BuildingFix308.VegRowAsk308 { sheet = path, id = fp.Id ?? "", proto = fp.PrototypeId ?? "", at = fp.Position });
                }
            }
            if (asks.Count == 0) return "Cliffs308 plants:" + what + ": no shared-sheet plant within " + F(cfg.plantClearM) + " m of a walked line";
            return "Cliffs308 plants:" + what + " (" + asks.Count + " rows)\n" + (what == "apply" ? BuildingFix308.VegRowsApply("cliffs308", asks, .05f) : BuildingFix308.VegRowsPlan("cliffs308", asks, .05f));
        }

        static string Rocks(bool build)
        {
            var scene = PlayScene(); var old = Root(scene, RocksRoot);
            if (!build)
            {
                if (old == null) return "Cliffs308 rocks:clear: root absent";
                string b0 = Backup(scene, "rocks_clear"); UnityEngine.Object.DestroyImmediate(old); Save(scene); return "Cliffs308 rocks:clear: removed, backup " + b0;
            }
            string file = Path.Combine(StageDir, "Out", "rocks.json"); if (!File.Exists(file)) throw new Exception("no " + file + " (run python Tools/Art/cliffs308.py build ...)");
            var data = JsonUtility.FromJson<RockFile>(File.ReadAllText(file)); string backup = Backup(scene, "rocks");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject(RocksRoot); SceneManager.MoveGameObjectToScene(root, scene); var prefabs = new Dictionary<string, (GameObject go, Bounds b)>(); int placed = 0, skipped = 0; var kinds = new Dictionary<string, int>(); var cfg = ReadConfig(); var tintCache = new Dictionary<string, Material>(); int tinted = 0;
            foreach (var r in data.rocks)
            {
                if (!prefabs.TryGetValue(r.prefab, out var pf))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(data.prefabDir + r.prefab + ".prefab"); var b = new Bounds();
                    if (go != null) { var probe = (GameObject)PrefabUtility.InstantiatePrefab(go); var rs = probe.GetComponentsInChildren<Renderer>(); if (rs.Length > 0) { b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); } UnityEngine.Object.DestroyImmediate(probe); }
                    pf = (go, b); prefabs[r.prefab] = pf;
                }
                if (pf.go == null || pf.b.size.sqrMagnitude < 1e-4f) { skipped++; continue; }
                float scale = r.size / Mathf.Max(pf.b.size.x, pf.b.size.z, 0.01f); var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf.go, root.transform);
                inst.name = r.kind + "_" + placed; inst.transform.localScale = inst.transform.localScale * scale; inst.transform.rotation = Quaternion.Euler(r.tiltX, r.yaw, r.tiltZ);
                var rs2 = inst.GetComponentsInChildren<Renderer>(); var wb = rs2[0].bounds; foreach (var x in rs2) wb.Encapsulate(x.bounds);
                // seated: the block's lowest point goes sink * height under the ground height the planner measured on the stage field
                inst.transform.position += new Vector3(r.x - wb.center.x, r.y - r.sink * wb.size.y - wb.min.y, r.z - wb.center.z);
                if (inst.GetComponentsInChildren<Collider>().Length == 0)
                {
                    var mf = inst.GetComponentsInChildren<MeshFilter>().OrderByDescending(m => m.sharedMesh != null ? m.sharedMesh.vertexCount : 0).FirstOrDefault();
                    if (mf != null && mf.sharedMesh != null) { var bc = mf.gameObject.AddComponent<BoxCollider>(); bc.center = mf.sharedMesh.bounds.center; bc.size = mf.sharedMesh.bounds.size * 0.62f; }
                }
                tinted += TintRock(cfg, inst, r.realm, tintCache);                 kinds[r.kind] = kinds.TryGetValue(r.kind, out int kn) ? kn + 1 : 1; placed++;
            }
            Save(scene);
            return "Cliffs308 rocks:build: " + placed + " rocks, realm-tinted renderers " + tinted + " (" + skipped + " skipped; " + string.Join(", ", kinds.Select(k => k.Key + " " + k.Value)) + "), prefabs " + prefabs.Count(k => k.Value.go != null) + " of " + prefabs.Count + ", backup " + backup;
        }

        static float[] Field(string stage)
        {
            string path = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), SurfaceDir, "height_" + stage + ".bytes"); if (!File.Exists(path)) throw new Exception("no stage field " + path + " (apply CliffBoundary308 surface:" + stage + " first)");
            var bytes = File.ReadAllBytes(path); if (bytes.Length != 1001 * 1501 * 4) throw new Exception(path + " is not a 1001 x 1501 float field"); var f = new float[1001 * 1501]; Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length); return f;
        }
        // the tile MESH surface: each 4 m quad = triangles (00, 10, 01) and (11, 01, 10)
        static float Sample(float[] f, float x, float z)
        {
            float u = Mathf.Clamp(x / 4f, 0f, 999.999f), w = Mathf.Clamp(z / 4f, 0f, 1499.999f); int j = (int)u, i = (int)w; float fu = u - j, fw = w - i;
            float h00 = f[i * 1001 + j], h10 = f[i * 1001 + j + 1], h01 = f[(i + 1) * 1001 + j], h11 = f[(i + 1) * 1001 + j + 1];
            return fu + fw <= 1f ? h00 + (h10 - h00) * fu + (h01 - h00) * fw : h11 + (h01 - h11) * (1f - fu) + (h10 - h11) * (1f - fw);
        }

        static string OldRocks(bool reseat)
        {
            var cfg = ReadConfig(); var scene = PlayScene(); var root = Root(scene, cfg.oldRocksRoot); if (root == null) return "Cliffs308 rocks:" + (reseat ? "reseat" : "unseat") + ": root " + cfg.oldRocksRoot + " absent";
            if (!reseat)
            {
                if (!File.Exists(OldRocksLedger)) return "Cliffs308 rocks:unseat: nothing recorded";
                var led = JsonUtility.FromJson<OldRockLedger>(File.ReadAllText(OldRocksLedger)); string b0 = Backup(scene, "old_rocks_unseat"); int n0 = 0;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true)) if (PrefabUtility.IsPartOfPrefabInstance(mr)) PrefabUtility.RevertObjectOverride(mr, InteractionMode.AutomatedAction);
                foreach (var r in led.rocks) { if (r.index >= root.transform.childCount) continue; var t = root.transform.GetChild(r.index); var p = t.position; p.y = r.y; t.position = p; t.gameObject.SetActive(r.active); n0++; }
                Save(scene); File.Move(OldRocksLedger, OldRocksLedger + ".undone_" + DateTime.Now.ToString("yyyyMMddTHHmmss", Inv)); return "Cliffs308 rocks:unseat: " + n0 + " rocks put back, backup " + b0;
            }
            if (File.Exists(OldRocksLedger)) throw new Exception("already re-seated (rocks:unseat first): " + OldRocksLedger);
            var from = Field(cfg.floraFrom); var to = Field(cfg.floraTo); string backup = Backup(scene, "old_rocks"); var ledger = new OldRockLedger { root = cfg.oldRocksRoot, from = cfg.floraFrom, to = cfg.floraTo }; int moved = 0, buried = 0; float maxMove = 0f; var tintCache = new Dictionary<string, Material>(); int tintedOld = 0;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var t = root.transform.GetChild(i); var p = t.position; tintedOld += TintRock(cfg, t.gameObject, RealmAt(cfg, p.x, p.z), tintCache); float d = Sample(to, p.x, p.z) - Sample(from, p.x, p.z); if (Mathf.Abs(d) < 0.05f) continue;
                ledger.rocks.Add(new OldRock { index = i, y = p.y, active = t.gameObject.activeSelf });
                float sgx = (Sample(to, p.x + 2f, p.z) - Sample(to, p.x - 2f, p.z)) / 4f, sgz = (Sample(to, p.x, p.z + 2f) - Sample(to, p.x, p.z - 2f)) / 4f;
                if (d > cfg.oldRocksBuryM || Mathf.Sqrt(sgx * sgx + sgz * sgz) >= Mathf.Tan(cfg.oldRocksFaceDeg * Mathf.Deg2Rad)) { t.gameObject.SetActive(false); buried++; continue; }
                p.y += d; t.position = p; moved++; maxMove = Mathf.Max(maxMove, Mathf.Abs(d));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(OldRocksLedger)); File.WriteAllText(OldRocksLedger, JsonUtility.ToJson(ledger)); Save(scene);
            return "Cliffs308 rocks:reseat " + cfg.floraFrom + " -> " + cfg.floraTo + ": " + root.transform.childCount + " rocks, moved " + moved + " (max " + F(maxMove) + " m), switched off under a buttress " + buried + ", backup " + backup;
        }

        static string Flora(bool reseat)
        {
            var cfg = ReadConfig(); if (cfg.floraSheets.Length == 0) throw new Exception("config floraSheets is empty");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!reseat)
            {
                if (!Directory.Exists(FloraBackupDir)) return "Cliffs308 flora:restore: nothing recorded";
                int n = 0; foreach (string sheet in cfg.floraSheets) { string b = Path.Combine(FloraBackupDir, Path.GetFileName(sheet)); if (!File.Exists(b)) continue; File.Copy(b, Path.Combine(project, sheet), true); AssetDatabase.ImportAsset(sheet, ImportAssetOptions.ForceUpdate); n++; }
                Directory.Move(FloraBackupDir, FloraBackupDir + "_restored_" + DateTime.Now.ToString("yyyyMMddTHHmmss", Inv));
                return "Cliffs308 flora:restore: " + n + " sheets restored";
            }
            if (Directory.Exists(FloraBackupDir)) throw new Exception("already re-seated (flora:restore first): " + FloraBackupDir);
            var from = Field(cfg.floraFrom); var to = Field(cfg.floraTo); float tan = Mathf.Tan(cfg.floraRemoveSlopeDeg * Mathf.Deg2Rad); Directory.CreateDirectory(FloraBackupDir); var sb = new StringBuilder();
            foreach (string sheet in cfg.floraSheets)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(sheet); if (asset == null) throw new Exception("sheet missing: " + sheet);
                File.Copy(Path.Combine(project, sheet), Path.Combine(FloraBackupDir, Path.GetFileName(sheet)), true);
                var so = new SerializedObject(asset); var list = so.FindProperty("FixedPlacements"); if (list == null || !list.isArray) throw new Exception("no FixedPlacements in " + sheet);
                int moved = 0, removed = 0; float maxMove = 0f;
                for (int i = list.arraySize - 1; i >= 0; i--)
                {
                    var pos = list.GetArrayElementAtIndex(i).FindPropertyRelative("Position"); var p = pos.vector3Value; float d = Sample(to, p.x, p.z) - Sample(from, p.x, p.z);
                    if (Mathf.Abs(d) < 0.05f) continue;
                    float gx = (Sample(to, p.x + 2f, p.z) - Sample(to, p.x - 2f, p.z)) / 4f, gz = (Sample(to, p.x, p.z + 2f) - Sample(to, p.x, p.z - 2f)) / 4f;
                    if (Mathf.Sqrt(gx * gx + gz * gz) >= tan) { list.DeleteArrayElementAtIndex(i); removed++; continue; }
                    p.y += d; pos.vector3Value = p; moved++; maxMove = Mathf.Max(maxMove, Mathf.Abs(d));
                }
                so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
                sb.Append("\n  ").Append(Path.GetFileName(sheet)).Append(": placements ").Append(list.arraySize).Append(", re-seated ").Append(moved).Append(" (max ").Append(F(maxMove)).Append(" m), removed on faces ").Append(removed);
            }
            return "Cliffs308 flora:reseat " + cfg.floraFrom + " -> " + cfg.floraTo + ", backup " + FloraBackupDir + sb;
        }
    }
}
