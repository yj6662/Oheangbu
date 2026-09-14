using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    [InitializeOnLoad]
    public static partial class WorldMacroPlaytestAuthoring
    {
        public const string ScenePath="Assets/_Project/Scenes/World/W_WorldMacro_Playtest.unity";
        public const string Folder=WorldMacroBuilder.Folder+"/Playtest";
        public static string Output=>WorldMacroBuilder.Output+"/Playtest";
        [Serializable] class Request { public string id,method,argument; }
        [Serializable] class Reply { public string status,result,error; }
        static WorldMacroPlaytestAuthoring(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            string path=Output+"/command.json";if(!File.Exists(path))return;
            var request=JsonUtility.FromJson<Request>(File.ReadAllText(path));
            File.Move(path,Output+"/request_"+request.id+".json");var reply=new Reply();
            try{reply.result=Execute(request.method,request.argument);reply.status="COMPLETE";}
            catch(Exception e){reply.status="FAILED";reply.error=e.ToString();Debug.LogException(e);}
            File.WriteAllText(Output+"/response_"+request.id+".json",JsonUtility.ToJson(reply,true));
        }
        static string Execute(string method,string argument)
        {
            switch(method){
                case "UI":return PlaytestMenuAuthoring.Execute(argument);
                case "HUD":return WorldMacroPlaytestHudAuthoring.Execute(argument);
                case "Audio":return WorldMacroPlaytestAudioAuthoring.Execute(argument);
                case "Feedback":return WorldMacroFeedbackRuntimeReview.Execute(argument);
                case "VisualCorridor":return WorldMacroVisualCorridorAuthoring.Execute(argument);
                case "PlayerAppearance":return WorldMacroPlayerAppearanceAuthoring.Execute(argument);
                case "PlayerReRig":return WorldMacroPlayerReRigAuthoring.Execute(argument);
                case "PlayerReview":return argument=="poll"?WorldMacroPlayerAppearanceAuthoring.Poll():WorldMacroPlayerAppearanceAuthoring.BeginRuntimeReview(argument);
                case "SummonCast":return WorldMacroSummonCastAuthoring.Execute(argument);
                case "Release":return WorldMacroPlaytestRelease.Execute(argument);
                case "PrepareNavigation":return WorldMacroPlaytestNavStartupAuthoring.PrepareSavedAgents();
                case "AssetReuse":return WorldMacroPlaytestAssetReuse.Execute(argument);
                case "PolishSurvey":return WorldMacroPlaytestPolish.Survey();
                case "PolishCapture":return WorldMacroPlaytestPolish.Capture(argument);
                case "PolishApply":return WorldMacroPlaytestPolish.Apply();
                case "PolishValidate":return WorldMacroPlaytestPolish.Validate();
                case "PolishAisle":return WorldMacroPlaytestPolish.ClearAisle();
                case "PolishContacts":return WorldMacroPlaytestPolish.RepairContacts();
                case "Survey":return Survey();
                case "Route":return Route();
                case "Build":return Build();
                case "Audit":return Audit();
                case "SaveTests":return SaveTests();
                case "Play":return Play(argument);
                case "Stop":EditorApplication.isPlaying=false;return "Stopping playtest";
                case "Runtime":return Runtime(argument);
                case "Capture":return Capture(argument);
                case "RefineRoute":return RefineRoute();
                case "Measure":return WorldMacroPlaytestPerformance.Begin(argument=="on");
                case "MeasurePoll":return WorldMacroPlaytestPerformance.Poll();
                case "RepairPresentation":return RepairPresentation();
                case "InputBegin":return WorldMacroPlaytestInputProbe.Begin();
                case "InputResult":return WorldMacroPlaytestInputProbe.Result();
                case "RepairBranch":return RepairBranch();
                case "Regression":return Regression();
                case "SeedRecovery":return SeedRecovery();
                default:throw new Exception("Unknown playtest command "+method);
            }
        }
        public static Vector3 Ground(Vector3 p, bool terrainOnly=false)
        {
            var hits=Physics.RaycastAll(new Vector3(p.x,terrainOnly?2200:p.y+2,p.z),Vector3.down,terrainOnly?4400:8,1,QueryTriggerInteraction.Ignore);
            foreach(var hit in hits.OrderBy(h=>h.distance))
                if(hit.normal.y>0&&(!terrainOnly||hit.collider.name.StartsWith("Terrain_")))return hit.point;
            throw new Exception("No supporting ground: "+p);
        }
        public static string Survey()
        {
            if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
            if(!File.Exists(ScenePath)){
                if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Current scene has unsaved changes");
                AssetDatabase.CopyAsset(WorldMacroBuilder.ScenePath,ScenePath);
            }
            if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=ScenePath)EditorSceneManager.OpenScene(ScenePath);
            Physics.SyncTransforms();
            var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
            var inn=Object.FindObjectsByType<WorldMacroContentPoint>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(p=>p.Id=="geumpyo_inn");
            var rows=new List<string>();
            rows.Add("Cave="+cave.position+" Entry="+cave.Find("Entry").position+" Inn="+inn.transform.position);
            var a=cave.Find("Entry").position;var b=inn.transform.position;
            for(int i=0;i<=100;i++){
                Vector3 p=Ground(Vector3.Lerp(a,b,i/100f),true);
                rows.Add(i+","+p.x+","+p.y+","+p.z);
            }
            foreach(var r in WorldMacroBuilder.Sheet.Routes)
                if(r.Points.Any(p=>p.x>1800&&p.x<3500&&p.z>400&&p.z<1500))rows.Add("Route "+r.Id+" "+JsonUtility.ToJson(new Points{points=r.Points}));
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if(t.name.Contains("Inn"))rows.Add("Inn object "+t.name+" "+t.position);
            File.WriteAllLines(Output+"/survey.txt",rows);return string.Join("\n",rows.Take(3))+"; see survey.txt";
        }
        [Serializable] class Points {public Vector3[] points;}
        static bool Surface(Vector3 candidate,out Vector3 feet,bool terrainOnly=false)
        {
            feet=default;
            foreach(var hit in Physics.RaycastAll(new Vector3(candidate.x,2200,candidate.z),Vector3.down,4400,1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance)){
                if(terrainOnly&&!hit.collider.name.StartsWith("Terrain_"))continue;
                if(!hit.collider.name.StartsWith("Terrain_")&&!hit.collider.name.Contains("Bridge_")&&!hit.collider.name.Contains("Cave_Walkable")&&!hit.collider.name.Contains("Cave_Entrance_Stone_Ramp")&&!hit.collider.name.Contains("Playtest_Inn_Floor"))continue;
                if(hit.normal.y<.715f)return false;
                feet=hit.point+Vector3.up*.12f;
                foreach(var river in WorldMacroBuilder.Sheet.Rivers)for(int i=1;i<river.Points.Length;i++)
                    if(WorldMacroTerrain.SegmentDistance(feet.x,feet.z,river.Points[i-1],river.Points[i],out var t)<river.Width*.5f+2&&feet.y<Mathf.Lerp(river.Points[i-1].y,river.Points[i].y,t)+.5f)return false;
                if(Physics.CheckCapsule(feet+Vector3.up*.3f,feet+Vector3.up*1.47f,.28f,1,QueryTriggerInteraction.Ignore))return false;
                return true;
            }
            return false;
        }
        static bool Segment(Vector3 a,Vector3 b,float spacing,List<Vector3> samples=null)
        {
            int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/spacing));Vector3 previous=a;
            for(int i=0;i<=count;i++){
                if(!Surface(Vector3.Lerp(a,b,i/(float)count),out var p))return false;
                if(i>0&&Mathf.Abs(p.y-previous.y)>Vector2.Distance(new Vector2(p.x,p.z),new Vector2(previous.x,previous.z))*.74f+.08f)return false;
                if(samples!=null)samples.Add(p);previous=p;
            }
            return true;
        }
        static Vector3[] FindPath(Vector3 start,Vector3 end)
        {
            const float grid=8;float minX=Mathf.Min(start.x,end.x)-240,minZ=Mathf.Min(start.z,end.z)-2600;
            int nx=Mathf.CeilToInt((Mathf.Abs(start.x-end.x)+480)/grid),nz=Mathf.CeilToInt((Mathf.Abs(start.z-end.z)+5200)/grid);
            int total=nx*nz;var valid=new byte[total];var positions=new Vector3[total];var cost=Enumerable.Repeat(float.PositiveInfinity,total).ToArray();var previous=Enumerable.Repeat(-1,total).ToArray();
            bool Valid(int id){if(valid[id]!=0)return valid[id]==1;bool good=Surface(new Vector3(minX+(id%nx)*grid,0,minZ+(id/nx)*grid),out positions[id],true);valid[id]=(byte)(good?1:2);return good;}
            int Nearest(Vector3 p){int best=-1;float d=float.MaxValue;for(int id=0;id<total;id++){float dx=minX+(id%nx)*grid-p.x,dz=minZ+(id/nx)*grid-p.z;float distance=dx*dx+dz*dz;if(distance>grid*grid*9||distance>=d||!Valid(id))continue;if(Segment(p,positions[id],1)){d=distance;best=id;}}if(best<0)throw new Exception("No path endpoint near "+p);return best;}
            int from=Nearest(start),to=Nearest(end);var open=new SortedSet<(float score,int serial,int id)>();int serial=0;cost[from]=0;open.Add((Vector3.Distance(start,end),serial++,from));var closed=new bool[total];
            while(open.Count>0){var item=open.Min;open.Remove(item);int id=item.id;if(closed[id])continue;closed[id]=true;if(id==to)break;
                int x=id%nx,z=id/nx;for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){
                    if(dx==0&&dz==0||x+dx<0||x+dx>=nx||z+dz<0||z+dz>=nz)continue;int next=(z+dz)*nx+x+dx;
                    if(closed[next]||!Valid(next)||!Segment(positions[id],positions[next],2))continue;
                    float distance=Vector3.Distance(positions[id],positions[next]);float value=cost[id]+distance+Mathf.Abs(positions[id].y-positions[next].y)*.5f;
                    if(value>=cost[next])continue;cost[next]=value;previous[next]=id;open.Add((value+Vector3.Distance(positions[next],positions[to]),serial++,next));
                }
            }
            if(previous[to]<0){File.WriteAllLines(Output+"/route_frontier.csv",Enumerable.Range(0,total).Where(i=>valid[i]!=0).Select(i=>positions[i].x+","+positions[i].y+","+positions[i].z+","+valid[i]+","+closed[i]));throw new Exception("No grounded connector found; visited="+closed.Count(x=>x)+"; terrain unchanged");}
            var path=new List<Vector3>{end};for(int id=to;id>=0;id=previous[id])path.Add(positions[id]);path.Add(start);path.Reverse();
            var simplified=new List<Vector3>{start};int at=0;while(at<path.Count-1){int far=at+1;for(int n=path.Count-1;n>at+1;n--)if(Segment(path[at],path[n],1)){far=n;break;}simplified.Add(path[far]);at=far;}
            return simplified.ToArray();
        }
        static string Route()
        {
            if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=ScenePath)throw new Exception("Playtest edit scene required");
            Physics.SyncTransforms();var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
            var bridge=WorldMacroBuilder.Sheet.Routes.First(r=>r.Id=="Trail_Mine_Inn");
            var target=bridge.Points[bridge.Points.Length-8];
            var connector=FindPath(cave.Find("Entry").position,target);
            var main=new List<Vector3>();for(int z=10;z>=-44;z--)main.Add(Ground(cave.TransformPoint(new Vector3(0,0,z)))+Vector3.up*.12f);
            main.AddRange(connector.Skip(1));main.AddRange(bridge.Points.Skip(bridge.Points.Length-7));main.AddRange(WorldMacroBuilder.Sheet.Routes.First(r=>r.Id=="Trail_Bridge_Inn").Points.Skip(1));
            File.WriteAllText(Output+"/route.json",JsonUtility.ToJson(new Points{points=main.ToArray()},true));
            return "Ground connector authored: "+connector.Length+" knots; "+main.Count+" full route knots. No terrain regeneration or automatic walking.";
        }
    }
}
