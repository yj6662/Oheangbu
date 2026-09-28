using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactFolklore298
    {
        [Serializable] public sealed class FilmFrame298
        {
            public int index, sample;
            public string role, clip, path, sha256;
            public float phase, clipTime, holdSeconds;
            public Bounds physicalBounds;
        }
        [Serializable] public sealed class FilmClip298
        {
            public string role, name, asset, assetSha256;
            public float seconds, peak01, timelineStart;
            public bool loop;
            public int firstFrame, frameCount;
        }
        [Serializable] public sealed class FilmSourceMesh298
        {
            public string name, asset, beforeSha256, afterSha256;
            public int vertices;
        }
        [Serializable] public sealed class FilmReceipt298
        {
            public string id, displayName, prefab, utc, status, scope, manifestSha256, sourceModelSha256;
            public int width = 512, height = 512, samplesPerRole = 0, samplingFps = 24, maximumSamplesPerRole = 300;
            public bool rootMotion = false, restTrsRestoredPerSample, sourceMeshesUnchanged;
            public Bounds frameBounds;
            public Vector3 cameraPosition, cameraEuler, cameraTarget;
            public float orthographicSize, nearClip, farClip, totalSeconds;
            public List<FilmClip298> clips = new List<FilmClip298>();
            public List<FilmFrame298> frames = new List<FilmFrame298>();
            public List<FilmSourceMesh298> sourceMeshes = new List<FilmSourceMesh298>();
        }

        // Isolated studio film only: no actor logic, Follow, IK, root motion or scene save.
        public static string Film(string argument)
        {
            RequireEdit();
            string requested = string.IsNullOrWhiteSpace(argument) ? "all" : argument;
            var rows = ReadManifest().rows.Where(r => requested == "all" || r.id == requested).ToArray();
            if (rows.Length == 0) throw new Exception("No matching Folklore298 model: " + requested);
            var results = new List<string>();
            foreach (var row in rows) results.Add(FilmPrefab298(row));
            return string.Join("\n", results);
        }

        static string FilmPrefab298(ModelRow row)
        {
            string prefabPath = PrefabPath(row.id);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new Exception("Missing prefab " + prefabPath);
            string actorFolder = Path.Combine(OutputRoot, "Captures", row.id);
            string runFolder = Path.Combine(actorFolder, "Film", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
            Directory.CreateDirectory(Path.Combine(runFolder, "frames"));
            var receipt = new FilmReceipt298 {
                id = row.id, displayName = row.displayName, prefab = prefabPath, utc = DateTime.UtcNow.ToString("O"), status = "CAPTURING",
                manifestSha256 = FilmFileHash298(Path.Combine(OutputRoot, "import-manifest.json")), sourceModelSha256 = FilmFileHash298(SourceAbsolute(row.model)),
                scope = "512px studio preview of manifest role clips through manual SamplePoseGraph298, root motion disabled, CPU physical mesh snapshots. All behaviours including procedural body follow and terrain IK are disabled. This is not actual world play, foot contact, attack timing or art acceptance evidence. Samples per clip = ceil(length*24), minimum 2, maximum 300. Uniform samples cover each complete clip; each is held for length/sampleCount seconds. Non-looping clips include their final pose."
            };
            var sourceMeshes = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.sharedMesh)
                .Concat(prefab.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh)).Where(m => m != null).Distinct().ToArray();
            foreach (var mesh in sourceMeshes) receipt.sourceMeshes.Add(new FilmSourceMesh298 {name = mesh.name, asset = AssetDatabase.GetAssetPath(mesh), vertices = mesh.vertexCount, beforeSha256 = FilmMeshHash298(mesh)});
            var preview = new PreviewRenderUtility(false, true);
            Action restore = null;
            try
            {
                var instance = Object.Instantiate(prefab); instance.name = row.id + "_StudioFilm298";
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = false;
                preview.AddSingleGO(instance);
                var animator = instance.GetComponentInChildren<Animator>(true) ?? throw new Exception("No Animator " + row.id);
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.fireEvents = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var transforms = instance.GetComponentsInChildren<Transform>(true);
                var positions = transforms.Select(t => t.localPosition).ToArray();
                var rotations = transforms.Select(t => t.localRotation).ToArray();
                var scales = transforms.Select(t => t.localScale).ToArray();
                restore = () => { for (int i = 0; i < transforms.Length; i++) { transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i]; } };
                var frameBounds = MeshBounds298(instance);
                var roles = new[] {"idle", "walk", "attack", "hit", "stun", "death"};
                var resolved = new List<AnimationClip>();
                var assetHashes = new Dictionary<string, string>();
                // Measure every captured pose first. The camera remains unchanged through all roles.
                foreach (string role in roles)
                {
                    var clip = Clip(row, role); resolved.Add(clip);
                    var spec = row.clips.Single(c => c.role == role);
                    string asset = AssetDatabase.GetAssetPath(clip);
                    if (!assetHashes.ContainsKey(asset)) assetHashes[asset] = FilmFileHash298(Path.GetFullPath(asset));
                    bool loop = spec.loop || role == "idle" || role == "walk";
                    int sampleCount = Mathf.Clamp(Mathf.CeilToInt(clip.length * receipt.samplingFps), 2, receipt.maximumSamplesPerRole);
                    receipt.clips.Add(new FilmClip298 {role = role, name = clip.name, asset = asset, assetSha256 = assetHashes[asset], seconds = clip.length, peak01 = spec.peak01,
                        loop = loop, firstFrame = receipt.frames.Count, frameCount = sampleCount, timelineStart = receipt.totalSeconds});
                    for (int i = 0; i < sampleCount; i++)
                    {
                        float phase = loop ? i / (float)sampleCount : i / (float)(sampleCount - 1);
                        restore(); SamplePoseGraph298(animator, clip, clip.length * phase);
                        var posed = MeshBounds298(instance); frameBounds.Encapsulate(posed);
                        receipt.frames.Add(new FilmFrame298 {index = receipt.frames.Count, sample = i, role = role, clip = clip.name, phase = phase,
                            clipTime = clip.length * phase, holdSeconds = clip.length / sampleCount, physicalBounds = posed});
                    }
                    receipt.totalSeconds += clip.length;
                }
                if (!float.IsFinite(frameBounds.size.magnitude) || frameBounds.size.magnitude < .01f) throw new Exception("Invalid studio envelope");
                receipt.frameBounds = frameBounds; ConfigureFilmCamera298(preview, frameBounds);
                var camera = preview.camera;
                receipt.cameraPosition = camera.transform.position; receipt.cameraEuler = camera.transform.eulerAngles; receipt.cameraTarget = frameBounds.center;
                receipt.orthographicSize = camera.orthographicSize; receipt.nearClip = camera.nearClipPlane; receipt.farClip = camera.farClipPlane;
                foreach (var f in receipt.frames)
                {
                    restore(); SamplePoseGraph298(animator, resolved[Array.IndexOf(roles, f.role)], f.clipTime);
                    var actual = MeshBounds298(instance);
                    if ((actual.min - f.physicalBounds.min).sqrMagnitude > .000001f || (actual.max - f.physicalBounds.max).sqrMagnitude > .000001f) throw new Exception("Repeated pose did not match measured envelope " + row.id + "/" + f.index);
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var p = actual.center + Vector3.Scale(actual.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        var v = camera.WorldToViewportPoint(p);
                        if (v.x < .025f || v.x > .975f || v.y < .025f || v.y > .975f || v.z <= camera.nearClipPlane || v.z >= camera.farClipPlane) throw new Exception("Film envelope would crop a sampled pose " + row.id + "/" + f.index);
                    }
                    string file = Path.Combine(runFolder, "frames", "frame_" + f.index.ToString("D3") + ".png");
                    RenderFilmPose298(preview, instance, file);
                    f.path = Path.GetRelativePath(OutputRoot, file).Replace('\\', '/'); f.sha256 = FilmFileHash298(file);
                }
                restore(); receipt.restTrsRestoredPerSample = true;
                for (int i = 0; i < sourceMeshes.Length; i++)
                {
                    receipt.sourceMeshes[i].afterSha256 = FilmMeshHash298(sourceMeshes[i]);
                    if (receipt.sourceMeshes[i].afterSha256 != receipt.sourceMeshes[i].beforeSha256) throw new Exception("Source mesh changed during studio film");
                }
                if (FilmFileHash298(SourceAbsolute(row.model)) != receipt.sourceModelSha256) throw new Exception("Source model file changed during studio film");
                receipt.sourceMeshesUnchanged = true; receipt.status = "STUDIO_FRAMES_CAPTURED_NOT_WORLD_PLAY_VERIFIED";
                string json = JsonUtility.ToJson(receipt, true);
                File.WriteAllText(Path.Combine(runFolder, "film-receipt.json"), json);
                File.WriteAllText(Path.Combine(actorFolder, "film-receipt.json"), json);
                return row.id + ": " + receipt.frames.Count + " frames at 512px, " + receipt.totalSeconds.ToString("F3") + "s; ready for encode_films298.py";
            }
            catch (Exception e)
            {
                receipt.status = "FAILED: " + e.Message; File.WriteAllText(Path.Combine(runFolder, "film-receipt.json"), JsonUtility.ToJson(receipt, true)); throw;
            }
            finally { restore?.Invoke(); preview.Cleanup(); }
        }

        static void ConfigureFilmCamera298(PreviewRenderUtility preview, Bounds frame)
        {
            float radius = frame.extents.magnitude;
            var camera = preview.camera;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.40f, .43f, .44f, 1);
            camera.allowHDR = false; camera.allowMSAA = true; camera.cullingMask = 1; camera.aspect = 1;
            camera.orthographic = true; camera.orthographicSize = radius * 1.2f;
            Vector3 offset = Quaternion.Euler(0, 25, 0) * new Vector3(0, .15f, 1).normalized;
            camera.transform.position = frame.center + offset * Mathf.Max(4, radius * 5); camera.transform.LookAt(frame.center);
            camera.nearClipPlane = .01f; camera.farClipPlane = Mathf.Max(20, radius * 12);
            preview.ambientColor = new Color(.42f, .42f, .42f, 1);
            preview.lights[0].intensity = 2.4f; preview.lights[0].transform.rotation = Quaternion.Euler(35, 160, 0);
            preview.lights[1].intensity = 1.2f; preview.lights[1].transform.rotation = Quaternion.Euler(320, -30, 0);
        }

        static void RenderFilmPose298(PreviewRenderUtility preview, GameObject instance, string path)
        {
            const int size = 512;
            var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.enabled && s.gameObject.activeInHierarchy).ToArray();
            var snapshots = new List<GameObject>(); var meshes = new List<Mesh>();
            var previous = RenderTexture.active; Texture2D texture = null;
            try
            {
                foreach (var skin in skins)
                {
                    var mesh = BakePhysicalMesh298(skin); meshes.Add(mesh);
                    var snapshot = new GameObject("FilmPhysicalSnapshot298"); snapshots.Add(snapshot);
                    snapshot.transform.SetParent(skin.transform, false); snapshot.layer = 0;
                    snapshot.AddComponent<MeshFilter>().sharedMesh = mesh; snapshot.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials; skin.enabled = false;
                }
                preview.BeginPreview(new Rect(0, 0, size, size), GUIStyle.none); preview.Render(true, false);
                var target = preview.EndPreview() as RenderTexture ?? throw new Exception("Film render target missing");
                RenderTexture.active = target; texture = new Texture2D(size, size, TextureFormat.RGB24, false, false);
                texture.ReadPixels(new Rect(0, 0, size, size), 0, 0); texture.Apply(false, false);
                var pixels = texture.GetPixels(); for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
                texture.SetPixels(pixels); texture.Apply(false, false); File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; if (texture != null) Object.DestroyImmediate(texture);
                foreach (var snapshot in snapshots) Object.DestroyImmediate(snapshot);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
                foreach (var skin in skins) skin.enabled = true;
            }
        }

        static string FilmFileHash298(string path) { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static string FilmMeshHash298(Mesh mesh)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                void V3(Vector3 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                writer.Write(mesh.vertexCount); foreach (var v in mesh.vertices) V3(v); foreach (var n in mesh.normals) V3(n);
                foreach (var t in mesh.tangents) { writer.Write(t.x); writer.Write(t.y); writer.Write(t.z); writer.Write(t.w); }
                foreach (var uv in mesh.uv) { writer.Write(uv.x); writer.Write(uv.y); }
                foreach (var w in mesh.boneWeights) { writer.Write(w.boneIndex0); writer.Write(w.boneIndex1); writer.Write(w.boneIndex2); writer.Write(w.boneIndex3); writer.Write(w.weight0); writer.Write(w.weight1); writer.Write(w.weight2); writer.Write(w.weight3); }
                foreach (var bind in mesh.bindposes) for (int i = 0; i < 16; i++) writer.Write(bind[i]);
                writer.Write(mesh.subMeshCount); for (int i = 0; i < mesh.subMeshCount; i++) { var indices = mesh.GetIndices(i); writer.Write((int)mesh.GetTopology(i)); writer.Write(indices.Length); foreach (int index in indices) writer.Write(index); }
                writer.Flush(); stream.Position = 0; using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
