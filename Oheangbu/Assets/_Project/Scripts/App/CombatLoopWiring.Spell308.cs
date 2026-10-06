using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using VContainer;

namespace Oheangbu.App
{
    // #308 table-driven dispatch (SPEC-SPELL-120-308 sections 1, 3, 5, 8). This partial owns
    //  - the gate (which rows may be cast now),
    //  - the route of a row without a legacy feature to its registered effect handler (SpellDispatchRule308:
    //    Prepare -> ink -> Commit (rules) -> accept -> presentation; a presenter can never change what was judged),
    //  - the host the handlers call back into (ISpellCastHost, backed by the unchanged judgement bodies of the main file),
    //  - the registry fan-out: one call each at spend, after hold decay, at accept, on enemy hit, on player hit, per frame, on clear.
    // The 36 glyphs that resolved before #308 never come through the handler route: their rows carry a legacy feature and the
    // main file's switch runs them exactly as before. Without ConstructSpells (old scenes, old checks) every new row is
    // Unavailable and the 36 behave as they always did.
    public sealed partial class CombatLoopWiring : ISpellCastHost, ISpellGate
    {
        private SpellEffectRegistry308 _spells308;
        private ISpellUnlocks _unlocks308;
        private ISpellPresenter _fx308;
        // What the handlers are handed instead of the presenter itself: every presenter call isolated, and held back while
        // a cast's rules are being committed (SpellEffectPresenterGuard308.cs).
        private readonly SpellPresenterGuard308 _fxGuard308 = new SpellPresenterGuard308();
        private readonly SpellBuffState308 _buffs308 = new SpellBuffState308();
        // The row of the letter being dispatched (set only inside OnLetterDrawn). Old callers hand the Resolve* methods a
        // bare SpellCast through reflection; those fall back to the table, then to the grammar (RowFor).
        private SpellRow _row308;

        // Misfire re-broadcast (read only): the glyph and why it did not cast. No rule and no HUD hangs on it.
        public event Action<char, SpellResolveStatus> CastMisfired;

        public SpellEffectRegistry308 Spells308 => _spells308;

        // Second injection point: Construct keeps its four arguments (twenty-odd editor checks call it).
        [Inject]
        public void ConstructSpells(SpellEffectRegistry308 spells, ISpellUnlocks unlocks, ISpellPresenter presenter)
        {
            _spells308 = spells; _unlocks308 = unlocks; _fx308 = presenter;
            (presenter as ISpellPresenterBinding)?.Bind(_brushAdapter != null ? _brushAdapter.VisualSet : null, transform);
            _fxGuard308.EndAll();
            _fxGuard308.Bind(presenter);
            // one handler that could not be built is reported here, once; the others and the 36 legacy glyphs go on
            if (spells != null)
                for (int i = 0; i < spells.Failures.Count; i++) Debug.LogError("[Spell308] effect handler not available: " + spells.Failures[i], this);
        }

        private ISpellPresenter Fx308 => _fxGuard308;
        private SpellTickContext TickContext308() => new SpellTickContext(Time.time, Time.deltaTime, this, Fx308);

        // ---- gate ----
        SpellResolveStatus ISpellGate.Judge(SpellRow row)
        {
            bool open;
            switch (row.Feature)
            {
                // the same five conditions the old TryResolve call passed as flags
                case SpellLegacyFeature.Book: open = true; break;
                case SpellLegacyFeature.Guk: open = FieldSpells != null && FieldSpells.IsUnlocked; break;
                case SpellLegacyFeature.BuffG: open = EABuffs != null && EABuffs.Unlocked; break;
                case SpellLegacyFeature.Ward: open = EAWards != null && EAWards.Available; break;
                case SpellLegacyFeature.Giyeok: open = EAGiyeok != null && EAGiyeok.Unlocked; break;
                case SpellLegacyFeature.Mum: open = MumBridges != null && MumBridges.IsUnlocked; break;
                default:
                    bool registered = _spells308 != null && _spells308.Has(row.Handler);
                    // a TEST-only row (Gate test in its rule sheet) is judged without the main-game grant of its final
                    bool unlocked = registered && row.Final != SpellFinal.None && _unlocks308 != null && _resolver != null &&
                        _resolver.TryUnlock(row.Final, out var rule) && _unlocks308.FinalUnlocked(SpellUnlockPolicy308.RuleFor(row, rule));
                    return SpellUnlockPolicy308.JudgeNew(row, registered, unlocked);
            }
            return open ? SpellResolveStatus.Ok : SpellResolveStatus.Unavailable;
        }

