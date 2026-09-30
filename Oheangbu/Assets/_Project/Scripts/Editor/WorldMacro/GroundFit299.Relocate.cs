using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #299: move a house that sits on a slope to level ground instead of propping it up.
    //   children:<path>                         — descendants with their gameplay components (what moves with the house)
    //   flat-search:<path>:<radius>[:<step>]    — level, unobstructed spots for the object's footprint near where it is:
    //                                             terrain height range over the rotated footprint, clearance of other colliders
    //   relocate:<path>:<x>,<z>[:save]          — move the object so its root sits on the terrain at (x, z); yaw kept
    public static partial class GroundFit299
    {
        static string Children(string path)
        {
            var root = FindPath(SceneManager.GetActiveScene(), path) ?? throw new Exception("no " + path);
            var sb = new StringBuilder();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var kinds = t.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer))
                    .Select(c => c.GetType().Name).ToArray();
                if (kinds.Length == 0 && t != root) continue;
                sb.AppendLine($"{PathOf(t)} @ {t.position.ToString("F1")} [{string.Join(", ", kinds)}]{(t.gameObject.activeInHierarchy ? "" : " (inactive)")}");
            }
            return sb.ToString();
        }

        // footprint of an object in its own yaw frame: XZ extents of its renderers around its position
        static void Footprint(Transform root, out Vector2 half, out Vector2 centreOffset, out float yaw)
        {
            yaw = root.eulerAngles.y;
            var inv = Quaternion.Euler(0, -yaw, 0);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r.name == "StoneSkirt299") continue;
                var b = r.bounds;
                foreach (var cx in new[] { b.min.x, b.max.x }) foreach (var cz in new[] { b.min.z, b.max.z })
                {
                    var l = inv * (new Vector3(cx, 0, cz) - new Vector3(root.position.x, 0, root.position.z));
                    minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x); minZ = Mathf.Min(minZ, l.z); maxZ = Mathf.Max(maxZ, l.z);
                }
            }
            half = new Vector2((maxX - minX) * .5f, (maxZ - minZ) * .5f);
            centreOffset = new Vector2((maxX + minX) * .5f, (maxZ + minZ) * .5f);
        }

        static string FlatSearch(string argument)
        {
            var a = argument.Split(':');
            var root = FindPath(SceneManager.GetActiveScene(), a[0]) ?? throw new Exception("no " + a[0]);
            float radius = float.Parse(a[1], CultureInfo.InvariantCulture), step = a.Length > 2 ? float.Parse(a[2], CultureInfo.InvariantCulture) : 3f;
            // optional search centre (default: where the object stands)
            Vector3? centreOverride = null;
            if (a.Length > 3) { var cz = a[3].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray(); centreOverride = new Vector3(cz[0], root.position.y, cz[1]); }
            Footprint(root, out var half, out var off, out float yaw);
            var rot = Quaternion.Euler(0, yaw, 0);
            var own = new HashSet<Collider>(root.GetComponentsInChildren<Collider>(true));
            var hits = new RaycastHit[64];
            var overlaps = new Collider[64];
            Physics.SyncTransforms();
            var found = new List<(float range, float dist, Vector3 at, string blocker)>();
            var origin = centreOverride ?? root.position;
            // baked trees and shrubs (no colliders) must not stand inside the footprint either
            var trunks = new List<(Vector3 p, string id)>();
            foreach (var sheet in SceneSheets())
            {
                var tall = new HashSet<string>(sheet.Prototypes.Where(x => x != null && (x.Category == Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Tree || x.Category == Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Shrub)).Select(x => x.Id));
                foreach (var f in sheet.FixedPlacements)
                    if (tall.Contains(f.PrototypeId) && new Vector2(f.Position.x - origin.x, f.Position.z - origin.z).magnitude < radius + 30f) trunks.Add((f.Position, f.PrototypeId));
            }
            // the footprint plus a 1.5 m apron has to be level
            var pad = half + new Vector2(1.5f, 1.5f);
            for (float x = -radius; x <= radius; x += step)
                for (float z = -radius; z <= radius; z += step)
                {
                    var c = origin + new Vector3(x, 0, z);
                    float lo = float.MaxValue, hi = float.MinValue; bool ok = true;
                    for (float u = -pad.x; u <= pad.x + .01f && ok; u += 1f)
                        for (float w = -pad.y; w <= pad.y + .01f; w += 1f)
                        {
                            var p = c + rot * new Vector3(off.x + u, 0, off.y + w);
                            p.y = origin.y;
                            float y = TerrainY(p, hits);
                            if (float.IsNaN(y)) { ok = false; break; }
                            lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                        }
                    if (!ok) continue;
                    float range = hi - lo;
                    if (range > .8f) continue;
                    // nothing else (houses, fences, rocks, trees with colliders) inside the footprint volume above the ground
                    var centre = c + rot * new Vector3(off.x, 0, off.y); centre.y = hi + 2.2f;
                    int n = Physics.OverlapBoxNonAlloc(centre, new Vector3(pad.x, 2f, pad.y), overlaps, rot, ~0, QueryTriggerInteraction.Ignore);
                    string blocker = null;
                    for (int i = 0; i < n; i++)
                        if (!own.Contains(overlaps[i]) && overlaps[i].transform.root.name != "Reworld292_Terrain") { blocker = PathOf(overlaps[i].transform); break; }
                    if (blocker == null)
                    {
                        var inv = Quaternion.Inverse(rot); var cxz = c + rot * new Vector3(off.x, 0, off.y);
                        foreach (var tk in trunks)
                        {
                            var l = inv * new Vector3(tk.p.x - cxz.x, 0, tk.p.z - cxz.z);
                            if (Mathf.Abs(l.x) < half.x + .8f && Mathf.Abs(l.z) < half.y + .8f) { blocker = "tree " + tk.id; break; }   // trunk clear of the walls/eaves
                        }
                    }
                    found.Add((range, new Vector2(x, z).magnitude, new Vector3(c.x, lo, c.z), blocker));
                }
            var sb = new StringBuilder();
            sb.AppendLine($"footprint {half.x * 2:F1} x {half.y * 2:F1} m (+1.5 m apron), yaw {yaw:F0}, from {origin.ToString("F1")}");
            foreach (var f in found.Where(f => f.blocker == null).OrderBy(f => f.range * 4 + f.dist / 20f).Take(12))
                sb.AppendLine($"  clear  range {f.range:F2} m  dist {f.dist,5:F1} m  at ({f.at.x:F1}, {f.at.z:F1}) ground {f.at.y:F2}");
            foreach (var f in found.Where(f => f.blocker != null).OrderBy(f => f.range).Take(4))
                sb.AppendLine($"  blocked range {f.range:F2} m  dist {f.dist,5:F1} m  at ({f.at.x:F1}, {f.at.z:F1}) by {f.blocker}");
            return sb.ToString();
        }

        // relocate-near:<parent path>:<cx>,<cz>:<radius>:<dx>,<dz>[:exclude=<child>[,<child>]][:save]
        // moves the parent's direct children standing within radius of (cx, cz) by (dx, dz), each re-grounded by the terrain
        // difference under it; reports anything that now overlaps other colliders or a baked tree trunk
        static string RelocateNear(string argument)
        {
            bool save = argument.EndsWith(":save", StringComparison.Ordinal);
            if (save) argument = argument.Substring(0, argument.Length - 5);
            var exclude = new HashSet<string>();
            int ex = argument.IndexOf(":exclude=", StringComparison.Ordinal);
            if (ex >= 0) { foreach (var s in argument.Substring(ex + 9).Split(',')) exclude.Add(s); argument = argument.Substring(0, ex); }
            var a = argument.Split(':');
            var scene = SceneManager.GetActiveScene();
            var parent = FindPath(scene, a[0]) ?? throw new Exception("no " + a[0]);
            var c = a[1].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            float radius = float.Parse(a[2], CultureInfo.InvariantCulture);
            var d = a[3].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            var hits = new RaycastHit[64]; var overlaps = new Collider[32];
            Physics.SyncTransforms();
            var trunks = new List<Vector3>();
            foreach (var sheet in SceneSheets())
            {
                var tall = new HashSet<string>(sheet.Prototypes.Where(x => x != null && x.Category == Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Tree).Select(x => x.Id));
                foreach (var f in sheet.FixedPlacements) if (tall.Contains(f.PrototypeId) && new Vector2(f.Position.x - c[0] - d[0], f.Position.z - c[1] - d[1]).magnitude < radius + 20f) trunks.Add(f.Position);
            }
            var moved = new List<Transform>();
            foreach (Transform child in parent)
            {
                if (exclude.Contains(child.name)) continue;
                if (new Vector2(child.position.x - c[0], child.position.z - c[1]).magnitude > radius) continue;
                float g0 = TerrainY(child.position, hits);
                var target = child.position + new Vector3(d[0], 0, d[1]);
                float g1 = TerrainY(target, hits);
                target.y += (float.IsNaN(g0) || float.IsNaN(g1)) ? 0f : g1 - g0;
                child.position = target; moved.Add(child); EditorUtility.SetDirty(child.gameObject);
            }
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            sb.AppendLine($"moved {moved.Count} children of {a[0]} by ({d[0]}, {d[1]}) and re-grounded");
            var own = new HashSet<Collider>(moved.SelectMany(m => m.GetComponentsInChildren<Collider>(true)));
            foreach (var m in moved)
            {
                string conflict = null;
                foreach (var col in m.GetComponentsInChildren<Collider>(false))
                {
                    var b = col.bounds;
                    int n = Physics.OverlapBoxNonAlloc(b.center, b.extents * .95f, overlaps, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n && conflict == null; i++)
                    {
                        var o = overlaps[i];
                        if (own.Contains(o) || o.transform.root.name == "Reworld292_Terrain") continue;
                        if (o.transform.IsChildOf(parent) && moved.Any(x => o.transform.IsChildOf(x))) continue;
                        conflict = "overlaps " + PathOf(o.transform);
                    }
                    if (conflict == null) foreach (var tk in trunks) if (Mathf.Abs(tk.x - b.center.x) < b.extents.x + .35f && Mathf.Abs(tk.z - b.center.z) < b.extents.z + .35f) { conflict = "tree trunk at " + tk.ToString("F1"); break; }
                    if (conflict != null) break;
                }
                if (conflict != null) sb.AppendLine($"  CONFLICT {m.name} @ {m.position.ToString("F1")}: {conflict}");
            }
            if (save && scene.isDirty) SaveWithBackup(scene, sb);
            return sb.ToString();
        }

        static string Relocate(string argument)
        {
            bool save = argument.EndsWith(":save", StringComparison.Ordinal);
            if (save) argument = argument.Substring(0, argument.Length - 5);
            var a = argument.Split(':');
            var scene = SceneManager.GetActiveScene();
            var root = FindPath(scene, a[0]) ?? throw new Exception("no " + a[0]);
            var xz = a[1].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            var hits = new RaycastHit[64];
            Physics.SyncTransforms();
            // the root sits where it did relative to the ground under it; the new ground is sampled over the footprint
            Footprint(root, out var half, out var off, out float yaw);
            var rot = Quaternion.Euler(0, yaw, 0);
            float oldGround = TerrainY(root.position, hits);
            var target = new Vector3(xz[0], root.position.y, xz[1]);
            float lo = float.MaxValue, hi = float.MinValue;
            for (float u = -half.x; u <= half.x + .01f; u += 1f)
                for (float w = -half.y; w <= half.y + .01f; w += 1f)
                {
                    var p = target + rot * new Vector3(off.x + u, 0, off.y + w);
                    float y = TerrainY(p, hits); if (float.IsNaN(y)) continue;
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                }
            if (lo == float.MaxValue) throw new Exception("no terrain at the target");
            // on level ground the root belongs on the ground: take the highest ground in the footprint so nothing floats
            float lift = float.IsNaN(oldGround) ? 0f : root.position.y - oldGround;
            float newY = hi + Mathf.Clamp(lift, -.2f, .2f);
            var before = root.position;
            root.position = new Vector3(xz[0], newY, xz[1]);
            var skirt = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "StoneSkirt299").ToArray();
            foreach (var s in skirt) UnityEngine.Object.DestroyImmediate(s.gameObject);
            EditorUtility.SetDirty(root.gameObject);
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            sb.AppendLine($"{a[0]}: {before.ToString("F2")} -> {root.position.ToString("F2")} (ground range {hi - lo:F2} m, old root-ground offset {lift:F2} m, removed {skirt.Length} stone skirt)");
            if (save && scene.isDirty) SaveWithBackup(scene, sb);
            return sb.ToString();
        }
    }
}
