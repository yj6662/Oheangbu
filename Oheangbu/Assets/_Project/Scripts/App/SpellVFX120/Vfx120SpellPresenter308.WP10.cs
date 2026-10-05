namespace Oheangbu.App.SpellVFX120
{
    // #308 WP-10 presentation translation (interim wiring of the catalogue prefabs, not an art verdict): the detonation of an
    // installed mark. The ember charge has its own detonation clock; every other mark body gets the generic hit signal.
    public sealed partial class Vfx120SpellPresenter308
    {
        partial void CueWP10(Vfx120Effect fx, in SpellFxRequest request, SpellFxCue cue, in SpellFxCueArgs args, ref bool handled)
        {
            if (request.Role != SpellFxRole.Mark || cue != SpellFxCue.Detonate) return;
            if (!fx.DetonateEmberCharge()) fx.Signal(Vfx120Effect.Cue.Hit, args.Target);
            handled = true;
        }
    }
}
