using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // [SPEC-EDITOR-MEMORY-308 B] Did the 0.2 m vertex clustering of the large collider meshes change where the player can walk?
    // Each pair (old mesh -> new mesh, Tools/Unity/Stage308_ops/logs/mem/colliders.json) is put into its own preview scene (own physics
    // scene, nothing is left in the open scenes) at the pose of a collider that uses it, and both are probed with the same rays.
    //   list                 the pairs and where they are used in the open scenes
    //   pair:<i>[:<grid m>]  probe one pair, write Tools/Unity/Stage308_stream/collider_check/<i>.json
    //   report               one table from the files written so far
    // Read-only for the project: no asset and no scene is changed.
    public static class ColliderCheck308
    {
        [Serializable] sealed class Pairs { public float cell; public Pair[] pairs; }
        [Serializable] sealed class Pair { public string old; public string @new; public int v0, v1; }
        [Serializable] sealed class Spot { public float x, y, z, was, now; }
        [Serializable] sealed class Result
        {
            public int index; public string oldPath, newPath, usedBy; public int uses; public float grid, step, radius, height, slope;
            public int columns, walkable, missing, over15, stepPairs, stepBroken, standFree, standBlocked, wallRays, wallOpened;
            public float maxDiff, p95Diff, seconds;
            public List<Spot> worst = new List<Spot>(), steps = new List<Spot>(), blocked = new List<Spot>(), opened = new List<Spot>();
        }

        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        static string PairsFile => Path.Combine(Repo, "Tools", "Unity", "Stage308_ops", "logs", "mem", "colliders.json");
        static string OutDir => Path.Combine(Repo, "Tools", "Unity", "Stage308_stream", "collider_check");
        static string F(float v, string f = "F2") => v.ToString(f, CultureInfo.InvariantCulture);

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            var pairs = JsonUtility.FromJson<Pairs>(File.ReadAllText(PairsFile)).pairs;
            switch (a[0])
            {
                case "list": return List(pairs);
                case "pair": return Probe(pairs, int.Parse(a[1], CultureInfo.InvariantCulture), a.Length > 2 ? float.Parse(a[2], CultureInfo.InvariantCulture) : .25f, null, "");
                // try:<i>:<cell> — probe the old mesh against a fresh clustering on <cell> (nothing written but <i>_try<cell>.json)
                case "try": { int i = int.Parse(a[1], CultureInfo.InvariantCulture); var old = AssetDatabase.LoadAssetAtPath<Mesh>(pairs[i].old); var made = MemoryFix308.Cluster(old, float.Parse(a[2], CultureInfo.InvariantCulture)); string text = Probe(pairs, i, .25f, made, "_try" + a[2]); int v = made.vertexCount; Object.DestroyImmediate(made); return "TRY cell " + a[2] + " vertices " + v + " | " + text; }
                // rebuild:<i>:<cell> — write a fresh clustering INTO the pair's new mesh asset (same asset, same guid: no scene changes)
                case "rebuild": return Rebuild(pairs, int.Parse(a[1], CultureInfo.InvariantCulture), float.Parse(a[2], CultureInfo.InvariantCulture));
                case "report": return Report(pairs);
                default: return "commands: list | pair:<i>[:<grid m>] | try:<i>:<cell> | rebuild:<i>:<cell> | report";
            }
        }

        static List<MeshCollider> Users(Mesh mesh) => Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.sharedMesh == mesh).ToList();

        static string List(Pair[] pairs)
        {
            var sb = new StringBuilder("ColliderCheck308 list (open scenes " + SceneManager.sceneCount + ")\n");
            for (int i = 0; i < pairs.Length; i++)
            {
                var now = AssetDatabase.LoadAssetAtPath<Mesh>(pairs[i].@new); var was = AssetDatabase.LoadAssetAtPath<Mesh>(pairs[i].old);
                var users = now != null ? Users(now) : new List<MeshCollider>(); var oldUsers = was != null ? Users(was) : new List<MeshCollider>();
                sb.AppendLine(i.ToString().PadLeft(3) + " new users " + users.Count + " old users " + oldUsers.Count + " v " + pairs[i].v0 + " -> " + pairs[i].v1 + "  " + Path.GetFileName(pairs[i].old)
                    + (users.Count > 0 ? "  @ " + users[0].gameObject.scene.name + ":" + users[0].name + " size " + users[0].bounds.size.ToString("F0") : ""));
            }
            return sb.ToString();
        }

        sealed class Rig : IDisposable
        {
            public Scene Scene; public PhysicsScene Physics; public MeshCollider Collider;
            public Rig(Mesh mesh, Transform like)
            {
                Scene = EditorSceneManager.NewPreviewScene(); Physics = Scene.GetPhysicsScene();
                var go = new GameObject("probe") { hideFlags = HideFlags.HideAndDontSave }; SceneManager.MoveGameObjectToScene(go, Scene);
                if (like != null) { go.transform.SetPositionAndRotation(like.position, like.rotation); go.transform.localScale = like.lossyScale; }
                Collider = go.AddComponent<MeshCollider>(); Collider.sharedMesh = mesh;
                UnityEngine.Physics.SyncTransforms();
            }
            public void Dispose() { EditorSceneManager.ClosePreviewScene(Scene); }

            /// <summary>Every upward-facing surface under a column, top first (a mesh collider answers one hit a ray: go on below it).</summary>
            public void Layers(float x, float z, float top, float bottom, List<RaycastHit> into)
            {
                into.Clear(); float y = top;
                for (int k = 0; k < 16 && y > bottom; k++)
                {
                    if (!Physics.Raycast(new Vector3(x, y, z), Vector3.down, out var hit, y - bottom, ~0, QueryTriggerInteraction.Ignore)) break;
                    into.Add(hit); y = hit.point.y - .02f;
                }
            }
            public bool Blocked(Vector3 feet, float lift, float radius, float height)
            {
                var hits = new Collider[1];
                return Physics.OverlapCapsule(feet + Vector3.up * (lift + radius), feet + Vector3.up * Mathf.Max(lift + radius, height - radius), radius, hits, ~0, QueryTriggerInteraction.Ignore) > 0;
            }
        }

        static string Rebuild(Pair[] pairs, int index, float cell)
        {
            var was = AssetDatabase.LoadAssetAtPath<Mesh>(pairs[index].old); var now = AssetDatabase.LoadAssetAtPath<Mesh>(pairs[index].@new);
            if (was == null || now == null) return "pair " + index + ": mesh missing";
            string file = Path.Combine(Repo, "Oheangbu", pairs[index].@new); string keep = Path.Combine(Repo, "Tools", "Unity", "Stage308_stream", "Backup", "colliders_c20"); Directory.CreateDirectory(keep);
            string copy = Path.Combine(keep, Path.GetFileName(file)); if (!File.Exists(copy)) File.Copy(file, copy);
            var made = MemoryFix308.Cluster(was, cell); int before = now.vertexCount;
            now.Clear(); now.indexFormat = made.indexFormat; now.vertices = made.vertices; now.triangles = made.triangles; now.RecalculateBounds();
            Object.DestroyImmediate(made); EditorUtility.SetDirty(now); AssetDatabase.SaveAssetIfDirty(now);
            return "rebuilt " + pairs[index].@new + " on " + F(cell) + " m: vertices " + before + " -> " + now.vertexCount + " triangles " + now.triangles.Length / 3 + " (first copy of the former file: " + copy + ")";
        }

        static string Probe(Pair[] pairs, int index, float grid, Mesh candidate, string suffix)
        {
            if (index < 0 || index >= pairs.Length) return "no pair " + index;
            var was = AssetDatabase.LoadAssetAtPath<Mesh>(pairs[index].old); var now = AssetDatabase.LoadAssetAtPath<Mesh>(pairs[index].@new);
            if (was == null || now == null) return "pair " + index + ": mesh missing (old " + (was != null) + " new " + (now != null) + ")";
            if (pairs[index].v0 == pairs[index].v1) return "pair " + index + " " + Path.GetFileName(pairs[index].old) + " | SAME vertex count " + pairs[index].v0 + " -> not clustered, not probed";
            var users = Users(now); if (candidate != null) now = candidate;
            var like = users.Count > 0 ? users[0].transform : null;
            // the player's body, from the scene when it is open
            float step = .3f, radius = .3f, height = 1.8f, slope = 45; string body = "defaults";
            var controller = Object.FindObjectsByType<Oheangbu.App.World.WorldMacroCombatWalker>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(w => w.Body).FirstOrDefault(b => b != null);
            if (controller != null) { step = controller.stepOffset; radius = controller.radius; height = controller.height; slope = controller.slopeLimit; body = "scene"; }
            float minUp = Mathf.Cos(slope * Mathf.Deg2Rad);
            var r = new Result { index = index, oldPath = pairs[index].old, newPath = pairs[index].@new, uses = users.Count, usedBy = like != null ? users[0].gameObject.scene.name + ":" + users[0].name : "(no user in the open scenes - probed at the origin)", grid = grid, step = step, radius = radius, height = height, slope = slope };
            double t0 = EditorApplication.timeSinceStartup;
            using (var a = new Rig(was, like)) using (var b = new Rig(now, like))
            {
                Bounds box = a.Collider.bounds; box.Encapsulate(b.Collider.bounds);
                // at most 1.5 million columns: a wider mesh is probed on a coarser grid (the figure is in the answer)
                grid = Mathf.Max(grid, Mathf.Sqrt(box.size.x * box.size.z / 1.5e6f)); r.grid = grid;
                int nx = Mathf.CeilToInt(box.size.x / grid) + 1, nz = Mathf.CeilToInt(box.size.z / grid) + 1;
                float top = box.max.y + 1, bottom = box.min.y - 1;
                var oldLayers = new List<RaycastHit>(); var newLayers = new List<RaycastHit>();
                // walkable old heights of every column, kept for the neighbour (step) test
                var floors = new Dictionary<long, float[]>(); var newFloors = new Dictionary<long, float[]>();
                var diffs = new List<float>();
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        float x = box.min.x + ix * grid, z = box.min.z + iz * grid;
                        a.Layers(x, z, top, bottom, oldLayers); if (oldLayers.Count == 0) continue;
                        b.Layers(x, z, top, bottom, newLayers); r.columns++;
                        var mine = oldLayers.Where(h => h.normal.y >= minUp).Select(h => h.point.y).ToArray();
                        if (mine.Length == 0) continue;
                        long key = (long)ix << 32 | (uint)iz; floors[key] = mine; newFloors[key] = newLayers.Select(h => h.point.y).ToArray();
                        foreach (float y in mine)
                        {
                            r.walkable++;
                            float best = float.PositiveInfinity, at = y; foreach (var h in newLayers) if (Mathf.Abs(h.point.y - y) < best) { best = Mathf.Abs(h.point.y - y); at = h.point.y; }
                            if (best > 1) { r.missing++; if (r.worst.Count < 400) r.worst.Add(new Spot { x = x, y = y, z = z, was = y, now = float.NaN }); continue; }
                            diffs.Add(best); if (best > .15f) { r.over15++; if (r.worst.Count < 400) r.worst.Add(new Spot { x = x, y = y, z = z, was = y, now = at }); }
                            // standing room and walls, on a coarser lattice (every second column both ways)
                            if ((ix & 1) != 0 || (iz & 1) != 0) continue;
                            var feet = new Vector3(x, y, z);
                            bool free = !a.Blocked(feet, step + .05f, radius, height);
                            if (free)
                            {
                                bool roomy = true;
                                foreach (var d in Dirs) roomy &= !a.Blocked(feet + d * grid, step + .05f, radius, height);
                                if (roomy)
                                {
                                    r.standFree++;
                                    if (b.Blocked(feet, step + .05f, radius, height)) { r.standBlocked++; if (r.blocked.Count < 200) r.blocked.Add(new Spot { x = x, y = y, z = z }); }
                                }
                                foreach (var d in Dirs)
                                {
                                    var eye = feet + Vector3.up * .9f;
                                    if (!a.Physics.Raycast(eye, d, out var wall, 1f, ~0, QueryTriggerInteraction.Ignore)) continue;
                                    r.wallRays++;
                                    if (!b.Physics.Raycast(eye, d, 1.5f, ~0, QueryTriggerInteraction.Ignore)) { r.wallOpened++; if (r.opened.Count < 200) r.opened.Add(new Spot { x = x, y = y + .9f, z = z, was = wall.distance }); }
                                }
                            }
                        }
                    }
                // steps: two neighbouring columns the old mesh let the player cross (height difference within the step height)
                foreach (var kv in floors)
                {
                    int ix = (int)(kv.Key >> 32), iz = (int)(kv.Key & 0xffffffff);
                    for (int n = 0; n < 2; n++)
                    {
                        long other = n == 0 ? (long)(ix + 1) << 32 | (uint)iz : (long)ix << 32 | (uint)(iz + 1);
                        if (!floors.TryGetValue(other, out var theirs)) continue;
                        foreach (float ya in kv.Value) foreach (float yb in theirs)
                        {
                            if (Mathf.Abs(ya - yb) > step) continue;
                            r.stepPairs++;
                            float na = Nearest(newFloors[kv.Key], ya), nb = Nearest(newFloors[other], yb);
                            if (float.IsNaN(na) || float.IsNaN(nb)) continue;   // counted as missing above
                            if (Mathf.Abs(na - nb) > step + .02f) { r.stepBroken++; if (r.steps.Count < 400) r.steps.Add(new Spot { x = box.min.x + ix * grid, y = ya, z = box.min.z + iz * grid, was = Mathf.Abs(ya - yb), now = Mathf.Abs(na - nb) }); }
                        }
                    }
                }
                diffs.Sort();
                r.maxDiff = diffs.Count > 0 ? diffs[diffs.Count - 1] : 0; r.p95Diff = diffs.Count > 0 ? diffs[(int)(diffs.Count * .95f)] : 0;
            }
            r.seconds = (float)(EditorApplication.timeSinceStartup - t0);
            Directory.CreateDirectory(OutDir); File.WriteAllText(Path.Combine(OutDir, index + suffix + ".json"), JsonUtility.ToJson(r, true));
            return "pair " + index + " " + Path.GetFileName(r.oldPath) + " | grid " + F(grid) + " | body " + body + " step " + F(step) + " r " + F(radius) + " h " + F(height) + " slope " + F(slope, "F0") + " | " + Line(r);
        }

        static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        static float Nearest(float[] ys, float y) { float best = float.PositiveInfinity, at = float.NaN; foreach (float v in ys) if (Mathf.Abs(v - y) < best) { best = Mathf.Abs(v - y); at = v; } return best > 1 ? float.NaN : at; }

        static string Line(Result r) =>
            "walkable cells " + r.walkable + " | height diff max " + F(r.maxDiff) + " p95 " + F(r.p95Diff) + " | >0.15 m " + Pct(r.over15, r.walkable) + " | gone (>1 m) " + Pct(r.missing, r.walkable)
            + " | steps broken " + r.stepBroken + " of " + r.stepPairs + " | standing room lost " + r.standBlocked + " of " + r.standFree + " | walls gone " + r.wallOpened + " of " + r.wallRays + " | " + F(r.seconds, "F0") + " s";

        static string Pct(int n, int of) => n + " (" + (of > 0 ? (100f * n / of).ToString("F2", CultureInfo.InvariantCulture) : "0") + " %)";

        static string Report(Pair[] pairs)
        {
            var sb = new StringBuilder("ColliderCheck308 report\n");
            for (int i = 0; i < pairs.Length; i++)
            {
                string file = Path.Combine(OutDir, i + ".json");
                sb.AppendLine(i.ToString().PadLeft(3) + " " + Path.GetFileNameWithoutExtension(pairs[i].old).PadRight(46) + " " + (File.Exists(file) ? Line(JsonUtility.FromJson<Result>(File.ReadAllText(file))) : "not probed"));
            }
            return sb.ToString();
        }
    }
}

