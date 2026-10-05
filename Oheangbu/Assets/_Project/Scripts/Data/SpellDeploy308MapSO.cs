using System;
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Data.Spell
{
    // SPEC-SPELL-DEPLOY-308 section 6: the 120-row deploy map. The rows are produced from the glyph CSV by
    // Tools/Art/deploy308_map.py and imported by the editor command "deploy308-map"; they are never typed by hand.
    // Category is READ from the CSV column, not inferred. Presentation only.
    [CreateAssetMenu(menuName = "Oheangbu/Spell/Deploy 308 map TEST", fileName = "SpellDeploy308Map")]
    public sealed class SpellDeploy308MapSO : ScriptableObject
    {
        [Serializable]
        public struct Row
        {
            public string Letter;
            public DeployCategory308 Category;
            public DeployFrame308 Frame;
            public Element Element;
            public DeployFinal308 Final;
            [Tooltip("The row may get impact frames at the burst's first cel (the 50 attack rows). D308-10c: a cast asks for them only when its judged target is groggy at that cel (profile Impact.Trigger).")]
            public bool ImpactFrame;
            [Tooltip("The same for the triggered burst of an installed mark (combo install rows; profile switch Impact.ComboTrigger), under the same groggy condition.")]
            public bool ImpactOnTrigger;
            public DeployLegacyBody308 LegacyBody;
            [Tooltip("Optional form exception (empty = the category form).")]
            public string FormOverride;
            public char Char => string.IsNullOrEmpty(Letter) ? default : Letter[0];
        }

        public string SourceCsvSha256 = "";
        public Row[] Rows = Array.Empty<Row>();

        [NonSerialized] private Dictionary<char, int> _index;

        public int Count => Rows != null ? Rows.Length : 0;

        public bool TryGet(char letter, out Row row)
        {
            if (_index == null || _index.Count != Count) BuildIndex();
            if (_index.TryGetValue(letter, out int i)) { row = Rows[i]; return true; }
            row = default; return false;
        }

        public int CountOf(DeployCategory308 category)
        {
            int n = 0;
            if (Rows != null) foreach (var row in Rows) if (row.Category == category) n++;
            return n;
        }

        private void BuildIndex()
        {
            _index = new Dictionary<char, int>(Count);
            for (int i = 0; i < Count; i++) { char c = Rows[i].Char; if (c != default && !_index.ContainsKey(c)) _index.Add(c, i); }
        }

        private void OnValidate() { _index = null; }

        /// <summary>The Spec's category counts (attack 50 = 25 single + 25 area).</summary>
        public static bool CountsMatch(SpellDeploy308MapSO map, out string report)
        {
            int[] want = { 25, 25, 5, 5, 25, 5, 5, 5, 20 };
            var text = new System.Text.StringBuilder();
            bool ok = map != null && map.Count == 120;
            text.Append("rows ").Append(map != null ? map.Count : 0);
            for (int i = 0; i < want.Length && map != null; i++)
            {
                int n = map.CountOf((DeployCategory308)i);
                text.Append(' ').Append((DeployCategory308)i).Append('=').Append(n);
                ok &= n == want[i];
            }
            report = text.ToString();
            return ok;
        }
    }
}