        // ---- row lookups for the legacy bodies ----
        private SpellRow RowFor(char letter)
        {
            if (_row308 != null && _row308.Is(letter)) return _row308;
            return _resolver != null && _resolver.TryRow(letter, out var row) ? row : null;
        }

        // A row parameter, or what the glyph implied before #308 when there is no row (bare casts from old callers).
        private float Trait308(char letter, string key)
        {
            var row = RowFor(letter);
            return row != null ? row.F(key, 0f) : SpellGrammar308.LegacyTrait(letter, key);
        }

        private SpellLegacyFeature Feature308(char letter)
        {
            var row = RowFor(letter);
            if (row != null) return row.Feature;
            return SpellGrammar308.TryDecompose(letter, out var glyph) ? SpellGrammar308.LegacyFeatureOf(glyph) : SpellLegacyFeature.None;
        }

        // ---- registry fan-out (each is one call from the main file) ----
        private float CostScale308(in SpellCast cast)
            => _spells308 != null ? _spells308.CostScale(new SpellCastInfo(cast, RowFor(cast.Letter)), Time.time) : 1f;

        private SpellCast ModifyCast308(SpellCast cast, SpellRow row)
        {
            if (_spells308 == null) return cast;
            var draft = new SpellCastDraft(cast, row);
            _spells308.ModifyCast(ref draft, Time.time);
            if (draft.Power == cast.Power && draft.SpeedMul == cast.SpeedMul) return cast;
            return new SpellCast(cast.Letter, cast.Kind, cast.Element, draft.Power, cast.Area, draft.SpeedMul, cast.HoldScale, cast.Brush, cast.Brush01);
        }

        private void AcceptCast308(SpellCast cast, Vector3 origin, Vector3 forward)
        {
            CastAccepted?.Invoke(cast, origin, forward);
            _spells308?.OnCastAccepted(new SpellCastContext(cast, RowFor(cast.Letter), Time.time, this, Fx308));
        }

        // The accept of a cast its handler has judged. CastAccepted is a read-only re-broadcast that presentation listens to
        // (the deploy layer, the audio): each listener runs inside its own guard, so one that throws is reported and the
        // others, and the accept hooks (rules: a consumed empowerment, a companion shot, the last element), still run.
        // The 36 legacy glyphs keep AcceptCast308 above, raised exactly as before #308.
        private void AcceptJudged308(SpellCast cast, Vector3 origin, Vector3 forward)
        {
            var listeners = CastAccepted;
            if (listeners != null)
                foreach (Action<SpellCast, Vector3, Vector3> listener in listeners.GetInvocationList())
                {
                    try { listener(cast, origin, forward); }
                    catch (Exception exception) { Debug.LogException(exception, this); }
                }
            _spells308?.OnCastAccepted(new SpellCastContext(cast, RowFor(cast.Letter), Time.time, this, Fx308));
        }

        private void NotifyEnemyHit308(in EnemyDamageResult result)
        {
            if (_spells308 == null) return;
            var source = result.Attack.Source;
            // player-side sources only, the same set that counts as boss contact
            if (source == DamageSource.Enemy || source == DamageSource.Unknown) return;
            _spells308.OnEnemyHit(result, TickContext308());
        }

