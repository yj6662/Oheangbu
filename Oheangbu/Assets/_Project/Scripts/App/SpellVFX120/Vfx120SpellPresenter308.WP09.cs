namespace Oheangbu.App.SpellVFX120
{
    // #308 WP-09 interim translation of the companion-buff cues (SPEC-SPELL-120-308 section 9; catalogue prefabs, not an art
    // verdict). The buff look is begun with the Companion role, follows the player and lives its own authored life (the
    // handler asks for no duration, so nothing glows for as long as the buff). Each companion shot arrives here as
    // Secondary (launched) and Hit (landed); once the look has ended the cues find no live handle and do nothing.
    // Those two cues are consumed: a companion shot is not the buff look's own impact, so the generic hit signal (which
    // starts the prefab's native impact) must not run for them. Where the catalogue prefab carries pellets of its own (the
    // fire companion set, three single-use pellets) the first free pellet is launched and confirmed; on every other prefab
    // the two calls answer false and nothing happens. The landed shot is still visible through the wiring's contact burst.
    public sealed partial class Vfx120SpellPresenter308
    {
        const int WP09PelletSlots = 3;

        partial void CueWP09(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled)
        {
            if (fx == null || request.Role != SpellFxRole.Companion) return;
            if (cue == SpellFxCue.Secondary)
            {
                handled = true;
                for (int slot = 0; slot < WP09PelletSlots; slot++)
                    if (fx.LaunchFirePellet(slot, args.Point)) break;
            }
            else if (cue == SpellFxCue.Hit)
            {
                handled = true;
                for (int slot = 0; slot < WP09PelletSlots; slot++)
                    if (fx.ConfirmFirePelletHit(slot, args.Point)) break;
            }
        }
    }
}
