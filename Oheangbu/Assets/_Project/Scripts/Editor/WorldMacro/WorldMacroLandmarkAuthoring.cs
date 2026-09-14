using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.Data.World;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Incremental, source-preserving landmarks. Never regenerates geography.</summary>
    public static class WorldMacroLandmarkAuthoring
    {
        public const string Folder=WorldMacroBuilder.Folder+"/Landmarks";
        public const string RootName="WorldMacro_Landmarks_Authored";
        static string Output=>WorldMacroBuilder.Output+"/Landmarks";
        static readonly Dictionary<string,Material> materialCopies=new Dictionary<string,Material>();
        static Material Stone=>AssetDatabase.LoadAssetAtPath<Material>(WorldMacroBuilder.Folder+"/MacroStone.mat");
        static Material Timber=>AssetDatabase.LoadAssetAtPath<Material>(WorldMacroBuilder.Folder+"/MacroTimber.mat");
        static Material Roof=>AssetDatabase.LoadAssetAtPath<Material>(WorldMacroBuilder.Folder+"/MacroRoof.mat");

        static void RequireScene()
        {
            if(Application.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=WorldMacroBuilder.ScenePath)
                throw new Exception("Macro scene in Edit mode required");
        }
        static WorldMacroLandmarkSheetSO LoadSheet()
        {
            Directory.CreateDirectory(Folder+"/Prefabs");Directory.CreateDirectory(Folder+"/Materials");Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Output);
            var sheet=AssetDatabase.LoadAssetAtPath<WorldMacroLandmarkSheetSO>(Folder+"/Landmarks.asset");
            if(sheet!=null)return sheet;
            sheet=ScriptableObject.CreateInstance<WorldMacroLandmarkSheetSO>();
            sheet.Landmarks=new[]{
                Spec("Palace","Hwanggyeong",76,88,38),Spec("Fortress","Cheolong",140,110,26),
                Spec("Cave","Mine",28,42,0),Spec("Temple","OldTemple",42,60,23)};
            AssetDatabase.CreateAsset(sheet,Folder+"/Landmarks.asset");return sheet;
        }
        static WorldMacroLandmarkSheetSO.Landmark Spec(string id,string site,float w,float d,float hall)=>new WorldMacroLandmarkSheetSO.Landmark{Id=id,SiteId=site,CourtyardSize=new Vector2(w,d),HallWidth=hall};
        public static string Install(string id)
        {
            RequireScene();var sheet=LoadSheet();var spec=sheet.Landmarks.FirstOrDefault(s=>s.Id==id);
            if(spec==null)throw new Exception("Expected Palace, Fortress, Cave or Temple");
            if(id=="Palace")RequireModel("Bongsudang");if(id=="Temple")RequireModel("Byeolchu");
            if(id=="Cave"){RequireModel("Wall05B");RequireModel("Ceiling05A");RequireModel("Floor02B");}
            Physics.SyncTransforms();
            if(!spec.PlacementResolved){ResolvePlacement(spec);spec.PlacementResolved=true;EditorUtility.SetDirty(sheet);}
            var group=GameObject.Find(RootName);if(group==null)group=new GameObject(RootName);
            var old=group.transform.Find(id);if(old!=null){Directory.CreateDirectory(Output+"/Backups");PrefabUtility.SaveAsPrefabAsset(old.gameObject,Folder+"/Prefabs/"+id+"_Previous.prefab");Object.DestroyImmediate(old.gameObject);}
            var root=new GameObject(id).transform;root.SetParent(group.transform,false);root.SetPositionAndRotation(spec.Position,Quaternion.Euler(0,spec.Yaw,0));
            if(id=="Palace")Palace(root,spec);
            else if(id=="Fortress")Fortress(root,spec);
            else if(id=="Cave")Cave(root,spec);
            else Temple(root,spec);
            RemoveOverlappingPlaceholders(root,spec);
            var camera=Object.FindFirstObjectByType<WorldMacroReviewController>();
            camera.InspectionStops=new[]{"Palace","Fortress","Cave","Temple"}.Select(key=>group.transform.Find(key)?.Find("Entry")).Concat(new[]{camera.InspectionStops!=null&&camera.InspectionStops.Length>4?camera.InspectionStops[4]:null}).ToArray();EditorUtility.SetDirty(camera);
            Physics.SyncTransforms();
            PrefabUtility.SaveAsPrefabAsset(root.gameObject,Folder+"/Prefabs/"+id+".prefab");
            WorldMacroWaterAuthoring.SaveScene();AuditOne(root,spec);
            return id+" authored beside "+spec.SiteId+"; sources/terrain preserved. Static exterior/access TEST; user walk validation pending.";
        }
        public static string Reposition(string id)
        {
            RequireScene();var sheet=LoadSheet();var spec=sheet.Landmarks.FirstOrDefault(s=>s.Id==id);
            if(spec==null)throw new Exception("Unknown landmark "+id);
            spec.PlacementResolved=false;EditorUtility.SetDirty(sheet);
            return Install(id);
        }
        static void RequireModel(string name)
        {
            for(int i=0;i<3;i++)if(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Models/LM_"+name+"_LOD"+i+".fbx")==null)
                throw new Exception("Import lightweight LOD first: "+name+" "+i);
        }
        static float Ground(Vector3 p)
        {
            float value=float.NegativeInfinity;
            foreach(var hit in Physics.RaycastAll(new Vector3(p.x,2200,p.z),Vector3.down,4400,1,QueryTriggerInteraction.Ignore))
                if(hit.collider.name.StartsWith("Terrain_"))value=Mathf.Max(value,hit.point.y);
            if(float.IsNegativeInfinity(value))throw new Exception("Missing physical terrain at "+p);
            return value;
        }
        static float RoadClearance(Vector3 p)
        {
            float result=float.MaxValue;
            foreach(var r in WorldMacroBuilder.Sheet.Routes)for(int i=1;i<r.Points.Length;i++)
                result=Mathf.Min(result,WorldMacroTerrain.SegmentDistance(p.x,p.z,r.Points[i-1],r.Points[i],out _)-r.Width*.5f);
            return result;
        }
        static float RiverClearance(Vector3 p)
        {
            float result=float.MaxValue;
            foreach(var river in WorldMacroBuilder.Sheet.Rivers)for(int i=1;i<river.Points.Length;i++)
                result=Mathf.Min(result,WorldMacroTerrain.SegmentDistance(p.x,p.z,river.Points[i-1],river.Points[i],out _)-river.Width*.5f);
            return result;
        }
        static bool DryGround(Vector3 p,float terrain)
        {
            // Runtime water meshes extend beyond nominal channel width to the actual
            // bank. Also reject low floodplain samples beside those wider surfaces.
            foreach(var river in WorldMacroBuilder.Sheet.Rivers)for(int i=1;i<river.Points.Length;i++){
                float distance=WorldMacroTerrain.SegmentDistance(p.x,p.z,river.Points[i-1],river.Points[i],out float t);
                if(distance<river.Width*.5f+75&&terrain<Mathf.Lerp(river.Points[i-1].y,river.Points[i].y,t)+.65f)return false;
            }
            return true;
        }
        static void TerrainFootprint(Transform root,Vector2 size,out float lo,out float hi,out bool dry)
        {
            lo=float.MaxValue;hi=float.MinValue;dry=true;
            for(int z=-2;z<=2;z++)for(int x=-2;x<=2;x++){
                var q=root.TransformPoint(new Vector3(x*size.x*.25f,0,z*size.y*.25f));float h=Ground(q);
                lo=Mathf.Min(lo,h);hi=Mathf.Max(hi,h);dry&=DryGround(q,h);
            }
        }
        static void ResolvePlacement(WorldMacroLandmarkSheetSO.Landmark spec)
        {
            var site=WorldMacroBuilder.Sheet.FindSite(spec.SiteId);if(site==null)throw new Exception("Missing site "+spec.SiteId);
            float best=float.MaxValue;Vector3 bestP=Vector3.zero;float yaw=0;
            float radius=spec.CourtyardSize.magnitude*.5f;
            // Pick a bench beside the existing route. The road never curves merely to accommodate this building.
            for(int ring=0;ring<(spec.Id=="Cave"?42:9);ring++)for(int j=0;j<24;j++)
            {
                float angle=j*Mathf.PI*2/24,range=radius+28+ring*24;
                var p=site.Position+new Vector3(Mathf.Cos(angle)*range,0,Mathf.Sin(angle)*range);
                try{
                if(!WorldMacroTerrain.Contains(WorldMacroBuilder.Sheet,p.x,p.z)||RoadClearance(p)<radius+4||RiverClearance(p)<radius+12)continue;
                var facing=p-site.Position;facing.y=0;var rotation=Quaternion.LookRotation(facing);
                float lo=float.MaxValue,hi=float.MinValue;bool dry=true;
                for(int z=-2;z<=2;z++)for(int x=-2;x<=2;x++){
                    var q=p+rotation*new Vector3(x*spec.CourtyardSize.x*.25f,0,z*spec.CourtyardSize.y*.25f);
                    float h=Ground(q);lo=Mathf.Min(lo,h);hi=Mathf.Max(hi,h);dry&=DryGround(q,h);
                }
                // Include the approach, so a dry courtyard cannot have stairs in the river.
                for(int n=0;n<8;n++){
                    var q=p+rotation*new Vector3(0,0,-spec.CourtyardSize.y*.5f-n*5);
                    dry&=DryGround(q,Ground(q))&&RiverClearance(q)>8;
                    if(spec.Id=="Cave")dry&=RoadClearance(q)>4;
                }
                if(!dry)continue;
                if(spec.Id=="Cave"){
                    // The mine landmark may move down the slope to an actual bench.
                    // Do not compensate a steep site with an elevated concrete-like ramp.
                    if(hi-lo>4)continue;
                    float corridorHigh=float.MinValue;
                    for(int z=-16;z<=18;z+=2)for(int x=-6;x<=6;x+=2)
                        corridorHigh=Mathf.Max(corridorHigh,Ground(p+rotation*new Vector3(x,0,z)));
                    float floor=corridorHigh+.5f,previous=float.NaN,maxGrade=0;
                    float start=Ground(p+rotation*new Vector3(0,0,-40))+.04f;
                    for(int z=-40;z<=-16;z++){
                        float t=(z+40)/24f,h=Mathf.Max(Mathf.Lerp(start,floor,t),Ground(p+rotation*new Vector3(0,0,z))+.04f);
                        if(!float.IsNaN(previous))maxGrade=Mathf.Max(maxGrade,Mathf.Atan(Mathf.Abs(h-previous))*Mathf.Rad2Deg);previous=h;
                    }
                    if(maxGrade>12)continue;
                }
                float score=(hi-lo)*20+range*.06f;
                if(score<best){best=score;bestP=new Vector3(p.x,hi+.10f,p.z);yaw=rotation.eulerAngles.y;}
                }catch(Exception e)when(e.Message.StartsWith("Missing physical terrain")){continue;}
            }
            if(best==float.MaxValue)throw new Exception("No route-clear bench found for "+spec.Id);
            spec.Position=bestP;spec.Yaw=yaw;
        }
        static Transform Node(Transform parent,string name,Vector3 p)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=p;return t;}
        static GameObject Box(Transform parent,string name,Vector3 p,Vector3 size,Material material,bool collision=true)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=p;g.transform.localScale=size;
            g.GetComponent<Renderer>().sharedMaterial=material;if(!collision)Object.DestroyImmediate(g.GetComponent<Collider>());g.isStatic=true;return g;
        }
        static void Foundation(Transform root,Vector2 size)
        {
            float low=root.position.y;
            foreach(var p in new[]{new Vector3(-size.x*.5f,0,-size.y*.5f),new Vector3(size.x*.5f,0,-size.y*.5f),new Vector3(-size.x*.5f,0,size.y*.5f),new Vector3(size.x*.5f,0,size.y*.5f)})low=Mathf.Min(low,Ground(root.TransformPoint(p)));
            float depth=root.position.y-low+.5f;
            Box(root,"Stone_Terrace",new Vector3(0,-depth*.5f,0),new Vector3(size.x,depth,size.y),Stone);
        }
        static void Access(Transform root,Vector2 size,float width,float aimHeight)
        {
            float front=-size.y*.5f;var foot=root.TransformPoint(new Vector3(0,0,front-4));float terrain=Ground(foot);
            int count=Mathf.Clamp(Mathf.CeilToInt((root.position.y-terrain)/.16f),1,90);float tread=.42f;
            for(int i=0;i<count;i++){
                float z=front-(count-i)*tread+.2f;
                float surface=root.position.y-(count-i-1)*.16f;
                float bottom=Ground(root.TransformPoint(new Vector3(0,0,z)))-.15f;
                float height=Mathf.Max(.16f,surface-bottom);
                Box(root,"Access_Step_"+i,new Vector3(0,surface-root.position.y-height*.5f,z),new Vector3(width,height,tread+.01f),Stone);
            }
            var eyeLocal=new Vector3(0,0,front-count*tread-36);var eyeWorld=root.TransformPoint(eyeLocal);eyeWorld.y=Ground(eyeWorld)+.04f;
            var entry=Node(root,"Entry",root.InverseTransformPoint(eyeWorld));entry.localRotation=Quaternion.identity;
            Node(root,"ViewTarget",new Vector3(0,aimHeight,4));
        }
        static Material CopySurface(Material source)
        {
            if(source==null)return Stone;
            string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            if(string.IsNullOrEmpty(guid))guid=source.name.Replace("/","_");
            if(materialCopies.TryGetValue(guid,out var prior)&&prior!=null)return prior;
            string path=Folder+"/Materials/"+guid+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){
                m=new Material(Shader.Find("Oheangbu/WorldMacroTexturedSurface")){name=source.name+"_Macro",enableInstancing=true};
                Texture texture=null;
                foreach(var prop in new[]{"_BaseMap","_MainTex","_DiffuseMap","_Albedo"})if(source.HasProperty(prop)&&source.GetTexture(prop)!=null){texture=source.GetTexture(prop);break;}
                if(texture==null)texture=source.mainTexture;
                m.SetTexture("_BaseMap",texture);m.SetColor("_BaseColor",new Color(.64f,.62f,.58f,1));
                if(source.HasProperty("_BumpMap")&&source.GetTexture("_BumpMap")!=null){m.SetTexture("_BumpMap",source.GetTexture("_BumpMap"));m.SetFloat("_BumpScale",.65f);m.EnableKeyword("_NORMALMAP");}
                AssetDatabase.CreateAsset(m,path);
            }
            materialCopies[guid]=m;return m;
        }
        static Bounds BoundsOf(Transform root)
        {
            var rr=root.GetComponentsInChildren<Renderer>(true);if(rr.Length==0)throw new Exception("No renderers "+root.name);
            var b=rr[0].bounds;foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);return b;
        }
        static Bounds LocalBounds(Transform model,Transform reference)
        {
            var b=new Bounds();bool first=true;
            foreach(var filter in model.GetComponentsInChildren<MeshFilter>(true)){
                if(filter.sharedMesh==null)continue;var box=filter.sharedMesh.bounds;
                for(int i=0;i<8;i++){
                    var corner=box.center+Vector3.Scale(box.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    var point=reference.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if(first){b=new Bounds(point,Vector3.zero);first=false;}else b.Encapsulate(point);
                }
            }
            return b;
        }
        static Transform PlaceSource(Transform root,string path,string name,Vector3 p,float width,float yaw=0,bool collide=true)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(source==null)throw new Exception("Missing source "+path);
            var carrier=Node(root,name,p);carrier.localRotation=Quaternion.Euler(0,yaw,0);
            var model=Object.Instantiate(source,carrier);model.name="Preserved_Source";model.transform.localPosition=Vector3.zero;
            foreach(var c in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
            var b=LocalBounds(model.transform,carrier);float scale=width/Mathf.Max(.01f,b.size.x);model.transform.localScale*=scale;
            b=LocalBounds(model.transform,carrier);model.transform.localPosition-=new Vector3(b.center.x,b.min.y,b.center.z);
            foreach(var r in model.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=r.sharedMaterials.Select(CopySurface).ToArray();
            if(collide)foreach(var f in model.GetComponentsInChildren<MeshFilter>(true)){if(f.sharedMesh==null)continue;var mc=f.gameObject.AddComponent<MeshCollider>();mc.sharedMesh=f.sharedMesh;}
            return carrier;
        }
        static Transform PlaceLod(Transform root,string key,string sourcePrefab,Vector3 p,float width,float yaw=0,bool collide=true)
        {
            var carrier=Node(root,key,p);carrier.localRotation=Quaternion.Euler(0,yaw,0);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefab);if(source==null)throw new Exception(sourcePrefab);
            var sourceRenderer=source.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r=>r.sharedMaterials.Length>0);
            var materials=sourceRenderer.sharedMaterials.Select(CopySurface).ToArray();var lods=new LOD[3];
            float scale=1;Vector3 offset=Vector3.zero;
            for(int i=0;i<3;i++){
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Models/LM_"+key+"_LOD"+i+".fbx");
                var model=Object.Instantiate(asset,carrier);model.name="LOD"+i;model.transform.localPosition=Vector3.zero;
                foreach(var c in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
                if(i==0){var b=LocalBounds(model.transform,carrier);scale=width/Mathf.Max(.01f,b.size.x);}
                model.transform.localScale*=scale;
                if(i==0){var b=LocalBounds(model.transform,carrier);offset=-new Vector3(b.center.x,b.min.y,b.center.z);}
                model.transform.localPosition+=offset;
                var renderers=model.GetComponentsInChildren<Renderer>(true);foreach(var r in renderers){
                    if(r.sharedMaterials.Length!=materials.Length)throw new Exception(key+" material slot mismatch "+r.sharedMaterials.Length+" vs "+materials.Length);
                    r.sharedMaterials=materials;
                }
                lods[i]=new LOD(new[]{.16f,.055f,.006f}[i],renderers);
                if(i==2&&collide)foreach(var f in model.GetComponentsInChildren<MeshFilter>(true)){var mc=f.gameObject.AddComponent<MeshCollider>();mc.sharedMesh=f.sharedMesh;}
            }
            var group=carrier.gameObject.AddComponent<LODGroup>();group.SetLODs(lods);group.RecalculateBounds();return carrier;
        }
        const string Halls="Assets/HwaseongHaenggung/Prefabs/";
        const string Fort="Assets/HwaseongForteressGate/Prefabs/";
        const string Cavern="Assets/BillemotdonggulLavaTubePack/Prefabs/";
        static void Palace(Transform root,WorldMacroLandmarkSheetSO.Landmark s)
        {
            Foundation(root,s.CourtyardSize);
            PlaceLod(root,"Bongsudang",Halls+"SM_Bongsudang.prefab",new Vector3(0,.3f,18),s.HallWidth,180);
            PlaceLod(root,"Byeolchu",Halls+"SM_Byeolchu.prefab",new Vector3(-27,.1f,3),25,90);
            PlaceLod(root,"Byeolchu",Halls+"SM_Byeolchu.prefab",new Vector3(27,.1f,3),25,-90);
            Enclosure(root,s.CourtyardSize,3.2f,10,false);
            Gate(root,new Vector3(0,0,-s.CourtyardSize.y*.5f),10,6.5f);
            Box(root,"Processional_Stone_Path",new Vector3(0,.035f,-9),new Vector3(5,.07f,56),Stone);
            Access(root,s.CourtyardSize,10,10);
        }
        static void Fortress(Transform root,WorldMacroLandmarkSheetSO.Landmark s)
        {
            Foundation(root,s.CourtyardSize);Enclosure(root,s.CourtyardSize,10.5f,14,true);
            Gate(root,new Vector3(0,0,-s.CourtyardSize.y*.5f),14,10.5f);
            PlaceLod(root,"Byeolchu",Halls+"SM_Byeolchu.prefab",new Vector3(0,.2f,28),s.HallWidth,180);
            foreach(float x in new[]{-s.CourtyardSize.x*.5f+6,s.CourtyardSize.x*.5f-6})foreach(float z in new[]{-s.CourtyardSize.y*.5f+7,s.CourtyardSize.y*.5f-7}){
                Box(root,"Watchtower_Stone",new Vector3(x,6,z),new Vector3(13,12,14),Stone);
                PlaceSource(root,Halls+"SM_Naeposa.prefab","Watch_Pavilion",new Vector3(x,12,z),9,180,false);
            }
            Access(root,s.CourtyardSize,14,12);
        }
        static void Enclosure(Transform root,Vector2 size,float height,float gateWidth,bool fortress)
        {
            float x=size.x*.5f,z=size.y*.5f;
            WallRun(new Vector3(-x,0,-z),new Vector3(-gateWidth*.5f,0,-z));WallRun(new Vector3(gateWidth*.5f,0,-z),new Vector3(x,0,-z));
            WallRun(new Vector3(-x,0,-z),new Vector3(-x,0,z));WallRun(new Vector3(x,0,-z),new Vector3(x,0,z));WallRun(new Vector3(-x,0,z),new Vector3(x,0,z));
            void WallRun(Vector3 a,Vector3 b){float length=Vector3.Distance(a,b);int count=Mathf.CeilToInt(length/9);for(int i=0;i<count;i++){
                var pos=Vector3.Lerp(a,b,(i+.5f)/count);float yaw=Quaternion.LookRotation(b-a).eulerAngles.y-90;
                var part=PlaceSource(root,Fort+"SM_CW.prefab","Rampart",pos,length/count+.1f,yaw,false);
                var bounds=BoundsOf(part);float yscale=height/bounds.size.y;foreach(Transform model in part)model.localScale=new Vector3(model.localScale.x,model.localScale.y*yscale,model.localScale.z*(fortress?1.9f:1));
                var collider=part.gameObject.AddComponent<BoxCollider>();collider.center=new Vector3(0,height*.5f,0);collider.size=new Vector3(length/count+.1f,height,fortress?4:1.8f);
            }}
        }
        static void Gate(Transform root,Vector3 p,float width,float height)
        {
            var gate=Node(root,"Open_Gate",p);
            // Pillars and lintel are separate; a passage never gets one solid box collider.
            PlaceSource(gate,Fort+"SM_FortificationArch.prefab","Stone_Arch",Vector3.zero,height,0,true);
            float fill=Mathf.Max(.1f,(width-height)*.5f);
            foreach(float side in new[]{-1f,1f})Box(gate,"Flank_Pier",new Vector3(side*(height*.5f+fill*.5f),height*.22f,0),new Vector3(fill,height*.44f,2.2f),Stone);
            float roofWidth=height*1.65f;
            Box(gate,"Pavilion_Deck",new Vector3(0,height+.06f,0),new Vector3(roofWidth*.9f,.28f,roofWidth*.52f),Stone,false);
            foreach(float x in new[]{-.34f,0,.34f})foreach(float z in new[]{-.18f,.18f})
                PlaceSource(gate,Fort+"SM_B_GateHouse_Column.prefab","Pavilion_Column",new Vector3(x*roofWidth,height+.2f,z*roofWidth),.4f,0,false);
            PlaceSource(gate,Fort+"SM_B_GateHouse_Crossbeam.prefab","Painted_Beams",new Vector3(0,height+2.3f,0),roofWidth*.74f,0,false);
            PlaceSource(gate,Fort+"SM_B_GateHouse_Rafter.prefab","Eave_Rafters",new Vector3(0,height+2.6f,0),roofWidth*.96f,0,false);
            var tiles=PlaceSource(gate,Fort+"SM_B_GateHouse_Roof_001.prefab","Tiled_Gate_Roof",new Vector3(0,height+2.9f,0),roofWidth,0,false);
            var lod=tiles.gameObject.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.012f,tiles.GetComponentsInChildren<Renderer>())});lod.RecalculateBounds();
        }
        static void Temple(Transform root,WorldMacroLandmarkSheetSO.Landmark s)
        {
            Foundation(root,s.CourtyardSize);PlaceLod(root,"Byeolchu",Halls+"SM_Byeolchu.prefab",new Vector3(0,.15f,13),s.HallWidth,180);
            Enclosure(root,s.CourtyardSize,1.9f,6,false);Gate(root,new Vector3(0,0,-s.CourtyardSize.y*.5f),6,4.2f);
            var pagoda=Node(root,"Courtyard_Three_Storey_Stone_Pagoda",new Vector3(0,0,-8));
            Box(pagoda,"Plinth",new Vector3(0,.4f,0),new Vector3(4,.8f,4),Stone);
            for(int i=0;i<3;i++){
                float y=.8f+i*1.65f,w=2.35f-i*.42f;
                Box(pagoda,"Storey_"+i,new Vector3(0,y+.5f,0),new Vector3(w,1,w),Stone);
                Box(pagoda,"Eave_"+i,new Vector3(0,y+1.1f,0),new Vector3(w+.85f,.24f,w+.85f),Stone);
                var taper=Box(pagoda,"Roof_"+i,new Vector3(0,y+1.34f,0),new Vector3(w+.25f,.3f,w+.25f),Stone);taper.transform.localRotation=Quaternion.Euler(0,0,0);
            }
            Box(pagoda,"Finial",new Vector3(0,6,0),new Vector3(.32f,1.4f,.32f),Stone);
            Access(root,s.CourtyardSize,6,8);
        }
        static void Cave(Transform root,WorldMacroLandmarkSheetSO.Landmark s)
        {
            // A real short cavity above an existing dry bench. The terrain is intact;
            // the raised mine floor and approach conceal it without pretending to cut a hole.
            float highest=float.MinValue,lowest=float.MaxValue;
            for(int z=-16;z<=18;z++)for(int x=-6;x<=6;x++){
                float h=Ground(root.TransformPoint(new Vector3(x,0,z)));highest=Mathf.Max(highest,h);lowest=Mathf.Min(lowest,h);
            }
            root.position=new Vector3(root.position.x,highest+.5f,root.position.z);s.Position=root.position;
            EditorUtility.SetDirty(LoadSheet());
            var caveMaterial=CopySurface(AssetDatabase.LoadAssetAtPath<Material>("Assets/BillemotdonggulLavaTubePack/Material/MI_Wall05B.mat"));
            var groundMaterial=AssetDatabase.LoadAssetAtPath<Material>(WorldMacroBuilder.Folder+"/MacroGround.mat");
            BuildCaveShell(root,groundMaterial);
            float thickness=root.position.y-lowest+.4f;
            var floorMaterial=groundMaterial;
            var slab=Box(root,"Cave_Walkable_Floor",new Vector3(0,-thickness*.5f,1),new Vector3(12,thickness,34),floorMaterial);
            var slabMesh=Object.Instantiate(slab.GetComponent<MeshFilter>().sharedMesh);var slabUv=new Vector2[slabMesh.vertexCount];
            var slabVertices=slabMesh.vertices;var slabNormals=slabMesh.normals;
            for(int i=0;i<slabUv.Length;i++){
                var p=Vector3.Scale(slabVertices[i],slab.transform.localScale);var n=slabNormals[i];
                slabUv[i]=Mathf.Abs(n.y)>.5f?new Vector2(p.x,p.z)*.23f:(Mathf.Abs(n.z)>.5f?new Vector2(p.x,p.y):new Vector2(p.z,p.y))*.23f;
            }
            slabMesh.uv=slabUv;slabMesh.RecalculateTangents();slab.GetComponent<MeshFilter>().sharedMesh=SaveCaveMesh(slabMesh,"Cave_Floor_Tiled_UV");
            BuildCaveRamp(root,floorMaterial);
            for(int i=0;i<4;i++){
                float z=-12+i*8;
                foreach(float side in new[]{-1f,1f}){
                    var rock=PlaceLod(root,"Wall05B",Cavern+"SM_Wall05B.prefab",new Vector3(side*6.8f,-.25f,z),2.7f,side<0?0:180,false);
                    rock.localScale=new Vector3(1,1.5f,1);
                }
            }
            var back=PlaceLod(root,"Wall05B",Cavern+"SM_Wall05B.prefab",new Vector3(0,-.2f,17),2.8f,90,false);
            back.localScale=new Vector3(1,2,1);
            Box(root,"Cave_Back_Collision",new Vector3(0,3.5f,18),new Vector3(12,8,2),groundMaterial);
            var entry=Node(root,"Entry",new Vector3(0,0,-44));var world=entry.position;world.y=Ground(world)+.04f;entry.position=world;
            Node(root,"ViewTarget",new Vector3(0,3.6f,-8));
            // Mine work lights have a world reason; use one warm point at the mouth, not an evenly spaced guide chain.
            var lantern=Node(root,"Mine_Entrance_Lantern",new Vector3(-4,3,-15));var light=lantern.gameObject.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.61f,.31f);light.intensity=3;light.range=10;light.shadows=LightShadows.None;
        }
        static void BuildCaveShell(Transform root,Material mat)
        {
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
            const int rows=17,arc=24;
            for(int shell=0;shell<2;shell++)for(int z=0;z<rows;z++)for(int a=0;a<=arc;a++){
                float depth=-16+z*2f,angle=a*Mathf.PI/arc;
                float ripple=(Mathf.PerlinNoise(a*.32f,z*.21f)-.5f)*(shell==0?.28f:1.2f);
                float x=Mathf.Cos(angle)*(shell==0?5.7f:10.2f)+ripple;
                float y=Mathf.Sin(angle)*(shell==0?6.9f:10.5f)-.2f;
                if(shell==1){
                    // The outside is an asymmetric rock mound, not an extruded half-pipe.
                    float crag=Mathf.PerlinNoise(a*.49f+2.3f,z*.31f+1.7f);
                    x=Mathf.Cos(angle)*(11.2f+2.2f*crag)+Mathf.Sin(angle)*Mathf.Sin(z*.38f)*1.1f;
                    y=Mathf.Sin(angle)*(11.4f+3.8f*crag+1.2f*Mathf.Sin(z*.3f))-.2f;
                    depth=-20+z*2.5f+Mathf.Sin(angle)*Mathf.Sin(a*.81f+z*.39f)*.9f;
                    float edgeGround=Ground(root.TransformPoint(new Vector3(x,0,depth)))-root.position.y-.3f;
                    y+=edgeGround*Mathf.Pow(1-Mathf.Sin(angle),2);
                }
                vertices.Add(new Vector3(x,y,depth));uv.Add(new Vector2(a*.27f,depth*.23f));
            }
            int stride=arc+1,layer=rows*stride;
            for(int shell=0;shell<2;shell++)for(int z=0;z<rows-1;z++)for(int a=0;a<arc;a++){
                int i=shell*layer+z*stride+a;Quad(i,i+1,i+stride+1,i+stride,shell==0);
            }
            // Cap vertices need their own planar UVs: shared arc/depth UVs gave
            // both radii identical coordinates and stretched the mouth into radial streaks.
            for(int a=0;a<arc;a++){Cap(a,a+layer,a+1+layer,a+1,true,false);int i=(rows-1)*stride+a;Cap(i,i+1,i+1+layer,i+layer,true,false);}
            for(int z=0;z<rows-1;z++)foreach(int a in new[]{0,arc}){int i=z*stride+a;Cap(i,i+stride,i+stride+layer,i+layer,a==0,true);}
            var mesh=new Mesh{name="Cave_Hollow_Rock_Shell"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            mesh=SaveCaveMesh(mesh,"Cave_Hollow_Rock_Shell");
            var node=Node(root,"Hollow_Rock_Shell",Vector3.zero);node.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;node.gameObject.AddComponent<MeshRenderer>().sharedMaterial=mat;node.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
            void Quad(int a,int b,int c,int d,bool reverse){if(reverse){indices.AddRange(new[]{a,c,b,a,d,c});}else indices.AddRange(new[]{a,b,c,a,c,d});}
            void Cap(int a,int b,int c,int d,bool reverse,bool bottom){int start=vertices.Count;foreach(int index in new[]{a,b,c,d}){var v=vertices[index];vertices.Add(v);uv.Add(new Vector2(v.x*.23f,(bottom?v.z:v.y)*.23f));}Quad(start,start+1,start+2,start+3,reverse);}
        }
        static Mesh SaveCaveMesh(Mesh mesh,string key)
        {
            string path=Folder+"/Meshes/"+key+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(old==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
            // Native channel setters refresh both GPU data and MeshCollider cooking.
            old.Clear();old.vertices=mesh.vertices;old.uv=mesh.uv;old.normals=mesh.normals;old.tangents=mesh.tangents;old.triangles=mesh.triangles;old.bounds=mesh.bounds;old.UploadMeshData(false);
            Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;
        }
        static void BuildCaveRamp(Transform root,Material material)
        {
            const int rows=24,columns=6;var v=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
            for(int row=0;row<=rows;row++)for(int col=0;col<=columns;col++){
                float t=row/(float)rows,x=Mathf.Lerp(1.4f,3,t)*(col/(float)columns*2-1),z=Mathf.Lerp(-40,-16,t);
                float start=Ground(root.TransformPoint(new Vector3(x,0,-40)))+.04f;
                var p=root.TransformPoint(new Vector3(x,0,z));float ground=Ground(p);
                float y=Mathf.Max(Mathf.Lerp(start,root.position.y,t),ground+.04f)-root.position.y;
                if(row==rows)y=0;
                v.Add(new Vector3(x,y,z));uv.Add(new Vector2(x*.22f,z*.22f));
            }
            for(int row=0;row<rows;row++)for(int col=0;col<columns;col++){
                int i=row*(columns+1)+col;indices.AddRange(new[]{i,i+columns+1,i+1,i+1,i+columns+1,i+columns+2});
            }
            var mesh=new Mesh{name="Cave_Entrance_Stone_Ramp"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh=SaveCaveMesh(mesh,"Cave_Entrance_Stone_Ramp");
            var node=Node(root,"Cave_Entrance_Stone_Ramp",Vector3.zero);node.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;node.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;node.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
        static float CaveWalkSurface(Transform root,Vector3 p)
        {
            float y=Ground(p);
            foreach(var hit in Physics.RaycastAll(new Vector3(p.x,2200,p.z),Vector3.down,4400,~0,QueryTriggerInteraction.Ignore))
                if(hit.collider.transform.IsChildOf(root)&&(hit.collider.name=="Cave_Walkable_Floor"||hit.collider.name=="Cave_Entrance_Stone_Ramp"))y=Mathf.Max(y,hit.point.y);
            return y;
        }
        static void RemoveOverlappingPlaceholders(Transform root,WorldMacroLandmarkSheetSO.Landmark spec)
        {
            var massing=GameObject.Find("03_SettlementAndLandmark_Massing");if(massing==null)return;
            foreach(Transform site in massing.transform)foreach(Transform child in site){
                if(!child.gameObject.activeSelf)continue;var rr=child.GetComponentsInChildren<Renderer>();if(rr.Length==0)continue;
                var b=rr[0].bounds;foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);var p=root.InverseTransformPoint(b.center);
                if(Mathf.Abs(p.x)<spec.CourtyardSize.x*.5f+5&&Mathf.Abs(p.z)<spec.CourtyardSize.y*.5f+5)child.gameObject.SetActive(false);
            }
        }
        [Serializable]class Report{public string id,scope="Static appearance and authored access. User walking, full interior and boss content unverified.";public Vector3 position;public Vector2 courtyard;public float roadClearance,riverClearance,terrainRelief,caveMinimumFloorClearance,caveRampMaxGrade;public int lod0Triangles,allStoredTriangles,renderers,colliders,missingMaterials;public bool entryPresent,dryFootprint;}
        static void AuditOne(Transform root,WorldMacroLandmarkSheetSO.Landmark spec)
        {
            var filters=root.GetComponentsInChildren<MeshFilter>(true);var renderers=root.GetComponentsInChildren<Renderer>(true);
            var report=new Report{id=spec.Id,position=root.position,courtyard=spec.CourtyardSize,roadClearance=RoadClearance(root.position),renderers=renderers.Length,colliders=root.GetComponentsInChildren<Collider>(true).Length,missingMaterials=renderers.Count(r=>r.sharedMaterials.Any(m=>m==null)),entryPresent=root.Find("Entry")!=null};
            TerrainFootprint(root,spec.CourtyardSize,out float low,out float high,out bool dry);report.terrainRelief=high-low;report.dryFootprint=dry;
            report.riverClearance=RiverClearance(root.position)-spec.CourtyardSize.magnitude*.5f;
            if(spec.Id=="Cave"){
                report.caveMinimumFloorClearance=float.MaxValue;
                for(int z=-16;z<=16;z++)for(int x=-4;x<=4;x++){
                    var p=root.TransformPoint(new Vector3(x,0,z));report.caveMinimumFloorClearance=Mathf.Min(report.caveMinimumFloorClearance,CaveWalkSurface(root,p)-Ground(p));
                }
                Vector3 prior=root.TransformPoint(new Vector3(0,0,-40));prior.y=CaveWalkSurface(root,prior);
                for(int z=-39;z<=-16;z++){
                    var p=root.TransformPoint(new Vector3(0,0,z));p.y=CaveWalkSurface(root,p);
                    report.caveRampMaxGrade=Mathf.Max(report.caveRampMaxGrade,Mathf.Atan2(Mathf.Abs(p.y-prior.y),new Vector2(p.x-prior.x,p.z-prior.z).magnitude)*Mathf.Rad2Deg);prior=p;
                }
            }
            foreach(var f in filters){if(f.sharedMesh==null)continue;int tris=0;for(int i=0;i<f.sharedMesh.subMeshCount;i++)tris+=(int)f.sharedMesh.GetIndexCount(i)/3;report.allStoredTriangles+=tris;
                bool lower=false;for(var p=f.transform;p!=root&&p!=null;p=p.parent)if(p.name=="LOD1"||p.name=="LOD2")lower=true;if(!lower)report.lod0Triangles+=tris;}
            File.WriteAllText(Output+"/"+spec.Id+"_setup.json",JsonUtility.ToJson(report,true));
        }
        public static string Visit(string id)
        {
            RequireScene();var root=GameObject.Find(RootName)?.transform.Find(id);if(root==null)throw new Exception("Landmark not installed "+id);
            var view=Object.FindFirstObjectByType<WorldMacroReviewController>();var entry=root.Find("Entry");
            view.transform.SetPositionAndRotation(entry.position+Vector3.up*view.EyeHeight,entry.rotation);
            if(view.WalkBody!=null){view.WalkBody.enabled=false;view.WalkBody.transform.SetPositionAndRotation(entry.position,entry.rotation);view.WalkBody.enabled=true;}view.Mode=1;
            WorldMacroWaterAuthoring.SaveScene();return "Capsule ready at "+id+". Play for WASD + right mouse look.";
        }
        public static string RefineSurfaces()
        {
            RequireScene();var shader=Shader.Find("Oheangbu/WorldMacroTexturedSurface");if(shader==null)throw new Exception("Import textured surface shader first");
            var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(m=>m.message)));
            foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{Folder+"/Materials"})){
                var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));mat.shader=shader;EditorUtility.SetDirty(mat);
            }
            var group=GameObject.Find(RootName).transform;var sheet=LoadSheet();
            foreach(var s in sheet.Landmarks){var root=group.Find(s.Id);if(root==null)continue;
                var entry=root.Find("Entry");if(s.Id!="Cave"){
                    var p=root.TransformPoint(new Vector3(0,0,-s.CourtyardSize.y*.5f-43));p.y=Ground(p)+.04f;entry.position=p;
                }
                PrefabUtility.SaveAsPrefabAsset(root.gameObject,Folder+"/Prefabs/"+s.Id+".prefab");AuditOne(root,s);
            }
            AssetDatabase.SaveAssets();WorldMacroWaterAuthoring.SaveScene();return "Textured macro materials and entrance inspection points updated; lighting/geography unchanged.";
        }
        [Serializable]class Check{public string name,status,detail;}
        [Serializable]class Checks{public string scope="Static authored geometry and capsule-volume probes only; no walking, combat or runtime LOD validation.";public Check[] checks;}
        public static string Audit()
        {
            RequireScene();Physics.SyncTransforms();var checks=new List<Check>();var group=GameObject.Find(RootName).transform;var sheet=LoadSheet();
            foreach(var s in sheet.Landmarks){var root=group.Find(s.Id);if(root==null){Add(s.Id+" installed",false,"Missing root");continue;}
                float clearance=RoadClearance(root.position)-s.CourtyardSize.magnitude*.5f;
                Add(s.Id+" existing-route clearance",clearance>=0,clearance.ToString("F2")+"m beyond footprint bounding radius");
                float river=RiverClearance(root.position)-s.CourtyardSize.magnitude*.5f;
                TerrainFootprint(root,s.CourtyardSize,out float low,out float high,out bool dry);
                Add(s.Id+" river and dry-ground clearance",river>=8&&dry,river.ToString("F2")+"m beyond nominal bank / dry footprint "+dry+" / actual terrain relief "+(high-low).ToString("F2")+"m");
                var entry=root.Find("Entry");float gap=entry.position.y-Ground(entry.position);
                Add(s.Id+" entry ground",Mathf.Abs(gap-.04f)<.03f,gap.ToString("F3")+"m");
                var collisions=new List<string>();
                if(s.Id=="Cave")for(int z=-40;z<=14;z+=3){var p=root.TransformPoint(new Vector3(0,0,z));p.y=CaveWalkSurface(root,p)+.06f;Probe(p);}
                else for(int z=-2;z<=2;z++){var p=root.TransformPoint(new Vector3(0,.06f,-s.CourtyardSize.y*.5f+z));Probe(p);}
                Add(s.Id+" entry passage capsule-volume",collisions.Count==0,string.Join(",",collisions.Distinct()));
                AuditOne(root,s);
                void Probe(Vector3 feet){foreach(var c in Physics.OverlapCapsule(feet+Vector3.up*.28f,feet+Vector3.up*1.47f,.28f,~0,QueryTriggerInteraction.Ignore))if(c.transform.IsChildOf(root))collisions.Add(c.name);}
            }
            var shader=Shader.Find("Oheangbu/WorldMacroTexturedSurface");var messages=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();Add("textured shader compilation",messages.Length==0,string.Join(";",messages.Select(m=>m.message)));
            var camera=Object.FindFirstObjectByType<WorldMacroReviewController>();Add("capsule dimensions",camera.WalkBody!=null&&Mathf.Abs(camera.WalkBody.height-1.75f)<.001f&&Mathf.Abs(camera.WalkBody.radius-.28f)<.001f&&Mathf.Abs(camera.EyeHeight-1.62f)<.001f,"1.75m height / 0.56m diameter / 1.62m eye");
            File.WriteAllText(Output+"/clearance_audit.json",JsonUtility.ToJson(new Checks{checks=checks.ToArray()},true));
            return string.Join("\n",checks.Select(c=>c.status+" "+c.name+" "+c.detail));
            void Add(string name,bool pass,string detail)=>checks.Add(new Check{name=name,status=pass?"PASS":"FAIL",detail=detail});
        }
    }
}
