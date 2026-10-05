using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 read-only diagnostic: render the RegionSky308 reference stations with some URP renderer features switched off in
    // memory only (restored in finally; the renderer asset is never saved). Answers "does a screen pass cover the sky?".
    // Queue: Oheangbu.EditorTools.WorldMacro.SkyDiag308 Run "off=<Feature1,Feature2>:label=<label>[:stations=S13,S07]"
    public static class SkyDiag308
    {
        public static string Run(string command)
        {
            var opt = PostLedger308.Options((command ?? "").Split(':'));
            var off = opt.TryGetValue("off", out var o) ? o.Split(',').Where(s => s.Length > 0).ToArray() : Array.Empty<string>();
            string label = opt.TryGetValue("label", out var l) && l.Length > 0 ? l : "skydiag";
            string stations = opt.TryGetValue("stations", out var s) && s.Length > 0 ? s : "S13,S07,S12,S14";
            var features = new List<ScriptableRendererFeature>();
            foreach (var asset in GraphicsSettings.allConfiguredRenderPipelines.Concat(new[] { GraphicsSettings.currentRenderPipeline }).OfType<UniversalRenderPipelineAsset>().Distinct())
            {
                var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(asset) as ScriptableRendererData[];
                foreach (var data in list ?? Array.Empty<ScriptableRendererData>())
                    if (data != null) features.AddRange(data.rendererFeatures.Where(f => f != null && off.Contains(f.name) && f.isActive));
            }
            var log = "SkyDiag308 off [" + string.Join(",", off) + "] found " + features.Count + " active feature instance(s)\n";
            var dirty = features.Select(f => EditorUtility_IsDirty(f)).ToArray();
            int? mask = opt.TryGetValue("mask", out var m) && int.TryParse(m, out var mv) ? mv : (int?)null;
            var cams = mask.HasValue ? UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.CompareTag("MainCamera") || c.name == "Main Camera").ToArray() : Array.Empty<Camera>();
            bool solid = opt.ContainsKey("solid"), nopp = opt.ContainsKey("nopp"); if ((solid || nopp) && !mask.HasValue) { cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.CompareTag("MainCamera") || c.name == "Main Camera").ToArray(); }
            var priorMasks = cams.Select(c => c.cullingMask).ToArray(); var priorClear = cams.Select(c => c.clearFlags).ToArray(); var priorPP = cams.Select(c => c.GetComponent<UniversalAdditionalCameraData>() != null && c.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing).ToArray();
            int priorQuality = QualitySettings.GetQualityLevel(); int? quality = opt.TryGetValue("quality", out var qs) && int.TryParse(qs, out var qv) ? qv : (int?)null;
            try
            {
                foreach (var c in cams) { if (mask.HasValue) c.cullingMask = mask.Value; if (solid) c.clearFlags = CameraClearFlags.SolidColor; if (nopp && c.GetComponent<UniversalAdditionalCameraData>() != null) c.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = false; }
                if (quality.HasValue) { QualitySettings.SetQualityLevel(quality.Value, true); log += "quality " + QualitySettings.names[quality.Value] + " pipeline " + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "null") + " | "; }
                if (mask.HasValue) log += "cullingMask " + mask.Value + " on " + cams.Length + " camera(s)\n";
                foreach (var f in features) f.SetActive(false);
                log += RegionSky308Capture.Run("render-ref:" + label + ":stations=" + stations + ":scene=main");
            }
            finally
            {
                for (int i = 0; i < features.Count; i++) { features[i].SetActive(true); if (!dirty[i]) UnityEditor.EditorUtility.ClearDirty(features[i]); }
                for (int i = 0; i < cams.Length; i++) { cams[i].cullingMask = priorMasks[i]; cams[i].clearFlags = priorClear[i]; if (nopp && cams[i].GetComponent<UniversalAdditionalCameraData>() != null) cams[i].GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = priorPP[i]; }
                if (quality.HasValue) QualitySettings.SetQualityLevel(priorQuality, true);
            }
            return log;
        }
        static bool EditorUtility_IsDirty(UnityEngine.Object o) => UnityEditor.EditorUtility.IsDirty(o);
    }
}
