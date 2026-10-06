using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // Handler id -> effect, plus the fan-out of the optional hooks (SPEC-SPELL-120-308 section 4). Built by
    // SpellEffectInstaller308 from the effects the container could build, one by one; checks build it with new.
    // Nothing here throws: an effect without an id, a second effect with an id that is already taken and an effect the
    // container could not build are left out and listed in Failures (the wiring reports them once). The other handlers and
    // the 36 existing spells go on. One effect that throws in a hook is reported and skipped the same way.
    public sealed class SpellEffectRegistry308
    {
        private readonly Dictionary<string, ISpellEffect> _byId = new Dictionary<string, ISpellEffect>(StringComparer.Ordinal);
        private readonly ISpellEffect[] _all;
        private readonly ISpellCostHook[] _cost;
        private readonly ISpellCastHook[] _cast;
        private readonly ISpellAcceptHook[] _accept;
        private readonly ISpellHitHook[] _hit;
        private readonly IPlayerHitHook[] _playerHit;

        private readonly List<string> _failures = new List<string>();

        public IReadOnlyList<ISpellEffect> All => _all;
        // Effects that are not in the registry, one line each ("<class>: <why>"). Empty = every registered effect is in.
        public IReadOnlyList<string> Failures => _failures;

        // failures = what the caller already could not build (SpellEffectList308.Build).
        public SpellEffectRegistry308(IEnumerable<ISpellEffect> effects, IEnumerable<string> failures = null)
        {
            if (failures != null) _failures.AddRange(failures);
            var all = new List<ISpellEffect>();
            var cost = new List<ISpellCostHook>(); var cast = new List<ISpellCastHook>(); var accept = new List<ISpellAcceptHook>();
            var hit = new List<ISpellHitHook>(); var playerHit = new List<IPlayerHitHook>();
            foreach (var effect in effects ?? Array.Empty<ISpellEffect>())
            {
                if (effect == null) { _failures.Add("(null): no effect"); continue; }
                string id;
                try { id = effect.Id; }
                catch (Exception exception) { _failures.Add(effect.GetType().Name + ": " + exception.GetType().Name + " " + exception.Message); continue; }
                if (string.IsNullOrEmpty(id)) { _failures.Add(effect.GetType().Name + ": a spell effect needs an id"); continue; }
                if (_byId.TryGetValue(id, out var first))
                { _failures.Add(effect.GetType().Name + ": the id '" + id + "' already belongs to " + first.GetType().Name); continue; }
                _byId.Add(id, effect); all.Add(effect);
                if (effect is ISpellCostHook c) cost.Add(c);
                if (effect is ISpellCastHook m) cast.Add(m);
                if (effect is ISpellAcceptHook a) accept.Add(a);
                if (effect is ISpellHitHook h) hit.Add(h);
                if (effect is IPlayerHitHook p) playerHit.Add(p);
            }
            _all = all.ToArray(); _cost = cost.ToArray(); _cast = cast.ToArray(); _accept = accept.ToArray();
            _hit = hit.ToArray(); _playerHit = playerHit.ToArray();
        }

        public bool Has(string id) => !string.IsNullOrEmpty(id) && _byId.ContainsKey(id);

        public ISpellEffect Find(string id)
        {
            return !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var effect) ? effect : null;
        }

        // Product of the cost hooks. A hook that answers a non-finite or negative number is ignored.
        public float CostScale(in SpellCastInfo cast, float now)
        {
            float scale = 1f;
            for (int i = 0; i < _cost.Length; i++)
            {
                try
                {
                    float one = _cost[i].CostScale(cast, now);
                    if (float.IsFinite(one) && one >= 0f) scale *= one;
                }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            return scale;
        }

        // Power and projectile speed only. A non-finite or negative result of a hook is dropped (the draft keeps its value).
        public void ModifyCast(ref SpellCastDraft draft, float now)
        {
            for (int i = 0; i < _cast.Length; i++)
            {
                float power = draft.Power, speed = draft.SpeedMul;
                try { _cast[i].ModifyCast(ref draft, now); }
                catch (Exception exception) { Debug.LogException(exception); draft.Power = power; draft.SpeedMul = speed; continue; }
                if (!float.IsFinite(draft.Power) || draft.Power < 0f) draft.Power = power;
                if (!float.IsFinite(draft.SpeedMul) || draft.SpeedMul <= 0f) draft.SpeedMul = speed;
            }
        }

        public void OnCastAccepted(in SpellCastContext ctx)
        {
            for (int i = 0; i < _accept.Length; i++)
            {
                try { _accept[i].OnCastAccepted(ctx); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            for (int i = 0; i < _hit.Length; i++)
            {
                try { _hit[i].OnEnemyHit(result, ctx); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        public void OnPlayerHit(in PlayerHitInfo hit, in SpellTickContext ctx, ref bool interruptsDrawing)
        {
            for (int i = 0; i < _playerHit.Length; i++)
            {
                try { _playerHit[i].OnPlayerHit(hit, ctx, ref interruptsDrawing); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        public void Tick(in SpellTickContext ctx)
        {
            for (int i = 0; i < _all.Length; i++)
            {
                try { _all[i].Tick(ctx); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        public void Clear(SpellClearReason reason)
        {
            for (int i = 0; i < _all.Length; i++)
            {
                try { _all[i].Clear(reason); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
    }
}
