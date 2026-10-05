using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // #308 spell deploy layer hook (SPEC-SPELL-DEPLOY-308 section 1, hook A). Vfx120Effect.cs itself gets two lines: one at the top
    // of Build() and one at the top of Sample(). A profile without a Deploy308 reference (or with the layer's master switch
    // off, or a letter the map does not know) takes the current path untouched. The row's legacy-body policy decides whether
    // the deploy layer replaces the old body (Retire) or is drawn with it (Keep / Wrap). The judgement clock, the area plan
    // and the target are read exactly as they were received; nothing is written back.
    // D308-13b: the basic five (가 나 마 사 아) are Retire rows like every single attack - their Bolt300 flight body and impact
    // are not built; the deploy layer's main stroke is the flight and its hit splash (on a confirmed hit) is the impact.
    public sealed partial class Vfx120Effect
    {
        private ISpellDeployHost308 _deployHost;
        private float _deployGrade = .5f;
        private Color _deployTint;
        private bool _deployHasTint, _deployRetires;
        private InkDeployRuntime308 _deploy;

        public InkDeployRuntime308 Deploy308Runtime => _deploy;
        public bool Deploy308Retires => _deploy != null && _deployRetires;

        public override void SetDeployHost(ISpellDeployHost308 host, float grade01, Color tint = default)
        {
            _deployHost = host; _deployGrade = Mathf.Clamp01(grade01);
            _deployTint = tint; _deployHasTint = tint.a > 0f;
        }

        // true = the deploy layer is the whole presentation of this cast (Retire rows): Build() stops there
        // #308 forms2 N1: a fault of the layer never reaches whoever called Begin (an EA runtime, the adapter, a field service).
        // The layer's part is taken down, the lifetime is what it was, and the catalogue body is built as if the layer were off.
        private bool BuildDeploy308()
        {
            float life = Life;
            try { return BuildDeploy308Core(); }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
                try { ClearDeploy308(); } catch (System.Exception inner) { Debug.LogException(inner, this); _deploy = null; _deployRetires = false; }
                Life = life;
                return false;
            }
        }

        private bool BuildDeploy308Core()
        {
            ClearDeploy308();
            var deploy = Profile != null ? Profile.Deploy308 : null;
            if (deploy == null || !deploy.LayerEnabled || deploy.Map == null || string.IsNullOrEmpty(Profile.Glyph)) return false;
            if (!deploy.Map.TryGet(Profile.Glyph[0], out var row)) return false;

            float hold = 0f;
            switch (row.Category)
            {
                case DeployCategory308.Ward: hold = Profile.Duration; break;
                case DeployCategory308.ComboInstall: case DeployCategory308.Field: hold = row.LegacyBody == DeployLegacyBody308.Retire ? 0f : Profile.Duration; break;
            }
            var cast = new DeployCast308
            {
                Profile = deploy, Row = row, Origin = ReceivedOrigin, Target = ReceivedTarget, FallbackPoint = ReceivedFallback, Plan = ReceivedAreaPlan,
                ImpactClock = ReceivedImpactClock, Grade01 = _deployGrade, Tint = _deployHasTint ? _deployTint : Profile.Pigment, HoldSeconds = hold,
                WardRadius = Profile.WardKind != Vfx120WardKind.None ? Profile.WardRadius : 0f, WardHeight = Profile.WardKind != Vfx120WardKind.None ? Profile.WardHeight : 0f,
                Seed = ReceivedAreaPlan != null && ReceivedAreaPlan.VisualSeed != 0 ? ReceivedAreaPlan.VisualSeed : PreviewControlled ? 308 : Profile.Glyph[0] * 7919 + Time.frameCount,
                Tier = _deployHost != null ? _deployHost.Tier : DeployTier308.PC,
                // #308 forms2: the rule's own clocks and the letter's own path numbers, handed on as they were received
                GuardWindow = ReceivedGuardClock > 0f ? ReceivedGuardBrightWindow : 0f, GuardLife = Mathf.Max(0f, ReceivedGuardClock),
                FadeSeconds = Profile.WardKind != Vfx120WardKind.None ? Profile.WardFade : 0f,
                PathArc = Profile.Bolt300Arc, PathCurve = Profile.Bolt300Curve, PathEase = Profile.Bolt300Ease,
                NewForms = true,   // this seam (hook A) is where the forms2 forms replace a retired catalogue body
            };
            _deploy = gameObject.AddComponent<InkDeployRuntime308>();
            if (!_deploy.Configure(cast, _deployHost)) { ClearDeploy308(); return false; }   // pool exhausted: the current path plays instead
            // #308 forms2 N3: an effect that runs on somebody else's clock in Play (the planted tree lift) keeps its body whatever the row says
            _deployRetires = InkForms2Rules308.RetireAllowed(row, PreviewControlled && Application.isPlaying);
            if (!_deployRetires) return false;
            // #308 forms2 N4: only an attack takes the layer's length. A guard, a ward, a buff keep the lifetime their owner gave
            // this object (the rule side and the sounds read it); the layer's body fits itself to that clock.
            if (!InkForms2Rules308.KeepsLegacyLife(row.Category)) Life = _deploy.Duration;
            return true;
        }

        private bool SampleDeploy308()
        {
            if (_deploy == null) return false;
            _deploy.Sample(Age);
            return _deployRetires;
        }

        private void ClearDeploy308()
        {
            _deployRetires = false;
            if (_deploy == null) return;
            _deploy.Dispose();
            if (Application.isPlaying) Destroy(_deploy); else DestroyImmediate(_deploy);
            _deploy = null;
        }
    }
}
