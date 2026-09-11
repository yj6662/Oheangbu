using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120TraditionalBuilder
    {
        const string Pack = "Assets/KoreanTraditionalPattern_Effect/Prefabs/";
        public const string Output = Vfx120Editor.AssetRoot + "/Traditional";

        public static string Build()
        {
            if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot, "Traditional");
            // Variants retain the vendor's actual mesh, material and PS curves.
            // Only the carrier frame and named overbright fill layers are changed.
            var cast = Group("KTP_Cast_LeafBloom");
            var emblem = Add(cast, "Bottom/Bottom03-01.prefab", "Pattern", Quaternion.Euler(90, 0, 0));
            emblem.name = "Primary_AuthoredPattern";
            emblem.transform.localScale *= .28f;
            var charge = Add(cast, "Fly/Fly06-01.prefab", "Charge", Quaternion.Euler(0, 90, 0));
            charge.name = "Supporting_Charge"; Opacity(charge, .30f);
            GameObject castPrefab = Save(cast);

            var impact = Group("KTP_Impact_LeafBurst");
            Add(impact, "Fly/Fly01-01.prefab", "Explosion", Quaternion.Euler(0, 90, 0));
            GameObject impactPrefab = Save(impact);

            var summon = Group("KTP_Summon_LotusCourt");
            var floor = Add(summon, "Bottom/Bottom12-01.prefab", "", Quaternion.identity);
            foreach (var t in floor.GetComponentsInChildren<Transform>(true))
                if (t.name == "Light_00" || t.name == "Light_02") t.gameObject.SetActive(false);
            GameObject summonPrefab = Save(summon);

            var shield = Group("KTP_Shield_JadeCanopy");
            var canopy = Add(shield, "Bottom/Bottom04-01.prefab", "", Quaternion.identity);
            foreach (var t in canopy.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Aura") || t.name.StartsWith("Light")) Opacity(t.gameObject, .42f);
            AddShieldPanels(shield, Pack + "Bottom/Bottom04-01.prefab");
            GameObject shieldPrefab = Save(shield);

            var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
            foreach (var entry in catalog.Entries)
            {
                var p = entry.Profile;
                if (p.Glyph == "가") { p.NativeCastPrefab = castPrefab; p.NativeImpactPrefab = impactPrefab; p.NativeScale = .48f; }
                else if (p.Glyph == "곰") { p.NativeCastPrefab = castPrefab; p.NativeFieldPrefab = summonPrefab; p.NativeFieldRole = Vfx120TraditionalMotif.Role.Summon; p.NativeReplaceBody = false; p.NativeScale = .65f; }
                else if (p.Glyph == "구") { p.NativeCastPrefab = castPrefab; p.NativeFieldPrefab = shieldPrefab; p.NativeFieldRole = Vfx120TraditionalMotif.Role.Shield; p.NativeReplaceBody = true; p.NativeScale = .52f; }
                else continue;
                EditorUtility.SetDirty(p);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "ktp_rework_build.txt"),
                "KTP authored subtree prototype: cast, impact, summon circle, shield.\n" +
                "Profiles: 가, 곰, 구. TEST: native source reuse implemented; art/runtime approval pending.\n" +
                "Cast: Bottom03-01/Pattern carrier scale 0.28 with Fly06-01/Charge at 30% support.\n" +
                "Impact: Fly01-01/Explosion, source emission plane yaw +90.\n" +
                "Summon: Bottom12-01, Light_00/Light_02 flat fill disabled in variant only.\n" +
                "Shield: Bottom04-01, Aura/Light fill alpha x0.42.\n" +
                "Original vendor prefabs, materials, meshes and textures unchanged.\n" +
                "Existing procedural summon creature is still provisional pending Meshy import.\n");
            return "BUILT_4_NATIVE_MOTIF_PROTOTYPES_AND_3_PROFILE_CONNECTIONS";
        }
        static GameObject Group(string name) => new GameObject(name);
        // Four open curved panels carry the vendor Pattern PS, including its UV/custom
        // data/alpha growth. The curved carrier adds an actual protective vertical boundary.
        public static void AddShieldPanels(GameObject root, string sourcePrefabPath)
        {
            const string meshPath = Output + "/KTP_ShieldArc.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                const int columns = 16, rows = 8;
                var vertices = new Vector3[(columns + 1) * (rows + 1)];
                var uv = new Vector2[vertices.Length]; var indices = new int[columns * rows * 6];
                for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
                {
                    int i = y * (columns + 1) + x; float u = x / (float)columns, v = y / (float)rows;
                    float angle = Mathf.Lerp(-40, 40, u) * Mathf.Deg2Rad;
                    vertices[i] = new Vector3(Mathf.Sin(angle) * 2.6f, .10f + v * 3.6f, Mathf.Cos(angle) * 2.6f);
                    uv[i] = new Vector2(u, v);
                }
                int k = 0;
                for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
                {
                    int a = y * (columns + 1) + x, b = a + columns + 1;
                    indices[k++] = a; indices[k++] = a + 1; indices[k++] = b;
                    indices[k++] = a + 1; indices[k++] = b + 1; indices[k++] = b;
                }
                mesh = new Mesh { name = "KTP_ShieldArc" }; mesh.vertices = vertices; mesh.uv = uv;
                mesh.triangles = indices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
            var pattern = source.GetComponentsInChildren<Transform>(true).First(t => t.name == "Pattern").gameObject;
            for (int i = 0; i < 4; i++)
            {
                var panel = UnityEngine.Object.Instantiate(pattern, root.transform, false);
                panel.name = "Upright_PatternPanel_" + i;
                panel.transform.localPosition = Vector3.zero;
                panel.transform.localRotation = Quaternion.Euler(0, i * 90, 0);
                panel.transform.localScale = Vector3.one; panel.SetActive(true);
                var ps = panel.GetComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.startSize3D = false; main.startSize = 1; main.startSpeed = 0;
                main.startRotation3D = false; main.startRotation = 0; main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var shape = ps.shape; shape.enabled = false;
                var rotation = ps.rotationOverLifetime; rotation.enabled = false;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = mesh;
                renderer.alignment = ParticleSystemRenderSpace.Local;
            }
        }
        static GameObject Add(GameObject root, string path, string child, Quaternion rotation)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + path);
            if (source == null) throw new ArgumentException(path);
            if (!string.IsNullOrEmpty(child))
                source = source.GetComponentsInChildren<Transform>(true).First(t => t.name.Equals(child, StringComparison.OrdinalIgnoreCase)).gameObject;
            var copy = UnityEngine.Object.Instantiate(source, root.transform, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = rotation * copy.transform.localRotation;
            copy.SetActive(true);
            foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>(true))
            { ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); var main = ps.main; main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None; }
            return copy;
        }
        static void Opacity(GameObject root, float factor)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main; var color = main.startColor;
                color.colorMin = Alpha(color.colorMin, factor); color.colorMax = Alpha(color.colorMax, factor);
                if (color.gradientMin != null) color.gradientMin = GradientAlpha(color.gradientMin, factor);
                if (color.gradientMax != null) color.gradientMax = GradientAlpha(color.gradientMax, factor);
                main.startColor = color;
            }
        }
        static Color Alpha(Color color, float factor) { color.a *= factor; return color; }
        static Gradient GradientAlpha(Gradient source, float factor)
        {
            var target = new Gradient { mode = source.mode };
            var keys = source.alphaKeys; for (int i = 0; i < keys.Length; i++) keys[i].alpha *= factor;
            target.SetKeys(source.colorKeys, keys); return target;
        }
        static GameObject Save(GameObject root)
        {
            // One vendor Charge helper has an unresolved material but no emission.
            // Keep its PS transform/timing; give its disabled renderer a valid shared
            // native material so no missing shader reference enters the runtime variant.
            var renderers = root.GetComponentsInChildren<ParticleSystemRenderer>(true);
            var valid = renderers.SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.shader != null && m.shader.isSupported);
            foreach (var renderer in renderers)
                if (renderer.sharedMaterials.Any(m => m == null || m.shader == null || !m.shader.isSupported))
                {
                    var ps = renderer.GetComponent<ParticleSystem>();
                    if (ps == null || ps.emission.enabled || valid == null)
                        throw new InvalidOperationException("Active native renderer has unsupported material: " + renderer.name);
                    renderer.sharedMaterial = valid; renderer.enabled = false;
                }
            try { return PrefabUtility.SaveAsPrefabAsset(root, Output + "/" + root.name + ".prefab"); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
