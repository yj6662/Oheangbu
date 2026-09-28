using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string CombatChecks263(bool generated=false)
  {
   FrontageScene249();var source=VillageSession().Actors.Single(a=>a.Id==WorldMacroPlaytestSession.SinmokId);var report=new List<string>();
   void C(bool pass,string name)=>report.Add((pass?"PASS ":"FAIL ")+name);
   void Call(object obj,string method)=>obj.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(obj,null);
   void Set(object obj,string field,object value)=>obj.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(obj,value);
   foreach(CheongryongAttackKind kind in Enum.GetValues(typeof(CheongryongAttackKind)))foreach(float bearing in new[]{0f,135f}){
    var preview=EditorSceneManager.NewPreviewScene();GameObject clone=null;SinmokRigAnimation rig=null;
    try{
     clone=Object.Instantiate(source.gameObject);SceneManager.MoveGameObjectToScene(clone,preview);clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.SetActive(true);
     var enemy=clone.GetComponent<EnemyVitals>();enemy.Restore();var config=(CombatConfigSO)new SerializedObject(enemy).FindProperty("_config").objectReferenceValue;
     var ground=new GameObject("Isolated floor");SceneManager.MoveGameObjectToScene(ground,preview);ground.transform.position=Vector3.down*.5f;ground.AddComponent<BoxCollider>().size=new Vector3(100,1,100);
     var player=new GameObject("Isolated player").AddComponent<PlayerVitals>();SceneManager.MoveGameObjectToScene(player.gameObject,preview);Set(player,"_config",config);player.Restore();
     player.transform.position=Quaternion.Euler(0,bearing,0)*new Vector3(0,0,kind==CheongryongAttackKind.HeadBite?5:kind==CheongryongAttackKind.TailSweep?7:12);
     float now=100;var combat=clone.GetComponent<CheongryongCombatController>();Set(combat,"_clock",(Func<float>)(()=>now));combat.ConfigureProfile(combat.Profile,config);combat.Configure(player.transform,player,new ParryJudge(config));
     rig=clone.GetComponent<SinmokRigAnimation>();rig.Animator.Rebind();Call(rig,"OnEnable");
     Physics.SyncTransforms();preview.GetPhysicsScene().Simulate(.001f);now+=4;
     string label=kind+" at "+bearing+" degrees";C(combat.TryBeginAttack(kind),label+" starts with actual cloned Sinmok sockets/profile");var plan=combat.CurrentPlan;if(plan==null)continue;
     int hits=0;combat.AttackImpactResolved+=h=>{if(h.AppliedDamage>0)hits++;};float hp=player.Hp01*player.MaxHp;
     rig.Evaluate(now);var initial=rig.TurningTrunk.localRotation;float maxPose=0,turn=0;
     float started=now,end=plan.ActiveEndAt+.02f;int steps=Mathf.CeilToInt((end-started)*60);if(steps<1||steps>600)throw new Exception("Invalid bounded diagnostic duration "+steps);
     for(int step=1;step<=steps;step++){now=Mathf.Min(end,started+step/60f);Call(combat,"Update");rig.Evaluate(now);maxPose=Mathf.Max(maxPose,rig.PoseWeight);turn=Mathf.Max(turn,Quaternion.Angle(initial,rig.TurningTrunk.localRotation));}
     float expectedFacing=Mathf.Atan2(plan.Direction.x,plan.Direction.z)*Mathf.Rad2Deg;
     C(maxPose>.9f&&(generated?rig.FacingVisual!=null&&Mathf.Abs(Mathf.DeltaAngle(rig.FacingVisual.localEulerAngles.y,expectedFacing))<2:turn>1),label+" playable graph follows telegraph/strike clock and locked plan facing");
     C(hits==1&&player.Hp01*player.MaxHp<hp,label+" one real PlayerVitals damage application");
     combat.StopAttack();now+=4;C(combat.TryBeginAttack(kind),label+" can begin next cycle");var cancelled=combat.CurrentPlan;
     enemy.TakeDamage(100000,AttackProvenance.Create(player,DamageSource.PlayerDirect,Element.Metal));now+=3;Call(combat,"Update");rig.Evaluate(now);
     C(cancelled!=null&&cancelled.IsCancelled&&hits==1,label+" death cancels pending damage");
    }catch(Exception e){report.Add("FAIL "+kind+" "+bearing+": "+e);}finally{if(rig!=null)Call(rig,"OnDisable");if(clone!=null)Object.DestroyImmediate(clone);EditorSceneManager.ClosePreviewScene(preview);}
   }
   File.WriteAllLines(generated?"../Art/Characters/Animation273/sinmok-combat-checks.txt":Output263+"/combat-checks.txt",report);return string.Join("\n",report);
  }
 }
}
