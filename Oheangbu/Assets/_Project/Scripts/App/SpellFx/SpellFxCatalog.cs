using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153: which prefab plays for (letter, variant). One entry = { letter, variant, prefab }; the prefab root carries a <see cref="SpellFxPlayer"/>.
    /// The asset lives at a fixed path (<see cref="AssetPath"/>); while it does not exist <see cref="Load"/> answers null and the director does nothing (every old look stays).
    /// A letter is "covered" when the variant currently chosen for it (<see cref="SpellFxSwitch"/>) has a playable prefab here.</summary>
    public sealed class SpellFxCatalog : ScriptableObject
    {
        public const string AssetPath = "Assets/_Project/Art/SpellVFX308/Restart/SpellFxCatalog.asset";
        /// <summary>A player build finds the same asset under Resources/SpellFxCatalog (none today).</summary>
        public const string ResourcePath = "SpellFxCatalog";

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("The glyph, exactly one character.")] public string letter = "";
            [Tooltip("A, B, C ... : looks of the same letter that can be swapped in the same scene.")] public string variant = SpellFxSwitch.DefaultVariant;
            public GameObject prefab;
        }

        public Entry[] entries = new Entry[0];

        /// <summary>A fixture / tool puts its own catalogue here (no asset needed); <see cref="Load"/> answers it first. Reset when Play starts.</summary>
        public static SpellFxCatalog Override;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Override = null; }

        /// <summary>The catalogue, or null when none exists yet (tolerated).</summary>
        public static SpellFxCatalog Load()
        {
            if (Override != null) return Override;
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<SpellFxCatalog>(AssetPath);
#else
            return Resources.Load<SpellFxCatalog>(ResourcePath);
#endif
        }

        /// <summary>The prefab for (letter, variant) when it has a <see cref="SpellFxPlayer"/> on its root; null otherwise.</summary>
        public GameObject Find(string letter, string variant)
        {
            if (entries == null || string.IsNullOrEmpty(letter)) return null;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e == null || e.prefab == null) continue;
                if (!string.Equals(e.letter, letter, StringComparison.Ordinal) || !string.Equals(e.variant, variant, StringComparison.Ordinal)) continue;
                return e.prefab.GetComponent<SpellFxPlayer>() != null ? e.prefab : null;
            }
            return null;
        }

        public GameObject Find(char letter, string variant) => Find(letter.ToString(), variant);

        /// <summary>Is the variant chosen for <paramref name="letter"/> right now playable here?</summary>
        public bool Covers(char letter) => Find(letter, SpellFxSwitch.Get(letter)) != null;

        /// <summary>The variants listed for a letter (in entry order, duplicates skipped).</summary>
        public List<string> VariantsOf(string letter)
        {
            var l = new List<string>();
            if (entries != null) foreach (var e in entries) if (e != null && e.letter == letter && !l.Contains(e.variant)) l.Add(e.variant);
            return l;
        }

        /// <summary>true = no duplicate (letter, variant), a one-character letter, a prefab with a SpellFxPlayer in every entry.</summary>
        public bool Validate(out string problems)
        {
            var sb = new StringBuilder();
            var seen = new HashSet<string>();
            if (entries != null)
                for (int i = 0; i < entries.Length; i++)
                {
                    var e = entries[i];
                    if (e == null) { sb.Append("entry ").Append(i).Append(" is null; "); continue; }
                    string key = e.letter + "/" + e.variant;
                    if (string.IsNullOrEmpty(e.letter) || e.letter.Length != 1) sb.Append("entry ").Append(i).Append(" letter '").Append(e.letter).Append("' is not one character; ");
                    if (string.IsNullOrEmpty(e.variant)) sb.Append("entry ").Append(i).Append(" has no variant; ");
                    if (!seen.Add(key)) sb.Append("duplicate ").Append(key).Append("; ");
                    if (e.prefab == null) sb.Append(key).Append(" has no prefab; ");
                    else if (e.prefab.GetComponent<SpellFxPlayer>() == null) sb.Append(key).Append(" prefab '").Append(e.prefab.name).Append("' has no SpellFxPlayer on its root; ");
                }
            problems = sb.ToString();
            return problems.Length == 0;
        }
    }

    /// <summary>The variant currently chosen per letter (default "A"). Static on purpose: a tool changes it between two casts of the same scene. Domain reload is off, so
    /// it is cleared when the runtime starts.</summary>
    public static class SpellFxSwitch
    {
        public const string DefaultVariant = "A";
        static readonly Dictionary<string, string> Chosen = new Dictionary<string, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Chosen.Clear(); }

        public static string Get(string letter) => letter != null && Chosen.TryGetValue(letter, out var v) ? v : DefaultVariant;
        public static string Get(char letter) => Get(letter.ToString());

        /// <summary>Chooses the variant for a letter; null / empty puts it back to the default.</summary>
        public static void Set(string letter, string variant)
        {
            if (string.IsNullOrEmpty(letter)) return;
            if (string.IsNullOrEmpty(variant) || variant == DefaultVariant) Chosen.Remove(letter); else Chosen[letter] = variant;
        }
        public static void Set(char letter, string variant) => Set(letter.ToString(), variant);

        public static void ResetAll() { Chosen.Clear(); }
    }
}
