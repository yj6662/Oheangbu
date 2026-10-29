using Oheangbu.Vol2.Import;
using Oheangbu.Vol2.Rules;
namespace Oheangbu.Vol2.Data
{
    /// <summary>The only way to get validated vol.2 numbers and spells out of the assets. Rebuilds the DTOs from the SO data and requires
    /// the canonical DTO hash and both source hashes to equal the trusted manifest (the frozen Golden/import_manifest_v11.json). A refused
    /// load returns the reason; nothing may cast on a refused load. Stateless: no singleton, no cache.</summary>
    public static class Vol2DataLoader
    {
        public static LoadOutcome Load(NumbersV3SO numbers, Vol2TableSO table, Vol2ImportManifestDto trusted)
        {
            if (numbers == null || table == null || trusted == null) return new LoadOutcome(null, null, "NumbersV3SO, Vol2TableSO and a trusted manifest are required");
            return Vol2Snapshot.Load(numbers.rows, numbers.sourceSha256, table.spells, table.sourceSha256, table.manifest, trusted);
        }
    }
}
