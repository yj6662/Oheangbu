using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPalanquinShortcutReview
    {
        const string Folder="Assets/_Project/Art/World/WorldMacro/Playtest/VehicleCall";
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/VehicleCall"));
        public static string Execute(string command)
        {
            Directory.CreateDirectory(Output);
            if(command=="apply")return Apply();
            if(command=="begin"){visualOnly=false;return Begin();}
            if(command=="visual-begin"){visualOnly=true;return Begin();}
            if(command=="poll")return status;
            if(command=="abort"){Finish("ABORTED");return status;}
            throw new ArgumentException(command);
        }
        static string Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit only");
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            var caller=session.GetComponent<WorldMacroPalanquinSummon>();
            var gesture=Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            if(caller==null||gesture==null)throw new InvalidOperationException("Existing caller and C02 rig required");
            Directory.CreateDirectory(Output+"/Before");
            if(!File.Exists(Output+"/Before/Playtest.unity"))File.Copy(session.gameObject.scene.path,Output+"/Before/Playtest.unity");
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var prototype=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/TemporaryOheangbu.prefab");
            if(prototype==null)
            {
                var root=new GameObject("TemporaryOheangbu");
                var gold=Material("Gold",new Color(.46f,.30f,.11f),.72f,.3f);
                var inset=Material("Socket",new Color(.09f,.065f,.03f),.55f,.25f);
                var cord=Material("Cord",new Color(.11f,.13f,.16f),0,.2f);
                Box(root.transform,"Plaque",new Vector3(0,0,0),new Vector3(.16f,.245f,.012f),gold);
                Box(root.transform,"RimL",new Vector3(-.078f,0,.009f),new Vector3(.006f,.245f,.006f),gold);
                Box(root.transform,"RimR",new Vector3(.078f,0,.009f),new Vector3(.006f,.245f,.006f),gold);
                Box(root.transform,"RimT",new Vector3(0,.120f,.009f),new Vector3(.16f,.006f,.006f),gold);
                Box(root.transform,"RimB",new Vector3(0,-.120f,.009f),new Vector3(.16f,.006f,.006f),gold);
                Vector2[] sockets={new Vector2(0,.065f),new Vector2(-.048f,0),Vector2.zero,new Vector2(.048f,0),new Vector2(0,-.065f)};
                for(int i=0;i<sockets.Length;i++)
                {
                    var p=new Vector3(sockets[i].x,sockets[i].y,.009f);
                    Ring(root.transform,"SocketRim"+i,p,.019f,.0025f,gold);
                    var disk=GameObject.CreatePrimitive(PrimitiveType.Cylinder);disk.name="Socket"+i;disk.transform.SetParent(root.transform,false);
                    disk.transform.localPosition=p;disk.transform.localRotation=Quaternion.Euler(90,0,0);disk.transform.localScale=new Vector3(.033f,.001f,.033f);
                    Object.DestroyImmediate(disk.GetComponent<Collider>());disk.GetComponent<Renderer>().sharedMaterial=inset;
                }
                Ring(root.transform,"Hanger",new Vector3(0,.139f,0),.016f,.004f,gold);
                Box(root.transform,"Cord",new Vector3(0,.157f,0),new Vector3(.007f,.032f,.008f),cord);
                Combine(root);
                prototype=PrefabUtility.SaveAsPrefabAsset(root,Folder+"/TemporaryOheangbu.prefab");Object.DestroyImmediate(root);
            }
            if(caller.TemporaryPendant==null)
            {
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(prototype,session.transform);
                caller.TemporaryPendant=instance.transform;instance.SetActive(false);
            }
            var plaqueGold=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Gold.mat");
            foreach(string materialName in new[]{"Gold","Socket","Cord"})
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/"+materialName+".mat");
                m.shader=Shader.Find("Oheangbu/WorldMacroTexturedSurface");
                m.SetFloat("_AmbientFloor",.45f);m.SetFloat("_Saturation",.8f);m.SetFloat("_LightResponse",.6f);m.SetFloat("_WashStrength",0);
                if(materialName=="Gold")m.SetColor("_BaseColor",new Color(.65f,.43f,.16f));
                EditorUtility.SetDirty(m);
            }
            caller.Session=session;caller.Gesture=gesture;caller.GestureSeconds=1.8f;caller.RecallDistance=30;
            if(session.Content.Opening!=null)
            {
                session.Content.Opening.BeforeVehicleObjective="G로 자동차를 부른 뒤, 금표 주막 쪽 큰길로 이동한다";
                session.Content.Opening.Commission.Text="동쪽 폐광에서 폭파 소리가 났소. 현장에 남은 흔적을 살펴보고 돌아와 주시오. 아직 누가, 왜 그런 것인지는 모르오.\n\nG를 누르면 오행부로 자동차를 부를 수 있소. 큰길을 따라 금표 주막 쪽으로 가면 폐광으로 이어지는 산길이 나오오. 좁은 길은 걸어서 살펴보시오.\n\n내린 뒤 멀어지면 차는 오행부로 돌아오니, 주차한 곳을 찾아 돌아올 필요는 없소. 큰길에서 G로 다시 부르면 되오.";
                EditorUtility.SetDirty(session.Content.Opening);
            }
            EditorUtility.SetDirty(caller);EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(session.gameObject.scene);
            int tris=prototype.GetComponentsInChildren<MeshFilter>(true).Sum(f=>f.sharedMesh.triangles.Length/3);
            string result="APPLIED G; gesture=1.8s; call at .936s; recall=30m after successful exit, 2s grace + .75s outside; temporary gold five-socket plaque="+tris+" tris; scene saved.";
            File.WriteAllText(Output+"/authoring.txt",result);return result;
        }
        static Material Material(string name,Color color,float metallic,float smoothness)
        {
            string path=Folder+"/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat!=null)return mat;
            mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};
            mat.SetColor("_BaseColor",color);mat.SetFloat("_Metallic",metallic);mat.SetFloat("_Smoothness",smoothness);
            AssetDatabase.CreateAsset(mat,path);return mat;
        }
        static void Box(Transform root,string name,Vector3 p,Vector3 s,Material mat)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root,false);
            go.transform.localPosition=p;go.transform.localScale=s;Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=mat;
        }
        static void Ring(Transform root,string name,Vector3 p,float radius,float tube,Material mat)
        {
            var v=new List<Vector3>();var t=new List<int>();const int n=24,k=6;
            for(int i=0;i<n;i++)for(int j=0;j<k;j++)
            {float a=i*Mathf.PI*2/n,b=j*Mathf.PI*2/k;float r=radius+tube*Mathf.Cos(b);v.Add(new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,Mathf.Sin(b)*tube));}
            for(int i=0;i<n;i++)for(int j=0;j<k;j++)
            {int a=i*k+j,b=((i+1)%n)*k+j,c=((i+1)%n)*k+(j+1)%k,d=i*k+(j+1)%k;t.AddRange(new[]{a,b,c,a,c,d});}
            var mesh=new Mesh{name=name};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=new GameObject(name);go.transform.SetParent(root,false);go.transform.localPosition=p;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;
        }
        static void Combine(GameObject root)
        {
            var parts=root.GetComponentsInChildren<MeshFilter>();
            foreach(var group in parts.GroupBy(p=>p.GetComponent<Renderer>().sharedMaterial))
            {
                var mesh=new Mesh{name="Pendant_"+group.Key.name};
                mesh.CombineMeshes(group.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=root.transform.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray());
                AssetDatabase.CreateAsset(mesh,Folder+"/"+mesh.name+".asset");
                var go=new GameObject(mesh.name);go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=group.Key;
            }
            foreach(var part in parts){var mesh=part.sharedMesh;Object.DestroyImmediate(part.gameObject);if(!AssetDatabase.Contains(mesh))Object.DestroyImmediate(mesh);}
        }
    }
}
