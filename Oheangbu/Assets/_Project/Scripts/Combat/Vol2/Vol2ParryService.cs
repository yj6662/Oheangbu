using System;
using System.Globalization;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.Combat
{
    /// <summary>What one parry step did (begin / impact / timeout), for the log and the lab CSV.</summary>
    public readonly struct Vol2ParryRecord
    {
        public readonly string Kind;            // Begin | Impact | Timeout
        public readonly bool Accepted;
        public readonly string Reason;          // the rules' reason: Begin, Defended, Failed, Unblocked, Cooldown, Input, ...
        public readonly long AttemptId, EventId;
        public readonly bool Defended;
        public readonly decimal Posture;
        public readonly decimal InkDebt;        // ink points taken from the pool for this step (0 when nothing was owed)
        public readonly bool Settled;
        public readonly bool Projectile, UnknownAttack;
        public readonly long TelegraphUs;
        public readonly Body? AttackBody;
        public Vol2ParryRecord(string kind, bool accepted, string reason, long attemptId, long eventId, bool defended, decimal posture, decimal inkDebt,
            bool settled, bool projectile, bool unknown, long telegraphUs, Body? attackBody)
        { Kind = kind; Accepted = accepted; Reason = reason; AttemptId = attemptId; EventId = eventId; Defended = defended; Posture = posture; InkDebt = inkDebt;
          Settled = settled; Projectile = projectile; UnknownAttack = unknown; TelegraphUs = telegraphUs; AttackBody = attackBody; }
        public override string ToString() => Kind + " " + Reason + (Accepted ? "" : " (refused)") + " attempt=" + AttemptId + " event=" + EventId +
            (Defended ? " DEFENDED" : "") + " posture=" + Posture.ToString(CultureInfo.InvariantCulture) + " debt=" + InkDebt.ToString(CultureInfo.InvariantCulture) +
            (Kind == "Impact" ? " projectile=" + (Projectile ? "Y" : "N") + (UnknownAttack ? "(unknown attack)" : "") + " telegraph=" + TelegraphUs + "us" : "");
    }

    /// <summary>S5-b slice 2 (design section 9): the vol.2 parry, between the old judge and ParryRulesV3. Begin = the player's bare ㅓ letter was
    /// accepted (ink 0 allowed; the debt is settled later, once). Resolve = an enemy impact asked ParryJudge.ResolveImpact, which asks here
    /// BEFORE any damage is applied: Defended -> the caller voids all of it; correct body and a telegraph of at least 0.8 s -> posture 40
    /// through the bridge; a wrong body or a neutral attack still defends with no posture; a projectile is never defended and does not use
    /// the attempt up. Tick settles an attempt whose window ended without an impact (ink debt min(ink, 14) once, no refund).</summary>
    public sealed class Vol2ParryService : IVol2ImpactResolver
    {
        readonly DefenceNumbersV3Dto _n;
        readonly IVol2Clock _clock;
        readonly Vol2InkAccount _ink;
        readonly Vol2PostureBridge _posture;
        readonly Vol2TelegraphBook _telegraphs;
        readonly Vol2TargetBook _targets;
        ParryRulesV3.State _state;
        long _synthetic = 1000000000000L;     // event ids for impacts that carry no AttackId

        public Vol2ParryRecord Last { get; private set; }
        public event Action<Vol2ParryRecord> Recorded;
        public bool Pending => _state.Pending;
        public long AttemptId => _state.AttemptId;
        public Body Body => _state.Body;
        public long StartedUs => _state.StartedUs;
        public int UnknownAttacks { get; private set; }

        public Vol2ParryService(DefenceNumbersV3Dto numbers, IVol2Clock clock, Vol2InkAccount ink, Vol2PostureBridge posture, Vol2TelegraphBook telegraphs, Vol2TargetBook targets)
        {
            _n = numbers ?? throw new ArgumentNullException(nameof(numbers));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _ink = ink ?? throw new ArgumentNullException(nameof(ink));
            _posture = posture ?? throw new ArgumentNullException(nameof(posture));
            _telegraphs = telegraphs ?? throw new ArgumentNullException(nameof(telegraphs));
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
            _state = new ParryRulesV3.State(_n);
        }

        /// <summary>The player's bare ㅓ letter was accepted: open an attempt. Refused (cooldown 0.8 s, duplicate, bad input) = no attempt.</summary>
        public Vol2ParryRecord Begin(long attemptId, Body body)
        {
            var step = ParryRulesV3.Begin(_state, new ParryRulesV3.Input(attemptId, _clock.NowUs, body, _ink.Units));
            if (!step.Accepted) return Publish(new Vol2ParryRecord("Begin", false, step.Reason, attemptId, 0, false, 0m, 0m, false, false, false, 0, null));
            _state = step.State;
            decimal debt = step.InkDelta < 0m ? _ink.SpendClamped(-step.InkDelta) : 0m;   // an unsettled earlier attempt is paid now, once
            return Publish(new Vol2ParryRecord("Begin", true, step.Reason, attemptId, step.CommandId, false, 0m, debt, step.Settled, false, false, 0, null));
        }

        public Vol2ImpactVerdict Resolve(float now, Vector3 impactPoint, AttackProvenance attack)
        {
            if (!_state.Pending) return Vol2ImpactVerdict.Unblocked;   // no parry attempt stands: the hit lands, nothing to settle
            bool known = _telegraphs.TryGet(attack.AttackId, out var info);
            if (!known) UnknownAttacks++;
            bool projectile = known ? info.Projectile : true;         // no cue = not an authored attack: treated as a projectile (never defended)
            long telegraphUs = known ? info.TelegraphUs : 0;
            Body? body = Vol2Mapping.ToBody(attack.Element);
            var attacker = attack.Instigator as EnemyVitals;
            long eventId = attack.AttackId > 0 ? attack.AttackId : ++_synthetic;
            var impact = new ParryRulesV3.Impact(eventId, _state.AttemptId, _ink.Units, 0m, body, projectile, false,
                _targets.Id(attacker), attacker != null ? attacker.LifeRevision : 0, telegraphUs);
            var step = ParryRulesV3.Resolve(_state, impact, _clock.NowUs);
            if (!step.Accepted)
            {
                Publish(new Vol2ParryRecord("Impact", false, step.Reason, _state.AttemptId, eventId, false, 0m, 0m, false, projectile, !known, telegraphUs, body));
                return Vol2ImpactVerdict.Unblocked;
            }
            _state = step.State;
            decimal debt = step.InkDelta < 0m ? _ink.SpendClamped(-step.InkDelta) : 0m;
            if (step.Posture > 0m && attacker != null) _posture.ApplyParry(attacker);
            Publish(new Vol2ParryRecord("Impact", true, step.Reason, _state.AttemptId, eventId, step.Defended, step.Posture, debt, step.Settled, projectile, !known, telegraphUs, body));
            return step.Defended ? new Vol2ImpactVerdict(ParryOutcome.Success, Vol2Mapping.ToElement(_state.Body)) : Vol2ImpactVerdict.Unblocked;
        }

        /// <summary>Per frame: an attempt whose window ended with no impact is settled as a failure (the ink debt, once, no refund).</summary>
        public void Tick()
        {
            if (!_state.Pending) return;
            long now = _clock.NowUs;
            if (now - _state.StartedUs < _n.ParryWindowUs) return;
            long eventId = ++_synthetic;
            var step = ParryRulesV3.Resolve(_state, new ParryRulesV3.Impact(eventId, _state.AttemptId, _ink.Units, timeout: true), now);
            if (!step.Accepted) { Publish(new Vol2ParryRecord("Timeout", false, step.Reason, _state.AttemptId, eventId, false, 0m, 0m, false, false, false, 0, null)); return; }
            _state = step.State;
            decimal debt = step.InkDelta < 0m ? _ink.SpendClamped(-step.InkDelta) : 0m;
            Publish(new Vol2ParryRecord("Timeout", true, step.Reason, _state.AttemptId, eventId, false, 0m, debt, step.Settled, false, false, 0, null));
        }

        /// <summary>A new fight / a lab row: the attempt, its cooldown and its dedup sets start from nothing.</summary>
        public void Reset() { _state = new ParryRulesV3.State(_n); }

        Vol2ParryRecord Publish(Vol2ParryRecord r) { Last = r; Recorded?.Invoke(r); return r; }
    }
}
