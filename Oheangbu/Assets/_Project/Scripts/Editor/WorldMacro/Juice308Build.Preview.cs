using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 player juice: the close-up arm previews (SPEC-ANIM-JUICE-308 capture rounds P1 / P2 / P7), Edit Mode, no Play, no focus.
    //   · The open scene is only read. The player's root object is cloned straight into a preview scene (Object.Instantiate with a
    //     scene argument), every clone object is HideAndDontSave, its cameras and audio are switched off, and the clone is
    //     destroyed and the preview scene closed in a finally block. "fixtures:cleanup" removes anything a crash left behind.
    //   · The cloned rig is driven through its preview seam (GestureInput308): the driver feeds draw state and pointer, raises
    //     the stroke events, and steps the rig with a fixed frame time. The recorded movement is a fixed list, so two runs give
    //     the same frames. Nothing here touches DrawingInputController or the recogniser.
    //   · On = the preview profile (A-1 / A-2 on). Off = the same profile with Intensity 0 (the rig's off contract).
    //   · cam:judge (CameraJudge, at the end of this file) uses the same clone for the camera reaction: its own reference wall, a
    //     stand-in letter or car, 60 Hz frames in three rows (off / on / on exaggerated).
    //   · What this cannot show: joint lag against a real hand's speed, the animator's gait under the arm, and the look in the
    //     real scene's light - those are Play items (DEPLOY_PLAN.md).
    public static partial class Juice308Build
    {
        static readonly (string name, Vector2 viewport)[] GripPoses =
        {
            ("center", new Vector2(.5f, .5f)), ("mid-left", new Vector2(.25f, .5f)), ("mid-right", new Vector2(.75f, .5f)),
            ("mid-top", new Vector2(.5f, .75f)), ("mid-bottom", new Vector2(.5f, .25f)),
        };
        static readonly (string name, Vector2 viewport)[] NinePoints =
        {
            ("center", new Vector2(.5f, .5f)), ("left", new Vector2(.1f, .5f)), ("right", new Vector2(.9f, .5f)), ("top", new Vector2(.5f, .9f)), ("bottom", new Vector2(.5f, .1f)),
            ("top-left", new Vector2(.1f, .9f)), ("top-right", new Vector2(.9f, .9f)), ("bottom-left", new Vector2(.1f, .1f)), ("bottom-right", new Vector2(.9f, .1f)),
        };
        const float StepSeconds = .005f;          // fixed frame time of the driver (200 Hz): capture times are exact multiples
        const int TileWidth = 640, TileHeight = 360;

        sealed class Stage : IDisposable
        {
            public Scene Preview; public GameObject Root; public WorldMacroPlayerGestureRig Rig; public Camera Camera; public RenderTexture Target;
            public PlayerJuice308ProfileSO On, Off; public Texture2D Tile; public string Notes = "";
            public void Dispose()
            {
                try { if (Rig != null) { Rig.PreviewEnd308(); Rig.SetJuiceProfile308(null); } } catch (Exception) { }
                if (Preview.IsValid())
                {
                    foreach (var root in Preview.GetRootGameObjects()) Object.DestroyImmediate(root);
                    EditorSceneManager.ClosePreviewScene(Preview);
                }
                if (Target != null) { Target.Release(); Object.DestroyImmediate(Target); }
                if (Tile != null) Object.DestroyImmediate(Tile);
                if (On != null) Object.DestroyImmediate(On);
                if (Off != null) Object.DestroyImmediate(Off);
            }
        }

        // child-index path from an ancestor (names are not unique: "Parent/Name" finds the first sibling of that name)
        static bool IndexPath(Transform ancestor, Transform target, List<int> path)
        {
            path.Clear();
            for (Transform t = target; t != ancestor; t = t.parent) { if (t == null) return false; path.Insert(0, t.GetSiblingIndex()); }
            return true;
        }
        static Transform Follow(Transform ancestor, List<int> path)
        {
            Transform t = ancestor;
            foreach (int i in path) { if (t == null || i >= t.childCount) return null; t = t.GetChild(i); }
            return t;
        }

        static Stage OpenStage(out string refusal)
        {
            refusal = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { refusal = "refused: the arm previews run in Edit Mode (in Play use preview-profile:on with the usual grip captures)"; return null; }
            var scene = SceneManager.GetActiveScene();
            var sourceRig = SceneRig(scene, out string issue);
            if (sourceRig == null) { refusal = "refused: " + issue; return null; }
            var so = new SerializedObject(sourceRig);
            var cameraRig = so.FindProperty("_cameraRig")?.objectReferenceValue as CameraRigController;
            var nearRoot = so.FindProperty("_nearRoot")?.objectReferenceValue as Transform;
            var view = cameraRig != null ? typeof(CameraRigController).GetField("_camera", Inst).GetValue(cameraRig) as Transform : null;
            if (cameraRig == null || view == null || view.GetComponent<Camera>() == null) { refusal = "refused: the rig has no camera rig / camera wired"; return null; }
            if (nearRoot == null) { refusal = "refused: the rig has no close-up arm (_nearRoot)"; return null; }
            Transform sourceRoot = sourceRig.transform.root;
            if (view.root != sourceRoot || nearRoot.root != sourceRoot)
            { refusal = "refused: the player's parts live under different roots (rig '" + sourceRoot.name + "', camera '" + view.root.name + "', close-up arm '" + nearRoot.root.name + "') - this driver clones one root"; return null; }
            var rigPath = new List<int>(); var viewPath = new List<int>();
            if (!IndexPath(sourceRoot, sourceRig.transform, rigPath) || !IndexPath(sourceRoot, view, viewPath)) { refusal = "refused: could not address the rig / camera under '" + sourceRoot.name + "'"; return null; }

            var stage = new Stage();
            try
            {
                stage.Preview = EditorSceneManager.NewPreviewScene();
                stage.Root = (GameObject)Object.Instantiate((Object)sourceRoot.gameObject, stage.Preview);
                stage.Root.name = FixturePrefix + "_Player";
                foreach (var t in stage.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                foreach (var cam in stage.Root.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
                foreach (var listener in stage.Root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                foreach (var source in stage.Root.GetComponentsInChildren<AudioSource>(true)) source.enabled = false;
                var rigTransform = Follow(stage.Root.transform, rigPath); var viewTransform = Follow(stage.Root.transform, viewPath);
                stage.Rig = rigTransform != null ? rigTransform.GetComponent<WorldMacroPlayerGestureRig>() : null;
                stage.Camera = viewTransform != null ? viewTransform.GetComponent<Camera>() : null;
                if (stage.Rig == null || stage.Camera == null) { refusal = "refused: the clone lost its rig or camera"; stage.Dispose(); return null; }

                // the draw close-up framing the game uses (data: CombatConfigSO), not the shoulder view the scene is saved in
                var cloneCameraRig = stage.Camera.GetComponentInParent<CameraRigController>(true);
                var config = cloneCameraRig != null ? typeof(CameraRigController).GetField("_config", Inst).GetValue(cloneCameraRig) as CombatConfigSO : null;
                if (config != null)
                {
                    stage.Camera.transform.localPosition = config.ShoulderDrawOffset;
                    if (config.DrawCloseupFov > 0f) stage.Camera.fieldOfView = config.DrawCloseupFov;
                    stage.Notes += "camera: draw close-up offset " + config.ShoulderDrawOffset.ToString("F2") + ", field of view " + F(stage.Camera.fieldOfView, "F1") + ". ";
                }
                else stage.Notes += "camera: no CombatConfigSO on the clone's camera rig - the scene's saved pose is used. ";
                stage.Camera.scene = stage.Preview; stage.Camera.cullingMask = ~0;
                stage.Camera.clearFlags = CameraClearFlags.SolidColor; stage.Camera.backgroundColor = new Color(.86f, .84f, .78f, 1f);
                stage.Target = new RenderTexture(TileWidth, TileHeight, 24) { name = FixturePrefix + "_ArmRT", hideFlags = HideFlags.HideAndDontSave };
                stage.Camera.targetTexture = stage.Target;
                stage.Tile = new Texture2D(TileWidth, TileHeight, TextureFormat.RGB24, false) { name = FixturePrefix + "_ArmTile", hideFlags = HideFlags.HideAndDontSave };
                var light = FixtureObject("Light", stage.Preview).AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(42f, stage.Camera.transform.eulerAngles.y - 28f, 0f);

                foreach (var bristles in stage.Root.GetComponentsInChildren<BrushBristleRig>(true)) bristles.TryBind();
                if (!stage.Rig.TryBind()) { refusal = "refused: the cloned rig did not bind in Edit Mode: " + stage.Rig.BindingError; stage.Dispose(); return null; }
                stage.On = CaptureProfile(true, out string profileSource);
                stage.Off = CaptureProfile(true, out _); stage.Off.Intensity = 0f;
                stage.Notes += "profile: " + profileSource + ", preview (A-1 / A-2 on). ";
                return stage;
            }
            catch (Exception)
            {
                stage.Dispose(); throw;
            }
        }

        struct ArmSample
        {
            public float TipErrorPixels, LeanDegrees, Headroom, ShoulderCorrection, GripError, ElbowHeight, Tilt, Splay; public Vector2 Lean;
            public bool NearVisible, TipReachable;
        }

        static ArmSample Sample(WorldMacroPlayerGestureRig rig)
        {
            var d = rig.Diagnostics; var f = rig.JuiceFrame308;
            return new ArmSample { TipErrorPixels = d.NearTipErrorPixels, LeanDegrees = rig.JuiceShaftLeanDegrees308, Headroom = rig.JuiceHeadroom308, ShoulderCorrection = d.NearShoulderCorrectionMeters,
                GripError = d.GripErrorMeters, ElbowHeight = d.NearElbowHeight, Tilt = f.ShaftRaiseDegrees, Lean = f.ShaftLeanDegrees, Splay = f.SplayAdd, NearVisible = d.NearVisible, TipReachable = d.TipReachable };
        }

        // One deterministic take: the arm rises at `viewport`, then either a stroke starts (a1) or a short stroke is drawn and ends (a2).
        // Returns after `afterEvent` seconds past the event (0 = the event frame). worstContactTip = the largest tip error on a contact frame.
        static ArmSample Drive(Stage stage, PlayerJuice308ProfileSO profile, Vector2 viewport, string effect, float afterEvent, out float worstContactTip)
        {
            var rig = stage.Rig; var cam = stage.Camera; Rect rect = cam.pixelRect; worstContactTip = 0f;
            Vector2 Px(Vector2 v) => new Vector2(rect.x + v.x * rect.width, rect.y + v.y * rect.height);
            rig.PreviewEnd308();
            rig.SetJuiceProfile308(profile);
            var input = new WorldMacroPlayerGestureRig.GestureInput308 { Drawing = true, Stroking = false, HasPointer = true, Camera = cam, Screen = Px(viewport) };
            rig.SetPreviewInput308(input); rig.PreviewModeEntered308();
            for (int i = 0; i < 100; i++) rig.PreviewEvaluate308(StepSeconds);   // 0.5 s: hand and brush have risen and the lags have settled
            int steps = Mathf.RoundToInt(afterEvent / StepSeconds);
            if (effect == "a2")
            {
                // a short stroke: 0.2 s to the right and slightly down at a steady speed, then the pen lifts
                rig.PreviewStrokeStarted308(); input.Stroking = true;
                Vector2 end = viewport + new Vector2(.10f, -.03f); const int strokeSteps = 40;
                for (int i = 0; i <= strokeSteps; i++)
                {
                    input.Screen = Px(Vector2.Lerp(viewport, end, i / (float)strokeSteps)); rig.SetPreviewInput308(input); rig.PreviewEvaluate308(StepSeconds);
                    float e = rig.Diagnostics.NearTipErrorPixels; if (e >= 0f && !float.IsInfinity(e)) worstContactTip = Mathf.Max(worstContactTip, e);
                }
                rig.PreviewStrokeEnded308(); input.Stroking = false; rig.SetPreviewInput308(input);
                for (int i = 0; i <= steps; i++) rig.PreviewEvaluate308(StepSeconds);
            }
            else
            {
                rig.PreviewStrokeStarted308(); input.Stroking = true; rig.SetPreviewInput308(input);
                for (int i = 0; i <= steps; i++)
                {
                    rig.PreviewEvaluate308(StepSeconds);
                    float e = rig.Diagnostics.NearTipErrorPixels; if (e >= 0f && !float.IsInfinity(e)) worstContactTip = Mathf.Max(worstContactTip, e);
                }
            }
            return Sample(rig);
        }

        static Color[] Shoot(Stage stage)
        {
            stage.Camera.Render();
            var active = RenderTexture.active; RenderTexture.active = stage.Target;
            stage.Tile.ReadPixels(new Rect(0, 0, TileWidth, TileHeight), 0, 0); stage.Tile.Apply(false);
            RenderTexture.active = active;
            return stage.Tile.GetPixels();
        }

        // the same take seen from a second place: the camera is moved for one render and put back before the rig is stepped again
        static Color[] ShootFrom(Stage stage, Vector3 position, Vector3 lookAt, float fov, bool worldBody)
        {
            var t = stage.Camera.transform; Vector3 p = t.position; Quaternion r = t.rotation; float f = stage.Camera.fieldOfView;
            var changed = new List<(Renderer renderer, UnityEngine.Rendering.ShadowCastingMode mode, bool enabled)>();
            try
            {
                if (worldBody)
                    foreach (var renderer in stage.Root.GetComponentsInChildren<Renderer>(true))
                    {
                        changed.Add((renderer, renderer.shadowCastingMode, renderer.enabled));
                        if (renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    }
                t.SetPositionAndRotation(position, Quaternion.LookRotation((lookAt - position).normalized, Vector3.up)); stage.Camera.fieldOfView = fov;
                return Shoot(stage);
            }
            finally
            {
                t.SetPositionAndRotation(p, r); stage.Camera.fieldOfView = f;
                foreach (var c in changed) if (c.renderer != null) { c.renderer.shadowCastingMode = c.mode; c.renderer.enabled = c.enabled; }
            }
        }

        static string Json(ArmSample s, float worstContactTip)
            => "{\"nearVisible\":" + (s.NearVisible ? "true" : "false") + ",\"tipErrorPx\":" + (float.IsInfinity(s.TipErrorPixels) ? "null" : F(s.TipErrorPixels)) + ",\"worstContactTipErrorPx\":" + F(worstContactTip)
               + ",\"shaftLeanAppliedDeg\":" + F(s.LeanDegrees) + ",\"headroom\":" + F(s.Headroom) + ",\"tiltDeg\":" + F(s.Tilt) + ",\"leanX\":" + F(s.Lean.x) + ",\"leanY\":" + F(s.Lean.y) + ",\"splayAdd\":" + F(s.Splay)
               + ",\"shoulderCorrectionM\":" + F(s.ShoulderCorrection) + ",\"gripErrorM\":" + F(s.GripError, "G4") + ",\"elbowHeightM\":" + F(s.ElbowHeight) + "}";

        // P1: grip comparison at the peak frames of A-1 (60 ms) and A-2 (first ring peak), seven views, on / off
        static string GripComparison(string label)
        {
            var stage = OpenStage(out string refusal);
            if (stage == null) return refusal;
            using (stage)
            {
                string dir = Path.Combine(OutRoot, "P1", string.IsNullOrEmpty(label) ? Stamp() : label + "_" + Stamp()); Directory.CreateDirectory(dir);
                var report = new StringBuilder("{\"round\":\"P1 grip comparison\",\"note\":\"" + stage.Notes.Replace("\"", "'") + "On = preview profile (A-1 / A-2 on), off = Intensity 0. Edit-mode clone in a preview scene.\",\"captures\":[");
                int files = 0; bool first = true; float worstOn = 0f, worstOff = 0f; bool visible = true;
                foreach (string effect in new[] { "a1", "a2" })
                {
                    float peak = effect == "a1" ? stage.On.StrokeStart.PressAttack
                        : PlayerJuice308Curves.RingPeakTime(stage.On.StrokeEnd.FlickHz, stage.On.StrokeEnd.FlickDamping);
                    peak = Mathf.Round(peak / StepSeconds) * StepSeconds;
                    var sheet = new Texture2D(TileWidth * 7, TileHeight * 2, TextureFormat.RGB24, false) { name = FixturePrefix + "_P1Sheet", hideFlags = HideFlags.HideAndDontSave };
                    try
                    {
                        for (int row = 0; row < 2; row++)
                        {
                            var profile = row == 0 ? stage.On : stage.Off; string state = row == 0 ? "on" : "off";
                            for (int i = 0; i < 7; i++)
                            {
                                // five pointer places from the eye; then the centre take from close to the grasp and from outside the body
                                Vector2 viewport = i < GripPoses.Length ? GripPoses[i].viewport : GripPoses[0].viewport;
                                string name = i < GripPoses.Length ? GripPoses[i].name : i == 5 ? "grip-close" : "world-drawing";
                                var sample = Drive(stage, profile, viewport, effect, peak, out float contact);
                                Color[] pixels;
                                if (i == 5)
                                {
                                    var hand = stage.Rig.EffectTip != null ? stage.Rig.EffectTip.position : stage.Camera.transform.position + stage.Camera.transform.forward;
                                    pixels = ShootFrom(stage, stage.Camera.transform.position, hand - stage.Camera.transform.forward * .2f, 14f, false);
                                }
                                else if (i == 6)
                                {
                                    Transform body = stage.Rig.transform;
                                    Vector3 tip = stage.Rig.WorldTip != null ? stage.Rig.WorldTip.position : body.position + body.up * 1.3f + body.forward * .5f;
                                    pixels = ShootFrom(stage, body.TransformPoint(new Vector3(1.1f, 1.5f, 1.7f)), tip, 35f, true);
                                }
                                else pixels = Shoot(stage);
                                sheet.SetPixels(i * TileWidth, (1 - row) * TileHeight, TileWidth, TileHeight, pixels);
                                stage.Tile.SetPixels(pixels); stage.Tile.Apply(false);
                                File.WriteAllBytes(Path.Combine(dir, effect + "_" + name + "_" + state + ".png"), stage.Tile.EncodeToPNG()); files++;
                                report.Append(first ? "" : ",").Append("{\"effect\":\"").Append(effect).Append("\",\"view\":\"").Append(name).Append("\",\"state\":\"").Append(state).Append("\",\"msAfterEvent\":").Append(F(peak * 1000f, "F0"))
                                    .Append(",\"arm\":").Append(Json(sample, contact)).Append('}'); first = false;
                                visible &= sample.NearVisible;
                                if (row == 0) worstOn = Mathf.Max(worstOn, contact); else worstOff = Mathf.Max(worstOff, contact);
                            }
                        }
                        sheet.Apply(false);
                        File.WriteAllBytes(Path.Combine(dir, "sheet_" + effect + "_top-on_bottom-off.png"), sheet.EncodeToPNG()); files++;
                    }
                    finally { Object.DestroyImmediate(sheet); }
                }
                report.Append("],\"worstContactTipErrorPx\":{\"on\":").Append(F(worstOn)).Append(",\"off\":").Append(F(worstOff)).Append("},\"closeUpArmVisibleInEveryTake\":").Append(visible ? "true" : "false").Append('}');
                File.WriteAllText(Path.Combine(dir, "p1.json"), report.ToString(), new UTF8Encoding(false));
                return "p1: " + files + " file(s) -> " + dir + " ; close-up arm visible in every take " + visible + " ; worst contact tip error on " + F(worstOn, "F3") + " px / off " + F(worstOff, "F3") + " px. "
                    + stage.Notes + "The user judges the grip from these before A-1 / A-2 are switched on in the main profile.";
            }
        }

        // P2: a strip of one effect over time (0 - 260 ms), rows: intensity 1.0, 0.6, off. Also P7 ("p2:nine"): the nine-point tip table.
        static string ArmStrip(string effect, string label)
        {
            if (effect == "nine" || effect == "p7") return NinePointTable();
            if (effect != "a1" && effect != "a2") { label = effect; effect = "a1"; }
            var stage = OpenStage(out string refusal);
            if (stage == null) return refusal;
            using (stage)
            {
                float[] times = { 0f, .02f, .04f, .06f, .09f, .12f, .18f, .26f };
                string dir = Path.Combine(OutRoot, "P2"); Directory.CreateDirectory(dir); string stamp = Stamp();
                var sheet = new Texture2D(TileWidth * times.Length, TileHeight * 3, TextureFormat.RGB24, false) { name = FixturePrefix + "_P2Sheet", hideFlags = HideFlags.HideAndDontSave };
                var soft = Object.Instantiate(stage.On); soft.name = FixturePrefix + "_SoftProfile"; soft.hideFlags = HideFlags.HideAndDontSave; soft.DrawIntensity = .6f;
                var report = new StringBuilder("{\"round\":\"P2 strip\",\"effect\":\"" + effect + "\",\"rows\":[\"intensity 1.0\",\"intensity 0.6\",\"off\"],\"frames\":[");
                try
                {
                    bool first = true;
                    for (int row = 0; row < 3; row++)
                    {
                        var profile = row == 0 ? stage.On : row == 1 ? soft : stage.Off;
                        for (int i = 0; i < times.Length; i++)
                        {
                            var sample = Drive(stage, profile, GripPoses[0].viewport, effect, times[i], out float contact);
                            sheet.SetPixels(i * TileWidth, (2 - row) * TileHeight, TileWidth, TileHeight, Shoot(stage));
                            report.Append(first ? "" : ",").Append("{\"row\":").Append(row).Append(",\"ms\":").Append(F(times[i] * 1000f, "F0")).Append(",\"arm\":").Append(Json(sample, contact)).Append('}'); first = false;
                        }
                    }
                    sheet.Apply(false);
                    string png = Path.Combine(dir, "strip_" + effect + "_" + (string.IsNullOrEmpty(label) ? "" : label + "_") + stamp + ".png");
                    File.WriteAllBytes(png, sheet.EncodeToPNG());
                    report.Append("]}"); File.WriteAllText(Path.ChangeExtension(png, ".json"), report.ToString(), new UTF8Encoding(false));
                    return "p2 " + effect + ": " + times.Length + " frames x 3 rows -> " + png + " (+ .json). " + stage.Notes;
                }
                finally { Object.DestroyImmediate(sheet); Object.DestroyImmediate(soft); }
            }
        }

        // P7 / AC-J4: the tip stays on the pointer with the largest lean, at nine screen places (numbers only)
        static string NinePointTable()
        {
            var stage = OpenStage(out string refusal);
            if (stage == null) return refusal;
            using (stage)
            {
                var c = new Checks(); var rows = new StringBuilder(); float worstOn = 0f, worstOff = 0f, worstShoulder = 0f; int leaned = 0;
                foreach (string effect in new[] { "a1", "a2" })
                {
                    float peak = effect == "a1" ? stage.On.StrokeStart.PressAttack : PlayerJuice308Curves.RingPeakTime(stage.On.StrokeEnd.FlickHz, stage.On.StrokeEnd.FlickDamping);
                    foreach (var point in NinePoints)
                    {
                        var on = Drive(stage, stage.On, point.viewport, effect, peak, out float contactOn);
                        var off = Drive(stage, stage.Off, point.viewport, effect, peak, out float contactOff);
                        worstOn = Mathf.Max(worstOn, contactOn); worstOff = Mathf.Max(worstOff, contactOff);
                        worstShoulder = Mathf.Max(worstShoulder, on.ShoulderCorrection - off.ShoulderCorrection);
                        if (on.LeanDegrees > 0f) leaned++;
                        rows.Append(rows.Length > 0 ? "," : "").Append("{\"effect\":\"").Append(effect).Append("\",\"point\":\"").Append(point.name).Append("\",\"on\":").Append(Json(on, contactOn)).Append(",\"off\":").Append(Json(off, contactOff)).Append('}');
                    }
                }
                c.Add("P7.tip_on_pointer", worstOn <= .5f && worstOn - worstOff <= .1f, "contact frames at nine places, A-1 and A-2 at full amplitude: worst tip error on " + F(worstOn, "F3") + " px, off " + F(worstOff, "F3") + " px (limits .5 px and +.1 px)");
                c.Add("P7.shoulder_budget", worstShoulder <= .002f, "largest increase of the shoulder correction against off: " + F(worstShoulder * 1000f, "F2") + " mm (limit 2 mm)");
                c.Note("takes in which a lean reached the arm: " + leaned + " of " + (NinePoints.Length * 2) + " (0 = the lean never applied: check that the close-up arm was visible)");
                Directory.CreateDirectory(Path.Combine(OutRoot, "P7"));
                File.WriteAllText(Path.Combine(OutRoot, "P7", "nine_points_" + Stamp() + ".json"), "{\"round\":\"P7 nine points\",\"rows\":[" + rows + "]}", new UTF8Encoding(false));
                return Finish(c, "p7_nine_points");
            }
        }

        // ---------------------------------------------------------------- hand-jump (SPEC-LOCKON-DRAW-STABILITY-308 4.7, TE-7)
        //
        // The close-up hand and the brush tip while the camera turns or the player walks, on the cloned player at the scene's own
        // coordinates (the main scene: about 3 km from the origin). The pointer rests on one pixel with a stroke down; the clone's
        // body (the transform the lock-on pull turns) is yawed 1 / 5 / 20 deg/s or walks sideways, 60 Hz. With a pointer at rest the
        // close-up arm is solved relative to the camera, so on screen it should not move at all: what moves is the error.
        // Read every frame without the engine's projection functions (ExactScreen308, double precision):
        //   tip      the brush tip's distance from the pointer pixel, and the rig's own reading of it (NearTipErrorPixels). The tip is
        //            read from the transform the rig reads (_nearTipSocket), not from Diagnostics.EffectTipWorld: that value has been
        //            through another transform's local position, which is one float step (0.2 px) at these coordinates
        //   hand     the wrist target's place on screen; its change from one frame to the next is the jump
        //   control  what Camera.ScreenPointToRay would have done on that frame (nothing is placed with it): its direction against
        //            the exact ray's swings the shaft about the tip - given as travel of the hand in px
        // The camera's target is 1920 x 1080 here: the rig settles the tip to 0.35 px of the camera's own pixels, so a capture tile
        // (640 x 360) would measure a coarser game. Nothing is rendered. The limits are the mirror's (Tools/Unity/Stage308_lockdraw/
        // sim/lockdraw_hand_f32.py EDITOR_*; juice_checks.py S23 holds the two files together).
        // What this cannot show: a real hand's speed, the animator's gait under the arm, the look - Play items.
        const double HandTipLimit = .5;            // SPEC-ANIM-JUICE-308 AC-J4: the tip on the pointer (true offset, px at 1080p)
        const double HandReadLimit = .05;          // the rig's own reading of the tip against the double-precision projection (px)
        const double HandControlSeenLimit = 2.0;   // the engine ray must put the hand at least this far away at these coordinates (mirror: 3.2 - 7.5 px)
        const double HandSteadierShare = .5;       // hand jump (rms) against the jump the engine ray would have added (mirror: 0.08 - 0.21; 1.4 - 1.9 with the engine calls put back)
        const float HandFarFromOrigin = 500f;      // nearer the origin than this the engine functions are accurate: the control has nothing to show
        const int HandSettleFrames = 60, HandFrames = 120;
        static readonly (string name, float yaw, float walk)[] HandTakes = { ("turn 1 deg/s", 1f, 0f), ("turn 5 deg/s", 5f, 0f), ("turn 20 deg/s", 20f, 0f), ("walk 3 m/s", 0f, 3f) };
        static readonly Vector2 HandPointer = new Vector2(.58f, .46f);

        static string Dd(double v, string f = "F3") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);

        // Where a world point is on the camera's screen and how far in front of the camera, in double precision. world - camera position
        // is the difference of two nearby floats (exact); the rotation into the view and the projection use the camera's own rotation
        // and projection matrix. (The same reading as LockDraw308.PreciseScreen; kept here so this file does not depend on that tool.)
        static void ExactScreen308(Camera cam, Vector3 world, out double sx, out double sy, out double depth)
        {
            Transform t = cam.transform; Vector3 o = t.position; Quaternion q = t.rotation;
            double dx = (double)world.x - o.x, dy = (double)world.y - o.y, dz = (double)world.z - o.z;
            double qx = q.x, qy = q.y, qz = q.z, qw = q.w, n = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            if (n < 1e-12) { sx = sy = depth = double.NaN; return; }
            qx /= n; qy /= n; qz /= n; qw /= n;
            // the camera's right / up / forward axes = the columns of its rotation matrix
            double rx = 1 - 2 * (qy * qy + qz * qz), ry = 2 * (qx * qy + qw * qz), rz = 2 * (qx * qz - qw * qy);
            double ux = 2 * (qx * qy - qw * qz), uy = 1 - 2 * (qx * qx + qz * qz), uz = 2 * (qy * qz + qw * qx);
            double fx = 2 * (qx * qz + qw * qy), fy = 2 * (qy * qz - qw * qx), fz = 1 - 2 * (qx * qx + qy * qy);
            double x = dx * rx + dy * ry + dz * rz, y = dx * ux + dy * uy + dz * uz; depth = dx * fx + dy * fy + dz * fz;
            double z = -depth; Matrix4x4 p = cam.projectionMatrix; Rect r = cam.pixelRect;   // view space, z towards the viewer
            double cx = p.m00 * x + p.m01 * y + p.m02 * z + p.m03, cy = p.m10 * x + p.m11 * y + p.m12 * z + p.m13, cw = p.m30 * x + p.m31 * y + p.m32 * z + p.m33;
            sx = r.x + (cx / cw + 1.0) * .5 * r.width; sy = r.y + (cy / cw + 1.0) * .5 * r.height;
        }

        static string HandJump()
        {
            var stage = OpenStage(out string refusal);
            if (stage == null) return refusal;
            using (stage)
            {
                var c = new Checks(); var rig = stage.Rig; var cam = stage.Camera;
                if (cam.orthographic) return "refused: hand-jump expects the player's perspective camera";
                // the game's resolution instead of the capture tile (nothing is rendered: the target only gives the camera its pixel size)
                cam.targetTexture = null; stage.Target.Release(); Object.DestroyImmediate(stage.Target);
                stage.Target = new RenderTexture(1920, 1080, 0) { name = FixturePrefix + "_HandRT", hideFlags = HideFlags.HideAndDontSave };
                cam.targetTexture = stage.Target;
                var tipSocket = new SerializedObject(rig).FindProperty("_nearTipSocket")?.objectReferenceValue as Transform;
                if (tipSocket == null) return "refused: the cloned rig has no close-up tip socket (_nearTipSocket)";
                // review S1: H4 reads the rig's wrist TARGET (before the two-bone solve); the grip socket is the drawn transform after the solve and the brush placement
                var gripSocket = new SerializedObject(rig).FindProperty("_nearGripSocket")?.objectReferenceValue as Transform;
                var owner = cam.GetComponentInParent<CameraRigController>(true);
                Transform body = owner != null ? owner.transform : cam.transform.root;
                Vector3 bodyPosition = body.position; Quaternion bodyRotation = body.rotation;
                Rect rect = cam.pixelRect; Vector2 screen = new Vector2(rect.x + HandPointer.x * rect.width, rect.y + HandPointer.y * rect.height);
                bool far = cam.transform.position.magnitude >= HandFarFromOrigin;
                double tanHalf = Math.Tan(cam.fieldOfView * .5 * Math.PI / 180.0);
                const float frame = 1f / 60f;
                double worstTip = 0, worstRead = 0, worstBefore = 0, worstMoved = 0, worstControl = 0, worstHand = 0, handSq = 0, controlSq = 0, depthSum = 0, reachSum = 0, worstGrip = 0, gripSq = 0; int steps = 0; bool visible = true;
                var rows = new StringBuilder();
                foreach (var take in HandTakes)
                {
                    body.SetPositionAndRotation(bodyPosition, bodyRotation);
                    rig.PreviewEnd308(); rig.SetJuiceProfile308(stage.Off);
                    var input = new WorldMacroPlayerGestureRig.GestureInput308 { Drawing = true, Stroking = false, HasPointer = true, Camera = cam, Screen = screen };
                    rig.SetPreviewInput308(input); rig.PreviewModeEntered308();
                    for (int i = 0; i < 100; i++) rig.PreviewEvaluate308(StepSeconds);   // 0.5 s: hand and brush have risen and the lags have settled
                    rig.PreviewStrokeStarted308(); input.Stroking = true; rig.SetPreviewInput308(input);
                    double takeTip = 0, takeHand = 0, takeHandSq = 0, takeControl = 0, takeControlJump = 0, takeControlSq = 0, hx0 = 0, hy0 = 0, cx0 = 0, cy0 = 0, takeGrip = 0, takeGripSq = 0, gx0 = 0, gy0 = 0; int takeSteps = 0;
                    for (int i = 0; i < HandSettleFrames + HandFrames; i++)
                    {
                        float t = i * frame;
                        body.SetPositionAndRotation(bodyPosition + bodyRotation * Vector3.right * (take.walk * t), Quaternion.AngleAxis(take.yaw * t, Vector3.up) * bodyRotation);
                        rig.PreviewEvaluate308(frame);
                        var d = rig.Diagnostics;
                        bool solved = d.NearVisible && d.NearTipErrorPixels >= 0f && !float.IsInfinity(d.NearTipErrorPixels) && !float.IsInfinity(d.NearTipErrorBeforeCorrectionPixels);
                        Vector3 tipWorld = tipSocket.position;   // the same transform, in the same state, as the rig's last read of this frame
                        ExactScreen308(cam, tipWorld, out double tx, out double ty, out _);
                        ExactScreen308(cam, d.NearWristTarget, out double hx, out double hy, out double handDepth);
                        double gx = 0, gy = 0; if (gripSocket != null) ExactScreen308(cam, gripSocket.position, out gx, out gy, out _);
                        // control: the engine's ray on this frame against the exact one - computed only, nothing is placed with it
                        Vector3 engine = cam.ScreenPointToRay(screen).direction, exact = Oheangbu.App.BrushStrokeFeedAdapter.ScreenPointToRayPrecise308(cam, screen).direction;
                        Vector3 right = cam.transform.right, up = cam.transform.up;
                        double ex = (double)engine.x - exact.x, ey = (double)engine.y - exact.y, ez = (double)engine.z - exact.z;
                        double reach = Vector3.Distance(tipWorld, d.NearWristTarget);
                        double unitsPerPixel = 2.0 * handDepth * tanHalf / cam.pixelHeight;
                        double cx = (ex * right.x + ey * right.y + ez * right.z) * reach / unitsPerPixel, cy = (ex * up.x + ey * up.y + ez * up.z) * reach / unitsPerPixel;
                        if (i >= HandSettleFrames)
                        {
                            if (!solved) visible = false;
                            else
                            {
                                double tip = Math.Sqrt((tx - screen.x) * (tx - screen.x) + (ty - screen.y) * (ty - screen.y));
                                double hand = Math.Sqrt((hx - hx0) * (hx - hx0) + (hy - hy0) * (hy - hy0)), control = Math.Sqrt((cx - cx0) * (cx - cx0) + (cy - cy0) * (cy - cy0));
                                takeTip = Math.Max(takeTip, tip); worstRead = Math.Max(worstRead, Math.Abs(d.NearTipErrorPixels - tip));
                                worstBefore = Math.Max(worstBefore, d.NearTipErrorBeforeCorrectionPixels); worstMoved = Math.Max(worstMoved, d.NearEndpointCorrectionMeters);
                                takeHand = Math.Max(takeHand, hand); takeHandSq += hand * hand; takeControl = Math.Max(takeControl, Math.Sqrt(cx * cx + cy * cy));
                                takeControlJump = Math.Max(takeControlJump, control); takeControlSq += control * control; depthSum += handDepth; reachSum += reach; takeSteps++;
                                double grip = Math.Sqrt((gx - gx0) * (gx - gx0) + (gy - gy0) * (gy - gy0)); takeGrip = Math.Max(takeGrip, grip); takeGripSq += grip * grip;
                            }
                        }
                        hx0 = hx; hy0 = hy; cx0 = cx; cy0 = cy; gx0 = gx; gy0 = gy;
                    }
                    double takeHandRms = takeSteps > 0 ? Math.Sqrt(takeHandSq / takeSteps) : 0, takeControlRms = takeSteps > 0 ? Math.Sqrt(takeControlSq / takeSteps) : 0;
                    c.Note(take.name + ": tip up to " + Dd(takeTip) + " px from the pointer | hand jump max " + Dd(takeHand) + " rms " + Dd(takeHandRms) + " px | control (the engine ray on the same frames): hand up to " + Dd(takeControl) +
                        " px away, jump max " + Dd(takeControlJump) + " rms " + Dd(takeControlRms) + " px" +
                        (gripSocket != null ? " | drawn grip socket jump max " + Dd(takeGrip) + " rms " + Dd(takeSteps > 0 ? Math.Sqrt(takeGripSq / takeSteps) : 0) + " px" : ""));
                    rows.Append(rows.Length > 0 ? "," : "").Append("{\"take\":\"").Append(take.name).Append("\",\"frames\":").Append(takeSteps).Append(",\"tipWorstPx\":").Append(Dd(takeTip, "F4")).Append(",\"handJumpMaxPx\":").Append(Dd(takeHand, "F4"))
                        .Append(",\"handJumpRmsPx\":").Append(Dd(takeHandRms, "F4")).Append(",\"controlWorstPx\":").Append(Dd(takeControl, "F4")).Append(",\"controlJumpMaxPx\":").Append(Dd(takeControlJump, "F4")).Append(",\"controlJumpRmsPx\":").Append(Dd(takeControlRms, "F4")).Append('}');
                    worstTip = Math.Max(worstTip, takeTip); worstHand = Math.Max(worstHand, takeHand); worstControl = Math.Max(worstControl, takeControl); handSq += takeHandSq; controlSq += takeControlSq; steps += takeSteps; worstGrip = Math.Max(worstGrip, takeGrip); gripSq += takeGripSq;
                }
                rig.PreviewEnd308();
                int expected = HandTakes.Length * HandFrames;
                double handRms = steps > 0 ? Math.Sqrt(handSq / steps) : 0, controlRms = steps > 0 ? Math.Sqrt(controlSq / steps) : 0;
                double handDepthMean = steps > 0 ? depthSum / steps : 0, reachMean = steps > 0 ? reachSum / steps : 0, mmPerPixel = 2.0 * handDepthMean * tanHalf / cam.pixelHeight * 1000.0;
                c.Note("clone at " + cam.transform.position.ToString("F1") + " (" + (far ? "far from the origin" : "within " + F(HandFarFromOrigin, "F0") + " m of the origin") + "), camera target " + cam.pixelWidth + " x " + cam.pixelHeight +
                    ", pointer at rest on viewport (" + F(HandPointer.x, "F2") + ", " + F(HandPointer.y, "F2") + "), a stroke down; " + HandTakes.Length + " takes x " + HandFrames + " frames at 60 Hz after " + HandSettleFrames +
                    " settle frames. The hand (wrist target) is " + Dd(handDepthMean, "F2") + " m in front of the camera and " + Dd(reachMean, "F2") + " m from the tip; 1 px there = " + Dd(mmPerPixel) + " mm. " + stage.Notes);
                c.Add("H0.close_up_arm_solved", visible && steps == expected, "frames on which the close-up arm was solved and its tip read: " + steps + " of " + expected + (visible ? "" : " - the rows below prove nothing on the missing frames"));
                c.Add("H1.tip_on_pointer", visible && worstTip <= HandTipLimit, "the brush tip's true distance from the pointer pixel (double projection of its world position): worst " + Dd(worstTip) + " px (limit " + Dd(HandTipLimit, "F1") +
                    "); the solve left it up to " + Dd(worstBefore) + " px away before the rig's settle loop, which moved the arm by up to " + Dd(worstMoved * 1000.0, "F2") + " mm");
                c.Add("H2.rig_reads_the_exact_place", visible && worstRead <= HandReadLimit, "the rig's own reading of the tip (NearTipErrorPixels) against that distance: differs by up to " + Dd(worstRead, "F4") + " px (limit " + Dd(HandReadLimit, "F2") +
                    ") - a reading through Camera.WorldToScreenPoint differs by tenths of a pixel here");
                if (far)
                {
                    c.Add("H3.control_sees_engine_ray", worstControl > HandControlSeenLimit, "control: Camera.ScreenPointToRay on the same frames would have put the hand up to " + Dd(worstControl, "F2") + " px (" + Dd(worstControl * mmPerPixel, "F2") +
                        " mm) from where the exact ray puts it (> " + Dd(HandControlSeenLimit, "F0") + " expected at these coordinates: the hand shake), its jump from frame to frame rms " + Dd(controlRms, "F2") + " px");
                    c.Add("H4.hand_steadier_than_engine_ray", visible && handRms <= HandSteadierShare * controlRms, "with the pointer at rest the hand moves on screen by max " + Dd(worstHand) + " px (" + Dd(worstHand * mmPerPixel) + " mm), rms " + Dd(handRms) +
                        " px from one frame to the next = " + Dd(controlRms > 0 ? handRms / controlRms : 0, "F2") + " of what the engine ray would have added (limit " + Dd(HandSteadierShare, "F1") + "). What is left is the float grid of the world coordinates");
                }
                else c.Note("H3 / H4 not judged: this scene's player is within " + F(HandFarFromOrigin, "F0") + " m of the origin, where the engine functions are accurate (control " + Dd(worstControl) + " px; hand jump max " + Dd(worstHand) + " rms " + Dd(handRms) + " px)");
                double gripRms = steps > 0 ? Math.Sqrt(gripSq / steps) : 0;
                c.Note(gripSocket != null ? "the DRAWN hand - the brush's grip socket (_nearGripSocket: the transform after the two-bone solve and the brush placement) - moves on screen by max " + Dd(worstGrip) + " px, rms " + Dd(gripRms) +
                    " px from one frame to the next. H4 above judges the rig's wrist TARGET; this line is the drawn transform and is not judged (its float floor has never been measured)"
                    : "the drawn hand was not read: the cloned rig has no grip socket (_nearGripSocket) - H4 speaks of the wrist target only");
                Directory.CreateDirectory(Path.Combine(OutRoot, "Hand"));
                File.WriteAllText(Path.Combine(OutRoot, "Hand", "hand_jump_" + Stamp() + ".json"), "{\"round\":\"hand-jump\",\"cameraPosition\":\"" + cam.transform.position.ToString("F2") + "\",\"target\":\"" + cam.pixelWidth + "x" + cam.pixelHeight +
                    "\",\"tipWorstPx\":" + Dd(worstTip, "F4") + ",\"readDifferencePx\":" + Dd(worstRead, "F5") + ",\"handJumpMaxPx\":" + Dd(worstHand, "F4") + ",\"handJumpRmsPx\":" + Dd(handRms, "F4") + ",\"controlWorstPx\":" + Dd(worstControl, "F4") +
                    ",\"controlJumpRmsPx\":" + Dd(controlRms, "F4") + ",\"gripSocketJumpMaxPx\":" + Dd(worstGrip, "F4") + ",\"gripSocketJumpRmsPx\":" + Dd(gripRms, "F4") + ",\"mmPerPixelAtHand\":" + Dd(mmPerPixel, "F4") + ",\"takes\":[" + rows + "]}", new UTF8Encoding(false));
                return Finish(c, "hand_jump");
            }
        }

        // ---------------------------------------------------------------- identity (the off contract, measured)
        //
        // "Without a profile nothing but B-1 changes" was only read from the code. This drives one scripted take (the arm rises, a
        // stroke, a pause, a second stroke, the commit, the throw) on the cloned rig several times and compares the local position /
        // rotation / scale of EVERY transform under the clone on every recorded frame:
        //   A  no profile at all (SetJuiceForcedOff308: the state of a project that has the code and no asset), twice - the second
        //      run says how well the driver repeats itself, and that is the tolerance of the rows below (0 = bit for bit)
        //   B  a profile with Intensity 0                      -> must equal A on every frame
        //   C  the main profile with the brush feel (A-1 / A-2) switched off -> its draw frames must equal A (after the commit B-2
        //      re-times the throw). 3rd revision (D308-24 answer 15): the main profile itself has A-1 / A-2 ON, so its own draw
        //      frames differ by design; the profile as it is becomes a control row (I2b: the feel must be visible).
        //   D  the preview profile (A-1 / A-2 on), a control   -> its draw frames must differ, or the comparison saw nothing
        // What this cannot say: that A equals the code before the stage (that code is not in the assembly any more). The patches
        // that could change the pose without a profile are the ones listed in SPEC-ANIM-JUICE-308 "off contract"; the offline
        // string checks S14 - S16 pin them.
        static List<float[]> IdentityTake(Stage stage, Transform[] all, PlayerJuice308ProfileSO profile, bool forcedOff, out int drawFrames, out string castNote)
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
            rig.PreviewEnd308();
            rig.SetJuiceForcedOff308(forcedOff);
            rig.SetJuiceProfile308(forcedOff ? null : profile);
            Vector2 start = GripPoses[0].viewport, end = start + new Vector2(.10f, -.03f);
            var input = new WorldMacroPlayerGestureRig.GestureInput308 { Drawing = true, Stroking = false, HasPointer = true, Camera = cam, Screen = Px(start) };
            rig.SetPreviewInput308(input); rig.PreviewModeEntered308();
            for (int i = 0; i < 100; i++) { rig.PreviewEvaluate308(StepSeconds); if (i >= 90) Snap(); }   // 0.5 s: hand and brush have risen
            rig.PreviewStrokeStarted308(); input.Stroking = true;
            const int strokeSteps = 40;
            for (int i = 0; i <= strokeSteps; i++) { input.Screen = Px(Vector2.Lerp(start, end, i / (float)strokeSteps)); rig.SetPreviewInput308(input); rig.PreviewEvaluate308(StepSeconds); Snap(); }
            rig.PreviewStrokeEnded308(); input.Stroking = false; rig.SetPreviewInput308(input);
            for (int i = 0; i < 20; i++) { rig.PreviewEvaluate308(StepSeconds); Snap(); }
            // a second stroke 0.1 s after the first: its anchor is read from the frame before (a lean left by the first stroke's flick must not be in it)
            rig.PreviewStrokeStarted308(); input.Stroking = true;
            for (int i = 0; i <= 10; i++) { input.Screen = Px(Vector2.Lerp(end, start, i / 10f)); rig.SetPreviewInput308(input); rig.PreviewEvaluate308(StepSeconds); Snap(); }
            rig.PreviewStrokeEnded308(); input.Stroking = false; rig.SetPreviewInput308(input);
            drawFrames = frames.Count;
            try
            {
                // the commit (a recognised letter that was cast) and the throw
                input.Drawing = false; input.HasPointer = false; rig.SetPreviewInput308(input);
                rig.PreviewCommitted308(true, false);
                for (int i = 0; i < 90; i++) { rig.PreviewEvaluate308(StepSeconds); Snap(); }
            }
            catch (Exception e) { castNote = e.GetType().Name + ": " + e.Message; }
            return frames;
        }

        // largest absolute difference of one stored value over frames [from, to) and how many values differ at all
        static float IdentityDifference(List<float[]> a, List<float[]> b, int from, int to, Transform[] all, out int differing, out string where)
        {
            float worst = 0f; differing = 0; where = "";
            to = Mathf.Min(to, Mathf.Min(a.Count, b.Count));
            for (int f = from; f < to; f++)
            {
                float[] x = a[f], y = b[f];
                for (int i = 0; i < x.Length; i++)
                {
                    if (x[i] == y[i]) continue;
                    differing++;
                    float d = Mathf.Abs(x[i] - y[i]);
                    if (d > worst) { worst = d; var t = all[i / 10]; where = (t != null ? t.name : "?") + (i % 10 < 3 ? " position" : i % 10 < 7 ? " rotation" : " scale") + " @frame " + f; }
                }
            }
            return worst;
        }

        static string Identity()
        {
            var stage = OpenStage(out string refusal);
            if (stage == null) return refusal;
            using (stage)
            {
                var c = new Checks(); PlayerJuice308ProfileSO main = null, mainQuiet = null;
                try
                {
                    var all = stage.Root.GetComponentsInChildren<Transform>(true);
                    main = CaptureProfile(false, out string mainSource);
                    IdentityTake(stage, all, null, true, out _, out _);                                         // warm-up: every lag state has been through one take
                    var a1 = IdentityTake(stage, all, null, true, out int draw, out string castNote);
                    var a2 = IdentityTake(stage, all, null, true, out _, out _);
                    var b = IdentityTake(stage, all, stage.Off, false, out _, out _);
                    bool feelOn = main.StrokeStart.Enabled || main.StrokeEnd.Enabled;
                    mainQuiet = Object.Instantiate(main); mainQuiet.name = FixturePrefix + "_MainQuietProfile"; mainQuiet.hideFlags = HideFlags.HideAndDontSave;
                    mainQuiet.StrokeStart.Enabled = false; mainQuiet.StrokeEnd.Enabled = false;
                    var m = IdentityTake(stage, all, mainQuiet, false, out _, out _);
                    var asIs = feelOn ? IdentityTake(stage, all, main, false, out _, out _) : null;
                    var on = IdentityTake(stage, all, stage.On, false, out _, out _);
                    int total = a1.Count; bool castDriven = castNote.Length == 0 && total > draw;
                    c.Note("take: " + all.Length + " transforms under the clone, " + draw + " draw frames + " + (total - draw) + " frames after the commit, step " + F(StepSeconds * 1000f, "F0") + " ms. " + stage.Notes);
                    if (!castDriven) c.Note("the commit / throw could not be driven in Edit Mode (" + (castNote.Length > 0 ? castNote : "no frame recorded") + "): the rows below cover the draw frames only; the throw stays a Play item");
                    bool sameShape = a2.Count == total && b.Count == total && m.Count == total && on.Count == total && (asIs == null || asIs.Count == total);
                    float repeat = IdentityDifference(a1, a2, 0, total, all, out int repeatCount, out string repeatWhere);
                    float tolerance = repeat * 2f;
                    c.Add("I0.driver_repeats", sameShape && repeat <= 1e-4f, "the same take twice without a profile: " + repeatCount + " value(s) differ, largest " + F(repeat, "G3") + (repeatCount > 0 ? " (" + repeatWhere + ")" : "") +
                        " - this is the tolerance of the rows below (0 = they are compared bit for bit)");
                    float off = IdentityDifference(a1, b, 0, total, all, out int offCount, out string offWhere);
                    c.Add("I1.no_profile_equals_intensity_0", sameShape && off <= tolerance, "no profile at all against a profile with Intensity 0, all " + total + " frames: " + offCount + " value(s) differ, largest " + F(off, "G3") +
                        (offCount > 0 ? " (" + offWhere + ")" : "") + " (limit " + F(tolerance, "G3") + ")");
                    float mainDraw = IdentityDifference(a1, m, 0, draw, all, out int mainCount, out string mainWhere);
                    c.Add("I2.main_profile_draw_frames_equal", sameShape && mainDraw <= tolerance, "no profile against the main profile with the brush feel switched off (" + mainSource + "; in the profile itself A-1 " + main.StrokeStart.Enabled + ", A-2 " + main.StrokeEnd.Enabled + "), the " + draw +
                        " draw frames: " + mainCount + " value(s) differ, largest " + F(mainDraw, "G3") + (mainCount > 0 ? " (" + mainWhere + ")" : "") + " (limit " + F(tolerance, "G3") + ")");
                    if (asIs != null)
                    {
                        float feel = IdentityDifference(a1, asIs, 0, draw, all, out int feelCount, out string feelWhere);
                        c.Add("I2b.main_profile_feel_is_live", feel > Mathf.Max(tolerance * 10f, 1e-5f), "control - the main profile as it is (A-1 " + main.StrokeStart.Enabled + ", A-2 " + main.StrokeEnd.Enabled + ": on by D308-24 answer 15), draw frames: " + feelCount +
                            " value(s) differ, largest " + F(feel, "G3") + (feelCount > 0 ? " (" + feelWhere + ")" : " - nothing differs: the brush feel does not reach the arm"));
                    }
                    float control = IdentityDifference(a1, on, 0, draw, all, out int controlCount, out string controlWhere);
                    c.Add("I3.control_sees_the_lean", control > Mathf.Max(tolerance * 10f, 1e-5f), "control - the preview profile (A-1 / A-2 on), draw frames: " + controlCount + " value(s) differ, largest " + F(control, "G3") +
                        (controlCount > 0 ? " (" + controlWhere + ")" : " - nothing differs: the close-up arm was not solved in this clone, so rows I1 / I2 prove nothing here"));
                    if (castDriven)
                    {
                        float thrown = IdentityDifference(a1, m, draw, total, all, out int thrownCount, out string thrownWhere);
                        c.Note("after the commit, no profile against the main profile: " + thrownCount + " value(s) differ, largest " + F(thrown, "G3") + (thrownCount > 0 ? " (" + thrownWhere + ")" : "") +
                            " - B-2 (" + main.CastBeat.Enabled + ") re-times the throw, so a difference here is expected when it is on");
                    }
                }
                finally
                {
                    try { stage.Rig.SetJuiceForcedOff308(false); } catch (Exception) { }
                    if (main != null) Object.DestroyImmediate(main);
                    if (mainQuiet != null) Object.DestroyImmediate(mainQuiet);
                }
                return Finish(c, "identity");
            }
        }

        // ---------------------------------------------------------------- P8b: the camera reaction where a person can judge it
        //
        // cam:strip shows the open scene from the game camera. At the mine start that is a dark tunnel with no arm, no letter and
        // no car in frame, and a 0.9 degree nod is 15 px of a picture nobody can read (operator's note, 2026-10-05). This capture
        // builds its own place instead - preview objects only; the open scene is read, never touched:
        //   · the player's clone in a preview scene (OpenStage), paper-coloured background;
        //   · a reference wall ahead: a line every degree (every fifth one heavy), the view centre in red. The camera turns about
        //     its own position, so the wall slides by exactly the nod: 0.9 degrees = nine tenths of a cell. 3rd revision (AC-J18):
        //     the line widths are pixels of the capture, from data (Art/Characters308/Juice/Data/JudgeWall308.json; never below
        //     2.5 px) - the first version's 1.3 px lines broke up on the reaction frames and read as part of the effect;
        //   · cast: the draw close-up, the close-up arm driven through the last stroke, the commit and the throw (B-2), and a
        //     stand-in letter (the fixture's strokes as world-fixed ink lines on the drawing plane - not the game's stroke renderer);
        //     call / setdown: the shoulder view and a stand-in car (a box of a car's size) on a metre grid;
        //   · 60 Hz frames from two frames before the event to 0.40 s after it, three renders each: off, on (the profile's own
        //     size, full power) and on x N (the same curve N times larger, ceilings raised - a magnifying glass for direction
        //     and shape, NOT a size the game can show).
        // Every render takes the reaction through the clone's CameraRigController (the owner). Output: tiles + judge.json; the
        // sheet, the overlay and the clip are made outside the editor (Tools/Unity/Stage308_juice/judge_sheet308.py).
        // What this still cannot show: the real scene's light, the real letter's flash, the real car, the feel in the hand (Play).
        const int JudgeWidth = 960, JudgeHeight = 540, JudgeHz = 60, JudgeFramesBefore = 2, JudgeFramesAfter = 24;

        static Material JudgeMaterial(string name, Color color)
        {
            Shader shader = null;
            foreach (string candidate in new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default", "Hidden/Internal-Colored" })
            { shader = Shader.Find(candidate); if (shader != null) break; }
            if (shader == null) return null;
            var material = new Material(shader) { name = FixturePrefix + "_" + name, hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            return material;
        }

        // straight segments as flat ribbons lying in the plane with this normal (both windings, so no culling mode matters)
        sealed class JudgeLines
        {
            readonly List<Vector3> vertices = new List<Vector3>(); readonly List<int> indices = new List<int>();
            public int Count => indices.Count / 12;
            public void Add(Vector3 a, Vector3 b, Vector3 planeNormal, float halfWidth)
            {
                Vector3 along = b - a; if (along.sqrMagnitude < 1e-12f) return;
                Vector3 side = Vector3.Cross(planeNormal, along).normalized * halfWidth; int i = vertices.Count;
                vertices.Add(a - side); vertices.Add(a + side); vertices.Add(b + side); vertices.Add(b - side);
                indices.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            public GameObject Build(string name, Scene preview, Material material)
            {
                if (indices.Count == 0 || material == null) return null;
                var mesh = new Mesh { name = FixturePrefix + "_" + name + "Mesh", hideFlags = HideFlags.HideAndDontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
                var go = FixtureObject(name, preview);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
                return go;
            }
        }

        static string CameraJudge(string name, string[] options)
        {
            if (name != "cast" && name != "call" && name != "setdown") return "refused: cam:judge:<cast|call|setdown>[:narrow][:amp=N]";
            bool narrow = false; float amp = 4f;
            foreach (string option in options)
            {
                if (option.Length == 0) continue;
                if (option == "narrow") narrow = true;
                else if (option.StartsWith("amp=", StringComparison.Ordinal) && float.TryParse(option.Substring(4), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) && parsed >= 1f && parsed <= 10f) amp = parsed;
                else return "refused: unknown option '" + option + "' (narrow | amp=1..10)";
            }
            var scene = SceneManager.GetActiveScene(); bool dirtyBefore = scene.isDirty; int rootsBefore = scene.rootCount;
            // the open scene's game camera: its field of view is the shoulder view's (read only)
            float shoulderFov = 60f;
            {
                var sourceRig = SceneRig(scene, out _);
                var sourceCameraRig = sourceRig != null ? new SerializedObject(sourceRig).FindProperty("_cameraRig")?.objectReferenceValue as CameraRigController : null;
                var sourceView = sourceCameraRig != null ? typeof(CameraRigController).GetField("_camera", Inst).GetValue(sourceCameraRig) as Transform : null;
                var sourceCamera = sourceView != null ? sourceView.GetComponent<Camera>() : null;
                if (sourceCamera != null) shoulderFov = sourceCamera.fieldOfView;
            }
            var stage = OpenStage(out string refusal);
            if (stage == null) return refusal;
            RenderTexture rt = null; Texture2D tile = null; PlayerJuice308ProfileSO armProfile = null, cameraOn = null, cameraAmp = null;
            var materials = new List<Material>();
            float seenFov = 0f; Quaternion seenRotation = Quaternion.identity; bool seen = false; Camera probed = null;
            Action<UnityEngine.Rendering.ScriptableRenderContext, Camera> probe = (x, k) => { if (k == probed) { seenFov = k.fieldOfView; seenRotation = k.transform.localRotation; seen = true; } };
            bool probing = false;
            using (stage)
            {
                try
                {
                    var rig = stage.Rig; var cam = stage.Camera; var view = cam.transform; probed = cam;
                    var cameraRig = cam.GetComponentInParent<CameraRigController>(true);
                    if (cameraRig == null) return "refused: the clone's camera has no CameraRigController above it";
                    var config = typeof(CameraRigController).GetField("_config", Inst).GetValue(cameraRig) as CombatConfigSO;
                    bool closeUp = name == "cast";
                    if (!closeUp)
                    {
                        // the vehicle call is made and answered in the shoulder view
                        if (config != null) view.localPosition = config.ShoulderOffset;
                        cam.fieldOfView = shoulderFov;
                    }
                    rt = new RenderTexture(JudgeWidth, JudgeHeight, 24) { name = FixturePrefix + "_JudgeRT", hideFlags = HideFlags.HideAndDontSave };
                    tile = new Texture2D(JudgeWidth, JudgeHeight, TextureFormat.RGB24, false) { name = FixturePrefix + "_JudgeTile", hideFlags = HideFlags.HideAndDontSave };
                    cam.targetTexture = rt;
                    float framingFov = cam.fieldOfView; Rect rect = cam.pixelRect;
                    Vector2 Px(Vector2 v) => new Vector2(rect.x + v.x * rect.width, rect.y + v.y * rect.height);
                    Vector3 eye = view.position, forward = view.forward, right = view.right, up = view.up;

                    // ---- the reference wall: one line a degree, every fifth heavy, the view centre in red
                    var inkMinor = JudgeMaterial("WallMinor", new Color(.55f, .53f, .50f, 1f)); var inkMajor = JudgeMaterial("WallMajor", new Color(.16f, .15f, .14f, 1f));
                    var red = JudgeMaterial("WallCentre", new Color(.72f, .12f, .10f, 1f)); var ink = JudgeMaterial("Ink", new Color(.05f, .05f, .06f, 1f));
                    var carBody = JudgeMaterial("Car", new Color(.24f, .23f, .22f, 1f));
                    materials.AddRange(new[] { inkMinor, inkMajor, red, ink, carBody });
                    if (inkMinor == null) return "refused: no unlit shader found for the reference wall";
                    float wallDistance = closeUp ? 4f : 14f; Vector3 wall = eye + forward * wallDistance;
                    const int degreesAcross = 50, degreesUp = 30;
                    float Offset(float degrees) => wallDistance * Mathf.Tan(degrees * Mathf.Deg2Rad);
                    if (!JudgeWall(out JudgeWallData wallData, out string wallIssue)) return wallIssue;
                    // the wall faces the camera squarely, so one metre of it is the same number of pixels everywhere on it
                    float metresPerPixel = wallDistance * Mathf.Tan(framingFov * .5f * Mathf.Deg2Rad) / (JudgeHeight * .5f);
                    float thin = wallData.minorLinePx * .5f * metresPerPixel, heavy = wallData.majorLinePx * .5f * metresPerPixel, centreHalf = wallData.centreLinePx * .5f * metresPerPixel;
                    float spanX = Offset(degreesAcross), spanY = Offset(degreesUp);
                    var minor = new JudgeLines(); var major = new JudgeLines(); var centre = new JudgeLines();
                    for (int k = -degreesUp; k <= degreesUp; k++)
                    {
                        bool heavyLine = k % wallData.majorEveryDegrees == 0;
                        if (!heavyLine && k % wallData.minorEveryDegrees != 0) continue;
                        Vector3 p = wall + up * Offset(k);
                        (k == 0 ? centre : heavyLine ? major : minor).Add(p - right * spanX, p + right * spanX, forward, k == 0 ? centreHalf : heavyLine ? heavy : thin);
                    }
                    for (int k = -degreesAcross; k <= degreesAcross; k++)
                    {
                        bool heavyLine = k % wallData.majorEveryDegrees == 0;
                        if (!heavyLine && k % wallData.minorEveryDegrees != 0) continue;
                        Vector3 p = wall + right * Offset(k);
                        (k == 0 ? centre : heavyLine ? major : minor).Add(p - up * spanY, p + up * spanY, forward, k == 0 ? centreHalf : heavyLine ? heavy : thin);
                    }
                    minor.Build("WallMinor", stage.Preview, inkMinor); major.Build("WallMajor", stage.Preview, inkMajor); centre.Build("WallCentre", stage.Preview, red);

                    // ---- cast: a stand-in letter on the drawing plane; call / setdown: a stand-in car on a metre grid
                    string standIn; Vector2 strokeStart = new Vector2(.5f, .5f), strokeEnd = new Vector2(.6f, .47f);
                    if (closeUp)
                    {
                        string fixturePath = Path.Combine(StageRoot, "fixtures/stroke_na.json");
                        var fixture = File.Exists(fixturePath) ? JsonUtility.FromJson<Fixture>(File.ReadAllText(fixturePath)) : null;
                        var feed = new SerializedObject(rig).FindProperty("_brushFeed")?.objectReferenceValue as Oheangbu.App.BrushStrokeFeedAdapter;
                        float depth = feed != null ? feed.EffectiveSurfaceDistance(cam) : 1f;
                        // the points are placed from the eye and the view axes (small numbers), not with Camera.ViewportToWorldPoint: at
                        // main-scene coordinates that function's float32 inverse view-projection matrix is a few millimetres coarse
                        float halfHeight = depth * Mathf.Tan(framingFov * .5f * Mathf.Deg2Rad), halfWidth = halfHeight * rect.width / Mathf.Max(1f, rect.height);
                        var letter = new JudgeLines(); int strokes = 0;
                        if (fixture != null && fixture.strokes != null && fixture.width > 0 && fixture.height > 0)
                            foreach (var stroke in fixture.strokes)
                            {
                                if (stroke == null || stroke.points == null || stroke.points.Length < 6) continue;
                                Vector2 first = default, last = default; Vector3 previous = default;
                                for (int i = 0; i + 2 < stroke.points.Length; i += 3)
                                {
                                    var viewport = new Vector2(stroke.points[i] / fixture.width, stroke.points[i + 1] / fixture.height);
                                    Vector3 world = eye + forward * depth + right * ((viewport.x * 2f - 1f) * halfWidth) + up * ((viewport.y * 2f - 1f) * halfHeight);
                                    if (i == 0) first = viewport; else letter.Add(previous, world, forward, .006f);
                                    previous = world; last = viewport;
                                }
                                strokeStart = first; strokeEnd = last; strokes++;   // the arm draws the letter's last stroke
                            }
                        letter.Build("Letter", stage.Preview, ink);
                        standIn = strokes > 0 ? "a stand-in letter: the " + strokes + " strokes of fixtures/stroke_na.json as world-fixed ink lines " + F(depth, "F2") + " m ahead (not the game's stroke renderer)"
                            : "no stand-in letter (fixtures/stroke_na.json is missing)";
                    }
                    else
                    {
                        Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up); flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
                        Vector3 side = Vector3.Cross(Vector3.up, flat); float ground = rig.transform.position.y;
                        Vector3 feet = new Vector3(rig.transform.position.x, ground, rig.transform.position.z);
                        const float carDistance = 6f; var size = new Vector3(1.6f, 1.5f, 2.8f);
                        var car = FixtureObject("Car", stage.Preview);
                        car.transform.SetPositionAndRotation(feet + flat * carDistance + Vector3.up * (size.y * .5f + .25f), Quaternion.LookRotation(flat, Vector3.up));
                        car.transform.localScale = size;
                        car.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                        var carRenderer = car.AddComponent<MeshRenderer>(); carRenderer.sharedMaterial = carBody;
                        carRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; carRenderer.receiveShadows = false;
                        var floor = new JudgeLines();
                        for (int k = -8; k <= 8; k++) floor.Add(feet + side * k - flat * 4f, feet + side * k + flat * 20f, Vector3.up, k % 5 == 0 ? .03f : .012f);
                        for (int k = -4; k <= 20; k++) floor.Add(feet + flat * k - side * 8f, feet + flat * k + side * 8f, Vector3.up, k % 5 == 0 ? .03f : .012f);
                        floor.Build("Floor", stage.Preview, inkMajor);
                        standIn = "a stand-in car: a " + F(size.x, "F1") + " x " + F(size.y, "F1") + " x " + F(size.z, "F1") + " m box " + F(carDistance, "F0") + " m ahead on a metre grid (not the game's car)";
                    }

                    // ---- profiles: the arm keeps its own motion (B-2) and pushes no camera value; the capture feeds the owner itself
                    armProfile = CaptureProfile(false, out string profileSource); armProfile.Camera.Enabled = false;
                    cameraOn = CaptureProfile(false, out _);
                    if (narrow)
                    {
                        cameraOn.CastKick.FovDegrees = -Mathf.Abs(cameraOn.CastKick.FovDegrees); cameraOn.SetDownThump.FovDegrees = -Mathf.Abs(cameraOn.SetDownThump.FovDegrees);
                        profileSource += ", field-of-view sign flipped (narrow)";
                    }
                    cameraAmp = Object.Instantiate(cameraOn); cameraAmp.name = FixturePrefix + "_AmplifiedProfile"; cameraAmp.hideFlags = HideFlags.HideAndDontSave;
                    cameraAmp.CameraIntensity *= amp; cameraAmp.Camera.MaxPitchDegrees *= amp; cameraAmp.Camera.MaxRollDegrees *= amp; cameraAmp.Camera.MaxFovDegrees *= amp;
                    var solverOn = new PlayerJuice308Solver(); solverOn.Configure(cameraOn); var solverAmp = new PlayerJuice308Solver(); solverAmp.Configure(cameraAmp);

                    string tag = name + (narrow ? "_narrow" : ""); string dir = Path.Combine(OutRoot, "P8", "judge_" + tag + "_" + Stamp()); Directory.CreateDirectory(dir);
                    UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += probe; probing = true;
                    cameraRig.ResetRenderOffsetDiagnostics308();
                    bool restored = true; int renders = 0; float dt = 1f / JudgeHz; var rows = new StringBuilder();
                    string Shoot(string row, int index, Vector3 offset, out float sawFov, out float sawNod)
                    {
                        Quaternion rotation0 = view.localRotation; Vector3 position0 = view.localPosition; float fov0 = cam.fieldOfView;
                        cameraRig.SetRenderOffset308(offset.x, offset.y, offset.z);
                        seen = false; cam.Render(); renders++;
                        cameraRig.ClearRenderOffset308();
                        restored &= Same(view.localRotation, rotation0) && SameBits(cam.fieldOfView, fov0) && SameBits(view.localPosition, position0);
                        sawFov = seen ? seenFov : float.NaN; sawNod = seen ? AngleDegrees(seenRotation, rotation0) : float.NaN;
                        var active = RenderTexture.active; RenderTexture.active = rt; tile.ReadPixels(new Rect(0, 0, JudgeWidth, JudgeHeight), 0, 0); tile.Apply(false); RenderTexture.active = active;
                        string file = row + "_" + index.ToString("00") + ".png"; File.WriteAllBytes(Path.Combine(dir, file), tile.EncodeToPNG());
                        return file;
                    }
                    string Value(float v) => float.IsNaN(v) ? "null" : F(v);
                    void Frame(int index, int frameFromEvent, Vector3 on, Vector3 amplified)
                    {
                        Shoot("off", index, Vector3.zero, out _, out _);
                        Shoot("on", index, on, out float onFov, out float onNod);
                        Shoot("amp", index, amplified, out float ampFov, out float ampNod);
                        float pxPerTan = 540f / Mathf.Tan(framingFov * .5f * Mathf.Deg2Rad);
                        rows.Append(rows.Length > 0 ? "," : "").Append("{\"index\":").Append(index).Append(",\"ms\":").Append(F(frameFromEvent * dt * 1000f, "F1"))
                            .Append(",\"on\":{\"nod\":").Append(F(on.x)).Append(",\"roll\":").Append(F(on.y)).Append(",\"fov\":").Append(F(on.z)).Append(",\"renderSawFov\":").Append(Value(onFov)).Append(",\"renderSawNodDegrees\":").Append(Value(onNod))
                            .Append(",\"shiftPx1080\":").Append(F(pxPerTan * Mathf.Tan(on.x * Mathf.Deg2Rad), "F1")).Append('}')
                            .Append(",\"amp\":{\"nod\":").Append(F(amplified.x)).Append(",\"roll\":").Append(F(amplified.y)).Append(",\"fov\":").Append(F(amplified.z)).Append(",\"renderSawFov\":").Append(Value(ampFov)).Append(",\"renderSawNodDegrees\":").Append(Value(ampNod))
                            .Append(",\"shiftPx1080\":").Append(F(pxPerTan * Mathf.Tan(amplified.x * Mathf.Deg2Rad), "F1")).Append("}}");
                    }

                    int frameIndex = 0; string armNote;
                    if (closeUp)
                    {
                        // the arm rises, draws the letter's last stroke, lifts; two frames later the letter is committed and thrown
                        rig.PreviewEnd308(); rig.SetJuiceForcedOff308(false); rig.SetJuiceProfile308(armProfile);
                        var input = new WorldMacroPlayerGestureRig.GestureInput308 { Drawing = true, Stroking = false, HasPointer = true, Camera = cam, Screen = Px(strokeStart) };
                        rig.SetPreviewInput308(input); rig.PreviewModeEntered308();
                        for (int i = 0; i < 100; i++) rig.PreviewEvaluate308(StepSeconds);
                        rig.PreviewStrokeStarted308(); input.Stroking = true; const int strokeSteps = 40;
                        for (int i = 0; i <= strokeSteps; i++) { input.Screen = Px(Vector2.Lerp(strokeStart, strokeEnd, i / (float)strokeSteps)); rig.SetPreviewInput308(input); rig.PreviewEvaluate308(StepSeconds); }
                        rig.PreviewStrokeEnded308(); input.Stroking = false; rig.SetPreviewInput308(input);
                        for (int f = -JudgeFramesBefore; f < 0; f++) { rig.PreviewEvaluate308(dt); Frame(frameIndex++, f, Vector3.zero, Vector3.zero); }
                        input.Drawing = false; input.HasPointer = false; rig.SetPreviewInput308(input);
                        rig.PreviewCommitted308(true, false);
                        armNote = "close-up arm: last stroke of the stand-in letter, commit, throw (B-2 of " + profileSource + "); visible at the commit " + rig.Diagnostics.NearVisible;
                    }
                    else
                    {
                        rig.PreviewEnd308();
                        for (int f = -JudgeFramesBefore; f < 0; f++) Frame(frameIndex++, f, Vector3.zero, Vector3.zero);
                        armNote = "the rig is not driven (the call stroke itself is round P5); the body is whatever the clone shows in Edit Mode";
                    }
                    Fire(solverOn, name); Fire(solverAmp, name);   // the event: this frame is t = 0, the clocks run from the next one (as in the game)
                    Vector3 peakOn = Vector3.zero;
                    for (int f = 0; f <= JudgeFramesAfter; f++)
                    {
                        if (closeUp) rig.PreviewEvaluate308(dt);
                        solverOn.Advance(dt); solverOn.Evaluate(true, out JuiceFrame308 on); solverAmp.Advance(dt); solverAmp.Evaluate(true, out JuiceFrame308 amplified);
                        peakOn = Vector3.Max(peakOn, new Vector3(Mathf.Abs(on.Camera.x), Mathf.Abs(on.Camera.y), Mathf.Abs(on.Camera.z)));
                        Frame(frameIndex++, f, on.Camera, amplified.Camera);
                    }
                    float peakPx = 540f * Mathf.Tan(peakOn.x * Mathf.Deg2Rad) / Mathf.Tan(framingFov * .5f * Mathf.Deg2Rad);
                    File.WriteAllText(Path.Combine(dir, "judge.json"), "{\"round\":\"P8b camera reaction, judge capture\",\"event\":\"" + name + "\",\"narrow\":" + (narrow ? "true" : "false") + ",\"amp\":" + F(amp, "F2")
                        + ",\"framing\":\"" + (closeUp ? "draw close-up" : "shoulder view") + "\",\"framingFov\":" + F(framingFov) + ",\"hz\":" + JudgeHz + ",\"tile\":[" + JudgeWidth + "," + JudgeHeight + "]"
                        + ",\"wall\":{\"distanceM\":" + F(wallDistance, "F1") + ",\"cellDegrees\":" + wallData.minorEveryDegrees + ",\"heavyEveryDegrees\":" + wallData.majorEveryDegrees
                        + ",\"minorLinePx\":" + F(wallData.minorLinePx, "F2") + ",\"majorLinePx\":" + F(wallData.majorLinePx, "F2") + ",\"centreLinePx\":" + F(wallData.centreLinePx, "F2") + ",\"pxPerDegreeAtCentre\":" + F(JudgeHeight * .5f * Mathf.Tan(Mathf.Deg2Rad) / Mathf.Tan(framingFov * .5f * Mathf.Deg2Rad), "F2") + "}"
                        + ",\"profile\":\"" + profileSource.Replace("\"", "'") + ", full power\",\"standIn\":\"" + standIn.Replace("\"", "'") + "\",\"arm\":\"" + armNote.Replace("\"", "'") + "\""
                        + ",\"rows\":{\"off\":\"off_NN.png\",\"on\":\"on_NN.png (the profile's own size)\",\"amp\":\"amp_NN.png (x " + F(amp, "F1") + ", exaggerated - not a size the game shows)\"}"
                        + ",\"peak\":{\"nod\":" + F(peakOn.x) + ",\"roll\":" + F(peakOn.y) + ",\"fov\":" + F(peakOn.z) + ",\"shiftPx1080\":" + F(peakPx, "F1") + "}"
                        + ",\"frames\":[" + rows + "],\"renders\":" + renders + ",\"applied\":" + cameraRig.RenderOffsetApplied308 + ",\"conflicts\":" + cameraRig.RenderOffsetConflicts308
                        + ",\"cameraRestoredBitExact\":" + (restored ? "true" : "false") + ",\"quality\":\"" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "\"}", new UTF8Encoding(false));
                    return "cam:judge " + tag + " (" + profileSource + "; x" + F(amp, "F1") + " row is exaggerated): " + frameIndex + " frames x off / on / amp -> " + dir + " ; " + standIn + " ; " + armNote
                        + " ; peak nod " + F(peakOn.x, "F3") + " deg = " + F(peakPx, "F1") + " px at 1080p (field of view " + F(framingFov, "F1") + "), fov " + F(peakOn.z, "F3")
                        + " ; camera restored bit-exact " + restored + ", conflicts " + cameraRig.RenderOffsetConflicts308 + ", scene dirty " + scene.isDirty + " (before " + dirtyBefore + "), roots " + scene.rootCount + " (before " + rootsBefore + ")"
                        + " ; wall lines " + F(wallData.minorLinePx, "F1") + " / " + F(wallData.majorLinePx, "F1") + " px (minor / heavy) at " + JudgeWidth + " x " + JudgeHeight
                        + (seen ? "" : " ; WARNING: the per-camera probe never fired (the tiles are unconfirmed)") + " ; sheet and clip: python Tools/Unity/Stage308_juice/judge_sheet308.py";
                }
                finally
                {
                    if (probing) UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= probe;
                    try { if (stage.Rig != null) { stage.Rig.PreviewEnd308(); stage.Rig.SetJuiceProfile308(null); } } catch (Exception) { }   // the rig lets go of the capture's profile first
                    foreach (var cameraRig in stage.Root != null ? stage.Root.GetComponentsInChildren<CameraRigController>(true) : new CameraRigController[0]) cameraRig.ClearRenderOffset308();
                    if (stage.Camera != null) stage.Camera.targetTexture = stage.Target;
                    if (stage.Preview.IsValid())
                        foreach (var root in stage.Preview.GetRootGameObjects())
                            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                                if (filter.sharedMesh != null && filter.sharedMesh.name.StartsWith(FixturePrefix, StringComparison.Ordinal)) Object.DestroyImmediate(filter.sharedMesh);
                    foreach (var material in materials) if (material != null) Object.DestroyImmediate(material);
                    if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                    if (tile != null) Object.DestroyImmediate(tile);
                    if (armProfile != null) Object.DestroyImmediate(armProfile);
                    if (cameraOn != null) Object.DestroyImmediate(cameraOn);
                    if (cameraAmp != null) Object.DestroyImmediate(cameraAmp);
                }
            }
        }
    }
}
