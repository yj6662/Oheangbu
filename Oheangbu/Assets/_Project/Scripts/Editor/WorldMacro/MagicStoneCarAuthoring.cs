using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>New visual version cloned from the installed vehicle. Does not reset its physics.</summary>
    public static class MagicStoneCarAuthoring
    {
        public const string Folder=WorldMacroBuilder.Folder+"/MagicStoneCar";
        public const string PrefabPath=Folder+"/Prefabs/MagicStoneCar_TEST.prefab";
        public const string SurfaceShader="Oheangbu/MagicStoneCarSurface";
        public static string Output=>WorldMacroBuilder.Output+"/MagicStoneCar";
        static readonly string[] Parts={"Cabin","Roof","Engine","Wheel"};
        static readonly string[] AssemblyParts={"Cabin","Roof","Engine","Wheel","Support"};
        static readonly int[] Caps={50000,24000,12000};
        [Serializable] public sealed class TextureEntry
        {
            public string part,material,baseColor,normal,metallicSmoothness,occlusion,engineEmissionMask;
            public bool useConstantMaterial,emissionMaskVerified;public Color constantBaseColor=Color.white;public float metallic,smoothness=.4f;
        }
        [Serializable] public sealed class TextureManifest {public TextureEntry[] entries;}
        [Serializable] public sealed class PartPose {public string part;public Vector3 localPosition,localEuler;}
        [Serializable] public sealed class SocketManifest
        {
            public PartPose[] parts;
            public Vector3 seatLocalPosition=new Vector3(0,2.18f,.65f),seatLocalEuler;
            public Vector3 engineCoreLocalPosition,patternLocalPosition,patternLocalEuler;
            public Vector3[] ventLocalPositions,exitLocalPositions;
            public Vector3 leftWheelVisualEuler=new Vector3(0,180,0),rightWheelVisualEuler;
            public Vector3 externalLookLocalPosition=new Vector3(0,1.8f,0);
        }
        [Serializable] public sealed class Check {public string name,status,detail;}
        [Serializable] public sealed class Report
        {
            public string utc,scope="Static new-model, LOD, PBR and camera wiring audit. Driving/keyboard/comfort are separate tests.";
            public string prefabPath=PrefabPath;
            public int[] renderedTriangles=new int[3];public int materials;public Check[] checks;
        }
        [Serializable] sealed class PhysicsState
        {
            public float mass,linearDamping,angularDamping;public Vector3 centreOfMass;public bool gravity,kinematic;
            public int solver,velocitySolver;public float[] drive;public BoxState[] boxes;public WheelState[] wheels;
        }
        [Serializable] sealed class BoxState {public Vector3 position,scale,centre,size;public Quaternion rotation;public bool trigger;}
        [Serializable] sealed class WheelState
        {
            public Vector3 position,scale;public Quaternion rotation;public float radius,mass,suspension,damping,force;
            public JointSpring spring;public WheelFrictionCurve forward,side;
        }
        static void RequireEdit()
        {if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=WorldMacroBuilder.ScenePath)throw new InvalidOperationException("Existing macro scene must be stopped in Edit mode.");}
        static GameObject Installed()=>GameObject.Find(WorldMacroPalanquinAuthoring.RootName);
        static Transform Node(Transform parent,string name,Vector3 position)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;}
        static void ClearChildren(Transform parent)
        {for(int i=parent.childCount-1;i>=0;i--)Object.DestroyImmediate(parent.GetChild(i).gameObject);}
        static string Owned(string path)
        {
            if(string.IsNullOrWhiteSpace(path))throw new InvalidOperationException("Required explicit PBR texture path is missing.");
            path=path.Replace('\\','/');string full=Path.GetFullPath(path),allowed=Path.GetFullPath(Folder)+Path.DirectorySeparatorChar;
            if(!path.StartsWith(Folder+"/",StringComparison.Ordinal)||!full.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!File.Exists(full))throw new InvalidOperationException("Missing or outside new MagicStoneCar asset folder: "+path);
            return path;
        }
        static void ImportTexture(string path,bool normal,bool colour)
        {
            path=Owned(path);var importer=AssetImporter.GetAtPath(path) as TextureImporter;if(importer==null)throw new InvalidOperationException("Refresh new textures first: "+path);
            var type=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            if(importer.textureType!=type||importer.sRGBTexture!=colour||importer.maxTextureSize!=4096||importer.anisoLevel!=4||importer.filterMode!=FilterMode.Bilinear||(normal&&importer.convertToNormalmap))
            {importer.textureType=type;importer.sRGBTexture=colour;importer.maxTextureSize=4096;importer.anisoLevel=4;importer.filterMode=FilterMode.Bilinear;if(normal)importer.convertToNormalmap=false;importer.SaveAndReimport();}
        }
        static Material Surface(TextureEntry maps,bool core)
        {
            string materialName=(maps.useConstantMaterial?maps.material:maps.part)+(core?"_Core":"_Surface");
            string path=Folder+"/Materials/"+materialName+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);bool create=m==null;
            var shader=Shader.Find(SurfaceShader);if(shader==null)throw new InvalidOperationException("Import the new vehicle-only PBR surface shader first.");
            if(create)m=new Material(shader){name=materialName,enableInstancing=true};else m.shader=shader;
            // Only owned new-car materials change. Scene ambient and old palanquin/world materials are untouched.
            m.SetFloat("_AmbientFloor",.4f);m.SetFloat("_Saturation",.7f);m.SetFloat("_WashStart",800);m.SetFloat("_WashEnd",9500);m.SetFloat("_WashStrength",.58f);
            m.SetFloat("_AlphaClip",0);m.SetFloat("_Cull",2);m.SetOverrideTag("RenderType","Opaque");m.renderQueue=-1;
            m.SetFloat("_EmissionEnabled",core?1:0);m.SetColor("_EmissionColor",Color.black);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
            // This shader samples the explicit maps directly; stale URP local keywords are not its API.
            foreach(var keyword in new[]{"_NORMALMAP","_METALLICSPECGLOSSMAP","_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A","_OCCLUSIONMAP","_EMISSION"})m.DisableKeyword(keyword);
            if(maps.useConstantMaterial)
            {
                m.SetColor("_BaseColor",maps.constantBaseColor);m.SetFloat("_Metallic",Mathf.Clamp01(maps.metallic));m.SetFloat("_Smoothness",Mathf.Clamp01(maps.smoothness));
                m.SetTexture("_BaseMap",Texture2D.whiteTexture);m.SetTexture("_MetallicGlossMap",Texture2D.whiteTexture);m.SetTexture("_OcclusionMap",Texture2D.whiteTexture);
                m.SetTexture("_BumpMap",null);m.SetFloat("_BumpScale",0);m.SetTexture("_EmissionMap",Texture2D.whiteTexture);m.SetFloat("_OcclusionStrength",1);
                if(create)AssetDatabase.CreateAsset(m,path);else EditorUtility.SetDirty(m);return m;
            }
            m.SetColor("_BaseColor",Color.white);m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.baseColor));
            m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.normal));m.SetFloat("_BumpScale",1);
            m.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.metallicSmoothness));m.SetFloat("_Metallic",1);m.SetFloat("_Smoothness",.85f);
            m.SetTexture("_OcclusionMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.occlusion));m.SetFloat("_OcclusionStrength",1);
            string emission=maps.emissionMaskVerified&&!string.IsNullOrEmpty(maps.engineEmissionMask)?maps.engineEmissionMask:maps.baseColor;
            m.SetTexture("_EmissionMap",core?AssetDatabase.LoadAssetAtPath<Texture2D>(emission):Texture2D.whiteTexture);
            if(create)AssetDatabase.CreateAsset(m,path);else EditorUtility.SetDirty(m);return m;
        }
        [Serializable] sealed class SurfaceLookReport
        {
            public string utc,scope="Vehicle-owned material shader update only; standard URP PBR with local diffuse indirect floor. No global illumination, geometry, textures or drive settings changed.";
            public string shader=SurfaceShader;public float localAmbientFloor=.4f,saturation=.7f,mappedSmoothnessScale=.85f;
            public string[] updatedMaterials;public bool physicsUnchanged;public string visualReview="PENDING Unity shader compilation and same-camera review";
        }
        public static string ApplyVehicleSurfaceLook()
        {
            RequireEdit();var root=Installed();var car=root==null?null:root.GetComponent<WorldMacroPalanquinController>();
            if(car==null||root.GetComponent<MagicStoneCarDriveVfx>()==null)throw new InvalidOperationException("Install the new MagicStoneCar first. The old palanquin is not changed by this command.");
            string physics=PhysicsJson(car);var manifest=JsonUtility.FromJson<TextureManifest>(File.ReadAllText(Folder+"/TextureManifest.json"));
            if(manifest?.entries==null)throw new InvalidOperationException("Missing new-car texture manifest.");
            var updated=new List<string>();
            foreach(var e in manifest.entries)
            {
                if(!e.useConstantMaterial){Owned(e.baseColor);Owned(e.normal);Owned(e.metallicSmoothness);Owned(e.occlusion);}
                if(!e.emissionMaskVerified)e.engineEmissionMask=null;
                if(e.useConstantMaterial)
                {
                    bool core=e.material.IndexOf("MagicStoneLanternGlass",StringComparison.OrdinalIgnoreCase)>=0;
                    updated.Add(AssetDatabase.GetAssetPath(Surface(e,core)));
                }
                else
                {
                    updated.Add(AssetDatabase.GetAssetPath(Surface(e,false)));
                    if(e.part=="Engine")updated.Add(AssetDatabase.GetAssetPath(Surface(e,true)));
                }
            }
            AssetDatabase.SaveAssets();Directory.CreateDirectory(Output);
            var report=new SurfaceLookReport{utc=DateTime.UtcNow.ToString("o"),updatedMaterials=updated.Distinct().ToArray(),physicsUnchanged=physics==PhysicsJson(car)};
            File.WriteAllText(Output+"/surface_look.json",JsonUtility.ToJson(report,true));
            return "Updated "+report.updatedMaterials.Length+" owned vehicle materials; physics unchanged="+report.physicsUnchanged+".\n"+Audit();
        }
        static string PhysicsJson(WorldMacroPalanquinController car)
        {
            var p=car.Profile;var b=car.Body;
            return JsonUtility.ToJson(new PhysicsState{mass=b.mass,linearDamping=b.linearDamping,angularDamping=b.angularDamping,centreOfMass=b.centerOfMass,gravity=b.useGravity,kinematic=b.isKinematic,solver=b.solverIterations,velocitySolver=b.solverVelocityIterations,
                drive=new[]{p.MotorTorque,p.FrontDriveShare,p.ForwardSpeed,p.ReverseSpeed,p.BrakeTorquePerWheel,p.ParkingBrakeTorquePerWheel,p.DirectionChangeSpeed,p.ThrottleResponse,p.BrakeResponse,p.LowSpeedSteerAngle,p.HighSpeedSteerAngle,p.SteerDegreesPerSecond,p.AntiRollForce},
                boxes=car.GetComponentsInChildren<BoxCollider>(true).Where(c=>c.name!="MagicStoneEngine_Collision").Select(c=>new BoxState{position=c.transform.localPosition,rotation=c.transform.localRotation,scale=c.transform.localScale,centre=c.center,size=c.size,trigger=c.isTrigger}).ToArray(),
                wheels=car.Wheels.Select(w=>new WheelState{position=w.Collider.transform.localPosition,rotation=w.Collider.transform.localRotation,scale=w.Collider.transform.localScale,radius=w.Collider.radius,mass=w.Collider.mass,suspension=w.Collider.suspensionDistance,damping=w.Collider.wheelDampingRate,force=w.Collider.forceAppPointDistance,spring=w.Collider.suspensionSpring,forward=w.Collider.forwardFriction,side=w.Collider.sidewaysFriction}).ToArray()});
        }
        public static string Install()
        {
            RequireEdit();var old=Installed();if(old==null)throw new InvalidOperationException("Keep the configured existing palanquin installed before replacing visuals.");
            var oldCar=old.GetComponent<WorldMacroPalanquinController>();if(oldCar==null||!oldCar.ValidateConfiguration(out _))throw new InvalidOperationException("Existing physics configuration is invalid.");
            var sockets=JsonUtility.FromJson<SocketManifest>(File.ReadAllText(Folder+"/SocketManifest.json"));
            var textures=JsonUtility.FromJson<TextureManifest>(File.ReadAllText(Folder+"/TextureManifest.json"));
            if(sockets?.parts==null||textures?.entries==null)throw new InvalidOperationException("Explicit part poses and PBR texture manifest are required.");
            var models=new Dictionary<string,GameObject[]>();var maps=new Dictionary<string,TextureEntry>();
            foreach(var part in AssemblyParts)
            {
                if(sockets.parts.Count(p=>p.part==part)!=1)throw new InvalidOperationException("Exactly one part pose required: "+part);
                if(part!="Support"&&textures.entries.Count(e=>e.part==part)!=1)throw new InvalidOperationException("Exactly one PBR texture entry required: "+part);
                if(part=="Support"&&textures.entries.Count(e=>e.part==part&&e.useConstantMaterial)<2)throw new InvalidOperationException("Support requires named constant material entries for brass and lantern glass.");
                maps[part]=textures.entries.First(e=>e.part==part);
                if(!maps[part].emissionMaskVerified)maps[part].engineEmissionMask=null;
                models[part]=Enumerable.Range(0,3).Select(l=>AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Models/MagicStoneCar_"+part+"_LOD"+l+".fbx")).ToArray();
                if(models[part].Any(m=>m==null||m.GetComponentInChildren<SkinnedMeshRenderer>(true)!=null||m.GetComponentsInChildren<MeshFilter>(true).All(f=>f.sharedMesh==null)))throw new InvalidOperationException("Import all three static LODs for "+part+". No substitute model is created.");
                var e=maps[part];if(e.useConstantMaterial)continue;Owned(e.baseColor);Owned(e.normal);Owned(e.metallicSmoothness);Owned(e.occlusion);if(!string.IsNullOrEmpty(e.engineEmissionMask))Owned(e.engineEmissionMask);
            }
            foreach(var p in new[]{Folder+"/Materials",Folder+"/Prefabs",Folder+"/VFX",Output})Directory.CreateDirectory(p);
            foreach(var e in maps.Values.Where(e=>!e.useConstantMaterial)){ImportTexture(e.baseColor,false,true);ImportTexture(e.normal,true,false);ImportTexture(e.metallicSmoothness,false,false);ImportTexture(e.occlusion,false,false);if(!string.IsNullOrEmpty(e.engineEmissionMask))ImportTexture(e.engineEmissionMask,false,true);}
            string before=PhysicsJson(oldCar);GameObject root=null;var oldSeat=old.GetComponent<WorldMacroPalanquinSeat>();
            try
            {
                root=Object.Instantiate(old);root.name=WorldMacroPalanquinAuthoring.RootName+"_Building";root.SetActive(false);
                if(PrefabUtility.IsPartOfPrefabInstance(root))PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                var car=root.GetComponent<WorldMacroPalanquinController>();var seat=root.GetComponent<WorldMacroPalanquinSeat>();
                foreach(var v in root.GetComponentsInChildren<MagicStoneCarDriveVfx>(true))Object.DestroyImmediate(v);
                foreach(var g in root.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(g);
                ClearChildren(car.BodyVisualRoot);for(int i=0;i<4;i++)ClearChildren(car.Wheels[i].Visual);
                var runningGear=root.transform.Find("RunningGearVisual");if(runningGear!=null)for(int i=runningGear.childCount-1;i>=0;i--)if(runningGear.GetChild(i).name.StartsWith("FrameStrut_"))Object.DestroyImmediate(runningGear.GetChild(i).gameObject);
                var priorEngineCollider=root.transform.Find("MagicStoneEngine_Collision");if(priorEngineCollider!=null)Object.DestroyImmediate(priorEngineCollider.gameObject);
                var lodRenderers=new[]{new List<Renderer>(),new List<Renderer>(),new List<Renderer>()};var cores=new List<Renderer>();
                foreach(var part in AssemblyParts)
                {
                    var e=maps[part];var pose=sockets.parts.Single(p=>p.part==part);var material=Surface(e,false);
                    int instances=part=="Wheel"?4:1;
                    for(int i=0;i<instances;i++)
                    {
                        Transform parent=part=="Wheel"?car.Wheels[i].Visual:Node(car.BodyVisualRoot,part,pose.localPosition);
                        if(part!="Wheel")parent.localRotation=Quaternion.Euler(pose.localEuler);
                        else{var rotation=i%2==0?sockets.leftWheelVisualEuler:sockets.rightWheelVisualEuler;car.Wheels[i].VisualRotationOffset=rotation;parent.localRotation=Quaternion.Euler(rotation);}
                        for(int l=0;l<3;l++)
                        {
                            var model=(GameObject)PrefabUtility.InstantiatePrefab(models[part][l]);model.transform.SetParent(parent,false);model.name=part+"_LOD"+l;
                            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                            // Imported FBX axis-conversion rotation is intentional and must survive.
                            foreach(var c in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
                            foreach(var b in model.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(b);
                            foreach(var a in model.GetComponentsInChildren<Animator>(true))Object.DestroyImmediate(a);
                            foreach(var r in model.GetComponentsInChildren<Renderer>(true))
                            {
                                bool core=part=="Engine"&&(r.name.IndexOf("MagicStoneCore",StringComparison.OrdinalIgnoreCase)>=0||!string.IsNullOrEmpty(e.engineEmissionMask));
                                if(part=="Support")
                                {
                                    core=r.name.IndexOf("MagicStoneLanternGlass",StringComparison.OrdinalIgnoreCase)>=0;
                                    r.sharedMaterials=r.sharedMaterials.Select(source=>{var matching=textures.entries.FirstOrDefault(t=>t.part=="Support"&&t.material==source.name);if(matching==null)throw new InvalidOperationException("Missing constant material manifest for "+source.name);return Surface(matching,core);}).ToArray();
                                }
                                else r.sharedMaterials=Enumerable.Repeat(core?Surface(e,true):material,Mathf.Max(1,r.sharedMaterials.Length)).ToArray();
                                lodRenderers[l].Add(r);if(core)cores.Add(r);
                            }
                        }
                    }
                }
                if(!cores.Any(r=>r.transform.IsChildOf(car.BodyVisualRoot.Find("Engine"))))throw new InvalidOperationException("Engine must provide actual MagicStoneCore renderers or an emission mask; no floating substitute orb is created.");
                var engineBounds=LocalMeshBounds(car.BodyVisualRoot.Find("Engine/Engine_LOD0"),root.transform);
                var engineCollider=Node(root.transform,"MagicStoneEngine_Collision",Vector3.zero).gameObject.AddComponent<BoxCollider>();engineCollider.center=engineBounds.center;engineCollider.size=engineBounds.size+Vector3.one*.02f;
                File.WriteAllText(Output+"/engine_collider.json",JsonUtility.ToJson(new BoxState{position=engineCollider.transform.localPosition,rotation=engineCollider.transform.localRotation,scale=engineCollider.transform.localScale,centre=engineCollider.center,size=engineCollider.size,trigger=false},true));
                // Keep only the two existing collider-free axle rods, shared by all visual LODs.
                var staticRenderers=root.GetComponentsInChildren<Renderer>(true).Except(lodRenderers.SelectMany(r=>r)).ToArray();
                foreach(var rows in lodRenderers)rows.AddRange(staticRenderers);
                var group=root.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.None;group.SetLODs(new[]{new LOD(.48f,lodRenderers[0].ToArray()),new LOD(.20f,lodRenderers[1].ToArray()),new LOD(.035f,lodRenderers[2].ToArray())});group.RecalculateBounds();
                for(int l=0;l<3;l++)if(Triangles(lodRenderers[l])>Caps[l])throw new InvalidOperationException("LOD"+l+" exceeds its repeated-instance triangle cap: "+Triangles(lodRenderers[l])+" / "+Caps[l]);
                var profile=Object.Instantiate(oldCar.Profile);profile.name="MagicStoneCarProfile";profile.ExternalShoulderOffset=.8f;profile.ExternalDistance=6;profile.ExternalPivotHeight=.2f;
                string profilePath=Folder+"/MagicStoneCarProfile.asset";var savedProfile=AssetDatabase.LoadAssetAtPath<WorldMacroPalanquinProfileSO>(profilePath);
                if(savedProfile==null){AssetDatabase.CreateAsset(profile,profilePath);car.Profile=profile;}else{EditorUtility.CopySerialized(profile,savedProfile);Object.DestroyImmediate(profile);car.Profile=savedProfile;}
                seat.SeatSocket=Node(car.BodyVisualRoot,"SeatSocket",sockets.seatLocalPosition);seat.SeatSocket.localRotation=Quaternion.Euler(sockets.seatLocalEuler);seat.StartInSeatedView=false;
                seat.ExternalLookSocket.localPosition=sockets.externalLookLocalPosition;
                if(sockets.exitLocalPositions!=null&&sockets.exitLocalPositions.Length>0)
                {foreach(var e in seat.ExitSockets??Array.Empty<Transform>())if(e!=null)Object.DestroyImmediate(e.gameObject);seat.ExitSockets=sockets.exitLocalPositions.Select((p,i)=>Node(root.transform,"Exit_"+i,p)).ToArray();}
                CreateVfx(root,car,sockets,cores.ToArray());
                string after=PhysicsJson(car);if(before!=after)throw new InvalidOperationException("New visual assembly changed the preserved physics fingerprint.");
                File.WriteAllText(Output+"/physics_before.json",before);File.WriteAllText(Output+"/physics_after.json",after);
                string backupPath=Folder+"/Prefabs/Previous_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")+".prefab";
                var backup=Object.Instantiate(old);if(PrefabUtility.IsPartOfPrefabInstance(backup))PrefabUtility.UnpackPrefabInstance(backup,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                PrefabUtility.SaveAsPrefabAsset(backup,backupPath);Object.DestroyImmediate(backup);
                seat.ReviewController=null;seat.ViewCamera=null;root.name=WorldMacroPalanquinAuthoring.RootName;root.SetActive(true);
                PrefabUtility.SaveAsPrefabAssetAndConnect(root,PrefabPath,InteractionMode.AutomatedAction);
                seat.ReviewController=oldSeat.ReviewController;seat.ViewCamera=oldSeat.ViewCamera;PrefabUtility.RecordPrefabInstancePropertyModifications(seat);
                var review=seat.ReviewController;review.PlayerShoulderView=true;review.ShoulderOffset=new Vector3(.55f,1.5f,-2.7f);
                if(review.InspectionStops!=null&&review.InspectionStops.Length>4)review.InspectionStops[4]=root.transform.Find("Entry");EditorUtility.SetDirty(review);
                Object.DestroyImmediate(old);WorldMacroWaterAuthoring.SaveScene();return Audit();
            }
            catch{if(root!=null)Object.DestroyImmediate(root);throw;}
        }
        static int Triangles(IEnumerable<Renderer> renderers)
        {int count=0;foreach(var r in renderers.Distinct()){var mesh=r.GetComponent<MeshFilter>()?.sharedMesh;if(mesh!=null)for(int s=0;s<mesh.subMeshCount;s++)count+=(int)mesh.GetIndexCount(s)/3;}return count;}
        static Bounds LocalMeshBounds(Transform part,Transform root)
        {
            if(part==null)throw new InvalidOperationException("Missing source part for collider bounds.");bool first=true;Bounds result=default;
            foreach(var f in part.GetComponentsInChildren<MeshFilter>(true))if(f.sharedMesh!=null)
            {var b=f.sharedMesh.bounds;for(int i=0;i<8;i++){var p=root.InverseTransformPoint(f.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));if(first){result=new Bounds(p,Vector3.zero);first=false;}else result.Encapsulate(p);}}
            if(first)throw new InvalidOperationException("Empty engine bounds.");return result;
        }
        static void CreateVfx(GameObject root,WorldMacroPalanquinController car,SocketManifest sockets,Renderer[] cores)
        {
            string patternPath=Folder+"/VFX/DrivePattern.prefab";
            if(!File.Exists(patternPath)&&!AssetDatabase.CopyAsset("Assets/_Project/Art/SpellVFX120/Traditional/Catalog/KTP_Cast_Water.prefab",patternPath))throw new InvalidOperationException("Could not copy the existing authored KTP pattern.");
            var prefab=PrefabUtility.LoadPrefabContents(patternPath);
            try
            {
                if(prefab.GetComponentsInChildren<MonoBehaviour>(true).Length>0||prefab.GetComponentsInChildren<Collider>(true).Length>0)throw new InvalidOperationException("Drive motif may contain presentation particles only.");
                foreach(var p in prefab.GetComponentsInChildren<ParticleSystem>(true)){p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=p.main;m.playOnAwake=false;m.loop=false;m.duration=.4f;m.startLifetime=.4f;m.useUnscaledTime=false;m.simulationSpace=ParticleSystemSimulationSpace.Local;m.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;m.maxParticles=Mathf.Min(m.maxParticles,64);}
                PrefabUtility.SaveAsPrefabAsset(prefab,patternPath);
            }finally{PrefabUtility.UnloadPrefabContents(prefab);}
            var profile=AssetDatabase.LoadAssetAtPath<MagicStoneCarVfxProfileSO>(Folder+"/MagicStoneCarVfxProfile.asset");
            if(profile==null){profile=ScriptableObject.CreateInstance<MagicStoneCarVfxProfileSO>();AssetDatabase.CreateAsset(profile,Folder+"/MagicStoneCarVfxProfile.asset");}
            profile.PatternPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(patternPath);EditorUtility.SetDirty(profile);
            var vfx=root.AddComponent<MagicStoneCarDriveVfx>();vfx.Vehicle=car;vfx.Profile=profile;vfx.CoreRenderers=cores;
            vfx.PatternSocket=Node(car.BodyVisualRoot,"ActivationPatternSocket",sockets.patternLocalPosition);vfx.PatternSocket.localRotation=Quaternion.Euler(sockets.patternLocalEuler);vfx.PatternSocket.localScale=Vector3.one*.22f;
            Node(car.BodyVisualRoot,"FrontCoreSocket",sockets.engineCoreLocalPosition);
            if(sockets.ventLocalPositions==null||sockets.ventLocalPositions.Length==0)throw new InvalidOperationException("Provide actual engine vent sockets.");
            string matPath=Folder+"/Materials/DriveVent.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")){name="DriveVent"};material.SetColor("_BaseColor",new Color(.2f,.65f,.7f,.35f));material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_ZWrite",0);material.SetFloat("_SrcBlend",5);material.SetFloat("_DstBlend",10);material.SetOverrideTag("RenderType","Transparent");material.renderQueue=3000;material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");AssetDatabase.CreateAsset(material,matPath);}
            string texturePath=Folder+"/VFX/VentSoftDot.asset";var dot=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if(dot==null){dot=new Texture2D(64,64,TextureFormat.RGBA32,false){name="VentSoftDot",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};var pixels=new Color[4096];for(int y=0;y<64;y++)for(int x=0;x<64;x++){float radius=new Vector2((x+.5f)/32-1,(y+.5f)/32-1).magnitude;pixels[y*64+x]=new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-radius),2));}dot.SetPixels(pixels);dot.Apply(false,false);AssetDatabase.CreateAsset(dot,texturePath);}material.SetTexture("_BaseMap",dot);EditorUtility.SetDirty(material);
            vfx.Vents=sockets.ventLocalPositions.Select((position,i)=>
            {
                var p=Node(car.BodyVisualRoot,"Vent_"+i,position).gameObject.AddComponent<ParticleSystem>();p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var m=p.main;m.playOnAwake=false;m.loop=false;m.duration=.45f;m.startLifetime=new ParticleSystem.MinMaxCurve(.2f,.45f);m.startSpeed=new ParticleSystem.MinMaxCurve(.15f,.5f);m.startSize=new ParticleSystem.MinMaxCurve(.025f,.08f);m.startColor=new Color(.35f,.8f,.85f,.5f);m.maxParticles=48;m.simulationSpace=ParticleSystemSimulationSpace.World;m.useUnscaledTime=false;
                var emission=p.emission;emission.enabled=false;var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=18;shape.radius=.055f;
                var colour=p.colorOverLifetime;colour.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.7f,.15f),new GradientAlphaKey(0,1)});colour.color=gradient;
                var r=p.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
                return p;
            }).ToArray();
        }
        public static string Audit()
        {
            RequireEdit();var root=Installed();if(root==null)throw new InvalidOperationException("Install first.");var car=root.GetComponent<WorldMacroPalanquinController>();var seat=root.GetComponent<WorldMacroPalanquinSeat>();var vfx=root.GetComponent<MagicStoneCarDriveVfx>();var checks=new List<Check>();
            var group=root.GetComponent<LODGroup>();var report=new Report{utc=DateTime.UtcNow.ToString("o")};var lods=group==null?Array.Empty<LOD>():group.GetLODs();
            Add("three visual LOD levels",lods.Length==3,"Four independently rotating wheel instances included in each level.");
            for(int l=0;l<3;l++){report.renderedTriangles[l]=l<lods.Length?Triangles(lods[l].renderers):0;Add("LOD"+l+" triangle cap",report.renderedTriangles[l]>0&&report.renderedTriangles[l]<=Caps[l],report.renderedTriangles[l]+" / "+Caps[l]);}
            var materials=root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();report.materials=materials.Length;
            Add("preserved existing vehicle physics settings",File.Exists(Output+"/physics_before.json")&&File.ReadAllText(Output+"/physics_before.json")==PhysicsJson(car),"Rigidbody settings, existing boxes, wheel configuration and drive values preserved. Added front engine collider is recorded separately; compound inertia/contact response needs Play verification.");
            Add("new engine physical enclosure",root.transform.Find("MagicStoneEngine_Collision")?.GetComponent<BoxCollider>()!=null,"Actual LOD0 engine bounds +0.02m, approved new front overhang enclosure. engine_collider.json records the addition.");
            Add("controller valid",car!=null&&car.ValidateConfiguration(out _),"Static reference validation only.");
            Add("player and vehicle shoulder defaults",seat!=null&&!seat.StartInSeatedView&&seat.ReviewController!=null&&seat.ReviewController.PlayerShoulderView&&Mathf.Approximately(car.Profile.ExternalShoulderOffset,.8f),"V switches vehicle seated / shoulder; actual inputs still require Play review.");
            Add("front core and local motif wiring",vfx!=null&&vfx.CoreRenderers!=null&&vfx.CoreRenderers.Length>0&&vfx.PatternSocket!=null&&vfx.Profile!=null&&vfx.Profile.PatternPrefab!=null&&vfx.Vents.Length>0,"Particle-only copied KTP source; no combat events.");
            var errors=materials.Select(m=>m.shader).Distinct().Where(s=>s!=null).SelectMany(ShaderUtil.GetShaderMessages).Where(m=>m.severity.ToString()=="Error").ToArray();Add("material shader errors",errors.Length==0,string.Join(";",errors.Select(e=>e.message)));
            foreach(var part in Parts){var m=materials.FirstOrDefault(x=>x.name==part+"_Surface"||x.name==part+"_Core");Add(part+" URP PBR maps",m!=null&&m.shader.name==SurfaceShader&&m.GetTexture("_BaseMap")!=null&&m.GetTexture("_BumpMap")!=null&&m.GetTexture("_MetallicGlossMap")!=null&&m.GetTexture("_OcclusionMap")!=null,"Vehicle-only UniversalFragmentPBR; 4096 import limit; metallic R + smoothness A; source textures unchanged.");}
            var surfaces=materials.Where(m=>m.shader!=null&&m.shader.name==SurfaceShader).ToArray();
            Add("vehicle local indirect floor",surfaces.Length>=7&&surfaces.All(m=>Mathf.Approximately(m.GetFloat("_AmbientFloor"),.4f)&&Mathf.Approximately(m.GetFloat("_Saturation"),.7f)),"Local diffuse floor 0.4, saturation 0.7. World ambient/reflection and old world surfaces unchanged by authoring.");
            Add("emission restricted to core and lantern glass",surfaces.All(m=>m.GetFloat("_EmissionEnabled")<.5f||m.name=="Engine_Core"||m.name=="MagicStoneLanternGlass_Core"),"Ordinary wood/metal surface brightness is indirect diffuse, not emission.");
            report.checks=checks.ToArray();Directory.CreateDirectory(Output);File.WriteAllText(Output+"/setup_audit.json",JsonUtility.ToJson(report,true));return string.Join("\n",checks.Select(c=>c.status+" "+c.name+" "+c.detail));
            void Add(string name,bool pass,string detail)=>checks.Add(new Check{name=name,status=pass?"PASS":"FAIL",detail=detail});
        }
    }
}
