using Oheangbu.Vol2.Import;
using UnityEngine;
namespace Oheangbu.Vol2.Data
{
    /// <summary>S5-a (D308-149): the E0 numbers (414 rows, all TEST) as imported from vol2_numbers_v3.csv. Written only by the editor
    /// command vol2:import; read only through <see cref="Vol2DataLoader"/>, which rebuilds the DTOs and compares their hash with the frozen
    /// manifest. Values are invariant decimal strings (Unity cannot serialize decimal). Not wired into gameplay.</summary>
    public sealed class NumbersV3SO : ScriptableObject
    {
        public string sourceFile;      // file name of the CSV copy under Data/Spells/Vol2
        public string sourceSha256;    // SHA-256 of the CSV bytes at import time
        public NumberRowData[] rows;
    }
}
