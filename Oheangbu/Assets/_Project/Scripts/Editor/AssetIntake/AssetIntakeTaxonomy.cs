using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Oheangbu.EditorTools
{
    /// <summary>
    /// Filename and source-folder triage for SPEC-ASSET-INTAKE. These rules never
    /// establish visual verification, historical suitability, or placement approval.
    /// The catalog records rendered evidence and any visual correction separately.
    /// </summary>
    public static class AssetIntakeTaxonomy
    {
        private sealed class Rule
        {
            public readonly string Pack;
            public readonly Regex Name;
            public readonly string Category;
            public readonly string Label;

            public Rule(string pack, string pattern, string category, string label)
            {
                Pack = pack;
                Name = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                Category = category;
                Label = label;
            }
        }

        // Specific kit conventions precede generic words. In particular, a fortress
        // "Ridge" is a roof ridge, and cave "Wall/Floor" are geological surfaces.
        private static readonly Rule[] Rules =
        {
            new Rule("KTinteractiveProp", @"^PlayerCapsule$", "데모/외부 플레이어", "외부 데모 컨트롤러"),
            new Rule("KTinteractiveProp", @"^(BesideTable|Tabletop)(?:[ _]|$)", "생활 소품/가구", "탁자·소반"),
            new Rule("KTinteractiveProp", @"^(Closet|HalfChest|ShelfCabinet|StorageCabinets|Box)(?:[ _]|$)", "생활 소품/수납", "장·반닫이·수납함"),
            new Rule("KTinteractiveProp", @"^Mirror(?:[ _]|$)", "생활 소품/거울", "거울"),

            new Rule("BillemotdonggulLavaTubePack", @"^SM_ArtifactFragments", "유물/파편", "동굴 출토 유물 파편"),
            new Rule("BillemotdonggulLavaTubePack", @"^SM_SootMarks", "표면 장식/흔적", "그을음 흔적"),
            new Rule("BillemotdonggulLavaTubePack", @"^SM_(Ceiling|Cupola|Lava_stalactite)", "암석·지형/동굴 천장", "용암동굴 천장·돌출부"),
            new Rule("BillemotdonggulLavaTubePack", @"^SM_Wall", "암석·지형/동굴 벽", "용암동굴 벽면"),
            new Rule("BillemotdonggulLavaTubePack", @"^SM_(Floor|Lava_tongue)", "암석·지형/동굴 바닥", "용암동굴 바닥·용암 흔적"),
            new Rule("BillemotdonggulLavaTubePack", @"^SM_Rockfall", "암석·지형/낙석", "낙석·붕괴 암괴"),
            new Rule("YongmeoriCoast", @"^SM_YMC_(Cliff|RockShelf)", "암석·지형/해안 절벽", "층리 절벽·암반"),
            new Rule("YongmeoriCoast", @"^SM_YMC_Rocks", "암석·지형/암석", "해안 암석"),

            new Rule("JejumokGwana", @"^(Oedaemun|Chungdaemun)$", "건축 완성물/문루·대문", "목관아 문루·대문"),
            new Rule("JejumokGwana", @"^(Gwandeokjong|Gyullimdang|Honhwagak|MangGyeongru|Wooryeondang|Yeonhuigak|Yeonjuihyeopdang)$", "건축 완성물/관아·누정", "목관아 전각·누정"),
            new Rule("JejumokGwana", @"^Prop_House", "건축 완성물/집", "소형 집"),
            new Rule("JejumokGwana", @"^Base_", "건축 부재/기단·초석", "건물 기단"),
            new Rule("JejumokGwana", @"^(Corner[A-Z]|Stick[A-Z])$", "건축 부재/접합 부재", "모서리·막대 부재"),
            new Rule("JejumokGwana", @"^Gwandeokjong(Illust|Pattern)", "표면 장식/회화·단청", "관덕정 회화·문양"),

            new Rule("HwaseongForteressGate", @"^SM_(B_GateHouse|GateGuardPost)(?:_\d+)?$", "건축 완성물/문루·대문", "성문 누각·수비 시설"),
            new Rule("HwaseongForteressGate", @"^SM_(CW|B_Bastion|Bastion|Fortification)(?:_\d+)?$", "건축 조립체/성곽", "성곽 조립체"),
            new Rule("HwaseongForteressGate", @"^SM_(?:.*_)?(?:Ridge|RoofStatue|Gargoyle)", "건축 부재/용마루·지붕 장식", "용마루·잡상"),
            new Rule("HwaseongForteressGate", @"^SM_.*(Parapet|BarbicanWall|FortificationWall|RammedEarth|BarbicanArch|FortificationArch|G_Arch|B_SmallArch)", "건축 부재/성벽·여장", "성벽·여장·아치"),
            new Rule("HwaseongForteressGate", @"^SM_.*(DoorBase|DoorStoper)", "건축 부재/문 부속", "성문 받침·멈춤쇠"),
            new Rule("HwaseongForteressGate", @"^SM_BarbicanRoofboard", "건축 부재/지붕", "옹성 지붕판"),
            new Rule("HwaseongForteressGate", @"^SM_BarbicanFloor", "건축 부재/바닥·포장", "옹성 바닥"),
            new Rule("HwaseongForteressGate", @"^SM_BarbicanStair", "건축 부재/계단·디딤돌", "옹성 계단"),
            new Rule("HwaseongForteressGate", @"^SM_CW_Stone$", "건축 부재/성벽·여장", "성곽 석재 부재"),
            new Rule("HwaseongForteressGate", @"^SM_Cannon", "도구·무기/화포", "화포·화포 부품"),
            new Rule("HwaseongForteressGate", @"^SM_Note_", "표면 장식/안내판", "성문 안내판"),

            new Rule("HwaseongHaenggung", @"^SM_(?:Hongsalmun|Jungyangmun|Jwaikmun|Naesanmun|Oisamun|Smallgate|byeolju_SmallGate)(?:[_\d]|$)", "건축 완성물/문루·대문", "행궁 대문·홍살문"),
            new Rule("HwaseongHaenggung", @"^SM_(?:Bijangcheong|Bokgunyeong|Boknaedang|Bongsudang|Byeolchu|byeolju|Gyeongryugwan|Jeonsacheong|Jibsacheong|Mirohanjeong|Naeposa|Naknamhyeon|Namgunyeong|Oijeong|Oijeongriso|Punghuadang|Seoricheong|Sinpungru|Uhagwan|Uhwagan|Unhwagak|Yuyeotaek)(?:_|\d|$)", "건축 완성물/관아·누정", "행궁 전각·누정"),
            new Rule("HwaseongHaenggung", @"^SM_F_Intceiling", "건축 부재/천장", "실내 천장"),
            new Rule("HwaseongHaenggung", @"^SM_F_", "건축 부재/바닥·포장", "바닥·포장면"),
            new Rule("HwaseongHaenggung", @"^SM_W_Foundation", "건축 부재/기단·초석", "건물 기단"),
            new Rule("HwaseongHaenggung", @"^SM_W_", "건축 부재/벽·담장", "건물 벽·담장"),
            new Rule("HwaseongHaenggung", @"^SM_(D_|WI_)", "건축 부재/문·창호", "문·창호"),
            new Rule("HwaseongHaenggung", @"^SM_P_", "건축 부재/기둥", "기둥"),
            new Rule("HwaseongHaenggung", @"^SM_S_(Circle|Octagonal|Square)", "건축 부재/기단·초석", "기둥 초석"),
            new Rule("HwaseongHaenggung", @"^SM_R_(?:Sign|Painting)", "표면 장식/현판·회화", "현판·회화"),
            new Rule("HwaseongHaenggung", @"^SM_R_Curtain", "생활 소품/직물", "휘장"),
            new Rule("HwaseongHaenggung", @"^SM_R_Lantern", "생활 소품/등기구", "등롱"),
            new Rule("HwaseongHaenggung", @"^SM_R_.*(?:Beam|DancheongSupport)", "건축 부재/보·도리·서까래", "보·지붕 지지 부재"),
            new Rule("HwaseongHaenggung", @"^SM_R_Dancheong", "건축 부재/공포·단청", "단청 부재"),
            new Rule("HwaseongHaenggung", @"^SM_R_Ceiling", "건축 부재/천장", "천장"),
            new Rule("HwaseongHaenggung", @"^SM_R_(?:GateDoor|GateWooden)", "건축 부재/문·창호", "문 부재"),
            new Rule("HwaseongHaenggung", @"^SM_R_", "건축 조립체/지붕", "지붕·상부 조립체"),
            new Rule("HwaseongHaenggung", @"^SM_M_(?:GreenLog|Redlog|WoodLog)", "건축 부재/목재", "통나무·목재 부재"),
            new Rule("HwaseongHaenggung", @"^SM_M_Fish", "생활 소품/장식", "물고기 모양 장식"),
            new Rule("HwaseongHaenggung", @"^SM_SkySphere", "데모/하늘 메시", "배경 하늘 메시"),

            new Rule("SeyeonjeongPavilion", @"^SM_(?:YongMaru|NerimMaru|ChunyeoMaru)", "건축 부재/용마루·지붕 장식", "용마루·내림마루·추녀마루"),
            new Rule("SeyeonjeongPavilion", @"^SM_Panseokbo", "건축 조립체/수로·보", "판석보"),
            new Rule("SeyeonjeongPavilion", @"^SM_StoneBridge", "건축 조립체/다리", "돌다리"),
            new Rule("SeyeonjeongPavilion", @"^SM_Ground", "암석·지형/지면", "정원 지면"),
            new Rule("SeyeonjeongPavilion", @"^SM_(?:Duckweed|NymphaeaTetragona)", "식생/수생 식물", "수면 잎·수생 식물"),
            new Rule("SeyeonjeongPavilion", @"^SM_ParthenocissusTricuspidata", "식생/덩굴", "담쟁이 계열 덩굴"),
            new Rule("SeyeonjeongPavilion", @"^SM_(?:BrassicaNapus|Carex|Deparia|PhragmitesAustralis|Henonis)", "식생/풀·초본", "초본·갈대·대나무류"),
            new Rule("SeyeonjeongPavilion", @"^SM_(?:HydrangeaMacrophylla|Ilexcrenata|Viburnumodoratissimum)", "식생/관목", "정원 관목"),
            new Rule("SeyeonjeongPavilion", @"^SM_(?:AphanantheAspera|Camelliajaponica|Dendropanaxtrifidus|Lagerstroemiaindica|MeliaAzedarach|Pinus|Quercusacutissima|Salixpierotii|UlmusDavidiana|thunbergii)", "식생/수목", "수목·계절 변형"),
            new Rule("SeyeonjeongPavilion", @"^SM_TreeRoot", "식생/뿌리", "노출 나무뿌리"),
            new Rule("SeyeonjeongPavilion", @"^SM_BaseStone", "건축 부재/기단·초석", "기둥 초석"),

            new Rule("Korea_TreasureProps", @"^SM_\d+_(?:Statue|Clay_Statue|Sculpt|An_Earthenware_Doll|A_Celadon_Doll)", "유물/불상·조각", "불상·도용·조각"),
            new Rule("Korea_TreasureProps", @"^SM_\d+_(?:Saligu|Bronze_Ritual_Object|Incense_Burner|Buddhist_Temple_Bell)", "유물/의례·불교 공예", "사리구·향로·의례 공예"),
            new Rule("Korea_TreasureProps", @"^SM_\d+_(?:Helmet|Golden_Crown|Gilt_Bronze_Shoes)", "유물/복식·장신구", "투구·관·금동 신발"),
            new Rule("Korea_TreasureProps", @"^SM_\d+_(?:Stamp|Stationery|Wooden_Scroll|Yeonjeong)", "유물/문방·인장", "문방구·인장·기록물"),
            new Rule("Korea_TreasureProps", @"^SM_\d+_Sundial", "유물/관측 기구", "해시계"),
            new Rule("Korea_TreasureProps", @"^SM_\d+_(?:Pillow|Queen_Pillow|Storage_Box)", "유물/생활 공예", "베개·보관함"),
            new Rule("Korea_TreasureProps", @"^SM_\d+_(?:A_Ceramic_Bottle|Bottle|Bowl|Cup|Silver_Cup|Pot|Vase|Jar|Kettle)", "유물/그릇·용기", "병·항아리·잔·주전자"),

            new Rule("KoreanTraditionalFestival", @"^SM_Cow(?:_|$)", "생물/가축", "소 모델"),
            new Rule("KoreanTraditionalFestival", @"^SM_Cloth", "복식/의복·모자", "전통 의복·구성 부품"),
            new Rule("KoreanTraditionalFestival", @"^SM_(?:BanggatHat|StrawShoes|UjangRaincoat)", "복식/의복·모자", "삿갓·짚신·우장"),
            new Rule("KoreanTraditionalFestival", @"^SM_.*(?:Saddle|Muzzle)", "도구·무기/농업·운반", "소 길마·안장·부리망"),
            new Rule("KoreanTraditionalFestival", @"^SM_(?:RiceSeedlings|SheafOfRice|Haystack|Scarecrow)", "생활 소품/농경", "모·볏단·건초·허수아비"),
            new Rule("KoreanTraditionalFestival", @"^SM_(?:Rice|SackOfRice|FoodMesh)", "생활 소품/식량", "쌀·식량"),
            new Rule("KoreanTraditionalFestival", @"^SM_(?:.*(?:Shovel|Rake|Plow|Thresher|Harrow|Broom|Mop)|Flail|MeHammer|Pestle|Mortar|Sickle|Winnower|ThreshingFan|PullCar|FrameCarrier|YongduleLadle)", "도구·무기/농업·운반", "농기구·운반구"),
            new Rule("KoreanTraditionalFestival", @"^SM_.*(?:Rope|Mat|Cushion|Pedestal)", "생활 소품/짚·직물", "밧줄·멍석·깔개"),
            new Rule("KoreanTraditionalFestival", @"^SM_.*(?:Bag|Basket|Hamper|Bucket|Tray|Ladle|Scale|Fan)", "생활 소품/생활 도구", "바구니·용기·생활 도구"),

            new Rule(null, @"^(?:SM_)?(?:YMC_)?(?:Grass|Bush|Hedgerows)", "식생/풀·관목", "풀·관목"),
            new Rule(null, @"^(?:SM_)?(?:Zelkova|FineTree|WillowTree)", "식생/수목", "수목"),
            new Rule(null, @"^(?:SM_)?(?:W_Mountain|Landscape)", "암석·지형/지형·산", "지형·산 메시"),
            new Rule(null, @"^(?:SM_)?(?:Rock(?:_|$)|Stone(?:_|$)|Amseog)", "암석·지형/암석", "자연 암석"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Lantern|Candlestick|Torch|Lamps)", "생활 소품/등기구", "등롱·촛대·횃불"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Chimney|Brickchimney)", "건축 부재/굴뚝", "굴뚝"),
            new Rule(null, @"^(?:SM_)?(?:Bronze_burner|\d+_Stove)", "생활 소품/화로", "화로"),
            new Rule(null, @"^(?:SM_)?(?:\d+_)?Brick", "건축 부재/벽돌", "벽돌·전돌"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Signboard|Signages|NameBoard)", "표면 장식/현판·회화", "현판·이름판"),
            new Rule(null, @"^(?:SM_)?(?:.*Flag|FlagPost)(?:[_\d]|$)", "생활 소품/깃발", "깃발·깃대·부속"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Stair|WoodStair|StoneStair|RedStair|RoyalStair|Stepladder|SteppingStone)", "건축 부재/계단·디딤돌", "계단·디딤돌"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Railing|Handrail|Treefence)", "건축 부재/난간·울타리", "난간·울타리"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Stonewall|StraightStronewall|OuterWall|WallSet)", "건축 부재/벽·담장", "벽·돌담"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Pillar|RedPillar|StonePillar|Column)", "건축 부재/기둥", "기둥·기둥 부속"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Beam|Crossbeam|FlatLintel|Lintel|Purlin|Rafter|HipRafter|Heartwood|Roof_Frame|Roof_Support)", "건축 부재/보·도리·서까래", "보·도리·서까래"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:BracketSupport|ComplexBracket|Hwaban|Dancheong)", "건축 부재/공포·단청", "공포·화반·단청 부재"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Roof|RoofBoard)", "건축 부재/지붕", "지붕·지붕판"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Floor|Maru)(?:[_\d]|$)", "건축 부재/바닥·포장", "바닥·마루"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?Ceiling", "건축 부재/천장", "천장"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Door|SideDoor|Window|Frame|Lattice|hangingdoor|MetalDoor|ArchDoor)", "건축 부재/문·창호", "문·창호·틀"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?(?:Pedestal|Stonewaterway)", "건축 부재/기단·수로", "받침·수로"),
            new Rule(null, @"^(?:SM_)?(?:.*_)?WoodenBox", "생활 소품/수납", "나무 상자"),
            new Rule(null, @"^(?:SM_)?Well$", "생활 시설/우물", "우물"),
            new Rule(null, @"^(?:SM_)?Tombstone$", "유물/비석", "비석"),
        };

        private static Rule Match(string path)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(normalized);
            foreach (Rule rule in Rules)
            {
                if (rule.Pack != null && normalized.IndexOf("/" + rule.Pack + "/", StringComparison.OrdinalIgnoreCase) < 0
                    && !normalized.StartsWith(rule.Pack + "/", StringComparison.OrdinalIgnoreCase)) continue;
                if (rule.Name.IsMatch(name)) return rule;
            }
            return null;
        }

        public static string CategoryFor(string path) => Match(path)?.Category ?? "미분류/육안 확인";

        public static string LabelFor(string path)
        {
            string name = Path.GetFileNameWithoutExtension((path ?? string.Empty).Replace('\\', '/'));
            Rule rule = Match(path);
            return rule == null ? name + " · 육안 확인 필요" : rule.Label + " · " + name;
        }

        public static string ConfidenceFor(string path)
        {
            Rule rule = Match(path);
            if (rule == null || rule.Category == "건축 부재/접합 부재") return "육안 확인";
            return "이름·폴더 기반 추정";
        }

        public static string PlacementFor(string category)
        {
            string value = category ?? string.Empty;
            if (value.StartsWith("데모/", StringComparison.Ordinal)) return "배치 제외: 프로젝트 플레이어·하늘 설정을 대체하지 않음";
            if (value.StartsWith("미분류/", StringComparison.Ordinal)) return "보류: 추가 각도·형상 확인 후 역할 결정";
            if (value.Contains("동굴") || value.Contains("낙석")) return "청림 폐광: 접합부·접지·보행 공간 및 충돌 확인";
            if (value.Contains("해안 절벽")) return "절벽·강변 후보: 해안 층리의 지형 적합성과 스케일 확인";
            if (value.StartsWith("암석·지형/", StringComparison.Ordinal)) return "청림 산길·원경: 실루엣·노멀·LOD·먹 농도 조정";
            if (value.Contains("수생") || value.Contains("수로·보") || value.Contains("다리")) return "연못·하천·누정 주변: 수위·접지·동선 확인";
            if (value.StartsWith("식생/", StringComparison.Ordinal)) return "산길·마을·정원: 수종·계절·잎 알파·양면·LOD 확인";
            if (value.Contains("성곽") || value.Contains("성벽") || value.Contains("화포")) return "성곽·관문 후보: 군사 시설의 시대·지역·규모 확인";
            if (value.StartsWith("건축 완성물/", StringComparison.Ordinal)) return "관아·역참·누정 후보: 실제 건물 용도 확인 후 선택; 주막 대체는 별도 판단";
            if (value.StartsWith("건축", StringComparison.Ordinal)) return "주막·역참·마을 조립 후보: 접합 규격·피벗·치수·재질 통일";
            if (value.Contains("등기구")) return "주막·안식 지점: 원본 복구 후 의미광·발광 상한 별도 적용";
            if (value.StartsWith("유물/", StringComparison.Ordinal)) return "사찰·유적·서사 공간 후보: 시대·용도·보상 역할 확인";
            if (value.StartsWith("도구·무기/", StringComparison.Ordinal) || value.Contains("농경") || value.Contains("식량")) return "농가·마을·창고 후보: 작업 흔적과 운반 동선에 맞춰 배치";
            if (value.StartsWith("복식/", StringComparison.Ordinal) || value.StartsWith("생물/", StringComparison.Ordinal)) return "배치 보류: 정적 장식/리깅/애니메이션 지원과 시대 적합성 확인";
            if (value.StartsWith("표면 장식/", StringComparison.Ordinal)) return "건물·유적 표면: 실제 문구·문양·데칼 여부 및 면 겹침 확인";
            return "주막·역참·마을 후보: 실제 크기·생활 용도·시대 및 수묵 재질 확인";
        }
    }
}
