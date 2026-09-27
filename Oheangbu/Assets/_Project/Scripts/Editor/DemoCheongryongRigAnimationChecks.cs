using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    // Explicit isolated invocation only. No refresh, saved scene edits, rendering, Play mode or builds.
    public static class DemoCheongryongRigAnimationChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Actual imported FBX pose and graph order relative to BodyFollow in Play mode",
                "Head bite pose versus contact geometry", "Actual tail sweep deformation (not implemented)",
                "Death/cancellation transitions in live combat", "Runtime performance" };
        }

        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run rig animation checks in Edit mode.");
            var result = new Report();
            void Check(bool pass, string name) => (pass ? result.passed : result.failed).Add(name);
            try { Pure(Check); } catch (Exception e) { result.failed.Add("Phase math: " + e); }
            try { ImportedBindings(Check); } catch (Exception e) { result.failed.Add("Imported Generic paths: " + e); }
            Scene scene = EditorSceneManager.NewPreviewScene();
            var clips = new List<AnimationClip>();
            try { Graph(scene, clips, Check); } catch (Exception e) { result.failed.Add("Isolated graph: " + e); }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (AnimationClip clip in clips) if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
            }
            result.status = result.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(result, true);
        }

        private static void Pure(Action<bool, string> check)
        {
            CheongryongClipPhase At(float t, bool cancelled = false) => CheongryongRigPhase.Attack(t, 10, 11, 11.3f, 12, cancelled);
            check(Near(At(10).NormalizedTime, 0) && Near(At(10).Weight, 0), "Windup starts from rest with zero abrupt attack weight");
            check(Near(At(10.5f).NormalizedTime, .25f) && Near(At(10.5f).Weight, 1), "Half windup maps to quarter clip at full attack blend");
            check(Near(At(11).NormalizedTime, .5f) && Near(At(11).Weight, 1), "Release is exactly the authored midpoint anticipation peak");
            check(Near(At(11.3f).NormalizedTime, .9f) && Near(At(11.3f).Weight, 1), "Active end reaches 90 percent clip before recovery blend");
            check(At(11.65f).Weight > .49f && At(11.65f).Weight < .51f && At(11.65f).NormalizedTime > .94f,
                "Recovery finishes the pose while blending back to idle");
            check(Near(At(12).Weight, 0) && Near(At(500).Weight, 0) && Near(At(9).Weight, 0), "Before/after timeline never retains an attack pose");
            check(Near(At(10.99f, true).Weight, 0) && Near(At(11.1f, true).Weight, 0), "Cancellation before or after release removes attack contribution");
            CheongryongClipPhase paused = At(10.73f); bool stable = true;
            for (int i = 0; i < 1000; i++) stable &= Near(At(10.73f).NormalizedTime, paused.NormalizedTime) && Near(At(10.73f).Weight, paused.Weight);
            check(stable, "Repeated paused samples do not advance pose or blending");
            float[] rates = { 24, 30, 60, 120 }; bool rateIndependent = true;
            foreach (float rate in rates)
            {
                for (int i = 0; i < rate; i++) At(10 + i / rate);
                rateIndependent &= Near(At(11).NormalizedTime, .5f) && Near(At(11).Weight, 1);
            }
            check(rateIndependent && Mathf.Abs(At(11.7f).NormalizedTime - .9571429f) < .0001f && At(11.7f).Weight < .4f,
                "Absolute-time phase reaches the expected recovery pose regardless of preceding frame rate or a long-frame gap");
            check(Near(CheongryongRigPhase.IdleTime(5.25f, 2), 1.25f) && Near(CheongryongRigPhase.IdleTime(-2, 2), 0), "Idle loops by scaled elapsed time and clamps before start");
            check(Near(CheongryongRigPhase.Attack(float.NaN, 10, 11, 12, 13).Weight, 0) &&
                Near(CheongryongRigPhase.Attack(11, 10, 10, 12, 13).Weight, 0) &&
                Near(CheongryongRigPhase.IdleTime(float.PositiveInfinity, 2), 0), "Invalid clocks and degenerate timeline are finite and inactive");
        }

        private static void ImportedBindings(Action<bool, string> check)
        {
            const string path = "Assets/_Project/Art/Demo/Chapter3/Cheongryong/SM_Cheongryong_Prototype.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            check(model != null, "Actual dragon FBX hierarchy is available for binding verification");
            if (model == null) return;
            int clips = 0, missingPaths = 0, clipsWithHead = 0;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                clips++; bool head = false;
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(Transform)) continue;
                    if (binding.path == "Head" || binding.path.EndsWith("/Head", StringComparison.Ordinal)) head = true;
                    if (!string.IsNullOrEmpty(binding.path) && model.transform.Find(binding.path) == null) missingPaths++;
                }
                if (head) clipsWithHead++;
            }
            check(clips == 3 && clipsWithHead == 3, "All three actual imported clips contain Head transform curves");
            check(missingPaths == 0, "Generic clip paths resolve below the imported model root used by Chapter3_Visual Animator");
        }

        private static void Graph(Scene scene, List<AnimationClip> clips, Action<bool, string> check)
        {
            var go = new GameObject("CheongryongRigAnimation_Isolated"); SceneManager.MoveGameObjectToScene(go, scene);
            var head = new GameObject("Head").transform; head.SetParent(go.transform, false);
            var animator = go.AddComponent<Animator>(); animator.applyRootMotion = true; animator.fireEvents = true;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
            var presentation = go.AddComponent<CheongryongRigAnimation>();
            AnimationClip idle = Clip("Idle_Probe", 2, .02f), bite = Clip("Head_Probe", 1.2f, .2f), tail = Clip("Tail_Probe", 1.4f, .3f);
            clips.Add(idle); clips.Add(bite); clips.Add(tail);
            check(presentation.Configure(animator, idle, bite, tail, null) && presentation.HasGraph,
                "Three Generic clips create one explicitly controlled graph");
            check(!animator.applyRootMotion && !animator.fireEvents && animator.runtimeAnimatorController == null && animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
                "Graph disables root motion/events and detaches automatic AnimatorController evaluation");
            go.transform.localPosition = new Vector3(3, 2, 5); go.transform.localRotation = Quaternion.Euler(0, 25, 0); go.transform.localScale = Vector3.one * 1.4f;
            Vector3 rootPosition = go.transform.localPosition, rootScale = go.transform.localScale; Quaternion rootRotation = go.transform.localRotation;
            presentation.EvaluateAt(10); presentation.EvaluateAt(10.5f); Vector3 pausedHead = head.localPosition; int count = presentation.GraphEvaluationCount;
            check(pausedHead.x > .001f && pausedHead.x <= .021f, "Manual Generic graph evaluates an actual child transform curve");
            presentation.EvaluateAt(10.5f);
            check(presentation.GraphEvaluationCount == count + 1 && Near(presentation.CurrentClipTime, .5f) && Vector3.Distance(head.localPosition, pausedHead) < .0001f,
                "Exactly one manual evaluation per explicit sample; paused sample reapplies the same pose");
            check(go.transform.localPosition == rootPosition && go.transform.localScale == rootScale && Quaternion.Angle(go.transform.localRotation, rootRotation) < .1f,
                "Graph evaluation preserves Animator root translation/rotation/scale");
            count = presentation.GraphEvaluationCount; presentation.EvaluateAt(float.NaN);
            check(presentation.GraphEvaluationCount == count, "Invalid sample time never evaluates a graph");
            var profile = ScriptableObject.CreateInstance<CheongryongCombatProfile>();
            try
            {
                var plan = new CheongryongAttackPlan(profile, CheongryongAttackKind.HeadBite,
                    AttackProvenance.Create(go, DamageSource.Enemy, null), Vector3.zero, Vector3.forward, Vector3.forward, 20);
                typeof(CheongryongAttackPlan).GetMethod("AdvanceTo", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plan, new object[] { plan.ActiveEndAt });
                Lifecycle(presentation, "OnAttackStarted", plan);
                Lifecycle(presentation, "OnAttackEnded", plan, false);
                presentation.EvaluateAt(plan.ActiveEndAt);
                check(Near(presentation.CurrentAttackWeight, 1) && Near(presentation.CurrentClipTime, bite.length * .9f),
                    "Normally ended plan retains its active-end pose after combat removes CurrentPlan");
                float middleRecovery = (plan.ActiveEndAt + plan.EndAt) * .5f;
                presentation.EvaluateAt(middleRecovery);
                check(Mathf.Abs(presentation.CurrentAttackWeight - .5f) < .0001f && Near(presentation.CurrentClipTime, bite.length * .95f),
                    "Retained plan plays and blends the actual recovery half-way toward idle");
                presentation.EvaluateAt(middleRecovery);
                check(Mathf.Abs(presentation.CurrentAttackWeight - .5f) < .0001f, "Retained recovery remains paused on unchanged scaled time");
                presentation.EvaluateAt(plan.EndAt);
                check(Near(presentation.CurrentAttackWeight, 0), "Retained recovery expires at EndAt");
                Lifecycle(presentation, "OnAttackStarted", plan); Lifecycle(presentation, "OnAttackEnded", plan, true);
                presentation.EvaluateAt(plan.ActiveEndAt);
                check(Near(presentation.CurrentAttackWeight, 0), "Cancelled event discards retained pose immediately");
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
            presentation.enabled = false;
            // Non-ExecuteAlways behaviours do not consistently receive Editor preview callbacks.
            // Explicitly dispatch the actual lifecycle handler; its cleanup must be idempotent.
            Lifecycle(presentation, "OnDisable");
            check(!presentation.HasGraph && animator.applyRootMotion && animator.fireEvents && animator.cullingMode == AnimatorCullingMode.CullCompletely,
                "Disable destroys graph and restores owned Animator settings");
            check(Near(presentation.CurrentAttackWeight, 0) && !presentation.TailSweepRequested, "Cleanup clears attack/tail request state");
            presentation.enabled = true; Lifecycle(presentation, "OnEnable"); presentation.EvaluateAt(15);
            check(presentation.HasGraph && Near(presentation.CurrentClipTime, 0), "Re-enable creates a new graph and fresh idle epoch");
            check(!presentation.TailSweepVisualImplemented, "Head-only tail anticipation is explicitly not reported as a tail sweep visual");
            check(!presentation.Configure(animator, null, bite, tail, null) && !presentation.HasGraph && animator.applyRootMotion,
                "Invalid reconfiguration releases prior graph without retaining Animator ownership");
        }

        private static AnimationClip Clip(string name, float duration, float excursion)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            // Probe clip tests the Generic graph mechanics; it does not replace the dragon FBX.
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Head", typeof(Transform), "m_LocalPosition.x"),
                new AnimationCurve(new Keyframe(0, 0), new Keyframe(duration * .5f, excursion), new Keyframe(duration, 0)));
            return clip;
        }
        private static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
        private static void Lifecycle(CheongryongRigAnimation target, string name, params object[] arguments) =>
            typeof(CheongryongRigAnimation).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
    }
}
