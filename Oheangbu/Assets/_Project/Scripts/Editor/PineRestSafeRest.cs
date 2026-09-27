using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static partial class PineRestGameBuilder
    {
        static string SafeRest()
        {
            if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)
                throw new Exception("Journey edit required");
            var content=AssetDatabase.LoadAssetAtPath<PrologueContentSO>(Folder+"/Content.asset");
            content.RestRequiresSafety=true;
            EditorUtility.SetDirty(content);
            AssetDatabase.SaveAssets();
            return "Journey rest now requires disengagement from live pursuers";
        }

        static Keyboard restKeyboard, priorRestKeyboard;
        static PrologueSession restSession;
        static PrologueContentSO.Point restPoint;
        static Vector3 originalRestPosition;
        static string originalCheckpoint;
        static double restNext,restDeadline;
        static int restPhase,restResults;
        static readonly List<string> restChecks=new List<string>();
        const string SafeRestReport="../Art/World/PineRest/safe_rest_input.txt";

        static string SafeRestInput()
        {
            restSession=RoadRestSession();
            if(restKeyboard!=null||!restSession.Content.RestRequiresSafety)throw new Exception("Idle audit with safe rest enabled required");
            restPoint=restSession.Content.Points.First(p=>p.Kind==PrologueInteractionKind.Rest);
            originalRestPosition=restPoint.Position;
            originalCheckpoint=restSession.Progress.checkpoint;
            priorRestKeyboard=Keyboard.current;
            restKeyboard=InputSystem.AddDevice<Keyboard>("JourneySafeRestAudit");
            restPhase=0;restChecks.Clear();restNext=EditorApplication.timeSinceStartup;restDeadline=restNext+20;
            EditorApplication.update+=SafeRestTick;
            File.WriteAllText(SafeRestReport,"RUNNING");
            return "Safe rest real F input audit started; temporary rest location/HP fixtures";
        }

        static void SafeRestTick()
        {
            try
            {
                if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup>restDeadline)throw new Exception("Stopped or timed out at phase "+restPhase);
                if(EditorApplication.timeSinceStartup<restNext)return;
                restNext=EditorApplication.timeSinceStartup+.25;
                var ui=Object.FindFirstObjectByType<PrologueInteraction>();
                var health=restSession.Player.GetComponent<PlayerVitals>();
                bool chased=restSession.Encounters.Any(a=>a!=null&&a.isActiveAndEnabled&&a.Current==PrologueEncounter.Behaviour.Chase&&a.GetComponent<EnemyVitals>().IsAlive);
                void Check(bool value,string label){restChecks.Add((value?"PASS ":"FAIL ")+label);}
                switch(restPhase)
                {
                    case 0:
                        var enemy=restSession.Encounters.First(a=>a.isActiveAndEnabled&&a.GetComponent<EnemyVitals>().IsAlive);
                        restSession.Teleport(enemy.transform.position-Vector3.forward*8,0);
                        health.Restore(.6f);
                        restPoint.Position=restSession.Player.position-Vector3.up;
                        restPhase++;
                        break;
                    case 1:
                        if(!chased)return;
                        Check(ui.FocusedId==restPoint.Id,"temporary rest target acquired during actual AI chase");
                        restResults=ui.ResultCount;
                        InputSystem.QueueStateEvent(restKeyboard,new KeyboardState(Key.F));
                        restPhase++;
                        break;
                    case 2:
                        InputSystem.QueueStateEvent(restKeyboard,new KeyboardState());
                        Check(ui.ResultCount>restResults&&ui.LastResult==PrologueSession.InteractionResult.Waiting,"F refuses rest while chased");
                        Check(health.Hp01<=.6001f&&health.Hp01>0,"blocked rest does not heal");
                        Check(restSession.Progress.checkpoint==originalCheckpoint,"blocked rest preserves checkpoint");
                        Check(chased,"blocked rest does not reset pursuer");
                        restPoint.Position=originalRestPosition;
                        restSession.Teleport(originalRestPosition+Vector3.up,0);
                        restPhase++;
                        break;
                    case 3:
                        if(chased)return;
                        Check(ui.FocusedId==restPoint.Id,"ordinary rest target acquired after disengagement");
                        restResults=ui.ResultCount;
                        InputSystem.QueueStateEvent(restKeyboard,new KeyboardState(Key.F));
                        restPhase++;
                        break;
                    case 4:
                        InputSystem.QueueStateEvent(restKeyboard,new KeyboardState());
                        Check(ui.ResultCount>restResults&&ui.LastResult==PrologueSession.InteractionResult.Success,"F rests after disengagement");
                        Check(health.Hp01==1&&restSession.Progress.checkpoint==restPoint.Id,"safe rest restores health and checkpoint");
                        var loaded=new PrologueProgressStore(Path.Combine(Application.persistentDataPath,restSession.Content.SaveSlot+restSession.TestSaveSuffix+".json")).Load();
                        Check(loaded.checkpoint==restPoint.Id&&loaded.hp==1,"safe rest persisted to disk");
                        FinishSafeRest(null);
                        break;
                }
            }
            catch(Exception e){FinishSafeRest(e.ToString());}
        }

        static void FinishSafeRest(string error)
        {
            EditorApplication.update-=SafeRestTick;
            if(restPoint!=null)restPoint.Position=originalRestPosition;
            if(restKeyboard!=null){InputSystem.RemoveDevice(restKeyboard);restKeyboard=null;}
            if(priorRestKeyboard!=null&&priorRestKeyboard.added)priorRestKeyboard.MakeCurrent();
            if(error!=null)restChecks.Add("FAIL "+error);
            restChecks.Add("Actual AI chase and virtual F input. Teleport, HP and temporary rest position are test setup, not continuous combat playthrough. Rest position restored.");
            File.WriteAllLines(SafeRestReport,restChecks);
        }
    }
}
