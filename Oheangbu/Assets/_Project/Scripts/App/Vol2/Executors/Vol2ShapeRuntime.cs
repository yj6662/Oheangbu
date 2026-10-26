using System;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using VContainer;

namespace Oheangbu.App
{
    /// <summary>The slice 3 services of one combat scope, bundled so the installer, the lab runner and the presenter hold ONE object:
    /// the cast coordinator and what it was built from. Created by <see cref="Vol2Installer"/> for a Vol2 scope only; in Legacy308
    /// <see cref="Vol2Services.Shapes"/> is null and nothing of this exists. The main agent re-binds the ports here
    /// (<c>Coordinator.Damage</c>, <c>.Ink</c>, <c>.Status</c>, <c>.ParryPort</c>) when slice 2's real entries are in.</summary>
    public sealed class Vol2ShapeRuntime : IDisposable
    {
        public Vol2CastCoordinator Coordinator { get; }
        public Vol2TargetRegistry Registry => Coordinator.Registry;
        /// <summary>Slice 4: every residue, mark and carrier of the scope. Slice 8 (reactions) reads and commits through it.</summary>
        public Vol2MaterialRegistry Materials => Coordinator.Materials;
        public Vol2SwingSignal Swings => Coordinator.Swings;
        public IVol2Caster Caster { get; }
        /// <summary>Non-null when the ink comes from the combat InkPool (the lab refills it between rows).</summary>
        public Vol2InkPoolSpender PoolInk { get; }
        public GameObject Host { get; private set; }
        /// <summary>S5-b slice 8 (Vol2 scopes only): the field gates (the coordinator's field port), the world's materials and the 뭄 dash block. Null in a fixture runtime.</summary>
        public Vol2FieldGateAdapter Field { get; private set; }
        public Vol2WorldMaterials World { get; private set; }
        public Vol2MovementBlock Block { get; private set; }
        /// <summary>S5-b slice 9 (D308-150 V2): the swing driver of the scope - the 갈무리 and the ㅓ contacts of a real brush swing. Bound by Vol2DefenceInstaller.BindSeams (Vol2 scopes
        /// only); null in Legacy308 and in a fixture runtime until the fixture makes one.</summary>
        public Vol2SwingDriver Swing { get; set; }
        /// <summary>The TEMPORARY debug presenter (null in a fixture). Its Drawn counter is how the lab knows the shapes were drawn.</summary>
        public Vol2DebugPresenter Presenter { get; private set; }
        /// <summary>S5-b slice 9a: the vol.2 spell look (null when no catalogue / shader). The debug presenter skips the casts it covers.</summary>
        public Vol2VfxPresenter Vfx { get; private set; }

        Vol2ShapeRuntime(Vol2CastCoordinator coordinator, IVol2Caster caster, Vol2InkPoolSpender poolInk)
        { Coordinator = coordinator; Caster = caster; PoolInk = poolInk; }

        /// <summary>Builds the runtime with explicit parts (fixtures use this with fakes; no scene object is made).</summary>
        public static Vol2ShapeRuntime Create(Vol2Services services, Vol2TargetRegistry registry, IVol2Caster caster, IVol2Obstruction wall,
            IVol2DamageSink damage, IVol2StatusSink status, IVol2InkSpender ink, Vol2ShapeTuning tuning = null,
            Vol2VerbRegistry verbs = null, Vol2MaterialRegistry materials = null, Vol2VerbTuning verbTuning = null)
        {
            var coordinator = new Vol2CastCoordinator(services, registry, caster, wall, damage, status, ink, tuning, new Vol2SwingSignal(), verbs, materials, verbTuning);
            var runtime = new Vol2ShapeRuntime(coordinator, caster, ink as Vol2InkPoolSpender);
            services.Shapes = runtime;
            return runtime;
        }

