using Unity.Profiling;

namespace Oheangbu.App
{
    // #307 phase 0 (Tools/Unity/Plan307/PERF_DESIGN.md C.0): named CPU scopes read by Perf307 / PerfRoute307 and shown in the
    // Profiler. Measurement only: a scope never changes what the wrapped code does. Begin/End are ENABLE_PROFILER code, so a
    // non-development player pays next to nothing. Static readonly markers hold no Play-session state (no reset needed with
    // domain reload disabled). Names are also listed in Art/Performance/Perf307/routes.json (customMarkers).
    public static class Perf307Markers
    {
        public static readonly ProfilerMarker ArtCollect = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Art.Collect");
        public static readonly ProfilerMarker ArtSubmit = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Art.Submit");
        // #307 S8 collect phases (main-thread classify, worker tests, in-order replay into the streams)
        public static readonly ProfilerMarker ArtClassify = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Art.Classify");
        public static readonly ProfilerMarker ArtTests = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Art.Tests");
        public static readonly ProfilerMarker ArtReplay = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Art.Replay");
        public static readonly ProfilerMarker ArtSort = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Art.Sort");   // #307 SortChunks307
        public static readonly ProfilerMarker Grass = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Grass");
        public static readonly ProfilerMarker SessionFocus = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Session.Focus");
        public static readonly ProfilerMarker SessionSafeFeet = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Session.SafeFeet");
        public static readonly ProfilerMarker SessionSave = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Session.Save");
        public static readonly ProfilerMarker SessionCull = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Session.Cull");
        public static readonly ProfilerMarker RigEvaluate = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Rig.Evaluate");
        public static readonly ProfilerMarker Gesture = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.Gesture");
        public static readonly ProfilerMarker FootPlacement = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.FootPlacement");
        public static readonly ProfilerMarker ColliderSync = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.ColliderSync");
        public static readonly ProfilerMarker NaturalSolidsQuery = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.NaturalSolids.Query");
        public static readonly ProfilerMarker NpcJob = new ProfilerMarker(ProfilerCategory.Scripts, "Oh.NpcJob");
    }
}
