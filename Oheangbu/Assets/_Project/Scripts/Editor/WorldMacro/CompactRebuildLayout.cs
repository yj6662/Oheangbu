using System;
using System.IO;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string Layout()
  {
   if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/_Project/Art/World/WorldCompact","Rebuild");
   string path=Folder+"/WorldLayout.asset";var layout=AssetDatabase.LoadAssetAtPath<CompactWorldLayoutSO>(path);
   if(layout==null){layout=ScriptableObject.CreateInstance<CompactWorldLayoutSO>();AssetDatabase.CreateAsset(layout,path);}
   var places=new List<CompactWorldLayoutSO.Place>();
   void Place(string id,string realm,string label,float x,float z,string purpose,string interaction=null,string enemy=null)=>places.Add(new CompactWorldLayoutSO.Place{Id=id,Realm=realm,Label=label,XZ=new Vector2(x,z),Purpose=purpose,InteractionId=interaction,EncounterId=enemy});
   Place("mine","cheongrim","폐광",3520,1800,"폭파 물증과 첫 작도","mine_inquiry");
   Place("mine_overlook","cheongrim","계곡 전망",3390,1990,"광맥에서 주막 등불로 시야 인계");
   Place("geumpyo_inn","cheongrim","금표 주막",3110,2250,"첫 온기와 탐험 분기","geumpyo_inn");
   Place("relay","cheongrim","길목 역참",2840,2390,"정담의 첫 만남","jeongdam_j1");
   Place("logging","cheongrim","버려진 벌목장",3340,2700,"이상 성장 물증/금극목 환경 학습","logging_inquiry");
   Place("herb_path","cheongrim","약초꾼 길",3030,2800,"전투 우회와 숲 안쪽 정보");
   Place("root_cave","cheongrim","뿌리 굴",3550,2850,"어두운 실내 탐험과 심부 복귀");
   Place("old_tree","cheongrim","신목",3690,3270,"민담 경고가 있는 선택 위험 지선");
   Place("deep_forest","cheongrim","물든 심부",3280,3150,"과성장과 광맥 인도");
   Place("sanctuary","cheongrim","청룡 성역",3210,3550,"보스/종성/국/仁",null,"cheongryong");
   Place("high_cache","cheongrim","능선 위 흔적",3440,2450,"국 획득 전 노출되는 재방문 보상","guk_high_reward");
   Place("merchant","cheongrim","객주 분소",2700,2160,"왕소/봉인 화물 계약","wangso_w1");
   Place("road_pass","hwanggyeong","상경 고개",2440,1840,"황경 스카이라인 첫 전망");
   Place("inspection_one","hwanggyeong","고개 검문",2260,1840,"호송 수렴점","checkpoint_1");
   Place("road_hamlet","hwanggyeong","가도 취락",2120,2000,"휴식/우회 재합류");
   Place("inspection_two","hwanggyeong","하천 검문",1980,2180,"두 번째 검문과 도시 접근","checkpoint_2");
   Place("capital_delivery","hwanggyeong","성저 객주",1820,2320,"화물 인도","cargo_delivery");
   Place("south_gate","hwanggyeong","황경 남문",2000,2530,"장수 조우와 입성",null,"south_gate_general");
   Place("capital_center","hwanggyeong","황경 개천",2000,2960,"초반 앵커/후반 전복");
   Place("palace","hwanggyeong","궁성 방면",2000,3380,"후반 봉쇄와 피날레");
   Place("jeokro","jeokro","불탄 전장",1780,900,"주작/눈/禮의 강토");
   Place("cheolong","cheolong","국경 관문",760,2920,"백호/숫/義의 강토");
   Place("hyeongang","hyeongang","잠긴 옛 도읍",1930,4820,"현무/웅/智와 중반 진실");
   Place("temple","hyeongang","숨은 사찰",2670,5230,"후반 잔월회 아크 재방문");
   var routes=new List<CompactWorldLayoutSO.Route>();
   void Link(string a,string b,CompactRouteRole role=CompactRouteRole.Main,string ability=null)=>routes.Add(new CompactWorldLayoutSO.Route{Id=a+"__"+b,From=a,To=b,Role=role,RequiredAbility=ability,Width=role==CompactRouteRole.Main?6:3});
   Link("mine","mine_overlook");Link("mine_overlook","geumpyo_inn");Link("geumpyo_inn","relay");Link("geumpyo_inn","logging",CompactRouteRole.Exploration);Link("geumpyo_inn","herb_path",CompactRouteRole.Exploration);Link("relay","merchant");Link("relay","herb_path",CompactRouteRole.Exploration);Link("logging","root_cave",CompactRouteRole.Exploration);Link("root_cave","deep_forest",CompactRouteRole.ReturnShortcut);Link("herb_path","deep_forest");Link("logging","deep_forest");Link("deep_forest","sanctuary");Link("deep_forest","old_tree",CompactRouteRole.Exploration);Link("logging","high_cache",CompactRouteRole.AbilityGate,"국");Link("high_cache","geumpyo_inn",CompactRouteRole.ReturnShortcut);Link("merchant","road_pass");Link("road_pass","inspection_one");Link("inspection_one","road_hamlet");Link("road_hamlet","inspection_two");Link("inspection_two","capital_delivery");Link("capital_delivery","south_gate");Link("south_gate","capital_center");Link("capital_center","palace");Link("capital_center","jeokro");Link("jeokro","cheolong");Link("cheolong","hyeongang");Link("hyeongang","temple");Link("hyeongang","capital_center");
   foreach(var place in places)
   {
    var previous=Array.Find(layout.Places,p=>p.Id==place.Id);if(previous==null||!previous.HasSourceBinding)continue;
    place.HasSourceBinding=true;place.SceneRoots=previous.SceneRoots;place.SourceAnchor=previous.SourceAnchor;place.InteractionIds=previous.InteractionIds;place.EncounterIds=previous.EncounterIds;place.CheckpointIds=previous.CheckpointIds;place.YawDelta=previous.YawDelta;place.SurfaceBlendDistance=previous.SurfaceBlendDistance;
   }
   layout.Places=places.ToArray();layout.Routes=routes.ToArray();
   routes.Find(r=>r.Id=="high_cache__geumpyo_inn").OneWay=true;
   foreach(string from in new[]{"merchant","road_pass","inspection_one","road_hamlet","inspection_two","capital_delivery"})routes.Find(r=>r.From==from).GradeForVehicle=true;
   routes.Find(r=>r.Id=="merchant__road_pass").Bends=new[]{new Vector2(2590,2090),new Vector2(2510,1940)};
   routes.Find(r=>r.Id=="mine_overlook__geumpyo_inn").Bends=new[]{new Vector2(3300,2110),new Vector2(3235,2220)};
   routes.Find(r=>r.Id=="herb_path__deep_forest").Bends=new[]{new Vector2(3110,2920),new Vector2(3170,3070)};
   layout.Ridges=new[]{
    new CompactWorldLayoutSO.Ridge{Id="eastern_spine",Height=230,Width=330,Spine=new[]{new Vector2(3820,1450),new Vector2(3880,2500),new Vector2(3780,3650)}},
    new CompactWorldLayoutSO.Ridge{Id="forest_divide",Height=150,Width=200,Spine=new[]{new Vector2(2920,2580),new Vector2(3000,3040),new Vector2(2880,3550)}},
    new CompactWorldLayoutSO.Ridge{Id="western_ramparts",Height=220,Width=340,Spine=new[]{new Vector2(400,2100),new Vector2(580,3300),new Vector2(1150,4050)}},
    new CompactWorldLayoutSO.Ridge{Id="northern_bowl",Height=180,Width=420,Spine=new[]{new Vector2(850,5500),new Vector2(2100,5680),new Vector2(3250,5500)}}};
   layout.River=new[]{new Vector2(2140,5700),new Vector2(2040,4900),new Vector2(2150,3880),new Vector2(1960,3020),new Vector2(1980,2180),new Vector2(1550,1300),new Vector2(1400,250)};
   EditorUtility.SetDirty(layout);AssetDatabase.SaveAssets();
   File.WriteAllText(Output+"/layout.json",JsonUtility.ToJson(layout,true));
   return "World layout authored: "+places.Count+" places / "+routes.Count+" route links; not yet terrain geometry or traversal proof";
  }
 }
}
