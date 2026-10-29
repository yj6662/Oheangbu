using System;
using System.IO;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Data;
using Oheangbu.Vol2.Import;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Oheangbu.App
{
    /// <summary>S5-b slice 1: turns the two imported ScriptableObjects (NumbersV3SO, Vol2TableSO) into a <see cref="Vol2Rulebook"/>
    /// through <see cref="Vol2DataLoader"/>, which rebuilds the DTOs and requires the canonical hash to equal the frozen manifest.
    /// Anything refused returns an UNAVAILABLE rulebook carrying the reason: the Vol2 route then refuses every letter (no cast, no
    /// legacy fallback).
    /// KNOWN LIMIT (slice 10 / E9): the trusted manifest is read from Tools/SpellVFX120/Pure308/Vol2V3/Golden (the independent Python
    /// computation), a path that exists in the editor and on the dev machine only. A player build has no trusted manifest and the
    /// route would be unavailable there; the main scene is Legacy308 and never reaches this code.</summary>
    public static class Vol2RulebookLoader
    {
        public const string NumbersAssetPath = "Assets/_Project/Data/Spells/Vol2/NumbersV3.asset";
        public const string TableAssetPath = "Assets/_Project/Data/Spells/Vol2/Vol2Table_v11.asset";
        public const string TrustedManifestRelative = "../../Tools/SpellVFX120/Pure308/Vol2V3/Golden/import_manifest_v11.json";

        public static string TrustedManifestPath => Path.GetFullPath(Path.Combine(Application.dataPath, TrustedManifestRelative));

        /// <param name="numbers">serialized reference of the scope; null falls back to the asset path in the editor only</param>
        public static Vol2Rulebook Load(NumbersV3SO numbers, Vol2TableSO table)
        {
#if UNITY_EDITOR
            if (numbers == null) numbers = AssetDatabase.LoadAssetAtPath<NumbersV3SO>(NumbersAssetPath);
            if (table == null) table = AssetDatabase.LoadAssetAtPath<Vol2TableSO>(TableAssetPath);
#endif
            if (numbers == null || table == null) return Vol2Rulebook.Unavailable("NumbersV3SO / Vol2TableSO not available (run vol2:import, or assign them on the scope)");
            string path = TrustedManifestPath;
            if (!File.Exists(path)) return Vol2Rulebook.Unavailable("trusted manifest not available: " + path);
            try
            {
                var trusted = Vol2Golden.ReadTrustedManifest(File.ReadAllText(path));
                var outcome = Vol2DataLoader.Load(numbers, table, trusted);
                if (!outcome.Ok) return Vol2Rulebook.Unavailable("loader refused: " + outcome.Error);
                return new Vol2Rulebook(outcome.Numbers, outcome.Spells, table.manifest != null ? table.manifest.dtoHash : "", numbers.sourceSha256, table.sourceSha256);
            }
            catch (Exception e) { return Vol2Rulebook.Unavailable("loader exception: " + e.GetType().Name + " " + e.Message); }
        }
    }
}
