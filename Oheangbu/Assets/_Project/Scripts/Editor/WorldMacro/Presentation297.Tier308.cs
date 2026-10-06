using System;
using System.IO;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 먼 불빛 (SPEC-ATTRACTION-LIGHT-308): the #297 still, at a named quality tier. `shot` always raises the editor to PC, so the
    // diagnosis could not take a Mobile still; this entry renders the same rig at "PC" or "Mobile" and puts the editor's quality
    // level back (Close restores the level Open found). Nothing else of Presentation297 changes.
    public static partial class Presentation297
    {
        internal static string ShotTier308(string name, Vector3 eye, Vector3 target, float fov, int width, int height, string tier, bool hidePlayer)
        {
            int level = Array.IndexOf(QualitySettings.names, tier);
            if (level < 0) throw new Exception("no quality level named '" + tier + "' (" + string.Join(", ", QualitySettings.names) + ")");
            string file = Path.Combine(Root, "Stills", name + ".png");
            var rig = Open(width, height, fov, hidePlayer);
            try
            {
                if (QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);
                Render(rig, eye, target, fov, file, 3);
            }
            finally { Close(rig); }
            return file;
        }
    }
}
