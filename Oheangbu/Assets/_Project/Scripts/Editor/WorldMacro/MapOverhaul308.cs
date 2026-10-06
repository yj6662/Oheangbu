using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 map overhaul (SPEC-MAP-OVERHAUL-308, D308-14 / 14b / 15) - bundle importer, checks and Edit-mode stills [TEST values throughout].
    // Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.MapOverhaul308 Run "<command>"   (Map308 Run is the same)
    // Queue-safe: no dialog is ever opened; refusals come back as "REFUSED ...", failures as "FAILED ...".
    // WRITES ONLY under Assets/_Project/Art/UI/UI308/Map/ and Assets/_Project/Resources/UI308/Map/, plus ONE reference in
    // Assets/_Project/Resources/UI304/map/MapStyle304.asset (Notation308). Every path is checked and protected trees
    // (Watershed295 / Reworld292 / MountainTrail285, W_Demo_Compact, 03_Content) are refused on top of that. No scene and no
    // Map.asset is written. Only the edited assets are saved (AssetDatabase.SaveAssetIfDirty): other sessions' dirty assets are left
    // alone. Every write is checked on the FILE afterwards. Ledger: Art/UI308/Map/ledger_import.json (byte backups under Backups/).
    //   status                 read-only: stage bundle, imported bundle, notation asset, the MapStyle304 reference, shaders
    //   dry  (= import:dry)    what apply would write (nothing is written)
    //                          TWO bundles may be staged: stage 'base' in the stage folder's mirror of Assets, any other height stage
    //                          beside it under Tools/Unity/Stage308_map/Bundles/<stage>/. The one whose stage is the stage the scenes
    //                          carry (cliff ledgers) is the bundle every command reads.
    //   apply (= import)       copies the baked bundle (map308_*.png / .bytes / .json) from the stage folder, sets the importers to the
    //                          contract, creates / refreshes Resources/UI308/Map/MapNotation308.asset from map308_notation.json
    //                          (frames: sprites of the theme kit atlas) and points MapStyle304.Notation308 at it. Refuses when the
    //                          bundle is incomplete, its contract version differs, Map.asset or a layout copy changed since the bake,
    //                          or the bundle's stage is not the stage the scenes carry. The same input twice = "변경 없음".
    //   check                  Edit: bundle sha, importer values, references, BakedFor, shader compile + keywords, LDR, texture memory
    //   revert                 clears MapStyle304.Notation308 (the old map is back; files stay)
    //   revert:files           the same, then removes what this ledger created and restores what it changed (bytes), backs up first
    //   report                 the notation asset's numbers
    //   reveal                 read-only (#308 map 3c): the marker reveal rules of the notation asset - the asset against the data file
    //                          apply reads (Tools/Unity/Stage308_map3c/Data/marker_reveal308.json), the table against Map.asset and the
    //                          location catalogue, the ground rule's numbers against the LIVE MapFog308.cginc #defines and the HUD
    //                          minimap's glyph, "every GroundDrawn marker can show", and the recorded fog states of
    //                          Tools/Unity/Stage308_map3c/Data/reveal308_cases.json run through MapGround308 (visible == ground drawn)
    //                          -> Art/UI308/Map/checks-reveal.txt
    //   preview[:at=x,z][:reveal=all|none|<metres>][:follow=<deg>][:size=1080|1440][:bundle=on|off][:groundrule=on|off][:sheetrim=on|off][:tag=<name>]
    //                          #308 map 4: sheetrim=off = the unfolded sheet with the minimap's rim (the picture before map 4; a copy of
    //                          the notation in memory, the asset is not touched). The answer carries three RIM lines: the notation's
    //                          numbers, then the vector each material holds (`RIM material sheet` / `RIM material minimap`, read back
    //                          from the material; Tools/Unity/Stage308_map4/Offline/map4_caps.py rimline compares the three).
    //                          `check` has one more line since map 4 (AC-M4.10: the asset's rim numbers = the bundle's) -> 36 lines.
    //                          Edit-mode stills WITHOUT Play: the real WorldMapPresenter and HudMinimap304 in a preview scene (isolated
    //                          session, nothing saved, the open scene is only read) -> Art/UI308/Map/preview/<tag>_*.png
    //                          #308 map 3c: a marker without an arrival event is known when the GAME's walk test says so (its cell
    //                          and its reveal rule); groundrule=off = every such marker by its cell alone (the picture before map 3c)
    // #308 recovery (Temporary Exception "lost bake inputs", Tools/Unity/Stage308_recover_mapin): the bundle's recorded inputs are
    // compared by MapBundleInputs308. A recorded input that is NOT ON DISK is taken on the bundle's own record only when
    // Tools/Art/map308_lost_inputs.json names that path with that sha - `TRUSTED absent input (...)`, one line each, in status / dry /
    // apply / check (INFO lines there: the count stays 36). An input that is on disk with other bytes is refused as before.
    // No static field: the import rules are built on each call, the reader's state lives on the stack.
    public static class MapOverhaul308
    {
        const string StageBundle = "Tools/Unity/Stage308_map/_ProjectAssets/Art/UI/UI308/Map";   // the bundle of stage 'base'
        const string StageBundles = "Tools/Unity/Stage308_map/Bundles";                          // Bundles/<stage>/: the bundle of another height stage
        const string ArtRoot = "Assets/_Project/Art/UI/UI308";
        const string ArtFolder = ArtRoot + "/Map";
        const string ShaderFolder = ArtFolder + "/Shaders";
        const string StrokeShaderPath = ShaderFolder + "/MapStroke308.shader", IconShaderPath = ShaderFolder + "/MapIcon308.shader", FogIncludePath = ShaderFolder + "/MapFog308.cginc";
        const string PaperShaderPath = "Assets/_Project/Resources/WorldMap/PaperMapSurface.shader";
        const string ResourcesRoot = "Assets/_Project/Resources/UI308";
        const string ResourcesFolder = ResourcesRoot + "/Map";
        const string NotationPath = ResourcesFolder + "/MapNotation308.asset";
        const string StylePath = "Assets/_Project/Resources/UI304/map/MapStyle304.asset";
        const string KitFolder = ArtRoot + "/Theme";
        const string KitAtlasPath = KitFolder + "/theme308_atlas.png", KitJsonPath = KitFolder + "/theme308_atlas.json";
        const string KitStageJson = "Tools/Unity/Stage308_theme/_ProjectAssets/Art/UI/UI308/Theme/theme308_atlas.json";
        const string NotationFile = "map308_notation.json";
        const string Usage = "status | dry | apply | check | reveal | revert[:files] | report | preview[:at=x,z][:reveal=all|none|<metres>][:follow=<deg>][:size=1080|1440][:bundle=on|off][:groundrule=on|off][:sheetrim=on|off][:tag=<name>]";
        // #308 map 3c: when a marker without an arrival event first shows. Read only; apply fills the notation asset from the first,
        // `reveal` replays the second. No data file = apply KEEPS the asset's table (the folder is not under git: its absence is
        // not a switch - the switch is "enabled": false in the input, which leaves a file without rows).
        const string RevealDataFile = "Tools/Unity/Stage308_map3c/Data/marker_reveal308.json";
        const string RevealCasesFile = "Tools/Unity/Stage308_map3c/Data/reveal308_cases.json";

        static string Out => Path.Combine(Harness303.RepoRoot, "Art", "UI308", "Map");
        static string RepoAbs(string repoPath) => Path.Combine(Harness303.RepoRoot, repoPath.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>The staged bundle folder every command reads (repo-relative): Bundles/&lt;stage&gt; when the scenes carry a height
        /// stage other than 'base' and that bundle is baked, else the stage folder's own bundle (stage 'base'). The stage name comes
        /// from the cliff ledgers and becomes a folder name: letters and digits only.</summary>
        static string BundleFolder()
        {
            string stage = SceneStage(new List<string>());
            if (!string.IsNullOrEmpty(stage) && stage != "base" && stage.Length <= 16 && stage.All(char.IsLetterOrDigit))
            {
                string side = StageBundles + "/" + stage;
                if (File.Exists(Path.Combine(RepoAbs(side), NotationFile))) return side;
            }
            return StageBundle;
        }

        /// <summary>Every staged bundle: folder -> the stage its notation names ("?" when unreadable).</summary>
        static List<(string folder, string stage)> StagedBundles()
        {
            var list = new List<(string, string)>();
            var folders = new List<string> { StageBundle };
            string sides = RepoAbs(StageBundles);
            if (Directory.Exists(sides)) foreach (string dir in Directory.GetDirectories(sides).OrderBy(d => d, StringComparer.Ordinal)) folders.Add(StageBundles + "/" + Path.GetFileName(dir));
            foreach (string folder in folders)
            {
                string file = Path.Combine(RepoAbs(folder), NotationFile);
                if (!File.Exists(file)) continue;
                string stage = "?";
                try { stage = Json.Text(Json.Parse(File.ReadAllText(file, Encoding.UTF8)), "?", "stage"); } catch (Exception) { }
                list.Add((folder, stage));
            }
            return list;
        }
        sealed class Refuse : Exception { public Refuse(string m) : base(m) { } }
        static string F(float v, string f = "0.###") => v.ToString(f, CultureInfo.InvariantCulture);

        /// <summary>#308 map 4: the two rims a bundle's notation carries, as one line (dry and apply print it).</summary>
        static string RimNote(object root)
        {
            string mini = "minimap " + Json.Text(root, "?", "values", "fogEdge", "variant") + " a" + F((float)Json.Num(root, 0, "values", "fogEdge", "inkAlpha")) + " "
                + F((float)Json.Num(root, 0, "values", "fogEdge", "px")) + " px broken";
            if (Json.At(root, "values", "fogEdge", "sheet") == null) return mini + " | sheet: no block in this bundle (the sheet draws the minimap's rim)";
            return mini + " | sheet whole " + F((float)Json.Num(root, 0, "values", "fogEdge", "sheet", "whole")) + ", " + F((float)Json.Num(root, 0, "values", "fogEdge", "sheet", "px")) + " px a"
                + F((float)Json.Num(root, 0, "values", "fogEdge", "sheet", "inkAlpha"));
        }

        /// <summary>#308 map 4 (AC-M4.10): the rim numbers apply writes from a bundle's notation, as one string. `check` compares it with
        /// RimAsset: the minimap's pair, the sheet's switch and - when the bundle carries the sheet block - its width and ink alpha.</summary>
        static string RimBundle(object root)
        {
            bool sheet = Json.At(root, "values", "fogEdge", "sheet") != null;
            return "minimap a" + F((float)Json.Num(root, -1, "values", "fogEdge", "inkAlpha")) + " " + F((float)Json.Num(root, -1, "values", "fogEdge", "px")) + " px | sheet whole "
                + F(sheet ? Mathf.Clamp01((float)Json.Num(root, 0, "values", "fogEdge", "sheet", "whole")) : 0f)
                + (sheet ? ", " + F(Mathf.Clamp((float)Json.Num(root, -1, "values", "fogEdge", "sheet", "px"), .5f, 4f)) + " px a" + F(Mathf.Clamp01((float)Json.Num(root, -1, "values", "fogEdge", "sheet", "inkAlpha"))) : "");
        }

        static string RimAsset(MapNotation308SO n, bool sheet) => "minimap a" + F(n.EdgeRimAlpha) + " " + F(n.EdgeRimPx) + " px | sheet whole " + F(n.SheetRimWhole)
            + (sheet ? ", " + F(n.SheetRimPx) + " px a" + F(n.SheetRimAlpha) : "");

        static string V4(Vector4 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ", " + F(v.w) + ")";

        /// <summary>Spec name of the entry point; same as Run.</summary>
        public static string Execute(string argument) => Run(argument);

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return Status();
                if (c == "report") return Report();
                if (c == "check") return Check();
                if (c == "reveal") return Reveal();
                bool import = c == "dry" || c == "apply" || c == "import" || c == "import:dry";
                bool revert = c == "revert" || c == "revert:files";
                bool preview = c == "preview" || c.StartsWith("preview:", StringComparison.Ordinal);
                if (!import && !revert && !preview) return "REFUSED unknown MapOverhaul308 command '" + c + "' (" + Usage + ")";
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit Mode only (Play is running)";
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "REFUSED the editor is compiling or importing; call again when done";
                if (EditorUtility.scriptCompilationFailed) return "REFUSED scripts failed to compile";
                if (import) return Import(c == "dry" || c == "import:dry");
                if (revert) return Revert(c == "revert:files");
                return Preview(c);
            }
            catch (Refuse r) { return "REFUSED " + r.Message; }
            catch (Exception e) { return "FAILED " + e; }
        }

        // ------------------------------------------------------------------ write guard + ledger
        static void Guard(string assetPath)
        {
            string p = (assetPath ?? "").Replace('\\', '/');
            // a prefix test alone would let "…/UI308/Map/../Theme/x" through (a path from the notation's outputs[] or from the ledger)
            if (p.Split('/').Any(part => part == ".." || part == ".")) throw new Refuse("path with a '.' or '..' segment refused: " + p);
            bool allowed = p.StartsWith(ArtFolder + "/", StringComparison.Ordinal) || p.StartsWith(ResourcesFolder + "/", StringComparison.Ordinal)
                || p == ArtFolder || p == ArtRoot || p == ResourcesFolder || p == ResourcesRoot || p == StylePath;
            if (!allowed) throw new Refuse("write outside the #308 map folders refused: " + p);
            if (Harness303.IsProtected(p)) throw new Refuse("protected path " + p);
        }

        [Serializable] sealed class Entry { public string utc = "", asset = "", backup = "", shaBefore = "", shaAfter = "", note = ""; public bool created; }
        [Serializable] sealed class Ledger { public string kind = "map308_import", created = "", stage = "", notationSha = ""; public List<Entry> entries = new List<Entry>(); }
        static string LedgerFile => Path.Combine(Out, "ledger_import.json");
        static Ledger ReadLedger() => File.Exists(LedgerFile) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerFile)) : null;
        static void WriteLedger(Ledger l) { Directory.CreateDirectory(Out); File.WriteAllText(LedgerFile, JsonUtility.ToJson(l, true), new UTF8Encoding(false)); }
        static string BackupDir(string tag) => Path.Combine(Out, "Backups", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + tag);

        // records the file as it is now (byte copy) before a write; created = the file does not exist yet
        static Entry Before(Ledger ledger, string filePath, string backupDir, string note)
        {
            Guard(filePath.EndsWith(".meta", StringComparison.Ordinal) ? filePath.Substring(0, filePath.Length - 5) : filePath);
            string abs = Harness303.Abs(filePath);
            var e = new Entry { utc = DateTime.UtcNow.ToString("O"), asset = filePath, note = note, created = !File.Exists(abs) };
            if (!e.created)
            {
                Directory.CreateDirectory(backupDir);
                e.backup = Path.Combine(backupDir, Path.GetFileName(filePath));
                File.Copy(abs, e.backup, true);
                e.shaBefore = Harness303.Sha(abs);
            }
            ledger.entries.Add(e);
            return e;
        }
        static void After(Entry e)
        {
            string abs = Harness303.Abs(e.asset);
            if (!File.Exists(abs)) throw new Exception("write did not land on disk: " + e.asset);
            e.shaAfter = Harness303.Sha(abs);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            Guard(path);
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static bool SameSha(string a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>A bundle file name as the bake writes it: "map308_" + letters / digits / '_' + ".png" | ".bytes" | ".json", no folder part.</summary>
        static bool BundleFileName(string file)
        {
            if (string.IsNullOrEmpty(file) || !file.StartsWith("map308_", StringComparison.Ordinal)) return false;
            string ext = Path.GetExtension(file), stem = file.Substring(0, file.Length - ext.Length);
            if (ext != ".png" && ext != ".bytes" && ext != ".json") return false;
            foreach (char ch in stem) if (!(ch >= 'a' && ch <= 'z') && !(ch >= 'A' && ch <= 'Z') && !(ch >= '0' && ch <= '9') && ch != '_') return false;
            return true;
        }

        // ------------------------------------------------------------------ the stage bundle (read only)
        sealed class Bundle
        {
            public string JsonText = "", JsonSha = "", Stage = "", BakedFor = "", Folder = "", Dir = "";
            public int Version;
            public Dictionary<string, object> Root;
            public List<(string file, string sha, long bytes)> Outputs = new List<(string, string, long)>();
            public List<(string role, string path, string sha)> Inputs = new List<(string, string, string)>();
            public List<string> Trusted = new List<string>(), InputNotes = new List<string>();   // #308 recovery: BundleProblems fills them
        }

        static Bundle ReadBundle()
        {
            string folder = BundleFolder(), dir = RepoAbs(folder);
            string file = Path.Combine(dir, NotationFile);
            if (!File.Exists(file)) throw new Refuse("no bundle in " + folder + " (" + NotationFile + " missing): run Tools/Art/map308_bake.py first");
            var b = new Bundle { JsonText = File.ReadAllText(file, Encoding.UTF8), JsonSha = Harness303.Sha(file), Folder = folder, Dir = dir };
            b.Root = Json.Parse(b.JsonText) as Dictionary<string, object>;
            if (b.Root == null) throw new Refuse(NotationFile + " is not a JSON object");
            b.Version = (int)Json.Num(b.Root, -1, "version");
            b.Stage = Json.Text(b.Root, "", "stage");
            b.BakedFor = Json.Text(b.Root, "", "bakedFor");
            foreach (var o in Json.List(b.Root, "outputs"))
                b.Outputs.Add((Json.Text(o, "", "file"), Json.Text(o, "", "sha256"), (long)Json.Num(o, 0, "bytes")));
            foreach (var o in Json.List(b.Root, "inputs"))
                b.Inputs.Add((Json.Text(o, "", "role"), Json.Text(o, "", "path"), Json.Text(o, "", "sha256")));
            return b;
        }

        /// <summary>Every reason the bundle must not be imported (empty = fine): contract version, missing or altered files, a
        /// Map.asset / layout copy that changed since the bake, a stage that is not the stage of the scenes.</summary>
        static List<string> BundleProblems(Bundle b, out string sceneStage)
        {
            var problems = new List<string>();
            sceneStage = SceneStage(problems);
            if (b.Version != MapNotation308SO.ContractVersion) problems.Add("contract version " + b.Version + " (this reader " + MapNotation308SO.ContractVersion + ")");
            foreach (string need in new[] { "map308_terrain.png", "map308_pattern.png", "map308_strokes.bytes", "map308_stroke_atlas.png", "map308_icons_L.png", "map308_icons_S.png", "map308_reveal_regions.bytes" })
                if (!b.Outputs.Any(o => o.file == need)) problems.Add("outputs[] does not list " + need);
            foreach (var o in b.Outputs)
            {
                // the name becomes a path under the map folder: only the bundle's own flat file names are taken
                if (!BundleFileName(o.file)) { problems.Add("outputs[] names a file that is not a map308_* bundle file: '" + o.file + "'"); continue; }
                string abs = Path.Combine(b.Dir, o.file);
                if (!File.Exists(abs)) { problems.Add("bundle file missing: " + o.file); continue; }
                if (!SameSha(Harness303.Sha(abs), o.sha)) problems.Add("bundle file differs from the sha in the notation: " + o.file);
            }
            // a stale notation: an input changed after the bake. Every input the bake recorded is compared (the map data and the
            // layout copies, and also the height field, the wet mask, the crossings, the tree sheets and the wall layouts: a
            // changed forest or terrain would otherwise be imported as an old picture).
            // #308 recovery: the comparison is MapBundleInputs308's - the same answers and the same words, plus `TRUSTED absent input`
            // for a file that is not on disk and is a row of the lost-input list with the sha this bundle recorded (never for a file
            // that is on disk: other bytes are refused whatever the list says)
            var inputs = MapBundleInputs308.Classify(Harness303.RepoRoot, b.Inputs, b.Stage);
            problems.AddRange(inputs.Problems); b.Trusted = inputs.Trusted; b.InputNotes = inputs.Notes;
            if (!b.Inputs.Any(i => i.role == "map")) problems.Add("inputs[] has no 'map' entry (Map.asset sha)");
            RevealProblems(problems);   // #308 map 3c: a malformed marker reveal data file is refused before anything is copied
            if (string.IsNullOrEmpty(b.Stage)) problems.Add("the notation names no stage");
            else if (sceneStage != null && !string.Equals(b.Stage, sceneStage, StringComparison.Ordinal))
                problems.Add("bundle stage '" + b.Stage + "' (" + b.Folder + ") is not the stage of the scenes ('" + sceneStage + "'): bake with map308_bake.py --stage " + sceneStage
                    + (sceneStage == "base" ? "" : " (it writes " + StageBundles + "/" + sceneStage + ")"));
            return problems;
        }

        static string Short(string sha) => string.IsNullOrEmpty(sha) ? "?" : sha.Substring(0, Math.Min(10, sha.Length));

        [Serializable] sealed class SceneLedger { public string key = "", state = "", stage = ""; }

        /// <summary>The height stage the scenes carry, from the cliff ledgers (Art/World/Compact/Rebuild/CliffBoundary308/Out/
        /// cb308-&lt;scene&gt;.json, state "applied"): "base" when none is applied; null (and a problem) when the scenes disagree.</summary>
        static string SceneStage(List<string> problems)
        {
            string dir = Path.Combine(Harness303.RepoRoot, "Art", "World", "Compact", "Rebuild", "CliffBoundary308", "Out");
            var stages = new SortedSet<string>(StringComparer.Ordinal); int ledgers = 0;
            if (Directory.Exists(dir))
                foreach (string file in Directory.GetFiles(dir, "cb308-*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(file).Substring("cb308-".Length);
                    if (name.StartsWith("surface-", StringComparison.Ordinal) || name.StartsWith("veg-", StringComparison.Ordinal) || name.StartsWith("check-", StringComparison.Ordinal)
                        || name.StartsWith("walk-", StringComparison.Ordinal) || name.EndsWith(".boxes", StringComparison.Ordinal)) continue;
                    // #308 map 1b: a scene's height ledger is cb308-<scene>.json and a scene key has no '-' or '.'. Phase 1b also
                    // writes cb308-wall-*, wall-check-*, rim-*, small-*, install-* next to them: reports and ledgers of other
                    // steps (some without a state or a stage) that were counted as scenes on stage 'base' and refused every command.
                    if (name.IndexOf('-') >= 0 || name.IndexOf('.') >= 0) continue;
                    SceneLedger l = null;
                    try { l = JsonUtility.FromJson<SceneLedger>(File.ReadAllText(file)); } catch (Exception) { }
                    if (l == null) continue;
                    ledgers++;
                    stages.Add(l.state == "applied" && !string.IsNullOrEmpty(l.stage) ? l.stage : "base");
                }
            if (ledgers == 0) return "base";
            if (stages.Count == 1) return stages.Min;
            problems.Add("the scenes carry different height stages (" + string.Join(", ", stages) + "): one bundle serves all scenes that share Map.asset");
            return null;
        }

        // ------------------------------------------------------------------ import
        // file -> importer contract (SPEC 7). kind: 0 data texture, 1 TextAsset
        // #308 map fix (M4): Pot = scale the file up to the next power of two at import (TextureImporterNPOTScale.ToLarger).
        // Unity compresses a texture WITH mip maps only when it is a power of two; the 1000 x 1500 terrain with mips and
        // CompressedHQ was imported as RGBA32 (15.63 MB in the editor, 8.0 MB on the GPU). As 1024 x 2048 it is BC7 with
        // the same mip chain use (2.8 MB on the GPU). The picture is read by world uv, so its texel size does not matter.
        sealed class Rule { public string File; public bool Mips, RepeatU, RepeatV, Compress, Pot; public int MaxSize; }
        static Rule[] TextureRules => new[]
        {
            new Rule { File = "map308_terrain.png", Mips = true, Compress = true, Pot = true, MaxSize = 2048 },
            new Rule { File = "map308_pattern.png", Mips = true, RepeatU = true, RepeatV = true, MaxSize = 256 },
            new Rule { File = "map308_stroke_atlas.png", Mips = true, RepeatU = true, MaxSize = 1024 },
            new Rule { File = "map308_icons_L.png", MaxSize = 512 },
            new Rule { File = "map308_icons_S.png", MaxSize = 256 },
        };

        /// <summary>GPU bytes of a texture: every mip = blocks across x blocks down x bytes per block of its imported format.</summary>
        static long GpuBytes(Texture t)
        {
            var format = t.graphicsFormat;
            long block = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(format);
            long bw = Math.Max(1, (int)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockWidth(format));
            long bh = Math.Max(1, (int)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockHeight(format));
            long total = 0; int w = t.width, h = t.height;
            for (int mip = 0; mip < Math.Max(1, t.mipmapCount); mip++)
            {
                total += ((w + bw - 1) / bw) * ((h + bh - 1) / bh) * block;
                w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1);
            }
            return total;
        }

        static string ImporterDiff(TextureImporter t, Rule r)
        {
            var d = new List<string>();
            if (t.textureType != TextureImporterType.Default) d.Add("type " + t.textureType + " -> Default");
            if (t.sRGBTexture) d.Add("sRGB on -> off");
            if (t.mipmapEnabled != r.Mips) d.Add("mips " + t.mipmapEnabled + " -> " + r.Mips);
            if (t.alphaIsTransparency) d.Add("alphaIsTransparency on -> off");
            var npot = r.Pot ? TextureImporterNPOTScale.ToLarger : TextureImporterNPOTScale.None;
            if (t.npotScale != npot) d.Add("npot " + t.npotScale + " -> " + npot);
            var wu = r.RepeatU ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; var wv = r.RepeatV ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (t.wrapModeU != wu || t.wrapModeV != wv) d.Add("wrap " + t.wrapModeU + "/" + t.wrapModeV + " -> " + wu + "/" + wv);
            if (t.filterMode != FilterMode.Bilinear) d.Add("filter " + t.filterMode + " -> Bilinear");
            if (t.maxTextureSize != r.MaxSize) d.Add("max " + t.maxTextureSize + " -> " + r.MaxSize);
            var want = r.Compress ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
            if (t.textureCompression != want) d.Add("compression " + t.textureCompression + " -> " + want);
            if (t.isReadable) d.Add("readable on -> off");
            return string.Join(", ", d);
        }

        static void ApplyImporter(TextureImporter t, Rule r)
        {
            t.textureType = TextureImporterType.Default; t.sRGBTexture = false; t.mipmapEnabled = r.Mips; t.alphaIsTransparency = false;
            t.alphaSource = TextureImporterAlphaSource.FromInput; t.npotScale = r.Pot ? TextureImporterNPOTScale.ToLarger : TextureImporterNPOTScale.None;
            t.wrapModeU = r.RepeatU ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; t.wrapModeV = r.RepeatV ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear; t.maxTextureSize = r.MaxSize; t.isReadable = false;
            t.textureCompression = r.Compress ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;   // HQ = BC7 on the PC targets
        }

        static string Import(bool dry)
        {
            var bundle = ReadBundle();
            var problems = BundleProblems(bundle, out string sceneStage);
            if (problems.Count > 0) return "REFUSED the bundle cannot be imported:\n  " + string.Join("\n  ", problems);
            string bakedPath = bundle.BakedFor.StartsWith("Oheangbu/", StringComparison.Ordinal) ? bundle.BakedFor.Substring("Oheangbu/".Length) : bundle.BakedFor;
            var baked = AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(bakedPath);
            if (baked == null) return "REFUSED bakedFor is not a WorldMapBakedDataSO: " + bakedPath;
            if (Harness303.IsProtected(bakedPath)) return "REFUSED bakedFor is a protected asset: " + bakedPath;
            foreach (string shader in new[] { StrokeShaderPath, IconShaderPath, FogIncludePath })
                if (!File.Exists(Harness303.Abs(shader))) return "REFUSED " + shader + " is not deployed: copy the stage's _ProjectAssets (Shaders + PaperMapSurface.shader) and the scripts first";
            var paper = AssetDatabase.LoadAssetAtPath<Shader>(PaperShaderPath);
            if (paper == null || !File.ReadAllText(Harness303.Abs(PaperShaderPath)).Contains("_MAP308")) return "REFUSED " + PaperShaderPath + " has no _MAP308 variant: deploy the stage's PaperMapSurface.shader first";
            var style = AssetDatabase.LoadAssetAtPath<MapStyle304SO>(StylePath);
            if (style == null) return "REFUSED " + StylePath + " missing (MapSetup304 map304-setup makes it)";

            var log = new List<string>();
            log.Add((dry ? "DRY" : "APPLY") + " map308 bundle " + bundle.Folder + ": stage '" + bundle.Stage + "' (scenes '" + sceneStage + "'), contract " + bundle.Version + ", notation sha " + bundle.JsonSha.Substring(0, 12)
                + ", baked for " + Path.GetFileName(bakedPath));
            foreach (string line in bundle.Trusted.Concat(bundle.InputNotes)) log.Add((dry ? "DRY " : "") + line);   // #308 recovery
            // 1. files
            var copies = new List<string>();
            var files = bundle.Outputs.Select(o => o.file).Concat(new[] { NotationFile }).Distinct().ToList();
            foreach (string file in files)
            {
                string from = Path.Combine(bundle.Dir, file), to = ArtFolder + "/" + file, toAbs = Harness303.Abs(to);
                bool same = File.Exists(toAbs) && SameSha(Harness303.Sha(toAbs), Harness303.Sha(from));
                if (!same) copies.Add(file);
            }
            foreach (string file in copies) log.Add((dry ? "DRY would copy " : "COPY ") + file + " -> " + ArtFolder);
            // 2. importer settings (known after the copy; for a dry run the current state when the file is already there)
            if (dry)
            {
                foreach (var rule in TextureRules)
                {
                    var importer = AssetImporter.GetAtPath(ArtFolder + "/" + rule.File) as TextureImporter;
                    string diff = importer != null ? ImporterDiff(importer, rule) : "new file";
                    if (diff.Length > 0) log.Add("DRY would set import of " + rule.File + ": " + diff);
                }
                var existing = AssetDatabase.LoadAssetAtPath<MapNotation308SO>(NotationPath);
                log.Add(existing == null ? "DRY would create " + NotationPath : "DRY would refresh " + NotationPath + " from " + NotationFile);
                log.Add(style.Notation308 == existing && existing != null ? "DRY MapStyle304.Notation308 already points at it" : "DRY would set MapStyle304.Notation308 (" + StylePath + ")");
                log.Add("DRY REVEAL " + (RevealData(out string revealDry).Count > 0 ? revealDry : "KEPT " + RevealKept(existing) + " - " + revealDry));   // #308 map 3c
                log.Add("DRY RIM " + RimNote(bundle.Root));   // #308 map 4
                log.Add(KitNote());
                log.Add("DRY nothing written");
                return string.Join("\n", log);
            }

            var ledger = ReadLedger() ?? new Ledger { created = DateTime.UtcNow.ToString("O") };
            ledger.stage = bundle.Stage; ledger.notationSha = bundle.JsonSha;
            string backup = BackupDir("_import");
            int written = 0;
            EnsureFolder(ArtRoot); EnsureFolder(ArtFolder); EnsureFolder(ResourcesRoot); EnsureFolder(ResourcesFolder);
            foreach (string file in copies)
            {
                string to = ArtFolder + "/" + file;
                var e = Before(ledger, to, backup, "bundle file");
                File.Copy(Path.Combine(bundle.Dir, file), Harness303.Abs(to), true);
                AssetDatabase.ImportAsset(to, ImportAssetOptions.ForceUpdate);
                After(e); written++;
            }
            foreach (var rule in TextureRules)
            {
                string path = ArtFolder + "/" + rule.File;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new Exception("no TextureImporter for " + path);
                string diff = ImporterDiff(importer, rule);
                if (diff.Length == 0) continue;
                var e = Before(ledger, path + ".meta", backup, "import settings: " + diff);
                ApplyImporter(importer, rule);
                importer.SaveAndReimport();
                After(e); written++;
                log.Add("SET import of " + rule.File + ": " + diff);
            }
            // 3. the notation asset
            var notation = AssetDatabase.LoadAssetAtPath<MapNotation308SO>(NotationPath);
            bool created = notation == null;
            var entry = Before(ledger, NotationPath, backup, created ? "notation asset (new)" : "notation asset (refresh)");
            if (created)
            {
                notation = ScriptableObject.CreateInstance<MapNotation308SO>(); notation.name = "MapNotation308";
                AssetDatabase.CreateAsset(notation, NotationPath);
            }
            string before = EditorJsonUtility.ToJson(notation);
            var kept = new List<string>();
            Fill(notation, bundle, baked, kept, log);
            bool changed = created || EditorJsonUtility.ToJson(notation) != before;
            if (changed)
            {
                EditorUtility.SetDirty(notation); AssetDatabase.SaveAssetIfDirty(notation);
                After(entry); written++;
                log.Add((created ? "CREATED " : "UPDATED ") + NotationPath);
            }
            else { ledger.entries.Remove(entry); log.Add("변경 없음 " + NotationPath); }
            foreach (string k in kept) log.Add("KEPT default (the notation has no such key): " + k);
            // 4. the one reference
            if (style.Notation308 != notation)
            {
                var e = Before(ledger, StylePath, backup, "MapStyle304.Notation308 -> MapNotation308");
                style.Notation308 = notation;
                EditorUtility.SetDirty(style); AssetDatabase.SaveAssetIfDirty(style);
                After(e); written++;
                log.Add("SET MapStyle304.Notation308 -> " + NotationPath);
            }
            else log.Add("변경 없음 MapStyle304.Notation308");
            // 5. verify on the files
            string guid = AssetDatabase.AssetPathToGUID(NotationPath);
            if (!File.ReadAllText(Harness303.Abs(StylePath)).Contains(guid)) throw new Exception("MapStyle304.asset on disk does not reference " + NotationPath);
            string notationText = File.ReadAllText(Harness303.Abs(NotationPath));
            foreach (string file in new[] { "map308_terrain.png", "map308_pattern.png", "map308_stroke_atlas.png", "map308_strokes.bytes" })
                if (!notationText.Contains(AssetDatabase.AssetPathToGUID(ArtFolder + "/" + file))) throw new Exception(NotationPath + " on disk does not reference " + file);
            log.Add("FILES verified: MapStyle304 -> notation -> terrain, pattern, stroke atlas, strokes");
            if (written > 0) { WriteLedger(ledger); log.Add("WROTE " + written + " file(s); ledger " + LedgerFile); }
            else log.Add("변경 없음 (nothing written)");
            log.Add(KitNote());
            return string.Join("\n", log);
        }

        // ------------------------------------------------------------------ JSON -> MapNotation308SO
        static Color Hex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            return ColorUtility.TryParseHtmlString(hex.StartsWith("#", StringComparison.Ordinal) ? hex : "#" + hex, out Color c) ? c : fallback;
        }

        static Vector4 Four(object node, Vector4 fallback, params string[] path)
        {
            var list = Json.List(node, path);
            if (list.Count < 4) return fallback;
            return new Vector4((float)Json.AsNum(list[0], fallback.x), (float)Json.AsNum(list[1], fallback.y), (float)Json.AsNum(list[2], fallback.z), (float)Json.AsNum(list[3], fallback.w));
        }

        static float Num(object node, float current, List<string> kept, params string[] path)
        {
            if (Json.At(node, path) == null) { kept.Add(string.Join(".", path)); return current; }
            return (float)Json.Num(node, current, path);
        }

        static void Fill(MapNotation308SO n, Bundle bundle, WorldMapBakedDataSO baked, List<string> kept, List<string> log)
        {
            var root = bundle.Root;
            n.Version = bundle.Version; n.Stage = bundle.Stage; n.BakedFor = baked;
            n.Terrain = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtFolder + "/map308_terrain.png");
            n.Pattern = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtFolder + "/map308_pattern.png");
            n.StrokeAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtFolder + "/map308_stroke_atlas.png");
            n.IconsL = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtFolder + "/map308_icons_L.png");
            n.IconsS = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtFolder + "/map308_icons_S.png");
            n.Strokes = AssetDatabase.LoadAssetAtPath<TextAsset>(ArtFolder + "/map308_strokes.bytes");
            n.RevealRegions = AssetDatabase.LoadAssetAtPath<TextAsset>(ArtFolder + "/map308_reveal_regions.bytes");
            n.NotationJson = AssetDatabase.LoadAssetAtPath<TextAsset>(ArtFolder + "/" + NotationFile);
            n.StrokeShader = AssetDatabase.LoadAssetAtPath<Shader>(StrokeShaderPath);
            n.IconShader = AssetDatabase.LoadAssetAtPath<Shader>(IconShaderPath);
            n.Inputs = bundle.Inputs.Select(i => new MapFileHash308 { Path = i.path, Sha256 = i.sha }).ToList();
            n.Outputs = bundle.Outputs.Select(o => new MapFileHash308 { Path = o.file, Sha256 = o.sha, Bytes = o.bytes }).ToList();

            // zoom bands: the first three upper limits
            var bands = Json.List(root, "zoomBands");
            if (bands.Count >= 3) n.ZoomBands = new Vector3((float)Json.Num(bands[0], n.ZoomBands.x, "maxMetresPerPx"), (float)Json.Num(bands[1], n.ZoomBands.y, "maxMetresPerPx"), (float)Json.Num(bands[2], n.ZoomBands.z, "maxMetresPerPx"));
            else kept.Add("zoomBands");

            // values
            n.Paper = Hex(Json.Text(root, null, "values", "paper"), n.Paper); n.Unknown = Hex(Json.Text(root, null, "values", "wash"), n.Unknown);
            n.Ink = Hex(Json.Text(root, null, "values", "ink"), n.Ink); n.Cinnabar = Hex(Json.Text(root, null, "values", "cinnabar"), n.Cinnabar);
            n.EdgeRimAlpha = Num(root, n.EdgeRimAlpha, kept, "values", "fogEdge", "inkAlpha");
            n.EdgeRimPx = Num(root, n.EdgeRimPx, kept, "values", "fogEdge", "px");
            // #308 map 4: the sheet's thin whole rim (values.fogEdge.sheet). A bundle of an older tool has no such block: the sheet then
            // draws the minimap's rim (whole 0) - a missing block never turns the new rim on by a default
            if (Json.At(root, "values", "fogEdge", "sheet") == null) n.SheetRimWhole = 0f;
            else
            {
                n.SheetRimWhole = Mathf.Clamp01(Num(root, 0f, kept, "values", "fogEdge", "sheet", "whole"));
                n.SheetRimPx = Mathf.Clamp(Num(root, n.SheetRimPx, kept, "values", "fogEdge", "sheet", "px"), .5f, 4f);
                n.SheetRimAlpha = Mathf.Clamp01(Num(root, n.SheetRimAlpha, kept, "values", "fogEdge", "sheet", "inkAlpha"));
            }
            log.Add("RIM " + RimNote(root));
            n.EdgeThreshold = new Vector2(Num(root, n.EdgeThreshold.x, kept, "values", "fogEdge", "crispEdge", "thresholdFrom"),
                Num(root, n.EdgeThreshold.y, kept, "values", "fogEdge", "crispEdge", "thresholdSpan"));
            var edgeCells = Json.List(root, "values", "fogEdge", "crispEdge", "noiseCellsPerFogCell");
            if (edgeCells.Count >= 2) n.EdgeNoiseCells = new Vector2((float)Json.AsNum(edgeCells[0], n.EdgeNoiseCells.x), (float)Json.AsNum(edgeCells[1], n.EdgeNoiseCells.y));
            else kept.Add("values.fogEdge.crispEdge.noiseCellsPerFogCell");
            n.Grain = new Vector2(n.Grain.x, Num(root, n.Grain.y, kept, "values", "washGrainAmp"));

            // terrain
            n.SlopeWash = Num(root, n.SlopeWash, kept, "terrain", "slopeWashMax");
            n.ElevationWash = Num(root, n.ElevationWash, kept, "terrain", "elevationWash");
            n.ElevationMaxMetres = Num(root, n.ElevationMaxMetres, kept, "terrain", "elevationMaxM");
            n.RockFrom = Num(root, n.RockFrom, kept, "terrain", "rockFrom");
            n.RockSoftness = Num(root, n.RockSoftness, kept, "terrain", "rockSoftness");
            n.RockInk = Num(root, n.RockInk, kept, "terrain", "rockInk");
            var elevation = Json.List(root, "terrain", "elevationRangeM");
            if (elevation.Count == 2) n.ElevationRangeMetres = new Vector2((float)Json.AsNum(elevation[0], n.ElevationRangeMetres.x), (float)Json.AsNum(elevation[1], n.ElevationRangeMetres.y));
            else kept.Add("terrain.elevationRangeM");
            n.ForestShowFrom = Num(root, n.ForestShowFrom, kept, "terrain", "forest", "showFrom");
            n.ForestGain = Num(root, n.ForestGain, kept, "terrain", "forest", "gain");
            n.ForestInk = Num(root, n.ForestInk, kept, "terrain", "forest", "dotInk");
            n.ForestInkWorld = Num(root, n.ForestInkWorld, kept, "terrain", "forest", "dotInkWorld");
            n.WaterRangeMetres = Num(root, n.WaterRangeMetres, kept, "terrain", "water", "sdfRangeM");
            n.ShorePx = Num(root, n.ShorePx, kept, "terrain", "water", "shoreLinePx");
            n.ShoreInk = Num(root, n.ShoreInk, kept, "terrain", "water", "shoreInk");
            n.RippleFromMetres = Num(root, n.RippleFromMetres, kept, "terrain", "water", "patternFromM");
            n.RippleFeatherMetres = Num(root, n.RippleFeatherMetres, kept, "terrain", "water", "patternFeatherM");
            n.RippleInk = Num(root, n.RippleInk, kept, "terrain", "water", "rippleInk");
            n.RippleInkWorld = Num(root, n.RippleInkWorld, kept, "terrain", "water", "rippleInkWorld");
            n.PatternMetres = Four(root, n.PatternMetres, "terrain", "patternPeriodM");

            // stroke atlas layout
            n.AtlasRowPx = Num(root, n.AtlasRowPx, kept, "strokeAtlas", "rowPx");
            n.AtlasRowPad = Num(root, n.AtlasRowPad, kept, "strokeAtlas", "marginPx");
            n.AtlasInkSpan = new Vector2(Num(root, n.AtlasInkSpan.x, kept, "strokeAtlas", "inkTop"), Num(root, n.AtlasInkSpan.y, kept, "strokeAtlas", "inkBottom"));
            var size = Json.List(root, "strokeAtlas", "size");
            if (size.Count == 2 && n.AtlasRowPx > 0f) n.AtlasRows = Mathf.Max(1, Mathf.RoundToInt((float)Json.AsNum(size[1], 512) / n.AtlasRowPx));
            var rows = Json.List(root, "strokeAtlas", "rows");
            if (rows.Count > 0)
                n.AtlasRowSpecs = rows.Select(r => new MapAtlasRow308((int)Json.Num(r, 0, "row"), Json.Text(r, "", "name"), (float)Json.Num(r, n.AtlasRowPx * .5f, "axisV"), Json.Flag(r, false, "knockout"),
                    Json.Text(r, "repeat", "uMode") == "stretch" ? 0f : (float)Json.Num(r, 1, "uAspect"))).ToList();
            else kept.Add("strokeAtlas.rows");

            // stroke classes (ridges: one entry per rank; a rank past minRankByBand is off in that band)
            var classes = Json.List(root, "strokeClasses");
            if (classes.Count > 0)
            {
                var defaults = MapNotation308SO.DefaultStrokes();
                var styles = new List<MapStrokeStyle308>();
                foreach (var c in classes)
                {
                    int cls = (int)Json.Num(c, 0, "class");
                    if (cls <= 0 || cls > 15) { log.Add("SKIPPED stroke class without a number: " + Json.Text(c, "?", "name")); continue; }
                    var limit = Four(c, Vector4.zero, "minRankByBand");
                    var ranks = Json.List(c, "ranks");
                    var entries = ranks.Count > 0 ? ranks : new List<object> { c };
                    foreach (var e in entries)
                    {
                        int rank = ranks.Count > 0 ? (int)Json.Num(e, 0, "rank") : 0;
                        var fallback = defaults.FirstOrDefault(d => d.Class == cls && d.Rank == rank) ?? defaults.FirstOrDefault(d => d.Class == cls);
                        float row = (float)Json.Num(c, 0, "atlasRow"), tile = (float)Json.Num(c, 0, "tileMetres");
                        var s = new MapStrokeStyle308
                        {
                            Class = cls, Rank = rank, Name = Json.Text(c, "", "nameKo"),
                            WidthPx = Four(e, Vector4.zero, "widthPx"),
                            AtlasRow = Four(e, new Vector4(row, row, row, row), "atlasRowByBand"),
                            TileMetres = Four(e, new Vector4(tile, tile, tile, tile), "tileMetresByBand"),
                            KnockoutPx = (float)Json.Num(c, 0, "knockoutPx"),
                            InkAlpha = (float)Json.Num(e, Json.Num(c, .85, "inkAlpha"), "inkAlpha"),
                            PhysicalWidthBelowPx = (float)Json.Num(c, 0, "drawWhenPhysicalWidthPxBelow"),
                            CapScale = fallback != null ? fallback.CapScale : .35f,
                        };
                        if (rank > 0)
                        {
                            if (limit.x > 0f && rank > limit.x) s.WidthPx.x = 0f;
                            if (limit.y > 0f && rank > limit.y) s.WidthPx.y = 0f;
                            if (limit.z > 0f && rank > limit.z) s.WidthPx.z = 0f;
                            if (limit.w > 0f && rank > limit.w) s.WidthPx.w = 0f;
                        }
                        if (Json.Text(c, "", "uMode") == "stretch") s.TileMetres = Vector4.zero;
                        styles.Add(s);
                    }
                }
                n.StrokeStyles = styles;
            }
            else kept.Add("strokeClasses");

            // states
            n.States = Json.List(root, "states").Select(s => new MapState308 { Id = Json.Text(s, "", "id"), Kind = Json.Text(s, "shortcut", "kind"), Key = Json.Text(s, "", "key") }).ToList();

            // glyphs and the tables that pick them
            var glyphs = Json.List(root, "glyphs");
            if (glyphs.Count > 0) n.Glyphs = glyphs.Select(g => new MapGlyph308 { Id = Json.Text(g, "", "id"), Cell = (int)Json.Num(g, -1, "cell"), NameKo = Json.Text(g, "", "nameKo") }).ToList();
            else kept.Add("glyphs");
            n.MarkerGlyphs = Json.Map(root, "markerGlyphs").Where(kv => kv.Value is string).Select(kv => new MapGlyphLink308(kv.Key, (string)kv.Value)).OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
            var kinds = Json.Map(root, "kindFallback");
            if (kinds.Count == 0) kinds = Json.Map(root, "markerGlyphs", "kindFallback");
            n.KindGlyphs = kinds.Where(kv => kv.Value is string).Select(kv => new MapGlyphLink308(kv.Key, (string)kv.Value)).OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
            var priority = Json.List(root, "labelPriority").OfType<string>().ToList();
            if (priority.Count > 0) n.LabelPriority = priority;
            // the pre-#308 word guess stays as the last resort, on the bundle's own glyph ids
            n.WordGlyphs = new List<MapGlyphLink308>();
            foreach (var pair in new[] { ("굴", "cave"), ("광", "mine"), ("혈", "cave"), ("산", "peak"), ("봉", "peak"), ("령", "peak"), ("고개", "peak") })
                if (n.TryCell(pair.Item2, out _)) n.WordGlyphs.Add(new MapGlyphLink308(pair.Item1, pair.Item2));

            // #308 map 3c: when a marker without an arrival event first shows (RevealDataFile; RevealProblems has already refused a
            // file that names an unknown rule or carries a number out of range). A file that is there and carries no row empties the
            // table (the switch). NO file: the asset keeps its table and numbers - the stage folder is not under git, and its absence
            // must not quietly bring first contact back
            var reveal = RevealData(out string revealNote);
            if (reveal.Count > 0)
            {
                n.MarkerRevealDefault = RevealName(Json.Text(reveal, "FirstContact", "default"), out _);
                n.MarkerReveal = Json.Map(reveal, "rules").OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new MapMarkerRevealRow308 { MarkerId = kv.Key, Reveal = RevealName(Json.Text(kv.Value, "", "reveal"), out _), RadiusMetres = (float)Json.Num(kv.Value, 0, "radiusM") }).ToList();
                n.GroundRadiusMetres = (float)Json.Num(reveal, n.GroundRadiusMetres, "groundDrawn", "radiusM");
                n.GroundStepMetres = (float)Json.Num(reveal, n.GroundStepMetres, "groundDrawn", "stepM");
                n.GroundNeedLand = (float)Json.Num(reveal, n.GroundNeedLand, "groundDrawn", "needLand");
                n.GroundKCover = (float)Json.Num(reveal, n.GroundKCover, "groundDrawn", "kCover");
                log.Add("REVEAL " + revealNote);
            }
            else log.Add("REVEAL KEPT " + RevealKept(n) + " - " + revealNote);

            // sizes (the sheet keeps MapStyle304SO's)
            n.MiniIconPx = Num(root, n.MiniIconPx, kept, "sizes", "minimap", "icon");
            n.MiniPlayerPx = Num(root, n.MiniPlayerPx, kept, "sizes", "minimap", "player");
            n.MiniPinPx = Num(root, n.MiniPinPx, kept, "sizes", "minimap", "pin");
            n.MiniCoinPx = Num(root, n.MiniCoinPx, kept, "sizes", "minimap", "coin");
            n.MiniEdgePx = Num(root, n.MiniEdgePx, kept, "sizes", "minimap", "edgeMargin");
            n.WakeRingScale = Num(root, n.WakeRingScale, kept, "sizes", "minimap", "wakeRingScale");

            // legend: the terrain-and-roads cells
            var legend = new List<MapLegendCell308>();
            foreach (var cell in Json.List(root, "legend"))
            {
                if (Json.Text(cell, "", "group") != "terrain") continue;
                string name = Json.Text(cell, "", "name"), pattern = Json.Text(cell, "", "pattern");
                if (pattern == "r") legend.Add(new MapLegendCell308(MapLegendKind308.Forest, 0, 0, name));
                else if (pattern == "g") legend.Add(new MapLegendCell308(MapLegendKind308.Water, 0, 0, name));
                else if (Json.At(cell, "strokeClass") != null)
                    legend.Add(new MapLegendCell308(MapLegendKind308.Stroke, (int)Json.Num(cell, 0, "strokeClass"), (int)Json.Num(cell, 0, "rank"), name, (int)Json.Num(cell, 0, "over")));
                else if (Json.At(cell, "glyph") != null) legend.Add(new MapLegendCell308(MapLegendKind308.Glyph, 0, 0, name) { Glyph = Json.Text(cell, "", "glyph") });
            }
            if (legend.Count > 0) n.Legend = legend; else kept.Add("legend (terrain group)");

            // frames: geometry from the notation, sprites from the theme kit atlas
            n.MiniFrameWidth = Num(root, n.MiniFrameWidth, kept, "frames", "minimap", "frameWidth");
            var board = Json.List(root, "frames", "board", "rect");
            if (board.Count == 4)
            {
                float x0 = (float)Json.AsNum(board[0], 542), y0 = (float)Json.AsNum(board[1], 126), x1 = (float)Json.AsNum(board[2], 1378), y1 = (float)Json.AsNum(board[3], 1036);
                n.BoardRect = new Rect(x0, y0, x1 - x0, y1 - y0);
            }
            else kept.Add("frames.board.rect");
            n.BoardBand = Num(root, n.BoardBand, kept, "frames", "board", "margin");
            KitSprites(n, root, log);
        }

        // ------------------------------------------------------------------ theme kit cells -> the notation's frame sprites
        static string KitNote()
        {
            if (!File.Exists(Harness303.Abs(KitAtlasPath)))
                return "KIT theme atlas NOT imported (" + KitAtlasPath + "): the maps draw without frame, north piece, plates and inlay lines until Stage308_theme is deployed and 'apply' runs again";
            // read only: this command never writes under Theme/. A kit atlas left on Unity's default import (compressed, sRGB as
            // found, NPOT scaled) would draw the frames with block artefacts; the theme kit's own setup must set these.
            var importer = AssetImporter.GetAtPath(KitAtlasPath) as TextureImporter;
            var off = new List<string>();
            if (importer != null)
            {
                if (!importer.sRGBTexture) off.Add("sRGB off");
                if (importer.textureCompression != TextureImporterCompression.Uncompressed) off.Add("compression " + importer.textureCompression);
                if (importer.npotScale != TextureImporterNPOTScale.None) off.Add("npot " + importer.npotScale);
                if (!importer.alphaIsTransparency) off.Add("alphaIsTransparency off");
                if (importer.wrapMode != TextureWrapMode.Clamp) off.Add("wrap " + importer.wrapMode);
                if (importer.maxTextureSize < 512) off.Add("max size " + importer.maxTextureSize);
            }
            return "KIT theme atlas present: " + KitAtlasPath + (off.Count == 0 ? "" : " - WARNING its import differs from theme308_atlas.json import_settings (" + string.Join(", ", off)
                + "): the theme kit's setup must fix it before the frames are judged");
        }

        /// <summary>Frame.Mini / Frame.MapBoard / Piece.North / Plate.Icon / Inlay.Line from the theme kit atlas (D308-15: the map makes
        /// no frames of its own). The kit's own named sub-sprites are used when its importer has sliced the atlas; otherwise the cells
        /// are cut from the atlas texture by theme308_atlas.json and stored as sub-assets of the notation asset (they reference the kit's
        /// texture: no copy of the art, nothing written under Theme/).</summary>
        static void KitSprites(MapNotation308SO n, Dictionary<string, object> root, List<string> log)
        {
            var own = AssetDatabase.LoadAllAssetsAtPath(NotationPath).OfType<Sprite>().ToList();
            var used = new HashSet<Sprite>();
            void DropUnused()
            {
                foreach (var old in own) if (!used.Contains(old)) { AssetDatabase.RemoveObjectFromAsset(old); Object.DestroyImmediate(old, true); }
            }
            n.FrameMini = n.FrameBoard = n.NorthPiece = n.PlateIcon = n.InlayLine = null;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(KitAtlasPath);
            if (texture == null) { DropUnused(); log.Add("FRAMES none: " + KitAtlasPath + " is not imported yet"); return; }
            string jsonFile = File.Exists(Harness303.Abs(KitJsonPath)) ? Harness303.Abs(KitJsonPath) : Path.Combine(Harness303.RepoRoot, KitStageJson.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(jsonFile)) { DropUnused(); log.Add("FRAMES none: theme308_atlas.json not found next to the atlas or in the theme stage"); return; }
            var kit = Json.Parse(File.ReadAllText(jsonFile, Encoding.UTF8)) as Dictionary<string, object>;
            float ppu = (float)Json.Num(kit, 200, "import_settings", "pixelsPerUnit");
            var named = AssetDatabase.LoadAllAssetsAtPath(KitAtlasPath).OfType<Sprite>().GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.First());
            int cut = 0;
            Sprite Cell(string slot, string fallbackCell)
            {
                string cell = Json.Text(root, fallbackCell, "frames", "slots", slot, "kitCell");
                if (named.TryGetValue(cell, out var sliced)) return sliced;
                var spec = Json.List(kit, "cells").FirstOrDefault(c => Json.Text(c, "", "name") == cell);
                if (spec == null) { log.Add("FRAMES kit cell missing: " + cell + " (" + slot + ")"); return null; }
                var rect = Json.List(spec, "rect_unity"); var border = Json.List(spec, "border_texels");
                if (rect.Count != 4) { log.Add("FRAMES kit cell without rect_unity: " + cell); return null; }
                var r = new Rect((float)Json.AsNum(rect[0], 0), (float)Json.AsNum(rect[1], 0), (float)Json.AsNum(rect[2], 1), (float)Json.AsNum(rect[3], 1));
                if (r.xMax > texture.width + .5f || r.yMax > texture.height + .5f) { log.Add("FRAMES kit cell outside the imported atlas (" + texture.width + " x " + texture.height + "): " + cell); return null; }
                var bd = border.Count == 4 ? new Vector4((float)Json.AsNum(border[0], 0), (float)Json.AsNum(border[1], 0), (float)Json.AsNum(border[2], 0), (float)Json.AsNum(border[3], 0)) : Vector4.zero;
                string name = "map308_" + cell;
                // the same cell from an earlier import is kept (a second run of the same input writes nothing)
                var kept = own.FirstOrDefault(x => x != null && x.name == name && x.texture == texture && x.rect == r && x.border == bd && Mathf.Approximately(x.pixelsPerUnit, ppu));
                if (kept != null) { used.Add(kept); return kept; }
                var sprite = Sprite.Create(texture, r, new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect, bd);
                sprite.name = name;
                AssetDatabase.AddObjectToAsset(sprite, n);
                used.Add(sprite); cut++;
                return sprite;
            }
            n.FrameMini = Cell("Frame.Mini", "frame_mini");
            n.FrameBoard = Cell("Frame.MapBoard", "frame_board");
            n.NorthPiece = Cell("Piece.North", "piece_north");
            n.PlateIcon = Cell("Plate.Icon", "plate_lacquer");
            n.InlayLine = Cell("Inlay.Line", "line_najeon_thin");
            DropUnused();
            if (n.NorthPiece != null)
            {
                // the kit cell: its bottom sits 7 px inside the outer edge of the frame (the seat ends on the frame line)
                n.MiniNorthPx = n.NorthPiece.rect.height * 100f / n.NorthPiece.pixelsPerUnit;
                n.MiniNorthOffset = n.MiniFrameWidth - 7f + n.MiniNorthPx * .5f;
            }
            log.Add("FRAMES kit: mini " + Name(n.FrameMini) + ", board " + Name(n.FrameBoard) + ", north " + Name(n.NorthPiece) + ", plate " + Name(n.PlateIcon) + ", inlay " + Name(n.InlayLine)
                + (named.Count > 0 ? " (sprites sliced by the kit)" : " (cells cut from the kit atlas into the notation asset: " + cut + " new)"));
        }

        static string Name(Object o) => o != null ? o.name : "MISSING";

        // ------------------------------------------------------------------ revert
        static string Revert(bool files)
        {
            var log = new List<string>();
            var ledger = ReadLedger() ?? new Ledger { created = DateTime.UtcNow.ToString("O") };
            string backup = BackupDir("_revert");
            var style = AssetDatabase.LoadAssetAtPath<MapStyle304SO>(StylePath);
            if (style != null && style.Notation308 != null)
            {
                var e = Before(ledger, StylePath, backup, "MapStyle304.Notation308 cleared");
                style.Notation308 = null;
                EditorUtility.SetDirty(style); AssetDatabase.SaveAssetIfDirty(style);
                After(e);
                log.Add("CLEARED MapStyle304.Notation308 (the pre-#308 map draws from the next Play)");
            }
            else log.Add("변경 없음 MapStyle304.Notation308 is already empty");
            if (files)
            {
                var seen = new HashSet<string>();
                // oldest entry of each path: what the path was BEFORE the first write of this ledger is what comes back
                foreach (var e in ledger.entries.Where(x => x.asset != StylePath).ToList())
                {
                    if (!seen.Add(e.asset)) continue;
                    bool meta = e.asset.EndsWith(".meta", StringComparison.Ordinal);
                    if (meta && !File.Exists(Harness303.Abs(e.asset.Substring(0, e.asset.Length - 5)))) continue;   // its asset was created here and is gone again
                    Guard(e.asset.EndsWith(".meta", StringComparison.Ordinal) ? e.asset.Substring(0, e.asset.Length - 5) : e.asset);
                    string abs = Harness303.Abs(e.asset);
                    if (e.created)
                    {
                        if (!File.Exists(abs)) continue;
                        Directory.CreateDirectory(backup);
                        File.Copy(abs, Path.Combine(backup, Path.GetFileName(e.asset)), true);
                        if (e.asset.EndsWith(".meta", StringComparison.Ordinal)) continue;   // goes with its asset
                        AssetDatabase.DeleteAsset(e.asset);
                        log.Add("REMOVED " + e.asset);
                    }
                    else if (!string.IsNullOrEmpty(e.backup) && File.Exists(e.backup))
                    {
                        File.Copy(e.backup, abs, true);
                        log.Add("RESTORED " + e.asset);
                    }
                }
                AssetDatabase.Refresh(ImportAssetOptions.Default);
                ledger.entries.Clear();
            }
            WriteLedger(ledger);
            log.Add("ledger " + LedgerFile + (Directory.Exists(backup) ? ", backups " + backup : ""));
            return string.Join("\n", log);
        }

        // ------------------------------------------------------------------ status / report / check
        static string Status()
        {
            var log = new List<string>();
            string folder = BundleFolder();
            if (File.Exists(Path.Combine(RepoAbs(folder), NotationFile)))
            {
                try
                {
                    var b = ReadBundle();
                    var problems = BundleProblems(b, out string sceneStage);
                    log.Add("STAGE bundle " + b.Folder + ": stage '" + b.Stage + "', contract " + b.Version + ", " + b.Outputs.Count + " files, scenes '" + sceneStage + "' - "
                        + (problems.Count == 0 ? "importable" : problems.Count + " problem(s): " + string.Join(" | ", problems))
                        + (b.Trusted.Count > 0 ? " - " + b.Trusted.Count + " recorded input(s) not on disk, taken on the bundle's record" : ""));
                    foreach (string line in b.Trusted.Concat(b.InputNotes)) log.Add("STAGE bundle " + line);   // #308 recovery
                }
                catch (Refuse r) { log.Add("STAGE bundle " + r.Message); }
            }
            else log.Add("STAGE bundle missing (" + folder + "/" + NotationFile + ")");
            log.Add("STAGE bundles staged: " + string.Join(", ", StagedBundles().Select(s => "'" + s.stage + "' " + s.folder)) + " (the one whose stage = the scenes' is read)");
            foreach (string shader in new[] { StrokeShaderPath, IconShaderPath })
            {
                var s = AssetDatabase.LoadAssetAtPath<Shader>(shader);
                log.Add("SHADER " + shader + " " + (s == null ? "missing" : ShaderUtil.ShaderHasError(s) ? "HAS ERRORS" : "ok"));
            }
            log.Add("SHADER include " + FogIncludePath + " " + (File.Exists(Harness303.Abs(FogIncludePath)) ? "ok" : "missing"));
            string paperFile = Harness303.Abs(PaperShaderPath);
            log.Add("SHADER " + PaperShaderPath + " " + (File.Exists(paperFile) && File.ReadAllText(paperFile).Contains("multi_compile_local _ _MAP308") ? "_MAP308 variant present" : "no _MAP308 variant (pre-#308 shader)"));
            var notation = AssetDatabase.LoadAssetAtPath<MapNotation308SO>(NotationPath);
            log.Add("NOTATION " + NotationPath + " " + (notation == null ? "missing (both maps draw the pre-#308 way)"
                : "stage '" + notation.Stage + "', baked for " + Name(notation.BakedFor) + ", usable " + notation.IsUsableFor(notation.BakedFor) + ", icons " + notation.HasIcons
                  + ", frames mini " + Name(notation.FrameMini) + " board " + Name(notation.FrameBoard)));
            var style = AssetDatabase.LoadAssetAtPath<MapStyle304SO>(StylePath);
            log.Add("STYLE " + StylePath + " " + (style == null ? "missing" : "Notation308 = " + (style.Notation308 == null ? "none (pre-#308 map)" : AssetDatabase.GetAssetPath(style.Notation308))));
            log.Add(KitNote());
            var scene = EditorSceneManager.GetActiveScene();
            log.Add("SCENE " + (scene.path.Length > 0 ? scene.path : scene.name) + " dirty=" + scene.isDirty);
            return string.Join("\n", log);
        }

        static string Report()
        {
            var n = AssetDatabase.LoadAssetAtPath<MapNotation308SO>(NotationPath);
            if (n == null) return "NOTATION missing " + NotationPath;
            var sb = new StringBuilder();
            sb.AppendLine("MapNotation308: contract " + n.Version + ", stage '" + n.Stage + "', baked for " + AssetDatabase.GetAssetPath(n.BakedFor));
            sb.AppendLine("zoom bands (m/px) <= " + F(n.ZoomBands.x) + " | " + F(n.ZoomBands.y) + " | " + F(n.ZoomBands.z) + " | above");
            sb.AppendLine("values paper #" + ColorUtility.ToHtmlStringRGB(n.Paper) + " unwalked #" + ColorUtility.ToHtmlStringRGB(n.Unknown) + " ink #" + ColorUtility.ToHtmlStringRGB(n.Ink)
                + " cinnabar #" + ColorUtility.ToHtmlStringRGB(n.Cinnabar) + " | crisp edge at " + F(n.EdgeThreshold.x) + " + " + F(n.EdgeThreshold.y) + " x noise (cells " + F(n.EdgeNoiseCells.x) + ", "
                + F(n.EdgeNoiseCells.y) + "), rim a" + F(n.EdgeRimAlpha) + " over the wash, " + F(n.EdgeRimPx) + " px inside"
                + " | sheet rim " + (n.SheetRimWhole > 0f ? "whole " + F(n.SheetRimWhole) + ", " + F(n.SheetRimPx) + " px a" + F(n.SheetRimAlpha) : "= the minimap's"));
            sb.AppendLine("terrain slope wash " + F(n.SlopeWash) + ", elevation wash " + F(n.ElevationWash) + ", forest from " + F(n.ForestShowFrom) + " ink " + F(n.ForestInk) + ", rock from " + F(n.RockFrom)
                + ", water range " + F(n.WaterRangeMetres) + " m shore " + F(n.ShorePx) + " px, pattern m " + n.PatternMetres);
            foreach (var s in n.StrokeStyles)
                sb.AppendLine("stroke " + s.Class + (s.Rank > 0 ? "." + s.Rank : "") + " " + s.Name + ": px " + s.WidthPx + " row " + s.AtlasRow + " tile m " + s.TileMetres + " ink " + F(s.InkAlpha)
                    + (s.KnockoutPx > 0 ? " paper +" + F(s.KnockoutPx) + " px" : "") + (s.PhysicalWidthBelowPx > 0 ? " only under " + F(s.PhysicalWidthBelowPx) + " px wide" : ""));
            sb.AppendLine("glyphs " + n.Glyphs.Count + ", marker links " + n.MarkerGlyphs.Count + ", kind links " + n.KindGlyphs.Count + ", states " + n.States.Count + ", legend cells " + n.Legend.Count);
            sb.AppendLine("marker reveal: default " + n.MarkerRevealDefault + ", rules " + (n.MarkerReveal.Count == 0 ? "none" : string.Join(", ", n.MarkerReveal.Select(r => r.MarkerId + " " + r.Reveal + (r.RadiusMetres > 0f ? " r " + F(r.RadiusMetres) + " m" : ""))))
                + " | ground drawn = land >= " + F(n.GroundNeedLand) + " within " + F(n.GroundRadiusMetres) + " m (points every " + F(n.GroundStepMetres) + " m, KCOVER " + F(n.GroundKCover) + ")");
            sb.AppendLine("minimap px: icon " + F(n.MiniIconPx) + " player " + F(n.MiniPlayerPx) + " pin " + F(n.MiniPinPx) + " north " + F(n.MiniNorthPx) + " edge " + F(n.MiniEdgePx) + " frame " + F(n.MiniFrameWidth));
            sb.Append("frames: mini " + Name(n.FrameMini) + ", board " + Name(n.FrameBoard) + " " + n.BoardRect + ", north " + Name(n.NorthPiece) + ", plate " + Name(n.PlateIcon) + ", inlay " + Name(n.InlayLine));
            return sb.ToString();
        }

        sealed class Checks
        {
            public readonly List<string> Lines = new List<string>(); public int Failed, Total;
            public void Add(bool ok, string label) { Total++; if (!ok) Failed++; Lines.Add((ok ? "ok      " : "FAILED  ") + label); }
            public string Finish(string name, string file)
            {
                string head = name + " " + (Failed == 0 ? "ok" : "FAILED") + " " + (Total - Failed) + "/" + Total;
                Directory.CreateDirectory(Out);
                File.WriteAllText(Path.Combine(Out, file), head + "\n" + string.Join("\n", Lines) + "\n", new UTF8Encoding(false));
                return head + "\n" + string.Join("\n", Lines);
            }
        }

        static string Check()
        {
            var c = new Checks();
            var n = AssetDatabase.LoadAssetAtPath<MapNotation308SO>(NotationPath);
            c.Add(n != null, "AC-E1 notation asset exists: " + NotationPath);
            if (n == null) return c.Finish("check", "checks-import.txt");
            // bundle = the stage bundle, file by file
            Bundle bundle = null; string why = "";
            try { bundle = ReadBundle(); } catch (Refuse r) { why = r.Message; }
            c.Add(bundle != null, "stage bundle readable" + (why.Length > 0 ? ": " + why : ""));
            if (bundle != null)
            {
                var problems = BundleProblems(bundle, out string sceneStage);
                c.Add(problems.Count == 0, "AC-E1 stage bundle is importable (version, files, input sha, stage '" + bundle.Stage + "' = scenes '" + sceneStage + "')" + (problems.Count > 0 ? ": " + string.Join(" | ", problems) : "")
                    + (bundle.Trusted.Count > 0 ? " - " + bundle.Trusted.Count + " recorded input(s) not on disk, taken on the bundle's record (Temporary Exception: lost bake inputs)" : ""));
                foreach (string line in bundle.Trusted.Concat(bundle.InputNotes)) c.Lines.Add("INFO    " + line);   // #308 recovery: not counted
                int same = 0;
                foreach (var o in bundle.Outputs) { string abs = Harness303.Abs(ArtFolder + "/" + o.file); if (File.Exists(abs) && SameSha(Harness303.Sha(abs), o.sha)) same++; }
                c.Add(same == bundle.Outputs.Count, "AC-E1 imported files = the notation's sha256: " + same + "/" + bundle.Outputs.Count);
                c.Add(n.Stage == bundle.Stage && n.Version == bundle.Version, "AC-E1 notation asset stage / contract = the bundle's: '" + n.Stage + "' / " + n.Version);
                // #308 map 4 (AC-M4.10): what apply wrote is what the bundle says - a swapped or defaulted rim number shows here, not in a picture
                bool sheetBlock = Json.At(bundle.Root, "values", "fogEdge", "sheet") != null;
                c.Add(RimAsset(n, sheetBlock) == RimBundle(bundle.Root), "AC-M4.10 the notation asset's rim numbers = the bundle's values.fogEdge: asset [" + RimAsset(n, sheetBlock) + "] bundle [" + RimBundle(bundle.Root) + "]"
                    + (sheetBlock ? "" : " (no sheet block in this bundle: the sheet draws the minimap's rim)"));
                string bakedPath = bundle.BakedFor.StartsWith("Oheangbu/", StringComparison.Ordinal) ? bundle.BakedFor.Substring("Oheangbu/".Length) : bundle.BakedFor;
                c.Add(n.BakedFor != null && AssetDatabase.GetAssetPath(n.BakedFor) == bakedPath, "AC-E1 BakedFor = " + bakedPath);
            }
            foreach (var rule in TextureRules)
            {
                var importer = AssetImporter.GetAtPath(ArtFolder + "/" + rule.File) as TextureImporter;
                string diff = importer == null ? "not imported" : ImporterDiff(importer, rule);
                c.Add(diff.Length == 0, "AC-E1 import settings of " + rule.File + (diff.Length > 0 ? ": " + diff : " = the contract"));
            }
            c.Add(n.Terrain != null && n.Pattern != null && n.StrokeAtlas != null && n.Strokes != null && n.RevealRegions != null && n.IconsL != null && n.IconsS != null,
                "notation references: terrain, pattern, stroke atlas, strokes, reveal regions, icons L / S");
            if (n.Strokes != null)
            {
                var parsed = MapStrokes308Data.Parse(n.Strokes.bytes, out string error);
                c.Add(parsed != null, "map308_strokes.bytes parses (magic, version, ranges, CRC32)" + (parsed != null ? ": " + parsed.StrokeCount + " strokes, " + parsed.PointCount + " points" : ": " + error));
                if (parsed != null && n.BakedFor != null)
                    c.Add((parsed.Min - n.BakedFor.BoundsMin).sqrMagnitude < .01f && (parsed.Max - n.BakedFor.BoundsMax).sqrMagnitude < .01f, "strips' bounds = the map's bounds " + n.BakedFor.BoundsMin + " - " + n.BakedFor.BoundsMax);
            }
            if (n.RevealRegions != null) c.Add(n.RevealRegions.bytes.Length == 125 * 188, "reveal regions are 125 x 188 bytes: " + n.RevealRegions.bytes.Length);
            c.Add(n.BakedFor != null && n.IsUsableFor(n.BakedFor), "the bundle is usable for its map (IsUsableFor)");
            var compact = AssetDatabase.FindAssets("t:WorldMapBakedDataSO").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>).Where(d => d != null && d != n.BakedFor).ToList();
            c.Add(compact.All(d => !n.IsUsableFor(d)), "AC-E5 every other map data (" + compact.Count + ", the protected W_Demo_Compact's among them) keeps the pre-#308 path");
            var style = AssetDatabase.LoadAssetAtPath<MapStyle304SO>(StylePath);
            c.Add(style != null && style.Notation308 == n, "MapStyle304.Notation308 -> the notation asset");
            c.Add(Resources.Load<MapStyle304SO>(MapStyle304SO.ResourcePath) == style, "MapStyle304 is the Resources asset the runtime resolves");
            // shaders
            foreach (string path in new[] { StrokeShaderPath, IconShaderPath, PaperShaderPath })
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                bool ok = shader != null && !ShaderUtil.ShaderHasError(shader);
                int messages = shader != null ? ShaderUtil.GetShaderMessageCount(shader) : -1;
                c.Add(ok && messages == 0, "AC-E3 " + Path.GetFileName(path) + " compiles with no error or warning (messages " + messages + ")");
                if (shader == null) continue;
                string text = File.ReadAllText(Harness303.Abs(path));
                bool hdr = false;
                for (int i = 0; i < shader.GetPropertyCount(); i++) if ((shader.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.HDR) != 0) hdr = true;
                c.Add(!hdr && !text.Contains("_Emission") && !text.Contains("[HDR]"), "AC-O8 " + Path.GetFileName(path) + ": no HDR property, no emission");
            }
            string paperText = File.Exists(Harness303.Abs(PaperShaderPath)) ? File.ReadAllText(Harness303.Abs(PaperShaderPath)) : "";
            c.Add(paperText.Contains("#pragma multi_compile_local _ _MAP308") && paperText.Contains("#pragma multi_compile_local _ _MINI_HUD"), "AC-E3 _MAP308 and _MINI_HUD are multi_compile keywords (not stripped from builds)");
            c.Add(paperText.Contains("MapFog308.cginc") && File.Exists(Harness303.Abs(FogIncludePath)), "the paper and the strips share MapFog308.cginc");
            string fogText = File.Exists(Harness303.Abs(FogIncludePath)) ? File.ReadAllText(Harness303.Abs(FogIncludePath)) : "", strokeText = File.Exists(Harness303.Abs(StrokeShaderPath)) ? File.ReadAllText(Harness303.Abs(StrokeShaderPath)) : "";
            c.Add(fogText.Contains("MapFog308_Edge") && paperText.Contains("MapFog308_Edge(worldUv,_M308Edge)") && strokeText.Contains("MapFog308_Edge(worldUv,_S308Edge)"),
                "the paper's _MAP308 branch and the strips draw the same crisp walked edge (MapFog308_Edge)");
            c.Add(n.EdgeThreshold.x > 0f && n.EdgeThreshold.x + n.EdgeThreshold.y < 1f, "#214 the crisp edge stays inside the walked cells: threshold " + F(n.EdgeThreshold.x) + " > 0, + span " + F(n.EdgeThreshold.y) + " < 1");
            string iconText = File.Exists(Harness303.Abs(IconShaderPath)) ? File.ReadAllText(Harness303.Abs(IconShaderPath)) : "";
            c.Add(iconText.Contains("_Ink2Chan") && iconText.Contains("_Ink2("), "the icon shader draws the second ink layer (B channel: the brush mark's ferrule and handle)");
            // LDR tokens
            float max = 0f;
            foreach (var colour in new[] { n.Paper, n.Unknown, n.Ink, n.Cinnabar, n.Nacre, n.Lacquer }) max = Mathf.Max(max, colour.maxColorComponent);
            c.Add(max <= 1f && n.Nacre.maxColorComponent <= .82f, "AC-O8 colours are LDR (max channel " + F(max) + "), nacre <= .82 (" + F(n.Nacre.maxColorComponent) + ")");
            // AC-E4 texture memory. #308 map fix (M4): the gate is the GPU size of each texture, computed from its imported
            // format, size and mip count (block size x blocks per mip). Profiler.GetRuntimeMemorySizeLong in the editor also
            // counts the CPU copy the editor keeps of every texture (about twice the GPU size), so it is printed, not gated.
            long bytes = 0, editorBytes = 0; var parts = new List<string>();
            foreach (var t in new Texture[] { n.Terrain, n.Pattern, n.StrokeAtlas, n.IconsL, n.IconsS })
            {
                if (t == null) continue;
                long gpu = GpuBytes(t);
                bytes += gpu; editorBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                parts.Add(t.name + " " + F(gpu / 1048576f, "0.00") + " " + t.graphicsFormat + " " + t.width + "x" + t.height + " mips " + t.mipmapCount);
            }
            c.Add(n.Terrain == null || UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(n.Terrain.graphicsFormat),
                "AC-E4 the terrain picture is block compressed: " + (n.Terrain != null ? n.Terrain.graphicsFormat + " " + n.Terrain.width + "x" + n.Terrain.height + " mips " + n.Terrain.mipmapCount : "none"));
            c.Add(bytes <= 8L * 1048576L, "AC-E4 bundle textures on the GPU " + F(bytes / 1048576f, "0.00") + " MB <= 8 MB (" + string.Join(", ", parts)
                + "; computed from format x size x mips. Profiler.GetRuntimeMemorySizeLong in the editor = " + F(editorBytes / 1048576f, "0.00") + " MB, which includes the editor's CPU copies)");
            // AC-E5 protected paths: nothing this ledger wrote is protected, and the protected files still have their bytes
            var ledger = ReadLedger();
            c.Add(ledger == null || ledger.entries.All(e => !Harness303.IsProtected(e.asset)), "AC-E5 the import ledger holds no protected path (" + (ledger != null ? ledger.entries.Count : 0) + " entries)");
            c.Add(ledger == null || ledger.entries.All(e => !e.asset.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && !e.asset.EndsWith("_Map.asset", StringComparison.Ordinal)), "no scene and no Map.asset in the ledger");
            c.Lines.Add("INFO    AC-E2 (PersistentMinimap / MiniRoot / TwiceFoldedHanji / PrintedMapWindow / MapInput, no Mask) is built at run time: preview reports the hierarchy; Play checks it live");
            c.Lines.Add(KitNote());
            return c.Finish("check", "checks-import.txt");
        }

        // ------------------------------------------------------------------ #308 map 3c: marker reveal rules (read only)
        static MapMarkerReveal308 RevealName(string name, out bool known)
        {
            known = true;
            switch (name)
            {
                case "FirstContact": return MapMarkerReveal308.FirstContact;
                case "GroundDrawn": return MapMarkerReveal308.GroundDrawn;
                case "OnArrival": return MapMarkerReveal308.OnArrival;
                default: known = false; return MapMarkerReveal308.FirstContact;
            }
        }

        /// <summary>What apply leaves in place when there is no data file: the asset's own table, in one line.</summary>
        static string RevealKept(MapNotation308SO n)
        {
            if (n == null) return "nothing (no notation asset yet: no rule row, every marker without an arrival event by its cell)";
            var rows = n.MarkerReveal ?? new List<MapMarkerRevealRow308>();
            return "the notation asset's table as it is: default " + n.MarkerRevealDefault + ", " + rows.Count + " rule row(s)"
                + (rows.Count > 0 ? " (" + string.Join(", ", rows.OrderBy(r => r.MarkerId, StringComparer.Ordinal).Select(r => r.MarkerId + " " + r.Reveal)) + ")" : "")
                + "; ground drawn = land >= " + F(n.GroundNeedLand) + " within " + F(n.GroundRadiusMetres) + " m";
        }

        /// <summary>The marker reveal data apply reads (RevealDataFile), or an empty map when the file is not there. note = one line
        /// for the log. A file that does not parse comes back empty with the reason (RevealProblems refuses it).</summary>
        static Dictionary<string, object> RevealData(out string note)
        {
            string file = RepoAbs(RevealDataFile);
            if (!File.Exists(file)) { note = "NO DATA FILE (" + RevealDataFile + "): put the folder back as sealed; to switch the rule off use \"enabled\": false in its input, not a missing file"; return new Dictionary<string, object>(); }
            Dictionary<string, object> root = null;
            try { root = Json.Parse(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>; } catch (Exception e) { note = "data file does not parse (" + RevealDataFile + "): " + e.Message; return new Dictionary<string, object>(); }
            if (root == null) { note = "data file is not a JSON object (" + RevealDataFile + ")"; return new Dictionary<string, object>(); }
            var rules = Json.Map(root, "rules");
            note = "data " + RevealDataFile + " sha " + Short(Harness303.Sha(file)) + ": default " + Json.Text(root, "?", "default") + ", " + rules.Count + " rule(s)"
                + (rules.Count > 0 ? " (" + string.Join(", ", rules.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + " " + Json.Text(kv.Value, "?", "reveal"))) + ")" : "")
                + "; ground drawn = land >= " + F((float)Json.Num(root, 0, "groundDrawn", "needLand")) + " within " + F((float)Json.Num(root, 0, "groundDrawn", "radiusM")) + " m";
            return root;
        }

        /// <summary>The marker reveal data file: every reason the import must not read it. No file at all does not refuse the
        /// import (the asset then keeps its table: Fill).</summary>
        static void RevealProblems(List<string> problems)
        {
            if (!File.Exists(RepoAbs(RevealDataFile))) return;
            var reveal = RevealData(out string note);
            if (reveal.Count == 0) { problems.Add("marker reveal " + note); return; }
            string where = Path.GetFileName(RevealDataFile) + ": ";
            RevealName(Json.Text(reveal, "", "default"), out bool known);
            if (!known) problems.Add(where + "default is not FirstContact | GroundDrawn | OnArrival: '" + Json.Text(reveal, "", "default") + "'");
            bool ground = Json.Text(reveal, "", "default") == "GroundDrawn";
            foreach (var kv in Json.Map(reveal, "rules"))
            {
                string name = Json.Text(kv.Value, "", "reveal");
                RevealName(name, out known);
                if (!known) problems.Add(where + "rules." + kv.Key + ".reveal is not FirstContact | GroundDrawn | OnArrival: '" + name + "'");
                if (Json.Num(kv.Value, 0, "radiusM") < 0) problems.Add(where + "rules." + kv.Key + ".radiusM is negative");
                ground |= name == "GroundDrawn";
            }
            if (!ground) return;
            double need = Json.Num(reveal, -1, "groundDrawn", "needLand"), cap = Json.Num(reveal, .98, "groundDrawn", "edge", "needCap");
            if (!(need > 0) || need > cap + 1e-6 || cap > .98 + 1e-6) problems.Add(where + "groundDrawn.needLand " + F((float)need) + " is not in (0, " + F((float)cap) + "] (a point whose nine cells are walked reads land .9995 in float: a threshold above .98 could never pass)");
            // the numbers the threshold's budget was made for (review F6): a radius of 0 would read the anchor alone, a step wider than
            // the radius only the anchor and eight rim points - both would show a mark over unwalked ground instead of hiding it
            double radius = Json.Num(reveal, -1, "groundDrawn", "radiusM"), step = Json.Num(reveal, -1, "groundDrawn", "stepM");
            double stepMax = Json.Num(reveal, 1, "groundDrawn", "stepMaxM"), minRadius = Json.Num(reveal, 0, "groundDrawn", "minRadiusM");
            bool small = Json.Flag(reveal, false, "groundDrawn", "allowRadiusBelowGlyph");
            if (!(radius > 0)) problems.Add(where + "groundDrawn.radiusM is missing or not positive (a radius of 0 reads the anchor alone)");
            else if (!small && radius < minRadius - 1e-6) problems.Add(where + "groundDrawn.radiusM " + F((float)radius) + " m is under the minimap glyph + its anti-aliasing (" + F((float)minRadius, "0.###") + " m)");
            if (!(step > 0) || step > stepMax + 1e-9) problems.Add(where + "groundDrawn.stepM " + F((float)step) + " is not in (0, " + F((float)stepMax) + "] (the threshold's margin is made for points about half a metre apart)");
            foreach (var kv in Json.Map(reveal, "rules"))
            {
                double own = Json.Num(kv.Value, 0, "radiusM");
                if (Json.Text(kv.Value, "", "reveal") == "GroundDrawn" && own > 0 && !small && own < minRadius - 1e-6)
                    problems.Add(where + "rules." + kv.Key + ".radiusM " + F((float)own) + " m is under the minimap glyph + its anti-aliasing (" + F((float)minRadius, "0.###") + " m)");
            }
            if (!(Json.Num(reveal, -1, "groundDrawn", "kCover") > 0)) problems.Add(where + "groundDrawn.kCover is missing or not positive");
        }

        /// <summary>`#define MAPFOG308_E_&lt;name&gt; &lt;number&gt;` of the live MapFog308.cginc; false when the line is not there.</summary>
        static bool FogDefine(string text, string name, out float value)
        {
            value = 0f;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^[ \t]*#define[ \t]+MAPFOG308_E_" + name + @"[ \t]+([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)", System.Text.RegularExpressions.RegexOptions.Multiline);
            return m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>A fog texel array (alpha 0 = walked) as WorldMapPresenter.RefreshFog writes it WITH a bundle: every cell that is
        /// not walked is fog, also the cells nobody can ever walk.</summary>
        static Color32[] RevealFog(WorldMapDiscoveryGrid grid)
        {
            var fog = new Color32[grid.Width * grid.Height];
            for (int y = 0; y < grid.Height; y++) for (int x = 0; x < grid.Width; x++)
                fog[y * grid.Width + x] = grid.IsDiscovered(x, y) ? new Color32(0, 0, 0, 0) : new Color32(255, 255, 255, 255);
            return fog;
        }

        static string Reveal()
        {
            var c = new Checks();
            var n = AssetDatabase.LoadAssetAtPath<MapNotation308SO>(NotationPath);
            c.Add(n != null, "notation asset exists: " + NotationPath);
            if (n == null) return c.Finish("reveal", "checks-reveal.txt");
            var data = n.BakedFor;
            c.Add(data != null && data.IsUsable, "the notation's map data (BakedFor) is usable");
            if (data == null || !data.IsUsable) return c.Finish("reveal", "checks-reveal.txt");
            var rows = n.MarkerReveal ?? new List<MapMarkerRevealRow308>();
            c.Lines.Add("INFO    default " + n.MarkerRevealDefault + ", " + rows.Count + " rule row(s); ground drawn = land >= " + F(n.GroundNeedLand) + " within " + F(n.GroundRadiusMetres)
                + " m, points every " + F(n.GroundStepMetres) + " m, KCOVER " + F(n.GroundKCover));

            // 0. the data file is there, and the asset carries what it says (apply was run after the file last changed).
            // A missing file is NOT repaired by apply (apply keeps the asset's table): the folder has to come back
            var file = RevealData(out string fileNote);
            c.Lines.Add("INFO    " + fileNote);
            c.Add(file.Count > 0, "the marker reveal data file is there and reads: " + RevealDataFile + (file.Count > 0 ? "" : " - put Tools/Unity/Stage308_map3c/Data back as sealed"
                + " (python Tools/Unity/Stage308_map3c/map3c_copy.py lists what is missing); `apply` does not repair this, it keeps " + RevealKept(n)));
            var wantRows = Json.Map(file, "rules").OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + " " + Json.Text(kv.Value, "", "reveal") + " " + F((float)Json.Num(kv.Value, 0, "radiusM"))).ToList();
            var haveRows = rows.OrderBy(r => r.MarkerId, StringComparer.Ordinal).Select(r => r.MarkerId + " " + r.Reveal + " " + F(r.RadiusMetres)).ToList();
            bool sameFile = n.MarkerRevealDefault.ToString() == Json.Text(file, "FirstContact", "default") && wantRows.SequenceEqual(haveRows)
                && (file.Count == 0 || (Mathf.Abs(n.GroundRadiusMetres - (float)Json.Num(file, -1, "groundDrawn", "radiusM")) < 1e-5f && Mathf.Abs(n.GroundStepMetres - (float)Json.Num(file, -1, "groundDrawn", "stepM")) < 1e-5f
                    && Mathf.Abs(n.GroundNeedLand - (float)Json.Num(file, -1, "groundDrawn", "needLand")) < 1e-5f && Mathf.Abs(n.GroundKCover - (float)Json.Num(file, -1, "groundDrawn", "kCover")) < 1e-5f));
            sameFile &= file.Count > 0;
            c.Add(sameFile, "the notation asset carries the data file's table and numbers" + (sameFile ? "" : file.Count == 0 ? ": cannot be compared without the data file (the asset has " + (haveRows.Count == 0 ? "no row" : string.Join(", ", haveRows)) + ")"
                : ": run apply (the asset has " + (haveRows.Count == 0 ? "no row" : string.Join(", ", haveRows)) + ")"));

            // 1. the table against Map.asset and the location catalogue
            var markers = (data.Markers ?? Array.Empty<WorldMapMarkerSpec>()).Where(m => m != null).ToList();
            var walkers = markers.Where(m => !m.RequiresArrival && !m.InitiallyDiscovered).ToList();
            c.Lines.Add("INFO    markers without an arrival event (the walk test decides): " + (walkers.Count == 0 ? "none" : string.Join(", ", walkers.Select(m => m.Id + " " + n.RevealOf(m.Id, out _)))));
            c.Add(rows.Select(r => r.MarkerId).Distinct().Count() == rows.Count, "no marker has two rule rows");
            var located = new HashSet<string>(data.Locations != null && data.Locations.Entries != null ? data.Locations.Entries.Where(e => e != null && !string.IsNullOrEmpty(e.MarkerId)).Select(e => e.MarkerId) : Enumerable.Empty<string>());
            var ground = new List<(WorldMapMarkerSpec marker, float radius)>();
            foreach (var row in rows)
            {
                var marker = markers.FirstOrDefault(m => m.Id == row.MarkerId);
                if (marker == null) { c.Add(false, "rule row '" + row.MarkerId + "' names a marker of " + data.name); continue; }
                if (row.Reveal == MapMarkerReveal308.OnArrival)
                    c.Add(located.Contains(marker.Id), "rule " + marker.Id + " OnArrival: the location catalogue has an arrival entry for it (else it would never show)");
                else
                    c.Add(!marker.RequiresArrival, "rule " + marker.Id + " " + row.Reveal + ": the marker has no arrival event (a row for an arrival marker has no effect)");
                if (row.Reveal == MapMarkerReveal308.GroundDrawn && !marker.RequiresArrival) { n.RevealOf(marker.Id, out float radius); ground.Add((marker, radius)); }
            }
            if (n.MarkerRevealDefault == MapMarkerReveal308.GroundDrawn)
                foreach (var marker in walkers) if (rows.All(r => r.MarkerId != marker.Id)) ground.Add((marker, n.GroundRadiusMetres));

            // 2. the numbers
            float stepMax = (float)Json.Num(file, 1, "groundDrawn", "stepMaxM");
            c.Add(n.GroundNeedLand > 0f && n.GroundNeedLand <= .98f + 1e-6f && n.GroundStepMetres > 0f && n.GroundStepMetres <= stepMax + 1e-6f && n.GroundKCover > 0f && n.GroundRadiusMetres > 0f,
                "ground rule numbers: 0 < need " + F(n.GroundNeedLand) + " <= .98, 0 < step " + F(n.GroundStepMetres) + " m <= " + F(stepMax) + ", KCOVER " + F(n.GroundKCover) + " > 0, radius " + F(n.GroundRadiusMetres) + " m > 0");
            // the HUD minimap's glyph at any turn of the follow view, plus one px, in metres (the scene's HUD when one is open)
            var hud = Object.FindObjectsByType<HudController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(h => h.gameObject.scene == SceneManager.GetActiveScene());
            var spec = hud != null && hud.Minimap != null ? hud.Minimap : new MinimapSpec304();
            float windowH = spec.Diameter * Mathf.Clamp(spec.Window, .2f, 1f) / Mathf.Clamp(spec.Aspect, .5f, 3f);
            float glyphMetres = (n.MiniIconPx * .70710678f + 1f) * 2f * Mathf.Max(1f, spec.OutsideMetres) / Mathf.Max(1f, windowH);
            foreach (var g in ground.GroupBy(g => g.radius).Select(g => g.Key).DefaultIfEmpty(n.GroundRadiusMetres))
                c.Add(g >= glyphMetres - .01f, "ground radius " + F(g) + " m covers the minimap glyph at any turn: (MiniIconPx " + F(n.MiniIconPx) + " x .7071 + 1) px x " + F(2f * spec.OutsideMetres) + " m / " + F(windowH)
                    + " px = " + F(glyphMetres, "0.00") + " m (" + (hud != null ? "the open scene's HUD" : "MinimapSpec304 defaults: no HUD in the active scene") + ")");

            // 3. the threshold against the LIVE shader: need >= the deepest the crisp edge can lie + the margin the bake recorded
            string fogFile = Harness303.Abs(FogIncludePath), fog = File.Exists(fogFile) ? File.ReadAllText(fogFile) : "";
            bool f0 = FogDefine(fog, "FROM", out float from), s0 = FogDefine(fog, "SPAN", out float span), k0 = FogDefine(fog, "KCOVER", out float kcover), fl0 = FogDefine(fog, "FLOOR", out float floor);
            c.Add(f0 && s0 && k0 && fl0, "the live " + Path.GetFileName(FogIncludePath) + " carries #define MAPFOG308_E_FROM / SPAN / KCOVER / FLOOR (formula F or later)");
            if (f0 && s0 && k0 && fl0)
            {
                float deep = FogDefine(fog, "DEEP", out float d) ? d : 0f;
                float extra = deep != 0f && FogDefine(fog, "SG", out float sg) && FogDefine(fog, "SWMAX", out float swmax) && FogDefine(fog, "BITE", out float bite) ? Mathf.Max(0f, sg + swmax + bite) : 0f;
                float worst = from + span + extra;
                float margin = (float)Json.Num(file, -1, "groundDrawn", "edge", "marginCells"), cap = (float)Json.Num(file, .98, "groundDrawn", "edge", "needCap");
                c.Add(margin >= 0f, "the data file records the margin of the ground rule (groundDrawn.edge.marginCells " + F(margin) + ")");
                float want = Mathf.Min(cap, worst + Mathf.Max(0f, margin));
                c.Add(n.GroundNeedLand >= want - 1e-4f, "need " + F(n.GroundNeedLand) + " >= min(" + F(cap) + ", FROM " + F(from) + " + SPAN " + F(span) + (deep != 0f ? " + bites " + F(extra) + " (DEEP 1)" : " (DEEP 0)") + " + margin " + F(margin) + ") = " + F(want)
                    + " of the live cginc" + (n.GroundNeedLand >= want - 1e-4f ? "" : ": make the data again (Tools/Unity/Stage308_map3c/Offline/make_reveal_data.py reads the cginc), then apply"));
                c.Add(Mathf.Abs(n.GroundKCover - kcover) < 1e-4f, "KCOVER of the rule " + F(n.GroundKCover) + " = the live cginc's " + F(kcover));
                c.Add(worst < 1f, "FROM + SPAN" + (deep != 0f ? " + SG + SWMAX + BITE" : "") + " = " + F(worst) + " < 1 (a point whose nine cells are walked is always drawn)");
                c.Lines.Add("INFO    the rule holds on every view whose fog cells are wider than FLOOR " + F(floor) + " / need " + F(n.GroundNeedLand) + " = " + F(floor / Mathf.Max(.01f, n.GroundNeedLand), "0.00")
                    + " px (minimap 28 px, sheet 12 px, whole world 4 px at 1080p)");
            }

            // 4. every GroundDrawn marker can show, and its own cell alone does not show it
            var bounds = data.BoundsMax - data.BoundsMin;
            float perX = 0f, perY = 0f;
            var all = new WorldMapDiscoveryGrid(data.BoundsMin, data.BoundsMax, data.Outline);
            if (bounds.x > 0f && bounds.y > 0f) { perX = all.Width / bounds.x; perY = all.Height / bounds.y; }
            // every cell the game can ever walk (the outline decides, as in play; a region gate only says from where)
            var everyCell = new byte[all.ByteCount];
            for (int y = 0; y < all.Height; y++) for (int x = 0; x < all.Width; x++)
                if (all.IsPlayableCell(x, y)) { int index = y * all.Width + x; everyCell[index >> 3] |= (byte)(1 << (index & 7)); }
            all = new WorldMapDiscoveryGrid(data.BoundsMin, data.BoundsMax, data.Outline, everyCell);
            var fogAll = RevealFog(all);
            foreach (var g in ground)
            {
                float cx = (g.marker.WorldXZ.x - data.BoundsMin.x) * perX, cy = (g.marker.WorldXZ.y - data.BoundsMin.y) * perY;
                float least = MapGround308.MinLand(fogAll, all.Width, all.Height, cx, cy, perX, perY, g.radius, n.GroundStepMetres, n.GroundKCover, float.NegativeInfinity);
                c.Add(MapGround308.Drawn(fogAll, all.Width, all.Height, cx, cy, perX, perY, g.radius, n.GroundStepMetres, n.GroundNeedLand, n.GroundKCover),
                    g.marker.Id + " can show: with every walkable cell walked the ground within " + F(g.radius) + " m is drawn (least land " + F(least) + ")");
                var ownCell = new byte[all.ByteCount];
                int ox = Mathf.FloorToInt((g.marker.WorldXZ.x - data.BoundsMin.x) / WorldMapDiscoveryGrid.CellSize), oy = Mathf.FloorToInt((g.marker.WorldXZ.y - data.BoundsMin.y) / WorldMapDiscoveryGrid.CellSize);
                if (all.IsPlayableCell(ox, oy)) { int index = oy * all.Width + ox; ownCell[index >> 3] |= (byte)(1 << (index & 7)); }
                var one = new WorldMapDiscoveryGrid(data.BoundsMin, data.BoundsMax, data.Outline, ownCell);
                var fogOne = RevealFog(one);
                c.Add(one.IsDiscovered(g.marker.WorldXZ) && !MapGround308.Drawn(fogOne, one.Width, one.Height, cx, cy, perX, perY, g.radius, n.GroundStepMetres, n.GroundNeedLand, n.GroundKCover),
                    g.marker.Id + " does not show on its own cell alone (first contact: the paper still draws wash there; least land "
                    + F(MapGround308.MinLand(fogOne, one.Width, one.Height, cx, cy, perX, perY, g.radius, n.GroundStepMetres, n.GroundKCover, float.NegativeInfinity)) + ")");
            }

            // 5. the recorded fog states: the C# rule gives what the offline twin recorded (visible == ground drawn)
            string casesFile = RepoAbs(RevealCasesFile);
            c.Add(File.Exists(casesFile), "recorded fog states: " + RevealCasesFile);
            if (File.Exists(casesFile))
            {
                var root = Json.Parse(File.ReadAllText(casesFile, Encoding.UTF8));
                bool sameNumbers = Mathf.Abs((float)Json.Num(root, -1, "rule", "needLand") - n.GroundNeedLand) < 1e-5f && Mathf.Abs((float)Json.Num(root, -1, "rule", "stepM") - n.GroundStepMetres) < 1e-5f
                    && Mathf.Abs((float)Json.Num(root, -1, "rule", "kCover") - n.GroundKCover) < 1e-5f;
                c.Add(sameNumbers, "the states were recorded with the notation's numbers (need " + F((float)Json.Num(root, -1, "rule", "needLand")) + ", step " + F((float)Json.Num(root, -1, "rule", "stepM")) + ", KCOVER " + F((float)Json.Num(root, -1, "rule", "kCover"))
                    + ")" + (sameNumbers ? "" : ": record again (Tools/Unity/Stage308_map3c/Offline/map3c_checks.py record)"));
                int total = 0, agree = 0, shown = 0, landOff = 0; float worstLand = 0f; var wrong = new List<string>();
                foreach (var item in Json.List(root, "cases"))
                {
                    total++;
                    string id = Json.Text(item, "?", "id");
                    if (!WorldMapDiscoveryGrid.TryDecode(Json.Text(item, "", "cells"), all.ByteCount, out byte[] bytes)) { wrong.Add(id + " (cells do not decode)"); continue; }
                    var grid = new WorldMapDiscoveryGrid(data.BoundsMin, data.BoundsMax, data.Outline, bytes);
                    var fogCase = RevealFog(grid);
                    float x = (float)Json.Num(item, 0, "x"), z = (float)Json.Num(item, 0, "z"), radius = (float)Json.Num(item, n.GroundRadiusMetres, "radiusM");
                    float cx = (x - data.BoundsMin.x) * perX, cy = (z - data.BoundsMin.y) * perY;
                    bool drawn = MapGround308.Drawn(fogCase, grid.Width, grid.Height, cx, cy, perX, perY, radius, n.GroundStepMetres, n.GroundNeedLand, n.GroundKCover);
                    bool cell = grid.IsDiscovered(new Vector2(x, z));
                    float least = MapGround308.MinLand(fogCase, grid.Width, grid.Height, cx, cy, perX, perY, radius, n.GroundStepMetres, n.GroundKCover, float.NegativeInfinity);
                    float off = Mathf.Abs(least - (float)Json.Num(item, -9, "minLand")); worstLand = Mathf.Max(worstLand, off); if (off > 1e-3f) landOff++;
                    bool ok = drawn == Json.Flag(item, !drawn, "groundDrawn") && cell == Json.Flag(item, !cell, "cellWalked");
                    if (ok) agree++; else wrong.Add(id);
                    if (drawn && cell) shown++;
                }
                c.Add(total > 0 && agree == total, "MapGround308 on the recorded fog states = the offline twin: " + agree + "/" + total + " (cell walked and ground drawn), " + shown + " of them shown"
                    + (wrong.Count > 0 ? "; differing: " + string.Join(", ", wrong.Take(8)) : ""));
                c.Add(landOff == 0, "least land of each state within .001 of the recorded value (largest difference " + F(worstLand, "0.#####") + ")");
                c.Lines.Add("INFO    in the offline twin every state the rule shows has its whole minimap glyph on drawn ground (" + Json.Text(root, "?", "twinNote") + ")");
            }
            return c.Finish("reveal", "checks-reveal.txt");
        }

        // ------------------------------------------------------------------ preview (Edit mode, no Play, nothing saved)
        static string Preview(string command)
        {
            // options
            Vector2? at = null; bool? revealAll = true; float revealMetres = 0f, heading = float.NaN; int height = 1080; bool bundleOn = true, groundRule = true, sheetRim = true; string tag = "preview";
            foreach (string part in command.Split(':').Skip(1))
            {
                int eq = part.IndexOf('='); if (eq <= 0) return "REFUSED preview option '" + part + "' (" + Usage + ")";
                string key = part.Substring(0, eq).Trim(), value = part.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "at":
                    {
                        var xy = value.Split(',');
                        if (xy.Length != 2 || !float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return "REFUSED at=x,z";
                        at = new Vector2(x, z); break;
                    }
                    case "reveal":
                        if (value == "all") { revealAll = true; revealMetres = 0f; }
                        else if (value == "none") { revealAll = false; revealMetres = 0f; }
                        else if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) && r > 0f) { revealAll = false; revealMetres = r; }
                        else return "REFUSED reveal=all|none|<metres>";
                        break;
                    case "follow": if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out heading)) return "REFUSED follow=<degrees>"; break;
                    case "size": if (value == "1440") height = 1440; else if (value == "1080") height = 1080; else return "REFUSED size=1080|1440"; break;
                    case "bundle": bundleOn = value != "off"; break;
                    case "groundrule": if (value == "on") groundRule = true; else if (value == "off") groundRule = false; else return "REFUSED groundrule=on|off"; break;
                    case "sheetrim": if (value == "on") sheetRim = true; else if (value == "off") sheetRim = false; else return "REFUSED sheetrim=on|off"; break;
                    case "tag": tag = new string(value.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray()); if (tag.Length == 0) tag = "preview"; break;
                    default: return "REFUSED preview option '" + key + "' (" + Usage + ")";
                }
            }
            var active = SceneManager.GetActiveScene(); bool dirtyBefore = active.isDirty;
            var ui = Object.FindObjectsByType<PlaytestUiRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(u => u.gameObject.scene == active);
            if (ui == null || ui.MapData == null || ui.Content == null || ui.WorldSheet == null || ui.Theme == null)
                return "REFUSED the active scene has no PlaytestUiRoot with MapData / Content / WorldSheet / Theme (open W_Demo_Main; the preview only reads it)";
            var styleAsset = AssetDatabase.LoadAssetAtPath<MapStyle304SO>(StylePath);
            if (styleAsset == null) return "REFUSED " + StylePath + " missing";
            var notation = AssetDatabase.LoadAssetAtPath<MapNotation308SO>(NotationPath);
            if (bundleOn && notation == null) return "REFUSED " + NotationPath + " missing (run apply first, or preview:bundle=off for the pre-#308 map)";

            string output = Path.Combine(Out, "preview"); Directory.CreateDirectory(output);
            int width = height * 16 / 9; float scale = height / 1080f;
            var lines = new List<string>();
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject New(string name, params Type[] types) { var g = new GameObject(name, types); SceneManager.MoveGameObjectToScene(g, preview); return g; }
            var instance = typeof(PlaytestUiRoot).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var oldInstance = PlaytestUiRoot.Instance;
            RenderTexture rt = null; WorldMapPresenter presenter = null; HudMinimap304 mini = null; MapStyle304SO style = null; WorldMacroPlaytestSO content = null; GameObject panel = null;
            MapNotation308SO notationCopy = null;
            try
            {
                var cam = New("Map308 preview camera", typeof(Camera)).GetComponent<Camera>();
                cam.enabled = false; cam.scene = preview; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.06f, .06f, .055f); cam.cullingMask = 1 << 31;
                rt = new RenderTexture(width, height, 24) { name = "Map308Preview" }; cam.targetTexture = rt; cam.aspect = 16f / 9f;
                panel = New("Map308 preview canvas", typeof(RectTransform), typeof(Canvas));
                var canvas = panel.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1; canvas.scaleFactor = scale;
                var sessionHost = New("Map308 preview session"); sessionHost.SetActive(false);
                var session = sessionHost.AddComponent<WorldMacroPlaytestSession>();
                content = Object.Instantiate(ui.Content);
                Vector2 here = at ?? new Vector2(content.StartFeet.x, content.StartFeet.z);
                content.StartFeet = new Vector3(here.x, content.StartFeet.y, here.y); session.Content = content;
                var progress = WorldMacroProgress.CreateNew("map308-preview", content.StartFeet, 0);
                typeof(WorldMacroPlaytestSession).GetProperty("Progress").SetValue(session, progress);
                // the style the real maps resolve, with the bundle switched as asked (a copy in memory: the asset is not touched)
                style = Object.Instantiate(styleAsset); style.hideFlags = HideFlags.HideAndDontSave; style.Notation308 = bundleOn ? notation : null;
                // #308 map 4: sheetrim=off = the sheet with the minimap's rim. A copy of the notation in memory carries the switch; the asset is not touched
                if (bundleOn && notation != null && !sheetRim)
                { notationCopy = Object.Instantiate(notation); notationCopy.hideFlags = HideFlags.HideAndDontSave; notationCopy.SheetRimWhole = 0f; style.Notation308 = notationCopy; }
                if (instance != null && instance.CanWrite) instance.SetValue(null, ui);
                var theme = ui.Theme;
                presenter = panel.AddComponent<WorldMapPresenter>(); presenter.enabled = false;
                presenter.Initialize((RectTransform)panel.transform, session, ui.WorldSheet, new WorldMapUiDependencies
                { BakedData = ui.MapData, MapStyle = style, Theme = theme, Icons = theme.Icons, Font = theme.Font, PaperTexture = theme.PaperTexture, Ink = theme.Ink, Paper = theme.Paper, Muted = theme.Muted, Seal = theme.Seal, ReducedMotion = true });
                presenter.PreviewReveal308(revealAll, here, revealMetres);
                // #308 map 3c: a marker without an arrival event is known when the game's own walk test says so (cell + reveal rule);
                // an arrival marker by its cell, as before (the preview has no arrival events). groundrule=off = the cell alone
                presenter.PreviewIgnoreReveal308(!groundRule);
                progress.ui.discoveredMarkers = ui.MapData.Markers.Where(m => m != null && presenter.PreviewWalkReveals308(m)).Select(m => m.Id).ToList();
                lines.Add("PREVIEW bundle " + (presenter.Notation308 != null ? "ON (stage '" + presenter.Notation308.Stage + "')" : bundleOn ? "OFF: the bundle is not usable for " + ui.MapData.name + " (pre-#308 path)" : "off (pre-#308 path)")
                    + " at " + here + " reveal " + (revealAll == true ? "all" : revealMetres > 0f ? F(revealMetres) + " m" : "none") + " known markers " + progress.ui.discoveredMarkers.Count + "/" + ui.MapData.Markers.Length + " " + width + "x" + height);
                if (presenter.Notation308 != null)
                    lines.Add("RIM minimap a" + F(presenter.Notation308.EdgeRimAlpha) + " " + F(presenter.Notation308.EdgeRimPx) + " px broken | sheet "
                        + (presenter.Notation308.SheetRimWhole > 0f ? "whole " + F(presenter.Notation308.SheetRimWhole) + ", " + F(presenter.Notation308.SheetRimPx) + " px a" + F(presenter.Notation308.SheetRimAlpha) : "= the minimap's rim")
                        + (sheetRim ? "" : " (sheetrim=off)"));
                if (presenter.Notation308 != null)
                    foreach (var m in ui.MapData.Markers)
                    {
                        if (m == null || m.RequiresArrival) continue;
                        var rule = presenter.PreviewGround308(m, out float radius, out float least);
                        if (rule == MapMarkerReveal308.FirstContact) continue;
                        lines.Add("REVEAL " + m.Id + " " + rule + (groundRule ? "" : " (groundrule=off: by its cell)") + ": cell walked " + presenter.PreviewDiscovered308(m.WorldXZ)
                            + (rule == MapMarkerReveal308.GroundDrawn ? ", least land within " + F(radius) + " m " + F(least) + " (need " + F(presenter.Notation308.GroundNeedLand) + ")" : "")
                            + " -> " + (progress.ui.discoveredMarkers.Contains(m.Id) ? "shown" : "not shown"));
                    }

                void Layers() { foreach (var t in panel.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31; }
                string Capture(string name, RectInt? crop)
                {
                    Layers(); Canvas.ForceUpdateCanvases(); cam.Render();
                    var previous = RenderTexture.active; RenderTexture.active = rt;
                    var area = crop ?? new RectInt(0, 0, width, height);
                    var tex = new Texture2D(area.width, area.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(area.x, area.y, area.width, area.height), 0, 0); tex.Apply();
                    string file = Path.Combine(output, tag + "_" + name + ".png");
                    File.WriteAllBytes(file, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex); RenderTexture.active = previous;
                    return file;
                }

                // the unfolded sheet: default view and whole world
                presenter.OpenedFromPage304 = true;   // no veil wipe animation in a still
                presenter.SetExpanded(true);
                presenter.PreviewRefresh308();
                lines.Add("FULL current view: " + Capture("full_current", null) + " | strip vertices " + presenter.FullStripVertices308 + " uploads " + presenter.FullStripUploads308);
                // #308 map 4: what the sheet's material really holds (ink alpha, broken rim px, whole 0 / 1, whole rim px; px = notation px x canvas x page)
                if (presenter.Notation308 != null) lines.Add("RIM material sheet _M308Rim " + V4(presenter.PreviewSheetRim308) + " canvas x" + F(scale));
                presenter.ShowWholeWorld(); presenter.PreviewRefresh308();
                lines.Add("FULL whole world: " + Capture("full_whole", null) + " | strip vertices " + presenter.FullStripVertices308 + " uploads " + presenter.FullStripUploads308);
                var names = new[] { "ExpandedWorldMap/TwiceFoldedHanji", "ExpandedWorldMap/TwiceFoldedHanji/PrintedMapWindow", "ExpandedWorldMap/TwiceFoldedHanji/PrintedMapWindow/MapInput" };
                lines.Add("AC-E2 sheet names: " + string.Join(", ", names.Select(nm => Path.GetFileName(nm) + "=" + (panel.transform.Find(nm) != null ? "ok" : "MISSING"))));
                presenter.SetExpanded(false);
                presenter.PreviewRefresh308();

                // the HUD minimap over a light and a dark ground
                var hud = Object.FindObjectsByType<HudController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(h => h.gameObject.scene == active);
                var spec = hud != null && hud.Minimap != null ? hud.Minimap : new MinimapSpec304();
                mini = HudMinimap304.Create(UiStyle304SO.Resolve(theme), HudTokens304.Load(), panel.transform, spec, theme.Icons);
                bool follow = !float.IsNaN(heading);
                bool ready = mini.PreviewFrame308(presenter, follow, heading) && mini.PreviewFrame308(presenter, follow, heading);
                int cx = Mathf.RoundToInt(spec.Centre.x * scale), cy = Mathf.RoundToInt((1080f - spec.Centre.y) * scale), hw = Mathf.RoundToInt(190 * scale), hh = Mathf.RoundToInt(150 * scale);
                var cropRect = new RectInt(Mathf.Clamp(cx - hw, 0, width - 2 * hw), Mathf.Clamp(cy - hh, 0, height - 2 * hh), 2 * hw, 2 * hh);
                cam.backgroundColor = new Color(.56f, .58f, .52f);
                lines.Add("MINI light ground: " + Capture("mini_light", cropRect));
                cam.backgroundColor = new Color(.10f, .11f, .10f);
                lines.Add("MINI dark ground: " + Capture("mini_dark", cropRect));
                var root = mini.Root; var map = mini.Map;
                bool masks = root.GetComponentsInChildren<UnityEngine.UI.Mask>(true).Length + root.GetComponentsInChildren<UnityEngine.UI.RectMask2D>(true).Length == 0;
                lines.Add("AC-E2 minimap: ready " + ready + ", root '" + root.name + "', MiniRoot " + (map != null && map.name == "MiniRoot" ? "ok" : "MISSING") + ", uvRect " + (map != null ? map.uvRect.ToString() : "-")
                    + ", _MINI_HUD " + (mini.Material != null && mini.Material.IsKeywordEnabled("_MINI_HUD")) + ", _MAP308 " + (mini.Material != null && mini.Material.IsKeywordEnabled("_MAP308"))
                    + ", Mask / RectMask2D none " + masks + ", notation " + mini.Notation308Active + ", interior " + mini.Interior + ", range " + F(mini.RangeMetres) + " m");
                // #308 map 4: the minimap's material keeps z = w = 0 (and its shader variant does not read them)
                if (presenter.Notation308 != null) lines.Add("RIM material minimap _M308Rim " + (mini.Material != null && mini.Material.HasProperty("_M308Rim") ? V4(mini.Material.GetVector("_M308Rim")) : "no material") + " canvas x" + F(scale));
                lines.Add("MINI strips: uploads " + mini.StripUploads308 + ", vertices " + mini.StripVertices308 + ", mean build " + mini.StripMeanBuildMilliseconds308.ToString("0.000", CultureInfo.InvariantCulture)
                    + " ms (editor), minimap prints (raster) " + presenter.MiniPrints + ", markers shown " + mini.VisibleMarkers + ", objectives left off " + mini.ObjectivesLeftOff);
                File.WriteAllText(Path.Combine(output, tag + ".txt"), string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            }
            finally
            {
                if (instance != null && instance.CanWrite) instance.SetValue(null, oldInstance);
                // MonoBehaviour.OnDestroy does not run outside Play: release the run-time materials and textures by hand
                if (mini != null) mini.ReleasePreview308();
                if (presenter != null) presenter.ReleasePreview308();
                if (panel != null) Object.DestroyImmediate(panel);
                if (content != null) Object.DestroyImmediate(content);
                if (style != null) Object.DestroyImmediate(style);
                if (notationCopy != null) Object.DestroyImmediate(notationCopy);
                if (rt != null) { if (RenderTexture.active == rt) RenderTexture.active = null; rt.Release(); Object.DestroyImmediate(rt); }
                EditorSceneManager.ClosePreviewScene(preview);
            }
            lines.Add("sceneDirty " + dirtyBefore + " -> " + SceneManager.GetActiveScene().isDirty + " (preview scene closed, nothing saved)");
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ a small JSON reader (objects, arrays, strings, numbers, true / false / null)
        static class Json
        {
            public static object Parse(string text) { int i = 0; return Value(text, ref i); }

            static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            static object Value(string s, ref int i)
            {
                Skip(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON ends early");
                char c = s[i];
                if (c == '{')
                {
                    var map = new Dictionary<string, object>(StringComparer.Ordinal); i++;
                    Skip(s, ref i);
                    if (s[i] == '}') { i++; return map; }
                    while (true)
                    {
                        Skip(s, ref i);
                        string key = Quoted(s, ref i);
                        Skip(s, ref i);
                        if (s[i] != ':') throw new FormatException("JSON: ':' expected at " + i);
                        i++;
                        map[key] = Value(s, ref i);
                        Skip(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        if (s[i] == '}') { i++; return map; }
                        throw new FormatException("JSON: ',' or '}' expected at " + i);
                    }
                }
                if (c == '[')
                {
                    var list = new List<object>(); i++;
                    Skip(s, ref i);
                    if (s[i] == ']') { i++; return list; }
                    while (true)
                    {
                        list.Add(Value(s, ref i));
                        Skip(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        if (s[i] == ']') { i++; return list; }
                        throw new FormatException("JSON: ',' or ']' expected at " + i);
                    }
                }
                if (c == '"') return Quoted(s, ref i);
                if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
                if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
                if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
                if (i == start) throw new FormatException("JSON: value expected at " + i);
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            static string Quoted(string s, ref int i)
            {
                if (s[i] != '"') throw new FormatException("JSON: string expected at " + i);
                i++;
                var sb = new StringBuilder();
                while (s[i] != '"')
                {
                    char c = s[i++];
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                i++;
                return sb.ToString();
            }

            public static object At(object node, params string[] path)
            {
                foreach (string key in path)
                {
                    if (!(node is Dictionary<string, object> map) || !map.TryGetValue(key, out node)) return null;
                }
                return node;
            }

            public static double AsNum(object value, double fallback) => value is double d ? d : fallback;
            public static double Num(object node, double fallback, params string[] path) => AsNum(At(node, path), fallback);
            public static string Text(object node, string fallback, params string[] path) => At(node, path) as string ?? fallback;
            public static bool Flag(object node, bool fallback, params string[] path) => At(node, path) is bool b ? b : fallback;
            public static List<object> List(object node, params string[] path) => At(node, path) as List<object> ?? new List<object>();
            public static Dictionary<string, object> Map(object node, params string[] path) => At(node, path) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }
    }

    /// <summary>The Spec's name for the same command set (SPEC-MAP-OVERHAUL-308 6.4: `Map308 import | check | revert`).</summary>
    public static class Map308
    {
        public static string Run(string command) => MapOverhaul308.Run(command);
        public static string Execute(string argument) => MapOverhaul308.Run(argument);
    }
}
