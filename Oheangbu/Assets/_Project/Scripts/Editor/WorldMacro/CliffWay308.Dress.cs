using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 cliff way, rock dressing (user 2026-10-09: 용머리 해안 에셋으로 재배치). Every value [TEST].
    // The walked path, steps, rail and all collision stay what `build` made. `dress` lays light copies of the Yongmeori coast cliff
    // scans (Ymc308) onto the wall in tiers: each scan is bent along the path and its depth squashed to a relief in front of the
    // generated shell, which stays as the backing that closes the gaps. No piece has a collider. A vertex that would stand in the
    // walkway is moved back onto the cut face (never deleted), so the path reads as cut into the rock: within the headroom nothing
    // is in front of the inner edge, above it rock may hang over the path.
    //   dress | dress:plan | dress:clear        numbers: Tools/Unity/Stage308_cliffway/cliffway308.json "dress"
    public static partial class CliffWay308
    {
        const string DressName = "Ymc", DressDir = AssetDir + "/Dress";
        [Serializable] sealed class Stations { public float cx, cz, water, baseDepth, hw, batterUp, batterDown, headroom; public float[] s = Array.Empty<float>(), th = Array.Empty<float>(), rp = Array.Empty<float>(), y = Array.Empty<float>(), top = Array.Empty<float>(), fill = Array.Empty<float>(); }
        [Serializable] sealed class Tier { public string name = ""; public string anchor = "water"; public float offset, relief = 7f, clearAbove = 1.3f, clearBelow = 2.8f, overlap = .7f, minWall = 8f, scaleMin = 1f, scaleMax = 1f; public float from = .04f, to = .97f; public string[] pieces = Array.Empty<string>(); }
        [Serializable] sealed class DressCfg { public int seed = 30809; public float overhang = 4f, lod = .22f, tint = 1.5f, behind = 2f; public float[] shellTint = Array.Empty<float>(), capTint = Array.Empty<float>(), pathTint = Array.Empty<float>(); public Tier[] tiers = Array.Empty<Tier>(); }
        [Serializable] sealed class CfgFile { public DressCfg dress = new DressCfg(); }

        static string Dress(string c)
        {
            var scene = PlayScene(c != "dress:plan");
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName); if (root == null) throw new Exception("no " + RootName + " root (run build first)");
            var old = root.transform.Find(DressName);
            if (c == "dress:clear")
            {
                if (old != null) Object.DestroyImmediate(old.gameObject); if (AssetDatabase.IsValidFolder(DressDir)) AssetDatabase.DeleteAsset(DressDir);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); return "CliffWay308 dress: cleared";
            }
            var st = JsonUtility.FromJson<Stations>(File.ReadAllText(Path.Combine(DataDir, "stations.json")));
            var cfg = JsonUtility.FromJson<CfgFile>(File.ReadAllText(Path.Combine(RepoRoot, "Tools", "Unity", "Stage308_cliffway", "cliffway308.json"))).dress;
            if (cfg.tiers.Length == 0) throw new Exception("no \"dress\" tiers in cliffway308.json");
            bool plan = c == "dress:plan"; int n = st.s.Length; float length = st.s[n - 1];
            if (!plan)
            {
                if (old != null) Object.DestroyImmediate(old.gameObject);
                if (AssetDatabase.IsValidFolder(DressDir)) AssetDatabase.DeleteAsset(DressDir); Folder(DressDir);
            }
            var holder = plan ? null : new GameObject(DressName); if (holder != null) holder.transform.SetParent(root.transform, false);
            var rng = new System.Random(cfg.seed); var sb = new StringBuilder("CliffWay308 dress" + (plan ? " (plan)" : "") + "\n");
            var face = new Dictionary<string, float>(); int pieces = 0, moved = 0, unique = 0; long tris = 0, bytes = 0;

            // station values at a fractional index (the path is sampled every half metre)
            float L(float[] a, float f) { f = Mathf.Clamp(f, 0f, n - 1.001f); int i = (int)f; return Mathf.Lerp(a[i], a[i + 1], f - i); }
            int At(float s) => Mathf.Clamp(Mathf.RoundToInt(s / length * (n - 1)), 0, n - 1);
            var mats = new Dictionary<string, Material[]>();
            Material[] Mats(string name)
            {
                if (mats.TryGetValue(name, out var known)) return known;
                // this tool's own paler copies of the pack materials (the pack is read, never changed): the lake slopes around the wall are pale
                Folder(AssetDir + "/Materials");
                var made = Ymc308.Materials(name).Select(src =>
                {
                    string path = AssetDir + "/Materials/ymc_" + src.name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null) { m = new Material(src) { name = "ymc_" + src.name }; AssetDatabase.CreateAsset(m, path); }
                    string prop = src.HasProperty("_BaseColor") ? "_BaseColor" : "_Color"; var col = src.HasProperty(prop) ? src.GetColor(prop) : Color.white; m.SetColor(prop, new Color(col.r * cfg.tint, col.g * cfg.tint, col.b * cfg.tint, col.a)); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
                    return m;
                }).ToArray();
                mats[name] = made; return made;
            }

            if (!plan)
                foreach (var (slot, tint) in new[] { ("cliff_rock", cfg.shellTint), ("cliff_cap", cfg.capTint) })
                {
                    // the shell shows in the cut along the path and between scans: its tone follows the scans
                    var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDir + "/Materials/" + slot + ".mat"); if (m == null || tint.Length < 3) continue;
                    m.SetColor("_BaseColor", new Color(tint[0], tint[1], tint[2], 1f)); EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
                }
            foreach (var tier in cfg.tiers)
            {
                float s = tier.from * length; int placed = 0;
                while (s < tier.to * length)
                {
                    int i = At(s); bool below = tier.anchor == "water" || tier.anchor == "under"; float wall = below ? st.y[i] - st.water : st.top[i] - st.y[i];
                    if (wall < tier.minWall || st.fill[i] < 3f) { s += 6f; continue; }
                    string name = tier.pieces[rng.Next(tier.pieces.Length)]; var lite = Ymc308.Load(name, 0); var far = Ymc308.Load(name, 1);
                    if (!face.TryGetValue(name, out float faceYaw)) { faceYaw = FaceYaw(lite); face[name] = faceYaw; }
                    float scale = Mathf.Lerp(tier.scaleMin, tier.scaleMax, (float)rng.NextDouble());
                    // the piece turned so that its open face looks along +z of a flat frame: x along the wall, y up, z toward the lake
                    var rot = Quaternion.Euler(0f, -faceYaw + ((float)rng.NextDouble() - .5f) * 24f, 0f);
                    var v0 = lite.vertices; var flat = new Vector3[v0.Length]; float xMin = 1e9f, xMax = -1e9f, yMin = 1e9f, yMax = -1e9f; var depth = new float[v0.Length];
                    for (int k = 0; k < v0.Length; k++)
                    {
                        var q = rot * (v0[k] * scale); flat[k] = q; depth[k] = q.z;
                        if (q.x < xMin) xMin = q.x; if (q.x > xMax) xMax = q.x; if (q.y < yMin) yMin = q.y; if (q.y > yMax) yMax = q.y;
                    }
                    Array.Sort(depth); float zLo = depth[(int)(depth.Length * .05f)], zHi = depth[(int)(depth.Length * .95f)];
                    float squash = Mathf.Min(1f, tier.relief / Mathf.Max(zHi - zLo, .1f));                   // a scan can be 50 m deep: the wall gets it as relief
                    float width = xMax - xMin, height = yMax - yMin, xMid = (xMin + xMax) * .5f;
                    float bottom = tier.anchor == "water" ? st.water + tier.offset : tier.anchor == "under" ? st.y[i] + tier.offset - height : tier.anchor == "path" ? st.y[i] + tier.offset : st.top[i] + tier.offset - height;
                    float sMid = st.s[i]; pieces++; placed++; s += Mathf.Max(8f, width * tier.overlap);
                    if (plan) { sb.Append("  ").Append(tier.name).Append(' ').Append(name).Append(" s ").Append(F(sMid)).Append(" scale ").Append(F(scale, "F2")).Append(" size ").Append(F(width)).Append(" x ").Append(F(height)).Append(" depth ").Append(F(zHi - zLo)).Append(" bottom ").Append(F(bottom)).Append('\n'); continue; }

                    // a flat-frame point bent onto the wall: along the path by arc length, out from the shell by its squashed depth
                    Vector3 Bend(Vector3 q, ref int cut)
                    {
                        // minus: the path runs clockwise, +x of the flat frame must not mirror the scan
                        float f = Mathf.Clamp((sMid - (q.x - xMid)) / length, 0f, 1f) * (n - 1); float th = L(st.th, f), rp = L(st.rp, f), yp = L(st.y, f);
                        float y = bottom + (q.y - yMin), h = y - yp;
                        float shell = h < 0f ? rp - st.hw + st.batterDown * h : rp + st.hw + st.batterUp * h;
                        float r = shell - (h < 0f ? tier.clearBelow : tier.clearAbove) - (Mathf.Clamp(q.z, zLo, zHi) - zLo) * squash;
                        if (h < -.25f)
                        {
                            // under the walkway the rock closes in to the outer edge, so nothing stands in front of the tread
                            float t = Mathf.Clamp01((-h - .25f) / 8f); float rmin = rp - st.hw + .1f - 14f * t * t * (3f - 2f * t);
                            if (r < rmin) r = rmin;
                        }
                        else
                        {
                            // the cut: within the headroom the scan stays behind the shell (the cut face is the shell's), above it rock may hang over the path
                            float t = Mathf.Clamp01((h - st.headroom) / 6f); t = t * t * (3f - 2f * t); float rmin = rp + st.hw + st.batterUp * Mathf.Max(h, 0f) + Mathf.Lerp(cfg.behind, -cfg.overhang, t);
                            if (r < rmin) { r = rmin; cut++; }
                        }
                        return new Vector3(st.cx + Mathf.Cos(th) * r, y, st.cz + Mathf.Sin(th) * r);
                    }
                    int none = 0; var centre = Bend(new Vector3(xMid, (yMin + yMax) * .5f, zLo), ref none);
                    var g = new GameObject(tier.name + "_" + placed.ToString("00") + "_" + name.Replace("SM_YMC_", "")); g.transform.SetParent(holder.transform, false); g.transform.position = centre;
                    var lods = new List<LOD>();
                    foreach (var (level, mesh) in new[] { (0, lite), (1, far) })
                    {
                        var src = level == 0 ? flat : mesh.vertices.Select(q => rot * (q * scale)).ToArray(); var local = new Vector3[src.Length]; int cut = 0;
                        for (int k = 0; k < src.Length; k++) local[k] = Bend(src[k], ref cut) - centre;
                        var sub = Enumerable.Range(0, mesh.subMeshCount).Select(mesh.GetTriangles).ToArray();
                        var use = Ymc308.Make(g.name + "_L" + level, local, Ymc308.Normals(local, sub), mesh.uv, sub);
                        AssetDatabase.CreateAsset(use, DressDir + "/" + use.name + ".asset"); unique++; if (level == 0) { moved += cut; tris += sub.Sum(t => t.Length / 3); }
                        var child = new GameObject("LOD" + level); child.transform.SetParent(g.transform, false);
                        child.AddComponent<MeshFilter>().sharedMesh = use; var r = child.AddComponent<MeshRenderer>(); r.sharedMaterials = Mats(name);
                        r.shadowCastingMode = level == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off; r.allowOcclusionWhenDynamic = false;
                        lods.Add(new LOD(level == 0 ? cfg.lod : 0f, new Renderer[] { r }));
                        bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(use);
                    }
                    var group = g.AddComponent<LODGroup>(); group.SetLODs(lods.ToArray()); group.RecalculateBounds();
                }
                sb.Append("  tier ").Append(tier.name).Append(": ").Append(placed).Append(" pieces\n");
            }
            if (!plan)
            {
                // the walked floor is the cliff's own stone, cut and worn (user 2026-10-09, with a Huashan photograph: 바닥을 돌로):
                // the treads take this tool's copy of the cliff rock, a little paler than the wall so the way still reads
                var treads = root.transform.Find("Steps")?.GetComponent<MeshRenderer>(); var wallRock = AssetDatabase.LoadAssetAtPath<Material>(AssetDir + "/Materials/cliff_rock.mat");
                if (treads != null && wallRock != null)
                {
                    string path = AssetDir + "/Materials/path_stone.mat"; var stone = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (stone == null) { stone = new Material(wallRock) { name = "path_stone" }; AssetDatabase.CreateAsset(stone, path); }
                    var t = cfg.pathTint.Length >= 3 ? cfg.pathTint : new[] { .8f, .75f, .68f };
                    stone.CopyPropertiesFromMaterial(wallRock); stone.SetColor("_BaseColor", new Color(t[0], t[1], t[2], 1f)); EditorUtility.SetDirty(stone); AssetDatabase.SaveAssetIfDirty(stone);
                    treads.sharedMaterials = Enumerable.Repeat(stone, treads.sharedMaterials.Length).ToArray();
                }
                EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new Exception("the play scene was not saved");
            }
            sb.Append("  pieces ").Append(pieces).Append(", near triangles ").Append(tris.ToString("N0", CultureInfo.InvariantCulture)).Append(", vertices moved off the walkway ").Append(moved)
              .Append(", meshes of their own ").Append(unique).Append(", mesh memory ").Append(F(bytes / 1048576f)).Append(" MB\n");
            sb.Append("  face yaw ").Append(string.Join(", ", face.Select(k => k.Key.Replace("SM_YMC_", "") + " " + F(k.Value, "F0"))));
            return sb.ToString();
        }

        // Which way a scan faces: the horizontal direction its surface normals lean to (a scan of a cliff is mostly its seaward face).
        static float FaceYaw(Mesh m)
        {
            var v = m.vertices; var sum = Vector3.zero;
            for (int s = 0; s < m.subMeshCount; s++) { var t = m.GetTriangles(s); for (int i = 0; i < t.Length; i += 3) sum += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]); }
            sum.y = 0f; return sum.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(sum.x, sum.z) * Mathf.Rad2Deg;
        }
    }
}
