using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 temple kit (D308-42, SPEC-ARCH-TEMPLE-308): the Korea Heritage Service packs the user added on 2026-10-08
    // ("KHS - Korean temples" 268804, "KHS - Korean Traditional Stone Pagoda" 261899). Asset Store packs stay out of git
    // (Docs/ASSET-REIMPORT-MANIFEST.md). Queue-safe: never opens a dialog.
    //   import:<file name part>   import a downloaded .unitypackage from the local Asset Store cache (no dialog)
    //   status                    what is under the pack folders (counts by type, texture sizes)
    public static class TempleKit308
    {
        static readonly string[] Folders = { "Assets/TemplesKor", "Assets/StonePagoda" };
        static string Cache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store-5.x", "KOREA HERITAGE SERVICE");

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return Status();
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only";
                if (c.StartsWith("import:", StringComparison.Ordinal))
                {
                    string part = c.Substring(7);
                    var file = Directory.GetFiles(Cache, "*.unitypackage", SearchOption.AllDirectories).FirstOrDefault(f => Path.GetFileName(f).IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (file == null) return "REFUSED no package matching '" + part + "' under " + Cache;
                    AssetDatabase.ImportPackage(file, false);
                    return "import started: " + file + " (" + (new FileInfo(file).Length / 1048576f).ToString("F0") + " MB) - poll status";
                }
                if (c.StartsWith("cap:", StringComparison.Ordinal)) return Cap(int.Parse(c.Substring(4)));
                if (c == "urp") return Urp();
                if (c.StartsWith("urp:", StringComparison.Ordinal)) return Urp(c.Substring(4));
                if (c.StartsWith("sizes:", StringComparison.Ordinal)) return Sizes(c.Substring(6));
                if (c == "kit") return Kit();
                if (c == "lantern") return Lantern();
                if (c == "sheet") return Sheet();
                if (c.StartsWith("sheet:", StringComparison.Ordinal)) return Sheet(c.Substring(6));
                return "REFUSED import:<name part> | status | cap:<max> | urp | kit";
            }
            catch (Exception e) { return "REFUSED " + e.GetType().Name + ": " + e.Message; }
        }

        const string Pack = "Assets/TemplesKor", KitDir = "Assets/_Project/Art/World/Temple308";

        // cap:<max> - importer max size of the pack's textures (the pack ships 4K-8K PNG; the project cap is 2K, SPEC-EDITOR-MEMORY-308 F)
        static string Cap(int max)
        {
            int changed = 0, seen = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Pack }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid); var importer = AssetImporter.GetAtPath(path) as TextureImporter; if (importer == null) continue; seen++;
                    bool dirty = false;
                    if (importer.maxTextureSize > max) { importer.maxTextureSize = max; dirty = true; }
                    if (importer.textureCompression == TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Compressed; dirty = true; }
                    if (dirty) { importer.SaveAndReimport(); changed++; }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            return "cap " + max + ": " + changed + " of " + seen + " textures changed";
        }

        // urp - the pack's materials use the Built-in Standard shader: switch them to URP Lit keeping the maps
        static string Urp(string folder = Pack + "/Materials")
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit"); int changed = 0, seen = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)); if (m == null) continue; seen++;
                if (m.shader == lit) { if (m.GetFloat("_Smoothness") > .3f) { m.SetFloat("_Smoothness", .22f); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); changed++; } continue; }
                var main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null; var bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                var metal = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null; var occ = m.HasProperty("_OcclusionMap") ? m.GetTexture("_OcclusionMap") : null;
                Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white; float mode = m.HasProperty("_Mode") ? m.GetFloat("_Mode") : 0f; float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : .5f;
                m.shader = lit;
                m.SetTexture("_BaseMap", main); m.SetColor("_BaseColor", color);
                if (bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
                if (metal != null) { m.SetTexture("_MetallicGlossMap", metal); m.EnableKeyword("_METALLICSPECGLOSSMAP"); } else { m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", .15f); }
                if (occ != null) { m.SetTexture("_OcclusionMap", occ); m.EnableKeyword("_OCCLUSIONMAP"); }
                if (mode == 1f) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", cutoff); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; }
                EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); changed++;
            }
            return "urp: " + changed + " of " + seen + " materials switched to URP Lit";
        }

        // kit - one prefab per assembled building of the pack's Demo scene (the pack ships parts only), pivot at the footprint centre on the ground
        static string Kit()
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var demo = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Pack + "/Scenes/Demo.unity", UnityEditor.SceneManagement.OpenSceneMode.Additive);
            var sb = new StringBuilder("kit\n"); int made = 0;
            try
            {
                if (!AssetDatabase.IsValidFolder(KitDir)) AssetDatabase.CreateFolder("Assets/_Project/Art/World", "Temple308");
                if (!AssetDatabase.IsValidFolder(KitDir + "/Prefabs")) AssetDatabase.CreateFolder(KitDir, "Prefabs");
                foreach (var root in demo.GetRootGameObjects())
                {
                    var renderers = root.GetComponentsInChildren<MeshRenderer>(true); if (renderers.Length == 0 || root.name == "Floor") continue;
                    var groups = root.name == "Etc" ? root.transform.Cast<Transform>().Select(t => t.gameObject).ToArray() : new[] { root };
                    foreach (var group in groups)
                    {
                        var rs = group.GetComponentsInChildren<MeshRenderer>(true); if (rs.Length == 0) continue;
                        var bounds = rs[0].bounds; foreach (var r in rs) bounds.Encapsulate(r.bounds);
                        var holder = new GameObject("Temple308_" + group.name.Replace(' ', '_'));
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, demo);
                        holder.transform.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                        var copy = UnityEngine.Object.Instantiate(group, group.transform.position, group.transform.rotation); copy.name = group.name; copy.transform.localScale = group.transform.lossyScale;
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy, demo); copy.transform.SetParent(holder.transform, true); copy.SetActive(true);
                        holder.transform.position = Vector3.zero;
                        string path = KitDir + "/Prefabs/" + holder.name + ".prefab";
                        PrefabUtility.SaveAsPrefabAsset(holder, path); UnityEngine.Object.DestroyImmediate(holder); made++;
                        int verts = rs.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null).Sum(f => f.sharedMesh.vertexCount);
                        sb.Append("  ").Append(Path.GetFileNameWithoutExtension(path)).Append(" size ").Append(bounds.size.x.ToString("F1")).Append(" x ").Append(bounds.size.y.ToString("F1")).Append(" x ").Append(bounds.size.z.ToString("F1"))
                          .Append(" m, renderers ").Append(rs.Length).Append(", vertices ").Append(verts).Append('\n');
                    }
                }
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(demo, true); UnityEngine.SceneManagement.SceneManager.SetActiveScene(active); }
            return sb.Append("  prefabs ").Append(made).ToString();
        }

        // sheet - one picture per kit prefab, drawn in a preview scene of its own (nothing is left in the open scene)
        internal static string Sheet(string prefix = "Temple308_")
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Art", "Pitch308", "shots", "temple")); Directory.CreateDirectory(outDir);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene(); int made = 0;
            var rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32); var tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
            try
            {
                var lightGo = new GameObject("L"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
                var key = lightGo.AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.6f; lightGo.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
                var fillGo = new GameObject("F"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fillGo, scene);
                var fill = fillGo.AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = .9f; fill.color = new Color(.85f, .9f, 1f);
                var camGo = new GameObject("C"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.86f, .85f, .80f); cam.fieldOfView = 32f; cam.targetTexture = rt; cam.enabled = false;
                cam.nearClipPlane = .3f; cam.farClipPlane = 600f;
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { KitDir + "/Prefabs", "Assets/_Project/Art/World/SteamKit308", "Assets/StonePagoda/Prefabs" }.Where(AssetDatabase.IsValidFolder).ToArray()))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid); var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!prefab.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var rs = go.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) { UnityEngine.Object.DestroyImmediate(go); continue; }
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    float radius = b.extents.magnitude; float dist = radius / Mathf.Sin(cam.fieldOfView * .5f * Mathf.Deg2Rad) * .92f;
                    // dressed buildings are drawn from the front and from the back (the machinery hangs on the back and the roof)
                    foreach (var (yaw, tag) in prefix == "Temple308S_" ? new[] { (205f, "_front"), (40f, "_back") } : new[] { (205f, "") })
                    {
                        var dir = Quaternion.Euler(16f, yaw, 0f) * Vector3.forward;
                        cam.transform.position = b.center - dir * dist; cam.transform.rotation = Quaternion.LookRotation(dir);
                        fill.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(25f, yaw + 150f, 0f) * Vector3.forward);
                        key.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(40f, yaw - 35f, 0f) * Vector3.forward);
                        cam.Render(); if (made == 0) cam.Render();   // the first frame of a new preview scene can come out empty
                        var prev = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); tex.Apply(); RenderTexture.active = prev;
                        File.WriteAllBytes(Path.Combine(outDir, prefab.name + tag + ".png"), tex.EncodeToPNG()); made++;
                    }
                    UnityEngine.Object.DestroyImmediate(go);
                }
                cam.targetTexture = null;
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); }
            return "sheet: " + made + " pictures -> " + outDir;
        }

        // sizes:<folder> - every prefab under the folder: bounds and vertex count (to pick and place pieces)
        static string Sizes(string folder)
        {
            var sb = new StringBuilder("sizes " + folder).Append((char)10);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); var rs = prefab.GetComponentsInChildren<MeshRenderer>(true); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                int verts = rs.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null).Sum(f => f.sharedMesh.vertexCount);
                sb.Append("  ").Append(prefab.name).Append(' ').Append(b.size.x.ToString("F1")).Append('x').Append(b.size.y.ToString("F1")).Append('x').Append(b.size.z.ToString("F1")).Append(" min y ").Append(b.min.y.ToString("F2")).Append(" verts ").Append(verts).Append((char)10);
            }
            return sb.ToString().TrimEnd();
        }

        // lantern - the Meshy stone lantern (Tools/MeshyRuns/Temple308/run_lantern.py, image-to-3D from the three-view concept):
        // one URP material from its maps (the emission map is NOT used: stone does not glow), scaled to 2.05 m, as Temple308S_Lantern.
        static string Lantern()
        {
            const string dir = "Assets/_Project/Art/World/Temple308/Lantern";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/Lantern308.fbx"); if (model == null) return "REFUSED no " + dir + "/Lantern308.fbx";
            var normal = (TextureImporter)AssetImporter.GetAtPath(dir + "/Lantern308_N.png");
            if (normal != null && normal.textureType != TextureImporterType.NormalMap) { normal.textureType = TextureImporterType.NormalMap; normal.SaveAndReimport(); }
            var metal = (TextureImporter)AssetImporter.GetAtPath(dir + "/Lantern308_M.png");
            if (metal != null && metal.sRGBTexture) { metal.sRGBTexture = false; metal.SaveAndReimport(); }
            string matPath = dir + "/Lantern308.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Lantern308" }; AssetDatabase.CreateAsset(m, matPath); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/Lantern308_BC.png")); m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/Lantern308_N.png")); m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/Lantern308_M.png")); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", .2f);
            m.DisableKeyword("_EMISSION"); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var holder = new GameObject("Temple308S_Lantern"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model, holder.transform);
                foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = r.sharedMaterials.Select(_ => m).ToArray();
                var rs = body.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float k = 2.05f / Mathf.Max(.001f, b.size.y); body.transform.localScale *= k;
                b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                body.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
                b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                // the collider is the pillar and the stones around it, inside the drawn lantern
                var box = holder.AddComponent<BoxCollider>(); box.center = new Vector3(0f, b.size.y / 2f, 0f); box.size = new Vector3(b.size.x * .55f, b.size.y, b.size.z * .55f);
                PrefabUtility.SaveAsPrefabAsset(holder, KitDir + "/Prefabs/Temple308S_Lantern.prefab");
                int verts = rs.Select(r => r.GetComponent<MeshFilter>()).Where(f => f != null && f.sharedMesh != null).Sum(f => f.sharedMesh.vertexCount);
                return "lantern: Temple308S_Lantern " + b.size.x.ToString("F2") + " x " + b.size.y.ToString("F2") + " x " + b.size.z.ToString("F2") + " m, vertices " + verts + ", scale " + k.ToString("F3");
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }

        static string Status()
        {
            var sb = new StringBuilder("TempleKit308 | compiling " + EditorApplication.isCompiling + " updating " + EditorApplication.isUpdating + "\n");
            foreach (string folder in AssetDatabase.GetSubFolders("Assets").Where(f => Folders.Contains(f) || f.IndexOf("Temple", StringComparison.OrdinalIgnoreCase) >= 0 || f.IndexOf("Pagoda", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                sb.Append("  ").Append(folder).Append(": ");
                foreach (string type in new[] { "Prefab", "Model", "Texture2D", "Material", "Scene" })
                    sb.Append(type).Append(' ').Append(AssetDatabase.FindAssets("t:" + type, new[] { folder }).Length).Append("  ");
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }
    }
}
