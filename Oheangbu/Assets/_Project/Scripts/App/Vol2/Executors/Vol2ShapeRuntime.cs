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
        public Vol2SwingSignal Swings => Coordinator.Swings;
        public IVol2Caster Caster { get; }
        /// <summary>Non-null when the ink comes from the combat InkPool (the lab refills it between rows).</summary>
        public Vol2InkPoolSpender PoolInk { get; }
        public GameObject Host { get; private set; }
        /// <summary>The TEMPORARY debug presenter (null in a fixture). Its Drawn counter is how the lab knows the shapes were drawn.</summary>
        public Vol2DebugPresenter Presenter { get; private set; }

        Vol2ShapeRuntime(Vol2CastCoordinator coordinator, IVol2Caster caster, Vol2InkPoolSpender poolInk)
        { Coordinator = coordinator; Caster = caster; PoolInk = poolInk; }

        /// <summary>Builds the runtime with explicit parts (fixtures use this with fakes; no scene object is made).</summary>
        public static Vol2ShapeRuntime Create(Vol2Services services, Vol2TargetRegistry registry, IVol2Caster caster, IVol2Obstruction wall,
            IVol2DamageSink damage, IVol2StatusSink status, IVol2InkSpender ink, Vol2ShapeTuning tuning = null)
        {
            var coordinator = new Vol2CastCoordinator(services, registry, caster, wall, damage, status, ink, tuning, new Vol2SwingSignal());
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
            return runtime;
        }

        public void Dispose() { Coordinator.Dispose(); }
    }

    /// <summary>Ticks the coordinator once per frame with the scaled delta, and lets go of the event subscriptions when the scene object dies.</summary>
    public sealed class Vol2ShapeRuntimeTicker : MonoBehaviour
    {
        Vol2ShapeRuntime _runtime;
        public void Bind(Vol2ShapeRuntime runtime) { _runtime = runtime; }
        void Update() { _runtime?.Coordinator.Tick(Time.deltaTime); }
        void OnDestroy() { _runtime?.Dispose(); _runtime = null; }
    }
}
