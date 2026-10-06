using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 hook file of the spell deploy layer (SPEC-SPELL-DEPLOY-308, D308-10 / D308-10b / D308-10c / D308-13b). The main
    // CombatLoopWiring.cs carries one line of it (the KTP enemy-hit contact asks DeployOwnsHit308); everything else is here:
    // CombatLifetimeScope calls BootDeploy308 once the container is built, and the layer only listens to the read-only
    // re-broadcasts (CastAccepted / CastPlanned / ParryResolved / EnemyDamageResolved) and to the targets' weak-point events.
    // Presentation only - nothing here changes a rule, a clock or a cast plan. The layer sleeps when the profile is missing
    // or its master switch is off.
    public sealed partial class CombatLoopWiring
    {
        [Header("#308 술식 전개 층 (표현)")]
        [Tooltip("비우면 Resources/Deploy308/SpellDeploy308Profile → 없거나 LayerEnabled가 꺼져 있으면 층이 잠든다")]
        [SerializeField] private SpellDeploy308ProfileSO _deploy308;
        private SpellDeployDirector308 _deployDirector308;
        public SpellDeployDirector308 DeployDirector308 => _deployDirector308;

        // D308-10c groggy probe memory (presentation side: no rule reads any of it)
        private const int PlanMemory308 = 8;
        private readonly CastPlan[] _plans308 = new CastPlan[PlanMemory308];   // the last cast plans, for an area cast's planned hit targets
        private int _planNext308, _bootFrame308;
        private EnemyVitals[] _groggyHooked308;                                // the targets whose weak-point events are listened to
        private bool[] _groggyOpen308;                                         // per target: its weak-point window was open at the last event
        private float[] _groggyKilledAt308;                                    // per target: when it died while that window was open

        public void BootDeploy308()
        {
            if (!Application.isPlaying || _deployDirector308 != null) return;
            GameObject host = null;
            try
            {
                var profile = _deploy308 != null ? _deploy308 : Resources.Load<SpellDeploy308ProfileSO>(SpellDeploy308ProfileSO.ResourcesPath);
                if (profile == null || !profile.LayerEnabled) return;
                host = new GameObject("SpellDeploy308") { hideFlags = HideFlags.DontSave };
                host.transform.SetParent(transform, false);
                _deployDirector308 = host.AddComponent<SpellDeployDirector308>();
                _bootFrame308 = Time.frameCount;
                _deployDirector308.Boot(profile, this, _brushAdapter, _hud, _playerTransform != null ? _playerTransform : transform, _motor, JudgementWindowNear, DeployTargetGroggy308);
                EnsureDeployHooks308();
            }
            catch (System.Exception e)
            {
                // presentation only: a failure here must not stop the container build callbacks (combat boots without the layer)
                Debug.LogException(e, this);
                _deployDirector308 = null;
                if (host != null) Destroy(host);
            }
        }

        /// <summary>Read-only probe (section 8): would a full-screen impact frame hide a judgement window right now? True while the
        /// guard stands inside its parry window, or an enemy within `radius` is telegraphing / its projectile is flying and its
        /// predicted impact is at most `seconds` away. Same scaled clock as ParryJudge; nothing is fed back.</summary>
        public bool JudgementWindowNear(float seconds, float radius)
        {
            float now = Time.time;
            if (_judge != null && _judge.GuardInWindow(now)) return true;
            Vector3 player = PlayerPosition();
            float reach = radius * radius;
            for (int i = 0; i < _controllers.Count; i++)
            {
                var controller = _controllers[i];
                if (controller == null || !controller.isActiveAndEnabled) continue;
                if (!controller.IsTelegraphing && !controller.IsProjectileFlying) continue;
                if ((controller.transform.position - player).sqrMagnitude > reach) continue;
                float impact = controller.PredictedImpactTime;
                if (impact >= now - .05f && impact - now <= seconds) return true;
            }
            return false;
        }

        // ---- D308-10c: impact frames only for a spell that lands on a groggy enemy ----

        /// <summary>The layer's listeners on this wiring: the cast plans it re-broadcasts and its targets' weak-point events. Laid by
        /// the layer (never by combat), again whenever the target list has changed - the container may build the layer before
        /// this component's own Awake has collected the targets. Returns true once that Awake has certainly run.</summary>
        public bool EnsureDeployHooks308()
        {
            if (_deployDirector308 == null) return true;
            bool same = _groggyHooked308 != null && _groggyHooked308.Length == _targets.Count;
            for (int i = 0; same && i < _targets.Count; i++) same = ReferenceEquals(_groggyHooked308[i], _targets[i]);
            if (!same)
            {
                bool first = _groggyHooked308 == null;
                UnhookTargets308();
                _groggyHooked308 = _targets.ToArray();
                _groggyOpen308 = new bool[_groggyHooked308.Length]; _groggyKilledAt308 = new float[_groggyHooked308.Length];
                for (int i = 0; i < _groggyHooked308.Length; i++)
                {
                    _groggyKilledAt308[i] = float.NegativeInfinity;
                    var target = _groggyHooked308[i];
                    if (target == null) continue;
                    _groggyOpen308[i] = target.WeakPointActive;
                    target.WeakPointOpened += OnWeakPointChanged308; target.WeakPointClosed += OnWeakPointChanged308;
                }
                if (first) CastPlanned += RememberPlan308;
            }
            return _targets.Count > 0 || Time.frameCount > _bootFrame308 + 1;
        }

        private void UnhookTargets308()
        {
            if (_groggyHooked308 == null) return;
            foreach (var target in _groggyHooked308)
                if (target != null) { target.WeakPointOpened -= OnWeakPointChanged308; target.WeakPointClosed -= OnWeakPointChanged308; }
        }

        /// <summary>Called by the layer's director when it is destroyed: its listeners leave with it.</summary>
        public void UnhookDeploy308()
        {
            UnhookTargets308();
            if (_groggyHooked308 != null) CastPlanned -= RememberPlan308;
            _groggyHooked308 = null; _groggyOpen308 = null; _groggyKilledAt308 = null;
            for (int i = 0; i < PlanMemory308; i++) _plans308[i] = null;
        }

        private void RememberPlan308(CastPlan plan)
        {
            if (plan == null) return;
            _plans308[_planNext308] = plan; _planNext308 = (_planNext308 + 1) % PlanMemory308;
        }

        // A weak-point window opened or closed somewhere. Which one is found by comparing with what was seen at the last event;
        // a window that closed because its owner DIED is remembered with its time - a spell that kills a groggy enemy closes
        // the window before the burst's first cel can ask about it (that cel may come half a cel after the judged hit).
        private void OnWeakPointChanged308()
        {
            if (_groggyHooked308 == null) return;
            for (int i = 0; i < _groggyHooked308.Length; i++)
            {
                var target = _groggyHooked308[i];
                if (target == null) { _groggyOpen308[i] = false; continue; }
                bool open = target.WeakPointActive;
                if (_groggyOpen308[i] && !open && !target.IsAlive) _groggyKilledAt308[i] = Time.time;
                _groggyOpen308[i] = open;
            }
        }

        // groggy = the weak-point window is open (EnemyVitals.WeakPointActive: the stun + amplification window the groggy meter's
        // blossom opens, whichever of the three canon sources filled it - parry, combo detonation, counter-element sword strike),
        // or its controller stands stunned, or the window was closed by this target's death within `killGrace` seconds.
        private bool Groggy308(EnemyVitals vitals, float killGrace)
        {
            if (vitals == null) return false;
            if (vitals.WeakPointActive) return true;
            if (vitals.IsAlive && vitals.TryGetComponent<EnemyController>(out var controller) && controller.IsStunned) return true;
            if (_groggyHooked308 == null) return false;
            for (int i = 0; i < _groggyHooked308.Length; i++)
                if (ReferenceEquals(_groggyHooked308[i], vitals)) return Time.time - _groggyKilledAt308[i] <= killGrace;
            return false;
        }

        /// <summary>Read-only probe (D308-10c, the layer's TargetGroggy): is the judged target of a cast groggy right now?
        /// Single cast (`plan` null): `target` is. Area cast: at least one target that plan scheduled a hit on is - the planned
        /// hits come from this wiring's own CastPlanned re-broadcast (a volley also carries them on the plan). Nothing is fed
        /// back: no groggy value, no weak-point window, no plan is changed.</summary>
        public bool DeployTargetGroggy308(Transform target, AreaImpactPlan plan, float killGrace)
        {
            EnsureDeployHooks308();
            if (plan != null)
            {
                for (int i = 0; i < PlanMemory308; i++)
                {
                    var cast = _plans308[i];
                    if (cast == null || !ReferenceEquals(cast.Area, plan)) continue;
                    for (int h = 0; h < cast.Hits.Count; h++) if (Groggy308(cast.Hits[h].Target, killGrace)) return true;
                    return false;
                }
                for (int s = 0; s < plan.Shots.Count; s++) if (Groggy308(plan.Shots[s].Target, killGrace)) return true;
                return false;
            }
            if (target == null) return false;
            for (int i = 0; i < _targets.Count; i++)
            {
                var vitals = _targets[i];
                if (vitals == null) continue;
                Transform root = vitals.transform;
                if (root == target || target.IsChildOf(root) || root.IsChildOf(target)) return Groggy308(vitals, killGrace);
            }
            return false;
        }

        // D308-13b: on an enabled attack row whose old body is retired the deploy layer's own hit splash is the hit, so the
        // KTP enemy-hit contact is not spawned (one line in ApplyConfirmedEnemyHit asks this). Parry contacts and the
        // player's own hit contact never come here. Damage, the re-broadcasts and their order are untouched.
        // #308 present add-on: this is also where the layer learns the GLYPH of the hit the wiring has just confirmed (the
        // re-broadcast carries none). The director answers the same question as before and hands the glyph to its presenter
        // stage, which may throw the hit splash for a hit no live cast took (a handler's second stage, a derived shot).
        private bool DeployOwnsHit308(char letter) => _deployDirector308 != null && _deployDirector308.ConfirmedHitLetter(letter);

        // ---- #308 forms2 (Tools/Unity/Stage308_forms2/DESIGN.md S3): read-only probes. Each one reads a state the rules already
        // keep and returns it; none of them changes a guard, a buff, a field, a summon or a clock.

        /// <summary>Does a guard stand right now (window or plain block), and which guard is it (ParryJudge.GuardRevision)?</summary>
        public bool DeployGuardStands308(out uint revision)
        {
            revision = _judge != null ? _judge.GuardRevision : 0u;
            return _judge != null && _judge.GuardStands(Time.time);
        }

        /// <summary>Which of the five EA buffs run (bit = the element's index).</summary>
        public int DeployBuffMask308() => EABuffs != null ? EABuffs.ActiveMask(Time.time) : 0;

        /// <summary>The fire aura's reach (m), 0 without the buff runtime.</summary>
        public float DeployAuraRadius308() => EABuffs != null ? EABuffs.AuraRadius : 0f;

        /// <summary>Does the thing a field letter made still exist (the lift's platform, a bridge)? `known` = there is a service to ask.</summary>
        public bool DeployFieldAlive308(char letter, out bool known)
        {
            if (Feature308(letter) == Oheangbu.Spellcraft.SpellLegacyFeature.Mum) { known = MumBridges != null; return known && MumBridges.ActiveCount > 0; }
            known = FieldSpells != null;
            return known && FieldSpells.HasPlatform;
        }

        // The two questions the rule side asks the layer. Both only choose which PRESENTATION is started; a fault answers "no"
        // (the catalogue presentation plays), and neither is asked on a path that grants or takes anything.
        private bool DeployOwnsParry308(Oheangbu.Core.Domain.Element element)
        {
            try { return _deployDirector308 != null && _deployDirector308.OwnsParry(element); }
            catch (System.Exception e) { Debug.LogException(e, this); return false; }
        }

        public bool DeployOwnsSummon308(char letter)
        {
            try { return _deployDirector308 != null && _deployDirector308.OwnsSummonEntrance(letter); }
            catch (System.Exception e) { Debug.LogException(e, this); return false; }
        }
    }
}
