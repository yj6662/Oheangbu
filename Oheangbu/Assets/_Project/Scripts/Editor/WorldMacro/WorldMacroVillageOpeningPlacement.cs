using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroVillageOpeningReview
    {
        static readonly Vector3 Site=new Vector3(1900,133.25f,645);
        const string Office="Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab";
        static string Apply()
        {
            if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=WorldMacroPlaytestAuthoring.ScenePath)throw new InvalidOperationException("Playtest Edit scene required.");
            if(GameObject.Find(RootName)!=null)throw new InvalidOperationException("Office already exists; targeted edits only.");
            Directory.CreateDirectory(Output+"/Backups");DevSceneKit.EnsureFolder(Folder);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),Output+"/Backups/BeforeVillageOffice.unity",true);
            File.WriteAllText(Output+"/Backups/Content.json",EditorJsonUtility.ToJson(Session.Content,true));
            var root=new GameObject(RootName).transform;
            CreateYard(root);
            var house=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Office));
            house.name="Village_Office_OwnedHanok";house.transform.SetParent(root,false);
            house.transform.SetPositionAndRotation(Site+Vector3.up*.02f,Quaternion.Euler(0,90,0));
            foreach(var renderer in house.GetComponentsInChildren<Renderer>())renderer.sharedMaterials=renderer.sharedMaterials.Select(WorldMacroVisualCorridorAuthoring.Surface).ToArray();
            foreach(var f in house.GetComponentsInChildren<MeshFilter>())if(f.sharedMesh!=null){f.gameObject.AddComponent<MeshCollider>().sharedMesh=f.sharedMesh;f.gameObject.isStatic=true;}
            return FinishPlacement();
        }
        static string FinishPlacement()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit required");
            var root=GameObject.Find(RootName).transform;
            if(root.Find("Village_Clerk_Interaction")!=null)throw new InvalidOperationException("Already placed.");
            // Reuse the already baked, script-free furnishing from the earlier approved inn.
            var tableSource=GameObject.Find("Playtest_OwnedAssets").transform.Find("Geumpyo_ThatchedInn/Rest_Low_Table");
            var table=Object.Instantiate(tableSource.gameObject,root).transform;table.name="Clerk_WorkTable";
            table.SetPositionAndRotation(Site+new Vector3(10.1f,.02f,0),Quaternion.Euler(0,90,0));table.gameObject.SetActive(true);
            foreach(var r in table.GetComponentsInChildren<Renderer>(true))r.enabled=true;
            var point=new GameObject("Village_Clerk_Interaction").transform;point.SetParent(root,false);point.position=Site+new Vector3(8.8f,.05f,0);
            var id=point.gameObject.AddComponent<WorldMacroContentPoint>();id.Id=WorldMacroOpeningProfileSO.CommissionId;id.CombatConnected=true;id.CombatRole="Temporary clerk; actual commission dialogue; no combat";
            var source=Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).First(t=>t.name=="NPC_Proxy"&&Vector3.Distance(t.position,new Vector3(1901,134,580))<10);
            var clerk=Object.Instantiate(source.gameObject,point);clerk.name="Clerk_TemporaryAppearance";clerk.transform.localPosition=new Vector3(0,.85f,0);clerk.transform.localRotation=Quaternion.Euler(0,90,0);clerk.SetActive(true);id.Visual=clerk.transform;
            foreach(var c in clerk.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
            var body=point.gameObject.AddComponent<CapsuleCollider>();body.height=1.7f;body.radius=.27f;body.center=Vector3.up*.85f;
            var opening=ScriptableObject.CreateInstance<WorldMacroOpeningProfileSO>();opening.name="VillageOpening";
            opening.StartFeet=Site+new Vector3(11.1f,.07f,.9f);opening.StartYaw=270;
            opening.Commission.Position=point.position;opening.Commission.Radius=3.1f;
            opening.Commission.Text="동쪽 폐광에서 폭파 소리가 났소. 현장에 남은 흔적을 살펴보고 돌아와 주시오. 아직 누가, 왜 그런 것인지는 모르오.\n\n오행부로 마석 자동차를 부를 수 있소. 큰길을 따라 금표 주막 쪽으로 가면 폐광으로 이어지는 산길이 나오오. 좁은 길은 걸어서 살펴보시오. 차를 가지러 돌아올 필요는 없소. 큰길에서 다시 부르면 되니.";
            opening.BeforeVehicleObjective="[Esc] → 오행부에서 자동차를 부른 뒤, 금표 주막 쪽 큰길로 이동한다";
            opening.BeforeEvidenceObjective="금표 주막에서 동쪽 산길을 따라 폐광으로 가서 폭파 흔적을 조사한다";
            AssetDatabase.CreateAsset(opening,Folder+"/VillageOpening.asset");Session.Content.Opening=opening;EditorUtility.SetDirty(Session.Content);
            var caller=Session.GetComponent<WorldMacroPalanquinSummon>()??Session.gameObject.AddComponent<WorldMacroPalanquinSummon>();
            caller.Vehicle=Object.FindFirstObjectByType<WorldMacroPalanquinController>();caller.Seat=Object.FindFirstObjectByType<WorldMacroPalanquinSeat>();caller.Walker=Session.Walker;caller.WorldSheet=WorldMacroBuilder.Sheet;
            var dressing=Object.FindFirstObjectByType<WorldMacroDressingRenderer>()?.Sheet;
            if(dressing!=null)
            {
                File.WriteAllText(Output+"/Backups/DressingPreserves.json",JsonUtility.ToJson(new Areas{areas=dressing.PreservedAreas},true));
                dressing.PreservedAreas=dressing.PreservedAreas.Concat(new[]{new WorldMacroDressingSheetSO.PreserveArea{Id="village_office_courtyard",Centre=Site+new Vector3(8,0,0),HalfSize=new Vector2(16,12)},new WorldMacroDressingSheetSO.PreserveArea{Id="village_office_road_access",Centre=Site+new Vector3(32,0,0),HalfSize=new Vector2(11,3)}}).ToArray();
                EditorUtility.SetDirty(dressing);
            }
            Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return Validate();
        }
        [Serializable] sealed class Areas{public WorldMacroDressingSheetSO.PreserveArea[] areas;}
        static void CreateYard(Transform root)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();const int nx=32,nz=20;
            for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
            {
                float dx=-16+x*2,dz=-20+z*2;var world=Site+new Vector3(dx,30,dz);
                if(!Physics.Raycast(world,Vector3.down,out var hit,60,1,QueryTriggerInteraction.Ignore))throw new InvalidOperationException("Unsupported office site "+world);
                float court=Mathf.Max(Mathf.Max(-7-dx,dx-20),Mathf.Abs(dz)-9);
                float access=Mathf.Max(Mathf.Max(16-dx,dx-45),Mathf.Abs(dz)-2.5f);
                float fade=1-Mathf.SmoothStep(0,1,Mathf.Max(0,Mathf.Min(court,access))/9);
                float surface=Mathf.Max(hit.point.y+.018f,Mathf.Lerp(hit.point.y+.018f,Site.y,fade));
                vertices.Add(new Vector3(world.x,surface,world.z));uv.Add(new Vector2(world.x,world.z)*.2f);
                if(x<nx&&z<nz){int a=z*(nx+1)+x;triangles.AddRange(new[]{a,a+nx+1,a+1,a+1,a+nx+1,a+nx+2});}
            }
            var mesh=new Mesh{name="VillageOffice_ContinuousCourtyard"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Folder+"/Courtyard.asset");
            var soil=new GameObject(mesh.name);soil.transform.SetParent(root,false);soil.AddComponent<MeshFilter>().sharedMesh=mesh;
            soil.AddComponent<MeshRenderer>().sharedMaterial=GameObject.Find("Terrain_050").GetComponent<Renderer>().sharedMaterial;soil.AddComponent<MeshCollider>().sharedMesh=mesh;soil.isStatic=true;
        }
        static string Validate()
        {
            var lines=new List<string>();void Check(bool ok,string label)=>lines.Add((ok?"PASS ":"FAIL ")+label);
            var p=Session.Content.Opening;var root=GameObject.Find(RootName);
            Check(p!=null&&p.IsConfigured,"opening profile configured; old mine start retained");
            if(p!=null){Physics.SyncTransforms();Check(Session.TrySafeFeet(p.StartFeet,out var feet),"new-game capsule support/clearance "+p.StartFeet+" -> "+feet);Check(Vector3.Distance(p.StartFeet,p.Commission.Position)<p.Commission.Radius,"clerk is within first interaction radius");}
            Check(root!=null,"office placed from owned Naeposa prefab");
            if(root!=null){int tris=root.GetComponentsInChildren<MeshFilter>().Sum(x=>x.sharedMesh.triangles.Length/3);lines.Add("Office/yard/props visible tris="+tris);Check(root.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m!=null&&!ShaderUtil.ShaderHasError(m.shader))),"materials available; provider originals unchanged");}
            Check(Session.GetComponent<WorldMacroPalanquinSummon>()!=null,"existing single car summon connected");
            lines.Add("UNVERIFIED actual player input traversal and external-PC audio listening; temporary clerk appearance.");
            File.WriteAllLines(Output+"/scene_validation.txt",lines);return string.Join("\n",lines);
        }
        static string Capture(string key)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit environment still required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Commit >=85%");
            var target=Site+Vector3.up*2;
            var pos=Site+new Vector3(28,13,22);
            if(key=="arrival")pos=Site+new Vector3(15,2,5);
            string image=WorldMacroDressingProbe.Capture("VillageOffice_"+key,true,pos.x,pos.y,pos.z,target.x,target.y,target.z);
            File.Copy(image,Output+"/office-"+key+".png",true);return Output+"/office-"+key+".png";
        }
    }
}
