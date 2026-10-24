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
