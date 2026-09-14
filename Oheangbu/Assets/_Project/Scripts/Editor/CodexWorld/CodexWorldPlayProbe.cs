using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Oheangbu.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Physical traversal of the comparison scene without moving or configuring the real player.</summary>
    public static class CodexWorldPlayProbe
    {
        private struct Waypoint
        {
            public Vector3 Position;
            public string Section;
            public Waypoint(Vector3 position, string section) { Position = position; Section = section; }
        }

        public static string RuntimeState()
        {
            var scene = SceneManager.GetActiveScene();
            var player = FindPlayer(scene);
            return "scene=" + scene.path + "; playing=" + Application.isPlaying + "; player="
                + (player == null ? "MISSING" : player.name + "; position=" + Position(player.transform.position)
                    + "; isGrounded=" + player.isGrounded + "; controllerEnabled=" + player.enabled);
        }

        public static string ProbeTraversal()
        {
            var scene = SceneManager.GetActiveScene();
            if (!Application.isPlaying) return "FAIL: ProbeTraversal requires Play mode.";
            if (scene.path != CodexWorldSceneBuilder.ScenePath)
                return "FAIL: active scene is " + scene.path + "; expected " + CodexWorldSceneBuilder.ScenePath;
            var player = FindPlayer(scene);
            if (player == null || !player.enabled) return "FAIL: enabled real player CharacterController not found.";
            var settings = AssetDatabase.LoadAssetAtPath<CodexWorldSettingsSO>(CodexWorldSceneBuilder.SettingsPath);
            if (settings == null) return "FAIL: CodexWorld settings asset missing.";
            Transform inn = null;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.name == CodexThatchedInn.RootName) inn = candidate;
            if (inn == null) return "FAIL: thatched inn not found in active scene.";

            var waypoints = new List<Waypoint>();
            for (int z = 1; z <= 218; z++)
                waypoints.Add(new Waypoint(new Vector3(CodexWorldGeometry.PathX(z), 0f, z), "main path forward"));
            // Return physically to the junction; a new spawn at z29 would skip part of the continuous test.
            for (int z = 217; z >= 29; z--)
                waypoints.Add(new Waypoint(new Vector3(CodexWorldGeometry.PathX(z), 0f, z), "main path return"));
            Vector3 junction = new Vector3(CodexWorldGeometry.PathX(29f), 0f, 29f);
            Vector3 approach = inn.TransformPoint(settings.InnApproachLocal);
            approach.y = 0f;
            Vector3 porch = inn.TransformPoint(settings.InnPorchLocal);
            porch.y = 0f; // Route y is never imposed: CharacterController and scene colliders determine it.
            AppendSegment(waypoints, junction, approach, "inn approach");
            AppendSegment(waypoints, approach, porch, "front porch between posts");

            GameObject probeObject = null;
            CharacterController probe = null;
            Vector3 playerBefore = player.transform.position;
            var playerColliders = new HashSet<Collider>();
            foreach (var collider in player.transform.root.GetComponentsInChildren<Collider>(true))
                if (collider.enabled && collider.gameObject.activeInHierarchy && collider.gameObject.scene == scene)
                    playerColliders.Add(collider);
            int moves = 0, completed = 0, groundSamples = 0, groundMisses = 0, sideContacts = 0;
            float maxLateralError = 0f, maxWaypointError = 0f;
            float minGroundGap = float.PositiveInfinity, maxGroundGap = float.NegativeInfinity;
            string failure = null;
            Vector3 finalPosition = Vector3.zero;
            try
            {
                probeObject = new GameObject("~CodexWorldTraversalProbe")
                {
                    // Hidden/DontSave objects can be removed from Unity's physics scene.
                    // This Play-only object is removed in finally and is never saved.
                    hideFlags = HideFlags.None,
                    layer = player.gameObject.layer
                };
                probeObject.transform.localScale = player.transform.lossyScale;
                probe = probeObject.AddComponent<CharacterController>();
                probe.enabled = false;
                probe.height = player.height;
                probe.radius = player.radius;
                probe.center = player.center;
                probe.slopeLimit = player.slopeLimit;
                probe.stepOffset = player.stepOffset;
                probe.skinWidth = player.skinWidth;
                probe.minMoveDistance = player.minMoveDistance;
                probe.enableOverlapRecovery = player.enableOverlapRecovery;
                probe.detectCollisions = true;
                probe.sharedMaterial = player.sharedMaterial;
                float scaleY = Mathf.Abs(probeObject.transform.lossyScale.y);
                probeObject.transform.position = new Vector3(CodexWorldGeometry.PathX(0f),
                    CodexWorldGeometry.Height(CodexWorldGeometry.PathX(0f), 0f)
                    + (player.height * 0.5f - player.center.y) * scaleY + 0.1f, 0f);
                probe.enabled = true;
                foreach (var collider in playerColliders)
                    if (collider != null) Physics.IgnoreCollision(probe, collider, true);
                Physics.SyncTransforms();

                for (int i = 0; i < 24; i++)
                {
                    probe.Move(Vector3.down * 0.15f);
                    moves++;
                    if (probe.isGrounded) break;
                }
                MeasureGround(probe, scene, playerColliders, ref groundSamples, ref groundMisses, ref minGroundGap, ref maxGroundGap);

                Vector3 previousTarget = new Vector3(CodexWorldGeometry.PathX(0f), 0f, 0f);
                foreach (var waypoint in waypoints)
                {
                    const int maxMovesPerWaypoint = 24;
                    for (int step = 0; step < maxMovesPerWaypoint; step++)
                    {
                        Vector3 delta = waypoint.Position - probe.transform.position;
                        delta.y = 0f;
                        float distance = delta.magnitude;
                        if (distance <= 0.06f) break;
                        Vector3 movement = delta / distance * Mathf.Min(0.25f, distance) + Vector3.down * 0.15f;
                        CollisionFlags flags = probe.Move(movement);
                        moves++;
                        if ((flags & CollisionFlags.Sides) != 0) sideContacts++;
                        maxLateralError = Mathf.Max(maxLateralError,
                            DistanceToSegmentXZ(probe.transform.position, previousTarget, waypoint.Position));
                    }
                    float error = DistanceXZ(probe.transform.position, waypoint.Position);
                    maxWaypointError = Mathf.Max(maxWaypointError, error);
                    MeasureGround(probe, scene, playerColliders, ref groundSamples, ref groundMisses, ref minGroundGap, ref maxGroundGap);
                    if (error > 0.5f)
                    {
                        failure = "Blocked during " + waypoint.Section + "; target=" + Position(waypoint.Position)
                            + "; actual=" + Position(probe.transform.position) + "; horizontal error=" + Number(error) + "m";
                        break;
                    }
                    completed++;
                    previousTarget = waypoint.Position;
                }
                finalPosition = probe.transform.position;
                if (groundMisses > 0 && failure == null) failure = "No supporting ground found for " + groundMisses + " measurements.";
                if (Vector3.Distance(playerBefore, player.transform.position) > 0.0001f && failure == null)
                    failure = "Real player's position changed during synchronous probe.";
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
                if (probeObject != null) finalPosition = probeObject.transform.position;
            }
            finally
            {
                if (probe != null)
                {
                    foreach (var collider in playerColliders)
                        if (collider != null) Physics.IgnoreCollision(probe, collider, false);
                    probe.enabled = false;
                }
                if (probeObject != null) Object.DestroyImmediate(probeObject);
            }

            var output = new StringBuilder();
            output.AppendLine((failure == null ? "PASS" : "FAIL") + ": physical CharacterController traversal in Play mode");
            output.AppendLine("Scene=" + scene.path + "; route=path z0->218->29, inn approach, front porch");
            output.AppendLine("Completed waypoints=" + completed + "/" + waypoints.Count + "; Move calls=" + moves + "; side contacts=" + sideContacts);
            output.AppendLine("Max lateral deviation=" + Number(maxLateralError) + "m; max waypoint horizontal error=" + Number(maxWaypointError) + "m");
            output.AppendLine("Ground samples=" + groundSamples + "; misses=" + groundMisses + "; capsule-bottom gap min/max="
                + Number(minGroundGap) + "/" + Number(maxGroundGap) + "m");
            output.AppendLine("Copied controller: height=" + Number(player.height) + "; radius=" + Number(player.radius)
                + "; slope=" + Number(player.slopeLimit) + "; step=" + Number(player.stepOffset) + "; skin=" + Number(player.skinWidth));
            output.AppendLine("Probe final=" + Position(finalPosition) + "; real player=" + Position(player.transform.position)
                + "; real-player displacement=" + Number(Vector3.Distance(playerBefore, player.transform.position)) + "m; temporary probe removed");
            if (failure != null) output.AppendLine(failure);
            output.AppendLine("Collision traversal only; this does not exercise player input, combat, or frame-time performance.");
            System.IO.Directory.CreateDirectory(CodexWorldAudit.CaptureFolder);
            System.IO.File.WriteAllText(CodexWorldAudit.CaptureFolder+"/traversal-validation.txt",output.ToString());
            return output.ToString();
        }

        private static void AppendSegment(List<Waypoint> waypoints, Vector3 start, Vector3 end, string section)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(DistanceXZ(start, end)));
            for (int i = 1; i <= count; i++) waypoints.Add(new Waypoint(Vector3.Lerp(start, end, i / (float)count), section));
        }

        private static void MeasureGround(CharacterController probe, Scene scene, HashSet<Collider> ignored,
            ref int samples, ref int misses, ref float minimum, ref float maximum)
        {
            samples++;
            Physics.SyncTransforms();
            Bounds bounds = probe.bounds;
            var hits = Physics.RaycastAll(bounds.center + Vector3.up * 0.05f, Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore);
            float distance = float.PositiveInfinity;
            float groundY = 0f;
            foreach (var hit in hits)
            {
                if (hit.collider == probe || ignored.Contains(hit.collider) || hit.collider.gameObject.scene != scene || hit.normal.y < 0.25f) continue;
                if (hit.distance < distance) { distance = hit.distance; groundY = hit.point.y; }
            }
            if (float.IsPositiveInfinity(distance)) { misses++; return; }
            float gap = bounds.min.y - groundY;
            minimum = Mathf.Min(minimum, gap);
            maximum = Mathf.Max(maximum, gap);
        }

        private static CharacterController FindPlayer(Scene scene)
        {
            CharacterController fallback = null;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var controller in root.GetComponentsInChildren<CharacterController>(true))
            {
                if (controller.name.StartsWith("~Codex", StringComparison.Ordinal)) continue;
                if (controller.name == "Player") return controller;
                if (fallback == null) fallback = controller;
            }
            return fallback;
        }

        private static float DistanceToSegmentXZ(Vector3 point, Vector3 start, Vector3 end)
        {
            point.y = start.y = end.y = 0f;
            Vector3 segment = end - start;
            float t = segment.sqrMagnitude > 0.00001f ? Mathf.Clamp01(Vector3.Dot(point - start, segment) / segment.sqrMagnitude) : 0f;
            return Vector3.Distance(point, start + segment * t);
        }

        private static float DistanceXZ(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
        private static string Number(float value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
        private static string Position(Vector3 value) => "(" + Number(value.x) + "," + Number(value.y) + "," + Number(value.z) + ")";
    }
}
