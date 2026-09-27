using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.Demo;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    public static class DemoCheongryongTailSweepChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Imported skin deformation at Body_12 bend", "Ground contact and tail/body self-intersection",
                "Visual sweep versus actual combat contact in Play mode", "Navigation displacement of the locked attack origin", "Runtime performance" };
        }
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run tail pose checks in Edit mode.");
            var report = new Report(); void Check(bool pass, string name) => (pass ? report.passed : report.failed).Add(name);
            try { Pure(Check); } catch (Exception e) { report.failed.Add("Pure tail arc: " + e); }
            Scene scene = EditorSceneManager.NewPreviewScene();
            try { Hierarchy(scene, Check); } catch (Exception e) { report.failed.Add("Tail layer: " + e); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(report, true);
        }

        private static void Pure(Action<bool, string> check)
        {
            CheongryongTailPhase Phase(float now, bool cancel = false) => CheongryongTailPoseMath.Phase(now, 10, 11, 12, 13, 75, cancel);
            check(Near(Phase(10).Weight, 0) && Near(Phase(10.5f).Weight, .5f), "Windup smoothly loads the tail from its current follow pose");
            check(Near(Phase(11).AngleDegrees, -75) && Near(Phase(11.5f).AngleDegrees, 0) && Near(Phase(12).AngleDegrees, 75),
                "Active interval sweeps locked negative sector edge through center to positive edge");
            check(Near(Phase(12.5f).Weight, .5f) && Near(Phase(13).Weight, 0) && Near(Phase(11.5f, true).Weight, 0),
                "Recovery and cancellation have bounded zero-weight endpoints");
            bool stable = true; var paused = Phase(11.25f);
            for (int i = 0; i < 1000; i++) stable &= Near(Phase(11.25f).AngleDegrees, paused.AngleDegrees) && Near(Phase(11.25f).Weight, 1);
            check(stable && Near(CheongryongTailPoseMath.Phase(float.NaN, 10, 11, 12, 13, 75).Weight, 0), "Paused and invalid clocks cannot advance or corrupt the sweep");
            var baseline = new Vector3[14]; var output = new Vector3[14]; var scratch = new Vector3[13];
            for (int i = 0; i < baseline.Length; i++) baseline[i] = new Vector3(.07f * Mathf.Sin(i * .4f), 1 - .025f * i, -.325f * i);
            bool lengthPreserved = true, heightPreserved = true, finite = true, aligned = true;
            foreach (float angle in new[] { -130f, -75f, 0f, 75f, 130f })
            {
                check(CheongryongTailPoseMath.Solve(baseline, Vector3.forward, angle, 1, output, scratch), "Curved chain solves at " + angle + " degrees");
                Vector3 expected = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                aligned &= Vector3.Angle(Vector3.ProjectOnPlane(output[13] - output[0], Vector3.up), expected) < .05f;
                for (int i = 0; i < 13; i++)
                {
                    lengthPreserved &= Mathf.Abs(Vector3.Distance(output[i], output[i + 1]) - Vector3.Distance(baseline[i], baseline[i + 1])) < .00001f;
                    heightPreserved &= Mathf.Abs(output[i + 1].y - baseline[i + 1].y) < .00001f;
                    finite &= CheongryongArcLengthHistory.Finite(output[i + 1]);
                }
            }
            check(lengthPreserved && heightPreserved && finite, "Every sweep preserves Euclidean lengths and baseline heights with finite coordinates");
            check(aligned, "At full attack weight the tip chord reaches the same locked angle used by the sector plan");
            CheongryongTailPoseMath.Solve(baseline, Vector3.forward, 75, 0, output, scratch);
            bool rest = true; for (int i = 0; i < 14; i++) rest &= Vector3.Distance(output[i], baseline[i]) < .00001f;
            check(rest, "Zero-weight recovery exactly restores the supplied current baseline");
            check(!CheongryongTailPoseMath.Solve(baseline, Vector3.zero, 0, 1, output, scratch) &&
                !CheongryongTailPoseMath.Solve(baseline, Vector3.forward, float.NaN, 1, output, scratch), "Undefined direction and invalid angle are rejected");
        }

        private static void Hierarchy(Scene scene, Action<bool, string> check)
        {
            var go = new GameObject("TailSweep_Isolated"); SceneManager.MoveGameObjectToScene(go, scene);
            Transform root = go.transform; root.localScale = Vector3.one * 1.3f;
            Transform head = Child("Head", root, new Vector3(0, 1, 1));
            var bones = new Transform[13];
            for (int i = 0; i < 13; i++) bones[i] = Child("Body_" + (i + 12), i == 0 ? head : bones[i - 1], i == 0 ? Vector3.back : new Vector3(.01f * Mathf.Sin(i), -.012f, -.325f));
            Transform tip = Child("TailTip", bones[12], new Vector3(0, -.012f, -.325f));
            Transform paw = Child("Hind_L_Upper", bones[3], new Vector3(-.4f, -.2f, 0));
            Vector3 pawLocal = paw.localPosition, rootPosition = root.position, headPosition = head.position, basePosition = bones[0].position;
            Quaternion headRotation = head.rotation; var baseline = new Vector3[14]; var rotations = new Quaternion[13];
            for (int i = 0; i < 13; i++) { baseline[i] = bones[i].position; rotations[i] = bones[i].rotation; } baseline[13] = tip.position;
            var presentation = go.AddComponent<CheongryongTailSweepPresentation>();
            check(presentation.Configure(null, bones, tip) && presentation.SweepImplemented, "A direct 13-bone tail chain configures an actual deformation layer");
            check(presentation.ApplyLockedPose(Vector3.forward, -75, 1) && presentation.MaximumSegmentLengthError < .0001f, "Actual parented tail changes pose with exact segment lengths");
            Vector3 atStart = tip.position; presentation.ApplyLockedPose(Vector3.forward, 75, 1); Vector3 atEnd = tip.position;
            check(Vector3.Distance(atStart, atEnd) > 3, "Tail tip physically sweeps several meters between sector edges");
            check(Vector3.Distance(bones[0].position, basePosition) < .0001f && root.position == rootPosition && head.position == headPosition && Quaternion.Angle(head.rotation, headRotation) < .1f,
                "Tail base position, root and Head remain unchanged");
            for (int i = 0; i < 300; i++) presentation.ApplyLockedPose(Vector3.forward, 75, 1);
            check(Vector3.Distance(tip.position, atEnd) < .0001f && presentation.MaximumSegmentLengthError < .0001f,
                "Repeated evaluation without a fresh upstream solve does not accumulate rotations");
            check(paw.parent == bones[3] && paw.localPosition == pawLocal && bones[3].localScale == Vector3.one,
                "Paw descendants and bone local scales survive the sweep");
            presentation.ClearPose(); bool restored = true;
            for (int i = 0; i < 13; i++) restored &= Vector3.Distance(bones[i].position, baseline[i]) < .0001f;
            check(restored && Vector3.Distance(tip.position, baseline[13]) < .0001f && Near(presentation.CurrentWeight, 0), "Cancellation restores the last baseline only while this layer still owns its output");
            presentation.ApplyLockedPose(Vector3.forward, -75, 1);
            // Simulate a fresh BodyFollow solution shifted sideways before the presentation layer.
            for (int i = 0; i < 13; i++) bones[i].SetPositionAndRotation(baseline[i] + Vector3.right, rotations[i]); tip.position = baseline[13] + Vector3.right;
            presentation.ApplyLockedPose(Vector3.forward, 75, 0);
            check(Vector3.Distance(tip.position, baseline[13] + Vector3.right) < .0001f,
                "Recovery samples the current post-follow pose, not an old attack-start curve");
            presentation.ApplyLockedPose(Vector3.forward, -45, 1); presentation.enabled = false;
            typeof(CheongryongTailSweepPresentation).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presentation, null);
            check(Near(presentation.CurrentWeight, 0) && Vector3.Distance(tip.position, baseline[13] + Vector3.right) < .0001f,
                "Explicit disable lifecycle clears and restores owned tail deformation in Edit mode");
            presentation.enabled = true; check(presentation.Configure(null, bones, tip) && presentation.ApplyLockedPose(Vector3.forward, 20, .7f),
                "Re-entry captures the new bind/follow baseline without stale attack ownership");
            presentation.ClearPose();
            var invalid = (Transform[])bones.Clone(); invalid[2] = invalid[1];
            check(!presentation.Configure(null, invalid, tip) && !presentation.SweepImplemented, "Invalid chain cannot claim an implemented sweep");
        }
        private static Transform Child(string name, Transform parent, Vector3 local)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = local; return t; }
        private static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
    }
}
