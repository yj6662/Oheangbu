using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Spellcraft;
using VContainer;

namespace Oheangbu.App
{
    // S5-b slice 1 (D308-149, design s5b_design_v1 section 3): the path switch of the spell runtime. This partial owns ONE decision:
    // in Vol2 mode the spell part of OnLetterDrawn is skipped and the letter goes to the route's Vol2GlyphAdapter instead.
    // Only that hook is bypassed: movement, aim, lock-on, HUD, the ink pool, the brush adapter and the counterattack input are not
    // touched. Without ConstructVol2 (every scene and edit-mode check that does not build the combat scope) the route is null and
    // the wiring behaves exactly as before. A Vol2 refusal never falls back to the old resolver.
    public sealed partial class CombatLoopWiring
    {
        private SpellRoute _spellRoute;

        // Third injection point (Construct keeps its four arguments; ConstructSpells is the second).
        [Inject]
        public void ConstructVol2(SpellRoute route) { _spellRoute = route; }

        public SpellRoute ActiveSpellRoute => _spellRoute;
        public SpellRuntimeMode SpellMode => _spellRoute != null ? _spellRoute.Mode : SpellRuntimeMode.Legacy308;

        // The recognition-result channel the drawing input raises on; the spell lab runner injects the 120 letters through it.
        public DrawnLetterEventChannelSO LetterChannel => _letterDrawn;

        // true = the letter was taken by the vol.2 route (accepted or refused) and the old handling must not run.
        private bool TryRouteVol2(DrawnLetter letter)
        {
            if (_spellRoute == null || !_spellRoute.IsVol2) return false;
            EnsureVol2Defence();   // slice 2: the payment gate must stand before the first letter (CombatLoopWiring.Vol2Defence.cs)
            var decision = _spellRoute.RouteVol2(letter);
            // a refusal (not a letter, locked, no data, not paid for) looks like the old misfire to the brush; the ink of an accepted cast is
            // paid inside the adapter by the defence kit's payment gate, before CastAccepted is raised
            if (!decision.Accepted) _brushAdapter?.NotifyCastFailed();
            // D308-150 V9 step 5a: an accepted vol.2 cast's glyph body is the vol.2 view's (Vol2LegacyStage plays the old body on the executor's clock). The stroke
            // adapter must not spawn its own copy at the commit (SpawnPattern, aimed at nothing): the same "presentation owned elsewhere" notice the field casts use
            // stops only that body - the drawn letter still unravels into the brush and fades as the old game's commit does (BeginUnravel308 / BeginFading).
            else _brushAdapter?.NotifyFieldPresentationOwned();
            return true;
        }
    }
}
