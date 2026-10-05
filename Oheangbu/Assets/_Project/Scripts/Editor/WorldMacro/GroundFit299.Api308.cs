using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 (SPEC-WORLD-BUILDING-AUDIT-308 §D): structured results of the #299 house tools for BuildingFix308, without saving.
    // The existing GroundFit299 files stay as they are; this partial reuses their private helpers (Footprint, TerrainY,
    // SceneSheets, AddStone) and returns data instead of text. Differences from the string commands, on purpose:
    //   FlatSearch308   — same footprint/apron/terrain-range/overlap/trunk rules as flat-search, every spot returned (clear and
    //                     blocked) so BuildingFix308 can apply its own TEST filters (range .5 m, corridors, building gap).
    //   RelocateTarget  — the relocate height rule (highest ground in the footprint + the old root-ground offset clamped to ±.2 m)
    //                     computed without moving anything.
    //   StoneSkirt308   — the stone-skirt mesh for one plate, saved with SaveAssetIfDirty only (never AssetDatabase.SaveAssets,
    //                     which would flush other sessions' dirty assets), under a holder named StoneSkirt308.
    public static partial class GroundFit299
    {
        internal sealed class Spot308 { public Vector3 at; public float range, dist; public string blocker; }

        internal static void Footprint308(Transform root, out Vector2 half, out Vector2 centreOffset, out float yaw) => Footprint(root, out half, out centreOffset, out yaw);
        internal static float TerrainY308(Vector3 p) => TerrainY(p, new RaycastHit[64]);

        internal static List<string> Children308(Transform root)
        {
            var list = new List<string>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var kinds = t.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)).Select(c => c.GetType().Name).ToArray();
                if (kinds.Length == 0 && t != root) continue;
                list.Add(PathOf(t) + " @ " + t.position.ToString("F1") + " [" + string.Join(", ", kinds) + "]" + (t.gameObject.activeInHierarchy ? "" : " (inactive)"));
            }
            return list;
        }

        internal static List<Spot308> FlatSearch308(Transform root, float radius, float step, Vector3? centreOverride)
        {
            Footprint(root, out var half, out var off, out float yaw);
            var rot = Quaternion.Euler(0, yaw, 0);
            var own = new HashSet<Collider>(root.GetComponentsInChildren<Collider>(true));
            var hits = new RaycastHit[64]; var overlaps = new Collider[64];
            Physics.SyncTransforms();
            var found = new List<Spot308>();
            var origin = centreOverride ?? root.position;
            var trunks = new List<(Vector3 p, string id)>();
            foreach (var sheet in SceneSheets())
            {
                var tall = new HashSet<string>(sheet.Prototypes.Where(x => x != null && (x.Category == WorldMacroDressingSheetSO.Kind.Tree || x.Category == WorldMacroDressingSheetSO.Kind.Shrub)).Select(x => x.Id));
                foreach (var f in sheet.FixedPlacements)
                    if (tall.Contains(f.PrototypeId) && new Vector2(f.Position.x - origin.x, f.Position.z - origin.z).magnitude < radius + 30f) trunks.Add((f.Position, f.PrototypeId));
            }
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
                    if (range > .8f) continue;   // the #299 search limit; BuildingFix308 narrows it (config)
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
                            if (Mathf.Abs(l.x) < half.x + .8f && Mathf.Abs(l.z) < half.y + .8f) { blocker = "tree " + tk.id; break; }
                        }
                    }
                    found.Add(new Spot308 { range = range, dist = new Vector2(x, z).magnitude, at = new Vector3(c.x, lo, c.z), blocker = blocker });
                }
            return found;
        }

        // where Relocate would put the root at (x, z): highest ground in the footprint + the old root-ground offset clamped to ±.2 m
        internal static Vector3 RelocateTarget308(Transform root, float x, float z, out float range, out float lift)
        {
            var hits = new RaycastHit[64];
            Physics.SyncTransforms();
            Footprint(root, out var half, out var off, out float yaw);
            var rot = Quaternion.Euler(0, yaw, 0);
            float oldGround = TerrainY(root.position, hits);
            var target = new Vector3(x, root.position.y, z);
            float lo = float.MaxValue, hi = float.MinValue;
            for (float u = -half.x; u <= half.x + .01f; u += 1f)
                for (float w = -half.y; w <= half.y + .01f; w += 1f)
                {
                    var p = target + rot * new Vector3(off.x + u, 0, off.y + w);
                    float y = TerrainY(p, hits); if (float.IsNaN(y)) continue;
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                }
            if (lo == float.MaxValue) { range = float.NaN; lift = float.NaN; return new Vector3(float.NaN, float.NaN, float.NaN); }
            lift = float.IsNaN(oldGround) ? 0f : root.position.y - oldGround;
            range = hi - lo;
            return new Vector3(x, hi + Mathf.Clamp(lift, -.2f, .2f), z);
        }

        // stone skirt for one plate (the #299 dry-laid stone rule), visual only, saved with SaveAssetIfDirty; returns the mesh path or
        // null with a note when the plate sits on the ground all round
        internal static string StoneSkirt308(Transform plate, string materialPath, out string note)
        {
            note = "";
            var hits = new RaycastHit[64];
            Physics.SyncTransforms();
            if (!AssetDatabase.IsValidFolder(SkirtFolder)) { note = "skirt folder missing " + SkirtFolder; return null; }
            var material = AssetDatabase.LoadAssetAtPath<Material>(string.IsNullOrEmpty(materialPath) ? StoneMaterial : materialPath);
            if (material == null) { note = "no stone material"; return null; }
            var pf = plate != null ? plate.GetComponent<MeshFilter>() : null;
            if (pf == null || pf.sharedMesh == null || plate.parent == null) { note = "plate has no mesh"; return null; }
            string key = PathOf(plate).Replace('/', '_');
            string meshPath = SkirtFolder + "/" + key.Substring(Math.Max(0, key.Length - 90)) + "_skirt308.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                var lb = pf.sharedMesh.bounds;
                Vector3 W(float x, float z) => plate.TransformPoint(new Vector3(x, lb.max.y, z));
                var corners = new[] { W(lb.min.x, lb.min.z), W(lb.max.x, lb.min.z), W(lb.max.x, lb.max.z), W(lb.min.x, lb.max.z) };
                var centre = (corners[0] + corners[2]) * .5f;
                var rng = new System.Random(StableKey(key));
                var plateColliders = plate.GetComponents<Collider>();
                float PlateTop(Vector3 edgePoint, Vector3 inward, out bool ok)
                {
                    foreach (float inset in new[] { .12f, .3f, .6f, 1f, 1.6f })
                    {
                        var o = edgePoint + inward * inset; var ray = new Ray(new Vector3(o.x, o.y + 6f, o.z), Vector3.down);
                        foreach (var col in plateColliders)
                            if (col.Raycast(ray, out var ph, 30f) && ph.normal.y > .7f) { ok = true; return ph.point.y; }
                    }
                    ok = false; return float.NaN;
                }
                var v = new List<Vector3>(); var nn = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
                int stones = 0; float maxGap = 0;
                for (int e = 0; e < 4; e++)
                {
                    Vector3 c0 = corners[e], c1 = corners[(e + 1) % 4];
                    var along = c1 - c0; along.y = 0; float L = along.magnitude; if (L < .5f) continue; along /= L;
                    var outward = Vector3.Cross(Vector3.up, along); if (Vector3.Dot(outward, (c0 + c1) * .5f - centre) < 0) outward = -outward;
                    int ns = Mathf.Max(2, Mathf.CeilToInt(L / .25f) + 1);
                    var top = new float[ns]; var foot = new float[ns];
                    for (int i = 0; i < ns; i++)
                    {
                        var p = Vector3.Lerp(c0, c1, i / (float)(ns - 1));
                        float pt = PlateTop(p, -outward, out bool found);
                        float g = TerrainY(p + outward * .2f, hits);
                        if (!found || float.IsNaN(g)) { top[i] = float.NaN; foot[i] = float.NaN; continue; }
                        top[i] = pt; foot[i] = g; maxGap = Mathf.Max(maxGap, top[i] - foot[i]);
                    }
                    int firstOk = Array.FindIndex(top, x => !float.IsNaN(x));
                    for (int i = 0; i < ns && firstOk >= 0; i++)
                    {
                        if (!float.IsNaN(top[i])) continue;
                        int l = i - 1; while (l >= 0 && float.IsNaN(top[l])) l--;
                        int r = i + 1; while (r < ns && float.IsNaN(top[r])) r++;
                        float tl = l >= 0 ? top[l] : top[r], tr2 = r < ns ? top[r] : top[l];
                        float f = (l >= 0 && r < ns) ? (i - l) / (float)(r - l) : 0f;
                        top[i] = Mathf.Lerp(tl, tr2, f);
                        var p = Vector3.Lerp(c0, c1, i / (float)(ns - 1));
                        float g = TerrainY(p + outward * .2f, hits); foot[i] = float.IsNaN(g) ? top[i] : g;
                        maxGap = Mathf.Max(maxGap, top[i] - foot[i]);
                    }
                    for (int i = 0; i < ns; i++) if (float.IsNaN(top[i])) { top[i] = 0f; foot[i] = 0f; }
                    bool any = false; for (int i = 0; i < ns; i++) if (top[i] != 0f || foot[i] != 0f) any = true;
                    if (!any) continue;
                    float Sample(float[] arr, float s) { float f = Mathf.Clamp01(s / L) * (ns - 1); int i = Mathf.Min(ns - 2, (int)f); return Mathf.Lerp(arr[i], arr[i + 1], f - i); }
                    float lowest = float.MaxValue, highest = float.MinValue;
                    for (int i = 0; i < ns; i++) if (top[i] != 0f || foot[i] != 0f) { lowest = Mathf.Min(lowest, foot[i] - .2f); highest = Mathf.Max(highest, top[i]); }
                    float y = lowest;
                    while (y < highest - .04f)
                    {
                        float h = .20f + (float)rng.NextDouble() * .22f;
                        float s = -(float)rng.NextDouble() * .5f;
                        while (s < L)
                        {
                            float len = .38f + (float)rng.NextDouble() * .55f;
                            float s0 = Mathf.Max(0f, s), s1 = Mathf.Min(L, s + len); s += len;
                            if (s1 - s0 < .18f) continue;
                            float mid = (s0 + s1) * .5f;
                            if (Sample(top, s0) == 0f || Sample(top, s1) == 0f) continue;
                            float bandTop = Mathf.Min(Sample(top, s0), Sample(top, s1), Sample(top, mid)) - .025f;
                            float bandFoot = Mathf.Max(Sample(foot, s0), Sample(foot, s1)) - .2f;
                            if (bandTop - Mathf.Min(Sample(foot, s0), Sample(foot, s1)) < .08f) continue;
                            float y0 = Mathf.Max(y, bandFoot), y1 = Mathf.Min(y + h, bandTop);
                            if (y1 - y0 < .07f) continue;
                            float sh = (y1 - y0) * (.95f + (float)rng.NextDouble() * .2f), sl = s1 - s0;
                            float depth = .34f + (float)rng.NextDouble() * .2f;
                            var basePoint = c0 + along * mid; basePoint.y = (y0 + y1) * .5f + ((float)rng.NextDouble() - .5f) * .06f;
                            basePoint.y = Mathf.Min(basePoint.y, bandTop - sh * .5f);
                            var sc = basePoint + outward * (.01f + (float)rng.NextDouble() * .06f - depth * .5f);
                            AddStone(v, nn, uv, tris, sc, along, outward, sl + .05f, sh + .04f, depth, rng, s0 + e * 13.7f);
                            stones++;
                        }
                        y += h;
                    }
                }
                if (stones == 0) { note = "plate edge within 8 cm of the ground all round (max gap " + maxGap.ToString("F2") + " m)"; return null; }
                var parent = plate.parent;
                for (int i = 0; i < v.Count; i++) v[i] = parent.InverseTransformPoint(v[i]);
                mesh = new Mesh { name = key + "_skirt308" };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
                AssetDatabase.SaveAssetIfDirty(mesh);
                note = "NEW " + stones + " stones, max gap " + maxGap.ToString("F2") + " m";
            }
            else note = "REUSE " + meshPath;
            var holder = plate.parent.Find("StoneSkirt308");
            if (holder == null)
            {
                var go = new GameObject("StoneSkirt308");
                go.transform.SetParent(plate.parent, false);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                holder = go.transform;
            }
            holder.localPosition = Vector3.zero; holder.localRotation = Quaternion.identity; holder.localScale = Vector3.one;
            var mf = holder.GetComponent<MeshFilter>(); if (mf == null) mf = holder.gameObject.AddComponent<MeshFilter>();
            var mr = holder.GetComponent<MeshRenderer>(); if (mr == null) mr = holder.gameObject.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh; mr.sharedMaterial = material;
            EditorUtility.SetDirty(holder.gameObject);
            return meshPath;
        }

        // string.GetHashCode is randomised per process on .NET; a stable seed keeps the stones identical in every scene
        static int StableKey(string s) { unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h; } }
    }
}
