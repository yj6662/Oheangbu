using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // SPEC-SPELL-LAB-308 §3: the spell test scene has no campaign session, so CombatLifetimeScope would lock every glyph behind
    // a final consonant. This stand-in opens them in the editor and development builds only; a release build stays locked (the
    // same answer as LockedSpellUnlocks308). A scene with a session never reads it - the session always wins.
    // §3.1: the ㄱ-final runtimes (giyeok attacks, buffs, wards) are wired only by WorldMacroPlaytestSession.Start; the lab drops
    // the session, so the stand-in wires them itself (same debug-build gate) from the profiles the builder copied off the session.
    public sealed class SpellLabUnlocks308 : MonoBehaviour, ISpellUnlocks
    {
        [SerializeField] private CombatLoopWiring _wiring;
        [SerializeField] private EABuffProfileSO _buffs;
        [SerializeField] private EAWardProfileSO _wards;
        [SerializeField] private EAGiyeokProfileSO _giyeok;

        public bool FinalUnlocked(SpellUnlockRule rule) => Debug.isDebugBuild;

        private void Start()
        {
            if (!Debug.isDebugBuild || _wiring == null) return;
            bool Open() => Debug.isDebugBuild && this != null && isActiveAndEnabled;
            if (_buffs != null) _wiring.ConfigureEABuffs(_buffs, Open);
            if (_wards != null) _wiring.ConfigureEAWards(_wards);
            if (_giyeok != null) _wiring.ConfigureEAGiyeok(_giyeok, Open);
        }
    }
}
