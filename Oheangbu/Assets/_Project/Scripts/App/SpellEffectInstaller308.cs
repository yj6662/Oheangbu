using System;
using System.Collections.Generic;
using VContainer;

namespace Oheangbu.App
{
    // The one place effect handlers are registered (SPEC-SPELL-120-308 section 4). This file is written once by the
    // foundation and then frozen. A work package adds SpellEffectInstaller308.WPnn.cs and implements only its own method:
    //
    //     public static partial class SpellEffectInstaller308
    //     {
    //         static partial void InstallWP03(SpellEffectList308 effects)
    //         {
    //             effects.Register<TideEffect308>();
    //         }
    //     }
    //
    // A partial method nobody implements disappears at compile time. No reflection scan (IL2CPP stripping; the static check
    // compares the rule sheets with these registrations).
    public static partial class SpellEffectInstaller308
    {
        public static void Install(IContainerBuilder builder)
        {
            var effects = new SpellEffectList308(builder);
            // basic shapes for new rows that need nothing but today's judgement (never an empty collection)
            effects.Register<CoreSingleEffect308>();
            effects.Register<CoreConeEffect308>();
            effects.Register<CoreCircleEffect308>();
            effects.Register<CorePathEffect308>();
            effects.Register<CoreVolleyEffect308>();
            InstallWP00(effects); InstallWP02(effects); InstallWP03(effects); InstallWP04(effects); InstallWP05(effects);
            InstallWP06(effects); InstallWP07(effects); InstallWP08(effects); InstallWP09(effects); InstallWP10(effects);
            InstallWP11(effects); InstallWP12(effects); InstallWP13(effects); InstallWP14(effects);
            // The registry is put together from the effects one by one (SpellEffectList308.Build): an effect the container
            // cannot build is left out and reported. It never stops the combat scope, so the 36 existing glyphs, the other
            // handlers and the deploy layer's boot callback go on.
            builder.Register<SpellEffectRegistry308>(effects.Build, Lifetime.Singleton);
        }

        static partial void InstallWP00(SpellEffectList308 effects);
        static partial void InstallWP02(SpellEffectList308 effects);
        static partial void InstallWP03(SpellEffectList308 effects);
        static partial void InstallWP04(SpellEffectList308 effects);
        static partial void InstallWP05(SpellEffectList308 effects);
        static partial void InstallWP06(SpellEffectList308 effects);
        static partial void InstallWP07(SpellEffectList308 effects);
        static partial void InstallWP08(SpellEffectList308 effects);
        static partial void InstallWP09(SpellEffectList308 effects);
        static partial void InstallWP10(SpellEffectList308 effects);
        static partial void InstallWP11(SpellEffectList308 effects);
        static partial void InstallWP12(SpellEffectList308 effects);
        static partial void InstallWP13(SpellEffectList308 effects);
        static partial void InstallWP14(SpellEffectList308 effects);

        // Fresh instances of every registered effect, outside any scene scope: the importer (parameter catalogue),
        // the edit-mode fixtures and the status report use this. Each call builds its own short-lived container.
        // An effect that could not be built is missing from the answer (CreateRegistry().Failures names it).
        public static ISpellEffect[] CreateAll()
        {
            var all = CreateRegistry().All;
            var effects = new ISpellEffect[all.Count];
            for (int i = 0; i < effects.Length; i++) effects[i] = all[i];
            return effects;
        }

        // The registry the scope would build, with its Failures (the status check prints them).
        public static SpellEffectRegistry308 CreateRegistry()
        {
            var builder = new ContainerBuilder();
            Install(builder);
            using (var container = builder.Build())
                return container.Resolve<SpellEffectRegistry308>();
        }
    }

    // The effect classes of one scope, in registration order (one instance per Install call: nothing static).
    // Register<T> gives the class to the container by its own type, so its constructor may take services the scope
    // registers (Builder), and remembers the type. Build asks the container for each one on its own.
    public sealed class SpellEffectList308
    {
        readonly IContainerBuilder _builder;
        readonly List<Type> _types = new List<Type>();

        public SpellEffectList308(IContainerBuilder builder) { _builder = builder; }

        // For the shared services a package's effects take in their constructors (never for an effect class).
        public IContainerBuilder Builder => _builder;
        public int Count => _types.Count;

        public void Register<T>() where T : class, ISpellEffect
        {
            _builder.Register<T>(Lifetime.Singleton);
            _types.Add(typeof(T));
        }

        public SpellEffectRegistry308 Build(IObjectResolver resolver)
        {
            var built = new List<ISpellEffect>(_types.Count);
            var failures = new List<string>();
            for (int i = 0; i < _types.Count; i++)
            {
                try
                {
                    if (resolver.Resolve(_types[i]) is ISpellEffect effect) built.Add(effect);
                    else failures.Add(_types[i].Name + ": the container answered nothing");
                }
                catch (Exception exception)
                {
                    // the innermost cause says more than the container's wrapper
                    var cause = exception;
                    while (cause.InnerException != null) cause = cause.InnerException;
                    failures.Add(_types[i].Name + ": " + cause.GetType().Name + " " + cause.Message);
                }
            }
            return new SpellEffectRegistry308(built, failures);
        }
    }
}