        private void NotifyPlayerHit308(ref bool interruptsDrawing)
        {
            if (_spells308 == null || _playerVitals == null) return;
            _spells308.OnPlayerHit(_playerVitals.LastHit, TickContext308(), ref interruptsDrawing);
        }

        private void TickSpells308()
        {
            if (_spells308 == null) return;
            _buffs308.Expire(Time.time);
            _spells308.Tick(TickContext308());
        }

        // Death, rest and scene leave all pass here (the session toggles this component for rest and respawn).
        public void ClearSpells308(SpellClearReason reason)
        {
            _row308 = null;
            _buffs308.Clear();
            _spells308?.Clear(reason);
            _fxGuard308.EndAll();
        }

        // ---- a row run by its handler: Prepare -> ink -> Commit (rules) -> accept -> presentation ----
        // The order, and what may and may not be undone, is SpellDispatchRule308 (pure: the offline runner checks it).
        private void CastRegistered308(SpellCast cast, SpellRow row)
        {
            var steps = new HandlerCast308(this, cast, row);
            SpellDispatchRule308.Run(ref steps);
        }

        private struct HandlerCast308 : ISpellDispatchSteps308
        {
            readonly CombatLoopWiring _wiring;
            readonly SpellCast _cast;
            readonly SpellRow _row;
            readonly ISpellEffect _effect;
            readonly SpellCastContext _ctx;
            float _inkBefore;
            int _hitsBefore, _heldBefore;
            bool _committing;

            public HandlerCast308(CombatLoopWiring wiring, SpellCast cast, SpellRow row)
            {
                _wiring = wiring; _cast = cast; _row = row;
                _effect = wiring._spells308 != null ? wiring._spells308.Find(row.Handler) : null;
                var primary = wiring.PrimaryStage308(row, out SpellRow ballistics);
                _ctx = new SpellCastContext(cast, row, Time.time, wiring, wiring._fxGuard308, primary, ballistics);
                _inkBefore = 0f; _hitsBefore = 0; _heldBefore = 0; _committing = false;
            }

            // a refused Prepare or missing ink is "no cast": no misfire ink on top (section 8)
            public bool Prepare() => _effect != null && _wiring._ink != null && _effect.Prepare(_ctx);

            public bool Spend()
            {
                _inkBefore = _wiring._ink.Value;
                return _wiring.TrySpendSpell(_wiring._config.SpellInkCost * Mathf.Max(0f, _row.F("cost", 1f)), _cast);
            }

            // Rules only. From here until Present nothing reaches the presenter: what the handler asks for waits.
            public bool Commit()
            {
                _hitsBefore = _wiring._pendingCasts.Count; _heldBefore = _ctx.Primary != null ? _ctx.Primary.HeldShots : 0; _committing = true;
                _wiring._fxGuard308.Hold();
                return _effect.Commit(_ctx);
            }

            // The one refund of the handler route. The hits this Commit scheduled go first (the wiring's own, and the shot its
            // first stage holds in flight), then the ink comes back.
            public void Withdraw(bool faulted)
            {
                if (_committing)
                {
                    _committing = false;
                    _wiring._fxGuard308.Release(false);
                    _wiring.Unschedule308(_hitsBefore);
                    _ctx.Primary?.Recall(_heldBefore);
                    if (faulted)
                    {
                        // the handler threw half way: its lasting state is unknown, so it starts clean (nothing of this
                        // cast stays switched on with the ink given back)
                        try { _effect.Clear(SpellClearReason.Faulted); }
                        catch (Exception exception) { Debug.LogException(exception, _wiring); }
                    }
                }
                _wiring._ink.Restore(_inkBefore);
            }

            public void Accept() => _wiring.AcceptJudged308(_cast, _wiring.PlayerPosition(), _wiring.PlayerForward());

            public void Present()
            {
                if (!_committing) return;
                _committing = false;
                _wiring._fxGuard308.Release(true);
            }

