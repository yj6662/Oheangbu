using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 ledger recovery (Adopt308.cs): FarLight308 "adopt:<scene>".
    // apply works from the scene, so it runs without a ledger - but revert destroys only what the ledger lists: with the ledger gone a
    // later apply opens an empty one and revert then leaves every sleeve, card and pole of the earlier applies standing. adopt writes
    // the rows apply would have listed: per lights[] row that resolves in this scene the sleeve (paper.name) and the card (glow.name)
    // under its lamp, per enabled poles[] row the holder under the root - only when the tool's own plan (plan:<scene>, the data's
    // variant) says "to write 0 ... MISSING 0, REFUSED 0", and no sleeve / card of this tool's names stands outside those rows.
    // The asset ledger (profile, baked materials) is not adopted: sync keeps what is there, assets:revert is not part of any cycle.
    public static partial class FarLight308
    {
        static readonly Regex PlanTail = new Regex(@"rows: to write (\d+) .*MISSING (\d+), REFUSED (\d+)");
        // the adopted mark (status) and the revert note hold while a row an adopt wrote is still listed (its utc field carries the
        // stamp); rows a later apply lists carry a plain utc, and a revert of everything archives the file
        static string AdoptedLive(Ledger l) => l != null && Adopt308.Is(l.adopted) && l.made.Any(m => m != null && Adopt308.Is(m.utc)) ? l.adopted : null;

        static string Adopt(string scenePath)
        {
            PostLedger308.RequireEditable();
            var c = Load(); string file = LedgerFile(scenePath), alias = PostLedger308.Short(scenePath);
            string head = "FarLight308 adopt " + alias + " (" + c.version + ", paper " + Variant(c, "") + ")\n";
            if (File.Exists(file))
            {
                var old = ReadLedger(scenePath);
                if (Adopt308.Is(old.adopted)) return head + "  " + Adopt308.Already(file, old.adopted, old.made.Count);
                throw new PostLedger308.Refused(Adopt308.Taken(file, old.made.Count));
            }
            // the tool's own plan decides whether the scene is what the data builds (it opens the scene and writes nothing)
            string plan = Pass(scenePath, true, ""); var tail = PlanTail.Match(plan);
            if (!tail.Success) throw new PostLedger308.Refused("the plan of " + alias + " carries no 'rows: to write' line - nothing written:\n" + plan);
            if (tail.Groups[1].Value != "0" || tail.Groups[2].Value != "0" || tail.Groups[3].Value != "0")
                throw new PostLedger308.Refused(alias + " is not what the present data builds (" + tail.Value.Trim() + ") - no ledger is written for a scene the data does not describe. Run plan:" + alias + " for the rows. Nothing written");
            var scene = PostLedger308.Open(scenePath);
            string utc = Adopt308.Utc(), sha = CliffCore308.ShaFile(PostLedger308.Abs(scenePath)), stamp = Adopt308.Stamp(utc, sha);
            var made = new List<Made>(); var mine = new HashSet<Transform>();
            void Take(string what, string row, Transform t) { if (t == null || !mine.Add(t)) return; made.Add(Record(what, row, t, stamp)); }
            foreach (var row in c.lights)
            {
                var lamp = Resolve(scene, row.path, V3(row.at), c.tolerance_m);
                if (lamp == null || Forbidden(c, lamp, out _)) continue;   // a pending optional row: apply listed nothing for it
                if (row.paper) Take("paper", row.id, Child(lamp, c.paper.name));
                if (row.glow) Take("glow", row.id, Child(lamp, c.glow.name));
            }
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == c.root);
            foreach (var pole in c.poles) if (pole.enabled && root != null) Take("pole", pole.id, Child(root.transform, pole.id));
            // a sleeve / card of this tool's names that no row accounts for, or a holder under the root that is not an enabled pole
            var stray = new List<string>();
            foreach (var t in PostLedger308.All<Transform>(scene))
            {
                if (t.name != c.paper.name && t.name != c.glow.name || mine.Contains(t)) continue;
                if (root != null && t.IsChildOf(root.transform)) continue;   // inside a pole: it goes with its holder
                stray.Add(PostLedger308.PathOf(t));
            }
            if (root != null) foreach (Transform t in root.transform) if (!mine.Contains(t)) stray.Add(PostLedger308.PathOf(t));
            if (stray.Count > 0) throw new PostLedger308.Refused(Adopt308.NotEqual(scenePath, stray.Select(x => x + " stands in the scene and no row of the data lists it").ToList(), 6));
            if (made.Count == 0) return head + "  nothing to adopt: no sleeve, card or pole of the data stands in " + scenePath + ". Nothing written";
            if (scene.isDirty) throw new PostLedger308.Refused("the scene is dirty after a read-only plan (bug): reload it. Nothing written");
            var ledger = new Ledger { scene = scenePath, adopted = stamp, made = made };
            ledger.backups.Add(Adopt308.NoBackup);
            WriteLedger(scenePath, ledger);
            return head + "  plan: " + tail.Value.Trim() + "\n"
                + "  wrote " + made.Count + " row(s) (" + string.Join(", ", made.GroupBy(m => m.what).Select(g => g.Key + " " + g.Count())) + "); " + stamp + "\n"
                + "  " + Adopt308.Unknown + ": the scene bytes before the first apply (revert destroys these objects and restores nothing)\n"
                + "  ledger " + file + " (the scene was not changed)";
        }
    }
}
