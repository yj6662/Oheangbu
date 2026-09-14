using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Oheangbu.Core.Domain;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlaytestAuthoring
    {
        static string SaveTests()
        {
            string folder=Output+"/SaveTests/"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");Directory.CreateDirectory(folder);
            var lines=new List<string>();void Check(bool ok,string label){lines.Add((ok?"PASS ":"FAIL ")+label);}
            var state=new WorldMacroProgress{version=2};var l=state.ledger;
            int reward=AssetDatabase.LoadAssetAtPath<PrologueContentSO>(Prologue.PrologueBuilder.Folder+"/Content.asset").EnemyReward;
            Check(state.Defeat("mine_beast/0",reward)&&!state.Defeat("mine_beast/0",reward)&&l.currency==reward,"persistent encounter reward idempotency");
            Check(PrologueProgressStore.Complete(l,"worker_satchel",24)&&!PrologueProgressStore.Complete(l,"worker_satchel",24),"one-time content reward");
            int cash=l.currency;PrologueProgressStore.Drop(l,Vector3.one);Check(l.currency==0&&l.dropCurrency==cash,"death transfers currency to safe-foot drop");
            Check(PrologueProgressStore.Retrieve(l)==cash&&PrologueProgressStore.Retrieve(l)==0&&l.currency==cash,"retrieve exactly once");
            PrologueProgressStore.Drop(l,Vector3.one);PrologueProgressStore.Drop(l,Vector3.forward);Check(l.dropCurrency==0&&l.completed.Contains("worker_satchel"),"second death replaces previous drop and preserves completion");
            var store=new AtomicJsonStore<WorldMacroProgress>(folder+"/slot.json",WorldMacroProgress.Valid);l.currency=37;store.Save(state);l.currency=41;store.Save(state);
            var loaded=new AtomicJsonStore<WorldMacroProgress>(folder+"/slot.json",WorldMacroProgress.Valid).Load();Check(loaded.ledger.currency==41&&loaded.defeated.Contains("mine_beast/0")&&loaded.ledger.completed.Contains("worker_satchel"),"new repository disk reload");
            File.WriteAllText(folder+"/slot.json","corrupt");Check(store.Load().ledger.currency==37&&store.LoadStatus=="backup","corrupt primary backup recovery");
            l.currency=53;store.Save(state);File.WriteAllText(folder+"/slot.json","corrupt again");Check(store.Load().ledger.currency==37,"save after recovery preserves valid backup");
            state.version=999;File.WriteAllText(folder+"/slot.json",JsonUtility.ToJson(state));Check(store.Load().ledger.currency==37,"unknown version falls back to valid backup");state.version=2;
            File.WriteAllText(folder+"/slot.json","{}");Check(store.Load().ledger.currency==37,"missing schema version rejected");
            l.currency=-1;Check(!WorldMacroProgress.Valid(state),"negative currency rejected");l.currency=0;l.hp=float.NaN;Check(!WorldMacroProgress.Valid(state),"invalid resource rejected");l.hp=1;
            var failure=new AtomicJsonStore<WorldMacroProgress>(folder,WorldMacroProgress.Valid);bool rejected=false;try{failure.Save(state);}catch(IOException){rejected=true;}catch(UnauthorizedAccessException){rejected=true;}Check(rejected,"filesystem failure propagates to caller");
            Check(!File.Exists(Path.Combine(Application.persistentDataPath,"cheongrim-prologue-v1.json.tmp")),"tests do not write prologue slot temporary file");
            string result=string.Join("\n",lines);File.WriteAllText(Output+"/save_tests.txt",result);return result;
        }
        static string Audit()
        {
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();if(session==null)throw new Exception("Playtest session required");
            var lines=new List<string>();void Check(bool ok,string label){lines.Add((ok?"PASS ":"FAIL ")+label);}
            Check(session.PreviewPoints.Length==83,"83 original content identities retained");
            Check(session.Actors.Length==3,"only three first-section combat enemies");
            Check(session.Content.Encounters.Select(e=>e.Id).Distinct().Count()==session.Actors.Length,"unique persistent encounter IDs");
            Check(Object.FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None).Count(m=>m.enabled)==1&&!Object.FindFirstObjectByType<WorldMacroReviewController>().enabled,"one walking input owner");
            Check(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c=>c.enabled&&c.CompareTag("MainCamera"))==1,"one active main camera");
            Check(Mathf.Abs(session.Walker.Body.height-1.75f)<.001f&&Mathf.Abs(session.Walker.Body.radius-.28f)<.001f&&session.Walker.Body.center==Vector3.up*.875f,"foot-root capsule 1.75m / 0.56m");
            Physics.SyncTransforms();int samples=0,bad=0;float maxSlope=0;var issues=new List<string>();
            foreach(var path in new[]{session.Content.MainPath,session.Content.BranchPath})for(int i=1;i<path.Length;i++){
                int count=Mathf.CeilToInt(Vector3.Distance(path[i-1],path[i])/.5f);Vector3 prior=path[i-1];
                for(int n=0;n<=count;n++){
                    Vector3 expected=Vector3.Lerp(path[i-1],path[i],n/(float)Mathf.Max(1,count));samples++;
                    // Probe from locally expected height, so cave roof and inn roof cannot masquerade as the walking floor.
                    var hits=Physics.RaycastAll(expected+Vector3.up*.8f,Vector3.down,3,1,QueryTriggerInteraction.Ignore).Where(h=>!h.transform.IsChildOf(session.Walker.Body.transform)).OrderBy(h=>h.distance).ToArray();
                    bool good=hits.Length>0;Vector3 feet=expected;
                    if(good){var h=hits[0];maxSlope=Mathf.Max(maxSlope,Vector3.Angle(h.normal,Vector3.up));good=h.normal.y>=Mathf.Cos(45*Mathf.Deg2Rad);feet=h.point+Vector3.up*.12f;
                        good&=!Physics.OverlapCapsule(feet+Vector3.up*.28f,feet+Vector3.up*1.47f,.28f,1,QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(session.Walker.Body.transform));
                        foreach(var delta in new[]{Vector3.right*.28f,Vector3.left*.28f,Vector3.forward*.28f,Vector3.back*.28f})good&=Physics.Raycast(feet+delta+Vector3.up*.3f,Vector3.down,.8f,1,QueryTriggerInteraction.Ignore);
                        if(n>0)good&=Mathf.Abs(feet.y-prior.y)<.3f+Vector3.Distance(new Vector3(feet.x,0,feet.z),new Vector3(prior.x,0,prior.z));
                    }
                    if(!good){bad++;if(issues.Count<100)issues.Add("segment="+i+" position="+expected+" support="+(hits.Length>0?hits[0].collider.name:"none"));}prior=feet;
                }
            }
            Check(bad==0,"route support / width / slope / headroom: "+bad+" failed of "+samples+" at <=0.5m stations; max slope="+maxSlope);
            foreach(var actor in session.Actors){var navPath=new NavMeshPath();var spec=session.Content.Encounters.First(e=>e.Id==actor.Id);bool on=NavMesh.SamplePosition(spec.Feet,out var hit,2,NavMesh.AllAreas);Check(on,"local NavMesh at "+actor.Id);if(on)Check(NavMesh.CalculatePath(hit.position,spec.Patrol.Last(),NavMesh.AllAreas,navPath)&&navPath.status==NavMeshPathStatus.PathComplete,"patrol route "+actor.Id);}
            lines.Add("UNVERIFIED real key/pointer combat, user route completion and visual acceptance");lines.AddRange(issues);string result=string.Join("\n",lines);File.WriteAllText(Output+"/static_audit.txt",result);return result;
        }
        static string Play(string suffix)
        {
            if(EditorApplication.isPlaying)throw new Exception("Already in Play");
            var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();s.TestSaveSuffix=suffix;
            EditorApplication.isPlaying=true;return "Starting playtest with isolated suffix "+suffix;
        }
        static string Runtime(string argument)
        {
            if(!EditorApplication.isPlaying)throw new Exception("Play mode required");var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();if(s.Progress==null)throw new Exception("Session not ready");
            if(argument.StartsWith("visit:")){
                string id=argument.Substring(6);var p=s.Content.Points.First(x=>x.Id==id);Vector3 candidate=p.Position-Vector3.forward*1.8f;
                if(!s.TrySafeFeet(candidate,out var feet))throw new Exception("No safe interaction stand "+id);s.Teleport(feet,0);return "At bounded interaction station "+id;
            }
            if(argument=="state")return JsonUtility.ToJson(s.Progress,true)+"\nfeet="+s.Walker.Body.transform.position+" safe="+s.LastSafeFeet+" saved="+s.SaveError+" load="+s.LoadStatus+" focused="+s.FocusedId+" drawing="+s.Walker.Motor.IsDrawing+" hp="+s.Walker.Body.GetComponent<PlayerVitals>().Hp01+" actors="+string.Join(",",s.Actors.Select(a=>a.Id+":"+a.Current+":"+a.GetComponent<EnemyVitals>().Hp01));
            if(argument=="checks")return RuntimeChecks(s);
            if(argument=="vehicle-station")return VehicleStation(s);
            if(argument=="vehicle-checks")return VehicleChecks(s);
            if(argument=="reload-check"){
                bool ok=s.LoadStatus=="primary"&&s.Progress.ledger.checkpoint=="geumpyo_inn"&&s.Progress.ledger.completed.Contains("worker_satchel")&&s.Progress.ledger.dropCurrency==0;
                string text=(ok?"PASS ":"FAIL ")+"new Play session reloads disk checkpoint / one-time reward / replaced drop; load="+s.LoadStatus;File.WriteAllText(Output+"/reload_check.txt",text);return text;
            }
            if(argument=="recovery-check"){
                bool ok=s.LoadStatus=="primary"&&s.Progress.ledger.checkpoint=="mine_start"&&Vector3.Distance(s.Walker.Body.transform.position,s.Content.StartFeet)<.3f&&s.Progress.ledger.currency==73&&s.Progress.ledger.dropCurrency==27&&s.Progress.ledger.completed.Contains("removed_content_tombstone")&&s.TrySafeFeet(s.Progress.ledger.dropPosition,out _);
                string result=(ok?"PASS ":"FAIL ")+"new session repairs missing checkpoint ID, stale terrain, invalid player/drop positions and preserves reward tombstones/currency";File.WriteAllText(Output+"/recovery_check.txt",result);return result;
            }
            if(argument=="seated-save"){VehicleStation(s);var seat=Object.FindFirstObjectByType<WorldMacroPalanquinSeat>();if(!seat.TryBoard()||!s.Save())throw new Exception("Seated save failed");return "Saved while occupied; stop and reload this audit slot";}
            if(argument=="seated-reload-check"){
                bool ok=s.LoadStatus=="primary"&&!s.Walker.Seated&&s.Walker.Motor.enabled&&s.Walker.Drawing.enabled&&s.TrySafeFeet(s.Walker.Body.transform.position,out _);
                string result=(ok?"PASS ":"FAIL ")+"scene exit while seated reloads safe walking position with combat input enabled";File.WriteAllText(Output+"/seated_reload_check.txt",result);return result;
            }
            if(argument=="los-check"){
                var a=s.Actors[0];var eye=a.transform.position+Vector3.up*.4f;
                s.Teleport(a.transform.position+Vector3.forward*4-Vector3.up*.8f,180);Physics.SyncTransforms();var ec=a.GetComponent<EnemyController>();bool clear=ec.HasLineOfSight();
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Temporary_Combat_Occlusion";wall.transform.position=(a.transform.position+s.Walker.Body.transform.position)*.5f+Vector3.up*.8f;wall.transform.localScale=new Vector3(4,4,.3f);Physics.SyncTransforms();bool blocked=!ec.HasLineOfSight();Object.DestroyImmediate(wall);Physics.SyncTransforms();
                string result=(clear&&blocked?"PASS ":"FAIL ")+"EnemyController physical wall occlusion clear="+clear+" blocked="+blocked;File.WriteAllText(Output+"/combat_occlusion.txt",result);return result;
            }
            if(argument=="combat-off"){s.CombatActive=false;s.Cull();return "Combat disabled for comparison";}
            if(argument=="combat-on"){s.CombatActive=true;s.Cull();return "Combat enabled";}
            if(argument.StartsWith("interact:"))return "interacted="+s.Interact(argument.Substring(9));
            throw new Exception("Unknown runtime check "+argument);
        }
        static string RuntimeChecks(WorldMacroPlaytestSession s)
        {
            if(string.IsNullOrEmpty(s.TestSaveSuffix))throw new Exception("Isolated audit suffix required");
            var lines=new List<string>();void Check(bool ok,string label){lines.Add((ok?"PASS ":"FAIL ")+label);}
            Check(s.Save(),"runtime save succeeds");Check(!s.CanInteract("missing_id"),"missing interaction rejected");
            Check(!s.TrySafeFeet(new Vector3(float.NaN,0,0),out _),"nonfinite restore location rejected");Check(!s.TrySafeFeet(new Vector3(999999,0,999999),out _),"out-of-world location rejected");
            var actor=s.Actors[0];int currency=s.Progress.ledger.currency;actor.GetComponent<EnemyVitals>().TakeDamage(float.MaxValue);s.EnemyDefeated(actor.Id);
            Check(s.Progress.ledger.currency==currency+s.Content.TestRules.EnemyReward,"death and repeated defeat callback pay once");
            actor.gameObject.SetActive(false);actor.gameObject.SetActive(true);s.Cull();Check(!actor.GetComponent<EnemyVitals>().IsAlive&&s.Progress.defeated.Contains(actor.Id),"activation cycle preserves defeated state");
            s.CombatActive=false;s.Cull();s.CombatActive=true;s.Cull();Check(!actor.GetComponent<EnemyVitals>().IsAlive,"distance/AI visibility cycle does not revive enemy");
            var satchel=s.Content.Points.First(p=>p.Id=="worker_satchel");if(!s.TrySafeFeet(satchel.Position-Vector3.forward*1.8f,out var foot))throw new Exception("No branch stand");s.Teleport(foot,0);currency=s.Progress.ledger.currency;
            Check(s.Interact(satchel.Id)&&s.Interact(satchel.Id)&&s.Progress.ledger.currency==currency+satchel.Currency,"repeat branch interaction rewards once");
            // A temporary opaque obstacle exercises the actual session visibility guard.
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Temporary_Interaction_Occluder";wall.transform.position=(foot+satchel.Position)*.5f+Vector3.up;wall.transform.localScale=new Vector3(2,3,.15f);Physics.SyncTransforms();Check(!s.CanInteract(satchel.Id),"wall blocks interaction");Object.DestroyImmediate(wall);Physics.SyncTransforms();
            currency=s.Progress.ledger.currency;s.Walker.Body.GetComponent<PlayerVitals>().ApplyFatalFall();Check(s.Progress.ledger.currency==0&&s.Progress.ledger.dropCurrency==currency&&s.Progress.ledger.completed.Contains(satchel.Id),"death drop and completed records survive respawn");
            Check(s.Actors.All(a=>a.GetComponent<EnemyVitals>().IsAlive),"death resets regular enemies");
            s.Teleport(s.Progress.ledger.dropPosition,0);Check(s.Interact("CurrencyDrop")&&s.Progress.ledger.currency==currency&&s.Progress.ledger.dropCurrency==0,"runtime drop retrieval");
            s.Walker.Body.GetComponent<PlayerVitals>().ApplyFatalFall();s.Walker.Body.GetComponent<PlayerVitals>().ApplyFatalFall();Check(s.Progress.ledger.dropCurrency==0&&s.Progress.ledger.completed.Contains(satchel.Id),"runtime second death replaces prior drop");
            var rest=s.Content.Points.First(p=>p.Id=="geumpyo_inn");s.Teleport(s.Content.InnCheckpointFeet,0);Check(s.Interact(rest.Id)&&s.Progress.ledger.checkpoint==rest.Id,"inn rest registers checkpoint");
            Check(s.Save(),"final state saved for next-session reload");
            lines.Add("UNVERIFIED real keys/pointer, app process restart, user completion");string result=string.Join("\n",lines);File.WriteAllText(Output+"/runtime_checks.txt",result);return result;
        }
        static string VehicleStation(WorldMacroPlaytestSession s)
        {
            var seat=Object.FindFirstObjectByType<WorldMacroPalanquinSeat>();
            if(seat.Occupied)return "Already occupied";
            var candidates=new List<Vector3>();if(seat.ExitSockets!=null)candidates.AddRange(seat.ExitSockets.Where(t=>t!=null).Select(t=>t.position));
            for(float r=2;r<=7;r+=.5f)foreach(var side in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})candidates.Add(seat.Vehicle.transform.position+seat.Vehicle.transform.TransformDirection(side)*r);
            foreach(var p in candidates)if(s.TrySafeFeet(p,out var feet)){s.Teleport(feet,seat.Vehicle.transform.eulerAngles.y);Physics.SyncTransforms();if(seat.CanBoard)return "At safe vehicle boarding station; E boards, V changes view";}
            throw new Exception("No safe boardable vehicle station found");
        }
        static string VehicleChecks(WorldMacroPlaytestSession s)
        {
            if(string.IsNullOrEmpty(s.TestSaveSuffix))throw new Exception("Audit suffix required");VehicleStation(s);var seat=Object.FindFirstObjectByType<WorldMacroPalanquinSeat>();var lines=new List<string>();
            void Check(bool ok,string label){lines.Add((ok?"PASS ":"FAIL ")+label);}
            Check(seat.TryBoard(),"existing seat boards via macro combat adapter");
            Check(s.Walker.Seated&&!s.Walker.Motor.enabled&&!s.Walker.Drawing.enabled&&!s.Walker.CameraRig.enabled&&!s.Walker.Body.enabled,"seated input / camera ownership");
            bool view=seat.SeatedView;Check(seat.ToggleView()&&seat.SeatedView!=view,"vehicle V-equivalent camera switch");
            Check(!s.CanInteract("geumpyo_inn"),"F interaction blocked while seated");Check(s.Save(),"seated session saves safe walking position only");
            Check(seat.TryExit(),"safe vehicle exit found");Check(!s.Walker.Seated&&s.Walker.Motor.enabled&&s.Walker.Drawing.enabled&&s.Walker.CameraRig.enabled&&s.Walker.Body.enabled,"combat controls restored after exit");
            Check(s.TrySafeFeet(s.Walker.Body.transform.position,out var foot)&&Mathf.Abs(foot.y-s.Walker.Body.transform.position.y)<.25f,"feet restored onto supported ground");
            string result=string.Join("\n",lines);File.WriteAllText(Output+"/vehicle_checks.txt",result);return result;
        }
        static string SeedRecovery()
        {
            if(EditorApplication.isPlaying)throw new Exception("Edit mode required");var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            var state=new WorldMacroProgress{version=2,terrainRevision="old_terrain",ledger=new PrologueProgress{checkpoint="removed_checkpoint",hasPosition=true,position=new Vector3(float.NaN,0,0),dropPosition=new Vector3(99999,-99999,0),currency=73,dropCurrency=27}};
            state.ledger.completed.Add("removed_content_tombstone");state.defeated.Add("removed_encounter");
            string path=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+"_recovery_20260913.json");new AtomicJsonStore<WorldMacroProgress>(path,WorldMacroProgress.Valid).Save(state);return "Prepared isolated recovery fixture";
        }
        static string Regression()
        {
            var config=AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DevSceneKit.DefaultConfigPath);var judge=new ParryJudge(config);var rows=new List<string>();void Check(bool ok,string label){rows.Add((ok?"PASS ":"FAIL ")+label);}
            judge.RaiseGuard(Element.Water,100);Check(judge.ResolveImpact(Element.Fire,100+config.ParryWindow*.5f,Vector3.zero)==ParryOutcome.Success,"water guard counters fire in current TEST parry window");
            Check(judge.ResolveImpact(Element.Fire,100+config.ParryWindow*.6f,Vector3.zero)==ParryOutcome.None,"successful guard consumed");
            judge.RaiseGuard(Element.Wood,100);Check(judge.ResolveImpact(Element.Fire,100,Vector3.zero)==ParryOutcome.Fail,"wood feeds fire: failed parry");
            judge.RaiseGuard(Element.Fire,100);Check(judge.ResolveImpact(Element.Fire,100,Vector3.zero)==ParryOutcome.Half,"same element half result");
            judge.RaiseGuard(Element.Water,100);Check(judge.ResolveImpact(Element.Fire,100+(config.ParryWindow+config.GuardDuration)*.5f,Vector3.zero)==ParryOutcome.Block,"late guard block uses existing duration");
            Check(judge.ResolveImpact(Element.Fire,100+config.GuardDuration+.01f,Vector3.zero)==ParryOutcome.None,"expired guard provides no defence");
            judge.RaiseGuard(Element.Water,100);judge.ClearGuard();Check(judge.ResolveImpact(Element.Fire,100,Vector3.zero)==ParryOutcome.None,"rest/death reset clears guard");
            var ink=new InkPool(config,null);ink.Restore(.1f);Check(!ink.TrySpend(.2f)&&Mathf.Approximately(ink.Value,.1f),"insufficient ink does not spend");ink.Restore();Check(Mathf.Approximately(ink.Value,1),"rest ink restoration");
            rows.Add("UNVERIFIED full recognition -> cast -> hit and incoming attack timing under physical input");string result=string.Join("\n",rows);File.WriteAllText(Output+"/combat_regression.txt",result);return result;
        }
        static string Capture(string argument)
        {
            if(EditorApplication.isPlaying)throw new Exception("Static capture in Edit mode only");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new Exception("Capture stopped at 85% commit");
            var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();Vector3 target,position;
            if(argument=="mine"){var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");target=cave.position-cave.forward*13+Vector3.up*2;position=cave.position-cave.forward*58+cave.right*12+Vector3.up*22;}
            else if(argument=="inn"){target=s.Content.InnCheckpointFeet;position=target+new Vector3(25,20,-30);}
            else if(argument=="branch"){target=s.Content.BranchPath.Last();position=target+new Vector3(12,8,-14);}
            else if(argument=="play"||argument=="inn_play"){
                var config=AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DevSceneKit.DefaultConfigPath);var rotation=Quaternion.Euler(0,argument=="play"?s.Content.StartYaw:0,0);var feet=argument=="play"?s.Content.StartFeet:s.Content.InnCheckpointFeet;
                position=feet+Vector3.up*1.55f+rotation*config.ShoulderOffset;target=position+rotation*Vector3.forward*10;
            }
            else throw new Exception("Unknown capture view");
            return WorldMacroDressingProbe.Capture("Playtest_"+argument,true,position.x,position.y,position.z,target.x,target.y,target.z);
        }
    }
}
