using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 read-only diagnostic: what covers the sky? Lists huge active renderers (any bounds axis > min metres, default 1500),
    // the player camera's far clip / clear flags and the active URP renderer features. Writes nothing.
    // Queue: Oheangbu.EditorTools.WorldMacro.SkyOccluder308 Run "scan[:min=1500]"
    public static class SkyOccluder308
    {
        public static string Run(string command)
        {
            float min = 1500f;
            foreach (var p in (command ?? "").Split(':')) if (p.StartsWith("min=")) float.TryParse(p.Substring(4), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out min);
            var sb = new StringBuilder("SkyOccluder308 " + SceneManager.GetActiveScene().path + "\n");
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine(" camera " + PostLedger308.PathOf(cam.transform) + " active=" + cam.isActiveAndEnabled + " far=" + cam.farClipPlane + " near=" + cam.nearClipPlane + " clear=" + cam.clearFlags
                    + " skyboxComp=" + (cam.GetComponent<Skybox>() != null && cam.GetComponent<Skybox>().enabled));
            sb.AppendLine(" RenderSettings.skybox=" + (RenderSettings.skybox != null ? RenderSettings.skybox.name + "/" + RenderSettings.skybox.shader.name : "null") + " fog=" + RenderSettings.fog);
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            sb.AppendLine(" pipeline " + (rp != null ? rp.name : "null") + " quality " + QualitySettings.GetQualityLevel());
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var s = r.bounds.size; if (Mathf.Max(s.x, Mathf.Max(s.y, s.z)) < min) continue;
                var m = r.sharedMaterial;
                sb.AppendLine(" BIG " + PostLedger308.PathOf(r.transform) + " " + r.GetType().Name + " size " + s.ToString("F0") + " centre " + r.bounds.center.ToString("F0")
                    + " mat " + (m != null ? m.name + "/" + m.shader.name + " q" + m.renderQueue : "null") + " layer " + r.gameObject.layer + " shadows " + r.shadowCastingMode);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
