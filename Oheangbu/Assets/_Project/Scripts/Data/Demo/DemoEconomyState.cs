using System;
using System.Collections.Generic;

namespace Oheangbu.Data.Demo
{
    public enum DemoUpgradeTrack { Wood, Fire, Earth, Metal, Water, Health, Ink }

    /// <summary>Campaign-owned durable purchases; Tongbo remains in the campaign ledger.</summary>
    [Serializable]
    public sealed class DemoEconomyState
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public long revision;
        public int[] levels = new int[7];
        public List<string> purchaseIds = new List<string>();

        public DemoEconomyState Copy()
        {
            return new DemoEconomyState
            {
                version = version,
                revision = revision,
                levels = levels == null ? null : (int[])levels.Clone(),
                purchaseIds = purchaseIds == null ? null : new List<string>(purchaseIds)
            };
        }

        public int Level(DemoUpgradeTrack track)
        {
            int index = (int)track;
            if (index < 0 || index >= 7 || levels == null || levels.Length != 7)
                throw new ArgumentOutOfRangeException(nameof(track));
            return levels[index];
        }

        public bool IsValid(DemoEconomyRules rules)
        {
            if (rules == null || version != CurrentVersion || revision < 0 ||
                levels == null || levels.Length != 7 || purchaseIds == null || purchaseIds.Count > 64)
                return false;
            for (int i = 0; i < levels.Length; i++)
                if (levels[i] < 0 || levels[i] > rules.MaximumLevel((DemoUpgradeTrack)i)) return false;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in purchaseIds)
                if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || !unique.Add(id)) return false;
            return true;
        }

        public bool SameAs(DemoEconomyState other)
        {
            if (other == null || version != other.version || revision != other.revision ||
                levels == null || other.levels == null || levels.Length != other.levels.Length ||
                purchaseIds == null || other.purchaseIds == null || purchaseIds.Count != other.purchaseIds.Count)
                return false;
            for (int i = 0; i < levels.Length; i++) if (levels[i] != other.levels[i]) return false;
            for (int i = 0; i < purchaseIds.Count; i++)
                if (!string.Equals(purchaseIds[i], other.purchaseIds[i], StringComparison.Ordinal)) return false;
            return true;
        }
    }

    /// <summary>A detached ledger/economy pair. Never hands mutable campaign state to a transaction.</summary>
    public sealed class DemoEconomySnapshot
    {
        readonly DemoEconomyState state;
        public int Tongbo { get; }
        public long Revision => state == null ? -1 : state.revision;
        public DemoEconomyState State => state == null ? null : state.Copy();

        public DemoEconomySnapshot(int tongbo, DemoEconomyState economy)
        { Tongbo = tongbo; state = economy == null ? null : economy.Copy(); }

        public bool IsValid(DemoEconomyRules rules) => Tongbo >= 0 && state != null && state.IsValid(rules);
        public bool SameAs(DemoEconomySnapshot other) => other != null && Tongbo == other.Tongbo && state != null && state.SameAs(other.state);
    }
}
