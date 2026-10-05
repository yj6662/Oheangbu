using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 TEST unlock harness (SPEC-SPELL-120-308 section 7, Q2: the main game keeps its unlock order; the other finals
    /// are reachable only through a TEST unlock in the isolated save "&lt;slot&gt;_spell308_test.json"). Queue safe: never a dialog,
    /// never SaveAssets, never a scene save, never Play entry or exit; a command in the wrong mode answers "refused: ...".
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Spell120Checks308 Run "unlock[:status]"
    ///   ... Run "unlock:arm"            Edit Mode: the open scene's session will open the isolated store at the next Play
    ///   ... Run "unlock:on[:finals]"    Play Mode on the isolated store: TEST unlock on (in Edit Mode while armed: applied by
    ///                                   the harness when the armed Play session has started)
    ///   ... Run "unlock:off[:finals]"   TEST unlock off
    ///   ... Run "unlock:disarm"         Edit Mode: the session goes back to the real slot
    /// finals = names joined by + (n s ng m, or Nieun Siot Ieung Mieum; g / Giyeok only when named; every = all five).
    /// Default (and "all"): the four finals the main game does not grant. Giyeok is granted by the main game through its
    /// ledger, so its TEST unlock matters only for a row that the data keeps TEST-only, or in a fresh isolated save.
    ///
    /// What this harness may touch: WorldMacroPlaytestSession.TestSaveSuffix of the edit-mode session (never saved: the object is
    /// not dirtied, and a scene save strips the suffix for the moment of the save), the SessionState key the title reads
    /// (PlaytestUiReviewSuffix), and the session's TEST unlock flags (not serialized). It never opens, writes or deletes a save
    /// file. The unlock itself is accepted by the session only when the store it OPENED is the isolated one, so a suffix that
    /// changed later cannot unlock an ordinary save (WorldMacroPlaytestSession.Spells308).
    ///
    /// Slot invariant (the #307 hazard: Play exit restores the open scene from its pre-Play backup, which brings an editor
    /// harness suffix back onto the edit-mode session). "_spell308_test" is a fixed suffix, so StaleHarnessSuffix307 does not
    /// clear it. This class does: the suffix counts only while the harness is armed. Not armed = it is removed before Play
    /// starts (ExitingEditMode), one and four seconds after Play ends, and after a recompile.</summary>
    [InitializeOnLoad]
    public static class Spell308TestUnlock
    {
        const string Suffix = WorldMacroPlaytestSession.Spell308TestSuffix;
        const string UiKey = "PlaytestUiReviewSuffix";   // PlaytestUiRoot.DiagnosticSuffix: the title loads the slot this key names
        const string ArmedKey = "Spell308.TestUnlock.Armed", PendingKey = "Spell308.TestUnlock.Pending", StrippedKey = "Spell308.TestUnlock.Stripped";
        const string SweepKeyA = "Spell308.TestUnlock.SweepA", SweepKeyB = "Spell308.TestUnlock.SweepB";
        const string ApplyKey = "Spell308.TestUnlock.Applying", PollKey = "Spell308.TestUnlock.Poll", AppliedKey = "Spell308.TestUnlock.AppliedTo";
        const string Tag = "[Spell308TestUnlock] ";

        static Spell308TestUnlock()
        {
            EditorApplication.playModeStateChanged += Changed;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            // editor start / recompile: scheduled passes do not survive a reload, so look again
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Schedule(2f, 5f);
            Watch();
        }

        static bool Armed => SessionState.GetBool(ArmedKey, false);
        static float Now => (float)EditorApplication.timeSinceStartup;

        public static string Run(string argument)
        {
            string[] parts = (argument ?? "").Trim().Split(':');
            string verb = parts[0].Trim().ToLowerInvariant(), list = parts.Length > 1 ? parts[1] : "";
            if (EditorApplication.isCompiling) return "refused: scripts are compiling; call again when done";
            switch (verb)
            {
                case "": case "status": return Status();
                case "arm": return Arm();
                case "disarm": return Disarm();
                case "on": return Switch(list, true);
                case "off": return Switch(list, false);
            }
            return "refused: expected unlock[:status] | unlock:arm | unlock:on[:finals] | unlock:off[:finals] | unlock:disarm";
        }

        // ---- commands ----
        static string Arm()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: unlock:arm is Edit Mode only (exit Play first)";
            var sessions = Sessions();
            if (sessions.Count > 1) return "refused: expected one WorldMacroPlaytestSession in the open scenes, found " + sessions.Count;
            string ui = SessionState.GetString(UiKey, "");
            if (!string.IsNullOrEmpty(ui) && ui != Suffix) return "refused: the UI suffix key carries '" + ui + "' (another harness is running)";
            // No session in the open scenes (the lobby is open): the title loads the slot the UI key names, which is enough.
            var session = sessions.Count == 1 ? sessions[0] : null;
            if (session != null)
            {
                if (session.Content == null || string.IsNullOrEmpty(session.Content.SaveSlot)) return "refused: the session has no save slot";
                if (!string.IsNullOrEmpty(session.TestSaveSuffix) && session.TestSaveSuffix != Suffix)
                    return "refused: the session carries another save suffix '" + session.TestSaveSuffix + "' (another harness, or its leftover)";
            }
            SessionState.SetBool(ArmedKey, true);
            SetSuffix(session, Suffix);
            SessionState.SetString(UiKey, Suffix);
            return "armed: " + (session != null ? "the next Play of " + session.gameObject.scene.name + " opens " + session.Content.SaveSlot + Suffix + ".json"
                : "no session in the open scenes; a game started from the title opens <slot>" + Suffix + ".json") + " (nothing was saved)\n" + Status();
        }

        static string Disarm()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: unlock:disarm is Edit Mode only (exit Play first)";
            SessionState.EraseBool(ArmedKey); SessionState.EraseString(PendingKey); SessionState.EraseBool(ApplyKey);
            int cleared = ClearLeftovers();
            return "disarmed: " + cleared + " session(s) back on the real slot\n" + Status();
        }

        static string Switch(string list, bool enable)
        {
            if (!TryFinals(list, out var finals, out string problem)) return "refused: " + problem;
            if (EditorApplication.isPlaying)
            {
                var session = Sessions().FirstOrDefault(s => s.isActiveAndEnabled);
                if (session == null) return "refused: no active WorldMacroPlaytestSession (still on the title: start or continue the game first)";
                if (!session.InitializationComplete) return "refused: the session is still starting; call again in a moment";
                if (enable && !session.Spell308TestStore)
                    return "refused: this session opened '" + session.Spell308StoreFileName + "', not the isolated store " + ExpectedStore(session) +
                        ". Exit Play, run unlock:arm in Edit Mode, then enter Play again. Nothing was changed.";
                foreach (var final in finals) session.SetSpell308TestUnlock(final, enable);
                if (Armed) Remember(finals, enable);
                return (enable ? "on: " : "off: ") + Names(finals) + "\n" + Status();
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: Play is starting; call again when it has started";
            if (!Armed) return "refused: unlock:" + (enable ? "on" : "off") + " needs Play Mode on the isolated store, or an armed harness (unlock:arm) that applies it at the next Play";
            Remember(finals, enable);
            return "pending: " + Names(Pending()) + " (applied when the armed Play session has started)\n" + Status();
        }

        static string Status()
        {
            var lines = new List<string>();
            bool play = EditorApplication.isPlaying;
            var pending = Pending();
            lines.Add("mode: " + (play ? "Play" : EditorApplication.isPlayingOrWillChangePlaymode ? "entering Play" : "Edit") + " | harness: " + (Armed ? "armed" : "not armed") +
                " | pending: " + (pending.Count > 0 ? Names(pending) : "none") + " | UI suffix key: '" + SessionState.GetString(UiKey, "") + "'");
            SpellUnlockRule[] rules = null;
            try { var build = SpellTable308.Build(false, out _); if (build.Ok) rules = build.Unlocks; }
            catch (Exception exception) { lines.Add("unlock rules not readable: " + exception.Message); }
            var sessions = Sessions();
            if (sessions.Count == 0) lines.Add("no WorldMacroPlaytestSession in the loaded scenes");
            foreach (var session in sessions)
            {
                string slot = session.Content != null ? session.Content.SaveSlot : "(no content)";
                string suffix = session.TestSaveSuffix ?? "";
                lines.Add("session [" + session.gameObject.scene.name + "] slot " + slot + ", TestSaveSuffix '" + suffix + "' = " +
                    (suffix == Suffix ? "the isolated TEST slot" : suffix.Length == 0 ? "the real slot" : "another harness slot"));
                if (!play) continue;
                lines.Add("  opened store: " + (session.Spell308StoreFileName.Length > 0 ? session.Spell308StoreFileName : "(not opened yet)") +
                    " | isolated: " + (session.Spell308TestStore ? "yes" : "NO (TEST unlock refused)"));
                foreach (SpellFinal final in Every())
                {
                    string owned = "";
                    if (rules != null)
                        foreach (var rule in rules)
                            if (rule.Final == final)
                                owned = " | owned: " + (session.FinalUnlocked(rule) ? "yes" : "no") + " (ledger " + rule.LedgerId + (rule.GrantedInMain ? ", granted in the main game" : ", TEST unlock only") + ")";
                    lines.Add("  " + final + ": TEST " + (session.Spell308TestUnlocked(final) ? "ON" : "off") + owned);
                }
            }
            return string.Join("\n", lines);
        }

        // ---- finals ----
        static IEnumerable<SpellFinal> Every() => new[] { SpellFinal.Giyeok, SpellFinal.Nieun, SpellFinal.Mieum, SpellFinal.Siot, SpellFinal.Ieung };
        static string Names(IEnumerable<SpellFinal> finals) => string.Join("+", finals.Select(f => f.ToString()));

        // "" = the four finals the main game does not grant. Giyeok only when it is named.
        static bool TryFinals(string list, out List<SpellFinal> finals, out string problem)
        {
            finals = new List<SpellFinal>(); problem = null;
            string text = (list ?? "").Trim();
            if (text.Length == 0 || text.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                finals.AddRange(new[] { SpellFinal.Nieun, SpellFinal.Mieum, SpellFinal.Siot, SpellFinal.Ieung });
                return true;
            }
            if (text.Equals("every", StringComparison.OrdinalIgnoreCase)) { finals.AddRange(Every()); return true; }
            foreach (string raw in text.Split('+', ','))
            {
                string token = raw.Trim().ToLowerInvariant();
                SpellFinal final;
                switch (token)
                {
                    case "g": case "giyeok": final = SpellFinal.Giyeok; break;
                    case "n": case "nieun": final = SpellFinal.Nieun; break;
                    case "m": case "mieum": final = SpellFinal.Mieum; break;
                    case "s": case "siot": final = SpellFinal.Siot; break;
                    case "ng": case "ieung": final = SpellFinal.Ieung; break;
                    default: problem = "unknown final '" + raw + "' (use n s ng m g, joined by +)"; return false;
                }
                if (!finals.Contains(final)) finals.Add(final);
            }
            return true;
        }

        static List<SpellFinal> Pending()
        {
            var list = new List<SpellFinal>();
            foreach (string token in SessionState.GetString(PendingKey, "").Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries))
                if (Enum.TryParse(token, out SpellFinal final) && final != SpellFinal.None && !list.Contains(final)) list.Add(final);
            return list;
        }

        static void Remember(List<SpellFinal> finals, bool enable)
        {
            var pending = Pending();
            foreach (var final in finals)
            {
                if (enable && !pending.Contains(final)) pending.Add(final);
                if (!enable) pending.Remove(final);
            }
            if (pending.Count == 0) SessionState.EraseString(PendingKey); else SessionState.SetString(PendingKey, Names(pending));
        }

        // ---- sessions and the suffix ----
        static List<WorldMacroPlaytestSession> Sessions()
        {
            return Object.FindObjectsByType<WorldMacroPlaytestSession>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(s => s != null).ToList();
        }

        static string ExpectedStore(WorldMacroPlaytestSession session) => (session.Content != null ? session.Content.SaveSlot : "<slot>") + Suffix + ".json";

        // Edit-mode object only, never dirtied: the value must not reach the scene file.
        static void SetSuffix(WorldMacroPlaytestSession session, string value)
        {
            if (session == null || session.TestSaveSuffix == value) return;
            bool dirty = EditorUtility.IsDirty(session);
            session.TestSaveSuffix = value;
            if (!dirty) EditorUtility.ClearDirty(session);
        }

        // Not armed: the isolated suffix and its UI key are leftovers. Returns how many sessions were put back.
        static int ClearLeftovers()
        {
            int cleared = 0;
            foreach (var session in Sessions())
                if (session.TestSaveSuffix == Suffix) { SetSuffix(session, ""); cleared++; }
            if (SessionState.GetString(UiKey, "") == Suffix) SessionState.EraseString(UiKey);
            return cleared;
        }

        // ---- the watcher ----
        static void Changed(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    BeforePlay();
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    AfterPlayStart();
                    if (Armed) { SessionState.SetBool(ApplyKey, true); SessionState.SetFloat(PollKey, 0f); SessionState.EraseInt(AppliedKey); Watch(); }
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    SessionState.EraseBool(ApplyKey); SessionState.EraseInt(AppliedKey);
                    Schedule(1f, 4f);   // the scene comes back from its pre-Play backup after this event (#307)
                    break;
            }
        }

        // Play is about to copy the open scene. Armed: the session and the title both name the isolated slot (unless another
        // harness put its own suffix there: that harness owns this Play). Not armed: a leftover never reaches Play.
        static void BeforePlay()
        {
            if (!Armed)
            {
                int cleared = ClearLeftovers();
                if (cleared > 0) Debug.LogWarning(Tag + "removed a leftover '" + Suffix + "' save suffix before Play (the harness is not armed): Play uses the real slot");
                return;
            }
            string ui = SessionState.GetString(UiKey, "");
            var sessions = Sessions();
            bool foreign = sessions.Any(s => !string.IsNullOrEmpty(s.TestSaveSuffix) && s.TestSaveSuffix != Suffix);
            if (foreign || (!string.IsNullOrEmpty(ui) && ui != Suffix)) return;
            foreach (var session in sessions) SetSuffix(session, Suffix);
            SessionState.SetString(UiKey, Suffix);
            // armed stays armed until unlock:disarm (or an editor restart): every Play says which slot it is about to open
            Debug.LogWarning(Tag + "armed: this Play opens the isolated TEST slot (<slot>" + Suffix + ".json), not the real save. Run unlock:disarm in Edit Mode to go back.");
        }

        // Second line of the same guard, for a Play that started without ExitingEditMode reaching this class.
        static void AfterPlayStart()
        {
            if (Armed) return;
            foreach (var session in Sessions())
            {
                if (session.TestSaveSuffix != Suffix) continue;
                if (session.Spell308StoreFileName.Length == 0) { session.TestSaveSuffix = ""; Debug.LogWarning(Tag + "removed a leftover '" + Suffix + "' save suffix at Play start: this Play uses the real slot"); }
                else Debug.LogError(Tag + "this Play opened the TEST store " + session.Spell308StoreFileName + " although the harness is not armed. Exit Play; the suffix is cleared in Edit Mode.");
            }
        }

        static void Schedule(float first, float second)
        {
            SessionState.SetFloat(SweepKeyA, Now + first); SessionState.SetFloat(SweepKeyB, Now + second);
            Watch();
        }

        static void Watch() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }

        static void Tick()
        {
            bool busy = false;
            if (EditorApplication.isPlaying) busy = TickApply();
            else if (!EditorApplication.isPlayingOrWillChangePlaymode) { busy = TickSweep(SweepKeyA); busy |= TickSweep(SweepKeyB); }
            else busy = true;
            if (!busy) EditorApplication.update -= Tick;
        }

        // true = still waiting
        static bool TickSweep(string key)
        {
            float at = SessionState.GetFloat(key, 0f);
            if (at <= 0f) return false;
            if (Now < at) return true;
            SessionState.EraseFloat(key);
            if (Armed) return false;
            int cleared = ClearLeftovers();
            if (cleared > 0) Debug.Log(Tag + "cleared a leftover '" + Suffix + "' save suffix on " + cleared + " edit-mode session(s) (the harness is not armed)");
            return false;
        }

        // The armed Play: when a session is up on the isolated store, the remembered finals are switched on. The flags live on
        // the session object, so a session loaded later in the same Play (back to the title, a new game) gets them again.
        // Twice a second, only while an armed Play runs.
        static bool TickApply()
        {
            if (!SessionState.GetBool(ApplyKey, false)) return false;
            if (Now < SessionState.GetFloat(PollKey, 0f)) return true;
            SessionState.SetFloat(PollKey, Now + .5f);
            var session = Sessions().FirstOrDefault(s => s.isActiveAndEnabled);
            if (session == null || !session.InitializationComplete) return true;
            int id = session.GetInstanceID();
            if (SessionState.GetInt(AppliedKey, 0) == id) return true;
            SessionState.SetInt(AppliedKey, id);
            if (!session.Spell308TestStore)
            {
                Debug.LogWarning(Tag + "the armed Play opened '" + session.Spell308StoreFileName + "', not " + ExpectedStore(session) + ": no TEST unlock was applied");
                return true;
            }
            var pending = Pending();
            foreach (var final in pending) session.SetSpell308TestUnlock(final, true);
            if (pending.Count > 0) Debug.Log(Tag + "TEST unlock on in " + session.Spell308StoreFileName + ": " + Names(pending));
            return true;
        }

        // ---- a scene save never carries the isolated suffix ----
        static void OnSceneSaving(Scene scene, string path)
        {
            bool stripped = false;
            foreach (var session in Sessions())
                if (session.gameObject.scene == scene && session.TestSaveSuffix == Suffix) { session.TestSaveSuffix = ""; stripped = true; }
            if (stripped) SessionState.SetString(StrippedKey, "|" + (scene.path ?? ""));
        }

        static void OnSceneSaved(Scene scene)
        {
            if (SessionState.GetString(StrippedKey, "") != "|" + (scene.path ?? "")) return;
            SessionState.EraseString(StrippedKey);
            if (!Armed || EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var session in Sessions())
                if (session.gameObject.scene == scene && string.IsNullOrEmpty(session.TestSaveSuffix)) SetSuffix(session, Suffix);
        }
    }
}
