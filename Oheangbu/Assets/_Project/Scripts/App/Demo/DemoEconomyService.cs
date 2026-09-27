using System;
using System.Threading;
using Oheangbu.Data.Demo;

namespace Oheangbu.App.Demo
{
    public enum DemoEconomyCommitStatus { Saved, Conflict, Failed }

    /// <summary>
    /// Read returns a detached snapshot. TryCommit must compare the live Tongbo AND economy to expected,
    /// persist a complete campaign candidate atomically, then publish it. Failed/conflict/throw leaves both
    /// in-memory and durable state unchanged. Apply runtime stats only after Saved, not during serialization.
    /// </summary>
    public interface IDemoEconomyStore
    {
        DemoEconomySnapshot Read();
        DemoEconomyCommitStatus TryCommit(DemoEconomySnapshot expected, DemoEconomySnapshot next, out string error);
    }

    public enum DemoPurchaseStatus
    {
        Purchased, DuplicateRequest, Busy, InvalidRequest, InvalidState,
        StaleQuote, MaximumLevel, InsufficientTongbo, SaveFailed, Conflict
    }

    public readonly struct DemoUpgradeQuote
    {
        public DemoUpgradeTrack Track { get; }
        public int CurrentLevel { get; }
        public int MaximumLevel { get; }
        public int Cost { get; }
        public int Tongbo { get; }
        public long Revision { get; }
        public float CurrentTotalBonus { get; }
        public float NextTotalBonus { get; }
        public bool AtMaximum => CurrentLevel == MaximumLevel;
        public bool CanAfford => !AtMaximum && Tongbo >= Cost;

        internal DemoUpgradeQuote(DemoUpgradeTrack track, DemoEconomySnapshot snapshot, DemoEconomyRules rules)
        {
            Track = track; CurrentLevel = snapshot.State.Level(track); MaximumLevel = rules.MaximumLevel(track);
            Cost = CurrentLevel == MaximumLevel ? 0 : rules.CostForNextLevel(track, CurrentLevel);
            Tongbo = snapshot.Tongbo; Revision = snapshot.Revision;
            CurrentTotalBonus = rules.TotalBonus(track, CurrentLevel);
            NextTotalBonus = rules.TotalBonus(track, Math.Min(CurrentLevel + 1, MaximumLevel));
        }
    }

    public sealed class DemoPurchaseReceipt
    {
        public DemoPurchaseStatus Status { get; }
        public string Error { get; }
        public DemoEconomySnapshot Committed { get; }
        public int Charged { get; }
        public DemoUpgradeTrack Track { get; }
        public string RequestId { get; }

        internal DemoPurchaseReceipt(DemoPurchaseStatus status, DemoUpgradeTrack track, string requestId,
            string error = null, DemoEconomySnapshot committed = null, int charged = 0)
        { Status = status; Track = track; RequestId = requestId; Error = error; Committed = committed; Charged = charged; }
    }

    /// <summary>Single transaction owner for all shops in a session. Quotes never reserve or spend currency.</summary>
    public sealed class DemoEconomyService
    {
        readonly IDemoEconomyStore store;
        readonly DemoEconomyRules rules;
        int inFlight;
        public DemoEconomyRules Rules => rules;

        public DemoEconomyService(DemoEconomyRules rules, IDemoEconomyStore store)
        { this.rules = rules ?? throw new ArgumentNullException(nameof(rules)); this.store = store ?? throw new ArgumentNullException(nameof(store)); }

        public bool TryQuote(DemoUpgradeTrack track, out DemoUpgradeQuote quote, out string error)
        {
            quote = default; error = null;
            if (!DemoEconomyRules.IsTrack(track)) { error = "알 수 없는 강화 항목입니다."; return false; }
            DemoEconomySnapshot snapshot;
            try { snapshot = store.Read(); }
            catch (Exception e) { error = "진행을 읽지 못했다: " + e.Message; return false; }
            if (snapshot == null || !snapshot.IsValid(rules)) { error = "강화 진행 데이터가 유효하지 않다."; return false; }
            quote = new DemoUpgradeQuote(track, snapshot, rules); return true;
        }

