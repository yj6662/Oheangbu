using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.BrushRender;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMacroBuilder
    {
        public const string ScenePath="Assets/_Project/Scenes/World/W_WorldMacro_Blockout.unity";
        public const string Folder="Assets/_Project/Art/World/WorldMacro";
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/World/WorldMacro"));
        public const string SheetPath=Folder+"/WorldMacroSheet.asset";
        public static WorldMacroSheetSO Sheet=>AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(SheetPath);
        [MenuItem("Oheangbu/Legacy/복구 전용/Build isolated blockout")]
        static void MenuBuild()=>Debug.Log(Build());
        public static string Build()
        {
            if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
            if(File.Exists(ScenePath))throw new Exception("Macro scene already exists. Edit its sheet and use Rebuild explicitly; existing C2/Prologue are never inputs.");
            Directory.CreateDirectory(Output);Directory.CreateDirectory(Folder+"/Meshes");
            var prior=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(prior.isDirty)EditorSceneManager.SaveScene(prior,Output+"/BeforeMacro_"+DateTime.Now.ToString("yyyyMMddHHmmss")+".unity",true);
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var sheet=Save(WorldMacroSeed.Create(),"WorldMacroSheet.asset");
            Construct(sheet);
            Object.FindFirstObjectByType<WorldMacroWaterClock>()?.RestoreAuthoredMaterial();
            RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Sky.mat");
            EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            Object.FindFirstObjectByType<WorldLookDriver>()?.Apply();
            Object.FindFirstObjectByType<WorldMacroWaterClock>()?.Rebind();
            Export();return "Built "+ScenePath+". Macro inspection only; no gameplay/save/vehicle integration.";
        }
        public static string Rebuild()
        {
            if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
            if(!File.Exists(ScenePath)||Sheet==null)throw new Exception("Build first");
            if(File.ReadAllText(ScenePath).Contains("Macro_PlayerCapsule")||File.ReadAllText(ScenePath).Contains("WorldMacro_Landmarks_Authored"))
                throw new Exception("Macro contains authored player/landmark content. Full regeneration is disabled; use incremental authoring to preserve this scene.");
            Directory.CreateDirectory(Output+"/Backups");
            var current=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(current.path==ScenePath)EditorSceneManager.SaveScene(current,Output+"/Backups/Macro_"+DateTime.Now.ToString("yyyyMMddHHmmss")+".unity",true);
            else if(current.isDirty)EditorSceneManager.SaveScene(current,Output+"/Backups/Other_"+DateTime.Now.ToString("yyyyMMddHHmmss")+".unity",true);
            // Only the macro authoring output is regenerated. The serialized sheet is retained.
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Construct(Sheet);Object.FindFirstObjectByType<WorldMacroWaterClock>()?.RestoreAuthoredMaterial();RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Sky.mat");EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();Object.FindFirstObjectByType<WorldLookDriver>()?.Apply();Object.FindFirstObjectByType<WorldMacroWaterClock>()?.Rebind();Export();return "Rebuilt macro outputs from existing sheet; backup retained.";
        }
        public static T Save<T>(T value,string relative) where T:Object
        {
            string path=Folder+"/"+relative;var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old==null){AssetDatabase.CreateAsset(value,path);return value;}
            if(old is Mesh a && value is Mesh b){a.Clear();a.indexFormat=b.indexFormat;a.vertices=b.vertices;a.normals=b.normals;a.uv=b.uv;a.triangles=b.triangles;a.RecalculateBounds();a.UploadMeshData(false);}
            else EditorUtility.CopySerialized(value,old);
            EditorUtility.SetDirty(old);Object.DestroyImmediate(value);return old;
        }
        static Material CopyMaterial(string source,string name)
        {
            var original=AssetDatabase.LoadAssetAtPath<Material>(source);if(original==null)throw new Exception("Missing reference material "+source);
            var m=new Material(original){name=name,enableInstancing=true};m.SetFloat("_GroundPath",0);m.SetFloat("_WashStart",800);m.SetFloat("_WashEnd",9500);m.SetFloat("_WashStrength",.58f);
            return Save(m,name+".mat");
        }
        static void Construct(WorldMacroSheetSO s)
        {
            var root=new GameObject("WorldMacro_AuthoredGeography").transform;
            var ground=CopyMaterial("Assets/_Project/Art/World/Prologue/Ground.mat","MacroGround");
            ground.shader=Shader.Find("Oheangbu/WorldMacroTerrain");
            if(ground.shader==null)throw new Exception("Missing isolated macro terrain shader");
            ground.SetFloat("_GroundPath",1);ground.SetTexture("_GroundPathMask",BuildRoadMask(s));
            ground.SetVector("_GroundPathRect",new Vector4(s.BoundsMin.x,s.BoundsMin.y,1/(s.BoundsMax.x-s.BoundsMin.x),1/(s.BoundsMax.y-s.BoundsMin.y)));
            ground.SetFloat("_NoiseScale",.07f);ground.SetFloat("_StrokeScale",.065f);ground.SetFloat("_StrokeStrength",.1f);ground.SetFloat("_BrushStrength",.075f);
            // Macro-only pigment adjustment: retain the approved sky, lighting and distance wash.
            ground.SetFloat("_ToneCeiling",.25f);ground.SetFloat("_InkDensity",1.75f);
            ground.SetVector("_GroundPathTones",new Vector4(.07f,.18f,0,0));
            ground.SetTexture("_RealmPigment",WorldMacroPalette.Build(s));ground.SetFloat("_RealmTintStrength",.38f);
            var terrain=Group(root,"01_GlobalTerrain_IndependentOfRoads");int n=0;
            for(float z=s.BoundsMin.y;z<s.BoundsMax.y;z+=s.ChunkSize)for(float x=s.BoundsMin.x;x<s.BoundsMax.x;x+=s.ChunkSize){
                var rect=new Rect(x,z,Mathf.Min(s.ChunkSize,s.BoundsMax.x-x),Mathf.Min(s.ChunkSize,s.BoundsMax.y-z));
                var mesh=WorldMacroTerrain.BuildTerrainMesh(s,rect,s.GridSpacing,true);
                if(mesh.triangles.Length==0){Object.DestroyImmediate(mesh);continue;}
                mesh.name="Terrain_"+n.ToString("D3");mesh=Save(mesh,"Meshes/"+mesh.name+".asset");MeshObject(terrain,mesh.name,mesh,ground,true);n++;
            }
            WorldMacroContext.Build(s,root,ground);
            var water=CopyMaterial("Assets/_Project/Art/World/Prologue/Stream.mat","MacroWater");
            var riverRoot=Group(root,"02_Drainage_WaterSurface");
            foreach(var river in s.Rivers){var mesh=Ribbon(river.Points,river.Width);mesh.name="Water_"+river.Id;MeshObject(riverRoot,mesh.name,Save(mesh,"Meshes/"+mesh.name+".asset"),water,false);}
            if(AssetDatabase.LoadAssetAtPath<Shader>(WorldMacroWaterAuthoring.GraphPath)!=null)WorldMacroWaterAuthoring.Configure(riverRoot,s);
            var stone=CopyMaterial("Assets/_Project/Art/World/Prologue/Ground.mat","MacroStone");stone.SetFloat("_ToneCeiling",.46f);
            var plaster=CopyMaterial("Assets/_Project/Art/World/Prologue/Ground.mat","MacroPlaster");plaster.SetFloat("_ToneFloor",.26f);plaster.SetFloat("_ToneCeiling",.82f);
            var roof=CopyMaterial("Assets/_Project/Art/World/Prologue/Ground.mat","MacroRoof");roof.SetFloat("_ToneFloor",.02f);roof.SetFloat("_ToneCeiling",.24f);
            var wood=CopyMaterial("Assets/_Project/Art/World/Prologue/Ground.mat","MacroTimber");wood.SetFloat("_UseWoodColor",1);wood.SetFloat("_TintRetain",.55f);wood.SetFloat("_ToneCeiling",.38f);
            var sites=Group(root,"03_SettlementAndLandmark_Massing");
            foreach(var site in s.Sites){
                var group=Group(sites,site.Id);group.position=site.Position;
                if(site.Kind=="Bridge"){BuildBridge(group,s,site,stone);continue;}
                bool city=site.Id=="Hwanggyeong"||site.Id=="Hyeongang"||site.Id=="Cheolong";
                if(city){BuildSettlement(group,s,site,site.Id=="Hwanggyeong"?48:28,plaster,roof,stone);}
                else if(site.Id=="Tree"){BuildSacredTree(group,s,new WorldMacroSheetSO.SiteSpec{Id=site.Id,Position=LandmarkBesidePath(s,site,26)},wood);}
                else if(site.Id=="SouthGate"){Gate(group,s,site,stone,roof);}
                else if(site.Kind.IndexOf("Pass",StringComparison.OrdinalIgnoreCase)>=0||site.Id.EndsWith("Pass")){/* Landform itself marks a pass; no artificial tower. */}
                else if(site.Id=="Dragon"){Ring(group,s,site.Position,32,stone);}
                else if(site.Id=="Mine"){House(group,LandmarkBesidePath(s,site,9),new Vector3(9,4,7),wood,roof);}
                else {House(group,LandmarkBesidePath(s,site,site.Id=="Inn"?14:11),new Vector3(site.Id=="Inn"?19:13,5,10),plaster,roof);}
            }
            BuildForest(Group(root,"04_ForestExtent_PlaceholderClusters"),s,wood);
            SetupEnvironment(s);
        }
        static Vector3 LandmarkBesidePath(WorldMacroSheetSO s,WorldMacroSheetSO.SiteSpec site,float radius)
        {
            // A POI is an arrival courtyard, not the middle of a solid placeholder house.
            // Move only the massing; keep all route endpoints and geographic terrain unchanged.
            var best=site.Position;float bestScore=float.MaxValue;
            for(int ring=0;ring<4;ring++)for(int i=0;i<16;i++)
            {
                float angle=i*Mathf.PI*2/16;float range=radius+10+ring*12;
                var p=site.Position+new Vector3(Mathf.Cos(angle)*range,0,Mathf.Sin(angle)*range);
                if(!WorldMacroTerrain.Contains(s,p.x,p.z))continue;
                bool blocked=false;foreach(var route in s.Routes)
                {for(int k=1;k<route.Points.Length;k++)if(WorldMacroTerrain.SegmentDistance(p.x,p.z,route.Points[k-1],route.Points[k],out _) < radius+route.Width*.5f+3){blocked=true;break;}if(blocked)break;}
                if(blocked)continue;
                p.y=WorldMacroTerrain.SurfaceHeight(s,p.x,p.z);
                float grade=Vector3.Angle(WorldMacroTerrain.Normal(s,p.x,p.z),Vector3.up);
                float score=grade*4+range;
                if(score<bestScore){best=p;bestScore=score;}
            }
            if(bestScore==float.MaxValue)throw new Exception("No clear courtyard-side position for "+site.Id);
            return best;
        }
        static Texture2D BuildRoadMask(WorldMacroSheetSO s)
        {
            int w=2048,h=3072;var bytes=new byte[w*h];
            float dx=(s.BoundsMax.x-s.BoundsMin.x)/(w-1),dz=(s.BoundsMax.y-s.BoundsMin.y)/(h-1);
            foreach(var r in s.Routes)for(int i=1;i<r.Points.Length;i++){
                Vector3 a=r.Points[i-1],b=r.Points[i];float length=Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));int steps=Mathf.CeilToInt(length/Mathf.Min(dx,dz));
                float half=r.Width*.5f;int rx=Mathf.CeilToInt((half+5)/dx),rz=Mathf.CeilToInt((half+5)/dz);
                for(int j=0;j<=steps;j++){var p=Vector3.Lerp(a,b,steps==0?0:j/(float)steps);int cx=Mathf.RoundToInt((p.x-s.BoundsMin.x)/dx),cz=Mathf.RoundToInt((p.z-s.BoundsMin.y)/dz);
                    for(int v=-rz;v<=rz;v++)for(int u=-rx;u<=rx;u++){int x=cx+u,z=cz+v;if(x<0||z<0||x>=w||z>=h)continue;float d=Mathf.Sqrt(u*u*dx*dx+v*v*dz*dz);byte value=(byte)(255*(1-Mathf.SmoothStep(0,1,(d-half)/4f)));int index=z*w+x;if(value>bytes[index])bytes[index]=value;}
                }
            }
            string path=Folder+"/RoadPigment.asset";var t=AssetDatabase.LoadAssetAtPath<Texture2D>(path);bool fresh=t==null;if(fresh)t=new Texture2D(w,h,TextureFormat.R8,false,true);else t.Reinitialize(w,h,TextureFormat.R8,false);
            t.name="Road pigment on shared terrain";t.SetPixelData(bytes,0);t.Apply();t.wrapMode=TextureWrapMode.Clamp;t.filterMode=FilterMode.Bilinear;if(fresh)AssetDatabase.CreateAsset(t,path);else EditorUtility.SetDirty(t);return t;
        }
        static void SetupEnvironment(WorldMacroSheetSO s)
        {
            var sun=new GameObject("Macro_Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.color=Color.white;sun.intensity=.8f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(40,-35,0);RenderSettings.sun=sun;RenderSettings.fog=false;
            var profile=Save(Object.Instantiate(AssetDatabase.LoadAssetAtPath<InkSkyProfile>("Assets/_Project/Art/World/Prologue/SkyProfile.asset")),"SkyProfile.asset");
            var sky=Save(new Material(Shader.Find("Oheangbu/Ink Cloud Sky")),"Sky.mat");profile.Apply(sky);profile.ApplyEnvironment();RenderSettings.skybox=sky;
            var cam=new GameObject("Macro_Review_Camera").AddComponent<Camera>();cam.tag="MainCamera";cam.nearClipPlane=.3f;cam.farClipPlane=22000;cam.fieldOfView=60;cam.clearFlags=CameraClearFlags.Skybox;cam.allowHDR=true;cam.AddListener();
            var data=cam.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.antialiasingQuality=AntialiasingQuality.High;data.requiresDepthTexture=true;data.requiresColorTexture=true;
            var viewer=cam.gameObject.AddComponent<WorldMacroReviewController>();viewer.Sheet=s;
            var anchor=s.FindSite("Inn")??s.Sites[0];var p=anchor.Position+new Vector3(0,0,-60);p.y=WorldMacroTerrain.SurfaceHeight(s,p.x,p.z)+1.8f;cam.transform.SetPositionAndRotation(p,Quaternion.LookRotation(anchor.Position+Vector3.up*5-p));
            var driver=new GameObject("Macro_WorldLook").AddComponent<WorldLookDriver>();Set(driver,"_palette",AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(DevSceneKit.PalettePath));Set(driver,"_skyCamera",cam);Set(driver,"_useSkybox",true);Set(driver,"_skyboxMaterial",sky);Set(driver,"_inkSkyProfile",profile);driver.Apply();
            WorldMacroSkyAuthoring.Configure(driver,s,profile);
            var volume=new GameObject("Macro_Subtle_Post").AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;
            string pp=Folder+"/Post.asset";var post=AssetDatabase.LoadAssetAtPath<VolumeProfile>(pp);
            if(post==null){AssetDatabase.CopyAsset("Assets/_Project/Art/World/Prologue/Post.asset",pp);post=AssetDatabase.LoadAssetAtPath<VolumeProfile>(pp);}volume.sharedProfile=post;
        }
        static void AddListener(this Camera c)=>c.gameObject.AddComponent<AudioListener>();
        public static void Set(Object target,string field,Object value){var so=new SerializedObject(target);so.FindProperty(field).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
        public static void Set(Object target,string field,bool value){var so=new SerializedObject(target);so.FindProperty(field).boolValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
        static Transform Group(Transform parent,string name){var g=new GameObject(name).transform;g.SetParent(parent);return g;}
        static GameObject MeshObject(Transform parent,string name,Mesh mesh,Material material,bool collider)
        {var g=new GameObject(name);g.transform.SetParent(parent);g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=material;if(collider)g.AddComponent<MeshCollider>().sharedMesh=mesh;return g;}
        static GameObject Cube(Transform parent,string name,Vector3 position,Vector3 size,Material material)
        {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent);go.transform.position=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;return go;}
        static void House(Transform parent,Vector3 floor,Vector3 size,Material wall,Material roof)
        {
            Cube(parent,"Massing_Walls",floor+Vector3.up*(size.y/2),size,wall);
            // Pitched roof keeps settlement silhouettes legible at eye level, with no detailed architecture claim.
            for(int sign=-1;sign<=1;sign+=2){var p=floor+new Vector3(sign*size.x*.26f,size.y+size.x*.13f,0);var o=Cube(parent,"Massing_Roof",p,new Vector3(size.x*.61f,.8f,size.z*1.2f),roof);o.transform.rotation=Quaternion.Euler(0,0,-sign*24);}
        }
        static void BuildSettlement(Transform parent,WorldMacroSheetSO s,WorldMacroSheetSO.SiteSpec site,int count,Material wall,Material roof,Material stone)
        {
            var random=new System.Random(s.Seed+site.Id.Length*91);
            bool capital=site.Id=="Hwanggyeong";
            var gate=s.FindSite("SouthGate");
            var local=capital?new[]{new Vector2(-320,-70),new Vector2(-360,100),new Vector2(-220,260),new Vector2(20,300),new Vector2(230,190),new Vector2(300,10),new Vector2(235,-200),new Vector2(160,-450),new Vector2(0,0),new Vector2(0,0),new Vector2(-150,-520),new Vector2(-270,-300)}
                :new[]{new Vector2(-185,-45),new Vector2(-170,125),new Vector2(-25,180),new Vector2(150,110),new Vector2(190,-35),new Vector2(85,-165),new Vector2(-70,-195)};
            var polygon=new Vector2[local.Length];
            for(int i=0;i<local.Length;i++)polygon[i]=local[i]+new Vector2(site.Position.x,site.Position.z);
            if(capital&&gate!=null){polygon[8]=new Vector2(gate.Position.x+11,gate.Position.z);polygon[9]=new Vector2(gate.Position.x-11,gate.Position.z);}
            float minX=polygon.Min(p=>p.x),maxX=polygon.Max(p=>p.x),minZ=polygon.Min(p=>p.y),maxZ=polygon.Max(p=>p.y);
            var placed=new List<Vector3>();var radii=new List<float>();
            bool NearRoad(float x,float z,float clearance)
            {
                foreach(var route in s.Routes)for(int k=1;k<route.Points.Length;k++)
                {float t;if(WorldMacroTerrain.SegmentDistance(x,z,route.Points[k-1],route.Points[k],out t)<route.Width*.5f+clearance)return true;}
                return false;
            }
            for(int attempt=0;attempt<count*40&&placed.Count<count;attempt++)
            {
                float x=Mathf.Lerp(minX,maxX,(float)random.NextDouble()),z=Mathf.Lerp(minZ,maxZ,(float)random.NextDouble());
                float w=10+(float)random.NextDouble()*15,depth=12+(float)random.NextDouble()*12,radius=Mathf.Sqrt(w*w+depth*depth)*.5f;
                if(!Inside(polygon,x,z)||NearRoad(x,z,radius+4f))continue;
                bool fit=true;float lo=float.MaxValue,hi=float.MinValue;
                for(int dx=-1;dx<=1;dx+=2)for(int dz=-1;dz<=1;dz+=2)
                {
                    float px=x+dx*(w*.5f+2f),pz=z+dz*(depth*.5f+2f);
                    if(!Inside(polygon,px,pz))fit=false;
                    float y=WorldMacroTerrain.SurfaceHeight(s,px,pz);lo=Mathf.Min(lo,y);hi=Mathf.Max(hi,y);
                }
                if(!fit||hi-lo>2.5f)continue;
                for(int i=0;i<placed.Count;i++)if(Vector2.Distance(new Vector2(x,z),new Vector2(placed[i].x,placed[i].z))<radius+radii[i]+4f){fit=false;break;}
                if(!fit)continue;
                var p=new Vector3(x,hi,z);placed.Add(p);radii.Add(radius);
                Cube(parent,"Massing_Foundation",new Vector3(x,(lo+hi)*.5f-.2f,z),new Vector3(w+.6f,Mathf.Max(.4f,hi-lo+.4f),depth+.6f),stone);
                House(parent,p,new Vector3(w,5+(float)random.NextDouble()*5,depth),wall,roof);
            }
            if(capital||site.Id=="Cheolong")for(int i=0;i<polygon.Length;i++)
            {
                if(capital&&i==8)continue; // Both ends meet the existing gate piers; the central arch stays open.
                var a=polygon[i];var b=polygon[(i+1)%polygon.Length];int steps=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)/24f));
                for(int k=0;k<steps;k++)
                {
                    var pa=Vector2.Lerp(a,b,k/(float)steps);var pb=Vector2.Lerp(a,b,(k+1)/(float)steps);var mid=(pa+pb)*.5f;
                    // Reserve every authored passage, including approaches immediately beside the gate.
                    if(NearRoad(mid.x,mid.y,Vector2.Distance(pa,pb)*.5f+3f))continue;
                    float py=WorldMacroTerrain.SurfaceHeight(s,pa.x,pa.y),qy=WorldMacroTerrain.SurfaceHeight(s,pb.x,pb.y);
                    var p=new Vector3(mid.x,(py+qy)*.5f+5,mid.y);
                    var o=Cube(parent,"Massing_CityWall",p,new Vector3(3,10+Mathf.Abs(py-qy),Vector2.Distance(pa,pb)+.6f),stone);
                    o.transform.rotation=Quaternion.LookRotation(new Vector3(pb.x-pa.x,0,pb.y-pa.y));
                }
            }
        }
        static void Gate(Transform parent,WorldMacroSheetSO s,WorldMacroSheetSO.SiteSpec site,Material stone,Material roof)
        {
            // Align the passage to the actual arriving street, not the world's north axis.
            var axis=Vector3.forward;var route=s.Routes.FirstOrDefault(r=>r.Id=="Road_Gate_CapitalReservation");
            if(route!=null)foreach(var point in route.Points)
            {var d=point-site.Position;d.y=0;if(d.magnitude>5){axis=d.normalized;break;}}
            var frame=Group(parent,"GateStructure");frame.position=Vector3.zero;
            Cube(frame,"Gate_Left",new Vector3(-10,5,0),new Vector3(6,10,8),stone);
            Cube(frame,"Gate_Right",new Vector3(10,5,0),new Vector3(6,10,8),stone);
            House(frame,Vector3.up*10,new Vector3(28,5,11),stone,roof);
            frame.position=site.Position;frame.rotation=Quaternion.LookRotation(axis);
        }
        static void Ring(Transform parent,WorldMacroSheetSO s,Vector3 center,float radius,Material stone)
        {for(int i=0;i<16;i++){float a=i*Mathf.PI/8;var p=center+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius);p.y=WorldMacroTerrain.SurfaceHeight(s,p.x,p.z);Cube(parent,"Sanctuary_Stone",p+Vector3.up*2,new Vector3(3,4,2),stone);}}
        static void BuildBridge(Transform parent,WorldMacroSheetSO s,WorldMacroSheetSO.SiteSpec site,Material stone)
        {
            var plan=WorldMacroBridgeGeometry.Create(s,site);var direction=plan.Axis;
            var center=site.Position;center.y=plan.DeckTop-.9f;
            var bridge=Cube(parent,"Bridge_Deck_TEST",center,new Vector3(9,1.8f,plan.DeckHalfLength*2),stone);bridge.transform.rotation=Quaternion.LookRotation(direction);
            for(int i=-1;i<=1;i++){var p=center+direction*i*(plan.DeckHalfLength-45)*.8f;float bed=WorldMacroTerrain.SurfaceHeight(s,p.x,p.z);float height=Mathf.Max(1,p.y-bed);Cube(parent,"Bridge_Pier",new Vector3(p.x,bed+height/2,p.z),new Vector3(5,height,5),stone);}
            // Short solid end approaches remove controller-blocking deck lips. Terrain is untouched.
            foreach(int sign in new[]{-1,1})
            {
                var v=new List<Vector3>();var t=new List<int>();var side=new Vector3(direction.z,0,-direction.x);const int rows=16,cols=5;
                for(int z=0;z<rows;z++)for(int x=0;x<cols;x++)
                {
                    float along=sign*(plan.DeckHalfLength-.1f+(WorldMacroBridgeGeometry.Plan.ApronLength+.1f)*z/(rows-1f));
                    float across=-4.5f+x*9f/(cols-1f);var p=center+direction*along+side*across;
                    float ground=WorldMacroTerrain.SurfaceHeight(s,p.x,p.z);
                    p.y=z==0?plan.DeckTop:plan.ApronHeight(along,across,ground);v.Add(p);
                }
                for(int z=0;z<rows-1;z++)for(int x=0;x<cols-1;x++)
                {int a=z*cols+x,b=a+1,c=a+cols,d=c+1;Triangle(a,c,b);Triangle(b,c,d);}
                // Retaining sides visibly support the raised surface instead of leaving a floating strip.
                foreach(int edge in new[]{0,cols-1})for(int z=0;z<rows-1;z++)
                {
                    int a=z*cols+edge,b=(z+1)*cols+edge;var pa=v[a];var pb=v[b];
                    pa.y=WorldMacroTerrain.SurfaceHeight(s,pa.x,pa.z)-.05f;pb.y=WorldMacroTerrain.SurfaceHeight(s,pb.x,pb.z)-.05f;
                    int c=v.Count;v.Add(pa);v.Add(pb);t.AddRange(new[]{a,b,c,b,c+1,c});
                }
                void Triangle(int a,int b,int c){if(Vector3.Cross(v[b]-v[a],v[c]-v[a]).y<0){int swap=b;b=c;c=swap;}t.AddRange(new[]{a,b,c});}
                var mesh=new Mesh{name="BridgeApron_"+site.Id+"_"+sign};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                MeshObject(parent,"Bridge_Apron_TEST_"+sign,Save(mesh,"Meshes/"+mesh.name+".asset"),stone,true);
            }
        }
        static void BuildSacredTree(Transform parent,WorldMacroSheetSO s,WorldMacroSheetSO.SiteSpec site,Material wood)
        {var o=GameObject.CreatePrimitive(PrimitiveType.Cylinder);o.name="SacredTree_Trunk_Massing";o.transform.SetParent(parent);o.transform.position=site.Position+Vector3.up*35;o.transform.localScale=new Vector3(9,35,9);o.GetComponent<Renderer>().sharedMaterial=wood;for(int i=0;i<7;i++){float a=i*2.39996f;var c=GameObject.CreatePrimitive(PrimitiveType.Sphere);c.name="SacredTree_Crown_Massing";c.transform.SetParent(parent);c.transform.position=site.Position+new Vector3(Mathf.Cos(a)*25,60+i*4,Mathf.Sin(a)*25);c.transform.localScale=new Vector3(45,18,35);c.GetComponent<Renderer>().sharedMaterial=wood;Object.DestroyImmediate(c.GetComponent<Collider>());}}
        static void BuildForest(Transform parent,WorldMacroSheetSO s,Material wood)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();var rng=new System.Random(s.Seed+33);
            foreach(var region in s.Regions){if(region.Density<=0)continue;var min=new Vector2(region.Polygon.Min(p=>p.x),region.Polygon.Min(p=>p.y));var max=new Vector2(region.Polygon.Max(p=>p.x),region.Polygon.Max(p=>p.y));int count=Mathf.RoundToInt(region.Density*850);
                for(int i=0;i<count;i++){float x=Mathf.Lerp(min.x,max.x,(float)rng.NextDouble()),z=Mathf.Lerp(min.y,max.y,(float)rng.NextDouble());if(!Inside(region.Polygon,x,z)||!WorldMacroTerrain.Contains(s,x,z))continue;
                    if(s.Sites.Any(p=>Vector2.Distance(new Vector2(p.Position.x,p.Position.z),new Vector2(x,z))<100))continue;
                    bool onRoad=false;foreach(var route in s.Routes){for(int k=1;k<route.Points.Length;k++){float t;if(WorldMacroTerrain.SegmentDistance(x,z,route.Points[k-1],route.Points[k],out t)<route.Width*.5f+12f){onRoad=true;break;}}if(onRoad)break;}if(onRoad)continue;
                    if(WorldMacroTerrain.Normal(s,x,z).y<.65f)continue;
                    float y=WorldMacroTerrain.SurfaceHeight(s,x,z);if(s.Rivers.Any(r=>r.Points.Zip(r.Points.Skip(1),(a,b)=>WorldMacroTerrain.SegmentDistance(x,z,a,b,out _)).Any(d=>d<r.Width*.8f)))continue;
                    float h=14+(float)rng.NextDouble()*18;int start=vertices.Count;
                    for(int k=0;k<7;k++){float a=k*Mathf.PI*2/7;vertices.Add(new Vector3(x+Mathf.Cos(a)*h*.27f,y+h*.2f,z+Mathf.Sin(a)*h*.27f));}vertices.Add(new Vector3(x,y+h,z));
                    for(int k=0;k<7;k++){triangles.Add(start+k);triangles.Add(start+7);triangles.Add(start+(k+1)%7);}
                }
            }
            var mesh=new Mesh{name="Forest_Proxy_Crowns",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();MeshObject(parent,mesh.name,Save(mesh,"Meshes/Forest.asset"),wood,false);
        }
        static bool Inside(Vector2[] p,float x,float z){bool yes=false;for(int i=0,j=p.Length-1;i<p.Length;j=i++)if((p[i].y>z)!=(p[j].y>z)&&x<(p[j].x-p[i].x)*(z-p[i].y)/(p[j].y-p[i].y)+p[i].x)yes=!yes;return yes;}
        static Mesh Ribbon(Vector3[] points,float width)
        {
            var dense=new List<Vector3>();for(int i=1;i<points.Length;i++){int n=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(points[i-1],points[i])/20));for(int k=0;k<n;k++)dense.Add(Vector3.Lerp(points[i-1],points[i],k/(float)n));}if(points.Length>0)dense.Add(points[points.Length-1]);
            var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();float distance=0;
            for(int i=0;i<dense.Count;i++){var d=(dense[Mathf.Min(i+1,dense.Count-1)]-dense[Mathf.Max(0,i-1)]).normalized;var side=new Vector3(d.z,0,-d.x).normalized*width*.5f;if(i>0)distance+=Vector3.Distance(dense[i],dense[i-1]);v.Add(dense[i]-side);v.Add(dense[i]+side);uv.Add(new Vector2(0,distance*.04f));uv.Add(new Vector2(1,distance*.04f));if(i>0){int a=(i-1)*2;t.AddRange(new[]{a,a+2,a+1,a+1,a+2,a+3});}}
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        [Serializable]class Grid{public float minX,minZ,step;public int cols,rows;public float[] heights;public bool[] inside;}
        public static string Export()
        {
            Directory.CreateDirectory(Output);var s=Sheet;if(s==null)throw new Exception("Missing sheet");File.WriteAllText(Output+"/sheet.json",JsonUtility.ToJson(s,true));
            const float step=80;int cols=Mathf.RoundToInt((s.BoundsMax.x-s.BoundsMin.x)/step)+1,rows=Mathf.RoundToInt((s.BoundsMax.y-s.BoundsMin.y)/step)+1;
            var grid=new Grid{minX=s.BoundsMin.x,minZ=s.BoundsMin.y,step=step,cols=cols,rows=rows,heights=new float[cols*rows],inside=new bool[cols*rows]};
            for(int z=0;z<rows;z++)for(int x=0;x<cols;x++){int i=z*cols+x;float px=grid.minX+x*step,pz=grid.minZ+z*step;grid.heights[i]=WorldMacroTerrain.SurfaceHeight(s,px,pz);grid.inside[i]=WorldMacroTerrain.Contains(s,px,pz);}
            File.WriteAllText(Output+"/terrain_grid.json",JsonUtility.ToJson(grid));return "Exported same-sheet geography and terrain lattice";
        }
    }
}
