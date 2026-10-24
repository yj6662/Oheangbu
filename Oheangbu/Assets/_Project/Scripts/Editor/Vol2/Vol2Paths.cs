using System.IO;
using UnityEngine;
namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-a (D308-149) locations. The two CSVs are byte copies of the research sources (the live 오행부_작도어휘_v0_1.csv,
    /// Unlock308.csv, Rules308_*.csv and Reactions308.csv are never touched). The golden fixtures stay where the pure tests read them.</summary>
    static class Vol2Paths
    {
        public const string DataFolder = "Assets/_Project/Data/Spells/Vol2";
        public const string NumbersCsv = DataFolder + "/vol2_numbers_v3.csv";
        public const string TableCsv = DataFolder + "/vol2_table120_v11.csv";
        public const string NumbersAsset = DataFolder + "/NumbersV3.asset";
        public const string TableAsset = DataFolder + "/Vol2Table_v11.asset";
        public const string TableVersion = "v11";
        public static string Full(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        public static string GoldenDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Tools/SpellVFX120/Pure308/Vol2V3/Golden"));
        public static string Golden(string file) => Path.Combine(GoldenDir, file);
        public static string DocsOriginal(string relative) => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Docs/Research", relative));
    }
}
