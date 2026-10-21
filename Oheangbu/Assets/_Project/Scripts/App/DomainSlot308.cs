namespace Oheangbu.App
{
    // #308 WP-17 (D308-121): a player holds one standing domain at a time, whatever its glyph. One instance per combat scope
    // (registered by SpellEffectInstaller308.WP17, handed to every domain handler through its constructor; never a static).
    // A handler that places a domain takes the slot; the handler that held it before is told to yield (it drops its domain
    // and ends its presentation). A handler made with new (checks) gets a slot of its own.
    public interface IDomainHolder308
    {
        string Id { get; }
        // Another domain took the slot: drop every domain of this handler now.
        void Yield(ISpellPresenter fx);
    }

    public sealed class DomainSlot308
    {
        public IDomainHolder308 Holder { get; private set; }

        // `next` now holds the player's domain. The previous holder (another handler) yields first.
        public void Take(IDomainHolder308 next, ISpellPresenter fx)
        {
            var previous = Holder;
            Holder = next;
            if (previous != null && previous != next) previous.Yield(fx);
        }

        // The holder's domain ended on its own (ran out, was cleared): the slot is free.
        public void Release(IDomainHolder308 holder)
        {
            if (Holder == holder) Holder = null;
        }
    }
}
