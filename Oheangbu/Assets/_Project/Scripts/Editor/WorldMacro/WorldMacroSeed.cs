using System;
using System.Collections.Generic;
using UnityEngine;
using Oheangbu.Data.World;
using Sheet = Oheangbu.Data.World.WorldMacroSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Geographic TEST seed. Landforms precede settlement and route planning.</summary>
    public static class WorldMacroSeed
    {
        static Vector3 P(float x, float z, float y = 0f) => new Vector3(x, y, z);
        static Vector2 Q(float x, float z) => new Vector2(x, z);
        static Sheet.RidgeSpec R(string id, float width, params Vector3[] p) => new Sheet.RidgeSpec { Id = id, Width = width, Points = p };
        static Sheet.BasinSpec B(string id, float x, float z, float rx, float rz, float floor) => new Sheet.BasinSpec { Id = id, Center = Q(x,z), Radius = Q(rx,rz), Floor = floor };
        static Sheet.RiverSpec W(string id, string parent, float width, params Vector3[] p) => new Sheet.RiverSpec { Id = id, ParentId = parent, Width = width, Points = p };
        static Sheet.SiteSpec S(string id, string label, string kind, RealmId realm, float x, float z) => new Sheet.SiteSpec { Id = id, Label = label, Kind = kind, Realm = realm, Position = P(x,z) };
        static Sheet.RegionSpec G(string id, string label, RealmId realm, float density, params Vector2[] p) => new Sheet.RegionSpec { Id = id, Label = label, Realm = realm, Density = density, Polygon = p };

        public static Sheet Create()
        {
            var s = ScriptableObject.CreateInstance<Sheet>();
            s.name = "WorldMacro_5Realms_TEST";
            s.FoothillHeight=140f;s.FoothillWavelength=620f;
            s.FoothillDetailHeight=34f;s.FoothillDetailWavelength=260f;
            s.BasinCoreFraction=.40f;s.BasinRimFraction=1.08f;
            s.ValleyMinimumWidth=300f;s.ValleyWidthPerRiverWidth=8.5f;
            s.ValleyBankTerrace=75f;s.ValleyFoothillRetention=.22f;
            s.Outline = new[] {
                Q(-2200,-5880),Q(-1050,-5740),Q(120,-5870),Q(1640,-5500),Q(2120,-4930),
                Q(3030,-4580),Q(3410,-3510),Q(3150,-2600),Q(3560,-1780),Q(3890,-590),
                Q(3740,620),Q(3940,1660),Q(3570,2660),Q(3670,3540),Q(3120,4230),
                Q(2760,4870),Q(1640,5240),Q(780,5850),Q(-380,5630),Q(-1290,5810),
                Q(-2210,5220),Q(-2400,4520),Q(-3120,4030),Q(-3360,2870),Q(-3800,2080),
                Q(-3540,1050),Q(-3980,110),Q(-3580,-890),Q(-3790,-1960),Q(-3390,-2950),
                Q(-3500,-3980),Q(-2960,-4640),Q(-2840,-5380)
            };
            s.Ridges = new[] {
                R("Bukhan_MainDivide",900,P(-600,1250,270),P(-40,1700,430),P(700,2400,570),P(1150,3150,490),P(1490,3870,535),P(1460,4580,450),P(1060,5490,365)),
                R("Cheongrim_EasternSpine",960,P(700,2400,570),P(1800,2540,515),P(2660,3000,580),P(3170,3500,440),P(3120,4200,335)),
                R("EasternSouthSpur",690,P(2660,3000,580),P(2990,2080,355),P(3240,1390,290),P(3120,540,330),P(3340,-200,280),P(3140,-1050,190)),
                R("EasternWestSpur",690,P(1800,2540,515),P(1540,1890,315),P(1160,1400,220),P(1150,680,195),P(1050,270,110)),
                R("CityNorthRidge",590,P(-600,1250,270),P(-1300,1050,370),P(-1740,610,300),P(-2020,-40,240),P(-2040,-640,145)),
                R("CitySouthRidge",640,P(-1800,-1980,210),P(-1000,-1910,235),P(-290,-2130,290),P(420,-2330,225),P(1100,-2150,185),P(1690,-1710,255),P(1940,-1010,285),P(1600,-320,160)),
                R("CheolongWestDivide",880,P(-3370,-3110,280),P(-3130,-2130,370),P(-3150,-1350,320),P(-3350,-400,380),P(-3140,370,470),P(-3100,1180,390),P(-3150,2200,440),P(-2960,2970,345),P(-2860,3890,280)),
                R("WestInnerSpur",610,P(-3150,2200,440),P(-2330,2290,320),P(-1860,1780,240),P(-1680,1320,130)),
                R("SongakNorthernDivide",830,P(-2360,4550,360),P(-1640,4900,500),P(-810,5220,565),P(-150,5440,420),P(620,5570,325)),
                R("SongakWestSpur",680,P(-1640,4900,500),P(-1720,4100,280),P(-1960,3500,340),P(-1770,2850,230)),
                R("SouthernBrokenHills",760,P(1570,-3010,160),P(1960,-3820,190),P(1570,-4510,150),P(1930,-5040,110)),
                R("JeokroWesternHills",570,P(-2780,-4370,140),P(-2220,-3530,175),P(-2230,-2760,140)),
                // Interior branches extend the geological divides. Basins reserve appropriately
                // sized settlement ground; mapped drainage is independent of these branch ranges.
                R("Bukhan_NorthwesternBranch",580,P(700,2400,570),P(340,2790,395),P(30,3010,285),P(-350,2780,175)),
                R("Bukhan_CapitalFoothills",610,P(-40,1700,430),P(420,1510,365),P(680,1100,290),P(350,590,150)),
                R("Cheongrim_InnerNorthernFork",710,P(1800,2540,515),P(2090,3130,405),P(1840,3540,365),P(2070,3960,260)),
                R("Cheongrim_NortheasternRibs",580,P(2660,3000,580),P(3100,2780,425),P(3380,2340,265)),
                R("EasternMidInterfluve",720,P(3120,540,330),P(2780,-190,365),P(2500,-940,390),P(2770,-1710,340)),
                R("EasternSouthernMassif",960,P(2770,-1710,340),P(2520,-2390,430),P(2850,-3040,385),P(2630,-3820,275)),
                R("EasternSouthWesternBranch",630,P(2520,-2390,430),P(2110,-2650,320),P(1700,-3100,230),P(1410,-3400,140)),
                R("CapitalSouthInnerFork",560,P(1940,-1010,285),P(1500,-1190,275),P(1280,-1640,315),P(1230,-2500,220)),
                R("Jeokro_SouthwesternShoulder",800,P(-2220,-3530,175),P(-1880,-4510,265),P(-1270,-5090,295),P(-700,-5460,155)),
                R("Jeokro_SouthernFoothills",640,P(1930,-5040,110),P(1300,-5060,225),P(760,-5360,195),P(190,-5540,110)),
                R("WesternSouthInterior",710,P(-3130,-2130,370),P(-2730,-2280,340),P(-2470,-2920,295),P(-2770,-3460,245)),
                R("WesternMiddleFork",720,P(-3150,2200,440),P(-2630,2910,395),P(-2330,3360,350),P(-2460,3830,255)),
                R("Songak_EasternLateral",750,P(1060,5490,365),P(1770,4970,415),P(2220,4550,380),P(2350,4050,255)),
                R("Songak_InnerWesternBranch",600,P(-1960,3500,340),P(-1320,3630,300),P(-1040,3140,285),P(-590,2860,160)),
                R("CapitalWesternShoulders",420,P(-1740,610,300),P(-1480,140,220),P(-1550,-390,145),P(-1720,-780,95)),
                R("CapitalEasternFoothills",440,P(680,1100,290),P(620,570,220),P(820,60,175),P(430,-370,85)),
                R("JeokroNorthwesternFoothills",510,P(-1000,-1910,235),P(-920,-2580,180),P(-1090,-3030,155),P(-880,-3430,95)),
                R("JeokroNortheasternFoothills",490,P(1230,-2500,220),P(720,-2650,185),P(370,-2970,145),P(510,-3290,95)),
                R("JeokroSouthernInterfluve",540,P(1410,-3400,140),P(890,-3810,190),P(470,-4220,145),P(-140,-4510,90)),
                R("JeokroWesternLowBranch",480,P(-2220,-3530,175),P(-1670,-3750,165),P(-1220,-4180,120)),
                R("WesternInnerFoothills",460,P(-2330,3360,350),P(-2060,2760,235),P(-2090,2210,205),P(-1800,1830,130)),
                R("NorthTransitEasternShoulder",440,P(-350,2780,175),P(-190,2370,210),P(-300,1890,160),P(-160,1540,105)),
                R("SongakSouthernShoulder",480,P(-590,2860,160),P(-420,3500,180),P(-100,3770,130),P(310,3720,90)),
                R("CheongrimOuterFoothillBranch",410,P(1160,1400,220),P(1530,1250,190),P(1720,870,125),P(1590,530,80))
            };
            s.Basins = new[] {
                B("CapitalBasin",-350,-770,860,940,72),
                B("CheongrimOuterValley",2030,850,480,700,134),
                B("OldCapitalBasin",100,4280,650,690,159),
                B("WesternPassCountry",-2240,350,580,680,126),
                B("SouthernOpenCountry",-430,-3560,680,750,36),
                B("DeepForestHollow",2240,1970,340,390,206),
                B("NorthTransitValley",-650,2050,290,510,120)
            };
            s.Rivers = new[] {
                W("HyeonMain", "", 62, P(600,5500,185),P(550,4150,152),P(-500,3300,129),P(-1350,2100,107),P(-1000,700,76),P(-1050,-800,52),P(-1400,-2400,24),P(-2100,-4200,13),P(-2050,-5350,8)),
                W("CheongTributary", "HyeonMain", 28, P(3000,2710,190),P(2550,2130,157),P(2330,1520,129),P(2290,830,108),P(1890,100,89),P(1160,-510,71),P(820,-1320,50),P(220,-1630,42),P(-720,-1940,32),P(-1400,-2400,24)),
                W("WesternTributary", "HyeonMain", 22, P(-2750,1820,153),P(-2470,940,121),P(-2490,80,100),P(-2340,-820,78),P(-1720,-1190,48),P(-1300,-1740,36),P(-1400,-2400,24)),
                W("SongakCreek", "HyeonMain", 15, P(-1150,4740,181),P(-950,4300,169),P(-250,4050,158),P(550,4150,152)),
                W("SouthernCreek", "HyeonMain", 20, P(830,-3190,40),P(220,-3570,29),P(-540,-3900,23),P(-1250,-4280,18),P(-2100,-4200,13)),
                W("EastDrain", "", 18, P(3420,1730,225),P(3470,980,180),P(3560,280,117),P(3700,-410,63),P(3660,-1380,27))
            };
            SmoothRivers(s);
            s.Regions = new[] {
                G("Hwanggyeong","황경",RealmId.Hwanggyeong,.12f,Q(-1960,-2030),Q(350,-2460),Q(1590,-1700),Q(1640,-180),Q(970,550),Q(-290,1240),Q(-1840,450)),
                G("Cheongrim","청림",RealmId.Cheongrim,.76f,Q(1640,-180),Q(1050,1300),Q(800,2530),Q(1900,3960),Q(3300,3890),Q(3870,1570),Q(3660,-1400)),
                G("Jeokro","적로",RealmId.Jeokro,.18f,Q(-3280,-3110),Q(-1960,-2030),Q(350,-2460),Q(1590,-1700),Q(3080,-2650),Q(3030,-4580),Q(1640,-5500),Q(-1900,-5790)),
                G("Cheolong","철옹",RealmId.Cheolong,.36f,Q(-3680,-1980),Q(-1960,-2030),Q(-1840,450),Q(-290,1240),Q(-1360,2940),Q(-3090,4010),Q(-3790,1900)),
                G("Hyeongang","현강",RealmId.Hyeongang,.29f,Q(-3090,4010),Q(-1360,2940),Q(-290,1240),Q(800,2530),Q(1900,3960),Q(2760,4870),Q(780,5850),Q(-1290,5810),Q(-2400,4520))
            };
            s.Sites = new[] {
                S("Hwanggyeong","황경 도성","City",RealmId.Hwanggyeong,-300,-630),
                S("Cheongrim","청림 외림","Forest",RealmId.Cheongrim,1990,810),
                S("Jeokro","적로 전장","Settlement",RealmId.Jeokro,-350,-3530),
                S("Cheolong","철옹 관성","Fortress",RealmId.Cheolong,-2240,350),
                S("Hyeongang","현강 옛 도읍","RuinedCity",RealmId.Hyeongang,20,4320),
                S("Mine","폐광 시작 예약","Mine",RealmId.Cheongrim,3090,470),
                S("Inn","금표 주막 예약","Inn",RealmId.Cheongrim,2110,700),
                S("Logging","벌목장 예약","Logging",RealmId.Cheongrim,1970,1360),
                S("DeepForest","물든 심부 예약","Forest",RealmId.Cheongrim,2280,1940),
                S("Tree","고목·국 재방문 예약","Tree",RealmId.Cheongrim,2790,2300),
                S("Dragon","청룡 영역 예약","Boss",RealmId.Cheongrim,2230,2480),
                S("SouthGate","황경 남문","Gate",RealmId.Hwanggyeong,-270,-1260),
                S("EastPass","청림 서남 고개","Pass",RealmId.Cheongrim,1180,-320),
                S("WestPass","철옹 동쪽 고개","Pass",RealmId.Cheolong,-1700,570),
                S("NorthPass","송악 남쪽 고개","Pass",RealmId.Hyeongang,-670,2370),
                S("SouthBridge","남쪽 나루·교량","Bridge",RealmId.Jeokro,-1400,-2400),
                S("NorthBridge","북쪽 교량","Bridge",RealmId.Hyeongang,-600,3158),
                S("PostStation","역참·정담 예약","PostStation",RealmId.Cheongrim,1780,330),
                S("MerchantBranch","객주 분소·왕소 예약","Merchant",RealmId.Cheongrim,1410,-80),
                S("OldTemple","북쪽 산사 예약","Temple",RealmId.Hyeongang,-1180,4690),
                S("EastBridge","청림 계류 교량","Bridge",RealmId.Cheongrim,2290,830),
                S("CapitalBridge","황경 서쪽 교량","Bridge",RealmId.Hwanggyeong,-1045,-650),
                S("SouthPost","성저 남쪽 역로","PostStation",RealmId.Hwanggyeong,260,-1440),
                S("CapitalSouthBridge","성저 남쪽 계류 교량","Bridge",RealmId.Hwanggyeong,220,-1630),
                S("ForestBridge","심부 계류 목교 예약","Bridge",RealmId.Cheongrim,2625,2226.667f),
                S("TempleBridge","산사 계류 목교 예약","Bridge",RealmId.Hyeongang,-1100,4630),
                S("WesternForkBridge","서남 지류 교량 예약","Bridge",RealmId.Cheolong,-1858.7f,-1105.1f),
                S("OldCapitalCreekBridge","옛 도읍 남쪽 계류 교량","Bridge",RealmId.Hyeongang,20,4059.6f),
                S("NorthCapitalPass","황경 북산 귀환 고개","Pass",RealmId.Hwanggyeong,-280,900),
                S("EastFoothillPass","청림 서쪽 산기슭 귀환 고개","Pass",RealmId.Cheongrim,1160,530),
                S("SouthSaddle","적로 북쪽 귀환 안부","Pass",RealmId.Jeokro,390,-2400)
            };
            for (int i = 0; i < s.Sites.Length; i++)
            {
                var site = s.Sites[i];
                if(site.Kind=="Bridge")SnapBridge(s,site);
                else if(site.Kind=="Pass"||site.Kind=="Merchant"||site.Kind=="PostStation"||site.Kind=="Inn"||site.Kind=="Settlement"||site.Kind=="Mine"||site.Kind=="Boss")
                    FitDrySite(s,site,site.Kind=="Pass"?640f:site.Id=="SouthPost"?192f:site.Kind=="Mine"||site.Kind=="Boss"?384f:320f);
                site.Position.y = WorldMacroTerrain.SurfaceHeight(s,site.Position.x,site.Position.z);
                if (site.Kind == "Bridge") site.Position.y = WorldMacroBridgeGeometry.Create(s,site).DeckTop-.9f;
            }
            var planner = new RoutePlanner(s);
            s.Routes = new[] {
                planner.Make("Trail_Mine_Inn","Mine","EastBridge",false,"EA: 폐광 탈출; 첫 구간 위치 예약"),
                planner.Make("Trail_Bridge_Inn","EastBridge","Inn",false,"EA: 금표 주막 첫 안식"),
                planner.Make("Trail_Inn_Logging","Inn","Logging",false,"EA: 벌목장"),
                planner.Make("Trail_Logging_Deep","Logging","DeepForest",false,"EA: 물든 심부"),
                planner.Make("Trail_Deep_Dragon","DeepForest","Dragon",false,"EA: 청룡; 국 획득 예약"),
                planner.Make("Trail_Deep_TreeBridge","DeepForest","ForestBridge",false,"국 이후 재방문; 본편 통과 조건 아님"),
                planner.Make("Trail_TreeBridge_Tree","ForestBridge","Tree",false,"국 이후 고목 지선 예약"),
                planner.Make("Trail_TreeBridge_Logging","ForestBridge","Logging",false,"국 이후 귀환 지선 예약"),
                planner.Make("Road_Inn_Post","Inn","PostStation",true,"EA: 역참·정담"),
                planner.Make("Road_Post_Merchant","PostStation","MerchantBranch",true,"EA: 객주 분소·왕소"),
                planner.Make("Road_Merchant_Pass","MerchantBranch","EastPass",true,"EA: 상경 가도"),
                planner.Make("Road_Pass_SouthPost","EastPass","SouthPost",true,"EA: 화물 동행; 성저 진입"),
                planner.Make("Road_SouthPost_Gate","SouthPost","SouthGate",true,"EA: 남문 관문전 종점; 도성 내부 후속"),
                planner.Make("Road_Gate_CapitalReservation","SouthGate","Hwanggyeong",true,"물리 연결 예약; 남문 승리 이후 통행·도성 내부 구현은 후속"),
                planner.Make("Road_Capital_WestBridge","Hwanggyeong","CapitalBridge",true,"후속 연결 예약"),
                planner.Make("Road_WestBridge_WestPass","CapitalBridge","WestPass",true,"후속 연결 예약"),
                planner.Make("Road_WestPass_Cheolong","WestPass","Cheolong",true,"후속 연결 예약"),
                planner.Make("Road_WestPass_NorthPass","WestPass","NorthPass",true,"긴 산지 가도; 북방 예약"),
                planner.Make("Road_NorthPass_Bridge","NorthPass","NorthBridge",true,"후속 연결 예약"),
                planner.Make("Road_NorthBridge_OldCreek","NorthBridge","OldCapitalCreekBridge",true,"옛 도읍 진입 예약"),
                planner.Make("Road_OldCreek_OldCapital","OldCapitalCreekBridge","Hyeongang",true,"옛 도읍 진입 예약"),
                planner.Make("Trail_OldCapital_TempleBridge","Hyeongang","TempleBridge",false,"북쪽 산사 탐험 예약"),
                planner.Make("Trail_TempleBridge_Temple","TempleBridge","OldTemple",false,"북쪽 산사 탐험 예약"),
                planner.Make("Road_Cheolong_WesternFork","Cheolong","WesternForkBridge",true,"서남 생활권 연결 예약"),
                planner.Make("Road_WesternFork_SouthBridge","WesternForkBridge","SouthBridge",true,"서남 생활권 연결 예약"),
                planner.Make("Road_SouthBridge_Jeokro","SouthBridge","Jeokro",true,"넓은 전장 가도 예약"),
                planner.Make("Road_Jeokro_CapitalSouthBridge","Jeokro","CapitalSouthBridge",true,"남쪽 성저 가도 예약"),
                planner.Make("Road_CapitalSouthBridge_SouthPost","CapitalSouthBridge","SouthPost",true,"남쪽 성저 가도 예약"),
                // New foot alternatives use the same terrain and existing bridges. They reserve
                // geography only and do not unlock the South Gate or add gameplay travel services.
                planner.Make("Return_NorthPass_NorthCapitalPass","NorthPass","NorthCapitalPass",false,"황경 귀환: 현강에서 철옹을 거치지 않는 북산 보행길; 후속 통행 예약"),
                planner.Make("Return_NorthCapitalPass_Capital","NorthCapitalPass","Hwanggyeong",false,"황경 귀환: 북쪽 분지 진입 보행길; 도성 통행 규칙은 후속"),
                planner.Make("Return_Cheongrim_Inn","Cheongrim","Inn",false,"황경 귀환: 청림 외림 중심과 금표 주막 연결"),
                planner.Make("Return_Inn_EastFoothillPass","Inn","EastFoothillPass",false,"황경 귀환: 역참 가도와 구분되는 청림 산기슭 보행길"),
                planner.Make("Return_EastFoothillPass_Capital","EastFoothillPass","Hwanggyeong",false,"황경 귀환: 동쪽 산자락에서 도성 분지로; 도성 통행 규칙은 후속"),
                planner.Make("Return_Jeokro_SouthSaddle","Jeokro","SouthSaddle",false,"황경 귀환: 넓은 전장에서 북쪽 안부를 넘는 보행길"),
                planner.Make("Return_SouthSaddle_CapitalSouthBridge","SouthSaddle","CapitalSouthBridge",false,"황경 귀환: 남쪽 안부와 기존 성저 계류 교량 연결"),
                planner.Make("Return_CapitalSouthBridge_SouthGate","CapitalSouthBridge","SouthGate",false,"황경 귀환: 교량에서 남문으로 직접 진입; 남문 관문전 규칙 유지")
            };
            return s;
        }

        static void SmoothRivers(Sheet s)
        {
            foreach(var river in s.Rivers)
            {
                var source=river.Points;var result=new List<Vector3>();
                for(int i=0;i+1<source.Length;i++)
                {
                    var a=source[Mathf.Max(0,i-1)];var b=source[i];var c=source[i+1];var d=source[Mathf.Min(source.Length-1,i+2)];
                    int steps=Mathf.Max(2,Mathf.CeilToInt(Vector3.Distance(b,c)/140f));
                    for(int k=0;k<steps;k++)
                    {
                        float t=k/(float)steps,tt=t*t,ttt=tt*t;
                        var p=.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*tt+(-a+3*b-3*c+d)*ttt);
                        p.y=Mathf.Lerp(b.y,c.y,t);result.Add(p);
                    }
                }
                result.Add(source[source.Length-1]);river.Points=result.ToArray();
            }
        }

        static bool Crosses(Vector3 a,Vector3 b,Vector3 c,Vector3 d,out Vector3 point)
        {
            float rx=b.x-a.x,rz=b.z-a.z,sx=d.x-c.x,sz=d.z-c.z,den=rx*sz-rz*sx;
            point=Vector3.zero;if(Mathf.Abs(den)<.001f)return false;
            float cx=c.x-a.x,cz=c.z-a.z,t=(cx*sz-cz*sx)/den,u=(cx*rz-cz*rx)/den;
            if(t<0||t>1||u<0||u>1)return false;point=Vector3.Lerp(a,b,t);return true;
        }
        static void SnapBridge(Sheet s,Sheet.SiteSpec site)
        {
            float nearest=float.MaxValue;var best=site.Position;
            foreach(var r in s.Rivers)for(int i=0;i+1<r.Points.Length;i++)
            {float t,d=WorldMacroTerrain.SegmentDistance(site.Position.x,site.Position.z,r.Points[i],r.Points[i+1],out t);if(d>=nearest)continue;nearest=d;best=Vector3.Lerp(r.Points[i],r.Points[i+1],t);}
            site.Position=best;
        }
        static void FitDrySite(Sheet s,Sheet.SiteSpec site,float radius)
        {
            var original=site.Position;var best=original;float score=float.MaxValue;
            for(float dz=-radius;dz<=radius;dz+=64f)for(float dx=-radius;dx<=radius;dx+=64f)
            {
                if(dx*dx+dz*dz>radius*radius)continue;var p=original+new Vector3(dx,0,dz);
                if(!WorldMacroTerrain.Contains(s,p.x,p.z))continue;
                bool valid=true;float closest=float.MaxValue,water=0;
                foreach(var river in s.Rivers)for(int i=0;i+1<river.Points.Length;i++)
                {
                    float t,d=WorldMacroTerrain.SegmentDistance(p.x,p.z,river.Points[i],river.Points[i+1],out t);
                    if(d<river.Width*.5f+70f){valid=false;break;}
                    if(d<closest){closest=d;water=Mathf.Lerp(river.Points[i].y,river.Points[i+1].y,t);}
                    if(Crosses(original,p,river.Points[i],river.Points[i+1],out _)){valid=false;break;}
                }
                if(!valid)continue;
                p.y=WorldMacroTerrain.SurfaceHeight(s,p.x,p.z);if(p.y<water+4f)continue;
                float gx=(WorldMacroTerrain.SurfaceHeight(s,p.x+16,p.z)-WorldMacroTerrain.SurfaceHeight(s,p.x-16,p.z))/32f;
                float gz=(WorldMacroTerrain.SurfaceHeight(s,p.x,p.z+16)-WorldMacroTerrain.SurfaceHeight(s,p.x,p.z-16))/32f;
                float grade=Mathf.Atan(Mathf.Sqrt(gx*gx+gz*gz))*Mathf.Rad2Deg;
                if(grade>(site.Kind=="Pass"?9f:7f))continue;
                float value=grade*12f+Mathf.Sqrt(dx*dx+dz*dz)*.07f+p.y*.04f;
                if(value<score){score=value;best=p;}
            }
            site.Position=best;
        }

        static float RiverLevel(Sheet s, float x, float z)
        {
            float nearest=float.MaxValue, level=0;
            foreach(var r in s.Rivers) for(int i=0;i+1<r.Points.Length;i++)
            {
                float t; float d=WorldMacroTerrain.SegmentDistance(x,z,r.Points[i],r.Points[i+1],out t);
                if(d<nearest){nearest=d;level=Mathf.Lerp(r.Points[i].y,r.Points[i+1].y,t);}
            }
            return level;
        }

        // Roads discover lower-gradient passages on already completed geography. They do not carve it.
        sealed class RoutePlanner
        {
            const float Step=64f;
            readonly Sheet sheet; readonly int nx,nz;
            readonly float[] heights,waterPenalty; readonly bool[] inside,wet;
            readonly Vector3[] nodes;
            readonly int surfaceNx,surfaceNz;
            readonly float[] surfaceHeights;readonly bool[] surfaceKnown;
            readonly Dictionary<long,SegmentGrade> edgeGrades=new Dictionary<long,SegmentGrade>();
            readonly List<WorldMacroBridgeGeometry.Plan> bridgePlans=new List<WorldMacroBridgeGeometry.Plan>();
            struct SegmentGrade
            {
                public float MeanSlopeSquared,PeakSlope,CrossSlope,PeakSurfaceSlope;
            }
            const float MaxWalkableSurfaceSlope=.985f; // Below tan(45 degrees), the actual PlayerRig limit.
            public RoutePlanner(Sheet s)
            {
                sheet=s;nx=Mathf.FloorToInt((s.BoundsMax.x-s.BoundsMin.x)/Step)+1;nz=Mathf.FloorToInt((s.BoundsMax.y-s.BoundsMin.y)/Step)+1;
                surfaceNx=Mathf.CeilToInt((s.BoundsMax.x-s.BoundsMin.x)/s.GridSpacing)+1;
                surfaceNz=Mathf.CeilToInt((s.BoundsMax.y-s.BoundsMin.y)/s.GridSpacing)+1;
                surfaceHeights=new float[surfaceNx*surfaceNz];surfaceKnown=new bool[surfaceHeights.Length];
                foreach(var site in s.Sites)if(site.Kind=="Bridge")bridgePlans.Add(WorldMacroBridgeGeometry.Create(s,site));
                nodes=new Vector3[nx*nz];heights=new float[nodes.Length];inside=new bool[nodes.Length];wet=new bool[nodes.Length];waterPenalty=new float[nodes.Length];
                for(int j=0;j<nz;j++)for(int i=0;i<nx;i++)
                {
                    int n=j*nx+i;float x=s.BoundsMin.x+i*Step,z=s.BoundsMin.y+j*Step;
                    nodes[n]=P(x,z);inside[n]=WorldMacroTerrain.Contains(s,x,z);if(!inside[n])continue;
                    heights[n]=Ground(x,z);nodes[n].y=heights[n];
                    float penalty=0;
                    foreach(var river in s.Rivers)for(int k=0;k+1<river.Points.Length;k++)
                    {float t;float d=WorldMacroTerrain.SegmentDistance(x,z,river.Points[k],river.Points[k+1],out t);if(d<river.Width*.5f+Step*.72f)penalty=28f;}
                    wet[n]=penalty>0;
                    if(penalty>0)foreach(var site in s.Sites)if(site.Kind=="Bridge" && Vector2.Distance(Q(x,z),Q(site.Position.x,site.Position.z))<135f){penalty=.4f;break;}
                    waterPenalty[n]=penalty;
                }
            }
            // Cached vertices reproduce the same 16 m triangle surface as the renderer/collider.
            // Sampling full edges must not repeatedly recompute all geological fields per route.
            float GroundVertex(int x,int z)
            {
                int n=z*surfaceNx+x;
                if(!surfaceKnown[n]){surfaceHeights[n]=WorldMacroTerrain.Height(sheet,sheet.BoundsMin.x+x*sheet.GridSpacing,sheet.BoundsMin.y+z*sheet.GridSpacing);surfaceKnown[n]=true;}
                return surfaceHeights[n];
            }
            float Ground(float x,float z)
            {
                float sx=(x-sheet.BoundsMin.x)/sheet.GridSpacing,sz=(z-sheet.BoundsMin.y)/sheet.GridSpacing;
                int ix=Mathf.FloorToInt(sx),iz=Mathf.FloorToInt(sz);
                if(ix<0||iz<0||ix+1>=surfaceNx||iz+1>=surfaceNz)return WorldMacroTerrain.SurfaceHeight(sheet,x,z);
                float u=sx-ix,v=sz-iz,a=GroundVertex(ix,iz),b=GroundVertex(ix+1,iz),c=GroundVertex(ix,iz+1);
                if(u+v<=1f)return a+(b-a)*u+(c-a)*v;
                float d=GroundVertex(ix+1,iz+1);return d+(c-d)*(1-u)+(b-d)*(1-v);
            }
            float GroundSlope(float x,float z)
            {
                float sx=(x-sheet.BoundsMin.x)/sheet.GridSpacing,sz=(z-sheet.BoundsMin.y)/sheet.GridSpacing;
                int ix=Mathf.FloorToInt(sx),iz=Mathf.FloorToInt(sz);
                if(ix<0||iz<0||ix+1>=surfaceNx||iz+1>=surfaceNz)return float.MaxValue;
                float a=GroundVertex(ix,iz),b=GroundVertex(ix+1,iz),c=GroundVertex(ix,iz+1),gx,gz;
                if(sx-ix+sz-iz<=1f){gx=(b-a)/sheet.GridSpacing;gz=(c-a)/sheet.GridSpacing;}
                else{float d=GroundVertex(ix+1,iz+1);gx=(d-c)/sheet.GridSpacing;gz=(d-b)/sheet.GridSpacing;}
                return Mathf.Sqrt(gx*gx+gz*gz);
            }
            SegmentGrade MeasureSegment(Vector3 a,Vector3 b)
            {
                float dx=b.x-a.x,dz=b.z-a.z,length=Mathf.Sqrt(dx*dx+dz*dz);
                if(length<.01f)return default;
                int steps=Mathf.Max(1,Mathf.CeilToInt(length/12f));float run=length/steps;
                var lateral=new Vector3(-dz/length,0,dx/length)*4f;
                var grade=new SegmentGrade();float previous=Ground(a.x,a.z);
                for(int i=0;i<=steps;i++)
                {
                    var p=Vector3.Lerp(a,b,i/(float)steps);float y=Ground(p.x,p.z);
                    float cross=Mathf.Abs(Ground(p.x+lateral.x,p.z+lateral.z)-Ground(p.x-lateral.x,p.z-lateral.z))/8f;
                    grade.CrossSlope=Mathf.Max(grade.CrossSlope,cross);
                    // The capsule can slide on a >45 degree face even when the path follows its
                    // contour. Use actual triangle gradients, not the 8 m averaged cross slope.
                    float surface=GroundSlope(p.x,p.z);
                    surface=Mathf.Max(surface,GroundSlope(p.x+lateral.x*.14f,p.z+lateral.z*.14f));
                    surface=Mathf.Max(surface,GroundSlope(p.x-lateral.x*.14f,p.z-lateral.z*.14f));
                    grade.PeakSurfaceSlope=Mathf.Max(grade.PeakSurfaceSlope,surface);
                    if(i>0){float slope=Mathf.Abs(y-previous)/run;grade.PeakSlope=Mathf.Max(grade.PeakSlope,slope);grade.MeanSlopeSquared+=slope*slope/steps;}
                    previous=y;
                }
                return grade;
            }
            SegmentGrade EdgeGrade(int a,int b)
            {
                long key=(long)Mathf.Min(a,b)*nodes.Length+Mathf.Max(a,b);
                if(!edgeGrades.TryGetValue(key,out var grade)){grade=MeasureSegment(nodes[a],nodes[b]);edgeGrades.Add(key,grade);}
                return grade;
            }
            static float GradeCost(SegmentGrade grade,bool carriage,bool returnTrail)
            {
                float peakLimit=carriage ? .28f : returnTrail ? .42f : .62f;
                float excess=Mathf.Max(0f,grade.PeakSlope-peakLimit);
                float crossExcess=Mathf.Max(0f,grade.CrossSlope-(carriage ? .25f : .48f));
                return grade.MeanSlopeSquared*(carriage?100f:returnTrail?52f:18f)
                    +excess*excess*(carriage?620f:returnTrail?360f:80f)
                    +grade.CrossSlope*grade.CrossSlope*(carriage?9f:returnTrail?3f:1f)
                    +crossExcess*crossExcess*(carriage?36f:returnTrail?10f:3f);
            }
            int Index(Vector3 p,bool carriage,bool returnTrail)
            {
                int x=Mathf.Clamp(Mathf.RoundToInt((p.x-sheet.BoundsMin.x)/Step),0,nx-1),z=Mathf.Clamp(Mathf.RoundToInt((p.z-sheet.BoundsMin.y)/Step),0,nz-1);
                int best=-1;float cost=float.MaxValue;
                for(int dz=-2;dz<=2;dz++)for(int dx=-2;dx<=2;dx++)
                {
                    int xx=x+dx,zz=z+dz;if(xx<0||zz<0||xx>=nx||zz>=nz)continue;int at=zz*nx+xx;
                    if(!inside[at]||wet[at])continue;
                    float d=Vector2.Distance(Q(p.x,p.z),Q(nodes[at].x,nodes[at].z));
                    bool crosses=false;foreach(var river in sheet.Rivers){for(int k=0;k+1<river.Points.Length;k++)if(Crosses(p,nodes[at],river.Points[k],river.Points[k+1],out _)){crosses=true;break;}if(crosses)break;}
                    if(crosses)continue;
                    var approach=MeasureSegment(p,nodes[at]);
                    if(approach.PeakSurfaceSlope>MaxWalkableSurfaceSlope||TouchesBridgeSupport(p,nodes[at]))continue;
                    float value=d*(1f+GradeCost(approach,carriage,returnTrail));
                    if(value<cost){cost=value;best=at;}
                }
                if(best<0)throw new InvalidOperationException("No dry same-bank macro navigation sample near "+p);
                return best;
            }
            public Sheet.RouteSpec Make(string id,string from,string to,bool carriage,string progression)
            {
                bool returnTrail=id.StartsWith("Return_",StringComparison.Ordinal);
                var fromSite=sheet.FindSite(from);var toSite=sheet.FindSite(to);
                Vector3 originalA=fromSite.Position,originalB=toSite.Position;
                bool fromBridge=fromSite.Kind=="Bridge",toBridge=toSite.Kind=="Bridge";
                Vector3 bankA=originalA,bankB=originalB;
                Vector3 a=fromBridge?BridgeApproach(fromSite,originalB,out bankA):originalA;
                Vector3 b=toBridge?BridgeApproach(toSite,originalA,out bankB):originalB;
                int start=Index(a,carriage,returnTrail),goal=Index(b,carriage,returnTrail),count=nodes.Length;
                var costs=new float[count];var came=new int[count];var closed=new bool[count];
                for(int i=0;i<count;i++){costs[i]=float.MaxValue;came[i]=-1;}
                var heap=new Heap();costs[start]=0;heap.Push(start,0);
                while(heap.Count>0)
                {
                    int at=heap.Pop();if(closed[at])continue;closed[at]=true;if(at==goal)break;
                    int x=at%nx,z=at/nx;
                    for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                    {
                        if(dx==0&&dz==0)continue;int xx=x+dx,zz=z+dz;if(xx<0||zz<0||xx>=nx||zz>=nz)continue;
                        int next=zz*nx+xx;if(closed[next]||!inside[next])continue;
                        if(wet[next])
                        {
                            if(waterPenalty[next]>=20f)continue;
                            if(fromBridge&&Vector2.Distance(Q(nodes[next].x,nodes[next].z),Q(originalA.x,originalA.z))<180f)continue;
                            if(toBridge&&Vector2.Distance(Q(nodes[next].x,nodes[next].z),Q(originalB.x,originalB.z))<180f)continue;
                        }
                        float horizontal=Step*(dx!=0&&dz!=0?1.41421356f:1f);
                        // Endpoint height averages miss narrow 40-degree faces inside a 64 m cell.
                        // Inspect longitudinal and cross slopes on the actual surface instead.
                        if(TouchesBridgeSupport(nodes[at],nodes[next]))continue;
                        var grade=EdgeGrade(at,next);
                        // Wet proxy edges around an intermediate bridge are replaced by its full
                        // supported axis below; dry trails cannot use an unwalkable side slope.
                        if(grade.PeakSurfaceSlope>MaxWalkableSurfaceSlope&&!wet[at]&&!wet[next])continue;
                        float slopeCost=GradeCost(grade,carriage,returnTrail);
                        float g=costs[at]+horizontal*(1+slopeCost+(waterPenalty[at]+waterPenalty[next])*.5f);
                        if(g>=costs[next])continue;costs[next]=g;came[next]=at;
                        float h=Vector2.Distance(Q(nodes[next].x,nodes[next].z),Q(b.x,b.z));heap.Push(next,g+h);
                    }
                }
                List<Vector3> reverse;
                if(goal!=start&&came[goal]<0)
                {
                    // A valid narrow bank can fall between 64 m samples. Refine only the failed
                    // connection's local corridor; keep all slope, water and bridge-side constraints.
                    reverse=FindFineCorridor(a,b,fromSite,toSite,carriage,returnTrail,32f);
                    float resolution=32f;
                    if(reverse==null){resolution=16f;reverse=FindFineCorridor(a,b,fromSite,toSite,carriage,returnTrail,resolution);}
                    if(reverse==null)throw new InvalidOperationException("Macro route disconnected at 64 m and bounded 32/16 m: "+id);
                    Debug.Log("WorldMacro refined narrow corridor at "+resolution+" m: "+id);
                }
                else
                {
                    reverse=new List<Vector3>();int cursor=goal;
                    while(cursor!=start){reverse.Add(nodes[cursor]);cursor=came[cursor];if(cursor<0)throw new InvalidOperationException(id);}
                    reverse.Add(nodes[start]);reverse.Reverse();
                }
                var points=new List<Vector3>{a};
                var firstNode=reverse[0];var lastNode=reverse[reverse.Count-1];
                if(Vector2.Distance(Q(a.x,a.z),Q(firstNode.x,firstNode.z))>.01f)points.Add(firstNode);
                for(int i=1;i+1<reverse.Count;i++)
                {
                    var prev=(reverse[i]-reverse[i-1]);prev.y=0;var next=(reverse[i+1]-reverse[i]);next.y=0;
                    if(Vector3.Angle(prev,next)>.1f || Vector3.Distance(points[points.Count-1],reverse[i])>220f)points.Add(reverse[i]);
                }
                if(Vector2.Distance(Q(points[points.Count-1].x,points[points.Count-1].z),Q(lastNode.x,lastNode.z))>.01f)points.Add(lastNode);
                if(Vector2.Distance(Q(points[points.Count-1].x,points[points.Count-1].z),Q(b.x,b.z))>.01f)points.Add(b);
                if(Vector2.Distance(Q(a.x,a.z),Q(b.x,b.z))<180f&&!CrossesWater(a,b))
                {
                    var direct=MeasureSegment(a,b);
                    if(direct.PeakSurfaceSlope<=MaxWalkableSurfaceSlope&&direct.PeakSlope<(carriage ? .18f : .32f)&&direct.CrossSlope<(carriage ? .22f : .4f)&&!TouchesBridgeSupport(a,b))points=new List<Vector3>{a,b};
                }
                SnapIntermediateBridges(points,fromSite,toSite);
                // Round only where the chord remains on equally usable terrain. Bridge controls
                // stay on their exact deck axis, and cliff-side corners cannot cut through the hill.
                for(int pass=0;pass<2;pass++)
                {
                    var rounded=new List<Vector3>{points[0]};
                    for(int i=1;i+1<points.Count;i++)
                    {
                        var p=points[i];bool changed=false;
                        if(!NearBridgeControls(p))for(float fraction=.25f;fraction>=.06f;fraction*=.5f)
                        {
                            var entry=Vector3.Lerp(p,points[i-1],fraction);var leave=Vector3.Lerp(p,points[i+1],fraction);
                            var beforeA=MeasureSegment(entry,p);var beforeB=MeasureSegment(p,leave);var after=MeasureSegment(entry,leave);
                            float peak=Mathf.Max(beforeA.PeakSlope,beforeB.PeakSlope),cross=Mathf.Max(beforeA.CrossSlope,beforeB.CrossSlope);
                            if(after.PeakSurfaceSlope>MaxWalkableSurfaceSlope||after.PeakSlope>Mathf.Max(carriage ? .25f : returnTrail ? .38f : .55f,peak+.025f)||after.CrossSlope>cross+.04f||CrossesWater(entry,leave)||TouchesBridgeSupport(entry,leave))continue;
                            rounded.Add(entry);rounded.Add(leave);changed=true;break;
                        }
                        if(!changed)rounded.Add(p);
                    }
                    rounded.Add(points[points.Count-1]);points=rounded;
                }
                if(fromBridge){points.Insert(0,bankA);points.Insert(0,originalA);}
                if(toBridge){points.Add(bankB);points.Add(originalB);}
                var sampled=new List<Vector3>();
                for(int i=0;i+1<points.Count;i++)
                {
                    float distance=Vector2.Distance(Q(points[i].x,points[i].z),Q(points[i+1].x,points[i+1].z));
                    int steps=Mathf.Max(1,Mathf.CeilToInt(distance/16f));
                    for(int j=0;j<steps;j++)sampled.Add(Project(Vector3.Lerp(points[i],points[i+1],j/(float)steps)));
                }
                sampled.Add(Project(originalB));
                return new Sheet.RouteSpec{Id=id,From=from,To=to,Carriage=carriage,Width=carriage?8f:2.2f,Points=sampled.ToArray(),Progression=progression};
            }
            List<Vector3> FindFineCorridor(Vector3 a,Vector3 b,Sheet.SiteSpec fromSite,Sheet.SiteSpec toSite,bool carriage,bool returnTrail,float step)
            {
                const float padding=640f;
                float loX=Mathf.Min(a.x,b.x),hiX=Mathf.Max(a.x,b.x),loZ=Mathf.Min(a.z,b.z),hiZ=Mathf.Max(a.z,b.z);
                if((fromSite.Realm==RealmId.Jeokro&&toSite.Id=="CapitalSouthBridge")||(toSite.Realm==RealmId.Jeokro&&fromSite.Id=="CapitalSouthBridge"))
                {
                    // The natural low southern return follows this tributary's basin well west
                    // of both endpoints. Include its geography as search space, not a waypoint.
                    foreach(var river in sheet.Rivers)if(river.Id=="SouthernCreek")foreach(var p in river.Points)
                    {loX=Mathf.Min(loX,p.x);hiX=Mathf.Max(hiX,p.x);loZ=Mathf.Min(loZ,p.z);hiZ=Mathf.Max(hiZ,p.z);}
                    var confluence=sheet.FindSite("SouthBridge").Position;
                    loX=Mathf.Min(loX,confluence.x);hiX=Mathf.Max(hiX,confluence.x);loZ=Mathf.Min(loZ,confluence.z);hiZ=Mathf.Max(hiZ,confluence.z);
                }
                float minX=sheet.BoundsMin.x+Mathf.Floor((Mathf.Max(sheet.BoundsMin.x,loX-padding)-sheet.BoundsMin.x)/step)*step;
                float minZ=sheet.BoundsMin.y+Mathf.Floor((Mathf.Max(sheet.BoundsMin.y,loZ-padding)-sheet.BoundsMin.y)/step)*step;
                float maxX=Mathf.Min(sheet.BoundsMax.x,hiX+padding),maxZ=Mathf.Min(sheet.BoundsMax.y,hiZ+padding);
                int width=Mathf.FloorToInt((maxX-minX)/step)+1,height=Mathf.FloorToInt((maxZ-minZ)/step)+1,count=width*height;
                var grid=new Vector3[count];var valid=new bool[count];var inWater=new bool[count];var waterCost=new float[count];
                for(int z=0;z<height;z++)for(int x=0;x<width;x++)
                {
                    int n=z*width+x;var p=P(minX+x*step,minZ+z*step);grid[n]=p;
                    if(!WorldMacroTerrain.Contains(sheet,p.x,p.z)||TouchesBridgeSupport(p,p))continue;
                    valid[n]=true;grid[n].y=Ground(p.x,p.z);
                    foreach(var river in sheet.Rivers)for(int k=0;k+1<river.Points.Length;k++)
                    {
                        float t,d=WorldMacroTerrain.SegmentDistance(p.x,p.z,river.Points[k],river.Points[k+1],out t);
                        if(d<river.Width*.5f+step*.72f)inWater[n]=true;
                    }
                    if(!inWater[n])continue;
                    bool proxy=false;
                    foreach(var site in sheet.Sites)if(site.Kind=="Bridge"&&site!=fromSite&&site!=toSite&&Vector2.Distance(Q(p.x,p.z),Q(site.Position.x,site.Position.z))<135f){proxy=true;break;}
                    if(!proxy)valid[n]=false;else waterCost[n]=.4f;
                }
                int Pick(Vector3 p)
                {
                    int cx=Mathf.RoundToInt((p.x-minX)/step),cz=Mathf.RoundToInt((p.z-minZ)/step),best=-1;float bestCost=float.MaxValue;
                    for(int dz=-3;dz<=3;dz++)for(int dx=-3;dx<=3;dx++)
                    {
                        int x=cx+dx,z=cz+dz;if(x<0||z<0||x>=width||z>=height)continue;int n=z*width+x;
                        if(!valid[n]||inWater[n]||CrossesWater(p,grid[n])||TouchesBridgeSupport(p,grid[n]))continue;
                        var grade=MeasureSegment(p,grid[n]);if(grade.PeakSurfaceSlope>MaxWalkableSurfaceSlope)continue;
                        float cost=Vector2.Distance(Q(p.x,p.z),Q(grid[n].x,grid[n].z))*(1+GradeCost(grade,carriage,returnTrail));
                        if(cost<bestCost){best=n;bestCost=cost;}
                    }
                    return best;
                }
                int start=Pick(a),goal=Pick(b);if(start<0||goal<0)return null;
                var costTo=new float[count];var came=new int[count];var closed=new bool[count];
                for(int i=0;i<count;i++){costTo[i]=float.MaxValue;came[i]=-1;}
                var open=new Heap();costTo[start]=0;open.Push(start,0);
                while(open.Count>0)
                {
                    int at=open.Pop();if(closed[at])continue;closed[at]=true;if(at==goal)break;
                    int x=at%width,z=at/width;
                    for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                    {
                        if(dx==0&&dz==0)continue;int xx=x+dx,zz=z+dz;if(xx<0||zz<0||xx>=width||zz>=height)continue;
                        int next=zz*width+xx;if(!valid[next]||closed[next]||TouchesBridgeSupport(grid[at],grid[next]))continue;
                        var grade=MeasureSegment(grid[at],grid[next]);
                        if(grade.PeakSurfaceSlope>MaxWalkableSurfaceSlope&&!inWater[at]&&!inWater[next])continue;
                        float run=step*(dx!=0&&dz!=0?1.41421356f:1f);
                        float cost=costTo[at]+run*(1+GradeCost(grade,carriage,returnTrail)+(waterCost[at]+waterCost[next])*.5f);
                        if(cost>=costTo[next])continue;costTo[next]=cost;came[next]=at;
                        open.Push(next,cost+Vector2.Distance(Q(grid[next].x,grid[next].z),Q(b.x,b.z)));
                    }
                }
                if(start!=goal&&came[goal]<0)return null;
                var path=new List<Vector3>();int cursor=goal;
                while(cursor!=start){path.Add(grid[cursor]);cursor=came[cursor];if(cursor<0)return null;}
                path.Add(grid[start]);path.Reverse();return path;
            }
            Vector3 Project(Vector3 p)
            {
                p.y=Ground(p.x,p.z);
                foreach(var plan in bridgePlans)p.y=plan.SurfaceHeight(p,p.y);
                return p;
            }
            bool NearBridgeControls(Vector3 p)
            {
                foreach(var site in sheet.Sites)if(site.Kind=="Bridge"&&Vector2.Distance(Q(p.x,p.z),Q(site.Position.x,site.Position.z))<180f)return true;
                return false;
            }
            bool TouchesBridgeSupport(Vector3 a,Vector3 b)
            {
                // General navigation must not step onto a raised deck/apron from its side. Wet
                // proxy crossings can pass beside the footprint and are snapped to the full axis.
                foreach(var plan in bridgePlans)
                {
                    var da=a-plan.Center;var db=b-plan.Center;
                    float ax=Vector3.Dot(da,plan.Axis),bx=Vector3.Dot(db,plan.Axis);
                    float az=da.x*plan.Axis.z-da.z*plan.Axis.x,bz=db.x*plan.Axis.z-db.z*plan.Axis.x;
                    float along=plan.DeckHalfLength+WorldMacroBridgeGeometry.Plan.ApronLength+2f;
                    float across=WorldMacroBridgeGeometry.Plan.Width*.5f+2f;
                    float enter=0f,leave=1f;
                    if(ClipSlab(ax,bx,-along,along,ref enter,ref leave)&&ClipSlab(az,bz,-across,across,ref enter,ref leave))return true;
                }
                return false;
            }
            static bool ClipSlab(float a,float b,float low,float high,ref float enter,ref float leave)
            {
                float d=b-a;if(Mathf.Abs(d)<.0001f)return a>=low&&a<=high;
                float t0=(low-a)/d,t1=(high-a)/d;if(t0>t1){float t=t0;t0=t1;t1=t;}
                enter=Mathf.Max(enter,t0);leave=Mathf.Min(leave,t1);return enter<=leave;
            }
            bool CrossesWater(Vector3 a,Vector3 b)
            {
                foreach(var river in sheet.Rivers)for(int i=0;i+1<river.Points.Length;i++)if(Crosses(a,b,river.Points[i],river.Points[i+1],out _))return true;
                return false;
            }
            void SnapIntermediateBridges(List<Vector3> points,Sheet.SiteSpec from,Sheet.SiteSpec to)
            {
                foreach(var bridge in sheet.Sites)
                {
                    if(bridge.Kind!="Bridge"||bridge==from||bridge==to)continue;
                    int crossing=-1;
                    for(int i=0;i+1<points.Count&&crossing<0;i++)foreach(var river in sheet.Rivers)
                    {
                        for(int k=0;k+1<river.Points.Length;k++)if(Crosses(points[i],points[i+1],river.Points[k],river.Points[k+1],out var p)
                            &&Vector2.Distance(Q(p.x,p.z),Q(bridge.Position.x,bridge.Position.z))<180f){crossing=i;break;}
                        if(crossing>=0)break;
                    }
                    if(crossing<0)continue;
                    int start=crossing,end=crossing+1;
                    while(start>0&&Vector2.Distance(Q(points[start].x,points[start].z),Q(bridge.Position.x,bridge.Position.z))<180f)start--;
                    while(end+1<points.Count&&Vector2.Distance(Q(points[end].x,points[end].z),Q(bridge.Position.x,bridge.Position.z))<180f)end++;
                    float width;var axis=BridgeAxis(bridge,out width);
                    float sideA=Mathf.Sign(Vector3.Dot(points[start]-bridge.Position,axis)),sideB=Mathf.Sign(Vector3.Dot(points[end]-bridge.Position,axis));
                    if(sideA==sideB)continue;
                    float stand=width*.5f+83f,bank=width*.5f+45f;
                    var controls=new[]{bridge.Position+axis*sideA*stand,bridge.Position+axis*sideA*bank,bridge.Position,
                        bridge.Position+axis*sideB*bank,bridge.Position+axis*sideB*stand};
                    points.RemoveRange(start+1,end-start-1);points.InsertRange(start+1,controls);
                }
            }
            Vector3 BridgeAxis(Sheet.SiteSpec site,out float width)
            {
                float nearest=float.MaxValue;width=40f;var axis=Vector3.right;
                foreach(var river in sheet.Rivers)for(int i=0;i+1<river.Points.Length;i++)
                {
                    float t,d=WorldMacroTerrain.SegmentDistance(site.Position.x,site.Position.z,river.Points[i],river.Points[i+1],out t);
                    if(d>=nearest)continue;nearest=d;width=river.Width;var tangent=river.Points[i+1]-river.Points[i];axis=new Vector3(-tangent.z,0,tangent.x).normalized;
                }
                return axis;
            }
            Vector3 BridgeApproach(Sheet.SiteSpec site,Vector3 other,out Vector3 bank)
            {
                float width;var axis=BridgeAxis(site,out width);
                if(Vector3.Dot(other-site.Position,axis)<0)axis=-axis;
                bank=site.Position+axis*(width*.5f+45f);
                // Stop the forced perpendicular approach on the natural alluvial bank. The
                // remaining climb is planned freely, not anchored into the steeper valley wall.
                return site.Position+axis*(width*.5f+83f);
            }
            sealed class Heap
            {
                readonly List<int> ids=new List<int>();readonly List<float> values=new List<float>();public int Count=>ids.Count;
                public void Push(int id,float value){int i=ids.Count;ids.Add(id);values.Add(value);while(i>0){int p=(i-1)/2;if(values[p]<=value)break;ids[i]=ids[p];values[i]=values[p];i=p;}ids[i]=id;values[i]=value;}
                public int Pop(){int result=ids[0],last=ids.Count-1,id=ids[last];float value=values[last];ids.RemoveAt(last);values.RemoveAt(last);if(last==0)return result;int i=0;while(i*2+1<last){int c=i*2+1;if(c+1<last&&values[c+1]<values[c])c++;if(values[c]>=value)break;ids[i]=ids[c];values[i]=values[c];i=c;}ids[i]=id;values[i]=value;return result;}
            }
        }
    }
}
