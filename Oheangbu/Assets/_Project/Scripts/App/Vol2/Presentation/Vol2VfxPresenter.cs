using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The scene side of the vol.2 spell look. It owns no timer that decides a lifetime: every frame it asks the model to tick on the scope clock (<see cref="Vol2VfxModel.Tick"/>)
    /// and lets the legacy bridge's stage sweep its finished bodies. The recipes (the legacy bridge, the free showcase) show each cast with their own Unity objects; the model's events
    /// drive spawning and retiring. The debug presenter is told to skip the casts the model covers; everything else still shows the debug lines.
    /// (2026-10-26: the pooled ink-mesh view of the rejected slice 9a / NB20 / stroke20 recipes was removed with them - Docs/Research/vfx120/cleanup_code_20261026.md.)</summary>
    [DefaultExecutionOrder(200)]
    public sealed class Vol2VfxPresenter : MonoBehaviour
    {
        Vol2CastCoordinator _coordinator;
        Vol2VfxModel _model;
        Vol2VfxCatalogue _catalogue;

        public bool Enabled = true;
        public Vol2VfxModel Model => _model;
        public Vol2CastCoordinator Coordinator => _coordinator;
        public Vol2VfxCatalogue Catalogue => _catalogue;
        /// <summary>Instances spawned so far (the counterpart of the debug presenter's Drawn).</summary>
        public int Drawn => _model != null ? _model.SpawnedCount : 0;
        /// <summary>The scope-clock time of the last Step: what the pictures on screen show (a capture reads it).</summary>
        public long LastStepUs { get; private set; }

        /// <summary>Builds the presenter under <paramref name="parent"/>. Null (and a warning) when no catalogue can be had.</summary>
        public static Vol2VfxPresenter Attach(Vol2CastCoordinator coordinator, Transform parent, Vol2VfxCatalogue catalogue = null, Vol2DebugPresenter debug = null, HideFlags flags = HideFlags.None,
            CombatLoopWiring wiring = null)
        {
            catalogue = catalogue ?? Vol2VfxCatalogue.Load();
            if (catalogue == null) { Debug.LogWarning("[Vol2Vfx] no catalogue: the debug lines stay"); return null; }
            var go = new GameObject("Vol2VfxPresenter") { hideFlags = flags };
            if (parent != null) go.transform.SetParent(parent, false);
            var p = go.AddComponent<Vol2VfxPresenter>();
            p._coordinator = coordinator; p._catalogue = catalogue;
            p._model = new Vol2VfxModel(coordinator, catalogue);
            // the brush tip / grip of the player's great brush (the bridge's tip ink, the showcase's ignition); searched lazily, a bounded number of times
            p._model.BrushTip = () => p.Rig() is var r && r != null && r.WorldTip != null ? r.WorldTip.position : (Vector3?)null;
            p._model.BrushGrip = () => p.Rig() is var r && r != null && r.WorldBrushRoot308 != null ? r.WorldBrushRoot308.position : (Vector3?)null;
            if (debug != null) debug.Skip = log => p.Enabled && p._model.Covers(log);
            // D308-150 V9: the legacy bridge's Unity side (the old visuals of the glyphs); in Play only - an edit-mode fixture binds its own recording stage
            if (Application.isPlaying)
            {
                if (wiring == null) wiring = FindFirstObjectByType<CombatLoopWiring>();
                p._legacy = new Vol2LegacyStage(coordinator, wiring, p._model.BrushTip);
                p._model.Legacy = p._legacy;
            }
            return p;
        }

        Vol2LegacyStage _legacy;
        /// <summary>D308-150 V9: the legacy bridge's stage (null in edit mode).</summary>
        public Vol2LegacyStage Legacy => _legacy;

        World.WorldMacroPlayerGestureRig _rig;
        int _rigSearches;
        World.WorldMacroPlayerGestureRig Rig()
        {
            if (_rig != null) return _rig;
            if (_rigSearches >= 40 || !Application.isPlaying) return null;
            _rigSearches++;
            _rig = FindFirstObjectByType<World.WorldMacroPlayerGestureRig>();
            return _rig;
        }

        void OnDestroy() { Release(); }

        /// <summary>Frees the model's subscriptions and the bridge stage (what OnDestroy does; public because OnDestroy does not run for an edit-mode fixture).</summary>
        public void Release()
        {
            if (_model != null) { _model.Dispose(); _model = null; }
            _legacy?.Dispose(); _legacy = null;
        }

        void Update() { Step(); }

        /// <summary>One frame: tick the model on the scope clock, let the bridge stage sweep. Public so an edit-mode fixture can drive it.</summary>
        public void Step()
        {
            if (_model == null || _coordinator == null) return;
            long now = _coordinator.Services.Clock.NowUs;
            LastStepUs = now;
            _model.Tick(now);
            _legacy?.Sweep();
            // 2026-10-26 fog fix: the recipes' own pieces (the free showcase roots live under this transform) draw after the realm fog when transparent (VfxAfterFog308)
            VfxAfterFog308.Keep(transform, ref _afterFogCount, ref _afterFogTick);
        }
        int _afterFogCount = -1, _afterFogTick;

        /// <summary>A scope reset: the coordinator's AbortAll already retires the instances through the model; this also clears the bridge's old bodies at once.</summary>
        public void ClearAll()
        {
            if (_model == null) return;
            _legacy?.ClearAll();          // D308-150 V9: the old bodies of the bridge go too (a finished cast's body may still be running its own life)
        }
    }
}
