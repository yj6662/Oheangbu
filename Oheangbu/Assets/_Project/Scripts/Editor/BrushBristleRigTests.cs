using System;
using Oheangbu.Data;
using Oheangbu.Presentation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>실제 Transform 체인의 길이/소켓/호출 순서 회귀. 임시 오브젝트만 생성한다.</summary>
    public static class BrushBristleRigTests
    {
        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly BrushDeformationProfileSO Profile;
            public readonly BrushBristleRig Rig;
            public readonly Transform Grip, Tip;
            public readonly Transform[] Bones = new Transform[6];
            public readonly float[] Lengths = { .028f, .026f, .024f, .022f, .019f, .016f };
            public readonly Vector3[] RestLocalPositions = new Vector3[6];
            public readonly Vector3 GripPosition;
            public readonly Quaternion GripRotation;
            public readonly Mesh Mesh;
            public readonly SkinnedMeshRenderer Skin;

            public Fixture(Quaternion restRotation, bool withSplay)
            {
                Root = new GameObject("BrushBristleRigTests_Temporary") { hideFlags = HideFlags.HideAndDontSave };
                Profile = ScriptableObject.CreateInstance<BrushDeformationProfileSO>();
                Profile.hideFlags = HideFlags.HideAndDontSave;
                Grip = Child("GripSocket", Root.transform);
                Grip.localPosition = new Vector3(.017f, .11f, -.013f);
                Grip.localRotation = Quaternion.Euler(5f, 13f, -9f);
                GripPosition = Grip.localPosition; GripRotation = Grip.localRotation;
                var ferrule = Child("RigidShaft", Root.transform);
                ferrule.localPosition = new Vector3(0f, .65f, 0f);
                ferrule.localRotation = restRotation;
                for (int i = 0; i < 6; i++)
                {
                    Bones[i] = Child("Bristle_" + i, i == 0 ? ferrule : Bones[i - 1]);
                    if (i > 0) Bones[i].localPosition = Vector3.up * Lengths[i - 1];
                    RestLocalPositions[i] = Bones[i].localPosition;
                }
                Tip = Child("TipSocket", Bones[5]); Tip.localPosition = Vector3.up * Lengths[5];
                if (withSplay)
                {
                    Skin = Child("Bristles", Root.transform).gameObject.AddComponent<SkinnedMeshRenderer>();
                    Mesh = new Mesh { name = "Temporary radial splay", hideFlags = HideFlags.HideAndDontSave };
                    Mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right * .01f };
                    Mesh.triangles = new[] { 0, 1, 2 };
                    Mesh.AddBlendShapeFrame("BristleSplay", 100f,
                        new[] { Vector3.zero, Vector3.zero, Vector3.right * .004f }, new Vector3[3], new Vector3[3]);
                    Skin.sharedMesh = Mesh;
                }
                Rig = Root.AddComponent<BrushBristleRig>();
                Require(Rig.Configure(Profile, Grip, Tip, Bones, Skin), Rig.BindingError);
            }

            public float CheckGeometry()
            {
                float worst = 0f;
                for (int i = 0; i < Bones.Length; i++)
                {
                    Vector3 next = i == 5 ? Tip.position : Bones[i + 1].position;
                    float error = Mathf.Abs(Vector3.Distance(Bones[i].position, next) - Lengths[i]);
                    worst = Mathf.Max(worst, error);
                    Require(error < .00002f, "a bristle segment stretched");
                    Require(Vector3.Distance(Bones[i].localPosition, RestLocalPositions[i]) < .00002f, "bind joint offset changed");
                    Require(Vector3.Distance(Bones[i].localScale, Vector3.one) < .000001f, "bone scale changed");
                }
                Require(Vector3.Distance(Grip.localPosition, GripPosition) < .000001f, "rig moved rigid grip");
                Require(Quaternion.Angle(Grip.localRotation, GripRotation) < .001f, "rig rotated rigid grip");
                Require(Vector3.Distance(Root.transform.InverseTransformPoint(Tip.position), Rig.Pose.TipPositionLocal) < .00002f,
                    "actual TipSocket differs from pre-IK evaluated endpoint");
                return worst;
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Root);
                if (Mesh != null) Object.DestroyImmediate(Mesh);
                Object.DestroyImmediate(Profile);
            }
        }

        public static string Run()
        {
            int samples = 0;
            float worstLength = 0f, worstRateDifference = 0f;
            Vector3[] checkpoints = null;
            foreach (int hz in new[] { 30, 60, 120 })
            using (var f = new Fixture(Quaternion.identity, true))
            {
                Require(f.Rig.SplayAvailable, "authored splay shape was not bound");
                var result = new Vector3[3];
                Vector3[] velocities = { new Vector3(1.2f, 0f, .4f), new Vector3(-.8f, 0f, 1f), Vector3.zero };
                for (int phase = 0; phase < 3; phase++)
                {
                    for (int frame = 0; frame < hz / 2; frame++)
                    {
                        Vector3 previousTip = f.Tip.position;
                        f.Rig.EvaluateLocal(velocities[phase], phase < 2, 1f / hz);
                        Require(f.Tip.position == previousTip, "Evaluate wrote scene transforms before IK");
                        Require(f.Rig.ApplyPose(), "pose application failed");
                        worstLength = Mathf.Max(worstLength, f.CheckGeometry());
                        samples++;
                    }
                    result[phase] = f.Rig.Pose.TipPositionLocal;
                    Require(Mathf.Abs(f.Skin.GetBlendShapeWeight(0) - f.Rig.Pose.Splay * 100f) < .0001f,
                        "splay shape did not follow evaluated pose");
                }
                if (checkpoints == null) checkpoints = result;
                else for (int i = 0; i < result.Length; i++)
                {
                    float error = Vector3.Distance(result[i], checkpoints[i]);
                    worstRateDifference = Mathf.Max(worstRateDifference, error);
                    Require(error < .00002f, "30/60/120 Hz constant-drive/reversal/recovery diverged");
                }
                Require(f.Rig.Pose.BendDegrees < .1f && f.Rig.Pose.Splay < .005f, "pen-up did not recover");
                f.Rig.ResetDeformation();
                Require(Vector3.Distance(f.Rig.Pose.TipPositionLocal, f.Rig.RestTipPositionLocal) < .000001f, "reset lost rest endpoint");
                f.Rig.EvaluateLocal(Vector3.right, true, 0.2f); f.Rig.ApplyPose();
                var frozen = f.Rig.Pose;
                f.Rig.EvaluateLocal(Vector3.left, true, 0f);
                Require(Vector3.Distance(f.Rig.Pose.TipPositionLocal, frozen.TipPositionLocal) < .000001f, "zero dt advanced state");
                f.Rig.EvaluateLocal(new Vector3(float.NaN, 0f, 0f), true, 1f / hz); f.Rig.ApplyPose();
                f.CheckGeometry();
                f.Rig.EvaluateLocal(Vector3.right, true, 1f); f.Rig.ApplyPose();
                Require(f.Rig.Pose.BendDegrees == 0f, "long context gap kept stale deflection");
                f.Rig.EvaluateLocal(Vector3.right, true, .2f); f.Rig.ApplyPose();
                Vector3 originalRest = f.Rig.RestTipPositionLocal;
                Require(f.Rig.TryBind(), "valid active chain could not rebind");
                Require(Vector3.Distance(f.Rig.Pose.TipPositionLocal, originalRest) < .00002f,
                    "rebind captured deformed pose as rest");
                f.Root.SetActive(false);
                Require(f.Rig.Pose.Splay == 0f, "disable did not reset splay");
            }

            using (var near = new Fixture(Quaternion.Euler(23f, 11f, 77f), false))
            using (var world = new Fixture(Quaternion.Euler(23f, 11f, 77f), false))
            {
                Require(!near.Rig.SplayAvailable, "missing blendshape falsely reported available");
                Quaternion reference = Quaternion.Euler(19f, 73f, -15f);
                Quaternion desiredGrip = Quaternion.Euler(37f, 9f, 12f);
                Quaternion changeOfFrame = Quaternion.Euler(-71f, 29f, 3f);
                Vector3 desiredTip = new Vector3(.3f, 1.4f, 2.1f);
                for (int i = 0; i < 90; i++)
                {
                    Vector3 speed = new Vector3(Mathf.Sin(i * .17f), Mathf.Cos(i * .17f), 0f);
                    var a = near.Rig.EvaluateInFrame(speed, reference, desiredGrip, true, 1f / 60f);
                    var b = world.Rig.EvaluateInFrame(speed, changeOfFrame * reference, changeOfFrame * desiredGrip, true, 1f / 60f);
                    Require(Vector3.Distance(a.TipPositionLocal, b.TipPositionLocal) < .00002f, "near/world reference rotation changed deformation");
                    // Emulate the integration contract: solve rigid grip from the already deformed tip.
                    Quaternion rootRotation = desiredGrip * Quaternion.Inverse(near.Rig.GripRotationLocal);
                    Vector3 gripGoal = desiredTip - desiredGrip * a.TipOffsetInGripLocal;
                    near.Root.transform.SetPositionAndRotation(gripGoal - rootRotation * near.GripPosition, rootRotation);
                    near.Rig.ApplyPose();
                    Require(Vector3.Distance(near.Tip.position, desiredTip) < .00002f, "deformed tip-to-grip inverse solve missed its target");
                    worstLength = Mathf.Max(worstLength, near.CheckGeometry());
                    samples++;
                }
            }
            using (var invalid = new Fixture(Quaternion.identity, false))
            {
                Vector3 rest = invalid.Rig.RestTipPositionLocal;
                invalid.Rig.EvaluateLocal(Vector3.right, true, .2f); invalid.Rig.ApplyPose();
                Require(!invalid.Rig.Configure(invalid.Profile, null, invalid.Tip, invalid.Bones), "missing grip accepted");
                Require(Vector3.Distance(invalid.Root.transform.InverseTransformPoint(invalid.Tip.position), rest) < .00002f,
                    "failed reconfigure left previous chain deformed");
                Require(invalid.Rig.Configure(invalid.Profile, invalid.Grip, invalid.Tip, invalid.Bones), "restored chain failed to bind");
                invalid.Root.transform.localScale = Vector3.one * 2f;
                Require(!invalid.Rig.TryBind(), "scaled root should fail the fixed world-length contract");
                invalid.Root.transform.localScale = Vector3.one;
                var wrongChain = (Transform[])invalid.Bones.Clone(); wrongChain[2] = wrongChain[1];
                Require(!invalid.Rig.Configure(invalid.Profile, invalid.Grip, invalid.Tip, wrongChain), "duplicate bone chain accepted");
            }
            return $"PASS BrushBristleRig: {samples} actual-chain samples; max segment drift={worstLength:F8}m, "
                + $"30/60/120Hz checkpoint delta={worstRateDifference:F8}m; pre-IK evaluation, fixed offsets/scale/grip, "
                + "rotated bind axes, near/world covariance, deformed-tip inverse placement, splay, pen-up, reset, invalid rig checks.";
        }

        private static Transform Child(string name, Transform parent)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("BrushBristleRigTests: " + message);
        }
    }
}
