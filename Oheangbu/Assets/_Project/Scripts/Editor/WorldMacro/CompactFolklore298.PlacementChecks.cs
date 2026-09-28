using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactFolklore298
    {
        // Shared authoring/diagnostic predicate. Bounds and gameplay conditions are
        // deliberately identical to the original candidate placement preflight.
        static string PlacementFailure298(ModelRow row, WorldMacroPlaytestSession session,
            Vector3[] original, IEnumerable<Vector3> reserved, int areaMask,
            float radius, float height, Vector3 probe, out Vector3 feet, out string floorName)
        {
            feet = probe; floorName = "";
            if (!NavMesh.SamplePosition(probe, out var hit, 3, areaMask)) return "no_nav_within_3m";
            if (Mathf.Abs(hit.position.y - probe.y) > 3) return "nav_vertical_over_3m";
            feet = hit.position;
            if (!string.IsNullOrWhiteSpace(row.realm) && !string.Equals(session.MountainLayout.RealmAt(new Vector2(feet.x, feet.z))?.Id, row.realm, StringComparison.OrdinalIgnoreCase)) return "wrong_realm";
            if (session.Traversal != null && session.Traversal.Immersion(feet) > .02f) return "water";
            var point = feet;
            if (original.Any(p => Vector3.Distance(p, point) < 18)) return "original_actor_within_18m";
            if (reserved.Any(p => Vector3.Distance(p, point) < 20)) return "reserved_actor_within_20m";
            if (!Physics.Raycast(feet + Vector3.up * .8f, Vector3.down, out var floor, 1.3f, ~0, QueryTriggerInteraction.Ignore)) return "no_floor";
            floorName = floor.collider.name;
            if (floor.normal.y < .85f) return "floor_normal_below_0_85";
            if (Mathf.Abs(floor.point.y - feet.y) > .35f) return "floor_nav_difference_over_0_35m";
            if (floor.collider.attachedRigidbody != null || floor.collider.GetComponentInParent<WorldTemporarySupport>() != null) return "non_permanent_floor";
            feet = floor.point;
            if (Physics.CheckCapsule(feet + Vector3.up * (radius + .08f), feet + Vector3.up * (height - radius + .08f), radius, ~0, QueryTriggerInteraction.Ignore)) return "occupied_capsule";
            foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                if (NavMesh.SamplePosition(feet + direction * 8, out var end, 2, areaMask))
                {
                    var path = new NavMeshPath();
                    if (NavMesh.CalculatePath(feet, end.position, areaMask, path) && path.status == NavMeshPathStatus.PathComplete) return null;
                }
            return "no_connected_8m_strip";
        }

        [Serializable] sealed class PlacementProbe298
        {
            public Vector3 probe, feet;
            public string mode, failure, floor;
        }
        [Serializable] sealed class PlacementCount298 { public string reason; public int count; }
        [Serializable] sealed class PlacementDiagnosis298
        {
            public string timestamp, scene, id, placeId, realm, status;
            public bool dirtyBefore, dirtyAfter, sceneSaved, manifestChanged;
            public Vector3 originalCentre;
            public PlacementCount298[] failures;
            public PlacementProbe298[] accepted, rejectedExamples;
            public PlacementReceipt[] reserved;
            public int tested, originalModeTested, terrainModeTested;
        }

        // Safe on the dirty candidate left by a preflight failure. It neither
        // opens, saves nor discards a scene, and never rewrites a manifest/SO.
        public static string DiagnosePlacement298(string id)
        {
            RequireEdit(); var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath && scene.path != SourceScene) throw new Exception("Placement diagnosis requires the existing298 candidate or296 source scene; no scene is opened automatically");
            var session = Session(); var manifest = ReadManifest();
            var row = manifest.rows.Single(r => r.id == id); SpeciesDefaults(row);
            var surface = new CompactWorldSurface(session.MountainLayout);
            Vector3 centre = row.preferredFeet;
            if (!string.IsNullOrWhiteSpace(row.placeId))
            {
                var place = session.MountainLayout.Places.Single(p => p.Id == row.placeId);
                centre = new Vector3(place.XZ.x, surface.Sample(place.XZ.x, place.XZ.y), place.XZ.y);
            }
            var report = new PlacementDiagnosis298 { timestamp = DateTime.UtcNow.ToString("O"), scene = scene.path,
                id = id, placeId = row.placeId, realm = row.realm, originalCentre = centre, dirtyBefore = scene.isDirty };
            var original = session.Actors.Where(a => !a.Id.StartsWith("folklore298/", StringComparison.Ordinal)).Select(a => a.transform.position).ToArray();
            var prior = session.Actors.Where(a => a.Id.StartsWith("folklore298/", StringComparison.Ordinal)).SelectMany(a => a.GetComponentsInChildren<Collider>(true)).ToArray();
            var enabled = prior.Select(c => c.enabled).ToArray();
            var reserved = new List<Vector3>(); var reservedRows = new List<PlacementReceipt>();
            var failures = new Dictionary<string, int>(); var accepted = new List<PlacementProbe298>(); var rejected = new List<PlacementProbe298>();
            var nav = session.Actors.Single(a => a.Id == row.templateId).GetComponent<NavMeshAgent>();
            float radius = Mathf.Max(.3f, row.capsuleRadius), height = Mathf.Max(radius * 2 + .1f, row.capsuleHeight);
            void Probe(Vector3 probe, string mode)
            {
                report.tested++; if (mode == "original_fixed_centre_height") report.originalModeTested++; else report.terrainModeTested++;
                string failure = PlacementFailure298(row, session, original, reserved, nav.areaMask, radius, height, probe, out var feet, out var floor);
                var result = new PlacementProbe298 { probe = probe, feet = feet, mode = mode, failure = failure, floor = floor };
                if (failure == null)
                {
                    if (accepted.All(a => Vector3.Distance(a.feet, feet) > 3)) accepted.Add(result);
                }
                else
                {
                    failures[failure] = failures.TryGetValue(failure, out int count) ? count + 1 : 1;
                    if (rejected.Count(r => r.failure == failure) < 8) rejected.Add(result);
                }
            }
            try
            {
                foreach (var c in prior) c.enabled = false; Physics.SyncTransforms();
                foreach (var species in NewSpecies.TakeWhile(s => s != id))
                {
                    var other = manifest.rows.Single(r => r.id == species); SpeciesDefaults(other);
                    var feet = FindPlacement(other, session, reserved); reserved.Add(feet);
                    reservedRows.Add(new PlacementReceipt { id = species, feet = feet, realm = other.realm });
                }
                for (int ring = 0; ring <= Mathf.FloorToInt(Mathf.Min(100, row.searchRadius) / 3); ring++)
                    for (int spoke = 0; spoke < (ring == 0 ? 1 : 24); spoke++)
                    {
                        float angle = spoke * Mathf.PI * 2 / 24;
                        Probe(centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (ring * 3), "original_fixed_centre_height");
                    }
                // Read-only recommendation search, up to220m from the authored
                // place. Each probe starts at its own actual terrain elevation;
                // the same3m Nav/floor/capsule/water/connection rules still apply.
                for (int ring = 0; ring <= 22 && accepted.Count < 24; ring++)
                    for (int spoke = 0; spoke < (ring == 0 ? 1 : Math.Max(24, ring * 8)); spoke++)
                    {
                        int spokes = Math.Max(24, ring * 8); float angle = spoke * Mathf.PI * 2 / spokes;
                        var probe = centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (ring * 10);
                        probe.y = surface.Sample(probe.x, probe.z); Probe(probe, "local_terrain_height");
                    }
            }
            finally
            {
                for (int i = 0; i < prior.Length; i++) prior[i].enabled = enabled[i];
                Physics.SyncTransforms(); report.dirtyAfter = scene.isDirty;
            }
            report.status = accepted.Count > 0 ? "RECOMMENDATIONS_REQUIRE_MANIFEST_SELECTION" : "NO_VALID_PLACEMENT";
            report.accepted = accepted.OrderBy(a => Vector3.Distance(a.feet, centre)).ToArray();
            report.rejectedExamples = rejected.ToArray(); report.reserved = reservedRows.ToArray();
            report.failures = failures.OrderByDescending(p => p.Value).Select(p => new PlacementCount298 { reason = p.Key, count = p.Value }).ToArray();
            string directory = Path.Combine(OutputRoot, "Analysis"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "placement-" + id + ".json"); File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return path + " accepted=" + accepted.Count + " tested=" + report.tested + " dirty=" + report.dirtyBefore + "->" + report.dirtyAfter + " no scene/asset/manifest save";
        }

        // Explicitly requested recovery for the first preflight-only failure.
        // This cannot save296, a partially generated298, or unrelated assets.
        public static string SaveCandidateAuthoringBaseline298()
        {
            RequireEdit(); var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || SceneManager.sceneCount != 1) throw new Exception("Only the single open298 candidate can be saved");
            var session = Session();
            if (AssetDatabase.GetAssetPath(session.Content) != AssetRoot + "/Data/WorldContent298.asset" ||
                AssetDatabase.GetAssetPath(session.MountainLayout) != AssetRoot + "/Data/WorldLayout298.asset" ||
                session.Content.SaveSlot != "world-folklore-298") throw new Exception("Separate298 Content/Layout/save slot required");
            string[] expected = { "mine_beast/1", "sinmok263", "village_road_raider_1", "south_gate_general", "mine_fire/0", "demo_growth_lesson", "cheongryong", "village_road_raider_0", "mine_beast/0" };
            if (session.Actors.Length != 9 || !session.Actors.Select(a => a.Id).OrderBy(s => s).SequenceEqual(expected.OrderBy(s => s)) ||
                scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Any(t => t.name.StartsWith("Folklore298_", StringComparison.Ordinal)) ||
                session.Content.Encounters.Any(e => e.Id.StartsWith("folklore298/", StringComparison.Ordinal)) ||
                session.MountainLayout.Places.Any(p => p.Id.StartsWith("folklore298/", StringComparison.Ordinal)))
                throw new Exception("Preflight-only original9actor baseline required; no generated298 actors/places may exist");
            AssetDatabase.SaveAssetIfDirty(session.Content); AssetDatabase.SaveAssetIfDirty(session.MountainLayout);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new Exception("Candidate baseline save failed");
            return "Saved only298 preflight authoring baseline and its private Content/Layout; original9actors remain. Full build placement preflight must run again.";
        }
    }
}
