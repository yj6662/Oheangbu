using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRebuildAuthoring
    {
        static string CaveExploreCheck(string command)
        {
            var s=ArtSession(); var ui=PlaytestUiRoot.Instance; var lines=new List<string>();
            if(command=="ui-cave-explore-walk") return CaveExploreLiveWalk();
            if(command=="ui-cave-explore-centerline-walk") return CaveExploreLiveWalk(true);
            if(command.StartsWith("ui-cave-explore-prop:"))
            {
                ui.CloseMenu();foreach(var actor in s.Actors)actor.gameObject.SetActive(false);
                string name=command.Substring("ui-cave-explore-prop:".Length);
                var target=GameObject.Find(name);if(target==null)throw new Exception("Unknown prop");
                var p=target.transform.position;var offset=name=="Abandoned_HaulCart"?new Vector3(-3,0,-2):new Vector3(2,0,2);
                var from=p+offset+Vector3.up*.12f;var d=-offset;s.Teleport(from,Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg);
                typeof(Oheangbu.Combat.PlayerMotor).GetField("_pitch",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(s.Walker.Motor,18f);
                return "Prop review pose "+name;
            }
            void Check(bool ok,string label) => lines.Add((ok?"PASS ":"FAIL ")+label);
            var zone=ui.MapData.Zones.Single(z=>z.Id=="mine_interior");
            string key=zone.Id+":"+zone.DiscoveryRevision;
            if(command=="ui-cave-explore-reloaded")
            {
                var saved=s.Progress.ui.interiorMaps.FirstOrDefault(m=>m.key==key);
                string expected=SessionState.GetString("Cave235.Saved","");
                Check(!string.IsNullOrEmpty(expected)&&saved!=null&&saved.cells==expected,"same interior discovery survives actual Play exit and reload");
                Check(s.Progress.ui.discoveredCells==SessionState.GetString("Cave235.Surface",""),"surface discovery remains independent after reload");
            }
            else
            {
                Check(zone.ExploreWalkedPassages,"candidate cave opts into walked discovery");
                var saved=s.Progress.ui.interiorMaps.FirstOrDefault(m=>m.key==key);
                Check(saved!=null&&!string.IsNullOrEmpty(saved.cells),"actual player position reveals cave cells");
                if(saved!=null)
                {
                    var size=ui.MapData.BoundsMax-ui.MapData.BoundsMin;
                    var min=ui.MapData.BoundsMin+Vector2.Scale(zone.IllustrationWorldUv.min,size);
                    var max=ui.MapData.BoundsMin+Vector2.Scale(zone.IllustrationWorldUv.max,size);
                    var grid=new WorldMapDiscoveryGrid(min,max,zone.Polygon,Convert.FromBase64String(saved.cells),2);
                    var start=s.Content.StartFeet;
                    Check(grid.IsDiscovered(new Vector2(start.x,start.z)),"start gallery recorded");
                    Check(!grid.IsDiscovered(new Vector2(3495,1775)),"unvisited evidence gallery remains hidden");
                    int cells=0;for(int y=0;y<grid.Height;y++)for(int x=0;x<grid.Width;x++)if(grid.IsDiscovered(x,y))cells++;
                    Check(cells>0&&cells<100,"initial reveal is local, not whole cave (cells="+cells+")");
                    var copy=s.Progress.ui.Copy();copy.interiorMaps[0].cells="";
                    Check(saved.cells.Length>0,"transaction snapshot owns independent interior records");
                    var round=JsonUtility.FromJson<UiProgress>(JsonUtility.ToJson(s.Progress.ui));round.Normalize();
                    Check(round.IsValid()&&round.interiorMaps.Any(m=>m.key==key&&m.cells==saved.cells),"UI JSON round trip preserves cave discoveries");
                    var older=JsonUtility.FromJson<UiProgress>("{}");older.Normalize();Check(older.IsValid()&&older.interiorMaps.Count==0,"old save with no cave records remains valid");
                }
                // A visible corner and an adjacent hidden gallery: callback must gate newly visited cells.
                var test=new WorldMapDiscoveryGrid(Vector2.zero,new Vector2(20,20),new[]{Vector2.zero,new Vector2(20,0),new Vector2(20,20),new Vector2(0,20)},null,2);
                test.Reveal(new Vector2(5,5),8,p=>p.x<8);
                Check(test.IsDiscovered(new Vector2(5,5))&&!test.IsDiscovered(new Vector2(11,5)),"occluded side of nearby gallery stays hidden fixture");
                var sounds=UnityEngine.Object.FindObjectsByType<CompactCaveAmbience>(FindObjectsSortMode.None);
                Check(sounds.Length==3&&sounds.All(a=>a.GetComponent<AudioSource>().clip!=null&&a.GetComponent<AudioSource>().outputAudioMixerGroup!=null),"three cave cues use SFX mixer");
                Check(sounds.All(a=>a.GetComponent<AudioSource>().spatialBlend==1&&a.GetComponent<AudioLowPassFilter>().cutoffFrequency<=2600),"spatial cues retain high-frequency limit");
                Check(GameObject.Find("Abandoned_HaulCart")!=null&&GameObject.Find("Left_WorkBundle")!=null&&GameObject.Find("Retired_SupportTimber")!=null,"three environmental prop groups present");
                var root=GameObject.Find("Cave_Exploration235");Check(root!=null&&root.GetComponentsInChildren<Collider>().Length==0,"decorative edge props add no blocking collider");
                foreach(var name in new[]{"WorldMap/CaveDiscovery","WorldMap/PaperMapSurface"})
                {
                    var shader=Resources.Load<Shader>(name);Check(shader!=null&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"valid shader "+name);
                }
                if(saved!=null)
                {
                    Check(s.SaveNow(out var error),"save actual exploration: "+error);
                    SessionState.SetString("Cave235.Saved",saved.cells);SessionState.SetString("Cave235.Surface",s.Progress.ui.discoveredCells);
                }
            }
            string report=string.Join("\n",lines);File.WriteAllText(Output+"/"+command+".txt",report);return report;
        }

        static string CaveExploreLiveWalk(bool centerline=false)
        {
            var s=ArtSession();var ui=PlaytestUiRoot.Instance;ui.CloseMenu();
            foreach(var a in s.Actors)a.gameObject.SetActive(false);
            var authored=JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CaveV4/geometry.json")).main;
            var nav=new UnityEngine.AI.NavMeshPath();
            if(!UnityEngine.AI.NavMesh.SamplePosition(authored[0],out var from,3,UnityEngine.AI.NavMesh.AllAreas)||
               !UnityEngine.AI.NavMesh.SamplePosition(authored[3],out var to,3,UnityEngine.AI.NavMesh.AllAreas)||
               !UnityEngine.AI.NavMesh.CalculatePath(from.position,to.position,UnityEngine.AI.NavMesh.AllAreas,nav)||nav.status!=UnityEngine.AI.NavMeshPathStatus.PathComplete)
                throw new Exception("First junction navigation incomplete");
            var route=centerline?authored.Take(4).ToArray():nav.corners;
            s.Teleport(route[0],285);var body=s.Walker.Body;var motor=s.Walker.Motor;motor.enabled=false;
            int next=1;float vertical=0;double begin=EditorApplication.timeSinceStartup,last=begin;var trace=new List<Vector3>();
            string surfaceBefore=s.Progress.ui.discoveredCells;
            EditorApplication.CallbackFunction move=null;
            move=()=>
            {
                if(!EditorApplication.isPlaying||body==null){EditorApplication.update-=move;return;}
                double now=EditorApplication.timeSinceStartup;float dt=Mathf.Min(.04f,(float)(now-last));last=now;
                if(Time.timeScale<=0)return;
                var delta=route[next]-body.transform.position;delta.y=0;
                if(delta.magnitude<.3f)next++;
                if(next==route.Length||now-begin>45)
                {
                    EditorApplication.update-=move;motor.enabled=true;
                    var record=s.Progress.ui.interiorMaps.FirstOrDefault(m=>m.key.StartsWith("mine_interior:"));
                    bool saved=s.SaveNow(out var error);
                    var result=(next==route.Length?"PASS":"FAIL")+" live CharacterController "+(centerline?"authored centerline":"NavMesh")+" route start->first junction; elapsed="+(now-begin).ToString("F2")+" traceSamples="+trace.Count+" end="+body.transform.position+"; surfaceUnchanged="+(surfaceBefore==s.Progress.ui.discoveredCells)+"; save="+saved+" "+error;
                    File.WriteAllText(Output+(centerline?"/cave236_centerline_walk.txt":"/cave235_live_walk.txt"),result);
                    if(record!=null){SessionState.SetString("Cave235.Saved",record.cells);SessionState.SetString("Cave235.Surface",s.Progress.ui.discoveredCells);}
                    return;
                }
                delta=route[next]-body.transform.position;delta.y=0;
                vertical=body.isGrounded?-1:Mathf.Max(-20,vertical-9.81f*dt);
                body.Move(Vector3.ClampMagnitude(delta,4.5f*dt)+Vector3.up*vertical*dt);
                trace.Add(body.transform.position);
            };
            EditorApplication.update+=move;return "Live controlled walk started; actors disabled, input motor suspended during diagnostic only; result "+(centerline?"cave236_centerline_walk.txt":"cave235_live_walk.txt");
        }
    }
}