        /// <summary>The scope hook: wiring, ink pool and physics wall check from the container, the TEMPORARY sinks, a ticker and the debug presenter in
        /// the scene. Vol2 mode only.</summary>
        public static Vol2ShapeRuntime CreateForScope(IObjectResolver resolver, Vol2Services services)
        {
            var wiring = resolver.Resolve<CombatLoopWiring>();
            InkPool pool = null; resolver.TryResolve(out pool);
            var registry = new Vol2TargetRegistry { SceneSource = () => wiring.SummonTargets };
            var caster = new Vol2WiringCaster(wiring);
            var wall = new Vol2PhysicsObstruction(wiring.SummonPlayer);
            IVol2InkSpender ink = pool != null
                ? (IVol2InkSpender)new Vol2InkPoolSpender(pool, services.Rulebook.Numbers.Get("ink.capacity"))
                : new Vol2RecordingInkSpender();
            var runtime = Create(services, registry, caster, wall, new Vol2TempEnemyDamageSink(Vol2Installer.TempHpPerActionUnit), new Vol2TempStatusSink(), ink);
            var go = new GameObject("Vol2ShapeRuntime");
            runtime.Host = go;
            go.AddComponent<Vol2ShapeRuntimeTicker>().Bind(runtime);
            runtime.Presenter = Vol2DebugPresenter.Attach(runtime.Coordinator, go.transform);
            runtime.Vfx = Vol2VfxPresenter.Attach(runtime.Coordinator, go.transform, null, runtime.Presenter, HideFlags.None, wiring);      // slice 9a (+ D308-150 V9 legacy bridge)
            // D308-150 V13: the cast camera (A softened, the default of every vol.2 scope) lives with the scope; presentation only, Play only
            if (runtime.Vfx != null && Application.isPlaying) Vol2CastCameraDirector.Ensure(go.transform);
            // TE-S3-6 (D308-150 V9, TEST, the user decides on return): the old feel launches a waiting cast at the swing's apex. ON in the spell lab scope only (the only
            // scene a Vol2 scope runs in today: Vol2LabRequestFile); the apex seconds are the live swing profile's (single / area), so the two never drift apart.
            var tuning = runtime.Coordinator.Tuning;
            tuning.LaunchAtApex = wiring != null && wiring.gameObject.scene.path == Vol2LabRequestFile.LabScenePath;
            var swing = wiring != null ? wiring.SwingProfile308 : null;
            if (swing != null) { tuning.PathApexSeconds = swing.AttackSingle.ApexSeconds; tuning.FanApexSeconds = swing.AttackArea.ApexSeconds; }
            // NB20: a successful parry (the combat wiring's verdict, never decided here) lets the ㅓ look splash at the impact point, on the scope clock
            if (runtime.Vfx != null && wiring != null)
            {
                Action<ParryOutcome, Oheangbu.Core.Domain.Element, Vector3> onParry = (outcome, element, point) =>
                {
                    if (outcome == ParryOutcome.Success && runtime.Vfx != null && runtime.Vfx.Model != null) runtime.Vfx.Model.Parried(point, services.Clock.NowUs);
                };
                wiring.ParryResolved += onParry;
                runtime._unbind = () => wiring.ParryResolved -= onParry;
            }
            // slice 8: reactions on, the field gates bound, the world's materials and the dash block. All of this exists only in a Vol2 scope: CreateForScope is never reached in Legacy308.
            runtime.Coordinator.Reactions.Enabled = true;
            runtime.Field = new Vol2FieldGateAdapter(caster, services.Clock, runtime.Coordinator.Reactions.Tuning) { Targets = () => runtime.Coordinator.Targets };
            runtime.Coordinator.Field = runtime.Field;
            runtime.World = new Vol2WorldMaterials(runtime.Materials, services.Clock, runtime.Coordinator.Reactions.Tuning);
            runtime.Block = new Vol2MovementBlock(runtime.Materials, runtime.Coordinator.Reactions.Tuning);
            go.AddComponent<Vol2WorldBinder>().Bind(runtime.World);
            return runtime;
        }

        Action _unbind;
        public void Dispose() { _unbind?.Invoke(); _unbind = null; Field?.Clear(); Coordinator.Dispose(); }
    }

    /// <summary>Ticks the coordinator once per frame with the scaled delta, and lets go of the event subscriptions when the scene object dies.</summary>
    public sealed class Vol2ShapeRuntimeTicker : MonoBehaviour
    {
        Vol2ShapeRuntime _runtime;
        public void Bind(Vol2ShapeRuntime runtime) { _runtime = runtime; }
        void Update()
        {
            if (_runtime == null) return;
            _runtime.Coordinator.Tick(Time.deltaTime);
            long now = _runtime.Coordinator.Services.Clock.NowUs;
            _runtime.Field?.Tick(now);
            _runtime.Block?.Tick(now, _runtime.Coordinator.Targets, Time.time);
        }
        void OnDestroy() { _runtime?.Dispose(); _runtime = null; }
    }
}
