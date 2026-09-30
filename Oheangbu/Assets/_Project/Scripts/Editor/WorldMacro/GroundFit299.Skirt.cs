using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #299 stage 2: house plates (ThatchedInn h1_house_ground) that stand out over a slope are finished with dry-laid
    // irregular stones (막돌 쌓기) from the ground up to the plate top, so the plate reads as a stone terrace instead of a
    // floating slab. The plate itself is untouched (level floor under the house, same collider, same NavMesh).
    //   stone-skirt:<plate path>[|<plate path>...][:save]
    // Per plate: a child "StoneSkirt299" under the plate's parent, visual only (no collider — NavMesh bake deferred by the
    // user), one combined mesh saved once to Finish297/GroundFit299/Skirts/<name>_skirt299.asset and reused by the other
    // working scenes. Material: the ThatchedInn stone plinth material (house_Re_Ground_02_Muted).
    public static partial class GroundFit299
    {
        const string SkirtFolder = CopyFolder + "/Skirts";
        // the ThatchedInn stone plinth (house_Re_Ground_02), so the terrace reads as the same masonry as the house base
        const string StoneMaterial = "Assets/_Project/Art/World/Architecture296/Materials/32062da856c47b746bdffc079c10d9cf_house_Re_Ground_02_Muted.mat";

        // one irregular stone: a rounded box (superellipsoid) with jittered proportions, the outer face bulging a little
        static void AddStone(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 centre, Vector3 along, Vector3 outward,
                             float length, float height, float depth, System.Random rng, float uOffset)
        {
            const int seg = 8, ring = 6;
            var up = Vector3.up;
            float jitterYaw = ((float)rng.NextDouble() - .5f) * 8f, jitterRoll = ((float)rng.NextDouble() - .5f) * 6f;
            var q = Quaternion.AngleAxis(jitterYaw, up) * Quaternion.AngleAxis(jitterRoll, outward);
            Vector3 a = q * along, o = q * outward, u = q * up;
            int start = v.Count;
            float squareness = .45f + (float)rng.NextDouble() * .25f;          // lower = boxier; fieldstone is rounded
            var corner = new float[8]; for (int i = 0; i < 8; i++) corner[i] = .80f + (float)rng.NextDouble() * .28f;
            for (int r = 0; r <= ring; r++)
            {
                float phi = Mathf.PI * r / ring - Mathf.PI / 2;               // -90..90 (bottom..top)
                for (int s = 0; s <= seg; s++)
                {
                    float th = 2 * Mathf.PI * s / seg;
                    float cx = Mathf.Cos(phi) * Mathf.Cos(th), cy = Mathf.Sin(phi), cz = Mathf.Cos(phi) * Mathf.Sin(th);
                    float px = Mathf.Sign(cx) * Mathf.Pow(Mathf.Abs(cx), squareness), py = Mathf.Sign(cy) * Mathf.Pow(Mathf.Abs(cy), squareness), pz = Mathf.Sign(cz) * Mathf.Pow(Mathf.Abs(cz), squareness);
                    int ci = (px > 0 ? 1 : 0) | (py > 0 ? 2 : 0) | (pz > 0 ? 4 : 0);
                    float k = corner[ci];
                    float bulge = pz > .5f ? 1f + .18f * (pz - .5f) / .5f : 1f;  // outer face (local +z) swells
                    var p = centre + a * (px * length * .5f * k) + u * (py * height * .5f * k) + o * (pz * depth * .5f * k * bulge);
                    v.Add(p); n.Add(Vector3.zero);
                    uv.Add(new Vector2(uOffset + px * length * .5f, p.y) * .5f);
                }
            }
            for (int r = 0; r < ring; r++)
                for (int s = 0; s < seg; s++)
                {
                    int i0 = start + r * (seg + 1) + s, i1 = i0 + 1, i2 = i0 + seg + 1, i3 = i2 + 1;
                    t.AddRange(new[] { i0, i2, i1, i1, i2, i3 });
                }
        }

        static string StoneSkirt(string argument)
        {
            bool save = argument.EndsWith(":save", StringComparison.Ordinal);
            if (save) argument = argument.Substring(0, argument.Length - 5);
            string materialPath = StoneMaterial;
            int mi = argument.IndexOf(":mat=", StringComparison.Ordinal);
            if (mi >= 0) { materialPath = argument.Substring(mi + 5); argument = argument.Substring(0, mi); }
            var scene = SceneManager.GetActiveScene();
            var hits = new RaycastHit[64];
            var sb = new StringBuilder();
            Physics.SyncTransforms();
            if (!AssetDatabase.IsValidFolder(SkirtFolder)) { Directory.CreateDirectory(Path.GetFullPath(SkirtFolder)); AssetDatabase.Refresh(); }
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) throw new Exception("no stone material " + materialPath);
            foreach (string path in argument.Split('|').Where(s => s.Length > 0))
            {
                var plate = FindPath(scene, path);
                var pf = plate != null ? plate.GetComponent<MeshFilter>() : null;
                if (pf == null || pf.sharedMesh == null || plate.parent == null) { sb.AppendLine("MISSING " + path); continue; }
                string key = path.Replace('/', '_');
                string meshPath = SkirtFolder + "/" + key.Substring(Math.Max(0, key.Length - 90)) + "_skirt299.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                string note = "REUSE";
                var holder = plate.parent.Find("StoneSkirt299");
                if (mesh == null)
                {
                    // the plate's top outline: its local bounds, at the top height, in world space
                    var lb = pf.sharedMesh.bounds;
                    Vector3 W(float x, float z) => plate.TransformPoint(new Vector3(x, lb.max.y, z));
                    var corners = new[] { W(lb.min.x, lb.min.z), W(lb.max.x, lb.min.z), W(lb.max.x, lb.max.z), W(lb.min.x, lb.max.z) };
                    var centre = (corners[0] + corners[2]) * .5f;
                    var rng = new System.Random(key.GetHashCode());
                    // the plate surface height at the edge comes from the plate's own collider (its mesh bounds also
                    // cover raised parts, so the bounds top is not the floor); step inward until the ray finds it
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
                    int stones = 0; float maxGap = 0; float wallArea = 0;
                    for (int e = 0; e < 4; e++)
                    {
                        Vector3 c0 = corners[e], c1 = corners[(e + 1) % 4];
                        var along = c1 - c0; along.y = 0; float L = along.magnitude; if (L < .5f) continue; along /= L;
                        var outward = Vector3.Cross(Vector3.up, along); if (Vector3.Dot(outward, (c0 + c1) * .5f - centre) < 0) outward = -outward;
                        // sample the band to fill along the edge: ground just outside the edge up to the plate top
                        int ns = Mathf.Max(2, Mathf.CeilToInt(L / .25f) + 1);
                        var top = new float[ns]; var foot = new float[ns];
                        for (int i = 0; i < ns; i++)
                        {
                            var p = Vector3.Lerp(c0, c1, i / (float)(ns - 1));
                            float pt = PlateTop(p, -outward, out bool found);
                            float g = TerrainY(p + outward * .2f, hits);
                            if (!found || float.IsNaN(g)) { top[i] = float.NaN; foot[i] = float.NaN; continue; }
                            top[i] = pt; foot[i] = g;
                            maxGap = Mathf.Max(maxGap, top[i] - foot[i]);
                        }
                        // edge samples where the ray missed the plate take the neighbouring plate height, so the wall runs
                        // on as one retaining face instead of breaking into stacks; only an edge with no plate at all closes
                        int firstOk = Array.FindIndex(top, x => !float.IsNaN(x)), lastOk = Array.FindLastIndex(top, x => !float.IsNaN(x));
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
                        float y = lowest; int course = 0;
                        while (y < highest - .04f)
                        {
                            float h = .20f + (float)rng.NextDouble() * .22f;
                            float s = -(float)rng.NextDouble() * .5f;              // stagger the joints course by course
                            while (s < L)
                            {
                                float len = .38f + (float)rng.NextDouble() * .55f;
                                float s0 = Mathf.Max(0f, s), s1 = Mathf.Min(L, s + len); s += len;
                                if (s1 - s0 < .18f) continue;
                                float mid = (s0 + s1) * .5f;
                                if (Sample(top, s0) == 0f || Sample(top, s1) == 0f) continue;
                                float bandTop = Mathf.Min(Sample(top, s0), Sample(top, s1), Sample(top, mid)) - .025f;
                                float bandFoot = Mathf.Max(Sample(foot, s0), Sample(foot, s1)) - .2f;
                                if (bandTop - Mathf.Min(Sample(foot, s0), Sample(foot, s1)) < .08f) continue;   // plate sits on the ground here
                                float y0 = Mathf.Max(y, bandFoot), y1 = Mathf.Min(y + h, bandTop);
                                if (y1 - y0 < .07f) continue;
                                float sh = (y1 - y0) * (.95f + (float)rng.NextDouble() * .2f), sl = s1 - s0;
                                float depth = .34f + (float)rng.NextDouble() * .2f;
                                var basePoint = c0 + along * mid; basePoint.y = (y0 + y1) * .5f + ((float)rng.NextDouble() - .5f) * .06f;
                                basePoint.y = Mathf.Min(basePoint.y, bandTop - sh * .5f);
                                // the outer face stands 1-7 cm proud of the plate edge; the stone runs back under the plate
                                var sc = basePoint + outward * (.01f + (float)rng.NextDouble() * .06f - depth * .5f);
                                AddStone(v, nn, uv, tris, sc, along, outward, sl + .05f, sh + .04f, depth, rng, s0 + e * 13.7f);
                                stones++; wallArea += sl * sh;
                            }
                            y += h; course++;
                        }
                    }
                    if (stones == 0) { sb.AppendLine($"SKIP {path}: plate edge within 8 cm of the ground all round (max gap {maxGap:F2} m)"); continue; }
                    // mesh in the holder's local space (holder sits at the plate parent's origin)
                    var parent = plate.parent;
                    for (int i = 0; i < v.Count; i++) v[i] = parent.InverseTransformPoint(v[i]);
                    mesh = new Mesh { name = key + "_skirt299" };
                    if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
                    mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh, meshPath);
                    note = $"NEW {stones} stones, wall {wallArea:F1} m2, max gap {maxGap:F2} m, {v.Count} vertices";
                }
                if (holder == null)
                {
                    var go = new GameObject("StoneSkirt299");
                    go.transform.SetParent(plate.parent, false);
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                    holder = go.transform;
                }
                holder.localPosition = Vector3.zero; holder.localRotation = Quaternion.identity; holder.localScale = Vector3.one;
                // Unity's fake-null components defeat ??, so check explicitly
                var mf = holder.GetComponent<MeshFilter>(); if (mf == null) mf = holder.gameObject.AddComponent<MeshFilter>();
                var mr = holder.GetComponent<MeshRenderer>(); if (mr == null) mr = holder.gameObject.AddComponent<MeshRenderer>();
                mf.sharedMesh = mesh; mr.sharedMaterial = material;
                EditorUtility.SetDirty(holder.gameObject);
                sb.AppendLine($"{path}: {note} -> {meshPath}");
            }
            AssetDatabase.SaveAssets();
            if (save && scene.isDirty) SaveWithBackup(scene, sb);
            return sb.ToString();
        }
    }
}
