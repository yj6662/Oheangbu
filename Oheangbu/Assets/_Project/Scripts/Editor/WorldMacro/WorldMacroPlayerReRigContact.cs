using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        [Serializable] private sealed class ContactDigit
        {
            public string digit;
            public int distalVertices, shaftRayHits;
            public float closestSkinGapMeters = -1f, maximumPenetrationMeters;
            public float manualLbsClosestSkinGapMeters = -1f, manualLbsMaximumPenetrationMeters;
            public int closestVertex = -1;
            public Vector3 closestSkinInGrip;
            public bool initialContactThresholdPass;
        }

        [Serializable] private sealed class ContactBonePose
        {
            public string name;
            public Vector3 localPosition, localScale;
            public Quaternion localRotation, expectedLocalRotation;
            public float rotationErrorDegrees;
        }

        [Serializable] private sealed class ContactReport
        {
            public string status, utc, sourceFbxSha256, skinWeightsBefore, skinWeightsDuring, skinWeightsAfter, error;
            public string sourceModelPath, gestureProfilePath, gestureProfileSha256;
            public BoundSkinSource[] boundSkins;
            public string method = "Actual current-pose SkinnedMeshRenderer.BakeMesh(useScale=true), full imported influences under Unlimited. Scale-compensated baked vertices are transformed by the complete renderer local chain exactly once. Two-sided ray/triangle intersections against the actual rigid shaft mesh, radially about GripSocket to fixed Bristle_01. Hand vertices require >=0.1 right-hand influence; distal patches require >=0.7 distal-finger influence. No primitive-cylinder approximation or gameplay colliders.";
            public string bakeScaleConventionSource = "https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html";
            public string uncompensatedComparison = "Diagnostic comparison only: useScale=false already includes renderer scale, so applying the complete renderer matrix again can double-scale vertices. This previous measurement is retained below for comparison, never used for contact acceptance.";
            public string[] limitations = { "Single actual live pose only. Closest distal vertex gap and worst sampled vertex penetration do not prove continuous triangle contact or all-motion safety.",
                "Measurements use local transform chains relative to the player to limit world-coordinate cancellation. Unity's actual BakeMesh precision remains part of the result." };
            public bool playerTransformsPreserved, qualityRestored, temporaryMeshesReleased, allFinite, bakeConventionValidated;
            public int frame, shaftTriangles, handVertices, shaftRayHits, shaftRayMisses, worstVertex = -1;
            public float maximumPenetrationMeters, rayRadiusMeters = .08f;
            public float manualLbsMaximumPenetrationMeters, maximumBakeVersusManualLbsMeters, meanBakeVersusManualLbsMeters;
            public float uncompensatedMaximumBakeVersusManualLbsMeters, uncompensatedMeanBakeVersusManualLbsMeters;
            public float maximumFingerPoseErrorDegrees, actualGripPositionErrorMeters;
            public int maximumHandInfluences;
            public string[] bodyRenderers, shaftRenderers;
            public Vector3 shaftAxisInGrip, worstSkinInGrip;
            public Vector3 playerWorldPosition, handWorldScale, brushWorldScale, actualGripInHand, worstManualLbsInGrip, worstBakeInGrip;
            public ContactBonePose[] currentFingerPoses;
            public ContactDigit[] fingers;
        }

        private struct ShaftTriangle
        {
            public Vector3 A, Edge1, Edge2;
            public float MinimumAxial, MaximumAxial;
        }

        private static string ValidateCurrentSkinContact(string auditOutput = null)
        {
            Need(EditorApplication.isPlaying, "Contact validation needs the existing live player pose; it never enters Play or moves the player.");
            var rig = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            Need(rig != null && rig.IsPresentationReady, "A bound live player gesture rig is required.");
            Transform brush = Find(rig.transform, "C02_WorldBrush"), near = Find(rig.transform, "C02_NearArm"), nearBrush = Find(rig.transform, "C02_NearBrush");
            Transform grip = Find(brush, "GripSocket"), bristleRoot = Find(brush, "Bristle_01");
            Need(brush != null && grip != null && bristleRoot != null, "Actual world brush, grip and fixed bristle root are required.");
            var transforms = rig.GetComponentsInChildren<Transform>(true);
            Vector3[] positions = transforms.Select(t => t.localPosition).ToArray(), scales = transforms.Select(t => t.localScale).ToArray();
            Quaternion[] rotations = transforms.Select(t => t.localRotation).ToArray();
            SkinWeights previousQuality = QualitySettings.skinWeights;
            var temporary = new List<Mesh>();
            var report = new ContactReport
            {
                utc = DateTime.UtcNow.ToString("o"), frame = Time.frameCount, sourceFbxSha256 = ActiveSkinSourceHash(rig),
                sourceModelPath = ActiveSkinSourcePath(rig), gestureProfilePath = AssetDatabase.GetAssetPath(rig.Profile),
                gestureProfileSha256 = Sha(AssetDatabase.GetAssetPath(rig.Profile)), boundSkins = ReadBoundSkinSources(rig),
                skinWeightsBefore = previousQuality.ToString(), allFinite = true,
                fingers = new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }.Select(d => new ContactDigit { digit = d }).ToArray()
            };
            var currentHand = Find(rig.transform, "RightHand");
            report.playerWorldPosition = rig.transform.position; report.handWorldScale = currentHand.lossyScale; report.brushWorldScale = brush.lossyScale;
            report.actualGripInHand = currentHand.InverseTransformPoint(grip.position);
            report.actualGripPositionErrorMeters = Vector3.Distance(currentHand.TransformPoint(rig.Profile.RightHandGripPosition), grip.position);
            report.currentFingerPoses = rig.Profile.Fingers.Where(p => p.BoneName.StartsWith("RightHand", StringComparison.Ordinal)).Select(p =>
            {
                Transform bone = Find(rig.transform, p.BoneName);
                Quaternion expected = WorldMacroPlayerGestureRig.FingerLocalRotation(p.RestLocalRotation, p, rig.Diagnostics.DrawingWeight, rig.Diagnostics.HarvestWeight);
                return new ContactBonePose { name = p.BoneName, localPosition = bone.localPosition, localScale = bone.localScale,
                    localRotation = bone.localRotation, expectedLocalRotation = expected, rotationErrorDegrees = Quaternion.Angle(bone.localRotation, expected) };
            }).ToArray();
            report.maximumFingerPoseErrorDegrees = report.currentFingerPoses.Max(p => p.rotationErrorDegrees);
            try
            {
                QualitySettings.skinWeights = SkinWeights.Unlimited;
                report.skinWeightsDuring = QualitySettings.skinWeights.ToString();
                Matrix4x4 gripInverse = RelativeToAncestor(grip, rig.transform).inverse;
                Vector3 axis = gripInverse.MultiplyPoint3x4(RelativeToAncestor(bristleRoot, rig.transform).MultiplyPoint3x4(Vector3.zero)).normalized;
                Need(axis.sqrMagnitude > .9f, "Fixed bristle root cannot define the rigid shaft axis.");
                report.shaftAxisInGrip = axis;
                var shaft = new List<ShaftTriangle>(); var shaftNames = new List<string>();
                foreach (var renderer in brush.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name.IndexOf("Handle", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Mesh mesh = BakeContactMesh(renderer, temporary); if (mesh == null) continue;
                    shaftNames.Add(renderer.name);
                    Matrix4x4 matrix = gripInverse * RelativeToAncestor(renderer.transform, rig.transform);
                    Vector3[] vertices = mesh.vertices; int[] triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        Vector3 a = matrix.MultiplyPoint3x4(vertices[triangles[i]]), b = matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]), c = matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                        float da = Vector3.Dot(a, axis), db = Vector3.Dot(b, axis), dc = Vector3.Dot(c, axis);
                        shaft.Add(new ShaftTriangle { A = a, Edge1 = b - a, Edge2 = c - a, MinimumAxial = Mathf.Min(da, Mathf.Min(db, dc)), MaximumAxial = Mathf.Max(da, Mathf.Max(db, dc)) });
                    }
                }
                report.shaftRenderers = shaftNames.ToArray(); report.shaftTriangles = shaft.Count;
                Need(shaft.Count > 0, "No actual handle triangles found; contact is unverified.");
                var bodyNames = new List<string>();
                foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.transform.IsChildOf(brush) || (near != null && skin.transform.IsChildOf(near)) || (nearBrush != null && skin.transform.IsChildOf(nearBrush))) continue;
                    Mesh mesh = BakeContactMesh(skin, temporary); if (mesh == null) continue;
                    var uncompensatedMesh = new Mesh { name = "Temporary_Contact_Uncompensated_" + skin.name, hideFlags = HideFlags.HideAndDontSave };
                    temporary.Add(uncompensatedMesh); skin.BakeMesh(uncompensatedMesh, false);
                    Vector3[] uncompensatedVertices = uncompensatedMesh.vertices;
                    bodyNames.Add(skin.name); Matrix4x4 matrix = gripInverse * RelativeToAncestor(skin.transform, rig.transform);
                    Vector3[] vertices = mesh.vertices; var counts = skin.sharedMesh.GetBonesPerVertex(); var weights = skin.sharedMesh.GetAllBoneWeights();
                    Vector3[] restVertices = skin.sharedMesh.vertices; Matrix4x4[] bindposes = skin.sharedMesh.bindposes;
                    Transform[] bones = skin.bones; bool[] handBones = new bool[bones.Length]; int[] distalBones = new int[bones.Length];
                    var manualMatrices = new Matrix4x4[bones.Length];
                    for (int bone = 0; bone < bones.Length; bone++)
                    {
                        string name = bones[bone] != null ? bones[bone].name : "";
                        handBones[bone] = name.StartsWith("RightHand", StringComparison.Ordinal); distalBones[bone] = -1;
                        for (int d = 0; d < report.fingers.Length; d++) if (name == "RightHand" + report.fingers[d].digit + "3") distalBones[bone] = d;
                        if (bones[bone] != null && bone < bindposes.Length)
                            manualMatrices[bone] = gripInverse * RelativeToAncestor(bones[bone], rig.transform) * bindposes[bone];
                    }
                    try
                    {
                        Need(vertices.Length == counts.Length, "BakeMesh/source vertex indexing differs: " + skin.name);
                        int weightOffset = 0; float[] distal = new float[report.fingers.Length];
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            float handWeight = 0f; int firstWeight = weightOffset; Array.Clear(distal, 0, distal.Length);
                            for (int j = 0; j < counts[i]; j++)
                            {
                                var weight = weights[weightOffset++];
                                if (weight.boneIndex >= bones.Length) continue;
                                if (handBones[weight.boneIndex]) handWeight += weight.weight;
                                int digit = distalBones[weight.boneIndex]; if (digit >= 0) distal[digit] += weight.weight;
                            }
                            if (handWeight < .1f) continue;
                            report.handVertices++; report.maximumHandInfluences = Mathf.Max(report.maximumHandInfluences, counts[i]);
                            Vector3 point = matrix.MultiplyPoint3x4(vertices[i]);
                            if (!Finite(point)) { report.allFinite = false; continue; }
                            Vector3 manual = Vector3.zero;
                            for (int w = firstWeight; w < weightOffset; w++)
                            {
                                var influence = weights[w];
                                if (influence.boneIndex < manualMatrices.Length) manual += manualMatrices[influence.boneIndex].MultiplyPoint3x4(restVertices[i]) * influence.weight;
                            }
                            float bakeDelta = Vector3.Distance(point, manual); report.meanBakeVersusManualLbsMeters += bakeDelta;
                            float uncompensatedDelta = Vector3.Distance(matrix.MultiplyPoint3x4(uncompensatedVertices[i]), manual);
                            report.uncompensatedMaximumBakeVersusManualLbsMeters = Mathf.Max(report.uncompensatedMaximumBakeVersusManualLbsMeters, uncompensatedDelta);
                            report.uncompensatedMeanBakeVersusManualLbsMeters += uncompensatedDelta;
                            if (bakeDelta > report.maximumBakeVersusManualLbsMeters)
                            { report.maximumBakeVersusManualLbsMeters = bakeDelta; report.worstManualLbsInGrip = manual; report.worstBakeInGrip = point; }
                            if (ShaftSurfaceGap(manual, axis, shaft, report.rayRadiusMeters, out float manualGap))
                            {
                                float manualPenetration = Mathf.Max(0f, -manualGap);
                                report.manualLbsMaximumPenetrationMeters = Mathf.Max(report.manualLbsMaximumPenetrationMeters, manualPenetration);
                                for (int d = 0; d < distal.Length; d++) if (distal[d] >= .7f)
                                {
                                    var digit = report.fingers[d]; digit.manualLbsMaximumPenetrationMeters = Mathf.Max(digit.manualLbsMaximumPenetrationMeters, manualPenetration);
                                    if (digit.manualLbsClosestSkinGapMeters < 0f || Mathf.Abs(manualGap) < digit.manualLbsClosestSkinGapMeters)
                                        digit.manualLbsClosestSkinGapMeters = Mathf.Abs(manualGap);
                                }
                            }
                            for (int d = 0; d < distal.Length; d++) if (distal[d] >= .7f) report.fingers[d].distalVertices++;
                            if (!ShaftSurfaceGap(point, axis, shaft, report.rayRadiusMeters, out float signedGap)) { report.shaftRayMisses++; continue; }
                            report.shaftRayHits++;
                            float penetration = Mathf.Max(0f, -signedGap);
                            if (penetration > report.maximumPenetrationMeters)
                            { report.maximumPenetrationMeters = penetration; report.worstVertex = i; report.worstSkinInGrip = point; }
                            for (int d = 0; d < distal.Length; d++)
                            {
                                if (distal[d] < .7f) continue;
                                var digit = report.fingers[d]; digit.shaftRayHits++; digit.maximumPenetrationMeters = Mathf.Max(digit.maximumPenetrationMeters, penetration);
                                float gap = Mathf.Abs(signedGap);
                                if (digit.closestSkinGapMeters < 0f || gap < digit.closestSkinGapMeters)
                                { digit.closestSkinGapMeters = gap; digit.closestVertex = i; digit.closestSkinInGrip = point; }
                            }
                        }
                    }
                    finally { counts.Dispose(); weights.Dispose(); }
                }
                report.bodyRenderers = bodyNames.ToArray();
                report.meanBakeVersusManualLbsMeters /= Mathf.Max(1, report.handVertices);
                report.uncompensatedMeanBakeVersusManualLbsMeters /= Mathf.Max(1, report.handVertices);
                report.bakeConventionValidated = report.handVertices > 0 && report.maximumBakeVersusManualLbsMeters <= .0005f;
                foreach (var digit in report.fingers)
                    digit.initialContactThresholdPass = digit.shaftRayHits > 0 && digit.closestSkinGapMeters <= .0015f && digit.maximumPenetrationMeters <= .0005f;
                report.status = !report.bakeConventionValidated ? "UNVERIFIED_BAKE_CONVENTION_MISMATCH"
                    : report.allFinite && report.handVertices > 0 && report.shaftRayHits > 0 && report.maximumPenetrationMeters <= .0005f
                        && report.fingers.Take(3).All(d => d.initialContactThresholdPass) ? "PASS_CURRENT_POSE_VERTEX_CONTACT_ONLY" : "FAIL_CURRENT_POSE_CONTACT";
            }
            catch (Exception exception) { report.status = "UNVERIFIED_CONTACT_ERROR"; report.error = exception.ToString(); }
            finally
            {
                QualitySettings.skinWeights = previousQuality;
                foreach (var mesh in temporary) if (mesh != null) Object.DestroyImmediate(mesh);
                report.temporaryMeshesReleased = temporary.All(mesh => mesh == null);
                report.qualityRestored = QualitySettings.skinWeights == previousQuality; report.skinWeightsAfter = QualitySettings.skinWeights.ToString();
                report.playerTransformsPreserved = transforms.Select((t, i) => t != null && t.localPosition == positions[i] && t.localScale == scales[i]
                    && t.localRotation.Equals(rotations[i])).All(value => value);
            }
            string directory = auditOutput ?? Output; Directory.CreateDirectory(directory); string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(directory, "current_skin_contact.json"), json); return json;
        }

        private static Mesh BakeContactMesh(Renderer renderer, List<Mesh> temporary)
        {
            if (renderer is SkinnedMeshRenderer skin)
            {
                var mesh = new Mesh { name = "Temporary_Contact_" + skin.name, hideFlags = HideFlags.HideAndDontSave };
                // true compensates Transform scale; the caller subsequently applies the full local-to-grip matrix.
                temporary.Add(mesh); skin.BakeMesh(mesh, true); return mesh;
            }
            var filter = renderer.GetComponent<MeshFilter>(); return filter != null ? filter.sharedMesh : null;
        }

        private static Matrix4x4 RelativeToAncestor(Transform transform, Transform ancestor)
        {
            Matrix4x4 result = Matrix4x4.identity;
            while (transform != null && transform != ancestor)
            {
                result = Matrix4x4.TRS(transform.localPosition, transform.localRotation, transform.localScale) * result;
                transform = transform.parent;
            }
            Need(transform == ancestor, "Contact geometry is outside the player hierarchy."); return result;
        }

        private static bool ShaftSurfaceGap(Vector3 point, Vector3 axis, List<ShaftTriangle> triangles, float radius, out float signedGap)
        {
            signedGap = 0f; float axial = Vector3.Dot(point, axis); Vector3 center = axis * axial;
            Vector3 radial = point - center; float distance = radial.magnitude;
            if (distance < .000001f) return false;
            Vector3 normal = radial / distance, origin = center + normal * radius, direction = -normal;
            float nearest = radius * 2f; bool found = false;
            foreach (var triangle in triangles)
            {
                if (axial < triangle.MinimumAxial - .000001f || axial > triangle.MaximumAxial + .000001f) continue;
                Vector3 p = Vector3.Cross(direction, triangle.Edge2); float determinant = Vector3.Dot(triangle.Edge1, p);
                if (Mathf.Abs(determinant) < 1e-10f) continue;
                float inverse = 1f / determinant; Vector3 t = origin - triangle.A;
                float u = Vector3.Dot(t, p) * inverse; if (u < -.000001f || u > 1.000001f) continue;
                Vector3 q = Vector3.Cross(t, triangle.Edge1); float v = Vector3.Dot(direction, q) * inverse;
                if (v < -.000001f || u + v > 1.000001f) continue;
                float hit = Vector3.Dot(triangle.Edge2, q) * inverse;
                if (hit < 0f || hit > nearest) continue;
                nearest = hit; found = true;
            }
            if (!found) return false;
            signedGap = distance - Mathf.Abs(radius - nearest); return true;
        }
    }
}
