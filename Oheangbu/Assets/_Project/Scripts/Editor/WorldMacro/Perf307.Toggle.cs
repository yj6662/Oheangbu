using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class Perf307
    {
        /// <summary>ab: toggle with the finish297 key syntax, applied to the live assets in memory only: no SetDirty, no SaveAssets.
        /// Set(true) captures the prior values on first use and applies B; Set(false) puts A back. Restore is also called on Play exit.
        ///   feature297:&lt;RendererFeature&gt;:&lt;0|1&gt;   one feature of Renderer297 (SetActive)
        ///   urp:shadow=&lt;m&gt;,cascades=&lt;n&gt;,ssao=&lt;0|1&gt;   shadow/cascades on the active tier's pipeline asset, ssao = SSAO297 feature
        /// forwardplus is refused (a rendering-mode change needs a renderer rebuild, not an in-Play A/B).
        ///   sky308:&lt;0|1&gt;   #308 (SPEC-REGION-SKY-308 AC-S19): every live WorldLookDriver's sky source swapped in memory only
        ///                     (WorldLookDriver.SetSkyMaterialOverride, [NonSerialized]: nothing dirtied or saved); B = 1: RealmInkSky308.mat,
        ///                     B = 0: the step-1 sky (scene-ledger L4 "before"). The driver rebuilds its transient copy (snap) on a swap.</summary>
        sealed class Toggle307
        {
            public string Spec = "";
            UniversalRendererData data;
            ScriptableRendererFeature feature; bool featureValue, featurePrior;
            ScriptableRendererFeature ssao; bool? ssaoValue; bool ssaoPrior;
            float? shadow; int? cascades; float shadowPrior; int cascadesPrior;
            UniversalRenderPipelineAsset asset;
            bool captured;
            float? farClip; readonly Dictionary<Camera, float> farPrior = new Dictionary<Camera, float>();
            int? artPath; readonly Dictionary<Oheangbu.App.World.CompactRebuildArtRenderer, int> artPrior = new Dictionary<Oheangbu.App.World.CompactRebuildArtRenderer, int>();
            bool? sky308; Material sky308Material;
            bool? wash308; readonly Dictionary<Oheangbu.App.World.EventWashDriver308, bool> washPrior = new Dictionary<Oheangbu.App.World.EventWashDriver308, bool>();
            readonly Dictionary<Oheangbu.App.WorldLookDriver, Material> skyPrior = new Dictionary<Oheangbu.App.WorldLookDriver, Material>();
            readonly Dictionary<Oheangbu.App.WorldLookDriver, Material> skyB = new Dictionary<Oheangbu.App.WorldLookDriver, Material>();
            readonly List<string> log = new List<string>();
            readonly Dictionary<Object, bool> dirtyBefore = new Dictionary<Object, bool>();

            public static Toggle307 Parse(string spec, out string why)
            {
                why = null; var t = new Toggle307 { Spec = spec };
                t.data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Renderer297);
                if (t.data == null) { why = "renderer data missing " + Renderer297; return null; }
                if (spec.StartsWith("feature297:", StringComparison.Ordinal))
                {
                    var a = spec.Substring(11).Split(':');
                    if (a.Length != 2 || a[1] != "0" && a[1] != "1") { why = "feature297:<name>:<0|1>"; return null; }
                    t.feature = t.data.rendererFeatures.FirstOrDefault(x => x != null && x.name == a[0]);
                    if (t.feature == null) { why = "no feature " + a[0] + "; have " + string.Join(",", t.data.rendererFeatures.Where(x => x != null).Select(x => x.name)); return null; }
                    t.featureValue = a[1] == "1"; t.Remember(t.feature); return t;
                }
                if (spec.StartsWith("urp:", StringComparison.Ordinal))
                {
                    foreach (var part in spec.Substring(4).Split(','))
                    {
                        var kv = part.Split('='); if (kv.Length != 2) { why = "urp:<key>=<value>[,...]"; return null; }
                        string k = kv[0].Trim(); string v = kv[1].Trim();
                        if (k == "shadow" && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float m) && m >= 0) t.shadow = m;
                        else if (k == "cascades" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= 4) t.cascades = n;
                        else if (k == "ssao" && (v == "0" || v == "1"))
                        {
                            t.ssao = t.data.rendererFeatures.FirstOrDefault(x => x != null && x.name == "SSAO297");
                            if (t.ssao == null) { why = "SSAO297 feature missing on Renderer297"; return null; }
                            t.ssaoValue = v == "1"; t.Remember(t.ssao);
                        }
                        else if (k == "forwardplus") { why = "forwardplus needs a renderer rebuild; measure it as two separate baselines instead"; return null; }
                        else { why = "urp key '" + part + "' (shadow=<m>, cascades=<1-4>, ssao=<0|1>)"; return null; }
                    }
                    if (!t.shadow.HasValue && !t.cascades.HasValue && !t.ssaoValue.HasValue) { why = "urp: nothing to toggle"; return null; }
                    return t;
                }
                if (spec.StartsWith("farclip:", StringComparison.Ordinal))
                {
                    // #307 cave culling experiment: every enabled game camera's farClipPlane (URP shadows follow min(shadowDistance, far))
                    if (!float.TryParse(spec.Substring(8), NumberStyles.Float, CultureInfo.InvariantCulture, out float far) || far <= 1) { why = "farclip:<metres>"; return null; }
                    t.farClip = far; return t;
                }
                if (spec.StartsWith("artpath:", StringComparison.Ordinal))
                {
                    // #307: CompactRebuildArtRenderer.CullPath307 on every live renderer (0 = S3 path, 1 = S8 screen-identical path)
                    string v = spec.Substring(8);
                    if (v != "0" && v != "1") { why = "artpath:<0|1>"; return null; }
                    t.artPath = v == "1" ? 1 : 0; return t;
                }
                if (spec.StartsWith("wash308:", StringComparison.Ordinal))
                {
                    // #308 (SPEC-EVENT-WASH-308 AC-W14): every live EventWashDriver308 switched on or off in memory only (its OnDisable
                    // clears the wash globals, OnEnable writes them again). A = the scene as it is; nothing is saved.
                    string v = spec.Substring(8);
                    if (v != "0" && v != "1") { why = "wash308:<0|1>"; return null; }
                    t.wash308 = v == "1"; return t;
                }
                if (spec.StartsWith("sky308:", StringComparison.Ordinal))
                {
                    string v = spec.Substring(7);
                    if (v != "0" && v != "1") { why = "sky308:<0|1>"; return null; }
                    if (v == "1")
                    {
                        if (RegionSky308.Sky2Shader(out string shaderWhy) == null) { why = shaderWhy; return null; }
                        t.sky308Material = AssetDatabase.LoadAssetAtPath<Material>(RegionSky308.Sky2MatPath);
                        if (t.sky308Material == null) { why = "RealmInkSky308.mat missing (RegionSky308 data:apply:step=2)"; return null; }
                    }
                    t.sky308 = v == "1"; return t;
                }
                why = "toggle must be artpath:<0|1>, sky308:<0|1>, wash308:<0|1>, feature297:<name>:<0|1> or urp:shadow=<m>[,cascades=<n>][,ssao=<0|1>]";
                return null;
            }

            void Remember(Object o) { if (o != null && !dirtyBefore.ContainsKey(o)) dirtyBefore.Add(o, EditorUtility.IsDirty(o)); }

            void Capture()
            {
                if (captured) return;
                if (feature != null) featurePrior = feature.isActive;
                if (ssao != null) ssaoPrior = ssao.isActive;
                if (shadow.HasValue || cascades.HasValue)
                {
                    asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    if (asset == null) log.Add("no active URP asset: shadow/cascades not toggled");
                    else { Remember(asset); shadowPrior = asset.shadowDistance; cascadesPrior = asset.shadowCascadeCount; }
                }
                if (farClip.HasValue) foreach (var c in Camera.allCameras) if (c.cameraType == CameraType.Game) farPrior[c] = c.farClipPlane;
                if (artPath.HasValue) foreach (var r in Object.FindObjectsByType<Oheangbu.App.World.CompactRebuildArtRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) artPrior[r] = r.CullPath307;
                if (wash308.HasValue)
                {
                    foreach (var w in Object.FindObjectsByType<Oheangbu.App.World.EventWashDriver308>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) washPrior[w] = w.enabled;
                    if (washPrior.Count == 0) log.Add("wash308: no live EventWashDriver308 (nothing toggled)");
                }
                if (sky308.HasValue)
                {
                    foreach (var d in Object.FindObjectsByType<Oheangbu.App.WorldLookDriver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    {
                        skyPrior[d] = d.SkyMaterialOverride;
                        var b = sky308.Value ? sky308Material : RegionSky308.Step1SkyMaterial(d.gameObject.scene.path, d.SkyMaterial);
                        if (b == null) log.Add("sky308:0 — no step-1 sky known for " + d.gameObject.scene.path + " (B = A)");
                        skyB[d] = b != null ? b : d.SkyMaterialOverride;
                    }
                    if (skyPrior.Count == 0) log.Add("sky308: no live WorldLookDriver (nothing toggled)");
                }
                captured = true;
                log.Add("A = " + State());
            }

            string State()
            {
                var parts = new List<string>();
                if (feature != null) parts.Add(feature.name + "=" + (feature.isActive ? 1 : 0));
                if (ssao != null) parts.Add("SSAO297=" + (ssao.isActive ? 1 : 0));
                if (farClip.HasValue) parts.Add("far=" + string.Join("/", farPrior.Keys.Where(c => c != null).Select(c => c.farClipPlane.ToString("0", CultureInfo.InvariantCulture))));
                if (artPath.HasValue) parts.Add("artpath=" + string.Join("/", artPrior.Keys.Where(r => r != null).Select(r => r.CullPath307)));
                if (wash308.HasValue) parts.Add("wash308=" + string.Join("/", washPrior.Keys.Where(w => w != null).Select(w => (w.enabled ? 1 : 0) + "(zones " + w.ActiveZones + ", volumes " + w.ActiveVolumes + ")")));
                if (sky308.HasValue) parts.Add("sky308=" + string.Join("/", skyPrior.Keys.Where(d => d != null).Select(d => d.SkyMaterial != null ? d.SkyMaterial.name : "null")));
                if (asset != null) parts.Add(asset.name + ".shadow=" + asset.shadowDistance.ToString("0.##", CultureInfo.InvariantCulture) + " cascades=" + asset.shadowCascadeCount);
                return string.Join(" ", parts);
            }

            public void Set(bool b)
            {
                Capture();
                if (feature != null) feature.SetActive(b ? featureValue : featurePrior);
                if (ssao != null) ssao.SetActive(b ? ssaoValue.Value : ssaoPrior);
                if (farClip.HasValue) foreach (var kv in farPrior) if (kv.Key != null) kv.Key.farClipPlane = b ? Mathf.Min(kv.Value, farClip.Value) : kv.Value;
                if (artPath.HasValue) foreach (var kv in artPrior) if (kv.Key != null) kv.Key.CullPath307 = b ? artPath.Value : kv.Value;
                if (sky308.HasValue) foreach (var kv in skyPrior) if (kv.Key != null) kv.Key.SetSkyMaterialOverride(b ? skyB[kv.Key] : kv.Value);
                if (wash308.HasValue) foreach (var kv in washPrior) if (kv.Key != null) kv.Key.enabled = b ? wash308.Value : kv.Value;
                if (asset != null)
                {
                    if (shadow.HasValue) asset.shadowDistance = b ? shadow.Value : shadowPrior;
                    if (cascades.HasValue) asset.shadowCascadeCount = b ? cascades.Value : cascadesPrior;
                }
            }

            public bool Restore(out string detail)
            {
                if (!captured) { detail = "toggle never applied"; return true; }
                Set(false);
 bool ok = farPrior.All(kv => kv.Key == null || Mathf.Approximately(kv.Key.farClipPlane, kv.Value)) && artPrior.All(kv => kv.Key == null || kv.Key.CullPath307 == kv.Value) && (feature == null || feature.isActive == featurePrior) && (ssao == null || ssao.isActive == ssaoPrior) &&
                          skyPrior.All(kv => kv.Key == null || kv.Key.SkyMaterialOverride == kv.Value) &&
                          washPrior.All(kv => kv.Key == null || kv.Key.enabled == kv.Value) &&
                          (asset == null || Mathf.Approximately(asset.shadowDistance, shadowPrior) && asset.shadowCascadeCount == cascadesPrior);
                var dirty = dirtyBefore.Where(kv => kv.Key != null && EditorUtility.IsDirty(kv.Key) && !kv.Value).Select(kv => kv.Key.name).ToList();
                if (dirty.Count > 0) log.Add("became dirty (values restored; not saved by this tool): " + string.Join(",", dirty));
                detail = Spec + " -> " + State() + (log.Count > 0 ? " | " + string.Join(" | ", log) : "");
                return ok;
            }
        }
    }
}
