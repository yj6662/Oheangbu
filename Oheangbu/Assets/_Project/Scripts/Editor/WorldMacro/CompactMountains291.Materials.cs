using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRebuildAuthoring
    {
        const string InkGroundSource291 = "Assets/_Project/Art/World/Surface276/Ground_380bd847120aad54595d413b786e1948.mat";

        // Apply only to the authored mountain additions. The existing valley materials,
        // camera, sky, shared painting shaders and provider assets remain unchanged.
        static string ApplyInkMaterials291()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != Scene290 || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Mountain candidate Edit scene required");
            var template = AssetDatabase.LoadAssetAtPath<Material>(InkGroundSource291);
            if (template == null || template.shader.name != "Oheangbu/Study/InkPaintingGround")
                throw new InvalidOperationException("The incumbent compact ground material is missing");
            var foliageShader = Shader.Find("Oheangbu/CompactNaturalVegetation");
            if (foliageShader == null) throw new InvalidOperationException("Compact vegetation shader is missing");

            string folder = A291 + "/Materials";
            DevSceneKit.EnsureFolder(folder);
            string[] names = { "Compact_Mountains_290", "Cheongrim_AssetPass_290", "Compact_MountainContent_290" };
            var roots = scene.GetRootGameObjects().Where(g => names.Contains(g.name)).ToArray();
            if (roots.Length == 0) throw new InvalidOperationException("No owned mountain roots found");
            var renderers = roots.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToArray();
            var derivatives = new Dictionary<string, Material>();
            var protectedFiles = new Dictionary<string, string>();
            protectedFiles.Add(InkGroundSource291, InkMaterialDigest291(InkGroundSource291));
            int changed = 0, stoneBindings = 0, foliageBindings = 0;

            foreach (var renderer in renderers)
            {
                var original = renderer.sharedMaterials;
                var replacement = original.ToArray();
                bool touched = false;
                for (int i = 0; i < original.Length; i++)
                {
                    var source = original[i];
                    if (source == null || source.shader == null) continue;
                    string sourcePath = AssetDatabase.GetAssetPath(source);
                    // Recover the immutable provider on a repeated application, so tint and
                    // relief adjustments never accumulate and no derivative is duplicated.
                    if (sourcePath.StartsWith(folder + "/", StringComparison.Ordinal))
                    {
                        string[] parts = source.name.Split('_');
                        if (parts.Length != 3 || parts[0] != "Ink291") continue;
                        var provider = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(parts[2]));
                        if (provider == null) throw new InvalidOperationException("Missing ink source for " + sourcePath);
                        source = provider;
                        sourcePath = AssetDatabase.GetAssetPath(source);
                    }
                    bool rock = source.shader.name == "Oheangbu/Prototype/Granite285";
                    bool foliage = source.shader.name == "Oheangbu/Prototype/CliffProp278" &&
                        sourcePath.StartsWith(A285 + "/Materials/Plant_", StringComparison.Ordinal);
                    if (!rock && !foliage) continue;
                    string role = foliage ? "foliage" : TerrainRenderer291(renderer.transform) ? "terrain" :
                        source.name.IndexOf("Rooted_soil", StringComparison.OrdinalIgnoreCase) >= 0 ? "soil" :
                        source.name.IndexOf("Worn_steps", StringComparison.OrdinalIgnoreCase) >= 0 ? "tread" : "rock";
                    string guid = AssetDatabase.AssetPathToGUID(sourcePath);
                    if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Nonpersistent source " + source.name);
                    string key = "Ink291_" + role + "_" + guid;
                    if (!derivatives.TryGetValue(key, out var material))
                    {
                        if (!protectedFiles.ContainsKey(sourcePath)) protectedFiles.Add(sourcePath, InkMaterialDigest291(sourcePath));
                        var prepared = foliage ? new Material(source) : new Material(template);
                        try
                        {
                            prepared.name = key;
                            prepared.enableInstancing = true;
                            if (foliage) ConfigureFoliageInk291(prepared, template, foliageShader);
                            else ConfigureStoneInk291(prepared, source, role);
                            string path = folder + "/" + key + ".mat";
                            material = AssetDatabase.LoadAssetAtPath<Material>(path);
                            if (material == null)
                            {
                                AssetDatabase.CreateAsset(prepared, path);
                                material = prepared;
                                prepared = null;
                            }
                            else EditorUtility.CopySerialized(prepared, material);
                            EditorUtility.SetDirty(material);
                            AssetDatabase.SaveAssetIfDirty(material);
                            derivatives.Add(key, material);
                        }
                        finally { if (prepared != null) Object.DestroyImmediate(prepared); }
                    }
                    replacement[i] = material;
                    touched |= replacement[i] != original[i];
                    if (foliage) foliageBindings++; else stoneBindings++;
                }
                if (!touched) continue;
                renderer.sharedMaterials = replacement;
                EditorUtility.SetDirty(renderer);
                changed++;
            }
            foreach (var pair in protectedFiles)
                if (InkMaterialDigest291(pair.Key) != pair.Value)
                    throw new InvalidOperationException("Source material unexpectedly changed: " + pair.Key);
            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory(O291);
            string report = $"Candidate-only ink materials: {derivatives.Count}; rebound renderers {changed}; stone slots {stoneBindings}, foliage slots {foliageBindings}. " +
                "Incumbent Surface276 palette and camera-owned distance fog retained. Stale geography, authored form, road, strata and grass maps disabled in new derivatives. " +
                "CC0 metre-scale rock colour and normals retained near the viewer; terrain retains slope-dependent soil/rock. Low source-texture saturation preserves pale earth/green pigment, while final ink/paper palette is no longer desaturated a second time. Provider and incumbent material hashes unchanged.";
            File.WriteAllText(O291 + "/materials.txt", report + "\n" + string.Join("\n", protectedFiles.Select(p => p.Value + " " + p.Key)));
            return report;
        }

        static bool TerrainRenderer291(Transform value)
        {
            for (var t = value; t != null; t = t.parent)
                if (t.name.StartsWith("Terrain_", StringComparison.Ordinal)) return true;
            return false;
        }

        static void ConfigureStoneInk291(Material material, Material source, string role)
        {
            bool terrain = role == "terrain", soil = role == "soil", tread = role == "tread";
            // Surface276 already carries the correct ink/paper palette and detail system.
            // Its old data textures describe another height field, not these new mountains.
            foreach (string property in new[] { "_PaintedGeoEnabled", "_PaintedFormEnabled", "_PaintedFormAuthored", "_PaintedRoadBank",
                "_PaintedFarPath", "_PaintedFarPathAir", "_PaintedPathMountain", "_PaintedValleyReserve", "_StrataStrength276",
                "_GroundPath", "_GrassCoverStrength267", "_RealmTintStrength", "_SourceUV", "_EdgeFade" }) SetInkFloat291(material, property, 0);
            foreach (string property in new[] { "_PaintedGeoField", "_PaintedFormMap", "_PaintedRoadBankField", "_StrataField276", "_GroundPathMask", "_GrassCover267" })
                if (material.HasProperty(property)) material.SetTexture(property, null);
            SetInkFloat291(material, "_RebuildScreenFog", 1);
            SetInkFloat291(material, "_CIAtmosphereOverride", 1);
            SetInkFloat291(material, "_WashStrength", 0);
            material.SetVector("_CIAirStrengths", Vector4.zero);
            // InkChroma268 is applied AFTER the ink/paper palette. Inheriting the old
            // .2 regional retention turned even warm paper and brown-black ink grey.
            // Restrain the sampled photographs instead; preserve the chosen pigments.
            SetInkFloat291(material, "_InkChromaRetain268", 1);
            SetInkFloat291(material, "_Saturation", terrain ? .2f : soil ? .18f : .12f);
            material.SetColor("_CIInk", terrain ? new Color(.165f, .149f, .133f, 1) :
                soil ? new Color(.18f, .151f, .119f, 1) : new Color(.16f, .145f, .126f, 1));
            material.SetColor("_CIPaper", soil ? new Color(.952f, .921f, .858f, 1) : new Color(.969f, .945f, .894f, 1));
            SetInkFloat291(material, "_GroundKind", soil || tread ? 1 : 0);
            // Broad ink masses remain visible under the near scanned surface. Normal
            // relief is kept separately, so reducing photographic colour is not blur.
            SetInkFloat291(material, "_RebuildDetailStrength", terrain ? .58f : soil ? .62f : tread ? .50f : .48f);
            material.SetVector("_RebuildDetailRange", new Vector4(18, 110, 0, 0));
            SetInkFloat291(material, "_RebuildGravelStrength", terrain || soil ? .42f : 0);
            SetInkFloat291(material, "_BumpScale", .7f);
            SetInkFloat291(material, "_CINormalStrength", .7f);
            // Path tone stays close to adjacent weathered stone instead of a pale road stripe.
            if (tread) material.SetVector("_CITones", new Vector4(.14f, .30f, .13f, .25f));
            var albedo = source.GetTexture("_BaseMap");
            var normal = source.GetTexture("_BumpMap");
            material.SetTexture("_RockMap", albedo);
            material.SetTexture("_RockNormal", normal);
            if (!terrain)
            {
                material.SetTexture("_GrassMap", albedo);
                material.SetTexture("_GrassNormal", normal);
                material.SetTexture("_DirtMap", albedo);
                material.SetTexture("_DirtNormal", normal);
            }
            float tiling = source.HasProperty("_WorldScale") ? source.GetFloat("_WorldScale") : .2857f;
            var incumbentTiling = material.GetVector("_Tiling");
            material.SetVector("_Tiling", terrain ? new Vector4(incumbentTiling.x, incumbentTiling.y, tiling, 0) : new Vector4(tiling, tiling, tiling, 0));
        }

        static void ConfigureFoliageInk291(Material material, Material template, Shader shader)
        {
            // Preserve provider alpha, UV, wind anchoring and LOD distances; exchange only
            // the prototype colour response for the compact foliage response.
            material.shader = shader;
            foreach (string property in new[] { "_CIInk", "_CIPaper", "_CIAir", "_CITones", "_CIDetailRange", "_CIRockRange",
                "_CIMountainRange", "_CIAirRange", "_CIMidAirRange", "_CIFarAirRange", "_CIFoliagePainterly" })
                if (material.HasProperty(property) && template.HasProperty(property)) material.SetVector(property, template.GetVector(property));
            SetInkFloat291(material, "_CIEnabled", 1);
            SetInkFloat291(material, "_CIDarkNear", 1);
            SetInkFloat291(material, "_CIPainterly", 1);
            SetInkFloat291(material, "_CIAtmosphereOverride", 1);
            material.SetVector("_CIAirStrengths", Vector4.zero);
            SetInkFloat291(material, "_WashStrength", 0);
            SetInkFloat291(material, "_WindExternalClock", 1);
            SetInkFloat291(material, "_InkChromaRetain268", 1);
            SetInkFloat291(material, "_Saturation", .2f);
            SetInkFloat291(material, "_AmbientFloor", .30f);
            SetInkFloat291(material, "_LightResponse", .65f);
            SetInkFloat291(material, "_BumpScale", Mathf.Min(.65f, material.GetFloat("_BumpScale")));
        }

        static void SetInkFloat291(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        static string InkMaterialDigest291(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
