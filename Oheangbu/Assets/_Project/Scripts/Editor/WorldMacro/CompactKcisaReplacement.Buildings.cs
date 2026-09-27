using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactKcisaReplacement
    {
        [Serializable] sealed class Change { public string original,replacement,source,note; public Vector3 position; }
        [Serializable] sealed class Changes { public List<Change> items=new List<Change>(); }
        static Changes changes;
        static bool Primitive(Transform t)
        { var f=t.GetComponent<MeshFilter>();if(f==null||f.sharedMesh==null)return false;string p=AssetDatabase.GetAssetPath(f.sharedMesh);return p.Contains("unity_builtin")||p.Contains("unity default"); }
        static Transform[] Primitives()=>All.Where(t=>Primitive(t)&&t.gameObject.activeInHierarchy&&t.GetComponent<Renderer>()!=null&&t.GetComponent<Renderer>().enabled).ToArray();
        static void BeginChanges()
        { Directory.CreateDirectory(Folder);string p=Output+"/replacements.json";changes=File.Exists(p)?JsonUtility.FromJson<Changes>(File.ReadAllText(p)):new Changes(); }
        static void Record(Transform old,Transform replacement,string source,string note)
        { changes.items.Add(new Change{original=PathOf(old),replacement=PathOf(replacement),source=source,note=note,position=old.position}); }
        // Keep gameplay objects and colliders. Clearing only the filter prevents preview culling from
        // accidentally re-enabling the old capsule/cube renderer later.
        static void Retire(Transform t,bool replaceCollider=false)
        {
            var f=t.GetComponent<MeshFilter>();if(f!=null){f.sharedMesh=null;EditorUtility.SetDirty(f);}
            var r=t.GetComponent<Renderer>();if(r!=null){r.enabled=false;EditorUtility.SetDirty(r);}
            if(replaceCollider)foreach(var c in t.GetComponents<Collider>()){c.enabled=false;EditorUtility.SetDirty(c);}
        }
        static string FinishChanges(string family,int count)
        {
            File.WriteAllText(Output+"/replacements.json",JsonUtility.ToJson(changes,true));
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return family+": "+count+" replacements saved; source assets untouched";
        }
        static Transform Place(Transform parent,string id,string source,Vector3 bottom,Vector3 envelope,float yaw=0)
            =>WorldMacroVisualCorridorAuthoring.PlaceSource(parent,id,source,bottom,envelope,yaw,false);
        static void MeshCollision(Transform holder)
        {
            var group=holder.GetComponent<LODGroup>();var rs=group==null?holder.GetComponentsInChildren<Renderer>():group.GetLODs()[0].renderers;
            foreach(var r in rs){var f=r.GetComponent<MeshFilter>();if(f==null||f.sharedMesh==null)continue;var c=r.gameObject.AddComponent<MeshCollider>();c.sharedMesh=f.sharedMesh;}
        }
        static string Buildings()
        {
            BeginChanges();int count=0;
            // Reuse the already source-derived, merged three-LOD house. No 338-renderer source
            // prefab instances are kept in the scene, and native aspect ratios are preserved.
            var template=All.Select(t=>t.GetComponent<LODGroup>()).FirstOrDefault(g=>g!=null&&g.GetLODs().Length==3&&g.GetLODs()[0].renderers.Sum(r=>r==null?0:Triangles(r))==86400);
            if(template==null)throw new InvalidOperationException("Existing KCISA House_2 with three LODs required");
            foreach(var wall in Primitives().Where(t=>t.name=="Massing_Walls"))
            {
                Guard();var bounds=wall.GetComponent<Renderer>().bounds;var parent=wall.parent;
                var roofs=parent.Cast<Transform>().Where(t=>t.name=="Massing_Roof"&&Primitive(t)&&Vector2.Distance(new Vector2(t.position.x,t.position.z),new Vector2(wall.position.x,wall.position.z))<bounds.size.x*.8f).OrderBy(t=>(t.position-wall.position).sqrMagnitude).Take(2).ToArray();
                var house=Object.Instantiate(template.gameObject).transform;house.name="KCISA_House_"+count.ToString("D3");house.SetParent(parent,true);
                house.rotation=Quaternion.Euler(0,wall.eulerAngles.y,0);house.localScale=Vector3.one;
                foreach(var c in house.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
                var renderers=house.GetComponent<LODGroup>().GetLODs()[0].renderers;
                Bounds local=new Bounds();bool first=true;
                foreach(var r in renderers){var mesh=r.GetComponent<MeshFilter>().sharedMesh;foreach(var p in Corners(mesh.bounds)){var v=house.InverseTransformPoint(r.transform.TransformPoint(p));if(first){local=new Bounds(v,Vector3.zero);first=false;}else local.Encapsulate(v);}}
                float scale=Mathf.Min(Mathf.Abs(wall.lossyScale.x)/local.size.x,Mathf.Abs(wall.lossyScale.z)/local.size.z,6.8f/local.size.y);
                house.localScale=Vector3.one*scale;
                house.position=new Vector3(wall.position.x,bounds.min.y,wall.position.z)-house.rotation*(new Vector3(local.center.x,local.min.y,local.center.z)*scale);
                house.GetComponent<LODGroup>().RecalculateBounds();MeshCollision(house);
                Record(wall,house,"Assets/House_2/house2 .fbx","Existing source-derived 86,400/24k/8k LOD house; uniform scale; actual mesh collision replaces solid block");
                Retire(wall,true);foreach(var roof in roofs)Retire(roof,true);count++;
            }
            return FinishChanges("KCISA settlement houses",count);
        }
        static int Triangles(Renderer r){var f=r.GetComponent<MeshFilter>();if(f==null||f.sharedMesh==null)return 0;int n=0;for(int i=0;i<f.sharedMesh.subMeshCount;i++)n+=(int)f.sharedMesh.GetIndexCount(i)/3;return n;}
        static IEnumerable<Vector3> Corners(Bounds b){for(int i=0;i<8;i++)yield return b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));}
        static string Walls()
        {
            BeginChanges();int count=0;const string source="Assets/HwaseongForteressGate/Prefabs/SM_CW.prefab";
            foreach(var t in Primitives().Where(t=>t.name=="Massing_CityWall"))
            {
                Guard();Vector3 size=t.lossyScale;var holder=new GameObject("KCISA_Rampart").transform;holder.SetParent(t.parent,false);holder.SetPositionAndRotation(t.position,t.rotation);
                // Native wall runs on X; the blockout wall runs on Z. Short sections preserve
                // texture scale and merlons instead of stretching one facade across the city.
                int pieces=Mathf.Max(1,Mathf.CeilToInt(size.z/7.8f));float step=size.z/pieces;
                for(int i=0;i<pieces;i++)
                {
                    var p=t.position+t.forward*(-size.z*.5f+step*(i+.5f))-Vector3.up*size.y*.5f;
                    var part=Place(holder,"Stonework_"+i,source,p,new Vector3(step*1.03f,0,0),t.eulerAngles.y+90);
                    var r=part.GetComponentsInChildren<Renderer>();Bounds b=r[0].bounds;foreach(var rr in r)b.Encapsulate(rr.bounds);
                    float sy=size.y/b.size.y,sz=size.x/(b.size.x*Mathf.Abs(t.right.x)+b.size.z*Mathf.Abs(t.right.z));
                    // Match the original wall top so there is no invisible ten-metre collider
                    // above a five-metre display. Only derived instance transforms change.
                    foreach(Transform child in part){child.localPosition=Vector3.Scale(child.localPosition,new Vector3(1,sy,1));child.localScale=Vector3.Scale(child.localScale,new Vector3(1,sy,1));}
                    part.GetComponent<LODGroup>().RecalculateBounds();
                }
                Retire(t);Record(t,holder,source,"Repeated original Hwaseong rampart modules; retained original blocking collider");count++;
            }
            return FinishChanges("KCISA city ramparts",count);
        }
    }
}
