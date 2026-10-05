using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Oheangbu.App.SpellVFX120
{
    // This file needs "Unity.RenderPipelines.Core.Runtime" in Oheangbu.App.asmdef (RenderGraph / Blitter live there). It is staged
    // apart from the rest (Stage308_deploy/_NeedsAsmdef) and deployed together with the asmdef copy next to it. Without it the
    // director still compiles and runs: CreatePass has no body, so there is no full-screen pass and no hit flicker (burst, residue,
    // footprints and the HUD globals are unaffected).
    public sealed partial class ImpactFrameDirector308
    {
        partial void CreatePass(ref IImpactPass308 pass) { pass = new ImpactFramePass308(); }
    }

    // SPEC-SPELL-DEPLOY-308 sections 7 / 8 / 12: the one pass of the deploy layer. ImpactFrameDirector308 enqueues it on the base
    // game camera ONLY on frames that need it (impact frame, hit flicker, flood), so a normal frame costs nothing and
    // Renderer297.asset is not edited. Injection = AfterRenderingPostProcessing: the colour is already tone-mapped LDR and
    // the pass only rearranges values - it cannot become emission.
    //   full-screen forms (shader passes 0-4): the colour is copied to a temporary target and drawn BACK onto the active colour.
    //     The camera colour is never swapped for a graph texture: in a camera stack (the view-model Overlay camera follows the
    //     base camera) URP hands the next camera its own persistent colour buffer, so a swapped-in temporary would be dropped
    //     and the frame would never reach the screen (UniversalRendererRenderGraph: nextRenderGraphCameraColorHandle).
    //   Mobile local form (pass 5): an invert quad drawn straight onto the colour, no copy
    //   hit flicker: the target's renderers drawn again with a flat value material (their own materials are never touched)
    public sealed class ImpactFramePass308 : ScriptableRenderPass, IImpactPass308
    {
        public const int MaxFlickerRenderers = 128;

        private sealed class BlitData { public TextureHandle Source; public Material Material; public int Pass; }
        private sealed class DrawData
        {
            public Material Quad; public int QuadPass;
            public Renderer[] Renderers; public Material[] Materials; public int[] SubMeshes; public int Count;
            public Matrix4x4 View, Projection;
        }

        private Material _material;
        private int _fullPass = -1, _quadPass = -1;
        private readonly Renderer[] _renderers = new Renderer[MaxFlickerRenderers];
        private readonly Material[] _materials = new Material[MaxFlickerRenderers];
        private readonly int[] _subMeshes = new int[MaxFlickerRenderers];
        private int _count;

        public int FlickerCount => _count;
        public bool HasWork => (_material != null && (_fullPass >= 0 || _quadPass >= 0)) || _count > 0;
        public int RecordedFull { get; private set; }
        public int RecordedDraw { get; private set; }
        public int SkippedBackBuffer { get; private set; }

        public ImpactFramePass308()
        {
            // exactly this event: URP keeps post-processing in an intermediate texture when a pass sits here
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            profilingSampler = new ProfilingSampler("ImpactFrame308");
        }

        public void Setup(Material material, int fullPass, int quadPass)
        {
            _material = material; _fullPass = material != null ? fullPass : -1; _quadPass = material != null ? quadPass : -1;
        }

        public void ClearFlicker() { for (int i = 0; i < _count; i++) { _renderers[i] = null; _materials[i] = null; } _count = 0; }

        public void AddFlicker(Renderer renderer, Material flat, int subMeshes)
        {
            if (renderer == null || flat == null || _count >= MaxFlickerRenderers) return;
            _renderers[_count] = renderer; _materials[_count] = flat; _subMeshes[_count] = Mathf.Clamp(subMeshes, 1, 8); _count++;
        }

        public bool Enqueue(Camera camera)
        {
            if (camera == null || !HasWork) return false;
            // a camera without its own data (the scene view) renders with the pipeline asset's default renderer
            ScriptableRenderer renderer = camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) ? data.scriptableRenderer
                : UniversalRenderPipeline.asset != null ? UniversalRenderPipeline.asset.scriptableRenderer : null;
            if (renderer == null) return false;
            renderer.EnqueuePass(this);
            return true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (resources.isActiveTargetBackBuffer) { SkippedBackBuffer++; return; }   // nothing to read from
            TextureHandle colour = resources.activeColorTexture;
            if (!colour.IsValid()) return;

            if (_material != null && _fullPass >= 0)
            {
                var desc = renderGraph.GetTextureDesc(colour);
                desc.name = "_OhImpactCopy308"; desc.clearBuffer = false; desc.msaaSamples = MSAASamples.None;
                TextureHandle copy = renderGraph.CreateTexture(desc);
                using (var builder = renderGraph.AddRasterRenderPass<BlitData>("ImpactCopy308", out var data, profilingSampler))
                {
                    data.Source = colour; data.Material = null; data.Pass = 0;
                    builder.UseTexture(colour);
                    builder.SetRenderAttachment(copy, 0);
                    builder.SetRenderFunc(static (BlitData d, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), 0f, false));
                }
                using (var builder = renderGraph.AddRasterRenderPass<BlitData>("ImpactFrame308", out var data, profilingSampler))
                {
                    data.Source = copy; data.Material = _material; data.Pass = _fullPass;
                    builder.UseTexture(copy);
                    if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                    builder.SetRenderAttachment(colour, 0);
                    builder.SetRenderFunc(static (BlitData d, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, d.Pass));
                }
                RecordedFull++;
            }

            bool quad = _material != null && _quadPass >= 0;
            if (!quad && _count == 0) return;
            using (var builder = renderGraph.AddRasterRenderPass<DrawData>("ImpactDraw308", out var data, profilingSampler))
            {
                data.Quad = quad ? _material : null; data.QuadPass = _quadPass;
                data.Renderers = _renderers; data.Materials = _materials; data.SubMeshes = _subMeshes; data.Count = _count;
                data.View = cameraData.GetViewMatrix();
                // the colour here is an intermediate texture, never the back buffer
                data.Projection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true);
                if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                builder.SetRenderAttachment(colour, 0);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    if (d.Quad != null) cmd.DrawProcedural(Matrix4x4.identity, d.Quad, d.QuadPass, MeshTopology.Triangles, 3, 1);
                    if (d.Count <= 0) return;
                    cmd.SetViewProjectionMatrices(d.View, d.Projection);
                    for (int i = 0; i < d.Count; i++)
                    {
                        var renderer = d.Renderers[i];
                        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        for (int s = 0; s < d.SubMeshes[i]; s++) cmd.DrawRenderer(renderer, d.Materials[i], s, 0);
                    }
                });
                RecordedDraw++;
            }
        }
    }
}
