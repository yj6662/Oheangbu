using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    /// <summary>
    /// Reads measured V2 evidence; never imports, retargets, animates or promotes an asset.
    /// RIG_PASS applies only to the file snapshot inspected now, not to a stored success flag.
    /// See Art/PlayerV2/Validation/SCHEMA.md for the deliberately fail-closed evidence contract.
    /// </summary>
    public static class DosaV2RigGate
    {
        public const string Schema = "dosa-v2-rig-gate/1";
        private const string DirectoryPath = "Art/PlayerV2/Validation/";
        private const double GripGapLimit = .0015;
        private const double GripPenetrationLimit = .0005;
        private static readonly string[] Environments = { "Blender", "Unity" };
        private static readonly string[] Variants = { "world", "near" };
        private static readonly string[] Tests = { "structuralHumanoid", "weights", "independentParts", "deformationStatic", "actualGrip", "clothStability" };
        private static readonly string[] Roles = { "blenderRig", "unityWorld", "unityNear", "brush", "unityWorldImport", "unityNearImport", "brushImport", "rigConfiguration", "staticPoseDefinitions", "validatorBlender", "validatorUnity" };
        private static readonly string[] WorldPoses = { "rest", "open_hand", "grip_down", "grip_up", "shoulder_45", "shoulder_90", "shoulder_120", "elbow_45", "elbow_90", "elbow_120", "forearm_minus90", "forearm_plus90", "wrist_flex", "wrist_extend", "torso_twist", "hip_flex", "knee_flex", "ankle_flex", "combined_reach" };
        private static readonly string[] Parts = { "left_fingers", "right_fingers", "left_sleeve", "right_sleeve", "waist_equipment", "robe", "brush" };
        private static readonly string[] Fingers = { "thumb", "index", "middle", "ring", "pinky" };
        private static readonly string[] ClothCases = { "rest_settle", "raised_arms_settle", "grip_settle", "reset" };
        private static readonly string[] RestViews = { "front", "back", "left", "right", "top", "bottom" };
        private static readonly string[] Closeups = { "hands", "armpits", "elbows", "inner_sleeves", "waist", "knees" };

        public static string Inspect() => InspectRepository(Path.GetFullPath(Path.Combine(Application.dataPath, "../..")));

        // Explicit root supports offline contract tests without opening Unity or changing scenes.
        public static string InspectRepository(string repositoryRoot)
        {
            var audit = new Audit(repositoryRoot);
            try { audit.Run(); }
            catch (Exception e) { audit.Fail("inspection", e.GetType().Name + ": " + e.Message); }
            return audit.Result().ToString(Formatting.Indented);
        }

        public static string WriteReport()
        {
            string result = Inspect();
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", DirectoryPath));
            System.IO.Directory.CreateDirectory(folder);
            // A diagnostic report is not an authorization token. Re-inspect before consuming it.
            File.WriteAllText(Path.Combine(folder, "rig-gate-inspection.json"), result, new UTF8Encoding(false));
            return result;
        }

        private static IEnumerable<string> Poses(string variant) => variant == "world"
            ? WorldPoses : WorldPoses.Where(p => p != "hip_flex" && p != "knee_flex" && p != "ankle_flex");

        private sealed class Audit
        {
            private readonly string _root;
            private readonly JArray _waiting = new JArray(), _failures = new JArray();
            private readonly Dictionary<string, string> _verified = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly HashSet<string> _messages = new HashSet<string>(StringComparer.Ordinal);
            private readonly Dictionary<string, JObject> _renders = new Dictionary<string, JObject>(StringComparer.Ordinal);
            private readonly Dictionary<string, HashSet<string>> _reviewedRenders = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            private JObject _assets, _roles, _limits;
            private string _snapshot;
            private DateTimeOffset? _configurationAt;
            private int _numericBlocks, _manualReviews;

            public Audit(string root) { _root = Path.GetFullPath(root); }
            public void Fail(string location, string message) => Issue(_failures, location, message);
            private void Wait(string location, string message) => Issue(_waiting, location, message);
            private void Issue(JArray target, string location, string message)
            {
                if (_messages.Add((target == _failures ? "FAIL:" : "WAIT:") + location + message))
                    target.Add(new JObject { ["location"] = location, ["reason"] = message });
            }

            public JObject Result() => new JObject
            {
                ["schema"] = Schema,
                ["status"] = _failures.Count > 0 ? "FAIL" : _waiting.Count > 0 ? "WAIT" : "RIG_PASS",
                ["inspectedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["snapshotSha256"] = _snapshot,
                ["verifiedFiles"] = _verified.Count,
                ["numericBlocks"] = _numericBlocks,
                ["manualReviews"] = _manualReviews,
                ["waiting"] = _waiting,
                ["failures"] = _failures,
                ["scope"] = "Static rig and direct-pose cloth fixture only. No production animation, retargeting, Meshy motion, scene or prefab promotion was performed.",
                ["limitation"] = "File hashes bind submitted measurements and reviewer attestations. This reader does not independently repeat measurements or determine image quality. Re-run after any asset/configuration change; a saved status is not a permanent gate."
            };

            public void Run()
            {
                JObject numeric = Read(DirectoryPath + "rig-numeric.json");
                JObject visual = Read(DirectoryPath + "rig-visual.json");
                if (numeric == null && visual == null) return;
                JObject source = numeric ?? visual;
                ReadManifest(source);
                if (numeric != null) CheckEnvelope(numeric, "DOSAV2_RIG_NUMERIC", "numeric");
                if (visual != null) CheckEnvelope(visual, "DOSAV2_RIG_VISUAL", "visual");
                LoadConfiguration();
                if (numeric != null) CheckNumeric(numeric);
                if (visual != null) CheckVisual(visual);
                // Detect writes during this inspection too; subsequent writes require a new Inspect.
                foreach (var file in _verified.ToArray()) VerifyFile(file.Key, file.Value, "finalSnapshot");
            }

            private JObject Read(string relative)
            {
                string path = Resolve(relative);
                if (path == null) return null;
                if (!File.Exists(path)) { Wait(relative, "Evidence file is missing."); return null; }
                try
                {
                    using (var reader = new JsonTextReader(File.OpenText(path)))
                    {
                        reader.DateParseHandling = DateParseHandling.None;
                        JObject value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                        if (reader.Read()) { Fail(relative, "Trailing JSON content."); return null; }
                        VerifyFile(relative, Hash(path), relative);
                        return value;
                    }
                }
                catch (Exception e) { Fail(relative, "Invalid/unreadable JSON: " + e.Message); return null; }
            }

            private string Resolve(string relative)
            {
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':')
                    || relative.Split('/').Any(p => p == ".." || p == "." || p.Length == 0))
                { Fail(relative ?? "path", "Expected a canonical repository-relative path using '/'."); return null; }
                string full = Path.GetFullPath(Path.Combine(_root, relative));
                if (!full.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                { Fail(relative, "Path escapes repository."); return null; }
                for (string part = full; part != null && !string.Equals(part, _root, StringComparison.OrdinalIgnoreCase); part = Path.GetDirectoryName(part))
                    if ((File.Exists(part) || System.IO.Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    { Fail(relative, "Evidence through a symlink/junction is not accepted."); return null; }
                return full;
            }

            private bool VerifyFile(string relative, string sha, string location)
            {
                if (!IsSha(sha)) { Fail(location, "SHA256 must be 64 lowercase hex characters."); return false; }
                string path = Resolve(relative);
                if (path == null) return false;
                if (!File.Exists(path)) { Wait(location, "Missing file: " + relative); return false; }
                if (new FileInfo(path).Length == 0) { Fail(location, "Empty evidence: " + relative); return false; }
                if (!string.Equals(Hash(path), sha, StringComparison.Ordinal))
                { Wait(location, "File changed; evidence must be regenerated/reviewed: " + relative); return false; }
                if (_verified.TryGetValue(relative, out string previous) && previous != sha)
                { Wait(location, "Conflicting snapshots for " + relative); return false; }
                _verified[relative] = sha;
                return true;
            }

            private static bool IsSha(string value) => value != null && value.Length == 64 && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
            private static string Hash(string path)
            {
                using (var stream = File.OpenRead(path))
                using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream));
            }
            private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

            private void ReadManifest(JObject doc)
            {
                JObject manifest = Object(doc, "manifest", "manifest");
                _assets = Object(manifest, "assets", "manifest.assets");
                _roles = Object(manifest, "roles", "manifest.roles");
                if (_assets == null || _roles == null) return;
                if (!_assets.Properties().Any()) Wait("manifest.assets", "Asset hash map is empty.");
                var portablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in _assets.Properties())
                {
                    if (!portablePaths.Add(item.Name)) Fail("manifest.assets", "Duplicate case-insensitive path: " + item.Name);
                    VerifyFile(item.Name, Text(_assets, item.Name, "manifest.assets." + item.Name), "manifest.assets." + item.Name);
                }
                foreach (string role in Roles)
                {
                    string path = Text(_roles, role, "manifest.roles." + role);
                    if (path != null && _assets[path] == null) Wait("manifest.roles." + role, "Role file must be in the asset hash map.");
                }
                var modelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string role in new[] { "blenderRig", "unityWorld", "unityNear", "brush" })
                {
                    string path = (string)_roles[role];
                    if (path != null && !modelPaths.Add(path)) Fail("manifest.roles", "Model roles must identify distinct authored files.");
                }
                CheckRoleExtension("blenderRig", ".blend");
                foreach (string role in new[] { "unityWorld", "unityNear", "brush" }) CheckRoleExtension(role, ".fbx");
                CheckRoleExtension("rigConfiguration", ".json");
                CheckRoleExtension("staticPoseDefinitions", ".json");
                var canonical = new StringBuilder();
                foreach (var item in _assets.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)) canonical.Append("asset\t").Append(item.Name).Append('\t').Append((string)item.Value).Append('\n');
                foreach (var item in _roles.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)) canonical.Append("role\t").Append(item.Name).Append('\t').Append((string)item.Value).Append('\n');
                using (var sha = SHA256.Create()) _snapshot = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())));
            }

            private void CheckRoleExtension(string role, string extension)
            {
                string path = (string)_roles?[role];
                if (path != null && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) Fail("manifest.roles." + role, "Expected " + extension + " asset.");
            }

            private void CheckEnvelope(JObject doc, string kind, string location)
            {
                Equal(doc, "schema", Schema, location); Equal(doc, "kind", kind, location);
                JObject manifest = Object(doc, "manifest", location + ".manifest");
                JObject assets = Object(manifest, "assets", location + ".manifest.assets"), roles = Object(manifest, "roles", location + ".manifest.roles");
                if (!SameMap(_assets, assets) || !SameMap(_roles, roles)) Wait(location, "Numeric and visual manifests must have identical complete asset/role maps.");
                string snapshot = Text(doc, "snapshotSha256", location);
                if (snapshot != null && snapshot != _snapshot) Wait(location, "Snapshot SHA256 differs from manifest; old evidence cannot pass.");
                Timestamp(doc, "recordedAtUtc", location);
            }

            private static bool SameMap(JObject a, JObject b) => a != null && b != null && a.Count == b.Count && a.Properties().All(p => JToken.DeepEquals(p.Value, b[p.Name]));

            private void LoadConfiguration()
            {
                string path = (string)_roles?["rigConfiguration"];
                if (path == null) return;
                JObject configuration = Read(path);
                _limits = Object(configuration, "acceptanceLimits", "configuration.acceptanceLimits");
                _configurationAt = Timestamp(configuration, "fixedAtUtc", "configuration");
                Text(configuration, "rationale", "configuration");
                JArray dependencies = Array(configuration, "renderDependencyPaths", "configuration");
                if (dependencies != null && dependencies.Count == 0) Wait("configuration.renderDependencyPaths", "Record material/texture dependencies used by the reviewed renders.");
                if (dependencies != null) foreach (JToken dependency in dependencies)
                {
                    string assetPath = dependency.Type == JTokenType.String ? (string)dependency : null;
                    if (assetPath == null || _assets?[assetPath] == null) Wait("configuration.renderDependencyPaths", "Every render dependency must be in the asset hash map.");
                }
                foreach (string key in new[] { "maxWeightSumError", "maxBoneLengthDeltaMeters", "maxRootDeltaMeters", "maxPinnedDriftMeters", "maxClothStretchRatio", "maxSettledSpeedMetersPerSecond", "minSimulationSeconds", "minSettledSamples" })
                {
                    double? value = Number(_limits, key, "configuration.acceptanceLimits");
                    if (value.HasValue && value <= 0) Fail("configuration.acceptanceLimits." + key, "Limit must be positive and fixed before measurements.");
                }
                double? stretch = Number(_limits, "maxClothStretchRatio", "configuration.acceptanceLimits");
                if (stretch.HasValue && stretch < 1) Fail("configuration.acceptanceLimits", "Stretch ratio limit must be >= 1.");
                string posePath = (string)_roles?["staticPoseDefinitions"];
                JObject poses = posePath == null ? null : Read(posePath);
                Equal(poses, "mode", "DIRECT_BONE_POSES", "poseDefinitions");
                JArray definitions = Array(poses, "poses", "poseDefinitions");
                foreach (string id in WorldPoses)
                {
                    JObject pose = Unique(definitions, "id", id, "poseDefinitions." + id);
                    JArray rotations = Array(pose, "boneRotations", "poseDefinitions." + id);
                    if (rotations != null && rotations.Count == 0) Wait("poseDefinitions." + id, "Record explicit bone transforms, including rest.");
                    if (rotations != null) foreach (JToken item in rotations)
                    {
                        JObject rotation = item as JObject;
                        Text(rotation, "bone", "poseDefinitions." + id);
                        Number(rotation, "x", "poseDefinitions." + id); Number(rotation, "y", "poseDefinitions." + id); Number(rotation, "z", "poseDefinitions." + id);
                    }
                }
            }

            private void CheckNumeric(JObject doc)
            {
                DateTimeOffset? measuredAt = Timestamp(doc, "recordedAtUtc", "numeric");
                if (_configurationAt.HasValue && measuredAt.HasValue && _configurationAt > measuredAt) Fail("numeric", "Acceptance configuration was fixed after measurement.");
                JObject environments = Object(doc, "environments", "numeric.environments");
                foreach (string environment in Environments)
                {
                    JObject engine = Object(environments, environment, "numeric." + environment);
                    Text(engine, "toolVersion", "numeric." + environment);
                    JObject tests = Object(engine, "tests", "numeric." + environment + ".tests");
                    foreach (string name in Tests)
                    {
                        string location = "numeric." + environment + "." + name;
                        JObject test = Object(tests, name, location);
                        if (test == null) continue;
                        _numericBlocks++;
                        Equal(test, "validator", "dosa-v2-" + name + "/1", location);
                        Equal(test, "mode", name == "clothStability" ? "DIRECT_POSE_PHYSICS" : "DIRECT_BONE_POSES", location);
                        JArray samples = Array(test, "samples", location);
                        switch (name)
                        {
                            case "structuralHumanoid": CheckStructural(samples, environment, location); break;
                            case "weights": CheckWeights(samples, location); break;
                            case "independentParts": CheckParts(samples, location); break;
                            case "deformationStatic": CheckDeformation(samples, location); break;
                            case "actualGrip": CheckGrip(samples, location); break;
                            case "clothStability": CheckCloth(samples, location); break;
                        }
                    }
                    CheckCoverage(tests, "numeric." + environment);
                }
            }

            private void CheckCoverage(JObject tests, string location)
            {
                var structure = tests?["structuralHumanoid"]?["samples"] as JArray;
                var weights = tests?["weights"]?["samples"] as JArray;
                var deformation = tests?["deformationStatic"]?["samples"] as JArray;
                if (structure == null) return;
                foreach (string variant in Variants)
                {
                    JObject model = structure.OfType<JObject>().FirstOrDefault(s => (string)s["variant"] == variant);
                    double? vertices = Number(model, "vertexCount", location + ".coverage." + variant);
                    double? bones = Number(model, "boneCount", location + ".coverage." + variant);
                    foreach (var rows in new[] { weights, deformation })
                    {
                        if (rows == null) continue;
                        foreach (JObject sample in rows.OfType<JObject>().Where(s => (string)s["variant"] == variant))
                        {
                            string label = location + ".coverage." + variant + "." + ((string)sample["poseId"] ?? "weights");
                            double? checkedVertices = Number(sample, "checkedVertices", label);
                            if (vertices.HasValue && checkedVertices.HasValue && vertices != checkedVertices)
                                Fail(label, "Weight/deformation coverage must include every vertex in the structural inventory.");
                            if (rows == deformation)
                            {
                                double? checkedBones = Number(sample, "checkedBones", label);
                                if (bones.HasValue && checkedBones.HasValue && bones != checkedBones)
                                    Fail(label, "Static deformation coverage must include every bone in the structural inventory.");
                            }
                        }
                    }
                }
            }

            private void CheckStructural(JArray samples, string environment, string location)
            {
                foreach (string variant in Variants)
                {
                    JObject s = Unique(samples, "variant", variant, location + "." + variant);
                    Positive(s, "vertexCount", location); Positive(s, "triangleCount", location); Positive(s, "boneCount", location); Positive(s, "mappedHumanoidBones", location);
                    Positive(s, "heightMeters", location); Positive(s, "metersPerUnit", location);
                    Text(s, "axisConvention", location); Text(s, "pivotDescription", location); Text(s, "auxiliaryBoneMap", location);
                    foreach (string field in new[] { "nonFiniteValues", "invalidIndices", "missingRequiredBones", "missingReferences", "invalidParentLinks", "leftRightMappingErrors", "unresolvedDegenerateFaces" }) Zero(s, field, location);
                    if (environment == "Unity") { True(s, "avatarValid", location); True(s, "avatarHuman", location); }
                    else True(s, "humanoidMappingComplete", location);
                }
            }

            private void CheckWeights(JArray samples, string location)
            {
                foreach (string variant in Variants)
                {
                    JObject s = Unique(samples, "variant", variant, location + "." + variant);
                    Positive(s, "checkedVertices", location); Positive(s, "activeInfluenceLimit", location);
                    foreach (string key in new[] { "unweightedVertices", "invalidBoneIndices", "negativeWeights", "nonFiniteWeights", "unresolvedTruncatedVertices" }) Zero(s, key, location);
                    Limited(s, "maxWeightSumError", "maxWeightSumError", location);
                }
            }

            private void CheckParts(JArray samples, string location)
            {
                foreach (string part in Parts)
                {
                    JObject s = Unique(samples, "part", part, location + "." + part);
                    Positive(s, "checkedVertices", location); Positive(s, "checkedTriangles", location);
                    Zero(s, "unintendedBridgeFaces", location); Zero(s, "unresolvedBoundaryDefects", location);
                    Text(s, "method", location); Text(s, "separationEvidence", location);
                }
            }

            private void CheckDeformation(JArray samples, string location)
            {
                foreach (string variant in Variants)
                foreach (string pose in Poses(variant))
                {
                    JObject s = Unique(samples, "variant", variant, location + "." + variant + "." + pose, "poseId", pose);
                    Positive(s, "checkedVertices", location); Positive(s, "checkedBones", location);
                    foreach (string key in new[] { "nonFiniteValues", "invertedJointFaces", "unresolvedSelfIntersections" }) Zero(s, key, location);
                    Limited(s, "maxBoneLengthDeltaMeters", "maxBoneLengthDeltaMeters", location);
                    Limited(s, "rootDeltaMeters", "maxRootDeltaMeters", location);
                }
            }

            private void CheckGrip(JArray samples, string location)
            {
                foreach (string variant in Variants)
                foreach (string pose in new[] { "grip_down", "grip_up" })
                {
                    JObject s = Unique(samples, "variant", variant, location + "." + variant + "." + pose, "poseId", pose);
                    Equal(s, "method", "SKIN_TRIANGLES_VS_FINITE_SHAFT", location);
                    Positive(s, "checkedSkinTriangles", location); Positive(s, "checkedShaftTriangles", location);
                    NonNegative(s, "penetratingTriangles", location);
                    AtMost(s, "maxPenetrationMeters", GripPenetrationLimit, location);
                    JArray fingers = Array(s, "fingers", location);
                    foreach (string finger in Fingers)
                    {
                        JObject f = Unique(fingers, "finger", finger, location + "." + finger);
                        Positive(f, "checkedSkinTriangles", location);
                        AtMost(f, "maxPenetrationMeters", GripPenetrationLimit, location);
                        if (finger == "thumb" || finger == "index" || finger == "middle") AtMost(f, "contactGapMeters", GripGapLimit, location);
                        else NonNegative(f, "contactGapMeters", location); // Pen-up lift is measured, not forced into contact.
                    }
                }
            }

            private void CheckCloth(JArray samples, string location)
            {
                foreach (string id in ClothCases)
                {
                    JObject s = Unique(samples, "caseId", id, location + "." + id);
                    Text(s, "solver", location); Text(s, "auxiliaryBoneRoles", location);
                    Positive(s, "simulatedFrames", location); Positive(s, "pinnedVertices", location); Positive(s, "freeVertices", location); Positive(s, "bodyColliders", location); Positive(s, "brushColliders", location);
                    AtLeastLimit(s, "simulationSeconds", "minSimulationSeconds", location); AtLeastLimit(s, "settledSamples", "minSettledSamples", location);
                    foreach (string key in new[] { "nonFiniteValues", "unresolvedBodyPenetrations", "unresolvedBrushPenetrations", "unintendedDoubleDrivenVertices" }) Zero(s, key, location);
                    Limited(s, "maxPinnedDriftMeters", "maxPinnedDriftMeters", location); Limited(s, "maxStretchRatio", "maxClothStretchRatio", location); Limited(s, "maxSettledSpeedMetersPerSecond", "maxSettledSpeedMetersPerSecond", location);
                    if (id == "reset") True(s, "resetRestored", location);
                }
            }

            private void CheckVisual(JObject doc)
            {
                Text(doc, "reviewer", "visual"); Timestamp(doc, "reviewedAtUtc", "visual");
                JArray renders = Array(doc, "renders", "visual");
                if (renders != null) foreach (JToken item in renders)
                {
                    JObject render = item as JObject;
                    string id = Text(render, "id", "visual.renders");
                    if (id == null) continue;
                    if (_renders.ContainsKey(id)) { Fail("visual.renders", "Duplicate render id: " + id); continue; }
                    string environment = Text(render, "environment", id), variant = Text(render, "variant", id);
                    if (environment != null && !Environments.Contains(environment)) Fail(id, "Unknown rendering environment.");
                    if (variant != null && !Variants.Contains(variant)) Fail(id, "Unknown model variant.");
                    Text(render, "poseId", id); Text(render, "view", id);
                    Equal(render, "snapshotSha256", _snapshot, id);
                    string path = Text(render, "path", id), sha = Text(render, "sha256", id);
                    if (path != null && VerifyFile(path, sha, id) && IsPng(path, id)) _renders.Add(id, render);
                }
                JArray reviews = Array(doc, "reviews", "visual");
                foreach (string environment in Environments)
                foreach (string test in Tests.Concat(new[] { "acceptanceConfiguration" }))
                {
                    string location = "visual." + environment + "." + test;
                    JObject review = Unique(reviews, "environment", environment, location, "test", test);
                    if (review == null) continue;
                    _manualReviews++;
                    Text(review, "reviewer", location); Text(review, "notes", location);
                    Timestamp(review, "reviewedAtUtc", location);
                    string verdict = Text(review, "verdict", location);
                    if (verdict == "FAIL") Fail(location, "Manual review found a defect.");
                    else if (verdict != "PASS") Wait(location, "Manual review is incomplete or uncertain.");
                    JArray refs = Array(review, "renderIds", location);
                    if (refs != null && refs.Count == 0) Wait(location, "Manual review must cite actual renders.");
                    var reviewed = new HashSet<string>(StringComparer.Ordinal);
                    _reviewedRenders[environment + "/" + test] = reviewed;
                    if (refs != null) foreach (JToken reference in refs)
                    {
                        string id = reference.Type == JTokenType.String ? (string)reference : null;
                        if (id == null || !_renders.TryGetValue(id, out JObject render)) Wait(location, "Review references a missing/unverified render: " + id);
                        else if ((string)render["environment"] != environment) Fail(location, "Review cites the wrong rendering environment.");
                        else if (verdict == "PASS") reviewed.Add(id);
                    }
                }
                foreach (string environment in Environments)
                {
                    foreach (string variant in Variants)
                    {
                        foreach (string pose in Poses(variant)) RequireRender(environment, "deformationStatic", variant, pose, null, null);
                        foreach (string pose in new[] { "grip_down", "grip_up" }) RequireRender(environment, "actualGrip", variant, pose, null, null);
                    }
                    foreach (string view in RestViews) RequireRender(environment, "structuralHumanoid", "world", "rest", view, null);
                    foreach (string region in Closeups) RequireRender(environment, "independentParts", "world", null, null, region);
                    foreach (string id in ClothCases) RequireRender(environment, "clothStability", "world", id, null, null);
                }
            }

            private bool IsPng(string relative, string location)
            {
                string path = Resolve(relative);
                if (path == null) return false;
                byte[] header = new byte[24];
                using (var stream = File.OpenRead(path))
                    if (stream.Read(header, 0, header.Length) != header.Length) { Fail(location, "PNG is truncated."); return false; }
                byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                if (!signature.SequenceEqual(header.Take(8)) || Encoding.ASCII.GetString(header, 12, 4) != "IHDR" || header.Skip(16).Take(4).All(b => b == 0) || header.Skip(20).Take(4).All(b => b == 0))
                { Fail(location, "Expected an actual PNG image with nonzero dimensions."); return false; }
                return true;
            }

            private void RequireRender(string environment, string test, string variant, string pose, string view, string region)
            {
                _reviewedRenders.TryGetValue(environment + "/" + test, out HashSet<string> reviewed);
                if (reviewed == null || !_renders.Values.Any(r => reviewed.Contains((string)r["id"]) && (string)r["environment"] == environment && (string)r["variant"] == variant
                    && (pose == null || (string)r["poseId"] == pose) && (view == null || (string)r["view"] == view) && (region == null || (string)r["region"] == region)))
                    Wait("visual.coverage", "Missing verified, manually reviewed render: " + environment + "/" + test + "/" + variant + "/" + pose + "/" + view + "/" + region);
            }

            private JObject Object(JObject parent, string key, string location)
            {
                if (parent == null || parent[key] == null) { Wait(location, "Required object is missing."); return null; }
                if (parent[key] is JObject value) return value;
                Fail(location, "Expected an object."); return null;
            }
            private JArray Array(JObject parent, string key, string location)
            {
                if (parent == null || parent[key] == null) { Wait(location + "." + key, "Required array is missing."); return null; }
                if (parent[key] is JArray value) return value;
                Fail(location + "." + key, "Expected an array."); return null;
            }
            private JObject Unique(JArray rows, string key, string value, string location, string secondKey = null, string secondValue = null)
            {
                if (rows == null) return null;
                var found = rows.OfType<JObject>().Where(r => (string)r[key] == value && (secondKey == null || (string)r[secondKey] == secondValue)).ToArray();
                if (found.Length == 0) { Wait(location, "Required measured/reviewed sample is missing."); return null; }
                if (found.Length != 1) { Fail(location, "Duplicate sample identity."); return null; }
                return found[0];
            }
            private string Text(JObject parent, string key, string location)
            {
                JToken token = parent?[key];
                if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.String && string.IsNullOrWhiteSpace((string)token))
                { Wait(location + "." + key, "Required text is missing."); return null; }
                if (token.Type != JTokenType.String) { Fail(location + "." + key, "Expected text."); return null; }
                return (string)token;
            }
            private double? Number(JObject parent, string key, string location)
            {
                JToken token = parent?[key];
                if (token == null || token.Type == JTokenType.Null) { Wait(location + "." + key, "Required measured number is missing."); return null; }
                if ((token.Type != JTokenType.Float && token.Type != JTokenType.Integer) || !double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || double.IsInfinity(value))
                { Fail(location + "." + key, "Expected a finite JSON number."); return null; }
                return value;
            }
            private DateTimeOffset? Timestamp(JObject parent, string key, string location)
            {
                string text = Text(parent, key, location);
                if (text == null) return null;
                if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset value) || value.Offset != TimeSpan.Zero)
                { Fail(location + "." + key, "Expected an ISO-8601 UTC timestamp."); return null; }
                if (value > DateTimeOffset.UtcNow.AddMinutes(5)) Fail(location + "." + key, "Evidence timestamp is in the future.");
                return value;
            }
            private void Equal(JObject obj, string key, string expected, string location)
            { string value = Text(obj, key, location); if (value != null && value != expected) Fail(location + "." + key, "Expected " + expected + "."); }
            private void True(JObject obj, string key, string location)
            { if (obj?[key] == null) Wait(location + "." + key, "Required measurement is missing."); else if (obj[key].Type != JTokenType.Boolean || !(bool)obj[key]) Fail(location + "." + key, "Required measured condition is false or invalid."); }
            private void Zero(JObject obj, string key, string location)
            { double? n = Number(obj, key, location); if (n.HasValue && n != 0) Fail(location + "." + key, "Expected zero unresolved defects, got " + n + "."); }
            private void Positive(JObject obj, string key, string location)
            {
                double? n = Number(obj, key, location);
                if (n.HasValue && (n <= 0 || key != "heightMeters" && key != "metersPerUnit" && n != Math.Truncate(n.Value)))
                    Fail(location + "." + key, "Expected positive measured coverage (counts must be integers).");
            }
            private void NonNegative(JObject obj, string key, string location)
            { double? n = Number(obj, key, location); if (n.HasValue && n < 0) Fail(location + "." + key, "Expected a nonnegative measurement."); }
            private void AtMost(JObject obj, string key, double limit, string location)
            { double? n = Number(obj, key, location); if (n.HasValue && (n < 0 || n > limit)) Fail(location + "." + key, "Measurement " + n + " exceeds [0, " + limit.ToString("R", CultureInfo.InvariantCulture) + "]."); }
            private void Limited(JObject obj, string key, string limitKey, string location)
            { double? limit = Number(_limits, limitKey, "configuration.acceptanceLimits"); if (limit.HasValue) AtMost(obj, key, limit.Value, location); else Number(obj, key, location); }
            private void AtLeastLimit(JObject obj, string key, string limitKey, string location)
            {
                double? n = Number(obj, key, location), limit = Number(_limits, limitKey, "configuration.acceptanceLimits");
                if (n.HasValue && key == "settledSamples" && n != Math.Truncate(n.Value)) Fail(location + "." + key, "Sample counts must be integers.");
                if (n.HasValue && limit.HasValue && n < limit) Fail(location + "." + key, "Measured coverage is below the predeclared minimum.");
            }
        }
    }
}
