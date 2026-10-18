using System;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.SpellVFX120;
using UnityEditor;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.56: the derived shots of the second-strike letters. Per letter a smaller copy of the
    // element's single bolt (ShotPair, _308l_derived) goes into the letter's attack copy (_308b) as Derived308Prefab; the
    // presenter flies it from the split point to each derived target. connect:attack / revert point at the _308b copies, so
    // there is nothing more to record. Group 2 (망 목 논 옥) has no Fx handle and is drawn by the confirmed-hit ink.
    //   derived        - build the 4
    public static partial class Vfx308Build
    {
        const float DerivedScale = .55f;   // TEST: under the parent body (1.0 for the singles)

        static readonly (string Glyph, string Id, string Key)[] DerivedLetters =
        {
            ("간", "003_AC04", "ga"), ("낫", "029_B0AB", "na"), ("속", "086_C18D", "sa"), ("안", "099_C548", "a"),
        };

        static string BuildDerived()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Vfx308Build derived: Edit mode only");
            V2 = true; Touched.Clear();
            var sb = new StringBuilder();
            foreach (var d in DerivedLetters)
            {
                var a = Attack.First(x => x.Effect == d.Id);
                var spell = Spells.First(x => x.Key == d.Key);
                if (!File.Exists(AttackProfilePath(a))) { sb.AppendLine(d.Glyph + ": the attack copy is not built (run build first)"); continue; }
                var shot = ShotPair(d.Id, spell, "_308l_derived", DerivedScale);
                if (shot == null) { sb.AppendLine(d.Glyph + ": the element bolt is not built (run build first)"); continue; }
                var profile = Load<Vfx120Profile>(AttackProfilePath(a));
                profile.Derived308Prefab = shot;
                EditorUtility.SetDirty(profile); Touched.Add(profile);
                sb.AppendLine(d.Glyph + " -> " + AttackProfilePath(a) + " derived " + shot.name + " (scale " + (spell.Scale * DerivedScale).ToString("0.00") + ")");
            }
            foreach (var o in Touched) AssetDatabase.SaveAssetIfDirty(o);
            return sb.ToString();
        }
    }
}
