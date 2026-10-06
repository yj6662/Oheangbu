using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-18 (3) fall rule - read-only editor checks (SPEC-WORLD-FALL-RULE-308). Nothing is written, no scene is opened or saved,
    // no dialog. Queue: Oheangbu.EditorTools.WorldMacro.FallRule308Checks Run "<command>"
    //   table      the case table of FallReferenceRule308 (FallRule308Table): "GREEN: PASS n, FAIL 0"
    //   profiles   every WorldTraversalTestProfile asset with its fall numbers and switch (which rule a scene using it plays)
    //   scene      the OPEN scenes: the session's profile, the walker's slope limit / step offset, the limit the rule will use
    //   play       Play only: the live reference of the running session (height, known, did the last walking tick refresh it)
    public static class FallRule308Checks
    {
        const string Usage = "table | profiles | scene | play";
        public static string Run(string command)
        {
            try
            {
                switch ((command ?? "").Trim())
                {
                    case "table": return FallRule308Table.Report(out _);
                    case "profiles": return Profiles();
                    case "scene": return Scene();
                    case "play": return Play();
                    default: return "refused: use " + Usage;
                }
            }
            catch (Exception e) { Debug.LogException(e); return "error: " + e.GetType().Name + ": " + e.Message; }
        }
        static string Line(WorldTraversalTestProfile p) => "FatalFallHeight " + p.FatalFallHeight.ToString("0.##") + " m, FallReferenceWalkableOnly " + (p.FallReferenceWalkableOnly ? "ON (D308-18)" : "OFF (rule as coded)") +
            ", FallReferenceMaxSlopeDeg " + p.FallReferenceMaxSlopeDeg.ToString("0.##") + (p.FallReferenceMaxSlopeDeg <= 0 ? " (= the walker's limit)" : "") + ", FallReferenceSupportGap " + p.FallReferenceSupportGap.ToString("0.###") + " m";
        static string Profiles()
        {
            var sb = new StringBuilder();
            var guids = AssetDatabase.FindAssets("t:WorldTraversalTestProfile");
            foreach (var path in guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x, StringComparer.Ordinal))
            {
                var p = AssetDatabase.LoadAssetAtPath<WorldTraversalTestProfile>(path);
                if (p != null) sb.AppendLine(path + ": " + Line(p));
            }
            sb.Append(guids.Length + " profile asset(s)");
            return sb.ToString();
        }
        static string Scene()
        {
            if (EditorApplication.isCompiling) return "refused: compiling";
            var sb = new StringBuilder(); int n = 0;
            foreach (var s in UnityEngine.Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                n++;
                sb.AppendLine(s.gameObject.scene.path + " / " + s.name + ":");
                var rules = s.Traversal != null ? s.Traversal.Rules : null;
                if (rules == null) sb.AppendLine("  no traversal profile: the session uses the last SAFE feet (TrySafeFeet: walkable support only, as before) with Content.TestRules.FallDeathHeight");
                else sb.AppendLine("  profile " + AssetDatabase.GetAssetPath(rules) + ": " + Line(rules));
                var body = s.Walker != null ? s.Walker.Body : null;
                if (body == null) { sb.AppendLine("  walker body not bound in Edit mode: the limit is read from the rig's CharacterController at Play"); continue; }
                float limit = FallReferenceRule308.EffectiveSlopeDeg(rules != null ? rules.FallReferenceMaxSlopeDeg : 0, body.slopeLimit);
                sb.AppendLine("  walker: slopeLimit " + body.slopeLimit.ToString("0.##") + " deg, stepOffset " + body.stepOffset.ToString("0.###") + " m, radius " + body.radius.ToString("0.###") + " -> the rule's limit " + limit.ToString("0.##") + " deg");
            }
            sb.Append(n + " session(s) in the open scene(s)");
            return sb.ToString();
        }
        static string Play()
        {
            if (!Application.isPlaying) return "refused: Play only";
            var s = UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (s == null) return "refused: no WorldMacroPlaytestSession in Play";
            var body = s.Walker != null ? s.Walker.Body : null; float feet = body != null ? body.transform.position.y : float.NaN;
            return "reference " + (s.FallReferenceKnown308 ? s.FallReferenceHeight308.ToString("F2") : "unknown") + " m, feet " + feet.ToString("F2") + " m, below the reference " + (s.FallReferenceKnown308 ? (s.FallReferenceHeight308 - feet).ToString("F2") : "n/a") +
                " m, last walking tick refreshed: " + (s.FallReferenceRefreshed308 ? "yes" : "no") + ", seated " + (s.Walker != null && s.Walker.Seated) + (s.Traversal != null && s.Traversal.Rules != null ? "; " + Line(s.Traversal.Rules) : "; no traversal profile");
        }
    }
}
