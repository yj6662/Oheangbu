using Oheangbu.Vol2.Import;
using UnityEngine;
namespace Oheangbu.Vol2.Data
{
    /// <summary>S5-a (D308-149): the 120-row integrated vol.2 table (v11) with the import manifest that pins the numbers and table
    /// source bytes and the canonical DTO hash. Written only by vol2:import. Row text is kept verbatim; the rules never read it (they are
    /// built from the numbers), so a text edit is only ever a new source hash. Not wired into gameplay.</summary>
    public sealed class Vol2TableSO : ScriptableObject
    {
        public string tableVersion;    // "v11"
        public string sourceFile;
        public string sourceSha256;
        public SpellRowData[] spells;
        public ManifestData manifest;  // numbers + table source hashes, schema/importer version, DTO hash
    }
}
