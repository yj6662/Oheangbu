using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroNaturalCave
    {
        public const string RootName="Playtest_NaturalCave";
        public static string Output=>WorldMacroPlaytestAuthoring.Output+"/NaturalCave";
        public static string Folder=>WorldMacroPlaytestAuthoring.Folder+"/NaturalCave";
        [Serializable] public class Geometry { public CaveMesh[] meshes; }
        [Serializable] public class CaveMesh { public string name; public Vector3[] vertices,normals; public int[] triangles; }
        static Transform Old=>GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
        static Transform Root=>GameObject.Find(RootName).transform;
        static WorldMacroPlaytestSession Session=>Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        public static string Execute(string command)
        {
            if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=WorldMacroPlaytestAuthoring.ScenePath)throw new Exception("Playtest Edit scene required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new Exception("System commit >=85%; stopped");
            Directory.CreateDirectory(Output);DevSceneKit.EnsureFolder(Folder+"/Meshes");DevSceneKit.EnsureFolder(Folder+"/Materials");
            if(command=="survey")return Survey();
            if(command=="apply")return Apply();
            if(command=="terrain")return Terrain();
            if(command=="props")return Props();
            if(command=="inn-ground")return InnGround();
            if(command=="embed")return Embed();
            if(command=="detail-survey")return DetailSurvey();
            if(command=="approach")return Approach();
            if(command=="finish")return Finish();
            if(command=="finish-entry-props")return FinishEntryProps();
            if(command=="outer-ground")return OuterGround();
            if(command=="rays")return Rays();
            if(command=="isolate")return Isolate();
            if(command=="validate")return Validate();
            if(command.StartsWith("capture:"))return Capture(command.Substring(8));
            throw new ArgumentException(command);
        }
        static string Survey()
        {
            var lines=new List<string>();
            foreach(var t in Old.GetComponentsInChildren<Transform>(true))lines.Add("OLD "+t.name+" active="+t.gameObject.activeInHierarchy+" p="+t.position+" r="+(t.GetComponent<Renderer>()!=null && t.GetComponent<Renderer>().enabled)+" c="+(t.GetComponent<Collider>()!=null && t.GetComponent<Collider>().enabled));
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>t.name.Contains("Ore")||t.name.Contains("Vein")||t.name.Contains("Mine_")||t.name.Contains("Investigation")))lines.Add("MINE "+t.name+" "+t.position+" parent="+t.parent?.name+" active="+t.gameObject.activeInHierarchy);
            var inn=GameObject.Find(WorldMacroOriginalInn.RootName)?.transform.Find("Thatched_Inn_C2_Original");
            if(inn!=null) for(float z=4.6f;z>=-1.9f;z-=.25f){var p=inn.TransformPoint(new Vector3(-1.5f,1,z));bool ok=Session.TrySafeFeet(p,out var safe);lines.Add("STEP "+z+" "+p+" =>"+ok+" "+safe+" hits="+string.Join(",",Physics.RaycastAll(p+Vector3.up*1.5f,Vector3.down,4,1).Select(h=>h.collider.name+":"+h.point.y)));}
            File.WriteAllLines(Output+"/scene_survey.txt",lines);return string.Join("\n",lines);
        }
        static Mesh Save(Mesh m,string name)
        {
            var path=Folder+"/Meshes/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(old!=null){EditorUtility.CopySerialized(m,old);Object.DestroyImmediate(m);m=old;}else AssetDatabase.CreateAsset(m,path);
            return m;
        }
        static Material Rock(bool floor=false)
        {
            string path=Folder+"/Materials/"+(floor?"CaveFloor":"CaveRock")+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("Oheangbu/NaturalCaveRock"));AssetDatabase.CreateAsset(m,path);}
            string stem="Assets/YongmeoriCoast/Texture/LandScape/"+(floor?"T_Ground03":"T_Rock01");
            m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(stem+"_BC.png"));m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(stem+"_NM.png"));
            m.SetColor("_BaseColor",floor?new Color(.50f,.47f,.41f):new Color(.65f,.64f,.59f));m.SetFloat("_WorldTiling",floor?.35f:.16f);m.SetFloat("_CaveAmbient",floor?.32f:.44f);m.SetFloat("_Saturation",.35f);m.SetFloat("_BumpScale",.65f);m.SetFloat("_LightResponse",.8f);m.SetFloat("_Cull",0);m.SetFloat("_WashStrength",.15f);EditorUtility.SetDirty(m);return m;
        }
        static string Apply()
        {
            if(GameObject.Find(RootName)!=null)throw new Exception("Natural cave exists; targeted edits only");
            var data=JsonUtility.FromJson<Geometry>(File.ReadAllText(Output+"/geometry.json"));if(data.meshes==null||data.meshes.Length<2)throw new Exception("Geometry incomplete");
            File.WriteAllText(Output+"/gameplay_before.json",EditorJsonUtility.ToJson(Session.Content,true));
            var root=new GameObject(RootName).transform;root.SetPositionAndRotation(Old.position,Old.rotation);
            foreach(Transform t in Old)if(t.name!="Entry"&&t.name!="ViewTarget"&&t.name!="Cave_Entrance_Stone_Ramp")t.gameObject.SetActive(false);
            var owned=GameObject.Find("Playtest_OwnedAssets").transform;
            foreach(string key in new[]{"Mine_Asset_Lining","Mine_Continuous_Asset_Panels","Cave_Visual_Closure","Mine_Work_Ambient","Mine_Work_Lantern"})owned.Find(key)?.gameObject.SetActive(false);
            var early=GameObject.Find("Playtest_EarlyArt").transform;
            foreach(Transform t in early)if(t.name.StartsWith("Mine_")||t.name=="Blast_Cord"||t.name=="Exit_Transport_Basket")t.gameObject.SetActive(false);
            foreach(var record in data.meshes)
            {
                var mesh=new Mesh{name=record.name,indexFormat=IndexFormat.UInt32};mesh.vertices=record.vertices;mesh.triangles=record.triangles;
                if(record.normals!=null&&record.normals.Length==record.vertices.Length)mesh.normals=record.normals;else mesh.RecalculateNormals();
                mesh.uv=record.vertices.Select(p=>new Vector2(p.x,p.z)*.2f).ToArray();mesh.RecalculateBounds();mesh.RecalculateTangents();mesh=Save(mesh,record.name);
                var go=new GameObject(record.name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=Rock(record.name.Contains("Floor"));go.AddComponent<MeshCollider>().sharedMesh=mesh;go.isStatic=true;
            }
            Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
            return Validate();
        }
        // All terrain outside this small throat neighbourhood retains its original vertices and triangles.
        // Only derivative meshes are assigned, to both renderer and physical collider.
        static string Terrain()
        {
            var caveRoot=Root;var wall=caveRoot.Find("Natural_Cave_Interior").GetComponent<MeshCollider>();var lines=new List<string>();
            bool backfaces=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
            try
            {
                foreach(var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(m=>m.name.StartsWith("Terrain_")).ToArray())
                {
                    var source=mf.sharedMesh;if(source==null)continue;
                    var positions=source.vertices;var world=positions.Select(p=>caveRoot.InverseTransformPoint(mf.transform.TransformPoint(p))).ToArray();
                    if(!world.Any(p=>p.x>-100&&p.x<30&&p.z>-65&&p.z<55))continue;
                    string originalPath=AssetDatabase.GetAssetPath(source);if(originalPath.Contains("/NaturalCave/"))throw new Exception("Terrain already patched; restore original derivative source before rerun");
                    var normals=source.normals;var uvs=source.uv;var colors=source.colors;var verts=new List<Vector3>();var tris=new List<int>();var uvlist=new List<Vector2>();var clist=new List<Color>();int refined=0,cut=0,lifted=0;
                    Vector3 Change(Vector3 p)
                    {
                        var q=caveRoot.InverseTransformPoint(mf.transform.TransformPoint(p));
                        if(q.x< -100||q.x>20||q.z< -45||q.z>45)return p;
                        float target=q.y;
                        // Blend the low entrance shoulder into the mountain rather than placing a roof dome on it.
                        if(q.x< -43.5f)
                        {
                            for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                            {
                                var sample=q+new Vector3(dx*6,0,dz*6);var origin=caveRoot.TransformPoint(new Vector3(sample.x,70,sample.z));
                                if(wall.Raycast(new Ray(origin,Vector3.down),out var hit,72))
                                {
                                    float roof=caveRoot.InverseTransformPoint(hit.point).y;
                                    float margin=Mathf.Sqrt(dx*dx+dz*dz)*6;
                                    target=Mathf.Max(target,roof+2.6f-margin*.72f);
                                }
                            }
                        }
                        if(target>q.y+.001f){q.y=target;lifted++;return mf.transform.InverseTransformPoint(caveRoot.TransformPoint(q));}return p;
                    }
                    void Add(Vector3 a,Vector3 b,Vector3 c,Vector2 ua,Vector2 ub,Vector2 uc,Color ca,Color cb,Color cc,int depth)
                    {
                        var mid=caveRoot.InverseTransformPoint(mf.transform.TransformPoint((a+b+c)/3));
                        bool near=mid.x>-100&&mid.x<25&&mid.z>-60&&mid.z<50;
                        if(near&&depth<5&&Mathf.Max((a-b).sqrMagnitude,Mathf.Max((b-c).sqrMagnitude,(a-c).sqrMagnitude))>4)
                        {refined++;var ab=(a+b)*.5f;var bc=(b+c)*.5f;var ac=(a+c)*.5f;var uab=(ua+ub)*.5f;var ubc=(ub+uc)*.5f;var uac=(ua+uc)*.5f;var cab=(ca+cb)*.5f;var cbc=(cb+cc)*.5f;var cac=(ca+cc)*.5f;Add(a,ab,ac,ua,uab,uac,ca,cab,cac,depth+1);Add(ab,b,bc,uab,ub,ubc,cab,cb,cbc,depth+1);Add(ac,bc,c,uac,ubc,uc,cac,cbc,cc,depth+1);Add(ab,bc,ac,uab,ubc,uac,cab,cbc,cac,depth+1);return;}
                        a=Change(a);b=Change(b);c=Change(c);mid=caveRoot.InverseTransformPoint(mf.transform.TransformPoint((a+b+c)/3));
                        if(mid.x> -48&&mid.x< -36&&mid.z> -5.8f&&mid.z<15.8f&&mid.y>.08f&&mid.y<13.2f){cut++;return;}
                        var o=caveRoot.TransformPoint(new Vector3(mid.x,70,mid.z));
                        if(mid.y>.08f&&wall.Raycast(new Ray(o,Vector3.down),out var roofHit,72)&&mid.y<caveRoot.InverseTransformPoint(roofHit.point).y-.15f){cut++;return;}
                        int n=verts.Count;verts.AddRange(new[]{a,b,c});uvlist.AddRange(new[]{ua,ub,uc});clist.AddRange(new[]{ca,cb,cc});tris.AddRange(new[]{n,n+1,n+2});
                    }
                    var ts=source.triangles;for(int i=0;i<ts.Length;i+=3){int a=ts[i],b=ts[i+1],c=ts[i+2];Add(positions[a],positions[b],positions[c],uvs.Length>0?uvs[a]:Vector2.zero,uvs.Length>0?uvs[b]:Vector2.zero,uvs.Length>0?uvs[c]:Vector2.zero,colors.Length>0?colors[a]:Color.white,colors.Length>0?colors[b]:Color.white,colors.Length>0?colors[c]:Color.white,0);}
                    var mesh=new Mesh{name=mf.name+"_CaveThroat",indexFormat=IndexFormat.UInt32};mesh.SetVertices(verts);mesh.SetTriangles(tris,0);mesh.SetUVs(0,uvlist);if(colors.Length>0)mesh.SetColors(clist);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=Save(mesh,mesh.name);mf.sharedMesh=mesh;mf.GetComponent<MeshCollider>().sharedMesh=mesh;
                    lines.Add(mf.name+" source="+originalPath+" tris="+ts.Length/3+" -> "+tris.Count/3+" subdivided="+refined+" cut="+cut+" vertex lifts="+lifted);
                }
            }
            finally{Physics.queriesHitBackfaces=backfaces;}
            Physics.SyncTransforms();File.WriteAllLines(Output+"/terrain_changes.txt",lines);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return string.Join("\n",lines);
        }
        static string Validate()
        {
            Physics.SyncTransforms();var lines=new List<string>();var root=Root;var rs=root.GetComponentsInChildren<MeshRenderer>();
            lines.Add("Geometry renderers="+rs.Length+" tris="+rs.Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3));
            lines.Add((rs.All(r=>r.sharedMaterial!=null&&!ShaderUtil.ShaderHasError(r.sharedMaterial.shader))?"PASS":"FAIL")+" shaders compile");
            bool back=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=true;
            try{foreach(var p in new[]{new Vector3(-136,.13f,4),new Vector3(-131,.13f,-2),new Vector3(-74,.13f,5),new Vector3(-43,.13f,5),new Vector3(-26,.13f,-1),new Vector3(-9,.13f,-15),new Vector3(0,.13f,-25)}){var w=root.TransformPoint(p);bool safe=Session.TrySafeFeet(w,out var feet);var roof=Physics.RaycastAll(w+Vector3.up*1.8f,Vector3.up,75,1).Where(h=>h.collider.transform.IsChildOf(root)).OrderBy(h=>h.distance).FirstOrDefault();lines.Add((safe?"PASS":"FAIL")+" floor capsule "+p+" feet="+feet+" ceiling clearance="+roof.distance);}}
            finally{Physics.queriesHitBackfaces=back;}
            lines.Add("UNVERIFIED user traversal, combat playthrough and moving CPU/GPU performance");File.WriteAllLines(Output+"/geometry_validation.txt",lines);return string.Join("\n",lines);
        }
        static string Capture(string view)
        {
            Vector3 p,t;var root=GameObject.Find(RootName)?.transform??Old;
            if(view=="deep"){p=root.TransformPoint(new Vector3(-144,1.65f,6));t=root.TransformPoint(new Vector3(-117,5,2));}
            else if(view=="chamber"){p=root.TransformPoint(new Vector3(-88,1.65f,-4));t=root.TransformPoint(new Vector3(-60,4,7));}
            else if(view=="mouth"){p=root.TransformPoint(new Vector3(-52,1.65f,5));t=root.TransformPoint(new Vector3(-22,3,-1));}
            else if(view=="outside"){p=root.TransformPoint(new Vector3(-6,3,-26));t=root.TransformPoint(new Vector3(-60,5,5));}
            else if(view=="mountain"){p=root.TransformPoint(new Vector3(66,44,-105));t=root.TransformPoint(new Vector3(-58,16,-5));}
            else if(view=="inn"){p=new Vector3(2005,135.3f,671);t=new Vector3(1998,136,690);}
            else throw new ArgumentException(view);
            var dressing=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();var budget=dressing.PacketBudgetMilliseconds;
            var timer=System.Diagnostics.Stopwatch.StartNew();do{dressing.PrepareView(p,4096,()=>{if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new Exception("Commit guard");});}while(dressing.PendingChunks>0&&timer.Elapsed.TotalSeconds<6);
            // Environment review camera: exclude actors whose procedural rigs are only evaluated in Play.
            var actors=Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled).ToArray();
            try{foreach(var r in actors)r.enabled=false;dressing.PacketBudgetMilliseconds=1000;var image=WorldMacroDressingProbe.Capture("NaturalCave_"+view,true,p.x,p.y,p.z,t.x,t.y,t.z);File.Copy(image,Output+"/"+view+".png",true);return Output+"/"+view+".png";}
            finally{foreach(var r in actors)r.enabled=true;dressing.PacketBudgetMilliseconds=budget;}
        }
    }
}
