using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Replaces the entire old C2 rock population with Blender-refined source geometry.</summary>
    public static class CodexRockKit
    {
        public const string Folder = CodexWorldSceneBuilder.AssetFolder + "/RockKit";
        public const string RootName = "07_Refined_Rock_Formations";
        private static readonly string[] Letters = { "A", "C", "D", "F", "I", "K", "L", "N" };
        private sealed class Variant { public Mesh[] Lod = new Mesh[3]; public Mesh Collision; }

        [MenuItem("Oheangbu/Dev/Codex World/Replace all rocks with refined kit")]
        private static void ReplaceMenu() => Debug.Log(ReplaceAll());

        public static string ReplaceAll()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != CodexWorldSceneBuilder.ScenePath || scene.isDirty)
                return "FAIL: open and save C2 in Edit mode first";
            var data = AssetDatabase.LoadAssetAtPath<CodexWorldSettingsSO>(CodexWorldSceneBuilder.SettingsPath);
            if (data == null) return "FAIL: missing settings";
            var variants = LoadVariants(); // Validate all source meshes before removing anything.
            var material = CreateMaterial(data, "Rock_Refined", false);
            var mineMaterial = CreateMaterial(data, "Rock_Refined_Mine", true);
            var previous = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name == RootName);
            var old = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true))
                .Where(f => f.sharedMesh != null && AssetDatabase.GetAssetPath(f.sharedMesh)
                    .StartsWith(CodexWorldSceneBuilder.MeshFolder + "/Granite_", StringComparison.Ordinal)).ToArray();
            // Construct the full replacement separately so a failed build preserves the old population.
            var root = new GameObject(RootName + "_Building");
            var random = new System.Random(data.Seed + 83017);
            int placed = 0;
            float R(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
            GameObject Put(int variant, string name, Vector3 p, float diameter, float yaw, bool collision, bool mine = false)
            {
                var g = new GameObject(name);
                g.transform.SetParent(root.transform, false);
                g.transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
                g.transform.localScale = Vector3.one * diameter;
                g.isStatic = true;
                var renderers = new Renderer[3];
                var v = variants[variant];
                for (int l = 0; l < 3; l++)
                {
                    var child = new GameObject("LOD" + l);
                    child.transform.SetParent(g.transform, false);
                    child.isStatic = true;
                    child.AddComponent<MeshFilter>().sharedMesh = v.Lod[l];
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = mine ? mineMaterial : material;
                    renderers[l] = renderer;
                }
                var group = g.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(data.RockLodNear, new[] { renderers[0] }),
                    new LOD(data.RockLodMiddle, new[] { renderers[1] }), new LOD(.003f, new[] { renderers[2] }) });
                group.RecalculateBounds();
                if (collision) g.AddComponent<MeshCollider>().sharedMesh = v.Collision;
                placed++;
                return g;
            }
            void GroundRock(int variant, string name, float x, float z, float diameter, float yaw, bool collision)
            {
                // Embed the base into the analytic floor; a broad footprint remains grounded on crossfall.
                float y = CodexWorldGeometry.Height(x, z) - diameter * .11f;
                Put(variant, name, new Vector3(x, y, z), diameter, yaw, collision);
            }
            bool Clear(float x, float z, float diameter) => Mathf.Abs(x - CodexWorldGeometry.PathX(z)) > data.RockPathClearance + diameter * .55f
                && Vector2.Distance(new Vector2(x, z), new Vector2(data.InnAnchor.x, data.InnAnchor.z)) > data.RockInnClearance + diameter * .5f;
            try
            {
                // Uneven, low formations on both banks. Each cluster has a dominant shoulder and talus.
                for (int i = 0; i < data.RockClusterCount; i++)
                {
                    int side = (i & 1) == 0 ? -1 : 1;
                    float z = 17 + i / 2 * 25 + R(-6, 6);
                    float diameter = R(data.RockClusterDiameter.x, data.RockClusterDiameter.y);
                    float x = CodexWorldGeometry.PathX(z) + side * R(24, 37);
                    if (!Clear(x, z, diameter)) continue;
                    float yaw = side * R(12, 40);
                    GroundRock(i % 3, "Bedrock_Shoulder_" + i, x, z, diameter, yaw, true);
                    GroundRock(5, "Jointed_Block_" + i, x + side * diameter * .3f, z + diameter * .18f,
                        diameter * .68f, yaw + R(-35, 35), true);
                    for (int j = 0; j < 3; j++)
                    {
                        float size = diameter * R(.12f, .30f);
                        float xx = x - side * diameter * R(.2f, .7f), zz = z + R(-1, 1) * diameter * .48f;
                        if (Clear(xx, zz, size)) GroundRock(3 + j % 3, "Talus_" + i + "_" + j, xx, zz, size, R(0, 360), size > 1.2f);
                    }
                }
                // Local fragments collect in pockets; the path keeps broad quiet gaps.
                for (int i = 0; i < data.RockScatterClusters; i++)
                {
                    float z = R(8, 212), side = i % 2 == 0 ? -1 : 1;
                    float x = CodexWorldGeometry.PathX(z) + side * R(7, 19);
                    for (int j = 0; j < 3; j++)
                    {
                        float size = j == 0 ? R(.8f, 2.3f) : R(.20f, .65f);
                        float xx = x + R(-1.5f, 1.5f), zz = z + R(-1.5f, 1.5f);
                        if (Clear(xx, zz, size)) GroundRock((i+j) % Letters.Length, "Fragment_Pocket_" + i + "_" + j, xx, zz, size, R(0, 360), size > 1.2f);
                    }
                }
                GroundRock(5, "Trail_Anchor_Jointed", 12f, 19f, 4.8f, -22f, true);
                GroundRock(1, "Trail_Anchor_Weathered", -12f, 13f, 3.6f, 65f, true);
                GroundRock(6, "Trail_Anchor_Fragment", 9.4f, 17f, 1.1f, 12f, false);
                // Broad geological blocks frame the mine without tall squeezed needles.
                for (int side = -1; side <= 1; side += 2)
                {
                    Put(5, "Mine_Portal_Base_" + side, new Vector3(side*5.6f, -.65f, 1.5f), 6.3f, side*14, true, true);
                    Put(1, "Mine_Portal_Shoulder_" + side, new Vector3(side*5.7f, 1.8f, 2.5f), 6.5f, side*26, true, true);
                    Put(0, "Mine_Portal_Outer_" + side, new Vector3(side*10.5f, -.8f, 6), 11, side*25, true);
                    Put(5, "Mine_Portal_Upper_" + side, new Vector3(side*5.2f, 3.8f, 4.5f), 7, side*12, true, true);
                }
                Put(0, "Mine_Natural_Lintel", new Vector3(0, 5.6f, 3.8f), 13, 3, true, true);
                Put(2, "Mine_Overburden", new Vector3(0, 8.7f, 7), 17, 10, true, true);
                for (int i = 0; i < 22; i++)
                {
                    float side = i % 2 == 0 ? -1 : 1;
                    Put(6, "Mine_Detritus_" + i, new Vector3(side * R(2.55f, 3.0f), -.035f, R(-31, -1)), R(.22f, .65f), R(0, 360), false, true);
                }
                for (int i = 0; i < 9; i++)
                {
                    Vector3 p = Vector3.Lerp(new Vector3(CodexWorldGeometry.PathX(29), 0, 29), data.InnAnchor + new Vector3(0, 0, -5), i/8f);
                    p.y = SiteHeight(p.x, p.z, data) - .29f;
                    Put(7, "Inn_Flat_Approach_" + i, p, 1.25f, R(-22, 22), false);
                }
                foreach (var f in old) Object.DestroyImmediate(f.gameObject);
                if (previous != null) Object.DestroyImmediate(previous.gameObject);
                root.name = RootName;
                EditorUtility.SetDirty(data);
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                bool saved = EditorSceneManager.SaveScene(scene);
                string result = $"{(saved ? "OK" : "FAIL")}: removed {old.Length} legacy rock instances; created {placed} refined rock instances; variants=8; LODs=3; saved={saved}";
                Directory.CreateDirectory(CodexWorldAudit.CaptureFolder);
                File.WriteAllText(CodexWorldAudit.CaptureFolder + "/rock-replacement.txt", result);
                return result;
            }
            catch { if (root != null) Object.DestroyImmediate(root); throw; }
        }

        private static float SiteHeight(float x, float z, CodexWorldSettingsSO data)
        {
            var local = Quaternion.Euler(0, -data.InnYaw, 0) * (new Vector3(x,0,z)-data.InnAnchor);
            float edge = Mathf.Max(Mathf.Abs(local.x)-6.4f, Mathf.Abs(local.z)-4.7f);
            float blend = 1-Mathf.SmoothStep(0,1,Mathf.Clamp01(edge/2.5f));
            return Mathf.Lerp(CodexWorldGeometry.Height(x,z), CodexWorldGeometry.Height(data.InnAnchor.x,data.InnAnchor.z), blend);
        }

        private static Variant[] LoadVariants()
        {
            var variants = new Variant[Letters.Length];
            DevSceneKit.EnsureFolder(Folder + "/Meshes");
            for (int i = 0; i < Letters.Length; i++)
            {
                string file = Folder + "/InkRock_" + Letters[i] + ".fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(file);
                if (model == null) throw new InvalidOperationException("Missing imported kit model: " + file);
                var filters = model.GetComponentsInChildren<MeshFilter>(true);
                var v = new Variant();
                for (int l = 0; l < 4; l++)
                {
                    string suffix = l < 3 ? "_LOD" + l : "_COL";
                    var f = filters.Single(t => t.name.Contains(suffix));
                    var copy = new Mesh();
                    copy.CombineMeshes(new[] { new CombineInstance { mesh=f.sharedMesh, transform=f.transform.localToWorldMatrix } }, true, true);
                    copy.RecalculateBounds();
                    var b = copy.bounds;
                    if (b.size.y > .9f || b.size.y < .1f || Mathf.Max(b.size.x,b.size.z) < .8f || Mathf.Max(b.size.x,b.size.z) > 1.2f)
                        throw new InvalidOperationException("Rock axis/scale mismatch " + f.name + ": " + b.size);
                    string path = Folder + "/Meshes/InkRock_" + Letters[i] + suffix + ".asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (existing != null)
                    {
                        existing.Clear(); existing.indexFormat=copy.indexFormat; existing.vertices=copy.vertices;
                        existing.normals=copy.normals; existing.uv=copy.uv; existing.triangles=copy.triangles;
                        existing.bounds=copy.bounds; existing.UploadMeshData(false); Object.DestroyImmediate(copy);
                        copy=existing; EditorUtility.SetDirty(copy);
                    }
                    else { copy.name="InkRock_"+Letters[i]+suffix; AssetDatabase.CreateAsset(copy,path); }
                    if (l<3) v.Lod[l]=copy; else v.Collision=copy;
                }
                variants[i]=v;
            }
            return variants;
        }

        private static Material CreateMaterial(CodexWorldSettingsSO data, string name, bool mine)
        {
            string path=CodexWorldSceneBuilder.AssetFolder + "/Materials/" + name + ".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material=new Material(Shader.Find("Oheangbu/CodexInkLandscape")) { name=name };
                AssetDatabase.CreateAsset(material,path);
            }
            material.SetFloat("_InkDensity",mine?1.35f:1.05f);
            material.SetFloat("_AmbientLevel",mine?.14f:data.RockAmbient);
            material.SetFloat("_LightResponse",data.RockDiffuse);
            material.SetFloat("_ToneFloor",.015f);
            material.SetFloat("_ToneCeiling",data.RockSurfaceTone);
            material.SetFloat("_NoiseScale",.9f);
            material.SetFloat("_NoiseStrength",.035f);
            material.SetFloat("_BrushStrength",0);
            material.SetFloat("_StrokeStrength",0);
            material.SetFloat("_GrainStrength",.008f);
            material.SetFloat("_RimStrength",.018f);
            material.SetFloat("_UndersideInk",.16f);
            material.SetFloat("_WashStart",65);
            material.SetFloat("_WashEnd",700);
            material.SetFloat("_WashStrength",.48f);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static string Validate()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != CodexWorldSceneBuilder.ScenePath) return "FAIL: open C2";
            var filters = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true)).ToArray();
            int legacy = filters.Count(f => f.sharedMesh != null && AssetDatabase.GetAssetPath(f.sharedMesh)
                .StartsWith(CodexWorldSceneBuilder.MeshFolder + "/Granite_", StringComparison.Ordinal));
            var root = scene.GetRootGameObjects().FirstOrDefault(r => r.name == RootName);
            if (root == null) return "FAIL: missing refined rock root";
            int errors = 0;
            long lod0Tris = 0, collisionTris = 0;
            var unique = new HashSet<Mesh>();
            var groups = root.GetComponentsInChildren<LODGroup>(true);
            foreach (var group in groups)
            {
                var lods=group.GetLODs();
                if (lods.Length!=3) { errors++; continue; }
                if (group.transform.localScale.x != group.transform.localScale.y || group.transform.localScale.y != group.transform.localScale.z) errors++;
                long previous = long.MaxValue;
                for (int l=0; l<3; l++)
                {
                    if (lods[l].renderers.Length!=1 || lods[l].renderers[0]==null) { errors++; continue; }
                    var renderer=lods[l].renderers[0];
                    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                    if (mesh==null) { errors++; continue; }
                    long tris=(long)mesh.GetIndexCount(0)/3;
                    if (tris>=previous) errors++;
                    previous=tris;
                    if (l==0) { lod0Tris+=tris; unique.Add(mesh); }
                    var mat=renderer.sharedMaterial;
                    if (mat==null || !mat.shader.isSupported || ShaderUtil.ShaderHasError(mat.shader)
                        || mat.GetFloat("_BrushStrength")!=0 || mat.GetFloat("_StrokeStrength")!=0) errors++;
                }
            }
            foreach (var collider in root.GetComponentsInChildren<MeshCollider>())
            {
                if (collider.sharedMesh==null) { errors++; continue; }
                long tris=(long)collider.sharedMesh.GetIndexCount(0)/3;
                collisionTris+=tris;
                if (tris>170) errors++;
            }
            foreach (var mesh in unique)
            {
                if (mesh.normals.Length != mesh.vertexCount) errors++;
                foreach (var v in mesh.vertices) if (!float.IsFinite(v.sqrMagnitude)) errors++;
                foreach (var n in mesh.normals) if (!float.IsFinite(n.sqrMagnitude) || n.sqrMagnitude<.9f) errors++;
            }
            string result=$"{(legacy==0 && errors==0 && unique.Count==8 ? "PASS" : "FAIL")}: legacyGraniteInstances={legacy}; "
                + $"newRocks={groups.Length}; variants={unique.Count}; LODs=3; invalid={errors}; "
                + $"colliders={root.GetComponentsInChildren<MeshCollider>().Length}; collisionTris={collisionTris}; "
                + $"allInstancesAtLOD0Tris={lod0Tris} (not simultaneous visible draw cost)";
            File.WriteAllText(CodexWorldAudit.CaptureFolder+"/rock-validation.txt",result);
            return result;
        }
    }
}
