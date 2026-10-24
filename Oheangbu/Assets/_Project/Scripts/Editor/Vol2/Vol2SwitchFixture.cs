using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>S5-b slice 1 (D308-149, design s5b_design_v1 section 3) edit-mode fixture of the spell path switch. Everything runs in memory
    /// on objects it creates and destroys (HideAndDontSave): no scene is opened or saved, no asset is written, no Play. Queue:
    /// python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2SwitchFixture Run "all"
    /// The discriminating pair: with an empty SpellResolver the OLD path answers every letter with CastMisfired (NotInVocabulary), so
    /// "misfire seen" = the old path was called and "no misfire" = it was not.
    ///   S1  no route (every scene without the combat scope)      -> old path called
    ///   S2  Legacy308 route                                       -> old path called, no adapter
    ///   S3  Vol2 route, real rulebook                             -> only the adapter is called: 3 accepted, 1 refused, 0 misfires
    ///   S4  Vol2 route, rulebook unavailable                      -> every letter refused RulebookUnavailable, still 0 misfires (no fallback)
    ///   S5  the route is the ONLY difference: the same wiring flips with the injected route; default mode is Legacy308
    ///   S6  the scope's serialized default is Legacy308
    /// Not covered (Play): the real scope building, the lab runner, the channel raised by the drawing input.</summary>
    public static class Vol2SwitchFixture
    {
        [Serializable] sealed class Report { public int pass, fail; public List<string> lines = new List<string>(); }

        public static string Run(string command)
        {
            if ((command ?? "").Trim() != "all") return "REFUSED vol2-switch-fixture: all";
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED vol2-switch-fixture: editor busy (play / compile / import)";
            var r = new Report();
            var made = new List<Object>();
            try { Body(r, made); } catch (Exception e) { Check(r, "EXCEPTION", false, e.ToString()); }
            finally { foreach (var o in made) if (o != null) Object.DestroyImmediate(o); }
            return JsonUtility.ToJson(r, true);
        }

        static void Check(Report r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        sealed class Probe
        {
            public CombatLoopWiring Wiring;
            public int Misfires; public SpellResolveStatus LastStatus;
            public int Accepted, Refused; public Vol2CastRejectReason LastReason;
            public void Cast(char letter) => OnLetterDrawn(Wiring, new DrawnLetter(letter, default, default, null, .6f, .6f, 1f, 1f, 3));
        }

        static readonly MethodInfo OnLetter = typeof(CombatLoopWiring).GetMethod("OnLetterDrawn", BindingFlags.Instance | BindingFlags.NonPublic);
        static void OnLetterDrawn(CombatLoopWiring w, DrawnLetter l) { if (OnLetter == null) throw new MissingMethodException("CombatLoopWiring.OnLetterDrawn"); OnLetter.Invoke(w, new object[] { l }); }

        static Probe NewWiring(List<Object> made, string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            made.Add(go);
            var w = go.AddComponent<CombatLoopWiring>();
            var config = ScriptableObject.CreateInstance<CombatConfigSO>(); config.hideFlags = HideFlags.HideAndDontSave; made.Add(config);
            Set(w, "_resolver", new SpellResolver(null));   // an empty book: the old path answers NotInVocabulary, observably
            Set(w, "_config", config);
            var p = new Probe { Wiring = w };
            w.CastMisfired += (c, s) => { p.Misfires++; p.LastStatus = s; };
            return p;
        }

        static void Set(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new MissingFieldException(target.GetType().Name, field);
            f.SetValue(target, value);
        }

        static void Body(Report r, List<Object> made)
        {
            var book = Vol2RulebookLoader.Load(null, null);
            Check(r, "S0", book.Ok, book.Ok ? "rulebook loaded (" + book.Spells.Count + " spells)" : "UNAVAILABLE: " + book.Error);
            if (!book.Ok) return;

            // S1: no route
            var none = NewWiring(made, "Vol2Switch_NoRoute");
            none.Cast('각'); none.Cast('A');
            Check(r, "S1", none.Misfires == 2 && none.LastStatus == SpellResolveStatus.NotInVocabulary && none.Wiring.SpellMode == SpellRuntimeMode.Legacy308 && none.Wiring.ActiveSpellRoute == null, "no route: old path called twice (" + none.Misfires + " misfires, " + none.LastStatus + "), mode " + none.Wiring.SpellMode);

            // S2: Legacy route
            var legacy = NewWiring(made, "Vol2Switch_Legacy");
            legacy.Wiring.ConstructVol2(SpellRoute.CreateLegacy());
            legacy.Cast('각'); legacy.Cast('걱');
            Check(r, "S2", legacy.Misfires == 2 && legacy.Wiring.SpellMode == SpellRuntimeMode.Legacy308 && legacy.Wiring.ActiveSpellRoute.Adapter == null, "Legacy308 route: old path called twice, no adapter");

            // S3: Vol2 route with the real rulebook (3 glyphs of the 120 + one outside letter)
            var clock = new ManualVol2Clock(0); var ids = new Vol2IdIssuer(1);
            var adapter = new Vol2GlyphAdapter(book.Numbers, null, clock, ids);
            var v2 = NewWiring(made, "Vol2Switch_Vol2");
            adapter.CastAccepted += d => v2.Accepted++;
            adapter.CastRejected += d => { v2.Refused++; v2.LastReason = d.Reason; };
            v2.Wiring.ConstructVol2(SpellRoute.CreateVol2(adapter));
            v2.Cast('각'); v2.Cast('걱'); v2.Cast('가'); v2.Cast('A');   // 각 (peak) / 걱 (recovery) / 가 (bare) / outside
            Check(r, "S3", v2.Misfires == 0 && v2.Accepted == 3 && v2.Refused == 1 && v2.LastReason == Vol2CastRejectReason.NotInVocabulary && v2.Wiring.SpellMode == SpellRuntimeMode.Vol2 && ids.CastsIssued == 3,
                "Vol2 route: adapter 3 accepted / 1 refused, old path misfires " + v2.Misfires + ", CastIds issued " + ids.CastsIssued);

            // S4: Vol2 route, unavailable rulebook -> explicit refusal, never the old path
            var down = NewWiring(made, "Vol2Switch_Unavailable");
            var dead = new Vol2GlyphAdapter(null, "fixture: no data", clock, new Vol2IdIssuer(2));
            dead.CastRejected += d => { down.Refused++; down.LastReason = d.Reason; };
            down.Wiring.ConstructVol2(SpellRoute.CreateVol2(dead));
            down.Cast('각'); down.Cast('걱');
            Check(r, "S4", down.Misfires == 0 && down.Refused == 2 && down.LastReason == Vol2CastRejectReason.RulebookUnavailable && dead.AcceptedCount == 0, "unavailable rulebook: 2 refusals RulebookUnavailable, old path misfires " + down.Misfires);

            // S5: the same wiring flips with the route alone
            var flip = NewWiring(made, "Vol2Switch_Flip");
            flip.Cast('각');
            int before = flip.Misfires;
            var a2 = new Vol2GlyphAdapter(book.Numbers, null, clock, new Vol2IdIssuer(3));
            flip.Wiring.ConstructVol2(SpellRoute.CreateVol2(a2));
            flip.Cast('각');
            int afterVol2 = flip.Misfires;
            flip.Wiring.ConstructVol2(SpellRoute.CreateLegacy());
            flip.Cast('각');
            Check(r, "S5", before == 1 && afterVol2 == 1 && flip.Misfires == 2 && a2.Received == 1, "same wiring: legacy -> misfire +1, Vol2 -> +0 (adapter got 1), legacy again -> +1");

            // S6: the scope's serialized default
            var scopeGo = new GameObject("Vol2Switch_Scope") { hideFlags = HideFlags.HideAndDontSave }; made.Add(scopeGo);
            var scope = scopeGo.AddComponent<CombatLifetimeScope>();
            var field = typeof(CombatLifetimeScope).GetField("_spellRuntimeMode", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(r, "S6", field != null && (SpellRuntimeMode)field.GetValue(scope) == SpellRuntimeMode.Legacy308 && default(SpellRuntimeMode) == SpellRuntimeMode.Legacy308, "CombatLifetimeScope._spellRuntimeMode defaults to Legacy308 (and so does a missing serialized value)");
        }
    }
}
