using System;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroActAuthoring
    {
        static string RepairCaveFloor()
        {
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%");
            if(GameObject.Find("ActsTerrain_CaveFloorRepair")!=null)return "Existing cave repair preserved";
            var s=Session;Vector3 begin=new Vector3(1881.24f,195.88f,515f),end=new Vector3(1883.92f,199.6f,584f);
            float Ground(Vector3 p,float fallback)
            {
                var hits=Physics.RaycastAll(p+Vector3.up*1.5f,Vector3.down,3.5f,1,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.71f&&h.collider.attachedRigidbody==null).OrderBy(h=>Mathf.Abs(h.point.y-p.y)).ToArray();
                return hits.Length>0?hits[0].point.y:fallback;
            }
            begin.y=Ground(begin,begin.y);end.y=Ground(end,end.y);
            var verts=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();var failures=new List<string>();
            const int rows=140;
            for(int i=0;i<rows;i++)
            {
                float t=(float)i/(rows-1);Vector3 center=Vector3.Lerp(begin,end,t);center.y+=.015f;
                foreach(float x in new[]{-1.2f,0,1.2f})
                {
                    Vector3 feet=center+Vector3.right*x;
                    var blockers=Physics.OverlapCapsule(feet+Vector3.up*.33f,feet+Vector3.up*1.47f,.27f,1,QueryTriggerInteraction.Ignore)
                        .Where(c=>!c.transform.IsChildOf(s.Walker.Body.transform)&&c.attachedRigidbody==null).ToArray();
                    if(blockers.Length>0)failures.Add(i+"/"+x+":"+string.Join(",",blockers.Select(c=>c.name)));
                }
                for(int j=0;j<6;j++)
                {
                    float x=j==0?-5:j==1?-1.8f:j==2?1.8f:j==3?5:j==4?5:-5;
                    var p=center+Vector3.right*x;
                    if(j==0||j==3)p.y=Ground(p,center.y-.5f)-.04f;
                    if(j>=4)p.y=center.y-1.5f;
                    verts.Add(p);uv.Add(new Vector2(x,t*69));
                }
                if(i==0)continue;
                for(int j=0;j<6;j++)
                {
                    int a=(i-1)*6+j,b=(i-1)*6+(j+1)%6,c=i*6+j,d=i*6+(j+1)%6;
                    tris.Add(a);tris.Add(c);tris.Add(b);tris.Add(b);tris.Add(c);tris.Add(d);
                }
            }
            if(failures.Count>0)return Write("cave_floor_repair.json",new Report{status="BLOCKED_BY_ACTUAL_COLLIDERS",scope="No floor mesh created",failures=failures});
            var oldFloor=Components<MeshRenderer>().First(m=>m.name=="Continuous_Approach_Soil");
            var mesh=new Mesh{name="CaveApproach_ContinuousRepair"};mesh.SetVertices(verts);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Folder+"/CaveFloorRepair.asset");
            var root=new GameObject("ActsTerrain_CaveFloorRepair");root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=oldFloor.sharedMaterial;root.AddComponent<MeshCollider>().sharedMesh=mesh;
            Physics.SyncTransforms();int corrected=0;
            for(int i=0;i<s.Content.MainPath.Length;i++)
            {
                var p=s.Content.MainPath[i];if(p.x<1878||p.x>1888||p.z<500||p.z>612)continue;
                p.y=Ground(p,p.y)+.05f;s.Content.MainPath[i]=p;corrected++;
            }
            EditorUtility.SetDirty(s.Content);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(s.gameObject.scene);EditorSceneManager.SaveScene(s.gameObject.scene);
            return Write("cave_floor_repair.json",new Report{status="AUTHORED_NEEDS_PHYSICS_RECHECK",scope="Missing 69m cave approach support, real floor separate from mountain roof; existing soil material reused",checks=new List<string>{"3.6m continuous core width","420 standing capsule clearance samples clear","MainPath heights corrected: "+corrected}});
        }
    }
}
