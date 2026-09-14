using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

namespace Oheangbu.EditorTools
{
    public static class CodexInnSteps
    {
        public const string Folder=CodexWorldAudit.CaptureFolder+"/InnSteps";
        [Serializable] private class MeshData { public Vector3[] vertices; public Vector2[] uv; public int[] triangles; }
        public static void Configure(GameObject step,int index,CodexWorldSettingsSO settings)
        {
            string path=CodexInnAssets.Folder+"/Meshes/EntryStoneStep_"+index+".asset";
            var generated=Build(settings.InnEntryStepSizes[index],settings,index);
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved==null) {saved=generated;AssetDatabase.CreateAsset(saved,path);}
            else
            {
                saved.Clear();saved.vertices=generated.vertices;saved.normals=generated.normals;saved.uv=generated.uv;
                saved.triangles=generated.triangles;saved.tangents=generated.tangents;saved.bounds=generated.bounds;
                UnityEngine.Object.DestroyImmediate(generated);EditorUtility.SetDirty(saved);
            }
            step.GetComponent<MeshFilter>().sharedMesh=saved;
            step.GetComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(CodexInnAssets.Folder+"/Materials/h1_house_ground_Muted.mat");
        }
        private static Vector2 Atlas(Vector2[] q,float u,float v)=>Vector2.Lerp(Vector2.Lerp(q[0],q[1],u),Vector2.Lerp(q[2],q[3],u),v);
        private static Mesh Build(Vector3 size,CodexWorldSettingsSO s,int index)
        {
            var treadUV=index%2==0?s.InnStepTreadUV:s.InnStepAlternateTreadUV;
            var riserUV=index%2==0?s.InnStepRiserUV:s.InnStepAlternateRiserUV;
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            float bevel=Mathf.Min(s.InnStepBevel,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.2f);
            Vector3 half=size*.5f,inner=half-Vector3.one*bevel;
            // Separate face vertices retain atlas seams; analytic normals stay smooth across the rounded edges.
            Vector3[] outward={Vector3.up,Vector3.down,Vector3.forward,Vector3.back,Vector3.right,Vector3.left};
            Vector3[] across={Vector3.right,Vector3.right,Vector3.right,Vector3.left,Vector3.back,Vector3.forward};
            for(int face=0;face<6;face++)
            {
                Vector3 normal=outward[face],uAxis=across[face],vAxis=Vector3.Cross(normal,uAxis);
                float uSize=Vector3.Dot(size,Abs(uAxis)),vSize=Vector3.Dot(size,Abs(vAxis));
                var us=new[]{-.5f,-.5f+bevel/uSize,0f,.5f-bevel/uSize,.5f};
                int rows=face<2?4:Mathf.Max(1,Mathf.CeilToInt(size.y/s.InnStepRiserTextureHeight));
                float[] vs=face<2?new[]{-.5f,-.5f+bevel/vSize,0f,.5f-bevel/vSize,.5f}:Enumerable.Range(0,rows+1).Select(i=>-.5f+i/(float)rows).ToArray();
                for(int y=0;y<rows;y++)for(int x=0;x<us.Length-1;x++)
                {
                    int first=vertices.Count;
                    foreach(var corner in new[]{new Vector2(us[x],vs[y]),new Vector2(us[x+1],vs[y]),new Vector2(us[x+1],vs[y+1]),new Vector2(us[x],vs[y+1])})
                    {
                        Vector3 raw=Vector3.Scale(normal*.5f+uAxis*corner.x+vAxis*corner.y,size);
                        Vector3 nearest=new Vector3(Mathf.Clamp(raw.x,-inner.x,inner.x),Mathf.Clamp(raw.y,-inner.y,inner.y),Mathf.Clamp(raw.z,-inner.z,inner.z));
                        Vector3 n=(raw-nearest).normalized,p=nearest+n*bevel;
                        vertices.Add(new Vector3(p.x/size.x,p.y/size.y,p.z/size.z));normals.Add(Vector3.Scale(n,size).normalized);
                        float u=corner.x+.5f,v=corner.y+.5f;
                        if(index%4>=2)u=1-u;
                        if(face<2) uv.Add(Atlas(treadUV,u,1-v));
                        else
                        {
                            // Repeat only the donor stone riser strip, never the entire building atlas.
                            v=(corner.y-vs[y])/(vs[y+1]-vs[y]);
                            if(face>=4) u=Mathf.Lerp(.2f,.2f+size.z/size.x,u);
                            uv.Add(Atlas(riserUV,u,1-v));
                        }
                    }
                    triangles.AddRange(new[]{first,first+1,first+2,first,first+2,first+3});
                }
            }
            var mesh=new Mesh{name="WeatheredEntryStone"};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
        private static Vector3 Abs(Vector3 v)=>new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z));
        public static string Apply()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!=CodexWorldSceneBuilder.ScenePath || scene.isDirty) return "FAIL: save C2 in Edit mode";
            var settings=AssetDatabase.LoadAssetAtPath<CodexWorldSettingsSO>(CodexWorldSceneBuilder.SettingsPath);
            var root=GameObject.Find(CodexThatchedInn.RootName);
            var prefab=PrefabUtility.LoadPrefabContents(CodexThatchedInn.PrefabPath);
            try
            {
                for(int i=0;i<settings.InnEntryStepPositions.Length;i++)
                {
                    Configure(root.transform.Find("Inn_Entry_Step_"+i).gameObject,i,settings);
                    var target=prefab.transform.Find("Inn_Entry_Step_"+i).gameObject;
                    target.GetComponent<MeshFilter>().sharedMesh=root.transform.Find(target.name).GetComponent<MeshFilter>().sharedMesh;
                    target.GetComponent<MeshRenderer>().sharedMaterial=root.transform.Find(target.name).GetComponent<MeshRenderer>().sharedMaterial;
                }
                PrefabUtility.SaveAsPrefabAsset(prefab,CodexThatchedInn.PrefabPath);
                EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                return CodexThatchedInn.Validate();
            }
            finally {PrefabUtility.UnloadPrefabContents(prefab);}
        }
        public static string CaptureClose()
        {
            Directory.CreateDirectory(Folder);var inn=GameObject.Find(CodexThatchedInn.RootName).transform;
            var go=new GameObject("~InnStepCamera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.enabled=false;
            camera.fieldOfView=45;camera.nearClipPlane=.05f;camera.farClipPlane=100;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            go.transform.position=inn.TransformPoint(new Vector3(-4,2.7f,5.5f));go.transform.LookAt(inn.TransformPoint(new Vector3(-1.5f,.65f,1.25f)));
            var rt=new RenderTexture(1280,960,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var pixels=new Texture2D(1280,960,TextureFormat.RGB24,false);var old=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
            try {ShaderUtil.allowAsyncCompilation=false;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1280,960),0,0);pixels.Apply();File.WriteAllBytes(Folder+"/close.png",pixels.EncodeToPNG());return Folder+"/close.png";}
            finally {ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(go);rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(pixels);}
        }
        public static string Inspect()
        {
            Directory.CreateDirectory(Folder);
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(CodexInnAssets.Folder+"/Meshes/h1_house_ground.asset");
            File.WriteAllText(Folder+"/source-mesh.json",JsonUtility.ToJson(new MeshData{vertices=mesh.vertices,uv=mesh.uv,triangles=mesh.triangles}));
            var tex=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/House_1/Tex/h1_house_ground_B.png");
            var rt=RenderTexture.GetTemporary(1024,1024,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var old=RenderTexture.active;
            var preview=new Texture2D(1024,1024,TextureFormat.RGB24,false);
            try {Graphics.Blit(tex,rt);RenderTexture.active=rt;preview.ReadPixels(new Rect(0,0,1024,1024),0,0);preview.Apply();File.WriteAllBytes(Folder+"/atlas-preview.png",preview.EncodeToPNG());}
            finally {RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(preview);}
            return "Source mesh/atlas inspection exported: "+Folder;
        }
    }
}
