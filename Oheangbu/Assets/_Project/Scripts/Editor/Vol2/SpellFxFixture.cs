using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.SpellFx;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using static Oheangbu.EditorTools.Vol2.Vol2ShapeFixtureKit;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>Edit-mode fixture of the restart plumbing (D308-153, SPEC-SPELL-FX-RESTART-308 section 5). No Play, no catalogue asset needed (it builds in-memory catalogues and prefab stand-ins
    /// hidden from the scene). Queue:  python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.SpellFxFixture Run "all"
    ///   C1 the real catalogue asset, when it exists: loads, no duplicate (letter, variant), every prefab has a SpellFxPlayer  (SKIP while the asset is not made yet)
    ///   C2 catalogue validation catches a duplicate, a prefab with no player, a letter that is not one character; Find answers only a playable prefab
    ///   C3 the variant switch: default A, per letter, reset
    ///   C4 the silencer: a covered letter's cast gets the silent recipe (Covers stays true, the instance and Parts stay empty), an uncovered letter keeps the legacy bridge recipe,
    ///      a variant with no entry leaves the old look, and releasing the director gives the old look back
    ///   C5 the director builds the prefab at the cast point, begins / launches / relays the executor's hit / ends it, and removes it after play length + tail on the scope clock
    ///   C6 a reset (AbortAll) removes the prefab at once with End(Aborted)
    ///   C7 material and reaction calls reach the prefab's parts without an exception
    ///   C8 an empty catalogue (and a missing one) covers nothing and does nothing
    /// Expected values are written here from the spec, never read back from a run.</summary>
    public static class SpellFxFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        const char Ga = '가';      // P0 wood: a path, one hit
        const char Na = '나';      // P0 fire: a path, one hit (never covered here)
        const char U = '우';       // R0 water: a ring, one hit

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED spellfx-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED spellfx-fixture: editor busy (play / compile / import)";
            var r = new Report();
            try { using (var errors = new ErrorCounter()) { Cases(r); Check(r, "E0", errors.Errors == 0, "no error log during the fixture" + (errors.Errors > 0 ? ": " + errors.First : "")); } }
            catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            finally { SpellFxCatalog.Override = null; SpellFxSwitch.ResetAll(); SpellFxSilencer.KeepBrushSwing = false; }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        /// <summary>A prefab stand-in: a hidden scene object with a player and a log part (a real prefab asset behaves the same under Instantiate).</summary>
        static GameObject MakePrefab(string name, bool withPlayer = true)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            if (withPlayer) go.AddComponent<SpellFxPlayer>();
            go.AddComponent<SpellFxLogPart>();
            return go;
        }

        static SpellFxCatalog MakeCatalog(params (string letter, string variant, GameObject prefab)[] items)
        {
            var c = ScriptableObject.CreateInstance<SpellFxCatalog>();
            c.hideFlags = HideFlags.HideAndDontSave;
            c.entries = items.Select(i => new SpellFxCatalog.Entry { letter = i.letter, variant = i.variant, prefab = i.prefab }).ToArray();
            return c;
        }

        /// <summary>The real coordinator on a hand clock + the old model + the director over a given catalogue, all hidden from the scene.</summary>
        sealed class Scene : IDisposable
        {
            public Rig Rig; public Vol2VfxModel Model; public SpellFxDirector Director;
            public Scene(Vol2Rulebook book, Vol2VfxCatalogue cat, SpellFxCatalog catalog)
            {
                Rig = Rig.Create(book);
                Model = new Vol2VfxModel(Rig.Coord, cat);
                Director = SpellFxDirector.Attach(Rig.Coord, null, catalog, HideFlags.HideAndDontSave);
            }
            public long NowUs => Rig.Services.Clock.NowUs;
            /// <summary>Advance the hand clock and step the model and the director like the view does each frame.</summary>
            public void Drive(double seconds, double dt = 0.05)
            {
                int n = Math.Max(1, (int)Math.Round(seconds / dt));
                for (int i = 0; i < n; i++) { Rig.Advance(seconds / n); Model.Tick(NowUs); Director.Step(); }
            }
            public Vol2VfxInstance Inst(Vol2CastLog log) { Model.TryGet(log.Cast, out var inst); return inst; }
            public void Dispose()
            {
                Director.Release();
                if (Director != null) UnityEngine.Object.DestroyImmediate(Director.gameObject);
                Model.Dispose();
            }
        }

        static void Cases(Report r)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "C0", book.Ok, book.Ok ? "rulebook loaded" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;
            var cat = Vol2VfxCatalogue.Load();
            Check(r, "C0b", cat != null, cat != null ? "old catalogue loaded" : "NO OLD CATALOGUE");
            if (cat == null) return;
            var made = new List<UnityEngine.Object>();
            bool dirtyBefore = UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;
            try
            {
                // ---- C1 the real asset, if it exists
                SpellFxCatalog.Override = null;
                var asset = SpellFxCatalog.Load();
                if (asset == null) Check(r, "C1", true, "SKIP: no catalogue asset at " + SpellFxCatalog.AssetPath + " yet (the director then does nothing - see C8)");
                else
                {
                    bool ok = asset.Validate(out string problems);
                    Check(r, "C1", ok, "catalogue asset " + asset.entries.Length + " entries: unique (letter, variant), a SpellFxPlayer on every prefab" + (ok ? "" : " [" + problems + "]"));
                }

                // ---- C2 validation and Find
                {
                    var pOk = MakePrefab("fx_ok"); var pNoPlayer = MakePrefab("fx_noplayer", false); made.Add(pOk); made.Add(pNoPlayer);
                    var good = MakeCatalog((U.ToString(), "A", pOk), (U.ToString(), "B", pOk), (Ga.ToString(), "A", pOk)); made.Add(good);
                    var dup = MakeCatalog((U.ToString(), "A", pOk), (U.ToString(), "A", pOk)); made.Add(dup);
                    var noPlayer = MakeCatalog((U.ToString(), "A", pNoPlayer)); made.Add(noPlayer);
                    var wrongLetter = MakeCatalog(("ab", "A", pOk)); made.Add(wrongLetter);
                    bool v = good.Validate(out _), d = !dup.Validate(out string dp) && dp.Contains("duplicate"), n = !noPlayer.Validate(out string np) && np.Contains("SpellFxPlayer"), w = !wrongLetter.Validate(out string wp) && wp.Contains("one character");
                    Check(r, "C2a", v && d && n && w, "validation: a good catalogue passes; a duplicate (letter, variant), a prefab with no SpellFxPlayer and a two-character letter are each reported");
                    Check(r, "C2b", good.Find(U, "A") == pOk && good.Find(U, "B") == pOk && good.Find(U, "C") == null && good.Find(Na, "A") == null && noPlayer.Find(U, "A") == null,
                        "Find answers the prefab of the exact (letter, variant) and only when it has a player (U/C and Na/A: none)");
                }

                // ---- C3 the switch
                {
                    SpellFxSwitch.ResetAll();
                    bool def = SpellFxSwitch.Get(U) == "A" && SpellFxSwitch.Get(Ga) == "A";
                    SpellFxSwitch.Set(U, "C");
                    bool per = SpellFxSwitch.Get(U) == "C" && SpellFxSwitch.Get(Ga) == "A";
                    SpellFxSwitch.Set(U, "A");
                    bool back = SpellFxSwitch.Get(U) == "A";
                    SpellFxSwitch.Set(U, "B"); SpellFxSwitch.ResetAll();
                    Check(r, "C3", def && per && back && SpellFxSwitch.Get(U) == "A", "default A; set per letter; setting A or ResetAll puts it back");
                }

                // ---- C4 the silencer
                {
                    var pGa = MakePrefab("fx_ga"); made.Add(pGa);
                    var catalog = MakeCatalog((Ga.ToString(), "A", pGa)); made.Add(catalog);
                    SpellFxSwitch.ResetAll();
                    // each check gets its own scope: the route may refuse a second cast while one is running, and nothing may leak from one to the next
                    string Refused(Vol2CastDecision d) => d != null && !d.Accepted ? " [cast refused: " + d.Reason + "]" : "";
                    using (var s = new Scene(book, cat, catalog))
                    {
                        bool registered = SpellFxSilencer.ProviderCount == 1 && SpellFxSilencer.IsCovered(Ga) && !SpellFxSilencer.IsCovered(Na);
                        var log = s.Rig.Cast(Ga, out var dec);
                        var inst = log != null ? s.Inst(log) : null;
                        Check(r, "C4a", registered && inst != null && inst.Recipe is SpellFxSilentRecipe && inst.Parts.Count == 0 && s.Model.Covers(log),
                            "a covered letter (P0 wood): the model's instance exists with the silent recipe and no part; Covers(log) stays true (the debug lines keep skipping it)" + Refused(dec));
                    }
                    using (var s = new Scene(book, cat, catalog))
                    {
                        var logNa = s.Rig.Cast(Na, out var decNa);
                        var instNa = logNa != null ? s.Inst(logNa) : null;
                        Check(r, "C4b", instNa != null && instNa.Recipe is Vol2LegacyPathRecipe && !(instNa.Recipe is SpellFxSilentRecipe) && instNa.Parts.Count >= 1,
                            "an uncovered letter (P0 fire) keeps the legacy bridge recipe and its own part" + Refused(decNa));
                    }
                    using (var s = new Scene(book, cat, catalog))
                    {
                        SpellFxSwitch.Set(Ga, "B");          // no entry for B: nothing covers it, the old look is back for this cast
                        int spawnedBefore = s.Director.Spawned;
                        var logB = s.Rig.Cast(Ga, out var decB);
                        var instB = logB != null ? s.Inst(logB) : null;
                        Check(r, "C4c", !SpellFxSilencer.IsCovered(Ga) && instB != null && !(instB.Recipe is SpellFxSilentRecipe) && instB.Parts.Count >= 1 && s.Director.Spawned == spawnedBefore && s.Director.Skipped == 1,
                            "a variant with no entry (Ga/B): not covered, the old look stays, the director built nothing and noted the skip" + Refused(decB));
                        SpellFxSwitch.ResetAll();
                    }
                    using (var s = new Scene(book, cat, catalog))
                    {
                        s.Director.Release();
                        var logR = s.Rig.Cast(Ga, out var decR);
                        var instR = logR != null ? s.Inst(logR) : null;
                        Check(r, "C4d", SpellFxSilencer.ProviderCount == 0 && instR != null && !(instR.Recipe is SpellFxSilentRecipe) && instR.Parts.Count >= 1,
                            "after the director is released the silencer has no provider and the old look is back" + Refused(decR));
                    }
                }
                // ---- C5 the director's flow on a path cast that hits a body
                {
                    var pGa = MakePrefab("fx_flow"); made.Add(pGa);
                    var catalog = MakeCatalog((Ga.ToString(), "A", pGa)); made.Add(catalog);
                    SpellFxSwitch.ResetAll();
                    using (var s = new Scene(book, cat, catalog))
                    {
                        s.Rig.Add("Body", 3f, 0f);
                        var log = s.Rig.Cast(Ga, out var dec);
                        var player = log != null ? s.Director.PlayerOf(log.Cast.Value) : null;
                        var part = player != null ? player.GetComponent<SpellFxLogPart>() : null;
                        Check(r, "C5a", player != null && part != null && s.Director.Spawned == 1 && s.Director.ActiveCount == 1 && player.Cast.Letter == Ga && player.Cast.Variant == "A"
                            && player.Cast.Frame == Frame.Path && player.Cast.Body == Body.Wood && player.Cast.Verb == Verb.Once
                            && player.Cast.LaunchUs == Math.Max(log.StartUs, log.Emitter.StartUs) && player.Cast.Origin == Vector3.zero && player.Cast.Forward == Vector3.forward
                            && Mathf.Abs(player.Cast.Radius - log.Spec.PathLength) < 1e-4f && player.transform.position == Vector3.zero,
                            "the cast builds ONE prefab at the cast point with the cast's letter / variant / frame / body / verb, launch = max(start, emitter start), reach = the path length"
                            + (dec != null && !dec.Accepted ? " [cast refused: " + dec.Reason + "]" : ""));
                        if (player == null || part == null) return;
                        Check(r, "C5b", part.Begins == 1 && player.BeginCount == 1 && part.Launches == 1 && player.Launched, "Begin once, then Launch once (a cast that leaves at its commit launches in its first frame)");
                        s.Drive(1.0);
                        Check(r, "C5c", log.Contacts.Count(c => c.BudgetAccepted) == 1 && part.Hits == 1 && s.Director.HitsRelayed == 1 && part.Lines.Any(l => l.Contains(" hit ")) && part.Ticks > 0,
                            "the one budget-approved contact reaches the part as ONE hit (position resolved through the registry), and the part is ticked every step (age " + part.LastTickAge.ToString("0.00") + " s)");
                        Check(r, "C5d", log.Finished && part.Ends == 1 && part.LastEndReason == SpellFxEndReason.Done && player.Cast.Ended && player.Cast.EndUs == log.EndUs && s.Director.ActiveCount == 1,
                            "the executor's CastEnded reaches the part once as Done, the prefab still stands (its play length of 2 s from the launch is not over at 1 s)");
                        s.Drive(2.5);
                        Check(r, "C5e", s.Director.ActiveCount == 0 && s.Director.Retired == 1 && player == null, "past the play length and the tail on the scope clock the director removes the prefab (1 retired, none left)");
                    }
                }

                // ---- C6 a reset removes the prefab at once (a path cast with no body in reach flies 12 m at 36 m/s = 0.33 s, so it is still running at 0.2 s; a finished cast is NOT touched by AbortAll)
                {
                    var pGa = MakePrefab("fx_abort"); made.Add(pGa);
                    var catalog = MakeCatalog((Ga.ToString(), "A", pGa)); made.Add(catalog);
                    SpellFxSwitch.ResetAll();
                    using (var s = new Scene(book, cat, catalog))
                    {
                        var log = s.Rig.Cast(Ga, out var dec);
                        var player = log != null ? s.Director.PlayerOf(log.Cast.Value) : null;
                        var part = player != null ? player.GetComponent<SpellFxLogPart>() : null;
                        bool built = player != null && part != null && s.Director.ActiveCount == 1;
                        s.Drive(0.2);
                        bool running = log != null && !log.Finished;
                        s.Rig.Coord.AbortAll();
                        s.Drive(0.05);
                        Check(r, "C6a", built && running && s.Director.ActiveCount == 0 && part.Ends == 1 && part.LastEndReason == SpellFxEndReason.Aborted && log.Status == "Aborted",
                            "a running path cast builds its prefab; AbortAll (the lab reset) removes it at the next step with End(Aborted), no tail" + (dec != null && !dec.Accepted ? " [cast refused: " + dec.Reason + "]" : ""));
                    }
                    var pU = MakePrefab("fx_ring"); made.Add(pU);
                    var ringCatalog = MakeCatalog((U.ToString(), "A", pU)); made.Add(ringCatalog);
                    using (var s = new Scene(book, cat, ringCatalog))
                    {
                        var log = s.Rig.Cast(U, out var dec);
                        var player = log != null ? s.Director.PlayerOf(log.Cast.Value) : null;
                        Check(r, "C6b", player != null && player.Cast.Frame == Frame.Ring && player.Cast.Body == Body.Water && Mathf.Abs(player.Cast.Radius - log.Spec.RingRadius) < 1e-4f,
                            "a ring cast (R0 water) builds its prefab with the ring radius as its reach" + (dec != null && !dec.Accepted ? " [cast refused: " + dec.Reason + "]" : ""));
                    }
                }
                // ---- C7 material and reaction calls reach the parts
                {
                    var pGa = MakePrefab("fx_relay"); made.Add(pGa);
                    var catalog = MakeCatalog((Ga.ToString(), "A", pGa)); made.Add(catalog);
                    SpellFxSwitch.ResetAll();
                    using (var s = new Scene(book, cat, catalog))
                    {
                        var log = s.Rig.Cast(Ga, out _);
                        var player = log != null ? s.Director.PlayerOf(log.Cast.Value) : null;
                        var part = player != null ? player.GetComponent<SpellFxLogPart>() : null;
                        if (player == null || part == null) { Check(r, "C7", false, "no prefab was built for the relay check"); return; }
                        player.Material(new SpellFxMaterial { Phase = SpellFxMaterialPhase.Created, Id = 7, Kind = Vol2MaterialKind.Residue, Body = Body.Wood, Letter = Ga, Position = Vector3.forward, Radius = 1f });
                        player.Material(new SpellFxMaterial { Phase = SpellFxMaterialPhase.Ended, Id = 7, Kind = Vol2MaterialKind.Residue, Body = Body.Wood, Letter = Ga, End = Vol2MaterialEnd.Expired });
                        player.Reaction(new SpellFxReaction { EventId = 1, ActingBody = Body.Fire, ContactedBody = Body.Wood, Relation = "Transfer", Product = "Child", Point = Vector3.zero, ActingSide = true });
                        Check(r, "C7", part.Materials == 2 && part.Reactions == 1 && player.MaterialCount == 2 && player.ReactionCount == 1, "Material (created, ended) and Reaction reach the prefab's parts");
                    }
                }

                // ---- C8 an empty / missing catalogue does nothing
                {
                    var empty = MakeCatalog(); made.Add(empty);
                    SpellFxSwitch.ResetAll();
                    using (var s = new Scene(book, cat, empty))
                    {
                        var log = s.Rig.Cast(Ga, out var dec);
                        var inst = log != null ? s.Inst(log) : null;
                        s.Drive(0.5);
                        Check(r, "C8a", inst != null && !(inst.Recipe is SpellFxSilentRecipe) && s.Director.Spawned == 0 && s.Director.ActiveCount == 0 && !s.Director.Covers(Ga),
                            "an empty catalogue covers nothing: the old look is drawn, the director builds nothing" + (dec != null && !dec.Accepted ? " [cast refused: " + dec.Reason + "]" : ""));
                    }
                    SpellFxCatalog.Override = null;
                    if (SpellFxCatalog.Load() == null)
                    {
                        using (var s = new Scene(book, cat, null))
                        {
                            var log = s.Rig.Cast(Ga, out _);
                            var inst = log != null ? s.Inst(log) : null;
                            s.Drive(0.5);
                            Check(r, "C8b", s.Director.Catalog == null && inst != null && !(inst.Recipe is SpellFxSilentRecipe) && s.Director.Spawned == 0 && SpellFxSilencer.ProviderCount == 1,
                                "no catalogue asset at all: tolerated, nothing covered, nothing built");
                        }
                    }
                    else Check(r, "C8b", true, "SKIP: the catalogue asset exists now (the missing-asset path was covered while it did not)");
                }
            }
            finally
            {
                SpellFxCatalog.Override = null; SpellFxSwitch.ResetAll();
                foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
                Check(r, "C9", SpellFxSilencer.ProviderCount == 0 && !(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty && !dirtyBefore), "no silencer provider is left registered and the open scene was not dirtied (everything the fixture built is hidden / not saved)");
            }
        }
    }
}
