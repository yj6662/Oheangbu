using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Oheangbu.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>SPEC-DEV-CODEX-WORLD: repeatable comparison captures and scene integrity measurements.</summary>
    public static class CodexWorldAudit
    {
        public const string CaptureFolder = "Screenshots/CodexWorld";
        private const float EyeHeight = 1.8f;
        private const float GroundTolerance = 0.15f;
        private static readonly string[] CutNames = { "mine", "threshold", "valley", "inn", "upper_path" };

        [MenuItem("Oheangbu/Dev/Codex World/Capture five views")]
        public static void CaptureMenu() => Debug.Log(CaptureAll());

        [MenuItem("Oheangbu/Dev/Codex World/Validate scene")]
        public static void ValidateMenu() => Debug.Log(Validate());

        /// <summary>Fixed eye-height views, 1920x1080, FOV 60, post processing on, antialiasing off.</summary>
        public static string CaptureCut(int cut)
        {
            if (cut < 1 || cut > 5) return "FAIL: cut must be 1..5.";
            var scene = SceneManager.GetActiveScene();
            if (scene.path != CodexWorldSceneBuilder.ScenePath)
                return "FAIL: open " + CodexWorldSceneBuilder.ScenePath + " before capturing.";

            Vector3 position;
            float yaw, pitch;
            switch (cut)
            {
                case 1: position = new Vector3(0f, EyeHeight, -30f); yaw = 0f; pitch = 0f; break;
                case 2: position = new Vector3(0f, EyeHeight, -3f); yaw = 0f; pitch = 0f; break;
                case 3: position = new Vector3(0f, CodexWorldGeometry.Height(0f, 12f) + EyeHeight, 12f); yaw = -12f; pitch = -3f; break;
                case 4: position = new Vector3(-3f, CodexWorldGeometry.Height(-3f, 27f) + EyeHeight, 27f); yaw = -40f; pitch = -5f; break;
                default: position = new Vector3(10f, CodexWorldGeometry.Height(10f, 90f) + EyeHeight, 90f); yaw = -5f; pitch = -3f; break;
            }

            WorldLookDriver driver = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                driver = root.GetComponentInChildren<WorldLookDriver>(true);
                if (driver != null) break;
            }
            if (driver == null || driver.Palette == null)
                return "FAIL: active scene has no WorldLookDriver with a palette.";
            driver.Apply();

            const int width = 1920;
            const int height = 1080;
            GameObject captureObject = null;
            Camera camera = null;
            RenderTexture renderTexture = null;
            Texture2D pixels = null;
            RenderTexture previousActive = RenderTexture.active;
            var hiddenRigRenderers = new List<Renderer>();
            try
            {
                // These are scenery comparisons: temporarily exclude the rig's placeholder body.
                foreach (var root in scene.GetRootGameObjects())
                    if (root.GetComponentInChildren<CombatLoopWiring>(true) != null)
                        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                            if (renderer.enabled) { hiddenRigRenderers.Add(renderer); renderer.enabled = false; }
                captureObject = new GameObject("~CodexWorldCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
                camera = captureObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1200f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = driver.Palette.PaperColor;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                captureObject.transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, yaw, 0f));
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = true;

                renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "CodexWorldCaptureRT",
                    antiAliasing = 1,
                    hideFlags = HideFlags.HideAndDontSave
                };
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false)
                {
                    name = "CodexWorldCapturePixels",
                    hideFlags = HideFlags.HideAndDontSave
                };
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string directory = Path.Combine(projectRoot, CaptureFolder);
                Directory.CreateDirectory(directory);
                string fullPath = Path.GetFullPath(Path.Combine(directory, "cut" + cut + "_" + CutNames[cut - 1] + ".png"));
                File.WriteAllBytes(fullPath, pixels.EncodeToPNG());
                return fullPath;
            }
            catch (Exception exception)
            {
                return "FAIL: cut " + cut + ": " + exception;
            }
            finally
            {
                // Preserve editor/other-camera state even when rendering or PNG writing throws.
                RenderTexture.active = previousActive;
                if (camera != null) camera.targetTexture = null;
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    Object.DestroyImmediate(renderTexture);
                }
                if (captureObject != null) Object.DestroyImmediate(captureObject);
                foreach (var renderer in hiddenRigRenderers) if (renderer != null) renderer.enabled = true;
            }
        }

        public static string CaptureAll()
        {
            var output = new StringBuilder("Codex world: 1920x1080, FOV60, post=true, AA=None, eye height=1.8m\n");
            for (int cut = 1; cut <= 5; cut++) output.AppendLine("Cut " + cut + ": " + CaptureCut(cut));
            return output.ToString();
        }

        /// <summary>Read-only integrity checks and analytic-ground versus actual-collider measurements.</summary>
        public static string Validate()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != CodexWorldSceneBuilder.ScenePath)
                return "FAIL: active scene is " + scene.path + "; expected " + CodexWorldSceneBuilder.ScenePath;

            int objects = 0, renderers = 0, colliders = 0, activeColliders = 0;
            int cameras = 0, mainCameras = 0, listeners = 0, wirings = 0;
            int missingScripts = 0, missingMaterials = 0, missingMeshes = 0;
            long triangles = 0;
            var problems = new List<string>();
            var meshes = new HashSet<Mesh>();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var go = transform.gameObject;
                objects++;
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (missing > 0)
                {
                    missingScripts += missing;
                    problems.Add("Missing script (" + missing + "): " + HierarchyPath(transform));
                }
                var camera = go.GetComponent<Camera>();
                if (camera != null)
                {
                    cameras++;
                    if (camera.CompareTag("MainCamera")) mainCameras++;
                }
                listeners += go.GetComponents<AudioListener>().Length;
                wirings += go.GetComponents<CombatLoopWiring>().Length;
                renderers += go.GetComponents<Renderer>().Length;
                foreach (var collider in go.GetComponents<Collider>())
                {
                    colliders++;
                    if (collider.enabled && go.activeInHierarchy) activeColliders++;
                }
                foreach (var renderer in go.GetComponents<MeshRenderer>())
                {
                    Material[] materials = renderer.sharedMaterials;
                    if (materials.Length == 0)
                    {
                        missingMaterials++;
                        problems.Add("No material slots: " + HierarchyPath(transform));
                    }
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] == null || materials[i].shader == null)
                        {
                            missingMaterials++;
                            problems.Add("Missing material/shader slot " + i + ": " + HierarchyPath(transform));
                        }
                    var filter = go.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                    {
                        missingMeshes++;
                        problems.Add("Missing MeshFilter/mesh: " + HierarchyPath(transform));
                    }
                }
                foreach (var filter in go.GetComponents<MeshFilter>())
                    if (filter.sharedMesh != null)
                    {
                        triangles += TriangleCount(filter.sharedMesh);
                        meshes.Add(filter.sharedMesh);
                    }
                foreach (var skinned in go.GetComponents<SkinnedMeshRenderer>())
                    if (skinned.sharedMesh != null)
                    {
                        triangles += TriangleCount(skinned.sharedMesh);
                        meshes.Add(skinned.sharedMesh);
                    }
            }

            if (cameras != 1 || mainCameras != 1) problems.Add("Expected one Camera tagged MainCamera; got " + cameras + "/" + mainCameras + ".");
            if (listeners != 1) problems.Add("Expected one AudioListener; got " + listeners + ".");
            if (wirings != 1) problems.Add("Expected one CombatLoopWiring; got " + wirings + ".");

            Physics.SyncTransforms();
            int samples = 0, misses = 0, errors = 0;
            float maxError = 0f;
            for (int z = 0; z <= 220; z += 2)
            {
                samples++;
                float x = CodexWorldGeometry.PathX(z);
                float expected = CodexWorldGeometry.Height(x, z);
                var hits = Physics.RaycastAll(new Vector3(x, 1000f, z), Vector3.down, 2000f, ~0, QueryTriggerInteraction.Ignore);
                bool found = false;
                float bestError = float.PositiveInfinity;
                foreach (var hit in hits)
                {
                    if (hit.collider.gameObject.scene != scene || hit.collider.name != "Valley") continue;
                    found = true;
                    bestError = Mathf.Min(bestError, Mathf.Abs(hit.point.y - expected));
                }
                if (!found)
                {
                    misses++;
                    problems.Add("Valley collision miss at path z=" + z);
                }
                else
                {
                    maxError = Mathf.Max(maxError, bestError);
                    if (bestError > GroundTolerance)
                    {
                        errors++;
                        problems.Add("Valley height error at z=" + z + ": " + Number(bestError) + "m");
                    }
                }
            }

            var output = new StringBuilder();
            output.AppendLine((problems.Count == 0 ? "PASS" : "FAIL") + ": Codex world scene integrity and ground collision audit");
            output.AppendLine("Scene: " + scene.path + "; saved=" + !scene.isDirty + "; playing=" + Application.isPlaying);
            output.AppendLine("Objects=" + objects + "; renderers=" + renderers + "; colliders=" + colliders + " (active=" + activeColliders + ")");
            output.AppendLine("Cameras=" + cameras + "; MainCamera=" + mainCameras + "; AudioListener=" + listeners + "; CombatLoopWiring=" + wirings);
            output.AppendLine("Missing scripts=" + missingScripts + "; material/shader slots=" + missingMaterials + "; MeshFilter/meshes=" + missingMeshes);
            output.AppendLine("Instanced mesh triangles=" + triangles + "; unique meshes=" + meshes.Count + " (editor geometry counts, not measured build performance)");
            output.AppendLine("Ground: " + samples + " centreline samples, z=0..220 every 2m; misses=" + misses + "; outside tolerance=" + errors
                + "; max error=" + Number(maxError) + "m; tolerance=" + Number(GroundTolerance) + "m");
            for (int i = 0; i < Mathf.Min(problems.Count, 30); i++) output.AppendLine("- " + problems[i]);
            if (problems.Count > 30) output.AppendLine("... " + (problems.Count - 30) + " more issues.");
            output.AppendLine("This does not validate combat, runtime exceptions, art acceptance, or frame-time performance.");
            return output.ToString();
        }

        private static long TriangleCount(Mesh mesh)
        {
            long count = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                if (mesh.GetTopology(submesh) == MeshTopology.Triangles) count += mesh.GetIndexCount(submesh) / 3;
            return count;
        }

        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static string Number(float value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
    }
}
