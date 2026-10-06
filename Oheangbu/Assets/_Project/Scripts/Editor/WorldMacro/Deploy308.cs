using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // SPEC-SPELL-DEPLOY-308 BUILD_PLAN step 7: importer, unit checks and the per-row switch of the spell deploy layer.
    // Queue: Oheangbu.EditorTools.WorldMacro.Deploy308 Run "<command>"   (no modal dialog: a refusal comes back as "refused: ...")
    //   deploy308-status
    //   deploy308-assets[:dry|:revert]     texture import settings, M_InkBurst308 / M_InkResidue308, the profile asset (master switch OFF)
    //   deploy308-map[:dry]                Generated/deploy_map308.json (made from the glyph CSV) -> SpellDeploy308Map.asset, 120 rows
    //   deploy308-unit                     AC-D2 D3 D4 D5 D9 D10 D11 D12 D19 D22 D16 D18 D26 D27 D23 (in memory, nothing saved)
    //   deploy308-enable:<글자,…>[:dry|:revert]   Vfx120Profile.Deploy308 per letter + the profile's EnabledLetters / master switch
    // Saving: AssetDatabase.SaveAssetIfDirty on the named asset only - never SaveAssets (other sessions' dirty assets stay untouched).
    // Assets this tool creates are listed in the ledger (repo root, outside Assets); deploy308-assets:revert deletes only those.
    public static class Deploy308
    {
        internal const string ArtDir = "Assets/_Project/Art/SpellVFX120/Deploy308";
        internal const string ResourcesDir = "Assets/_Project/Resources/Deploy308";
        internal const string ProfilePath = ResourcesDir + "/SpellDeploy308Profile.asset";
        internal const string MapPath = ResourcesDir + "/SpellDeploy308Map.asset";
        internal const string BurstMaterialPath = ArtDir + "/M_InkBurst308.mat";
        internal const string ResidueMaterialPath = ArtDir + "/M_InkResidue308.mat";
        internal const string AtlasPath = ArtDir + "/ink_deploy_atlas308.png";
        internal const string AtlasMobilePath = ArtDir + "/ink_deploy_atlas308_m.png";
        internal const string FloodPath = ArtDir + "/ink_flood_mask308.png";
        internal const string GeneratedDir = "Art/SpellVFX120/Deploy308/Generated";
        internal const string LedgerFile = "Art/SpellVFX120/Deploy308/ledger308.json";
        internal const string CsvFile = "Docs/오행부_작도어휘_v0_1.csv";
        internal const string BurstShader = "Oheangbu/InkBurst308", ResidueShader = "Oheangbu/InkResidue308",
            ImpactShader = "Hidden/Oheangbu/ImpactFrame308", FlatShader = "Hidden/Oheangbu/InkFlat308";

        [Serializable] internal sealed class MapRow { public string letter, category, frame, element, final, legacyBody, formOverride; public bool impactFrame, impactOnTrigger; }
        [Serializable] internal sealed class MapFile { public int version; public string source, csvSha256; public MapRow[] rows; }
        [Serializable] internal sealed class Enabled { public string letter, profilePath, previous; }
        [Serializable] internal sealed class Ledger { public string utc = ""; public List<string> created = new List<string>(); public List<Enabled> enabled = new List<Enabled>(); }

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            try
            {
                switch (a[0])
                {
                    case "deploy308-status": return Status();
                    case "deploy308-assets": return Assets(Has(a, "dry"), Has(a, "revert"));
                    case "deploy308-map": return Map(Has(a, "dry"));
                    case "deploy308-unit": return Deploy308Unit.Run();
                    case "deploy308-enable":
                        if (a.Length < 2 || a[1].Length == 0) throw new PostLedger308.Refused("use deploy308-enable:<글자,…>[:dry|:revert]");
                        return Enable(a[1], Has(a, "dry"), Has(a, "revert"));
                    default: throw new PostLedger308.Refused("unknown command '" + command + "'");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
        }

        static bool Has(string[] a, string flag) => a.Skip(1).Any(p => string.Equals(p.Trim(), flag, StringComparison.OrdinalIgnoreCase));

        static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
        }

        // ---------------------------------------------------------------- ledger

        internal static Ledger LoadLedger()
        {
            string path = PostLedger308.RepoPath(LedgerFile);
            if (!File.Exists(path)) return new Ledger();
            return JsonUtility.FromJson<Ledger>(File.ReadAllText(path, Encoding.UTF8)) ?? new Ledger();
        }

        static void SaveLedger(Ledger ledger)
        {
            ledger.utc = PostLedger308.Utc();
            string path = PostLedger308.RepoPath(LedgerFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(ledger, true).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
        }

        // ---------------------------------------------------------------- shared with DeployLook308

        internal static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        /// <summary>Reads Generated/deploy_map308.json and re-asserts what the python tool asserted (AC-D2).</summary>
        internal static SpellDeploy308MapSO.Row[] ReadMap(out string report, out string csvSha)
        {
            string path = PostLedger308.RepoPath(GeneratedDir + "/deploy_map308.json");
            if (!File.Exists(path)) throw new PostLedger308.Refused("no " + GeneratedDir + "/deploy_map308.json (run python Tools/Art/deploy308_map.py)");
            var file = JsonUtility.FromJson<MapFile>(File.ReadAllText(path, Encoding.UTF8));
            if (file == null || file.rows == null) throw new PostLedger308.Refused("deploy_map308.json has no rows");
            csvSha = file.csvSha256 ?? "";
            var rows = new SpellDeploy308MapSO.Row[file.rows.Length];
            var seen = new HashSet<char>();
            for (int i = 0; i < rows.Length; i++)
            {
                var r = file.rows[i];
                if (string.IsNullOrEmpty(r.letter) || r.letter.Length != 1 || !seen.Add(r.letter[0])) throw new PostLedger308.Refused("row " + i + ": bad or repeated letter '" + r.letter + "'");
                rows[i] = new SpellDeploy308MapSO.Row
                {
                    Letter = r.letter, Category = Parse<DeployCategory308>(r.category, i), Frame = Parse<DeployFrame308>(r.frame, i), Element = Parse<Element>(r.element, i),
                    Final = Parse<DeployFinal308>(r.final, i), ImpactFrame = r.impactFrame, ImpactOnTrigger = r.impactOnTrigger,
                    LegacyBody = Parse<DeployLegacyBody308>(r.legacyBody, i), FormOverride = r.formOverride ?? "",
                };
                string grid = GridError(rows[i]);
                if (grid != null) throw new PostLedger308.Refused("row " + r.letter + ": " + grid);
            }
            var probe = ScriptableObject.CreateInstance<SpellDeploy308MapSO>();
            try
            {
                probe.Rows = rows;
                if (!SpellDeploy308MapSO.CountsMatch(probe, out report)) throw new PostLedger308.Refused("category counts are not 50/5/5/25/5/5/5/20: " + report);
            }
            finally { Object.DestroyImmediate(probe); }
            int impact = rows.Count(r => r.ImpactFrame);
            if (impact != 50 || rows.Any(r => r.ImpactFrame != (r.Category == DeployCategory308.AttackSingle || r.Category == DeployCategory308.AttackArea)))
                throw new PostLedger308.Refused("impact frame rows must be exactly the 50 attack rows (found " + impact + ")");
            report += " | impact rows " + impact + " | on-trigger rows " + rows.Count(r => r.ImpactOnTrigger);
            return rows;
        }

        static T Parse<T>(string text, int row) where T : struct
        {
            if (!Enum.TryParse(text, false, out T value) || !Enum.IsDefined(typeof(T), value)) throw new PostLedger308.Refused("row " + row + ": '" + text + "' is not a " + typeof(T).Name);
            return value;
        }

        // Spec section 6: the (medial, final) grid is the same for all five elements. The syllable is decomposed here, so the
        // check does not depend on what the JSON says about frame / element / final.
        static string GridError(SpellDeploy308MapSO.Row row)
        {
            int code = row.Char - 0xAC00;
            if (code < 0 || code >= 11172) return "not a Hangul syllable";
            int initial = code / 588, medial = code % 588 / 28, final = code % 28;
            // initials ㄱ0 ㄴ2 ㅁ6 ㅅ9 ㅇ11 · medials ㅏ0 ㅓ4 ㅗ8 ㅜ13 · finals none0 ㄱ1 ㄴ4 ㅁ16 ㅅ19 ㅇ21
            int e = Array.IndexOf(new[] { 0, 2, 6, 9, 11 }, initial), m = Array.IndexOf(new[] { 0, 4, 8, 13 }, medial), f = Array.IndexOf(new[] { 0, 1, 4, 16, 19, 21 }, final);
            if (e < 0 || m < 0 || f < 0) return "jamo outside the 5 x 4 x 6 table";
            if ((int)row.Element != e) return "element " + row.Element + " does not match the initial";
            if ((int)row.Frame != m) return "frame " + row.Frame + " does not match the medial";
            if ((int)row.Final != f) return "final " + row.Final + " does not match the syllable";
            bool mieum = f == 3, none = f == 0, own = f == new[] { 1, 2, 3, 4, 5 }[e];   // the final that is the initial's own jamo
            DeployCategory308 want = m == 0 ? (mieum ? DeployCategory308.ComboInstall : DeployCategory308.AttackSingle)
                : m == 1 ? (none ? DeployCategory308.Parry : DeployCategory308.Buff)
                : m == 2 ? (mieum ? DeployCategory308.Summon : DeployCategory308.AttackArea)
                : none ? DeployCategory308.Ward : own ? DeployCategory308.Field : DeployCategory308.Blank;
            return row.Category == want ? null : "category " + row.Category + " but the grid says " + want;
        }

        internal static Color Grey(float value, float warm) => new Color(value, value * (1f - warm * .07f), value * (1f - warm * .14f), 1f);

        internal static void ApplyBurstMaterial(Material m, SpellDeploy308ProfileSO p, Texture atlas)
        {
            if (atlas != null) m.SetTexture("_Atlas", atlas);
            m.SetColor("_BodyColor", Grey(p.Stroke.BodyValue, 1f));
            m.SetColor("_RimColor", Grey(p.Stroke.RimValue, 1f));
            m.SetFloat("_RimShare", p.Stroke.RimShare);
            m.SetFloat("_NeedleMinPixels", p.Stroke.NeedleMinPixels);
            m.SetFloat("_NearClip", p.Stroke.NearClip);
            m.SetFloat("_NearFade", p.Stroke.NearFade);
            m.SetFloat("_TintAmount", 0f);
            m.SetFloat("_Cel", 1000f); m.SetFloat("_Advance", 1f); m.SetFloat("_Melt", 0f);
            // D308-10c: the glow is momentary and per burst - the material itself asks for none (the runtime's property block does)
            m.SetFloat("_Glow", 0f); m.SetFloat("_GlowCels", p.GlowCels); m.SetFloat("_GlowRim", Mathf.Clamp01(p.Stroke.GlowRim));
            m.enableInstancing = false;
        }

        internal static void ApplyResidueMaterial(Material m, Texture atlas)
        {
            if (atlas != null) m.SetTexture("_Atlas", atlas);
            m.enableInstancing = true;
        }

        internal static Shader RequireShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null) throw new PostLedger308.Refused("shader '" + name + "' is not in the project (copy Stage308_deploy/_ProjectAssets first, then Refresh)");
            if (ShaderUtil.ShaderHasError(shader))
            {
                var messages = ShaderUtil.GetShaderMessages(shader);
                throw new PostLedger308.Refused("shader '" + name + "' has compile errors: " + string.Join(" | ", messages.Take(4).Select(x => x.message + " (line " + x.line + ")")));
            }
            return shader;
        }

        // ---------------------------------------------------------------- status

        static string Status()
        {
            var sb = new StringBuilder("deploy308 status\n");
            var profile = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(ProfilePath);
            var map = AssetDatabase.LoadAssetAtPath<SpellDeploy308MapSO>(MapPath);
            sb.AppendLine("profile: " + (profile == null ? "missing" : ProfilePath + " | LayerEnabled=" + profile.LayerEnabled + " | EnabledLetters='" + profile.EnabledLetters + "' | map=" + (profile.Map != null)
                + " | burst mat=" + (profile.BurstMaterial != null) + " | residue mat=" + (profile.ResidueMaterial != null) + " | impact shader=" + (profile.ImpactShader != null)
                + " | flat shader=" + (profile.FlatShader != null) + " | atlas=" + (profile.Atlas != null) + " | flood events=" + (profile.Flood.AllowedEvents != null ? profile.Flood.AllowedEvents.Length : 0)));
            if (profile != null) sb.AppendLine("lifetimes (clamped 3-5 s): spell " + F(profile.SpellLife) + " foot " + F(profile.FootLife) + " field tail " + F(profile.FieldTail) + " | flips/s " + F(profile.FlipsPerSecond));
            if (profile != null) sb.AppendLine("D308-10c: Impact.Trigger=" + profile.Impact.Trigger + " (kill grace " + F(profile.Impact.GroggyKillGrace) + " s) | glow " + F(profile.GlowAmount) + " over " + profile.GlowCels
                + " cels (code ceilings " + F(SpellDeploy308ProfileSO.MaxGlowAmount) + " / " + SpellDeploy308ProfileSO.MaxGlowCels + ") | hit splash " + profile.Hit.Enabled + " (air drops " + profile.Hit.AirDrops + ", ground "
                + F(profile.Hit.GroundSmear) + " m, replaces the KTP enemy-hit contact " + profile.Hit.ReplaceLegacyContact + ") | hit reaction: pop " + F(profile.Flicker.PopSeconds) + " s, stain " + profile.Flicker.Stain + ", knock " + profile.Flicker.Knock);
            if (map == null) sb.AppendLine("map: missing");
            else { SpellDeploy308MapSO.CountsMatch(map, out string counts); sb.AppendLine("map: " + counts + " | csv " + map.SourceCsvSha256); }
            foreach (string name in new[] { BurstShader, ResidueShader, ImpactShader, FlatShader })
            {
                var shader = Shader.Find(name);
                sb.AppendLine("shader " + name + ": " + (shader == null ? "missing" : ShaderUtil.ShaderHasError(shader) ? "COMPILE ERROR" : "ok"));
            }
            sb.AppendLine("render pass type: " + (Type.GetType("Oheangbu.App.SpellVFX120.ImpactFramePass308, Oheangbu.App") != null ? "present" : "ABSENT (deploy _NeedsAsmdef: no impact frames, no flicker)"));
            int linked = 0; var letters = new StringBuilder();
            foreach (string guid in AssetDatabase.FindAssets("t:Vfx120Profile"))
            {
                var vp = AssetDatabase.LoadAssetAtPath<Vfx120Profile>(AssetDatabase.GUIDToAssetPath(guid));
                if (vp != null && vp.Deploy308 != null) { linked++; letters.Append(vp.Glyph); }
            }
            var ledger = LoadLedger();
            sb.AppendLine("Vfx120Profile assets with Deploy308 set: " + linked + " (" + letters + ") | ledger: created " + ledger.created.Count + ", enabled " + ledger.enabled.Count + " at " + ledger.utc);
            sb.Append("play: " + EditorApplication.isPlaying);
            if (EditorApplication.isPlaying)
            {
                var wiring = Object.FindFirstObjectByType<CombatLoopWiring>();
                sb.Append(wiring == null ? " | no CombatLoopWiring" : wiring.DeployDirector308 == null ? " | layer asleep (no director)" : " | " + wiring.DeployDirector308.Describe());
            }
            return sb.ToString();
        }

        internal static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- assets

        static string Assets(bool dry, bool revert)
        {
            RequireIdle();
            var ledger = LoadLedger();
            var sb = new StringBuilder("deploy308-assets" + (dry ? " (dry)" : revert ? " (revert)" : "") + "\n");
            if (revert)
            {
                foreach (string path in ledger.created.ToArray())
                {
                    // the ledger is a plain file: only assets inside the two folders of this tool are ever deleted
                    if (!Owned(path)) { sb.AppendLine("skipped " + path + " (outside " + ArtDir + " / " + ResourcesDir + ")"); continue; }
                    bool exists = AssetDatabase.LoadMainAssetAtPath(path) != null;
                    sb.AppendLine((dry ? "would delete " : "deleted ") + path + (exists ? "" : " (already gone)"));
                    if (!dry) { if (exists) AssetDatabase.DeleteAsset(path); ledger.created.Remove(path); }
                }
                if (!dry) SaveLedger(ledger);
                return sb.ToString().TrimEnd();
            }

            Shader burst = RequireShader(BurstShader), residue = RequireShader(ResidueShader), impact = RequireShader(ImpactShader), flat = RequireShader(FlatShader);
            foreach (string path in new[] { AtlasPath, AtlasMobilePath, FloodPath })
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new PostLedger308.Refused(path + " is not imported (copy " + GeneratedDir + "/*.png into " + ArtDir + " first, then Refresh)");
                var platform = importer.GetPlatformTextureSettings("Standalone");
                bool change = importer.sRGBTexture || !importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp || importer.alphaSource != TextureImporterAlphaSource.None
                    || !platform.overridden || platform.format != TextureImporterFormat.BC7;
                sb.AppendLine(path + ": " + (change ? (dry ? "would set" : "set") + " linear, mips, clamp, BC7 (Standalone)" : "import settings already right"));
                if (dry || !change) continue;
                importer.sRGBTexture = false; importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp; importer.alphaSource = TextureImporterAlphaSource.None;
                platform.overridden = true; platform.format = TextureImporterFormat.BC7; platform.maxTextureSize = 1024;
                importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
            }
            if (dry)
            {
                foreach (string path in new[] { BurstMaterialPath, ResidueMaterialPath, ProfilePath })
                    sb.AppendLine(path + ": " + (AssetDatabase.LoadMainAssetAtPath(path) == null ? "would create" : "exists (references re-linked, tuned values kept)"));
                return sb.ToString().TrimEnd();
            }

            EnsureFolder(ArtDir); EnsureFolder(ResourcesDir);
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var profile = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(ProfilePath);
            if (profile == null)
            {
                RequireFree(ProfilePath);
                profile = ScriptableObject.CreateInstance<SpellDeploy308ProfileSO>();
                profile.LayerEnabled = false;   // the layer sleeps until deploy308-enable switches a row on
                AssetDatabase.CreateAsset(profile, ProfilePath);
                Created(ledger, ProfilePath); sb.AppendLine("created " + ProfilePath + " (LayerEnabled=false)");
            }
            var burstMaterial = EnsureMaterial(BurstMaterialPath, burst, ledger, sb);
            ApplyBurstMaterial(burstMaterial, profile, atlas);
            var residueMaterial = EnsureMaterial(ResidueMaterialPath, residue, ledger, sb);
            ApplyResidueMaterial(residueMaterial, atlas);
            profile.BurstMaterial = burstMaterial; profile.ResidueMaterial = residueMaterial; profile.ImpactShader = impact; profile.FlatShader = flat;
            profile.Atlas = atlas; profile.AtlasMobile = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasMobilePath); profile.FloodMask = AssetDatabase.LoadAssetAtPath<Texture2D>(FloodPath);
            int layer = LayerMask.NameToLayer("VfxAfterFog");
            if (layer >= 0) profile.RenderLayer = layer;
            var map = AssetDatabase.LoadAssetAtPath<SpellDeploy308MapSO>(MapPath);
            if (map != null) profile.Map = map;
            foreach (Object asset in new Object[] { burstMaterial, residueMaterial, profile }) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
            SaveLedger(ledger);
            sb.AppendLine("profile linked: burst / residue materials, impact / flat shaders, atlas " + (atlas != null) + ", mobile atlas " + (profile.AtlasMobile != null) + ", flood mask " + (profile.FloodMask != null)
                + ", render layer " + profile.RenderLayer + ", map " + (profile.Map != null));
            sb.Append("shader compile errors: 0 (4 shaders) | saved 3 assets with SaveAssetIfDirty | ledger " + LedgerFile);
            return sb.ToString();
        }

        static Material EnsureMaterial(string path, Shader shader, Ledger ledger, StringBuilder sb)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) { if (material.shader != shader) material.shader = shader; return material; }
            RequireFree(path);
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            Created(ledger, path); sb.AppendLine("created " + path);
            return material;
        }

        static void Created(Ledger ledger, string path) { if (!ledger.created.Contains(path)) ledger.created.Add(path); }

        static bool Owned(string path) => path != null && !path.Contains("..") && (path.StartsWith(ArtDir + "/", StringComparison.Ordinal) || path.StartsWith(ResourcesDir + "/", StringComparison.Ordinal));

        // CreateAsset writes over whatever file is at the path: refuse when something that is not ours already sits there
        static void RequireFree(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(PostLedger308.RepoPath("Oheangbu/" + path)))
                throw new PostLedger308.Refused(path + " already holds a file of another kind: it is not overwritten (move it away, then run again)");
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ---------------------------------------------------------------- map

        static string Map(bool dry)
        {
            RequireIdle();
            var rows = ReadMap(out string report, out string csvSha);
            string csvNow = Sha256(File.ReadAllBytes(PostLedger308.RepoPath(CsvFile)));
            if (!string.Equals(csvNow, csvSha, StringComparison.OrdinalIgnoreCase))
                throw new PostLedger308.Refused("the glyph CSV changed after the map was generated (csv " + csvNow.Substring(0, 12) + ", map " + (csvSha.Length >= 12 ? csvSha.Substring(0, 12) : csvSha) + "): rerun python Tools/Art/deploy308_map.py");
            var sb = new StringBuilder("deploy308-map" + (dry ? " (dry)" : "") + ": " + report + " | csv sha matches\n");
            var map = AssetDatabase.LoadAssetAtPath<SpellDeploy308MapSO>(MapPath);
            if (dry) { sb.Append(map == null ? "would create " + MapPath : "would rewrite " + MapPath + " (" + map.Count + " rows now)"); return sb.ToString(); }
            var ledger = LoadLedger();
            EnsureFolder(ResourcesDir);
            if (map == null)
            {
                RequireFree(MapPath);
                map = ScriptableObject.CreateInstance<SpellDeploy308MapSO>();
                AssetDatabase.CreateAsset(map, MapPath);
                Created(ledger, MapPath); sb.AppendLine("created " + MapPath);
            }
            map.Rows = rows; map.SourceCsvSha256 = csvSha;
            EditorUtility.SetDirty(map); AssetDatabase.SaveAssetIfDirty(map);
            var profile = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(ProfilePath);
            if (profile != null && profile.Map != map) { profile.Map = map; EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile); sb.AppendLine("profile.Map linked"); }
            SaveLedger(ledger);
            sb.Append("wrote " + rows.Length + " rows");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- enable

        static string Enable(string letters, bool dry, bool revert)
        {
            RequireIdle();
            var profile = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(ProfilePath);
            if (profile == null) throw new PostLedger308.Refused("no profile asset (run deploy308-assets)");
            if (profile.Map == null) throw new PostLedger308.Refused("the profile has no map (run deploy308-map)");
            if (!revert && (profile.BurstMaterial == null || profile.ResidueMaterial == null)) throw new PostLedger308.Refused("the profile has no materials (run deploy308-assets)");
            var ledger = LoadLedger();
            var wanted = letters == "all" ? (revert ? profile.EnabledLetters : string.Concat(profile.Map.Rows.Select(r => r.Letter))) : letters.Replace(",", "").Replace(" ", "");
            var byGlyph = new Dictionary<char, List<Vfx120Profile>>();
            foreach (string guid in AssetDatabase.FindAssets("t:Vfx120Profile"))
            {
                var vp = AssetDatabase.LoadAssetAtPath<Vfx120Profile>(AssetDatabase.GUIDToAssetPath(guid));
                if (vp == null || string.IsNullOrEmpty(vp.Glyph)) continue;
                if (!byGlyph.TryGetValue(vp.Glyph[0], out var list)) byGlyph[vp.Glyph[0]] = list = new List<Vfx120Profile>();
                list.Add(vp);
            }
            var sb = new StringBuilder("deploy308-enable" + (dry ? " (dry)" : revert ? " (revert)" : "") + "\n");
            var enabled = new HashSet<char>(profile.EnabledLetters ?? "");
            int touched = 0;
            foreach (char letter in wanted.Distinct())
            {
                if (!profile.Map.TryGet(letter, out var row)) throw new PostLedger308.Refused("'" + letter + "' is not one of the 120 letters");
                byGlyph.TryGetValue(letter, out var list);
                int count = list != null ? list.Count : 0;
                sb.AppendLine(letter + " " + row.Category + " " + row.Element + " legacy=" + row.LegacyBody + " impact=" + row.ImpactFrame + ": " + count + " Vfx120Profile asset(s)"
                    + (count == 0 ? " - hook B / contact only (no authored effect asset carries this letter)" : ""));
                if (revert) enabled.Remove(letter); else enabled.Add(letter);
                if (list == null) continue;
                foreach (var vp in list)
                {
                    string path = AssetDatabase.GetAssetPath(vp);
                    var target = revert ? null : profile;
                    if (vp.Deploy308 == target) { sb.AppendLine("  = " + path); continue; }
                    sb.AppendLine("  " + (dry ? "would " : "") + (revert ? "clear " : "set ") + path);
                    if (dry) continue;
                    ledger.enabled.RemoveAll(x => x.profilePath == path);
                    if (!revert) ledger.enabled.Add(new Enabled { letter = letter.ToString(), profilePath = path, previous = vp.Deploy308 != null ? AssetDatabase.GetAssetPath(vp.Deploy308) : "" });
                    vp.Deploy308 = target;
                    EditorUtility.SetDirty(vp); AssetDatabase.SaveAssetIfDirty(vp);
                    touched++;
                }
            }
            string text = new string(enabled.OrderBy(c => c).ToArray());
            sb.Append("EnabledLetters '" + profile.EnabledLetters + "' -> '" + text + "' | LayerEnabled " + profile.LayerEnabled + " -> " + (text.Length > 0));
            if (dry) return sb.ToString();
            profile.EnabledLetters = text; profile.LayerEnabled = text.Length > 0;
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            SaveLedger(ledger);
            sb.Append(" | " + touched + " Vfx120Profile asset(s) saved (SaveAssetIfDirty each) | ledger " + LedgerFile);
            return sb.ToString();
        }
    }
}
