using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>#307 perf (SPEC-PERF-120): while the view camera stands in a baked sealed interior cell (InteriorSight307SO), the
    /// exterior cannot be seen, so the vegetation/grass renderers skip this camera and every layer is culled beyond the baked sight
    /// radius. Screen-identical by the bake's per-cell pixel proof. Runs after camera rigs (late execution order); leaving a sealed
    /// cell restores the camera's own cull distances and the renderers at once. Added at runtime by the session.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class InteriorSight307 : MonoBehaviour
    {
        Camera view;
        InteriorSight307SO[] sights = System.Array.Empty<InteriorSight307SO>();
        readonly List<CompactRebuildArtRenderer> arts = new List<CompactRebuildArtRenderer>();
        float nextScan; bool applied; float[] priorDistances;
        public bool Sealed => applied;
        public InteriorSight307SO Current { get; private set; }

        public void Configure(Camera camera, InteriorSight307SO[] baked)
        {
            Restore(); view = camera; sights = baked ?? System.Array.Empty<InteriorSight307SO>(); nextScan = 0f;
        }

        void LateUpdate()
        {
            if (view == null || sights.Length == 0) return;
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 2f; arts.Clear(); arts.AddRange(FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None)); }
            var eye = view.transform.position; InteriorSight307SO hit = null;
            for (int i = 0; i < sights.Length; i++) if (sights[i] != null && sights[i].IsSealed(eye)) { hit = sights[i]; break; }
            if (hit != null) Apply(hit); else Restore();
        }

        void Apply(InteriorSight307SO sight)
        {
            if (!applied) priorDistances = view.layerCullDistances;
            if (!applied || Current != sight)
            {
                var d = new float[32]; for (int i = 0; i < 32; i++) d[i] = sight.SightRadius;
                view.layerCullDistances = d;   // #307: planar layer culling (URP ignores layerCullSpherical and warned on every set; the bake used the same setting)
            }
            for (int i = 0; i < arts.Count; i++) if (arts[i] != null) arts[i].SuppressFor307 = view;
            applied = true; Current = sight;
        }

        void Restore()
        {
            if (!applied) return;
            if (view != null) view.layerCullDistances = priorDistances ?? new float[32];
            for (int i = 0; i < arts.Count; i++) if (arts[i] != null && arts[i].SuppressFor307 == view) arts[i].SuppressFor307 = null;
            applied = false; Current = null;
        }

        void OnDisable() => Restore();
    }
}
