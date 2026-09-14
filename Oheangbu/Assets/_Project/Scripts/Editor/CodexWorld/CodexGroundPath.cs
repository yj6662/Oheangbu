using System;
using System.IO;
using System.Linq;
using Oheangbu.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>C2 path authoring changes pigment only; ground geometry and physics stay shared.</summary>
    public static class CodexGroundPath
    {
        public const string MaskPath=CodexWorldSceneBuilder.AssetFolder+"/GroundPathMask.asset";
        public const string MaterialPath=CodexWorldSceneBuilder.AssetFolder+"/Materials/Ground_With_Path.mat";

        private static MeshRenderer Ground()=>SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r=>r.GetComponentsInChildren<MeshRenderer>(true))
            .Single(r=>r.name=="Valley" && AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>()?.sharedMesh)==CodexWorldSceneBuilder.MeshFolder+"/Ground.asset");

        [MenuItem("Oheangbu/Dev/Codex World/Blend path into ground")]
        private static void ApplyMenu()=>Debug.Log(Apply());

        public static string Apply()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!=CodexWorldSceneBuilder.ScenePath || scene.isDirty)
                return "FAIL: open and save C2 in Edit mode";
            var data=AssetDatabase.LoadAssetAtPath<CodexWorldSettingsSO>(CodexWorldSceneBuilder.SettingsPath);
            var ground=Ground();
            var oldPaths=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true))
                .Where(t=>t.name=="Winding_Path").ToArray();
            if(oldPaths.Any(t=>t.GetComponent<Collider>()!=null)) return "FAIL: unexpected path collider";
            if(data==null || data.GroundSize.x<=0 || data.GroundSize.y<=0) return "FAIL: invalid ground settings";
            int width=Mathf.Clamp(data.PathMaskResolution.x,128,4096),height=Mathf.Clamp(data.PathMaskResolution.y,128,4096);
            var bytes=new byte[width*height];
            int blended=0;
            for(int y=0;y<height;y++)
            {
                float z=(y+.5f)/height*data.GroundSize.y;
                float centre=CodexWorldGeometry.PathX(z);
                float halfWidth=data.PathWidth*.5f*(.95f+.18f*Mathf.Sin(z*.72f)+.035f*Mathf.Sin(z*4.1f));
                for(int x=0;x<width;x++)
                {
                    float wx=((x+.5f)/width-.5f)*data.GroundSize.x;
                    float irregular=(Mathf.PerlinNoise(wx*.8f+17.3f,z*.8f+9.1f)-.5f)*2*data.PathEdgeVariation;
                    float d=Mathf.Abs(wx-centre)-halfWidth+irregular;
                    float coverage=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((d+data.PathEdgeBlend*.5f)/Mathf.Max(.05f,data.PathEdgeBlend)));
                    byte value=(byte)Mathf.RoundToInt(coverage*255);bytes[y*width+x]=value;
                    if(value>0 && value<255) blended++;
                }
            }
            var mask=AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
            if(mask==null)
            {
                mask=new Texture2D(width,height,TextureFormat.R8,true,true) {name="GroundPathMask"};
                AssetDatabase.CreateAsset(mask,MaskPath);
            }
            else mask.Reinitialize(width,height,TextureFormat.R8,true);
            mask.wrapMode=TextureWrapMode.Clamp;mask.filterMode=FilterMode.Trilinear;mask.anisoLevel=4;
            mask.SetPixelData(bytes,0);mask.Apply(true,false);EditorUtility.SetDirty(mask);
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(material==null)
            {
                material=new Material(ground.sharedMaterial) {name="Ground_With_Path"};
                AssetDatabase.CreateAsset(material,MaterialPath);
            }
            else if(ground.sharedMaterial!=material) material.CopyPropertiesFromMaterial(ground.sharedMaterial);
            material.SetFloat("_GroundPath",1);material.SetFloat("_PathEdge",0);
            material.SetTexture("_GroundPathMask",mask);
            material.SetVector("_GroundPathRect",new Vector4(-data.GroundSize.x*.5f,0,1/data.GroundSize.x,1/data.GroundSize.y));
            material.SetVector("_GroundPathTones",new Vector4(data.PathSurfaceTones.x,data.PathSurfaceTones.y,0,0));
            material.SetFloat("_GroundPathVariation",data.PathPigmentVariation);
            if(ShaderUtil.ShaderHasError(material.shader)) return "FAIL: shader errors; path not removed";
            ground.sharedMaterial=material;
            foreach(var t in oldPaths) Object.DestroyImmediate(t.gameObject);
            EditorUtility.SetDirty(material);EditorUtility.SetDirty(data);AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            return Validate()+$"; removedRibbons={oldPaths.Length}; transitionTexels={blended}";
        }

        public static string Validate()
        {
            if(SceneManager.GetActiveScene().path!=CodexWorldSceneBuilder.ScenePath) return "FAIL: open C2";
            var ground=Ground();var mat=ground.sharedMaterial;
            var mesh=ground.GetComponent<MeshFilter>().sharedMesh;
            bool sharedCollider=ground.GetComponent<MeshCollider>()?.sharedMesh==mesh;
            int ribbons=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Count(t=>t.name=="Winding_Path");
            var mask=mat.GetTexture("_GroundPathMask") as Texture2D;
            int bad=0;
            if(mask!=null)
            for(int z=1;z<220;z++)
            {
                var rect=mat.GetVector("_GroundPathRect");
                float cx=(CodexWorldGeometry.PathX(z)-rect.x)*rect.z,v=(z-rect.y)*rect.w;
                if(mask.GetPixelBilinear(cx,v).r<.95f || mask.GetPixelBilinear(cx+10*rect.z,v).r>.01f) bad++;
            }
            bool pass=ribbons==0 && sharedCollider && mask!=null && bad==0 && mat.GetFloat("_GroundPath")==1 && mat.GetFloat("_PathEdge")==0 && !ShaderUtil.ShaderHasError(mat.shader);
            string result=$"{(pass?"PASS":"FAIL")}: ribbons={ribbons}; sharedGroundCollider={sharedCollider}; mask={mask?.width}x{mask?.height}; invalidRouteSamples={bad}; groundTriangles={mesh.GetIndexCount(0)/3}";
            Directory.CreateDirectory(CodexWorldAudit.CaptureFolder);File.WriteAllText(CodexWorldAudit.CaptureFolder+"/ground-path-validation.txt",result);
            return result;
        }
    }
}
