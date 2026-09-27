using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactFolklore298
    {
        [Serializable] public sealed class PoseReceipt298
        {
            public string id, prefab, capturedUtc;
            public int clips, samples, vertices, bones, invalidVertices, escapedBounds;
            public int cullingBoundsOutsideVertices, cullingBoundsOutsideSamples;
            public float largestRelativeExtent;
            public Bounds physicalRestBounds;
            public List<string> images = new List<string>();
            public List<string> observations = new List<string>();
            public List<ContactPose298> contactPoses = new List<ContactPose298>();
        }

        [Serializable] public sealed class ContactPose298
        {
            public string clip, role;
            public float phase;
            public Bounds physicalBounds;
            public Vector3 leftHand, rightHand, head, leftFoot, rightFoot;
            public bool human;
        }

        // Isolated render and deformation evidence. This does not certify combat,
        // locomotion contacts, art direction, or a human's visual approval.
        public static string Review(string argument)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode required");
            string id = string.IsNullOrWhiteSpace(argument) ? "all" : argument;
            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] {AssetRoot + "/Prefabs"})
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x).ToArray();
            var reports = new List<string>();
            foreach (var path in prefabPaths)
            {
                string actor = Path.GetFileNameWithoutExtension(path).Replace("PF_", "");
                if (id != "all" && actor != id) continue;
                reports.Add(ReviewPrefab298(actor, path));
            }
            if (reports.Count == 0) throw new Exception("No imported prefab matched " + id);
            return string.Join("\n", reports);
        }

        static string ReviewPrefab298(string id, string path)
        {
            var receipt = new PoseReceipt298 {id = id, prefab = path, capturedUtc = DateTime.UtcNow.ToString("O")};
            string output = Path.Combine(OutputRoot, "Captures", id); Directory.CreateDirectory(output);
            var preview = new PreviewRenderUtility(false, true);
            try
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                instance.name = id + "_PoseReview"; instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = false;
                preview.AddSingleGO(instance);
                var animator = instance.GetComponentInChildren<Animator>(true);
                if (animator != null) { animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
                var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                receipt.vertices = skins.Where(s => s.sharedMesh != null).Sum(s => s.sharedMesh.vertexCount);
                receipt.bones = skins.SelectMany(s => s.bones).Where(b => b != null).Distinct().Count();
                var transforms = instance.GetComponentsInChildren<Transform>(true);
                var positions = transforms.Select(t => t.localPosition).ToArray();
                var rotations = transforms.Select(t => t.localRotation).ToArray();
                var scales = transforms.Select(t => t.localScale).ToArray();
                Action restore = () => { for (int i = 0; i < transforms.Length; i++) { transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i]; } };
                var rest = MeshBounds298(instance);
                receipt.physicalRestBounds = rest;
                float span = rest.size.magnitude;
                if (!float.IsFinite(span) || span < .01f) throw new Exception("Invalid rest geometry " + id);
                var clips = AssetDatabase.GetDependencies(path, true).Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
                    .Where(c => !c.name.StartsWith("__preview__")).Distinct().OrderBy(c => c.name).ToArray();
                // Animation references can be held by the candidate's actor rather
                // than its visual prefab, so include imported character files too.
                if (clips.Length < 6)
                    clips = AssetDatabase.FindAssets("t:AnimationClip", new[] {AssetRoot}).Select(AssetDatabase.GUIDToAssetPath)
                        .Where(p => p.IndexOf("/" + id + "/", StringComparison.OrdinalIgnoreCase) >= 0)
                        .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
                        .Concat(clips).Where(c => !c.name.StartsWith("__preview__")).Distinct().OrderBy(c => c.name).ToArray();
                receipt.clips = clips.Length;
                foreach (float yaw in new[] {0f, 70f, 180f})
                {
                    string file = Path.Combine(output, "rest-" + (int)yaw + ".png");
                    RenderPose298(preview, instance, rest, yaw, file); receipt.images.Add(Path.GetFileName(file));
                }
                foreach (var clip in clips)
                {
                    var graph = PlayableGraph.Create("Folklore298 pose review"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimationClipPlayable.Create(graph, clip); playable.SetSpeed(0);
                    AnimationPlayableOutput.Create(graph, "Review bones", animator).SetSourcePlayable(playable); graph.Play();
                    try
                    {
                    var spec = ReadManifest().rows.FirstOrDefault(r => r.id == id)?.clips.FirstOrDefault(c => clip.name == c.name || clip.name.EndsWith("|"+c.name));
                    float contact = spec != null ? spec.peak01 : .5f;
                    foreach (float phase in new[] {0f, .25f, .5f, .75f, .999f, contact}.Distinct())
                    {
                        restore(); playable.SetTime(clip.length * phase); graph.Evaluate(0);
                        var posed = MeshBounds298(instance);
                        int outside = 0;
                        foreach (var skin in skins)
                        {
                            var culling = skin.bounds; culling.Expand(.02f);
                            outside += PhysicalSkinVertices298(skin).Count(p => !culling.Contains(p));
                        }
                        receipt.cullingBoundsOutsideVertices += outside;
                        if (outside > 0) receipt.cullingBoundsOutsideSamples++;
                        receipt.samples++;
                        float ratio = posed.size.magnitude / span;
                        if (!float.IsFinite(ratio)) receipt.invalidVertices++;
                        else receipt.largestRelativeExtent = Mathf.Max(receipt.largestRelativeExtent, ratio);
                        if (phase == contact && spec != null)
                        {
                            var pose = new ContactPose298 { clip = clip.name, role = spec.role, phase = phase, physicalBounds = posed, human = animator != null && animator.isHuman };
                            if (pose.human)
                            {
                                Vector3 Joint(HumanBodyBones bone) { var joint = animator.GetBoneTransform(bone); return joint != null ? instance.transform.InverseTransformPoint(joint.position) : Vector3.zero; }
                                pose.leftHand = Joint(HumanBodyBones.LeftHand); pose.rightHand = Joint(HumanBodyBones.RightHand);
                                pose.head = Joint(HumanBodyBones.Head); pose.leftFoot = Joint(HumanBodyBones.LeftFoot); pose.rightFoot = Joint(HumanBodyBones.RightFoot);
                            }
                            receipt.contactPoses.Add(pose);
                        }
                        if (ratio > 3f || Vector3.Distance(posed.center, rest.center) > span * 2) receipt.escapedBounds++;
                        if (phase == .5f || phase == contact || (phase == .999f && clip.name.IndexOf("dead", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            string safe = new string(clip.name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
                            string file = Path.Combine(output, safe + "-" + Mathf.RoundToInt(phase * 100) + ".png");
                            // Same camera for all clip samples; do not hide drifting
                            // roots or unexpected scale changes with automatic framing.
                            RenderPose298(preview, instance, rest, 25, file); receipt.images.Add(Path.GetFileName(file));
                        }
                    }
                    }
                    finally { graph.Destroy(); }
                }
                restore();
                receipt.observations.Add("Unity manual PlayableGraph with root motion disabled, CPU-baked pose views, five samples per clip and manifest contact phases. Static snapshots avoid same-frame GPU skin-cache reuse. Joint positions are prefab-local, before live IK. No play-input or foot-contact approval is implied.");
                if (clips.Length < 6) receipt.observations.Add("Fewer than six imported clips found; inspect manifest and clip bindings.");
                File.WriteAllText(Path.Combine(output, "pose-review.json"), JsonUtility.ToJson(receipt, true));
                return id + ": " + receipt.samples + " samples, " + receipt.vertices + " vertices, invalid=" + receipt.invalidVertices + ", bounds escapes=" + receipt.escapedBounds;
            }
            finally { preview.Cleanup(); }
        }

        static Bounds MeshBounds298(GameObject root)
        {
            bool has = false; var bounds = new Bounds();
            Action<Vector3> add = point => {
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) throw new Exception("Nonfinite deformed vertex");
                if (!has) { bounds = new Bounds(point, Vector3.zero); has = true; } else bounds.Encapsulate(point);
            };
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.enabled || skin.sharedMesh == null) continue;
                foreach (var vertex in PhysicalSkinVertices298(skin)) add(vertex);
            }
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && filter.sharedMesh.isReadable)
                    foreach (var vertex in filter.sharedMesh.vertices) add(filter.transform.TransformPoint(vertex));
            if (!has) throw new Exception("No readable geometry in preview");
            return bounds;
        }

        static void RenderPose298(PreviewRenderUtility preview, GameObject instance, Bounds frame, float yaw, string path)
        {
            const int width = 1024, height = 1024;
            var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.enabled).ToArray();
            var snapshots = new List<GameObject>(); var meshes = new List<Mesh>();
            foreach (var skin in skins)
            {
                var mesh = BakePhysicalMesh298(skin); meshes.Add(mesh);
                var snapshot = new GameObject("BakedPose298"); snapshot.transform.SetParent(skin.transform, false);
                snapshot.AddComponent<MeshFilter>().sharedMesh = mesh;
                snapshot.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                snapshots.Add(snapshot); skin.enabled = false;
            }
            float radius = frame.extents.magnitude;
            var camera = preview.camera;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.40f,.43f,.44f,1);
            camera.allowHDR = false; camera.allowMSAA = true; camera.cullingMask = 1;
            camera.orthographic = true; camera.orthographicSize = radius * 1.15f;
            Vector3 offset = Quaternion.Euler(0, yaw, 0) * new Vector3(0,.15f,1).normalized;
            camera.transform.position = frame.center + offset * Mathf.Max(4, radius * 5);
            camera.transform.LookAt(frame.center); camera.nearClipPlane = .01f; camera.farClipPlane = Mathf.Max(20, radius * 12);
            preview.ambientColor = new Color(.42f,.42f,.42f,1);
            preview.lights[0].intensity = 2.4f; preview.lights[0].transform.rotation = Quaternion.Euler(35,160,0);
            preview.lights[1].intensity = 1.2f; preview.lights[1].transform.rotation = Quaternion.Euler(320,-30,0);
            preview.BeginPreview(new Rect(0,0,width,height), GUIStyle.none); preview.Render(true,false);
            var target = preview.EndPreview() as RenderTexture;
            if (target == null) throw new Exception("Preview render target missing");
            var previous = RenderTexture.active; Texture2D texture = null;
            try
            {
                RenderTexture.active = target; texture = new Texture2D(width,height,TextureFormat.RGB24,false,false);
                texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply(false,false);
                // PreviewRenderUtility uses a linear UNorm target; PNG is sRGB.
                var pixels = texture.GetPixels(); for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
                texture.SetPixels(pixels); texture.Apply(false,false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; if (texture != null) Object.DestroyImmediate(texture);
                foreach (var snapshot in snapshots) Object.DestroyImmediate(snapshot);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
                foreach (var skin in skins) skin.enabled = true;
            }
        }
    }
}
