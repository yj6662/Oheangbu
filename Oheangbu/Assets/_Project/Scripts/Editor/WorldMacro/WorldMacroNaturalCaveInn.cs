using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroNaturalCave
    {
        static string InnGround()
        {
            var parent=GameObject.Find(WorldMacroOriginalInn.RootName).transform;
            if(parent.Find("Packed_Earth_Forecourt")!=null)throw new Exception("Inn ground already fitted");
            var house=parent.Find("Thatched_Inn_C2_Original");house.position-=Vector3.up*.15f;
            parent.Find("Forecourt_Maru_FromExistingAsset").gameObject.SetActive(false);
            float Ground(float x,float z)=>Physics.RaycastAll(new Vector3(x,160,z),Vector3.down,80,1).Where(h=>h.collider.name.StartsWith("Terrain_")).OrderBy(h=>h.distance).First().point.y;
            var center=new Vector3(Session.Content.InnCheckpointFeet.x,133.16f,Session.Content.InnCheckpointFeet.z+1.2f);
            var verts=new List<Vector3>();var uv=new List<Vector2>();var ts=new List<int>();const int rings=18,sides=72;
            for(int r=0;r<=rings;r++)for(int i=0;i<sides;i++)
            {float a=i*Mathf.PI*2/sides,f=r/(float)rings;float rough=1+.035f*Mathf.Sin(a*5)+.025f*Mathf.Cos(a*9);float x=center.x+Mathf.Cos(a)*13*f*rough,z=center.z+Mathf.Sin(a)*9*f*rough;float natural=Ground(x,z);float y=Mathf.Lerp(Mathf.Max(center.y,natural),natural,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.6f,1,f)))+.012f;verts.Add(new Vector3(x,y,z));uv.Add(new Vector2(x,z)*.3f);}
            for(int r=0;r<rings;r++)for(int i=0;i<sides;i++){int a=r*sides+i,b=r*sides+(i+1)%sides,c=a+sides,d=b+sides;ts.AddRange(new[]{a,c,b,b,c,d});}
            var mesh=new Mesh{name="Packed_Earth_Forecourt"};mesh.SetVertices(verts);mesh.SetUVs(0,uv);mesh.SetTriangles(ts,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=Save(mesh,mesh.name);
            var go=new GameObject(mesh.name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=Rock(true);go.AddComponent<MeshCollider>().sharedMesh=mesh;
            // Source stairs remain visible. A continuous sloping collision surface avoids capsule snagging on
            // the decorative stone risers; it connects actual bottom terrain to the existing raised porch.
            var steps=house.Cast<Transform>().Where(t=>t.name.StartsWith("Inn_Entry_Step_")).OrderBy(t=>t.name).ToArray();
            foreach(var step in steps)foreach(var c in step.GetComponentsInChildren<Collider>())c.enabled=false;
            var first=steps.First().GetComponent<Renderer>().bounds;var last=steps.Last().GetComponent<Renderer>().bounds;
            float x0=first.min.x,x1=first.max.x,z0=first.min.z-.25f,z1=last.max.z+.20f;
            float y0=Ground((x0+x1)*.5f,z0)+.025f,y1=last.max.y+.208f;
            // Raise only collision over the treads, with no scene-wide terrain/camera changes.
            var ramp=new Mesh{name="Inn_Stair_Traversal"};ramp.vertices=new[]{new Vector3(x0,y0,z0),new Vector3(x1,y0,z0),new Vector3(x0,y1,z1),new Vector3(x1,y1,z1)};ramp.triangles=new[]{0,2,1,1,2,3};ramp.RecalculateNormals();ramp.RecalculateBounds();ramp=Save(ramp,ramp.name);
            var col=new GameObject("Inn_Stair_Traversal");col.transform.SetParent(parent,false);col.AddComponent<MeshCollider>().sharedMesh=ramp;
            Physics.SyncTransforms();
            var checks=new List<string>{"Original C2 copied house lowered 0.15m to seat its base in terrain.","Large wooden forecourt removed; packed-earth forecourt uses owned YongmeoriCoast ground texture and slopes into physical terrain.","Original stone stair visuals retained with continuous ramp collision; source prefab and its colliders untouched.","Collision slope="+Mathf.Atan2(y1-y0,z1-z0)*Mathf.Rad2Deg};
            int passed=0,total=0;for(float z=z0+.4f;z<=z1+.45f;z+=.15f){total++;if(Session.TrySafeFeet(new Vector3((x0+x1)*.5f,y1,z),out _))passed++;}
            checks.Add((passed==total?"PASS ":"FAIL ")+"stair supported capsule samples="+passed+"/"+total);
            foreach(string id in new[]{"geumpyo_inn","logger","herbalist"}){var point=Session.Content.Points.First(p=>p.Id==id);checks.Add((Session.TrySafeFeet(point.Position+Vector3.back*.8f,out _)?"PASS ":"FAIL ")+id+" nearby support");}
            File.WriteAllLines(Output+"/inn_grounding.txt",checks);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return string.Join("\n",checks);
        }
    }
}
