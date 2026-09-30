using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // #306 hook file owned by track C2 (SPEC-PLAYTEST-306 #12: element organ telegraph, parry counter ink stroke). The main
    // CombatLoopWiring.cs only calls these. Presentation only: AddParry/GroggyMeter/ink already ran — the game value is immediate,
    // only its display (lock-on fill + reticle pulse) waits for the stroke to reach the lit organ (real-time capped).
    // Order on an owned success: OnParryImpactResolved → PulseReticle306 (snapshot + defer) → AddParry → OnParryCountered306 (launch).
    // A deferred pulse nobody claims (legacy/unlisted attacker) is flushed the same frame by the stroke host's LateUpdate.
    // Half parries never reach OnParryCountered306; a thin stroke comes from this file's own OwnedImpactResolved listener.
    public sealed partial class CombatLoopWiring
    {
        [Header("#306 속성 기관 예고·방어 성공 먹 획 (표현)")]
        [Tooltip("비우면 Resources/Telegraph306/EnemyTelegraphTiming306 → 없으면 TEST 기본값")]
        [SerializeField] private EnemyTelegraphTimingSO _telegraphTiming306;
        private bool _timingLooked306;
        private ParryCounterStrokeEffect _counter306;
        private bool _pulsePending306;
        private EnemyVitals _heldTarget306;
        private float _heldGroggy306;
        private ParryJudge _hookedJudge306;
        // read-only re-broadcast for checks/presentation (AC-12c): attacker, result, groggy01 after AddParry, this parry blossomed
        public event System.Action<EnemyVitals, ParryImpactResult, float, bool> ParryCountered;
        private System.Action<ParryImpactResult> _halfHook306;

        private EnemyTelegraphTimingSO Timing306
        {
            get
            {
                if (_telegraphTiming306 != null || _timingLooked306) return _telegraphTiming306;
                _timingLooked306 = true;
                _telegraphTiming306 = Resources.Load<EnemyTelegraphTimingSO>(EnemyTelegraphTimingSO.ResourcePath);
                if (_telegraphTiming306 == null) { _telegraphTiming306 = ScriptableObject.CreateInstance<EnemyTelegraphTimingSO>(); _telegraphTiming306.hideFlags = HideFlags.DontSave; }
                return _telegraphTiming306;
            }
        }

        private ParryCounterStrokeEffect Counter306()
        {
            if (!Application.isPlaying) return null;
            if (_counter306 == null)
            {
                var timing = Timing306; if (timing == null) return null;
                _counter306 = GetComponent<ParryCounterStrokeEffect>();
                if (_counter306 == null) { _counter306 = gameObject.AddComponent<ParryCounterStrokeEffect>(); _counter306.hideFlags = HideFlags.DontSave; }
                _counter306.Configure(timing, OnCounterArrived306, FlushPulse306);
                _counter306.SetUnhook(UnhookHalf306);
            }
            if (_judge != null && _hookedJudge306 != _judge)
            {
                UnhookHalf306();
                if (_halfHook306 == null) _halfHook306 = OnHalfParry306;
                _judge.OwnedImpactResolved += _halfHook306; _hookedJudge306 = _judge;
            }
            return _counter306;
        }

        private void UnhookHalf306()
        {
            if (_hookedJudge306 != null && _halfHook306 != null) _hookedJudge306.OwnedImpactResolved -= _halfHook306;
            _hookedJudge306 = null;
        }

        private Color GuardColour306(Element element) => _palette != null ? _palette.GetBaseColor(InitialOf(element)) : Color.white;

        // a successful owned parry was counted on `attacker` (right after AddParry)
        void OnParryCountered306(ParryImpactResult result, EnemyVitals attacker)
        {
            bool pulse = _pulsePending306; _pulsePending306 = false;
            // hold only a real pre-AddParry snapshot (the lock-on target); an unlocked attacker shows no groggy amount, only the stroke
            bool snap = pulse && attacker == _heldTarget306; float held = snap ? _heldGroggy306 : 0f;
            _heldTarget306 = null;
            var counter = Counter306();
            // this parry filled the meter (a parry landing on an already-full meter is not a new blossom)
            bool blossomed = attacker.Groggy.IsBlossomed && (!snap || held < 1f);
            ParryCountered?.Invoke(attacker, result, attacker.Groggy.Value01, blossomed);
            if (counter == null || !isActiveAndEnabled ||
                !counter.Launch(result.Point, GuardColour306(result.GuardElement), attacker, result.Attack.AttackId, false, blossomed, snap, held, pulse))
            { if (pulse) _hud?.PulseReticle(); RefreshHud(); }
        }

        private void OnHalfParry306(ParryImpactResult result)
        {
            if (!isActiveAndEnabled || result.Outcome != ParryOutcome.Half || !(result.Attack.Instigator is EnemyVitals attacker) ||
                attacker == null || !_targets.Contains(attacker)) return;
            var counter = Counter306();
            if (counter != null) counter.Launch(result.Point, GuardColour306(result.GuardElement), attacker, result.Attack.AttackId, true, false, false, 0f, false);
        }

        private void OnCounterArrived306(bool pulse) { if (pulse) _hud?.PulseReticle(); RefreshHud(); }

        private void FlushPulse306()
        {
            if (!_pulsePending306) return;
            _pulsePending306 = false; _heldTarget306 = null;
            _hud?.PulseReticle(); RefreshHud();
        }

        // lock-on groggy value to draw (presentation only; the game value is attacker.Groggy.Value01 at once)
        float DisplayGroggy306(EnemyVitals target)
        {
            if (!isActiveAndEnabled) { if (_counter306 != null) _counter306.Clear(); _pulsePending306 = false; _heldTarget306 = null; return target != null ? target.Groggy.Value01 : 0f; }
            var counter = Counter306();
            if (target == null) return 0f;
            if (_pulsePending306 && target == _heldTarget306) return _heldGroggy306;
            return counter != null && counter.TryGetHeldGroggy(target, out float held) ? held : target.Groggy.Value01;
        }

        // the reticle pulse on a successful parry — deferred to the counter stroke's arrival
        void PulseReticle306()
        {
            if (Counter306() == null) { _hud?.PulseReticle(); return; }
            _pulsePending306 = true; _heldTarget306 = GroggyHudTarget;
            _heldGroggy306 = _heldTarget306 != null ? _heldTarget306.Groggy.Value01 : 0f;
        }

        // player-side parry burst scale
        float PlayerParryBurstScale306() => Timing306 != null ? Timing306.PlayerParryBurstScale : 1f;
    }
}
