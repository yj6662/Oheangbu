using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Optional additive role pass. No campaign/quest logic, NPC movement, checkpoint or economy changes.
    public static class DemoNpcRolesAuthoring
    {
        const string Root="Demo_NpcRoles", Keeper="geumpyo_innkeeper";
        const string Bowl="Assets/Korea_TreasureProps/Prefabs/SM_071_Bowl.prefab";
        const string Scroll="Assets/Korea_TreasureProps/Prefabs/SM_029_Wooden_Scroll.prefab";
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/Demo/NpcRoles"));
        static WorldMacroPlaytestSession Session=>Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        [Serializable] sealed class Role { public string role,id,scenePath,dialogue,source,visibilityStatus,approachStatus;public bool actualVisual,actualInteraction,previewCullProtected,visibleNow,canInteractNow;public float actorToInteraction,playerDistance,radius;public int safeApproaches; }
        [Serializable] sealed class Check {public string name,status,detail;}
        [Serializable] sealed class Report
        {
            public string status,scope="Scene/read-only source audit until explicit apply. Existing NPC proxies remain temporary. Two inspection officers count as one occupational role.";
            public int distinctRoleCount,expectedRoles=8,explicitCheckpoints,legacyPlayableCheckpoints=2,legacyMineRestoreAnchors=1;
            public Vector3 keeperFeet,keeperFacing;public bool keeperClear;public Role[] roles;public Check[] checks;
            public string[] unverified={"Native F interaction and live NPC approach", "Final humanoid costume/animation and visual approval", "Checkpoint consolidation: proposal only; no IDs or save restore removed"};
        }
        static void RequireEdit()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||Session==null||Session.gameObject.scene.path!=DemoFoundationAuthoring.Scene||
                AssetDatabase.GetAssetPath(Session.Content)!=DemoFoundationAuthoring.Folder+"/Content.asset")throw new InvalidOperationException("Isolated W_Demo_Campaign Edit scene/content required");
        }
        public static string Execute(string command)
        {
            if(command=="runtime-audit")
            {
                if(!EditorApplication.isPlaying||Session==null||Session.gameObject.scene.path!=DemoFoundationAuthoring.Scene)throw new InvalidOperationException("Actual demo Play session required; read-only audit never teleports or changes state.");
                Directory.CreateDirectory(Output);return Save("runtime_audit.json",Audit(true));
            }
            RequireEdit();Directory.CreateDirectory(Output);Physics.SyncTransforms();
            if(command=="survey")return Save("survey.json",Survey());
            if(command=="apply")return Apply();
            if(command=="audit")return Save("audit.json",Audit());
            throw new ArgumentException("survey, apply, audit or runtime-audit");
        }
        static string Save(string name,Report report){string json=JsonUtility.ToJson(report,true);File.WriteAllText(Path.Combine(Output,name),json);return json;}
        static string PathOf(Transform t){if(t==null)return "MISSING";string path=t.name;while(t.parent!=null){t=t.parent;path=t.name+"/"+path;}return path;}
        static Transform Named(string name)=>Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .FirstOrDefault(t=>t.gameObject.scene==Session.gameObject.scene&&t.name==name);
        static Transform Actor(string id)
        {
            if(id=="village_commission")return Named("Playtest_Village_Office")?.Find("Clerk_TemporaryAppearance")??Named("Clerk_TemporaryAppearance");
            if(id=="checkpoint_1"||id=="checkpoint_2"||id=="cargo_delivery")
                return Named(DemoEscortSceneAuthoring.RootName)?.Find(id+"/"+(id=="cargo_delivery"?"GuesthouseClerk_ExistingProxy":"InspectionOfficer_ExistingProxy"));
            if(id=="jeongdam_j1"||id=="wangso_w1")return Named(DemoChapterTwoAuthoring.RootName)?.Find("NPCs/"+id);
            if(id==Keeper)return Named(Root)?.Find(Keeper);
            return Session.PreviewPoints.FirstOrDefault(p=>p!=null&&p.Id==id&&p.Visual!=null)?.transform;
        }
        static PrologueContentSO.Point Point(string id)=>id=="village_commission"?Session.Content.Opening?.Commission:Session.Content.Points.SingleOrDefault(p=>p.Id==id);
        static string EffectiveText(string id)
        {
            var stage=Session.Content.Campaign.Stages.FirstOrDefault(s=>s.TriggerId==id&&s.Event==Oheangbu.Data.Demo.DemoEventKind.Interaction);
            return stage!=null&&!string.IsNullOrEmpty(stage.Dialogue)?stage.Dialogue:Point(id)?.Text;
        }
        static Role Describe(string role,string id)
        {
            var actor=Actor(id);var point=Point(id);var identity=actor?.GetComponent<WorldMacroContentPoint>();
            var result=new Role{role=role,id=id,scenePath=PathOf(actor),actualVisual=actor!=null&&actor.GetComponentsInChildren<Renderer>(true).Length>0,
                actualInteraction=point!=null,dialogue=EffectiveText(id),source=id==Keeper?"Existing Chapter2 Replaceable_NPC_Visual clone":"Existing scene actor; final model deferred",
                previewCullProtected=identity==null||identity.CombatConnected,
                visibleNow=actor!=null&&actor.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled&&r.gameObject.activeInHierarchy)};
            if(point!=null&&actor!=null){result.radius=point.Radius;result.actorToInteraction=Vector3.Distance(actor.position,point.Position);result.playerDistance=Vector3.Distance(Session.Walker.Body.transform.position,point.Position);}
            return result;
        }
        static Role[] Roles()=>new[]{Describe("아전","village_commission"),Describe("벌목꾼","logger"),Describe("약초꾼","herbalist"),Describe("주막 주인",Keeper),
            Describe("정담","jeongdam_j1"),Describe("왕소","wangso_w1"),Describe("검문관","checkpoint_1"),Describe("검문관","checkpoint_2"),Describe("객주 직원","cargo_delivery")};
        static bool Clear(Vector3 feet)
        {
            foreach(var c in Physics.OverlapCapsule(feet+Vector3.up*.32f,feet+Vector3.up*1.42f,.27f,~0,QueryTriggerInteraction.Ignore))
                if(!c.transform.IsChildOf(Session.Walker.Body.transform))return false;
            return true;
        }
        static Report Survey()
        {
            var roles=Roles();var report=new Report{roles=roles,distinctRoleCount=roles.Where(r=>r.actualVisual&&r.actualInteraction).Select(r=>r.role).Distinct().Count(),
                explicitCheckpoints=Session.Content.Checkpoints.Length};
            var current=Actor(Keeper);
            if(current!=null){report.keeperFeet=current.position;report.keeperFacing=current.forward;report.keeperClear=true;}
            else
            {
                var inn=Point("geumpyo_inn")??throw new InvalidOperationException("Actual inn Rest missing");
                // Keep the rest/door interaction space clear and stay within the existing inn's social cluster.
                foreach(float radius in new[]{3.4f,4.2f,5f})
                {
                    for(int i=0;i<12;i++)
                    {
                        float angle=i*Mathf.PI/6;var p=inn.Position+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*radius;
                        try{p=WorldMacroPlaytestAuthoring.Ground(p,true)+Vector3.up*.06f;}catch(Exception){continue;}
                        if(Mathf.Abs(p.y-inn.Position.y)>.7f||Session.Content.Points.Any(q=>q.Id!="geumpyo_inn"&&Vector3.Distance(q.Position,p)<2.1f)||!Clear(p))continue;
                        var approach=Vector3.Lerp(p,inn.Position,.45f);try{approach=WorldMacroPlaytestAuthoring.Ground(approach,true)+Vector3.up*.06f;}catch(Exception){continue;}
                        if(!Session.TrySafeFeet(approach,out _))continue;
                        report.keeperFeet=p;report.keeperFacing=Vector3.ProjectOnPlane(inn.Position-p,Vector3.up).normalized;report.keeperClear=true;break;
                    }
                    if(report.keeperClear)break;
                }
            }
            bool source=Actor("jeongdam_j1")?.GetComponent<WorldMacroContentPoint>()?.Visual!=null;
            bool existing=roles.Where(r=>r.id!=Keeper).All(r=>r.actualVisual&&r.actualInteraction);
            bool props=AssetDatabase.LoadAssetAtPath<GameObject>(Bowl)!=null&&AssetDatabase.LoadAssetAtPath<GameObject>(Scroll)!=null;
            report.checks=new[]{new Check{name="seven existing roles plus two distinct inspection actors",status=existing?"PASS":"FAIL"},
                new Check{name="reusable NPC and Korean prop sources",status=source&&props?"PASS":"FAIL"},
                new Check{name="safe innkeeper and nearby approach",status=report.keeperClear?"PASS":"FAIL"},
                new Check{name="checkpoint budget",status="FINDING",detail=report.explicitCheckpoints+" explicit + office/inn 2 = "+(report.explicitCheckpoints+2)+" active locations; mine_start retained for compatibility. No deletions."}};
            report.status=existing&&source&&props&&report.keeperClear?"PASS":"FAIL";return report;
        }
        static void Backup()
        {
            string dir=Path.Combine(Output,"Backups");Directory.CreateDirectory(dir);
            foreach(string path in new[]{DemoFoundationAuthoring.Scene,AssetDatabase.GetAssetPath(Session.Content)})foreach(string p in new[]{path,path+".meta"})
                if(File.Exists(p)&&!File.Exists(Path.Combine(dir,Path.GetFileName(p))))File.Copy(p,Path.Combine(dir,Path.GetFileName(p)));
        }
        static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-","");}
        static Transform Child(Transform parent,string name){var node=parent.Find(name);if(node!=null)return node;node=new GameObject(name).transform;node.SetParent(parent,false);return node;}
        static void PutText(string id,string prompt,string text)
        {
            var p=Point(id)??throw new InvalidOperationException("Missing actual interaction "+id);
            var stage=Session.Content.Campaign.Stages.FirstOrDefault(s=>s.TriggerId==id&&s.Event==Oheangbu.Data.Demo.DemoEventKind.Interaction);
            if(stage!=null&&!string.IsNullOrEmpty(stage.Dialogue))throw new InvalidOperationException("Campaign overrides "+id+"; do not silently change shared narrative owner.");
            p.Prompt=prompt;p.Text=text;
        }
        static Bounds VisualBounds(Transform node)
        {var all=node.GetComponentsInChildren<Renderer>(true);if(all.Length==0)throw new InvalidOperationException("No existing prop visual");var b=all[0].bounds;foreach(var r in all.Skip(1))b.Encapsulate(r.bounds);return b;}
        static void Prop(Transform parent,string name,string path,Vector3 bottom,float height,float yaw)
        {
            if(parent.Find(name)!=null)return;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path)??throw new InvalidOperationException(path);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);instance.name=name;instance.transform.rotation=Quaternion.Euler(0,yaw,0);
            PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            var b=VisualBounds(instance.transform);float scale=Mathf.Min(height/Mathf.Max(.001f,b.size.y),.55f/Mathf.Max(.001f,b.size.x),.55f/Mathf.Max(.001f,b.size.z));instance.transform.localScale*=scale;b=VisualBounds(instance.transform);
            instance.transform.position+=bottom-new Vector3(b.center.x,b.min.y,b.center.z);
            foreach(var c in instance.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
        }
        static string Apply()
        {
            var survey=Survey();Save("survey.json",survey);if(survey.status!="PASS")throw new InvalidOperationException("NPC source/ground survey failed");
            string campaignBefore=Hash(JsonUtility.ToJson(Session.Content.Campaign)),checkpointsBefore=string.Join("|",Session.Content.Checkpoints.Select(c=>JsonUtility.ToJson(c)));Backup();
            var root=Named(Root)??new GameObject(Root).transform;var keeper=Child(root,Keeper);
            keeper.SetPositionAndRotation(survey.keeperFeet,Quaternion.LookRotation(survey.keeperFacing));
            var point=keeper.GetComponent<WorldMacroContentPoint>()??keeper.gameObject.AddComponent<WorldMacroContentPoint>();point.Id=Keeper;point.CombatConnected=true;point.CombatRole="주막 주인 / 임시 외형";
            if(point.Visual==null)
            {
                var template=Actor("jeongdam_j1").GetComponent<WorldMacroContentPoint>().Visual;
                var visual=Object.Instantiate(template,keeper);visual.name="Replaceable_Innkeeper_Visual";visual.localPosition=Vector3.up*.85f;visual.localRotation=Quaternion.identity;visual.gameObject.SetActive(true);
                foreach(var c in visual.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);point.Visual=visual;
                var body=keeper.gameObject.AddComponent<CapsuleCollider>();body.radius=.27f;body.height=1.7f;body.center=Vector3.up*.85f;
            }
            if(Point(Keeper)==null)Session.Content.Points=Session.Content.Points.Concat(new[]{new PrologueContentSO.Point{Id=Keeper,Kind=PrologueInteractionKind.Conversation,Position=keeper.position,Radius=2.3f,Currency=0}}).ToArray();
            PutText(Keeper,"주막 주인에게 쉬고 정비할 곳 묻기","주막 주인: 산길에서 오셨구려. 앞의 낮은 상에서 쉬면 체력과 먹을 채우고 이곳을 다시 일어설 자리로 삼을 수 있소.\n\n쉬고 나면 ‘정비’에서 조선통보로 마석과 체력·먹 용량을 보강할 수 있소. 이미 올린 보강은 다른 정비소에서도 그대로 이어진다오. 벌목꾼과 약초꾼에게 숲 사정도 듣고 가시오.");
            PutText("checkpoint_1","첫 검문관에게 차패와 화물 보증 제시","검문관: 짐을 맡은 사람과 보증할 차패를 함께 확인하겠소. 봉인은 그대로 두시오.\n\n차패와 운송 기록을 대조했소. 이 검문을 통과한 사실을 남기겠소. 황경 앞에서는 다시 확인하니 왕소와 화물을 함께 데려가시오.");
            PutText("checkpoint_2","황경 앞 검문관에게 운송 기록 확인받기","검문관: 앞 검문의 확인 기록이 있구려. 여기서는 동행인과 도착할 객주를 한 번 더 대조하겠소. 봉인을 풀 필요는 없소.\n\n확인이 끝났소. 본점 접수처에 화물을 인도하고 수령 확인을 받으시오. 성문 쪽 일은 인도를 마친 뒤 알아보는 편이 좋겠소.");
            PutText("cargo_delivery","객주 직원에게 봉인 화물 인도","객주 직원: 왕소가 맡긴 운송 건이군요. 두 검문의 기록과 봉인 상태를 확인했습니다. 안의 물건을 꺼내 보일 필요는 없습니다.\n\n화물을 본점에서 인수했습니다. 왕소와 함께 오시느라 수고하셨습니다. 남문 쪽으로 가기 전 가까운 쉼터에서 몸과 먹을 가다듬으십시오.");
            foreach(string id in new[]{"logger","herbalist"})
            {
                var identity=Actor(id).GetComponent<WorldMacroContentPoint>();
                if(identity==null||identity.Visual==null||Point(id)==null)throw new InvalidOperationException("Actual dialogue/visual binding missing "+id);
                identity.CombatConnected=true;identity.CombatRole=id=="logger"?"벌목꾼 / 실제 대화 연결":"약초꾼 / 실제 대화 연결";
                identity.Visual.gameObject.SetActive(true);EditorUtility.SetDirty(identity);
            }
            var props=Child(root,"ExistingRoleProps");
            var table=Named("Playtest_OwnedAssets")?.Find("Geumpyo_ThatchedInn/Rest_Low_Table");
            if(table!=null){var b=VisualBounds(table);Prop(props,"Inn_ServingBowl",Bowl,new Vector3(b.center.x+.18f,b.max.y+.02f,b.center.z),.13f,0);}
            foreach(string id in new[]{"checkpoint_1","checkpoint_2","cargo_delivery"})
            {
                var attendant=Actor(id);var identity=attendant.GetComponent<WorldMacroContentPoint>()??attendant.gameObject.AddComponent<WorldMacroContentPoint>();
                identity.Id=id;identity.Visual=attendant.Find("Replaceable_NPC_Visual");identity.CombatConnected=true;identity.CombatRole=id=="cargo_delivery"?"객주 직원 / 임시 외형":"검문관 / 임시 외형";
                var desk=attendant.parent.Find("ExistingInspectionDesk");if(desk==null)throw new InvalidOperationException("Actual attendant desk missing: "+id);
                var b=VisualBounds(desk);Prop(props,id+"_RegisterScroll",Scroll,new Vector3(b.center.x,b.max.y+.015f,b.center.z),.08f,desk.eulerAngles.y);
            }
            Session.PreviewPoints=Session.PreviewPoints.Where(p=>p!=null).Concat(root.GetComponentsInChildren<WorldMacroContentPoint>()).
                Concat(new[]{"checkpoint_1","checkpoint_2","cargo_delivery"}.Select(id=>Actor(id).GetComponent<WorldMacroContentPoint>())).Distinct().ToArray();
            if(Hash(JsonUtility.ToJson(Session.Content.Campaign))!=campaignBefore||string.Join("|",Session.Content.Checkpoints.Select(c=>JsonUtility.ToJson(c)))!=checkpointsBefore)throw new InvalidOperationException("Unexpected campaign/checkpoint mutation");
            EditorUtility.SetDirty(Session.Content);EditorUtility.SetDirty(Session);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(Session.gameObject.scene);EditorSceneManager.SaveScene(Session.gameObject.scene);
            return Save("audit.json",Audit());
        }
        static Report Audit(bool runtime=false)
        {
            var report=Survey();var checks=report.checks.ToList();
            bool all=report.roles.All(r=>r.actualVisual&&r.actualInteraction)&&report.distinctRoleCount==8;
            checks.Add(new Check{name="eight distinct roles (two inspection officers count once)",status=all?"PASS":"FAIL"});
            checks.Add(new Check{name="actual dialogue NPCs protected from unconnected-preview cull",status=report.roles.All(r=>r.previewCullProtected)?"PASS":"FAIL",detail="logger/herbalist previously had CombatConnected=false while ShowUnconnectedPreviewMarkers=false."});
            checks.Add(new Check{name="actual inn rest offers existing shop",status=WorldMacroCheckpointRules.TryResolve(Session.Content,null,"geumpyo_inn",out var cp)&&cp.Shop?"PASS":"FAIL",detail="Rest event opens existing 정비 UI; innkeeper only explains it."});
            foreach(string id in new[]{Keeper,"checkpoint_1","checkpoint_2","cargo_delivery"})checks.Add(new Check{name="non-placeholder effective dialogue "+id,status=(EffectiveText(id)?.Length??0)>80?"PASS":"FAIL"});
            if(runtime)
            {
                report.scope="Read-only real Play state after ordinary Session.Cull. No Cull invocation, teleport, F input, dialogue execution or save. Safe approach probes and current CanInteract are observations, not a playthrough.";
                foreach(var role in report.roles)
                {
                    var point=Point(role.id);if(point==null)continue;
                    role.canInteractNow=Session.CanInteract(role.id);
                    for(int i=0;i<8;i++)
                    {
                        float angle=i*Mathf.PI/4;var probe=point.Position+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*Mathf.Min(1.7f,point.Radius*.7f);
                        try{probe=WorldMacroPlaytestAuthoring.Ground(probe,true)+Vector3.up*.06f;}catch(Exception){continue;}
                        if(!Session.TrySafeFeet(probe,out var safe)||Vector3.Distance(safe,point.Position)>point.Radius)continue;
                        var eye=safe+Vector3.up*Session.Walker.EyeHeight;var to=point.Position+Vector3.up*1.25f-eye;
                        bool clear=Physics.RaycastAll(eye,to.normalized,to.magnitude,~0,QueryTriggerInteraction.Ignore).All(h=>h.transform.IsChildOf(Session.Walker.Body.transform)||h.transform.GetComponentInParent<WorldMacroContentPoint>()?.Id==role.id);
                        if(clear)role.safeApproaches++;
                    }
                    bool nearby=role.playerDistance<=220;
                    role.visibilityStatus=role.visibleNow?"VISIBLE":nearby?"NEARBY_NOT_VISIBLE":"FAR_VIEW_UNVERIFIED";
                    role.approachStatus=role.safeApproaches>0?"SUPPORTED_WITH_LOS":nearby?"NEARBY_NO_SAFE_APPROACH":"FAR_STREAMED_SUPPORT_UNVERIFIED";
                    bool structural=role.actualVisual&&role.actualInteraction&&role.previewCullProtected&&role.actorToInteraction<=role.radius+1;
                    bool localFailure=nearby&&(!role.visibleNow||role.safeApproaches==0);
                    string status=!structural||localFailure?"FAIL":!nearby&&(!role.visibleNow||role.safeApproaches==0)?"UNVERIFIED":"PASS";
                    checks.Add(new Check{name="runtime role binding and available approach "+role.id,status=status,
                        detail=$"visibility={role.visibilityStatus}; approach={role.approachStatus}; safe/LOS probes={role.safeApproaches}/8; actor-to-point={role.actorToInteraction:F2}m radius={role.radius:F2}; actual-player-distance={role.playerDistance:F2}; current CanInteract={role.canInteractNow}. Far hidden/streamed-out state is not classified as a missing actor. No input performed."});
                }
            }
            report.checks=checks.ToArray();report.status=checks.Any(c=>c.status=="FAIL")?"FAIL":checks.Any(c=>c.status=="UNVERIFIED")?"PASS_BINDINGS_WITH_UNVERIFIED_VISIBILITY":"PASS";return report;
        }
    }
}
