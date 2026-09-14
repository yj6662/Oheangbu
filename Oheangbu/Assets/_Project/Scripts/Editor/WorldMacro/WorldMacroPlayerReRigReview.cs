using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        [Serializable] private sealed class RuntimeReport
        {
            public string status, utc, scene, profile;
            public bool playMode, rootMotionOff, nearHasNoAnimator, allBonesFinite;
            public int frame, activeSkinningLeases; public float timeScale;
            public string skinWeights;
            public WorldMacroPlayerGestureRig.GestureDiagnostics gesture;
            public BoneRow[] fingers;
            public string[] limitations = { "Single stationary runtime snapshot, not a walking, native-input or whole-motion pass.",
                "Socket alignment is not a proof of actual skin/shaft contact. A hidden near-arm pixel error of -1 is unverified." };
        }
        [Serializable] private sealed class CaptureReport
        {
            public string status, utc, view, image;
            public int width = 1920, height = 1080;
            public bool playMode, cameraTransformPreserved, temporaryResourcesReleased;
            public Vector3 cameraPosition, target; public float fieldOfView, systemCommit;
            public string scope = "One current-pose offscreen world still. External views temporarily show the world body and hide the near arm only for this render, then restore visibility. No gameplay input, automatic walking, video, build or source-camera movement.";
        }

        [Serializable] private sealed class MathReport
        {
            public string status, scope = "Pure presentation rotation checks. No scene/Animator/input mutation; not actual grip or naturalness approval.";
            public int axisCases, fingerCycles;
            public float maximumAxisError, maximumFingerReturnDegrees;
            public bool invalidQuaternionRejected, zeroAxisFinite;
        }

        [Serializable] private sealed class SkinningReport
        {
            public string status, qualityBefore, qualityAfter, error;
            public string scope = "Temporary runtime lease objects only. Refcount, repeated binding, disable/re-enable, destroy and prior-setting restoration; not a deformed mesh comparison.";
            public int leasesBefore, leasesAfter;
            public bool acquiredUnlimited, repeatedBindIdempotent, disableReleased, enableReacquired,
                destroyReleased, previousSettingPreserved, externalPresetCheckExecuted, externalPresetRestored;
        }

        private static string ValidateSkinningLease()
        {
            Need(EditorApplication.isPlaying, "The skinning lease diagnostic requires an existing Play session; it never starts one.");
            var before = QualitySettings.skinWeights;
            var report = new SkinningReport { leasesBefore = WorldMacroPlayerSkinningLease.ActiveLeaseCount, qualityBefore = before.ToString() };
            GameObject first = null, second = null;
            try
            {
                first = new GameObject("Temporary_C02SkinningLease_A") { hideFlags = HideFlags.HideAndDontSave };
                second = new GameObject("Temporary_C02SkinningLease_B") { hideFlags = HideFlags.HideAndDontSave };
                var a = first.AddComponent<WorldMacroPlayerSkinningLease>(); var b = second.AddComponent<WorldMacroPlayerSkinningLease>();
                a.SetBound(true); b.SetBound(true);
                report.acquiredUnlimited = WorldMacroPlayerSkinningLease.EffectiveUnlimited && WorldMacroPlayerSkinningLease.ActiveLeaseCount == report.leasesBefore + 2;
                a.SetBound(true);
                report.repeatedBindIdempotent = WorldMacroPlayerSkinningLease.ActiveLeaseCount == report.leasesBefore + 2;
                a.enabled = false;
                report.disableReleased = !a.HasLease && WorldMacroPlayerSkinningLease.ActiveLeaseCount == report.leasesBefore + 1;
                a.enabled = true;
                report.enableReacquired = a.HasLease && WorldMacroPlayerSkinningLease.ActiveLeaseCount == report.leasesBefore + 2;
                Object.DestroyImmediate(first); first = null;
                b.SetBound(false);
                report.previousSettingPreserved = QualitySettings.skinWeights == before && WorldMacroPlayerSkinningLease.ActiveLeaseCount == report.leasesBefore;
                if (report.leasesBefore == 0)
                {
                    // Do not alter the eventual restoration policy of a real player's active lease.
                    report.externalPresetCheckExecuted = true;
                    b.SetBound(true);
                    QualitySettings.skinWeights = SkinWeights.TwoBones;
                    b.SetBound(true); // Re-assertion is shared by LateUpdate and an idempotent bind.
                    bool unlimited = WorldMacroPlayerSkinningLease.EffectiveUnlimited;
                    b.SetBound(false);
                    report.externalPresetRestored = unlimited && QualitySettings.skinWeights == SkinWeights.TwoBones;
                    QualitySettings.skinWeights = before;
                }
                b.SetBound(true);
                Object.DestroyImmediate(second); second = null;
                report.destroyReleased = WorldMacroPlayerSkinningLease.ActiveLeaseCount == report.leasesBefore && QualitySettings.skinWeights == before;
            }
            catch (Exception exception) { report.error = exception.ToString(); }
            finally
            {
                if (first != null) Object.DestroyImmediate(first);
                if (second != null) Object.DestroyImmediate(second);
                if (report.leasesBefore == 0 && WorldMacroPlayerSkinningLease.ActiveLeaseCount == 0) QualitySettings.skinWeights = before;
            }
            report.leasesAfter = WorldMacroPlayerSkinningLease.ActiveLeaseCount; report.qualityAfter = QualitySettings.skinWeights.ToString();
            report.status = report.error == null && report.acquiredUnlimited && report.repeatedBindIdempotent && report.disableReleased
                && report.enableReacquired && report.destroyReleased && report.previousSettingPreserved
                && (!report.externalPresetCheckExecuted || report.externalPresetRestored) && report.leasesAfter == report.leasesBefore
                ? "PASS_SKINNING_LEASE_ONLY" : "FAIL";
            Directory.CreateDirectory(Output); string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(Output, "skinning_lease.json"), json); return json;
        }

        private static string ValidateGestureMath()
        {
            var directions = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back, new Vector3(.3f, -.5f, .8f).normalized };
            var report = new MathReport();
            foreach (var local in directions) foreach (var axis in directions) foreach (float roll in new[] { -90f, 0f, 70f })
            {
                Quaternion q = WorldMacroPlayerGestureRig.GripRotation(local, axis, axis, roll);
                report.maximumAxisError = Mathf.Max(report.maximumAxisError, Vector3.Distance(q * local, axis)); report.axisCases++;
            }
            Quaternion rest = Quaternion.Euler(33f, -12f, 17f);
            var finger = new WorldMacroPlayerGestureProfile.FingerPose { CarryOffset = Quaternion.Euler(20f, 4f, 0f),
                DrawingOffset = Quaternion.Euler(75f, 8f, 2f), HarvestOffset = Quaternion.Euler(45f, 2f, -3f) };
            Quaternion baseline = WorldMacroPlayerGestureRig.FingerLocalRotation(rest, finger, 0f, 0f);
            for (int i = 0; i < 100; i++)
            {
                _ = WorldMacroPlayerGestureRig.FingerLocalRotation(rest, finger, 1f, 0f);
                _ = WorldMacroPlayerGestureRig.FingerLocalRotation(rest, finger, .5f, .6f);
                Quaternion returned = WorldMacroPlayerGestureRig.FingerLocalRotation(rest, finger, 0f, 0f);
                report.maximumFingerReturnDegrees = Mathf.Max(report.maximumFingerReturnDegrees, Quaternion.Angle(baseline, returned)); report.fingerCycles++;
            }
            report.invalidQuaternionRejected = !WorldMacroPlayerGestureProfile.IsUsableRotation(new Quaternion(0f, 0f, 0f, 0f))
                && !WorldMacroPlayerGestureProfile.IsUsableRotation(new Quaternion(float.NaN, 0f, 0f, 1f));
            report.zeroAxisFinite = WorldMacroPlayerGestureProfile.IsUsableRotation(WorldMacroPlayerGestureRig.GripRotation(Vector3.zero, Vector3.zero, Vector3.zero, 0f));
            report.status = report.maximumAxisError < .0001f && report.maximumFingerReturnDegrees < .001f
                && report.invalidQuaternionRejected && report.zeroAxisFinite ? "PASS_PURE_MATH_ONLY" : "FAIL";
            string json = JsonUtility.ToJson(report, true); Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "gesture_math.json"), json); return json;
        }

        private static string RuntimeInspection()
        {
            var rig = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            Need(rig != null, "No installed rerig gesture component in the active scene.");
            var animator = rig.GetComponent<Animator>(); var near = Find(rig.transform, "C02_NearArm");
            var fingerNames = rig.Profile != null ? rig.Profile.Fingers.Select(f => f.BoneName).ToArray() : Array.Empty<string>();
            BoneRow[] fingers = fingerNames.Select(name => Find(rig.transform, name)).Where(t => t != null).Select(t => new BoneRow
            { name = t.name, parent = t.parent != null ? t.parent.name : null, localPosition = t.localPosition, localRotation = t.localRotation,
                localScale = t.localScale, modelPosition = rig.transform.InverseTransformPoint(t.position),
                modelRotation = Quaternion.Inverse(rig.transform.rotation) * t.rotation }).ToArray();
            var report = new RuntimeReport
            {
                status = EditorApplication.isPlaying ? "RUNTIME_SNAPSHOT" : "EDIT_BIND_ONLY", utc = DateTime.UtcNow.ToString("o"),
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                profile = rig.Profile != null ? AssetDatabase.GetAssetPath(rig.Profile) : "MISSING", playMode = EditorApplication.isPlaying,
                rootMotionOff = animator != null && !animator.applyRootMotion, nearHasNoAnimator = near != null && near.GetComponentsInChildren<Animator>(true).Length == 0,
                allBonesFinite = rig.GetComponentsInChildren<Transform>(true).All(t => Finite(t.position) && Finite(t.localScale)
                    && WorldMacroPlayerGestureProfile.IsUsableRotation(t.rotation)), frame = Time.frameCount, timeScale = Time.timeScale,
                activeSkinningLeases = WorldMacroPlayerSkinningLease.ActiveLeaseCount, skinWeights = QualitySettings.skinWeights.ToString(),
                gesture = rig.Diagnostics, fingers = fingers
            };
            Directory.CreateDirectory(Output); string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(Output, "runtime_snapshot.json"), json); return json;
        }

        private static string CaptureCandidate(string view)
        {
            Need(new[] { "front", "back", "side", "hands", "hand-palm", "shoulder" }.Contains(view), "Unknown still view.");
            float commit = Prologue.PrologueAudit.CommitRatio(); Need(commit < .85f, "System commit is at or above 85%; no new capture allocated.");
            var rig = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
            Need(rig != null && walker != null && walker.ViewCamera != null, "Installed player and existing camera required.");
            Camera source = walker.ViewCamera; Transform body = walker.Body.transform;
            var hand = Find(rig.transform, "RightHand"); Need(hand != null, "Right hand missing.");
            Vector3 startCameraPosition = source.transform.position; Quaternion startCameraRotation = source.transform.rotation;
            Vector3 target = body.position + Vector3.up * .92f;
            Vector3 position;
            float fov = 38f;
            switch (view)
            {
                case "front": position = target + body.forward * 3.55f + Vector3.up * .05f; break;
                case "back": position = target - body.forward * 3.55f + Vector3.up * .05f; break;
                case "side": position = target + body.right * 3.55f + Vector3.up * .05f; break;
                case "hands": target = hand.position; position = target + body.forward * .42f + body.right * .20f + Vector3.up * .10f; fov = 42f; break;
                case "hand-palm": target = hand.position; position = target - body.forward * .37f - body.right * .20f - Vector3.up * .02f; fov = 42f; break;
                default: position = source.transform.position; target = position + source.transform.forward; fov = source.fieldOfView; break;
            }
            var report = new CaptureReport { status = "CAPTURING", utc = DateTime.UtcNow.ToString("o"), view = view,
                playMode = EditorApplication.isPlaying, cameraPosition = position, target = target, fieldOfView = fov, systemCommit = commit };
            string directory = Path.Combine(Output, "Screenshots"); Directory.CreateDirectory(directory);
            report.image = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "_" + view + ".png");
            Renderer[] renderers = rig.GetComponentsInChildren<Renderer>(true);
            bool[] visibility = renderers.Select(r => r.enabled).ToArray(); var shadows = renderers.Select(r => r.shadowCastingMode).ToArray();
            Transform near = Find(rig.transform, "C02_NearArm"), nearBrush = Find(rig.transform, "C02_NearBrush");
            GameObject cameraObject = null; Camera camera = null; RenderTexture rt = null; Texture2D pixels = null;
            var oldActive = RenderTexture.active; bool oldAsync = ShaderUtil.allowAsyncCompilation;
            try
            {
                if (view != "shoulder") for (int i = 0; i < renderers.Length; i++)
                {
                    bool close = (near != null && renderers[i].transform.IsChildOf(near)) || (nearBrush != null && renderers[i].transform.IsChildOf(nearBrush));
                    renderers[i].enabled = !close; renderers[i].shadowCastingMode = ShadowCastingMode.On;
                }
                ShaderUtil.allowAsyncCompilation = false;
                cameraObject = new GameObject("Temporary_C02ReRig_Still") { hideFlags = HideFlags.HideAndDontSave };
                camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
                camera.aspect = 1920f / 1080f; camera.fieldOfView = fov; camera.nearClipPlane = .02f;
                camera.useOcclusionCulling = false; camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
                if (view == "shoulder") camera.transform.rotation = source.transform.rotation;
                var sourceData = source.GetComponent<UniversalAdditionalCameraData>();
                if (sourceData != null) EditorUtility.CopySerialized(sourceData, camera.GetUniversalAdditionalCameraData());
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false, false);
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false); pixels.Apply(false, false);
                File.WriteAllBytes(report.image, pixels.EncodeToPNG()); report.status = "STILL_WRITTEN_VISUAL_REVIEW_PENDING";
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) { renderers[i].enabled = visibility[i]; renderers[i].shadowCastingMode = shadows[i]; }
                if (camera != null) camera.targetTexture = null; RenderTexture.active = oldActive; ShaderUtil.allowAsyncCompilation = oldAsync;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (pixels != null) Object.DestroyImmediate(pixels); if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                report.cameraTransformPreserved = Vector3.Distance(source.transform.position, startCameraPosition) < .000001f
                    && Quaternion.Angle(source.transform.rotation, startCameraRotation) < .0001f;
                report.temporaryResourcesReleased = cameraObject == null && rt == null && pixels == null;
                File.WriteAllText(Path.ChangeExtension(report.image, ".json"), JsonUtility.ToJson(report, true));
            }
            return JsonUtility.ToJson(report, true);
        }

        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    }
}
