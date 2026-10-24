using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Combat
{
    // S5-b slice 2: the seam between the old ParryJudge and the vol.2 parry rules (Vol2ParryService implements it). Combat-side types only.

    /// <summary>What the vol.2 rules decided about one impact: Success = the hit is fully defended (the caller voids ALL its damage);
    /// None = the hit lands. Guard is the element of the parry that took it (for the contact burst).</summary>
    public readonly struct Vol2ImpactVerdict
    {
        public readonly ParryOutcome Outcome;
        public readonly Element Guard;
        public Vol2ImpactVerdict(ParryOutcome outcome, Element guard) { Outcome = outcome; Guard = guard; }
        public static Vol2ImpactVerdict Unblocked => new Vol2ImpactVerdict(ParryOutcome.None, Element.Wood);
    }

    public interface IVol2ImpactResolver
    {
        /// <summary>Called once per impact, before the caller applies damage. now = Time.time (scaled); the resolver reads its own µs clock.</summary>
        Vol2ImpactVerdict Resolve(float now, Vector3 impactPoint, AttackProvenance attack);
    }
}
