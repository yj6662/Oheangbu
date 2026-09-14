using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroEarlyArt
    {
        static string Folder=>WorldMacroPlaytestAuthoring.Folder+"/EarlyArt";
        static readonly Dictionary<string,Dictionary<Material,List<CombineInstance>>> geometry=new Dictionary<string,Dictionary<Material,List<CombineInstance>>>();
        static readonly List<string> ledger=new List<string>();
        static Sheet.Prototype Proto(string id)=>Dressing.Sheet.Prototypes.First(p=>p.Id=="Cheongrim_"+id);
        static void Add(string group,Sheet.Prototype prototype,Matrix4x4 transform,int lod=1)
        {
            if(!geometry.TryGetValue(group,out var batches)){batches=new Dictionary<Material,List<CombineInstance>>();geometry.Add(group,batches);}
            foreach(var part in prototype.Lods[Mathf.Min(lod,prototype.Lods.Length-1)].Parts)
            {
                if(!batches.TryGetValue(part.Material,out var list)){list=new List<CombineInstance>();batches.Add(part.Material,list);}
                list.Add(new CombineInstance{mesh=part.Mesh,subMeshIndex=part.Submesh,transform=transform*part.Local});
            }
            ledger.Add(group+"\t"+prototype.Id+"\t"+transform.GetColumn(3)+"\t"+prototype.SourcePath);
        }
        static void Beam(string group,Vector3 a,Vector3 b,float width)
        {
            var p=Proto("SM_M_WoodLog");var q=Quaternion.LookRotation(b-a);
            var scale=new Vector3(width/p.Size.x,width/p.Size.y,Vector3.Distance(a,b)/p.Size.z);
            var center=(a+b)*.5f-q*Vector3.up*width*.5f;
            Add(group,p,Matrix4x4.TRS(center,q,scale));
        }
        static void Prop(string group,string id,Vector3 point,float scale,float yaw)
            =>Add(group,Proto(id),Matrix4x4.TRS(point,Quaternion.Euler(0,yaw,0),Vector3.one*scale));
        static Vector3 Ground(Vector3 p)=>new Vector3(p.x,WorldMacroVisualCorridorAuthoring.TerrainY(p),p.z);
        static string Apply()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.isDirty)throw new Exception("Save existing scene before this independent art pass");
            if(GameObject.Find(Root)!=null)throw new Exception("Early art already exists; use targeted repair");
            if(!File.Exists(Output+"/BeforeEarlyArt.unity"))File.Copy(scene.path,Output+"/BeforeEarlyArt.unity");
            File.WriteAllText(Output+"/content_expected.json",EditorJsonUtility.ToJson(Session.Content,true));
            DevSceneKit.EnsureFolder(Folder+"/Meshes");
            var root=new GameObject(Root).transform;geometry.Clear();ledger.Clear();
            var cave=GameObject.Find(WorldMacroLandmarkAuthoring.RootName).transform.Find("Cave");
            Vector3 C(float x,float y,float z)=>cave.TransformPoint(new Vector3(x,y,z));
            // Irregularly spaced timber sets frame the existing open walking and combat corridor.
            foreach(float z in new[]{-13f,-5.5f,4f,13f})
            {
                foreach(float side in new[]{-1f,1f})
                {
                    Beam("Mine_Supports",C(side*4.5f,.02f,z),C(side*4.5f,4.65f,z),.36f);
                    Beam("Mine_Supports",C(side*4.5f,3.2f,z),C(side*3.3f,4.65f,z),.25f);
                }
                Beam("Mine_Supports",C(-4.7f,4.7f,z),C(4.7f,4.7f,z),.44f);
            }
            // The investigation text already describes inward debris and a burned cord. Show it locally.
            Beam("Mine_BlastDebris",C(2.6f,.10f,8),C(4.2f,.35f,10),.30f);
            Beam("Mine_BlastDebris",C(3.2f,.03f,6.7f),C(4.7f,.26f,9.5f),.20f);
            for(int i=0;i<22;i++)
            {
                float z=6+i*.41f,x=3.1f+Mathf.Sin(i*2.8f)*.9f;
                Prop("Mine_BlastDebris","SM_Rock_L",C(x,-.03f,z),.35f+(i%4)*.20f,i*71);
            }
            Prop("Mine_Workplace","SM_M_WoodenBox",C(-4.2f,.02f,8.5f),.75f,30);
            Prop("Mine_Workplace","SM_M_WoodenBox",C(-4.25f,.02f,7.6f),.52f,22);
            Prop("Mine_Workplace","SM_052_Pot",C(-4.0f,.02f,6.7f),.8f,0);
            // A few abandoned transport bundles outside, without lining the entire road with props.
            for(int i=0;i<6;i++)
            {
                var a=Ground(C(6+i%2*.27f,0,-19-i/2*.25f));
                Beam("Mine_Transport",a+Vector3.up*(.15f+(i/2)*.14f),a+cave.forward*2.5f+Vector3.up*(.15f+(i/2)*.14f),.22f);
            }
            var bag=Session.Content.Points.First(p=>p.Id=="worker_satchel").Position;
            for(int i=0;i<3;i++)Prop("Branch_WorkerTrace","SM_Rock_L",Ground(bag+new Vector3(1.2f+i*.4f,0,.6f)),.5f+i*.12f,i*23);
            var inn=GameObject.Find("Playtest_OwnedAssets").transform.Find("Geumpyo_ThatchedInn");
            for(int i=0;i<9;i++)
            {
                var a=Ground(inn.TransformPoint(new Vector3(-11.3f+(i%3)*.23f,0,1.8f)));
                Beam("Inn_FuelStore",a+Vector3.up*(.1f+(i/3)*.15f),a+inn.forward*1.9f+Vector3.up*(.1f+(i/3)*.15f),.20f);
            }
            for(int i=0;i<3;i++)Prop("Inn_FuelStore","SM_052_Pot",Ground(inn.TransformPoint(new Vector3(10.8f,0,2.1f+i*.65f))),1.1f-i*.15f,i*18);
            BakeGeometry(root);
            var cord=WorldMacroVisualCorridorAuthoring.PlaceSource(root,"Blast_Cord","Assets/KoreanTraditionalFestival/Prefabs/SM_Rope.prefab",C(3.0f,.08f,8.5f),new Vector3(1.1f,.18f,1.1f),61);
            DevSceneKit.EnsureFolder(Folder+"/Materials");
            foreach(var renderer in cord.GetComponentsInChildren<Renderer>())
            {
                string path=Folder+"/Materials/CharredCord.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(mat==null){mat=new Material(renderer.sharedMaterial);mat.SetColor("_BaseColor",new Color(.13f,.105f,.082f,1));AssetDatabase.CreateAsset(mat,path);}
                renderer.sharedMaterial=mat;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            WorldMacroVisualCorridorAuthoring.PlaceSource(root,"Exit_Transport_Basket","Assets/KoreanTraditionalFestival/Prefabs/SM_Basket.prefab",Ground(C(6.8f,0,-21)),new Vector3(.8f,.6f,.8f),18);
            BuildFoliage(root);
            File.WriteAllLines(Output+"/asset_ledger.tsv",new[]{"group\tprototype\tposition\tsupplier"}.Concat(ledger));
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Refine();GroundFoliage();FoliageMaterials();CardSize();return Validate();
        }
        static void BakeGeometry(Transform root)
        {
            foreach(var group in geometry)
            {
                var holder=new GameObject(group.Key).transform;holder.SetParent(root,false);int index=0;
                foreach(var batch in group.Value)
                {
                    var mesh=new Mesh{name=group.Key,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                    mesh.CombineMeshes(batch.Value.ToArray(),true,true);mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh,Folder+"/Meshes/"+group.Key+"_"+index+".asset");
                    var node=new GameObject("Surface_"+index++);node.transform.SetParent(holder,false);
                    node.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=node.AddComponent<MeshRenderer>();renderer.sharedMaterial=batch.Key;
                    renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                    // Fixed world-space authored geometry, no additional runtime collision workload.
                    node.isStatic=true;
                }
            }
        }
        static float PathDistance(Vector3 p,Vector3[] route)
        {
            float best=float.MaxValue;var v=new Vector2(p.x,p.z);
            // MainPath is densely sampled; short segment stride retains bends and avoids quadratic authoring time.
            for(int i=0;i<route.Length-1;i+=12)
            {
                var a=new Vector2(route[i].x,route[i].z);var b=new Vector2(route[Mathf.Min(i+12,route.Length-1)].x,route[Mathf.Min(i+12,route.Length-1)].z);var d=b-a;
                best=Mathf.Min(best,Vector2.Distance(v,a+d*(d.sqrMagnitude<.001f?0:Mathf.Clamp01(Vector2.Dot(v-a,d)/d.sqrMagnitude))));
            }
            return best;
        }
        static void BuildFoliage(Transform root)
        {
            var sheet=Dressing.Sheet;var groups=new Dictionary<string,List<Matrix4x4>>();var prototypes=new Dictionary<string,Sheet.Prototype>();
            var centres=new Dictionary<string,List<Vector3>>();var route=Session.Content.MainPath;
            var records=new List<string>{"id\tprototype\tx\ty\tz\tscale"};
            var trees=new[]{Proto("SM_UlmusDavidiana_Summer_2"),Proto("SM_PinusDensiflora_Spring_2")};
            var fern=Proto("SM_Deparia_3");var grass=Proto("SM_Grass_Haenggung");int placed=0;
            void Put(Sheet.Prototype proto,Vector3 p,float scale,float yaw)
            {
                string key=proto.Id+"_"+Mathf.FloorToInt(p.x/128)+"_"+Mathf.FloorToInt(p.z/128);
                if(!groups.ContainsKey(key)){groups[key]=new List<Matrix4x4>();centres[key]=new List<Vector3>();prototypes[key]=proto;}
                groups[key].Add(Matrix4x4.TRS(p,Quaternion.Euler(0,yaw,0),Vector3.one*scale));centres[key].Add(p);
                records.Add((placed++)+"\t"+proto.Id+"\t"+p.x+"\t"+p.y+"\t"+p.z+"\t"+scale);
            }
            // Only the first region's visible slopes; high bare rock and passages remain uncovered.
            for(int x=1700;x<4150;x+=23)for(int z=400;z<2900;z+=23)
            {
                uint h=Sheet.Hash(x,z,20260914);float u=Sheet.Unit(h);if(u>.67f)continue;
                var p=new Vector3(x+Sheet.Unit(h>>3)*17,0,z+Sheet.Unit(h>>5)*17);
                float d=PathDistance(p,route);if(d<28||d>1150)continue;
                var cell=sheet.Cells.FirstOrDefault(c=>{var origin=sheet.Geography.BoundsMin+new Vector2(c.X*256,c.Z*256);return p.x>=origin.x&&p.x<origin.x+256&&p.z>=origin.y&&p.z<origin.y+256;});
                if(cell==null)continue;var o=sheet.Geography.BoundsMin+new Vector2(cell.X*256,cell.Z*256);
                p.y=Sheet.Height(cell,p.x,p.z,o);float slope=Sheet.Slope(cell,p.x,p.z,o);
                if(slope>58||p.y<100||p.y>900||Mathf.PerlinNoise(p.x*.007f,p.z*.007f)<.39f)continue;
                if(sheet.PreservedAreas.Any(a=>Sheet.Excludes(a,p,Sheet.Kind.Tree))||sheet.Passages.Any(a=>Sheet.PassageEdgeDistance(a,p)<9))continue;
                var proto=trees[p.y>400||slope>38?1:(h&7)<5?0:1];Put(proto,p-Vector3.up*.12f,(.8f+u*.7f)*Mathf.Lerp(1,.65f,Mathf.InverseLerp(400,900,p.y)),u*539);
            }
            // Fern and low grass pockets flank, rather than occupy, the usable trail.
            for(int n=36;n<route.Length-12;n+=28)
            {
                var tangent=(route[n+1]-route[n-1]).normalized;var side=Vector3.Cross(Vector3.up,tangent).normalized;
                for(int k=0;k<8;k++)
                {
                    float offset=(k%2==0?1:-1)*(6+(k/2)*1.4f);var p=Ground(route[n]+side*offset+tangent*Mathf.Sin(n+k)*2);
                    if(Mathf.Abs(p.y-route[n].y)>6||p.y<100)continue;
                    var proto=k%3==0?fern:grass;
                    if(sheet.PreservedAreas.Any(a=>Sheet.Excludes(a,p,proto.Category))||sheet.Passages.Any(a=>Sheet.PassageEdgeDistance(a,p)<1.2f))continue;
                    Put(proto,p-Vector3.up*.035f,1.0f+((n+k)%5)*.13f,n*17+k*53);
                }
            }
            var packets=new List<EarlyRegionFoliage.Packet>();
            foreach(var group in groups)
            {
                var proto=prototypes[group.Key];var matrices=group.Value;var b=new Bounds(centres[group.Key][0],Vector3.zero);foreach(var p in centres[group.Key])b.Encapsulate(p);b.Expand(new Vector3(45,48,45));
                EarlyRegionFoliage.Part[] Parts(int lod)=>proto.Lods[lod].Parts.Select(p=>new EarlyRegionFoliage.Part{Mesh=p.Mesh,Material=p.Material,Submesh=p.Submesh,Matrices=matrices.Select(m=>m*p.Local).ToArray()}).ToArray();
                if(matrices.Count>1023)throw new Exception("Packet exceeds instancing bound");
                packets.Add(new EarlyRegionFoliage.Packet{Bounds=b,Count=matrices.Count,Near=Parts(Mathf.Min(1,proto.Lods.Length-1)),Far=Parts(proto.Lods.Length-1),Distance=proto.Category==Sheet.Kind.Tree?2400:380});
            }
            var component=root.gameObject.AddComponent<EarlyRegionFoliage>();component.Owner=Dressing;component.Packets=packets.ToArray();
            File.WriteAllLines(Output+"/foliage_placements.tsv",records);
        }
        static string Refine()
        {
            var foliage=GameObject.Find(Root).GetComponent<EarlyRegionFoliage>();var grass=Proto("SM_Grass_Haenggung");
            int changed=0;
            foreach(var packet in foliage.Packets)
            {
                if(packet.Distance>400||!packet.Far.Any(p=>p.Mesh.GetIndexCount(p.Submesh)/3>100))continue;
                var source=packet.Far[0];var fern=Proto("SM_Deparia_3");var inv=fern.Lods[fern.Lods.Length-1].Parts[0].Local.inverse;
                var matrices=source.Matrices.Select(m=>m*inv).ToArray();
                packet.Far=grass.Lods.Last().Parts.Select(p=>new EarlyRegionFoliage.Part{Mesh=p.Mesh,Material=p.Material,Submesh=p.Submesh,Matrices=matrices.Select(m=>m*p.Local).ToArray()}).ToArray();changed++;
            }
            EditorUtility.SetDirty(foliage);EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return "Fern far cards repaired in "+changed+" packets.\n"+Validate();
        }
        static string GroundFoliage()
        {
            var foliage=GameObject.Find(Root).GetComponent<EarlyRegionFoliage>();float largest=0;int adjusted=0;
            foreach(var packet in foliage.Packets)
            {
                var near=packet.Near[0];var proto=Dressing.Sheet.Prototypes.First(p=>p.Realm==RealmId.Cheongrim&&p.Lods.Length>1&&p.Lods[1].Parts.Any(v=>v.Mesh==near.Mesh));
                var local=proto.Lods[1].Parts.First(p=>p.Mesh==near.Mesh).Local.inverse;Bounds b=default;var keep=new List<int>();
                for(int i=0;i<near.Matrices.Length;i++)
                {
                    var origin=(Vector3)(near.Matrices[i]*local).GetColumn(3);Vector3 ground;
                    var inn=GameObject.Find("Playtest_OwnedAssets").transform.Find("Geumpyo_ThatchedInn");var relative=inn.InverseTransformPoint(origin);
                    if(Mathf.Abs(relative.x)<12.5f&&Mathf.Abs(relative.z)<7.5f&&Mathf.Abs(relative.y)<12)continue;
                    try { ground=Ground(origin); } catch { continue; }
                    float dy=ground.y-origin.y-.08f;keep.Add(i);
                    largest=Mathf.Max(largest,Mathf.Abs(dy));if(Mathf.Abs(dy)>.2f)adjusted++;
                    foreach(var part in packet.Near)part.Matrices[i]=Matrix4x4.Translate(Vector3.up*dy)*part.Matrices[i];
                    foreach(var part in packet.Far)part.Matrices[i]=Matrix4x4.Translate(Vector3.up*dy)*part.Matrices[i];
                    ground.y-=.08f;if(keep.Count==1)b=new Bounds(ground,Vector3.zero);else b.Encapsulate(ground);
                }
                foreach(var part in packet.Near)part.Matrices=keep.Select(i=>part.Matrices[i]).ToArray();
                foreach(var part in packet.Far)part.Matrices=keep.Select(i=>part.Matrices[i]).ToArray();
                packet.Count=keep.Count;
                b.Expand(new Vector3(45,48,45));packet.Bounds=b;
            }
            EditorUtility.SetDirty(foliage);EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            File.WriteAllText(Output+"/grounding.txt","Actual scene terrain grounding; adjusted >0.2m="+adjusted+"; maximum correction="+largest+"m.\n");
            return "Grounded "+adjusted+" instances; maximum="+largest;
        }
        static string FoliageMaterials()
        {
            var foliage=GameObject.Find(Root).GetComponent<EarlyRegionFoliage>();DevSceneKit.EnsureFolder(Folder+"/Materials");
            foreach(var packet in foliage.Packets)foreach(var part in packet.Near.Concat(packet.Far))
            {
                var src=part.Material;
                string path=Folder+"/Materials/"+src.name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(mat==null){mat=new Material(src);mat.SetFloat("_FadeInStart",-1);mat.SetFloat("_FadeInEnd",0);mat.SetFloat("_FadeOutStart",packet.Distance-100);mat.SetFloat("_FadeOutEnd",packet.Distance);mat.enableInstancing=true;AssetDatabase.CreateAsset(mat,path);}
                if(part.Mesh.GetIndexCount(part.Submesh)/3==2){mat.shader=Shader.Find("Oheangbu/EarlyRegionFoliageCard");mat.SetFloat("_Far",packet.Distance);EditorUtility.SetDirty(mat);}
                part.Material=mat;
            }
            EditorUtility.SetDirty(foliage);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Independent supplementary material fade ranges saved; source materials preserved.";
        }
        static string CardSize()
        {
            var f=GameObject.Find(Root).GetComponent<EarlyRegionFoliage>();
            foreach(var packet in f.Packets.Where(p=>p.Distance>400))foreach(var part in packet.Far)
            {
                if(part.Mesh.name.StartsWith("EarlyCard"))continue;
                var proto=part.Material.name.Contains("Pinus")?Proto("SM_PinusDensiflora_Spring_2"):Proto("SM_UlmusDavidiana_Summer_2");
                float span=Mathf.Max(proto.Size.x,Mathf.Max(proto.Size.y,proto.Size.z))*1.08f;string path=Folder+"/Meshes/EarlyCard_"+proto.Id+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null){mesh=new Mesh{name="EarlyCard_"+proto.Id};mesh.vertices=new[]{new Vector3(-span/2,0,0),new Vector3(span/2,0,0),new Vector3(-span/2,span,0),new Vector3(span/2,span,0)};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,2,1,1,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);}
                for(int i=0;i<part.Matrices.Length;i++)part.Matrices[i]=part.Matrices[i]*Matrix4x4.Scale(new Vector3(1/span,1/span,1));part.Mesh=mesh;
            }
            EditorUtility.SetDirty(f);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Baked owned card extent; instance transforms retain only placement scale.";
        }
        static string Validate()
        {
            var root=GameObject.Find(Root);if(root==null)throw new Exception("Missing art root");
            var foliage=root.GetComponent<EarlyRegionFoliage>();var rs=root.GetComponentsInChildren<MeshRenderer>();
            long triangles=root.GetComponentsInChildren<MeshFilter>().Sum(m=>(long)m.sharedMesh.triangles.Length/3);
            bool preserved=EditorJsonUtility.ToJson(Session.Content,true)==File.ReadAllText(Output+"/content_expected.json");
            int missing=rs.Count(r=>r.sharedMaterial==null||r.sharedMaterial.shader==null||!r.sharedMaterial.shader.isSupported);
            long far=foliage.Packets.Sum(p=>p.Far.Sum(part=>(long)part.Mesh.GetIndexCount(part.Submesh)/3*part.Matrices.Length));
            File.WriteAllLines(Output+"/foliage_geometry.txt",foliage.Packets.SelectMany(p=>p.Far).Select(p=>p.Mesh.name+" / "+p.Material.name+" triangles="+p.Mesh.GetIndexCount(p.Submesh)/3+" billboard="+(p.Material.HasProperty("_Billboard")?p.Material.GetFloat("_Billboard"):0)).Distinct());
            var result="contentPreserved="+preserved+"\nstaticRenderers="+rs.Length+"\nstaticTriangles="+triangles+"\nfoliage="+foliage.Packets.Sum(p=>p.Count)+"\npackets="+foliage.Packets.Length+"\nfarTriangles="+far+"\nmissingMaterials="+missing+"\nnewColliders="+root.GetComponentsInChildren<Collider>().Length+"\ncommit="+Prologue.PrologueAudit.CommitRatio()+"\nperformance=UNVERIFIED (new draw path requires live movement measurement)\nvisual=USER_REVIEW\n";
            Physics.SyncTransforms();var checks=new List<string>();
            var parts=foliage.Packets.SelectMany(p=>p.Near.Concat(p.Far)).ToArray();
            checks.Add((parts.All(p=>p.Material!=null&&p.Material.enableInstancing&&!ShaderUtil.ShaderHasError(p.Material.shader)&&p.Matrices.Length<=1023)?"PASS":"FAIL")+" foliage shaders, instancing and <=1023 packet limit");
            checks.Add((Session.TrySafeFeet(Session.Content.InnCheckpointFeet,out _)?"PASS":"FAIL")+" inn checkpoint capsule clearance");
            foreach(var point in Session.Content.Points)
            {
                bool reachable=false;for(int i=0;i<16;i++){float a=i*Mathf.PI/8;var p=point.Position+new Vector3(Mathf.Cos(a)*1.6f,0,Mathf.Sin(a)*1.6f);if(Session.TrySafeFeet(p,out var feet)&&!Physics.Linecast(feet+Vector3.up*1.3f,point.Position+Vector3.up*.8f,1))reachable=true;}
                checks.Add((reachable?"PASS ":"FAIL ")+"interaction approach "+point.Id);
            }
            File.WriteAllLines(Output+"/technical_checks.txt",checks);
            var placements=new List<string>{"packet\tprototype\tx\ty\tz"};int packetIndex=0;
            foreach(var packet in foliage.Packets)
            {
                var first=packet.Near[0];var proto=Dressing.Sheet.Prototypes.First(p=>p.Realm==RealmId.Cheongrim&&p.Lods.Length>1&&p.Lods[1].Parts.Any(v=>v.Mesh==first.Mesh));
                var inverse=proto.Lods[1].Parts.First(p=>p.Mesh==first.Mesh).Local.inverse;
                foreach(var m in first.Matrices){var v=(m*inverse).GetColumn(3);placements.Add(packetIndex+"\t"+proto.Id+"\t"+v.x+"\t"+v.y+"\t"+v.z);}packetIndex++;
            }
            File.WriteAllLines(Output+"/foliage_final.tsv",placements);
            File.WriteAllText(Output+"/validation.txt",result);return result;
        }
    }
}