            public void Fail() => _wiring._brushAdapter?.NotifyCastFailed();
            public void Report(Exception exception) => Debug.LogException(exception, _wiring);
        }

        // Takes back every scheduled hit added since `count` (the hits of one withdrawn Commit; nothing else can be added
        // while a Commit runs).
        private void Unschedule308(int count)
        {
            if (count >= 0 && _pendingCasts.Count > count) _pendingCasts.RemoveRange(count, _pendingCasts.Count - count);
        }

        // The handler that flies the first stage of a single shot: the handler of the glyph without a final (Spellcraft Bible
        // chapter 4; PrimaryShotRule308), when the registry holds it. null = the lock-on single judgement.
        private ISpellPrimaryStage308 PrimaryStage308(SpellRow row, out SpellRow ballistics)
        {
            ballistics = null;
            char letter = PrimaryShotRule308.BallisticsLetter(row);
            if (letter == PrimaryShotRule308.None || _spells308 == null) return null;
            if (row.Is(letter)) ballistics = row;
            else if (_resolver == null || !_resolver.TryRow(letter, out ballistics)) { ballistics = null; return null; }
            string handler = PrimaryShotRule308.StageHandler(ballistics, _spells308.Has(ballistics.Handler));
            var stage = handler.Length > 0 ? _spells308.Find(handler) as ISpellPrimaryStage308 : null;
            if (stage == null) ballistics = null;
            return stage;
        }

        // ---- ISpellCastHost ----
        Transform ISpellCastHost.Player => SummonPlayer;
        Vector3 ISpellCastHost.PlayerPosition => PlayerPosition();
        Vector3 ISpellCastHost.PlayerForward => PlayerForward();
        PlayerVitals ISpellCastHost.PlayerVitals => _playerVitals;
        InkPool ISpellCastHost.Ink => _ink;
        CombatConfigSO ISpellCastHost.Config => _config;
        IReadOnlyList<EnemyVitals> ISpellCastHost.Targets => _targets;
        SpellBuffState308 ISpellCastHost.Buffs => _buffs308;
        EnemyVitals ISpellCastHost.AimedTarget() => AimedTarget();
        Vector3 ISpellCastHost.AimPoint(float range) => AimPoint(range);
        bool ISpellCastHost.TargetVisible(EnemyVitals target, Vector3 origin) => TargetVisible(target, origin);
        float ISpellCastHost.DamageScale(Element element) => SummonDamageScale(element);
        // The judgement bodies of the main file raise CastPlanned themselves, as their last step. That re-broadcast is read by
        // presentation and measurement listeners (the deploy layer keeps the plan there). When a HANDLER asks for a plan the
        // listeners are taken off for the length of the body and told right after it, at the same point, each inside its own
        // guard: a listener that throws is reported, the plan stays judged, and the exception never reaches the handler's
        // Commit (where it would withdraw the cast and give its ink back: a presentation failure never changes rules).
        // The 36 legacy glyphs call the bodies directly and raise the event exactly as before #308.
        CastPlan ISpellCastHost.PlanSingle(in SpellCast cast, bool feedAdapter)
        {
            var listeners = MutePlans308(); CastPlan plan = null;
            try { plan = PlanSingle(cast, feedAdapter); }
            finally { TellPlan308(listeners, plan); }
            return plan;
        }

        CastPlan ISpellCastHost.PlanCone(in SpellCast cast, bool feedAdapter)
        {
            var listeners = MutePlans308(); CastPlan plan = null;
            try { plan = PlanCone(cast, feedAdapter); }
            finally { TellPlan308(listeners, plan); }
            return plan;
        }

        CastPlan ISpellCastHost.PlanCircle(in SpellCast cast, bool feedAdapter)
        {
            var listeners = MutePlans308(); CastPlan plan = null;
            try { plan = PlanCircle(cast, feedAdapter); }
            finally { TellPlan308(listeners, plan); }
            return plan;
        }

