using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 read-only diagnostic: which renderers stand near a world point? Lists active renderers whose bounds come within
    // r metres (XZ) of the point, nearest first, with bounds, mesh, material and the lowest point above the terrain. Writes nothing.
    // Queue: Oheangbu.EditorTools.WorldMacro.NearRenderers308 Run "x=3080:z=2341[:r=14][:minsize=1.5][:max=40]"
    public static class NearRenderers308
    {
        public static string Run(string command)
        {
            var opt = PostLedger308.Options((command ?? "").Split(':'));
            float F(string k, float d) => opt.TryGetValue(k, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : d;
            float x = F("x", 0), z = F("z", 0), r = F("r", 14), minSize = F("minsize", 1.5f); int max = (int)F("max", 40);
            var sb = new StringBuilder("NearRenderers308 " + SceneManager.GetActiveScene().path + " point (" + x + ", " + z + ") r " + r + "\n");
            var hits = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(q => q.enabled && q.gameObject.activeInHierarchy && !(q is ParticleSystemRenderer))
                .Select(q => new { q, b = q.bounds })
                .Where(h => Mathf.Max(h.b.size.x, Mathf.Max(h.b.size.y, h.b.size.z)) >= minSize && Mathf.Max(h.b.size.x, h.b.size.z) < 400f)
                .Select(h => new { h.q, h.b, d = Vector2.Distance(new Vector2(x, z), new Vector2(Mathf.Clamp(x, h.b.min.x, h.b.max.x), Mathf.Clamp(z, h.b.min.z, h.b.max.z))) })
                .Where(h => h.d <= r).OrderBy(h => h.d).ThenByDescending(h => h.b.size.y).Take(max).ToList();
            foreach (var h in hits)
            {
                float ground = float.NaN;
                if (Physics.Raycast(new Vector3(h.b.center.x, h.b.max.y + 200f, h.b.center.z), Vector3.down, out var hit, 2000f, 1 << 0 | 1 << LayerMask.NameToLayer("Default"), QueryTriggerInteraction.Ignore)) ground = hit.point.y;
                var t = Terrain.activeTerrains.FirstOrDefault(tt => tt != null && h.b.center.x >= tt.transform.position.x && h.b.center.x <= tt.transform.position.x + tt.terrainData.size.x && h.b.center.z >= tt.transform.position.z && h.b.center.z <= tt.transform.position.z + tt.terrainData.size.z);
                float terrain = t != null ? t.SampleHeight(h.b.center) + t.transform.position.y : float.NaN;
                var mf = h.q.GetComponent<MeshFilter>(); var m = h.q.sharedMaterial;
                sb.AppendLine(" d " + h.d.ToString("F1") + " | " + PostLedger308.PathOf(h.q.transform) + " | size " + h.b.size.ToString("F1") + " y " + h.b.min.y.ToString("F1") + ".." + h.b.max.y.ToString("F1")
                    + " terrain " + terrain.ToString("F1") + " | mesh " + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-") + " | mat " + (m != null ? m.name : "null") + " | scale " + h.q.transform.lossyScale.ToString("F2") + " static " + h.q.gameObject.isStatic);
            }
            sb.AppendLine(" total " + hits.Count);
            return sb.ToString().TrimEnd();
        }
    }
}
