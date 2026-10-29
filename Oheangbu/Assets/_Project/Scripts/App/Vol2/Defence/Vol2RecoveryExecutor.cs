using System;
using System.Globalization;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;

namespace Oheangbu.App
{
    /// <summary>What the 걱 recovery needs to know about the fight and only the wiring can answer (TEST approximations until the game has
    /// real encounter / boss-phase ids): in combat or not, which encounter and boss phase, whether a weak-point window is open and which one.</summary>
    public interface IVol2CombatContext
    {
        bool Engaged { get; }
        long EncounterId { get; }
        long BossPhaseId { get; }
        bool WeakWindowOpen { get; }
        long WindowId { get; }
    }

    public readonly struct Vol2RecoveryRecord
    {
        public readonly string Kind;            // Begin | Tick | Damage | Cancel | Rest
        public readonly bool Accepted;
        public readonly string Reason;
        public readonly long CastId;
        public readonly decimal HpReserved;     // the cast's reserved HP (30, or less out of combat when the rest budget is short)
        public readonly decimal HpHealed;       // HP really given to the player by this step
        public readonly decimal InkSpent;
        public readonly bool Active;            // the recovery still runs after this step
        public readonly bool Engaged, ConsumedWindow;
        public Vol2RecoveryRecord(string kind, bool accepted, string reason, long cast, decimal reserved, decimal healed, decimal ink, bool active, bool engaged, bool window)
        { Kind = kind; Accepted = accepted; Reason = reason; CastId = cast; HpReserved = reserved; HpHealed = healed; InkSpent = ink; Active = active; Engaged = engaged; ConsumedWindow = window; }
        public override string ToString() => Kind + " " + Reason + (Accepted ? "" : " (refused)") + " cast=" + CastId + " reserved=" + HpReserved.ToString(CultureInfo.InvariantCulture) +
            " healed=" + HpHealed.ToString(CultureInfo.InvariantCulture) + " ink=" + InkSpent.ToString(CultureInfo.InvariantCulture) + (Active ? " active" : "") + (Engaged ? " in-combat" : " out-of-combat");
    }

    /// <summary>S5-b slice 2 (design sections 8 and 9, D308-145): the 걱 self-recovery. GeokRecoveryV3 keeps the ledger (HP 30 over 3 s on the
    /// common clock, ink 26, cooldown 20 s; in combat only inside the weak-point window and once per encounter / boss phase; out of combat
    /// capped at 30 HP per rest; no attack budget, no material, no ink-recovery pause); this executor gives the HP to the player and takes
    /// the ink. Being hit ends it at once (the HP already given stays). No visual in this slice - a log line / the lab column.</summary>
    public sealed class Vol2RecoveryExecutor
    {
        const long TickIntervalUs = 50000;
        readonly Vol2SessionLedger _ledger;
        readonly IVol2Clock _clock;
        readonly Vol2InkAccount _ink;
        readonly PlayerVitals _player;
        readonly IVol2CombatContext _context;
        long _event, _lastTickUs;
        public Vol2RecoveryRecord Last { get; private set; }
        public decimal TotalHealed { get; private set; }
        public event Action<Vol2RecoveryRecord> Recorded;
        public bool Active => _ledger.Recovery.Active;
        public long ActiveCastId => _ledger.Recovery.CastId;

        public Vol2RecoveryExecutor(Vol2SessionLedger ledger, IVol2Clock clock, Vol2InkAccount ink, PlayerVitals player, IVol2CombatContext context)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _ink = ink ?? throw new ArgumentNullException(nameof(ink));
            _player = player;
            _context = context;
        }

        decimal Hp => _player == null ? 0m : Math.Min((decimal)_player.MaxHp, (decimal)(_player.Hp01 * _player.MaxHp));
        decimal MaxHp => _player == null ? 0m : (decimal)_player.MaxHp;

        public Vol2RecoveryRecord Begin(CastId cast)
        {
            if (_player == null) return Publish(new Vol2RecoveryRecord("Begin", false, "NoPlayer", cast.Value, 0m, 0m, 0m, false, false, false));
            bool engaged = _context != null && _context.Engaged;
            var input = new GeokRecoveryV3.Input(++_event, GeokRecoveryV3.Action.Begin, _clock.NowUs, cast.Value, Hp, MaxHp, _ink.Units, 0m, engaged,
                _context != null ? _context.EncounterId : 0, _context != null ? _context.BossPhaseId : 0, _context != null ? _context.WindowId : 0, _context != null && _context.WeakWindowOpen);
            var step = GeokRecoveryV3.Apply(_ledger.Recovery, input);
            if (!step.Accepted) return Publish(new Vol2RecoveryRecord("Begin", false, step.Reason, cast.Value, 0m, 0m, 0m, _ledger.Recovery.Active, engaged, false));
            _ledger.Recovery = step.State;
            _lastTickUs = _clock.NowUs;
            decimal spent = step.InkDelta < 0m ? _ink.SpendClamped(-step.InkDelta) : 0m;
            return Publish(new Vol2RecoveryRecord("Begin", true, step.Reason, cast.Value, step.State.Reserved, 0m, spent, step.State.Active, engaged, step.ConsumesWindow));
        }

