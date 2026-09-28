using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Data.World;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    // Detached real physics and central transaction checks. Does not open/save the user's scene or progress.
    public static class MumBridge295Checks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed=new List<string>(),failed=new List<string>();
            public string[] unverified={"Manual drawn glyph, free camera targeting and art acceptance in the authored river valleys",
                "Full late boss acquisition (reserved), saved/rest/realm-reentry Play integration and standalone performance"};
        }
        public static string Run()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Mum detached fixtures require Edit mode.");
            var report=new Report();void Check(bool ok,string name)=>(ok?report.passed:report.failed).Add(name);
            void Group(string name,Action action){try{action();}catch(Exception e){report.failed.Add(name+": "+e);}}
            var book=AssetDatabase.LoadAssetAtPath<SpellBookSO>("Assets/_Project/Data/Configs/SpellBook_Proto.asset");
            if(book==null)throw new InvalidOperationException("Original spell book is missing.");
            string before=EditorJsonUtility.ToJson(book);
            Group("Unlock",()=>Unlock(book,Check));
            Group("Central payment",()=>Payment(book,Check));
            Group("Bounds",()=>Bounds(book,Check));
            Group("Supports",()=>Supports(book,Check));
            Group("Natural bank joins",()=>BankJoins(book,Check));
            foreach(int fps in new[]{30,60,120}){int rate=fps;Group("Walk "+rate,()=>Walk(book,rate,Check));}
            Group("Lifecycle",()=>Lifecycle(book,Check));
            Group("Maximum",()=>Maximum(book,Check));
            Check(EditorJsonUtility.ToJson(book)==before,"shared spell book unchanged after fixtures");
            report.status=report.failed.Count==0?"PASS":"FAIL";return JsonUtility.ToJson(report,true);
        }
        static void Unlock(SpellBookSO book,Action<bool,string> check)
        {
            var resolver=new SpellResolver(book);
            check(!resolver.TryResolve(Letter(),out _),"ordinary resolver keeps mum locked");
            check(resolver.TryResolve(Letter(),out var cast,false,false,false,false,true)&&cast.Kind==SpellKind.Field&&cast.Element==Element.Earth&&cast.Power==0,"explicit mum ownership resolves a non-damaging Earth field cast");
            var p=WorldMacroProgress.CreateNew("mum295-detached",Vector3.zero,0);
            p.ui.LearnSpellLetter("뭄");check(!MumBridgeUnlock295.HasProof(p),"UI spell discovery does not unlock a bridge");
            p.ledger.completed.Add(MumBridgeUnlock295.CodaId);check(!MumBridgeUnlock295.HasProof(p),"coda fact alone does not replace late boss proof");
            p.campaign.EncounterEvidence.Add(MumBridgeUnlock295.BossId);check(!MumBridgeUnlock295.HasProof(p),"encounter evidence without durable defeated record remains locked");
            p.defeated.Add(MumBridgeUnlock295.BossId);check(MumBridgeUnlock295.HasProof(p),"durable coda and matching late boss records unlock mum");
            using(var f=new Fixture(book)){f.Unlocked=false;check(!f.Service.TryPrepareTo(Cast(),f.Target,out _)&&f.Service.ActiveCount==0,"constructed SpellCast cannot bypass the runtime unlock");}
        }
        static void Payment(SpellBookSO book,Action<bool,string> check)
        {
            using(var f=new Fixture(book))
            {
                f.Draw();check(f.Accepted==1&&f.Service.ActiveCount==1&&!f.Service.HasPrepared&&Near(f.Ink.Value,1-f.Config.SpellInkCost),"actual central glyph path spends exactly one normal spell cost");
            }
            using(var f=new Fixture(book))
            {
                f.Ink.Restore(0);f.Draw();check(f.Accepted==0&&f.Service.ActiveCount==0&&!f.Service.HasPrepared&&Near(f.Ink.Value,0),"insufficient ink removes the inactive candidate without support or payment");
            }
            using(var f=new Fixture(book))
            {
                f.Blocked=true;f.Draw();check(f.Accepted==0&&f.Service.ActiveCount==0&&Near(f.Ink.Value,1),"combat rejection happens before payment");
            }
            using(var f=new Fixture(book))
            {
                bool inserted=false;
                f.Changed.Subscribe(value=>{if(value<.999f&&!inserted){inserted=true;f.Box("New obstruction between payment and commit",new Vector3(0,.8f,5),new Vector3(1,2,1));}});
                f.Draw();check(inserted&&f.Accepted==0&&f.Service.ActiveCount==0&&!f.Service.HasPrepared&&Near(f.Ink.Value,1),"new obstruction at payment rejects commit and refunds the exact previous ink balance");
            }
        }
        static void Bounds(SpellBookSO book,Action<bool,string> check)
        {
            foreach(float length in new[]{3.99f,4f,40f,40.01f})using(var f=new Fixture(book,length))
                check(f.Service.TryPrepareTo(Cast(),f.Target,out _)==(length>=4&&length<=40),"horizontal span boundary "+length+"m");
            foreach(float rise in new[]{2f,2.01f})using(var f=new Fixture(book,30,rise))
                check(f.Service.TryPrepareTo(Cast(),f.Target,out _)==(rise<=2),"end height boundary "+rise+"m");
            foreach(float slope in new[]{7.9f,8.1f})using(var f=new Fixture(book,4,Mathf.Tan(slope*Mathf.Deg2Rad)*4))
                check(f.Service.TryPrepareTo(Cast(),f.Target,out _)==(slope<8),"ramp angle boundary "+slope+" degrees");
        }
        static void Supports(SpellBookSO book,Action<bool,string> check)
        {
            using(var f=new Fixture(book)){f.Far.AddComponent<WorldTemporarySupport>();check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"temporary destination support cannot chain bridges");}
            using(var f=new Fixture(book)){f.Near.AddComponent<WorldTemporarySupport>();check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"casting from generated support cannot extend a bridge chain");}
            using(var f=new Fixture(book)){f.Far.AddComponent<Rigidbody>().isKinematic=true;check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"dynamic destination is rejected even when kinematic");}
            using(var f=new Fixture(book))
            {
                f.WaterAt(.1f);check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"submerged banks are rejected using the rendered-water query");
            }
            using(var f=new Fixture(book))
            {
                f.Far.GetComponent<BoxCollider>().size=new Vector3(1,1,6);f.Sync();
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"narrow far landing fails full-width support checks");
            }
            using(var f=new Fixture(book))
            {
                f.Box("Low ceiling",new Vector3(0,1.4f,5),new Vector3(4,.5f,3));
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"head clearance is checked through the complete bridge span");
            }
            using(var f=new Fixture(book))
            {
                f.Box("Existing road",new Vector3(0,-.5f,5),new Vector3(5,1,10));
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"continuous terrain cannot be covered with a generated floor");
            }
            using(var f=new Fixture(book))
            {
                f.MovePlayer(new Vector3(0,.8f,0));check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"airborne origin cannot cast");
            }
        }
        static void Walk(SpellBookSO book,int fps,Action<bool,string> check)
        {
            using(var f=new Fixture(book))
            {
                f.WaterAt(-1);f.Draw();if(f.Service.ActiveCount!=1)throw new InvalidOperationException("No bridge: "+f.Service.LastFailure);
                var bridge=f.Service.Bridges[0];
                f.Service.Tick(1.19f);check(!bridge.Support.enabled,""+fps+"Hz: incomplete formation has no invisible full-span support");
                f.Paused=true;float age=bridge.Age;f.Service.Tick(10);check(Near(bridge.Age,age),fps+"Hz: pause keeps formation clock fixed");
                f.Paused=false;f.Service.Tick(.02f);f.Sync();check(bridge.Support.enabled&&bridge.Support.sharedMesh==bridge.Mesh,fps+"Hz: completed visual and collider use the same mesh");
                float minimum=float.PositiveInfinity,maximumWater=0;int steps=Mathf.CeilToInt(12/(4.5f/fps));
                for(int i=0;i<steps;i++)
                {
                    f.Body.Move(Vector3.forward*(4.5f/fps)+Vector3.down*.04f);f.Sync();
                    minimum=Mathf.Min(minimum,f.Feet.y);maximumWater=Mathf.Max(maximumWater,f.Query.Immersion(f.Feet));
                }
                check(f.Feet.z>11.8f&&minimum>-.1f&&maximumWater<.01f,fps+"Hz: real CharacterController crosses dry above water; minY="+minimum+", endZ="+f.Feet.z);
                f.Service.Tick(120);check(f.Service.ActiveCount==1,fps+"Hz: no lifetime timer removes the completed bridge");
            }
        }
        static void BankJoins(SpellBookSO book,Action<bool,string> check)
        {
            using(var f=new Fixture(book,32))
            {
                f.LongGentleBanks();
                check(f.Service.TryPrepareTo(Cast(),f.Target,out _)&&f.Service.CommitPrepared(),"six-metre end approaches accept gentle static banks below the walking surface");
            }
            using(var f=new Fixture(book,32))
            {
                f.LongGentleBanks();f.Profile.MaximumBankEmbedApproach=2.5f;
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"the same gentle banks exceed the old2.5m approach setting");
            }
            using(var f=new Fixture(book,32))
            {
                f.LongGentleBanks();f.Box("Separate rock below long join",new Vector3(0,-.23f,5),new Vector3(.4f,.12f,.4f));
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"a separate rock below the top cannot borrow the longer terrain approach");
            }
            using(var f=new Fixture(book,32))
            {
                f.LongGentleBanks();f.Box("Head obstruction on long join",new Vector3(0,1.7f,5),new Vector3(.6f,.3f,.6f));
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"head clearance stays strict throughout the longer bank approach");
            }
            using(var f=new Fixture(book,16))
            {
                f.LongGentleBanks();
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"six-metre setting clamps to span quarter and rejects bank terrain in the central half");
            }
            using(var f=new Fixture(book))
            {
                f.FarCorner(.09f);
                check(f.Service.TryPrepareTo(Cast(),f.Target,out _)&&f.Service.CommitPrepared(),"a9cm endpoint bank corner respects the existing landing height tolerance");
            }
            using(var f=new Fixture(book))
            {
                f.FarCorner(.19f);
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"endpoint bank variation above18cm still rejects");
            }
            using(var f=new Fixture(book))
            {
                f.FarCorner(.09f);f.Box("Separate rock on landing",new Vector3(0,.1f,9.75f),new Vector3(.3f,.12f,.2f));
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"separate rock cannot borrow the18cm landing-ground allowance");
            }
            using(var f=new Fixture(book))
            {
                f.FarCorner(.09f);f.Box("Landing wall",new Vector3(.5f,.8f,9.8f),new Vector3(.2f,1.6f,.2f));
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"wall in the landing zone blocks full body/head clearance");
            }
            using(var f=new Fixture(book))
            {
                f.SlopedBanks();
                check(f.Service.TryPrepareTo(Cast(),f.Target,out _)&&f.Service.CommitPrepared(),"gentle static dry banks can meet the underside within the bounded end joins");
            }
            using(var f=new Fixture(book))
            {
                f.SlopedBanks();f.Profile.MaximumBankEmbedApproach=.6f;
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"profile join distance limits embedding instead of ignoring the whole bank collider");
            }
            using(var f=new Fixture(book))
            {
                f.SlopedBanks();f.Box("Middle underside rock",new Vector3(0,-.25f,5),new Vector3(.7f,.12f,.7f));
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"middle underside obstacle rejects even though its top is below the walking surface");
            }
            using(var f=new Fixture(book))
            {
                f.SlopedBanks();f.Box("Protruding bank rock",new Vector3(0,.12f,1.2f),new Vector3(.5f,.3f,.5f));
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"protruding bank rock rejects inside the allowed underside join distance");
            }
            using(var f=new Fixture(book))
            {
                f.SlopedBanks();f.Box("Separate buried bank wall",new Vector3(0,-.16f,1.2f),new Vector3(.4f,.12f,.4f));
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"a separate wall below the top cannot borrow the dry bank intersection allowance");
            }
            using(var f=new Fixture(book))
            {
                f.SlopedBanks();f.Box("Temporary support under join",new Vector3(0,-.16f,1.2f),new Vector3(.4f,.12f,.4f)).AddComponent<WorldTemporarySupport>();
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"temporary support remains forbidden in an embedded bank join");
            }
            using(var f=new Fixture(book,4))
            {
                f.Box("Short-span underside obstruction",new Vector3(0,-.22f,1.5f),new Vector3(.5f,.12f,.3f));
                check(!f.Service.TryPrepareTo(Cast(),f.Target,out _),"bank joins clamp to one quarter of span and retain central clearance on a4m bridge");
            }
        }
        static void Lifecycle(SpellBookSO book,Action<bool,string> check)
        {
            using(var f=new Fixture(book))
            {
                f.Draw();f.Service.Tick(2);f.MovePlayer(new Vector3(0,.08f,5));var bridge=f.Service.Bridges[0];
                check(!f.Query.IsPermanentDrySupport(new Vector3(0,.025f,5),bridge.Support),"bridge is excluded from permanent save/drop support");
                f.Service.ResetForWorldBoundary();check(f.Service.ActiveCount==1&&bridge.PendingRemoval&&bridge.Support.enabled,"reset preserves occupied support until safe departure");
                f.MovePlayer(new Vector3(0,.05f,13));f.Service.Tick(.02f);check(f.Service.ActiveCount==0,"pending boundary cleanup finishes after reaching the far bank");
            }
            using(var f=new Fixture(book))
            {
                string area="north";f.Service.CurrentArea=()=>area;f.Service.NotifyArea(area);f.Draw();f.Service.Tick(2);f.MovePlayer(new Vector3(0,.05f,-2));
                area="central";f.Service.NotifyArea(area);check(f.Service.ActiveCount==1,"leaving a realm does not impose a hidden lifetime timer");
                area="north";f.Service.NotifyArea(area);check(f.Service.ActiveCount==0,"re-entering the bridge realm clears its temporary geometry");
            }
            using(var f=new Fixture(book))
            {
                f.Draw();f.Service.Tick(2);f.Health.ApplyFatalFall();check(f.Service.ActiveCount==0,"environment death clears every bridge immediately");
            }
            using(var f=new Fixture(book))
            {
                f.Draw();f.Service.Tick(2);f.MovePlayer(new Vector3(0,.08f,5));var bridge=f.Service.Bridges[0];
                f.Service.enabled=false;
                // Preview scenes do not always send editor enable messages to ordinary runtime behaviours.
                Call(f.Service,"OnDisable");var hold=bridge!=null?bridge.GetComponent<MumBridgeSafetyHold>():null;
                check(hold!=null&&bridge.Support.enabled,"disabling the owner transfers occupied support to a safety holder");
                f.MovePlayer(new Vector3(0,.05f,13));Call(hold,"LateUpdate");check(bridge==null,"safety holder cleans up after the passenger leaves");
            }
        }
        static void Maximum(SpellBookSO book,Action<bool,string> check)
        {
            using(var f=new Fixture(book))
            {
                bool made=true;
                for(int i=0;i<16;i++)
                {
                    f.MovePlayer(new Vector3(i*4,.05f,0));var target=new Vector3(i*4,0,10);
                    made&=f.Service.TryPrepareTo(Cast(),target,out _)&&f.Service.CommitPrepared();f.Service.Tick(2);
                }
                check(made&&f.Service.ActiveCount==16,"sixteen independent real bridges may coexist");
                f.MovePlayer(new Vector3(64,.05f,0));check(!f.Service.TryPrepareTo(Cast(),new Vector3(64,0,10),out _)&&f.Service.ActiveCount==16&&!f.Service.HasPrepared,"seventeenth placement rejects without replacing an existing bridge");
            }
        }
        static SpellCast Cast()=>new SpellCast('뭄',SpellKind.Field,Element.Earth,0,default,1);
        static DrawnLetter Letter()=>new DrawnLetter('뭄',Jamo.Mieum,Jamo.U,Jamo.Mieum,.8f,.7f,2f,2f,4);
        static bool Near(float a,float b)=>Mathf.Abs(a-b)<.002f;
        static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
        static void Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);
        sealed class Fixture:IDisposable
        {
            public readonly Scene Scene;
            public readonly CharacterController Body;
            public readonly PlayerVitals Health;
            public readonly MumBridgeProfileSO Profile;
            public readonly MumBridgeService Service;
            public readonly CombatConfigSO Config;
            public readonly CombatLoopWiring Wiring;
            public readonly InkPool Ink;
            public readonly FloatEventChannelSO Changed;
            public readonly WorldTerrainQuery Query;
            public readonly GameObject Near,Far;
            public readonly Vector3 Target;
            readonly Material material;
            public bool Unlocked=true,Blocked,Paused;
            public int Accepted;
            public Vector3 Feet=>Body.transform.TransformPoint(Body.center)-Vector3.up*Body.height*.5f;
            public Fixture(SpellBookSO book,float length=10,float rise=0)
            {
                Scene=EditorSceneManager.NewPreviewScene();Config=ScriptableObject.CreateInstance<CombatConfigSO>();
                Changed=ScriptableObject.CreateInstance<FloatEventChannelSO>();Profile=ScriptableObject.CreateInstance<MumBridgeProfileSO>();
                var shader=Shader.Find("Universal Render Pipeline/Lit");if(shader==null)throw new InvalidOperationException("URP Lit is missing.");
                material=new Material(shader);Profile.DeckMaterial=material;
                Near=Box("Near permanent dry bank",new Vector3(30,-.5f,-2.5f),new Vector3(160,1,6));
                Far=Box("Far permanent dry bank",new Vector3(30,rise-.5f,length+2.5f),new Vector3(160,1,6));Target=new Vector3(0,rise,length);
                var player=Object("Player",new Vector3(0,.05f,0));Body=player.AddComponent<CharacterController>();Body.height=1.75f;Body.center=Vector3.up*.875f;Body.radius=.27f;Body.skinWidth=.015f;Body.minMoveDistance=0;Body.stepOffset=.3f;
                Health=player.AddComponent<PlayerVitals>();Set(Health,"_config",Config);Health.Restore();
                var cameraObject=Object("Aim",new Vector3(0,1.5f,-1));var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.transform.LookAt(Target);
                var host=Object("Bridge wiring",Vector3.zero);Query=host.AddComponent<WorldTerrainQuery>();Service=host.AddComponent<MumBridgeService>();
                Service.Configure(Body,null,Health,camera,Query,Profile,()=>Unlocked,()=>false,()=>Paused);Service.CombatBlocked=()=>Blocked;
                Wiring=host.AddComponent<CombatLoopWiring>();Set(Wiring,"_config",Config);Set(Wiring,"_playerVitals",Health);Set(Wiring,"_playerTransform",player.transform);
                Ink=new InkPool(Config,Changed);Ink.Restore();Wiring.Construct(new SpellResolver(book),new ParryJudge(Config),new GroggyMeter(Config),Ink);
                Wiring.MumBridges=Service;Wiring.CastAccepted+=(_,__,___)=>Accepted++;Sync();
            }
            GameObject Object(string name,Vector3 p){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,Scene);go.transform.position=p;return go;}
            public GameObject Box(string name,Vector3 center,Vector3 size){var go=Object(name,center);go.AddComponent<BoxCollider>().size=size;Sync();return go;}
            public void SlopedBanks()
            {
                foreach(bool far in new[]{false,true})
                {
                    var bank=far?Far:Near;var collider=bank.GetComponent<BoxCollider>();
                    bank.transform.SetPositionAndRotation(far?Target:Vector3.zero,Quaternion.Euler(far?-11.31f:11.31f,0,0));
                    collider.center=new Vector3(0,-.3f,far?1.5f:-1.5f);collider.size=new Vector3(160,.6f,8);
                }
                // The single bank collider supplies both the dry rear footprint and the gently descending approach.
                Sync();MovePlayer(new Vector3(0,.05f,0));
            }
            public void LongGentleBanks()
            {
                foreach(bool far in new[]{false,true})
                {
                    var bank=far?Far:Near;var collider=bank.GetComponent<BoxCollider>();
                    float angle=Mathf.Atan(.09f)*Mathf.Rad2Deg;
                    bank.transform.SetPositionAndRotation(far?Target:Vector3.zero,Quaternion.Euler(far?-angle:angle,0,0));
                    collider.center=new Vector3(0,-.3f,far?2f:-2f);collider.size=new Vector3(160,.6f,18);
                }
                Sync();MovePlayer(new Vector3(0,.05f,0));
            }
            public void FarCorner(float rise)
            {
                Far.transform.SetPositionAndRotation(Target,Quaternion.Euler(0,0,Mathf.Atan2(rise,1.3f)*Mathf.Rad2Deg));
                var collider=Far.GetComponent<BoxCollider>();collider.center=new Vector3(0,-.5f,2.5f);collider.size=new Vector3(160,1,6);Sync();
            }
            public void MovePlayer(Vector3 feet){Body.enabled=false;Body.transform.position=feet;Body.enabled=true;Sync();}
            public void Draw()=>Call(Wiring,"OnLetterDrawn",Letter());
            public void WaterAt(float height)
            {
                var a=new Vector3(-100,height,-100);var b=new Vector3(-100,height,100);var c=new Vector3(100,height,100);var d=new Vector3(100,height,-100);
                Query.Water=new[]{new WorldTerrainQuery.WaterTriangle{A=a,B=b,C=c},new WorldTerrainQuery.WaterTriangle{A=a,B=c,C=d}};Query.Reindex();
            }
            public void Sync(){Physics.SyncTransforms();var p=Scene.GetPhysicsScene();if(!p.IsValid()||p.Equals(Physics.defaultPhysicsScene))throw new InvalidOperationException("Local physics fixture required.");p.Simulate(.001f);}
            public void Dispose()
            {
                if(Health!=null)Health.ApplyFatalFall();if(Scene.IsValid())EditorSceneManager.ClosePreviewScene(Scene);
                UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(Profile);UnityEngine.Object.DestroyImmediate(Config);UnityEngine.Object.DestroyImmediate(Changed);
            }
        }
    }
}
