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
    // #308 stage n1 (SPEC-WORLD-CLIFF-BOUNDARY-308 "n1 단계", SPEC-WORLD-CLIFFWAY-308 "n1 단계"): the cliffs become terrain only.
    // The terrain itself is applied by CliffBoundary308 (surface:n1, tiles:<scene>:stage=n1). This tool owns the rest:
    //   status                     what is applied, what the scenes hold
    //   strata:apply | strata:revert   the ground material's rock / debris field re-derived for the stage (Tools/Art/strata308.py) is copied
    //                              into Assets/_Project/Art/World/CliffBoundary308/Surface and the ground material is pointed at it
    //   gallery:build | gallery:clear  the wood gallery + chain rail of the cove cliff way (Tools/Art/gallery308.py): root Gallery308 in the play scene
    //   old:hide | old:show        the mesh cliffs laid over the terrain (CliffMass308, CliffWay308) are switched off / on (nothing is deleted)
    //   wall:lower | wall:restore  the wall wings of Jangseong308 are lowered by the config's wallLowerM (the gate keeps its height)
    // Numbers: Tools/Unity/Stage308_rugged/rugged308.json. Ledger: Tools/Unity/Stage308_rugged/Ledger/rugged308_ledger.json.
    // Queue-safe: no dialog, refusals come back as "REFUSED ...". Scenes are backed up before they are saved; AssetDatabase.SaveAssets is never called.
    public static class Rugged308
    {
        const string Usage = "trail:hide | trail:show | crag:tint | crag:revert | scan:<x0>,<z0>,<x1>,<z1> | flora:reseat | flora:restore | rocks:build | rocks:clear | status | strata:apply | strata:revert | gallery:build | gallery:clear | old:hide | old:show | wall:lower | wall:restore";
        const string PlaySceneName = "W_Demo_Main", GalleryRoot = "Gallery308", WallRoot = "Jangseong308";
        const string GroundMaterial = "Assets/_Project/Art/World/Architecture296/Materials/554e75388d01b1b47ac1543a7e0617b0_KoreanGround.mat";
        const string FieldProperty = "_StrataField276", SurfaceDir = "Assets/_Project/Art/World/CliffBoundary308/Surface";
        const string GalleryAssets = "Assets/_Project/Art/World/Gallery308", MaterialDir = "Assets/_Project/Art/World/Finish297/Materials/";
        static readonly string[] OldRoots = { "CliffMass308", "CliffWay308" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string StageDir => Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_rugged");
        static string LedgerFile => Path.Combine(StageDir, "Ledger", "rugged308_ledger.json");
        static string GalleryData => Path.Combine(RepoRoot, "Art", "World", "Compact", "Rebuild", "Gallery308");

        [Serializable] sealed class FloraRule { public string from = "", to = ""; public float removeSlopeDeg = 40f; public string[] sheets = Array.Empty<string>(); }
        [Serializable] sealed class Config { public string stage = ""; public float wallLowerM; public FloraRule flora = new FloraRule(); public string[] pathFaceSites = Array.Empty<string>(); public CragTint crag = new CragTint(); }
        [Serializable] sealed class CragTint { public string source = "", facePrefix = ""; public float[] baseColor = Array.Empty<float>(), farTone = Array.Empty<float>(), value = Array.Empty<float>(); public float chroma = -1f, moss = -1f; }
        [Serializable] sealed class Rock { public string prefab = ""; public float x, y, z, yaw, size, sink, tiltX, tiltZ; }
        [Serializable] sealed class RockFile { public string prefabDir = ""; public Rock[] rocks = Array.Empty<Rock>(); }
        [Serializable] sealed class ScanOut { public float x0, z0, cell; public int cols, rows; public int[] occupied = Array.Empty<int>(); public string[] roots = Array.Empty<string>(); }
        [Serializable] sealed class Hidden { public string scene = "", path = ""; }
        [Serializable] sealed class Lowered { public string name = ""; public float before; }
        [Serializable] sealed class Ledger { public string strataBefore = "", strataNow = ""; public List<Hidden> hidden = new List<Hidden>(); public List<Lowered> lowered = new List<Lowered>(); public float wallLoweredM; public string cragBefore = ""; }
        [Serializable] sealed class Sub { public string material = ""; public int[] triangles = Array.Empty<int>(); }
        [Serializable] sealed class MeshData { public string name = ""; public float[] vertices = Array.Empty<float>(), normals = Array.Empty<float>(), uvs = Array.Empty<float>(); public Sub[] submeshes = Array.Empty<Sub>(); }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return Status();
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "REFUSED scripts are compiling or failed to compile";
                switch (c)
                {
                    case "strata:apply": return Strata(true);
                    case "strata:revert": return Strata(false);
                    case "gallery:build": return GalleryBuild();
                    case "gallery:clear": return GalleryClear();
                    case "old:hide": return Old(true);
                    case "old:show": return Old(false);
                    case "wall:lower": return Wall(true);
                    case "wall:restore": return Wall(false);
                    case "flora:reseat": return Flora(true);
                    case "flora:restore": return Flora(false);
                    case "rocks:build": return Rocks(true);
                    case "rocks:clear": return Rocks(false);
                    case "crag:tint": return Crag(true);
                    case "trail:hide": return Trail(true);
                    case "trail:show": return Trail(false);
                    case "crag:revert": return Crag(false);
                }
                if (c.StartsWith("scan:", StringComparison.Ordinal)) return Scan(c.Substring(5));
                return "REFUSED unknown command '" + c + "' (" + Usage + ")";
            }
            catch (Exception e) { return "REFUSED " + e.Message; }
        }

        static Config ReadConfig() => JsonUtility.FromJson<Config>(File.ReadAllText(Path.Combine(StageDir, "rugged308.json")));
        static Ledger ReadLedger() => File.Exists(LedgerFile) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerFile)) : new Ledger();
        static void WriteLedger(Ledger l) { Directory.CreateDirectory(Path.GetDirectoryName(LedgerFile)); File.WriteAllText(LedgerFile, JsonUtility.ToJson(l, true)); }
        static string F(float v) => v.ToString("F2", Inv);

        static Scene PlayScene(bool clean)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != PlaySceneName) throw new Exception("open " + PlaySceneName + " first (active scene is " + scene.name + ")");
            if (clean && scene.isDirty) throw new Exception("the play scene has unsaved changes");
            return scene;
        }

        static string Backup(IEnumerable<Scene> scenes, string label)
        {
            string dir = Path.Combine(StageDir, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_" + label); Directory.CreateDirectory(dir);
            foreach (var s in scenes) File.Copy(Path.GetFullPath(s.path), Path.Combine(dir, Path.GetFileName(s.path)), true);
            return dir;
        }

        static IEnumerable<Scene> LoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isLoaded) yield return s; }
        }

        static string PathOf(Transform t) { var parts = new List<string>(); for (; t != null; t = t.parent) parts.Add(t.name); parts.Reverse(); return string.Join("/", parts); }

        // topmost objects named like one of the old cliff roots, in every loaded scene (the realm scenes hold their moved content)
        static List<GameObject> OldObjects()
        {
            var found = new List<GameObject>();
            // the mesh rock walls of the #297 cut cliff paths whose wall is terrain now (config pathFaceSites): CliffPath_<site>_Face_<n>, wherever they are
            var facePrefixes = ReadConfig().pathFaceSites.Select(x => "CliffPath_" + x + "_Face_").ToArray();
            foreach (var scene in LoadedScenes())
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        // the realm scenes hold the cliff way's parts under their own root, without the old parent: known by name + mesh asset folder
                        bool moved = MovedParts.Contains(t.name) && t.parent != null && t.parent.parent == null && t.parent.name.StartsWith("Realm_", StringComparison.Ordinal) && UsesMeshUnder(t, "/CliffWay308/");
                        // their low far-band copies (RealmStream308 far:build) stay in the play scene: RealmFar308/<Realm>/<unit>
                        bool far = MovedParts.Contains(t.name) && t.parent != null && t.parent.parent != null && t.parent.parent.name == "RealmFar308" && t.parent.name == "Hyeongang" && NearCove(t);
                        bool face = facePrefixes.Any(x => t.name.StartsWith(x, StringComparison.Ordinal)) && (t.parent == null || !facePrefixes.Any(x => t.parent.name.StartsWith(x, StringComparison.Ordinal)));
                        if (!OldRoots.Contains(t.name) && !moved && !far && !face) continue;
                        bool nested = false; for (var p = t.parent; p != null; p = p.parent) if (OldRoots.Contains(p.name)) nested = true;
                        if (!nested) found.Add(t.gameObject);
                    }
            return found;
        }

        static readonly string[] MovedParts = { "Shell", "Steps", "Rail", "Col_Tread", "Col_Shell", "Col_Rail", "Ymc" };
        static bool UsesMeshUnder(Transform t, string folder)
        {
            foreach (var f in t.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null && AssetDatabase.GetAssetPath(f.sharedMesh).Contains(folder)) return true;
            foreach (var c in t.GetComponentsInChildren<MeshCollider>(true)) if (c.sharedMesh != null && AssetDatabase.GetAssetPath(c.sharedMesh).Contains(folder)) return true;
            return false;
        }

        static bool NearCove(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) return false;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return Mathf.Abs(b.center.x - 1990f) < 260f && Mathf.Abs(b.center.z - 4925f) < 260f;      // the cove of the old cliff way (stations.json cx, cz)
        }

        static string Status()
        {
            var sb = new StringBuilder("Rugged308 status\n"); var cfg = ReadConfig(); var led = ReadLedger();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterial);
            sb.Append("  stage ").Append(cfg.stage).Append(", wallLowerM ").Append(F(cfg.wallLowerM)).Append('\n');
            sb.Append("  ground field: ").Append(mat == null ? "MATERIAL MISSING" : AssetDatabase.GetAssetPath(mat.GetTexture(FieldProperty))).Append(" (ledger before: ").Append(led.strataBefore.Length == 0 ? "-" : led.strataBefore).Append(")\n");
            sb.Append("  loaded scenes: ").Append(string.Join(", ", LoadedScenes().Select(s => s.name + (s.isDirty ? "*" : "")))).Append('\n');
            foreach (var g in OldObjects())
                sb.Append("  old ").Append(g.scene.name).Append(':').Append(PathOf(g.transform)).Append(g.activeSelf ? " ON" : " off").Append(", renderers ").Append(g.GetComponentsInChildren<Renderer>(true).Length)
                  .Append(", colliders ").Append(g.GetComponentsInChildren<Collider>(true).Length).Append('\n');
            foreach (var scene in LoadedScenes())
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == GalleryRoot) sb.Append("  gallery root in ").Append(scene.name).Append(": children ").Append(root.transform.childCount).Append('\n');
                    if (root.name != WallRoot) continue;
                    sb.Append("  wall root in ").Append(scene.name).Append(" (ledger lowered ").Append(F(led.wallLoweredM)).Append(" m):");
                    foreach (Transform child in root.transform) sb.Append(' ').Append(child.name).Append("@y").Append(F(child.localPosition.y)).Append('[').Append(child.childCount).Append(']');
                    sb.Append('\n');
                }
            sb.Append("  ledger hidden ").Append(led.hidden.Count);
            return sb.ToString();
        }

        static string Strata(bool apply)
        {
            var cfg = ReadConfig(); var led = ReadLedger(); var mat = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterial);
            if (mat == null) throw new Exception("ground material not found: " + GroundMaterial);
            string now = AssetDatabase.GetAssetPath(mat.GetTexture(FieldProperty));
            if (!apply)
            {
                if (led.strataBefore.Length == 0) return "Rugged308 strata:revert: nothing recorded (field is " + now + ")";
                var before = AssetDatabase.LoadAssetAtPath<Texture2D>(led.strataBefore); if (before == null) throw new Exception("recorded field texture is gone: " + led.strataBefore);
                mat.SetTexture(FieldProperty, before); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
                string was = led.strataBefore; led.strataBefore = ""; led.strataNow = ""; WriteLedger(led);
                return "Rugged308 strata:revert: " + FieldProperty + " " + now + " -> " + was;
            }
            string file = "surface_" + cfg.stage + ".png", src = Path.Combine(RepoRoot, "Art", "World", "Compact", "Rebuild", "CliffBoundary308", "plan", "Stage", file), dst = SurfaceDir + "/" + file;
            if (!File.Exists(src)) throw new Exception("no " + src + " (run python Tools/Art/strata308.py Tools/Unity/Stage308_rugged/rugged308.json)");
            if (string.IsNullOrEmpty(now)) throw new Exception("the ground material has no " + FieldProperty + " texture to copy import settings from");
            if (!AssetDatabase.IsValidFolder(SurfaceDir)) throw new Exception(SurfaceDir + " is missing (apply the stage surface first)");
            File.Copy(src, Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), dst), true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            string from = now == dst && led.strataBefore.Length > 0 ? led.strataBefore : now;
            var si = AssetImporter.GetAtPath(from) as TextureImporter; var di = AssetImporter.GetAtPath(dst) as TextureImporter;
            if (si == null || di == null) throw new Exception("texture importer missing (" + from + " -> " + dst + ")");
            var settings = new TextureImporterSettings(); si.ReadTextureSettings(settings); di.SetTextureSettings(settings);
            di.textureCompression = si.textureCompression; di.maxTextureSize = si.maxTextureSize; di.SetPlatformTextureSettings(si.GetDefaultPlatformTextureSettings());
            var standalone = si.GetPlatformTextureSettings("Standalone"); if (standalone.overridden) di.SetPlatformTextureSettings(standalone);
            di.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dst); if (tex == null) throw new Exception("import failed: " + dst);
            if (now != dst && led.strataBefore.Length == 0) { led.strataBefore = now; }      // the first field only: later stages keep the original as the revert target
            led.strataNow = dst; mat.SetTexture(FieldProperty, tex); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); WriteLedger(led);
            return "Rugged308 strata:apply: " + FieldProperty + " " + now + " -> " + dst + " (" + tex.width + "x" + tex.height + " " + tex.format + ", sRGB " + di.sRGBTexture + ")";
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Mesh MakeMesh(MeshData d, out string[] slots)
        {
            int n = d.vertices.Length / 3; var v = new Vector3[n]; var nn = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(d.vertices[3 * i], d.vertices[3 * i + 1], d.vertices[3 * i + 2]);
                nn[i] = new Vector3(d.normals[3 * i], d.normals[3 * i + 1], d.normals[3 * i + 2]);
                uv[i] = new Vector2(d.uvs[2 * i], d.uvs[2 * i + 1]);
            }
            var mesh = new Mesh { name = d.name, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetNormals(nn); mesh.SetUVs(0, uv); mesh.subMeshCount = d.submeshes.Length;
            for (int s = 0; s < d.submeshes.Length; s++) mesh.SetTriangles(d.submeshes[s].triangles, s, false);
            mesh.RecalculateBounds(); mesh.RecalculateTangents(); slots = d.submeshes.Select(s => s.material).ToArray(); return mesh;
        }

        static string GalleryBuild()
        {
            var scene = PlayScene(true); string dir = Path.Combine(GalleryData, "Meshes");
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").OrderBy(x => x).ToArray() : Array.Empty<string>();
            if (files.Length == 0) throw new Exception("no mesh json under " + dir + " (run python Tools/Art/gallery308.py build ...)");
            string backup = Backup(new[] { scene }, "gallery");
            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == GalleryRoot); if (old != null) UnityEngine.Object.DestroyImmediate(old);
            Folder(GalleryAssets + "/Meshes");
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { GalleryAssets + "/Meshes" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            var root = new GameObject(GalleryRoot); SceneManager.MoveGameObjectToScene(root, scene); int drawn = 0, colliders = 0; long vertices = 0;
            foreach (string file in files)
            {
                var d = JsonUtility.FromJson<MeshData>(File.ReadAllText(file)); var mesh = MakeMesh(d, out var slots);
                AssetDatabase.CreateAsset(mesh, GalleryAssets + "/Meshes/" + d.name + ".asset"); AssetDatabase.SaveAssetIfDirty(mesh); vertices += mesh.vertexCount;
                var g = new GameObject(d.name); g.transform.SetParent(root.transform, false);
                if (d.name.EndsWith("_Collision", StringComparison.Ordinal))
                {
                    var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = false; mc.isTrigger = false; colliders++; continue;
                }
                var mats = slots.Select(s => AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + s + ".mat")).ToArray();
                if (mats.Any(m => m == null)) throw new Exception("material missing under " + MaterialDir + ": " + string.Join(",", slots));
                g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; drawn++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            return "Rugged308 gallery:build: " + drawn + " drawn meshes, " + colliders + " collider, " + vertices + " vertices, root " + GalleryRoot + " in " + scene.name + ", backup " + backup;
        }

        static string GalleryClear()
        {
            var scene = PlayScene(true); var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == GalleryRoot);
            if (old == null) return "Rugged308 gallery:clear: root absent";
            string backup = Backup(new[] { scene }, "gallery_clear"); UnityEngine.Object.DestroyImmediate(old); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return "Rugged308 gallery:clear: root removed, backup " + backup;
        }

        static string Old(bool hide)
        {
            PlayScene(false); var led = ReadLedger(); var dirty = LoadedScenes().Where(s => s.isDirty).Select(s => s.name).ToArray();
            if (dirty.Length > 0) throw new Exception("unsaved scenes: " + string.Join(", ", dirty));
            var touched = new List<Scene>(); var sb = new StringBuilder();
            if (hide)
            {
                var targets = OldObjects().Where(g => g.activeSelf).ToList(); if (targets.Count == 0) return "Rugged308 old:hide: nothing switched on (run RealmStream308 open-all first if the realm scenes are closed)";
                string backup = Backup(targets.Select(g => g.scene).Distinct(), "old_hide");
                foreach (var g in targets)
                {
                    led.hidden.Add(new Hidden { scene = g.scene.path, path = PathOf(g.transform) }); g.SetActive(false); EditorSceneManager.MarkSceneDirty(g.scene);
                    if (!touched.Contains(g.scene)) touched.Add(g.scene); sb.Append("\n  off ").Append(g.scene.name).Append(':').Append(PathOf(g.transform));
                }
                sb.Append("\n  backup ").Append(backup);
            }
            else
            {
                if (led.hidden.Count == 0) return "Rugged308 old:show: nothing recorded";
                var all = OldObjects(); var rest = new List<Hidden>();
                var hits = led.hidden.Select(h => (h, g: all.FirstOrDefault(x => x.scene.path == h.scene && PathOf(x.transform) == h.path))).ToList();
                string backup = Backup(hits.Where(x => x.g != null).Select(x => x.g.scene).Distinct(), "old_show");
                foreach (var (h, g) in hits)
                {
                    if (g == null) { rest.Add(h); sb.Append("\n  NOT LOADED ").Append(h.scene).Append(':').Append(h.path); continue; }
                    g.SetActive(true); EditorSceneManager.MarkSceneDirty(g.scene); if (!touched.Contains(g.scene)) touched.Add(g.scene); sb.Append("\n  on ").Append(g.scene.name).Append(':').Append(h.path);
                }
                led.hidden = rest; sb.Append("\n  backup ").Append(backup);
            }
            foreach (var s in touched) if (!EditorSceneManager.SaveScene(s)) throw new Exception("scene not saved: " + s.path);
            WriteLedger(led);
            return "Rugged308 old:" + (hide ? "hide" : "show") + ": " + touched.Count + " scenes saved" + sb;
        }

        static string Wall(bool lower)
        {
            var scene = PlayScene(true); var cfg = ReadConfig(); var led = ReadLedger();
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == WallRoot); if (root == null) throw new Exception("no " + WallRoot + " root in " + scene.name);
            if (lower == led.wallLoweredM > 0f) return "Rugged308 wall:" + (lower ? "lower: already lowered " + F(led.wallLoweredM) + " m" : "restore: not lowered");
            if (lower && cfg.wallLowerM <= 0f) throw new Exception("config wallLowerM is not set");
            string backup = Backup(new[] { scene }, lower ? "wall_lower" : "wall_restore"); var sb = new StringBuilder();
            if (lower)
            {
                foreach (Transform child in root.transform)
                {
                    if (child.name == "Gate" || child.name.Contains("|") || child.name.StartsWith("J308_", StringComparison.Ordinal)) { sb.Append("\n  kept ").Append(child.name); continue; }
                    led.lowered.Add(new Lowered { name = child.name, before = child.localPosition.y });
                    var p = child.localPosition; p.y -= cfg.wallLowerM; child.localPosition = p; sb.Append("\n  lowered ").Append(child.name).Append(" to y ").Append(F(p.y));
                }
                led.wallLoweredM = cfg.wallLowerM;
            }
            else
            {
                foreach (var row in led.lowered)
                {
                    var child = root.transform.Find(row.name); if (child == null) { sb.Append("\n  MISSING ").Append(row.name); continue; }
                    var p = child.localPosition; p.y = row.before; child.localPosition = p; sb.Append("\n  restored ").Append(row.name);
                }
                led.lowered.Clear(); led.wallLoweredM = 0f;
            }
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            WriteLedger(led);
            return "Rugged308 wall:" + (lower ? "lower " + F(cfg.wallLowerM) + " m" : "restore") + ", backup " + backup + sb;
        }
        // ---- scan: which 8 m cells of a box hold something drawn (not terrain, not the far band, not a sheet-like plane): the keep-out of a terrain op
        static string Scan(string arg)
        {
            var v = arg.Split(',').Select(x => float.Parse(x, Inv)).ToArray(); if (v.Length != 4) throw new Exception("use scan:<x0>,<z0>,<x1>,<z1>");
            const float cell = 8f; int cols = Mathf.CeilToInt((v[2] - v[0]) / cell), rows = Mathf.CeilToInt((v[3] - v[1]) / cell); var hit = new bool[cols * rows]; var roots = new Dictionary<string, int>();
            foreach (var scene in LoadedScenes())
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == "Reworld292_Terrain" || root.name == "RealmFar308") continue;
                    foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                    {
                        var b = r.bounds; if (b.size.x > 150f || b.size.z > 150f) continue;
                        if (b.max.x < v[0] || b.min.x > v[2] || b.max.z < v[1] || b.min.z > v[3]) continue;
                        string top = r.transform.root.name; if (top.StartsWith("Realm_", StringComparison.Ordinal)) { var t = r.transform; while (t.parent != null && t.parent.parent != null) t = t.parent; top = top + "/" + t.name; }
                        roots[top] = roots.TryGetValue(top, out int n) ? n + 1 : 1;
                        int c0 = Mathf.Clamp(Mathf.FloorToInt((b.min.x - v[0]) / cell), 0, cols - 1), c1 = Mathf.Clamp(Mathf.FloorToInt((b.max.x - v[0]) / cell), 0, cols - 1);
                        int r0 = Mathf.Clamp(Mathf.FloorToInt((b.min.z - v[1]) / cell), 0, rows - 1), r1 = Mathf.Clamp(Mathf.FloorToInt((b.max.z - v[1]) / cell), 0, rows - 1);
                        for (int rr = r0; rr <= r1; rr++) for (int cc = c0; cc <= c1; cc++) hit[rr * cols + cc] = true;
                    }
                }
            var o = new ScanOut { x0 = v[0], z0 = v[1], cell = cell, cols = cols, rows = rows, occupied = Enumerable.Range(0, hit.Length).Where(i => hit[i]).ToArray(), roots = roots.OrderByDescending(k => k.Value).Select(k => k.Key + " x" + k.Value).ToArray() };
            string file = Path.Combine(StageDir, "scan_" + arg.Replace(',', '_') + ".json"); File.WriteAllText(file, JsonUtility.ToJson(o));
            return "Rugged308 scan: " + o.occupied.Length + " of " + hit.Length + " cells occupied -> " + file + "\n  " + string.Join("\n  ", o.roots.Take(30));
        }

        // ---- flora: the realm sheets were planned on one stage field; move every plant by (to - from) where the field changed, drop it on a new face
        static float[] Field(string stage)
        {
            string path = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), SurfaceDir, "height_" + stage + ".bytes"); var bytes = File.ReadAllBytes(path);
            if (bytes.Length != 1001 * 1501 * 4) throw new Exception(path + " is not a 1001 x 1501 float field"); var f = new float[1001 * 1501]; Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length); return f;
        }
        static float Sample(float[] f, float x, float z)
        {
            float u = Mathf.Clamp(x / 4f, 0f, 999.999f), w = Mathf.Clamp(z / 4f, 0f, 1499.999f); int j = (int)u, i = (int)w; float fu = u - j, fw = w - i;
            return f[i * 1001 + j] * (1 - fu) * (1 - fw) + f[i * 1001 + j + 1] * fu * (1 - fw) + f[(i + 1) * 1001 + j] * (1 - fu) * fw + f[(i + 1) * 1001 + j + 1] * fu * fw;
        }
        static string FloraBackupDir => Path.Combine(StageDir, "Ledger", "flora_before");
        static string Flora(bool reseat)
        {
            var cfg = ReadConfig(); var rule = cfg.flora; if (rule.sheets.Length == 0) throw new Exception("config flora.sheets is empty");
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!reseat)
            {
                if (!Directory.Exists(FloraBackupDir)) return "Rugged308 flora:restore: nothing recorded";
                int n = 0; foreach (string sheet in rule.sheets) { string b = Path.Combine(FloraBackupDir, Path.GetFileName(sheet)); if (!File.Exists(b)) continue; File.Copy(b, Path.Combine(project, sheet), true); AssetDatabase.ImportAsset(sheet, ImportAssetOptions.ForceUpdate); n++; }
                Directory.Move(FloraBackupDir, FloraBackupDir + "_restored_" + DateTime.Now.ToString("yyyyMMddTHHmmss", Inv));
                return "Rugged308 flora:restore: " + n + " sheets restored";
            }
            if (Directory.Exists(FloraBackupDir)) throw new Exception("already re-seated (flora:restore first): " + FloraBackupDir);
            var from = Field(rule.from); var to = Field(rule.to); float tan = Mathf.Tan(rule.removeSlopeDeg * Mathf.Deg2Rad); Directory.CreateDirectory(FloraBackupDir); var sb = new StringBuilder();
            foreach (string sheet in rule.sheets)
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
            return "Rugged308 flora:reseat " + rule.from + " -> " + rule.to + ", backup " + FloraBackupDir + sb;
        }

        // ---- rocks: boulders at the foot of the faces (Tools/Art/rocks308.py -> rocks308.json); prefab instances, half sunk, their collider inside the drawn rock
        static string Rocks(bool build)
        {
            const string rootName = "RuggedRocks308"; var scene = PlayScene(true); var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == rootName);
            if (!build)
            {
                if (old == null) return "Rugged308 rocks:clear: root absent";
                string b0 = Backup(new[] { scene }, "rocks_clear"); UnityEngine.Object.DestroyImmediate(old); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); return "Rugged308 rocks:clear: removed, backup " + b0;
            }
            string file = Path.Combine(StageDir, "rocks308.json"); if (!File.Exists(file)) throw new Exception("no " + file + " (run python Tools/Art/rocks308.py)");
            var data = JsonUtility.FromJson<RockFile>(File.ReadAllText(file)); string backup = Backup(new[] { scene }, "rocks");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject(rootName); SceneManager.MoveGameObjectToScene(root, scene); var prefabs = new Dictionary<string, (GameObject go, Bounds b)>(); int placed = 0, skipped = 0; long tris = 0;
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
                inst.transform.localScale = inst.transform.localScale * scale; inst.transform.rotation = Quaternion.Euler(r.tiltX, r.yaw, r.tiltZ);
                var rs2 = inst.GetComponentsInChildren<Renderer>(); var wb = rs2[0].bounds; foreach (var x in rs2) wb.Encapsulate(x.bounds);
                inst.transform.position += new Vector3(r.x - wb.center.x, r.y - r.sink * wb.size.y - wb.min.y, r.z - wb.center.z);
                if (inst.GetComponentsInChildren<Collider>().Length == 0)
                {
                    var mf = inst.GetComponentsInChildren<MeshFilter>().OrderByDescending(m => m.sharedMesh != null ? m.sharedMesh.vertexCount : 0).FirstOrDefault();
                    if (mf != null && mf.sharedMesh != null) { var bc = mf.gameObject.AddComponent<BoxCollider>(); bc.center = mf.sharedMesh.bounds.center; bc.size = mf.sharedMesh.bounds.size * 0.62f; }
                }
                var first = inst.GetComponentsInChildren<MeshFilter>().FirstOrDefault(); if (first != null && first.sharedMesh != null) tris += (long)first.sharedMesh.GetIndexCount(0) / 3;
                placed++;
            }
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            return "Rugged308 rocks:build: " + placed + " rocks (" + skipped + " skipped), first-mesh triangles " + tris + ", prefabs " + string.Join(", ", prefabs.Select(k => k.Key + " " + F(k.Value.b.size.x) + "x" + F(k.Value.b.size.y) + "x" + F(k.Value.b.size.z))) + ", backup " + backup;
        }
        // ---- crag: the mesh rock wall that stays a mesh (protected ground under it) takes this tool's own copy of a realm rock material, tuned to the terrain rock
        const string CragMaterial = "Assets/_Project/Art/World/Rugged308/Materials/jeokro_crag_rock.mat";
        static string Crag(bool tint)
        {
            PlayScene(false); var cfg = ReadConfig(); var led = ReadLedger(); var c = cfg.crag; if (c.facePrefix.Length == 0) throw new Exception("config crag.facePrefix is not set");
            var dirty = LoadedScenes().Where(x => x.isDirty).Select(x => x.name).ToArray(); if (dirty.Length > 0) throw new Exception("unsaved scenes: " + string.Join(", ", dirty));
            var renderers = new List<MeshRenderer>();
            foreach (var scene in LoadedScenes())
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith(c.facePrefix, StringComparison.Ordinal) && (t.parent == null || !t.parent.name.StartsWith(c.facePrefix, StringComparison.Ordinal)))
                            renderers.AddRange(t.GetComponentsInChildren<MeshRenderer>(true));
            if (renderers.Count == 0) throw new Exception("no renderer under an object named " + c.facePrefix + "* (run RealmStream308 open-all first)");
            Material target;
            if (tint)
            {
                var source = AssetDatabase.FindAssets(c.source + " t:Material").Select(AssetDatabase.GUIDToAssetPath).Where(q => q.Contains("Architecture296/Materials")).Select(AssetDatabase.LoadAssetAtPath<Material>).FirstOrDefault(x => x != null);
                if (source == null) throw new Exception("source material not found: " + c.source);
                target = AssetDatabase.LoadAssetAtPath<Material>(CragMaterial);
                if (target == null) { Folder(Path.GetDirectoryName(CragMaterial).Replace('\\', '/')); target = new Material(source) { name = "jeokro_crag_rock" }; AssetDatabase.CreateAsset(target, CragMaterial); }
                else target.CopyPropertiesFromMaterial(source);
                if (c.baseColor.Length == 3) target.SetColor("_BaseColor", new Color(c.baseColor[0], c.baseColor[1], c.baseColor[2], 1f));
                if (c.farTone.Length == 3) target.SetColor("_FarTone", new Color(c.farTone[0], c.farTone[1], c.farTone[2], 1f));
                if (c.value.Length == 2) target.SetVector("_Value293", new Vector4(c.value[0], c.value[1], 0f, 0f));
                if (c.chroma >= 0f) target.SetFloat("_Chroma", c.chroma);
                if (c.moss >= 0f) target.SetFloat("_MossAmount", c.moss);
                EditorUtility.SetDirty(target); AssetDatabase.SaveAssetIfDirty(target);
                string was = AssetDatabase.GetAssetPath(renderers[0].sharedMaterial); if (was != CragMaterial && led.cragBefore.Length == 0) led.cragBefore = was;
            }
            else
            {
                if (led.cragBefore.Length == 0) return "Rugged308 crag:revert: nothing recorded";
                target = AssetDatabase.LoadAssetAtPath<Material>(led.cragBefore); if (target == null) throw new Exception("recorded material is gone: " + led.cragBefore);
            }
            var touched = renderers.Where(r => r.sharedMaterial != target).Select(r => r.gameObject.scene).Distinct().ToList();
            string backup = touched.Count > 0 ? Backup(touched, tint ? "crag_tint" : "crag_revert") : "-";
            foreach (var r in renderers) if (r.sharedMaterial != target) { r.sharedMaterial = target; EditorSceneManager.MarkSceneDirty(r.gameObject.scene); }
            foreach (var sc in touched) if (!EditorSceneManager.SaveScene(sc)) throw new Exception("scene not saved: " + sc.path);
            if (!tint) led.cragBefore = ""; WriteLedger(led);
            return "Rugged308 crag:" + (tint ? "tint" : "revert") + ": " + renderers.Count + " renderers -> " + AssetDatabase.GetAssetPath(target) + ", scenes saved " + touched.Count + ", backup " + backup
                + (tint ? " | _BaseColor " + target.GetColor("_BaseColor") + " _Chroma " + F(target.GetFloat("_Chroma")) + " _MossAmount " + F(target.GetFloat("_MossAmount")) : "");
        }
        // ---- trail: the drawn treads / planks of the #297 cut cliff paths are replaced by the rough ones of Gallery308 (D308-57). Their renderers
        // are switched off and their colliders stay (the walked ramp does not change); the far-band copies are switched off whole.
        static bool IsOldTread(string name) => name.StartsWith("CliffPath_", StringComparison.Ordinal) && name.EndsWith("_Trail", StringComparison.Ordinal);      // the hung gallery keeps its frame and rail: rough planks are laid over its deck
        static string Trail(bool hide)
        {
            PlayScene(false); var dirty = LoadedScenes().Where(x => x.isDirty).Select(x => x.name).ToArray(); if (dirty.Length > 0) throw new Exception("unsaved scenes: " + string.Join(", ", dirty));
            var targets = new List<Transform>();
            foreach (var scene in LoadedScenes())
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (IsOldTread(t.name) && (t.parent == null || !IsOldTread(t.parent.name))) targets.Add(t);
            if (targets.Count == 0) throw new Exception("no CliffPath_*_Trail / _Gallery object loaded (run RealmStream308 open-all first)");
            string backup = Backup(targets.Select(t => t.gameObject.scene).Distinct(), hide ? "trail_hide" : "trail_show"); var sb = new StringBuilder(); var touched = new List<Scene>();
            foreach (var t in targets)
            {
                bool far = false; for (var q = t.parent; q != null; q = q.parent) if (q.name == "RealmFar308") far = true;
                int n = 0;
                if (far) { if (t.gameObject.activeSelf == hide) { t.gameObject.SetActive(!hide); n = 1; } }
                else foreach (var r in t.GetComponentsInChildren<Renderer>(true)) if (r.enabled == hide) { r.enabled = !hide; n++; }
                if (n == 0) continue;
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene); if (!touched.Contains(t.gameObject.scene)) touched.Add(t.gameObject.scene);
                sb.Append("\n  ").Append(far ? "far " : "").Append(t.gameObject.scene.name).Append(':').Append(PathOf(t)).Append(far ? "" : " renderers " + n).Append(", colliders kept ").Append(t.GetComponentsInChildren<Collider>(true).Length);
            }
            foreach (var sc in touched) if (!EditorSceneManager.SaveScene(sc)) throw new Exception("scene not saved: " + sc.path);
            return "Rugged308 trail:" + (hide ? "hide" : "show") + ": " + touched.Count + " scenes saved, backup " + backup + sb;
        }
    }
}
