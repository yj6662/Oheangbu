using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.App.Demo;
using System.Reflection;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    [InitializeOnLoad]
    public static class FolkloreRuntimeChecks298
    {
        const string Key = "Folklore298.Runtime";
        static readonly string[] Ids = { "dokkaebi", "agwi", "changgui", "bulgasari", "fox_spirit", "imugi" };
        static string Folder => Path.Combine(CompactFolklore298.OutputRoot, "Runtime");
        static WorldMacroPlaytestSession Session => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        [Serializable] public sealed class Stamp { public string path, sha; }
        [Serializable] public sealed class ActorResult
        {
            public string id; public Vector3 home, playerStart, playerEnd, enemyEnd;
            public int alerts, telegraphs, impacts, moveFrames, settleFrames; public float damage, rootDistance, playerRootDeltaY, inputDistance, inputSeconds;
            public bool moveReleased; public string moveReleaseReason;
            public bool walkPose, attackPose, hitPose, stunPose, deathPose, restored;
            public string footState; public int footRays;
            public int footMaxRays, footSupportedFrames, footCorrectedFrames, footWalkingCorrectedFrames, footAppliedSamples;
            public int audioPlayed, audioFallback, audioDropped, audioPlayedStart, audioFallbackStart, audioDroppedStart;
            public string audioStatus, audioLastRole, audioLastDropReason, audioReadiness;
            public int audioFocusChanges, audioLastFocusFrame, inputNonzeroFrames, inputDisabledFrames;
            public List<InputSample> inputSamples = new List<InputSample>();
            public List<string> audioRoles = new List<string>();
            public bool attackCaptured; public int contacts, sampledFrames; public float walkedDistance;
            public List<FrameSample> samples = new List<FrameSample>();
            public List<ContactSample> attackEvents = new List<ContactSample>();
        }
        [Serializable] public sealed class InputSample
        {
            public int frame; public float gameTime; public string stage, focusedWindow, audioReadiness, inputUpdate;
            public bool applicationFocused, gameViewFocused, motorEnabled, controllerEnabled, inputBlocked, environmentBlocked, keyboardEnabled, keyboardCurrent, moveEnabled;
            public Vector2 move; public Vector3 player, motorVelocity;
        }
        [Serializable] public sealed class FrameSample
        {
            public int frame; public string pose, behaviour; public float poseTime, hp, playerHp, range, distance, navRemaining, gameTime, deltaTime, navStoppingDistance;
            public Vector3 root, player, hand, head, navVelocity, navDesiredVelocity; public bool telegraph, recovery, navStopped; public bool sight=true; public string blocker="";
        }
        [Serializable] public sealed class ContactSample
        {
            public string kind, outcome; public int frame; public float gameTime, currentDistance, originDistance, horizontalAngle, appliedDamage, playerHp, navStoppingDistance;
            public Vector3 root, player, lockedOrigin, lockedTarget, lockedForward, navVelocity, navDesiredVelocity;
            public bool inShape, lineOfSight, attackEnabled, navStopped;
        }
        [Serializable] public sealed class State
        {
            public bool active, finished, restored, restart, priorDirect, priorDirectPresent, fatalLog, interrupted, focusReady;
            public string interruptReason, priorFocusedWindowType; public int priorFocusedWindowId;
            public double focusLossAt = -1; public List<InputSample> focusEvents = new List<InputSample>();
            public string status, suffix, priorSuffix, priorUi, priorStartup, sceneSha, savedPath, expectedCheckpoint, sourceSha;
            public int phase, index, boots, lastFrame = -1; public double began, at, lastSample; public float gameAt; public uint revision;
            public float ambientIntensity, reflectionIntensity; public int ambientMode;
            public Color ambientSky, ambientEquator, ambientGround;
            public List<Stamp> saves = new List<Stamp>(); public List<string> checks = new List<string>(), failures = new List<string>();
            public List<ActorResult> actors = new List<ActorResult>();
            public string scope = "Actual candidate Play. Initial placement uses safe Teleport; short approach uses virtual WASD through the existing PlayerMotor. Natural perception/Nav/attack events are observed. Hit/stun/death use public combat APIs, followed by actual rest and a second Play boot. Audio verifies loaded clips and accepted emitter/pool playback requests from these events, not captured DSP output or listening. No human input, boss AI victory, or art approval is implied.";
        }
        static State state;
        static PrologueEncounter current;
        static EnemyController combat;
        static EnemyRigMotion298 motion;
        static GameObject runtimeProbe;
        static Keyboard keyboard, priorKeyboard; static Mouse mouse, priorMouse;
        static InputActionAsset actions; static InputDevice[] priorDevices; static Key[] keys = Array.Empty<Key>();
        static FolkloreRuntimeChecks298()
        {
            var saved = SessionState.GetString(Key, ""); if (saved.Length > 0) state = JsonUtility.FromJson<State>(saved);
            EditorApplication.update += Tick; EditorApplication.playModeStateChanged += Changed; Application.logMessageReceived += Logged;
        }
        public static string Run(string command)
        {
            if (command == "status") return Summary();
            if (command == "abort") { if (state?.active == true) { Check(false, "Explicit abort"); Stop(false); } return Summary(); }
            if (command != "start") throw new ArgumentException(command);
            if (state?.active == true || EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty || SceneManager.GetActiveScene().path != CompactFolklore298.ScenePath) throw new Exception("Saved298 Edit candidate required");
            if (Session == null || Session.Content.SaveSlot != "world-folklore-298") throw new Exception("Private candidate content required");
            Directory.CreateDirectory(Folder); Directory.CreateDirectory(Application.persistentDataPath);
            string archive = Path.Combine(Folder, "History", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")); Directory.CreateDirectory(archive);
            foreach (var file in Directory.GetFiles(Folder)) File.Copy(file,Path.Combine(archive,Path.GetFileName(file)),true);
            state = new State { active = true, status = "STARTING", suffix = "_qa_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"), priorSuffix = Session.TestSaveSuffix,
                priorFocusedWindowId = EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetInstanceID() : 0, priorFocusedWindowType = EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetType().FullName : "",
                priorUi = SessionState.GetString("PlaytestUiReviewSuffix", "__missing__"), priorStartup = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                priorDirect = SessionState.GetBool("Compact270.DirectPlay", false), priorDirectPresent = SessionState.GetBool("Compact270.DirectPlay", false) == SessionState.GetBool("Compact270.DirectPlay", true),
                began = EditorApplication.timeSinceStartup, sceneSha = Hash(CompactFolklore298.ScenePath), sourceSha = Hash(CompactFolklore298.SourceScene),
                ambientIntensity=RenderSettings.ambientIntensity,reflectionIntensity=RenderSettings.reflectionIntensity,ambientMode=(int)RenderSettings.ambientMode,
                ambientSky=RenderSettings.ambientSkyColor,ambientEquator=RenderSettings.ambientEquatorColor,ambientGround=RenderSettings.ambientGroundColor };
            foreach (var file in Directory.GetFiles(Application.persistentDataPath, "*.json*")) state.saves.Add(new Stamp { path = file, sha = Hash(file) });
            state.savedPath = Path.Combine(Application.persistentDataPath, Session.Content.SaveSlot + state.suffix + ".json");
            if (new[]{"", ".bak", ".tmp"}.Any(end=>File.Exists(state.savedPath+end))) throw new Exception("Unique isolated filename collision; nothing deleted");
            Persist(); try { Start(); } catch(Exception e) { Check(false,e.ToString());Stop(false); } return Summary();
        }
        static string Summary() => state == null ? "No298 runtime run" : state.status + " phase=" + state.phase + " actor=" + state.index + " checks=" + state.checks.Count + " failures=" + state.failures.Count + " restored=" + state.restored;
        static void Persist() { SessionState.SetString(Key, JsonUtility.ToJson(state)); File.WriteAllText(Path.Combine(Folder, "checks.json"), JsonUtility.ToJson(state, true)); }
        static void Check(bool ok, string text) { state.checks.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) state.failures.Add(text); }
        static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SceneManager.GetActiveScene().path != CompactFolklore298.ScenePath || Hash(CompactFolklore298.ScenePath) != state.sceneSha) throw new Exception("Candidate changed before Play start");
            Session.TestSaveSuffix = state.suffix; SessionState.SetString("PlaytestUiReviewSuffix", state.suffix); CompactLoadingStartup270.UseCurrent();
            state.restart = false; state.status = "ENTERING"; state.at = EditorApplication.timeSinceStartup; FocusGameView(); Persist(); EditorApplication.EnterPlaymode();
        }
        static void Stop(bool restart) { Unbind(); EndInput(); if(runtimeProbe!=null)Object.DestroyImmediate(runtimeProbe);runtimeProbe=null; state.restart = restart; state.status = restart ? "RESTARTING" : "STOPPING"; Persist(); if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.ExitPlaymode();else Changed(PlayModeStateChange.EnteredEditMode); }
        static void Logged(string message,string trace,LogType type)
        { if(state?.active==true && (type==LogType.Exception || type==LogType.Error || type==LogType.Assert)) { state.fatalLog=true;Check(false,type+": "+message+"\n"+trace); Persist(); } }
        static void RestoreStartup()
        {
            if(state==null || state.active || EditorApplication.isPlayingOrWillChangePlaymode)return;
            if (state.priorDirectPresent) SessionState.SetBool("Compact270.DirectPlay", state.priorDirect); else SessionState.EraseBool("Compact270.DirectPlay");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(state.priorStartup) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(state.priorStartup);
        }
        static void Changed(PlayModeStateChange mode)
        {
            if(state?.active!=true)return;
            if(mode==PlayModeStateChange.EnteredPlayMode){state.boots++;state.lastFrame=-1;state.status="RUNNING";state.at=EditorApplication.timeSinceStartup;state.focusReady=false;state.focusLossAt=-1;FocusGameView();Persist();return;}
            if(mode!=PlayModeStateChange.EnteredEditMode)return;
            if(state.restart && state.failures.Count==0){state.status="RESTART_PENDING";state.at=EditorApplication.timeSinceStartup;Persist();return;}
            if(!state.finished && state.failures.Count==0)Check(false,"Play exited before all phases completed");
            try
            {
                Unbind();EndInput();
                if (Session != null) Session.TestSaveSuffix = state.priorSuffix;
                if (state.priorUi == "__missing__") SessionState.EraseString("PlaytestUiReviewSuffix"); else SessionState.SetString("PlaytestUiReviewSuffix", state.priorUi);
                foreach(var end in new[]{"", ".bak", ".tmp"})if(File.Exists(state.savedPath+end)){File.Copy(state.savedPath+end,Path.Combine(Folder,"isolated-final.json"+end),true);File.Delete(state.savedPath+end);}
                Check(state.saves.All(s => File.Exists(s.path) && Hash(s.path) == s.sha), "Every pre-existing save preserved byte-identical");
                Check(Directory.GetFiles(Application.persistentDataPath,"*.json*").All(p=>state.saves.Any(s=>s.path==p)),"No new ordinary or temporary save remains after fixture");
                Check(Hash(CompactFolklore298.ScenePath) == state.sceneSha && Hash(CompactFolklore298.SourceScene)==state.sourceSha, "Candidate and original296 scene files preserved byte-identical");
                Check(Session!=null&&Session.TestSaveSuffix==state.priorSuffix,"Original Edit suffix restored");
                state.active=false;RestoreStartup();EditorApplication.delayCall+=RestoreStartup;
                Check(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)==state.priorStartup&&SessionState.GetBool("Compact270.DirectPlay",false)==state.priorDirect,"Prior startup scene/direct-play setting restored");state.restored=true;
            }
            catch(Exception e){Check(false,"Cleanup: "+e);state.active=false;}
            state.status=state.interrupted?"INTERRUPTED_FOCUS":state.finished&&state.restored&&state.failures.Count==0?"PASS":"FAIL_OR_INCOMPLETE";Persist();EditorApplication.delayCall+=RestoreEditorFocus;
        }
        static ActorResult Result => state.actors[state.index];
        static void Alert() { Result.alerts++; SnapshotAudio(); }
        static void Telegraph(EnemyAttackCue cue) { Result.telegraphs++; SnapshotAudio(); RecordContact("telegraph",false,0,""); }
        static void Impact(EnemyAttackImpact impact) { Result.impacts++; Result.damage += impact.AppliedDamage; if (impact.InShape) Result.contacts++; RecordContact("impact",impact.InShape,impact.AppliedDamage,impact.Outcome.ToString()); }
        static void RecordContact(string kind, bool inShape, float damage, string outcome)
        {
            Vector3 PrivateVector(string name) => (Vector3)typeof(EnemyController).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(combat);
            var origin=PrivateVector("_authoredOrigin");var forward=PrivateVector("_authoredForward");var player=current.Player.position;var delta=player-origin;delta.y=0;
            var nav=current.GetComponent<NavMeshAgent>();var pv=Session.Walker.Body.GetComponent<PlayerVitals>();
            Result.attackEvents.Add(new ContactSample {kind=kind,outcome=outcome,frame=Time.frameCount,gameTime=Time.time,root=current.transform.position,player=player,
                lockedOrigin=origin,lockedTarget=combat.AuthoredTargetPoint,lockedForward=forward,currentDistance=Vector3.Distance(current.transform.position,player),originDistance=Vector3.Distance(origin,player),
                horizontalAngle=Vector3.Angle(forward,delta),inShape=inShape,appliedDamage=damage,playerHp=pv.Hp01*pv.MaxHp,lineOfSight=combat.HasLineOfSight(),attackEnabled=combat.AttackEnabled,
                navStopped=nav.isStopped,navStoppingDistance=nav.stoppingDistance,navVelocity=nav.velocity,navDesiredVelocity=nav.desiredVelocity});
        }
        static void SnapshotAudio()
        {
            if(current==null)return;var audio=current.GetComponent<EnemyAudioEmitter298>();var row=Result;
            if(audio==null){row.audioStatus="MISSING_EMITTER";return;}
            int played=audio.Played-row.audioPlayedStart;
            if(played>row.audioPlayed&&!string.IsNullOrEmpty(audio.LastRole)&&!row.audioRoles.Contains(audio.LastRole))row.audioRoles.Add(audio.LastRole);
            row.audioPlayed=played;row.audioFallback=audio.FallbackPlayed-row.audioFallbackStart;row.audioDropped=audio.Dropped-row.audioDroppedStart;
            row.audioStatus=audio.Status;row.audioLastRole=audio.LastRole;row.audioLastDropReason=audio.LastDropReason;
            if(audio.Soundscape!=null){row.audioReadiness=audio.Soundscape.EnemyAudioState298;row.audioFocusChanges=audio.Soundscape.AudioFocusChanges298;row.audioLastFocusFrame=audio.Soundscape.AudioLastFocusFrame298;}
        }
        static void Unbind()
        {
            if (current != null) current.PlayerDetected -= Alert;
            if (combat != null) { combat.AttackTelegraphed -= Telegraph; combat.AttackImpactResolved -= Impact; }
            current = null; combat = null; motion = null;
        }
        static void BeginActor(WorldMacroPlaytestSession s)
        {
            FocusGameView(); Unbind(); current = s.Actors.Single(a => a.Id == "folklore298/" + Ids[state.index]);
            combat = current.GetComponent<EnemyController>(); motion = current.GetComponent<EnemyRigMotion298>();
            var row = new ActorResult { id = current.Id, home = current.transform.position }; state.actors.Add(row);
            var audio=current.GetComponent<EnemyAudioEmitter298>();
            if(audio!=null){row.audioPlayedStart=audio.Played;row.audioFallbackStart=audio.FallbackPlayed;row.audioDroppedStart=audio.Dropped;}
            var nav = current.GetComponent<NavMeshAgent>(); bool found = false;
            foreach (float angle in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f })
            {
                var probe = current.transform.position - Vector3.up * nav.baseOffset + Quaternion.Euler(0, angle, 0) * Vector3.forward * 7;
                if (!s.TrySafeFeet(probe, out var feet)) continue;
                var path = new NavMeshPath(); if (!NavMesh.CalculatePath(current.transform.position, feet, nav.areaMask, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                var towards = Vector3.ProjectOnPlane(current.transform.position - feet, Vector3.up); s.Teleport(feet, Quaternion.LookRotation(towards).eulerAngles.y); Physics.SyncTransforms();
                if (!combat.HasLineOfSight()) continue; found = true; break;
            }
            if (!found) throw new Exception(current.Id + " no safe approach");
            s.CombatActive = false; s.Cull(); s.Walker.Body.GetComponent<PlayerVitals>().Restore();
            current.PlayerDetected += Alert; combat.AttackTelegraphed += Telegraph; combat.AttackImpactResolved += Impact;
            row.playerStart = s.Walker.Body.transform.position;
            Check(current.gameObject.activeInHierarchy && nav.isOnNavMesh && motion.IsConfigured, row.id + " live Nav and motion configured");
            // Settle actual gravity at the safe start before any virtual movement or perception.
            BeginInput(s); keys = Array.Empty<Key>(); state.phase = -1; state.at = EditorApplication.timeSinceStartup; state.gameAt=Time.time; Persist();
        }
        static void Tick()
        {
            if (state?.active != true || EditorApplication.isCompiling) return;
            if(state.fatalLog){Stop(false);return;}
            if (state.status == "RESTART_PENDING" && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isUpdating && EditorApplication.timeSinceStartup-state.at>.25) { try { Start(); } catch(Exception e) { Check(false,e.ToString());Changed(PlayModeStateChange.EnteredEditMode); } return; }
            if (state.status != "RUNNING" || !EditorApplication.isPlaying) return;
            if (state.lastFrame == Time.frameCount) return; state.lastFrame = Time.frameCount;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - state.began > 600) throw new TimeoutException("298 runtime deadline");
                var s = Session; if (s == null || !s.InitializationComplete) { if (now - state.at > 90) throw new TimeoutException("Session init"); return; }
                if (s.TestSaveSuffix != state.suffix || s.Content.SaveSlot != "world-folklore-298" || SceneManager.GetActiveScene().path != CompactFolklore298.ScenePath) throw new Exception("Isolated candidate scope lost");
                if (PlaytestUiRoot.Instance?.LoadingInProgress == true) return;
                if (!ObserveFocus(s, now)) return;
                var ui = PlaytestUiRoot.Instance; if (ui != null) { ui.CloseMenu(); ui.Gate.ReleaseImmediately(); } Time.timeScale = 1;
                if (state.phase == 0)
                {
                    if(runtimeProbe==null){runtimeProbe=new GameObject("Folklore298 runtime observation");runtimeProbe.hideFlags=HideFlags.HideAndDontSave;runtimeProbe.AddComponent<FolkloreRuntimeProbe298>();}
                    Check(s.Actors.Count(a => a.Id.StartsWith("folklore298/")) == 6 && s.Actors.Length == 16, "Six new actors plus original nine (+ #306 mine tutorial boss = 16)");
                    Check(!s.HasMumBridge && !s.DemoSouthGateOpen, "Fresh progression locks preserved");
                    var store=typeof(WorldMacroPlaytestSession).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(s);
                    Check((string)store.GetType().GetField("path",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store)==state.savedPath&&s.LoadStatus=="new","Actual store opens fresh isolated namespace");
                    foreach(var id in Ids){var a=s.Actors.Single(x=>x.Id=="folklore298/"+id);var m=a.GetComponent<EnemyRigMotion298>();Check(s.Walker.Wiring.SummonTargets.Contains(a.GetComponent<EnemyVitals>())&&m.IsConfigured&&m.HasGraph,id+" actual CombatLoop and graph bound");}
                    var general=s.Actors.Single(a=>a.Id=="south_gate_general");var gm=general.GetComponent<EnemyRigMotion298>();
                    Check(gm.IsConfigured&&gm.HasGraph&&gm.Animator.isHuman,"General real Humanoid graph and three attack roles bound");
                    var spear=general.GetComponentsInChildren<Transform>(true).SingleOrDefault(t=>t.name=="Temporary_ReusedMesh_Polearm");Check(spear!=null&&spear.parent==gm.Animator.GetBoneTransform(HumanBodyBones.RightHand),"Existing polearm bound to new RightHand");
                    var dragon=s.Actors.Single(a=>a.Id=="cheongryong").GetComponent<CheongryongRigAnimation>();Check(dragon.IsConfigured&&dragon.HasGraph&&dragon.HasStateClips,"Dragon single graph binds head/tail and opt-in hit/stun/death");
                    foreach(var id in new[]{"bulgasari","fox_spirit"}){var f=s.Actors.Single(a=>a.Id=="folklore298/"+id).GetComponent<EnemyFootPlacement298>();Check(f!=null&&f.Configured&&f.Legs.Length==4&&f.Legs.All(l=>l.WeightedSurfaceVertices>=3&&l.CalibrationSource=="ACTUAL_BONE_BINDPOSE_WEIGHTED_WORLD_SURFACE"),id+" four actual weighted-surface foot chains");}
                    var audioActors=Ids.Select(id=>s.Actors.Single(a=>a.Id=="folklore298/"+id)).Concat(new[]{general,s.Actors.Single(a=>a.Id=="cheongryong")}).ToArray();
                    var audioClips=new List<AudioClip>();
                    foreach(var a in audioActors)
                    {
                        var audio=a.GetComponent<EnemyAudioEmitter298>();var profile=audio!=null?audio.Profile:null;
                        var cues=profile==null?Array.Empty<WorldMacroPlaytestAudioProfileSO.Cue>():new[]{profile.Alert,profile.Windup,profile.HitA,profile.HitB,profile.Death};
                        bool loaded=cues.Length==5&&cues.All(c=>EnemyAudioProfile298.Playable(c)&&c.Clip.loadState==AudioDataLoadState.Loaded);
                        Check(loaded&&audio.Status=="Ready",a.Id+" five actual loaded SFX slots and ready shared-pool emitter");
                        if(loaded)audioClips.AddRange(cues.Select(c=>c.Clip));
                    }
                    Check(audioClips.Count==40&&audioClips.Distinct().Count()==40,"Eight profiles bind forty distinct loaded SFX clips");
                    var readability=Object.FindObjectsByType<FolkloreReadability298>(FindObjectsSortMode.None).Where(r=>r.gameObject.scene==s.gameObject.scene&&r.GetComponentInParent<Oheangbu.App.Prologue.PrologueEncounter>(true)?.Id!=MineTutorialProfileSO.BossId).ToArray();   // #306 mine boss is a cloned humanoid, not one of the eight #298 bodies
                    bool shOk308=readability.Length>=8&&readability.Sum(r=>r.AppliedRenderers)==readability.Length&&readability.All(r=>r.Targets.Length==r.AppliedRenderers&&r.Targets.All(t=>t.lightProbeUsage==UnityEngine.Rendering.LightProbeUsage.CustomProvided&&t.sharedMaterials.All(m=>m!=null&&m.shader.name=="Universal Render Pipeline/Lit")));
                    Check(shOk308,"Every actual body renderer (the eight #298 bodies and the #306 additions, "+readability.Length+") uses opt-in SH while preserving URP Lit PBR"+(shOk308?"":" | #308 detail: components "+readability.Length+", applied "+readability.Sum(r=>r.AppliedRenderers)+", "+string.Join("; ",readability.Select(r=>r.GetComponentInParent<Oheangbu.App.Prologue.PrologueEncounter>(true)?.Id+" targets "+r.Targets.Length+"/"+r.AppliedRenderers+" "+string.Join(",",r.Targets.Select(t=>t.lightProbeUsage+":"+string.Join("+",t.sharedMaterials.Select(m=>m==null?"null":m.shader.name))))))));
                    Check(RenderSettings.ambientIntensity==state.ambientIntensity&&RenderSettings.reflectionIntensity==state.reflectionIntensity&&(int)RenderSettings.ambientMode==state.ambientMode&&RenderSettings.ambientSkyColor==state.ambientSky&&RenderSettings.ambientEquatorColor==state.ambientEquator&&RenderSettings.ambientGroundColor==state.ambientGround,"Actual Play global ambient/reflection settings match saved candidate baseline");
                    BeginActor(s); return;
                }
                if (state.phase == 8)
                {
                    if(Time.frameCount<5)return;
                    // #308 D308-2: a field boss (RespawnOnRest 0, agwi) stays defeated across rest and reload; the others come back
                    var respawning8 = Ids.Where(id => s.Content.Encounters.FirstOrDefault(e => e != null && e.Id == "folklore298/" + id)?.RespawnOnRest ?? true).ToArray();
                    Check(respawning8.All(id => s.Actors.Single(a => a.Id == "folklore298/" + id).GetComponent<EnemyVitals>().IsAlive), "Second actual Play restores living rested enemies (" + respawning8.Length + "/" + Ids.Length + " respawn on rest)");
                    var imugi=s.Actors.Single(a=>a.Id=="folklore298/imugi").GetComponent<EnemyRigMotion298>();
                    Check(imugi.SerpentFollow!=null&&imugi.SerpentFollow.enabled&&imugi.HasGraph&&imugi.CurrentPose!="Death","Rested imugi resumes actual head-only graph and body follow after second boot");
                    Check(!s.DemoSouthGateOpen && !s.HasMumBridge, "Reload creates no late unlock or gate completion");
                    Check(s.LoadStatus=="primary"&&s.Progress.ledger.checkpoint==state.expectedCheckpoint&&Ids.All(id=>s.Progress.defeated.Contains("folklore298/"+id)!=respawning8.Contains(id)),"Second Play loads actual rested durable snapshot rather than fresh fallback (field bosses stay defeated)");
                    state.finished=true;Stop(false); return;
                }
                if (state.phase == 7)
                {
                    s.CombatActive=false;s.Cull();var point = s.Content.Points.First(p => p.Id == "sanctuary_rest251"); bool rested = false;
                    foreach (float angle in new[] { 225f, 180f, 270f, 0f, 90f, 45f, 135f, 315f })
                    {
                        if (!s.TrySafeFeet(point.Position + Quaternion.Euler(0, angle, 0) * Vector3.forward * 1.1f, out var feet)) continue;
                        s.Teleport(feet, 0); Physics.SyncTransforms(); if (s.CanInteract(point.Id)) { rested = s.Interact(point.Id); break; }
                    }
                    Check(rested, "Actual sanctuary rest interaction accepted");
                    var respawning = Ids.Where(id => s.Content.Encounters.FirstOrDefault(e => e != null && e.Id == "folklore298/" + id)?.RespawnOnRest ?? true).ToArray();   // #308 D308-2: agwi is a field boss (RespawnOnRest 0)
                    Check(respawning.All(id => s.Actors.Single(a => a.Id == "folklore298/" + id).GetComponent<EnemyVitals>().IsAlive), "Rest respawns every defeated new enemy whose encounter respawns on rest (" + respawning.Length + "/" + Ids.Length + ")");
                    Check(s.SaveNow(out _), "Rested snapshot saved in isolated namespace"); state.expectedCheckpoint=s.Progress.ledger.checkpoint; state.phase = 8; Stop(true); return;
                }
                var life = current.GetComponent<EnemyVitals>(); var result = Result;
                SnapshotAudio();
                result.walkPose |= motion.CurrentPose == "Walk"; result.attackPose |= motion.CurrentPose == "Attack";
                if (motion.FootPlacement != null)
                {
                    var foot=motion.FootPlacement;result.footState=foot.LastStatus;result.footRays=foot.RayQueriesLastSample;
                    result.footMaxRays=Math.Max(result.footMaxRays,foot.RayQueriesLastSample);result.footAppliedSamples=Math.Max(result.footAppliedSamples,foot.AppliedSamples);
                    if(foot.Diagnostics.Any(d=>d.Supported))result.footSupportedFrames++;
                    if(foot.LastStatus=="SMALL_TERRAIN_CORRECTION_APPLIED"){result.footCorrectedFrames++;if(motion.CurrentPose=="Walk")result.footWalkingCorrectedFrames++;}
                }
                Observe(s,current,combat,motion,result);
                if (state.phase == -1)
                {
                    if (now-state.at>8) throw new TimeoutException(result.id+" neutral gravity settle did not finish");
                    if(result.settleFrames<3||Time.time-state.gameAt<.15f||!s.Walker.Motor.IsLocomotionGrounded||s.Walker.Motor.ActualLocalVelocity.sqrMagnitude>.01f)return;
                    result.playerStart=s.Walker.Body.transform.position;s.CombatActive=true;s.Cull();
                    keys=new[]{UnityEngine.InputSystem.Key.W};state.phase=1;state.at=now;state.gameAt=Time.time;Persist();return;
                }
                if (state.phase == 1)
                {
                    // Runtime LateUpdate releases W at1m; requiring many held-key frames
                    // makes low-FPS GameView runs walk through/past the encounter.
                    if(now-state.at>4){keys=Array.Empty<Key>();if(!result.moveReleased){result.moveReleased=true;result.moveReleaseReason="wall-time limit";}}
                    if(!result.moveReleased||result.moveFrames<2)return;
                    if(s.Walker.Motor.ActualLocalVelocity.sqrMagnitude>.01f&&Time.time-state.gameAt<2.5f&&now-state.at<6)return;
                    EndInput(); result.playerEnd = s.Walker.Body.transform.position;
                    Check(Vector3.ProjectOnPlane(result.playerEnd-result.playerStart,Vector3.up).magnitude > .5f, result.id + " virtual WASD moves actual motor");
                    Check(result.moveReleaseReason=="distance target",result.id+" controlled1m approach completes before safety timeout");
                    state.phase = 2; state.at = now; state.gameAt=Time.time; Persist(); return;
                }
                if (state.phase == 2)
                {
                    result.enemyEnd = current.transform.position; result.rootDistance = Vector3.Distance(current.Player.position, current.transform.position); result.playerRootDeltaY = current.Player.position.y - current.transform.position.y;
                    if (result.damage <= 0 && Time.time - state.gameAt < 24) return;
                    Check(result.alerts > 0 && result.telegraphs > 0 && result.impacts > 0 && result.contacts > 0 && result.damage > 0, result.id + " natural detection, chase and attack deals actual damage");
                    Check(result.walkedDistance>=1,result.id+" natural actor navigation travelled at least1m");
                    Check(result.walkPose && result.attackPose && motion.HasGraph, result.id + " live walk/attack graph observed");
                    Check(result.attackCaptured,result.id+" actual anticipation frame captured");
                    s.CombatActive=false;s.Cull();current.GetComponent<NavMeshAgent>().isStopped=true;combat.AttackEnabled=false;combat.StopAttack();
                    state.revision = life.LifeRevision; life.TakeDamage(1); SnapshotAudio(); state.phase = 3; state.at = now; state.gameAt=Time.time; return;
                }
                if (state.phase == 3 && Time.time - state.gameAt > .18f)
                {
                    result.hitPose = motion.CurrentPose == "Hit"; Check(result.hitPose, result.id + " real damage selects hit clip");
                    if(motion.SerpentFollow!=null)Check(!motion.SerpentFollow.enabled,result.id+" body follow disabled during actual hit clip");
                    life.OpenWeakPoint(); state.phase = 4; state.at = now; state.gameAt=Time.time; return;
                }
                if (state.phase == 4 && Time.time - state.gameAt > .18f)
                {
                    result.stunPose = motion.CurrentPose == "Stun"; Check(result.stunPose, result.id + " actual weak point selects stun clip");
                    if(motion.SerpentFollow!=null)Check(!motion.SerpentFollow.enabled,result.id+" body follow disabled during actual stun clip");
                    s.CombatActive=true;s.Cull();current.enabled = true; life.TakeDamage(float.MaxValue, AttackProvenance.Create(s.Walker.Body.gameObject, DamageSource.PlayerDirect, Element.Metal)); SnapshotAudio(); state.phase = 5; state.at = now; state.gameAt=Time.time; return;
                }
                if (state.phase == 5 && Time.time - state.gameAt > .5f)
                {
                    result.deathPose = motion.CurrentPose == "Death"; Check(!life.IsAlive && result.deathPose && motion.HasGraph, result.id + " lethal API selects death clip"); Capture(s, current, "death");
                    if(motion.SerpentFollow!=null)Check(!motion.SerpentFollow.enabled,result.id+" body follow disabled during actual death clip");
                    state.phase = 6; state.at = now; state.gameAt=Time.time; return;
                }
                if (state.phase == 6 && Time.time - state.gameAt > Math.Max(.2, motion.Death.length))
                {
                    SnapshotAudio();
                    Check(result.audioPlayed>=4&&result.audioFallback==0&&new[]{"Alert","Windup","Hit","Death"}.All(role=>result.audioRoles.Contains(role)),result.id+" actual alert/windup/hit/death events accepted original SFX; played="+result.audioPlayed+" fallback="+result.audioFallback+" dropped="+result.audioDropped+" status="+result.audioStatus);
                    Check(current.GetComponentsInChildren<Collider>(true).All(c=>!c.enabled),result.id+" actual death disables collision");
                    Check(s.SaveNow(out var defeatError)&&s.Progress.defeated.Contains(result.id),result.id+" actual defeated ID persisted: "+defeatError);
                    current.enabled=true;current.ResetEncounter(); result.restored = life.IsAlive && life.LifeRevision != state.revision;
                    Check(result.restored, result.id + " encounter reset restores a new life");
                    // Leave a genuine defeated actor for the following shared rest test.
                    life.TakeDamage(float.MaxValue, AttackProvenance.Create(s.Walker.Body.gameObject, DamageSource.PlayerDirect, Element.Metal));
                    Unbind(); state.index++; if (state.index < Ids.Length) BeginActor(s); else { state.phase = 7; state.at = now; Persist(); } return;
                }
            }
            catch (Exception e) { Check(false, e.ToString()); Stop(false); }
        }
        static void Observe(WorldMacroPlaytestSession s,PrologueEncounter actor,EnemyController attack,EnemyRigMotion298 rig,ActorResult result)
        {
            result.sampledFrames++;result.walkedDistance=Mathf.Max(result.walkedDistance,Vector3.Distance(result.home,actor.transform.position));
            if(EditorApplication.timeSinceStartup-state.lastSample<.08||result.samples.Count>=900)return;state.lastSample=EditorApplication.timeSinceStartup;
            var bones=rig.Animator.GetComponentsInChildren<Transform>();var hand=bones.FirstOrDefault(b=>b.name=="RightHand"||b.name=="Fore_R_Foot");var head=bones.FirstOrDefault(b=>b.name=="Head");var nav=actor.GetComponent<NavMeshAgent>();
            result.samples.Add(new FrameSample{frame=Time.frameCount,pose=rig.CurrentPose,poseTime=rig.PoseTime,behaviour=actor.Current.ToString(),hp=rig.Vitals.Hp,playerHp=s.Walker.Body.GetComponent<PlayerVitals>().Hp01*s.Walker.Body.GetComponent<PlayerVitals>().MaxHp,
                root=actor.transform.position,player=actor.Player.position,gameTime=Time.time,deltaTime=Time.deltaTime,navStopped=nav.isStopped,navStoppingDistance=nav.stoppingDistance,navVelocity=nav.velocity,navDesiredVelocity=nav.desiredVelocity,
                hand=hand!=null?actor.transform.InverseTransformPoint(hand.position):Vector3.zero,head=head!=null?actor.transform.InverseTransformPoint(head.position):Vector3.zero,
                sight=attack.HasLineOfSight(),blocker=SightBlocker308(actor),range=attack.AttackRange,distance=Vector3.Distance(actor.transform.position,actor.Player.position),telegraph=attack.IsTelegraphing,recovery=attack.IsRecovering,navRemaining=nav.isOnNavMesh&&float.IsFinite(nav.remainingDistance)?nav.remainingDistance:-1});
        }
        // #308 diagnostic: what stands on EnemyController's sight ray (root + .4 m to player + .4 m)? Same query, names only.
        static string SightBlocker308(PrologueEncounter actor)
        {
            Vector3 a=actor.transform.position+Vector3.up*.4f,b=actor.Player.position+Vector3.up*.4f;
            var hits=actor.gameObject.scene.GetPhysicsScene().Raycast(a,(b-a).normalized,out var first,(b-a).magnitude)?Physics.RaycastAll(a,(b-a).normalized,(b-a).magnitude):Array.Empty<RaycastHit>();
            var names=hits.Where(h=>!h.transform.IsChildOf(actor.transform)&&!h.transform.IsChildOf(actor.Player)).OrderBy(h=>h.distance)
                .Select(h=>{var t=h.transform;string path=t.name;for(int i=0;i<4&&t.parent!=null;i++){t=t.parent;path=t.name+"/"+path;}return path+(h.collider.isTrigger?" [trigger]":"")+" "+h.collider.GetType().Name+" L"+h.collider.gameObject.layer+" @"+h.distance.ToString("F2");}).Take(3).ToArray();
            return string.Join(" ; ",names);
        }
        static void Capture(WorldMacroPlaytestSession s, PrologueEncounter actor, string phase)
        {
            var source = s.Walker.ViewCamera; var cameraObject = new GameObject("Folklore298 proof camera");
            cameraObject.hideFlags=HideFlags.HideAndDontSave;SceneManager.MoveGameObjectToScene(cameraObject,s.gameObject.scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;camera.ResetWorldToCameraMatrix();camera.ResetProjectionMatrix();camera.ResetCullingMatrix();camera.aspect = 16f / 9;
            var sourceSky=source.GetComponent<Skybox>();if(sourceSky!=null){var sky=cameraObject.AddComponent<Skybox>();sky.material=sourceSky.material;sky.enabled=sourceSky.enabled;}
            var data=source.GetComponents<Component>().FirstOrDefault(c=>c!=null&&c.GetType().FullName=="UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            if(data!=null){var copy=cameraObject.AddComponent(data.GetType());EditorUtility.CopySerialized(data,copy);if(data.GetType().GetProperty("cameraStack")?.GetValue(copy)is System.Collections.IList stack)stack.Clear();}
            var target = actor.transform.position + Vector3.up * .3f;
            var eye = target + actor.transform.forward * 5 + actor.transform.right * 3 + Vector3.up * 1.2f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            var arts = Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None); var observers = arts.Select(a => a.Observer).ToArray();
            var dress=Object.FindObjectsByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>(FindObjectsSortMode.None);var dressObservers=dress.Select(d=>d.Observer).ToArray();
            var rt = new RenderTexture(1280, 720, 24); var old = RenderTexture.active; Texture2D texture = null; bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                foreach (var art in arts) art.Observer = camera; foreach(var d in dress)d.Observer=camera; ShaderUtil.allowAsyncCompilation = false; rt.Create(); camera.targetTexture = rt; camera.Render(); camera.Render();
                RenderTexture.active = rt; texture = new Texture2D(1280, 720, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                string file=Path.Combine(Folder,actor.Id.Replace('/','_')+"-"+phase+".png");File.WriteAllBytes(file,texture.EncodeToPNG());
                File.WriteAllText(file+".json",JsonUtility.ToJson(new CaptureFrame{utc=DateTime.UtcNow.ToString("O"),id=actor.Id,phase=phase,frame=Time.frameCount,gameTime=Time.time,telegraphProgress=combat!=null?combat.TelegraphProgress:0,actualTelegraph=combat!=null&&combat.IsTelegraphing,eye=eye,target=target,pose=motion.CurrentPose,poseTime=motion.PoseTime,physicalBounds=CompactFolklore298.PhysicalBounds(actor.transform.Find("Folklore298_Visual").gameObject),artInstances=arts.Sum(a=>a.VisibleInstances)},true));
            }
            finally { for (int i = 0; i < arts.Length; i++) arts[i].Observer = observers[i];for(int i=0;i<dress.Length;i++)dress[i].Observer=dressObservers[i]; ShaderUtil.allowAsyncCompilation = async; RenderTexture.active = old; camera.targetTexture = null; Object.DestroyImmediate(cameraObject); rt.Release(); Object.DestroyImmediate(rt); if (texture != null) Object.DestroyImmediate(texture); }
        }
        [Serializable] sealed class CaptureFrame { public string utc,id,phase,pose;public int frame,artInstances;public float poseTime,gameTime,telegraphProgress;public bool actualTelegraph;public Vector3 eye,target;public Bounds physicalBounds; }
        // Runs after the real motor/animation updates. EditorApplication.update has
        // a different Input System state buffer and cannot prove the consumed Move value.
        internal static void RuntimeLateSample()
        {
            if(state?.active!=true||state.status!="RUNNING"||current==null||!EditorApplication.isPlaying)return;
            var s=Session;if(s==null)return;var result=Result;
            if(state.phase==-1){result.settleFrames++;return;}
            if(state.phase==1)
            {
                var input=ReadInput(s,"runtime-late-wasd");result.inputSamples.Add(input);result.moveFrames++;
                if(input.move.sqrMagnitude>.01f)result.inputNonzeroFrames++;
                if(!input.motorEnabled||!input.controllerEnabled||input.inputBlocked||input.environmentBlocked||!input.keyboardEnabled||!input.moveEnabled)result.inputDisabledFrames++;
                result.inputDistance=Vector3.ProjectOnPlane(s.Walker.Body.transform.position-result.playerStart,Vector3.up).magnitude;
                result.inputSeconds=Time.time-state.gameAt;
                if(!result.moveReleased&&(result.inputDistance>=1f||result.inputSeconds>=1.5f))
                {
                    keys=Array.Empty<Key>();result.moveReleased=true;result.moveReleaseReason=result.inputDistance>=1f?"distance target":"game-time limit";
                }
            }
            if(state.phase>=1&&state.phase<=2&&!result.attackCaptured&&combat.IsTelegraphing&&motion.CurrentPose=="Attack"&&motion.PoseTime>0)
            {
                try{Capture(s,current,"attack");result.attackCaptured=true;}
                catch(Exception e){Check(false,"Actual anticipation capture: "+e);state.fatalLog=true;Persist();}
            }
        }
        static bool GameViewFocused => EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().FullName == "UnityEditor.GameView";
        static void FocusGameView()
        {
            var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if(type==null)throw new Exception("Unity GameView type unavailable");
            EditorWindow.GetWindow(type).Focus();
        }
        static void RestoreEditorFocus()
        {
            if(state==null||state.active||EditorApplication.isPlayingOrWillChangePlaymode)return;
            var previous=Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w=>w.GetInstanceID()==state.priorFocusedWindowId&&w.GetType().FullName==state.priorFocusedWindowType);
            if(previous!=null)previous.Focus();
        }
        static InputSample ReadInput(WorldMacroPlaytestSession s,string stage)
        {
            var motor=s.Walker.Motor;var move=actions!=null?actions.FindActionMap("Gameplay")?.FindAction("Move"):null;
            var audio=current!=null?current.GetComponent<EnemyAudioEmitter298>():null;
            return new InputSample {frame=Time.frameCount,gameTime=Time.time,stage=stage,inputUpdate=InputState.currentUpdateType.ToString(),applicationFocused=Application.isFocused,gameViewFocused=GameViewFocused,
                focusedWindow=EditorWindow.focusedWindow!=null?EditorWindow.focusedWindow.GetType().FullName:"none",motorEnabled=motor.isActiveAndEnabled,
                controllerEnabled=s.Walker.Body.enabled,inputBlocked=motor.RuntimeState!=null&&motor.RuntimeState.InputBlocked,environmentBlocked=motor.EnvironmentalInputBlocked,
                keyboardEnabled=keyboard!=null&&keyboard.enabled,keyboardCurrent=keyboard!=null&&Keyboard.current==keyboard,moveEnabled=move!=null&&move.enabled,
                move=move!=null?move.ReadValue<Vector2>():Vector2.zero,player=s.Walker.Body.transform.position,motorVelocity=motor.ActualLocalVelocity,
                audioReadiness=audio!=null&&audio.Soundscape!=null?audio.Soundscape.EnemyAudioState298:"no-current-emitter"};
        }
        static bool ObserveFocus(WorldMacroPlaytestSession s,double now)
        {
            bool focused=Application.isFocused&&GameViewFocused;
            if(!state.focusReady)
            {
                if(focused){state.focusReady=true;state.focusEvents.Add(ReadInput(s,"play-focus-ready"));Persist();return true;}
                if(now-state.at<10)return false;
                state.interrupted=true;state.interruptReason="Actual GameView focus was not acquired within10 seconds";
                state.focusEvents.Add(ReadInput(s,"startup-focus-timeout"));Check(false,state.interruptReason);Stop(false);return false;
            }
            if(!focused)
            {
                if(state.focusLossAt<0)
                {
                    state.focusLossAt=now;state.interrupted=true;state.interruptReason="Actual GameView/application focus was lost during Play";
                    state.focusEvents.Add(ReadInput(s,"focus-lost"));Check(false,state.interruptReason);Persist();
                }
                // Do not force focus back every frame or bypass production input/audio policy.
                if(now-state.focusLossAt>=2)Stop(false);
                return false;
            }
            if(state.focusLossAt>=0)
            {
                state.focusEvents.Add(ReadInput(s,"focus-returned-run-invalid"));Persist();Stop(false);return false;
            }
            return true;
        }
        static void BeginInput(WorldMacroPlaytestSession s)
        {
            priorKeyboard = Keyboard.current; priorMouse = Mouse.current; keyboard = InputSystem.AddDevice<Keyboard>("Folklore298Walk"); mouse = InputSystem.AddDevice<Mouse>("Folklore298Look"); keyboard.MakeCurrent(); mouse.MakeCurrent();
            actions = new SerializedObject(s.Walker.Motor).FindProperty("_actions").objectReferenceValue as InputActionAsset;
            if (actions != null) { priorDevices = actions.devices?.ToArray(); actions.devices = new InputDevice[] { keyboard, mouse }; }
            InputSystem.onBeforeUpdate += Feed;
        }
        static void Feed() { if (keyboard != null && InputState.currentUpdateType != InputUpdateType.BeforeRender) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.QueueStateEvent(mouse, new MouseState()); } }
        static void EndInput()
        {
            InputSystem.onBeforeUpdate -= Feed; keys = Array.Empty<Key>(); if (actions != null) actions.devices = priorDevices;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (mouse != null) InputSystem.RemoveDevice(mouse); keyboard = null; mouse = null; actions = null; priorDevices = null;
            if (priorKeyboard != null && priorKeyboard.added) priorKeyboard.MakeCurrent(); if (priorMouse != null && priorMouse.added) priorMouse.MakeCurrent();
        }
        static string Hash(string path) { using (var stream=File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
    }
    [DefaultExecutionOrder(32000)]
    public sealed class FolkloreRuntimeProbe298 : MonoBehaviour
    {
        void LateUpdate() => FolkloreRuntimeChecks298.RuntimeLateSample();
    }
}
