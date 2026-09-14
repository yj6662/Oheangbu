using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oheangbu.App.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Incremental repairs for the authored playtest; shared geography and source models stay intact.
    public static class WorldMacroPlaytestPolish
    {
        static string Output => WorldMacroPlaytestAuthoring.Output + "/Polish";
        static WorldMacroPlaytestSession Session => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        static Transform Cave => GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
        static void Require()
        {
            if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Playtest Edit scene required");
            Directory.CreateDirectory(Output);
            Physics.SyncTransforms();
        }
        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        static float Terrain(Vector3 p)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, 2400, p.z), Vector3.down, 4800, 1, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider.name.StartsWith("Terrain_")).ToArray();
            if (hits.Length == 0) throw new Exception("No terrain " + p);
            return hits.Max(h => h.point.y);
        }
        public static string Survey()
        {
            Require(); var rows = new List<string>();
            var inn = GameObject.Find("Inn").transform;
            foreach (var root in new[] { inn, Cave })
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                rows.Add(PathOf(r.transform) + " position=" + r.transform.position.ToString("F3") + " local=" + r.transform.localPosition.ToString("F3") + " scale=" + r.transform.localScale.ToString("F3") + " bounds=" + r.bounds.ToString("F3"));
            foreach (var p in Session.Content.Points)
            {
                rows.Add("POINT " + p.Id + " " + p.Position.ToString("F3"));
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => Vector3.Distance(r.bounds.center,p.Position)<5))
                    rows.Add("NEAR " + p.Id + " " + PathOf(r.transform) + " " + r.bounds.ToString("F3"));
            }
            var floor = inn.Find("Playtest_Inn_Floor").GetComponent<Renderer>().bounds;
            float max = float.MinValue; int intrusion=0;
            for(float x=floor.min.x+.3f;x<floor.max.x;x+=.5f) for(float z=floor.min.z+.3f;z<floor.max.z;z+=.5f)
            { float delta=Terrain(new Vector3(x,0,z))-floor.max.y; max=Mathf.Max(max,delta);if(delta>0)intrusion++; }
            rows.Add("INN terrainAboveFloor="+max+" intrudingSamples="+intrusion);
            File.WriteAllLines(Output+"/survey.txt",rows);return Output+"/survey.txt";
        }
        public static string Capture(string argument)
        {
            Require(); var parts=argument.Split(':');string stage=parts[0],view=parts[1];
            if(stage!="before"&&stage!="after")throw new ArgumentException("before/after required");
            Vector3 p,target; var inn=GameObject.Find("Inn").transform;
            var floor=inn.Find("Playtest_Inn_Floor").GetComponent<Renderer>().bounds;
            if(view=="inn_inside"){p=Session.Content.InnCheckpointFeet+Vector3.up*1.62f;target=p+new Vector3(0,.6f,10);}
            else if(view=="inn_entry"){p=new Vector3(floor.center.x,floor.max.y+1.62f,floor.min.z-4);target=new Vector3(floor.center.x,floor.max.y+1.62f,floor.center.z);}
            else if(view=="inn_side"){p=new Vector3(floor.max.x+3,floor.max.y+1.62f,floor.min.z-3);target=new Vector3(floor.center.x,floor.max.y+2,floor.center.z);}
            else if(view=="ramp_side"){p=Cave.TransformPoint(new Vector3(7,0,-22));p.y=Terrain(p)+1.62f;target=Cave.TransformPoint(new Vector3(0,-.5f,-22));}
            else if(view=="mine_inside"){p=Cave.TransformPoint(new Vector3(0,1.72f,12));target=Cave.TransformPoint(new Vector3(0,1.72f,-16));}
            else if(view=="branch"){p=Session.Content.BranchPath.Last()+new Vector3(2,1.62f,-2);target=Session.Content.BranchPath.Last()+Vector3.up*.3f;}
            else throw new ArgumentException("Unknown view "+view);
            if(stage=="after")
            {
                var before=JsonUtility.FromJson<WorldMacroDressingProbe.Result>(File.ReadAllText(WorldMacroBuilder.Output+"/Dressing/Polish_before_"+view+".json"));
                p=before.position;target=p+Quaternion.Euler(before.euler)*Vector3.forward*10;
            }
            string source=WorldMacroDressingProbe.Capture("Polish_"+stage+"_"+view,true,p.x,p.y,p.z,target.x,target.y,target.z);
            string destination=Output+"/"+stage+"_"+view+".png";File.Copy(source,destination,true);return destination;
        }
        const string MeshFolder=WorldMacroPlaytestAuthoring.Folder+"/PolishMeshes";
        static Mesh SaveMesh(Mesh mesh,string name)
        {
            DevSceneKit.EnsureFolder(MeshFolder);string path=MeshFolder+"/"+name+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing!=null)throw new InvalidOperationException("Repair already exists: "+path);
            mesh.name=name;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static GameObject MeshObject(Transform parent,string name,Mesh mesh,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
            go.AddComponent<MeshCollider>().sharedMesh=mesh;go.isStatic=true;return go;
        }
        // Extrude a convex CCW XY profile into a closed prism, with separate face normals.
        static Mesh Prism(Transform parent,Vector2[] profile,float front,float back,string name)
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            void Face(params Vector3[] points)
            {
                int start=vertices.Count;
                foreach(var p in points){vertices.Add(parent.InverseTransformPoint(p));uv.Add(new Vector2(p.x,p.y)*.23f);}
                for(int i=1;i<points.Length-1;i++)triangles.AddRange(new[]{start,start+i,start+i+1});
            }
            Vector3 V(int i,float z)=>new Vector3(profile[i].x,profile[i].y,z);
            Face(Enumerable.Range(0,profile.Length).Reverse().Select(i=>V(i,front)).ToArray());
            Face(Enumerable.Range(0,profile.Length).Select(i=>V(i,back)).ToArray());
            for(int i=0;i<profile.Length;i++){int j=(i+1)%profile.Length;Face(V(i,front),V(j,front),V(j,back),V(i,back));}
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);return SaveMesh(mesh,name);
        }
        static float RoofUnderside(Transform roof,float x,float z)
        {
            var point=roof.TransformPoint(new Vector3(0,-.5f,0));var n=roof.up;
            return point.y-(n.x*(x-point.x)+n.z*(z-point.z))/n.y;
        }
        static void CloseRamp()
        {
            var ramp=Cave.Find("Cave_Entrance_Stone_Ramp");var filter=ramp.GetComponent<MeshFilter>();var source=filter.sharedMesh;
            var v=source.vertices.ToList();var uv=source.uv.ToList();var t=source.triangles.ToList();int count=v.Count;
            var edges=new Dictionary<(int,int),(int a,int b,int count)>();
            for(int i=0;i<t.Count;i+=3)for(int k=0;k<3;k++)
            {int a=t[i+k],b=t[i+(k+1)%3];var key=(Math.Min(a,b),Math.Max(a,b));if(edges.TryGetValue(key,out var e))edges[key]=(e.a,e.b,e.count+1);else edges[key]=(a,b,1);}
            for(int i=0;i<count;i++)
            {
                var p=ramp.TransformPoint(v[i]);p.y=Mathf.Min(p.y-.18f,Terrain(p)-.18f);
                v.Add(ramp.InverseTransformPoint(p));uv.Add(uv[i]);
            }
            int topIndices=t.Count;
            for(int i=0;i<topIndices;i+=3)t.AddRange(new[]{t[i]+count,t[i+2]+count,t[i+1]+count});
            foreach(var e in edges.Values.Where(e=>e.count==1))
                t.AddRange(new[]{e.b,e.a,e.a+count,e.b,e.a+count,e.b+count});
            var mesh=new Mesh();mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);
            mesh=SaveMesh(mesh,"Cave_Ramp_Closed");filter.sharedMesh=mesh;ramp.GetComponent<MeshCollider>().sharedMesh=mesh;
        }
        static void GroundVisual(string id,float level)
        {
            var point=Session.PreviewPoints.First(p=>p.Id==id);var visual=point.Visual;
            var renderers=visual.GetComponentsInChildren<Renderer>();float min=renderers.Min(r=>r.bounds.min.y);
            visual.position+=Vector3.up*(level-min);
        }
        public static string Apply()
        {
            Require();var inn=GameObject.Find("Inn").transform;
            if(inn.Find("Polish_Gable_Front")!=null)throw new InvalidOperationException("Polish already applied; validate instead");
            if(!File.Exists(Output+"/BeforePolish.unity"))EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Output+"/BeforePolish.unity",true);
            var floor=inn.Find("Playtest_Inn_Floor");var bounds=floor.GetComponent<Renderer>().bounds;
            float high=float.MinValue;
            for(float x=bounds.min.x;x<=bounds.max.x+.001f;x+=.25f)for(float z=bounds.min.z;z<=bounds.max.z+.001f;z+=.25f)
                high=Mathf.Max(high,Terrain(new Vector3(x,0,z)));
            float top=Mathf.Max(bounds.max.y,high+.06f),delta=top-bounds.max.y;
            floor.position+=Vector3.up*delta*.5f;floor.localScale+=Vector3.up*delta;
            floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(WorldMacroBuilder.Folder+"/MacroTimber.mat");
            foreach(string id in new[]{"geumpyo_inn","logger","herbalist"})
            {
                var p=Session.Content.Points.First(p=>p.Id==id);p.Position+=Vector3.up*delta;
                var proxy=Session.PreviewPoints.First(p=>p.Id==id);proxy.transform.position=p.Position;
                Session.PreviewSheet.Entries.First(e=>e.Id==id).Position=p.Position;GroundVisual(id,top);
            }
            Session.Content.InnCheckpointFeet+=Vector3.up*delta;
            // Keep stored route samples on the repaired walking floor, preserving XZ and all IDs.
            for(int i=0;i<Session.Content.MainPath.Length;i++)
            {var p=Session.Content.MainPath[i];if(p.x>bounds.min.x&&p.x<bounds.max.x&&p.z>=bounds.min.z&&p.z<=bounds.max.z){p.y=top+.12f;Session.Content.MainPath[i]=p;}}
            var wall=inn.Find("Playtest_Inn_Back").GetComponent<Renderer>();var roofs=inn.GetComponentsInChildren<Transform>().Where(t=>t.name=="Massing_Roof").OrderBy(t=>t.position.x).ToArray();
            float mid=bounds.center.x,bottom=wall.bounds.max.y-.025f;
            var profile=new[]{new Vector2(bounds.min.x,bottom),new Vector2(bounds.max.x,bottom),
                new Vector2(bounds.max.x,RoofUnderside(roofs[1],bounds.max.x,bounds.center.z)+.025f),
                new Vector2(mid,Mathf.Min(RoofUnderside(roofs[0],mid,bounds.center.z),RoofUnderside(roofs[1],mid,bounds.center.z))+.025f),
                new Vector2(bounds.min.x,RoofUnderside(roofs[0],bounds.min.x,bounds.center.z)+.025f)};
            MeshObject(inn,"Polish_Gable_Front",Prism(inn,profile,bounds.min.z,bounds.min.z+.3f,"Inn_Gable_Front"),wall.sharedMaterial);
            MeshObject(inn,"Polish_Gable_Back",Prism(inn,profile,bounds.max.z-.3f,bounds.max.z,"Inn_Gable_Back"),wall.sharedMaterial);
            // Shallow, closed threshold follows terrain across its width and joins below the floor edge.
            var verts=new List<Vector3>();var indices=new List<int>();var tex=new List<Vector2>();
            const int steps=8;
            for(int z=0;z<=steps;z++)for(int x=0;x<2;x++)
            {
                float wx=mid+(x==0?-1.0f:1.0f),wz=Mathf.Lerp(bounds.min.z-2,bounds.min.z+.025f,z/(float)steps);
                float h=Mathf.Lerp(Terrain(new Vector3(wx,0,bounds.min.z-2))+.005f,top-.002f,z/(float)steps);
                verts.Add(inn.InverseTransformPoint(new Vector3(wx,h,wz)));tex.Add(new Vector2(wx,wz)*.23f);
            }
            for(int i=0;i<steps;i++){int a=i*2;indices.AddRange(new[]{a,a+2,a+1,a+1,a+2,a+3});}
            // Bottom and boundary faces use the same winding convention as the cave ramp.
            int n=verts.Count;for(int i=0;i<n;i++){var p=inn.TransformPoint(verts[i]);p.y=Mathf.Min(p.y-.2f,Terrain(p)-.2f);verts.Add(inn.InverseTransformPoint(p));tex.Add(tex[i]);}
            int topCount=indices.Count;for(int i=0;i<topCount;i+=3)indices.AddRange(new[]{indices[i]+n,indices[i+2]+n,indices[i+1]+n});
            var boundary=new List<(int a,int b)>{(1,0),(steps*2,steps*2+1)};
            for(int i=0;i<steps;i++){boundary.Add((i*2,(i+1)*2));boundary.Add(((i+1)*2+1,i*2+1));}
            foreach(var e in boundary)indices.AddRange(new[]{e.b,e.a,e.a+n,e.b,e.a+n,e.b+n});
            var threshold=new Mesh();threshold.SetVertices(verts);threshold.SetUVs(0,tex);threshold.SetTriangles(indices,0);
            MeshObject(inn,"Polish_Inn_Threshold",SaveMesh(threshold,"Inn_Threshold"),floor.GetComponent<Renderer>().sharedMaterial);
            CloseRamp();
            GroundVisual("mine_inquiry",Cave.position.y);
            var satchel=GameObject.Find("Playtest_WorkerSatchel").transform;
            float support=float.MaxValue;
            foreach(float x in new[]{-.25f,0,.25f})foreach(float z in new[]{-.2f,0,.2f})support=Mathf.Min(support,Terrain(satchel.position+new Vector3(x,0,z)));
            satchel.position=new Vector3(satchel.position.x,support+.15f-.005f,satchel.position.z);
            MoveRestInside();
            AlignContacts();
            Physics.SyncTransforms();EditorUtility.SetDirty(Session.Content);EditorUtility.SetDirty(Session.PreviewSheet);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            File.WriteAllText(Output+"/changes.txt","Inn floor raised "+delta+"m; closed front/back gables and threshold; closed cave ramp; grounded mine evidence, inn props/NPCs and satchel. Shared terrain/models unchanged.");
            return Validate();
        }
        static void MoveRestInside()
        {
            var floor=GameObject.Find("Inn").transform.Find("Playtest_Inn_Floor").GetComponent<Renderer>().bounds;
            var point=Session.Content.Points.First(p=>p.Id=="geumpyo_inn");
            point.Position=new Vector3(point.Position.x,point.Position.y,floor.center.z+1.5f);
            Session.PreviewPoints.First(p=>p.Id==point.Id).transform.position=point.Position;
            Session.PreviewSheet.Entries.First(p=>p.Id==point.Id).Position=point.Position;
        }
        public static string ClearAisle()
        {
            Require();MoveRestInside();EditorUtility.SetDirty(Session.Content);EditorUtility.SetDirty(Session.PreviewSheet);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return Validate();
        }
        static void AlignContacts()
        {
            var inn=GameObject.Find("Inn").transform;var floor=inn.Find("Playtest_Inn_Floor");
            float width=inn.Find("Playtest_Inn_Back").GetComponent<Renderer>().bounds.size.x;
            float depth=inn.Find("Playtest_Inn_Side").GetComponent<Renderer>().bounds.size.z;
            // Hide the slab's sides within wall thickness, avoiding coplanar exterior faces.
            floor.localScale=new Vector3(width-.04f,floor.localScale.y,depth-.04f);
            var bag=GameObject.Find("Playtest_WorkerSatchel").transform;
            var hit=Physics.RaycastAll(bag.position+Vector3.up*2,Vector3.down,5,1,QueryTriggerInteraction.Ignore).First(h=>h.collider.name.StartsWith("Terrain_"));
            bag.rotation=Quaternion.FromToRotation(Vector3.up,hit.normal);
            bag.position=hit.point+hit.normal*(bag.localScale.y*.5f);
            float gap=float.MinValue;
            foreach(float x in new[]{-.5f,0,.5f})foreach(float z in new[]{-.5f,0,.5f})
            {var p=bag.TransformPoint(new Vector3(x,-.5f,z));gap=Mathf.Max(gap,p.y-Terrain(p));}
            bag.position-=Vector3.up*(gap+.005f);
            // These original exterior rock LODs are buried in the closed shell; only intersecting
            // triangle tips were visible from inside. Retain them disabled in this scene for reversal.
            foreach(Transform child in Cave)if(child.name=="Wall05B")child.gameObject.SetActive(false);
        }
        public static string RepairContacts()
        {
            Require();AlignContacts();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return Validate();
        }
        public static string Validate()
        {
            Require();var rows=new List<string>();void Check(bool ok,string label){rows.Add((ok?"PASS ":"FAIL ")+label);}
            var inn=GameObject.Find("Inn").transform;var floor=inn.Find("Playtest_Inn_Floor").GetComponent<Renderer>().bounds;
            int samples=0,bad=0;float minClearance=float.MaxValue;
            for(float x=floor.min.x;x<=floor.max.x+.001f;x+=.25f)for(float z=floor.min.z;z<=floor.max.z+.001f;z+=.25f)
            {samples++;float d=floor.max.y-Terrain(new Vector3(x,0,z));minClearance=Mathf.Min(minClearance,d);if(d<.04f)bad++;}
            Check(bad==0,"inn floor terrain clearance: "+bad+" intrusions / "+samples+" samples, minimum="+minClearance);
            foreach(string name in new[]{"Polish_Gable_Front","Polish_Gable_Back","Polish_Inn_Threshold"})
                Check(inn.Find(name)!=null,"installed "+name);
            var roofs=inn.GetComponentsInChildren<Transform>().Where(t=>t.name=="Massing_Roof").OrderBy(t=>t.position.x).ToArray();
            int openings=0,wallRays=0;
            foreach(float z in new[]{floor.min.z-.2f,floor.max.z-.5f})for(float x=floor.min.x+.15f;x<floor.max.x-.15f;x+=.25f)
            {
                float roof=RoofUnderside(x<floor.center.x?roofs[0]:roofs[1],x,z);
                for(float y=inn.Find("Playtest_Inn_Back").GetComponent<Renderer>().bounds.max.y+.05f;y<roof-.05f;y+=.2f)
                {wallRays++;if(!Physics.Raycast(new Vector3(x,y,z),Vector3.forward,.8f,1,QueryTriggerInteraction.Ignore))openings++;}
            }
            Check(openings==0,"gable closure rays: "+openings+" openings / "+wallRays);
            foreach(var mf in new[]{Cave.Find("Cave_Entrance_Stone_Ramp").GetComponent<MeshFilter>(),inn.Find("Polish_Inn_Threshold").GetComponent<MeshFilter>(),inn.Find("Polish_Gable_Front").GetComponent<MeshFilter>(),inn.Find("Polish_Gable_Back").GetComponent<MeshFilter>()})
            {
                var v=mf.sharedMesh.vertices;var t=mf.sharedMesh.triangles;
                // Weld coincident positions because the gable intentionally separates face normals.
                var welded=new Dictionary<Vector3,int>();var ids=new int[v.Length];for(int i=0;i<v.Length;i++){if(!welded.TryGetValue(v[i],out int id)){id=welded.Count;welded[v[i]]=id;}ids[i]=id;}
                var counts=new Dictionary<(int,int),int>();var directed=new Dictionary<(int,int),int>();int degenerate=0;
                for(int i=0;i<t.Length;i+=3){if(Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).sqrMagnitude<1e-12f)degenerate++;
                    for(int k=0;k<3;k++){int a=ids[t[i+k]],b=ids[t[i+(k+1)%3]];var key=(Math.Min(a,b),Math.Max(a,b));counts.TryGetValue(key,out int n);counts[key]=n+1;directed.TryGetValue(key,out int balance);directed[key]=balance+(a<b?1:-1);}}
                Check(counts.Values.All(n=>n==2)&&directed.Values.All(n=>n==0)&&degenerate==0,mf.name+" closed oriented mesh; badEdges="+counts.Values.Count(n=>n!=2)+" winding="+directed.Values.Count(n=>n!=0)+" degenerate="+degenerate);
            }
            int blocked=0,walkSamples=0;
            for(float z=floor.min.z-2.2f;z<=Session.Content.InnCheckpointFeet.z;z+=.1f)foreach(float x in new[]{-.65f,0,.65f})
            {
                walkSamples++;var p=new Vector3(floor.center.x+x,floor.max.y+.7f,z);
                if(!Physics.Raycast(p,Vector3.down,out var hit,2,1,QueryTriggerInteraction.Ignore)){blocked++;continue;}
                var foot=hit.point+Vector3.up*.05f;
                if(Physics.OverlapCapsule(foot+Vector3.up*.3f,foot+Vector3.up*1.47f,.28f,1,QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(Session.Walker.Body.transform)))blocked++;
            }
            Check(blocked==0,"doorway 1.3m width / 1.75m capsule: "+blocked+" blocked of "+walkSamples);
            foreach(string id in new[]{"mine_inquiry","geumpyo_inn","logger","herbalist"})
            {
                var proxy=Session.PreviewPoints.First(p=>p.Id==id);float low=proxy.Visual.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y);
                Check(Mathf.Abs(low-(id=="mine_inquiry"?Cave.position.y:floor.max.y))<.002f,id+" visual grounded");
                Check(Vector3.Distance(proxy.transform.position,Session.Content.Points.First(p=>p.Id==id).Position)<.001f,id+" visual/interaction position aligned");
            }
            Check(Session.PreviewPoints.Length==83,"83 content identities preserved");
            Check(Session.TrySafeFeet(Session.Content.InnCheckpointFeet,out _),"inn checkpoint supports safe restore");
            foreach(var p in Session.Content.Points)
                Check(Session.TrySafeFeet(p.Position-Vector3.forward*1.8f,out _),p.Id+" interaction approach supports player capsule");
            var original=AssetDatabase.LoadAssetAtPath<Mesh>(WorldMacroLandmarkAuthoring.Folder+"/Meshes/Cave_Entrance_Stone_Ramp.asset");
            var repaired=Cave.Find("Cave_Entrance_Stone_Ramp").GetComponent<MeshFilter>().sharedMesh;
            Check(original.vertices.SequenceEqual(repaired.vertices.Take(original.vertexCount))&&original.triangles.SequenceEqual(repaired.triangles.Take(original.triangles.Length)),"cave walking surface vertices and triangles unchanged");
            var bag=GameObject.Find("Playtest_WorkerSatchel").transform;
            float largestGap=float.MinValue,deepest=float.MaxValue;
            foreach(float x in new[]{-.5f,0,.5f})foreach(float z in new[]{-.5f,0,.5f})
            {var p=bag.TransformPoint(new Vector3(x,-.5f,z));float d=p.y-Terrain(p);largestGap=Mathf.Max(largestGap,d);deepest=Mathf.Min(deepest,d);}
            Check(largestGap<=.002f&&deepest>-.05f,"satchel 9 footprint samples: max gap="+largestGap+" deepest contact="+deepest);
            File.WriteAllLines(Output+"/validation.txt",rows);return string.Join("\n",rows);
        }
    }
}
