// PURE308
namespace Oheangbu.App
{
    // #308 D308-123: the attack ids one dispatch handed out, (From, To]. The host opens one when a cast is dispatched and
    // closes it when the dispatch returns (AttackProvenance.Issued before and after). While it is open every later id is in
    // it: the legacy attacks schedule their hits after their accept. What the cast scheduled is inside (its shots, an area's
    // hits, a zone it laid, a first stage); what a hit hands out later lies above it (a second stage, a mark's detonation).
    public sealed class SpellAttackWindow308
    {
        public long From { get; }
        public long To { get; private set; } = long.MaxValue;

        public SpellAttackWindow308(long from) { From = from; }

        public bool Open => To == long.MaxValue;
        // closed with nothing handed out: an air cast, a cast with no target in reach
        public bool Empty => !Open && To <= From;

        public void Close(long to) { if (Open) To = to < From ? From : to; }

        public bool Holds(long attackId) => attackId > From && attackId <= To;
    }
}
