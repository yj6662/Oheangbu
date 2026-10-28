using System;
using System.IO;
using System.Linq;
using Oheangbu.App.SpellFx;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-153b variant 우/M (2026-10-29): builds the Meshy-wave route of the water ring 우.
    ///   1. copies the three models of Art/SpellVFX308/Restart/u_M/models (FBX + 2048 px colour JPG; the repo folder is outside Assets) into Assets/.../Restart/U/M/ and imports them
    ///      (no animation, no materials - the paint shader gets the texture; the part normalises the size / pivot at run time, so the file scale does not matter),
    ///   2. builds Restart/U/U_M.prefab (SpellFxPlayer + SpellFxMeshWavesPart wired to the models, textures and Oheangbu/SpellFx/MeshPaint),
    ///   3. MERGES the catalogue entry 우/M (every other entry stays).
    /// Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxSetupMCommands Run "setup:m"
    /// No dialogs. Saves only the assets it writes (SaveAssetIfDirty per asset). Reimports an asset only when a setting differs; the prefab is overwritten in place (same path / GUID).</summary>
    public static class SpellFxSetupMCommands
    {
        const string Root = "Assets/_Project/Art/SpellVFX308/Restart";
        const string MDir = Root + "/U/M";
        const string ShaderPath = Root + "/Shaders/SpellFxMeshPaint.shader";
        const string RepoModels = "Art/SpellVFX308/Restart/u_M/models";

        static readonly (string name, bool low)[] Models = { ("crest_right", false), ("crest_left", false), ("mounds", true) };

        public static string Run(string command)
        {
            try
            {
                var c = (command ?? "").Trim();
                if (!string.Equals(c, "setup:m", StringComparison.OrdinalIgnoreCase)) return "REFUSED setup: setup:m";
                return SetupM();
            }
            catch (Exception e) { return "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        static string SetupM()
        {
            var log = new System.Text.StringBuilder();
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string srcDir = Path.Combine(repo, RepoModels);
            if (!Directory.Exists(srcDir)) return "REFUSED setup:m: " + srcDir + " missing";
            Directory.CreateDirectory(Path.Combine(repo, "Oheangbu", MDir));

            // ---- 1. copy (only when the bytes differ), then import ----
            bool copied = false;
            foreach (var m in Models)
            {
                foreach (var file in new[] { "wave_" + m.name + ".fbx", "wave_" + m.name + "_paint.jpg" })
                {
                    string from = Path.Combine(srcDir, file), to = Path.Combine(repo, "Oheangbu", MDir, file);
                    if (!File.Exists(from)) return "REFUSED setup:m: " + from + " missing";
                    if (!File.Exists(to) || new FileInfo(to).Length != new FileInfo(from).Length || !File.ReadAllBytes(to).AsSpan().SequenceEqual(File.ReadAllBytes(from)))
                    { File.Copy(from, to, true); copied = true; }
                }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (copied) log.Append("models copied; ");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) return "REFUSED setup:m: " + ShaderPath + " missing (or did not compile)";

            var entries = new SpellFxMeshWavesPart.WaveModel[Models.Length];
            for (int i = 0; i < Models.Length; i++)
            {
                string fbx = MDir + "/wave_" + Models[i].name + ".fbx", jpg = MDir + "/wave_" + Models[i].name + "_paint.jpg";
                ImportModel(fbx);
                var paint = ImportPaint(jpg);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (go == null || paint == null) return "REFUSED setup:m: " + Models[i].name + " did not import (model " + (go != null) + ", paint " + (paint != null) + ")";
                entries[i] = new SpellFxMeshWavesPart.WaveModel { Name = Models[i].name, Model = go, Paint = paint, Low = Models[i].low };
                var mesh = go.GetComponentInChildren<MeshFilter>(true);
                if (mesh != null && mesh.sharedMesh != null)
                {
                    var b = mesh.sharedMesh.bounds.size;
                    log.Append(Models[i].name + " mesh bounds " + b.x.ToString("0.###") + " x " + b.y.ToString("0.###") + " x " + b.z.ToString("0.###") + " (" + mesh.sharedMesh.GetIndexCount(0) / 3 + " tris); ");
                }
            }

            // ---- 2. the prefab ----
            var prefab = Prefab(Root + "/U/U_M.prefab", 1.8f, go =>
            {
                var player = go.GetComponent<SpellFxPlayer>();
                var pso = new SerializedObject(player); pso.FindProperty("tailSeconds").floatValue = 1.2f; pso.ApplyModifiedPropertiesWithoutUndo();      // a hit late in the life still plays its wave
                var part = go.AddComponent<SpellFxMeshWavesPart>();
                var so = new SerializedObject(part);
                so.FindProperty("paintShader").objectReferenceValue = shader;
                so.FindProperty("radius").floatValue = 2.2f;
                so.FindProperty("lifeSeconds").floatValue = 1.8f;
                so.FindProperty("outlineWidth").floatValue = 0.025f;
                var arr = so.FindProperty("models"); arr.arraySize = entries.Length;
                for (int i = 0; i < entries.Length; i++)
                {
                    var el = arr.GetArrayElementAtIndex(i);
                    el.FindPropertyRelative("Name").stringValue = entries[i].Name;
                    el.FindPropertyRelative("Model").objectReferenceValue = entries[i].Model;
                    el.FindPropertyRelative("Paint").objectReferenceValue = entries[i].Paint;
                    el.FindPropertyRelative("Low").boolValue = entries[i].Low;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            });
            log.Append("M ok; ");

            // ---- 3. catalogue: MERGE - only 우/M is replaced ----
            var cat = AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(SpellFxCatalog.AssetPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SpellFxCatalog>(); AssetDatabase.CreateAsset(cat, SpellFxCatalog.AssetPath); }
            var list = (cat.entries ?? new SpellFxCatalog.Entry[0]).Where(e => e != null && !(e.letter == "우" && e.variant == "M")).ToList();
            list.Add(new SpellFxCatalog.Entry { letter = "우", variant = "M", prefab = prefab });
            cat.entries = list.ToArray();
            EditorUtility.SetDirty(cat); AssetDatabase.SaveAssetIfDirty(cat);
            log.Append("catalogue: " + string.Join(", ", cat.entries.Select(e => e.letter + "/" + e.variant)));
            return log.ToString();
        }

        static void ImportModel(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) return;
            bool change = imp.importAnimation || imp.animationType != ModelImporterAnimationType.None || imp.materialImportMode != ModelImporterMaterialImportMode.None
                || imp.importCameras || imp.importLights || imp.importBlendShapes || !Mathf.Approximately(imp.globalScale, 1f) || imp.meshCompression != ModelImporterMeshCompression.Off
                || imp.isReadable || imp.generateSecondaryUV;
            if (!change) return;
            imp.importAnimation = false; imp.animationType = ModelImporterAnimationType.None;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.importCameras = false; imp.importLights = false; imp.importBlendShapes = false;
            imp.globalScale = 1f; imp.meshCompression = ModelImporterMeshCompression.Off; imp.isReadable = false; imp.generateSecondaryUV = false;
            imp.SaveAndReimport();
        }

        /// <summary>The painted colour map: sRGB, mipmaps, trilinear, clamp.</summary>
        static Texture2D ImportPaint(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool change = imp.textureType != TextureImporterType.Default || !imp.sRGBTexture || !imp.mipmapEnabled || imp.wrapMode != TextureWrapMode.Clamp
                    || imp.filterMode != FilterMode.Trilinear || imp.anisoLevel != 4 || imp.maxTextureSize != 2048 || imp.textureCompression != TextureImporterCompression.CompressedHQ || imp.alphaSource != TextureImporterAlphaSource.None;
                if (change)
                {
                    imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp;
                    imp.filterMode = FilterMode.Trilinear; imp.anisoLevel = 4; imp.maxTextureSize = 2048; imp.textureCompression = TextureImporterCompression.CompressedHQ;
                    imp.alphaSource = TextureImporterAlphaSource.None;
                    imp.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static GameObject Prefab(string path, float playSeconds, Action<GameObject> build)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                var player = go.AddComponent<SpellFxPlayer>();
                var so = new SerializedObject(player); so.FindProperty("playSeconds").floatValue = playSeconds; so.ApplyModifiedPropertiesWithoutUndo();
                build(go);
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
