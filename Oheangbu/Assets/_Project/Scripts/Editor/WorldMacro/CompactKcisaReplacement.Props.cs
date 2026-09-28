using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactKcisaReplacement
    {
        static string Props()
        {
            BeginChanges();int count=0;
            foreach(var t in Primitives().Where(t=>t.name=="Inscribed_Surface"))
            {
                const string source="Assets/Korea_TreasureProps/Prefabs/SM_029_Wooden_Scroll.prefab";
                var b=t.GetComponent<Renderer>().bounds;
                var p=Place(t.parent,"KCISA_WrittenEvidence_"+t.GetSiblingIndex(),source,new Vector3(b.center.x,b.min.y,b.center.z),new Vector3(.65f,.25f,.5f),t.eulerAngles.y);
                Record(t,p,source,"Existing investigation trigger retained; physical wooden document replaces blank box");Retire(t);count++;
            }
            foreach(var t in Primitives().Where(t=>t.name=="Sanctuary_Stone"))
            {
                var b=t.GetComponent<Renderer>().bounds;
                var p=WorldMacroVisualCorridorAuthoring.PlaceDressingPrototype(t.parent,"KCISA_SanctuaryRock_"+t.GetSiblingIndex(),"Cheongrim_SM_Rock_K",new Vector3(b.center.x,b.min.y,b.center.z),new Vector3(3.5f,3.4f,3.5f),t.eulerAngles.y);
                Record(t,p,"Assets/SeyeonjeongPavilion/Prefabs/SM_Rock_K.prefab","Owned rock LODs; sanctuary collision retained");Retire(t);count++;
            }
            foreach(var t in Primitives().Where(t=>t.name=="SacredTree_Trunk_Massing"))
            {
                var b=t.GetComponent<Renderer>().bounds;
                var p=WorldMacroVisualCorridorAuthoring.PlaceDressingPrototype(t.parent,"KCISA_SacredElm","Cheongrim_SM_UlmusDavidiana_Summer_2",new Vector3(b.center.x,b.min.y,b.center.z),new Vector3(0,27,0),t.eulerAngles.y);
                foreach(var old in t.parent.Cast<Transform>().Where(c=>c.name.StartsWith("SacredTree_")&&Primitive(c)).ToArray())Retire(old);
                Record(t,p,"Assets/SeyeonjeongPavilion/Prefabs/SM_UlmusDavidiana_Summer_2.prefab","Existing shared tree LOD and wind materials; plate canopy removed");count++;
            }
            foreach(var baseStone in Primitives().Where(t=>t.name=="Shrine_Base"))
            {
                Guard();var parent=baseStone.parent;var b=baseStone.GetComponent<Renderer>().bounds;
                var holder=new GameObject("KCISA_SeonghwangCairn").transform;holder.SetParent(parent,false);holder.position=new Vector3(b.center.x,b.min.y,b.center.z);
                for(int level=0;level<3;level++)for(int i=0;i<6-level*2;i++)
                {
                    float a=(i*137.5f+level*27)*Mathf.Deg2Rad,rad=.38f-level*.1f;var pos=holder.position+new Vector3(Mathf.Cos(a)*rad,level*.12f-.035f,Mathf.Sin(a)*rad);
                    WorldMacroVisualCorridorAuthoring.PlaceDressingPrototype(holder,"Stone_"+level+"_"+i,"Cheongrim_SM_Rock_L",pos,Vector3.one*(.58f-level*.08f),i*73);
                }
                foreach(var old in parent.Cast<Transform>().Where(t=>new[]{"Shrine_Base","Shrine_Roof","Post"}.Contains(t.name)&&Primitive(t)).ToArray())Retire(old);
                Record(baseStone,holder,"Assets/SeyeonjeongPavilion/Prefabs/SM_Rock_L.prefab","Grounded traditional roadside stone cairn; existing checkpoint/rest interaction retained");count++;
            }
            foreach(var lantern in All.Where(t=>t.name=="Warm_Porch_Lantern"&&t.Find("KCISA_HangingLantern")==null).ToArray())
            {
                var old=lantern.Cast<Transform>().FirstOrDefault(t=>t.name=="Paper_Diffuser");if(old==null)continue;var b=old.GetComponent<Renderer>().bounds;
                const string source="Assets/HwaseongHaenggung/Prefabs/SM_R_Lantern_1.prefab";
                var p=Place(lantern,"KCISA_HangingLantern",source,new Vector3(b.center.x,b.min.y,b.center.z),new Vector3(.42f,1.1f,.42f),lantern.eulerAngles.y);
                foreach(var child in lantern.Cast<Transform>().Where(t=>t!=p).ToArray())
                {if(Primitive(child))Retire(child);foreach(var f in child.GetComponentsInChildren<MeshFilter>())if(f.sharedMesh!=null&&f.transform!=p&&!f.transform.IsChildOf(p))f.sharedMesh=null;}
                Record(old,p,source,"KCISA complete lantern; original light retained");count++;
            }
            foreach(var pagoda in All.Where(t=>t.name.Contains("Pagoda")&&t.GetComponentsInChildren<Transform>().Any(p=>p.name=="Finial"&&Primitive(p))).ToArray())
            {
                const string source="Assets/_Project/Art/World/WorldCompact/KcisaReplacement/Models/StonePagoda.fbx";
                var plinth=pagoda.Find("Plinth");var bottom=plinth.position-Vector3.up*Mathf.Abs(plinth.lossyScale.y)*.5f;
                var p=Place(pagoda,"Authored_GranitePagoda",source,bottom,new Vector3(3.8f,6.6f,3.8f),pagoda.eulerAngles.y);
                var mat=WorldMacroVisualCorridorAuthoring.Surface(Source(StoneCap).parts[0].mat);
                foreach(var r in p.GetComponentsInChildren<Renderer>())r.sharedMaterials=Enumerable.Repeat(mat,r.sharedMaterials.Length).ToArray();
                foreach(var t in pagoda.GetComponentsInChildren<Transform>().Where(Primitive).ToArray())Retire(t,true);
                MeshCollision(p);Record(pagoda,p,source,"New 5,580-triangle UV mapped Blender asset, sloped roof stones and carved moulding; KCISA-derived stone material");count++;
            }
            return FinishChanges("Nature, evidence, shrines, lanterns and pagoda",count);
        }
        static string RefineProps()
        {
            foreach(var pagoda in All.Where(t=>t.name=="Authored_GranitePagoda").ToArray())
            {
                var plinth=pagoda.parent.Find("Plinth");
                float support=plinth.position.y-Mathf.Abs(plinth.lossyScale.y)*.5f;
                var rr=pagoda.GetComponentsInChildren<Renderer>();float bottom=rr.Min(r=>r.bounds.min.y);
                pagoda.position+=Vector3.up*(support-bottom);EditorUtility.SetDirty(pagoda);
            }
            foreach(var holder in All.Where(t=>t.name=="KCISA_SeonghwangCairn").ToArray())foreach(Transform stone in holder)
            {
                if(!stone.name.StartsWith("Stone_"))continue;int level=int.Parse(stone.name.Split('_')[1]);
                var rr=stone.GetComponentsInChildren<Renderer>();float bottom=rr.Min(r=>r.bounds.min.y);
                stone.position+=Vector3.up*(holder.position.y+level*.12f-.035f-bottom);EditorUtility.SetDirty(stone);
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return "Pagoda grounded to preserved plinth/terrace height, not hierarchy origin; cairn stones contact lower tier";
        }
    }
}
