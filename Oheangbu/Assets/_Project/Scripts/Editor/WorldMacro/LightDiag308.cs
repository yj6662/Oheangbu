using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 read-only diagnosis of things that glow (user report 2026-10-08: huge white blooms with a black core seen from far away).
    //   glow            every renderer whose shader is a glow / light-marker shader (FarGlow, BeaconHalo, InkBeacon, InkLightSource, Lantern)
    //                   grouped by material: count, sizes, the material's numbers; and every non-directional light over intensity 6
    //   near:x,y,z[,r]  every renderer within r metres (default 14) of a point: path, shader, material, bounds size, queue
    public static class LightDiag308
    {
        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "glow") return Glow();
                if (c.StartsWith("near:", StringComparison.Ordinal)) return Near(c.Substring(5));
                if (c.StartsWith("mesh:", StringComparison.Ordinal)) return MeshOf(c.Substring(5));
                if (c.StartsWith("hide:", StringComparison.Ordinal)) return Hide(c.Substring(5));
                if (c == "show") return Show();
                if (c.StartsWith("normalsforce:", StringComparison.Ordinal)) { Force = true; try { return NormalsDebug(c.Substring(13)); } finally { Force = false; } }
                if (c.StartsWith("normalsdbg:", StringComparison.Ordinal)) return NormalsDebug(c.Substring(11));
                if (c.StartsWith("normals:", StringComparison.Ordinal)) return Normals(c.Substring(8));
                if (c.StartsWith("roots:", StringComparison.Ordinal)) return Roots(c.Substring(6));
                if (c.StartsWith("hideroot:", StringComparison.Ordinal)) return HideRoot(c.Substring(9));
                if (c.StartsWith("pick:", StringComparison.Ordinal)) return Pick(c.Substring(5));
                return "REFUSED glow | near:x,y,z[,r]";
            }
            catch (Exception e) { return "REFUSED " + e.GetType().Name + ": " + e.Message; }
        }

        static string PathOf(Transform t) { string s = t.name; int n = 0; while (t.parent != null && n++ < 4) { t = t.parent; s = t.name + "/" + s; } return s; }
        static bool GlowShader(string n) => n.Contains("FarGlow") || n.Contains("BeaconHalo") || n.Contains("InkBeacon") || n.Contains("InkLightSource") || n.Contains("Lantern") || n.Contains("Halo");

        static string Glow()
        {
            var sb = new StringBuilder("glow renderers").Append((char)10);
            var rs = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.sharedMaterial != null && r.sharedMaterial.shader != null && GlowShader(r.sharedMaterial.shader.name)).ToList();
            foreach (var g in rs.GroupBy(r => r.sharedMaterial).OrderByDescending(g => g.Count()))
            {
                var m = g.Key; sb.Append("  ").Append(m.name).Append(" [").Append(m.shader.name).Append("] x").Append(g.Count()).Append(" queue ").Append(m.renderQueue);
                foreach (string p in new[] { "_Color", "_BaseColor", "_EmissionColor" }) if (m.HasProperty(p)) sb.Append(' ').Append(p).Append(' ').Append(m.GetColor(p).ToString("F2"));
                foreach (string p in new[] { "_Peak", "_Radius", "_MinPx", "_MaxPx", "_MaxWorld", "_Intensity", "_Size", "_Gain", "_Scale" }) if (m.HasProperty(p)) sb.Append(' ').Append(p).Append(' ').Append(m.GetFloat(p).ToString("0.###"));
                sb.Append((char)10);
                foreach (var r in g.OrderByDescending(r => r.bounds.size.magnitude).Take(4)) sb.Append("      ").Append(PathOf(r.transform)).Append(" at ").Append(r.bounds.center.ToString("F0")).Append(" size ").Append(r.bounds.size.ToString("F1")).Append(" layer ").Append(LayerMask.LayerToName(r.gameObject.layer)).Append((char)10);
            }
            var ls = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(l => l.type != LightType.Directional && l.intensity > 6f).OrderByDescending(l => l.intensity).ToList();
            sb.Append("lights over 6: ").Append(ls.Count).Append((char)10);
            foreach (var l in ls.Take(12)) sb.Append("  ").Append(PathOf(l.transform)).Append(" intensity ").Append(l.intensity.ToString("0.#")).Append(" range ").Append(l.range.ToString("0.#")).Append(" at ").Append(l.transform.position.ToString("F0")).Append((char)10);
            return sb.ToString().TrimEnd();
        }

        // mesh:x,y,z,r,<name part>  the meshes and materials of matching renderers near a point: normals, tangents, zero-length ones, the material's numbers
        static string MeshOf(string arg)
        {
            var q = arg.Split(','); var p = new Vector3(float.Parse(q[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(q[2], System.Globalization.CultureInfo.InvariantCulture)); float rad = float.Parse(q[3], System.Globalization.CultureInfo.InvariantCulture); string part = q.Length > 4 ? q[4] : "";
            var sb = new StringBuilder("meshes near " + p.ToString("F0")).Append((char)10);
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.bounds.SqrDistance(p) < rad * rad && (PathOf(r.transform) + r.sharedMaterial?.name).IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var f = r.GetComponent<MeshFilter>(); var m = f != null ? f.sharedMesh : null; if (m == null) { sb.Append("  ").Append(PathOf(r.transform)).Append(" NO MESH").Append((char)10); continue; }
                var ns = m.normals; var ts = m.tangents; int zn = ns.Count(n => n.sqrMagnitude < 1e-6f || float.IsNaN(n.x)), zt = ts.Count(t => ((Vector3)t).sqrMagnitude < 1e-6f || float.IsNaN(t.x)); var vs = m.vertices; int nanV = vs.Count(v => float.IsNaN(v.x) || float.IsInfinity(v.x));
                sb.Append("  ").Append(PathOf(r.transform)).Append(" | mesh ").Append(m.name).Append(" (").Append(AssetDatabase.GetAssetPath(m)).Append(") verts ").Append(m.vertexCount).Append(" normals ").Append(ns.Length).Append(" zero ").Append(zn).Append(" tangents ").Append(ts.Length).Append(" zero ").Append(zt).Append(" nan verts ").Append(nanV).Append(" | scale ").Append(r.transform.lossyScale.ToString("F3")).Append((char)10);
                foreach (var mat in r.sharedMaterials.Where(x => x != null))
                {
                    sb.Append("      ").Append(mat.name).Append(" [").Append(mat.shader.name).Append("] ").Append(AssetDatabase.GetAssetPath(mat)).Append(" kw ").Append(string.Join(",", mat.shaderKeywords));
                    int n = mat.shader.GetPropertyCount(); for (int i = 0; i < n; i++) { var t = mat.shader.GetPropertyType(i); string pn = mat.shader.GetPropertyName(i); if (t == UnityEngine.Rendering.ShaderPropertyType.Float || t == UnityEngine.Rendering.ShaderPropertyType.Range) sb.Append(' ').Append(pn).Append('=').Append(mat.GetFloat(pn).ToString("0.###")); else if (t == UnityEngine.Rendering.ShaderPropertyType.Color) sb.Append(' ').Append(pn).Append('=').Append(mat.GetColor(pn).ToString("F2")); else if (t == UnityEngine.Rendering.ShaderPropertyType.Texture) sb.Append(' ').Append(pn).Append('=').Append(mat.GetTexture(pn) != null ? mat.GetTexture(pn).name : "none"); }
                    sb.Append((char)10);
                }
            }
            return sb.ToString().TrimEnd();
        }

        // hide:x,y,z,r,<part>  (Play or Edit, in memory) switch off the renderers near a point whose shader, material or path contains the part
        // ("lights" = the lights near the point instead); show = switch everything back on. For finding which thing a picture comes from.
        static readonly System.Collections.Generic.List<Behaviour> hiddenB = new System.Collections.Generic.List<Behaviour>(); static readonly System.Collections.Generic.List<Renderer> hiddenR = new System.Collections.Generic.List<Renderer>();
        static string Hide(string arg)
        {
            var q = arg.Split(','); var ic = System.Globalization.CultureInfo.InvariantCulture; var p = new Vector3(float.Parse(q[0], ic), float.Parse(q[1], ic), float.Parse(q[2], ic)); float rad = float.Parse(q[3], ic); string part = q[4]; int n = 0;
            if (part == "lights") { foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(l => l.enabled && l.type != LightType.Directional && Vector3.Distance(l.transform.position, p) < rad)) { l.enabled = false; hiddenB.Add(l); n++; } return "hid " + n + " lights"; }
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.enabled && r.sharedMaterial != null && r.bounds.SqrDistance(p) < rad * rad && r.bounds.size.magnitude < 80f && (r.sharedMaterial.shader.name + "|" + r.sharedMaterial.name + "|" + PathOf(r.transform) + "|" + r.GetType().Name).IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0))
            { r.enabled = false; hiddenR.Add(r); n++; }
            return "hid " + n + " renderers matching " + part;
        }
        static string Show() { int n = 0; foreach (var r in hiddenR) if (r != null) { r.enabled = true; n++; } foreach (var b in hiddenB) if (b != null) { b.enabled = true; n++; } hiddenR.Clear(); hiddenB.Clear(); return "shown " + n; }

        // pick:u,v[,grow]  (Play) every renderer whose bounds (grown by `grow` metres, default 1.5) the view ray through viewport point (u, v)
        // passes, nearest first, at any distance - what lies along the line of sight of a pixel
        static string Pick(string arg)
        {
            var q = arg.Split(','); var ic = System.Globalization.CultureInfo.InvariantCulture; float u = float.Parse(q[0], ic), v = float.Parse(q[1], ic), grow = q.Length > 2 ? float.Parse(q[2], ic) : 1.5f;
            var cam = Camera.main; if (cam == null) return "REFUSED no main camera"; var ray = cam.ViewportPointToRay(new Vector3(u, v, 0f));
            var sb = new StringBuilder("pick from " + ray.origin.ToString("F1") + " dir " + ray.direction.ToString("F3")).Append((char)10); int n = 0;
            foreach (var x in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.enabled && r.sharedMaterial != null).Select(r => { var b = r.bounds; b.Expand(grow * 2f); return (r, hit: b.IntersectRay(ray, out float d), d); }).Where(x => x.hit && x.d > 0f && x.r.bounds.size.magnitude < 400f).OrderBy(x => x.d).Take(60))
            { sb.Append("  ").Append(x.d.ToString("0")).Append(" m | ").Append(x.r.sharedMaterial.shader.name).Append(" | ").Append(x.r.sharedMaterial.name).Append(" | ").Append(PathOf(x.r.transform)).Append(" | ").Append(x.r.bounds.size.ToString("F1")).Append(" | ").Append(x.r.GetType().Name).Append(" layer ").Append(LayerMask.LayerToName(x.r.gameObject.layer)).Append((char)10); n++; }
            return sb.Append("  ").Append(n).Append(" listed").ToString();
        }

        // roots:x,y,z,r  the scene roots that have a renderer (ANY size) whose bounds come within r of the point: count, largest bounds
        static string Roots(string arg)
        {
            var q = arg.Split(','); var ic = System.Globalization.CultureInfo.InvariantCulture; var p = new Vector3(float.Parse(q[0], ic), float.Parse(q[1], ic), float.Parse(q[2], ic)); float rad = float.Parse(q[3], ic);
            var sb = new StringBuilder("roots near " + p.ToString("F0")).Append((char)10);
            foreach (var g in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.enabled && r.bounds.SqrDistance(p) < rad * rad).GroupBy(r => r.transform.root.name).OrderByDescending(g => g.Count()))
                sb.Append("  ").Append(g.Key).Append(" x").Append(g.Count()).Append(" largest ").Append(g.Max(r => r.bounds.size.magnitude).ToString("0")).Append(" m | ").Append(string.Join(", ", g.Select(r => r.sharedMaterial != null ? r.sharedMaterial.shader.name : "none").Distinct().Take(4))).Append((char)10);
            return sb.ToString().TrimEnd();
        }
        // hideroot:x,y,z,r,<root name | * >  like hide, by scene root, with no size limit ("*" = every root)
        static string HideRoot(string arg)
        {
            var q = arg.Split(','); var ic = System.Globalization.CultureInfo.InvariantCulture; var p = new Vector3(float.Parse(q[0], ic), float.Parse(q[1], ic), float.Parse(q[2], ic)); float rad = float.Parse(q[3], ic); string root = q[4]; int n = 0;
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.enabled && r.bounds.SqrDistance(p) < rad * rad && (root == "*" || r.transform.root.name == root))) { r.enabled = false; hiddenR.Add(r); n++; }
            return "hid " + n + " renderers of root " + root;
        }

        // normals:<folder>[:apply]  Mesh assets under the folder that have zero-length or NaN normals (a lit shader normalises them into NaN:
        // black pixels that Bloom spreads into a huge white glow). apply = repair them in place: the mean of the triangle normals that use
        // the vertex, or of the nearest sound normal at the same position, else straight up. Bounds, vertices, UVs and triangles untouched.
        static string Normals(string arg)
        {
            bool apply = arg.EndsWith(":apply", StringComparison.Ordinal); string folder = apply ? arg.Substring(0, arg.Length - 6) : arg;
            var sb = new StringBuilder("normals " + folder + (apply ? " APPLY" : " (scan)")).Append((char)10); int bad = 0, total = 0, fixedMeshes = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); if (!path.EndsWith(".asset", StringComparison.Ordinal)) continue;
                var m = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (m == null || !m.isReadable) continue; total++;
                var ns = m.normals; if (ns.Length == 0) continue; var idx = Enumerable.Range(0, ns.Length).Where(i => ns[i].sqrMagnitude < 1e-6f || float.IsNaN(ns[i].x) || float.IsNaN(ns[i].y) || float.IsNaN(ns[i].z)).ToList();
                if (idx.Count == 0) continue; bad++; if (bad <= 40) sb.Append("  ").Append(idx.Count).Append(" of ").Append(ns.Length).Append("  ").Append(path).Append((char)10);
                if (!apply) continue;
                var vs = m.vertices; var acc = new Vector3[ns.Length];
                for (int sm = 0; sm < m.subMeshCount; sm++) { var t = m.GetTriangles(sm); for (int i = 0; i + 2 < t.Length; i += 3) { var fn = Vector3.Cross(vs[t[i + 1]] - vs[t[i]], vs[t[i + 2]] - vs[t[i]]); acc[t[i]] += fn; acc[t[i + 1]] += fn; acc[t[i + 2]] += fn; } }
                foreach (int i in idx)
                {
                    var n = acc[i];
                    if (n.magnitude < 1e-7f) { float best = float.MaxValue; for (int j = 0; j < ns.Length; j++) { if (ns[j].sqrMagnitude < .5f || float.IsNaN(ns[j].x)) continue; float dd = (vs[j] - vs[i]).sqrMagnitude; if (dd < best) { best = dd; n = ns[j]; } } }
                    // Vector3.normalized gives ZERO under a length of 1e-5 (sliver triangles land there): divide by hand, and never leave a zero
                    float len = n.magnitude; ns[i] = len > 1e-18f && !float.IsNaN(len) ? n / len : Vector3.up; if (ns[i].sqrMagnitude < .5f || float.IsNaN(ns[i].x)) ns[i] = Vector3.up;
                }
                // the asset as it was goes to Tools/Unity/Stage308_backup_normals/<day>/ first (once: an existing copy is kept)
                string keep = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../Tools/Unity/Stage308_backup_normals/" + DateTime.Now.ToString("yyyyMMdd") + "/" + path.Replace('/', '_')));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(keep)); if (!System.IO.File.Exists(keep)) System.IO.File.Copy(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", path)), keep);
                m.normals = ns; EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); fixedMeshes++;
            }
            return sb.Append("  meshes ").Append(total).Append(", with bad normals ").Append(bad).Append(apply ? ", repaired " + fixedMeshes : "").ToString();
        }

        // normalsdbg:<mesh asset>  the bad normals of one mesh: the raw value, the vertex, the layout - and what a write-then-read gives back (not saved)
        static string NormalsDebug(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (m == null) return "REFUSED no mesh " + path;
            var sb = new StringBuilder(path).Append((char)10).Append("  layout ").Append(string.Join(" ", m.GetVertexAttributes().Select(d => d.attribute + ":" + d.format + "x" + d.dimension))).Append(" | submeshes ").Append(m.subMeshCount).Append(" readable ").Append(m.isReadable).Append((char)10);
            var ns = m.normals; var vs = m.vertices; var idx = Enumerable.Range(0, ns.Length).Where(i => ns[i].sqrMagnitude < 1e-6f || float.IsNaN(ns[i].x) || float.IsNaN(ns[i].y) || float.IsNaN(ns[i].z)).ToList();
            foreach (int i in idx.Take(12)) sb.Append("  #").Append(i).Append(" normal ").Append(ns[i].ToString("G4")).Append(" vertex ").Append(vs[i].ToString("F3")).Append((char)10);
            if (idx.Count > 0) { var copy = UnityEngine.Object.Instantiate(m); var n2 = copy.normals; foreach (int i in idx) n2[i] = Vector3.up; copy.normals = n2; var back = copy.normals; sb.Append("  write up then read: ").Append(string.Join(" ", idx.Take(6).Select(i => back[i].ToString("G3")))).Append(" | layout after ").Append(string.Join(" ", copy.GetVertexAttributes().Select(d => d.attribute + ":" + d.format))); UnityEngine.Object.DestroyImmediate(copy); }
            if (path.Length > 0 && idx.Count > 0 && Force)
            {
                foreach (int i in idx) ns[i] = Vector3.up; m.normals = ns; var r1 = m.normals; sb.Append((char)10).Append("  on the asset, before save: ").Append(string.Join(" ", idx.Take(4).Select(i => r1[i].ToString("G3"))));
                EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); var r2 = AssetDatabase.LoadAssetAtPath<Mesh>(path).normals; sb.Append(" | after save: ").Append(string.Join(" ", idx.Take(4).Select(i => r2[i].ToString("G3"))));
            }
            return sb.ToString();
        }
        static bool Force;

        static string Near(string arg)
        {
            var v = arg.Split(',').Select(x => float.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray(); var p = new Vector3(v[0], v[1], v[2]); float rad = v.Length > 3 ? v[3] : 14f;
            var sb = new StringBuilder("renderers within " + rad + " m of " + p.ToString("F0")).Append((char)10);
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.sharedMaterial != null && r.bounds.SqrDistance(p) < rad * rad && r.bounds.size.magnitude < 60f).OrderBy(r => r.sharedMaterial.shader.name).ThenBy(r => r.name))
                sb.Append("  ").Append(r.sharedMaterial.shader.name).Append(" | ").Append(r.sharedMaterial.name).Append(" q").Append(r.sharedMaterial.renderQueue).Append(" | ").Append(PathOf(r.transform)).Append(" | ").Append(r.bounds.size.ToString("F1")).Append(" | ").Append(r.GetType().Name).Append((char)10);
            return sb.ToString().TrimEnd();
        }
    }
}
