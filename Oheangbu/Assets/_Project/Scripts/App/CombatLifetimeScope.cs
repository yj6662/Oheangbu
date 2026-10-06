using Oheangbu.Combat;
using Oheangbu.Core.Events;
using Oheangbu.Spellcraft;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Oheangbu.App
{
    // 첫 LifetimeScope — 아키텍처 절대 규칙(DI=VContainer)의 최소형 이행.
    // 순수 서비스(해석기·판정기·게이지·먹 풀)만 컨테이너가 만들고,
    // Mono들은 씬 참조로 남긴다(과설계 방지 — 스코프 확장은 필요해질 때).
    public sealed class CombatLifetimeScope : LifetimeScope
    {
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private SpellBookSO _spellBook;
        [SerializeField] private FloatEventChannelSO _inkChanged;
        [SerializeField] private Prologue.PrologueSession _prologue;
        [SerializeField] private World.WorldMacroPlaytestSession _macroPlaytest;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance(_config);
            builder.RegisterInstance(_spellBook);
            builder.RegisterInstance(_inkChanged);
            builder.Register<SpellResolver>(Lifetime.Singleton);
            builder.Register<ParryJudge>(Lifetime.Singleton);
            builder.Register<GroggyMeter>(Lifetime.Singleton);
            builder.Register<InkPool>(Lifetime.Singleton);
            builder.RegisterComponentInHierarchy<CombatLoopWiring>();
            if(_prologue!=null) builder.RegisterComponent(_prologue);
            if(_macroPlaytest!=null) builder.RegisterComponent(_macroPlaytest);
            // #308 spell table dispatch (SPEC-SPELL-120-308): effect handlers + registry, the presenter (one line, owned by
            // SpellPresentationInstaller308), and who knows the unlocked final consonants (the campaign session, else nothing opens)
            SpellEffectInstaller308.Install(builder);
            SpellPresentationInstaller308.Install(builder);
            builder.RegisterInstance<ISpellUnlocks>(_macroPlaytest!=null ? (ISpellUnlocks)_macroPlaytest : new LockedSpellUnlocks308());
            // #308 spell deploy layer (SPEC-SPELL-DEPLOY-308): boots only when its profile exists and its master switch is on
            builder.RegisterBuildCallback(resolver => resolver.Resolve<CombatLoopWiring>().BootDeploy308());
        }
    }
}
