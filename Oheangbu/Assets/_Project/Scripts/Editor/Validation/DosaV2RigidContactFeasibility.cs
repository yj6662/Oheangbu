using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Oheangbu.Data;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.Validation
{
    /// <summary>Explicit call from a disposable direct-pose fixture AFTER Evaluate.
    /// No scene/device/clock changes, no production motions, no automatic Update.</summary>
    public static class DosaV2RigidContactFeasibility
    {
        public static string Run(PlayerSecondaryMotionRig secondary, string[] partNames = null,
            float[] proposalMaximumSwingDegrees = null)
        {
            try
            {
                Require(Application.isPlaying && secondary != null, "Use the disposable Play fixture with its current settled/direct pose.");
                bool ownedDiagnostic = secondary.gameObject.scene.name.StartsWith("DosaV2SecondaryCollision_", StringComparison.Ordinal)
                    && secondary.transform.root.name == "DisposableSecondaryCollisionFixture";
                Require(secondary.gameObject.scene.name == "C2_PlayerV2Validation" || ownedDiagnostic,
                    "V2 isolated validation scene or explicitly owned disposable collision fixture required.");
                var collision = secondary.GetComponent<PlayerSecondaryCollisionRig>();
                Require(collision != null && collision.IsConfigured && collision.FrameReady && string.IsNullOrEmpty(collision.LastError),
                    "Current collision BeginFrame/Evaluate evidence must already exist.");
                var bindings = Read<RigidOrnamentBinding[]>(secondary, "_ornaments");
                var profile = Read<PlayerSecondaryMotionProfileSO>(secondary, "_profile");
                var nodes = ((IEnumerable)Read<object>(secondary, "_nodes")).Cast<object>().ToArray();
                string profileBefore = JsonUtility.ToJson(profile);
                string bindingsBefore = string.Join("|", bindings.Select(b => JsonUtility.ToJson(b.Settings)));
                var selected = partNames == null ? collision.Measurements.Where(m => m.rigid && m.remainingPenetrationMeters > 0f).Select(m => m.name).ToArray() : partNames;
                Require(selected.Distinct(StringComparer.Ordinal).Count() == selected.Length, "Duplicate part requests are ambiguous.");
                if (selected.Length == 0) return new JObject { ["status"] = "NO_UNRESOLVED_RIGID_PARTS_AT_CURRENT_POSE",
                    ["scope"] = "No finite search executed. This is not a collision/RIG_PASS approval." }.ToString();
                var requests = new List<PlayerSecondaryCollisionRig.RigidPoseSearchRequest>();
                var initialNodeStates = nodes.ToDictionary(n => n,
                    n => (angle: Read<Vector3>(n, "Angle"), velocity: Read<Vector3>(n, "Velocity")));
                foreach (var name in selected)
                {
                    var matching = bindings.Where(b => b.Name == name).ToArray();
                    Require(matching.Length == 1, "No unique serialized original rigid binding: " + name);
                    var binding = matching[0]; var matchedNodes = nodes.Where(n => Read<Transform>(n, "Bone") == binding.Pivot).ToArray();
                    Require(matchedNodes.Length == 1, "No unique initialized spring node: " + name);
                    var node = matchedNodes[0]; var nodeSettings = Read<SecondarySpringSettings>(node, "Settings");
                    var authoredSettings = binding.OverrideSettings ? binding.Settings : profile.Ornament;
                    string settingsJson = JsonUtility.ToJson(nodeSettings);
                    Require(settingsJson == JsonUtility.ToJson(authoredSettings), "Node settings differ from the original active binding/profile.");
                    var angle = Read<Vector3>(node, "Angle");
                    requests.Add(new PlayerSecondaryCollisionRig.RigidPoseSearchRequest { Pivot = binding.Pivot,
                        RestLocalRotation = Read<Quaternion>(node, "RestRotation"), CurrentAngleRadians = angle,
                        MaximumSwingDegrees = Mathf.Clamp(nodeSettings.MaximumSwingDegrees, 0f, 80f),
                        SettingsProvenance = "Initialized spring Node.Settings matches " + (binding.OverrideSettings ? "RigidOrnamentBinding override " : "profile.Ornament ") + name +
                            "; profile=" + AssetDatabase.GetAssetPath(profile) + "; settings=" + settingsJson,
                        SettingsSha256 = Hash(settingsJson), ProposalMaximumSwingDegrees = proposalMaximumSwingDegrees ?? Array.Empty<float>() });
                }
                var result = collision.AuditRigidPoseFeasibility(requests.ToArray());
                bool stateUnchanged = initialNodeStates.All(pair => Read<Vector3>(pair.Key, "Angle").Equals(pair.Value.angle) && Read<Vector3>(pair.Key, "Velocity").Equals(pair.Value.velocity));
                var json = JObject.Parse(JsonUtility.ToJson(result)); json["actualSpringAngleVelocityUnchanged"] = stateUnchanged;
                bool profileChanged = profileBefore != JsonUtility.ToJson(profile) || bindingsBefore != string.Join("|", bindings.Select(b => JsonUtility.ToJson(b.Settings)));
                json["productionMotionUsed"] = false; json["originalProfileChanged"] = profileChanged;
                json["verifiedSpringNodes"] = initialNodeStates.Count;
                if (!stateUnchanged || profileChanged) json["status"] = "DIAGNOSTIC_STATE_RESTORE_FAILURE_NO_FEASIBILITY_CLAIM";
                return json.ToString();
            }
            catch (Exception e) { return new JObject { ["status"] = "WAIT", ["error"] = e.GetBaseException().Message,
                ["scope"] = "No sampled feasibility or RIG_PASS claim from an unconfigured fixture." }.ToString(); }
        }
        private static T Read<T>(object owner, string field)
        {
            Require(owner != null, "Missing source for " + field);
            var value = owner.GetType().GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Require(value != null, "Original spring field unavailable: " + field); return (T)value.GetValue(owner);
        }
        private static string Hash(string value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
