using System;
using System.Collections.Generic;
using System.IO;
using Oheangbu.App.Demo;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 D308-115: turns the five summon behaviours on in the live Architecture296 profiles, recording every previous value
    /// so `behaviour308:off` restores them exactly. A profile whose attack path the behaviour needs is off is left untouched.</summary>
    public static class DemoSummonBehaviour308Switch
    {
        const string LiveData = "Assets/_Project/Art/World/Architecture296/Data";
        static string RecordPath => Path.Combine(DemoSummonAuthoring.Output, "behaviour308-before.json");
        [Serializable] sealed class Entry { public string path, letter; public bool root, judge, beat, hunt, ward; }
        [Serializable] sealed class Record { public List<Entry> entries = new List<Entry>(); }
        [Serializable] sealed class Result { public string status; public List<string> changed = new List<string>(), skipped = new List<string>(); }

        public static string Execute(string command)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode required");
            if (command == "on") return On();
            if (command == "off") return Off();
            throw new ArgumentException("behaviour308: on/off");
        }

        static string On()
        {
            if (File.Exists(RecordPath)) throw new InvalidOperationException("Already on (record exists): " + RecordPath);
            var record = new Record(); var result = new Result();
            foreach (string guid in AssetDatabase.FindAssets("t:SummonCombatProfile", new[] { LiveData }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var p = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(path);
                if (p == null) continue;
                var before = new Entry { path = path, letter = p.Letter, root = p.RootMark308Enabled, judge = p.JudgeEnabled, beat = p.BeatEnabled, hunt = p.HuntEnabled, ward = p.WardEnabled };
                bool needed;
                switch (p.Letter)
                {
                    case "곰": needed = true; p.RootMark308Enabled = true; break;
                    case "놈": needed = p.FlameAttackEnabled; if (needed) p.JudgeEnabled = true; break;
                    case "몸": needed = p.ClubAttackEnabled; if (needed) p.BeatEnabled = true; break;
                    case "솜": needed = p.TigerAttackEnabled; if (needed) p.HuntEnabled = true; break;
                    case "옴": needed = p.WaterAttackEnabled; if (needed) p.WardEnabled = true; break;
                    default: continue;
                }
                if (!needed) { result.skipped.Add(path + " (attack path off)"); continue; }
                if (!p.TryValidate(out string error))
                {
                    Restore(p, before); result.skipped.Add(path + " (invalid: " + error + ")"); continue;
                }
                record.entries.Add(before); EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p); result.changed.Add(path + " " + p.Letter);
            }
            File.WriteAllText(RecordPath, JsonUtility.ToJson(record, true));
            result.status = result.changed.Count > 0 ? "ON" : "NOTHING";
            return JsonUtility.ToJson(result, true);
        }

        static string Off()
        {
            if (!File.Exists(RecordPath)) throw new InvalidOperationException("No record: " + RecordPath);
            var record = JsonUtility.FromJson<Record>(File.ReadAllText(RecordPath)); var result = new Result();
            foreach (var entry in record.entries)
            {
                var p = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(entry.path);
                if (p == null) { result.skipped.Add(entry.path + " (missing)"); continue; }
                Restore(p, entry); EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p); result.changed.Add(entry.path + " " + entry.letter);
            }
            if (result.skipped.Count == 0) File.Delete(RecordPath);
            result.status = result.skipped.Count == 0 ? "OFF" : "PARTIAL";
            return JsonUtility.ToJson(result, true);
        }

        static void Restore(SummonCombatProfile p, Entry e)
        {
            p.RootMark308Enabled = e.root; p.JudgeEnabled = e.judge; p.BeatEnabled = e.beat; p.HuntEnabled = e.hunt; p.WardEnabled = e.ward;
        }
    }
}
