using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Opt-in local addition. Survey never changes terrain, navigation, content or scene assets.
    public static class DemoGukRevisitAuthoring
    {
        public const string RootName = "Demo_GukRevisit";
        public const string StoneSource = "Assets/HwaseongForteressGate/Prefabs/SM_Floor_001.prefab";
        public const string FoundationSource = "Assets/SeyeonjeongPavilion/Prefabs/SM_BaseStone.prefab";
        public const string RewardSource = "Assets/KoreanTraditionalFestival/Prefabs/SM_JolongtaegiBag.prefab";
        const float Rise = 2.2f;
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/Chapter3"));
        static WorldMacroPlaytestSession Session => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        [Serializable] sealed class Check { public string name, status, detail; }
        [Serializable] sealed class Candidate
        {
            public Vector3 proposedXZ, measuredBase, measuredExit;
            public float yaw, terrainSpread, maxSlope, nearestRoadEdge, score;
            public bool suitable; public string rejection;
        }
        [Serializable] sealed class Report
        {
            public string status, scope, scene, stoneSource = StoneSource, rewardSource = RewardSource;
            public Vector3 existingLoggingPoint, oldReservedCliff = new Vector3(390,175,670), oldReservedTower = new Vector3(770,145,935);
            public string reservationNotice = "Old AreaSheet 5m gate coordinates are reservations, absent from this campaign scene. proposedXZ.y is deliberately 0; measuredBase/Exit use actual terrain colliders.";
            public Candidate selected; public Candidate[] candidates; public Check[] checks;
            public Vector3 actualLiftPad, actualUpperSurface, actualDescentExit;
            public float actualFrontHeight; public int terrainObjectsBefore, terrainObjectsAfter;
        }
        public static string Execute(string command)
        {
            RequireEdit(); Directory.CreateDirectory(Output);
            if (command == "survey") return Save("guk_revisit_survey.json", Survey());
            if (command == "apply") return Apply();
            if (command == "audit") return Save("guk_revisit_audit.json", Audit(FindOwned()));
            throw new ArgumentException("Expected survey, apply or audit.");
        }
        static void RequireEdit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying || Session == null ||
                Session.gameObject.scene.path != DemoFoundationAuthoring.Scene ||
                AssetDatabase.GetAssetPath(Session.Content) != DemoFoundationAuthoring.Folder + "/Content.asset")
                throw new InvalidOperationException("Dedicated W_Demo_Campaign Edit scene and isolated demo content required.");
        }
        static string Save(string file, Report report)
        { string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(Output, file), json); return json; }
        static DemoGukRevisitSite FindOwned() => Object.FindObjectsByType<DemoGukRevisitSite>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .SingleOrDefault(s => s.gameObject.scene == Session.gameObject.scene && s.name == RootName);
        static bool Terrain(Collider c) => c != null && c.name.StartsWith("Terrain_", StringComparison.Ordinal);
        static bool Owned(Collider c) => c.GetComponentInParent<DemoGukRevisitSite>() != null;
        static bool Ground(Vector3 p, out RaycastHit hit)
        {
            hit = default;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x,2200,p.z),Vector3.down,4400,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
                if (h.collider.gameObject.scene == Session.gameObject.scene && Terrain(h.collider) && h.normal.y > .1f) { hit=h; return true; }
            return false;
        }
        static Vector3 Local(Candidate c, float x, float z) => c.measuredBase + Quaternion.Euler(0,c.yaw,0) * new Vector3(x,0,z);
        static float Road(Vector3 p)
        {
            float result=float.PositiveInfinity;
            foreach (var route in WorldMacroBuilder.Sheet.Routes)
                for(int i=1;i<route.Points.Length;i++)
                    result=Mathf.Min(result,WorldMacroTerrain.SegmentDistance(p.x,p.z,route.Points[i-1],route.Points[i],out _)-route.Width*.5f);
            return result;
        }
        static bool Clear(Vector3 p, Vector3 half, Quaternion rotation)
        {
            return !Physics.OverlapBox(p,half,rotation,~0,QueryTriggerInteraction.Ignore).Any(c =>
                c.gameObject.scene == Session.gameObject.scene && !Terrain(c) && !Owned(c));
        }
        static Candidate Examine(Vector3 proposed, float yaw, Vector3 logging)
        {
            var c=new Candidate{proposedXZ=proposed,yaw=yaw,nearestRoadEdge=float.PositiveInfinity};
            if(!Ground(proposed + Quaternion.Euler(0,yaw,0)*new Vector3(0,0,-2.55f),out var pad))
            { c.rejection="No terrain under lift pad";return c; }
            // The centre's Y is the measured pad floor, not the old Content reservation's elevation.
            c.measuredBase=new Vector3(proposed.x,pad.point.y,proposed.z);
            float low=float.PositiveInfinity, high=float.NegativeInfinity;
            var q=Quaternion.Euler(0,yaw,0);
            for(int x=-3;x<=11;x+=2) for(int z=-4;z<=4;z+=2)
            {
                Vector3 p=Local(c,x,z);
                if(!Ground(p,out var h)){c.rejection="Missing footprint ground";return c;}
                low=Mathf.Min(low,h.point.y);high=Mathf.Max(high,h.point.y);
                c.maxSlope=Mathf.Max(c.maxSlope,Vector3.Angle(h.normal,Vector3.up));
                c.nearestRoadEdge=Mathf.Min(c.nearestRoadEdge,Road(p));
            }
            c.terrainSpread=high-low;
            if(c.terrainSpread>.65f || c.maxSlope>15){c.rejection="Footprint too uneven for a bounded stone addition";return c;}
            if(c.nearestRoadEdge<2){c.rejection="Would intrude on existing route";return c;}
            // A 3x3m ground apron includes deck corners, capsule headroom and room to recast.
            for(int x=-1;x<=1;x++) for(int z=-1;z<=1;z++)
            {
                Vector3 p=Local(c,x*.65f,-2.55f+z*.45f);
                if(!Ground(p,out var h)||Mathf.Abs(h.point.y-pad.point.y)>.075f)
                {c.rejection="Lift pad corner support not level";return c;}
            }
            if(!Ground(Local(c,10.2f,.75f),out var exit)){c.rejection="No descent exit ground";return c;}
            c.measuredExit=exit.point;
            if(!Clear(c.measuredBase+q*new Vector3(4,2.7f,0),new Vector3(7.5f,2.6f,4.5f),q))
            {c.rejection="Existing structure, vegetation, actor or prop occupies footprint/headroom";return c;}
            // Accessible ground approach is checked in 1m increments, independently of road reservation.
            Vector3 approach=Local(c,0,-4);bool connected=false;
            // The inquiry marker itself may be occupied by its NPC/prop. Start in one of its four 4m approach margins.
            foreach(var margin in new[]{Vector3.forward,Vector3.back,Vector3.left,Vector3.right})
            {
                Vector3 start=logging+margin*4;
                int n=Mathf.CeilToInt(Vector2.Distance(new Vector2(start.x,start.z),new Vector2(approach.x,approach.z)));
                bool path=true;
                for(int i=0;i<=n;i++)
                {
                    Vector3 p=Vector3.Lerp(start,approach,n==0?0:(float)i/n);
                    if(!Ground(p,out var h)||Vector3.Angle(h.normal,Vector3.up)>28 ||
                        !Clear(h.point+Vector3.up*1.1f,new Vector3(.36f,.85f,.36f),Quaternion.identity)) {path=false;break;}
                }
                if(path){connected=true;break;}
            }
            if(!connected){c.rejection="No clear ground approach from logging point margins";return c;}
            c.suitable=true;c.score=c.terrainSpread*20+c.maxSlope+Vector2.Distance(new Vector2(logging.x,logging.z),new Vector2(proposed.x,proposed.z))*.2f;
            return c;
        }
        static Report Survey()
        {
            Physics.SyncTransforms();
            var logging=Session.Content.Points.SingleOrDefault(p=>p.Id=="logging_inquiry");
            if(logging==null)throw new InvalidOperationException("Existing logging inquiry is required as the current scene anchor.");
            var candidates=new List<Candidate>();
            for(int x=-6;x<=6;x++) for(int z=-6;z<=6;z++)
            {
                var offset=new Vector3(x*8,0,z*8); if(offset.magnitude<20 || offset.magnitude>56)continue;
                foreach(float yaw in new[]{0f,90f,180f,270f})
                    candidates.Add(Examine(new Vector3(logging.Position.x+offset.x,0,logging.Position.z+offset.z),yaw,logging.Position));
            }
            var selected=candidates.Where(c=>c.suitable).OrderBy(c=>c.score).FirstOrDefault();
            return new Report{status=selected==null?"NO_SAFE_CANDIDATE":"SURVEY_ONLY",scene=Session.gameObject.scene.path,
                scope="Read-only local terrain/collider/route survey, within 56m of current logging point. No placement or reward proof claimed.",
                existingLoggingPoint=logging.Position,selected=selected,candidates=candidates.ToArray()};
        }
        static MeshFilter SourceMesh(string path)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab==null)throw new InvalidOperationException("Existing asset missing: "+path);
            var mesh=prefab.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m=>m.name.EndsWith("LOD0",StringComparison.Ordinal)) ?? prefab.GetComponentInChildren<MeshFilter>(true);
            if(mesh==null || mesh.sharedMesh==null || mesh.GetComponent<MeshRenderer>()==null)throw new InvalidOperationException("Existing mesh/material missing: "+path);
            return mesh;
        }
        static Transform Child(Transform parent,string name,Vector3 position)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.position=position;return t;}
        static MeshCollider Stone(Transform parent,string name,Vector3 centre,Vector3 size,Quaternion rotation,string sourcePath=null)
        {
            var source=SourceMesh(sourcePath??StoneSource);var b=source.sharedMesh.bounds;
            if(b.size.x<=0 || b.size.y<=0 || b.size.z<=0)throw new InvalidOperationException("Stone mesh bounds invalid");
            var t=Child(parent,name,centre);t.rotation=rotation;
            t.localScale=new Vector3(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z);
            t.position=centre-rotation*Vector3.Scale(b.center,t.localScale);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh=source.sharedMesh;
            t.gameObject.AddComponent<MeshRenderer>().sharedMaterials=source.GetComponent<MeshRenderer>().sharedMaterials;
            var collider=t.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=source.sharedMesh;
            return collider;
        }
        static int TerrainCount() => Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).Count(Terrain);
        static string Apply()
        {
            var survey=Survey();Save("guk_revisit_survey.json",survey);
            if(survey.selected==null)throw new InvalidOperationException("No verified safe local candidate; inspect guk_revisit_survey.json. Nothing authored.");
            SourceMesh(StoneSource);var bag=AssetDatabase.LoadAssetAtPath<GameObject>(RewardSource);
            if(bag==null)throw new InvalidOperationException("Existing reward bag missing");
            var old=FindOwned();int before=TerrainCount();var c=survey.selected;var q=Quaternion.Euler(0,c.yaw,0);
            var root=new GameObject(RootName+"_Pending");var site=root.AddComponent<DemoGukRevisitSite>();
            try
            {
                float bottom=c.measuredBase.y-.8f, top=c.measuredBase.y+Rise;
                Stone(root.transform,"ExistingStone_Foundation",new Vector3(c.measuredBase.x,(bottom+top-.1f)*.5f,c.measuredBase.z),new Vector3(4,top-bottom-.1f,4),q,FoundationSource);
                // Preserve the floor's shallow relief instead of stretching it through the full cliff height.
                Stone(root.transform,"ExistingStone_Upper",new Vector3(c.measuredBase.x,top-.15f,c.measuredBase.z),new Vector3(4,.3f,4),q);
                Vector3 a=Local(c,1.75f,.75f);a.y=top;
                Vector3 b=Local(c,10.2f,.75f);b.y=c.measuredExit.y+.025f;
                float length=Vector3.Distance(a,b),angle=Mathf.Atan2(a.y-b.y,8.45f)*Mathf.Rad2Deg;
                Quaternion ramp=q*Quaternion.Euler(0,0,-angle);
                Stone(root.transform,"ExistingStone_Descent",(a+b)*.5f-ramp*Vector3.up*.15f,new Vector3(length,.3f,1.8f),ramp);
                site.LiftPad=Child(root.transform,"LiftPad",Local(c,0,-2.55f));
                site.UpperSurface=Child(root.transform,"UpperSurface",Local(c,-.65f,.4f)+Vector3.up*Rise);
                site.DescentExit=Child(root.transform,"DescentExit",c.measuredExit);
                Physics.SyncTransforms();
                // Every walkable height is verified against the actual reused mesh collider. No invisible box top.
                var geometry=Audit(site,false); if(geometry.status!="PASS")
                {Save("guk_revisit_rejected_geometry.json",geometry);throw new InvalidOperationException("Existing stone surface or descent failed actual collider checks; pending addition removed.");}
                var copy=(GameObject)PrefabUtility.InstantiatePrefab(bag,root.transform);
                copy.name="ExistingRewardBag";
                foreach(var collider in copy.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                var renderers=copy.GetComponentsInChildren<Renderer>(true);
                if(renderers.Length==0)throw new InvalidOperationException("Reward source has no renderer");
                Bounds bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
                copy.transform.localScale*=.45f/Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
                bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
                copy.transform.position+=site.UpperSurface.position+q*new Vector3(0,0,.55f)-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                if(old!=null)Undo.DestroyObjectImmediate(old.gameObject);
                root.name=RootName;Undo.RegisterCreatedObjectUndo(root,"Author bounded Guk revisit site");
                var content=Session.Content;Undo.RecordObject(content,"Register Guk revisit points");
                content.Points=content.Points.Where(p=>p.Id!=DemoGukRevisitSite.Id && p.Id!=DemoGukRevisitSite.PreviewId).Concat(new[]{
                    new PrologueContentSO.Point{Id=DemoGukRevisitSite.Id,Kind=PrologueInteractionKind.Evidence,Position=site.UpperSurface.position+Vector3.up*.06f,Radius=1.25f,Prompt="높은 바위 위의 보따리 살피기",Text="국으로 올라온 높은 바위에서 남겨진 보따리를 찾았다."},
                    new PrologueContentSO.Point{Id=DemoGukRevisitSite.PreviewId,Kind=PrologueInteractionKind.Preview,Position=site.LiftPad.position+q*Vector3.back*1.1f,Radius=1.6f,Prompt="높은 바위 살피기",Text="바위 위에 작은 보따리가 보인다. 받침을 올리는 국이라면 닿을 수 있을 듯하다."}
                }).ToArray();
                EditorUtility.SetDirty(content);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
                var report=Audit(site);report.selected=c;report.terrainObjectsBefore=before;report.terrainObjectsAfter=TerrainCount();
                return Save("guk_revisit_audit.json",report);
            }
            catch { if(root!=null && root.name.EndsWith("_Pending",StringComparison.Ordinal))Object.DestroyImmediate(root);throw; }
        }
        static Report Audit(DemoGukRevisitSite site,bool content=true)
        {
            var checks=new List<Check>();void Check(string name,bool pass,string detail)=>checks.Add(new Check{name=name,status=pass?"PASS":"FAIL",detail=detail});
            Check("authored-site",site!=null,"One owned scene site is required.");
            if(site==null)return new Report{status="FAIL",checks=checks.ToArray()};
            Check("markers",site.LiftPad!=null&&site.UpperSurface!=null&&site.DescentExit!=null,"Actual support, upper surface and descent endpoint references.");
            if(site.LiftPad==null||site.UpperSurface==null||site.DescentExit==null)return new Report{status="FAIL",checks=checks.ToArray()};
            Physics.SyncTransforms();var slab=site.transform.Find("ExistingStone_Upper")?.GetComponent<MeshCollider>();
            var ramp=site.transform.Find("ExistingStone_Descent")?.GetComponent<MeshCollider>();
            Check("actual-mesh-colliders",slab!=null&&ramp!=null,"Both upper stone and descent use the existing visible source mesh.");
            if(slab==null||ramp==null)return new Report{status="FAIL",checks=checks.ToArray()};
            Quaternion q=slab.transform.rotation;Vector3 centre=site.LiftPad.position+q*new Vector3(0,0,2.55f);
            bool Surface(Collider collider,Vector3 p,out RaycastHit hit)=>collider.Raycast(new Ray(p+Vector3.up*6,Vector3.down),out hit,12);
            bool upper=true;float min=float.PositiveInfinity,max=float.NegativeInfinity;
            // 3x3m clear walking/recasting area, sampled on the real visible stone surface.
            for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++)
            {
                Vector3 p=centre+q*new Vector3(x*.5f,0,z*.5f);
                if(!Surface(slab,p,out var hit)){upper=false;continue;}
                min=Mathf.Min(min,hit.point.y);max=Mathf.Max(max,hit.point.y);
                upper &= Vector3.Angle(hit.normal,Vector3.up)<=8 && Mathf.Abs(hit.point.y-(centre.y+Rise))<=.08f;
            }
            Check("visible-collider-upper-3m-square",upper,"Actual mesh surface min/max: "+min+" / "+max+"; no box substitute.");
            bool markerHit=Surface(slab,site.UpperSurface.position,out var marker);
            float height=markerHit?marker.point.y-site.LiftPad.position.y:0;
            Check("front-height",height>=2.1f&&height<=2.3f,height.ToString("F3")+"m above measured lift floor; no front stairs.");
            Check("marker-surface-agreement",markerHit&&Vector3.Distance(marker.point,site.UpperSurface.position)<=.08f,"Authored marker must coincide with the measured surface within 0.08m.");
            bool descent=true;float previous=centre.y+Rise;
            for(int i=0;i<=34;i++)
            {
                float x=Mathf.Lerp(1.85f,10.12f,i/34f);
                for(int side=-1;side<=1;side++)
                {
                    Vector3 p=centre+q*new Vector3(x,0,.75f+side*.5f);
                    if(!Surface(ramp,p,out var hit)){descent=false;continue;}
                    descent &= Vector3.Angle(hit.normal,Vector3.up)<=28 && hit.point.y<=previous+.12f && previous-hit.point.y<.24f;
                    if(side==1)previous=hit.point.y;
                }
            }
            Check("descent-three-lanes",descent,"1m usable width, 0.25m samples, <=28 degree surface, no >0.24m abrupt drop.");
            Check("descent-ground-exit",Surface(ramp,site.DescentExit.position,out var end)&&Mathf.Abs(end.point.y-site.DescentExit.position.y)<=.12f,"Ramp mesh must meet the actual exit terrain within 0.12m.");
            Check("same-scene",site.gameObject.scene==Session.gameObject.scene,"No external site registration.");
            if(content)
            {
                var points=Session.Content.Points.Where(p=>p.Id==DemoGukRevisitSite.Id).ToArray();
                Check("owned-content-point",points.Length==1&&Vector3.Distance(points[0].Position,site.UpperSurface.position+Vector3.up*.06f)<.02f&&points[0].Kind==PrologueInteractionKind.Evidence,"Exact scene surface matches the unique demo Content point; Session owns rewards.");
            }
            return new Report{status=checks.All(c=>c.status=="PASS")?"PASS":"FAIL",scene=Session.gameObject.scene.path,
                scope="Geometry/asset/Content audit only. Reward transaction and a real CharacterController ascent/descent still require runtime checks; stage activation is not changed.",
                checks=checks.ToArray(),actualLiftPad=site.LiftPad.position,actualUpperSurface=site.UpperSurface.position,actualDescentExit=site.DescentExit.position,actualFrontHeight=height};
        }
    }
}
