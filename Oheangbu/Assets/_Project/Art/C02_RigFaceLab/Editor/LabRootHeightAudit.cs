using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.C02RigFaceLab.Editor
{
    public static class LabRootHeightAudit
    {
        [Serializable] class Settings { public string take; public bool lockRootHeightY, keepOriginalPositionY, heightFromFeet, lockRootRotation, keepOriginalOrientation; public float heightOffset, rotationOffset; }
        [Serializable] class Sample { public string take; public float time; public Vector3 min, max, root; public int vertices, triangles; public bool finite = true; public float maximumDeltaFromHumanPreserveY; }
        [Serializable] class Branch { public string name, asset, animationType, compression, method; public bool optimizeGameObjects, avatarValid, avatarHuman; public int shapes; public Settings[] settings; public Sample[] samples; }
        [Serializable] class Report { public string status = "MEASURED_NOT_ART_APPROVAL"; public string scope = "Independent byte-identical FBX branches: baseline Human, direct Generic source sampling, Human source-Y bake, and source-Y bake with compression Optimal / optimizeGameObjects. No root offsets, ground offsets, geometry or authored curves changed. Optimized comparison samples 16 poses, not all animation frames."; public Branch[] branches; public string qualityBefore, qualityAfter; }
        static string Prefix => LabEditor.AssetRoot + "/Models/RootHeightAudit";
        static string[] Takes = { "Armature|LAB_B1_Idle", "Armature|LAB_B1_Walk", "Armature|LAB_B1_Run", "Armature|LAB_B1_Attack" };
        public static string Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode only");
            string main = LabEditor.AssetRoot + "/Models/Integrated_B1_C3.fbx";
            var report = new Report { qualityBefore = QualitySettings.skinWeights.ToString() }; var old = QualitySettings.skinWeights;
            var result = new List<Branch>(); var reference = new Dictionary<string, Vector3[]>();
            try
            {
                for (int branch = 0; branch < 4; branch++)
                {
                    string asset = branch == 0 ? main : Prefix + "_" + branch + ".fbx";
                    if (branch != 0)
                    {
                        if (!File.Exists(asset)) { if (!AssetDatabase.CopyAsset(main, asset)) throw new IOException("Cannot create experiment import branch"); }
                        var change = (ModelImporter)AssetImporter.GetAtPath(asset);
                        change.animationType = branch == 1 ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Human;
                        change.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                        change.optimizeGameObjects = branch == 3; change.animationCompression = branch == 3 ? ModelImporterAnimationCompression.Optimal : ModelImporterAnimationCompression.Off;
                        if (branch >= 2)
                        {
                            var clips = change.clipAnimations.Length > 0 ? change.clipAnimations : change.defaultClipAnimations;
                            foreach (var clip in clips) { clip.lockRootHeightY = true; clip.keepOriginalPositionY = true; clip.heightFromFeet = false; }
                            change.clipAnimations = clips;
                        }
                        change.SaveAndReimport();
                    }
                    var importer = (ModelImporter)AssetImporter.GetAtPath(asset);
                    var clipSettings = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                    var row = new Branch { name = new[] { "Human_Original", "Generic_DirectOriginal", "Human_PreserveY", "Human_PreserveY_Optimized" }[branch], asset = asset, animationType = importer.animationType.ToString(), compression = importer.animationCompression.ToString(), optimizeGameObjects = importer.optimizeGameObjects, method = branch == 1 ? "Original Generic AnimationClip.SampleAnimation: authored bone curves, no Mecanim root extraction" : "Own Animator + imported Humanoid clip, applyRootMotion=false, 1x, CPU BakeMesh", settings = clipSettings.Where(c => Takes.Contains(c.name)).Select(c => new Settings { take = c.name, lockRootHeightY = c.lockRootHeightY, keepOriginalPositionY = c.keepOriginalPositionY, heightFromFeet = c.heightFromFeet, lockRootRotation = c.lockRootRotation, keepOriginalOrientation = c.keepOriginalOrientation, heightOffset = c.heightOffset, rotationOffset = c.rotationOffset }).ToArray() };
                    var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(asset)); model.name = "TemporaryRootAudit"; model.hideFlags = HideFlags.HideAndDontSave;
                    var animator = model.GetComponent<Animator>(); row.avatarValid = animator.avatar != null && animator.avatar.isValid; row.avatarHuman = animator.avatar != null && animator.avatar.isHuman;
                    var meshes = model.GetComponentsInChildren<SkinnedMeshRenderer>().OrderBy(r => r.name).ToArray(); row.shapes = meshes.Sum(r => r.sharedMesh.blendShapeCount);
                    var samples = new List<Sample>(); var baked = new Mesh(); var clipsAvailable = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<AnimationClip>().ToArray();
                    string controllerPath = LabEditor.AssetRoot + "/Controllers/RootHeightAudit_" + branch + ".controller";
                    if (AssetDatabase.LoadAssetAtPath<Object>(controllerPath) != null) AssetDatabase.DeleteAsset(controllerPath);
                    var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                    foreach (string take in Takes) { var state = controller.layers[0].stateMachine.AddState(take); state.motion = clipsAvailable.Single(c => c.name == take); state.writeDefaultValues = false; }
                    animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
                    QualitySettings.skinWeights = SkinWeights.Unlimited;
                    try
                    {
                        foreach (string take in Takes) foreach (float time in new[] { 0f, .25f, .5f, .75f })
                        {
                            var clip = clipsAvailable.Single(c => c.name == take);
                            if (branch == 1) { animator.enabled = false; clip.SampleAnimation(model, time * clip.length); }
                            else { animator.enabled = true; animator.Play(take, 0, time); animator.Update(0); }
                            var sample = new Sample { take = take, time = time, root = model.transform.position }; bool first = true; var all = new List<Vector3>();
                            foreach (var renderer in meshes)
                            {
                                renderer.quality = SkinQuality.Auto; renderer.BakeMesh(baked);
                                sample.triangles += (int)(renderer.sharedMesh.GetIndexCount(0) / 3);
                                foreach (var vertex in baked.vertices)
                                {
                                    var point = renderer.transform.TransformPoint(vertex); all.Add(point); sample.vertices++;
                                    sample.finite &= float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
                                    if (first) { sample.min = sample.max = point; first = false; } else { sample.min = Vector3.Min(sample.min, point); sample.max = Vector3.Max(sample.max, point); }
                                }
                            }
                            string key = take + "/" + time;
                            if (branch == 2) reference[key] = all.ToArray();
                            if (branch == 3 && reference.TryGetValue(key, out var baseline) && baseline.Length == all.Count) for (int i = 0; i < baseline.Length; i++) sample.maximumDeltaFromHumanPreserveY = Mathf.Max(sample.maximumDeltaFromHumanPreserveY, Vector3.Distance(baseline[i], all[i]));
                            samples.Add(sample);
                        }
                    }
                    finally { QualitySettings.skinWeights = old; Object.DestroyImmediate(baked); Object.DestroyImmediate(model); }
                    row.samples = samples.ToArray(); result.Add(row);
                }
            }
            finally { QualitySettings.skinWeights = old; report.qualityAfter = QualitySettings.skinWeights.ToString(); }
            report.branches = result.ToArray(); string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(LabEditor.OutputRoot, "root_height_and_optimization_audit.json"), json); return json;
        }
    }
}
