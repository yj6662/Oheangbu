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
    public static class DosaGripValidation
    {
        [Serializable] private sealed class Variant
        {
            public string name, screenshot;
            public Vector3 index1InGrip, index2InGrip, index3InGrip;
            public float meanDifferenceFromAuthoredFingersDegrees;
            public Quaternion[] rotations;
        }

        public static string Run()
        {
            if (!Application.isPlaying) return "NOT_RUN: Play Mode required";
            string directory = Path.GetFullPath("Screenshots/PlayerDosa/Grip"); Directory.CreateDirectory(directory);
            var variants = new List<Variant>();
            var randomState = UnityEngine.Random.state;
            try
            {
                variants.Add(Capture("authored_animation", 0f, Vector3.left, directory));
                variants.Add(Capture("negative_x", 1f, Vector3.left, directory));
                variants.Add(Capture("positive_x", 1f, Vector3.right, directory));
                variants.Add(Capture("negative_x_roll180", 1f, Vector3.left, directory, 180f));
                variants.Add(Capture("negative_x_roll90", 1f, Vector3.left, directory, 90f));
                variants.Add(Capture("negative_x_roll_minus90", 1f, Vector3.left, directory, -90f));
                foreach (var variant in variants)
                {
                    float sum = 0f;
                    for (int i = 0; i < variant.rotations.Length; i++) sum += Quaternion.Angle(variants[0].rotations[i], variant.rotations[i]);
                    variant.meanDifferenceFromAuthoredFingersDegrees = sum / variant.rotations.Length;
                    File.WriteAllText(Path.Combine(directory, variant.name + ".json"), JsonUtility.ToJson(variant, true));
                }
                return string.Join("\n", variants.Select(v => v.name + " authoredMeanDelta=" + v.meanDifferenceFromAuthoredFingersDegrees.ToString("F3")
                    + "deg index1=" + v.index1InGrip.ToString("F4") + " index2=" + v.index2InGrip.ToString("F4") + " index3=" + v.index3InGrip.ToString("F4") + " " + v.screenshot));
            }
            finally { UnityEngine.Random.state = randomState; }
        }

        private static Variant Capture(string name, float gripWeight, Vector3 curlAxis, string directory, float brushRoll = 0f)
        {
            GameObject root = null, cameraObject = null;
            DrawingPoseProfileSO pose = null;
            RenderTexture target = null; Texture2D pixels = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(DosaPlayerBuilder.VisualPrefabPath);
                root = Object.Instantiate(source); root.name = "DosaGripAudit_Temporary"; root.hideFlags = HideFlags.HideAndDontSave;
                root.transform.position = new Vector3(300f,0f,300f);
                foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
                var rig = root.GetComponent<PlayerVisualRig>();
                var world = root.transform.Find("WorldBody").GetComponent<Animator>();
                var near = root.transform.Find("NearArms").GetComponent<Animator>();
                pose = Object.Instantiate(AssetDatabase.LoadAssetAtPath<DrawingPoseProfileSO>(DosaPlayerBuilder.DrawingPath));
                pose.GripWeight = gripWeight; pose.FingerCurlAxis = curlAxis;
                pose.BrushRollDegrees = brushRoll;
                var visual = AssetDatabase.LoadAssetAtPath<PlayerVisualProfileSO>(DosaPlayerBuilder.VisualPath);
                rig.Configure(world, near, root.transform.Find("WorldBrush"), root.transform.Find("NearBrush"), visual, pose);
                cameraObject = new GameObject("DosaGripAudit_Camera"); cameraObject.hideFlags = HideFlags.HideAndDontSave;
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.transform.SetPositionAndRotation(root.transform.position + new Vector3(0f,1.7f,0f), Quaternion.identity);
                camera.fieldOfView = 52f; camera.nearClipPlane = .01f; camera.farClipPlane = 20f;
                camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.4f,.4f,.4f);
                target = new RenderTexture(1280, 960, 24); target.Create(); camera.targetTexture = target;
                world.Rebind(); near.Rebind(); world.Update(0f); near.Update(0f);
                var frame = new PlayerVisualFrame { Camera = camera, HasPointer = true,
                    PointerScreen = camera.ViewportToScreenPoint(new Vector3(.57f,.55f,0f)), Drawing = true, Stroking = true, Combat = true };
                for (int i = 0; i < 90; i++)
                {
                    rig.UpdateMotion(frame, 1f / 60f);
                    world.Update(1f / 60f); near.Update(1f / 60f);
                    rig.ApplyFrame(frame, 1f / 60f);
                }
                var bones = near.GetComponentsInChildren<Transform>(true);
                Transform Find(string boneName) => bones.First(t => t.name.EndsWith(boneName, StringComparison.Ordinal));
                var grip = Find("RightBrushGrip");
                var result = new Variant { name = name, screenshot = Path.Combine(directory, name + ".png"),
                    index1InGrip = grip.InverseTransformPoint(Find("RightHandIndex1").position),
                    index2InGrip = grip.InverseTransformPoint(Find("RightHandIndex2").position),
                    index3InGrip = grip.InverseTransformPoint(Find("RightHandIndex3").position) };
                var rotations = new List<Quaternion>();
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
                    for (int joint = 1; joint <= 3; joint++) rotations.Add(Find("RightHand" + finger + joint).localRotation);
                result.rotations = rotations.ToArray();
                camera.Render(); RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0f,0f,target.width,target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(result.screenshot, pixels.EncodeToPNG());
                return result;
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
    }
}
