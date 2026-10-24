using Oheangbu.Combat;
using Oheangbu.Core.Events;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Data;
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
        [SerializeField] private SpellLabUnlocks308 _labUnlocks;   // SPEC-SPELL-LAB-308: the spell test scene only (no session there)
        // S5-b slice 1 (D308-149): which spell runtime this scope runs. Chosen once, when the scope is built. Legacy308 is the default and
        // the only mode of every scene that has not been told otherwise. Vol2 is honoured ONLY where _labUnlocks exists (the spell lab):
        // a main / campaign scene stays Legacy308 whatever is serialized here (the switch to vol.2 there is a later, explicit step).
        [SerializeField] private SpellRuntimeMode _spellRuntimeMode = SpellRuntimeMode.Legacy308;
        [Tooltip("vol.2 data; empty = the editor loads the imported assets by path (the lab scene is wired in slice 10)")]
        [SerializeField] private NumbersV3SO _vol2Numbers;
        [SerializeField] private Vol2TableSO _vol2Table;

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
            // S5-b slice 1: the spell runtime route + the vol.2 clock / ids / ledgers (registered in both modes; an adapter and the lab runner only in Vol2)
            var mode = SpellRuntimeMode.Legacy308;
            Vol2LabRequest labRequest = null;
            if (_labUnlocks != null)
            {
                mode = _spellRuntimeMode;
                if (Vol2LabRequestFile.TryConsume(gameObject.scene.path, out labRequest)) mode = labRequest.Mode;
            }
            else if (_spellRuntimeMode != SpellRuntimeMode.Legacy308)
                Debug.LogWarning("[Vol2] _spellRuntimeMode is ignored outside the spell lab; this scope stays Legacy308", this);
            var vol2 = Vol2Installer.Build(mode, mode == SpellRuntimeMode.Vol2 ? Vol2RulebookLoader.Load(_vol2Numbers, _vol2Table) : null, _ => Debug.isDebugBuild);
            Vol2Installer.Install(builder, vol2);
            if (mode == SpellRuntimeMode.Vol2 && labRequest != null && labRequest.run)
                builder.RegisterBuildCallback(resolver => Vol2LabRunner.Create(resolver.Resolve<CombatLoopWiring>(), vol2, labRequest));
            if(_prologue!=null) builder.RegisterComponent(_prologue);
            if(_macroPlaytest!=null) builder.RegisterComponent(_macroPlaytest);
            // #308 spell table dispatch (SPEC-SPELL-120-308): effect handlers + registry, the presenter (one line, owned by
            // SpellPresentationInstaller308), and who knows the unlocked final consonants (the campaign session, else nothing opens)
            SpellEffectInstaller308.Install(builder);
            SpellPresentationInstaller308.Install(builder);
            builder.RegisterInstance<ISpellUnlocks>(_macroPlaytest!=null ? (ISpellUnlocks)_macroPlaytest : _labUnlocks!=null ? _labUnlocks : new LockedSpellUnlocks308());
            // #308 spell deploy layer (SPEC-SPELL-DEPLOY-308): boots only when its profile exists and its master switch is on
            builder.RegisterBuildCallback(resolver => resolver.Resolve<CombatLoopWiring>().BootDeploy308());
            // #308 D308-125 5 (c): the look of the ember patches and the scorch (presentation only)
            builder.RegisterBuildCallback(resolver => resolver.Resolve<CombatLoopWiring>().BootEmberLook308());
        }
    }
}
