using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>Opt-in standalone smoke for real title/play scene transitions and persisted UI progress.</summary>
    public sealed class PlaytestUiBuildSmoke : MonoBehaviour
    {
        const float MaximumSeconds=120f;
        const string ReportArgument="--ui-smoke=";
        static bool started;

        [Serializable] sealed class SmokeCheck
        {
            public string id,status,detail;
            public float seconds;
        }
        [Serializable] sealed class SmokeReport
        {
            public string runner="PlaytestUiBuildSmoke",outcome="RUNNING",startedUtc,finishedUtc;
            public string reportPath,diagnosticSuffix,initialScene,finalScene;
            public float durationSeconds;
            public int passed,failed,unverified;
            public List<SmokeCheck> checks=new List<SmokeCheck>();
        }

        SmokeReport report;
        string reportPath;
        float began,deadline;
        bool finished,waitResult;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if(started||Application.isEditor)return;
            string path=ArgumentValue(ReportArgument);
            string suffix=PlaytestUiRoot.DiagnosticSuffix;
            if(string.IsNullOrWhiteSpace(path)||string.IsNullOrEmpty(suffix))return;
            try{if(!Path.IsPathRooted(path))return;path=Path.GetFullPath(path);}
            catch(Exception e)when(e is ArgumentException||e is NotSupportedException||e is PathTooLongException){Debug.LogError("[UI Smoke] Invalid report path: "+e.Message);return;}
            started=true;
            var go=new GameObject("PlaytestUiBuildSmoke");DontDestroyOnLoad(go);
            go.AddComponent<PlaytestUiBuildSmoke>().Begin(path,suffix);
        }

        static string ArgumentValue(string prefix)
        {
            foreach(string arg in Environment.GetCommandLineArgs())
                if(arg.StartsWith(prefix,StringComparison.Ordinal))return arg.Substring(prefix.Length);
            return null;
        }

        void Begin(string path,string suffix)
        {
            reportPath=path;began=Time.realtimeSinceStartup;deadline=began+MaximumSeconds;
            report=new SmokeReport{reportPath=path,diagnosticSuffix=suffix,startedUtc=DateTime.UtcNow.ToString("O"),initialScene=SceneManager.GetActiveScene().name};
            StartCoroutine(Protect(Run()));
        }

        void Update()
        {
            if(!finished&&report!=null&&Time.realtimeSinceStartup>=deadline)
            {Add("time-limit",false,true,"Exceeded the 120 second standalone smoke limit.");Finish();}
        }

        IEnumerator Protect(IEnumerator routine)
        {
            while(!finished)
            {
                object current;
                try
                {
                    if(!routine.MoveNext())break;
                    current=routine.Current;
                }
                catch(Exception e)
                {
                    Add("unhandled-exception",false,true,e.GetType().Name+": "+e.Message);Finish();yield break;
                }
                yield return current;
            }
            if(!finished)Finish();
        }

        IEnumerator Run()
        {
            yield return WaitFor(()=>PlaytestUiRoot.Instance!=null&&PlaytestUiRoot.Instance.IsTitle&&FindButton("NewGame")!=null,12f);
            if(!Critical("title-ready",waitResult,"Title scene, UI root, and NewGame button are ready."))yield break;

            if(!InvokeButton("NewGame","uGUI Button.onClick invoked on title NewGame."))yield break;
            yield return null;
            var accept=FindButton("Accept");
            if(accept!=null&&!InvokeButton("Accept","uGUI Button.onClick accepted diagnostic-slot archival before NewGame."))yield break;

            yield return WaitFor(PlayReady,30f);
            if(!Critical("play-scene-ready",waitResult,"Play scene bound Session, map, and HUD."))yield break;
            var root=PlaytestUiRoot.Instance;var session=root.Session;
            var hud=FindFirstObjectByType<HudController>();
            bool font=root.Theme!=null&&root.Theme.Font!=null&&root.GetComponentsInChildren<Text>(true).Any(t=>t.font==root.Theme.Font);
            Add("korean-font",font,true,"Bundled theme font is assigned to runtime uGUI text.");
            Add("hud",hud!=null&&hud.IsSkinned&&hud.GetComponentsInChildren<Canvas>(true).Any(c=>c.enabled),true,"Skinned HUD and enabled Canvas found in the play scene.");
            bool map=root.Map!=null&&root.Map.MiniRoot!=null&&root.Map.FullRoot!=null;
            Add("map-presenter",map,true,"Runtime map has minimap and unfolded-map roots.");
            if(!font||hud==null||!hud.IsSkinned||!map)yield break;

            session.CombatActive=false;session.Cull();
            Add("collection-input-scope",false,false,"Collection uses diagnostic teleport and temporary gate release in the isolated smoke slot; native F input is not tested by this runner.");
            string[] bundleIds={"start","exit","inn","pass","capital"};
            foreach(string bundleId in bundleIds)
            {
                var pickup=FindObjectsByType<WorldMacroFragmentPickup>(FindObjectsSortMode.None).FirstOrDefault(p=>p.BundleId==bundleId);
                if(pickup==null){Add("collect-"+bundleId,false,false,"No scene-authored pickup component was found.");continue;}
                Vector3 anchor=pickup.InteractionPosition;
                if(!session.TrySafeFeet(anchor,out var safe)||Vector3.Distance(anchor,safe)>.35f)
                {Add("collect-"+bundleId,false,false,"Authored anchor was not confirmed as reachable ground; no collection was injected.");continue;}
                session.Teleport(anchor,session.Walker.Body.transform.eulerAngles.y);
                Physics.SyncTransforms();
                yield return null;
                root.Gate.ReleaseImmediately();
                bool collected=session.TryCollectFragmentBundle(bundleId,out var notice);
                Add("collect-"+bundleId,collected,collected,
                    collected?"Diagnostic Teleport used the exact pickup anchor, then TryCollectFragmentBundle accepted it.":"TryCollectFragmentBundle rejected the authored anchor; recorded as unverified.");
            }

            int expectedItems=session.Progress.ui.items.Count;
            string[] expectedLetters=session.Progress.ui.knownSpellLetters.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            bool completeCollection=expectedItems==WorldMacroCollectionCatalog.AllFragments.Count&&expectedLetters.Length==WorldMacroCollectionCatalog.AllSpells.Count;
            Add("collection-state",completeCollection,completeCollection,"Collected item count="+expectedItems+", known letters="+expectedLetters.Length+"; expected 20/20.");
            if(!Application.isFocused){root.Gate.Block();root.Gate.ReleaseWhenNeutral();}

            root.OpenPage("소지품");
            yield return null;
            bool inventory=root.Page=="소지품"&&Time.timeScale<=.0001f&&root.RuntimeState!=null&&root.RuntimeState.InputBlocked;
            Add("inventory-pause",inventory,true,"Direct diagnostic OpenPage call; inventory is visible and gameplay is paused/blocked.");
            if(!inventory)yield break;
            if(!InvokeButton("Tab_지도","uGUI tab Button.onClick invoked from the inventory folio."))yield break;
            yield return null;
            bool fullMap=root.Page=="지도"&&root.Map!=null&&root.Map.Expanded&&Time.timeScale<=.0001f&&root.RuntimeState.InputBlocked;
            Add("full-map-pause",fullMap,true,"Map tab opened the unfolded map while gameplay remained paused/blocked.");
            if(!fullMap)yield break;

            bool saved=session.SaveNow(out string saveError);
            Add("save-before-title",saved,true,saved?"Session.SaveNow completed for the diagnostic slot.":saveError);
            if(!saved)yield break;
            root.OpenPage("일시정지");
            yield return null;
            if(!InvokeButton("ReturnTitle","uGUI Button.onClick invoked on pause-page ReturnTitle."))yield break;
            yield return null;
            if(!InvokeButton("Accept","uGUI Button.onClick accepted ReturnTitle confirmation."))yield break;

            yield return WaitFor(()=>PlaytestUiRoot.Instance!=null&&PlaytestUiRoot.Instance.IsTitle&&FindButton("Continue")!=null,30f);
            if(!Critical("returned-title",waitResult,"ReturnTitle completed and rebuilt title uGUI."))yield break;
            root=PlaytestUiRoot.Instance;
            var slot=WorldMacroSaveSlot.Inspect(Application.persistentDataPath,root.ActiveSlotName);
            bool disk=slot.Status==WorldMacroSaveSlotStatus.Primary&&slot.Progress!=null&&slot.Progress.ui.items.Count==expectedItems;
            Add("saved-slot-inspection",disk,true,"Read-only diagnostic-slot inspection after returning to title: "+slot.Status+".");
            if(!disk)yield break;
            if(!InvokeButton("Continue","uGUI Button.onClick invoked on title Continue."))yield break;

            yield return WaitFor(PlayReady,30f);
            if(!Critical("continue-play-ready",waitResult,"Continue loaded and rebound the play scene."))yield break;
            root=PlaytestUiRoot.Instance;session=root.Session;
            string[] actualLetters=session.Progress.ui.knownSpellLetters.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            bool restored=session.Progress.ui.items.Count==expectedItems&&actualLetters.SequenceEqual(expectedLetters);
            Add("saved-state-restored",restored,true,"Item and known-letter state matched after the real Continue scene transition.");
        }

        bool PlayReady()
        {
            var root=PlaytestUiRoot.Instance;
            return root!=null&&!root.IsTitle&&!root.Busy&&root.Session!=null&&root.Session.Progress!=null&&root.Map!=null&&FindFirstObjectByType<HudController>()!=null;
        }

        IEnumerator WaitFor(Func<bool> condition,float seconds)
        {
            waitResult=false;float until=Mathf.Min(deadline,Time.realtimeSinceStartup+seconds);
            while(!finished&&Time.realtimeSinceStartup<until)
            {if(condition()){waitResult=true;yield break;}yield return null;}
        }

        bool InvokeButton(string name,string detail)
        {
            var button=FindButton(name);bool ok=button!=null&&button.interactable&&button.gameObject.activeInHierarchy;
            Add("button-"+name,ok,true,ok?detail:"Active interactable button was not found.");
            if(ok)button.onClick.Invoke();return ok;
        }

        static Button FindButton(string name)
        {
            var root=PlaytestUiRoot.Instance;if(root==null)return null;
            return root.GetComponentsInChildren<Button>(true).FirstOrDefault(b=>b.name==name&&b.gameObject.activeInHierarchy);
        }

        bool Critical(string id,bool ok,string detail)
        {Add(id,ok,true,detail);return ok;}

        void Add(string id,bool passed,bool verified,string detail)
        {
            if(report==null)return;
            string status=!verified?"UNVERIFIED":passed?"PASS":"FAIL";
            report.checks.Add(new SmokeCheck{id=id,status=status,detail=detail,seconds=Time.realtimeSinceStartup-began});
            if(!verified)report.unverified++;else if(passed)report.passed++;else report.failed++;
        }

        void Finish()
        {
            if(finished)return;finished=true;
            report.durationSeconds=Time.realtimeSinceStartup-began;report.finishedUtc=DateTime.UtcNow.ToString("O");
            report.finalScene=SceneManager.GetActiveScene().name;
            report.outcome=report.failed>0?"FAIL":report.unverified>0?"PASS_WITH_UNVERIFIED":"PASS";
            try
            {
                string directory=Path.GetDirectoryName(reportPath);if(!string.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);
                string temporary=reportPath+".tmp";File.WriteAllText(temporary,JsonUtility.ToJson(report,true));
                if(File.Exists(reportPath))File.Delete(reportPath);File.Move(temporary,reportPath);
            }
            catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is NotSupportedException)
            {Debug.LogError("[UI Smoke] Report write failed: "+e.Message);}
            Debug.Log("[UI Smoke] "+report.outcome+" report="+reportPath);
            Application.Quit();
        }
    }
}
