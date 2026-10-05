using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 player juice, 3rd revision (SPEC-ANIM-JUICE-308 "3차 개정", DECISIONS D308-24 answers 13 14 15 16 23 24).
    //   · The main profile asset is made FROM DATA: Art/Characters308/Juice/Data/PlayerJuice308Main.json lists every serialized
    //     field of PlayerJuice308ProfileSO (path, value, where the value comes from). A field the file does not list, a path the
    //     class does not have, a value that does not parse: every profile command refuses before it writes anything.
    //       profile:plan      dry: every field, its value, its source, and what an existing asset has instead
    //       profile:create    the asset does not exist: one new asset, saved alone (SaveAssetIfDirty - never SaveAssets), then
    //                         the saved FILE's text is read back and compared with the data (the loaded object alone would be
    //                         the instance that was just filled)
    //       profile:apply     the asset exists: its values are recorded (Art/Characters308/Juice/Records), then set from the data
    //       profile:check     rows D1 - D3 alone (the asset must exist; D3 = the file on disk AND the loaded asset equal the data)
    //       profile:revert:<record>   put a record's values back (the name profile:apply printed)
    //       profile:remove    the asset and its .meta are copied to the records folder, then the asset is deleted
    //   · New rows of "check": D1 - D3 (data = the class's fields / the user's answers / the asset), Y1 - Y2 (a parry cast gets no
    //     camera kick, and nothing else of it changes), F1 - F6 (the lock-on reticle follows the camera reaction: a bare
    //     HudController, the real camera owner, real renders in a preview scene), W3 (the open scene's wiring, read only).
    //   · "parry-identity": the cloned rig driven through the same take as a parry cast and as another cast.
    //   · JudgeWall: the reference wall of cam:judge takes its line widths (pixels of the capture) from data.
    // Queue paths never open a dialog. Nothing here saves a scene. Every fixture object is HideAndDontSave in a preview scene.
    public static partial class Juice308Build
    {
        const string ProfileDataRelative = "Art/Characters308/Juice/Data/PlayerJuice308Main.json";
        const string JudgeWallRelative = "Art/Characters308/Juice/Data/JudgeWall308.json";
        const string RecordsRelative = "Art/Characters308/Juice/Records";
        const string ProfileSchema = "308.juice.profile.1", ProfileRecordSchema = "308.juice.profile.record.1", JudgeWallSchema = "308.juice.judgewall.1";
        const float JudgeMinLinePx = 2.5f;   // AC-J18: the acceptance floor of a wall line (a check value, not a tuning number)
        const int FollowWidth = 160, FollowHeight = 90;   // the render target of rows F1 - F6 (see CheckMarkFollow)

        // DECISIONS D308-24 (2026-10-06): the user's answers as the acceptance values of row D2. These are not tuning numbers -
        // the tuning lives in the data file. A later answer changes the data AND this table, with its own decision record.
        static readonly (string path, string value, string answer)[] Decisions308 =
        {
            ("Camera.Enabled", "true", "D308-20 answer 4"),
            ("CastKick.Enabled", "true", "13"), ("CastKick.PitchDegrees", "0.9", "13"), ("CastKick.FovDegrees", "0.8", "13 / 16 (wider)"),
            ("CallSettle.Enabled", "true", "14"), ("CallSettle.PitchDegrees", "0.5", "14"),
            ("SetDownThump.Enabled", "true", "14"), ("SetDownThump.PitchDegrees", "0.7", "14"), ("SetDownThump.FovDegrees", "0.4", "14 / 16 (wider)"),
            ("StrokeStart.Enabled", "true", "15"), ("StrokeEnd.Enabled", "true", "15"),
            ("Camera.MarksFollow", "true", "23"), ("CastKick.OnParryCasts", "false", "24"),
        };

        // <profile-pure>  (the text between these marker pairs is also compiled and RUN outside the editor: checks/tool_model.py)
        [Serializable] sealed class ProfileRow { public string path, value, from, note; }
        [Serializable] sealed class ProfileData { public string schema, asset, decisions; public ProfileRow[] rows; }
        [Serializable] sealed class JudgeWallData { public string schema; public int[] tile; public float minorLinePx, majorLinePx, centreLinePx; public int minorEveryDegrees, majorEveryDegrees; }

        struct ProfileField { public string Path; public FieldInfo Group, Field; }   // Group null = a field of the asset itself

        sealed class ProfileSheet
        {
            public string File, Sha = "", Issue; public ProfileData Data; public List<ProfileField> Fields = new List<ProfileField>();
            public readonly Dictionary<string, ProfileRow> Rows = new Dictionary<string, ProfileRow>(StringComparer.Ordinal);
            public bool Ok => Issue == null;
            public string Sha16 => Sha.Length >= 16 ? Sha.Substring(0, 16) : Sha;
        }

        // ---------------------------------------------------------------- the profile's fields, by reflection

        static bool Scalar(Type t) => t == typeof(float) || t == typeof(bool) || t == typeof(int);

        // every serialized field of the profile: its own scalars and the scalars of its [Serializable] sets, in declaration order
        static List<ProfileField> ProfileFields(List<string> unsupported)
        {
            var list = new List<ProfileField>();
            const BindingFlags own = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
            foreach (var f in typeof(PlayerJuice308ProfileSO).GetFields(own).OrderBy(f => f.MetadataToken))
            {
                if (Scalar(f.FieldType)) { list.Add(new ProfileField { Path = f.Name, Field = f }); continue; }
                if (f.FieldType.IsClass && f.FieldType.IsSerializable && f.FieldType.DeclaringType == typeof(PlayerJuice308ProfileSO))
                {
                    foreach (var g in f.FieldType.GetFields(own).OrderBy(g => g.MetadataToken))
                    {
                        if (Scalar(g.FieldType)) list.Add(new ProfileField { Path = f.Name + "." + g.Name, Group = f, Field = g });
                        else unsupported?.Add(f.Name + "." + g.Name + " (" + g.FieldType.Name + ")");
                    }
                    continue;
                }
                unsupported?.Add(f.Name + " (" + f.FieldType.Name + ")");
            }
            return list;
        }

        static object ProfileOwner(PlayerJuice308ProfileSO profile, ProfileField f) => f.Group != null ? f.Group.GetValue(profile) : profile;
        static object ProfileGet(PlayerJuice308ProfileSO profile, ProfileField f) { object owner = ProfileOwner(profile, f); return owner != null ? f.Field.GetValue(owner) : null; }

        static bool ProfileParse(Type type, string text, out object value)
        {
            value = null; text = (text ?? "").Trim();
            if (type == typeof(bool)) { if (text == "true") { value = true; return true; } if (text == "false") { value = false; return true; } return false; }
            if (type == typeof(int)) { if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) { value = i; return true; } return false; }
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && !float.IsNaN(f) && !float.IsInfinity(f)) { value = f; return true; }
            return false;
        }

        static string ProfileText(object value)
        {
            if (value is bool b) return b ? "true" : "false";
            if (value is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // a float is the same value only when its bits are (the editor's JIT keeps float arithmetic at double precision)
        static bool ProfileSame(object a, object b)
        {
            if (a is float x && b is float y) return Bits(x) == Bits(y);
            return a != null && a.Equals(b);
        }
        // </profile-pure>

        // ---------------------------------------------------------------- the data file

        static ProfileSheet LoadProfileSheet() => LoadProfileSheet(Path.Combine(Repo, ProfileDataRelative), ProfileSchema);

        static ProfileSheet LoadProfileSheet(string file, string schema)
        {
            var sheet = new ProfileSheet { File = file }; var problems = new List<string>(); var unsupported = new List<string>();
            sheet.Fields = ProfileFields(unsupported);
            if (unsupported.Count > 0) problems.Add("the profile class has field(s) this tool cannot write: " + string.Join(", ", unsupported));
            if (!File.Exists(file)) { sheet.Issue = "missing " + file; return sheet; }
            byte[] bytes = File.ReadAllBytes(file); sheet.Sha = Hash(bytes);
            try { sheet.Data = JsonUtility.FromJson<ProfileData>(new UTF8Encoding(false).GetString(bytes).TrimStart('﻿')); }
            catch (Exception e) { sheet.Issue = "not readable as json (" + e.Message + "): " + file; return sheet; }
            var data = sheet.Data;
            if (data == null || data.rows == null) { sheet.Issue = "no rows in " + file; return sheet; }
            if (data.schema != schema) problems.Add("schema '" + data.schema + "' (expected '" + schema + "')");
            if (data.asset != ProfileAssetPath) problems.Add("it is written for '" + data.asset + "' (this tool writes " + ProfileAssetPath + ")");
            var known = new Dictionary<string, ProfileField>(StringComparer.Ordinal);
            foreach (var f in sheet.Fields) known[f.Path] = f;
            var duplicate = new List<string>(); var unknown = new List<string>(); var unparsed = new List<string>();
            foreach (var row in data.rows)
            {
                if (row == null || string.IsNullOrEmpty(row.path)) { unknown.Add("(a row without a path)"); continue; }
                if (sheet.Rows.ContainsKey(row.path)) { duplicate.Add(row.path); continue; }
                sheet.Rows[row.path] = row;
                if (!known.TryGetValue(row.path, out ProfileField field)) { unknown.Add(row.path); continue; }
                if (!ProfileParse(field.Field.FieldType, row.value, out _)) unparsed.Add(row.path + " = '" + row.value + "' (" + field.Field.FieldType.Name + ")");
            }
            var missing = sheet.Fields.Where(f => !sheet.Rows.ContainsKey(f.Path)).Select(f => f.Path).ToList();
            if (missing.Count > 0) problems.Add(missing.Count + " field(s) of the class have no row: " + string.Join(", ", missing));
            if (unknown.Count > 0) problems.Add(unknown.Count + " row(s) name no field of the class: " + string.Join(", ", unknown));
            if (duplicate.Count > 0) problems.Add(duplicate.Count + " path(s) listed twice: " + string.Join(", ", duplicate));
            if (unparsed.Count > 0) problems.Add(unparsed.Count + " value(s) do not parse: " + string.Join(", ", unparsed));
            if (problems.Count > 0) sheet.Issue = Path.GetFileName(file) + ": " + string.Join("; ", problems);
            return sheet;
        }

        // <profile-pure>
        static void ApplySheet(ProfileSheet sheet, PlayerJuice308ProfileSO profile)
        {
            foreach (var f in sheet.Fields)
            {
                ProfileParse(f.Field.FieldType, sheet.Rows[f.Path].value, out object value);
                f.Field.SetValue(ProfileOwner(profile, f), value);
            }
        }

        // "path: has X, data Y" for every field of `profile` that is not the sheet's value
        static List<string> ProfileDiff(ProfileSheet sheet, PlayerJuice308ProfileSO profile)
        {
            var diff = new List<string>();
            foreach (var f in sheet.Fields)
            {
                ProfileParse(f.Field.FieldType, sheet.Rows[f.Path].value, out object want); object has = ProfileGet(profile, f);
                if (!ProfileSame(has, want)) diff.Add(f.Path + ": " + ProfileText(has) + " (data " + ProfileText(want) + ")");
            }
            return diff;
        }

        // The asset as its FILE says it (the project serializes assets as text): "Key: value" of the one MonoBehaviour document,
        // the members of a [Serializable] set one level in ("Camera.MarksFollow"). Text only - the object the editor holds in
        // memory is not asked (AssetDatabase.LoadAssetAtPath right after CreateAsset hands back the instance that was just
        // filled, so comparing that one proves nothing about the file). null = the file is not readable this way (`issue` says why).
        static Dictionary<string, string> ProfileFileValues(string yaml, out string issue)
        {
            issue = null;
            if (yaml == null || !yaml.StartsWith("%YAML", StringComparison.Ordinal)) { issue = "it is not a text (YAML) asset"; return null; }
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string group = null; bool inside = false; int documents = 0, bodies = 0;
            foreach (string raw in yaml.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("--- ", StringComparison.Ordinal)) { documents++; inside = false; group = null; continue; }
                if (line == "MonoBehaviour:") { inside = true; bodies++; group = null; continue; }
                if (!inside || line.Length == 0) continue;
                int indent = 0; while (indent < line.Length && line[indent] == ' ') indent++;
                int colon = indent < line.Length && line[indent] != '-' ? line.IndexOf(':', indent) : -1;
                if (colon <= indent) { if (indent <= 2) group = null; continue; }   // a list item or a line without a key
                string key = line.Substring(indent, colon - indent), value = line.Substring(colon + 1).Trim();
                if (indent == 2) { group = value.Length == 0 ? key : null; if (value.Length > 0) values[key] = value; }
                else if (indent == 4 && group != null && value.Length > 0) values[group + "." + key] = value;
            }
            if (documents != 1 || bodies != 1) { issue = "it holds " + documents + " object(s), " + bodies + " of them a MonoBehaviour (one of each expected)"; return null; }
            return values;
        }

        // "path: the file has X (data Y)" for every field whose value in the asset FILE is not the sheet's value;
        // found = the fields the file lists. The file writes a bool as 0 / 1 and a float as a decimal that reads back to the same bits.
        static List<string> ProfileFileDiff(ProfileSheet sheet, Dictionary<string, string> file, out int found)
        {
            var diff = new List<string>(); found = 0;
            foreach (var f in sheet.Fields)
            {
                Type type = f.Field.FieldType;
                ProfileParse(type, sheet.Rows[f.Path].value, out object want);
                if (!file.TryGetValue(f.Path, out string text)) { diff.Add(f.Path + ": not in the file (data " + ProfileText(want) + ")"); continue; }
                found++;
                string asData = type == typeof(bool) ? (text == "1" ? "true" : text == "0" ? "false" : text) : text;
                if (!ProfileParse(type, asData, out object has) || !ProfileSame(has, want)) diff.Add(f.Path + ": the file has " + text + " (data " + ProfileText(want) + ")");
            }
            return diff;
        }
        // </profile-pure>

        // The asset file on disk against the data, as one clause. differ: fields that are not the data's value (missing ones
        // included); -1 = there is no file or it could not be read as text - that is never "equal".
        static string AssetFileAgainst(ProfileSheet sheet, out int differ, string against = "the data")
        {
            differ = -1;
            string file = Path.Combine(Repo, "Oheangbu", ProfileAssetPath);
            if (!File.Exists(file)) return "NOT READ - there is no file " + ProfileAssetPath;
            string yaml;
            try { yaml = File.ReadAllText(file); } catch (Exception e) { return "NOT READ - " + e.GetType().Name + ": " + e.Message; }
            var values = ProfileFileValues(yaml, out string issue);
            if (values == null) return "NOT READ - " + issue;
            var diff = ProfileFileDiff(sheet, values, out int found); differ = diff.Count;
            return found + " of " + sheet.Fields.Count + " fields found, " + diff.Count + " differ from " + against + (diff.Count > 0 ? " - " + string.Join("; ", diff) : "");
        }

        // a memory profile carrying the data file's values (the caller destroys it)
        static PlayerJuice308ProfileSO ProfileFromSheet(ProfileSheet sheet)
        {
            var profile = ScriptableObject.CreateInstance<PlayerJuice308ProfileSO>();
            profile.name = FixturePrefix + "_DataProfile"; profile.hideFlags = HideFlags.HideAndDontSave;
            ApplySheet(sheet, profile);
            return profile;
        }

        static string WriteProfileRecord(string name, PlayerJuice308ProfileSO profile, List<ProfileField> fields, string note)
        {
            var data = new ProfileData { schema = ProfileRecordSchema, asset = ProfileAssetPath, decisions = note,
                rows = fields.Select(f => new ProfileRow { path = f.Path, value = ProfileText(ProfileGet(profile, f)), from = "asset", note = "" }).ToArray() };
            string dir = Path.Combine(Repo, RecordsRelative); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name + ".json"), JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            return name;
        }

        static string AssetFileSha()
        {
            string file = Path.Combine(Repo, "Oheangbu", ProfileAssetPath);
            return File.Exists(file) ? Hash(File.ReadAllBytes(file)).Substring(0, 16) : "(no file)";
        }

        // ---------------------------------------------------------------- profile commands

        static string Profile308(string verb, string argument)
        {
            switch (verb)
            {
                case "": case "status": return ProfileStatus();
                case "plan": return ProfilePlan();
                case "create": return ProfileCreate308();
                case "apply": return ProfileApply308(null);
                case "revert": return string.IsNullOrEmpty(argument) ? "refused: profile:revert:<record> (the name profile:apply printed)" : ProfileApply308(argument);
                case "check": { var c = new Checks(); ProfileRows(c, true); return Finish(c, "profile_check"); }
                case "remove": return ProfileRemove308();
            }
            return "refused: profile:status | profile:plan | profile:create | profile:apply | profile:check | profile:revert:<record> | profile:remove";
        }

        static void ProfileStatus2(StringBuilder sb, PlayerJuice308ProfileSO asset, WorldMacroPlayerGestureRig rig)
        {
            if (asset != null) sb.Append("\n  3rd revision: marks follow the reaction ").Append(asset.Camera.MarksFollow).Append(" | kick on parry casts ").Append(asset.CastKick.OnParryCasts);
            var sheet = LoadProfileSheet();
            sb.Append("\ndata ").Append(ProfileDataRelative).Append(": ").Append(sheet.Ok ? sheet.Fields.Count + " rows = every field, sha256 " + sheet.Sha16 : "NOT USABLE - " + sheet.Issue);
            if (sheet.Ok && asset != null)
            {
                var diff = ProfileDiff(sheet, asset);
                sb.Append("\nasset against data: the file on disk ").Append(AssetFileAgainst(sheet, out _)).Append(" ; the loaded asset ").Append(diff.Count).Append(" field(s) differ").Append(diff.Count > 0 ? " - " + string.Join("; ", diff) : "")
                    .Append(" ; asset file sha256 ").Append(AssetFileSha());
            }
            if (rig == null || !Application.isPlaying) return;
            // Play only: the counters the Play items read before and after a take (parry casts: "renders with an offset" must not move;
            // casts while locked on: "placements with a shift" must)
            sb.Append("\nlive rig: parry tell wired ").Append(rig.JuiceParryTapWired308).Append(", commits seen as a parry cast ").Append(rig.JuiceParryCommits308).Append(", juice fault ").Append(rig.JuiceFaulted308);
            var owner = new SerializedObject(rig).FindProperty("_cameraRig")?.objectReferenceValue as CameraRigController;
            if (owner != null)
                sb.Append("\nlive camera owner: renders with an offset ").Append(owner.RenderOffsetApplied308).Append(", conflicts ").Append(owner.RenderOffsetConflicts308).Append(", refused while drawing ").Append(owner.RenderOffsetRefusedDrawing308)
                    .Append(", listener faults ").Append(owner.RenderOffsetListenerFaults308).Append(", marks follow ").Append(owner.RenderOffsetMarksFollow308).Append(", peak (nod, roll, fov) ").Append(owner.RenderOffsetPeak308.ToString("F3"));
            foreach (var hud in Object.FindObjectsByType<HudController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                sb.Append("\nlive HUD '").Append(hud.name).Append("': reticle placements with a shift ").Append(hud.ReticleFollowShifts308).Append(", placed again after a late push ").Append(hud.ReticleFollowReplaced308)
                    .Append(", shift on the reticle now ").Append(hud.ReticleFollowShift308.ToString("F2")).Append(" px");
        }

        static string ProfilePlan()
        {
            var sheet = LoadProfileSheet();
            if (!sheet.Ok) return "refused: " + sheet.Issue;
            var asset = AssetDatabase.LoadAssetAtPath<PlayerJuice308ProfileSO>(ProfileAssetPath);
            var defaults = ScriptableObject.CreateInstance<PlayerJuice308ProfileSO>(); defaults.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sb = new StringBuilder("profile:plan (nothing is written) - " + ProfileDataRelative + ", sha256 " + sheet.Sha16 + ", " + sheet.Fields.Count + " rows = every serialized field of PlayerJuice308ProfileSO");
                int decided = 0, differs = 0, assetDiffers = 0;
                foreach (var f in sheet.Fields)
                {
                    var row = sheet.Rows[f.Path]; ProfileParse(f.Field.FieldType, row.value, out object want);
                    bool offDesign = !ProfileSame(ProfileGet(defaults, f), want); bool decision = (row.from ?? "").StartsWith("D308", StringComparison.Ordinal);
                    if (decision) decided++; if (offDesign) differs++;
                    sb.Append("\n  ").Append(offDesign ? "* " : "  ").Append(f.Path).Append(" = ").Append(ProfileText(want)).Append("   [").Append(row.from).Append(decision ? "" : ", TEST").Append("]")
                        .Append(offDesign ? "  (the class's designed value: " + ProfileText(ProfileGet(defaults, f)) + ")" : "");
                    if (asset != null && !ProfileSame(ProfileGet(asset, f), want)) { assetDiffers++; sb.Append("  <- the asset has ").Append(ProfileText(ProfileGet(asset, f))); }
                }
                sb.Append("\nrows from a decision: ").Append(decided).Append(" ; rows that differ from the class's designed value (*): ").Append(differs)
                    .Append("\nasset ").Append(ProfileAssetPath).Append(": ").Append(asset == null ? "absent -> profile:create writes it" : assetDiffers == 0 ? "present and equal to the data (nothing to do)" : "present, " + assetDiffers + " field(s) differ -> profile:apply");
                return sb.ToString();
            }
            finally { Object.DestroyImmediate(defaults); }
        }

        static string ProfileCreate308()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: profile:create runs in Edit Mode";
            var sheet = LoadProfileSheet();
            if (!sheet.Ok) return "refused: " + sheet.Issue;
            if (AssetDatabase.LoadAssetAtPath<Object>(ProfileAssetPath) != null || File.Exists(Path.Combine(Repo, "Oheangbu", ProfileAssetPath)))
                return "refused: " + ProfileAssetPath + " already exists (profile:check compares it with the data, profile:apply rewrites it from the data, profile:remove takes it out)";
            string folder = Path.GetDirectoryName(ProfileAssetPath).Replace('\\', '/'), parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) return "refused: " + parent + " does not exist";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            var profile = ScriptableObject.CreateInstance<PlayerJuice308ProfileSO>();
            ApplySheet(sheet, profile);
            AssetDatabase.CreateAsset(profile, ProfileAssetPath);
            AssetDatabase.SaveAssetIfDirty(profile);   // this one asset only (never SaveAssets: other sessions' dirty assets stay theirs)
            // What was written is read back twice. First the FILE's own text: LoadAssetAtPath hands back the instance that was
            // just filled, so the loaded asset alone would only compare the memory object with the data it was filled from.
            string onDisk = AssetFileAgainst(sheet, out int fileDiffers);
            var back = AssetDatabase.LoadAssetAtPath<PlayerJuice308ProfileSO>(ProfileAssetPath);
            var diff = back != null ? ProfileDiff(sheet, back) : new List<string> { "the asset could not be loaded back" };
            string record = WriteProfileRecord("profile_create_" + Stamp(), profile, sheet.Fields, "created from " + ProfileDataRelative + " sha256 " + sheet.Sha);
            return "created " + ProfileAssetPath + " from " + ProfileDataRelative + " (sha256 " + sheet.Sha16 + "): " + sheet.Fields.Count + " fields written ; the file read back from disk: " + onDisk
                + " ; the loaded asset: " + diff.Count + " differ" + (diff.Count > 0 ? " - " + string.Join("; ", diff) : "") + (fileDiffers == 0 && diff.Count == 0 ? "" : " ; NOT EQUAL TO THE DATA")
                + " ; A-1 " + profile.StrokeStart.Enabled + ", A-2 " + profile.StrokeEnd.Enabled + ", D-1 " + F(profile.CastKick.PitchDegrees, "F2") + " / " + F(profile.CastKick.FovDegrees, "F2")
                + ", kick on parry casts " + profile.CastKick.OnParryCasts + ", marks follow " + profile.Camera.MarksFollow + ", D-3 " + profile.ThrowRoll.Enabled
                + " ; asset file sha256 " + AssetFileSha() + " ; record " + record + " ; nothing else was saved. The juice runs from the next Play.";
        }

        // record == null: the data file -> the asset. Otherwise a record of profile:apply -> the asset (revert).
        static string ProfileApply308(string record)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: profile:" + (record == null ? "apply" : "revert") + " runs in Edit Mode";
            var asset = AssetDatabase.LoadAssetAtPath<PlayerJuice308ProfileSO>(ProfileAssetPath);
            if (asset == null) return "refused: " + ProfileAssetPath + " does not exist (profile:create)";
            ProfileSheet sheet;
            if (record == null) sheet = LoadProfileSheet();
            else
            {
                if (record.IndexOfAny(new[] { '/', '\\', '.' }) >= 0) return "refused: the record is named without a folder or an extension";
                sheet = LoadProfileSheet(Path.Combine(Repo, RecordsRelative, record + ".json"), ProfileRecordSchema);
            }
            if (!sheet.Ok) return "refused: " + sheet.Issue;
            var diff = ProfileDiff(sheet, asset);
            if (diff.Count == 0) return "nothing to do: " + ProfileAssetPath + " already equals " + Path.GetFileName(sheet.File) + " (sha256 " + sheet.Sha16 + ", " + sheet.Fields.Count + " fields)";
            string before = WriteProfileRecord("profile_" + (record == null ? "apply" : "revert") + "_" + Stamp(), asset, sheet.Fields, "the asset before " + (record == null ? "profile:apply from " + ProfileDataRelative : "profile:revert:" + record) + " sha256 " + sheet.Sha);
            ApplySheet(sheet, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);   // this one asset only
            var after = ProfileDiff(sheet, asset);
            string onDisk = AssetFileAgainst(sheet, out int fileDiffers, record == null ? "the data" : "the record");   // the file's own text, not the object that was just set
            return (record == null ? "applied " + ProfileDataRelative : "reverted to record " + record) + " (sha256 " + sheet.Sha16 + ") to " + ProfileAssetPath + ": " + diff.Count + " field(s) changed - " + string.Join("; ", diff)
                + " ; after the save - the file read back from disk: " + onDisk + " ; the loaded asset: " + after.Count + " differ" + (fileDiffers == 0 && after.Count == 0 ? "" : " ; NOT EQUAL TO " + (record == null ? "THE DATA" : "THE RECORD"))
                + " ; asset file sha256 " + AssetFileSha() + " ; the values before are record " + before + " (profile:revert:" + before + ") ; nothing else was saved";
        }

        static string ProfileRemove308()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: profile:remove runs in Edit Mode";
            string file = Path.Combine(Repo, "Oheangbu", ProfileAssetPath);
            if (AssetDatabase.LoadAssetAtPath<Object>(ProfileAssetPath) == null || !File.Exists(file)) return "refused: " + ProfileAssetPath + " does not exist";
            string dir = Path.Combine(Repo, RecordsRelative, "profile_removed_" + Stamp()); Directory.CreateDirectory(dir);
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), true);
            if (File.Exists(file + ".meta")) File.Copy(file + ".meta", Path.Combine(dir, Path.GetFileName(file) + ".meta"), true);
            string sha = AssetFileSha();
            bool gone = AssetDatabase.DeleteAsset(ProfileAssetPath);
            return (gone ? "removed " : "COULD NOT remove ") + ProfileAssetPath + " (file sha256 " + sha + "); a copy with its .meta is in " + dir + ". Without the asset only the rejection fix B-1 runs. The folder Assets/_Project/Resources/Juice308 is left as it is.";
        }

        // rows D1 - D3. Returns the sheet (usable or not).
        static ProfileSheet ProfileRows(Checks c, bool assetRequired)
        {
            var sheet = LoadProfileSheet();
            c.Add("D1.profile_data_complete", sheet.Ok, sheet.Ok ? ProfileDataRelative + ": " + sheet.Data.rows.Length + " rows = the " + sheet.Fields.Count + " serialized fields of PlayerJuice308ProfileSO, 0 missing, 0 unknown, 0 twice, every value parses; sha256 " + sheet.Sha16
                : sheet.Issue);
            if (!sheet.Ok) return sheet;
            var known = sheet.Fields.ToDictionary(f => f.Path, f => f, StringComparer.Ordinal);
            var wrong = new List<string>(); var listed = new List<string>(); var unmarked = new List<string>();
            foreach (var (path, value, answer) in Decisions308)
            {
                if (!known.TryGetValue(path, out ProfileField field) || !ProfileParse(field.Field.FieldType, value, out object want)) { wrong.Add(path + " (not a field)"); continue; }
                var row = sheet.Rows[path]; ProfileParse(field.Field.FieldType, row.value, out object has);
                listed.Add(path + " " + ProfileText(has) + " (answer " + answer + ")");
                if (!ProfileSame(has, want)) wrong.Add(path + " = " + ProfileText(has) + ", the answer is " + ProfileText(want));
                if (!(row.from ?? "").StartsWith("D308", StringComparison.Ordinal)) unmarked.Add(path);
            }
            var strangers = sheet.Rows.Values.Where(r => (r.from ?? "").StartsWith("D308", StringComparison.Ordinal) && !Decisions308.Any(d => d.path == r.path)).Select(r => r.path).ToList();
            c.Add("D2.profile_data_is_the_decisions", wrong.Count == 0 && unmarked.Count == 0 && strangers.Count == 0, "D308-24: " + string.Join(", ", listed)
                + (wrong.Count > 0 ? " ; NOT the answer: " + string.Join("; ", wrong) : "") + (unmarked.Count > 0 ? " ; decided but not marked so in the data: " + string.Join(", ", unmarked) : "")
                + (strangers.Count > 0 ? " ; marked as decided but not in the decision table: " + string.Join(", ", strangers) : "") + " ; the other " + (sheet.Fields.Count - Decisions308.Length) + " rows are the designed values (TEST)");
            var asset = AssetDatabase.LoadAssetAtPath<PlayerJuice308ProfileSO>(ProfileAssetPath);
            if (asset == null)
            {
                if (assetRequired) c.Add("D3.asset_equals_data", false, ProfileAssetPath + " is absent (profile:create)");
                else c.Note("D3: " + ProfileAssetPath + " is absent - profile:create writes it from the data; until then only the rejection fix B-1 runs");
                return sheet;
            }
            // two readings: the file's own text (what a new editor life and the game load) and the object this editor holds.
            // Either one alone is not the row: the loaded object can be the instance profile:create filled a moment ago.
            var diff = ProfileDiff(sheet, asset);
            string onDisk = AssetFileAgainst(sheet, out int fileDiffers);
            c.Add("D3.asset_equals_data", diff.Count == 0 && fileDiffers == 0, ProfileAssetPath + " against the data, " + sheet.Fields.Count + " fields (floats by their bits) - the file on disk: " + onDisk
                + " ; the loaded asset: " + diff.Count + " differ" + (diff.Count > 0 ? " - " + string.Join("; ", diff) : "") + " ; asset file sha256 " + AssetFileSha());
            return sheet;
        }

        // ---------------------------------------------------------------- the judge wall's data (cam:judge)

        static bool JudgeWall(out JudgeWallData data, out string refusal)
        {
            data = null; refusal = null;
            string file = Path.Combine(Repo, JudgeWallRelative);
            if (!File.Exists(file)) { refusal = "refused: missing " + file; return false; }
            try { data = JsonUtility.FromJson<JudgeWallData>(File.ReadAllText(file).TrimStart('﻿')); } catch (Exception e) { refusal = "refused: " + JudgeWallRelative + " is not readable (" + e.Message + ")"; return false; }
            if (data == null || data.schema != JudgeWallSchema) { refusal = "refused: " + JudgeWallRelative + " has schema '" + (data != null ? data.schema : "") + "' (expected '" + JudgeWallSchema + "')"; return false; }
            if (data.tile == null || data.tile.Length != 2 || data.tile[0] != JudgeWidth || data.tile[1] != JudgeHeight)
            { refusal = "refused: " + JudgeWallRelative + " gives its line widths for another capture size than " + JudgeWidth + " x " + JudgeHeight; return false; }
            if (data.minorLinePx < JudgeMinLinePx || data.majorLinePx < data.minorLinePx || data.centreLinePx < data.minorLinePx || data.minorEveryDegrees < 1 || data.majorEveryDegrees < data.minorEveryDegrees)
            {
                refusal = "refused: " + JudgeWallRelative + " - a wall line must be at least " + F(JudgeMinLinePx, "F1") + " px at the capture size (thinner lines break up on the reaction frames), the heavy and the centre line at least as wide, "
                    + "and the steps at least 1 degree (minor " + F(data.minorLinePx, "F2") + " px every " + data.minorEveryDegrees + " deg, heavy " + F(data.majorLinePx, "F2") + " px every " + data.majorEveryDegrees + " deg, centre " + F(data.centreLinePx, "F2") + " px)";
                return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- the new rows of "check"

        static void CheckJuice2(Checks c, Scene scene)
        {
            var sheet = ProfileRows(c, false);
            PlayerJuice308ProfileSO main = null;
            try
            {
                main = sheet.Ok ? ProfileFromSheet(sheet) : NewProfile(false);
                if (!sheet.Ok) c.Note("the rows Y and F below run on the class defaults (the data file is not usable)");
                CheckParry(c, main);
                CheckMarkFollow(c, main);
            }
            finally { if (main != null) Object.DestroyImmediate(main); }
            c.Add("W4.judge_wall_data", JudgeWall(out JudgeWallData wall, out string wallIssue), wall != null && wallIssue == null
                ? JudgeWallRelative + ": minor " + F(wall.minorLinePx, "F1") + " px every " + wall.minorEveryDegrees + " deg, heavy " + F(wall.majorLinePx, "F1") + " px every " + wall.majorEveryDegrees + " deg, centre " + F(wall.centreLinePx, "F1") + " px at " + JudgeWidth + " x " + JudgeHeight + " (floor " + F(JudgeMinLinePx, "F1") + " px)"
                : wallIssue);
            CheckSceneJuice2(c, scene);
        }

        // one solver through a short letter, the commit and the throw; the camera part of every frame is appended to `camera`,
        // everything else of every frame (shaft, splay, throw path) to `body`
        static void ParryRun(PlayerJuice308ProfileSO profile, bool parry, List<Vector3> camera, List<float> body, out PlayerJuice308Solver solver)
        {
            const float dt = 1f / 60f;
            var s = new PlayerJuice308Solver(); s.Configure(profile); solver = s;
            void Step(bool cameraAllowed, float castSeconds)
            {
                s.Advance(dt); s.Evaluate(cameraAllowed, out JuiceFrame308 f);
                camera.Add(f.Camera); body.Add(f.ShaftRaiseDegrees); body.Add(f.ShaftLeanDegrees.x); body.Add(f.ShaftLeanDegrees.y); body.Add(f.SplayAdd);
                if (castSeconds >= 0f) { s.EvaluateCast(castSeconds, out float along, out float over, out float drop); body.Add(along); body.Add(over); body.Add(drop); }
            }
            s.ModeEntered(); s.StrokeStarted();
            for (int i = 0; i < 8; i++) { s.NoteStrokeVelocity(new Vector2(2.4f, -.5f), dt); Step(false, -1f); }
            s.StrokeEnded();
            for (int i = 0; i < 4; i++) Step(false, -1f);
            s.DrawingEnded();
            body.Add(s.BeginCast(.8f)); body.Add(s.CastTwistScale); body.Add(s.CastOvershoot);
            s.CastKick(1f, 1f, parry);
            for (int i = 0; i < 36; i++) Step(true, i * dt);
        }

        static void CheckParry(Checks c, PlayerJuice308ProfileSO main)
        {
            PlayerJuice308ProfileSO off = null, on = null;
            try
            {
                off = Object.Instantiate(main); off.name = FixturePrefix + "_ParryOffProfile"; off.hideFlags = HideFlags.HideAndDontSave; off.CastKick.OnParryCasts = false;
                on = Object.Instantiate(main); on.name = FixturePrefix + "_ParryOnProfile"; on.hideFlags = HideFlags.HideAndDontSave; on.CastKick.OnParryCasts = true;
                var plainCamera = new List<Vector3>(); var plainBody = new List<float>(); ParryRun(off, false, plainCamera, plainBody, out var plain);
                var parryCamera = new List<Vector3>(); var parryBody = new List<float>(); ParryRun(off, true, parryCamera, parryBody, out var parried);
                var allowedCamera = new List<Vector3>(); var allowedBody = new List<float>(); ParryRun(on, true, allowedCamera, allowedBody, out var allowed);
                Vector3 peak = Vector3.zero; int parryNonZero = 0, allowedDiffers = 0;
                for (int i = 0; i < plainCamera.Count; i++)
                {
                    peak = Vector3.Max(peak, new Vector3(Mathf.Abs(plainCamera[i].x), Mathf.Abs(plainCamera[i].y), Mathf.Abs(plainCamera[i].z)));
                    if (!Zero(parryCamera[i])) parryNonZero++;
                    if (!SameBits(allowedCamera[i], plainCamera[i])) allowedDiffers++;
                }
                var k1 = new Clauses().Add("parry cast: every frame's camera offset is exactly (0, 0, 0)", parryNonZero == 0)
                    .Add("no camera clock was started for it", parried.CameraEvents == 0 && !parried.CameraRunning && parried.ParryKicksSkipped == 1)
                    .Add("another cast on the same profile kicks", peak.x > 0f && plain.CameraEvents == 1 && plain.ParryKicksSkipped == 0)
                    .Add("with the switch on a parry cast kicks like the other, bit for bit", allowedDiffers == 0 && allowed.ParryKicksSkipped == 0)
                    .Add("the data's switch is off", !main.CastKick.OnParryCasts);
                c.Add("Y1.parry_cast_no_kick", k1.Ok, "D308-24 answer 24, " + plainCamera.Count + " frames at 60 Hz, full power: a parry cast has " + parryNonZero + " frame(s) with a camera offset (kicks skipped " + parried.ParryKicksSkipped
                    + "); the same cast as a non-parry peaks at nod " + F(peak.x, "F3") + " deg, field of view " + F(peak.z, "F3") + " deg; CastKick.OnParryCasts in the data: " + main.CastKick.OnParryCasts + " [" + k1 + "]");
                int bodyDiffers = 0; bool sameLength = plainBody.Count == parryBody.Count;
                for (int i = 0; sameLength && i < plainBody.Count; i++) if (!SameBits(plainBody[i], parryBody[i])) bodyDiffers++;
                c.Add("Y2.parry_cast_keeps_hand_and_brush", sameLength && bodyDiffers == 0 && plainBody.Count > 100, "shaft raise / lean, splay, the throw's length, path, overshoot and twist scale over the same take, " + plainBody.Count + " values: "
                    + bodyDiffers + " differ between the parry cast and the other cast (bits)");
            }
            finally { if (off != null) Object.DestroyImmediate(off); if (on != null) Object.DestroyImmediate(on); }
        }

        // where a camera draws `world` on its target, in double precision, from the camera as it is at the call
        // (position is subtracted first; symmetric perspective, the whole target)
        static void PreciseMark(Camera k, Vector3 world, out double sx, out double sy)
        {
            Transform view = k.transform; Vector3 p = view.position; Quaternion q = view.rotation;
            double dx = (double)world.x - p.x, dy = (double)world.y - p.y, dz = (double)world.z - p.z;
            double qx = q.x, qy = q.y, qz = q.z, qw = q.w, n = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            qx = -qx / n; qy = -qy / n; qz = -qz / n; qw /= n;   // the inverse rotation
            double tx = 2.0 * (qy * dz - qz * dy), ty = 2.0 * (qz * dx - qx * dz), tz = 2.0 * (qx * dy - qy * dx);
            double vx = dx + qw * tx + (qy * tz - qz * ty), vy = dy + qw * ty + (qz * tx - qx * tz), vz = dz + qw * tz + (qx * ty - qy * tx);
            double half = Math.Tan(k.fieldOfView * .5 * Math.PI / 180.0), aspect = (double)k.pixelWidth / k.pixelHeight;
            sx = (vx / (vz * half * aspect) * .5 + .5) * k.pixelWidth; sy = (vy / (vz * half) * .5 + .5) * k.pixelHeight;
        }

        static void CheckMarkFollow(Checks c, PlayerJuice308ProfileSO main)
        {
            Scene preview = default; RenderTexture rt = null;
            Camera cam = null; Vector3 world = default; double seenX = 0, seenY = 0; bool seen = false;
            Action<ScriptableRenderContext, Camera> probe = (x, k) => { if (k == cam) { PreciseMark(k, world, out seenX, out seenY); seen = true; } };
            bool probing = false; CameraRigController owner = null;
            try
            {
                // A small target on purpose: an edit-mode render keeps memory in proportion to its target for a minute or two (a 960 x 540
                // capture frame held about 35 MB, operator's log 2026-10-05), and these rows render a few hundred times. The pixel
                // values are scaled to 1080p / 1440p for the verdict (a float32 pixel coordinate near 100 is exact to 1e-5 px).
                // The mark also stays inside any Game view this way (UpdateReticle hides a mark beyond Screen.width / height).
                const int width = FollowWidth, height = FollowHeight;
                preview = EditorSceneManager.NewPreviewScene();
                var pivot = FixtureObject("FollowPivot", preview);
                pivot.transform.SetPositionAndRotation(new Vector3(3277f, 180f, 2252f), Quaternion.Euler(8f, 137f, 0f));   // the main scene's coordinate range
                var camObject = FixtureObject("FollowCamera", preview, pivot.transform);
                camObject.transform.localPosition = new Vector3(.35f, .25f, -.2f);
                cam = camObject.AddComponent<Camera>(); cam.enabled = false; cam.fieldOfView = 52f; cam.nearClipPlane = .08f; cam.farClipPlane = 22000f; cam.scene = preview;
                rt = new RenderTexture(width, height, 16) { name = FixturePrefix + "_FollowRT", hideFlags = HideFlags.HideAndDontSave }; cam.targetTexture = rt;
                owner = pivot.AddComponent<CameraRigController>();   // edit mode: no Awake / OnEnable
                typeof(CameraRigController).GetField("_camera", Inst).SetValue(owner, camObject.transform);
                typeof(CameraRigController).GetField("_cameraPivot", Inst).SetValue(owner, pivot.transform);
                var drawing = typeof(CameraRigController).GetField("_drawing", Inst);
                Transform view = camObject.transform; Quaternion rotation0 = view.localRotation; float fov0 = cam.fieldOfView; Vector3 position0 = view.localPosition;
                // a bare HUD: only the reticle the real UpdateReticle places (no canvas is built: Awake does not run in Edit Mode)
                var hudObject = FixtureObject("FollowHud", preview);
                var reticle = FixtureObject("FollowReticle", preview, hudObject.transform).AddComponent<RectTransform>();
                var hud = hudObject.AddComponent<HudController>();
                typeof(HudController).GetField("_reticle", Inst).SetValue(hud, reticle);
                // the target: 7 m ahead, right of and below the view axis (inside the lock-on zone). UpdateReticle aims 1.1 m above it.
                const float chest = 1.1f;
                var target = FixtureObject("FollowTarget", preview).transform;
                target.position = view.position + view.forward * 7f + view.right * 1.1f - view.up * .9f - Vector3.up * chest;
                world = target.position + Vector3.up * chest;
                float to1080 = 1080f / height, to1440 = 1440f / height;
                RenderPipelineManager.beginCameraRendering += probe; probing = true;

                // ---- F6: the owner's formula alone, against the camera as it is drawn (no HUD, no engine read)
                {
                    Vector3[] offsets = { new Vector3(.9f, 0f, .8f), new Vector3(1.2f, .6f, 1.5f), new Vector3(-.4f, -.3f, -.8f) };
                    Vector3 keep = world; double worst = 0; int points = 0; bool allSeen = true; string where = "";
                    owner.RenderOffsetMarksFollow308 = true;
                    foreach (float ahead in new[] { 3f, 20f })
                        for (int ix = -1; ix <= 1; ix++)
                            for (int iy = -1; iy <= 1; iy++)
                            {
                                float span = ahead * Mathf.Tan(fov0 * .5f * Mathf.Deg2Rad) * .6f;
                                world = view.position + view.forward * ahead + view.right * (ix * span * width / height) + view.up * (iy * span);
                                PreciseMark(cam, world, out double x0, out double y0);
                                foreach (var o in offsets)
                                {
                                    bool got = CameraRigController.MarkShift308(cam, world, o, out Vector2 shift);
                                    owner.SetRenderOffset308(o.x, o.y, o.z); seen = false; cam.Render(); owner.ClearRenderOffset308();
                                    allSeen &= seen && got; points++;
                                    double miss = Math.Sqrt((x0 + shift.x - seenX) * (x0 + shift.x - seenX) + (y0 + shift.y - seenY) * (y0 + shift.y - seenY));
                                    if (miss > worst) { worst = miss; where = "offset " + o.ToString("F1") + " at " + F(ahead, "F0") + " m (" + ix + ", " + iy + ")"; }
                                }
                            }
                    world = keep;
                    c.Add("F6.shift_formula_matches_the_render", allSeen && worst * to1440 <= .05, points + " points x offsets (3 / 20 m, across 60 % of the view, nod / roll / field of view up to the ceilings and with the other signs): un-offset place + MarkShift308 against the place the offset camera draws, "
                        + "worst " + F((float)worst, "G3") + " px at " + width + " x " + height + " = " + F((float)(worst * to1440), "G3") + " px at 1440p (limit .05)" + (where.Length > 0 ? " (" + where + ")" : "") + (allSeen ? "" : " - a render raised no camera event or the formula answered false"));
                }

                // ---- F1: no reaction = today's placement, whatever the switch says
                owner.RenderOffsetMarksFollow308 = true;
                hud.UpdateReticle(target, cam);
                Vector3 today = cam.WorldToScreenPoint(world); Vector3 plain = reticle.position; bool visible = reticle.gameObject.activeSelf;
                if (!visible)
                {
                    c.Add("F1.no_reaction_is_todays_place", false, "the bare HUD hid the reticle: its screen test uses the Game view (" + Screen.width + " x " + Screen.height + ") and the mark would be at " + today.ToString("F1") + " of the " + width + " x " + height
                        + " fixture. Rows F2 - F5 did not run. Open a Game view at least that large and run the check again.");
                    return;
                }
                owner.RenderOffsetMarksFollow308 = false; owner.SetRenderOffset308(.9f, 0f, .8f); hud.UpdateReticle(target, cam); Vector3 switchOff = reticle.position; owner.ClearRenderOffset308();
                int quietShifts = hud.ReticleFollowShifts308;   // no reaction, and a reaction with the switch off: must still be 0
                // The draw flag comes on while an offset is still ON the owner (the render would refuse it): the owner's own flag test
                // in TryGetMarkShift308 has to answer "nothing to add". The offset is pushed first on purpose - pushed after the flag
                // it would be refused by SetRenderOffset308 and this clause would read the "no offset" branch instead.
                // (Edit Mode keeps one frame count, so that push re-places this frame's reticle with its shift: the control clause.)
                owner.RenderOffsetMarksFollow308 = true; owner.SetRenderOffset308(.9f, 0f, .8f);
                bool pushMoved = hud.ReticleFollowShifts308 == quietShifts + 1 && !SameBits(reticle.position, today) && Bits(owner.RenderOffset308.x) == Bits(.9f);
                int beforeDrawFlag = hud.ReticleFollowShifts308;
                drawing.SetValue(owner, true); hud.UpdateReticle(target, cam); Vector3 whileDrawing = reticle.position; bool drawFlagHeld = hud.ReticleFollowShifts308 == beforeDrawFlag && Bits(owner.RenderOffset308.x) == Bits(.9f);
                drawing.SetValue(owner, false); owner.ClearRenderOffset308();
                hud.UpdateReticle(target, cam); Vector3 again = reticle.position;
                var k1 = new Clauses().Add("no reaction: the reticle is on Camera.WorldToScreenPoint (bits)", SameBits(plain, today)).Add("the HUD added no shift", quietShifts == 0)
                    .Add("switch off + a reaction: the same bits", SameBits(switchOff, today))
                    .Add("control - the same reaction with the switch on moves the mark", pushMoved)
                    .Add("draw flag + that reaction still on the owner: the same bits, no shift added", SameBits(whileDrawing, today) && drawFlagHeld)
                    .Add("after a reaction was cleared: the same bits", SameBits(again, today));
                c.Add("F1.no_reaction_is_todays_place", k1.Ok, "bare HudController + the camera owner at main-scene coordinates, " + width + " x " + height + ", target 7 m ahead: reticle at " + plain.ToString("F2") + " [" + k1 + "]");

                // ---- F2 - F4: the reaction of each event, frame by frame, in the order of a game frame:
                //      wiring LateUpdate (0): UpdateReticle | rig LateUpdate (1000): the juice pushes this frame's offset | the render
                double Run(bool follow, bool announced, out double worstEngineRead, out int frames, out bool everySeen, out string worstAt)
                {
                    double worst = 0; worstEngineRead = 0; frames = 0; everySeen = true; worstAt = "";
                    const float dt = 1f / 30f;
                    foreach (string name in new[] { "cast", "call", "setdown", "stack" })
                    {
                        var s = new PlayerJuice308Solver(); s.Configure(main);
                        if (name == "stack") { Fire(s, "cast"); Fire(s, "call"); Fire(s, "setdown"); } else Fire(s, name);
                        for (int f = 0; f < 13; f++)
                        {
                            s.Advance(dt); s.Evaluate(true, out JuiceFrame308 frame); Vector3 o = frame.Camera;
                            hud.UpdateReticle(target, cam);
                            owner.RenderOffsetMarksFollow308 = follow;
                            if (announced) owner.SetRenderOffset308(o.x, o.y, o.z);
                            else { var listener = typeof(HudController).GetField("_followChanged308", Inst).GetValue(hud) as Action; if (listener != null) owner.RenderOffsetChanged308 -= listener; owner.SetRenderOffset308(o.x, o.y, o.z); if (listener != null) owner.RenderOffsetChanged308 += listener; }
                            seen = false; cam.Render(); everySeen &= seen; frames++;
                            Vector3 at = reticle.position;
                            double miss = Math.Sqrt((at.x - seenX) * (at.x - seenX) + (at.y - seenY) * (at.y - seenY));
                            if (miss > worst) { worst = miss; worstAt = name + " +" + F(f * dt * 1000f, "F0") + " ms, offset " + o.ToString("F2"); }
                        }
                        owner.ClearRenderOffset308(); hud.UpdateReticle(target, cam);
                    }
                    return worst;
                }
                hud.UpdateReticle(target, cam); seen = false; cam.Render();
                double baseline = Math.Sqrt((reticle.position.x - seenX) * (reticle.position.x - seenX) + (reticle.position.y - seenY) * (reticle.position.y - seenY));   // the engine's own reading error, no reaction
                int shiftsBefore = hud.ReticleFollowShifts308, replacedBefore = hud.ReticleFollowReplaced308;
                double followed = Run(true, true, out _, out int framesOn, out bool seenOn, out string followedAt);
                int replaced = hud.ReticleFollowReplaced308 - replacedBefore, shifted = hud.ReticleFollowShifts308 - shiftsBefore;
                double before = Run(false, true, out _, out _, out bool seenOff, out string beforeAt);
                double late = Run(true, false, out _, out _, out bool seenLate, out string lateAt);
                c.Add("F2.reticle_stays_on_the_target", seenOn && followed * to1440 <= 1.0 && replaced > 0 && shifted > 0, "D308-24 answer 23, " + framesOn + " frames (cast, call, set-down and all three at once, 30 Hz, the data's values): reticle against the target as that frame is drawn, worst "
                    + F((float)followed, "F3") + " px at " + width + " x " + height + " = " + F((float)(followed * to1080), "F3") + " px at 1080p / " + F((float)(followed * to1440), "F3") + " px at 1440p (limit 1.0; with no reaction this fixture reads " + F((float)baseline, "F3")
                    + " px) (" + followedAt + "); placed again after a late push " + replaced + " time(s)");
                c.Add("F3.control_sees_the_old_gap", seenOff && before * to1080 > 5.0, "control - the same frames with the switch off (the placement before the 3rd revision): worst " + F((float)(before * to1080), "F2") + " px at 1080p (" + beforeAt + "); a row that reads under 5 px here could not see the defect");
                c.Add("F4.control_sees_a_late_place", seenLate && late * to1080 > 2.0, "control - the same frames with the owner's announcement withheld (the HUD keeps the offset of the frame before): worst " + F((float)(late * to1080), "F2") + " px at 1080p (" + lateAt + "); the announcement is what keeps the mark on this frame's offset");
                var k5 = new Clauses().Add("camera rotation after = before (bits)", Same(view.localRotation, rotation0)).Add("field of view after = before (bits)", SameBits(cam.fieldOfView, fov0)).Add("local position after = before (bits)", SameBits(view.localPosition, position0))
                    .Add("conflicts 0", owner.RenderOffsetConflicts308 == 0).Add("listener faults 0", owner.RenderOffsetListenerFaults308 == 0).Add("the offset is off the owner", Zero(owner.RenderOffset308));
                c.Add("F5.camera_untouched_by_the_follow", k5.Ok, "after " + owner.RenderOffsetApplied308 + " renders with an offset: the camera's own values are what they were [" + k5 + "]");
            }
            finally
            {
                if (probing) RenderPipelineManager.beginCameraRendering -= probe;
                if (owner != null) owner.ClearRenderOffset308();
                if (preview.IsValid())
                {
                    foreach (var root in preview.GetRootGameObjects())
                    {
                        foreach (var rig in root.GetComponentsInChildren<CameraRigController>(true)) rig.ClearRenderOffset308();
                        Object.DestroyImmediate(root);
                    }
                    EditorSceneManager.ClosePreviewScene(preview);
                }
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            }
        }

        // the open scene, read only: can the rig tell a parry cast (walker -> wiring), and will the HUD find the camera owner?
        static void CheckSceneJuice2(Checks c, Scene scene)
        {
            var rig = SceneRig(scene, out _);
            if (rig == null) return;   // CheckScene already said so
            var so = new SerializedObject(rig);
            var walker = so.FindProperty("_walker")?.objectReferenceValue as WorldMacroCombatWalker;
            var cameraRig = so.FindProperty("_cameraRig")?.objectReferenceValue as CameraRigController;
            bool wiring = walker != null && walker.Wiring != null;
            var view = cameraRig != null ? typeof(CameraRigController).GetField("_camera", Inst).GetValue(cameraRig) as Transform : null;
            var cam = view != null ? view.GetComponent<Camera>() : null;
            bool lookup = cam != null && cam.GetComponentInParent<CameraRigController>(true) == cameraRig;
            int huds = Object.FindObjectsByType<HudController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(h => h.gameObject.scene == scene);
            c.Add("W3.parry_tell_and_mark_follow_wired", wiring && lookup, "open scene " + scene.name + ": rig -> walker " + (walker != null) + " -> wiring " + wiring + " (the parry tell; without it every successful cast kicks, as before); the camera owner sits above its camera "
                + lookup + " (the HUD finds it there; without it the marks stay where they were); HudController in the scene: " + huds);
        }

        // ---------------------------------------------------------------- parry-identity (the cloned rig)

        // one take on the clone: rise, one stroke, the commit as a parry cast or not, the throw. Transforms of every recorded frame,
        // and the offset the rig pushed to the clone's camera owner on every frame after the commit.
        static List<float[]> ParryTake(Stage stage, Transform[] all, CameraRigController owner, PlayerJuice308ProfileSO profile, bool parry, List<Vector3> camera, out string castNote)
        {
            var rig = stage.Rig; var cam = stage.Camera; Rect rect = cam.pixelRect; castNote = "";
            Vector2 Px(Vector2 v) => new Vector2(rect.x + v.x * rect.width, rect.y + v.y * rect.height);
            var frames = new List<float[]>();
            void Snap()
            {
                var f = new float[all.Length * 10];
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i]; if (t == null) continue;
                    Vector3 p = t.localPosition; Quaternion q = t.localRotation; Vector3 s = t.localScale; int o = i * 10;
                    f[o] = p.x; f[o + 1] = p.y; f[o + 2] = p.z; f[o + 3] = q.x; f[o + 4] = q.y; f[o + 5] = q.z; f[o + 6] = q.w; f[o + 7] = s.x; f[o + 8] = s.y; f[o + 9] = s.z;
                }
                frames.Add(f);
            }
            rig.PreviewEnd308(); rig.SetJuiceForcedOff308(false); rig.SetJuiceProfile308(profile);
            Vector2 start = GripPoses[0].viewport, end = start + new Vector2(.10f, -.03f);
            var input = new WorldMacroPlayerGestureRig.GestureInput308 { Drawing = true, Stroking = false, HasPointer = true, Camera = cam, Screen = Px(start) };
            rig.SetPreviewInput308(input); rig.PreviewModeEntered308();
            for (int i = 0; i < 100; i++) rig.PreviewEvaluate308(StepSeconds);
            rig.PreviewStrokeStarted308(); input.Stroking = true;
            const int strokeSteps = 40;
            for (int i = 0; i <= strokeSteps; i++) { input.Screen = Px(Vector2.Lerp(start, end, i / (float)strokeSteps)); rig.SetPreviewInput308(input); rig.PreviewEvaluate308(StepSeconds); Snap(); }
            rig.PreviewStrokeEnded308(); input.Stroking = false; rig.SetPreviewInput308(input);
            for (int i = 0; i < 4; i++) { rig.PreviewEvaluate308(StepSeconds); Snap(); }
            try
            {
                input.Drawing = false; input.HasPointer = false; rig.SetPreviewInput308(input);
                rig.PreviewCommitted308(true, false, parry);
                for (int i = 0; i < 100; i++) { rig.PreviewEvaluate308(StepSeconds); Snap(); camera.Add(owner != null ? owner.RenderOffset308 : Vector3.zero); }
            }
            catch (Exception e) { castNote = e.GetType().Name + ": " + e.Message; }
            return frames;
        }

        static string ParryIdentity()
        {
            var stage = OpenStage(out string refusal);
            if (stage == null) return refusal;
            using (stage)
            {
                var c = new Checks(); PlayerJuice308ProfileSO off = null, on = null;
                try
                {
                    var all = stage.Root.GetComponentsInChildren<Transform>(true);
                    var owner = stage.Camera.GetComponentInParent<CameraRigController>(true);
                    off = CaptureProfile(false, out string source); bool dataSwitch = off.CastKick.OnParryCasts; off.CastKick.OnParryCasts = false;
                    on = Object.Instantiate(off); on.name = FixturePrefix + "_ParryOnProfile"; on.hideFlags = HideFlags.HideAndDontSave; on.CastKick.OnParryCasts = true;
                    var scratch = new List<Vector3>();
                    ParryTake(stage, all, owner, off, false, scratch, out _);   // warm-up: every lag state has been through one take
                    var plainCamera = new List<Vector3>(); var plain = ParryTake(stage, all, owner, off, false, plainCamera, out string castNote);
                    var repeatCamera = new List<Vector3>(); var repeat = ParryTake(stage, all, owner, off, false, repeatCamera, out _);
                    int commitsBefore = stage.Rig.JuiceParryCommits308;
                    var parryCamera = new List<Vector3>(); var parried = ParryTake(stage, all, owner, off, true, parryCamera, out _);
                    int commits = stage.Rig.JuiceParryCommits308 - commitsBefore;
                    var afterCamera = new List<Vector3>(); var after = ParryTake(stage, all, owner, off, false, afterCamera, out _);
                    var allowedCamera = new List<Vector3>(); ParryTake(stage, all, owner, on, true, allowedCamera, out _);
                    int total = plain.Count; bool driven = castNote.Length == 0 && plainCamera.Count > 0 && owner != null;
                    c.Note("take: " + all.Length + " transforms under the clone, " + total + " frames (a stroke, the commit, the throw), step " + F(StepSeconds * 1000f, "F0") + " ms; profile " + source + " with CastKick.OnParryCasts forced off (in the profile itself: " + dataSwitch + "). " + stage.Notes);
                    if (!driven) { c.Add("P0.commit_driven", false, "the commit / throw could not be driven on the clone in Edit Mode (" + (castNote.Length > 0 ? castNote : owner == null ? "no camera owner above the clone's camera" : "no frame recorded") + ") - this stays a Play item"); return Finish(c, "parry_identity"); }
                    float tolerance = IdentityDifference(plain, repeat, 0, total, all, out int repeatCount, out string repeatWhere) * 2f;
                    c.Add("P0.driver_repeats", repeat.Count == total && tolerance <= 2e-4f, "the same non-parry take twice: " + repeatCount + " value(s) differ, largest " + F(tolerance * .5f, "G3") + (repeatCount > 0 ? " (" + repeatWhere + ")" : "") + " - the tolerance of P3 (0 = bit for bit)");
                    Vector3 peak = Vector3.zero; int parryNonZero = 0, leak = 0, allowedDiffers = 0;
                    for (int i = 0; i < plainCamera.Count; i++)
                    {
                        peak = Vector3.Max(peak, new Vector3(Mathf.Abs(plainCamera[i].x), Mathf.Abs(plainCamera[i].y), Mathf.Abs(plainCamera[i].z)));
                        if (i < parryCamera.Count && !Zero(parryCamera[i])) parryNonZero++;
                        if (i >= afterCamera.Count || !SameBits(afterCamera[i], plainCamera[i])) leak++;
                        if (i >= allowedCamera.Count || !SameBits(allowedCamera[i], plainCamera[i])) allowedDiffers++;
                    }
                    c.Add("P1.parry_cast_pushes_no_camera_offset", parryCamera.Count == plainCamera.Count && parryNonZero == 0 && commits == 1, "the take committed as a parry cast: frames with an offset on the clone's camera owner " + parryNonZero + " of " + parryCamera.Count + "; the rig counted " + commits + " parry commit(s)");
                    c.Add("P2.control_the_other_cast_kicks", peak.x > .2f, "control - the same take as a non-parry cast: the rig pushed a nod up to " + F(peak.x, "F3") + " deg, field of view " + F(peak.z, "F3") + " deg (the clone has no letter, so the power proxy is 0 = the floor scale " + F(off.CastKick.PowerFloor, "F2") + ")"
                        + (peak.x > .2f ? "" : " - nothing was pushed: the clone's rig does not reach its camera owner in Edit Mode, so P1 proves nothing here"));
                    float body = IdentityDifference(plain, parried, 0, total, all, out int bodyCount, out string bodyWhere);
                    c.Add("P3.hand_brush_torso_identical", parried.Count == total && body <= tolerance, "every transform of every frame, parry cast against the other cast: " + bodyCount + " value(s) differ, largest " + F(body, "G3") + (bodyCount > 0 ? " (" + bodyWhere + ")" : "") + " (limit " + F(tolerance, "G3") + ")");
                    c.Add("P4.kind_does_not_leak", after.Count == total && leak == 0, "a non-parry cast right after the parry cast: its camera trace differs from the first non-parry take on " + leak + " frame(s)");
                    c.Add("P5.switch_on_control", allowedDiffers == 0, "control - CastKick.OnParryCasts on: the parry cast's camera trace differs from the non-parry take on " + allowedDiffers + " frame(s)");
                }
                finally
                {
                    if (off != null) Object.DestroyImmediate(off);
                    if (on != null) Object.DestroyImmediate(on);
                }
                return Finish(c, "parry_identity");
            }
        }
    }
}
