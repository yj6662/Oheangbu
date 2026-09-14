using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Directional enclosure and shoulder-camera tests; floor support alone is not enclosure evidence.</summary>
    public static class NaturalCaveShellAudit
    {
        static string Output => WorldMacroNaturalCave.Output + "/ShellAudit";
        static Transform Root => GameObject.Find(WorldMacroNaturalCave.RootName)?.transform;
        public static string Execute(string command)
        {
            if (Root == null) throw new InvalidOperationException("Natural cave is not loaded.");
            if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("Commit >=85%.");
            Directory.CreateDirectory(Output);
            if (command == "survey" || command == "validate") return Survey(command);
            if (command == "runtime") return RuntimeSnapshot();
            if (command == "repair-approach") return RepairApproach();
            if (command == "refine-approach") return RefineApproach();
            if (command == "validate-approach") return ValidateApproach();
            if (command.StartsWith("capture:")) return Capture(command.Substring(8));
            throw new ArgumentException(command);
        }

        static MeshCollider[] Shell()
        {
            return new[] { Root.Find("Natural_Cave_Interior")?.GetComponent<MeshCollider>(),
                Root.Find("Natural_Cave_Floor")?.GetComponent<MeshCollider>() }.Where(c => c != null && c.enabled && c.gameObject.activeInHierarchy).ToArray();
        }
        static bool Nearest(MeshCollider[] shell, Ray ray, float length, out RaycastHit nearest)
        {
            nearest = default; float d = length; bool found = false;
            foreach (var c in shell) if (c.Raycast(ray, out var hit, d)) { found = true; nearest = hit; d = hit.distance; }
            return found;
        }
        struct SoilVertex
        {
            public Vector3 Local;
            public Vector2 Uv;
            public static SoilVertex Lerp(SoilVertex a, SoilVertex b, float t) => new SoilVertex { Local = Vector3.LerpUnclamped(a.Local, b.Local, t), Uv = Vector2.LerpUnclamped(a.Uv, b.Uv, t) };
        }
        static MeshFilter Approach()
        {
            var all = Root.GetComponentsInChildren<MeshFilter>().Where(f => f.name == "Continuous_Approach_Soil").ToArray();
            if (all.Length != 1) throw new InvalidOperationException("Expected exactly one active Continuous_Approach_Soil.");
            return all[0];
        }
        static string RepairApproach()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Playtest Edit scene required.");
            var filter = Approach(); var original = filter.sharedMesh;
            if (original.name.StartsWith("Continuous_Approach_JoinedToInterior")) return "Already repaired. " + ValidateApproach();
            foreach (string view in new[] { "join-left", "join-right" })
                if (File.Exists(Output + "/" + view + ".png") && !File.Exists(Output + "/" + view + "-before.png"))
                    File.Copy(Output + "/" + view + ".png", Output + "/" + view + "-before.png");
            var root = Root; var source = original.vertices; var uv = original.uv; var indices = original.triangles;
            var vertices = new List<Vector3>(); var texcoords = new List<Vector2>(); var triangles = new List<int>();
            var welded = new Dictionary<Vector3Int, int>(); int clippedTriangles = 0, inputRaised = 0;
            var referenced = indices.Distinct().ToArray();
            foreach (int i in referenced)
            { var q = root.InverseTransformPoint(filter.transform.TransformPoint(source[i])); if (q.x < -43 && q.y > .1f) inputRaised++; }
            int Add(SoilVertex v)
            {
                // Interior floor ends at X=-43,Y=.03. The former approach extended 11m behind it and
                // sampled sloping cave walls as ground. Keep only a 2cm lap; match the join before easing out.
                float t = Mathf.InverseLerp(-43, -42.2f, v.Local.x); t = t * t * (3 - 2 * t);
                v.Local.y = Mathf.Lerp(.03f, v.Local.y, t);
                var point = filter.transform.InverseTransformPoint(root.TransformPoint(v.Local));
                var key = new Vector3Int(Mathf.RoundToInt(point.x * 10000), Mathf.RoundToInt(point.y * 10000), Mathf.RoundToInt(point.z * 10000));
                if (welded.TryGetValue(key, out int existing)) return existing;
                int id = vertices.Count; welded[key] = id; vertices.Add(point); texcoords.Add(v.Uv); return id;
            }
            for (int i = 0; i < indices.Length; i += 3)
            {
                var input = new SoilVertex[3]; var polygon = new List<SoilVertex>(4);
                for (int j = 0; j < 3; j++) { int k = indices[i + j]; input[j] = new SoilVertex { Local = root.InverseTransformPoint(filter.transform.TransformPoint(source[k])), Uv = uv.Length == source.Length ? uv[k] : Vector2.zero }; }
                for (int j = 0; j < 3; j++)
                {
                    var a = input[j]; var b = input[(j + 1) % 3]; bool insideA = a.Local.x >= -43.02f, insideB = b.Local.x >= -43.02f;
                    if (insideA) polygon.Add(a);
                    if (insideA != insideB) polygon.Add(SoilVertex.Lerp(a, b, (-43.02f - a.Local.x) / (b.Local.x - a.Local.x)));
                }
                if (input.Any(v => v.Local.x < -43.02f)) clippedTriangles++;
                for (int j = 1; j < polygon.Count - 1; j++)
                {
                    int a = Add(polygon[0]), b = Add(polygon[j]), c = Add(polygon[j + 1]);
                    if (a == b || b == c || c == a || Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude < 1e-12f) continue;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                }
            }
            if (triangles.Count < 300) throw new InvalidOperationException("Refusing an unexpectedly empty derivative.");
            string folder = WorldMacroNaturalCave.Folder + "/ShellRepair"; DevSceneKit.EnsureFolder(folder);
            var mesh = new Mesh { name = "Continuous_Approach_JoinedToInterior", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, texcoords); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Continuous_Approach_JoinedToInterior.asset"); AssetDatabase.CreateAsset(mesh, path);
            filter.sharedMesh = mesh; var collider = filter.GetComponent<MeshCollider>(); collider.sharedMesh = null; collider.sharedMesh = mesh;
            EditorUtility.SetDirty(filter); EditorUtility.SetDirty(collider); EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets(); Physics.SyncTransforms();
            var ledger = "Original preserved: " + AssetDatabase.GetAssetPath(original) + "\nDerivative: " + path
                + "\nApproach triangles " + indices.Length / 3 + " -> " + triangles.Count / 3 + "; vertices " + source.Length + " -> " + vertices.Count
                + "\nReferenced approach vertices above .10m behind the interior aperture: " + inputRaised
                + "\nClipped input triangles: " + clippedTriangles + ". Existing primary cave/floor/portal and all gameplay positions retained. No entrance cap added.";
            File.WriteAllText(Output + "/approach_repair.txt", ledger);
            return ledger + "\n" + ValidateApproach();
        }
        static string ValidateApproach()
        {
            var filter = Approach(); var approach = filter.GetComponent<MeshCollider>(); var floor = Root.Find("Natural_Cave_Floor").GetComponent<MeshCollider>();
            var lines = new List<string>(); var failures = new List<string>(); int sections = 0, samples = 0, supported = 0; float maximumHeightDelta = 0;
            bool oldBack = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
            try
            {
                Physics.SyncTransforms();
                for (float z = -15; z <= 25; z += .25f)
                {
                    Ray RayAt(float x) => new Ray(Root.TransformPoint(new Vector3(x, 2, z)), Vector3.down);
                    if (!floor.Raycast(RayAt(-43.15f), out _, 3) || !approach.Raycast(RayAt(-42.85f), out _, 3)) continue;
                    sections++; var heights = new List<float>();
                    foreach (float x in new[] { -43.15f, -43.10f, -43.05f, -43, -42.95f, -42.90f, -42.85f })
                    {
                        samples++; bool found = Nearest(new[] { floor, approach }, RayAt(x), 3, out var hit);
                        if (found) { supported++; heights.Add(Root.InverseTransformPoint(hit.point).y); }
                        else failures.Add("UNSUPPORTED local=" + new Vector3(x, .03f, z));
                    }
                    if (heights.Count > 0)
                    {
                        float delta = heights.Max() - heights.Min(); maximumHeightDelta = Mathf.Max(maximumHeightDelta, delta);
                        if (delta > .06f) failures.Add("STEP z=" + z + " 30cm width delta=" + delta);
                    }
                }
            }
            finally { Physics.queriesHitBackfaces = oldBack; }
            lines.Add((sections > 0 && failures.Count == 0 ? "PASS" : "FAIL") + " join support across 30cm strip; sections=" + sections + " samples=" + samples + " supported=" + supported + " maxHeightDelta=" + maximumHeightDelta);
            lines.Add("Same render/collider derivative=" + (filter.sharedMesh == approach.sharedMesh) + "; actual input/character walking remains unverified.");
            lines.AddRange(failures); File.WriteAllLines(Output + "/approach_validation.txt", lines); return string.Join("\n", lines);
        }
        static string RefineApproach()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Playtest Edit scene required.");
            var filter = Approach(); var source = filter.sharedMesh;
            if (source.name == "Continuous_Approach_JoinedToInterior_SoftJoin") return "Already refined. " + ValidateApproach();
            if (source.name != "Continuous_Approach_JoinedToInterior") throw new InvalidOperationException("Refine only the current clipped 2808-triangle derivative, never the archived full approach.");
            foreach (string view in new[] { "join-left", "join-right" })
                if (File.Exists(Output + "/" + view + ".png") && !File.Exists(Output + "/" + view + "-before.png"))
                    File.Copy(Output + "/" + view + ".png", Output + "/" + view + "-before.png");
            if (File.Exists(Output + "/approach_validation.txt")) File.Copy(Output + "/approach_validation.txt", Output + "/approach_validation-first.txt", true);
            var mesh = Object.Instantiate(source); mesh.name = "Continuous_Approach_JoinedToInterior_SoftJoin";
            var vertices = mesh.vertices; int changed = 0; float maxChange = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                var p = Root.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                if (p.x >= -39) continue;
                // A sub-metre easing ended before the next 1m mesh column, leaving linear triangles
                // to climb 16cm within the first 15cm. Use four full grid columns for the grade.
                float t = Mathf.InverseLerp(-43, -39, p.x); t = t * t * (3 - 2 * t);
                float y = Mathf.Lerp(.03f, p.y, t); maxChange = Mathf.Max(maxChange, Mathf.Abs(y - p.y));
                if (Mathf.Abs(y - p.y) > .00001f) changed++;
                p.y = y; vertices[i] = filter.transform.InverseTransformPoint(Root.TransformPoint(p));
            }
            mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            string path = AssetDatabase.GenerateUniqueAssetPath(WorldMacroNaturalCave.Folder + "/ShellRepair/" + mesh.name + ".asset"); AssetDatabase.CreateAsset(mesh, path);
            filter.sharedMesh = mesh; var collider = filter.GetComponent<MeshCollider>(); collider.sharedMesh = null; collider.sharedMesh = mesh;
            EditorUtility.SetDirty(filter); EditorUtility.SetDirty(collider); EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets(); Physics.SyncTransforms();
            var ledger = "Source preserved: " + AssetDatabase.GetAssetPath(source) + "\nDerivative: " + path + "\nJoin blend expanded to X[-43,-39] (four actual 1m columns)."
                + "\nChanged vertices=" + changed + "; max vertical adjustment=" + maxChange + "; triangles remain=" + mesh.triangles.Length / 3
                + ". The 30cm support test is retained; its failure threshold was not relaxed.";
            File.WriteAllText(Output + "/approach_refinement.txt", ledger); return ledger + "\n" + ValidateApproach();
        }
        static bool ThroughMouth(Vector3 origin, Vector3 direction)
        {
            if (direction.x <= .0001f) return false;
            float t = (-43 - origin.x) / direction.x;
            if (t <= 0) return false;
            var p = origin + direction * t;
            // This class audits the closed deep shell. Its X=-43 open portal is separately checked in screenshots.
            return p.y >= -.005f && p.y <= 15.1f && p.z >= -10 && p.z <= 21;
        }
        static string Survey(string suffix)
        {
            var root = Root; var shell = Shell(); var lines = new List<string>();
            if (shell.Length != 2) throw new InvalidOperationException("Both active primary render/collider surfaces are required.");
            var floor = shell.First(c => c.name == "Natural_Cave_Floor");
            lines.Add("All positions and ray directions below are cave-local metres. Sampling is a geometry test, not an input walkthrough.");
            foreach (var c in shell)
            {
                var r = c.GetComponent<Renderer>(); var f = c.GetComponent<MeshFilter>();
                lines.Add("SURFACE " + c.name + " active=" + c.gameObject.activeInHierarchy + " collider=" + c.enabled + " render=" + (r != null && r.enabled)
                    + " layer=" + c.gameObject.layer + " sameMesh=" + (f.sharedMesh == c.sharedMesh) + " source=" + AssetDatabase.GetAssetPath(f.sharedMesh));
            }
            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (camera.enabled || camera.name.Contains("Main")) lines.Add("CAMERA " + camera.name + " active=" + camera.isActiveAndEnabled + " near=" + camera.nearClipPlane + " far=" + camera.farClipPlane
                    + " mask=" + camera.cullingMask + " local=" + root.InverseTransformPoint(camera.transform.position));

            var errors = new List<string>(); int rayCount = 0, hits = 0, openings = 0, escape = 0, backfaceMismatch = 0, origins = 0;
            var cameraOrigins = new List<Vector3>(); bool oldBack = Physics.queriesHitBackfaces;
            try
            {
                Physics.SyncTransforms(); Physics.queriesHitBackfaces = true;
                for (int x = -160; x <= -48; x += 6) for (int z = -16; z <= 24; z += 4)
                {
                    if (!floor.Raycast(new Ray(root.TransformPoint(new Vector3(x, 1, z)), Vector3.down), out _, 2)) continue;
                    foreach (float y in new[] { 1.1f, 1.65f, 2.35f })
                    {
                        var local = new Vector3(x, y, z); var world = root.TransformPoint(local);
                        bool tooClose = false;
                        for (int a = 0; a < 360; a += 45)
                            if (Nearest(shell, new Ray(world, root.TransformDirection(Quaternion.Euler(0, a, 0) * Vector3.forward)), .7f, out _)) { tooClose = true; break; }
                        if (tooClose) continue;
                        origins++; if (y == 1.65f) cameraOrigins.Add(local);
                        foreach (int pitch in new[] { -70, -45, -20, 0, 20, 45, 70, 90 }) for (int yaw = 0; yaw < 360; yaw += 15)
                        {
                            float a = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
                            var direction = new Vector3(Mathf.Sin(a) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(a) * Mathf.Cos(p));
                            var ray = new Ray(world, root.TransformDirection(direction)); rayCount++;
                            Physics.queriesHitBackfaces = true;
                            bool actual = Nearest(shell, ray, 250, out var allHit);
                            Physics.queriesHitBackfaces = false;
                            bool front = Nearest(shell, ray, 250, out var frontHit);
                            Physics.queriesHitBackfaces = true;
                            if (actual)
                            {
                                hits++;
                                if (!front || Mathf.Abs(frontHit.distance - allHit.distance) > .04f)
                                { backfaceMismatch++; if (errors.Count < 100) errors.Add("BACKFACE " + local + " direction=" + direction + " expected=" + allHit.distance + " actual=" + (front ? frontHit.distance : -1)); }
                            }
                            else if (ThroughMouth(local, direction)) openings++;
                            else { escape++; if (errors.Count < 100) errors.Add("ESCAPE " + local + " direction=" + direction); }
                        }
                    }
                }
                lines.Add("ENCLOSURE origins=" + origins + " directions=" + rayCount + " hit=" + hits + " intentionalMouth=" + openings + " unexpectedEscape=" + escape + " backfaceMismatch=" + backfaceMismatch);
                CameraSweep(shell, cameraOrigins, lines, errors);
            }
            finally { Physics.queriesHitBackfaces = oldBack; }
            lines.AddRange(errors);
            lines.Add("UNVERIFIED actual input, runtime render occlusion, near-wall screenshots and outer portal/terrain seam until separately captured.");
            File.WriteAllLines(Output + "/" + suffix + ".txt", lines);
            return string.Join("\n", lines.Take(20)) + "\n" + Output + "/" + suffix + ".txt";
        }

        static void CameraSweep(MeshCollider[] shell, List<Vector3> samples, List<string> lines, List<string> errors)
        {
            var root = Root;
            // Match actual authored player rig where available. Avoid changing either controller or its target.
            var rig = Object.FindFirstObjectByType<Oheangbu.Combat.CameraRigController>();
            Vector3 offset = new Vector3(.55f, 0, -2.7f); float near = .08f, fov = 60, aspect = 16f / 9;
            if (rig != null)
            {
                var so = new SerializedObject(rig);
                var cfg = so.FindProperty("_config").objectReferenceValue as Oheangbu.Combat.CombatConfigSO;
                if (cfg != null) offset = cfg.ShoulderOffset;
                var cameraTransform = so.FindProperty("_camera").objectReferenceValue as Transform;
                var camera = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null;
                if (camera != null) { near = camera.nearClipPlane; fov = camera.fieldOfView; }
            }
            var wall = shell.First(c => c.name == "Natural_Cave_Interior");
            var mesh = wall.sharedMesh; var v = mesh.vertices; var n = mesh.normals;
            // Include close-wall positions missing from the former path-only check.
            for (int i = 0; i < v.Length; i += 7)
            {
                if (v[i].y < 1.35f || v[i].y > 1.95f || v[i].x > -47 || n.Length != v.Length) continue;
                var q = root.InverseTransformPoint(wall.transform.TransformPoint(v[i] + n[i] * .45f));
                if (q.y < 1 || q.y > 2.4f) continue;
                if (shell[1].Raycast(new Ray(root.TransformPoint(q), Vector3.down), out _, 3)) samples.Add(q);
            }
            int poses = 0, rayMiss = 0, nearMiss = 0, initialOverlap = 0;
            foreach (var q in samples) for (int yaw = 0; yaw < 360; yaw += 30) foreach (int pitch in new[] { -25, 0, 25 })
            {
                var orbit = root.rotation * Quaternion.Euler(pitch, yaw, 0); var focus = root.TransformPoint(q);
                var delta = orbit * offset; float length = delta.magnitude;
                Physics.queriesHitBackfaces = false;
                bool cast = Physics.SphereCast(focus, .25f, delta / length, out var hit, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float allowed = cast ? hit.distance : length; var candidate = focus + delta.normalized * allowed; poses++;
                Physics.queriesHitBackfaces = true;
                if (Nearest(shell, new Ray(focus, delta.normalized), allowed - .005f, out var shellHit))
                { rayMiss++; if (errors.Count < 100) errors.Add("BOOM_EXIT focus=" + q + " yaw=" + yaw + " pitch=" + pitch + " length=" + allowed + " shellDistance=" + shellHit.distance); }
                // Actual near-plane corners can protrude even if the camera centre stayed inside.
                float halfY = near * Mathf.Tan(fov * .5f * Mathf.Deg2Rad), halfX = halfY * aspect;
                bool bad = false;
                foreach (float x in new[] { -halfX, halfX }) foreach (float y in new[] { -halfY, halfY })
                {
                    var corner = candidate + orbit * new Vector3(x, y, near); var cd = corner - focus;
                    if (Nearest(shell, new Ray(focus, cd.normalized), cd.magnitude - .005f, out _)) bad = true;
                }
                if (bad) nearMiss++;
                // Point-start overlap cannot be resolved by a SphereCast. Record separately from actual exit.
                if (Physics.OverlapSphere(focus, .25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(c => shell.Contains(c))) initialOverlap++;
            }
            lines.Add("SHOULDER offset=" + offset + " radius=0.25 near=" + near + " sampleOrigins=" + samples.Count + " poses=" + poses
                + " centreBeyondShell=" + rayMiss + " nearCornersBeyondShell=" + nearMiss + " pivotBroadphaseOverlap=" + initialOverlap);
            lines.Add("Pivot broadphase overlap is only a diagnostic candidate; non-convex mesh overlap is not proof of penetration.");
        }

        static string Capture(string key)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit environment capture only.");
            Vector3 local = new Vector3(-136, 1.65f, 4); Vector3 d;
            switch (key)
            {
                case "start-forward": d = Vector3.right; break;
                case "start-back": d = Vector3.left; break;
                case "start-left": d = Vector3.forward; break;
                case "start-right": d = Vector3.back; break;
                case "start-ceiling": d = new Vector3(.3f, 1, .2f); break;
                case "join-left": local = new Vector3(-46, 1.65f, 5); d = Vector3.forward; break;
                case "join-right": local = new Vector3(-46, 1.65f, 5); d = Vector3.back; break;
                default: throw new ArgumentException(key);
            }
            var p = Root.TransformPoint(local); var t = p + Root.TransformDirection(d) * 12;
            var actors = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(r => r.enabled).ToArray();
            try
            {
                foreach (var r in actors) r.enabled = false;
                var image = WorldMacroDressingProbe.Capture("CaveShell_" + key, true, p.x, p.y, p.z, t.x, t.y, t.z);
                File.Copy(image, Output + "/" + key + ".png", true);
                return Output + "/" + key + ".png";
            }
            finally { foreach (var r in actors) r.enabled = true; }
        }

        static string RuntimeSnapshot()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Runtime snapshot requires Play mode.");
            var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
            var source = walker != null && walker.ViewCamera != null && walker.ViewCamera.isActiveAndEnabled ? walker.ViewCamera : Camera.main;
            if (source == null || !source.isActiveAndEnabled) throw new InvalidOperationException("Active player camera missing.");
            var root = Root; var lines = new List<string>();
            lines.Add("Current Play pose copied without moving the player, source camera or geometry. 1920x1080 offscreen render; no actual input traversal.");
            lines.Add("UTC=" + DateTime.UtcNow.ToString("o") + " time=" + Time.time + " scale=" + Time.timeScale + " commit=" + Prologue.PrologueAudit.CommitRatio());
            lines.Add("PLAYER feet=" + (walker != null && walker.Body != null ? root.InverseTransformPoint(walker.Body.transform.position).ToString("F4") : "missing"));
            lines.Add("VIEW " + source.name + " local=" + root.InverseTransformPoint(source.transform.position).ToString("F4") + " euler=" + source.transform.eulerAngles.ToString("F4")
                + " pixel=" + source.pixelWidth + "x" + source.pixelHeight + " depth=" + source.depth + " clear=" + source.clearFlags + " near=" + source.nearClipPlane + " far=" + source.farClipPlane);
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                lines.Add("CAMERA " + c.name + " active=" + c.isActiveAndEnabled + " depth=" + c.depth + " mask=" + c.cullingMask + " type=" + c.cameraType + " pos=" + c.transform.position);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                var f = r.GetComponent<MeshFilter>(); var collider = r.GetComponent<MeshCollider>();
                lines.Add("RENDER " + r.name + " enabled=" + r.enabled + " forceOff=" + r.forceRenderingOff + " visible=" + r.isVisible
                    + " staticBatch=" + r.isPartOfStaticBatch + " layer=" + r.gameObject.layer + " inMask=" + ((source.cullingMask & (1 << r.gameObject.layer)) != 0)
                    + " worldBounds=" + r.bounds.ToString("F3") + " centreViewport=" + source.WorldToViewportPoint(r.bounds.center).ToString("F3")
                    + " renderMesh=" + (f != null && f.sharedMesh != null ? f.sharedMesh.name + ":" + f.sharedMesh.vertexCount : "missing")
                    + " colliderMesh=" + (collider != null && collider.sharedMesh != null ? collider.sharedMesh.name + ":" + collider.sharedMesh.vertexCount : "none"));
                foreach (var m in r.sharedMaterials)
                    lines.Add(" MATERIAL " + (m == null ? "missing" : m.name + " shader=" + m.shader.name + " supported=" + m.shader.isSupported + " queue=" + m.renderQueue
                        + " cull=" + (m.HasProperty("_Cull") ? m.GetFloat("_Cull").ToString() : "n/a") + " alphaClip=" + (m.HasProperty("_AlphaClip") ? m.GetFloat("_AlphaClip").ToString() : "n/a")));
            }
            bool back = Physics.queriesHitBackfaces;
            try
            {
                Physics.queriesHitBackfaces = true;
                foreach (var d in new[] { source.transform.forward, -source.transform.forward, source.transform.right, -source.transform.right, Vector3.up, Vector3.down })
                {
                    var hits = Physics.RaycastAll(source.transform.position, d, 250, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        .Where(h => h.collider.transform.IsChildOf(root)).OrderBy(h => h.distance).Take(3);
                    lines.Add("VIEW_RAY " + root.InverseTransformDirection(d) + " => " + string.Join("; ", hits.Select(h => h.collider.name + ":" + h.distance.ToString("F3"))));
                }
            }
            finally { Physics.queriesHitBackfaces = back; }

            var dressing = Object.FindFirstObjectByType<WorldMacroDressingRenderer>();
            bool diagnosticBefore = dressing != null && dressing.AllowDiagnosticCameras;
            GameObject go = null; RenderTexture target = null; Texture2D pixels = null; var activeBefore = RenderTexture.active;
            var errors = new List<string>();
            void Log(string message, string stack, LogType type)
            { if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && errors.Count < 10) errors.Add(type + ":" + message); }
            try
            {
                go = new GameObject("Cave_RuntimePose_Snapshot") { hideFlags = HideFlags.HideAndDontSave };
                var camera = go.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
                camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                camera.aspect = 16f / 9; camera.rect = new Rect(0, 0, 1, 1);
                var additional = source.GetComponent<UniversalAdditionalCameraData>();
                if (additional != null) EditorUtility.CopySerialized(additional, camera.GetUniversalAdditionalCameraData());
                // Retain URP renderer, post-processing and scene layers. No LOD preload, material changes or geometry masks.
                if (dressing != null) dressing.AllowDiagnosticCameras = true;
                target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); target.Create();
                camera.targetTexture = target;
                Application.logMessageReceived += Log;
                try { camera.Render(); }
                finally { Application.logMessageReceived -= Log; }
                if (errors.Count > 0) throw new InvalidOperationException("Render rejected: " + string.Join("\n", errors));
                RenderTexture.active = target; pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply(false);
                File.WriteAllBytes(Output + "/runtime-current.png", pixels.EncodeToPNG());
                lines.Add("CAPTURE runtime-current.png; current player pose, source URP data and loaded dressing retained. Overlay Screen Space UI may render through a separate path.");
            }
            finally
            {
                RenderTexture.active = activeBefore;
                if (dressing != null) dressing.AllowDiagnosticCameras = diagnosticBefore;
                if (go != null) { go.GetComponent<Camera>().targetTexture = null; Object.DestroyImmediate(go); }
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (pixels != null) Object.DestroyImmediate(pixels);
                File.WriteAllLines(Output + "/runtime-current.txt", lines.Concat(errors));
            }
            return Output + "/runtime-current.txt\n" + Output + "/runtime-current.png";
        }
    }
}
