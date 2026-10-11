// PURE308
using System.Collections.Generic;

namespace Oheangbu.Spellcraft
{
    // #308b final-consonant grants (SPEC-SPELL-120-308 "받침 지급 #308b", D308-51 · D308-53) [TEST].
    // The main game hands out the finals in their LOCKED order, one per realm, each for defeating that realm's boss (the
    // grammar of the first final). Which boss grants which final is data (WorldMacroPlaytestSO.FinalGrants, in earning
    // order); this rule only walks that chain. Entry i is owned once the saved ledger holds ledgerIds[i]. An entry that is
    // not owned is granted when its proof (proofIds[i]: the boss's durable defeat record) is among the saved proofs AND
    // every entry before it is owned or granted in the same walk. An empty proof id = nothing in this table grants it
    // (granted elsewhere, or its boss does not exist yet): the chain waits there. A boss defeated out of order grants
    // nothing by itself; its final arrives when the chain catches up. No glyph, final or boss is named here, and nothing
    // here writes.
    public static class FinalGrantRule308
    {
        // How many entries are due now. granted (optional) receives their indexes in chain order.
        public static int Pending(IReadOnlyList<string> ledgerIds, IReadOnlyList<string> proofIds, ICollection<string> ledger, ICollection<string> proofs,
            List<int> granted = null)
        {
            if (ledgerIds == null || proofIds == null || ledger == null || proofs == null) return 0;
            int count = 0;
            for (int i = 0; i < ledgerIds.Count && i < proofIds.Count; i++)
            {
                if (string.IsNullOrEmpty(ledgerIds[i])) break;   // a broken entry proves no order: the chain ends
                if (ledger.Contains(ledgerIds[i])) continue;
                if (string.IsNullOrEmpty(proofIds[i]) || !proofs.Contains(proofIds[i])) break;
                count++; granted?.Add(i);   // counts as owned for the entries after it
            }
            return count;
        }
    }
}
