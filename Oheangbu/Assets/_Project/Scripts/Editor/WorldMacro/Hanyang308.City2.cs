using System;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 Hanyang phase 1c helpers.  c2:water:<x>,<z>;<x>,<z>...   height of the nearest shown water-mesh vertex (within 14 m) at each point
    public static partial class Hanyang308
    {
        static string City2(string[] a)
        {
            string verb = a.Length > 1 ? a[1] : "";
            if (verb != "water" || a.Length < 3) throw new Refuse("c2:water:<x>,<z>;...");
            var main = Main(); var sb = new StringBuilder("Hanyang308 c2:water\n"); var pts = a[2].Split(';').Select(s => s.Split(',').Select(q => float.Parse(q, Inv)).ToArray()).ToArray();
            var best = new float[pts.Length]; var by = new float[pts.Length]; var who = new string[pts.Length]; for (int i = 0; i < best.Length; i++) best[i] = 1e9f;
            foreach (var g in main.GetRootGameObjects())
            {
                if (g.name.IndexOf("Water", StringComparison.Ordinal) < 0) continue;
                foreach (var mf in g.GetComponentsInChildren<MeshFilter>(false))
                {
                    if (mf.sharedMesh == null) continue; var r = mf.GetComponent<Renderer>(); if (r == null || !r.enabled) continue; var bb = r.bounds; bool near = false;
                    foreach (var p in pts) if (p[0] > bb.min.x - 14 && p[0] < bb.max.x + 14 && p[1] > bb.min.z - 14 && p[1] < bb.max.z + 14) near = true;
                    if (!near) continue; Vector3[] vs; try { vs = mf.sharedMesh.isReadable ? mf.sharedMesh.vertices : ReadVerts(mf.sharedMesh); } catch { continue; }
                    var M = mf.transform.localToWorldMatrix;
                    foreach (var v in vs) { var w = M.MultiplyPoint3x4(v); for (int i = 0; i < pts.Length; i++) { float d = (w.x - pts[i][0]) * (w.x - pts[i][0]) + (w.z - pts[i][1]) * (w.z - pts[i][1]); if (d < best[i]) { best[i] = d; by[i] = w.y; who[i] = g.name + "/" + mf.name; } } }
                }
            }
            for (int i = 0; i < pts.Length; i++) sb.AppendLine("  " + F(pts[i][0]) + "," + F(pts[i][1]) + " water " + (best[i] < 196f ? F(by[i], "F2") + " d " + F(Mathf.Sqrt(best[i])) + " " + who[i] : "none"));
            return sb.ToString();
        }
    }
}
