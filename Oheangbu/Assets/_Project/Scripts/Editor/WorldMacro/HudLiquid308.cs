using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 HUD liquid vessels (SPEC-HUD-LIQUID-308, D308-11 / D308-11b) - asset setup, data / model checks, stills [TEST values throughout].
    // Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.HudLiquid308 Run "<command>"
    // Queue-safe: no dialog is ever opened; refusals come back as "REFUSED ...", failures as "FAILED ...".
    // WRITES ONLY under Assets/_Project/Art/UI/UI308/ and Assets/_Project/Resources/UI308/ (every path is checked, protected trees are
    // refused on top of that). It never walks themes, skins or scenes: the runtime finds the profile through Resources. Only the
    // edited asset is saved (AssetDatabase.SaveAssetIfDirty): other sessions' dirty assets are left alone. Every write is
    // checked on the FILE afterwards. Ledger: Art/UI308/HUD/ledger_setup.json (byte backups under Art/UI308/HUD/Backups/).
    //   status                      read-only: assets, shader, import settings, profile switch, preview
    //   hud308-setup:dry            what hud308-setup would write (nothing is written)
    //   hud308-setup                atlas import settings, Materials/M_InkVessel308.mat, Resources/UI308/HudLiquid308.asset.
    //                               Values already in the profile are kept; the same input twice = "변경 없음"
    //   hud308-setup:reset          the same, profile values back to the code defaults
    //   hud308-enable:on | :off     the profile's Enabled switch (off = the #304 meters come back on the next HUD build)
    //   hud308-revert               removes what this ledger created, restores what it changed (bytes), backs up first
    //   hud308-report               the profile's numbers
    //   checks:data                 Edit: shader compiles, no emission / HDR property, import settings, references, LDR colours,
    //                               theme values (lacquer, nacre: LDR, never above paper), the shared impact include
    //   checks:sim                  Edit: LiquidSim308 / ActionMeter308 / HudActionRules308 / ImpactGate308 / the key glyph lookup against the
    //                               Spec's [T] numbers, in C#. D308-11b: nothing moves while the player stands still, every action is
    //                               answered in proportion (the scripted timeline of AC-H16, HudLiquid308Timeline), the reading never
    //                               moves with the slosh; the key of each mark follows the real binding (probe actions, nothing rebound)
    //   preview:on:<hp>:<ink>:<state> | preview:off
    //                               stills. Edit Mode: an in-memory HUD canvas (HideAndDontSave, nothing saved, scene not dirtied).
    //                               Play: holds the live HUD's vessels. state = normal | slosh | low | pour | wet | cost | dry | impact
    //                               (impact: the light side comes from the deploy layer's point, so the frame is forced through
    //                               ImpactFrameDirector308.ForceFrame; without a director only the strength is held)
    //   checks:play | play-status | play-abort
    //                               Play: hierarchy contract (five graphics, no text object), the key glyph of each mark = the cell of
    //                               its real binding, same-frame values, impact-frame light for exactly the frames the deploy layer's
    //                               director is asked to hold (ForceFrame), stuck guard, mesh rebuilds while resting (= a flat line).
    //                               Runs over frames; read the result with play-status.
    // The impact globals (_OhImpact308, _OhImpactHud308, _OhImpactHudPoint308) have ONE writer, the deploy layer's
    // ImpactFrameDirector308. This tool never calls Shader.SetGlobal*: checks and stills ask the director for a forced frame.
    // No static fields: the Play probe keeps its state in UnityEditor.SessionState.
    public static class HudLiquid308
    {
        public const string ShaderName = "UI/InkVessel308";
        const string ArtFolder = "Assets/_Project/Art/UI/UI308";
        const string ShaderPath = ArtFolder + "/Shaders/InkVessel308.shader";
        const string TextureFolder = ArtFolder + "/Textures";
        const string AtlasPath = TextureFolder + "/hud308_atlas.png";
        const string AtlasJsonPath = TextureFolder + "/hud308_atlas.json";
        const string MaterialFolder = ArtFolder + "/Materials";
        const string MaterialPath = MaterialFolder + "/M_InkVessel308.mat";
        const string ResourcesFolder = "Assets/_Project/Resources/UI308";
        const string ProfilePath = ResourcesFolder + "/HudLiquid308.asset";
        const string PreviewName = "HudLiquid308_Preview";
        const string Usage = "status | hud308-setup[:dry|:reset] | hud308-enable:on|off | hud308-revert | hud308-report | checks:data | checks:sim | " +
            "preview:on:<hp>:<ink>:<state> | preview:off | checks:play | play-status | play-abort";

        static string Out => Path.Combine(Harness303.RepoRoot, "Art", "UI308", "HUD");
        sealed class Refuse : Exception { public Refuse(string m) : base(m) { } }
        static string F(float v, string f = "0.###") => v.ToString(f, CultureInfo.InvariantCulture);

        /// <summary>Spec name of the entry point; same as Run.</summary>
        public static string Execute(string argument) => Run(argument);

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                if (c == "status") return Status();
                if (c == "hud308-report") return Report();
                if (c == "checks:data") return ChecksData();
                if (c == "checks:sim") return ChecksSim();
                if (c.StartsWith("preview:on", StringComparison.Ordinal)) return PreviewOn(c);
                if (c == "preview:off") return PreviewOff();
                if (c == "checks:play") return PlayStart();
                if (c == "play-status") return PlayStatus();
                if (c == "play-abort") return PlayFinish("aborted by command", false);
                bool setup = c == "hud308-setup" || c == "hud308-setup:dry" || c == "hud308-setup:reset";
                bool enable = c == "hud308-enable:on" || c == "hud308-enable:off";
                if (!setup && !enable && c != "hud308-revert") return "REFUSED unknown HudLiquid308 command '" + c + "' (" + Usage + ")";
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit Mode only (Play is running)";
                if (EditorApplication.isCompiling) return "REFUSED scripts are compiling; call again when done";
                if (setup) return Setup(c.EndsWith(":dry", StringComparison.Ordinal), c.EndsWith(":reset", StringComparison.Ordinal));
                if (enable) return Enable(c.EndsWith(":on", StringComparison.Ordinal));
                return Revert();
            }
            catch (Refuse r) { return "REFUSED " + r.Message; }
            catch (Exception e) { return "FAILED " + e; }
        }

        // ------------------------------------------------------------------ write guard + ledger
        static void Guard(string assetPath)
        {
            string p = (assetPath ?? "").Replace('\\', '/');
            bool allowed = p.StartsWith(ArtFolder + "/", StringComparison.Ordinal) || p.StartsWith(ResourcesFolder + "/", StringComparison.Ordinal)
                || p == ResourcesFolder || p == MaterialFolder;
            if (!allowed) throw new Refuse("write outside the #308 HUD folders refused: " + p);
            if (Harness303.IsProtected(p)) throw new Refuse("protected path " + p);
        }

        [Serializable] sealed class Entry { public string utc = "", asset = "", backup = "", shaBefore = "", shaAfter = "", note = ""; public bool created; }
        [Serializable] sealed class Ledger { public string kind = "hud308_setup", created = ""; public List<Entry> entries = new List<Entry>(); }
        static string LedgerFile => Path.Combine(Out, "ledger_setup.json");
        static Ledger ReadLedger() => File.Exists(LedgerFile) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerFile)) : null;
        static void WriteLedger(Ledger l) { Directory.CreateDirectory(Out); File.WriteAllText(LedgerFile, JsonUtility.ToJson(l, true)); }
        static string BackupDir(string tag) => Path.Combine(Out, "Backups", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + tag);

        // records the file as it is now (byte copy) before a write; created = the file does not exist yet
        static Entry Before(Ledger ledger, string filePath, string backupDir, string note)
        {
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
            if (!AssetDatabase.IsValidFolder(parent)) throw new Refuse("parent folder missing: " + parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ inputs
        [Serializable] sealed class LevelJson { public float floor_uv = 0, full_uv = 0, neck_top_uv = 0, wavelength_px = 0; }
        [Serializable] sealed class CellsJson
        {
            public float[] vessel = null, dodge = null, jump = null, vehicle = null, vehicle_out = null;
            public float[] dodge_n = null, jump_n = null, vehicle_n = null, vehicle_out_n = null;      // najeon lids (theme)
        }
        [Serializable] sealed class CollarJson { public float line_half_px = 0, piece_px = 0; }
        [Serializable] sealed class ButtonJson { public float radius_px = 0, cell_px = 0, ring_inner_px = 0; }
        [Serializable] sealed class ThemeJson { public CollarJson collar = new CollarJson(); public ButtonJson button = new ButtonJson(); }
        [Serializable] sealed class AtlasFileJson { public string file = "", sha256 = ""; }
        [Serializable] sealed class MarkJson { public float size_px = 0, margin_px = 0; }
        [Serializable] sealed class KeysJson { public float[] grid = null, cell = null; public float columns = 0, box_px = 0; }
        [Serializable] sealed class AtlasJson
        {
            public AtlasFileJson atlas = new AtlasFileJson(); public float[] ref_rect = null, quad_ref = null; public float margin_px = 0, sdf_range_px = 0;
            public LevelJson level = new LevelJson(); public CellsJson cells = new CellsJson(); public MarkJson mark = new MarkJson();
            public ThemeJson theme = new ThemeJson();
            public KeysJson keys = new KeysJson();      // D308-11b: the key glyph block
        }

        static AtlasJson ReadAtlasJson()
        {
            string abs = Harness303.Abs(AtlasJsonPath);
            return File.Exists(abs) ? JsonUtility.FromJson<AtlasJson>(File.ReadAllText(abs)) : null;
        }

        static Vector4 Cell(float[] v, Vector4 fallback) => v != null && v.Length == 4 ? new Vector4(v[0], v[1], v[2], v[3]) : fallback;

        static void ApplyAtlas(AtlasSpec308 a, AtlasJson j)
        {
            if (j == null) return;
            a.Vessel = Cell(j.cells.vessel, a.Vessel); a.Dodge = Cell(j.cells.dodge, a.Dodge); a.Jump = Cell(j.cells.jump, a.Jump);
            a.Vehicle = Cell(j.cells.vehicle, a.Vehicle); a.VehicleOut = Cell(j.cells.vehicle_out, a.VehicleOut);
            if (j.quad_ref != null && j.quad_ref.Length == 2) a.QuadRef = new Vector2(j.quad_ref[0], j.quad_ref[1]);
            if (j.ref_rect != null && j.ref_rect.Length == 2) a.RefRect = new Vector2(j.ref_rect[0], j.ref_rect[1]);
            if (j.margin_px > 0f) a.MarginPx = j.margin_px;
            if (j.sdf_range_px > 0f) a.SdfRangePx = j.sdf_range_px;
            if (j.mark != null && j.mark.size_px > 0f) { a.MarkRefPx = j.mark.size_px; a.MarkMarginPx = j.mark.margin_px; }
            if (j.level.full_uv > 0f) { a.FloorUv = j.level.floor_uv; a.FullUv = j.level.full_uv; a.NeckTopUv = j.level.neck_top_uv; a.WavelengthPx = j.level.wavelength_px; }
            // theme (D308-15): lid cells, lid geometry, the collar line's cut
            a.DodgeLid = Cell(j.cells.dodge_n, a.DodgeLid); a.JumpLid = Cell(j.cells.jump_n, a.JumpLid);
            a.VehicleLid = Cell(j.cells.vehicle_n, a.VehicleLid); a.VehicleOutLid = Cell(j.cells.vehicle_out_n, a.VehicleOutLid);
            if (j.theme != null && j.theme.button != null && j.theme.button.radius_px > 0f)
            { a.LidRadiusPx = j.theme.button.radius_px; a.LidCellPx = j.theme.button.cell_px; a.LidRimInnerPx = j.theme.button.ring_inner_px; }
            if (j.theme != null && j.theme.collar != null && j.theme.collar.piece_px > 0f)
            { a.CollarHalfPx = j.theme.collar.line_half_px; a.CollarPiecePx = j.theme.collar.piece_px; }
            // key glyphs (D308-11b): where the key block sits in the atlas
            if (j.keys != null && j.keys.grid != null && j.keys.grid.Length == 4 && j.keys.cell != null && j.keys.cell.Length == 2 && j.keys.columns >= 1f)
            {
                a.KeyGrid = new Vector4(j.keys.grid[0], j.keys.grid[1], j.keys.grid[2], j.keys.grid[3]);
                a.KeyCell = new Vector2(j.keys.cell[0], j.keys.cell[1]); a.KeyColumns = j.keys.columns; a.KeyBoxPx = j.keys.box_px;
            }
        }

        // the #304 style asset (colours, rim alpha): read only. Protected trees are skipped; none = the code defaults (= the tokens)
        static UiStyle304SO FindStyle(out string path)
        {
            var guids = AssetDatabase.FindAssets("t:UiStyle304SO");
            var paths = new List<string>();
            foreach (var g in guids) { string p = AssetDatabase.GUIDToAssetPath(g); if (!Harness303.IsProtected(p)) paths.Add(p); }
            paths.Sort(StringComparer.Ordinal);
            foreach (var p in paths) { var s = AssetDatabase.LoadAssetAtPath<UiStyle304SO>(p); if (s != null) { path = p; return s; } }
            path = "<UiStyle304SO.Fallback: code defaults>";
            return UiStyle304SO.Fallback;
        }

        // ------------------------------------------------------------------ material <- style + profile
        static readonly string[] MaterialColours = { "_PaperColor", "_InkColor", "_AlertColor", "_Lacquer" };
        static readonly string[] MaterialVectors = { "_CellVessel", "_CellDodge", "_CellJump", "_CellVehicle", "_CellVehicleOut", "_QuadRef", "_Level",
            "_Glass", "_Glass2", "_Pool", "_Meniscus", "_Low", "_Cost", "_Pour", "_Impact", "_Mark", "_Look", "_Lens",
            "_CellDodgeN", "_CellJumpN", "_CellVehicleN", "_CellVehicleOutN", "_Theme", "_NacreN0", "_NacreA", "_NacreB", "_NacreHue", "_NacreArc",
            "_NacreCut", "_Button", "_Key", "_Key2", "_KeyGrid", "_KeyCell" };

        static void ApplyMaterial(Material m, Shader shader, UiStyle304SO style, HudLiquid308ProfileSO p)
        {
            if (m.shader != shader) m.shader = shader;
            var a = p.Atlas; var g = p.Glass; var l = p.Liquid; var i = p.Impact;
            m.SetColor("_PaperColor", UiStyle304SO.A(style.Paper, 1f));
            m.SetColor("_InkColor", UiStyle304SO.A(style.Ink, 1f));
            m.SetColor("_AlertColor", UiStyle304SO.A(style.CinnabarLift, 1f));
            m.SetVector("_CellVessel", a.Vessel); m.SetVector("_CellDodge", a.Dodge); m.SetVector("_CellJump", a.Jump);
            m.SetVector("_CellVehicle", a.Vehicle); m.SetVector("_CellVehicleOut", a.VehicleOut);
            m.SetVector("_QuadRef", new Vector4(a.QuadRef.x, a.QuadRef.y, a.MarginPx, a.SdfRangePx));
            m.SetVector("_Level", new Vector4(a.FloorUv, a.FullUv, a.WavelengthPx, a.NeckTopUv));
            m.SetVector("_Glass", new Vector4(g.OutlinePx, g.PaperRimPx, g.EmptyWash, g.HighlightAlpha));
            m.SetVector("_Glass2", new Vector4(g.WallPx, style.Meter.RimAlpha, g.OutlineAlpha, l.LiquidAlpha));
            m.SetVector("_Pool", new Vector4(l.PoolEdgePx, l.PoolEdgeDark, l.CoreLighten, l.FreshLighten));
            m.SetVector("_Meniscus", new Vector4(l.MeniscusPx, l.MeniscusAlpha, l.MeniscusClimbPx, l.MeniscusClimbRangePx));
            m.SetVector("_Low", new Vector4(p.Low.HpDarken, p.Low.TideAlpha, p.Low.TideMarks, p.Low.InkDryStreak));
            m.SetVector("_Cost", l.CostLine);
            m.SetVector("_Pour", new Vector4(l.PourThreadPx, 0f, 0f, 0f));
            m.SetVector("_Impact", new Vector4(i.ShadowPx, i.ShadowAlpha, i.LiquidShift, i.FarRimInk));
            m.SetVector("_Mark", new Vector4(p.Marks.DryAlpha, p.Marks.RimAlpha, p.Marks.RewetEdge, 0f));
            var k = p.Look;
            m.SetVector("_Look", new Vector4(k.WallAlpha, k.DepthDarken, k.OutlinePressure, k.LensBackAlpha));
            m.SetVector("_Lens", new Vector4(k.LensHpPx, k.LensInkPx, k.LensPaperMix, k.LensRangePx));
            // theme (D308-15): the values come from the profile's Theme block (a copy of the kit's tokens until UiTheme308SO exists)
            var t = p.Theme;
            m.SetVector("_CellDodgeN", a.DodgeLid); m.SetVector("_CellJumpN", a.JumpLid);
            m.SetVector("_CellVehicleN", a.VehicleLid); m.SetVector("_CellVehicleOutN", a.VehicleOutLid);
            m.SetVector("_Theme", new Vector4(t.Collar ? 1f : 0f, t.Lids ? 1f : 0f, t.LidDryAlpha, t.NacreFamilyShare.x + t.NacreFamilyShare.y));
            m.SetColor("_Lacquer", UiStyle304SO.A(t.Lacquer, 1f));
            m.SetVector("_NacreN0", t.NacreN0); m.SetVector("_NacreA", t.NacreA); m.SetVector("_NacreB", t.NacreB);
            m.SetVector("_NacreHue", new Vector4(t.NacreFamilyDeg.x, t.NacreFamilyDeg.y, t.NacreFamilyDeg.z, t.NacreSwingDeg));
            m.SetVector("_NacreArc", new Vector4(t.NacreArcDeg.x, t.NacreArcDeg.y, t.NacreHuePeriodPx, t.NacreFamilyShare.x));
            m.SetVector("_NacreCut", new Vector4(a.CollarHalfPx, a.CollarPiecePx, 0f, 0f));
            m.SetVector("_Button", new Vector4(a.LidRadiusPx, a.MarkRefPx + 2f * a.MarkMarginPx, a.LidCellPx, a.LidRimInnerPx));
            // key glyph (D308-11b): a lacquer keycap with one symbol cell. No colour of its own: face = _Lacquer, hairline / lip /
            // symbol = _PaperColor
            var mk = p.Marks;
            m.SetVector("_Key", new Vector4(mk.KeySize, mk.KeyRimPx, mk.KeyLipPx, mk.KeyDryAlpha));
            m.SetVector("_Key2", new Vector4(a.KeyBoxPx, mk.KeyGlyphLiftPx, mk.KeyRimAlpha, mk.KeyLipAlpha));
            m.SetVector("_KeyGrid", a.KeyGrid);
            m.SetVector("_KeyCell", new Vector4(a.KeyCell.x, a.KeyCell.y, a.KeyColumns, 0f));
        }

        // SPEC-UI-THEME-308 §1: the brightest channel the nacre formula can reach on the theme's hue arc (sRGB code value 0..1)
        static float NacreMaxChannel(ThemeSpec308 t)
        {
            float max = 0f;
            for (float deg = t.NacreArcDeg.x; deg <= t.NacreArcDeg.y; deg += 1f)
            {
                float h = deg * Mathf.Deg2Rad;
                Vector3 c = t.NacreN0 + t.NacreA * Mathf.Cos(h) + t.NacreB * Mathf.Sin(h);
                max = Mathf.Max(max, Mathf.Max(c.x, Mathf.Max(c.y, c.z)));
            }
            return max;
        }

        static string MaterialState(Material m)
        {
            var sb = new StringBuilder(m.shader != null ? m.shader.name : "<no shader>");
            foreach (var n in MaterialColours) sb.Append('|').Append(n).Append('=').Append(m.HasProperty(n) ? m.GetColor(n).ToString("F4") : "-");
            foreach (var n in MaterialVectors) sb.Append('|').Append(n).Append('=').Append(m.HasProperty(n) ? m.GetVector(n).ToString("F5") : "-");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ atlas import settings
        static List<string> ImportDiff(TextureImporter t)
        {
            var d = new List<string>();
            if (t.textureType != TextureImporterType.Default) d.Add("textureType " + t.textureType + " -> Default");
            if (t.sRGBTexture) d.Add("sRGB on -> off (linear data)");
            if (!t.mipmapEnabled) d.Add("mipmaps off -> on");
            if (t.wrapMode != TextureWrapMode.Clamp) d.Add("wrap " + t.wrapMode + " -> Clamp");
            if (t.filterMode != FilterMode.Trilinear) d.Add("filter " + t.filterMode + " -> Trilinear");
            if (t.textureCompression != TextureImporterCompression.Uncompressed) d.Add("compression " + t.textureCompression + " -> Uncompressed");
            if (t.maxTextureSize != 1024) d.Add("maxSize " + t.maxTextureSize + " -> 1024");
            if (t.alphaIsTransparency) d.Add("alphaIsTransparency on -> off (A is data)");
            if (t.npotScale != TextureImporterNPOTScale.None) d.Add("npot " + t.npotScale + " -> None");
            if (t.isReadable) d.Add("readable on -> off");
            return d;
        }

        static void ApplyImport(TextureImporter t)
        {
            t.textureType = TextureImporterType.Default; t.sRGBTexture = false; t.mipmapEnabled = true; t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Trilinear; t.textureCompression = TextureImporterCompression.Uncompressed; t.maxTextureSize = 1024;
            t.alphaIsTransparency = false; t.npotScale = TextureImporterNPOTScale.None; t.isReadable = false;
        }

        static string ShaderErrors(Shader shader)
        {
            var sb = new StringBuilder();
            foreach (var m in ShaderUtil.GetShaderMessages(shader))
                if (m.severity == ShaderCompilerMessageSeverity.Error) sb.Append(" [line ").Append(m.line).Append("] ").Append(m.message);
            return sb.Length > 0 ? sb.ToString() : " (no message)";
        }

        // ------------------------------------------------------------------ setup
        static string Setup(bool dry, bool reset)
        {
            var log = new List<string> { (dry ? "hud308-setup:dry" : reset ? "hud308-setup:reset" : "hud308-setup") };
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) throw new Refuse(ShaderPath + " is missing: copy Tools/Unity/Stage308_hud/_ProjectAssets/Art/UI/UI308 to " + ArtFolder + " and Refresh first");
            if (ShaderUtil.ShaderHasError(shader)) throw new Refuse(ShaderName + " does not compile:" + ShaderErrors(shader));
            var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer == null) throw new Refuse(AtlasPath + " is missing (copy the Textures folder of the stage and Refresh first)");
            var json = ReadAtlasJson();
            log.Add(json != null ? "ATLAS json " + AtlasJsonPath + " sha " + json.atlas.sha256 : "ATLAS json missing: the code defaults are used for the cells");
            if (json != null && !string.IsNullOrEmpty(json.atlas.sha256))
            {
                string onDisk = Harness303.Sha(Harness303.Abs(AtlasPath));
                if (!string.Equals(onDisk, json.atlas.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new Refuse("hud308_atlas.png (" + onDisk + ") is not the file hud308_atlas.json describes: regenerate with Tools/Art/hud308_assets.py and copy both");
            }
            var style = FindStyle(out string stylePath);
            log.Add("STYLE " + stylePath);

            var ledger = ReadLedger() ?? new Ledger { created = DateTime.UtcNow.ToString("O") };
            string backup = BackupDir(dry ? "-dry" : "-setup");
            int written = 0;

            // 1 atlas import settings (the .meta of our own texture)
            var diff = ImportDiff(importer);
            if (diff.Count == 0) log.Add("변경 없음 " + AtlasPath + " import settings");
            else if (dry) log.Add("DRY would set import of " + AtlasPath + ": " + string.Join(", ", diff));
            else
            {
                Guard(AtlasPath);
                var e = Before(ledger, AtlasPath + ".meta", backup, "import settings");
                ApplyImport(importer); importer.SaveAndReimport();
                After(e); written++;
                var again = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
                if (again == null || ImportDiff(again).Count > 0) throw new Exception("import settings did not land on " + AtlasPath);
                log.Add("SET import of " + AtlasPath + ": " + string.Join(", ", diff));
            }
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);

            // the values the material is filled from: the profile as it will be (existing values kept unless :reset) + the atlas json
            var profile = AssetDatabase.LoadAssetAtPath<HudLiquid308ProfileSO>(ProfilePath);
            var want = ScriptableObject.CreateInstance<HudLiquid308ProfileSO>();
            try
            {
                if (profile != null && !reset) EditorUtility.CopySerialized(profile, want);
                ApplyAtlas(want.Atlas, json);

                // 2 material
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                // a file that is there but is not what this command makes is never written over (CreateAsset would replace it)
                if (material == null && File.Exists(Harness303.Abs(MaterialPath)))
                    throw new Refuse(MaterialPath + " exists but does not load as a Material: move it away first (nothing was overwritten)");
                if (profile == null && File.Exists(Harness303.Abs(ProfilePath)))
                    throw new Refuse(ProfilePath + " exists but does not load as HudLiquid308ProfileSO (script not compiled, or another asset): nothing was overwritten");
                if (material == null)
                {
                    if (dry) log.Add("DRY would create " + MaterialPath);
                    else
                    {
                        Guard(MaterialPath); EnsureFolder(MaterialFolder);
                        var e = Before(ledger, MaterialPath, backup, "material");
                        material = new Material(shader) { name = "M_InkVessel308" };
                        ApplyMaterial(material, shader, style, want);
                        AssetDatabase.CreateAsset(material, MaterialPath);
                        AssetDatabase.SaveAssetIfDirty(material);
                        After(e); written++;
                        log.Add("CREATED " + MaterialPath);
                    }
                }
                else
                {
                    var probe = new Material(material);
                    ApplyMaterial(probe, shader, style, want);
                    bool changed = MaterialState(probe) != MaterialState(material);
                    Object.DestroyImmediate(probe);
                    if (!changed) log.Add("변경 없음 " + MaterialPath);
                    else if (dry) log.Add("DRY would update " + MaterialPath);
                    else
                    {
                        Guard(MaterialPath);
                        if (EditorUtility.IsDirty(material)) throw new Refuse(MaterialPath + " has unsaved in-memory changes (another session?)");
                        var e = Before(ledger, MaterialPath, backup, "material");
                        ApplyMaterial(material, shader, style, want);
                        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
                        After(e); written++;
                        log.Add("UPDATED " + MaterialPath);
                    }
                }

                // 3 profile (Resources: the runtime's only way to it; its references pull the material and the atlas into builds)
                if (profile == null)
                {
                    if (dry) log.Add("DRY would create " + ProfilePath);
                    else
                    {
                        Guard(ProfilePath); EnsureFolder(ResourcesFolder);
                        var e = Before(ledger, ProfilePath, backup, "profile");
                        profile = ScriptableObject.CreateInstance<HudLiquid308ProfileSO>();
                        ApplyAtlas(profile.Atlas, json); profile.Material = material; profile.AtlasTexture = atlas;
                        AssetDatabase.CreateAsset(profile, ProfilePath);
                        AssetDatabase.SaveAssetIfDirty(profile);
                        After(e); written++;
                        log.Add("CREATED " + ProfilePath);
                    }
                }
                else
                {
                    want.Material = material; want.AtlasTexture = atlas;
                    want.name = profile.name;   // CopySerialized copies the name too: the main object keeps the file's name
                    bool changed = JsonUtility.ToJson(want) != JsonUtility.ToJson(profile);
                    if (!changed) log.Add("변경 없음 " + ProfilePath);
                    else if (dry) log.Add("DRY would update " + ProfilePath + (reset ? " (values back to the code defaults)" : ""));
                    else
                    {
                        Guard(ProfilePath);
                        if (EditorUtility.IsDirty(profile)) throw new Refuse(ProfilePath + " has unsaved in-memory changes (another session?)");
                        var e = Before(ledger, ProfilePath, backup, reset ? "profile reset" : "profile");
                        EditorUtility.CopySerialized(want, profile);
                        EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                        After(e); written++;
                        log.Add("UPDATED " + ProfilePath);
                    }
                }

                if (!dry)
                {
                    // the references must be on disk, not only in memory
                    string profileText = File.ReadAllText(Harness303.Abs(ProfilePath));
                    string materialText = File.ReadAllText(Harness303.Abs(MaterialPath));
                    if (!profileText.Contains(AssetDatabase.AssetPathToGUID(MaterialPath))) throw new Exception(ProfilePath + " on disk does not reference the material");
                    if (!profileText.Contains(AssetDatabase.AssetPathToGUID(AtlasPath))) throw new Exception(ProfilePath + " on disk does not reference the atlas");
                    if (!materialText.Contains(AssetDatabase.AssetPathToGUID(ShaderPath))) throw new Exception(MaterialPath + " on disk does not reference " + ShaderName);
                    log.Add("FILES verified: profile -> material + atlas, material -> shader");
                    log.Add(written > 0 ? "WROTE " + written + " file(s); ledger " + LedgerFile : "변경 없음 (nothing written)");
                }
                else log.Add("DRY nothing written");
            }
            finally
            {
                // whatever was written before a refusal / failure stays revertible
                if (!dry && written > 0) WriteLedger(ledger);
                Object.DestroyImmediate(want);
            }
            return string.Join("\n", log);
        }

        static string Enable(bool on)
        {
            var profile = AssetDatabase.LoadAssetAtPath<HudLiquid308ProfileSO>(ProfilePath);
            if (profile == null) throw new Refuse(ProfilePath + " missing (run hud308-setup)");
            if (profile.Enabled == on) return "변경 없음 " + ProfilePath + " Enabled=" + on;
            Guard(ProfilePath);
            if (EditorUtility.IsDirty(profile)) throw new Refuse(ProfilePath + " has unsaved in-memory changes (another session?)");
            var ledger = ReadLedger() ?? new Ledger { created = DateTime.UtcNow.ToString("O") };
            var e = Before(ledger, ProfilePath, BackupDir("-enable"), "Enabled=" + on);
            profile.Enabled = on;
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            After(e); WriteLedger(ledger);
            if (!File.ReadAllText(Harness303.Abs(ProfilePath)).Contains("\n  Enabled: " + (on ? "1" : "0"))) throw new Exception("Enabled did not land on disk");
            return "UPDATED " + ProfilePath + " Enabled=" + on + (on ? " (vessels on the next HUD build)" : " (the #304 meters on the next HUD build)");
        }

        static string Revert()
        {
            var ledger = ReadLedger();
            if (ledger == null || ledger.entries.Count == 0) return "변경 없음 (no ledger at " + LedgerFile + ")";
            var log = new List<string> { "hud308-revert" };
            string backup = BackupDir("-revert");
            // the first entry of a file tells how it was before this ledger: created here, or the pristine bytes
            var first = new Dictionary<string, Entry>();
            foreach (var e in ledger.entries) if (!first.ContainsKey(e.asset)) first.Add(e.asset, e);
            foreach (var pair in first)
            {
                string path = pair.Key; var e = pair.Value;
                string assetPath = path.EndsWith(".meta", StringComparison.Ordinal) ? path.Substring(0, path.Length - 5) : path;
                Guard(assetPath);
                string abs = Harness303.Abs(path);
                if (File.Exists(abs)) { Directory.CreateDirectory(backup); File.Copy(abs, Path.Combine(backup, Path.GetFileName(path)), true); }
                if (e.created)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(path) != null) { AssetDatabase.DeleteAsset(path); log.Add("REMOVED " + path + " (created by this ledger; copy in " + backup + ")"); }
                    else log.Add("변경 없음 " + path + " (already gone)");
                }
                else if (File.Exists(e.backup))
                {
                    File.Copy(e.backup, abs, true);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                    log.Add("RESTORED " + path + " sha " + Harness303.Sha(abs));
                }
                else log.Add("FAILED to restore " + path + ": backup missing " + e.backup);
            }
            string spent = Path.Combine(Out, "ledger_setup.reverted-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture) + ".json");
            File.Move(LedgerFile, spent);
            log.Add("ledger kept as " + spent + "; empty folders (Materials, Resources/UI308) are left in place");
            return string.Join("\n", log);
        }

        // ------------------------------------------------------------------ read-only reports
        static string Status()
        {
            var sb = new StringBuilder("HudLiquid308 status");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            sb.Append("\nSHADER ").Append(ShaderPath).Append(shader == null ? " MISSING" : ShaderUtil.ShaderHasError(shader) ? " ERRORS:" + ShaderErrors(shader) : " ok");
            var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            sb.Append("\nATLAS ").Append(AtlasPath);
            if (importer == null) sb.Append(" MISSING");
            else { var d = ImportDiff(importer); sb.Append(d.Count == 0 ? " import ok" : " import differs: " + string.Join(", ", d)); }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            sb.Append("\nMATERIAL ").Append(MaterialPath).Append(material == null ? " missing" : " shader=" + (material.shader != null ? material.shader.name : "<none>"));
            var profile = AssetDatabase.LoadAssetAtPath<HudLiquid308ProfileSO>(ProfilePath);
            sb.Append("\nPROFILE ").Append(ProfilePath);
            if (profile == null) sb.Append(" missing (the HUD builds the #304 meters)");
            else sb.Append(" Enabled=").Append(profile.Enabled).Append(" material=").Append(profile.Material != null ? profile.Material.name : "<none>")
                .Append(" atlas=").Append(profile.AtlasTexture != null ? profile.AtlasTexture.name : "<none>");
            var ledger = ReadLedger();
            sb.Append("\nLEDGER ").Append(ledger == null ? "none" : ledger.entries.Count + " entries, " + LedgerFile);
            var preview = FindPreview();
            sb.Append("\nPREVIEW ").Append(EditorApplication.isPlaying ? "Play: " + (LiveVessels() != null && LiveVessels().Previewing ? "held" : "off")
                : preview != null ? "on (in memory, HideAndDontSave)" : "off");
            sb.Append("\nPLAY-PROBE phase=").Append(SessionState.GetInt(KPhase, 0));
            var scene = EditorSceneManager.GetActiveScene();
            sb.Append("\nSCENE ").Append(scene.path).Append(" dirty=").Append(scene.isDirty).Append(" playing=").Append(EditorApplication.isPlaying);
            return sb.ToString();
        }

        static string Report()
        {
            var p = AssetDatabase.LoadAssetAtPath<HudLiquid308ProfileSO>(ProfilePath);
            if (p == null) return "HUDLIQUID missing " + ProfilePath + " (run hud308-setup)";
            var l = p.Layout;
            string Liquid(string n, LiquidSpec308 s) => "\n" + n + " spring " + F(s.SpringHz) + " Hz ζ " + F(s.Damping) + " tilt per m/s " + F(s.TiltPerMps, "0.####")
                + " max " + F(s.MaxTilt) + " | wave " + F(s.WaveMaxPx) + " px τ " + F(s.WaveTau) + " per m/s " + F(s.WavePerMps) + " heave per m/s " + F(s.HeavePerMps)
                + " | drain τ " + F(s.DrainTau) + " min " + F(s.DrainMinSpeed) + "/s fill τ " + F(s.FillTau)
                + " | wet " + (s.WetFromStyle ? "from style" : "α " + F(s.WetAlpha) + " hold " + F(s.WetHold) + " dry " + F(s.WetDry))
                + " | kick " + F(s.KickTilt) + " /s " + F(s.KickWavePx) + " px, damage jolt " + F(s.DropKickTilt) + " /s";
            return "HUDLIQUID " + ProfilePath + " Enabled=" + p.Enabled
                + "\nLAYOUT hp " + l.Hp + " ink " + l.Ink + " arc " + l.ArcCentre + " r " + F(l.ArcRadius) + " deg " + F(l.DodgeDeg) + "/" + F(l.JumpDeg) + "/" + F(l.VehicleDeg)
                + " mark " + F(l.MarkSize) + " scale " + F(l.ClusterScale) + " followUiScale " + l.FollowUiScale
                + Liquid("HP ", p.Hp) + Liquid("INK", p.Ink)
                + "\nMOTION (D308-11b: player actions only; no idle term) speed dead zone " + F(p.Motion.SpeedDeadzone) + " m/s step clamp " + F(p.Motion.StepClamp)
                + " m/s | fast turn over " + F(p.Motion.TurnDeadzone) + " deg/s = " + F(p.Motion.TurnMpsPer180) + " m/s per 180 | full value change " + F(p.Motion.LevelFullDelta)
                + " | rest tilt " + F(p.Motion.RestTilt, "0.####") + " wave " + F(p.Motion.RestWavePx) + " px"
                + "\nLOW hpDarken " + F(p.Low.HpDarken) + " tide " + p.Low.TideMarks + " α " + F(p.Low.TideAlpha) + " inkStreak " + F(p.Low.InkDryStreak) + " (value and mark only: no tremble)"
                + "\nMARKS dry α " + F(p.Marks.DryAlpha) + " rewet " + p.Marks.RewetMs + " ms dry " + p.Marks.DryMs + " ms poll " + F(p.Marks.VehiclePollSeconds) + " s"
                + "\nKEYS " + (p.Marks.ShowKeys ? "on" : "off") + " keycap " + F(p.Marks.KeySize) + " px at " + p.Marks.KeyOffset + " dry α " + F(p.Marks.KeyDryAlpha)
                + " table rows " + (p.Marks.KeyGlyphs != null ? p.Marks.KeyGlyphs.Length : 0) + " vehicle action '" + p.Marks.VehicleAction + "' else " + p.Marks.VehicleKeyPath
                + "\nIMPACT light " + p.Impact.Enabled + " kick " + p.Impact.KickOnImpact + " gain " + F(p.Impact.LightGain) + " stuck " + F(p.Impact.StuckSeconds) + " s minInterval " + F(p.Impact.MinInterval)
                + " s followHudGlobal " + p.Impact.FollowHudGlobal + " globals " + ImpactFrameProbe308.ImpactGlobal + " / " + ImpactFrameProbe308.HudGlobal
                + " (read in C#) + " + ImpactFrameProbe308.HudPointGlobal + " (the light's side, read by the shader through ImpactHud308.hlsl)"
                + "\nTHEME collar " + p.Theme.Collar + " lids " + p.Theme.Lids + " lacquer #" + ColorUtility.ToHtmlStringRGB(p.Theme.Lacquer)
                + " nacre max channel " + F(NacreMaxChannel(p.Theme) * 255f, "0.#") + " / 255 lid dry α " + F(p.Theme.LidDryAlpha)
                + " collar line ±" + F(p.Atlas.CollarHalfPx) + " px piece " + F(p.Atlas.CollarPiecePx) + " px lid r " + F(p.Atlas.LidRadiusPx) + " px"
                + "\nREFS material " + (p.Material != null ? AssetDatabase.GetAssetPath(p.Material) : "<none>") + " atlas " + (p.AtlasTexture != null ? AssetDatabase.GetAssetPath(p.AtlasTexture) : "<none>");
        }

        sealed class Checks
        {
            public readonly List<string> Lines = new List<string>(); public int Failed, Total;
            public void Add(bool ok, string label) { Total++; if (!ok) Failed++; Lines.Add((ok ? "ok      " : "FAILED  ") + label); }
            public string Finish(string name, string file)
            {
                string head = name + " " + (Failed == 0 ? "ok " + Total + "/" + Total : "FAILED " + Failed + " of " + Total) + " (" + DateTime.UtcNow.ToString("O") + ")";
                string text = head + "\n" + string.Join("\n", Lines);
                Directory.CreateDirectory(Out); File.WriteAllText(Path.Combine(Out, file), text + "\n");
                return text;
            }
        }

        // ------------------------------------------------------------------ checks:data (Edit, read-only)
        static string ChecksData()
        {
            var c = new Checks();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            c.Add(shader != null, "shader asset " + ShaderPath);
            if (shader != null)
            {
                c.Add(!ShaderUtil.ShaderHasError(shader), ShaderName + " compiles" + (ShaderUtil.ShaderHasError(shader) ? ":" + ShaderErrors(shader) : ""));
                c.Add(shader.name == ShaderName, "shader name " + shader.name);
                int hdr = 0, emission = 0;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.HDR) != 0) hdr++;
                    if (shader.GetPropertyName(i).IndexOf("Emission", StringComparison.OrdinalIgnoreCase) >= 0) emission++;
                }
                c.Add(hdr == 0 && emission == 0, "no HDR colour property, no emission property (AC-H9.1) hdr=" + hdr + " emission=" + emission);
                string source = File.ReadAllText(Harness303.Abs(ShaderPath));
                c.Add(source.Contains("color.rgb = saturate(res.rgb)"), "fragment output is clamped to 0..1 (AC-H9.1)");
                c.Add(source.Contains("_LinearInkGamma") && source.Contains("_LinearPaperGamma"), "the #304 linear-space alpha remap properties exist");
                // D308-10b: one impact lights every HUD element from the same side = the vessel shader takes the point from the
                // deploy layer's shared include, like UI/InkMeter and UI/InkReveal; nothing here sets a global
                c.Add(source.Contains(ImpactInclude) && source.Contains("_OhImpactHudPoint308") && !source.Contains("SetGlobal"),
                    "the light's side comes from the shared include " + ImpactInclude + " (_OhImpactHudPoint308)");
                c.Add(File.Exists(Harness303.Abs(ImpactInclude)), "the deploy layer's include is in the project: " + ImpactInclude);
                c.Add(!source.Contains("_Time") && !source.Contains("_SinTime"), "nothing in the shader runs on time (D308-11b: no idle motion; nacre shimmer off: a still colour)");
                c.Add(source.Contains("_KeyGrid") && source.Contains("_KeyCell"), "the shader draws the key glyph quad (D308-11b: code 6, cells of the key block)");
            }
            var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            c.Add(importer != null, "atlas " + AtlasPath);
            if (importer != null) { var d = ImportDiff(importer); c.Add(d.Count == 0, "atlas import settings (linear, mips, Clamp, uncompressed)" + (d.Count > 0 ? ": " + string.Join(", ", d) : "")); }
            var json = ReadAtlasJson();
            c.Add(json != null, "atlas json " + AtlasJsonPath);
            if (json != null && importer != null)
                c.Add(string.Equals(Harness303.Sha(Harness303.Abs(AtlasPath)), json.atlas.sha256, StringComparison.OrdinalIgnoreCase), "atlas png sha256 = hud308_atlas.json");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var profile = AssetDatabase.LoadAssetAtPath<HudLiquid308ProfileSO>(ProfilePath);
            c.Add(material != null && material.shader != null && material.shader.name == ShaderName, "material " + MaterialPath + " on " + ShaderName);
            c.Add(profile != null, "profile " + ProfilePath);
            if (profile != null)
            {
                c.Add(profile.Material == material && material != null, "profile.Material = M_InkVessel308");
                c.Add(profile.AtlasTexture != null && AssetDatabase.GetAssetPath(profile.AtlasTexture) == AtlasPath, "profile.AtlasTexture = hud308_atlas");
                var loaded = Resources.Load<HudLiquid308ProfileSO>(HudLiquid308ProfileSO.ResourcePath);
                c.Add(loaded == profile, "Resources.Load(\"" + HudLiquid308ProfileSO.ResourcePath + "\") finds the profile");
                float life = profile.Ink.WetHold + profile.Ink.WetDry;
                c.Add(profile.Ink.WetFromStyle || (life >= 3f && life <= 5f), "ink wet film lives 3-5 s like world residue (D308-10b): " + F(life) + " s");
                c.Add(profile.Impact.MinInterval >= 1f / 3f - 1e-4f, "impact light at most 3 per second: min interval " + F(profile.Impact.MinInterval) + " s");
                c.Add(profile.Layout.ClusterScale >= .8f && profile.Layout.ClusterScale <= 1.3f, "cluster scale in .8-1.3: " + F(profile.Layout.ClusterScale));
                // D308-11b key glyphs: one symbol cell per key. Which key = the real binding (checks:sim / checks:play)
                {
                    var mk = profile.Marks; int rows = mk.KeyGlyphs != null ? mk.KeyGlyphs.Length : 0, unknown = 0;
                    for (int i = 0; i < rows; i++) if (mk.KeyGlyphs[i] == null || HudKeyGlyph308.CellByName(mk.KeyGlyphs[i].Cell) < 0) unknown++;
                    c.Add(unknown == 0, "key table: every row names a cell of the atlas key block (" + (rows - unknown) + " of " + rows + ")");
                    c.Add(mk.KeySize >= 12f && mk.KeySize <= 24f && mk.KeyDryAlpha > 0f && mk.KeyDryAlpha < 1f && profile.Atlas.KeyColumns >= 1f && profile.Atlas.KeyBoxPx <= mk.KeySize,
                        "key glyph: keycap " + F(mk.KeySize) + " px (12-24), symbol box " + F(profile.Atlas.KeyBoxPx) + " px, dry α " + F(mk.KeyDryAlpha));
                    c.Add(!string.IsNullOrEmpty(mk.VehicleKeyPath) && HudKeyGlyph308.Cell(mk.VehicleKeyPath, mk.KeyGlyphs) >= 0,
                        "vehicle key path (the summon polls a key; no action yet): " + mk.VehicleKeyPath + " -> cell " + HudKeyGlyph308.Cell(mk.VehicleKeyPath, mk.KeyGlyphs));
                    c.Lines.Add("info    key glyphs " + (mk.ShowKeys ? "on" : "off") + " (D308-11b: on). They are a second quad of each mark's graphic: no text object, no extra GameObject");
                }
                // theme (D308-15): the shell is an LDR colour below the paper value, the lacquer is the darkest value of the cluster
                float nacreMax = NacreMaxChannel(profile.Theme);
                c.Add(nacreMax <= 217.5f / 255f, "nacre formula max channel on its hue arc <= 217 / 255 (SPEC-UI-THEME-308 AC-T3): " + F(nacreMax * 255f, "0.#"));
                c.Add(profile.Theme.NacreArcDeg.x >= 150f && profile.Theme.NacreArcDeg.y <= 340f && profile.Theme.NacreArcDeg.x < profile.Theme.NacreArcDeg.y,
                    "nacre hue arc inside 150-340 deg (no yellow): " + F(profile.Theme.NacreArcDeg.x) + "-" + F(profile.Theme.NacreArcDeg.y));
                c.Add(profile.Theme.Lacquer.maxColorComponent <= .12f, "lacquer is a dark flat value: max channel " + F(profile.Theme.Lacquer.maxColorComponent * 255f, "0.#") + " / 255");
                c.Lines.Add("info    theme: collar " + profile.Theme.Collar + ", lids " + profile.Theme.Lids + " (both off = the look before D308-15; values live in the profile until UiTheme308SO exists)");
                if (json != null)
                {
                    var probe = ScriptableObject.CreateInstance<HudLiquid308ProfileSO>();
                    ApplyAtlas(probe.Atlas, json);
                    c.Add(JsonUtility.ToJson(probe.Atlas) == JsonUtility.ToJson(profile.Atlas), "profile atlas block = hud308_atlas.json");
                    Object.DestroyImmediate(probe);
                }
                if (material != null && shader != null)
                {
                    var style = FindStyle(out string stylePath);
                    var probe = new Material(material);
                    ApplyMaterial(probe, shader, style, profile);
                    c.Add(MaterialState(probe) == MaterialState(material), "material values = profile + style (" + stylePath + "); stale = rerun hud308-setup");
                    Object.DestroyImmediate(probe);
                    float brightest = 0f;
                    foreach (var n in MaterialColours) { var col = material.GetColor(n); brightest = Mathf.Max(brightest, col.maxColorComponent); }
                    float paper = material.GetColor("_PaperColor").maxColorComponent;
                    c.Add(brightest <= 1f && brightest <= paper + 1e-4f, "material colours are LDR and none is brighter than paper (max " + F(brightest) + ", paper " + F(paper) + ")");
                    c.Add(HudTokens304.ShaderInkGamma(material) >= 1f, "HudTokens304.ShaderInkGamma reads the material: " + F(HudTokens304.ShaderInkGamma(material)));
                }
            }
            var scene = EditorSceneManager.GetActiveScene();
            c.Lines.Add("info    scene " + scene.path + " dirty=" + scene.isDirty + " (this command writes nothing)");
            return c.Finish("checks:data", "checks-data.txt");
        }

        // ------------------------------------------------------------------ checks:sim (Edit, read-only; the real C# model)
        static HudLiquid308ProfileSO ProfileOrDefaults(out bool temporary)
        {
            var p = AssetDatabase.LoadAssetAtPath<HudLiquid308ProfileSO>(ProfilePath);
            temporary = p == null;
            return p != null ? p : ScriptableObject.CreateInstance<HudLiquid308ProfileSO>();
        }

        static float ReachTime(in LiquidParams308 p, float from, float to, float tolerance)
        {
            var s = default(LiquidState308); LiquidSim308.Reset(ref s, from); LiquidSim308.SetValue(ref s, in p, to);
            const float dt = 1f / 240f;
            for (float t = dt; t < 6f; t += dt)
            {
                LiquidSim308.Step(ref s, in p, false, dt);
                if (Mathf.Abs(s.Level - s.Value) <= tolerance) return t;
            }
            return float.PositiveInfinity;
        }

        // one sudden sideways speed change of `mps`, then the free swing at `fps`: the first peak, the visible swings after it,
        // and (at != null) the tilt at the given times (frames are cut to land on them)
        static void Impulse(in LiquidParams308 p, float mps, float fps, float until, out float peak, out int swings, float[] at, float[] tilt)
        {
            var s = default(LiquidState308); LiquidSim308.Reset(ref s, .5f);
            var a = new ActionSample308 { LateralStep = mps };
            LiquidSim308.Drive(ref s, in p, in a, 1f);
            float dt = 1f / fps, t = 0f, last = 0f; peak = 0f; swings = 0; int next = 0;
            while (t < until - 1e-6f)
            {
                float step = Mathf.Min(dt, until - t);
                if (at != null && next < at.Length && t + step > at[next] - 1e-6f) step = Mathf.Max(1e-5f, at[next] - t);
                LiquidSim308.Step(ref s, in p, false, step); t += step;
                if (at != null && next < at.Length && Mathf.Abs(t - at[next]) < 1e-4f) tilt[next++] = s.Tilt;
                peak = Mathf.Max(peak, Mathf.Abs(s.Tilt));
                // a turning point further than 2 % of the first peak from the level surface = one visible swing
                if (last != 0f && (s.TiltSpeed > 0f) != (last > 0f) && Mathf.Abs(s.Tilt) > .02f * peak) swings++;
                last = s.TiltSpeed;
            }
        }

        static float RestTime(in LiquidParams308 p)
        {
            var s = default(LiquidState308); LiquidSim308.Reset(ref s, .5f);
            s.Tilt = p.MaxTilt; s.WavePx = p.WaveMaxPx; s.Still = false;
            const float dt = 1f / 60f;
            for (float t = dt; t < 8f; t += dt) { LiquidSim308.Step(ref s, in p, false, dt); if (s.Still) return t; }
            return float.PositiveInfinity;
        }

        static string ChecksSim()
        {
            var c = new Checks();
            var profile = ProfileOrDefaults(out bool temporary);
            var style = FindStyle(out string stylePath);
            c.Lines.Add("info    profile " + (temporary ? "<code defaults: no asset yet>" : ProfilePath) + ", style " + stylePath);
            var hp = profile.Hp.Resolve(profile, style, .45f);
            var ink = profile.Ink.Resolve(profile, style, .45f);

            // AC-H3.2 the visible surface reaches the value
            float hpDrain = ReachTime(in hp, 1f, 0f, .02f), inkDrain = ReachTime(in ink, 1f, 0f, .02f);
            c.Add(hpDrain <= .30f && inkDrain <= .45f, "AC-H3.2 drain of a full swing to ±.02: hp " + F(hpDrain) + " s (≤ .30), ink " + F(inkDrain) + " s (≤ .45)");
            float hpFill3 = ReachTime(in hp, .3f, .6f, .02f), inkFill3 = ReachTime(in ink, .3f, .6f, .02f);
            c.Add(hpFill3 <= .7f && inkFill3 <= .9f, "AC-H3.2 fill of .3 to ±.02: hp " + F(hpFill3) + " s (≤ .7), ink " + F(inkFill3) + " s (≤ .9)");
            float hpFill1 = ReachTime(in hp, 0f, 1f, .02f), inkFill1 = ReachTime(in ink, 0f, 1f, .02f);
            c.Add(hpFill1 <= 1.0f && inkFill1 <= 1.3f, "AC-H3.2 fill of 1.0 to ±.02: hp " + F(hpFill1) + " s (≤ 1.0), ink " + F(inkFill1) + " s (≤ 1.3)");

            // AC-H4.1 one sudden sideways speed change: the first peak is TiltPerMps x the change; HP swings on, ink does not
            Impulse(in hp, 6f, 240f, 5f, out float hpPeak, out int hpSwings, null, null); Impulse(in ink, 6f, 240f, 5f, out float inkPeak, out int inkSwings, null, null);
            float hpWant = profile.Hp.TiltPerMps * 6f, inkWant = profile.Ink.TiltPerMps * 6f;
            c.Add(Mathf.Abs(hpPeak - hpWant) <= .05f * hpWant && hpSwings >= 3, "AC-H4.1 hp: a sudden 6 m/s peaks at " + F(hpPeak, "0.####") + " (" + F(hpWant, "0.####") + " ±5 %), swings over 2 %: " + hpSwings + " (≥ 3)");
            c.Add(Mathf.Abs(inkPeak - inkWant) <= .05f * inkWant && inkSwings <= 2, "AC-H4.1 ink: a sudden 6 m/s peaks at " + F(inkPeak, "0.####") + " (" + F(inkWant, "0.####") + " ±5 %), swings over 2 %: " + inkSwings + " (≤ 2)");

            // AC-H4.2 the same swing at 30 / 60 / 144 fps
            {
                float worst = 0f; var at = new[] { .25f, .5f, 1f }; var a = new float[3]; var b = new float[3]; var d = new float[3];
                Impulse(in hp, 6f, 30f, 1.001f, out _, out _, at, a); Impulse(in hp, 6f, 60f, 1.001f, out _, out _, at, b); Impulse(in hp, 6f, 144f, 1.001f, out _, out _, at, d);
                for (int i = 0; i < 3; i++) worst = Mathf.Max(worst, Mathf.Max(a[i], Mathf.Max(b[i], d[i])) - Mathf.Min(a[i], Mathf.Min(b[i], d[i])));
                c.Add(worst <= .01f, "AC-H4.2 tilt at .25 / .5 / 1.0 s after the same shove differs by " + F(worst, "0.#####") + " between 30, 60 and 144 fps (≤ .01)");
            }

            // AC-H4.3 it comes to rest
            float hpRest = RestTime(in hp), inkRest = RestTime(in ink);
            c.Add(hpRest <= 3f && inkRest <= 3f, "AC-H4.3 from max tilt + max ripple to Still: hp " + F(hpRest) + " s, ink " + F(inkRest) + " s (≤ 3)");

            // D308-11b / AC-H16.1: with nothing done nothing moves - 10 s of frames with empty samples at four levels (low HP too)
            {
                float moved = 0f; bool still = true; var none = default(ActionSample308);
                foreach (float level in new[] { 1f, .62f, .2f, .05f })
                {
                    var s = default(LiquidState308); var k = default(LiquidState308); LiquidSim308.Reset(ref s, level); LiquidSim308.Reset(ref k, level);
                    for (int i = 0; i < 600; i++)
                    {
                        LiquidSim308.Frame(ref s, ref k, in hp, in ink, in none, profile.Motion.TiltSign, false, 1f / 60f);
                        moved = Mathf.Max(moved, Mathf.Abs(s.Tilt) + s.WavePx + Mathf.Abs(k.Tilt) + k.WavePx + Mathf.Abs(s.Phase) + Mathf.Abs(k.Phase));
                        still &= s.Still && k.Still;
                    }
                }
                c.Add(moved == 0f && still, "AC-H16.1 standing still, 10 s at levels 1 / .62 / .2 / .05: tilt + ripple + phase " + F(moved, "0.######") + ", Still throughout: " + still);
            }

            // ActionMeter308: only what the body really did is measured
            {
                var spec = profile.Motion; const float dt = 1f / 60f;
                var meter = default(ActionMeter308); float startSum = 0f, steady = 0f, stopSum = 0f, teleport = 0f, speed = 0f; Vector3 pos = Vector3.zero;
                for (int i = 0; i < 300; i++)
                {
                    float want = i >= 60 && i < 180 ? 4.5f : 0f;
                    speed = Mathf.MoveTowards(speed, want, (want < speed ? 22f : 16f) * dt);
                    pos += Vector3.right * speed * dt;
                    if (i == 240) pos += Vector3.right * 40f;
                    var a = meter.Sample(pos, Vector3.right, 0f, dt, 0, 0, 0f, spec);
                    if (i >= 60 && i < 100) startSum += a.LateralStep;
                    if (i == 150) steady = Mathf.Abs(a.LateralStep);
                    if (i >= 180 && i < 220) stopSum += a.LateralStep;
                    if (i >= 240 && i < 244) teleport = Mathf.Max(teleport, Mathf.Abs(a.LateralStep) + Mathf.Abs(a.ForwardStep));
                }
                var look = default(ActionMeter308); float yaw = 0f, slow = 0f;
                for (int i = 0; i < 180; i++) { yaw += spec.TurnDeadzone * .6f * dt; slow = Mathf.Max(slow, Mathf.Abs(look.Sample(Vector3.zero, Vector3.right, yaw, dt, 0, 0, 0f, spec).LateralStep)); }
                var held = default(ActionMeter308); float wall = 0f;
                for (int i = 0; i < 120; i++) { var a = held.Sample(Vector3.one, Vector3.right, 0f, dt, 0, 0, 0f, spec); wall = Mathf.Max(wall, Mathf.Abs(a.LateralStep) + Mathf.Abs(a.ForwardStep)); }
                // a pause (dt 0) while the body runs, the menu stops it, the game resumes with the body standing: not an action
                var pausedMeter = default(ActionMeter308); float paused = 0f; Vector3 at = Vector3.zero;
                for (int i = 0; i < 120; i++) { at += Vector3.right * 5.5f * dt; pausedMeter.Sample(at, Vector3.right, 0f, dt, 0, 0, 0f, spec); }
                for (int i = 0; i < 30; i++) pausedMeter.Sample(at, Vector3.right, 0f, 0f, 0, 0, 0f, spec);
                for (int i = 0; i < 60; i++) { var a = pausedMeter.Sample(at, Vector3.right, 0f, dt, 0, 0, 0f, spec); paused = Mathf.Max(paused, Mathf.Abs(a.LateralStep) + Mathf.Abs(a.ForwardStep)); }
                // a change is handed on in pieces of at least StepBand: the start may keep less than one band back, and the stop returns all of it
                c.Add(Mathf.Abs(startSum - 4.5f) <= spec.StepBand + .01f && Mathf.Abs(startSum + stopSum) < .01f && steady == 0f && teleport == 0f && slow == 0f && wall == 0f && paused == 0f,
                    "ActionMeter308: a 4.5 m/s start sums to " + F(startSum) + " (within the band " + F(spec.StepBand) + "), start + stop " + F(startSum + stopSum) + ", steady " + F(steady)
                    + ", a 40 m placement " + F(teleport) + ", looking round under the dead zone " + F(slow) + ", a body held against a wall " + F(wall) + ", a stop across a pause " + F(paused));
            }

            // AC-H16.9 (review 2026-10-04): speed that builds up over many frames with no sideways part - a walk / a run straight ahead,
            // a car pulling away. Each frame's share is under the rest thresholds; the answer must be there all the same, at any frame rate
            {
                var limits = new HudLiquid308Timeline.Limits(); var spec = profile.Motion; bool all = true; string note = "";
                foreach (float fps in new[] { 30f, 60f, 144f, 240f })
                {
                    var walk = HudLiquid308Straight.Run(in hp, in ink, spec, limits, fps, 2.2f, 16f, 22f, spec.TiltSign);
                    var run = HudLiquid308Straight.Run(in hp, in ink, spec, limits, fps, 5.5f, 16f, 22f, spec.TiltSign);
                    var car = HudLiquid308Straight.Run(in hp, in ink, spec, limits, fps, 14f, 5f, 8f, spec.TiltSign);
                    all &= walk.HpPeakPx >= .4f && run.HpPeakPx >= 1f && car.HpPeakPx >= 1f && walk.HpPeakPx < run.HpPeakPx
                        && walk.CruiseMaxPx == 0f && run.CruiseMaxPx == 0f && car.CruiseMaxPx == 0f
                        && walk.SettleSeconds <= limits.SettleSeconds && run.SettleSeconds <= limits.SettleSeconds && car.SettleSeconds <= limits.SettleSeconds;
                    note += (note.Length > 0 ? " | " : "") + fps.ToString("0") + " fps: walk " + F(walk.HpPeakPx, "0.##") + ", run " + F(run.HpPeakPx, "0.##") + ", car " + F(car.HpPeakPx, "0.##");
                }
                c.Add(all, "AC-H16.9 straight ahead (no sideways part) is answered at every frame rate, flat at one speed (HP px at the glass): " + note);
            }

            // AC-H16: the scripted timeline (stand, walk, stop, run, stop, dodge, jump + land, fast turn, hit, ink spent, ink poured,
            // impact kick, stand) through ActionMeter308 -> LiquidSim308.Frame, at 60 and 144 fps
            {
                var body = new HudLiquid308Timeline.Body(); var limits = new HudLiquid308Timeline.Limits(); var spec = profile.Motion;
                bool all = true; string note = "";
                foreach (float fps in new[] { 60f, 144f })
                {
                    var full = HudLiquid308Timeline.Run(in hp, in ink, spec, body, limits, fps, false, true, spec.TiltSign);
                    var none = HudLiquid308Timeline.Run(in hp, in ink, spec, body, limits, fps, false, false, spec.TiltSign);
                    var reduced = HudLiquid308Timeline.Run(in hp, in ink, spec, body, limits, fps, true, true, spec.TiltSign);
                    var r = HudLiquid308Timeline.Check(full, none, reduced, body, limits);
                    all &= r.Ok;
                    if (fps > 100f) continue;
                    float settle = 0f; for (int i = 0; i < r.SettleSeconds.Length; i++) settle = Mathf.Max(settle, r.SettleSeconds[i]);
                    c.Add(r.Calm, "AC-H16.1 flat while standing: first 3 s " + F(r.StandFirstMaxPx, "0.####") + " px, last 5 s " + F(r.StandLastMaxPx, "0.####") + " px, "
                        + r.RestFrames + " resting frames at ≤ " + F(limits.RestPx) + " px");
                    c.Add(r.Responds && r.Proportional, "AC-H16.1 every action is answered, in proportion (HP px at the glass): walk " + F(r.HpPeakPx[HudLiquid308Timeline.SegWalk], "0.##")
                        + " < run " + F(r.HpPeakPx[HudLiquid308Timeline.SegRun], "0.##") + " < dodge " + F(r.HpPeakPx[HudLiquid308Timeline.SegDodge], "0.##")
                        + ", jump + land " + F(r.HpPeakPx[HudLiquid308Timeline.SegJump], "0.##") + ", fast turn " + F(r.HpPeakPx[HudLiquid308Timeline.SegTurn], "0.##")
                        + ", hit " + F(r.HpPeakPx[HudLiquid308Timeline.SegHit], "0.##") + ", impact " + F(r.HpPeakPx[HudLiquid308Timeline.SegImpact], "0.##")
                        + "; ink: spent " + F(r.InkPeakPx[HudLiquid308Timeline.SegInkSpent], "0.##") + ", poured " + F(r.InkPeakPx[HudLiquid308Timeline.SegInkRefill], "0.##"));
                    c.Add(r.Settles, "AC-H16.1 at rest again within " + F(limits.SettleSeconds) + " s of every action: longest " + F(settle, "0.##") + " s");
                    c.Add(r.ReadingKept, "AC-H16.2 the reading does not move with the slosh: level drift " + F(r.LevelDriftMax, "0.#######") + ", value drift " + F(r.ValueDriftMax, "0.#######") + " against the action-free run");
                    c.Add(r.ReducedFlat, "AC-H16.3 reduced motion: tilt " + F(r.ReducedMaxTilt, "0.######") + ", ripple " + F(r.ReducedMaxWavePx, "0.######") + ", thread " + F(r.ReducedMaxPour, "0.######") + " over the whole timeline");
                    note = "60 fps " + r.Ok;
                }
                c.Add(all, "AC-H16 the same timeline holds at 144 fps (" + note + ", both " + all + ")");
            }

            // D308-11b / AC-H15: the key of a mark is the real binding. Probe actions only (made here, never enabled; the game's
            // own actions are not touched and nothing is rebound)
            {
                var table = profile.Marks.KeyGlyphs;
                var shift = new InputAction("probe308_dodge", InputActionType.Button, "<Keyboard>/leftShift");
                var space = new InputAction("probe308_jump", InputActionType.Button, "<Keyboard>/space");
                var both = new InputAction("probe308_both", InputActionType.Button, "<Keyboard>/j"); both.AddBinding("<Gamepad>/buttonSouth");
                int a = HudKeyGlyph308.Cell(HudKeyBinding308.Path(shift, false), table), b = HudKeyGlyph308.Cell(HudKeyBinding308.Path(space, false), table);
                int desk = HudKeyGlyph308.Cell(HudKeyBinding308.Path(both, false), table), pad = HudKeyGlyph308.Cell(HudKeyBinding308.Path(both, true), table);
                space.ApplyBindingOverride(0, "<Keyboard>/k");                    // a rebinding of the PROBE: the glyph must follow
                int rebound = HudKeyGlyph308.Cell(HudKeyBinding308.Path(space, false), table);
                space.RemoveAllBindingOverrides();
                int back = HudKeyGlyph308.Cell(HudKeyBinding308.Path(space, false), table);
                c.Add(a == HudKeyGlyph308.Shift && b == HudKeyGlyph308.Space && desk == HudKeyGlyph308.CellByName("J") && pad == HudKeyGlyph308.CellByName("A")
                    && rebound == HudKeyGlyph308.CellByName("K") && back == HudKeyGlyph308.Space && HudKeyGlyph308.Cell(null, table) == HudKeyGlyph308.None,
                    "AC-H15.1 the key glyph follows the binding: leftShift -> " + a + " (Shift " + HudKeyGlyph308.Shift + "), space -> " + b + " (Space " + HudKeyGlyph308.Space
                    + "), j | pad south -> " + desk + " / " + pad + " by the device used last, an override to k -> " + rebound + ", removed -> " + back);
                shift.Dispose(); space.Dispose(); both.Dispose();
                // the project's own asset: what the three marks show today
                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
                if (asset == null) c.Lines.Add("info    Assets/InputSystem_Actions.inputactions not found: the live bindings were not read");
                else
                {
                    string dodgePath = HudKeyBinding308.Path(HudKeyBinding308.Find(asset, "Gameplay", "Dodge"), false), jumpPath = HudKeyBinding308.Path(HudKeyBinding308.Find(asset, "Gameplay", "Jump"), false);
                    var vehicle = HudKeyBinding308.Find(asset, "Gameplay", profile.Marks.VehicleAction);
                    string vehiclePath = vehicle != null ? HudKeyBinding308.Path(vehicle, false) : profile.Marks.VehicleKeyPath;
                    c.Add(HudKeyGlyph308.Cell(dodgePath, table) >= 0 && HudKeyGlyph308.Cell(jumpPath, table) >= 0 && HudKeyGlyph308.Cell(vehiclePath, table) >= 0,
                        "AC-H15.1 live bindings: dodge " + dodgePath + " -> cell " + HudKeyGlyph308.Cell(dodgePath, table) + ", jump " + jumpPath + " -> cell " + HudKeyGlyph308.Cell(jumpPath, table)
                        + ", vehicle " + vehiclePath + (vehicle != null ? " (action)" : " (profile path: the summon polls a key)") + " -> cell " + HudKeyGlyph308.Cell(vehiclePath, table));
                }
            }

            // review 2026-10-04: the slosh cannot run away. The hardest input (twice the largest speed change the meter lets through,
            // both ways), a 4x kick every frame, a 0.33 s hitch every 7th frame, then a NaN shove: the slope stays under TiltLimit,
            // stays finite, and rests.
            {
                bool ok = true; string note = "";
                for (int which = 0; which < 2; which++)
                {
                    var p = which == 0 ? hp : ink;
                    var s = default(LiquidState308); LiquidSim308.Reset(ref s, .5f); float peak = 0f; const float dt = 1f / 30f;
                    for (int i = 0; i < 300; i++)
                    {
                        var hard = new ActionSample308 { LateralStep = i % 3 == 0 ? -28f : 28f, ForwardStep = 14f, JumpSpeed = 14f, LandSpeed = 14f };
                        LiquidSim308.Kick(ref s, in p, p.KickTilt * 4f * (i % 2 == 0 ? 1f : -.5f), p.KickWavePx);
                        LiquidSim308.Drive(ref s, in p, in hard, -1f);
                        LiquidSim308.Step(ref s, in p, false, i % 7 == 0 ? dt * 10f : dt);
                        peak = Mathf.Max(peak, Mathf.Abs(s.Tilt));
                    }
                    LiquidSim308.Kick(ref s, in p, float.NaN, float.NaN);
                    var nan = new ActionSample308 { LateralStep = float.NaN };
                    LiquidSim308.Drive(ref s, in p, in nan, -1f);
                    LiquidSim308.Step(ref s, in p, false, dt);
                    bool finite = !float.IsNaN(s.Tilt) && !float.IsNaN(s.TiltSpeed) && !float.IsNaN(s.WavePx) && !float.IsNaN(s.Level);
                    float rest = float.PositiveInfinity;
                    for (float t = dt; t < 8f; t += dt) { LiquidSim308.Step(ref s, in p, false, dt); if (s.Still) { rest = t; break; } }
                    var before = s; LiquidSim308.Step(ref s, in p, false, 0f);
                    bool frozen = before.Tilt == s.Tilt && before.Level == s.Level && before.Phase == s.Phase;
                    ok &= peak <= p.TiltLimit + 1e-4f && p.TiltLimit >= p.MaxTilt && finite && rest <= 3f && frozen;
                    note += (which == 0 ? "hp" : ", ink") + " peak " + F(peak) + " (cap " + F(p.TiltLimit) + "), finite " + finite + ", rests in " + F(rest) + " s, dt 0 frozen " + frozen;
                }
                c.Add(ok, "slosh cannot run away (shoves + kicks + hitches + NaN): " + note);
            }

            // AC-H5.1 / 5.2 wet film
            {
                var s = default(LiquidState308); LiquidSim308.Reset(ref s, .8f); LiquidSim308.SetValue(ref s, in hp, .5f);
                bool placed = Mathf.Abs(s.WetTop - .8f) < 1e-4f && Mathf.Abs(LiquidSim308.WetAlphaNow(in s, in hp) - style.Meter.LagAlpha) < .03f;
                float gone = -1f; const float dt = 1f / 240f; float atHold = -1f;
                for (float t = dt; t < 8f; t += dt)
                {
                    LiquidSim308.Step(ref s, in hp, false, dt);
                    if (atHold < 0f && t >= hp.WetHold - dt) atHold = LiquidSim308.WetAlphaNow(in s, in hp);
                    if (LiquidSim308.WetAlphaNow(in s, in hp) <= 0f) { gone = t; break; }
                }
                c.Add(placed && Mathf.Abs(atHold - hp.WetAlpha) < .03f && Mathf.Abs(gone - (hp.WetHold + hp.WetDry)) <= .02f,
                    "AC-H5.1 hp .8 -> .5: film top " + F(s.WetTop) + ", α " + F(atHold) + " through the hold, gone at " + F(gone) + " s (" + F(hp.WetHold) + " + " + F(hp.WetDry) + ")");
                float inkLife = ink.WetHold + ink.WetDry;
                c.Add(inkLife >= 3f && inkLife <= 5f, "AC-H5.2 ink film life " + F(inkLife) + " s (3-5, D308-10b)");
            }

            // AC-H5.4 natural regeneration pours nothing
            {
                var s = default(LiquidState308); LiquidSim308.Reset(ref s, .2f); float pour = 0f, wave = 0f, tilt = 0f; bool fresh = false; const float dt = 1f / 60f;
                for (float t = 0f; t < 3f; t += dt)
                {
                    LiquidSim308.SetValue(ref s, in ink, s.Value + .05f * dt);
                    LiquidSim308.Step(ref s, in ink, false, dt);
                    pour = Mathf.Max(pour, s.Pour); wave = Mathf.Max(wave, s.WavePx); tilt = Mathf.Max(tilt, Mathf.Abs(s.Tilt)); fresh |= s.FreshAge >= 0f;
                }
                var j = default(LiquidState308); LiquidSim308.Reset(ref j, .2f); LiquidSim308.SetValue(ref j, in ink, .5f); LiquidSim308.Step(ref j, in ink, false, dt);
                c.Add(pour <= 0f && wave <= 0f && tilt <= 0f && !fresh && j.Pouring && j.FreshAge >= 0f && j.WavePx > 0f,
                    "AC-H5.4 regen .05/s is quiet (no action): thread " + F(pour) + ", ripple " + F(wave) + ", tilt " + F(tilt) + ", fresh " + fresh + "; a +.3 pour stirs the surface: " + F(j.WavePx) + " px");
            }

            // AC-H13 reduced motion
            {
                var s = default(LiquidState308); LiquidSim308.Reset(ref s, 1f); LiquidSim308.SetValue(ref s, in hp, .4f);
                var other = default(LiquidState308); LiquidSim308.Reset(ref other, .5f);
                var shove = new ActionSample308 { LateralStep = 12f, ForwardStep = 12f, JumpSpeed = 6f, LandSpeed = 6f };     // the hardest actions, every frame
                const float dt = 1f / 240f; float reach = -1f, mid = 0f, tilt = 0f;
                for (float t = dt; t < 1f; t += dt)
                {
                    LiquidSim308.Frame(ref s, ref other, in hp, in ink, in shove, profile.Motion.TiltSign, true, dt);
                    tilt = Mathf.Max(tilt, Mathf.Abs(s.Tilt) + s.WavePx + s.Pour + Mathf.Abs(other.Tilt) + other.WavePx);
                    if (Mathf.Abs(t - hp.ReducedSeconds * .5f) < dt * .5f) mid = s.Level;
                    if (reach < 0f && s.Level == s.Value) reach = t;
                }
                c.Add(Mathf.Abs(reach - hp.ReducedSeconds) <= .01f && Mathf.Abs(mid - .7f) <= .02f && tilt <= 0f,
                    "AC-H13.1 reduced motion: level lands at " + F(reach) + " s (" + F(hp.ReducedSeconds) + "), midway " + F(mid) + " (.7), tilt + ripple + thread " + F(tilt));
            }

            // AC-H7.1 the marks' table
            {
                var ready = new HudActionState308 { HasDodge = true, DodgeGateOpen = true, DodgeCooldown01 = 1f, HasJump = true, JumpGateOpen = true, Vehicle = VehicleHud308.CanCall };
                int bad = 0, rows = 0;
                void Row(HudActionState308 s, int mark, bool hidden, bool wet, float fill, HudGlyph308 glyph)
                {
                    HudActionRules308.Evaluate(in s, out var d, out var j, out var v);
                    var m = mark == 0 ? d : mark == 1 ? j : v; rows++;
                    if (m.Hidden != hidden || (!hidden && (m.Wet != wet || Mathf.Abs(m.Fill - fill) > 1e-4f || m.Glyph != glyph))) bad++;
                }
                var s0 = ready;
                Row(s0, 0, false, true, 0f, HudGlyph308.Dodge);
                s0 = ready; s0.HasDodge = false; Row(s0, 0, true, false, 0f, HudGlyph308.Dodge);
                s0 = ready; s0.DodgeCooldown01 = .5f; s0.DodgeGateOpen = false; s0.Dodging = true; Row(s0, 0, false, false, .5f, HudGlyph308.Dodge);
                s0 = ready; s0.DodgeGateOpen = false; Row(s0, 0, false, false, 0f, HudGlyph308.Dodge);
                s0 = ready; s0.Seated = true; Row(s0, 0, false, false, 0f, HudGlyph308.Dodge);
                s0 = ready; Row(s0, 1, false, true, 0f, HudGlyph308.Jump);
                s0 = ready; s0.HasJump = false; Row(s0, 1, true, false, 0f, HudGlyph308.Jump);
                s0 = ready; s0.Airborne = true; s0.JumpGateOpen = false; Row(s0, 1, false, false, 0f, HudGlyph308.Jump);
                s0 = ready; s0.JumpGateOpen = false; Row(s0, 1, false, false, 0f, HudGlyph308.Jump);
                s0 = ready; s0.Seated = true; Row(s0, 1, false, false, 0f, HudGlyph308.Jump);
                s0 = ready; s0.Vehicle = VehicleHud308.Hidden; Row(s0, 2, true, false, 0f, HudGlyph308.Vehicle);
                s0 = ready; Row(s0, 2, false, true, 0f, HudGlyph308.Vehicle);
                s0 = ready; s0.Vehicle = VehicleHud308.OutNear; s0.VehicleOutGlyph = true; Row(s0, 2, false, true, 0f, HudGlyph308.VehicleOut);
                s0 = ready; s0.Vehicle = VehicleHud308.Refused; Row(s0, 2, false, false, 0f, HudGlyph308.Vehicle);
                s0 = ready; s0.Vehicle = VehicleHud308.Busy; s0.VehicleBusy01 = .4f; Row(s0, 2, false, false, .4f, HudGlyph308.Vehicle);
                s0 = ready; s0.Vehicle = VehicleHud308.Seated; s0.Seated = true; Row(s0, 2, false, false, 0f, HudGlyph308.VehicleOut);
                c.Add(bad == 0 && rows == 16, "AC-H7.1 HudActionRules308: " + (rows - bad) + " of " + rows + " rows as the Spec table (dodge 5, jump 5, vehicle 6)");
            }

            // AC-H8 gate: light only while the cells run, one kick, min interval, stuck guard
            {
                var imp = profile.Impact; var gate = default(ImpactGate308); gate.Clear();
                var on = new ImpactFrame308 { Cell = 1, Light = 1f }; var off = default(ImpactFrame308);
                const float dt = 1f / 60f; int lit = 0, kicks = 0;
                for (int i = 0; i < 3; i++) { if (gate.Step(in on, dt, imp.StuckSeconds, imp.MinInterval, out bool b) > 0f) lit++; if (b) kicks++; }
                bool darkAfter = gate.Step(in off, dt, imp.StuckSeconds, imp.MinInterval, out _) <= 0f;
                float second = gate.Step(in on, dt, imp.StuckSeconds, imp.MinInterval, out bool began2);       // 4 frames later: inside MinInterval
                gate.Step(in off, dt, imp.StuckSeconds, imp.MinInterval, out _);
                gate.Clear(); int stuckLit = 0, frames = Mathf.CeilToInt((imp.StuckSeconds + .3f) / dt);
                for (int i = 0; i < frames; i++) if (gate.Step(in on, dt, imp.StuckSeconds, imp.MinInterval, out _) > 0f) stuckLit++;
                int allowed = Mathf.CeilToInt(imp.StuckSeconds / dt) + 2;
                c.Add(lit == 3 && kicks == 1 && darkAfter, "AC-H8.2 light for exactly the 3 written frames (" + lit + "), one kick (" + kicks + "), dark on the next frame: " + darkAfter);
                c.Add(second <= 0f && began2, "AC-H8.3 a second impact inside " + F(imp.MinInterval) + " s gives no light (" + F(second) + ") but still kicks: " + began2);
                c.Add(stuckLit <= allowed && stuckLit < frames, "AC-H8.3 a stuck global stops reacting after " + F(imp.StuckSeconds) + " s: lit " + stuckLit + " of " + frames + " frames");
            }

            if (temporary) Object.DestroyImmediate(profile);
            return c.Finish("checks:sim", "checks-sim.txt");
        }

        // ------------------------------------------------------------------ preview (stills)
        // The Edit Mode preview is a HideAndDontSave canvas: it would survive into Play (domain reload is off) and draw a second,
        // frozen cluster under the live HUD, and after a script reload its component has lost its state. So it is removed when
        // Play is entered and after every reload. No field: only the editor's own event list holds the handler.
        [InitializeOnLoadMethod]
        static void PreviewGuard()
        {
            EditorApplication.playModeStateChanged -= PreviewOnPlayMode;
            EditorApplication.playModeStateChanged += PreviewOnPlayMode;
            EditorApplication.delayCall += RemoveStalePreview;
        }

        static void PreviewOnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) RemoveStalePreview();
        }

        static void RemoveStalePreview()
        {
            if (EditorApplication.isPlaying) return;
            for (int i = 0; i < 8; i++) { var go = FindPreview(); if (go == null) break; Object.DestroyImmediate(go); }
        }

        static HudVessels308 LiveVessels()
        {
            var hud = Object.FindFirstObjectByType<HudController>();
            return hud != null ? hud.Vessels308 : null;
        }

        static GameObject FindPreview()
        {
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
                if (canvas != null && canvas.name == PreviewName && !EditorUtility.IsPersistent(canvas)) return canvas.gameObject;
            return null;
        }

        static void HideAll(Transform t, HideFlags flags)
        {
            t.gameObject.hideFlags = flags;
            for (int i = 0; i < t.childCount; i++) HideAll(t.GetChild(i), flags);
        }

        static string PreviewOn(string command)
        {
            // preview:on:<hp>:<ink>:<state>
            string[] part = command.Split(':');
            float hp = part.Length > 2 && float.TryParse(part[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float h) ? Mathf.Clamp01(h) : .62f;
            float ink = part.Length > 3 && float.TryParse(part[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float k) ? Mathf.Clamp01(k) : .44f;
            string state = part.Length > 4 ? part[4].Trim() : "normal";
            var scene = EditorSceneManager.GetActiveScene(); bool dirtyBefore = scene.isDirty;

            HudVessels308 vessels;
            if (EditorApplication.isPlaying)
            {
                vessels = LiveVessels();
                if (vessels == null) return "REFUSED the running HUD has no Vessels308 (profile missing or disabled)";
            }
            else
            {
                var old = FindPreview(); if (old != null) Object.DestroyImmediate(old);
                var profile = AssetDatabase.LoadAssetAtPath<HudLiquid308ProfileSO>(ProfilePath);
                if (profile == null) return "REFUSED " + ProfilePath + " missing (run hud308-setup): the preview needs the material and the atlas";
                var style = FindStyle(out _);
                var go = new GameObject(PreviewName, typeof(Canvas), typeof(CanvasScaler)) { hideFlags = HideFlags.HideAndDontSave };
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 39;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = .5f;
                vessels = HudVessels308.Create(profile, style, null, go.transform, null, hp, ink);
                HideAll(go.transform, HideFlags.HideAndDontSave);
            }

            var p = vessels.Profile;
            var hpPose = default(LiquidState308); LiquidSim308.Reset(ref hpPose, hp);
            var inkPose = default(LiquidState308); LiquidSim308.Reset(ref inkPose, ink);
            var actions = new HudActionState308 { HasDodge = true, DodgeGateOpen = true, DodgeCooldown01 = 1f, HasJump = true, JumpGateOpen = true, Vehicle = VehicleHud308.CanCall };
            float cost = 0f, light = 0f;
            switch (state)
            {
                case "normal": break;
                case "slosh":
                    hpPose.Tilt = p.Hp.MaxTilt * .8f; inkPose.Tilt = -p.Ink.MaxTilt * .8f;
                    hpPose.WavePx = p.Hp.WaveMaxPx * .6f; inkPose.WavePx = p.Ink.WaveMaxPx * .6f;
                    hpPose.Phase = .7f; inkPose.Phase = 2.1f; break;
                case "low":
                    actions.DodgeGateOpen = false; actions.JumpGateOpen = false; actions.Vehicle = VehicleHud308.Refused; break;
                case "pour":
                    inkPose.Level = Mathf.Max(0f, ink - .2f); inkPose.FreshBottom = Mathf.Max(0f, ink - .3f); inkPose.FreshAge = 0f; inkPose.Pour = 1f;
                    inkPose.WavePx = p.Ink.KickWavePx; break;
                case "wet":
                    hpPose.WetTop = Mathf.Min(1f, hp + .22f); hpPose.WetAge = 0f; inkPose.WetTop = Mathf.Min(1f, ink + .25f); inkPose.WetAge = 0f; break;
                case "cost": cost = .15f; break;
                case "dry":
                    actions.DodgeCooldown01 = .5f; actions.Dodging = true; actions.DodgeGateOpen = false; actions.Airborne = true; actions.JumpGateOpen = false;
                    actions.Vehicle = VehicleHud308.OutNear; actions.VehicleOutGlyph = true; break;
                case "impact":
                    light = 1f; hpPose.Tilt = -p.Hp.MaxTilt * .7f; inkPose.Tilt = -p.Ink.MaxTilt * .7f; break;
                default: return "REFUSED unknown preview state '" + state + "' (normal | slosh | low | pour | wet | cost | dry | impact)";
            }
            // the key glyphs of a still: the live HUD already has them from its presenter; an Edit Mode canvas reads the project's
            // own action asset (the same lookup, nothing enabled or rebound)
            if (!EditorApplication.isPlaying) vessels.SetKeys(PreviewKeys(p));
            vessels.ApplyPreview308(in hpPose, in inkPose, in actions, cost, light);
            // the light's side is the deploy layer's point: ask its director for a held frame (local form, no full-screen flip)
            bool forced = light > 0f && ForceImpact(true);
            if (!EditorApplication.isPlaying)
            {
                Canvas.ForceUpdateCanvases();
                EditorApplication.QueuePlayerLoopUpdate();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
            return "PREVIEW on hp=" + F(hp) + " ink=" + F(ink) + " state=" + state + (EditorApplication.isPlaying ? " (live HUD held; preview:off releases it)" : " (in-memory canvas '" + PreviewName + "', nothing saved)")
                + " material=" + (p.Material != null ? p.Material.name : "<none: fallback quads>")
                + (light <= 0f ? "" : forced ? " impact frame held through ImpactFrameDirector308.ForceFrame (preview:off releases it)"
                    : " NO ImpactFrameDirector308 here: the strength is held but no light side is shown (this tool never writes the impact globals)")
                + " sceneDirty " + dirtyBefore + " -> " + EditorSceneManager.GetActiveScene().isDirty;
        }

        static HudKeys308 PreviewKeys(HudLiquid308ProfileSO p)
        {
            var keys = HudKeys308.Empty;
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            var table = p.Marks.KeyGlyphs;
            if (asset != null)
            {
                keys.Dodge = HudKeyGlyph308.Cell(HudKeyBinding308.Path(HudKeyBinding308.Find(asset, "Gameplay", "Dodge"), false), table);
                keys.Jump = HudKeyGlyph308.Cell(HudKeyBinding308.Path(HudKeyBinding308.Find(asset, "Gameplay", "Jump"), false), table);
            }
            var vehicle = asset != null ? HudKeyBinding308.Find(asset, "Gameplay", p.Marks.VehicleAction) : null;
            keys.Vehicle = HudKeyGlyph308.Cell(vehicle != null ? HudKeyBinding308.Path(vehicle, false) : p.Marks.VehicleKeyPath, table);
            return keys;
        }

        static string PreviewOff()
        {
            ForceImpact(false);
            if (EditorApplication.isPlaying)
            {
                var vessels = LiveVessels();
                if (vessels == null || !vessels.Previewing) return "PREVIEW off (nothing was held)";
                vessels.ClearPreview308();
                return "PREVIEW off (live HUD released)";
            }
            int removed = 0;
            for (var go = FindPreview(); go != null && removed < 8; go = FindPreview()) { Object.DestroyImmediate(go); removed++; }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            return "PREVIEW off (removed " + removed + "), sceneDirty=" + EditorSceneManager.GetActiveScene().isDirty;
        }

        // ------------------------------------------------------------------ checks:play (Play; state in SessionState)
        const string KPhase = "HudLiquid308.play.phase", KFrame = "HudLiquid308.play.frame", KCount = "HudLiquid308.play.count", KLog = "HudLiquid308.play.log",
            KTime = "HudLiquid308.play.time", KRebuilds = "HudLiquid308.play.rebuilds", KA = "HudLiquid308.play.a", KB = "HudLiquid308.play.b";

        static void PlayLog(string line) { SessionState.SetString(KLog, SessionState.GetString(KLog, "") + line + "\n"); }
        static void PlayCheck(bool ok, string label) { PlayLog((ok ? "ok      " : "FAILED  ") + label); }

        const string ImpactInclude = "Assets/_Project/Art/UI/UI304/Shaders/ImpactHud308.hlsl";

        // the one writer of the impact globals in this scene (SPEC-SPELL-DEPLOY-308), if it is there
        static ImpactFrameDirector308 Director()
        {
            foreach (var d in Resources.FindObjectsOfTypeAll<ImpactFrameDirector308>())
                if (d != null && !EditorUtility.IsPersistent(d) && d.isActiveAndEnabled) return d;
            return null;
        }

        /// <summary>Holds (or releases) one impact frame THROUGH the director's own preview / check hook. kind 4 = the local form:
        /// the HUD globals are written at full strength but no full-screen value flip is spent. False = no director here.</summary>
        static bool ForceImpact(bool on)
        {
            var d = Director();
            if (d == null) return false;
            d.ForceFrame(on ? 4 : 0, new Vector2(.7f, .6f), 1f, true);
            return true;
        }

        static int Rebuilds(HudVessels308 v) => v.Hp.RebuildCount + v.Ink.RebuildCount + v.MarkDodge.RebuildCount + v.MarkJump.RebuildCount + v.MarkVehicle.RebuildCount;
        static float LightOn(InkVesselGraphic308 g) => g.Extra.y;      // uv3.y = the light's strength (its side is taken in the shader)

        static string PlayStart()
        {
            if (!EditorApplication.isPlaying) return "REFUSED checks:play needs Play Mode";
            if (SessionState.GetInt(KPhase, 0) > 0) return "REFUSED the probe is already running (play-status / play-abort)";
            var hud = Object.FindFirstObjectByType<HudController>();
            if (hud == null) return "REFUSED no HudController in the running scene";
            SessionState.SetString(KLog, "");
            var vessels = hud.Vessels308;
            Transform canvas = hud.Canvas != null ? hud.Canvas.transform : null;
            bool Has(string name) => canvas != null && canvas.Find(name) != null;
            bool Deep(string name) { foreach (var t in hud.GetComponentsInChildren<Transform>(true)) if (t.name == name) return true; return false; }
            PlayCheck(vessels != null && Has("Vessels308") && !Has("Meters304") && !Deep("InkBottle") && !Deep("Ink_ReceivedSegment") && !Deep("Ink_CostPreview"),
                "AC-H0.1 Vessels308 under HUD_Canvas, no Meters304 / InkBottle / Ink_ReceivedSegment / Ink_CostPreview");
            if (vessels == null) { PlayLog("info    the profile is missing or disabled: the #304 meters are in use; nothing else to probe"); return PlayFinish("no vessels", true); }
            var style = PlaytestUiView.Style(hud.Skin);
            Color32 hpColor = vessels.Hp.color, inkColor = vessels.Ink.color, cinnabar = style.Cinnabar, inkTone = style.Ink;
            PlayCheck(Deep("HUD_Canvas") && Deep("HP_BrushStroke") && Deep("Ink_BrushBar") && Deep("Reticle") && hpColor.Equals(cinnabar) && inkColor.Equals(inkTone),
                "AC-H11.1 HUD_Canvas / HP_BrushStroke / Ink_BrushBar / Reticle exist; HP colour = cinnabar, ink colour = ink");
            int texts = vessels.GetComponentsInChildren<TMPro.TMP_Text>(true).Length, graphics = vessels.GetComponentsInChildren<InkVesselGraphic308>(true).Length;
            PlayCheck(texts == 0 && graphics == 5 && vessels.transform.childCount == 5,
                "AC-H7.5 / AC-H15.7 Vessels308 = five graphics, no text object, key glyphs or not (texts " + texts + ", graphics " + graphics + ", children " + vessels.transform.childCount + ")");
            // D308-11b: the key each mark shows = the cell of the binding its action has right now (the motor's own actions, read only)
            {
                var marks = vessels.Profile.Marks; var motor = Object.FindFirstObjectByType<PlayerMotor>(); var keysNow = vessels.Keys;
                if (!marks.ShowKeys) PlayLog("info    AC-H15 not measured: Marks.ShowKeys is off");
                else if (motor == null) PlayLog("info    AC-H15 not measured: no PlayerMotor in this scene");
                else
                {
                    bool pad = HudKeyBinding308.GamepadUsedLast();
                    string dodgePath = HudKeyBinding308.Path(motor.DodgeInput, pad), jumpPath = HudKeyBinding308.Path(motor.JumpInput, pad);
                    var vehicleAction = HudKeyBinding308.Find(motor.InputActions, motor.InputMapName, marks.VehicleAction);
                    string vehiclePath = vehicleAction != null ? HudKeyBinding308.Path(vehicleAction, pad) : marks.VehicleKeyPath;
                    int d = HudKeyGlyph308.Cell(dodgePath, marks.KeyGlyphs), j = HudKeyGlyph308.Cell(jumpPath, marks.KeyGlyphs), v = HudKeyGlyph308.Cell(vehiclePath, marks.KeyGlyphs);
                    PlayCheck(keysNow.Dodge == d && keysNow.Jump == j && keysNow.Vehicle == v && d >= 0 && j >= 0 && v >= 0,
                        "AC-H15.8 key glyph cells = the live bindings: dodge " + dodgePath + " -> " + keysNow.Dodge + " (" + d + "), jump " + jumpPath + " -> " + keysNow.Jump + " (" + j
                        + "), vehicle " + vehiclePath + " -> " + keysNow.Vehicle + " (" + v + ")");
                    PlayCheck(vessels.MarkDodge.KeyCell == (vessels.DodgeView.Hidden ? vessels.MarkDodge.KeyCell : d) && vessels.MarkJump.KeyCell == (vessels.JumpView.Hidden ? vessels.MarkJump.KeyCell : j),
                        "AC-H15.8 the marks draw those cells (dodge quad " + vessels.MarkDodge.KeyCell + ", jump quad " + vessels.MarkJump.KeyCell + ", vehicle quad " + vessels.MarkVehicle.KeyCell + ")");
                }
            }
            PlayCheck(vessels.Hp.UsesVesselShader && vessels.Ink.UsesVesselShader && vessels.Hp.mainTexture == vessels.Profile.AtlasTexture,
                "vessels draw with " + ShaderName + " and the atlas (not the fallback quads)");
            var need = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
            PlayCheck(vessels.OwnCanvas != null && (vessels.OwnCanvas.additionalShaderChannels & need) == need && (hud.Canvas.additionalShaderChannels & need) == need,
                "TexCoord1..3 on Vessels308's canvas and on HUD_Canvas");
            float hp0 = hud.Hp01, ink0 = hud.Ink01;
            hud.SetHp01(.37f); hud.SetInk01(.21f);
            PlayCheck(Mathf.Approximately(hud.Hp01, .37f) && Mathf.Approximately(hud.Ink01, .21f), "AC-H3.3 Hp01 / Ink01 return the new value in the same frame");
            hud.SetHp01(hp0); hud.SetInk01(ink0); vessels.Snap();
            bool director = ForceImpact(false);
            PlayCheck(Director() == null || Object.FindObjectsByType<ImpactFrameDirector308>(FindObjectsSortMode.None).Length == 1,
                "one ImpactFrameDirector308 at most (the one writer of the impact globals)");
            if (!director) PlayLog("info    AC-H8.2 / H8.3 / H8.4 not measured: no ImpactFrameDirector308 in this scene. The impact globals have one writer "
                + "(the deploy layer); this tool does not write them, so there is nothing to react to");
            SessionState.SetInt(KPhase, director ? 1 : 3); SessionState.SetInt(KFrame, Time.frameCount); SessionState.SetInt(KCount, 0);
            SessionState.SetFloat(KTime, Time.unscaledTime); SessionState.SetInt(KA, 0); SessionState.SetInt(KB, 0);
            EditorApplication.update -= PlayStep; EditorApplication.update += PlayStep;
            return "checks:play started (impact light and stuck guard through ImpactFrameDirector308.ForceFrame, rest rebuilds: about 12 s). Read the result with play-status.\n"
                + SessionState.GetString(KLog, "");
        }

        static void PlayStep()
        {
            int phase = SessionState.GetInt(KPhase, 0);
            if (phase <= 0) { EditorApplication.update -= PlayStep; return; }
            if (!EditorApplication.isPlaying) { PlayFinish("Play stopped", false); return; }
            if (Time.frameCount == SessionState.GetInt(KFrame, -1)) return;        // one step per rendered frame
            SessionState.SetInt(KFrame, Time.frameCount);
            var vessels = LiveVessels();
            if (vessels == null) { PlayFinish("the HUD went away", false); return; }
            int n = SessionState.GetInt(KCount, 0) + 1; SessionState.SetInt(KCount, n);
            float since = Time.unscaledTime - SessionState.GetFloat(KTime, 0f);
            var impact = vessels.Profile.Impact;
            bool reacts = impact.Enabled && !vessels.ReducedMotion;
            switch (phase)
            {
                case 1:   // wait out the min interval, then write the globals for 2 frames
                    if (since < impact.MinInterval + .2f) { SessionState.SetInt(KCount, 0); return; }
                    if (n == 1) ForceImpact(true);
                    else if (n == 2)
                    {
                        SessionState.SetInt(KA, LightOn(vessels.Hp) > 0f && LightOn(vessels.Ink) > 0f && LightOn(vessels.MarkJump) >= 0f ? 1 : 0);
                        // the contract as the HUD sees it on this frame: the point every HUD shader takes its light side from
                        ImpactFrameProbe308.Read(impact.FollowHudGlobal, out ImpactFrame308 seen);
                        PlayLog("info    forced frame: viewport " + seen.Viewport.ToString("F3") + " pixel-frame point " + seen.PointPixel.ToString("F3")
                            + " cell " + seen.Cell + " light " + F(seen.Light) + (seen.Light <= 0f ? " (the deploy layer's HUD reaction is off: no light is expected)" : ""));
                        PlayCheck(seen.Cell >= 1 && (seen.PointPixel.x != 0f || seen.PointPixel.y != 0f) && Mathf.Abs(seen.PointPixel.x - seen.Viewport.x) < 1e-4f
                            && (Mathf.Abs(seen.PointPixel.y - seen.Viewport.y) < 1e-4f || Mathf.Abs(seen.PointPixel.y - (1f - seen.Viewport.y)) < 1e-4f),
                            "the deploy layer supplies the pixel-frame point with the frame (same x, y or 1 - y of the viewport point)");
                        if (seen.Light <= 0f) SessionState.SetInt(KA, 2);       // 2 = the layer itself gives no HUD light
                    }
                    else if (n == 3) { SessionState.SetInt(KB, LightOn(vessels.Hp) > 0f ? 1 : 0); ForceImpact(false); }
                    else if (n == 4)
                    {
                        if (SessionState.GetInt(KA, 0) == 2) reacts = false;
                        bool lit = SessionState.GetInt(KA, 0) == 1 && SessionState.GetInt(KB, 0) == 1, dark = LightOn(vessels.Hp) <= 0f && LightOn(vessels.Ink) <= 0f;
                        PlayCheck(reacts ? lit && dark : !lit && dark, reacts
                            ? "AC-H8.2 light payload on both written frames (" + lit + ") and 0 on the frame after (" + dark + ")"
                            : "AC-H8.5 light payload stays 0 (Impact.Enabled off or reduced motion): lit " + lit);
                        PlayCheck(!vessels.Still || !impact.KickOnImpact || vessels.ReducedMotion, "AC-H8.4 the impact kicked the liquid (not Still right after): Still=" + vessels.Still);
                        Next(2);
                    }
                    break;
                case 2:   // a global left on: the reaction must stop after StuckSeconds
                    if (since < impact.MinInterval + .2f) { SessionState.SetInt(KCount, 0); return; }
                    if (n == 1) { ForceImpact(true); SessionState.SetFloat(KTime, Time.unscaledTime - impact.MinInterval - .2f); SessionState.SetInt(KA, 0); }
                    else
                    {
                        float held = since - impact.MinInterval - .2f;
                        if (LightOn(vessels.Hp) > 0f) SessionState.SetInt(KA, Mathf.RoundToInt(held * 1000f));     // last time it was lit
                        if (held > impact.StuckSeconds + .35f)
                        {
                            float last = SessionState.GetInt(KA, 0) / 1000f;
                            PlayCheck(LightOn(vessels.Hp) <= 0f && last <= impact.StuckSeconds + .1f,
                                "AC-H8.3 global held " + F(held) + " s: last lit at " + F(last) + " s (stuck guard " + F(impact.StuckSeconds) + " s), dark now");
                            ForceImpact(false);
                            Next(3);
                        }
                    }
                    break;
                case 3:   // rest: no mesh rebuild for 300 frames once Still
                    if (!vessels.Still)
                    {
                        SessionState.SetInt(KCount, 0);
                        if (since > 8f) { PlayLog("info    AC-H4.4 / AC-H16.8 not measured: the liquid never rested in 8 s (the player is moving, or a value is still changing: regeneration)"); PlayFinish("done", true); }
                        return;
                    }
                    if (n == 1) SessionState.SetInt(KRebuilds, Rebuilds(vessels));
                    else if (n >= 301)
                    {
                        int delta = Rebuilds(vessels) - SessionState.GetInt(KRebuilds, 0);
                        var hpNow = vessels.HpState; var inkNow = vessels.InkState;
                        PlayCheck(delta == 0 && hpNow.Tilt == 0f && hpNow.WavePx == 0f && inkNow.Tilt == 0f && inkNow.WavePx == 0f,
                            "AC-H4.4 / AC-H16.8 300 resting frames: mesh rebuilds " + delta + ", surface flat (hp tilt " + F(hpNow.Tilt, "0.#####") + " ripple " + F(hpNow.WavePx, "0.###")
                            + ", ink tilt " + F(inkNow.Tilt, "0.#####") + " ripple " + F(inkNow.WavePx, "0.###") + ") at hp " + F(hpNow.Value) + " ink " + F(inkNow.Value));
                        PlayFinish("done", true);
                    }
                    break;
            }
        }

        static void Next(int phase)
        {
            SessionState.SetInt(KPhase, phase); SessionState.SetInt(KCount, 0); SessionState.SetFloat(KTime, Time.unscaledTime);
            SessionState.SetInt(KA, 0); SessionState.SetInt(KB, 0);
        }

        static string PlayFinish(string reason, bool write)
        {
            EditorApplication.update -= PlayStep;
            ForceImpact(false);
            SessionState.SetInt(KPhase, 0);
            string log = SessionState.GetString(KLog, "");
            int failed = 0, total = 0;
            foreach (var line in log.Split('\n')) { if (line.StartsWith("ok ", StringComparison.Ordinal)) total++; else if (line.StartsWith("FAILED", StringComparison.Ordinal)) { total++; failed++; } }
            string text = "checks:play " + reason + ": " + (failed == 0 ? "ok " + total + "/" + total : "FAILED " + failed + " of " + total) + " (" + DateTime.UtcNow.ToString("O") + ")\n" + log;
            if (write) { Directory.CreateDirectory(Out); File.WriteAllText(Path.Combine(Out, "checks-play.txt"), text); }
            SessionState.SetString(KLog, text);
            return text;
        }

        static string PlayStatus()
        {
            int phase = SessionState.GetInt(KPhase, 0);
            return (phase > 0 ? "checks:play RUNNING phase " + phase + " (1 impact light, 2 stuck guard, 3 rest rebuilds)\n" : "") + SessionState.GetString(KLog, "(no probe has run in this editor session)");
        }
    }
}
