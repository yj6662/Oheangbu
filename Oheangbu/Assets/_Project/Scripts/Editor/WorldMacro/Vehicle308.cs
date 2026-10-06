using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 마석 자동차 UX (SPEC-VEHICLE-UX-308, D308-8) — data authoring + read-only data checks [TEST values throughout].
    // Queue-safe: Run(string) never opens a dialog; refusals come back as "REFUSED ...". Scenes are never modified or saved: the runtime
    // finds the two SOs through Resources (Vehicle308/…) and adds VehicleRider308 / VehicleInkPresentation308 itself, so there is no
    // per-scene wiring and no scene ledger. Every asset write goes through one idempotent ledger per key (Art/Playtest308/Vehicle/
    // ledger_<key>.json) with a byte backup of the file as it was; a second identical apply reports "변경 없음"; revert restores the
    // pristine bytes (modified assets) or deletes the asset (created by this ledger). Only the edited asset is saved (SaveAssetIfDirty).
    // Every write / "변경 없음" is verified on the FILE afterwards (text re-read): a change that did not land returns "FAILED …", never
    // a silent no-op. (#308 fix, 2026-10-02: fields-derive loaded the fields asset before EditorSceneManager.OpenScene(Single); the
    // scene open unloads unused assets, so the edit went to a stale managed object while ToJson / SetDirty / SaveAssetIfDirty resolved
    // the reloaded one — before == after, "변경 없음", file still "Fields: []". Assets are now loaded after any scene open, Edit refuses a
    // stale reference, and the file check catches any other divergence.)
    //   status | fields-status                    read-only; fields-status compares the in-memory boss fields with the file's ids
    //   assets | assets-revert [--force]          M_VehicleInkShell308.mat (needs Shaders/VehicleInkShell308.shader copied in first),
    //                                             Resources/Vehicle308/VehicleUx308Profile.asset (+ShellMaterial, M_InkDust, seated clip slot,
    //                                             D308-8c CallStrokeMaterial = M_ParryCounterStroke306 when empty — the existing InkStroke ribbon),
    //                                             Resources/Vehicle308/VehicleBossFields308.asset (empty until fields-derive)
    //   fields-derive:<scene> [margin=<m>] | fields-revert [--force]
    //                                             opens <scene> (one of the three; protected / dirty refused; never saved) and merges
    //                                             its boss encounters into VehicleBossFields308: actors with EnemyVitals.IsBoss + the named
    //                                             list (cheongryong, sinmok263, south_gate_general, mine_tutorial_boss, folklore298/agwi).
    //                                             Centre = content Encounter.Feet, Radius = Leash + margin (TEST 8). Enabled / OpenAfterDefeat /
    //                                             Polygon edits by hand are kept. Run on #296 -> #298 -> W_Demo_Main (union, idempotent).
    //   ride-layer-apply | ride-layer-revert [--force]
    //                                             the active scene's player appearance controller gets a weight-0 override layer "Ride308"
    //                                             (Empty default + "Seated" = profile.SeatedClip, speed parameter Ride308Speed), last layer
    //   checks:data                               profile / material / shader property names / LDR colours / clip; boss fields vs the escort
    //                                             stops and rest checkpoints of the three contents -> Art/Playtest308/Vehicle/checks-data.txt
    // Play checks (AC-V1..V7) are Vehicle308Checks.
    public static class Vehicle308
    {
        public const string MainScene = "Assets/_Project/Scenes/World/W_Demo_Main.unity";
        public const string Scene296 = "Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
        public const string Scene298 = "Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity";
        static readonly string[] Scenes = { Scene296, Scene298, MainScene };
        static readonly string[] Contents =
        {
            "Assets/_Project/Art/World/Architecture296/Data/a3cb428dc79f23e45a4caf7729b52193_7a0b9698e5728bd4ea84bd5116570b9f_2b30a7723e5289c47a4fd3cde0d788cf_Content.asset",
            "Assets/_Project/Art/Characters/Folklore298/Data/WorldContent298.asset",
            "Assets/_Project/Scenes/World/Main/WorldContent_Main.asset",
        };
        public const string ShaderPath = "Assets/_Project/Shaders/VehicleInkShell308.shader";
        public const string ShaderName = "Oheangbu/VehicleInkShell308";
        public const string ShellMaterialPath = "Assets/_Project/Art/World/WorldMacro/MagicStoneCar/Materials/M_VehicleInkShell308.mat";
        public const string ResourcesFolder = "Assets/_Project/Resources/Vehicle308";
        public const string ProfilePath = ResourcesFolder + "/VehicleUx308Profile.asset";
        public const string FieldsPath = ResourcesFolder + "/VehicleBossFields308.asset";
        public const string DustMaterialPath = "Assets/_Project/Art/Materials/M_InkDust.mat";
        public const string SeatedClipPath = "Assets/_Project/Art/Characters/Npc306/Clips/N306_WritingSeated.fbx";
        public const string SeatedClipName = "N306_WritingSeated";
        // D308-8c: the call stroke's trail / flow reuse the parry counter stroke's InkStroke material (copied per ribbon at run time, flash add 0)
        public const string CallStrokeMaterialPath = "Assets/_Project/Art/Telegraph306/M_ParryCounterStroke306.mat";
        public const string InkStrokeShaderName = "Oheangbu/InkStroke";
        public static readonly string[] NamedBosses = { "cheongryong", "sinmok263", "south_gate_general", "mine_tutorial_boss", "folklore298/agwi" };
        public static readonly string[] EscortStops = { "escort_start", "checkpoint_1", "checkpoint_2", "cargo_delivery" };
        const float DefaultMargin = 8f;

        public static string Out => Path.Combine(Harness303.RepoRoot, "Art", "Playtest308", "Vehicle");
        sealed class Refuse : Exception { public Refuse(string m) : base(m) { } }
        // a write that did not land (or a stale reference that would have lost it): reported as "FAILED <message>", no stack
        sealed class Failed : Exception { public Failed(string m) : base(m) { } }
        static string F(float v, string f = "F2") => v.ToString(f, CultureInfo.InvariantCulture);

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return Status();
                if (c == "fields-status") return FieldsStatus();
                if (c == "checks:data") return ChecksData();
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only (Play is running)";
                bool force = c.EndsWith(" --force", StringComparison.Ordinal); if (force) c = c.Substring(0, c.Length - 8).TrimEnd();
                if (c == "assets") return Assets();
                if (c == "assets-revert") return Revert("vehicle308_assets", force);
                if (c.StartsWith("fields-derive:", StringComparison.Ordinal)) return FieldsDerive(c.Substring(14).Trim());
                if (c == "fields-revert") return Revert("vehicle308_fields", force);
                if (c == "ride-layer-apply") return RideLayerApply();
                if (c == "ride-layer-revert") return Revert("vehicle308_ridelayer", force);
            }
            catch (Refuse r) { return "REFUSED " + r.Message; }
            catch (Failed f) { return "FAILED " + f.Message; }
            catch (Exception e) { return "FAILED " + e; }
            return "REFUSED unknown Vehicle308 command '" + c + "' (status | fields-status | assets | assets-revert [--force] | fields-derive:<scene> [margin=<m>] | fields-revert [--force] | ride-layer-apply | ride-layer-revert [--force] | checks:data)";
        }

        // ---------- ledger (Content308 pattern) ----------

        [Serializable] sealed class Write { public string utc = "", asset = "", backup = "", shaBefore = "", shaAfter = ""; public bool created; public List<string> changes = new List<string>(); }
        [Serializable] sealed class Ledger
        {
            public string kind = "", created = "";
            public List<string> assets = new List<string>(), pristine = new List<string>(), pristineSha = new List<string>();
            public List<bool> createdHere = new List<bool>();
            public List<Write> writes = new List<Write>();
        }
        static string LedgerFile(string key) => Path.Combine(Out, "ledger_" + key + ".json");
        static Ledger ReadLedger(string key) { var f = LedgerFile(key); return File.Exists(f) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(f)) : null; }
        static void WriteLedger(string key, Ledger l) { Directory.CreateDirectory(Out); File.WriteAllText(LedgerFile(key), JsonUtility.ToJson(l, true)); }
        static string BackupDir() => Path.Combine(Out, "Backups", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture));
        static Ledger Open(string key) => ReadLedger(key) ?? new Ledger { kind = key, created = DateTime.UtcNow.ToString("O") };

        static void Guard(string path) { if (Harness303.IsProtected(path)) throw new Refuse("protected path " + path); }

        // Runs `edit` on an existing asset: unchanged serialized form writes nothing (null); otherwise backs up the file first.
        // `verify` reads the asset FILE text after the step (also when nothing was written) and returns null when the expected
        // state is on disk, else what is missing -> "FAILED" (the ledger is kept when bytes were written, so revert still works).
        static Write Edit(Object asset, string backupDir, Ledger ledger, Func<List<string>> edit, Func<string, string> verify)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) throw new Failed("the object to edit has no asset path (unloaded or never saved) - nothing written");
            Guard(path); Live(asset, path);
            if (EditorUtility.IsDirty(asset)) throw new Refuse(path + " has unsaved in-memory changes (another session?) - save or discard them first");
            string before = EditorJsonUtility.ToJson(asset); List<string> changes;
            try { changes = edit(); }
            catch { EditorJsonUtility.FromJsonOverwrite(before, asset); throw; }
            if (EditorJsonUtility.ToJson(asset) == before) { Verify(path, verify, ledger, null); return null; }
            string abs = Harness303.Abs(path); Directory.CreateDirectory(backupDir);
            string copy = Path.Combine(backupDir, Path.GetFileName(path)); File.Copy(abs, copy, true);
            var w = new Write { utc = DateTime.UtcNow.ToString("O"), asset = path, backup = copy, shaBefore = Harness303.Sha(abs), changes = changes ?? new List<string>() };
            if (!ledger.assets.Contains(path)) { ledger.assets.Add(path); ledger.pristine.Add(copy); ledger.pristineSha.Add(w.shaBefore); ledger.createdHere.Add(false); }
            EditorUtility.SetDirty(asset); SaveFile(path);
            w.shaAfter = Harness303.Sha(abs); ledger.writes.Add(w);
            Verify(path, verify, ledger, w); return w;
        }
        static Write Created(Object asset, Ledger ledger, string what, Func<string, string> verify = null)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            var w = new Write { utc = DateTime.UtcNow.ToString("O"), asset = path, created = true, changes = new List<string> { "created " + what } };
            if (!ledger.assets.Contains(path)) { ledger.assets.Add(path); ledger.pristine.Add(""); ledger.pristineSha.Add(""); ledger.createdHere.Add(true); }
            SaveFile(path);
            w.shaAfter = File.Exists(Harness303.Abs(path)) ? Harness303.Sha(Harness303.Abs(path)) : ""; ledger.writes.Add(w);
            string name = asset.name;   // read before the check: the default verify needs the asset's own name in the file
            Verify(path, verify ?? (text => text.Contains("m_Name: " + name) ? null : "lacks 'm_Name: " + name + "'"), ledger, w);
            return w;
        }
        // every dirty object of that one file (main asset and sub-assets such as a new state machine) — never SaveAssets
        static void SaveFile(string path) => AssetDatabase.SaveAssetIfDirty(new GUID(AssetDatabase.AssetPathToGUID(path)));

        // #308 fix: a reference taken before a scene open can be a stale managed object (OpenScene Single unloads unused assets; the
        // asset is reloaded on the next access as a NEW managed instance). Edits on the stale one never reach ToJson / SetDirty /
        // SaveAssetIfDirty, which resolve the instance id to the reloaded object -> the old silent "변경 없음". Refuse it instead.
        static void Live(Object asset, string path)
        {
            var live = AssetDatabase.LoadMainAssetAtPath(path);
            // a destroyed native object reads as null; a ScriptableObject's data lives in its managed instance, so that one must be the
            // very object the asset database holds now (native types like Material / AnimatorController keep their data natively)
            if (asset == null || live == null || (asset is ScriptableObject && !ReferenceEquals(live, asset)))
                throw new Failed("stale in-memory reference to " + path + " (the object was unloaded and reloaded, e.g. by a scene open) - the edit would be lost; nothing written. Load the asset after any scene open and run again");
        }
        // Re-reads the asset file (text serialization) and runs the step's check. On failure the ledger is written first when bytes
        // changed (w != null), so revert keeps its backup, then "FAILED <what>" is returned by Run.
        static void Verify(string path, Func<string, string> verify, Ledger ledger, Write w)
        {
            string abs = Harness303.Abs(path);
            string problem = !File.Exists(abs) ? "is missing" : verify?.Invoke(File.ReadAllText(abs));
            if (problem == null) return;
            if (w != null) { w.changes.Add("VERIFY FAILED: file " + problem); WriteLedger(ledger.kind, ledger); }
            throw new Failed("post-write verification: " + path + " on disk " + problem
                + (w == null ? " (nothing was written because the in-memory object already matched: memory and file disagree - reimport the asset and run again; for the boss fields 'fields-status' shows both)"
                             : " (sha " + w.shaAfter + "; ledger " + LedgerFile(ledger.kind) + " kept for revert)"));
        }
        static string GuidOf(Object o) => o == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o));
        // checks that the file references every given asset (by GUID; object slots serialize as {fileID, guid, type})
        static string References(string text, params Object[] objects)
        {
            var missing = objects.Where(o => o != null && !text.Contains(GuidOf(o))).Select(o => o.name + " (" + GuidOf(o) + ")").ToList();
            return missing.Count == 0 ? null : "lacks a reference to " + string.Join(", ", missing);
        }
        // ids of the boss fields as the FILE has them ("  - Id: <id>" lines of the Fields list; quoted scalars unquoted)
        static List<string> FileFieldIds(string text)
        {
            var ids = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"^\s*- Id:[ \t]*(.*?)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                string v = m.Groups[1].Value;
                if (v.Length >= 2 && v[0] == '\'' && v[v.Length - 1] == '\'') v = v.Substring(1, v.Length - 2).Replace("''", "'");
                else if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
                ids.Add(v);
            }
            return ids;
        }

        static string Revert(string key, bool force)
        {
            var l = ReadLedger(key); if (l == null) throw new Refuse("no ledger " + LedgerFile(key) + " (nothing applied, or already reverted)");
            var drift = new List<string>();
            for (int i = 0; i < l.assets.Count; i++)
            {
                Guard(l.assets[i]);
                bool created = i < l.createdHere.Count && l.createdHere[i];
                if (!created && !File.Exists(l.pristine[i])) throw new Refuse("pristine backup missing " + l.pristine[i]);
                var loaded = AssetDatabase.LoadMainAssetAtPath(l.assets[i]); if (loaded != null && EditorUtility.IsDirty(loaded)) throw new Refuse(l.assets[i] + " has unsaved in-memory changes");
                var last = l.writes.LastOrDefault(w => w != null && w.asset == l.assets[i]);
                string abs = Harness303.Abs(l.assets[i]);
                if (last != null && !string.IsNullOrEmpty(last.shaAfter) && File.Exists(abs) && Harness303.Sha(abs) != last.shaAfter) drift.Add(l.assets[i]);
            }
            if (drift.Count > 0 && !force) throw new Refuse("changed after this ledger's last write: " + string.Join(", ", drift) + " - append ' --force' to restore anyway");
            var sb = new StringBuilder(key + " (revert)\n");
            foreach (var d in drift) sb.AppendLine("  WARN forced over a later change to " + d);
            for (int i = l.assets.Count - 1; i >= 0; i--)
            {
                bool created = i < l.createdHere.Count && l.createdHere[i];
                if (created) { bool ok = AssetDatabase.DeleteAsset(l.assets[i]); sb.AppendLine("  deleted " + l.assets[i] + (ok ? "" : " (WARN delete failed / already gone)")); continue; }
                string abs = Harness303.Abs(l.assets[i]); File.Copy(l.pristine[i], abs, true); AssetDatabase.ImportAsset(l.assets[i], ImportAssetOptions.ForceUpdate);
                sb.AppendLine("  " + l.assets[i] + " <- " + l.pristine[i] + (Harness303.Sha(abs) == l.pristineSha[i] ? " (bytes identical to the pristine backup)" : " (WARN sha differs after import)"));
            }
            string archived = LedgerFile(key) + ".reverted-" + Harness303.UtcStamp(); File.Move(LedgerFile(key), archived);
            return sb.Append("  ledger archived " + archived).ToString();
        }

        static string Report(string title, string key, List<Write> writes, List<string> notes)
        {
            var sb = new StringBuilder(title + "\n");
            var real = writes.Where(w => w != null).ToList();
            if (real.Count == 0) sb.AppendLine("  변경 없음 (nothing written)");
            foreach (var w in real) { sb.AppendLine("  " + (w.created ? "created " : "wrote ") + w.asset + (string.IsNullOrEmpty(w.backup) ? "" : " (backup " + w.backup + ")")); foreach (var ch in w.changes) sb.AppendLine("    " + ch); }
            foreach (var n in notes) sb.AppendLine("  note " + n);
            sb.AppendLine("  ledger " + LedgerFile(key) + (File.Exists(LedgerFile(key)) ? "" : " (none)"));
            Directory.CreateDirectory(Out); File.WriteAllText(Path.Combine(Out, key + "-last.txt"), sb.ToString());
            return sb.ToString();
        }

        // ---------- assets ----------

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/'), leaf = Path.GetFileName(folder);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, leaf);
        }
        public static AnimationClip SeatedClip() => AssetDatabase.LoadAllAssetsAtPath(SeatedClipPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == SeatedClipName);

        static string Assets()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) throw new Refuse("missing " + ShaderPath + " - copy Tools/Unity/Stage308_vehicle/_ProjectAssets/Shaders/VehicleInkShell308.shader there and refresh first");
            if (ShaderUtil.ShaderHasError(shader)) throw new Refuse(ShaderPath + " has compile errors - fix before making the material");
            foreach (var p in new[] { ShellMaterialPath, ProfilePath, FieldsPath }) Guard(p);
            var ledger = Open("vehicle308_assets"); var writes = new List<Write>(); var notes = new List<string>(); string backup = BackupDir();
            // material
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ShellMaterialPath);
            if (mat == null) { mat = new Material(shader) { name = "M_VehicleInkShell308" }; AssetDatabase.CreateAsset(mat, ShellMaterialPath); writes.Add(Created(mat, ledger, "material on " + ShaderName)); }
            else writes.Add(Edit(mat, backup, ledger, () => { var ch = new List<string>(); if (mat.shader != shader) { ch.Add("shader " + mat.shader.name + " -> " + ShaderName); mat.shader = shader; } return ch; },
                text => References(text, shader)));
            // profile
            EnsureFolder(ResourcesFolder);
            var dust = AssetDatabase.LoadAssetAtPath<Material>(DustMaterialPath);
            if (dust == null) notes.Add("WARN " + DustMaterialPath + " missing: ink dust stays off (InkParticleMaterial empty)");
            var clip = SeatedClip();
            if (clip == null) notes.Add("WARN seated clip " + SeatedClipName + " not found in " + SeatedClipPath + ": the rider uses the procedural pose");
            else if (!clip.isHumanMotion) notes.Add("WARN " + SeatedClipName + " is not a humanoid clip: ride-layer-apply will refuse it");
            var stroke = AssetDatabase.LoadAssetAtPath<Material>(CallStrokeMaterialPath);
            if (stroke == null) notes.Add("WARN " + CallStrokeMaterialPath + " missing: the call stroke ribbons copy the " + InkStrokeShaderName + " shader (Shader.Find)");
            else if (stroke.shader == null || stroke.shader.name != InkStrokeShaderName) { notes.Add("WARN " + CallStrokeMaterialPath + " is not on " + InkStrokeShaderName + " (" + (stroke.shader != null ? stroke.shader.name : "no shader") + "): not assigned"); stroke = null; }
            var profile = AssetDatabase.LoadAssetAtPath<VehicleUx308ProfileSO>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VehicleUx308ProfileSO>(); profile.name = "VehicleUx308Profile";
                profile.ShellMaterial = mat; profile.InkParticleMaterial = dust; profile.SeatedClip = clip; profile.CallStrokeMaterial = stroke;
                AssetDatabase.CreateAsset(profile, ProfilePath); writes.Add(Created(profile, ledger, "profile (class TEST defaults + ShellMaterial, M_InkDust, " + (clip != null ? clip.name : "no clip") + ", call stroke " + (stroke != null ? stroke.name : "shader fallback") + ")"));
            }
            else writes.Add(Edit(profile, backup, ledger, () =>
            {
                // fill empty slots only; a value someone set by hand is kept and reported
                var ch = new List<string>();
                if (profile.ShellMaterial == null) { profile.ShellMaterial = mat; ch.Add("ShellMaterial <- " + ShellMaterialPath); } else if (profile.ShellMaterial != mat) notes.Add("ShellMaterial kept: " + AssetDatabase.GetAssetPath(profile.ShellMaterial));
                if (profile.InkParticleMaterial == null && dust != null) { profile.InkParticleMaterial = dust; ch.Add("InkParticleMaterial <- " + DustMaterialPath); }
                if (profile.SeatedClip == null && clip != null) { profile.SeatedClip = clip; ch.Add("SeatedClip <- " + SeatedClipName); }
                if (profile.CallStrokeMaterial == null && stroke != null) { profile.CallStrokeMaterial = stroke; ch.Add("CallStrokeMaterial <- " + CallStrokeMaterialPath + " (D308-8c)"); }
                else if (profile.CallStrokeMaterial != null && profile.CallStrokeMaterial != stroke) notes.Add("CallStrokeMaterial kept: " + AssetDatabase.GetAssetPath(profile.CallStrokeMaterial));
                return ch;
            }, text => References(text, profile.ShellMaterial, profile.InkParticleMaterial, profile.SeatedClip, profile.CallStrokeMaterial)));
            var fields = AssetDatabase.LoadAssetAtPath<VehicleBossFieldSO>(FieldsPath);
            if (fields == null)
            {
                fields = ScriptableObject.CreateInstance<VehicleBossFieldSO>(); fields.name = "VehicleBossFields308";
                AssetDatabase.CreateAsset(fields, FieldsPath); writes.Add(Created(fields, ledger, "boss fields (empty: run fields-derive:<scene>)"));
            }
            if (writes.Any(w => w != null)) WriteLedger("vehicle308_assets", ledger);
            notes.Add("runtime lookup: Resources.Load(\"" + VehicleUx308ProfileSO.ResourcesPath + "\") / (\"" + VehicleUx308ProfileSO.BossFieldsResourcesPath + "\") — no scene wiring");
            return Report("Vehicle308 assets", "vehicle308_assets", writes, notes);
        }

        // ---------- boss fields ----------

        static string FieldsDerive(string arg)
        {
            float margin = DefaultMargin;
            int sp = arg.IndexOf(" margin=", StringComparison.Ordinal);
            if (sp >= 0) { if (!float.TryParse(arg.Substring(sp + 8).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out margin) || margin < 0f) throw new Refuse("bad margin"); arg = arg.Substring(0, sp).Trim(); }
            string path = arg.Replace('\\', '/');
            Guard(path);
            if (!Scenes.Contains(path)) throw new Refuse("not a #308 target scene: " + path + " (allowed: " + string.Join(", ", Scenes) + ")");
            // existence only: no reference is kept across the scene open below (it unloads unused assets — the #308 no-op bug)
            if (AssetDatabase.LoadAssetAtPath<VehicleBossFieldSO>(FieldsPath) == null) throw new Refuse("missing " + FieldsPath + " - run assets first");
            for (int i = 0; i < SceneManager.sceneCount; i++) { var open = SceneManager.GetSceneAt(i); if (open.isDirty) throw new Refuse("scene " + open.path + " has unsaved changes - save or discard them first"); }
            var active = SceneManager.GetActiveScene();
            var scene = active.path == path && SceneManager.sceneCount == 1 ? active : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            // loaded AFTER the scene open, and nothing opens a scene again before the edit below
            var fields = AssetDatabase.LoadAssetAtPath<VehicleBossFieldSO>(FieldsPath) ?? throw new Failed(FieldsPath + " could not be loaded after opening " + path);
            var sessions = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).ToArray();
            if (sessions.Length != 1) throw new Refuse(path + " has " + sessions.Length + " WorldMacroPlaytestSession components (expected 1)");
            var s = sessions[0]; if (s.Content == null) throw new Refuse("session has no content");
            var found = new List<(string id, Vector3 feet, float leash, string why)>(); var notes = new List<string>();
            foreach (var actor in s.Actors ?? Array.Empty<Oheangbu.App.Prologue.PrologueEncounter>())
            {
                if (actor == null || string.IsNullOrEmpty(actor.Id)) continue;
                var vitals = actor.GetComponent<EnemyVitals>();
                bool boss = vitals != null && vitals.IsBoss, named = NamedBosses.Contains(actor.Id);
                var spec = s.Content.Encounters?.FirstOrDefault(e => e != null && e.Id == actor.Id);
                if (actor.Id == "demo_growth_lesson") notes.Add("growth lesson demo_growth_lesson: IsBoss " + boss + (boss ? " -> boss field" : " -> not a boss arena, no field"));
                if (!boss && !named) continue;
                if (spec == null) { notes.Add("WARN " + actor.Id + " has no content Encounter: skipped"); continue; }
                found.Add((actor.Id, spec.Feet, spec.Leash, (boss ? "IsBoss" : "") + (boss && named ? "+" : "") + (named ? "named" : "")));
            }
            foreach (var n in NamedBosses) if (!found.Any(f => f.id == n)) notes.Add("named boss " + n + " not in this scene's actors (kept as is in the asset)");
            var ledger = Open("vehicle308_fields"); var writes = new List<Write>(); var skipped = new List<string>();
            string key = Path.GetFileNameWithoutExtension(path);
            writes.Add(Edit(fields, BackupDir(), ledger, () =>
            {
                var ch = new List<string>(); var list = (fields.Fields ?? Array.Empty<VehicleBossFieldSO.Field>()).ToList();
                foreach (var f in found)
                {
                    var entry = list.FirstOrDefault(x => x != null && x.Id == f.id);
                    float radius = Mathf.Max(1f, f.leash + margin);
                    string source = key + " encounter " + f.id + " (" + f.why + ") leash " + F(f.leash, "F0") + " + margin " + F(margin, "F0");
                    if (entry == null) { entry = new VehicleBossFieldSO.Field { Id = f.id, EncounterId = f.id, Centre = f.feet, Radius = radius, Source = source }; list.Add(entry); ch.Add("+ " + f.id + " r " + F(radius, "F0") + " at " + Harness303.V(f.feet)); continue; }
                    // #308 review: the three scenes are merged by priority #296 < #298 < W_Demo_Main. A lower-priority scene never
                    // overwrites an entry derived from a higher one, so re-running the whole sequence writes nothing (idempotent union).
                    if (ScenePriority(entry.Source) > ScenePriority(key)) { skipped.Add(f.id + " kept from " + entry.Source); continue; }
                    if (entry.EncounterId != f.id) { ch.Add(f.id + " EncounterId '" + entry.EncounterId + "' -> '" + f.id + "'"); entry.EncounterId = f.id; }
                    if ((entry.Centre - f.feet).sqrMagnitude > 1e-4f) { ch.Add(f.id + " centre " + Harness303.V(entry.Centre) + " -> " + Harness303.V(f.feet)); entry.Centre = f.feet; }
                    if (Mathf.Abs(entry.Radius - radius) > 1e-3f) { ch.Add(f.id + " radius " + F(entry.Radius, "F1") + " -> " + F(radius, "F1")); entry.Radius = radius; }
                    if (entry.Source != source) entry.Source = source;
                }
                fields.Fields = list.ToArray(); return ch;
            }, text =>
            {
                // the file must hold every id derived from this scene (added, updated or kept by priority) and as many entries as memory
                var ids = FileFieldIds(text);
                var missing = found.Select(f => f.id).Where(id => !ids.Contains(id)).ToList();
                int memory = fields.Fields?.Length ?? 0;
                if (missing.Count > 0) return "lacks derived field id(s) " + string.Join(", ", missing) + " (file has " + ids.Count + ": " + string.Join(", ", ids) + ")";
                if (ids.Count != memory) return "has " + ids.Count + " fields, memory " + memory;
                return null;
            }));
            if (writes.Any(w => w != null)) WriteLedger("vehicle308_fields", ledger);
            foreach (var k in skipped) notes.Add("priority: " + k);
            notes.Add("scene opened read-only (never saved): " + path + "; fields now " + (fields.Fields?.Length ?? 0) + " (file verified: " + FileFieldIds(File.ReadAllText(Harness303.Abs(FieldsPath))).Count + " ids, every derived id present)");
            foreach (var f in fields.Fields ?? Array.Empty<VehicleBossFieldSO.Field>()) notes.Add("  field " + f.Id + (f.Enabled ? "" : " (disabled)") + " r " + F(f.Radius, "F0") + " centre " + Harness303.V(f.Centre) + (f.OpenAfterDefeat ? " opens after defeat" : "") + " | " + f.Source);
            return Report("Vehicle308 fields-derive " + key, "vehicle308_fields", writes, notes);
        }

        // index in Scenes (#296 0, #298 1, W_Demo_Main 2) of the scene a Source string / scene key starts with; -1 = hand-made / unknown
        static int ScenePriority(string sourceOrKey)
        {
            if (string.IsNullOrEmpty(sourceOrKey)) return -1;
            for (int i = Scenes.Length - 1; i >= 0; i--)
            {
                string k = Path.GetFileNameWithoutExtension(Scenes[i]);
                if (sourceOrKey == k || sourceOrKey.StartsWith(k + " ", StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        // ---------- ride layer ----------

        static AnimatorController ActiveController(out string detail)
        {
            var appearances = Object.FindObjectsByType<WorldMacroPlayerAppearance>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var controllers = new List<AnimatorController>();
            foreach (var a in appearances)
            {
                var rac = a.Profile != null ? a.Profile.Controller : (a.Animator != null ? a.Animator.runtimeAnimatorController : null);
                if (rac is AnimatorOverrideController o) rac = o.runtimeAnimatorController;
                if (rac is AnimatorController ac && !controllers.Contains(ac)) controllers.Add(ac);
            }
            detail = appearances.Length + " appearance(s) in " + SceneManager.GetActiveScene().path + ", controller(s) " + string.Join(", ", controllers.Select(AssetDatabase.GetAssetPath));
            if (controllers.Count != 1) throw new Refuse("expected exactly one player AnimatorController in the active scene: " + detail);
            return controllers[0];
        }

        public static bool RideLayerReady(AnimatorController controller, VehicleUx308ProfileSO profile, AnimationClip clip, out string why)
        {
            why = null;
            var layers = controller.layers; int index = Array.FindIndex(layers, l => l.name == profile.RideLayer);
            if (index < 0) { why = "no layer " + profile.RideLayer; return false; }
            var layer = layers[index];
            if (index != layers.Length - 1) why = "layer is not last (" + index + "/" + (layers.Length - 1) + ")";
            else if (layer.blendingMode != AnimatorLayerBlendingMode.Override || layer.defaultWeight != 0f || layer.avatarMask != null) why = "layer blending / weight / mask differ";
            else
            {
                var state = layer.stateMachine.states.Select(x => x.state).FirstOrDefault(x => x.name == profile.RideState);
                if (state == null) why = "no state " + profile.RideState;
                else if (state.motion != clip) why = "state motion " + (state.motion != null ? state.motion.name : "none") + " != " + (clip != null ? clip.name : "none");
                else if (!state.speedParameterActive || state.speedParameter != profile.RideSpeedParameter) why = "speed parameter not bound";
                else if (!controller.parameters.Any(p => p.name == profile.RideSpeedParameter && p.type == AnimatorControllerParameterType.Float)) why = "no float parameter " + profile.RideSpeedParameter;
            }
            return why == null;
        }

        static string RideLayerApply()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VehicleUx308ProfileSO>(ProfilePath) ?? throw new Refuse("missing " + ProfilePath + " - run assets first");
            var clip = profile.SeatedClip != null ? profile.SeatedClip : SeatedClip();
            if (clip == null) throw new Refuse("no seated clip (profile.SeatedClip empty and " + SeatedClipName + " not found)");
            if (!clip.isHumanMotion) throw new Refuse(clip.name + " is not a humanoid clip");
            var controller = ActiveController(out string detail);
            string path = AssetDatabase.GetAssetPath(controller); Guard(path);
            var notes = new List<string> { detail, "clip " + AssetDatabase.GetAssetPath(clip) + " :: " + clip.name + " " + F(clip.length) + " s" };
            var writes = new List<Write>();
            if (RideLayerReady(controller, profile, clip, out string why)) return Report("Vehicle308 ride-layer-apply", "vehicle308_ridelayer", writes, notes);
            notes.Add("rebuild: " + why);
            var ledger = Open("vehicle308_ridelayer");
            writes.Add(Edit(controller, BackupDir(), ledger, () =>
            {
                var ch = new List<string>();
                int existing = Array.FindIndex(controller.layers, l => l.name == profile.RideLayer);
                if (existing >= 0) { controller.RemoveLayer(existing); ch.Add("removed old layer " + profile.RideLayer); }
                if (!controller.parameters.Any(p => p.name == profile.RideSpeedParameter))
                { controller.AddParameter(new AnimatorControllerParameter { name = profile.RideSpeedParameter, type = AnimatorControllerParameterType.Float, defaultFloat = 0f }); ch.Add("parameter " + profile.RideSpeedParameter + " (float 0)"); }
                var sm = new AnimatorStateMachine { name = profile.RideLayer, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(sm, controller);
                var empty = sm.AddState("Empty"); empty.writeDefaultValues = false; sm.defaultState = empty;
                var seated = sm.AddState(profile.RideState, new Vector3(260, 60, 0));
                seated.motion = clip; seated.writeDefaultValues = false; seated.speedParameterActive = true; seated.speedParameter = profile.RideSpeedParameter;
                controller.AddLayer(new AnimatorControllerLayer { name = profile.RideLayer, stateMachine = sm, defaultWeight = 0f, blendingMode = AnimatorLayerBlendingMode.Override, iKPass = false });
                ch.Add("layer " + profile.RideLayer + " (last, override, weight 0): Empty + " + profile.RideState + " = " + clip.name + " x " + profile.RideSpeedParameter);
                return ch;
            }, text =>
            {
                var lacks = new[] { profile.RideLayer, profile.RideState, profile.RideSpeedParameter }.Where(n => !text.Contains("m_Name: " + n)).ToList();
                return lacks.Count > 0 ? "lacks 'm_Name: " + string.Join("', 'm_Name: ", lacks) + "'" : References(text, clip);
            }));
            if (writes.Any(w => w != null)) WriteLedger("vehicle308_ridelayer", ledger);
            return Report("Vehicle308 ride-layer-apply", "vehicle308_ridelayer", writes, notes);
        }

        // ---------- status / data checks (read-only) ----------

        static string Status()
        {
            var sb = new StringBuilder("Vehicle308 status (play=" + EditorApplication.isPlaying + ")\n");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            sb.AppendLine("  shader " + ShaderPath + ": " + (shader == null ? "MISSING" : ShaderUtil.ShaderHasError(shader) ? "HAS ERRORS" : "ok"));
            sb.AppendLine("  material " + ShellMaterialPath + ": " + (AssetDatabase.LoadAssetAtPath<Material>(ShellMaterialPath) != null ? "ok" : "missing"));
            var profile = AssetDatabase.LoadAssetAtPath<VehicleUx308ProfileSO>(ProfilePath);
            sb.AppendLine("  profile " + ProfilePath + ": " + (profile == null ? "missing" : "ShellMaterial " + (profile.ShellMaterial != null) + ", dust " + (profile.InkParticleMaterial != null) + ", clip " + (profile.SeatedClip != null ? profile.SeatedClip.name : "none") + ", G recall within " + F(profile.RecallToggleDistance, "F0") + " m"));
            var fields = AssetDatabase.LoadAssetAtPath<VehicleBossFieldSO>(FieldsPath);
            sb.AppendLine("  boss fields " + FieldsPath + ": " + (fields == null ? "missing" : (fields.Fields?.Length ?? 0) + " (" + string.Join(", ", (fields.Fields ?? Array.Empty<VehicleBossFieldSO.Field>()).Select(f => f.Id + (f.Enabled ? "" : "-off"))) + ")"));
            foreach (var key in new[] { "vehicle308_assets", "vehicle308_fields", "vehicle308_ridelayer" }) sb.AppendLine("  ledger " + key + ": " + (File.Exists(LedgerFile(key)) ? "present" : "none"));
            try { var c = ActiveController(out string d); sb.AppendLine("  ride layer: " + (profile != null && RideLayerReady(c, profile, profile.SeatedClip != null ? profile.SeatedClip : SeatedClip(), out string why) ? "ready" : "not ready") + " | " + d); }
            catch (Refuse r) { sb.AppendLine("  ride layer: " + r.Message); }
            var active = SceneManager.GetActiveScene(); sb.Append("  active scene " + active.path + (active.isDirty ? " (dirty)" : ""));
            return sb.ToString();
        }

        // read-only (replaces the temporary 'fields-probe'): the boss fields as memory has them vs as the FILE has them
        static string FieldsStatus()
        {
            var sb = new StringBuilder("Vehicle308 fields-status (read-only, play=" + EditorApplication.isPlaying + ")\n");
            string abs = Harness303.Abs(FieldsPath);
            var fields = AssetDatabase.LoadAssetAtPath<VehicleBossFieldSO>(FieldsPath);
            if (fields == null) return sb.Append("  missing " + FieldsPath + (File.Exists(abs) ? " (file exists but does not load)" : "")).ToString();
            var memory = (fields.Fields ?? Array.Empty<VehicleBossFieldSO.Field>()).Select(f => f == null ? "(null)" : f.Id).ToList();
            var file = File.Exists(abs) ? FileFieldIds(File.ReadAllText(abs)) : new List<string>();
            sb.AppendLine("  memory " + memory.Count + " [" + string.Join(", ", memory) + "]" + (EditorUtility.IsDirty(fields) ? " (DIRTY: unsaved in-memory change)" : ""));
            sb.AppendLine("  file   " + file.Count + " [" + string.Join(", ", file) + "]" + (File.Exists(abs) ? " sha " + Harness303.Sha(abs) : " (file missing)"));
            sb.Append("  " + (memory.SequenceEqual(file) ? "agree" : "DIVERGED: memory and file differ - an unsaved edit, or an edit that never reached the file; reimport " + FieldsPath + " (or run fields-derive with the scene already open) and compare again"));
            return sb.ToString();
        }

        static string ChecksData()
        {
            var lines = new List<string> { "#308 Vehicle308 checks:data " + DateTime.Now.ToString("s") + " (read-only; Spec SPEC-VEHICLE-UX-308 AC-V0, AC-V2a data part, AC-V5a data part)" };
            int fails = 0;
            void C(bool ok, string what) { if (!ok) fails++; lines.Add((ok ? "PASS " : "FAIL ") + what); }
            var profile = AssetDatabase.LoadAssetAtPath<VehicleUx308ProfileSO>(ProfilePath);
            C(profile != null, "profile " + ProfilePath);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            C(shader != null && !ShaderUtil.ShaderHasError(shader), "shader " + ShaderPath + " present without errors");
            if (shader != null)
            {
                var names = Enumerable.Range(0, shader.GetPropertyCount()).Select(i => shader.GetPropertyName(i)).ToArray();
                C(!names.Any(n => n.IndexOf("Emission", StringComparison.OrdinalIgnoreCase) >= 0), "AC-V5 shell shader has no emission property (" + string.Join(",", names) + ")");
                C(shader.renderQueue >= 2501, "AC-V5 shell shader in the transparent queue (" + shader.renderQueue + ")");
                // first use of the shell in Play is its first frame: async editor compilation would draw the cyan placeholder
                // (no clip -> the whole car mesh as a translucent cyan polygon; #308 summon_gather.png). Builds are unaffected.
                string source = System.IO.File.Exists(ShaderPath) ? System.IO.File.ReadAllText(ShaderPath) : "";
                C(source.Split('\n').Any(l => l.Trim().StartsWith("#pragma editor_sync_compilation", StringComparison.Ordinal)),"AC-V5 shell shader compiles synchronously in the editor (no cyan placeholder on its first frame)");
            }
            if (profile != null)
            {
                C(profile.ShellMaterial != null && profile.ShellMaterial.shader != null && profile.ShellMaterial.shader.name == ShaderName, "ShellMaterial uses " + ShaderName);
                C(profile.InkParticleMaterial != null, "ink dust material set (" + (profile.InkParticleMaterial != null ? AssetDatabase.GetAssetPath(profile.InkParticleMaterial) : "none") + ")");
                float maxInk = Mathf.Max(profile.InkColour.r, profile.InkColour.g, profile.InkColour.b, profile.EdgeColour.r, profile.EdgeColour.g, profile.EdgeColour.b);
                C(maxInk <= .85f, "AC-V5 ink colours LDR <= .85 (max " + F(maxInk, "F3") + ")");
                float summon = profile.GatherSeconds + profile.HoldSeconds + profile.RecedeSeconds, recall = profile.CoverSeconds + profile.ScatterSeconds;
                C(summon >= .5f && summon <= 1.6f && recall >= .5f && recall <= 1.6f, "AC-V5 timings summon " + F(summon) + " s, recall " + F(recall) + " s within [0.5, 1.6] (TEST)");
                C(profile.SeatedClip == null || profile.SeatedClip.isHumanMotion, "seated clip slot is humanoid (" + (profile.SeatedClip != null ? profile.SeatedClip.name : "empty -> procedural pose") + ")");
                C(!string.IsNullOrEmpty(profile.BoardPrompt) && !string.IsNullOrEmpty(profile.ExitPrompt) && profile.BoardPrompt.Length <= 6 && profile.ExitPrompt.Length <= 6, "AC-V4 prompt words short (\"" + profile.BoardPrompt + "\", \"" + profile.ExitPrompt + "\")");
                // D308-8c call stroke (data part of AC-V9)
                C(profile.CallStroke, "AC-V9 call stroke on (CallStroke " + profile.CallStroke + "; false = legacy pendant)");
                C(profile.CallStrokeSeconds >= .9f && profile.CallStrokeSeconds <= 1.2f, "AC-V9 stroke gesture " + F(profile.CallStrokeSeconds) + " s within [0.9, 1.2] (TEST) — the action gate is held this long");
                float raise = Mathf.Clamp(profile.StrokeRaiseEnd, .05f, .9f), contact = Mathf.Clamp(profile.StrokeContactEnd, raise + .05f, .98f);
                C(raise == profile.StrokeRaiseEnd && contact == profile.StrokeContactEnd && contact - raise >= .15f, "AC-V9 phases raise " + F(profile.StrokeRaiseEnd) + " < contact end " + F(profile.StrokeContactEnd) + " (stroke share " + F(contact - raise) + " >= .15, no clamping)");
                C(profile.StrokeEase == null || profile.StrokeEase.length == 0 || (Mathf.Abs(profile.StrokeEase.Evaluate(0f)) < .02f && Mathf.Abs(profile.StrokeEase.Evaluate(1f) - 1f) < .02f), "AC-V9 stroke ease runs 0 -> 1");
                C((profile.StrokeTo - profile.StrokeFrom).magnitude >= .5f, "AC-V9 stroke path length " + F((profile.StrokeTo - profile.StrokeFrom).magnitude) + " plane units >= .5");
                C(profile.StrokeFrom.x >= 0f && profile.StrokeTo.x >= 0f && Mathf.Min(profile.StrokeFrom.y, profile.StrokeTo.y) >= .4f, "AC-V9 stroke on the upper right of the drawing plane (x >= 0, y >= .4: visible over the right shoulder in the shoulder view; capture is the judge)");
                C(profile.CallStrokeMaterial == null || (profile.CallStrokeMaterial.shader != null && profile.CallStrokeMaterial.shader.name == InkStrokeShaderName), "AC-V9 call stroke material on " + InkStrokeShaderName + " (" + (profile.CallStrokeMaterial != null ? AssetDatabase.GetAssetPath(profile.CallStrokeMaterial) : "empty -> shader copy") + "; flash add forced 0 at run time)");
                C(profile.StrokeBodyTurnSpeed * raise * profile.CallStrokeSeconds >= 90f, "AC-V9 the body can turn 90° (standing look-around lag) to the call direction before the brush touches the air (" + F(profile.StrokeBodyTurnSpeed) + " deg/s x raise " + F(raise * profile.CallStrokeSeconds) + " s)");
                float flowLead = profile.FlowSeconds * profile.GatherAfterFlow;
                C(profile.FlowSeconds <= .6f && flowLead <= profile.FlowSeconds, "AC-V9 ink flow " + F(profile.FlowSeconds) + " s, gather/cover starts after " + F(flowLead) + " s (hand-over)");
                // not judged: which rule opens the action gate when the stroke ends (user decision pending, SPEC-VEHICLE-UX-308 §3b)
                lines.Add("INFO AC-V9 stroke gate release: " + (profile.StrokeGateWaitsForNeutral ? "waits for held keys to return to neutral (SPEC-VEHICLE-CALL rule; a held move key outlasts the stroke)" : "at once when the stroke ends (lock = the stroke; death / unfocused still wait)"));
            }
            var fields = AssetDatabase.LoadAssetAtPath<VehicleBossFieldSO>(FieldsPath);
            C(fields != null && fields.Fields != null && fields.Fields.Length > 0, "boss fields present (" + (fields?.Fields?.Length ?? 0) + ")");
            if (fields != null && fields.Fields != null)
            {
                foreach (var n in NamedBosses) C(fields.Fields.Any(f => f != null && f.Id == n && f.Enabled), "AC-V1 named boss field " + n);
                for (int i = 0; i < Contents.Length; i++)
                {
                    var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(Contents[i]);
                    if (content == null) { lines.Add("INFO content missing " + Contents[i]); continue; }
                    string key = Path.GetFileNameWithoutExtension(Contents[i]); if (key.Length > 24) key = key.Substring(key.Length - 24);
                    foreach (var f in fields.Fields)
                    {
                        if (f == null || !f.Enabled) continue;
                        var spec = string.IsNullOrEmpty(f.EncounterId) ? null : content.Encounters?.FirstOrDefault(e => e != null && e.Id == f.EncounterId);
                        Vector3 centre = spec != null ? spec.Feet : f.Centre;
                        foreach (var id in EscortStops)
                        {
                            var p = content.Points?.FirstOrDefault(x => x != null && x.Id == id); if (p == null) continue;
                            C(!VehicleBossFieldSO.Contains(f, centre, p.Position, 4f), "AC-V1 escort stop " + id + " outside field " + f.Id + " [" + key + "]");
                        }
                        foreach (var cp in content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>())
                            if (cp != null && VehicleBossFieldSO.Contains(f, centre, cp.Feet, 0f)) lines.Add("WARN rest checkpoint " + cp.Id + " lies in boss field " + f.Id + " [" + key + "] (summoning refused there)");
                    }
                }
            }
            lines.Add("RESULT " + (fails == 0 ? "PASS" : "FAIL") + " (fails " + fails + ")");
            Directory.CreateDirectory(Out); File.WriteAllText(Path.Combine(Out, "checks-data.txt"), string.Join("\n", lines), new UTF8Encoding(false));
            return string.Join("\n", lines.Skip(Math.Max(0, lines.Count - 40)));
        }
    }
}
