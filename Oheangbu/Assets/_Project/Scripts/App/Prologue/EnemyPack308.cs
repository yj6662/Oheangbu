using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.Prologue
{
    // #308 D308-70 (SPEC-ENEMY-BRAIN-308, TEST): the enemies hunting one player, kept on that player's object. The first brain that
    // needs it adds it at run time (precedent: Riposte301 on the walker). Not a global: one per target, reached through
    // PrologueEncounter.Player, no statics. It hands out attack turns (how many of a kind may be in an attack at once, and how soon
    // after one another) and carries a call from an actor that noticed the player to the actors around it. It never moves, damages
    // or commands anybody; each actor decides for itself what a turn or a call means.
    [DisallowMultipleComponent]
    public sealed class EnemyPack308 : MonoBehaviour
    {
        readonly List<PrologueEncounter> members = new List<PrologueEncounter>();
        readonly List<PrologueEncounter> melee = new List<PrologueEncounter>(), ranged = new List<PrologueEncounter>();
        float lastGrant = float.NegativeInfinity;

        public int Members => members.Count;
        public int MeleeTurns => melee.Count;
        public int RangedTurns => ranged.Count;
        public int Grants { get; private set; }
        public int Calls { get; private set; }
        // the most turns of one kind ever held at the same time (the Play check compares it with the profile's cap)
        public int PeakMeleeTurns { get; private set; }
        public int PeakRangedTurns { get; private set; }

        public void Join(PrologueEncounter actor) { if (actor != null && !members.Contains(actor)) members.Add(actor); }
        public void Leave(PrologueEncounter actor) { members.Remove(actor); Release(actor); }
        public bool Holds(PrologueEncounter actor) => melee.Contains(actor) || ranged.Contains(actor);
        public void Release(PrologueEncounter actor) { melee.Remove(actor); ranged.Remove(actor); }

        /// <summary>max 0 = no limit and no gap (everybody attacks when ready, as before).</summary>
        public bool TryClaim(PrologueEncounter actor, bool isRanged, int max, float gap, float now)
        {
            if (actor == null) return false;
            if (Holds(actor)) return true;
            Prune(melee); Prune(ranged);
            var turns = isRanged ? ranged : melee;
            if (max > 0 && (turns.Count >= max || now - lastGrant < gap)) return false;
            turns.Add(actor); lastGrant = now; Grants++;
            PeakMeleeTurns = Mathf.Max(PeakMeleeTurns, melee.Count); PeakRangedTurns = Mathf.Max(PeakRangedTurns, ranged.Count);
            return true;
        }

        public void Call(PrologueEncounter caller, Vector3 known, float radius, float at)
        {
            if (caller == null) return;
            Calls++;
            Vector3 from = caller.transform.position; float limit = radius * radius;
            for (int i = members.Count - 1; i >= 0; i--)
            {
                var m = members[i];
                if (m == null) { members.RemoveAt(i); continue; }
                if (m != caller && m.isActiveAndEnabled && (m.transform.position - from).sqrMagnitude <= limit) m.HearCall308(known, at);
            }
        }

        // a holder that died, was switched off or went home without saying so gives its turn back
        static void Prune(List<PrologueEncounter> turns)
        {
            for (int i = turns.Count - 1; i >= 0; i--)
                if (turns[i] == null || !turns[i].isActiveAndEnabled || turns[i].Current != PrologueEncounter.Behaviour.Chase) turns.RemoveAt(i);
        }
    }
}
