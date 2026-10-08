using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 cliff way (벼랑 잔도, D308-38, SPEC-WORLD-CLIFFWAY-308). Every value [TEST].
    // The geometry is made offline by Tools/Art/cliffway308.py (Art/World/Compact/Rebuild/CliffWay308/Meshes/*.json + layout.json,
    // numbers in Tools/Unity/Stage308_cliffway/cliffway308.json). This tool owns ONE root (CliffWay308) in the play scene and ONE
    // asset folder (Assets/_Project/Art/World/CliffWay308) and nothing else: no terrain op, no water, no protected tree, no light,
    // no emission, no text, no content row. The root stays in the play scene (a landmark seen across the lake; it is not streamed).
    //   CliffWay308/Shell            LODGroup: LOD0 | LOD1 (granite, the material the other cliffs use)
    //   CliffWay308/Steps, /Rail     cut treads; iron posts, chains, red cloth, locks, warning posts at the rail gaps
    //   CliffWay308/Col_*            MeshColliders only (never convex, never a trigger): the tread ramp, the shell's own LOD1 surface,
    //                                one thin slab per chain bay. No box, no invisible wall away from drawn geometry.
    // Queue-safe: Run(string) never opens a dialog; refusals come back as "REFUSED ...". Only the play scene and this tool's own
    // assets are saved (never AssetDatabase.SaveAssets).
    //   status | plan | build | clear | check
    public static class CliffWay308
    {
        const string Usage = "status | plan | build | clear | check";
        static readonly Color RockTint = new Color(1.12f, 1.12f, 1.05f), CapTint = new Color(1.32f, 1.30f, 1.20f);   // [TEST] against stills
        const string RootName = "CliffWay308", SceneName = "W_Demo_Main", AssetDir = "Assets/_Project/Art/World/CliffWay308";
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string DataDir => Path.Combine(RepoRoot, "Art", "World", "Compact", "Rebuild", "CliffWay308");
        static string BackupDir => Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_cliffway", "Backup");

        [Serializable] sealed class Sub { public string material = ""; public int[] triangles = Array.Empty<int>(); }
        [Serializable] sealed class MeshData { public string name = ""; public float[] vertices = Array.Empty<float>(), normals = Array.Empty<float>(), uvs = Array.Empty<float>(); public Sub[] submeshes = Array.Empty<Sub>(); }
        [Serializable] sealed class FaceSlope { public float below, above; }
        [Serializable] sealed class Layout
        {
            public string id = ""; public float length, width, heightMin, heightMax, aboveWaterMax, gradeMaxDeg, riserMax, drop3ShareOver30, drop3Median;
            public float wallAboveMax, wallAboveMedian, topAboveWaterMax, fillMax, skinUnderGroundShare; public int posts, treads;
            public float[] start = Array.Empty<float>(), end = Array.Empty<float>(); public FaceSlope faceSlopeDeg = new FaceSlope();
        }

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return Status();
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed) return "REFUSED scripts are compiling or failed to compile";
                if (c == "plan") return Plan();
                if (c == "build") return Build();
                if (c == "clear") return Clear();
                if (c == "check") return Check();
                return "REFUSED " + Usage;
            }
            catch (Exception e) { return "REFUSED " + e.GetType().Name + ": " + e.Message; }
        }

        static Scene PlayScene(bool clean)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != SceneName) throw new Exception("open " + SceneName + " first (active scene is " + scene.name + ")");
            if (clean && scene.isDirty) throw new Exception("the play scene has unsaved changes");
            return scene;
        }

        static Layout ReadLayout() => JsonUtility.FromJson<Layout>(File.ReadAllText(Path.Combine(DataDir, "layout.json")));
        static string[] MeshFiles() => Directory.Exists(Path.Combine(DataDir, "Meshes")) ? Directory.GetFiles(Path.Combine(DataDir, "Meshes"), "*.json").OrderBy(x => x).ToArray() : Array.Empty<string>();
        static string F(float v, string format = "F1") => v.ToString(format, CultureInfo.InvariantCulture);

        static string Status()
        {
            var scene = SceneManager.GetActiveScene();
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            return "CliffWay308 | scene " + scene.name + " | root " + (root == null ? "absent" : "present, " + root.GetComponentsInChildren<MeshRenderer>(true).Length + " renderers, " + root.GetComponentsInChildren<MeshCollider>(true).Length + " colliders")
                   + " | data meshes " + MeshFiles().Length + " | assets " + (AssetDatabase.IsValidFolder(AssetDir) ? AssetDatabase.FindAssets("t:Mesh", new[] { AssetDir }).Length + " meshes" : "no folder");
        }

        static string Plan()
        {
            var l = ReadLayout(); var sb = new StringBuilder("CliffWay308 plan (" + l.id + ")\n");
            sb.Append("  path ").Append(F(l.length)).Append(" m, width ").Append(F(l.width)).Append(" m, height ").Append(F(l.heightMin)).Append(" - ").Append(F(l.heightMax))
              .Append(" m, above the water up to ").Append(F(l.aboveWaterMax)).Append(" m, steepest ").Append(F(l.gradeMaxDeg)).Append(" deg, riser up to ").Append(F(l.riserMax, "F3")).Append(" m\n");
            sb.Append("  wall above the path up to ").Append(F(l.wallAboveMax)).Append(" m (median ").Append(F(l.wallAboveMedian)).Append("), top above the water up to ").Append(F(l.topAboveWaterMax))
              .Append(" m, face ").Append(F(l.faceSlopeDeg.below)).Append(" / ").Append(F(l.faceSlopeDeg.above)).Append(" deg\n");
            sb.Append("  posts ").Append(l.posts).Append(", treads ").Append(l.treads).Append('\n');
            foreach (string file in MeshFiles())
            {
                var d = JsonUtility.FromJson<MeshData>(File.ReadAllText(file));
                sb.Append("  ").Append(d.name).Append(": vertices ").Append(d.vertices.Length / 3).Append(", triangles ").Append(d.submeshes.Sum(s => s.triangles.Length) / 3)
                  .Append(", materials ").Append(string.Join(",", d.submeshes.Select(s => s.material))).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        static Material Slot(string slot, Dictionary<string, Material> cache)
        {
            if (cache.TryGetValue(slot, out var known)) return known;
            Material m = null;
            if (slot == "cliff_rock" || slot == "cliff_cap")
            {
                // this tool's own copy of the granite the other cliffs use (the source is read, never changed), paler: the lake slopes around it are pale
                string path = AssetDir + "/Materials/" + slot + ".mat"; m = AssetDatabase.LoadAssetAtPath<Material>(path);
                var source = AssetDatabase.FindAssets("Cheongrim_Granite t:Material").Select(AssetDatabase.GUIDToAssetPath).Where(q => q.Contains("Architecture296/Materials")).Select(AssetDatabase.LoadAssetAtPath<Material>).FirstOrDefault(x => x != null);
                if (m == null && source != null) { m = new Material(source) { name = slot }; Folder(AssetDir + "/Materials"); AssetDatabase.CreateAsset(m, path); }
                if (m != null) { m.SetColor("_BaseColor", slot == "cliff_cap" ? CapTint : RockTint); EditorUtility.SetDirty(m); }
            }
            if (m == null) m = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/Finish297/Materials/" + slot + ".mat");
            if (m == null)
            {
                string path = AssetDir + "/Materials/" + slot + ".mat"; m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    Color color = slot == "cloth_red" ? new Color(.62f, .08f, .07f) : slot == "brass" ? new Color(.55f, .42f, .16f) : slot == "iron" ? new Color(.15f, .14f, .13f)
                        : slot == "wood_dark" ? new Color(.22f, .16f, .11f) : new Color(.5f, .5f, .48f);
                    m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = slot };
                    m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", slot == "brass" ? .45f : slot == "iron" ? .3f : .08f); m.SetFloat("_Metallic", slot == "brass" || slot == "iron" ? .7f : 0f);
                    if (slot == "cloth_red") { m.SetFloat("_Cull", (float)CullMode.Off); m.doubleSidedGI = true; }
                    Folder(AssetDir + "/Materials"); AssetDatabase.CreateAsset(m, path);
                }
            }
            cache[slot] = m; return m;
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
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
            mesh.SetVertices(v); mesh.SetNormals(nn); mesh.SetUVs(0, uv);
            mesh.subMeshCount = d.submeshes.Length;
            for (int s = 0; s < d.submeshes.Length; s++) mesh.SetTriangles(d.submeshes[s].triangles, s, false);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            slots = d.submeshes.Select(s => s.material).ToArray();
            return mesh;
        }

        static string Build()
        {
            var scene = PlayScene(true);
            var files = MeshFiles(); if (files.Length == 0) throw new Exception("no mesh json under " + DataDir + " (run Tools/Art/cliffway308.py)");
            // backup of the scene file before it is written
            string stamp = DateTime.Now.ToString("yyyyMMddTHHmmss"); string backup = Path.Combine(BackupDir, stamp + "_build"); Directory.CreateDirectory(backup);
            File.Copy(Path.GetFullPath(scene.path), Path.Combine(backup, Path.GetFileName(scene.path)), true);

            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName); if (old != null) Object.DestroyImmediate(old);
            Folder(AssetDir + "/Meshes");
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { AssetDir + "/Meshes" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));

            var cache = new Dictionary<string, Material>(); var meshes = new Dictionary<string, (Mesh mesh, string[] slots)>();
            foreach (string file in files)
            {
                var d = JsonUtility.FromJson<MeshData>(File.ReadAllText(file));
                var mesh = MakeMesh(d, out var slots);
                AssetDatabase.CreateAsset(mesh, AssetDir + "/Meshes/" + d.name + ".asset"); meshes[d.name] = (mesh, slots);
            }
            var root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene);
            GameObject Child(string name, Transform parent) { var g = new GameObject(name); g.transform.SetParent(parent, false); g.isStatic = true; return g; }
            MeshRenderer Draw(string name, string meshName, Transform parent, bool shadows)
            {
                var (mesh, slots) = meshes[meshName]; var g = Child(name, parent);
                g.AddComponent<MeshFilter>().sharedMesh = mesh; var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterials = slots.Select(s => Slot(s, cache)).ToArray();
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true; r.allowOcclusionWhenDynamic = false;
                return r;
            }
            var shell = Child("Shell", root.transform); var group = shell.AddComponent<LODGroup>();
            var l0 = Draw("LOD0", "CliffWay_Shell_LOD0", shell.transform, true); var l1 = Draw("LOD1", "CliffWay_Shell_LOD1", shell.transform, false);
            group.SetLODs(new[] { new LOD(.12f, new Renderer[] { l0 }), new LOD(0f, new Renderer[] { l1 }) }); group.RecalculateBounds();
            var steps = Draw("Steps", "CliffWay_Steps", root.transform, false); var rail = Draw("Rail", "CliffWay_Rail", root.transform, false);
            foreach (var (name, meshName) in new[] { ("Col_Tread", "CliffWay_Tread_Collision"), ("Col_Shell", "CliffWay_Shell_Collision"), ("Col_Rail", "CliffWay_Rail_Collision") })
            {
                var g = Child(name, root.transform); var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = meshes[meshName].mesh; mc.convex = false; mc.isTrigger = false;
            }
            foreach (var m in cache.Values) if (AssetDatabase.GetAssetPath(m).StartsWith(AssetDir)) AssetDatabase.SaveAssetIfDirty(m);
            foreach (var m in meshes.Values) AssetDatabase.SaveAssetIfDirty(m.mesh);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            long bytes = meshes.Values.Sum(m => Profiler.GetRuntimeMemorySizeLong(m.mesh));
            return "CliffWay308 build: " + meshes.Count + " meshes (" + F(bytes / 1048576f) + " MB), root at " + scene.name + ", backup " + backup + "\n" + Check();
        }

        static string Clear()
        {
            var scene = PlayScene(true);
            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (old != null) { Object.DestroyImmediate(old); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            bool folder = AssetDatabase.IsValidFolder(AssetDir); if (folder) AssetDatabase.DeleteAsset(AssetDir);
            return "CliffWay308 clear: root " + (old != null ? "removed" : "absent") + ", assets " + (folder ? "removed" : "absent");
        }

        static string Check()
        {
            var scene = PlayScene(false); var l = ReadLayout(); var sb = new StringBuilder("CliffWay308 check\n"); int pass = 0, fail = 0;
            void Line(bool ok, string id, string text) { sb.Append(ok ? "  PASS " : "  FAIL ").Append(id).Append(' ').Append(text).Append('\n'); if (ok) pass++; else fail++; }
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            Line(l.width >= 1.4f && l.gradeMaxDeg <= 35f && l.riserMax <= .171f, "AC-C1", "width " + F(l.width) + " m, steepest " + F(l.gradeMaxDeg) + " deg, riser " + F(l.riserMax, "F3") + " m");
            Line(l.drop3ShareOver30 >= .7f, "AC-C2", "drop 3 m outside the path is 30 m or more on " + F(l.drop3ShareOver30 * 100f, "F0") + " % of the length (median " + F(l.drop3Median) + " m)");
            Line(l.wallAboveMax >= 35f && l.topAboveWaterMax >= 80f && Mathf.Min(l.faceSlopeDeg.below, l.faceSlopeDeg.above) >= 78f, "AC-C3",
                "wall above the path " + F(l.wallAboveMax) + " m, top above the water " + F(l.topAboveWaterMax) + " m, face " + F(l.faceSlopeDeg.below) + " / " + F(l.faceSlopeDeg.above) + " deg");
            if (root == null) { Line(false, "AC-C5", "no " + RootName + " root in " + scene.name); return sb.Append("  checks, ").Append(pass).Append(" pass ").Append(fail).Append(" fail").ToString(); }
            var colliders = root.GetComponentsInChildren<Collider>(true);
            bool onlyMesh = colliders.All(c => c is MeshCollider mc && !mc.convex && !mc.isTrigger && mc.sharedMesh != null);
            bool own = root.GetComponentsInChildren<MeshFilter>(true).All(f => f.sharedMesh != null && AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(AssetDir));
            Line(onlyMesh && own && colliders.Length == 3, "AC-C5", colliders.Length + " colliders, all MeshCollider of this tool's meshes: " + (onlyMesh && own));
            bool scripts = root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 && root.GetComponentsInChildren<Light>(true).Length == 0;
            Line(scripts, "AC-C5b", "no script and no light under the root");
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            Line(renderers.All(r => r.sharedMaterials.All(m => m != null)), "AC-C5c", renderers.Length + " renderers, every material slot filled");
            long bytes = root.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).Concat(colliders.OfType<MeshCollider>().Select(c => c.sharedMesh)).Distinct().Sum(m => Profiler.GetRuntimeMemorySizeLong(m));
            Line(bytes <= 60L * 1048576, "AC-C9", "mesh memory " + F(bytes / 1048576f) + " MB (budget 60)");
            return sb.Append("  checks, ").Append(pass).Append(" pass ").Append(fail).Append(" fail").ToString();
        }
    }
}
