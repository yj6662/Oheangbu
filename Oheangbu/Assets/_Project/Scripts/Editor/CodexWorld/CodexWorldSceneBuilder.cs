using System;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.BrushRender;
using Oheangbu.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // SPEC-DEV-CODEX-WORLD. C2 was selected by the user as the canonical world on 2026-09-07.
    public static class CodexWorldSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Dev/C2_CodexWorld.unity";
        public const string AssetFolder = "Assets/_Project/Art/CodexWorld";
        public const string MeshFolder = AssetFolder + "/Meshes";
        public const string SettingsPath = "Assets/_Project/Data/World/CodexWorld_Settings.asset";
        private static System.Random rng;
        private static CodexWorldSettingsSO settings;
        private static readonly List<Mesh> rocks = new List<Mesh>();
        private static readonly List<Mesh> pineTrunks = new List<Mesh>();
        private static readonly List<Mesh> pineCrowns = new List<Mesh>();
        private static Material stone, paleStone, dark, wood, foliage;

        [MenuItem("Oheangbu/Dev/Codex World/Build canonical world (regenerate scene)")]
        public static void BuildMenu() => Debug.Log(BuildScene());

        public static string BuildScene()
        {
            if (EditorApplication.isPlaying) return "FAIL: stop Play mode first";
            var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (previous.isDirty) return "FAIL: save current scene before regenerating the canonical world";
            if (Shader.Find("Oheangbu/CodexInkLandscape") == null) return "FAIL: CodexInkLandscape not imported";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DevSceneKit.EnsureFolder(MeshFolder);
            DevSceneKit.EnsureFolder(AssetFolder + "/Materials");
            DevSceneKit.EnsureFolder("Assets/_Project/Data/World");
            settings = AssetDatabase.LoadAssetAtPath<CodexWorldSettingsSO>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<CodexWorldSettingsSO>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            rng = new System.Random(settings.Seed);
            stone = Ink("Granite", 1.06f, .25f, .07f, .76f, .65f);
            paleStone = Ink("PaleStone", .88f, .35f, .16f, .83f, .7f);
            dark = Ink("Mine", 1.65f, .07f, .015f, .43f, .3f);
            wood = Ink("Timber", 1.25f, .2f, .025f, .66f, .65f, true);
            foliage = Ink("PineNeedles", 1.9f, .1f, .025f, .25f, .75f);
            var plaster = Ink("Plaster", .65f, .4f, .48f, .86f, .5f);
            var roof = Ink("RoofTiles", 1.4f, .18f, .055f, .39f, .55f);
            var lantern = MaterialAt("Lantern", "Oheangbu/CodexInkLantern", null);
            var vein = MaterialAt("Vein", "Oheangbu/InkLightSource", m => {m.SetFloat("_UseVeinColor",1);m.SetFloat("_Cull",0);m.SetFloat("_BandWidth",.32f);});

            rocks.Clear(); pineTrunks.Clear(); pineCrowns.Clear();
            for (int i = 0; i < 12; i++) rocks.Add(SaveMesh("Granite_" + i, CodexWorldGeometry.Rock(settings.Seed + i * 197, settings.RockResolution.x, settings.RockResolution.y)));
            for (int i = 0; i < 4; i++) BuildPineMeshes(i);
            var groundSource = CodexWorldGeometry.Ground(settings.Seed, settings.GroundSize.x, settings.GroundSize.y, settings.GroundResolution.x, settings.GroundResolution.y);
            LevelInnSite(groundSource);
            var ground = SaveMesh("Ground", groundSource);
            var tunnel = SaveMesh("MineTunnel", CodexWorldGeometry.Tunnel(settings.Seed, settings.TunnelLength, 7f, 6f));
            AssetDatabase.SaveAssets();

            var world = new GameObject("CodexWorld — 墨嶺客棧").transform;
            var valley = Group(world, "01_Valley_and_Stone_Path");
            MeshObject(valley, "Valley", ground, stone, Vector3.zero, Vector3.one, true);
            BuildMine(Group(world, "00_Abandoned_Mine"), tunnel, vein);
            BuildCliffs(Group(world, "02_Granite_Escarpments"));
            BuildMountains(Group(world, "05_Distant_Ink_Mountains"));
            BuildTrees(Group(world, "03_Windswept_Pines"));
            BuildUndergrowth(Group(world, "06_Dry_Grass_and_Path_Edges"));

            var innPosition = settings.InnAnchor;
            innPosition.y = CodexWorldGeometry.Height(innPosition.x, innPosition.z);
            Group(world, "04_Mountain_Inn"); // The selected thatched source is installed after scene assets are saved.
            // Shallow stepping stones join the inn's front porch to the main path.
            for (int i = 0; i < 9; i++)
            {
                float t = i / 8f;
                Vector3 p = Vector3.Lerp(new Vector3(CodexWorldGeometry.PathX(29), 0, 29), innPosition + new Vector3(0, 0, -5), t);
                p.y = SiteHeight(p.x, p.z) + .05f;
                Rock(valley, "Inn_Approach_" + i, p, new Vector3(1.4f, .1f, .6f), paleStone, false);
            }

            var sun = new GameObject("Sun_White").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = Color.white; sun.intensity = settings.SunIntensity;
            sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(settings.SunEuler);
            RenderSettings.sun = sun; RenderSettings.fog = false;
            RenderSettings.skybox = null; RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            var volume = new GameObject("GlobalVolume_Codex_NoBloom").AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 100;
            const string volumePath = AssetFolder + "/Codex_NoBloom.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(volumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, volumePath);
                var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0);
                AssetDatabase.AddObjectToAsset(bloom, profile);
                EditorUtility.SetDirty(profile);
            }
            volume.sharedProfile = profile;

            // References loaded after asset creation, avoiding stale object references during import.
            var palette = AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(DevSceneKit.PalettePath);
            settings = AssetDatabase.LoadAssetAtPath<CodexWorldSettingsSO>(SettingsPath);
            var rig = DevSceneKit.InstantiateRig(scene, settings.Spawn, 0f);
            // A scene-local camera config starts scenery review at the intended 1.8 m eye.
            const string cameraConfigPath = AssetFolder + "/Codex_CameraConfig.asset";
            if (AssetDatabase.LoadAssetAtPath<Oheangbu.Combat.CombatConfigSO>(cameraConfigPath) == null)
            {
                AssetDatabase.CopyAsset(DevSceneKit.DefaultConfigPath, cameraConfigPath);
                var configObject=new SerializedObject(AssetDatabase.LoadAssetAtPath<Oheangbu.Combat.CombatConfigSO>(cameraConfigPath));
                configObject.FindProperty("_shoulderStart").boolValue=false;
                configObject.ApplyModifiedPropertiesWithoutUndo();
            }
            var cameraRig=rig.GetComponentInChildren<Oheangbu.Combat.CameraRigController>(true);
            var cameraRigData=new SerializedObject(cameraRig);
            cameraRigData.FindProperty("_config").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Oheangbu.Combat.CombatConfigSO>(cameraConfigPath);
            cameraRigData.ApplyModifiedPropertiesWithoutUndo();
            var camera = rig.GetComponentInChildren<Camera>(true);
            camera.fieldOfView = 60; camera.farClipPlane = 1200;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var driver = new GameObject("WorldLook").AddComponent<WorldLookDriver>();
            var serialized = new SerializedObject(driver);
            serialized.FindProperty("_palette").objectReferenceValue = palette;
            serialized.FindProperty("_veinInitial").intValue = 'ㄱ';
            serialized.FindProperty("_beaconInitial").intValue = 'ㄴ';
            serialized.FindProperty("_skyCamera").objectReferenceValue = camera;
            serialized.ApplyModifiedPropertiesWithoutUndo(); driver.Apply();

            DevSceneKit.EnsureFolder("Assets/_Project/Scenes/Dev");
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!buildScenes.Exists(s => s.path == ScenePath))
            {
                buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
            }
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = world.gameObject;
            if (saved)
            {
                string lodResult = CodexMountainLOD.Install();
                if (!lodResult.StartsWith("PASS:", StringComparison.Ordinal)) return lodResult;
            }
            if (saved && settings.UseDerivedRockKit)
            {
                string replacement = CodexRockKit.ReplaceAll();
                if (!replacement.StartsWith("OK:", StringComparison.Ordinal)) return replacement;
            }
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAtDirect(new Vector3(0, 5, 18), Quaternion.Euler(9, 0, 0), 28);
            if (saved)
            {
                string pathResult = CodexGroundPath.Apply();
                if (!pathResult.StartsWith("PASS:", StringComparison.Ordinal)) return pathResult;
                string innResult = CodexThatchedInn.Apply();
                if (!innResult.StartsWith("PASS:", StringComparison.Ordinal)) return innResult;
            }
            return $"{(saved ? "OK" : "FAIL")}: {ScenePath}; seed={settings.Seed}; walkable valley={settings.GroundSize}; spawn={settings.Spawn}";
        }

        private static void BuildMine(Transform parent, Mesh tunnel, Material vein)
        {
            MeshObject(parent, "Irregular_Arched_Tunnel", tunnel, dark, Vector3.zero, Vector3.one, true)
                .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
            Cube(parent, "Mine_Floor", new Vector3(0,-.2f,-settings.TunnelLength/2), new Vector3(12,.4f,settings.TunnelLength), dark);
            Cube(parent, "Closed_Workface", new Vector3(0,2.8f,-settings.TunnelLength), new Vector3(7,6,.5f), dark);
            for (int i = 0; i < 7; i++)
            {
                float z = -2.2f - i * 4.4f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Beam(parent, "Mine_Post", new Vector3(side*2.85f,0,z), new Vector3(side*2.65f,4.15f,z), .26f, wood);
                    Beam(parent, "Mine_KneeBrace", new Vector3(side*2.75f,3.0f,z), new Vector3(side*1.6f,4.5f,z), .18f, wood);
                }
                Beam(parent, "Mine_Lintel", new Vector3(-2.9f,4.35f,z), new Vector3(2.9f,4.35f,z), .32f, wood);
            }
            // The opening is held by uneven granite masses, leaving a clear walkable centre.
            for (int side = -1; side <= 1; side += 2)
                for (int k = 0; k < 5; k++)
                    Rock(parent, "Portal_Crag", new Vector3(side*(5.8f+k*2.5f),2.8f+k*.4f,1+k*.85f),
                        new Vector3(2.5f+k*.4f,4.4f+k*.6f,3f), k<2?dark:stone, true);
            Rock(parent, "Portal_Crown", new Vector3(-.9f,5.1f,5f), new Vector3(5.1f,1.4f,2.7f),dark,true);
            Rock(parent, "Portal_Overburden", new Vector3(0f,10f,4f), new Vector3(7f,5.5f,5f),dark,true);
            Rock(parent, "Portal_Keystone", new Vector3(3.0f,5.9f,-.4f), new Vector3(1.3f,1.9f,1.8f),dark,true);
            for (int i=0;i<3;i++)
            {
                float z=-9-i*9;
                var strip=GameObject.CreatePrimitive(PrimitiveType.Quad); strip.name="Wood_Mineral_Seam";
                strip.transform.SetParent(parent,false); strip.transform.position=new Vector3(3.04f,1.75f,z);
                strip.transform.rotation=Quaternion.Euler(0,90,-12+i*9);strip.transform.localScale=new Vector3(3.8f,1.5f,1);
                strip.GetComponent<MeshRenderer>().sharedMaterial=vein;Object.DestroyImmediate(strip.GetComponent<Collider>());
                var light=new GameObject("Vein_White_Wash").AddComponent<Light>();light.transform.SetParent(parent,false);
                light.transform.position=new Vector3(2.2f,2,z);light.type=LightType.Point;light.color=Color.white;light.intensity=1.2f;light.range=7;
            }
            for(int i=0;i<28;i++)
            {
                float side=i%2==0?-1:1;
                Rock(parent,"Mine_Rubble",new Vector3(side*R(2.45f,3.05f),.15f,R(-32,-1)),new Vector3(R(.2f,.55f),R(.15f,.4f),R(.3f,.7f)),dark,false);
            }
        }

        private static void BuildCliffs(Transform parent)
        {
            // Large directed masses first; minor fragments never obscure the traversable corridor.
            for(int side=-1;side<=1;side+=2)
                for(int i=0;i<10;i++)
                {
                    float z=15+i*19;
                    float x=CodexWorldGeometry.PathX(z)+side*R(27,43);
                    float h=R(6,14)+(i>7?6:0);
                    var p=new Vector3(x,CodexWorldGeometry.Height(x,z)+h*.2f,z);
                    var go=Rock(parent,"Granite_Buttress",p,new Vector3(R(5,9),h,R(4,9)),i%3==0?paleStone:stone,true);
                    go.transform.rotation=Quaternion.Euler(R(-12,12),R(-24,24),side*R(5,15));
                    for(int crease=0;crease<3;crease++)
                        Rock(parent,"Cliff_Stratum",p+new Vector3(side*R(-3,3),R(-2,4),-4f-crease*.55f),new Vector3(R(1,2.5f),h*.7f,R(.5f,1.2f)),stone,false);
                }
            for(int i=0;i<settings.ScatteredRockCount;i++)
            {
                float z=R(5,settings.GroundSize.y-5),side=i%2==0?-1:1;
                float x=CodexWorldGeometry.PathX(z)+side*R(4,22);
                if(Vector2.Distance(new Vector2(x,z),new Vector2(settings.InnAnchor.x,settings.InnAnchor.z))<11)continue;
                var p=new Vector3(x,CodexWorldGeometry.Height(x,z),z);
                float size=R(.25f,2.4f);
                Rock(parent,"Weathered_Stone",p,new Vector3(size*R(.7f,1.3f),size*.55f,size),i%4==0?paleStone:stone,size>1.3f);
            }
        }

        private static void BuildMountains(Transform parent)
        {
            float[] depth={245,390,610,880};
            float[] height={82,125,185,235};
            var mat = SharedMountainMaterial(settings);
            for(int layer=0;layer<4;layer++)
            {
                for(int side=-1;side<=1;side+=2)
                {
                    var mesh=SaveMesh($"Ridge_{layer}_{side}",CodexWorldGeometry.Ridge(settings.Seed+layer*977+side*31,300+layer*200,height[layer],settings.MountainResolution.x,settings.MountainResolution.y));
                    float x=side*(155+layer*115)+(layer%2==0?35:-35);
                    MeshObject(parent,$"Mountain_Layer_{layer}_{side}",mesh,mat,new Vector3(x,-3-layer*4,depth[layer]+side*19),Vector3.one,false);
                }
            }
            var saddle=SaveMesh("Ridge_Saddle",CodexWorldGeometry.Ridge(settings.Seed+5743,330,105,settings.MountainResolution.x,settings.MountainResolution.y));
            MeshObject(parent,"Distant_Central_Saddle",saddle,mat,new Vector3(-10,-8,400),Vector3.one,false);
            foreach (var renderer in parent.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static void BuildTrees(Transform parent)
        {
            Pine(parent,new Vector3(11,0,19),1.25f,0, -24);
            Pine(parent,new Vector3(-21,0,47),1.0f,1,35);
            Pine(parent,new Vector3(22,0,59),1.2f,2,160);
            for(int i=0;i<settings.PineCount;i++)
            {
                float z=R(25,205),x=CodexWorldGeometry.PathX(z)+(i%2==0?-1:1)*R(13,35);
                if(Vector2.Distance(new Vector2(x,z),new Vector2(settings.InnAnchor.x,settings.InnAnchor.z))<13)continue;
                Pine(parent,new Vector3(x,0,z),R(.48f,.98f),i%4,R(0,360));
            }
        }

        private static void BuildUndergrowth(Transform parent)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int clump=0;clump<650;clump++)
            {
                float z=R(4,215),side=clump%2==0?-1:1;
                float x=CodexWorldGeometry.PathX(z)+side*R(2.7f,12f);
                if(Vector2.Distance(new Vector2(x,z),new Vector2(settings.InnAnchor.x,settings.InnAnchor.z))<9)continue;
                var basePoint=new Vector3(x,SiteHeight(x,z)-.02f,z);
                float height=R(.15f,.6f);
                for(int blade=0;blade<7;blade++)
                {
                    float angle=R(0,Mathf.PI*2);var spread=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                    var across=Vector3.Cross(Vector3.up,spread)*R(.015f,.038f);
                    var p=basePoint+spread*R(0,.2f);var knee=p+Vector3.up*height*.55f+spread*.1f;
                    var tip=p+Vector3.up*height*R(.75f,1.3f)+spread*R(.2f,.45f);
                    int k=vertices.Count;vertices.Add(p-across);vertices.Add(p+across);vertices.Add(knee+across*.6f);vertices.Add(knee-across*.6f);vertices.Add(tip);
                    triangles.AddRange(new[]{k,k+2,k+1,k,k+3,k+2,k+3,k+4,k+2});
                }
            }
            var m=new Mesh{name="Dry_Grass"};m.SetVertices(vertices);m.SetTriangles(triangles,0);m.RecalculateNormals();m.RecalculateBounds();
            var grassMat=Ink("DryGrass",1.4f,.16f,.03f,.32f,.72f,true);grassMat.SetFloat("_Cull",0);grassMat.SetFloat("_StrokeStrength",0);
            MeshObject(parent,"Sparse_Ink_Grasses",SaveMesh("Dry_Grass",m),grassMat,Vector3.zero,Vector3.one,false);
        }

        private static void Pine(Transform parent,Vector3 p,float size,int variant,float yaw)
        {
            p.y=CodexWorldGeometry.Height(p.x,p.z);
            var root=Group(parent,"Korean_Pine_"+variant);root.position=p;root.rotation=Quaternion.Euler(0,yaw,0);root.localScale=Vector3.one*size;
            MeshObject(root,"Bent_Trunk_and_Branches",pineTrunks[variant],wood,Vector3.zero,Vector3.one,false);
            MeshObject(root,"Broken_Ink_Canopy",pineCrowns[variant],foliage,Vector3.zero,Vector3.one,false);
            var c=root.gameObject.AddComponent<CapsuleCollider>();c.center=new Vector3(.25f,2.3f,0);c.height=4.6f;c.radius=.4f;
        }

        private static void BuildPineMeshes(int variant)
        {
            var trunks=new List<CombineInstance>();var centres=new List<Vector3>();var radii=new List<Vector3>();var temporary=new List<Mesh>();
            float lean=1.8f+variant*.25f;
            Vector3[] spine={Vector3.zero,new Vector3(.35f,1.5f,.15f),new Vector3(-.4f,3.3f,.2f),new Vector3(.6f,5.2f,.1f),new Vector3(lean,7,.1f),new Vector3(lean+.4f,8.8f,0),new Vector3(lean+1.1f,10.2f,.1f)};
            var trunk=CodexWorldGeometry.Branch(spine,new[]{.52f,.42f,.33f,.26f,.19f,.12f,.025f},9);temporary.Add(trunk);
            trunks.Add(new CombineInstance{mesh=trunk,transform=Matrix4x4.identity});
            for(int b=0;b<9;b++)
            {
                int knot=2+b/2;float sign=b%2==0?-1:1;float angle=b*2.4f+variant;
                Vector3 origin=spine[Mathf.Min(knot,5)];
                Vector3 end=origin+new Vector3(sign*R(2.8f,5.3f)*(1-b*.045f),R(.2f,1.25f),Mathf.Sin(angle)*2.8f);
                var branch=CodexWorldGeometry.Branch(new[]{origin,Vector3.Lerp(origin,end,.4f)+Vector3.down*.45f,end,end+new Vector3(sign*.55f,.55f,0)},new[]{.14f,.10f,.055f,.009f},6);
                temporary.Add(branch);trunks.Add(new CombineInstance{mesh=branch,transform=Matrix4x4.identity});
                for(int tuft=0;tuft<3;tuft++)
                {
                    Vector3 centre=end+new Vector3(R(-.9f,.9f),R(.1f,.5f),R(-1,1));
                    centres.Add(centre);radii.Add(new Vector3(R(1.2f,2.4f),R(.23f,.5f),R(.8f,1.6f)));
                }
            }
            centres.Add(spine[6]);radii.Add(new Vector3(2.1f,.45f,1.7f));
            var tm=new Mesh();tm.CombineMeshes(trunks.ToArray(),true,true);pineTrunks.Add(SaveMesh("Pine_Trunk_"+variant,tm));
            pineCrowns.Add(SaveMesh("Pine_Canopy_"+variant,CodexWorldFoliage.Canopy(centres.ToArray(),radii.ToArray(),settings.Seed+variant*137)));
            foreach(var m in temporary)Object.DestroyImmediate(m);
        }

        private static void LevelInnSite(Mesh mesh)
        {
            var v=mesh.vertices;
            for(int i=0;i<v.Length;i++)
                v[i].y=SiteHeight(v[i].x,v[i].z);
            mesh.vertices=v;mesh.RecalculateNormals();mesh.RecalculateBounds();
        }

        private static float SiteHeight(float x,float z)
        {
            float level=CodexWorldGeometry.Height(settings.InnAnchor.x,settings.InnAnchor.z);
            var local=Quaternion.Euler(0,-settings.InnYaw,0)*(new Vector3(x,0,z)-settings.InnAnchor);
            float edge=Mathf.Max(Mathf.Abs(local.x)-6.4f,Mathf.Abs(local.z)-4.7f);
            float blend=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(edge/2.5f));
            return Mathf.Lerp(CodexWorldGeometry.Height(x,z),level,blend);
        }

        // Explicit first visual review: edits only comparison materials; ordinary rebuilds preserve tuning.
        public static string ApplyFirstReview()
        {
            void Tune(string name,Action<Material> apply)
            {var m=AssetDatabase.LoadAssetAtPath<Material>(AssetFolder+"/Materials/"+name+".mat");if(m==null)return;apply(m);EditorUtility.SetDirty(m);}
            Tune("Path",m=>{m.SetFloat("_ToneFloor",.18f);m.SetFloat("_ToneCeiling",.66f);m.SetFloat("_InkDensity",1);m.SetFloat("_NoiseStrength",.26f);m.SetFloat("_BrushStrength",.1f);});
            Tune("Granite",m=>{m.SetFloat("_ToneFloor",.015f);m.SetFloat("_ToneCeiling",.60f);m.SetFloat("_NoiseStrength",.23f);m.SetFloat("_BrushStrength",.38f);m.SetFloat("_LightResponse",.55f);});
            Tune("PaleStone",m=>{m.SetFloat("_ToneFloor",.09f);m.SetFloat("_ToneCeiling",.72f);m.SetFloat("_NoiseStrength",.20f);m.SetFloat("_BrushStrength",.3f);});
            Tune("Mine",m=>{m.SetFloat("_NoiseStrength",.25f);m.SetFloat("_BrushStrength",.48f);m.SetFloat("_AddLightGain",1.5f);});
            Tune("PineNeedles",m=>{m.SetFloat("_Cull",0);m.SetFloat("_ToneFloor",0);m.SetFloat("_ToneCeiling",.15f);});
            Tune("RoofTiles",m=>m.SetFloat("_Cull",0));
            for(int i=0;i<4;i++){int layer=i;Tune("Ridge_"+i,m=>{m.SetFloat("_WashStrength",.35f+layer*.19f);m.SetFloat("_ToneFloor",.05f+layer*.1f);m.SetFloat("_NoiseStrength",.2f);m.SetFloat("_BrushStrength",.35f);});}
            string[] names={"Granite","PaleStone","Mine","Path","Timber","Plaster","RoofTiles","PineNeedles","Ridge_0","Ridge_1","Ridge_2","Ridge_3"};
            float[] scales={.55f,.55f,.9f,.65f,.9f,.7f,.9f,.8f,.1f,.06f,.035f,.025f};
            float[] strengths={.65f,.65f,.7f,.08f,.4f,.06f,.16f,.1f,.4f,.3f,.2f,.12f};
            for(int i=0;i<names.Length;i++){int k=i;Tune(names[i],m=>{m.SetFloat("_StrokeScale",scales[k]);m.SetFloat("_StrokeStrength",strengths[k]);});}
            Tune("Path",m=>m.SetFloat("_PathEdge",1));
            AssetDatabase.SaveAssets();return "OK: comparison material review applied";
        }

        [MenuItem("Oheangbu/Dev/Codex World/Apply shared mountain surface and atmosphere")]
        private static void ApplyCanonicalLookMenu() => Debug.Log(ApplyCanonicalLook());

        public static string ApplyCanonicalLook() => ApplyRealisticMountains(false);

        [MenuItem("Oheangbu/Dev/Codex World/Refresh realistic mountains (preserve placement)")]
        private static void RefreshRealisticMountainsMenu() => Debug.Log(RefreshRealisticMountains());

        public static string RefreshRealisticMountains() => ApplyRealisticMountains(true);

        private static string ApplyRealisticMountains(bool geometry)
        {
            if (EditorApplication.isPlaying) return "FAIL: stop Play before authoring";
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath || scene.isDirty) return "FAIL: open and save C2 before applying mountains";
            var data = AssetDatabase.LoadAssetAtPath<CodexWorldSettingsSO>(SettingsPath);
            if (data == null || Shader.Find("Oheangbu/CodexMountain") == null) return "FAIL: settings or mountain shader missing";
            var targets = new List<MeshRenderer>();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null
                    && AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(MeshFolder + "/Ridge_", StringComparison.Ordinal))
                    targets.Add(renderer);
            }
            if (targets.Count != 9) return "FAIL: expected 9 canonical mountain renderers, found " + targets.Count;
            if (geometry)
            {
                float[] heights = { 82, 125, 185, 235 };
                for (int layer = 0; layer < 4; layer++)
                for (int side = -1; side <= 1; side += 2)
                    SaveMesh($"Ridge_{layer}_{side}", CodexWorldGeometry.Ridge(data.Seed + layer * 977 + side * 31,
                        300 + layer * 200, heights[layer], data.MountainResolution.x, data.MountainResolution.y));
                SaveMesh("Ridge_Saddle", CodexWorldGeometry.Ridge(data.Seed + 5743, 330, 105,
                    data.MountainResolution.x, data.MountainResolution.y));
            }
            var material = SharedMountainMaterial(data);
            foreach (var renderer in targets)
            {
                Undo.RecordObject(renderer, "Apply shared mountain atmosphere");
                renderer.sharedMaterial = material;
                renderer.SetPropertyBlock(null);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                EditorUtility.SetDirty(renderer);
                CodexMountainLOD.Configure(renderer, data);
            }
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            return $"{(saved ? "OK" : "FAIL")}: mountains={targets.Count}; sharedMaterials=1; "
                + $"geometryUpdated={geometry}; resolution={data.MountainResolution}; sceneSaved={saved}; placement preserved";
        }

        private static Material SharedMountainMaterial(CodexWorldSettingsSO data)
        {
            var m = MaterialAt("Mountain_Shared", "Oheangbu/CodexMountain", null);
            m.shader = Shader.Find("Oheangbu/CodexMountain");
            m.SetFloat("_RockTone", data.MountainRockTone);
            m.SetFloat("_Ambient", data.MountainAmbient);
            m.SetFloat("_Diffuse", data.MountainDiffuse);
            m.SetFloat("_Variation", data.MountainSurfaceVariation);
            m.SetFloat("_AtmosphereStart", data.MountainAtmosphereStart);
            m.SetFloat("_AtmosphereDensity", data.MountainAtmosphereDensity);
            m.SetFloat("_AtmosphereTone", data.MountainAtmosphereTone);
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("Oheangbu/Dev/Codex World/Refresh smooth geometry (preserve placement)")]
        private static void RefreshCanonicalGeometryMenu() => Debug.Log(RefreshCanonicalGeometry());

        /// <summary>
        /// Updates C2-owned mesh assets in place, preserving their GUIDs and every placed
        /// instance. Prefer this to BuildScene after the world contains hand-placed assets.
        /// The shared Height/PathX function, valley floor and inn approach are unchanged.
        /// </summary>
        public static string RefreshCanonicalGeometry()
        {
            if (EditorApplication.isPlaying) return "FAIL: stop Play mode before mesh authoring";
            var loadedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (loadedScene.path != ScenePath) return "FAIL: open C2 before refreshing its geometry and LODs";
            if (loadedScene.path == ScenePath && loadedScene.isDirty) return "FAIL: save C2 before refreshing geometry";
            var data = AssetDatabase.LoadAssetAtPath<CodexWorldSettingsSO>(SettingsPath);
            if (data == null) return "FAIL: canonical world settings asset is missing";
            if (!AssetDatabase.IsValidFolder(MeshFolder)) return "FAIL: build canonical world assets first";
            var updated = new HashSet<Mesh>();
            for (int i = 0; i < 12; i++)
                updated.Add(SaveMesh("Granite_" + i, CodexWorldGeometry.Rock(data.Seed + i * 197,
                    data.RockResolution.x, data.RockResolution.y)));
            float[] heights = { 82, 125, 185, 235 };
            for (int layer = 0; layer < 4; layer++)
            for (int side = -1; side <= 1; side += 2)
                updated.Add(SaveMesh($"Ridge_{layer}_{side}", CodexWorldGeometry.Ridge(data.Seed + layer * 977 + side * 31,
                    300 + layer * 200, heights[layer], data.MountainResolution.x, data.MountainResolution.y)));
            updated.Add(SaveMesh("Ridge_Saddle", CodexWorldGeometry.Ridge(data.Seed + 5743, 330, 105,
                data.MountainResolution.x, data.MountainResolution.y)));
            updated.Add(SaveMesh("MineTunnel", CodexWorldGeometry.Tunnel(data.Seed, data.TunnelLength, 7f, 6f)));

            int colliders = 0;
            foreach (var collider in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mesh = collider.sharedMesh;
                if (collider.gameObject.scene.path != ScenePath || mesh == null || !updated.Contains(mesh)) continue;
                // Force PhysX to recook native buffers changed on the same Mesh object.
                collider.sharedMesh = null; collider.sharedMesh = mesh; colliders++;
            }
            long vertices = 0, triangles = 0;
            foreach (var mesh in updated) { vertices += mesh.vertexCount; triangles += mesh.GetIndexCount(0) / 3; }
            if (loadedScene.path == ScenePath)
            {
                foreach (var renderer in CodexMountainLOD.Originals()) CodexMountainLOD.Configure(renderer, data);
                EditorSceneManager.MarkSceneDirty(loadedScene);
                EditorSceneManager.SaveScene(loadedScene);
            }
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return $"OK: refreshed {updated.Count} owned mesh assets, {vertices} unique vertices/{triangles} unique tris; "
                + $"recooked {colliders} loaded C2 colliders; GUIDs, placements, ground and path preserved";
        }

        private static Material Ink(string name,float density,float ambient,float floor,float ceiling,float wash,bool timber=false)
        {
            return MaterialAt(name,"Oheangbu/CodexInkLandscape",m=>{
                m.SetFloat("_InkDensity",density);m.SetFloat("_AmbientLevel",ambient);m.SetFloat("_ToneFloor",floor);m.SetFloat("_ToneCeiling",ceiling);
                m.SetFloat("_WashStart",settings.WashStart);m.SetFloat("_WashEnd",settings.WashEnd);m.SetFloat("_WashStrength",wash);
                m.SetFloat("_UseWoodColor",timber?1:0);m.SetFloat("_TintRetain",timber?.23f:0);m.SetFloat("_LightResponse",.7f);
                m.SetFloat("_NoiseScale",.55f);m.SetFloat("_NoiseStrength",.1f);m.SetFloat("_RimStrength",.05f);
            });
        }
        private static Material MaterialAt(string name,string shaderName,Action<Material> configure)
        {
            string file=AssetFolder+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(file);if(m!=null)return m;
            var shader=Shader.Find(shaderName);if(shader==null)throw new InvalidOperationException("Missing shader "+shaderName);
            m=new Material(shader){name=name};configure?.Invoke(m);AssetDatabase.CreateAsset(m,file);return m;
        }
        private static Mesh SaveMesh(string name,Mesh mesh)
        {
            string file=MeshFolder+"/"+name+".asset";mesh.name=name;var old=AssetDatabase.LoadAssetAtPath<Mesh>(file);
            if(old!=null)
            {
                // Updating native buffers explicitly also invalidates the renderer's GPU data.
                old.Clear(); old.indexFormat=mesh.indexFormat;old.vertices=mesh.vertices;old.normals=mesh.normals;
                old.uv=mesh.uv;old.colors=mesh.colors;old.triangles=mesh.triangles;old.bounds=mesh.bounds;
                old.UploadMeshData(false);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;
            }
            AssetDatabase.CreateAsset(mesh,file);return mesh;
        }
        private static Transform Group(Transform parent,string name){var g=new GameObject(name).transform;g.SetParent(parent,false);return g;}
        private static GameObject MeshObject(Transform parent,string name,Mesh mesh,Material material,Vector3 p,Vector3 scale,bool collision)
        {var g=new GameObject(name);g.transform.SetParent(parent,false);g.transform.localPosition=p;g.transform.localScale=scale;g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=material;if(collision)g.AddComponent<MeshCollider>().sharedMesh=mesh;g.isStatic=true;return g;}
        private static GameObject Rock(Transform parent,string name,Vector3 p,Vector3 scale,Material material,bool collision)
        {var g=MeshObject(parent,name,rocks[rng.Next(rocks.Count)],material,p,scale,collision);g.transform.localRotation=Quaternion.Euler(R(-8,8),R(0,360),R(-8,8));return g;}
        private static GameObject Cube(Transform parent,string name,Vector3 p,Vector3 scale,Material material)=>DevSceneKit.AddCube(parent,name,p,scale,material);
        private static void Beam(Transform parent,string name,Vector3 a,Vector3 b,float width,Material material)
        {var g=Cube(parent,name,(a+b)*.5f,new Vector3(width,Vector3.Distance(a,b),width),material);g.transform.rotation=Quaternion.FromToRotation(Vector3.up,b-a);}
        private static float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
    }
}
