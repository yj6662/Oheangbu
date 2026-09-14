using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroVillageOpeningReview
    {
        public const string RootName="Playtest_Village_Office";
        public const string Folder="Assets/_Project/Art/World/WorldMacro/Playtest/VillageOpening";
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/World/WorldMacro/Playtest/VillageOpening"));
        static WorldMacroPlaytestSession Session=>Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        public static string Execute(string command)
        {
            Directory.CreateDirectory(Output);
            if(command=="survey")return Survey();
            if(command=="apply")return Apply();
            if(command=="finish-placement")return FinishPlacement();
            if(command=="refresh-dressing"){Object.FindFirstObjectByType<Oheangbu.App.World.Dressing.WorldMacroDressingRenderer>().ResetCache();return "Placement cache refreshed for the new courtyard exclusions; no terrain regeneration.";}
            if(command.StartsWith("runtime-shot:"))
            {
                if(!Application.isPlaying||Screen.width!=1920||Screen.height!=1080)throw new InvalidOperationException("Actual 1920x1080 Play GameView required");
                if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Commit >=85%");
                string name=command.Substring(13);if(name.Length==0||name.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'))throw new ArgumentException("Simple capture name required");
                string path=Output+"/"+name+".png";
                if(File.Exists(path))throw new InvalidOperationException("Capture already exists; preserve prior still");
                ScreenCapture.CaptureScreenshot(path,1);
                File.WriteAllText(Output+"/"+name+".txt","Actual GameView screenshot requested; verify file after rendered frames. Page="+Oheangbu.App.World.UI.PlaytestUiRoot.Instance.Page+"; feet="+Session.Walker.Body.transform.position+"; commit="+Prologue.PrologueAudit.CommitRatio());
                return path;
            }
            if(command=="validate")return Validate();
            if(command.StartsWith("capture:"))return Capture(command.Substring(8));
            throw new ArgumentException(command);
        }
        static string Survey()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit survey required.");
            var lines=new List<string>();
            foreach(string n in new[]{"Jibsacheong_1","Jibsacheong_2","Bijangcheong","Naeposa"})
            {
                string path="Assets/HwaseongHaenggung/Prefabs/SM_"+n+".prefab";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(prefab==null){lines.Add("MISSING "+path);continue;}
                var clone=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    clone.hideFlags=HideFlags.HideAndDontSave;
                    clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                    var rr=clone.GetComponentsInChildren<Renderer>();var b=rr[0].bounds;
                    foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);
                    lines.Add("ASSET "+path+" bounds="+b.ToString("F3")+" colliders="+clone.GetComponentsInChildren<Collider>().Length);
                }
                finally{Object.DestroyImmediate(clone);}
            }
            Physics.SyncTransforms();
            foreach(var center in new[]{new Vector3(1900,133,630),new Vector3(1880,134,600),new Vector3(1935,133,590),new Vector3(1850,133,600)})
            {
                var heights=new List<float>();string ground="";
                for(int x=-16;x<=16;x+=8)for(int z=-12;z<=12;z+=6)
                {
                    var p=center+new Vector3(x,20,z);
                    if(Physics.Raycast(p,Vector3.down,out var hit,50,1,QueryTriggerInteraction.Ignore)){heights.Add(hit.point.y);ground=hit.collider.name;}
                }
                lines.Add("SITE "+center.ToString("F2")+" samples="+heights.Count+" min="+heights.DefaultIfEmpty().Min()+" max="+heights.DefaultIfEmpty().Max()+" ground="+ground);
            }
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t=>t.GetComponent<Renderer>()!=null && Vector3.Distance(t.position,new Vector3(1900,133,630))<100).Take(60))
                lines.Add("NEAR "+t.name+" "+t.position.ToString("F2"));
            var route=Session.Content.MainPath;
            lines.Add("ACTUAL_PATH samples="+route.Length+" first="+route[0]+" last="+route.Last());
            File.WriteAllLines(Output+"/site_survey.txt",lines);
            return string.Join("\n",lines);
        }
    }
}
