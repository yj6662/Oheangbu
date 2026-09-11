using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120MeshyBuilder
    {
        const string Root = Vfx120Editor.AssetRoot + "/MeshySummons/";
        [Serializable] class Row
        {
            public string id, prefab, shader;
            public int requestedPolycount = 12000, actualTriangles, vertices, renderers, skins, animators;
            public Vector3 size, bottom;
            public bool shaderSupported, baseMap, normalMap, maskMap, triangleMatch;
        }
        [Serializable] class Report
        {
            public string status = "STATIC_IMPORT_AND_PROFILE_CONNECTION_ONLY";
            public string runtime = "UNVERIFIED", animation = "NOT_AUTHORED", art = "PROVISIONAL";
            public int actualTriangles;
            public Row[] models;
        }
        public static string Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode required");
            var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
            string[] ids = { "016_ACF0", "040_B188", "088_C19C" };
            string[] glyphs = { "곰", "놈", "솜" };
            int[] expected = { 12543, 12532, 12414 };
            float[] metallic = { 0, .05f, .5f };
            var rows = new List<Row>();
            for (int n = 0; n < ids.Length; n++)
            {
                string id = ids[n], folder = Root + id + "/", path = folder + "SM_Summon_" + id + ".fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) throw new FileNotFoundException(path);
                importer.globalScale = 1; importer.useFileScale = true;
                importer.importAnimation = false; importer.animationType = ModelImporterAnimationType.None;
                importer.isReadable = false; importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importCameras = false; importer.importLights = false;
                importer.SaveAndReimport();
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (source == null) throw new InvalidOperationException("FBX import failed: " + id);
                string prefix = folder + "Textures/T_" + id;
                SetTexture(prefix + "_BaseColor.png", true, false, false);
                SetTexture(prefix + "_Normal.png", false, true, false);
                SetTexture(prefix + "_Roughness_Min065.png", false, false, true);
                var roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_Roughness_Min065.png");
                var pixels = roughness.GetPixels32();
                for (int p = 0; p < pixels.Length; p++)
                    pixels[p] = new Color32((byte)Mathf.RoundToInt(metallic[n] * 255), 0, 0, (byte)(255 - pixels[p].r));
                var packed = new Texture2D(roughness.width, roughness.height, TextureFormat.RGBA32, false, true);
                packed.SetPixels32(pixels); packed.Apply(false);
                string packedPath = prefix + "_MetallicSmoothness.png";
                File.WriteAllBytes(packedPath, packed.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(packed);
                AssetDatabase.ImportAsset(packedPath, ImportAssetOptions.ForceSynchronousImport);
                SetTexture(packedPath, false, false, false);
                SetTexture(prefix + "_Roughness_Min065.png", false, false, false);

                string materialPath = folder + "M_Summon_" + id + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
                material.shader = shader;
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_BaseColor.png"));
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "_Normal.png"));
                material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath));
                material.SetColor("_BaseColor", new Color(.82f, .82f, .79f, 1));
                material.SetFloat("_BumpScale", .7f); material.SetFloat("_Smoothness", 1);
                material.SetFloat("_Metallic", metallic[n]); material.SetFloat("_SmoothnessTextureChannel", 0);
                material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetTexture("_EmissionMap", material.GetTexture("_BaseMap"));
                material.SetColor("_EmissionColor", new Color(.12f, .115f, .10f));
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                material.EnableKeyword("_EMISSION");
                // Shared source PBR textures remain intact. The owner only fades BaseColor.a.
                material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", (float)CullMode.Back);
                material.SetFloat("_AlphaClip", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = 3000;
                // Run the installed URP material validator during authoring, before the
                // source-immutability baseline. This prevents lazy inspector repair.
                var gui = ShaderGUIUtility(material.shader);
                if (gui != null) gui.ValidateMaterial(material);
                EditorUtility.SetDirty(material);

                var host = new GameObject("PF_Summon_" + id);
                try
                {
                    var model = UnityEngine.Object.Instantiate(source, host.transform, false);
                    model.name = "Meshy7_AuthoredModel";
                    var renderers = model.GetComponentsInChildren<Renderer>(true);
                    foreach (var renderer in renderers)
                    {
                        renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                        renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                    }
                    var meshes = model.GetComponentsInChildren<MeshFilter>(true).Select(x => x.sharedMesh).Where(x => x != null).ToArray();
                    int triangles = meshes.Sum(x => Enumerable.Range(0, x.subMeshCount).Sum(i => (int)x.GetIndexCount(i) / 3));
                    if (triangles != expected[n]) throw new InvalidOperationException(id + " unexpected triangles=" + triangles);
                    var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                    string prefabPath = folder + "PF_Summon_" + id + ".prefab";
                    var prefab = PrefabUtility.SaveAsPrefabAsset(host, prefabPath);
                    var profile = catalog.Entries.First(x => x.Glyph == glyphs[n]).Profile;
                    profile.SummonPrefab = prefab; profile.SummonScale = 1; profile.SummonYaw = 0;
                    profile.NativeFieldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120TraditionalBuilder.Output + "/KTP_Summon_LotusCourt.prefab");
                    profile.NativeFieldRole = Vfx120TraditionalMotif.Role.Summon; profile.NativeReplaceBody = false;
                    profile.NativeCastPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120TraditionalBuilder.Output + "/KTP_Cast_LeafBloom.prefab");
                    profile.NativeScale = .65f; EditorUtility.SetDirty(profile);
                    rows.Add(new Row { id = id, prefab = prefabPath, shader = shader.name,
                        actualTriangles = triangles, vertices = meshes.Sum(x => x.vertexCount), renderers = renderers.Length,
                        skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length,
                        animators = model.GetComponentsInChildren<Animator>(true).Length,
                        size = bounds.size, bottom = new Vector3(0, 0, 0), shaderSupported = shader.isSupported,
                        baseMap = material.GetTexture("_BaseMap") != null, normalMap = material.GetTexture("_BumpMap") != null,
                        maskMap = material.GetTexture("_MetallicGlossMap") != null, triangleMatch = true });
                }
                finally { UnityEngine.Object.DestroyImmediate(host); }
            }
            AssetDatabase.SaveAssets();
            var report = new Report { actualTriangles = rows.Sum(x => x.actualTriangles), models = rows.ToArray() };
            var json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "meshy_unity_import.json"), json);
            return json;
        }
        static void SetTexture(string path, bool srgb, bool normal, bool readable)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb; importer.isReadable = readable; importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        static UnityEditor.ShaderGUI ShaderGUIUtility(Shader shader)
        {
            // LitShader is internal in the installed URP editor assembly. Use its
            // declared custom-editor type without a new asmdef dependency.
            const string name = "UnityEditor.Rendering.Universal.ShaderGUI.LitShader";
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(name, false);
                if (type != null && typeof(UnityEditor.ShaderGUI).IsAssignableFrom(type))
                    return (UnityEditor.ShaderGUI)Activator.CreateInstance(type, true);
            }
            return null;
        }
    }
}
