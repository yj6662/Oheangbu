using System;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.Combat
{
    /// <summary>One real hit on the player: HP really lost (parry success and dodge never get here), who struck, when.</summary>
    public readonly struct Vol2PlayerHit
    {
        public readonly long HitId;
        public readonly float Amount;
        public readonly EnemyVitals Attacker;
        public readonly IncomingDamageKind Kind;
        public readonly long AtUs;
        public Vol2PlayerHit(long hitId, float amount, EnemyVitals attacker, IncomingDamageKind kind, long atUs)
        { HitId = hitId; Amount = amount; Attacker = attacker; Kind = kind; AtUs = atUs; }
        public bool Valid => HitId > 0;
        public override string ToString() => "hit#" + HitId + " " + Amount.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " HP @" + AtUs + "us";
    }

    /// <summary>S5-b slice 5 SEAM (design sections 5 and 9): the hit-confirmation hook the 멈 needs and slice 2 did not have. "맞을 때" = the
    /// player really loses HP (ConfirmedAmount greater than 0 after the incoming scales; a successful parry and a dodge never reach it).
    /// <see cref="Report"/> runs every <see cref="Confirmed"/> handler SYNCHRONOUSLY, before the caller does anything else with the hit; a
    /// handler (the 멈 seed) may call <see cref="GrantStaggerExemption"/> inside it. The body-stagger executor then asks
    /// <see cref="TakeStaggerExemption"/> BEFORE it plays its stagger: true once for that hit, never for the next one (the flag is cleared by the
    /// next Report). The decision is made before the stagger runs; nothing is cancelled afterwards. Damage and the letter interruption are
    /// not touched by this class.
    /// HOW IT IS FED TODAY: <see cref="Attach"/> subscribes PlayerVitals.Damaged (fires only for a real hit, LastHit is set before it). Delegates
    /// run in subscription order, so Attach must come BEFORE the stagger listener binds (WorldMacroPlayerReaction303.Bind), or the call to
    /// <see cref="Report"/> must be moved into PlayerVitals.TakeAttackDamage just before Damaged?.Invoke (one line, owner: slice 2).
    /// The stagger listener's own change (one line: <c>if (gate.TakeStaggerExemption()) return;</c> at the top of its OnDamaged) is an open
    /// item for its owner. One instance per combat scope, no statics.</summary>
    public sealed class Vol2SeedHitGate
    {
        readonly IVol2Clock _clock;
        PlayerVitals _vitals;
        long _next;
        bool _exemptPending;
        public Vol2PlayerHit Last { get; private set; }
        public long Reported { get; private set; }
        public long ExemptionsGranted { get; private set; }
        public long ExemptionsTaken { get; private set; }
        /// <summary>true = the latest hit was granted a stagger exemption that nobody has taken yet.</summary>
        public bool StaggerExemptPending => _exemptPending;
        public bool Attached => _vitals != null;

        public event Action<Vol2PlayerHit> Confirmed;

        public Vol2SeedHitGate(IVol2Clock clock) { _clock = clock ?? throw new ArgumentNullException(nameof(clock)); }

        public void Attach(PlayerVitals vitals)
        {
            if (vitals == null) throw new ArgumentNullException(nameof(vitals));
            if (ReferenceEquals(_vitals, vitals)) return;
            Detach();
            _vitals = vitals; _lastSerial = 0;          // slice 8: a new player starts counting its hits from 0 again
            _vitals.Damaged += OnDamaged;
        }

        public void Detach()
        {
            if (_vitals != null) _vitals.Damaged -= OnDamaged;
            _vitals = null; _lastSerial = 0;
        }

        long _lastSerial;

        void OnDamaged(float amount)
        {
            // S5-b slice 8: a hit is reported ONCE however many ask first (the stagger listener may run before this subscription: EnsureReported)
            if (_vitals != null && _vitals.HitSerial308 != 0 && _vitals.HitSerial308 == _lastSerial) return;
            if (_vitals != null) _lastSerial = _vitals.HitSerial308;
            var info = _vitals != null ? _vitals.LastHit : default;
            Report(amount, info.Attacker, info.Kind);
        }

        /// <summary>S5-b slice 8 (closes the 멈 seam): the body-stagger listener asks THIS before <see cref="TakeStaggerExemption"/>, because it may have subscribed to
        /// PlayerVitals.Damaged before the gate did and so run first. If the player's latest hit was not reported yet it is reported now (the Confirmed handlers - the 멈 seed - run, and
        /// may grant the exemption); a hit already reported is not reported again. No change to the player's damage.</summary>
        public Vol2PlayerHit EnsureReported(PlayerVitals vitals)
        {
            if (vitals == null || !ReferenceEquals(vitals, _vitals) || vitals.HitSerial308 == 0) return default;
            if (vitals.HitSerial308 == _lastSerial) return Last;
            _lastSerial = vitals.HitSerial308;
            var info = vitals.LastHit;
            return Report(info.Amount, info.Attacker, info.Kind);
        }

        /// <summary>A real hit was confirmed. A non-positive or non-finite amount is not a hit (returns the default, nothing is raised).</summary>
        public Vol2PlayerHit Report(float amount, EnemyVitals attacker = null, IncomingDamageKind kind = IncomingDamageKind.Unspecified)
        {
            if (!(amount > 0f) || float.IsNaN(amount) || float.IsInfinity(amount)) return default;
            _exemptPending = false;
            var hit = new Vol2PlayerHit(++_next, amount, attacker, kind, _clock.NowUs);
            Last = hit; Reported++;
            var handlers = Confirmed;
            if (handlers == null) return hit;
            foreach (Action<Vol2PlayerHit> h in handlers.GetInvocationList())
            {
                try { h(hit); } catch (Exception e) { Debug.LogException(e); }
            }
            return hit;
        }

        /// <summary>Called by a Confirmed handler while the hit is dispatched: this hit's body stagger (and only this hit's) is exempt.</summary>
        public void GrantStaggerExemption()
        {
            if (!Last.Valid) return;
            if (!_exemptPending) ExemptionsGranted++;
            _exemptPending = true;
        }

        /// <summary>Asked by whatever plays the hit's body stagger, before it plays. true exactly once per granted hit.</summary>
        public bool TakeStaggerExemption()
        {
            if (!_exemptPending) return false;
            _exemptPending = false; ExemptionsTaken++;
            return true;
        }
    }
}