        /// <param name="expectedCurrentLevel">The displayed quote level, retained by the button until this request completes.</param>
        /// <param name="requestId">One stable ID per user purchase intent. Retries reuse it, fresh purchases get a new ID.</param>
        public bool TryPurchase(DemoUpgradeTrack track, int expectedCurrentLevel, string requestId, out DemoPurchaseReceipt receipt)
        {
            if (Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
            { receipt = new DemoPurchaseReceipt(DemoPurchaseStatus.Busy, track, requestId, "이전 거래를 처리하는 중이다."); return false; }
            try
            {
                if (!DemoEconomyRules.IsTrack(track) || expectedCurrentLevel < 0 || string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128)
                { receipt = new DemoPurchaseReceipt(DemoPurchaseStatus.InvalidRequest, track, requestId, "유효하지 않은 거래 요청이다."); return false; }
                DemoEconomySnapshot before;
                try { before = store.Read(); }
                catch (Exception e)
                { receipt = new DemoPurchaseReceipt(DemoPurchaseStatus.InvalidState, track, requestId, "진행을 읽지 못했다: " + e.Message); return false; }
                if (before == null || !before.IsValid(rules))
                { receipt = new DemoPurchaseReceipt(DemoPurchaseStatus.InvalidState, track, requestId, "강화 진행 데이터가 유효하지 않다."); return false; }
                DemoEconomyState candidate = before.State;
                if (candidate.purchaseIds.Contains(requestId))
                { receipt = new DemoPurchaseReceipt(DemoPurchaseStatus.DuplicateRequest, track, requestId, "이미 처리된 거래다."); return false; }
                int level = candidate.Level(track);
                if (level != expectedCurrentLevel)
                { receipt = new DemoPurchaseReceipt(DemoPurchaseStatus.StaleQuote, track, requestId, "강화 단계가 변경되었다. 표시를 갱신한다."); return false; }
                if (level >= rules.MaximumLevel(track))
                { receipt = new DemoPurchaseReceipt(DemoPurchaseStatus.MaximumLevel, track, requestId, "최대 강화 단계다."); return false; }
                int cost = rules.CostForNextLevel(track, level);
                if (before.Tongbo < cost)
                { receipt = new DemoPurchaseReceipt(DemoPurchaseStatus.InsufficientTongbo, track, requestId, "통보가 부족하다."); return false; }
                if (candidate.revision == long.MaxValue || candidate.purchaseIds.Count >= 64)
                { receipt = new DemoPurchaseReceipt(DemoPurchaseStatus.InvalidState, track, requestId, "강화 거래 기록을 갱신할 수 없다."); return false; }
                candidate.levels[(int)track] = level + 1;
                candidate.revision++;
                candidate.purchaseIds.Add(requestId);
                var next = new DemoEconomySnapshot(before.Tongbo - cost, candidate);
                DemoEconomyCommitStatus committed;
                string error;
                try { committed = store.TryCommit(before, next, out error); }
                catch (Exception e) { committed = DemoEconomyCommitStatus.Failed; error = e.Message; }
                if (committed != DemoEconomyCommitStatus.Saved)
                {
                    receipt = new DemoPurchaseReceipt(committed == DemoEconomyCommitStatus.Conflict ? DemoPurchaseStatus.Conflict : DemoPurchaseStatus.SaveFailed,
                        track, requestId, string.IsNullOrEmpty(error) ? "저장하지 못해 거래가 적용되지 않았다." : error);
                    return false;
                }
                receipt = new DemoPurchaseReceipt(DemoPurchaseStatus.Purchased, track, requestId, committed: next, charged: cost);
                return true;
            }
            finally { Volatile.Write(ref inFlight, 0); }
        }

        public RuntimePlayerStats GetStats()
        {
            var snapshot = store.Read();
            if (snapshot == null || !snapshot.IsValid(rules)) throw new InvalidOperationException("Invalid demo economy state.");
            return new RuntimePlayerStats(snapshot.State, rules);
        }
    }
}
