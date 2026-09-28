using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    /// <summary>Opt-in diffuse probe fill for298 bodies. Keeps URP Lit PBR and scene lighting intact.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class FolkloreReadability298 : MonoBehaviour
    {
        public Renderer[] Targets = Array.Empty<Renderer>();
        // Persist the authored mode because MPBs are transient while renderer
        // CustomProvided can be serialized during candidate authoring.
        public LightProbeUsage[] RestoreUsage = Array.Empty<LightProbeUsage>();
        [Range(0, .5f)] public float DiffuseFill = .30f;
        public int AppliedRenderers { get; private set; }
        public string LastError { get; private set; }
        Renderer[] bound = Array.Empty<Renderer>();
        LightProbeUsage[] originalUsage = Array.Empty<LightProbeUsage>();
        MaterialPropertyBlock[] originalBlocks = Array.Empty<MaterialPropertyBlock>();
        MaterialPropertyBlock block;
        SphericalHarmonicsL2[] coefficients;
        float nextRefresh;

        void Bind()
        {
            bound ??= Array.Empty<Renderer>();
            bool same = bound.Length == Targets.Length && originalUsage != null && originalUsage.Length == bound.Length && originalBlocks != null && originalBlocks.Length == bound.Length;
            if (same) for (int i = 0; i < bound.Length; i++) if (bound[i] != Targets[i]) { same = false; break; }
            if (same) for (int i = 0; i < bound.Length; i++) if (originalBlocks[i] == null) { same = false; break; }
            if (same) return;
            Restore(); bound = (Renderer[])Targets.Clone();
            originalUsage = new LightProbeUsage[bound.Length]; originalBlocks = new MaterialPropertyBlock[bound.Length];
            for (int i = 0; i < bound.Length; i++)
            {
                var renderer = bound[i]; originalBlocks[i] = new MaterialPropertyBlock();
                if (renderer == null) continue;
                originalUsage[i] = RestoreUsage != null && RestoreUsage.Length == bound.Length ? RestoreUsage[i] : renderer.lightProbeUsage;
                renderer.GetPropertyBlock(originalBlocks[i]);
            }
        }

        public void Apply()
        {
            if (!isActiveAndEnabled) return;
            // Prefab loading and editor domain restoration can leave transient
            // fields null. Allocate Unity objects at use time on the main thread.
            block ??= new MaterialPropertyBlock();
            if (coefficients == null || coefficients.Length != 1) coefficients = new SphericalHarmonicsL2[1];
            Targets ??= Array.Empty<Renderer>(); Bind(); AppliedRenderers = 0; LastError = null;
            for (int i = 0; i < bound.Length; i++)
            {
                var renderer = bound[i]; if (renderer == null) { LastError = "Missing target renderer"; continue; }
                // Sample ordinary scene probes afresh: never add fill to our own
                // previous SH, and never alter global ambient/reflection data.
                var position = renderer.probeAnchor != null ? renderer.probeAnchor.position : renderer.bounds.center;
                LightProbes.GetInterpolatedProbe(position, renderer, out var sh);
                sh.AddAmbientLight(new Color(DiffuseFill, DiffuseFill, DiffuseFill, 1));
                coefficients[0] = sh;
                renderer.GetPropertyBlock(block);
                block.CopySHCoefficientArraysFrom(coefficients);
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                renderer.SetPropertyBlock(block); AppliedRenderers++;
            }
            nextRefresh = Time.unscaledTime + .25f;
        }

        public void Restore()
        {
            for (int i = 0; bound != null && i < bound.Length; i++)
            {
                if (bound[i] == null) continue;
                if (originalUsage != null && i < originalUsage.Length) bound[i].lightProbeUsage = originalUsage[i];
                else if (RestoreUsage != null && i < RestoreUsage.Length) bound[i].lightProbeUsage = RestoreUsage[i];
                var saved = originalBlocks != null && i < originalBlocks.Length ? originalBlocks[i] : null;
                bound[i].SetPropertyBlock(saved == null || saved.isEmpty ? null : saved);
            }
            bound = Array.Empty<Renderer>(); originalUsage = Array.Empty<LightProbeUsage>(); originalBlocks = Array.Empty<MaterialPropertyBlock>(); AppliedRenderers = 0;
        }
        void OnEnable() => Apply();
        void OnValidate() { if (isActiveAndEnabled) Apply(); }
        void LateUpdate() { if (Application.isPlaying && Time.unscaledTime >= nextRefresh) Apply(); }
        void OnDisable() => Restore();
    }
}
