using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #303 player body reactions (SPEC-PLAYER-FEEL-300 H).
    //   import                — Mixamo clips (Y Bot, no skin; user-approved download) → Humanoid, root baked into the pose;
    //                           the shared locomotion controller gets a weight-0 override layer "Reaction303" (backup first);
    //                           profile asset with the clip lengths
    //   attach                — (Edit, one of the three #303 scenes open) reaction / lean / death presentation on the player
    //   attach-scene:<path>   — open that scene, attach, save
    //   react-sequence[:nocam][:pitch=<deg>] — (Play, review save) scripted hits + death + get-up with game-view captures
    //                           into death/<tag>/ (cam303 | nocam303, _pitch<deg>); an existing tag folder is moved to
    //                           death/<tag>.bak_<time> first. nocam = the get-up camera measures only (no framing, no pivot
    //                           levelling: the "before" numbers). pitch = the view pitch the player holds at the lethal blow
    //                           (reproduces the gate-blocked pivot pop; clamped to the rig's pitch limits)
    //   react-status          — the sequence log (JSON: status + log)
    public static class Player303Build
    {
        const string Source = "../Art/Characters/Player303/mixamo";
        const string Folder = "Assets/_Project/Art/Characters/Player303";
        const string Controller = "Assets/_Project/Art/Characters/PlaytestNaturalLocomotion/AC_PlaytestLocomotion.controller";
        const string ProfilePath = Folder + "/PlayerReaction303.asset";
        static readonly (string file, string state)[] Clips =
        {
            ("P303_HitFront", "HitFront"), ("P303_HitBack", "HitBack"), ("P303_HitLeft", "HitLeft"), ("P303_HitRight", "HitRight"),
            ("P303_HitLarge", "HitLarge"), ("P303_DeathBackward", "Death"), ("P303_GetUpFromBack", "GetUp"),
        };

        public static string Run(string command)
        {
            if (command == "import") return Import();
            if (command == "attach") return Attach();
            if (command == "lean-status")
            {
                var lean = Object.FindFirstObjectByType<WorldMacroPlayerLean303>(); var r = Object.FindFirstObjectByType<WorldMacroPlayerReaction303>();
                if (lean == null) return "no lean component";
                var m = lean.Motor; var an = lean.Animator; var spine = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Spine) : null;
                return $"enabled {lean.isActiveAndEnabled} roll {lean.Roll:F2} pitch {lean.Pitch:F2} motor {(m != null)} speed {(m != null ? m.ActualLocalVelocity.magnitude : 0):F2} yaw {(m != null ? m.ActualYawSpeed : 0):F1} dodging {(m != null && m.IsDodging)} drawing {(m != null && m.IsDrawing)} " +
                       $"human {(an != null && an.isHuman)} spine {(spine != null ? spine.name : "-")} reaction {(r != null ? r.State + " " + r.Weight.ToString("F2") : "-")}";
            }
            if (command == "react-sequence" || command.StartsWith("react-sequence:")) return ReactSequence(command.Length > 15 ? command.Substring(15) : "");
            if (command == "react-status") return JsonUtility.ToJson(reactState ?? new ReactState(), true);
            if (command == "clipinfo")
            {
                var sb = new System.Text.StringBuilder();
                foreach (var (file, state) in Clips)
                {
                    var clip = AssetDatabase.LoadAllAssetsAtPath(Folder + "/Animations/" + file + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                    var b = AnimationUtility.GetCurveBindings(clip).FirstOrDefault(x => x.propertyName == "RootT.y");
                    var bz = AnimationUtility.GetCurveBindings(clip).FirstOrDefault(x => x.propertyName == "RootT.z");
                    var cy = b.propertyName != null ? AnimationUtility.GetEditorCurve(clip, b) : null; var cz = bz.propertyName != null ? AnimationUtility.GetEditorCurve(clip, bz) : null;
                    sb.Append(file + " " + clip.length.ToString("F2") + "s y:");
                    for (float t = 0; t <= clip.length + 1e-3f; t += .25f) sb.Append(" " + (cy != null ? cy.Evaluate(t).ToString("F2") : "-"));
                    sb.Append(" | z:"); for (float t = 0; t <= clip.length + 1e-3f; t += .5f) sb.Append(" " + (cz != null ? cz.Evaluate(t).ToString("F2") : "-"));
                    sb.AppendLine();
                }
                return sb.ToString();
            }
            if (command.StartsWith("attach-scene:"))
            {
                string path = command.Substring(13);
                if (!Roadside303.Scenes303.Contains(path)) throw new Exception("not a #303 scene: " + path);
                if (SceneManager.GetActiveScene().isDirty) throw new Exception("the open scene has unsaved changes");
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                return Attach();
            }
            throw new ArgumentException("Player303Build: import | attach | attach-scene:<path> | react-sequence[:nocam][:pitch=<deg>] | react-status | lean-status | clipinfo");
        }

        // react-sequence (Play, review save): scripted hits on the live player through PlayerVitals (the real damage path):
        // front, left, right, back small hits, one large stagger, then a lethal blow -> the death presentation and the
        // respawn get-up. The attacker direction comes from a marker placed around the player. Film it alongside.
        // H.1 (RESPAWN_CAMERA_PLAN §4.1): captures go to death/<tag>/ so the 2026-09-29 frames in death/ stay the "before"
        // set; every capture logs the get-up camera numbers and the end logs the jump / pitch-on-return / release summary.
        [Serializable] class ReactState { public string status = "idle"; public System.Collections.Generic.List<string> log = new System.Collections.Generic.List<string>(); }
        static ReactState reactState; static float reactStart; static int reactStep; static Transform threatMarker;
        static readonly System.Collections.Generic.HashSet<int> captured = new System.Collections.Generic.HashSet<int>();
        static readonly (float at, float angle, float share)[] ReactPlan = { (.8f, 0f, .06f), (2.3f, -90f, .06f), (3.8f, 90f, .06f), (5.3f, 180f, .06f), (6.9f, 0f, .3f), (9.2f, 0f, 2f) };
        // seconds after the lethal blow: fall, veil closed, lifting (3.2 / 3.6 framing), rising (4.6 / 5.0 return), input back, standing
        static readonly (int k, float when)[] DeathCaptures = { (0, .9f), (1, 2.1f), (2, 2.9f), (3, 3.2f), (4, 3.6f), (5, 4.6f), (6, 5.0f), (7, 5.6f), (8, 6.5f) };
        const float ReactDoneAfter = 6.8f;
        const string DeathRoot = "../Art/PlaytestRecovery/Player303/death";
        static string reactFolder; static float? reactPitch; static bool reactPitchSet, reactRunningLogged, reactDeathSeen, reactDeathLogged;
        static WorldMacroPlayerGetUpCamera303 reactCamera;

        static string ReactSequence(string options)
        {
            bool nocam = false; float? pitch = null;
            foreach (var raw in options.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string o = raw.Trim();
                if (o == "nocam") nocam = true;
                else if (o.StartsWith("pitch=") && float.TryParse(o.Substring(6), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float p)) pitch = p;
                else throw new ArgumentException("react-sequence options: nocam | pitch=<deg> (got '" + o + "')");
            }
            if (!EditorApplication.isPlaying) throw new Exception("react-sequence: Play mode only");
            var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>() ?? throw new Exception("no session");
            if (string.IsNullOrEmpty(s.TestSaveSuffix)) throw new Exception("refusing to run on the real save");
            var r = Object.FindFirstObjectByType<WorldMacroPlayerReaction303>();
            WorldMacroPlayerGetUpCamera303 cam = null;
            if (r != null) cam = r.GetUpCamera != null ? r.GetUpCamera : r.GetComponent<WorldMacroPlayerGetUpCamera303>();
            if (nocam && cam == null) throw new Exception("react-sequence:nocam needs the get-up camera (no reaction or no profile)");
            ReleaseCamera();   // a restarted run
            string tag = (nocam ? "nocam303" : "cam303") + (pitch.HasValue ? "_pitch" + pitch.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "");
            string folder = Abs(DeathRoot + "/" + tag), moved = BackUpFolder(folder);
            Directory.CreateDirectory(folder);
            reactState = new ReactState { status = "running" }; reactStart = Time.time; reactStep = 0; captured.Clear();
            reactFolder = folder; reactPitch = pitch; reactPitchSet = reactRunningLogged = reactDeathSeen = reactDeathLogged = false;
            reactCamera = cam; if (cam != null) cam.DebugMeasureOnly = nocam;
            reactState.log.Add("mode " + tag + (nocam ? " (measure only)" : "") + " captures " + folder + (moved != null ? "; previous run moved to " + moved : "") + (cam == null ? "; get-up camera missing" : ""));
            if (threatMarker == null) threatMarker = new GameObject("ReactThreat303").transform;
            EditorApplication.update -= ReactTick; EditorApplication.update += ReactTick;
            return "react-sequence (" + tag + ") started at game time " + reactStart.ToString("F2");
        }

        static void ReactTick()
        {
            if (reactState == null || reactState.status != "running") { EditorApplication.update -= ReactTick; return; }
            if (!EditorApplication.isPlaying) { reactState.status = "aborted: Play mode ended"; reactCamera = null; EditorApplication.update -= ReactTick; return; }
            var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>(); var r = Object.FindFirstObjectByType<WorldMacroPlayerReaction303>();
            if (s == null || r == null) { reactState.status = "error: no session/reaction"; ReleaseCamera(); return; }
            var body = s.Walker.Body.transform; var vitals = body.GetComponent<Oheangbu.Combat.PlayerVitals>();
            float t = Time.time - reactStart;
            float dAt = ReactPlan[ReactPlan.Length - 1].at;
            // pitch=<deg>: the player holds this view pitch at the lethal blow (the motor writes it to the pivot until the gate blocks)
            if (reactPitch.HasValue && !reactPitchSet && reactStep == ReactPlan.Length - 1 && t >= dAt - .5f)
            { reactPitchSet = true; reactState.log.Add($"t={t:F2} " + SetViewPitch(s.Walker, reactPitch.Value)); }
            if (reactStep < ReactPlan.Length && t >= ReactPlan[reactStep].at)
            {
                var (at, angle, share) = ReactPlan[reactStep];
                threatMarker.position = body.position + Quaternion.AngleAxis(angle, Vector3.up) * body.forward * 2.5f; r.DebugThreat = threatMarker;
                bool hit = vitals.TakeAttackDamage(share * vitals.MaxHp, Oheangbu.Combat.IncomingDamageKind.Melee);
                reactState.log.Add($"t={t:F2} angle={angle} share={share} hit={hit} state={r.State} weight={r.Weight:F2} hp={vitals.Hp01:F2}");
                reactStep++;
            }
            // game-view captures (UI veil included) through the death: fall, veil closed, lifting over the get-up, standing
            foreach (var (k, when) in DeathCaptures)
                if (!captured.Contains(k) && t >= dAt + when)
                {
                    captured.Add(k); Directory.CreateDirectory(reactFolder);
                    ScreenCapture.CaptureScreenshot(Path.Combine(reactFolder, $"death_{k}_{when:F1}s.png"));
                    reactState.log.Add($"t={t:F2} capture {k} veil {(s.DeathPresentation != null ? s.DeathPresentation.VeilAlpha : -1):F2} lying {r.Lying} gettingUp {r.GettingUp} p {r.GetUpProgress:F2} | {CameraLine(s.Walker)}");
                }
            // the presentation's own finish (input returns ~2 neutral frames later); unchanged timing is part of the regression check
            if (s.DeathPresentation != null)
            {
                if (s.DeathPresentation.IsActive) reactDeathSeen = true;
                else if (reactDeathSeen && !reactDeathLogged) { reactDeathLogged = true; reactState.log.Add($"t={t:F2} death presentation finished {t - dAt:F2}s after the lethal blow"); }
            }
            if (reactStep >= ReactPlan.Length && t > dAt + ReactDoneAfter && s.DeathPresentation != null && !s.DeathPresentation.IsActive && !s.DeathRespawnPending)
            {
                reactState.log.Add($"t={t:F2} death presentation done (completed {s.DeathPresentation.Completed}) at {body.position} hp={vitals.Hp01:F2}");
                reactState.log.Add($"t={t:F2} {CameraSummary(dAt)}");
                r.DebugThreat = null; ReleaseCamera(); reactState.status = "done"; EditorApplication.update -= ReactTick;
            }
            else if (reactStep >= ReactPlan.Length && s.DeathPresentation != null && s.DeathPresentation.IsActive && !reactRunningLogged)
            {
                reactRunningLogged = true;
                reactState.log.Add($"t={t:F2} death presentation running, veil {s.DeathPresentation.VeilAlpha:F2}, lying {r.Lying}");
            }
        }

        static void ReleaseCamera() { if (reactCamera != null) reactCamera.DebugMeasureOnly = false; reactCamera = null; }

        static float PivotPitch(WorldMacroCombatWalker w)
        {
            var pivot = w != null && w.ViewCamera != null ? w.ViewCamera.transform.parent : null;
            return pivot != null ? Mathf.DeltaAngle(0f, pivot.localEulerAngles.x) : float.NaN;
        }

        // per capture: active / weight / first person, the focus in the rendered view (z < 0: behind the camera), framing
        // distance (< GetUpCameraDistance = pulled in by a ceiling), release, first-person inputs, last applied frame
        static string CameraLine(WorldMacroCombatWalker w)
        {
            var c = reactCamera;
            if (c == null) return "cam -";
            var vp = c.FocusViewport;
            string applied = c.LastAppliedFrame >= 0 ? (Time.frameCount - c.LastAppliedFrame) + "f ago" : "never";
            return $"cam active {c.Active} w {c.Weight:F2} fp {c.FirstPerson} focusVp ({vp.x:F2},{vp.y:F2},{vp.z:F1}) dist {c.FramingDistance:F2} released {c.ReleasedByInput} " +
                   $"eyeH {c.EyeHeight:F2} lookUp {c.LookUp:F1} applied {applied} pivotPitch {PivotPitch(w):F2} bodyVis {(w.CameraRig != null && w.CameraRig.IsBodyVisible)}";
        }

        static string CameraSummary(float dAt)
        {
            var c = reactCamera;
            if (c == null) return "camera summary: no get-up camera";
            return $"camera summary: measureOnly {c.DebugMeasureOnly} starts {c.Starts} leveled {c.PivotLeveled} fp {c.FirstPerson} maxStep {c.MaxStepDegrees:F2}deg maxMove {c.MaxStepMetres:F3}m " +
                   $"pitchBeforeReturn {c.PitchBeforeReturn:F2} pivotAtRelease {c.PivotPitchAtRelease:F2} returnJump {c.ReturnPitchJump:F2} released {c.ReleasedByInput} " +
                   $"inputBack {(c.InputReturnedAt >= 0f ? "+" + (c.InputReturnedAt - reactStart - dAt).ToString("F2") + "s" : "-")}";
        }

        // test hook for pitch=<deg>: PlayerMotor keeps its pitch private; the rig's limits clamp it like a look input would
        static string SetViewPitch(WorldMacroCombatWalker w, float degrees)
        {
            var m = w != null ? w.Motor : null;
            var field = typeof(Oheangbu.Combat.PlayerMotor).GetField("_pitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (m == null || field == null || field.FieldType != typeof(float)) return "pitch hook unavailable (PlayerMotor._pitch not found)";
            float min = -80f, max = 80f;
            if (w.CameraRig != null) w.CameraRig.GetPitchLimits(out min, out max);
            float v = Mathf.Clamp(degrees, min, max);
            field.SetValue(m, v);
            return $"view pitch {v:F1} (asked {degrees:F1}, limits {min:F0}..{max:F0}); pivot now {PivotPitch(w):F2}, the motor writes it next frame";
        }

        // an earlier run of the same tag is kept, not overwritten
        static string BackUpFolder(string folder)
        {
            if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any()) return null;
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture), dest = folder + ".bak_" + stamp;
            for (int i = 2; Directory.Exists(dest); i++) dest = folder + ".bak_" + stamp + "_" + i;
            Directory.Move(folder, dest);
            return dest;
        }

        static string Abs(string p) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", p));

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string Import()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode only");
            EnsureFolder(Folder + "/Animations");
            var clips = new AnimationClip[Clips.Length];
            for (int i = 0; i < Clips.Length; i++)
            {
                var (file, state) = Clips[i];
                string src = Path.Combine(Abs(Source), file + ".fbx"), dst = Folder + "/Animations/" + file + ".fbx";
                if (!File.Exists(src)) throw new Exception("missing " + src);
                if (!File.Exists(Abs(dst))) { File.Copy(src, Abs(dst)); AssetDatabase.ImportAsset(dst); }
                var mi = (ModelImporter)AssetImporter.GetAtPath(dst);
                mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = true;
                mi.SaveAndReimport();
                var take = mi.defaultClipAnimations.FirstOrDefault() ?? throw new Exception("no take in " + dst);
                // the body's travel stays in the pose; the capsule never moves with a reaction
                // the fall keeps its travel in the pose (the body drops a metre behind the capsule); every other reaction stays
                // centred on the capsule (travel extracted as root motion, which the player animator discards) so fading
                // back to locomotion never slides the body home
                bool travel = state == "Death";
                take.name = file; take.loopTime = false;
                take.lockRootRotation = true; take.keepOriginalOrientation = true;
                take.lockRootHeightY = true; take.keepOriginalPositionY = false; take.heightFromFeet = true;
                take.lockRootPositionXZ = travel; take.keepOriginalPositionXZ = travel;
                // the get-up lies still for its first 2.3 s and stands idle after 6.3 s (RootT.y, clipinfo)
                if (state == "GetUp") { take.firstFrame = 69; take.lastFrame = 189; }
                // the fall: a 1.5 s stagger before the body drops; keep 0.7 s of it so the drop shows before the veil closes
                if (state == "Death") { take.firstFrame = 24; take.lastFrame = 109; }
                mi.clipAnimations = new[] { take }; mi.SaveAndReimport();
                clips[i] = AssetDatabase.LoadAllAssetsAtPath(dst).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            }

            // controller: back up, then (re)build the Reaction303 layer
            string backup = Abs("../Art/PlaytestRecovery/Player303/backup"); Directory.CreateDirectory(backup);
            string backupFile = Path.Combine(backup, "AC_PlaytestLocomotion.controller.before303");
            if (!File.Exists(backupFile)) File.Copy(Abs(Controller), backupFile);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller) ?? throw new Exception("missing " + Controller);
            var layers = controller.layers.ToList();
            int existing = layers.FindIndex(l => l.name == "Reaction303");
            if (existing >= 0) controller.RemoveLayer(existing);
            var sm = new AnimatorStateMachine { name = "Reaction303", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);
            var empty = sm.AddState("Empty"); empty.writeDefaultValues = false; sm.defaultState = empty;
            for (int i = 0; i < Clips.Length; i++)
            {
                var st = sm.AddState(Clips[i].state, new Vector3(260, 60 * i, 0)); st.motion = clips[i]; st.writeDefaultValues = false;
            }
            controller.AddLayer(new AnimatorControllerLayer { name = "Reaction303", stateMachine = sm, defaultWeight = 0f, blendingMode = AnimatorLayerBlendingMode.Override, iKPass = false });
            EditorUtility.SetDirty(controller);

            var profile = AssetDatabase.LoadAssetAtPath<PlayerReaction303Profile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<PlayerReaction303Profile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            else { var fresh = ScriptableObject.CreateInstance<PlayerReaction303Profile>(); EditorUtility.CopySerialized(fresh, profile); Object.DestroyImmediate(fresh); }   // TEST values live in the class defaults for now
            profile.name = Path.GetFileNameWithoutExtension(ProfilePath);
            profile.DeathSeconds = clips[5].length; profile.GetUpSeconds = clips[6].length;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return "imported " + string.Join(", ", clips.Select(c => c.name + " " + c.length.ToString("F2"))) + "; controller layer Reaction303 (" + Clips.Length + " states); profile " + ProfilePath + "; backup " + backupFile;
        }

        static string Attach()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode only");
            var scene = SceneManager.GetActiveScene();
            if (!Roadside303.Scenes303.Contains(scene.path)) throw new Exception("open one of the #303 scenes");
            var profile = AssetDatabase.LoadAssetAtPath<PlayerReaction303Profile>(ProfilePath) ?? throw new Exception("run import first");
            var session = Object.FindFirstObjectByType<WorldMacroPlaytestSession>() ?? throw new Exception("no session");
            var appearance = Object.FindObjectsByType<WorldMacroPlayerAppearance>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(a => a.gameObject.scene == scene) ?? throw new Exception("no player appearance");
            var go = appearance.gameObject; var animator = appearance.Animator ?? go.GetComponent<Animator>();
            var reaction = go.GetComponent<WorldMacroPlayerReaction303>() ?? go.AddComponent<WorldMacroPlayerReaction303>();
            reaction.Profile = profile; reaction.Animator = animator; reaction.Session = session;
            var lean = go.GetComponent<WorldMacroPlayerLean303>() ?? go.AddComponent<WorldMacroPlayerLean303>();
            lean.Profile = profile; lean.Animator = animator; lean.Motor = session.Walker.Motor; lean.Drawing = session.Walker.Drawing; lean.Reaction = reaction;
            var death = go.GetComponent<WorldMacroPlayerDeath303>() ?? go.AddComponent<WorldMacroPlayerDeath303>();
            death.Session = session; death.Reaction = reaction; death.Profile = profile;
            session.DeathPresentation = death;
            foreach (var c in new Object[] { reaction, lean, death, session }) EditorUtility.SetDirty(c);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return "attached reaction / lean / death presentation to " + go.name + " in " + scene.path + " (motor " + (lean.Motor != null) + ", drawing " + (lean.Drawing != null) + ")";
        }
    }
}
