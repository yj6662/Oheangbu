using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.Spell;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // SPEC-SPELL-SWING-308 stage 3 (D308-80b): the great two-handed brush and its motions, from the files made outside Unity
    // (Art/Characters308/GreatBrush308/v5 and .../motion2) into the game. Editor only, edit mode only, no dialogs: every command
    // returns its report as text (the editor queue calls Run).
    //
    //   status            what exists (sources, imported assets, controller copy, scene, Resources profiles)
    //   import            copy brush FBX + bristle texture + the ten clip FBXs into Assets (fixed .meta guids) and set the importers
    //   materials         temporary brush materials on Oheangbu/WorldMacroTexturedSurface (the wet-ink shader is deferred)
    //   controller        COPY the live controller to AC_PlaytestLocomotion_Brush308 and add the two brush layers (the live one is never edited)
    //   profile           (re)write the computed parts of Resources/Swing308/GreatBrush308Profile from grip.json - only if the asset exists
    //   install[:save]    scene: C02_WorldBrush308 / C02_NearBrush308 next to the old brushes (old ones switched off, not deleted),
    //                     GreatBrushBristles308 + SpellSwing308, appearance profile -> the controller copy, the two Resources profiles
    //   revert[:save]     undo install: old brushes on, controller reference back, added objects and components removed, the profiles deleted
    //   check             contract checks (blendshape defaults, bones, sockets, unit scale, clip lengths, avatar, layers, grip, scene wiring)
    //
    // Order: import -> materials -> controller -> install -> check.   `:save` saves the player's scene; without it the scene is only marked dirty.
    public static class GreatBrush308Build
    {
        const string Folder = "Assets/_Project/Art/Characters/GreatBrush308";
        const string BrushModel = Folder + "/Models/SM_GreatBrush308.fbx";
        // authored length (grip.json "length_s") over imported length; 1 when either is unknown
        static float ClipSpeed308(string state, AnimationClip clip)
        {
            try
            {
                if (clip == null || clip.length <= 0f) return 1f;
                float authored = ClipSecondsJson(At(LoadGrip(out _), "clips", state));
                return authored > 0f ? Mathf.Clamp(clip.length / authored, .5f, 2f) : 1f;
            }
            catch { return 1f; }
        }

        const string BristleTexture = Folder + "/Textures/T_GreatBrush308_Bristle.png";
        const string LiveController = "Assets/_Project/Art/Characters/PlaytestNaturalLocomotion/AC_PlaytestLocomotion.controller";
        const string Controller = Folder + "/AC_PlaytestLocomotion_Brush308.controller";
        const string MaskPath = Folder + "/Brush308_UpperBody.mask";
        const string TorsoMaskPath = Folder + "/Brush308_Torso.mask";
        const string TorsoLayerName = "Brush308Torso";
        const string LeftMaskPath = Folder + "/Brush308_LeftArm.mask";
        const string LeftLayerName = "Brush308Left";
        const string RecordPath = Folder + "/install-record.json";
        const string ResourcesFolder = "Assets/_Project/Resources/Swing308";
        const string GreatProfilePath = ResourcesFolder + "/GreatBrush308Profile.asset";
        const string SwingProfilePath = ResourcesFolder + "/SpellSwingProfile308.asset";
        const string PlayerModel = WorldMacroPlayerReRigAuthoring.ModelPath;
        const string BrushShader = "Oheangbu/WorldMacroTexturedSurface";
        const string WorldName = "C02_WorldBrush308", NearName = "C02_NearBrush308";
        const string ClipPrefix = "A_Brush308_";

        static readonly string[] ClipNames = { "SwingSweep", "SwingArc", "SwingSlam", "GuardIn", "GuardHold", "BuffDraw", "StanceIdle", "CarryIdle", "ToCarry", "ToStance", "SwingThrow", "SwingStrikeFar" };   // D308-84a: the last two
        static readonly string[] BodyStates = { "SwingSweep", "SwingArc", "SwingSlam", "GuardIn", "GuardHold", "BuffDraw", "SwingThrow", "SwingStrikeFar" };   // the full-body layer: motions only
        static readonly string[] TimedStates = { "SwingSweep", "SwingArc", "SwingSlam", "GuardIn", "BuffDraw", "SwingThrow", "SwingStrikeFar" };              // normalized time from Brush308Time
        static readonly string[] ShiftStates = { "ToCarry", "ToStance" };
        static readonly string[] AvatarFromStance = { "SwingThrow", "SwingStrikeFar", "SwingSlam" };   // D308-87: SwingSlam re-exported alone (whole-body)
        static readonly string[] WholeBody = { "SwingSlam" };   // D308-87: steps · hop · lunge - the body's travel stays in the pose (see Import())

        // fixed guids: the same files get the same identity on every machine and after every re-import
        static readonly Dictionary<string, string> Guids = new Dictionary<string, string>
        {
            { Folder, "3f08a1d2c4b54e6f9a7b70d308800021" }, { Folder + "/Models", "3f08a1d2c4b54e6f9a7b70d308800022" },
            { Folder + "/Animations", "3f08a1d2c4b54e6f9a7b70d308800023" }, { Folder + "/Materials", "3f08a1d2c4b54e6f9a7b70d308800024" },
            { Folder + "/Textures", "3f08a1d2c4b54e6f9a7b70d308800025" },
            { BrushModel, "3f08a1d2c4b54e6f9a7b70d308800031" }, { BristleTexture, "3f08a1d2c4b54e6f9a7b70d308800032" },
            { Controller, "3f08a1d2c4b54e6f9a7b70d308800033" },
            { ClipPath("SwingSweep"), "3f08a1d2c4b54e6f9a7b70d308800041" }, { ClipPath("SwingArc"), "3f08a1d2c4b54e6f9a7b70d308800042" },
            { ClipPath("SwingSlam"), "3f08a1d2c4b54e6f9a7b70d308800043" }, { ClipPath("GuardIn"), "3f08a1d2c4b54e6f9a7b70d308800044" },
            { ClipPath("GuardHold"), "3f08a1d2c4b54e6f9a7b70d308800045" }, { ClipPath("BuffDraw"), "3f08a1d2c4b54e6f9a7b70d308800046" },
            { ClipPath("StanceIdle"), "3f08a1d2c4b54e6f9a7b70d308800047" }, { ClipPath("CarryIdle"), "3f08a1d2c4b54e6f9a7b70d308800048" },
            { ClipPath("ToCarry"), "3f08a1d2c4b54e6f9a7b70d308800049" }, { ClipPath("ToStance"), "3f08a1d2c4b54e6f9a7b70d30880004a" },
            { ClipPath("SwingThrow"), "3f08a1d2c4b54e6f9a7b70d30880004b" }, { ClipPath("SwingStrikeFar"), "3f08a1d2c4b54e6f9a7b70d30880004c" },
        };

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [Serializable]
        sealed class Record
        {
            public string utc, scene, oldWorldBrush, oldNearBrush, appearanceProfile, previousController;
            public bool animatorControllerSwitched, greatProfileCreated, swingProfileCreated, swingComponentAdded;
        }

        public static string Run(string command)
        {
            try
            {
                string c = (command ?? "").Trim();
                switch (c)
                {
                    case "status": return Status();
                    case "check": return Check();
                    case "import": return EditOnly() ?? Import();
                    case "materials": return EditOnly() ?? Materials();
                    case "controller": return EditOnly() ?? BuildController();
                    case "profile": return EditOnly() ?? ProfileCommand();
                    case "install": return EditOnly() ?? Install(false);
                    case "install:save": return EditOnly() ?? Install(true);
                    case "save": return EditOnly() ?? SaveInstalled();
                    case "sample": return EditOnly() ?? Sample();
                    case "nearleft": return EditOnly() ?? NearLeft();
                    case "handskin": return HandSkin();
                    case "tail": return EditOnly() ?? Tail();
                    case "nearhand": return EditOnly() ?? NearHand(false);
                    case "nearhand:revert": return EditOnly() ?? NearHand(true);
                    case "nearpreview": return EditOnly() ?? NearPreview("stroke");
                    case "nearpreview:hover": return EditOnly() ?? NearPreview("hover");
                    case "warnings": return ImportWarnings();
                    case "revert": return EditOnly() ?? Revert(false);
                    case "revert:save": return EditOnly() ?? Revert(true);
                }
                return "GreatBrush308Build: status | import | materials | controller | profile | install[:save] | revert[:save] | check";
            }
            catch (Exception e) { return "GreatBrush308Build exception: " + e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0].Trim(); }
        }

        static string EditOnly() => EditorApplication.isPlayingOrWillChangePlaymode ? "refused: edit mode only" : null;

        // ---------------------------------------------------------------- paths

        static string ClipPath(string name) => Folder + "/Animations/" + ClipPrefix + name + ".fbx";
        static string Abs(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string SourceBrush => Path.Combine(Repo, "Art", "Characters308", "GreatBrush308", "v6");
        static string SourceMotion => Path.Combine(Repo, "Art", "Characters308", "GreatBrush308", "motion2");
        static string GripJson => Path.Combine(SourceMotion, "grip.json");

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            if (Guids.TryGetValue(path, out string guid))
            {
                Directory.CreateDirectory(Abs(path));
                if (!File.Exists(Abs(path) + ".meta"))
                    File.WriteAllText(Abs(path) + ".meta", "fileFormatVersion: 2\nguid: " + guid + "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            else AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // Copies a source file to an asset path when it is new or different; the .meta (fixed guid) is written only when there is none.
        static string CopyIn(string source, string assetPath, string extraMeta = "")
        {
            if (!File.Exists(source)) return "MISSING";
            string target = Abs(assetPath);
            bool same = File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length && File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(source));
            if (!same) File.Copy(source, target, true);
            if (!File.Exists(target + ".meta") && Guids.TryGetValue(assetPath, out string guid))
                File.WriteAllText(target + ".meta", "fileFormatVersion: 2\nguid: " + guid + "\n" + extraMeta);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | (same ? ImportAssetOptions.Default : ImportAssetOptions.ForceUpdate));
            return same ? "same" : "copied";
        }

        static AnimationClip LoadClip(string name)
            => AssetDatabase.LoadAllAssetsAtPath(ClipPath(name)).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        static Avatar PlayerAvatar() => AssetDatabase.LoadAllAssetsAtPath(PlayerModel).OfType<Avatar>().FirstOrDefault();

        // ---------------------------------------------------------------- grip.json (a small reader: the file is a tree of objects, lists, numbers, strings)

        sealed class Reader
        {
            readonly string s; int i;
            public Reader(string text) { s = text; }
            public object Value()
            {
                Skip();
                if (i >= s.Length) throw new FormatException("unexpected end of json");
                char c = s[i];
                if (c == '{')
                {
                    var map = new Dictionary<string, object>(); i++; Skip();
                    if (s[i] == '}') { i++; return map; }
                    while (true)
                    {
                        Skip(); string key = Text(); Skip(); Expect(':'); map[key] = Value(); Skip();
                        if (s[i] == ',') { i++; continue; }
                        Expect('}'); return map;
                    }
                }
                if (c == '[')
                {
                    var list = new List<object>(); i++; Skip();
                    if (s[i] == ']') { i++; return list; }
                    while (true)
                    {
                        list.Add(Value()); Skip();
                        if (s[i] == ',') { i++; continue; }
                        Expect(']'); return list;
                    }
                }
                if (c == '"') return Text();
                if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
                if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
                if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (i == start) throw new FormatException("json: unexpected '" + c + "' at " + i);
                return double.Parse(s.Substring(start, i - start), Inv);
            }
            void Skip() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
            void Expect(char c) { if (i >= s.Length || s[i] != c) throw new FormatException("json: expected '" + c + "' at " + i); i++; }
            string Text()
            {
                Expect('"'); var b = new StringBuilder();
                while (s[i] != '"')
                {
                    if (s[i] == '\\')
                    {
                        char e = s[i + 1]; i += 2;
                        if (e == 'u') { b.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; }
                        else b.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e);
                    }
                    else b.Append(s[i++]);
                }
                i++; return b.ToString();
            }
        }

        static Dictionary<string, object> LoadGrip(out string error)
        {
            error = null;
            if (!File.Exists(GripJson)) { error = "grip.json missing (" + GripJson + ")"; return null; }
            try { return new Reader(File.ReadAllText(GripJson, Encoding.UTF8)).Value() as Dictionary<string, object>; }
            catch (Exception e) { error = "grip.json unreadable: " + e.Message; return null; }
        }

        static object At(object node, params string[] path)
        {
            foreach (string key in path)
            {
                if (!(node is Dictionary<string, object> map) || !map.TryGetValue(key, out node)) return null;
            }
            return node;
        }
        static float Number(object value, float fallback = 0f) => value is double d ? (float)d : fallback;
        // grip.json as exported by export_m2.py (2026-10-13); the second name of each pair is the earlier draft of the format
        static float ClipSecondsJson(object clip) => Number(At(clip, "length_s") ?? At(clip, "seconds"), -1f);
        static object RestBlock(object json) => At(json, "right_hand_to_brush_root", "rest_pose_model_space", "unity_model_space")
            ?? At(json, "right_hand_to_gripsocket", "model_space_at_rest", "unity");
        static object SpreadBlock(object json) => At(json, "right_hand_to_brush_root", "spread_over_two_hand_clips") ?? At(json, "right_hand_to_gripsocket", "spread_over_clips");
        static bool Vector(object value, out Vector3 v)
        {
            v = default;
            if (!(value is List<object> list) || list.Count != 3 || !list.All(x => x is double)) return false;
            v = new Vector3((float)(double)list[0], (float)(double)list[1], (float)(double)list[2]);
            return true;
        }

        // ---------------------------------------------------------------- status

        static string Status()
        {
            var b = new StringBuilder("GreatBrush308 status\n");
            b.AppendLine("source brush   " + (File.Exists(Path.Combine(SourceBrush, "SM_GreatBrush308.fbx")) ? "present" : "MISSING") + "  " + SourceBrush);
            var missing = ClipNames.Where(n => !File.Exists(Path.Combine(SourceMotion, "export", ClipPrefix + n + ".fbx"))).ToArray();
            b.AppendLine("source clips   " + (ClipNames.Length - missing.Length) + "/" + ClipNames.Length + (missing.Length > 0 ? "  MISSING " + string.Join(",", missing) : ""));
            b.AppendLine("grip.json      " + (File.Exists(GripJson) ? "present" : "MISSING"));
            b.AppendLine("brush model    " + (AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel) != null ? "imported" : "absent"));
            b.AppendLine("clips          " + ClipNames.Count(n => LoadClip(n) != null) + "/" + ClipNames.Length + " imported");
            b.AppendLine("materials      " + MaterialNames.Count(n => AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(n)) != null) + "/" + MaterialNames.Length);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            b.AppendLine("controller     " + (controller == null ? "absent" : "copy present, layers " + string.Join(" / ", controller.layers.Select(l => l.name))));
            b.AppendLine("profiles       great " + (File.Exists(Abs(GreatProfilePath)) ? "present" : "absent") + " · swing " + (File.Exists(Abs(SwingProfilePath)) ? "present" : "absent"));
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            if (gesture == null) b.AppendLine("scene          no WorldMacroPlayerGestureRig in the open scenes");
            else
            {
                Transform world = Field<Transform>(gesture, "_worldBrushRoot");
                b.AppendLine("scene          " + gesture.gameObject.scene.name + " · world brush " + (world != null ? world.name : "none") + " · installed " + (world != null && world.name == WorldName)
                    + " · SpellSwing308 " + (gesture.GetComponent<SpellSwing308>() != null) + " · great bound " + gesture.GreatBrushBound308);
            }
            b.Append("record         " + (File.Exists(Abs(RecordPath)) ? "present" : "none"));
            return b.ToString();
        }

        // ---------------------------------------------------------------- import

        static string Import()
        {
            var b = new StringBuilder("GreatBrush308 import\n");
            EnsureFolder(Folder + "/Models"); EnsureFolder(Folder + "/Animations"); EnsureFolder(Folder + "/Materials"); EnsureFolder(Folder + "/Textures");
            var grip = LoadGrip(out string gripError);
            if (grip == null) b.AppendLine("  note: " + gripError + " - loop flags fall back to the clip names");

            string brush = CopyIn(Path.Combine(SourceBrush, "SM_GreatBrush308.fbx"), BrushModel);
            b.AppendLine("  brush model    " + brush);
            b.AppendLine("  bristle map    " + CopyIn(Path.Combine(SourceBrush, "T_GreatBrush308_Bristle.png"), BristleTexture));
            if (brush != "MISSING")
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(BrushModel);
                mi.animationType = ModelImporterAnimationType.Generic; mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;   // bones as plain transforms, no humanoid
                mi.importAnimation = false; mi.importBlendShapes = true; mi.importCameras = false; mi.importLights = false; mi.importVisibility = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; mi.materialLocation = ModelImporterMaterialLocation.InPrefab;   // slot names survive; install replaces them
                mi.meshCompression = ModelImporterMeshCompression.Off; mi.optimizeGameObjects = false; mi.preserveHierarchy = true;
                mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = false; mi.skinWeights = ModelImporterSkinWeights.Standard;
                mi.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
                b.AppendLine("  brush import   " + (model == null ? "FAILED" : "tris " + (model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).Sum(s => s.sharedMesh.triangles.Length / 3)
                    + model.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => f.sharedMesh.triangles.Length / 3)) + " · transforms " + model.GetComponentsInChildren<Transform>(true).Length));
            }

            Avatar avatar = PlayerAvatar();
            if (avatar == null || !avatar.isValid || !avatar.isHuman) b.AppendLine("  AVATAR: no valid humanoid avatar in " + PlayerModel + " - clips are copied but not set up");
            foreach (string name in ClipNames)
            {
                string path = ClipPath(name);
                string copied = CopyIn(Path.Combine(SourceMotion, "export", ClipPrefix + name + ".fbx"), path);
                if (copied == "MISSING") { b.AppendLine("  " + name.PadRight(11) + " MISSING (not exported yet)"); continue; }
                if (avatar == null || !avatar.isValid || !avatar.isHuman) { b.AppendLine("  " + name.PadRight(11) + " " + copied + " · no avatar"); continue; }
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                // its own avatar, not a copy of the player's: with the copied avatar the root came out a hundred times too large (2026-10-13 sample:
                // hips 140 - 530 m up at the apex). Humanoid retargeting carries the motion to the player's avatar either way.
                // D308-84a (2026-10-14 sample): the avatar made from a file takes that file's default pose as its skeleton. The ten clips of
                // 10-13 were exported together and all got the stance; SwingThrow / SwingStrikeFar, exported alone, got another pose and
                // retargeted wrong (frame 0 not the stance, left hand 70 cm off). Those two copy StanceIdle's avatar (same file, same scale).
                Avatar stanceAvatar = name != "StanceIdle" && AvatarFromStance.Contains(name)
                    ? AssetDatabase.LoadAllAssetsAtPath(ClipPath("StanceIdle")).OfType<Avatar>().FirstOrDefault(a => a.isValid && a.isHuman) : null;
                mi.animationType = ModelImporterAnimationType.Human;
                if (stanceAvatar != null) { mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; mi.sourceAvatar = stanceAvatar; }
                else { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; mi.sourceAvatar = null; }
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = true; mi.importBlendShapes = false;
                mi.SaveAndReimport();
                var take = mi.defaultClipAnimations.FirstOrDefault();
                if (take == null) { b.AppendLine("  " + name.PadRight(11) + " " + copied + " · NO TAKE in the file"); continue; }
                object loopValue = At(grip, "clips", name, "loop");
                bool loop = loopValue is bool flag ? flag : name.EndsWith("Idle", StringComparison.Ordinal) || name == "GuardHold";
                take.name = ClipPrefix + name; take.loopTime = loop; take.loopPose = false;
                // as the #303 reaction clips on this character (Player303Build): facing and height stay in the pose (height from the
                // feet), the travel is taken out as root motion - the player's Animator discards it, so the body stays on the capsule
                // and fading back to locomotion never slides it home (memory: humanoid-clip-root-bake)
                take.lockRootRotation = true; take.keepOriginalOrientation = true;
                take.lockRootHeightY = true; take.keepOriginalPositionY = false; take.heightFromFeet = true;
                take.lockRootPositionXZ = false; take.keepOriginalPositionXZ = false;
                // D308-87 whole-body clips step, lunge and hop inside the clip while the character stays put: the body's horizontal travel
                // is part of the motion, not travel - keep it in the pose (from the file's still root), or the feet slide back under a body
                // that never left the capsule centre
                if (WholeBody.Contains(name)) { take.lockRootPositionXZ = true; take.keepOriginalPositionXZ = true; take.keepOriginalPositionY = true; take.heightFromFeet = false; }
                mi.clipAnimations = new[] { take }; mi.SaveAndReimport();
                var clip = LoadClip(name);
                float expected = ClipSecondsJson(At(grip, "clips", name));
                b.AppendLine("  " + name.PadRight(11) + " " + copied + (clip == null ? " · NO CLIP after import"
                    : " · " + clip.length.ToString("F3", Inv) + " s" + (expected >= 0f ? " (json " + expected.ToString("F3", Inv) + ")" : "") + " · loop " + loop + " · human " + clip.isHumanMotion));
            }
            b.Append("next: materials -> controller -> install");
            return b.ToString();
        }

        // ---------------------------------------------------------------- materials (temporary: the wet-ink shader waits for the look decision D308-78)

        static readonly string[] MaterialNames = { "Bamboo", "Brass", "Bristle", "Cord" };
        static string MaterialPath(string name) => Folder + "/Materials/M_GreatBrush308_" + name + ".mat";

        static string Materials()
        {
            Shader shader = Shader.Find(BrushShader);
            if (shader == null) return "refused: shader " + BrushShader + " not found";
            EnsureFolder(Folder + "/Materials");
            // TEST colours (sRGB): black bamboo, dull brass, ink-dark bristles, a dark red cord. Vertex colour A (wetness) is not read yet.
            var colours = new Dictionary<string, Color> { { "Bamboo", new Color(.11f, .10f, .09f) }, { "Brass", new Color(.55f, .43f, .22f) }, { "Bristle", new Color(.92f, .92f, .92f) }, { "Cord", new Color(.36f, .09f, .08f) } };
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BristleTexture);
            var b = new StringBuilder("GreatBrush308 materials (temporary, " + BrushShader + ")\n");
            if (texture == null && File.Exists(Abs(BristleTexture)))
            {
                // seen 2026-10-13: the bare meta came in as a Cubemap (a default preset), so the map never loaded as a Texture2D
                if (AssetImporter.GetAtPath(BristleTexture) is TextureImporter ti)
                {
                    ti.textureType = TextureImporterType.Default; ti.textureShape = TextureImporterShape.Texture2D; ti.sRGBTexture = true;
                    ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Repeat; ti.SaveAndReimport();
                }
                else AssetDatabase.ImportAsset(BristleTexture, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                texture =AssetDatabase.LoadAssetAtPath<Texture2D>(BristleTexture);
                b.AppendLine("  bristle map reimported: " + (texture != null ? "loaded" : "still not loadable, main type " + AssetDatabase.GetMainAssetTypeAtPath(BristleTexture) + " importer " + AssetImporter.GetAtPath(BristleTexture)));
            }
            foreach (string name in MaterialNames)
            {
                string path = MaterialPath(name);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool made = material == null;
                if (made) material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                material.shader = shader;
                material.SetColor("_BaseColor", colours[name]);
                material.SetTexture("_BaseMap", name == "Bristle" ? texture : null);
                material.SetFloat("_Saturation", .78f); material.SetFloat("_AmbientFloor", .48f); material.SetFloat("_LightResponse", .75f);
                material.SetFloat("_Cull", name == "Bristle" ? 0f : 2f);   // strand cards are seen from both sides
                material.shaderKeywords = Array.Empty<string>(); material.renderQueue = -1; material.enableInstancing = true;
                if (made) AssetDatabase.CreateAsset(material, path); else EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                b.AppendLine("  " + Path.GetFileName(path) + (made ? " created" : " updated") + (name == "Bristle" ? " · map " + (texture != null ? "set" : "MISSING") : ""));
            }
            return b.ToString().TrimEnd();
        }

        static Material[] MaterialsFor(Renderer renderer)
        {
            var slots = renderer.sharedMaterials;
            var result = new Material[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                string slot = slots[i] != null ? slots[i].name : "";
                string pick = MaterialNames.FirstOrDefault(n => slot.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? (renderer.name.IndexOf("Bristle", StringComparison.OrdinalIgnoreCase) >= 0 ? "Bristle" : "Bamboo");
                result[i] = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(pick));
            }
            return result;
        }

        // ---------------------------------------------------------------- controller (a copy; the live controller file is only read)

        static GreatBrush308Profile ProfileOrDefaults(out bool temporary)
        {
            var profile = AssetDatabase.LoadAssetAtPath<GreatBrush308Profile>(GreatProfilePath);
            temporary = profile == null;
            return temporary ? ScriptableObject.CreateInstance<GreatBrush308Profile>() : profile;
        }

        static string BuildController()
        {
            if (!File.Exists(Abs(LiveController))) return "refused: live controller missing " + LiveController;
            EnsureFolder(Folder);
            var names = ProfileOrDefaults(out bool temporary);
            try
            {
                // byte copy of the live file onto the copy's path: the copy keeps its own guid (.meta) and takes the live file's content,
                // so a later change of the live controller is picked up by running this command again
                File.Copy(Abs(LiveController), Abs(Controller), true);
                if (!File.Exists(Abs(Controller) + ".meta"))
                    File.WriteAllText(Abs(Controller) + ".meta", "fileFormatVersion: 2\nguid: " + Guids[Controller] + "\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 9100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                AssetDatabase.ImportAsset(Controller, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
                if (controller == null) return "FAILED: the controller copy did not import";
                controller.name = Path.GetFileNameWithoutExtension(Controller);
                for (int i = controller.layers.Length - 1; i >= 0; i--)
                    if (controller.layers[i].name == names.Layer || controller.layers[i].name == names.BodyLayer || controller.layers[i].name == TorsoLayerName || controller.layers[i].name == LeftLayerName) controller.RemoveLayer(i);
                foreach (string parameter in new[] { names.TimeParameter, names.ShiftParameter })
                    if (!string.IsNullOrEmpty(parameter) && controller.parameters.All(p => p.name != parameter)) controller.AddParameter(parameter, AnimatorControllerParameterType.Float);

                var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
                bool maskMade = mask == null;
                if (maskMade) mask = new AvatarMask { name = Path.GetFileNameWithoutExtension(MaskPath) };
                // waist up: spine, head, both arms and hands. The hips (root), the legs and the IK goals stay with locomotion.
                for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                {
                    var p = (AvatarMaskBodyPart)part;
                    // 2026-10-13: arms and hands only - the spine and the head went to their own layer (Brush308Torso), which the
                    // swing driver turns down while the player walks so the walk's own sway shows
                    mask.SetHumanoidBodyPartActive(p, p == AvatarMaskBodyPart.RightArm || p == AvatarMaskBodyPart.RightFingers);   // the left arm has its own layer (Brush308Left)
                }
                if (maskMade) AssetDatabase.CreateAsset(mask, MaskPath); else EditorUtility.SetDirty(mask);
                AssetDatabase.SaveAssetIfDirty(mask);
                var torsoMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(TorsoMaskPath);
                bool torsoMade = torsoMask == null;
                if (torsoMade) torsoMask = new AvatarMask { name = Path.GetFileNameWithoutExtension(TorsoMaskPath) };
                for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                {
                    var p = (AvatarMaskBodyPart)part;
                    torsoMask.SetHumanoidBodyPartActive(p, p == AvatarMaskBodyPart.Body || p == AvatarMaskBodyPart.Head);
                }
                if (torsoMade) AssetDatabase.CreateAsset(torsoMask, TorsoMaskPath); else EditorUtility.SetDirty(torsoMask);
                AssetDatabase.SaveAssetIfDirty(torsoMask);
                var leftMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(LeftMaskPath);
                bool leftMade = leftMask == null;
                if (leftMade) leftMask = new AvatarMask { name = Path.GetFileNameWithoutExtension(LeftMaskPath) };
                for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                {
                    var p = (AvatarMaskBodyPart)part;
                    leftMask.SetHumanoidBodyPartActive(p, p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.LeftFingers);
                }
                if (leftMade) AssetDatabase.CreateAsset(leftMask, LeftMaskPath); else EditorUtility.SetDirty(leftMask);
                AssetDatabase.SaveAssetIfDirty(leftMask);

                var missing = new List<string>();
                AnimatorStateMachine Machine(string layer, string[] states)
                {
                    var sm = new AnimatorStateMachine { name = layer, hideFlags = HideFlags.HideInHierarchy };
                    AssetDatabase.AddObjectToAsset(sm, controller);
                    var empty = sm.AddState("Empty", new Vector3(260, -60, 0)); empty.writeDefaultValues = false; sm.defaultState = empty;
                    for (int i = 0; i < states.Length; i++)
                    {
                        var state = sm.AddState(states[i], new Vector3(260, 60 * i, 0));
                        state.motion = LoadClip(states[i]); state.writeDefaultValues = false;
                        state.speed = ClipSpeed308(states[i], state.motion as AnimationClip);   // the exported FBX reads 1.25x long in Unity (frame-rate interpretation): play it at its authored length
                        if (state.motion == null && !missing.Contains(states[i])) missing.Add(states[i]);
                        // one-shot motions are placed by a normalized-time parameter: SpellSwing308 puts the clip's apex on the rule's apex
                        string timed = TimedStates.Contains(states[i]) ? names.TimeParameter : ShiftStates.Contains(states[i]) ? names.ShiftParameter : null;
                        if (!string.IsNullOrEmpty(timed)) { state.timeParameterActive = true; state.timeParameter = timed; }
                    }
                    return sm;
                }
                // upper body: both idle stances and every motion. Whole body: the motions only, weighted in while the player stands.
                controller.AddLayer(new AnimatorControllerLayer { name = names.Layer, stateMachine = Machine(names.Layer, ClipNames), avatarMask = mask, defaultWeight = 0f, blendingMode = AnimatorLayerBlendingMode.Override, iKPass = false });
                controller.AddLayer(new AnimatorControllerLayer { name = TorsoLayerName, stateMachine = Machine(TorsoLayerName, ClipNames), avatarMask = torsoMask, defaultWeight = 0f, blendingMode = AnimatorLayerBlendingMode.Override, iKPass = false });
                controller.AddLayer(new AnimatorControllerLayer { name = LeftLayerName, stateMachine = Machine(LeftLayerName, ClipNames), avatarMask = leftMask, defaultWeight = 0f, blendingMode = AnimatorLayerBlendingMode.Override, iKPass = false });
                bool body = !string.IsNullOrEmpty(names.BodyLayer);
                if (body) controller.AddLayer(new AnimatorControllerLayer { name = names.BodyLayer, stateMachine = Machine(names.BodyLayer, BodyStates), defaultWeight = 0f, blendingMode = AnimatorLayerBlendingMode.Override, iKPass = false });
                // right above locomotion, below the hit reactions and the ride: those keep overriding the brush layers
                var layers = controller.layers.ToList();
                var added = layers.Where(l => l.name == names.Layer || l.name == TorsoLayerName || l.name == LeftLayerName || l.name == names.BodyLayer).ToList();
                layers.RemoveAll(l => added.Contains(l));
                layers.InsertRange(Mathf.Min(1, layers.Count), added);
                controller.layers = layers.ToArray();
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                return "GreatBrush308 controller: " + Controller + "\n  layers " + string.Join(" / ", controller.layers.Select(l => l.name + (l.avatarMask != null ? "(mask)" : "")))
                    + "\n  states " + ClipNames.Length + " + Empty on " + names.Layer + (body ? ", " + BodyStates.Length + " + Empty on " + names.BodyLayer : "")
                    + "\n  parameters " + names.TimeParameter + ", " + names.ShiftParameter
                    + (missing.Count > 0 ? "\n  STATES WITHOUT A CLIP: " + string.Join(", ", missing) + " (import first, then run controller again)" : "")
                    + "\n  the live controller was not changed";
            }
            finally { if (temporary) Object.DestroyImmediate(names); }
        }

        // ---------------------------------------------------------------- profile (grip + states computed from grip.json and the imported assets)

        // modelFromBone: the bone's bind pose in the model's root space, from the skinned mesh's bind poses (independent of the asset's current pose)
        static bool BoneBind(GameObject model, string bone, out Matrix4x4 modelFromBone)
        {
            modelFromBone = Matrix4x4.identity;
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                int index = Array.FindIndex(skin.bones, t => t != null && t.name == bone);
                if (index < 0 || index >= skin.sharedMesh.bindposes.Length) continue;
                modelFromBone = model.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix * skin.sharedMesh.bindposes[index].inverse;
                return true;
            }
            return false;
        }

        static Quaternion Rotation(Matrix4x4 m) => Quaternion.LookRotation(((Vector3)m.GetColumn(2)).normalized, ((Vector3)m.GetColumn(1)).normalized);

        // The right hand's grip (GripSocket in the hand bone's local space) and the left hand's hold point, from grip.json's
        // rest-pose description in model space. The brush's own axes are measured from the model (where its ring chains sit),
        // so no import axis convention of the brush is assumed; the player's side uses the mesh bind poses.
        static bool ComputeGrip(GreatBrush308Profile profile, StringBuilder log, out GreatBrush308Profile.Grip grip, out Vector3 leftHold)
        {
            grip = default; leftHold = default;
            var json = LoadGrip(out string error);
            if (json == null) { log.AppendLine("  grip: " + error); return false; }
            object rest = RestBlock(json);
            if (!Vector(At(rest, "right_hand_joint"), out Vector3 joint) || !Vector(At(rest, "brush_root_pos") ?? At(rest, "gripsocket_pos"), out Vector3 socket)
                || !Vector(At(rest, "brush_tip_dir") ?? At(rest, "tip_dir"), out Vector3 tip) || !Vector(At(rest, "brush_blenderY_dir") ?? At(rest, "palm_dir"), out Vector3 palm))
            { log.AppendLine("  grip: grip.json has no right_hand_to_brush_root.rest_pose_model_space.unity_model_space block (right_hand_joint, brush_root_pos, brush_tip_dir, brush_blenderY_dir)"); return false; }
            float behind = Number(At(json, "brush", "right_hand_hold_behind_gripsocket_m"), profile.RightHandBehindGripSocket);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModel);
            var brush = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            if (player == null || brush == null) { log.AppendLine("  grip: player model or brush model not imported"); return false; }
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            string rightName = gesture != null && gesture.Profile != null ? gesture.Profile.RightHandName : "RightHand";
            string leftName = gesture != null && gesture.Profile != null ? gesture.Profile.LeftHandName : "LeftHand";
            if (!BoneBind(player, rightName, out Matrix4x4 right) || !BoneBind(player, leftName, out Matrix4x4 left))
            { log.AppendLine("  grip: hand bones " + rightName + " / " + leftName + " are not skinned bones of " + PlayerModel); return false; }

            // the brush's own frame, measured on a hidden copy
            GameObject copy = Object.Instantiate(brush); copy.hideFlags = HideFlags.HideAndDontSave;
            Vector3 shaft, across, side, socketInRoot; Quaternion socketRotation;
            try
            {
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var bristles = copy.AddComponent<GreatBrushBristles308>();
                if (!bristles.IsBound && !bristles.TryBind()) { log.AppendLine("  grip: brush does not bind - " + bristles.BindingError); return false; }
                shaft = bristles.ShaftAxisInRoot; across = bristles.AxisYInRoot; side = bristles.AxisXInRoot; socketInRoot = bristles.GripPositionInRoot; socketRotation = bristles.GripRotationInRoot;
                if (bristles.BoundRingChains < 2) log.AppendLine("  grip: WARNING ring chains A/B not found - the roll about the shaft is a guess");
            }
            finally { Object.DestroyImmediate(copy); }

            tip.Normalize(); palm = Vector3.ProjectOnPlane(palm, tip).normalized;
            if (tip.sqrMagnitude < .5f || palm.sqrMagnitude < .5f) { log.AppendLine("  grip: tip_dir / palm_dir in grip.json are not usable"); return false; }
            // brush root frame -> player model space: the shaft onto tip_dir, the brush's own press axis (Blender +Y, the side of ring B) onto palm_dir
            Quaternion modelFromBrush = Quaternion.LookRotation(palm, tip) * Quaternion.Inverse(Quaternion.LookRotation(across, shaft));
            Quaternion socketInModel = modelFromBrush * socketRotation;
            Quaternion rightRotation = Rotation(right);
            grip = new GreatBrush308Profile.Grip { Position = right.inverse.MultiplyPoint3x4(socket), Rotation = Quaternion.Inverse(rightRotation) * socketInModel };
            // the left hand: where the right hand holds the shaft, mirrored across the body's mid-plane (the rest pose is symmetric)
            profile.RightHandBehindGripSocket = behind;
            Vector3 rightHold = socket - tip * behind;
            leftHold = left.inverse.MultiplyPoint3x4(new Vector3(-rightHold.x, rightHold.y, rightHold.z));
            float jointError = Vector3.Distance(right.GetColumn(3), joint);
            // the third axis is not used to build the grip, so it checks it: the brush's ring A side must land on grip.json's Blender +X image
            if (Vector(At(rest, "brush_blenderX_dir"), out Vector3 expectedSide))
            {
                float dot = Vector3.Dot((modelFromBrush * side).normalized, expectedSide.normalized);
                log.AppendLine("  grip: brush X axis (ring A side) vs grip.json " + dot.ToString("F4", Inv) + (dot < .99f ? "  WARNING: expected 1 - the brush sits rolled or mirrored about its shaft" : ""));
            }
            if (Vector(At(json, "right_hand_to_brush_root", "unity_by_formula", "position_m"), out Vector3 formula))
            {
                float bone = ((Vector3)right.GetColumn(1)).magnitude;   // the hand bone's bind scale: grip.json's value is in metres
                log.AppendLine("  grip: socket in hand (metres) " + (grip.Position * bone).ToString("F4") + " · grip.json by formula " + formula.ToString("F4")
                    + " · apart " + (Vector3.Distance(grip.Position * bone, formula) * 1000f).ToString("F1", Inv) + " mm (a large value means the bone's local axes differ from the formula's convention, not that the grip is wrong)");
            }
            log.AppendLine("  grip: hand joint bind vs grip.json " + (jointError * 1000f).ToString("F1", Inv) + " mm" + (jointError > .01f ? "  WARNING > 10 mm: the model-space mapping (-x, z, -y) or the rest pose does not match" : "")
                + " · socket in hand " + grip.Position.ToString("F4") + " · left hold " + leftHold.ToString("F4")
                + " · right hand holds " + behind.ToString("F2", Inv) + " m behind GripSocket"
                + " · spread over clips " + Number(At(SpreadBlock(json), "position_mm")).ToString("F2", Inv) + " mm / " + Number(At(SpreadBlock(json), "rotation_deg")).ToString("F2", Inv) + " deg");
            return true;
        }

        static GreatBrush308Profile.StateEntry[] BuildStates(Dictionary<string, object> json, StringBuilder log)
        {
            float fps = Mathf.Max(1f, Number(At(json, "fps"), 30f));
            var states = new List<GreatBrush308Profile.StateEntry>();
            foreach (string name in ClipNames)
            {
                var clip = LoadClip(name);
                object info = At(json, "clips", name);
                var entry = new GreatBrush308Profile.StateEntry
                {
                    State = name, ClipSeconds = clip != null ? clip.length / ClipSpeed308(name, clip) : 0f, ClipApexSeconds = Number(At(info, "apex_s")),
                    Loop = At(info, "loop") is bool loop ? loop : clip != null && clip.isLooping, LeftHand = Array.Empty<Vector2>()
                };
                if (name == "GuardIn") entry.Next = "GuardHold";
                if (name == "GuardHold") entry.HoldSeconds = .35f;   // TEST: the block is held at least this long (the lock may be shorter: parry 0.20 s)
                if (At(info, "left_hand_on") is List<object> on && on.Count > 0)
                {
                    var keys = new List<Vector2>(); float last = -1f;
                    for (int f = 0; f < on.Count; f++)
                    {
                        // [time, 0 | 1] keys (the exported format); a plain per-frame 0 | 1 list is read as well
                        bool pair = on[f] is List<object> key && key.Count == 2;
                        float time = pair ? Number(((List<object>)on[f])[0]) : f / fps;
                        float value = Number(pair ? ((List<object>)on[f])[1] : on[f]) >= .5f ? 1f : 0f;
                        if (value != last) { keys.Add(new Vector2(time, value)); last = value; }
                    }
                    entry.LeftHand = keys.ToArray();
                }
                else if (name != "CarryIdle") entry.LeftHand = new[] { new Vector2(0f, name == "ToCarry" || name == "SwingArc" ? 0f : 1f) };   // no json: the clip's known shape
                if (clip == null) log.AppendLine("  state " + name + ": no clip imported (the state will not be played)");
                states.Add(entry);
            }
            return states.ToArray();
        }

        // Writes the computed parts. Tuned values (bristles, timings, near placement) are left alone on an existing asset.
        static GreatBrush308Profile WriteProfile(bool createIfMissing, WorldMacroPlayerGestureRig gesture, StringBuilder log, out bool created)
        {
            created = false;
            var profile = AssetDatabase.LoadAssetAtPath<GreatBrush308Profile>(GreatProfilePath);
            if (profile == null && !createIfMissing) { log.AppendLine("  profile: " + GreatProfilePath + " does not exist (install creates it)"); return null; }
            var json = LoadGrip(out string error);
            if (json == null) { log.AppendLine("  profile: " + error); return null; }
            var work = profile != null ? profile : ScriptableObject.CreateInstance<GreatBrush308Profile>();
            if (!ComputeGrip(work, log, out var grip, out Vector3 leftHold))
            {
                if (profile == null) Object.DestroyImmediate(work);
                return null;
            }
            work.BrushPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            work.TwoHandGrip = grip; work.CarryGrip = grip;   // grip.json: one transform for every clip, the shoulder carry included
            { var stanceGrip = grip; var carryGrip = grip; var leftGrip = work.LeftTwoHandGrip; if (TruthGrips(ref stanceGrip, ref carryGrip, ref leftGrip, log)) { work.TwoHandGrip = stanceGrip; work.CarryGrip = carryGrip; work.LeftTwoHandGrip = leftGrip; } }   // solved from the Blender truth when it is there
            work.LeftHandHoldLocal = leftHold;
            work.States = BuildStates(json, log);
            if (profile == null)
            {
                // close-up numbers start from the values the 0.9 m brush uses now (they are tuned here from then on, not there)
                var live = gesture != null ? gesture.Profile : null;
                if (live != null)
                {
                    work.NearTipDepth = live.NearTipDepth; work.NearMinimumDepth = live.NearMinimumDepth; work.NearMaximumDepth = live.NearMaximumDepth;
                    work.NearShoulderOffset = live.NearShoulderOffset; work.NearRaiseSeconds = live.NearRaiseSeconds; work.NearRaiseOffset = live.NearRaiseOffset;
                }
                EnsureFolder(ResourcesFolder);
                AssetDatabase.CreateAsset(work, GreatProfilePath);
                created = true;
            }
            else EditorUtility.SetDirty(work);
            AssetDatabase.SaveAssetIfDirty(work);
            log.AppendLine("  profile: " + (created ? "created " : "updated ") + GreatProfilePath + " · states " + work.States.Count(s => s.ClipSeconds > 0f) + "/" + work.States.Length + " with a clip · valid " + work.IsValid);
            return work;
        }

        // saves the scene an earlier plain "install" left dirty (the install itself is not repeated)
        static string SaveInstalled()
        {
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            if (gesture == null || Child(gesture.transform.root, WorldName) == null) return "refused: not installed";
            var scene = gesture.gameObject.scene;
            return "saved scene " + scene.path + " (dirty " + scene.isDirty + "): " + EditorSceneManager.SaveScene(scene);
        }

        // Edit-mode look at the clips on the player's own model (no Animator layers, no rig): every clip sampled at start / apex / end
        // on a hidden copy with the brush held by the profile grip, numbers plus one still each (Art/Characters308/GreatBrush308/unity_sample).
        static string Sample()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModel); var brushModel = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            var profile = AssetDatabase.LoadAssetAtPath<GreatBrush308Profile>(GreatProfilePath);
            if (model == null || brushModel == null || profile == null) return "refused: player model / brush model / profile missing";
            string folder = Path.Combine(Repo, "Art", "Characters308", "GreatBrush308", "unity_sample"); Directory.CreateDirectory(folder);
            var b = new StringBuilder("GreatBrush308 sample (clip alone on the player model)\n");
            Vector3 at = new Vector3(0f, 400f, 0f);
            var scenePlayer = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>(); if (scenePlayer != null) at = scenePlayer.transform.position + Vector3.up * 60f;
            GameObject copy = Object.Instantiate(model, at, Quaternion.identity); copy.hideFlags = HideFlags.HideAndDontSave;
            GameObject brush = Object.Instantiate(brushModel); brush.hideFlags = HideFlags.HideAndDontSave;
            if (scenePlayer != null)   // the model's own import materials are blank: wear what the scene player wears
                foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    var twin = scenePlayer.GetComponentsInChildren<Renderer>(true).FirstOrDefault(x => x.name == r.name && x.sharedMaterials.Length == r.sharedMaterials.Length);
                    if (twin != null) r.sharedMaterials = twin.sharedMaterials;
                }
            foreach (var r in brush.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) { string slot = MaterialNames.FirstOrDefault(n => mats[i] != null && mats[i].name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0); if (slot != null) mats[i] = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(slot)); }
                r.sharedMaterials = mats;
            }
            var camObject = new GameObject("GreatBrush308 sample camera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(640, 640, 24); var tex = new Texture2D(640, 640, TextureFormat.RGB24, false);
            try
            {
                var animator = copy.GetComponent<Animator>(); if (animator == null) animator = copy.AddComponent<Animator>();
                animator.avatar = PlayerAvatar();
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips), rh = animator.GetBoneTransform(HumanBodyBones.RightHand), lh = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot), rf = animator.GetBoneTransform(HumanBodyBones.RightFoot), head = animator.GetBoneTransform(HumanBodyBones.Head);
                Transform socket = Child(brush.transform, "GripSocket"), socketL = Child(brush.transform, "GripSocketL"), tip = Child(brush.transform, "TipSocket");
                if (hips == null || rh == null || lh == null || socket == null) return "refused: bones / sockets not found on the copies";
                b.AppendLine("  rest: hips y " + (hips.position.y - at.y).ToString("F3", Inv) + " · feet y " + (Mathf.Min(lf.position.y, rf.position.y) - at.y).ToString("F3", Inv) + " · copy scale " + copy.transform.lossyScale.ToString("F3"));
                var cam = camObject.AddComponent<Camera>(); cam.targetTexture = rt; cam.fieldOfView = 30f; cam.nearClipPlane = .1f; cam.farClipPlane = 60f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.78f, .74f, .66f);
                foreach (string name in ClipNames)
                {
                    var clip = LoadClip(name); int index = profile.Find(name); if (clip == null) { b.AppendLine("  " + name + " no clip"); continue; }
                    float speed = ClipSpeed308(name, clip); float apex = index >= 0 ? profile.States[index].ClipApexSeconds * speed : 0f;
                    float[] times = { 0f, apex > 0f ? apex : clip.length * .5f, clip.length };
                    string[] tags = { "start", apex > 0f ? "apex" : "mid", "end" };
                    for (int k = 0; k < 3; k++)
                    {
                        copy.transform.SetPositionAndRotation(at, Quaternion.identity);
                        clip.SampleAnimation(copy, Mathf.Min(times[k], clip.length));
                        bool carry = name == "CarryIdle";
                        var grip = carry ? profile.CarryGrip : profile.TwoHandGrip;
                        Quaternion socketWorld = rh.rotation * grip.Rotation; Vector3 socketPos = rh.TransformPoint(grip.Position);
                        Quaternion socketInBrush = Quaternion.Inverse(brush.transform.rotation) * socket.rotation; Vector3 socketInBrushPos = brush.transform.InverseTransformPoint(socket.position);
                        brush.transform.rotation = socketWorld * Quaternion.Inverse(socketInBrush);
                        brush.transform.position = socketPos - brush.transform.rotation * socketInBrushPos;
                        Vector3 root = copy.transform.position; Quaternion inv = Quaternion.Inverse(copy.transform.rotation);
                        b.AppendLine("  " + name.PadRight(11) + tags[k].PadRight(6) + (times[k] / speed).ToString("F2", Inv) + " s · root moved " + (inv * (root - at)).ToString("F2") + " · hips " + (inv * (hips.position - root)).ToString("F2") +
                            " · feet y " + (Mathf.Min(lf.position.y, rf.position.y) - root.y).ToString("F2", Inv) + " · hips facing " + Vector3.Dot(hips.forward, copy.transform.forward).ToString("F2", Inv) + " head " + (head != null ? Vector3.Dot(head.forward, copy.transform.forward).ToString("F2", Inv) : "?") +
                            " · left hand to L socket " + (socketL != null ? ((lh.TransformPoint(profile.LeftHandHoldLocal) - socketL.position).magnitude * 100f).ToString("F1", Inv) + " cm" : "?") +
                            " · tip " + (tip != null ? (inv * (tip.position - root)).ToString("F2") : "?"));
                        Vector3 centre = root + Vector3.up * .95f;
                        cam.transform.position = centre + copy.transform.rotation * new Vector3(2.6f, .5f, 4.2f); cam.transform.LookAt(centre);
                        cam.Render();
                        var old = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); tex.Apply(); RenderTexture.active = old;
                        File.WriteAllBytes(Path.Combine(folder, name + "_" + k + "_" + tags[k] + ".png"), tex.EncodeToPNG());
                    }
                }
                b.AppendLine("  stills: " + folder + " (camera in front-right of the player, 3/4 view)");
            }
            finally
            {
                Object.DestroyImmediate(copy); Object.DestroyImmediate(brush); Object.DestroyImmediate(camObject); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            return b.ToString();
        }

        static string ImportWarnings()
        {
            var b = new StringBuilder("GreatBrush308 import messages\n");
            foreach (string name in ClipNames)
            {
                var mi = AssetImporter.GetAtPath(ClipPath(name)) as ModelImporter; if (mi == null) continue;
                var so = new SerializedObject(mi);
                foreach (string field in new[] { "m_RigImportErrors", "m_RigImportWarnings", "m_AnimationImportErrors", "m_AnimationImportWarnings", "m_AnimationRetargetingWarnings", "m_AnimationDoRetargetingWarnings" })
                {
                    var prop = so.FindProperty(field); if (prop == null) continue;
                    string text = prop.propertyType == SerializedPropertyType.String ? prop.stringValue : prop.propertyType == SerializedPropertyType.Boolean ? prop.boolValue.ToString() : "?";
                    if (!string.IsNullOrEmpty(text) && text != "False") b.AppendLine("  " + name + " " + field + ": " + (text.Length > 700 ? text.Substring(0, 700) + "..." : text));
                }
                var clip = LoadClip(name);
                if (clip != null) b.AppendLine("  " + name + " curves " + AnimationUtility.GetCurveBindings(clip).Length + " · muscle sample: " + string.Join(", ", AnimationUtility.GetCurveBindings(clip).Where(c => c.propertyName.StartsWith("Right Arm", StringComparison.Ordinal) || c.propertyName.StartsWith("Right Forearm", StringComparison.Ordinal)).Select(c => { var cv = AnimationUtility.GetEditorCurve(clip, c); float lo = cv.keys.Min(k => k.value), hi = cv.keys.Max(k => k.value); return c.propertyName + " " + lo.ToString("F2", Inv) + ".." + hi.ToString("F2", Inv); })));
            }
            return b.ToString();
        }

        // ---------------------------------------------------------------- grip from the Blender truth (2026-10-13)
        // grip.json's hand-to-brush rotation was converted by formula and never checked in the editor: in Play the carry put the
        // bristle head 0.7 m to the left of the head instead of behind it. brush_truth.json (export of the solved .blend) has the
        // brush sockets and the joints per frame in Unity character space; the grip is solved here from what the retargeted hand
        // really does in Unity: grip = inverse(hand) x (socket pose the truth asks for).
        static string TruthJson => Path.Combine(SourceMotion, "metrics", "brush_truth.json");

        static Vector3 TruthPoint(object truth, string clip, string point, int frame)
        {
            var list = At(truth, "clips", clip, "points", point, "unity") as List<object>;
            if (list == null || list.Count == 0) return new Vector3(float.NaN, 0f, 0f);
            var p = list[Mathf.Clamp(frame, 0, list.Count - 1)] as List<object>;
            return new Vector3(Number(p[0]), Number(p[1]), Number(p[2]));
        }

        // the grip that puts the brush where the truth has it at this frame of this clip, on the given sampled copy
        static bool TruthGrip(object truth, string name, int frame, GameObject copy, Animator animator, Quaternion socketInRoot, out GreatBrush308Profile.Grip grip, out float wristError, bool left = false)
        {
            grip = new GreatBrush308Profile.Grip { Rotation = Quaternion.identity }; wristError = -1f;
            var clip = LoadClip(name); if (clip == null) return false;
            float speed = ClipSpeed308(name, clip);
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clip.SampleAnimation(copy, Mathf.Min(frame / 30f * speed, clip.length));
            Transform hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand); Transform root = copy.transform;
            Vector3 socket = TruthPoint(truth, name, "GripSocket", frame), end = TruthPoint(truth, name, "BrushRootZ10cm", frame), ring = TruthPoint(truth, name, "RingASide10cm", frame), wrist = TruthPoint(truth, name, left ? "LeftHand" : "RightHand", frame);
            Vector3 place = left ? TruthPoint(truth, name, "GripSocketL", frame) : socket;   // the socket this hand holds (the brush frame is measured at GripSocket either way)
            if (float.IsNaN(socket.x) || float.IsNaN(end.x) || float.IsNaN(ring.x)) return false;
            Vector3 y = (end - socket).normalized, r = Vector3.ProjectOnPlane(ring - socket, y).normalized;   // shaft toward the tip; ring A side = brush root -X
            Vector3 x = -r, z = Vector3.Cross(x, y);
            Quaternion brushRoot = Quaternion.LookRotation(z, y);
            // the offset is taken from the wrist (Blender's wrist -> socket vector on Unity's wrist): humanoid retargeting shifts the whole
            // body by some centimetres, and an absolute socket position would put that shift between the hand and the shaft
            Quaternion socketWorld = root.rotation * brushRoot * socketInRoot; Vector3 socketPosition = hand.position + root.rotation * (place - wrist);
            grip = new GreatBrush308Profile.Grip { Position = hand.InverseTransformPoint(socketPosition), Rotation = Quaternion.Inverse(hand.rotation) * socketWorld };
            wristError = Vector3.Distance(root.InverseTransformPoint(hand.position), wrist);
            return true;
        }

        // fills the two grips from the truth; false (and nothing changed) when the truth file or the models are missing
        static bool TruthGrips(ref GreatBrush308Profile.Grip stance, ref GreatBrush308Profile.Grip carry, ref GreatBrush308Profile.Grip leftStance, StringBuilder log)
        {
            if (!File.Exists(TruthJson)) { log.AppendLine("  truth: no brush_truth.json - the grip stays the grip.json formula"); return false; }
            object truth; try { truth = new Reader(File.ReadAllText(TruthJson, Encoding.UTF8)).Value(); } catch (Exception e) { log.AppendLine("  truth: unreadable " + e.Message); return false; }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModel); var brushModel = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            if (model == null || brushModel == null) return false;
            GameObject copy = Object.Instantiate(model); copy.hideFlags = HideFlags.HideAndDontSave;
            GameObject brush = Object.Instantiate(brushModel); brush.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = copy.GetComponent<Animator>(); if (animator == null) animator = copy.AddComponent<Animator>();
                animator.avatar = PlayerAvatar();
                brush.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Transform socket = Child(brush.transform, "GripSocket"); if (socket == null || animator.GetBoneTransform(HumanBodyBones.RightHand) == null) return false;
                Quaternion socketInRoot = socket.rotation;
                GreatBrush308Profile.Grip s, c;
                if (!TruthGrip(truth, "StanceIdle", 0, copy, animator, socketInRoot, out s, out float ws) || !TruthGrip(truth, "CarryIdle", 0, copy, animator, socketInRoot, out c, out float wc)) { log.AppendLine("  truth: stance / carry frame could not be solved"); return false; }
                log.AppendLine("  truth: stance grip moved " + (Vector3.Distance(s.Position, stance.Position) * 1000f).ToString("F0", Inv) + " mm / " + Quaternion.Angle(s.Rotation, stance.Rotation).ToString("F1", Inv) + " deg from the formula · carry grip differs from the stance grip by "
                    + (Vector3.Distance(s.Position, c.Position) * 1000f).ToString("F0", Inv) + " mm / " + Quaternion.Angle(s.Rotation, c.Rotation).ToString("F1", Inv) + " deg · wrist vs truth " + (ws * 100f).ToString("F1", Inv) + " / " + (wc * 100f).ToString("F1", Inv) + " cm");
                // how well one stance grip serves the other clips (what the retargeted hand does differently from Blender's)
                foreach (string name in ClipNames)
                {
                    if (name == "CarryIdle" || name == "ToCarry" || name == "ToStance") continue;
                    var list = At(truth, "clips", name, "points", "GripSocket", "unity") as List<object>; int frames = list != null ? list.Count : 0; float worst = 0f, worstMm = 0f, wrist = 0f;
                    for (int f = 0; f < frames; f += 2)
                        if (TruthGrip(truth, name, f, copy, animator, socketInRoot, out var g, out float w)) { worst = Mathf.Max(worst, Quaternion.Angle(g.Rotation, s.Rotation)); worstMm = Mathf.Max(worstMm, Vector3.Distance(g.Position, s.Position) * 1000f); wrist = Mathf.Max(wrist, w); }
                    log.AppendLine("  truth: " + name.PadRight(11) + " stance grip off by up to " + worst.ToString("F1", Inv) + " deg / " + worstMm.ToString("F0", Inv) + " mm · wrist up to " + (wrist * 100f).ToString("F1", Inv) + " cm from Blender's");
                }
                Transform socketL = Child(brush.transform, "GripSocketL");
                if (socketL != null && TruthGrip(truth, "StanceIdle", 0, copy, animator, socketL.rotation, out var l, out float wl, true)) { leftStance = l; log.AppendLine("  truth: left hand grip solved (wrist vs truth " + (wl * 100f).ToString("F1", Inv) + " cm)"); }
                stance = s; carry = c;
                return true;
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(brush); }
        }

        // ---------------------------------------------------------------- the close-up left arm (user 2026-10-13)
        // The close-up arm object carries the whole skeleton but only the right arm's triangles. For the two-handed hold the left
        // arm's triangles are added as extra renderers on the same bones (names start with NearL308_); revert removes them.
        static string NearLeftMeshPath(string skin) => Folder + "/Near/SM_NearL308_" + skin + ".asset";

        static string NearLeft()
        {
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            if (gesture == null) return "refused: no player in the open scenes";
            var log = new StringBuilder("GreatBrush308 close-up left arm\n");
            bool ok = BuildNearLeft(gesture, log);
            if (ok) { EditorSceneManager.MarkSceneDirty(gesture.gameObject.scene); gesture.RefreshGreatBrush308(); log.AppendLine("  scene dirty, NOT saved (save)"); }
            return log.ToString();
        }

        static bool BuildNearLeft(WorldMacroPlayerGestureRig gesture, StringBuilder log)
        {
            Transform nearRoot = Field<Transform>(gesture, "_nearRoot");
            if (nearRoot == null) { log.AppendLine("  near left: no close-up arm"); return false; }
            RemoveNearLeft(nearRoot);
            EnsureFolder(Folder + "/Near");
            int made = 0, triangles = 0;
            foreach (var right in nearRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // the same-named skin of the world body has the whole mesh
                var world = gesture.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == right.name && !s.transform.IsChildOf(nearRoot) && s.sharedMesh != null && s.bones.Length == right.bones.Length);
                if (world == null) { log.AppendLine("  near left: no world skin for " + right.name); continue; }
                Mesh subset = ExtractArm(world, "Left"); if (subset == null) continue;
                string path = NearLeftMeshPath(right.name);
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) AssetDatabase.CreateAsset(subset, path); else { EditorUtility.CopySerialized(subset, existing); Object.DestroyImmediate(subset); subset = existing; }
                var go = new GameObject(WorldMacroPlayerGestureRig.NearLeftPrefix308 + right.name);
                go.transform.SetParent(right.transform.parent, false); go.layer = right.gameObject.layer;
                go.transform.localPosition = right.transform.localPosition; go.transform.localRotation = right.transform.localRotation; go.transform.localScale = right.transform.localScale;
                var skin = go.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = subset; skin.bones = right.bones; skin.rootBone = right.rootBone; skin.sharedMaterials = right.sharedMaterials;
                skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; skin.receiveShadows = false; skin.updateWhenOffscreen = true; skin.enabled = false;
                made++; triangles += subset.triangles.Length / 3;
            }
            log.AppendLine("  near left: " + made + " renderer(s), " + triangles + " triangles");
            return made > 0;
        }

        static int RemoveNearLeft(Transform nearRoot)
        {
            int removed = 0;
            foreach (var t in nearRoot.GetComponentsInChildren<Transform>(true).Where(t => t != null && t.name.StartsWith(WorldMacroPlayerGestureRig.NearLeftPrefix308, StringComparison.Ordinal)).ToArray())
            { Object.DestroyImmediate(t.gameObject); removed++; }
            return removed;
        }

        // as WorldMacroPlayerReRigInstallation.ExtractRightArm, for a named side
        static Mesh ExtractArm(SkinnedMeshRenderer skin, string side)
        {
            Mesh source = skin.sharedMesh; var bones = skin.bones;
            var sourceCounts = source.GetBonesPerVertex(); var sourceWeights = source.GetAllBoneWeights();
            try
            {
                bool[] selected = new bool[source.vertexCount]; int[] weightStart = new int[source.vertexCount]; int offset = 0;
                for (int i = 0; i < source.vertexCount; i++)
                {
                    weightStart[i] = offset; float arm = 0f;
                    for (int j = 0; j < sourceCounts[i]; j++)
                    {
                        var w = sourceWeights[offset++]; string name = w.boneIndex < bones.Length && bones[w.boneIndex] != null ? bones[w.boneIndex].name : "";
                        if (name == side + "Arm" || name == side + "ForeArm" || name.StartsWith(side + "Hand", StringComparison.Ordinal)) arm += w.weight;
                    }
                    selected[i] = arm >= .65f;
                }
                var remap = new Dictionary<int, int>(); var ids = new List<int>(); var submeshes = new List<int[]>();
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    int[] tris = source.GetTriangles(sub); var kept = new List<int>();
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        if (!selected[tris[i]] || !selected[tris[i + 1]] || !selected[tris[i + 2]]) continue;
                        for (int j = 0; j < 3; j++) { int old = tris[i + j]; if (!remap.TryGetValue(old, out int next)) { next = ids.Count; remap.Add(old, next); ids.Add(old); } kept.Add(next); }
                    }
                    submeshes.Add(kept.ToArray());
                }
                if (ids.Count == 0) return null;
                var mesh = new Mesh { name = "NearL308_" + skin.name, indexFormat = ids.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                Vector3[] vertices = source.vertices; mesh.vertices = ids.Select(i => vertices[i]).ToArray();
                Vector3[] normals = source.normals; if (normals.Length == source.vertexCount) mesh.normals = ids.Select(i => normals[i]).ToArray();
                Vector4[] tangents = source.tangents; if (tangents.Length == source.vertexCount) mesh.tangents = ids.Select(i => tangents[i]).ToArray();
                Color[] colors = source.colors; if (colors.Length == source.vertexCount) mesh.colors = ids.Select(i => colors[i]).ToArray();
                for (int channel = 0; channel < 8; channel++) { var uv = new List<Vector4>(); source.GetUVs(channel, uv); if (uv.Count == source.vertexCount) mesh.SetUVs(channel, ids.Select(i => uv[i]).ToList()); }
                mesh.bindposes = source.bindposes;
                var counts = new Unity.Collections.NativeArray<byte>(ids.Count, Unity.Collections.Allocator.Temp); var weights = new List<BoneWeight1>();
                for (int i = 0; i < ids.Count; i++) { int old = ids[i]; counts[i] = sourceCounts[old]; for (int j = 0; j < sourceCounts[old]; j++) weights.Add(sourceWeights[weightStart[old] + j]); }
                using (var all = new Unity.Collections.NativeArray<BoneWeight1>(weights.ToArray(), Unity.Collections.Allocator.Temp)) mesh.SetBoneWeights(counts, all);
                counts.Dispose();
                mesh.subMeshCount = submeshes.Count; for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
                mesh.RecalculateBounds(); return mesh;
            }
            finally { sourceCounts.Dispose(); sourceWeights.Dispose(); }
        }

        // ---------------------------------------------------------------- close-up preview without Play (2026-10-13)
        // The first-person hands were wrong three times in a row and each look cost a Play session. This runs the rig's own
        // close-up solve in edit mode for a grid of pointer positions and writes a still per position, the hand cropped large,
        // plus the numbers that say whether the hand is on the shaft. Nothing is saved; the rig is put back.
        //   nearpreview[:stroke|hover]
        // which way the thumb points: along the shaft toward the tip (+) or back toward the player (-), and in the camera's axes
        static string ThumbLine(Transform nearRoot, Transform hand, Vector3 axis, Transform view)
        {
            Transform root = Child(nearRoot, "RightHandThumb1"), end = Child(nearRoot, "RightHandThumb3"), index = Child(nearRoot, "RightHandIndex1"), pinky = Child(nearRoot, "RightHandPinky1");
            if (root == null || end == null || index == null || pinky == null) return "";
            Vector3 thumb = (end.position - root.position).normalized, radial = (index.position - pinky.position).normalized;
            return " · thumb along shaft " + Vector3.Dot(thumb, axis).ToString("F2", Inv) + " (camera " + view.InverseTransformDirection(thumb).ToString("F2") + ") · thumb side of the hand along shaft " + Vector3.Dot(radial, axis).ToString("F2", Inv);
        }

        // how far the middle finger goes round the shaft: the angle (about the shaft's axis) swept from the finger root to the
        // fingertip, and the nearest / farthest joint from the axis. A hand that holds the shaft sweeps well over 120 degrees
        // with every joint 2 - 4 cm from the axis; a fist beside the shaft sweeps little.
        static string WrapLine(Transform nearRoot, Vector3 onAxis, Vector3 axis)
        {
            var joints = new List<Transform>();
            for (int j = 1; j <= 4; j++) { var t = Child(nearRoot, "RightHandMiddle" + j); if (t != null) joints.Add(t); }
            if (joints.Count < 3) return "";
            float swept = 0f, nearest = float.MaxValue, farthest = 0f; Vector3 last = Vector3.zero;
            for (int i = 0; i < joints.Count; i++)
            {
                Vector3 radial = Vector3.ProjectOnPlane(joints[i].position - onAxis, axis);
                nearest = Mathf.Min(nearest, radial.magnitude); farthest = Mathf.Max(farthest, radial.magnitude);
                if (i > 0) swept += Vector3.SignedAngle(last, radial, axis);
                last = radial;
            }
            return " · wrap " + Mathf.Abs(swept).ToString("F0", Inv) + " deg (joints " + (nearest * 100f).ToString("F1", Inv) + " - " + (farthest * 100f).ToString("F1", Inv) + " cm from the axis)";
        }

        // Is the hand's mesh skinned finger by finger? (user 2026-10-13: "메시가 뭉개져서 손가락 개별 리깅이 안되는 상황인 거 아니야?")
        // For the player's full body mesh: how many vertices each finger bone owns, how many are shared between two different
        // fingers (a web or a fused mitten bends with both and tears / smears), and how many belong to the hand bone itself.
        // ---------------------------------------------------------------- the baked first-person hand (D308-80h, user 2026-10-13
        // "blender로 손 모델 따로 만들어서 진행해봐"). Tools/Blender/GreatBrush308/nearhand_build.py cuts the right arm from the player,
        // closes the fingers round the shaft and bakes that into the vertices (fingers weighted to the hand bone). Here its mesh is
        // moved into the close-up arm's own mesh space (fitted on joint positions, so no assumption about either file's axes or
        // scale), given the close-up arm's bind poses and put on the same bones; the old close-up arm renderer is switched off.
        //   nearhand | nearhand:revert
        const string NearHandPrefix = "NearHand308_";
        static string NearHandSource => Path.Combine(Repo, "Art", "Characters308", "GreatBrush308", "nearhand");
        const string NearHandModel = Folder + "/Near/SM_NearHand308.fbx";
        const string NearHandMesh = Folder + "/Near/SM_NearHand308_Fitted.asset";
        const string NearHandBase = Folder + "/Near/T_NearHand308_Base.png";
        const string NearHandNormal = Folder + "/Near/T_NearHand308_Normal.png";
        const string NearHandMaterial = Folder + "/Near/M_NearHand308.mat";

        static string NearHand(bool revert)
        {
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            if (gesture == null) return "refused: no player in the open scenes";
            Transform nearRoot = Field<Transform>(gesture, "_nearRoot");
            if (nearRoot == null) return "refused: no close-up arm";
            var log = new StringBuilder("GreatBrush308 baked first-person hand" + (revert ? " (revert)" : "") + "\n");
            var list = Renderers(gesture, "_nearRenderers").Where(r => r != null).ToList();
            var profile = AssetDatabase.LoadAssetAtPath<GreatBrush308Profile>(GreatProfilePath);
            // undo a previous install first (also the whole of revert)
            foreach (var old in nearRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name.StartsWith(NearHandPrefix, StringComparison.Ordinal)).ToArray())
            {
                string original = old.name.Substring(NearHandPrefix.Length);
                var right = old.transform.parent.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == original);
                int at = list.IndexOf(old); if (at >= 0) { if (right != null) list[at] = right; else list.RemoveAt(at); }
                Object.DestroyImmediate(old.gameObject); log.AppendLine("  removed " + NearHandPrefix + original + (right != null ? " (old arm back in the list)" : ""));
            }
            if (revert)
            {
                SetRenderers(gesture, "_nearRenderers", list.ToArray());
                if (profile != null) { profile.NearBakedGrip = false; EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile); }
                gesture.RefreshGreatBrush308(); EditorSceneManager.MarkSceneDirty(gesture.gameObject.scene);
                return log.AppendLine("  NearBakedGrip off · scene dirty, NOT saved (save)").ToString();
            }
            if (profile == null) return "refused: no great brush profile (install)";
            string json = Path.Combine(NearHandSource, "nearhand.json");
            if (!File.Exists(json)) return "refused: " + json + " missing (run nearhand_build.py export)";
            EnsureFolder(Folder + "/Near");
            log.AppendLine("  model " + CopyIn(Path.Combine(NearHandSource, "SM_NearHand308.fbx"), NearHandModel));
            var importer = AssetImporter.GetAtPath(NearHandModel) as ModelImporter;
            if (importer != null && (importer.animationType != ModelImporterAnimationType.Generic || importer.avatarSetup != ModelImporterAvatarSetup.NoAvatar || importer.importBlendShapes || importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importNormals != ModelImporterNormals.Import))
            {
                importer.animationType = ModelImporterAnimationType.Generic; importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar; importer.importAnimation = false;   // Rig None would drop the skin importer.importBlendShapes = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.meshCompression = ModelImporterMeshCompression.Off; importer.optimizeMeshPolygons = false; importer.optimizeMeshVertices = false; importer.weldVertices = true;
                importer.SaveAndReimport();
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NearHandModel);
            var baked = prefab != null ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (baked == null || baked.sharedMesh == null) return log.Append("refused: the model has no skinned mesh").ToString();
            var target = list.OfType<SkinnedMeshRenderer>().FirstOrDefault(s => s.sharedMesh != null && s.bones.Any(t => t != null && t.name == "RightHand"));
            if (target == null) return log.Append("refused: the close-up arm has no skinned renderer with a RightHand bone").ToString();

            // joint positions in each mesh's own space at bind
            Mesh src = baked.sharedMesh, old0 = target.sharedMesh; var srcBones = baked.bones; var oldBones = target.bones; var srcPoses = src.bindposes; var oldPoses = old0.bindposes;
            int srcHand = Array.FindIndex(srcBones, t => t != null && t.name == "RightHand"), oldHand = Array.FindIndex(oldBones, t => t != null && t.name == "RightHand");
            if (srcHand < 0 || oldHand < 0) return log.Append("refused: RightHand is not a skin bone").ToString();
            Matrix4x4 srcWorldToMesh = (srcBones[srcHand].localToWorldMatrix * srcPoses[srcHand]).inverse;
            Vector3 SrcJoint(string name) { var t = Child(prefab.transform, name); return t != null ? srcWorldToMesh.MultiplyPoint3x4(t.position) : Vector3.positiveInfinity; }
            Vector3 OldJoint(string name) { int i = Array.FindIndex(oldBones, t => t != null && t.name == name); return i >= 0 ? oldPoses[i].inverse.MultiplyPoint3x4(Vector3.zero) : Vector3.positiveInfinity; }
            Matrix4x4 Frame(Vector3 h, Vector3 a, Vector3 b)
            {
                Vector3 x = (a - h).normalized, y = Vector3.ProjectOnPlane(b - h, x).normalized, z = Vector3.Cross(x, y);
                var m = Matrix4x4.identity; m.SetColumn(0, x); m.SetColumn(1, y); m.SetColumn(2, z); m.SetColumn(3, new Vector4(h.x, h.y, h.z, 1f)); return m;
            }
            string[] fit = { "RightHand", "RightHandIndex1", "RightHandPinky1" };
            if (fit.Any(n => float.IsInfinity(SrcJoint(n).x) || float.IsInfinity(OldJoint(n).x))) return log.Append("refused: a fitting joint is missing in one of the two skeletons").ToString();
            float scale = Vector3.Distance(OldJoint("RightForeArm"), OldJoint("RightHand")) / Mathf.Max(1e-6f, Vector3.Distance(SrcJoint("RightForeArm"), SrcJoint("RightHand")));
            Matrix4x4 toOld = Frame(OldJoint(fit[0]), OldJoint(fit[1]), OldJoint(fit[2])) * Matrix4x4.Scale(Vector3.one * scale) * Frame(SrcJoint(fit[0]), SrcJoint(fit[1]), SrcJoint(fit[2])).inverse;
            var residual = new StringBuilder();
            foreach (string n in new[] { "RightArm", "RightForeArm", "RightHandMiddle1", "RightHandThumb1", "RightHandIndex3" })
                residual.Append(" " + n.Substring(5) + " " + (Vector3.Distance(toOld.MultiplyPoint3x4(SrcJoint(n)), OldJoint(n)) * 1000f).ToString("F1", Inv));
            log.AppendLine("  fit: scale " + scale.ToString("F4", Inv) + " · joints off after fitting (mm):" + residual);

            var mesh = new Mesh { name = "SM_NearHand308_Fitted", indexFormat = src.vertexCount > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = src.vertices.Select(v => toOld.MultiplyPoint3x4(v)).ToArray();
            if (src.normals.Length == src.vertexCount) mesh.normals = src.normals.Select(v => toOld.MultiplyVector(v).normalized).ToArray();
            if (src.tangents.Length == src.vertexCount) mesh.tangents = src.tangents.Select(t => { Vector3 d = toOld.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized; return new Vector4(d.x, d.y, d.z, t.w); }).ToArray();
            for (int channel = 0; channel < 4; channel++) { var uv = new List<Vector4>(); src.GetUVs(channel, uv); if (uv.Count == src.vertexCount) mesh.SetUVs(channel, uv); }
            int[] map = srcBones.Select(t => t == null ? -1 : Array.FindIndex(oldBones, o => o != null && o.name == t.name)).ToArray();
            var counts = src.GetBonesPerVertex(); var weights = src.GetAllBoneWeights(); var outWeights = new BoneWeight1[weights.Length]; int lost = 0;
            for (int i = 0; i < weights.Length; i++) { int to = weights[i].boneIndex < map.Length ? map[weights[i].boneIndex] : -1; if (to < 0) { to = oldHand; lost++; } outWeights[i] = new BoneWeight1 { boneIndex = to, weight = weights[i].weight }; }
            mesh.bindposes = oldPoses;
            using (var all = new Unity.Collections.NativeArray<BoneWeight1>(outWeights, Unity.Collections.Allocator.Temp)) mesh.SetBoneWeights(counts, all);
            mesh.subMeshCount = 1; var tris = new List<int>(); for (int sub = 0; sub < src.subMeshCount; sub++) tris.AddRange(src.GetTriangles(sub)); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(NearHandMesh);
            // a fresh asset every time: copying into the old one left a skinned renderer with "data does not match the expected vertex stride" when the layout changed
            if (existing != null) AssetDatabase.DeleteAsset(NearHandMesh);
            AssetDatabase.CreateAsset(mesh, NearHandMesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            log.AppendLine("  mesh " + mesh.vertexCount + " vertices, " + mesh.triangles.Length / 3 + " triangles (old close-up arm " + old0.vertexCount + " / " + old0.triangles.Length / 3 + ") · weights that had no bone here " + lost);

            var go = new GameObject(NearHandPrefix + target.name);
            go.transform.SetParent(target.transform.parent, false); go.layer = target.gameObject.layer;
            go.transform.localPosition = target.transform.localPosition; go.transform.localRotation = target.transform.localRotation; go.transform.localScale = target.transform.localScale;
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh; skin.bones = target.bones; skin.rootBone = target.rootBone; skin.sharedMaterials = target.sharedMaterials.Take(1).ToArray();
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; skin.receiveShadows = target.receiveShadows; skin.updateWhenOffscreen = true; skin.enabled = false;
            target.enabled = false;
            // its own picture (user 2026-10-13: a hand seen up close all the time): the arm unwrapped alone, skin repainted, plus a normal map
            if (File.Exists(Path.Combine(NearHandSource, "T_NearHand308_Base.png")) && mesh.uv.Length == mesh.vertexCount)
            {
                log.AppendLine("  base map " + CopyIn(Path.Combine(NearHandSource, "T_NearHand308_Base.png"), NearHandBase) + " · normal map " + CopyIn(Path.Combine(NearHandSource, "T_NearHand308_Normal.png"), NearHandNormal));
                foreach (string path in new[] { NearHandBase, NearHandNormal })
                {
                    var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (ti == null) continue;
                    bool normal = path == NearHandNormal; var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    if (ti.textureType == type && ti.sRGBTexture == !normal && ti.mipmapEnabled && ti.maxTextureSize == 2048 && ti.anisoLevel == 4) continue;
                    ti.textureType = type; ti.sRGBTexture = !normal; ti.mipmapEnabled = true; ti.maxTextureSize = 2048; ti.anisoLevel = 4; ti.wrapMode = TextureWrapMode.Clamp;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport();
                }
                var source = target.sharedMaterials.FirstOrDefault(m => m != null);
                var material = AssetDatabase.LoadAssetAtPath<Material>(NearHandMaterial); bool made = material == null;
                if (made) material = new Material(source); else if (source != null) { material.shader = source.shader; material.CopyPropertiesFromMaterial(source); }
                material.name = "M_NearHand308";
                var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(NearHandBase); var normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>(NearHandNormal);
                if (material.HasProperty("_BaseMap")) { material.SetTexture("_BaseMap", baseMap); material.SetTextureScale("_BaseMap", Vector2.one); material.SetTextureOffset("_BaseMap", Vector2.zero); }
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", baseMap);
                if (material.HasProperty("_BumpMap") && normalMap != null) { material.SetTexture("_BumpMap", normalMap); material.SetFloat("_BumpScale", 1f); }
                if (material.HasProperty("_Saturation")) material.SetFloat("_Saturation", Mathf.Max(material.GetFloat("_Saturation"), .95f));   // skin seen up close: the body's muted look reads as grey putty
                if (made) AssetDatabase.CreateAsset(material, NearHandMaterial); else EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material); skin.sharedMaterials = new[] { material };
                log.AppendLine("  material " + NearHandMaterial + " (shader " + material.shader.name + ")");
            }
            else log.AppendLine("  no baked picture next to the model: the old arm material stays (its UVs will not match a re-unwrapped mesh)");
            list[list.IndexOf(target)] = skin; SetRenderers(gesture, "_nearRenderers", list.ToArray());

            // the grip numbers
            var r = new Reader(File.ReadAllText(json)); var root = r.Value() as Dictionary<string, object>;
            Vector3 V(string key) { var a = (List<object>)root[key]; return new Vector3(Convert.ToSingle(a[0], Inv), Convert.ToSingle(a[1], Inv), Convert.ToSingle(a[2], Inv)); }
            profile.NearBakedGrip = true; profile.NearBakedCentre = V("shaft_centre"); profile.NearBakedAxis = V("shaft_axis_to_tip"); profile.NearBakedNormal = V("palm_to_shaft"); profile.NearBakedForearm = V("forearm_dir");
            if (root.TryGetValue("knuckle_m", out object k)) profile.NearBakedKnuckle = Convert.ToSingle(k, Inv);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            log.AppendLine("  profile: NearBakedGrip on · centre " + profile.NearBakedCentre.ToString("F4") + " axis " + profile.NearBakedAxis.ToString("F3") + " normal " + profile.NearBakedNormal.ToString("F3"));
            gesture.RefreshGreatBrush308(); EditorSceneManager.MarkSceneDirty(gesture.gameObject.scene);
            return log.AppendLine("  scene dirty, NOT saved (save)").ToString();
        }

        // The tail ornament hangs (D308-80i): brings the v6 brush model in and upgrades the brushes already in the scene in place
        // (they are plain copies of the model, not prefab instances): the missing transforms (Tail_01..03, TailEndSocket) are
        // added under their parents, each skin's bone list follows the model's by name, and GreatBrushTail308 is added.
        // Then the chain is left to fall for two seconds where each brush is and the result is reported. Scene dirty, NOT saved.
        static string Tail()
        {
            var b = new StringBuilder("GreatBrush308 tail ornament\n");
            b.AppendLine("  brush model " + CopyIn(Path.Combine(SourceBrush, "SM_GreatBrush308.fbx"), BrushModel) + " (" + SourceBrush + ")");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            if (model == null) return b.Append("refused: no brush model").ToString();
            if (Child(model.transform, GreatBrushTail308.Prefix + "01") == null) return b.Append("refused: the model has no Tail_01 (run tail_v6.py)").ToString();
            var brushes = Object.FindObjectsByType<GreatBrushBristles308>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (brushes.Length == 0) return b.Append("refused: no great brush in the open scenes (install)").ToString();
            foreach (var bristles in brushes)
            {
                Transform root = bristles.transform; int added = 0;
                foreach (var source in model.GetComponentsInChildren<Transform>(true))   // parents come first
                {
                    if (source == model.transform) continue;
                    Transform parent = source.parent == model.transform ? root : Child(root, source.parent.name), have = Child(root, source.name);
                    if (have != null)
                    {
                        // a tail transform from an earlier chain (three rigid links): the model's place and parent now
                        if (have.name.StartsWith("Tail", StringComparison.Ordinal) && parent != null) { have.SetParent(parent, false); have.localPosition = source.localPosition; have.localRotation = source.localRotation; have.localScale = source.localScale; }
                        continue;
                    }
                    if (parent == null) { b.AppendLine("  " + root.name + ": no parent for " + source.name); continue; }
                    var made = new GameObject(source.name).transform; made.SetParent(parent, false); made.gameObject.layer = parent.gameObject.layer;
                    made.localPosition = source.localPosition; made.localRotation = source.localRotation; made.localScale = source.localScale; added++;
                }
                int skins = 0, unmatched = 0;
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var from = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == skin.name);
                    if (from == null) continue;
                    var bones = from.bones.Select(t => t == null ? null : Child(root, t.name)).ToArray(); unmatched += bones.Count(t => t == null);
                    skin.sharedMesh = from.sharedMesh; skin.bones = bones; skins++; EditorUtility.SetDirty(skin);
                }
                var tail = root.GetComponent<GreatBrushTail308>(); if (tail == null) tail = root.gameObject.AddComponent<GreatBrushTail308>();
                var profile = AssetDatabase.LoadAssetAtPath<GreatBrush308Profile>(GreatProfilePath); if (profile != null) tail.Configure(profile.Tail);
                bool bound = tail.TryBind(); tail.ResetChain();
                Transform grip = Child(root, GreatBrushBristles308.GripName), end = Child(root, "ShaftEndSocket");
                Vector3 tipward = grip != null && end != null ? (grip.position - end.position).normalized : Vector3.up;
                string line = "  " + root.name + (root.gameObject.activeInHierarchy ? "" : " (inactive)") + ": transforms added " + added + " · skins " + skins + " · bones without a transform " + unmatched + " · bound " + bound + (bound ? "" : " (" + tail.BindingError + ")");
                if (bound)
                {
                    Vector3 before = tail.HangDirection; tail.Settle(2f); Vector3 after = tail.HangDirection;
                    line += " · links " + tail.Count + " · shaft tip-ward vs up " + Vector3.Dot(tipward, Vector3.up).ToString("F2", Inv) + " · ornament vs down: modelled " + Vector3.Dot(before, Vector3.down).ToString("F2", Inv) + " → hanging " + Vector3.Dot(after, Vector3.down).ToString("F2", Inv)
                        + " · nearest to the shaft line " + (tail.NearestToShaft < 0f ? "not alongside" : (tail.NearestToShaft * 100f).ToString("F1", Inv) + " cm");
                }
                b.AppendLine(line); EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
                if (bound && root.gameObject.activeInHierarchy && end != null)
                {
                    // slung on the back the tip points down and the tail up (user's screenshot 2026-10-14): turn the brush that way for a
                    // moment, let the ornament fall, measure and take a still, then put the brush back
                    Vector3 keepPosition = root.position; Quaternion keepRotation = root.rotation;
                    root.rotation = Quaternion.AngleAxis(155f, Vector3.forward) * keepRotation; root.position += keepPosition - end.position + Vector3.up * 600f;   // far above everything: the still shows the brush alone
                    tail.ResetChain(); Vector3 stiff = tail.HangDirection; tail.Settle(10f); Vector3 hung = tail.HangDirection;
                    tipward = (grip.position - end.position).normalized;
                    b.AppendLine("    slung test (tip-ward vs up " + Vector3.Dot(tipward, Vector3.up).ToString("F2", Inv) + "): ornament vs down: stiff " + Vector3.Dot(stiff, Vector3.down).ToString("F2", Inv) + " → hanging " + Vector3.Dot(hung, Vector3.down).ToString("F2", Inv)
                        + " · nearest to the shaft line " + (tail.NearestToShaft < 0f ? "not alongside" : (tail.NearestToShaft * 100f).ToString("F1", Inv) + " cm"));
                    var shown = root.GetComponentsInChildren<Renderer>(true); var was = shown.Select(r => r.enabled).ToArray();
                    var modes = shown.Select(r => r.shadowCastingMode).ToArray(); b.AppendLine("    renderers: " + string.Join(" · ", shown.Select(r => r.name + " enabled " + r.enabled + " shadows " + r.shadowCastingMode + " layer " + LayerMask.LayerToName(r.gameObject.layer))));
                    foreach (var r in shown) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    foreach (var r in shown) { r.enabled = true; if (r is SkinnedMeshRenderer sk) { sk.forceMatrixRecalculationPerRender = true; sk.updateWhenOffscreen = false; sk.localBounds = new Bounds(Vector3.zero, Vector3.one * 6f); } }   // bounds that follow the transform at once: in edit mode the skinned bounds stay where the brush was last drawn and the camera culls it
                    var camObject = new GameObject("Tail308Camera") { hideFlags = HideFlags.HideAndDontSave }; var cam = camObject.AddComponent<Camera>();
                    var rt = new RenderTexture(900, 900, 24); var tex = new Texture2D(900, 900, TextureFormat.RGB24, false);
                    try
                    {
                        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.74f, .71f, .64f); cam.cullingMask = 1 << root.gameObject.layer; cam.farClipPlane = 6f; cam.fieldOfView = 30f; cam.nearClipPlane = .05f; cam.targetTexture = rt;
                        Vector3 look = end.position + Vector3.down * .08f; cam.transform.position = look + new Vector3(.25f, .12f, -.95f); cam.transform.LookAt(look);
                        cam.Render(); var old = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0); tex.Apply(); RenderTexture.active = old;
                        string folder = Path.Combine(Repo, "Art", "Characters308", "GreatBrush308", "tail_preview"); Directory.CreateDirectory(folder);
                        File.WriteAllBytes(Path.Combine(folder, root.name + "_slung.png"), tex.EncodeToPNG()); b.AppendLine("    still " + Path.Combine(folder, root.name + "_slung.png"));
                    }
                    finally { cam.targetTexture = null; Object.DestroyImmediate(camObject); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
                    for (int i = 0; i < shown.Length; i++) { shown[i].enabled = was[i]; shown[i].shadowCastingMode = modes[i]; }
                    foreach (var r in shown) if (r is SkinnedMeshRenderer sk) { sk.ResetLocalBounds(); sk.updateWhenOffscreen = true; }
                    root.SetPositionAndRotation(keepPosition, keepRotation); tail.ResetChain();
                }
            }
            return b.AppendLine("  scene dirty, NOT saved (save)").ToString();
        }

        static string HandSkin()
        {
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            if (gesture == null) return "refused: no player in the open scenes";
            Transform nearRoot = Field<Transform>(gesture, "_nearRoot");
            var b = new StringBuilder("GreatBrush308 handskin\n");
            foreach (var skin in gesture.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null || (nearRoot != null && skin.transform.IsChildOf(nearRoot))) continue;
                var bones = skin.bones; if (!bones.Any(t => t != null && t.name == "RightHandIndex1")) continue;
                Mesh mesh = skin.sharedMesh; var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
                string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
                int[] owned = new int[5]; int[] jointOwned = new int[15]; int shared = 0, handOnly = 0, handVerts = 0; int offset = 0;
                for (int v = 0; v < mesh.vertexCount; v++)
                {
                    float[] perFinger = new float[5]; float hand = 0f; int best = -1; float bestWeight = 0f; int bestJoint = -1;
                    for (int j = 0; j < counts[v]; j++)
                    {
                        var w = weights[offset++]; string name = w.boneIndex < bones.Length && bones[w.boneIndex] != null ? bones[w.boneIndex].name : "";
                        if (name == "RightHand") hand += w.weight;
                        for (int f = 0; f < 5; f++)
                            if (name.StartsWith("RightHand" + fingers[f], StringComparison.Ordinal))
                            {
                                perFinger[f] += w.weight;
                                if (w.weight > bestWeight) { bestWeight = w.weight; best = f; int digit = name[name.Length - 1] - '1'; bestJoint = f * 3 + Mathf.Clamp(digit, 0, 2); }
                            }
                    }
                    float total = hand + perFinger.Sum(); if (total < .5f) continue;
                    handVerts++;
                    int strong = perFinger.Count(x => x > .2f);
                    if (strong >= 2) shared++;
                    if (best >= 0 && perFinger[best] >= hand) { owned[best]++; if (bestJoint >= 0) jointOwned[bestJoint]++; } else handOnly++;
                }
                b.AppendLine("  skin " + skin.name + ": hand + finger vertices " + handVerts + " · owned by the palm bone " + handOnly + " · shared between two fingers " + shared + " (" + (handVerts > 0 ? (100f * shared / handVerts).ToString("F0", Inv) : "0") + " %)");
                for (int f = 0; f < 5; f++)
                    b.AppendLine("    " + fingers[f].PadRight(7) + owned[f] + " vertices · by joint " + jointOwned[f * 3] + " / " + jointOwned[f * 3 + 1] + " / " + jointOwned[f * 3 + 2]);
            }
            return b.ToString();
        }

        static string NearPreview(string mode)
        {
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            if (gesture == null) return "refused: no player in the open scenes";
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var type = typeof(WorldMacroPlayerGestureRig);
            gesture.RefreshGreatBrush308();
            var solve = type.GetMethod("NearGreat308", Any); var show = type.GetMethod("SetNearVisible", Any); var flag = type.GetField("_nearPreview308", Any);
            Transform nearRoot = Field<Transform>(gesture, "_nearRoot");
            object nearBrush = type.GetField("_nearBrush", Any)?.GetValue(gesture);
            Transform brushRoot = nearBrush?.GetType().GetField("Root", Any)?.GetValue(nearBrush) as Transform, tip = nearBrush?.GetType().GetField("Tip", Any)?.GetValue(nearBrush) as Transform;
            if (solve == null || show == null || flag == null || nearRoot == null || brushRoot == null || tip == null) return "refused: the rig has no close-up solve / arm / brush (" + (gesture.GreatBrushWhy308 ?? "bound") + ")";
            string folder = Path.Combine(Repo, "Art", "Characters308", "GreatBrush308", "near_preview"); Directory.CreateDirectory(folder);
            var b = new StringBuilder("GreatBrush308 nearpreview (" + (mode == "hover" ? "hover" : "stroke") + ")\n");
            Vector3 rootPosition = nearRoot.position, brushPosition = brushRoot.position; Quaternion rootRotation = nearRoot.rotation, brushRotation = brushRoot.rotation;
            var camObject = new GameObject("GreatBrush308 near preview camera") { hideFlags = HideFlags.HideAndDontSave };
            const int W = 1280, H = 720;
            var rt = new RenderTexture(W, H, 24); var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            try
            {
                var cam = camObject.AddComponent<Camera>();
                var main = Camera.main;
                cam.fieldOfView = main != null ? main.fieldOfView : 60f; cam.nearClipPlane = .05f; cam.farClipPlane = 80f; cam.targetTexture = rt; cam.aspect = W / (float)H;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.74f, .71f, .64f);
                cam.transform.SetPositionAndRotation(gesture.transform.position + Vector3.up * 220f, Quaternion.Euler(4f, 20f, 0f));
                b.AppendLine("  camera fov " + cam.fieldOfView.ToString("F0", Inv));
                {
                    // the hand's own measures (cm): wrist -> finger root -> joints -> tip, the thumb, and the brush mesh size
                    string Chain(string finger)
                    {
                        Transform h = Child(nearRoot, "RightHand"); var parts = new List<string>(); Transform last = h;
                        for (int j = 1; j <= 4; j++) { Transform t = Child(nearRoot, "RightHand" + finger + j); if (t == null) break; parts.Add((Vector3.Distance(last.position, t.position) * 100f).ToString("F1", Inv)); last = t; }
                        return finger + " " + string.Join(" / ", parts);
                    }
                    string sizes = "";
                    foreach (var r in brushRoot.GetComponentsInChildren<Renderer>(true)) sizes += " " + r.name + " " + r.bounds.size.ToString("F3");
                    b.AppendLine("  hand (cm): " + Chain("Middle") + " · " + Chain("Index") + " · " + Chain("Pinky") + " · " + Chain("Thumb") + " · brush renderers" + sizes);
                }
                {
                    // which transforms are the hand: the rig's, the first one of that name, and the ones the skins are bound to
                    object nearBody = type.GetField("_near", Any)?.GetValue(gesture);
                    Transform rigHand = nearBody?.GetType().GetField("Hand", Any)?.GetValue(nearBody) as Transform, rigFore = nearBody?.GetType().GetField("Forearm", Any)?.GetValue(nearBody) as Transform, rigUpper = nearBody?.GetType().GetField("Upper", Any)?.GetValue(nearBody) as Transform;
                    var named = nearRoot.GetComponentsInChildren<Transform>(true).Where(t => t.name == "RightHand").ToArray();
                    b.AppendLine("  rig chain " + (rigUpper != null ? rigUpper.name : "?") + " > " + (rigFore != null ? rigFore.name : "?") + " > " + (rigHand != null ? rigHand.name : "?") + " · transforms named RightHand under the close-up arm: " + named.Length + " · rig hand is the first: " + (named.Length > 0 && named[0] == rigHand)
                        + " · parents " + (rigHand != null && rigHand.parent == rigFore) + "/" + (rigFore != null && rigFore.parent == rigUpper));
                    foreach (var skin in nearRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var bound = skin.bones.Where(t => t != null && t.name == "RightHand").ToArray();
                        b.AppendLine("  skin " + skin.name + " enabled " + skin.enabled + " · bones " + skin.bones.Length + " · bound RightHand " + bound.Length + " same as rig " + (bound.Length > 0 && bound[0] == rigHand) + " · root bone " + (skin.rootBone != null ? skin.rootBone.name : "none") + " · under near root " + (bound.Length > 0 && bound[0].IsChildOf(nearRoot)));
                    }
                }
                flag.SetValue(gesture, true);
                // edit mode: a skinned mesh keeps the pose it was last skinned in unless it is told to skin again for every render
                foreach (var skin in nearRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { skin.forceMatrixRecalculationPerRender = true; skin.updateWhenOffscreen = true; }
                foreach (var skin in brushRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { skin.forceMatrixRecalculationPerRender = true; skin.updateWhenOffscreen = true; }
                show.Invoke(gesture, new object[] { true });
                Transform hand = Child(nearRoot, "RightHand"), middle = Child(nearRoot, "RightHandMiddle1"), middleTip = Child(nearRoot, "RightHandMiddle3"), thumbTip = Child(nearRoot, "RightHandThumb3"), fore = Child(nearRoot, "RightForeArm"), upper = Child(nearRoot, "RightArm");
                float[] xs = { .25f, .5f, .75f }, ys = { .7f, .5f, .3f }; int index = 0;
                foreach (float y in ys)
                    foreach (float x in xs)
                    {
                        Vector2 screen = new Vector2(x * W, y * H);
                        bool ok = false;
                        for (int i = 0; i < 12; i++) ok = (bool)solve.Invoke(gesture, new object[] { cam, screen, Vector3.zero, mode != "hover", 1f / 60f });
                        if (!ok) { b.AppendLine("  " + index + " solve refused (" + (gesture.GreatBrushWhy308 ?? "?") + ")"); index++; continue; }
                        cam.Render();
                        var old = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = old;
                        File.WriteAllBytes(Path.Combine(folder, "near_" + index + ".png"), tex.EncodeToPNG());
                        if (index == 1 || index == 4 || index == 3)
                        {
                            // the same hand from three close cameras: the eye zoomed in, from the right side, from above
                            Vector3 eye = cam.transform.position; Quaternion look = cam.transform.rotation; float fov = cam.fieldOfView; Vector3 at = gesture.NearHoldRight308;
                            Vector3[] from = { eye, at + look * new Vector3(-.30f, .10f, .05f), at + look * new Vector3(.03f, .30f, -.10f) }; string[] tag = { "eye", "side", "top" };
                            var leftShown = nearRoot.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith(WorldMacroPlayerGestureRig.NearLeftPrefix308, StringComparison.Ordinal) && r.enabled).ToArray();
                            foreach (var r in leftShown) r.enabled = false;   // the right hand alone
                            for (int k = 0; k < 3; k++)
                            {
                                cam.transform.position = from[k]; cam.transform.rotation = Quaternion.LookRotation(at - from[k], k == 2 ? look * Vector3.forward : look * Vector3.up); cam.fieldOfView = k == 0 ? 14f : 34f;
                                cam.Render();
                                var keep = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = keep;
                                File.WriteAllBytes(Path.Combine(folder, "hand_" + index + "_" + tag[k] + ".png"), tex.EncodeToPNG());
                            }
                            cam.transform.SetPositionAndRotation(eye, look); cam.fieldOfView = fov;
                            foreach (var r in leftShown) r.enabled = true;
                        }
                        Transform v = cam.transform; Vector3 axis = brushRoot.up, hold = gesture.NearHoldRight308;
                        string line = "  " + index + " pointer (" + x.ToString("F2", Inv) + ", " + y.ToString("F2", Inv) + ")";
                        if (hand != null && middle != null && fore != null && upper != null)
                        {
                            Vector3 f = (middle.position - hand.position).normalized, forearm = (hand.position - fore.position).normalized;
                            float elbow = Vector3.Angle(upper.position - fore.position, hand.position - fore.position);
                            Vector3 handScreen = cam.WorldToScreenPoint(hand.position);
                            line += " · hold " + v.InverseTransformPoint(hold).ToString("F2") + " · wrist bend " + Vector3.Angle(f, forearm).ToString("F0", Inv) + " deg · elbow " + elbow.ToString("F0", Inv) + " deg"
                                + " · fingers vs shaft " + Vector3.Angle(f, axis).ToString("F0", Inv) + " deg"
                                + (middleTip != null ? " · middle tip to shaft " + (Vector3.Cross(middleTip.position - (Child(brushRoot, "GripSocket") ?? tip).position, axis).magnitude * 100f).ToString("F1", Inv) + " cm" : "")
                                + (thumbTip != null ? " · thumb tip to shaft " + (Vector3.Cross(thumbTip.position - (Child(brushRoot, "GripSocket") ?? tip).position, axis).magnitude * 100f).ToString("F1", Inv) + " cm" : "")
                                + " · knuckle to shaft " + (Vector3.Cross(middle.position - (Child(brushRoot, "GripSocket") ?? tip).position, axis).magnitude * 100f).ToString("F1", Inv) + " cm"
                                + ThumbLine(nearRoot, hand, axis, v)
                                + " · baked roll " + gesture.NearBakedRoll308.ToString("F0", Inv) + " twist " + gesture.NearBakedTwist308.ToString("F0", Inv) + " deg"
                                + WrapLine(nearRoot, (Child(brushRoot, "GripSocket") ?? tip).position, axis)
                                + " · hand on screen (" + (handScreen.x / W).ToString("F2", Inv) + ", " + (handScreen.y / H).ToString("F2", Inv) + ")";
                        }
                        b.AppendLine(line);
                        index++;
                    }
                b.AppendLine("  stills: " + folder);
            }
            catch (Exception e) { b.AppendLine("  exception: " + (e.InnerException ?? e).GetType().Name + " " + (e.InnerException ?? e).Message + " @ " + ((e.InnerException ?? e).StackTrace ?? "").Split('\n')[0]); }
            finally
            {
                try { show.Invoke(gesture, new object[] { false }); } catch { }
                flag.SetValue(gesture, false);
                nearRoot.SetPositionAndRotation(rootPosition, rootRotation); brushRoot.SetPositionAndRotation(brushPosition, brushRotation);
                Object.DestroyImmediate(camObject); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            return b.ToString();
        }

        static string ProfileCommand()
        {
            var log = new StringBuilder("GreatBrush308 profile\n");
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            if (WriteProfile(false, gesture, log, out _) != null && gesture != null) gesture.RefreshGreatBrush308();
            return log.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- install / revert

        static T Field<T>(Object owner, string name) where T : Object
        {
            var property = new SerializedObject(owner).FindProperty(name);
            return property != null ? property.objectReferenceValue as T : null;
        }

        static Renderer[] Renderers(Object owner, string name)
        {
            var property = new SerializedObject(owner).FindProperty(name);
            var list = new List<Renderer>();
            if (property != null && property.isArray)
                for (int i = 0; i < property.arraySize; i++) list.Add(property.GetArrayElementAtIndex(i).objectReferenceValue as Renderer);
            return list.ToArray();
        }

        static void SetRenderers(Object owner, string name, Renderer[] renderers)
        {
            var so = new SerializedObject(owner); var property = so.FindProperty(name);
            if (property == null || !property.isArray) return;
            property.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Transform Child(Transform root, string name) => root != null ? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) : null;

        static Transform MakeBrush(GameObject model, string name, Transform like)
        {
            Vector3 scale = like.parent != null ? like.parent.lossyScale : Vector3.one;
            if (Mathf.Abs(scale.x - scale.y) > .00001f || Mathf.Abs(scale.y - scale.z) > .00001f || scale.x <= 0f) throw new Exception("the brush's parent needs a positive uniform scale");
            GameObject brush = Object.Instantiate(model, like.parent); brush.name = name;
            brush.transform.localPosition = like.localPosition; brush.transform.localRotation = like.localRotation;
            brush.transform.localScale = Vector3.one / scale.x;   // unit world scale (the rig's brush contract)
            foreach (var animator in brush.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
            foreach (var t in brush.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = like.gameObject.layer;
            foreach (var renderer in brush.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterials = MaterialsFor(renderer);
            brush.AddComponent<GreatBrushTail308>();       // the tail ornament hangs by its weight (v6 bones Tail_01..03)
            brush.AddComponent<GreatBrushBristles308>();   // binds its bones by name when it wakes; it is not a BrushBristleRig (six bones, one chain)
            Undo.RegisterCreatedObjectUndo(brush, "Install great brush 308");
            return brush.transform;
        }

        static string Install(bool save)
        {
            var log = new StringBuilder("GreatBrush308 install" + (save ? " (save)" : "") + "\n");
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
            if (gesture == null || walker == null) return "refused: the open scenes have no player (WorldMacroPlayerGestureRig / WorldMacroCombatWalker)";
            var appearance = gesture.GetComponent<WorldMacroPlayerAppearance>();
            var animator = gesture.GetComponent<Animator>();
            if (appearance == null || appearance.Profile == null || animator == null) return "refused: the player has no appearance profile / Animator";
            Transform oldWorld = Field<Transform>(gesture, "_worldBrushRoot"), nearRoot = Field<Transform>(gesture, "_nearRoot"), oldNear = Field<Transform>(gesture, "_nearBrushRoot");
            if (oldWorld == null || nearRoot == null || oldNear == null) return "refused: the gesture rig has no world brush / close-up arm / close-up brush to stand next to";
            if (oldWorld.name == WorldName || Child(gesture.transform.root, WorldName) != null) return "refused: already installed (revert first)";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            if (model == null) return "refused: brush model not imported (import)";
            if (MaterialNames.Any(n => AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(n)) == null)) return "refused: brush materials missing (materials)";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            var names = ProfileOrDefaults(out bool temporary);
            string layer = names.Layer, idleA = names.StanceIdle, idleB = names.CarryIdle;
            if (temporary) Object.DestroyImmediate(names);
            if (controller == null || controller.layers.All(l => l.name != layer)) return "refused: the controller copy with layer " + layer + " is missing (controller)";
            if (LoadClip(idleA) == null || LoadClip(idleB) == null) return "refused: the two idle clips (" + idleA + ", " + idleB + ") are not imported - without them there is no pose to hold the brush in";
            if (!File.Exists(GripJson)) return "refused: grip.json missing - the hand-to-brush transform is unknown";

            var record = new Record
            {
                utc = DateTime.UtcNow.ToString("o"), scene = gesture.gameObject.scene.path, oldWorldBrush = oldWorld.name, oldNearBrush = oldNear.name,
                appearanceProfile = AssetDatabase.GetAssetPath(appearance.Profile), previousController = AssetDatabase.GetAssetPath(appearance.Profile.Controller)
            };

            // the Resources profile first: nothing in the scene is touched when the grip cannot be computed
            var profile = WriteProfile(true, gesture, log, out record.greatProfileCreated);
            if (profile == null) return log.Append("refused: the great brush profile could not be written (see above); the scene was not changed").ToString();
            var swingProfile = AssetDatabase.LoadAssetAtPath<SpellSwingProfile308>(SwingProfilePath);
            if (swingProfile == null)
            {
                swingProfile = ScriptableObject.CreateInstance<SpellSwingProfile308>();
                AssetDatabase.CreateAsset(swingProfile, SwingProfilePath); AssetDatabase.SaveAssetIfDirty(swingProfile);
                record.swingProfileCreated = true;
                log.AppendLine("  swing rule: created " + SwingProfilePath + " (class defaults) - the swing rule is ON from now");
            }
            else log.AppendLine("  swing rule: " + SwingProfilePath + " already there (kept)");

            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Install great brush 308");
            try
            {
                Transform world = MakeBrush(model, WorldName, oldWorld), near = MakeBrush(model, NearName, oldNear);
                // where the close-up hand holds the brush (the rig places it from the profile's NearGripAlongShaft at every bind)
                Transform nearSocket = Child(near, GreatBrushBristles308.GripName);
                if (nearSocket == null || Child(world, GreatBrushBristles308.TipName) == null) throw new Exception("the brush model has no GripSocket / TipSocket");
                var nearGrip = new GameObject(WorldMacroPlayerGestureRig.NearGripName308).transform;
                nearGrip.SetParent(nearSocket.parent, false);
                nearGrip.localPosition = nearSocket.localPosition; nearGrip.localRotation = nearSocket.localRotation; nearGrip.localScale = nearSocket.localScale;
                nearGrip.gameObject.layer = near.gameObject.layer;

                Renderer[] worldRenderers = world.GetComponentsInChildren<Renderer>(true), nearArm = Renderers(gesture, "_nearRenderers");
                Undo.RecordObject(oldWorld.gameObject, "Install great brush 308"); Undo.RecordObject(oldNear.gameObject, "Install great brush 308");
                oldWorld.gameObject.SetActive(false); oldNear.gameObject.SetActive(false);   // switched off, not deleted
                Undo.RecordObject(gesture, "Install great brush 308");
                gesture.ConfigureWorldBrush(world);
                gesture.ConfigureNearArm(nearRoot, near, nearGrip, null, nearArm);

                // the world brush belongs to the body's renderers (hidden while the close-up shows); the close-up brush does not
                Undo.RecordObject(walker, "Install great brush 308");
                walker.Visuals = (walker.Visuals ?? Array.Empty<Renderer>()).Concat(worldRenderers).Distinct().ToArray();
                if (walker.CameraRig != null)
                {
                    Undo.RecordObject(walker.CameraRig, "Install great brush 308");
                    SetRenderers(walker.CameraRig, "_bodyRenderers", Renderers(walker.CameraRig, "_bodyRenderers").Concat(worldRenderers).Distinct().ToArray());
                }
                Undo.RecordObject(appearance, "Install great brush 308");
                SetRenderers(appearance, "_worldRenderers", Renderers(appearance, "_worldRenderers").Concat(worldRenderers).Distinct().ToArray());

                var swing = gesture.GetComponent<SpellSwing308>();
                if (swing == null) { swing = Undo.AddComponent<SpellSwing308>(gesture.gameObject); record.swingComponentAdded = true; }
                swing.Animator = animator; swing.Session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>(); swing.Profile = null;   // the profile comes from Resources: deleting it turns everything off

                // the controller copy (the same states plus the brush layers at weight 0): the profile asset is shared by every scene that uses it
                Undo.RecordObject(appearance.Profile, "Install great brush 308");
                appearance.Profile.Controller = controller;
                if (animator.runtimeAnimatorController != null && AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) == record.previousController)
                { Undo.RecordObject(animator, "Install great brush 308"); animator.runtimeAnimatorController = controller; record.animatorControllerSwitched = true; }

                EditorUtility.SetDirty(gesture); EditorUtility.SetDirty(walker); EditorUtility.SetDirty(appearance); EditorUtility.SetDirty(swing);
                EditorUtility.SetDirty(appearance.Profile); EditorUtility.SetDirty(animator);
                if (walker.CameraRig != null) EditorUtility.SetDirty(walker.CameraRig);
                bool bound = gesture.RefreshGreatBrush308();
                if (bound && !gesture.GreatBrushBound308)
                {
                    // the profile was created in this same command: make sure Resources can see it, then look once more
                    AssetDatabase.ImportAsset(GreatProfilePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    bound = gesture.RefreshGreatBrush308();
                }
                log.AppendLine("  scene: " + WorldName + " + " + NearName + " created, " + record.oldWorldBrush + " / " + record.oldNearBrush + " switched off · rig bound " + bound
                    + (bound ? "" : " (" + gesture.BindingError + ")") + " · great brush bound " + gesture.GreatBrushBound308);
                log.AppendLine("  appearance profile " + record.appearanceProfile + ": controller " + record.previousController + " -> " + Controller);
                if (!bound) throw new Exception("the rig did not bind: " + gesture.BindingError);
                if (!gesture.GreatBrushBound308) throw new Exception("the rig did not bind the great brush: " + gesture.GreatBrushWhy308);
                if (gesture.NearGripError308 != null) throw new Exception("the close-up grip node was not placed: " + gesture.NearGripError308);
                Undo.CollapseUndoOperations(group);
            }
            catch (Exception e)
            {
                Undo.RevertAllDownToGroup(group);
                if (record.greatProfileCreated) AssetDatabase.DeleteAsset(GreatProfilePath);
                if (record.swingProfileCreated) AssetDatabase.DeleteAsset(SwingProfilePath);
                gesture.RefreshGreatBrush308();
                return log.Append("FAILED and rolled back: " + e.Message).ToString();
            }

            File.WriteAllText(Abs(RecordPath), JsonUtility.ToJson(record, true));
            AssetDatabase.ImportAsset(RecordPath);
            var scene = gesture.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssetIfDirty(appearance.Profile);   // only this asset: other sessions' dirty assets are not flushed
            if (save) log.AppendLine("  saved scene " + scene.path + ": " + EditorSceneManager.SaveScene(scene));
            else log.AppendLine("  scene " + scene.path + " is dirty, NOT saved (install:save saves it)");
            log.Append("next: check, then look at the close-up in a preview before Play (SPEC 4a)");
            return log.ToString();
        }

        static string Revert(bool save)
        {
            var log = new StringBuilder("GreatBrush308 revert" + (save ? " (save)" : "") + "\n");
            Record record = File.Exists(Abs(RecordPath)) ? JsonUtility.FromJson<Record>(File.ReadAllText(Abs(RecordPath))) : null;
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
            if (gesture != null)
            {
                Transform world = Child(gesture.transform.root, WorldName), near = Child(gesture.transform.root, NearName);
                Transform nearRoot = Field<Transform>(gesture, "_nearRoot");
                Transform oldWorld = world != null && world.parent != null ? world.parent.Find(record != null ? record.oldWorldBrush : "C02_WorldBrush") : null;
                Transform oldNear = near != null && near.parent != null ? near.parent.Find(record != null ? record.oldNearBrush : "C02_NearBrush") : null;
                if (world != null || near != null)
                {
                    if (oldWorld == null || oldNear == null) return log.Append("refused: the old brushes were not found next to the new ones - nothing changed").ToString();
                    Renderer[] added = world != null ? world.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
                    oldWorld.gameObject.SetActive(true); oldNear.gameObject.SetActive(true);
                    gesture.ConfigureWorldBrush(oldWorld);
                    gesture.ConfigureNearArm(nearRoot, oldNear, null, null, Renderers(gesture, "_nearRenderers"));
                    if (walker != null)
                    {
                        walker.Visuals = (walker.Visuals ?? Array.Empty<Renderer>()).Where(r => !added.Contains(r)).ToArray();
                        if (walker.CameraRig != null) { SetRenderers(walker.CameraRig, "_bodyRenderers", Renderers(walker.CameraRig, "_bodyRenderers").Where(r => !added.Contains(r)).ToArray()); EditorUtility.SetDirty(walker.CameraRig); }
                        EditorUtility.SetDirty(walker);
                    }
                    var appearance = gesture.GetComponent<WorldMacroPlayerAppearance>();
                    if (appearance != null) { SetRenderers(appearance, "_worldRenderers", Renderers(appearance, "_worldRenderers").Where(r => !added.Contains(r)).ToArray()); EditorUtility.SetDirty(appearance); }
                    if (world != null) Object.DestroyImmediate(world.gameObject);
                    if (near != null) Object.DestroyImmediate(near.gameObject);
                    log.AppendLine("  scene: old brushes on, " + WorldName + " / " + NearName + " removed");
                }
                else log.AppendLine("  scene: no great brush objects");
                var swing = gesture.GetComponent<SpellSwing308>();
                if (swing != null && (record == null || record.swingComponentAdded)) { Object.DestroyImmediate(swing); log.AppendLine("  SpellSwing308 removed"); }
                var animator = gesture.GetComponent<Animator>();
                var previous = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(record != null && !string.IsNullOrEmpty(record.previousController) ? record.previousController : LiveController);
                var appearanceNow = gesture.GetComponent<WorldMacroPlayerAppearance>();
                if (previous != null && appearanceNow != null && appearanceNow.Profile != null && AssetDatabase.GetAssetPath(appearanceNow.Profile.Controller) == Controller)
                {
                    appearanceNow.Profile.Controller = previous; EditorUtility.SetDirty(appearanceNow.Profile); AssetDatabase.SaveAssetIfDirty(appearanceNow.Profile);
                    log.AppendLine("  appearance profile: controller back to " + AssetDatabase.GetAssetPath(previous));
                }
                if (previous != null && animator != null && animator.runtimeAnimatorController != null && AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) == Controller)
                { animator.runtimeAnimatorController = previous; EditorUtility.SetDirty(animator); }
                EditorUtility.SetDirty(gesture);
            }
            else log.AppendLine("  scene: no player in the open scenes (only the assets are reverted)");

            // the two Resources profiles: without them the game is the one before this stage (the swing profile only if install made it)
            if (AssetDatabase.LoadAssetAtPath<Object>(GreatProfilePath) != null)
            {
                // a profile that was there before install (tuned by hand) is kept: without the installed brush the rig runs the old pass anyway (PR #30 review)
                if (record == null || record.greatProfileCreated) { AssetDatabase.DeleteAsset(GreatProfilePath); log.AppendLine("  deleted " + GreatProfilePath); }
                else log.AppendLine("  kept " + GreatProfilePath + " (it existed before install)");
            }
            if (AssetDatabase.LoadAssetAtPath<Object>(SwingProfilePath) != null)
            {
                if (record == null || record.swingProfileCreated) { AssetDatabase.DeleteAsset(SwingProfilePath); log.AppendLine("  deleted " + SwingProfilePath + " (the swing rule is off again)"); }
                else log.AppendLine("  kept " + SwingProfilePath + " (it existed before install)");
            }
            if (AssetDatabase.IsValidFolder(ResourcesFolder) && AssetDatabase.FindAssets("", new[] { ResourcesFolder }).Length == 0) AssetDatabase.DeleteAsset(ResourcesFolder);
            if (File.Exists(Abs(RecordPath))) AssetDatabase.DeleteAsset(RecordPath);
            if (gesture != null)
            {
                bool bound = gesture.RefreshGreatBrush308();
                var scene = gesture.gameObject.scene;
                EditorSceneManager.MarkSceneDirty(scene);
                log.AppendLine("  rig bound " + bound + (bound ? "" : " (" + gesture.BindingError + ")") + " · great brush bound " + gesture.GreatBrushBound308);
                if (save) log.AppendLine("  saved scene " + scene.path + ": " + EditorSceneManager.SaveScene(scene));
                else log.AppendLine("  scene " + scene.path + " is dirty, NOT saved (revert:save saves it)");
            }
            log.Append("imported models, clips, materials and the controller copy stay in " + Folder + " (unused without the profiles)");
            return log.ToString();
        }

        // ---------------------------------------------------------------- check

        static string Check()
        {
            var b = new StringBuilder("GreatBrush308 check\n"); int fail = 0, warn = 0;
            void Line(bool ok, string text, bool warning = false) { if (!ok) { if (warning) warn++; else fail++; } b.AppendLine("  " + (ok ? "ok   " : warning ? "WARN " : "FAIL ") + text); }
            var json = LoadGrip(out string gripError);
            Line(json != null, json != null ? "grip.json read" : gripError);

            // brush model
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BrushModel);
            Line(model != null, "brush model imported " + BrushModel);
            if (model != null)
            {
                var transforms = model.GetComponentsInChildren<Transform>(true);
                bool Has(string n) => transforms.Any(t => t.name == n);
                var bones = new List<string> { "BrushHandle" };
                for (int i = 1; i <= GreatBrushBristles308.CentreCount; i++) bones.Add(GreatBrushBristles308.CentrePrefix + i.ToString("00"));
                for (int r = 0; r < GreatBrushBristles308.RingCount; r++) for (int j = 1; j <= GreatBrushBristles308.RingLength; j++) bones.Add(GreatBrushBristles308.RingPrefix + (char)('A' + r) + "_" + j.ToString("00"));
                var lost = bones.Where(n => !Has(n)).ToArray();
                Line(lost.Length == 0, "bones " + (bones.Count - lost.Length) + "/" + bones.Count + (lost.Length > 0 ? " missing " + string.Join(",", lost.Take(6)) : ""));
                var sockets = new[] { "GripSocket", "GripSocketL", "TipSocket", "ShaftEndSocket" };
                Line(sockets.All(Has), "sockets " + string.Join(",", sockets.Where(Has)));
                var scaled = transforms.Where(t => (t.lossyScale - Vector3.one).sqrMagnitude > .000001f).Select(t => t.name).ToArray();
                Line(scaled.Length == 0, "unit scale on every transform" + (scaled.Length > 0 ? " - NOT on " + string.Join(",", scaled.Take(6)) + " (" + scaled.Length + ")" : ""));
                foreach (string shape in new[] { GreatBrushBristles308.SplayShape, GreatBrushBristles308.PressShape })
                {
                    var skin = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.sharedMesh != null && s.sharedMesh.GetBlendShapeIndex(shape) >= 0);
                    Line(skin != null, "blendshape " + shape + (skin != null ? " on " + skin.name : " missing"));
                    if (skin != null) { float w = skin.GetBlendShapeWeight(skin.sharedMesh.GetBlendShapeIndex(shape)); Line(Mathf.Abs(w) < .0001f, "blendshape " + shape + " default weight " + w.ToString("F2", Inv) + " (must be 0)"); }
                }
                GameObject copy = Object.Instantiate(model); copy.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var bristles = copy.AddComponent<GreatBrushBristles308>();
                    bool bound = bristles.IsBound || bristles.TryBind();
                    Line(bound, "GreatBrushBristles308 binds" + (bound ? " · ring chains " + bristles.BoundRingChains + "/" + GreatBrushBristles308.RingCount + " · splay " + bristles.HasSplayShape + " press " + bristles.HasPressShape : " - " + bristles.BindingError));
                    if (bound)
                    {
                        float length = bristles.RestTipOffsetInGripLocal.magnitude;
                        Line(Mathf.Abs(length - .95f) < .02f, "GripSocket -> TipSocket " + length.ToString("F3", Inv) + " m (v5: 0.950)", true);
                        Transform socket = Child(copy.transform, GreatBrushBristles308.GripName);
                        Line(Vector3.Dot(socket.up, bristles.ShaftAxisInRoot) > .999f, "GripSocket local +Y along the shaft: dot " + Vector3.Dot(socket.up, bristles.ShaftAxisInRoot).ToString("F4", Inv), true);
                        b.AppendLine("       brush axes in its root: shaft " + bristles.ShaftAxisInRoot.ToString("F3") + " · ring A side " + bristles.AxisXInRoot.ToString("F3") + " · press axis " + bristles.AxisYInRoot.ToString("F3"));
                    }
                }
                finally { Object.DestroyImmediate(copy); }
                int tris = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).Sum(s => s.sharedMesh.triangles.Length / 3) + model.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f => f.sharedMesh.triangles.Length / 3);
                Line(tris <= 6000, "triangles " + tris + " (SPEC AC-6 says <= 6,000; v5 was accepted at 16,100)", true);
            }

            // clips
            Avatar avatar = PlayerAvatar();
            Line(avatar != null && avatar.isValid && avatar.isHuman, "player avatar valid humanoid (" + PlayerModel + ")");
            foreach (string name in ClipNames)
            {
                var clip = LoadClip(name);
                if (clip == null) { Line(false, "clip " + name + " not imported"); continue; }
                float expected = ClipSecondsJson(At(json, "clips", name));
                bool lengthOk = expected < 0f || Mathf.Abs(clip.length / ClipSpeed308(name, clip) - expected) <= 1f / 30f + .001f;   // the state plays at ClipSpeed308, so the played length is what must match
                bool loopOk = !(At(json, "clips", name, "loop") is bool loop) || loop == clip.isLooping;
                Line(clip.isHumanMotion && lengthOk && loopOk, "clip " + name.PadRight(11) + clip.length.ToString("F3", Inv) + " s" + (expected >= 0f ? " (json " + expected.ToString("F3", Inv) + ")" : " (no json)")
                    + " · loop " + clip.isLooping + " · human " + clip.isHumanMotion);
            }

            // controller copy
            var names = ProfileOrDefaults(out bool temporary);
            try
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
                Line(controller != null, "controller copy " + Controller);
                if (controller != null)
                {
                    var layers = controller.layers;
                    int upper = Array.FindIndex(layers, l => l.name == names.Layer), body = Array.FindIndex(layers, l => l.name == names.BodyLayer);
                    Line(upper >= 0, "layer " + names.Layer + (upper >= 0 ? " at " + upper + " · mask " + (layers[upper].avatarMask != null) + " · states " + layers[upper].stateMachine.states.Length : " missing"));
                    Line(body >= 0 || string.IsNullOrEmpty(names.BodyLayer), "layer " + names.BodyLayer + (body >= 0 ? " at " + body + " · states " + layers[body].stateMachine.states.Length : " missing"));
                    if (upper >= 0)
                    {
                        var empty = layers[upper].stateMachine.states.Where(s => s.state.name != "Empty" && s.state.motion == null).Select(s => s.state.name).ToArray();
                        Line(empty.Length == 0, "every brush state has a clip" + (empty.Length > 0 ? " - NOT " + string.Join(",", empty) : ""));
                        int reaction = Array.FindIndex(layers, l => l.name == "Reaction303");
                        Line(reaction < 0 || reaction > Mathf.Max(upper, body), "hit reactions stay above the brush layers (Reaction303 at " + reaction + ")");
                    }
                    Line(controller.parameters.Any(p => p.name == names.TimeParameter) && controller.parameters.Any(p => p.name == names.ShiftParameter), "parameters " + names.TimeParameter + " / " + names.ShiftParameter);
                    var live = AssetDatabase.LoadAssetAtPath<AnimatorController>(LiveController);
                    if (live != null) Line(live.layers.All(l => l.name != names.Layer), "the live controller has no " + names.Layer + " layer (it must stay untouched)");
                }
            }
            finally { if (temporary) Object.DestroyImmediate(names); }

            // profiles
            var profile = AssetDatabase.LoadAssetAtPath<GreatBrush308Profile>(GreatProfilePath);
            if (profile == null) b.AppendLine("  --   no " + GreatProfilePath + " (not installed: the game runs as before)");
            else
            {
                Line(profile.IsValid, "great brush profile valid · states " + profile.States.Count(s => s.ClipSeconds > 0f) + "/" + profile.States.Length);
                var log = new StringBuilder();
                if (ComputeGrip(profile, log, out var grip, out Vector3 leftHold))
                {
                    float position = Vector3.Distance(grip.Position, profile.TwoHandGrip.Position), angle = Quaternion.Angle(grip.Rotation, profile.TwoHandGrip.Rotation);
                    Line(position < .001f && angle < .1f, "profile grip equals the grip computed now (" + (position * 1000f).ToString("F2", Inv) + " mm, " + angle.ToString("F2", Inv) + " deg)", true);
                }
                b.Append(log.ToString().Replace("  grip:", "       grip:"));
                Line(AssetDatabase.LoadAssetAtPath<SpellSwingProfile308>(SwingProfilePath) != null, "swing rule profile present (" + SwingProfilePath + ")", true);
                var swingProfile = AssetDatabase.LoadAssetAtPath<SpellSwingProfile308>(SwingProfilePath);
                if (swingProfile != null)
                    foreach (var entry in new[] { swingProfile.AttackSingle, swingProfile.AttackArea, swingProfile.Summon, swingProfile.Field, swingProfile.Install, swingProfile.Parry, swingProfile.Ward, swingProfile.Buff })
                    {
                        string state = profile.Resolve(entry.State);
                        Line(string.IsNullOrEmpty(state) || profile.Find(state) >= 0, "swing state " + entry.State + " -> " + state, true);
                    }
            }

            // scene
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            if (gesture == null) b.AppendLine("  --   no player in the open scenes");
            else
            {
                if (!Application.isPlaying) gesture.TryBind();   // edit mode: a script reload drops the bound brush data
                Transform world = Field<Transform>(gesture, "_worldBrushRoot");
                bool installed = world != null && world.name == WorldName;
                b.AppendLine("  --   scene " + gesture.gameObject.scene.name + " · installed " + installed);
                if (installed)
                {
                    var walker = Object.FindFirstObjectByType<WorldMacroCombatWalker>();
                    var animator = gesture.GetComponent<Animator>(); var appearance = gesture.GetComponent<WorldMacroPlayerAppearance>();
                    Line((world.lossyScale - Vector3.one).sqrMagnitude < .000001f, "world brush unit world scale " + world.lossyScale.ToString("F4"));
                    Line(world.GetComponent<GreatBrushBristles308>() != null && world.GetComponent<Oheangbu.Presentation.BrushBristleRig>() == null, "world brush has GreatBrushBristles308 and no BrushBristleRig");
                    Line(gesture.GreatBrushBound308, "rig bound the great brush" + (gesture.BindingError != null ? " - " + gesture.BindingError : ""));
                    Line(gesture.GetComponent<SpellSwing308>() != null, "SpellSwing308 on the player");
                    Line(appearance != null && appearance.Profile != null && AssetDatabase.GetAssetPath(appearance.Profile.Controller) == Controller, "appearance profile uses the controller copy");
                    Line(animator != null && animator.avatar == avatar, "the scene Animator's avatar is the avatar the clips were imported with", true);
                    var renderers = world.GetComponentsInChildren<Renderer>(true);
                    Line(walker != null && walker.Visuals != null && renderers.All(r => walker.Visuals.Contains(r)), "world brush renderers are in the walker's body renderers");
                    Line(walker != null && walker.CameraRig != null && renderers.All(r => Renderers(walker.CameraRig, "_bodyRenderers").Contains(r)), "world brush renderers are hidden with the body in the close-up");
                    Transform near = Field<Transform>(gesture, "_nearBrushRoot"), nearGrip = Field<Transform>(gesture, "_nearGripSocket");
                    Line(near != null && near.name == NearName && nearGrip != null && nearGrip.name == WorldMacroPlayerGestureRig.NearGripName308, "close-up brush and its grip node are bound");
                    Transform nearSocket = Child(near, GreatBrushBristles308.GripName);
                    if (nearGrip != null && nearSocket != null && profile != null)
                    {
                        float moved = Vector3.Distance(nearGrip.position, nearSocket.position);
                        Line(gesture.NearGripError308 == null && Mathf.Abs(moved - Mathf.Abs(profile.NearGripAlongShaft)) < .005f,
                            "close-up grip node " + moved.ToString("F3", Inv) + " m from GripSocket (profile " + profile.NearGripAlongShaft.ToString("F3", Inv) + ")"
                            + (gesture.NearGripError308 != null ? " - " + gesture.NearGripError308 : moved < .005f && Mathf.Abs(profile.NearGripAlongShaft) > .005f ? " - NOT PLACED: the hand would be 0.95 m behind the tip, out of the picture" : ""));
                    }
                    string why = gesture.GreatBrushWhy308;
                    if (why != null) b.AppendLine("       great brush not running now: " + why);
                    Line(renderers.All(r => r.sharedMaterials.All(m => m != null)), "world brush materials assigned");
                    b.AppendLine("       player display scale " + gesture.transform.lossyScale.x.ToString("F3", Inv) + " (the brush keeps scale 1: the hand-to-socket offset scales with the body)");
                }
            }
            b.Append(fail == 0 ? "PASS (contract only" + (warn > 0 ? ", " + warn + " warnings" : "") + ") - nothing visual is judged here" : "FAIL " + fail + (warn > 0 ? " · warnings " + warn : ""));
            return b.ToString();
        }
    }
}
