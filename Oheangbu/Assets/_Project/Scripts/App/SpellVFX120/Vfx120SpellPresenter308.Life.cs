// PURE308
namespace Oheangbu.App.SpellVFX120
{
    // How long the interim presenter lets a catalogue body live (ART-INK: a spell lights up for a moment; sustained glow
    // belongs to man-made light sources only). The catalogue bodies are shared glowing prefabs, so this presenter never
    // draws persistence with them:
    //  - a body never lives longer than the life it was authored with (a request can only shorten it),
    //  - a request longer than the cue is a lasting effect (a buff, a sword form, a tree, an installed mark, a rock, a
    //    zone): it gets a short cast cue and nothing more, however long its body was authored for. What stays in the world
    //    is the rule state; drawing it is the deploy-layer presenter's job once that replaces this one.
    // These seconds are presentation only: no rule reads them, and no rule sheet carries them.
    public static class SpellCueLife308
    {
        // The longest cue a lasting effect gets from a catalogue body (the catalogue profile's own default life).
        public const float LastingCueSeconds = 3f;

        // requested = SpellFxRequest.Duration (0 = "your own default"), authored = the catalogue profile's Duration.
        public static float Seconds(float requested, float authored)
        {
            if (!(authored > 0f)) authored = LastingCueSeconds;
            if (!(requested > 0f)) return authored;                      // no wish: the authored life
            float wish = requested > LastingCueSeconds ? LastingCueSeconds : requested;   // a lasting effect: a short cue only
            return wish < authored ? wish : authored;                    // never longer than authored
        }

        // 11.54 (D308-110): a #308 lasting body (Aura308Lasting - the tree) draws its own persistence in ink (dark first,
        // faint after, no glow): it lives as long as asked, still never longer than authored.
        public static float Seconds(float requested, float authored, bool lasting)
        {
            if (!lasting) return Seconds(requested, authored);
            if (!(authored > 0f)) authored = LastingCueSeconds;
            if (!(requested > 0f)) return authored;
            return requested < authored ? requested : authored;
        }

        // true = the body will be gone before the effect it stands for is over (its persistence is not drawn here).
        public static bool CueOnly(float requested, float authored) => requested > 0f && Seconds(requested, authored) < requested;
    }
}
