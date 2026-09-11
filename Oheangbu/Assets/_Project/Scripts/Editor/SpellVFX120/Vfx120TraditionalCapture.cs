using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // Inspect the actual vendor prefab in the current saved camera. No source assets
    // or scene objects are edited; all instantiated content is scoped to this job.
    public static class Vfx120TraditionalCapture
    {
        [Serializable] public class Request
        {
            public string[] paths;
            public string[] children;
            public float[] heights;
            public Vector3[] rotations;
            public float[] ages = { .12f, .35f, .75f, 1.3f, 2.2f };
            public string folder = "KTPNativeReview";
            public float scale = .65f;
            public int width = 1280, height = 720;
        }
        [Serializable] class Evidence
        {
            public string source, child, scene, camera, capturedUtc, status;
            public Vector3 position, cameraPosition, cameraEuler;
            public float scale, cameraFov;
            public int systems, renderers, aliveParticles, unsupportedShaders, animatorCount, monoBehaviourCount;
            public float[] sampledSeconds;
            public string[] shaders;
        }
        static Request job;
        static int entry, sample;
        static GameObject instance;
        static ParticleSystem[] systems, roots;
        static Camera camera;
        static RenderTexture rt;
        static Texture2D pixels;
        static string folder, status = "IDLE", error;

        public static string Start(string json)
        {
            if (job != null) throw new InvalidOperationException("Traditional capture is running");
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit scene required");
            var next = JsonUtility.FromJson<Request>(json);
            if (next == null || next.paths == null || next.paths.Length == 0) throw new ArgumentException("Prefab paths required");
            if (next.ages == null || next.ages.Length == 0 || next.ages.Any(t => t < 0 || float.IsNaN(t))) throw new ArgumentException("Positive sample times required");
            foreach (string path in next.paths)
                if (!path.StartsWith("Assets/KoreanTraditionalPattern_Effect/Prefabs/") || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    throw new ArgumentException("Unavailable original KTP prefab: " + path);
            camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Current scene camera missing");
            job = next; entry = sample = 0; error = ""; status = "RUNNING";
            folder = Path.Combine(Vfx120Editor.Output, job.folder); Directory.CreateDirectory(folder);
            rt = new RenderTexture(job.width, job.height, 24, RenderTextureFormat.ARGB32); rt.Create();
            pixels = new Texture2D(job.width, job.height, TextureFormat.RGB24, false);
            EditorApplication.update += Tick;
            return Poll();
        }

        public static string Poll() => JsonUtility.ToJson(new Progress { status = status, error = error, entry = entry, sample = sample, total = job != null ? job.paths.Length : entry });
        [Serializable] class Progress { public string status, error; public int entry, sample, total; }

        static void Tick()
        {
            if (job == null || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (entry >= job.paths.Length) { Finish(null); return; }
                if (instance == null)
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(job.paths[entry]);
                    string child = job.children != null && entry < job.children.Length ? job.children[entry] : "";
                    if (!string.IsNullOrEmpty(child))
                    {
                        var subtree = source.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == child);
                        if (subtree == null) throw new ArgumentException("Original prefab child missing: " + child);
                        source = subtree.gameObject;
                    }
                    instance = UnityEngine.Object.Instantiate(source);
                    instance.hideFlags = HideFlags.HideAndDontSave;
                    float ground = 0;
                    var hits = Physics.RaycastAll(new Vector3(0, 5, 4), Vector3.down, 12, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    var surfaces = hits.Where(h => h.normal.y > .45f && !(h.collider is CharacterController)).OrderBy(h => h.distance).ToArray();
                    if (surfaces.Length > 0) ground = surfaces[0].point.y;
                    float height = job.heights != null && entry < job.heights.Length ? job.heights[entry] : .03f;
                    instance.transform.position = new Vector3(0, ground + height, 4);
                    if (job.rotations != null && entry < job.rotations.Length)
                        instance.transform.rotation = Quaternion.Euler(job.rotations[entry]) * instance.transform.rotation;
                    instance.transform.localScale *= job.scale;
                    instance.SetActive(true);
                    systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                    foreach (var ps in systems)
                    {
                        var main = ps.main; main.stopAction = ParticleSystemStopAction.None;
                        ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(8321 + Array.IndexOf(systems, ps) * 97);
                        ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    roots = systems.Where(ps => !systems.Any(other => other != ps && ps.transform.IsChildOf(other.transform))).ToArray();
                }
                foreach (var ps in roots) ps.Simulate(job.ages[sample], true, true, true);
                var previous = camera.targetTexture; var active = RenderTexture.active;
                try
                {
                    camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                    pixels.ReadPixels(new Rect(0, 0, job.width, job.height), 0, 0); pixels.Apply();
                }
                finally { camera.targetTexture = previous; RenderTexture.active = active; }
                string name = Path.GetFileNameWithoutExtension(job.paths[entry]);
                string subtreeName = job.children != null && entry < job.children.Length ? job.children[entry] : "";
                if (!string.IsNullOrEmpty(subtreeName)) name += "_" + subtreeName;
                File.WriteAllBytes(Path.Combine(folder, name + "_" + sample + ".png"), pixels.EncodeToPNG());
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                var mats = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).ToArray();
                var ev = new Evidence {
                    source = job.paths[entry], child = subtreeName, scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                    camera = camera.name, capturedUtc = DateTime.UtcNow.ToString("o"),
                    status = "ORIGINAL_PREFAB_EDIT_SIMULATION_ONLY", position = instance.transform.position, scale = job.scale,
                    cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles, cameraFov = camera.fieldOfView,
                    systems = systems.Length, renderers = renderers.Length, aliveParticles = systems.Sum(p => p.particleCount),
                    unsupportedShaders = mats.Count(m => m.shader == null || !m.shader.isSupported),
                    animatorCount = instance.GetComponentsInChildren<Animator>(true).Length,
                    monoBehaviourCount = instance.GetComponentsInChildren<MonoBehaviour>(true).Length,
                    sampledSeconds = job.ages, shaders = mats.Select(m => m.shader != null ? m.shader.name : "MISSING").Distinct().ToArray()
                };
                File.WriteAllText(Path.Combine(folder, name + "_" + sample + ".json"), JsonUtility.ToJson(ev, true));
                sample++;
                if (sample >= job.ages.Length)
                {
                    UnityEngine.Object.DestroyImmediate(instance); instance = null; entry++; sample = 0;
                }
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }
        static void Finish(string failure)
        {
            EditorApplication.update -= Tick;
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            instance = null; pixels = null; rt = null; job = null;
            status = failure == null ? "COMPLETE" : "FAILED"; error = failure ?? "";
        }
    }
}
