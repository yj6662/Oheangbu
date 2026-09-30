using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #299: which road/paving meshes can actually be seen? Old-world roads survive under the Reworld292 terrain.
    //   road-visible — every enabled mesh named like a road/path/paving: share of sampled vertices within ±1 m of the
    //   terrain surface (visible), under it by more than 1 m (buried) or above it (raised: bridges, decks), plus shader
    public static partial class GroundFit299
    {
        static readonly string[] RoadWords = { "road", "path", "trail", "street", "plaza", "pave", "paving", "yard", "court", "madang", "forecourt", "apron", "threshold", "approach", "walk", "tread", "causeway", "square" };

        static string RoadVisible()
        {
            var scene = SceneManager.GetActiveScene();
            var hits = new RaycastHit[64];
            Physics.SyncTransforms();
            var rows = new System.Collections.Generic.List<(float near, string line)>();
            foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(false)))
            {
                if (!r.enabled) continue;
                var f = r.GetComponent<MeshFilter>(); var mesh = f != null ? f.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                string low = (r.name + " " + mesh.name).ToLowerInvariant();
                if (!RoadWords.Any(w => low.Contains(w))) continue;
                var v = mesh.vertices; int stride = Mathf.Max(1, v.Length / 200);
                int near = 0, buried = 0, above = 0, none = 0, n = 0;
                for (int i = 0; i < v.Length; i += stride)
                {
                    var p = r.transform.TransformPoint(v[i]); n++;
                    // terrain top from far above, so roads buried deep under it are still measured
                    float g = float.NaN;
                    int hn = Physics.RaycastNonAlloc(new Vector3(p.x, 3000f, p.z), Vector3.down, hits, 4000f, ~0, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < hn; h++) if (hits[h].collider != null && hits[h].collider.transform.root.name == "Reworld292_Terrain" && (float.IsNaN(g) || hits[h].point.y > g)) g = hits[h].point.y;
                    if (float.IsNaN(g)) { none++; continue; }
                    float d = p.y - g;
                    if (d < -1f) buried++; else if (d > 1f) above++; else near++;
                }
                if (n == 0) continue;
                float share = near / (float)n;
                var b = r.bounds;
                rows.Add((share, $"visible {share:P0} buried {buried / (float)n:P0} raised {above / (float)n:P0} off-terrain {none}  {PathOf(r.transform)}  [{string.Join(",", r.sharedMaterials.Where(m => m != null).Select(m => m.shader.name).Distinct())}] @({b.center.x:F0},{b.center.y:F0},{b.center.z:F0}) {b.size.x:F0}x{b.size.z:F0}"));
            }
            var sb = new StringBuilder();
            foreach (var row in rows.OrderByDescending(x => x.near)) sb.AppendLine(row.line);
            return sb.Length > 0 ? sb.ToString() : "no road-like meshes";
        }
    }
}
