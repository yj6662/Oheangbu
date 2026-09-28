using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Disposable form experiment, never a replacement for the authored game terrain.
    public static class InkPaintingFormStudy
    {
        const int Width = 1920, Height = 1080, Segments = 160;
        static Pending pending;
        public static string LastResult { get; private set; } = "Not captured";

        sealed class Pending
        {
            public Scene Scene;
            public Camera Camera;
            public Mesh Mesh;
            public Material Material;
            public string Path;
        }

        // A renderer created during the command needs an Editor update before its first draw.
        // Capture therefore queues the render; file creation / LastResult is completion evidence.
        public static string Capture(string output, Material mountainMat)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Form study requires Edit mode");
            if (pending != null) throw new InvalidOperationException("A form study capture is already pending");
            if (mountainMat == null || mountainMat.shader == null ||
                mountainMat.shader.name != "Oheangbu/Study/InkPaintingGround")
                throw new ArgumentException("Pass the current InkPaintingGround study material", nameof(mountainMat));
            CheckMemory();
            string directory = Path.GetFullPath(output);
            Directory.CreateDirectory(directory);
            var work = new Pending { Path = Path.Combine(directory, "FormStudy.png") };
            try
            {
                work.Scene = EditorSceneManager.NewPreviewScene();
                work.Material = new Material(mountainMat)
                {
                    name = "Isolated rounded-mountain material",
                    hideFlags = HideFlags.HideAndDontSave
                };
                // Only the disposable study clone receives these changes. Mountain pigment,
                // atlas, ink loading and atmosphere are copied from the supplied material.
                SetFloat(work.Material, "_GroundPath", 0);
                SetFloat(work.Material, "_GroundKind", 0);
                SetFloat(work.Material, "_RealmTintStrength", 0);
                SetFloat(work.Material, "_EdgeFade", 0);
                SetFloat(work.Material, "_SourceUV", 0);
                work.Material.SetVector("_CITones", new Vector4(.55f, .72f, .55f, .72f));
                work.Mesh = BuildHeightfield();
                var surface = Make(work.Scene, "Rounded mountain form study");
                surface.AddComponent<MeshFilter>().sharedMesh = work.Mesh;
                var renderer = surface.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = work.Material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                var light = Make(work.Scene, "Form study directional light").AddComponent<Light>();
                var sourceLight = RenderSettings.sun;
                light.type = LightType.Directional;
                light.color = sourceLight != null ? sourceLight.color : Color.white;
                light.intensity = sourceLight != null ? sourceLight.intensity : 1f;
                light.transform.rotation = sourceLight != null ? sourceLight.transform.rotation : Quaternion.Euler(42, -30, 0);
                light.shadows = LightShadows.Soft;
                light.cullingMask = ~0;

                var camera = Make(work.Scene, "Form study camera").AddComponent<Camera>();
                work.Camera = camera;
                camera.enabled = false;
                camera.cameraType = CameraType.Preview; // Excludes the game's procedural-dressing callbacks.
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.77f, .75f, .70f, 1);
                camera.transform.SetPositionAndRotation(new Vector3(0, 220, -850),
                    Quaternion.LookRotation(new Vector3(0, 250, 400) - new Vector3(0, 220, -850)));
                camera.fieldOfView = 42;
                camera.aspect = Width / (float)Height;
                camera.nearClipPlane = .3f;
                camera.farClipPlane = 4000;
                camera.cullingMask = ~0;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(work.Scene);
                if (camera.overrideSceneCullingMask == 0 || renderer.gameObject.scene != work.Scene)
                    throw new InvalidOperationException("Preview scene rendering mask is unavailable");
                camera.useOcclusionCulling = false;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.allowDynamicResolution = false;
                var additional = camera.GetUniversalAdditionalCameraData();
                additional.renderType = CameraRenderType.Base;
                additional.renderPostProcessing = false;
                additional.renderShadows = true;
                additional.volumeLayerMask = 0;
                additional.antialiasing = AntialiasingMode.None;

                pending = work;
                AssemblyReloadEvents.beforeAssemblyReload += Cancel;
                EditorApplication.quitting += Cancel;
                EditorApplication.update += ArmRender;
                EditorApplication.QueuePlayerLoopUpdate();
                LastResult = "PENDING next Editor update: " + work.Path +
                    "; isolated mountain-form experiment, NOT the existing world; bright valley clone only";
                return LastResult;
            }
            catch
            {
                Unsubscribe();
                pending = null;
                Dispose(work);
                throw;
            }
        }

        static void ArmRender()
        {
            // Use Editor updates rather than repaint-dependent delayCall (the Editor may be hidden). A second
            // deferred callback guarantees a complete Editor update for registration.
            EditorApplication.update -= ArmRender;
            if (pending == null) return;
            EditorApplication.update += RenderPrepared;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        static GameObject Make(Scene scene, string name)
        {
            var gameObject = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            gameObject.layer = 0;
            return gameObject;
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        static void RenderPrepared()
        {
            var work = pending;
            if (work == null) return;
            RenderTexture target = null;
            Texture2D pixels = null;
            var previousTarget = RenderTexture.active;
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            var ambient = new AmbientSnapshot();
            try
            {
                CheckMemory();
                ShaderUtil.allowAsyncCompilation = false;
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
                {
                    name = "FormStudy 1080p", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1
                };
                if (!target.Create()) throw new InvalidOperationException("Could not create form study render target");
                work.Camera.targetTexture = target;
                for (int i = 0; i < 6; i++)
                {
                    CheckMemory();
                    if (GraphicsSettings.currentRenderPipeline != null)
                        RenderPipeline.SubmitRenderRequest(work.Camera, new RenderPipeline.StandardRequest { destination = target });
                    else work.Camera.Render();
                }
                RenderTexture.active = target;
                pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                { name = "FormStudy readback", hideFlags = HideFlags.HideAndDontSave };
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                pixels.Apply();
                // Reject the earlier failure mode: a valid PNG containing only a clear colour.
                var samples = pixels.GetPixels32();
                int low = 765, high = 0, dark = 0;
                for (int i = 0; i < samples.Length; i += 7)
                {
                    var c = samples[i]; int value = c.r + c.g + c.b;
                    low = Mathf.Min(low, value); high = Mathf.Max(high, value);
                    if (value < 450) dark++;
                }
                if (high - low < 60 || dark < 100)
                    throw new InvalidOperationException("Form study is blank or has no visible dark mountain; capture rejected");
                File.WriteAllBytes(work.Path, pixels.EncodeToPNG());
                LastResult = "CAPTURED " + work.Path +
                    "; 분리된 산형태 실험, 기존 월드 아님; 160x160 quads, six curved ridge layers; valley clone tones .55-.72";
                Debug.Log(LastResult);
            }
            catch (Exception error)
            {
                LastResult = "FAILED isolated form study: " + error.Message;
                Debug.LogException(error);
            }
            finally
            {
                if (work.Camera != null) work.Camera.targetTexture = null;
                RenderTexture.active = previousTarget;
                ShaderUtil.allowAsyncCompilation = previousAsync;
                ambient.RestoreIfChanged();
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (pixels != null) Object.DestroyImmediate(pixels);
                Unsubscribe();
                pending = null;
                Dispose(work);
            }
        }

        static void Cancel()
        {
            var work = pending;
            pending = null;
            Unsubscribe();
            if (work != null)
            {
                LastResult = "CANCELLED before form study render; preview resources released";
                Dispose(work);
            }
        }

        static void Unsubscribe()
        {
            EditorApplication.update -= ArmRender;
            EditorApplication.update -= RenderPrepared;
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            EditorApplication.quitting -= Cancel;
        }

        static void Dispose(Pending work)
        {
            if (work == null) return;
            try { if (work.Scene.IsValid()) EditorSceneManager.ClosePreviewScene(work.Scene); }
            finally
            {
                if (work.Mesh != null) Object.DestroyImmediate(work.Mesh);
                if (work.Material != null) Object.DestroyImmediate(work.Material);
            }
        }

        static void CheckMemory()
        {
            if (Prologue.PrologueAudit.CommitRatio() >= .85f)
                throw new InvalidOperationException("System commit >=85%; form study stopped");
        }

        sealed class AmbientSnapshot
        {
            readonly AmbientMode mode = RenderSettings.ambientMode;
            readonly float intensity = RenderSettings.ambientIntensity;
            readonly Color light = RenderSettings.ambientLight, sky = RenderSettings.ambientSkyColor,
                equator = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor;
            // The study never assigns ambient settings. Conditional restoration avoids
            // unnecessary writes to the active scene while protecting against render callbacks.
            public void RestoreIfChanged()
            {
                if (RenderSettings.ambientMode != mode) RenderSettings.ambientMode = mode;
                if (RenderSettings.ambientIntensity != intensity) RenderSettings.ambientIntensity = intensity;
                if (RenderSettings.ambientLight != light) RenderSettings.ambientLight = light;
                if (RenderSettings.ambientSkyColor != sky) RenderSettings.ambientSkyColor = sky;
                if (RenderSettings.ambientEquatorColor != equator) RenderSettings.ambientEquatorColor = equator;
                if (RenderSettings.ambientGroundColor != ground) RenderSettings.ambientGroundColor = ground;
            }
        }

        static Mesh BuildHeightfield()
        {
            int stride = Segments + 1;
            var vertices = new Vector3[stride * stride];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var colours = new Color32[vertices.Length];
            var triangles = new int[Segments * Segments * 6];
            for (int z = 0; z <= Segments; z++) for (int x = 0; x <= Segments; x++)
            {
                int i = z * stride + x;
                float px = -750 + 1500f * x / Segments, pz = -100 + 1400f * z / Segments;
                vertices[i] = new Vector3(px, Elevation(px, pz), pz);
                const float epsilon = 2f;
                float dx = (Elevation(px + epsilon, pz) - Elevation(px - epsilon, pz)) / (2 * epsilon);
                float dz = (Elevation(px, pz + epsilon) - Elevation(px, pz - epsilon)) / (2 * epsilon);
                normals[i] = new Vector3(-dx, 1, -dz).normalized;
                uv[i] = new Vector2(x / (float)Segments, z / (float)Segments);
                colours[i] = new Color32(255, 255, 255, 255);
                if (x == Segments || z == Segments) continue;
                int t = (z * Segments + x) * 6;
                triangles[t] = i; triangles[t + 1] = i + stride; triangles[t + 2] = i + 1;
                triangles[t + 3] = i + 1; triangles[t + 4] = i + stride; triangles[t + 5] = i + stride + 1;
            }
            var mesh = new Mesh { name = "Six curved ink ridges, isolated 1500x1400m heightfield", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.colors32 = colours; mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        static float Elevation(float x, float z)
        {
            // Smooth overlapping fields form one surface. No cone peaks or primitive seams.
            float h = Ridge(x, z, -205, 930, 335, 170, 710, 82, -.18f, .35f);
            h = SmoothMax(h, Ridge(x, z, 350, 1030, 280, 155, 520, 70, .12f, 1.7f), 42);
            h = SmoothMax(h, Ridge(x, z, -430, 635, 270, 110, 405, 90, -.24f, 2.1f), 35);
            h = SmoothMax(h, Ridge(x, z, 140, 570, 420, 100, 370, 145, -.12f, -.7f), 32);
            h = SmoothMax(h, Ridge(x, z, 400, 255, 245, 92, 235, 70, .18f, .65f), 28);
            h = SmoothMax(h, Ridge(x, z, -535, 125, 225, 105, 185, 58, -.14f, 1.4f), 25);
            float valleyCentre = -70 + 135 * Mathf.Sin((z + 85) * .0045f);
            float valley = Gaussian((x - valleyCentre) / 175) * Gaussian((z - 230) / 370);
            h *= 1 - .76f * valley;
            float sides = 1 - Smooth(650, 750, Mathf.Abs(x));
            float ends = Smooth(-100, -15, z) * (1 - Smooth(1150, 1300, z));
            return -12 + h * sides * ends;
        }

        static float Ridge(float x, float z, float cx, float cz, float length, float width, float height,
            float bend, float sweep, float phase)
        {
            float along = (x - cx) / length;
            float centreZ = cz + bend * Mathf.Sin(along * 1.8f + phase) + (x - cx) * sweep;
            float cross = z - centreZ;
            float asymmetricWidth = width * Mathf.Lerp(.74f, 1.25f, Smooth(-width, width, cross));
            float shoulder = 1 + .11f * Mathf.Sin(along * 2.7f + phase);
            return height * shoulder * Gaussian(along) * Gaussian(cross / asymmetricWidth);
        }

        static float Gaussian(float x) => Mathf.Exp(-.5f * x * x);
        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));
        static float SmoothMax(float a, float b, float width)
        {
            float overlap = Mathf.Max(0, width - Mathf.Abs(a - b));
            return Mathf.Max(a, b) + overlap * overlap / (4 * width);
        }
    }
}
