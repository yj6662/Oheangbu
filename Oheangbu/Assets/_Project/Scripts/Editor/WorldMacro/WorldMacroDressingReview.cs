using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Review planning and file-backed reporting only. Never captures or changes the scene.</summary>
    public static class WorldMacroDressingReview
    {
        static string Output=>WorldMacroBuilder.Output+"/Dressing";
        [Serializable] public sealed class View
        {
            public string id,region,kind,label,before,after,note;public Vector3 position,target;public float fov=60;
        }
        [Serializable] public sealed class Plan
        {
            public string scope="1920x1080 stills only. Same pose and FOV per before/after pair. Visual judgement belongs to the user. Ground views do not relocate the actual capsule.";
            public View[] views;
        }
        public static string PreparePlan()
        {
            var views=new List<View>();var prior=WorldMacroAudit.Views();
            string[] regions={"Cheongrim","Jeokro","Cheolong","Hyeongang","Hwanggyeong"};
            string[] names={"청림","적로","철옹","현강","황경"};
            string[] ground={"eye_inn","eye_jeokro","eye_cheolong","eye_oldcapital","eye_capital"};
            for(int i=0;i<regions.Length;i++)
            {
                var a=prior.First(p=>p.id=="aerial_"+regions[i]);Add(regions[i]+"_aerial",regions[i],"aerial",names[i]+" 조감",a.position,a.target,"조감 촬영에서만 거리 담채 .10, 원경 식생 확장. 적용 전후에 동일하며 런타임 설정은 복원됨.");
                var g=prior.First(p=>p.id==ground[i]);var forward=(g.target-g.position);forward.y=0;forward.Normalize();
                var shoulder=g.position-forward*2.5f+Vector3.Cross(Vector3.up,forward)*.55f+Vector3.up*.45f;
                float terrain=WorldMacroTerrain.SurfaceHeight(WorldMacroBuilder.Sheet,shoulder.x,shoulder.z);shoulder.y=Mathf.Max(shoulder.y,terrain+1.7f);
                Add(regions[i]+"_ground",regions[i],"ground",names[i]+" 어깨 높이 지상 구도",shoulder,g.target,"기존 검사 경로에서 뒤로 2.5m 이동한 구도. 실제 플레이어를 옮기지 않으며 캡슐 가시성·통행 성공을 의미하지 않음.");
            }
            var geography=WorldMacroBuilder.Sheet;var pairs=new HashSet<string>();
            foreach(var route in geography.Routes)
            {
                if(!route.Carriage||route.Points.Length<10)continue;
                int last=Realm(route.Points[0]);
                for(int i=1;i<route.Points.Length&&pairs.Count<2;i++)
                {
                    int next=Realm(route.Points[i]);if(next==last)continue;string pair=Mathf.Min(last,next)+"_"+Mathf.Max(last,next);
                    if(pairs.Add(pair))
                    {
                        int from=Mathf.Max(0,i-9),to=Mathf.Min(route.Points.Length-1,i+12);var position=route.Points[from]+Vector3.up*2.2f;
                        Add("boundary_"+pairs.Count,"Boundary","boundary","강토 경계 군락 혼합 "+pairs.Count,position,route.Points[to]+Vector3.up*2,"실제 가도 "+route.Id+"를 따라 지역 폴리곤이 바뀌는 지점. 64m 군락과 경계 양쪽 약150m 혼합 검토.");
                    }
                    last=next;
                }
                if(pairs.Count>=2)break;
            }
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/capture_plan.json",JsonUtility.ToJson(new Plan{views=views.ToArray()},true));
            return Output+"/capture_plan.json ("+views.Count+" pairs; not captured)";
            void Add(string id,string region,string kind,string label,Vector3 position,Vector3 target,string note)
            {views.Add(new View{id=id,region=region,kind=kind,label=label,before=id+"_before",after=id+"_after",position=position,target=target,note=note});}
        }
        static int Realm(Vector3 position)
        {
            var regions=WorldMacroBuilder.Sheet.Regions;float nearest=float.PositiveInfinity;int fallback=0;
            for(int r=0;r<regions.Length;r++)
            {
                var p=regions[r].Polygon;bool inside=false;
                for(int i=0,j=p.Length-1;i<p.Length;j=i++)
                {
                    var a=p[j];var b=p[i];if((a.y>position.z)!=(b.y>position.z)&&position.x<(b.x-a.x)*(position.z-a.y)/(b.y-a.y)+a.x)inside=!inside;
                    var d=b-a;float t=d.sqrMagnitude<.001f?0:Mathf.Clamp01(Vector2.Dot(new Vector2(position.x,position.z)-a,d)/d.sqrMagnitude);
                    float distance=(new Vector2(position.x,position.z)-(a+d*t)).sqrMagnitude;if(distance<nearest){nearest=distance;fallback=r;}
                }
                if(inside)return r;
            }
            return fallback;
        }
        public static string CapturePlanned(string id,bool dressing)
        {
            if(!File.Exists(Output+"/capture_plan.json"))PreparePlan();var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(Output+"/capture_plan.json"));var view=plan.views.FirstOrDefault(v=>v.id==id);
            if(view==null)throw new ArgumentException("Unknown planned view: "+id);
            var source=Object.FindFirstObjectByType<Oheangbu.App.World.WorldMacroReviewController>()?.GetComponent<Camera>()??Camera.main;if(source==null)throw new InvalidOperationException("Macro camera missing.");
            float fov=source.fieldOfView;
            try{source.fieldOfView=view.fov;return WorldMacroDressingProbe.Capture(dressing?view.after:view.before,dressing,view.position.x,view.position.y,view.position.z,view.target.x,view.target.y,view.target.z);}
            finally{source.fieldOfView=fov;}
        }
        static string Esc(string value)=>System.Net.WebUtility.HtmlEncode(value??"");
        static string HtmlRecord(string file,string label)=>File.Exists(Output+"/"+file)?"<a href='"+Esc(file)+"'>"+Esc(label)+"</a>":Esc(label)+" · 미작성";
        static string MarkdownRecord(string file,string label)=>File.Exists(Output+"/"+file)?"["+label+"]("+file+")":label+" · 미작성";
        [Serializable] sealed class RecordHeader { public string utc,scope; }
        public static string WriteReview()
        {
            if(!File.Exists(Output+"/capture_plan.json"))PreparePlan();var plan=JsonUtility.FromJson<Plan>(File.ReadAllText(Output+"/capture_plan.json"));
            var html=new StringBuilder("<!doctype html><html lang='ko'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>5강토 환경 검토</title><style>body{margin:0;padding:28px;background:#151b1d;color:#e2e5df;font:16px/1.65 system-ui;max-width:1600px;margin:auto}h1{font-size:28px}h2{font-size:21px;margin-top:40px}a{color:#b8d6c1}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px}figure{margin:0}img{width:100%;aspect-ratio:16/9;object-fit:contain;background:#080b0c}.missing{aspect-ratio:16/9;display:grid;place-items:center;background:#252d2f;color:#a4adaa}small{color:#b4beba}pre{white-space:pre-wrap;background:#20282a;padding:18px}table{border-collapse:collapse}td,th{padding:8px 16px;border:1px solid #47514f}@media(max-width:800px){.pair{grid-template-columns:1fr}}</style><h1>5강토 전체 환경 — 식생·암석·생활 흔적</h1><p>비주얼 판단은 사용자 검토 사항입니다. 정지 이미지와 실제 저장된 검사 결과만 표시하며, 아직 없는 파일은 미검증으로 남깁니다. 실제 수면·길·건축물은 보존합니다.</p><p><a href='REPORT.md'>기술 보고서</a> · <a href='capture_plan.json'>촬영 위치·구도</a> · <a href='authoring_audit.json'>저작 데이터 검사</a></p>");
            var report=new StringBuilder("# 5강토 환경 검토\n\n기존 지형·길·수면·건축물 보존. 영상 없이 1920×1080 정지 이미지. 비주얼 합격은 사용자 판단.\n\n");
            report.AppendLine(MarkdownRecord("capture_plan.json","촬영 위치·구도")+" · "+MarkdownRecord("authoring_audit.json","저작 데이터·원본 보존 검사")+" · "+MarkdownRecord("grounding_geometry.json","선택 LOD 접지 보정 기록")+"\n");
            report.AppendLine("## 구현 범위\n\n256m 셀, 4m 수면·길·건물 제외 마스크, 64m 군락 기준 지역 혼합, 원본 저밀도 LOD 및 빌보드, 풀 80m/관목 220m/나무 800m, 원경 군락 3.2km, 근접 줄기·바위·소품 충돌체 최대384개. 생활 소품은 확정 좌표로 보존되며 퀘스트·보상 연결은 이번 범위가 아님.\n");
            const string aerialNote="조감 촬영에 한해 거리 담채 강도 .10과 원경 식생 확장을 적용한다. 전후 쌍에는 동일한 촬영 조건을 사용하며 이후 런타임 설정을 복원한다. 지상 구도는 실제 플레이어를 옮긴 보행 검사가 아니다. 개별 JSON의 aerialWashOverride·위치·방향·화각을 근거로 구분한다.";
            report.AppendLine(aerialNote+"\n");
            html.Append("<p>").Append(Esc(aerialNote)).Append("</p><p>").Append(HtmlRecord("grounding_geometry.json","선택 LOD 접지 보정 기록")).Append("</p>");
            foreach(var view in plan.views)
            {
                html.Append("<section id='").Append(Esc(view.id)).Append("'><h2>").Append(Esc(view.label)).Append("</h2><small>").Append(Esc(view.note)).Append("</small><div class='pair'>");
                foreach(var pair in new[]{(view.before,"적용 전 · 기존 원뿔 숲"),(view.after,"적용 후 · 새 환경")})
                {bool exists=File.Exists(Output+"/"+pair.Item1+".png");html.Append("<figure>").Append(exists?"<a href='"+Esc(pair.Item1)+".png'><img loading='lazy' src='"+Esc(pair.Item1)+".png' alt='"+Esc(view.label+" "+pair.Item2)+"'></a>":"<div class='missing'>미촬영 · 미검증</div>").Append("<figcaption>").Append(Esc(pair.Item2)).Append(" · ").Append(HtmlRecord(pair.Item1+".json","촬영 상태·구도 JSON")).Append("</figcaption></figure>");}
                html.Append("</div></section>");report.AppendLine("- "+view.label+": "+MarkdownRecord(view.before+".png","전 이미지")+" / "+MarkdownRecord(view.after+".png","후 이미지")+" · "+MarkdownRecord(view.before+".json","전 기록")+" / "+MarkdownRecord(view.after+".json","후 기록")+". 비주얼 판정 미검증.");
            }
            string audit=Output+"/authoring_audit.json";html.Append("<h2>기술 검사</h2>");report.AppendLine("\n## 기술 검사\n");
            if(File.Exists(audit))
            {var data=JsonUtility.FromJson<WorldMacroDressingAuthoring.Report>(File.ReadAllText(audit));html.Append("<p>저작 검사 UTC: ").Append(Esc(data.utc)).Append("</p><pre>").Append(Esc(string.Join("\n",data.checks))).Append("</pre>");report.AppendLine("저작 검사 UTC: "+data.utc+"\n");report.AppendLine(string.Join("\n",data.checks.Select(c=>"- "+c)));report.AppendLine($"\n셀 {data.cells}, 원형 설정 {data.prototypes}, 군락 {data.storyClusters}, 확정 소품 {data.fixedPlacements}. 이 수치는 배치 데이터의 검사이며 사용자 주행/카메라 검수를 대체하지 않음.");}
            else{html.Append("<p>저작 검사 파일 없음 · 미검증</p>");report.AppendLine("- 저작 검사 미실행·미검증");}
            if(File.Exists(Output+"/grounding_geometry.json"))
            {
                var grounding=JsonUtility.FromJson<RecordHeader>(File.ReadAllText(Output+"/grounding_geometry.json"));
                string note="접지 보정 UTC: "+grounding.utc+". 선택 LOD의 캐시 메시 밑면·접지 표본에 대한 보정 기록이다. 실제 월드 전 구간의 도보·차량 접촉 검사는 미검증이며, 앞선 저작 검사와 검사 범위·시점을 구분한다.";
                html.Append("<p>").Append(HtmlRecord("grounding_geometry.json","접지 보정 상세")).Append(" · ").Append(Esc(note)).Append("</p>");report.AppendLine("\n"+MarkdownRecord("grounding_geometry.json","접지 보정 상세")+" · "+note);
            }
            html.Append("<h2>1080p 성능 측정</h2><table><tr><th>검사</th><th>상태</th><th>프레임 중앙 / P95 ms</th><th>CPU / GPU ms</th></tr>");report.AppendLine("\n## 성능\n\nUnity Editor의 같은 1080p 렌더 타깃·고정 카메라 비교. 빌드 성능 보증이 아니며 GPU 미수집은 0으로 간주하지 않음.\n");
            report.AppendLine("120fps의 프레임 예산은 약 8.33ms다. 아래는 저장된 개별 측정 기록이며, 구현이 바뀐 경우 변경 전 기록과 최종 기록을 구분해 평가한다. 한 시점의 짧은 측정으로 전체 맵 목표 달성을 확정하지 않는다.\n");
            var performance=Directory.GetFiles(Output,"*_performance.json").OrderBy(file=>file).ToArray();
            foreach(var file in performance)
            {var p=JsonUtility.FromJson<WorldMacroDressingProbe.Result>(File.ReadAllText(file));string record=Path.GetFileName(file);html.Append("<tr><td>").Append(HtmlRecord(record,p.label)).Append("<br><small>").Append(Esc(p.utc)).Append("</small></td><td>").Append(Esc(p.status)).Append("</td><td>").Append(p.frameMedianMs.ToString("F2")).Append(" / ").Append(p.frameP95Ms.ToString("F2")).Append("</td><td>").Append(p.cpuSamples>0?p.cpuMedianMs.ToString("F2"):"미수집").Append(" / ").Append(p.gpuSamples>0?p.gpuMedianMs.ToString("F2"):"미수집").Append("</td></tr>");report.AppendLine($"- {MarkdownRecord(record,p.label)}: {p.status}, UTC {p.utc}, {p.samples}프레임, 중앙/P95 {p.frameMedianMs:F2}/{p.frameP95Ms:F2}ms, CPU {(p.cpuSamples>0?p.cpuMedianMs.ToString("F2"):"미수집")}ms, GPU {p.gpuStatus}, 풀 초과 {p.colliderOverflow}.");}
            if(performance.Length==0){html.Append("<tr><td colspan='4'>측정 파일 없음 · 미검증</td></tr>");report.AppendLine("- 성능 측정 미실행·미검증");}
            html.Append("</table><p>120fps의 프레임 예산은 약 8.33ms입니다. 개별 시점의 짧은 측정이며 코드 변경 전후 기록은 저장 시각과 파일을 구분해 평가합니다. 전체 맵 성능 합격을 의미하지 않습니다.</p><p>실제 도보·차량 완주, 카메라와 나뭇가지의 충돌, 프레임 변동 및 최종 밀도·색상·전통적인 인상은 별도 검토 사항입니다.</p></html>");
            report.AppendLine("\n## 잔여 검증\n\n실제 도보·차량 완주, 카메라 경계 통과, 순간 이동 직후 충돌, 수목 LOD 전환의 시각 품질, 최종 지역별 밀도·색상은 이 보고서의 정적 검사만으로 통과 처리하지 않음.");
            File.WriteAllText(Output+"/REVIEW.html",html.ToString());File.WriteAllText(Output+"/REPORT.md",report.ToString());return Output+"/REVIEW.html";
        }
    }
}
