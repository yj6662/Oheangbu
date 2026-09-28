using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        /// <summary>
        /// #297 vertical double-hook grip (쌍구법) for the main scene's C02 gesture rig. Presentation only.
        /// Edit mode: make-profile[:force][:scale], fit, scale:&lt;v&gt;, assign[:nosave], revert[:nosave], status.
        /// Play mode: capture:&lt;state&gt;[:label], check[:label] (RuntimeGestureQa, first-person fixture).
        /// The fit reads the scene's player instance and writes only the new profile; the scene and prefab are not modified.
        /// </summary>
        public static string Shuanggou297(string command) => Shuanggou297Tool.Run(command);

        private static class Shuanggou297Tool
        {
            public const string ProfileAsset = "Assets/_Project/Art/World/Finish297/Data/PlayerGesture297.asset";
            static string OutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/World/Compact/Rebuild/Finish297/Grip297"));
            static string StatePath => Path.Combine(OutputRoot, "assign_state.json");
            static readonly string[] Digits = { "Index", "Middle", "Thumb", "Ring", "Pinky" };

            [Serializable] sealed class AssignState { public string utc, scene, rig, previous, assigned; }

            [Serializable] sealed class FitDigit
            {
                public string digit;
                public float residualMm, clearanceMm, abductionDegrees;
                public float[] flexDegrees;
            }

            [Serializable] sealed class FitReport
            {
                public string status, utc, scene, rig, profile, source, handBone, handleMeasurement;
                public float handScale, shaftRadiusMeters, handleButtAlongMeters, ferruleAlongMeters, gripToTipMeters;
                public float holdAlongMeters, holdFromFerruleMeters, graspToTipMeters, brushScale;
                public float shaftForwardMeters, shaftPalmarMeters, cost;
                public Vector3 shuanggouGripPosition;
                public Quaternion shuanggouGripRotation;
                public FitDigit[] digits;
                public List<string> notes = new List<string>();
                public string[] limitations =
                {
                    "Capsule finger model against the measured shaft cylinder; skinned mesh contact/penetration is not verified by this fit.",
                    "Only DrawingOffset of the 15 right-hand finger entries and the vertical grip frame are written. Carry/Harvest keep grip A.",
                    "IMPLEMENTED only: visual acceptance needs Play captures (center, corners, world-drawing, grip-close, grip-palm)."
                };
            }

            [Serializable] sealed class Status
            {
                public string scene, rig, profile, state;
                public bool profileExists, shuanggouGrip, playing;
                public float brushScale;
                public Vector3 shuanggouGripPosition;
            }

            public static string Run(string command)
            {
                string raw = string.IsNullOrWhiteSpace(command) ? "status" : command.Trim();
                string[] parts = raw.Split(':');
                string verb = parts[0].Trim().ToLowerInvariant();
                string[] args = parts.Skip(1).Select(x => x.Trim()).ToArray();
                switch (verb)
                {
                    case "make-profile": return MakeProfile(args);
                    case "fit": return Fit(null);
                    case "pen": return PenFit(args);
                    case "fist": return FistFromCarry(args);
                    case "fist-fit": return FistFit(args);
                    case "scale": return SetScale(args);
                    case "assign": return Assign(!args.Contains("nosave"));
                    case "revert": return Revert(!args.Contains("nosave"));
                    case "status": return StatusJson();
                    case "capture":
                        Need(args.Length >= 1 && args[0].Length > 0, "capture:<state>[:label]; states: center, top-left, top-right, bottom-left, bottom-right, carry, world-drawing, grip-close, grip-palm.");
                        return Capture(args[0], args.Length > 1 ? args[1] : null);
                    case "check": return Capture(null, args.Length > 0 ? args[0] : null);
                    default: throw new ArgumentException("Unknown Shuanggou297 command '" + raw + "'. Use make-profile, fit, scale:<v>, assign, revert, status, capture:<state>[:label], check[:label].");
                }
            }

            static void NeedEdit() => Need(!EditorApplication.isPlayingOrWillChangePlaymode, "Edit mode required (this command changes assets or the scene).");

            static WorldMacroPlayerGestureRig SceneRig()
            {
                var rigs = Object.FindObjectsByType<WorldMacroPlayerGestureRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                Need(rigs.Length > 0, "No WorldMacroPlayerGestureRig in the open scene(s).");
                Scene active = SceneManager.GetActiveScene();
                return rigs.FirstOrDefault(r => r.gameObject.scene == active) ?? rigs[0];
            }

            static WorldMacroPlayerGestureProfile LoadProfile(string path) =>
                string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>(path);

            static AssignState LoadState() => File.Exists(StatePath) ? JsonUtility.FromJson<AssignState>(File.ReadAllText(StatePath)) : null;

            static string ScenePath(Component c) => c.gameObject.scene.path;

            static string HierarchyPath(Transform t)
            {
                var names = new List<string>();
                for (; t != null; t = t.parent) names.Add(t.name);
                names.Reverse();
                return string.Join("/", names);
            }

            // ---------------------------------------------------------------- profile

            static string MakeProfile(string[] args)
            {
                NeedEdit();
                bool force = args.Any(a => a.Equals("force", StringComparison.OrdinalIgnoreCase));
                float scale = 1.25f;
                foreach (string a in args)
                    if (float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) scale = parsed;
                var rig = SceneRig();
                string source = AssetDatabase.GetAssetPath(rig.Profile);
                if (source == ProfileAsset) source = LoadState()?.previous;
                var sourceProfile = LoadProfile(source);
                Need(sourceProfile != null, "Source profile not found: neither the scene rig's profile nor the recorded previous profile (" + source + ").");
                var existing = LoadProfile(ProfileAsset);
                if (existing != null && !force)
                    return "PROFILE_EXISTS " + ProfileAsset + " (unchanged). Run 'fit' to refit, 'scale:<v>' for the handle stretch, or 'make-profile:force' to recopy from " + source + ".";
                EnsureFolder(Path.GetDirectoryName(ProfileAsset).Replace('\\', '/'));
                if (existing == null) Need(AssetDatabase.CopyAsset(source, ProfileAsset), "CopyAsset " + source + " → " + ProfileAsset + " failed.");
                else EditorUtility.CopySerialized(sourceProfile, existing);
                var profile = LoadProfile(ProfileAsset);
                Need(profile != null, "New profile did not load: " + ProfileAsset);
                profile.name = Path.GetFileNameWithoutExtension(ProfileAsset);
                profile.ShuanggouGrip = true;
                profile.BrushScale = Mathf.Clamp(scale, 1f, 1.6f);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                return Fit(source);
            }

            static string SetScale(string[] args)
            {
                NeedEdit();
                float value = 1f;
                Need(args.Length > 0 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value), "scale:<1..1.6>");
                var profile = LoadProfile(ProfileAsset);
                Need(profile != null, "Run make-profile first.");
                profile.BrushScale = Mathf.Clamp(value, 1f, 1.6f);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                return "BRUSH_SCALE " + profile.BrushScale.ToString("0.###", CultureInfo.InvariantCulture)
                    + " (handle-only length stretch pinned at the ferrule, applied when the rig binds in Play).";
            }

            static void EnsureFolder(string folder)
            {
                if (AssetDatabase.IsValidFolder(folder)) return;
                string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }

            // ---------------------------------------------------------------- assignment

            static string Assign(bool save)
            {
                NeedEdit();
                var profile = LoadProfile(ProfileAsset);
                Need(profile != null, "Run make-profile first.");
                var rig = SceneRig();
                string previous = AssetDatabase.GetAssetPath(rig.Profile);
                var state = LoadState();
                if (previous != ProfileAsset || state == null)
                {
                    state = new AssignState { utc = DateTime.UtcNow.ToString("o"), scene = ScenePath(rig), rig = HierarchyPath(rig.transform),
                        previous = previous == ProfileAsset ? state?.previous : previous, assigned = ProfileAsset };
                    Directory.CreateDirectory(OutputRoot);
                    File.WriteAllText(StatePath, JsonUtility.ToJson(state, true));
                }
                return SetRigProfile(rig, profile, save, "ASSIGNED", state.previous);
            }

            static string Revert(bool save)
            {
                NeedEdit();
                var state = LoadState();
                Need(state != null && !string.IsNullOrEmpty(state.previous), "No recorded previous profile (" + StatePath + ").");
                var previous = LoadProfile(state.previous);
                Need(previous != null, "Recorded previous profile is missing: " + state.previous);
                return SetRigProfile(SceneRig(), previous, save, "REVERTED", state.previous);
            }

            static string SetRigProfile(WorldMacroPlayerGestureRig rig, WorldMacroPlayerGestureProfile profile, bool save, string label, string previous)
            {
                Scene scene = rig.gameObject.scene;
                bool wasDirty = scene.isDirty;
                var serialized = new SerializedObject(rig);
                var property = serialized.FindProperty("_profile");
                Need(property != null, "Gesture rig field _profile not found.");
                property.objectReferenceValue = profile;
                serialized.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = false;
                string note = "";
                if (save && !wasDirty) saved = EditorSceneManager.SaveScene(scene);
                else if (save) note = " Scene already had unsaved changes before this command; NOT saved — save it deliberately.";
                return label + " rig=" + HierarchyPath(rig.transform) + " profile=" + AssetDatabase.GetAssetPath(profile)
                    + " previous=" + previous + " scene=" + scene.path + " saved=" + saved + "." + note;
            }

            static string StatusJson()
            {
                var profile = LoadProfile(ProfileAsset);
                var status = new Status { profileExists = profile != null, playing = EditorApplication.isPlaying,
                    shuanggouGrip = profile != null && profile.ShuanggouGrip, brushScale = profile != null ? profile.BrushScale : 0f,
                    shuanggouGripPosition = profile != null ? profile.ShuanggouGripPosition : Vector3.zero,
                    state = File.Exists(StatePath) ? File.ReadAllText(StatePath) : null };
                var rigs = Object.FindObjectsByType<WorldMacroPlayerGestureRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (rigs.Length > 0)
                {
                    status.scene = ScenePath(rigs[0]); status.rig = HierarchyPath(rigs[0].transform);
                    status.profile = AssetDatabase.GetAssetPath(rigs[0].Profile);
                }
                return JsonUtility.ToJson(status, true);
            }

            static string Capture(string state, string label)
            {
                Need(EditorApplication.isPlaying, "capture/check run in an unpaused Play session (RuntimeGestureQa).");
                var rig = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
                Need(rig != null, "No active gesture rig.");
                string name = string.IsNullOrEmpty(label)
                    ? (rig.Profile != null && rig.Profile.ShuanggouGrip ? "shuanggou" : "baseline")
                    : new string(label.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
                string directory = Path.Combine(OutputRoot, name);
                Directory.CreateDirectory(directory);
                return RuntimeGestureQa(state, directory, true);
            }

            // ---------------------------------------------------------------- fit

            sealed class Chain
            {
                public string Digit;
                public bool Thumb;
                public readonly WorldMacroPlayerGestureProfile.FingerPose[] Poses = new WorldMacroPlayerGestureProfile.FingerPose[3];
                public readonly Transform[] Bones = new Transform[3];
                public readonly Vector3[] LocalPosition = new Vector3[3], LocalScale = new Vector3[3], FlexAxis = new Vector3[3], Joint = new Vector3[3];
                public readonly Quaternion[] Rest = new Quaternion[3], Rot = new Quaternion[3];
                public readonly float[] Flex = new float[3], FlexMin = new float[3], FlexMax = new float[3];
                public Vector3 AbdAxis, Distal, Tip, Effector, Knuckle, Target;
                public float Abd, AbdLimit, Thickness, Length, Weight;
                public Matrix4x4 Prefix;
                public Quaternion PrefixRotation;

                public Quaternion Local(int j) => Quaternion.AngleAxis(Flex[j], FlexAxis[j])
                    * (j == 0 ? Quaternion.AngleAxis(Abd, AbdAxis) : Quaternion.identity) * Rest[j];

                public Quaternion ParentRotation(int j) => j == 0 ? PrefixRotation : Rot[j - 1];

                public void Forward()
                {
                    Matrix4x4 m = Prefix;
                    Quaternion r = PrefixRotation;
                    for (int j = 0; j < 3; j++)
                    {
                        Quaternion local = Local(j);
                        m = m * Matrix4x4.TRS(LocalPosition[j], local, LocalScale[j]);
                        r = r * local;
                        Joint[j] = m.MultiplyPoint3x4(Vector3.zero);
                        Rot[j] = r;
                    }
                    Tip = m.MultiplyPoint3x4(Distal);
                    Effector = m.MultiplyPoint3x4(Distal * .6f); // pad (or nail) centre of the distal phalanx
                }

                public void Pose(float a, float b, float c) { Flex[0] = a; Flex[1] = b; Flex[2] = c; Abd = 0f; Forward(); }
            }

            static Chain BindChain(Transform hand, WorldMacroPlayerGestureProfile profile, string digit, Vector3 palmar, Vector3 dorsal, Vector3 ulnar, List<string> notes)
            {
                var chain = new Chain { Digit = digit, Thumb = digit == "Thumb", AbdAxis = Vector3.right };
                for (int j = 0; j < 3; j++)
                {
                    chain.FlexAxis[j] = Vector3.right;
                    string name = "RightHand" + digit + (j + 1);
                    chain.Poses[j] = profile.Fingers?.FirstOrDefault(f => f != null && f.BoneName == name);
                    chain.Bones[j] = Find(hand, name);
                    if (chain.Poses[j] == null || chain.Bones[j] == null) { notes.Add("Skipped " + digit + ": " + name + " missing in profile or hand."); return null; }
                    if (j > 0 && chain.Bones[j].parent != chain.Bones[j - 1]) { notes.Add("Skipped " + digit + ": " + name + " is not a direct child."); return null; }
                    chain.LocalPosition[j] = chain.Bones[j].localPosition;
                    chain.LocalScale[j] = chain.Bones[j].localScale;
                    chain.Rest[j] = chain.Poses[j].UseExplicitRestPose
                        ? WorldMacroPlayerGestureProfile.SafeRotation(chain.Poses[j].RestLocalRotation) : chain.Bones[j].localRotation;
                }
                Transform parent = chain.Bones[0].parent;
                chain.Prefix = hand.worldToLocalMatrix * parent.localToWorldMatrix;
                chain.PrefixRotation = Quaternion.Inverse(hand.rotation) * parent.rotation;
                Transform distal = chain.Bones[2];
                Transform end = null;
                foreach (Transform child in distal) if (end == null || child.localPosition.sqrMagnitude > end.localPosition.sqrMagnitude) end = child;
                chain.Distal = end != null && end.localPosition.sqrMagnitude > 1e-10f ? end.localPosition
                    : distal.localPosition.normalized * distal.localPosition.magnitude * (chain.Thumb ? .80f : .85f);
                // Rest pose → anatomical axes. Flexion bends toward the palm (the thumb toward palm and ulnar side).
                chain.Pose(0f, 0f, 0f);
                Vector3[] points = { chain.Joint[0], chain.Joint[1], chain.Joint[2], chain.Tip };
                Vector3 flexToward = chain.Thumb ? (palmar * .7f + ulnar * .7f).normalized : palmar;
                for (int j = 0; j < 3; j++)
                {
                    Vector3 dir = (points[j + 1] - points[j]).normalized;
                    Vector3 toward = Vector3.ProjectOnPlane(flexToward, dir);
                    Vector3 axis = toward.sqrMagnitude > 1e-8f ? Vector3.Cross(dir, toward.normalized).normalized : Vector3.Cross(dir, ulnar).normalized;
                    chain.FlexAxis[j] = Quaternion.Inverse(chain.ParentRotation(j)) * axis;
                }
                Vector3 dir0 = (points[1] - points[0]).normalized;
                Vector3 abduction = Vector3.ProjectOnPlane(dorsal, dir0);
                chain.AbdAxis = Quaternion.Inverse(chain.PrefixRotation) * (abduction.sqrMagnitude > 1e-8f ? abduction.normalized : ulnar);
                chain.Knuckle = chain.Joint[0];
                chain.Length = Vector3.Distance(points[0], points[1]) + Vector3.Distance(points[1], points[2]) + Vector3.Distance(points[2], points[3]);
                if (chain.Thumb)
                {
                    chain.FlexMin[0] = -15f; chain.FlexMax[0] = 55f; chain.FlexMin[1] = -10f; chain.FlexMax[1] = 60f;
                    chain.FlexMin[2] = -15f; chain.FlexMax[2] = 80f; chain.AbdLimit = 30f;
                }
                else
                {
                    chain.FlexMin[0] = -10f; chain.FlexMax[0] = 90f; chain.FlexMin[1] = 0f; chain.FlexMax[1] = 105f;
                    chain.FlexMin[2] = 0f; chain.FlexMax[2] = 80f; chain.AbdLimit = 15f;
                }
                chain.Weight = digit == "Index" || digit == "Thumb" ? 1.3f : digit == "Middle" ? 1.1f : digit == "Ring" ? .9f : .5f;
                return chain;
            }

            static float Hinge(Vector3 effector, Vector3 target, Vector3 joint, Vector3 axis)
            {
                Vector3 from = Vector3.ProjectOnPlane(effector - joint, axis), to = Vector3.ProjectOnPlane(target - joint, axis);
                if (from.sqrMagnitude < 1e-14f || to.sqrMagnitude < 1e-14f) return 0f;
                return Vector3.SignedAngle(from, to, axis);
            }

            // Hinge-constrained CCD: distal → proximal flexion, plus MCP/CMC abduction, with a soft DIP≈⅔PIP coupling.
            static void Solve(Chain c, Vector3 target, float tolerance)
            {
                c.Target = target;
                if (c.Thumb) c.Pose(10f, 15f, 15f); else c.Pose(25f, 45f, 25f);
                for (int iteration = 0; iteration < 60; iteration++)
                {
                    for (int j = 2; j >= 0; j--)
                    {
                        c.Forward();
                        Vector3 axis = (c.ParentRotation(j) * c.FlexAxis[j]).normalized;
                        c.Flex[j] = Mathf.Clamp(c.Flex[j] + Hinge(c.Effector, target, c.Joint[j], axis), c.FlexMin[j], c.FlexMax[j]);
                        if (j != 0) continue;
                        c.Forward();
                        Vector3 abduction = (c.PrefixRotation * c.AbdAxis).normalized;
                        c.Abd = Mathf.Clamp(c.Abd + Hinge(c.Effector, target, c.Joint[0], abduction), -c.AbdLimit, c.AbdLimit);
                    }
                    if (!c.Thumb) c.Flex[2] = Mathf.Lerp(c.Flex[2], Mathf.Clamp(c.Flex[1] * .67f, c.FlexMin[2], c.FlexMax[2]), .2f);
                    c.Forward();
                    if ((c.Effector - target).sqrMagnitude < tolerance * tolerance) break;
                }
                c.Forward();
            }

            static float Clearance(Chain c, Vector3 origin, Vector3 axis, float radius, out float penalty)
            {
                Vector3[] points = { c.Joint[0], c.Joint[1], c.Joint[2], c.Tip };
                float contact = radius + c.Thickness, clearance = float.PositiveInfinity;
                penalty = 0f;
                for (int s = 0; s < 3; s++)
                    for (int k = 0; k <= 5; k++)
                    {
                        Vector3 x = Vector3.Lerp(points[s], points[s + 1], k / 5f);
                        float d = Vector3.ProjectOnPlane(x - origin, axis).magnitude - contact;
                        clearance = Mathf.Min(clearance, d);
                        penalty += Mathf.Max(0f, -d - .1f * contact);
                    }
                return clearance;
            }

            sealed class Frame
            {
                public Vector3 Forward, Palmar, Axis, Front, PalmSide;
                public float Radius;
                public Dictionary<string, Chain> Chains;
                public float Tolerance;
            }

            static float Level(Frame f, Vector3 origin, Vector3 point) => Vector3.Dot(point - origin, f.Axis);

            static Vector3 Contact(Frame f, Vector3 origin, float level, Vector3 direction, float distance) =>
                origin + f.Axis * level + Vector3.ProjectOnPlane(direction, f.Axis).normalized * distance;

            static float Evaluate(Frame f, float forward, float palmar)
            {
                Vector3 origin = f.Forward * forward + f.Palmar * palmar;
                var ch = f.Chains;
                float cost = 0f;
                float indexLevel = ch.TryGetValue("Index", out Chain index) ? Level(f, origin, index.Knuckle) : 0f;
                float middleLevel = ch.TryGetValue("Middle", out Chain middle) ? Level(f, origin, middle.Knuckle) : indexLevel;
                foreach (string digit in Digits)
                {
                    if (!ch.TryGetValue(digit, out Chain c)) continue;
                    float reach = f.Radius + c.Thickness;
                    Vector3 target;
                    switch (digit)
                    {
                        case "Index": target = Contact(f, origin, indexLevel, f.Front * .9f + f.PalmSide * .45f, reach); break;
                        case "Middle": target = Contact(f, origin, middleLevel, f.Front + f.PalmSide * .25f, reach); break;
                        case "Thumb": target = Contact(f, origin, Mathf.Lerp(indexLevel, middleLevel, .3f), -f.Front + f.PalmSide * .3f, reach); break;
                        case "Ring": target = Contact(f, origin, Level(f, origin, c.Knuckle), -f.PalmSide + f.Front * .15f, reach); break;
                        default:
                            // Little finger tucked behind the ring finger, on its palm side.
                            if (ch.TryGetValue("Ring", out Chain ring))
                                target = ring.Effector + f.Axis * ((Level(f, origin, c.Knuckle) - Level(f, origin, ring.Knuckle)) * .8f)
                                    - f.PalmSide * (ring.Thickness + c.Thickness);
                            else target = Contact(f, origin, Level(f, origin, c.Knuckle), -f.PalmSide, reach + c.Thickness * 2f);
                            break;
                    }
                    Solve(c, target, f.Tolerance);
                    Clearance(c, origin, f.Axis, f.Radius, out float penalty);
                    cost += c.Weight * Vector3.Distance(c.Effector, target) / Mathf.Max(1e-5f, c.Length) + 4f * penalty / Mathf.Max(1e-5f, c.Length);
                }
                return cost;
            }

            static string Fit(string source)
            {
                NeedEdit();
                var profile = LoadProfile(ProfileAsset);
                Need(profile != null, "Run make-profile first (" + ProfileAsset + ").");
                var rig = SceneRig();
                var report = new FitReport { utc = DateTime.UtcNow.ToString("o"), scene = ScenePath(rig), rig = HierarchyPath(rig.transform),
                    profile = ProfileAsset, source = source ?? AssetDatabase.GetAssetPath(rig.Profile), brushScale = profile.BrushScale };
                var animator = QaGet<Animator>(rig, "_animator");
                Need(animator != null, "Gesture rig has no Animator.");
                Transform hand = animator.isHuman && animator.avatar != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                if (hand == null) hand = Find(animator.transform, profile.RightHandName);
                Need(hand != null, "Right hand bone not found.");
                report.handBone = HierarchyPath(hand);
                Vector3 lossy = hand.lossyScale;
                report.handScale = lossy.x;
                if (Mathf.Abs(lossy.x - lossy.y) > .01f * lossy.x || Mathf.Abs(lossy.x - lossy.z) > .01f * lossy.x)
                    report.notes.Add("Hand lossy scale is not uniform " + lossy.ToString("F4") + "; x is used.");
                float unit = Mathf.Max(1e-6f, Mathf.Abs(lossy.x)); // metres per hand-local unit

                // Brush geometry in the GripSocket frame (brush root is unit scale, so this is metres).
                Transform brushRoot = QaGet<Transform>(rig, "_worldBrushRoot");
                Need(brushRoot != null, "World brush root is not bound on the rig.");
                Transform grip = QaGet<Transform>(rig, "_worldGripSocket"); if (grip == null) grip = Find(brushRoot, "GripSocket");
                Transform tip = QaGet<Transform>(rig, "_worldTipSocket"); if (tip == null) tip = Find(brushRoot, "TipSocket");
                Need(grip != null && tip != null && grip != tip, "World brush GripSocket/TipSocket missing.");
                Transform shaftEnd = Find(brushRoot, "ShaftEndSocket"); if (shaftEnd == null) shaftEnd = Find(brushRoot, "Bristle_01");
                Quaternion toGrip = Quaternion.Inverse(grip.rotation);
                Vector3 tipLocal = toGrip * (tip.position - grip.position);
                report.gripToTipMeters = tipLocal.magnitude;
                Need(report.gripToTipMeters > .02f, "GripSocket and TipSocket coincide.");
                Vector3 tipDirection = tipLocal / report.gripToTipMeters;
                report.ferruleAlongMeters = shaftEnd != null ? Vector3.Dot(toGrip * (shaftEnd.position - grip.position), tipDirection) : report.gripToTipMeters * .6f;
                var samples = new List<Vector2>(4096);
                report.handleMeasurement = MeasureHandle(brushRoot, grip, shaftEnd, toGrip, tipDirection, samples, report.notes);
                report.handleButtAlongMeters = samples.Count > 0 ? samples.Min(s => s.x) : 0f;
                float span = Mathf.Max(.05f, report.ferruleAlongMeters - report.handleButtAlongMeters);
                report.holdFromFerruleMeters = profile.ShuanggouHoldFromFerrule > 0f ? profile.ShuanggouHoldFromFerrule : .65f * span;
                report.holdAlongMeters = Mathf.Clamp(report.ferruleAlongMeters - report.holdFromFerruleMeters,
                    report.handleButtAlongMeters + .03f, Mathf.Max(report.handleButtAlongMeters + .03f, report.ferruleAlongMeters - .05f));
                report.graspToTipMeters = report.gripToTipMeters - report.holdAlongMeters;
                report.shaftRadiusMeters = ShaftRadius(samples, report.holdAlongMeters, report.notes);

                // Hand frame from the calibrated profile axes (hand-local units).
                Vector3 forward = profile.HandForwardLocal.normalized;
                Vector3 dorsal = Vector3.ProjectOnPlane(profile.HandDorsalLocal, forward).normalized;
                Vector3 radial = Vector3.Cross(forward, dorsal).normalized;
                if (Vector3.Dot(radial, profile.HandThumbSideLocal) < 0f) { radial = -radial; report.notes.Add("Radial axis sign taken from HandThumbSideLocal."); }
                Vector3 palmar = -dorsal, ulnar = -radial;
                var frame = new Frame { Forward = forward, Palmar = palmar, Axis = ulnar, Radius = report.shaftRadiusMeters / unit,
                    Tolerance = .0004f / unit, Chains = new Dictionary<string, Chain>() };
                frame.Front = Vector3.ProjectOnPlane(forward, ulnar).normalized;
                frame.PalmSide = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(palmar, ulnar), frame.Front).normalized;
                foreach (string digit in Digits)
                {
                    var chain = BindChain(hand, profile, digit, palmar, dorsal, ulnar, report.notes);
                    if (chain == null) continue;
                    chain.Thickness = Mathf.Clamp(.32f * chain.LocalPosition[2].magnitude * unit, .005f, .012f) / unit;
                    frame.Chains[digit] = chain;
                }
                Need(frame.Chains.ContainsKey("Index") && frame.Chains.ContainsKey("Middle") && frame.Chains.ContainsKey("Thumb"),
                    "Thumb, index and middle chains are required for the double-hook fit.");

                // Grid search of the shaft line (in front of the hollow palm), then a half-step refinement.
                var indexChain = frame.Chains["Index"];
                float knuckles = (Vector3.Dot(indexChain.Knuckle, forward) + Vector3.Dot(frame.Chains["Middle"].Knuckle, forward)) * .5f;
                float length = indexChain.Length;
                float bestForward = knuckles + length * .15f, bestPalmar = length * .55f, best = float.PositiveInfinity;
                foreach (float f in new[] { -.05f, .05f, .15f, .25f, .35f })
                    foreach (float p in new[] { .35f, .45f, .55f, .65f, .75f })
                    {
                        float cost = Evaluate(frame, knuckles + length * f, length * p);
                        if (cost < best) { best = cost; bestForward = knuckles + length * f; bestPalmar = length * p; }
                    }
                float centreForward = bestForward, centrePalmar = bestPalmar;
                for (int i = -1; i <= 1; i++)
                    for (int k = -1; k <= 1; k++)
                    {
                        if (i == 0 && k == 0) continue;
                        float f = centreForward + i * length * .05f, p = centrePalmar + k * length * .05f;
                        float cost = Evaluate(frame, f, p);
                        if (cost < best) { best = cost; bestForward = f; bestPalmar = p; }
                    }
                report.cost = Evaluate(frame, bestForward, bestPalmar);
                report.shaftForwardMeters = bestForward * unit;
                report.shaftPalmarMeters = bestPalmar * unit;

                // Write DrawingOffset only (UseExplicitRestPose entries: offset = Inverse(Rest) * local).
                Vector3 origin = forward * bestForward + palmar * bestPalmar;
                var digits = new List<FitDigit>();
                bool findings = false;
                foreach (string digit in Digits)
                {
                    if (!frame.Chains.TryGetValue(digit, out Chain c)) continue;
                    for (int j = 0; j < 3; j++)
                        c.Poses[j].DrawingOffset = (Quaternion.Inverse(c.Rest[j]) * c.Local(j)).normalized;
                    float clearance = Clearance(c, origin, ulnar, frame.Radius, out _);
                    var row = new FitDigit { digit = digit, residualMm = Vector3.Distance(c.Effector, c.Target) * unit * 1000f,
                        clearanceMm = clearance * unit * 1000f, abductionDegrees = c.Abd, flexDegrees = c.Flex.ToArray() };
                    findings |= row.residualMm > 4f || row.clearanceMm < -2f;
                    digits.Add(row);
                }
                report.digits = digits.ToArray();

                // Vertical grip frame: shaft along the ulnar axis through the fitted line; GripSocket sits holdAlong
                // behind the grasp centre (index/middle level), toward the butt.
                float holdLevel = (Level(frame, origin, indexChain.Knuckle) + Level(frame, origin, frame.Chains["Middle"].Knuckle)) * .5f;
                Vector3 hold = origin + ulnar * holdLevel;
                profile.ShuanggouGripRotation = Quaternion.FromToRotation(tipDirection, ulnar).normalized;
                profile.ShuanggouGripPosition = hold - ulnar * (report.holdAlongMeters / unit);
                profile.ShuanggouGrip = true;
                report.shuanggouGripPosition = profile.ShuanggouGripPosition;
                report.shuanggouGripRotation = profile.ShuanggouGripRotation;
                report.status = findings ? "FIT_FINDINGS_IMPLEMENTED" : "FIT_IMPLEMENTED";
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                string directory = Path.Combine(OutputRoot, "fit");
                Directory.CreateDirectory(directory);
                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Path.Combine(directory, "fit_report.json"), json);
                return json;
            }

            // Pen (tripod) grasp for the forward-reaching hand: fingers point toward the look direction, back of the
            // hand up. The shaft leaves the hand between thumb and index toward the fingertips (pitched toward the palm,
            // slightly ulnar); index pad on top, thumb pad on the radial side, middle finger under it, ring and little
            // finger tucked below. pen[:pitch[:yaw[:holdFromFerrule]]] in degrees / metres. Writes DrawingOffset and the
            // vertical grip frame (the rig maps the shaft to the drawing axis and the back of the hand to view-up).
            static string PenFit(string[] args)
            {
                NeedEdit();
                float Arg(int i, float d) => args.Length > i && float.TryParse(args[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : d;
                float pitch = Arg(0, 22f), yaw = Arg(1, 8f);
                var profile = LoadProfile(ProfileAsset);
                Need(profile != null, "Run make-profile first (" + ProfileAsset + ").");
                if (args.Length > 2) profile.ShuanggouHoldFromFerrule = Arg(2, profile.ShuanggouHoldFromFerrule);
                var rig = SceneRig();
                var report = new FitReport { utc = DateTime.UtcNow.ToString("o"), scene = ScenePath(rig), rig = HierarchyPath(rig.transform),
                    profile = ProfileAsset, source = "pen pitch=" + pitch + " yaw=" + yaw, brushScale = profile.BrushScale };
                var animator = QaGet<Animator>(rig, "_animator");
                Need(animator != null, "Gesture rig has no Animator.");
                Transform hand = animator.isHuman && animator.avatar != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                if (hand == null) hand = Find(animator.transform, profile.RightHandName);
                Need(hand != null, "Right hand bone not found.");
                report.handBone = HierarchyPath(hand);
                float unit = Mathf.Max(1e-6f, Mathf.Abs(hand.lossyScale.x));
                report.handScale = unit;

                Transform brushRoot = QaGet<Transform>(rig, "_worldBrushRoot");
                Need(brushRoot != null, "World brush root is not bound on the rig.");
                Transform grip = QaGet<Transform>(rig, "_worldGripSocket"); if (grip == null) grip = Find(brushRoot, "GripSocket");
                Transform tip = QaGet<Transform>(rig, "_worldTipSocket"); if (tip == null) tip = Find(brushRoot, "TipSocket");
                Need(grip != null && tip != null && grip != tip, "World brush GripSocket/TipSocket missing.");
                Transform shaftEnd = Find(brushRoot, "ShaftEndSocket"); if (shaftEnd == null) shaftEnd = Find(brushRoot, "Bristle_01");
                Quaternion toGrip = Quaternion.Inverse(grip.rotation);
                Vector3 tipLocal = toGrip * (tip.position - grip.position);
                report.gripToTipMeters = tipLocal.magnitude;
                Need(report.gripToTipMeters > .02f, "GripSocket and TipSocket coincide.");
                Vector3 tipDirection = tipLocal / report.gripToTipMeters;
                report.ferruleAlongMeters = shaftEnd != null ? Vector3.Dot(toGrip * (shaftEnd.position - grip.position), tipDirection) : report.gripToTipMeters * .6f;
                var samples = new List<Vector2>(4096);
                report.handleMeasurement = MeasureHandle(brushRoot, grip, shaftEnd, toGrip, tipDirection, samples, report.notes);
                report.handleButtAlongMeters = samples.Count > 0 ? samples.Min(s => s.x) : 0f;
                report.holdFromFerruleMeters = profile.ShuanggouHoldFromFerrule > 0f ? profile.ShuanggouHoldFromFerrule : .08f;
                report.holdAlongMeters = Mathf.Clamp(report.ferruleAlongMeters - report.holdFromFerruleMeters,
                    report.handleButtAlongMeters + .03f, Mathf.Max(report.handleButtAlongMeters + .03f, report.ferruleAlongMeters - .02f));
                report.graspToTipMeters = report.gripToTipMeters - report.holdAlongMeters;
                report.shaftRadiusMeters = ShaftRadius(samples, report.holdAlongMeters, report.notes);

                Vector3 forward = profile.HandForwardLocal.normalized;
                Vector3 dorsal = Vector3.ProjectOnPlane(profile.HandDorsalLocal, forward).normalized;
                Vector3 radial = Vector3.Cross(forward, dorsal).normalized;
                if (Vector3.Dot(radial, profile.HandThumbSideLocal) < 0f) radial = -radial;
                Vector3 palmar = -dorsal, ulnar = -radial;
                var chains = new Dictionary<string, Chain>();
                foreach (string digit in Digits)
                {
                    var chain = BindChain(hand, profile, digit, palmar, dorsal, ulnar, report.notes);
                    if (chain == null) continue;
                    chain.Thickness = Mathf.Clamp(.32f * chain.LocalPosition[2].magnitude * unit, .005f, .012f) / unit;
                    chains[digit] = chain;
                }
                Need(chains.ContainsKey("Index") && chains.ContainsKey("Middle") && chains.ContainsKey("Thumb"), "Thumb, index and middle chains are required.");
                // Relaxed tripod curl (the forward-reaching hand): index/middle/thumb scaled by `open` until their pads sit on
                // the shaft surface; ring and little finger keep a tucked curl. The shaft runs from the thumb-index web to the
                // pinch point (pad centroid), so its pitch/yaw follow the rig's own finger proportions.
                var index = chains["Index"]; var middle = chains["Middle"]; var thumb = chains["Thumb"];
                float radius = report.shaftRadiusMeters / unit;
                void PoseAll(float open)
                {
                    index.Pose(28f * open, 36f * open, 16f * open);
                    middle.Pose(36f * open, 46f * open, 22f * open);
                    thumb.Pose(Mathf.Clamp(24f * open, -15f, 55f), Mathf.Clamp(16f * open, -10f, 60f), Mathf.Clamp(14f * open, -15f, 80f));
                    thumb.Abd = Mathf.Clamp(12f, -thumb.AbdLimit, thumb.AbdLimit); thumb.Forward();
                    if (chains.TryGetValue("Ring", out Chain ring)) ring.Pose(46f, 62f, 38f);
                    if (chains.TryGetValue("Pinky", out Chain pinky)) pinky.Pose(52f, 66f, 38f);
                }
                Vector3 Web() => Vector3.Lerp(index.Knuckle, thumb.Joint[1], .5f) + dorsal * radius * .6f;
                Vector3 Pinch() => (index.Effector + middle.Effector + thumb.Effector) / 3f;
                float TryOpen(float open, out Vector3 axis, out Vector3 centre)
                {
                    PoseAll(open);
                    centre = Pinch(); axis = (centre - Web()).normalized;
                    float err = 0f;
                    foreach (var c in new[] { index, middle, thumb })
                    {
                        float d = Vector3.ProjectOnPlane(c.Effector - centre, axis).magnitude;
                        err += Mathf.Abs(d - (radius + c.Thickness));
                    }
                    return err;
                }
                // relaxed curl; the shaft axis runs web -> pinch, and its surface sits under the index pad (index on top,
                // radial side); thumb and middle then bend onto the surface (CCD), ring/little finger stay tucked
                float bestOpen = args.Length > 0 ? Mathf.Clamp(Arg(0, 1f), .1f, 1.5f) : 1f;
                TryOpen(bestOpen, out Vector3 pen, out _);
                Vector3 Side(Vector3 v) => Vector3.ProjectOnPlane(v, pen).normalized;
                // the index arches over the shaft (rounded, pad on top) independently of the open middle/thumb curl
                float indexCurl = args.Length > 1 && Arg(1, 0f) > 0f ? Arg(1, 1f) : 1f;
                index.Pose(32f * indexCurl, 50f * indexCurl, 26f * indexCurl);
                float clearanceScale = args.Length > 4 ? Arg(4, 1.0f) : 1.0f;
                // pad-on-surface factor for thumb and middle (1 = the capsule pad touches the shaft surface)
                float contact = args.Length > 5 ? Arg(5, 1.0f) : 1.0f;
                Vector3 bestOrigin = index.Effector + Side(palmar * .85f + ulnar * .45f) * (radius + index.Thickness) * clearanceScale;
                var f = new Frame { Forward = forward, Palmar = palmar, Axis = pen, Radius = radius, Tolerance = .0004f / unit, Chains = chains };
                Solve(thumb, Contact(f, bestOrigin, -.012f / unit, Side(radial * 1f + palmar * .35f), (radius + thumb.Thickness) * contact), f.Tolerance);
                Solve(middle, Contact(f, bestOrigin, .010f / unit, Side(palmar * .35f + ulnar * 1f), (radius + middle.Thickness) * contact), f.Tolerance);
                float best = Vector3.Distance(thumb.Effector, thumb.Target) + Vector3.Distance(middle.Effector, middle.Target);
                float pitchOut = Mathf.Atan2(Vector3.Dot(pen, palmar), Vector3.Dot(pen, forward)) * Mathf.Rad2Deg;
                float yawOut = Mathf.Atan2(Vector3.Dot(pen, ulnar), Vector3.Dot(pen, forward)) * Mathf.Rad2Deg;
                report.source = "pen tripod open=" + bestOpen.ToString("F2") + " indexCurl=" + indexCurl.ToString("F2") + " clearance=" + clearanceScale.ToString("F2") + " pitch=" + pitchOut.ToString("F1") + " yaw=" + yawOut.ToString("F1");
                report.cost = best * unit;
                Vector3 knuckle = index.Knuckle;
                report.shaftForwardMeters = Vector3.Dot(bestOrigin - knuckle, forward) * unit;
                report.shaftPalmarMeters = Vector3.Dot(bestOrigin - knuckle, palmar) * unit;
                var digits = new List<FitDigit>();
                bool findings = Vector3.Dot(pen, forward) < .5f;
                if (findings) report.notes.Add("Shaft is more than 60 degrees off the finger direction.");
                foreach (string digit in Digits)
                {
                    if (!chains.TryGetValue(digit, out Chain c)) continue;
                    if (c != thumb && c != middle) c.Target = c.Effector;
                    for (int j = 0; j < 3; j++) c.Poses[j].DrawingOffset = (Quaternion.Inverse(c.Rest[j]) * c.Local(j)).normalized;
                    float clearance = Clearance(c, bestOrigin, pen, radius, out _);
                    var row = new FitDigit { digit = digit, residualMm = (Vector3.ProjectOnPlane(c.Effector - bestOrigin, pen).magnitude - radius - c.Thickness) * unit * 1000f,
                        clearanceMm = clearance * unit * 1000f, abductionDegrees = c.Abd, flexDegrees = c.Flex.ToArray() };
                    findings |= row.clearanceMm < -4f;
                    digits.Add(row);
                }
                report.digits = digits.ToArray();
                // the index contact is the hold point; the GripSocket sits holdAlong behind it toward the butt
                profile.ShuanggouGripRotation = Quaternion.FromToRotation(tipDirection, pen).normalized;
                profile.ShuanggouGripPosition = bestOrigin - pen * (report.holdAlongMeters / unit);
                profile.ShuanggouGrip = true;
                profile.ShuanggouDorsalFacing = true;
                profile.ShuanggouElbowRule = true;
                report.shuanggouGripPosition = profile.ShuanggouGripPosition;
                report.shuanggouGripRotation = profile.ShuanggouGripRotation;
                report.status = findings ? "PEN_FIT_FINDINGS_IMPLEMENTED" : "PEN_FIT_IMPLEMENTED";
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                string directory = Path.Combine(OutputRoot, "fit");
                Directory.CreateDirectory(directory);
                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Path.Combine(directory, "pen_fit_report.json"), json);
                return json;
            }

            // Fist (power) grasp for the big brush: the shaft crosses the palm obliquely and leaves the fist on the little-finger
            // side (tilted distally by `tilt` degrees); the four fingers wrap around it, the thumb closes over the index/middle.
            // fist[:tilt[:holdFromFerrule[:size]]] — degrees, runtime metres, uniform brush size. Lengths measured in Edit
            // mode are scaled by `size` (the rig scales the brush at bind). Writes DrawingOffset, the grip frame and the
            // fist-mode switches (ShuanggouFist, BrushSize, handle stretch off).
            static string FistFit(string[] args)
            {
                NeedEdit();
                float Arg(int i, float d) => args.Length > i && float.TryParse(args[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : d;
                float tilt = Arg(0, 30f), holdFromFerrule = Arg(1, .22f), size = Mathf.Clamp(Arg(2, 1.3f), .5f, 3f);
                var profile = LoadProfile(ProfileAsset);
                Need(profile != null, "Run make-profile first (" + ProfileAsset + ").");
                var rig = SceneRig();
                var report = new FitReport { utc = DateTime.UtcNow.ToString("o"), scene = ScenePath(rig), rig = HierarchyPath(rig.transform),
                    profile = ProfileAsset, source = "fist tilt=" + tilt + " size=" + size, brushScale = size };
                var animator = QaGet<Animator>(rig, "_animator");
                Need(animator != null, "Gesture rig has no Animator.");
                Transform hand = animator.isHuman && animator.avatar != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                if (hand == null) hand = Find(animator.transform, profile.RightHandName);
                Need(hand != null, "Right hand bone not found.");
                report.handBone = HierarchyPath(hand);
                float unit = Mathf.Max(1e-6f, Mathf.Abs(hand.lossyScale.x));
                report.handScale = unit;

                Transform brushRoot = QaGet<Transform>(rig, "_worldBrushRoot");
                Need(brushRoot != null, "World brush root is not bound on the rig.");
                Transform grip = QaGet<Transform>(rig, "_worldGripSocket"); if (grip == null) grip = Find(brushRoot, "GripSocket");
                Transform tip = QaGet<Transform>(rig, "_worldTipSocket"); if (tip == null) tip = Find(brushRoot, "TipSocket");
                Need(grip != null && tip != null && grip != tip, "World brush GripSocket/TipSocket missing.");
                Transform shaftEnd = Find(brushRoot, "ShaftEndSocket"); if (shaftEnd == null) shaftEnd = Find(brushRoot, "Bristle_01");
                Quaternion toGrip = Quaternion.Inverse(grip.rotation);
                Vector3 tipLocal = toGrip * (tip.position - grip.position);
                float gripToTip = tipLocal.magnitude;
                Need(gripToTip > .02f, "GripSocket and TipSocket coincide.");
                Vector3 tipDirection = tipLocal / gripToTip;
                float ferruleAlong = shaftEnd != null ? Vector3.Dot(toGrip * (shaftEnd.position - grip.position), tipDirection) : gripToTip * .6f;
                var samples = new List<Vector2>(4096);
                report.handleMeasurement = MeasureHandle(brushRoot, grip, shaftEnd, toGrip, tipDirection, samples, report.notes);
                float butt = samples.Count > 0 ? samples.Min(s => s.x) : 0f;
                // runtime (scaled) brush
                report.gripToTipMeters = gripToTip * size;
                report.ferruleAlongMeters = ferruleAlong * size;
                report.handleButtAlongMeters = butt * size;
                report.holdFromFerruleMeters = holdFromFerrule;
                report.holdAlongMeters = Mathf.Clamp(report.ferruleAlongMeters - holdFromFerrule,
                    report.handleButtAlongMeters + .05f, Mathf.Max(report.handleButtAlongMeters + .05f, report.ferruleAlongMeters - .04f));
                report.graspToTipMeters = report.gripToTipMeters - report.holdAlongMeters;
                report.shaftRadiusMeters = ShaftRadius(samples, report.holdAlongMeters / size, report.notes) * size;

                Vector3 forward = profile.HandForwardLocal.normalized;
                Vector3 dorsal = Vector3.ProjectOnPlane(profile.HandDorsalLocal, forward).normalized;
                Vector3 radial = Vector3.Cross(forward, dorsal).normalized;
                if (Vector3.Dot(radial, profile.HandThumbSideLocal) < 0f) radial = -radial;
                Vector3 palmar = -dorsal, ulnar = -radial;
                Vector3 axis = (ulnar * Mathf.Cos(tilt * Mathf.Deg2Rad) + forward * Mathf.Sin(tilt * Mathf.Deg2Rad)).normalized;
                var chains = new Dictionary<string, Chain>();
                foreach (string digit in Digits)
                {
                    var chain = BindChain(hand, profile, digit, palmar, dorsal, ulnar, report.notes);
                    if (chain == null) continue;
                    chain.Thickness = Mathf.Clamp(.32f * chain.LocalPosition[2].magnitude * unit, .005f, .012f) / unit;
                    chains[digit] = chain;
                }
                Need(chains.ContainsKey("Index") && chains.ContainsKey("Middle") && chains.ContainsKey("Thumb"), "Thumb, index and middle chains are required.");
                float radius = report.shaftRadiusMeters / unit;
                var f = new Frame { Forward = forward, Palmar = palmar, Axis = axis, Radius = radius, Tolerance = .0004f / unit, Chains = chains };
                Vector3 front = Vector3.ProjectOnPlane(forward, axis).normalized;
                Vector3 palmSide = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(palmar, axis), front).normalized;
                Vector3 wrap = (palmSide * .75f - front * .65f).normalized;          // fingertips curl back on the far side
                Vector3 over = (palmSide * 1f + front * .15f).normalized;           // thumb closes over the fingers
                var index = chains["Index"]; var middle = chains["Middle"]; var thumb = chains["Thumb"];
                float length = index.Length;
                Vector3 knuckles = Vector3.zero; int count = 0;
                foreach (string d in new[] { "Index", "Middle", "Ring", "Pinky" }) if (chains.TryGetValue(d, out Chain k)) { knuckles += k.Knuckle; count++; }
                knuckles /= Mathf.Max(1, count);
                // Parametric fist curl: every joint of a finger curls together (MCP:PIP:DIP ≈ 70:90:55, thumb 30:45:40);
                // per finger the curl amount that best lays its middle/distal segments on the shaft surface wins, with
                // penetration penalised (the thumb lies over the index/middle, one finger-thickness further out).
                float[] fingerCurl = { 70f, 90f, 55f }, thumbCurl = { 30f, 45f, 40f };
                float WrapCost(Chain c, Vector3 origin, float need)
                {
                    Vector3[] pts = { c.Joint[0], c.Joint[1], c.Joint[2], c.Tip };
                    float cost = 0f;
                    for (int sgm = 0; sgm < 3; sgm++)
                        for (int k = 0; k <= 4; k++)
                        {
                            float d = Vector3.ProjectOnPlane(Vector3.Lerp(pts[sgm], pts[sgm + 1], k / 4f) - origin, axis).magnitude - need;
                            if (d < 0f) cost += 4f * -d; else if (sgm > 0) cost += d;
                        }
                    // a fist wraps: the fingertip must come round to the far side (the thumb over the fingers)
                    Vector3 around = Vector3.ProjectOnPlane(c.Tip - origin, axis);
                    float wrapped = around.sqrMagnitude > 1e-10f ? Vector3.Dot(around.normalized, c.Thumb ? over : wrap) : -1f;
                    return cost / Mathf.Max(1e-5f, c.Length) + 3f * (1f - wrapped);
                }
                void Curl(Chain c, float k)
                {
                    float[] basis = c.Thumb ? thumbCurl : fingerCurl;
                    c.Pose(Mathf.Min(basis[0] * k, c.FlexMax[0]), Mathf.Min(basis[1] * k, c.FlexMax[1]), Mathf.Min(basis[2] * k, c.FlexMax[2]));
                    if (c.Thumb) { c.Abd = Mathf.Clamp(12f, -c.AbdLimit, c.AbdLimit); c.Forward(); }
                }
                float Score(Vector3 origin)
                {
                    float cost = 0f;
                    foreach (string digit in Digits)
                    {
                        if (!chains.TryGetValue(digit, out Chain c)) continue;
                        float need = radius + c.Thickness + (c.Thumb ? 2f * index.Thickness : 0f);
                        float bestK = 1f, bestCost = float.PositiveInfinity;
                        for (float k = .2f; k <= 1.45f; k += .03f)
                        {
                            Curl(c, k);
                            float w = WrapCost(c, origin, need);
                            if (w < bestCost) { bestCost = w; bestK = k; }
                        }
                        Curl(c, bestK);
                        c.Target = c.Effector;
                        cost += c.Weight * bestCost;
                    }
                    return cost;
                }
                Vector3 bestOrigin = knuckles;
                float best = float.PositiveInfinity;
                // the shaft lies across the base of the fingers (forward of the knuckle heads), palmar of the knuckles
                foreach (float a in new[] { -.3f, -.2f, -.1f, 0f, .1f, .2f, .3f, .4f })
                    foreach (float b in new[] { 0f, .05f, .1f, .15f, .2f })
                    {
                        Vector3 o = knuckles + forward * (a * length) + palmar * (radius + b * length);
                        float cost = Score(o);
                        if (cost < best) { best = cost; bestOrigin = o; }
                    }
                Vector3 centre = bestOrigin;
                for (int i = -1; i <= 1; i++)
                    for (int j = -1; j <= 1; j++)
                    {
                        if (i == 0 && j == 0) continue;
                        Vector3 o = centre + (forward * i + palmar * j) * (.05f * length);
                        float cost = Score(o);
                        if (cost < best) { best = cost; bestOrigin = o; }
                    }
                report.cost = Score(bestOrigin);
                report.shaftForwardMeters = Vector3.Dot(bestOrigin - knuckles, forward) * unit;
                report.shaftPalmarMeters = Vector3.Dot(bestOrigin - knuckles, palmar) * unit;
                var digits = new List<FitDigit>();
                bool findings = false;
                foreach (string digit in Digits)
                {
                    if (!chains.TryGetValue(digit, out Chain c)) continue;
                    for (int j = 0; j < 3; j++) c.Poses[j].DrawingOffset = (Quaternion.Inverse(c.Rest[j]) * c.Local(j)).normalized;
                    float clearance = Clearance(c, bestOrigin, axis, radius, out _);
                    var row = new FitDigit { digit = digit, residualMm = (Vector3.ProjectOnPlane(c.Tip - bestOrigin, axis).magnitude - radius - c.Thickness) * unit * 1000f,
                        clearanceMm = clearance * unit * 1000f, abductionDegrees = c.Abd, flexDegrees = c.Flex.ToArray() };
                    findings |= row.residualMm > 6f || row.clearanceMm < -4f;
                    digits.Add(row);
                }
                report.digits = digits.ToArray();
                // hold point = shaft at the middle finger's level; the GripSocket sits holdAlong behind it toward the butt
                Vector3 hold = bestOrigin + axis * Level(f, bestOrigin, middle.Knuckle);
                profile.ShuanggouGripRotation = Quaternion.FromToRotation(tipDirection, axis).normalized;
                profile.ShuanggouGripPosition = hold - axis * (report.holdAlongMeters / unit);
                profile.ShuanggouGrip = true;
                profile.ShuanggouFist = true;
                profile.BrushSize = size;
                profile.BrushScale = 1f;
                profile.NearMinimumDepth = Mathf.Min(profile.NearMinimumDepth, .5f);   // left strokes lay the brush across: the tip comes closer
                report.shuanggouGripPosition = profile.ShuanggouGripPosition;
                report.shuanggouGripRotation = profile.ShuanggouGripRotation;
                report.status = findings ? "FIST_FIT_FINDINGS_IMPLEMENTED" : "FIST_FIT_IMPLEMENTED";
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                string directory = Path.Combine(OutputRoot, "fit");
                Directory.CreateDirectory(directory);
                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Path.Combine(directory, "fist_fit_report.json"), json);
                return json;
            }

            // Fist from the carry grasp: the carry pose (grip A) is already a fist fitted around this brush. Drawing uses the
            // same fingers and handle frame; the rig turns the fist with the wrist (FistHand) and scales the brush uniformly.
            // fist[:size[:holdFromFerrule]] — the drawing hold slides up the handle to holdFromFerrule (runtime metres below
            // the ferrule) so the fist sits near the bristles; reports where the tip leaves the fist in the hand frame.
            static string FistFromCarry(string[] args)
            {
                NeedEdit();
                float size = args.Length > 0 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? Mathf.Clamp(v, .5f, 3f) : 1.3f;
                var profile = LoadProfile(ProfileAsset);
                Need(profile != null, "Run make-profile first (" + ProfileAsset + ").");
                var rig = SceneRig();
                Transform brushRoot = QaGet<Transform>(rig, "_worldBrushRoot");
                Transform grip = QaGet<Transform>(rig, "_worldGripSocket"); if (grip == null && brushRoot != null) grip = Find(brushRoot, "GripSocket");
                Transform tip = QaGet<Transform>(rig, "_worldTipSocket"); if (tip == null && brushRoot != null) tip = Find(brushRoot, "TipSocket");
                Need(grip != null && tip != null, "World brush GripSocket/TipSocket missing.");
                Vector3 rest = (Quaternion.Inverse(grip.rotation) * (tip.position - grip.position)).normalized;
                float holdFromFerrule = args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float hf) ? hf : .2f;
                Transform shaftEnd = brushRoot != null ? (Find(brushRoot, "ShaftEndSocket") ?? Find(brushRoot, "Bristle_01")) : null;
                float ferrule = shaftEnd != null ? Vector3.Dot(Quaternion.Inverse(grip.rotation) * (shaftEnd.position - grip.position), rest) * size : 0f;
                float slide = Mathf.Max(0f, ferrule - holdFromFerrule);   // runtime metres from the carry hold toward the tip
                float unit = 1f;
                { var animator = QaGet<Animator>(rig, "_animator"); Transform hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null; if (hand != null) unit = Mathf.Max(1e-6f, Mathf.Abs(hand.lossyScale.x)); }
                int fingers = 0;
                foreach (var pose in profile.Fingers ?? Array.Empty<WorldMacroPlayerGestureProfile.FingerPose>())
                    if (pose != null && pose.BoneName != null && pose.BoneName.StartsWith("RightHand", StringComparison.Ordinal)) { pose.DrawingOffset = pose.CarryOffset; fingers++; }
                profile.ShuanggouGripRotation = profile.RightHandGripRotation;
                Vector3 shaftLocal = (WorldMacroPlayerGestureProfile.SafeRotation(profile.RightHandGripRotation) * rest).normalized;
                profile.ShuanggouGripPosition = profile.RightHandGripPosition - shaftLocal * (slide / unit);
                profile.ShuanggouGrip = true;
                profile.ShuanggouFist = true;
                profile.BrushSize = size;
                profile.BrushScale = 1f;
                profile.NearMinimumDepth = Mathf.Min(profile.NearMinimumDepth, .5f);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                Vector3 shaft = (WorldMacroPlayerGestureProfile.SafeRotation(profile.RightHandGripRotation) * rest).normalized;
                Vector3 forward = profile.HandForwardLocal.normalized, dorsal = Vector3.ProjectOnPlane(profile.HandDorsalLocal, forward).normalized;
                Vector3 radial = Vector3.Cross(forward, dorsal).normalized;
                if (Vector3.Dot(radial, profile.HandThumbSideLocal) < 0f) radial = -radial;
                return "FIST_FROM_CARRY_IMPLEMENTED fingers=" + fingers + " size=" + size.ToString("0.##", CultureInfo.InvariantCulture)
                    + " tip-in-hand: forward=" + Vector3.Dot(shaft, forward).ToString("F2", CultureInfo.InvariantCulture)
                    + " dorsal=" + Vector3.Dot(shaft, dorsal).ToString("F2", CultureInfo.InvariantCulture)
                    + " radial=" + Vector3.Dot(shaft, radial).ToString("F2", CultureInfo.InvariantCulture)
                    + " slide=" + slide.ToString("F3", CultureInfo.InvariantCulture) + " ferrule=" + ferrule.ToString("F3", CultureInfo.InvariantCulture)
                    + " gripPosition=" + profile.ShuanggouGripPosition.ToString("F3");
            }

            static string MeasureHandle(Transform brushRoot, Transform grip, Transform shaftEnd, Quaternion toGrip, Vector3 axis,
                List<Vector2> samples, List<string> notes)
            {
                string method = "none";
                foreach (var filter in brushRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null) continue;
                    if (shaftEnd != null && (filter.transform == shaftEnd || filter.transform.IsChildOf(shaftEnd))) continue;
                    Vector3[] vertices = null;
                    try { vertices = filter.sharedMesh.vertices; } catch (Exception e) { notes.Add("Vertices of " + filter.name + " unreadable: " + e.Message); }
                    if (vertices != null && vertices.Length > 0)
                    {
                        method = "vertices:" + filter.name;
                        foreach (var v in vertices) samples.Add(AlongRadial(toGrip * (filter.transform.TransformPoint(v) - grip.position), axis));
                        continue;
                    }
                    // Fallback: local bounds corners (radius over-estimated at the thickest section).
                    Bounds b = filter.sharedMesh.bounds;
                    method = "bounds:" + filter.name;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector2 s = AlongRadial(toGrip * (filter.transform.TransformPoint(corner) - grip.position), axis);
                        samples.Add(new Vector2(s.x, s.y * .70f));
                    }
                    notes.Add("Handle radius from mesh bounds (vertices unavailable).");
                }
                if (samples.Count == 0) notes.Add("No handle mesh under the brush root; default 9 mm radius.");
                return method;
            }

            static Vector2 AlongRadial(Vector3 local, Vector3 axis)
            {
                float along = Vector3.Dot(local, axis);
                return new Vector2(along, (local - axis * along).magnitude);
            }

            static float ShaftRadius(List<Vector2> samples, float hold, List<string> notes)
            {
                foreach (float window in new[] { .02f, .05f, .12f })
                {
                    var radial = samples.Where(s => Mathf.Abs(s.x - hold) <= window).Select(s => s.y).OrderBy(x => x).ToList();
                    if (radial.Count < 8) continue;
                    if (window > .02f) notes.Add("Shaft radius window widened to ±" + window.ToString("0.00", CultureInfo.InvariantCulture) + " m.");
                    return Mathf.Clamp(radial[Mathf.Min(radial.Count - 1, Mathf.FloorToInt(radial.Count * .85f))], .004f, .03f);
                }
                return .009f;
            }
        }
    }

    /// <summary>Menu/queue entry for the #297 vertical double-hook grip commands.</summary>
    public static class WorldMacroPlayerShuanggou297
    {
        public static string Run(string command) => WorldMacroPlayerReRigAuthoring.Shuanggou297(command);

        [MenuItem("Oheangbu/Player/Shuanggou 297/Make Profile + Fit")] static void MenuMake() => Log("make-profile");
        [MenuItem("Oheangbu/Player/Shuanggou 297/Refit")] static void MenuFit() => Log("fit");
        [MenuItem("Oheangbu/Player/Shuanggou 297/Assign To Scene Rig")] static void MenuAssign() => Log("assign");
        [MenuItem("Oheangbu/Player/Shuanggou 297/Revert Scene Rig")] static void MenuRevert() => Log("revert");
        [MenuItem("Oheangbu/Player/Shuanggou 297/Status")] static void MenuStatus() => Log("status");

        static void Log(string command)
        {
            try { Debug.Log("[Shuanggou297] " + Run(command)); }
            catch (Exception e) { Debug.LogError("[Shuanggou297] " + command + ": " + e.Message); }
        }
    }
}
