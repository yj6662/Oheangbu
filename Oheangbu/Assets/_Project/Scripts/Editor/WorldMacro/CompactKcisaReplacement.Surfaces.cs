using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactKcisaReplacement
    {
        const string StonePanel="Assets/HwaseongForteressGate/Prefabs/SM_CW.prefab";
        const string StoneCap="Assets/JejumokGwana/Prefabs/Floors/Floor_stone_2.prefab";
        const string TimberFloor="Assets/HwaseongForteressGate/Prefabs/SM_Floor_001.prefab";
        sealed class SourceGeometry {public Bounds bounds;public List<(Mesh mesh,int slot,Material mat,Matrix4x4 matrix)> parts=new List<(Mesh,int,Material,Matrix4x4)>();}
        static readonly Dictionary<string,SourceGeometry> geometry=new Dictionary<string,SourceGeometry>();
        static SourceGeometry Source(string path)
        {
            if(geometry.TryGetValue(path,out var found))return found;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(source==null)throw new FileNotFoundException(path);
            var temp=Object.Instantiate(source);temp.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);temp.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                var data=new SourceGeometry();bool first=true;
                foreach(var f in temp.GetComponentsInChildren<MeshFilter>(true))
                {
                    var r=f.GetComponent<Renderer>();if(f.sharedMesh==null||r==null)continue;
                    foreach(var c in Corners(f.sharedMesh.bounds)){var v=f.transform.TransformPoint(c);if(first){data.bounds=new Bounds(v,Vector3.zero);first=false;}else data.bounds.Encapsulate(v);}
                    for(int i=0;i<f.sharedMesh.subMeshCount;i++)if(i<r.sharedMaterials.Length&&r.sharedMaterials[i]!=null)data.parts.Add((f.sharedMesh,i,r.sharedMaterials[i],f.transform.localToWorldMatrix));
                }
                if(first)throw new InvalidOperationException("No geometry "+path);geometry[path]=data;return data;
            }
            finally{Object.DestroyImmediate(temp);}
        }
        static void AddTiles(Dictionary<Material,List<CombineInstance>> batches,string source,Vector3 centre,Vector3 size,Quaternion rotation,float maxX,float maxZ)
        {
            var s=Source(source);int nx=Mathf.Max(1,Mathf.CeilToInt(size.x/maxX)),nz=Mathf.Max(1,Mathf.CeilToInt(size.z/maxZ));
            var tile=new Vector3(size.x/nx,size.y,size.z/nz);
            for(int x=0;x<nx;x++)for(int z=0;z<nz;z++)
            {
                var offset=new Vector3(-size.x/2+tile.x*(x+.5f),0,-size.z/2+tile.z*(z+.5f));
                var scale=new Vector3(tile.x/s.bounds.size.x,tile.y/s.bounds.size.y,tile.z/s.bounds.size.z);
                var matrix=Matrix4x4.TRS(centre+rotation*offset,rotation,scale)*Matrix4x4.Translate(-s.bounds.center);
                foreach(var part in s.parts){if(!batches.TryGetValue(part.mat,out var list)){list=new List<CombineInstance>();batches[part.mat]=list;}list.Add(new CombineInstance{mesh=part.mesh,subMeshIndex=part.slot,transform=matrix*part.matrix});}
            }
        }
        static Transform Clad(Transform old,bool wood)
        {
            Vector3 size=new Vector3(Mathf.Abs(old.lossyScale.x),Mathf.Abs(old.lossyScale.y),Mathf.Abs(old.lossyScale.z));
            var batches=new Dictionary<Material,List<CombineInstance>>();
            if(wood)AddTiles(batches,TimberFloor,Vector3.zero,size,Quaternion.identity,3,3);
            else
            {
                float cap=Mathf.Min(.15f,size.y*.4f),wall=Mathf.Min(.16f,Mathf.Min(size.x,size.z)*.3f);
                AddTiles(batches,StoneCap,new Vector3(0,(size.y-cap)/2,0),new Vector3(size.x,cap,size.z),Quaternion.identity,3,3);
                if(size.y>.22f)
                {
                    foreach(float sign in new[]{-1f,1f})
                    {
                        AddTiles(batches,StonePanel,new Vector3(0,0,sign*(size.z-wall)/2),new Vector3(size.x,size.y,wall),Quaternion.identity,6,3);
                        AddTiles(batches,StonePanel,new Vector3(sign*(size.x-wall)/2,0,0),new Vector3(size.z,size.y,wall),Quaternion.Euler(0,90,0),6,3);
                    }
                }
            }
            var holder=new GameObject(wood?"KCISA_TimberAssembly":"KCISA_MasonryAssembly").transform;holder.SetPositionAndRotation(old.position,old.rotation);holder.SetParent(old,true);
            Directory.CreateDirectory(Folder+"/Meshes");int index=0;
            foreach(var batch in batches)
            {
                var mesh=new Mesh{name=holder.name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(batch.Value.ToArray(),true,true);mesh.RecalculateBounds();
                string key=Hash128.Compute(PathOf(old)+old.GetSiblingIndex()+old.position.ToString("R")+index).ToString();string path=Folder+"/Meshes/"+key+".asset";
                if(AssetDatabase.LoadAssetAtPath<Mesh>(path)!=null)Object.DestroyImmediate(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("SourceSurface_"+index++);go.transform.SetParent(holder,false);go.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=WorldMacroVisualCorridorAuthoring.Surface(batch.Key);go.isStatic=true;
            }
            var group=holder.gameObject.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(.004f,holder.GetComponentsInChildren<Renderer>())});group.RecalculateBounds();Retire(old);
            Record(old,holder,wood?TimberFloor:StonePanel+"; "+StoneCap,"Source mesh modules fitted and merged per existing support volume; original support collider, top level and gameplay root retained");return holder;
        }
        static string Surfaces()
        {
            BeginChanges();int count=0;
            string[] names={"Massing_Foundation","Bridge_Pier","Bridge_Deck_TEST","Corridor_Wall","Left_Wall","Right_Wall","Door_Jamb","End_Wall","Room_Floor","Corridor","Entry_Ramp","Stone_Terrace","Watchtower_Stone","Flank_Pier","Pavilion_Deck","Timber_Cap","Evidence_Platform","Evidence_Altar"};
            foreach(var old in Primitives().Where(t=>names.Contains(t.name)||t.name.StartsWith("Access_Step_")||t.name=="Roof"&&PathOf(t).StartsWith("WorldMacro_Content_Layout")))
            {
                Guard();bool wood=old.name=="Timber_Cap"||old.name=="Bridge_Deck_TEST"||old.name=="Roof"||old.name=="Pavilion_Deck";
                Clad(old,wood);count++;
            }
            return FinishChanges("KCISA source masonry / timber surfaces",count);
        }
        static string RepairStoneCaps()
        {
            int count=0;var source=Source(StoneCap);var material=WorldMacroVisualCorridorAuthoring.Surface(source.parts[0].mat);
            AssetDatabase.StartAssetEditing();
            try
            {
            foreach(var holder in All.Where(t=>t.name=="KCISA_MasonryAssembly").ToArray())
            {
                Guard();
                var old=holder.parent;var size=old.lossyScale;float cap=Mathf.Min(.15f,size.y*.4f);
                var batches=new Dictionary<Material,List<CombineInstance>>();AddTiles(batches,StoneCap,new Vector3(0,(size.y-cap)/2,0),new Vector3(size.x,cap,size.z),Quaternion.identity,3,3);
                var filter=holder.Find("SourceSurface_0").GetComponent<MeshFilter>();var replacement=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,name=filter.sharedMesh.name};
                replacement.CombineMeshes(batches.Values.SelectMany(v=>v).ToArray(),true,true);replacement.RecalculateBounds();
                string pavedPath=AssetDatabase.GetAssetPath(filter.sharedMesh);
                if(!pavedPath.EndsWith("_paving.asset"))pavedPath=pavedPath.Replace(".asset","_paving.asset");
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(pavedPath);
                if(existing==null){AssetDatabase.CreateAsset(replacement,pavedPath);existing=replacement;}
                else Object.DestroyImmediate(replacement);
                filter.sharedMesh=existing;EditorUtility.SetDirty(filter);
                filter.GetComponent<Renderer>().sharedMaterial=material;EditorUtility.SetDirty(filter.GetComponent<Renderer>());holder.GetComponent<LODGroup>().RecalculateBounds();count++;
            }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            var ground=All.Select(t=>t.GetComponent<Renderer>()).Where(r=>r!=null).SelectMany(r=>r.sharedMaterials).First(m=>m!=null&&m.shader.name=="Oheangbu/CompactNaturalGround");
            string path=Folder+"/PagodaGranite.mat";var stone=AssetDatabase.LoadAssetAtPath<Material>(path);if(stone==null){stone=new Material(ground);AssetDatabase.CreateAsset(stone,path);}
            stone.SetFloat("_GroundPath",0);stone.SetFloat("_GroundKind",0);stone.SetFloat("_RealmTintStrength",0);stone.SetFloat("_BumpScale",.38f);stone.SetFloat("_Brightness",1.3f);stone.SetFloat("_Saturation",.25f);stone.SetVector("_Tiling",new Vector4(.65f,.65f,.65f,0));
            foreach(string p in new[]{"_GrassMap","_DirtMap"})stone.SetTexture(p,ground.GetTexture("_RockMap"));foreach(string p in new[]{"_GrassNormal","_DirtNormal"})stone.SetTexture(p,ground.GetTexture("_RockNormal"));EditorUtility.SetDirty(stone);
            foreach(var t in All.Where(t=>t.name=="Authored_GranitePagoda"))foreach(var r in t.GetComponentsInChildren<Renderer>()){r.sharedMaterials=Enumerable.Repeat(stone,r.sharedMaterials.Length).ToArray();EditorUtility.SetDirty(r);}
            AssetDatabase.SaveAssets();UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return count+" cap meshes use original 3x3m Jejumok stone paving; new pagoda uses non-atlas triplanar granite";
        }
    }
}
