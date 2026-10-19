using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // D308-117 C3b [TEST] - the look pass of the two dungeon front sections opened before 청룡 (뿌리 굴 cr_root · 숯가마 골 땅굴 cr_kiln).
    // Visual only: no collider is added or removed anywhere, so the baked NavMesh stays valid (no bake). Data per dungeon:
    // Tools/Unity/Stage308_dungeon/dress308/<id>.json, written by Tools/Art/dungeon308_dress.py from the built layout.json.
    // This tool owns ONE root (DungeonDress308) in the play scene and ONE asset folder (Assets/_Project/Art/World/Dungeon308/Dress308).
    //   DungeonDress308/<id>/<item id>   a prefab or a mesh asset: colliders stripped, seated on the floor, hung from the ceiling or set as placed;
    //                                    a "solid" hero prop gets ONE box collider (90 % of its bounds) and a carving NavMeshObstacle, so the
    //                                    player and the enemies go round it at runtime and the baked NavMesh still needs no bake
    //   DungeonDress308/<id>/Lamp_<n>    a lantern set down on the floor like the cave dungeons' (D308-61): its paper glows, one point light in it
    //   meshes                           the Meshy hero props: Art/World/Dungeon308/Dress308Meshy/Kit/<name>.json (Tools/Blender/Dungeon308/dress_meshy.py)
    //                                    -> Dress308/Meshes/<name>.asset
    //   mats                             the drawn renderers of Dungeon308/<id> get other materials (kiln: soot earth instead of plaster and brick);
    //                                    every original is kept in a ledger (Stage308_dungeon/dress308/Ledger/<id>.json) and put back by revert
    // Commands (queue-safe, never a dialog; refusals come back as "REFUSED ..."):
    //   survey:<id>   the children, renderers and materials of Dungeon308/<id> (read only)
    //   mats          makes / refreshes the materials the data asks for (newMats)
    //   apply:<id>    backup, revert first (idempotent), then materials + items + lamps from the data, scene saved
    //   revert:<id>   removes DungeonDress308/<id> and puts the original materials back
    //   check[:<id>]  data = scene, no collider under the root, lights only in lanterns, keep-outs clear, the dungeon's colliders unchanged
    public static class DungeonDress308
    {
        const string RootName = "DungeonDress308", DungeonRoot = "Dungeon308", SceneName = "W_Demo_Main", AssetDir = "Assets/_Project/Art/World/Dungeon308/Dress308";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string StageDir => Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_dungeon");
        static string DressDir => Path.Combine(StageDir, "dress308");

        [Serializable] sealed class MatSwap { public string renderer = "", from = "", to = ""; }
        [Serializable] sealed class NewMat { public string path = "", source = "", shader = "", baseMap = "", normalMap = ""; public float[] color = Array.Empty<float>(); public float smoothness = -1f, ambient = -1f; }
        [Serializable] sealed class Item { public string id = "", prefab = "", mesh = "", material = "", seat = "floor", kind = "", cell = ""; public float[] pos = Array.Empty<float>(), rot = Array.Empty<float>(), scale = Array.Empty<float>(); public float fit, fitAxis = -1f, sink; public bool noShadow, solid; }
        [Serializable] sealed class Keep { public string what = ""; public float[] pos = Array.Empty<float>(); public float r = 1f, h = 2.2f; }
        [Serializable] sealed class Walk { public string what = ""; public float[] min = Array.Empty<float>(), max = Array.Empty<float>(); public float clutter = .15f; }
        [Serializable] sealed class LampRow { public string cell = ""; public float[] pos = Array.Empty<float>(); public float yaw, intensity, range; }
        [Serializable] sealed class Data { public string version = "", id = ""; public float[] boundsMin = Array.Empty<float>(), boundsMax = Array.Empty<float>(); public NewMat[] newMats = Array.Empty<NewMat>(); public MatSwap[] mats = Array.Empty<MatSwap>(); public Item[] items = Array.Empty<Item>(); public LampRow[] lamps = Array.Empty<LampRow>(); public Keep[] keep = Array.Empty<Keep>(); public Walk[] walk = Array.Empty<Walk>(); public int lampsPerCell = 2; }
        [Serializable] sealed class LedgerRow { public string path = ""; public string[] materials = Array.Empty<string>(); }
        [Serializable] sealed class Ledger { public string id = "", colliderDigest = ""; public LedgerRow[] rows = Array.Empty<LedgerRow>(); }
        [Serializable] sealed class LampCfg { public string paperMesh = "", frameMesh = "", paperMaterial = "", frameMaterial = ""; public float[] color = Array.Empty<float>(); public float intensity = 2.8f, range = 15f, lightY = .34f; }
        [Serializable] sealed class Cfg { public LampCfg lamp = new LampCfg(); }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c.StartsWith("survey:", StringComparison.Ordinal)) return Survey(c.Substring(7).Trim());
                if (c.StartsWith("probe:", StringComparison.Ordinal)) return Probe(c.Substring(6));
                if (c == "check") return Check(null);
                if (c.StartsWith("check:", StringComparison.Ordinal)) return Check(c.Substring(6).Trim());
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "REFUSED scripts are compiling or failed to compile";
                if (c == "mats") return Mats();
                if (c == "meshes") return Meshes();
                if (c.StartsWith("apply:", StringComparison.Ordinal))
                {
                    string path = SceneManager.GetActiveScene().path;
                    try { return Apply(c.Substring(6).Trim()); }
                    catch (Exception e) { if (SceneManager.GetActiveScene().isDirty) EditorSceneManager.OpenScene(path, OpenSceneMode.Single); return "REFUSED " + e.Message + " (the play scene was reopened from disk; nothing saved)"; }
                }
                if (c.StartsWith("revert:", StringComparison.Ordinal)) return Revert(c.Substring(7).Trim(), true);
                return "REFUSED unknown command '" + c + "' (survey:<id> | mats | apply:<id> | revert:<id> | check[:<id>])";
            }
            catch (Exception e) { return "REFUSED " + e.Message + (e is NullReferenceException ? " @ " + e.StackTrace : ""); }
        }

        static Scene PlayScene(bool clean)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != SceneName) throw new Exception("open " + SceneName + " first (active scene is " + scene.name + ")");
            if (clean && scene.isDirty) throw new Exception("the play scene has unsaved changes");
            return scene;
        }
        static string F(float v) => v.ToString("F2", Inv);
        static string V3(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);
        static Data ReadData(string id)
        {
            string f = Path.Combine(DressDir, id + ".json");
            if (!File.Exists(f)) throw new Exception("no " + f + " (run python Tools/Art/dungeon308_dress.py)");
            var d = JsonUtility.FromJson<Data>(File.ReadAllText(f)); if (d.id != id) throw new Exception(f + " is for '" + d.id + "'"); return d;
        }
        static string LedgerFile(string id) => Path.Combine(DressDir, "Ledger", id + ".json");
        static Transform DungeonHome(Scene scene, string id)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == DungeonRoot); var home = root != null ? root.transform.Find(id) : null;
            if (home == null) throw new Exception("no " + DungeonRoot + "/" + id + " in the play scene"); return home;
        }
        static string PathUnder(Transform t, Transform top) { var parts = new List<string>(); for (var x = t; x != null && x != top; x = x.parent) parts.Insert(0, x.name); return string.Join("/", parts); }
        static string ColliderDigest(Transform home)
        {
            var sb = new StringBuilder();
            foreach (var c in home.GetComponentsInChildren<Collider>(true).OrderBy(c => PathUnder(c.transform, home), StringComparer.Ordinal))
                sb.Append(PathUnder(c.transform, home)).Append('|').Append(c.GetType().Name).Append('|').Append(c.enabled).Append('|').Append(V3(c.bounds.center)).Append(V3(c.bounds.size)).Append('\n');
            using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16);
        }
        static string Backup(Scene scene, string label)
        {
            string dir = Path.Combine(DressDir, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss", Inv) + "_" + label); Directory.CreateDirectory(dir);
            File.Copy(Path.GetFullPath(scene.path), Path.Combine(dir, Path.GetFileName(scene.path)), true); return dir;
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string Survey(string id)
        {
            var scene = PlayScene(false); var home = DungeonHome(scene, id); var sb = new StringBuilder("DungeonDress308 survey " + id + "\n");
            foreach (Transform t in home) sb.Append("  child ").Append(t.name).Append(" renderers ").Append(t.GetComponentsInChildren<Renderer>(true).Length).Append(" colliders ").Append(t.GetComponentsInChildren<Collider>(true).Length).Append('\n');
            foreach (var r in home.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                sb.Append("  r ").Append(PathUnder(r.transform, home)).Append(" mesh ").Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-").Append(" bounds ").Append(V3(r.bounds.min)).Append("..").Append(V3(r.bounds.max)).Append(" mats [")
                  .Append(string.Join(", ", r.sharedMaterials.Select(m => m == null ? "null" : m.name + "<" + (m.shader != null ? m.shader.name : "-") + ">"))).Append("]\n");
            }
            sb.Append("  collider digest ").Append(ColliderDigest(home));
            return sb.ToString();
        }

        // probe:<asset>;<asset>... - the bounds of prefabs / meshes from their mesh bounds and asset transforms (nothing is instantiated)
        static string Probe(string list)
        {
            var sb = new StringBuilder("DungeonDress308 probe");
            foreach (var p in list.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p); var b = new Bounds(); bool any = false; int lights = 0, cols = 0;
                if (go != null)
                {
                    var w2l = go.transform.worldToLocalMatrix; lights = go.GetComponentsInChildren<Light>(true).Length; cols = go.GetComponentsInChildren<Collider>(true).Length;
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.sharedMesh == null || mf.GetComponent<Renderer>() == null || mf.name.Contains("LOD1") || mf.name.Contains("LOD2") || mf.name.Contains("LOD3")) continue;
                        var m = w2l * mf.transform.localToWorldMatrix; var mb = mf.sharedMesh.bounds;
                        for (int i = 0; i < 8; i++) { var q = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))); if (any) b.Encapsulate(q); else { b = new Bounds(q, Vector3.zero); any = true; } }
                    }
                }
                else { var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(p); if (mesh != null) { b = mesh.bounds; any = true; } }
                sb.Append("\n  ").Append(p).Append(any ? " size " + V3(b.size) + " min " + V3(b.min) : " MISSING").Append(" lights ").Append(lights).Append(" colliders ").Append(cols);
            }
            return sb.ToString();
        }

        static string Mats()
        {
            Folder(AssetDir + "/Materials"); var sb = new StringBuilder("DungeonDress308 mats"); int n = 0;
            foreach (var f in Directory.GetFiles(DressDir, "*.json"))
                foreach (var m in JsonUtility.FromJson<Data>(File.ReadAllText(f)).newMats)
                {
                    if (!m.path.StartsWith(AssetDir + "/", StringComparison.Ordinal)) throw new Exception("new material outside " + AssetDir + ": " + m.path);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(m.path);
                    if (!string.IsNullOrEmpty(m.source))
                    {
                        var src = AssetDatabase.LoadAssetAtPath<Material>(m.source); if (src == null) throw new Exception("material source missing: " + m.source);
                        if (mat == null) { mat = new Material(src) { name = Path.GetFileNameWithoutExtension(m.path) }; AssetDatabase.CreateAsset(mat, m.path); } else { mat.shader = src.shader; mat.CopyPropertiesFromMaterial(src); }
                    }
                    else
                    {
                        var shader = Shader.Find(m.shader); if (shader == null) throw new Exception("shader not found: " + m.shader);
                        if (mat == null) { mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(m.path) }; AssetDatabase.CreateAsset(mat, m.path); } else mat.shader = shader;
                    }
                    if (m.color.Length == 3) { var col = new Color(m.color[0], m.color[1], m.color[2], 1f); if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col); if (mat.HasProperty("_Color")) mat.SetColor("_Color", col); }
                    if (m.smoothness >= 0f && mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", m.smoothness);
                    if (m.ambient >= 0f && mat.HasProperty("_Ambient")) mat.SetFloat("_Ambient", m.ambient);
                    if (!string.IsNullOrEmpty(m.baseMap)) { var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(m.baseMap); if (tex == null) throw new Exception("texture missing: " + m.baseMap); mat.SetTexture("_BaseMap", tex); if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex); }
                    if (!string.IsNullOrEmpty(m.normalMap))
                    {
                        if (AssetImporter.GetAtPath(m.normalMap) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                        var nt = AssetDatabase.LoadAssetAtPath<Texture2D>(m.normalMap); if (nt == null) throw new Exception("texture missing: " + m.normalMap); mat.SetTexture("_BumpMap", nt); mat.EnableKeyword("_NORMALMAP");
                    }
                    EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); n++; sb.Append("\n  ").Append(m.path).Append(" <").Append(mat.shader.name).Append('>');
                }
            return sb.Insert(0, "").Append("\n  ").Append(n).Append(" material(s)").ToString();
        }

        [Serializable] sealed class JMesh { public string name = ""; public float[] vertices = Array.Empty<float>(), normals = Array.Empty<float>(), uvs = Array.Empty<float>(); public JSub[] submeshes = Array.Empty<JSub>(); }
        [Serializable] sealed class JSub { public string material = ""; public int[] triangles = Array.Empty<int>(); }

        static string Meshes()
        {
            string kit = Path.Combine(RepoRoot, "Art", "World", "Dungeon308", "Dress308Meshy", "Kit"); Folder(AssetDir + "/Meshes"); var sb = new StringBuilder("DungeonDress308 meshes");
            foreach (var f in Directory.GetFiles(kit, "dd_*.json"))
            {
                var j = JsonUtility.FromJson<JMesh>(File.ReadAllText(f)); int n = j.vertices.Length / 3; var v = new Vector3[n]; var nn = new Vector3[n]; var uv = new Vector2[n];
                for (int i = 0; i < n; i++) { v[i] = new Vector3(j.vertices[3 * i], j.vertices[3 * i + 1], j.vertices[3 * i + 2]); nn[i] = new Vector3(j.normals[3 * i], j.normals[3 * i + 1], j.normals[3 * i + 2]).normalized; uv[i] = new Vector2(j.uvs[2 * i], j.uvs[2 * i + 1]); }
                string path = AssetDir + "/Meshes/" + j.name + ".asset"; var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); bool fresh = mesh == null; if (fresh) mesh = new Mesh(); else mesh.Clear();
                mesh.name = j.name; mesh.indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16; mesh.SetVertices(v); mesh.SetNormals(nn); mesh.SetUVs(0, uv); mesh.SetTriangles(j.submeshes[0].triangles, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                if (fresh) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
                sb.Append("\n  ").Append(path).Append(" vertices ").Append(n).Append(" bounds ").Append(V3(mesh.bounds.size));
            }
            return sb.ToString();
        }

        static string Apply(string id)
        {
            var scene = PlayScene(true); var data = ReadData(id); var home = DungeonHome(scene, id); string backup = Backup(scene, "apply-" + id);
            string before = ColliderDigest(home); var sb = new StringBuilder("DungeonDress308 apply " + id + " (data " + data.version + "), backup " + backup + "\n");
            sb.Append("  ").Append(Revert(id, false)).Append('\n');
            // materials: the ledger keeps every original of a renderer the data touches (the revert above has just put the old ones back)
            var rows = new List<LedgerRow>(); int swapped = 0;
            foreach (var r in home.GetComponentsInChildren<Renderer>(true))
            {
                string path = PathUnder(r.transform, home); var mats = r.sharedMaterials; bool touched = false;
                for (int i = 0; i < mats.Length; i++)
                    foreach (var s in data.mats)
                    {
                        if (!(path == s.renderer || path.StartsWith(s.renderer + "/", StringComparison.Ordinal) || r.name.StartsWith(s.renderer, StringComparison.Ordinal))) continue;
                        if (mats[i] == null || (s.from != "*" && mats[i].name != s.from)) continue;
                        var to = AssetDatabase.LoadAssetAtPath<Material>(s.to); if (to == null) throw new Exception("material missing: " + s.to + " (run mats)");
                        if (!touched) { rows.Add(new LedgerRow { path = path, materials = mats.Select(m => m == null ? "" : AssetDatabase.GetAssetPath(m)).ToArray() }); touched = true; }
                        mats[i] = to; swapped++; break;
                    }
                if (touched) { if (rows.Last().materials.Any(string.IsNullOrEmpty)) throw new Exception(path + " holds a material that is not an asset (cannot be put back)"); r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(LedgerFile(id)));
            File.WriteAllText(LedgerFile(id), JsonUtility.ToJson(new Ledger { id = id, colliderDigest = before, rows = rows.ToArray() }, true));
            sb.Append("  materials: ").Append(swapped).Append(" slot(s) on ").Append(rows.Count).Append(" renderer(s)\n");
            // items and lamps
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (root == null) { root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene); }
            var mine = new GameObject(id).transform; mine.SetParent(root.transform, false);
            int items = 0, stripped = 0, solids = 0; var kinds = new Dictionary<string, int>();
            foreach (var it in data.items)
            {
                GameObject go;
                if (!string.IsNullOrEmpty(it.prefab))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(it.prefab); if (prefab == null) throw new Exception(it.id + ": prefab missing " + it.prefab);
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); go.transform.SetParent(mine, false);
                }
                else
                {
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(it.mesh); var mat = AssetDatabase.LoadAssetAtPath<Material>(it.material);
                    if (mesh == null || mat == null) throw new Exception(it.id + ": mesh or material missing " + it.mesh + " / " + it.material);
                    go = new GameObject(); go.transform.SetParent(mine, false); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                }
                go.name = it.id;
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(col); stripped++; }
                foreach (var lt in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(lt);      // 발광 상한: only the lanterns light
                var t = go.transform; t.localScale = it.scale.Length == 3 ? V(it.scale) : it.scale.Length == 1 ? Vector3.one * it.scale[0] : Vector3.one;
                t.rotation = Quaternion.Euler(it.rot.Length == 3 ? V(it.rot) : it.rot.Length == 1 ? new Vector3(0f, it.rot[0], 0f) : Vector3.zero); t.position = V(it.pos);
                var rs = go.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) throw new Exception(it.id + ": no renderer");
                Bounds B() { var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); return b; }
                if (it.fit > 0f)
                {
                    var b0 = B(); float have = it.fitAxis == 0f ? b0.size.x : it.fitAxis == 2f ? b0.size.z : it.fitAxis == 3f ? Mathf.Max(b0.size.x, b0.size.z) : b0.size.y;
                    if (have > 1e-4f) t.localScale *= it.fit / have;
                }
                var b = B(); var p = V(it.pos);
                if (it.seat == "floor") t.position += new Vector3(p.x - b.center.x, p.y - it.sink - b.min.y, p.z - b.center.z);
                else if (it.seat == "ceiling") t.position += new Vector3(p.x - b.center.x, p.y + it.sink - b.max.y, p.z - b.center.z);
                else if (it.seat == "centre") t.position += p - b.center;
                if (it.noShadow) foreach (var x in rs) x.shadowCastingMode = ShadowCastingMode.Off;
                if (it.solid)
                {
                    var bb = B(); var box = go.AddComponent<BoxCollider>(); var inv = t.InverseTransformVector(bb.size * .9f);
                    box.center = t.InverseTransformPoint(bb.center); box.size = new Vector3(Mathf.Abs(inv.x), Mathf.Abs(inv.y), Mathf.Abs(inv.z));
                    var ob = go.AddComponent<NavMeshObstacle>(); ob.shape = NavMeshObstacleShape.Box; ob.center = box.center; ob.size = box.size; ob.carving = true; ob.carveOnlyStationary = true; solids++;
                }
                GameObjectUtility.SetStaticEditorFlags(go, 0);
                items++; kinds[it.kind] = kinds.TryGetValue(it.kind, out int k0) ? k0 + 1 : 1;
            }
            var lc = JsonUtility.FromJson<Cfg>(File.ReadAllText(Path.Combine(StageDir, "dungeon308.json"))).lamp;
            Mesh paper = AssetDatabase.LoadAssetAtPath<Mesh>(lc.paperMesh), frame = AssetDatabase.LoadAssetAtPath<Mesh>(lc.frameMesh); Material paperMat = AssetDatabase.LoadAssetAtPath<Material>(lc.paperMaterial), frameMat = AssetDatabase.LoadAssetAtPath<Material>(lc.frameMaterial);
            int lamps = 0;
            foreach (var l in data.lamps)
            {
                if (paper == null || frame == null || paperMat == null || frameMat == null || lc.color.Length != 3) throw new Exception("lantern mesh or material missing (dungeon308.json lamp)");
                var g = new GameObject("Lamp_" + lamps); g.transform.SetParent(mine, false); g.transform.SetPositionAndRotation(V(l.pos), Quaternion.Euler(0f, l.yaw, 0f));
                var f1 = new GameObject("Frame"); f1.transform.SetParent(g.transform, false); f1.AddComponent<MeshFilter>().sharedMesh = frame; f1.AddComponent<MeshRenderer>().sharedMaterial = frameMat;
                var p1 = new GameObject("Paper"); p1.transform.SetParent(g.transform, false); p1.AddComponent<MeshFilter>().sharedMesh = paper; var pr = p1.AddComponent<MeshRenderer>(); pr.sharedMaterial = paperMat; pr.shadowCastingMode = ShadowCastingMode.Off;
                var glow = new GameObject("Glow"); glow.transform.SetParent(g.transform, false); glow.transform.localPosition = new Vector3(0f, lc.lightY, 0f); var light = glow.AddComponent<Light>();
                light.type = LightType.Point; light.color = new Color(lc.color[0], lc.color[1], lc.color[2], 1f); light.intensity = l.intensity > 0f ? l.intensity : lc.intensity; light.range = l.range > 0f ? l.range : lc.range; light.shadows = LightShadows.None; lamps++;
            }
            string after = ColliderDigest(home); if (after != before) throw new Exception("the dungeon's colliders changed (" + before + " -> " + after + ") - nothing is saved");
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) return "FAILED the play scene was not saved";
            sb.Append("  items ").Append(items).Append(" (").Append(string.Join(", ", kinds.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + " " + k.Value))).Append("), colliders stripped from items ").Append(stripped).Append(", solid hero props ").Append(solids).Append(", lanterns ").Append(lamps).Append('\n');
            sb.Append("  the dungeon's colliders unchanged (digest ").Append(before).Append("): no NavMesh bake\n");
            return sb.Append(Check(id)).ToString();
        }

        static string Revert(string id, bool save)
        {
            var scene = PlayScene(save); var home = DungeonHome(scene, id); string backup = save ? Backup(scene, "revert-" + id) : null; int put = 0;
            if (File.Exists(LedgerFile(id)))
            {
                var led = JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerFile(id)));
                foreach (var row in led.rows)
                {
                    var t = home.Find(row.path); var r = t != null ? t.GetComponent<Renderer>() : null; if (r == null) throw new Exception("ledger renderer gone: " + row.path);
                    var mats = row.materials.Select(p => AssetDatabase.LoadAssetAtPath<Material>(p)).ToArray(); if (mats.Any(m => m == null)) throw new Exception("ledger material gone on " + row.path);
                    r.sharedMaterials = mats; EditorUtility.SetDirty(r); put++;
                }
                File.Delete(LedgerFile(id));
            }
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName); var mine = root != null ? root.transform.Find(id) : null; bool had = mine != null;
            if (had) Object.DestroyImmediate(mine.gameObject);
            if (root != null && root.transform.childCount == 0) Object.DestroyImmediate(root);
            string say = "revert " + id + ": materials put back on " + put + " renderer(s), dress root " + (had ? "removed" : "absent");
            if (!save) return say;
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) return "FAILED the play scene was not saved";
            return "DungeonDress308 " + say + ", backup " + backup;
        }

        static string Check(string only)
        {
            var scene = PlayScene(false); var sb = new StringBuilder("DungeonDress308 check\n"); int pass = 0, fail = 0;
            void C(bool ok, string text) { sb.Append(ok ? "  [PASS] " : "  [FAIL] ").Append(text).Append('\n'); if (ok) pass++; else fail++; }
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            var ids = only != null ? new[] { only } : Directory.Exists(DressDir) ? Directory.GetFiles(DressDir, "*.json").Select(Path.GetFileNameWithoutExtension).ToArray() : Array.Empty<string>();
            if (root != null)
            {
                var cols = root.GetComponentsInChildren<Collider>(true);
                C(cols.All(c => c is BoxCollider && c.transform.parent != null && c.transform.parent.parent == root.transform && c.GetComponent<NavMeshObstacle>() is NavMeshObstacle o && o.carving),
                    "colliders only on solid hero props, each with a carving obstacle (" + cols.Length + "; no bake)");
                var lights = root.GetComponentsInChildren<Light>(true);
                C(lights.All(l => l.type == LightType.Point && l.transform.parent != null && l.transform.parent.name.StartsWith("Lamp_", StringComparison.Ordinal)), "발광 상한: the only lights are lanterns (" + lights.Length + ")");
            }
            foreach (var id in ids)
            {
                var data = ReadData(id); var home = DungeonHome(scene, id); var mine = root != null ? root.transform.Find(id) : null; bool applied = File.Exists(LedgerFile(id));
                if (mine == null) { C(!applied, id + ": not applied (no dress root" + (applied ? ", but a ledger is left" : ", no ledger") + ")"); continue; }
                var led = applied ? JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerFile(id))) : null;
                C(led != null && led.colliderDigest == ColliderDigest(home), id + ": the dungeon's colliders are what they were before the pass (" + (led != null ? led.colliderDigest : "no ledger") + ")");
                var children = mine.Cast<Transform>().ToArray();
                var missing = data.items.Where(i => children.Count(t => t.name == i.id) != 1).Select(i => i.id).ToArray();
                C(missing.Length == 0 && children.Count(t => t.name.StartsWith("Lamp_", StringComparison.Ordinal)) == data.lamps.Length && children.Length == data.items.Length + data.lamps.Length,
                    id + ": one object per item row and lantern (" + data.items.Length + " + " + data.lamps.Length + (missing.Length > 0 ? "; missing " + string.Join(", ", missing.Take(6)) : "") + ")");
                var perCell = data.lamps.GroupBy(l => l.cell).Where(g => g.Count() > data.lampsPerCell).Select(g => g.Key).ToArray();
                C(perCell.Length == 0, id + ": at most " + data.lampsPerCell + " lanterns a room (D308-62)" + (perCell.Length > 0 ? " - over: " + string.Join(", ", perCell) : ""));
                var bmin = V(data.boundsMin); var bmax = V(data.boundsMax); var outside = new List<string>(); var blocking = new List<string>(); var clutter = new List<string>();
                foreach (var t in children)
                {
                    var rs = t.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) continue; var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
                    if (b.min.x < bmin.x || b.min.y < bmin.y || b.min.z < bmin.z || b.max.x > bmax.x || b.max.y > bmax.y || b.max.z > bmax.z) outside.Add(t.name);
                    if (t.name.StartsWith("Lamp_", StringComparison.Ordinal)) continue;
                    foreach (var k in data.keep)
                    {
                        var c = V(k.pos); var q = new Vector3(Mathf.Clamp(c.x, b.min.x, b.max.x), 0f, Mathf.Clamp(c.z, b.min.z, b.max.z));
                        if (new Vector2(q.x - c.x, q.z - c.z).magnitude < k.r && b.min.y < c.y + k.h && b.max.y > c.y + .05f) { blocking.Add(t.name + " @ " + k.what); break; }
                    }
                    foreach (var w in data.walk)
                    {
                        var a = V(w.min); var z = V(w.max);
                        if (b.max.x > a.x && b.min.x < z.x && b.max.z > a.z && b.min.z < z.z && b.min.y < a.y + 2.1f && b.max.y > a.y + w.clutter) { clutter.Add(t.name + " in " + w.what + " (" + F(b.max.y - a.y) + " m)"); break; }
                    }
                }
                C(outside.Count == 0, id + ": everything inside the dungeon's box" + (outside.Count > 0 ? " - out: " + string.Join(", ", outside.Take(6)) : ""));
                C(blocking.Count == 0, id + ": no prop in a keep-out (door · rewards · ledges · enemies · lantern sight)" + (blocking.Count > 0 ? " - " + string.Join("; ", blocking.Take(8)) : ""));
                C(clutter.Count == 0, id + ": the walking lines hold only flat floor pieces" + (clutter.Count > 0 ? " - " + string.Join("; ", clutter.Take(8)) : ""));
                var swapped = data.mats.Length == 0 || (led != null && led.rows.Length > 0 && led.rows.All(r => { var t = home.Find(r.path); var rr = t != null ? t.GetComponent<Renderer>() : null; return rr != null && rr.sharedMaterials.Any(m => m != null && data.mats.Any(s => AssetDatabase.GetAssetPath(m) == s.to)); }));
                C(swapped, id + ": the material swaps stand (" + (led != null ? led.rows.Length : 0) + " renderer(s))");
            }
            return sb.Append(pass).Append(" checks, ").Append(fail).Append(" failed").ToString();
        }
    }
}
