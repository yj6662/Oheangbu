using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Read-only physics evidence for the fixed review views. No camera or scene objects are created.</summary>
    public static class InkPaintingViewProbe
    {
        [Serializable] sealed class View { public string id; public Vector3 eye, target; }
        [Serializable] sealed class Views { public View[] views; }
        [Serializable] public sealed class Sample
        {
            public int column, row, physicsHits, excludedHits;
            public Vector2 imageUv, viewportUv;
            public string status, colliderPath, rendererPath, meshAsset;
            public string[] shaders;
            public Vector3 rayDirection, point, normal;
            public float distance;
        }
        [Serializable] public sealed class Surface
        {
            public string colliderPath, meshAsset;
            public int samples;
            public float nearestDistance, farthestDistance, minimumHeight, maximumHeight;
        }
        [Serializable] public sealed class Report
        {
            public string status, utc, scene, view, viewsSource, limitation;
            public Vector3 eye, target;
            public int columns = 16, rows = 9, hits, unresolved;
            public float verticalFov = 60, aspect = 16f / 9f, viewportYMin = .35f, viewportYMax = 1, maximumDistance = 4500;
            public bool sceneDirtyBefore, sceneDirtyAfter;
            public Sample[] samples, highestSurfaceSamples, elevatedSurfaceSamples;
            public Surface[] commonSurfaces;
        }

        public static string Probe(string output, string view)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("View probe requires Edit mode.");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.path != WorldMacroCompactAuthoring.TargetScene)
                throw new InvalidOperationException("Open the actual compact scene before probing its physics.");
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(view)) throw new ArgumentException("An output directory and fixed view ID are required.");
            string viewsSource = new[] { Path.Combine(output, "views.json"),
                WorldMacroCompactAuthoring.Output + "/InkLandscape/InkPaintingStudy/views.json",
                WorldMacroCompactAuthoring.Output + "/InkLandscape/BroadBrush/views.json" }.FirstOrDefault(File.Exists);
            if (viewsSource == null) throw new FileNotFoundException("No fixed review views.json was found.");
            var views = JsonUtility.FromJson<Views>(File.ReadAllText(viewsSource));
            var selected = views?.views?.SingleOrDefault(v => v.id == view);
            if (selected == null) throw new ArgumentException("Unknown fixed review view: " + view);
            var forward = selected.target - selected.eye;
            if (forward.sqrMagnitude < .0001f) throw new InvalidOperationException("Invalid fixed view direction.");
            var report = new Report { status = "MEASURED_PHYSICS_ONLY", utc = DateTime.UtcNow.ToString("o"), scene = scene.path,
                view = view, viewsSource = Path.GetFullPath(viewsSource), eye = selected.eye, target = selected.target, sceneDirtyBefore = scene.isDirty,
                limitation = "Read-only current Physics.RaycastAll, not a render or visibility test. Non-surface colliders are skipped, so selected terrain may be occluded by a building or vegetation. No colliders are added or synchronized. Render-only Context meshes cannot be identified by this probe; misses mean BACKGROUND_OR_UNRESOLVED, not a pass. Highest points are sampled hits only, not global mountain summits." };
            var rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            float tangent = Mathf.Tan(report.verticalFov * .5f * Mathf.Deg2Rad);
            var samples = new List<Sample>(report.columns * report.rows);
            for (int row = 0; row < report.rows; row++) for (int column = 0; column < report.columns; column++)
            {
                float u = (column + .5f) / report.columns;
                float v = Mathf.Lerp(report.viewportYMax, report.viewportYMin, (row + .5f) / report.rows);
                var direction = rotation * new Vector3((2 * u - 1) * tangent * report.aspect, (2 * v - 1) * tangent, 1).normalized;
                var sample = new Sample { column = column, row = row, imageUv = new Vector2(u, 1 - v), viewportUv = new Vector2(u, v),
                    rayDirection = direction, status = "BACKGROUND_OR_UNRESOLVED", colliderPath = "", rendererPath = "", meshAsset = "", shaders = Array.Empty<string>() };
                var hits = Physics.RaycastAll(selected.eye, direction, report.maximumDistance, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                sample.physicsHits = hits.Length;
                foreach (var hit in hits)
                {
                    var collider = hit.collider;
                    if (collider == null || collider.gameObject.scene != scene) { sample.excludedHits++; continue; }
                    // The authored terrain collider and visual renderer share the same GameObject.
                    // Do not ascend to a building's parent renderer or infer a material from its children.
                    var renderer = collider.GetComponent<MeshRenderer>();
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !renderer.sharedMaterials.Any(IsSurface))
                    { sample.excludedHits++; continue; }
                    sample.status = "SELECTED_SURFACE_PHYSICS_HIT";
                    sample.colliderPath = Hierarchy(collider.transform); sample.rendererPath = Hierarchy(renderer.transform);
                    sample.shaders = renderer.sharedMaterials.Where(m => m != null && m.shader != null).Select(m => m.shader.name).Distinct().ToArray();
                    var mesh = collider is MeshCollider mc ? mc.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    sample.meshAsset = mesh == null ? "" : AssetDatabase.GetAssetPath(mesh);
                    sample.point = hit.point; sample.normal = hit.normal; sample.distance = hit.distance;
                    report.hits++;
                    break;
                }
                if (sample.status == "BACKGROUND_OR_UNRESOLVED") report.unresolved++;
                samples.Add(sample);
            }
            report.samples = samples.ToArray();
            var measured = samples.Where(s => s.status == "SELECTED_SURFACE_PHYSICS_HIT").ToArray();
            report.commonSurfaces = measured.GroupBy(s => s.colliderPath).Select(g => new Surface { colliderPath = g.Key,
                meshAsset = g.First().meshAsset, samples = g.Count(), nearestDistance = g.Min(s => s.distance), farthestDistance = g.Max(s => s.distance),
                minimumHeight = g.Min(s => s.point.y), maximumHeight = g.Max(s => s.point.y) }).OrderByDescending(s => s.samples).ThenBy(s => s.colliderPath).ToArray();
            report.highestSurfaceSamples = measured.OrderByDescending(s => s.point.y).Take(12).ToArray();
            report.elevatedSurfaceSamples = measured.Where(s => s.point.y >= selected.eye.y + 35).OrderByDescending(s => s.point.y).Take(24).ToArray();
            report.sceneDirtyAfter = scene.isDirty;
            if (report.sceneDirtyBefore != report.sceneDirtyAfter) report.status = "UNEXPECTED_SCENE_DIRTY_CHANGE";
            Directory.CreateDirectory(output);
            string path = Path.Combine(output, view + "_surface_probe.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            string common = string.Join("; ", report.commonSurfaces.Take(8).Select(s => s.colliderPath + "=" + s.samples));
            return report.status + ": " + report.hits + "/" + samples.Count + " selected-surface hits, " + report.unresolved
                + " background/unresolved. " + common + ". Report: " + Path.GetFullPath(path);
        }

        static bool IsSurface(Material material)
        {
            if (material == null || material.shader == null) return false;
            string shader = material.shader.name;
            return shader == "Oheangbu/CompactNaturalGround" || shader == "Oheangbu/WorldMacroTerrain"
                || shader == "Oheangbu/Study/InkPaintingGround" || shader == "Oheangbu/Study/InkPaintingTerrain";
        }

        static string Hierarchy(Transform value)
        {
            string path = value.name;
            while (value.parent != null) { value = value.parent; path = value.name + "/" + path; }
            return path;
        }
    }
}
