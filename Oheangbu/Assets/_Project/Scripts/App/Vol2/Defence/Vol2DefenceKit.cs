using System;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;

namespace Oheangbu.App
{
    /// <summary>What paying for one accepted cast did (the lab CSV and the log read it).</summary>
    public readonly struct Vol2CastPayment
    {
        public readonly char Letter;
        public readonly string Kind;            // Spend | Parry | Recovery | Free
        public readonly bool Paid;
        public readonly string Refusal;
        public readonly decimal InkBefore, InkAfter;
        public readonly decimal HpReserved;     // 걱 only: the HP the recovery will give over its life
        public Vol2CastPayment(char letter, string kind, bool paid, string refusal, decimal before, decimal after, decimal hpReserved)
        { Letter = letter; Kind = kind; Paid = paid; Refusal = refusal ?? ""; InkBefore = before; InkAfter = after; HpReserved = hpReserved; }
        /// <summary>Ink points taken by the cast itself (positive); a parry takes none at cast time (its debt comes later).</summary>
        public decimal InkSpent => InkBefore - InkAfter;
        public override string ToString() => Letter + " " + Kind + (Paid ? " paid" : " REFUSED " + Refusal) + " ink " + InkBefore.ToString(CultureInfo.InvariantCulture) + "->" + InkAfter.ToString(CultureInfo.InvariantCulture) +
            (HpReserved > 0m ? " hp+" + HpReserved.ToString(CultureInfo.InvariantCulture) : "");
    }

    /// <summary>S5-b slice 2: the defence side of one vol.2 combat scope, as one object the wiring owns - ink payment of accepted casts, the parry
    /// service, the posture bridge, the 갈무리 service and the 걱 executor, all on the one scope clock and the one numbers profile.
    /// Plain C#, built from objects (an edit-mode fixture builds it with a ManualVol2Clock and plain objects; the wiring builds it from the
    /// route's adapter). Nothing here is static.</summary>
    public sealed class Vol2DefenceKit
    {
        public DefenceNumbersV3Dto Numbers { get; }
        public IVol2Clock Clock { get; }
        public float HpPerUnit { get; }
        public Vol2InkAccount Ink { get; }
        public Vol2SessionLedger Session { get; }
        public Vol2TargetBook Targets { get; }
        public Vol2TelegraphBook Telegraphs { get; } = new Vol2TelegraphBook();
        /// <summary>Interned ids of windows and encounters for the 걱 gate.</summary>
        public Vol2KeyBook Keys { get; } = new Vol2KeyBook();
        public Vol2PostureBridge Posture { get; }
        public Vol2ParryService Parry { get; }
        public Vol2GatherService Gather { get; }
        public Vol2RecoveryExecutor Recovery { get; }
        public Vol2CastPayment LastPayment { get; private set; }
        /// <summary>The ports for slice 3's cast coordinator (bound by Vol2DefenceInstaller): the real damage entry and the "already paid" ink port.</summary>
        public Vol2DefenceDamageSink DamageSink { get; }
        public Vol2PaidInkSpender PaidInk { get; }
        /// <summary>true (default): the gate takes the ink of an ordinary cast, before CastAccepted - and the coordinator's ink port must be PaidInk.
        /// false: the gate leaves ordinary casts to whoever else pays (the coordinator's temporary spender), and only the parry and the 걱 recovery
        /// are handled here - used when the kit was bound without the installer, so a cast is never charged twice.</summary>
        public bool PaysOrdinaryCasts { get; set; } = true;
        readonly Dictionary<long, decimal> _paid = new Dictionary<long, decimal>();
        readonly Queue<long> _paidOrder = new Queue<long>();
        public int PaymentsMade { get; private set; }
        public int PaymentsRefused { get; private set; }
        public event Action<Vol2CastPayment> Paid;

        public Vol2DefenceKit(NumbersV3Dto numbers, IVol2Clock clock, Oheangbu.Combat.InkPool ink, Oheangbu.Combat.PlayerVitals player,
            Vol2SessionLedger session, IVol2CombatContext context, float hpPerUnit = Vol2Units.DefaultHpPerUnit)
        {
            if (numbers == null || !numbers.IsValidated) throw new ArgumentException("validated numbers required", nameof(numbers));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (hpPerUnit <= 0f || float.IsNaN(hpPerUnit) || float.IsInfinity(hpPerUnit)) throw new ArgumentOutOfRangeException(nameof(hpPerUnit));
            HpPerUnit = hpPerUnit;
            Numbers = new DefenceNumbersV3Dto(numbers);
            Session = session ?? new Vol2SessionLedger(Numbers);
            Ink = new Vol2InkAccount(ink);
            Targets = new Vol2TargetBook(Session.Keys);
            Posture = new Vol2PostureBridge(Numbers, Clock, Targets);
            Parry = new Vol2ParryService(Numbers, Clock, Ink, Posture, Telegraphs, Targets);
            Gather = new Vol2GatherService(Session, Clock, Ink, Targets, hpPerUnit);
            Recovery = new Vol2RecoveryExecutor(Session, Clock, Ink, player, context);
            DamageSink = new Vol2DefenceDamageSink(this);
            PaidInk = new Vol2PaidInkSpender(this);
        }

