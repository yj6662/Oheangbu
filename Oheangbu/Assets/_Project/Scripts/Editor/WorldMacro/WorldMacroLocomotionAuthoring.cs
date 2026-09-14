using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Opt-in locomotion assets. Never regenerates terrain, modifies the selected rig, or builds a player.</summary>
    public static partial class WorldMacroLocomotionAuthoring
    {
        public const string OriginalFolder = "Assets/_Project/Art/Characters/PlaytestLocomotion";
        private static string _assetFolderOverride;
        public static string Folder => _assetFolderOverride ?? OriginalFolder;
        public const string Model = "Assets/_Project/Art/Characters/PlaytestReRig/Models/Player_C02_ReRig.fbx";
        public const string SourceProfile = "Assets/_Project/Art/Characters/PlaytestReRig/PlayerAppearance_C02_ReRig.asset";
        public static string Actions => Folder + "/Player_Locomotion_Actions.fbx";
        public static string ControllerPath => Folder + "/AC_PlaytestLocomotion.controller";
        public static string ProfilePath => Folder + "/PlayerAppearance_Locomotion.asset";
        public static string MotorProfilePath => Folder + "/PlayerLocomotion.asset";
        public static string ReportFolder => Path.GetFullPath(Path.Combine(Application.dataPath, _assetFolderOverride==NaturalFolder?"../../Art/PlaytestRecovery/NaturalLocomotion/Unity":_assetFolderOverride==null?"../../Art/PlaytestPolish/Locomotion/Reports":_assetFolderOverride==DodgeTurnFolder?"../../Art/PlaytestRecovery/DodgeTurn/Unity":"../../Art/PlaytestRecovery/Motion/Unity"));
        private static readonly string[] Directions = { "Forward", "Back", "Left", "Right" };
        private static readonly Vector2[] Axes = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        private static readonly string[] PoseNames = { "SitDown", "SitIdle", "StandUp", "JumpRise", "JumpFall", "Land" };

        [Serializable] private sealed class ClipMeasure
        {
            public string name, source, method;
            public bool humanoid, finite;
            public float duration, nativeSupportSpeed, requestedSpeed, playbackRate;
            public int supportSamples;
        }
        [Serializable] private sealed class Report
        {
            public string status, command;
            public string scope = "Derivative Humanoid clips and opt-in movement. Synthetic pose/math checks are separate from native input and terrain play.";
            public List<ClipMeasure> clips = new List<ClipMeasure>();
            public List<string> pass = new List<string>(), fail = new List<string>();
            public string[] unverified = { "Native keyboard play, slopes, stairs, visual cloth/sitting contact and camera framing", "Full gameplay jump/landing/air gate and repeated seating transitions" };
        }

        public static string Execute(string command)
        {
            switch ((command ?? "").Trim().ToLowerInvariant())
            {
                case "prepare": return Prepare();
                case "recovery-prepare": return PrepareRecovery();
                case "recovery-apply": return ApplyRecovery();
                case "recovery-footcheck": _assetFolderOverride=RecoveryFolder;try{return RunFootChecks();}finally{_assetFolderOverride=null;}
                case "apply": return Apply();
                case "validate": return Validate();
                case "math": return MathCheck();
                case "motorcheck": return RunMotorChecks();
                case "footcheck": return RunFootChecks();
                case "recalibrate-forward": return RecalibrateForward();
                default: throw new ArgumentException("Expected prepare, apply, validate, or math. No build command exists.");
            }
        }

        private static void EditOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run this authoring operation in Edit mode.");
        }

        private static string Prepare()
        {
            EditOnly();
            Directory.CreateDirectory(Folder + "/Animations");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var baseline = Need<WorldMacroPlayerAppearanceProfile>(SourceProfile);
            var model = Need<GameObject>(Model);
            Avatar avatar = model.GetComponent<Animator>()?.avatar;
            if (avatar == null || !avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("The unchanged C02 derivative must already have a valid Humanoid avatar.");
            var importer = (ModelImporter)AssetImporter.GetAtPath(Actions);
            if (importer == null) throw new FileNotFoundException("Generate the separate Blender animation derivative first.", Actions);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            // The animation-only FBX must use the same centimetre-to-metre import as
            // its avatar source. A pre-existing importer with useFileScale=false
            // inflated Humanoid Body/RootT curves into kilometre-scale translations.
            importer.useFileScale = true;
            importer.globalScale = ((ModelImporter)AssetImporter.GetAtPath(Model)).globalScale;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importCameras = false; importer.importLights = false;
            importer.optimizeGameObjects = false;
            importer.SaveAndReimport();
            // Keep the exact authored bone transforms as Generic source data. Runtime
            // Humanoid curves are baked through the existing C02 avatar below, avoiding
            // a second independently inferred T-pose that misread lowered arms as raised.
            var takes = importer.defaultClipAnimations;
            foreach (var take in takes)
            {
                take.loopTime = take.name.Contains("SitIdle") || take.name.Contains("JumpFall") || take.name.Contains("Walk") || take.name.Contains("Run");
                take.loopPose = take.loopTime;
                take.lockRootRotation = true; take.lockRootHeightY = true; take.lockRootPositionXZ = true;
                take.keepOriginalOrientation = true; take.keepOriginalPositionY = true; take.keepOriginalPositionXZ = true;
                take.heightFromFeet = false;
            }
            importer.clipAnimations = takes;
            importer.SaveAndReimport();
            var report = new Report { command = "prepare" };
            var walk = new AnimationClip[4]; var run = new AnimationClip[4]; var dodge = new AnimationClip[4];
            var walkRates = new float[4]; var runRates = new float[4];
            for (int i = 0; i < 4; i++)
            {
                walk[i] = CopySource("Walk" + Directions[i], report, true);
                run[i] = CopySource("Run" + Directions[i], report, true);
                dodge[i] = CopySource("Dodge" + Directions[i], report, false);
                CorrectAuthoredSoleHeight(model, baseline.UniformScale, walk[i]);
                CorrectAuthoredSoleHeight(model, baseline.UniformScale, run[i]);
                walkRates[i] = Calibrate(model, baseline.UniformScale, walk[i], Axes[i], 2.2f, report);
                runRates[i] = Calibrate(model, baseline.UniformScale, run[i], Axes[i], 5.5f, report);
            }
            AnimationClip[] poses = PoseNames.Select(n => BakeExactAuthored(FindClip(Actions, "PT_" + n), n, n == "SitIdle" || n == "JumpFall")).ToArray();
            foreach (var pose in poses)
            {
                CorrectAuthoredSoleHeight(model, baseline.UniformScale, pose);
                if (!pose.humanMotion || pose.length <= 0f) throw new InvalidDataException("Pose clip not imported as Humanoid: " + pose.name);
                report.clips.Add(new ClipMeasure { name = pose.name, source = Actions, humanoid = pose.humanMotion, finite = true,
                    duration = pose.length, playbackRate = 1f, method = "Imported separate Blender action; visual contact remains unverified" });
            }
            var controller = BuildController(baseline.Idle, walk, run, dodge, poses, walkRates, runRates);
            var profile = Asset<WorldMacroPlayerAppearanceProfile>(ProfilePath);
            EditorUtility.CopySerialized(baseline, profile);
            profile.name = "PlayerAppearance_Locomotion"; profile.Controller = controller; profile.DirectionalLocomotion = true;
            profile.WalkSpeed = 2.2f; profile.RunSpeed = 5.5f; profile.Walk = walk[0]; profile.Run = run[0];
            profile.DirectionalWalk = walk; profile.DirectionalRun = run; profile.DirectionalDodge = dodge;
            profile.SitDown = poses[0]; profile.SitIdle = poses[1]; profile.StandUp = poses[2];
            profile.JumpRise = poses[3]; profile.JumpFall = poses[4]; profile.Land = poses[5];
            EditorUtility.SetDirty(profile);
            var motor = Asset<PlayerLocomotionProfileSO>(MotorProfilePath);
            // A repeat prepare preserves deliberately tuned motor values.
            EditorUtility.SetDirty(motor); AssetDatabase.SaveAssets();
            report.pass.Add("Twelve existing directional motion sources reused; eight movement trajectories corrected on the C02 skeleton. Original FBX importers left untouched.");
            report.pass.Add("Six derivative actions imported on the existing C02 avatar; root motion baked into poses and Animator root motion remains off.");
            report.status = "PREPARED_REQUIRES_SCENE_APPLY_AND_PLAY_VALIDATION";
            return Save(report);
        }

        private static AnimationClip CopySource(string name, Report report, bool loop)
        {
            string source = "Assets/_Project/Art/Characters/Dosa/Animations/A_DosaCourier_" + name + ".fbx";
            // Direct Dosa Humanoid conversion did not preserve C02 stance geometry. The movement
            // derivative reuses verified original motion trajectories on the unchanged C02 bones.
            AnimationClip motion = name.StartsWith("Walk", StringComparison.Ordinal) || name.StartsWith("Run", StringComparison.Ordinal)
                ? FindClip(Actions, "PT_" + name) : FindClip(source, name);
            AnimationClip clip = name.StartsWith("Walk", StringComparison.Ordinal) || name.StartsWith("Run", StringComparison.Ordinal)
                ? BakeExactAuthored(motion, name, loop) : CopyClip(motion, name, loop);
            if (!clip.humanMotion) throw new InvalidDataException(source + " is not a Humanoid animation.");
            return clip;
        }

        private static AnimationClip FindClip(string path, string contains)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            var clip = clips.FirstOrDefault(c => c.name == contains) ?? clips.FirstOrDefault(c => c.name.Contains(contains));
            if (clip == null && clips.Length == 1) clip = clips[0];
            return clip ?? throw new FileNotFoundException("Animation not found: " + contains, path);
        }

        private static AnimationClip CopyClip(AnimationClip source, string name, bool loop)
        {
            string path = Folder + "/Animations/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            EditorUtility.CopySerialized(source, clip); clip.name = name;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop; settings.loopBlend = loop;
            settings.loopBlendOrientation = true; settings.loopBlendPositionY = true; settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = true; settings.keepOriginalPositionY = true; settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            // Reused motion does not own the independently controlled eyelids.
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) AnimationUtility.SetEditorCurve(clip, binding, null);
            EditorUtility.SetDirty(clip); return clip;
        }

        private static float Calibrate(GameObject source, float scale, AnimationClip clip, Vector2 axis, float speed, Report report)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject instance = null; PlayableGraph graph = default;
            try
            {
                instance = Object.Instantiate(source); instance.name = "Locomotion_Calibration_Only";
                SceneManager.MoveGameObjectToScene(instance, preview);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); instance.transform.localScale = Vector3.one * scale;
                Animator animator = instance.GetComponent<Animator>(); animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
                graph = PlayableGraph.Create("Locomotion_Support_Sampling"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetSpeed(0f);
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable); graph.Play();
                const int count = 121; var left = new Vector3[count]; var right = new Vector3[count];
                var leftHeight = new float[count]; var rightHeight = new float[count];
                var leftSole = CalibrationSole(instance, animator.GetBoneTransform(HumanBodyBones.LeftFoot));
                var rightSole = CalibrationSole(instance, animator.GetBoneTransform(HumanBodyBones.RightFoot));
                float dt = clip.length / (count - 1); bool finite = true;
                for (int i = 0; i < count; i++)
                {
                    playable.SetTime(i * dt); graph.Evaluate(0f);
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    left[i] = SampleSole(leftSole, out leftHeight[i]); right[i] = SampleSole(rightSole, out rightHeight[i]);
                    finite &= Finite(left[i]) && Finite(right[i]);
                }
                var samples = new List<float>(); var direction = new Vector3(axis.x, 0f, axis.y);
                var authored = AuthoredSupportFor(clip.name);
                GatherSupport(left, leftHeight, dt, direction, samples, authored?.samples.Select(s => s.leftSupport).ToArray());
                GatherSupport(right, rightHeight, dt, direction, samples, authored?.samples.Select(s => s.rightSupport).ToArray());
                samples.Sort(); float median = samples.Count > 8 ? samples[samples.Count / 2] : 0f;
                float supportDistance = 0f, supportSeconds = 0f;
                IntegrateSupport(left, dt, direction, authored.samples.Select(s => s.leftSupport).ToArray(), ref supportDistance, ref supportSeconds);
                IntegrateSupport(right, dt, direction, authored.samples.Select(s => s.rightSupport).ToArray(), ref supportDistance, ref supportSeconds);
                float integrated = supportSeconds > 0f ? supportDistance / supportSeconds : 0f;
                // The uneven forward walk's median velocity overstates its full stance
                // travel and left 116mm of root/foot disagreement. Match the integrated
                // signed distance for this clip; do not make IK invent extra stride.
                bool useIntegrated = clip.name == "WalkForward";
                float measured = useIntegrated ? integrated : median;
                Directory.CreateDirectory(ReportFolder);
                File.WriteAllText(Path.Combine(ReportFolder, "calibration_" + clip.name + ".json"), JsonUtility.ToJson(new CalibrationTrace
                { clip = clip.name, direction = direction, left = left, right = right, leftMinimumY = leftHeight, rightMinimumY = rightHeight,
                    signedSupportSpeeds = samples.ToArray(), median = median, integratedSupportSpeed = integrated,
                    supportDistance = supportDistance, supportSeconds = supportSeconds, selectedMethod = useIntegrated ? "integrated signed stance travel/time" : "median signed stance velocity",
                    leftSoleVertices = leftSole.Length, rightSoleVertices = rightSole.Length }, true));
                // Reject a degenerate retarget instead of silently inventing a clip speed.
                if (!finite || measured < .15f || measured > 10f)
                    throw new InvalidDataException("Cannot calibrate actual foot support speed for " + clip.name + ": " + measured + "m/s (" + samples.Count + " samples).");
                float rate = speed / measured;
                report.clips.Add(new ClipMeasure { name = clip.name, source = AssetDatabase.GetAssetPath(clip), humanoid = clip.humanMotion,
                    finite = finite, duration = clip.length, nativeSupportSpeed = measured, requestedSpeed = speed, playbackRate = rate,
                    supportSamples = samples.Count, method = useIntegrated
                        ? "Isolated current C02,121 samples; total signed weighted-sole travel / total time across verified authored stance pairs. Geometry unchanged. Runtime slip checked separately."
                        : "Isolated current C02,121 samples; median signed weighted-sole velocity in verified authored support windows, |vertical velocity| < .55m/s. Runtime slip checked separately." });
                return rate;
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                if (instance != null) Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        [Serializable] private sealed class CalibrationTrace
        {
            public string clip;
            public Vector3 direction;
            public Vector3[] left, right;
            public float[] leftMinimumY, rightMinimumY, signedSupportSpeeds;
            public float median, integratedSupportSpeed, supportDistance, supportSeconds;
            public string selectedMethod;
            public int leftSoleVertices, rightSoleVertices;
        }
        private sealed class CalibrationVertex
        {
            public Transform[] bones; public Vector3[] bindPoints; public float[] weights; public float restY;
        }
        private static CalibrationVertex[] CalibrationSole(GameObject instance, Transform foot)
        {
            var vertices = new List<CalibrationVertex>();
            foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = skin.sharedMesh; if (mesh == null) continue;
                var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
                var positions = mesh.vertices; var bones = skin.bones; var bind = mesh.bindposes;
                try
                {
                    int offset = 0;
                    for (int i = 0; i < counts.Length; i++)
                    {
                        int first = offset; float footWeight = 0f;
                        for (int j = 0; j < counts[i]; j++) { var w = weights[offset++]; if (bones[w.boneIndex] == foot) footWeight += w.weight; }
                        if (footWeight < .7f) continue;
                        var row = new CalibrationVertex { bones = new Transform[counts[i]], bindPoints = new Vector3[counts[i]], weights = new float[counts[i]], restY = skin.transform.TransformPoint(positions[i]).y };
                        for (int j = 0; j < counts[i]; j++) { var w = weights[first + j]; row.bones[j] = bones[w.boneIndex]; row.bindPoints[j] = bind[w.boneIndex].MultiplyPoint3x4(positions[i]); row.weights[j] = w.weight; }
                        vertices.Add(row);
                    }
                }
                finally { counts.Dispose(); weights.Dispose(); }
            }
            if (vertices.Count < 4) throw new InvalidDataException("No actual weighted sole samples for " + foot.name);
            vertices.Sort((a, b) => a.restY.CompareTo(b.restY));
            float cutoff = vertices[Mathf.Min(vertices.Count - 1, Mathf.CeilToInt(vertices.Count * .15f))].restY;
            return vertices.Where(v => v.restY <= cutoff).ToArray();
        }
        private static Vector3 SampleSole(CalibrationVertex[] samples, out float minimum)
        {
            Vector3 centroid = Vector3.zero; minimum = float.PositiveInfinity;
            foreach (var sample in samples)
            {
                Vector3 point = Vector3.zero;
                for (int i = 0; i < sample.weights.Length; i++) point += sample.bones[i].TransformPoint(sample.bindPoints[i]) * sample.weights[i];
                centroid += point; minimum = Mathf.Min(minimum, point.y);
            }
            return centroid / samples.Length;
        }
        private static void GatherSupport(Vector3[] points, float[] heights, float dt, Vector3 travel, List<float> speeds, bool[] authoredSupport = null)
        {
            float bottom = heights.Min();
            for (int i = 2; i < points.Length - 2; i++)
            {
                Vector3 velocity = (points[i + 1] - points[i - 1]) / (2f * dt);
                // Keep the sign in diagnostics. A reversed gait must fail, not be hidden by Abs.
                bool stance = authoredSupport != null && authoredSupport.Length == points.Length
                    ? authoredSupport[i] : heights[i] <= bottom + .025f;
                if (stance && Mathf.Abs(velocity.y) < .55f) speeds.Add(-Vector3.Dot(velocity, travel));
            }
        }

        private static void IntegrateSupport(Vector3[] points, float dt, Vector3 travel, bool[] support, ref float distance, ref float seconds)
        {
            for (int i = 1; i < points.Length; i++) if (support[i - 1] && support[i])
            { distance -= Vector3.Dot(points[i] - points[i - 1], travel); seconds += dt; }
        }

        private static string RecalibrateForward()
        {
            EditOnly();
            var profile = Need<WorldMacroPlayerAppearanceProfile>(ProfilePath);
            var controller = Need<AnimatorController>(ControllerPath);
            var tree = controller.layers[0].stateMachine.states.First(s => s.state.name == "Locomotion").state.motion as BlendTree;
            var children = tree.children; var clip = profile.DirectionalWalk[0];
            int index = Array.FindIndex(children, c => c.motion == clip);
            if (index < 0) throw new InvalidDataException("Forward walk is absent from the installed locomotion tree.");
            var report = new Report { command = "recalibrate_forward", status = "FORWARD_RATE_UPDATED_REQUIRES_FOOTCHECK" };
            float previous = children[index].timeScale;
            float rate = Calibrate(Need<GameObject>(Model), profile.UniformScale, clip, Vector2.up, 2.2f, report);
            children[index].timeScale = rate; tree.children = children; EditorUtility.SetDirty(tree); AssetDatabase.SaveAssets();
            report.pass.Add("Only forward walk playback rate updated from " + previous + " to " + rate + "; clips, model, other directions, motor speed and scene untouched.");
            return Save(report);
        }

        private static AnimatorController BuildController(AnimationClip idle, AnimationClip[] walk, AnimationClip[] run,
            AnimationClip[] dodge, AnimationClip[] poses, float[] walkRates, float[] runRates)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            else
            {
                foreach (var child in AssetDatabase.LoadAllAssetsAtPath(ControllerPath)) if (child != controller) Object.DestroyImmediate(child, true);
                controller.layers = Array.Empty<AnimatorControllerLayer>(); controller.parameters = Array.Empty<AnimatorControllerParameter>();
            }
            foreach (var name in new[] { "PlanarSpeed", "MoveX", "MoveZ", "VerticalSpeed", "SitAmount", "StandAmount", "DodgeProgress", "DodgeX", "DodgeZ" }) controller.AddParameter(name, AnimatorControllerParameterType.Float);
            foreach (var name in new[] { "Grounded", "Sitting", "SitRequested", "Dodging" }) controller.AddParameter(name, AnimatorControllerParameterType.Bool);
            foreach (var name in new[] { "Jump", "Land", "Dodge" }) controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
            controller.AddLayer("Playtest Locomotion"); var layers = controller.layers; layers[0].defaultWeight = 1; layers[0].iKPass = true; controller.layers = layers;
            var sm = layers[0].stateMachine;
            var locomotion = State(sm, "Locomotion", null); sm.defaultState = locomotion;
            var tree = Tree(controller, "Actual local velocity", "MoveX", "MoveZ");
            var children = new List<ChildMotion> { new ChildMotion { motion = idle, position = Vector2.zero, timeScale = 1f } };
            for (int i = 0; i < 4; i++)
            {
                children.Add(new ChildMotion { motion = walk[i], position = Axes[i] * 2.2f, timeScale = walkRates[i] });
                children.Add(new ChildMotion { motion = run[i], position = Axes[i] * 5.5f, timeScale = runRates[i] });
            }
            tree.children = children.ToArray(); locomotion.motion = tree;
            var down = State(sm, "SitDown", poses[0]); var sit = State(sm, "SitIdle", poses[1]); var up = State(sm, "StandUp", poses[2]);
            down.timeParameter = "SitAmount"; down.timeParameterActive = true;
            up.timeParameter = "StandAmount"; up.timeParameterActive = true;
            var rise = State(sm, "JumpRise", poses[3]); var fall = State(sm, "JumpFall", poses[4]); var land = State(sm, "Land", poses[5]);
            var dash = State(sm, "Dodge", null); var dashTree = Tree(controller, "Dodge direction", "DodgeX", "DodgeZ");
            dashTree.children = Enumerable.Range(0, 4).Select(i => new ChildMotion { motion = dodge[i], position = Axes[i], timeScale = 1f }).ToArray(); dash.motion = dashTree; dash.timeParameter = "DodgeProgress"; dash.timeParameterActive = true;
            Transition(locomotion, down, "SitRequested", AnimatorConditionMode.If, .06f);
            Exit(down, sit, .96f, .04f);
            Transition(down, up, "SitRequested", AnimatorConditionMode.IfNot, .08f);
            Transition(sit, up, "SitRequested", AnimatorConditionMode.IfNot, .08f);
            var getup = Transition(up, locomotion, "Sitting", AnimatorConditionMode.IfNot, .08f);
            Transition(locomotion, rise, "Jump", AnimatorConditionMode.If, .035f);
            var edgeFall = Transition(locomotion, fall, "Grounded", AnimatorConditionMode.IfNot, .08f);
            Transition(rise, fall, "VerticalSpeed", AnimatorConditionMode.Less, .07f, 0f);
            Transition(rise, land, "Grounded", AnimatorConditionMode.If, .055f);
            Transition(fall, land, "Grounded", AnimatorConditionMode.If, .055f);
            Exit(land, locomotion, .85f, .09f);
            Transition(land, rise, "Jump", AnimatorConditionMode.If, .035f);
            Transition(locomotion, dash, "Dodge", AnimatorConditionMode.If, .025f);
            Transition(land, dash, "Dodge", AnimatorConditionMode.If, .025f);
            Transition(dash, locomotion, "Dodging", AnimatorConditionMode.IfNot, .06f);
            Transition(dash, fall, "Grounded", AnimatorConditionMode.IfNot, .04f);
            // Unsupported seating cancels as a fall; it never carries the seated pose into the air.
            Transition(down, fall, "Grounded", AnimatorConditionMode.IfNot, .06f);
            Transition(sit, fall, "Grounded", AnimatorConditionMode.IfNot, .06f);
            Transition(up, fall, "Grounded", AnimatorConditionMode.IfNot, .06f);
            EditorUtility.SetDirty(controller); return controller;
        }

        private static BlendTree Tree(AnimatorController controller, string name, string x, string y)
        {
            var tree = new BlendTree { name = name, blendType = BlendTreeType.FreeformCartesian2D, blendParameter = x, blendParameterY = y, useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller); return tree;
        }
        private static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion)
        { var state = sm.AddState(name); state.motion = motion; state.writeDefaultValues = false; return state; }
        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, string parameter, AnimatorConditionMode mode, float blend, float threshold = 0f)
        {
            var t = from.AddTransition(to); t.hasExitTime = false; t.hasFixedDuration = true; t.duration = blend; t.canTransitionToSelf = false;
            t.AddCondition(mode, threshold, parameter); return t;
        }
        private static void Exit(AnimatorState from, AnimatorState to, float exit, float blend)
        { var t = from.AddTransition(to); t.hasExitTime = true; t.exitTime = exit; t.hasFixedDuration = true; t.duration = blend; t.canTransitionToSelf = false; }

        private static string Apply()
        {
            EditOnly();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != WorldMacroPlaytestAuthoring.ScenePath) throw new InvalidOperationException("Only the separate W_WorldMacro_Playtest scene may opt in.");
            var appearance = Object.FindFirstObjectByType<WorldMacroPlayerAppearance>();
            if (appearance == null || appearance.Animator == null) throw new InvalidOperationException("Install the approved re-rigged C02 appearance first.");
            var motor = appearance.GetComponentInParent<PlayerMotor>();
            if (motor == null) throw new InvalidOperationException("PlayerMotor not found above the appearance.");
            var profile = Need<WorldMacroPlayerAppearanceProfile>(ProfilePath); var config = Need<PlayerLocomotionProfileSO>(MotorProfilePath);
            Undo.RecordObject(motor, "Opt in playtest locomotion");
            var motorData = new SerializedObject(motor); motorData.FindProperty("_locomotion").objectReferenceValue = config; motorData.ApplyModifiedProperties();
            Undo.RecordObject(appearance, "Use directional locomotion appearance");
            var data = new SerializedObject(appearance); data.FindProperty("_profile").objectReferenceValue = profile; data.ApplyModifiedProperties();
            Undo.RecordObject(appearance.Animator, "Use directional locomotion controller");
            appearance.Animator.runtimeAnimatorController = profile.Controller; appearance.Animator.applyRootMotion = false;
            EditorUtility.SetDirty(motor); EditorUtility.SetDirty(appearance); EditorUtility.SetDirty(appearance.Animator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(appearance); PrefabUtility.RecordPrefabInstancePropertyModifications(appearance.Animator);
            EditorSceneManager.MarkSceneDirty(scene);
            var report = new Report { command = "apply", status = "APPLIED_SCENE_DIRTY_NOT_SAVED" };
            report.pass.Add("Opt-in profile and controller assigned only to the separate playtest player. No scene regeneration or build.");
            return Save(report);
        }

        private static string Validate()
        {
            var report = new Report { command = "validate" };
            var profile = AssetDatabase.LoadAssetAtPath<WorldMacroPlayerAppearanceProfile>(ProfilePath);
            var motor = AssetDatabase.LoadAssetAtPath<PlayerLocomotionProfileSO>(MotorProfilePath);
            Check(report, "Opt-in motor profile walk 2.2 / run 5.5 / jump .75", motor != null && Mathf.Abs(motor.WalkSpeed - 2.2f) < .001f && Mathf.Abs(motor.RunSpeed - 5.5f) < .001f && Mathf.Abs(motor.JumpHeight - .75f) < .001f);
            Check(report, "Directional controller and eighteen Humanoid clips", profile != null && profile.DirectionalLocomotion && profile.Controller != null
                && profile.DirectionalWalk.Concat(profile.DirectionalRun).Concat(profile.DirectionalDodge)
                    .Concat(new[] { profile.SitDown, profile.SitIdle, profile.StandUp, profile.JumpRise, profile.JumpFall, profile.Land }).All(c => c != null && c.humanMotion && c.length > 0f));
            var appearance = Object.FindFirstObjectByType<WorldMacroPlayerAppearance>();
            var installedMotor = appearance != null ? appearance.GetComponentInParent<PlayerMotor>() : null;
            Check(report, "Scene opted in without changing source profile", appearance != null && appearance.Profile == profile && installedMotor != null && installedMotor.LocomotionProfile == motor && !appearance.Animator.applyRootMotion);
            Check(report, "Foot-placement adapter present", appearance != null && appearance.GetComponent<WorldMacroPlayerFootPlacement>() != null);
            report.status = report.fail.Count == 0 ? "STATIC_UNITY_CONFIGURATION_PASS_RUNTIME_UNVERIFIED" : "FAILED"; return Save(report);
        }

        private static string MathCheck()
        {
            var report = new Report { command = "math" };
            foreach (int fps in new[] { 30, 60, 120 }) foreach (float timeScale in new[] { 1f, .2f })
            {
                float dt = timeScale / fps, g = -20f, v = PlayerMotor.JumpSpeed(.75f, g), y = 0f, peak = 0f;
                for (int i = 0; i < fps * 12 && (i == 0 || y >= 0f); i++) { y += v * dt + .5f * g * dt * dt; v += g * dt; peak = Mathf.Max(y, peak); }
                Check(report, fps + "fps scale " + timeScale + " trapezoid .75m apex", Mathf.Abs(peak - .75f) < .003f);
            }
            Check(report, "Diagonal desired motion normalized", Mathf.Abs(Vector3.ClampMagnitude(new Vector3(1f, 0f, 1f), 1f).magnitude - 1f) < .00001f);
            var baseline = Need<WorldMacroPlayerAppearanceProfile>(SourceProfile);
            Check(report, "Source appearance remains non-opt-in", !baseline.DirectionalLocomotion);
            report.status = report.fail.Count == 0 ? "PURE_MATH_PASS_NATIVE_INPUT_UNVERIFIED" : "FAILED"; return Save(report);
        }

        private static bool Finite(Vector3 p) => !float.IsNaN(p.x + p.y + p.z) && !float.IsInfinity(p.x + p.y + p.z);
        private static void Check(Report report, string label, bool pass) { (pass ? report.pass : report.fail).Add(label); }
        private static T Need<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException(typeof(T).Name, path);
        private static T Asset<T>(string path) where T : ScriptableObject
        { var value = AssetDatabase.LoadAssetAtPath<T>(path); if (value == null) { value = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(value, path); } return value; }
        private static string Save(Report report)
        { Directory.CreateDirectory(ReportFolder); string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(ReportFolder, "unity_" + report.command + ".json"), json); return json; }
    }
}