        CastPlan ISpellCastHost.PlanPath(in SpellCast cast, bool feedAdapter)
        {
            var listeners = MutePlans308(); CastPlan plan = null;
            try { plan = PlanPath(cast, feedAdapter); }
            finally { TellPlan308(listeners, plan); }
            return plan;
        }

        CastPlan ISpellCastHost.PlanVolley(in SpellCast cast, bool feedAdapter, IReadOnlyList<float> shotWeights)
        {
            var listeners = MutePlans308(); CastPlan plan = null;
            try { plan = PlanVolley(cast, feedAdapter, shotWeights); }
            finally { TellPlan308(listeners, plan); }
            return plan;
        }

        private Action<CastPlan> MutePlans308()
        {
            var listeners = CastPlanned; CastPlanned = null;
            return listeners;
        }

        // plan = null: the body threw (a rule fault, not a presentation one): the listeners go back and nothing is told.
        private void TellPlan308(Action<CastPlan> listeners, CastPlan plan)
        {
            // a listener that joined while the body ran (nothing does today) is kept: it hears the next plan
            var joined = CastPlanned;
            CastPlanned = listeners;
            if (joined != null) CastPlanned += joined;
            if (plan == null || listeners == null) return;
            foreach (Action<CastPlan> listener in listeners.GetInvocationList())
            {
                try { listener(plan); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
        }

        PlannedHit ISpellCastHost.ScheduleHit(CastPlan plan, EnemyVitals target, float impactTime, float power)
        {
            if (plan == null || target == null || !float.IsFinite(impactTime) || !float.IsFinite(power) || power <= 0f) return null;
            return Schedule(plan, target, impactTime, power);
        }

        // The checks of the giyeok trace hit, for any handler: this wiring's own target, the life the hit was aimed at,
        // a real attack identity, a player-side direct source, and line of sight.
        EnemyDamageResult ISpellCastHost.ApplyDirectHit(EnemyVitals target, uint life, float power, Vector3 origin, AttackProvenance attack, char letter, bool piercesActors)
        {
            bool direct = attack.Source == DamageSource.PlayerDirect || attack.Source == DamageSource.Companion ||
                attack.Source == DamageSource.Retaliation || attack.Source == DamageSource.Harmony;
            if (!isActiveAndEnabled || target == null || !target.IsAlive || !target.isActiveAndEnabled || target.LifeRevision != life
                || !_targets.Contains(target) || !float.IsFinite(power) || power <= 0 || !direct
                || attack.AttackId <= 0 || attack.Instigator == null || !GiyeokTargetVisible(target, origin, piercesActors)) return default;
            return ApplyConfirmedEnemyHit(target, power, attack, letter);
        }

        EnemyDamageResult ISpellCastHost.ApplyPersistentHit(EnemyVitals target, float power, Vector3 origin, AttackProvenance attack, char letter)
            => ApplyPersistentSpellHit(target, power, origin, attack, letter);

        // the stroke adapter is presentation: whatever it does with the notice, the handler's rules go on
        void ISpellCastHost.PresentationOwned()
        {
            try { _brushAdapter?.NotifyFieldPresentationOwned(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        // Share of the cast power one volley shot carries: equal shares, or the caller's weights normalised by their sum.
        private static bool ValidWeights308(IReadOnlyList<float> weights, int shots, out float sum)
        {
            sum = 0f;
            if (weights == null) return false;
            if (weights.Count != shots) throw new ArgumentException("Volley weights must match the shot count (" + shots + ").");
            for (int i = 0; i < weights.Count; i++)
            {
                if (!float.IsFinite(weights[i]) || weights[i] < 0f) throw new ArgumentException("Volley weights must be finite and not negative.");
                sum += weights[i];
            }
            if (sum <= 0f) throw new ArgumentException("Volley weights must not all be zero.");
            return true;
        }
    }
}
