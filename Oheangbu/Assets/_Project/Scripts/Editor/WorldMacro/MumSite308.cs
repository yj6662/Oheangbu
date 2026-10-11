using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 a place where 뭄 has something to cross (SPEC-MUM-BRIDGE-295 "형성 연출 #308", user 2026-10-08: inside the temple precinct).
    // Two stone blocks from an existing asset pack stand on the ground with a gap between them; their flat tops are the two banks.
    // Everything is under one new root (MumSite308) of the main scene, so it can be switched off or deleted without touching anything else.
    //   scan:<prefab name>                                   size, pivot and colliders of a prefab
    //   build:<prefab name>:<x>,<z>:<gap m>:<top m>[:<scale>[:<kerb m>]] two blocks along +z around the point, tops <top> above the ground, <gap> between their faces
    //   remove | status
    public static class MumSite308
    {
        const string RootName = "MumSite308";

        public static string Run(string command)
        {
            var a = (command ?? "").Split(':'); var inv = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                switch (a[0])
                {
                    case "scan": return Scan(a[1]);
                    case "build":
                    {
                        var c = a[2].Split(',');
                        return Build(a[1], float.Parse(c[0], inv), float.Parse(c[1], inv), float.Parse(a[3], inv), float.Parse(a[4], inv), a.Length > 5 ? float.Parse(a[5], inv) : 1f, a.Length > 6 ? float.Parse(a[6], inv) : 0f);
                    }
                    case "rock":
                    {
                        var c = a[1].Split(',');
                        return Rock(float.Parse(c[0], inv), float.Parse(c[1], inv), float.Parse(a[2], inv), float.Parse(a[3], inv), a.Length > 4 ? float.Parse(a[4], inv) : 1f, a.Length > 5 ? float.Parse(a[5], inv) : 0f);
                    }
                    case "remove": { var r = Root(false); if (r == null) return "no site"; var s = r.scene; Object.DestroyImmediate(r); EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s); return "removed"; }
                    case "status": { var r = Root(false); return r == null ? "no site" : RootName + " in " + r.scene.name + ", " + r.transform.childCount + " blocks, " + string.Join(" | ", r.GetComponentsInChildren<Collider>().Select(b => "top " + b.bounds.max.y.ToString("0.00", inv) + " z " + b.bounds.min.z.ToString("0.0", inv) + ".." + b.bounds.max.z.ToString("0.0", inv) + " x " + b.bounds.min.x.ToString("0.0", inv) + ".." + b.bounds.max.x.ToString("0.0", inv))); }
                }
            }
            catch (Exception e) { return "REFUSED " + e.GetType().Name + ": " + e.Message; }
            return "REFUSED scan:<prefab> | build:<prefab>:<x>,<z>:<gap>:<top>[:<scale>] | rock:<x>,<z>:<gap>:<top>[:<scale>] | remove | status";
        }

        static GameObject Prefab(string name)
        {
            var path = AssetDatabase.FindAssets(name + " t:Prefab").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == name);
            return path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : throw new Exception("no prefab named " + name);
        }

        static Bounds Of(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) throw new Exception("no renderer in " + go.name);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static string Scan(string name)
        {
            var prefab = Prefab(name); var go = Object.Instantiate(prefab); go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var b = Of(go);
                return name + " " + AssetDatabase.GetAssetPath(prefab) + " size " + b.size.ToString("F2") + " centre " + b.center.ToString("F2") + " colliders " + go.GetComponentsInChildren<Collider>().Length
                    + " tris " + go.GetComponentsInChildren<MeshFilter>().Where(m => m.sharedMesh != null).Sum(m => m.sharedMesh.triangles.Length / 3);
            }
            finally { Object.DestroyImmediate(go); }
        }

        static GameObject Root(bool create)
        {
            var scene = SceneManager.GetActiveScene();
            var r = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (r == null && create) { r = new GameObject(RootName); SceneManager.MoveGameObjectToScene(r, scene); }
            return r;
        }

        // rock:<x>,<z>:<gap m>:<top m>[:<scale>[:<yaw>]] (user 2026-10-09: 돌 상자 대신 용머리 해안 암반) - two slabs cut from the coast rock
        // shelf (light copy, Ymc308) lie along +z, tops level <top> above the ground, <gap> open between them. The cut mesh is both what
        // is drawn and what is walked on (one MeshCollider a bank, no box). Prints where to stand and where to aim.
        const string RockPiece = "SM_YMC_RockShelf01B";
        const string RockDir = Ymc308.Dir + "/MumSite";

        static string Rock(float x, float z, float gap, float top, float scale, float yaw)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED edit mode only";
            var scene = SceneManager.GetActiveScene(); var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!scene.path.EndsWith("W_Demo_Main.unity", StringComparison.Ordinal)) return "REFUSED open W_Demo_Main first (active scene " + scene.path + ")";
            var old = Root(false); if (old != null) { foreach (var c in old.GetComponentsInChildren<Collider>()) c.enabled = false; Physics.SyncTransforms(); }
            if (!Physics.Raycast(new Vector3(x, 2000f, z), Vector3.down, out var hit, 4000f, ~0, QueryTriggerInteraction.Ignore)) return "REFUSED no ground under the point";
            if (old != null) Object.DestroyImmediate(old);
            float ground = hit.point.y, plane = ground + top;
            if (!AssetDatabase.IsValidFolder(RockDir)) AssetDatabase.CreateFolder(Ymc308.Dir, "MumSite");
            var root = Root(true); var report = new System.Text.StringBuilder(RootName + " rock at " + x.ToString("0.0", inv) + "," + ground.ToString("0.00", inv) + "," + z.ToString("0.0", inv) + " on " + hit.collider.name + ", top " + plane.ToString("0.00", inv));
            var edges = new float[2];
            for (int k = 0; k < 2; k++)
            {
                int side = k == 0 ? -1 : 1; var src = Ymc308.Load(RockPiece, 0); var rot = Quaternion.Euler(0f, yaw + (side < 0 ? 180f : 0f), 0f);
                // a window of the long rock shelf (a different stretch for each bank): inside it the top is one level, its rim goes down
                // into the ground, what lies outside is dropped. The slab keeps the scan's surface, not its lumps and troughs.
                const float halfX = 5.5f, halfZ = 4.5f, rim = .7f; float along = side * 22f * scale;
                var all = src.vertices; var b = new Bounds(rot * (all[0] * scale), Vector3.zero); for (int i = 0; i < all.Length; i++) { all[i] = rot * (all[i] * scale); b.Encapsulate(all[i]); }
                var at = new Vector3(x, 0f, z + side * (gap * .5f + halfZ)); var keep = new int[all.Length]; var vs = new List<Vector3>(); var uvs = new List<Vector2>(); var rims = new List<bool>(); var uv0 = src.uv;
                for (int i = 0; i < all.Length; i++)
                {
                    float qx = all[i].x - b.center.x, qz = all[i].z - b.center.z - along; keep[i] = -1;
                    // a rounded outline that wanders (not a rectangle): 1 on the outline, the outer part is the rim
                    float ang = Mathf.Atan2(qz, qx), f = Mathf.Pow(Mathf.Pow(Mathf.Abs(qx) / halfX, 8f) + Mathf.Pow(Mathf.Abs(qz) / halfZ, 8f), .125f) / (1f + .07f * Mathf.Sin(3f * ang + k * 2.1f) + .04f * Mathf.Sin(7f * ang + k));
                    if (f > 1f) continue;
                    bool edge = f > .9f;
                    // y holds the scan's own height for now; x of uv2 marks the rim
                    keep[i] = vs.Count; vs.Add(new Vector3(qx, all[i].y, qz)); uvs.Add(uv0[i]); rims.Add(edge);
                }
                // the scan's relief is kept at half height under the top level; the 2.2 m toward the gap are level (the bridge lands there)
                var heights = vs.Select(q => q.y).OrderBy(h => h).ToArray(); float reference = heights.Length > 0 ? heights[(int)(heights.Length * .85f)] : 0f;
                for (int i = 0; i < vs.Count; i++)
                {
                    var q = vs[i]; float toGap = halfZ + (side < 0 ? -q.z : q.z);                    // 0 at the gap-side edge of the window
                    float w = Mathf.Clamp01((toGap - rim - 2.2f) / 1.5f); w = w * w * (3f - 2f * w);
                    q.y = rims[i] ? ground - .25f : plane + Mathf.Clamp((q.y - reference) * .5f, -.5f, 0f) * w; vs[i] = q;
                }
                var v = vs.ToArray();
                var sub = Enumerable.Range(0, src.subMeshCount).Select(m => { var t = src.GetTriangles(m); var o = new List<int>(); for (int i = 0; i < t.Length; i += 3) if (keep[t[i]] >= 0 && keep[t[i + 1]] >= 0 && keep[t[i + 2]] >= 0) { o.Add(keep[t[i]]); o.Add(keep[t[i + 1]]); o.Add(keep[t[i + 2]]); } return o.ToArray(); }).ToArray();
                if (v.Length < 100) { Object.DestroyImmediate(root); return "REFUSED the window on " + RockPiece + " is empty"; }
                var mesh = Ymc308.Make(side < 0 ? "MumBank_Near" : "MumBank_Far", v, Ymc308.Normals(v, sub), uvs.ToArray(), sub);
                string path = RockDir + "/" + mesh.name + ".asset"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
                var g = new GameObject(side < 0 ? "Bank_Near" : "Bank_Far"); g.transform.SetParent(root.transform, false); g.transform.position = at;
                g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterials = Ymc308.Materials(RockPiece);
                var col = g.AddComponent<MeshCollider>(); col.sharedMesh = mesh; Physics.SyncTransforms();
                // the end of the bank: nearest the gap where a strip 2.8 m wide and 1.6 m long is all slab (the planner probes the landing)
                bool Slab(float zEdge)
                {
                    for (float dx = -1.4f; dx <= 1.41f; dx += .35f)
                        for (float back = 0f; back <= 1.61f; back += .2f)
                            if (!col.Raycast(new Ray(new Vector3(x + dx, plane + 1f, zEdge - side * back), Vector3.down), out var h, 1.05f) || h.point.y < plane - .02f) return false;
                    return true;
                }
                float found = float.NaN;
                for (float step = 0f; step <= halfZ * 2f; step += .1f) { float zEdge = side < 0 ? at.z + halfZ - step : at.z - halfZ + step; if (Slab(zEdge)) { found = zEdge; break; } }
                if (float.IsNaN(found)) { Object.DestroyImmediate(root); return "REFUSED no strip of slab 2.8 m wide on " + RockPiece + " at top " + top + " scale " + scale; }
                edges[k] = z + side * gap * .5f; g.transform.position += Vector3.forward * (edges[k] - found); at = g.transform.position; Physics.SyncTransforms();
                var wb = new Bounds(new Vector3(at.x, plane, at.z), new Vector3(halfX * 2f, 0f, halfZ * 2f));
                report.Append((char)10).Append("  ").Append(g.name).Append(" z ").Append(wb.min.z.ToString("0.0", inv)).Append("..").Append(wb.max.z.ToString("0.0", inv)).Append(" x ").Append(wb.min.x.ToString("0.0", inv)).Append("..").Append(wb.max.x.ToString("0.0", inv))
                    .Append(" flat edge z ").Append(edges[k].ToString("0.00", inv)).Append(" triangles ").Append(sub.Sum(t => t.Length / 3));
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            report.Append((char)10).Append("  feet ").Append(x.ToString("0.0", inv)).Append(',').Append(plane.ToString("0.00", inv)).Append(',').Append((edges[0] - .5f).ToString("0.00", inv))
                  .Append(" far ").Append(x.ToString("0.0", inv)).Append(',').Append(plane.ToString("0.00", inv)).Append(',').Append((edges[1] + .5f).ToString("0.00", inv));
            return report.ToString();
        }

        static string Build(string name, float x, float z, float gap, float top, float scale, float lip)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED edit mode only";
            var scene = SceneManager.GetActiveScene();
            if (!scene.path.EndsWith("W_Demo_Main.unity", StringComparison.Ordinal)) return "REFUSED open W_Demo_Main first (active scene " + scene.path + ")";
            var prefab = Prefab(name);
            var old = Root(false); if (old != null) Object.DestroyImmediate(old);
            if (!Physics.Raycast(new Vector3(x, 2000f, z), Vector3.down, out var hit, 4000f, ~0, QueryTriggerInteraction.Ignore)) return "REFUSED no ground under the point";
            float ground = hit.point.y; var root = Root(true); var report = new System.Text.StringBuilder(RootName + " at " + x + "," + ground.ToString("0.00") + "," + z + " on " + hit.collider.name);
            foreach (int side in new[] { -1, 1 })
            {
                var block = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform); block.name = side < 0 ? "Bank_Near" : "Bank_Far";
                block.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, side < 0 ? 180f : 0f, 0f)); block.transform.localScale = Vector3.one * scale;
                var b = Of(block);
                // the face toward the gap at +-gap/2, the top at ground + top, centred on x
                float faceZ = z + side * gap * .5f;
                var shift = new Vector3(x - b.center.x, ground + top + lip - b.max.y, side < 0 ? faceZ - b.max.z : faceZ - b.min.z);
                block.transform.position += shift; b = Of(block);
                foreach (var c in block.GetComponentsInChildren<Collider>()) c.enabled = false;         // one plain box is the bank: a flat top the planner can read
                var box = new GameObject("Top_Collision"); box.transform.SetParent(block.transform, true); box.transform.SetPositionAndRotation(b.center, Quaternion.identity);
                var s = block.transform.lossyScale; box.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
                box.transform.position = b.center + Vector3.down * (lip * .5f); box.AddComponent<BoxCollider>().size = new Vector3(b.size.x, b.size.y - lip, b.size.z);   // the walked top is below the kerb
                GameObjectUtility.SetStaticEditorFlags(block, StaticEditorFlags.BatchingStatic);
                report.Append((char)10).Append("  ").Append(block.name).Append(" top ").Append(b.max.y.ToString("0.00")).Append(" z ").Append(b.min.z.ToString("0.0")).Append("..").Append(b.max.z.ToString("0.0"))
                    .Append(" x ").Append(b.min.x.ToString("0.0")).Append("..").Append(b.max.x.ToString("0.0"));
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return report.ToString();
        }
    }
}
