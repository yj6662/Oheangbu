using System;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 D308-132 step 6: which prefab draws which reaction kind. Resources/Spell308/SpellReactionLook308 is written by
    // `Vfx308Build reaction` (the prefabs stay in Art/SpellVFX308/V2/Prefabs). A kind with an entry but no prefab is RECOGNISED and
    // simply not drawn yet (진창 / Mud: the presenter says so once); a kind with no entry at all is unknown. No asset in Resources
    // = nothing is drawn (the rules do not care). KeepHeight: the look's vertical size is in metres, so the presenter scales it
    // by (radius, 1, radius) and not by the radius all round (the steam must stay under 1.3 m at any radius).
    public sealed class SpellReactionLook308SO : ScriptableObject
    {
        public const string ResourcesPath = "Spell308/SpellReactionLook308";

        [Serializable]
        public struct Entry
        {
            public ReactionKind308 Kind;
            public GameObject Prefab;
            public bool KeepHeight;
        }

        public Entry[] Entries = Array.Empty<Entry>();

        public bool Knows(ReactionKind308 kind)
        {
            for (int i = 0; Entries != null && i < Entries.Length; i++) if (Entries[i].Kind == kind) return kind != ReactionKind308.None;
            return false;
        }

        public bool TryGet(ReactionKind308 kind, out Entry entry)
        {
            for (int i = 0; Entries != null && i < Entries.Length; i++)
                if (Entries[i].Kind == kind && Entries[i].Prefab != null) { entry = Entries[i]; return true; }
            entry = default;
            return false;
        }
    }
}
