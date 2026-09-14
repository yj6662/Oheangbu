using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Incremental TEST vehicle assembly. No asset generation, terrain rebuild or automatic driving.</summary>
    public static class WorldMacroPalanquinAuthoring
    {
        public const string Folder = WorldMacroBuilder.Folder + "/Palanquin";
        public const string RootName = "WorldMacro_MagicPalanquin_TEST";
        public const string PrefabPath = Folder + "/Prefabs/MagicPalanquin_TEST.prefab";
        public const string ProfilePath = Folder + "/PalanquinProfile.asset";
        static string Output => WorldMacroBuilder.Output + "/Palanquin";
        static readonly string[] Parts = { "Cabin", "Roof", "Wheel" };
        static readonly string[] WheelNames = { "FL", "FR", "RL", "RR" };
        static readonly Vector3[] Mounts = { new Vector3(-1.075f,.74f,1.25f), new Vector3(1.075f,.74f,1.25f), new Vector3(-1.075f,.74f,-1.25f), new Vector3(1.075f,.74f,-1.25f) };
        static readonly Dictionary<string,TextureEntry> textureBindings = new Dictionary<string,TextureEntry>(StringComparer.Ordinal);

        [Serializable] public sealed class TextureEntry
        {
            public string part, baseColor, normal, metallicRoughness;
        }
        [Serializable] public sealed class TextureManifest { public TextureEntry[] entries; }

        [Serializable] public sealed class SocketManifest
        {
            public bool overrideSeat;
            public Vector3 seatLocalPosition = new Vector3(0,2.18f,.65f);
            public Vector3 seatLocalEuler;
            public Vector3 leftWheelVisualEuler=new Vector3(0,180,0),rightWheelVisualEuler;
            public Vector3[] exitLocalPositions;
        }
        [Serializable] sealed class Check { public string name, status, detail; }
        [Serializable] sealed class SetupReport
        {
            public string utc, scenePath, prefabPath = PrefabPath, profilePath = ProfilePath;
            public string scope = "TEST static assembly and collider probes; not a driving, suspension, boarding, exit or comfort pass.";
            public Vector3 position, visualBoundsSize, seatLocalPosition;
            public float trackM, wheelbaseM, wheelRadiusM, roadClearanceM, terrainReliefM;
            public int meshTriangles, materials, wheelColliders;
            public string[] sources;
            public TextureEntry[] textureManifest;
            public string metallicRoughnessUse = "MetallicRoughness paths listed in the manifest retain packed G=roughness/B=metallic files; these maps are unused by the current diffuse WorldMacroTexturedSurface shader.";
            public Check[] checks;
            public string[] unverified = { "Rigidbody drive/brake/steering and four-wheel contact in Play", "Body sway and seated visibility", "E boarding/exit/reboarding and V view switch", "Slope, bridge and full route traversal; user walking remains user review" };
        }
        [Serializable] sealed class CaptureInfo
        {
            public string view, file, utc, scenePath;
            public string scope = "Static assembly composition. Seated capture uses SeatSocket but does not enter a live vehicle or validate driving.";
            public Vector3 position, target;
            public int width = 1920, height = 1080;
            public float commitBefore, commitAfter, fieldOfView, nearClip;
            public bool synchronousShaderCompilation = true;
        }
        static void RequireScene()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != WorldMacroBuilder.ScenePath)
                throw new InvalidOperationException("Existing macro scene in Edit mode required; no scene is opened or regenerated automatically.");
        }
        static GameObject RequireModel(string part)
        {
            string path = Folder + "/Models/Palanquin_" + part + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new FileNotFoundException("Missing generated static vehicle part. Import before Install; no substitute geometry is created: " + path);
            if (model.GetComponentsInChildren<MeshFilter>(true).All(f => f.sharedMesh == null) || model.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                throw new InvalidOperationException("Expected a static MeshFilter part, not an empty or skinned asset: " + path);
            return model;
        }
        static WorldMacroPalanquinProfileSO Profile()
        {
            var p = AssetDatabase.LoadAssetAtPath<WorldMacroPalanquinProfileSO>(ProfilePath);
            if (p != null) return p; // Keep later user handling adjustments on reinstall.
            p = ScriptableObject.CreateInstance<WorldMacroPalanquinProfileSO>();
            p.WheelRadius = .6f; p.HullCentre = new Vector3(0,2.02f,0); p.HullSize = new Vector3(1.8f,2.15f,3);
            p.CentreOfMass = new Vector3(0,.75f,0); p.ExitSideDistance = 2.05f; p.ExitEndDistance = 2.85f;
            p.BoardingDistance = 2.9f; p.ExternalDistance = 7; p.ExternalPivotHeight = .2f;
            AssetDatabase.CreateAsset(p, ProfilePath); return p;
        }
        static TextureEntry[] ReadTextureManifest()
        {
            string path=Folder+"/TextureManifest.json";
            if(!File.Exists(path))throw new FileNotFoundException("Missing explicit PBR texture mapping; FBX material guesses are not used: "+path);
            var manifest=JsonUtility.FromJson<TextureManifest>(File.ReadAllText(path));
            if(manifest?.entries==null)throw new InvalidOperationException("TextureManifest requires entries [{part,baseColor,normal,metallicRoughness}].");
            foreach(var part in Parts)
            {
                var matches=manifest.entries.Where(e=>e!=null&&e.part==part).ToArray();
                if(matches.Length!=1)throw new InvalidOperationException("TextureManifest requires exactly one entry for "+part+".");
                var e=matches[0];e.baseColor=OwnedTexturePath(e.baseColor);e.normal=OwnedTexturePath(e.normal);
                if(e.baseColor==e.normal)throw new InvalidOperationException(part+" base and normal must be separate texture assets.");
                if(!string.IsNullOrWhiteSpace(e.metallicRoughness))e.metallicRoughness=OwnedTexturePath(e.metallicRoughness);
            }
            var entries=Parts.Select(p=>manifest.entries.Single(e=>e!=null&&e.part==p)).ToArray();
            if(entries.Select(e=>e.baseColor).Intersect(entries.Select(e=>e.normal)).Any())throw new InvalidOperationException("A texture asset cannot serve both sRGB base colour and linear normal roles.");
            return entries;
        }
        static string OwnedTexturePath(string assetPath)
        {
            if(string.IsNullOrWhiteSpace(assetPath))throw new InvalidOperationException("Base color and normal asset paths are mandatory.");
            assetPath=assetPath.Replace('\\','/');
            string permitted=Path.GetFullPath(Path.Combine(Application.dataPath,"_Project/Art/World/WorldMacro/Palanquin"))+Path.DirectorySeparatorChar;
            string absolute=Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));
            if(!assetPath.StartsWith(Folder+"/",StringComparison.Ordinal)||!absolute.StartsWith(permitted,StringComparison.OrdinalIgnoreCase)||!File.Exists(absolute))
                throw new InvalidOperationException("Texture must exist within the new Palanquin folder; external originals are never reimported: "+assetPath);
            return assetPath;
        }
        static void PrepareTexture(string path,bool normal)
        {
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null)throw new InvalidOperationException("Refresh/import this new texture before Install: "+path);
            var desired=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            bool changed=importer.textureType!=desired||importer.sRGBTexture==normal||importer.maxTextureSize!=2048||importer.filterMode!=FilterMode.Bilinear||importer.anisoLevel!=4||(normal&&importer.convertToNormalmap);
            if(changed)
            {
                importer.textureType=desired;importer.sRGBTexture=!normal;importer.maxTextureSize=2048;
                importer.filterMode=FilterMode.Bilinear;importer.anisoLevel=4;if(normal)importer.convertToNormalmap=false;
                importer.SaveAndReimport();
            }
            if(AssetDatabase.LoadAssetAtPath<Texture2D>(path)==null)throw new InvalidOperationException("Texture did not import: "+path);
        }
        public static string Install()
        {
            RequireScene(); var models = Parts.Select(RequireModel).ToArray();
            var mappedTextures=ReadTextureManifest();
            var shader = Shader.Find("Oheangbu/WorldMacroTexturedSurface");
            if (shader == null) throw new InvalidOperationException("Import WorldMacroTexturedSurface.shader before assembling the vehicle.");
            var view = Object.FindFirstObjectByType<WorldMacroReviewController>();
            var camera = view == null ? null : view.GetComponent<Camera>();
            if (view == null || view.WalkBody == null || camera == null || WorldMacroBuilder.Sheet == null)
                throw new InvalidOperationException("Macro review camera, WalkBody and macro sheet must already exist.");
            SocketManifest manifest = new SocketManifest();
            if (File.Exists(Folder + "/SocketManifest.json")) manifest = JsonUtility.FromJson<SocketManifest>(File.ReadAllText(Folder + "/SocketManifest.json")) ?? manifest;
            if (manifest.overrideSeat && (manifest.seatLocalPosition.y < 1.1f || manifest.seatLocalPosition.y > 2.9f || Mathf.Abs(manifest.seatLocalPosition.x) > .85f || Mathf.Abs(manifest.seatLocalPosition.z) > 1.45f))
                throw new InvalidOperationException("SocketManifest seat lies outside the TEST cabin envelope; correct the authored seated eye socket.");
            Physics.SyncTransforms(); var previous = GameObject.Find(RootName);
            ResolvePlacement(previous == null ? null : previous.transform, out Vector3 position, out Quaternion rotation);
            foreach (var path in new[] { Folder, Folder + "/Materials", Folder + "/Prefabs", Output }) Directory.CreateDirectory(path);
            textureBindings.Clear();
            foreach(var entry in mappedTextures){PrepareTexture(entry.baseColor,false);PrepareTexture(entry.normal,true);textureBindings.Add(entry.part,entry);}
            var profile = Profile(); var root = new GameObject(RootName + "_Building"); root.SetActive(false);
            try
            {
                root.transform.SetPositionAndRotation(position, rotation);
                var body = root.AddComponent<Rigidbody>(); var hull = root.AddComponent<BoxCollider>();
                var visual = Node(root.transform, "BodyVisualRoot", Vector3.zero);
                // Width/length fit uniformly. Do not shrink a broad roof merely to force its height.
                var cabinBounds = FitPart(visual, models[0], "Cabin", new Vector2(1.8f,3), 1f);
                var roofBounds=FitPart(visual, models[1], "Roof", new Vector2(2.3f,3.4f), cabinBounds.max.y-.035f);
                if(cabinBounds.size.y>2.5f||roofBounds.size.y>1.5f)throw new InvalidOperationException("Imported part height disagrees with Blender assembly; preserve the FBX coordinate conversion before fitting.");
                var roofCollision=Node(root.transform,"Roof_Collision",Vector3.zero).gameObject.AddComponent<BoxCollider>();
                roofCollision.center=roofBounds.center;roofCollision.size=roofBounds.size+new Vector3(.06f,.08f,.06f);
                var seatSocket = Node(visual, "SeatSocket", manifest.overrideSeat ? manifest.seatLocalPosition : new Vector3(0,2.18f,.65f));
                seatSocket.localRotation = manifest.overrideSeat ? Quaternion.Euler(manifest.seatLocalEuler) : Quaternion.identity;
                var controller = root.AddComponent<WorldMacroPalanquinController>();
                controller.Profile = profile; controller.Body = body; controller.Hull = hull; controller.BodyVisualRoot = visual;
                controller.Wheels = new WorldMacroPalanquinController.WheelBinding[4];
                for (int i = 0; i < 4; i++)
                {
                    var mount = Node(root.transform, "WheelCollider_" + WheelNames[i], Mounts[i]);
                    var wheel = mount.gameObject.AddComponent<WheelCollider>();
                    var wheelVisual = Node(root.transform, "WheelVisual_" + WheelNames[i], Mounts[i] - Vector3.up * .14f);
                    FitWheel(wheelVisual, models[2], profile.WheelRadius);
                    var wheelOffset=i%2==0?manifest.leftWheelVisualEuler:manifest.rightWheelVisualEuler;
                    wheelVisual.localRotation=Quaternion.Euler(wheelOffset);
                    controller.Wheels[i] = new WorldMacroPalanquinController.WheelBinding { Collider = wheel, Visual = wheelVisual, VisualRotationOffset=wheelOffset };
                }
                BuildRunningGear(root.transform,controller);
                var seat = root.AddComponent<WorldMacroPalanquinSeat>(); seat.Vehicle = controller; seat.SeatSocket = seatSocket;
                seat.ExternalLookSocket = Node(root.transform, "ExternalLookSocket", new Vector3(0,1.8f,0));
                Vector3[] exits = manifest.exitLocalPositions != null && manifest.exitLocalPositions.Length > 0 ? manifest.exitLocalPositions : new[] { new Vector3(2.05f,0,0), new Vector3(-2.05f,0,0), new Vector3(0,0,-2.85f) };
                seat.ExitSockets = exits.Select((p,i) => Node(root.transform, "Exit_" + i, p)).ToArray();
                var entry = Node(root.transform, "Entry", new Vector3(2.05f,0,.15f));
                var entryPosition = entry.position; if (!TryGround(entryPosition, out var entryHit)) throw new InvalidOperationException("Boarding entry has no physical terrain.");
                entryPosition.y = entryHit.point.y + .04f; entry.position = entryPosition;
                entry.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(seatSocket.position - entry.position, Vector3.up), Vector3.up);
                root.SetActive(true);Physics.SyncTransforms();
                if (!controller.ApplyConfiguration()) throw new InvalidOperationException(controller.ConfigurationIssue);
                if (previous != null)
                {
                    PrefabUtility.SaveAsPrefabAsset(previous, Folder + "/Prefabs/Previous_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".prefab");
                }
                root.name = RootName;
                root.SetActive(true);
                // Save a reusable prefab without references to a particular scene camera/capsule.
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
                seat.ReviewController = view; seat.ViewCamera = camera;
                PrefabUtility.RecordPrefabInstancePropertyModifications(seat);
                var stops = new Transform[Mathf.Max(5,view.InspectionStops == null ? 0 : view.InspectionStops.Length)];
                if (view.InspectionStops != null) Array.Copy(view.InspectionStops,stops,view.InspectionStops.Length);
                stops[4] = entry; view.InspectionStops = stops; EditorUtility.SetDirty(view);
                if (previous != null) Object.DestroyImmediate(previous);
                WorldMacroWaterAuthoring.SaveScene(); return "Installed generated palanquin TEST beside Inn. " + Audit();
            }
            catch { if (root != null && root.name.EndsWith("_Building")) Object.DestroyImmediate(root); throw; }
        }
        static Transform Node(Transform parent, string name, Vector3 position)
        { var t = new GameObject(name).transform; t.SetParent(parent,false); t.localPosition = position; return t; }
        static Mesh RodMesh()
        {
            Directory.CreateDirectory(Folder+"/Meshes");string path=Folder+"/Meshes/RunningGear_Rod8.asset";
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved!=null)return saved;
            const int sides=8;var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            for(int end=0;end<2;end++)for(int i=0;i<=sides;i++)
            {
                float angle=i*Mathf.PI*2/sides;var radial=new Vector3(0,Mathf.Cos(angle),Mathf.Sin(angle));
                vertices.Add(radial+Vector3.right*(end==0?-.5f:.5f));normals.Add(radial);uv.Add(new Vector2(end,i/(float)sides));
            }
            for(int i=0;i<sides;i++){int a=i,b=i+1,c=i+1+sides+1,d=i+sides+1;triangles.AddRange(new[]{a,b,c,a,c,d});}
            for(int end=0;end<2;end++)
            {
                int centre=vertices.Count;var axis=Vector3.right*(end==0?-1:1);vertices.Add(axis*.5f);normals.Add(axis);uv.Add(new Vector2(.5f,.5f));
                for(int i=0;i<sides;i++){float angle=i*Mathf.PI*2/sides;var radial=new Vector3(0,Mathf.Cos(angle),Mathf.Sin(angle));vertices.Add(axis*.5f+radial);normals.Add(axis);uv.Add(new Vector2(.5f+radial.y*.5f,.5f+radial.z*.5f));}
                for(int i=0;i<sides;i++){int a=centre+1+i,b=centre+1+(i+1)%sides;if(end==0)triangles.AddRange(new[]{centre,b,a});else triangles.AddRange(new[]{centre,a,b});}
            }
            var mesh=new Mesh{name="RunningGear_Rod8"};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static void BuildRunningGear(Transform root,WorldMacroPalanquinController car)
        {
            // A previously saved prefab can also retain an added-object override with this name.
            // Remove every direct cosmetic group after unpacking; physics and source parts stay intact.
            for(int i=root.childCount-1;i>=0;i--){var prior=root.GetChild(i);if(prior.name=="RunningGearVisual")Object.DestroyImmediate(prior.gameObject);}
            var group=Node(root,"RunningGearVisual",Vector3.zero);var mesh=RodMesh();
            string path=Folder+"/Materials/RunningGear_DarkMetal.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null)
            {
                var source=car.Wheels[0].Visual.GetComponentInChildren<Renderer>(true).sharedMaterial;
                material=new Material(source){name="RunningGear_DarkMetal",enableInstancing=true};
                material.SetTexture("_BaseMap",null);material.SetTexture("_BumpMap",null);material.SetFloat("_BumpScale",0);material.SetFloat("_AlphaClip",0);
                material.SetColor("_BaseColor",new Color(.12f,.115f,.10f,1));material.SetFloat("_Saturation",.25f);AssetDatabase.CreateAsset(material,path);
            }
            for(int axle=0;axle<2;axle++)
            {
                Vector3 left=root.InverseTransformPoint(car.Wheels[axle*2].Visual.position),right=root.InverseTransformPoint(car.Wheels[axle*2+1].Visual.position);
                float length=Vector3.ProjectOnPlane(right-left,Vector3.up).magnitude;
                var shaft=Rod(axle==0?"FrontAxle":"RearAxle",(left+right)*.5f,new Vector3(length,.055f,.055f),Quaternion.FromToRotation(Vector3.right,(right-left).normalized));
                if(axle==0)car.FrontAxleVisual=shaft;else car.RearAxleVisual=shaft;
                foreach(int side in new[]{-1,1})Rod("FrameStrut_"+axle+"_"+side,new Vector3(side*.72f,.77f,(left.z+right.z)*.5f),new Vector3(.55f,.045f,.045f),Quaternion.Euler(0,0,90));
            }
            Transform Rod(string name,Vector3 position,Vector3 scale,Quaternion rotation)
            {
                var t=Node(group,name,position);t.localScale=scale;t.localRotation=rotation;
                t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=t.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;return t;
            }
        }
        public static string AddRunningGear()
        {
            RequireScene();var root=GameObject.Find(RootName);var car=root==null?null:root.GetComponent<WorldMacroPalanquinController>();
            if(car==null||!car.ValidateConfiguration(out _))throw new InvalidOperationException("Install the configured generated vehicle before adding cosmetic running gear.");
            if(PrefabUtility.IsPartOfPrefabInstance(root))PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            BuildRunningGear(root.transform,car);EditorUtility.SetDirty(car);
            var seat=root.GetComponent<WorldMacroPalanquinSeat>();var view=seat.ReviewController;var camera=seat.ViewCamera;
            try{seat.ReviewController=null;seat.ViewCamera=null;PrefabUtility.SaveAsPrefabAssetAndConnect(root,PrefabPath,InteractionMode.AutomatedAction);}
            finally{seat.ReviewController=view;seat.ViewCamera=camera;PrefabUtility.RecordPrefabInstancePropertyModifications(seat);}
            WorldMacroWaterAuthoring.SaveScene();return "Added two rigid axles and four frame struts: one shared 32-triangle mesh, 192 visible triangles, one copied dark material. Physics unchanged. "+Audit();
        }
        static Transform InstantiatePart(Transform parent, GameObject source, string name)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source); model.name = name;
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            // FBX root rotation carries Blender Z-up to Unity Y-up conversion.
            // Resetting it turns the cabin and roof upright onto their ends.
            model.transform.SetParent(parent,false); model.transform.localPosition = Vector3.zero;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var b in model.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(b);
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = r.sharedMaterials.Select(m=>CopySurface(m,name)).ToArray();
                r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
            }
            return model.transform;
        }
        static Bounds LocalBounds(Transform subject, Transform reference)
        {
            Bounds result = default; bool first = true;
            foreach (var f in subject.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue; var b = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = reference.InverseTransformPoint(f.transform.TransformPoint(b.center + Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
                    if (first) { result = new Bounds(p,Vector3.zero); first = false; } else result.Encapsulate(p);
                }
            }
            if (first || result.size.sqrMagnitude < .0001f) throw new InvalidOperationException("Empty static part bounds: " + subject.name);
            return result;
        }
        static Bounds FitPart(Transform parent, GameObject source, string name, Vector2 envelope, float bottomY)
        {
            var model = InstantiatePart(parent,source,name); var b = LocalBounds(model,parent);
            if (Mathf.Min(b.size.x,b.size.y,b.size.z) < .0001f) throw new InvalidOperationException("Degenerate part dimensions: " + name);
            float scale = Mathf.Min(envelope.x/b.size.x,envelope.y/b.size.z);
            model.localScale *= scale; b = LocalBounds(model,parent);
            model.localPosition += new Vector3(-b.center.x,bottomY-b.min.y,-b.center.z);
            return LocalBounds(model,parent);
        }
        static void FitWheel(Transform parent, GameObject source, float radius)
        {
            var model = InstantiatePart(parent,source,"Wheel"); var b = LocalBounds(model,parent);
            if (b.size.x > Mathf.Min(b.size.y,b.size.z)*.65f || Mathf.Min(b.size.y,b.size.z)/Mathf.Max(b.size.y,b.size.z) < .85f)
                throw new InvalidOperationException("Wheel must be a circular static part with its axle along X; correct the Blender export rather than guessing an axis.");
            model.localScale *= radius*2/Mathf.Max(b.size.y,b.size.z); b = LocalBounds(model,parent); model.localPosition -= b.center;
        }
        static Material CopySurface(Material source,string part)
        {
            if (source == null) throw new InvalidOperationException("Generated part has a missing source material; import its texture material first.");
            if(!textureBindings.TryGetValue(part,out var maps))throw new InvalidOperationException("No explicit texture binding for "+part+".");
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId) || string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Generated material must be a saved source asset: " + source.name);
            string path = Folder + "/Materials/" + part + "_" + guid + "_" + localId + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path); bool create=material==null;
            if(create)material = new Material(Shader.Find("Oheangbu/WorldMacroTexturedSurface")) { name=source.name+"_Palanquin", enableInstancing=true };
            material.shader=Shader.Find("Oheangbu/WorldMacroTexturedSurface");
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.baseColor));
            material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(maps.normal));
            string baseProperty = new[] { "_BaseMap", "_MainTex" }.FirstOrDefault(source.HasProperty);
            if (baseProperty != null) { material.SetTextureScale("_BaseMap",source.GetTextureScale(baseProperty)); material.SetTextureOffset("_BaseMap",source.GetTextureOffset(baseProperty)); }
            Color tint = source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.GetColor("_Color"):Color.white;
            material.SetColor("_BaseColor",new Color(tint.r*.72f,tint.g*.70f,tint.b*.67f,1));
            material.SetFloat("_BumpScale",source.HasProperty("_BumpScale")?Mathf.Max(.1f,source.GetFloat("_BumpScale")):1);
            if (source.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip",source.GetFloat("_AlphaClip"));
            if (source.HasProperty("_Cutoff")) material.SetFloat("_Cutoff",source.GetFloat("_Cutoff"));
            if (source.HasProperty("_Cull")) material.SetFloat("_Cull",source.GetFloat("_Cull"));
            if(create)AssetDatabase.CreateAsset(material,path);else EditorUtility.SetDirty(material);
            return material;
        }
        static Renderer[] PartRenderers(Transform root,string part)
        {
            var nodes=part=="Wheel"?WheelNames.Select(n=>root.Find("WheelVisual_"+n)).ToArray():new[]{root.Find("BodyVisualRoot/"+part)};
            return nodes.Where(n=>n!=null).SelectMany(n=>n.GetComponentsInChildren<Renderer>(true)).ToArray();
        }
        static bool TryGround(Vector3 p, out RaycastHit ground)
        {
            ground = default; bool found = false;
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x,2200,p.z),Vector3.down,4400,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if (hit.collider.name.StartsWith("Terrain_") && (!found || hit.point.y > ground.point.y)) { ground=hit; found=true; }
            return found;
        }
        static float RoadClearance(Vector3 p, out Vector3 direction)
        {
            float nearest = float.PositiveInfinity; direction = Vector3.forward;
            foreach (var route in WorldMacroBuilder.Sheet.Routes) for (int i=1;i<route.Points.Length;i++)
            {
                float d=WorldMacroTerrain.SegmentDistance(p.x,p.z,route.Points[i-1],route.Points[i],out _)-route.Width*.5f;
                if(d<nearest){nearest=d;direction=Vector3.ProjectOnPlane(route.Points[i]-route.Points[i-1],Vector3.up).normalized;}
            }
            return nearest;
        }
        static bool Dry(Vector3 p)
        {
            // Conservative elevation buffer around authored channels, including their widened banks.
            foreach(var river in WorldMacroBuilder.Sheet.Rivers)for(int i=1;i<river.Points.Length;i++)
            {
                float d=WorldMacroTerrain.SegmentDistance(p.x,p.z,river.Points[i-1],river.Points[i],out float t);
                if(d<Mathf.Max(180,river.Width*3) && p.y<Mathf.Lerp(river.Points[i-1].y,river.Points[i].y,t)+.5f)return false;
            }
            return true;
        }
        static bool Bench(Vector3 p, Quaternion rotation, Transform ignored, out float highest, out float relief)
        {
            highest=float.NegativeInfinity;float lowest=float.PositiveInfinity;
            for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)
            {
                var q=p+rotation*new Vector3(x*2.6f,0,z*3.4f);
                if(!WorldMacroTerrain.Contains(WorldMacroBuilder.Sheet,q.x,q.z)||!TryGround(q,out var ground)||Vector3.Angle(ground.normal,Vector3.up)>7){relief=999;return false;}
                q.y=ground.point.y;if(!Dry(q)){relief=999;return false;}highest=Mathf.Max(highest,q.y);lowest=Mathf.Min(lowest,q.y);
            }
            relief=highest-lowest;if(relief>.35f)return false;
            foreach(var c in Physics.OverlapBox(new Vector3(p.x,highest+1.75f,p.z),new Vector3(2.6f,1.65f,3.4f),rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(!c.name.StartsWith("Terrain_") && !(c is CharacterController) && (ignored==null||!c.transform.IsChildOf(ignored)))return false;
            return true;
        }
        static void ResolvePlacement(Transform previous, out Vector3 position, out Quaternion rotation)
        {
            var inn=WorldMacroBuilder.Sheet.FindSite("Inn");if(inn==null)throw new InvalidOperationException("Macro Inn site is missing.");
            if(previous!=null && RoadClearance(previous.position,out _)>=4 && Bench(previous.position,previous.rotation,previous,out var reusedHeight,out _))
            {position=new Vector3(previous.position.x,reusedHeight+.03f,previous.position.z);rotation=previous.rotation;return;}
            float best=float.PositiveInfinity;position=default;rotation=Quaternion.identity;
            for(int ring=0;ring<7;ring++)for(int a=0;a<24;a++)
            {
                float angle=a*Mathf.PI/12,range=12+ring*16;var p=inn.Position+new Vector3(Mathf.Cos(angle)*range,0,Mathf.Sin(angle)*range);
                float clearance=RoadClearance(p,out var forward);if(clearance<4||clearance>30||forward.sqrMagnitude<.5f)continue;
                var r=Quaternion.LookRotation(forward,Vector3.up);if(!Bench(p,r,previous,out var high,out var relief))continue;
                float score=relief*100+range*.05f+Mathf.Abs(clearance-7)*.3f;
                if(score<best){best=score;position=new Vector3(p.x,high+.03f,p.z);rotation=r;}
            }
            if(float.IsPositiveInfinity(best))throw new InvalidOperationException("No dry route-clear Inn bench met the TEST limits (7 degrees, 0.35m relief). Place an approved staging point explicitly; terrain is not flattened automatically.");
        }
        public static string Audit()
        {
            RequireScene();Physics.SyncTransforms();var root=GameObject.Find(RootName);if(root==null)throw new InvalidOperationException("Install generated palanquin first.");
            var car=root.GetComponent<WorldMacroPalanquinController>();var seat=root.GetComponent<WorldMacroPalanquinSeat>();var checks=new List<Check>();
            bool configured=car!=null&&car.ValidateConfiguration(out _);Add("four independent wheels and rigid hierarchy",configured,car==null?"Missing controller":car.ConfigurationIssue??"Static configuration only");
            Add("single Rigidbody",root.GetComponentsInChildren<Rigidbody>(true).Length==1,"Dynamic root; visual parts contain no rigid bodies");
            Add("scene camera and capsule ownership",seat!=null&&seat.ReviewController!=null&&seat.ReviewController.WalkBody!=null&&seat.ViewCamera!=null,"Runtime E/V ownership still unverified");
            var entry=root.transform.Find("Entry");bool ground=entry!=null&&TryGround(entry.position,out _);Add("inspection stop on physical terrain",ground,"Entry beside vehicle; no automatic capsule movement");
            float clearance=RoadClearance(root.transform.position,out _);Add("existing road remains clear",clearance>=4,clearance.ToString("F2")+"m from authored road edge");
            bool bench=Bench(root.transform.position,root.transform.rotation,root.transform,out _,out float relief);Add("dry nearly level staging footprint",bench,relief.ToString("F3")+"m height spread on 9 terrain samples");
            for(int i=0;i<4 && car!=null&&car.Wheels!=null&&i<car.Wheels.Length;i++)
            {
                var wheel=car.Wheels[i]?.Collider;bool support=wheel!=null&&TryGround(wheel.transform.position,out var hit);
                Add("wheel "+WheelNames[i]+" terrain below",support,"Static downward terrain query, not WheelCollider.GetGroundHit");
            }
            var shader=Shader.Find("Oheangbu/WorldMacroTexturedSurface");var errors=shader==null?new[]{"Missing shader"}:ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").Select(m=>m.message).ToArray();Add("copied surface shader",errors.Length==0,string.Join(";",errors));
            var filters=root.GetComponentsInChildren<MeshFilter>(true);var renderers=root.GetComponentsInChildren<Renderer>(true);
            Add("copied material slots present",renderers.Length>0&&renderers.All(r=>r.sharedMaterials.Length>0&&r.sharedMaterials.All(m=>m!=null)),"Presence alone is not texture validation; explicit binding checks follow");
            TextureEntry[] mappedTextures=Array.Empty<TextureEntry>();
            try{mappedTextures=ReadTextureManifest();Add("explicit texture manifest",true,"Cabin/Roof/Wheel mapped to files owned by Palanquin");}
            catch(Exception e){Add("explicit texture manifest",false,e.Message);}
            foreach(var part in Parts)
            {
                var mapping=mappedTextures.FirstOrDefault(e=>e.part==part);
                var materials=PartRenderers(root.transform,part).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
                Texture2D colour=mapping==null?null:AssetDatabase.LoadAssetAtPath<Texture2D>(mapping.baseColor),normal=mapping==null?null:AssetDatabase.LoadAssetAtPath<Texture2D>(mapping.normal);
                bool bound=colour!=null&&normal!=null&&materials.Length>0&&materials.All(m=>m!=null&&m.shader==shader&&m.HasProperty("_BaseMap")&&m.HasProperty("_BumpMap")&&m.GetTexture("_BaseMap")==colour&&m.GetTexture("_BumpMap")==normal&&m.GetFloat("_BumpScale")>0&&AssetDatabase.GetAssetPath(m).StartsWith(Folder+"/Materials/",StringComparison.Ordinal));
                Add(part+" base colour and normal binding",bound,mapping==null?"Missing mapping":mapping.baseColor+" | "+mapping.normal);
                var colourImporter=mapping==null?null:AssetImporter.GetAtPath(mapping.baseColor) as TextureImporter;
                var normalImporter=mapping==null?null:AssetImporter.GetAtPath(mapping.normal) as TextureImporter;
                bool import=colourImporter!=null&&normalImporter!=null&&colourImporter.textureType==TextureImporterType.Default&&colourImporter.sRGBTexture&&normalImporter.textureType==TextureImporterType.NormalMap&&!normalImporter.sRGBTexture&&!normalImporter.convertToNormalmap&&new[]{colourImporter,normalImporter}.All(i=>i.maxTextureSize==2048&&i.filterMode==FilterMode.Bilinear&&i.anisoLevel==4);
                Add(part+" owned texture import settings",import,"Base sRGB; authored normal map linear; max2048/Bilinear/aniso4. Original source assets outside Palanquin untouched by this code.");
            }
            float track=0,wheelbase=0;
            if(configured){var w=car.Wheels.Select(b=>root.transform.InverseTransformPoint(b.Collider.transform.position)).ToArray();track=(Mathf.Abs(w[1].x-w[0].x)+Mathf.Abs(w[3].x-w[2].x))*.5f;wheelbase=(w[0].z+w[1].z-w[2].z-w[3].z)*.5f;}
            var report=new SetupReport{utc=DateTime.UtcNow.ToString("o"),scenePath=SceneManager.GetActiveScene().path,position=root.transform.position,visualBoundsSize=LocalBounds(root.transform,root.transform).size,seatLocalPosition=seat!=null&&seat.SeatSocket!=null?root.transform.InverseTransformPoint(seat.SeatSocket.position):Vector3.zero,trackM=track,wheelbaseM=wheelbase,wheelRadiusM=car!=null&&car.Profile!=null?car.Profile.WheelRadius:0,roadClearanceM=clearance,terrainReliefM=relief,wheelColliders=root.GetComponentsInChildren<WheelCollider>(true).Length,materials=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().Count(),sources=Parts.Select(p=>Folder+"/Models/Palanquin_"+p+".fbx").ToArray(),textureManifest=mappedTextures,checks=checks.ToArray()};
            foreach(var filter in filters)if(filter.sharedMesh!=null)for(int s=0;s<filter.sharedMesh.subMeshCount;s++)report.meshTriangles+=(int)filter.sharedMesh.GetIndexCount(s)/3;
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/setup_audit.json",JsonUtility.ToJson(report,true));
            return string.Join("\n",checks.Select(c=>c.status+" "+c.name+" "+c.detail));
            void Add(string name,bool pass,string detail)=>checks.Add(new Check{name=name,status=pass?"PASS":"FAIL",detail=detail});
        }
        public static string Capture(string view)
        {
            RequireScene();if(view!="seated"&&view!="exterior")throw new ArgumentException("Expected seated or exterior.");
            float commit=Prologue.PrologueAudit.CommitRatio();if(commit>=.85f)throw new InvalidOperationException("Palanquin capture stopped: system commit >=85%; no render allocated.");
            var root=GameObject.Find(RootName);var seat=root==null?null:root.GetComponent<WorldMacroPalanquinSeat>();var source=seat==null?null:seat.ViewCamera;
            if(root==null||seat==null||seat.SeatSocket==null||source==null)throw new InvalidOperationException("Install and link the vehicle before capture.");
            Vector3 position,target;
            if(view=="seated"){position=seat.SeatSocket.position;target=position+seat.SeatSocket.forward*15;}
            else{var b=LocalBounds(root.transform,root.transform);target=root.transform.TransformPoint(b.center);position=target+root.transform.TransformDirection(new Vector3(5.2f,2.4f,6.8f));if(TryGround(position,out var floor))position.y=Mathf.Max(position.y,floor.point.y+1.8f);}
            var info=new CaptureInfo{view=view,file=view+".png",utc=DateTime.UtcNow.ToString("o"),scenePath=SceneManager.GetActiveScene().path,position=position,target=target,commitBefore=commit,fieldOfView=source.fieldOfView,nearClip=source.nearClipPlane};
            GameObject go=null;Camera camera=null;RenderTexture texture=null;Texture2D image=null;Material skyCopy=null;
            var oldActive=RenderTexture.active;var oldSky=RenderSettings.skybox;bool asyncBefore=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;if(oldSky!=null)skyCopy=new Material(oldSky){hideFlags=HideFlags.HideAndDontSave};
                go=new GameObject("Temporary_PalanquinCapture"){hideFlags=HideFlags.HideAndDontSave};camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
                camera.aspect=1920f/1080;camera.nearClipPlane=info.nearClip;camera.useOcclusionCulling=false;camera.layerCullDistances=new float[32];camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position,Vector3.up));
                var data=source.GetComponent<UniversalAdditionalCameraData>();if(data!=null)EditorUtility.CopySerialized(data,camera.GetUniversalAdditionalCameraData());
                Object.FindFirstObjectByType<WorldLookDriver>()?.PreviewRegionalSky(position);
                if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Palanquin capture stopped before rendering: system commit >=85%.");
                texture=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
                camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply(false);
                Directory.CreateDirectory(Output);File.WriteAllBytes(Output+"/"+info.file,image.EncodeToPNG());
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=asyncBefore;if(camera!=null)camera.targetTexture=null;RenderTexture.active=oldActive;
                if(texture!=null){texture.Release();Object.DestroyImmediate(texture);}if(image!=null)Object.DestroyImmediate(image);if(go!=null)Object.DestroyImmediate(go);
                if(oldSky!=null&&skyCopy!=null)oldSky.CopyPropertiesFromMaterial(skyCopy);RenderSettings.skybox=oldSky;if(skyCopy!=null)Object.DestroyImmediate(skyCopy);
            }
            info.commitAfter=Prologue.PrologueAudit.CommitRatio();File.WriteAllText(Output+"/"+view+".json",JsonUtility.ToJson(info,true));
            return view+" 1920x1080 still: "+Output+"/"+info.file+"; live boarding/driving unverified.";
        }
    }
}
