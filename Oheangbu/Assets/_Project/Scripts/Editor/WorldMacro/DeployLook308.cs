using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // SPEC-SPELL-DEPLOY-308 look gates A / B / C (BUILD_PLAN section 2): an in-memory preview of the spell deploy layer.
    // Nothing is saved: every object, mesh, material, texture and ScriptableObject it makes carries HideFlags.DontSave and a
    // "DeployLook308" name, no asset is created, the scene is never saved, no real renderer is disabled and no material of the
    // scene is changed. The tool keeps no state in statics - only in its own scene objects (the State| child).
    // Queue: Oheangbu.EditorTools.WorldMacro.DeployLook308 Run "<command>"
    //   preview:on:<글자>:<beat>[:grade=0|.5|1][:view=1.5|4|10][:seed=n][:flash=full|reduced|off][:tier=pc|mobile][:shape=cone|circle|path|volley]
    //             [:at=x,y,z][:yaw=deg][:age=s][:t=s][:notint][:allowdirty][:hit][:hitage=s][:groggy[=1|2|3]][:onesample]
    //        beat = ignite | birth | burst | hold | melt | residue | impact1 | impact2 | impact3 | flood (with :flood=0..1)
    //        D308-10c, the three cases of one cast (same letter, same place, three captures side by side):
    //          ordinary      preview:on:가:birth              the strokes glow on the cel they are born; no frame, nothing else
    //          hit confirmed preview:on:가:burst:hit          + the ink that bursts out of the hit point (:hitage = seconds after the hit, default .08)
    //          groggy target preview:on:가:burst:hit:groggy   + the impact frame (:groggy=2 / =3 for the later frames; impact1..3 beats imply it)
    //   preview:on:foot:<steps>[:slope=deg][:run][:age=s][:at=x,y,z][:yaw=deg][:allowdirty]
    //   preview:off          remove everything, destroy the memory objects, zero the globals, restore the quality level
    //   status               what is up, the cel timetable, counts, reach against the plan, estimated screen cover, globals
    // The eye camera "DeployLook308_Eye" (disabled, eye height 1.6 m) is what the capture tool renders; scene cameras are not moved.
    public static partial class DeployLook308
    {
        const string RootName = "DeployLook308_Preview", EyeName = "DeployLook308_Eye", Prefix = "DeployLook308_";
        const HideFlags Flags = HideFlags.DontSave;
        const float EyeHeight = 1.6f, BrushReach = 1.6f;

        // A preview left up must not ride into Play: its objects are DontSave (they survive the scene reload) and the quality
        // level would stay raised. It is taken down when the editor leaves Edit mode. Nothing is kept in a static: the
        // subscription is made again on every domain load and the handler looks the preview up by name.
        [InitializeOnLoadMethod]
        static void WatchPlayMode()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChange;
            EditorApplication.playModeStateChanged += OnPlayModeChange;
        }

        static void OnPlayModeChange(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode || GameObject.Find(RootName) == null) return;
            Debug.Log("[DeployLook308] preview removed before Play: " + Teardown(true, out _));
        }

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            try
            {
                switch (a[0])
                {
                    case "preview":
                        if (a.Length > 3 && a[1] == "on" && a[2] == "foot") return Foot(a);
                        if (a.Length > 3 && a[1] == "on") return On(a);
                        if (a.Length > 1 && a[1] == "off") return Off();
                        throw new PostLedger308.Refused("use preview:on:<글자>:<beat>[...], preview:on:foot:<steps>[...] or preview:off");
                    case "status": return Status();
                    default: throw new PostLedger308.Refused("unknown command '" + command + "'");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
        }

        // ---------------------------------------------------------------- shared setup

        sealed class Context
        {
            public Dictionary<string, string> Opt;
            public SpellDeploy308ProfileSO Profile;
            public SpellDeploy308MapSO Map;
            public Texture Atlas;
            public Material Burst, Residue;
            public GameObject Root;
            public Camera Eye;
            public Vector3 Ground, Forward, EyePoint;
            public int PriorQuality;
            public bool DirtyAtOn, GroundFound;
            public StringBuilder Log = new StringBuilder();
        }

        static float Num(Dictionary<string, string> opt, string key, float fallback) =>
            opt.TryGetValue(key, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

        static Context Begin(IEnumerable<string> options)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            var c = new Context { Opt = PostLedger308.Options(options) };
            var scene = SceneManager.GetActiveScene();
            c.DirtyAtOn = scene.isDirty;
            if (scene.isDirty && !c.Opt.ContainsKey("allowdirty")) throw new PostLedger308.Refused("the scene has unsaved changes that are not from this tool (add :allowdirty to preview anyway)");
            // every read happens before the first change
            Shader burstShader = Deploy308.RequireShader(Deploy308.BurstShader), residueShader = Deploy308.RequireShader(Deploy308.ResidueShader);
            var asset = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(Deploy308.ProfilePath);
            var mapAsset = AssetDatabase.LoadAssetAtPath<SpellDeploy308MapSO>(Deploy308.MapPath);
            SpellDeploy308MapSO.Row[] rows = mapAsset != null ? null : Deploy308.ReadMap(out _, out _);
            Texture2D atlasAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(Deploy308.AtlasPath);
            byte[] atlasBytes = null;
            if (atlasAsset == null)
            {
                string png = PostLedger308.RepoPath(Deploy308.GeneratedDir + "/ink_deploy_atlas308.png");
                if (!File.Exists(png)) throw new PostLedger308.Refused("no atlas: neither " + Deploy308.AtlasPath + " nor " + Deploy308.GeneratedDir + "/ink_deploy_atlas308.png (run python Tools/Art/deploy308_atlas.py)");
                atlasBytes = File.ReadAllBytes(png);
            }

            c.PriorQuality = QualitySettings.GetQualityLevel();
            if (FindRoots().Count > 0) { c.Log.AppendLine("replaced an earlier preview: " + Teardown(false, out int carried)); if (carried >= 0) c.PriorQuality = carried; }

            // a private copy of the profile: preview options never touch the asset
            c.Profile = asset != null ? Object.Instantiate(asset) : ScriptableObject.CreateInstance<SpellDeploy308ProfileSO>();
            c.Profile.name = Prefix + "Profile"; c.Profile.hideFlags = Flags; c.Profile.LayerEnabled = true;
            if (mapAsset != null) c.Map = mapAsset;
            else { c.Map = ScriptableObject.CreateInstance<SpellDeploy308MapSO>(); c.Map.name = Prefix + "Map"; c.Map.hideFlags = Flags; c.Map.Rows = rows; }
            c.Profile.Map = c.Map;
            if (atlasAsset != null) c.Atlas = atlasAsset;
            else
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGB24, true, true) { name = Prefix + "Atlas", hideFlags = Flags, wrapMode = TextureWrapMode.Clamp };
                texture.LoadImage(atlasBytes, false);
                c.Atlas = texture;
            }
            c.Burst = new Material(burstShader) { name = Prefix + "Burst", hideFlags = Flags };
            Deploy308.ApplyBurstMaterial(c.Burst, c.Profile, c.Atlas);
            c.Residue = new Material(residueShader) { name = Prefix + "Residue", hideFlags = Flags };
            Deploy308.ApplyResidueMaterial(c.Residue, c.Atlas);
            c.Residue.enableInstancing = false;   // the preview draws one renderer per mark
            c.Profile.BurstMaterial = c.Burst; c.Profile.ResidueMaterial = c.Residue; c.Profile.Atlas = c.Atlas as Texture2D;
            c.Profile.ImpactShader = Shader.Find(Deploy308.ImpactShader); c.Profile.FlatShader = Shader.Find(Deploy308.FlatShader);
            if (c.Profile.FloodMask == null)
            {
                string flood = PostLedger308.RepoPath(Deploy308.GeneratedDir + "/ink_flood_mask308.png");
                if (File.Exists(flood))
                {
                    var mask = new Texture2D(2, 2, TextureFormat.RGB24, true, true) { name = Prefix + "Flood", hideFlags = Flags, wrapMode = TextureWrapMode.Repeat };
                    mask.LoadImage(File.ReadAllBytes(flood), false);
                    c.Profile.FloodMask = mask;
                }
            }

            // where the eye stands: :at / :yaw, else the scene view pivot looking along the scene view
            var view = SceneView.lastActiveSceneView;
            Vector3 at = view != null ? view.pivot : Vector3.zero;
            if (c.Opt.TryGetValue("at", out var atText))
            {
                var p = atText.Split(',');
                if (p.Length != 3 || !float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out at.x) || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out at.y)
                    || !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out at.z)) throw new PostLedger308.Refused("at must be x,y,z");
            }
            float yaw = Num(c.Opt, "yaw", view != null ? view.rotation.eulerAngles.y : 0f);
            c.Forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            c.GroundFound = Physics.Raycast(at + Vector3.up * 40f, Vector3.down, out var hit, 120f, c.Profile.Residue.GroundLayers, QueryTriggerInteraction.Ignore) && hit.normal.y >= c.Profile.Residue.MinNormalY;
            c.Ground = c.GroundFound ? hit.point : at;   // no ground collider: the flat plane through the given point
            c.EyePoint = c.Ground + Vector3.up * EyeHeight;
            return c;
        }

        static void MakeRoot(Context c, float fov)
        {
            c.Root = Make(RootName, null);
            var eye = Make(EyeName, c.Root.transform);
            eye.transform.SetPositionAndRotation(c.EyePoint, Quaternion.LookRotation(c.Forward, Vector3.up));
            c.Eye = eye.AddComponent<Camera>();
            c.Eye.enabled = false; c.Eye.fieldOfView = fov; c.Eye.nearClipPlane = .05f; c.Eye.farClipPlane = 2000f;
            var data = eye.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
        }

        static void Finish(Context c, string state, bool raiseQuality)
        {
            if (raiseQuality)
            {
                int pc = Array.IndexOf(QualitySettings.names, "PC");
                if (pc >= 0 && pc != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(pc, true);
            }
            Make("State|q=" + c.PriorQuality + "|" + state + "|dirtyAtOn=" + c.DirtyAtOn + "|utc=" + PostLedger308.Utc(), c.Root.transform);
            SceneView.RepaintAll();
        }

        static void Fail(Context c)
        {
            if (c == null) return;
            Teardown(false, out _);
            foreach (Object o in new Object[] { c.Burst, c.Residue }) if (o != null) Object.DestroyImmediate(o);
            DestroyNamed();
            if (QualitySettings.GetQualityLevel() != c.PriorQuality) QualitySettings.SetQualityLevel(c.PriorQuality, true);
        }

        // ---------------------------------------------------------------- burst / residue / impact preview

        static string On(string[] a)
        {
            string glyph = a[2], beat = a[3].ToLowerInvariant();
            if (Forms2Beat(beat)) return OnForms2(a);   // #308 forms2: guard / fence / emerge / buff* / aura / fieldmark / loose / comet (DeployLook308.Forms2.cs)
            if (glyph.Length != 1) throw new PostLedger308.Refused("give one letter, e.g. preview:on:가:burst");
            string[] beats = { "ignite", "birth", "burst", "hold", "melt", "residue", "impact1", "impact2", "impact3", "flood" };
            if (Array.IndexOf(beats, beat) < 0) throw new PostLedger308.Refused("beat must be one of " + string.Join(" ", beats));
            Context c = null;
            try
            {
                c = Begin(a.Skip(4));
                if (!c.Map.TryGet(glyph[0], out var row)) throw new PostLedger308.Refused("'" + glyph + "' is not one of the 120 letters");
                float grade = Mathf.Clamp01(Num(c.Opt, "grade", .5f)), view = Mathf.Clamp(Num(c.Opt, "view", 4f), .8f, 60f);
                int seed = (int)Num(c.Opt, "seed", 308f);
                bool mobile = c.Opt.TryGetValue("tier", out var tierText) && tierText.Equals("mobile", StringComparison.OrdinalIgnoreCase);
                string flash = c.Opt.TryGetValue("flash", out var flashText) ? flashText.ToLowerInvariant() : "full";
                if (c.Opt.ContainsKey("notint")) c.Profile.Stroke.TintMax = 0f;   // the black-and-white sheet of AC-D6
                var tier = mobile ? DeployTier308.Mobile : DeployTier308.PC;
                if (mobile && c.Profile.AtlasMobile != null) { c.Burst.SetTexture("_Atlas", c.Profile.AtlasMobile); c.Residue.SetTexture("_Atlas", c.Profile.AtlasMobile); }

                MakeRoot(c, Num(c.Opt, "fov", 60f));
                // the cast as the adapter would hand it over: brush tip in front of the eye, target `view` metres away at chest height
                Vector3 origin = c.EyePoint + c.Forward * BrushReach - Vector3.up * .25f;
                Vector3 target = c.Ground + c.Forward * view + Vector3.up * 1.1f;
                bool playerAnchored = row.Category == DeployCategory308.Summon || row.Category == DeployCategory308.Field;
                if (playerAnchored) origin = c.Ground + Vector3.up * .05f;
                AreaImpactPlan plan = null;
                if (row.Category == DeployCategory308.AttackArea)
                {
                    string shape = c.Opt.TryGetValue("shape", out var shapeText) ? shapeText.ToLowerInvariant() : row.Element == Element.Fire ? "cone" : "circle";
                    plan = new AreaImpactPlan { Direction = c.Forward, Delay = .25f, VisualSeed = seed };
                    switch (shape)
                    {
                        case "cone": plan.Shape = AreaShape.Cone; plan.Point = c.Ground; plan.Angle = 30f; plan.Length = Mathf.Max(3f, view); break;
                        case "path": plan.Shape = AreaShape.Path; plan.Point = c.Ground + c.Forward; plan.Radius = 1.2f; plan.Length = Mathf.Max(4f, view); plan.Speed = 9f; break;
                        case "volley":
                            plan.Shape = AreaShape.Volley; plan.Point = c.Ground + c.Forward * view; plan.ShotCount = 5; plan.ShotInterval = .12f;
                            for (int i = 0; i < 5; i++)
                                plan.Shots.Add(new PlannedHit { HasImpactPoint = true, ImpactPoint = c.Ground + c.Forward * view + Quaternion.Euler(0f, i * 72f, 0f) * Vector3.right * 1.2f + Vector3.up * .1f,
                                    ImpactTime = plan.CreatedAt + .25f + i * .12f });
                            break;
                        case "circle": plan.Shape = AreaShape.Circle; plan.Point = c.Ground + c.Forward * view; plan.Radius = 3f; break;
                        default: throw new PostLedger308.Refused("shape must be cone, circle, path or volley");
                    }
                }
                var holder = Make("Burst_" + glyph, c.Root.transform);
                holder.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(c.Forward, Vector3.up));
                var runtime = holder.AddComponent<InkDeployRuntime308>();
                var cast = new DeployCast308
                {
                    Profile = c.Profile, Row = row, Origin = origin, FallbackPoint = target, Plan = plan, ImpactClock = row.Category == DeployCategory308.AttackSingle ? .5f : 0f,
                    Grade01 = grade, Tint = PreviewTint(row.Element), Seed = seed, Tier = tier, CameraPosition = c.EyePoint, HasCameraPosition = true,
                    HoldSeconds = row.Category == DeployCategory308.Ward ? 6f : 0f, WardRadius = row.Category == DeployCategory308.Ward ? 3f : 0f, WardHeight = row.Category == DeployCategory308.Ward ? 2.2f : 0f,
                    NewForms = true,   // #308 forms2: the preview shows what the effect seam (hook A) draws for this row
                };
                if (!runtime.Configure(cast, null)) throw new PostLedger308.Refused("the runtime refused the cast");
                float cel = runtime.CelSeconds;
                float burstEnd = (Mathf.Max(runtime.Stats.LastBirthCel, runtime.ImpactCel) + .5f) * cel;
                // a column (summon / ward) is born on one cel and then rises: its burst beat is the last cel of the rise
                if (runtime.Stats.HasColumn) burstEnd = (runtime.ImpactCel + Mathf.Max(1, Mathf.RoundToInt(c.Profile.Beats.Burst / cel)) - .5f) * cel;
                // residue: the burst has melted away, only the ground marks are left
                // birth: the burst's first cel - the main strokes are born on it, so their momentary glow is at its full value
                float time = beat == "ignite" ? c.Profile.Beats.Ignite * .5f : beat == "birth" ? (runtime.ImpactCel + .5f) * cel : beat == "hold" ? (burstEnd + runtime.HoldEnd) * .5f
                    : beat == "melt" ? (runtime.HoldEnd + runtime.MeltEnd) * .5f : beat == "residue" ? runtime.MeltEnd + cel : burstEnd;
                time = Num(c.Opt, "t", time);   // :t=<seconds> samples any moment of the timeline
                // forms3 (D13): a comet's advance is written frame by frame while it flies (InkDeployRuntime308.FollowComet) and is
                // never written again after its impact. One sample at a moment after the impact left it at 0: the whole burst was
                // drawn at the brush tip, 1.6 m from the eye, and filled the view. The preview now plays the flight the way the
                // game does - a sample every 1/60 s from 0 - before it takes the asked moment (:onesample = the old single sample).
                int stepped = 0;
                if (runtime.Stats.Comet && c.Profile.Comet.PerFrame && !c.Opt.ContainsKey("onesample"))
                    for (float f = 1f / 60f; f < time; f += 1f / 60f) { runtime.Sample(f); stepped++; }
                runtime.Sample(time);

                int marks = 0;
                float age = Num(c.Opt, "age", 0f);
                if (beat == "residue")
                {
                    // edit mode has no frame updates: every ground mark of the cast becomes one renderer, dried to `age` seconds
                    var quad = InkResidueField308.BuildQuad(); quad.name = Prefix + "Quad";
                    for (int i = 0; i < runtime.DropCount; i++)
                    {
                        var drop = runtime.Drop(i);
                        if (drop.Air) continue;
                        Vector3 world = runtime.Root.TransformPoint(drop.Local) + Vector3.up * .5f;
                        Vector3 normal = Vector3.up, point = new Vector3(world.x, c.Ground.y, world.z);
                        if (Physics.Raycast(world + Vector3.up * .6f, Vector3.down, out var hit, 6f, c.Profile.Residue.GroundLayers, QueryTriggerInteraction.Ignore) && hit.normal.y >= c.Profile.Residue.MinNormalY)
                        { point = hit.point; normal = hit.normal; }
                        bool held = drop.Held || c.Profile.CategoryOf(row.Category).HeldResidue;
                        Mark(c, quad, "Mark_" + i, InkResidueField308.Place(point, normal, c.Forward, drop.Size, drop.Size, c.Profile.Residue.Lift),
                            new Vector4(0f, held ? c.Profile.FieldTail : c.Profile.SpellLife, drop.Cell, c.Profile.Residue.Opacity), new Vector4(0f, 0f, 0f, 0f));
                        marks++;
                    }
                    Shader.SetGlobalFloat("_OhResidueNow", age);
                }

                // D308-10c "hit confirmed": the splash a confirmed hit adds, `hitage` seconds after the hit. Edit mode has no frame
                // updates, so every drop becomes one renderer at the place it has reached (the ones that landed are ground marks).
                bool attackRow = row.Category == DeployCategory308.AttackSingle || row.Category == DeployCategory308.AttackArea;
                bool hitCase = c.Opt.ContainsKey("hit"), groggy = c.Opt.ContainsKey("groggy") || beat.StartsWith("impact", StringComparison.Ordinal);
                if ((hitCase || c.Opt.ContainsKey("groggy")) && !attackRow) throw new PostLedger308.Refused(":hit / :groggy are for attack letters (" + glyph + " is " + row.Category + ")");
                string hitText = "";
                if (hitCase)
                {
                    float hitAge = Mathf.Max(0f, Num(c.Opt, "hitage", .08f));
                    // the struck enemy stands where the cast is aimed: the judged target of a single cast, the plan's focus of an area cast
                    Vector3 hitPoint = plan != null ? InkDeployRuntime308.ImpactPointOf(plan) + Vector3.up * (InkDeployRuntime308.ChestHeight - .6f) : target;
                    float hitGround = c.Ground.y;
                    if (Physics.Raycast(hitPoint + Vector3.up * 2f, Vector3.down, out var under, 8f, c.Profile.Residue.GroundLayers, QueryTriggerInteraction.Ignore) && under.normal.y >= c.Profile.Residue.MinNormalY) hitGround = under.point.y;
                    int composed = runtime.ComposeHit(hitPoint, c.EyePoint, hitGround);
                    var hitQuad = InkResidueField308.BuildQuad(); hitQuad.name = Prefix + "HitQuad";
                    int inAir = 0, standing = 0, landed = 0, puddles = 0;
                    for (int i = 0; i < composed; i++)
                    {
                        var drop = runtime.HitDrop(i);
                        if (!drop.Air)
                        {
                            Mark(c, hitQuad, "Hit_" + i + "_puddle", InkResidueField308.Place(new Vector3(drop.Local.x, hitGround, drop.Local.z), Vector3.up, c.Forward, drop.Size, drop.Size, c.Profile.Residue.Lift),
                                new Vector4(0f, c.Profile.SpellLife, drop.Cell, c.Profile.Residue.Opacity), Vector4.zero);
                            puddles++; continue;
                        }
                        if (beat == "residue") continue;   // the residue beat shows what is left on the ground
                        if (drop.Still)
                        {
                            if (hitAge >= drop.Life) continue;
                            Mark(c, hitQuad, "Hit_" + i + "_still", Matrix4x4.TRS(drop.Local, Quaternion.identity, Vector3.one * drop.Size), new Vector4(0f, drop.Life, drop.Cell, 1f), new Vector4(0f, 0f, 1f, 0f));
                            standing++; continue;
                        }
                        Vector3 at = drop.Local + drop.Velocity * hitAge + Vector3.down * (.5f * c.Profile.Residue.Gravity * hitAge * hitAge);
                        if (at.y <= hitGround)
                        {
                            Mark(c, hitQuad, "Hit_" + i + "_landed", InkResidueField308.Place(new Vector3(at.x, hitGround, at.z), Vector3.up, c.Forward, drop.Size * 1.6f, drop.Size * 1.6f, c.Profile.Residue.Lift),
                                new Vector4(0f, c.Profile.SpellLife, InkBurstMeshBuilder308.CellDrop, c.Profile.Residue.Opacity), Vector4.zero);
                            landed++; continue;
                        }
                        // forms3 (D10): a tailed drop lies along the way it travels, as the residue field draws it
                        var airPose = InkResidueField308.AirPose(c.Profile.AirTail, at, drop.Velocity + Vector3.down * (c.Profile.Residue.Gravity * hitAge), drop.Size, drop.Cell, out bool tailed);
                        Mark(c, hitQuad, "Hit_" + i + "_air", airPose, new Vector4(0f, 1.6f, drop.Cell, 1f), new Vector4(0f, 0f, 1f, tailed ? 1f : 0f));
                        inAir++;
                    }
                    if (beat != "residue") Shader.SetGlobalFloat("_OhResidueNow", hitAge);
                    hitText = " | hit confirmed: " + composed + " drops, " + hitAge.ToString("0.###", CultureInfo.InvariantCulture) + " s after the hit -> " + inAir + " in the air, " + standing + " standing (star / blot), "
                        + landed + " landed, " + puddles + " ground puddle (" + (c.Profile.Hit.GroundSmear * c.Profile.Hit.Scale(c.Profile.GradeIndex(grade))).ToString("0.##", CultureInfo.InvariantCulture) + " m against the plain "
                        + c.Profile.Residue.ImpactSmear.ToString("0.##", CultureInfo.InvariantCulture) + " m)";
                }

                string impact = "";
                if (beat.StartsWith("impact", StringComparison.Ordinal) || beat == "flood" || groggy)
                {
                    var director = c.Root.AddComponent<ImpactFrameDirector308>();
                    director.Configure(c.Profile, tier, null, null, null);
                    if (!director.PassAvailable) throw new PostLedger308.Refused("the impact pass is not available (ImpactFramePass308 / the App asmdef reference are not deployed, or shader " + Deploy308.ImpactShader + " is missing)");
                    var hook = c.Root.AddComponent<ImpactPreviewHook308>();
                    hook.Director = director;
                    if (beat == "flood")
                    {
                        float x = Mathf.Clamp01(Num(c.Opt, "flood", .5f));
                        hook.ShaderPass = ImpactFrameDirector308.PassFlood; hook.FloodCover = Mathf.Clamp01(x / .33f); hook.FloodClear = Mathf.Clamp01((x - .33f) / .67f);
                        impact = " | flood " + x.ToString("0.##", CultureInfo.InvariantCulture) + " (cover " + hook.FloodCover.ToString("0.##", CultureInfo.InvariantCulture) + ", clear " + hook.FloodClear.ToString("0.##", CultureInfo.InvariantCulture) + ")";
                    }
                    else
                    {
                        // a frame number comes from the beat (impact1..3) or from :groggy[=n] on any other beat (default: the first frame)
                        int kind = beat.StartsWith("impact", StringComparison.Ordinal) ? beat[beat.Length - 1] - '0' : Mathf.Clamp(Mathf.RoundToInt(Num(c.Opt, "groggy", 1f)), 1, 3);
                        Vector3 viewport = c.Eye.WorldToViewportPoint(row.Category == DeployCategory308.AttackArea && plan != null ? InkDeployRuntime308.ImpactPointOf(plan) : target);
                        director.ForceFrame(kind, new Vector2(viewport.x, viewport.y), 1f, true);
                        hook.ShaderPass = flash == "off" ? -1 : mobile ? ImpactFrameDirector308.PassLocalInvert : flash == "reduced" ? ImpactFrameDirector308.PassLocal : kind - 1;
                        impact = " | impact frame " + kind + " (shader pass " + hook.ShaderPass + ", flash " + flash + ", impact uv " + viewport.x.ToString("0.###", CultureInfo.InvariantCulture) + "," + viewport.y.ToString("0.###", CultureInfo.InvariantCulture) + ")";
                    }
                }

                Finish(c, "glyph=" + glyph + "|beat=" + beat + "|grade=" + grade.ToString("0.##", CultureInfo.InvariantCulture) + "|view=" + view.ToString("0.##", CultureInfo.InvariantCulture)
                    + "|seed=" + seed + "|tier=" + tier + "|t=" + time.ToString("0.###", CultureInfo.InvariantCulture) + "|hit=" + hitCase + "|groggy=" + groggy, !mobile);
                // which of the three cases this capture shows (D308-10c)
                string which = groggy ? "groggy target (impact frame)" : hitCase ? "hit confirmed (no frame)" : "ordinary cast (no frame, no splash)";
                c.Log.AppendLine("preview on: " + glyph + " " + row.Category + " " + row.Element + " " + row.Frame + " final " + row.Final + " | beat " + beat + " at " + time.ToString("0.###", CultureInfo.InvariantCulture) + " s (cel "
                    + runtime.CurrentCel + ", impact cel " + runtime.ImpactCel + ") | grade " + grade + " | view " + view + " m | tier " + tier + (c.GroundFound ? "" : " | no ground collider: flat plane") + impact
                    + (marks > 0 ? " | " + marks + " ground marks at age " + age + " s" : "")
                    + (runtime.Stats.Comet ? " | comet: " + (stepped > 0 ? "played in " + stepped + " frames before the sample" : "ONE sample") + ", flight advance " + runtime.FlightAdvance.ToString("0.###", CultureInfo.InvariantCulture) : ""));
                c.Log.AppendLine("case: " + which + " | glow amount at this cel " + runtime.GlowNow.ToString("0.###", CultureInfo.InvariantCulture) + " (data " + c.Profile.GlowAmount.ToString("0.###", CultureInfo.InvariantCulture) + " over "
                    + c.Profile.GlowCels + " cels; the burst beat's glow ends at " + runtime.GlowEnd.ToString("0.###", CultureInfo.InvariantCulture) + " s; a stroke born on this cel is at "
                    + c.Profile.GlowFalloff(0f).ToString("0.##", CultureInfo.InvariantCulture) + ", one cel old " + c.Profile.GlowFalloff(1f).ToString("0.##", CultureInfo.InvariantCulture) + ", two "
                    + c.Profile.GlowFalloff(2f).ToString("0.##", CultureInfo.InvariantCulture) + ")" + hitText);
                c.Log.Append(Describe(runtime, plan));
                return c.Log.ToString();
            }
            catch { Fail(c); throw; }
        }

        static Color PreviewTint(Element element)
        {
            switch (element)
            {
                case Element.Wood: return new Color(.30f, .48f, .32f);
                case Element.Fire: return new Color(.72f, .28f, .20f);
                case Element.Earth: return new Color(.62f, .50f, .28f);
                case Element.Metal: return new Color(.66f, .66f, .64f);
                default: return new Color(.24f, .36f, .56f);
            }
        }

        static void Mark(Context c, Mesh quad, string name, Matrix4x4 pose, Vector4 a, Vector4 b)
        {
            var go = Make(name, c.Root.transform);
            go.transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            go.transform.localScale = pose.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = c.Residue; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetVector("_StampA", a); block.SetVector("_StampB", b); block.SetVector("_StampC", new Vector4(0f, 0f, 1f, 1f));
            renderer.SetPropertyBlock(block);
        }

        // ---------------------------------------------------------------- footprints (synthetic walk)

        static string Foot(string[] a)
        {
            if (!int.TryParse(a[3], out int steps) || steps < 1 || steps > 200) throw new PostLedger308.Refused("steps must be 1..200");
            Context c = null;
            try
            {
                c = Begin(a.Skip(4));
                var foot = c.Profile.Foot;
                float slope = Mathf.Clamp(Num(c.Opt, "slope", 0f), 0f, 60f), age = Mathf.Max(0f, Num(c.Opt, "age", 0f));
                bool run = c.Opt.ContainsKey("run");
                MakeRoot(c, 60f);
                // look down and ahead at the trail, the way the player would
                c.Eye.transform.rotation = Quaternion.LookRotation((c.Forward * 3f - Vector3.up * EyeHeight).normalized, Vector3.up);
                Vector3 right = Vector3.Cross(Vector3.up, c.Forward);
                Vector3 normal = Quaternion.AngleAxis(-slope, right) * Vector3.up;        // uphill ahead
                Vector3 along = Vector3.ProjectOnPlane(c.Forward, normal).normalized, side = Vector3.Cross(normal, along);
                float stride = run ? foot.StrideRun : foot.StrideWalk, interval = run ? .28f : .42f;
                float now = steps * interval + age, life = c.Profile.FootLife;
                int cap = c.Profile.Tier(DeployTier308.PC).Footprints;
                var quad = InkResidueField308.BuildQuad(); quad.name = Prefix + "Quad";
                int alive = 0, made = 0; bool newest = false, alternates = true; float worstSide = 0f, worstAngle = 0f;
                bool blocked = slope > foot.MaxSlopeDeg;
                for (int i = Mathf.Max(0, steps - cap); i < steps && !blocked; i++)
                {
                    bool left = i % 2 == 0;
                    Vector3 point = c.Ground + along * (stride * (i + 1)) + side * (left ? -foot.SideOffset : foot.SideOffset);
                    Vector3 toe = Quaternion.AngleAxis(left ? -foot.ToeOutDeg : foot.ToeOutDeg, normal) * along;
                    float born = (i + 1) * interval;
                    var pose = InkResidueField308.Place(point, normal, toe, foot.Width, foot.Length, c.Profile.Residue.Lift);
                    Mark(c, quad, "Foot_" + i + (left ? "_L" : "_R"), pose, new Vector4(born, life, i % 2 == 0 ? InkBurstMeshBuilder308.CellFoot : InkBurstMeshBuilder308.CellFootB, foot.Opacity),
                        new Vector4(0f, left ? 0f : 1f, 0f, 0f));
                    made++;
                    if (now - born <= life) { alive++; if (i == steps - 1) newest = true; }
                    worstSide = Mathf.Max(worstSide, Mathf.Abs(Mathf.Abs(Vector3.Dot(point - c.Ground, side)) - .10f));
                    worstAngle = Mathf.Max(worstAngle, Vector3.Angle(pose.MultiplyVector(Vector3.up), normal));
                    if (i > 0 && left == ((i - 1) % 2 == 0)) alternates = false;
                }
                Shader.SetGlobalFloat("_OhResidueNow", now);
                Finish(c, "glyph=foot|beat=foot|steps=" + steps + "|slope=" + slope.ToString("0.#", CultureInfo.InvariantCulture) + "|run=" + run + "|now=" + now.ToString("0.###", CultureInfo.InvariantCulture), true);
                c.Log.AppendLine("preview on: footprints " + steps + " steps " + (run ? "running" : "walking") + " on a " + slope.ToString("0.#", CultureInfo.InvariantCulture) + " deg slope | stride " + stride + " m, step every " + interval
                    + " s (synthetic), life " + life + " s, cap " + cap + (blocked ? " | slope above Foot.MaxSlopeDeg " + foot.MaxSlopeDeg + ": no prints (as in play)" : ""));
                c.Log.Append("marks " + made + " | alive at now " + now.ToString("0.##", CultureInfo.InvariantCulture) + " s: " + alive + " (cap " + cap + ") | newest present " + newest + " | left / right alternate " + alternates
                    + " | side offset error " + worstSide.ToString("0.###", CultureInfo.InvariantCulture) + " m (limit .02) | angle to the ground plane " + worstAngle.ToString("0.##", CultureInfo.InvariantCulture) + " deg (limit 3)"
                    + " | size " + foot.Width + " x " + foot.Length + " m | opacity " + foot.Opacity + " | tint 0 | emission 0");
                return c.Log.ToString();
            }
            catch { Fail(c); throw; }
        }

        // ---------------------------------------------------------------- off / status

        static string Off()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (FindRoots().Count == 0)
            {
                int stray = DestroyNamed();
                return "preview: none to remove" + (stray > 0 ? " (" + stray + " stray memory objects destroyed)" : "") + " | quality " + QualityText() + " | scene dirty=" + SceneManager.GetActiveScene().isDirty;
            }
            string text = Teardown(true, out _);
            SceneView.RepaintAll();
            return "preview off: " + text + " | quality " + QualityText() + " | scene dirty=" + SceneManager.GetActiveScene().isDirty;
        }

        static string QualityText() => QualitySettings.GetQualityLevel() + " (" + QualitySettings.names[QualitySettings.GetQualityLevel()] + ")";

        static string Teardown(bool restoreQuality, out int priorQuality)
        {
            priorQuality = -1; int objects = 0; bool dirtyAtOn = false;
            foreach (var root in FindRoots())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    objects++;
                    if (!t.name.StartsWith("State|", StringComparison.Ordinal)) continue;
                    int q = StateInt(t.name, "q"); if (q >= 0) priorQuality = q;
                    dirtyAtOn |= t.name.Contains("dirtyAtOn=True");
                }
                foreach (var hook in root.GetComponentsInChildren<ImpactPreviewHook308>(true)) hook.enabled = false;   // unsubscribes
                foreach (var director in root.GetComponentsInChildren<ImpactFrameDirector308>(true)) { director.ForceFrame(0, default, 0f, false); director.ReleaseResources(); }
                foreach (var runtime in root.GetComponentsInChildren<InkDeployRuntime308>(true)) runtime.Dispose();
                Object.DestroyImmediate(root);
            }
            int memory = DestroyNamed();
            Shader.SetGlobalVector("_OhImpact308", Vector4.zero); Shader.SetGlobalVector("_OhImpactHud308", Vector4.zero); Shader.SetGlobalFloat("_OhResidueNow", 0f);
            Shader.SetGlobalVector("_OhImpactHudPoint308", Vector4.zero);
            if (restoreQuality && priorQuality >= 0 && priorQuality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != priorQuality) QualitySettings.SetQualityLevel(priorQuality, true);
            return objects + " objects and " + memory + " memory objects destroyed, globals zeroed" + (priorQuality >= 0 ? ", quality level " + priorQuality + (restoreQuality ? " restored" : " carried") : "")
                + (dirtyAtOn ? " (the scene was already dirty when the preview went up)" : "");
        }

        // meshes, materials, textures and ScriptableObjects this tool made (never assets)
        static int DestroyNamed()
        {
            int n = 0;
            foreach (var o in Resources.FindObjectsOfTypeAll<Object>())
            {
                if (o == null || o is GameObject || o is Component || EditorUtility.IsPersistent(o)) continue;
                if (!(o is Mesh || o is Material || o is Texture2D || o is ScriptableObject)) continue;
                bool ours = o.name.StartsWith(Prefix, StringComparison.Ordinal) || ((o.hideFlags & HideFlags.DontSave) == HideFlags.DontSave && (o.name == "InkDeploy308" || o.name == "InkResidue308_Quad" || o.name == "ImpactFrame308 (runtime)"));
                if (!ours) continue;
                Object.DestroyImmediate(o); n++;
            }
            return n;
        }

        static string Status()
        {
            var roots = FindRoots();
            var scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.AppendLine("status: " + roots.Count + " preview root(s) | quality " + QualityText() + " | scene " + scene.path + " dirty=" + scene.isDirty
                + " | _OhImpact308 " + Shader.GetGlobalVector("_OhImpact308") + " _OhImpactHud308 " + Shader.GetGlobalVector("_OhImpactHud308")
                + " _OhResidueNow " + Shader.GetGlobalFloat("_OhResidueNow").ToString("0.###", CultureInfo.InvariantCulture));
            foreach (var root in roots)
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                var state = all.FirstOrDefault(t => t.name.StartsWith("State|", StringComparison.Ordinal));
                sb.AppendLine("objects " + all.Length + " | " + (state != null ? state.name : "no state"));
                int marks = all.Count(t => t.name.StartsWith("Mark_", StringComparison.Ordinal) || t.name.StartsWith("Foot_", StringComparison.Ordinal));
                if (marks > 0) sb.AppendLine("ground marks " + marks);
                int hitMarks = all.Count(t => t.name.StartsWith("Hit_", StringComparison.Ordinal));
                if (hitMarks > 0) sb.AppendLine("hit splash marks " + hitMarks);
                foreach (var hook in root.GetComponentsInChildren<ImpactPreviewHook308>(true)) sb.AppendLine("impact hook: shader pass " + hook.ShaderPass + ", enqueued " + hook.Enqueued + " camera renders");
                foreach (var runtime in root.GetComponentsInChildren<InkDeployRuntime308>(true)) sb.Append(Describe(runtime, null));
            }
            return sb.ToString().TrimEnd();
        }

        static string Describe(InkDeployRuntime308 runtime, AreaImpactPlan plan)
        {
            var s = runtime.Stats;
            var sb = new StringBuilder();
            sb.AppendLine("strokes: bold " + s.Bold + ", fine " + s.Fine + ", needles " + s.Needles + ", sprites " + s.Sprites + ", column strips " + s.ColumnStrips + ", ribbons " + s.Ribbons
                + " | drops " + s.Drops + " | vertices " + runtime.VertexCount + (runtime.DroppedPrimitives > 0 ? " (" + runtime.DroppedPrimitives + " primitives did not fit)" : "")
                + " | est. screen cover of the bold strokes " + (s.ScreenShare * 100f).ToString("0.#", CultureInfo.InvariantCulture) + " % (limit 35)");
            float cel = runtime.CelSeconds;
            var births = new SortedDictionary<int, int>();
            for (int i = 0; i < runtime.BirthCount; i++) { int k = Mathf.RoundToInt(runtime.BirthCel(i)); births[k] = births.TryGetValue(k, out int n) ? n + 1 : 1; }
            sb.AppendLine("cel " + (cel * 1000f).ToString("0.#", CultureInfo.InvariantCulture) + " ms | judgement clock " + runtime.JudgementSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s -> first burst cel "
                + runtime.ImpactCel + " (" + (runtime.ImpactCel * cel).ToString("0.###", CultureInfo.InvariantCulture) + " s) | births (cel x count): " + string.Join(" ", births.Select(p => p.Key + "x" + p.Value))
                + " | hold ends " + runtime.HoldEnd.ToString("0.###", CultureInfo.InvariantCulture) + " s, melt ends " + runtime.MeltEnd.ToString("0.###", CultureInfo.InvariantCulture) + " s");
            if (plan != null)
            {
                float want = plan.Shape == AreaShape.Circle ? plan.Radius : plan.Length;
                string reach = want > 0f ? "reach " + s.Reach.ToString("0.###", CultureInfo.InvariantCulture) + " m against the plan's " + want.ToString("0.###", CultureInfo.InvariantCulture) + " m ("
                    + ((s.Reach / want - 1f) * 100f).ToString("+0.#;-0.#", CultureInfo.InvariantCulture) + " %, limit 5)" : "no reach in this plan";
                string angle = plan.Shape == AreaShape.Cone ? " | widest stroke " + s.HalfAngleDeg.ToString("0.#", CultureInfo.InvariantCulture) + " deg against the plan's half angle " + plan.Angle.ToString("0.#", CultureInfo.InvariantCulture) : "";
                string shots = plan.Shape == AreaShape.Volley ? " | strokes " + s.Bold + " for " + plan.Shots.Count + " shots" : "";
                sb.AppendLine("plan " + plan.Shape + ": " + reach + angle + shots);
            }
            return sb.ToString();
        }

        static GameObject Make(string name, Transform parent)
        {
            var go = new GameObject(name) { hideFlags = Flags };
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        static List<GameObject> FindRoots() =>
            Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && g.name == RootName && !EditorUtility.IsPersistent(g) && g.transform.parent == null).ToList();

        static int StateInt(string state, string key)
        {
            foreach (string part in state.Split('|'))
                if (part.StartsWith(key + "=", StringComparison.Ordinal) && int.TryParse(part.Substring(key.Length + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
            return -1;
        }
    }
}
