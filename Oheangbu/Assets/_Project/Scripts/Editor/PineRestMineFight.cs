using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  const string MineFightReport="../Art/World/PineRest/mine_fight.txt";
  static PrologueSession fightSession;
  static PrologueEncounter fightActor;
  static EnemyVitals fightEnemy;
  static PlayerVitals fightPlayer;
  static Keyboard fightKeyboard,oldFightKeyboard;
  static Mouse fightMouse,oldFightMouse;
  static Key[] fightKeys=Array.Empty<Key>();
  static bool fightHold,fightDead,fightCast,dodgedAttack;
  static int fightPhase,fightCasts,fightDodges,fightMoney;
  static float fightDirect,fightHarvest,fightReceived;
  static double fightStart,fightNext;
  static readonly List<string> fightLog=new List<string>();
  static void FightLog(string text)=>fightLog.Add((EditorApplication.timeSinceStartup-fightStart).ToString("F2")+" "+text);
  static void FightDamage(EnemyDamageResult hit){if(hit.Attack.Source==DamageSource.PlayerDirect){fightDirect+=hit.AppliedDamage;FightLog("spell damage="+hit.AppliedDamage);}else if(hit.Attack.Source==DamageSource.Harvest)fightHarvest+=hit.AppliedDamage;}
  static void FightHurt(float amount){fightReceived+=amount;FightLog("player hit="+amount);}
  static void FightDied(){fightDead=true;}
  static string MineFight(){
   var s=RoadRestSession();if(fightSession!=null)throw new Exception("Fight already running");
   fightActor=s.Encounters[0];fightEnemy=fightActor.GetComponent<EnemyVitals>();if(!fightEnemy.IsAlive||s.Progress.defeated.Contains(fightActor.Id))throw new Exception("Fresh living mine enemy required");
   s.Teleport(fightActor.transform.position-Vector3.forward*8,0);
   fightSession=s;fightPlayer=s.Player.GetComponent<PlayerVitals>();fightMoney=s.Progress.currency;
   fightPhase=fightCasts=fightDodges=0;fightDirect=fightHarvest=fightReceived=0;fightDead=fightCast=dodgedAttack=false;fightStart=EditorApplication.timeSinceStartup;fightNext=fightStart+1;
   fightLog.Clear();FightLog("Initial approach fixture only; subsequent movement/damage from input. Enemy hp="+fightEnemy.Hp);
   fightEnemy.DamageResolved+=FightDamage;fightPlayer.Damaged+=FightHurt;fightPlayer.Died+=FightDied;EditorApplication.update+=MineFightTick;
   File.WriteAllText(MineFightReport,"RUNNING");return "Mine fight real input rehearsal started";
  }
  static void MineFightTick(){
   try{
    if(!EditorApplication.isPlaying||fightDead)throw new Exception("Player died or Play stopped");
    if(EditorApplication.timeSinceStartup-fightStart>75)throw new Exception("Fight timeout");
    if(EditorApplication.timeSinceStartup<fightNext)return;
    if(fightCast){var report=Vfx120PlayerInputAudit.Poll();if(!report.Contains("\"FINISHED\""))return;File.WriteAllText("../Art/World/PineRest/mine_fight_cast_"+fightCasts+".json",report);fightCast=false;FightLog("cast finished; enemy hp="+fightEnemy.Hp);}
    if(!fightEnemy.IsAlive){
     var disk=new PrologueProgressStore(Path.Combine(Application.persistentDataPath,fightSession.Content.SaveSlot+fightSession.TestSaveSuffix+".json")).Load();
     bool stored=disk!=null&&disk.defeated.Contains(fightActor.Id)&&disk.currency==fightMoney+fightSession.Content.EnemyReward;
     if(!stored)throw new Exception("Death or reward not persisted");
     FinishMineFight(fightDirect>0&&fightHarvest>0?null:"Missing spell/harvest evidence");return;
    }
    if(fightPhase==0){var target=fightSession.Player.GetComponent<LockOn>();if(target.IsLocked)target.Toggle();target.Toggle();if(target.Target!=fightEnemy)throw new Exception("Mine target not selected");fightPhase=1;StartFightCast();return;}
    if(fightEnemy.Hp<=1.1f){
     if(fightCasts>=4)throw new Exception("Four casts failed to finish fight");
     var controller=fightEnemy.GetComponent<EnemyController>();if(!controller.IsRecovering)return;
     ReleaseFightInput();StartFightCast();return;
    }
    if(fightKeyboard==null){oldFightKeyboard=Keyboard.current;oldFightMouse=Mouse.current;fightKeyboard=InputSystem.AddDevice<Keyboard>("MineFightKeyboard");fightMouse=InputSystem.AddDevice<Mouse>("MineFightMouse");fightKeyboard.MakeCurrent();fightMouse.MakeCurrent();InputSystem.onBeforeUpdate+=FightFeed;FightLog("harvest input begins");}
    var enemy=fightEnemy.GetComponent<EnemyController>();
    if(!enemy.IsTelegraphing)dodgedAttack=false;
    bool dodge=enemy.IsTelegraphing&&enemy.TelegraphProgress>.72f&&!dodgedAttack;
    fightKeys=dodge?new[]{fightDodges%2==0?Key.A:Key.D,Key.LeftShift}:Array.Empty<Key>();
    if(dodge){dodgedAttack=true;fightDodges++;FightLog("lateral dodge input");}
    fightHold=!dodge&&Time.frameCount%30!=0;
   }catch(Exception e){FinishMineFight(e.Message);}
  }
  static void StartFightCast(){fightCasts++;string result=Vfx120PlayerInputAudit.StartJourneyAttack();if(result.StartsWith("BLOCKED"))throw new Exception(result);fightCast=true;FightLog("registered ah glyph input #"+fightCasts);}
  static void FightFeed(){if(fightKeyboard!=null)InputSystem.QueueStateEvent(fightKeyboard,new KeyboardState(fightKeys));if(fightMouse!=null)InputSystem.QueueStateEvent(fightMouse,new MouseState{position=new Vector2(Screen.width/2f,Screen.height/2f),buttons=(ushort)(fightHold?1:0)});}
  static void ReleaseFightInput(){InputSystem.onBeforeUpdate-=FightFeed;if(fightKeyboard!=null)InputSystem.RemoveDevice(fightKeyboard);if(fightMouse!=null)InputSystem.RemoveDevice(fightMouse);fightKeyboard=null;fightMouse=null;fightKeys=Array.Empty<Key>();fightHold=false;if(oldFightKeyboard!=null&&oldFightKeyboard.added)oldFightKeyboard.MakeCurrent();if(oldFightMouse!=null&&oldFightMouse.added)oldFightMouse.MakeCurrent();}
  static void FinishMineFight(string error){
   EditorApplication.update-=MineFightTick;ReleaseFightInput();if(fightCast)Vfx120PlayerInputAudit.Cancel();
   if(fightEnemy!=null)fightEnemy.DamageResolved-=FightDamage;if(fightPlayer!=null){fightPlayer.Damaged-=FightHurt;fightPlayer.Died-=FightDied;}
   FightLog((error==null?"PASS":"FAIL "+error)+"; casts="+fightCasts+" dodges="+fightDodges+" direct="+fightDirect+" harvest="+fightHarvest+" playerDamage="+fightReceived+" enemyHp="+fightEnemy.Hp+" playerHp="+fightPlayer.Hp01);
   fightLog.Add("Registered stroke replay, initial position/lock fixture; no manual handwriting, direct damage, AI disable, health/ink refill, or post-start teleport. Not boss/full-game completion.");File.WriteAllLines(MineFightReport,fightLog);fightSession=null;
  }
 }
}
