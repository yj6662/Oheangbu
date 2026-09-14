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
    // Asset-first, incremental repair of the separate playable scene. Never rebuilds geography.
    public static class WorldMacroPlaytestAssetReuse
    {
        const string House = "Assets/House_2/house2 .fbx";
        static string Output => WorldMacroPlaytestAuthoring.Output + "/AssetReuse";
        static string Folder => WorldMacroPlaytestAuthoring.Folder + "/AssetReuse";
        static WorldMacroPlaytestSession Session => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        public static string Execute(string command)
        {
            if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Playtest edit scene required");
            Directory.CreateDirectory(Output);
            if(command=="inspect")return Inspect();
            if(command=="apply")return Apply();
            if(command=="retry-unsaved-install"){
                if(!File.Exists(Output+"/BeforeAssetReuse.unity")||File.Exists(Output+"/asset_reuse.tsv"))throw new Exception("Not an interrupted unsaved install");
                EditorSceneManager.OpenScene(WorldMacroPlaytestAuthoring.ScenePath);return Apply();
            }
            if(command=="validate")return Validate();
            if(command=="seams")return Seams();
            if(command=="cave-panels")return CavePanels();
            if(command=="cave-backing")return CaveBacking();
            if(command=="ramp-surface")return RampSurface();
            if(command=="ramp-trim")return TrimRamp();
            if(command=="batch")return Batch();
            if(command=="rest-reach"){
                var point=Session.Content.Points.First(p=>p.Id=="geumpyo_inn");var before=point.Position;
                point.Position=Session.Content.InnCheckpointFeet+new Vector3(1.3f,0,1);
                var visual=Session.PreviewPoints.First(p=>p.Id==point.Id);visual.transform.position+=point.Position-before;
                Session.PreviewSheet.Entries.First(p=>p.Id==point.Id).Position=point.Position;
                GameObject.Find(RootName).transform.Find("Geumpyo_ThatchedInn/Rest_Low_Table").position+=point.Position-before;
                EditorUtility.SetDirty(Session.Content);EditorUtility.SetDirty(Session.PreviewSheet);
                File.WriteAllText(Output+"/rest_position_repair.txt","Rest target "+before+" -> "+point.Position+"; checkpoint unchanged. Target was beyond interaction radius from checkpoint after prior polish.");
                File.WriteAllText(Output+"/gameplay_expected.json",EditorJsonUtility.ToJson(Session.Content));
                Session.TestSaveSuffix="";EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Rest table moved within reach of checkpoint; interaction rule unchanged.";
            }
            if(command=="finish"){
                Session.TestSaveSuffix="";EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Saved with normal user slot and Edit mode";
            }
            if(command=="floor-overlap"){
                foreach(var t in GameObject.Find(RootName).GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Rock_Floor_")))t.localScale=new Vector3(1.8f,1,2);
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Overlapping scanned floor borders, unchanged height and collision";
            }
            if(command.StartsWith("capture:"))return Capture(command.Substring(8));
            throw new ArgumentException(command);
        }
        static readonly List<string> Ledger=new List<string>();
        const string RootName="Playtest_OwnedAssets";
        static Material Surface(Material source)
        {
            if(source==null)throw new ArgumentException("Source material must resolve before authoring");
            string path=Folder+"/Materials/"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source))+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat!=null)return mat;
            mat=new Material(Shader.Find("Oheangbu/WorldMacroTexturedSurface")){name=source.name+"_Playtest",enableInstancing=true};
            Texture tex=null;foreach(var key in new[]{"_BaseMap","_MainTex","_DiffuseMap"})if(source.HasProperty(key)&&source.GetTexture(key)!=null){tex=source.GetTexture(key);break;}
            mat.SetTexture("_BaseMap",tex);mat.SetColor("_BaseColor",new Color(.72f,.70f,.65f,1));
            if(source.HasProperty("_BumpMap"))mat.SetTexture("_BumpMap",source.GetTexture("_BumpMap"));
            if(mat.HasProperty("_Cull"))mat.SetFloat("_Cull",0);
            AssetDatabase.CreateAsset(mat,path);return mat;
        }
        // Bake selected source parts into a reusable static mesh, retaining their UVs and material slots.
        static Transform Part(Transform parent,string key,string sourcePath,Func<Renderer,bool> select,Vector3 size,Vector3 bottom,float yaw=0,bool solid=false)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);if(source==null)throw new Exception(sourcePath);
            var temp=Object.Instantiate(source);temp.transform.position=Vector3.zero;temp.transform.rotation=Quaternion.identity;
            try {
                var rs=temp.GetComponentsInChildren<Renderer>(true).Where(select).ToArray();if(rs.Length==0)throw new Exception("No source parts "+key);
                var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);
                var scale=new Vector3(size.x/Mathf.Max(.001f,b.size.x),size.y/Mathf.Max(.001f,b.size.y),size.z/Mathf.Max(.001f,b.size.z));
                var normalize=Matrix4x4.Scale(scale)*Matrix4x4.Translate(-new Vector3(b.center.x,b.min.y,b.center.z));
                var batches=new Dictionary<Material,List<CombineInstance>>();var temporary=new List<Mesh>();
                try {
                    foreach(var r in rs){
                        var filter=r.GetComponent<MeshFilter>();Mesh mesh=filter==null?null:filter.sharedMesh;
                        if(r is SkinnedMeshRenderer skin){mesh=new Mesh();skin.BakeMesh(mesh);temporary.Add(mesh);}
                        if(mesh==null)continue;
                        for(int i=0;i<mesh.subMeshCount;i++){
                            var m=r.sharedMaterials[i];if(!batches.ContainsKey(m))batches[m]=new List<CombineInstance>();
                            batches[m].Add(new CombineInstance{mesh=mesh,subMeshIndex=i,transform=normalize*r.localToWorldMatrix});
                        }
                    }
                    var root=new GameObject(key).transform;root.SetParent(parent,false);root.localPosition=bottom;root.localRotation=Quaternion.Euler(0,yaw,0);
                    int index=0;long tris=0;
                    foreach(var batch in batches){
                        string path=Folder+"/Meshes/"+key+"_"+index+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        if(mesh==null){mesh=new Mesh{name=key,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(batch.Value.ToArray(),true,true);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);}
                        var g=new GameObject("AssetSurface_"+index++);g.transform.SetParent(root,false);g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=Surface(batch.Key);g.isStatic=true;tris+=mesh.triangles.Length/3;
                    }
                    if(solid){var c=root.gameObject.AddComponent<BoxCollider>();c.center=new Vector3(0,size.y*.5f,0);c.size=size;}
                    Ledger.Add(key+"\t"+sourcePath+"\t"+string.Join(",",rs.Select(r=>r.name))+"\t"+tris+"\t"+size);
                    return root;
                } finally {foreach(var m in temporary)Object.DestroyImmediate(m);}
            } finally {Object.DestroyImmediate(temp);}
        }
        static void Hide(Transform root,bool collisions)
        {foreach(var r in root.GetComponentsInChildren<Renderer>(true))r.enabled=false;if(collisions)foreach(var c in root.GetComponentsInChildren<Collider>(true))c.enabled=false;}
        static string Apply()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.isDirty)throw new Exception("Unsaved scene changes; save before asset replacement");
            if(GameObject.Find(RootName)!=null)throw new Exception("Already installed; targeted repairs only");
            if(!File.Exists(Output+"/BeforeAssetReuse.unity"))File.Copy(scene.path,Output+"/BeforeAssetReuse.unity",false);
            File.WriteAllText(Output+"/gameplay_before.json",EditorJsonUtility.ToJson(Session.Content));
            DevSceneKit.EnsureFolder(Folder+"/Meshes");DevSceneKit.EnsureFolder(Folder+"/Materials");Ledger.Clear();
            var root=new GameObject(RootName).transform;
            var inn=GameObject.Find("Inn").transform;var floor=inn.Find("Playtest_Inn_Floor").GetComponent<Renderer>().bounds;
            var house=new GameObject("Geumpyo_ThatchedInn").transform;house.SetParent(root,false);house.position=new Vector3(floor.center.x,floor.max.y,floor.center.z);
            foreach(Transform child in inn)Hide(child,child.name!="Playtest_Inn_Floor"&&child.name!="Polish_Inn_Threshold");
            // Source thatch, plaster-and-timber panels and pillars form an open front common room.
            Part(house,"Thatched_Roof",House,r=>r.name=="polySurface13",new Vector3(21.4f,2.03f,11.7f),new Vector3(0,3.2f,0));
            foreach(float x in new[]{-9f,-6f,-3f,0,3f,6f,9f})
                Part(house,"Rear_Panel_"+x,House,r=>r.name=="polySurface35",new Vector3(3.02f,3.35f,.18f),new Vector3(x,0,4.75f),0,true);
            foreach(float side in new[]{-1f,1f})for(int n=0;n<3;n++)
                Part(house,"Side_Panel_"+side+"_"+n,House,r=>r.name=="polySurface35",new Vector3(3.3f,3.35f,.18f),new Vector3(side*9.45f,0,-3.3f+n*3.3f),90,true);
            foreach(float x in new[]{-9.35f,-6f,-3f,3f,6f,9.35f})
                Part(house,"Porch_Post_"+x,House,r=>r.name=="polySurface77",new Vector3(.29f,3.35f,.29f),new Vector3(x,0,-4.6f),0,true);
            for(int x=0;x<8;x++)for(int z=0;z<4;z++)
                Part(house,"Floor_"+x+"_"+z,"Assets/SeyeonjeongPavilion/Prefabs/SM_Maru_2.prefab",r=>true,new Vector3(2.375f,.20f,2.5f),new Vector3(-8.3125f+x*2.375f,-.20f,-3.75f+z*2.5f));
            var rest=Session.PreviewPoints.First(p=>p.Id=="geumpyo_inn");Hide(rest.Visual,true);
            string props="Assets/KTinteractiveProp/Volum 02/Prefabs/";
            Part(house,"Rest_Low_Table",props+"Tabletop 01.prefab",r=>true,new Vector3(1.25f,.48f,.62f),house.InverseTransformPoint(rest.Visual.position));
            Part(house,"Cupboard",props+"Closet 01.prefab",r=>true,new Vector3(.85f,1.9f,.56f),new Vector3(-7.6f,0,3.8f),180,true);
            foreach(float x in new[]{-4.3f,4.3f})Part(house,"Rice_Sack_"+x,"Assets/KoreanTraditionalFestival/Prefabs/SM_SackOfRice.prefab",r=>true,new Vector3(1.13f,.93f,.54f),new Vector3(x,0,3.8f),0,true);
            foreach(float x in new[]{-3f,3f}){
                var lamp=Part(house,"Porch_Lantern_"+x,props+"Lantern.prefab",r=>true,new Vector3(.3f,.55f,.3f),new Vector3(x,2.5f,-4.4f));
                var light=lamp.gameObject.AddComponent<Light>();light.type=LightType.Point;light.range=5;light.intensity=1.1f;light.color=new Color(1,.7f,.4f);light.shadows=LightShadows.None;
            }
            var satchel=GameObject.Find("Playtest_WorkerSatchel");Hide(satchel.transform,true);
            var bagPoint=Session.Content.Points.First(p=>p.Id=="worker_satchel").Position;
            var bag=Part(root,"Worker_Straw_Bag","Assets/KoreanTraditionalFestival/Prefabs/SM_JolongtaegiBag.prefab",r=>true,new Vector3(.42f,.33f,.60f),bagPoint-Vector3.up*.12f,18);
            var inquiry=Session.PreviewPoints.First(p=>p.Id=="mine_inquiry");Hide(inquiry.Visual,true);
            Part(root,"Mine_Work_Basket","Assets/KoreanTraditionalFestival/Prefabs/SM_Basket.prefab",r=>true,new Vector3(.65f,.29f,.48f),inquiry.Visual.position);
            var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
            var rockRoot=new GameObject("Mine_Asset_Lining").transform;rockRoot.SetParent(root,false);rockRoot.SetPositionAndRotation(cave.position,cave.rotation);
            Hide(cave.Find("Hollow_Rock_Shell"),false);Hide(cave.Find("Cave_Back_Collision"),false);
            string models=WorldMacroLandmarkAuthoring.Folder+"/Models/LM_";
            // Use existing reduced imports instead of re-importing the enormous scan source.
            for(int n=0;n<5;n++)foreach(int side in new[]{-1,1})
                Part(rockRoot,"Mine_Wall_"+side+"_"+n,models+"Wall05B_LOD1.fbx",r=>true,new Vector3(1.9f,6.8f,8.5f),new Vector3(side*5.9f,-.2f,-13+n*7),side<0?0:180);
            for(int n=0;n<5;n++)
                Part(rockRoot,"Mine_Ceiling_"+n,models+"Ceiling05A_LOD1.fbx",r=>true,new Vector3(13.8f,3.4f,9f),new Vector3(0,6.2f,-14+n*7));
            Part(rockRoot,"Mine_Back_Rock",models+"Wall05B_LOD1.fbx",r=>true,new Vector3(2,7,13),new Vector3(0,-.2f,17.5f),90);
            // Lightweight LOD FBX uses placeholder materials: retain the matching original scan surfaces.
            foreach(var r in rockRoot.GetComponentsInChildren<Renderer>()){
                string material=r.transform.parent.name.Contains("Ceiling")?"MI_Ceiling05A":"MI_Wall05B";
                var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/BillemotdonggulLavaTubePack/Material/"+material+".mat");
                if(source==null)throw new Exception("Missing scan material "+material);
                r.sharedMaterial=Surface(source);
            }
            Physics.SyncTransforms();File.WriteAllLines(Output+"/asset_reuse.tsv",new[]{"instance\tsource\tparts\ttriangles\tsize"}.Concat(Ledger));
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            return "Asset replacements saved. "+Ledger.Count+" source instances; gameplay data preserved. Validate and capture next.";
        }
        static string Capture(string view)
        {
            var meta=JsonUtility.FromJson<WorldMacroDressingProbe.Result>(File.ReadAllText(WorldMacroBuilder.Output+"/Dressing/Polish_before_"+view+".json"));
            var p=meta.position;var t=p+Quaternion.Euler(meta.euler)*Vector3.forward*10;
            string path=WorldMacroDressingProbe.Capture("AssetReuse_"+view,true,p.x,p.y,p.z,t.x,t.y,t.z);
            File.Copy(path,Output+"/after_"+view+".png",true);return Output+"/after_"+view+".png";
        }
        static string Seams()
        {
            var root=GameObject.Find(RootName).transform;var house=root.Find("Geumpyo_ThatchedInn");
            Ledger.Clear();
            if(house.Find("Rear_Joint_-7.5")==null){
                foreach(float x in new[]{-7.5f,-4.5f,-1.5f,1.5f,4.5f,7.5f})
                    Part(house,"Rear_Joint_"+x,House,r=>r.name=="polySurface77",new Vector3(.26f,3.45f,.32f),new Vector3(x,0,4.68f));
                foreach(float side in new[]{-1f,1f})foreach(float z in new[]{-4.95f,-1.65f,1.65f,4.95f})
                    Part(house,"Side_Joint_"+side+"_"+z,House,r=>r.name=="polySurface77",new Vector3(.32f,3.45f,.26f),new Vector3(side*9.4f,0,z));
            }
            var inn=GameObject.Find("Inn").transform;
            inn.Find("Polish_Inn_Threshold").GetComponent<Renderer>().enabled=true;
            var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
            // Closed lining is retained as the watertight construction substrate behind irregular scanned modules.
            // Asset scans have open boundaries; their bounding boxes alone cannot seal a cave.
            var wall=Surface(AssetDatabase.LoadAssetAtPath<Material>("Assets/BillemotdonggulLavaTubePack/Material/MI_Wall05B.mat"));
            foreach(string name in new[]{"Hollow_Rock_Shell","Cave_Back_Collision"}){var r=cave.Find(name).GetComponent<Renderer>();r.sharedMaterial=wall;r.enabled=true;}
            var ground=Surface(AssetDatabase.LoadAssetAtPath<Material>("Assets/BillemotdonggulLavaTubePack/Material/M_Floor02B.mat"));
            foreach(string name in new[]{"Cave_Walkable_Floor","Cave_Entrance_Stone_Ramp"})cave.Find(name).GetComponent<Renderer>().sharedMaterial=ground;
            File.AppendAllLines(Output+"/asset_reuse.tsv",Ledger);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Closed cave substrate textured with scan source; scanned relief retained; timber wall joints filled; visible entrance threshold restored.";
        }
        static string CavePanels()
        {
            var root=GameObject.Find(RootName).transform;var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
            if(root.Find("Mine_Continuous_Asset_Panels")!=null)throw new Exception("Panels already installed");
            Ledger.Clear();var lining=new GameObject("Mine_Continuous_Asset_Panels").transform;lining.SetParent(root,false);lining.SetPositionAndRotation(cave.position,cave.rotation);
            string source=WorldMacroLandmarkAuthoring.Folder+"/Models/LM_Wall02A_LOD1.fbx";
            var wall=Surface(AssetDatabase.LoadAssetAtPath<Material>("Assets/BillemotdonggulLavaTubePack/Material/MI_Wall02A.mat"));
            for(int n=0;n<5;n++)for(int arc=0;arc<5;arc++){
                float angle=arc*45f,a=angle*Mathf.Deg2Rad;var rotation=Quaternion.Euler(0,0,angle);
                var center=new Vector3(5.6f*Mathf.Cos(a),1.8f+4.8f*Mathf.Sin(a),-14+n*7);
                var panel=Part(lining,"Rock_Panel_"+n+"_"+arc,source,r=>true,new Vector3(1.15f,6.6f,11),Vector3.zero);
                panel.localRotation=rotation;panel.localPosition=center-rotation*Vector3.up*3.3f;
                foreach(var r in panel.GetComponentsInChildren<Renderer>())r.sharedMaterial=wall;
            }
            var back=Part(lining,"Rear_Continuous_Rock",source,r=>true,new Vector3(1.3f,7.6f,13.5f),new Vector3(0,-.5f,17.5f),90);
            foreach(var r in back.GetComponentsInChildren<Renderer>())r.sharedMaterial=wall;
            var ground=Surface(AssetDatabase.LoadAssetAtPath<Material>("Assets/BillemotdonggulLavaTubePack/Material/M_Floor02B.mat"));
            for(int i=0;i<5;i++){
                var panel=Part(lining,"Rock_Floor_"+i,WorldMacroLandmarkAuthoring.Folder+"/Models/LM_Floor02B_LOD2.fbx",r=>true,new Vector3(12,.055f,9),new Vector3(0,-.035f,-12+i*6));
                foreach(var r in panel.GetComponentsInChildren<Renderer>())r.sharedMaterial=ground;
            }
            // The original substrate only closes tiny open scan borders; never tile an atlas onto it.
            string path=Folder+"/Materials/Cave_Substrate.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(Shader.Find("Oheangbu/WorldMacroTexturedSurface"));mat.SetColor("_BaseColor",new Color(.12f,.11f,.095f));mat.SetFloat("_Cull",0);AssetDatabase.CreateAsset(mat,path);}
            foreach(string key in new[]{"Hollow_Rock_Shell","Cave_Back_Collision","Cave_Walkable_Floor","Cave_Entrance_Stone_Ramp"})cave.Find(key).GetComponent<Renderer>().sharedMaterial=mat;
            root.Find("Mine_Asset_Lining").gameObject.SetActive(false);
            File.AppendAllLines(Output+"/asset_reuse.tsv",Ledger);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Continuous rock scan panels installed with original UVs. Previous jagged lining archived inactive.";
        }
        static string CaveBacking()
        {
            var root=GameObject.Find(RootName).transform;
            var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
            var shell=cave.Find("Hollow_Rock_Shell");shell.GetComponent<Renderer>().enabled=false;
            var backing=root.Find("Cave_Visual_Closure");
            if(backing==null){backing=new GameObject("Cave_Visual_Closure").transform;backing.SetParent(root,false);backing.SetPositionAndRotation(cave.position,cave.rotation);backing.localScale=new Vector3(1.3f,1.35f,1);backing.gameObject.AddComponent<MeshFilter>().sharedMesh=shell.GetComponent<MeshFilter>().sharedMesh;backing.gameObject.AddComponent<MeshRenderer>();}
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Materials/Cave_Substrate.mat");mat.SetColor("_BaseColor",new Color(.29f,.27f,.23f));EditorUtility.SetDirty(mat);backing.GetComponent<Renderer>().sharedMaterial=mat;
            // Light falls locally in the working area, not through global exposure changes.
            var lightNode=root.Find("Mine_Work_Ambient");if(lightNode==null){lightNode=new GameObject("Mine_Work_Ambient").transform;lightNode.SetParent(root,false);var l=lightNode.gameObject.AddComponent<Light>();l.type=LightType.Point;l.range=14;l.intensity=1.8f;l.color=new Color(.85f,.71f,.55f);l.shadows=LightShadows.None;}
            lightNode.position=cave.TransformPoint(new Vector3(-3,3,9));
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Scan panels no longer occluded by the earlier construction substrate; physical support unchanged.";
        }
        static string RampSurface()
        {
            var root=GameObject.Find(RootName).transform;var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
            var ramp=cave.Find("Cave_Entrance_Stone_Ramp").GetComponent<MeshCollider>();
            if(root.Find("Ramp_Scan_Surface")!=null)throw new Exception("Ramp already surfaced");
            var group=new GameObject("Ramp_Scan_Surface").transform;group.SetParent(root,false);group.SetPositionAndRotation(cave.position,cave.rotation);Ledger.Clear();
            var mat=Surface(AssetDatabase.LoadAssetAtPath<Material>("Assets/BillemotdonggulLavaTubePack/Material/M_Floor02B.mat"));
            for(int i=0;i<7;i++){
                float z=-39+i*3.5f,width=Mathf.Lerp(3.2f,6.8f,(z+40)/24);
                var piece=Part(group,"Ramp_Rock_"+i,WorldMacroLandmarkAuthoring.Folder+"/Models/LM_Floor02B_LOD2.fbx",r=>true,new Vector3(width*1.65f,.035f,9),new Vector3(0,0,z));
                foreach(var f in piece.GetComponentsInChildren<MeshFilter>()){
                    var mesh=f.sharedMesh;var vertices=mesh.vertices;
                    for(int v=0;v<vertices.Length;v++){
                        var p=f.transform.TransformPoint(vertices[v]);float relief=vertices[v].y;
                        var ray=new Ray(new Vector3(p.x,cave.position.y+15,p.z),Vector3.down);
                        if(ramp.Raycast(ray,out var hit,50))p.y=hit.point.y+.006f+relief*.2f;
                        else {
                            var hits=Physics.RaycastAll(ray,100,1).Where(h=>h.collider.name.StartsWith("Terrain_")).ToArray();
                            p.y=hits.Length>0?hits.Max(h=>h.point.y)+.006f:cave.position.y-.2f;
                        }
                        vertices[v]=f.transform.InverseTransformPoint(p);
                    }
                    mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);f.GetComponent<Renderer>().sharedMaterial=mat;
                }
            }
            var lamp=Part(root,"Mine_Work_Lantern","Assets/KTinteractiveProp/Volum 02/Prefabs/Lantern.prefab",r=>true,new Vector3(.32f,.58f,.32f),cave.TransformPoint(new Vector3(-4.8f,2.1f,9)),cave.eulerAngles.y);
            root.Find("Mine_Work_Ambient").position=lamp.position+Vector3.up*.3f;
            File.AppendAllLines(Output+"/asset_reuse.tsv",Ledger);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Owned rock scan surface fitted to existing ramp; supporting collision preserved. Mine work light has a visible lantern.";
        }
        static string Batch()
        {
            var root=GameObject.Find(RootName).transform;
            foreach(string key in new[]{"Geumpyo_ThatchedInn","Mine_Continuous_Asset_Panels","Ramp_Scan_Surface"}){
                var group=root.Find(key);if(group.Find("Material_Batches")!=null)continue;
                var surfaces=group.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).ToArray();
                var holder=new GameObject("Material_Batches").transform;holder.SetParent(group,false);int i=0;
                foreach(var batch in surfaces.GroupBy(r=>r.sharedMaterial)){
                    var mesh=new Mesh{name=key+"_batch_"+i,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                    mesh.CombineMeshes(batch.Select(r=>new CombineInstance{mesh=r.GetComponent<MeshFilter>().sharedMesh,subMeshIndex=0,transform=group.worldToLocalMatrix*r.localToWorldMatrix}).ToArray(),true,true);mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh,Folder+"/Meshes/"+mesh.name+".asset");
                    var go=new GameObject("Batch_"+i++);go.transform.SetParent(holder,false);go.isStatic=true;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=batch.Key;
                    foreach(var r in batch)r.enabled=false;
                }
            }
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Static renderers merged per material and local building/cave group; source instances and collision retained.";
        }
        static string TrimRamp()
        {
            var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");var collider=cave.Find("Cave_Entrance_Stone_Ramp").GetComponent<MeshCollider>();
            var root=GameObject.Find(RootName).transform.Find("Ramp_Scan_Surface");
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>()){
                var mesh=filter.sharedMesh;var vertices=mesh.vertices;
                for(int i=0;i<vertices.Length;i++){
                    var q=cave.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));q.z=Mathf.Clamp(q.z,-39.99f,-16.01f);
                    float half=Mathf.Lerp(1.4f,3,(q.z+40)/24)-.015f;q.x=Mathf.Clamp(q.x,-half,half);
                    var p=cave.TransformPoint(q);var ray=new Ray(new Vector3(p.x,cave.position.y+15,p.z),Vector3.down);
                    if(!collider.Raycast(ray,out var hit,50))throw new Exception("Trimmed ramp sample misses support");
                    vertices[i]=filter.transform.InverseTransformPoint(hit.point+Vector3.up*.009f);
                }
                var indices=mesh.triangles;var valid=new List<int>();for(int i=0;i<indices.Length;i+=3){var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];if(Vector3.Cross(b-a,c-a).sqrMagnitude>1e-12f)valid.AddRange(new[]{indices[i],indices[i+1],indices[i+2]});}
                mesh.vertices=vertices;mesh.triangles=valid.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Scan borders trimmed to ramp support; removed stretched side triangles. Construction ramp sides remain explicit.";
        }
        static string Validate()
        {
            Physics.SyncTransforms();var root=GameObject.Find(RootName);var rows=new List<string>();
            void Check(bool ok,string label)=>rows.Add((ok?"PASS ":"FAIL ")+label);
            string expected=File.Exists(Output+"/gameplay_expected.json")?"gameplay_expected.json":"gameplay_before.json";
            Check(File.ReadAllText(Output+"/"+expected)==EditorJsonUtility.ToJson(Session.Content),"gameplay matches recorded layout; rest target reach repair separately recorded");
            var rs=root.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
            Check(rs.All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader!=null&&!ShaderUtil.ShaderHasError(m.shader))),"all asset materials resolve without shader errors");
            Check(root.GetComponentsInChildren<MeshFilter>().Where(f=>f.name!="Cave_Visual_Closure").All(f=>f.sharedMesh!=null&&AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(Folder)),"visible replacement geometry is saved source-derived mesh; named cave closure is construction substrate");
            Check(Session.TrySafeFeet(Session.Content.InnCheckpointFeet,out _),"checkpoint capsule can stand safely");
            foreach(var point in Session.Content.Points){
                bool reachable=false;for(int i=0;i<16;i++){
                    float a=i*Mathf.PI/8;var p=point.Position+new Vector3(Mathf.Cos(a)*1.6f,0,Mathf.Sin(a)*1.6f);
                    if(Session.TrySafeFeet(p,out var feet)&&!Physics.Linecast(feet+Vector3.up*1.3f,point.Position+Vector3.up*.8f,1))reachable=true;
                }
                Check(reachable,"clear interaction approach "+point.Id);
            }
            rows.Add("INFO active source-derived renderers="+rs.Length+" triangles="+rs.Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3));
            rows.Add("UNVERIFIED user walking completion and visual acceptance; no new FPS claim");
            File.WriteAllLines(Output+"/validation.txt",rows);return string.Join("\n",rows);
        }
        static string Inspect()
        {
            var lines=new List<string>();
            lines.Add("dirty="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty);
            var temp=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(House));
            try {
                foreach(var r in temp.GetComponentsInChildren<Renderer>()) {
                    var b=r.bounds;
                    if(b.size.y>1.3f || r.name=="polySurface13")lines.Add(r.name+" bounds="+b.ToString("F3")+" mat="+string.Join(",",r.sharedMaterials.Select(m=>m.name)));
                }
                foreach(var p in Session.Content.Points)lines.Add("POINT "+p.Id+" "+p.Position.ToString("F3"));
                foreach(var p in Session.PreviewPoints.Where(p=>Session.Content.Points.Any(c=>c.Id==p.Id)))
                    lines.Add("VISUAL "+p.Id+" "+p.Visual.position.ToString("F3"));
            } finally {Object.DestroyImmediate(temp);}
            File.WriteAllLines(Output+"/inspection.txt",lines);return Output+"/inspection.txt";
        }
    }
}
