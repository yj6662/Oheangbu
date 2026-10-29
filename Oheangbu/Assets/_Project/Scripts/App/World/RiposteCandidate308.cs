// PURE308
namespace Oheangbu.App.World
{
    // D308-147 (debate 32, plan C): the riposte side of one Q gesture, no engine objects. Riposte301 keeps one of these and feeds it
    // the facts it reads from the world (the key of the target's open window, whether it may act); the decisions live here so they run
    // outside Unity (Pure308 Cases_Riposte308). The candidate fixes a window when the key goes down and consumes nothing; the first
    // stroke gives the window to the drawing; a release without a stroke is checked again against the world as it is then.
    public sealed class RiposteCandidate308
    {
        /// <summary>The windows already answered (a drawing or a volley), per enemy.</summary>
        public RiposteWindowClaim308 Claims { get; } = new RiposteWindowClaim308();
        /// <summary>A held key is a candidate that has not drawn.</summary>
        public bool Active { get; private set; }
        /// <summary>The window the candidate was fixed on.</summary>
        public RiposteWindowKey308 Key { get; private set; }

        /// <summary>The key went down. <paramref name="eligible"/> = the world allows a volley now (ready, locked target in reach, able to
        /// act). A candidate needs a real window nobody has answered. Returns whether this press is a candidate.</summary>
        public bool Begin(RiposteWindowKey308 current, bool eligible)
        {
            Clear();
            if (!eligible || !Claims.CanOfferRiposte(current)) return false;
            Active = true; Key = current;
            return true;
        }

        /// <summary>The first stroke: the gesture is a drawing and the window's riposte is gone for good - whatever happens to the drawing.</summary>
        public void StrokeChosen()
        {
            if (!Active) return;
            Claims.TryChooseDraw(Key);
            Clear();
        }

        /// <summary>The gesture ended with no choice (a menu, a shut gate, a disable): nothing fires later.</summary>
        public void Cancel() { Clear(); }

        /// <summary>The key came up with no stroke. true = the volley may start: the candidate is still the same window (same enemy, life and
        /// opening, still open) and the world allows it (<paramref name="eligible"/>: the same lock target, ready, in reach, able to act).
        /// The candidate is spent either way; the window itself is claimed by <see cref="TryClaim"/>.</summary>
        public bool ReleaseWithoutStroke(RiposteWindowKey308 current, bool eligible)
        {
            if (!Active) return false;
            var mine = Key;
            Clear();
            return eligible && current.Equals(mine) && Claims.CanOfferRiposte(current);
        }

        /// <summary>The volley takes the window. Once per window: false for a window a drawing or an earlier volley answered.</summary>
        public bool TryClaim(RiposteWindowKey308 key) => Claims.TryChooseRiposte(key);

        private void Clear() { Active = false; Key = default; }
    }
}
