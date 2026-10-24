using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using VContainer;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 2: registers the defence side of a vol.2 combat scope. ONE call from Vol2Installer.Install (slice 3 owns that file):
    ///     Vol2DefenceInstaller.Register(builder, services);
    /// In Legacy308 (or without a loaded rulebook) it registers nothing and the wiring behaves exactly as before. In Vol2 it registers the
    /// scope's Vol2SessionLedger (the 갈무리 ink ledger and the 걱 rest budget - the thing that must survive a scene reload; to be moved to the
    /// session scope when one exists) and, once the container is built, binds the defence kit to the combat wiring. If the line is not added,
    /// the wiring binds the same kit itself on its first frame (CombatLoopWiring.Vol2Defence.cs), with a scope-local ledger.</summary>
    public static class Vol2DefenceInstaller
    {
        /// <summary>S5-b slice 8: the open seams of slices 2, 5, 6 and 7 that need the kit AND the coordinator, bound in one place (Vol2 scopes only - Register returns early otherwise).
        ///  - 심는다: Vol2SeedWiring.Bind (kit posture / 갈무리 / stable target ids, the player's real hits, the drawing state) and the kit's HitGate (the 멈 stagger exemption is read from it).
        ///  - 가른다: the 날붓 port, explicitly (the executor no longer has to look the wiring up late).
        ///  - 돌아온다: the borrowed power's HP-per-unit is the kit's (closes TE-S7-6: one conversion for the scope).
        ///  - the field: a Vol2FieldGateAdapter was bound by the shape runtime; nothing more here.
        ///  - slice 9: the swing driver (Vol2SwingBinder): the 갈무리 / ㅓ contact swing on the left click, the legacy lock-on pull off while the player's rig exists.</summary>
        static void BindSeams(Vol2ShapeRuntime shapes, Vol2DefenceKit kit, CombatLoopWiring wiring)
        {
            var coordinator = shapes.Coordinator;
            Oheangbu.Drawing.DrawingInputController input = null;
            System.Func<bool> drawing = () =>
            {
                if (input == null) input = Object.FindFirstObjectByType<Oheangbu.Drawing.DrawingInputController>();
                return input != null && input.InDrawMode;
            };
            var hub = Vol2SeedWiring.Bind(coordinator, kit, wiring.Vol2Player, drawing);
            kit.HitGate = hub != null ? hub.Hits : null;
            if (coordinator.Verbs.TryGet(Verb.Split, out var split) && split is Vol2SplitExecutor se) se.Blade = new Vol2PostureBladePort(kit.Posture);
            if (coordinator.Verbs.TryGet(Verb.Return, out var ret) && ret is Vol2ReturnExecutor re) re.Tuning.HpPerUnit = kit.HpPerUnit;
            // slice 9 (D308-150 V2): the swing driver - the left click's brush-tip swing raises the swing signal (the ㅓ contacts) and reports the 갈무리 (first enemy / air)
            Vol2SwingBinder.Bind(shapes, wiring);
        }

        public static void Register(IContainerBuilder builder, Vol2Services services, float hpPerUnit = Vol2Units.DefaultHpPerUnit)
        {
            if (builder == null || services == null || services.Mode != Oheangbu.Spellcraft.SpellRuntimeMode.Vol2) return;
            var book = services.Rulebook;
            if (book == null || !book.Ok) return;
            var session = new Vol2SessionLedger(new DefenceNumbersV3Dto(book.Numbers));
            builder.RegisterInstance(session);
            builder.RegisterBuildCallback(resolver =>
            {
                var wiring = resolver.Resolve<CombatLoopWiring>();
                var kit = wiring != null ? wiring.BindVol2Defence(session, hpPerUnit) : null;
                if (kit == null) return;
                // slice 3's coordinator (built by its own build callback, registered BEFORE this one): hand it the real ports, and let the gate take the
                // ink of ordinary casts. If there is none yet (this line placed before the shape callback) the gate leaves ordinary casts to the coordinator.
                var shapes = services.Shapes;
                if (shapes != null)
                {
                    shapes.Coordinator.Damage = kit.DamageSink;
                    shapes.Coordinator.Ink = kit.PaidInk;
                    kit.PaysOrdinaryCasts = true;
                    BindSeams(shapes, kit, wiring);
                }
                else
                {
                    kit.PaysOrdinaryCasts = false;
                    Debug.LogWarning("[Vol2Defence] no shape runtime yet: place Vol2DefenceInstaller.Register after the shape runtime's build callback in Vol2Installer.Install");
                }
            });
        }
    }
}
