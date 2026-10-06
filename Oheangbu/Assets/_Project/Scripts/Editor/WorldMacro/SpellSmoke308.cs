using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 Play smoke for the spell path (DEPLOY_PLAN section 7): casts every glyph through the letter channel the brush
    /// adapter and CombatLoopWiring both listen to (the real resolver, the real wiring of the open scene, no fixture scene), and
    /// records what happened. It compares nothing with "before"; it checks invariants: no exception, the category's effect is
    /// there, a glyph outside the 36 misfires for MisfireInkCost.
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.SpellSmoke308 Run "start"      Edit Mode: isolated save
    ///       world-main_c303smoke_&lt;UTC&gt;.json (Harness303), enters Play itself, runs, leaves Play, cleans the isolated files
    ///   ... Run "run"        Play Mode that is already on an isolated store (a harness suffix, e.g. the armed TEST-unlock Play):
    ///       runs inside it and leaves Play running. Refused on the real slot and outside Play.
    ///   ... Run "status" | "report" | "abort"
    ///   ... Run "quality" | "quality:0" | "quality:PC"   Edit Mode: read / set the editor quality level (Play leaves its own behind)
    ///   ... Run "saves" | "saves:park:&lt;suffix&gt;"       list save files with their sha; move &lt;slot&gt;&lt;suffix&gt;.json* into the run folder
    ///   ... Run "direct" | "direct:on" | "direct:off"    Edit Mode: the next Play starts in the open scene, not the lobby (for the
    ///       armed TEST-unlock Play, which has no start command of its own); direct:off puts the flag and the start scene back
    /// Options, joined by ':' after start / run:
    ///   set=legacy (default, the 36) | new (the other 84: generic invariants only) | all | letters (set=가노서)
    ///   groups=single,area,parry,summon,giyeok,buff,ward,guk,mum   (legacy set only)
    ///   target=&lt;actor id&gt;   default: the nearest living non-boss enemy of the wiring, 60 m or more from the mine tutorial boss
    ///   mode=full (default: stroke events + letter channel + Committed + ModeExited, the Vfx120GameplayAudit precedent)
    ///        | channel (the letter channel only) | wiring (CombatLoopWiring.OnLetterDrawn only; the adapter is not told)
    ///   ai=hold (default: enemy AI within 40 m is switched off for the run) | live
    ///   unlock=fixture (default: the giyeok ledger id is put into the in-memory progress of the isolated store for the
    ///        giyeok / buff / guk groups, and taken out again) | none
    ///   guard=0.1   EnemyVitals.DamageMultiplier of the target during the run, so one cast cannot kill it (put back at the end)
    ///   shots      one game-view capture per accepted glyph
    /// Fixtures (all recorded in the report, all inside the isolated store, none saved to an asset or a scene): ink and player
    /// HP refilled before each glyph, the target restored before each glyph, the target's damage multiplier, enemy AI held,
    /// the giyeok ledger id, teleport, LockOn.Toggle (the public selector, no Tab key), clearing a guard / buff / ward /
    /// summon / lift after its check. No virtual input devices: no key is pressed, so the recogniser and the Input System are
    /// not exercised (Commission303Combat covers that with one glyph).
    /// Output: Art/Playtest308/Smoke/&lt;runId&gt;/smoke308.txt (one line per glyph + "smoke308: N glyphs, F failed").</summary>
    public static class SpellSmoke308
    {
        const string SuffixTag = "_c303smoke_";
        const string Single = "single", Area = "area", Parry = "parry", Summon = "summon", Giyeok = "giyeok", Buff = "buff", Ward = "ward", Guk = "guk", Mum = "mum", New = "new";
        static readonly string[] LegacyGroups = { Single, Area, Parry, Summon, Giyeok, Buff, Ward, Guk, Mum };
        static readonly string[] LegacyLetters = { "가나마사아", "고노모소오", "거너머서어", "곰놈몸솜옴", "각낙삭악", "걱넉먹석억", "구누무수우", "국", "뭄" };
        // what the injected letter claims about the drawing (the Vfx120GameplayAudit values): form distance, mean distance, stroke s, hold s, strokes
        const float Worst = .8f, Average = .75f, StrokeSeconds = 1.2f, HoldSeconds = 1.4f; const int Strokes = 2;
        const double RunLimit = 900, AttackWait = 8, SummonWait = 20, GiyeokWait = 4;

        sealed class Row
        {
            public char letter; public string group = "", status = "", detail = "", resolve = "", effect = "", lockName = "";
            public bool accepted, misfired, summonAccepted; public float inkBefore, inkAfter, power, damage, firstDamage; public string firstSource = "";
            public int hits, planned = -1, exceptions, errors; public double began, firstHitAt = -1, ended; public string firstException = ""; public bool shot;
        }
        sealed class Options
        {
            public string set = "legacy", target = "", mode = "full", scene = "main"; public string[] groups = LegacyGroups;
            public bool holdAi = true, unlock = true, shots; public float guard = .1f;
        }
        sealed class State
        {
            public string status = "idle", note = "", runId = "", folder = ""; public bool ownsPlay; public Options o = new Options();
            public readonly List<Row> rows = new List<Row>(); public readonly List<string> lines = new List<string>(), fixtures = new List<string>();
            public int index, phase, probesOk, probes, strayExceptions, harnessFails; public double began, at; public string summary = "";
            public bool unconfirmed;   // no FAIL, but a group that must be seen in this run was not seen (COND / SKIP): not "OK"
        }

        static State st = new State(); static readonly Isolation303 iso = new Isolation303(); static bool hooked, logHooked;
        // live references: all dropped in Release (domain reload is off on entering Play; nothing here may outlive a run)
        static WorldMacroPlaytestSession s; static CombatLoopWiring wiring; static DrawnLetterEventChannelSO channel; static InkPool ink; static ParryJudge judge;
        static CombatConfigSO config; static LockOn lockOn; static DrawingInputController input; static PlayerVitals player; static EnemyVitals target;
        static ICollection pending; static readonly List<Behaviour> held = new List<Behaviour>();
        static Vector3 stand; static float standYaw, priorMultiplier = 1f; static bool multiplierSet, ledgerAdded, priorBackground, backgroundSet, subscribed, tidied;
        static Row current; static int transformsBefore; static uint guardRevision; static double teleportedAt; static bool gateProbed; static int teleports;
        static double stoppedAt = -1; static Vector3 home; static float homeYaw; static bool homeSet, clearNoted, mainScene; static RaycastHit[] sightHits = new RaycastHit[16];

        static double Now => EditorApplication.timeSinceStartup;
        static string F(float v, string f = "F3") => v.ToString(f, CultureInfo.InvariantCulture);
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Playtest308/Smoke"));
        static void Line(string x) => st.lines.Add(x);

        public static string Run(string command)
        {
            command = (command ?? "").Trim();
            string[] parts = command.Split(':'); string verb = parts[0].Trim().ToLowerInvariant();
            switch (verb)
            {
                case "": case "help": return Help();
                case "status": return Status();
                case "report": return st.lines.Count == 0 ? "no run yet" : string.Join("\n", st.lines);
                case "abort": if (st.status != "running" && st.status != "finishing") return "nothing to abort (" + st.status + ")"; Line("ABORTED by command"); Finish("aborted"); return "aborted";
                case "quality": return Quality(parts.Length > 1 ? parts[1].Trim() : "");
                case "saves": return Saves(parts.Skip(1).Select(p => p.Trim()).ToArray());
                case "direct": return Direct(parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "");
                case "start": return Start(parts.Skip(1), true);
                case "run": return Start(parts.Skip(1), false);
                case "tutorial-skip": return TutorialSkip();
                case "tutorial-status": return tutorialNote + " | timeScale " + F(Time.timeScale, "F2") + " | page '" + (EditorApplication.isPlaying ? UiAdapter303.Page : "") + "'";
            }
            return "refused: unknown command '" + command + "'\n" + Help();
        }

        // 2026-10-04 first-run fix: a new journey that stands still for 8 s gets the #306 mine tutorial card, which stops the
        // game (PauseCoordinator) - in a held Play nobody presses a key, so react-sequence / the HUD probe never advance.
        // Same hook as Vehicle308Checks (PlaytestUiRoot.SkipTutorial306ForHarness = skip every card), isolated store only.
        static double tutorialUntil; static string tutorialNote = "tutorial-skip: not asked";
        static string TutorialSkip()
        {
            if (!EditorApplication.isPlaying) return "refused: tutorial-skip is Play only";
            var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (session == null) return "refused: no session";
            if (string.IsNullOrEmpty(session.TestSaveSuffix)) return "refused: this session is on the real slot";
            tutorialUntil = Now + 40; tutorialNote = "tutorial-skip: watching (no card yet)";
            EditorApplication.update -= TutorialTick; EditorApplication.update += TutorialTick;
            TutorialTick();
            return tutorialNote;
        }

        static void TutorialTick()
        {
            if (!EditorApplication.isPlaying || Now > tutorialUntil)
            {
                EditorApplication.update -= TutorialTick;
                if (tutorialNote.Contains("watching")) tutorialNote = "tutorial-skip: no card appeared in 40 s";
                return;
            }
            var ui = Oheangbu.App.World.UI.PlaytestUiRoot.Instance;
            if (ui != null && ui.SkipTutorial306ForHarness())
            {
                tutorialNote = "tutorial-skip: skipped the #306 tutorial cards (harness) " + F((float)(40 - (tutorialUntil - Now)), "F1") + " s after the command";
                EditorApplication.update -= TutorialTick;
            }
        }

        static string Help() => "SpellSmoke308: start[:opts] (Edit, isolated save, own Play) | run[:opts] (inside a Play on an isolated store) | status | report | abort | quality[:n|name] | saves[:park:<suffix>] | direct[:on|off]\n" +
            "opts: set=legacy|new|all|<letters> groups=single,area,parry,summon,giyeok,buff,ward,guk,mum target=<actor id> mode=full|channel|wiring ai=hold|live unlock=fixture|none guard=0.1 shots";

        static string Status()
        {
            string tail = string.Join(" / ", st.lines.Skip(Math.Max(0, st.lines.Count - 4)));
            return st.status + " | " + st.note + " | glyph " + Math.Min(st.index, st.rows.Count) + "/" + st.rows.Count + (st.summary.Length > 0 ? " | " + st.summary : "") + " | " + tail;
        }

        // ------------------------------------------------------------------ small editor helpers (no Play needed)
        static string Quality(string want)
        {
            string[] names = QualitySettings.names; int now = QualitySettings.GetQualityLevel();
            string read = "quality level " + now + " (" + (now >= 0 && now < names.Length ? names[now] : "?") + ") of [" + string.Join(",", names) + "]";
            if (want.Length == 0) return read;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: quality:<level> is Edit Mode only (in a held Play use Map307Capture hold-quality) | " + read;
            int index = int.TryParse(want, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : Array.FindIndex(names, x => string.Equals(x, want, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index >= names.Length) return "refused: no quality level '" + want + "' | " + read;
            if (index != now) QualitySettings.SetQualityLevel(index, true);
            int after = QualitySettings.GetQualityLevel();
            return "quality level " + now + " -> " + after + " (" + names[after] + "); changed in memory only - the next project save (SaveAssets, an importer run) writes it into ProjectSettings/QualitySettings.asset, so put 0 back before any step that saves";
        }

        // Play of the open scene without the lobby (the two lines of CompactLoadingStartup270.UseCurrent, as Harness303.Apply does),
        // for a Play another harness owns the slot of (the armed TEST-unlock Play): direct:on before Play, direct:off after it.
        const string DirectSet = "SpellSmoke308.Direct.Set", DirectPrior = "SpellSmoke308.Direct.Prior", DirectPresent = "SpellSmoke308.Direct.Present", DirectStartup = "SpellSmoke308.Direct.Startup";
        static string Direct(string verb)
        {
            bool on = SessionState.GetBool(Harness303.DirectPlayKey, false);
            string read = "direct play " + (on ? "on" : "off") + " | start scene '" + AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene) + "' | held by this command: " + SessionState.GetBool(DirectSet, false);
            if (verb.Length == 0) return read;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: direct:on / direct:off are Edit Mode only | " + read;
            if (verb == "on")
            {
                if (SessionState.GetBool(DirectSet, false)) return "already on | " + read;
                SessionState.SetBool(DirectPrior, on);
                SessionState.SetBool(DirectPresent, SessionState.GetBool(Harness303.DirectPlayKey, false) == SessionState.GetBool(Harness303.DirectPlayKey, true));
                SessionState.SetString(DirectStartup, AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene) ?? "");
                SessionState.SetBool(Harness303.DirectPlayKey, true); UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = null;
                SessionState.SetBool(DirectSet, true);
                return "direct play on: the next Play starts in the open scene (no lobby). Run direct:off after that Play. Nothing was saved.";
            }
            if (verb == "off")
            {
                if (!SessionState.GetBool(DirectSet, false)) return "not held by this command (nothing changed) | " + read;
                if (SessionState.GetBool(DirectPresent, false)) SessionState.SetBool(Harness303.DirectPlayKey, SessionState.GetBool(DirectPrior, false)); else SessionState.EraseBool(Harness303.DirectPlayKey);
                string startup = SessionState.GetString(DirectStartup, "");
                UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = startup.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(startup);
                SessionState.EraseBool(DirectSet); SessionState.EraseBool(DirectPrior); SessionState.EraseBool(DirectPresent); SessionState.EraseString(DirectStartup);
                return "direct play put back: flag " + SessionState.GetBool(Harness303.DirectPlayKey, false) + ", start scene '" + startup + "'";
            }
            return "refused: expected direct | direct:on | direct:off";
        }

        static string Saves(string[] args)
        {
            string folder = Harness303.SaveFolder;
            if (args.Length == 0 || args[0].Length == 0)
            {
                if (!Directory.Exists(folder)) return "save folder missing: " + folder;
                var sb = new StringBuilder("save folder " + folder);
                foreach (var f in Directory.GetFiles(folder, "*.json*", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal))
                    sb.Append("\n").Append(Path.GetFileName(f)).Append(" | ").Append(new FileInfo(f).Length).Append(" B | ").Append(File.GetLastWriteTime(f).ToString("s")).Append(" | sha256 ").Append(Harness303.Sha(f));
                return sb.ToString();
            }
            if (args[0] != "park" || args.Length < 2) return "refused: expected saves | saves:park:<suffix>";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: saves:park is Edit Mode only";
            string suffix = args[1];
            // the real slot has no suffix: an empty or odd suffix can never reach it
            if (suffix.Length < 2 || suffix[0] != '_' || suffix.Any(c => !(char.IsLetterOrDigit(c) || c == '_'))) return "refused: the suffix must start with '_' and hold only letters, digits and '_' (the real slot is never moved)";
            var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            string slot = session != null && session.Content != null ? session.Content.SaveSlot : Harness303.MainSlot;
            string park = Path.Combine(Root, "parked", Harness303.UtcStamp()); int moved = 0; var names = new List<string>();
            foreach (string end in new[] { "", ".bak", ".tmp" })
            {
                string f = Path.Combine(folder, slot + suffix + ".json" + end);
                if (!File.Exists(f)) continue;
                Directory.CreateDirectory(park); File.Move(f, Path.Combine(park, Path.GetFileName(f))); moved++; names.Add(Path.GetFileName(f));
            }
            return moved == 0 ? "nothing to park: no " + slot + suffix + ".json* in " + folder : "parked " + moved + " file(s) (" + string.Join(", ", names) + ") into " + park + " (moved, not deleted)";
        }

        // ------------------------------------------------------------------ start
        static string Start(IEnumerable<string> optionTokens, bool fromEdit)
        {
            if (st.status == "running" || st.status == "finishing" || st.status == "cleaning") return "refused: already " + st.status;
            if (!TryOptions(optionTokens, out var o, out string problem)) return "refused: " + problem;
            var rows = BuildRows(o, out problem);
            if (rows == null) return "refused: " + problem;
            if (fromEdit)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: start is Edit Mode only (inside a Play on an isolated store use run)";
                if (!Harness303.TryTarget(o.scene, out string scenePath, out string slot)) return "refused: scene must be main or folklore298";
                // the #307 hazard: a finished harness can leave its suffix on the edit-mode session (Playtest306Checks does the same)
                var stale = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
                if (stale != null && (stale.TestSaveSuffix ?? "").StartsWith("_c303", StringComparison.Ordinal)) { stale.TestSaveSuffix = ""; EditorUtility.ClearDirty(stale); }
                string why = Harness303.Prepare(iso, scenePath, slot, SuffixTag);
                if (why != null) return why;
                Harness303.Apply(iso, Object.FindFirstObjectByType<WorldMacroPlaytestSession>());
            }
            else
            {
                if (!EditorApplication.isPlaying) return "refused: run needs Play Mode (from Edit Mode use start)";
                var live = Harness303.Session;
                if (live == null || !live.InitializationComplete) return "refused: no started WorldMacroPlaytestSession (still on the title or loading)";
                string why = Isolated(live);
                if (why != null) return why;
            }
            Release();
            st = new State { status = "running", ownsPlay = fromEdit, o = o, began = Now, at = Now, runId = Harness303.UtcStamp() };
            st.folder = Path.Combine(Root, st.runId); st.rows.AddRange(rows);
            Line("#308 spell smoke " + DateTime.Now.ToString("s") + " run " + st.runId + (fromEdit ? " isolated suffix " + iso.suffix + " (own Play)" : " inside the running Play") +
                " | set " + o.set + " mode " + o.mode + " ai " + (o.holdAi ? "hold" : "live") + " unlock " + (o.unlock ? "fixture" : "none") + " guard " + F(o.guard, "F2"));
            Line("automated: letters are injected at the letter channel; no key or mouse input, no recogniser. Brush claim: worst " + F(Worst, "F2") + " mean " + F(Average, "F2") + " stroke " + F(StrokeSeconds, "F1") + " s hold " + F(HoldSeconds, "F1") + " s");
            if (!hooked) { EditorApplication.update += Tick; EditorApplication.playModeStateChanged += OnPlayMode; hooked = true; }
            if (fromEdit) EditorApplication.isPlaying = true;
            return "started smoke308 " + st.runId + " (" + st.rows.Count + " glyphs" + (fromEdit ? ", suffix " + iso.suffix : ", in the running Play") + ") folder " + st.folder;
        }

        // null = isolated; otherwise the refusal
        static string Isolated(WorldMacroPlaytestSession session)
        {
            string slot = session.Content != null ? session.Content.SaveSlot : "";
            string file = session.Spell308StoreFileName ?? "";
            if (slot.Length == 0 || file.Length == 0) return "refused: the session has not opened a store yet";
            if (string.Equals(file, slot + ".json", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(session.TestSaveSuffix) || !file.StartsWith(slot + "_", StringComparison.Ordinal))
                return "refused: this session opened '" + file + "', the real slot. The smoke runs only on an isolated store (use start from Edit Mode, or arm the TEST-unlock harness first). Nothing was changed.";
            return null;
        }

        static bool TryOptions(IEnumerable<string> tokens, out Options o, out string problem)
        {
            o = new Options(); problem = null;
            foreach (string raw in tokens)
            {
                string token = raw.Trim(); if (token.Length == 0) continue;
                int eq = token.IndexOf('='); string key = (eq < 0 ? token : token.Substring(0, eq)).ToLowerInvariant(), value = eq < 0 ? "" : token.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "set": o.set = value; break;
                    case "groups": o.groups = value.Split(',').Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).ToArray(); if (o.groups.Any(g => !LegacyGroups.Contains(g))) { problem = "unknown group in '" + value + "'"; return false; } break;
                    case "target": o.target = value; break;
                    case "mode": if (value != "full" && value != "channel" && value != "wiring") { problem = "mode must be full, channel or wiring"; return false; } o.mode = value; break;
                    case "ai": if (value != "hold" && value != "live") { problem = "ai must be hold or live"; return false; } o.holdAi = value == "hold"; break;
                    case "unlock": if (value != "fixture" && value != "none") { problem = "unlock must be fixture or none"; return false; } o.unlock = value == "fixture"; break;
                    case "guard": if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out o.guard) || o.guard <= 0f || o.guard > 1f) { problem = "guard must be in (0, 1]"; return false; } break;
                    case "scene": o.scene = value; break;
                    case "shots": o.shots = true; break;
                    default: problem = "unknown option '" + token + "'"; return false;
                }
            }
            return true;
        }

        static bool IsLegacy(char letter) => LegacyLetters.Any(g => g.IndexOf(letter) >= 0);
        static string GroupOf(char letter) { for (int i = 0; i < LegacyLetters.Length; i++) if (LegacyLetters[i].IndexOf(letter) >= 0) return LegacyGroups[i]; return New; }

        static List<Row> BuildRows(Options o, out string problem)
        {
            problem = null; var rows = new List<Row>(); string set = (o.set ?? "").Trim();
            bool legacy = set == "legacy" || set == "all", fresh = set == "new" || set == "all";
            if (legacy)
                for (int g = 0; g < LegacyGroups.Length; g++)
                    if (o.groups.Contains(LegacyGroups[g])) foreach (char c in LegacyLetters[g]) rows.Add(new Row { letter = c, group = LegacyGroups[g] });
            if (fresh)
                for (int i = 1; i <= SpellGrammar308.Count; i++) { char c = SpellGrammar308.LetterAt(i); if (c != '\0' && !IsLegacy(c)) rows.Add(new Row { letter = c, group = New }); }
            if (!legacy && !fresh)
                foreach (char c in set)
                {
                    if (!SpellGrammar308.TryDecompose(c, out _)) { problem = "'" + c + "' is not one of the 120 vocabulary glyphs (set=legacy|new|all|<letters>)"; return null; }
                    rows.Add(new Row { letter = c, group = GroupOf(c) });
                }
            if (rows.Count == 0) { problem = "no glyph selected"; return null; }
            return rows;
        }

        // ------------------------------------------------------------------ tick
        static void OnPlayMode(PlayModeStateChange change)
        {
            // Play left under a running smoke (the user, another tool, an error pause): nothing live can be put back; close the run
            if (change == PlayModeStateChange.ExitingPlayMode && st.status == "running")
            {
                Line("FAIL Play ended while the smoke was running (glyph " + st.index + "/" + st.rows.Count + ")");
                Restore(); Unsubscribe(); Summarise("interrupted"); st.status = st.ownsPlay ? "finishing" : "done INTERRUPTED"; if (!st.ownsPlay) { Write(); Release(); }
            }
        }

        static void Tick()
        {
            if (st.status == "finishing" && !EditorApplication.isPlayingOrWillChangePlaymode) { Cleanup(); return; }
            if (st.status != "running") return;
            if (Now - st.began > RunLimit) { Line("FAIL timeout after " + RunLimit + " s at glyph " + st.index + " phase " + st.phase + " (" + st.note + ")"); Finish("timeout"); return; }
            if (!EditorApplication.isPlaying) return;
            try { Step(); }
            catch (Exception e) { Line("FAIL harness exception: " + e); Finish("harness error"); }
        }

        static void Step()
        {
            if (s == null)
            {
                s = Harness303.Session;
                if (s == null) { st.note = "waiting for the session"; return; }
            }
            if (!s.InitializationComplete || UiAdapter303.LoadingInProgress == true) { st.note = "waiting for init / loading"; st.at = Now; return; }
            if (wiring == null)
            {
                if (st.ownsPlay && !tidied) { UiAdapter303.CloseMenu(); UiAdapter303.ReleaseGate(); tidied = true; st.at = Now; return; }
                if (Now - st.at < 3) { st.note = "settling"; return; }
                string why = Setup();
                if (why != null) { Line("FAIL setup: " + why); Finish("setup failed"); }
                return;
            }
            if (Time.timeScale < .01f)
            {
                // counted from the moment the stop began, not from the phase start (a stop late in a 20 s summon wait is not "12 s")
                if (stoppedAt < 0) stoppedAt = Now;
                st.note = "time is stopped (page '" + UiAdapter303.Page + "')";
                if (Now - stoppedAt > 2) { UiAdapter303.CloseMenu(); UiAdapter303.ReleaseGate(); }
                if (Now - stoppedAt > 12) { Line("FAIL time scale stayed 0 for 12 s (page '" + UiAdapter303.Page + "'): a card or a menu holds the game"); Finish("blocked"); }
                return;
            }
            if (stoppedAt >= 0) { st.at += Now - stoppedAt; stoppedAt = -1; }   // the game did not run meanwhile: the phase clock does not count it
            if (st.index >= st.rows.Count) { Finish("complete"); return; }
            var row = st.rows[st.index];
            switch (st.phase)
            {
                case 0: Prepare(row); return;
                case 1: if (Now - st.at < .35) return; Cast(row); return;
                case 2: Wait(row); return;
                case 3: if (Now - st.at < .6 || pending != null && pending.Count > 0 && Now - st.at < 6) return; st.index++; st.phase = 0; st.at = Now; return;
            }
        }

        static string Setup()
        {
            string why = Isolated(s);
            if (why != null) return why;
            wiring = s.Walker != null ? s.Walker.Wiring : null;
            if (wiring == null) return "the session's walker has no CombatLoopWiring";
            channel = Harness303.Field<DrawnLetterEventChannelSO>(wiring, "_letterDrawn"); ink = Harness303.Field<InkPool>(wiring, "_ink"); judge = Harness303.Field<ParryJudge>(wiring, "_judge");
            config = Harness303.Field<CombatConfigSO>(wiring, "_config"); lockOn = Harness303.Field<LockOn>(wiring, "_lockOn"); input = Harness303.Field<DrawingInputController>(wiring, "_drawingInput");
            player = Harness303.Field<PlayerVitals>(wiring, "_playerVitals"); pending = Harness303.FieldValue(wiring, "_pendingCasts") as ICollection;
            if (channel == null || ink == null || config == null) return "wiring is missing its letter channel, ink pool or config (channel " + (channel != null) + ", ink " + (ink != null) + ", config " + (config != null) + ")";
            Line("INFO store " + s.Spell308StoreFileName + " | slot " + s.Content.SaveSlot + " suffix '" + s.TestSaveSuffix + "' | quality " + QualitySettings.GetQualityLevel() + " (" + QualitySettings.names[QualitySettings.GetQualityLevel()] + ") | focus app " + Application.isFocused + " game view " + Harness303.GameViewFocused);
            Line("INFO costs: spell " + F(config.SpellInkCost) + " parry " + F(config.ParryInkCost) + " misfire " + F(config.MisfireInkCost) + " | ink capacity x" + F(ink.TotalCapacity, "F2") +
                " | gates: giyeok ledger " + s.HasDemoGuk + ", buffs " + (wiring.EABuffs != null ? wiring.EABuffs.Unlocked.ToString() : "no runtime") + ", wards " + (wiring.EAWards != null ? wiring.EAWards.Available.ToString() : "no runtime") +
                ", giyeok runtime " + (wiring.EAGiyeok != null ? wiring.EAGiyeok.Unlocked.ToString() : "no runtime") + ", guk " + (wiring.FieldSpells != null ? wiring.FieldSpells.IsUnlocked.ToString() : "no service") +
                ", mum " + (wiring.MumBridges != null ? wiring.MumBridges.IsUnlocked.ToString() : "no service") + ", summon combat " + (wiring.SummonCombat != null ? "present" : "none (static preview)"));
            priorBackground = Application.runInBackground; Application.runInBackground = true; backgroundSet = true;
            mainScene = string.Equals(s.gameObject.scene.path, Harness303.MainScene, StringComparison.OrdinalIgnoreCase);
            home = s.Walker.Body.transform.position; homeYaw = s.Walker.Body.transform.eulerAngles.y; homeSet = true;
            bool needsTarget = st.rows.Any(r => r.group == Single || r.group == Area || r.group == Summon || r.group == Giyeok || r.group == New);
            if (needsTarget)
            {
                why = PickTarget();
                if (why != null) return why;
                priorMultiplier = target.DamageMultiplier; target.DamageMultiplier = st.o.guard; multiplierSet = true;
                st.fixtures.Add("target " + target.name + " DamageMultiplier " + F(priorMultiplier, "F2") + " -> " + F(st.o.guard, "F2") + " (nominal damage = applied / " + F(st.o.guard, "F2") + ")");
                if (st.o.holdAi)
                {
                    foreach (var a in s.Actors)
                    {
                        if (a == null || Vector3.Distance(a.transform.position, target.transform.position) > 40f) continue;
                        var ai = a.GetComponent<EnemyController>(); if (ai != null && ai.enabled) { ai.enabled = false; held.Add(ai); }
                        if (a.enabled) { a.enabled = false; held.Add(a); }
                    }
                    st.fixtures.Add("enemy AI held within 40 m of the target: " + held.Count + " component(s) (EnemyController + PrologueEncounter; while its encounter is off a kill is NOT recorded as a defeat - the target is restored before each glyph)");
                    st.fixtures.Add("the player is sent back to " + Harness303.V(home) + " before the AI is switched on again");
                }
                target.DamageResolved += OnDamage;
            }
            else { stand = s.Walker.Body.transform.position; standYaw = s.Walker.Body.transform.eulerAngles.y; }
            wiring.CastAccepted += OnAccepted; wiring.CastMisfired += OnMisfired; wiring.CastPlanned += OnPlanned; wiring.SummonAccepted += OnSummon; subscribed = true;
            if (!logHooked) { Application.logMessageReceived += OnLog; logHooked = true; }
            transformsBefore = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            st.phase = 0; st.at = Now; st.note = "ready";
            return null;
        }

        static string PickTarget()
        {
            var targets = Harness303.FieldValue(wiring, "_targets") as List<EnemyVitals>;
            if (targets == null || targets.Count == 0) return "the wiring has no enemy targets";
            var feet = s.Walker.Body.transform.position;
            if (st.o.target.Length > 0)
            {
                var actor = s.Actors.FirstOrDefault(a => a != null && a.Id == st.o.target);
                target = actor != null ? actor.GetComponent<EnemyVitals>() : null;
                if (target == null || !targets.Contains(target)) return "target '" + st.o.target + "' is not an enemy of this wiring";
                if (!target.IsAlive || !target.isActiveAndEnabled) return "target '" + st.o.target + "' is dead or inactive in this save";
            }
            else
            {
                var boss = s.Actors.FirstOrDefault(a => a != null && a.Id == MineTutorialProfileSO.BossId && a.gameObject.activeInHierarchy);
                Vector3? avoid = boss != null ? boss.transform.position : (Vector3?)null;
                foreach (var candidate in targets.Where(t => t != null && t.IsAlive && t.isActiveAndEnabled && !t.IsBoss).OrderBy(t => Harness303.Flat(t.transform.position, feet)))
                {
                    if (avoid.HasValue && Harness303.Flat(candidate.transform.position, avoid.Value) < 60f) continue;
                    if (!TryStand(candidate, out _, out _)) continue;
                    target = candidate; break;
                }
                if (target == null) return "no living non-boss enemy with NavMesh ground 6 m from it and a clear line of sight (60 m or more from the mine tutorial boss); name one with target=<actor id>";
            }
            if (!TryStand(target, out stand, out standYaw)) return "no NavMesh spot 6 m from target " + target.name + " with a clear line of sight to it";
            var encounter = target.GetComponent<Oheangbu.App.Prologue.PrologueEncounter>();
            Line("INFO target " + (encounter != null ? encounter.Id : target.name) + " hp " + F(target.Hp, "F0") + "/" + F(target.MaxHp, "F0") + " boss " + target.IsBoss + " at " + Harness303.V(target.transform.position) + " | stand " + Harness303.V(stand) + " yaw " + F(standYaw, "F0"));
            return null;
        }

        static bool TryStand(EnemyVitals enemy, out Vector3 feet, out float yaw)
        {
            var e = enemy.transform.position;
            for (int k = 0; k < 8; k++)
            {
                var dir = Quaternion.Euler(0, k * 45f, 0) * enemy.transform.forward;
                if (!NavMesh.SamplePosition(e + dir * 6f, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - e.y) > 2.5f) continue;
                if (!SightClear(enemy, hit.position)) continue;
                var look = Vector3.ProjectOnPlane(e - hit.position, Vector3.up);
                feet = hit.position; yaw = look.sqrMagnitude > .01f ? Quaternion.LookRotation(look).eulerAngles.y : 0f; return true;
            }
            feet = default; yaw = 0f; return false;
        }

        // The wiring's own occlusion rule (GiyeokTargetVisible / ApplyPersistentSpellHit: a ray to the target + 0.4 m, any solid that
        // is not the target blocks). A stand behind a tree or a rock would turn into "no damage" FAILs that are the spot's fault.
        static bool SightClear(EnemyVitals enemy, Vector3 feet)
        {
            foreach (float height in new[] { 1.5f, .9f })
            {
                Vector3 origin = feet + Vector3.up * height, delta = enemy.transform.position + Vector3.up * .4f - origin;
                int count = ScenePhysicsQuery.RaycastAll(enemy.gameObject.scene, origin, delta, delta.magnitude, ~0, ref sightHits);
                for (int i = 0; i < count; i++)
                {
                    var t = sightHits[i].transform;
                    if (sightHits[i].collider.isTrigger || t.IsChildOf(enemy.transform) || (s != null && s.Walker != null && t.IsChildOf(s.Walker.Body.transform))) continue;
                    return false;
                }
            }
            return true;
        }

        // persistent effects of a cast (sword form, zones, capacity buffs, guard, summon, ward): cleared so the next glyph starts clean
        static void ClearEffects()
        {
            if (wiring == null) return;
            if (wiring.SummonCombat != null) wiring.SummonCombat.Clear();
            if (wiring.EABuffs != null) wiring.EABuffs.Clear(Time.time);
            if (wiring.EAWards != null) wiring.EAWards.Clear();
            if (wiring.EAGiyeok != null) wiring.EAGiyeok.Clear();
            if (judge != null) judge.ClearGuard();
            wiring.ClearSpells308(SpellClearReason.Rest);
        }

        // ------------------------------------------------------------------ one glyph
        static bool NeedsTarget(string group) => group == Single || group == Area || group == Summon || group == Giyeok || group == New;

        static void Prepare(Row row)
        {
            st.note = row.letter + " prepare";
            if (Now - teleportedAt < .6) return;   // the camera follows a teleport on the next frames; lock-on reads the camera
            // gate fixture for the groups the giyeok ledger opens: first show that the gate is shut without it (section 7 line 6)
            if (st.o.unlock && !ledgerAdded && !gateProbed && (row.group == Giyeok || row.group == Buff || row.group == Guk) && !s.HasDemoGuk)
            {
                gateProbed = true; GateProbe(row.letter);
                if (s.Progress != null && s.Progress.ledger != null && s.Progress.ledger.completed != null && !s.Progress.ledger.completed.Contains(WorldMacroPlaytestSession.GiyeokUnlockId))
                {
                    s.Progress.ledger.completed.Add(WorldMacroPlaytestSession.GiyeokUnlockId); ledgerAdded = true;
                    st.fixtures.Add("ledger id " + WorldMacroPlaytestSession.GiyeokUnlockId + " added to the in-memory progress of " + s.Spell308StoreFileName + " (HasDemoGuk now " + s.HasDemoGuk + "); taken out at the end");
                }
                // the fixture works only through the demo campaign (HasDemoGuk = DemoCampaignActive && ledger): say so instead of SKIPs
                if (!s.HasDemoGuk) { st.harnessFails++; Line("FAIL fixture: the giyeok ledger id is " + (ledgerAdded ? "in" : "NOT in (no in-memory ledger)") + " but HasDemoGuk is still False (DemoCampaignActive " + s.DemoCampaignActive + "): giyeok / buff / guk cannot be checked in this store"); }
            }
            if (player != null && player.Hp01 < .5f) { player.Restore(1f); st.fixtures.Add(row.letter + ": player HP refilled"); }
            ink.Restore(1f);
            if (NeedsTarget(row.group) && target != null)
            {
                if (!target.IsAlive) { Line("INFO target died before " + row.letter + " (restored)"); }
                if (target.Hp < target.MaxHp || !target.IsAlive) target.Restore();
                if ((Harness303.Flat(s.Walker.Body.transform.position, stand) > .75f || Mathf.Abs(Mathf.DeltaAngle(s.Walker.Body.transform.eulerAngles.y, standYaw)) > 10f) && teleports < 2)
                { s.Teleport(stand, standYaw); teleportedAt = Now; teleports++; return; }
                if (lockOn != null)
                {
                    if (lockOn.Target != null && lockOn.Target != target) lockOn.Toggle();
                    if (lockOn.Target == null) lockOn.Toggle();
                    if (lockOn.Target != null && lockOn.Target != target) lockOn.Toggle();   // the selector chose another enemy: fall back to free aim
                }
            }
            teleports = 0; st.phase = 1; st.at = Now;
        }

        static void GateProbe(char letter)
        {
            var probe = new Row { letter = letter, group = "gate" }; current = probe;
            ink.Restore(1f); probe.inkBefore = ink.Value;
            string error = Inject(letter); probe.inkAfter = ink.Value; current = null;
            float spent = probe.inkBefore - probe.inkAfter, expected = Mathf.Min(probe.inkBefore, config.MisfireInkCost / ink.TotalCapacity);
            bool ok = error == null && probe.misfired && !probe.accepted && Mathf.Abs(spent - expected) < 1e-3f;
            st.probes++; if (ok) st.probesOk++;
            Line((ok ? "PASS " : "FAIL ") + "gate " + letter + " before the unlock fixture: " + (probe.misfired ? "misfire " + probe.resolve : probe.accepted ? "CAST" : "nothing") + ", ink -" + F(spent) + " (misfire cost " + F(expected) + ")" + (error != null ? " | " + error : ""));
            ink.Restore(1f);
        }

        static void Cast(Row row)
        {
            st.note = row.letter + " cast"; current = row; row.began = Now;
            if (NeedsTarget(row.group)) row.lockName = lockOn != null && lockOn.Target != null ? (lockOn.Target == target ? "locked" : "locked on " + lockOn.Target.name) : "free aim";
            if (judge != null) guardRevision = judge.GuardRevision;
            row.inkBefore = ink.Value;
            string error = Inject(row.letter);
            row.inkAfter = ink.Value;
            if (error != null) { row.exceptions++; if (row.firstException.Length == 0) row.firstException = error; }
            st.phase = 2; st.at = Now;   // the capture is taken in Wait (first damage, or 0.4 s in): at the cast frame nothing shows yet
        }

        // returns null, or the exception a listener threw. Ink is read by the caller right after: the dispatch is synchronous.
        static string Inject(char letter)
        {
            if (!SpellGrammar308.TryDecompose(letter, out var glyph)) return "not a vocabulary glyph";
            Jamo? final = glyph.Final == SpellFinal.None ? (Jamo?)null : (Jamo)((int)glyph.Final - 1);
            var drawn = new DrawnLetter(letter, glyph.Initial, glyph.Medial, final, Worst, Average, StrokeSeconds, HoldSeconds, Strokes);
            try
            {
                if (st.o.mode == "wiring")
                {
                    var method = typeof(CombatLoopWiring).GetMethod("OnLetterDrawn", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (method == null) return "CombatLoopWiring.OnLetterDrawn not found";
                    method.Invoke(wiring, new object[] { drawn });
                    return null;
                }
                bool full = st.o.mode == "full" && input != null;
                if (full)
                {
                    var cam = Camera.main; Rect rect = cam != null ? cam.pixelRect : new Rect(0, 0, Screen.width, Screen.height);
                    for (int stroke = 0; stroke < Strokes; stroke++)
                    {
                        RaiseInput("StrokeStarted");
                        for (int p = 0; p < 8; p++)
                        {
                            float u = p / 7f;
                            RaiseInput("StrokePointAdded", new Vector2(rect.x + rect.width * (.42f + .15f * u), rect.y + rect.height * (.43f + .13f * (stroke == 0 ? u : 1f - u))));
                        }
                        RaiseInput("StrokeEnded");
                    }
                }
                channel.Raise(drawn);
                if (full) { RaiseInput("Committed", true); RaiseInput("ModeExited"); }   // the order of a recognised commit
                return null;
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                return inner.GetType().Name + ": " + inner.Message;
            }
        }

        // the input controller's events have no public raise: the adapter's subscriptions are called as the controller would
        static void RaiseInput(string name, params object[] args)
        {
            var field = typeof(DrawingInputController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.GetValue(input) is Delegate callback) callback.DynamicInvoke(args);
        }

        static void Wait(Row row)
        {
            double t = Now - st.at;
            st.note = row.letter + " wait " + t.ToString("F1", CultureInfo.InvariantCulture);
            if (st.o.shots && row.accepted && !row.shot && (row.hits > 0 || t >= (row.group == Parry ? .3 : .4))) { row.shot = true; Harness303.Capture(st.folder, "g_" + st.index.ToString("D3") + "_" + row.group); }
            if (row.accepted)
            {
                if (st.o.shots && !row.shot) return;
                if ((row.group == Single || row.group == Area) && row.hits == 0 && t < AttackWait) return;
                if (row.group == Summon && wiring.SummonCombat != null && wiring.SummonCombat.Supports(row.letter) && row.hits == 0 && t < SummonWait) return;
                if (row.group == Giyeok && t < GiyeokWait && row.hits == 0) return;
                if (row.group == New && t < 2.5) return;
            }
            if (t < .3) return;
            Evaluate(row);
            current = null; row.ended = Now; st.phase = 3; st.at = Now;
            Line(Describe(row));
        }

        static void Evaluate(Row row)
        {
            float spent = row.inkBefore - row.inkAfter, misfireCost = Mathf.Min(row.inkBefore, config.MisfireInkCost / ink.TotalCapacity);
            bool misfireInk = Mathf.Abs(spent - misfireCost) < 1e-3f;
            void Set(string status, string detail) { row.status = status; row.detail = detail; }

            if (row.group == New)
            {
                bool testStore = s.Spell308TestStore;
                if (row.misfired) { st.probes++; if (misfireInk && !row.accepted) { st.probesOk++; Set("PASS", "misfire " + row.resolve); } else Set("FAIL", "misfire " + row.resolve + " but ink -" + F(spent) + " (expected " + F(misfireCost) + ")" + (row.accepted ? " and a cast was accepted" : "")); }
                else if (row.accepted) { if (testStore && spent > 0f) Set("PASS", "cast in the TEST store"); else Set("FAIL", testStore ? "cast accepted without spending ink" : "a glyph outside the 36 was CAST on a store that is not the TEST-unlock store"); }
                else if (Mathf.Abs(spent) < 1e-4f) Set(testStore ? "COND" : "FAIL", "no cast and no misfire (a handler refused in Prepare, or ink was short)" + (testStore ? "" : " on a store where it should misfire"));
                else Set("FAIL", "neither accepted nor misfired, ink -" + F(spent));
                // TEST store: the effect of this glyph must not decide the next one (sword form blocks drawing, zones and buffs persist)
                if (row.accepted && testStore) { ClearEffects(); if (!clearNoted) { clearNoted = true; st.fixtures.Add("new glyphs in the TEST store: persistent effects cleared after each accepted glyph (ClearSpells308(Rest) + buffs, wards, giyeok, summon, guard)"); } }
            }
            else if (row.misfired)
            {
                // A legacy glyph that misfires. "As designed" only where this run has no way to open the gate:
                //   mum (boss proof, no fixture) | giyeok / buff / guk with unlock=none | a ward whose runtime says it is not available.
                // With the ledger fixture in, a giyeok / buff / guk misfire means the fixture did not take or the route is broken: FAIL.
                bool shut = (row.resolve == "Unavailable" || row.resolve == "Locked") && misfireInk;
                bool ledgerGroup = row.group == Giyeok || row.group == Buff || row.group == Guk;
                string what = "misfire " + row.resolve + " for " + F(spent) + " ink";
                if (!shut) Set("FAIL", "misfire " + row.resolve + " (ink -" + F(spent) + ")");
                else if (row.group == Mum) Set("SKIP", "gate shut in this save: " + what + ", as designed (mum needs its boss proof; no fixture)");
                else if (row.group == Guk && wiring.FieldSpells == null) Set("COND", what + ": this scene's wiring has no field-spell service");
                else if (ledgerGroup && !st.o.unlock) Set("SKIP", "gate shut in this save: " + what + ", as designed (unlock=none)");
                else if (ledgerGroup) Set("FAIL", what + " although the giyeok ledger fixture is on (ledger added " + ledgerAdded + ", HasDemoGuk " + s.HasDemoGuk + ", DemoCampaignActive " + s.DemoCampaignActive + "): the fixture did not open the gate, or the resolve route is broken");
                else if (row.group == Ward && wiring.EAWards != null && wiring.EAWards.Available) Set("FAIL", what + " although the ward runtime is available: wards have no gate in this save");
                else if (row.group == Ward) Set("SKIP", what + ": the ward runtime is " + (wiring.EAWards == null ? "missing" : "not available (profile / player HP / wiring)") + " - not checked");
                else Set("FAIL", "misfire " + row.resolve + " (ink -" + F(spent) + ")");
            }
            else if (!row.accepted)
            {
                bool placed = row.group == Ward || row.group == Guk || row.group == Mum;
                string reason = row.group == Guk && wiring.FieldSpells != null ? wiring.FieldSpells.LastFailure : row.group == Mum && wiring.MumBridges != null ? wiring.MumBridges.LastFailure : "";
                if (placed && Mathf.Abs(spent) < 1e-4f) Set("COND", "no cast, no ink spent: the placement was refused here" + (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")"));
                else Set("FAIL", "resolved but not accepted (ink -" + F(spent) + ")");
            }
            else if (spent <= 1e-4f) Set("FAIL", "accepted without spending ink");
            else
            {
                switch (row.group)
                {
                    case Single: case Area:
                        if (target == null) Set("COND", "no target");
                        else if (row.hits > 0 && row.damage > 0f) Set("PASS", "");
                        else Set("FAIL", "accepted, planned " + row.planned + " hit(s), no damage on the target within " + AttackWait + " s (" + row.lockName + ")");
                        break;
                    case Parry:
                    {
                        bool raised = judge != null && judge.GuardRevision != guardRevision, window = judge != null && judge.GuardInWindow(Time.time), stands = judge != null && judge.GuardStands(Time.time);
                        row.effect = "guard raised " + raised + " window " + window + " stands " + stands + (judge != null ? " (window " + F(judge.GuardWindow, "F2") + " s, life " + F(judge.GuardLifetime, "F2") + " s)" : "");
                        if (stands) Set("PASS", ""); else Set("FAIL", "no standing guard after the cast");
                        if (judge != null) judge.ClearGuard();
                        break;
                    }
                    case Summon:
                    {
                        var manager = wiring.SummonCombat; bool combat = manager != null && manager.Supports(row.letter);
                        row.effect = "summon accepted " + row.summonAccepted + (combat ? ", actor " + (manager.Active != null ? manager.Active.name : "none") + " letter '" + (manager.ActiveLetter == '\0' ? "-" : manager.ActiveLetter.ToString()) + "' snapshot " + F(manager.DamageSnapshot, "F1") + ", strikes " + manager.ConsumedStrikes + " confirmed " + manager.ConfirmedHits : ", static preview (no combat manager for this glyph)");
                        if (!row.summonAccepted) Set("FAIL", "cast accepted but SummonAccepted did not fire");
                        // the main scene has a DemoSummonCombatManager: a summon without it there is the regression section 7-4 looks for
                        else if (!combat && mainScene) Set("FAIL", manager == null ? "the main scene's wiring has no summon combat manager (SummonCombat is null): the summon is presentation only" : "the summon combat manager does not support '" + row.letter + "'");
                        else if (!combat) Set("COND", "presentation-only summon in this scene: no damage expected");
                        else if (row.hits > 0 && row.firstSource == "Summon") Set("PASS", "");
                        else Set("FAIL", "summon stood but dealt no damage within " + SummonWait + " s (last failure '" + manager.LastFailure + "', path failures " + manager.PathFailures + ")");
                        if (manager != null) manager.Clear();
                        break;
                    }
                    case Giyeok:
                        row.effect = "giyeok runtime active " + (wiring.EAGiyeok != null ? wiring.EAGiyeok.ActiveCount : 0);
                        if (wiring.EAGiyeok == null) Set("FAIL", "accepted but there is no giyeok runtime");
                        else Set("PASS", row.hits == 0 ? "no damage event within " + GiyeokWait + " s (recorded, not judged)" : "");
                        if (wiring.EAGiyeok != null) wiring.EAGiyeok.Clear();
                        break;
                    case Buff:
                    {
                        bool active = wiring.EABuffs != null && wiring.EABuffs.Active(row.letter, Time.time);
                        row.effect = "buff active " + active + (active ? ", remaining " + F(wiring.EABuffs.Remaining(row.letter, Time.time), "F1") + " s" : "");
                        if (active) Set("PASS", ""); else Set("FAIL", "accepted but the buff is not active");
                        if (wiring.EABuffs != null) wiring.EABuffs.Clear(Time.time);
                        break;
                    }
                    case Ward:
                    {
                        bool exists = wiring.EAWards != null && wiring.EAWards.Exists(Time.time) && wiring.EAWards.Letter == row.letter;
                        row.effect = "ward exists " + exists + (wiring.EAWards != null ? " at " + Harness303.V(wiring.EAWards.Centre) : "");
                        if (exists) Set("PASS", ""); else Set("FAIL", "accepted but no ward stands");
                        if (wiring.EAWards != null) wiring.EAWards.Clear();
                        break;
                    }
                    case Guk:
                    {
                        var field = wiring.FieldSpells; bool up = field != null && field.State != FieldLiftState.None && field.HasPlatform;
                        row.effect = "lift state " + (field != null ? field.State.ToString() : "-") + " platform " + (field != null && field.HasPlatform);
                        if (up) Set("PASS", ""); else Set("FAIL", "accepted but no lift");
                        if (field != null) field.ReleaseSafely();
                        break;
                    }
                    case Mum:
                    {
                        var mum = wiring.MumBridges; int bridges = mum != null ? mum.ActiveCount : 0;
                        row.effect = "bridges " + bridges;
                        if (bridges > 0) Set("PASS", ""); else Set("FAIL", "accepted but no bridge");
                        if (mum != null) mum.ResetForWorldBoundary();
                        break;
                    }
                    default: Set("PASS", ""); break;
                }
            }
            if (row.exceptions > 0) { row.status = "FAIL"; row.detail = (row.detail.Length > 0 ? row.detail + "; " : "") + row.exceptions + " exception(s): " + row.firstException; }
        }

        static string Describe(Row row)
        {
            float spent = row.inkBefore - row.inkAfter, guard = multiplierSet ? st.o.guard : 1f;
            var sb = new StringBuilder();
            sb.Append(row.letter).Append(" | ").Append(row.group).Append(" | ").Append(row.status)
              .Append(" | ").Append(row.misfired ? "misfire " + row.resolve : row.accepted ? "accepted" : "not accepted")
              .Append(" | ink ").Append(F(row.inkBefore)).Append("->").Append(F(row.inkAfter)).Append(" (-").Append(F(spent)).Append(")");
            if (row.accepted) sb.Append(" | power ").Append(F(row.power, "F2"));
            if (NeedsTarget(row.group) && row.accepted)
            {
                sb.Append(" | ").Append(row.lockName).Append(", planned ").Append(row.planned < 0 ? "-" : row.planned.ToString()).Append(", hits ").Append(row.hits);
                if (row.hits > 0) sb.Append(", first ").Append(row.firstSource).Append(" ").Append(F(row.firstDamage, "F2")).Append(" (nominal ").Append(F(row.firstDamage / guard, "F2")).Append(") after ").Append((row.firstHitAt - row.began).ToString("F2", CultureInfo.InvariantCulture)).Append(" s, total ").Append(F(row.damage, "F2")).Append(" (nominal ").Append(F(row.damage / guard, "F2")).Append(")");
            }
            if (row.effect.Length > 0) sb.Append(" | ").Append(row.effect);
            sb.Append(" | exc ").Append(row.exceptions).Append(" err ").Append(row.errors).Append(" | ").Append((Now - row.began).ToString("F1", CultureInfo.InvariantCulture)).Append(" s");
            if (row.detail.Length > 0) sb.Append(" | ").Append(row.detail);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ listeners (read only)
        static void OnAccepted(SpellCast cast, Vector3 origin, Vector3 forward) { if (current != null && cast.Letter == current.letter) { current.accepted = true; current.power = cast.Power; } }
        static void OnMisfired(char letter, SpellResolveStatus status) { if (current != null && letter == current.letter) { current.misfired = true; current.resolve = status.ToString(); } }
        static void OnPlanned(CastPlan plan) { if (current != null && plan != null && plan.Cast.Letter == current.letter) current.planned = Mathf.Max(0, current.planned) + plan.Hits.Count; }
        static void OnSummon(SpellCast cast, Vector3 origin, Vector3 forward) { if (current != null && cast.Letter == current.letter) current.summonAccepted = true; }
        static void OnDamage(EnemyDamageResult result)
        {
            if (current == null || result.AppliedDamage <= 0f) return;
            if (current.hits == 0) { current.firstDamage = result.AppliedDamage; current.firstSource = result.Attack.Source.ToString(); current.firstHitAt = Now; }
            current.hits++; current.damage += result.AppliedDamage;
        }
        static void OnLog(string condition, string stack, LogType type)
        {
            if (st.status != "running") return;
            if (type == LogType.Exception)
            {
                if (current != null) { current.exceptions++; if (current.firstException.Length == 0) current.firstException = FirstLine(condition); }
                else { st.strayExceptions++; if (st.strayExceptions <= 5) Line("INFO exception between glyphs: " + FirstLine(condition)); }
            }
            else if ((type == LogType.Error || type == LogType.Assert) && current != null) current.errors++;
        }
        static string FirstLine(string text) { text = text ?? ""; int n = text.IndexOf('\n'); text = n >= 0 ? text.Substring(0, n) : text; return text.Length > 200 ? text.Substring(0, 200) : text; }

        // ------------------------------------------------------------------ end
        static void Unsubscribe()
        {
            if (subscribed && wiring != null) { wiring.CastAccepted -= OnAccepted; wiring.CastMisfired -= OnMisfired; wiring.CastPlanned -= OnPlanned; wiring.SummonAccepted -= OnSummon; }
            if (target != null) target.DamageResolved -= OnDamage;
            if (logHooked) { Application.logMessageReceived -= OnLog; logHooked = false; }
            subscribed = false;
        }

        // every fixture goes back while the Play is still up (a run inside someone else's Play leaves it as it found it)
        static void Restore()
        {
            try
            {
                if (wiring != null)
                {
                    if (wiring.SummonCombat != null) wiring.SummonCombat.Clear();
                    if (wiring.EABuffs != null) wiring.EABuffs.Clear(Time.time);
                    if (wiring.EAWards != null) wiring.EAWards.Clear();
                    if (wiring.EAGiyeok != null) wiring.EAGiyeok.Clear();
                }
                if (multiplierSet && target != null) target.DamageMultiplier = priorMultiplier;
                // away from the target before its AI comes back (a run inside someone else's Play leaves the player where it found it)
                if (homeSet && s != null && EditorApplication.isPlaying && multiplierSet) s.Teleport(home, homeYaw);
                foreach (var h in held) if (h != null) h.enabled = true;
                if (ledgerAdded && s != null && s.Progress != null && s.Progress.ledger != null && s.Progress.ledger.completed != null) s.Progress.ledger.completed.Remove(WorldMacroPlaytestSession.GiyeokUnlockId);
                if (lockOn != null && lockOn.Target != null) lockOn.Toggle();
                if (backgroundSet) Application.runInBackground = priorBackground;
            }
            catch (Exception e) { Line("INFO restore: " + e.Message); }
            held.Clear(); multiplierSet = ledgerAdded = backgroundSet = false;
        }

        static void Summarise(string how)
        {
            // the summary counts the glyph rows; glyphs the run never reached are counted as failed
            foreach (var row in st.rows) if (row.status.Length == 0) { row.status = "FAIL"; row.detail = "not reached (" + how + ")"; }
            int pass = st.rows.Count(r => r.status == "PASS"), fail = st.rows.Count(r => r.status == "FAIL"), cond = st.rows.Count(r => r.status == "COND"), skip = st.rows.Count(r => r.status == "SKIP");
            int exceptions = st.rows.Sum(r => r.exceptions) + st.strayExceptions;
            foreach (string f in st.fixtures) Line("FIXTURE " + f);
            // Floor: the groups this run can always see must be seen in full. A COND / SKIP there is not a failure of the game,
            // but the run did not check it, so the verdict is UNCONFIRMED, never OK.
            //   single, area, parry: always | summon: in the main scene | giyeok, buff: with unlock=fixture | new: every row
            //   ward, guk: PASS or COND (the spot) | mum: SKIP (no fixture)
            var floor = new StringBuilder(); bool shortOf = false;
            foreach (string g in new[] { Single, Area, Parry, Summon, Giyeok, Buff, New })
            {
                int n = st.rows.Count(r => r.group == g); if (n == 0) continue;
                int p = st.rows.Count(r => r.group == g && r.status == "PASS");
                bool must = g == Single || g == Area || g == Parry || g == New || (g == Summon && mainScene) || ((g == Giyeok || g == Buff) && st.o.unlock);
                if (must && p < n) shortOf = true;
                floor.Append(floor.Length > 0 ? ", " : "").Append(g).Append(' ').Append(p).Append('/').Append(n).Append(must ? "" : " (not required here)");
            }
            foreach (string g in new[] { Ward, Guk, Mum })
            {
                int n = st.rows.Count(r => r.group == g); if (n == 0) continue;
                int skipped = st.rows.Count(r => r.group == g && r.status == "SKIP");
                if (g != Mum && skipped > 0) shortOf = true;
                floor.Append(floor.Length > 0 ? ", " : "").Append(g).Append(" pass ").Append(st.rows.Count(r => r.group == g && r.status == "PASS")).Append(" cond ").Append(st.rows.Count(r => r.group == g && r.status == "COND")).Append(" skip ").Append(skipped).Append('/').Append(n);
            }
            st.unconfirmed = shortOf;
            Line((shortOf ? "SHORT " : "FLOOR ok ") + "passes per group: " + floor + (shortOf ? " - below the floor: the smoke did not confirm every group (verdict UNCONFIRMED unless something FAILed)" : ""));
            if (st.strayExceptions > 0) Line("FAIL " + st.strayExceptions + " exception(s) between glyphs (outside a glyph window): counted in the verdict");
            st.summary = "smoke308: " + st.rows.Count + " glyphs, " + fail + " failed (pass " + pass + ", cond " + cond + ", skip " + skip + "; misfire probes " + st.probesOk + "/" + st.probes +
                "; exceptions " + exceptions + "; stray " + st.strayExceptions + "; harness " + st.harnessFails + "; floor " + (shortOf ? "SHORT" : "ok") + "; " + how + "; " + (Now - st.began).ToString("F0", CultureInfo.InvariantCulture) + " s)";
            Line(st.summary);
        }

        static void Finish(string how)
        {
            if (st.status != "running") return;
            current = null;
            if (EditorApplication.isPlaying && wiring != null)
            {
                Restore();
                int after = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                Line("INFO transforms in the loaded scenes " + transformsBefore + " -> " + after + " (" + (after - transformsBefore).ToString("+0;-0;0") + "; the harness creates none, effects still fading are the game's own)");
            }
            Unsubscribe(); Summarise(how);
            if (st.ownsPlay)
            {
                st.status = "finishing";
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                EditorApplication.delayCall += Cleanup;
            }
            else { Write(); st.status = Verdict(); Release(); }
        }

        // FAIL: a glyph row, a misfire probe, an exception outside a glyph window, or a fixture that did not take. UNCONFIRMED: no
        // failure, but a group below its floor. OK only when every required group was seen in full.
        static string Verdict() => "done " + (st.rows.Any(r => r.status == "FAIL") || st.probes != st.probesOk || st.strayExceptions > 0 || st.harnessFails > 0 ? "FAIL" : st.unconfirmed ? "UNCONFIRMED" : "OK");

        static void Cleanup()
        {
            if (st.status != "finishing") return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += Cleanup; return; }
            st.status = "cleaning";
            bool clean = true;
            foreach (var c in Harness303.Cleanup(iso, st.folder)) { Line(c.status + " " + c.id + " " + c.detail); if (c.status == "FAIL") clean = false; }
            Line("INFO after Play: quality " + QualitySettings.GetQualityLevel() + " (" + QualitySettings.names[QualitySettings.GetQualityLevel()] + ") — set it back with quality:0 if it moved");
            Write();
            st.status = Verdict() + (clean ? "" : " (cleanup FAIL)"); Release();
        }

        static void Write()
        {
            try { Directory.CreateDirectory(st.folder); File.WriteAllText(Path.Combine(st.folder, "smoke308.txt"), string.Join("\n", st.lines), new UTF8Encoding(false)); }
            catch (Exception e) { Line("INFO report not written: " + e.Message); }
        }

        static void Release()
        {
            s = null; wiring = null; channel = null; ink = null; judge = null; config = null; lockOn = null; input = null; player = null; target = null; pending = null; current = null;
            held.Clear(); multiplierSet = ledgerAdded = backgroundSet = subscribed = tidied = gateProbed = homeSet = clearNoted = mainScene = false; teleportedAt = 0; teleports = 0; stoppedAt = -1;
        }
    }
}
