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
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 temple kit in the world (D308-42 / 43, SPEC-ARCH-TEMPLE-308 §8). Every value [TEST].
    // Puts dressed temple prefabs (Temple308S_*) where the #296 venue used government-office halls, and switches those halls OFF
    // (never deletes them; revert switches them back on). Owns ONE root in the play scene (Temple308World) and a ledger
    // (Tools/Unity/Stage308_temple/world_ledger.json). The realm scenes must be open (RealmStream308 open-all): the halls live there.
    //   scan:<name part>      objects under any open scene whose name contains the part: path, bounds, active
    //   plan | apply | revert | check      data = Tools/Unity/Stage308_temple/world308.json
    public static class TempleWorld308
    {
        const string RootName = "Temple308World", PrefabDir = "Assets/_Project/Art/World/Temple308/Prefabs";
        static string Stage => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Tools", "Unity", "Stage308_temple"));
        [Serializable] sealed class Place { public string id = "", prefab = "", anchor = ""; public float[] offset = { 0, 0, 0 }; public float[] at = Array.Empty<float>(); public float yaw; public float scale = 1f; public bool ground = true; }
        [Serializable] sealed class Data { public string playScene = "W_Demo_Main"; public string[] hide = Array.Empty<string>(); public Place[] places = Array.Empty<Place>(); }
        [Serializable] sealed class Ledger { public string time = ""; public List<string> hidden = new List<string>(); }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only";
                if (c.StartsWith("scan:", StringComparison.Ordinal)) return Scan(c.Substring(5));
                if (c.StartsWith("tree:", StringComparison.Ordinal))
                {
                    var top = Find(c.Substring(5)); if (top == null) return "REFUSED not found";
                    var lines = new StringBuilder("tree " + PathOf(top) + "\n");
                    foreach (Transform t in top)
                    {
                        var b = BoundsOf(t.gameObject);
                        lines.Append("  ").Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)");
                        if (b != null) lines.Append(" | y ").Append(F(b.Value.min.y)).Append("..").Append(F(b.Value.max.y)).Append(" size ").Append(F(b.Value.size.x)).Append('x').Append(F(b.Value.size.z)).Append(" r ").Append(t.GetComponentsInChildren<Renderer>(true).Length).Append(" c ").Append(t.GetComponentsInChildren<Collider>(true).Length);
                        lines.Append('\n');
                    }
                    return lines.ToString().TrimEnd();
                }
                if (c.StartsWith("move:", StringComparison.Ordinal))
                {
                    // move:<path>:<x>,<y>,<z>  or  move:<path>:+<dy>   one content object of the play scene (the content asset is edited by hand, see SPEC §10)
                    var parts = c.Substring(5).Split(':'); var t = Find(parts[0]); if (t == null) return "REFUSED not found " + parts[0];
                    Vector3 was = t.position;
                    if (parts[1].StartsWith("+", StringComparison.Ordinal)) t.position += Vector3.up * float.Parse(parts[1].Substring(1), CultureInfo.InvariantCulture);
                    else { var v = parts[1].Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); t.position = new Vector3(v[0], v[1], v[2]); }
                    EditorSceneManager.MarkSceneDirty(t.gameObject.scene); EditorSceneManager.SaveScene(t.gameObject.scene);
                    File.AppendAllText(Path.Combine(Stage, "content_moves.txt"), DateTime.Now.ToString("s") + " " + PathOf(t) + " " + was.ToString("F3") + " -> " + t.position.ToString("F3") + (char)10);
                    return "moved " + PathOf(t) + " " + was.ToString("F2") + " -> " + t.position.ToString("F2");
                }
                if (c == "plan") return Apply(true);
                if (c == "apply") return Apply(false);
                if (c == "revert") return Revert();
                if (c == "check") return Check();
                return "REFUSED scan:<name part> | plan | apply | revert | check";
            }
            catch (Exception e) { return "REFUSED " + e.GetType().Name + ": " + e.Message; }
        }

        static IEnumerable<Scene> Open() { for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isLoaded) yield return s; } }
        static string PathOf(Transform t) { var parts = new List<string>(); for (var x = t; x != null; x = x.parent) parts.Add(x.name); parts.Reverse(); return t.gameObject.scene.name + ":" + string.Join("/", parts); }
        static string F(float v) => v.ToString("F1", CultureInfo.InvariantCulture);
        static Bounds? BoundsOf(GameObject g)
        {
            Bounds? b = null; foreach (var r in g.GetComponentsInChildren<Renderer>(true)) { if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; } }
            return b;
        }
        static IEnumerable<Transform> All() => Open().SelectMany(s => s.GetRootGameObjects()).SelectMany(g => g.GetComponentsInChildren<Transform>(true));
        static Transform Find(string path) => All().FirstOrDefault(t => PathOf(t) == path) ?? All().FirstOrDefault(t => PathOf(t).EndsWith(path, StringComparison.Ordinal));

        static string Scan(string part)
        {
            var sb = new StringBuilder("scan " + part + "\n"); int n = 0;
            foreach (var t in All().Where(t => t.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                if (t.parent != null && t.parent.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) continue;   // top-most match only
                var b = BoundsOf(t.gameObject); if (++n > 60) break;
                sb.Append("  ").Append(PathOf(t)).Append(t.gameObject.activeInHierarchy ? "" : " (off)");
                if (b != null) sb.Append(" | centre ").Append(F(b.Value.center.x)).Append(',').Append(F(b.Value.min.y)).Append(',').Append(F(b.Value.center.z)).Append(" size ").Append(F(b.Value.size.x)).Append('x').Append(F(b.Value.size.y)).Append('x').Append(F(b.Value.size.z))
                    .Append(" yaw ").Append(F(t.eulerAngles.y)).Append(" renderers ").Append(t.GetComponentsInChildren<Renderer>(true).Length);
                sb.Append('\n');
            }
            return sb.Append("  matches ").Append(n).ToString();
        }

        static Data Read() => JsonUtility.FromJson<Data>(File.ReadAllText(Path.Combine(Stage, "world308.json")));
        static Scene Play(Data d) { var s = SceneManager.GetSceneByName(d.playScene); if (!s.isLoaded) throw new Exception(d.playScene + " is not open"); return s; }

        static float GroundY(Vector3 p)
        {
            // the terrain colliders are in the play scene: highest hit under the point that is not one of our own pieces
            Physics.SyncTransforms();
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, 600f, p.z), Vector3.down, 800f).OrderBy(h => h.distance))
            {
                Transform t = hit.collider.transform; bool ours = false;
                for (var x = t; x != null; x = x.parent) if (x.name == RootName) { ours = true; break; }
                if (!ours && !hit.collider.isTrigger) return hit.point.y;
            }
            return p.y;
        }

        static string PrefabPath(string prefab) => prefab.StartsWith("Assets/", StringComparison.Ordinal) ? prefab
            : (prefab.StartsWith("Steam_", StringComparison.Ordinal) ? "Assets/_Project/Art/World/SteamKit308/Prefabs" : PrefabDir) + "/" + prefab + ".prefab";

        static string Apply(bool dry)
        {
            var d = Read(); var scene = Play(d); var sb = new StringBuilder("TempleWorld308 " + (dry ? "plan" : "apply") + "\n");
            var hide = new List<Transform>();
            foreach (string path in d.hide) { var t = Find(path); if (t == null) sb.Append("  MISSING hide ").Append(path).Append('\n'); else hide.Add(t); }
            foreach (var p in d.places)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(p.prefab)); var anchor = p.anchor.Length > 0 ? Find(p.anchor) : null;
                if (prefab == null) { sb.Append("  MISSING prefab ").Append(p.prefab).Append('\n'); continue; }
                if (anchor == null && p.at.Length != 3) { sb.Append("  MISSING anchor ").Append(p.anchor).Append('\n'); continue; }
            }
            if (sb.ToString().Contains("MISSING")) return sb.Append("  REFUSED: fix the data").ToString();
            if (dry) { foreach (var t in hide) sb.Append("  hide ").Append(PathOf(t)).Append('\n'); foreach (var p in d.places) sb.Append("  place ").Append(p.id).Append(" = ").Append(p.prefab).Append(" at ").Append(p.anchor).Append('\n'); return sb.ToString().TrimEnd(); }

            string backup = Path.Combine(Stage, "Backup", DateTime.Now.ToString("yyyyMMddTHHmmss") + "_world"); Directory.CreateDirectory(backup);
            var touched = new HashSet<Scene> { scene }; foreach (var t in hide) touched.Add(t.gameObject.scene);
            foreach (var s in touched) File.Copy(Path.GetFullPath(s.path), Path.Combine(backup, Path.GetFileName(s.path)), true);

            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName); if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene);
            sb.Append("  ").Append(BuildGround(root)).Append((char)10);
            foreach (var p in d.places)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(p.prefab)); var anchor = p.anchor.Length > 0 ? Find(p.anchor) : null;
                Vector3 at;
                if (p.at.Length == 3) at = new Vector3(p.at[0], p.at[1], p.at[2]);
                else { var b = BoundsOf(anchor.gameObject); at = b != null ? new Vector3(b.Value.center.x, b.Value.min.y, b.Value.center.z) : anchor.position; }
                at += new Vector3(p.offset[0], 0f, p.offset[2]); if (p.ground) at.y = GroundY(at); at.y += p.offset[1];
                var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform); g.name = p.id;
                g.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, p.yaw, 0f)); g.transform.localScale = Vector3.one * p.scale; g.isStatic = true;
                foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
                if (p.prefab.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    // pack prefabs keep the place they had in their demo scene: seat the drawn stone itself on the point
                    var rb = BoundsOf(g); if (rb != null) g.transform.position += new Vector3(at.x - rb.Value.center.x, at.y - rb.Value.min.y, at.z - rb.Value.center.z);
                }
                if (g.GetComponentsInChildren<Collider>(true).Length == 0)
                {
                    // pack pieces without a collider (stone lanterns, pagodas): seated on their own lowest point, one box INSIDE the stone (70 % of the footprint)
                    var pb = BoundsOf(g); if (pb != null)
                    {
                        g.transform.position += Vector3.up * (at.y - pb.Value.min.y); pb = BoundsOf(g);
                        var box = g.AddComponent<BoxCollider>(); box.center = g.transform.InverseTransformPoint(pb.Value.center);
                        var size = g.transform.InverseTransformVector(new Vector3(pb.Value.size.x * .7f, pb.Value.size.y, pb.Value.size.z * .7f)); box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                    }
                }
                sb.Append("  placed ").Append(p.id).Append(" at ").Append(F(at.x)).Append(',').Append(F(at.y)).Append(',').Append(F(at.z)).Append(" yaw ").Append(F(p.yaw)).Append('\n');
            }
            string ledgerFile = Path.Combine(Stage, "world_ledger.json"); var ledger = File.Exists(ledgerFile) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(ledgerFile)) : new Ledger(); ledger.time = DateTime.Now.ToString("s");   // a second apply keeps what the first one switched off
            foreach (var t in hide) { if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); ledger.hidden.Add(PathOf(t)); EditorSceneManager.MarkSceneDirty(t.gameObject.scene); } }
            File.WriteAllText(Path.Combine(Stage, "world_ledger.json"), JsonUtility.ToJson(ledger, true));
            EditorSceneManager.MarkSceneDirty(scene);
            foreach (var s in touched) if (!EditorSceneManager.SaveScene(s)) throw new Exception("scene not saved: " + s.name);
            return sb.Append("  hidden ").Append(ledger.hidden.Count).Append(", backup ").Append(backup).ToString();
        }

        // the temple ground (Tools/Art/templeground308.py): earth terraces, rubble walls, cap stones, stairs + one collision mesh
        [Serializable] sealed class Sub { public string material = ""; public int[] triangles = Array.Empty<int>(); }
        [Serializable] sealed class MeshData { public string name = ""; public float[] vertices = Array.Empty<float>(), normals = Array.Empty<float>(), uvs = Array.Empty<float>(); public Sub[] submeshes = Array.Empty<Sub>(); }
        const string GroundDir = "Assets/_Project/Art/World/Temple308/Ground";
        static readonly Color EarthTint = new Color(.58f, .54f, .45f);   // [TEST] packed yard earth, tuned against stills
        static string BuildGround(GameObject root)
        {
            string data = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Art", "World", "Compact", "Rebuild", "TempleGround308", "Meshes"));
            if (!Directory.Exists(data)) return "ground: no data (run Tools/Art/templeground308.py)";
            if (!AssetDatabase.IsValidFolder(GroundDir)) AssetDatabase.CreateFolder("Assets/_Project/Art/World/Temple308", "Ground");
            var ground = new GameObject("Ground"); ground.transform.SetParent(root.transform, false); ground.isStatic = true; int n = 0;
            foreach (string file in Directory.GetFiles(data, "*.json").OrderBy(x => x))
            {
                var d = JsonUtility.FromJson<MeshData>(File.ReadAllText(file)); int count = d.vertices.Length / 3;
                var v = new Vector3[count]; var nn = new Vector3[count]; var uv = new Vector2[count];
                for (int i = 0; i < count; i++) { v[i] = new Vector3(d.vertices[3 * i], d.vertices[3 * i + 1], d.vertices[3 * i + 2]); nn[i] = new Vector3(d.normals[3 * i], d.normals[3 * i + 1], d.normals[3 * i + 2]); uv[i] = new Vector2(d.uvs[2 * i], d.uvs[2 * i + 1]); }
                string path = GroundDir + "/" + d.name + ".asset"; AssetDatabase.DeleteAsset(path);
                var mesh = new Mesh { name = d.name, indexFormat = count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(v); mesh.SetNormals(nn); mesh.SetUVs(0, uv); mesh.subMeshCount = d.submeshes.Length;
                for (int k = 0; k < d.submeshes.Length; k++) mesh.SetTriangles(d.submeshes[k].triangles, k, false);
                mesh.RecalculateBounds(); mesh.RecalculateTangents(); AssetDatabase.CreateAsset(mesh, path);
                var g = new GameObject(d.name.Replace("TempleGround_", "")); g.transform.SetParent(ground.transform, false); g.isStatic = true; n++;
                if (d.name.EndsWith("_Collision", StringComparison.Ordinal)) { g.AddComponent<MeshCollider>().sharedMesh = mesh; continue; }
                g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterials = d.submeshes.Select(x => GroundMaterial(x.material)).ToArray();
                r.shadowCastingMode = d.name.EndsWith("_Earth", StringComparison.Ordinal) ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return "ground: " + n + " meshes";
        }

        static Material GroundMaterial(string slot)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/Finish297/Materials/" + slot + ".mat"); if (m != null) return m;
            string path = GroundDir + "/" + slot + ".mat"; m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = slot }; AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", EarthTint); m.SetFloat("_Smoothness", .04f); m.SetFloat("_Metallic", 0f); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); return m;
        }

        static string Revert()
        {
            var d = Read(); var scene = Play(d); string file = Path.Combine(Stage, "world_ledger.json"); int back = 0; var touched = new HashSet<Scene> { scene };
            if (File.Exists(file))
            {
                var ledger = JsonUtility.FromJson<Ledger>(File.ReadAllText(file));
                foreach (string path in ledger.hidden) { var t = Find(path); if (t != null) { t.gameObject.SetActive(true); touched.Add(t.gameObject.scene); EditorSceneManager.MarkSceneDirty(t.gameObject.scene); back++; } }
                File.Move(file, Path.Combine(Stage, "world_ledger_reverted_" + DateTime.Now.ToString("yyyyMMddTHHmmss") + ".json"));
            }
            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName); if (old != null) { Object.DestroyImmediate(old); EditorSceneManager.MarkSceneDirty(scene); }
            foreach (var s in touched) EditorSceneManager.SaveScene(s);
            return "TempleWorld308 revert: " + back + " switched back on, root " + (old != null ? "removed" : "absent");
        }

        static string Check()
        {
            var d = Read(); var scene = Play(d); var sb = new StringBuilder("TempleWorld308 check\n"); int pass = 0, fail = 0;
            void Line(bool ok, string id, string text) { sb.Append(ok ? "  PASS " : "  FAIL ").Append(id).Append(' ').Append(text).Append('\n'); if (ok) pass++; else fail++; }
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            Line(root != null && root.transform.childCount == d.places.Length + 1, "AC-T1", "root with the ground and " + (root == null ? 0 : root.transform.childCount - 1) + " of " + d.places.Length + " pieces");
            if (root == null) return sb.ToString();
            var lights = root.GetComponentsInChildren<Light>(true); var scripts = root.GetComponentsInChildren<MonoBehaviour>(true).Where(b => b == null || b.GetType().Name != "UniversalAdditionalLightData").ToArray();
            Line(scripts.Length == 0 && lights.All(l => l.name == "LampLight" && l.type == LightType.Point && l.shadows == LightShadows.None), "AC-T2", "no script; lights only on lamps (D308-48): " + lights.Length + " warm point lights, no shadow");
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            Line(renderers.All(r => r.sharedMaterials.All(m => m != null && !m.IsKeywordEnabled("_EMISSION"))), "AC-T3", renderers.Length + " renderers, every material present and none emissive");
            var colliders = root.GetComponentsInChildren<Collider>(true);
            Line(colliders.Length > 0 && colliders.All(c => !c.isTrigger && ((c is MeshCollider mc && mc.sharedMesh != null) || (c is BoxCollider && c.GetComponentsInChildren<Renderer>(true).Length > 0))), "AC-T4", colliders.Length + " colliders (" + colliders.Count(c => c is BoxCollider) + " boxes, each inside its own stone), no trigger");
            int still = d.hide.Count(p => { var t = Find(p); return t != null && t.gameObject.activeSelf; });
            Line(still == 0, "AC-T5", "replaced halls switched off (" + (d.hide.Length - still) + " of " + d.hide.Length + ")");
            long verts = renderers.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null).Sum(f => (long)f.sharedMesh.vertexCount);
            long bytes = renderers.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null).Select(f => f.sharedMesh).Concat(colliders.OfType<MeshCollider>().Select(c => c.sharedMesh)).Distinct().Sum(m => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m));
            sb.Append("  info vertices ").Append(verts.ToString("N0", CultureInfo.InvariantCulture)).Append(", mesh memory ").Append(F(bytes / 1048576f)).Append(" MB\n");
            return sb.Append("  checks, ").Append(pass).Append(" pass ").Append(fail).Append(" fail").ToString();
        }
    }
}
