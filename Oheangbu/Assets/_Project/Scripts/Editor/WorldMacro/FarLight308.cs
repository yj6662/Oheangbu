using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 먼 불빛 (SPEC-ATTRACTION-LIGHT-308, TEST) — makes the attraction lights the D308-16 re-layout leans on readable from the
    // spots the design names, inside the canon limits (ART-INK 발광 상한: man-made attraction lights only, never a bulb, never a marker).
    //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.FarLight308 Run "<command>"
    // Queue-safe: Run(string) never opens a dialog; a refusal comes back as "refused: ...". Scene order = the promotion chain
    // (arch296 -> folk298 -> main). Protected trees (Watershed295, Reworld292, MountainTrail285), W_Demo_Compact and 03_Content are
    // never opened or written; a lamp under a protected tree is refused row by row. No AssetDatabase.SaveAssets: the scene and each
    // asset this tool owns are saved alone. Every number comes from Art/World/Compact/Rebuild/FarLight308/farlight308.json.
    //   status                      data, profile, materials, ledgers, vegetation rows (read only)
    //   sync                        profile asset + one FarGlow308 material per kind, filled from the data (assets only; caps checked)
    //   plan:<scene>[:variant=P0|P1|P2]   what apply would write in that scene (nothing is changed; the scene is opened)
    //   apply:<scene>[:variant=..]  1) sync  2) per lights[] row: the paper of the KCISA lantern in the data's paper.variant (FarLight308.Paper.cs:
    //                               P0 none | P1 the #308 box, InnLantern297 | P2 한지, a drum on the paper faces inside the ribs) and / or
    //                               the far glow card (child of the lamp, VfxAfterFog layer, AttractionLight308 bound to its Light).
    //                               A sleeve of another variant is replaced, P0 takes it away. variant=.. overrides the data for
    //                               this call only (comparison stills); check:<scene> passes only in the data's variant.
    //                               The P2 drum mesh is measured from its vertices and the sleeve scaled from the MEASURED radius /
    //                               half height (relayout fix 2b: Cylinder.fbx is the legacy radius-1 mesh; a mesh that is not the
    //                               closed round drum the data describes refuses its rows). check AC-L14 measures what is drawn.
    //                               3) per enabled poles[] row: the 장대 등 under the FarLight308 root. Scene backup first, the scene is
    //                               saved, ledger Art/Playtest308/Relayout/farlight/ledger_farlight308_<scene>.json. A second apply = 변경 없음.
    //   revert:<scene>              destroys exactly what the ledger lists (newest first), saves the scene, archives the ledger
    //   adopt:<scene>               #308 ledger recovery (FarLight308.Adopt.cs): the ledger apply WOULD have written, from the opened scene,
    //                               only when plan:<scene> has nothing to write and nothing is missing; one ledger file, nothing else
    //   check:<scene>               read only: AC-L1..L8 (counts by type, caps vs Bloom, layer / LOD / shader, source binding, nothing on
    //                               enemies / contamination / protected trees, physics ray per sightline [M], feature order) ->
    //                               Art/Playtest308/Relayout/farlight/check_farlight308_<scene>.txt
    //   rows:plan | rows:apply | rows:revert   the enabled vegRows through the BuildingFix308.Veg ledger (BuildingFix308.VegRows, tag
    //                               from the data). The sheets are shared: one apply serves the three scenes; no scene is opened.
    //   shots:list                  the capture list (same eyes and aims as the diagnosis stills; PC + Mobile; fov 60 + 9)
    //   shot:<sightline>:<n60|z09>:<PC|Mobile>[:eye=after][:label=<text>]   one still (Presentation297 rig at that quality tier; the
    //                               editor's quality level is put back) -> Art/Playtest308/Relayout/farlight/stills_<label>/
    //   assets:revert               deletes the profile and the baked materials — only when no scene ledger is left
    public static partial class FarLight308
    {
        internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        internal static string DataFile => PostLedger308.RepoPath("Art/World/Compact/Rebuild/FarLight308/farlight308.json");
        internal static string OutDir => PostLedger308.RepoPath("Art/Playtest308/Relayout/farlight");
        internal static string BackupRoot => Path.Combine(OutDir, "Backups");
        internal static string LedgerFile(string scene) => Path.Combine(OutDir, "ledger_farlight308_" + PostLedger308.Short(scene) + ".json");
        internal static string AssetLedgerFile => Path.Combine(OutDir, "ledger_farlight308_assets.json");

        // ------------------------------------------------------------------ data (farlight308.json)

        [Serializable] internal sealed class KindCfg
        {
            public string kind = "", material = "";
            public float[] colour = Array.Empty<float>(), nearFade = Array.Empty<float>(), farFade = Array.Empty<float>();
            public float peak, farPeak, cover, chroma, falloff, worldRadius, maxWorldRadius, minPx, minPxFar, maxPx, pull, flicker, flickerSpeed, inkRing;
        }
        [Serializable] internal sealed class PaperCfg
        {
            public string name = "", body = "", variant = "";
            public float widthScale, heightFraction, centreFraction;   // P1 (the #308 box)
            public FrameCfg frame = new FrameCfg();                    // the lantern mesh as measured (FarLight308.Paper.cs)
            public HanjiCfg hanji = new HanjiCfg();                    // P2
        }
        [Serializable] internal sealed class GlowCfg { public string name = ""; public float lanternAnchorFraction; }
        [Serializable] internal sealed class LightRow
        {
            public string id = "", kind = "", path = "", pair = "", note = "";
            public float[] at = Array.Empty<float>();
            public bool glow, paper, optional;
        }
        [Serializable] internal sealed class PoleRow
        {
            public string id = "", kind = "", lanternSource = "", note = "";
            public bool enabled, collider;
            public float x, z, y_offline, height, poleRadius, armLength, armThickness, lightRange, lightIntensity, clear_m;
            public float[] faceTo = Array.Empty<float>(), sourceAt = Array.Empty<float>(), lightColour = Array.Empty<float>();
        }
        [Serializable] internal sealed class VegRow { public bool enabled; public string sheet = "", id = "", proto = "", what = "", note = ""; public float[] at = Array.Empty<float>(); }
        [Serializable] internal sealed class VegCfg { public string tag = ""; public float tolerance_m; public VegRow[] rows = Array.Empty<VegRow>(); }
        [Serializable] internal sealed class Sightline
        {
            public string id = "", design = "", priority = "", label = "", light = "", alt = "", eyeNote = "";
            public float[] eye = Array.Empty<float>(), eyeAfter = Array.Empty<float>(), aim = Array.Empty<float>();
        }
        [Serializable] internal sealed class Cfg
        {
            public string id = "", version = "", profileAsset = "", shader = "", materialFolder = "", paperMaterial = "", layer = "", root = "";
            public float tolerance_m, cullBounds_m, referenceHeight, maxCentre, bloomThreshold, eyeHeight_m, stillEyeHeight_m;
            public string[] allowedPaths = Array.Empty<string>();
            public KindCfg[] kinds = Array.Empty<KindCfg>();
            public PaperCfg paper = new PaperCfg();
            public PostCfg post = new PostCfg();
            public GlowCfg glow = new GlowCfg();
            public LightRow[] lights = Array.Empty<LightRow>();
            public PoleRow[] poles = Array.Empty<PoleRow>();
            public VegCfg vegRows = new VegCfg();
            public Sightline[] sightlines = Array.Empty<Sightline>();
        }

        internal static Vector3 V3(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        internal static string F(float v, string f = "0.##") => v.ToString(f, Inv);
        internal static string P(Vector3 v) => "(" + F(v.x, "F2") + ", " + F(v.y, "F2") + ", " + F(v.z, "F2") + ")";

        internal static Cfg Load()
        {
            if (!File.Exists(DataFile)) throw new PostLedger308.Refused("data file missing: " + DataFile);
            var c = JsonUtility.FromJson<Cfg>(File.ReadAllText(DataFile)) ?? throw new PostLedger308.Refused("data file unreadable: " + DataFile);
            if (c.id != "farlight308") throw new PostLedger308.Refused("not a farlight308 data file: " + DataFile);
            if (c.kinds.Length == 0 || string.IsNullOrEmpty(c.profileAsset) || string.IsNullOrEmpty(c.shader) || string.IsNullOrEmpty(c.root)
                || string.IsNullOrEmpty(c.paper.name) || string.IsNullOrEmpty(c.glow.name) || c.tolerance_m <= 0f || c.referenceHeight <= 0f
                || c.allowedPaths.Length == 0 || c.allowedPaths.Any(string.IsNullOrWhiteSpace) || c.eyeHeight_m <= 0f || c.stillEyeHeight_m <= 0f)
                throw new PostLedger308.Refused("data file incomplete (kinds / profileAsset / shader / root / paper.name / glow.name / tolerance_m / referenceHeight / allowedPaths / eyeHeight_m / stillEyeHeight_m)");
            foreach (var p in new[] { c.profileAsset, c.materialFolder, c.paperMaterial })
                if (PostLedger308.IsProtectedPath(p)) throw new PostLedger308.Refused("protected path in the data: " + p);
            foreach (var k in c.kinds)
            {
                ParseKind(k.kind);
                if (k.colour.Length < 3 || k.nearFade.Length < 2 || k.farFade.Length < 2) throw new PostLedger308.Refused("kind " + k.kind + ": colour[3], nearFade[2], farFade[2] are required");
                if (k.farPeak <= 0f || k.farPeak > 1f || k.minPxFar <= 0f || k.minPxFar > k.minPx || k.maxWorldRadius < k.worldRadius || k.maxWorldRadius <= 0f)
                    throw new PostLedger308.Refused("kind " + k.kind + ": the distance law needs 0 < farPeak <= 1, 0 < minPxFar <= minPx, maxWorldRadius >= worldRadius > 0 (a card with no world size cap grows without bound with distance)");
                // the card's bounds: a quad of cullBounds_m turned 45 degrees about Y, so it reaches cullBounds_m x 0.35 on X and on Z
                if (c.cullBounds_m * .35f < k.maxWorldRadius)
                    throw new PostLedger308.Refused("cullBounds_m " + F(c.cullBounds_m) + " is too small for kind " + k.kind + " (needs >= maxWorldRadius / 0.35 = " + F(k.maxWorldRadius / .35f) + "): the card would be culled at the screen edge");
                float slope = k.peak * (1f + k.flicker) - k.cover;
                float centre = 1f + Mathf.Max(slope, slope * k.farPeak);   // = AttractionLightProfileSO.CentreOverWhite
                // ART-INK 발광 상한: the brightest the card can get over a white background must stay under the Bloom threshold
                if (centre > c.maxCentre + 1e-4f || c.maxCentre >= c.bloomThreshold)
                    throw new PostLedger308.Refused("kind " + k.kind + ": peak x (1 + flicker) + (1 - cover) = " + F(centre, "0.###") + " exceeds maxCentre " + F(c.maxCentre) + " (Bloom threshold " + F(c.bloomThreshold) + ") — the card would bloom");
            }
            ValidatePaper(c);
            return c;
        }

        internal static AttractionLightProfileSO.Kind ParseKind(string s)
        {
            if (Enum.TryParse(s, out AttractionLightProfileSO.Kind k)) return k;
            throw new PostLedger308.Refused("unknown attraction light kind '" + s + "' (InnLantern | ShrineCandle | OreVein)");
        }

        // ------------------------------------------------------------------ ledgers

        [Serializable] internal sealed class Made { public string what = "", row = "", path = "", utc = ""; public Vector3 at; }
        [Serializable] internal sealed class Ledger { public string scene = "", adopted = ""; public List<Made> made = new List<Made>(); public List<string> backups = new List<string>(); }   // adopted: FarLight308.Adopt.cs
        [Serializable] internal sealed class AssetLedger { public List<string> created = new List<string>(); }

        static Ledger ReadLedger(string scene)
        {
            string f = LedgerFile(scene);
            return File.Exists(f) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(f)) ?? new Ledger { scene = scene } : new Ledger { scene = scene };
        }
        static void WriteLedger(string scene, Ledger l) { Directory.CreateDirectory(OutDir); File.WriteAllText(LedgerFile(scene), JsonUtility.ToJson(l, true), new UTF8Encoding(false)); }
        static AssetLedger ReadAssetLedger() => File.Exists(AssetLedgerFile) ? JsonUtility.FromJson<AssetLedger>(File.ReadAllText(AssetLedgerFile)) ?? new AssetLedger() : new AssetLedger();
        static void WriteAssetLedger(AssetLedger l) { Directory.CreateDirectory(OutDir); File.WriteAllText(AssetLedgerFile, JsonUtility.ToJson(l, true), new UTF8Encoding(false)); }

        // ------------------------------------------------------------------ entry

        const string Usage = "status | mesh[:<built-in mesh name>] | sync | plan:<scene>[:variant=P0|P1|P2] | apply:<scene>[:variant=P0|P1|P2] | revert:<scene> | adopt:<scene> | check:<scene> | rows:plan | rows:apply | rows:revert | shots:list | shot:<sightline>:<n60|z09>:<PC|Mobile>[:eye=after][:label=..] | assets:revert  (scene = arch296 | folk298 | main)";

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            try
            {
                var parts = c.Split(':'); string head = parts[0];
                switch (head)
                {
                    case "status": return Status();
                    case "mesh": return MeshProbe(parts.Length > 1 ? parts[1].Trim() : "");   // read only: what a built-in mesh name measures as
                    case "sync": PostLedger308.RequireEditable(); return Sync(Load(), false, out _);
                    case "plan": return Pass(SceneArg(parts), true, VariantArg(parts));
                    case "apply": return Pass(SceneArg(parts), false, VariantArg(parts));
                    case "revert": return Adopt308.RevertNote(File.Exists(LedgerFile(SceneArg(parts))) ? AdoptedLive(ReadLedger(SceneArg(parts))) : null) + Revert(SceneArg(parts));
                    case "adopt": return Adopt(SceneArg(parts));
                    case "check": return Check(SceneArg(parts));
                    case "rows": return Rows(parts.Length > 1 ? parts[1].Trim() : "");
                    case "shots": return ShotList();
                    case "shot": return Shot(parts);
                    case "assets": if (parts.Length > 1 && parts[1].Trim() == "revert") return AssetsRevert(); break;
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
            catch (Exception e) { return "FAILED " + e; }   // never an unhandled throw into the queue
            return "refused: unknown FarLight308 command '" + c + "' (" + Usage + ")";
        }

        static string SceneArg(string[] parts)
        {
            string token = parts.Length > 1 ? parts[1].Trim() : "";
            var hit = PostLedger308.Scenes.FirstOrDefault(s => string.Equals(PostLedger308.Short(s), token, StringComparison.OrdinalIgnoreCase) || s == token);
            if (hit == null) throw new PostLedger308.Refused("scene '" + token + "' is not one of " + string.Join(", ", PostLedger308.Scenes.Select(PostLedger308.Short)));
            return hit;
        }

        static string Status()
        {
            var sb = new StringBuilder("FarLight308 status (play=" + EditorApplication.isPlaying + ")\n");
            Cfg c;
            try { c = Load(); } catch (PostLedger308.Refused r) { return sb.Append("  data: " + r.Message).ToString(); }
            sb.AppendLine("  data " + DataFile + " (" + c.version + "): kinds " + c.kinds.Length + ", lights " + c.lights.Length + " (optional " + c.lights.Count(l => l.optional) + "), poles " + c.poles.Count(p => p.enabled) + " enabled / " + c.poles.Length
                + ", vegRows " + c.vegRows.rows.Count(r => r.enabled) + " enabled / " + c.vegRows.rows.Length + ", sightlines " + c.sightlines.Length);
            var profile = AssetDatabase.LoadAssetAtPath<AttractionLightProfileSO>(c.profileAsset);
            sb.AppendLine("  profile " + c.profileAsset + ": " + (profile == null ? "absent (sync / apply creates it)" : profile.Entries.Length + " entries, shader " + (profile.GlowShader != null ? profile.GlowShader.name : "MISSING")));
            sb.AppendLine("  shader " + c.shader + ": " + (Shader.Find(c.shader) != null ? "present" : "MISSING (deploy FarGlow308.shader and Refresh)"));
            sb.AppendLine("  layer " + c.layer + ": " + LayerMask.NameToLayer(c.layer));
            foreach (var k in c.kinds) sb.AppendLine("  material " + MaterialPath(c, k) + ": " + (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(c, k)) != null ? "present" : "absent"));
            sb.AppendLine(PaperStatus(c));
            foreach (var s in PostLedger308.Scenes)
            {
                var l = File.Exists(LedgerFile(s)) ? ReadLedger(s) : null;
                sb.AppendLine("  " + PostLedger308.Short(s) + ": ledger " + (l == null ? "none" : l.made.Count + " object(s) (" + string.Join(", ", l.made.GroupBy(m => m.what).Select(g => g.Key + " " + g.Count())) + ")" + Adopt308.Mark(AdoptedLive(l))));
            }
            var active = SceneManager.GetActiveScene();
            return sb.Append("  active scene " + active.path + (active.isDirty ? " (dirty)" : "")).ToString();
        }

        // ------------------------------------------------------------------ assets (profile + materials)

        static string MaterialPath(Cfg c, KindCfg k) => c.materialFolder.TrimEnd('/') + "/" + k.material + ".mat";

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static void Fill(Cfg c, KindCfg k, AttractionLightProfileSO.Entry e)
        {
            e.Kind = ParseKind(k.kind);
            e.Colour = new Color(k.colour[0], k.colour[1], k.colour[2], 1f);
            e.Peak = k.peak; e.Cover = k.cover; e.Chroma = k.chroma; e.Falloff = k.falloff; e.WorldRadius = k.worldRadius; e.MinPx = k.minPx; e.MaxPx = k.maxPx;
            e.FarPeak = k.farPeak; e.MinPxFar = k.minPxFar; e.MaxWorldRadius = k.maxWorldRadius;
            e.NearFade = new Vector2(k.nearFade[0], k.nearFade[1]); e.FarFade = new Vector2(k.farFade[0], k.farFade[1]);
            e.Pull = k.pull; e.Flicker = k.flicker; e.FlickerSpeed = k.flickerSpeed; e.InkRing = k.inkRing;
        }

        /// <summary>Profile asset + one material per kind, filled from the data. dry = report only. Only these assets are saved.</summary>
        static string Sync(Cfg c, bool dry, out AttractionLightProfileSO profile)
        {
            var sb = new StringBuilder("FarLight308 sync" + (dry ? " (dry)" : "") + "\n");
            var shader = Shader.Find(c.shader) ?? throw new PostLedger308.Refused("shader " + c.shader + " is not in the project (deploy FarGlow308.shader, Refresh, shader errors 0)");
            profile = AssetDatabase.LoadAssetAtPath<AttractionLightProfileSO>(c.profileAsset);
            var assets = ReadAssetLedger(); bool created = profile == null;
            if (dry)
            {
                sb.AppendLine("  profile " + c.profileAsset + (created ? ": would be created" : ": present"));
                foreach (var k in c.kinds) sb.AppendLine("  material " + MaterialPath(c, k) + (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(c, k)) == null ? ": would be created" : ": present (values re-baked from the data)"));
                SyncPaper(c, true, assets, sb);
                return sb.ToString();
            }
            if (created)
            {
                profile = ScriptableObject.CreateInstance<AttractionLightProfileSO>();
                EnsureFolder(Path.GetDirectoryName(c.profileAsset).Replace('\\', '/'));
                AssetDatabase.CreateAsset(profile, c.profileAsset);
                if (!assets.created.Contains(c.profileAsset)) assets.created.Add(c.profileAsset);
            }
            else if (EditorUtility.IsDirty(profile)) throw new PostLedger308.Refused(c.profileAsset + " has unsaved in-memory changes (another session?)");
            string before = EditorJsonUtility.ToJson(profile);
            profile.GlowShader = shader; profile.ReferenceHeight = c.referenceHeight; profile.MaxCentre = c.maxCentre; profile.BloomThreshold = c.bloomThreshold;
            var entries = new List<AttractionLightProfileSO.Entry>();
            EnsureFolder(c.materialFolder.TrimEnd('/'));
            foreach (var k in c.kinds)
            {
                var kind = ParseKind(k.kind);
                var e = (profile.Entries ?? Array.Empty<AttractionLightProfileSO.Entry>()).FirstOrDefault(x => x != null && x.Kind == kind) ?? new AttractionLightProfileSO.Entry();
                Fill(c, k, e);
                string path = MaterialPath(c, k);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(shader) { name = k.material, enableInstancing = false };
                    AssetDatabase.CreateAsset(m, path);
                    if (!assets.created.Contains(path)) assets.created.Add(path);
                    sb.AppendLine("  created " + path);
                }
                else if (m.shader != shader) { m.shader = shader; sb.AppendLine("  shader of " + path + " -> " + shader.name); }
                string mb = EditorJsonUtility.ToJson(m);
                profile.Write(e, m);
                if (EditorJsonUtility.ToJson(m) != mb) { EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); sb.AppendLine("  baked " + path + " (peak " + F(e.Peak) + " -> " + F(e.Peak * e.FarPeak) + ", cover " + F(e.Cover) + ", min " + F(e.MinPx) + " -> " + F(e.MinPxFar) + " px, world radius <= " + F(e.MaxWorldRadius) + " m, near " + F(e.NearFade.x) + "-" + F(e.NearFade.y) + " m, far " + F(e.FarFade.x) + "-" + F(e.FarFade.y) + " m)"); }
                e.Material = m; entries.Add(e);
                if (!profile.WithinCaps(e)) throw new PostLedger308.Refused("kind " + k.kind + " breaks the profile caps after the bake");
            }
            profile.Entries = entries.ToArray();
            if (created || EditorJsonUtility.ToJson(profile) != before) { EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile); sb.AppendLine("  " + (created ? "created " : "updated ") + c.profileAsset); }
            else sb.AppendLine("  profile unchanged");
            SyncPaper(c, false, assets, sb);
            WriteAssetLedger(assets);
            return sb.ToString();
        }

        static string AssetsRevert()
        {
            PostLedger308.RequireEditable();
            foreach (var s in PostLedger308.Scenes) if (File.Exists(LedgerFile(s)) && ReadLedger(s).made.Count > 0) throw new PostLedger308.Refused("scene ledger " + PostLedger308.Short(s) + " still lists objects — revert:" + PostLedger308.Short(s) + " first");
            var assets = ReadAssetLedger(); var sb = new StringBuilder("FarLight308 assets:revert\n");
            foreach (var p in assets.created.ToArray())
            {
                if (PostLedger308.IsProtectedPath(p)) throw new PostLedger308.Refused("protected path in the asset ledger: " + p);
                bool gone = AssetDatabase.LoadMainAssetAtPath(p) == null || AssetDatabase.DeleteAsset(p);
                sb.AppendLine("  " + (gone ? "deleted " : "COULD NOT delete ") + p); if (gone) assets.created.Remove(p);
            }
            WriteAssetLedger(assets);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ vegetation rows (BuildingFix308.VegRows)

        static string Rows(string mode)
        {
            var c = Load(); var v = c.vegRows;
            if (string.IsNullOrEmpty(v.tag) || v.tolerance_m <= 0f) throw new PostLedger308.Refused("vegRows.tag / tolerance_m missing in the data");
            var asks = new List<BuildingFix308.VegRowAsk308>();
            foreach (var r in v.rows.Where(r => r.enabled))
            {
                if (PostLedger308.IsProtectedPath(r.sheet)) throw new PostLedger308.Refused("protected sheet in vegRows: " + r.sheet);
                if (r.at.Length < 3 || string.IsNullOrEmpty(r.id) || string.IsNullOrEmpty(r.sheet)) throw new PostLedger308.Refused("vegRows row incomplete: " + r.id);
                asks.Add(new BuildingFix308.VegRowAsk308 { sheet = r.sheet, id = r.id, proto = r.proto, at = V3(r.at) });
            }
            string head = "FarLight308 rows:" + mode + " — tag " + v.tag + ", " + asks.Count + " enabled row(s), " + v.rows.Count(r => !r.enabled) + " staged off\n";
            switch (mode)
            {
                case "plan": return head + BuildingFix308.VegRowsPlan(v.tag, asks, v.tolerance_m);
                case "apply": PostLedger308.RequireEditable(); if (asks.Count == 0) return head + "  nothing enabled"; return head + BuildingFix308.VegRowsApply(v.tag, asks, v.tolerance_m);
                case "revert": PostLedger308.RequireEditable(); return head + BuildingFix308.VegRowsRevert(v.tag, v.tolerance_m);
            }
            throw new PostLedger308.Refused("rows:<plan|apply|revert>");
        }

        // ------------------------------------------------------------------ scene lookup

        /// <summary>The object at a scene path. Where siblings share a name every one is followed, and the candidate nearest to
        /// `at` (within tol) wins — never "the first child of that name" (two porch lanterns share one name under one parent).</summary>
        internal static Transform Resolve(Scene scene, string path, Vector3 at, float tol)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var names = path.Split('/'); var level = new List<Transform>();
            foreach (var g in scene.GetRootGameObjects()) if (g.name == names[0]) level.Add(g.transform);
            for (int i = 1; i < names.Length && level.Count > 0; i++)
            {
                var next = new List<Transform>();
                foreach (var t in level) foreach (Transform ch in t) if (ch.name == names[i]) next.Add(ch);
                level = next;
            }
            Transform best = null; float bd = tol;
            foreach (var t in level) { float d = Vector3.Distance(t.position, at); if (d <= bd) { bd = d; best = t; } }
            return best;
        }

        internal static Transform Child(Transform parent, string name)
        {
            foreach (Transform ch in parent) if (ch.name == name) return ch;
            return null;
        }

        /// <summary>An allow list, not a ban list (review F6): a card / sleeve may only sit under one of the data's allowedPaths
        /// (rest shrines, the inns) or under this tool's own root. Everything else — and the two explicit bans — is refused.</summary>
        internal static bool Forbidden(Cfg c, Transform t, out string why)
        {
            why = null;
            if (PostLedger308.UnderProtectedTree(t)) { why = "under a protected tree"; return true; }
            string path = PostLedger308.PathOf(t);
            if (!path.StartsWith(c.root + "/", StringComparison.Ordinal) && !c.allowedPaths.Any(a => path.StartsWith(a, StringComparison.Ordinal)))
            { why = "outside the allowed paths (" + string.Join(", ", c.allowedPaths) + ", " + c.root + "/): " + path; return true; }
            // enemies and contaminated actors never glow (ART-INK 오염은 빛나지 않는다)
            if (t.GetComponentInParent<Oheangbu.App.Prologue.PrologueEncounter>(true) != null) { why = "on an encounter actor (enemies / contamination never glow)"; return true; }
            return false;
        }

        /// <summary>Oriented box of the lantern body: centre, yaw, width, height, depth (world metres). False when the lamp has no body.</summary>
        internal static bool Body(Transform lamp, string bodyName, out Vector3 centre, out float yaw, out Vector3 size)
        {
            centre = lamp.position; yaw = lamp.eulerAngles.y; size = Vector3.zero;
            var body = Child(lamp, bodyName); if (body == null) return false;
            MeshFilter best = null; float vol = 0f;
            foreach (var f in body.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                var s = Vector3.Scale(f.sharedMesh.bounds.size, Abs(f.transform.lossyScale)); float v = s.x * s.y * s.z;
                if (v > vol) { vol = v; best = f; }
            }
            if (best == null) return false;
            var tr = best.transform; var local = Vector3.Scale(best.sharedMesh.bounds.size, Abs(tr.lossyScale));
            centre = tr.TransformPoint(best.sharedMesh.bounds.center);
            // which local axis stands up
            var axes = new[] { tr.right, tr.up, tr.forward }; int up = 0;
            for (int i = 1; i < 3; i++) if (Mathf.Abs(axes[i].y) > Mathf.Abs(axes[up].y)) up = i;
            int a = (up + 1) % 3, b = (up + 2) % 3;
            var flat = Vector3.ProjectOnPlane(axes[b], Vector3.up);
            yaw = flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat.normalized, Vector3.up).eulerAngles.y : lamp.eulerAngles.y;
            size = new Vector3(local[a], local[up], local[b]);
            return size.y > 0.01f;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static Vector3 Div(Vector3 world, Vector3 parentLossy) =>
            new Vector3(world.x / Mathf.Max(Mathf.Abs(parentLossy.x), 1e-4f), world.y / Mathf.Max(Mathf.Abs(parentLossy.y), 1e-4f), world.z / Mathf.Max(Mathf.Abs(parentLossy.z), 1e-4f));

        internal static Light LampLight(Transform lamp) => lamp.GetComponent<Light>() ?? lamp.GetComponentsInChildren<Light>(true).FirstOrDefault();

        /// <summary>Where the card of a lamp sits: lantern = lanternAnchorFraction up its body; a lamp with no body (a candle) = on
        /// its own Light. A `pair` row would put it between two siblings — the data leaves it empty for the altars (review F4).</summary>
        internal static Vector3 Anchor(Cfg c, Transform lamp, string pair)
        {
            if (!string.IsNullOrEmpty(pair) && lamp.parent != null)
            {
                var other = Child(lamp.parent, pair);
                return other != null ? (lamp.position + other.position) * .5f : lamp.position;
            }
            if (Body(lamp, c.paper.body, out var centre, out _, out var size)) return centre + Vector3.up * ((c.glow.lanternAnchorFraction - .5f) * size.y);
            return lamp.position;
        }

        // ------------------------------------------------------------------ plan / apply

        // verb null = create; ReShape / TakeAway = a paper swap (FarLight308.Paper.cs) — those destroy an object of this tool
        sealed class Op { public string what, row, text, verb; public Action<Scene, Ledger, string> run; }

        static string Pass(string scenePath, bool dry, string variantOverride)
        {
            PostLedger308.RequireEditable();
            var c = Load();
            string variant = Variant(c, variantOverride);
            int layer = LayerMask.NameToLayer(c.layer);
            if (layer < 0) throw new PostLedger308.Refused("layer '" + c.layer + "' does not exist (Renderer297 draws it after the fog; Bolt300Build makes it)");
            var sb = new StringBuilder("FarLight308 " + (dry ? "plan" : "apply") + " " + PostLedger308.Short(scenePath) + " (" + c.version + ", paper " + variant + (variant != Variant(c, "") ? " — OVERRIDE of the data's " + Variant(c, "") + ", for stills only" : "") + ")\n");
            // the scene first: when it cannot be opened nothing — not even the profile / material assets — is written (review N8b)
            var paperMat = AssetDatabase.LoadAssetAtPath<Material>(c.paperMaterial) ?? throw new PostLedger308.Refused("paper material missing: " + c.paperMaterial);
            var scene = PostLedger308.Open(scenePath);
            AttractionLightProfileSO profile;
            sb.Append(Sync(c, dry, out profile).Replace("FarLight308 sync" + (dry ? " (dry)" : "") + "\n", ""));
            var ops = new List<Op>(); int pending = 0, missing = 0, refusedRows = 0, present = 0;
            var paperCtx = NewPaperCtx(c, variant, paperMat, dry);
            if (variant == P2 || c.poles.Any(p => p.enabled)) sb.AppendLine("  sleeve mesh [M vertices]: " + (paperCtx.drum != null ? paperCtx.drum.text : "REFUSED — " + paperCtx.drumWhy));

            foreach (var row in c.lights)
            {
                var lamp = Resolve(scene, row.path, V3(row.at), c.tolerance_m);
                if (lamp == null)
                {
                    if (row.optional) { pending++; sb.AppendLine("  pending " + row.id + ": not in this scene (optional row)"); }
                    else { missing++; sb.AppendLine("  MISSING " + row.id + ": nothing at " + row.path + " within " + F(c.tolerance_m) + " m of " + P(V3(row.at))); }
                    continue;
                }
                if (Forbidden(c, lamp, out string why)) { refusedRows++; sb.AppendLine("  REFUSED row " + row.id + ": " + why); continue; }
                if (!row.glow && !row.paper) { sb.AppendLine("  staged off: " + row.id + " (no card, no paper)"); continue; }
                var light = LampLight(lamp);
                if (light == null) { refusedRows++; sb.AppendLine("  REFUSED row " + row.id + ": no Light on " + PostLedger308.PathOf(lamp) + " (not a lamp)"); continue; }
                var kind = ParseKind(row.kind);
                if (row.paper)
                {
                    // FarLight308.Paper.cs: create / keep / re-shape / take away, by the variant
                    string refusal = PlanPaper(c, paperCtx, row.id, lamp, ops, ref present);
                    if (refusal != null) { refusedRows++; sb.AppendLine("  REFUSED paper " + row.id + ": " + refusal); }
                }
                if (row.glow)
                {
                    if (Child(lamp, c.glow.name) != null) present++;
                    else
                    {
                        var at = Anchor(c, lamp, row.pair); string id = row.id; var lampRef = lamp; var lightRef = light;
                        ops.Add(new Op { what = "glow", row = id, text = "glow " + id + ": " + c.glow.name + " (" + kind + ") at " + P(at) + ", source Light " + light.name + " I=" + F(light.intensity) + " R=" + F(light.range),
                            run = (s, l, utc) => l.made.Add(Record("glow", id, MakeGlow(c, profile, kind, lampRef, lightRef, at, layer), utc)) });
                    }
                }
            }

            foreach (var pole in c.poles)
            {
                if (!pole.enabled) { sb.AppendLine("  staged off: pole " + pole.id); continue; }
                var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == c.root);
                if (root != null && Child(root.transform, pole.id) != null) { present++; continue; }
                var source = Resolve(scene, pole.lanternSource, V3(pole.sourceAt), c.tolerance_m);
                if (source == null) { missing++; sb.AppendLine("  MISSING pole " + pole.id + ": lantern source " + pole.lanternSource + " not found"); continue; }
                if (Forbidden(c, source, out string why)) { refusedRows++; sb.AppendLine("  REFUSED pole " + pole.id + ": source " + why); continue; }
                var body = Child(source, c.paper.body);
                if (body == null) { refusedRows++; sb.AppendLine("  REFUSED pole " + pole.id + ": source has no '" + c.paper.body + "' body"); continue; }
                if (!Ground(pole.x, pole.z, out var ground)) { refusedRows++; sb.AppendLine("  REFUSED pole " + pole.id + ": no terrain under (" + F(pole.x) + ", " + F(pole.z) + ")"); continue; }
                string blocked = Blocked(ground, pole);
                if (blocked != null) { refusedRows++; sb.AppendLine("  REFUSED pole " + pole.id + ": " + blocked); continue; }
                if (paperCtx.drum == null) { refusedRows++; sb.AppendLine("  REFUSED pole " + pole.id + ": the post mesh — " + (paperCtx.drumWhy ?? "the data has no paper.hanji block to name and measure it")); continue; }
                var poleRef = pole; var sourceRef = source; var kind = ParseKind(pole.kind);
                ops.Add(new Op { what = "pole", row = pole.id, text = "pole " + pole.id + ": 장대 등 " + F(pole.height) + " m at " + P(ground) + " (offline y " + F(pole.y_offline) + (Mathf.Abs(ground.y - pole.y_offline) > .5f ? ", WARN differs" : "") + "), Point light R " + F(pole.lightRange) + " I " + F(pole.lightIntensity) + " no shadows, paper + glow",
                    run = (s, l, utc) => l.made.Add(Record("pole", poleRef.id, MakePole(c, profile, kind, poleRef, sourceRef, ground, paperCtx, layer, s), utc)) });
            }

            foreach (var op in ops) sb.AppendLine("  " + (dry ? "would " : "") + (op.verb ?? "create") + " " + op.text);
            string tail = "  rows: to write " + ops.Count + " (paper " + ops.Count(o => o.what == "paper") + " [create " + ops.Count(o => o.what == "paper" && o.verb == null) + ", re-shape " + ops.Count(o => o.verb == ReShape) + ", take away " + ops.Count(o => o.verb == TakeAway) + "], glow " + ops.Count(o => o.what == "glow") + ", pole " + ops.Count(o => o.what == "pole") + "), already there " + present + ", pending " + pending + ", MISSING " + missing + ", REFUSED " + refusedRows;
            if (dry) return sb.AppendLine(tail).Append("  nothing was changed (plan)").ToString();
            if (ops.Count == 0) return sb.AppendLine(tail).Append("  변경 없음 (nothing to write; scene not saved)").ToString();

            string stamp = PostLedger308.Utc(); var ledger = ReadLedger(scenePath);
            string backup = PostLedger308.Backup(BackupRoot, stamp, scenePath); ledger.backups.Add(backup);
            var done = new List<Made>();
            try
            {
                foreach (var op in ops) { int n = ledger.made.Count; op.run(scene, ledger, stamp); for (int i = n; i < ledger.made.Count; i++) done.Add(ledger.made[i]); }
                PostLedger308.SaveScene(scene);   // inside the try: a failed save leaves nothing of this call in the open scene either (review N8c)
            }
            catch
            {
                // nothing half-made stays in the open scene: destroy what this call created, leave the ledger file as it was.
                // (After a failed save the scene stays marked dirty with its old content: the next command refuses until it is reloaded or saved.)
                foreach (var m in Enumerable.Reverse(done)) { var t = FindMade(scene, c, m); if (t != null) Object.DestroyImmediate(t.gameObject); }
                DropEmptyRoot(scene, c);
                // a paper swap destroys the old sleeve before the new one is made: what was destroyed cannot be put back by hand,
                // so the scene is read again from the disk (no dialog: OpenScene drops the unsaved state of the scene it replaces)
                if (ops.Any(o => o.verb != null)) EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                throw;
            }
            WriteLedger(scenePath, ledger);
            return sb.AppendLine(tail).AppendLine("  saved " + scenePath + "; backup " + backup).Append("  ledger " + LedgerFile(scenePath)).ToString();
        }

        static Made Record(string what, string row, Transform t, string utc) => new Made { what = what, row = row, path = PostLedger308.PathOf(t), at = t.position, utc = utc };

        // ------------------------------------------------------------------ builders

        static Mesh Builtin(string name) => Resources.GetBuiltinResource<Mesh>(name) ?? throw new Exception("built-in mesh missing: " + name);

        static void Quiet(MeshRenderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        static Transform MakePaper(Cfg c, Transform lamp, Vector3 pos, float yaw, Vector3 scale, Material mat)
        {
            var go = new GameObject(c.paper.name) { layer = lamp.gameObject.layer };
            go.transform.SetParent(lamp, true);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Div(scale, lamp.lossyScale);
            go.AddComponent<MeshFilter>().sharedMesh = Builtin("Cube.fbx");
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; Quiet(r);
            return go.transform;
        }

        static Transform MakeGlow(Cfg c, AttractionLightProfileSO profile, AttractionLightProfileSO.Kind kind, Transform lamp, Light light, Vector3 at, int layer)
        {
            var entry = profile.Find(kind) ?? throw new Exception("profile has no entry for " + kind);
            if (entry.Material == null) throw new Exception("profile entry " + kind + " has no baked material (run sync)");
            var go = new GameObject(c.glow.name) { layer = layer };
            go.transform.SetParent(lamp, true);
            // the shader reads only the object's origin. The quad is only the culling bound: turned 45 degrees about Y so the flat
            // quad has extent on X and on Z whichever way the camera looks (Load checks cullBounds_m against maxWorldRadius)
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 45f, 0f));
            go.transform.localScale = Div(Vector3.one * Mathf.Max(c.cullBounds_m, 1f), lamp.lossyScale);
            go.AddComponent<MeshFilter>().sharedMesh = Builtin("Quad.fbx");
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = entry.Material; Quiet(r);
            go.AddComponent<AttractionLight308>().Bind(profile, kind, light, r);
            return go.transform;
        }

        // terrain = TerrainCollider or a *Terrain* / *Surface* object (Enclosure305 / Content308 rule); this tool's own root never counts
        static bool IsTerrain(Collider col)
        {
            if (col is TerrainCollider) return true;
            for (var t = col.transform; t != null; t = t.parent) { var n = t.name; if (n.Contains("Terrain") || n.Contains("Surface")) return true; }
            return false;
        }

        static bool Ground(float x, float z, out Vector3 p)
        {
            p = default; float best = float.NegativeInfinity; Physics.SyncTransforms();
            foreach (var h in Physics.RaycastAll(new Vector3(x, 2000f, z), Vector3.down, 4000f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || h.normal.y <= .5f || !IsTerrain(h.collider)) continue;
                bool preview = false; for (var t = h.collider.transform; t != null; t = t.parent) if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) preview = true;
                if (!preview && h.point.y > best) { best = h.point.y; p = h.point; }
            }
            return !float.IsNegativeInfinity(best);
        }

        static string Blocked(Vector3 ground, PoleRow pole)
        {
            foreach (var col in Physics.OverlapCapsule(ground + Vector3.up * (pole.clear_m + .2f), ground + Vector3.up * pole.height, Mathf.Max(pole.clear_m, .05f), ~0, QueryTriggerInteraction.Ignore))
                if (!IsTerrain(col)) return "the pole spot overlaps " + PostLedger308.PathOf(col.transform) + " within " + F(pole.clear_m) + " m (move x / z in the data)";
            return null;
        }

        static Transform MakePole(Cfg c, AttractionLightProfileSO profile, AttractionLightProfileSO.Kind kind, PoleRow pole, Transform source, Vector3 ground, PaperCtx paper, int layer, Scene scene)
        {
            var rootGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == c.root);
            if (rootGo == null) { rootGo = new GameObject(c.root); SceneManager.MoveGameObjectToScene(rootGo, scene); }
            var face = pole.faceTo.Length >= 2 ? new Vector3(pole.faceTo[0] - ground.x, 0f, pole.faceTo[1] - ground.z) : Vector3.forward;
            var holder = new GameObject(pole.id).transform; holder.SetParent(rootGo.transform, false);
            holder.SetPositionAndRotation(ground, face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face.normalized, Vector3.up) : Quaternion.identity);
            // timber of the source inn's lantern cap, else the lantern body's own material
            Material timber = null;
            foreach (Transform ch in source) if (ch.name == "Timber_Cap") { var tr = ch.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(x => x.sharedMaterial != null); if (tr != null) { timber = tr.sharedMaterial; break; } }
            var bodySource = Child(source, c.paper.body);
            if (timber == null) timber = bodySource.GetComponentsInChildren<MeshRenderer>(true).Select(x => x.sharedMaterial).FirstOrDefault(m => m != null);
            if (timber == null) throw new Exception("pole " + pole.id + ": no material on the source lantern to make the pole from");

            // the post = the same drum mesh the paper uses, scaled from its MEASURED radius / half height (relayout fix 2b: the
            // name "Cylinder.fbx" gave the legacy radius-1 mesh — a post twice as thick as poleRadius; nothing is assumed now)
            var drum = paper.drum ?? throw new Exception("pole " + pole.id + ": sleeve mesh " + (paper.drumWhy ?? "not measured"));
            var post = new GameObject("Pole"); post.transform.SetParent(holder, false);
            var postScale = new Vector3(pole.poleRadius / drum.radius, pole.height * .5f / drum.halfHeight, pole.poleRadius / drum.radius);
            post.transform.localPosition = Vector3.up * (pole.height * .5f) - Vector3.Scale(drum.centre, postScale);
            post.transform.localScale = postScale;
            post.AddComponent<MeshFilter>().sharedMesh = drum.mesh;
            post.AddComponent<MeshRenderer>().sharedMaterial = timber;
            if (pole.collider) { var cap = post.AddComponent<CapsuleCollider>(); cap.direction = 1; cap.center = drum.centre; cap.radius = drum.radius; cap.height = 2f * drum.halfHeight; }

            var arm = new GameObject("Arm"); arm.transform.SetParent(holder, false);
            arm.transform.localPosition = new Vector3(0f, pole.height - pole.armThickness * .5f, pole.armLength * .5f);
            arm.transform.localScale = new Vector3(pole.armThickness, pole.armThickness, pole.armLength + pole.poleRadius * 2f);
            arm.AddComponent<MeshFilter>().sharedMesh = Builtin("Cube.fbx");
            arm.AddComponent<MeshRenderer>().sharedMaterial = timber;

            // the lamp: a copy of the source's KCISA lantern body hanging from the arm end, one Point light, paper sleeve, far glow
            var lamp = new GameObject("Warm_Pole_Lantern").transform; lamp.SetParent(holder, false);
            var body = Object.Instantiate(bodySource.gameObject, lamp); body.name = c.paper.body;
            foreach (var col in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            Body(lamp, c.paper.body, out var centre0, out _, out var size0);
            var hang = holder.TransformPoint(new Vector3(0f, pole.height - pole.armThickness, pole.armLength));
            lamp.position += hang - (centre0 + Vector3.up * (size0.y * .5f));   // top of the body under the arm end
            Body(lamp, c.paper.body, out var centre, out float yaw, out var size);
            var lightGo = new GameObject("Light"); lightGo.transform.SetParent(lamp, false); lightGo.transform.position = centre;
            var light = lightGo.AddComponent<Light>(); light.type = LightType.Point; light.range = pole.lightRange; light.intensity = pole.lightIntensity;
            light.color = pole.lightColour.Length >= 3 ? new Color(pole.lightColour[0], pole.lightColour[1], pole.lightColour[2]) : Color.white; light.shadows = LightShadows.None;
            var sleeve = WantPaper(c, paper, lamp, out string noPaper);   // the pole's lantern wears the same variant as the porch lanterns
            if (sleeve != null) BuildPaper(c, lamp, sleeve); else if (paper.variant != P0) throw new Exception("pole " + pole.id + ": " + noPaper);
            MakeGlow(c, profile, kind, lamp, light, centre + Vector3.up * ((c.glow.lanternAnchorFraction - .5f) * size.y), layer);
            return holder;
        }

        // ------------------------------------------------------------------ revert

        /// <summary>The object a ledger row made: same name (the last path segment), nearest to the recorded position within 0.5 m.
        /// A sleeve / card whose lamp another tool has re-seated since (RoadInn308 moves its lantern; relayout fix 2b) is found
        /// through its lights[] row instead: the lamp the row names, then this tool's child of that name under it.</summary>
        static Transform FindMade(Scene scene, Cfg c, Made m)
        {
            string name = m.path.Substring(m.path.LastIndexOf('/') + 1); Transform best = null; float bd = .5f;
            foreach (var t in PostLedger308.All<Transform>(scene))
            {
                if (t.name != name) continue;
                float d = Vector3.Distance(t.position, m.at); if (d <= bd) { bd = d; best = t; }
            }
            if (best != null || m.what == "pole" || (name != c.paper.name && name != c.glow.name)) return best;
            var row = c.lights.FirstOrDefault(l => l.id == m.row); if (row == null) return null;
            var lamp = Resolve(scene, row.path, V3(row.at), c.tolerance_m);
            return lamp != null ? Child(lamp, name) : null;
        }

        static void DropEmptyRoot(Scene scene, Cfg c)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == c.root);
            if (root != null && root.transform.childCount == 0 && root.GetComponents<Component>().Length == 1) Object.DestroyImmediate(root);
        }

        static string Revert(string scenePath)
        {
            PostLedger308.RequireEditable();
            var c = Load();
            if (!File.Exists(LedgerFile(scenePath))) throw new PostLedger308.Refused("no ledger " + LedgerFile(scenePath) + " (nothing applied, or already reverted)");
            var ledger = ReadLedger(scenePath); var sb = new StringBuilder("FarLight308 revert " + PostLedger308.Short(scenePath) + "\n");
            var scene = PostLedger308.Open(scenePath);
            string stamp = PostLedger308.Utc(); string backup = PostLedger308.Backup(BackupRoot, stamp, scenePath); int gone = 0, kept = 0;
            foreach (var m in Enumerable.Reverse(ledger.made).ToList())
            {
                var t = FindMade(scene, c, m);
                if (t == null) { sb.AppendLine("  already gone: " + m.what + " " + m.row + " (" + m.path + ")"); ledger.made.Remove(m); continue; }
                // only this tool's own objects are ever destroyed: the card / sleeve by name, a pole under this tool's root
                bool mine = m.what == "pole" ? t.parent != null && t.parent.name == c.root && t.parent.parent == null : t.name == c.paper.name || t.name == c.glow.name;
                if (!mine || PostLedger308.UnderProtectedTree(t)) { kept++; sb.AppendLine("  KEPT (not this tool's object): " + PostLedger308.PathOf(t)); continue; }
                Object.DestroyImmediate(t.gameObject); ledger.made.Remove(m); gone++;
            }
            DropEmptyRoot(scene, c);
            PostLedger308.SaveScene(scene);
            if (ledger.made.Count == 0) { string archived = LedgerFile(scenePath) + ".reverted-" + stamp; File.Move(LedgerFile(scenePath), archived); sb.AppendLine("  ledger archived " + archived); }
            else { ledger.backups.Add(backup); WriteLedger(scenePath, ledger); }
            return sb.Append("  destroyed " + gone + ", kept " + kept + "; saved " + scenePath + "; backup " + backup).ToString();
        }
    }
}
