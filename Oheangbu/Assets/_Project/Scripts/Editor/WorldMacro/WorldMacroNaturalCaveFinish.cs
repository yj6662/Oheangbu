using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oheangbu.App.World.Dressing;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroNaturalCave
    {
        static string OuterGround()
        {
            if(Root.Find("Portal_Outer_Soil")!=null)throw new Exception("Outer ground already present");
            var grid=JsonUtility.FromJson<NaturalCaveSolidPortal.HeightGrid>(File.ReadAllText(Output+"/portal_original_height.json"));var vs=new List<Vector3>();var ts=new List<int>();
            for(int x=0;x<=23;x++)for(int z=0;z<=53;z++)
            {
                var p=new Vector3(-18+x,0,-38+z);int ix=Mathf.Clamp(Mathf.RoundToInt(p.x-grid.xs[0]),0,grid.xs.Length-1),iz=Mathf.Clamp(Mathf.RoundToInt(p.z-grid.zs[0]),0,grid.zs.Length-1);float original=grid.heights[ix*grid.zs.Length+iz];
                var hits=Physics.RaycastAll(Root.TransformPoint(new Vector3(p.x,50,p.z)),Vector3.down,80,1).Where(h=>h.collider.name.StartsWith("Terrain_")).OrderBy(h=>h.distance).ToArray();if(hits.Length>0)original=Root.InverseTransformPoint(hits[0].point).y;
                p.y=Mathf.Min(.008f,original-.018f);vs.Add(p);
            }
            for(int x=0;x<23;x++)for(int z=0;z<53;z++){int a=x*54+z,b=a+54;ts.AddRange(new[]{a,a+1,b,b,a+1,b+1});}
            var mesh=new Mesh{name="Portal_Outer_Soil"};mesh.SetVertices(vs);mesh.SetTriangles(ts,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=Save(mesh,mesh.name);var obj=new GameObject(mesh.name);obj.transform.SetParent(Root,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=Rock(true);obj.AddComponent<MeshCollider>().sharedMesh=mesh;Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Added 2438-triangle soil underlay for the open approach at the SDF patch boundary; below the preserved road.";
        }
        static string FinishEntryProps()
        {
            var lines=new List<string>();
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t=>t.name.StartsWith("MineRock_")))
            {
                var rs=t.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)continue;var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
                var hits=Physics.RaycastAll(bounds.center+Vector3.up*20,Vector3.down,50,1).Where(h=>h.collider.name.StartsWith("Terrain_")||h.collider.name.Contains("Portal_Terrain")||h.collider.name.Contains("Broad_Apron")).OrderBy(h=>h.distance).ToArray();
                if(hits.Length>0){float dy=hits[0].point.y-bounds.min.y-.16f;t.position+=Vector3.up*dy;lines.Add(t.name+" grounded deltaY="+dy);}
                foreach(var r in rs)r.sharedMaterials=r.sharedMaterials.Select(_=>Rock()).ToArray();
            }
            // Preserve the collision approach; replace its borrowed slab appearance with the same local soil.
            var ramp=Old.Find("Cave_Entrance_Stone_Ramp");if(ramp!=null){var r=ramp.GetComponent<Renderer>();if(r!=null){r.enabled=true;r.sharedMaterial=Rock(true);}}
            var owned=GameObject.Find("Playtest_OwnedAssets");if(owned!=null)foreach(var t in owned.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Ramp_Scan_Surface"))foreach(var r in t.GetComponentsInChildren<Renderer>(true))r.enabled=false;
            Physics.SyncTransforms();File.WriteAllLines(Output+"/entrance_prop_grounding.txt",lines);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return string.Join("\n",lines);
        }
        static string Finish()
        {
            var root=Root;var lines=new List<string>();
            var inn=GameObject.Find(WorldMacroOriginalInn.RootName).transform;var earth=inn.Find("Packed_Earth_Forecourt").GetComponent<MeshFilter>();
            var floor=earth.sharedMesh;if(floor.normals.Average(n=>n.y)<0){var t=floor.triangles;for(int i=0;i<t.Length;i+=3){int temp=t[i+1];t[i+1]=t[i+2];t[i+2]=temp;}floor.triangles=t;floor.RecalculateNormals();EditorUtility.SetDirty(floor);earth.GetComponent<MeshCollider>().sharedMesh=null;earth.GetComponent<MeshCollider>().sharedMesh=floor;}
            var dressing=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();
            string path=Folder+"/Dressing_CaveAware.asset";var sheet=AssetDatabase.LoadAssetAtPath<Sheet>(path);
            if(sheet==null){sheet=Object.Instantiate(dressing.Sheet);sheet.name="Dressing_CaveAware";AssetDatabase.CreateAsset(sheet,path);}
            var exclusions=sheet.PreservedAreas.Where(a=>a.Id!="content:natural_cave_throat"&&a.Id!="content:c2_inn_forecourt").ToList();
            exclusions.Add(new Sheet.PreserveArea{Id="content:natural_cave_throat",Centre=root.TransformPoint(new Vector3(-40,0,0)),HalfSize=new Vector2(35,30),Yaw=root.eulerAngles.y,LimitHeight=true,MinimumY=root.position.y-3,MaximumY=root.position.y+19,AffectedKinds=31,TypedClearance=true,Padding=Vector4.zero});
            exclusions.Add(new Sheet.PreserveArea{Id="content:c2_inn_forecourt",Centre=new Vector3(1998,133,687),HalfSize=new Vector2(7,10),Yaw=0,AffectedKinds=31,TypedClearance=true,Padding=Vector4.zero});
            sheet.PreservedAreas=exclusions.ToArray();dressing.Sheet=sheet;EditorUtility.SetDirty(sheet);EditorUtility.SetDirty(dressing);dressing.ResetCache();
            var extra=Object.FindFirstObjectByType<EarlyRegionFoliage>();int removed=0;
            if(extra!=null)foreach(var packet in extra.Packets)foreach(var part in packet.Near.Concat(packet.Far))
            {var before=part.Matrices.Length;part.Matrices=part.Matrices.Where(m=>!exclusions.Skip(exclusions.Count-2).Any(e=>Sheet.Excludes(e,(Vector3)m.GetColumn(3),Sheet.Kind.Tree))).ToArray();removed+=before-part.Matrices.Length;}
            if(extra!=null)EditorUtility.SetDirty(extra);lines.Add("Supplemental foliage matrix entries removed from throat/forecourt="+removed+"; hill foliage above cave remains allowed.");
            var details=root.Find("Authored_Mine_Details");if(details!=null)foreach(var mf in details.GetComponentsInChildren<MeshFilter>())if(mf.name.StartsWith("Mine_Detail_")&&mf.GetComponent<MeshCollider>()==null)mf.gameObject.AddComponent<MeshCollider>().sharedMesh=mf.sharedMesh;
            // Ore veins are attached to the real cavern wall using its collider, with two worksite clusters.
            if(root.Find("Exposed_Ore") == null)
            {
                var ore=new GameObject("Exposed_Ore").transform;ore.SetParent(root,false);var wall=root.Find("Natural_Cave_Interior").GetComponent<MeshCollider>();
                var material=new Material(Shader.Find("Oheangbu/InkLightSource"));material.SetFloat("_UseVeinColor",1);material.SetFloat("_SourceIntensity",1.4f);material.SetFloat("_BandWidth",.72f);material.SetFloat("_VeinBreakup",.2f);AssetDatabase.CreateAsset(material,Folder+"/Materials/Ore_Vein.mat");
                int k=0;foreach(var origin in new[]{new Vector3(-137,2.3f,-7),new Vector3(-79,2.6f,10),new Vector3(-52,2.5f,-2)})
                {
                    var toward=origin.z<0?Vector3.back:Vector3.forward;var vertices=new List<Vector3>();var uvs=new List<Vector2>();var tris=new List<int>();Vector3 midpoint=Vector3.zero;int hitCount=0;
                    for(int i=0;i<21;i++){float x=origin.x-2.5f+i*.25f,y=origin.y+Mathf.Sin(i*.37f)*.42f;var ray=new Ray(root.TransformPoint(new Vector3(x,y,origin.z)),root.TransformDirection(toward));if(!wall.Raycast(ray,out var hit,20))continue;var point=root.InverseTransformPoint(hit.point+hit.normal*.025f);midpoint+=point;hitCount++;vertices.Add(point+Vector3.up*.15f);vertices.Add(point-Vector3.up*.15f);uvs.Add(new Vector2(i/20f,1));uvs.Add(new Vector2(i/20f,0));if(hitCount>1){int n=vertices.Count-4;tris.AddRange(new[]{n,n+1,n+2,n+2,n+1,n+3});}}
                    if(hitCount<2)continue;var mesh=new Mesh{name="Ore_Seam_"+k};mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=Save(mesh,mesh.name);var obj=new GameObject(mesh.name);obj.transform.SetParent(ore,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=material;
                    var lamp=new GameObject("Ore_Bounce_"+k);lamp.transform.SetParent(ore,false);lamp.transform.localPosition=midpoint/hitCount-toward*.7f;var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(.50f,.61f,.45f);light.intensity=5;light.range=15;light.shadows=LightShadows.None;k++;
                }
                lines.Add("Wall-conforming ore clusters="+k);
            }
            Physics.SyncTransforms();lines.Add((Session.TrySafeFeet(Session.Content.InnCheckpointFeet,out _)?"PASS ":"FAIL ")+"inn checkpoint after grounding");
            foreach(var point in Session.Content.Points.Where(p=>p.Id=="geumpyo_inn"||p.Id=="logger"||p.Id=="herbalist"))lines.Add((Session.TrySafeFeet(point.Position+Vector3.back*.8f,out _)?"PASS ":"FAIL ")+point.Id+" approach support");
            File.WriteAllLines(Output+"/final_refinements.txt",lines);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return string.Join("\n",lines);
        }
    }
}
