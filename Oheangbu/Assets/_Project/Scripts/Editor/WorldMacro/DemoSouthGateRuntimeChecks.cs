using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Runs the real authored actor/session/gate/UI in Play. Prerequisites are explicitly a UUID-only fixture.
    [InitializeOnLoad]
    public static class DemoSouthGateRuntimeChecks
    {
        const string Key="DemoSouthGateRuntimeChecks.Run", SuffixKey="PlaytestUiReviewSuffix", Missing="__southgate_missing__";
        static string DiagnosticScene=>SceneManager.GetActiveScene().path==WorldMacroCompactAuthoring.TargetScene?WorldMacroCompactAuthoring.TargetScene:DemoFoundationAuthoring.Scene;
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,DiagnosticScene==WorldMacroCompactAuthoring.TargetScene?"../../Art/Demo/ActsTerrain/DemoSouthGateRuntimeChecks.json":"../../Art/Demo/SouthGate/runtime_tests.json"));
        [Serializable] sealed class Protected { public string path,hash; }
        [Serializable] sealed class Result { public string category,name,status,detail; }
        [Serializable] sealed class GroundProbe
        { public bool hit;public string collider;public Vector3 sample,ground,normal;public float sampleMinusGround; }
        [Serializable] sealed class FootRegion
        { public string side;public int influencedVertices;public Vector3 minimum,maximum,sole;public GroundProbe support; }
        [Serializable] sealed class SkinProbe
        { public string renderer,mesh,error,weightStatus;public bool active,enabled;public int vertices;public Bounds rendererBounds;public Vector3 bakedMinimum,bakedMaximum,lowestVertex;public float conservativeBoundsBelowBaked;public FootRegion[] feet;public GroundProbe lowestSupport; }
        [Serializable] sealed class StaticProbe
        { public string renderer,mesh,status;public bool weapon,active,enabled;public Bounds rendererBounds;public Vector3 minimumVertex;public GroundProbe support; }
        [Serializable] sealed class BoneProbe {public string name;public Vector3 position;public GroundProbe support;}
        [Serializable] sealed class VisualProbe
        {
            public string label,scope="Read-only current deformed pose. BakeMesh(false) excludes object scale, then renderer.TransformPoint applies world scale once. No Animator.Evaluate, pose assignment, material, mesh, import or transform edit. Bone weights use the existing C02 maximum-four-influences source; unavailable CPU weights are reported, never inferred.",state,attack,interpretation;
            public int frame;public float scaledTime,planSampleTime,actorFeetY,actualLowestSkinY,boundsLowestY;public Vector3 navVelocity;
            public bool graph;public SkinProbe[] skins;public StaticProbe[] staticMeshes;public BoneProbe[] footBones;
        }
        [Serializable] sealed class Run
        {
            public string status,phase,suffix,savePath,priorSuffix,contentPath,campaignPath,error,contentHash,campaignHash,visualAudit;
            public bool active,stopping,cleaned,priorBackground,priorCombat,installed,holdEnding,holding,sampledIdle,sampledLocomotion;
            public int step=-1,contentId,campaignId,beforeCurrency,reward,openingEvents,deaths,interruptions,attacks,contacts;
            public float killTime,openTime,scaledStart; public double deadline,phaseDeadline,nextFocus;
            public List<Protected> files=new List<Protected>(); public List<Result> checks=new List<Result>();
            public List<VisualProbe> visualProbes=new List<VisualProbe>();
            public string scope="Actual authored SouthGate actor, native MonoBehaviour Update, EnemyVitals provenance damage, Session death transaction, 2.4s physical door animation and terminal UI/input gate. Earlier campaign stages and delivered escort are PREPARED FIXTURES in a fresh _southgate_UUID save; they are not escort/full-campaign completion evidence. Safe teleports and damage APIs are diagnostic setup, not keyboard combat.";
            public string[] unverified={"Native input/full campaign and escort playthrough", "Dedicated general costume and two-hand polearm animations", "Human visual approval, model ground/animation quality", "Actual app restart and save failure injection", "CPU/GPU performance, audiovisual clipping and 1080p screenshots"};
        }
        static Run run;
        static WorldMacroPlaytestSession session;
        static PlaytestUiRoot ui;
        static PrologueEncounter actor;
        static SouthGateGeneralController general;
        static SouthGateDoorPresentation door;
        static SouthGateGeneralPresentation presentation;
        static WorldMacroPlaytestSO original,clone;
        static DemoCampaignProfile campaign;
        static SouthGateGeneralAttackPlan interruptedPlan;
        static bool subscribed;
        static bool InputReady=>ui!=null&&!ui.Busy&&!ui.Pause.IsPaused&&!session.GameplayInputBlocked;

        static DemoSouthGateRuntimeChecks()
        {
            string saved=SessionState.GetString(Key,"");if(saved.Length>0)run=JsonUtility.FromJson<Run>(saved);
            EditorApplication.playModeStateChanged+=PlayChanged;EditorApplication.quitting+=Quitting;
            if(run!=null&&(run.active||run.stopping))EditorApplication.update+=Tick;
        }
        public static string Execute(string command)
        {
            if((command=="begin"||command=="begin-hold")&&Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%");
            if(command=="visual-probe")
            {
                if(!EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=DiagnosticScene)throw new InvalidOperationException("Actual demo Play pose required");
                var target=Object.FindFirstObjectByType<WorldMacroPlaytestSession>()?.Actors.SingleOrDefault(a=>a!=null&&a.Id==WorldMacroPlaytestSession.SouthGateGeneralId);
                if(target==null)throw new InvalidOperationException("Actual registered general missing");
                var report=SampleVisual(target,"manual_current_pose");string json=JsonUtility.ToJson(report,true);Directory.CreateDirectory(Path.GetDirectoryName(Output));
                string probePath=Path.Combine(Path.GetDirectoryName(Output),"visual_pose_probe_frame_"+Time.frameCount+".json");File.WriteAllText(probePath,json);return json;
            }
            if(command=="poll")return run!=null?JsonUtility.ToJson(run,true):File.Exists(Output)?File.ReadAllText(Output):"NOT_RUN";
            if(command=="stop"){if(run==null||run.cleaned)return "NOT_RUNNING";Finish(run.holding?"PASS_BOUNDED_API_INTEGRATION":"ABORTED",run.holding?null:"Explicit stop");return "STOPPING";}
            if(command=="release"){if(run==null||!run.holding)throw new InvalidOperationException("Not holding ending");Finish("PASS_BOUNDED_API_INTEGRATION",null);return "STOPPING";}
            if(command!="begin"&&command!="begin-hold")throw new ArgumentException("Use begin, begin-hold, poll, release, stop or visual-probe.");
            if(run!=null&&(run.active||run.stopping)||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
                throw new InvalidOperationException("Compiled idle Edit mode required.");
            var authored=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(SceneManager.GetActiveScene().path!=DiagnosticScene||authored==null||
                AssetDatabase.GetAssetPath(authored.Content)!=(DiagnosticScene==WorldMacroCompactAuthoring.TargetScene?WorldMacroCompactAuthoring.Folder+"/Data/03_Content.asset":DemoFoundationAuthoring.Folder+"/Content.asset"))throw new InvalidOperationException("Dedicated authored demo scene/content required.");
            var bosses=authored.Actors.Where(a=>a!=null&&a.Id==WorldMacroPlaytestSession.SouthGateGeneralId).ToArray();
            if(bosses.Length!=1||bosses[0].GetComponent<SouthGateGeneralController>()==null||
                Object.FindObjectsByType<SouthGateDoorPresentation>(FindObjectsSortMode.None).Count(d=>d.gameObject.scene==authored.gameObject.scene&&d.IsConfigured)!=1)
                throw new InvalidOperationException("Apply/audit one real general and configured gate before running.");
            var checkpoint=authored.Content.Checkpoints.Single(c=>c.Id==DemoSouthGateSceneAuthoring.RestId);
            string suffix="_southgate_"+Guid.NewGuid().ToString("N"),path=Path.Combine(Application.persistentDataPath,authored.Content.SaveSlot+suffix+".json");
            if(File.Exists(path)||File.Exists(path+".bak")||File.Exists(path+".tmp"))throw new IOException("UUID already exists");
            run=new Run { active=true,status="RUNNING",phase="prepare explicitly isolated prerequisite fixture",suffix=suffix,savePath=path,
                priorSuffix=SessionState.GetString(SuffixKey,Missing),priorBackground=Application.runInBackground,
                contentPath=AssetDatabase.GetAssetPath(authored.Content),campaignPath=AssetDatabase.GetAssetPath(authored.Content.Campaign),
                contentHash=HashText(JsonUtility.ToJson(authored.Content)),campaignHash=HashText(JsonUtility.ToJson(authored.Content.Campaign)),
                holdEnding=command=="begin-hold",deadline=EditorApplication.timeSinceStartup+160 };
            foreach(string file in Saves().Concat(new[]{run.contentPath,run.contentPath+".meta",run.campaignPath,run.campaignPath+".meta",DiagnosticScene,DiagnosticScene+".meta"}).Distinct())
                if(File.Exists(file))run.files.Add(new Protected{path=file,hash=Hash(file)});
            Persist();
            try
            {
                var fixture=WorldMacroProgress.CreateNew(authored.Content.TerrainRevision,checkpoint.Feet,checkpoint.Yaw);
                fixture.ledger.hasPosition=true;fixture.ledger.checkpoint=checkpoint.Id;fixture.ledger.hp=1;fixture.ledger.ink=1;fixture.ledger.currency=41;
                fixture.campaign.CampaignId=authored.Content.Campaign.CampaignId;
                var stages=authored.Content.Campaign.Stages;int index=Array.FindIndex(stages,s=>s.Id=="south_gate");
                Check(index>0&&index==stages.Length-2&&stages[index+1].Id=="ending","ordered final two stages","Prerequisite stage IDs come from actual profile.","FIXTURE");
                fixture.campaign.Completed=stages.Take(index).Select(s=>s.Id).ToList();fixture.ledger.completed.Add("cargo_delivery");
                foreach(string id in stages.Take(index+1).SelectMany(s=>s.RequiredDefeatedIds??Array.Empty<string>()).Distinct())
                    if(id!=WorldMacroPlaytestSession.SouthGateGeneralId)fixture.defeated.Add(id);
                fixture.escort=new DemoEscortState{Stage=DemoEscortStage.Delivered,CompanionMode=DemoEscortCompanionMode.Waiting,
                    DeliveryRewardRecorded=true,CheckpointId=checkpoint.Id,CheckpointFeet=checkpoint.Feet,CompanionFeet=checkpoint.Feet,
                    CheckpointYaw=checkpoint.Yaw,Revision=8,LastEvidenceId="southgate-delivery-FIXTURE"};
                Check(WorldMacroProgress.Valid(fixture),"valid prerequisite fixture","No live progress injected; initial UUID file explicitly represents untested prerequisites.","FIXTURE");
                new AtomicJsonStore<WorldMacroProgress>(path,WorldMacroProgress.Valid).Save(fixture);
                SessionState.SetString(SuffixKey,suffix);Application.runInBackground=true;
                session=null;ui=null;general=null;door=null;actor=null;subscribed=false;Persist();
                EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
            }
            catch(Exception e){Finish("FINDINGS",e.ToString());}
            return "RUNNING isolated prerequisite FIXTURE; actual final actor/death/save/gate/UI; no full journey evidence.";
        }
        static IEnumerable<string> Saves()=>Directory.Exists(Application.persistentDataPath)?Directory.GetFiles(Application.persistentDataPath,"*.json*",SearchOption.TopDirectoryOnly)
            .Where(p=>run==null||p!=run.savePath&&p!=run.savePath+".bak"&&p!=run.savePath+".tmp"):Array.Empty<string>();
        static string Hash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
        static string HashText(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-","");}
        static void Persist(){if(run==null)return;string json=JsonUtility.ToJson(run,true);SessionState.SetString(Key,json);Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllText(Output,json);}
        static void Check(bool pass,string name,string detail,string category="ACTUAL")
        {run.checks.Add(new Result{category=category,name=name,status=pass?"PASS":"FAIL",detail=detail});if(!pass)throw new InvalidOperationException(name+": "+detail);}
        static void Next(int step,string phase,double seconds=15)
        {run.step=step;run.phase=phase;run.phaseDeadline=EditorApplication.timeSinceStartup+seconds;Persist();}
        static void Isolation()
        {
            if(session.TestSaveSuffix!=run.suffix||SessionState.GetString(SuffixKey,Missing)!=run.suffix||
                Path.Combine(Application.persistentDataPath,session.Content.SaveSlot+session.TestSaveSuffix+".json")!=run.savePath)
                throw new InvalidOperationException("UUID isolation lost; stop before any write/action.");
        }
        static void Protect()
        {
            foreach(var file in run.files)Check(File.Exists(file.path)&&Hash(file.path)==file.hash,"protected file unchanged",file.path,"ISOLATION");
            var set=new HashSet<string>(run.files.Select(f=>f.path),StringComparer.OrdinalIgnoreCase);
            Check(Saves().All(set.Contains),"no non-test save created","Only the exact UUID primary/bak/tmp may change.","ISOLATION");
            var source=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(run.contentPath);
            Check(source!=null&&HashText(JsonUtility.ToJson(source))==run.contentHash&&HashText(JsonUtility.ToJson(source.Campaign))==run.campaignHash,
                "source assets unchanged in memory","Only DontSave runtime clones enabled final stages.","ISOLATION");
        }
        static void Bind()
        {
            session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();ui=PlaytestUiRoot.Instance;if(session==null)return;
            actor=session.Actors.SingleOrDefault(a=>a!=null&&a.Id==WorldMacroPlaytestSession.SouthGateGeneralId);
            general=actor!=null?actor.GetComponent<SouthGateGeneralController>():null;
            presentation=actor!=null?actor.GetComponent<SouthGateGeneralPresentation>():null;
            door=Object.FindObjectsByType<SouthGateDoorPresentation>(FindObjectsSortMode.None).SingleOrDefault(d=>d.gameObject.scene==session.gameObject.scene&&d.IsConfigured);
            if(subscribed||general==null)return;
            general.AttackStarted+=Attack;general.AttackImpactResolved+=Impact;general.EmpowerInterrupted+=Interrupted;
            general.Vitals.Died+=Died;session.DemoSouthGateOpened+=Opened;subscribed=true;
        }
        static void Attack(SouthGateGeneralAttackPlan p){run.attacks++;run.phase="actual attack "+p.Kind;Persist();}
        static void Impact(SouthGateGeneralImpact p){if(p.Contact)run.contacts++;}
        static void Interrupted(SouthGateGeneralAttackPlan p){run.interruptions++;interruptedPlan=p;}
        static void Died(){run.deaths++;}
        static void Opened(){run.openingEvents++;run.openTime=Time.time;}
        static void Fixture()
        {
            original=session.Content;run.priorCombat=session.CombatActive;
            clone=Object.Instantiate(original);clone.hideFlags=HideFlags.DontSave;clone.name="SouthGateOnly_FIXTURE_"+run.suffix;
            campaign=Object.Instantiate(original.Campaign);campaign.hideFlags=HideFlags.DontSave;campaign.name="SouthGateFinalStages_FIXTURE_"+run.suffix;
            foreach(var stage in campaign.Stages.Where(s=>s.Id=="south_gate"||s.Id=="ending"))stage.Implemented=true;
            clone.Campaign=campaign;clone.Opening=null;session.Content=clone;session.CombatActive=true;
            run.contentId=clone.GetInstanceID();run.campaignId=campaign.GetInstanceID();run.installed=true;
            Check(session.DemoSouthGateEncounterAvailable&&!session.DemoSouthGateOpen,"actual backend available only with explicit fixture","No GateOpened or BossDefeated completion was inserted.","FIXTURE");
            Check(general.Vitals.MaxHp==480&&general.Vitals.IsAlive&&presentation!=null&&presentation.IsConfigured&&door!=null&&!door.OpenAnimationComplete,
                "authored life, configured model and closed gate","Actual 480 HP actor, configured humanoid presentation and one existing gate.");
            run.beforeCurrency=session.Progress.ledger.currency;
            run.reward=campaign.Stages.Where(s=>s.Id=="south_gate"||s.Id=="ending").Sum(s=>s.TongboReward);
            session.Cull();Next(0,"wait real input focus then safe combat approach",25);
        }
        static void Focus()
        {
            if(InputReady||run.step>=4||EditorApplication.timeSinceStartup<run.nextFocus)return;
            if(Application.isFocused&&EditorWindow.focusedWindow?.GetType().FullName=="UnityEditor.GameView")return;
            run.nextFocus=EditorApplication.timeSinceStartup+1;
            var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");if(type!=null){var view=EditorWindow.GetWindow(type,false,"Game",true);view.Focus();view.Repaint();}
        }
        static void Approach()
        {
            foreach(float radius in new[]{2.5f,2.9f,3.2f})for(int i=0;i<16;i++)
            {
                float angle=i*Mathf.PI/8;Vector3 p=actor.transform.position+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*radius;
                try{p=WorldMacroPlaytestAuthoring.Ground(p,true)+Vector3.up*.06f;}catch(Exception){continue;}
                if(!session.TrySafeFeet(p,out var feet))continue;
                var target=actor.transform.position-feet;target.y=0;session.Teleport(feet,Quaternion.LookRotation(target).eulerAngles.y);Physics.SyncTransforms();session.Cull();
                Check(true,"safe close approach via actual Session API",feet+"; no player path/input proof.");return;
            }
            throw new InvalidOperationException("No safe close capsule position around actual general.");
        }
        static void ObserveVisual()
        {
            var renderers=actor.GetComponentsInChildren<Renderer>(true);
            var skins=actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var collider=actor.GetComponent<CapsuleCollider>();
            var bounds=renderers.Length>0?renderers[0].bounds:new Bounds(actor.transform.position,Vector3.zero);
            foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
            float footY=actor.transform.position.y-.875f;
            run.visualAudit=$"Actual actor={actor.name}; root capsule bounds={collider?.bounds}; rendererCount={renderers.Length}; distinctInstanceIds={renderers.Select(r=>r.GetInstanceID()).Distinct().Count()}; skins={skins.Length}; allVisualBounds={bounds}; groundFootY={footY:F3}; visualMinMinusFoot={bounds.min.y-footY:F3}; graph={presentation.HasGraph}. Includes polearm/helmet/skinned renderer bounds, so this is diagnostic and not visual grounding approval.";
            run.checks.Add(new Result{category="VISUAL_AUDIT",name="temporary actor/renderer/ground summary",status="UNVERIFIED",detail=run.visualAudit});
            run.visualProbes.Add(SampleVisual(actor,"natural_combo_earth_preparation"));
        }
        static string PathOf(Transform t){string path=t.name;while(t.parent!=null){t=t.parent;path=t.name+"/"+path;}return path;}
        static GroundProbe SupportAt(Vector3 sample,Transform owner)
        {
            var evidence=new GroundProbe{sample=sample};
            // A sole may already be below terrain. Start above it and report the signed difference honestly.
            foreach(var hit in Physics.RaycastAll(sample+Vector3.up*1.5f,Vector3.down,5,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
            {
                if(hit.transform.IsChildOf(owner)||hit.transform.GetComponentInParent<PrologueEncounter>()!=null||
                    hit.transform.GetComponentInParent<PlayerMotor>()!=null||hit.normal.y<=.35f||hit.collider.attachedRigidbody!=null)continue;
                evidence.hit=true;evidence.collider=PathOf(hit.transform);evidence.ground=hit.point;evidence.normal=hit.normal;evidence.sampleMinusGround=sample.y-hit.point.y;break;
            }
            return evidence;
        }
        static bool FootBone(Transform bone,bool left)
        {
            string foot=left?"LeftFoot":"RightFoot",toe=left?"LeftToeBase":"RightToeBase";
            for(var t=bone;t!=null;t=t.parent)if(t.name==foot||t.name==toe)return true;
            return false;
        }
        static float FootInfluence(BoneWeight weight,bool[] footBones)
        {
            float total=0;
            void Add(int index,float amount){if(amount>0&&index>=0&&index<footBones.Length&&footBones[index])total+=amount;}
            Add(weight.boneIndex0,weight.weight0);Add(weight.boneIndex1,weight.weight1);Add(weight.boneIndex2,weight.weight2);Add(weight.boneIndex3,weight.weight3);return total;
        }
        static SkinProbe BakeSkin(SkinnedMeshRenderer skin,Transform owner)
        {
            var evidence=new SkinProbe{renderer=PathOf(skin.transform),mesh=skin.sharedMesh!=null?skin.sharedMesh.name:"MISSING",rendererBounds=skin.bounds,active=skin.gameObject.activeInHierarchy,enabled=skin.enabled};
            if(skin.sharedMesh==null){evidence.error="Missing source mesh";return evidence;}
            Mesh baked=null;
            try
            {
                baked=new Mesh{name="SouthGate_ReadOnly_Bake",hideFlags=HideFlags.HideAndDontSave};skin.BakeMesh(baked,false);
                var points=baked.vertices;evidence.vertices=points.Length;if(points.Length==0)throw new InvalidOperationException("Bake returned no vertices");
                BoneWeight[] weights=null;
                try{weights=skin.sharedMesh.boneWeights;evidence.weightStatus=weights.Length==points.Length?"Existing maximum-four source weights available":"Weight/vertex count mismatch";}
                catch(Exception e){evidence.weightStatus="CPU source weights unavailable: "+e.Message;}
                var bones=skin.bones;var footFlags=new[]{bones.Select(b=>b!=null&&FootBone(b,true)).ToArray(),bones.Select(b=>b!=null&&FootBone(b,false)).ToArray()};var feet=new List<FootRegion>();
                for(int side=0;side<2;side++)feet.Add(new FootRegion{side=side==0?"LeftFoot+LeftToeBase":"RightFoot+RightToeBase",minimum=Vector3.one*float.MaxValue,maximum=Vector3.one*float.MinValue});
                evidence.bakedMinimum=Vector3.one*float.MaxValue;evidence.bakedMaximum=Vector3.one*float.MinValue;
                for(int i=0;i<points.Length;i++)
                {
                    Vector3 world=skin.transform.TransformPoint(points[i]);
                    if(world.y<evidence.bakedMinimum.y)evidence.lowestVertex=world;
                    evidence.bakedMinimum=Vector3.Min(evidence.bakedMinimum,world);evidence.bakedMaximum=Vector3.Max(evidence.bakedMaximum,world);
                    if(weights==null||weights.Length!=points.Length)continue;
                    for(int side=0;side<2;side++)if(FootInfluence(weights[i],footFlags[side])>=.25f)
                    {
                        var region=feet[side];if(world.y<region.minimum.y)region.sole=world;
                        region.minimum=Vector3.Min(region.minimum,world);region.maximum=Vector3.Max(region.maximum,world);region.influencedVertices++;
                    }
                }
                evidence.feet=feet.Where(f=>f.influencedVertices>0).ToArray();
                foreach(var foot in evidence.feet)foot.support=SupportAt(foot.sole,owner);
                evidence.conservativeBoundsBelowBaked=evidence.bakedMinimum.y-skin.bounds.min.y;
                evidence.lowestSupport=SupportAt(evidence.lowestVertex,owner);
            }
            catch(Exception e){evidence.error=e.ToString();}
            finally{if(baked!=null)Object.DestroyImmediate(baked);}
            return evidence;
        }
        static VisualProbe SampleVisual(PrologueEncounter observed,string label)
        {
            var controller=observed.GetComponent<SouthGateGeneralController>();var presenter=observed.GetComponent<SouthGateGeneralPresentation>();
            var nav=observed.GetComponent<UnityEngine.AI.NavMeshAgent>();
            var report=new VisualProbe{label=label,frame=Time.frameCount,scaledTime=Time.time,state=controller?.State.ToString(),attack=controller?.CurrentPlan?.Kind.ToString(),
                planSampleTime=controller?.CurrentPlan?.SampleTime??-1,actorFeetY=observed.transform.position.y-.875f,graph=presenter!=null&&presenter.HasGraph,
                navVelocity=nav!=null&&nav.enabled&&nav.isOnNavMesh?nav.velocity:Vector3.zero};
            report.skins=observed.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s=>BakeSkin(s,observed.transform)).ToArray();
            var staticMeshes=new List<StaticProbe>();
            foreach(var filter in observed.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=filter.GetComponent<Renderer>();if(renderer==null||filter.sharedMesh==null)continue;
                var evidence=new StaticProbe{renderer=PathOf(filter.transform),mesh=filter.sharedMesh.name,rendererBounds=renderer.bounds,
                    weapon=PathOf(filter.transform).Contains("Temporary_ReusedMesh_Polearm"),active=renderer.gameObject.activeInHierarchy,enabled=renderer.enabled,status="CPU vertices unverified"};
                try
                {
                    float min=float.MaxValue;foreach(var vertex in filter.sharedMesh.vertices){var world=filter.transform.TransformPoint(vertex);if(world.y<min){min=world.y;evidence.minimumVertex=world;}}
                    if(min<float.MaxValue){evidence.status="Actual mesh vertices sampled";evidence.support=SupportAt(evidence.minimumVertex,observed.transform);}
                }
                catch(Exception e){evidence.status="Bounds only; CPU vertices unavailable: "+e.Message;}
                staticMeshes.Add(evidence);
            }
            report.staticMeshes=staticMeshes.ToArray();
            string[] names={"LeftFoot","RightFoot","LeftToeBase","RightToeBase"};
            report.footBones=observed.GetComponentsInChildren<Transform>(true).Where(t=>names.Contains(t.name)).Select(t=>new BoneProbe{name=t.name,position=t.position,support=SupportAt(t.position,observed.transform)}).ToArray();
            var valid=report.skins.Where(s=>s.error==null&&s.vertices>0&&s.active&&s.enabled).ToArray();
            report.actualLowestSkinY=valid.Length>0?valid.Min(s=>s.bakedMinimum.y):0;
            var all=observed.GetComponentsInChildren<Renderer>(true).Where(r=>r.gameObject.activeInHierarchy&&r.enabled).ToArray();report.boundsLowestY=all.Length>0?all.Min(r=>r.bounds.min.y):0;
            var soles=valid.SelectMany(s=>s.feet??Array.Empty<FootRegion>()).Where(f=>f.support!=null&&f.support.hit).ToArray();
            bool penetration=soles.Any(f=>f.support.sampleMinusGround<-.02f);
            bool otherSkinBelow=valid.Any(s=>s.lowestSupport!=null&&s.lowestSupport.hit&&s.lowestSupport.sampleMinusGround<-.02f);
            report.interpretation=penetration?"MEASURED foot-influenced sole vertex below physical support by >2cm in this sampled pose; inspect per-mesh details. This is not merely a renderer bound.":
                otherSkinBelow?"MEASURED deformed skin vertex below physical support; foot-region penetration not established. Per-mesh names separate clothing/body. Manual inspection required.":
                soles.Length>0?"No >2cm sole/skin support penetration found in this one pose. Compare static weapon bounds and conservativeBoundsBelowBaked to explain aggregate -0.601m; not all locomotion poses verified.":
                "UNVERIFIED sole attribution: missing foot weights or support. Baked skin minima and separate static/weapon bounds remain evidence; do not infer grounding from aggregate bounds.";
            return report;
        }
        static WorldMacroProgress Disk()
        {
            Isolation();var store=new AtomicJsonStore<WorldMacroProgress>(run.savePath,WorldMacroProgress.Valid);var value=store.Load();
            Check(value!=null&&store.LoadStatus=="primary","independent primary file reader","Disk reload is not an app restart.");return value;
        }
        static void Tick()
        {
            if(run==null)return;if(run.stopping){if(!EditorApplication.isPlayingOrWillChangePlaymode)Cleanup();return;}if(!run.active)return;
            try
            {
                if(run.holding){if(EditorApplication.timeSinceStartup>=run.phaseDeadline)Finish("PASS_BOUNDED_API_INTEGRATION",null);return;}
                if(EditorApplication.timeSinceStartup>run.deadline)throw new TimeoutException("160s overall bound exceeded");
                if(!EditorApplication.isPlaying||EditorApplication.isPaused)return;
                if(session==null||general==null||ui==null)Bind();
                if(session==null||general==null||ui==null||!session.DemoEscortReady)return;
                Isolation();Focus();
                if(run.step>=0&&EditorApplication.timeSinceStartup>run.phaseDeadline)throw new TimeoutException(run.phase+"; state="+general.State+"; enabled="+general.enabled+"; input="+session.GameplayInputBlocked+"; page="+ui.Page+"; focus="+Application.isFocused);
                if(run.holding)return;
                switch(run.step)
                {
                    case -1:
                        Check(session.LoadStatus=="primary"&&session.Progress.escort.Stage==DemoEscortStage.Delivered,"explicit UUID prerequisite loaded","All earlier journey stages are test setup, not verified gameplay.","FIXTURE");Fixture();break;
                    case 0:
                        if(!InputReady)return;Approach();run.scaledStart=Time.time;Next(1,"observe actual native Update attack cycle",48);break;
                    case 1:
                        if(!InputReady)return;
                        if(general.CurrentPlan==null)
                        {
                            var nav=actor.GetComponent<UnityEngine.AI.NavMeshAgent>();bool moving=nav!=null&&nav.enabled&&nav.isOnNavMesh&&nav.velocity.sqrMagnitude>.01f;
                            if(moving&&!run.sampledLocomotion){run.visualProbes.Add(SampleVisual(actor,"natural_locomotion"));run.sampledLocomotion=true;Persist();}
                            else if(!moving&&!run.sampledIdle){run.visualProbes.Add(SampleVisual(actor,"natural_idle"));run.sampledIdle=true;Persist();}
                        }
                        if(session.Walker.Body.GetComponent<PlayerVitals>().Hp01<=0)throw new InvalidOperationException("Player died during bounded native attack cycle");
                        if(general.CurrentPlan==null||general.CurrentPlan.Kind!=SouthGateAttackKind.NeutralEarthCombo||general.CurrentPlan.Pulses.Count!=2||!general.CurrentPlan.InEarthPreparation(Time.time))return;
                        var plan=general.CurrentPlan;
                        Check(general.enabled&&general.AttackEnabled&&!actor.GetComponent<EnemyController>().enabled&&presentation.HasGraph,
                            "actual combat owner and animation graph","Native Update produced the combo; no Controller.Tick/clock/forced attack used.");
                        ObserveVisual();
                        var damage=general.Vitals.TakeDamage(1,AttackProvenance.Create(session.Walker.Body.gameObject,DamageSource.PlayerDirect,Element.Wood));
                        Check(damage.AppliedDamage>0&&run.interruptions==1&&interruptedPlan==plan&&plan.EmpowerInterrupted&&plan.Pulses[1].Cancelled&&general.CounterWindowActive,
                            "real positive wood provenance interrupts Earth preparation","EnemyVitals.DamageResolved cancelled pending Earth only and opened counter opportunity.");
                        Next(2,"wait cancelled Earth release on actual clock",8);run.scaledStart=Time.time;break;
                    case 2:
                        if(Time.time<interruptedPlan.Pulses[1].ActiveEndAt+.1f)return;
                        Check(run.interruptions==1&&interruptedPlan.Pulses[1].Cancelled&&!interruptedPlan.Pulses[1].Consumed,"cancelled Earth never resolves","Waited beyond actual pending pulse end without synthetic ticks.");
                        Check(run.attacks>0&&Time.time>run.scaledStart,"native attack time elapsed",$"attacks={run.attacks}, actual contacts={run.contacts}, scaled={Time.time-run.scaledStart:F2}s");
                        run.killTime=Time.time;
                        var lethal=general.Vitals.TakeDamage(float.MaxValue,AttackProvenance.Create(session.Walker.Body.gameObject,DamageSource.PlayerDirect,Element.Wood));
                        Check(lethal.Killed&&run.deaths==1&&!general.Vitals.IsAlive,"real registered lethal EnemyVitals event","Session's subscribed death event is the only primary victory trigger.");
                        Check(!door.OpenAnimationComplete&&!ui.ShowingDemoEnding,"terminal UI waits for real door animation","No forced gate/UI command.");Next(3,"atomic completion and physical door Update",12);break;
                    case 3:
                        if(!session.DemoCampaignCompleted||!door.OpenAnimationComplete||!ui.ShowingDemoEnding)return;
                        Check(run.openingEvents==1&&Time.time-run.openTime>=2.35f,"one persisted event then real 2.4s opening",$"scaled door elapsed={Time.time-run.openTime:F3}, events={run.openingEvents}");
                        Check(session.Progress.ledger.currency==run.beforeCurrency+run.reward&&session.Progress.campaign.Completed.Count(s=>s=="south_gate")==1&&session.Progress.campaign.Completed.Count(s=>s=="ending")==1,
                            "exact configured reward and ordered completion",$"currency={session.Progress.ledger.currency}, reward={run.reward}");
                        Check(ui.Pause.IsPaused&&ui.Pause.Gate.InputBlocked&&session.GameplayInputBlocked&&Time.timeScale==0,
                            "actual ending UI pauses gameplay and gates input","ShowingDemoEnding, Pause, shared InputGate and timeScale agree.");
                        var disk=Disk();Check(disk.defeated.Contains(WorldMacroPlaytestSession.SouthGateGeneralId)&&disk.ledger.completed.Contains(WorldMacroPlaytestSession.SouthGateOpenedId)&&
                            disk.campaign.Completed.Contains("ending")&&disk.ledger.currency==session.Progress.ledger.currency&&disk.escort.Stage==DemoEscortStage.Delivered,
                            "independent disk completion evidence","Actual Session atomic transaction persisted gate, death, ending, reward, delivered state.");
                        session.EnemyDefeated(WorldMacroPlaytestSession.SouthGateGeneralId);Next(4,"duplicate notification remains idempotent",5);break;
                    case 4:
                        Check(run.openingEvents==1&&run.deaths==1&&session.Progress.ledger.currency==run.beforeCurrency+run.reward&&Disk().ledger.currency==run.beforeCurrency+run.reward,
                            "duplicate post-death ID cannot repay","Primary kill was EnemyVitals; duplicate string notification was only a negative check.");
                        Protect();
                        if(run.holdEnding){run.holding=true;run.phase="HOLD_ENDING: real ending UI already paused; root may capture then release";run.phaseDeadline=EditorApplication.timeSinceStartup+50;Persist();}
                        else Finish("PASS_BOUNDED_API_INTEGRATION",null);break;
                }
            }
            catch(Exception e){Finish("FINDINGS",e.ToString());}
        }
        static void Restore()
        {
            if(session==null)session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(subscribed){if(general!=null){general.AttackStarted-=Attack;general.AttackImpactResolved-=Impact;general.EmpowerInterrupted-=Interrupted;if(general.Vitals!=null)general.Vitals.Died-=Died;}
                if(session!=null)session.DemoSouthGateOpened-=Opened;}subscribed=false;
            if(original==null)original=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(run.contentPath);
            if(run.installed&&session!=null&&session.TestSaveSuffix==run.suffix){session.Content=original;session.CombatActive=run.priorCombat;}
            foreach(int id in new[]{run.contentId,run.campaignId}){var obj=EditorUtility.InstanceIDToObject(id);if(obj!=null&&!AssetDatabase.Contains(obj))Object.DestroyImmediate(obj);}
        }
        static void Finish(string status,string error)
        {
            if(run==null||run.cleaned)return;run.status=status;run.error=error;run.active=false;run.holding=false;run.stopping=true;run.phase="restoring runtime profiles and exiting Play";
            try{Restore();}catch(Exception e){run.status="FINDINGS";run.error=(run.error??"")+" Cleanup: "+e;}
            Persist();if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.isPlaying=false;else Cleanup();
        }
        static void PlayChanged(PlayModeStateChange state)
        {
            if(run==null||run.cleaned)return;
            if(state==PlayModeStateChange.ExitingPlayMode){if(run.active){run.status="ABORTED";run.error="Play exited before completion";run.active=false;}try{Restore();}catch(Exception e){run.error=e.ToString();run.status="FINDINGS";}Persist();}
            if(state==PlayModeStateChange.EnteredEditMode)Cleanup();
        }
        static void Quitting(){if(run!=null&&!run.cleaned){run.status="ABORTED";run.error="Editor quitting";try{Restore();}finally{Cleanup();}}}
        static void Cleanup()
        {
            if(run==null||run.cleaned)return;
            try{Protect();}catch(Exception e){run.status="FINDINGS";run.error=(run.error??"")+" Isolation: "+e;}
            if(run.priorSuffix==Missing)SessionState.EraseString(SuffixKey);else SessionState.SetString(SuffixKey,run.priorSuffix);
            Application.runInBackground=run.priorBackground;run.active=false;run.holding=false;run.stopping=false;run.cleaned=true;run.phase="Edit mode restored; UUID files retained only as test evidence";
            EditorApplication.update-=Tick;Persist();
        }
    }
}
