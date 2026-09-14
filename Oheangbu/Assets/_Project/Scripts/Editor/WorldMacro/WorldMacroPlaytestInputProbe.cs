using System;
using System.IO;
using Oheangbu.App.World;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Passive observer only. Native key/pointer events are sent separately by Computer Use.
    public static class WorldMacroPlaytestInputProbe
    {
        [Serializable] class Evidence {public int drawEntries,drawExits,commits,successfulCommits,dodgeStarts,fKeys,eKeys,vKeys;public bool drawingBlocksF;public Vector3 from,to;public bool shoulderRestored;public string scope="Passive gameplay observer; native UI input sent separately. This does not certify user completion.";}
        static WorldMacroPlaytestSession session;static Evidence evidence;static bool dashing;static int frame;
        public static string Begin(){if(!EditorApplication.isPlaying)throw new Exception("Play required");if(session!=null)throw new Exception("Input observer already running");session=UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestSession>();evidence=new Evidence{from=session.Walker.Body.transform.position};session.Walker.Drawing.ModeEntered+=Enter;session.Walker.Drawing.ModeExited+=Exit;session.Walker.Drawing.Committed+=Commit;EditorApplication.update+=Tick;return "Passive native-input observation started";}
        static void Enter(){evidence.drawEntries++;evidence.drawingBlocksF=!session.CanInteract("geumpyo_inn");}
        static void Exit(){evidence.drawExits++;}
        static void Commit(bool ok){evidence.commits++;if(ok)evidence.successfulCommits++;}
        static void Tick(){if(session==null||!EditorApplication.isPlaying){EditorApplication.update-=Tick;return;}if(frame==Time.frameCount)return;frame=Time.frameCount;var dodge=session.Walker.Motor.GetComponent<DodgeAction>();if(dodge.IsDashing&&!dashing)evidence.dodgeStarts++;dashing=dodge.IsDashing;var k=Keyboard.current;if(k==null)return;if(k.fKey.wasPressedThisFrame)evidence.fKeys++;if(k.eKey.wasPressedThisFrame)evidence.eKeys++;if(k.vKey.wasPressedThisFrame)evidence.vKeys++;}
        public static string Result(){if(session==null||evidence==null)throw new Exception("Input observer required");evidence.to=session.Walker.Body.transform.position;evidence.shoulderRestored=session.Walker.CameraRig.IsShoulder&&!session.Walker.CameraRig.IsDrawingCloseup;string result=JsonUtility.ToJson(evidence,true);File.WriteAllText(WorldMacroPlaytestAuthoring.Output+"/native_input.json",result);return result;}
    }
}
