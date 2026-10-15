using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // SPEC-SPELL-LAB-308 §3: the spell test scene has no campaign session, so CombatLifetimeScope would lock every glyph behind
    // a final consonant. This stand-in opens them in the editor and development builds only; a release build stays locked (the
    // same answer as LockedSpellUnlocks308). A scene with a session never reads it - the session always wins.
    public sealed class SpellLabUnlocks308 : MonoBehaviour, ISpellUnlocks
    {
        public bool FinalUnlocked(SpellUnlockRule rule) => Debug.isDebugBuild;
    }
}
