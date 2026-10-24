using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Core.Domain;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using static Oheangbu.EditorTools.Vol2.Vol2ShapeFixtureKit;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 3 edit-mode fixture of the five bodies for the 20 무받침 (한 번) glyphs. No scene, no Play. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2BodyFixture Run "all"
    ///   B1  the 20 rows: each letter's body element and body action, from a table written here out of the 한줄정의 of the CSV (not from the code under test)
    ///   B2  the body numbers equal the NumbersV3 rows (instant, bind, burn, push, wet)
    ///   B3  the 20 letters through the coordinator against one body each: the frame's shape touches it (parry: the window opens), with the body's
    ///       own effect, once
    ///   B4  explicit enum mapping (no integer cast): Body -> Element for the five</summary>
    public static class Vol2BodyFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        // letter, element, action word. From the 한줄정의 of the rows: 붙든다 / 데운다 / 민다 / 벤다 / 적신다 (CSV 몸거동 first clause names the same).
        static readonly (char letter, Element element, string action)[] Expected =
        {
            ('가', Element.Wood, "bind"), ('거', Element.Wood, "bind"), ('고', Element.Wood, "bind"), ('구', Element.Wood, "bind"),
            ('나', Element.Fire, "burn"), ('너', Element.Fire, "burn"), ('노', Element.Fire, "burn"), ('누', Element.Fire, "burn"),
            ('마', Element.Earth, "push"), ('머', Element.Earth, "push"), ('모', Element.Earth, "push"), ('무', Element.Earth, "push"),
            ('사', Element.Metal, "cut"), ('서', Element.Metal, "cut"), ('소', Element.Metal, "cut"), ('수', Element.Metal, "cut"),
            ('아', Element.Water, "wet"), ('어', Element.Water, "wet"), ('오', Element.Water, "wet"), ('우', Element.Water, "wet"),
        };

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-body-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-body-fixture: editor busy (play / compile / import)";
            var r = new Report();
            try { using (var errors = new ErrorCounter()) { Cases(r); Check(r, "E0", errors.Errors == 0, "no error log during the fixture" + (errors.Errors > 0 ? ": " + errors.First : "")); } }
            catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        static string Word(Vol2BodyActionKind k) => k == Vol2BodyActionKind.Bind ? "bind" : k == Vol2BodyActionKind.Burn ? "burn" : k == Vol2BodyActionKind.Push ? "push" : k == Vol2BodyActionKind.Cut ? "cut" : "wet";

        static void Cases(Report r)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "F0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var n = book.Numbers;

            // ---- B1 ----
            var onceRows = book.Spells.Where(s => s.Verb == Verb.Once).ToList();
            Check(r, "B1a", onceRows.Count == 20 && Expected.Length == 20 && Expected.All(e => onceRows.Any(s => s.Letter == e.letter)), "the table has exactly the 20 한 번 rows, and the expectation table names the same 20 letters");
            var bad = new List<string>();
            foreach (var e in Expected)
            {
                var row = onceRows.First(s => s.Letter == e.letter);
                var p = Vol2BodyProfile.For(row.Body, n);
                if (p.Element != e.element || Word(p.Action) != e.action) bad.Add(e.letter + ":" + p.Element + "/" + Word(p.Action));
            }
            Check(r, "B1b", bad.Count == 0, "20/20 letters: body element and body action as the 한줄정의 says" + (bad.Count > 0 ? " BAD " + string.Join(",", bad) : ""));

            // ---- B2 ----
            var wood = Vol2BodyProfile.For(Body.Wood, n); var fire = Vol2BodyProfile.For(Body.Fire, n); var earth = Vol2BodyProfile.For(Body.Earth, n);
            var metal = Vol2BodyProfile.For(Body.Metal, n); var water = Vol2BodyProfile.For(Body.Water, n);
            Check(r, "B2a", wood.Instant == 0.6m && wood.MoveMultiplier == 0.5m && wood.DurationUs == 2000000 && fire.Instant == 1.0m && fire.BurnTotal == 0.3m && fire.DurationUs == 2000000
                && earth.Instant == 1.105m && earth.Utility == 0.15m && metal.Instant == 0.8m && water.Instant == 0.6m && water.DurationUs == 4000000,
                "wood 0.6 / x0.5 / 2 s; fire 1.0 / burn 0.3 over 2 s; earth 1.105 (push value 0.15); metal 0.8; water 0.6 / wet 4 s: the NumbersV3 rows");
            Check(r, "B2b", fire.WetFactor(true) == 0.5m && metal.WetFactor(true) == 1.25m && wood.WetFactor(true) == 1m && earth.WetFactor(true) == 1m && water.WetFactor(true) == 1m && fire.WetFactor(false) == 1m && metal.WetFactor(false) == 1m,
                "a wet body takes fire x0.5 and metal x1.25, nothing else changes, a dry body changes nothing");
            Check(r, "B4", VolMap(Body.Wood, Element.Wood) && VolMap(Body.Fire, Element.Fire) && VolMap(Body.Earth, Element.Earth) && VolMap(Body.Metal, Element.Metal) && VolMap(Body.Water, Element.Water),
                "Body -> Element by an explicit mapping, each of the five");

            // ---- B3 ----
            var failures = new List<string>();
            foreach (var e in Expected)
            {
                var row = onceRows.First(s => s.Letter == e.letter);
                var rig = Rig.Create(book);
                bool parry = row.Glyph.IsParry;
                // one body placed where the frame must touch it: path 4 m ahead, fan 3 m ahead, ring on the band (2 m), attachment 1 m ahead (reached by a swing)
                float ahead = row.Frame == Frame.Path ? 4f : row.Frame == Frame.Fan ? 3f : row.Frame == Frame.Ring ? 2f : 1f;
                rig.Add("Body", ahead, 0f);
                var log = rig.Cast(e.letter, out var dec);
                if (log == null) { failures.Add(e.letter + " no log"); continue; }
                if (row.Frame == Frame.Attachment)
                {
                    var swing = rig.Services.Ids.NextSwing().Value; var sl = new Vector3(0, 1.1f, 0);
                    rig.Runtime.Swings.Raise(new Vol2SwingEvent(swing, sl, sl + Vector3.forward * rig.Coord.Tuning.BrushReach, rig.Services.Clock.NowUs));
                }
                rig.Advance(0.25, 5);
                if (!parry)
                {
                    rig.Advance(2.5, 25);       // lets the longest lasting effect (the burn) end
                    var rec = log.Contacts.Count == 1 ? log.Contacts[0] : null;
                    bool ok = rec != null && rec.BudgetAccepted && rec.DamageApplied && rec.BodyEffect.StartsWith(e.action, StringComparison.Ordinal)
                        && rig.Status.Requests.Count == 1 && Word(rig.Status.Requests[0].Kind) == e.action && log.Shape == ShapeOf(row.Frame) && log.Status == "Done";
                    if (!ok) failures.Add(e.letter + " " + (rec == null ? log.Contacts.Count + " contacts" : "budget " + rec.BudgetReason + " body '" + rec.BodyEffect + "' status " + log.Status));
                }
                else
                {
                    bool ok = log.ParryWindowOpened && log.Contacts.Count == 1 && !log.Contacts[0].DamageRequested && rig.Damage.Requests.Count == 0 && rig.Parry.Windows.Count == 1
                        && rig.Parry.Windows[0].Element == e.element && log.Shape == Vol2ShapeKind.Attachment;
                    if (!ok) failures.Add(e.letter + " parry window " + log.ParryWindowOpened + " contacts " + log.Contacts.Count + " element " + (rig.Parry.Windows.Count > 0 ? rig.Parry.Windows[0].Element.ToString() : "none"));
                }
            }
            Check(r, "B3", failures.Count == 0, "20/20 letters through the coordinator: the frame's shape touches its body once with the body's own effect (the five ㅓ: the parry window opens with the body's element, no effect)" + (failures.Count > 0 ? " FAIL " + string.Join(" | ", failures) : ""));
        }

        static bool VolMap(Body b, Element e) => Vol2BodyProfile.ElementOf(b) == e;
        static Vol2ShapeKind ShapeOf(Frame f) => f == Frame.Path ? Vol2ShapeKind.Path : f == Frame.Attachment ? Vol2ShapeKind.Attachment : f == Frame.Fan ? Vol2ShapeKind.Fan : Vol2ShapeKind.Ring;
    }
}
