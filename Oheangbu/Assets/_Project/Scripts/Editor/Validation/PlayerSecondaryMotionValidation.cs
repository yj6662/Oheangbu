using System;
using System.Collections.Generic;
using System.IO;
using Oheangbu.Data;
using Oheangbu.Presentation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Temporary authored fixtures only; does not install assets or touch a player/scene setting.</summary>
    public static class PlayerSecondaryMotionValidation
    {
        [Serializable] private sealed class Report
        {
            public string status;
            public string scope = "Runtime framework fixtures, not final costume cloth quality or RIG_PASS. Cloth tests verify coefficients, ownership and lifecycle; native moving-cloth penetration/performance requires the new authored mesh in Play Mode.";
            public int checks;
            public float maxFrameRateDifferenceDegrees, maxChainLengthErrorMeters, cameraRelativeDifferenceDegrees;
            public float peakSwingDegrees, pausedRotationDifferenceDegrees, maxRigidDistanceErrorMeters;
            public long allocatedBytesFor1000Evaluations;
            public List<string> failures = new List<string>();
        }

        public static string Run()
        {
            if (!Application.isPlaying) return "NOT_RUN: Play Mode required for temporary Cloth fixture initialization.";
            var report = new Report();
            Group(report, "Frame rate / damping", () => FrameRates(report));
            Group(report, "Game time / reset", () => TimeAndReset(report));
            Group(report, "Camera reference", () => CameraReference(report));
            Group(report, "Ownership / invalid bindings", () => Ownership(report));
            Group(report, "Authored cloth", () => ClothBindings(report));
            report.status = report.failures.Count == 0 ? "PASS" : "FAIL";
            string json = JsonUtility.ToJson(report, true);
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "secondary-motion-validation.json"), json);
            Debug.Log("[PlayerSecondaryMotionValidation] " + json);
            return json;
        }

        private static void FrameRates(Report report)
        {
            var end = new Quaternion[3];
            int sample = 0;
            foreach (int fps in new[] { 30, 60, 120 })
            {
                using (var f = new Fixture())
                {
                    Vector3 rootPosition = f.Root.transform.position;
                    Quaternion rootRotation = f.Root.transform.rotation;
                    Vector3 pivotLocal = f.Pivot.localPosition, pivotScale = f.Pivot.localScale;
                    float rigidDistance = Vector3.Distance(f.RigidA.position, f.RigidB.position);
                    f.Rig.Evaluate(0f);
                    for (int frame = 1; frame <= fps * 3; frame++)
                    {
                        float t = frame / (float)fps;
                        f.Anchor.localPosition = new Vector3(.1f * Mathf.Sin(t * Mathf.PI), 1f, 0f);
                        f.Anchor.localRotation = Quaternion.Euler(0f, 12f * Mathf.Sin(t * Mathf.PI), 0f);
                        f.Rig.Evaluate(1f / fps, t <= 1f ? Vector3.right * 3f : Vector3.zero);
                        report.peakSwingDegrees = Mathf.Max(report.peakSwingDegrees, f.Rig.Diagnostics.LargestSwingDegrees);
                        report.maxChainLengthErrorMeters = Mathf.Max(report.maxChainLengthErrorMeters,
                            Mathf.Abs(Vector3.Distance(f.ChainA.position, f.ChainB.position) - .1f),
                            Mathf.Abs(Vector3.Distance(f.ChainB.position, f.ChainTip.position) - .08f));
                        report.maxRigidDistanceErrorMeters = Mathf.Max(report.maxRigidDistanceErrorMeters,
                            Mathf.Abs(Vector3.Distance(f.RigidA.position, f.RigidB.position) - rigidDistance));
                    }
                    end[sample++] = f.Pivot.localRotation;
                    Check(report, f.Root.transform.position == rootPosition && f.Root.transform.rotation == rootRotation,
                        "Solver moved the gameplay/root transform.");
                    Check(report, f.Pivot.localPosition == pivotLocal && f.Pivot.localScale == pivotScale,
                        "Solver changed a rigid pivot position or scale.");
                    Check(report, f.Rig.Diagnostics.DroppedSimulationSeconds < .00001f,
                        "30/60/120 Hz dropped game-time simulation.");
                    for (int i = 0; i < fps * 3; i++) f.Rig.Evaluate(1f / fps);
                    Check(report, Quaternion.Angle(f.Pivot.localRotation, Quaternion.identity) < .1f,
                        "An unforced pendant did not settle through damping.");
                }
            }
            report.maxFrameRateDifferenceDegrees = Mathf.Max(Quaternion.Angle(end[0], end[1]), Quaternion.Angle(end[0], end[2]));
            Check(report, report.peakSwingDegrees > 1f && report.peakSwingDegrees <= 35.01f,
                "Inertia/wind must visibly move ornaments while respecting the authored swing limit.");
            Check(report, report.maxFrameRateDifferenceDegrees < 1.5f,
                "Equivalent 30/60/120 Hz anchor trajectories differ by more than 1.5 degrees.");
            Check(report, report.maxChainLengthErrorMeters < .0001f && report.maxRigidDistanceErrorMeters < .0001f,
                "Secondary transforms stretched a chain or deformed a rigid ornament.");
        }

        private static void TimeAndReset(Report report)
        {
            using (var full = new Fixture())
            using (var slow = new Fixture())
            {
                // Equal elapsed game time: one wall second at normal speed, four at quarter speed.
                for (int i = 0; i < 120; i++) full.Rig.Evaluate(1f / 120f, Vector3.right * 4f);
                for (int i = 0; i < 480; i++) slow.Rig.Evaluate(.25f / 120f, Vector3.right * 4f);
                Check(report, Quaternion.Angle(full.Pivot.localRotation, slow.Pivot.localRotation) < .01f,
                    "Slow motion changed the simulated state at equal game time.");
                Quaternion paused = slow.Pivot.localRotation;
                for (int i = 0; i < 120; i++) slow.Rig.Evaluate(0f, Vector3.forward * 8f);
                report.pausedRotationDifferenceDegrees = Quaternion.Angle(paused, slow.Pivot.localRotation);
                Check(report, report.pausedRotationDifferenceDegrees < .01f && slow.Rig.Diagnostics.LastSubsteps == 0,
                    "Paused game time advanced secondary motion.");
                int resets = slow.Rig.Diagnostics.Resets;
                slow.Root.transform.position += Vector3.forward * 3f;
                slow.Rig.Evaluate(0f);
                Check(report, slow.Rig.Diagnostics.Resets == resets + 1
                    && slow.Rig.Diagnostics.LastReset == SecondaryMotionResetReason.Teleport
                    && Quaternion.Angle(slow.Pivot.localRotation, Quaternion.identity) < .01f,
                    "Teleport did not reset angular history to the authored pose.");
                slow.Rig.Evaluate(1f / 30f, Vector3.right * 5f);
                slow.Rig.enabled = false;
                Check(report, !slow.Rig.Diagnostics.Active && Quaternion.Angle(slow.Pivot.localRotation, Quaternion.identity) < .01f,
                    "Disabling the component retained offsets or active diagnostics.");
                slow.Rig.enabled = true;
                slow.Rig.Evaluate(0f);
                Check(report, slow.Rig.Diagnostics.LastReset == SecondaryMotionResetReason.Enabled
                    && Quaternion.Angle(slow.Pivot.localRotation, Quaternion.identity) < .01f,
                    "Re-enabling retained stale velocity.");
                slow.Rig.SetRepresentationActive(false);
                slow.Rig.Evaluate(.1f, Vector3.right * 30f);
                Check(report, !slow.Rig.Diagnostics.Active && Quaternion.Angle(slow.Pivot.localRotation, Quaternion.identity) < .01f,
                    "Hidden near representation continued running.");
                slow.Rig.SetRepresentationActive(true);
                slow.Rig.Evaluate(1f / 120f);
                for (int i = 0; i < 32; i++) slow.Rig.Evaluate(1f / 120f);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) slow.Rig.Evaluate(1f / 120f);
                report.allocatedBytesFor1000Evaluations = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(report, report.allocatedBytesFor1000Evaluations == 0,
                    "Bound ornament/chain evaluation allocated managed memory.");
            }
        }

        private static void CameraReference(Report report)
        {
            using (var still = new Fixture(true))
            using (var moving = new Fixture(true))
            {
                for (int i = 0; i < 240; i++)
                {
                    float t = i / 120f;
                    moving.Camera.transform.SetPositionAndRotation(new Vector3(t * 2f, .2f * Mathf.Sin(t), -t), Quaternion.Euler(50f * Mathf.Sin(t), t * 85f, 0f));
                    // Both representations receive the same wind in their own display frame.
                    still.Rig.Evaluate(1f / 120f, still.Camera.transform.right * 2f);
                    moving.Rig.Evaluate(1f / 120f, moving.Camera.transform.right * 2f);
                    report.cameraRelativeDifferenceDegrees = Mathf.Max(report.cameraRelativeDifferenceDegrees,
                        Quaternion.Angle(still.Pivot.localRotation, moving.Pivot.localRotation));
                }
                Check(report, report.cameraRelativeDifferenceDegrees < .1f,
                    "Camera-only movement injected motion into the near representation.");
                Check(report, moving.Rig.Diagnostics.LastReset != SecondaryMotionResetReason.Teleport,
                    "Camera movement was incorrectly treated as a near-view teleport.");
                moving.Rig.SetCameraReference(null);
                Check(report, moving.Rig.Diagnostics.LastReset == SecondaryMotionResetReason.ReferenceChanged
                    && Quaternion.Angle(moving.Pivot.localRotation, Quaternion.identity) < .01f,
                    "Reference switching did not reset prior camera-space state.");
            }
        }

        private static void Ownership(Report report)
        {
            using (var f = new Fixture())
            {
                int originalColliders = f.Root.GetComponentsInChildren<Collider>(true).Length;
                int originalBodies = f.Root.GetComponentsInChildren<Rigidbody>(true).Length;
                f.Rig.Evaluate(.05f, Vector3.right * 8f);
                Check(report, f.Root.GetComponentsInChildren<Collider>(true).Length == originalColliders
                    && f.Root.GetComponentsInChildren<Rigidbody>(true).Length == originalBodies,
                    "Runtime created physics objects without explicit authoring.");
                var duplicate = new[] { new RigidOrnamentBinding { Pivot = f.Pivot }, new RigidOrnamentBinding { Pivot = f.Pivot } };
                Check(report, !f.Rig.Configure(f.Profile, f.Root.transform, duplicate, null, null)
                    && !string.IsNullOrEmpty(f.Rig.LastBindingError), "Duplicate pivot binding was accepted.");
                Check(report, Quaternion.Angle(f.Pivot.localRotation, Quaternion.identity) < .01f,
                    "Failed reconfiguration retained a deformation from the previous binding.");
                f.Pivot.localScale = new Vector3(1f, 2f, 1f);
                Check(report, !f.Rig.Configure(f.Profile, f.Root.transform,
                    new[] { new RigidOrnamentBinding { Pivot = f.Pivot } }, null, null), "Nonuniform secondary pivot scale was accepted.");
                f.Pivot.localScale = Vector3.one;
                f.Profile.Ornament.FrequencyHz = float.NaN;
                Check(report, !f.Rig.Configure(f.Profile, f.Root.transform,
                    new[] { new RigidOrnamentBinding { Pivot = f.Pivot } }, null, null), "Nonfinite spring settings were accepted.");
                f.Profile.Ornament.FrequencyHz = 2.5f;
                Check(report, !f.Rig.Configure(f.Profile, f.Root.transform,
                    new[] { new RigidOrnamentBinding { Pivot = f.Root.transform } }, null, null), "Gameplay root was accepted as a secondary pivot.");
            }
        }

        private static void ClothBindings(Report report)
        {
            using (var f = new Fixture())
            {
                var clothObject = Child(f.Root.transform, "AuthoredCloth").gameObject;
                var renderer = clothObject.AddComponent<SkinnedMeshRenderer>();
                var mesh = new Mesh { name = "SecondaryMotionTemporaryCloth" };
                try
                {
                    mesh.vertices = new[] { new Vector3(-.1f, 0f, 0f), new Vector3(.1f, 0f, 0f), new Vector3(-.1f, -.2f, 0f), new Vector3(.1f, -.2f, 0f) };
                    mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                    mesh.bindposes = new[] { f.Root.transform.worldToLocalMatrix * clothObject.transform.localToWorldMatrix };
                    var weights = new BoneWeight[4];
                    for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                    mesh.boneWeights = weights; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    renderer.sharedMesh = mesh; renderer.bones = new[] { f.Root.transform }; renderer.rootBone = f.Root.transform;
                    renderer.enabled = false;
                    var cloth = clothObject.AddComponent<Cloth>();
                    var original = cloth.coefficients;
                    Check(report, original.Length == 4, "Native Cloth fixture particle count does not match its four authored vertices.");
                    if (original.Length != 4) return;
                    var capsule = Child(f.Root.transform, "ExplicitClothProxy").gameObject.AddComponent<CapsuleCollider>();
                    var coefficients = new[] { Coefficient(0f), Coefficient(0f), Coefficient(.08f), Coefficient(.1f) };
                    var binding = new PlayerClothBinding { Cloth = cloth, Coefficients = coefficients, Capsules = new[] { capsule } };
                    Check(report, !f.Rig.Configure(f.Profile, f.Root.transform, null, null, new[] { binding }),
                        "An ordinary gameplay collider was accepted as a Cloth proxy.");
                    capsule.gameObject.layer = 2; capsule.isTrigger = true; capsule.excludeLayers = -1; capsule.includeLayers = 0;
                    Check(report, f.Rig.Configure(f.Profile, f.Root.transform, null, null, new[] { binding }),
                        "Valid explicit Cloth binding rejected: " + f.Rig.LastBindingError);
                    var assigned = cloth.coefficients;
                    Check(report, assigned[0].maxDistance == 0f && assigned[1].maxDistance == 0f
                        && Mathf.Abs(assigned[2].maxDistance - .08f) < .000001f,
                        "Authored fixed seams / maxDistance were not applied.");
                    coefficients[2] = Coefficient(.5f);
                    f.Rig.enabled = false;
                    Check(report, !cloth.enabled, "Disabling secondary motion left owned native Cloth enabled.");
                    f.Rig.enabled = true;
                    Check(report, Mathf.Abs(cloth.coefficients[2].maxDistance - .08f) < .000001f,
                        "Caller mutation changed the captured authored cloth coefficients.");
                    f.Rig.Evaluate(0f, Vector3.right * 2f);
                    Check(report, (cloth.externalAcceleration - Vector3.right * f.Profile.Cloth.WindScale * 2f).sqrMagnitude < .000001f,
                        "Cloth wind did not use the supplied acceleration.");
                    Check(report, capsule.isTrigger && capsule.gameObject.layer == 2 && capsule.excludeLayers.value == -1
                        && capsule.includeLayers.value == 0 && capsule.attachedRigidbody == null,
                        "Runtime altered explicit proxy physics flags.");
                    f.Rig.SetRepresentationActive(false);
                    Check(report, !cloth.enabled, "Hidden representation left native Cloth running.");
                    f.Rig.Configure(f.Profile, f.Root.transform, null, null, null);
                    assigned = cloth.coefficients;
                    bool restored = assigned.Length == original.Length;
                    for (int i = 0; i < assigned.Length && restored; i++)
                        restored &= assigned[i].maxDistance == original[i].maxDistance
                            && assigned[i].collisionSphereDistance == original[i].collisionSphereDistance;
                    Check(report, restored, "Releasing Cloth ownership did not restore the original authored component.");
                    binding.Coefficients = new[] { Coefficient(0f) };
                    Check(report, !f.Rig.Configure(f.Profile, f.Root.transform, null, null, new[] { binding }),
                        "A coefficient count mismatch was accepted.");
                    binding.Coefficients = new[] { Coefficient(.1f), Coefficient(.1f), Coefficient(.1f), Coefficient(.1f) };
                    Check(report, !f.Rig.Configure(f.Profile, f.Root.transform, null, null, new[] { binding }),
                        "Wearable Cloth without fixed seam vertices was accepted.");
                }
                finally { Object.DestroyImmediate(clothObject); Object.DestroyImmediate(mesh); }
            }
        }

        private static ClothSkinningCoefficient Coefficient(float distance) => new ClothSkinningCoefficient { maxDistance = distance, collisionSphereDistance = 0f };
        private static void Group(Report report, string name, Action test)
        { try { test(); } catch (Exception e) { report.failures.Add(name + ": " + e); } }
        private static void Check(Report report, bool condition, string failure)
        { report.checks++; if (!condition) report.failures.Add(failure); }
        private static Transform Child(Transform parent, string name)
        { var result = new GameObject(name).transform; result.SetParent(parent, false); return result; }

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly Transform Anchor, Pivot, ChainA, ChainB, ChainTip, RigidA, RigidB;
            public readonly Camera Camera;
            public readonly PlayerSecondaryMotionProfileSO Profile;
            public readonly PlayerSecondaryMotionRig Rig;

            public Fixture(bool near = false)
            {
                Root = new GameObject("SecondaryMotionValidation") { hideFlags = HideFlags.HideAndDontSave };
                Profile = ScriptableObject.CreateInstance<PlayerSecondaryMotionProfileSO>();
                if (near)
                {
                    var cameraObject = new GameObject("SecondaryMotionValidationCamera") { hideFlags = HideFlags.HideAndDontSave };
                    Camera = cameraObject.AddComponent<Camera>(); Camera.enabled = false;
                    Root.transform.SetParent(Camera.transform, false);
                }
                Anchor = Child(Root.transform, "AnimatedAnchor"); Anchor.localPosition = Vector3.up;
                Pivot = Child(Anchor, "RigidOrnamentPivot");
                RigidA = Child(Pivot, "RigidPointA"); RigidA.localPosition = new Vector3(-.03f, -.08f, 0f);
                RigidB = Child(Pivot, "RigidPointB"); RigidB.localPosition = new Vector3(.03f, -.08f, 0f);
                ChainA = Child(Anchor, "SecondaryBoneA"); ChainA.localPosition = Vector3.right * .2f;
                ChainB = Child(ChainA, "SecondaryBoneB"); ChainB.localPosition = Vector3.down * .1f;
                ChainTip = Child(ChainB, "ChainTip"); ChainTip.localPosition = Vector3.down * .08f;
                Rig = Root.AddComponent<PlayerSecondaryMotionRig>();
                bool configured = Rig.Configure(Profile, Root.transform,
                    new[] { new RigidOrnamentBinding { Pivot = Pivot } },
                    new[] { new SecondaryBoneChainBinding { Bones = new[] { ChainA, ChainB }, LastBoneTipLocal = ChainTip.localPosition } }, null, Camera);
                if (!configured) throw new InvalidOperationException(Rig.LastBindingError);
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Root);
                if (Camera != null) Object.DestroyImmediate(Camera.gameObject);
                Object.DestroyImmediate(Profile);
            }
        }
    }
}
