using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class CodexThatchedInn
    {
        public const string RootName="Thatched_Inn";
        public const string PrefabPath=CodexInnAssets.Folder+"/ThatchedInn.prefab";
        [MenuItem("Oheangbu/Dev/Codex World/Replace inn with thatched asset")]
        private static void ApplyMenu()=>Debug.Log(Apply());
        public static string Apply()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!=CodexWorldSceneBuilder.ScenePath || scene.isDirty) return "FAIL: save C2 in Edit mode";
            var data=AssetDatabase.LoadAssetAtPath<CodexWorldSettingsSO>(CodexWorldSceneBuilder.SettingsPath);
            if(data==null || data.InnEntryStepPositions.Length!=data.InnEntryStepSizes.Length) return "FAIL: inn settings/steps invalid";
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(data.InnModelPath);
            var shader=Shader.Find("Oheangbu/DesaturatedAssetLit");
            if(source==null || shader==null || ShaderUtil.ShaderHasError(shader)) return "FAIL: source/shader unavailable";
            var holder=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="04_Mountain_Inn");
            var old=holder.Cast<Transform>().Select(t=>t.gameObject).ToArray();
            DevSceneKit.EnsureFolder(CodexInnAssets.Folder+"/Meshes");DevSceneKit.EnsureFolder(CodexInnAssets.Folder+"/Materials");
            var temp=(GameObject)PrefabUtility.InstantiatePrefab(source);temp.hideFlags=HideFlags.HideAndDontSave;
            var candidate=new GameObject(RootName);
            bool installed=false;
            try
            {
                var b=CodexInnAssets.BoundsOf(temp);var pivot=new Vector3(b.center.x,b.min.y,b.center.z);
                var batches=new Dictionary<Material,List<CombineInstance>>();
                foreach(var r in temp.GetComponentsInChildren<MeshRenderer>())
                {
                    var mesh=r.GetComponent<MeshFilter>().sharedMesh;
                    if(r.sharedMaterials.Length!=mesh.subMeshCount || r.sharedMaterials.Any(m=>m==null)) throw new InvalidOperationException("Invalid source slots: "+r.name);
                    for(int s=0;s<mesh.subMeshCount;s++)
                    {
                        var mat=r.sharedMaterials[s];if(!batches.ContainsKey(mat))batches[mat]=new List<CombineInstance>();
                        batches[mat].Add(new CombineInstance{mesh=mesh,subMeshIndex=s,transform=Matrix4x4.Translate(-pivot)*r.localToWorldMatrix});
                    }
                }
                foreach(var pair in batches)
                {
                    string name=pair.Key.name;string folder=CodexInnAssets.Folder;
                    string path=folder+"/Materials/"+name+"_Muted.mat";
                    var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(mat==null){mat=new Material(pair.Key);AssetDatabase.CreateAsset(mat,path);}
                    else mat.CopyPropertiesFromMaterial(pair.Key);
                    mat.shader=shader;mat.SetFloat("_AssetSaturation",data.InnSaturation);mat.SetFloat("_AssetValue",data.InnAlbedoValue);
                    mat.SetFloat("_AssetAmbient",data.InnDiffuseSkyFill);
                    mat.SetFloat("_Smoothness",data.InnSmoothness);mat.SetFloat("_Metallic",0);mat.SetColor("_EmissionColor",Color.black);mat.DisableKeyword("_EMISSION");
                    mat.SetFloat("_EnvironmentReflections",0);mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    if(mat.GetTexture("_BumpMap")!=null)mat.EnableKeyword("_NORMALMAP");
                    EditorUtility.SetDirty(mat);
                    var combined=new Mesh{name=name,indexFormat=IndexFormat.UInt32};combined.CombineMeshes(pair.Value.ToArray(),true,true);combined.RecalculateBounds();
                    string meshPath=folder+"/Meshes/"+name+".asset";
                    var saved=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if(saved==null){saved=combined;AssetDatabase.CreateAsset(saved,meshPath);}
                    else
                    {
                        saved.Clear();saved.indexFormat=combined.indexFormat;saved.vertices=combined.vertices;saved.normals=combined.normals;
                        saved.tangents=combined.tangents;saved.colors=combined.colors;
                        for(int channel=0;channel<8;channel++){var uv=new List<Vector4>();combined.GetUVs(channel,uv);saved.SetUVs(channel,uv);}
                        saved.triangles=combined.triangles;saved.bounds=combined.bounds;saved.UploadMeshData(false);
                        Object.DestroyImmediate(combined);EditorUtility.SetDirty(saved);
                    }
                    var go=new GameObject(name);go.transform.SetParent(candidate.transform,false);go.isStatic=true;
                    go.AddComponent<MeshFilter>().sharedMesh=saved;go.AddComponent<MeshRenderer>().sharedMaterial=mat;
                    if(!name.Contains("Roof")) go.AddComponent<MeshCollider>().sharedMesh=saved;
                }
                // Visible stone treads connect the raised source floor without changing the player's step height.
                for(int i=0;i<data.InnEntryStepPositions.Length;i++)
                {
                    var tread=GameObject.CreatePrimitive(PrimitiveType.Cube);tread.name="Inn_Entry_Step_"+i;tread.transform.SetParent(candidate.transform,false);
                    tread.transform.localPosition=data.InnEntryStepPositions[i];tread.transform.localScale=data.InnEntryStepSizes[i];
                    CodexInnSteps.Configure(tread,i,data);
                }
                var driver=Object.FindFirstObjectByType<WorldLookDriver>();driver.Apply();
                var lampSource=AssetDatabase.LoadAssetAtPath<Material>(CodexWorldSceneBuilder.AssetFolder+"/Materials/Lantern.mat");
                string lampPath=CodexInnAssets.Folder+"/Materials/InnLantern.mat";
                var lamp=AssetDatabase.LoadAssetAtPath<Material>(lampPath);
                if(lamp==null){lamp=new Material(lampSource);AssetDatabase.CreateAsset(lamp,lampPath);}
                lamp.SetFloat("_Intensity",data.InnLanternSourceIntensity);lamp.SetFloat("_PaperMix",data.InnLanternPaperMix);EditorUtility.SetDirty(lamp);
                var wood=AssetDatabase.LoadAssetAtPath<Material>(CodexWorldSceneBuilder.AssetFolder+"/Materials/Timber.mat");
                foreach(var p in data.InnLanternPositions)
                {
                    var lantern=new GameObject("Warm_Porch_Lantern").transform;lantern.SetParent(candidate.transform,false);lantern.localPosition=p;
                    Cube(lantern,"Paper_Diffuser",Vector3.zero,new Vector3(.44f,.65f,.44f),lamp);
                    foreach(float y in new[]{-.35f,.35f})Cube(lantern,"Timber_Cap",new Vector3(0,y,0),new Vector3(.5f,.07f,.5f),wood);
                    foreach(float x in new[]{-.23f,.23f})foreach(float z in new[]{-.23f,.23f}) Cube(lantern,"Frame",new Vector3(x,0,z),new Vector3(.035f,.66f,.035f),wood);
                    Cube(lantern,"Suspension",new Vector3(0,.56f,0),new Vector3(.025f,.4f,.025f),wood);
                    var light=lantern.gameObject.AddComponent<Light>();light.type=LightType.Point;
                    light.color=Color.Lerp(driver.Palette.WoodColor,driver.Palette.PaperColor,.25f);
                    light.intensity=data.InnLanternIntensity;light.range=data.InnLanternRange;light.shadows=LightShadows.Soft;
                    light.shadowBias=.025f;light.shadowNormalBias=.1f;
                }
                candidate.transform.localScale=new Vector3(data.InnModelScale,data.InnModelVerticalScale,data.InnModelScale);
                PrefabUtility.SaveAsPrefabAsset(candidate,PrefabPath);
                candidate.transform.SetParent(holder,false);candidate.transform.localRotation=Quaternion.Euler(0,data.InnYaw+data.InnModelYawOffset,0);
                candidate.transform.localScale=new Vector3(data.InnModelScale,data.InnModelVerticalScale,data.InnModelScale);
                var anchor=data.InnAnchor;anchor.y=CodexWorldGeometry.Height(anchor.x,anchor.z)-data.InnFoundationEmbed;candidate.transform.position=anchor;
                foreach(var go in old)Object.DestroyImmediate(go);
                installed=true;EditorUtility.SetDirty(data);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                return Validate();
            }
            finally{Object.DestroyImmediate(temp);if(!installed)Object.DestroyImmediate(candidate);}
        }
        private static void Cube(Transform parent,string name,Vector3 p,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;
            if(name=="Paper_Diffuser")go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
        }
        public static string Validate()
        {
            var ts=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
            var root=ts.Single(t=>t.name==RootName);var rs=root.GetComponentsInChildren<MeshRenderer>();
            var lights=root.GetComponentsInChildren<Light>();
            int bad=rs.Count(r=>r.sharedMaterial==null || r.sharedMaterial.shader==null || ShaderUtil.ShaderHasError(r.sharedMaterial.shader));
            int old=ts.Count(t=>t.name=="JoseonInn");long triangles=rs.Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);
            int muted=rs.Where(r=>r.sharedMaterial!=null && r.sharedMaterial.HasProperty("_AssetSaturation") && r.sharedMaterial.GetFloat("_AssetSaturation")<.1f).Select(r=>r.sharedMaterial).Distinct().Count();
            string result=$"{(old==0 && bad==0 && muted==11 && lights.Length==2?"PASS":"FAIL")}: oldInn={old}; renderers={rs.Length}; mutedMaterialBatches={muted}; invalid={bad}; tris={triangles}; colliders={root.GetComponentsInChildren<Collider>().Length}; lights={lights.Length}; bounds={CodexInnAssets.BoundsOf(root.gameObject)}";
            File.WriteAllText(CodexWorldAudit.CaptureFolder+"/thatched-inn-validation.txt",result);return result;
        }
    }
}