        /// <summary>Per frame (the rules are fed at most every 50 ms, and always at the end of the 3 s).</summary>
        public void Tick()
        {
            var s = _ledger.Recovery;
            if (!s.Active) return;
            long now = _clock.NowUs;
            bool last = now - s.StartedUs >= s.Numbers.HealingLifeUs || _player == null || _player.Hp01 <= 0f;
            if (!last && now - _lastTickUs < TickIntervalUs) return;
            _lastTickUs = now;
            var input = new GeokRecoveryV3.Input(++_event, GeokRecoveryV3.Action.Tick, now, s.CastId, Hp, MaxHp, _ink.Units);
            var step = GeokRecoveryV3.Apply(s, input);
            if (!step.Accepted) { Publish(new Vol2RecoveryRecord("Tick", false, step.Reason, s.CastId, s.Reserved, 0m, 0m, s.Active, false, false)); return; }
            _ledger.Recovery = step.State;
            decimal healed = 0m;
            if (step.HpDelta > 0m && _player != null) healed = (decimal)_player.Heal((float)step.HpDelta);
            TotalHealed += healed;
            if (healed > 0m || !step.State.Active) Publish(new Vol2RecoveryRecord("Tick", true, step.Reason, s.CastId, step.State.Reserved, healed, 0m, step.State.Active, false, false));
        }

        /// <summary>The player took a real hit: the recovery stops at once (the HP given so far stays).</summary>
        public Vol2RecoveryRecord OnPlayerDamaged(float damage)
        {
            var s = _ledger.Recovery;
            if (!s.Active || !(damage > 0f)) return default;
            var step = GeokRecoveryV3.Apply(s, new GeokRecoveryV3.Input(++_event, GeokRecoveryV3.Action.Damage, _clock.NowUs, s.CastId, Hp, MaxHp, _ink.Units, (decimal)damage));
            if (!step.Accepted) return Publish(new Vol2RecoveryRecord("Damage", false, step.Reason, s.CastId, s.Reserved, 0m, 0m, s.Active, false, false));
            _ledger.Recovery = step.State;
            return Publish(new Vol2RecoveryRecord("Damage", true, "Interrupted", s.CastId, s.Reserved, 0m, 0m, step.State.Active, false, false));
        }

        /// <summary>Death, a scene leave, a forced end.</summary>
        public Vol2RecoveryRecord Cancel()
        {
            var s = _ledger.Recovery;
            if (!s.Active) return default;
            var step = GeokRecoveryV3.Apply(s, new GeokRecoveryV3.Input(++_event, GeokRecoveryV3.Action.Cancel, _clock.NowUs, s.CastId, Hp, MaxHp, _ink.Units));
            if (!step.Accepted) return Publish(new Vol2RecoveryRecord("Cancel", false, step.Reason, s.CastId, s.Reserved, 0m, 0m, s.Active, false, false));
            _ledger.Recovery = step.State;
            return Publish(new Vol2RecoveryRecord("Cancel", true, "Cancelled", s.CastId, s.Reserved, 0m, 0m, step.State.Active, false, false));
        }

        /// <summary>A rest (a lantern, a bonfire): the out-of-combat budget (HP 30) is whole again. NOTHING in the game raises a rest event yet:
        /// whoever owns resting calls this (open item).</summary>
        public Vol2RecoveryRecord NotifyRest()
        {
            var s = _ledger.Recovery;
            var step = GeokRecoveryV3.Apply(s, new GeokRecoveryV3.Input(++_event, GeokRecoveryV3.Action.Rest, _clock.NowUs, 0, Hp, MaxHp, _ink.Units));
            if (!step.Accepted) return Publish(new Vol2RecoveryRecord("Rest", false, step.Reason, 0, 0m, 0m, 0m, s.Active, false, false));
            _ledger.Recovery = step.State;
            return Publish(new Vol2RecoveryRecord("Rest", true, "Rested", 0, step.State.OutsideRemaining, 0m, 0m, step.State.Active, false, false));
        }

        Vol2RecoveryRecord Publish(Vol2RecoveryRecord r) { Last = r; Recorded?.Invoke(r); return r; }
    }
}
