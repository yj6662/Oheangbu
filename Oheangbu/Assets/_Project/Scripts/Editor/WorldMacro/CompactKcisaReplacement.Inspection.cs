using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactKcisaReplacement
    {
        [Serializable] sealed class ClipBindingCheck {public string controller,clip;public int bindings,missing;public string[] missingPaths;}
        [Serializable] sealed class ClipChecks {public string mode="Editor transform-path compatibility only; runtime locomotion/combat not verified";public ClipBindingCheck[] clips;}
        static string CheckActorBindings()
        {
            var rows=new System.Collections.Generic.List<ClipBindingCheck>();
            var actors=All.Where(t=>t.name=="OwnedActorAppearance").Select(t=>t.GetComponentInChildren<Animator>()).Where(a=>a!=null).GroupBy(a=>a.runtimeAnimatorController);
            foreach(var group in actors)
            {
                var animator=group.First();
                foreach(var clip in group.Key.animationClips.Distinct())
                {
                    var bindings=AnimationUtility.GetCurveBindings(clip);var missing=bindings.Where(b=>!string.IsNullOrEmpty(b.path)&&animator.transform.Find(b.path)==null).Select(b=>b.path).Distinct().ToArray();
                    rows.Add(new ClipBindingCheck{controller=group.Key.name,clip=clip.name,bindings=bindings.Length,missing=missing.Length,missingPaths=missing});
                }
            }
            string json=JsonUtility.ToJson(new ClipChecks{clips=rows.ToArray()},true);File.WriteAllText(Output+"/animation_bindings.json",json);return json;
        }
        static string InspectSurfaces()
        {
            var sb=new StringBuilder();var pagoda=All.First(t=>t.name=="Authored_GranitePagoda");
            foreach(var part in Source(StoneCap).parts){var vv=part.mesh.vertices;var tt=part.mesh.GetTriangles(part.slot);float area=0;for(int i=0;i<tt.Length;i+=3){var a=part.matrix.MultiplyPoint3x4(vv[tt[i]]);var b=part.matrix.MultiplyPoint3x4(vv[tt[i+1]]);var vertexC=part.matrix.MultiplyPoint3x4(vv[tt[i+2]]);area+=Mathf.Max(0,Vector3.Cross(b-a,vertexC-a).y/2);}sb.AppendLine("source projected top area="+area+" mesh="+part.mesh.name);}
            sb.AppendLine("pagoda position="+pagoda.position+" bounds="+pagoda.GetComponentInChildren<Renderer>().bounds);
            foreach(var holder in All.Where(t=>t.name=="KCISA_MasonryAssembly"&&(t.position-pagoda.position).sqrMagnitude<900))
            {
                var f=holder.Find("SourceSurface_0").GetComponent<MeshFilter>();var r=f.GetComponent<Renderer>();var mesh=f.sharedMesh;
                sb.AppendLine(PathOf(holder));sb.AppendLine("old scale="+holder.parent.lossyScale+" holder="+holder.lossyScale+" local bounds="+mesh.bounds+" world="+r.bounds+" enabled="+r.enabled+" active="+r.gameObject.activeInHierarchy+" forcedOff="+r.forceRenderingOff+" vertices="+mesh.vertexCount);
                var v=mesh.vertices;var tri=mesh.triangles;int up=0,down=0;
                for(int i=0;i<tri.Length;i+=3){var n=Vector3.Cross(v[tri[i+1]]-v[tri[i]],v[tri[i+2]]-v[tri[i]]).normalized;if(n.y>.5f)up++;if(n.y<-.5f)down++;}
                sb.AppendLine("tris="+tri.Length/3+" up="+up+" down="+down+" mat="+AssetDatabase.GetAssetPath(r.sharedMaterial)+" texture="+AssetDatabase.GetAssetPath(r.sharedMaterial.mainTexture));
                var p=r.bounds.center;foreach(var h in Physics.RaycastAll(new Vector3(p.x,r.bounds.max.y+10,p.z),Vector3.down,30).OrderBy(h=>h.distance))sb.AppendLine("support="+PathOf(h.transform)+" y="+h.point.y);
            }
            var terrain=All.First(t=>t.name=="Terrain_050");var c=terrain.GetComponent<MeshCollider>();sb.AppendLine("terrain50="+AssetDatabase.GetAssetPath(c.sharedMesh)+" dirty="+EditorUtility.IsDirty(c.sharedMesh));
            File.WriteAllText(Output+"/surface_inspection.txt",sb.ToString());return sb.ToString();
        }
    }
}
