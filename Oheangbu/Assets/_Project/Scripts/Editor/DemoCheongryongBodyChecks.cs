using System;
using System.Collections.Generic;
using Oheangbu.App.Demo;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    // Explicit invocation only: no InitializeOnLoad, Play mode, rendering, asset saves or builds.
    public static class DemoCheongryongBodyChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Imported dragon mesh deformation and Animator ordering in Play mode",
                "Ground contact and four-paw placement", "Self-intersection on tight turns/reversal",
                "Head/tail attack contact alignment", "Animated or nonuniform ancestor scale", "Runtime performance" };
        }

        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run body checks in Edit mode.");
            var report = new Report();
            void Check(bool pass, string name) => (pass ? report.passed : report.failed).Add(name);
            try { Pure(Check); } catch (Exception e) { report.failed.Add("Pure history: " + e); }
            Scene scene = EditorSceneManager.NewPreviewScene();
            try { Hierarchy(scene, Check); } catch (Exception e) { report.failed.Add("Parented chain: " + e); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL";
            return JsonUtility.ToJson(report, true);
        }

        private static void Pure(Action<bool, string> check)
        {
            var h = new CheongryongArcLengthHistory(64, .02f, 8, 2);
            check(h.Reset(new[] { Vector3.zero, Vector3.back, Vector3.back * 2 }), "Reset seeds ordered head-to-tail history");
            check(Near(h.Sample(.5f, Vector3.back), Vector3.back * .5f) &&
                Near(h.Sample(4, Vector3.back), Vector3.back * 4), "Arc sampling interpolates and extends oldest tangent");
            int count = h.Count; Vector3 before = h.Sample(1.25f, Vector3.back);
            for (int i = 0; i < 1000; i++) h.Advance(Vector3.zero);
            check(h.Count == count && Near(h.Sample(1.25f, Vector3.back), before), "Stationary frames preserve pose and history count");
            h.Advance(Vector3.forward * .005f);
            check(h.Count == count && Near(h.Sample(0, Vector3.back), Vector3.forward * .005f), "Sub-spacing movement updates live head without allocating a trail point");
            Vector3 live = h.Head;
            check(h.Advance(new Vector3(float.NaN, 0, 0)) == CheongryongTrailAdvance.Invalid &&
                h.Advance(new Vector3(0, float.PositiveInfinity, 0)) == CheongryongTrailAdvance.Invalid && Near(h.Head, live),
                "NaN/Infinity inputs are rejected without changing history");
            check(!h.Reset(new[] { Vector3.zero, new Vector3(float.NaN, 0, 0) }) && Near(h.Head, live), "Invalid reset is transactional");
            check(h.Advance(Vector3.right * 50) == CheongryongTrailAdvance.Teleport && Near(h.Head, live), "Teleport is reported before polluting the trail");
            h.Reset(new[] { Vector3.right * 50, Vector3.right * 50 + Vector3.back });
            check(h.Count == 2 && Near(h.Sample(.5f, Vector3.back), Vector3.right * 50 + Vector3.back * .5f), "Teleport reset drops the former location");

            h.Reset(new[] { Vector3.zero, Vector3.back });
            for (int i = 1; i <= 10000; i++) h.Advance(new Vector3(i * .03f, 0, Mathf.Sin(i * .05f)));
            check(h.Count <= h.Capacity && h.Capacity == 64 && h.StoredLength <= 8.0001f,
                "Ten thousand advances retain fixed memory capacity and bounded stored arc length");
            var shortTrail = new CheongryongArcLengthHistory(128, .01f, 1, 2);
            shortTrail.Reset(new[] { Vector3.zero });
            for (int i = 0; i < 500; i++) shortTrail.Advance(Vector3.forward * (.03f * i));
            check(shortTrail.StoredLength <= 1.0001f && shortTrail.Count < 40, "Arc-length trimming works independently of point capacity");
            shortTrail.Reset(new[] { Vector3.zero, Vector3.back * 100 });
            check(shortTrail.StoredLength <= 1.0001f, "Oversized two-point seed is clipped to the arc-length budget");

            h.Reset(new[] { Vector3.zero, Vector3.back, Vector3.back * 2 });
            for (int i = 1; i <= 20; i++) h.Advance(Vector3.forward * (.05f * i));
            for (int i = 19; i >= -20; i--) h.Advance(Vector3.forward * (.05f * i));
            var lengths = new float[24]; var fallback = new Vector3[24]; var solved = new Vector3[25];
            for (int i = 0; i < 24; i++) { lengths[i] = .325f; fallback[i] = Vector3.back; }
            h.Solve(h.Head, lengths, fallback, solved); float error = 0; bool finite = true;
            for (int i = 0; i < 24; i++) { error = Mathf.Max(error, Mathf.Abs(Vector3.Distance(solved[i], solved[i + 1]) - lengths[i])); finite &= CheongryongArcLengthHistory.Finite(solved[i + 1]); }
            check(finite && error < 1e-5f && Near(solved[0], h.Head), "Full reversal yields finite joints and exact Euclidean segment lengths");
            h.Reset(new[] { Vector3.zero, Vector3.zero, Vector3.zero });
            h.Solve(Vector3.zero, lengths, fallback, solved);
            check(h.Count == 1 && Near(solved[24], Vector3.back * (24 * .325f)), "Duplicate points and zero-length history use a finite fallback tangent");
            check(CheongryongArcLengthHistory.Finite(h.Sample(float.NaN, Vector3.zero)), "Invalid sampling distance has a finite safe result");
        }

        private static void Hierarchy(Scene scene, Action<bool, string> check)
        {
            var rootObject = new GameObject("BodyFollow_IsolatedRoot"); SceneManager.MoveGameObjectToScene(rootObject, scene);
            Transform root = rootObject.transform; root.localScale = Vector3.one * 1.3f;
            Transform head = Child("Head", root, Vector3.zero); var body = new Transform[24];
            for (int i = 0; i < body.Length; i++) body[i] = Child("Body_" + (i + 1).ToString("00"), i == 0 ? head : body[i - 1], i == 0 ? Vector3.zero : new Vector3(.015f * Mathf.Sin(i), 0, -.325f));
            Transform tail = Child("TailTip", body[23], Vector3.back * .325f);
            Transform paw = Child("Fore_L_Upper", body[4], new Vector3(-.4f, -.2f, 0));
            Vector3 pawLocal = paw.localPosition, pawScale = paw.localScale;
            var original = new Vector3[25]; var segmentLengths = new float[24]; var scales = new Vector3[24];
            for (int i = 0; i < 24; i++) { original[i] = body[i].position; scales[i] = body[i].localScale; }
            original[24] = tail.position;
            for (int i = 0; i < 24; i++) segmentLengths[i] = Vector3.Distance(original[i], original[i + 1]);
            var follow = rootObject.AddComponent<CheongryongBodyFollow>();
            check(follow.Configure(head, body, tail) && follow.IsConfigured, "Explicit direct-parent rig chain configures");
            follow.EvaluateFollow(); bool rest = true;
            for (int i = 0; i < 24; i++) rest &= Near(body[i].position, original[i]);
            check(rest && Near(tail.position, original[24]), "Seeded curved bind pose survives first solve at uniform model scale");
            Quaternion oldHead = Quaternion.Euler(0, 30, 0); head.localRotation = oldHead;
            for (int i = 0; i < 300; i++) follow.EvaluateFollow();
            bool unchanged = true;
            for (int i = 0; i < 24; i++) unchanged &= Near(body[i].position, original[i]);
            check(unchanged && Quaternion.Angle(head.localRotation, oldHead) < .1f,
                "Repeated stationary evaluation removes inherited Head rotation without modifying Head or accumulating drift");
            for (int frame = 1; frame <= 120; frame++)
            {
                root.position = new Vector3(.006f * frame, 0, .02f * frame);
                root.rotation = Quaternion.Euler(0, frame * .2f, 0);
                // Simulate Animator resetting body rotations before each LateUpdate.
                for (int i = 0; i < 24; i++) body[i].localRotation = Quaternion.identity;
                follow.EvaluateFollow();
            }
            bool preserved = follow.MaximumSegmentLengthError < .0001f;
            for (int i = 0; i < 24; i++)
                preserved &= Mathf.Abs(Vector3.Distance(body[i].position, i == 23 ? tail.position : body[i + 1].position) - segmentLengths[i]) < .0001f && body[i].localScale == scales[i];
            check(preserved && Near(body[0].position, head.position), "Moving/turning parent plus Animator resets preserve anchor, scale and every segment length");
            check(paw.parent == body[4] && paw.localPosition == pawLocal && paw.localScale == pawScale,
                "Paw descendants and local transforms are retained");
            root.position += Vector3.right * 50; follow.EvaluateFollow();
            check(follow.TeleportResetCount == 1 && Vector3.Distance(tail.position, root.position) < 12 && follow.MaximumSegmentLengthError < .0001f,
                "Parent teleport reseeds locally instead of dragging the tail across the map");
            root.rotation = Quaternion.Euler(0, 155, 0); follow.ResetPoseHistory(); follow.EvaluateFollow();
            check(follow.HistoryCount > 0 && follow.MaximumSegmentLengthError < .0001f && CheongryongArcLengthHistory.Finite(tail.position),
                "Explicit reset after turning is finite and preserves bind lengths");
            var invalid = (Transform[])body.Clone(); invalid[3] = invalid[2];
            check(!follow.Configure(head, invalid, tail), "Duplicate or incorrectly parented body bones are rejected");
        }

        private static Transform Child(string name, Transform parent, Vector3 position)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; return t; }
        private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .0001f;
    }
}
