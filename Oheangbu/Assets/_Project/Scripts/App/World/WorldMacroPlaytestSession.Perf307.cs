using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        // #307 perf helpers that follow the session's camera (SPEC-PERF-120). Screen-identical by construction; see each component.
        // (A lantern shadow budget was tried here and removed: with fine shadow cells it saved ~0 ms and could re-pack the atlas.)
        InteriorSight307 interiorSight;
        public InteriorSight307 InteriorSight => interiorSight;
        void BindPerf307()
        {
            if (Walker == null || Walker.ViewCamera == null) return;
            var baked = Resources.LoadAll<InteriorSight307SO>("Perf307");   // baked sealed cave cells (InteriorSight307BakeTool)
            if (baked.Length == 0) return;
            interiorSight = GetComponent<InteriorSight307>() ?? gameObject.AddComponent<InteriorSight307>();
            interiorSight.hideFlags = HideFlags.DontSave;
            interiorSight.Configure(Walker.ViewCamera, baked);
        }
    }
}
