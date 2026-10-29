using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>A part that only writes down the calls it gets (D308-153): drop it on a prefab to see when the cast begins, launches, hits and ends on the scope clock, and
    /// use it in a fixture as the witness that the director's calls reached the prefab's parts. Draws nothing.</summary>
    public sealed class SpellFxLogPart : MonoBehaviour, ISpellFxPart
    {
        public const int MaxLines = 256;
        [SerializeField] bool echoToConsole;

        public readonly List<string> Lines = new List<string>();
        public int Begins, Launches, Ticks, Hits, Materials, Reactions, Ends;
        public SpellFxEndReason LastEndReason;
        public float LastTickAge;

        void Add(SpellFxCast cast, string what)
        {
            if (Lines.Count >= MaxLines) Lines.RemoveAt(0);
            string line = cast.Id + " age " + cast.Age.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " " + what;
            Lines.Add(line);
            if (echoToConsole) Debug.Log("[SpellFx] " + line);
        }

        public void OnBegin(SpellFxCast cast) { Begins++; Add(cast, "begin"); }
        public void OnLaunch(SpellFxCast cast) { Launches++; Add(cast, "launch"); }
        public void OnTick(SpellFxCast cast, float ageSeconds) { Ticks++; LastTickAge = ageSeconds; }
        public void OnHit(SpellFxCast cast, SpellFxHit hit) { Hits++; Add(cast, "hit " + hit.Kind + " target " + hit.TargetId + " at " + hit.Position.ToString("F2")); }
        public void OnMaterial(SpellFxCast cast, SpellFxMaterial material) { Materials++; Add(cast, "material " + material.Phase + " " + material.Kind + " #" + material.Id); }
        public void OnReaction(SpellFxCast cast, SpellFxReaction reaction) { Reactions++; Add(cast, "reaction " + reaction.Relation + " " + reaction.ActingBody + ">" + reaction.ContactedBody + (reaction.ActingSide ? " (acting)" : " (contacted)")); }
        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason) { Ends++; LastEndReason = reason; Add(cast, "end " + reason); }
    }
}
