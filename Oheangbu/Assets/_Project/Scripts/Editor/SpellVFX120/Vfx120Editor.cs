using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using UnityEngine.Rendering.Universal;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120Editor
    {
        public const string AssetRoot = "Assets/_Project/Art/SpellVFX120";
        public static string Repo => Directory.GetParent(Application.dataPath).Parent.FullName;
        public static string Output => Path.Combine(Repo, "Art/SpellVFX120");
        [Serializable] public class Definition
        {
            public string glyph, title, intent, family, accentFamily, behavior, layout, pattern, recipe;
            public bool assigned, connected, grounded, mist;
            public int count, ribbons;
            public float size, duration, flight, turns, lift, stagger;
            public Color pigment, accent;
            public Vector3 partScale;
        }
        [Serializable] public class Manifest { public Definition[] spells; }
        [Serializable] public class Audit
        {
            public string status; public int count, assigned, connected, nullReferences, shaderErrors, invalidMeshes;
            public string[] glyphs; public string unityVersion;
        }

        [MenuItem("Oheangbu/VFX120/Build authored catalog")]
        public static string Build()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(Output, "build_manifest.json")));
            if (manifest.spells.Length != 120 || manifest.spells.Select(x => x.glyph).Distinct().Count() != 120)
                throw new InvalidOperationException("Expected 120 unique authored definitions");
            foreach (string folder in new[] { "Meshes", "Materials", "Profiles", "Prefabs", "Scenes", "Data" }) EnsureFolder(AssetRoot + "/" + folder);
            var shader = Shader.Find("Oheangbu/VFX120/InkPigment");
            if (shader == null) throw new InvalidOperationException("VFX120 shader is not imported");
            var meshCache = new Dictionary<string, Mesh>();
            foreach (var family in manifest.spells.SelectMany(x => new[] { x.family, x.accentFamily }).Distinct())
            {
                string path = AssetRoot + "/Meshes/" + family + ".asset";
                var generated = ExternalMesh(family) ?? Vfx120MeshFactory.Build(family);
                ValidateMesh(generated);
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) { AssetDatabase.CreateAsset(generated, path); existing = generated; }
                else { EditorUtility.CopySerialized(generated, existing); UnityEngine.Object.DestroyImmediate(generated); }
                meshCache[family] = existing;
            }
            var body = MaterialAt("M_Pigment", shader, null, false);
            var ink = MaterialAt("M_Ink", shader, null, false);
            var mist = MaterialAt("M_PigmentDust", shader, null, true);
            var catalog = LoadOrCreate<Vfx120Catalog>(AssetRoot + "/Data/VFX120_Catalog.asset");
            catalog.Entries = new Vfx120Catalog.Entry[120];
            var visualSet = LoadOrCreate<SpellVisualSetSO>(AssetRoot + "/Data/SpellVisualSet_120.asset");
            var serialized = new SerializedObject(visualSet);
            var entries = serialized.FindProperty("_entries"); entries.arraySize = 120;
            for (int i = 0; i < 120; i++)
            {
                var d = manifest.spells[i];
                string id = (i + 1).ToString("000") + "_" + ((int)d.glyph[0]).ToString("X4");
                var p = LoadOrCreate<Vfx120Profile>(AssetRoot + "/Profiles/" + id + ".asset");
                p.Glyph = d.glyph; p.Title = d.title; p.Intent = d.intent; p.Assigned = d.assigned;
                p.Family = d.family;
                p.Behavior = (Vfx120Behavior)Enum.Parse(typeof(Vfx120Behavior), d.behavior);
                p.Layout = (Vfx120Layout)Enum.Parse(typeof(Vfx120Layout), d.layout);
                p.BodyMesh = meshCache[d.family]; p.AccentMesh = meshCache[d.accentFamily];
                bool solid = d.family != "Cloud" && d.family != "Flame" && d.family != "Ember" && d.family != "Ripple" && d.family != "Ribbon" && d.family != "WaveCrest" && d.family != "Sand";
                float fluid = d.family == "WaveCrest" ? 1 : d.family == "Flame" ? 2 : d.family == "Sand" ? 3 : d.family == "Cloud" ? 4 : 0;
                p.BodyMaterial = MaterialAt("M_Body_" + d.family, shader, null, false, solid, d.family == "Shard" || d.family == "Blade" ? 1 : 0, fluid);
                p.InkMaterial = ink; p.MistMaterial = mist;
                p.Pigment = d.pigment; p.Accent = d.accent;
                p.Count = d.count; p.Size = d.size; p.Duration = d.duration; p.Flight = d.flight;
                p.Turns = d.turns; p.Lift = d.lift; p.Stagger = d.stagger;
                p.PartScale = d.partScale; p.RibbonCount = d.ribbons; p.UseMist = d.mist; p.Grounded = d.grounded;
                p.SourcePattern = d.pattern; p.RecipeJson = d.recipe;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(d.pattern);
                if (texture == null) throw new InvalidOperationException("Missing reviewed pattern: " + d.pattern);
                p.PatternMaterial = MaterialAt("M_Motif_" + texture.name, shader, texture, false);
                EditorUtility.SetDirty(p);
                var root = new GameObject("VFX120_" + d.glyph);
                root.AddComponent<Vfx120Effect>().Profile = p;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, AssetRoot + "/Prefabs/" + id + ".prefab");
                UnityEngine.Object.DestroyImmediate(root);
                catalog.Entries[i] = new Vfx120Catalog.Entry { Glyph = d.glyph, Profile = p, Prefab = prefab, GameplayConnected = d.connected };
                var e = entries.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Letter").stringValue = d.glyph;
                e.FindPropertyRelative("FxPrefab").objectReferenceValue = prefab;
                e.FindPropertyRelative("ScaleMul").floatValue = 1;
                e.FindPropertyRelative("ArcHeight").floatValue = 0;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog); EditorUtility.SetDirty(visualSet);
            AssetDatabase.SaveAssets();
            return AuditCatalog();
        }

        public static string AuditCatalog()
        {
            var c = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(AssetRoot + "/Data/VFX120_Catalog.asset");
            var a = new Audit { status = "ASSET_REFERENCES_ONLY", unityVersion = Application.unityVersion };
            a.count = c.Entries.Length; a.assigned = c.Entries.Count(e => e.Profile.Assigned); a.connected = c.Entries.Count(e => e.GameplayConnected);
            a.glyphs = c.Entries.Select(e => e.Glyph).ToArray();
            var meshes = new HashSet<Mesh>();
            foreach (var e in c.Entries)
            {
                var p = e.Profile;
                if (e.Prefab == null || p == null || p.BodyMesh == null || p.AccentMesh == null || p.PatternMaterial == null || p.PatternMaterial.mainTexture == null) a.nullReferences++;
                if (p.BodyMaterial == null || ShaderUtil.ShaderHasError(p.BodyMaterial.shader)) a.shaderErrors++;
                meshes.Add(p.BodyMesh); meshes.Add(p.AccentMesh);
            }
            foreach (var mesh in meshes) { try { ValidateMesh(mesh); } catch { a.invalidMeshes++; } }
            string json = JsonUtility.ToJson(a, true); Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "asset_validation.json"), json); return json;
        }

        [MenuItem("Oheangbu/VFX120/Open review scene")]
        public static string Review()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play before opening review");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene before VFX review");
            string path = AssetRoot + "/Scenes/SpellVFX120_Review.unity";
            if (File.Exists(Path.Combine(Application.dataPath, "../" + path))) EditorSceneManager.OpenScene(path);
            else
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("VFX120 Review Camera").AddComponent<Camera>();
                camera.tag = "MainCamera"; camera.transform.position = new Vector3(7, 4.6f, -8.5f);
                camera.transform.LookAt(new Vector3(0, .9f, 2.2f)); camera.fieldOfView = 43;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.31f,.33f,.32f);
                camera.nearClipPlane = .05f; camera.farClipPlane = 70;
                var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.renderShadows = false;
                var light = new GameObject("Soft daylight").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; light.transform.rotation = Quaternion.Euler(43,-25,0);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Neutral earthen floor"; floor.transform.localScale = new Vector3(3,1,3);
                var floorMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); floorMat.SetColor("_BaseColor", new Color(.46f,.435f,.37f));
                string matPath = AssetRoot + "/Materials/M_ReviewFloor.mat";
                var previous = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (previous == null) AssetDatabase.CreateAsset(floorMat, matPath); else { UnityEngine.Object.DestroyImmediate(floorMat); floorMat = previous; }
                floor.GetComponent<Renderer>().sharedMaterial = floorMat;
                var rig = new GameObject("VFX120 Review").AddComponent<Vfx120Review>();
                rig.Catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(AssetRoot + "/Data/VFX120_Catalog.asset");
                RenderSettings.fog = false;
                EditorSceneManager.SaveScene(scene, path);
            }
            return path;
        }

        public static string Connect()
        {
            const string path = "Assets/_Project/Prefabs/Rig/PlayerRig.prefab";
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var adapter = contents.GetComponentInChildren<BrushStrokeFeedAdapter>(true);
                var so = new SerializedObject(adapter);
                var prop = so.FindProperty("_visualSet");
                var previous = prop.objectReferenceValue;
                string backup = Path.Combine(Output, "previous_visual_set.json");
                if (!File.Exists(backup)) File.WriteAllText(backup, "{\"asset\":\"" + AssetDatabase.GetAssetPath(previous) + "\",\"guid\":\"" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(previous)) + "\"}");
                prop.objectReferenceValue = AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>(AssetRoot + "/Data/SpellVisualSet_120.asset");
                so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return "Shared PlayerRig visual catalog connected; SpellBook and game rules unchanged";
        }

        public static string Calibrate()
        {
            if (SceneManager.GetActiveScene().path != AssetRoot + "/Scenes/SpellVFX120_Review.unity") throw new InvalidOperationException("VFX review scene only");
            var camera = Camera.main;
            camera.transform.position = new Vector3(4.7f, 3.1f, -5.7f);
            camera.transform.LookAt(new Vector3(0, .8f, 2.4f)); camera.fieldOfView = 37;
            camera.allowMSAA = true;
            var floor = GameObject.Find("Neutral earthen floor"); if (floor != null) floor.transform.localScale = new Vector3(30,1,30);
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot + "/Materials/M_ReviewTarget.mat");
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", new Color(.22f,.245f,.25f)); material.SetFloat("_Smoothness", .15f);
                AssetDatabase.CreateAsset(material, AssetRoot + "/Materials/M_ReviewTarget.mat");
            }
            AddTarget("VFX Target", new Vector3(0,0,4), material);
            AddTarget("VFX Secondary Target", new Vector3(2.3f,0,5.1f), material);
            AddTarget("VFX Caster", Vector3.zero, material);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return "Review framing updated";
        }

        private static void AddTarget(string name, Vector3 point, Material material)
        {
            if (GameObject.Find(name) != null) return;
            var root = new GameObject(name); root.transform.position = point;
            var pieces = new[] { new Vector3(0,1.04f,0),new Vector3(0,1.61f,0),new Vector3(-.13f,.42f,0),new Vector3(.13f,.42f,0),new Vector3(-.31f,1.06f,0),new Vector3(.31f,1.06f,0) };
            var scales = new[] { new Vector3(.44f,.39f,.3f),new Vector3(.29f,.29f,.29f),new Vector3(.18f,.36f,.18f),new Vector3(.18f,.36f,.18f),new Vector3(.14f,.30f,.14f),new Vector3(.14f,.30f,.14f) };
            for(int i=0;i<pieces.Length;i++)
            {
                var part=GameObject.CreatePrimitive(i==1?PrimitiveType.Sphere:PrimitiveType.Capsule);
                part.transform.SetParent(root.transform,false); part.transform.localPosition=pieces[i];part.transform.localScale=scales[i];
                part.GetComponent<Renderer>().sharedMaterial=material;
                UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
            }
        }

        public static string ExportMeshes()
        {
            var folder = Path.Combine(Output, "MeshSources"); Directory.CreateDirectory(folder);
            foreach (string family in Vfx120MeshFactory.Families)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetRoot + "/Meshes/" + family + ".asset");
                if (mesh == null) continue;
                var text = new System.Text.StringBuilder("# Original VFX mesh; Unity Y-up, +Z forward\no " + family + "\n");
                foreach (var v in mesh.vertices) text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"v {0:R} {1:R} {2:R}\n",v.x,v.y,v.z);
                foreach (var v in mesh.uv) text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"vt {0:R} {1:R}\n",v.x,v.y);
                foreach (var v in mesh.normals) text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"vn {0:R} {1:R} {2:R}\n",v.x,v.y,v.z);
                var tris = mesh.triangles;
                for (int i = 0; i < tris.Length; i += 3) text.AppendFormat("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n",tris[i]+1,tris[i+1]+1,tris[i+2]+1);
                File.WriteAllText(Path.Combine(folder, family + ".obj"), text.ToString());
            }
            return folder;
        }

        public static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/'); string current = parts[0];
            for (int i = 1; i < parts.Length; i++) { string next = current + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]); current = next; }
        }

        private static void ValidateMesh(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount < 3 || mesh.triangles.Length < 3)
                throw new InvalidOperationException("Empty VFX mesh");
            foreach (var v in mesh.vertices)
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))
                    throw new InvalidOperationException("Non-finite VFX mesh: " + mesh.name);
            if (mesh.bounds.size.sqrMagnitude < .001f) throw new InvalidOperationException("Collapsed VFX mesh: " + mesh.name);
        }

        // Bake the imported FBX node transform once into a shared mesh. This is an effect,
        // not a skinned player, and retains the Blender-authored per-vertex pigment.
        private static Mesh ExternalMesh(string family)
        {
            string file = family == "Beast" ? "Beast_FolkTiger_B2.fbx" : family == "StoneGuardian" ? "StoneGuardian_Jangseung_B1.fbx" : null;
            if (file == null) return null;
            EnsureFolder(AssetRoot + "/Models");
            string path = AssetRoot + "/Models/" + file;
            string source = Path.Combine(Output, "Blender", file);
            if (!File.Exists(source)) throw new InvalidOperationException("Missing authored Blender source: " + source);
            if (!File.Exists(path)) File.Copy(source, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (!importer.isReadable || importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.isReadable = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false; importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.SaveAndReimport();
            }
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var filter = asset.GetComponentInChildren<MeshFilter>();
            if (filter == null) throw new InvalidOperationException("FBX contains no mesh: " + path);
            var result = UnityEngine.Object.Instantiate(filter.sharedMesh); result.name = family;
            var vertices = result.vertices; var matrix = filter.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            result.vertices = vertices; result.RecalculateBounds();
            Vector3 center = result.bounds.center, dimensions = result.bounds.size;
            float scale = Mathf.Max(dimensions.x, dimensions.y, dimensions.z);
            for (int i = 0; i < vertices.Length; i++) vertices[i] = (vertices[i] - center) / scale;
            result.vertices = vertices; result.RecalculateBounds(); result.RecalculateNormals(); result.RecalculateTangents();
            File.WriteAllText(Path.Combine(Output, "Blender", family + "_unity_import.json"),
                JsonUtility.ToJson(new MeshImportAudit { family=family, vertices=result.vertexCount, triangles=result.triangles.Length/3,
                    colors=result.colors.Length, bounds=result.bounds.size, originalDimensions=dimensions, transformDeterminant=matrix.determinant }, true));
            return result;
        }
        [Serializable] private class MeshImportAudit
        { public string family; public int vertices, triangles, colors; public Vector3 bounds, originalDimensions; public float transformDeterminant; }
        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var value = AssetDatabase.LoadAssetAtPath<T>(path); if (value != null) return value;
            value = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(value, path); return value;
        }
        private static Material MaterialAt(string name, Shader shader, Texture2D texture, bool soft, bool body = false, float metal = 0, float fluid = 0)
        {
            string path = AssetRoot + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader; mat.SetTexture("_BaseMap", texture); mat.SetFloat("_Pattern", texture != null ? 1 : 0); mat.SetFloat("_Soft", soft ? 1 : 0);
            mat.SetFloat("_Body", body ? 1 : 0); mat.SetFloat("_ZWrite", body ? 1 : 0); mat.SetFloat("_Metal", metal);
            mat.SetFloat("_Fluid", fluid);
            if (soft) mat.SetColor("_BaseColor", Color.white);
            mat.enableInstancing = true; EditorUtility.SetDirty(mat); return mat;
        }
    }
}
