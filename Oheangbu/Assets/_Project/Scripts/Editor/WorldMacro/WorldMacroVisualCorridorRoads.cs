using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroVisualCorridorAuthoring
    {
        static readonly string[] CapitalRouteIds={"Road_Inn_Post","Road_Post_Merchant","Road_Merchant_Pass","Road_Pass_SouthPost","Road_SouthPost_Gate","Road_Gate_CapitalReservation"};
        static Vector3[] activeMain=Array.Empty<Vector3>();
        static readonly List<Vector3> occupied=new List<Vector3>();
        static WorldMacroSheetSO.RouteSpec FindRoute(string id)=>WorldMacroBuilder.Sheet.Routes.First(r=>r.Id==id);
        static Vector3[] JoinRoute(params string[] ids)
        {
            var points=new List<Vector3>();
            foreach(var id in ids){var r=FindRoute(id);if(points.Count>0&&Vector3.Distance(points.Last(),r.Points[0])>10)throw new Exception("Route endpoint mismatch "+id);points.AddRange(points.Count==0?r.Points:r.Points.Skip(1));}
            return points.ToArray();
        }
        public static string BuildRoads()
        {
            EnsureFolders();Physics.SyncTransforms();
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(session==null||session.Content.MainPath.Length<2)throw new Exception("Actual first-section MainPath required");
            activeMain=session.Content.MainPath;occupied.Clear();
            var routes=Group("01_TerrainConformedRoutes");var nature=Group("02_MountainAndForest");
            if(routes.childCount>0||nature.childCount>0)throw new Exception("Roads already exist; do not replace manually authored content");
            var forest=JoinRoute("Trail_Inn_Logging","Trail_Logging_Deep");var capital=JoinRoute(CapitalRouteIds);
            var report=new List<string>();
            Ribbon(routes,"Mine_To_Inn",activeMain,Sheet.TrailWidth,report);
            Ribbon(routes,"Inn_To_DeepForest",forest,Sheet.TrailWidth,report);
            foreach(var id in CapitalRouteIds)Ribbon(routes,id,FindRoute(id).Points,Mathf.Min(5.6f,FindRoute(id).Width*.68f),report);
            DressRoute(nature,"MineMountain",activeMain,150,4,Sheet.Seed,false);
            DressRoute(nature,"Cheongrim",forest,95,9,Sheet.Seed+91,true);
            DressRoute(nature,"CapitalRoad",capital,340,3,Sheet.Seed+173,false);
            var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
            var ramp=GameObject.Find("Playtest_OwnedAssets").transform.Find("Ramp_Scan_Surface");
            if(cave!=null){
                // Assets mark a worked mine threshold, not evenly spaced navigation lights.
                var entry=cave.Find("Entry").position;
                for(int side=-1;side<=1;side+=2)for(int i=0;i<4;i++){
                    Vector3 p=entry+cave.right*(side*(6.8f+i*.3f))-cave.forward*i*3.2f;
                    p.y=TerrainY(p)-.18f;PlaceDressingPrototype(nature,"MineRock_"+side+"_"+i,"Cheongrim_SM_Rock_K",p,new Vector3(0,2.1f+i*.2f,0),17+i*61+side*42);
                }
                Vector3 pLog=entry+cave.right*8;pLog.y=TerrainY(pLog);
                PlaceDressingPrototype(nature,"MineStack","Cheongrim_SM_M_WoodLog",pLog,new Vector3(2.8f,0,0),cave.eulerAngles.y+18);
            }
            foreach(var id in new[]{"Logging","DeepForest"}){
                var site=WorldMacroBuilder.Sheet.FindSite(id);
                Vector3 p=site.Position+new Vector3(14,0,10);p.y=TerrainY(p);
                PlaceDressingPrototype(nature,id+"_Logs","Cheongrim_SM_M_WoodLog",p,new Vector3(3.2f,0,0),id=="Logging"?22:81);
            }
            report.Add("Placed root records="+Sheet.Placements.Length+". Terrain, source dressing and gameplay paths unchanged.");
            File.WriteAllLines(Output+"/roads_install.txt",report);EditorUtility.SetDirty(Sheet);AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return string.Join("\n",report);
        }
        static Vector3 Along(Vector3[] p,float distance,out Vector3 tangent)
        {
            for(int i=1;i<p.Length;i++){float d=Vector3.Distance(p[i-1],p[i]);if(d<.001f)continue;if(distance<=d){tangent=(p[i]-p[i-1]);tangent.y=0;tangent.Normalize();return Vector3.Lerp(p[i-1],p[i],distance/d);}distance-=d;}
            tangent=(p.Last()-p[p.Length-2]).normalized;return p.Last();
        }
        static void InitializeViews()
        {
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();if(session==null)return;
            var main=session.Content.MainPath;var forest=JoinRoute("Trail_Inn_Logging","Trail_Logging_Deep");
            var views=new List<WorldMacroVisualCorridorSO.View>();
            Add("mine_exit",main,95,32);Add("mountain_path",main,470,42);Add("forest_approach",forest,850,40);
            var gate=WorldMacroBuilder.Sheet.FindSite("SouthGate").Position;
            var eye=gate+new Vector3(9,0,-74);eye.y=TerrainY(eye)+1.55f;
            views.Add(new WorldMacroVisualCorridorSO.View{Id="capital_approach",Eye=eye,Target=gate+Vector3.up*9});
            SetViews(views.ToArray());
            void Add(string id,Vector3[] line,float at,float look){Vector3 d;var p=Along(line,at,out d);p.y=TerrainY(p)+1.55f;var t=Along(line,at+look,out _);t.y=TerrainY(t)+2.5f;views.Add(new WorldMacroVisualCorridorSO.View{Id=id,Eye=p,Target=t});}
        }
        static void Ribbon(Transform parent,string id,Vector3[] points,float width,List<string> report)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();var uvs=new List<Vector2>();var colours=new List<Color>();float travelled=0;
            var dense=new List<Vector3>();for(int i=1;i<points.Length;i++){int n=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(points[i-1],points[i])/2));for(int j=0;j<n;j++)dense.Add(Vector3.Lerp(points[i-1],points[i],j/(float)n));}dense.Add(points.Last());
            int prior=-1,skipped=0;float[] cross={-.5f,-.25f,.25f,.5f};
            for(int i=0;i<dense.Count;i++){
                var p=dense[i];float ground=WorldMacroTerrain.SurfaceHeight(WorldMacroBuilder.Sheet,p.x,p.z);
                if(Mathf.Abs(ground-p.y)>1.3f){prior=-1;skipped++;continue;} // Keep bridge decks and underground surfaces exposed.
                var direction=dense[Mathf.Min(i+1,dense.Count-1)]-dense[Mathf.Max(0,i-1)];direction.y=0;if(direction.sqrMagnitude<.001f){prior=-1;continue;}direction.Normalize();var side=Vector3.Cross(Vector3.up,direction);
                if(i>0)travelled+=Vector3.Distance(dense[i],dense[i-1]);int row=vertices.Count;
                float edgeW=width*(1+.055f*Mathf.Sin(travelled*.13f)+.035f*Mathf.Sin(travelled*.53f));
                for(int j=0;j<4;j++){
                    var q=p+side*(cross[j]*edgeW);q.y=WorldMacroTerrain.SurfaceHeight(WorldMacroBuilder.Sheet,q.x,q.z)+.012f;vertices.Add(q);uvs.Add(new Vector2(q.x,q.z)*.37f);colours.Add(new Color(1,1,1,j==0||j==3?0:.8f));
                }
                if(prior>=0)for(int j=0;j<3;j++){int a=prior+j,b=row+j;triangles.AddRange(new[]{a,b,a+1,a+1,b,b+1});}prior=row;
            }
            var mesh=new Mesh{name=id,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uvs);mesh.SetColors(colours);mesh.RecalculateNormals();mesh.RecalculateBounds();
            string path=Folder+"/Meshes/"+id+".asset";if(AssetDatabase.LoadAssetAtPath<Mesh>(path)!=null)throw new Exception("Saved ribbon already exists "+id);AssetDatabase.CreateAsset(mesh,path);
            var g=new GameObject(id);g.transform.SetParent(parent,false);g.AddComponent<MeshFilter>().sharedMesh=mesh;var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=TrailMaterial();r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=true;g.isStatic=true;
            report.Add(id+": "+triangles.Count/3+" visual tris; "+skipped+" elevated/underground samples excluded. No colliders added.");
        }
        static Material TrailMaterial()
        {
            string path=Folder+"/Materials/Trail_Dirt.mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m!=null)return m;
            var shader=Shader.Find("Oheangbu/VisualCorridorPath");if(shader==null)throw new Exception("Path shader must import first");
            m=new Material(shader){name="Trail_OwnedDirt",enableInstancing=true};m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SeyeonjeongPavilion/Texture/Ground/T_Dirt_2_BC.png"));m.SetColor("_BaseColor",new Color(.62f,.58f,.49f,.72f));AssetDatabase.CreateAsset(m,path);return m;
        }
        static void DressRoute(Transform parent,string key,Vector3[] path,float spacing,int count,int seed,bool forest)
        {
            float total=0;for(int i=1;i<path.Length;i++)total+=Vector3.Distance(path[i-1],path[i]);var rng=new System.Random(seed);int serial=0;
            for(float s=50;s<total-30;s+=spacing*(.72f+(float)rng.NextDouble()*.6f)){
                var centre=Along(path,s,out var forward);var side=Vector3.Cross(Vector3.up,forward);
                for(int j=0;j<count;j++){
                    bool tree=j%3!=0;float sign=rng.NextDouble()<.42?-1:1;float offset=(tree?10:5)+(float)rng.NextDouble()*(forest?24:15);
                    var p=centre+side*sign*offset+forward*((float)rng.NextDouble()*spacing*.42f-spacing*.21f);
                    if(!DryPlacement(p,tree?3.4f:1.3f))continue;p.y=TerrainY(p)-(tree?.04f:.15f);
                    string proto=tree?(forest&&j%2==1?"Cheongrim_SM_Henonis_1":"Cheongrim_SM_UlmusDavidiana_Summer_2"):"Cheongrim_SM_Rock_L";
                    float height=tree?(forest?13:10)+(float)rng.NextDouble()*5:.6f+(float)rng.NextDouble()*1.0f;
                    PlaceDressingPrototype(parent,key+"_"+serial++,proto,p,new Vector3(0,height,0),(float)rng.NextDouble()*360);occupied.Add(p);
                    if(forest&&tree){var fern=p+side*2;fern.y=TerrainY(fern);if(DryPlacement(fern,.5f))PlaceDressingPrototype(parent,key+"_fern_"+serial,"Cheongrim_SM_Deparia_1",fern,new Vector3(0,1.1f,0),(float)rng.NextDouble()*360);}
                }
            }
        }
        static bool DryPlacement(Vector3 p,float radius)
        {
            var geo=WorldMacroBuilder.Sheet;if(!WorldMacroTerrain.Contains(geo,p.x,p.z)||WorldMacroTerrain.Normal(geo,p.x,p.z).y<.78f)return false;
            float h=WorldMacroTerrain.SurfaceHeight(geo,p.x,p.z);
            foreach(var river in geo.Rivers)for(int i=1;i<river.Points.Length;i++)if(WorldMacroTerrain.SegmentDistance(p.x,p.z,river.Points[i-1],river.Points[i],out var t)<river.Width*.5f+45&&h<Mathf.Lerp(river.Points[i-1].y,river.Points[i].y,t)+1) return false;
            foreach(var r in geo.Routes)for(int i=1;i<r.Points.Length;i++)if(WorldMacroTerrain.SegmentDistance(p.x,p.z,r.Points[i-1],r.Points[i],out _)<r.Width*.5f+radius+1)return false;
            for(int i=1;i<activeMain.Length;i+=2)if(WorldMacroTerrain.SegmentDistance(p.x,p.z,activeMain[i-1],activeMain[i],out _)<Sheet.TrailWidth*.5f+radius+1)return false;
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();foreach(var point in session.Content.Points)if(Vector3.Distance(new Vector3(p.x,point.Position.y,p.z),point.Position)<14)return false;
            return !occupied.Any(q=>(new Vector2(q.x-p.x,q.z-p.z)).sqrMagnitude<(radius+2)*(radius+2));
        }
    }
}
