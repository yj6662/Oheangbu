using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 steam-temple part kit on the buildings (D308-46, SPEC-ARCH-TEMPLE-308 §13). Every value [TEST].
    // The parts are Meshy models brought to a mating standard by Tools/Blender/Temple308/meshy_parts.py (Art/World/Temple308/Kit:
    // Meshes/<part>.json in Unity axes, Textures/<part>_{BC,N,MS}.png). This tool owns Assets/_Project/Art/World/Temple308/Kit
    // (mesh assets, textures, one material per part, Kit_<part> prefabs) and the prefabs Temple308K_* (a plain Temple308_* building
    // + a "Kit" child, or a stand-alone prop). Nothing else is changed: the pack prefabs, Temple308_* and Temple308S_* stay as they are.
    // The machinery follows the building: pillars are found from the pillar meshes, the roof surface from the roof's vertices.
    // No emission, no light, no script (ART-LIGHT cap). Stack parts: origin bottom centre, front = -Z. Wheel parts: origin hub, axis Z.
    //   import          mesh assets, textures, materials, Kit_<part> prefabs
    //   scan:<name>     the renderers of Temple308_<name> (name, bounds) and the pillars found
    //   dress           Temple308K_* : lantern, sutra wheel, boiler, engine, every hall, the bell pavilion (rules in KitDress308.Rules.cs)
    //   finish          LODGroup + colliders on every Temple308K_* (SteamKit308.Finish)
    //   sheet           pictures of every Temple308K_* -> Art/World/Temple308/Unity
    //   shot:<prefix>   the same pictures of any prefab under Temple308/Prefabs whose name starts with the prefix
    public static partial class KitDress308
    {
        const string Dir = "Assets/_Project/Art/World/Temple308/Kit", TempleDir = "Assets/_Project/Art/World/Temple308/Prefabs", SteamMat = "Assets/_Project/Art/World/SteamKit308/Materials/";
        static string DataRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Art", "World", "Temple308"));
        [Serializable] sealed class Sub { public string material = ""; public int[] triangles = Array.Empty<int>(); }
        [Serializable] sealed class MeshData { public string name = ""; public float[] vertices = Array.Empty<float>(), normals = Array.Empty<float>(), uvs = Array.Empty<float>(); public Sub[] submeshes = Array.Empty<Sub>(); }
        sealed class Pillar { public Vector3 c; public float r, top; }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only";
                if (c == "import") return Import();
                if (c.StartsWith("scan:", StringComparison.Ordinal)) return Scan(c.Substring(5));
                if (c == "dress") return Dress();
                if (c == "finish") return SteamKit308.Finish("Temple308K_", "Kit");
                if (c == "pivots") return Pivots();
                if (c == "sheet") return Sheet("Temple308K_");
                if (c.StartsWith("shot:", StringComparison.Ordinal)) return Sheet(c.Substring(5));
                return "REFUSED import | scan:<name> | dress | finish | sheet | shot:<prefix>";
            }
            catch (Exception e) { return "REFUSED " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace; }
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Texture2D Tex(string name, bool linear, bool normal)
        {
            string source = Path.Combine(DataRoot, "Kit", "Textures", name + ".png"); if (!File.Exists(source)) return null;
            Folder(Dir + "/Textures"); string path = Dir + "/Textures/" + name + ".png";
            File.Copy(source, Path.GetFullPath(path), true); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.sRGBTexture == linear || importer.maxTextureSize != 1024 || importer.textureType != type) { importer.textureType = type; importer.sRGBTexture = !linear; importer.maxTextureSize = 1024; importer.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Plain(string name, Color color, float metallic, float smoothness)
        {
            Folder(Dir + "/Materials"); string path = Dir + "/Materials/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness); m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); return m;
        }

        static string Import()
        {
            string meshDir = Path.Combine(DataRoot, "Kit", "Meshes");
            if (!Directory.Exists(meshDir)) return "REFUSED no data under " + meshDir + " (run Tools/Blender/Temple308/meshy_parts.py)";
            Folder(Dir + "/Meshes"); Folder(Dir + "/Prefabs"); Folder(Dir + "/Materials"); var sb = new StringBuilder("KitDress308 import\n");
            foreach (string file in Directory.GetFiles(meshDir, "*.json").OrderBy(x => x))
            {
                var d = JsonUtility.FromJson<MeshData>(File.ReadAllText(file)); int n = d.vertices.Length / 3;
                var v = new Vector3[n]; var nn = new Vector3[n]; var uv = new Vector2[n];
                for (int i = 0; i < n; i++) { v[i] = new Vector3(d.vertices[3 * i], d.vertices[3 * i + 1], d.vertices[3 * i + 2]); nn[i] = new Vector3(d.normals[3 * i], d.normals[3 * i + 1], d.normals[3 * i + 2]); uv[i] = new Vector2(d.uvs[2 * i], d.uvs[2 * i + 1]); }
                string meshPath = Dir + "/Meshes/" + d.name + ".asset"; var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath); bool fresh = mesh == null;
                if (fresh) mesh = new Mesh { name = d.name }; else mesh.Clear();
                mesh.indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(v); mesh.SetNormals(nn); mesh.SetUVs(0, uv); mesh.SetTriangles(d.submeshes[0].triangles, 0, false); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                if (fresh) AssetDatabase.CreateAsset(mesh, meshPath); else { EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); }
                string matPath = Dir + "/Materials/" + d.name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = d.name }; AssetDatabase.CreateAsset(m, matPath); }
                m.SetColor("_BaseColor", Color.white); m.SetTexture("_BaseMap", Tex(d.name + "_BC", false, false));
                var nm = Tex(d.name + "_N", true, true); if (nm != null) { m.SetTexture("_BumpMap", nm); m.EnableKeyword("_NORMALMAP"); }
                var ms = Tex(d.name + "_MS", true, false);
                if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", .8f); m.SetFloat("_SmoothnessTextureChannel", 0f); }
                m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
                var go = new GameObject("Kit_" + d.name); go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.On;
                PrefabUtility.SaveAsPrefabAsset(go, Dir + "/Prefabs/" + go.name + ".prefab"); Object.DestroyImmediate(go);
                sb.Append("  ").Append(d.name).Append(' ').Append(mesh.bounds.size.ToString("F2")).Append(" vertices ").Append(n).Append('\n');
            }
            Plain("Kit_Granite", new Color(.52f, .52f, .50f), 0f, .12f); Plain("Kit_Wood", new Color(.20f, .13f, .09f), 0f, .2f);
            Plain("Kit_Copper", new Color(.42f, .25f, .17f), .8f, .36f); Plain("Kit_Iron", new Color(.10f, .10f, .11f), .7f, .3f); Plain("Kit_Brass", new Color(.55f, .42f, .20f), .9f, .5f);
            return sb.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- reading a building
        static bool Has(string name, string part) => name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        static Bounds? BoundsOf(IEnumerable<Renderer> rs) { Bounds? b = null; foreach (var r in rs) { if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; } } return b; }
        static List<Vector3> Verts(IEnumerable<Renderer> rs)
        {
            var list = new List<Vector3>();
            foreach (var r in rs) { var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue; var m = r.transform.localToWorldMatrix; foreach (var v in f.sharedMesh.vertices) list.Add(m.MultiplyPoint3x4(v)); }
            return list;
        }
        static float SurfaceY(List<Vector3> verts, float x, float z, float radius, float fallback)
        {
            float best = float.NegativeInfinity, r2 = radius * radius;
            foreach (var v in verts) { float dx = v.x - x, dz = v.z - z; if (dx * dx + dz * dz <= r2 && v.y > best) best = v.y; }
            return float.IsNegativeInfinity(best) ? fallback : best;
        }

        // Pillars: in this pack every pillar is a renderer of its own (wide "pillar" pieces are stone bases with braces, not pillars).
        static List<Pillar> Pillars(Renderer[] all, out float foot)
        {
            var list = all.Where(r => Has(r.name, "Pillar") && r.bounds.size.x < .9f && r.bounds.size.z < .9f && r.bounds.size.y > 1.5f)
                          .Select(r => new Pillar { c = new Vector3(r.bounds.center.x, r.bounds.min.y, r.bounds.center.z), r = Mathf.Max(r.bounds.size.x, r.bounds.size.z) / 2f, top = r.bounds.max.y }).ToList();
            foot = list.Count > 0 ? list.Min(p => p.c.y) : 0f; return list;
        }

        static GameObject Body(string name, Transform holder, out Renderer[] all, out Bounds B)
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TempleDir + "/Temple308_" + name + ".prefab"), holder);
            all = body.GetComponentsInChildren<Renderer>(true); B = BoundsOf(all).Value;
            var steps = BoundsOf(all.Where(r => Has(r.name, "Step")));   // front = +Z: the entrance steps tell which side the hall faces
            float yaw = 0f;
            if (steps != null) { Vector3 d = steps.Value.center - B.center; d.y = 0f; if (d.magnitude > 1f) yaw = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? (d.x > 0 ? -90f : 90f) : (d.z > 0 ? 0f : 180f); }
            body.transform.RotateAround(new Vector3(B.center.x, 0f, B.center.z), Vector3.up, yaw); B = BoundsOf(all).Value;
            body.transform.position -= new Vector3(B.center.x, B.min.y, B.center.z); B = BoundsOf(all).Value;
            return body;
        }

        static string Scan(string name)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var holder = new GameObject("scan"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);
                Body(name, holder.transform, out var all, out var B); var sb = new StringBuilder("scan " + name + " bounds " + B.min.ToString("F2") + " .. " + B.max.ToString("F2") + "\n");
                foreach (var g in all.GroupBy(r => r.name).OrderBy(g => g.Key)) { var b = BoundsOf(g).Value; sb.Append("  ").Append(g.Key).Append(" x").Append(g.Count()).Append(' ').Append(b.min.ToString("F2")).Append(" .. ").Append(b.max.ToString("F2")).Append('\n'); }
                var ps = Pillars(all, out float foot); sb.Append("  pillars ").Append(ps.Count).Append(" foot ").Append(foot.ToString("F2")).Append('\n');
                foreach (var p in ps.OrderBy(p => p.c.z).ThenBy(p => p.c.x)) sb.Append("    ").Append(p.c.x.ToString("F2")).Append(',').Append(p.c.z.ToString("F2")).Append(" r ").Append(p.r.ToString("F2")).Append(" top ").Append(p.top.ToString("F2")).Append('\n');
                return sb.ToString().TrimEnd();
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }

        // pivots - where the building itself sits in the old dressed prefab (Temple308S_*) and in the new one (Temple308K_*): the world
        // placements were made for the old pivots, so the two must agree before the prefabs are swapped.
        static string Pivots()
        {
            var sb = new StringBuilder("pivots (building bounds centre x,z / min y / size x,z)").Append((char)10);
            foreach (string name in Halls.Concat(new[] { "Song_Jong" }))
            {
                sb.Append("  ").Append(name);
                foreach (string prefix in new[] { "Temple308S_", "Temple308K_" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TempleDir + "/" + prefix + name + ".prefab"); if (prefab == null) { sb.Append(" | missing"); continue; }
                    var body = prefab.transform.GetChild(0); var b = BoundsOf(body.GetComponentsInChildren<Renderer>(true)).Value;
                    sb.Append(" | ").Append(b.center.x.ToString("F2")).Append(',').Append(b.center.z.ToString("F2")).Append(" / ").Append(b.min.y.ToString("F2")).Append(" / ").Append(b.size.x.ToString("F1")).Append(',').Append(b.size.z.ToString("F1"));
                }
                sb.Append((char)10);
            }
            return sb.ToString().TrimEnd();
        }

        // sheet - Temple308K_* from three sides, in a preview scene of its own
        static string Sheet(string filter)
        {
            string outDir = Path.Combine(DataRoot, "Unity"); Directory.CreateDirectory(outDir); const int w = 1600, h = 1200; int made = 0;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene(); var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                var lightGo = new GameObject("L"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene); var key = lightGo.AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.7f; key.shadows = LightShadows.Soft;
                var fillGo = new GameObject("F"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fillGo, scene); var fill = fillGo.AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = 1.0f; fill.color = new Color(.85f, .9f, 1f);
                var camGo = new GameObject("C"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.86f, .85f, .80f); cam.fieldOfView = 28f; cam.targetTexture = rt; cam.enabled = false; cam.nearClipPlane = .3f; cam.farClipPlane = 600f;
                foreach (string guid in AssetDatabase.FindAssets(filter + " t:Prefab", new[] { TempleDir }))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); if (!prefab.name.StartsWith(filter, StringComparison.Ordinal)) continue; var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var b = BoundsOf(go.GetComponentsInChildren<Renderer>(true)).Value; float dist = b.extents.magnitude / Mathf.Sin(cam.fieldOfView * .5f * Mathf.Deg2Rad) * .7f;
                    foreach (var (yaw, pitch, tag) in prefab.name.Contains("Song_Jong") ? new[] { (160f, 12f, "front"), (125f, 12f, "back"), (240f, 12f, "left"), (40f, 14f, "far") } : new[] { (225f, 12f, "front"), (305f, 12f, "back"), (150f, 12f, "left"), (40f, 14f, "far") })
                    {
                        var dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward; cam.transform.position = b.center - dir * dist; cam.transform.rotation = Quaternion.LookRotation(dir);
                        fill.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(25f, yaw + 150f, 0f) * Vector3.forward); key.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(40f, yaw - 35f, 0f) * Vector3.forward);
                        cam.Render(); if (made == 0) cam.Render();
                        var prev = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = prev;
                        File.WriteAllBytes(Path.Combine(outDir, prefab.name + "_" + tag + ".png"), tex.EncodeToPNG()); made++;
                    }
                    // close-ups: the camera stands outside the building, beyond the piece it looks at
                    var kit = go.transform.Find("Kit"); if (kit != null && go.transform.childCount > 1)
                        foreach (string target in new[] { "Cylinder", "Flywheel", "FireBox", "RidgeCollar" })
                        {
                            var t = kit.Cast<Transform>().FirstOrDefault(x => x.name == target); if (t == null) continue;
                            var tb = t.GetComponent<Renderer>().bounds; var look = tb.center + Vector3.up * (target == "Cylinder" ? 1.6f : target == "FireBox" ? 2.0f : .3f);
                            var away = look - b.center; away.y = 0f; if (away.magnitude < .5f) away = Vector3.back; away.Normalize();
                            var dir = Quaternion.Euler(0f, target == "Flywheel" ? -40f : 32f, 0f) * -away; dir = (dir + Vector3.down * .2f).normalized;
                            float d = target == "Cylinder" ? 12f : target == "FireBox" ? 13f : target == "RidgeCollar" ? 10f : 8f;
                            cam.transform.position = look - dir * d; cam.transform.rotation = Quaternion.LookRotation(dir);
                            key.transform.rotation = Quaternion.LookRotation((dir + Vector3.down * .7f + cam.transform.right * .5f).normalized); fill.transform.rotation = Quaternion.LookRotation((dir - cam.transform.right * .8f).normalized);
                            cam.Render();
                            var prev = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = prev;
                            File.WriteAllBytes(Path.Combine(outDir, prefab.name + "_close_" + target + ".png"), tex.EncodeToPNG()); made++;
                        }
                    // inside: from the opened front bay toward the shrine, and from beside the shrine back toward the door (lit by two preview-only lamps)
                    var inside = kit != null ? kit.Find("Interior") : null;
                    if (inside != null)
                    {
                        var c0 = inside.position; float fov0 = cam.fieldOfView; cam.fieldOfView = 62f; cam.nearClipPlane = .1f; var lamps = new List<GameObject>();
                        foreach (var off in new[] { new Vector3(-4f, 3.2f, 1.5f), new Vector3(4f, 3.2f, 1.5f), new Vector3(0f, 3.4f, -2.5f) })
                        { var lg = new GameObject("lamp"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lg, scene); lg.transform.position = c0 + off; var pl = lg.AddComponent<Light>(); pl.type = LightType.Point; pl.range = 16f; pl.intensity = 0f; /* the hall is seen by its own lamps (D308-48) */ pl.color = new Color(1f, .9f, .78f); lamps.Add(lg); }
                        foreach (var (pos, look, tag) in new[] { (c0 + new Vector3(0f, 1.7f, b.extents.z * .42f), c0 + new Vector3(0f, 2.0f, -b.extents.z * .5f), "inside_door"), (c0 + new Vector3(-b.extents.x * .52f, 1.7f, -b.extents.z * .3f), c0 + new Vector3(b.extents.x * .3f, 1.6f, b.extents.z * .2f), "inside_back"), (c0 + new Vector3(b.extents.x * .2f, 1.7f, b.extents.z * .1f), c0 + new Vector3(-b.extents.x * .25f, 1.9f, -b.extents.z * .45f), "inside_engine") })
                        {
                            cam.transform.position = pos; cam.transform.rotation = Quaternion.LookRotation((look - pos).normalized); cam.Render();
                            var prev = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = prev;
                            File.WriteAllBytes(Path.Combine(outDir, prefab.name + "_" + tag + ".png"), tex.EncodeToPNG()); made++;
                        }
                        foreach (var lg in lamps) Object.DestroyImmediate(lg); cam.fieldOfView = fov0; cam.nearClipPlane = .3f;
                    }
                    Object.DestroyImmediate(go);
                }
                cam.targetTexture = null;
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
            return "sheet: " + made + " pictures -> " + outDir;
        }
    }
}
