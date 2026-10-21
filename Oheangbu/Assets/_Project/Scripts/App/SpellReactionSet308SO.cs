using UnityEngine;

namespace Oheangbu.App
{
    // #308 D308-132: the reaction table as the game reads it. A copy of Data/Spells/Reactions308.csv that the editor command
    // spell308-reactions writes (SpellTable308); the sheet stays the one place to edit. No copy in Resources = no reaction:
    // the table is switched on by baking it, and off by removing the copy.
    public sealed class SpellReactionSet308SO : ScriptableObject
    {
        public const string ResourcesPath = "Spell308/SpellReactionSet308";
        [TextArea(4, 24)] public string Csv = "";
    }
}
