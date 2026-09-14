using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMacroAudit
    {
        [Serializable]public class CheckResult{public string name,status,detail;}
        [Serializable]public class RouteMeasure{public string id,from,to;public bool carriage;public float lengthM,widthM,maxGradeDeg,p95GradeDeg,maxBridgeApproachGradeDeg,minTurnRadiusM,travelSecondsAtConstantSpeed;public int outsideSamples,waterSamplesWithoutBridge;}
        [Serializable]public class RiverMeasure{public string id,parent;public float lengthM,maxRiseM,confluenceGapM,confluenceLevelErrorM,minBedDepthM;public int dryCenterSamples;}
        [Serializable]public class Measurement{public string status="MEASURED_BLOCKOUT_NOT_GAMEPLAY";public CheckResult[] checks;public RouteMeasure[] routes;public RiverMeasure[] rivers;public int terrainChunks,sites;public long terrainTriangles,totalMeshTriangles;public float footprintKm2,boundsKm2,maxSeamHeightErrorM,maxSeamNormalError,systemCommitRatio;public string movement="Route surface samples and constant-speed camera-distance calculation; not vehicle physics or gameplay traversal.";}
        [Serializable]public class CaptureSpec{public string id,file,label,kind;public Vector3 position,target;public float fov=60;}
        [Serializable]class CaptureManifest{public string scope="Screenshots only: macro terrain with placeholder settlements/forests. Ground views are eye-height inspection, not gameplay.";public CaptureSpec[] captures;}
        static readonly List<CheckResult> Checks=new List<CheckResult>();
        static void Check(bool ok,string name,string detail="")=>Checks.Add(new CheckResult{name=name,status=ok?"PASS":"FAIL",detail=detail});
        static float Surface(WorldMacroSheetSO s,Vector3 p)=>WorldMacroTerrain.SurfaceHeight(s,p.x,p.z);
        static float Water(WorldMacroSheetSO s,Vector3 p,out float distance,out float halfWidth)
        {
            distance=float.MaxValue;halfWidth=0;float level=0;
            foreach(var r in s.Rivers)for(int i=1;i<r.Points.Length;i++){float t,d=WorldMacroTerrain.SegmentDistance(p.x,p.z,r.Points[i-1],r.Points[i],out t);if(d<distance){distance=d;level=Mathf.Lerp(r.Points[i-1].y,r.Points[i].y,t);halfWidth=r.Width*.5f;}}
            return level;
        }
        static bool BridgeNear(WorldMacroSheetSO s,Vector3 p)=>s.Sites.Any(v=>v.Kind=="Bridge"&&Vector2.Distance(new Vector2(p.x,p.z),new Vector2(v.Position.x,v.Position.z))<150);
        static float TravelSurface(WorldMacroSheetSO s,Vector3 p)
        {
            float height=Surface(s,p);
            if(BridgeNear(s,p))foreach(var hit in Physics.RaycastAll(new Vector3(p.x,2000,p.z),Vector3.down,4000,1,QueryTriggerInteraction.Ignore))
                if(hit.collider.name=="Bridge_Deck_TEST"||hit.collider.name.StartsWith("Bridge_Apron_TEST"))height=Mathf.Max(height,hit.point.y);
            return height;
        }
        public static string Audit()
        {
            var s=WorldMacroBuilder.Sheet;if(s==null)throw new Exception("Build first");Checks.Clear();
            Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path==WorldMacroBuilder.ScenePath,"isolated macro scene");
            Check(s.Regions.Select(r=>r.Realm).Distinct().Count()==5,"five realms");
            Check(s.Sites.Select(v=>v.Id).Distinct().Count()==s.Sites.Length,"unique site ids");
            Check(s.Sites.All(v=>WorldMacroTerrain.Contains(s,v.Position.x,v.Position.z)),"sites within irregular footprint");
            var m=new Measurement{sites=s.Sites.Length,systemCommitRatio=Prologue.PrologueAudit.CommitRatio()};
            float area=0;for(int i=0,j=s.Outline.Length-1;i<s.Outline.Length;j=i++)area+=s.Outline[j].x*s.Outline[i].y-s.Outline[i].x*s.Outline[j].y;
            m.footprintKm2=Mathf.Abs(area)*.5e-6f;m.boundsKm2=(s.BoundsMax.x-s.BoundsMin.x)*(s.BoundsMax.y-s.BoundsMin.y)*1e-6f;
            Check(m.footprintKm2<m.boundsKm2*.92f,"irregular footprint differs from allocation rectangle",m.footprintKm2.ToString("F2")+" km² / "+m.boundsKm2.ToString("F2")+" km²");
            int missing=0,shaderErrors=0;var seams=new Dictionary<Vector2,Vector3>();var normals=new Dictionary<Vector2,Vector3>();
            foreach(var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)){
                var mesh=filter.sharedMesh;if(mesh==null){missing++;continue;}m.totalMeshTriangles+=mesh.triangles.Length/3;
                var renderer=filter.GetComponent<Renderer>();if(renderer==null){missing++;continue;}foreach(var mat in renderer.sharedMaterials){if(mat==null)missing++;else if(mat.shader==null||ShaderUtil.ShaderHasError(mat.shader))shaderErrors++;}
                bool core=filter.name.StartsWith("Terrain_"),context=filter.name.StartsWith("Context_");
                if(!core&&!context)continue;if(core){m.terrainChunks++;m.terrainTriangles+=mesh.triangles.Length/3;}
                var v=mesh.vertices;var n=mesh.normals;var bounds=mesh.bounds;
                for(int i=0;i<v.Length;i++){
                    var key=new Vector2(v[i].x,v[i].z);if(seams.TryGetValue(key,out var other)){m.maxSeamHeightErrorM=Mathf.Max(m.maxSeamHeightErrorM,Mathf.Abs(other.y-v[i].y));m.maxSeamNormalError=Mathf.Max(m.maxSeamNormalError,Vector3.Distance(normals[key],n[i]));}else{seams[key]=v[i];normals[key]=n[i];}
                }
            }
            Check(missing==0,"mesh and material references",missing.ToString());Check(shaderErrors==0,"shader compile errors",shaderErrors.ToString());
            Check(m.maxSeamHeightErrorM<.001f&&m.maxSeamNormalError<.001f,"global terrain and background shared lattice seams",$"height={m.maxSeamHeightErrorM}, normal={m.maxSeamNormalError}");
            var contextRoot=GameObject.Find("BackgroundContext_RenderOnly_NoGameplay_NoColliders");
            Check(contextRoot!=null&&contextRoot.GetComponentsInChildren<MeshRenderer>().Length>0&&contextRoot.GetComponentsInChildren<Collider>().Length==0,"background continuation render only");
            Physics.SyncTransforms();int misses=0,tests=0;float surfaceError=0;
            var terrainRoot=GameObject.Find("01_GlobalTerrain_IndependentOfRoads").transform;
            foreach(var route in s.Routes)foreach(var p in route.Points.Where((v,i)=>i%4==0)){
                if(!WorldMacroTerrain.Contains(s,p.x,p.z))continue;tests++;
                var hits=Physics.RaycastAll(new Vector3(p.x,2000,p.z),Vector3.down,4000,1,QueryTriggerInteraction.Ignore);bool found=false;
                foreach(var hit in hits)if(hit.collider.transform.IsChildOf(terrainRoot)){found=true;surfaceError=Mathf.Max(surfaceError,Mathf.Abs(hit.point.y-Surface(s,p)));break;}if(!found)misses++;
            }
            Check(misses==0&&surfaceError<.025f,"surface sampler matches actual terrain collider",$"samples={tests}; misses={misses}; max error={surfaceError:F5}m");
            var clone=Object.Instantiate(s);clone.Routes=Array.Empty<WorldMacroSheetSO.RouteSpec>();bool independent=s.Sites.All(p=>Mathf.Abs(Surface(s,p.Position)-Surface(clone,p.Position))<.0001f);Object.DestroyImmediate(clone);
            Check(independent,"terrain independent of route authoring");
            var routes=new List<RouteMeasure>();
            foreach(var r in s.Routes){
                var rm=new RouteMeasure{id=r.Id,from=r.From,to=r.To,carriage=r.Carriage,widthM=r.Width,minTurnRadiusM=100000};var grades=new List<float>();
                bool endpoints=s.FindSite(r.From)!=null&&s.FindSite(r.To)!=null&&r.Points.Length>=2;
                if(endpoints)endpoints=Vector3.Distance(r.Points[0],s.FindSite(r.From).Position)<1f&&Vector3.Distance(r.Points[r.Points.Length-1],s.FindSite(r.To).Position)<1f;
                Check(endpoints,"route endpoints "+r.Id);
                for(int i=1;i<r.Points.Length;i++){
                    var a=r.Points[i-1];var b=r.Points[i];float horizontal=Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));int steps=Mathf.Max(1,Mathf.CeilToInt(horizontal/8));Vector3 prev=a;prev.y=TravelSurface(s,prev);
                    for(int k=1;k<=steps;k++){var p=Vector3.Lerp(a,b,k/(float)steps);p.y=TravelSurface(s,p);float dl=Vector2.Distance(new Vector2(prev.x,prev.z),new Vector2(p.x,p.z));float grade=Mathf.Atan2(Mathf.Abs(p.y-prev.y),Mathf.Max(.001f,dl))*Mathf.Rad2Deg;
                        if(!BridgeNear(s,p))grades.Add(grade);else rm.maxBridgeApproachGradeDeg=Mathf.Max(rm.maxBridgeApproachGradeDeg,grade);rm.lengthM+=Vector3.Distance(prev,p);if(!WorldMacroTerrain.Contains(s,p.x,p.z))rm.outsideSamples++;
                        float water=Water(s,p,out var d,out var half);if(d<half&&p.y<water)rm.waterSamplesWithoutBridge++;prev=p;
                    }
                    if(i+1<r.Points.Length){var c=r.Points[i+1];var ab=new Vector2(b.x-a.x,b.z-a.z);var bc=new Vector2(c.x-b.x,c.z-b.z);var ca=new Vector2(a.x-c.x,a.z-c.z);float cross=Mathf.Abs(ab.x*bc.y-ab.y*bc.x);if(cross>.001f)rm.minTurnRadiusM=Mathf.Min(rm.minTurnRadiusM,ab.magnitude*bc.magnitude*ca.magnitude/(2*cross));}
                }
                grades.Sort();rm.maxGradeDeg=grades.Count>0?grades[grades.Count-1]:0;rm.p95GradeDeg=grades.Count>0?grades[Mathf.Clamp((int)(grades.Count*.95f),0,grades.Count-1)]:0;rm.travelSecondsAtConstantSpeed=rm.lengthM/(r.Carriage?s.CarriageSpeed:s.WalkSpeed);routes.Add(rm);
            }
            m.routes=routes.ToArray();Check(routes.All(r=>r.outsideSamples==0),"route samples within geographic footprint");Check(routes.All(r=>r.waterSamplesWithoutBridge==0),"water crossings supported by actual bridge decks",string.Join(", ",routes.Where(r=>r.waterSamplesWithoutBridge>0).Select(r=>r.id+":"+r.waterSamplesWithoutBridge)));
            var adjacency=new Dictionary<string,List<string>>();foreach(var r in s.Routes){if(!adjacency.ContainsKey(r.From))adjacency[r.From]=new List<string>();if(!adjacency.ContainsKey(r.To))adjacency[r.To]=new List<string>();adjacency[r.From].Add(r.To);adjacency[r.To].Add(r.From);}
            var reached=new HashSet<string>();var queue=new Queue<string>();queue.Enqueue("Mine");while(queue.Count>0){var id=queue.Dequeue();if(!reached.Add(id))continue;if(adjacency.TryGetValue(id,out var ns))foreach(var n in ns)queue.Enqueue(n);}
            Check(new[]{"Inn","Dragon","SouthGate","Hwanggyeong","Jeokro","Cheolong","Hyeongang","OldTemple"}.All(reached.Contains),"reserved world route graph connected",string.Join(",",reached));
            Check(new[]{"Hwanggyeong","Cheongrim","Jeokro","Cheolong","Hyeongang"}.All(reached.Contains),"all five regional anchors connected to capital");
            var rivers=new List<RiverMeasure>();foreach(var r in s.Rivers){var rr=new RiverMeasure{id=r.Id,parent=r.ParentId,minBedDepthM=10000};for(int i=1;i<r.Points.Length;i++){
                var a=r.Points[i-1];var b=r.Points[i];rr.lengthM+=Vector3.Distance(a,b);rr.maxRiseM=Mathf.Max(rr.maxRiseM,b.y-a.y);int count=Mathf.CeilToInt(Vector3.Distance(a,b)/32);
                for(int k=0;k<=count;k++){var p=Vector3.Lerp(a,b,count>0?k/(float)count:0);float depth=p.y-Surface(s,p);rr.minBedDepthM=Mathf.Min(rr.minBedDepthM,depth);if(depth<.05f)rr.dryCenterSamples++;}}
                if(!string.IsNullOrEmpty(r.ParentId)){var parent=s.Rivers.First(v=>v.Id==r.ParentId);var end=r.Points[r.Points.Length-1];rr.confluenceGapM=float.MaxValue;for(int i=1;i<parent.Points.Length;i++){float t,d=WorldMacroTerrain.SegmentDistance(end.x,end.z,parent.Points[i-1],parent.Points[i],out t);if(d<rr.confluenceGapM){rr.confluenceGapM=d;rr.confluenceLevelErrorM=Mathf.Abs(end.y-Mathf.Lerp(parent.Points[i-1].y,parent.Points[i].y,t));}}}
                rivers.Add(rr);
            }
            m.rivers=rivers.ToArray();Check(rivers.All(r=>r.maxRiseM<.01f),"rivers flow downhill");Check(rivers.All(r=>r.confluenceGapM<.1f&&r.confluenceLevelErrorM<.1f),"tributary confluences agree");Check(rivers.All(r=>r.dryCenterSamples==0),"river centerlines below visible water",string.Join(", ",rivers.Where(r=>r.dryCenterSamples>0).Select(r=>r.id+":"+r.dryCenterSamples)));
            Check(!RenderSettings.fog,"no stacked global fog");Check(Camera.main!=null,"inspection camera exists");
            m.checks=Checks.ToArray();File.WriteAllText(WorldMacroBuilder.Output+"/measurements.json",JsonUtility.ToJson(m,true));
            string result=string.Join("\n",Checks.Select(c=>c.status+" "+c.name+" "+c.detail));File.WriteAllText(WorldMacroBuilder.Output+"/technical.txt",result);
            return result+"\nterrain tris="+m.terrainTriangles+"; footprint="+m.footprintKm2+"km2; commit="+m.systemCommitRatio;
        }
        public static CaptureSpec[] Views()
        {
            var s=WorldMacroBuilder.Sheet;var list=new List<CaptureSpec>();
            var overview=View("overview","전체 지형 · 남동에서 북서 조망","overview",new Vector3(6300,11900,-10350),new Vector3(0,180,300),42);
            var perimeter=s.Outline.Select(v=>new Vector3(v.x*1.23f,WorldMacroTerrain.Height(s,v.x*1.23f,v.y*1.23f),v.y*1.23f)).ToArray();
            for(int attempt=0;attempt<30;attempt++){
                var inverse=Quaternion.Inverse(Quaternion.LookRotation(overview.target-overview.position));float tangent=Mathf.Tan(overview.fov*.5f*Mathf.Deg2Rad);
                bool fits=perimeter.All(p=>{var v=inverse*(p-overview.position);return v.z>0&&Mathf.Abs(v.y)<v.z*tangent*.88f&&Mathf.Abs(v.x)<v.z*tangent*(16f/9f)*.88f;});
                if(fits)break;overview.position=overview.target+(overview.position-overview.target)*1.04f;
            }
            list.Add(overview);
            foreach(string id in new[]{"Hwanggyeong","Cheongrim","Jeokro","Cheolong","Hyeongang"}){
                var a=s.FindSite(id);if(a==null)continue;list.Add(View("aerial_"+id,a.Label+" · 지역 조감","aerial",a.Position+new Vector3(1450,1650,-2000),a.Position+Vector3.up*80,62));
            }
            AddEye("eye_inn","금표 주막 접근 · 플레이 눈높이","Inn",new Vector3(-25,0,-80),new Vector3(0,3,0));
            AddEye("eye_capital","황경 남문 진입 · 플레이 눈높이","SouthGate",new Vector3(25,0,-135),new Vector3(0,6,0));
            // The V3 saddle moved and the old fixed compass aim faces its near uphill bank.
            // Inspect the real descent toward the capital at eye height, along its current direction.
            var passRoad=s.Routes.First(r=>r.Id=="Road_Pass_SouthPost");int passIndex=passRoad.Points.Length/5;
            var passEye=passRoad.Points[passIndex];passEye.y=TravelSurface(s,passEye)+1.8f;
            var passAim=passRoad.Points[Mathf.Min(passIndex+18,passRoad.Points.Length-1)]+Vector3.up*1.8f;
            list.Add(View("eye_eastpass","청림 고개 내리막 · 현재 가도 방향의 플레이 눈높이","eye",passEye,passAim,60));
            AddEye("eye_oldcapital","현강 옛 도읍 접근 · 플레이 눈높이","Hyeongang",new Vector3(40,0,-320),new Vector3(0,8,0));
            AddEye("eye_cheolong","철옹 관성 접근 · 금 속성 지면","Cheolong",new Vector3(0,0,-220),new Vector3(0,7,0));
            AddEye("eye_bridge","북쪽 물길과 도하점 · 플레이 눈높이","NorthBridge",new Vector3(80,0,-100),new Vector3(-20,0,30));
            AddEye("eye_jeokro","적로의 열린 구릉 · 플레이 눈높이","Jeokro",new Vector3(-120,0,-150),new Vector3(120,10,250));
            return list.ToArray();
            void AddEye(string id,string label,string siteId,Vector3 offset,Vector3 aim){var site=s.FindSite(siteId);if(site==null)return;var desired=site.Position+offset;
                // Inspect from the actual authored route, not an arbitrary point on a steep slope.
                var connected=s.Routes.Where(r=>r.From==siteId||r.To==siteId).SelectMany(r=>r.Points).ToArray();
                if(id=="eye_capital"||id=="eye_jeokro"){
                    float clearance=new Vector2(offset.x,offset.z).magnitude*.8f;
                    var outside=connected.Where(v=>new Vector2(v.x-site.Position.x,v.z-site.Position.z).magnitude>=clearance).ToArray();
                    if(outside.Length>0)connected=outside;
                }
                var p=connected.Length>0?connected.OrderBy(v=>new Vector2(v.x-desired.x,v.z-desired.z).sqrMagnitude).First():desired;
                p.y=TravelSurface(s,p)+1.8f;list.Add(View(id,label,"eye",p,site.Position+aim,60));}
        }
        static CaptureSpec View(string id,string label,string kind,Vector3 p,Vector3 target,float fov)=>new CaptureSpec{id=id,file=id+".png",label=label,kind=kind,position=p,target=target,fov=fov};
        public static string Capture(string id)
        {
            if(EditorApplication.isPlaying)throw new Exception("Capture in edit mode");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new Exception("New screenshot stopped: system commit >=85%");
            if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=WorldMacroBuilder.ScenePath)throw new Exception("Macro scene required");
            bool previousTone=id.StartsWith("tone_before_"),withoutTint=id.StartsWith("tint_before_");
            string viewId=previousTone?id.Substring("tone_before_".Length):withoutTint?id.Substring("tint_before_".Length):id;
            var views=Views();var view=views.FirstOrDefault(v=>v.id==viewId);if(view==null)return string.Join(",",views.Select(v=>v.id));
            string outputFile=previousTone||withoutTint?id+".png":view.file;
            var go=new GameObject("Temporary_Macro_Capture"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;cam.fieldOfView=view.fov;cam.farClipPlane=40000;cam.nearClipPlane=view.kind=="eye"?.3f:10;
            cam.transform.SetPositionAndRotation(view.position,Quaternion.LookRotation(view.target-view.position));
            cam.useOcclusionCulling=false;cam.layerCullDistances=new float[32];
            var data=cam.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.requiresDepthTexture=true;data.requiresColorTexture=true;
            RenderTexture rt=null;Texture2D texture=null;var prior=RenderTexture.active;
            var materials=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.HasProperty("_WashStrength")).Distinct().ToArray();
            var wash=materials.Select(m=>m.GetFloat("_WashStrength")).ToArray();
            var ground=AssetDatabase.LoadAssetAtPath<Material>(WorldMacroBuilder.Folder+"/MacroGround.mat");
            float density=ground.GetFloat("_InkDensity"),ceiling=ground.GetFloat("_ToneCeiling");var pathTones=ground.GetVector("_GroundPathTones");
            float tint=ground.HasProperty("_RealmTintStrength")?ground.GetFloat("_RealmTintStrength"):0;
            // Same V4 geometry and camera: restore V3 pigment values, or isolate the new colour wash.
            if(previousTone){ground.SetFloat("_InkDensity",1.16f);ground.SetFloat("_ToneCeiling",.62f);ground.SetVector("_GroundPathTones",new Vector4(.23f,.57f,0,0));}
            if(previousTone||withoutTint)ground.SetFloat("_RealmTintStrength",0);
            // An aerial drafting view cannot use a ground-camera distance range: it would erase the whole world.
            if(view.kind!="eye")foreach(var material in materials)material.SetFloat("_WashStrength",.10f);
            var look=Object.FindFirstObjectByType<Oheangbu.App.WorldLookDriver>();
            try{look?.PreviewRegionalSky(view.position);rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();File.WriteAllBytes(WorldMacroBuilder.Output+"/"+outputFile,texture.EncodeToPNG());}
            finally{ground.SetFloat("_InkDensity",density);ground.SetFloat("_ToneCeiling",ceiling);ground.SetVector("_GroundPathTones",pathTones);ground.SetFloat("_RealmTintStrength",tint);for(int i=0;i<materials.Length;i++)materials[i].SetFloat("_WashStrength",wash[i]);cam.targetTexture=null;RenderTexture.active=prior;if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(texture!=null)Object.DestroyImmediate(texture);Object.DestroyImmediate(go);look?.Apply();}
            File.WriteAllText(WorldMacroBuilder.Output+"/captures.json",JsonUtility.ToJson(new CaptureManifest{captures=views.Where(v=>File.Exists(WorldMacroBuilder.Output+"/"+v.file)).ToArray()},true));
            return "1920x1080 screenshot: "+outputFile+"; commit="+Prologue.PrologueAudit.CommitRatio();
        }
    }
}
