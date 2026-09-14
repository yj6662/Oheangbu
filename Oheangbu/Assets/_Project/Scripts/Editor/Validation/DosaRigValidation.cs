using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>실제 수입 Avatar/소켓/스킨을 평가한다. 저장된 리그나 실행 중 플레이어에는 쓰지 않는다.</summary>
    public static class DosaRigValidation
    {
        [Serializable] private sealed class Case
        {
            public float fov, pitch, aspect;
            public int simulatedFps, x, y;
            public float tipPixels, gripMeters;
            public bool reachable;
        }
        [Serializable] private sealed class ClipReport
        {
            public string name;
            public int sampledPoses;
            public Vector3 largestBounds;
            public bool finiteAndBounded = true;
        }
        [Serializable] private sealed class Report
        {
            public string status, note;
            public int cases, failedChecks, clips, sampledClipPoses;
            public float maxTipErrorPixels, maxGripErrorMeters, maxBoneLengthDeltaMeters;
            public float maxShoulderMotionDegrees, maxChestMotionDegrees, maxHipMotionDegrees, maxArmMotionDegrees;
            public float smallStrokeChestDegrees, largeStrokeChestDegrees;
            public Case worstTipCase, worstGripCase;
            public List<string> failures = new List<string>();
            public List<ClipReport> clipReports = new List<ClipReport>();
        }

        public static string Run()
        {
            var report = new Report { note = "Actual imported Humanoid/socket tests with deterministic 30/60/120 Hz evaluation steps. This is not an FPS benchmark, perceptual quality approval, or locomotion foot-slide certification." };
            if (!Application.isPlaying) return "NOT_RUN: Play Mode required";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DosaPlayerBuilder.VisualPrefabPath);
            if (prefab == null) return "NOT_RUN: PF_DosaVisual has not been built";
            var randomState = UnityEngine.Random.state;
            try
            {
                using (var f = new Fixture(prefab))
                {
                    Check(report, f.World.isHuman && f.Near.isHuman && f.World.avatar.isValid && f.Near.avatar.isValid, "Both avatars must be valid Humanoid");
                    foreach (float aspect in new[] { 4f / 3f, 16f / 9f, 21f / 9f })
                    {
                        f.SetAspect(aspect);
                        foreach (float fov in new[] { 52f, 60f })
                        foreach (float pitch in new[] { -80f, -35f, 0f, 60f, 80f })
                        foreach (int fps in new[] { 30, 60, 120 })
                        {
                            f.Camera.fieldOfView = fov;
                            f.Camera.transform.SetPositionAndRotation(f.Root.transform.position + new Vector3(.35f, 1.7f, -.2f), Quaternion.Euler(pitch, 31f, 0f));
                            for (int x = 0; x <= 4; x++)
                            for (int y = 0; y <= 4; y++)
                            {
                                var input = f.Frame(new Vector2(x / 4f, y / 4f), true);
                                f.Step(input, 1f / fps);
                                var d = f.Rig.Diagnostics;
                                var sample = new Case { fov = fov, pitch = pitch, aspect = aspect, simulatedFps = fps,
                                    x = x, y = y, tipPixels = d.TipScreenErrorPixels, gripMeters = d.GripErrorMeters, reachable = d.TipReachable };
                                report.cases++;
                                if (report.worstTipCase == null || d.TipScreenErrorPixels > report.maxTipErrorPixels)
                                { report.maxTipErrorPixels = d.TipScreenErrorPixels; report.worstTipCase = sample; }
                                if (report.worstGripCase == null || d.GripErrorMeters > report.maxGripErrorMeters)
                                { report.maxGripErrorMeters = d.GripErrorMeters; report.worstGripCase = sample; }
                                string label = $"aspect={aspect:F3} FOV={fov} pitch={pitch} step=1/{fps} grid={x},{y}";
                                Check(report, d.TipReachable, "Reach envelope failed: " + label);
                                Check(report, float.IsFinite(d.TipScreenErrorPixels) && d.TipScreenErrorPixels >= 0f && d.TipScreenErrorPixels <= 2f,
                                    $"Actual socket tip error {d.TipScreenErrorPixels:F4}px: " + label);
                                Check(report, d.GripErrorMeters <= .001f, $"Hand grip detached {d.GripErrorMeters:F6}m: " + label);
                                Check(report, Mathf.Abs(f.BrushLength - .28f) <= .001f, "Fixed brush socket length changed: " + label);
                                Check(report, f.Camera.WorldToScreenPoint(d.BrushTipWorld).z > 0f, "Tip behind camera: " + label);
                                float lengthDelta = f.BoneLengthDelta();
                                report.maxBoneLengthDeltaMeters = Mathf.Max(report.maxBoneLengthDeltaMeters, lengthDelta);
                                Check(report, lengthDelta <= .001f, $"Arm/leg length changed {lengthDelta:F6}m: " + label);
                                Check(report, f.Root.transform.position == f.RootPosition && f.World.transform.position == f.WorldPosition
                                    && Quaternion.Angle(f.Root.transform.rotation, f.RootRotation) < .001f, "Visual solver moved owning root: " + label);
                                Check(report, f.WorldRenderers.All(r => r.enabled && r.shadowCastingMode == ShadowCastingMode.ShadowsOnly), "World body visible during near drawing: " + label);
                                Check(report, f.NearRenderers.All(r => r.enabled && r.shadowCastingMode == ShadowCastingMode.Off), "Near arms missing or duplicate shadow: " + label);
                                report.maxShoulderMotionDegrees = Mathf.Max(report.maxShoulderMotionDegrees, f.LastShoulderDelta);
                                report.maxChestMotionDegrees = Mathf.Max(report.maxChestMotionDegrees, f.LastChestDelta);
                                report.maxHipMotionDegrees = Mathf.Max(report.maxHipMotionDegrees, f.LastHipDelta);
                                report.maxArmMotionDegrees = Mathf.Max(report.maxArmMotionDegrees, f.LastArmDelta);
                            }
                        }
                    }
                    Check(report, report.maxShoulderMotionDegrees > .05f && report.maxChestMotionDegrees > .05f
                        && report.maxHipMotionDegrees > .02f && report.maxArmMotionDegrees > 1f,
                        "World shoulder/chest/pelvis/arm did not all respond to drawing");
                    CheckStrokeAmplitude(report, f);
                }
                CheckClipBounds(report);
            }
            catch (Exception exception) { Check(report, false, "Audit exception: " + exception); }
            finally { UnityEngine.Random.state = randomState; }
            report.status = report.failedChecks == 0 ? "PASS" : "FAIL";
            string directory = Path.GetFullPath("Screenshots/PlayerDosa");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "rig-validation.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return $"{report.status}: cases={report.cases}; clips={report.clips}/{DosaPlayerBuilder.MotionNames.Length}; maxTip={report.maxTipErrorPixels:F5}px; grip={report.maxGripErrorMeters:F7}m; boneDelta={report.maxBoneLengthDeltaMeters:F7}m; failedChecks={report.failedChecks}; worstTip={JsonUtility.ToJson(report.worstTipCase)}; report={path}";
        }

        private static void CheckStrokeAmplitude(Report report, Fixture f)
        {
            f.Camera.transform.rotation = Quaternion.identity;
            Vector2 end = new Vector2(.8f, .65f);
            for (int i = 0; i < 40; i++) f.Step(f.Frame(end, false), 1f / 60f);
            f.Step(f.Frame(end - new Vector2(.01f, .01f), true), 1f / 60f);
            for (int i = 0; i < 40; i++) f.Step(f.Frame(end, true), 1f / 60f);
            report.smallStrokeChestDegrees = f.LastChestDelta;
            for (int i = 0; i < 40; i++) f.Step(f.Frame(end, false), 1f / 60f);
            f.Step(f.Frame(new Vector2(.1f, .2f), true), 1f / 60f);
            for (int i = 0; i < 40; i++) f.Step(f.Frame(end, true), 1f / 60f);
            report.largeStrokeChestDegrees = f.LastChestDelta;
            Check(report, report.largeStrokeChestDegrees > report.smallStrokeChestDegrees * 1.25f,
                "Large stroke must engage chest more than tiny stroke ending at same screen point");
        }

        private static void CheckClipBounds(Report report)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(DosaPlayerBuilder.WorldModel);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DosaPlayerBuilder.ControllerPath);
            if (source == null || controller == null) { Check(report, false, "Missing imported model/controller for clip audit"); return; }
            GameObject clone = null; AnimatorOverrideController overrides = null; Mesh mesh = null;
            try
            {
                clone = Object.Instantiate(source); clone.name = "DosaClipAudit_Temporary"; clone.hideFlags = HideFlags.HideAndDontSave;
                clone.transform.SetPositionAndRotation(new Vector3(70f, 0f, 70f), Quaternion.identity);
                var animator = clone.GetComponent<Animator>() ?? clone.AddComponent<Animator>();
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(DosaPlayerBuilder.WorldModel).OfType<Avatar>().First(a => a.isHuman && a.isValid);
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                overrides = new AnimatorOverrideController(controller); animator.runtimeAnimatorController = overrides;
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); overrides.GetOverrides(pairs);
                var renderers = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                mesh = new Mesh { name = "DosaClipAudit_Baked" };
                foreach (string name in DosaPlayerBuilder.MotionNames)
                {
                    var clip = AssetDatabase.LoadAllAssetsAtPath(DosaPlayerBuilder.ClipPath(name)).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                    var result = new ClipReport { name = name }; report.clipReports.Add(result);
                    if (clip == null) { result.finiteAndBounded = false; Check(report, false, "Missing clip " + name); continue; }
                    Check(report, clip.humanMotion, "Imported clip is not Humanoid motion: " + name);
                    for (int i = 0; i < pairs.Count; i++) pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, clip);
                    overrides.ApplyOverrides(pairs); animator.Rebind(); animator.Update(0f);
                    animator.SetLayerWeight(1, 0f);
                    animator.SetBool("Drawing", false); animator.SetBool("Dodge", false); animator.SetBool("Combat", false);
                    animator.SetFloat("MoveX", 0f); animator.SetFloat("MoveY", 0f);
                    foreach (float phase in new[] { 0f, .25f, .5f, .75f, .99f })
                    {
                        animator.Play("Locomotion.Locomotion", 0, phase); animator.Update(0f);
                        Bounds combined = default; bool first = true;
                        foreach (var renderer in renderers)
                        {
                            renderer.BakeMesh(mesh); mesh.RecalculateBounds();
                            Bounds bounds = mesh.bounds;
                            for (int corner = 0; corner < 8; corner++)
                            {
                                Vector3 p = bounds.center + Vector3.Scale(bounds.extents,
                                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                                p = clone.transform.InverseTransformPoint(renderer.transform.TransformPoint(p));
                                if (first) { combined = new Bounds(p, Vector3.zero); first = false; } else combined.Encapsulate(p);
                            }
                        }
                        Vector3 size = combined.size;
                        bool sane = !first && float.IsFinite(size.x) && float.IsFinite(size.y) && float.IsFinite(size.z)
                            && size.x > .05f && size.y > .3f && size.z > .05f
                            && size.x < 4f && size.y < 3f && size.z < 4f && combined.min.y > -.5f && combined.max.y < 3f;
                        result.finiteAndBounded &= sane;
                        result.largestBounds = Vector3.Max(result.largestBounds, size);
                        result.sampledPoses++; report.sampledClipPoses++;
                        Check(report, sane, $"Humanoid baked mesh exploded/vanished in {name} phase={phase:F2} bounds={combined}");
                    }
                    report.clips++;
                }
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone);
                if (overrides != null) Object.DestroyImmediate(overrides);
                if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        private static void Check(Report report, bool pass, string message)
        {
            if (pass) return;
            report.failedChecks++;
            if (report.failures.Count < 64) report.failures.Add(message);
        }

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly PlayerVisualRig Rig;
            public readonly Animator World, Near;
            public readonly Camera Camera;
            public readonly Renderer[] WorldRenderers, NearRenderers;
            public readonly Vector3 RootPosition, WorldPosition;
            public readonly Quaternion RootRotation;
            private readonly GameObject _cameraObject;
            private RenderTexture _target;
            private readonly Transform[] _starts, _ends;
            private readonly float[] _lengths;
            private readonly Transform _shoulder, _chest, _hips, _arm;
            private readonly Transform _brushGrip, _brushTip;
            public float LastShoulderDelta, LastChestDelta, LastHipDelta, LastArmDelta;
            public float BrushLength => Vector3.Distance(_brushGrip.position, _brushTip.position);

            public Fixture(GameObject prefab)
            {
                try
                {
                Root = Object.Instantiate(prefab); Root.name = "DosaRigAudit_Temporary";
                Root.hideFlags = HideFlags.HideAndDontSave;
                Root.transform.SetPositionAndRotation(new Vector3(30f, 0f, 30f), Quaternion.identity);
                Rig = Root.GetComponent<PlayerVisualRig>();
                World = Root.transform.Find("WorldBody").GetComponent<Animator>();
                Near = Root.transform.Find("NearArms").GetComponent<Animator>();
                WorldRenderers = World.GetComponentsInChildren<Renderer>(true).Concat(Root.transform.Find("WorldBrush").GetComponentsInChildren<Renderer>(true)).ToArray();
                NearRenderers = Near.GetComponentsInChildren<Renderer>(true).Concat(Root.transform.Find("NearBrush").GetComponentsInChildren<Renderer>(true)).ToArray();
                _brushGrip = Root.transform.Find("NearBrush/GripSocket");
                _brushTip = Root.transform.Find("NearBrush/TipSocket");
                _cameraObject = new GameObject("DosaRigAudit_Camera"); _cameraObject.hideFlags = HideFlags.HideAndDontSave;
                Camera = _cameraObject.AddComponent<Camera>(); Camera.enabled = false; Camera.nearClipPlane = .3f;
                Camera.farClipPlane = 1000f; Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = new Color(.4f,.4f,.4f);
                World.Rebind(); Near.Rebind(); World.Update(0f); Near.Update(0f);
                RootPosition = Root.transform.position; RootRotation = Root.transform.rotation; WorldPosition = World.transform.position;
                _shoulder = World.GetBoneTransform(HumanBodyBones.RightShoulder);
                _chest = World.GetBoneTransform(HumanBodyBones.Chest);
                _hips = World.GetBoneTransform(HumanBodyBones.Hips);
                _arm = World.GetBoneTransform(HumanBodyBones.RightUpperArm);
                var starts = new List<Transform>(); var ends = new List<Transform>();
                foreach (var a in new[] { World, Near })
                {
                    AddPair(a, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, starts, ends);
                    AddPair(a, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, starts, ends);
                    AddPair(a, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, starts, ends);
                    AddPair(a, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, starts, ends);
                    AddPair(a, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, starts, ends);
                    AddPair(a, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, starts, ends);
                }
                _starts = starts.ToArray(); _ends = ends.ToArray(); _lengths = new float[_starts.Length];
                for (int i = 0; i < _starts.Length; i++) _lengths[i] = Vector3.Distance(_starts[i].position, _ends[i].position);
                }
                catch { Dispose(); throw; }
            }

            private static void AddPair(Animator a, HumanBodyBones from, HumanBodyBones to, List<Transform> starts, List<Transform> ends)
            {
                var start = a.GetBoneTransform(from); var end = a.GetBoneTransform(to);
                if (start != null && end != null) { starts.Add(start); ends.Add(end); }
            }

            public void SetAspect(float aspect)
            {
                if (_target != null) { Camera.targetTexture = null; _target.Release(); Object.DestroyImmediate(_target); }
                _target = new RenderTexture(Mathf.RoundToInt(1080f * aspect), 1080, 24);
                _target.Create(); Camera.targetTexture = _target; Camera.aspect = aspect;
            }

            public PlayerVisualFrame Frame(Vector2 viewport, bool stroking)
            {
                return new PlayerVisualFrame { Camera = Camera, HasPointer = true,
                    PointerScreen = Camera.ViewportToScreenPoint(viewport), Drawing = true, Stroking = stroking,
                    Combat = true, Grounded = false, WorldBodyVisible = true };
            }

            public void Step(PlayerVisualFrame frame, float dt)
            {
                Rig.UpdateMotion(frame, dt);
                World.Update(dt * Mathf.Max(Time.timeScale, .01f)); Near.Update(dt * Mathf.Max(Time.timeScale, .01f));
                Quaternion shoulder = _shoulder.rotation, chest = _chest.rotation, hips = _hips.rotation, arm = _arm.rotation;
                Rig.ApplyFrame(frame, dt);
                LastShoulderDelta = Quaternion.Angle(shoulder, _shoulder.rotation);
                LastChestDelta = Quaternion.Angle(chest, _chest.rotation);
                LastHipDelta = Quaternion.Angle(hips, _hips.rotation);
                LastArmDelta = Quaternion.Angle(arm, _arm.rotation);
            }

            public float BoneLengthDelta()
            {
                float max = 0f;
                for (int i = 0; i < _starts.Length; i++) max = Mathf.Max(max, Mathf.Abs(Vector3.Distance(_starts[i].position, _ends[i].position) - _lengths[i]));
                return max;
            }

            public void Dispose()
            {
                if (Camera != null) Camera.targetTexture = null;
                if (_target != null) { _target.Release(); Object.DestroyImmediate(_target); }
                if (_cameraObject != null) Object.DestroyImmediate(_cameraObject);
                if (Root != null) Object.DestroyImmediate(Root);
            }
        }
    }
}
