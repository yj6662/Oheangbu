using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class CodexMountainAudit
    {
        [Serializable] private sealed class DistanceResult
        {
            public bool passed;
            public string method = "Same fixed world-space patch; orthographic camera moves; linear GPU pixels; atmosphere-off control";
            public float[] metres = { 200, 500, 900 };
            public Color[] atmosphereOff = new Color[3], atmosphereOn = new Color[3], predicted = new Color[3];
            public float maximumControlDelta, maximumEquationError;
        }

        public static string Validate()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != CodexWorldSceneBuilder.ScenePath) return "FAIL: open C2";
            var renderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true))
                .Where(r => r.GetComponent<MeshFilter>()?.sharedMesh != null
                    && AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh)
                        .StartsWith(CodexWorldSceneBuilder.MeshFolder + "/Ridge_", StringComparison.Ordinal)).ToArray();
            var material = AssetDatabase.LoadAssetAtPath<Material>(CodexWorldSceneBuilder.AssetFolder + "/Materials/Mountain_Shared.mat");
            int invalid = 0, propertyBlocks = 0, colliders = 0;
            long tris = 0;
            foreach (var r in renderers)
            {
                if (r.sharedMaterials.Length != 1 || r.sharedMaterial != material) invalid++;
                if (r.HasPropertyBlock()) propertyBlocks++;
                colliders += r.GetComponents<Collider>().Length;
                if (r.shadowCastingMode != ShadowCastingMode.Off) invalid++;
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                if (normals.Length != vertices.Length) invalid++;
                for (int i = 0; i < vertices.Length; i++)
                    if (!float.IsFinite(vertices[i].sqrMagnitude) || !float.IsFinite(normals[i].sqrMagnitude)
                        || normals[i].sqrMagnitude < .95f) invalid++;
                tris += mesh.GetIndexCount(0) / 3;
            }
            bool shaderOk = material != null && material.shader != null && material.shader.isSupported
                && !ShaderUtil.ShaderHasError(material.shader);
            bool pass = renderers.Length == 9 && invalid == 0 && propertyBlocks == 0 && colliders == 0 && shaderOk;
            string result = $"{(pass ? "PASS" : "FAIL")}: mountains={renderers.Length}; sharedMaterial={material?.name}; "
                + $"invalid={invalid}; propertyBlocks={propertyBlocks}; colliders={colliders}; shaderOK={shaderOk}; triangles={tris}";
            Directory.CreateDirectory(CodexWorldAudit.CaptureFolder);
            File.WriteAllText(CodexWorldAudit.CaptureFolder + "/mountain-validation.txt", result);
            return result;
        }

        /// <summary>Actual GPU evidence: camera distance changes, surface position/material do not.</summary>
        public static string ProbeDistance()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != CodexWorldSceneBuilder.ScenePath) return "FAIL: open C2 in Edit mode";
            var source = AssetDatabase.LoadAssetAtPath<Material>(CodexWorldSceneBuilder.AssetFolder + "/Materials/Mountain_Shared.mat");
            if (source == null) return "FAIL: apply mountain material first";
            var driver = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WorldLookDriver>(true)).First();
            driver.Apply();
            const int size = 64;
            GameObject patch = null, eye = null;
            Mesh mesh = null;
            Material material = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            var previous = RenderTexture.active;
            var result = new DistanceResult();
            try
            {
                mesh = new Mesh { name = "~MountainDistancePatch", hideFlags = HideFlags.HideAndDontSave };
                mesh.vertices = new[] { new Vector3(-50,-50,0), new Vector3(-50,50,0), new Vector3(50,50,0), new Vector3(50,-50,0) };
                mesh.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
                mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray();
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateBounds();
                material = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
                patch = new GameObject("~MountainDistancePatch") { hideFlags = HideFlags.HideAndDontSave, layer = 31 };
                patch.transform.position = new Vector3(10000, 0, 0);
                patch.AddComponent<MeshFilter>().sharedMesh = mesh;
                patch.AddComponent<MeshRenderer>().sharedMaterial = material;
                eye = new GameObject("~MountainDistanceCamera") { hideFlags = HideFlags.HideAndDontSave };
                var camera = eye.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 32;
                camera.aspect = 1;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 1500;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                camera.cullingMask = 1 << 31;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                pixels = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
                camera.targetTexture = target;
                float density = source.GetFloat("_AtmosphereDensity");
                float start = source.GetFloat("_AtmosphereStart");
                Color air = Color.Lerp(Shader.GetGlobalColor("_OhInkColor"), Shader.GetGlobalColor("_OhPaperColor"), source.GetFloat("_AtmosphereTone"));
                for (int i = 0; i < 3; i++)
                {
                    eye.transform.position = patch.transform.position - Vector3.forward * result.metres[i];
                    for (int control = 0; control < 2; control++)
                    {
                        material.SetFloat("_AtmosphereDensity", control == 0 ? 0 : density);
                        camera.Render();
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                        pixels.Apply();
                        Color pixel = pixels.GetPixel(size / 2, size / 2);
                        if (control == 0) result.atmosphereOff[i] = pixel;
                        else result.atmosphereOn[i] = pixel;
                    }
                    float d = Mathf.Sqrt(result.metres[i] * result.metres[i] + .5f);
                    float transmission = Mathf.Exp(-density * Mathf.Max(0, d - start));
                    result.predicted[i] = Color.Lerp(air, result.atmosphereOff[i], transmission);
                    result.maximumEquationError = Mathf.Max(result.maximumEquationError, Delta(result.predicted[i], result.atmosphereOn[i]));
                    result.maximumControlDelta = Mathf.Max(result.maximumControlDelta, Delta(result.atmosphereOff[0], result.atmosphereOff[i]));
                }
                result.passed = result.maximumControlDelta < .002f && result.maximumEquationError < .003f
                    && result.atmosphereOn[2].grayscale > result.atmosphereOn[1].grayscale + .01f
                    && result.atmosphereOn[1].grayscale > result.atmosphereOn[0].grayscale + .01f;
                string json = JsonUtility.ToJson(result, true);
                Directory.CreateDirectory(CodexWorldAudit.CaptureFolder);
                File.WriteAllText(CodexWorldAudit.CaptureFolder + "/mountain-distance-probe.json", json);
                return json;
            }
            finally
            {
                RenderTexture.active = previous;
                if (eye != null) Object.DestroyImmediate(eye);
                if (patch != null) Object.DestroyImmediate(patch);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (mesh != null) Object.DestroyImmediate(mesh);
                if (material != null) Object.DestroyImmediate(material);
            }
        }

        private static float Delta(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r-b.r), Mathf.Abs(a.g-b.g), Mathf.Abs(a.b-b.b));
    }
}