        /// <summary>The ink points the gate took for this cast (every accepted cast it paid for, 걱 and the parry included); false for a cast it did not pay
        /// (or one long forgotten: the last 256 are kept).</summary>
        public bool TryGetPaid(CastId cast, out decimal paid) => _paid.TryGetValue(cast.Value, out paid);

        /// <summary>The payment gate of the adapter (Vol2GlyphAdapter.Payment): called after the plan is built and BEFORE CastAccepted is raised, so a
        /// cast that cannot be paid never reaches an executor. Returns null = paid, else the refusal (the adapter turns it into
        /// PaymentRefused). A bare ㅓ letter opens a parry attempt (ink 0 is allowed; the debt is settled later); 걱 goes to the recovery (its own
        /// ink 26, cooldown and combat gate); everything else pays the plan's cost from the pool.</summary>
        public string TryPay(Vol2CastDecision d)
        {
            decimal before = Ink.Units;
            string kind, refusal = null;
            decimal reserved = 0m;
            if (d.IsParry)
            {
                kind = "Parry";
                var r = Parry.Begin(d.CastId.Value, d.Glyph.Body);
                if (!r.Accepted) refusal = "Parry:" + r.Reason;
            }
            else if (d.IsRecovery)
            {
                kind = "Recovery";
                var r = Recovery.Begin(d.CastId);
                if (!r.Accepted) refusal = "Recovery:" + r.Reason;
                else reserved = r.HpReserved;
            }
            else if (!PaysOrdinaryCasts) kind = "Deferred";
            else
            {
                decimal cost = d.Plan != null ? d.Plan.Cost : 0m;
                kind = cost > 0m ? "Spend" : "Free";
                if (!Ink.TrySpend(cost)) refusal = "InkShort need=" + cost.ToString(CultureInfo.InvariantCulture) + " have=" + before.ToString(CultureInfo.InvariantCulture);
            }
            var payment = new Vol2CastPayment(d.Letter, kind, refusal == null, refusal, before, Ink.Units, reserved);
            if (refusal == null && kind != "Deferred")
            {
                // every cast the gate paid for is on record, 걱 (26, taken by the recovery's own rules) and the parry (0) included: whoever executes
                // the cast later (slice 4's Leave executor, the coordinator) asks PaidInk and finds it paid, so nothing is charged twice
                _paid[d.CastId.Value] = d.Plan != null ? d.Plan.Cost : 0m; _paidOrder.Enqueue(d.CastId.Value);
                while (_paidOrder.Count > 256) _paid.Remove(_paidOrder.Dequeue());
            }
            LastPayment = payment;
            if (refusal == null) PaymentsMade++; else PaymentsRefused++;
            Paid?.Invoke(payment);
            return refusal;
        }

        /// <summary>Per frame, from the wiring: settle an expired parry attempt, run the recovery.</summary>
        public void Tick()
        {
            Parry.Tick();
            Recovery.Tick();
        }

        /// <summary>Death or leaving the scene: the recovery stops, the parry attempt is dropped.</summary>
        public void OnPlayerDied()
        {
            Recovery.Cancel();
            Parry.Reset();
        }

        /// <summary>A new encounter / scene reset of the target side: every posture and window count starts from zero.</summary>
        public void ResetEncounter()
        {
            Posture.Reset();
            Telegraphs.Clear();
        }

        /// <summary>The spell lab (design section 12 step 2): before each row the previous row's state is gone - ink full, no parry cooldown, no
        /// running recovery, no posture. The session ledger (갈무리 ink given, the 걱 rest budget and cooldown) is NOT reset here: it is the
        /// thing that must survive, and a lab that wants a clean ledger builds a new one.</summary>
        public void ResetForRow()
        {
            Recovery.Cancel();
            Parry.Reset();
            Posture.Reset();
            Ink.Refill(1f);
            LastPayment = default;
        }
    }
}
