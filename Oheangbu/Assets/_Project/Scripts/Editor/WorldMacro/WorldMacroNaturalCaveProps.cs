using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oheangbu.App.World.Dressing;
using Object=UnityEngine.Object;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroNaturalCave
    {
        static string Props()
        {
            if(Root.Find("Authored_Mine_Details")!=null)throw new Exception("Details already installed");
            var parent=new GameObject("Authored_Mine_Details").transform;parent.SetParent(Root,false);
            var prototypes=Object.FindFirstObjectByType<WorldMacroDressingRenderer>().Sheet.Prototypes;
            var batches=new Dictionary<Material,List<CombineInstance>>();var ledger=new List<string>();
            Sheet.Prototype Proto(string id)=>prototypes.First(p=>p.Id=="Cheongrim_"+id);
            void Add(string id,Vector3 position,Quaternion rotation,Vector3 scale)
            {
                var p=Proto(id);foreach(var part in p.Lods[Mathf.Min(1,p.Lods.Length-1)].Parts)
                {if(!batches.TryGetValue(part.Material,out var list)){list=new List<CombineInstance>();batches.Add(part.Material,list);}list.Add(new CombineInstance{mesh=part.Mesh,subMeshIndex=part.Submesh,transform=Matrix4x4.TRS(position,rotation,scale)*part.Local});}
                ledger.Add(id+"\t"+position+"\t"+scale+"\t"+p.SourcePath);
            }
            void Beam(Vector3 a,Vector3 b,float width)
            {var p=Proto("SM_M_WoodLog");var rot=Quaternion.LookRotation(b-a);Add("SM_M_WoodLog",(a+b)*.5f-rot*Vector3.up*width*.5f,rot,new Vector3(width/p.Size.x,width/p.Size.y,Vector3.Distance(a,b)/p.Size.z));}
            // Source boulders break the structural surface near the floor. No obstacle sits on the centre route.
            var random=new System.Random(150926);var surface=Root.Find("Natural_Cave_Interior").GetComponent<MeshFilter>().sharedMesh;var vs=surface.vertices;var ns=surface.normals;var placed=new List<Vector3>();
            for(int k=0;k<vs.Length*2&&placed.Count<88;k++)
            {
                int i=random.Next(vs.Length);var v=vs[i];if(v.x>-43 || v.y<.05f||v.y>4.8f||Mathf.Abs(ns[i].y)>.8f||placed.Any(p=>(p-v).sqrMagnitude<28))continue;
                // Lower side-wall ledges, partially buried in the solid wall.
                float scale= .60f+(float)random.NextDouble()*.65f;
                Add("SM_Rock_K",new Vector3(v.x-ns[i].x*.9f,-.16f,v.z-ns[i].z*.9f),Quaternion.Euler(0,(float)random.NextDouble()*360,0),new Vector3(scale*1.25f,scale*1.8f,scale));placed.Add(v);
            }
            // Two partial mining galleries sit at working faces, not across the full cave ceiling.
            foreach(var origin in new[]{new Vector3(-141,0,-10),new Vector3(-77,0,14)})
            {
                Beam(origin,origin+Vector3.up*3.7f,.32f);Beam(origin+Vector3.right*4,origin+new Vector3(4,3.7f,0),.29f);Beam(origin+new Vector3(-.2f,3.7f,0),origin+new Vector3(4.3f,3.8f,0),.37f);
                Beam(origin+Vector3.up*2.7f,origin+new Vector3(1,3.7f,0),.21f);
                Add("SM_M_WoodenBox",origin+new Vector3(1,.02f,1.2f),Quaternion.Euler(0,20,0),Vector3.one*.85f);
                Add("SM_052_Pot",origin+new Vector3(2.1f,.02f,1.5f),Quaternion.identity,Vector3.one*.75f);
            }
            Beam(new Vector3(-132,.08f,-4),new Vector3(-128,.4f,-5.2f),.34f);Beam(new Vector3(-134,.10f,-4),new Vector3(-131,.3f,-6),.25f);
            for(int i=0;i<17;i++)Add("SM_Rock_L",new Vector3(-133+(float)random.NextDouble()*5,.01f,-3-(float)random.NextDouble()*4),Quaternion.Euler(0,i*73,0),Vector3.one*(.3f+(float)random.NextDouble()*.8f));
            // Only a few work lamps mark bends. They have a physical source and do not form a waypoint strip.
            var lampSource=GameObject.Find("Playtest_OwnedAssets").transform.Find("Mine_Work_Lantern");
            if(lampSource!=null)foreach(var position in new[]{new Vector3(-137,3,-9),new Vector3(-100,2.8f,3),new Vector3(-74,3,14),new Vector3(-47,2.8f,11)})
            {
                var lamp=Object.Instantiate(lampSource.gameObject,parent);lamp.name="Work_Lantern_"+position.x;lamp.SetActive(true);lamp.transform.localPosition=position;lamp.transform.localRotation=Quaternion.identity;
                foreach(var r in lamp.GetComponentsInChildren<Renderer>(true))r.enabled=true;
                foreach(var l in lamp.GetComponentsInChildren<Light>(true))Object.DestroyImmediate(l);
                var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.68f,.35f);light.intensity=8;light.range=21;light.shadows=LightShadows.None;
            }
            // Open-faced ore is local to the documented worksite; never placed as a navigation overlay.
            int n=0;foreach(var pair in batches){var mesh=new Mesh{name="Mine_Detail_"+n,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(pair.Value.ToArray(),true,true);mesh=Save(mesh,mesh.name);var go=new GameObject(mesh.name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=pair.Key.name.Contains("Rock")?Rock():pair.Key;go.isStatic=true;n++;}
            File.WriteAllLines(Output+"/details_ledger.tsv",new[]{"prototype\tlocal_position\tscale\tsource"}.Concat(ledger));
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Reused "+ledger.Count+" source rock/wood/prop placements in "+n+" material meshes; work lamps copied. Decorative ledges stay outside the central walking route.";
        }
    }
}
