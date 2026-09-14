using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Data;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Read-only skin-to-handle audit. Candidate angles affect temporary profile clones only.</summary>
    public static class DosaGripContactValidation
    {
        [Serializable] private sealed class Surface
        {
            public string finger;
            public int triangles, intersectingTriangles, distalTriangles;
            public float minimumGapMeters = 100f, distalMinimumGapMeters = 100f;
            public Vector3 closestSkinInGrip;
        }
        [Serializable] private sealed class Sample
        {
            public string name;
            public Vector3 thumbAngles, fingerAngles;
            public Vector2 thumbOpposition;
            public Vector3 indexAngles, middleAngles, ringAngles, pinkyAngles;
            public bool penUp, eligibleForContact;
            public string screenshot;
            public Vector3 index1InGrip, index2InGrip, index3InGrip;
            public List<Surface> surfaces = new List<Surface>();
        }
        [Serializable] private sealed class Report
        {
            public string status = "MEASURED";
            public string note = "Signed minimum gap from a finger-weighted skin triangle to the actual finite cylindrical handle: positive=separation, negative=intersection. This is a geometric contact diagnostic, not an automatic art approval. Distal means average weights of finger joints 2/3 >= .3. Palm uses RightHand weights. No production pose/profile is modified.";
            public float handleRadiusMeters, handleMinimumY, handleMaximumY;
            public List<Sample> samples = new List<Sample>();
        }

        private static readonly string[] Names = { "Thumb", "Index", "Middle", "Ring", "Pinky", "Palm" };

        [Serializable] private sealed class SolverBone
        {
            public string name; public int parent;
            public Vector3 position, scale; public Quaternion rotation;
            public Matrix4x4 matrix, bind;
        }
        [Serializable] private sealed class SolverVertex
        {
            public Vector3 source, baked;
            public int[] bones; public float[] weights;
        }
        [Serializable] private sealed class SolverData
        {
            public SolverBone[] bones; public SolverVertex[] vertices; public int[] triangles;
            public float radius, minimumY, maximumY;
        }

        public static string Run()
        {
            if (!Application.isPlaying) return "NOT_RUN: Play Mode required";
            var report = new Report();
            var random = UnityEngine.Random.state;
            try
            {
                foreach (var existing in Object.FindObjectsByType<PlayerVisualRig>(FindObjectsSortMode.None))
                {
                    Transform arms = existing.transform.Find("NearArms");
                    Transform brush = existing.transform.Find("NearBrush");
                    if (existing.Diagnostics.NearVisible && arms != null && brush != null)
                        report.samples.Add(Measure("live_" + existing.name, arms, brush, report));
                }
                AddCandidate(report, "calibrated_pen_down", false);
                AddCandidate(report, "calibrated_pen_up", true);
            }
            finally { UnityEngine.Random.state = random; }
            string path = Path.GetFullPath("Screenshots/PlayerDosa/grip-contact-validation.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return path + "\n" + string.Join("\n", report.samples.Select(s => s.name + " " + string.Join(", ", s.surfaces.Select(f => f.finger + "=" + (f.minimumGapMeters * 1000f).ToString("F2") + "mm distal=" + (f.distalMinimumGapMeters * 1000f).ToString("F2")))));
        }

        private static void AddCandidate(Report report, string name, bool penUp)
        {
            GameObject root = null, cameraObject = null;
            DrawingPoseProfileSO pose = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DosaPlayerBuilder.VisualPrefabPath));
                root.name = "DosaGripContact_Temporary";
                root.hideFlags = HideFlags.HideAndDontSave;
                root.transform.position = new Vector3(300f, 0f, 300f);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
                var rig = root.GetComponent<PlayerVisualRig>();
                var world = root.transform.Find("WorldBody").GetComponent<Animator>();
                var near = root.transform.Find("NearArms").GetComponent<Animator>();
                pose = Object.Instantiate(AssetDatabase.LoadAssetAtPath<DrawingPoseProfileSO>(DosaPlayerBuilder.DrawingPath));
                rig.Configure(world, near, root.transform.Find("WorldBrush"), root.transform.Find("NearBrush"),
                    AssetDatabase.LoadAssetAtPath<PlayerVisualProfileSO>(DosaPlayerBuilder.VisualPath), pose);
                cameraObject = new GameObject("DosaGripContact_Camera"); cameraObject.hideFlags = HideFlags.HideAndDontSave;
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.transform.position = root.transform.position + Vector3.up * 1.7f;
                camera.fieldOfView = 52f; camera.nearClipPlane = .01f;
                camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.4f, .4f, .4f);
                target = new RenderTexture(1280, 720, 24); target.Create(); camera.targetTexture = target;
                world.Rebind(); near.Rebind(); world.Update(0f); near.Update(0f);
                var frame = new PlayerVisualFrame { Camera = camera, HasPointer = true, Drawing = true,
                    Stroking = !penUp, Combat = true, PointerScreen = new Vector2(860f, 464f) / 1.5f };
                for (int i = 0; i < 90; i++)
                {
                    rig.UpdateMotion(frame, 1f / 60f);
                    world.Update(1f / 60f); near.Update(1f / 60f);
                    rig.ApplyFrame(frame, 1f / 60f);
                }
                foreach (var pair in new[] { (label: "near", body: near.transform, brush: root.transform.Find("NearBrush")),
                    (label: "world", body: world.transform, brush: root.transform.Find("WorldBrush")) })
                {
                    var sample = Measure(name + "_" + pair.label, pair.body, pair.brush, report);
                    sample.thumbAngles = pose.ThumbCurlDegrees; sample.fingerAngles = pose.FingerCurlDegrees;
                    sample.thumbOpposition = pose.ThumbOppositionDegrees;
                    sample.indexAngles = pose.IndexCurlDegrees; sample.middleAngles = pose.MiddleCurlDegrees;
                    sample.ringAngles = pose.RingCurlDegrees; sample.pinkyAngles = pose.PinkyCurlDegrees;
                    sample.penUp = penUp; sample.eligibleForContact = true;
                    if (pair.label == "near") sample.screenshot = Path.GetFullPath("Screenshots/PlayerDosa/Grip/" + name + ".png");
                    report.samples.Add(sample);
                }
                string screenshot = Path.GetFullPath("Screenshots/PlayerDosa/Grip/" + name + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(screenshot));
                camera.Render(); RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(screenshot, pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (root != null) Object.DestroyImmediate(root);
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (pose != null) Object.DestroyImmediate(pose);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (pixels != null) Object.DestroyImmediate(pixels);
            }
        }

        private static Sample Measure(string name, Transform arms, Transform brush, Report report)
        {
            var grip = brush.Find("GripSocket") ?? brush;
            var handle = brush.Find("Handle");
            var handleVertices = handle.GetComponent<MeshFilter>().sharedMesh.vertices;
            float radius = 0f, lower = float.MaxValue, upper = float.MinValue;
            foreach (var v in handleVertices)
            {
                var p = grip.InverseTransformPoint(handle.TransformPoint(v));
                radius = Mathf.Max(radius, new Vector2(p.x, p.z).magnitude);
                lower = Mathf.Min(lower, p.y); upper = Mathf.Max(upper, p.y);
            }
            report.handleRadiusMeters = radius; report.handleMinimumY = lower; report.handleMaximumY = upper;
            var result = new Sample { name = name };
            var transforms = arms.GetComponentsInChildren<Transform>(true);
            Vector3 Joint(string joint) => grip.InverseTransformPoint(transforms.First(t => t.name.EndsWith(joint, StringComparison.Ordinal)).position);
            result.index1InGrip = Joint("RightHandIndex1"); result.index2InGrip = Joint("RightHandIndex2"); result.index3InGrip = Joint("RightHandIndex3");
            foreach (string finger in Names) result.surfaces.Add(new Surface { finger = finger });
            foreach (var skin in arms.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh baked = new Mesh();
                try
                {
                    skin.BakeMesh(baked);
                    var vertices = baked.vertices; var triangles = baked.triangles;
                    var weights = skin.sharedMesh.boneWeights; var bones = skin.bones;
                    if (weights.Length != vertices.Length) throw new InvalidOperationException("Skin weight count differs from baked vertices");
                    int[] group = new int[bones.Length]; bool[] distal = new bool[bones.Length];
                    for (int b = 0; b < bones.Length; b++)
                    {
                        group[b] = -1;
                        for (int f = 0; f < 5; f++)
                            if (bones[b].name.Contains("RightHand" + Names[f]))
                            { group[b] = f; distal[b] = bones[b].name.EndsWith("2", StringComparison.Ordinal) || bones[b].name.EndsWith("3", StringComparison.Ordinal); }
                        if (bones[b].name.EndsWith("RightHand", StringComparison.Ordinal)) group[b] = 5;
                    }
                    float Weight(BoneWeight w, int g, bool onlyDistal)
                    {
                        float Part(int bone, float value) => group[bone] == g && (!onlyDistal || distal[bone]) ? value : 0f;
                        return Part(w.boneIndex0, w.weight0) + Part(w.boneIndex1, w.weight1) + Part(w.boneIndex2, w.weight2) + Part(w.boneIndex3, w.weight3);
                    }
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = grip.InverseTransformPoint(skin.transform.TransformPoint(vertices[i]));
                    if (name == "baseline") ExportSolver(skin, grip, vertices, triangles, radius, lower, upper);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                        for (int g = 0; g < Names.Length; g++)
                        {
                            if ((Weight(weights[a], g, false) + Weight(weights[b], g, false) + Weight(weights[c], g, false)) / 3f < .5f) continue;
                            var s = result.surfaces[g]; s.triangles++;
                            float gap = SegmentTriangle(new Vector3(0f, lower, 0f), new Vector3(0f, upper, 0f), vertices[a], vertices[b], vertices[c], out Vector3 closest) - radius;
                            if (gap < s.minimumGapMeters) { s.minimumGapMeters = gap; s.closestSkinInGrip = closest; }
                            if (gap < 0f) s.intersectingTriangles++;
                            if ((Weight(weights[a], g, true) + Weight(weights[b], g, true) + Weight(weights[c], g, true)) / 3f < .3f) continue;
                            s.distalTriangles++; s.distalMinimumGapMeters = Mathf.Min(s.distalMinimumGapMeters, gap);
                        }
                    }
                }
                finally { Object.DestroyImmediate(baked); }
            }
            return result;
        }

        private static void ExportSolver(SkinnedMeshRenderer skin, Transform grip, Vector3[] baked, int[] triangles, float radius, float lower, float upper)
        {
            var bones = skin.bones; var bind = skin.sharedMesh.bindposes;
            var source = skin.sharedMesh.vertices; var weights = skin.sharedMesh.boneWeights;
            var data = new SolverData { bones = new SolverBone[bones.Length], vertices = new SolverVertex[source.Length],
                triangles = triangles, radius = radius, minimumY = lower, maximumY = upper };
            for (int i = 0; i < bones.Length; i++)
                data.bones[i] = new SolverBone { name = bones[i].name, parent = Array.IndexOf(bones, bones[i].parent),
                    position = bones[i].localPosition, rotation = bones[i].localRotation, scale = bones[i].localScale,
                    matrix = grip.worldToLocalMatrix * bones[i].localToWorldMatrix, bind = bind[i] };
            for (int i = 0; i < source.Length; i++)
            {
                var w = weights[i];
                data.vertices[i] = new SolverVertex { source = source[i], baked = baked[i],
                    bones = new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 }, weights = new[] { w.weight0, w.weight1, w.weight2, w.weight3 } };
            }
            string path = Path.GetFullPath("Screenshots/PlayerDosa/grip-solver-input.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(data));
        }

        private static float SegmentTriangle(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c, out Vector3 closest)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a), direction = end - start;
            float denominator = Vector3.Dot(normal, direction);
            if (Mathf.Abs(denominator) > 1e-12f)
            {
                float t = Vector3.Dot(normal, a - start) / denominator;
                Vector3 p = start + direction * t;
                if (t >= 0f && t <= 1f && (ClosestTriangle(p, a, b, c) - p).sqrMagnitude < 1e-12f)
                { closest = p; return 0f; }
            }
            closest = ClosestTriangle(start, a, b, c); float best = (closest - start).sqrMagnitude;
            Vector3 q = ClosestTriangle(end, a, b, c); float d = (q - end).sqrMagnitude;
            if (d < best) { best = d; closest = q; }
            Vector3[] edgeA = { a, b, c }, edgeB = { b, c, a };
            for (int i = 0; i < 3; i++)
            {
                d = SegmentSegment(start, end, edgeA[i], edgeB[i], out q);
                if (d < best) { best = d; closest = q; }
            }
            return Mathf.Sqrt(best);
        }

        private static float SegmentSegment(Vector3 p, Vector3 q, Vector3 a, Vector3 b, out Vector3 onEdge)
        {
            Vector3 d1 = q - p, d2 = b - a, r = p - a;
            float aa = Vector3.Dot(d1, d1), ee = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (ee <= 1e-12f) { t = 0f; s = Mathf.Clamp01(-Vector3.Dot(d1, r) / aa); }
            else
            {
                float cc = Vector3.Dot(d1, r), bb = Vector3.Dot(d1, d2), denom = aa * ee - bb * bb;
                s = Mathf.Abs(denom) > 1e-12f ? Mathf.Clamp01((bb * f - cc * ee) / denom) : 0f;
                t = (bb * s + f) / ee;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-cc / aa); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((bb - cc) / aa); }
            }
            onEdge = a + d2 * t;
            return (p + d1 * s - onEdge).sqrMagnitude;
        }

        private static Vector3 ClosestTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denominator = va + vb + vc;
            return Mathf.Abs(denominator) < 1e-18f ? a : a + ab * (vb / denominator) + ac * (vc / denominator);
        }
    }
}
