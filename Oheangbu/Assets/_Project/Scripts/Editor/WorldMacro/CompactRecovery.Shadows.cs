using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRecovery
    {
        static string ShadowCasters()
        {
            RequireEdit();var scene=SceneManager.GetActiveScene();
            var sun=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Light>(true)).First(l=>l.type==LightType.Directional&&l.enabled);
            Vector3 p=new Vector3(852.600769f,132.355682f,191.979431f);var direction=-sun.transform.forward;
            var lines=new List<string>{"sun="+sun.name+" direction="+direction+" bias="+sun.shadowBias+" normalBias="+sun.shadowNormalBias};
            foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)))
            {
                if(!r.enabled||!r.gameObject.activeInHierarchy||r.shadowCastingMode==ShadowCastingMode.Off)continue;
                if(!r.bounds.IntersectRay(new Ray(p+Vector3.up*.005f,direction),out var distance))continue;
                var mesh=r.GetComponent<MeshFilter>()?.sharedMesh;if(mesh==null)continue;
                var v=mesh.vertices;var ts=mesh.triangles;var ray=new Ray(r.transform.InverseTransformPoint(p+Vector3.up*.005f),r.transform.InverseTransformVector(direction));
                var hits=new List<string>();
                for(int i=0;i<ts.Length;i+=3)
                {
                    var a=v[ts[i]];var e1=v[ts[i+1]]-a;var e2=v[ts[i+2]]-a;var h=Vector3.Cross(ray.direction,e2);float det=Vector3.Dot(e1,h);if(Mathf.Abs(det)<1e-8f)continue;
                    var s=ray.origin-a;float u=Vector3.Dot(s,h)/det;if(u<0||u>1)continue;var q=Vector3.Cross(s,e1);float w=Vector3.Dot(ray.direction,q)/det;if(w<0||w+u>1)continue;
                    float t=Vector3.Dot(e2,q)/det;if(t<0)continue;
                    hits.Add("triangle="+(i/3)+" distance="+t+" face="+Vector3.Cross(e1,e2).normalized);
                }
                lines.Add(PathOf(r.transform)+" boundsDistance="+distance+" shader="+string.Join(",",r.sharedMaterials.Where(m=>m!=null).Select(m=>m.shader.name))+" hits="+string.Join(";",hits));
            }
            string result=string.Join("\n",lines);File.WriteAllText(Path.Combine(Output,"shadow_casters.txt"),result);return result;
        }
    }
}
