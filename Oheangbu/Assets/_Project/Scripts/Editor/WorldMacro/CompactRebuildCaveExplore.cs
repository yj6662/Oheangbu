using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRebuildAuthoring
    {
        static string CaveExploreBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode required");
            var receipt = JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output + "/migration_slice.json"));
            string folder = Path.GetDirectoryName(receipt.scene).Replace('\\', '/') + "/Cave235";
            DevSceneKit.EnsureFolder(folder);
            var original = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(receipt.scene, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var session = roots.SelectMany(r => r.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
                var ui = roots.SelectMany(r => r.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
                var mine = roots.Single(g => g.name == "mine");
                var zone = ui.MapData.Zones.Single(z => z.Id == "mine_interior");
                zone.ExploreWalkedPassages = true; zone.DiscoveryRevision = "cave-v4-2m-235";
                var previous = mine.transform.Find("Cave_Exploration235");
                if (previous != null) Object.DestroyImmediate(previous.gameObject);
                var root = new GameObject("Cave_Exploration235").transform; root.SetParent(mine.transform, false);
                var art = roots.SelectMany(r => r.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single().Art;
                var timber = art.Prototypes.First(p => p.Id.Contains("WoodLog")).Lods[0].Parts[0].Material;
                var metal = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                metal.SetColor("_BaseColor", new Color(.11f,.12f,.11f)); metal.SetFloat("_Smoothness", .08f);
                metal = SurveyMaterial(metal, folder + "/WornIron.mat");
                var floor = mine.GetComponentsInChildren<MeshCollider>(true).Single(c => c.name == "Natural_Cave_Floor");
                Vector3 Ground(float x, float z)
                {
                    if (!floor.Raycast(new Ray(new Vector3(x,145,z),Vector3.down), out var hit,20)) throw new Exception("Prop lacks cave floor: " + x + "," + z);
                    return hit.point;
                }
                Transform Group(string name, Vector3 p, float yaw)
                {
                    var g = new GameObject(name).transform; g.SetParent(root, false); g.position = p; g.rotation = Quaternion.Euler(0,yaw,0); return g;
                }
                void Part(Transform parent, string name, Vector3 p, Vector3 scale, Vector3 rotation, Material mat, PrimitiveType kind = PrimitiveType.Cube)
                {
                    var g = GameObject.CreatePrimitive(kind); g.name = name; g.transform.SetParent(parent,false);
                    g.transform.localPosition = p; g.transform.localScale = scale; g.transform.localEulerAngles = rotation;
                    g.GetComponent<Renderer>().sharedMaterial = mat; Object.DestroyImmediate(g.GetComponent<Collider>());
                }
                var cart = Group("Abandoned_HaulCart", Ground(3534,1842), 28);
                for (int i=0;i<5;i++) Part(cart,"BedPlank_"+i,new Vector3((i-2)*.19f,.46f,0),new Vector3(.17f,.08f,1.5f),new Vector3(0,i*.7f,0),timber);
                foreach (float side in new[]{-1f,1f})
                {
                    Part(cart,"AxleRail",new Vector3(side*.38f,.34f,0),new Vector3(.12f,.15f,1.6f),Vector3.zero,timber);
                    Part(cart,"Handle",new Vector3(side*.35f,.4f,-1.15f),new Vector3(.09f,.09f,1.2f),new Vector3(5,0,0),timber);
                    Part(cart,"Wheel",new Vector3(side*.61f,.3f,.14f),new Vector3(.56f,.075f,.56f),new Vector3(0,0,90),timber,PrimitiveType.Cylinder);
                    Part(cart,"WheelPin",new Vector3(side*.7f,.3f,.14f),new Vector3(.12f,.04f,.12f),new Vector3(0,0,90),metal,PrimitiveType.Cylinder);
                }
                Part(cart,"SplitSide",new Vector3(.47f,.64f,.18f),new Vector3(.07f,.26f,1.05f),new Vector3(0,5,-24),timber);
                var beams = Group("Retired_SupportTimber",Ground(3537.2f,1788), 8);
                for(int i=0;i<3;i++) Part(beams,"SplitBeam_"+i,new Vector3(i*.26f,.12f+i*.08f,0),new Vector3(.2f,.23f,2.6f-i*.25f),new Vector3(0,i*4,0),timber);
                var bundle = Group("Left_WorkBundle",Ground(3503,1772.3f), 22);
                var source = mine.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Worker_Straw_Bag");
                if(source == null) throw new Exception("Existing straw bag visual not found");
                var meshes=source.GetComponentsInChildren<MeshFilter>(true);
                var bounds=meshes[0].GetComponent<Renderer>().bounds;
                foreach(var f in meshes) bounds.Encapsulate(f.GetComponent<Renderer>().bounds);
                var origin=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                foreach(var f in meshes)
                {
                    var g=new GameObject("WovenBag");g.transform.SetParent(bundle,false);
                    g.transform.localPosition=f.transform.position-origin;
                    g.transform.localRotation=f.transform.rotation;g.transform.localScale=f.transform.lossyScale;
                    g.AddComponent<MeshFilter>().sharedMesh=f.sharedMesh;g.AddComponent<MeshRenderer>().sharedMaterials=f.GetComponent<Renderer>().sharedMaterials;
                }
                Part(bundle,"ToolHandle",new Vector3(.55f,.07f,0),new Vector3(.06f,.065f,1.1f),new Vector3(0,-25,0),timber);
                Part(bundle,"ToolHead",new Vector3(.36f,.09f,.43f),new Vector3(.43f,.09f,.08f),new Vector3(0,-25,0),metal);
                var mix=AssetDatabase.FindAssets("t:WorldMacroAudioMixProfileSO").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WorldMacroAudioMixProfileSO>).FirstOrDefault(m=>m.Sfx!=null);
                if(mix==null)throw new Exception("SFX mixer missing");
                var notes=new List<string>();
                void Sound(string clip,Vector3 p,float gain,float radius)
                {
                    string path=folder+"/"+clip+".wav";
                    File.Copy("../Art/Audio/Cave235/"+clip+".wav",path,true);AssetDatabase.ImportAsset(path);
                    var importer=(AudioImporter)AssetImporter.GetAtPath(path);importer.forceToMono=true;
                    var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.CompressedInMemory;settings.compressionFormat=AudioCompressionFormat.Vorbis;settings.quality=.7f;importer.defaultSampleSettings=settings;importer.SaveAndReimport();
                    var g=Group(clip,p,0).gameObject;var a=g.AddComponent<AudioSource>();a.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);a.playOnAwake=false;a.volume=0;a.outputAudioMixerGroup=mix.Sfx;
                    g.AddComponent<AudioLowPassFilter>().cutoffFrequency=2600;
                    var ambience=g.AddComponent<CompactCaveAmbience>();ambience.Session=session;ambience.Map=ui.MapData;ambience.Level=gain;ambience.Radius=radius;
                    notes.Add(clip+" "+p+" level="+gain+" radius="+radius);
                }
                Sound("deep_water",new Vector3(3540,137.1f,1788),.20f,24);
                Sound("exit_air",new Vector3(3444,137.4f,1849),.26f,32);
                Sound("side_gallery_rub",new Vector3(3536,136.6f,1839),.13f,23);
                foreach(var p in new[]{cart,beams,bundle})notes.Add("PROP "+p.name+" "+p.position);
                ui.MapData.Revision="compact-cave-v4-exploration235";
                EditorUtility.SetDirty(ui.MapData);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
                File.WriteAllText(Output+"/cave_exploration235.txt",string.Join("\n",notes));
                return "PASS cave discovery opted in; 3 spatial filtered sources; 3 authored prop groups; candidate saved\n"+string.Join("\n",notes);
            }
            finally {EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
        }
    }
}
